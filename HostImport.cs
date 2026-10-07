using System.Net;
using System.Net.Sockets;
using System.Text;

namespace PingWatchdog;

internal sealed class HostOptions
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Tags { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public DateTimeOffset? SnoozedUntilUtc { get; set; }
    public bool IsSnoozed(DateTimeOffset now) => SnoozedUntilUtc is { } until && until > now;
    public int IntervalSeconds { get; set; }
    public int TimeoutMs { get; set; }
    public int FailureThreshold { get; set; }
    public HostOptions Copy() => (HostOptions)MemberwiseClone();
}

internal sealed record ImportedHost(string Address, string Label, string Group, string Error);

internal static class HostImport
{
    internal const int Limit = 4096;
    internal static string Normalize(string address) => IPAddress.TryParse(address, out var ip)
        ? ip.ToString() : address.Trim().TrimEnd('.').ToLowerInvariant();

    internal static bool Valid(string value)
    {
        if (IPAddress.TryParse(value, out _)) return true;
        if (value.Length is < 1 or > 253 || value.All(c => char.IsDigit(c) || c == '.')) return false;
        return value.TrimEnd('.').Split('.').All(p => p.Length is > 0 and <= 63 &&
            char.IsAsciiLetterOrDigit(p[0]) && char.IsAsciiLetterOrDigit(p[^1]) &&
            p.All(c => char.IsAsciiLetterOrDigit(c) || c == '-'));
    }

    internal static List<ImportedHost> Parse(string text)
    {
        var result = new List<ImportedHost>();
        foreach (var line in text.Replace("\r", "").Split('\n').Where(l => !string.IsNullOrWhiteSpace(l)))
        {
            var cells = Split(line, line.Contains('\t') ? '\t' : ',');
            if (cells[0].Equals("Address", StringComparison.OrdinalIgnoreCase) ||
                cells[0].Equals("IP", StringComparison.OrdinalIgnoreCase) ||
                cells[0].Equals("IP / Hostname", StringComparison.OrdinalIgnoreCase)) continue;
            // A comma-separated list of valid addresses is different from Address, Label, Group.
            bool list = cells.Count > 1 && cells.All(Valid) &&
                (cells.All(c => IPAddress.TryParse(c, out _)) || cells.All(c => !IPAddress.TryParse(c, out _)));
            foreach (var address in list ? cells : new List<string> { cells[0] })
            {
                string label = list || cells.Count < 2 ? "" : cells[1];
                string group = list || cells.Count < 3 ? "" : cells[2];
                try
                {
                    var expanded = Expand(address).ToList();
                    if (result.Count + expanded.Count > Limit) throw new FormatException($"Import limit is {Limit} hosts.");
                    foreach (var host in expanded) result.Add(new(host, label, group, ""));
                }
                catch (FormatException ex) { result.Add(new(address, label, group, ex.Message)); }
                if (result.Count >= Limit) return result;
            }
        }
        return result;
    }

    private static IEnumerable<string> Expand(string value)
    {
        if (value.Contains('/'))
        {
            var parts = value.Split('/');
            if (parts.Length != 2 || !IPAddress.TryParse(parts[0], out var ip) || ip.AddressFamily != AddressFamily.InterNetwork ||
                !int.TryParse(parts[1], out int prefix) || prefix is < 0 or > 32)
                throw new FormatException("Enter an IPv4 CIDR range.");
            ulong count = 1UL << (32 - prefix);
            if (count > Limit) throw new FormatException($"Range exceeds {Limit} hosts; use a smaller subnet.");
            var bytes = ip.GetAddressBytes();
            uint number = ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
            uint start = number & (prefix == 0 ? 0 : uint.MaxValue << (32 - prefix));
            ulong first = prefix < 31 ? 1UL : 0;
            ulong end = prefix < 31 ? count - 1 : count;
            for (ulong i = first; i < end; i++) yield return FromNumber(start + (uint)i);
            yield break;
        }
        int dash = value.LastIndexOf('-');
        if (dash > 0 && IPAddress.TryParse(value[..dash], out var from) && from.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = from.GetAddressBytes();
            if (!int.TryParse(value[(dash + 1)..], out int last) || last < bytes[3] || last > 255)
                throw new FormatException("Use a range such as 192.168.1.10-25.");
            for (int i = bytes[3]; i <= last; i++) yield return $"{bytes[0]}.{bytes[1]}.{bytes[2]}.{i}";
            yield break;
        }
        if (!Valid(value)) throw new FormatException("Invalid IP address or hostname.");
        yield return Normalize(value);
    }
    private static string FromNumber(uint n) => $"{n >> 24}.{(n >> 16) & 255}.{(n >> 8) & 255}.{n & 255}";
    private static List<string> Split(string line, char separator)
    {
        var cells = new List<string>(); var cell = new StringBuilder(); bool quoted = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"')
            {
                if (quoted && i + 1 < line.Length && line[i + 1] == '"') { cell.Append('"'); i++; }
                else quoted = !quoted;
            }
            else if (c == separator && !quoted) { cells.Add(cell.ToString().Trim()); cell.Clear(); }
            else cell.Append(c);
        }
        cells.Add(cell.ToString().Trim());
        return cells;
    }
}
