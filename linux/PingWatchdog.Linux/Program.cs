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

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder
            .Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
