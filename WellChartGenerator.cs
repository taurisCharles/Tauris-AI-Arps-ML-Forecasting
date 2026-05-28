using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using ScottPlot;

namespace ArpsForecasting
{
    public class WellChartGenerator
    {
        private readonly string wellName;
        private readonly List<double> allTime;
        private readonly List<double> allProduction;
        private readonly List<double?> allPressure;
        private readonly string outputDir;
        private readonly int effectiveOffset;
        private readonly CadenceType cadence;
        private readonly double stepDays;
        private readonly int exportPhaseMode;
        private readonly string resultWellName;
        private readonly string outputFileStem;
        private readonly string yAxisLabel;

        public class ArpsParams
        {
            public required string WellName { get; set; }
            public string? ModelType { get; set; }
            public double EffectiveDate { get; set; }
            public double Qi { get; set; }
            public double Di { get; set; }
            public double AnnualDiPercent { get; set; }
            public double B { get; set; }
        }

        private readonly List<ArpsParams> arpsParameters = new();

        public WellChartGenerator(
            string wellName,
            List<double> time,
            List<double> production,
            string outputDir,
            int effectiveOffset = 0,
            List<double?>? pressure = null,
            CadenceType cadence = CadenceType.Daily,
            double stepDays = 1.0,
            int exportPhaseMode = 0,
            string? resultWellName = null,
            string? outputFileStem = null,
            string? yAxisLabel = null)
        {
            this.wellName = wellName ?? throw new ArgumentNullException(nameof(wellName));
            this.allTime = time ?? throw new ArgumentNullException(nameof(time));
            this.allProduction = production ?? throw new ArgumentNullException(nameof(production));
            this.allPressure = pressure ?? Enumerable.Repeat<double?>(null, production.Count).ToList();
            this.outputDir = outputDir ?? throw new ArgumentNullException(nameof(outputDir));
            this.effectiveOffset = effectiveOffset;
            this.cadence = cadence;
            this.stepDays = stepDays <= 0 ? 1.0 : stepDays;
            this.exportPhaseMode = (exportPhaseMode == 3 || exportPhaseMode == 4) ? exportPhaseMode : 0;
            this.resultWellName = string.IsNullOrWhiteSpace(resultWellName) ? this.wellName : resultWellName;
            this.outputFileStem = string.IsNullOrWhiteSpace(outputFileStem) ? this.wellName : outputFileStem;
            this.yAxisLabel = string.IsNullOrWhiteSpace(yAxisLabel) ? "Production" : yAxisLabel;

            if (time.Count != production.Count || time.Count == 0)
                throw new ArgumentException("Time and production lists must have the same length and not be empty.");
            if (this.allPressure.Count < production.Count)
                this.allPressure.AddRange(Enumerable.Repeat<double?>(null, production.Count - this.allPressure.Count));
        }

