using System.Drawing.Drawing2D;

namespace PingWatchdog;

// Wallboard and map names backed by the shared Quiet Precision theme.
internal static class WallboardStyle
{
    internal static Color Background => AppTheme.Palette.Canvas;
    internal static Color Panel => AppTheme.Palette.Surface;
    internal static Color Line => AppTheme.Palette.Border;
    internal static Color Text => AppTheme.Palette.Text;
    internal static Color Muted => AppTheme.Palette.Secondary;
    internal static Color Accent => AppTheme.Palette.Accent;
    internal static Color Primary => AppTheme.Palette.Accent;
    internal static Color Selection => AppTheme.Palette.Surface3;
    internal static Color Green => AppTheme.Palette.Teal;
    internal static Color Yellow => AppTheme.Palette.Warning;
    internal static Color Red => AppTheme.Palette.Danger;

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
        // Cards: r-lg (12px), 1px border, no shadow (B4).
        using var path = Rounded(bounds, AppTheme.RadiusCard);
        graphics.FillPath(fill, path);
        graphics.DrawPath(border, path);
    }
}

internal sealed class WallboardButton : ThemeButton { }
