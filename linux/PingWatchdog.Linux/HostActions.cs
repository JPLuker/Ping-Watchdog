// Host actions shared by the List, the Map and the Wallboard: one menu, one set of behaviors.
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace PingWatchdog.Linux;

internal sealed class HostActions
{
    private readonly WatchdogEngine _engine;
    private readonly Window _owner;
    private readonly Action<string, bool> _report;
    private readonly Action<HostSnapshot>? _showInList;

    // report(message, offerUndo)
    public HostActions(WatchdogEngine engine, Window owner, Action<string, bool> report, Action<HostSnapshot>? showInList)
    {
        _engine = engine;
        _owner = owner;
        _report = report;
        _showInList = showInList;
    }

    internal List<Control> LastMenuItems { get; private set; } = new();
    internal ContextMenu? LastMenu { get; private set; }

    internal static string MenuText(string text) => text.Replace("_", "__");

    public void ShowMenu(Control anchor, Point point, IReadOnlyList<HostSnapshot> hosts, bool fromList)
    {
        if (hosts.Count == 0) return;
        var items = hosts.Count == 1 ? SingleItems(hosts[0], fromList) : BulkItems(hosts);
        LastMenuItems = items;
        var menu = new ContextMenu
        {
            ItemsSource = items,
            Placement = PlacementMode.AnchorAndGravity,
            PlacementTarget = anchor,
            PlacementRect = new Rect(point, new Size(1, 1)),
            PlacementAnchor = Avalonia.Controls.Primitives.PopupPositioning.PopupAnchor.TopLeft,
            PlacementGravity = Avalonia.Controls.Primitives.PopupPositioning.PopupGravity.BottomRight
        };
        LastMenu = menu;
        menu.Open(anchor);
    }

    private List<Control> SingleItems(HostSnapshot host, bool fromList)
    {
        var keys = new[] { host.Key };
        string shown = host.DisplayName;
        var items = new List<Control>
        {
            new MenuItem { Header = MenuText(string.IsNullOrWhiteSpace(host.Label) ? host.Address : host.Label + " • " + host.Address), IsEnabled = false }
        };
        if (!fromList && _showInList is not null) items.Add(Ui.MenuItem("Edit in host list", () => _showInList(host)));
        items.Add(Ui.MenuItem("Copy address", async () => _report(await ClipboardText.SetAsync(_owner, host.Address) ? "Copied " + host.Address : "Couldn't reach the clipboard", false)));
        items.Add(Ui.MenuItem("Edit host…", () => _ = EditAsync(host), gesture: fromList ? "F2" : null));
        if (!string.IsNullOrWhiteSpace(host.Label))
            items.Add(Ui.MenuItem("Clear label", () => Run(_engine.SetLabel(host.Site, host.Address, ""), $"Cleared the name for {host.Address}")));
        items.Add(Ui.MenuItem("Set group…", () => _ = SetGroupAsync(keys, host.Group)));
        if (!string.IsNullOrWhiteSpace(host.Group))
            items.Add(Ui.MenuItem("Clear group", () => Run(_engine.SetGroup(keys, ""), $"Cleared the group for {shown}")));
        items.Add(Ui.MenuItem("Ping in terminal", () => { if (!PingInTerminal(host.Address)) _report("No terminal app was found", false); }));
        items.Add(new Separator());
        if (host.Snoozed)
            items.Add(new MenuItem { Header = "Snoozed until " + DisplayTime.Clock(host.Options.SnoozedUntilUtc!.Value.LocalDateTime), IsEnabled = false });
        items.Add(Ui.MenuItem("Snooze host for 1 hour", () => Run(_engine.SetSnooze(keys, DateTimeOffset.UtcNow.AddHours(1)), $"Snoozed {shown} for 1 hour")));
        if (host.Snoozed)
            items.Add(Ui.MenuItem("Resume monitoring now", () => Run(_engine.SetSnooze(keys, null), $"Resumed {shown}")));
        items.Add(Ui.MenuItem(host.Options.Priority ? "Remove priority" : "Mark as priority", () =>
            Run(_engine.SetPriority(keys, !host.Options.Priority), host.Options.Priority ? $"Removed priority from {shown}" : $"Marked {shown} as priority")));
        items.Add(Ui.MenuItem(host.Options.Enabled ? "Disable monitoring" : "Enable monitoring", () =>
            Run(_engine.SetEnabled(keys, !host.Options.Enabled), (host.Options.Enabled ? "Disabled " : "Enabled ") + shown)));
        if (fromList)
        {
            items.Add(Ui.MenuItem("Move to site…", () => _ = MoveAsync(new[] { host })));
            items.Add(Ui.MenuItem("Monitoring…", () => _ = MonitoringAsync(new[] { host })));
            if (host.Temporary) items.Add(Ui.MenuItem("Keep in a site…", () => _ = KeepAsync(new[] { host })));
        }
        items.Add(new Separator());
        items.Add(Ui.MenuItem("Remove host…", () => _ = RemoveAsync(new[] { host }), gesture: fromList ? "Delete" : null));
        return items;
    }

