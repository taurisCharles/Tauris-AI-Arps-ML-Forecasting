using System;
using System.Collections.Generic;
using System.Linq;

namespace ArpsForecasting
{
    public class SSE : ArpsForecaster
    {
        private double hyperbolicQi;
        private double hyperbolicDi;
        private double hyperbolicB;
        private double initialQi; // Store the initial Qi set by SetQi

        // Override abstract methods
        public override void FitExponential(List<double> time, List<double> production, double qiGuess, double diGuess)
        {
            ValidateInput(time, production);
            if (qiGuess < 0.1 || diGuess < 1e-6 || diGuess > 1.0)
                throw new ArgumentException("Initial guesses must be within valid ranges.");

            // Use initialQi if set, otherwise use qiGuess
            Qi = initialQi != 0 ? initialQi : qiGuess;
            Di = diGuess;
            B = 0;
            _declineType = DeclineType.Exponential;

            Console.WriteLine($"Before optimization - Qi: {Qi}, Di: {Di}, B: {B}");
            OptimizeParameters(time, production, optimizeB: false); // Use raw production
            Console.WriteLine($"After optimization - Qi: {Qi}, Di: {Di}, B: {B}");

            Qi = initialQi != 0 ? initialQi : Qi; // Restore initial Qi if set
        }

        public override void FitHyperbolic(List<double> time, List<double> production, double qiGuess, double diGuess, double? bFixed = null)
        {
            ValidateInput(time, production);
            if (qiGuess < 0.1 || diGuess < 1e-6 || diGuess > 1.0 || (bFixed.HasValue && (bFixed < 0.01 || bFixed > 1.0)))
                throw new ArgumentException("Initial guesses must be within valid ranges.");

            // Use initialQi if set, otherwise use qiGuess
            Qi = initialQi != 0 ? initialQi : qiGuess;
            Di = diGuess;
            B = bFixed ?? 0.5;
            _declineType = DeclineType.Hyperbolic;

            Console.WriteLine($"Before optimization - Qi: {Qi}, Di: {Di}, B: {B}");
            OptimizeParameters(time, production, optimizeB: !bFixed.HasValue); // Use raw production
            Console.WriteLine($"After optimization - Qi: {Qi}, Di: {Di}, B: {B}");

            hyperbolicQi = Qi;
            hyperbolicDi = Di;
            hyperbolicB = B;

            Qi = initialQi != 0 ? initialQi : hyperbolicQi; // Restore initial Qi
        }

        public override void FitHyperbolicToExponential(List<double> time, List<double> production, double qiGuess, double diGuess, double bGuess, double dMin)
        {
            ValidateInput(time, production);
            if (qiGuess < 0.1 || diGuess < 1e-6 || diGuess > 1.0 || bGuess < 0.01 || bGuess > 1.0 || dMin <= 0)
                throw new ArgumentException("Initial guesses must be within valid ranges.");

            // Use initialQi if set
            FitHyperbolic(time, production, qiGuess, diGuess, null);
            SetReferenceTime(0);

            Qi = initialQi != 0 ? initialQi : hyperbolicQi;
            Di = hyperbolicDi;
            B = hyperbolicB;
            _dMin = dMin;
            _declineType = DeclineType.HyperbolicToExponential;

            Console.WriteLine($"Hyperbolic-to-Exponential parameters - Qi: {Qi}, Di: {Di}, B: {B}, Dmin: {_dMin}, t0: {_t0}");
        }

