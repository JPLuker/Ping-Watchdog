using System.Text.Json;
namespace PingWatchdog;
internal static class HostManagerTests
{
    internal static void Run()
    {
        void Check(bool condition, string name) { if (!condition) throw new InvalidOperationException("Host Manager: " + name); }
        var rows = HostImport.Parse("Address,Label,Group\n10.1.1.2,\"Accounting, Copier\",Printers\nserver.example\n10.1.999.2\n10.1.1.10-12\n10.2.0.0/30");
        Check(rows.Count == 8, "all input retained and ranges expanded");
        Check(rows[0].Label == "Accounting, Copier" && rows[0].Group == "Printers", "quoted CSV");
        Check(rows[2].Error.Length > 0, "invalid IPv4 retained");
        Check(rows[^2].Address == "10.2.0.1" && rows[^1].Address == "10.2.0.2", "CIDR usable hosts");
        Check(HostImport.Parse("10.1.1.1,Printer01,Printers")[0].Label == "Printer01", "single-word CSV labels");
        Check(HostImport.Parse("10.1.1.1,10.1.1.2").Count == 2, "comma-separated addresses");
        Check(HostImport.Parse("10.1.1.1\tOffice Printer\tPrinters")[0].Label == "Office Printer", "spreadsheet input");
        Check(HostImport.Parse("10.0.0.0/8")[0].Error.Length > 0, "oversized CIDR bounded");
        Check(HostImport.Parse("10.0.0.1-300")[0].Error.Length > 0, "bad short range");
        Check(HostImport.Parse("10.0.0.0/31").Count == 2 && HostImport.Parse("10.0.0.1/32").Count == 1, "point-to-point and host routes");
        Check(HostImport.Valid("2001:db8::1") && HostImport.Valid("server-01"), "IPv6 and hostname");
        Check(!HostImport.Valid("-bad") && !HostImport.Valid("a b") && !HostImport.Valid("999.1.1.1"), "invalid addresses rejected");
        Check(HostImport.Normalize("SERVER.EXAMPLE.") == "server.example", "hostname duplicate normalization");
        var config = new SiteDefinition { Name = "Office", Hosts = new() { "10.1.1.1" } };
        var options = new HostOptions { Tags = "critical", Enabled = false, IntervalSeconds = 10, TimeoutMs = 2000, FailureThreshold = 4 };
        config.HostDetails["10.1.1.1"] = options;
        var copy = JsonSerializer.Deserialize<SiteDefinition>(JsonSerializer.Serialize(config))!;
        Check(copy.HostDetails["10.1.1.1"].Id == options.Id && !copy.HostDetails["10.1.1.1"].Enabled, "stable identity and options round-trip");
        var legacy = JsonSerializer.Deserialize<SiteDefinition>("{\"Name\":\"Legacy\",\"Hosts\":[\"localhost\"]}")!;
        Check(legacy.HostDetails.Count == 0, "legacy config compatible");
    }
}
