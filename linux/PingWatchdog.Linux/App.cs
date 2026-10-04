using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace PingWatchdog.Linux;

internal sealed class App : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        RequestedThemeVariant = ThemeVariant.Dark;
    }

    public override void OnFrameworkInitializationCompleted()
    {
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
