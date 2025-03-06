using System;
using System.Collections.Generic;
using System.IO;

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

                // Create a Charts folder if it doesn't exist
                string chartsDir = Path.Combine(Directory.GetCurrentDirectory(), "Charts");
                if (!Directory.Exists(chartsDir))
                    Directory.CreateDirectory(chartsDir);

                // Process each well using WellChartGenerator
                foreach (var well in wellsData)
                {
                    var chartGenerator = new WellChartGenerator(well.Key, well.Value.time, well.Value.production, chartsDir);
                    chartGenerator.GenerateAndSaveChart();
                }
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
                var columns = line.Split(',');
                if (columns.Length != 3 || !double.TryParse(columns[1], out double t) || !double.TryParse(columns[2], out double p))
                    continue;

                string wellName = columns[0].Trim();
                if (!wellsData.ContainsKey(wellName))
                    wellsData[wellName] = (new List<double>(), new List<double>());

                wellsData[wellName].time.Add(t);
                wellsData[wellName].production.Add(p);
            }

            return wellsData;
        }
    }
}