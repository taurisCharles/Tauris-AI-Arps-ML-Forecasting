// HybridForecaster.cs
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.ML;
using Microsoft.ML.Data;

namespace ArpsForecasting
{
    public class HybridForecaster
    {
        private readonly MLContext mlContext;
        private readonly ITransformer model;
        private readonly PredictionEngine<HybridData, HybridPrediction> predictionEngine;

        public HybridForecaster(List<double> time, List<double> production, List<float> arpsForecast)
        {
            mlContext = new MLContext();
            var data = new List<HybridData>();
            for (int i = 10; i < time.Count; i++)
            {
                var residuals = arpsForecast[i] > 0 ? (float)production[i] / arpsForecast[i] : 1.0f;
                data.Add(new HybridData
                {
                    Time = (float)time[i],
                    ArpsForecast = arpsForecast[i],
                    Lag1 = (float)production[i - 1],
                    Lag2 = (float)production[i - 2],
                    Lag3 = (float)production[i - 3],
                    Residuals = residuals
                });
            }

            var dataView = mlContext.Data.LoadFromEnumerable(data);
            var pipeline = mlContext.Transforms.CopyColumns(outputColumnName: "Label", inputColumnName: "Residuals")
                .Append(mlContext.Transforms.Concatenate("Features", "Time", "ArpsForecast", "Lag1", "Lag2", "Lag3"))
                .Append(mlContext.Regression.Trainers.FastTree());
            model = pipeline.Fit(dataView);
            predictionEngine = mlContext.Model.CreatePredictionEngine<HybridData, HybridPrediction>(model);
        }

        public List<float> Forecast(List<double> plotTime, List<double> time, List<double> production)
        {
            var forecasts = new List<float>();
            var recentData = new List<(double time, double production)>();
            for (int i = 0; i < time.Count; i++)
            {
                recentData.Add((time[i], production[i]));
            }

            var arpsForecaster = new SSE();
            arpsForecaster.FitHyperbolicToExponential(time, production, 53.76, 0.0001343, 1, 0.000198);
            var arpsForecast = plotTime.Select(t => (float)arpsForecaster.Forecast(t, time.Max())).ToList();

            foreach (var t in plotTime)
            {
                var recent = recentData.Where(x => x.time <= t).OrderByDescending(x => x.time).Take(3).ToList();
                if (recent.Count < 3)
                {
                    forecasts.Add(0);
                    continue;
                }

                var input = new HybridData
                {
                    Time = (float)t,
                    ArpsForecast = arpsForecast[plotTime.IndexOf(t)],
                    Lag1 = (float)recent[0].production,
                    Lag2 = (float)recent[1].production,
                    Lag3 = (float)recent[2].production
                };

                var prediction = predictionEngine.Predict(input);
                // Cap residuals to prevent excessive scaling
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
        public float Lag1 { get; set; }
        public float Lag2 { get; set; }
        public float Lag3 { get; set; }
        public float Residuals { get; set; }
    }

    public class HybridPrediction
    {
        [ColumnName("Score")]
        public float Residuals { get; set; }
    }
}