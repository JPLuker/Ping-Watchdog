using Avalonia;
using Velopack;

namespace PingWatchdog.Linux;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        VelopackApp.Build().Run();

        if (args.Contains("--self-test", StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                LinuxSelfTest.Run();
                LinuxRegressionTests.Run();
                Console.WriteLine("Ping Watchdog Linux self-test passed.");
                Environment.ExitCode = 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                Environment.ExitCode = 1;
            }
            return;
        }

        LinuxUiRegressionTests.Enabled = args.Contains("--ui-regression-test", StringComparer.OrdinalIgnoreCase);
        Environment.ExitCode = BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        var builder = AppBuilder.Configure<App>().UsePlatformDetect();
        // X11/XWayland remains the production default; native Wayland is an explicit option.
        bool nativeWayland = Environment.GetEnvironmentVariable("WATCHDOG_BACKEND") == "wayland";
        return (nativeWayland ? builder.UseWayland() : builder).LogToTrace();
    }
}
