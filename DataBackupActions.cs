using System.Diagnostics;
using System.Text.Json;

namespace PingWatchdog;

public sealed partial class MainForm
{
    private bool _restoreRequiresRestart;
    private string DataDirectory => Path.GetDirectoryName(_autoConfigPath)!;

    private DataBackup CreateDataBackup()
    {
        // Refuse to silently replace unreadable saved data with a session fallback.
        if (File.Exists(_autoConfigPath)) SafeConfigStore.Read(_autoConfigPath);
        var settings = GetAppSettingsSnapshot();
        var preferences = File.Exists(UserPreferencesPath)
            ? UserPreferenceStore.Read(UserPreferencesPath)
            : new UserPreferences
            {
                Appearance = settings.Appearance, RestoreLastSiteOnStartup = settings.RestoreLastSiteOnStartup,
                StartMonitoringOnLaunch = settings.StartMonitoringOnLaunch,
                ShowUpdateControlOnHome = settings.ShowUpdateControlOnHome
            };
        if (File.Exists(_eventHistoryPath))
        {
            var savedHistory = JsonSerializer.Deserialize<List<StateEventRecord>>(File.ReadAllText(_eventHistoryPath));
            if (savedHistory == null || savedHistory.Any(item => item == null || item.Timestamp == default ||
                string.IsNullOrWhiteSpace(item.Site) || string.IsNullOrWhiteSpace(item.Host) || string.IsNullOrWhiteSpace(item.Kind)))
                throw new InvalidDataException("Saved history is damaged. Keep the original and recover it before making a complete backup.");
        }
        return new DataBackup
        {
            CreatedUtc = DateTimeOffset.UtcNow, AppVersion = GetDisplayVersion(),
            Configuration = BuildConfig(), Preferences = preferences,
            History = GetStateEventSnapshot().ToList()
        };
    }

    private void ExportDataBackup()
    {
        var owner = (IWin32Window?)_settingsForm ?? this;
        using var dialog = new SaveFileDialog
        {
            Title = "Create complete backup", Filter = "Ping Watchdog backup (*.pingwatchbackup.json)|*.pingwatchbackup.json",
            FileName = "ping-watchdog-" + DateTime.Now.ToString("yyyy-MM-dd-HHmmss") + ".pingwatchbackup.json",
            DefaultExt = "pingwatchbackup.json", AddExtension = true
        };
        if (dialog.ShowDialog(owner) != DialogResult.OK) return;
        try
        {
            DataBackupStore.Write(dialog.FileName, CreateDataBackup(), DataDirectory);
            MessageBox.Show(owner, "Backup saved.", "Complete backup", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(owner, "Could not create the backup. " + ex.Message,
                "Backup failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void RestoreDataBackup()
    {
        IWin32Window owner = (IWin32Window?)_settingsForm ?? this;
        if (_cts != null)
        {
            MessageBox.Show(owner, "Stop monitoring before restoring a backup.", "Restore backup",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        using var dialog = new OpenFileDialog
        {
            Title = "Restore complete backup", Filter = "Ping Watchdog backup (*.pingwatchbackup.json)|*.pingwatchbackup.json",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(owner) != DialogResult.OK) return;
        DataBackup backup;
        try { backup = DataBackupStore.Read(dialog.FileName); }
        catch (Exception ex)
        {
            MessageBox.Show(owner, "The backup could not be opened. No data was changed. " + ex.Message,
                "Restore failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (MessageBox.Show(owner,
            $"Replace saved data with this backup?\r\n\r\nCreated: {backup.CreatedUtc.ToLocalTime():g}\r\n" +
            $"{backup.Configuration!.Sites.Count} sites, {backup.Configuration.Sites.Sum(site => site.Hosts.Count)} hosts, {backup.History!.Count} history events.\r\n\r\n" +
            "Sites, host settings, snoozes, preferences and history will be replaced. Nothing is merged. The current files will be retained for recovery. Ping Watchdog will restart.",
            "Restore backup", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;

        // Respect the existing Settings save/discard/cancel protection before changing data.
        var settingsWindow = _settingsForm;
        if (settingsWindow != null && !settingsWindow.IsDisposed)
        {
            settingsWindow.Close();
            if (!settingsWindow.IsDisposed) return;
        }
        try
        {
            DataBackupStore.Restore(DataDirectory, backup);
            _restoreRequiresRestart = true; // Closing must not autosave the old in-memory setup.
        }
        catch (Exception ex)
        {
            _restoreRequiresRestart = File.Exists(Path.Combine(DataDirectory, DataBackupStore.PendingName));
            MessageBox.Show(this, ex.Message, "Restore failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            if (_restoreRequiresRestart) Close();
            return;
        }
        try { Application.Restart(); }
        catch (Exception ex)
        {
            MessageBox.Show(this, "The backup was restored. Close and reopen Ping Watchdog to load it. " + ex.Message,
                "Restart required", MessageBoxButtons.OK, MessageBoxIcon.Information);
            Close();
        }
    }

    private void OpenDataFolder()
    {
        try
        {
            Directory.CreateDirectory(DataDirectory);
            Process.Start(new ProcessStartInfo(DataDirectory) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show((IWin32Window?)_settingsForm ?? this, "Could not open the data folder. " + ex.Message,
                "Data folder", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
