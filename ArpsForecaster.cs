// ArpsForecaster.cs
using System;
using System.Collections.Generic;

namespace ArpsForecasting
{
    public abstract class ArpsForecaster
    {
        public double Qi { get; protected set; }
        public double Di { get; protected set; }
        public double B { get; protected set; }
        protected DeclineType _declineType;
        protected double _dMin;
        protected double _t0;

        public enum DeclineType
        {
            Exponential,
            Hyperbolic,
            HyperbolicToExponential
        }

        public abstract void FitExponential(List<double> time, List<double> production, double qiGuess, double diGuess);
        public abstract void FitHyperbolic(List<double> time, List<double> production, double qiGuess, double diGuess, double? bFixed = null);
        public abstract void FitHyperbolicToExponential(List<double> time, List<double> production, double qiGuess, double diGuess, double bGuess, double dMin);

        public abstract double Forecast(double time, double historicalEndTime);

        public double CalculateSSE(List<double> time, List<double> production)
        {
            double sse = 0;
            for (int i = 0; i < time.Count; i++)
            {
                double predicted = Forecast(time[i], time.Max());
                sse += Math.Pow(predicted - production[i], 2);
            }
            return sse;
        }

        protected void ValidateInput(List<double> time, List<double> production)
        {
            if (time.Count != production.Count || time.Count < 3)
                throw new ArgumentException("Time and production lists must have equal length and at least 3 data points.");
        }

        public void SetReferenceTime(double t0)
        {
            _t0 = t0;
        }
    }
}