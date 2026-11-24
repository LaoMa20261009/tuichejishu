using System;

namespace CrabWeighing
{
    static class Basket
    {
        public static readonly string[] Colors = { "红色", "绿色", "蓝色" };
        public const int MaximumCount = 60;
        public const double MatchTolerance = 10;

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

        public static int[] Infer(double grams, string detected, double[] reference)
        {
            string[] visible = detected.Split('、');
            var minimum = new int[3];
            var maximum = new int[3];
            for (int i = 0; i < Colors.Length; i++)
            {
                bool present = Array.IndexOf(visible, Colors[i]) >= 0;
                minimum[i] = present ? 1 : 0;
                maximum[i] = present ? (int)Math.Min(MaximumCount, (grams + MatchTolerance) / reference[i]) : 0;
            }

            int[] match = null;
            for (int red = minimum[0]; red <= maximum[0]; red++)
            for (int green = minimum[1]; green <= Math.Min(maximum[1], MaximumCount - red); green++)
            {
                double remaining = grams - red * reference[0] - green * reference[1];
                int low = (int)Math.Max(minimum[2], Math.Ceiling((remaining - MatchTolerance) / reference[2]));
                int high = (int)Math.Min(Math.Min(maximum[2], MaximumCount - red - green),
                    Math.Floor((remaining + MatchTolerance) / reference[2]));
                if (low > high || red + green + high == 0) continue;
                if (match != null || low != high) return null;
                match = new[] { red, green, low };
            }
            return match;
        }

        public static double[] Allocate(double grams, int[] counts, double[] reference)
        {
            double total = 0;
            for (int i = 0; i < counts.Length; i++) total += counts[i] * reference[i];
            var weights = new double[counts.Length];
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
