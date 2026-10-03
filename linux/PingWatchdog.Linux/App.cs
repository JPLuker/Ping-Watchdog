using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
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
            var engine = new WatchdogEngine();
            var updates = new LinuxUpdateService();
            var window = new MainWindow(engine, updates);

            desktop.MainWindow = window;
            desktop.Exit += (_, _) =>
            {
                updates.Dispose();
                engine.Dispose();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
