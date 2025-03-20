// SSE.cs (Updated with AnnualDiPercent)
using System;
using System.Collections.Generic;

namespace ArpsForecasting
{
    public class SSE : ArpsForecaster
    {
        private double hyperbolicQi;
        private double hyperbolicDi;
        private double hyperbolicB;
        private double initialQi;
        public const double MaxDiAnnual = 3.65;
        public const double MinDiAnnual = 0.01;
        public const double MaxDi = MaxDiAnnual / 365.0;
        public const double MinDi = MinDiAnnual / 365.0;

        private readonly ForecastConfig _config;

        public SSE(ForecastConfig? config = null)
        {
            _config = config ?? new ForecastConfig();
            _declineType = DeclineType.HyperbolicToExponential;
            _dMin = _config.TerminalDecline ?? 0.0005;
            _t0 = _config.t0;
        }

        public override void FitExponential(List<double> time, List<double> production, double qiGuess, double diGuess)
        {
            ValidateInput(time, production);
            if (qiGuess < 0.1 || diGuess < 1e-6 || diGuess > 1.0)
                throw new ArgumentException("Initial guesses must be within valid ranges: Qi >= 0.1, 1e-6 <= Di <= 1.0");

            Qi = initialQi > 0 ? initialQi : qiGuess;
            Di = Math.Max(_config.MinDi, Math.Min(_config.MaxDi, diGuess));
            B = 0;
            _declineType = DeclineType.Exponential;
        }

        public override void FitHyperbolic(List<double> time, List<double> production, double qiGuess, double diGuess, double? bFixed = null)
        {
            ValidateInput(time, production);
            if (qiGuess < 0.1 || diGuess < _config.MinDi || diGuess > _config.MaxDi ||
                (bFixed.HasValue && (bFixed < _config.MinB || bFixed > _config.MaxB)))
                throw new ArgumentException($"Initial guesses must be within valid ranges: Qi >= 0.1, Di between {_config.MinDi * 365 * 100:F2}% and {_config.MaxDi * 365 * 100:F2}% annual, B between {_config.MinB:F1} and {_config.MaxB:F1}");

            Qi = initialQi > 0 ? initialQi : qiGuess;
            Di = Math.Max(_config.MinDi, Math.Min(_config.MaxDi, diGuess));
            B = bFixed ?? (_config.BFactor ?? 0.5);
            _declineType = DeclineType.Hyperbolic;

            hyperbolicQi = Qi;
            hyperbolicDi = Di;
            hyperbolicB = B;
        }

        public override void FitHyperbolicToExponential(List<double> time, List<double> production, double qiGuess, double diGuess, double bGuess, double dMin)
        {
            ValidateInput(time, production);
            if (qiGuess < 0.1 || diGuess < _config.MinDi || diGuess > _config.MaxDi ||
                bGuess < _config.MinB || bGuess > _config.MaxB || dMin <= 0)
                throw new ArgumentException($"Initial guesses must be within valid ranges: Qi >= 0.1, Di between {_config.MinDi * 365 * 100:F2}% and {_config.MaxDi * 365 * 100:F2}% annual, B between {_config.MinB:F1} and {_config.MaxB:F1}, Dmin > 0");

            FitHyperbolic(time, production, qiGuess, diGuess, bGuess);
            Qi = initialQi > 0 ? initialQi : hyperbolicQi;
            Di = hyperbolicDi;
            B = hyperbolicB;
            _dMin = dMin;
            _declineType = DeclineType.HyperbolicToExponential;
        }

        public override double Forecast(double time, double historicalEndTime)
        {
            if (time < _t0) return 0;

            switch (_declineType)
            {
                case DeclineType.Exponential:
                    return Math.Max(Qi * Math.Exp(-Di * (time - _t0)), 0);
                case DeclineType.Hyperbolic:
                    double denominator = 1 + B * Di * (time - _t0);
                    return denominator <= 0 || B == 0 ? 0 : Math.Max(Qi / Math.Pow(denominator, 1 / B), 0);
                case DeclineType.HyperbolicToExponential:
                    double tSwitch = _t0 + 100; // Simplified placeholder
                    if (time <= tSwitch)
                    {
                        denominator = 1 + B * Di * (time - _t0);
                        return denominator <= 0 || B == 0 ? 0 : Math.Max(Qi / Math.Pow(denominator, 1 / B), 0);
                    }
                    else
                    {
                        denominator = 1 + B * Di * (tSwitch - _t0);
                        if (denominator <= 0 || B == 0) return 0;
                        double qSwitch = Qi / Math.Pow(denominator, 1 / B);
                        return Math.Max(qSwitch * Math.Exp(-_dMin * (time - tSwitch)), 0);
                    }
                default:
                    throw new InvalidOperationException("No decline model fitted.");
            }
        }

        public void SetQi(double qi)
        {
            if (qi <= 0) throw new ArgumentException("Qi must be positive.");
            Qi = initialQi = qi;
        }

        // Added property for annual decline percentage
        public double AnnualDiPercent => (1 - Math.Exp(-Di * 365)) * 100;
    }
}