        public void GenerateAndSaveChart()
        {
            if (allTime.Count < 12 || allProduction.Count < 12)
            {
                Console.WriteLine($"Skipping well {wellName}: insufficient valid data.");
                return;
            }

            Console.WriteLine($"\nProcessing well: {wellName} ({cadence}, stepDays={stepDays:F3})");

            var config = BuildCadenceConfig(stepDays);
            int horizonSteps = cadence == CadenceType.Daily ? 1100 : 60;
            int tailEvalWindow = cadence == CadenceType.Daily ? 180 : 12;

            int pointsToUse = Math.Max(5, allTime.Count / 3);
            int startIndex = Math.Max(0, allTime.Count - pointsToUse);
            double effectiveDate = allTime[startIndex] + effectiveOffset;
            int changePoint = DetectLastChangePointIndex(allProduction);
            if (changePoint > 0)
                effectiveDate = Math.Max(effectiveDate, allTime[changePoint]);
            effectiveDate = Math.Max(effectiveDate, allTime.Min());

            var plotTime = Enumerable.Range((int)allTime.Min(), (int)(allTime.Max() - allTime.Min()) + horizonSteps + 1)
                .Select(t => (double)t)
                .ToList();

            var prophetStopwatch = Stopwatch.StartNew();
            var prophetForecaster = new ProphetLikeForecaster(allTime, allProduction);
            var prophetForecast = prophetForecaster.Forecast(plotTime, allTime, allProduction);
            prophetStopwatch.Stop();

            var hybridStopwatch = Stopwatch.StartNew();
            var hybridForecaster = new HybridForecaster(allTime, allProduction, allPressure, config);
            var hybridForecast = hybridForecaster.Forecast(plotTime, allTime, allProduction, allPressure);
            hybridStopwatch.Stop();
            SmoothFutureSegment(hybridForecast, plotTime, allTime.Max(), alpha: 0.28f);

            var arpsFitTimes = plotTime.Where(t => t >= effectiveDate).ToList();
            var prophetFitData = prophetForecast.Where((_, i) => plotTime[i] >= effectiveDate).Select(f => (double)f).ToList();
            var hybridFitData = hybridForecast.Where((_, i) => plotTime[i] >= effectiveDate).Select(f => (double)f).ToList();
            int arpsEffectiveIndex = plotTime.IndexOf(effectiveDate);
            double prophetInitialQi = prophetForecast[Math.Max(0, arpsEffectiveIndex)] > 0.1 ? prophetForecast[Math.Max(0, arpsEffectiveIndex)] : 0.1;
            double hybridInitialQi = hybridForecast[Math.Max(0, arpsEffectiveIndex)] > 0.1 ? hybridForecast[Math.Max(0, arpsEffectiveIndex)] : 0.1;

            double EstimateDi(List<double> time, List<double> data)
            {
                if (time.Count < 2) return config.InitialDecline ?? 0.20 / (365.0 / stepDays);
                double deltaT = Math.Max(1e-6, time[1] - time[0]);
                double deltaQ = data[0] - data[1];
                if (deltaQ <= 0 || data[0] == 0) return config.InitialDecline ?? 0.20 / (365.0 / stepDays);
                return Math.Min(deltaQ / (data[0] * deltaT), config.MaxDi);
            }

            var prophetArpsForecaster = new SSE(config);
            prophetArpsForecaster.SetQi(prophetInitialQi);
            prophetArpsForecaster.SetReferenceTime(effectiveDate);
            prophetArpsForecaster.FitHyperbolic(arpsFitTimes, prophetFitData, prophetInitialQi, EstimateDi(arpsFitTimes, prophetFitData), null);
            var prophetArpsForecast = plotTime.Select(t => (float)(t < effectiveDate ? 0 : prophetArpsForecaster.Forecast(t, effectiveDate))).ToList();

            var hybridArpsForecaster = new SSE(config);
            hybridArpsForecaster.SetQi(hybridInitialQi);
            hybridArpsForecaster.SetReferenceTime(effectiveDate);
            hybridArpsForecaster.FitHyperbolic(arpsFitTimes, hybridFitData, hybridInitialQi, EstimateDi(arpsFitTimes, hybridFitData), null);
            var hybridArpsForecast = plotTime.Select(t => (float)(t < effectiveDate ? 0 : hybridArpsForecaster.Forecast(t, effectiveDate))).ToList();

            ApplyForecastConstraints(hybridForecast, plotTime, allTime.Max());
            ApplyForecastConstraints(prophetForecast, plotTime, allTime.Max());
            ApplyForecastConstraints(prophetArpsForecast, plotTime, allTime.Max());
            ApplyForecastConstraints(hybridArpsForecast, plotTime, allTime.Max());

            var modelForecasts = new Dictionary<string, List<float>>
            {
                ["FOR-Prophet-Like Forecast"] = prophetForecast,
                ["FOR-Hybrid ARPS + RF"] = hybridForecast,
                ["FOR-Prophet-Like ARPS"] = prophetArpsForecast,
                ["FOR-Hybrid ARPS"] = hybridArpsForecast
            };

            var holdoutStart = Math.Max(0, allTime.Count - tailEvalWindow);
            var modelScores = modelForecasts.ToDictionary(
                kv => kv.Key,
                kv => EvaluateHistoricalForecast(plotTime, kv.Value, allTime, allProduction, holdoutStart));

            var rollingScores = new Dictionary<string, ForecastMetrics>
            {
                ["FOR-Prophet-Like Forecast"] = RunRollingOriginBacktest("prophet", config),
                ["FOR-Hybrid ARPS + RF"] = RunRollingOriginBacktest("hybrid", config),
                ["FOR-Prophet-Like ARPS"] = RunRollingOriginBacktest("prophet-arps", config),
                ["FOR-Hybrid ARPS"] = RunRollingOriginBacktest("hybrid-arps", config)
            };

            var champion = rollingScores
                .OrderBy(kv => kv.Value.Wape)
                .ThenBy(kv => kv.Value.Smape)
                .First().Key;

            var (p10, p90) = BuildUncertaintyBands(modelForecasts[champion], plotTime, allTime, allProduction);
            modelForecasts["FOR-P10"] = p10;
            modelForecasts["FOR-P90"] = p90;

            Console.WriteLine($"Model timings (ms): Prophet={prophetStopwatch.ElapsedMilliseconds}, Hybrid={hybridStopwatch.ElapsedMilliseconds}");
            foreach (var score in modelScores.OrderBy(s => s.Value.Wape))
            {
                Console.WriteLine($"{score.Key}: MAE={score.Value.Mae:F2}, sMAPE={score.Value.Smape:F2}%, WAPE={score.Value.Wape:F2}% (tail)");
            }
            foreach (var score in rollingScores.OrderBy(s => s.Value.Wape))
            {
                Console.WriteLine($"{score.Key}: MAE={score.Value.Mae:F2}, sMAPE={score.Value.Smape:F2}%, WAPE={score.Value.Wape:F2}% (rolling)");
            }
            Console.WriteLine($"Champion model: {champion}");

            arpsParameters.Add(new ArpsParams
            {
                WellName = resultWellName,
                ModelType = "Prophet-Like ARPS",
                EffectiveDate = effectiveDate,
                Qi = prophetArpsForecaster.Qi,
                Di = prophetArpsForecaster.Di,
                AnnualDiPercent = ConvertToAnnualPercent(prophetArpsForecaster.Di),
                B = prophetArpsForecaster.B
            });
            arpsParameters.Add(new ArpsParams
            {
                WellName = resultWellName,
                ModelType = "Hybrid ARPS",
                EffectiveDate = effectiveDate,
                Qi = hybridArpsForecaster.Qi,
                Di = hybridArpsForecaster.Di,
                AnnualDiPercent = ConvertToAnnualPercent(hybridArpsForecaster.Di),
                B = hybridArpsForecaster.B
            });
            AddArpsExportVariants(arpsFitTimes, prophetFitData, effectiveDate, prophetInitialQi, config, EstimateDi);
            var phaseCurves = new List<(string Label, List<float> Values)>();
            if (exportPhaseMode > 0)
                phaseCurves = AddPhaseBasedArpsExports(config, plotTime);

            // Series CSV export intentionally disabled for now.

            try
            {
                var plt = new Plot();
                plt.Add.Scatter(allTime.ToArray(), allProduction.ToArray(), color: Colors.Blue).Label = "HIST-Actual";
                plt.Add.Scatter(plotTime.ToArray(), prophetForecast.ToArray(), color: Colors.Green).Label = "FOR-Prophet";
                plt.Add.Scatter(plotTime.ToArray(), hybridForecast.ToArray(), color: Colors.Cyan).Label = "FOR-Hybrid";
                plt.Add.Scatter(plotTime.ToArray(), prophetArpsForecast.ToArray(), color: Colors.Red).Label = "FOR-Prophet ARPS";
                plt.Add.Scatter(plotTime.ToArray(), hybridArpsForecast.ToArray(), color: Colors.Purple).Label = "FOR-Hybrid ARPS";
                plt.Add.Scatter(plotTime.ToArray(), p10.ToArray(), color: Colors.Gray).Label = "FOR-P10";
                plt.Add.Scatter(plotTime.ToArray(), p90.ToArray(), color: Colors.Gray).Label = "FOR-P90";
                var phaseColors = new[] { Colors.Orange, Colors.Brown, Colors.Magenta, Colors.Teal, Colors.Olive, Colors.Gold };
                for (int i = 0; i < phaseCurves.Count; i++)
                {
                    var c = phaseColors[i % phaseColors.Length];
                    plt.Add.Scatter(plotTime.ToArray(), phaseCurves[i].Values.ToArray(), color: c).Label = phaseCurves[i].Label;
                }
                plt.Title($"{wellName} ({cadence}) | Champion: {champion}");
                plt.XLabel(cadence == CadenceType.Daily ? "Time (days)" : "Time (months)");
                plt.YLabel(yAxisLabel);
                plt.ShowLegend(Alignment.UpperRight);
                plt.Legend.Font.Size = 9;

                string outputPath = Path.Combine(outputDir, $"{outputFileStem}_forecast.png");
                plt.SavePng(outputPath, 900, 650);
                Console.WriteLine($"Plot saved as '{outputPath}'");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Chart rendering skipped for '{wellName}': {ex.Message}");
            }
        }

