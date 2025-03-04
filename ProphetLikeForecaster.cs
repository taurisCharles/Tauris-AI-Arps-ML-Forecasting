// ProphetLikeForecaster.cs
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.ML;
using Microsoft.ML.Data;

namespace ArpsForecasting
{
    public class ProphetLikeForecaster
    {
        private readonly MLContext mlContext;
        private readonly ITransformer trendModel;
        private readonly PredictionEngine<TrendData, TrendPrediction> trendPredictionEngine;
        private readonly double[] dailySeasonalityCoefficients;
        private readonly double[] weeklySeasonalityCoefficients;
        private readonly double lastTrendValue;

        public ProphetLikeForecaster(List<double> time, List<double> production)
        {
            mlContext = new MLContext();

            // Prepare data for trend modeling
            var trendData = new List<TrendData>();
            for (int i = 0; i < time.Count; i++)
            {
                trendData.Add(new TrendData
                {
                    Time = (float)time[i],
                    TimeSquared = (float)(time[i] * time[i]),
                    Production = (float)production[i]
                });
            }

            var trendDataView = mlContext.Data.LoadFromEnumerable(trendData);

            // Train a simpler trend model using linear regression
            var trendPipeline = mlContext.Transforms.CopyColumns(outputColumnName: "Label", inputColumnName: "Production")
                .Append(mlContext.Transforms.Concatenate("Features", "Time"))
                .Append(mlContext.Regression.Trainers.LbfgsPoissonRegression());
            trendModel = trendPipeline.Fit(trendDataView);
            trendPredictionEngine = mlContext.Model.CreatePredictionEngine<TrendData, TrendPrediction>(trendModel);

            // Fit seasonality using Fourier series (daily and weekly)
            dailySeasonalityCoefficients = FitFourierSeries(time, production, 1, 3);
            weeklySeasonalityCoefficients = FitFourierSeries(time, production, 7, 3);

            // Anchor the last trend value to the actual production at t=1080
            int lastIndex = time.IndexOf(time.Max());
            lastTrendValue = production[lastIndex]; // 53.76
        }

        public List<float> Forecast(List<double> plotTime, List<double> time, List<double> production)
        {
            var forecasts = new List<float>();
            double lastTime = time.Max();

            foreach (var t in plotTime)
            {
                float trend;
                if (t <= lastTime)
                {
                    var trendInput = new TrendData
                    {
                        Time = (float)t,
                        TimeSquared = (float)(t * t)
                    };
                    trend = trendPredictionEngine.Predict(trendInput).PredictedProduction;
                }
                else
                {
                    // Adjusted decay rate for a more realistic decline
                    trend = (float)(lastTrendValue * Math.Exp(-0.001 * (t - lastTime)));
                }
                if (float.IsNaN(trend) || float.IsInfinity(trend))
                {
                    trend = 0;
                }

                // Calculate seasonality (only apply in historical period)
                float dailySeasonality = t <= lastTime ? CalculateSeasonality(t, 1, dailySeasonalityCoefficients) : 0;
                float weeklySeasonality = t <= lastTime ? CalculateSeasonality(t, 7, weeklySeasonalityCoefficients) : 0;

                // Combine components
                float forecast = trend + dailySeasonality + weeklySeasonality;
                if (float.IsNaN(forecast) || float.IsInfinity(forecast))
                {
                    forecast = trend;
                }
                forecasts.Add(Math.Max(forecast, 0));
            }

            return forecasts;
        }

