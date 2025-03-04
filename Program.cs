// Program.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ScottPlot;
using System.Diagnostics;

namespace ArpsForecasting
{
    class Program
    {
        static void Main(string[] args)
        {
            try
            {
                string csvPath = Path.Combine(Directory.GetCurrentDirectory(), "production_data.csv");
                var (allTime, allProduction) = LoadDataFromCsv(csvPath);

                if (allTime.Count == 0 || allProduction.Count == 0)
                    throw new InvalidOperationException("No valid data loaded from CSV.");

                // Define historical and forecast periods
                double historicalEndTime = allTime.Max(); // t=1080
                var historicalTime = allTime.Where(t => t <= historicalEndTime).ToList();
                var historicalProduction = allProduction.Take(historicalTime.Count).ToList();
                var plotTime = Enumerable.Range((int)allTime.Min(), (int)allTime.Max() - (int)allTime.Min() + 1100 + 1).Select(t => (double)t).ToList();
                var forecastTime = plotTime.Where(t => t > historicalEndTime).ToList();

                var plt = new Plot();
                var actualScatter = plt.Add.Scatter(allTime.ToArray(), allProduction.ToArray(), color: Colors.Blue);
                actualScatter.Label = "Actual Production (Full History)";

                // Random Forest Forecast with timer
                var rfStopwatch = Stopwatch.StartNew();
                var rfForecaster = new RandomForestForecaster(allTime, allProduction);
                var rfForecast = rfForecaster.Forecast(plotTime, allTime, allProduction);
                rfStopwatch.Stop();
                Console.WriteLine($"Random Forest Forecast Time: {rfStopwatch.ElapsedMilliseconds} ms");
                var rfForecastForPlot = plotTime.Select(t => t <= historicalEndTime ? 0 : rfForecast[plotTime.IndexOf(t)]).ToList();
                var rfLine = plt.Add.Scatter(plotTime.ToArray(), rfForecastForPlot.ToArray(), color: Colors.Purple);
                rfLine.Label = "ML.NET Random Forest";
                rfLine.MarkerSize = 0;

                // Hybrid Arps + Random Forest with timer
                var hybridStopwatch = Stopwatch.StartNew();
                var arpsForecaster = new SSE();
                arpsForecaster.FitHyperbolicToExponential(allTime, allProduction, 53.76, 0.0001343, 1, 0.000198);
                var arpsForecast = plotTime.Select(t => (float)arpsForecaster.Forecast(t, historicalEndTime)).ToList();
                var hybridForecaster = new HybridForecaster(allTime, allProduction, arpsForecast);
                var hybridForecast = hybridForecaster.Forecast(plotTime, allTime, allProduction);
                hybridStopwatch.Stop();
                Console.WriteLine($"Hybrid Arps + Random Forest Forecast Time: {hybridStopwatch.ElapsedMilliseconds} ms");
                var hybridForecastForPlot = plotTime.Select(t => t <= historicalEndTime ? (float)allProduction[allTime.IndexOf(t)] : hybridForecast[plotTime.IndexOf(t)]).ToList();
                var hybridLine = plt.Add.Scatter(plotTime.ToArray(), hybridForecastForPlot.ToArray(), color: Colors.Cyan);
                hybridLine.Label = "Hybrid Arps + Random Forest";
                hybridLine.MarkerSize = 0;

                // Equivalent Arps Model with timer
                var equivalentArpsStopwatch = Stopwatch.StartNew();
                var equivalentArpsForecaster = new SSE();

                // Combine historical data and hybrid forecast
                var combinedTime = new List<double>();
                var combinedProduction = new List<double>();
                for (int i = 0; i < plotTime.Count; i++)
                {
                    double t = plotTime[i];
                    if (t <= historicalEndTime)
                    {
                        combinedTime.Add(t);
                        int index = allTime.IndexOf(t);
                        combinedProduction.Add(allProduction[index]);
                    }
                    else
                    {
                        combinedTime.Add(t);
                        int index = plotTime.IndexOf(t);
                        combinedProduction.Add(hybridForecast[index]);
                    }
                }

                Console.WriteLine($"CombinedTime length: {combinedTime.Count}, CombinedProduction length: {combinedProduction.Count}");
                equivalentArpsForecaster.FitHyperbolicToExponential(combinedTime, combinedProduction, 53.76, 0.0001343, 1, 0.000198);
                var equivalentArpsForecast = plotTime.Select(t => (float)equivalentArpsForecaster.Forecast(t, historicalEndTime)).ToList();
                equivalentArpsStopwatch.Stop();
                Console.WriteLine($"Equivalent Arps Forecast Time: {equivalentArpsStopwatch.ElapsedMilliseconds} ms");
                var equivalentArpsForecastForPlot = plotTime.Select(t => t <= historicalEndTime ? 0 : equivalentArpsForecast[plotTime.IndexOf(t)]).ToList();
                var equivalentArpsLine = plt.Add.Scatter(plotTime.ToArray(), equivalentArpsForecastForPlot.ToArray(), color: Colors.Magenta);
                equivalentArpsLine.Label = "Equivalent Arps (Qi=53.76 @ t=1080, Di=0.0001343, b=1, Dmin=0.000198)";
                equivalentArpsLine.MarkerSize = 0;

                // Prophet-Like Forecast with timer
                var prophetStopwatch = Stopwatch.StartNew();
                var prophetForecaster = new ProphetLikeForecaster(allTime, allProduction);
                var prophetForecast = prophetForecaster.Forecast(plotTime, allTime, allProduction);
                prophetStopwatch.Stop();
                Console.WriteLine($"Prophet-Like Forecast Time: {prophetStopwatch.ElapsedMilliseconds} ms");

                // Debug forecast values
                Console.WriteLine("Prophet-Like Forecast Values (first 5 after historicalEndTime):");
                var futureTimes = plotTime.Where(t => t > historicalEndTime).Take(5).ToList();
                foreach (var t in futureTimes)
                {
                    int index = plotTime.IndexOf(t);
                    Console.WriteLine($"Time {t}: {prophetForecast[index]:F3}");
                }

                var prophetForecastForPlot = plotTime.Select(t => t <= historicalEndTime ? 0 : prophetForecast[plotTime.IndexOf(t)]).ToList();
                var prophetLine = plt.Add.Scatter(plotTime.ToArray(), prophetForecastForPlot.ToArray(), color: Colors.Green);
                prophetLine.Label = "Prophet-Like Forecast (C#)";
                prophetLine.MarkerSize = 0;

                // Print projections for key future days
                Console.WriteLine("\n=== Projections for Next 1100 Days ===");
                var futureDays = new List<int> { 1100, 1200, 1300, 1400, 1500, 1600, 1700, 1800, 1900, 2000, 2100, 2180 };
                Console.WriteLine("Random Forest Projections:");
                foreach (var day in futureDays)
                {
                    int index = plotTime.IndexOf(day);
                    if (index >= 0)
                        Console.WriteLine($"Day {day}: {rfForecast[index]:F3}");
                }

                Console.WriteLine("\nHybrid Arps + Random Forest Projections:");
                foreach (var day in futureDays)
                {
                    int index = plotTime.IndexOf(day);
                    if (index >= 0)
                        Console.WriteLine($"Day {day}: {hybridForecast[index]:F3}");
                }

                Console.WriteLine("\nEquivalent Arps Projections:");
                foreach (var day in futureDays)
                {
                    int index = plotTime.IndexOf(day);
                    if (index >= 0)
                        Console.WriteLine($"Day {day}: {equivalentArpsForecast[index]:F3}");
                }

                Console.WriteLine("\nProphet-Like Forecast Projections (C#):");
                foreach (var day in futureDays)
                {
                    int index = plotTime.IndexOf(day);
                    if (index >= 0)
                        Console.WriteLine($"Day {day}: {prophetForecast[index]:F3}");
                }

                plt.Title($"Production Data vs Forecast");
                plt.XLabel("Time (days)");
                plt.YLabel("Production (units)");
                plt.ShowLegend(Alignment.UpperRight);
                plt.SavePng("forecast.png", 800, 600);
                Console.WriteLine("Plot saved as 'forecast.png' in the current directory.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
        }

        static (List<double> time, List<double> production) LoadDataFromCsv(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"CSV file not found at: {filePath}");

            var time = new List<double>();
            var production = new List<double>();
            var lines = File.ReadAllLines(filePath);

            if (lines.Length <= 1)
                throw new InvalidDataException("CSV file is empty or has only a header.");

            foreach (var line in lines.Skip(1))
            {
                var columns = line.Split(',');
                if (columns.Length != 2 || !double.TryParse(columns[0], out double t) || !double.TryParse(columns[1], out double p))
                    continue;

                time.Add(t);
                production.Add(p);
            }

            return (time, production);
        }
    }
}