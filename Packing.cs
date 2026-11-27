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
        public bool Finished { get; private set; }
        public readonly int[] Goals, Counts = new int[Basket.Colors.Length];
        public readonly double[] Weights = new double[Basket.Colors.Length];
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
            Finished = File.Exists(Path.Combine(Folder, "completed.txt"));
            string ledger = Path.Combine(Folder, "moves.tsv");
            if (!File.Exists(ledger)) return;
            foreach (string line in File.ReadLines(ledger))
            {
                var fields = line.Split('\t');
                DateTimeOffset at;
                int color, count;
                double delta, weight;
                if (fields.Length != 10 || !DateTimeOffset.TryParseExact(fields[0], "o", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out at) || !int.TryParse(fields[1], out color) || color < -1 || color >= Basket.Colors.Length ||
                    !int.TryParse(fields[2], out count) || !double.TryParse(fields[3], NumberStyles.Float,
                    CultureInfo.InvariantCulture, out delta) || !double.TryParse(fields[4], NumberStyles.Float,
                    CultureInfo.InvariantCulture, out weight)) continue;
                Track(new PackingMove { Time = at, Color = color, Count = count, Delta = delta, Weight = weight,
                    Spec = fields[5], Before = fields[6], After = fields[7], Target = fields[8], OutsideRange = fields[9] == "1" });
            }
        }

        public bool IsComplete { get { return Goals.Sum() > 0 && Goals.Select((goal, i) => Counts[i] >= goal).All(done => done); } }
        public int GoalsMet { get { return Goals.Select((goal, i) => Math.Min(goal, Math.Max(0, Counts[i]))).Sum(); } }

        public bool CanFinish
        {
            get { return !Finished && Goals.Sum() > 0 && Goals.SequenceEqual(Counts) && Moves.All(move => move.Color >= 0); }
        }

        public void Finish()
        {
            if (!CanFinish) throw new InvalidOperationException("请核对每种规格的数量，并处理待确认记录。");
            using (var file = new FileStream(Path.Combine(Folder, "completed.txt"), FileMode.CreateNew, FileAccess.Write))
            {
                byte[] stamp = Encoding.UTF8.GetBytes(DateTimeOffset.Now.ToString("o", CultureInfo.InvariantCulture));
                file.Write(stamp, 0, stamp.Length);
                file.Flush(true);
            }
            Finished = true;
        }

        public void SaveGoals()
        {
            if (Finished) return;
            Directory.CreateDirectory(Folder);
            File.WriteAllLines(Path.Combine(Folder, "task.txt"), Goals.Select(goal => goal.ToString(CultureInfo.InvariantCulture)));
        }

        public void Append(PackingMove move)
        {
            if (Finished) throw new InvalidOperationException("装箱工单已完成。");
            Directory.CreateDirectory(Folder);
            byte[] bytes = Encoding.UTF8.GetBytes(Serialize(move) + Environment.NewLine);
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

        public void ResolveLast(PackingMove confirmed)
        {
            if (Moves.Count == 0) throw new InvalidOperationException("没有可补全的称重记录。");
            Resolve(Moves[Moves.Count - 1], confirmed);
        }

        public void Resolve(PackingMove pending, PackingMove confirmed)
        {
            int index = Moves.IndexOf(pending);
            if (Finished || index < 0 || pending.Color != -1 || confirmed.Color < 0 || confirmed.Color >= Counts.Length ||
                confirmed.Count == 0 || Math.Sign(confirmed.Count) != Math.Sign(pending.Delta))
                throw new InvalidOperationException("这条记录无法按所选规格补全。");

            confirmed.Time = pending.Time;
            var newMoves = Moves.ToArray();
            newMoves[index] = confirmed;
            int[] checkedCounts = new int[Counts.Length];
            foreach (PackingMove move in newMoves)
            {
                if (move.Color < 0) continue;
                checkedCounts[move.Color] += move.Count;
                if (checkedCounts[move.Color] < 0)
                    throw new InvalidOperationException("核对后会导致该规格出现负数。请先核对较早的放入记录。");
            }

            string ledger = Path.Combine(Folder, "moves.tsv");
            string temporary = ledger + "." + Guid.NewGuid().ToString("N");
            try
            {
                File.WriteAllLines(temporary, newMoves.Select(Serialize), Encoding.UTF8);
                File.Replace(temporary, ledger, null);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            Moves.Clear();
            Array.Clear(Counts, 0, Counts.Length);
            Array.Clear(Weights, 0, Weights.Length);
            foreach (PackingMove move in newMoves) Track(move);
        }

        static string Serialize(PackingMove move)
        {
            return string.Join("\t", move.Time.ToString("o", CultureInfo.InvariantCulture),
                move.Color.ToString(), move.Count.ToString(), move.Delta.ToString("F1", CultureInfo.InvariantCulture),
                move.Weight.ToString("F1", CultureInfo.InvariantCulture), move.Spec.Replace('	', ' '),
                move.Before, move.After, move.Target, move.OutsideRange ? "1" : "0");
        }

        void Track(PackingMove move)
        {
            Moves.Add(move);
            if (move.Color < 0) return;
            Counts[move.Color] += move.Count;
            Weights[move.Color] += move.Delta;
            if (Counts[move.Color] == 0) Weights[move.Color] = 0;
        }
    }
}
