namespace PingWatchdog;

public sealed partial class MainForm
{
    private void ExportMonitoringBatch()
    {
        var targets = GetConfiguredTargets().Select(target => (
            target.Site, target.Address, Label: GetNickname(target.Site, target.Address),
            TimeoutMs: GetHostOptions(FindSite(target.Site), target.Address).TimeoutMs > 0
                ? GetHostOptions(FindSite(target.Site), target.Address).TimeoutMs : (int)_timeoutMs.Value)).ToList();
        if (targets.Count == 0)
        {
            MessageBox.Show(this, "Add an enabled host to the selected site first.", "Export monitoring batch");
            return;
        }
        using var save = new SaveFileDialog { Title = "Export continuous ping monitor",
            Filter = "Windows batch file (*.bat)|*.bat", DefaultExt = "bat", AddExtension = true,
            FileName = "PingWatchdog-monitor.bat" };
        if (save.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            File.WriteAllText(save.FileName, BuildMonitoringBatch(targets, (int)_intervalSeconds.Value),
                new System.Text.UTF8Encoding(false));
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Could not export monitoring batch"); }
    }

    private static string BuildMonitoringBatch(IReadOnlyList<(string Site, string Address, string Label, int TimeoutMs)> targets, int interval)
    {
        if (targets.Count == 0) throw new ArgumentException("No monitoring targets.");
        foreach (var target in targets)
        {
            if (string.IsNullOrWhiteSpace(target.Address) || target.Address.StartsWith("-", StringComparison.Ordinal) ||
                !target.Address.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or ':' or '_') ||
                (System.Net.IPAddress.TryParse(target.Address, out _) == false && Uri.CheckHostName(target.Address) == UriHostNameType.Unknown))
                throw new ArgumentException("A target contains an unsupported address: " + target.Address);
        }
        static string Label(string value) => new(value.Select(c => char.IsLetterOrDigit(c) || c is ' ' or '.' or '-' or '_' or ':' or '/' ? c : ' ').ToArray());
        var lines = new List<string> { "@echo off", "setlocal DisableDelayedExpansion", "chcp 65001 >nul",
            "title Ping Watchdog - continuous monitoring", ":monitor", "cls",
            "echo Continuous ping monitor", "echo Checks run in sequence. Press Ctrl+C to stop.", "echo." };
        foreach (var target in targets)
        {
            lines.Add("echo " + Label(target.Site + " / " + (string.IsNullOrWhiteSpace(target.Label) ? target.Address : target.Label + " - " + target.Address)));
            lines.Add("ping.exe -n 1 -w " + Math.Clamp(target.TimeoutMs, 250, 60000) + " \"" + target.Address + "\"");
            lines.Add("echo.");
        }
        lines.Add("if /I \"%~1\"==\"--once\" exit /b 0");
        lines.Add("timeout.exe /t " + Math.Clamp(interval, 1, 3600) + " /nobreak >nul");
        lines.Add("goto monitor");
        return string.Join("\r\n", lines) + "\r\n";
    }
}
