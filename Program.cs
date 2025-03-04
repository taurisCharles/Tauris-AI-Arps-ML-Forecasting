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

                // Hybrid Arps + Random Forest with timer
                var hybridStopwatch = Stopwatch.StartNew();
                var arpsForecaster = new SSE();
                arpsForecaster.FitHyperbolicToExponential(allTime, allProduction, 53.76, 0.0001343, 1, 0.000198);
                var arpsForecast = plotTime.Select(t => (float)arpsForecaster.Forecast(t, historicalEndTime)).ToList();
                var hybridForecaster = new HybridForecaster(allTime, allProduction, arpsForecast);
                var hybridForecast = hybridForecaster.Forecast(plotTime, allTime, allProduction);
                hybridStopwatch.Stop();
                Console.WriteLine($"Hybrid Arps + Random Forest Forecast Time: {hybridStopwatch.ElapsedMilliseconds} ms");

                // Debug Hybrid forecast for plotting
                var hybridForecastForPlot = plotTime.Select(t => t <= historicalEndTime ? (float)allProduction[allTime.IndexOf(t)] : hybridForecast[plotTime.IndexOf(t)]).ToList();
                Console.WriteLine("Hybrid Arps + Random Forest For Plot (first 5 after historicalEndTime):");
                foreach (var t in futureTimes)
                {
                    int index = plotTime.IndexOf(t);
                    Console.WriteLine($"Time {t}: {hybridForecastForPlot[index]:F3}");
                }

                // Fit Arps Equation Equivalent for Prophet-Like Forecast
                var prophetArpsStopwatch = Stopwatch.StartNew();
                var prophetArpsForecaster = new SSE();
                double dMinDaily = 0.07 / 365; // 7% annual rate converted to daily
                prophetArpsForecaster.FitHyperbolicToExponential(plotTime, prophetForecast.Select(x => (double)x).ToList(), 53.76, 0.0001343, 1, dMinDaily);
                var prophetArpsForecast = plotTime.Select(t => (float)prophetArpsForecaster.Forecast(t, 0)).ToList();
                prophetArpsStopwatch.Stop();
                Console.WriteLine($"Prophet-Like Arps Equivalent Forecast Time: {prophetArpsStopwatch.ElapsedMilliseconds} ms");

                var prophetArpsLine = plt.Add.Scatter(plotTime.ToArray(), prophetArpsForecast.ToArray(), color: Colors.Orange);
                prophetArpsLine.Label = $"Prophet-Like Arps Equivalent (Qi=53.76 @ t=1080, Di=0.0001343, b=1, Dmin={dMinDaily:F7})";
                prophetArpsLine.MarkerSize = 0;

                // Fit Arps Equation Equivalent for Hybrid Arps + Random Forest
                var hybridArpsStopwatch = Stopwatch.StartNew();
                var hybridArpsForecaster = new SSE();

                // Combine historical data and hybrid forecast for fitting
                var hybridCombined = new List<double>();
                for (int i = 0; i < plotTime.Count; i++)
                {
                    double t = plotTime[i];
                    if (t <= historicalEndTime)
                    {
                        int index = allTime.IndexOf(t);
                        hybridCombined.Add(allProduction[index]);
                    }
                    else
                    {
                        int index = plotTime.IndexOf(t);
                        hybridCombined.Add(hybridForecast[index]);
                    }
                }

                hybridArpsForecaster.FitHyperbolicToExponential(plotTime, hybridCombined, 53.76, 0.0001343, 1, dMinDaily);
                var hybridArpsForecast = plotTime.Select(t => (float)hybridArpsForecaster.Forecast(t, 0)).ToList();
                hybridArpsStopwatch.Stop();
                Console.WriteLine($"Hybrid Arps + Random Forest Arps Equivalent Forecast Time: {hybridArpsStopwatch.ElapsedMilliseconds} ms");

                var hybridArpsLine = plt.Add.Scatter(plotTime.ToArray(), hybridArpsForecast.ToArray(), color: Colors.Purple);
                hybridArpsLine.Label = $"Hybrid Arps + Random Forest Arps Equivalent (Qi=53.76 @ t=1080, Di=0.0001343, b=1, Dmin={dMinDaily:F7})";
                hybridArpsLine.MarkerSize = 0;

                // Plot the Prophet-Like Forecast (Green) after the Arps Equivalent lines
                var prophetLine = plt.Add.Scatter(plotTime.ToArray(), prophetForecast.ToArray(), color: Colors.Green);
                prophetLine.Label = "Prophet-Like Forecast (C#)";
                prophetLine.MarkerSize = 0;

                // Plot Hybrid Arps + Random Forest last to ensure visibility
                var hybridLine = plt.Add.Scatter(plotTime.ToArray(), hybridForecastForPlot.ToArray(), color: Colors.Cyan);
                hybridLine.Label = "Hybrid Arps + Random Forest";
                hybridLine.MarkerSize = 0;

                // Print projections for key future days
                Console.WriteLine("\n=== Projections for Next 1100 Days ===");
                var futureDays = new List<int> { 1100, 1200, 1300, 1400, 1500, 1600, 1700, 1800, 1900, 2000, 2100, 2180 };

                Console.WriteLine("\nHybrid Arps + Random Forest Projections:");
                foreach (var day in futureDays)
                {
                    int index = plotTime.IndexOf(day);
                    if (index >= 0)
                        Console.WriteLine($"Day {day}: {hybridForecast[index]:F3}");
                }

                Console.WriteLine("\nProphet-Like Forecast Projections (C#):");
                foreach (var day in futureDays)
                {
                    int index = plotTime.IndexOf(day);
                    if (index >= 0)
                        Console.WriteLine($"Day {day}: {prophetForecast[index]:F3}");
                }

                Console.WriteLine("\nProphet-Like Arps Equivalent Projections:");
                foreach (var day in futureDays)
                {
                    int index = plotTime.IndexOf(day);
                    if (index >= 0)
                        Console.WriteLine($"Day {day}: {prophetArpsForecast[index]:F3}");
                }

                Console.WriteLine("\nHybrid Arps + Random Forest Arps Equivalent Projections:");
                foreach (var day in futureDays)
                {
                    int index = plotTime.IndexOf(day);
                    if (index >= 0)
                        Console.WriteLine($"Day {day}: {hybridArpsForecast[index]:F3}");
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