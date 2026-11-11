using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Media;

namespace PingWatchdog.Linux;

internal sealed class App : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        // Follow the desktop's light or dark preference (Quiet Precision has both).
        RequestedThemeVariant = ThemeVariant.Default;
        Styles.Add(new Style(s => s.OfType<Avalonia.Controls.Window>()) { Setters = {
            new Setter(Avalonia.Controls.Window.FontFamilyProperty, new FontFamily("Segoe UI, Noto Sans, DejaVu Sans")),
            new Setter(Avalonia.Controls.Window.FontSizeProperty, Theme.BodyFontSize)
        } });
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Resolve the palette before any window or control reads a brush.
        bool dark = Environment.GetEnvironmentVariable("WATCHDOG_THEME") is { } forced
            ? forced.Equals("dark", StringComparison.OrdinalIgnoreCase)
            : ActualThemeVariant == ThemeVariant.Dark;
        Theme.Use(dark);
        RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (LinuxUiRegressionTests.Enabled)
            {
                LinuxUiRegressionTests.Start(desktop);
                base.OnFrameworkInitializationCompleted();
                return;
            }
            var engine = new WatchdogEngine();
            var updates = new LinuxUpdateService();
            var window = new MainWindow(engine, updates);
            var tray = new LinuxTrayService(this, window, engine, updates);
            window.AttachTray(tray);

            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
            desktop.MainWindow = window;
            desktop.Exit += (_, _) =>
            {
                tray.Dispose();
                updates.Dispose();
                engine.Dispose();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
