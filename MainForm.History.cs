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

// Outage history: recording, saving, filtering and the Outage history window.
public sealed partial class MainForm
{
    private void RecordStateEvent(
        string site,
        string host,
        string displayHost,
        string kind,
        string message)
    {
        if (_cts is not null && !IsHostProbeAllowed(site, host))
            return;

        lock (_stateEvents)
        {
            _stateEvents.Add(new StateEventRecord(
                DateTime.Now,
                site,
                host,
                displayHost,
                kind,
                message));

            PersistEventHistoryUnsafe();
        }
    }

    private void LoadEventHistory()
    {
        if (!_persistSites)
            return;

        try
        {
            if (!File.Exists(_eventHistoryPath))
                return;

            var loaded = JsonSerializer.Deserialize<List<StateEventRecord>>(
                File.ReadAllText(_eventHistoryPath));

            if (loaded is null)
                return;

            lock (_stateEvents)
            {
                _stateEvents.Clear();
                _stateEvents.AddRange(
                    loaded
                        .Where(e =>
                            e.Timestamp != default &&
                            !string.IsNullOrWhiteSpace(e.Site) &&
                            !string.IsNullOrWhiteSpace(e.Host) &&
                            !string.IsNullOrWhiteSpace(e.Kind))
                        .OrderBy(e => e.Timestamp));
            }
        }
        catch
        {
            // A damaged history file must never prevent monitoring.
        }
    }

    private void PersistEventHistoryUnsafe()
    {
        if (!_persistSites || _restoreRequiresRestart)
            return;

        try
        {
            var directory = Path.GetDirectoryName(_eventHistoryPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            string tempPath = _eventHistoryPath + ".tmp";

            File.WriteAllText(
                tempPath,
                JsonSerializer.Serialize(_stateEvents));

            File.Move(tempPath, _eventHistoryPath, overwrite: true);
        }
        catch
        {
            // History persistence is best-effort; monitoring stays authoritative.
        }
    }

    private IReadOnlyList<StateEventRecord> GetStateEventSnapshot()
    {
        lock (_stateEvents)
            return _stateEvents.ToList();
    }

    internal static IReadOnlyList<StateEventRecord> FilterStateEvents(
        IEnumerable<StateEventRecord> source,
        int windowHours,
        bool hideSuspects,
        DateTime now)
    {
        windowHours = NormalizeEventHistoryHours(windowHours);
        DateTime? cutoff = windowHours == 0
            ? null
            : now.AddHours(-windowHours);

        return source
            .Where(e => !cutoff.HasValue || e.Timestamp >= cutoff.Value)
            .Where(e =>
                !hideSuspects ||
                !e.Kind.Equals("Suspect", StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e.Timestamp)
            .ToList();
    }

    internal static int NormalizeEventHistoryHours(int hours)
    {
        return hours switch
        {
            0 or 24 or 168 or 720 => hours,
            _ => 24
        };
    }

    internal static string GetEventHistoryWindowLabel(int hours)
    {
        return NormalizeEventHistoryHours(hours) switch
        {
            24 => "Last 24 hours",
            168 => "Last 7 days",
            720 => "Last 30 days",
            _ => "All time"
        };
    }

    private EventHistoryPreferences GetEventHistoryPreferences()
    {
        return new EventHistoryPreferences(
            _eventHistoryHours,
            _hideSuspectEvents);
    }

    private void SetEventHistoryPreferences(int hours, bool hideSuspects)
    {
        _eventHistoryHours = NormalizeEventHistoryHours(hours);
        _hideSuspectEvents = hideSuspects;
        SaveSites();
    }

    private void CycleEventHistoryWindow()
    {
        int[] windows = { 24, 168, 720, 0 };
        int current = Array.IndexOf(windows, _eventHistoryHours);
        _eventHistoryHours = windows[(Math.Max(0, current) + 1) % windows.Length];
        SaveSites();
    }

    private void ToggleSuspectHistory()
    {
        _hideSuspectEvents = !_hideSuspectEvents;
        SaveSites();
    }

    private void OpenEventHistory(IWin32Window? owner = null)
    {
        if (_eventHistoryForm is not null && !_eventHistoryForm.IsDisposed)
        {
            _eventHistoryForm.Show();
            _eventHistoryForm.Activate();
            return;
        }

        _eventHistoryForm = new EventHistoryForm(
            GetStateEventSnapshot,
            GetEventHistoryPreferences,
            SetEventHistoryPreferences);

        _eventHistoryForm.FormClosed += (_, _) => _eventHistoryForm = null;
        _eventHistoryForm.Show(owner ?? this);
    }
}
