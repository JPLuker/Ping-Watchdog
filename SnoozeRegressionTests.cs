namespace PingWatchdog;

public sealed partial class MainForm
{
    internal static void RunSnoozeRegressionTests()
    {
        using var form = new MainForm(false, true, false);
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException("Snooze: " + message); }
        string site = form._sites[0].Name;
        form.ImportManagedHosts(new()
        {
            new(site, "127.0.0.1", "Paused host", "", new HostOptions(), "Unknown"),
            new(site, "127.0.0.2", "Other host", "", new HostOptions(), "Unknown")
        }, false);
        form.StartMonitoring();
        var first = form.HostSnapshot().Single(host => host.Address == "127.0.0.1");
        string key = BuildHostKey(site, first.Address), otherKey = BuildHostKey(site, "127.0.0.2");
        var oldRuntime = form._hosts[key];
        var cancelledToken = form._hostTokens[key].Token;
        var otherToken = form._hostTokens[otherKey];
        for (int n = 0; n < 3; n++) form.ProcessResult(oldRuntime, false, null);
        Check(oldRuntime.State == HostState.Offline, "test host did not establish an outage");
        form.AppendCommandLog(site, first.Address, false, "FAILED test");
        form.SetHostSnooze(first.Options.Id, DateTimeOffset.UtcNow.AddHours(1));
        Check(cancelledToken.IsCancellationRequested && !form._hosts.ContainsKey(key) && !form._hostTokens.ContainsKey(key),
            "snooze must cancel and remove its worker");
        Check(ReferenceEquals(form._hostTokens[otherKey], otherToken) && !otherToken.IsCancellationRequested,
            "snooze restarted an unrelated worker");
        Check(!form.GetConfiguredTargets().Any(host => host.Address == first.Address), "snoozed host remained a probe target");
        int commands = form._commandEntries.Count, events = form._stateEvents.Count;
        form.AppendCommandLog(site, first.Address, false, "late probe");
        form.ProcessResult(oldRuntime, true, 1);
        form.RecordStateEvent(site, first.Address, "Paused host", "RECOVERED", "late probe");
        Check(commands == form._commandEntries.Count && events == form._stateEvents.Count, "in-flight probe escaped snooze");
        form.RebuildCommandView();
        Check(!form._commandBox.Text.Contains(first.Address), "snoozed host remained in live CMD output");
        var snapshot = form.BuildWallboardSnapshot();
        Check(snapshot.Commands.All(command => !command.Text.Contains(first.Address)), "Wallboard CMD output retained snoozed host");
        Check(snapshot.Sites.SelectMany(s => s.Hosts).Single(host => host.Address == first.Address).SnoozedUntilUtc > DateTimeOffset.UtcNow,
            "snoozed host lost its visible inventory/expiry");

        var saved = form.BuildConfig();
        using (var reopened = new MainForm(false, true, false))
        {
            reopened.ApplyConfig(saved);
            reopened.StartMonitoring();
            Check(!reopened._hosts.ContainsKey(key), "restart ignored the saved snooze");
            reopened.StopMonitoring();
        }

        // Simulate expiry without user action; the normal refresh timer invokes this path.
        form.FindSite(site)!.HostDetails[first.Address].SnoozedUntilUtc = DateTimeOffset.UtcNow.AddSeconds(-1);
        form.RefreshGrid();
        Check(form._hosts.ContainsKey(key) && !form._hostTokens[key].IsCancellationRequested, "expiry did not resume the worker");
        form.SetHostSnooze(first.Options.Id, DateTimeOffset.UtcNow.AddHours(1));
        form.SetHostSnooze(first.Options.Id, null);
        Check(form._hosts.ContainsKey(key), "manual resume did not restart monitoring");

        form.SetHostSnooze(first.Options.Id, DateTimeOffset.UtcNow.AddHours(1));
        var other = form.HostSnapshot().Single(host => host.Address == "127.0.0.2");
        form.SetHostSnooze(other.Options.Id, DateTimeOffset.UtcNow.AddHours(1));
        form.StopMonitoring();
        form.StartMonitoring();
        Check(form._cts != null && form._hostTokens.Count == 0, "an all-snoozed session cannot wait for expiry");
        form.FindSite(site)!.HostDetails[first.Address].SnoozedUntilUtc = DateTimeOffset.UtcNow.AddSeconds(-1);
        form.RefreshGrid();
        Check(form._hostTokens.ContainsKey(key) && !form._hostTokens.ContainsKey(otherKey),
            "all-snoozed session failed to resume only the expired host");
        form.StopMonitoring();
    }
}
