using System.Drawing;

namespace ExHyperV.Tools
{
    internal static class ConsoleWindowBounds
    {
        // Both rectangles use physical screen pixels, including negative monitor origins.
        internal static Rectangle Constrain(Rectangle window, Rectangle workArea)
        {
            if (workArea.Width <= 0 || workArea.Height <= 0) return window;
            int width = Math.Max(1, Math.Min(window.Width, workArea.Width));
            int height = Math.Max(1, Math.Min(window.Height, workArea.Height));
            int left = Math.Max(workArea.Left, Math.Min(window.Left, workArea.Right - width));
            int top = Math.Max(workArea.Top, Math.Min(window.Top, workArea.Bottom - height));
            return new Rectangle(left, top, width, height);
        }
    }
}
