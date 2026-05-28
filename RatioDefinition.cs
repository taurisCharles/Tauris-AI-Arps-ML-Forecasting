namespace ArpsForecasting
{
    public sealed class RatioDefinition
    {
        public RatioDefinition(string name, string numeratorMetric, string denominatorMetric, string valueUnit)
        {
            Name = name;
            NumeratorMetric = numeratorMetric;
            DenominatorMetric = denominatorMetric;
            ValueUnit = valueUnit;
        }

        public string Name { get; }
        public string NumeratorMetric { get; }
        public string DenominatorMetric { get; }
        public string ValueUnit { get; }

        public static RatioDefinition[] Defaults =>
        [
            new RatioDefinition("WOR", "Water", "Oil", "bbl/bbl")
        ];
    }
}
