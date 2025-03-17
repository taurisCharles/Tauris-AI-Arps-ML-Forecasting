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
            // Define paths
            string csvPath = @"C:\Dev\ArpsForecasting\production_data.csv";
            string outputDir = @"C:\Dev\ArpsForecasting\Charts";

            // Create output directory if it doesn't exist
            if (!Directory.Exists(outputDir))
                Directory.CreateDirectory(outputDir);
            Console.WriteLine($"Cleared existing files in '{outputDir}'");

            // Load well data from CSV
            var wells = LoadWellData(csvPath);
            if (!wells.Any())
            {
                Console.WriteLine("No well data found in CSV file. Exiting.");
                return;
            }
            Console.WriteLine($"Loaded wells: {string.Join(", ", wells.Select(w => w.name))}");

            int effectiveOffset = 0;
            var arpsParameters = new List<WellChartGenerator.ArpsParams>();

            // Process each well
            foreach (var well in wells)
            {
                var generator = new WellChartGenerator(well.name, well.time, well.production, outputDir, effectiveOffset); // Removed forecastFraction
                generator.GenerateAndSaveChart();
                arpsParameters.AddRange(generator.GetArpsParameters());
            }

            // Save arpsParameters to CSV
            string arpsPath = Path.Combine(Path.GetDirectoryName(outputDir), "arps_parameters.csv");
            using (var writer = new StreamWriter(arpsPath))
            {
                writer.WriteLine("WellName,ModelType,EffectiveDate,Qi,Di,B");
                foreach (var param in arpsParameters)
                {
                    writer.WriteLine($"{param.WellName},{param.ModelType},{param.EffectiveDate},{param.Qi},{param.Di},{param.B}");
                }
            }
            Console.WriteLine($"ARPS parameters saved to '{arpsPath}'");
        }

        // Method to load well data from CSV
        static List<(string name, List<double> time, List<double> production)> LoadWellData(string csvPath)
        {
            var wellsData = new Dictionary<string, (List<double> time, List<double> production)>();

            try
            {
                using (var reader = new StreamReader(csvPath))
                {
                    // Skip header
                    reader.ReadLine();

                    // Read each line
                    while (!reader.EndOfStream)
                    {
                        var line = reader.ReadLine();
                        var values = line.Split(',');

                        if (values.Length < 3) continue; // Skip malformed lines

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

            // Convert dictionary to list of tuples
            return wellsData.Select(kv => (kv.Key, kv.Value.time, kv.Value.production)).ToList();
        }
    }
}