using PingWatchdog.Shared;

namespace PingWatchdog;

internal sealed class SettingsForm : Form
{
    private readonly Func<AppSettingsSnapshot> _provider;
    private readonly Action<AppSettingsSnapshot> _apply;
    private readonly Func<Task> _updateAction;
    private readonly Action _openHistory;
    private readonly System.Windows.Forms.Timer _runtimeTimer = new() { Interval = 750 };

    private readonly ListBox _nav = new()
    {
        Dock = DockStyle.Fill,
        BorderStyle = BorderStyle.None,
        IntegralHeight = false,
        Font = AuroraTheme.LabelFont,
        DrawMode = DrawMode.OwnerDrawFixed,
        ItemHeight = 36,
        AccessibleName = "Settings categories"
    };

    private readonly ComboBox _appearance = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180, AccessibleName = "Appearance" };

    private readonly Panel _content = new() { Dock = DockStyle.Fill };
    private readonly Button _saveButton = new AuroraButton() { Text = "Save changes", AutoSize = true };
    private readonly Button _closeButton = new AuroraButton() { Text = "Close", AutoSize = true };
    private readonly Button _saveDetails = new AuroraButton() { Text = "Details", AutoSize = true, Visible = false };
    private string? _saveError;

    private readonly CheckBox _minimizeToTray = new() { Text = "Minimize to notification area", AutoSize = true };
    private readonly CheckBox _restoreLastSite = new() { Text = "Restore the last selected site on startup", AutoSize = true };
    private readonly CheckBox _use12HourTime = new() { Text = "Use 12-hour time (AM / PM)", AutoSize = true };
    private readonly CheckBox _notifications = new() { Text = "Windows outage and recovery notifications", AutoSize = true };
    private readonly CheckBox _showMainCli = new() { Text = "Show ping log in the main window", AutoSize = true };
    private readonly CheckBox _wallboardCli = new() { Text = "Show ping log when Wallboard opens", AutoSize = true };

    private readonly NumericUpDown _interval = Number(1, 300, 2, 1, 90);
    private readonly NumericUpDown _timeout = Number(250, 10000, 1000, 250, 110);
    private readonly NumericUpDown _downAfter = Number(2, 20, 3, 1, 90);
    private readonly NumericUpDown _recoverAfter = Number(1, 20, 2, 1, 90);
    private readonly Label _monitorLockLabel = new() { AutoSize = true };

    private readonly ComboBox _historyRange = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        Width = 180
    };
    private readonly CheckBox _hideSuspects = new() { Text = "Hide SUSPECT events by default", AutoSize = true };
    private readonly Button _openHistoryButton = new AuroraButton() { Text = "Open history", AutoSize = true };

    private readonly CheckBox _autoUpdates = new() { Text = "Check automatically on startup and every 6 hours", AutoSize = true };
    private readonly CheckBox _showHomeUpdateControl = new() { Text = "Developer: show update control in the main UI", AutoSize = true };
    private readonly Label _versionValue = new() { AutoSize = true, Font = new Font("Segoe UI Semibold", 10) };
    private readonly Label _updateStatus = new() { AutoSize = false, Height = 48, Width = 560 };
    private readonly Button _updateButton = new AuroraButton() { Text = "Check for Updates", AutoSize = true };

    private readonly Dictionary<string, Control> _pages = new(StringComparer.OrdinalIgnoreCase);
    private bool _loading;
    private AppSettingsSnapshot? _baseline;
    private readonly Label _saveStatus = new() { AutoSize = true, Padding = new Padding(0, 8, 12, 0) };
    private readonly CheckBox _autoStart = new() { Text = "Start monitoring automatically when the app opens", AutoSize = true };
    private readonly Button _resetButton = new AuroraButton() { Text = "Restore default settings…", AutoSize = true };

    public SettingsForm(
        Func<AppSettingsSnapshot> provider,
        Action<AppSettingsSnapshot> apply,
        Func<Task> updateAction,
        Action openHistory)
    {
        _provider = provider;
        _apply = apply;
        _updateAction = updateAction;
        _openHistory = openHistory;

        Text = "Settings • Ping Watchdog";
        Width = 900;
        Height = 650;
        MinimumSize = new Size(760, 540);
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9.5f);
        BackColor = Color.FromArgb(10, 14, 20);
        ForeColor = Color.FromArgb(234, 240, 246);

        _historyRange.Items.AddRange(new object[]
        {
            "Last 24 hours",
            "Last 7 days",
            "Last 30 days",
            "All time"
        });

        _appearance.Items.AddRange(new object[] { "System", "Light", "Dark" });
        _appearance.SelectedIndex = 0;
        _appearance.SelectedIndexChanged += (_, _) => UpdateDirtyState();
        BuildLayout();
        ApplyTheme();
        Shown += (_, _) => AuroraTheme.Register(this);

        _nav.Items.AddRange(new object[] { "General", "Appearance", "Monitoring", "Notifications", "History", "Updates", "Advanced", "About" });
        _nav.DrawItem += DrawCategory;
        _nav.SelectedIndexChanged += (_, _) => ShowSelectedPage();
        _nav.SelectedIndex = 0;

        foreach (var check in new[] { _autoStart, _restoreLastSite, _use12HourTime, _minimizeToTray,
            _notifications, _showMainCli, _wallboardCli, _hideSuspects, _autoUpdates, _showHomeUpdateControl })
            check.CheckedChanged += (_, _) => UpdateDirtyState();
        foreach (var number in new[] { _interval, _timeout, _downAfter, _recoverAfter })
            number.ValueChanged += (_, _) => UpdateDirtyState();
        _historyRange.SelectedIndexChanged += (_, _) => UpdateDirtyState();
        KeyPreview = true;
        AcceptButton = _saveButton;
        CancelButton = _closeButton;
        _saveDetails.Click += (_, _) => MessageBox.Show(this, _saveError ?? "No save error.", "Settings could not be saved", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        KeyDown += (_, e) => { if (e.Control && e.KeyCode == Keys.S) { SaveSettings(); e.SuppressKeyPress = true; } };
        FormClosing += (_, e) =>
        {
            if (!HasChanges()) return;
            var answer = MessageBox.Show(this, "Save your settings before closing?", "Unsaved settings",
                MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (answer == DialogResult.Cancel) e.Cancel = true;
            else if (answer == DialogResult.Yes) { SaveSettings(); e.Cancel = HasChanges(); }
        };
        _resetButton.Click += (_, _) => RestoreDefaults();
        _saveButton.Click += (_, _) => SaveSettings();
        _closeButton.Click += (_, _) => Close();
        _openHistoryButton.Click += (_, _) => _openHistory();
        _updateButton.Click += async (_, _) =>
        {
            _updateButton.Enabled = false;
            try { await _updateAction(); }
            finally
            {
                if (!IsDisposed && !Disposing)
                    _updateButton.Enabled = true;
                RefreshRuntimeState();
            }
        };

        Shown += (_, _) =>
        {
            LoadSettings();
            _runtimeTimer.Start();
        };
        _runtimeTimer.Tick += (_, _) => RefreshRuntimeState();
        FormClosed += (_, _) =>
        {
            _runtimeTimer.Stop();
            _runtimeTimer.Dispose();
        };
    }

    internal void RefreshRuntimeState()
    {
        if (IsDisposed || Disposing)
            return;

        var snapshot = _provider();
        _versionValue.Text = snapshot.Version;
        _updateStatus.Text = snapshot.UpdateStatus;
        _updateButton.Text = snapshot.UpdateActionText;

        bool editable = !snapshot.MonitoringActive;
        foreach (var control in new Control[] { _interval, _timeout, _downAfter, _recoverAfter })
            control.Enabled = editable;

        _monitorLockLabel.Text = editable
            ? "Changes apply to the next monitoring session."
            : "Monitoring is active. Stop monitoring to change timing and outage thresholds.";
        _monitorLockLabel.ForeColor = editable
            ? Color.FromArgb(139, 153, 169)
            : Color.FromArgb(245, 191, 71);
    }

    private static NumericUpDown Number(decimal min, decimal max, decimal value, decimal increment, int width)
    {
        return new NumericUpDown
        {
            Minimum = min,
            Maximum = max,
            Value = value,
            Increment = increment,
            Width = width,
            BorderStyle = BorderStyle.FixedSingle
        };
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 3,
            Padding = new Padding(0),
            Margin = new Padding(0)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));

        var header = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(12, 18, 26)
        };
        header.Controls.Add(new Label
        {
            Text = "Settings",
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 20, FontStyle.Bold),
            ForeColor = Color.White,
            Location = new Point(24, 14)
        });
        root.Controls.Add(header, 0, 0);
        root.SetColumnSpan(header, 2);

        var navPanel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12, 16, 10, 16),
            BackColor = Color.FromArgb(14, 20, 28)
        };
        navPanel.Controls.Add(_nav);
        root.Controls.Add(navPanel, 0, 1);

        _content.Padding = new Padding(24, 18, 24, 18);
        root.Controls.Add(_content, 1, 1);

        _pages["General"] = BuildGeneralPage();
        _pages["Appearance"] = BuildDisplayPage();
        _pages["Notifications"] = BuildNotificationsPage();
        _pages["Advanced"] = BuildAdvancedPage();
        _pages["Monitoring"] = BuildMonitoringPage();
        _pages["History"] = BuildHistoryPage();
        _pages["Updates"] = BuildUpdatesPage();
        _pages["About"] = BuildAboutPage();

        foreach (var page in _pages.Values)
        {
            page.Dock = DockStyle.Fill;
            page.Visible = false;
            _content.Controls.Add(page);
        }

        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(18, 14, 22, 10),
            BackColor = Color.FromArgb(12, 18, 26)
        };
        footer.Controls.Add(_closeButton);
        footer.Controls.Add(_saveButton);
        footer.Controls.Add(_saveDetails);
        footer.Controls.Add(_saveStatus);
        root.Controls.Add(footer, 0, 2);
        root.SetColumnSpan(footer, 2);

        Controls.Add(root);
    }

    internal void SelectCategory(string category)
    {
        int index = _nav.Items.IndexOf(category);
        if (index >= 0) _nav.SelectedIndex = index;
    }

    private Control BuildGeneralPage()
    {
        var page = Page("General", "Application behavior and display defaults.");
        var stack = (FlowLayoutPanel)page.Controls[1];

        stack.Controls.Add(Section(
            "Window behavior",
            _minimizeToTray,
            "When enabled, minimizing Ping Watchdog hides it to the notification area while monitoring continues."));
        stack.Controls.Add(Section("Startup view", _restoreLastSite, "All Hosts opens by default. Enable this to reopen the site you last selected."));
        stack.Controls.Add(Section("Monitoring startup", _autoStart, "When enabled, saved hosts start monitoring on launch. Disabled hosts remain excluded."));

        return page;
    }

    private Control BuildDisplayPage()
    {
        var page = Page("Appearance", "Clock format and ping-log visibility.");
        var stack = (FlowLayoutPanel)page.Controls[1];
        stack.Controls.Add(Section("Appearance", _appearance, "System follows Windows. Changes apply when saved."));
        stack.Controls.Add(Section("Time display", _use12HourTime, "Applies to host timestamps, history, CLI trace, and the Wallboard clock."));
        stack.Controls.Add(Section("Ping log", new Control[] { _showMainCli, _wallboardCli },
            "Choose where ping commands and replies are shown by default."));
        return page;
    }

    private Control BuildNotificationsPage()
    {
        var page = Page("Notifications", "Outage alerts and temporary quiet periods.");
        var stack = (FlowLayoutPanel)page.Controls[1];
        stack.Controls.Add(Section("Windows notifications", _notifications,
            "Receive notifications when a host goes down or recovers. Monitoring and history continue when alerts are off."));
        stack.Controls.Add(new Label { Text = "Right-click a host to snooze it for an hour. Pings and alerts pause until it resumes automatically. Resume monitoring ends the snooze early.",
            AutoSize = true, MaximumSize = new Size(540, 0), Margin = new Padding(0, 8, 0, 8) });
        return page;
    }

    private Control BuildAdvancedPage()
    {
        var page = Page("Advanced", "Preference recovery.");
        var stack = (FlowLayoutPanel)page.Controls[1];
        stack.Controls.Add(Section("Restore preferences", _resetButton,
            "Stages the default settings for review. Save changes applies them. Hosts, site organization, and outage history are preserved. Active-session timing stays unchanged."));
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
            Text = "These filters change what Wallboard and Outage History show. They never delete the underlying events.",
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
        stack.Controls.Add(_autoUpdates);

        _showHomeUpdateControl.Margin = new Padding(0, 4, 0, 14);


        stack.Controls.Add(_updateButton);
        stack.Controls.Add(new Label
        {
            Text = "Updates can run in the background. Check for updates is also available in the sidebar.",
            AutoSize = true,
            ForeColor = Color.FromArgb(139, 153, 169),
            Margin = new Padding(0, 12, 0, 0)
        });
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
        var license = new AuroraButton { Text = "View license", AutoSize = true };
        var issue = new AuroraButton { Text = "Report an issue…", AutoSize = true };
        var diagnostics = new AuroraButton { Text = "Review diagnostic info…", AutoSize = true };
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
        stack.Controls.Add(license);
        stack.Controls.Add(issue);
        stack.Controls.Add(diagnostics);
        stack.Controls.Add(status);
        return page;
    }

    internal async Task CaptureAboutReview(string output, string appearance)
    {
        SelectCategory("About");
        await Task.Delay(75);
        using (var bitmap = new Bitmap(Width, Height))
        {
            DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            bitmap.Save(Path.Combine(output, "support-about-" + appearance + ".png"));
        }
        using var review = SupportInfo.CreateReview("Diagnostic information", SupportInfo.CreateDiagnostics(_provider().Version), true);
        review.Show(this);
        await Task.Delay(75);
        using (var bitmap = new Bitmap(review.Width, review.Height))
        {
            review.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            bitmap.Save(Path.Combine(output, "support-diagnostics-" + appearance + ".png"));
        }
        review.Close();
    }

    private Control Page(string title, string subtitle)
    {
        var page = new TableLayoutPanel
        {
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Color.FromArgb(10, 14, 20)
        };
        page.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        page.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var head = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Margin = new Padding(0)
        };
        head.Controls.Add(new Label
        {
            Text = title,
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 16, FontStyle.Bold),
            ForeColor = Color.White
        });

        var stack = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Margin = new Padding(0)
        };
        void FitPage()
        {
            int width = Math.Max(100, stack.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 6);
            foreach (Control child in stack.Controls)
            {
                child.MinimumSize = new Size(width, child.MinimumSize.Height);
                child.MaximumSize = new Size(width, 0);
                foreach (var label in child.Controls.OfType<Label>())
                {
                    label.MaximumSize = new Size(Math.Max(80, width - child.Padding.Horizontal - 10), 0);
                    if (label == _updateStatus) label.AutoSize = true;
                }
            }
            foreach (var label in head.Controls.OfType<Label>()) label.MaximumSize = new Size(Math.Max(80, head.ClientSize.Width), 0);
        }
        stack.SizeChanged += (_, _) => FitPage();
        page.VisibleChanged += (_, _) => FitPage();
        page.Controls.Add(head, 0, 0);
        page.Controls.Add(stack, 0, 1);
        return page;
    }

    private static Control Section(string title, Control content, string description) =>
        Section(title, new[] { content }, description);

    private static Control Section(string title, IEnumerable<Control> controls, string description)
    {
        var panel = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(AuroraTheme.Space4),
            Margin = new Padding(0, 0, 0, 12),
            BackColor = Color.FromArgb(18, 25, 34)
        };
        panel.Controls.Add(new Label
        {
            Text = title,
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 10.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(234, 240, 246)
        });
        foreach (var control in controls)
        {
            control.Margin = new Padding(0, 8, 0, 0);
            panel.Controls.Add(control);
        }
        panel.Controls.Add(new Label
        {
            Text = description,
            AutoSize = true,
            MaximumSize = new Size(540, 0),
            ForeColor = Color.FromArgb(139, 153, 169),
            Margin = new Padding(0, 8, 0, 0)
        });
        return panel;
    }

    private static Control SettingRow(string label, Control input, string suffix)
    {
        var row = new FlowLayoutPanel
        {
            AutoSize = true,
            MinimumSize = new Size(0, 48),
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0, 7, 0, 7),
            Margin = new Padding(0, 0, 0, 8)
        };
        row.Controls.Add(new Label
        {
            Text = label,
            Width = 205,
            Height = 32,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.FromArgb(220, 228, 237)
        });
        input.Margin = new Padding(0, 2, 8, 0);
        row.Controls.Add(input);
        if (!string.IsNullOrWhiteSpace(suffix))
        {
            row.Controls.Add(new Label
            {
                Text = suffix,
                AutoSize = true,
                Padding = new Padding(0, 8, 0, 0),
                ForeColor = Color.FromArgb(139, 153, 169)
            });
        }
        return row;
    }

    private static Control LabeledValue(string label, Control value)
    {
        var panel = new FlowLayoutPanel
        {
            AutoSize = true,
            MinimumSize = new Size(0, 58),
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Margin = new Padding(0, 0, 0, 10)
        };
        panel.Controls.Add(new Label
        {
            Text = label,
            AutoSize = true,
            ForeColor = Color.FromArgb(139, 153, 169)
        });
        panel.Controls.Add(value);
        return panel;
    }

    private void ApplyTheme()
    {
        ((AuroraButton)_saveButton).Primary = true;
        AuroraTheme.Apply(this);
    }

    private void ShowSelectedPage()
    {
        string selected = _nav.SelectedItem?.ToString() ?? "General";
        foreach (var pair in _pages)
            pair.Value.Visible = pair.Key.Equals(selected, StringComparison.OrdinalIgnoreCase);
        if (_pages.TryGetValue(selected, out var page))
            page.BringToFront();
    }

    private void LoadSettings()
    {
        _loading = true;
        _saveError = null;
        try
        {
            var s = _provider();
            _baseline = s;
            _interval.Value = Clamp(_interval, s.PingIntervalSeconds);
            _timeout.Value = Clamp(_timeout, s.PingTimeoutMs);
            _downAfter.Value = Clamp(_downAfter, s.FailureThreshold);
            _recoverAfter.Value = Clamp(_recoverAfter, s.RecoveryThreshold);
            _showMainCli.Checked = s.ShowCommandView;
            _wallboardCli.Checked = s.WallboardShowCli;
            _minimizeToTray.Checked = s.MinimizeToTray;
            _notifications.Checked = s.NotificationsEnabled;
            _use12HourTime.Checked = s.Use12HourTime;
            _appearance.SelectedIndex = Enum.IsDefined(s.Appearance) ? (int)s.Appearance : 0;
            _restoreLastSite.Checked = s.RestoreLastSiteOnStartup;
            _autoStart.Checked = s.StartMonitoringOnLaunch;
            _autoUpdates.Checked = s.AutoCheckUpdates;
            _showHomeUpdateControl.Checked = s.ShowUpdateControlOnHome;
            _historyRange.SelectedIndex = HistoryHoursToIndex(s.EventHistoryHours);
            _hideSuspects.Checked = s.HideSuspectEvents;
            RefreshRuntimeState();
        }
        finally { _loading = false; UpdateDirtyState(); }
    }

    private AppSettingsSnapshot ReadSettings(AppSettingsSnapshot current) => current with
    {
        PingIntervalSeconds = (int)_interval.Value, PingTimeoutMs = (int)_timeout.Value,
        FailureThreshold = (int)_downAfter.Value, RecoveryThreshold = (int)_recoverAfter.Value,
        ShowCommandView = _showMainCli.Checked, WallboardShowCli = _wallboardCli.Checked,
        MinimizeToTray = _minimizeToTray.Checked, NotificationsEnabled = _notifications.Checked,
        AutoCheckUpdates = _autoUpdates.Checked, ShowUpdateControlOnHome = _showHomeUpdateControl.Checked,
        EventHistoryHours = IndexToHistoryHours(_historyRange.SelectedIndex),
        Use12HourTime = _use12HourTime.Checked, RestoreLastSiteOnStartup = _restoreLastSite.Checked,
        Appearance = (AppearanceMode)Math.Max(0, _appearance.SelectedIndex),
        StartMonitoringOnLaunch = _autoStart.Checked, HideSuspectEvents = _hideSuspects.Checked
    };
    private bool HasChanges() => !_loading && _baseline != null && ReadSettings(_baseline) != _baseline;
    private void UpdateDirtyState()
    {
        if (_loading) return;
        bool dirty = HasChanges();
        if (!dirty) _saveError = null;
        _saveButton.Enabled = dirty;
        _saveStatus.Text = _saveError != null ? "Could not save" : dirty ? "Unsaved changes" : "Saved";
        _saveStatus.AccessibleName = _saveStatus.Text;
        _saveStatus.AccessibleDescription = _saveError;
        _saveDetails.Visible = _saveError != null;
    }
    private bool SaveSettings()
    {
        if (_loading || !HasChanges()) return true;
        try
        {
            _apply(ReadSettings(_provider()));
            LoadSettings();
            return true;
        }
        catch (Exception ex)
        {
            _saveError = ex.Message;
            UpdateDirtyState();
            return false;
        }
    }

    private void DrawCategory(object? sender, DrawItemEventArgs e)
    {
        using var background = new SolidBrush(AuroraTheme.Palette.Surface);
        e.Graphics.FillRectangle(background, e.Bounds);
        if (e.Index < 0 || e.Index >= _nav.Items.Count) return;
        bool selected = (e.State & DrawItemState.Selected) != 0;
        var bounds = new Rectangle(e.Bounds.X + 4, e.Bounds.Y + 2, Math.Max(1, e.Bounds.Width - 8), Math.Max(1, e.Bounds.Height - 4));
        using var shape = WallboardStyle.Rounded(bounds, AuroraTheme.RadiusMedium);
        if (selected)
        {
            using var fill = new SolidBrush(AuroraTheme.Palette.Surface3);
            e.Graphics.FillPath(fill, shape);
        }
        TextRenderer.DrawText(e.Graphics, _nav.Items[e.Index].ToString(), AuroraTheme.LabelFont,
            new Rectangle(bounds.X + 12, bounds.Y, Math.Max(1, bounds.Width - 24), bounds.Height),
            selected ? AuroraTheme.Palette.Text : AuroraTheme.Palette.Secondary,
            TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        if ((e.State & DrawItemState.Focus) != 0 && _nav.Focused)
        {
            using var focus = new Pen(AuroraTheme.Palette.Accent, 2);
            e.Graphics.DrawPath(focus, shape);
        }
    }

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
    }

    internal static async Task CaptureSaveFailureReview(AppSettingsSnapshot initial, Form owner, string path)
    {
        using var review = new SettingsForm(() => initial, _ => throw new IOException(
            "The settings file is locked. Close the other application and try Save changes again."),
            () => Task.CompletedTask, () => { });
        review.Width = 760;
        review.Show(owner);
        await Task.Delay(75); // Allow Shown/load and native layout before changing the draft.
        review._autoStart.Checked = !initial.StartMonitoringOnLaunch;
        review.SaveSettings();
        review.PerformLayout();
        foreach (var button in new[] { review._saveButton, review._saveDetails, review._closeButton })
            if (!button.Visible || !button.Parent!.ClientRectangle.Contains(button.Bounds))
                throw new InvalidOperationException($"Settings save-error control {button.Text}: visible={button.Visible}, bounds={button.Bounds}, parent={button.Parent?.ClientRectangle}.");
        using (var bitmap = new Bitmap(review.Width, review.Height))
        {
            review.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            bitmap.Save(path);
        }
        review.LoadSettings();
        review.Close();
    }

    private void RestoreDefaults()
    {
        if (MessageBox.Show(this, "Stage the default settings? Review them, then click Save changes to apply.",
            "Restore defaults", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        if (!_provider().MonitoringActive)
        { _interval.Value = 2; _timeout.Value = 1000; _downAfter.Value = 3; _recoverAfter.Value = 2; }
        _minimizeToTray.Checked = true; _restoreLastSite.Checked = false; _autoStart.Checked = false;
        _appearance.SelectedIndex = 0;
        _use12HourTime.Checked = false; _notifications.Checked = true;
        _showMainCli.Checked = true; _wallboardCli.Checked = true;
        _autoUpdates.Checked = true; _showHomeUpdateControl.Checked = false;
        _historyRange.SelectedIndex = 0; _hideSuspects.Checked = true;
        UpdateDirtyState();
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

    private static decimal Clamp(NumericUpDown control, int value) =>
        Math.Clamp(value, (int)control.Minimum, (int)control.Maximum);

    private static int HistoryHoursToIndex(int hours) => hours switch
    {
        24 => 0,
        168 => 1,
        720 => 2,
        _ => 3
    };

    private static int IndexToHistoryHours(int index) => index switch
    {
        0 => 24,
        1 => 168,
        2 => 720,
        _ => 0
    };
}
