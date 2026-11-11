namespace PingWatchdog;

// FOCAL: one named value. Native compact chrome; measured rows keep actions visible.
internal static class CompactDialog
{
    internal static void Build(Form form, string title, Label prompt, Control input, string action, int width = 440) =>
        Build(form, title, new[] { (prompt, input) }, action, width);

    // Several labeled fields in one column; the first field takes focus.
    internal static void Build(Form form, string title, (Label Prompt, Control Input)[] fields, string action, int width = 440)
    {
        form.Text = title;
        form.AutoScaleMode = AutoScaleMode.Dpi;
        form.Font = AppTheme.LabelFont;
        form.ClientSize = new Size(width, 180);
        form.MinimumSize = new Size(400, 220);
        form.AutoSize = true;
        form.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        form.FormBorderStyle = FormBorderStyle.FixedDialog;
        form.MinimizeBox = form.MaximizeBox = false;
        form.ShowInTaskbar = false;
        form.StartPosition = FormStartPosition.CenterParent;
        for (int i = 0; i < fields.Length; i++)
        {
            var (prompt, input) = fields[i];
            input.Font = AppTheme.LabelFont;
            input.Dock = DockStyle.Top;
            input.TabIndex = i * 2 + 1;
            input.Margin = new Padding(0, 0, 0, i + 1 < fields.Length ? 16 : 8);
            prompt.AutoSize = true;
            prompt.TabIndex = i * 2;
            prompt.Margin = new Padding(0, 0, 0, 8);
        }
        var save = new ThemeButton { Text = action, AutoSize = true, DialogResult = DialogResult.OK,
            Font = AppTheme.LabelFont, MinimumSize = new Size(75, 32), TabIndex = 1 };
        var cancel = new ThemeButton { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel,
            Font = AppTheme.LabelFont, MinimumSize = new Size(75, 32), TabIndex = 0 };
        cancel.Click += (_, _) => form.Close();
        var actions = new FlowLayoutPanel
        {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill,
            WrapContents = true, FlowDirection = FlowDirection.RightToLeft,
            Margin = new Padding(0, 12, 0, 0), TabIndex = fields.Length * 2
        };
        actions.Controls.Add(save);
        actions.Controls.Add(cancel);
        int rows = fields.Length * 2 + 1;
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 1, RowCount = rows,
            Padding = new Padding(AppTheme.Space4)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < rows; i++) root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        for (int i = 0; i < fields.Length; i++)
        {
            root.Controls.Add(fields[i].Prompt, 0, i * 2);
            root.Controls.Add(fields[i].Input, 0, i * 2 + 1);
        }
        root.Controls.Add(actions, 0, rows - 1);
        form.Controls.Add(root);
        form.AcceptButton = save;
        form.CancelButton = cancel;
        AppTheme.Register(form);
        var first = fields[0].Input;
        form.Shown += (_, _) => { first.Focus(); if (first is TextBoxBase text) text.SelectAll(); };
    }

    internal static void StylePicker(ComboBox combo)
    {
        combo.FlatStyle = FlatStyle.Flat;
        combo.Font = AppTheme.LabelFont;
        combo.DrawMode = DrawMode.OwnerDrawFixed;
        combo.ItemHeight = 24;
        combo.DrawItem += (_, e) =>
        {
            var p = AppTheme.Palette;
            bool selected = (e.State & DrawItemState.Selected) != 0;
            var background = selected ? p.Surface3 : p.Surface;
            var foreground = selected && SystemInformation.HighContrast ? SystemColors.HighlightText : p.Text;
            using (var brush = new SolidBrush(background)) e.Graphics.FillRectangle(brush, e.Bounds);
            string text = e.Index >= 0 && e.Index < combo.Items.Count ? combo.Items[e.Index]?.ToString() ?? "" : combo.Text;
            TextRenderer.DrawText(e.Graphics, text, combo.Font, e.Bounds, foreground,
                TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            if ((e.State & DrawItemState.Focus) != 0) ControlPaint.DrawFocusRectangle(e.Graphics, e.Bounds, foreground, background);
        };
    }

    internal static void CheckBounds(Form form)
    {
        void Inspect(Control parent)
        {
            foreach (Control child in parent.Controls)
            {
                if (!child.Visible) continue;
                if (child is Button or TextBoxBase or ComboBox)
                {
                    var bounds = form.RectangleToClient(child.RectangleToScreen(child.ClientRectangle));
                    if (!parent.ClientRectangle.Contains(child.Bounds) || !form.ClientRectangle.Contains(bounds))
                        throw new InvalidOperationException($"{form.Text}: clipped {child.AccessibleName ?? child.Text}; bounds={child.Bounds}, parent={parent.ClientRectangle}.");
                }
                if (child is not DataGridView) Inspect(child);
            }
        }
        form.PerformLayout();
        Inspect(form);
    }
}
