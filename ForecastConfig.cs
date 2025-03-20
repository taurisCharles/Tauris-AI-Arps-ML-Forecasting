// ForecastConfig.cs (Confirmed)
namespace ArpsForecasting
{
    public class ForecastConfig
    {
        public double? InitialRate { get; set; }
        public double? InitialDecline { get; set; }
        public double? BFactor { get; set; }
        public double? TerminalDecline { get; set; }
        public double MinB { get; set; }
        public double MaxB { get; set; }
        public double MinDi { get; set; }
        public double MaxDi { get; set; }
        public double t0 { get; set; }
        public double? bFixed { get; set; }
        public int NumLags { get; set; }
        public int MinTrainingIndex { get; set; }

        public ForecastConfig()
        {
            InitialRate = 737.58;
            InitialDecline = 0.005;
            BFactor = 1.0;
            MinB = 0.5;
            MaxB = 1.5;
            MinDi = SSE.MinDi;
            MaxDi = SSE.MaxDi;
            TerminalDecline = 0.0005;
            t0 = 0;
            bFixed = null;
            NumLags = 3;
            MinTrainingIndex = 10;
        }

        public override string ToString()
        {
            return $"ForecastConfig(InitialRate={InitialRate:F2}, InitialDecline={InitialDecline:F6}, " +
                   $"BFactor={BFactor:F1}, MinB={MinB:F1}, MaxB={MaxB:F1}, MinDi={MinDi:F6}, " +
                   $"MaxDi={MaxDi:F6}, TerminalDecline={TerminalDecline:F6}, t0={t0}, " +
                   $"bFixed={(bFixed.HasValue ? bFixed.Value.ToString("F1") : "null")}, " +
                   $"NumLags={NumLags}, MinTrainingIndex={MinTrainingIndex})";
        }
    }
}