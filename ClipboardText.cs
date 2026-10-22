using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace PingWatchdog;

internal readonly record struct ClipboardRead(string? Text, bool Busy);

// Reads clipboard text the way people expect "paste" to work. Remote sessions and other apps can hold
// the clipboard for a moment, and chat or web copies may carry only HTML/RTF or hidden characters;
// a plain text box silently pastes nothing in those cases.
internal static partial class ClipboardText
{
    internal static ClipboardRead Read(Func<IDataObject?>? source = null, int attempts = 6)
    {
        for (int attempt = 0; attempt < attempts; attempt++)
        {
            try
            {
                var data = source is null ? Clipboard.GetDataObject() : source();
                return new ClipboardRead(data is null ? null : Extract(data), false);
            }
            catch (ExternalException)
            {
                if (attempt + 1 < attempts) Thread.Sleep(40);
            }
        }
        return new ClipboardRead(null, true);
    }

    internal static string? Extract(IDataObject data)
    {
        foreach (var format in new[] { DataFormats.UnicodeText, DataFormats.Text, DataFormats.OemText })
            if (data.GetDataPresent(format) && data.GetData(format) is string text && text.Length > 0)
                return text;
        if (data.GetDataPresent(DataFormats.Html) && data.GetData(DataFormats.Html) is string html && FromHtml(html) is { Length: > 0 } fromHtml)
            return fromHtml;
        if (data.GetDataPresent(DataFormats.Rtf) && data.GetData(DataFormats.Rtf) is string rtf)
        {
            try { using var box = new RichTextBox { Rtf = rtf }; if (box.Text.Length > 0) return box.Text; }
            catch (ArgumentException) { }
        }
        return null;
    }

    // CF_HTML wraps the copied fragment in a header and comment markers.
    internal static string FromHtml(string html)
    {
        int start = html.IndexOf("<!--StartFragment-->", StringComparison.OrdinalIgnoreCase);
        int end = html.IndexOf("<!--EndFragment-->", StringComparison.OrdinalIgnoreCase);
        string fragment = start >= 0 && end > start ? html[(start + 20)..end] : html;
        fragment = BlockTags().Replace(fragment, "\n");
        fragment = AnyTag().Replace(fragment, "");
        return WebUtility.HtmlDecode(fragment).Trim();
    }

    // Keeps line structure (spreadsheet paste) but removes characters people cannot see.
    internal static string NormalizeMultiline(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            if (c is ' ' or ' ' or ' ') builder.Append(' ');
            else if (c is '\r' or '\n' or '\t') builder.Append(c);
            else if (char.IsControl(c) || char.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.Format || c == '­') continue;
            else builder.Append(c);
        }
        return builder.ToString();
    }

    // One clean line for names: no line breaks, tabs, hidden characters or doubled spaces.
    internal static string NormalizeLine(string text) =>
        Whitespace().Replace(NormalizeMultiline(text).Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' '), " ").Trim();

    [GeneratedRegex(@"<\s*(br|/p|/div|/tr|/li)[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex BlockTags();
    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex AnyTag();
    [GeneratedRegex(@" {2,}")]
    private static partial Regex Whitespace();

    // Routes paste in a combo box's edit field through the same reader.
    internal static void AttachPlainPaste(ComboBox combo, Action onBusy)
    {
        combo.HandleCreated += (_, _) =>
        {
            var edit = FindWindowEx(combo.Handle, IntPtr.Zero, "Edit", null);
            if (edit != IntPtr.Zero) new PasteHook(combo, onBusy).AssignHandle(edit);
        };
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string? className, string? windowName);

    private sealed class PasteHook : NativeWindow
    {
        private readonly ComboBox _combo;
        private readonly Action _onBusy;
        internal PasteHook(ComboBox combo, Action onBusy)
        {
            _combo = combo; _onBusy = onBusy;
            combo.HandleDestroyed += (_, _) => ReleaseHandle();
        }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == PlainTextBox.WmPaste)
            {
                var read = Read();
                if (read.Busy) _onBusy();
                else if (read.Text is { } text)
                {
                    string clean = NormalizeLine(text);
                    int start = _combo.SelectionStart;
                    string current = _combo.Text;
                    _combo.Text = current[..start] + clean + current[Math.Min(current.Length, start + _combo.SelectionLength)..];
                    _combo.SelectionStart = start + clean.Length;
                }
                m.Result = IntPtr.Zero;
                return;
            }
            base.WndProc(ref m);
        }
    }
}

// Text box whose Ctrl+V, Shift+Insert and right-click Paste all go through ClipboardText.
internal sealed class PlainTextBox : TextBox
{
    internal const int WmPaste = 0x0302;
    internal bool SingleLineText { get; set; } = true;
    internal event Action? PasteBusy;

    internal void PasteText(string text)
    {
        string clean = SingleLineText ? ClipboardText.NormalizeLine(text) : ClipboardText.NormalizeMultiline(text);
        if (MaxLength > 0 && MaxLength < 32767)
            clean = clean[..Math.Min(clean.Length, Math.Max(0, MaxLength - (TextLength - SelectionLength)))];
        SelectedText = clean;
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmPaste)
        {
            var read = ClipboardText.Read();
            if (read.Busy) PasteBusy?.Invoke();
            else if (read.Text is { } text) PasteText(text);
            m.Result = IntPtr.Zero;
            return;
        }
        base.WndProc(ref m);
    }
}
