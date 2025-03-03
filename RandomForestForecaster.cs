// RandomForestForecaster.cs
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.ML;
using Microsoft.ML.Data;

namespace ArpsForecasting
{
    public class RandomForestForecaster
    {
        private readonly MLContext mlContext;
        private readonly ITransformer model;
        private readonly PredictionEngine<ProductionData, ProductionPrediction> predictionEngine;
        private readonly double historicalDeclineRate;

        public RandomForestForecaster(List<double> time, List<double> production)
        {
            mlContext = new MLContext();

            // Filter out zeros for training
            var nonZeroData = new List<(double time, double production)>();
            for (int i = 0; i < time.Count; i++)
            {
                if (production[i] > 0)
                {
                    nonZeroData.Add((time[i], production[i]));
                }
            }

            var nonZeroTime = nonZeroData.Select(x => x.time).ToList();
            var nonZeroProduction = nonZeroData.Select(x => x.production).ToList();

            // Calculate historical decline rate (from day 1 to last day)
            historicalDeclineRate = (nonZeroProduction[0] - nonZeroProduction.Last()) / (nonZeroTime.Last() - nonZeroTime[0]);

            // Prepare data for ML.NET with additional features
            var data = new List<ProductionData>();
            for (int i = 10; i < nonZeroTime.Count; i++)
            {
                var movingAverage5 = nonZeroProduction.Skip(Math.Max(0, i - 5)).Take(5).Average();
                var movingAverage30 = nonZeroProduction.Skip(Math.Max(0, i - 30)).Take(30).Average();
                var recentZeros = nonZeroProduction.Skip(Math.Max(0, i - 10)).Take(10).Count(p => p == 0);
                data.Add(new ProductionData
                {
                    Time = (float)nonZeroTime[i],
                    TimeTrend = (float)(nonZeroTime[i] / nonZeroTime.Max()),
                    DeclineRate = (float)historicalDeclineRate,
                    Lag1 = (float)nonZeroProduction[i - 1],
                    Lag2 = (float)nonZeroProduction[i - 2],
                    Lag3 = (float)nonZeroProduction[i - 3],
                    Lag4 = (float)nonZeroProduction[i - 4],
                    Lag5 = (float)nonZeroProduction[i - 5],
                    Lag6 = (float)nonZeroProduction[i - 6],
                    Lag7 = (float)nonZeroProduction[i - 7],
                    Lag8 = (float)nonZeroProduction[i - 8],
                    Lag9 = (float)nonZeroProduction[i - 9],
                    Lag10 = (float)nonZeroProduction[i - 10],
                    MovingAverage5 = (float)movingAverage5,
                    MovingAverage30 = (float)movingAverage30,
                    RecentZeroCount = recentZeros,
                    Production = (float)nonZeroProduction[i]
                });
            }

            var dataView = mlContext.Data.LoadFromEnumerable(data);

            // Define training pipeline
            var pipeline = mlContext.Transforms.CopyColumns(outputColumnName: "Label", inputColumnName: "Production")
                .Append(mlContext.Transforms.Concatenate("Features", "Time", "TimeTrend", "DeclineRate", "Lag1", "Lag2", "Lag3", "Lag4", "Lag5", "Lag6", "Lag7", "Lag8", "Lag9", "Lag10", "MovingAverage5", "MovingAverage30", "RecentZeroCount"))
                .Append(mlContext.Regression.Trainers.FastTree(numberOfTrees: 500, numberOfLeaves: 150));

            model = pipeline.Fit(dataView);
            predictionEngine = mlContext.Model.CreatePredictionEngine<ProductionData, ProductionPrediction>(model);
        }

        public List<float> Forecast(List<double> plotTime, List<double> time, List<double> production)
        {
            var forecasts = new List<float>();
            var recentData = new List<(double time, double production)>();

            for (int i = 0; i < time.Count; i++)
            {
                recentData.Add((time[i], production[i]));
            }

            foreach (var t in plotTime)
            {
                var recent = recentData.Where(x => x.time <= t).OrderByDescending(x => x.time).Take(10).ToList();
                if (recent.Count < 10)
                {
                    forecasts.Add(0);
                    continue;
                }

                var movingAverage5 = recentData.Where(x => x.time <= t).OrderByDescending(x => x.time).Take(5).Select(x => x.production).Average();
                var movingAverage30 = recentData.Where(x => x.time <= t).OrderByDescending(x => x.time).Take(30).Select(x => x.production).Average();
                var recentZeros = recentData.Where(x => x.time <= t).OrderByDescending(x => x.time).Take(10).Select(x => x.production).Count(p => p == 0);
                var input = new ProductionData
                {
                    Time = (float)t,
                    TimeTrend = (float)(t / 2180),
                    DeclineRate = (float)historicalDeclineRate,
                    Lag1 = (float)recent[0].production,
                    Lag2 = (float)recent[1].production,
                    Lag3 = (float)recent[2].production,
                    Lag4 = (float)recent[3].production,
                    Lag5 = (float)recent[4].production,
                    Lag6 = (float)recent[5].production,
                    Lag7 = (float)recent[6].production,
                    Lag8 = (float)recent[7].production,
                    Lag9 = (float)recent[8].production,
                    Lag10 = (float)recent[9].production,
                    MovingAverage5 = (float)movingAverage5,
                    MovingAverage30 = (float)movingAverage30,
                    RecentZeroCount = recentZeros
                };

                var prediction = predictionEngine.Predict(input);
                float predictedValue = Math.Max(prediction.PredictedProduction, 0);
                forecasts.Add(predictedValue);

                if (t > time.Max())
                {
                    recentData.Add((t, predictedValue));
                }
            }

            return forecasts;
        }
    }

    public class ProductionData
    {
        public float Time { get; set; }
        public float TimeTrend { get; set; }
        public float DeclineRate { get; set; }
        public float Lag1 { get; set; }
        public float Lag2 { get; set; }
        public float Lag3 { get; set; }
        public float Lag4 { get; set; }
        public float Lag5 { get; set; }
        public float Lag6 { get; set; }
        public float Lag7 { get; set; }
        public float Lag8 { get; set; }
        public float Lag9 { get; set; }
        public float Lag10 { get; set; }
        public float MovingAverage5 { get; set; }
        public float MovingAverage30 { get; set; }
        public float RecentZeroCount { get; set; }
        public float Production { get; set; }
    }

    public class ProductionPrediction
    {
        [ColumnName("Score")]
        public float PredictedProduction { get; set; }
    }
}