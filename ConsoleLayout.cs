using System;
using System.Drawing;

namespace CrabWeighing
{
    static class ConsoleLayout
    {
        public static readonly Size DesignSize = new Size(1280, 800);

        public static Rectangle WindowBounds(Rectangle screen)
        {
            int width = Math.Min(DesignSize.Width, screen.Width);
            int height = Math.Min(DesignSize.Height, screen.Height);
            return new Rectangle(screen.X + (screen.Width - width) / 2,
                screen.Y + (screen.Height - height) / 2, width, height);
        }

        public static Rectangle Place(Size canvas, Rectangle design)
        {
            int left = design.Left * canvas.Width / DesignSize.Width;
            int right = design.Right * canvas.Width / DesignSize.Width;
            int top = design.Top * canvas.Height / DesignSize.Height;
            int bottom = design.Bottom * canvas.Height / DesignSize.Height;
            return Rectangle.FromLTRB(left, top, right, bottom);
        }
    }
}
