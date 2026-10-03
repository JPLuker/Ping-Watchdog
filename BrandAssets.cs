namespace PingWatchdog;

/// <summary>Raster assets rendered from the supplied SVG, embedded for single-file publishing.</summary>
internal static class BrandAssets
{
    // A new filename avoids the Shell reusing the old heartbeat icon's cached path.
    internal static string ShellIconPath => Path.Combine(AppContext.BaseDirectory, "Assets", "Watchdog-dog-v1.ico");
    internal static string NotificationIconPath => Path.Combine(AppContext.BaseDirectory, "Assets", "Watchdog-dog-v1.png");

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
