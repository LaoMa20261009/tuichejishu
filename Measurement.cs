using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace CrabWeighing
{
    static class RopeColor
    {
        static readonly string[] Names = { "白色", "红色", "橙色", "黄色", "绿色", "蓝色", "紫色" };
        static readonly Point[] Directions = { new Point(1, 0), new Point(0, 1), new Point(1, 1), new Point(1, -1) };

        public static string Detect(VideoFrame frame, Rectangle area, double minimumCoverage)
        {
            area.Intersect(new Rectangle(0, 0, VideoFrame.Width, VideoFrame.Height));
            if (area.Width < 24 || area.Height < 24) return "";
            byte[] pixels = frame.Pixels;
            int width = area.Width, height = area.Height;
            var mask = new byte[width * height];

            for (int y = 0; y < height; y++)
            {
                int pixel = ((area.Top + y) * VideoFrame.Width + area.Left) * 3;
                for (int x = 0; x < width; x++, pixel += 3)
                    mask[y * width + x] = Classify(pixels[pixel + 2], pixels[pixel + 1], pixels[pixel]);
            }

            var counts = new int[Names.Length];
            for (int y = 6; y < height - 6; y++)
            {
                for (int x = 6; x < width - 6; x++)
                {
                    int index = y * width + x;
                    byte color = mask[index];
                    if (color == 0) continue;
                    foreach (Point direction in Directions)
                    {
                        int along = direction.Y * width + direction.X;
                        int across = direction.X * width - direction.Y;
                        if (mask[index - 3 * along] == color && mask[index + 3 * along] == color &&
                            mask[index - 6 * across] != color && mask[index + 6 * across] != color)
                        {
                            counts[color - 1]++;
                            break;
                        }
                    }
                }
            }

            int required = Math.Max(18, (int)Math.Ceiling(width * height * minimumCoverage / 100));
            return string.Join("、", Names.Where((name, index) => counts[index] >= required));
        }

        static byte Classify(int red, int green, int blue)
        {
            int max = Math.Max(red, Math.Max(green, blue));
            int delta = max - Math.Min(red, Math.Min(green, blue));
            if (max >= 170 && delta <= 34) return 1;
            if (max < 85 || delta < 42 || delta * 100 < max * 44) return 0;

            double hue;
            if (max == red) hue = 60.0 * (green - blue) / delta;
            else if (max == green) hue = 120.0 + 60.0 * (blue - red) / delta;
            else hue = 240.0 + 60.0 * (red - green) / delta;
            if (hue < 0) hue += 360;
            if (hue < 15 || hue >= 345) return 2;
            if (hue < 42) return 3;
            if (hue < 69) return 4;
            if (hue < 155) return 5;
            if (hue < 250) return 6;
            return 7;
        }
    }

    sealed class CaptureGate
    {
        const double StableSeconds = 1.0;
        const double UnloadSeconds = 0.6;
        long candidateTick;
        long emptyTick;
        long lastFrameTick;
        long lastWeightTick;
        int frames;
        int samples;
        double minimum;
        double maximum;
        string unit;
        string colors;
        bool recorded;
        public bool HasRecorded { get { return recorded; } }

        public void ResetCandidate()
        {
            candidateTick = 0;
            emptyTick = 0;
            frames = 0;
            samples = 0;
        }

        public void MarkRecorded()
        {
            recorded = true;
            ResetCandidate();
        }

        public bool Observe(long now, WeightSample weight, double emptyLimit, double tolerance,
            string detected, long frameTick, bool auto)
        {
            if (weight == null || !weight.Valid || !weight.Stable)
            {
                ResetCandidate();
                return false;
            }

            if (Math.Abs(weight.Value) <= emptyLimit)
            {
                candidateTick = 0;
                if (emptyTick == 0) emptyTick = now;
                if (Elapsed(now, emptyTick) >= UnloadSeconds) recorded = false;
                return false;
            }

            emptyTick = 0;
            if (recorded || !auto || weight.Value <= emptyLimit || string.IsNullOrEmpty(detected))
            {
                candidateTick = 0;
                return false;
            }

            if (candidateTick == 0 || unit != weight.Unit || colors != detected ||
                Math.Max(maximum, weight.Value) - Math.Min(minimum, weight.Value) > tolerance)
            {
                candidateTick = now;
                minimum = maximum = weight.Value;
                unit = weight.Unit;
                colors = detected;
                lastFrameTick = 0;
                lastWeightTick = 0;
                frames = 0;
                samples = 0;
            }
            minimum = Math.Min(minimum, weight.Value);
            maximum = Math.Max(maximum, weight.Value);
            if (lastWeightTick != weight.Tick)
            {
                samples++;
                lastWeightTick = weight.Tick;
            }
            if (lastFrameTick == frameTick) return false;
            frames++;
            lastFrameTick = frameTick;
            return frames >= 3 && samples >= 2 && Elapsed(now, candidateTick) >= StableSeconds;
        }

        public static double Elapsed(long end, long start)
        {
            return (end - start) / (double)Stopwatch.Frequency;
        }
    }

    sealed class MeasurementRecord
    {
        public readonly string Time, Colors, Weight, Unit, Source;

        public MeasurementRecord(string time, string colors, string weight, string unit, string source)
        {
            Time = time;
            Colors = colors;
            Weight = weight;
            Unit = unit;
            Source = source;
        }

        public string[] Fields { get { return new[] { Time, Colors, Weight, Unit, Source }; } }
    }

    sealed class History
    {
        public readonly string FilePath;

        public History(string filePath) { FilePath = filePath; }

        public IEnumerable<MeasurementRecord> Load()
        {
            if (!File.Exists(FilePath)) yield break;
            foreach (string line in File.ReadLines(FilePath, Encoding.UTF8))
            {
                string[] fields = line.Split('\t');
                DateTimeOffset time;
                double weight;
                if (fields.Length == 5 &&
                    DateTimeOffset.TryParseExact(fields[0], "o", CultureInfo.InvariantCulture, DateTimeStyles.None, out time) &&
                    double.TryParse(fields[2], NumberStyles.Float, CultureInfo.InvariantCulture, out weight))
                    yield return new MeasurementRecord(fields[0], fields[1], fields[2], fields[3], fields[4]);
            }
        }

        public void Append(MeasurementRecord record)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            string line = string.Join("\t", record.Fields.Select(field =>
                field.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ')));
            using (var file = new FileStream(FilePath, FileMode.Append, FileAccess.Write, FileShare.Read))
            {
                byte[] bytes = Encoding.UTF8.GetBytes(line + "\r\n");
                file.Write(bytes, 0, bytes.Length);
                file.Flush(true);
            }
        }
    }
}
