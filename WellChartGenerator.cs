// WellChartGenerator.cs (Corrected)
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

        public class ArpsParams
        {
            public required string WellName { get; set; }
            public string? ModelType { get; set; }
            public double EffectiveDate { get; set; }
            public double Qi { get; set; }
            public double Di { get; set; } // Daily decline rate
            public double AnnualDiPercent { get; set; } // Annual % decline (set from SSE)
            public double B { get; set; }
        }

        private List<ArpsParams> arpsParameters = new();

        public WellChartGenerator(string wellName, List<double> time, List<double> production, string outputDir, int effectiveOffset = 0)
        {
            this.wellName = wellName ?? throw new ArgumentNullException(nameof(wellName));
            this.allTime = time ?? throw new ArgumentNullException(nameof(time));
            this.allProduction = production ?? throw new ArgumentNullException(nameof(production));
            this.outputDir = outputDir ?? throw new ArgumentNullException(nameof(outputDir));
            this.effectiveOffset = effectiveOffset;

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

            int pointsToUse = (int)(allTime.Count * 1.0 / 3.0);
            int startIndex = allTime.Count - pointsToUse;
            double effectiveDate = allTime[startIndex] + effectiveOffset;
            if (effectiveDate < allTime.Min()) effectiveDate = allTime.Min();
            Console.WriteLine($"Effective date for ARPS equivalents for {wellName}: {effectiveDate}");

            var plotTime = Enumerable.Range((int)allTime.Min(), (int)allTime.Max() - (int)allTime.Min() + 1100 + 1).Select(t => (double)t).ToList();

            var plt = new Plot();
            var actualScatter = plt.Add.Scatter(allTime.ToArray(), allProduction.ToArray(), color: Colors.Blue);
            actualScatter.Label = "Actual Production";
            Console.WriteLine($"Plotted historical data for {wellName}");

            var prophetStopwatch = Stopwatch.StartNew();
            var prophetForecaster = new ProphetLikeForecaster(allTime, allProduction);
            var prophetForecast = prophetForecaster.Forecast(plotTime, allTime, allProduction);
            prophetStopwatch.Stop();
            Console.WriteLine($"Prophet-Like Forecast Time for {wellName}: {prophetStopwatch.ElapsedMilliseconds} ms");
            Console.WriteLine($"Prophet forecast at effectiveDate {effectiveDate}: {prophetForecast[plotTime.IndexOf(effectiveDate)]:F2}");

            var hybridStopwatch = Stopwatch.StartNew();
            var config = new ForecastConfig();
            Console.WriteLine($"Created ForecastConfig for {wellName}: InitialRate={config.InitialRate}, InitialDecline={config.InitialDecline}, BFactor={config.BFactor}, TerminalDecline={config.TerminalDecline}, MinB={config.MinB}, MaxB={config.MaxB}, MinDi={config.MinDi}, MaxDi={config.MaxDi}, t0={config.t0}, bFixed={config.bFixed}");
            var hybridForecaster = new HybridForecaster(allTime, allProduction, config);
            var hybridForecast = hybridForecaster.Forecast(plotTime, allTime, allProduction);
            hybridStopwatch.Stop();
            Console.WriteLine($"Hybrid Arps + Random Forest Forecast Time for {wellName}: {hybridStopwatch.ElapsedMilliseconds} ms");
            Console.WriteLine($"Hybrid forecast at effectiveDate {effectiveDate}: {hybridForecast[plotTime.IndexOf(effectiveDate)]:F2}");

            var hybridForecastForPlot = new List<float>();
            foreach (var t in plotTime)
            {
                int index = plotTime.IndexOf(t);
                if (index >= 0 && index < hybridForecast.Count)
                    hybridForecastForPlot.Add(hybridForecast[index]);
                else
                    hybridForecastForPlot.Add(0.0f);
            }
            Console.WriteLine($"Prepared hybrid forecast for plotting for {wellName}");

            var arpsFitTimes = plotTime.Where(t => t >= effectiveDate).ToList();
            var prophetFitData = prophetForecast.Where((_, i) => plotTime[i] >= effectiveDate).Select(f => (double)f).ToList();
            var hybridFitData = hybridForecast.Where((_, i) => plotTime[i] >= effectiveDate).Select(f => (double)f).ToList();

            int arpsEffectiveIndex = plotTime.IndexOf(effectiveDate);
            double prophetInitialQi = prophetForecast[arpsEffectiveIndex] > 0.1 ? prophetForecast[arpsEffectiveIndex] : 0.1;
            double hybridInitialQi = hybridForecast[arpsEffectiveIndex] > 0.1 ? hybridForecast[arpsEffectiveIndex] : 0.1;

            double EstimateDi(List<double> time, List<double> data)
            {
                if (time.Count < 2) return config.InitialDecline ?? 0.20 / 365.0;
                double deltaT = time[1] - time[0];
                double deltaQ = data[0] - data[1];
                if (deltaQ <= 0 || data[0] == 0) return config.InitialDecline ?? 0.20 / 365.0;
                return Math.Min(deltaQ / (data[0] * deltaT), config.MaxDi);
            }

            var prophetArpsForecaster = new SSE(config);
            prophetArpsForecaster.SetQi(prophetInitialQi);
            prophetArpsForecaster.SetReferenceTime(effectiveDate);
            double prophetDiGuess = EstimateDi(arpsFitTimes, prophetFitData);
            prophetArpsForecaster.FitHyperbolic(arpsFitTimes, prophetFitData, prophetInitialQi, prophetDiGuess, null);
            var prophetArpsForecast = plotTime.Select(t => (float)(t < effectiveDate ? 0 : prophetArpsForecaster.Forecast(t, effectiveDate))).ToList();

            arpsParameters.Add(new ArpsParams
            {
                WellName = wellName,
                ModelType = "Prophet-Like ARPS",
                EffectiveDate = effectiveDate,
                Qi = prophetArpsForecaster.Qi,
                Di = prophetArpsForecaster.Di,
                AnnualDiPercent = prophetArpsForecaster.AnnualDiPercent, // Set from SSE
                B = prophetArpsForecaster.B
            });

            var hybridArpsForecaster = new SSE(config);
            hybridArpsForecaster.SetQi(hybridInitialQi);
            hybridArpsForecaster.SetReferenceTime(effectiveDate);
            double hybridDiGuess = EstimateDi(arpsFitTimes, hybridFitData);
            hybridArpsForecaster.FitHyperbolic(arpsFitTimes, hybridFitData, hybridInitialQi, hybridDiGuess, null);
            var hybridArpsForecast = plotTime.Select(t => (float)(t < effectiveDate ? 0 : hybridArpsForecaster.Forecast(t, effectiveDate))).ToList();

            arpsParameters.Add(new ArpsParams
            {
                WellName = wellName,
                ModelType = "Hybrid ARPS",
                EffectiveDate = effectiveDate,
                Qi = hybridArpsForecaster.Qi,
                Di = hybridArpsForecaster.Di,
                AnnualDiPercent = hybridArpsForecaster.AnnualDiPercent, // Set from SSE
                B = hybridArpsForecaster.B
            });

            var prophetLine = plt.Add.Scatter(plotTime.ToArray(), prophetForecast.ToArray(), color: Colors.Green);
            prophetLine.Label = "Prophet-Like Forecast";
            prophetLine.MarkerSize = 0;

            var hybridLine = plt.Add.Scatter(plotTime.ToArray(), hybridForecastForPlot.ToArray(), color: Colors.Cyan);
            hybridLine.Label = "Hybrid ARPS + RF";
            hybridLine.MarkerSize = 0;

            var prophetArpsLine = plt.Add.Scatter(plotTime.ToArray(), prophetArpsForecast.ToArray(), color: Colors.Red);
            prophetArpsLine.Label = $"Prophet-Like ARPS (Qi:{prophetArpsForecaster.Qi:F2}, Di:{prophetArpsForecaster.AnnualDiPercent:F2}%, b:{prophetArpsForecaster.B:F2}, Eff Date: {effectiveDate})";
            prophetArpsLine.MarkerSize = 0;

            var hybridArpsLine = plt.Add.Scatter(plotTime.ToArray(), hybridArpsForecast.ToArray(), color: Colors.Purple);
            hybridArpsLine.Label = $"Hybrid ARPS (Qi:{hybridArpsForecaster.Qi:F2}, Di:{hybridArpsForecaster.AnnualDiPercent:F2}%, b:{hybridArpsForecaster.B:F2}, Eff Date: {effectiveDate})";
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