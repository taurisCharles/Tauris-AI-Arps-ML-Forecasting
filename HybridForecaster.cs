// HybridForecaster.cs
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.ML;
using Microsoft.ML.Data;
using Microsoft.ML.FastTree;

public class HybridForecaster
{
    private readonly MLContext mlContext;
    private readonly ITransformer model;
    private readonly PredictionEngine<ProductionData, ProductionPrediction> predictionEngine;
    private readonly double historicalDeclineRate;
    private readonly List<float> arpsForecast;

    public HybridForecaster(List<double> time, List<double> production, List<float> arpsForecast)
    {
        mlContext = new MLContext();
        this.arpsForecast = arpsForecast;

        // Filter out zeros for training
        var nonZeroData = new List<(double time, double production, double arps)>();
        for (int i = 0; i < time.Count; i++)
        {
            if (production[i] > 0)
            {
                nonZeroData.Add((time[i], production[i], arpsForecast[i]));
            }
        }

        var nonZeroTime = nonZeroData.Select(x => x.time).ToList();
        var nonZeroProduction = nonZeroData.Select(x => x.production).ToList();
        var nonZeroArps = nonZeroData.Select(x => (float)x.arps).ToList();

        // Calculate historical decline rate
        historicalDeclineRate = (nonZeroProduction[0] - nonZeroProduction.Last()) / (nonZeroTime.Last() - nonZeroTime[0]);

        // Prepare data for ML.NET (train on residuals)
        var data = new List<ProductionData>();
        for (int i = 10; i < nonZeroTime.Count; i++) // Increase to 10 lags for more history
        {
            var movingAverage5 = nonZeroProduction.Skip(Math.Max(0, i - 5)).Take(5).Average();
            var movingAverage30 = nonZeroProduction.Skip(Math.Max(0, i - 30)).Take(30).Average();
            var recentZeros = nonZeroProduction.Skip(Math.Max(0, i - 10)).Take(10).Count(p => p == 0);
            var residual = nonZeroProduction[i] - nonZeroArps[i]; // Residual = actual - Arps forecast
            data.Add(new ProductionData
            {
                Time = (float)nonZeroTime[i],
                TimeTrend = (float)(nonZeroTime[i] / 2180),
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
                Production = (float)residual // Train on residuals
            });
        }

        var dataView = mlContext.Data.LoadFromEnumerable(data);

        // Define training pipeline
        var pipeline = mlContext.Transforms.CopyColumns(outputColumnName: "Label", inputColumnName: "Production")
            .Append(mlContext.Transforms.Concatenate("Features", "Time", "TimeTrend", "DeclineRate", "Lag1", "Lag2", "Lag3", "Lag4", "Lag5", "Lag6", "Lag7", "Lag8", "Lag9", "Lag10", "MovingAverage5", "MovingAverage30", "RecentZeroCount"))
            .Append(mlContext.Regression.Trainers.FastForest(numberOfTrees: 500, numberOfLeaves: 150));

        // Train the model
        model = pipeline.Fit(dataView);

        // Create prediction engine
        predictionEngine = mlContext.Model.CreatePredictionEngine<ProductionData, ProductionPrediction>(model);
    }

    public List<float> Forecast(List<double> plotTime, List<double> time, List<double> production)
    {
        var forecasts = new List<float>();
        var recentData = new List<(double time, double production, double arps)>();

        // Populate historical data
        for (int i = 0; i < time.Count; i++)
        {
            recentData.Add((time[i], production[i], arpsForecast[i]));
        }

        foreach (var t in plotTime)
        {
            int index = plotTime.IndexOf(t);
            var recent = recentData.Where(x => x.time <= t).OrderByDescending(x => x.time).Take(10).ToList();
            if (recent.Count < 10)
            {
                forecasts.Add(arpsForecast[index]); // Use Arps forecast if not enough data
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

            var residualPrediction = predictionEngine.Predict(input);
            float predictedResidual = Math.Max(residualPrediction.PredictedProduction, 0);
            float hybridForecast = arpsForecast[index] + predictedResidual;
            forecasts.Add(Math.Max(hybridForecast, 0));

            // Add predicted value to recentData for future predictions
            if (t > time.Max())
            {
                recentData.Add((t, hybridForecast, arpsForecast[index]));
            }
        }

        return forecasts;
    }
}