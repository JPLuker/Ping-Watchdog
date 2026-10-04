using System.Threading.Channels;

namespace PingWatchdog.Linux;

internal static class MonitoringRegressionTests
{
    public static async Task RunAsync()
    {
        string root = Path.Combine(Path.GetTempPath(), "watchdog-probes-" + Guid.NewGuid().ToString("N"));
        var requests = Channel.CreateUnbounded<TaskCompletionSource<ProbeResult>>();
        Task<ProbeResult> Probe(string address, int timeout, CancellationToken token)
        {
            var reply = new TaskCompletionSource<ProbeResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            requests.Writer.TryWrite(reply);
            return reply.Task; // Deliberately allow late results to test the engine's own guard.
        }
        using var engine = new WatchdogEngine(root, Probe, (_, token) => Task.CompletedTask);
        try
        {
            var config = engine.Snapshot().Settings;
            config.NotificationsEnabled = false;
            config.AutoCheckUpdates = false;
            config.FailureThreshold = 3;
            config.RecoveryThreshold = 2;
            engine.ApplySettings(config);
            engine.SaveHosts("Default Site", "probe.example");
            engine.StartMonitoring();
            HostSnapshot Host() => engine.Snapshot().Hosts.Single();
            int Events(string kind) => engine.HistorySnapshot(0, false).Events.Count(e => e.Kind == kind);
            async Task Reply(bool success)
            {
                int count = engine.Snapshot().Commands.Count;
                var request = await requests.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));
                request.SetResult(new ProbeResult(success, success ? 7 : null, success ? "Reply" : "Timeout"));
                await UntilAsync(() => engine.Snapshot().Commands.Count == count + 1);
            }
            Check(Host().State == HostState.Unknown, "New monitor begins UNKNOWN");
            await Reply(true);
            Check(Host().State == HostState.Online && Host().LatencyMs == 7 && Host().LastReply is not null, "Successful probe sets ONLINE, latency, and last reply");
            var lastReply = Host().LastReply;
            await Reply(false);
            Check(Host().State == HostState.Suspect && Host().Failures == 1 && Events("SUSPECT") == 1, "First failure emits one SUSPECT event");
            await Reply(false);
            Check(Host().State == HostState.Suspect && Events("SUSPECT") == 1, "Repeated suspect failure does not duplicate events");
            await Reply(false);
            var outage = Host().OutageStarted;
            Check(Host().State == HostState.Offline && outage is not null && Host().LastReply == lastReply && Events("DOWN") == 1, "Threshold emits DOWN and preserves last reply");
            await Reply(false);
            Check(Events("DOWN") == 1 && Host().OutageStarted == outage, "Offline failures retain the original outage");
            await Reply(true);
            Check(Host().State == HostState.Offline && Events("RECOVERED") == 0, "One reply does not recover before the threshold");
            await Reply(false);
            await Reply(true);
            Check(Host().State == HostState.Offline, "An intervening failure resets recovery progress");
            await Reply(true);
            Check(Host().State == HostState.Online && Host().OutageStarted is null && Events("RECOVERED") == 1, "Consecutive replies emit one RECOVERED event and clear outage");

            var old = await requests.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));
            int commands = engine.Snapshot().Commands.Count;
            engine.StopMonitoring();
            engine.StartMonitoring();
            var current = await requests.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));
            old.SetResult(new ProbeResult(false, null, "Late old-session reply"));
            await Task.Delay(30);
            Check(Host().State == HostState.Unknown && engine.Snapshot().Commands.Count == commands, "Late stopped-session probe cannot update a restarted host");
            current.SetResult(new ProbeResult(true, 3, "Current-session reply"));
            await UntilAsync(() => Host().State == HostState.Online);
            Check(engine.Snapshot().Commands.Count == commands + 1, "Restarted monitor retains its own active probe loop");

            var removed = await requests.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));
            engine.SaveHosts("Default Site", "");
            commands = engine.Snapshot().Commands.Count;
            removed.SetResult(new ProbeResult(false, null, "Late removed-host reply"));
            await Task.Delay(30);
            Check(engine.Snapshot().Hosts.Count == 0 && engine.Snapshot().Commands.Count == commands, "Removed host cannot publish a pending result");
        }
        finally
        {
            engine.StopMonitoring();
            try { Directory.Delete(root, true); } catch { }
        }
    }

    private static async Task UntilAsync(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (!predicate()) await Task.Delay(5, timeout.Token);
    }
    private static void Check(bool condition, string name) => LinuxRegressionTests.Check(condition, name);
}
