using System;
using System.Drawing;

namespace CrabWeighing
{
    static class FoamBox
    {
        // Look for a bright, neutral connected rim with horizontal and vertical sides.
        public static Rectangle Find(VideoFrame frame, Rectangle search)
        {
            search.Intersect(new Rectangle(0, 0, VideoFrame.Width, VideoFrame.Height));
            int w = search.Width / 2, h = search.Height / 2;
            if (w < 30 || h < 24) return Rectangle.Empty;
            byte[] mask = new byte[w * h];
            int[] queue = new int[mask.Length];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int p = ((search.Y + y * 2) * VideoFrame.Width + search.X + x * 2) * 3;
                int min = Math.Min(frame.Pixels[p], Math.Min(frame.Pixels[p + 1], frame.Pixels[p + 2]));
                int max = Math.Max(frame.Pixels[p], Math.Max(frame.Pixels[p + 1], frame.Pixels[p + 2]));
                if (min >= 156 && max - min <= 40) mask[y * w + x] = 1;
            }
            Rectangle best = Rectangle.Empty;
            double bestScore = 0;
            for (int i = 0; i < mask.Length; i++)
            {
                if (mask[i] != 1) continue;
                mask[i] = 2;
                queue[0] = i;
                int count = 1, l = w, r = 0, t = h, b = 0;
                for (int q = 0; q < count; q++)
                {
                    int y = queue[q] / w, x = queue[q] % w;
                    l = Math.Min(l, x); r = Math.Max(r, x);
                    t = Math.Min(t, y); b = Math.Max(b, y);
                    for (int yy = Math.Max(0, y - 1); yy <= Math.Min(h - 1, y + 1); yy++)
                    for (int xx = Math.Max(0, x - 1); xx <= Math.Min(w - 1, x + 1); xx++)
                    {
                        int next = yy * w + xx;
                        if (mask[next] != 1) continue;
                        mask[next] = 2;
                        queue[count++] = next;
                    }
                }

                int sx = r - l + 1, sy = b - t + 1, size = sx * sy;
                if (sx < 43 || sy < 34 || sx > 3.4 * sy || sy > 2.6 * sx ||
                    size < mask.Length * 0.10 || (size > mask.Length * 0.85 && count > size * 0.87) || count < size * 0.055) continue;
                var top = new bool[sx]; var bottom = new bool[sx];
                var left = new bool[sy]; var right = new bool[sy];
                int bandX = Math.Max(2, sx / 7), bandY = Math.Max(2, sy / 7);
                for (int q = 0; q < count; q++)
                {
                    int x = queue[q] % w - l, y = queue[q] / w - t;
                    if (y < bandY) top[x] = true;
                    if (y >= sy - bandY) bottom[x] = true;
                    if (x < bandX) left[y] = true;
                    if (x >= sx - bandX) right[y] = true;
                }
                double[] sides = { Coverage(top), Coverage(bottom), Coverage(left), Coverage(right) };
                Array.Sort(sides);
                if (sides[1] < 0.54 || sides[0] < 0.20) continue;
                double dx = (l + r - w) / (double)w, dy = (t + b - h) / (double)h;
                double score = size * (sides[0] + sides[1] + sides[2] + sides[3]) / (1 + dx * dx + dy * dy);
                if (score <= bestScore) continue;
                bestScore = score;
                best = Rectangle.FromLTRB(search.X + (l + bandX / 2) * 2, search.Y + (t + bandY / 2) * 2,
                    search.X + (r - bandX / 2) * 2, search.Y + (b - bandY / 2) * 2);
            }
            return best;
        }

        static double Coverage(bool[] band)
        {
            int count = 0;
            foreach (bool filled in band) if (filled) count++;
            return count / (double)band.Length;
        }
    }
}