        public List<ArpsParams> GetArpsParameters() => arpsParameters;

        private ForecastConfig BuildCadenceConfig(double cadenceStepDays)
        {
            var config = new ForecastConfig();
            double scale = cadenceStepDays;
            config.MinDi = SSE.MinDi * scale;
            config.MaxDi = SSE.MaxDi * scale;
            config.TerminalDecline = (config.TerminalDecline ?? 0.0005) * scale;
            config.InitialDecline = (config.InitialDecline ?? 0.005) * scale;
            return config;
        }

        private static void ApplyForecastConstraints(List<float> forecast, List<double> plotTime, double lastHistoricalTime)
        {
            float? previousFuture = null;
            float? lastHistorical = null;
            for (int i = 0; i < forecast.Count; i++)
            {
                if (plotTime[i] <= lastHistoricalTime)
                    lastHistorical = Math.Max(0, forecast[i]);
            }

            for (int i = 0; i < forecast.Count; i++)
            {
                if (plotTime[i] <= lastHistoricalTime) continue;
                float value = Math.Max(0, forecast[i]);
                if (previousFuture.HasValue)
                {
                    float maxAllowed = previousFuture.Value * 1.01f;
                    if (value > maxAllowed) value = maxAllowed;
                    if (value < 0) value = 0;
                }
                else if (lastHistorical.HasValue)
                {
                    // Prevent a large boundary jump at forecast start.
                    float maxFirst = lastHistorical.Value * 1.01f;
                    if (value > maxFirst) value = maxFirst;
                }
                forecast[i] = value;
                previousFuture = value;
            }
        }

