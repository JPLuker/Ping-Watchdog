// Quiet Precision (InternalDesignGuidelines DESIGN.md Part B), the same palette as the Windows AppTheme.
// Warm neutrals, ink as the accent, color only for host state. Everything binds to these resources,
// so switching light/dark (or following the desktop) recolors every open window.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace PingWatchdog.Linux;

internal sealed record Palette(bool Dark, Color Canvas, Color Surface, Color Surface2, Color Surface3,
    Color Text, Color Text2, Color Text3, Color Accent, Color OnAccent,
    Color Success, Color Warning, Color Danger, Color Info, Color Border, Color BorderStrong, Color Focus)
{
    static Color C(string hex) => Color.Parse(hex);

    public static readonly Palette Light = new(false, C("#FAFAF9"), C("#FFFFFF"), C("#F3F3F1"), C("#E9E9E6"),
        C("#1B1B19"), C("#53524D"), C("#64635D"), C("#1B1B19"), C("#FFFFFF"),
        C("#1A7531"), C("#875A00"), C("#C21F2B"), C("#0A63C9"), C("#DEDDD9"), C("#8E8D86"), C("#1F5FD9"));

    public static readonly Palette DarkPalette = new(true, C("#111110"), C("#191918"), C("#212120"), C("#2B2B29"),
        C("#EDEDEA"), C("#B5B4AE"), C("#9C9B95"), C("#EDEDEA"), C("#111110"),
        C("#4CC26A"), C("#D9A53A"), C("#F47A73"), C("#58A6FF"), C("#30302D"), C("#75746E"), C("#6EA8FE"));

    public Color StateColor(HostSnapshot host) =>
        host.Disabled || host.Snoozed || !host.Monitoring ? Text3 : StateColor(host.State);

    public Color StateColor(HostState state) => state switch
    {
        HostState.Online => Success,
        HostState.Suspect => Warning,
        HostState.Offline => Danger,
        _ => Text3
    };

    // WCAG relative-luminance contrast, used by the self-test to prove every text role passes.
    public static double Contrast(Color a, Color b)
    {
        static double L(Color c)
        {
            static double Channel(byte v) { double s = v / 255.0; return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4); }
            return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
        }
        double x = L(a), y = L(b);
        return (Math.Max(x, y) + 0.05) / (Math.Min(x, y) + 0.05);
    }
}

internal static class Tokens
{
    // B6 spacing and B5 shape.
    public const double Space1 = 4, Space2 = 8, Space3 = 12, Space4 = 16, Space5 = 20, Space6 = 24, Space8 = 32;
    public const double RadiusSmall = 6, RadiusMedium = 8, RadiusCard = 12, RadiusDialog = 16;
    // B7 type: the desktop's UI font, a monospace face for addresses, logs and timestamps.
    public const double DisplaySize = 32, Title1 = 24, Title2 = 20, Title3 = 16, Body = 14, Label = 13, Caption = 13;
    public static readonly FontFamily Mono = new("Cascadia Mono, JetBrains Mono, Noto Sans Mono, DejaVu Sans Mono, Liberation Mono, monospace");

    public static event Action? Changed;

    public static Palette Current => Application.Current?.ActualThemeVariant == ThemeVariant.Dark ? Palette.DarkPalette : Palette.Light;
    public static IBrush Brush(Color color) => new SolidColorBrush(color);

    public static void Install(Application app)
    {
        var fluent = new FluentTheme();
        fluent.Palettes[ThemeVariant.Light] = PaletteResources(Palette.Light);
        fluent.Palettes[ThemeVariant.Dark] = PaletteResources(Palette.DarkPalette);
        app.Styles.Add(fluent);
        app.Resources.ThemeDictionaries[ThemeVariant.Light] = Resources(Palette.Light);
        app.Resources.ThemeDictionaries[ThemeVariant.Dark] = Resources(Palette.DarkPalette);
        foreach (var style in Styles()) app.Styles.Add(style);
        app.ActualThemeVariantChanged += (_, _) => Changed?.Invoke();
    }

    public static void SetMode(AppearanceMode mode)
    {
        if (Application.Current is null) return;
        Application.Current.RequestedThemeVariant = mode switch
        {
            AppearanceMode.Light => ThemeVariant.Light,
            AppearanceMode.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };
        Changed?.Invoke();
    }

