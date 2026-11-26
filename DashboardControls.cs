using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace CrabWeighing
{
    static class ConsolePaint
    {
        public static int Pixels(Control control, int pixels)
        {
            var window = control.FindForm() as MainForm;
            return window == null ? pixels : Math.Max(1, (int)Math.Round(
                pixels * window.ClientSize.Width / (double)ConsoleLayout.DesignSize.Width));
        }

        public static Font PaintFont(Control control, Font source)
        {
            return new Font(source.FontFamily, Pixels(control, (int)source.Size), source.Style, source.Unit);
        }

        public static void Tile(Graphics graphics, Rectangle box, int radius, Color fill, Color border)
        {
            if (box.Width <= 1 || box.Height <= 1) return;
            SmoothingMode smoothing = graphics.SmoothingMode;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            int diameter = Math.Min(radius * 2, Math.Min(box.Width - 1, box.Height - 1));
            using (var path = new GraphicsPath())
            {
                int right = box.Right - 1, bottom = box.Bottom - 1;
                path.AddArc(box.X, box.Y, diameter, diameter, 180, 90);
                path.AddArc(right - diameter, box.Y, diameter, diameter, 270, 90);
                path.AddArc(right - diameter, bottom - diameter, diameter, diameter, 0, 90);
                path.AddArc(box.X, bottom - diameter, diameter, diameter, 90, 90);
                path.CloseFigure();
                using (var brush = new SolidBrush(fill)) graphics.FillPath(brush, path);
                using (var pen = new Pen(border)) graphics.DrawPath(pen, path);
            }
            graphics.SmoothingMode = smoothing;
        }

        public static void Dot(Graphics graphics, int x, int y, int diameter, Color color)
        {
            var smoothing = graphics.SmoothingMode;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var brush = new SolidBrush(color)) graphics.FillEllipse(brush, x, y, diameter, diameter);
            graphics.SmoothingMode = smoothing;
        }
    }

    sealed class ConsoleCard : TableLayoutPanel
    {
        public Color Outline { get; set; }

        public ConsoleCard()
        {
            DoubleBuffered = true;
            Outline = Color.FromArgb(47, 61, 80);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent == null ? BackColor : Parent.BackColor);
            ConsolePaint.Tile(e.Graphics, ClientRectangle, 5, BackColor, Outline);
        }
    }

    sealed class BoxMark : Control
    {
        protected override void OnPaint(PaintEventArgs e)
        {
            ConsolePaint.Tile(e.Graphics, ClientRectangle, 4, Color.FromArgb(224, 238, 247), Color.FromArgb(184, 202, 219));
            using (var pen = new Pen(Color.FromArgb(17, 48, 67), 2))
            {
                int left = 10, top = 10, right = Width - 11, bottom = Height - 11;
                e.Graphics.DrawLines(pen, new[] { new Point(left, top + 7), new Point(left, top), new Point(left + 7, top) });
                e.Graphics.DrawLines(pen, new[] { new Point(right - 7, top), new Point(right, top), new Point(right, top + 7) });
                e.Graphics.DrawLines(pen, new[] { new Point(left, bottom - 7), new Point(left, bottom), new Point(left + 7, bottom) });
                e.Graphics.DrawLines(pen, new[] { new Point(right - 7, bottom), new Point(right, bottom), new Point(right, bottom - 7) });
                e.Graphics.DrawRectangle(pen, left + 7, top + 8, right - left - 14, bottom - top - 16);
                e.Graphics.DrawLine(pen, left + 7, top + 13, right - 7, top + 13);
            }
            base.OnPaint(e);
        }
    }

    sealed class OverallProgress : Control
    {
        static readonly Color Accent = Color.FromArgb(34, 211, 238);
        readonly Font figure = new Font("Consolas", 32, FontStyle.Bold, GraphicsUnit.Pixel);
        readonly Font caption = new Font("Microsoft YaHei UI", 13, FontStyle.Bold, GraphicsUnit.Pixel);
        int complete, goal;

        public void SetProgress(int complete, int goal)
        {
            this.complete = complete;
            this.goal = goal;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            ConsolePaint.Tile(e.Graphics, ClientRectangle, 5, Color.FromArgb(10, 32, 46), Color.FromArgb(12, 101, 122));
            Func<int, int> px = value => ConsolePaint.Pixels(this, value);
            int top = Math.Max(2, (Height - px(85)) / 2);
            var center = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding;
            using (Font label = ConsolePaint.PaintFont(this, caption))
            using (Font number = ConsolePaint.PaintFont(this, figure))
            {
                TextRenderer.DrawText(e.Graphics, "总装箱进度", label, new Rectangle(0, top, Width, px(19)), Accent, center);
                string percent = goal == 0 ? "—" : ((double)complete / goal * 100).ToString("0", CultureInfo.InvariantCulture) + "%";
                TextRenderer.DrawText(e.Graphics, percent, number, new Rectangle(0, top + px(20), Width, px(40)), Accent, center);
                var pill = new Rectangle(Math.Max(2, (Width - px(86)) / 2), top + px(63), Math.Min(px(86), Width - 4), px(22));
                ConsolePaint.Tile(e.Graphics, pill, px(10), Color.FromArgb(14, 26, 43), Color.FromArgb(25, 71, 96));
                TextRenderer.DrawText(e.Graphics, complete + " / " + goal + " 只", label, pill, ForeColor, center);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { figure.Dispose(); caption.Dispose(); }
            base.Dispose(disposing);
        }
    }

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
        readonly Font strong = new Font("Microsoft YaHei UI", 14, FontStyle.Bold, GraphicsUnit.Pixel);
        readonly Font detailFont = new Font("Microsoft YaHei UI", 13, FontStyle.Regular, GraphicsUnit.Pixel);
        string name = "", detail = "", percentage = "—";
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
            this.name = name;
            detail = "目标 " + goal + " 只   |   已装 " + packed + " 只";
            percentage = goal == 0 ? "—" : (fraction * 100).ToString("0", CultureInfo.InvariantCulture) + "%";
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            ConsolePaint.Tile(e.Graphics, ClientRectangle, 4, BackColor, ControlPaint.Dark(tint, 0.48F));
            Func<int, int> px = value => ConsolePaint.Pixels(this, value);
            ConsolePaint.Dot(e.Graphics, px(9), px(6), px(7), tint);
            int nameWidth = Math.Min(px(151), Width / 3);
            const TextFormatFlags text = TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding;
            using (Font label = ConsolePaint.PaintFont(this, strong))
            using (Font description = ConsolePaint.PaintFont(this, detailFont))
            {
                TextRenderer.DrawText(e.Graphics, name, label, new Rectangle(px(22), 1, nameWidth, px(17)), tint, text);
                TextRenderer.DrawText(e.Graphics, detail, description,
                    new Rectangle(px(27) + nameWidth, 1, Math.Max(1, Width - nameWidth - px(76)), px(17)),
                    Color.FromArgb(164, 179, 197), text);
                TextRenderer.DrawText(e.Graphics, percentage, label,
                    new Rectangle(Width - px(46), 1, px(38), px(17)), tint,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
            var track = new Rectangle(px(9), Height - px(6), Math.Max(1, Width - px(18)), px(3));
            using (var brush = new SolidBrush(Color.FromArgb(7, 14, 26))) e.Graphics.FillRectangle(brush, track);
            using (var brush = new SolidBrush(tint))
                e.Graphics.FillRectangle(brush, track.X, track.Y, (int)(track.Width * fraction), track.Height);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { strong.Dispose(); detailFont.Dispose(); }
            base.Dispose(disposing);
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
            e.Graphics.Clear(Parent == null ? fill : Parent.BackColor);
            ConsolePaint.Tile(e.Graphics, ClientRectangle, 4, fill, ControlPaint.Light(fill, 0.22F));
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, Enabled ? ForeColor : Color.FromArgb(128, 144, 165),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            if (Focused && ShowFocusCues)
                ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -3, -3));
        }
    }

    sealed class WeightReadout : Label
    {
        readonly Font caption = new Font("Microsoft YaHei UI", 12, FontStyle.Bold, GraphicsUnit.Pixel);

        protected override void OnPaint(PaintEventArgs e)
        {
            ConsolePaint.Tile(e.Graphics, ClientRectangle, 5, Color.FromArgb(8, 24, 37), Color.FromArgb(15, 65, 86));
            Func<int, int> px = value => ConsolePaint.Pixels(this, value);
            ConsolePaint.Dot(e.Graphics, px(9), px(11), px(5), ForeColor);
            using (Font label = ConsolePaint.PaintFont(this, caption))
                TextRenderer.DrawText(e.Graphics, "实时净重", label, new Point(px(20), px(6)), ForeColor);
            var room = new Rectangle(px(8), px(17), Math.Max(1, Width - px(16)), Math.Max(1, Height - px(18)));
            TextRenderer.DrawText(e.Graphics, Text, Font, room, ForeColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) caption.Dispose();
            base.Dispose(disposing);
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
            using (Font heading = ConsolePaint.PaintFont(this, headerFont))
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
                    Color ink = row == 0 ? ForeColor :
                        col == 2 || col == 5 ? colorInks[1] :
                        col == 3 ? Color.FromArgb(252, 196, 71) :
                        row == rows.Length ? Color.FromArgb(34, 211, 238) : ForeColor;
                    if (col == 0 && row > 0)
                    {
                        ConsolePaint.Dot(e.Graphics, left + 8, band.Top + (band.Height - 7) / 2, 7,
                            row == rows.Length ? Color.FromArgb(34, 211, 238) : colorInks[row - 1]);
                        cell.X += 17;
                        cell.Width -= 17;
                    }
                    TextRenderer.DrawText(e.Graphics, cells[col], row == 0 ? heading : Font, cell, ink,
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