        private void SmoothFutureSegment(List<float> series, List<double> plotTime, double lastHistoricalTime, float alpha)
        {
            alpha = Math.Max(0.05f, Math.Min(0.95f, alpha));
            int firstFuture = plotTime.FindIndex(t => t > lastHistoricalTime);
            if (firstFuture <= 0 || firstFuture >= series.Count) return;

            float prev = Math.Max(0, series[firstFuture - 1]);
            for (int i = firstFuture; i < series.Count; i++)
            {
                float raw = Math.Max(0, series[i]);
                float smooth = alpha * raw + (1 - alpha) * prev;
                series[i] = smooth;
                prev = smooth;
            }
        }

        private void AddArpsExportVariants(
            List<double> arpsFitTimes,
            List<double> targetData,
            double effectiveDate,
            double initialQi,
            ForecastConfig config,
            Func<List<double>, List<double>, double> estimateDi)
        {
            var bCandidates = new[] { 0.5, 1.0, 1.5 }
                .Where(b => b >= config.MinB && b <= config.MaxB);

            foreach (double b in bCandidates)
            {
                try
                {
                    var fit = new SSE(config);
                    fit.SetQi(initialQi);
                    fit.SetReferenceTime(effectiveDate);
                    fit.FitHyperbolic(arpsFitTimes, targetData, initialQi, estimateDi(arpsFitTimes, targetData), bFixed: b);
                    arpsParameters.Add(new ArpsParams
                    {
                        WellName = resultWellName,
                        ModelType = $"Export ARPS b={b:0.0}",
                        EffectiveDate = effectiveDate,
                        Qi = fit.Qi,
                        Di = fit.Di,
                        AnnualDiPercent = ConvertToAnnualPercent(fit.Di),
                        B = fit.B
                    });
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Skipping ARPS export variant b={b:0.0} for {wellName}: {ex.Message}");
                }
            }
        }

