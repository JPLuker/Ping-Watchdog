namespace PingWatchdog;

public sealed partial class MainForm
{
    private static bool CanEnablePortableStorage
    {
        get
        {
            var locator = Velopack.Locators.VelopackLocator.Current;
            return !StoragePaths.Current.Portable && (locator.IsPortable || locator.CurrentlyInstalledVersion == null);
        }
    }

    private bool CloseSettingsForDataChange()
    {
        var window = _settingsForm;
        if (window == null || window.IsDisposed) return true;
        window.Close();
        return window.IsDisposed;
    }

    private void EnablePortableStorage()
    {
        var owner = (IWin32Window?)_settingsForm ?? this;
        if (_cts != null)
        {
            MessageBox.Show(owner, "Stop monitoring before switching storage.", "Portable storage", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (!CanEnablePortableStorage) return;
        if (MessageBox.Show(owner, "Copy the saved setup to the Data folder beside this portable copy and restart? The original AppData files remain. Existing destination data is never merged or replaced.",
            "Enable portable storage", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        if (!CloseSettingsForDataChange()) return;
        try
        {
            StoragePaths.EnablePortable(StoragePaths.Current, CreateDataBackup(), CanEnablePortableStorage);
            _restoreRequiresRestart = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Storage was not switched. Original files remain. " + ex.Message, "Portable storage",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        try { Application.Restart(); }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Portable storage is ready. Close and reopen this copy to use it. " + ex.Message,
                "Restart required", MessageBoxButtons.OK, MessageBoxIcon.Information);
            Close();
        }
    }

    private void DeleteSavedData()
    {
        var owner = (IWin32Window?)_settingsForm ?? this;
        if (_cts != null)
        {
            MessageBox.Show(owner, "Stop monitoring before deleting saved data.", "Delete saved data", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        using (var confirmation = new DeleteDataDialog(StoragePaths.Current))
            if (confirmation.ShowDialog(owner) != DialogResult.OK) return;
        if (!CloseSettingsForDataChange()) return;
        _restoreRequiresRestart = true;
        _updateTimer.Stop();
        StartupRecovery.SuppressForDataDeletion();
        try { DataDeletionStore.Delete(StoragePaths.Current); }
        catch (Exception ex)
        {
            string message = File.Exists(StoragePaths.Current.DeletionMarker)
                ? "Deletion could not finish. Some files may remain. Close other copies and reopen Ping Watchdog to retry."
                : "Deletion was not started. Review the folder access or linked folders before trying again.";
            MessageBox.Show(this, message + " " + ex.Message,
                "Deletion incomplete", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        Close(); // Never save the old in-memory inventory after deletion or partial deletion.
    }
}
