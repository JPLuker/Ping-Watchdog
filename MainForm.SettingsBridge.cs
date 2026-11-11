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

// Reading and applying Settings, and opening the Settings window.
public sealed partial class MainForm
{
    private AppSettingsSnapshot GetAppSettingsSnapshot()
    {
        return new AppSettingsSnapshot(
            (int)_intervalSeconds.Value,
            (int)_timeoutMs.Value,
            (int)_failureThreshold.Value,
            (int)_recoveryThreshold.Value,
            _showCommandView.Checked,
            _wallboardShowCli,
            _minimizeToTray,
            _notificationsEnabled,
            _autoCheckUpdates,
            _showUpdateControlOnHome,
            _eventHistoryHours,
            _hideSuspectEvents,
            DisplayTime.Use12HourTime,
            _cts is not null,
            GetDisplayVersion(),
            GetUpdateStatusText(),
            GetUpdateActionText(),
            _restoreLastSiteOnStartup,
            _startMonitoringOnLaunch,
            AppTheme.Mode,
            _priorityAlarmSound,
            UpdatesManaged,
            _persistSites && StartupRegistration.IsEnabled(),
            _openWallboardOnStartup,
            VersionOverlay.Enabled);
    }

    private void ApplyAppSettings(AppSettingsSnapshot settings)
    {
        PersistAppSettings(settings);
        bool wasAutoCheck = _autoCheckUpdates;

        _autoCheckUpdates = settings.AutoCheckUpdates;
        _minimizeToTray = settings.MinimizeToTray;
        _notificationsEnabled = settings.NotificationsEnabled;
        _priorityAlarmSound = settings.PriorityAlarmSound ?? "";
        _priorityAlarm.SoundPath = _priorityAlarmSound;
        _wallboardShowCli = settings.WallboardShowCli;
        _showUpdateControlOnHome = settings.ShowUpdateControlOnHome;
        _restoreLastSiteOnStartup = settings.RestoreLastSiteOnStartup;
        _startMonitoringOnLaunch = settings.StartMonitoringOnLaunch;
        _openWallboardOnStartup = settings.OpenWallboardOnStartup;
        VersionOverlay.SetEnabled(settings.ShowVersionOverlay);
        if (_persistSites && settings.StartWithWindows != StartupRegistration.IsEnabled())
        {
            try { StartupRegistration.Set(settings.StartWithWindows); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            { throw new InvalidOperationException("Windows did not save the sign-in setting. " + ex.Message, ex); }
        }
        AppTheme.SetMode(settings.Appearance);
        _eventHistoryHours = NormalizeEventHistoryHours(settings.EventHistoryHours);
        _hideSuspectEvents = settings.HideSuspectEvents;
        DisplayTime.Use12HourTime = settings.Use12HourTime;

        if (_cts is null)
        {
            _intervalSeconds.Value = Math.Clamp(
                settings.PingIntervalSeconds,
                (int)_intervalSeconds.Minimum,
                (int)_intervalSeconds.Maximum);
            _timeoutMs.Value = Math.Clamp(
                settings.PingTimeoutMs,
                (int)_timeoutMs.Minimum,
                (int)_timeoutMs.Maximum);
            _failureThreshold.Value = Math.Clamp(
                settings.FailureThreshold,
                (int)_failureThreshold.Minimum,
                (int)_failureThreshold.Maximum);
            _recoveryThreshold.Value = Math.Clamp(
                settings.RecoveryThreshold,
                (int)_recoveryThreshold.Minimum,
                (int)_recoveryThreshold.Maximum);
        }

        if (_showCommandView.Checked != settings.ShowCommandView)
            _showCommandView.Checked = settings.ShowCommandView;

        if (_autoCheckUpdates && !UpdatesManaged)
        {
            _updateTimer.Start();

            if (!wasAutoCheck && UpdatePolicy.IsDue(_lastUpdateCheckUtc, DateTimeOffset.UtcNow))
                _ = CheckForUpdatesAsync(userInitiated: false);
        }
        else
        {
            _updateTimer.Stop();
        }

        ApplyResponsiveLayout();
        _eventHistoryForm?.RefreshNow();
        _settingsForm?.RefreshRuntimeState();
        RefreshGrid();
        RebuildCommandView();
        _statusLabel.Text = "Settings saved.";
    }

    private void OpenSettings(IWin32Window? owner = null)
    {
        if (_settingsForm is not null && !_settingsForm.IsDisposed)
        {
            _settingsForm.Show();
            _settingsForm.Activate();
            _settingsForm.RefreshRuntimeState();
            return;
        }

        _settingsForm = new SettingsForm(
            GetAppSettingsSnapshot,
            ApplyAppSettings,
            RunUpdateActionAsync,
            () => OpenEventHistory(owner), ExportDataBackup, RestoreDataBackup, OpenDataFolder, EnablePortableStorage, DeleteSavedData, CanEnablePortableStorage);

        _settingsForm.FormClosed += (_, _) => _settingsForm = null;
        _settingsForm.Show(owner ?? this);
    }
}
