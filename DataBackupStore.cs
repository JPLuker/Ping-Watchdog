using System.Text;
using System.Text.Json;

namespace PingWatchdog;

internal sealed class DataBackup
{
    public int FormatVersion { get; set; } = 1;
    public DateTimeOffset CreatedUtc { get; set; }
    public string AppVersion { get; set; } = "";
    public WatchdogConfig? Configuration { get; set; }
    public UserPreferences? Preferences { get; set; }
    public List<StateEventRecord>? History { get; set; }
}

internal sealed class RestoreJournal
{
    public int FormatVersion { get; set; } = 1;
    public Dictionary<string, string?> Originals { get; set; } = new();
}

internal static class DataBackupStore
{
    internal static readonly string[] FileNames =
        { "autosave.pingwatch.json", "preferences.json", "event-history.json", "sites.json" };
    internal const string PendingName = "restore-pending.json";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    internal static void Validate(DataBackup backup)
    {
        if (backup.FormatVersion != 1 || backup.CreatedUtc == default ||
            backup.Configuration == null || backup.Preferences == null || backup.History == null)
            throw new InvalidDataException("This backup is incomplete or uses an unsupported format.");
        var config = backup.Configuration;
        SafeConfigStore.EnsureSupported(config.FormatVersion);
        if (backup.Preferences.FormatVersion != 1 || !Enum.IsDefined(backup.Preferences.Appearance))
            throw new InvalidDataException("The backup preferences use an unsupported format or appearance.");
        if (config.Sites == null || config.Sites.Count == 0 || config.SiteFolders == null ||
            config.SiteFolders.Any(folder => folder == null) ||
            config.PingIntervalSeconds is < 1 or > 300 || config.PingTimeoutMs is < 250 or > 10000 ||
            config.FailureThreshold is < 2 or > 20 || config.RecoveryThreshold is < 1 or > 20)
            throw new InvalidDataException("The backup configuration has invalid sites or timing values.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var site in config.Sites)
        {
            if (site == null || string.IsNullOrWhiteSpace(site.Name) || site.Name != site.Name.Trim() ||
                site.Name.Equals("All Sites", StringComparison.OrdinalIgnoreCase) || !names.Add(site.Name) ||
                site.Hosts == null || site.Hosts.Any(host => string.IsNullOrWhiteSpace(host) || host != host.Trim()) ||
                site.Hosts.Distinct(StringComparer.OrdinalIgnoreCase).Count() != site.Hosts.Count ||
                site.Labels == null || site.Categories == null || site.HostDetails == null ||
                site.HostDetails.Any(pair => pair.Value == null))
                throw new InvalidDataException("The backup contains an invalid or duplicate site or host.");
        }
        if (backup.History.Any(item => item == null || item.Timestamp == default ||
            string.IsNullOrWhiteSpace(item.Site) || string.IsNullOrWhiteSpace(item.Host) ||
            string.IsNullOrWhiteSpace(item.Kind)))
            throw new InvalidDataException("The backup contains invalid history records.");
    }

    internal static DataBackup Read(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("FormatVersion", out var version) || version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out int format) || format != 1)
            throw new InvalidDataException("This is not a supported Ping Watchdog backup.");
        foreach (string section in new[] { "Configuration", "Preferences" })
        {
            if (!root.TryGetProperty(section, out var value) || value.ValueKind != JsonValueKind.Object ||
                !value.TryGetProperty("FormatVersion", out var sectionVersion) ||
                sectionVersion.ValueKind != JsonValueKind.Number || !sectionVersion.TryGetInt32(out int number) || number != 1)
                throw new InvalidDataException("The backup has an incomplete or unsupported " + section.ToLowerInvariant() + " section.");
        }
        var backup = JsonSerializer.Deserialize<DataBackup>(root.GetRawText())
            ?? throw new InvalidDataException("The backup is empty.");
        Validate(backup);
        return backup;
    }

    internal static void Write(string path, DataBackup backup, string dataDirectory)
    {
        Validate(backup);
        // A complete backup must not replace a live data file or recovery journal.
        string folder = Path.GetFullPath(dataDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (Path.GetFullPath(path).StartsWith(folder, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Save the backup outside the application data folder.");
        if (File.Exists(path)) Read(path); // Do not overwrite an unrelated or unsupported file.
        SafeConfigStore.WriteAtomic(path, JsonSerializer.Serialize(backup, JsonOptions));
    }

    private static RestoreJournal ReadJournal(string path)
    {
        var journal = JsonSerializer.Deserialize<RestoreJournal>(File.ReadAllText(path))
            ?? throw new InvalidDataException("The restore recovery record is empty.");
        if (journal.FormatVersion != 1 || journal.Originals == null ||
            journal.Originals.Count != FileNames.Length || FileNames.Any(name => !journal.Originals.ContainsKey(name)))
            throw new InvalidDataException("The restore recovery record is invalid. Keep it for recovery.");
        // Validate every byte payload before changing any destination; no archive-provided paths are accepted.
        foreach (string? value in journal.Originals.Values)
            if (value != null) Convert.FromBase64String(value);
        return journal;
    }

    private static void RollBack(string directory, RestoreJournal journal)
    {
        foreach (string name in FileNames)
        {
            string path = Path.Combine(directory, name);
            string? value = journal.Originals[name];
            if (value == null) File.Delete(path);
            else SafeConfigStore.WriteAtomicBytes(path, Convert.FromBase64String(value));
        }
    }

    internal static bool RecoverPending(string directory)
    {
        string pending = Path.Combine(directory, PendingName);
        if (!File.Exists(pending)) return false;
        RollBack(directory, ReadJournal(pending));
        File.Move(pending, Path.Combine(directory, "restore-rolled-back-" + Guid.NewGuid().ToString("N") + ".json"));
        return true;
    }

    // The journal is durable before any replacement. Renaming it is the commit point.
    // Startup rolls back an interrupted transaction before loading or autosaving data.
    internal static string Restore(string directory, DataBackup backup, Action<int>? afterWrite = null)
    {
        Validate(backup);
        Directory.CreateDirectory(directory);
        RecoverPending(directory);
        var journal = new RestoreJournal();
        foreach (string name in FileNames)
        {
            string path = Path.Combine(directory, name);
            if (Directory.Exists(path)) throw new IOException("A data-file destination is a folder: " + path);
            journal.Originals[name] = File.Exists(path) ? Convert.ToBase64String(File.ReadAllBytes(path)) : null;
        }
        string pending = Path.Combine(directory, PendingName);
        SafeConfigStore.WriteAtomic(pending, JsonSerializer.Serialize(journal, JsonOptions));
        string originals = Path.Combine(directory, "restore-originals-" + Guid.NewGuid().ToString("N") + ".json");
        string[] replacements =
        {
            JsonSerializer.Serialize(backup.Configuration, JsonOptions),
            JsonSerializer.Serialize(backup.Preferences, JsonOptions),
            JsonSerializer.Serialize(backup.History, JsonOptions),
            JsonSerializer.Serialize(backup.Configuration!.Sites, JsonOptions)
        };
        try
        {
            for (int index = 0; index < FileNames.Length; index++)
            {
                SafeConfigStore.WriteAtomic(Path.Combine(directory, FileNames[index]), replacements[index]);
                afterWrite?.Invoke(index + 1);
            }
            File.Move(pending, originals);
            return originals;
        }
        catch (Exception saveError)
        {
            try { RecoverPending(directory); }
            catch (Exception recoveryError)
            {
                throw new IOException("Restore failed and recovery could not finish. Close Ping Watchdog and keep restore-pending.json in the data folder; startup will retry recovery. " +
                    saveError.Message + " " + recoveryError.Message, new AggregateException(saveError, recoveryError));
            }
            throw new IOException("Restore failed. The previous files were restored. " + saveError.Message, saveError);
        }
    }

    internal static void RunTests()
    {
        string directory = Path.Combine(Path.GetTempPath(), "PingWatchdog-backup-" + Guid.NewGuid().ToString("N"));
        string data = Path.Combine(directory, "data"), file = Path.Combine(directory, "complete.pingwatchbackup.json");
        Directory.CreateDirectory(data);
        void Check(bool condition) { if (!condition) throw new InvalidOperationException("Complete backup regression."); }
        var expiry = DateTimeOffset.UtcNow.AddHours(1);
        var backup = new DataBackup
        {
            CreatedUtc = DateTimeOffset.UtcNow, AppVersion = "test",
            Configuration = new WatchdogConfig
            {
                Sites = new() { new SiteDefinition {
                    Name = "Example", FolderPath = "Office", Hosts = new() { "192.0.2.1" },
                    Labels = new() { ["192.0.2.1"] = "Router" }, Categories = new() { ["192.0.2.1"] = "Network" },
                    HostDetails = new() { ["192.0.2.1"] = new HostOptions { SnoozedUntilUtc = expiry } } } },
                SiteFolders = new() { "Office" }
            },
            Preferences = new UserPreferences { Appearance = AppearanceMode.Dark, RestoreLastSiteOnStartup = true },
            History = new() { new StateEventRecord(DateTime.Now, "Example", "192.0.2.1", "Router", "DOWN", "Timeout") }
        };
        try
        {
            Write(file, backup, data);
            var loaded = Read(file);
            Check(loaded.Configuration!.Sites[0].HostDetails["192.0.2.1"].SnoozedUntilUtc == expiry &&
                loaded.Preferences!.Appearance == AppearanceMode.Dark && loaded.History!.Count == 1 &&
                loaded.Configuration.Sites[0].Labels["192.0.2.1"] == "Router" &&
                loaded.Configuration.Sites[0].Categories["192.0.2.1"] == "Network");
            string originals = Restore(data, loaded);
            Check(File.Exists(originals) && SafeConfigStore.Read(Path.Combine(data, FileNames[0])).Sites[0].Name == "Example");
            // Keep raw damaged bytes too; recovery must not depend on parsing the old setup.
            File.WriteAllBytes(Path.Combine(data, FileNames[0]), new byte[] { 0xff, 0x00, 0x80 });
            File.Delete(Path.Combine(data, FileNames[2]));
            var before = FileNames.ToDictionary(name => name,
                name => File.Exists(Path.Combine(data, name)) ? Convert.ToBase64String(File.ReadAllBytes(Path.Combine(data, name))) : null);
            bool failed = false;
            try { Restore(data, loaded, index => { if (index == 2) throw new IOException("Injected commit failure"); }); }
            catch (IOException) { failed = true; }
            Check(failed && !File.Exists(Path.Combine(data, PendingName)));
            foreach (var entry in before)
                Check(entry.Value == (File.Exists(Path.Combine(data, entry.Key)) ? Convert.ToBase64String(File.ReadAllBytes(Path.Combine(data, entry.Key))) : null));
            // Simulate process termination between file replacements, then the next startup.
            File.WriteAllText(Path.Combine(data, PendingName), JsonSerializer.Serialize(new RestoreJournal { Originals = before }));
            File.WriteAllText(Path.Combine(data, FileNames[0]), "partial replacement");
            Check(RecoverPending(data) && !RecoverPending(data));
            Check(Convert.ToBase64String(File.ReadAllBytes(Path.Combine(data, FileNames[0]))) == before[FileNames[0]]);
            var rejected = loaded;
            rejected.FormatVersion = 999;
            bool unsupported = false;
            try { Restore(data, rejected); } catch (InvalidDataException) { unsupported = true; }
            Check(unsupported && Convert.ToBase64String(File.ReadAllBytes(Path.Combine(data, FileNames[0]))) == before[FileNames[0]]);
            rejected.FormatVersion = 1;
            using (var locked = new FileStream(Path.Combine(data, FileNames[1]), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                bool lockedRefused = false;
                try { Restore(data, rejected); } catch (IOException) { lockedRefused = true; }
                Check(lockedRefused && !File.Exists(Path.Combine(data, PendingName)));
            }
            string validJson = JsonSerializer.Serialize(rejected, JsonOptions);
            foreach (string invalid in new[]
            {
                validJson.Replace("\"FormatVersion\": 1", "\"FormatVersion\": 999"),
                validJson.Replace("\"Preferences\": {", "\"IgnoredPreferences\": {"),
                validJson.Replace("\"History\": [", "\"IgnoredHistory\": [")
            })
            {
                File.WriteAllText(file, invalid);
                bool invalidRefused = false;
                try { Read(file); } catch (InvalidDataException) { invalidRefused = true; }
                Check(invalidRefused);
            }
            File.WriteAllText(file, "{broken");
            bool corrupt = false;
            try { Read(file); } catch (JsonException) { corrupt = true; }
            Check(corrupt);
            bool protectedPath = false;
            try { Write(Path.Combine(data, FileNames[0]), rejected, data); } catch (InvalidOperationException) { protectedPath = true; }
            Check(protectedPath);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
