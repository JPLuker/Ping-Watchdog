using PingWatchdog.Shared;

namespace PingWatchdog;

// Settings pages, one builder per category (DESIGN.md A4 order).
internal sealed partial class SettingsForm
{
    private Control BuildGeneralPage()
    {
        var page = Page("General", "Application behavior and display defaults.");
        var stack = (FlowLayoutPanel)page.Controls[1];

        stack.Controls.Add(Section(
            "Window behavior",
            _minimizeToTray,
            "Monitoring keeps running while it's hidden."));
        stack.Controls.Add(Section("Startup", new Control[] { _startWithWindows, _autoStart, _wallboardOnStartup, _restoreLastSite },
            "The Wallboard opens on the screen it was last on."));

        return page;
    }

    private Control BuildDisplayPage()
    {
        var page = Page("Appearance", "Clock format and ping-log visibility.");
        var stack = (FlowLayoutPanel)page.Controls[1];
        stack.Controls.Add(Section("Appearance", _appearance, "System follows Windows."));
        stack.Controls.Add(Section("Time display", _use12HourTime, ""));
        stack.Controls.Add(Section("Ping log", new Control[] { _showMainCli, _wallboardCli },
            ""));
        return page;
    }

    private Control BuildNotificationsPage()
    {
        var page = Page("Notifications", "Outage alerts and temporary quiet periods.");
        var stack = (FlowLayoutPanel)page.Controls[1];
        stack.Controls.Add(Section("Windows notifications", _notifications,
            "History still records outages when alerts are off."));
        var soundActions = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Margin = Padding.Empty };
        var choose = new ThemeButton { Text = "Choose sound…", AutoSize = true };
        var test = new ThemeButton { Text = "Test", AutoSize = true };
        var reset = new ThemeButton { Text = "Use Windows alarm", AutoSize = true, Ghost = true };
        choose.Click += (_, _) =>
        {
            using var dialog = new OpenFileDialog { Filter = "WAV sound|*.wav", Title = "Priority alarm sound" };
            if (dialog.ShowDialog(this) == DialogResult.OK) SetAlarmSound(dialog.FileName);
        };
        test.Click += (_, _) => PriorityAlarm.PlaySound(_alarmSound);
        reset.Click += (_, _) => SetAlarmSound("");
        soundActions.Controls.AddRange(new Control[] { choose, test, reset });
        stack.Controls.Add(Section("Priority alarm", new Control[] { _alarmSoundName, soundActions },
            "Priority hosts ring every 15 seconds when they go down or come back, until you acknowledge it."));
        return page;
    }

    private Control BuildAdvancedPage()
    {
        var page = Page("Advanced", "Preference recovery.");
        var stack = (FlowLayoutPanel)page.Controls[1];
        stack.Controls.Add(Section("Restore preferences", _resetButton,
            "Puts the defaults in place for you to review and save. Hosts and history aren't touched."));
        stack.Controls.Add(Section("Testing", _versionOverlay, "Shows the version and build in the bottom-left corner of every window."));
        return page;
    }

    private Control BuildMonitoringPage()
    {
        var page = Page("Monitoring", "Default ICMP timing and outage rules.");
        var stack = (FlowLayoutPanel)page.Controls[1];
        stack.Controls.Add(SettingRow("Ping interval", _interval, "seconds"));
        stack.Controls.Add(SettingRow("Ping timeout", _timeout, "milliseconds"));
        stack.Controls.Add(SettingRow("Declare DOWN after", _downAfter, "consecutive failures"));
        stack.Controls.Add(SettingRow("Declare RECOVERED after", _recoverAfter, "consecutive successes"));
        _monitorLockLabel.Margin = new Padding(0, 14, 0, 0);
        stack.Controls.Add(_monitorLockLabel);
        return page;
    }

    private Control BuildHistoryPage()
    {
        var page = Page("History", "Persistent outage-history display defaults.");
        var stack = (FlowLayoutPanel)page.Controls[1];
        stack.Controls.Add(SettingRow("Default history range", _historyRange, ""));
        _hideSuspects.Margin = new Padding(0, 10, 0, 8);
        stack.Controls.Add(_hideSuspects);
        stack.Controls.Add(new Label
        {
            Text = "Only changes what's shown. Events are never deleted.",
            AutoSize = true,
            MaximumSize = new Size(560, 0),
            ForeColor = Color.FromArgb(139, 153, 169),
            Margin = new Padding(0, 4, 0, 16)
        });
        stack.Controls.Add(_openHistoryButton);
        return page;
    }

    private Control BuildUpdatesPage()
    {
        var page = Page("Updates", "GitHub release checks and installed-version status.");
        var stack = (FlowLayoutPanel)page.Controls[1];
        stack.Controls.Add(LabeledValue("Update status", _updateStatus));
        _autoUpdates.Margin = new Padding(0, 12, 0, 10);
        stack.Controls.Add(_updatesManaged);
        stack.Controls.Add(_autoUpdates);

        _showHomeUpdateControl.Margin = new Padding(0, 4, 0, 14);


        stack.Controls.Add(_updateButton);
        stack.Controls.Add(new Label
        {
            Text = "",
            AutoSize = true,
            ForeColor = Color.FromArgb(139, 153, 169),
            Margin = new Padding(0, 12, 0, 0)
        });
        return page;
    }

    private Control BuildDataPage()
    {
        // FOCAL: the existing staged Settings save action. Data operations stay secondary.
        var page = Page("Data and storage", "");
        var stack = (FlowLayoutPanel)page.Controls[1];
        var backup = new ThemeButton { Text = "Create backup…", AutoSize = true, Enabled = _backupAction != null };
        var restore = new ThemeButton { Text = "Restore backup…", AutoSize = true, Enabled = _restoreAction != null };
        var open = new ThemeButton { Text = "Open data folder", AutoSize = true, Enabled = _openDataFolder != null };
        var portable = new ThemeButton { Text = "Enable portable storage…", AutoSize = true, Enabled = _portableAction != null && _canEnablePortable };
        _deleteDataButton = new ThemeButton { Text = "Delete saved data…", AutoSize = true, Destructive = true, Ghost = true, Enabled = _deleteDataAction != null };
        backup.Click += (_, _) => _backupAction?.Invoke();
        restore.Click += (_, _) => _restoreAction?.Invoke();
        open.Click += (_, _) => _openDataFolder?.Invoke();
        portable.Click += (_, _) => _portableAction?.Invoke();
        _deleteDataButton.Click += (_, _) => _deleteDataAction?.Invoke();
        stack.Controls.Add(Section(StoragePaths.Current.Portable ? "Portable storage" : "Per-user storage", open, StoragePaths.Current.DataDirectory));
        var backupButtons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = true };
        backupButtons.Controls.Add(backup); backupButtons.Controls.Add(restore);
        stack.Controls.Add(Section("Backups", new Control[] { backupButtons, _dataStatus },
            "Restoring replaces your current data and restarts the app."));
        stack.Controls.Add(Section("Storage location", portable, StoragePaths.Current.Portable
            ? "Keep the Data folder when you move or update this copy."
            : _canEnablePortable ? "Copies your data next to this portable copy and restarts. The AppData copy stays."
            : "Only in the portable copy."));
        stack.Controls.Add(Section("Delete saved data", _deleteDataButton,
            "Removes data, backups and recovery reports, then closes the app. Stop monitoring first."));
        return page;
    }

    private Control BuildAboutPage()
    {
        var page = Page("About", "");
        var stack = (FlowLayoutPanel)page.Controls[1];
        stack.Controls.Add(LabeledValue("Ping Watchdog", _versionValue));
        stack.Controls.Add(new Label
        {
            Text = "Copyright © 2026 Joseph Luker. All rights reserved.",
            AutoSize = true, Margin = new Padding(0, 0, 0, 12)
        });
        var license = new ThemeButton { Text = "View license", AutoSize = true };
        var issue = new ThemeButton { Text = "Report an issue…", AutoSize = true };
        var diagnostics = new ThemeButton { Text = "Review diagnostic info…", AutoSize = true };
        var status = new Label { AutoSize = true, Margin = new Padding(0, 8, 0, 0) };
        license.Click += (_, _) =>
        {
            try { using var review = SupportInfo.CreateReview("License", SupportInfo.ReadLicense(), false); review.ShowDialog(this); }
            catch (Exception ex) { status.Text = "Could not open the license: " + ex.Message; }
        };
        issue.Click += (_, _) =>
        {
            try { SupportInfo.OpenIssue(); status.Text = "Issue page opened in your browser."; }
            catch (Exception ex) { status.Text = "Could not open your browser. Visit " + SupportInfo.IssueUrl + "\r\n" + ex.Message; }
        };
        diagnostics.Click += (_, _) =>
        {
            using var review = SupportInfo.CreateReview("Diagnostic information", SupportInfo.CreateDiagnostics(_provider().Version), true);
            review.ShowDialog(this);
        };
        var actions = new FlowLayoutPanel
        {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.TopDown, WrapContents = false,
            Margin = new Padding(0)
        };
        var notices = new ThemeButton { Text = "Open third-party notices", AutoSize = true };
        notices.Click += (_, _) =>
        {
            string folder = Path.Combine(AppContext.BaseDirectory, "ThirdPartyNotices");
            try
            {
                if (!File.Exists(Path.Combine(folder, "INDEX.txt")))
                    throw new FileNotFoundException("This copy has no packaged notice folder.");
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(folder) { UseShellExecute = true });
            }
            catch (Exception ex) { status.Text = "Could not open notices: " + ex.Message; }
        };
        actions.Controls.Add(license);
        actions.Controls.Add(notices);
        actions.Controls.Add(issue);
        actions.Controls.Add(diagnostics);
        actions.Controls.Add(status);
        stack.Controls.Add(actions);
        return page;
    }
}
