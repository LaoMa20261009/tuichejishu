using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CrabWeighing
{
    enum CameraSource { Network, Usb, File, Demo }

    sealed class VideoFrame
    {
        public const int Width = 640;
        public const int Height = 360;
        public const int ByteCount = Width * Height * 3;
        public readonly byte[] Pixels;
        public readonly long Tick = Stopwatch.GetTimestamp();

        public readonly TimeSpan? Position;

        public VideoFrame(byte[] pixels, TimeSpan? position = null)
        {
            Pixels = pixels;
            Position = position;
        }

        public Bitmap ToBitmap()
        {
            var bitmap = new Bitmap(Width, Height, PixelFormat.Format24bppRgb);
            var data = bitmap.LockBits(new Rectangle(0, 0, Width, Height), ImageLockMode.WriteOnly,
                PixelFormat.Format24bppRgb);
            try { Marshal.Copy(Pixels, 0, data.Scan0, Pixels.Length); }
            finally { bitmap.UnlockBits(data); }
            return bitmap;
        }
    }

    sealed class CameraSession : IDisposable
    {
        readonly object processLock = new object();
        readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        readonly LocalVideo localVideo;
        Process process;
        VideoFrame latest;
        volatile string error;
        public readonly CameraSource Source;

        public CameraSession(CameraSource source, string address, string executable, Func<int[]> demoCount)
        {
            Source = source;
            if (source == CameraSource.File)
            {
                localVideo = new LocalVideo(address);
                cancellation.Dispose();
                return;
            }
            if (source == CameraSource.Demo)
            {
                Task.Factory.StartNew(() => GenerateDemo(demoCount), TaskCreationOptions.LongRunning);
                return;
            }

            string options = "";
            if (source == CameraSource.Network)
            {
                Uri uri;
                if (!Uri.TryCreate(address, UriKind.Absolute, out uri) ||
                    (uri.Scheme != "rtsp" && uri.Scheme != "rtsps" && uri.Scheme != "http" && uri.Scheme != "https") ||
                    string.IsNullOrEmpty(uri.Host))
                    throw new ArgumentException("请输入 RTSP、RTSPS、HTTP 或 HTTPS 视频地址。");
                options = uri.Scheme.StartsWith("rtsp", StringComparison.Ordinal)
                    ? "-rtsp_transport tcp " : "";
            }
            else if (source == CameraSource.Usb)
            {
                if (string.IsNullOrWhiteSpace(address)) throw new ArgumentException("请输入 USB 摄像头的 DirectShow 名称。");
                options = "-f dshow ";
                address = "video=" + address;
            }
            string arguments = "-hide_banner -loglevel error -nostdin " + options + "-i " + Quote(address) +
                " -map 0:v:0 -an -sn -vf fps=10,scale=640:360:force_original_aspect_ratio=decrease," +
                "pad=640:360:(ow-iw)/2:(oh-ih)/2,setsar=1 -threads 1 -pix_fmt bgr24 -f rawvideo pipe:1";
            var child = new Process
            {
                StartInfo = new ProcessStartInfo(executable, arguments)
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                }
            };
            try { child.Start(); }
            catch { child.Dispose(); throw; }
            process = child;
            Task.Factory.StartNew(() => ReadFrames(child), TaskCreationOptions.LongRunning);
        }

        public VideoFrame Latest { get { return localVideo == null ? Volatile.Read(ref latest) : localVideo.Latest; } }
        public string Error { get { return localVideo == null ? error : localVideo.Error; } }
        public bool Ended { get { return localVideo != null && localVideo.Ended; } }

        void ReadFrames(Process child)
        {
            try
            {
                Stream stream = child.StandardOutput.BaseStream;
                while (!cancellation.IsCancellationRequested)
                {
                    var pixels = new byte[VideoFrame.ByteCount];
                    int offset = 0;
                    while (offset < pixels.Length)
                    {
                        int count = stream.Read(pixels, offset, pixels.Length - offset);
                        if (count == 0)
                        {
                            error = "视频流已结束。请检查地址、网络、设备名或 FFmpeg，再重新连接。";
                            return;
                        }
                        offset += count;
                    }
                    Interlocked.Exchange(ref latest, new VideoFrame(pixels));
                }
            }
            catch (Exception)
            {
                if (!cancellation.IsCancellationRequested) error = "无法读取视频流，请重新连接。";
            }
            finally
            {
                lock (processLock)
                {
                    StopProcess(child);
                    child.Dispose();
                    process = null;
                }
                cancellation.Dispose();
            }
        }

        void GenerateDemo(Func<int[]> getCount)
        {
            try
            {
                while (!cancellation.IsCancellationRequested)
                {
                    var pixels = new byte[VideoFrame.ByteCount];
                    using (var bitmap = new Bitmap(VideoFrame.Width, VideoFrame.Height, PixelFormat.Format24bppRgb))
                    {
                        using (Graphics graphics = Graphics.FromImage(bitmap))
                        {
                            graphics.Clear(Color.FromArgb(25, 43, 49));
                            graphics.FillRectangle(Brushes.Gainsboro, 107, 42, 426, 276);
                            graphics.FillRectangle(Brushes.DarkSlateGray, 123, 58, 394, 244);
                            int[] colors = getCount();
                            if (colors.Length > 0)
                            {
                                using (var shell = new SolidBrush(Color.FromArgb(90, 75, 59)))
                                using (var red = new Pen(Color.Firebrick, 5))
                                using (var green = new Pen(Color.LimeGreen, 5))
                                using (var blue = new Pen(Color.RoyalBlue, 5))
                                using (var white = new Pen(Color.Ivory, 5))
                                {
                                    var ropes = new[] { red, green, blue, white };
                                    for (int i = 0; i < Math.Min(colors.Length, 12); i++)
                                    {
                                        int x = 135 + i % 4 * 95, y = 70 + i / 4 * 76;
                                        graphics.FillEllipse(shell, x, y, 83, 62);
                                        graphics.DrawEllipse(ropes[colors[i]], x + 11, y + 8, 61, 44);
                                    }
                                }
                            }
                        }
                        var data = bitmap.LockBits(new Rectangle(0, 0, VideoFrame.Width, VideoFrame.Height),
                            ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
                        try { Marshal.Copy(data.Scan0, pixels, 0, pixels.Length); }
                        finally { bitmap.UnlockBits(data); }
                    }
                    Interlocked.Exchange(ref latest, new VideoFrame(pixels));
                    if (cancellation.Token.WaitHandle.WaitOne(100)) break;
                }
            }
            catch (Exception) { error = "无法生成演示视频。"; }
            finally { cancellation.Dispose(); }
        }

        // ProcessStartInfo.Arguments receives a Windows command line, not a shell command.
        internal static string Quote(string argument)
        {
            var result = new StringBuilder("\"");
            int slashes = 0;
            foreach (char c in argument)
            {
                if (c == '\\') { slashes++; continue; }
                result.Append('\\', c == '"' ? slashes * 2 + 1 : slashes);
                result.Append(c);
                slashes = 0;
            }
            return result.Append('\\', slashes * 2).Append('"').ToString();
        }

        static void StopProcess(Process child)
        {
            try { if (!child.HasExited) child.Kill(); }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
        }

        public void Dispose()
        {
            if (localVideo != null)
            {
                localVideo.Dispose();
                return;
            }
            try { cancellation.Cancel(); }
            catch (ObjectDisposedException) { }
            lock (processLock)
            {
                if (process != null) StopProcess(process);
            }
        }
    }
}
