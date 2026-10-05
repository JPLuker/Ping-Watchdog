using System.Diagnostics;
using Velopack;
using Velopack.Sources;

namespace PingWatchdog;

internal interface IRecoveryUpdates
{
    Task<string?> CheckAsync(CancellationToken cancellation);
    Task DownloadAsync(Action<int> progress, CancellationToken cancellation);
    void Apply();
}

/// <summary>Only the updater is used here; no MainForm, notifications, configuration, or brand assets.</summary>
internal sealed class RecoveryUpdates : IRecoveryUpdates
{
    private UpdateManager? _manager;
    private UpdateInfo? _update;

    public async Task<string?> CheckAsync(CancellationToken cancellation)
    {
        Program.InitializeUpdater(Array.Empty<string>());
        _manager = new UpdateManager(new GithubSource("https://github.com/JPLuker/Ping-Watchdog",
            accessToken: null, prerelease: false));
        if (!_manager.IsInstalled)
            throw new InvalidOperationException("This copy cannot update itself. Use Open downloads to install the current Windows release.");
        _update = await _manager.CheckForUpdatesAsync().WaitAsync(cancellation);
        return _update?.TargetFullRelease.Version.ToString();
    }

    public Task DownloadAsync(Action<int> progress, CancellationToken cancellation) =>
        (_manager ?? throw new InvalidOperationException("Check for updates first."))
            .DownloadUpdatesAsync(_update ?? throw new InvalidOperationException("No newer update is available."), progress, cancellation);

    public void Apply() => (_manager ?? throw new InvalidOperationException("No update is ready."))
        .ApplyUpdatesAndRestart(_update ?? throw new InvalidOperationException("No update is ready."), Array.Empty<string>());
}

internal sealed class RecoveryForm : Form
{
    private readonly IRecoveryUpdates _updates;
    private readonly CancellationTokenSource _lifetime = new();
    internal readonly Button UpdateButton = new() { Text = "Check for updates", AutoSize = true };
    internal readonly Label StatusLabel = new() { AutoSize = true, MaximumSize = new Size(680, 0) };
    internal readonly TextBox Details = new()
    {
        Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false,
        Dock = DockStyle.Fill, Font = new Font("Consolas", 9)
    };
    private readonly Button _retry = new() { Text = "Retry startup", AutoSize = true };
    private bool _busy;
    private bool _resourcesDisposed;
    internal bool UpdateReady { get; private set; }

    internal RecoveryForm(string report, string reportPath, IRecoveryUpdates? updates = null)
    {
        _updates = updates ?? new RecoveryUpdates();
        Text = "Ping Watchdog — Startup recovery";
        Icon = SystemIcons.Warning;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(760, 490);
        MinimumSize = new Size(660, 430);
        Font = new Font("Segoe UI", 9);
        AutoScaleMode = AutoScaleMode.Dpi;
        Details.Text = report;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(18) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(new Label
        {
            Text = "Ping Watchdog could not continue", AutoSize = true,
            Font = new Font("Segoe UI", 15, FontStyle.Bold), Margin = new Padding(0, 0, 0, 10)
        }, 0, 0);
        root.Controls.Add(new Label
        {
            Text = "The main app stopped unexpectedly. This recovery window can check for a newer release without opening the dashboard.\r\nYour saved sites and settings have not been reset.",
            AutoSize = true, MaximumSize = new Size(680, 0), Margin = new Padding(0, 0, 0, 12)
        }, 0, 1);
        root.Controls.Add(Details, 0, 2);
        StatusLabel.Text = "Error report: " + reportPath;
        StatusLabel.Margin = new Padding(0, 10, 0, 10);
        root.Controls.Add(StatusLabel, 0, 3);
        var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true, Margin = Padding.Empty };
        var downloads = new Button { Text = "Open downloads", AutoSize = true };
        var copy = new Button { Text = "Copy details", AutoSize = true };
        var close = new Button { Text = "Close", AutoSize = true };
        foreach (var button in new[] { UpdateButton, downloads, copy, _retry, close })
        {
            button.Padding = new Padding(8, 4, 8, 4);
            button.Margin = new Padding(0, 0, 6, 4);
            actions.Controls.Add(button);
        }
        root.Controls.Add(actions, 0, 4);
        Controls.Add(root);
        UpdateButton.Click += async (_, _) => await UpdateClickedAsync();
        downloads.Click += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo(StartupRecovery.ReleasesUrl) { UseShellExecute = true }); }
            catch (Exception ex) { StatusLabel.Text = "Could not open your browser: " + ex.Message; }
        };
        copy.Click += (_, _) =>
        {
            try { Clipboard.SetText(Details.Text); StatusLabel.Text = "Error details copied."; }
            catch (Exception ex) { StatusLabel.Text = "Could not copy details: " + ex.Message; }
        };
        _retry.Click += (_, _) =>
        {
            try { using var process = StartupRecovery.StartProcess(); Close(); }
            catch (Exception ex) { StatusLabel.Text = "Could not restart: " + ex.Message; }
        };
        close.Click += (_, _) => Close();
        FormClosed += (_, _) => _lifetime.Cancel();
    }

    internal async Task UpdateClickedAsync()
    {
        if (_busy) return;
        _busy = true;
        UpdateButton.Enabled = _retry.Enabled = false;
        try
        {
            if (UpdateReady)
            {
                StatusLabel.Text = "Installing the downloaded update and restarting…";
                _updates.Apply();
                return;
            }
            StatusLabel.Text = "Checking for a newer Windows release…";
            string? version = await _updates.CheckAsync(_lifetime.Token);
            if (version is null)
            {
                StatusLabel.Text = "No newer Windows release is available. You can retry startup or download an installer from the release page.";
                return;
            }
            StatusLabel.Text = $"Downloading version {version}…";
            await _updates.DownloadAsync(progress =>
            {
                if (IsDisposed || !IsHandleCreated) return;
                try { BeginInvoke(() => { if (!IsDisposed) StatusLabel.Text = $"Downloading version {version}: {progress}%"; }); }
                catch (InvalidOperationException) { }
            }, _lifetime.Token);
            UpdateReady = true;
            UpdateButton.Text = "Install update and restart";
            StatusLabel.Text = $"Version {version} is ready. Install it to restart Ping Watchdog.";
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (!IsDisposed) StatusLabel.Text = "Update failed: " + ex.Message + " Use Open downloads if this continues.";
        }
        finally
        {
            _busy = false;
            if (!IsDisposed) UpdateButton.Enabled = _retry.Enabled = true;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_resourcesDisposed)
        {
            _resourcesDisposed = true;
            _lifetime.Cancel();
            _lifetime.Dispose();
        }
        base.Dispose(disposing);
    }
}