    private List<Control> BulkItems(IReadOnlyList<HostSnapshot> hosts)
    {
        var keys = hosts.Select(h => h.Key).ToList();
        bool allPriority = hosts.All(h => h.Options.Priority);
        bool allEnabled = hosts.All(h => h.Options.Enabled);
        string count = WatchdogEngine.Hosts(hosts.Count);
        var items = new List<Control>
        {
            new MenuItem { Header = count + " selected", IsEnabled = false },
            Ui.MenuItem(allPriority ? "Remove priority" : "Mark as priority", () =>
                Run(_engine.SetPriority(keys, !allPriority), (allPriority ? "Removed priority from " : "Marked priority on ") + count)),
            Ui.MenuItem(allEnabled ? "Disable monitoring" : "Enable monitoring", () =>
                Run(_engine.SetEnabled(keys, !allEnabled), (allEnabled ? "Disabled " : "Enabled ") + count)),
            Ui.MenuItem("Snooze hosts for 1 hour", () => Run(_engine.SetSnooze(keys, DateTimeOffset.UtcNow.AddHours(1)), $"Snoozed {count} for 1 hour")),
            Ui.MenuItem("Move to site…", () => _ = MoveAsync(hosts)),
            Ui.MenuItem("Set group…", () => _ = SetGroupAsync(keys, hosts.Select(h => h.Group).Distinct().Count() == 1 ? hosts[0].Group : "")),
            Ui.MenuItem("Monitoring…", () => _ = MonitoringAsync(hosts)),
        };
        if (hosts.Any(h => h.Temporary)) items.Add(Ui.MenuItem("Keep in a site…", () => _ = KeepAsync(hosts.Where(h => h.Temporary).ToList())));
        items.Add(new Separator());
        items.Add(Ui.MenuItem("Remove…", () => _ = RemoveAsync(hosts), gesture: "Delete"));
        return items;
    }

    private void Run(string? error, string success)
    {
        if (error is not null) _ = Dialogs.Message(_owner, "Change not saved", error);
        else _report(success, true);
    }

    public async Task EditAsync(HostSnapshot host)
    {
        string? message = null;
        var result = await Dialogs.EditHost(_owner, host, (address, label) =>
        {
            string? error = _engine.EditHost(host.Key, address, label);
            if (error is null)
            {
                bool moved = !address.Equals(host.Address, StringComparison.OrdinalIgnoreCase);
                message = moved ? $"Changed {host.Address} to {HostImport.Normalize(address)}"
                    : label.Length == 0 ? $"Cleared the name for {host.Address}" : $"Named {host.Address} “{label}”";
            }
            return error;
        });
        if (result is not null && message is not null) _report(message, true);
    }

