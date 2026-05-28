using System;

namespace ArpsForecasting
{
    public class ForecastParameterRow
    {
        public required string WellName { get; set; }
        public required string Metric { get; set; }
        public required string ForecastFamily { get; set; }
        public required string ModelType { get; set; }
        public double? EffectiveDate { get; set; }
        public double? Qi { get; set; }
        public double? Di { get; set; }
        public double? AnnualDiPercent { get; set; }
        public double? B { get; set; }
        public string ValueUnit { get; set; } = string.Empty;
        public string NumeratorMetric { get; set; } = string.Empty;
        public string DenominatorMetric { get; set; } = string.Empty;
        public double? LatestObservedValue { get; set; }
        public double? ForecastAnchorValue { get; set; }
        public string Cadence { get; set; } = string.Empty;
        public bool PressureUsed { get; set; }
        public int SourcePointCount { get; set; }
        public int ValidPointCount { get; set; }
        public string Status { get; set; } = "ok";
        public string Message { get; set; } = string.Empty;
        public DateTime GeneratedAtUtc { get; set; } = DateTime.UtcNow;
    }
}