        private double[] FitFourierSeries(List<double> time, List<double> production, double period, int fourierOrder)
        {
            int n = time.Count;
            int numCoefficients = 2 * fourierOrder;
            var coefficients = new double[numCoefficients];

            // Create design matrix X for Fourier terms
            var X = new double[n][];
            for (int i = 0; i < n; i++)
            {
                X[i] = new double[numCoefficients];
                for (int k = 1; k <= fourierOrder; k++)
                {
                    X[i][2 * (k - 1)] = Math.Sin(2 * Math.PI * k * time[i] / period);
                    X[i][2 * (k - 1) + 1] = Math.Cos(2 * Math.PI * k * time[i] / period);
                }
            }

            // Create target vector y (detrended production)
            var trendData = new List<TrendData>();
            for (int i = 0; i < time.Count; i++)
            {
                trendData.Add(new TrendData
                {
                    Time = (float)time[i],
                    TimeSquared = (float)(time[i] * time[i]),
                    Production = (float)production[i]
                });
            }
            var trendDataView = mlContext.Data.LoadFromEnumerable(trendData);
            var trendPredictions = mlContext.Data.CreateEnumerable<TrendPrediction>(trendModel.Transform(trendDataView), reuseRowObject: false);
            var detrendedProduction = production.Zip(trendPredictions, (p, pred) =>
            {
                float trendValue = pred.PredictedProduction;
                if (float.IsNaN(trendValue) || float.IsInfinity(trendValue))
                    return 0.0;
                return p - trendValue;
            }).ToList();

            // Solve for coefficients using least squares with regularization
            var XtX = new double[numCoefficients][];
            var Xty = new double[numCoefficients];
            const double lambda = 1e-5;
            for (int i = 0; i < numCoefficients; i++)
            {
                XtX[i] = new double[numCoefficients];
                for (int j = 0; j < numCoefficients; j++)
                {
                    XtX[i][j] = 0;
                    for (int k = 0; k < n; k++)
                    {
                        XtX[i][j] += X[k][i] * X[k][j];
                    }
                    if (i == j)
                        XtX[i][j] += lambda;
                }
                Xty[i] = 0;
                for (int k = 0; k < n; k++)
                {
                    Xty[i] += X[k][i] * detrendedProduction[k];
                }
            }

            // Solve XtX * coefficients = Xty using Gaussian elimination
            for (int i = 0; i < numCoefficients; i++)
            {
                if (Math.Abs(XtX[i][i]) < 1e-10)
                {
                    Console.WriteLine($"Warning: Near-zero pivot in Gaussian elimination at index {i}. Adjusting...");
                    XtX[i][i] = 1e-10;
                }
                for (int j = i + 1; j < numCoefficients; j++)
                {
                    double factor = XtX[j][i] / XtX[i][i];
                    for (int k = 0; k < numCoefficients; k++)
                        XtX[j][k] -= factor * XtX[i][k];
                    Xty[j] -= factor * Xty[i];
                }
            }

            for (int i = numCoefficients - 1; i >= 0; i--)
            {
                double sum = 0;
                for (int j = i + 1; j < numCoefficients; j++)
                    sum += XtX[i][j] * coefficients[j];
                coefficients[i] = (Xty[i] - sum) / XtX[i][i];
                if (double.IsNaN(coefficients[i]) || double.IsInfinity(coefficients[i]))
                    coefficients[i] = 0;
            }

            return coefficients;
        }

        private float CalculateSeasonality(double time, double period, double[] coefficients)
        {
            float seasonality = 0;
            int fourierOrder = coefficients.Length / 2;
            for (int k = 1; k <= fourierOrder; k++)
            {
                int sinIndex = 2 * (k - 1);
                int cosIndex = sinIndex + 1;
                double sinTerm = coefficients[sinIndex] * Math.Sin(2 * Math.PI * k * time / period);
                double cosTerm = coefficients[cosIndex] * Math.Cos(2 * Math.PI * k * time / period);
                if (double.IsNaN(sinTerm) || double.IsInfinity(sinTerm))
                    sinTerm = 0;
                if (double.IsNaN(cosTerm) || double.IsInfinity(cosTerm))
                    cosTerm = 0;
                seasonality += (float)(sinTerm + cosTerm);
            }
            return seasonality;
        }
    }

    public class TrendData
    {
        public float Time { get; set; }
        public float TimeSquared { get; set; }
        public float Production { get; set; }
    }

    public class TrendPrediction
    {
        [ColumnName("Score")]
        public float PredictedProduction { get; set; }
    }
}