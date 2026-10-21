using Microsoft.Win32;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace PingWatchdog;

internal enum AppearanceMode { System, Light, Dark }

// DESIGN.md native adaptation. Dense content uses the required opaque fallback.
internal sealed record AuroraPalette(bool Dark, Color Canvas, Color Surface, Color Surface2,
    Color Surface3, Color Text, Color Secondary, Color Muted, Color Accent, Color OnAccent,
    Color Teal, Color Warning, Color Danger, Color Success)
{
    internal Color StatusText(Color semantic) => Dark ? semantic : Text;
    internal static AuroraPalette Create(bool dark)
    {
        Color C(string value) => ColorTranslator.FromHtml(value);
        return dark
            ? new(true, C("#0B0D16"), C("#151826"), C("#1D2133"), C("#262B40"), C("#F2F3F8"), C("#B8BCD0"), C("#9095AE"), C("#9C8EFF"), C("#0B0D16"), C("#3DD9C4"), C("#F5B547"), C("#FF6B6B"), C("#4ADE95"))
            : new(false, C("#F4F5FA"), C("#FFFFFF"), C("#EEF0F7"), C("#E4E7F1"), C("#14172B"), C("#474D6B"), C("#62688A"), C("#6B5CF0"), C("#FFFFFF"), C("#0F9F8F"), C("#B7791F"), C("#D93B3B"), C("#16935C"));
    }
}

internal static class AuroraTheme
{
    internal const int Space1 = 4, Space2 = 8, Space3 = 12, Space4 = 16, Space6 = 24;
    internal const int RadiusSmall = 10, RadiusMedium = 14, RadiusCard = 20;
    internal static readonly Font BodyFont = new("Segoe UI", 11.25f); // 15 logical px
    internal static readonly Font LabelFont = new("Segoe UI", 10.5f); // 14 logical px
    internal static readonly Font CaptionFont = new("Segoe UI", 9.75f); // 13 logical px
    internal static readonly Font MonoFont = new("Consolas", 9.75f);
    private static readonly HashSet<Form> Windows = new();
    private static Form? _dispatcher;
    internal static AppearanceMode Mode { get; private set; } = AppearanceMode.System;
    internal static AuroraPalette Palette { get; private set; } = Resolve();
    internal static event Action? Changed;

