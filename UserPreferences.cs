using System.Text.Json;

namespace PingWatchdog;

internal sealed class UserPreferences
{
    public bool ShowUpdateControlOnHome { get; set; }
    public bool RestoreLastSiteOnStartup { get; set; }
}

internal static class UserPreferenceStore
{
    internal static UserPreferences Load(string path, bool fallbackShowUpdateControl)
    {
        try
        {
            if (File.Exists(path))
            {
                var loaded = JsonSerializer.Deserialize<UserPreferences>(File.ReadAllText(path));
                if (loaded is not null)
                    return loaded;
            }
        }
        catch
        {
            // A damaged preference file must never prevent monitoring.
        }

        var fallback = new UserPreferences
        {
            ShowUpdateControlOnHome = fallbackShowUpdateControl
        };
        Save(path, fallback);
        return fallback;
    }

    internal static void Save(string path, UserPreferences preferences)
    {
        try
        {
            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            string temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(preferences, new JsonSerializerOptions
            {
                WriteIndented = true
            }));
            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            // UI preferences are best-effort and must not block monitoring.
        }
    }

    internal static void RunTests()
    {
        string directory = Path.Combine(Path.GetTempPath(), "PingWatchdog-pref-test-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "preferences.json");
        try
        {
            Save(path, new UserPreferences { ShowUpdateControlOnHome = true });
            var loaded = Load(path, fallbackShowUpdateControl: false);
            if (!loaded.ShowUpdateControlOnHome)
                throw new InvalidOperationException("User preference persistence regression.");
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
    private void ApplyStartupView()
    {
        if (!_restoreLastSiteOnStartup) _selectedSiteName = null;
    }

    private string UserPreferencesPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PingWatchdog",
        "preferences.json");

    private void LoadUserPreferences()
    {
        if (!_persistSites)
            return;

        var preferences = UserPreferenceStore.Load(
            UserPreferencesPath,
            _showUpdateControlOnHome);
        _showUpdateControlOnHome = preferences.ShowUpdateControlOnHome;
        _restoreLastSiteOnStartup = preferences.RestoreLastSiteOnStartup;
    }

    private void SaveUserPreferences()
    {
        if (!_persistSites)
            return;

        UserPreferenceStore.Save(
            UserPreferencesPath,
            new UserPreferences
            {
                ShowUpdateControlOnHome = _showUpdateControlOnHome,
                RestoreLastSiteOnStartup = _restoreLastSiteOnStartup
            });
    }
}
