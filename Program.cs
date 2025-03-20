// Program.cs (Updated with 5 Significant Digits)
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
            string csvPath = @"C:\Dev\ArpsForecasting\production_data.csv";
            string outputDir = @"C:\Dev\ArpsForecasting\Charts";

            if (!Directory.Exists(outputDir))
                Directory.CreateDirectory(outputDir);
            Console.WriteLine($"Cleared existing files in '{outputDir}'");

            var wells = LoadWellData(csvPath);
            if (!wells.Any())
            {
                Console.WriteLine("No well data found in CSV file. Exiting.");
                return;
            }
            Console.WriteLine($"Loaded wells: {string.Join(", ", wells.Select(w => w.name))}");

            int effectiveOffset = 0;
            var arpsParameters = new List<WellChartGenerator.ArpsParams>();

            foreach (var well in wells)
            {
                var generator = new WellChartGenerator(well.name, well.time, well.production, outputDir, effectiveOffset);
                generator.GenerateAndSaveChart();
                arpsParameters.AddRange(generator.GetArpsParameters());
            }

            string? dirName = Path.GetDirectoryName(outputDir);
            if (string.IsNullOrEmpty(dirName))
            {
                Console.WriteLine("Output directory path is invalid. Skipping CSV save.");
                return;
            }
            string arpsPath = Path.Combine(dirName, "arps_parameters.csv");
            using (var writer = new StreamWriter(arpsPath))
            {
                writer.WriteLine("WellName,ModelType,EffectiveDate,Qi,AnnualDiPercent,B");
                foreach (var param in arpsParameters)
                {
                    writer.WriteLine($"{param.WellName},{param.ModelType},{param.EffectiveDate},{param.Qi:G5},{param.AnnualDiPercent:G5},{param.B:G5}");
                }
            }
            Console.WriteLine($"ARPS parameters saved to '{arpsPath}'");
        }

        static List<(string name, List<double> time, List<double> production)> LoadWellData(string csvPath)
        {
            var wellsData = new Dictionary<string, (List<double> time, List<double> production)>();

            try
            {
                using (var reader = new StreamReader(csvPath))
                {
                    reader.ReadLine(); // Skip header
                    while (!reader.EndOfStream)
                    {
                        var line = reader.ReadLine();
                        if (string.IsNullOrEmpty(line)) continue;

                        var values = line.Split(',');
                        if (values.Length < 3) continue;

                        string wellName = values[0].Trim();
                        if (!double.TryParse(values[1], out double time)) continue;
                        if (!double.TryParse(values[2], out double production)) continue;

                        if (!wellsData.ContainsKey(wellName))
                        {
                            wellsData[wellName] = (new List<double>(), new List<double>());
                        }

                        wellsData[wellName].time.Add(time);
                        wellsData[wellName].production.Add(production);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading CSV file: {ex.Message}");
                return new List<(string, List<double>, List<double>)>();
            }

            return wellsData.Select(kv => (kv.Key, kv.Value.time, kv.Value.production)).ToList();
        }
    }
}