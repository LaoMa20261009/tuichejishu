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

    sealed class ConsoleLabel : Label
    {
        protected override void OnPaint(PaintEventArgs e)
        {
            Rectangle box = Rectangle.FromLTRB(Padding.Left, Padding.Top,
                ClientRectangle.Right - Padding.Right, ClientRectangle.Bottom - Padding.Bottom);
            var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding;
            if (TextAlign == ContentAlignment.MiddleCenter) flags |= TextFormatFlags.HorizontalCenter;
            else if (TextAlign == ContentAlignment.MiddleRight) flags |= TextFormatFlags.Right;
            else flags |= TextFormatFlags.Left;
            TextRenderer.DrawText(e.Graphics, Text, Font, box, ForeColor, flags);
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
            ConsolePaint.Tile(e.Graphics, ClientRectangle, 8, active ? Color.FromArgb(238, 246, 255) :
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
                var badge = new Rectangle(px(61), px(38), px(86), px(20));
                Color statusInk = current > goal ? Color.FromArgb(153, 27, 27) : current == goal && goal > 0 ? green : primary;
                Color statusFill = current > goal ? Color.FromArgb(255, 225, 225) : current == goal && goal > 0 ?
                    Color.FromArgb(213, 245, 231) : Color.FromArgb(222, 235, 252);
                ConsolePaint.Tile(e.Graphics, badge, px(4), statusFill, statusFill);
                TextRenderer.DrawText(e.Graphics, status, note, badge, statusInk,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                TextRenderer.DrawText(e.Graphics, "剩余 " + Math.Max(0, goal - current) + " 只", note,
                    new Rectangle(px(153), px(38), Math.Max(1, Width - px(245)), px(20)), text, left);
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