        private List<(string Label, List<float> Values)> AddPhaseBasedArpsExports(ForecastConfig config, List<double> plotTime)
        {
            var curves = new List<(string Label, List<float> Values)>();
            if (allTime.Count < 30 || allProduction.Count < 30) return curves;
            int peakIdx = FindPeakIndexInWindow(30, 60);
            if (peakIdx < 0) return curves;

            int holdDays = exportPhaseMode == 4 ? 30 : 15;
            int declineStartIdx = peakIdx + Math.Max(1, (int)Math.Round(holdDays / stepDays));
            if (declineStartIdx >= allTime.Count - 10) declineStartIdx = Math.Max(peakIdx + 1, allTime.Count / 3);
            if (declineStartIdx >= allTime.Count - 10) return curves;

            var declineTime = allTime.Skip(declineStartIdx).ToList();
            var declineRate = allProduction.Skip(declineStartIdx).ToList();
            if (declineTime.Count < 10) return curves;

            double effectiveDate = declineTime.First();
            double qi = Math.Max(0.1, allProduction[peakIdx]);
            double diGuess = EstimateDeclineGuess(declineTime, declineRate, config);
            foreach (double b in new[] { 0.5, 1.0, 1.5 }.Where(v => v >= config.MinB && v <= config.MaxB))
            {
                try
                {
                    var fit = new SSE(config);
                    fit.SetQi(qi);
                    fit.SetReferenceTime(effectiveDate);
                    fit.FitHyperbolic(declineTime, declineRate, qi, diGuess, bFixed: b);
                    arpsParameters.Add(new ArpsParams
                    {
                        WellName = resultWellName,
                        ModelType = $"Phase{exportPhaseMode} Primary b={b:0.0}",
                        EffectiveDate = effectiveDate,
                        Qi = fit.Qi,
                        Di = fit.Di,
                        AnnualDiPercent = ConvertToAnnualPercent(fit.Di),
                        B = fit.B
                    });
                    var curve = plotTime.Select(t => (float)(t < effectiveDate ? 0 : fit.Forecast(t, effectiveDate))).ToList();
                    ApplyForecastConstraints(curve, plotTime, allTime.Max());
                    curves.Add(($"FOR-Phase{exportPhaseMode} Primary b={b:0.0}", curve));
                }
                catch
                {
                    // Skip failed phase variant fit for this well/b.
                }
            }

            if (exportPhaseMode == 4)
            {
                int tailWindow = Math.Max(90, (int)Math.Round(180 / stepDays));
                int tailStartIdx = Math.Max(declineStartIdx + 5, allTime.Count - tailWindow);
                if (tailStartIdx < allTime.Count - 10)
                {
                    var tailTime = allTime.Skip(tailStartIdx).ToList();
                    var tailRate = allProduction.Skip(tailStartIdx).ToList();
                    double tailQi = Math.Max(0.1, tailRate.First());
                    double tailDiGuess = EstimateDeclineGuess(tailTime, tailRate, config);
                    foreach (double b in new[] { 0.5, 1.0, 1.5 }.Where(v => v >= config.MinB && v <= config.MaxB))
                    {
                        try
                        {
                            var tailFit = new SSE(config);
                            tailFit.SetQi(tailQi);
                            tailFit.SetReferenceTime(tailTime.First());
                            tailFit.FitHyperbolic(tailTime, tailRate, tailQi, tailDiGuess, bFixed: b);
                            arpsParameters.Add(new ArpsParams
                            {
                                WellName = resultWellName,
                                ModelType = $"Phase4 Tail b={b:0.0}",
                                EffectiveDate = tailTime.First(),
                                Qi = tailFit.Qi,
                                Di = tailFit.Di,
                                AnnualDiPercent = ConvertToAnnualPercent(tailFit.Di),
                                B = tailFit.B
                            });
                            var tailCurve = plotTime.Select(t => (float)(t < tailTime.First() ? 0 : tailFit.Forecast(t, tailTime.First()))).ToList();
                            ApplyForecastConstraints(tailCurve, plotTime, allTime.Max());
                            curves.Add(($"FOR-Phase4 Tail b={b:0.0}", tailCurve));
                        }
                        catch
                        {
                            // Skip failed tail variant fit for this well/b.
                        }
                    }
                }
            }
            return curves;
        }

