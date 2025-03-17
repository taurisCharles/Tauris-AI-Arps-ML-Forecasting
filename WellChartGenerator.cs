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
        private readonly List<double> allTime; // Full historical data for plotting and training
        private readonly List<double> allProduction;
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

        public WellChartGenerator(string wellName, List<double> time, List<double> production, string outputDir, int effectiveOffset = 0)
        {
            this.wellName = wellName;
            this.allTime = time ?? throw new ArgumentNullException(nameof(time));
            this.allProduction = production ?? throw new ArgumentNullException(nameof(production));
            this.outputDir = outputDir ?? throw new ArgumentNullException(nameof(outputDir));
            this.effectiveOffset = effectiveOffset;

            // Validate input data
            if (time.Count != production.Count || time.Count == 0)
                throw new ArgumentException("Time and production lists must have the same length and not be empty.");
        }

        public void GenerateAndSaveChart()
        {
            if (allTime.Count == 0 || allProduction.Count == 0)
            {
                Console.WriteLine($"Skipping well {wellName}: No valid data.");
                return;
            }

            Console.WriteLine($"\nProcessing well: {wellName}");

            // Define effective date for ARPS equivalents (last 1/3 of data)
            int pointsToUse = (int)(allTime.Count * 1.0 / 3.0);
            int startIndex = allTime.Count - pointsToUse;
            double effectiveDate = allTime[startIndex] + effectiveOffset;
            if (effectiveDate < allTime.Min()) effectiveDate = allTime.Min();
            Console.WriteLine($"Effective date for ARPS equivalents for {wellName}: {effectiveDate}");

            var plotTime = Enumerable.Range((int)allTime.Min(), (int)allTime.Max() - (int)allTime.Min() + 1100 + 1).Select(t => (double)t).ToList();

            var plt = new Plot();
            // Plot full historical data
            var actualScatter = plt.Add.Scatter(allTime.ToArray(), allProduction.ToArray(), color: Colors.Blue);
            actualScatter.Label = "Actual Production";
            Console.WriteLine($"Plotted historical data for {wellName}");

            // Prophet-Like Forecast (trained on full dataset)
            var prophetStopwatch = Stopwatch.StartNew();
            var prophetForecaster = new ProphetLikeForecaster(allTime, allProduction); // Use full dataset
            Console.WriteLine($"Created ProphetLikeForecaster for {wellName} with full dataset");
            var prophetForecast = prophetForecaster.Forecast(plotTime, allTime, allProduction);
            prophetStopwatch.Stop();
            Console.WriteLine($"Prophet-Like Forecast Time for {wellName}: {prophetStopwatch.ElapsedMilliseconds} ms");
            Console.WriteLine($"Prophet forecast at effectiveDate {effectiveDate}: {prophetForecast[plotTime.IndexOf(effectiveDate)]:F2}");

            // Hybrid Arps + Random Forest (trained on full dataset)
            var hybridStopwatch = Stopwatch.StartNew();
            var config = new ForecastConfig();
            Console.WriteLine($"Created ForecastConfig for {wellName}: {config}");
            HybridForecaster hybridForecaster = null;
            try
            {
                hybridForecaster = new HybridForecaster(allTime, allProduction, config); // Use full dataset
                Console.WriteLine($"Created HybridForecaster for {wellName} with full dataset and config: {config}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in HybridForecaster constructor: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                throw;
            }

            var hybridForecast = new List<float>();
            try
            {
                hybridForecast = hybridForecaster.Forecast(plotTime, allTime, allProduction);
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
            Console.WriteLine($"Hybrid forecast at effectiveDate {effectiveDate}: {hybridForecast[plotTime.IndexOf(effectiveDate)]:F2}");

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

            // Prepare data for ARPS equivalents (subset of historical data starting from effectiveDate)
            var arpsFitTimes = allTime.Where(t => t >= effectiveDate).ToList();
            var arpsFitData = allProduction.Where((p, i) => allTime[i] >= effectiveDate).ToList();

            // Use historical production at effectiveDate as initial Qi
            int arpsEffectiveIndex = allTime.IndexOf(allTime.Last(t => t <= effectiveDate));
            double prophetInitialQi = arpsEffectiveIndex >= 0 && arpsEffectiveIndex < allProduction.Count ? allProduction[arpsEffectiveIndex] : allProduction.Last();
            double hybridInitialQi = arpsEffectiveIndex >= 0 && arpsEffectiveIndex < allProduction.Count ? allProduction[arpsEffectiveIndex] : allProduction.Last();
            if (prophetInitialQi < 0.1)
            {
                Console.WriteLine($"Warning: Prophet Initial Qi ({prophetInitialQi}) is below 0.1. Adjusting to 0.1.");
                prophetInitialQi = 0.1;
            }
            if (hybridInitialQi < 0.1)
            {
                Console.WriteLine($"Warning: Hybrid Initial Qi ({hybridInitialQi}) is below 0.1. Adjusting to 0.1.");
                hybridInitialQi = 0.1;
            }
            Console.WriteLine($"Prophet Initial Qi for ARPS at effectiveDate {effectiveDate}: {prophetInitialQi}");
            Console.WriteLine($"Hybrid Initial Qi for ARPS at effectiveDate {effectiveDate}: {hybridInitialQi}");

            // Fit ARPS for Prophet-Like forecast starting from effectiveDate
            var prophetArpsForecaster = new SSE();
            prophetArpsForecaster.SetQi(prophetInitialQi);
            prophetArpsForecaster.SetReferenceTime(effectiveDate);
            Console.WriteLine($"Set initial Qi for Prophet ArpsForecaster: {prophetInitialQi}");
            double initialDi = 0.20 / 365.0; // 20% annual for more reasonable decline
            Console.WriteLine($"Calling FitHyperbolic for Prophet-Like ARPS with qiGuess={prophetInitialQi}, diGuess={initialDi}");
            prophetArpsForecaster.FitHyperbolic(arpsFitTimes, arpsFitData, prophetInitialQi, initialDi, null);
            var prophetArpsForecast = plotTime.Select(t => (float)(t < effectiveDate ? 0 : prophetArpsForecaster.Forecast(t, effectiveDate))).ToList();

            arpsParameters.Add(new ArpsParams
            {
                WellName = wellName,
                ModelType = "Prophet-Like ARPS",
                EffectiveDate = effectiveDate,
                Qi = prophetArpsForecaster.Qi,
                Di = prophetArpsForecaster.Di,
                B = prophetArpsForecaster.B
            });

            // Fit ARPS for Hybrid forecast starting from effectiveDate
            var hybridArpsForecaster = new SSE();
            hybridArpsForecaster.SetQi(hybridInitialQi);
            hybridArpsForecaster.SetReferenceTime(effectiveDate);
            Console.WriteLine($"Set initial Qi for Hybrid ArpsForecaster: {hybridInitialQi}");
            Console.WriteLine($"Calling FitHyperbolic for Hybrid ARPS with qiGuess={hybridInitialQi}, diGuess={initialDi}");
            hybridArpsForecaster.FitHyperbolic(arpsFitTimes, arpsFitData, hybridInitialQi, initialDi, null);
            var hybridArpsForecast = plotTime.Select(t => (float)(t < effectiveDate ? 0 : hybridArpsForecaster.Forecast(t, effectiveDate))).ToList();

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
            prophetArpsLine.Label = $"Prophet-Like ARPS (Qi:{prophetArpsForecaster.Qi:F2}, Di:{prophetArpsForecaster.Di * 365 * 100:F2}%, b:{prophetArpsForecaster.B:F2}, Eff Date: {effectiveDate})";
            prophetArpsLine.MarkerSize = 0;

            var hybridArpsLine = plt.Add.Scatter(plotTime.ToArray(), hybridArpsForecast.ToArray(), color: Colors.Purple);
            hybridArpsLine.Label = $"Hybrid ARPS (Qi:{hybridArpsForecaster.Qi:F2}, Di:{hybridArpsForecaster.Di * 365 * 100:F2}%, b:{hybridArpsForecaster.B:F2}, Eff Date: {effectiveDate})";
            hybridArpsLine.MarkerSize = 0;

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