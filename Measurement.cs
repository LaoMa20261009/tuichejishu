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
        double recordedWeight;
        string recordedUnit;
        string recordedColors;
        public bool IsHolding(WeightSample weight, double tolerance, bool allowLoadChanges, string detected, bool allowColorChanges)
        {
            double changeLimit = Math.Max(2 * tolerance, 2 * Math.Pow(10, -weight.Digits));
            return recorded && (!allowColorChanges || string.IsNullOrEmpty(detected) || recordedColors == detected) &&
                (!allowLoadChanges ||
                (recordedUnit == weight.Unit && Math.Abs(weight.Value - recordedWeight) <= changeLimit));
        }

        public void ResetCandidate()
        {
            candidateTick = 0;
            emptyTick = 0;
            frames = 0;
            samples = 0;
        }

        public void StartBatch()
        {
            recorded = false;
            ResetCandidate();
        }

        public void MarkRecorded(WeightSample weight, string detected)
        {
            recorded = true;
            recordedWeight = weight.Value;
            recordedUnit = weight.Unit;
            recordedColors = detected;
            ResetCandidate();
        }

        public bool Observe(long now, WeightSample weight, double emptyLimit, double tolerance,
            string detected, long frameTick, bool auto, bool allowLoadChanges, bool allowColorChanges)
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
            if (IsHolding(weight, tolerance, allowLoadChanges, detected, allowColorChanges) || !auto || weight.Value <= emptyLimit || string.IsNullOrEmpty(detected))
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
        public readonly string Time, Color, Source;
        public readonly double Grams;
        public readonly int? Count;

        public MeasurementRecord(string time, string color, double grams, int? count, string source)
        {
            Time = time;
            Color = color;
            Grams = grams;
            Count = count;
            Source = source;
        }
    }

    sealed class History
    {
        public readonly string FilePath;

        public History(string filePath) { FilePath = filePath; }

        public IEnumerable<MeasurementRecord> Load()
        {
            string legacy = Path.Combine(Path.GetDirectoryName(FilePath), "measurements.tsv");
            if (File.Exists(legacy))
            {
                foreach (string line in File.ReadLines(legacy, Encoding.UTF8))
                {
                    string[] fields = line.Split('	');
                    double weight, grams;
                    if (fields.Length >= 5 && ValidRow(fields, out weight) &&
                        Basket.TryGrams(weight, fields[3], out grams))
                        yield return new MeasurementRecord(fields[0], fields[1], grams, null, fields[4]);
                }
            }
            if (!File.Exists(FilePath)) yield break;
            foreach (string line in File.ReadLines(FilePath, Encoding.UTF8))
            {
                string[] fields = line.Split('	');
                double grams;
                int count;
                if (fields.Length == 5 && ValidRow(fields, out grams) &&
                    int.TryParse(fields[3], NumberStyles.None, CultureInfo.InvariantCulture, out count) && count > 0)
                    yield return new MeasurementRecord(fields[0], fields[1], grams, count, fields[4]);
            }
        }

        static bool ValidRow(string[] fields, out double weight)
        {
            DateTimeOffset time;
            return double.TryParse(fields[2], NumberStyles.Float, CultureInfo.InvariantCulture, out weight) &&
                !double.IsNaN(weight) && !double.IsInfinity(weight) && fields[1].Length > 0 &&
                fields[1].IndexOf('、') < 0 &&
                DateTimeOffset.TryParseExact(fields[0], "o", CultureInfo.InvariantCulture, DateTimeStyles.None, out time);
        }

        public void Append(IEnumerable<MeasurementRecord> records)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            var lines = records.Select(record => string.Join("\t", record.Time, record.Color,
                record.Grams.ToString("F1", CultureInfo.InvariantCulture),
                record.Count.Value.ToString(CultureInfo.InvariantCulture), record.Source));
            byte[] bytes = Encoding.UTF8.GetBytes(string.Join("\r\n", lines) + "\r\n");
            using (var file = new FileStream(FilePath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read))
            {
                long originalSize = file.Length;
                file.Position = originalSize;
                try
                {
                    file.Write(bytes, 0, bytes.Length);
                    file.Flush(true);
                }
                catch
                {
                    file.SetLength(originalSize);
                    throw;
                }
            }
        }
    }
}