    private static AuroraPalette Resolve()
    {
        if (SystemInformation.HighContrast)
            return new(false, SystemColors.Window, SystemColors.Window, SystemColors.Control, SystemColors.Highlight,
                SystemColors.WindowText, SystemColors.WindowText, SystemColors.GrayText, SystemColors.Highlight,
                SystemColors.HighlightText, SystemColors.WindowText, SystemColors.WindowText, SystemColors.WindowText, SystemColors.WindowText);
        bool dark = Mode == AppearanceMode.Dark;
        if (Mode == AppearanceMode.System)
        {
            try { dark = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1) is int value && value == 0; }
            catch { dark = false; }
        }
        return AuroraPalette.Create(dark);
    }

    internal static void Initialize(Form main)
    {
        if (_dispatcher != null) return;
        _dispatcher = main;
        Application.Idle += DiscoverWindows;
        SystemEvents.UserPreferenceChanged += OnSystemPreferenceChanged;
        main.Disposed += (_, _) =>
        {
            Application.Idle -= DiscoverWindows;
            SystemEvents.UserPreferenceChanged -= OnSystemPreferenceChanged;
            Windows.Clear(); _dispatcher = null;
        };
        Register(main);
    }

    private static void DiscoverWindows(object? sender, EventArgs e)
    {
        foreach (var form in Application.OpenForms.Cast<Form>().ToArray()) Register(form);
    }

    private static void OnSystemPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        var main = _dispatcher;
        if (main == null || main.IsDisposed || !main.IsHandleCreated) return;
        try { main.BeginInvoke((Action)(() => SetMode(Mode))); }
        catch (InvalidOperationException) { }
    }

    internal static void SetMode(AppearanceMode mode)
    {
        Mode = Enum.IsDefined(mode) ? mode : AppearanceMode.System;
        Palette = Resolve();
        foreach (var form in Windows.ToArray())
            if (!form.IsDisposed) Apply(form);
        Changed?.Invoke();
    }

    internal static void Register(Form form)
    {
        if (form.IsDisposed || !Windows.Add(form)) return;
        form.Disposed += (_, _) => Windows.Remove(form);
        form.Shown += (_, _) => Apply(form);
        Apply(form);
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hwnd, string? subAppName, string? subIdList);

    private static void ThemeScrollbars(Control control)
    {
        if (!control.IsHandleCreated) return;
        try
        {
            string? theme = SystemInformation.HighContrast ? null : Palette.Dark ? "DarkMode_Explorer" : "Explorer";
            SetWindowTheme(control.Handle, theme, null);
            foreach (var bar in control.Controls.OfType<ScrollBar>())
                if (bar.IsHandleCreated) SetWindowTheme(bar.Handle, theme, null);
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
    }

    internal static void Apply(Form form)
    {
        if (form.IsDisposed) return;
        var p = Palette;
        form.SuspendLayout();
        try
        {
            form.BackColor = p.Canvas; form.ForeColor = p.Text;
            ApplyControl(form);
            if (form.IsHandleCreated)
            {
                try { int dark = p.Dark ? 1 : 0; DwmSetWindowAttribute(form.Handle, 20, ref dark, sizeof(int)); } catch { }
            }
        }
        finally { form.ResumeLayout(true); form.Invalidate(true); }
    }

    private static void ApplyControl(Control control)
    {
        var p = Palette;
        // Native point sizes are independent of DPI; 9.75pt is the 13 logical-pixel reading floor.
        if (control.Font.SizeInPoints < CaptionFont.SizeInPoints)
            control.Font = control.Font.FontFamily.Name.Equals("Consolas", StringComparison.OrdinalIgnoreCase) ? MonoFont : CaptionFont;
        string role = control.Tag as string ?? "";
        control.ForeColor = p.Text;
        control.BackColor = role is "card" or "nav" or "header" ? p.Surface : p.Canvas;
        switch (control)
        {
            case PictureBox picture:
                picture.BackColor = Color.Transparent;
                break;
            case DataGridView grid:
                grid.EnableHeadersVisualStyles = false;
                grid.BackgroundColor = p.Surface; grid.GridColor = p.Surface2;
                grid.BorderStyle = BorderStyle.None;
                grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
                grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
                grid.DefaultCellStyle.BackColor = p.Surface; grid.DefaultCellStyle.ForeColor = p.Text;
                grid.DefaultCellStyle.SelectionBackColor = p.Surface3; grid.DefaultCellStyle.SelectionForeColor = p.Text;
                grid.DefaultCellStyle.Font = BodyFont;
                grid.RowsDefaultCellStyle.SelectionBackColor = p.Surface3;
                grid.RowsDefaultCellStyle.SelectionForeColor = p.Text;
                grid.AlternatingRowsDefaultCellStyle.SelectionBackColor = p.Surface3;
                grid.AlternatingRowsDefaultCellStyle.SelectionForeColor = p.Text;
                grid.AlternatingRowsDefaultCellStyle.BackColor = p.Surface;
                grid.ColumnHeadersDefaultCellStyle.BackColor = p.Surface2; grid.ColumnHeadersDefaultCellStyle.ForeColor = p.Secondary;
                grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = p.Surface2; grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = p.Secondary;
                grid.ColumnHeadersDefaultCellStyle.Font = CaptionFont;
                grid.RowTemplate.Height = Math.Max(grid.RowTemplate.Height, (int)(36 * grid.DeviceDpi / 96f));
                foreach (DataGridViewRow row in grid.Rows) { row.DefaultCellStyle.BackColor = Color.Empty; row.DefaultCellStyle.ForeColor = Color.Empty; }
                break;
            case AuroraButton button:
                button.Font = LabelFont;
                button.FlatStyle = FlatStyle.Flat; button.FlatAppearance.BorderSize = 0;
                button.BackColor = p.Surface2; button.ForeColor = p.Text;
                break;
            case Button button:
                button.FlatStyle = FlatStyle.Flat; button.FlatAppearance.BorderSize = 0;
                button.FlatAppearance.MouseOverBackColor = p.Surface3;
                button.BackColor = p.Surface2; button.ForeColor = p.Text;
                break;
            case RichTextBox rich:
                rich.Font = MonoFont;
                rich.BackColor = p.Surface; rich.ForeColor = p.Text;
                break;
            case TextBoxBase box:
                box.BackColor = p.Surface; box.ForeColor = p.Text;
                break;
            case ComboBox combo:
                combo.BackColor = p.Surface; combo.ForeColor = p.Text;
                break;
            case NumericUpDown number:
                number.BackColor = p.Surface; number.ForeColor = p.Text;
                break;
            case ListBox list:
                list.BackColor = p.Surface; list.ForeColor = p.Text;
                break;
            case TreeView tree:
                tree.BackColor = p.Surface; tree.ForeColor = p.Text;
                tree.LineColor = p.Muted;
                break;
            case Label label:
                label.BackColor = Color.Transparent;
                label.ForeColor = role switch
                {
                    "statOnline" => p.StatusText(p.Teal), "statSuspect" => p.StatusText(p.Warning),
                    "statOffline" => p.StatusText(p.Danger), "accentText" => p.Accent,
                    "title" or "primaryText" or "statTotal" => p.Text, _ => p.Secondary
                };
                break;
            case CheckBox check:
                check.BackColor = Color.Transparent;
                break;
            case ToolStrip strip:
                ApplyMenu(strip);
                break;
        }
        if (control.ContextMenuStrip != null) ApplyMenu(control.ContextMenuStrip);
        if (control is DataGridView or RichTextBox or ListBox) ThemeScrollbars(control);
        // Grid editors and up/down internals manage their own layout and fonts.
        if (control is not DataGridView && control is not NumericUpDown)
            foreach (Control child in control.Controls) ApplyControl(child);
    }

    internal static void ApplyMenu(ToolStrip menu)
    {
        menu.BackColor = Palette.Surface; menu.ForeColor = Palette.Text;
        menu.Renderer = new ToolStripProfessionalRenderer(new AuroraMenuColors());
        foreach (ToolStripItem item in menu.Items)
        {
            item.BackColor = Palette.Surface; item.ForeColor = Palette.Text;
            if (item is ToolStripDropDownItem drop && drop.HasDropDownItems) ApplyMenu(drop.DropDown);
        }
    }

    internal static void RunTests()
    {
        static double L(Color c)
        {
            double F(byte b) { double v = b / 255d; return v <= .04045 ? v / 12.92 : Math.Pow((v + .055) / 1.055, 2.4); }
            return .2126 * F(c.R) + .7152 * F(c.G) + .0722 * F(c.B);
        }
        foreach (bool dark in new[] { false, true })
        {
            var p = AuroraPalette.Create(dark);
            foreach (var pair in new[] { (p.Text, p.Surface), (p.Secondary, p.Surface), (p.Muted, p.Surface), (p.OnAccent, p.Accent), (p.Text, p.Surface3) })
                if ((Math.Max(L(pair.Item1), L(pair.Item2)) + .05) / (Math.Min(L(pair.Item1), L(pair.Item2)) + .05) < 4.5)
                    throw new InvalidOperationException("Appearance text contrast is below AA.");
        }
    }
}

