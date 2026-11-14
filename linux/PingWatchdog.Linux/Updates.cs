// In-app updates for the AppImage (Velopack, Linux channel). Other package types say where updates come from.
using System.Diagnostics;
using System.Reflection;
using Velopack;
using Velopack.Sources;

namespace PingWatchdog.Linux;

internal sealed class LinuxUpdateService : IDisposable
{
    internal const string RepositoryUrl = "https://github.com/JPLuker/Ping-Watchdog";
    internal const string ReleasesUrl = RepositoryUrl + "/releases";
    private readonly CancellationTokenSource _lifetime = new();
    private UpdateManager? _pendingManager;
    private UpdateInfo? _pendingUpdate;
    private bool _checking;

    public event EventHandler? Changed;
    public event EventHandler<string>? UpdateReady;
    public string Status { get; private set; } = "";
    public bool Checking => _checking;
    public bool HasPendingUpdate => _pendingUpdate is not null;
    public string? PendingVersion => _pendingUpdate?.TargetFullRelease.Version.ToString();
    public string ActionText => _pendingUpdate is null ? "Check for updates" : "Restart to update";
    public static string Version => Assembly.GetExecutingAssembly().GetName().Version is { } v
        ? $"{v.Major}.{v.Minor}.{Math.Max(0, v.Build)}"
        : "0.0.0";

    // deb, rpm, tar.gz and Flatpak builds carry a marker file; the AppImage doesn't need one.
    public static string PackageKind
    {
        get
        {
            try
            {
                string marker = Path.Combine(AppContext.BaseDirectory, "package-kind.txt");
                if (File.Exists(marker)) return File.ReadAllText(marker).Trim();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            return Environment.GetEnvironmentVariable("APPIMAGE") is { Length: > 0 } ? "AppImage" : "development";
        }
    }

    public static string UnmanagedText => PackageKind switch
    {
        "deb" or "rpm" => "Updates come with new packages from the downloads page.",
        "flatpak" => "Updates come with new Flatpak bundles from the downloads page.",
        "tar.gz" => "Download new versions from the downloads page.",
        _ => "This copy doesn't update itself."
    };

    public async Task CheckAsync(bool userInitiated)
    {
        if (_checking || _pendingUpdate is not null)
            return;

        _checking = true;
        Status = "Checking for updates…";
        Changed?.Invoke(this, EventArgs.Empty);

        try
        {
            var source = new GithubSource(RepositoryUrl, accessToken: null, prerelease: true);
            var manager = new UpdateManager(source, new UpdateOptions { ExplicitChannel = "linux" });
            if (!manager.IsInstalled)
            {
                Status = UnmanagedText;
                return;
            }

            var update = await manager.CheckForUpdatesAsync();
            if (update is null)
            {
                Status = "Up to date";
                return;
            }

            string version = update.TargetFullRelease.Version.ToString();
            Status = $"Downloading {version}…";
            Changed?.Invoke(this, EventArgs.Empty);

            await manager.DownloadUpdatesAsync(update, progress =>
            {
                Status = $"Downloading {version}… {progress}%";
                Changed?.Invoke(this, EventArgs.Empty);
            });

            _pendingManager = manager;
            _pendingUpdate = update;
            Status = $"Version {version} is ready. Restart to update.";
            Changed?.Invoke(this, EventArgs.Empty);
            UpdateReady?.Invoke(this, version);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Any failure here is reported, never thrown: monitoring keeps running.
            Status = userInitiated ? $"Couldn't check for updates: {ex.Message}" : "Couldn't check for updates";
        }
        finally
        {
            _checking = false;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    // At most once a day, as on Windows.
    public async Task RunBackgroundAsync(Func<bool> enabled)
    {
        try
        {
            if (enabled())
                await CheckAsync(false);

            while (await Task.Delay(TimeSpan.FromHours(24), _lifetime.Token).ContinueWith(
                task => !task.IsCanceled,
                CancellationToken.None))
            {
                if (enabled())
                    await CheckAsync(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    public void ApplyAndRestart(WatchdogEngine engine)
    {
        if (_pendingManager is null || _pendingUpdate is null)
            return;
        LinuxUpdateRestart.Apply(engine, () => _pendingManager.ApplyUpdatesAndRestart(_pendingUpdate));
    }

    public static void OpenDownloads() => DesktopLauncher.Open(ReleasesUrl);

    public void Dispose()
    {
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}

internal static class LinuxUpdateRestart
{
    public static void Apply(WatchdogEngine engine, Action applyUpdate)
    {
        engine.Save();
        bool wasMonitoring = engine.Monitoring;
        engine.StopMonitoring();
        try
        {
            applyUpdate();
        }
        catch
        {
            if (wasMonitoring) engine.StartMonitoring();
            throw;
        }
    }
}

// Opens links, folders and files with the desktop's default handler.
internal static class DesktopLauncher
{
    internal static Func<string, bool> Launcher { get; set; } = Launch;

    internal static bool Open(string target) => Launcher(target);

    private static bool Launch(string target)
    {
        try
        {
            var start = new ProcessStartInfo("xdg-open") { UseShellExecute = false };
            start.ArgumentList.Add(target);
            using var process = Process.Start(start);
            return process is not null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }
}
