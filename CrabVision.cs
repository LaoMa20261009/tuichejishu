using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace CrabWeighing
{
    sealed class CrabTarget
    {
        public readonly int ColorIndex;
        public readonly Rectangle Bounds;

        public CrabTarget(int colorIndex, Rectangle bounds)
        {
            ColorIndex = colorIndex;
            Bounds = bounds;
        }
    }

    sealed class CrabObservation
    {
        public readonly CrabTarget[] Targets;
        public readonly int[] Counts;

        public CrabObservation(CrabTarget[] targets)
        {
            Targets = targets;
            Counts = new int[Basket.Colors.Length];
            foreach (CrabTarget target in targets) Counts[target.ColorIndex]++;
        }

        public string Signature { get { return string.Join(",", Counts.Select(count => count.ToString())); } }
    }

    static class CrabVision
    {
        // Local connectivity closes short gaps in a partially occluded rope.
        public static CrabObservation Read(VideoFrame frame, Rectangle area, double minimumCoverage)
        {
            area.Intersect(new Rectangle(0, 0, VideoFrame.Width, VideoFrame.Height));
            var targets = new List<CrabTarget>();
            if (area.Width < 24 || area.Height < 24) return new CrabObservation(targets.ToArray());
            int width = area.Width, height = area.Height;
            int size = width * height;
            var labels = new byte[size];
            var queue = new int[size];

            for (int y = 0; y < height; y++)
            {
                int source = ((area.Y + y) * VideoFrame.Width + area.X) * 3;
                for (int x = 0; x < width; x++, source += 3)
                    labels[y * width + x] = Classify(frame.Pixels[source + 2], frame.Pixels[source + 1], frame.Pixels[source]);
            }

            int minimumPixels = Math.Max(24, (int)Math.Ceiling(size * minimumCoverage / 400));
            for (int origin = 0; origin < size; origin++)
            {
                byte color = labels[origin];
                if (color == 0) continue;
                labels[origin] = 0;
                queue[0] = origin;
                int count = 1, left = width, right = 0, top = height, bottom = 0;
                for (int current = 0; current < count; current++)
                {
                    int y = queue[current] / width, x = queue[current] % width;
                    left = Math.Min(left, x);
                    right = Math.Max(right, x);
                    top = Math.Min(top, y);
                    bottom = Math.Max(bottom, y);

                    for (int row = Math.Max(0, y - 2); row <= Math.Min(height - 1, y + 2); row++)
                    for (int col = Math.Max(0, x - 2); col <= Math.Min(width - 1, x + 2); col++)
                    {
                        int neighbor = row * width + col;
                        if (labels[neighbor] != color) continue;
                        labels[neighbor] = 0;
                        queue[count++] = neighbor;
                    }
                }

                int spanX = right - left + 1, spanY = bottom - top + 1;
                int boxArea = spanX * spanY;
                if (count < minimumPixels || spanX < 13 || spanY < 13 ||
                    spanX > 3.5 * spanY || spanY > 3.5 * spanX ||
                    boxArea > size * 0.42 || count < boxArea * 0.025 || count > boxArea * 0.56)
                    continue;

                // A usable target surrounds an interior and has rope in at least three quadrants.
                int quadrants = 0;
                for (int i = 0; i < count; i++)
                {
                    int y = queue[i] / width, x = queue[i] % width;
                    int quadrant = (x * 2 > left + right ? 1 : 0) + (y * 2 > top + bottom ? 2 : 0);
                    quadrants |= 1 << quadrant;
                }
                if (quadrants == 3 || quadrants == 5 || quadrants == 6 || quadrants == 9 ||
                    quadrants == 10 || quadrants == 12 || (quadrants & (quadrants - 1)) == 0)
                    continue;
                targets.Add(new CrabTarget(color - 1,
                    new Rectangle(area.X + left, area.Y + top, spanX, spanY)));
            }

            return new CrabObservation(targets.ToArray());
        }

        static byte Classify(int red, int green, int blue)
        {
            int max = Math.Max(red, Math.Max(green, blue));
            int delta = max - Math.Min(red, Math.Min(green, blue));
            if (max >= 155 && delta <= 36) return 4;
            if (max < 78 || delta < 31 || delta * 100 < max * 40) return 0;
            double hue;
            if (max == red) hue = 60.0 * (green - blue) / delta;
            else if (max == green) hue = 120.0 + 60.0 * (blue - red) / delta;
            else hue = 240.0 + 60.0 * (red - green) / delta;
            if (hue < 0) hue += 360;
            if (hue < 19 || hue >= 342) return 1;
            if (hue >= 34 && hue < 72) return 5;
            if (hue >= 75 && hue < 159) return 2;
            if (hue >= 180 && hue < 253) return 3;
            return 0;
        }
    }
}
