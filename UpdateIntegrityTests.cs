using System.IO.Compression;
using System.Security.Cryptography;
using Velopack;
using Velopack.Exceptions;
using Velopack.Locators;
using Velopack.Logging;
using Velopack.Sources;

namespace PingWatchdog;

// Runs the pinned library's actual download/verification/cleanup path. No live feed,
// installed app, executable updater or user data is used.
internal static class UpdateIntegrityTests
{
    internal static async Task RunAsync()
    {
        foreach (var fault in new[] { DownloadFault.SameSizeCorruption, DownloadFault.Truncated, DownloadFault.Interrupted })
        {
            using var fixture = new UpdateIntegrityFixture { Fault = fault };
            var update = await fixture.Manager.CheckForUpdatesAsync().ConfigureAwait(false)
                ?? throw new InvalidOperationException("Integrity test feed was not discovered.");
            bool rejected = false;
            try { await fixture.Manager.DownloadUpdatesAsync(update).ConfigureAwait(false); }
            catch (ChecksumFailedException) when (fault != DownloadFault.Interrupted) { rejected = true; }
            catch (IOException) when (fault == DownloadFault.Interrupted) { rejected = true; }
            Check(rejected, fault + ": damaged download was accepted.");
            fixture.CheckNotStaged();

            fixture.Fault = DownloadFault.None;
            await fixture.Manager.DownloadUpdatesAsync(update).ConfigureAwait(false);
            Check(fixture.DownloadCalls == 2, fault + ": retry reused a failed download.");
            fixture.CheckValidStaged();
        }
        Console.WriteLine("Real Velopack integrity tests passed: same-size corruption, truncation, interrupted transfer, cleanup and valid retry.");
    }

    internal static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

internal enum DownloadFault { None, SameSizeCorruption, Truncated, Interrupted }

internal sealed class UpdateIntegrityFixture : IUpdateSource, IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PingWatchdog-Integrity-" + Guid.NewGuid().ToString("N"));
    private readonly byte[] _valid;
    private readonly VelopackAsset _asset;
    private readonly string _packages;
    internal UpdateManager Manager { get; }
    internal DownloadFault Fault { get; set; }
    internal int DownloadCalls { get; private set; }

    internal UpdateIntegrityFixture()
    {
        _packages = Path.Combine(_root, "packages");
        Directory.CreateDirectory(_packages);
        using (var data = new MemoryStream())
        {
            using (var zip = new ZipArchive(data, ZipArchiveMode.Create, leaveOpen: true))
            {
                using var writer = new StreamWriter(zip.CreateEntry("IntegrityProbe.nuspec").Open());
                writer.Write("<package><metadata><id>IntegrityProbe</id><version>2.0.0</version><authors>Test</authors><description>Isolated update test</description><channel>win</channel></metadata></package>");
            }
            _valid = data.ToArray();
        }
        _asset = new VelopackAsset
        {
            PackageId = "IntegrityProbe", Version = SemanticVersion.Parse("2.0.0"),
            Type = VelopackAssetType.Full, FileName = "IntegrityProbe-2.0.0-full.nupkg",
            Size = _valid.Length, SHA1 = Convert.ToHexString(SHA1.HashData(_valid)),
            SHA256 = Convert.ToHexString(SHA256.HashData(_valid))
        };
        // No Update.exe is included in the ZIP, so even the library's extraction
        // step cannot start or replace an executable. Never call Apply in these tests.
        var locator = new TestVelopackLocator("IntegrityProbe", "1.0.0", _packages,
            _root, _root, Path.Combine(_root, "Update.exe"), channel: "win");
        Manager = new UpdateManager(this, locator: locator);
    }

    public Task<VelopackAssetFeed> GetReleaseFeed(IVelopackLogger logger, string? appId,
        string channel, Guid? stagingId = null, VelopackAsset? latestLocalRelease = null) =>
        Task.FromResult(new VelopackAssetFeed { Assets = new[] { _asset } });

    public async Task DownloadReleaseEntry(IVelopackLogger logger, VelopackAsset releaseEntry,
        string localFile, Action<int> progress, CancellationToken cancelToken = default)
    {
        DownloadCalls++;
        var bytes = (byte[])_valid.Clone();
        if (Fault == DownloadFault.SameSizeCorruption) bytes[bytes.Length / 2] ^= 0x40;
        if (Fault is DownloadFault.Truncated or DownloadFault.Interrupted) bytes = bytes[..(bytes.Length / 2)];
        await File.WriteAllBytesAsync(localFile, bytes, cancelToken).ConfigureAwait(false);
        if (Fault == DownloadFault.Interrupted) throw new IOException("Injected interrupted package transfer.");
        progress(100);
    }

    internal void CheckNotStaged()
    {
        UpdateIntegrityTests.Check(!Directory.EnumerateFiles(_packages, "*.nupkg").Any() &&
            !Directory.EnumerateFiles(_packages, "*.partial").Any(),
            "Rejected update left an installable or partial package.");
        UpdateIntegrityTests.Check(Manager.UpdatePendingRestart is null,
            "Rejected update was reported as pending restart.");
    }

    internal void CheckValidStaged()
    {
        string package = Path.Combine(_packages, _asset.FileName);
        UpdateIntegrityTests.Check(File.Exists(package) && File.ReadAllBytes(package).SequenceEqual(_valid),
            "Retry did not stage the original verified package.");
        UpdateIntegrityTests.Check(Manager.UpdatePendingRestart?.Version?.Equals(_asset.Version) == true,
            "Valid retry was not reported as ready.");
        UpdateIntegrityTests.Check(!Directory.EnumerateFiles(_packages, "*.partial").Any(),
            "Valid retry left a partial package.");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