    private static ColorPaletteResources PaletteResources(Palette p) => new()
    {
        Accent = p.Focus,
        RegionColor = p.Canvas,
        AltHigh = p.Surface,
        AltMedium = p.Surface,
        AltMediumLow = p.Surface2,
        AltLow = p.Surface2,
        BaseHigh = p.Text,
        BaseMediumHigh = p.Text2,
        BaseMedium = p.Text3,
        BaseMediumLow = p.BorderStrong,
        BaseLow = p.Border,
        ChromeAltLow = p.Text,
        ChromeBlackHigh = Colors.Black,
        ChromeBlackMedium = Color.FromArgb(160, 0, 0, 0),
        ChromeBlackMediumLow = Color.FromArgb(100, 0, 0, 0),
        ChromeBlackLow = Color.FromArgb(40, 0, 0, 0),
        ChromeHigh = p.BorderStrong,
        ChromeMedium = p.Surface2,
        ChromeMediumLow = p.Surface,
        ChromeLow = p.Surface,
        ChromeGray = p.Text3,
        ChromeWhite = Colors.White,
        ListLow = p.Surface2,
        ListMedium = p.Surface3,
        ErrorText = p.Danger
    };

    private static ResourceDictionary Resources(Palette p)
    {
        IBrush B(Color c) => new SolidColorBrush(c);
        var hover = p.Dark ? Color.Parse("#2B2B29") : Color.Parse("#E9E9E6");
        var pressed = p.Dark ? Color.Parse("#353533") : Color.Parse("#DEDDD9");
        var accentHover = p.Dark ? Color.Parse("#D6D6D2") : Color.Parse("#34342F");
        return new ResourceDictionary
        {
            ["Pw.Canvas"] = B(p.Canvas),
            ["Pw.Surface"] = B(p.Surface),
            ["Pw.Surface2"] = B(p.Surface2),
            ["Pw.Surface3"] = B(p.Surface3),
            ["Pw.Text"] = B(p.Text),
            ["Pw.Text2"] = B(p.Text2),
            ["Pw.Text3"] = B(p.Text3),
            ["Pw.Accent"] = B(p.Accent),
            ["Pw.AccentHover"] = B(accentHover),
            ["Pw.OnAccent"] = B(p.OnAccent),
            ["Pw.Success"] = B(p.Success),
            ["Pw.Warning"] = B(p.Warning),
            ["Pw.Danger"] = B(p.Danger),
            ["Pw.Info"] = B(p.Info),
            ["Pw.Border"] = B(p.Border),
            ["Pw.BorderStrong"] = B(p.BorderStrong),
            ["Pw.Focus"] = B(p.Focus),
            ["Pw.Hover"] = B(hover),
            ["Pw.Pressed"] = B(pressed),
            ["Pw.Overlay"] = B(Color.FromArgb(p.Dark ? (byte)150 : (byte)90, 0, 0, 0)),
            // Secondary buttons: quiet surface, 1px border, ink text.
            ["ButtonBackground"] = B(p.Surface2),
            ["ButtonBackgroundPointerOver"] = B(hover),
            ["ButtonBackgroundPressed"] = B(pressed),
            ["ButtonBackgroundDisabled"] = B(p.Surface2),
            ["ButtonForeground"] = B(p.Text),
            ["ButtonForegroundPointerOver"] = B(p.Text),
            ["ButtonForegroundPressed"] = B(p.Text),
            ["ButtonForegroundDisabled"] = B(p.Text3),
            ["ButtonBorderBrush"] = B(p.Border),
            ["ButtonBorderBrushPointerOver"] = B(p.BorderStrong),
            ["ButtonBorderBrushPressed"] = B(p.BorderStrong),
            ["ButtonBorderBrushDisabled"] = B(p.Border),
            ["CheckBoxCheckBackgroundFillChecked"] = B(p.Focus),
            ["CheckBoxCheckBackgroundFillCheckedPointerOver"] = B(p.Focus),
            ["CheckBoxCheckBackgroundStrokeChecked"] = B(p.Focus),
            ["CheckBoxCheckGlyphForegroundChecked"] = B(p.Dark ? p.Canvas : Colors.White),
            ["CheckBoxCheckGlyphForegroundCheckedPointerOver"] = B(p.Dark ? p.Canvas : Colors.White),
            ["TextControlBackground"] = B(p.Surface),
            ["TextControlBackgroundPointerOver"] = B(p.Surface),
            ["TextControlBackgroundFocused"] = B(p.Surface),
            ["TextControlBorderBrush"] = B(p.BorderStrong),
            ["TextControlBorderBrushPointerOver"] = B(p.Text3),
            ["TextControlBorderBrushFocused"] = B(p.Focus),
            ["TextControlForeground"] = B(p.Text),
            ["TextControlForegroundFocused"] = B(p.Text),
            ["TextControlForegroundPointerOver"] = B(p.Text),
            ["TextControlPlaceholderForeground"] = B(p.Text3),
            ["TextControlSelectionHighlightColor"] = B(p.Focus),
            ["MenuFlyoutPresenterBackground"] = B(p.Surface),
            ["MenuFlyoutPresenterBorderBrush"] = B(p.Border),
            ["MenuFlyoutItemBackgroundPointerOver"] = B(hover),
            ["MenuFlyoutItemForeground"] = B(p.Text),
            ["MenuFlyoutItemForegroundDisabled"] = B(p.Text3),
            ["ComboBoxDropDownBackground"] = B(p.Surface),
            ["ComboBoxBackground"] = B(p.Surface),
            ["ComboBoxBorderBrush"] = B(p.BorderStrong),
            ["ToolTipBackground"] = B(p.Surface),
            ["ToolTipForeground"] = B(p.Text),
            ["ToolTipBorderBrush"] = B(p.Border),
        };
    }

