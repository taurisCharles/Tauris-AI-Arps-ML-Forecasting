using System.Collections.Generic;

namespace ArpsForecasting
{
    public sealed class WellForecastResult
    {
        public required string WellName { get; set; }
        public List<ForecastParameterRow> ParameterRows { get; set; } = new();
        public List<WellChartGenerator.ArpsParams> ArpsParameters { get; set; } = new();
        public List<string> Messages { get; set; } = new();
    }
}
