using System;
using System.Collections.Generic;

namespace ArpsForecasting
{
    public class ForecastRunResult
    {
        public bool Success { get; set; }
        public required string CsvPath { get; set; }
        public required string OutputDir { get; set; }
        public required string ArpsParametersPath { get; set; }
        public required string AriesExportPath { get; set; }
        public required string SummaryPath { get; set; }
        public string? PdfReportPath { get; set; }
        public required string ForecastParametersPath { get; set; }
        public required string ForecastParametersJsonPath { get; set; }
        public int WellCount { get; set; }
        public int ChartsGenerated { get; set; }
        public int ArpsParameterCount { get; set; }
        public int ForecastParameterCount { get; set; }
        public DateTime GeneratedAtUtc { get; set; } = DateTime.UtcNow;
        public List<string> ChartPaths { get; set; } = new();
        public List<string> Messages { get; set; } = new();
    }
}
