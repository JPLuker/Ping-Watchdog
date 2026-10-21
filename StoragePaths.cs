namespace PingWatchdog;

internal sealed record StorageLayout(string PackageDirectory, string DataDirectory, string StartupDirectory,
    string BrandDirectory, string DeletionMarker, bool Portable)
{
    internal string[] DeletionRoots => Portable ? new[] { DataDirectory }
        : new[] { DataDirectory, StartupDirectory, BrandDirectory };
}

internal static class StoragePaths
{
    internal const string PortableMarker = "PingWatchdog.portable";
    private static readonly Lazy<StorageLayout> Layout = new(() => Resolve(AppContext.BaseDirectory,
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)));
    internal static StorageLayout Current => Layout.Value;

    internal static string PackageRoot(string executableDirectory)
    {
        string directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(executableDirectory));
        string? parent = Path.GetDirectoryName(directory);
        string name = Path.GetFileName(directory);
        // Velopack replaces current/app-* during updates. Keep user data beside, never inside, that directory.
        if (parent != null && File.Exists(Path.Combine(parent, "Update.exe")) &&
            (name.Equals("current", StringComparison.OrdinalIgnoreCase) || name.StartsWith("app-", StringComparison.OrdinalIgnoreCase)))
            return parent;
        return directory;
    }

    internal static StorageLayout Resolve(string executableDirectory, string roaming, string local)
    {
        string package = PackageRoot(executableDirectory);
        string marker = Path.Combine(package, PortableMarker);
        bool portable = File.Exists(marker);
        if (portable && File.ReadAllText(marker).Trim() != "1")
            throw new InvalidDataException("The portable storage marker is unsupported. Keep the Data folder and use a compatible version.");
        string data = portable ? Path.Combine(package, "Data") : Path.Combine(roaming, "PingWatchdog");
        return new StorageLayout(package, data,
            portable ? Path.Combine(data, "Startup") : Path.Combine(local, "PingWatchdog", "Startup"),
            portable ? Path.Combine(data, "Brand") : Path.Combine(local, "PingWatchdog", "Brand"),
            Path.Combine(portable ? package : roaming, "PingWatchdog.delete-pending"), portable);
    }

    internal static void EnablePortable(StorageLayout source, DataBackup backup, bool allowed)
    {
        if (!allowed) throw new InvalidOperationException("Folder-local storage is available in portable copies only.");
        if (source.Portable) throw new InvalidOperationException("This copy already uses portable storage.");
        string marker = Path.Combine(source.PackageDirectory, PortableMarker);
        string destination = Path.Combine(source.PackageDirectory, "Data");
        if (File.Exists(marker) || Directory.Exists(destination) || File.Exists(destination))
            throw new IOException("A portable marker or Data folder already exists. Keep it intact and review it before switching storage.");
        string stage = Path.Combine(source.PackageDirectory, "Data.pending-" + Guid.NewGuid().ToString("N"));
        try
        {
            DataBackupStore.Restore(stage, backup);
            Directory.Move(stage, destination);
            // Activate only after the complete setup has been committed to the destination.
            SafeConfigStore.WriteAtomic(marker, "1");
        }
        finally
        {
            if (Directory.Exists(stage)) Directory.Delete(stage, recursive: true);
        }
        // The original AppData files are deliberately retained; no merge or silent deletion.
    }

    internal static void RunTests()
    {
        string root = Path.Combine(Path.GetTempPath(), "PingWatchdog-storage-" + Guid.NewGuid().ToString("N"));
        string package = Path.Combine(root, "package"), roaming = Path.Combine(root, "roaming"), local = Path.Combine(root, "local");
        Directory.CreateDirectory(package); Directory.CreateDirectory(roaming);
        void Check(bool value) { if (!value) throw new InvalidOperationException("Storage routing regression."); }
        try
        {
            var standard = Resolve(package, roaming, local);
            Check(!standard.Portable && standard.DataDirectory == Path.Combine(roaming, "PingWatchdog"));
            var backup = new DataBackup { CreatedUtc = DateTimeOffset.UtcNow,
                Configuration = new WatchdogConfig { Sites = new() { new SiteDefinition { Name = "Office", Hosts = new() { "192.0.2.1" } } } },
                Preferences = new UserPreferences { Appearance = AppearanceMode.Dark }, History = new() };
            bool refused = false;
            try { EnablePortable(standard, backup, false); } catch (InvalidOperationException) { refused = true; }
            Check(refused && !Directory.Exists(Path.Combine(package, "Data")));
            Directory.CreateDirectory(standard.DataDirectory);
            File.WriteAllText(Path.Combine(standard.DataDirectory, "original.txt"), "keep");
            EnablePortable(standard, backup, true);
            var portable = Resolve(package, roaming, local);
            Check(portable.Portable && portable.StartupDirectory == Path.Combine(package, "Data", "Startup"));
            Check(SafeConfigStore.Read(Path.Combine(portable.DataDirectory, "autosave.pingwatch.json")).Sites[0].Hosts[0] == "192.0.2.1");
            Check(File.ReadAllText(Path.Combine(standard.DataDirectory, "original.txt")) == "keep");
            Directory.CreateDirectory(Path.Combine(package, "current"));
            File.WriteAllText(Path.Combine(package, "Update.exe"), "test fixture");
            Check(Resolve(Path.Combine(package, "current"), roaming, local).DataDirectory == portable.DataDirectory);
            File.WriteAllText(Path.Combine(package, PortableMarker), "999");
            bool future = false;
            try { Resolve(package, roaming, local); } catch (InvalidDataException) { future = true; }
            Check(future);
            File.Delete(Path.Combine(package, PortableMarker));
            bool collision = false;
            try { EnablePortable(standard, backup, true); } catch (IOException) { collision = true; }
            Check(collision && !File.Exists(Path.Combine(package, PortableMarker)));
            // A read-only/blocked marker cannot activate a partial migration.
            string blocked = Path.Combine(root, "blocked");
            Directory.CreateDirectory(blocked);
            Directory.CreateDirectory(Path.Combine(blocked, PortableMarker));
            bool activationFailed = false;
            try { EnablePortable(Resolve(blocked, roaming, local), backup, true); }
            catch (IOException) { activationFailed = true; }
            catch (UnauthorizedAccessException) { activationFailed = true; }
            Check(activationFailed && !File.Exists(Path.Combine(blocked, PortableMarker)));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
