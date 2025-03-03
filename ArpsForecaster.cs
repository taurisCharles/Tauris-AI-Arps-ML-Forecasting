// ArpsForecaster.cs
using System;
using System.Collections.Generic;

public abstract class ArpsForecaster
{
    public double Qi { get; protected set; }
    public double Di { get; protected set; }
    public double B { get; protected set; }
    protected DeclineType _declineType;
    protected double _dMin;

    public enum DeclineType
    {
        Exponential,
        Hyperbolic,
        HyperbolicToExponential
    }

    public abstract void FitExponential(List<double> time, List<double> production, double qiGuess, double diGuess);
    public abstract void FitHyperbolic(List<double> time, List<double> production, double qiGuess, double diGuess, double? bFixed = null);
    public abstract void FitHyperbolicToExponential(List<double> time, List<double> production, double qiGuess, double diGuess, double bGuess, double dMin);

    public double Forecast(double time)
    {
        switch (_declineType)
        {
            case DeclineType.Exponential:
                return Math.Max(Qi * Math.Exp(-Di * time), 0);

            case DeclineType.Hyperbolic:
                double denominator = 1 + B * Di * time;
                if (denominator <= 0 || B == 0)
                    return 0;
                return Math.Max(Qi / Math.Pow(denominator, 1 / B), 0);

            case DeclineType.HyperbolicToExponential:
                // Ensure tSwitch is positive by setting a maximum initial scaling factor
                double baseFactor = Qi * Di / _dMin;
                double scalingFactor = baseFactor; // Start with baseFactor to ensure tSwitchFactor >= 1

                int iterations = 0;
                const int maxIterations = 1000; // Increase to allow more attempts
                while (iterations < maxIterations)
                {
                    double tSwitchFactor = Math.Pow(baseFactor / scalingFactor, B);
                    double tSwitch = (1 / (B * Di)) * (tSwitchFactor - 1);
                    if (double.IsNaN(tSwitch) || double.IsInfinity(tSwitch) || tSwitch < 0)
                    {
                        Console.WriteLine($"Invalid tSwitch: {tSwitch}, adjusting scaling factor...");
                        scalingFactor *= 2; // Increase scalingFactor to make tSwitchFactor closer to 1+
                        iterations++;
                        continue;
                    }
                    if (tSwitch >= 500 && tSwitch <= 1000)
                    {
                        Console.WriteLine($"tSwitch calculated as: {tSwitch}");
                        if (time <= tSwitch)
                        {
                            denominator = 1 + B * Di * time;
                            if (denominator <= 0 || B == 0)
                                return 0;
                            double hyperbolicValue = Qi / Math.Pow(denominator, 1 / B);
                            if (time == 50 || time == 100 || time == 300 || time == 599 || time == 1080)
                                Console.WriteLine($"Time {time}: Using hyperbolic, value = {hyperbolicValue:F3}");
                            return Math.Max(hyperbolicValue, 0);
                        }
                        else
                        {
                            denominator = 1 + B * Di * tSwitch;
                            if (denominator <= 0 || B == 0)
                                return 0;
                            double qSwitch = Qi / Math.Pow(denominator, 1 / B);
                            double exponentialValue = qSwitch * Math.Exp(-_dMin * (time - tSwitch));
                            if (time == 50 || time == 100 || time == 300 || time == 599 || time == 1080 || time == 1100 || time == 1200 || time == 1300 || time == 1400 || time == 1500 || time == 1600 || time == 1700 || time == 1800 || time == 1900 || time == 2000 || time == 2100 || time == 2180)
                                Console.WriteLine($"Time {time}: Using exponential, qSwitch = {qSwitch:F3}, value = {exponentialValue:F3}");
                            return Math.Max(exponentialValue, 0);
                        }
                    }
                    // Adjust scaling factor based on tSwitch
                    if (tSwitch < 500)
                        scalingFactor /= 2; // Decrease scalingFactor to increase tSwitch
                    else
                        scalingFactor *= 2; // Increase scalingFactor to decrease tSwitch
                    iterations++;
                }
                throw new InvalidOperationException("Failed to find a valid tSwitch after maximum iterations.");

            default:
                throw new InvalidOperationException("No decline model has been fitted.");
        }
    }

    public double CalculateSSE(List<double> time, List<double> production)
    {
        double sse = 0;
        for (int i = 0; i < time.Count; i++)
        {
            double predicted = Forecast(time[i]);
            sse += Math.Pow(predicted - production[i], 2);
        }
        return sse;
    }

    protected void ValidateInput(List<double> time, List<double> production)
    {
        if (time.Count != production.Count || time.Count < 3)
            throw new ArgumentException("Time and production lists must have equal length and at least 3 data points.");
    }
}