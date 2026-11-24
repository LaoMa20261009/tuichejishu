using System;
using System.Diagnostics;

namespace CrabWeighing
{
    sealed class SimulatedScale
    {
        static readonly double[] CrabWeights = { 0.184, 0.231, 0.208, 0.256, 0.193, 0.219 };
        readonly Random random = new Random();
        double previous;
        double target;
        long changedAt = Stopwatch.GetTimestamp();

        public void SetCount(int count)
        {
            long now = Stopwatch.GetTimestamp();
            previous = Interpolate(now);
            target = 0;
            for (int i = 0; i < count; i++) target += CrabWeights[i % CrabWeights.Length];
            changedAt = now;
        }

        double Interpolate(long now)
        {
            double fraction = Math.Min(1, CaptureGate.Elapsed(now, changedAt) / 0.65);
            return previous + (target - previous) * Math.Max(0, fraction);
        }

        public WeightSample Read(long now)
        {
            bool stable = CaptureGate.Elapsed(now, changedAt) >= 0.75;
            double value = stable ? target : Math.Max(0, Interpolate(now) + (random.NextDouble() - 0.5) * 0.025);
            return new WeightSample(Math.Round(value, 3), "kg", stable, true, 3);
        }
    }
}
