using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;

namespace PingWatchdog;

internal static class SupportInfo
{
    internal const string IssueUrl = "https://github.com/JPLuker/Ping-Watchdog/issues/new/choose";

    internal static string BuildIdentity =>
        typeof(SupportInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(SupportInfo).Assembly.GetName().Version?.ToString() ?? "Unavailable";

    internal static string CreateDiagnostics(string version)
    {
        // Deliberately do not read configuration, host inventory, history or recovery reports.
        string data = StoragePaths.Current.DataDirectory;
        string recovery = StoragePaths.Current.StartupDirectory;
        return string.Join(Environment.NewLine, new[]
        {
            $"Ping Watchdog {version}",
            $"Build: {BuildIdentity}",
            $"Windows: {RuntimeInformation.OSDescription}",
            $"Runtime: {RuntimeInformation.FrameworkDescription}",
            $"Process architecture: {RuntimeInformation.ProcessArchitecture}",
            $"OS architecture: {RuntimeInformation.OSArchitecture}",
            $"Storage mode: {(StoragePaths.Current.Portable ? "Portable" : "Per-user")}",
            $"Data folder: {data}",
            $"Configuration: {Path.Combine(data, "autosave.pingwatch.json")}",
            $"Preferences: {Path.Combine(data, "preferences.json")}",
            $"History: {Path.Combine(data, "event-history.json")}",
            $"Startup reports: {recovery}",
            $"Logs: {StoragePaths.Current.LogDirectory}"
        });
    }

    internal static string ReadLicense()
    {
        using var stream = typeof(SupportInfo).Assembly.GetManifestResourceStream("PingWatchdog.License")
            ?? throw new InvalidOperationException("The bundled license could not be found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    internal static void OpenIssue() =>
        Process.Start(new ProcessStartInfo(IssueUrl) { UseShellExecute = true });

    internal static Form CreateReview(string title, string content, bool allowCopy,
        Action<string>? copy = null)
    {
        var form = new Form
        {
            Text = title + " • Ping Watchdog", Width = 760, Height = 540,
            MinimumSize = new Size(600, 400), StartPosition = FormStartPosition.CenterParent,
            AutoScaleMode = AutoScaleMode.Dpi, Font = AppTheme.LabelFont
        };
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(20)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var explanation = new Label
        {
            Text = allowCopy
                ? "Review before copying. Folder paths can include your Windows username. Nothing is sent automatically."
                : "License included with this release.",
            AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 12)
        };
        var text = new TextBox
        {
            Text = content, Multiline = true, ReadOnly = true, Dock = DockStyle.Fill,
            ScrollBars = ScrollBars.Both, WordWrap = false, BorderStyle = BorderStyle.FixedSingle,
            AccessibleName = allowCopy ? "Diagnostic information" : "License text",
            Margin = new Padding(0)
        };
        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true, Margin = new Padding(0, 12, 0, 0)
        };
        var copyButton = new ThemeButton { Text = "Copy diagnostic info", AutoSize = true };
        var close = new ThemeButton { Text = "Close", AutoSize = true, DialogResult = DialogResult.Cancel };
        var status = new Label { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 8, 0, 0), AccessibleName = "Copy status" };
        copyButton.Click += (_, _) =>
        {
            try
            {
                (copy ?? (value => Clipboard.SetText(value)))(text.Text);
                status.Text = "Copied";
            }
            catch (Exception ex)
            {
                status.Text = "Could not copy. Select the text and try Ctrl+C.";
                status.AccessibleDescription = ex.Message;
            }
        };
        if (allowCopy) footer.Controls.Add(copyButton);
        footer.Controls.Add(close);
        root.Controls.Add(status, 0, 3);
        root.Controls.Add(explanation, 0, 0);
        root.Controls.Add(text, 0, 1);
        root.Controls.Add(footer, 0, 2);
        form.Controls.Add(root);
        form.CancelButton = close;
        close.Click += (_, _) => form.Close();
        AppTheme.Apply(form);
        form.Shown += (_, _) => { AppTheme.Register(form); text.Focus(); text.Select(0, 0); };
        return form;
    }

    internal static void RunTests()
    {
        string report = CreateDiagnostics("test-version");
        if (!report.Contains("test-version") || !report.Contains("Process architecture:") ||
            !report.Contains("Startup reports:") || !ReadLicense().Contains("PROPRIETARY LICENSE"))
            throw new InvalidOperationException("Support information is incomplete.");
        string? copied = null;
        using var review = CreateReview("Diagnostics", report, true, value => copied = value);
        var root = (TableLayoutPanel)review.Controls[0];
        var footer = (FlowLayoutPanel)root.GetControlFromPosition(0, 2)!;
        var button = (Button)footer.Controls[0];
        // Invoke the same action without showing or changing the real clipboard.
        review.Show();
        button.PerformClick();
        if (copied != report || root.GetControlFromPosition(0, 3)!.Text != "Copied")
            throw new InvalidOperationException("Diagnostic copying changed the report.");
        review.Close();
        using var failed = CreateReview("Diagnostics", report, true, _ => throw new IOException("Clipboard unavailable"));
        failed.Show();
        var failedFooter = (FlowLayoutPanel)((TableLayoutPanel)failed.Controls[0]).GetControlFromPosition(0, 2)!;
        ((Button)failedFooter.Controls[0]).PerformClick();
        if (!((TableLayoutPanel)failed.Controls[0]).GetControlFromPosition(0, 3)!.Text.StartsWith("Could not copy"))
            throw new InvalidOperationException("Clipboard failure was not reported.");
        failed.Close();
    }
}
