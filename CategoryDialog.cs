namespace PingWatchdog;

internal sealed class CategoryDialog : Form
{
    private static readonly string[] SuggestedCategories =
    {
        "AP",
        "Firewall",
        "Switch",
        "Router",
        "Server",
        "Printer",
        "Camera",
        "UPS",
        "Workstation",
        "IoT",
        "Other"
    };

    private readonly ComboBox _box = new()
    {
        Dock = DockStyle.Fill,
        DropDownStyle = ComboBoxStyle.DropDown,
        AutoCompleteMode = AutoCompleteMode.SuggestAppend,
        AutoCompleteSource = AutoCompleteSource.ListItems
    };

    public string Category => _box.Text.Trim();

    public CategoryDialog(string host, string current)
    {
        Text = $"Set category • {host}";
        Width = 430;
        Height = 170;
        MinimizeBox = false;
        MaximizeBox = false;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Color.FromArgb(13, 17, 23);
        ForeColor = Color.FromArgb(230, 237, 243);
        Font = new Font("Segoe UI", 9.5f);

        _box.Items.AddRange(SuggestedCategories);
        _box.Text = current;
        _box.BackColor = Color.FromArgb(22, 27, 34);
        _box.ForeColor = Color.FromArgb(230, 237, 243);
        _box.FlatStyle = FlatStyle.Flat;

        var ok = new Button
        {
            Text = "Save Category",
            DialogResult = DialogResult.OK,
            AutoSize = true,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(35, 134, 96),
            ForeColor = Color.White
        };

        var cancel = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            AutoSize = true,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(22, 27, 34),
            ForeColor = Color.White
        };

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false
        };
        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(12)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(new Label
        {
            Text = "Category:",
            AutoSize = true,
            ForeColor = Color.FromArgb(139, 148, 158)
        }, 0, 0);
        layout.Controls.Add(_box, 0, 1);
        layout.Controls.Add(new Label
        {
            Text = "Choose a common device type or enter your own category.",
            AutoSize = true,
            ForeColor = Color.FromArgb(139, 148, 158),
            Padding = new Padding(0, 5, 0, 0)
        }, 0, 2);
        layout.Controls.Add(buttons, 0, 3);

        Controls.Add(layout);
        AcceptButton = ok;
        CancelButton = cancel;

        Shown += (_, _) =>
        {
            _box.Focus();
            _box.SelectAll();
        };
    }
}
