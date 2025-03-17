using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ArpsForecasting
{
    class Program
    {
        static void Main(string[] args)
        {
            try
            {
                string csvPath = Path.Combine(Directory.GetCurrentDirectory(), "production_data.csv");
                var wellsData = LoadDataFromCsv(csvPath);

                if (!wellsData.Any())
                    throw new InvalidOperationException("No valid well data loaded from CSV.");

                // Clear the Charts folder
                string chartsDir = Path.Combine(Directory.GetCurrentDirectory(), "Charts");
                if (Directory.Exists(chartsDir))
                {
                    foreach (var file in Directory.GetFiles(chartsDir))
                    {
                        File.Delete(file);
                    }
                    Console.WriteLine($"Cleared existing files in '{chartsDir}'");
                }
                else
                {
                    Directory.CreateDirectory(chartsDir);
                    Console.WriteLine($"Created new Charts directory at '{chartsDir}'");
                }

                // Delete existing arps_parameters.csv if it exists
                string outputCsvPath = Path.Combine(Directory.GetCurrentDirectory(), "arps_parameters.csv");
                if (File.Exists(outputCsvPath))
                {
                    File.Delete(outputCsvPath);
                    Console.WriteLine($"Deleted existing 'arps_parameters.csv'");
                }

                // List to collect all ARPS parameters
                var allArpsParams = new List<WellChartGenerator.ArpsParams>();

                // Process each well using WellChartGenerator
                foreach (var well in wellsData)
                {
                    var chartGenerator = new WellChartGenerator(well.Key, well.Value.time, well.Value.production, chartsDir, 0, 1.0 / 3.0); // Last 1/3 for forecasting
                    chartGenerator.GenerateAndSaveChart();
                    allArpsParams.AddRange(chartGenerator.GetArpsParameters());
                }

                // Write ARPS parameters to a single CSV file
                using (var writer = new StreamWriter(outputCsvPath, append: false))
                {
                    writer.WriteLine("WellName,ModelType,EffectiveDate,Qi,Di,B");
                    foreach (var param in allArpsParams)
                    {
                        double diPercent = param.Di * 365 * 100;
                        writer.WriteLine($"{param.WellName},{param.ModelType},{param.EffectiveDate:F0},{param.Qi:F2},{diPercent:F2}%,{param.B:F2}");
                    }
                }
                Console.WriteLine($"ARPS parameters saved to '{outputCsvPath}'");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
        }

        static Dictionary<string, (List<double> time, List<double> production)> LoadDataFromCsv(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"CSV file not found at: {filePath}");

            var wellsData = new Dictionary<string, (List<double> time, List<double> production)>();
            var lines = File.ReadAllLines(filePath);

            if (lines.Length <= 1)
                throw new InvalidDataException("CSV file is empty or has only a header.");

            foreach (var line in lines.Skip(1))
            {
                var columns = line.Split(',').Select(c => c.Trim()).ToArray();
                if (columns.Length < 3) continue;
                var relevantColumns = new[] { columns[0], columns[1], columns[2] };

                if (!double.TryParse(relevantColumns[1], out double t) || !double.TryParse(relevantColumns[2], out double p) || p <= 0)
                {
                    continue;
                }

                string wellName = relevantColumns[0];
                if (!wellsData.ContainsKey(wellName))
                    wellsData[wellName] = (new List<double>(), new List<double>());

                wellsData[wellName].time.Add(t);
                wellsData[wellName].production.Add(p);
            }

            Console.WriteLine($"Loaded wells: {string.Join(", ", wellsData.Keys)}");
            return wellsData;
        }
    }
}