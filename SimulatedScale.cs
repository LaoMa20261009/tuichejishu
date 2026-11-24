using System;
using System.Diagnostics;

namespace CrabWeighing
{
    sealed class SimulatedScale
    {
        readonly Random random = new Random();
        double previous;
        double target;
        long changedAt = Stopwatch.GetTimestamp();

        public void SetCount(int count, double[] reference)
        {
            long now = Stopwatch.GetTimestamp();
            previous = Interpolate(now);
            target = 0;
            for (int i = 0; i < count; i++) target += reference[i % 3];
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
            double value = stable ? target : Math.Max(0, Interpolate(now) + (random.NextDouble() - 0.5) * 25);
            return new WeightSample(Math.Round(value, 1), "g", stable, true, 1);
        }
    }
}