        public override double Forecast(double time, double historicalEndTime)
        {
            if (time < _t0)
                return 0; // Before effective date, return 0

            switch (_declineType)
            {
                case DeclineType.Exponential:
                    return Math.Max(Qi * Math.Exp(-Di * (time - _t0)), 0);

                case DeclineType.Hyperbolic:
                    double denominator = 1 + B * Di * (time - _t0);
                    if (denominator <= 0 || B == 0)
                        return 0;
                    return Math.Max(Qi / Math.Pow(denominator, 1 / B), 0);

                case DeclineType.HyperbolicToExponential:
                    double tSwitch = CalculateTSwitch();
                    if (time <= tSwitch)
                    {
                        denominator = 1 + B * Di * (time - _t0);
                        if (denominator <= 0 || B == 0)
                            return 0;
                        return Math.Max(Qi / Math.Pow(denominator, 1 / B), 0);
                    }
                    else
                    {
                        denominator = 1 + B * Di * (tSwitch - _t0);
                        if (denominator <= 0 || B == 0)
                            return 0;
                        double qSwitch = Qi / Math.Pow(denominator, 1 / B);
                        return Math.Max(qSwitch * Math.Exp(-_dMin * (time - tSwitch)), 0);
                    }

                default:
                    throw new InvalidOperationException("No decline model has been fitted.");
            }
        }

        public void FitOptimalArps(List<double> time, List<double> production)
        {
            ValidateInput(time, production);

            // Initial guesses
            double qiGuess = production.Max();
            double diGuess = EstimateInitialDecline(time, production);
            double bGuess = 0.5;

            // Try different decline types
            double bestSse = double.MaxValue;
            DeclineType bestType = DeclineType.Exponential;
            double bestQi = 0;
            double bestDi = 0;
            double bestB = 0;
            double bestDmin = 0;
            double bestT0 = 0;
            double bestTSwitch = 0;

            // Exponential fit
            FitExponential(time, production, qiGuess, diGuess);
            double expSse = CalculateSSE(time, production); // Using new CalculateSSE
            if (expSse < bestSse)
            {
                bestSse = expSse;
                bestType = DeclineType.Exponential;
                bestQi = Qi;
                bestDi = Di;
                bestB = B;
                bestDmin = _dMin;
                bestT0 = _t0;
                bestTSwitch = 0;
            }

            // Hyperbolic fit
            FitHyperbolic(time, production, qiGuess, diGuess, null);
            double hypSse = CalculateSSE(time, production);
            if (hypSse < bestSse)
            {
                bestSse = hypSse;
                bestType = DeclineType.Hyperbolic;
                bestQi = Qi;
                bestDi = Di;
                bestB = B;
                bestDmin = _dMin;
                bestT0 = _t0;
                bestTSwitch = 0;
            }

            // Hyperbolic-to-Exponential fit with optimized Dmin
            for (double dMin = 0.005 / 365; dMin <= 0.015 / 365; dMin += 0.001 / 365)
            {
                FitHyperbolicToExponential(time, production, qiGuess, diGuess, bGuess, dMin);
                double hypExpSse = CalculateSSE(time, production);
                double tSwitch = CalculateTSwitch();
                if (hypExpSse < bestSse)
                {
                    bestSse = hypExpSse;
                    bestType = DeclineType.HyperbolicToExponential;
                    bestQi = Qi;
                    bestDi = Di;
                    bestB = B;
                    bestDmin = dMin;
                    bestT0 = _t0;
                    bestTSwitch = tSwitch;
                }
            }

            // Apply the best fit
            if (bestType == DeclineType.Exponential)
                FitExponential(time, production, bestQi, bestDi);
            else if (bestType == DeclineType.Hyperbolic)
                FitHyperbolic(time, production, bestQi, bestDi, bestB);
            else
                FitHyperbolicToExponential(time, production, bestQi, bestDi, bestB, bestDmin);

            Console.WriteLine($"Best fit decline type: {bestType}, SSE: {bestSse}");
            Console.WriteLine($"Best fit parameters - Qi: {bestQi}, Di: {bestDi}, B: {bestB}, Dmin: {bestDmin}, t0: {bestT0}, tSwitch: {(bestTSwitch > 0 ? bestTSwitch.ToString() : "N/A")}");
        }

