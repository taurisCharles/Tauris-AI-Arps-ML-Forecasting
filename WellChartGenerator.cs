using System;
using System.Collections.Generic;
using System.Linq;
using ScottPlot;
using System.Diagnostics;

namespace ArpsForecasting
{
    public class WellChartGenerator
    {
        private readonly string wellName;
        private readonly List<double> allTime;
        private readonly List<double> allProduction;
        private readonly string outputDir;
        private readonly int effectiveOffset;

        public WellChartGenerator(string wellName, List<double> time, List<double> production, string outputDir, int effectiveOffset = -720)
        {
            this.wellName = wellName;
            this.allTime = time ?? throw new ArgumentNullException(nameof(time));
            this.allProduction = production ?? throw new ArgumentNullException(nameof(production));
            this.outputDir = outputDir ?? throw new ArgumentNullException(nameof(outputDir));
            this.effectiveOffset = effectiveOffset;
        }

        public void GenerateAndSaveChart()
        {
            if (allTime.Count == 0 || allProduction.Count == 0)
            {
                Console.WriteLine($"Skipping well {wellName}: No valid data.");
                return;
            }

            Console.WriteLine($"\nProcessing well: {wellName}");

            // Define historical and forecast periods
            double historicalEndTime = allTime.Max(); // Last time point for this well
            var historicalTime = allTime.Where(t => t <= historicalEndTime).ToList();
            var historicalProduction = allProduction.Take(historicalTime.Count).ToList();
            var plotTime = Enumerable.Range((int)allTime.Min(), (int)allTime.Max() - (int)allTime.Min() + 1100 + 1).Select(t => (double)t).ToList();
            var forecastTime = plotTime.Where(t => t > historicalEndTime).ToList();

            var plt = new Plot();
            var actualScatter = plt.Add.Scatter(allTime.ToArray(), allProduction.ToArray(), color: Colors.Blue);
            actualScatter.Label = "Actual Production";

            // Prophet-Like Forecast with timer
            var prophetStopwatch = Stopwatch.StartNew();
            var prophetForecaster = new ProphetLikeForecaster(allTime, allProduction);
            var prophetForecast = prophetForecaster.Forecast(plotTime, allTime, allProduction);
            prophetStopwatch.Stop();
            Console.WriteLine($"Prophet-Like Forecast Time for {wellName}: {prophetStopwatch.ElapsedMilliseconds} ms");

            // Debug forecast values
            Console.WriteLine($"Prophet-Like Forecast Values for {wellName} (first 5 after historicalEndTime):");
            var futureTimes = plotTime.Where(t => t > historicalEndTime).Take(5).ToList();
            foreach (var t in futureTimes)
            {
                int index = plotTime.IndexOf(t);
                Console.WriteLine($"Time {t}: {prophetForecast[index]:F3}");
            }

            // Hybrid Arps + Random Forest with timer
            var hybridStopwatch = Stopwatch.StartNew();
            var config = new ForecastConfig(); // Let HybridForecaster estimate parameters
            var hybridForecaster = new HybridForecaster(allTime, allProduction, config);
            var hybridForecast = hybridForecaster.Forecast(plotTime, allTime, allProduction);
            hybridStopwatch.Stop();
            Console.WriteLine($"Hybrid Arps + Random Forest Forecast Time for {wellName}: {hybridStopwatch.ElapsedMilliseconds} ms");

            // Debug Hybrid forecast for plotting
            var hybridForecastForPlot = new List<float>();
            foreach (var t in plotTime)
            {
                int index = allTime.IndexOf(t);
                if (t <= historicalEndTime && index >= 0 && index < allProduction.Count)
                    hybridForecastForPlot.Add((float)allProduction[index]);
                else
                    hybridForecastForPlot.Add((float)hybridForecast[plotTime.IndexOf(t)]);
            }
            Console.WriteLine($"Hybrid Arps + Random Forest For Plot for {wellName} (first 5 after historicalEndTime):");
            foreach (var t in futureTimes)
            {
                int index = plotTime.IndexOf(t);
                Console.WriteLine($"Time {t}: {hybridForecastForPlot[index]:F3}");
            }

            // Define effective date for ARPS equivalents
            double effectiveDate = historicalEndTime + effectiveOffset; // e.g., t=1080-720=360

            // Interpolate Qi values at t=effectiveDate from forecasts
            int tBeforeIndex = plotTime.IndexOf(Math.Floor(effectiveDate / 100) * 100); // Nearest 100 before effectiveDate
            int tAfterIndex = plotTime.IndexOf(Math.Ceiling(effectiveDate / 100) * 100); // Nearest 100 after effectiveDate
            double prophetQi = prophetForecast[tBeforeIndex] + (prophetForecast[tAfterIndex] - prophetForecast[tBeforeIndex]) * (effectiveDate - plotTime[tBeforeIndex]) / (plotTime[tAfterIndex] - plotTime[tBeforeIndex]);
            double hybridQi = hybridForecastForPlot[tBeforeIndex] + (hybridForecastForPlot[tAfterIndex] - hybridForecastForPlot[tBeforeIndex]) * (effectiveDate - plotTime[tBeforeIndex]) / (plotTime[tAfterIndex] - plotTime[tBeforeIndex]);
            Console.WriteLine($"Qi at t={effectiveDate} (Prophet-Like Forecast) for {wellName}: {prophetQi:F3}");
            Console.WriteLine($"Qi at t={effectiveDate} (Hybrid ARPS + RF) for {wellName}: {hybridQi:F3}");

            // Fit Arps Equation Equivalent for Prophet-Like Forecast
            var prophetArpsStopwatch = Stopwatch.StartNew();
            var prophetArpsForecaster = new SSE();
            prophetArpsForecaster.SetQi(prophetQi); // Set Qi to Prophet-Like Forecast at effective date
            prophetArpsForecaster.SetReferenceTime(effectiveDate); // Set effective date
            var prophetFitTimes = plotTime.Where(t => t >= effectiveDate).ToList();
            var prophetFitData = prophetFitTimes.Select(t => (double)prophetForecast[plotTime.IndexOf(t)]).ToList();
            prophetArpsForecaster.FitHyperbolic(prophetFitTimes, prophetFitData, prophetQi, 0.003, 0.8); // Use pure hyperbolic fit
            var prophetArpsForecast = plotTime.Select(t => (float)prophetArpsForecaster.Forecast(t, effectiveDate)).ToList();
            prophetArpsStopwatch.Stop();
            Console.WriteLine($"Prophet-Like Arps Equivalent Forecast Time for {wellName}: {prophetArpsStopwatch.ElapsedMilliseconds} ms");

            // Get ARPS parameters for Prophet-Like Equivalent
            string prophetArpsParams = GetArpsParameters(prophetArpsForecaster, effectiveDate);

            // Fit Arps Equation Equivalent for Hybrid Arps + Random Forest
            var hybridArpsStopwatch = Stopwatch.StartNew();
            var hybridArpsForecaster = new SSE();
            hybridArpsForecaster.SetQi(hybridQi); // Set Qi to Hybrid ARPS + RF at effective date
            hybridArpsForecaster.SetReferenceTime(effectiveDate); // Set effective date
            var hybridFitTimes = plotTime.Where(t => t >= effectiveDate).ToList();
            var hybridFitData = hybridFitTimes.Select(t => (double)hybridForecastForPlot[plotTime.IndexOf(t)]).ToList();
            hybridArpsForecaster.FitHyperbolic(hybridFitTimes, hybridFitData, hybridQi, 0.004, 0.7); // Different initial guesses
            var hybridArpsForecast = plotTime.Select(t => (float)hybridArpsForecaster.Forecast(t, effectiveDate)).ToList();
            hybridArpsStopwatch.Stop();
            Console.WriteLine($"Hybrid Arps + Random Forest Arps Equivalent Forecast Time for {wellName}: {hybridArpsStopwatch.ElapsedMilliseconds} ms");

            // Get ARPS parameters for Hybrid Equivalent
            string hybridArpsParams = GetArpsParameters(hybridArpsForecaster, effectiveDate);

            // Plot the Prophet-Like Forecast (Green)
            var prophetLine = plt.Add.Scatter(plotTime.ToArray(), prophetForecast.ToArray(), color: Colors.Green);
            prophetLine.Label = "Prophet-Like Forecast";
            prophetLine.MarkerSize = 0;

            // Plot Hybrid Arps + Random Forest
            var hybridLine = plt.Add.Scatter(plotTime.ToArray(), hybridForecastForPlot.ToArray(), color: Colors.Cyan);
            hybridLine.Label = "Hybrid ARPS + RF";
            hybridLine.MarkerSize = 0;

            // Plot ARPS equivalents last to bring them to the front
            var prophetArpsLine = plt.Add.Scatter(plotTime.ToArray(), prophetArpsForecast.ToArray(), color: Colors.Red);
            prophetArpsLine.Label = $"Prophet-Like ARPS (Eff Date: {effectiveDate}, {prophetArpsParams})";
            prophetArpsLine.MarkerSize = 0;

            var hybridArpsLine = plt.Add.Scatter(plotTime.ToArray(), hybridArpsForecast.ToArray(), color: Colors.Purple);
            hybridArpsLine.Label = $"Hybrid ARPS (Eff Date: {effectiveDate}, {hybridArpsParams})";
            hybridArpsLine.MarkerSize = 0;

            // Print projections for every 100 days from 100 to 2180
            Console.WriteLine($"\n=== Projections for Every 100 Days from 100 to 2180 for {wellName} ===");
            var reportDays = Enumerable.Range(1, 21).Select(i => i * 100).ToList(); // 100, 200, 300, ..., 2100, 2180

            Console.WriteLine($"\nActual Production Projections for {wellName}:");
            foreach (var day in reportDays)
            {
                int index = allTime.IndexOf((double)day);
                if (index >= 0 && index < allProduction.Count)
                    Console.WriteLine($"Day {day}: {allProduction[index]:F3}");
                else
                    Console.WriteLine($"Day {day}: N/A (beyond historical data)");
            }

            Console.WriteLine($"\nProphet-Like Forecast Projections for {wellName}:");
            foreach (var day in reportDays)
            {
                int index = plotTime.IndexOf((double)day);
                if (index >= 0)
                    Console.WriteLine($"Day {day}: {prophetForecast[index]:F3}");
                else
                    Console.WriteLine($"Day {day}: N/A");
            }

            Console.WriteLine($"\nHybrid Arps + Random Forest Projections for {wellName}:");
            foreach (var day in reportDays)
            {
                int index = plotTime.IndexOf((double)day);
                if (index >= 0)
                    Console.WriteLine($"Day {day}: {hybridForecastForPlot[index]:F3}");
                else
                    Console.WriteLine($"Day {day}: N/A");
            }

            Console.WriteLine($"\nProphet-Like Arps Equivalent Projections for {wellName}:");
            foreach (var day in reportDays)
            {
                int index = plotTime.IndexOf((double)day);
                if (index >= 0)
                    Console.WriteLine($"Day {day}: {prophetArpsForecast[index]:F3}");
                else
                    Console.WriteLine($"Day {day}: N/A");
            }

            Console.WriteLine($"\nHybrid Arps + Random Forest Arps Equivalent Projections for {wellName}:");
            foreach (var day in reportDays)
            {
                int index = plotTime.IndexOf((double)day);
                if (index >= 0)
                    Console.WriteLine($"Day {day}: {hybridArpsForecast[index]:F3}");
                else
                    Console.WriteLine($"Day {day}: N/A");
            }

            plt.Title($"{wellName} Production Data vs Forecast");
            plt.XLabel("Time (days)");
            plt.YLabel("Production (units)");
            plt.ShowLegend(Alignment.UpperRight); // Enable legend
            plt.Legend.Font.Size = 10; // Set font size after enabling legend

            // Save the plot in the Charts folder with the well name
            string outputPath = System.IO.Path.Combine(outputDir, $"{wellName}_forecast.png");
            plt.SavePng(outputPath, 800, 600);
            Console.WriteLine($"Plot saved as '{outputPath}'");
        }

        private static string GetArpsParameters(SSE forecaster, double effectiveDate)
        {
            double diPercent = forecaster.Di * 365 * 100; // Convert daily Di to annual percentage
            double dminPercent = forecaster.Dmin * 365 * 100; // Convert daily Dmin to annual percentage
            if (forecaster.Dmin == 0) // Pure hyperbolic fit
                return $"Qi:{forecaster.Qi:F2}, Di:{diPercent:F2}%, b:{forecaster.B:F2}";
            return $"Qi:{forecaster.Qi:F2}, Di:{diPercent:F2}%, b:{forecaster.B:F2}, Dmin:{dminPercent:F2}%";
        }
    }
}