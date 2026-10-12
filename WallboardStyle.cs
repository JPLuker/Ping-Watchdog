using System.Drawing.Drawing2D;

namespace PingWatchdog;

// Windows-only presentation tokens adapted from Watchdog Toolkit/STYLE-GUIDE.md.
internal static class WallboardStyle
{
    internal static readonly Color Background = ColorTranslator.FromHtml("#0d101b");
    internal static readonly Color Panel = ColorTranslator.FromHtml("#171b2c");
    internal static readonly Color Line = ColorTranslator.FromHtml("#292f45");
    internal static readonly Color Text = ColorTranslator.FromHtml("#edf0fb");
    internal static readonly Color Muted = ColorTranslator.FromHtml("#919ab6");
    internal static readonly Color Accent = ColorTranslator.FromHtml("#9a83ff");
    internal static readonly Color Primary = ColorTranslator.FromHtml("#8d72ef");
    internal static readonly Color Selection = ColorTranslator.FromHtml("#302846");
    internal static readonly Color Green = ColorTranslator.FromHtml("#59d9b6");
    internal static readonly Color Yellow = ColorTranslator.FromHtml("#edc477");
    internal static readonly Color Red = ColorTranslator.FromHtml("#ef879d");

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

internal sealed class WallboardButton : Button
{
    private bool _hover;
    protected override void OnMouseEnter(EventArgs e) { _hover = true; base.OnMouseEnter(e); Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; base.OnMouseLeave(e); Invalidate(); }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.Clear(Parent?.BackColor ?? WallboardStyle.Background);
        var bounds = new Rectangle(1, 1, Width - 3, Height - 3);
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        using var path = WallboardStyle.Rounded(bounds, 8);
        using var fill = new SolidBrush(_hover && Enabled ? WallboardStyle.Selection : BackColor);
        using var border = new Pen(Focused ? WallboardStyle.Accent : FlatAppearance.BorderColor, Focused ? 2 : 1);
        e.Graphics.FillPath(fill, path);
        e.Graphics.DrawPath(border, path);
        TextRenderer.DrawText(e.Graphics, Text, Font, bounds, Enabled ? ForeColor : WallboardStyle.Muted,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
    }
}
