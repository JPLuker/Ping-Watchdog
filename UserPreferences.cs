using System.Text.Json;

namespace PingWatchdog;

internal sealed class UserPreferences
{
    public int FormatVersion { get; set; } = 1;
    public bool ShowUpdateControlOnHome { get; set; }
    public bool RestoreLastSiteOnStartup { get; set; }
    public bool StartMonitoringOnLaunch { get; set; }
    public bool OpenWallboardOnStartup { get; set; }
    // The display the Wallboard was last on (Screen.DeviceName); empty uses another screen than the main window.
    public string WallboardScreen { get; set; } = "";
    // Set just before an update installs; checked and cleared on the next start.
    public string PendingUpdateVersion { get; set; } = "";
    // VERSIONING.md 4.1 developer setting.
    public bool ShowVersionOverlay { get; set; }
    public AppearanceMode Appearance { get; set; } = AppearanceMode.System;
    // Keeps automatic update checks to once a day across restarts.
    public DateTimeOffset? LastUpdateCheckUtc { get; set; }
}

internal static class UserPreferenceStore
{
    internal static UserPreferences Read(string path)
    {
        string json = File.ReadAllText(path);
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Preferences must be a JSON object.");
        if (document.RootElement.TryGetProperty("FormatVersion", out var version) &&
            (version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out int value) || value != 1))
            throw new InvalidDataException("These preferences use an unsupported format. Restore a backup or update Ping Watchdog.");
        return JsonSerializer.Deserialize<UserPreferences>(json)
            ?? throw new InvalidDataException("Preferences are empty.");
    }

    internal static UserPreferences Load(string path, bool fallbackShowUpdateControl, Action<string>? reportError = null)
    {
        if (File.Exists(path))
        {
            try { return Read(path); }
            catch (Exception ex)
            {
                reportError?.Invoke("Preferences could not be opened. Defaults are being used for this session; the original file has been kept. Restore a backup before saving preferences.\r\n\r\n" + ex.Message);
            }
        }
        // Reading must not overwrite a damaged or unsupported file with defaults.
        return new UserPreferences { ShowUpdateControlOnHome = fallbackShowUpdateControl };
    }

    internal static void Save(string path, UserPreferences preferences)
    {
        if (preferences.FormatVersion != 1)
            throw new InvalidDataException("The preference format is unsupported.");
        if (File.Exists(path)) Read(path);
        SafeConfigStore.WriteAtomic(path, JsonSerializer.Serialize(preferences, new JsonSerializerOptions { WriteIndented = true }));
    }

    internal static void RunTests()
    {
        string directory = Path.Combine(Path.GetTempPath(), "PingWatchdog-pref-test-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "preferences.json");
        try
        {
            Save(path, new UserPreferences { ShowUpdateControlOnHome = true, RestoreLastSiteOnStartup = true, StartMonitoringOnLaunch = true, Appearance = AppearanceMode.Dark });
            var loaded = Load(path, fallbackShowUpdateControl: false);
            if (!loaded.ShowUpdateControlOnHome || !loaded.RestoreLastSiteOnStartup || !loaded.StartMonitoringOnLaunch || loaded.Appearance != AppearanceMode.Dark)
                throw new InvalidOperationException("User preference persistence regression.");
            File.WriteAllText(path, "{\"ShowUpdateControlOnHome\":true}");
            if (Load(path, false).Appearance != AppearanceMode.System)
                throw new InvalidOperationException("Legacy preferences must follow the system theme.");
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch { }
        }
    }
}

public sealed partial class MainForm
{
    private bool _restoreLastSiteOnStartup;
    private bool _startMonitoringOnLaunch;
    private bool _openWallboardOnStartup;
    private string _wallboardScreen = "";
    private string _appliedUpdateVersion = "";
    private void ApplyStartupView()
    {
        if (!_restoreLastSiteOnStartup) _selectedSiteName = null;
    }

    private string UserPreferencesPath => Path.Combine(StoragePaths.Current.DataDirectory, "preferences.json");

    private void LoadUserPreferences()
    {
        if (!_persistSites)
            return;

        var preferences = UserPreferenceStore.Load(
            UserPreferencesPath,
            _showUpdateControlOnHome,
            message => ReportStorageProblem(message));
        _showUpdateControlOnHome = preferences.ShowUpdateControlOnHome;
        _restoreLastSiteOnStartup = preferences.RestoreLastSiteOnStartup;
        _startMonitoringOnLaunch = preferences.StartMonitoringOnLaunch;
        _openWallboardOnStartup = preferences.OpenWallboardOnStartup;
        _wallboardScreen = preferences.WallboardScreen ?? "";
        _appliedUpdateVersion = preferences.PendingUpdateVersion ?? "";
        VersionOverlay.SetEnabled(preferences.ShowVersionOverlay);
        _lastUpdateCheckUtc = preferences.LastUpdateCheckUtc;
        AuroraTheme.SetMode(preferences.Appearance);
    }

    private DateTimeOffset? _lastUpdateCheckUtc;

    // Only the timestamp changes; every other saved preference is read back and kept.
    private void RecordUpdateCheck(DateTimeOffset when)
    {
        _lastUpdateCheckUtc = when;
        UpdateSavedPreference(p => p.LastUpdateCheckUtc = when);
    }

    // Remembers which display the Wallboard was on, so it opens there next time.
    private void RecordWallboardScreen(string deviceName)
    {
        if (deviceName.Length == 0 || deviceName == _wallboardScreen) return;
        _wallboardScreen = deviceName;
        UpdateSavedPreference(p => p.WallboardScreen = deviceName);
    }

    private UserPreferences CurrentPreferences() => new()
    {
        ShowUpdateControlOnHome = _showUpdateControlOnHome, RestoreLastSiteOnStartup = _restoreLastSiteOnStartup,
        StartMonitoringOnLaunch = _startMonitoringOnLaunch, OpenWallboardOnStartup = _openWallboardOnStartup,
        WallboardScreen = _wallboardScreen, Appearance = AuroraTheme.Mode, LastUpdateCheckUtc = _lastUpdateCheckUtc,
        ShowVersionOverlay = VersionOverlay.Enabled
    };

    // Background bookkeeping: read the saved file, change one value, keep the rest. Failures stay quiet.
    private void UpdateSavedPreference(Action<UserPreferences> change)
    {
        if (!_persistSites) return;
        try
        {
            var preferences = File.Exists(UserPreferencesPath) ? UserPreferenceStore.Read(UserPreferencesPath) : CurrentPreferences();
            change(preferences);
            UserPreferenceStore.Save(UserPreferencesPath, preferences);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException) { }
    }

}
