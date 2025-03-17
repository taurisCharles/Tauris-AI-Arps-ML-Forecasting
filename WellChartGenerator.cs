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
        private readonly List<double> allTime; // Full historical data for plotting
        private readonly List<double> allProduction;
        private readonly List<double> forecastTime; // Subset for forecasting
        private readonly List<double> forecastProduction; // Smoothed subset
        private readonly string outputDir;
        private readonly int effectiveOffset;

        public class ArpsParams
        {
            public string WellName { get; set; }
            public string? ModelType { get; set; }
            public double EffectiveDate { get; set; }
            public double Qi { get; set; }
            public double Di { get; set; }
            public double B { get; set; }
        }

        private List<ArpsParams> arpsParameters = new List<ArpsParams>();

        public WellChartGenerator(string wellName, List<double> time, List<double> production, string outputDir, int effectiveOffset = 0, double forecastFraction = 1.0 / 3.0)
        {
            this.wellName = wellName;
            this.allTime = time ?? throw new ArgumentNullException(nameof(time));
            this.allProduction = production ?? throw new ArgumentNullException(nameof(production));
            this.outputDir = outputDir ?? throw new ArgumentNullException(nameof(outputDir));
            this.effectiveOffset = effectiveOffset;

            // Validate input data
            if (time.Count != production.Count || time.Count == 0)
                throw new ArgumentException("Time and production lists must have the same length and not be empty.");

            // Subset for forecasting (e.g., last 1/3)
            int pointsToUse = (int)(time.Count * forecastFraction);
            if (pointsToUse < 1) pointsToUse = 1;
            int startIndex = time.Count - pointsToUse;
            this.forecastTime = time.Skip(startIndex).ToList();
            this.forecastProduction = SmoothData(production.Skip(startIndex).ToList(), windowSize: 5); // Apply 5-point moving average
            Console.WriteLine($"Using {forecastTime.Count} of {time.Count} points for forecasting (last {forecastFraction:P0}) with smoothing");
        }

        // Simple moving average smoothing with safeguard
        private List<double> SmoothData(List<double> data, int windowSize)
        {
            var smoothed = new List<double>();
            for (int i = 0; i < data.Count; i++)
            {
                int start = Math.Max(0, i - windowSize / 2);
                int end = Math.Min(data.Count - 1, i + windowSize / 2);
                double sum = 0;
                for (int j = start; j <= end; j++)
                    sum += data[j];
                double smoothedValue = sum / (end - start + 1);
                smoothed.Add(Math.Max(smoothedValue, 0.1)); // Ensure no values below 0.1
            }
            Console.WriteLine($"Smoothed data for {wellName}: Min={smoothed.Min():F2}, Max={smoothed.Max():F2}");
            return smoothed;
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
            double historicalEndTime = forecastTime.Max(); // End of the subset used for forecasting
            Console.WriteLine($"Historical end time for {wellName}: {historicalEndTime}");
            var plotTime = Enumerable.Range((int)allTime.Min(), (int)allTime.Max() - (int)allTime.Min() + 1100 + 1).Select(t => (double)t).ToList();

            var plt = new Plot();
            // Plot full historical data
            var actualScatter = plt.Add.Scatter(allTime.ToArray(), allProduction.ToArray(), color: Colors.Blue);
            actualScatter.Label = "Actual Production";
            Console.WriteLine($"Plotted historical data for {wellName}");

            // Prophet-Like Forecast (trained on smoothed subset)
            var prophetStopwatch = Stopwatch.StartNew();
            var prophetForecaster = new ProphetLikeForecaster(forecastTime, forecastProduction);
            Console.WriteLine($"Created ProphetLikeForecaster for {wellName}");
            var prophetForecast = prophetForecaster.Forecast(plotTime, forecastTime, forecastProduction);
            prophetStopwatch.Stop();
            Console.WriteLine($"Prophet-Like Forecast Time for {wellName}: {prophetStopwatch.ElapsedMilliseconds} ms");
            Console.WriteLine($"Prophet forecast at historicalEndTime {historicalEndTime}: {prophetForecast[plotTime.IndexOf(historicalEndTime)]:F2}");

            // Hybrid Arps + Random Forest (trained on smoothed subset)
            var hybridStopwatch = Stopwatch.StartNew();
            var config = new ForecastConfig();
            Console.WriteLine($"Created ForecastConfig for {wellName}: {config}");
            HybridForecaster hybridForecaster = null;
            try
            {
                hybridForecaster = new HybridForecaster(forecastTime, forecastProduction, config);
                Console.WriteLine($"Created HybridForecaster for {wellName} with config: {config}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in HybridForecaster constructor: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                throw;
            }

            var hybridForecast = new List<float>(); // Declare outside try-catch
            try
            {
                hybridForecast = hybridForecaster.Forecast(plotTime, forecastTime, forecastProduction);
                Console.WriteLine($"Hybrid forecast completed successfully");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in HybridForecaster.Forecast: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                hybridForecast = new List<float>(prophetForecast.Select(x => (float)x)); // Fallback to Prophet forecast
            }
            hybridStopwatch.Stop();
            Console.WriteLine($"Hybrid Arps + Random Forest Forecast Time for {wellName}: {hybridStopwatch.ElapsedMilliseconds} ms");
            Console.WriteLine($"Hybrid forecast at historicalEndTime {historicalEndTime}: {hybridForecast[plotTime.IndexOf(historicalEndTime)]:F2}");

            // Prepare hybrid forecast for plotting
            var hybridForecastForPlot = new List<float>();
            foreach (var t in plotTime)
            {
                int index = plotTime.IndexOf(t);
                if (index >= 0 && index < hybridForecast.Count)
                    hybridForecastForPlot.Add(hybridForecast[index]);
                else
                    hybridForecastForPlot.Add(0.0f); // Default to 0 if index is out of range
            }
            Console.WriteLine($"Prepared hybrid forecast for plotting for {wellName}");

            // Define effective date for ARPS equivalents
            double effectiveDate = historicalEndTime + effectiveOffset;
            if (effectiveDate < forecastTime.Min()) effectiveDate = forecastTime.Min();
            Console.WriteLine($"Effective date for {wellName}: {effectiveDate}");

            // Use actual production at effectiveDate as initial Qi
            int effectiveIndex = forecastTime.IndexOf(forecastTime.LastOrDefault(t => t <= effectiveDate));
            Console.WriteLine($"Effective index for {wellName}: {effectiveIndex}, forecastTime count: {forecastTime.Count}");
            double initialQi = effectiveIndex >= 0 && effectiveIndex < forecastProduction.Count ? forecastProduction[effectiveIndex] : forecastProduction.Last();
            Console.WriteLine($"Initial Qi for {wellName} at effectiveDate {effectiveDate}: {initialQi}");
            if (initialQi < 0.1)
            {
                Console.WriteLine($"Warning: Initial Qi ({initialQi}) is below 0.1. Adjusting to 0.1.");
                initialQi = 0.1;
            }

            var prophetArpsForecaster = new SSE();
            prophetArpsForecaster.SetQi(initialQi);
            Console.WriteLine($"Set initial Qi for Prophet ArpsForecaster: {initialQi}");
            var hybridArpsForecaster = new SSE();
            hybridArpsForecaster.SetQi(initialQi);
            Console.WriteLine($"Set initial Qi for Hybrid ArpsForecaster: {initialQi}");

            // Fit ARPS for Prophet-Like with adjusted initial guesses
            double initialDi = 0.20 / 365.0; // 20% annual
            Console.WriteLine($"Calling FitHyperbolic for Prophet-Like with qiGuess={initialQi}, diGuess={initialDi}");
            prophetArpsForecaster.FitHyperbolic(forecastTime, forecastProduction, initialQi, initialDi, null);
            var prophetArpsForecast = plotTime.Select(t => (float)prophetArpsForecaster.Forecast(t, effectiveDate)).ToList();

            arpsParameters.Add(new ArpsParams
            {
                WellName = wellName,
                ModelType = "Prophet-Like ARPS",
                EffectiveDate = effectiveDate,
                Qi = prophetArpsForecaster.Qi,
                Di = prophetArpsForecaster.Di,
                B = prophetArpsForecaster.B
            });

            // Fit ARPS for Hybrid with adjusted initial guesses
            Console.WriteLine($"Calling FitHyperbolic for Hybrid with qiGuess={initialQi}, diGuess={initialDi}");
            hybridArpsForecaster.FitHyperbolic(forecastTime, forecastProduction, initialQi, initialDi, null);
            var hybridArpsForecast = plotTime.Select(t => (float)hybridArpsForecaster.Forecast(t, effectiveDate)).ToList();

            arpsParameters.Add(new ArpsParams
            {
                WellName = wellName,
                ModelType = "Hybrid ARPS",
                EffectiveDate = effectiveDate,
                Qi = hybridArpsForecaster.Qi,
                Di = hybridArpsForecaster.Di,
                B = hybridArpsForecaster.B
            });

            // Plot forecasts
            var prophetLine = plt.Add.Scatter(plotTime.ToArray(), prophetForecast.ToArray(), color: Colors.Green);
            prophetLine.Label = "Prophet-Like Forecast";
            prophetLine.MarkerSize = 0;

            var hybridLine = plt.Add.Scatter(plotTime.ToArray(), hybridForecastForPlot.ToArray(), color: Colors.Cyan);
            hybridLine.Label = "Hybrid ARPS + RF";
            hybridLine.MarkerSize = 0;

            var prophetArpsLine = plt.Add.Scatter(plotTime.ToArray(), prophetArpsForecast.ToArray(), color: Colors.Red);
            prophetArpsLine.Label = $"Prophet-Like ARPS (Qi:{prophetArpsForecaster.Qi:F2}, Di:{prophetArpsForecaster.Di * 365 * 100:F2}%, b:{prophetArpsForecaster.B:F2})";
            prophetArpsLine.MarkerSize = 0;

            var hybridArpsLine = plt.Add.Scatter(plotTime.ToArray(), hybridArpsForecast.ToArray(), color: Colors.Purple);
            hybridArpsLine.Label = $"Hybrid ARPS (Qi:{hybridArpsForecaster.Qi:F2}, Di:{hybridArpsForecaster.Di * 365 * 100:F2}%, b:{hybridArpsForecaster.B:F2})";
            hybridLine.MarkerSize = 0;

            plt.Title($"{wellName} Production Data vs Forecast");
            plt.XLabel("Time (days)");
            plt.YLabel("Production (units)");
            plt.ShowLegend(Alignment.UpperRight);
            plt.Legend.Font.Size = 10;

            string outputPath = System.IO.Path.Combine(outputDir, $"{wellName}_forecast.png");
            plt.SavePng(outputPath, 800, 600);
            Console.WriteLine($"Plot saved as '{outputPath}'");
        }

        public List<ArpsParams> GetArpsParameters() => arpsParameters;
    }
}