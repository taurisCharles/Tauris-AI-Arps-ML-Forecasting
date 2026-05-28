// HybridForecaster.cs (Corrected)
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.ML;
using Microsoft.ML.Data;

namespace ArpsForecasting
{
    public class HybridData
    {
        public float Time { get; set; }
        public float ArpsForecast { get; set; }
        [VectorType(3)]
        public float[] Lags { get; set; } = new float[3];
        public float Pressure { get; set; }
        public float PressureDelta { get; set; }
        public float RateDelta { get; set; }
        public float MovingAverage5 { get; set; }
        public float Cumulative { get; set; }
        public float Residuals { get; set; }
    }

    public class HybridPrediction
    {
        [ColumnName("Score")]
        public float Residuals { get; set; }
    }

    public class HybridForecaster
    {
        private readonly MLContext mlContext;
        private readonly ITransformer model;
        private readonly PredictionEngine<HybridData, HybridPrediction> predictionEngine;
        private readonly SSE arpsForecaster;
        private readonly ForecastConfig config;

        public HybridForecaster(List<double> time, List<double> production, List<double?>? pressure = null, ForecastConfig? config = null)
        {
            this.config = config ?? new ForecastConfig();
            if (time == null) throw new ArgumentNullException(nameof(time));
            if (production == null) throw new ArgumentNullException(nameof(production));
            if (time.Count < this.config.MinTrainingIndex + this.config.NumLags)
                throw new ArgumentException("Insufficient data for training.");

            mlContext = new MLContext();
            arpsForecaster = new SSE(this.config);

            double qiGuess = this.config.InitialRate ?? production.Max();
            double diGuess = this.config.InitialDecline ?? EstimateInitialDecline(time, production);
            diGuess = Math.Max(this.config.MinDi, Math.Min(this.config.MaxDi, diGuess));
            double bGuess = this.config.BFactor ?? 0.5;
            double dMin = this.config.TerminalDecline ?? 0.07 / 365;

            arpsForecaster.FitHyperbolicToExponential(time, production, qiGuess, diGuess, bGuess, dMin);

            var data = PrepareTrainingData(time, production, pressure);
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

        private List<HybridData> PrepareTrainingData(List<double> time, List<double> production, List<double?>? pressure)
        {
            var arpsForecast = time.Select(t => (float)arpsForecaster.Forecast(t, time.Max())).ToList();
            var data = new List<HybridData>();
            var cumulative = RunningCumulative(time, production);
            for (int i = config.MinTrainingIndex; i < time.Count; i++)
            {
                var residuals = arpsForecast[i] > 0 ? (float)production[i] / arpsForecast[i] : 1.0f;
                var lags = Enumerable.Range(1, config.NumLags)
                    .Select(lag => (float)production[i - lag]).ToArray();
                float pNow = (float)(pressure != null && i < pressure.Count && pressure[i].HasValue ? pressure[i]!.Value : 0.0);
                float pPrev = (float)(pressure != null && i > 0 && i - 1 < pressure.Count && pressure[i - 1].HasValue ? pressure[i - 1]!.Value : pNow);
                float pDelta = pNow - pPrev;
                float rateDelta = (float)(production[i] - production[i - 1]);
                float movingAverage5 = (float)production.Skip(Math.Max(0, i - 4)).Take(Math.Min(5, i + 1)).Average();
                if (lags.Length != config.NumLags) throw new InvalidOperationException("Lags array length must match NumLags");
                data.Add(new HybridData
                {
                    Time = (float)time[i],
                    ArpsForecast = arpsForecast[i],
                    Lags = lags,
                    Pressure = pNow,
                    PressureDelta = pDelta,
                    RateDelta = rateDelta,
                    MovingAverage5 = movingAverage5,
                    Cumulative = (float)cumulative[i],
                    Residuals = residuals
                });
            }
            return data;
        }

        private IEstimator<ITransformer> BuildPipeline()
        {
            return mlContext.Transforms.CopyColumns("Label", "Residuals")
                .Append(mlContext.Transforms.Concatenate(
                    "Features",
                    "Time",
                    "ArpsForecast",
                    "Lags",
                    "Pressure",
                    "PressureDelta",
                    "RateDelta",
                    "MovingAverage5",
                    "Cumulative"))
                .Append(mlContext.Regression.Trainers.FastTree());
        }

        public List<float> Forecast(List<double> plotTime, List<double> time, List<double> production, List<double?>? pressure = null)
        {
            var forecasts = new List<float>();
            var arpsForecast = plotTime.Select(t => (float)arpsForecaster.Forecast(t, time.Max())).ToList();
            var recentData = time.Zip(production, (t, p) => (t, p)).OrderBy(x => x.t).ToList();
            var pressureByTime = new Dictionary<double, float>();
            if (pressure != null)
            {
                for (int i = 0; i < time.Count && i < pressure.Count; i++)
                {
                    if (pressure[i].HasValue)
                        pressureByTime[time[i]] = (float)pressure[i]!.Value;
                }
            }
            float lastKnownPressure = pressureByTime.Count > 0 ? pressureByTime.OrderBy(kv => kv.Key).Last().Value : 0;

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
                float pNow = pressureByTime.TryGetValue(t, out float pHist) ? pHist : lastKnownPressure;
                float pPrev = pressureByTime.TryGetValue(t - 1, out float pPrevHist) ? pPrevHist : pNow;
                float pDelta = pNow - pPrev;
                float rateDelta = lags[0] - lags[Math.Min(1, lags.Length - 1)];
                float movingAverage5 = recentData.Where(x => x.t <= t).TakeLast(5).Select(x => (float)x.p).DefaultIfEmpty(0).Average();
                float cumulative = (float)IntegrateRecent(recentData, t);
                var input = new HybridData
                {
                    Time = (float)t,
                    ArpsForecast = arpsForecast[plotTime.IndexOf(t)],
                    Lags = lags,
                    Pressure = pNow,
                    PressureDelta = pDelta,
                    RateDelta = rateDelta,
                    MovingAverage5 = movingAverage5,
                    Cumulative = cumulative
                };

                var prediction = predictionEngine.Predict(input);
                float cappedResidual = Math.Max(0.5f, Math.Min(prediction.Residuals, 2.0f));
                float adjustedForecast = cappedResidual * input.ArpsForecast;
                forecasts.Add(Math.Max(adjustedForecast, 0));

                if (t > time.Max())
                {
                    recentData.Add((t, adjustedForecast));
                    pressureByTime[t] = pNow;
                }
            }
            return forecasts;
        }

        private static List<double> RunningCumulative(List<double> time, List<double> production)
        {
            var cumulative = new List<double>(time.Count);
            double acc = 0;
            for (int i = 0; i < time.Count; i++)
            {
                if (i == 0)
                {
                    cumulative.Add(0);
                    continue;
                }
                double dt = Math.Max(0, time[i] - time[i - 1]);
                acc += production[i] * dt;
                cumulative.Add(acc);
            }
            return cumulative;
        }

        private static double IntegrateRecent(List<(double t, double p)> recentData, double upToTime)
        {
            double acc = 0;
            for (int i = 1; i < recentData.Count; i++)
            {
                if (recentData[i].t > upToTime) break;
                double dt = Math.Max(0, recentData[i].t - recentData[i - 1].t);
                acc += recentData[i].p * dt;
            }
            return acc;
        }
    }
}
