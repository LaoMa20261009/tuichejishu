using System;
using System.Drawing;
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

    sealed class TaskMeter : Control
    {
        readonly Color tint;
        string caption = "";
        double fraction;

        public TaskMeter(Color tint, Color background)
        {
            this.tint = tint;
            BackColor = background;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        }

        public void SetProgress(string name, int packed, int goal)
        {
            fraction = goal == 0 ? 0 : Math.Min(1, (double)packed / goal);
            caption = name + "    目标 " + goal + " 只  /  已装 " + packed + " 只";
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var bounds = new Rectangle(8, 0, Math.Max(0, Width - 16), Math.Max(0, Height - 4));
            TextRenderer.DrawText(e.Graphics, caption, Font, bounds, tint,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            using (var brush = new SolidBrush(tint))
                e.Graphics.FillRectangle(brush, bounds.X, bounds.Bottom, (int)(bounds.Width * fraction), 2);
            base.OnPaint(e);
        }
    }
    sealed class ConsoleButton : Button
    {
        bool hover;

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            hover = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            hover = false;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Color fill = Enabled && hover ? ControlPaint.Light(BackColor, 0.12F) : BackColor;
            using (var brush = new SolidBrush(fill)) e.Graphics.FillRectangle(brush, ClientRectangle);
            using (var pen = new Pen(ControlPaint.Light(fill, 0.28F)))
                e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, Enabled ? ForeColor : Color.FromArgb(128, 144, 165),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            if (Focused && ShowFocusCues)
                ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -3, -3));
        }
    }

    sealed class WeightReadout : Label
    {
        protected override void OnPaint(PaintEventArgs e)
        {
            var room = new Rectangle(6, 0, Math.Max(1, Width - 12), Height);
            TextRenderer.DrawText(e.Graphics, Text, Font, room, ForeColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    sealed class PackingSummary : Control
    {
        readonly string[] headings = { "规格 / 绳色", "待装", "已装", "剩余", "总重量 g", "完成率" };
        readonly int[] widths = { 31, 11, 11, 11, 18, 18 };
        readonly Color[] colorInks;
        readonly Font headerFont = new Font("Microsoft YaHei UI", 15, FontStyle.Bold, GraphicsUnit.Pixel);
        string[][] rows = new string[0][];

        public PackingSummary(Color[] colorInks)
        {
            this.colorInks = colorInks;
            BackColor = Color.FromArgb(11, 17, 28);
            ForeColor = Color.FromArgb(235, 241, 250);
            SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        public void SetRows(string[][] values)
        {
            rows = values;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (Height < 6) return;
            int rowHeight = Height / 6;
            for (int row = 0; row <= rows.Length; row++)
            {
                var band = new Rectangle(0, row * rowHeight, Width, rowHeight);
                Color background = row == 0 ? Color.FromArgb(22, 33, 52) :
                    row == rows.Length ? Color.FromArgb(17, 24, 41) :
                    row % 2 == 0 ? Color.FromArgb(14, 22, 36) : BackColor;
                using (var brush = new SolidBrush(background)) e.Graphics.FillRectangle(brush, band);
                using (var pen = new Pen(Color.FromArgb(47, 61, 80)))
                    e.Graphics.DrawLine(pen, 0, band.Bottom - 1, Width, band.Bottom - 1);
                string[] cells = row == 0 ? headings : rows[row - 1];
                int offset = 0;
                for (int col = 0; col < widths.Length; col++)
                {
                    int left = Width * offset / 100;
                    offset += widths[col];
                    int right = Width * offset / 100;
                    var cell = Rectangle.FromLTRB(left + 5, band.Top, right - 3, band.Bottom);
                    Color ink = row == 0 ? ForeColor : row == rows.Length ? Color.FromArgb(34, 211, 238) :
                        col == 0 ? colorInks[row - 1] : col == 2 ? colorInks[1] : ForeColor;
                    TextRenderer.DrawText(e.Graphics, cells[col], row == 0 ? headerFont : Font, cell, ink,
                        TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
                        (col == 0 ? TextFormatFlags.Left : TextFormatFlags.HorizontalCenter));
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) headerFont.Dispose();
            base.Dispose(disposing);
        }
    }
}
