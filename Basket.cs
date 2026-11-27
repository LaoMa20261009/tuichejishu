using System;

namespace CrabWeighing
{
    static class Basket
    {
        public static readonly string[] Colors = { "红色", "绿色", "蓝色", "白色", "黄色" };
        public const int MaximumCount = 60;

        public static bool TryGrams(double weight, string unit, out double grams)
        {
            double factor;
            switch ((unit ?? "").Trim().ToLowerInvariant())
            {
                case "g": case "克": case "公克": factor = 1; break;
                case "kg": case "公斤": case "千克": factor = 1000; break;
                case "t": case "公吨": case "吨": factor = 1000000; break;
                case "oz": case "盎司": factor = 28.349523125; break;
                case "lb": case "磅": factor = 453.59237; break;
                case "klb": case "千磅": factor = 453592.37; break;
                default: grams = 0; return false;
            }
            grams = weight * factor;
            return !double.IsInfinity(grams) && !double.IsNaN(grams);
        }

        public static double[] Allocate(double grams, int[] counts, double[] reference)
        {
            var weights = new double[counts.Length];
            int only = Array.FindIndex(counts, count => count > 0);
            if (Array.FindLastIndex(counts, count => count > 0) == only)
            {
                if (only >= 0) weights[only] = Math.Round(grams, 1);
                return weights;
            }
            double total = 0;
            for (int i = 0; i < counts.Length; i++) total += counts[i] * reference[i];
            double cumulative = 0, assigned = 0;
            for (int i = 0; i < counts.Length; i++)
            {
                cumulative += counts[i] * reference[i];
                double rounded = Math.Round(grams * (cumulative / total), 1);
                weights[i] = Math.Round(rounded - assigned, 1);
                assigned = rounded;
            }
            return weights;
        }
    }
}
