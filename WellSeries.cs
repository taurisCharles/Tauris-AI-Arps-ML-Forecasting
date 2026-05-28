using System;
using System.Collections.Generic;
using System.Linq;

namespace ArpsForecasting
{
    public enum CadenceType
    {
        Daily,
        Monthly
    }

    public class WellSeries
    {
        public required string Name { get; set; }
        public required List<double> Time { get; set; }
        public required List<double> Production { get; set; }
        public required List<double?> Pressure { get; set; }
        public required Dictionary<string, List<double>> PhaseSeries { get; set; }
        public CadenceType Cadence { get; set; }
        public double StepDays { get; set; }

        public IReadOnlyList<string> GetAvailablePhases()
        {
            return PhaseSeries
                .Where(kv => kv.Value.Count == Time.Count && kv.Value.Any(v => v > 0))
                .Select(kv => kv.Key)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public bool TryGetPhaseSeries(string phase, out List<double> series)
        {
            if (PhaseSeries.TryGetValue(phase, out var values) && values.Count == Time.Count)
            {
                series = values;
                return true;
            }

            series = new List<double>();
            return false;
        }
    }
}
