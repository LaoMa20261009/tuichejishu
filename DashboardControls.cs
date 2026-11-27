using System;
using System.Drawing;
using System.Drawing.Drawing2D;
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

        public static void Tile(Graphics graphics, Rectangle box, int radius, Color fill, Color border, int strokeWidth = 1)
        {
            if (box.Width <= 1 || box.Height <= 1) return;
            SmoothingMode smoothing = graphics.SmoothingMode;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = RoundedPath(box, radius))
            {
                using (var brush = new SolidBrush(fill)) graphics.FillPath(brush, path);
                using (var pen = new Pen(border, strokeWidth)) graphics.DrawPath(pen, path);
            }
            graphics.SmoothingMode = smoothing;
        }

        static GraphicsPath RoundedPath(Rectangle box, int radius)
        {
            var path = new GraphicsPath();
            int diameter = Math.Min(radius * 2, Math.Min(box.Width - 1, box.Height - 1));
            int right = box.Right - 1, bottom = box.Bottom - 1;
            path.AddArc(box.X, box.Y, diameter, diameter, 180, 90);
            path.AddArc(right - diameter, box.Y, diameter, diameter, 270, 90);
            path.AddArc(right - diameter, bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(box.X, bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static void RoundEdges(Control control, int radius)
        {
            EventHandler apply = (sender, e) =>
            {
                if (control.Width < 2 || control.Height < 2) return;
                int corner = Math.Min(Pixels(control, radius), Math.Min(control.Width, control.Height) / 2);
                var region = new Region(new Rectangle(0, corner, control.Width, control.Height - 2 * corner));
                // Keep the native clip as rectangles so large windows do not require a full-size path mask.
                for (int y = 0; y < corner; y++)
                {
                    double dy = corner - y - 0.5;
                    int inset = (int)Math.Round(corner - Math.Sqrt(corner * corner - dy * dy));
                    int width = control.Width - 2 * inset;
                    region.Union(new Rectangle(inset, y, width, 1));
                    region.Union(new Rectangle(inset, control.Height - y - 1, width, 1));
                }
                var prior = control.Region;
                control.Region = region;
                if (prior != null) prior.Dispose();
            };
            control.SizeChanged += apply;
            control.ParentChanged += apply;
            apply(control, EventArgs.Empty);
        }

        public static void DrawFittedText(Graphics graphics, string text, Font font, Rectangle bounds,
            Color ink, TextFormatFlags alignment)
        {
            if (bounds.Width <= 0 || bounds.Height <= 0) return;
            const TextFormatFlags options = TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
            Size natural = TextRenderer.MeasureText(graphics, text, font, Size.Empty, options);
            if (natural.Width <= bounds.Width)
            {
                TextRenderer.DrawText(graphics, text, font, bounds, ink, options | alignment);
                return;
            }

            // TextRenderer rounds font sizes to device pixels. Recheck after scaling.
            int size = Math.Max(1, (int)(font.SizeInPoints * bounds.Width / natural.Width));
            for (; size > 0; size--)
            {
                using (var fitted = new Font(font.FontFamily, size, font.Style, GraphicsUnit.Point))
                {
                    if (TextRenderer.MeasureText(graphics, text, fitted, Size.Empty, options).Width > bounds.Width) continue;
                    TextRenderer.DrawText(graphics, text, fitted, bounds, ink, options | alignment);
                    return;
                }
            }
        }

        public static void Dot(Graphics graphics, int x, int y, int diameter, Color color)
        {
            var smoothing = graphics.SmoothingMode;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var brush = new SolidBrush(color)) graphics.FillEllipse(brush, x, y, diameter, diameter);
            graphics.SmoothingMode = smoothing;
        }
    }

    enum ConsoleIcon { None, Scale, Camera, Photo, Scan, Box, Trash }

    static class ConsoleIcons
    {
        public static void Draw(Graphics graphics, ConsoleIcon icon, Rectangle box, Color ink)
        {
            var state = graphics.Save();
            graphics.TranslateTransform(box.X, box.Y);
            graphics.ScaleTransform(box.Width / 24f, box.Height / 24f);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var pen = new Pen(ink, 1.7f) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                switch (icon)
                {
                    case ConsoleIcon.Scale:
                        graphics.DrawRectangle(pen, 3, 4, 18, 16);
                        graphics.DrawArc(pen, 7, 7, 10, 9, 180, 180);
                        graphics.DrawLine(pen, 12, 13, 15, 9);
                        break;
                    case ConsoleIcon.Camera:
                        graphics.DrawRectangle(pen, 3, 6, 12, 12);
                        graphics.DrawPolygon(pen, new[] { new Point(15, 10), new Point(21, 7), new Point(21, 17), new Point(15, 14) });
                        break;
                    case ConsoleIcon.Photo:
                        graphics.DrawRectangle(pen, 3, 4, 18, 16);
                        graphics.DrawEllipse(pen, 13, 7, 4, 4);
                        graphics.DrawLines(pen, new[] { new Point(4, 17), new Point(9, 11), new Point(15, 17), new Point(18, 14), new Point(21, 17) });
                        break;
                    case ConsoleIcon.Scan:
                        graphics.DrawLines(pen, new[] { new Point(3, 8), new Point(3, 3), new Point(8, 3) });
                        graphics.DrawLines(pen, new[] { new Point(16, 3), new Point(21, 3), new Point(21, 8) });
                        graphics.DrawLines(pen, new[] { new Point(3, 16), new Point(3, 21), new Point(8, 21) });
                        graphics.DrawLines(pen, new[] { new Point(16, 21), new Point(21, 21), new Point(21, 16) });
                        graphics.DrawLine(pen, 7, 10, 17, 10);
                        graphics.DrawLine(pen, 7, 14, 15, 14);
                        break;
                    case ConsoleIcon.Box:
                        graphics.DrawPolygon(pen, new[] { new Point(3, 7), new Point(12, 3), new Point(21, 7), new Point(21, 17), new Point(12, 21), new Point(3, 17) });
                        graphics.DrawLines(pen, new[] { new Point(3, 7), new Point(12, 11), new Point(21, 7) });
                        graphics.DrawLine(pen, 12, 11, 12, 21);
                        break;
                    case ConsoleIcon.Trash:
                        graphics.DrawLine(pen, 4, 6, 20, 6);
                        graphics.DrawLines(pen, new[] { new Point(6, 6), new Point(7, 21), new Point(17, 21), new Point(18, 6) });
                        graphics.DrawLines(pen, new[] { new Point(9, 6), new Point(9, 3), new Point(15, 3), new Point(15, 6) });
                        graphics.DrawLine(pen, 10, 10, 10, 17);
                        graphics.DrawLine(pen, 14, 10, 14, 17);
                        break;
                }
            }
            graphics.Restore(state);
        }
    }

    sealed class ConsoleLabel : Label
    {
        public ConsoleIcon Icon { get; set; }
        public bool FitText { get; set; }
        public bool RoundedFill { get; set; }
        Color statusDot;
        public Color StatusDot
        {
            get { return statusDot; }
            set
            {
                if (statusDot == value) return;
                statusDot = value;
                Invalidate();
            }
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Rectangle box = Rectangle.FromLTRB(Padding.Left, Padding.Top,
                ClientRectangle.Right - Padding.Right, ClientRectangle.Bottom - Padding.Bottom);
            if (Icon != ConsoleIcon.None)
            {
                int unit = ConsolePaint.Pixels(this, 18), inset = ConsolePaint.Pixels(this, 9);
                ConsolePaint.Tile(e.Graphics, ClientRectangle, inset, BackColor, Color.FromArgb(205, 222, 243));
                ConsoleIcons.Draw(e.Graphics, Icon, new Rectangle(inset, (Height - unit) / 2, unit, unit), ForeColor);
                ConsolePaint.Dot(e.Graphics, Width - 2 * inset, (Height - inset) / 2, inset, StatusDot);
                int start = unit + 2 * inset;
                int width = Width - start - 3 * inset;
                int split = Text.IndexOf('\n');
                string title = split < 0 ? Text : Text.Substring(0, split);
                string status = split < 0 ? "" : Text.Substring(split + 1);
                const TextFormatFlags badgeText = TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                    TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding;
                using (var font = new Font(Font.FontFamily, ConsolePaint.Pixels(this, 14), FontStyle.Bold, GraphicsUnit.Pixel))
                    TextRenderer.DrawText(e.Graphics, title, font, new Rectangle(start, 0, width, Height / 2 + inset / 2), ForeColor, badgeText);
                using (var font = new Font(Font.FontFamily, ConsolePaint.Pixels(this, 13), FontStyle.Regular, GraphicsUnit.Pixel))
                    TextRenderer.DrawText(e.Graphics, status, font, new Rectangle(start, Height / 2, width, Height / 2 - inset / 3), ForeColor, badgeText);
                return;
            }
            if (RoundedFill && Parent != null && BackColor != Color.Transparent)
            {
                e.Graphics.Clear(Parent.BackColor);
                ConsolePaint.Tile(e.Graphics, ClientRectangle, ConsolePaint.Pixels(this, 7), BackColor, BackColor);
            }
            var flags = TextFormatFlags.VerticalCenter;
            if (TextAlign == ContentAlignment.MiddleCenter) flags |= TextFormatFlags.HorizontalCenter;
            else if (TextAlign == ContentAlignment.MiddleRight) flags |= TextFormatFlags.Right;
            else flags |= TextFormatFlags.Left;
            if (FitText) ConsolePaint.DrawFittedText(e.Graphics, Text, Font, box, ForeColor, flags);
            else TextRenderer.DrawText(e.Graphics, Text, Font, box, ForeColor,
                flags | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
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

    sealed class ConsoleButton : Button
    {
        bool hover;
        public ConsoleIcon Icon { get; set; }

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
            ConsolePaint.Tile(e.Graphics, ClientRectangle, ConsolePaint.Pixels(this, 9), fill, ControlPaint.Light(fill, 0.22F));
            Color ink = Enabled ? ForeColor : Color.FromArgb(128, 144, 165);
            Rectangle text = ClientRectangle;
            if (Icon != ConsoleIcon.None)
            {
                int unit = ConsolePaint.Pixels(this, 20), gap = ConsolePaint.Pixels(this, 7);
                int textWidth = string.IsNullOrEmpty(Text) ? 0 : TextRenderer.MeasureText(Text, Font,
                    Size.Empty, TextFormatFlags.NoPadding).Width + gap;
                int left = Math.Max(gap, (Width - unit - textWidth) / 2);
                ConsoleIcons.Draw(e.Graphics, Icon, new Rectangle(left, (Height - unit) / 2, unit, unit), ink);
                int start = left + unit + gap;
                text = new Rectangle(start, 0, Math.Max(1, Math.Min(textWidth - gap, Width - gap - start)), Height);
            }
            TextRenderer.DrawText(e.Graphics, Text, Font, text, ink,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            if (Focused && ShowFocusCues)
                ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -3, -3));
        }
    }

    sealed class ProgressTrack : Control
    {
        int progress;

        public ProgressTrack()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        public int Value
        {
            set { progress = Math.Max(0, Math.Min(1000, value)); Invalidate(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Color track = Color.FromArgb(225, 235, 250), fill = Color.FromArgb(0, 105, 71);
            int radius = Math.Max(1, Height / 2);
            ConsolePaint.Tile(e.Graphics, ClientRectangle, radius, track, track);
            ConsolePaint.Tile(e.Graphics, new Rectangle(0, 0, Width * progress / 1000, Height), radius, fill, fill);
        }
    }

    sealed class GradeCard : Button
    {
        readonly string spec, colorName, numbers, status;
        readonly Color ink, fill;
        readonly bool active;
        readonly int current, goal;

        public GradeCard(string spec, string color, int current, int goal, bool active, Color ink, Color fill)
        {
            this.spec = spec;
            colorName = color;
            this.current = current;
            this.goal = goal;
            this.active = active;
            this.ink = ink;
            this.fill = fill;
            numbers = current + " / " + goal + " 只";
            status = goal > 0 && current == goal ? "已全部装满" : current > goal ? "超过目标" :
                active ? "当前装箱中" : "等待装箱";
            AccessibleName = color + " " + spec + " " + numbers;
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            Cursor = Cursors.Hand;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Color text = Color.FromArgb(11, 28, 48), primary = Color.FromArgb(0, 97, 148);
            Color green = Color.FromArgb(0, 105, 71), faint = Color.FromArgb(226, 236, 251);
            e.Graphics.Clear(Parent == null ? BackColor : Parent.BackColor);
            ConsolePaint.Tile(e.Graphics, ClientRectangle, ConsolePaint.Pixels(this, 11), active ? Color.FromArgb(238, 246, 255) :
                Color.White, active ? primary : faint);
            Func<int, int> px = value => ConsolePaint.Pixels(this, value);
            ConsolePaint.Dot(e.Graphics, px(13), px(14), px(38), fill);
            ConsolePaint.Dot(e.Graphics, px(23), px(24), px(18), ink);
            using (var title = new Font(Font.FontFamily, px(16), FontStyle.Bold, GraphicsUnit.Pixel))
            using (var note = new Font(Font.FontFamily, px(13), FontStyle.Regular, GraphicsUnit.Pixel))
            using (var figure = new Font("Consolas", px(21), FontStyle.Bold, GraphicsUnit.Pixel))
            {
                const TextFormatFlags left = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis;
                TextRenderer.DrawText(e.Graphics, colorName + " · " + spec, title,
                    new Rectangle(px(61), px(10), Math.Max(1, Width - px(203)), px(25)), active ? primary : text, left);
                Color hint = Color.FromArgb(86, 100, 119);
                TextRenderer.DrawText(e.Graphics, status + " · 剩余 " + Math.Max(0, goal - current) + " 只", note,
                    new Rectangle(px(61), px(38), Math.Max(1, Width - px(166)), px(20)), hint, left);
                TextRenderer.DrawText(e.Graphics, numbers, figure,
                    new Rectangle(Width - px(139), px(10), px(125), px(29)), goal > 0 && current >= goal ? green : primary,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
                if (goal > 0)
                    TextRenderer.DrawText(e.Graphics, (100.0 * current / goal).ToString("0.#") + "%", note,
                        new Rectangle(Width - px(100), px(39), px(86), px(19)), green,
                        TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
            }
            var track = new Rectangle(px(13), Height - px(20), Math.Max(1, Width - px(26)), px(8));
            ConsolePaint.Tile(e.Graphics, track, px(4), faint, faint);
            if (goal > 0 && current > 0)
            {
                track.Width = (int)(track.Width * Math.Min(1.0, current / (double)goal));
                Color progress = current >= goal ? green : primary;
                ConsolePaint.Tile(e.Graphics, track, px(4), progress, progress);
            }
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -3, -3));
        }
    }
}
