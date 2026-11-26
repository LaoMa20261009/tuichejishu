using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace CrabWeighing
{
    sealed class LabelSpec
    {
        public int Color, Quantity;
        public decimal Minimum, Maximum;
        public string Name;
    }

    sealed class LabelPhoto
    {
        sealed class Reading
        {
            public int Line, X, Y, Width;
            public string Text;
        }

        sealed class Hit
        {
            public Match Match;
            public double X;
            public int Y;
        }

        static readonly Regex Specification = new Regex(
            @"(?<low>\d{1,4}(?:\.\d+)?)\s*[-~—–－～至]\s*(?<high>\d{1,4}(?:\.\d+)?)\s*(?:g|克)\s*[/／]?\s*只?\s*[（(\[【]?(?<color>红|綠|绿|蓝|白)(?:色|绳)?", RegexOptions.IgnoreCase);
        static readonly Regex Quantity = new Regex(@"(?:数量|數量|数目|qty)[：:]?(?<count>\d{1,4})只?", RegexOptions.IgnoreCase);
        static readonly Regex NumberWithUnit = new Regex(@"[：:]?(?<count>\d{1,4})只(?![（(\[【])");

        public static async Task<LabelSpec[]> ReadAsync(string filename)
        {
            string executable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
            if (!File.Exists(executable)) throw new InvalidOperationException("标签 OCR 需要支持中文文字识别的 Windows 10 或 Windows 11。");
            string prepared = Path.Combine(Path.GetTempPath(), "crab-label-" + Guid.NewGuid().ToString("N") + ".png");
            try
            {
                await Task.Run(() => PrepareImage(filename, prepared));
                return await Recognize(executable, prepared);
            }
            finally
            {
                if (File.Exists(prepared)) File.Delete(prepared);
            }
        }

        static void PrepareImage(string source, string target)
        {
            using (Image photo = Image.FromFile(source))
            {
                if (photo.PropertyIdList.Contains(0x0112))
                {
                    byte orientation = photo.GetPropertyItem(0x0112).Value[0];
                    if (orientation == 3) photo.RotateFlip(RotateFlipType.Rotate180FlipNone);
                    else if (orientation == 6) photo.RotateFlip(RotateFlipType.Rotate90FlipNone);
                    else if (orientation == 8) photo.RotateFlip(RotateFlipType.Rotate270FlipNone);
                }
                double scale = Math.Min(1, 2200.0 / Math.Max(photo.Width, photo.Height));
                using (var resized = new Bitmap(Math.Max(1, (int)(photo.Width * scale)),
                    Math.Max(1, (int)(photo.Height * scale)), PixelFormat.Format24bppRgb))
                using (Graphics canvas = Graphics.FromImage(resized))
                {
                    canvas.Clear(Color.White);
                    canvas.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    canvas.DrawImage(photo, new Rectangle(0, 0, resized.Width, resized.Height));
                    resized.Save(target, ImageFormat.Png);
                }
            }
        }

        static async Task<LabelSpec[]> Recognize(string executable, string filename)
        {
            string path = Convert.ToBase64String(Encoding.UTF8.GetBytes(Path.GetFullPath(filename)));
            string script = Script.Replace("__PHOTO__", path);
            string command = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
            using (var process = Process.Start(new ProcessStartInfo(executable,
                "-NoProfile -NonInteractive -EncodedCommand " + command)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
                RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
            }))
            {
                Task<string> output = process.StandardOutput.ReadToEndAsync();
                Task<string> error = process.StandardError.ReadToEndAsync();
                bool finished = await Task.Run(() => process.WaitForExit(35000));
                if (!finished)
                {
                    if (!process.HasExited) process.Kill();
                    throw new TimeoutException("标签识别超时，请裁剪标签区域后重试。");
                }
                string data = await output;
                await error;
                if (process.ExitCode != 0)
                {
                    if (data.Contains("NO_CHINESE_OCR"))
                        throw new InvalidOperationException("Windows 缺少中文 OCR。请在语言设置中安装「中文（简体）」的文字识别功能。");
                    throw new InvalidOperationException("Windows 无法识别这张照片。请检查中文 OCR 是否可用，或把照片转为 JPG / PNG。");
                }
                return Parse(data);
            }
        }

        internal static LabelSpec[] Parse(string data)
        {
            var readings = new List<Reading>();
            foreach (string line in data.Split('\n'))
            {
                var fields = line.TrimEnd('\r').Split('\t');
                int number, x, y, width;
                if (fields.Length != 5 || !int.TryParse(fields[0], out number) || !int.TryParse(fields[1], out x) ||
                    !int.TryParse(fields[2], out y) || !int.TryParse(fields[3], out width)) continue;
                try
                {
                    readings.Add(new Reading { Line = number, X = x, Y = y, Width = width,
                        Text = Encoding.UTF8.GetString(Convert.FromBase64String(fields[4])) });
                }
                catch (FormatException) { }
            }

            if (readings.Count == 0) return new LabelSpec[0];
            double columnTolerance = Math.Max(105, 0.15 * readings.Max(word => word.X + word.Width));
            double rowTolerance = Math.Max(105, 0.20 * readings.Max(word => word.Y));
            var groups = readings.GroupBy(word => word.Line).Select(group => group.OrderBy(word => word.X).ToArray()).ToArray();
            var specs = groups.SelectMany(line => Find(line, Specification)).ToArray();
            var quantities = groups.SelectMany(line =>
            {
                var labeled = Find(line, Quantity);
                // A degraded photo can still be readable when OCR recognizes the count and 只.
                return labeled.Length > 0 ? labeled : Find(line, NumberWithUnit);
            }).ToList();
            var result = new List<LabelSpec>();
            foreach (Hit spec in specs.OrderBy(hit => hit.Y).ThenBy(hit => hit.X))
            {
                Hit quantity = quantities.Where(hit => hit.Y > spec.Y && hit.Y - spec.Y <= rowTolerance && Math.Abs(hit.X - spec.X) <= columnTolerance)
                    .OrderBy(hit => hit.Y - spec.Y + Math.Abs(hit.X - spec.X) * 1.8).FirstOrDefault();
                if (quantity == null) continue;
                quantities.Remove(quantity);
                decimal low = decimal.Parse(spec.Match.Groups["low"].Value, CultureInfo.InvariantCulture);
                decimal high = decimal.Parse(spec.Match.Groups["high"].Value, CultureInfo.InvariantCulture);
                int count = int.Parse(quantity.Match.Groups["count"].Value, CultureInfo.InvariantCulture);
                if (low <= 0 || high < low || count <= 0) continue;
                string color = spec.Match.Groups["color"].Value;
                int index = color == "红" ? 0 : color == "绿" || color == "綠" ? 1 : color == "蓝" ? 2 : 3;
                string grade = low.ToString("0.#", CultureInfo.InvariantCulture) + "–" + high.ToString("0.#", CultureInfo.InvariantCulture) + "g";
                var existing = result.FirstOrDefault(item => item.Color == index);
                if (existing != null)
                {
                    if (existing.Minimum != low || existing.Maximum != high)
                        throw new InvalidOperationException("同一绳色含多个重量规格，请分开拍照并分别建单。");
                    existing.Quantity += count;
                    if (existing.Quantity > 10000) throw new InvalidOperationException("同一绳色的目标数量超过上限。");
                    continue;
                }
                result.Add(new LabelSpec { Color = index, Quantity = count, Minimum = low, Maximum = high, Name = grade });
            }
            return result.OrderBy(item => item.Color).ToArray();
        }

        static Hit[] Find(Reading[] words, Regex pattern)
        {
            var text = new StringBuilder();
            var x = new List<double>(); var y = new List<int>();
            foreach (Reading word in words)
            for (int index = 0; index < word.Text.Length; index++)
            {
                char character = word.Text[index];
                if (char.IsWhiteSpace(character)) continue;
                if (character >= '０' && character <= '９') character = (char)('0' + character - '０');
                text.Append(character);
                x.Add(word.X + index * word.Width / (double)Math.Max(1, word.Text.Length));
                y.Add(word.Y);
            }
            return pattern.Matches(text.ToString()).Cast<Match>().Select(match =>
            {
                int index = match.Groups["count"].Success ? match.Groups["count"].Index : match.Index;
                return new Hit { Match = match, X = x[index], Y = y[index] };
            }).ToArray();
        }

        const string Script = @"
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
try {
    Add-Type -AssemblyName System.Runtime.WindowsRuntime
    $null = [Windows.Storage.StorageFile, Windows.Storage, ContentType=WindowsRuntime]
    $null = [Windows.Storage.Streams.IRandomAccessStream, Windows.Storage.Streams, ContentType=WindowsRuntime]
    $null = [Windows.Graphics.Imaging.BitmapDecoder, Windows.Graphics.Imaging, ContentType=WindowsRuntime]
    $null = [Windows.Graphics.Imaging.SoftwareBitmap, Windows.Graphics.Imaging, ContentType=WindowsRuntime]
    $null = [Windows.Media.Ocr.OcrEngine, Windows.Foundation, ContentType=WindowsRuntime]
    $null = [Windows.Media.Ocr.OcrResult, Windows.Foundation, ContentType=WindowsRuntime]
    $null = [Windows.Globalization.Language, Windows.Globalization, ContentType=WindowsRuntime]
    $method = [System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object {
        $_.Name -eq 'AsTask' -and $_.IsGenericMethod -and $_.GetParameters().Count -eq 1 -and
        $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1'
    } | Select-Object -First 1
    function Wait-OcrOperation($operation, [Type] $type) {
        $task = $method.MakeGenericMethod($type).Invoke($null, @($operation))
        $task.Wait()
        $task.Result
    }
    $language = [Windows.Globalization.Language]::new('zh-Hans')
    $engine = [Windows.Media.Ocr.OcrEngine]::TryCreateFromLanguage($language)
    if ($null -eq $engine) { [Console]::WriteLine('NO_CHINESE_OCR'); exit 4 }
    $path = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('__PHOTO__'))
    $file = Wait-OcrOperation ([Windows.Storage.StorageFile]::GetFileFromPathAsync($path)) ([Windows.Storage.StorageFile])
    $stream = Wait-OcrOperation ($file.OpenAsync([Windows.Storage.FileAccessMode]::Read)) ([Windows.Storage.Streams.IRandomAccessStream])
    $decoder = Wait-OcrOperation ([Windows.Graphics.Imaging.BitmapDecoder]::CreateAsync($stream)) ([Windows.Graphics.Imaging.BitmapDecoder])
    $bitmap = Wait-OcrOperation ($decoder.GetSoftwareBitmapAsync()) ([Windows.Graphics.Imaging.SoftwareBitmap])
    $result = Wait-OcrOperation ($engine.RecognizeAsync($bitmap)) ([Windows.Media.Ocr.OcrResult])
    $line = 0
    foreach ($entry in $result.Lines) {
        $line++
        foreach ($word in $entry.Words) {
            $r = $word.BoundingRect
            $encoded = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($word.Text))
            [Console]::WriteLine((@($line, [int]$r.X, [int]$r.Y, [int]$r.Width, $encoded) -join [char]9))
        }
    }
    exit 0
} catch { [Console]::Error.WriteLine($_.Exception.Message); exit 5 }
";
    }
}