internal sealed class AuroraMenuColors : ProfessionalColorTable
{
    public override Color ToolStripDropDownBackground => AuroraTheme.Palette.Surface;
    public override Color MenuItemSelected => AuroraTheme.Palette.Surface3;
    public override Color MenuItemBorder => AuroraTheme.Palette.Accent;
    public override Color MenuBorder => AuroraTheme.Palette.Surface3;
    public override Color ImageMarginGradientBegin => AuroraTheme.Palette.Surface;
    public override Color ImageMarginGradientMiddle => AuroraTheme.Palette.Surface;
    public override Color ImageMarginGradientEnd => AuroraTheme.Palette.Surface;
    public override Color SeparatorDark => AuroraTheme.Palette.Surface3;
    public override Color SeparatorLight => AuroraTheme.Palette.Surface;
}

// FOCAL: only the primary task action receives the solid accent.
internal class AuroraButton : Button
{
    internal bool Primary { get; set; }
    internal bool Destructive { get; set; }
    internal bool Ghost { get; set; }
    internal bool Selected { get; set; }
    private bool _hover, _pressed;
    public AuroraButton()
    {
        FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
        Padding = new Padding(AuroraTheme.Space4, AuroraTheme.Space1, AuroraTheme.Space4, AuroraTheme.Space1);
    }
    public override Size GetPreferredSize(Size proposedSize)
    {
        var text = TextRenderer.MeasureText(Text, Font, Size.Empty,
            TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        int focusSpace = (int)Math.Ceiling(8 * DeviceDpi / 96f);
        return new Size(Math.Max(MinimumSize.Width, text.Width + Padding.Horizontal + focusSpace),
            Math.Max(MinimumSize.Height, Math.Max((int)Math.Ceiling(36 * DeviceDpi / 96f), text.Height + Padding.Vertical + focusSpace)));
    }
    protected override void SetBoundsCore(int x, int y, int width, int height, BoundsSpecified specified)
    {
        var preferred = GetPreferredSize(Size.Empty);
        base.SetBoundsCore(x, y, Math.Max(width, preferred.Width), Math.Max(height, preferred.Height), specified);
    }
    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        if (Parent != null)
        {
            var preferred = GetPreferredSize(Size.Empty);
            Size = new Size(Math.Max(Width, preferred.Width), Math.Max(Height, preferred.Height));
            Parent.PerformLayout();
        }
    }
    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        if (Parent != null)
        {
            var preferred = GetPreferredSize(Size.Empty);
            Size = new Size(Math.Max(Width, preferred.Width), Math.Max(Height, preferred.Height));
            Parent.PerformLayout();
        }
    }
    protected override void OnMouseEnter(EventArgs e) { _hover = true; base.OnMouseEnter(e); Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; base.OnMouseLeave(e); Invalidate(); }
    protected override void OnMouseDown(MouseEventArgs e) { _pressed = e.Button == MouseButtons.Left; base.OnMouseDown(e); Invalidate(); }
    protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; base.OnMouseUp(e); Invalidate(); }
    protected override void OnMouseCaptureChanged(EventArgs e) { _pressed = false; base.OnMouseCaptureChanged(e); Invalidate(); }
    protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Space) _pressed = true; base.OnKeyDown(e); Invalidate(); }
    protected override void OnKeyUp(KeyEventArgs e) { _pressed = false; base.OnKeyUp(e); Invalidate(); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { _pressed = false; base.OnLostFocus(e); Invalidate(); }
    internal Color ResolveTextColor(AuroraPalette p) =>
        !Enabled ? p.Muted : Destructive ? (Primary ? (p.Dark ? p.Canvas : Color.White) : p.StatusText(p.Danger))
        : Primary ? p.OnAccent : p.Text;

    internal static void RunDestructiveContrastTests()
    {
        double Luminance(Color color)
        {
            double Channel(byte component)
            {
                double value = component / 255.0;
                return value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4);
            }
            return .2126 * Channel(color.R) + .7152 * Channel(color.G) + .0722 * Channel(color.B);
        }
        void Check(Color text, Color background)
        {
            double a = Luminance(text), b = Luminance(background);
            if ((Math.Max(a, b) + .05) / (Math.Min(a, b) + .05) < 4.5)
                throw new InvalidOperationException("Destructive action text does not meet the reading contrast floor.");
        }
        using var action = new AuroraButton { Destructive = true, Ghost = true, Enabled = true };
        foreach (bool dark in new[] { false, true })
        {
            var palette = AuroraPalette.Create(dark);
            action.Primary = false;
            foreach (var background in new[] { palette.Canvas, palette.Surface2, palette.Surface3 })
                Check(action.ResolveTextColor(palette), background);
            action.Primary = true;
            Check(action.ResolveTextColor(palette), palette.Danger);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var p = AuroraTheme.Palette;
        e.Graphics.Clear(Parent?.BackColor ?? p.Canvas);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        int inset = Math.Max(4, (int)(4 * DeviceDpi / 96f));
        var bounds = Rectangle.Inflate(ClientRectangle, -inset, -inset);
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        using var path = WallboardStyle.Rounded(bounds, bounds.Height / 2);
        var fillColor = Primary && Enabled ? (Destructive ? p.Danger : p.Accent) : Selected || _pressed || (_hover && Enabled) ? p.Surface3 : Ghost ? Parent?.BackColor ?? p.Surface : p.Surface2;
        using var fill = new SolidBrush(fillColor);
        e.Graphics.FillPath(fill, path);
        if (Focused && ShowFocusCues)
        {
            using var focus = new Pen(p.Accent, 2 * DeviceDpi / 96f);
            using var ring = WallboardStyle.Rounded(Rectangle.Inflate(bounds, 2, 2), bounds.Height / 2 + 2);
            e.Graphics.DrawPath(focus, ring);
        }
        var ink = ResolveTextColor(p);
        var textBounds = bounds;
        if (_pressed) textBounds.Offset(0, 1);
        if (TextAlign is ContentAlignment.MiddleLeft) textBounds.X += Padding.Left;
        TextRenderer.DrawText(e.Graphics, Text, Font, textBounds, ink,
            (TextAlign is ContentAlignment.MiddleLeft ? TextFormatFlags.Left : TextFormatFlags.HorizontalCenter) | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
            (ShowKeyboardCues ? TextFormatFlags.Default : TextFormatFlags.HidePrefix));
    }
}
