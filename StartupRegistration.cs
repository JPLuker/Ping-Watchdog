using Microsoft.Win32;

namespace PingWatchdog;

// #40: "when I start my laptop the wallboard goes to the same screen and starts monitoring".
// Starting with Windows uses the per-user Run key, the standard place Windows reads at sign-in.
// No admin rights. Turning it off removes the value; uninstall removes it too (Program.InitializeUpdater).
internal static class StartupRegistration
{
    internal const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    internal const string ValueName = "Ping Watchdog";
    internal const string StartupArgument = "--startup";
    // Tests point this at a scratch key so they never touch the real Run key.
    internal static string KeyPath { get; set; } = RunKey;

    internal static string Command(string executable) => $"\"{executable}\" {StartupArgument}";

    internal static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
            return key?.GetValue(ValueName) is string value && value.Length > 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { return false; }
    }

    internal static void Set(bool enabled, string? executable = null)
    {
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath, writable: true)
            ?? throw new IOException("Windows did not open the startup list.");
        if (enabled)
        {
            string path = executable ?? Environment.ProcessPath ?? throw new IOException("The app location is unknown.");
            key.SetValue(ValueName, Command(path), RegistryValueKind.String);
        }
        else if (key.GetValue(ValueName) is not null) key.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    // Keeps the value pointing at the running copy after an update or a moved portable folder.
    internal static void RefreshPath()
    {
        try { if (IsEnabled()) Set(true); }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
    }

    internal static void RunTests()
    {
        string original = KeyPath;
        KeyPath = @"Software\PingWatchdog-Tests\Run-" + Guid.NewGuid().ToString("N");
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException("Startup registration: " + message); }
        try
        {
            Check(!IsEnabled(), "a new key reported enabled");
            Set(true, @"C:\Apps\Ping Watchdog\PingWatchdog.exe");
            Check(IsEnabled(), "enabling did not register");
            using (var key = Registry.CurrentUser.OpenSubKey(KeyPath))
                Check((string?)key?.GetValue(ValueName) == "\"C:\\Apps\\Ping Watchdog\\PingWatchdog.exe\" --startup", "the command is not quoted with --startup");
            Set(false);
            Check(!IsEnabled(), "disabling left the value");
            Set(false);
        }
        finally
        {
            try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\PingWatchdog-Tests", throwOnMissingSubKey: false); } catch (UnauthorizedAccessException) { }
            KeyPath = original;
        }
        Console.WriteLine("Startup registration tests passed.");
    }
}
