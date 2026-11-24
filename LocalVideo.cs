using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace CrabWeighing
{
    // WPF's media engine uses the Windows codecs and requires its own STA dispatcher.
    sealed class LocalVideo : IDisposable
    {
        readonly string path;
        readonly object lifecycle = new object();
        Dispatcher dispatcher;
        bool disposed;
        VideoFrame latest;
        volatile string error;
        volatile bool ended;

        public LocalVideo(string path)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("找不到选中的视频文件。");
            this.path = Path.GetFullPath(path);
            var thread = new Thread(Play) { IsBackground = true, Name = "Local video" };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        }

        public VideoFrame Latest { get { return Volatile.Read(ref latest); } }
        public string Error { get { return error; } }
        public bool Ended { get { return ended; } }

        void Play()
        {
            Dispatcher current = Dispatcher.CurrentDispatcher;
            lock (lifecycle)
            {
                if (disposed) return;
                dispatcher = current;
            }
            MediaPlayer player = null;
            DispatcherTimer timer = null;
            try
            {
                player = new MediaPlayer { IsMuted = true, ScrubbingEnabled = true };
                var visual = new DrawingVisual();
                var buffer = new byte[VideoFrame.Width * VideoFrame.Height * 4];
                TimeSpan previousPosition = TimeSpan.MinValue;
                timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(100) };
                timer.Tick += (sender, args) =>
                {
                    try
                    {
                        TimeSpan position = player.Position;
                        if (position == previousPosition) return;
                        previousPosition = position;
                        var image = new RenderTargetBitmap(VideoFrame.Width, VideoFrame.Height, 96, 96, PixelFormats.Pbgra32);
                        image.Render(visual);
                        image.CopyPixels(buffer, VideoFrame.Width * 4, 0);
                        var pixels = new byte[VideoFrame.ByteCount];
                        for (int i = 0, j = 0; i < pixels.Length; i += 3, j += 4)
                        {
                            pixels[i] = buffer[j];
                            pixels[i + 1] = buffer[j + 1];
                            pixels[i + 2] = buffer[j + 2];
                        }
                        Interlocked.Exchange(ref latest, new VideoFrame(pixels, position));
                    }
                    catch (Exception)
                    {
                        error = "读取视频画面失败。请试用 H.264 编码的 MP4。";
                        current.BeginInvokeShutdown(DispatcherPriority.Normal);
                    }
                };
                player.MediaOpened += (sender, args) =>
                {
                    if (player.NaturalVideoWidth <= 0 || player.NaturalVideoHeight <= 0)
                    {
                        error = "文件中没有 Windows 可解码的视频轨。请试用 H.264 编码的 MP4。";
                        current.BeginInvokeShutdown(DispatcherPriority.Normal);
                        return;
                    }
                    double ratio = Math.Min(VideoFrame.Width / (double)player.NaturalVideoWidth,
                        VideoFrame.Height / (double)player.NaturalVideoHeight);
                    double width = player.NaturalVideoWidth * ratio;
                    double height = player.NaturalVideoHeight * ratio;
                    using (DrawingContext drawing = visual.RenderOpen())
                    {
                        drawing.DrawRectangle(Brushes.Black, null, new Rect(0, 0, VideoFrame.Width, VideoFrame.Height));
                        drawing.DrawVideo(player, new Rect((VideoFrame.Width - width) / 2,
                            (VideoFrame.Height - height) / 2, width, height));
                    }
                    timer.Start();
                };
                player.MediaEnded += (sender, args) =>
                {
                    timer.Stop();
                    ended = true;
                    current.BeginInvokeShutdown(DispatcherPriority.Normal);
                };
                player.MediaFailed += (sender, args) =>
                {
                    error = "Windows 无法播放此文件。请试用 H.264 编码的 MP4，并确认可在 Windows 播放器中打开。";
                    current.BeginInvokeShutdown(DispatcherPriority.Normal);
                };
                player.Open(new Uri(path, UriKind.Absolute));
                player.Play();
                Dispatcher.Run();
            }
            catch (Exception)
            {
                error = "无法打开视频。请确认文件可用并尝试 H.264 编码的 MP4。";
            }
            finally
            {
                if (timer != null) timer.Stop();
                if (player != null) player.Close();
                lock (lifecycle) { dispatcher = null; }
            }
        }

        public void Dispose()
        {
            lock (lifecycle)
            {
                disposed = true;
                if (dispatcher != null && !dispatcher.HasShutdownStarted)
                    dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
            }
        }
    }
}