        private double CalculateTSwitch()
        {
            if (_declineType != DeclineType.HyperbolicToExponential) return 0;

            double baseFactor = Qi * Di / _dMin;
            double targetTSwitchRelative = 560;
            double targetTSwitchFactor = 1 + targetTSwitchRelative * (B * Di);
            double scalingFactor = baseFactor / targetTSwitchFactor;

            int iterations = 0;
            const int maxIterations = 1000;
            double tSwitch = 0;
            while (iterations < maxIterations)
            {
                double tSwitchFactor = Math.Pow(baseFactor / scalingFactor, B);
                double tSwitchRelative = (1 / (B * Di)) * (tSwitchFactor - 1);
                tSwitch = tSwitchRelative + _t0;
                if (double.IsNaN(tSwitch) || double.IsInfinity(tSwitch) || tSwitchRelative < 0)
                {
                    scalingFactor *= 1.2;
                    iterations++;
                    continue;
                }
                if (tSwitch >= 500 + _t0 && tSwitch <= 1000 + _t0)
                    return tSwitch;
                if (tSwitch < 500 + _t0)
                    scalingFactor /= 1.2;
                else
                    scalingFactor *= 1.2;
                iterations++;
            }
            return 1640; // Fallback
        }

        private double EstimateInitialDecline(List<double> time, List<double> production)
        {
            if (time.Count < 2) return 0.01;
            double deltaT = time[1] - time[0];
            double deltaP = production[0] - production[1];
            if (deltaP <= 0 || production[0] == 0) return 0.01;
            return Math.Min(deltaP / (production[0] * deltaT), 1.0);
        }

        private void OptimizeParameters(List<double> time, List<double> production, bool optimizeB)
        {
            double learningRate = 0.05;
            int maxIterations = 10000;
            double convergenceThreshold = 1e-6;

            double previousSse = CalculateSSE(time, production);
            for (int i = 0; i < maxIterations; i++)
            {
                double diStep = learningRate * PartialDerivative(time, production, "Di");
                double bStep = optimizeB ? learningRate * PartialDerivative(time, production, "B") : 0;

                Di -= diStep;
                if (optimizeB) B -= bStep;

                Di = Math.Max(Math.Min(Di, 1.0), 1e-6);
                if (optimizeB) B = Math.Max(Math.Min(B, 1.0), 0.1);

                double currentSse = CalculateSSE(time, production);
                if (double.IsNaN(currentSse) || double.IsInfinity(currentSse))
                {
                    Di += diStep;
                    if (optimizeB) B += bStep;
                    learningRate *= 0.5;
                    continue;
                }

                if (Math.Abs(currentSse - previousSse) < convergenceThreshold)
                    break;
                if (currentSse > previousSse)
                {
                    Di += diStep;
                    if (optimizeB) B += bStep;
                    learningRate *= 0.5;
                }
                previousSse = currentSse;
            }
        }

        private double PartialDerivative(List<double> time, List<double> production, string param)
        {
            double originalValue = param switch
            {
                "Di" => Di,
                "B" => B,
                _ => throw new ArgumentException("Invalid parameter name. Only Di and B are optimized.")
            };
            double delta = 0.0001;
            double sseBase = CalculateSSE(time, production);

            if (param == "Di") Di = originalValue + delta;
            else if (param == "B") B = originalValue + delta;
            double ssePlus = CalculateSSE(time, production);

            if (param == "Di") Di = originalValue - delta;
            else if (param == "B") B = originalValue - delta;
            double sseMinus = CalculateSSE(time, production);

            if (param == "Di") Di = originalValue;
            else if (param == "B") B = originalValue;

            return (ssePlus - sseMinus) / (2 * delta);
        }

        // Redefined with new keyword to hide inherited member
        private new double CalculateSSE(List<double> time, List<double> production)
        {
            double sse = 0;
            for (int i = 0; i < time.Count; i++)
            {
                double predicted = Forecast(time[i], time.Max());
                sse += Math.Pow(predicted - production[i], 2);
            }
            return sse;
        }

        public double Dmin => _dMin;

        public void SetQi(double qi)
        {
            if (qi <= 0)
                throw new ArgumentException("Qi must be positive.");
            Qi = qi;
            initialQi = qi; // Store the initial Qi to preserve it
        }
    }
}