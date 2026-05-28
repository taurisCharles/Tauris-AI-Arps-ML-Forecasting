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
        private double _tSwitch;
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
            if (qiGuess < 0.1)
                throw new ArgumentException("Initial guess Qi must be >= 0.1");

            var valid = new List<(double t, double q, double w)>();
            for (int i = 0; i < time.Count; i++)
            {
                if (production[i] <= 0) continue;
                double dt = time[i] - _t0;
                if (dt < 0) continue;
                valid.Add((dt, production[i], RecencyWeight(i, time.Count)));
            }

            if (valid.Count < 3)
            {
                Qi = initialQi > 0 ? initialQi : qiGuess;
                Di = Math.Max(_config.MinDi, Math.Min(_config.MaxDi, diGuess));
                B = 0;
                _declineType = DeclineType.Exponential;
                return;
            }

            // Weighted linear regression on ln(q): ln(q) = ln(Qi) - Di * t
            double sw = 0, sx = 0, sy = 0, sxx = 0, sxy = 0;
            foreach (var (t, q, w) in valid)
            {
                double y = Math.Log(q);
                sw += w;
                sx += w * t;
                sy += w * y;
                sxx += w * t * t;
                sxy += w * t * y;
            }

            double denom = sw * sxx - sx * sx;
            if (Math.Abs(denom) < 1e-12)
            {
                Qi = initialQi > 0 ? initialQi : qiGuess;
                Di = Math.Max(_config.MinDi, Math.Min(_config.MaxDi, diGuess));
            }
            else
            {
                double slope = (sw * sxy - sx * sy) / denom;
                double intercept = (sy - slope * sx) / sw;
                Qi = initialQi > 0 ? initialQi : Math.Max(0.1, Math.Exp(intercept));
                Di = Math.Max(_config.MinDi, Math.Min(_config.MaxDi, -slope));
            }

            B = 0;
            _tSwitch = _t0;
            _declineType = DeclineType.Exponential;
        }

        public override void FitHyperbolic(List<double> time, List<double> production, double qiGuess, double diGuess, double? bFixed = null)
        {
            ValidateInput(time, production);
            if (qiGuess < 0.1 || diGuess < _config.MinDi || diGuess > _config.MaxDi ||
                (bFixed.HasValue && (bFixed < _config.MinB || bFixed > _config.MaxB)))
                throw new ArgumentException($"Initial guesses must be within valid ranges: Qi >= 0.1, Di between {_config.MinDi * 365 * 100:F2}% and {_config.MaxDi * 365 * 100:F2}% annual, B between {_config.MinB:F1} and {_config.MaxB:F1}");

            double bestSse = double.PositiveInfinity;
            double bestQi = initialQi > 0 ? initialQi : qiGuess;
            double bestDi = Math.Max(_config.MinDi, Math.Min(_config.MaxDi, diGuess));
            double bestB = bFixed ?? Math.Max(_config.MinB, Math.Min(_config.MaxB, _config.BFactor ?? 0.8));
            bool found = false;

            int bSteps = bFixed.HasValue ? 1 : 50;
            int diSteps = 120;
            for (int bi = 0; bi < bSteps; bi++)
            {
                double b = bFixed ?? (_config.MinB + (_config.MaxB - _config.MinB) * (bi / (double)Math.Max(1, bSteps - 1)));
                for (int dii = 0; dii < diSteps; dii++)
                {
                    double di = _config.MinDi + (_config.MaxDi - _config.MinDi) * (dii / (double)Math.Max(1, diSteps - 1));
                    if (TryFitQiForHyperbolic(time, production, b, di, out double qiOpt, out double sse) && sse < bestSse)
                    {
                        found = true;
                        bestSse = sse;
                        bestQi = initialQi > 0 ? initialQi : qiOpt;
                        bestDi = di;
                        bestB = b;
                    }
                }
            }

            if (!found)
            {
                bestQi = initialQi > 0 ? initialQi : qiGuess;
                bestDi = Math.Max(_config.MinDi, Math.Min(_config.MaxDi, diGuess));
                bestB = bFixed ?? Math.Max(_config.MinB, Math.Min(_config.MaxB, _config.BFactor ?? 0.8));
            }

            Qi = Math.Max(0.1, bestQi);
            Di = bestDi;
            B = bestB;
            _tSwitch = _t0;
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

            if (B <= 0 || Di <= _dMin)
            {
                _tSwitch = _t0;
            }
            else
            {
                double dtSwitch = (Di / _dMin - 1.0) / (B * Di);
                _tSwitch = _t0 + Math.Max(0, dtSwitch);
            }

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
                    if (time <= _tSwitch)
                    {
                        denominator = 1 + B * Di * (time - _t0);
                        return denominator <= 0 || B == 0 ? 0 : Math.Max(Qi / Math.Pow(denominator, 1 / B), 0);
                    }
                    else
                    {
                        denominator = 1 + B * Di * (_tSwitch - _t0);
                        if (denominator <= 0 || B == 0) return 0;
                        double qSwitch = Qi / Math.Pow(denominator, 1 / B);
                        return Math.Max(qSwitch * Math.Exp(-_dMin * (time - _tSwitch)), 0);
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

        private static double RecencyWeight(int index, int count)
        {
            if (count <= 1) return 1.0;
            double frac = index / (double)(count - 1);
            return 0.25 + 0.75 * frac * frac;
        }

        private bool TryFitQiForHyperbolic(
            List<double> time,
            List<double> production,
            double b,
            double di,
            out double qiOpt,
            out double sse)
        {
            qiOpt = 0.1;
            sse = double.PositiveInfinity;

            if (b <= 0 || di <= 0) return false;

            double num = 0;
            double den = 0;
            var features = new List<(double f, double p, double w)>();

            for (int i = 0; i < time.Count; i++)
            {
                if (production[i] < 0) continue;
                double dt = time[i] - _t0;
                if (dt < 0) continue;
                double d = 1 + b * di * dt;
                if (d <= 0) return false;
                double f = 1.0 / Math.Pow(d, 1.0 / b);
                double w = RecencyWeight(i, time.Count);
                features.Add((f, production[i], w));
                num += w * f * production[i];
                den += w * f * f;
            }

            if (features.Count < 3 || den <= 0) return false;

            qiOpt = Math.Max(0.1, num / den);
            double acc = 0;
            foreach (var (f, p, w) in features)
            {
                double err = qiOpt * f - p;
                double scale = Math.Max(1.0, p);
                double relErr = err / scale;
                acc += w * relErr * relErr;
            }
            sse = acc;
            return true;
        }

        // Added property for annual decline percentage
        public double AnnualDiPercent => (1 - Math.Exp(-Di * 365)) * 100;
    }
}
