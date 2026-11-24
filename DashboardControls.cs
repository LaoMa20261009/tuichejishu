using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace CrabWeighing
{
    sealed class VideoSurface : Control
    {
        Image frame;

        public VideoSurface()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        public Rectangle FrameBounds
        {
            get
            {
                double scale = Math.Min(ClientSize.Width / (double)VideoFrame.Width,
                    ClientSize.Height / (double)VideoFrame.Height);
                int width = (int)(VideoFrame.Width * scale), height = (int)(VideoFrame.Height * scale);
                return new Rectangle((ClientSize.Width - width) / 2, (ClientSize.Height - height) / 2, width, height);
            }
        }

        public void SetFrame(Image next)
        {
            if (frame != null) frame.Dispose();
            frame = next;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Rectangle bounds = FrameBounds;
            if (frame != null && bounds.Width > 0 && bounds.Height > 0)
                e.Graphics.DrawImage(frame, bounds);
            else
                TextRenderer.DrawText(e.Graphics, "选择视频源并连接", Font, ClientRectangle,
                    Color.FromArgb(173, 194, 221), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            base.OnPaint(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && frame != null) frame.Dispose();
            base.Dispose(disposing);
        }
    }

    sealed class ColorGallery : ScrollableControl
    {
        const int Gap = 10;
        const int CardHeight = 72;
        readonly List<Entry> entries = new List<Entry>();
        readonly Font colorFont = new Font("Microsoft YaHei UI", 22, FontStyle.Bold, GraphicsUnit.Pixel);
        readonly Font timeFont = new Font("Microsoft YaHei UI", 15, FontStyle.Regular, GraphicsUnit.Pixel);
        readonly Color[] inks;
        readonly Color[] fills;

        public ColorGallery(Color[] inks, Color[] fills)
        {
            this.inks = inks;
            this.fills = fills;
            AutoScroll = true;
            BackColor = Color.White;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        int Columns
        {
            get { return Math.Max(1, (Width - SystemInformation.VerticalScrollBarWidth + Gap) / (156 + Gap)); }
        }

        public void LoadHistory(IEnumerable<MeasurementRecord> records)
        {
            foreach (MeasurementRecord record in records)
            {
                DateTimeOffset time;
                if (DateTimeOffset.TryParseExact(record.Time, "o", CultureInfo.InvariantCulture, DateTimeStyles.None, out time))
                    entries.Add(new Entry(record.Color, time));
            }
            RefreshExtent();
        }

        public void AddColors(IEnumerable<string> colors, DateTimeOffset time)
        {
            foreach (string color in colors) entries.Add(new Entry(color, time));
            RefreshExtent();
        }

        void RefreshExtent()
        {
            int rows = (entries.Count + Columns - 1) / Columns;
            var extent = new Size(0, rows * (CardHeight + Gap));
            if (AutoScrollMinSize != extent) AutoScrollMinSize = extent;
            Invalidate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (entries != null) RefreshExtent();
        }

        protected override void OnScroll(ScrollEventArgs e)
        {
            base.OnScroll(e);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (entries.Count == 0)
            {
                TextRenderer.DrawText(e.Graphics, "等待识别到绳色", Font, ClientRectangle, ForeColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }

            int columns = Columns;
            int width = Math.Max(1, (ClientSize.Width - (columns - 1) * Gap) / columns);
            int offset = AutoScrollPosition.Y;
            int firstRow = Math.Max(0, (e.ClipRectangle.Top - offset) / (CardHeight + Gap));
            int lastRow = Math.Max(firstRow, (e.ClipRectangle.Bottom - offset) / (CardHeight + Gap));
            for (int row = firstRow; row <= lastRow; row++)
            for (int column = 0; column < columns; column++)
            {
                int index = entries.Count - 1 - (row * columns + column);
                if (index < 0) break;
                Entry entry = entries[index];
                int color = Array.IndexOf(Basket.Colors, entry.Color);
                Color ink = color < 0 ? Color.FromArgb(63, 77, 99) : inks[color];
                Color fill = color < 0 ? Color.FromArgb(236, 241, 247) : fills[color];
                var card = new Rectangle(column * (width + Gap), row * (CardHeight + Gap) + offset, width, CardHeight);
                using (var brush = new SolidBrush(fill)) e.Graphics.FillRectangle(brush, card);
                using (var brush = new SolidBrush(ink)) e.Graphics.FillRectangle(brush, card.X, card.Y, 4, card.Height);
                TextRenderer.DrawText(e.Graphics, entry.Color, colorFont,
                    new Rectangle(card.X + 14, card.Y + 7, Math.Max(1, card.Width - 22), 33),
                    ink, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                TextRenderer.DrawText(e.Graphics, entry.Time.ToString("MM-dd  HH:mm:ss", CultureInfo.InvariantCulture), timeFont,
                    new Rectangle(card.X + 14, card.Y + 43, Math.Max(1, card.Width - 22), 20),
                    ForeColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                colorFont.Dispose();
                timeFont.Dispose();
            }
            base.Dispose(disposing);
        }

        sealed class Entry
        {
            public readonly string Color;
            public readonly DateTimeOffset Time;

            public Entry(string color, DateTimeOffset time)
            {
                Color = color;
                Time = time;
            }
        }
    }
}
