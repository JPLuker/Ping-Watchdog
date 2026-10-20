using System.Text;
using System.Text.Json;

namespace PingWatchdog;

/// <summary>Protect configuration originals before parsing, applying or replacing them.</summary>
internal static class SafeConfigStore
{
    internal const int CurrentVersion = 1;

    internal static void EnsureSupported(int version)
    {
        if (version > CurrentVersion)
            throw new InvalidDataException("This configuration was saved by a newer version. Update Ping Watchdog to open it.");
        if (version < 1)
            throw new InvalidDataException("The configuration format version is invalid.");
    }

    internal static WatchdogConfig Read(string path)
    {
        string json = File.ReadAllText(path);
        Validate(json);
        return JsonSerializer.Deserialize<WatchdogConfig>(json)
            ?? throw new InvalidDataException("The configuration is empty.");
    }

    private static void Validate(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("The configuration must be a JSON object.");
        int version = CurrentVersion; // Legacy files omit the format field.
        if (document.RootElement.TryGetProperty("FormatVersion", out var format) &&
            (format.ValueKind != JsonValueKind.Number || !format.TryGetInt32(out version)))
            throw new InvalidDataException("The configuration format version is invalid.");
        EnsureSupported(version);
    }

    internal static void Write(string path, WatchdogConfig config)
    {
        EnsureSupported(config.FormatVersion);
        // Never silently replace newer or damaged data, including in a Save dialog.
        if (File.Exists(path)) Validate(File.ReadAllText(path));
        WriteAtomic(path, JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));
    }

    internal static void WriteAtomic(string path, string text)
    {
        string fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        string temporary = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                writer.Write(text);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(fullPath))
            {
                // Copy rotation leaves every previous backup intact if a copy fails.
                if (File.Exists(fullPath + ".backup-2"))
                    File.Copy(fullPath + ".backup-2", fullPath + ".backup-3", overwrite: true);
                if (File.Exists(fullPath + ".backup-1"))
                    File.Copy(fullPath + ".backup-1", fullPath + ".backup-2", overwrite: true);
                File.Replace(temporary, fullPath, fullPath + ".backup-1", ignoreMetadataErrors: true);
            }
            else File.Move(temporary, fullPath);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    internal static void RunTests()
    {
        string directory = Path.Combine(Path.GetTempPath(), "PingWatchdog-storage-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "config.json");
        void Check(bool condition) { if (!condition) throw new InvalidOperationException("Configuration safety regression."); }
        try
        {
            var config = new WatchdogConfig { PingIntervalSeconds = 2 };
            Write(path, config);
            Check(Read(path).PingIntervalSeconds == 2);
            for (int i = 3; i <= 6; i++) { config.PingIntervalSeconds = i; Write(path, config); }
            Check(Read(path).PingIntervalSeconds == 6);
            Check(Read(path + ".backup-1").PingIntervalSeconds == 5);
            Check(Read(path + ".backup-2").PingIntervalSeconds == 4);
            Check(Read(path + ".backup-3").PingIntervalSeconds == 3);
            Check(Directory.GetFiles(directory, "*.backup-*").Length == 3);
            string original = File.ReadAllText(path);
            using (var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                bool refused = false;
                try { WriteAtomic(path, "replacement"); } catch (IOException) { refused = true; }
                Check(refused);
            }
            Check(File.ReadAllText(path) == original);
            string future = "{\"FormatVersion\":999,\"NewField\":\"keep this\"}";
            File.WriteAllText(path, future);
            bool readRefused = false, writeRefused = false;
            try { Read(path); } catch (InvalidDataException) { readRefused = true; }
            try { Write(path, config); } catch (InvalidDataException) { writeRefused = true; }
            Check(readRefused && writeRefused && File.ReadAllText(path) == future);
            File.WriteAllText(path, "{broken");
            bool damagedRefused = false;
            try { Write(path, config); } catch (JsonException) { damagedRefused = true; }
            Check(damagedRefused && File.ReadAllText(path) == "{broken");
            File.WriteAllText(path, "{\"Sites\":[]}");
            Check(Read(path).FormatVersion == CurrentVersion);
            Check(Directory.GetFiles(directory, "*.tmp").Length == 0);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}

public sealed partial class MainForm
{
    private bool _configWriteBlocked;
    private bool _storageWarningShown;
    private string? _storageWarning;

    private void ReportStorageProblem(string message, bool protectOriginal = false)
    {
        if (_configWriteBlocked && !protectOriginal && _storageWarning != null) return;
        _configWriteBlocked |= protectOriginal;
        _storageWarning = message;
        ShowStorageWarning();
    }

    private void ShowStorageWarning()
    {
        if (_storageWarningShown || _storageWarning == null || !IsHandleCreated || !Visible) return;
        _storageWarningShown = true;
        MessageBox.Show(this, _storageWarning, "Saved data", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }
}