        private int FindPeakIndexInWindow(int minDay, int maxDay)
        {
            double t0 = allTime.First();
            var candidates = Enumerable.Range(0, allTime.Count)
                .Where(i =>
                {
                    double days = (allTime[i] - t0) * stepDays;
                    return days >= minDay && days <= maxDay;
                })
                .ToList();
            if (candidates.Count == 0)
            {
                candidates = Enumerable.Range(0, allTime.Count)
                    .Where(i => (allTime[i] - t0) * stepDays <= 90)
                    .ToList();
            }
            if (candidates.Count == 0) return -1;
            return candidates.OrderByDescending(i => allProduction[i]).First();
        }

        private static double EstimateDeclineGuess(List<double> time, List<double> rate, ForecastConfig config)
        {
            if (time.Count < 2 || rate.Count < 2 || rate[0] <= 0) return config.InitialDecline ?? config.MinDi;
            double dt = Math.Max(1e-6, time[1] - time[0]);
            double dq = rate[0] - rate[1];
            if (dq <= 0) return config.InitialDecline ?? config.MinDi;
            double di = dq / (rate[0] * dt);
            return Math.Max(config.MinDi, Math.Min(config.MaxDi, di));
        }

        private int DetectLastChangePointIndex(List<double> production)
        {
            if (production.Count < 40) return 0;
            int window = Math.Max(5, production.Count / 40);
            for (int i = production.Count - window - 1; i >= window; i--)
            {
                double before = production.Skip(i - window).Take(window).Average();
                double after = production.Skip(i).Take(window).Average();
                if (before <= 0) continue;
                double ratio = after / before;
                if (ratio > 1.5 || ratio < 0.65) return i;
            }
            return 0;
        }

        private (List<float> p10, List<float> p90) BuildUncertaintyBands(
            List<float> baseForecast,
            List<double> plotTime,
            List<double> histTime,
            List<double> histProduction)
        {
            var actualByTime = new Dictionary<double, double>();
            for (int i = 0; i < histTime.Count; i++) actualByTime[histTime[i]] = histProduction[i];

            var residualRatios = new List<double>();
            var matchedRatios = new List<double>();
            for (int i = 0; i < plotTime.Count && i < baseForecast.Count; i++)
            {
                if (!actualByTime.TryGetValue(plotTime[i], out double actual) || actual <= 1e-6) continue;
                double ratio = baseForecast[i] / actual;
                if (double.IsFinite(ratio) && ratio > 0) matchedRatios.Add(ratio);
            }

            // Use the trailing window so old regime errors don't distort current bands.
            int trailingCount = Math.Min(matchedRatios.Count, cadence == CadenceType.Daily ? 180 : 18);
            if (trailingCount > 0)
            {
                residualRatios.AddRange(matchedRatios.Skip(matchedRatios.Count - trailingCount));
            }

            if (residualRatios.Count < 10)
            {
                return (baseForecast.Select(v => v * 0.85f).ToList(), baseForecast.Select(v => v * 1.15f).ToList());
            }

            // Winsorize to suppress near-zero denominator blowups.
            for (int i = 0; i < residualRatios.Count; i++)
                residualRatios[i] = Math.Max(0.6, Math.Min(1.6, residualRatios[i]));

            residualRatios.Sort();
            double q10 = Quantile(residualRatios, 0.10);
            double q90 = Quantile(residualRatios, 0.90);
            q10 = Math.Max(0.70, Math.Min(0.98, q10));
            q90 = Math.Max(Math.Max(q10, 1.02), Math.Min(1.35, q90));

            var p10 = baseForecast.Select(v => (float)Math.Max(0, v * q10)).ToList();
            var p90 = baseForecast.Select(v => (float)Math.Max(0, v * q90)).ToList();

            double lastHist = histTime.Count > 0 ? histTime.Max() : plotTime.Max();
            ApplyForecastConstraints(p10, plotTime, lastHist);
            ApplyForecastConstraints(p90, plotTime, lastHist);

            for (int i = 0; i < Math.Min(baseForecast.Count, Math.Min(p10.Count, p90.Count)); i++)
            {
                if (p10[i] > baseForecast[i]) p10[i] = baseForecast[i];
                if (p90[i] < baseForecast[i]) p90[i] = baseForecast[i];
            }

            return (p10, p90);
        }

