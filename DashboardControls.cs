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
}
