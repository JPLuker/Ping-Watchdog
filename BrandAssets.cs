namespace PingWatchdog;

/// <summary>Raster assets rendered from the supplied SVG, embedded for single-file publishing.</summary>
internal static class BrandAssets
{
    // A new filename avoids the Shell reusing the old heartbeat icon's cached path.
    private static string ShellAssetsDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PingWatchdog", "Brand");
    internal static string ShellIconPath => Path.Combine(ShellAssetsDirectory, "Watchdog-dog-v1.ico");
    internal static string NotificationIconPath => Path.Combine(ShellAssetsDirectory, "Watchdog-dog-v1.png");

    internal static void EnsureShellAssets()
    {
        // Single-file publishing extracts content into a temporary .NET folder.
        // Shell shortcuts need artwork that survives updates and temporary-file cleanup.
        Directory.CreateDirectory(ShellAssetsDirectory);
        Export("PingWatchdog.Brand.Icon", ShellIconPath);
        Export("PingWatchdog.Brand.Logo", NotificationIconPath);
    }

    private static void Export(string resourceName, string path)
    {
        if (File.Exists(path))
            return;
        using var stream = typeof(BrandAssets).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException("Embedded Watchdog artwork is missing.");
        using var file = File.Create(path);
        stream.CopyTo(file);
    }

    internal static Bitmap LoadLogo()
    {
        using var stream = typeof(BrandAssets).Assembly.GetManifestResourceStream("PingWatchdog.Brand.Logo")
            ?? throw new InvalidOperationException("Embedded Watchdog logo is missing.");
        using var image = Image.FromStream(stream);
        return new Bitmap(image);
    }

    internal static Icon LoadIcon()
    {
        using var stream = typeof(BrandAssets).Assembly.GetManifestResourceStream("PingWatchdog.Brand.Icon")
            ?? throw new InvalidOperationException("Embedded Watchdog icon is missing.");
        using var icon = new Icon(stream, 32, 32);
        return (Icon)icon.Clone();
    }
}