        private static double Quantile(List<double> sortedValues, double p)
        {
            if (sortedValues.Count == 0) return 1;
            if (p <= 0) return sortedValues.First();
            if (p >= 1) return sortedValues.Last();
            double pos = (sortedValues.Count - 1) * p;
            int lo = (int)Math.Floor(pos);
            int hi = (int)Math.Ceiling(pos);
            if (lo == hi) return sortedValues[lo];
            double t = pos - lo;
            return sortedValues[lo] * (1 - t) + sortedValues[hi] * t;
        }

        private static void ExportSeriesCsv(
            string csvPath,
            List<double> historicalTime,
            List<double> historicalProduction,
            List<double> plotTime,
            Dictionary<string, List<float>> forecastSeries)
        {
            using var writer = new StreamWriter(csvPath);
            writer.WriteLine("Series,Time,Production");

            for (int i = 0; i < historicalTime.Count; i++)
                writer.WriteLine($"HIST-Actual Production,{historicalTime[i]:G17},{historicalProduction[i]:G17}");

            foreach (var kv in forecastSeries)
            {
                string seriesName = kv.Key.StartsWith("FOR-") ? kv.Key : $"FOR-{kv.Key}";
                WriteForecastSeries(writer, seriesName, plotTime, kv.Value);
            }
        }

        private static void WriteForecastSeries(StreamWriter writer, string seriesName, List<double> time, List<float> values)
        {
            int n = Math.Min(time.Count, values.Count);
            for (int i = 0; i < n; i++)
                writer.WriteLine($"{seriesName},{time[i]:G17},{values[i]:G9}");
        }

        private static ForecastMetrics EvaluateHistoricalForecast(
            List<double> plotTime,
            List<float> forecast,
            List<double> actualTime,
            List<double> actualProduction,
            int startActualIndex = 0)
        {
            var byTime = new Dictionary<double, double>();
            for (int i = 0; i < plotTime.Count && i < forecast.Count; i++)
                byTime[plotTime[i]] = forecast[i];

            double absErrSum = 0;
            double apeSum = 0;
            double smapeSum = 0;
            double sqErrSum = 0;
            double absActualSum = 0;
            int n = 0;
            int nApe = 0;

            for (int i = Math.Max(0, startActualIndex); i < actualTime.Count; i++)
            {
                if (!byTime.TryGetValue(actualTime[i], out double pred)) continue;
                double actual = actualProduction[i];
                double err = pred - actual;
                absErrSum += Math.Abs(err);
                sqErrSum += err * err;
                absActualSum += Math.Abs(actual);
                n++;
                if (actual > 1e-6)
                {
                    apeSum += Math.Abs(err) / actual;
                    nApe++;
                }
                double denom = Math.Abs(actual) + Math.Abs(pred);
                if (denom > 1e-6) smapeSum += 2.0 * Math.Abs(err) / denom;
            }

            return new ForecastMetrics
            {
                N = n,
                Mae = n > 0 ? absErrSum / n : 0,
                Rmse = n > 0 ? Math.Sqrt(sqErrSum / n) : 0,
                Mape = nApe > 0 ? (apeSum / nApe) * 100.0 : 0,
                Smape = n > 0 ? (smapeSum / n) * 100.0 : 0,
                Wape = absActualSum > 1e-6 ? (absErrSum / absActualSum) * 100.0 : 0
            };
        }

