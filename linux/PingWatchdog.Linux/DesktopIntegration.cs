// Linux desktop services: clipboard, sign-in autostart, the priority alarm sound, support details,
// backups, restore and deleting saved data.
using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Threading;

namespace PingWatchdog.Linux;

// One helper for every paste: plain text first, then HTML-only clipboards (remote sessions, browsers).
internal static class ClipboardText
{
    private static readonly DataFormat<byte[]> Html = DataFormat.CreateBytesPlatformFormat("text/html");

    public static async Task<bool> SetAsync(Visual visual, string text)
    {
        try
        {
            if (TopLevel.GetTopLevel(visual)?.Clipboard is not { } clipboard) return false;
            await clipboard.SetTextAsync(text);
            return true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { return false; }
    }

    public static async Task<string?> GetAsync(Visual visual)
    {
        if (TopLevel.GetTopLevel(visual)?.Clipboard is not { } clipboard) return null;
        try
        {
            string? text = await clipboard.TryGetTextAsync();
            if (!string.IsNullOrEmpty(text)) return Normalize(text);
            byte[]? html = await clipboard.TryGetValueAsync(Html);
            return html is null ? null : Normalize(FromHtml(Encoding.UTF8.GetString(html)));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { return null; }
    }

    public static string Normalize(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n');

    // Table cells become tabs and rows become lines, so pasted spreadsheet rows still import.
    internal static string FromHtml(string html)
    {
        string text = Regex.Replace(html, @"<(script|style)[^>]*>.*?</\1>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"</t[dh]>\s*<t[dh][^>]*>", "\t", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<br\s*/?>|</tr>|</p>|</div>|</li>", "\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, "<[^>]+>", "");
        return System.Net.WebUtility.HtmlDecode(text).Trim();
    }

    // Single-line boxes join pasted lines with spaces; multi-line boxes keep them.
    public static void Attach(TextBox box)
    {
        box.PastingFromClipboard += async (_, e) =>
        {
            e.Handled = true;
            string? text = await GetAsync(box);
            if (text is null) return;
            if (!box.AcceptsReturn) text = Regex.Replace(text, @"\s*\n\s*", " ").Trim();
            Insert(box, text);
        };
    }

    internal static void Insert(TextBox box, string text)
    {
        string current = box.Text ?? "";
        int start = Math.Min(box.SelectionStart, box.SelectionEnd), end = Math.Max(box.SelectionStart, box.SelectionEnd);
        start = Math.Clamp(start, 0, current.Length);
        end = Math.Clamp(end, 0, current.Length);
        box.Text = current[..start] + text + current[end..];
        box.CaretIndex = start + text.Length;
        box.SelectionStart = box.SelectionEnd = box.CaretIndex;
    }
}

// "Open Ping Watchdog when you sign in": an XDG autostart entry.
internal static class Autostart
{
    internal static string? DirectoryOverride { get; set; }

    internal static string EntryPath
    {
        get
        {
            string? xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            string root = DirectoryOverride ?? (!string.IsNullOrWhiteSpace(xdg) ? xdg : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config"));
            return Path.Combine(root, "autostart", "ping-watchdog.desktop");
        }
    }

    internal static bool Enabled => File.Exists(EntryPath);

    // The AppImage path moves with the file; deb/rpm installs use the installed launcher.
    internal static string LaunchCommand
    {
        get
        {
            string? appImage = Environment.GetEnvironmentVariable("APPIMAGE");
            if (!string.IsNullOrWhiteSpace(appImage)) return Quote(appImage);
            if (LinuxUpdateService.PackageKind == "flatpak") return "flatpak run io.github.jpluker.PingWatchdog";
            return Quote(Environment.ProcessPath ?? "ping-watchdog");
        }
    }

    private static string Quote(string path) => path.Contains(' ') ? "\"" + path.Replace("\"", "\\\"") + "\"" : path;

    internal static string? Set(bool enabled)
    {
        try
        {
            if (!enabled)
            {
                if (File.Exists(EntryPath) && !new FileInfo(EntryPath).Attributes.HasFlag(FileAttributes.ReparsePoint)) File.Delete(EntryPath);
                return null;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(EntryPath)!);
            SafeFile.WriteAtomic(EntryPath, string.Join('\n',
                "[Desktop Entry]",
                "Type=Application",
                "Name=Ping Watchdog",
                "Exec=" + LaunchCommand + " --startup",
                "Icon=ping-watchdog",
                "X-GNOME-Autostart-enabled=true",
                "Terminal=false",
                ""));
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "Couldn't change the sign-in setting: " + ex.Message;
        }
    }
}

// Priority hosts ring every 15 seconds until each alert is acknowledged (Windows PriorityAlarm).
internal sealed record PriorityAlert(string Id, string Site, string Address, string DisplayHost, bool Offline, DateTime At)
{
    internal string Summary => $"{DisplayHost} · {Site} {(Offline ? "went offline" : "is back online")} · {DisplayTime.Clock(At)}";
}

internal sealed class PriorityAlarm : IDisposable
{
    internal static readonly TimeSpan RepeatInterval = TimeSpan.FromSeconds(15);
    private readonly List<PriorityAlert> _active = new();
    private readonly DispatcherTimer _timer = new() { Interval = RepeatInterval };
    private readonly Action<string?> _play;
    internal event Action? Changed;
    internal string SoundPath { get; set; } = "";
    internal IReadOnlyList<PriorityAlert> Active => _active;
    internal bool Ringing => _timer.IsEnabled;
    internal int RingCount { get; private set; }

    internal PriorityAlarm(Action<string?>? play = null)
    {
        _play = play ?? PlaySound;
        _timer.Tick += (_, _) => Ring();
    }

    internal PriorityAlert Raise(string site, string address, string displayHost, bool offline, DateTime at)
    {
        _active.RemoveAll(a => a.Site.Equals(site, StringComparison.OrdinalIgnoreCase) && a.Address.Equals(address, StringComparison.OrdinalIgnoreCase));
        var alert = new PriorityAlert(Guid.NewGuid().ToString("N"), site, address, displayHost, offline, at);
        _active.Add(alert);
        Ring();
        _timer.Stop(); _timer.Start();
        Changed?.Invoke();
        return alert;
    }

    internal bool Acknowledge(string id)
    {
        if (_active.RemoveAll(a => a.Id == id) == 0) return false;
        if (_active.Count == 0) _timer.Stop();
        Changed?.Invoke();
        return true;
    }

    internal void AcknowledgeAll()
    {
        if (_active.Count == 0) return;
        _active.Clear(); _timer.Stop();
        Changed?.Invoke();
    }

    // A host that is no longer priority, or was removed, stops ringing.
    internal void Forget(Func<PriorityAlert, bool> stale)
    {
        if (_active.RemoveAll(a => stale(a)) == 0) return;
        if (_active.Count == 0) _timer.Stop();
        Changed?.Invoke();
    }

    internal void Ring()
    {
        if (_active.Count == 0) return;
        RingCount++;
        _play(SoundPath);
    }

    internal static readonly string[] DesktopAlarms =
    {
        "/usr/share/sounds/freedesktop/stereo/alarm-clock-elapsed.oga",
        "/usr/share/sounds/freedesktop/stereo/complete.oga",
        "/usr/share/sounds/freedesktop/stereo/bell.oga"
    };

    // PipeWire or PulseAudio players first, then libcanberra, then the terminal bell.
    internal static void PlaySound(string? path)
    {
        string? file = !string.IsNullOrWhiteSpace(path) && File.Exists(path) ? path : DesktopAlarms.FirstOrDefault(File.Exists);
        var attempts = new List<(string Tool, string[] Args)>();
        if (file is not null)
        {
            attempts.Add(("pw-play", new[] { file }));
            attempts.Add(("paplay", new[] { file }));
        }
        attempts.Add(("canberra-gtk-play", new[] { "-i", "alarm-clock-elapsed" }));
        foreach (var (tool, args) in attempts)
        {
            try
            {
                var start = new ProcessStartInfo(tool) { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true };
                foreach (var arg in args) start.ArgumentList.Add(arg);
                using var process = Process.Start(start);
                if (process is not null) return;
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
        }
        try { Console.Write('\a'); } catch (IOException) { }
    }

    public void Dispose() => _timer.Stop();
}

internal static class SupportInfo
{
    internal const string IssueUrl = LinuxUpdateService.RepositoryUrl + "/issues/new/choose";

    internal static string ReadLicense()
    {
        foreach (var candidate in new[] { Path.Combine(AppContext.BaseDirectory, "LICENSE"), Path.Combine(AppContext.BaseDirectory, "..", "share", "doc", "ping-watchdog", "LICENSE") })
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
        return "Copyright © 2026 Joseph Luker. All rights reserved.\n\nThe license file isn't included in this copy. See " + LinuxUpdateService.RepositoryUrl + "/blob/main/LICENSE";
    }

    internal static string NoticesFolder => Path.Combine(AppContext.BaseDirectory, "ThirdPartyNotices");

    internal static string CreateDiagnostics(WatchdogEngine engine, LinuxUpdateService updates, string? screen)
    {
        var snapshot = engine.Snapshot();
        var hosts = snapshot.Sites.SelectMany(s => s.Hosts).ToList();
        var lines = new List<string>
        {
            "Ping Watchdog " + LinuxUpdateService.Version + " for Linux",
            "Package: " + LinuxUpdateService.PackageKind,
            "OS: " + RuntimeInformation.OSDescription,
            "Architecture: " + RuntimeInformation.OSArchitecture,
            ".NET: " + RuntimeInformation.FrameworkDescription,
            "Desktop: " + (Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") ?? "unknown"),
            "Session: " + (Environment.GetEnvironmentVariable("XDG_SESSION_TYPE") ?? "unknown") +
                (Environment.GetEnvironmentVariable("WATCHDOG_BACKEND") == "wayland" ? " (native Wayland backend)" : ""),
            "Display: " + (screen ?? "unknown"),
            "Monitoring: " + (snapshot.Monitoring ? "yes" : "no"),
            $"Sites: {snapshot.Sites.Count(s => !s.Temporary)} · Hosts: {hosts.Count} · Disabled: {hosts.Count(h => h.Disabled)} · Snoozed: {hosts.Count(h => h.Snoozed)} · Priority: {hosts.Count(h => h.Options.Priority)}",
            $"Timing: every {snapshot.Settings.PingIntervalSeconds}s, {snapshot.Settings.PingTimeoutMs} ms timeout, down after {snapshot.Settings.FailureThreshold}, recovered after {snapshot.Settings.RecoveryThreshold}",
            "Updates: " + (string.IsNullOrWhiteSpace(updates.Status) ? "not checked" : updates.Status),
            "Notifications: " + (snapshot.Settings.NotificationsEnabled ? "on" : "off") + (ToolExists("notify-send") ? "" : " (notify-send not found)"),
            "Configuration: " + engine.ConfigPath,
            "History: " + engine.HistoryPath,
        };
        // Addresses and names stay out: this text is meant to be pasted into a public issue.
        return string.Join('\n', lines);
    }

    internal static bool ToolExists(string tool) =>
        (Environment.GetEnvironmentVariable("PATH") ?? "").Split(':', StringSplitOptions.RemoveEmptyEntries).Any(dir => File.Exists(Path.Combine(dir, tool)));
}

// Backups, restore and "Delete saved data". Only files Ping Watchdog writes are touched.
internal static class DataStore
{
    internal const string ManifestName = "ping-watchdog-backup.json";
    private static readonly string[] Owned = { "autosave.pingwatch.json", "preferences.json" };

    internal sealed record Manifest(string App, string Version, DateTime Created, string Platform);

    internal static string BackupDirectory(WatchdogEngine engine) => Path.Combine(engine.StateDirectory, "backups");

    internal static string DefaultBackupName() => $"PingWatchdog-backup-{DateTime.Now:yyyy-MM-dd-HHmm}.zip";

    internal static void CreateBackup(WatchdogEngine engine, Stream output)
    {
        engine.Save();
        using var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        void Add(string path, string name)
        {
            if (!File.Exists(path)) return;
            var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
            using var target = entry.Open();
            using var source = File.OpenRead(path);
            source.CopyTo(target);
        }
        Add(engine.ConfigPath, "autosave.pingwatch.json");
        Add(engine.PreferencesPath, "preferences.json");
        Add(engine.HistoryPath, "event-history.json");
        var manifest = zip.CreateEntry(ManifestName);
        using var writer = new StreamWriter(manifest.Open());
        writer.Write(JsonSerializer.Serialize(new Manifest("Ping Watchdog", LinuxUpdateService.Version, DateTime.Now, "Linux")));
    }

    // Reads and checks a backup without changing anything. Returns an error, or the files to restore.
    internal static (Dictionary<string, byte[]>? Files, string? Error) ReadBackup(Stream input)
    {
        try
        {
            using var zip = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: true);
            var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (var entry in zip.Entries)
            {
                if (entry.Length > 64L * 1024 * 1024) return (null, "That backup has a file that's too large.");
                if (entry.FullName is not ("autosave.pingwatch.json" or "preferences.json" or "event-history.json" or ManifestName)) continue;
                using var stream = entry.Open();
                using var memory = new MemoryStream();
                stream.CopyTo(memory);
                files[entry.FullName] = memory.ToArray();
            }
            if (!files.ContainsKey(ManifestName)) return (null, "That file isn't a Ping Watchdog backup.");
            if (!files.TryGetValue("autosave.pingwatch.json", out var config)) return (null, "That backup has no host configuration.");
            var parsed = JsonSerializer.Deserialize<WatchdogConfig>(config);
            if (parsed?.Sites is null) return (null, "The configuration in that backup can't be read.");
            if (files.TryGetValue("event-history.json", out var history))
                JsonSerializer.Deserialize<List<StateEventRecord>>(history);
            return (files, null);
        }
        catch (Exception ex) when (ex is InvalidDataException or JsonException or IOException or NotSupportedException)
        {
            return (null, "That backup can't be read: " + ex.Message);
        }
    }

    // Keeps a copy of the current data first, then replaces each file atomically.
    internal static string? Restore(WatchdogEngine engine, Dictionary<string, byte[]> files)
    {
        try
        {
            Directory.CreateDirectory(BackupDirectory(engine));
            using (var safety = File.Create(Path.Combine(BackupDirectory(engine), "before-restore-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".zip")))
                CreateBackup(engine, safety);
            void Write(string name, string path)
            {
                if (!files.TryGetValue(name, out var bytes)) return;
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                string temp = path + ".restore";
                File.WriteAllBytes(temp, bytes);
                File.Move(temp, path, true);
            }
            Write("autosave.pingwatch.json", engine.ConfigPath);
            Write("preferences.json", engine.PreferencesPath);
            Write("event-history.json", engine.HistoryPath);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "Couldn't restore the backup: " + ex.Message;
        }
    }

    // Removes Ping Watchdog's own files. Anything else in those folders, and anything reached through a link, stays.
    internal static (int Removed, List<string> Kept) DeleteSavedData(WatchdogEngine engine)
    {
        var kept = new List<string>();
        int removed = 0;
        bool IsLink(string path) => new FileInfo(path).Attributes.HasFlag(FileAttributes.ReparsePoint);
        void DeleteFile(string path)
        {
            if (!File.Exists(path)) return;
            if (IsLink(path)) { kept.Add(path); return; }
            File.Delete(path); removed++;
        }
        bool OwnedName(string name) =>
            Owned.Contains(name) || name == "event-history.json" ||
            name.EndsWith(".tmp", StringComparison.Ordinal) || name.EndsWith(".restore", StringComparison.Ordinal) ||
            name.StartsWith("autosave.pingwatch.json.unreadable-", StringComparison.Ordinal);
        foreach (string directory in new[] { engine.ConfigDirectory, engine.StateDirectory })
        {
            if (!Directory.Exists(directory) || IsLink(directory)) { if (Directory.Exists(directory)) kept.Add(directory); continue; }
            foreach (string file in Directory.GetFiles(directory))
            {
                if (OwnedName(Path.GetFileName(file))) DeleteFile(file);
                else kept.Add(file);
            }
        }
        string backups = BackupDirectory(engine);
        if (Directory.Exists(backups) && !IsLink(backups))
        {
            foreach (string file in Directory.GetFiles(backups))
            {
                string name = Path.GetFileName(file);
                if (name.StartsWith("before-restore-", StringComparison.Ordinal) && name.EndsWith(".zip", StringComparison.Ordinal)) DeleteFile(file);
                else kept.Add(file);
            }
            if (Directory.GetFileSystemEntries(backups).Length == 0) Directory.Delete(backups);
        }
        foreach (string directory in new[] { engine.StateDirectory, engine.ConfigDirectory })
            if (Directory.Exists(directory) && !IsLink(directory) && Directory.GetFileSystemEntries(directory).Length == 0)
                Directory.Delete(directory);
        Autostart.Set(false);
        return (removed, kept);
    }

    // Starts this copy again (the AppImage path when there is one) after a restore.
    internal static bool Relaunch()
    {
        try
        {
            string? path = Environment.GetEnvironmentVariable("APPIMAGE");
            if (string.IsNullOrWhiteSpace(path)) path = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(path)) return false;
            var start = new ProcessStartInfo(path) { UseShellExecute = false };
            if (Path.GetFileNameWithoutExtension(path) == "dotnet" && Environment.GetCommandLineArgs().FirstOrDefault() is { } dll)
                start.ArgumentList.Add(dll);
            using var process = Process.Start(start);
            return process is not null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }
}