    private async Task SetGroupAsync(IReadOnlyCollection<HostKey> keys, string current)
    {
        var value = await new PromptDialog("Set group", "Group", current, "Save", null).ShowDialog<string?>(_owner);
        if (value is null) return;
        Run(_engine.SetGroup(keys, value), $"Set group “{value}” on {WatchdogEngine.Hosts(keys.Count)}");
    }

    private IEnumerable<string> SavedSites() => _engine.Snapshot().Sites.Where(s => !s.Temporary).Select(s => s.Name);

    private async Task MoveAsync(IReadOnlyList<HostSnapshot> hosts)
    {
        var choice = await Dialogs.ChooseSite(_owner, "Move to site", "Move", SavedSites(), hosts.Count == 1 ? hosts[0].Site : null);
        if (choice is not { } c) return;
        Run(_engine.MoveHosts(hosts.Select(h => h.Key).ToList(), c.Site, c.Create), $"Moved {WatchdogEngine.Hosts(hosts.Count)} to {c.Site}");
    }

    private async Task KeepAsync(IReadOnlyList<HostSnapshot> hosts)
    {
        var choice = await Dialogs.ChooseSite(_owner, "Keep in a site", "Keep", SavedSites());
        if (choice is not { } c) return;
        Run(_engine.KeepTemporaryHosts(hosts.Select(h => h.Key).ToList(), c.Site, c.Create), $"Kept {WatchdogEngine.Hosts(hosts.Count)} in {c.Site}");
    }

    private async Task MonitoringAsync(IReadOnlyList<HostSnapshot> hosts)
    {
        string subject = hosts.Count == 1 ? hosts[0].DisplayName : WatchdogEngine.Hosts(hosts.Count);
        var result = await Dialogs.Monitoring(_owner, subject, hosts.Count == 1 ? hosts[0].Options : null, _engine.Snapshot().Settings);
        if (result is not { } r) return;
        Run(_engine.SetMonitoring(hosts.Select(h => h.Key).ToList(), r.Interval, r.Timeout, r.Failures), "Changed monitoring for " + subject);
    }

    public async Task RemoveAsync(IReadOnlyList<HostSnapshot> hosts)
    {
        string text = hosts.Count == 1 ? $"Remove {hosts[0].Address} from {hosts[0].Site}?" : $"Remove {WatchdogEngine.Hosts(hosts.Count)}?";
        if (!await Dialogs.Confirm(_owner, hosts.Count == 1 ? "Remove host" : "Remove hosts", text, "Remove", destructive: true)) return;
        Run(_engine.RemoveHosts(hosts.Select(h => h.Key).ToList()), hosts.Count == 1 ? "Removed " + hosts[0].DisplayName : "Removed " + WatchdogEngine.Hosts(hosts.Count));
    }

    // Opens a continuous ping in the desktop's terminal. Arguments are passed directly, never through a shell.
    internal static bool PingInTerminal(string address)
    {
        if (!HostImport.Valid(address)) return false;
        var terminals = new (string Tool, string[] Prefix)[]
        {
            ("x-terminal-emulator", new[] { "-e" }),
            ("io.elementary.terminal", new[] { "-x" }),
            ("gnome-terminal", new[] { "--" }),
            ("kgx", new[] { "--" }),
            ("ptyxis", new[] { "--" }),
            ("konsole", new[] { "-e" }),
            ("xfce4-terminal", new[] { "-x" }),
            ("tilix", new[] { "-e" }),
            ("alacritty", new[] { "-e" }),
            ("kitty", Array.Empty<string>()),
            ("xterm", new[] { "-e" }),
        };
        foreach (var (tool, prefix) in terminals)
        {
            if (!SupportInfo.ToolExists(tool)) continue;
            try
            {
                var start = new ProcessStartInfo(tool) { UseShellExecute = false };
                foreach (var arg in prefix) start.ArgumentList.Add(arg);
                start.ArgumentList.Add("ping");
                start.ArgumentList.Add(address);
                using var process = Process.Start(start);
                if (process is not null) return true;
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
        }
        return false;
    }
}
