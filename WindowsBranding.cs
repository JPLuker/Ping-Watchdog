using System.Runtime.InteropServices;
using Velopack.Locators;
using Velopack.Windows;

namespace PingWatchdog;

internal static class WindowsBranding
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern void SHChangeNotify(uint eventId, uint flags, string item, IntPtr unused);

    internal static void Refresh()
    {
        // Branding repair must never prevent monitoring from starting.
        try
        {
            var locator = VelopackLocator.Current;
            string appId = locator?.AppUserModelId ?? "velopack.PingWatchdog";
            SetCurrentProcessExplicitAppUserModelID(appId);
            if (locator?.RootAppDir is not string root || !File.Exists(BrandAssets.ShellIconPath))
                return;

            var locations = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Microsoft", "Internet Explorer", "Quick Launch", "User Pinned", "TaskBar")
            };
            foreach (string location in locations.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                foreach (string path in Directory.EnumerateFiles(location, "*.lnk", SearchOption.TopDirectoryOnly))
                {
                    try
                    {
                        if (RefreshShortcut(path, root, appId, BrandAssets.ShellIconPath))
                            SHChangeNotify(0x2000, 0x5, path, IntPtr.Zero); // UPDATEITEM, PATHW
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Trace.WriteLine($"Watchdog shortcut branding: {ex.Message}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"Watchdog shell branding: {ex.Message}");
        }
    }

    private static bool RefreshShortcut(string path, string appRoot, string appId, string iconPath)
    {
        using var link = new ShellLink(path);
        string target = Path.GetFullPath(Environment.ExpandEnvironmentVariables(link.Target));
        string root = Path.GetFullPath(appRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            return false;
        string file = Path.GetFileName(target);
        if (!file.Equals("PingWatchdog.exe", StringComparison.OrdinalIgnoreCase) &&
            !(file.Equals("Update.exe", StringComparison.OrdinalIgnoreCase) &&
              link.Arguments.Contains("PingWatchdog.exe", StringComparison.OrdinalIgnoreCase)))
            return false;
        if (string.Equals(link.IconPath, iconPath, StringComparison.OrdinalIgnoreCase) && link.IconIndex == 0)
            return false;

        // Keep the launch target, arguments, and pin intact. Only refresh this app's branding.
        link.IconPath = iconPath;
        link.IconIndex = 0;
        link.SetAppUserModelId(appId);
        link.Save();
        return true;
    }

    internal static void RunShortcutSmokeTest(string output)
    {
        if (!File.Exists(BrandAssets.ShellIconPath) || !File.Exists(BrandAssets.NotificationIconPath))
            throw new InvalidOperationException("Published shell/notification artwork is missing.");
        string path = Path.Combine(output, "branding-test.lnk");
        string target = Application.ExecutablePath;
        const string arguments = "--branding-test";
        using (var link = new ShellLink())
        {
            link.Target = target;
            link.Arguments = arguments;
            link.IconPath = Path.Combine(AppContext.BaseDirectory, "old-heartbeat.ico");
            link.Save(path);
        }
        if (!RefreshShortcut(path, AppContext.BaseDirectory, "velopack.PingWatchdog", BrandAssets.ShellIconPath))
            throw new InvalidOperationException("Legacy shortcut icon was not repaired.");
        using (var link = new ShellLink(path))
        {
            if (link.Target != target || link.Arguments != arguments || link.IconPath != BrandAssets.ShellIconPath)
                throw new InvalidOperationException("Shortcut branding did not preserve its launch settings.");
        }
        if (RefreshShortcut(path, AppContext.BaseDirectory, "velopack.PingWatchdog", BrandAssets.ShellIconPath))
            throw new InvalidOperationException("Shortcut branding repeats when already current.");
        using (var unrelated = new ShellLink())
        {
            unrelated.Target = Path.Combine(AppContext.BaseDirectory, "OtherApp.exe");
            unrelated.Save(path);
        }
        if (RefreshShortcut(path, AppContext.BaseDirectory, "velopack.PingWatchdog", BrandAssets.ShellIconPath))
            throw new InvalidOperationException("Shortcut branding changed another application.");
        File.Delete(path);
    }
}
