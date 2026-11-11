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

// One-field and two-field dialogs for naming hosts and sites.
internal sealed class NicknameDialog : Form
{
    private readonly PlainTextBox _box = new() { AccessibleName = "Label", BorderStyle = BorderStyle.FixedSingle };
    public string Nickname => _box.Text.Trim();

    public NicknameDialog(string host, string current)
    {
        _box.Text = current;
        CompactDialog.Build(this, $"Set label • {host}", new Label { Text = "Label" }, _box, "Save label");
    }
}

// #40: "I tried to adjust a hosts address cuz DHCP and it only asks me to change the label".
// One dialog edits both. The address must be valid; history stays with the old address.
internal sealed class HostEditDialog : Form
{
    private readonly PlainTextBox _address = new() { AccessibleName = "Address", BorderStyle = BorderStyle.FixedSingle };
    private readonly PlainTextBox _label = new() { AccessibleName = "Label", BorderStyle = BorderStyle.FixedSingle };
    private readonly Label _addressPrompt = new() { Text = "Address" };
    public string Address => HostImport.Normalize(ClipboardText.NormalizeLine(_address.Text));
    public string Nickname => _label.Text.Trim();

    public HostEditDialog(string address, string label, Func<string, string?> addressError)
    {
        _address.Text = address; _label.Text = label;
        CompactDialog.Build(this, $"Edit host • {address}", new[] { (_addressPrompt, (Control)_address), (new Label { Text = "Label" }, (Control)_label) }, "Save");
        FormClosing += (_, e) =>
        {
            if (DialogResult != DialogResult.OK) return;
            string? error = HostImport.Valid(_address.Text.Trim()) ? addressError(Address) : "Enter a valid IP address or host name";
            if (error is null) return;
            e.Cancel = true;
            _addressPrompt.Text = error; _addressPrompt.Tag = "statOffline"; AppTheme.Apply(this);
            _address.Focus(); _address.SelectAll();
        };
        _address.TextChanged += (_, _) =>
        {
            if (_addressPrompt.Tag is null) return;
            _addressPrompt.Text = "Address"; _addressPrompt.Tag = null; AppTheme.Apply(this);
        };
    }

    internal void SetForTest(string address, string label) { _address.Text = address; _label.Text = label; }
    internal string PromptForTest => _addressPrompt.Text;
}

internal sealed class SiteNameDialog : Form
{
    private readonly PlainTextBox _box = new() { AccessibleName = "Site or group name", BorderStyle = BorderStyle.FixedSingle };
    private readonly Label _prompt = new() { Text = "Site / group name" };
    public string SiteName => ClipboardText.NormalizeLine(_box.Text);

    public SiteNameDialog(string title, string actionText, string current = "")
    {
        _box.Text = current;
        CompactDialog.Build(this, title, _prompt, _box, actionText);
        _box.PasteBusy += () => { _prompt.Text = "Clipboard busy, try again"; _prompt.Tag = "statSuspect"; AppTheme.Apply(this); };
        _box.TextChanged += (_, _) =>
        {
            if (_prompt.Tag is null) return;
            _prompt.Text = "Site / group name"; _prompt.Tag = null; AppTheme.Apply(this);
        };
    }
}
