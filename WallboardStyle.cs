using System.Drawing.Drawing2D;

namespace PingWatchdog;

// Compatibility names backed by the common DESIGN.md theme.
internal static class WallboardStyle
{
    internal static Color Background => AuroraTheme.Palette.Canvas;
    internal static Color Panel => AuroraTheme.Palette.Surface;
    internal static Color Line => AuroraTheme.Palette.Surface3;
    internal static Color Text => AuroraTheme.Palette.Text;
    internal static Color Muted => AuroraTheme.Palette.Secondary;
    internal static Color Accent => AuroraTheme.Palette.Accent;
    internal static Color Primary => AuroraTheme.Palette.Accent;
    internal static Color Selection => AuroraTheme.Palette.Surface3;
    internal static Color Green => AuroraTheme.Palette.Teal;
    internal static Color Yellow => AuroraTheme.Palette.Warning;
    internal static Color Red => AuroraTheme.Palette.Danger;

    internal static GraphicsPath Rounded(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        int d = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
        path.AddArc(bounds.Left, bounds.Top, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Top, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    internal static void DrawSurface(Graphics graphics, Rectangle bounds, Brush fill, Pen border)
    {
        using var path = Rounded(bounds, 14);
        graphics.FillPath(fill, path);
        graphics.DrawPath(border, path);
    }
}

internal sealed class WallboardButton : AuroraButton { }
