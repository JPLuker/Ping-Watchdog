using System.Collections.Concurrent;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using Velopack;
using Velopack.Sources;
using PingWatchdog.Shared;

namespace PingWatchdog;

// The ping activity log under Map and List.
public sealed partial class MainForm
{
    private void ToggleCommandView()
    {
        SetWorkspaceView(_listView);
        RebuildCommandView();
    }

    private void ClearCommandLog()
    {
        _commandEntries.Clear();
        _commandBox.Clear();
    }

    private void AppendCommandLog(
        string site,
        string host,
        bool success,
        string resultText)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() =>
                AppendCommandLog(site, host, success, resultText));
            return;
        }

        if (_cts is not null && !IsHostProbeAllowed(site, host)) return;

        var entry = new CommandLogEntry(
            site,
            host,
            DateTime.Now,
            success,
            resultText,
            _hosts.TryGetValue(BuildHostKey(site, host), out var monitored) && monitored.Options.TimeoutMs > 0
                ? monitored.Options.TimeoutMs : _pingTimeoutMs);

        _commandEntries.Add(entry);

        bool trimmed = false;

        if (_commandEntries.Count > 2000)
        {
            _commandEntries.RemoveRange(0, 500);
            trimmed = true;
        }

        if (trimmed)
        {
            RebuildCommandView();
            return;
        }

        if (CommandEntryMatchesFilter(entry))
            AppendCommandEntryToView(entry);
    }

    private bool CommandEntryMatchesFilter(CommandLogEntry entry)
    {
        if (!IsHostProbeAllowed(entry.Site, entry.Host))
            return false;

        return _selectedSiteName is null ||
               entry.Site.Equals(
                   _selectedSiteName,
                   StringComparison.OrdinalIgnoreCase);
    }

    private void RebuildCommandView()
    {
        _commandBox.Clear();

        foreach (var entry in _commandEntries)
        {
            if (CommandEntryMatchesFilter(entry))
                AppendCommandEntryToView(entry, scroll: false);
        }

        _commandBox.SelectionStart = _commandBox.TextLength;
        _commandBox.ScrollToCaret();
    }

    private string FormatCommandEntry(CommandLogEntry entry)
    {
        string nickname = GetNickname(entry.Site, entry.Host);
        string labelPart = string.IsNullOrWhiteSpace(nickname)
            ? string.Empty
            : $" [{nickname}]";

        return
            $"{entry.Host}: [{entry.Site}]{labelPart} [{DisplayTime.Clock(entry.Timestamp)}] ping {entry.Host} -n 1 -w {entry.TimeoutMs}  ->  {entry.ResultText}";
    }

    private void AppendCommandEntryToView(
        CommandLogEntry entry,
        bool scroll = true)
    {
        string command = FormatCommandEntry(entry);

        _commandBox.SelectionStart = _commandBox.TextLength;
        _commandBox.SelectionLength = 0;
        _commandBox.SelectionColor = entry.Success
            ? AppTheme.Palette.Text
            : AppTheme.Palette.StatusText(AppTheme.Palette.Danger);

        _commandBox.AppendText(command + Environment.NewLine);
        _commandBox.SelectionColor = _commandBox.ForeColor;

        if (scroll && _showCommandView.Checked)
        {
            _commandBox.SelectionStart = _commandBox.TextLength;
            _commandBox.ScrollToCaret();
        }
    }
}
