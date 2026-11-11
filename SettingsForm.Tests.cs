using PingWatchdog.Shared;

namespace PingWatchdog;

// Settings save-failure and layout checks used by the self-tests and UI smoke test.
internal sealed partial class SettingsForm
{
    internal static void RunSaveFailureTests(AppSettingsSnapshot initial)
    {
        var current = initial;
        bool fail = true;
        using var form = new SettingsForm(() => current, settings =>
        {
            if (fail) throw new IOException("Test: destination is locked.");
            current = settings;
        }, () => Task.CompletedTask, () => { });
        form.LoadSettings();
        form._autoStart.Checked = !initial.StartMonitoringOnLaunch;
        if (form.SaveSettings() || !form.HasChanges() || !form._saveButton.Enabled || form._saveStatus.Text == "Saved" ||
            current.StartMonitoringOnLaunch != initial.StartMonitoringOnLaunch || form._saveError == null)
            throw new InvalidOperationException("A failed Settings save lost its draft or reported success.");
        fail = false;
        if (!form.SaveSettings() || form.HasChanges() || form._saveStatus.Text != "Saved" ||
            current.StartMonitoringOnLaunch == initial.StartMonitoringOnLaunch)
            throw new InvalidOperationException("Settings save retry did not commit the draft.");
        form._autoStart.Checked = initial.StartMonitoringOnLaunch;
        form.LoadSettings(); // Discarding a draft reloads the actual committed values.
        if (form.HasChanges() || form._autoStart.Checked != current.StartMonitoringOnLaunch)
            throw new InvalidOperationException("Settings discard did not restore committed values.");
        form._autoStart.Checked = !current.StartMonitoringOnLaunch;
        var canceled = new FormClosingEventArgs(CloseReason.FormOwnerClosing, false);
        form.ResolveCloseDecision(canceled, DialogResult.Cancel);
        if (!canceled.Cancel || !form.HasChanges())
            throw new InvalidOperationException("Canceling owner close lost the Settings draft.");
        fail = true;
        var failed = new FormClosingEventArgs(CloseReason.FormOwnerClosing, false);
        form.ResolveCloseDecision(failed, DialogResult.Yes);
        if (!failed.Cancel || !form.HasChanges())
            throw new InvalidOperationException("Failed Settings save allowed owner shutdown.");
        var discarded = new FormClosingEventArgs(CloseReason.FormOwnerClosing, false);
        form.ResolveCloseDecision(discarded, DialogResult.No);
        if (discarded.Cancel || form.HasChanges() || form._autoStart.Checked != current.StartMonitoringOnLaunch)
            throw new InvalidOperationException("Explicit discard was not resolved before owner shutdown.");
    }

    internal void RunLayoutChecks()
    {
        int originalAppearance = _appearance.SelectedIndex;
        _appearance.SelectedIndex = (originalAppearance + 1) % 3;
        if (!HasChanges()) throw new InvalidOperationException("Appearance changes were not tracked.");
        _appearance.SelectedIndex = originalAppearance;
        if (HasChanges()) throw new InvalidOperationException("Reverting appearance left unsaved changes.");
        bool original = _autoStart.Checked;
        _autoStart.Checked = !original;
        if (!HasChanges() || !_saveButton.Enabled) throw new InvalidOperationException("Settings edits are not marked unsaved.");
        _autoStart.Checked = original;
        if (HasChanges()) throw new InvalidOperationException("Reverting settings left a dirty state.");
        foreach (int width in new[] { 760, 1100 })
        {
            Width = width;
            for (int i = 0; i < _nav.Items.Count; i++)
            {
                _nav.SelectedIndex = i; PerformLayout();
                var page = (TableLayoutPanel)_pages[_nav.Items[i].ToString()!];
                var stack = (FlowLayoutPanel)page.Controls[1]; stack.PerformLayout();
                foreach (Control child in stack.Controls)
                    if (child.Width > stack.ClientSize.Width)
                        throw new InvalidOperationException("Settings section exceeds page width.");
            }
        }
        _nav.SelectedIndex = 0;
    }
}
