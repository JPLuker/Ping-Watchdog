namespace PingWatchdog.Shared;

// No UI-framework types: both native applications compile this file.
internal static class Presentation
{
    public const string Window = "#0A0E14", Header = "#0C121A", Navigation = "#0E141C",
        Card = "#121922", Input = "#0B1118", Border = "#27313D", Text = "#EAF0F6",
        Muted = "#8B99A9", Accent = "#39D9EE", Online = "#48CF89", Suspect = "#F4BE48",
        Offline = "#FF6870", Unknown = "#8B99A9", WallboardBackground = "#05090E",
        WallboardPanel = "#090F16", WallboardCard = "#0E151D",
        WallboardOnline = "#4DD38D", WallboardSuspect = "#F4BD44", WallboardOffline = "#FF626D", WallboardUnknown = "#667A8D";
    public const int HeaderHeight = 74, FooterHeight = 28, SidebarWidth = 218,
        TableHeaderHeight = 38, TableRowHeight = 34, VisibleTopologyHosts = 12,
        CompactTableHeaderHeight = 32, CompactTableRowHeight = 28,
        WallboardHeaderHeight = 116, WallboardStatsHeight = 82, WallboardFooterHeight = 34;
    public static readonly string[] SettingsPages = { "General", "Monitoring", "History", "Updates" };
    public static readonly string[] HistoryRanges = { "Last 24 hours", "Last 7 days", "Last 30 days", "All time" };
    public static readonly TableColumn[] HostColumns = {
        new("Site", "Site", 145), new("Host", "Host", 150, 32), new("Label", "Label", 140, 25),
        new("Status", "Status", 95), new("Latency", "Latency", 85), new("Failures", "Failures", 75),
        new("LastReply", "Last Reply", 150), new("Outage", "Outage Since", 150)
    };
    public static readonly TableColumn[] HistoryColumns = {
        new("Timestamp", "Timestamp", 160), new("Kind", "Event", 105), new("Site", "Site", 155),
        new("Host", "Host", 0, 34), new("Details", "Details", 0, 66)
    };
    public static readonly TableColumn[] WallboardHostColumns = {
        new("Site", "Site", 78), new("Host", "Host", 112), new("Label", "Label", 82),
        new("Status", "Status", 67), new("Latency", "ms", 48)
    };
    public static int HeaderForWidth(double width) => width < 1080 ? 70 : HeaderHeight;
    public static int Sidebar(double width) => width < 960 ? 176 : width < 1080 ? 196 : SidebarWidth;
    public static UiPoint[] SitePoints(int count, double x, double y, double width, double height, int coreRadius)
    {
        double radiusX = Math.Max(coreRadius + 80, width * .37), radiusY = Math.Max(coreRadius + 65, height * .34);
        return Enumerable.Range(0, count).Select(i => {
            double angle = -Math.PI / 2 + Math.PI * 2 * i / Math.Max(1, count);
            return new UiPoint(x + width / 2 + (int)(Math.Cos(angle) * radiusX), y + height / 2 + (int)(Math.Sin(angle) * radiusY));
        }).ToArray();
    }
    public static double HostAngle(int index, int visible)
    {
        bool outer = visible > 7 && index >= 6;
        int ringIndex = outer ? index - 6 : index, ringCount = outer ? visible - 6 : Math.Min(visible, 6);
        return -Math.PI / 2 + Math.PI * 2 * ringIndex / Math.Max(1, ringCount) + (outer ? Math.PI / Math.Max(1, ringCount) : 0);
    }
    public static UiPoint HostPoint(int index, int visible, UiPoint center, double baseOrbit = 68)
    {
        bool outer = visible > 7 && index >= 6;
        int ringIndex = outer ? index - 6 : index, ringCount = outer ? visible - 6 : Math.Min(visible, 6);
        double angle = HostAngle(index, visible);
        double radius = baseOrbit + (outer ? 44 : 0);
        return new UiPoint(center.X + (int)(Math.Cos(angle) * radius), center.Y + (int)(Math.Sin(angle) * radius));
    }
}
internal sealed record TableColumn(string Key, string Header, int Width, double Weight = 0);
internal readonly record struct UiPoint(double X, double Y);