    private static IEnumerable<IStyle> Styles()
    {
        Style S(Func<Selector?, Selector> selector, params (AvaloniaProperty Property, object Value)[] setters)
        {
            var style = new Style(selector);
            foreach (var (property, value) in setters) style.Setters.Add(new Setter(property, value));
            return style;
        }
        Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension R(string key) => new(key);

        yield return S(x => x.OfType<Window>(),
            (Window.BackgroundProperty, R("Pw.Canvas")),
            (Window.ForegroundProperty, R("Pw.Text")),
            (Window.FontSizeProperty, Body));
        yield return S(x => x.OfType<TextBlock>(), (TextBlock.LineHeightProperty, double.NaN));
        // Buttons: rounded rectangles; chips are pills (B5).
        yield return S(x => x.OfType<Button>(),
            (Button.CornerRadiusProperty, new CornerRadius(RadiusMedium)),
            (Button.PaddingProperty, new Thickness(14, 6)),
            (Button.MinHeightProperty, 32.0),
            (Button.FontSizeProperty, Body),
            (Button.BorderThicknessProperty, new Thickness(1)),
            (Button.VerticalContentAlignmentProperty, VerticalAlignment.Center));
        yield return S(x => x.OfType<ToggleButton>(),
            (ToggleButton.CornerRadiusProperty, new CornerRadius(RadiusMedium)),
            (ToggleButton.PaddingProperty, new Thickness(14, 6)),
            (ToggleButton.MinHeightProperty, 32.0),
            (ToggleButton.BorderThicknessProperty, new Thickness(1)));
        foreach (var style in Variant("primary", "Pw.Accent", "Pw.AccentHover", "Pw.OnAccent", "Pw.Accent")) yield return style;
        foreach (var style in Variant("ghost", "Pw.Canvas", "Pw.Hover", "Pw.Text", "Pw.Canvas")) yield return style;
        foreach (var style in Variant("nav", "Pw.Canvas", "Pw.Hover", "Pw.Text", "Pw.Canvas")) yield return style;
        foreach (var style in Variant("destructive", "Pw.Surface2", "Pw.Hover", "Pw.Danger", "Pw.Border")) yield return style;
        yield return S(x => x.OfType<Button>().Class("nav"),
            (Button.HorizontalContentAlignmentProperty, HorizontalAlignment.Left),
            (Button.HorizontalAlignmentProperty, HorizontalAlignment.Stretch),
            (Button.PaddingProperty, new Thickness(16, 7)),
            (Button.CornerRadiusProperty, new CornerRadius(RadiusSmall)));
        yield return S(x => x.OfType<Button>().Class("nav").Class("selected").Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"),
            (ContentPresenter.BackgroundProperty, R("Pw.Surface3")));
        yield return S(x => x.OfType<Button>().Class("pill"),
            (Button.CornerRadiusProperty, new CornerRadius(999)),
            (Button.PaddingProperty, new Thickness(14, 5)));
        yield return S(x => x.OfType<ToggleButton>().Class("chip"),
            (ToggleButton.CornerRadiusProperty, new CornerRadius(999)),
            (ToggleButton.PaddingProperty, new Thickness(14, 4)),
            (ToggleButton.MinHeightProperty, 30.0),
            (ToggleButton.BackgroundProperty, R("Pw.Canvas")),
            (ToggleButton.BorderBrushProperty, R("Pw.BorderStrong")),
            (ToggleButton.ForegroundProperty, R("Pw.Text")));
        yield return S(x => x.OfType<ToggleButton>().Class("chip").Class(":checked").Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"),
            (ContentPresenter.BackgroundProperty, R("Pw.Surface3")),
            (ContentPresenter.BorderBrushProperty, R("Pw.Surface3")),
            (ContentPresenter.ForegroundProperty, R("Pw.Text")));
        yield return S(x => x.OfType<ToggleButton>().Class("chip").Class(":pointerover").Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"),
            (ContentPresenter.BackgroundProperty, R("Pw.Hover")),
            (ContentPresenter.ForegroundProperty, R("Pw.Text")));
        yield return S(x => x.OfType<ToggleButton>().Class("segment"),
            (ToggleButton.BackgroundProperty, R("Pw.Surface2")),
            (ToggleButton.BorderBrushProperty, R("Pw.Border")),
            (ToggleButton.ForegroundProperty, R("Pw.Text")));
        yield return S(x => x.OfType<ToggleButton>().Class("segment").Class(":checked").Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"),
            (ContentPresenter.BackgroundProperty, R("Pw.Surface3")),
            (ContentPresenter.BorderBrushProperty, R("Pw.BorderStrong")),
            (ContentPresenter.ForegroundProperty, R("Pw.Text")));
        yield return S(x => x.OfType<ToggleButton>().Class("segment").Class(":pointerover").Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"),
            (ContentPresenter.BackgroundProperty, R("Pw.Hover")),
            (ContentPresenter.ForegroundProperty, R("Pw.Text")));
        // Focus: a 2px blue ring, never the accent (Quiet Precision B10).
        yield return S(x => x.Is<Control>().Class(":focus-visible"), (Control.FocusAdornerProperty, FocusRing()));
        yield return S(x => x.OfType<TextBox>(),
            (TextBox.CornerRadiusProperty, new CornerRadius(RadiusMedium)),
            (TextBox.MinHeightProperty, 32.0),
            (TextBox.FontSizeProperty, Body),
            (TextBox.PaddingProperty, new Thickness(10, 6)));
        yield return S(x => x.OfType<ComboBox>(),
            (ComboBox.CornerRadiusProperty, new CornerRadius(RadiusMedium)),
            (ComboBox.MinHeightProperty, 32.0),
            (ComboBox.FontSizeProperty, Body));
        yield return S(x => x.OfType<NumericUpDown>(),
            (NumericUpDown.CornerRadiusProperty, new CornerRadius(RadiusMedium)),
            (NumericUpDown.FontSizeProperty, Body));
        yield return S(x => x.OfType<CheckBox>(), (CheckBox.FontSizeProperty, Body));
        yield return S(x => x.OfType<ListBoxItem>(),
            (ListBoxItem.PaddingProperty, new Thickness(0)),
            (ListBoxItem.MinHeightProperty, 0.0),
            (ListBoxItem.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        yield return S(x => x.OfType<ListBoxItem>().Class(":selected").Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"),
            (ContentPresenter.BackgroundProperty, R("Pw.Surface3")));
        yield return S(x => x.OfType<ListBoxItem>().Class(":selected").Class(":pointerover").Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"),
            (ContentPresenter.BackgroundProperty, R("Pw.Surface3")));
        yield return S(x => x.OfType<ListBoxItem>().Class(":pointerover").Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"),
            (ContentPresenter.BackgroundProperty, R("Pw.Surface2")));
        yield return S(x => x.OfType<ListBox>(),
            (ListBox.BackgroundProperty, Brushes.Transparent),
            (ListBox.BorderThicknessProperty, new Thickness(0)));
        yield return S(x => x.OfType<ContextMenu>(), (ContextMenu.CornerRadiusProperty, new CornerRadius(RadiusMedium)));
        yield return S(x => x.OfType<MenuItem>(), (MenuItem.FontSizeProperty, Body));
    }

    // Button colors per variant, including hover and pressed, which Fluent sets on the template part.
    private static IEnumerable<IStyle> Variant(string name, string background, string hover, string foreground, string border)
    {
        var R = (string key) => new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension(key);
        var normal = new Style(x => x.OfType<Button>().Class(name));
        normal.Setters.Add(new Setter(Button.BackgroundProperty, R(background)));
        normal.Setters.Add(new Setter(Button.ForegroundProperty, R(foreground)));
        normal.Setters.Add(new Setter(Button.BorderBrushProperty, R(border)));
        yield return normal;
        foreach (var state in new[] { ":pointerover", ":pressed" })
        {
            var style = new Style(x => x.OfType<Button>().Class(name).Class(state).Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"));
            style.Setters.Add(new Setter(ContentPresenter.BackgroundProperty, R(state == ":pressed" && name != "primary" ? "Pw.Pressed" : hover)));
            style.Setters.Add(new Setter(ContentPresenter.ForegroundProperty, R(foreground)));
            style.Setters.Add(new Setter(ContentPresenter.BorderBrushProperty, R(border)));
            yield return style;
        }
    }

    private static ITemplate<Control> FocusRing() => new FuncTemplate<Control>(() => new Border
    {
        BorderThickness = new Thickness(2),
        CornerRadius = new CornerRadius(RadiusMedium),
        Margin = new Thickness(-3),
        [!Border.BorderBrushProperty] = new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("Pw.Focus")
    });
}

// Small factories so every window is built from the same parts.
internal static class Ui
{
    public static T Res<T>(this T control, AvaloniaProperty property, string key) where T : Control
    {
        control[!property] = new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension(key);
        return control;
    }

    public static TextBlock Text(string text, double size = Tokens.Body, string color = "Pw.Text", FontWeight? weight = null, bool mono = false)
    {
        var block = new TextBlock
        {
            Text = text,
            FontSize = size,
            FontWeight = weight ?? FontWeight.Normal,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        if (mono) block.FontFamily = Tokens.Mono;
        return block.Res(TextBlock.ForegroundProperty, color);
    }

    public static TextBlock Title(string text, double size = Tokens.Title1) => Text(text, size, weight: FontWeight.SemiBold);

    public static Button Button(string text, string? kind = null, string? automation = null)
    {
        var button = new Button { Content = text };
        if (kind is not null) foreach (var c in kind.Split(' ')) button.Classes.Add(c);
        Avalonia.Automation.AutomationProperties.SetName(button, automation ?? text);
        return button;
    }

    public static Border Card(Control child, double padding = Tokens.Space4)
    {
        return new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(Tokens.RadiusCard),
            Padding = new Thickness(padding),
            Child = child,
            ClipToBounds = true
        }.Res(Border.BackgroundProperty, "Pw.Surface").Res(Border.BorderBrushProperty, "Pw.Border");
    }

    public static Border Divider(Thickness? margin = null) => new Border
    {
        Height = 1,
        Margin = margin ?? new Thickness(0, Tokens.Space3),
        HorizontalAlignment = HorizontalAlignment.Stretch
    }.Res(Border.BackgroundProperty, "Pw.Border");

    // A status dot plus its word: color is never the only signal.
    public static Control StatusDot(Color color, double size = 8)
    {
        return new Avalonia.Controls.Shapes.Ellipse { Width = size, Height = size, Fill = new SolidColorBrush(color), VerticalAlignment = VerticalAlignment.Center };
    }

    public static StackPanel Row(double spacing, params Control[] children)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = spacing, VerticalAlignment = VerticalAlignment.Center };
        foreach (var child in children) panel.Children.Add(child);
        return panel;
    }

    public static StackPanel Column(double spacing, params Control[] children)
    {
        var panel = new StackPanel { Orientation = Orientation.Vertical, Spacing = spacing };
        foreach (var child in children) panel.Children.Add(child);
        return panel;
    }

    public static MenuItem MenuItem(string header, Action? action, bool enabled = true, string? gesture = null)
    {
        var item = new MenuItem { Header = header, IsEnabled = enabled && action is not null };
        if (gesture is not null) item.InputGesture = Avalonia.Input.KeyGesture.Parse(gesture);
        if (action is not null) item.Click += (_, _) => action();
        return item;
    }
}
