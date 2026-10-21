namespace PingWatchdog;

internal static class DataDeletionStore
{
    private static void InspectTree(string root)
    {
        if (!Directory.Exists(root))
        {
            if (File.Exists(root)) throw new IOException("A data-folder path is a file: " + root);
            return;
        }
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            string directory = pending.Pop();
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Deletion refuses linked folders. Review the data folder first: " + directory);
            foreach (string child in Directory.EnumerateDirectories(directory)) pending.Push(child);
            foreach (string file in Directory.EnumerateFiles(directory))
                if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Deletion refuses linked files. Review the data folder first: " + file);
        }
    }

    internal static void Delete(StorageLayout layout)
    {
        foreach (string root in layout.DeletionRoots) InspectTree(root);
        SafeConfigStore.WriteAtomic(layout.DeletionMarker, "1");
        FinishPending(layout);
    }

    internal static bool FinishPending(StorageLayout layout)
    {
        if (!File.Exists(layout.DeletionMarker)) return false;
        if (File.ReadAllText(layout.DeletionMarker).Trim() != "1")
            throw new InvalidDataException("The saved-data deletion record is unsupported. Keep it and use a compatible version.");
        foreach (string root in layout.DeletionRoots) InspectTree(root);
        foreach (string root in layout.DeletionRoots)
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        // An empty configuration prevents the bundled demonstration hosts reappearing after deletion.
        SafeConfigStore.Write(Path.Combine(layout.DataDirectory, "autosave.pingwatch.json"),
            new WatchdogConfig { Sites = new() { new SiteDefinition { Name = "Default Site" } } });
        File.Delete(layout.DeletionMarker);
        return true;
    }

    internal static void RunTests()
    {
        string root = Path.Combine(Path.GetTempPath(), "PingWatchdog-delete-" + Guid.NewGuid().ToString("N"));
        string package = Path.Combine(root, "package"), roaming = Path.Combine(root, "roaming"), local = Path.Combine(root, "local");
        Directory.CreateDirectory(package); Directory.CreateDirectory(roaming);
        void Check(bool value) { if (!value) throw new InvalidOperationException("Saved-data deletion regression."); }
        try
        {
            File.WriteAllText(Path.Combine(package, "app.exe"), "retain installation");
            string external = Path.Combine(root, "export.pingwatchbackup.json");
            File.WriteAllText(external, "retain export");
            var layout = StoragePaths.Resolve(package, roaming, local);
            foreach (string folder in layout.DeletionRoots)
            {
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "private.txt"), "remove");
            }
            File.WriteAllText(Path.Combine(layout.DataDirectory, "restore-pending.json"), "old restore must not resurrect data");
            string lockedPath = Path.Combine(layout.DataDirectory, "locked.txt");
            File.WriteAllText(lockedPath, "remove on retry");
            using (var locked = new FileStream(lockedPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                bool failed = false;
                try { Delete(layout); } catch (IOException) { failed = true; }
                Check(failed && File.Exists(layout.DeletionMarker));
            }
            Check(FinishPending(layout) && !FinishPending(layout));
            Check(!File.Exists(lockedPath) && !File.Exists(Path.Combine(layout.DataDirectory, "restore-pending.json")));
            Check(SafeConfigStore.Read(Path.Combine(layout.DataDirectory, "autosave.pingwatch.json")).Sites.All(site => site.Hosts.Count == 0));
            Check(!Directory.Exists(layout.StartupDirectory) && !Directory.Exists(layout.BrandDirectory));
            Check(File.ReadAllText(external) == "retain export" && File.ReadAllText(Path.Combine(package, "app.exe")) == "retain installation");
            File.WriteAllText(Path.Combine(layout.DataDirectory, "keep.txt"), "inactive setup");
            SafeConfigStore.WriteAtomic(Path.Combine(package, StoragePaths.PortableMarker), "1");
            var portable = StoragePaths.Resolve(package, roaming, local);
            Directory.CreateDirectory(portable.DataDirectory);
            File.WriteAllText(Path.Combine(portable.DataDirectory, "private.txt"), "remove portable only");
            Delete(portable);
            Check(!File.Exists(Path.Combine(portable.DataDirectory, "private.txt")) &&
                File.ReadAllText(Path.Combine(layout.DataDirectory, "keep.txt")) == "inactive setup");
            Check(File.Exists(Path.Combine(package, StoragePaths.PortableMarker)));
            // A malformed/newer deletion request must not touch current files.
            File.WriteAllText(portable.DeletionMarker, "999");
            bool invalid = false;
            try { FinishPending(portable); } catch (InvalidDataException) { invalid = true; }
            Check(invalid && File.Exists(Path.Combine(portable.DataDirectory, "autosave.pingwatch.json")));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
