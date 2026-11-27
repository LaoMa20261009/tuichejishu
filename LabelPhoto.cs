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
        public RectangleF TextArea;
    }

    sealed class LabelPhotoResult
    {
        public LabelSpec[] Specs = new LabelSpec[0];
        public string Engine = "";
        public string Transcript = "";
        public string Note = "";
        public string[] Barcodes = new string[0];
    }

    sealed class LabelPhoto
    {
        sealed class Reading
        {
            public int Line, X, Y, Width, Height;
            public string Text;
        }

        sealed class Hit
        {
            public Match Match;
            public double X;
            public int Y;
            public double Right;
        }

        static readonly Regex Specification = new Regex(
            @"(?<![0-9.])(?:(?<low>[0-9]{1,4}(?:\.[0-9]+)?)[-~—–−至](?<high>[0-9]{1,4}(?:\.[0-9]+)?)|" +
            @"(?<low>[0-9]{2,3})\.(?<high>[0-9]{2,3})(?=[g克/])|" +
            @"(?<low>[0-9]{3})(?<high>[0-9]{3})(?=[g克/]))(?:g|克|¢)?(?:v(?=/))?(?:[/\\]?(?:只|隻|支|R))?[([【]?(?<color>红|紅|綠|绿|蓝|藍|白|黄|黃)(?:色|绳|繩)?",
            RegexOptions.IgnoreCase);
        static readonly Regex Quantity = new Regex(@"(?:数量|數量|数目|qty)[:;]?(?<count>[0-9Oo]{1,4})(?:只|隻|支)?", RegexOptions.IgnoreCase);
        static readonly Regex NumberWithUnit = new Regex(@"(?<![0-9Gg克/])[:;]?(?<count>[0-9]{1,4})(?:只|隻|支)(?![([【])");
        static readonly string[] RegionNames = { "整图", "左上", "右上", "左下", "右下" };

        public static async Task<LabelPhotoResult> ReadAsync(string filename)
        {
            string temporary = Path.Combine(Path.GetTempPath(), "crab-label-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temporary);
            try
            {
                string[] photos = await Task.Run(() => PrepareImages(filename, temporary));
                var result = new LabelPhotoResult();
                string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                string executable = Path.Combine(windows, "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
                if (File.Exists(executable))
                {
                    try
                    {
                        string[] readings = await RecognizeWindows(executable, photos);
                        result = JoinRegions(readings, "Windows 中文 OCR", photos[0]);
                    }
                    catch (InvalidOperationException ex) { result.Note = ex.Message; }
                    catch (TimeoutException ex) { result.Note = ex.Message; }
                    catch (System.ComponentModel.Win32Exception) { result.Note = "Windows OCR 无法启动。"; }
                }
                else result.Note = "当前环境没有 Windows 中文 OCR。";

                result.Barcodes = await Task.Run(() => ReadBarcodes(photos));
                if (result.Specs.Length >= 3) return result;
                try
                {
                    string[] readings = await RecognizeTesseract(photos);
                    LabelPhotoResult extra = JoinRegions(readings, "Tesseract 中文 OCR", photos[0]);
                    if (extra.Transcript.Length > 0)
                    {
                        result.Specs = Combine(new[] { result.Specs, extra.Specs });
                        result.Transcript = string.Join(Environment.NewLine + Environment.NewLine,
                            new[] { result.Transcript, extra.Transcript }.Where(text => text.Length > 0));
                        result.Engine = result.Engine.Length == 0 ? extra.Engine : result.Engine + " / " + extra.Engine;
                    }
                }
                catch (System.ComponentModel.Win32Exception) { }
                catch (TimeoutException) { }
                if (result.Specs.Length == 0 && result.Note.Length == 0)
                    result.Note = "OCR 已运行，但没有配对出完整的绳色、规格和数量。";
                return result;
            }
            finally
            {
                try { Directory.Delete(temporary, true); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        static string[] ReadBarcodes(string[] files)
        {
            var codes = new HashSet<string>();
            foreach (string file in files)
                using (var bitmap = new Bitmap(file))
                    codes.UnionWith(BoxBarcode.Read(bitmap));
            return codes.ToArray();
        }

        static string[] PrepareImages(string source, string directory)
        {
            using (Image photo = Image.FromFile(source))
            {
                if (photo.PropertyIdList.Contains(0x0112))
                {
                    byte[] value = photo.GetPropertyItem(0x0112).Value;
                    if (value.Length > 0 && value[0] == 3) photo.RotateFlip(RotateFlipType.Rotate180FlipNone);
                    else if (value.Length > 0 && value[0] == 6) photo.RotateFlip(RotateFlipType.Rotate90FlipNone);
                    else if (value.Length > 0 && value[0] == 8) photo.RotateFlip(RotateFlipType.Rotate270FlipNone);
                }
                int width = photo.Width, height = photo.Height;
                var crops = new List<Rectangle> { new Rectangle(0, 0, width, height) };
                if (width >= 320 && height >= 220)
                {
                    int left = width * 42 / 100, right = width * 58 / 100;
                    int top = height * 42 / 100, bottom = height * 58 / 100;
                    crops.Add(Rectangle.FromLTRB(0, 0, right, bottom));
                    crops.Add(Rectangle.FromLTRB(left, 0, width, bottom));
                    crops.Add(Rectangle.FromLTRB(0, top, right, height));
                    crops.Add(Rectangle.FromLTRB(left, top, width, height));
                }
                var files = new List<string>();
                foreach (Rectangle crop in crops)
                {
                    double scale = Math.Min(2200.0 / Math.Max(crop.Width, crop.Height),
                        Math.Max(1, 1450.0 / Math.Max(crop.Width, crop.Height)));
                    using (var resized = new Bitmap(Math.Max(1, (int)(crop.Width * scale)),
                        Math.Max(1, (int)(crop.Height * scale)), PixelFormat.Format24bppRgb))
                    using (Graphics canvas = Graphics.FromImage(resized))
                    {
                        canvas.Clear(Color.White);
                        canvas.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                        canvas.DrawImage(photo, new Rectangle(0, 0, resized.Width, resized.Height), crop, GraphicsUnit.Pixel);
                        string file = Path.Combine(directory, files.Count + ".png");
                        resized.Save(file, ImageFormat.Png);
                        files.Add(file);
                    }
                }
                return files.ToArray();
            }
        }

        static async Task<string[]> RecognizeWindows(string executable, string[] photos)
        {
            string names = Convert.ToBase64String(Encoding.UTF8.GetBytes(string.Join("|", photos)));
            string script = Script.Replace("__PHOTOS__", names);
            string command = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
            string error;
            var result = await RunProcess(executable, "-NoProfile -NonInteractive -EncodedCommand " + command, 50000);
            error = result.Item2;
            if (result.Item3 != 0)
            {
                if (result.Item1.Contains("NO_CHINESE_OCR"))
                    throw new InvalidOperationException("Windows 未安装中文文字识别功能。");
                if (error.IndexOf("Access", StringComparison.OrdinalIgnoreCase) >= 0)
                    throw new InvalidOperationException("Windows 拒绝调用 OCR，请检查系统设置或使用手动核对。");
                string detail = error.Trim();
                throw new InvalidOperationException("Windows 中文 OCR 调用失败。" +
                    (detail.Length == 0 ? "" : Environment.NewLine + detail.Substring(0, Math.Min(320, detail.Length))));
            }
            return result.Item1.Split(new[] { "REGION_START" }, StringSplitOptions.None).Skip(1).ToArray();
        }

        static async Task<string[]> RecognizeTesseract(string[] photos)
        {
            string executable = FindTesseract();
            var result = new List<string>();
            for (int i = 0; i < photos.Length; i++)
            {
                var output = await RunProcess(executable, CameraSession.Quote(photos[i]) +
                    " stdout -l chi_sim --psm " + (i == 0 ? "11" : "6") + " -c tessedit_create_tsv=1", 15000);
                if (output.Item3 != 0) break;
                result.Add(ConvertTesseract(output.Item1));
            }
            return result.ToArray();
        }

        static string FindTesseract()
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT) return "tesseract";
            string[] folders = { AppDomain.CurrentDomain.BaseDirectory,
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetEnvironmentVariable("ProgramW6432") ?? "" };
            foreach (string folder in folders.Where(path => path.Length > 0))
            {
                string direct = Path.Combine(folder, "tesseract.exe");
                if (File.Exists(direct)) return direct;
                string installed = Path.Combine(folder, "Tesseract-OCR", "tesseract.exe");
                if (File.Exists(installed)) return installed;
            }
            return "tesseract.exe";
        }

        static string ConvertTesseract(string tsv)
        {
            var result = new StringBuilder();
            var lines = new Dictionary<string, int>();
            foreach (string row in tsv.Split('\n'))
            {
                string[] fields = row.TrimEnd('\r').Split(new[] { '\t' }, 12);
                if (fields.Length != 12 || fields[0] != "5" || string.IsNullOrWhiteSpace(fields[11])) continue;
                string key = string.Join("/", fields.Skip(1).Take(4));
                int line;
                if (!lines.TryGetValue(key, out line)) lines.Add(key, line = lines.Count + 1);
                result.AppendLine(string.Join("\t", line.ToString(), fields[6], fields[7], fields[8], fields[9],
                    Convert.ToBase64String(Encoding.UTF8.GetBytes(fields[11]))));
            }
            return result.ToString();
        }

        static async Task<Tuple<string, string, int>> RunProcess(string executable, string arguments, int timeout)
        {
            using (var process = Process.Start(new ProcessStartInfo(executable, arguments)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
                RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
            }))
            {
                Task<string> output = process.StandardOutput.ReadToEndAsync();
                Task<string> error = process.StandardError.ReadToEndAsync();
                bool finished = await Task.Run(() => process.WaitForExit(timeout));
                if (!finished)
                {
                    if (!process.HasExited) process.Kill();
                    throw new TimeoutException("标签识别超时，可裁剪到标签区域后重试。");
                }
                return Tuple.Create(await output, await error, process.ExitCode);
            }
        }

        static LabelPhotoResult JoinRegions(string[] readings, string engine, string wholeImage)
        {
            var transcript = new List<string>();
            var passes = new List<LabelSpec[]>();
            Size dimensions;
            using (Image image = Image.FromFile(wholeImage)) dimensions = image.Size;
            for (int i = 0; i < readings.Length; i++)
            {
                Reading[] words = ReadWords(readings[i]);
                if (words.Length == 0) continue;
                var lines = words.GroupBy(word => word.Line).Select(line =>
                    string.Join(" ", line.OrderBy(word => word.X).Select(word => word.Text)));
                transcript.Add("[" + engine + " · " + RegionNames[i] + "]" + Environment.NewLine +
                    string.Join(Environment.NewLine, lines));
                var specs = ParseWords(words);
                foreach (var spec in specs)
                {
                    var box = spec.TextArea;
                    spec.TextArea = i != 0 ? RectangleF.Empty : new RectangleF(box.X / dimensions.Width,
                        box.Y / dimensions.Height, box.Width / dimensions.Width, box.Height / dimensions.Height);
                }
                passes.Add(specs);
            }
            return new LabelPhotoResult { Specs = Combine(passes), Engine = engine,
                Transcript = string.Join(Environment.NewLine + Environment.NewLine, transcript) };
        }

        static LabelSpec[] Combine(IEnumerable<LabelSpec[]> passes)
        {
            // A specification seen in overlapping crops is the same label, not another order line.
            return passes.SelectMany(pass => pass).GroupBy(item => item.Color).Select(group => group.First())
                .OrderBy(item => item.Color).ToArray();
        }

        internal static LabelSpec[] Parse(string data)
        {
            return ParseWords(ReadWords(data));
        }

        static Reading[] ReadWords(string data)
        {
            var readings = new List<Reading>();
            foreach (string line in data.Split('\n'))
            {
                var fields = line.TrimEnd('\r').Split('\t');
                int number, x, y, width, height;
                if ((fields.Length != 5 && fields.Length != 6) || !int.TryParse(fields[0], out number) ||
                    !int.TryParse(fields[1], out x) || !int.TryParse(fields[2], out y) || !int.TryParse(fields[3], out width)) continue;
                try
                {
                    string text = Encoding.UTF8.GetString(Convert.FromBase64String(fields[fields.Length - 1]));
                    if (text.Length == 0) continue;
                    if (fields.Length == 5 || !int.TryParse(fields[4], out height))
                        height = Math.Max(10, width / text.Length);
                    readings.Add(new Reading { Line = number, X = x, Y = y, Width = width, Height = Math.Max(1, height), Text = text });
                }
                catch (FormatException) { }
            }
            return readings.ToArray();
        }

        static LabelSpec[] ParseWords(Reading[] readings)
        {
            if (readings.Length == 0) return new LabelSpec[0];
            int medianHeight = readings.Select(word => word.Height).OrderBy(height => height).ElementAt(readings.Length / 2);
            double columnTolerance = Math.Max(5.5 * medianHeight, 0.11 * readings.Max(word => word.X + word.Width));
            double rowTolerance = Math.Max(6 * medianHeight, 0.13 * readings.Max(word => word.Y));
            var original = readings.GroupBy(word => word.Line).Select(group => group.OrderBy(word => word.X).ToArray()).ToArray();
            var rows = new List<List<Reading>>();
            foreach (Reading word in readings.OrderBy(word => word.Y + word.Height / 2))
            {
                var row = rows.LastOrDefault();
                if (row == null || Math.Abs(word.Y - row[0].Y) > Math.Max(6, medianHeight * 0.55))
                    rows.Add(row = new List<Reading>());
                row.Add(word);
            }
            var spatial = rows.SelectMany(row => SplitColumns(row, Math.Max(28, medianHeight * 2.7))).ToArray();
            var groups = original.Concat(spatial).ToArray();
            // Try a continuation below a weight line when OCR emitted the color on a second line.
            var continuations = spatial.Where(line => Find(line, Specification).Length == 0 &&
                    line.Any(word => word.Text.Any(char.IsDigit)))
                .SelectMany(line => spatial.Where(next => next[0].Y > line[0].Y &&
                    next[0].Y - line[0].Y < medianHeight * 2.1 &&
                    (next[0].X >= line[0].X - columnTolerance / 2 &&
                     next[0].X < line.Max(word => word.X + word.Width) + medianHeight) &&
                    next.Any(word => word.Text.IndexOfAny(new[] { '红', '紅', '绿', '綠', '蓝', '藍', '白', '黄', '黃' }) >= 0) &&
                    !next.Any(word => word.Text.Any(char.IsDigit)))
                    .Take(1).Select(next => line.Concat(next).ToArray()));
            var specs = DistinctHits(groups.Concat(continuations).SelectMany(line => Find(line, Specification)), medianHeight);
            var quantities = DistinctHits(groups.SelectMany(line =>
            {
                var labeled = Find(line, Quantity);
                return labeled.Length > 0 ? labeled : Find(line, NumberWithUnit);
            }), medianHeight).ToList();
            var result = new List<LabelSpec>();
            foreach (Hit spec in specs.OrderBy(hit => hit.Y).ThenBy(hit => hit.X))
            {
                Hit quantity = quantities.Where(hit => (hit.Y > spec.Y + medianHeight * 0.38 &&
                    hit.Y - spec.Y <= rowTolerance && Math.Abs(hit.X - spec.X) <= columnTolerance &&
                    !specs.Any(other => other.Y > spec.Y + medianHeight && other.Y < hit.Y &&
                        Math.Abs(other.X - spec.X) < columnTolerance / 2)) ||
                    (Math.Abs(hit.Y - spec.Y) <= medianHeight * 0.5 && hit.X > spec.Right &&
                        hit.X - spec.Right <= columnTolerance))
                    .OrderBy(hit => Math.Abs(hit.Y - spec.Y) + Math.Abs(hit.X - spec.X) * 1.35).FirstOrDefault();
                if (quantity == null) continue;
                quantities.Remove(quantity);
                decimal low = decimal.Parse(spec.Match.Groups["low"].Value, CultureInfo.InvariantCulture);
                decimal high = decimal.Parse(spec.Match.Groups["high"].Value, CultureInfo.InvariantCulture);
                int count = int.Parse(quantity.Match.Groups["count"].Value.Replace('O', '0').Replace('o', '0'), CultureInfo.InvariantCulture);
                if (low <= 0 || high < low || count <= 0) continue;
                string color = spec.Match.Groups["color"].Value;
                int index = color == "红" || color == "紅" ? 0 : color == "绿" || color == "綠" ? 1 : color == "蓝" || color == "藍" ? 2 : color == "白" ? 3 : 4;
                var existing = result.FirstOrDefault(item => item.Color == index);
                if (existing != null)
                {
                    if (existing.Minimum != low || existing.Maximum != high) continue;
                    existing.Quantity += count;
                    if (existing.Quantity > 10000) throw new InvalidOperationException("同一绳色的目标数量超过上限。");
                    continue;
                }
                float left = (float)Math.Min(spec.X, quantity.X) - medianHeight * 2;
                float right = (float)Math.Max(spec.Right, quantity.Right) + medianHeight;
                result.Add(new LabelSpec { Color = index, Quantity = count, Minimum = low, Maximum = high,
                    TextArea = RectangleF.FromLTRB(Math.Max(0, left), Math.Min(spec.Y, quantity.Y) - 3,
                        right, Math.Max(spec.Y, quantity.Y) + medianHeight + 3) });
            }
            return result.OrderBy(item => item.Color).ToArray();
        }

        static IEnumerable<Reading[]> SplitColumns(IEnumerable<Reading> row, double gap)
        {
            var words = new List<Reading>();
            int right = 0;
            foreach (Reading word in row.OrderBy(word => word.X))
            {
                if (words.Count > 0 && word.X - right > gap)
                {
                    yield return words.ToArray();
                    words.Clear();
                }
                words.Add(word);
                right = word.X + word.Width;
            }
            if (words.Count > 0) yield return words.ToArray();
        }

        static Hit[] DistinctHits(IEnumerable<Hit> hits, int height)
        {
            var result = new List<Hit>();
            foreach (Hit hit in hits)
                if (!result.Any(other => Math.Abs(hit.X - other.X) <= height &&
                    Math.Abs(hit.Y - other.Y) <= height * 0.6))
                    result.Add(hit);
            return result.ToArray();
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
                if (character >= '！' && character <= '～') character = (char)(character - 0xfee0);
                text.Append(character);
                x.Add(word.X + index * word.Width / (double)Math.Max(1, word.Text.Length));
                y.Add(word.Y);
            }
            return pattern.Matches(text.ToString()).Cast<Match>().Select(match =>
            {
                int index = match.Groups["count"].Success ? match.Groups["count"].Index : match.Index;
                return new Hit { Match = match, X = x[index], Y = y[index], Right = x[match.Index + match.Length - 1] };
            }).ToArray();
        }

        const string Script = @"
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
try {
    Add-Type -AssemblyName System.Runtime.WindowsRuntime
    $null = [Windows.Storage.StorageFile, Windows.Storage, ContentType=WindowsRuntime]
    $null = [Windows.Storage.FileAccessMode, Windows.Storage, ContentType=WindowsRuntime]
    $null = [Windows.Storage.Streams.IRandomAccessStream, Windows.Storage.Streams, ContentType=WindowsRuntime]
    $null = [Windows.Graphics.Imaging.BitmapDecoder, Windows.Graphics.Imaging, ContentType=WindowsRuntime]
    $null = [Windows.Graphics.Imaging.BitmapPixelFormat, Windows.Graphics.Imaging, ContentType=WindowsRuntime]
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
    $engine = $null
    $languages = [Windows.Media.Ocr.OcrEngine]::AvailableRecognizerLanguages | Where-Object {
        $_.LanguageTag -match '^zh-(Hans|CN|SG)'
    }
    foreach ($language in $languages) {
        $engine = [Windows.Media.Ocr.OcrEngine]::TryCreateFromLanguage($language)
        if ($null -ne $engine) { break }
    }
    if ($null -eq $engine) { [Console]::WriteLine('NO_CHINESE_OCR'); exit 4 }
    $paths = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('__PHOTOS__')).Split('|')
    $completed = 0
    foreach ($path in $paths) {
        [Console]::WriteLine('REGION_START')
        try {
            $file = Wait-OcrOperation ([Windows.Storage.StorageFile]::GetFileFromPathAsync($path)) ([Windows.Storage.StorageFile])
            $stream = Wait-OcrOperation ($file.OpenAsync([Windows.Storage.FileAccessMode]::Read)) ([Windows.Storage.Streams.IRandomAccessStream])
            $decoder = Wait-OcrOperation ([Windows.Graphics.Imaging.BitmapDecoder]::CreateAsync($stream)) ([Windows.Graphics.Imaging.BitmapDecoder])
            $bitmap = Wait-OcrOperation ($decoder.GetSoftwareBitmapAsync()) ([Windows.Graphics.Imaging.SoftwareBitmap])
            $gray = [Windows.Graphics.Imaging.SoftwareBitmap]::Convert($bitmap, [Windows.Graphics.Imaging.BitmapPixelFormat]::Gray8)
            $result = Wait-OcrOperation ($engine.RecognizeAsync($gray)) ([Windows.Media.Ocr.OcrResult])
            $completed++
            $line = 0
            foreach ($entry in $result.Lines) {
                $line++
                foreach ($word in $entry.Words) {
                    $r = $word.BoundingRect
                    $encoded = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($word.Text))
                    [Console]::WriteLine((@($line, [int]$r.X, [int]$r.Y, [int]$r.Width, [int]$r.Height, $encoded) -join [char]9))
                }
            }
        } catch {
            [Console]::Error.WriteLine($_.Exception.Message)
        } finally {
            if ($null -ne $gray) { $gray.Dispose(); $gray = $null }
            if ($null -ne $bitmap) { $bitmap.Dispose(); $bitmap = $null }
            if ($null -ne $stream) { $stream.Dispose(); $stream = $null }
        }
    }
    if ($completed -eq 0) { exit 5 }
    exit 0
} catch { [Console]::Error.WriteLine($_.Exception.Message); exit 5 }
";
    }
}
