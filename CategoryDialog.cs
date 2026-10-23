namespace PingWatchdog;

internal sealed class CategoryDialog : Form
{
    // FOCAL: category field. Compact native dialog, sized from its content.
    private static readonly string[] SuggestedCategories =
    {
        "AP", "Firewall", "Switch", "Router", "Server", "Printer",
        "Camera", "UPS", "Workstation", "IoT", "Other"
    };
    private readonly ComboBox _box = new()
    {
        Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDown,
        AutoCompleteMode = AutoCompleteMode.SuggestAppend,
        AutoCompleteSource = AutoCompleteSource.ListItems,
        AccessibleName = "Category", TabIndex = 0
    };
    public string Category => _box.Text.Trim();

    public CategoryDialog(string host, string current)
    {
        Text = $"Set category • {host}";
        ClientSize = new Size(420, 180);
        MinimumSize = new Size(400, 220);
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = AuroraTheme.LabelFont;
        MinimizeBox = MaximizeBox = false;
        ShowInTaskbar = false;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        _box.Items.AddRange(SuggestedCategories);
        _box.Text = current;
        var save = new AuroraButton { Text = "Save category", DialogResult = DialogResult.OK, AutoSize = true, TabIndex = 1 };
        var cancel = new AuroraButton { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true, TabIndex = 0 };
        var buttons = new FlowLayoutPanel
        {
            AutoSize = true, Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft, WrapContents = true,
            Margin = new Padding(0, 16, 0, 0), TabIndex = 1
        };
        buttons.Controls.Add(save);
        buttons.Controls.Add(cancel);
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 1, RowCount = 3,
            Padding = new Padding(AuroraTheme.Space4)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 3; i++) root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(new Label { Text = "Category", AutoSize = true, Margin = new Padding(0, 0, 0, 8) }, 0, 0);
        root.Controls.Add(_box, 0, 1);
        root.Controls.Add(buttons, 0, 2);
        Controls.Add(root);
        AcceptButton = save;
        CancelButton = cancel;
        AuroraTheme.Register(this);
        Shown += (_, _) => { _box.Focus(); _box.SelectAll(); };
    }

    internal void CheckLayout()
    {
        PerformLayout();
        foreach (var button in Controls.OfType<TableLayoutPanel>().Single().Controls
            .OfType<FlowLayoutPanel>().Single().Controls.OfType<Button>())
            if (!button.Visible || !button.Parent!.ClientRectangle.Contains(button.Bounds))
                throw new InvalidOperationException("Category action is clipped.");
    }
}
