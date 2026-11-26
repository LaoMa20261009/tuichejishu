using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace CrabWeighing
{
    sealed class PackingMove
    {
        public DateTimeOffset Time;
        public int Color, Count;
        public double Delta, Weight;
        public string Spec, Before, After, Target;
        public bool OutsideRange;

        public static PackingMove Detect(int[] previous, int[] current, double previousWeight, double weight, double tolerance)
        {
            int color = -1, count = 0;
            for (int i = 0; i < current.Length; i++)
            {
                int change = current[i] - previous[i];
                if (change == 0) continue;
                if (color != -1) return null;
                color = i;
                count = change;
            }
            double delta = weight - previousWeight;
            if (color < 0 || Math.Abs(delta) <= tolerance || Math.Sign(delta) != Math.Sign(count)) return null;
            return new PackingMove { Time = DateTimeOffset.Now, Color = color, Count = count, Delta = delta, Weight = weight };
        }
    }

    sealed class PackingOrder
    {
        public readonly string Code, Folder;
        public readonly int[] Goals, Counts = new int[4];
        public readonly double[] Weights = new double[4];
        public readonly List<PackingMove> Moves = new List<PackingMove>();

        public PackingOrder(string root, string code, int[] goals)
        {
            if (string.IsNullOrWhiteSpace(code) || code.Length > 60 ||
                code.Any(c => char.IsControl(c))) throw new ArgumentException("请输入有效箱码。");
            Code = code;
            Goals = goals;
            byte[] hash;
            using (var sha = System.Security.Cryptography.SHA256.Create())
                hash = sha.ComputeHash(Encoding.UTF8.GetBytes(code));
            Folder = Path.Combine(root, BitConverter.ToString(hash).Replace("-", ""));
            string ledger = Path.Combine(Folder, "moves.tsv");
            if (!File.Exists(ledger)) return;
            foreach (string line in File.ReadLines(ledger))
            {
                var fields = line.Split('\t');
                DateTimeOffset at;
                int color, count;
                double delta, weight;
                if (fields.Length != 10 || !DateTimeOffset.TryParseExact(fields[0], "o", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out at) || !int.TryParse(fields[1], out color) || color < 0 || color >= 4 ||
                    !int.TryParse(fields[2], out count) || !double.TryParse(fields[3], NumberStyles.Float,
                    CultureInfo.InvariantCulture, out delta) || !double.TryParse(fields[4], NumberStyles.Float,
                    CultureInfo.InvariantCulture, out weight)) continue;
                Track(new PackingMove { Time = at, Color = color, Count = count, Delta = delta, Weight = weight,
                    Spec = fields[5], Before = fields[6], After = fields[7], Target = fields[8], OutsideRange = fields[9] == "1" });
            }
        }

        public bool IsComplete { get { return Goals.Sum() > 0 && Goals.Select((goal, i) => Counts[i] >= goal).All(done => done); } }
        public int GoalsMet { get { return Goals.Select((goal, i) => Math.Min(goal, Math.Max(0, Counts[i]))).Sum(); } }

        public void SaveGoals()
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllLines(Path.Combine(Folder, "task.txt"), Goals.Select(goal => goal.ToString(CultureInfo.InvariantCulture)));
        }

        public void Append(PackingMove move)
        {
            Directory.CreateDirectory(Folder);
            string line = string.Join("\t", move.Time.ToString("o", CultureInfo.InvariantCulture),
                move.Color.ToString(), move.Count.ToString(), move.Delta.ToString("F1", CultureInfo.InvariantCulture),
                move.Weight.ToString("F1", CultureInfo.InvariantCulture), move.Spec.Replace('\t', ' '),
                move.Before, move.After, move.Target, move.OutsideRange ? "1" : "0");
            byte[] bytes = Encoding.UTF8.GetBytes(line + Environment.NewLine);
            using (var ledger = new FileStream(Path.Combine(Folder, "moves.tsv"), FileMode.OpenOrCreate,
                FileAccess.Write, FileShare.Read))
            {
                long length = ledger.Length;
                ledger.Position = length;
                try
                {
                    ledger.Write(bytes, 0, bytes.Length);
                    ledger.Flush(true);
                }
                catch
                {
                    ledger.SetLength(length);
                    throw;
                }
            }
            Track(move);
        }

        void Track(PackingMove move)
        {
            Moves.Add(move);
            Counts[move.Color] += move.Count;
            Weights[move.Color] += move.Delta;
            if (Counts[move.Color] == 0) Weights[move.Color] = 0;
        }
    }
}
