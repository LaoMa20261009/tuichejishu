using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace CrabWeighing
{
    sealed class SimulatedScale
    {
        readonly Random random = new Random();
        readonly List<int> colors = new List<int>();
        readonly List<double> weights = new List<double>();
        double previous;
        double target;
        long changedAt = Stopwatch.GetTimestamp();
        long nextRead;
        WeightSample sample;

        public int[] Colors { get { return colors.ToArray(); } }

        public void Adjust(int color, int count, double[] reference)
        {
            if (count == 0) return;
            long now = Stopwatch.GetTimestamp();
            previous = Interpolate(now);
            if (count > 0)
                for (int i = 0; i < count; i++)
                {
                    colors.Add(color);
                    weights.Add(Math.Max(1, reference[color] + (random.NextDouble() - 0.5) * 9));
                }
            else
                for (int i = 0; i < -count; i++)
                {
                    int index = colors.LastIndexOf(color);
                    if (index < 0) break;
                    colors.RemoveAt(index);
                    weights.RemoveAt(index);
                }
            target = weights.Sum();
            changedAt = now;
        }

        public void Clear()
        {
            long now = Stopwatch.GetTimestamp();
            previous = Interpolate(now);
            colors.Clear();
            weights.Clear();
            target = 0;
            changedAt = now;
            nextRead = 0;
        }

        double Interpolate(long now)
        {
            double fraction = Math.Min(1, CaptureGate.Elapsed(now, changedAt) / 1.25);
            return previous + (target - previous) * Math.Max(0, fraction);
        }

        public WeightSample Read(long now)
        {
            if (sample != null && now < nextRead) return sample;
            nextRead = now + Stopwatch.Frequency;
            bool stable = CaptureGate.Elapsed(now, changedAt) >= 1.35;
            double value = stable ? target : Math.Max(0, Interpolate(now) + (random.NextDouble() - 0.5) * 8);
            sample = new WeightSample(Math.Round(value, 1), "g", stable, true, 1);
            return sample;
        }
    }
}
