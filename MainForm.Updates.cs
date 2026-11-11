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

// Checking for, downloading and installing updates.
public sealed partial class MainForm
{
    private string GetUpdateActionText()
    {
        return _pendingUpdateManager is not null && _pendingUpdateInfo is not null
            ? "Restart to Update"
            : "Check for Updates";
    }

    private async Task RunUpdateActionAsync()
    {
        if (_pendingUpdateManager is not null && _pendingUpdateInfo is not null)
        {
            RestartToApplyPendingUpdate();
            return;
        }

        await CheckForUpdatesAsync(userInitiated: true);
        _settingsForm?.RefreshRuntimeState();
    }

    private void SetUpdateReadyUi(string version)
    {
        _pendingUpdateVersion = version;
        SetUpdateStatus($"Version {_pendingUpdateVersion} is downloaded and ready.");
        _checkUpdateButton.Enabled = true;

        _trayUpdateItem.Text = $"Restart to Update ({version})";
        _trayUpdateItem.Visible = true;
        _trayUpdateItem.Enabled = true;

        _statusLabel.Text = $"Update {version} downloaded • restart when ready";
        _settingsForm?.RefreshRuntimeState();
    }

    private void ClearPendingUpdateUi()
    {
        _pendingUpdateManager = null;
        _pendingUpdateInfo = null;
        _pendingUpdateVersion = null;
        _trayUpdateItem.Visible = false;
        _trayUpdateItem.Enabled = false;
        _settingsForm?.RefreshRuntimeState();
    }

    private void CheckAppliedUpdate()
    {
        if (!_persistSites || _appliedUpdateVersion.Length == 0) return;
        string expected = _appliedUpdateVersion;
        _appliedUpdateVersion = "";
        UpdateSavedPreference(p => p.PendingUpdateVersion = "");
        if (UpdatePolicy.IsInstalled(GetDisplayVersion(), expected))
        {
            SetUpdateStatus($"Updated to {(expected.StartsWith('v') ? expected : "v" + expected)}", notify: true);
            return;
        }
        using var failure = new UpdateFailureDialog($"Ping Watchdog is still on {GetDisplayVersion()}.",
            $"The update to {expected} didn't install.", "Update didn't install", "Try again");
        if (failure.ShowDialog(this) == DialogResult.Retry)
            BeginInvoke(async () => await CheckForUpdatesAsync(userInitiated: true));
    }

    private void RestartToApplyPendingUpdate()
    {
        if (_pendingUpdateManager is null || _pendingUpdateInfo is null)
            return;

        string version = _pendingUpdateVersion ?? "the downloaded version";
        bool monitoring = _cts is not null;

        using var confirmation = new UpdateRestartDialog(version, monitoring);
        if (confirmation.ShowDialog(UpdateDialogOwner) == DialogResult.OK)
            ApplyPendingUpdateAndRestart();
    }

    private void ApplyPendingUpdateAndRestart()
    {
        if (_pendingUpdateManager is null || _pendingUpdateInfo is null)
            return;

        string version = _pendingUpdateVersion ?? "the downloaded version";

        try
        {
            // Closing Settings invokes its existing save/discard/cancel guard.
            var settings = _settingsForm;
            if (settings is { IsDisposed: false })
            {
                settings.Close();
                if (!settings.IsDisposed) return;
            }
            PersistCurrentEditor();
            if (!TrySaveSites())
            {
                SetUpdateStatus("Update postponed. Save or export your changes before restarting.", true, true);
                return;
            }
            _wallboardForm?.Close();

            if (_cts is not null)
                StopMonitoring();

            _checkUpdateButton.Enabled = false;
            SetUpdateStatus("Restarting…", true, true);
            _trayUpdateItem.Enabled = false;
            _statusLabel.Text = $"Applying update {version}...";

            // #35: no Velopack progress window with an OK button. The app closes, the update installs,
            // and Ping Watchdog reopens; the next start confirms the version changed (CheckAppliedUpdate).
            string target = _pendingUpdateInfo.TargetFullRelease.Version.ToString();
            UpdateSavedPreference(p => p.PendingUpdateVersion = target);
            StartupRecovery.ExpectUpdateExit();
            _pendingUpdateManager.WaitExitThenApplyUpdates(_pendingUpdateInfo.TargetFullRelease, silent: true, restart: true, Array.Empty<string>());
            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            StartupRecovery.CancelExpectedExit();
            _checkUpdateButton.Enabled = true;
            SetUpdateStatus("Update downloaded and ready.");
            _trayUpdateItem.Enabled = true;
            _statusLabel.Text = "Update restart failed.";

            using var failure = new UpdateFailureDialog(ex.Message);
            if (failure.ShowDialog(UpdateDialogOwner) == DialogResult.Retry)
            {
                // Return to the message loop before reopening confirmation; repeated failures
                // must not build a recursive stack or bypass Settings/save protection.
                BeginInvoke((Action)(() =>
                {
                    if (!IsDisposed && !Disposing) RestartToApplyPendingUpdate();
                }));
            }
        }
    }

    private async Task CheckForUpdatesAsync(bool userInitiated)
    {
        if ((!userInitiated && !_autoCheckUpdates) || IsDisposed || Disposing) return;
        if (UpdatesManaged)
        {
            SetUpdateStatus(UpdatePolicy.ManagedText);
            return;
        }
        if (_pendingUpdateManager != null && _pendingUpdateInfo != null)
        {
            SetUpdateStatus($"Version {_pendingUpdateVersion} is downloaded and ready.", userInitiated, true);
            return;
        }
        if (_updateCheckInProgress) return;
        _updateCheckInProgress = true;
        _checkUpdateButton.Enabled = false;
        SetUpdateStatus("Checking for updates…", userInitiated, true);
        try
        {
            var manager = new UpdateManager(new GithubSource(UpdateRepoUrl, accessToken: null, prerelease: false));
            if (!manager.IsInstalled)
            {
                SetUpdateStatus("Install the current Setup or Portable release to enable automatic updates.", userInitiated, true);
                return;
            }
            var update = await manager.CheckForUpdatesAsync();
            RecordUpdateCheck(DateTimeOffset.UtcNow);
            if (update == null)
            {
                ClearPendingUpdateUi();
                SetUpdateStatus("Up to date", userInitiated);
                return;
            }
            string version = update.TargetFullRelease.Version.ToString();
            SetUpdateStatus($"Downloading version {version}…", userInitiated, true);
            await manager.DownloadUpdatesAsync(update, progress =>
            {
                if (!IsHandleCreated || IsDisposed || Disposing) return;
                try { BeginInvoke((Action)(() => { if (!IsDisposed && !Disposing && _updateCheckInProgress && _pendingUpdateManager == null) SetUpdateStatus($"Downloading {progress}%", userInitiated, true); })); }
                catch (InvalidOperationException) { }
            });
            _pendingUpdateManager = manager;
            _pendingUpdateInfo = update;
            SetUpdateReadyUi(version);
            SetUpdateStatus($"Version {version} is ready to install.", true, true);
        }
        catch (Exception ex)
        {
            SetUpdateStatus("Update check failed: " + ex.Message, userInitiated, true);
        }
        finally
        {
            _updateCheckInProgress = false;
            if (!IsDisposed && !Disposing) _checkUpdateButton.Enabled = true;
            _settingsForm?.RefreshRuntimeState();
        }
    }
}