        private ForecastMetrics RunRollingOriginBacktest(string modelKey, ForecastConfig config)
        {
            int minTrain = Math.Max(12, cadence == CadenceType.Daily ? 90 : 6);
            int start = Math.Max(minTrain, allTime.Count - (cadence == CadenceType.Daily ? 180 : 18));
            var preds = new List<double>();
            var actuals = new List<double>();

            for (int i = start; i < allTime.Count - 1; i++)
            {
                var trainTime = allTime.Take(i + 1).ToList();
                var trainProd = allProduction.Take(i + 1).ToList();
                var trainPressure = allPressure.Take(i + 1).ToList();
                double tNext = allTime[i + 1];
                double actualNext = allProduction[i + 1];

                if (TryPredictNext(modelKey, trainTime, trainProd, trainPressure, tNext, config, out double pred))
                {
                    preds.Add(Math.Max(0, pred));
                    actuals.Add(Math.Max(0, actualNext));
                }
            }

            if (preds.Count == 0)
                return new ForecastMetrics();

            var idx = Enumerable.Range(0, preds.Count).Select(x => (double)x).ToList();
            return EvaluateHistoricalForecast(
                idx,
                preds.Select(v => (float)v).ToList(),
                idx,
                actuals,
                0);
        }

        private bool TryPredictNext(
            string modelKey,
            List<double> trainTime,
            List<double> trainProd,
            List<double?> trainPressure,
            double tNext,
            ForecastConfig config,
            out double prediction)
        {
            prediction = 0;
            if (trainTime.Count < 8 || trainProd.Count < 8) return false;

            try
            {
                if (modelKey == "prophet")
                {
                    var m = new ProphetLikeForecaster(trainTime, trainProd);
                    prediction = m.Forecast(new List<double> { tNext }, trainTime, trainProd)[0];
                    return true;
                }

                if (modelKey == "hybrid")
                {
                    if (trainTime.Count < config.MinTrainingIndex + config.NumLags + 1) return false;
                    var m = new HybridForecaster(trainTime, trainProd, trainPressure, config);
                    prediction = m.Forecast(new List<double> { tNext }, trainTime, trainProd, trainPressure)[0];
                    return true;
                }

                if (modelKey == "prophet-arps" || modelKey == "hybrid-arps")
                {
                    int cp = DetectLastChangePointIndex(trainProd);
                    var tFit = trainTime.Skip(cp).ToList();
                    var qFit = trainProd.Skip(cp).ToList();
                    if (tFit.Count < 6) return false;

                    var arps = new SSE(config);
                    arps.SetReferenceTime(tFit.First());
                    double qi = Math.Max(0.1, qFit.First());
                    double di = Math.Max(config.MinDi, Math.Min(config.MaxDi, config.InitialDecline ?? config.MinDi));
                    arps.FitHyperbolicToExponential(tFit, qFit, qi, di, config.BFactor ?? 0.8, config.TerminalDecline ?? config.MinDi);
                    prediction = arps.Forecast(tNext, trainTime.Last());
                    return true;
                }
            }
            catch
            {
                return false;
            }

            return false;
        }

        private double ConvertToAnnualPercent(double diPerCadence)
        {
            double diPerDay = diPerCadence / stepDays;
            return (1 - Math.Exp(-diPerDay * 365.0)) * 100;
        }

        private sealed class ForecastMetrics
        {
            public int N { get; set; }
            public double Mae { get; set; }
            public double Rmse { get; set; }
            public double Mape { get; set; }
            public double Smape { get; set; }
            public double Wape { get; set; }
        }
    }
}
