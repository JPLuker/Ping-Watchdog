namespace PingWatchdog;

// Spreadsheet-style paste for editable grids (#40: "Can't past in the lables for new hosts").
// Ctrl+V or Shift+Insert on a selected cell pastes without opening the editor first. Several lines fill
// down, tab-separated values fill across, and read-only or checkbox cells are skipped. While a cell is
// being edited, paste goes through ClipboardText so HTML-only or busy clipboards behave like text boxes.
internal static class GridPaste
{
    internal static void Attach(DataGridView grid, Action onBusy)
    {
        grid.KeyDown += (_, e) =>
        {
            bool paste = (e.Control && e.KeyCode == Keys.V) || (e.Shift && e.KeyCode == Keys.Insert);
            if (!paste || grid.IsCurrentCellInEditMode || grid.CurrentCell is null) return;
            e.Handled = true; e.SuppressKeyPress = true;
            var read = ClipboardText.Read();
            if (read.Busy) onBusy();
            else if (read.Text is { } text) PasteInto(grid, text);
        };
        grid.EditingControlShowing += (_, e) =>
        {
            if (e.Control is TextBox box && box.Tag is not EditorPasteHook)
                box.Tag = new EditorPasteHook(box, onBusy);
        };
    }

    // Returns how many cells changed. Rows past the end of the grid are ignored.
    internal static int PasteInto(DataGridView grid, string text)
    {
        if (grid.CurrentCell is null) return 0;
        var lines = ClipboardText.NormalizeMultiline(text).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').ToList();
        while (lines.Count > 1 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
        int startRow = grid.CurrentCell.RowIndex, startColumn = grid.CurrentCell.ColumnIndex, changed = 0;
        for (int line = 0; line < lines.Count && startRow + line < grid.Rows.Count; line++)
        {
            var values = lines[line].Split('\t');
            int column = startColumn;
            foreach (var value in values)
            {
                while (column < grid.Columns.Count && !Writable(grid, startRow + line, column)) column++;
                if (column >= grid.Columns.Count) break;
                grid.Rows[startRow + line].Cells[column].Value = ClipboardText.NormalizeLine(value);
                changed++; column++;
            }
        }
        return changed;
    }

    private static bool Writable(DataGridView grid, int row, int column)
    {
        var cell = grid.Rows[row].Cells[column];
        return grid.Columns[column].Visible && !cell.ReadOnly && cell is DataGridViewTextBoxCell;
    }

    private sealed class EditorPasteHook : NativeWindow
    {
        private readonly TextBox _box;
        private readonly Action _onBusy;
        internal EditorPasteHook(TextBox box, Action onBusy)
        {
            _box = box; _onBusy = onBusy;
            if (box.IsHandleCreated) AssignHandle(box.Handle);
            box.HandleCreated += (_, _) => AssignHandle(box.Handle);
            box.HandleDestroyed += (_, _) => ReleaseHandle();
        }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == PlainTextBox.WmPaste)
            {
                var read = ClipboardText.Read();
                if (read.Busy) _onBusy();
                else if (read.Text is { } text) _box.SelectedText = ClipboardText.NormalizeLine(text);
                m.Result = IntPtr.Zero;
                return;
            }
            base.WndProc(ref m);
        }
    }
}
