using System.Text.Json;

namespace PingWatchdog;

internal static class SettingsPersistence
{
    internal static void Save(string configPath, string preferencesPath, WatchdogConfig config, UserPreferences preferences)
    {
        // Validate both originals before changing either file.
        SafeConfigStore.EnsureSupported(config.FormatVersion);
        if (File.Exists(configPath)) SafeConfigStore.Read(configPath);
        bool hadPreferences = File.Exists(preferencesPath);
        if (hadPreferences) UserPreferenceStore.Read(preferencesPath);
        string? originalPreferences = hadPreferences ? File.ReadAllText(preferencesPath) : null;
        UserPreferenceStore.Save(preferencesPath, preferences);
        try { SafeConfigStore.Write(configPath, config); }
        catch (Exception saveError)
        {
            try
            {
                if (hadPreferences) SafeConfigStore.WriteAtomic(preferencesPath, originalPreferences!);
                else File.Delete(preferencesPath);
            }
            catch (Exception rollbackError)
            {
                throw new IOException("Settings were not fully saved, and previous preferences could not be restored. Keep the window open and restore the saved backups. " +
                    saveError.Message + " " + rollbackError.Message, new AggregateException(saveError, rollbackError));
            }
            throw new IOException("Settings could not be saved. Previous preferences were restored. " + saveError.Message, saveError);
        }
    }

    internal static void RunTests()
    {
        string directory = Path.Combine(Path.GetTempPath(), "PingWatchdog-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string config = Path.Combine(directory, "config.json"), preferences = Path.Combine(directory, "preferences.json");
        void Check(bool value) { if (!value) throw new InvalidOperationException("Settings persistence regression."); }
        try
        {
            Save(config, preferences, new WatchdogConfig(), new UserPreferences());
            Save(config, preferences, new WatchdogConfig { PingIntervalSeconds = 9 }, new UserPreferences { Appearance = AppearanceMode.Dark });
            Check(SafeConfigStore.Read(config).PingIntervalSeconds == 9 && UserPreferenceStore.Read(preferences).Appearance == AppearanceMode.Dark);
            string configBefore = File.ReadAllText(config), preferencesBefore = File.ReadAllText(preferences);
            using (var locked = new FileStream(config, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                bool failed = false;
                try { Save(config, preferences, new WatchdogConfig(), new UserPreferences()); }
                catch (IOException) { failed = true; }
                Check(failed);
            }
            Check(File.ReadAllText(config) == configBefore && File.ReadAllText(preferences) == preferencesBefore);

            // A directory at the destination forces a real commit failure after preferences were written.
            string blockedConfig = Path.Combine(directory, "blocked.json");
            Directory.CreateDirectory(blockedConfig);
            bool rolledBack = false;
            try { Save(blockedConfig, preferences, new WatchdogConfig(), new UserPreferences { Appearance = AppearanceMode.Light }); }
            catch (IOException) { rolledBack = true; }
            Check(rolledBack && File.ReadAllText(preferences) == preferencesBefore);
            Check(!Directory.GetFiles(directory).Any(path => path.EndsWith(".tmp")));

            File.WriteAllText(preferences, "{broken");
            string? warning = null;
            UserPreferenceStore.Load(preferences, false, message => warning = message);
            Check(warning != null && File.ReadAllText(preferences) == "{broken");
            bool damagedRefused = false;
            try { Save(config, preferences, new WatchdogConfig(), new UserPreferences()); }
            catch (JsonException) { damagedRefused = true; }
            Check(damagedRefused && File.ReadAllText(config) == configBefore && File.ReadAllText(preferences) == "{broken");

            string future = "{\"FormatVersion\":999,\"NewPreference\":\"keep\"}";
            File.WriteAllText(preferences, future);
            bool futureRefused = false;
            try { UserPreferenceStore.Save(preferences, new UserPreferences()); }
            catch (InvalidDataException) { futureRefused = true; }
            Check(futureRefused && File.ReadAllText(preferences) == future);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}

public sealed partial class MainForm
{
    private void PersistAppSettings(AppSettingsSnapshot settings)
    {
        if (!_persistSites) return;
        if (_configWriteBlocked)
            throw new InvalidOperationException("Autosave is off because the saved configuration could not be opened. The original file is protected. Restore a backup or update Ping Watchdog before saving settings.");

        var config = BuildConfig(captureEditor: false);
        if (_cts == null)
        {
            config.PingIntervalSeconds = settings.PingIntervalSeconds;
            config.PingTimeoutMs = settings.PingTimeoutMs;
            config.FailureThreshold = settings.FailureThreshold;
            config.RecoveryThreshold = settings.RecoveryThreshold;
        }
        config.ShowCommandView = settings.ShowCommandView;
        config.WallboardShowCli = settings.WallboardShowCli;
        config.MinimizeToTray = settings.MinimizeToTray;
        config.NotificationsEnabled = settings.NotificationsEnabled;
        config.PriorityAlarmSound = settings.PriorityAlarmSound ?? "";
        config.AutoCheckUpdates = settings.AutoCheckUpdates;
        config.ShowUpdateControlOnHome = settings.ShowUpdateControlOnHome;
        config.EventHistoryHours = NormalizeEventHistoryHours(settings.EventHistoryHours);
        config.HideSuspectEvents = settings.HideSuspectEvents;
        config.Use12HourTime = settings.Use12HourTime;

        SettingsPersistence.Save(_autoConfigPath, UserPreferencesPath, config, new UserPreferences
        {
            ShowUpdateControlOnHome = settings.ShowUpdateControlOnHome,
            RestoreLastSiteOnStartup = settings.RestoreLastSiteOnStartup,
            StartMonitoringOnLaunch = settings.StartMonitoringOnLaunch,
            Appearance = settings.Appearance,
            LastUpdateCheckUtc = _lastUpdateCheckUtc
        });
    }
}
