global using Window = PingWatchdog.Linux.CompatWindow;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace PingWatchdog.Linux;

/// <summary>
/// Keeps the code-only Linux UI readable on Avalonia 12, whose StyledElement.Theme
/// property otherwise shadows Ping Watchdog's Theme helper inside Window subclasses.
/// It also translates the pre-12 SystemDecorations assignment used by the first
/// Linux UI pass to the current WindowDecorations property.
/// </summary>
internal class CompatWindow : Avalonia.Controls.Window
{
    protected new static class Theme
    {
        public static IBrush Window => global::PingWatchdog.Linux.Theme.Window;
        public static IBrush Panel => global::PingWatchdog.Linux.Theme.Panel;
        public static IBrush Card => global::PingWatchdog.Linux.Theme.Card;
        public static IBrush Border => global::PingWatchdog.Linux.Theme.Border;
        public static IBrush Text => global::PingWatchdog.Linux.Theme.Text;
        public static IBrush Muted => global::PingWatchdog.Linux.Theme.Muted;
        public static IBrush Cyan => global::PingWatchdog.Linux.Theme.Cyan;
        public static IBrush Green => global::PingWatchdog.Linux.Theme.Green;
        public static IBrush Yellow => global::PingWatchdog.Linux.Theme.Yellow;
        public static IBrush Red => global::PingWatchdog.Linux.Theme.Red;
        public static IBrush Unknown => global::PingWatchdog.Linux.Theme.Unknown;

        public static IBrush Brush(string hex) =>
            global::PingWatchdog.Linux.Theme.Brush(hex);

        public static IBrush State(HostState state) =>
            global::PingWatchdog.Linux.Theme.State(state);

        public static Button Button(string text, bool accent = false) =>
            global::PingWatchdog.Linux.Theme.Button(text, accent);

        public static Border CardBorder(Control child, Thickness? margin = null) =>
            global::PingWatchdog.Linux.Theme.CardBorder(child, margin);

        public static TextBlock Label(
            string text,
            double size = 12,
            FontWeight? weight = null,
            IBrush? color = null) =>
            global::PingWatchdog.Linux.Theme.Label(text, size, weight, color);
    }

#pragma warning disable CS0618
    protected new DecorationShim SystemDecorations
    {
        get => new(WindowDecorations);
        set => WindowDecorations = value.Value;
    }
#pragma warning restore CS0618

    protected readonly record struct DecorationShim(Avalonia.Controls.WindowDecorations Value)
    {
        public DecorationShim None => new(Avalonia.Controls.WindowDecorations.None);
        public DecorationShim Full => new(Avalonia.Controls.WindowDecorations.Full);
        public DecorationShim BorderOnly => new(Avalonia.Controls.WindowDecorations.BorderOnly);
    }
}
