// Program.cs
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace ArpsForecasting
{
    class Program
    {
        static int Main(string[] args)
        {
            var argList = args?.ToList() ?? new List<string>();
            bool runHarness = argList.Any(a => string.Equals(a, "--harness", StringComparison.OrdinalIgnoreCase));
            bool runWeb = argList.Any(a => string.Equals(a, "--web", StringComparison.OrdinalIgnoreCase));
            argList.RemoveAll(a => string.Equals(a, "--harness", StringComparison.OrdinalIgnoreCase));
            argList.RemoveAll(a => string.Equals(a, "--web", StringComparison.OrdinalIgnoreCase));

            if (runHarness)
                return SampleHarness.Run(argList.ToArray());

            if (runWeb)
                return ForecastWebHost.Run(argList.ToArray());

            int phaseMode = ParsePhaseMode(argList);
            string csvPath = ResolveCsvPath(argList);
            string outputDir = ResolveOutputDir(argList, csvPath);
            var result = RunForecast(csvPath, outputDir, generatePdf: true, exportPhaseMode: phaseMode);
            return result.Success ? 0 : 1;
        }

        private static int ParsePhaseMode(List<string> args)
        {
            int phaseMode = 4;
            for (int i = args.Count - 1; i >= 0; i--)
            {
                string a = args[i];
                if (!a.StartsWith("--phase=", StringComparison.OrdinalIgnoreCase)) continue;
                if (int.TryParse(a.Substring("--phase=".Length), out int p) && (p == 0 || p == 3 || p == 4))
                    phaseMode = p;
                args.RemoveAt(i);
            }
            return phaseMode;
        }

        private static string ResolveCsvPath(List<string> args)
        {
            if (args.Count > 0) return args[0];

            string cwdPath = Path.Combine(Directory.GetCurrentDirectory(), "production_data.csv");
            if (File.Exists(cwdPath)) return cwdPath;

            return Path.Combine(AppContext.BaseDirectory, "production_data.csv");
        }

        private static string ResolveOutputDir(List<string> args, string csvPath)
        {
            if (args.Count > 1) return args[1];
            string csvDir = Path.GetDirectoryName(Path.GetFullPath(csvPath)) ?? Directory.GetCurrentDirectory();
            return Path.Combine(csvDir, "Charts");
        }

        public static ForecastRunResult RunForecast(string csvPath, string outputDir, bool generatePdf, int exportPhaseMode = 0)
        {
            csvPath = Path.GetFullPath(csvPath);
            outputDir = Path.GetFullPath(outputDir);

            if (!Directory.Exists(outputDir))
                Directory.CreateDirectory(outputDir);

            string artifactDir = Directory.GetParent(outputDir)?.FullName ?? outputDir;
            string summaryPath = Path.Combine(artifactDir, "forecast_run_summary.json");
            var messages = new List<string>();

            Console.WriteLine($"Using CSV: {csvPath}");
            Console.WriteLine($"Output charts: {outputDir}");
            var wells = LoadWellData(csvPath);
            if (!wells.Any())
            {
                const string noDataMessage = "No well data found in CSV file. Exiting.";
                Console.WriteLine(noDataMessage);
                messages.Add(noDataMessage);
                var emptyResult = CreateRunResult(
                    success: false,
                    csvPath: csvPath,
                    outputDir: outputDir,
                    arpsParametersPath: string.Empty,
                    ariesExportPath: string.Empty,
                    summaryPath: summaryPath,
                    pdfReportPath: null,
                    forecastParametersPath: string.Empty,
                    forecastParametersJsonPath: string.Empty,
                    wellCount: 0,
                    arpsParameterCount: 0,
                    forecastParameterCount: 0,
                    chartPaths: Array.Empty<string>(),
                    messages: messages);
                WriteRunSummary(emptyResult);
                return emptyResult;
            }
            Console.WriteLine($"Loaded wells: {string.Join(", ", wells.Select(w => w.Name))}");

            int effectiveOffset = 0;
            var allArpsParameters = new ConcurrentBag<WellChartGenerator.ArpsParams>();
            var allForecastRows = new ConcurrentBag<ForecastParameterRow>();
            var workerMessages = new ConcurrentBag<string>();
            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount)
            };

            Parallel.ForEach(wells, parallelOptions, well =>
            {
                var wellResult = ForecastWell(well, outputDir, effectiveOffset, exportPhaseMode);
                foreach (var row in wellResult.ParameterRows)
                    allForecastRows.Add(row);
                foreach (var arpsParam in wellResult.ArpsParameters)
                    allArpsParameters.Add(arpsParam);
                foreach (var message in wellResult.Messages)
                    workerMessages.Add(message);
            });

            foreach (var message in workerMessages.OrderBy(m => m, StringComparer.OrdinalIgnoreCase))
                messages.Add(message);

            string? pdfPath = null;
            if (generatePdf)
            {
                try
                {
                    pdfPath = PdfReportGenerator.GenerateChartDeck(outputDir, "Arps Forecast Chart Deck");
                    Console.WriteLine($"Chart deck saved to '{pdfPath}'");
                }
                catch (Exception ex)
                {
                    string message = $"PDF generation skipped: {ex.Message}";
                    messages.Add(message);
                    Console.WriteLine(message);
                }
            }

            var chartPaths = Directory.GetFiles(outputDir, "*_forecast.png", SearchOption.TopDirectoryOnly)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (chartPaths.Count == 0)
            {
                const string noChartsMessage = "No forecast chart PNG outputs were generated.";
                messages.Add(noChartsMessage);
                Console.WriteLine(noChartsMessage);
            }

            var arpsParameters = allArpsParameters
                .OrderBy(p => p.WellName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(p => p.ModelType, StringComparer.OrdinalIgnoreCase)
                .ThenBy(p => p.EffectiveDate)
                .ToList();
            if (arpsParameters.Count == 0)
            {
                const string noArpsMessage = "No ARPS parameter rows were generated.";
                messages.Add(noArpsMessage);
                Console.WriteLine(noArpsMessage);
            }

            var forecastRows = allForecastRows
                .OrderBy(r => r.WellName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.Metric, StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.ForecastFamily, StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.ModelType, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (forecastRows.Count == 0)
            {
                const string noFlatRowsMessage = "No flat forecast parameter rows were generated.";
                messages.Add(noFlatRowsMessage);
                Console.WriteLine(noFlatRowsMessage);
            }

            string arpsPath = string.Empty;
            string ariesPath = string.Empty;
            if (arpsParameters.Count > 0)
            {
                arpsPath = Path.Combine(artifactDir, "arps_parameters.csv");
                using (var writer = new StreamWriter(arpsPath))
                {
                    writer.WriteLine("WellName,ModelType,EffectiveDate,Qi,AnnualDiPercent,B");
                    foreach (var param in arpsParameters)
                        writer.WriteLine($"{EscapeCsv(param.WellName)},{EscapeCsv(param.ModelType)},{param.EffectiveDate:G7},{param.Qi:G7},{param.AnnualDiPercent:G7},{param.B:G7}");
                }
                Console.WriteLine($"ARPS parameters saved to '{arpsPath}'");

                ariesPath = Path.Combine(artifactDir, "arps_aries_phdwin.csv");
                using (var writer = new StreamWriter(ariesPath))
                {
                    writer.WriteLine("WellName,CurveName,DeclineType,EffectiveTime,Qi,NominalDiPerDay,NominalDiAnnualPercent,B");
                    foreach (var param in arpsParameters)
                    {
                        string curveName = string.IsNullOrWhiteSpace(param.ModelType) ? "ARPS" : param.ModelType!;
                        writer.WriteLine($"{EscapeCsv(param.WellName)},{EscapeCsv(curveName)},Hyperbolic,{param.EffectiveDate:G7},{param.Qi:G7},{param.Di:G9},{param.AnnualDiPercent:G7},{param.B:G7}");
                    }
                }
                Console.WriteLine($"ARIES/PHDwin export saved to '{ariesPath}'");
            }

            string forecastParametersPath = string.Empty;
            string forecastParametersJsonPath = string.Empty;
            if (forecastRows.Count > 0)
            {
                forecastParametersPath = Path.Combine(artifactDir, "forecast_parameters.csv");
                using (var writer = new StreamWriter(forecastParametersPath))
                {
                    writer.WriteLine("WellName,Metric,ForecastFamily,ModelType,EffectiveDate,Qi,Di,AnnualDiPercent,B,ValueUnit,NumeratorMetric,DenominatorMetric,LatestObservedValue,ForecastAnchorValue,Cadence,PressureUsed,SourcePointCount,ValidPointCount,Status,Message,GeneratedAtUtc");
                    foreach (var row in forecastRows)
                    {
                        writer.WriteLine(string.Join(",",
                            EscapeCsv(row.WellName),
                            EscapeCsv(row.Metric),
                            EscapeCsv(row.ForecastFamily),
                            EscapeCsv(row.ModelType),
                            FormatNullable(row.EffectiveDate),
                            FormatNullable(row.Qi),
                            FormatNullable(row.Di),
                            FormatNullable(row.AnnualDiPercent),
                            FormatNullable(row.B),
                            EscapeCsv(row.ValueUnit),
                            EscapeCsv(row.NumeratorMetric),
                            EscapeCsv(row.DenominatorMetric),
                            FormatNullable(row.LatestObservedValue),
                            FormatNullable(row.ForecastAnchorValue),
                            EscapeCsv(row.Cadence),
                            row.PressureUsed ? "true" : "false",
                            row.SourcePointCount.ToString(CultureInfo.InvariantCulture),
                            row.ValidPointCount.ToString(CultureInfo.InvariantCulture),
                            EscapeCsv(row.Status),
                            EscapeCsv(row.Message),
                            EscapeCsv(row.GeneratedAtUtc.ToString("O", CultureInfo.InvariantCulture))));
                    }
                }
                Console.WriteLine($"Flat forecast parameters saved to '{forecastParametersPath}'");

                forecastParametersJsonPath = Path.Combine(artifactDir, "forecast_parameters.json");
                var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
                File.WriteAllText(forecastParametersJsonPath, JsonSerializer.Serialize(forecastRows, jsonOptions));
                Console.WriteLine($"Flat forecast parameters JSON saved to '{forecastParametersJsonPath}'");
            }

            var result = CreateRunResult(
                success: forecastRows.Count > 0 && chartPaths.Count > 0 && arpsParameters.Count > 0,
                csvPath: csvPath,
                outputDir: outputDir,
                arpsParametersPath: arpsPath,
                ariesExportPath: ariesPath,
                summaryPath: summaryPath,
                pdfReportPath: pdfPath,
                forecastParametersPath: forecastParametersPath,
                forecastParametersJsonPath: forecastParametersJsonPath,
                wellCount: wells.Count,
                arpsParameterCount: arpsParameters.Count,
                forecastParameterCount: forecastRows.Count,
                chartPaths: chartPaths,
                messages: messages);
            WriteRunSummary(result);
            return result;
        }

        private static WellForecastResult ForecastWell(WellSeries well, string outputDir, int effectiveOffset, int exportPhaseMode)
        {
            var result = new WellForecastResult { WellName = well.Name };
            bool pressureUsed = well.Pressure.Any(p => p.HasValue);

            foreach (var phase in GetForecastPhases(well))
            {
                try
                {
                    string phaseLabel = phase.PhaseName;
                    string chartLabel = $"{well.Name} [{phaseLabel}]";
                    string outputFileStem = SanitizeFileStem($"{well.Name}_{phaseLabel}");
                    var generator = new WellChartGenerator(
                        chartLabel,
                        well.Time,
                        phase.Series,
                        outputDir,
                        effectiveOffset,
                        well.Pressure,
                        well.Cadence,
                        well.StepDays,
                        exportPhaseMode,
                        resultWellName: well.Name,
                        outputFileStem: outputFileStem,
                        yAxisLabel: GetMetricUnit(phaseLabel));
                    generator.GenerateAndSaveChart();

                    var arpsParameters = generator.GetArpsParameters();
                    result.ArpsParameters.AddRange(arpsParameters);
                    foreach (var param in arpsParameters)
                    {
                        result.ParameterRows.Add(new ForecastParameterRow
                        {
                            WellName = well.Name,
                            Metric = phaseLabel,
                            ForecastFamily = "Arps",
                            ModelType = param.ModelType ?? "ARPS",
                            EffectiveDate = param.EffectiveDate,
                            Qi = param.Qi,
                            Di = param.Di,
                            AnnualDiPercent = param.AnnualDiPercent,
                            B = param.B,
                            ValueUnit = GetMetricUnit(phaseLabel),
                            Cadence = well.Cadence.ToString(),
                            PressureUsed = pressureUsed,
                            SourcePointCount = well.Time.Count,
                            ValidPointCount = phase.Series.Count(v => v >= 0),
                            LatestObservedValue = phase.Series.Count > 0 ? phase.Series[^1] : null,
                            ForecastAnchorValue = param.Qi,
                            Status = "ok",
                            Message = string.Empty
                        });
                    }
                }
                catch (Exception ex)
                {
                    result.Messages.Add($"Phase forecast failed for {well.Name} [{phase.PhaseName}]: {ex.Message}");
                }
            }

            foreach (var ratio in RatioDefinition.Defaults)
            {
                var ratioRow = BuildRatioRow(well, ratio, pressureUsed);
                if (ratioRow is not null)
                    result.ParameterRows.Add(ratioRow);
            }

            return result;
        }

        private static IEnumerable<(string PhaseName, List<double> Series)> GetForecastPhases(WellSeries well)
        {
            foreach (string phase in new[] { "Oil", "Gas" })
            {
                if (well.TryGetPhaseSeries(phase, out var series) && series.Any(v => v > 0))
                    yield return (phase, series);
            }

            if (well.TryGetPhaseSeries("Production", out var productionSeries) && productionSeries.Any(v => v > 0))
                yield return ("Production", productionSeries);
        }

        private static ForecastParameterRow? BuildRatioRow(WellSeries well, RatioDefinition ratio, bool pressureUsed)
        {
            if (!well.TryGetPhaseSeries(ratio.NumeratorMetric, out var numerator) ||
                !well.TryGetPhaseSeries(ratio.DenominatorMetric, out var denominator))
            {
                return null;
            }

            var ratioPoints = new List<(double Time, double Value)>();
            for (int i = 0; i < well.Time.Count && i < numerator.Count && i < denominator.Count; i++)
            {
                if (denominator[i] <= 1e-6 || numerator[i] < 0)
                    continue;

                ratioPoints.Add((well.Time[i], numerator[i] / denominator[i]));
            }

            if (ratioPoints.Count < 6)
                return null;

            int trailingCount = Math.Min(6, ratioPoints.Count);
            var trailing = ratioPoints.Skip(ratioPoints.Count - trailingCount).ToList();
            double anchor = trailing.Average(p => p.Value);
            double latest = ratioPoints[^1].Value;

            return new ForecastParameterRow
            {
                WellName = well.Name,
                Metric = ratio.Name,
                ForecastFamily = "Ratio",
                ModelType = "TrailingAverage",
                EffectiveDate = trailing[0].Time,
                ValueUnit = ratio.ValueUnit,
                NumeratorMetric = ratio.NumeratorMetric,
                DenominatorMetric = ratio.DenominatorMetric,
                LatestObservedValue = latest,
                ForecastAnchorValue = anchor,
                Cadence = well.Cadence.ToString(),
                PressureUsed = pressureUsed,
                SourcePointCount = well.Time.Count,
                ValidPointCount = ratioPoints.Count,
                Status = "ok",
                Message = $"Derived from {ratio.NumeratorMetric}/{ratio.DenominatorMetric} trailing average."
            };
        }

        private static string GetMetricUnit(string metric)
        {
            return metric.ToLowerInvariant() switch
            {
                "oil" => "BOPD",
                "gas" => "MCFD",
                "water" => "BWPD",
                "wor" => "bbl/bbl",
                _ => "Rate"
            };
        }

        public static List<WellSeries> LoadWellData(string csvPath)
        {
            var wellsData = new Dictionary<string, List<RawWellRow>>(StringComparer.OrdinalIgnoreCase);

            try
            {
                using var reader = new StreamReader(csvPath);
                string? header = reader.ReadLine();
                if (string.IsNullOrWhiteSpace(header))
                    return new List<WellSeries>();

                var headerCols = header.Split(',').Select(h => h.Trim()).ToList();
                int wellIdx = FindColumnIndex(headerCols, "WellName", "Well", "Name");
                int timeIdx = FindColumnIndex(headerCols, "Time", "Day", "Month");
                int productionIdx = FindColumnIndex(headerCols, "Production", "Rate");
                int oilIdx = FindColumnIndex(headerCols, "Oil", "OilRate", "GrossOil");
                int gasIdx = FindColumnIndex(headerCols, "Gas", "GasRate", "GrossGas");
                int waterIdx = FindColumnIndex(headerCols, "Water", "WaterRate", "GrossWater");
                int pressureIdx = FindColumnIndex(headerCols, "Pressure", "ReservoirPressure", "TubingPressure");

                bool hasAnyRate = productionIdx >= 0 || oilIdx >= 0 || gasIdx >= 0 || waterIdx >= 0;
                if (wellIdx < 0 || timeIdx < 0 || !hasAnyRate)
                {
                    Console.WriteLine("CSV header must include WellName, Time, and at least one of Production, Oil, Gas, or Water.");
                    return new List<WellSeries>();
                }

                while (!reader.EndOfStream)
                {
                    var line = reader.ReadLine();
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    var values = line.Split(',');
                    if (values.Length <= Math.Max(wellIdx, timeIdx)) continue;

                    string wellName = values[wellIdx].Trim();
                    if (string.IsNullOrWhiteSpace(wellName)) continue;
                    if (!double.TryParse(values[timeIdx], NumberStyles.Float, CultureInfo.InvariantCulture, out double time) &&
                        !double.TryParse(values[timeIdx], out time))
                        continue;

                    var row = new RawWellRow
                    {
                        Time = time,
                        Production = ParseOptionalDouble(values, productionIdx),
                        Oil = ParseOptionalDouble(values, oilIdx),
                        Gas = ParseOptionalDouble(values, gasIdx),
                        Water = ParseOptionalDouble(values, waterIdx),
                        Pressure = ParseOptionalDouble(values, pressureIdx)
                    };

                    if (!wellsData.TryGetValue(wellName, out var rows))
                    {
                        rows = new List<RawWellRow>();
                        wellsData[wellName] = rows;
                    }
                    rows.Add(row);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading CSV file: {ex.Message}");
                return new List<WellSeries>();
            }

            var cleaned = new List<WellSeries>();
            foreach (var kv in wellsData)
            {
                int rawCount = kv.Value.Count;
                var grouped = kv.Value
                    .Where(x => x.Time >= 0)
                    .GroupBy(x => x.Time)
                    .Select(g => new
                    {
                        Time = g.Key,
                        Production = AverageNullable(g.Select(x => x.Production)),
                        Oil = AverageNullable(g.Select(x => x.Oil)),
                        Gas = AverageNullable(g.Select(x => x.Gas)),
                        Water = AverageNullable(g.Select(x => x.Water)),
                        Pressure = AverageNullable(g.Select(x => x.Pressure))
                    })
                    .OrderBy(x => x.Time)
                    .ToList();

                if (grouped.Count < 10)
                {
                    Console.WriteLine($"Skipping well '{kv.Key}' after cleaning: only {grouped.Count} valid rows.");
                    continue;
                }

                var cadence = DetectCadence(grouped.Select(x => x.Time).ToList());
                int window = cadence == CadenceType.Daily ? 7 : 3;
                var phaseSeries = new Dictionary<string, List<double>>(StringComparer.OrdinalIgnoreCase);

                TryAddPhaseSeries("Production", grouped.Select(x => x.Production).ToList(), grouped.Select(x => x.Time).ToList(), window, phaseSeries);
                TryAddPhaseSeries("Oil", grouped.Select(x => x.Oil).ToList(), grouped.Select(x => x.Time).ToList(), window, phaseSeries);
                TryAddPhaseSeries("Gas", grouped.Select(x => x.Gas).ToList(), grouped.Select(x => x.Time).ToList(), window, phaseSeries);
                TryAddPhaseSeries("Water", grouped.Select(x => x.Water).ToList(), grouped.Select(x => x.Time).ToList(), window, phaseSeries);

                if (phaseSeries.Count == 0)
                {
                    Console.WriteLine($"Skipping well '{kv.Key}' after cleaning: no usable phase history.");
                    continue;
                }

                string primaryPhase = phaseSeries.ContainsKey("Oil")
                    ? "Oil"
                    : phaseSeries.ContainsKey("Gas")
                        ? "Gas"
                        : phaseSeries.ContainsKey("Water")
                            ? "Water"
                            : "Production";

                Console.WriteLine(
                    $"Well '{kv.Key}': cadence={cadence}, raw={rawCount}, valid+deduped={grouped.Count}, phases={string.Join("/", phaseSeries.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))}");

                cleaned.Add(new WellSeries
                {
                    Name = kv.Key,
                    Time = grouped.Select(x => x.Time).ToList(),
                    Production = phaseSeries[primaryPhase],
                    Pressure = grouped.Select(x => x.Pressure).ToList(),
                    PhaseSeries = phaseSeries,
                    Cadence = cadence,
                    StepDays = cadence == CadenceType.Daily ? 1.0 : 30.4375
                });
            }

            return cleaned;
        }

        private static void TryAddPhaseSeries(string phaseName, List<double?> values, List<double> time, int window, Dictionary<string, List<double>> phaseSeries)
        {
            if (values.Count != time.Count)
                return;

            var normalized = values
                .Select(v => v.HasValue ? Math.Max(0, v.Value) : 0.0)
                .ToList();
            if (!normalized.Any(v => v > 0))
                return;

            var smoothed = SmoothOutliers(
                time.Zip(normalized, (t, p) => (time: t, production: p)).ToList(),
                window: window,
                madMultiplier: 4.0);
            phaseSeries[phaseName] = smoothed.Select(x => x.production).ToList();
        }

        static List<(double time, double production)> SmoothOutliers(
            List<(double time, double production)> series,
            int window,
            double madMultiplier)
        {
            if (series.Count < 5) return series;

            var result = new List<(double time, double production)>(series.Count);
            int half = Math.Max(1, window / 2);

            for (int i = 0; i < series.Count; i++)
            {
                int start = Math.Max(0, i - half);
                int end = Math.Min(series.Count - 1, i + half);
                var windowVals = series.Skip(start).Take(end - start + 1).Select(x => x.production).OrderBy(x => x).ToList();
                double median = windowVals[windowVals.Count / 2];
                var absDev = windowVals.Select(x => Math.Abs(x - median)).OrderBy(x => x).ToList();
                double mad = absDev[Math.Max(0, absDev.Count / 2)];

                double p = series[i].production;
                if (mad > 0)
                {
                    double maxDev = madMultiplier * 1.4826 * mad;
                    p = Math.Min(median + maxDev, Math.Max(median - maxDev, p));
                }

                result.Add((series[i].time, p));
            }

            return result;
        }

        static int FindColumnIndex(List<string> headers, params string[] names)
        {
            for (int i = 0; i < headers.Count; i++)
            {
                string h = headers[i].Replace(" ", "").ToLowerInvariant();
                if (names.Any(n => h == n.Replace(" ", "").ToLowerInvariant()))
                    return i;
            }
            return -1;
        }

        static CadenceType DetectCadence(List<double> time)
        {
            if (time.Count < 4) return CadenceType.Daily;
            var deltas = new List<double>();
            for (int i = 1; i < time.Count; i++)
            {
                double dt = time[i] - time[i - 1];
                if (dt > 0) deltas.Add(dt);
            }
            if (deltas.Count == 0) return CadenceType.Daily;
            deltas.Sort();
            double median = deltas[deltas.Count / 2];
            return median >= 20 ? CadenceType.Monthly : CadenceType.Daily;
        }

        private static ForecastRunResult CreateRunResult(
            bool success,
            string csvPath,
            string outputDir,
            string arpsParametersPath,
            string ariesExportPath,
            string summaryPath,
            string? pdfReportPath,
            string forecastParametersPath,
            string forecastParametersJsonPath,
            int wellCount,
            int arpsParameterCount,
            int forecastParameterCount,
            IEnumerable<string> chartPaths,
            IEnumerable<string> messages)
        {
            var chartPathList = chartPaths.ToList();
            return new ForecastRunResult
            {
                Success = success,
                CsvPath = csvPath,
                OutputDir = outputDir,
                ArpsParametersPath = arpsParametersPath,
                AriesExportPath = ariesExportPath,
                SummaryPath = summaryPath,
                PdfReportPath = pdfReportPath,
                ForecastParametersPath = forecastParametersPath,
                ForecastParametersJsonPath = forecastParametersJsonPath,
                WellCount = wellCount,
                ChartsGenerated = chartPathList.Count,
                ArpsParameterCount = arpsParameterCount,
                ForecastParameterCount = forecastParameterCount,
                ChartPaths = chartPathList,
                Messages = messages.ToList()
            };
        }

        private static void WriteRunSummary(ForecastRunResult result)
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(result.SummaryPath, JsonSerializer.Serialize(result, options));
            Console.WriteLine($"Run summary saved to '{result.SummaryPath}'");
        }

        private static string EscapeCsv(string? value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;
            bool needsQuotes = value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r');
            string escaped = value.Replace("\"", "\"\"");
            return needsQuotes ? $"\"{escaped}\"" : escaped;
        }

        private static string FormatNullable(double? value)
        {
            return value.HasValue ? value.Value.ToString("G17", CultureInfo.InvariantCulture) : string.Empty;
        }

        private static string SanitizeFileStem(string value)
        {
            var invalid = Path.GetInvalidFileNameChars();
            return new string(value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
        }

        private static double? ParseOptionalDouble(string[] values, int index)
        {
            if (index < 0 || index >= values.Length)
                return null;

            string raw = values[index].Trim();
            if (string.IsNullOrWhiteSpace(raw))
                return null;

            if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double invariantValue))
                return invariantValue;
            if (double.TryParse(raw, out double value))
                return value;
            return null;
        }

        private static double? AverageNullable(IEnumerable<double?> values)
        {
            var valid = values.Where(v => v.HasValue).Select(v => v!.Value).ToList();
            if (valid.Count == 0)
                return null;
            return valid.Average();
        }

        private sealed class RawWellRow
        {
            public double Time { get; set; }
            public double? Production { get; set; }
            public double? Oil { get; set; }
            public double? Gas { get; set; }
            public double? Water { get; set; }
            public double? Pressure { get; set; }
        }
    }
}
