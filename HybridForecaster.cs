using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.ML;
using Microsoft.ML.Data;

namespace ArpsForecasting
{
    public class ForecastConfig
    {
        public double? InitialRate { get; set; }
        public double? InitialDecline { get; set; }
        public double? BFactor { get; set; }
        public double? TerminalDecline { get; set; }
        public int NumLags { get; set; } = 3;
        public int MinTrainingIndex { get; set; } = 10;
    }

    public class HybridForecaster
    {
        private readonly MLContext mlContext;
        private readonly ITransformer model;
        private readonly PredictionEngine<HybridData, HybridPrediction> predictionEngine;
        private readonly SSE arpsForecaster;
        private readonly ForecastConfig config;

        public HybridForecaster(List<double> time, List<double> production, ForecastConfig config)
        {
            this.config = config ?? throw new ArgumentNullException(nameof(config));
            if (time.Count < config.MinTrainingIndex + config.NumLags)
                throw new ArgumentException("Insufficient data for training.");

            mlContext = new MLContext();
            arpsForecaster = new SSE();

            double qiGuess = config.InitialRate ?? production.Max();
            double diGuess = config.InitialDecline ?? EstimateInitialDecline(time, production);
            double bGuess = config.BFactor ?? 0.5;
            double dMin = config.TerminalDecline ?? 0.07 / 365;

            arpsForecaster.FitHyperbolicToExponential(time, production, qiGuess, diGuess, bGuess, dMin);

            var data = PrepareTrainingData(time, production);
            var dataView = mlContext.Data.LoadFromEnumerable(data);
            var pipeline = BuildPipeline();
            model = pipeline.Fit(dataView);
            predictionEngine = mlContext.Model.CreatePredictionEngine<HybridData, HybridPrediction>(model);
        }

        private double EstimateInitialDecline(List<double> time, List<double> production)
        {
            if (time.Count < 2) return 0.01;
            double deltaT = time[1] - time[0];
            double deltaP = production[0] - production[1];
            if (deltaP <= 0 || production[0] == 0) return 0.01;
            return Math.Min(deltaP / (production[0] * deltaT), 1.0);
        }

        private List<HybridData> PrepareTrainingData(List<double> time, List<double> production)
        {
            var arpsForecast = time.Select(t => (float)arpsForecaster.Forecast(t, time.Max())).ToList();
            var data = new List<HybridData>();
            for (int i = config.MinTrainingIndex; i < time.Count; i++)
            {
                var residuals = arpsForecast[i] > 0 ? (float)production[i] / arpsForecast[i] : 1.0f;
                var lags = Enumerable.Range(1, config.NumLags)
                    .Select(lag => (float)production[i - lag]).ToArray();
                if (lags.Length != config.NumLags) throw new InvalidOperationException("Lags array length must match NumLags");
                data.Add(new HybridData
                {
                    Time = (float)time[i],
                    ArpsForecast = arpsForecast[i],
                    Lags = lags,
                    Residuals = residuals
                });
            }
            return data;
        }

        private IEstimator<ITransformer> BuildPipeline()
        {
            return mlContext.Transforms.CopyColumns("Label", "Residuals")
                .Append(mlContext.Transforms.Concatenate("Features", "Time", "ArpsForecast", "Lags"))
                .Append(mlContext.Regression.Trainers.FastTree());
        }

        public List<float> Forecast(List<double> plotTime, List<double> time, List<double> production)
        {
            var forecasts = new List<float>();
            var arpsForecast = plotTime.Select(t => (float)arpsForecaster.Forecast(t, time.Max())).ToList();
            var recentData = time.Zip(production, (t, p) => (t, p)).OrderBy(x => x.t).ToList();

            foreach (var t in plotTime)
            {
                var recent = recentData.Where(x => x.t <= t).TakeLast(config.NumLags).ToList();
                if (recent.Count < config.NumLags)
                {
                    forecasts.Add(0);
                    continue;
                }

                var lags = recent.Select(x => (float)x.p).Reverse().ToArray();
                if (lags.Length != config.NumLags) throw new InvalidOperationException("Lags array length must match NumLags");
                var input = new HybridData
                {
                    Time = (float)t,
                    ArpsForecast = arpsForecast[plotTime.IndexOf(t)],
                    Lags = lags
                };

                var prediction = predictionEngine.Predict(input);
                float cappedResidual = Math.Max(0.5f, Math.Min(prediction.Residuals, 2.0f));
                float adjustedForecast = cappedResidual * input.ArpsForecast;
                forecasts.Add(Math.Max(adjustedForecast, 0));

                if (t > time.Max())
                {
                    recentData.Add((t, adjustedForecast));
                }
            }
            return forecasts;
        }
    }

    public class HybridData
    {
        public float Time { get; set; }
        public float ArpsForecast { get; set; }
        [VectorType(3)] // Explicitly set to match default NumLags=3, adjust if changed
        public float[] Lags { get; set; } = new float[3]; // Default size matches VectorType
        public float Residuals { get; set; }
    }

    public class HybridPrediction
    {
        [ColumnName("Score")]
        public float Residuals { get; set; }
    }
}