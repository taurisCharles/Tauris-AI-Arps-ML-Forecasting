using System;
using System.IO;
using System.Linq;

namespace ArpsForecasting
{
    public static class SampleHarness
    {
        public static int Run(string[] args)
        {
            var positionals = args.Where(a => !a.StartsWith("--", StringComparison.OrdinalIgnoreCase)).ToArray();
            string cwd = Directory.GetCurrentDirectory();
            string harnessRoot = Path.Combine(cwd, "harness");
            string harnessInputDir = Path.Combine(harnessRoot, "input");
            string harnessOutputDir = Path.Combine(harnessRoot, "output");
            string harnessChartsDir = Path.Combine(harnessOutputDir, "Charts");

            Directory.CreateDirectory(harnessInputDir);
            Directory.CreateDirectory(harnessOutputDir);

            string defaultCsv = Path.Combine(harnessInputDir, "production_data.csv");
            SeedDefaultInputIfMissing(defaultCsv, cwd);

            string csvPath = positionals.Length > 0
                ? Path.GetFullPath(positionals[0])
                : defaultCsv;

            string outputDir = positionals.Length > 1
                ? Path.GetFullPath(positionals[1])
                : harnessChartsDir;
            int phaseMode = ParsePhaseMode(args);

            Console.WriteLine("=== ArpsForecasting Sample Harness ===");
            Console.WriteLine($"CSV: {csvPath}");
            Console.WriteLine($"OutputDir: {outputDir}");

            if (!File.Exists(csvPath))
            {
                Console.WriteLine("HARNESS FAIL: sample CSV not found.");
                return 2;
            }

            CleanHarnessOutputs(outputDir);

            var wells = Program.LoadWellData(csvPath);
            if (wells.Count == 0)
            {
                Console.WriteLine("HARNESS FAIL: no valid wells parsed from sample CSV.");
                return 3;
            }

            var nonMonotonic = wells
                .Where(w => w.Time.Zip(w.Time.Skip(1), (a, b) => b >= a).Any(ok => !ok))
                .Select(w => w.Name)
                .ToList();
            if (nonMonotonic.Count > 0)
            {
                Console.WriteLine($"HARNESS FAIL: non-monotonic time series for wells: {string.Join(", ", nonMonotonic)}");
                return 4;
            }

            var run = Program.RunForecast(csvPath, outputDir, generatePdf: false, exportPhaseMode: phaseMode);
            if (!run.Success)
            {
                Console.WriteLine("HARNESS FAIL: forecast run returned unsuccessful status.");
                return 5;
            }

            if (run.ChartsGenerated == 0 || run.ChartPaths.Count == 0)
            {
                Console.WriteLine("HARNESS FAIL: no forecast chart PNG outputs were generated.");
                return 6;
            }

            if (run.ChartPaths.Any(path => !File.Exists(path)))
            {
                Console.WriteLine("HARNESS FAIL: one or more reported chart paths do not exist.");
                return 9;
            }

            if (string.IsNullOrWhiteSpace(run.ArpsParametersPath) || !File.Exists(run.ArpsParametersPath))
            {
                Console.WriteLine("HARNESS FAIL: arps_parameters.csv was not generated.");
                return 7;
            }

            if (string.IsNullOrWhiteSpace(run.AriesExportPath) || !File.Exists(run.AriesExportPath))
            {
                Console.WriteLine("HARNESS FAIL: arps_aries_phdwin.csv was not generated.");
                return 10;
            }

            if (string.IsNullOrWhiteSpace(run.SummaryPath) || !File.Exists(run.SummaryPath))
            {
                Console.WriteLine("HARNESS FAIL: forecast_run_summary.json was not generated.");
                return 11;
            }

            if (string.IsNullOrWhiteSpace(run.ForecastParametersPath) || !File.Exists(run.ForecastParametersPath))
            {
                Console.WriteLine("HARNESS FAIL: forecast_parameters.csv was not generated.");
                return 12;
            }

            int lineCount = File.ReadLines(run.ArpsParametersPath).Count();
            if (lineCount < 2)
            {
                Console.WriteLine("HARNESS FAIL: arps_parameters.csv has no model rows.");
                return 8;
            }

            int flatLineCount = File.ReadLines(run.ForecastParametersPath).Count();
            if (flatLineCount < 2)
            {
                Console.WriteLine("HARNESS FAIL: forecast_parameters.csv has no rows.");
                return 13;
            }

            Console.WriteLine("HARNESS PASS");
            Console.WriteLine($"Wells parsed: {wells.Count}");
            Console.WriteLine($"Charts generated: {run.ChartsGenerated}");
            Console.WriteLine($"ARPS rows: {Math.Max(0, lineCount - 1)}");
            Console.WriteLine($"Flat rows: {Math.Max(0, flatLineCount - 1)}");
            Console.WriteLine($"ARPS file: {run.ArpsParametersPath}");
            Console.WriteLine($"Flat file: {run.ForecastParametersPath}");
            Console.WriteLine($"Summary file: {run.SummaryPath}");

            return 0;
        }

        private static void CleanHarnessOutputs(string outputDir)
        {
            if (Directory.Exists(outputDir))
            {
                foreach (var file in Directory.GetFiles(outputDir))
                    File.Delete(file);

                foreach (var dir in Directory.GetDirectories(outputDir))
                    Directory.Delete(dir, recursive: true);
            }

            string artifactDir = Directory.GetParent(Path.GetFullPath(outputDir))?.FullName ?? Path.GetFullPath(outputDir);
            foreach (string artifactName in new[]
            {
                "arps_parameters.csv",
                "arps_aries_phdwin.csv",
                "forecast_parameters.csv",
                "forecast_parameters.json",
                "forecast_run_summary.json",
                "forecast-charts.pdf"
            })
            {
                string artifactPath = Path.Combine(artifactDir, artifactName);
                if (File.Exists(artifactPath))
                    File.Delete(artifactPath);
            }
        }

        private static void SeedDefaultInputIfMissing(string harnessCsvPath, string cwd)
        {
            if (File.Exists(harnessCsvPath)) return;

            string rootSample = Path.Combine(cwd, "production_data.csv");
            if (!File.Exists(rootSample)) return;

            File.Copy(rootSample, harnessCsvPath, overwrite: false);
            Console.WriteLine($"Seeded harness input: {harnessCsvPath}");
        }

        private static int ParsePhaseMode(string[] args)
        {
            foreach (string a in args)
            {
                if (!a.StartsWith("--phase=", StringComparison.OrdinalIgnoreCase)) continue;
                if (int.TryParse(a.Substring("--phase=".Length), out int p) && (p == 0 || p == 3 || p == 4))
                    return p;
            }
            return 4;
        }
    }
}
