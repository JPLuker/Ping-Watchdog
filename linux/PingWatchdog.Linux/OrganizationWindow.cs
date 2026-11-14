// Organize sites: nested folders, new sites and folders, rename, move and delete.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace PingWatchdog.Linux;

internal sealed class OrganizationWindow : Window
{
    private readonly WatchdogEngine _engine;
    private readonly TreeView _items = new();
    private readonly TextBlock _details = Ui.Text("Select a site or folder.", Tokens.Body, "Pw.Text2");
    private bool _closed;

    public OrganizationWindow(WatchdogEngine engine)
    {
        _engine = engine;
        Title = "Organize sites • Ping Watchdog";
        Icon = new WindowIcon(BrandAssets.Logo);
        Width = 760;
        Height = 600;
        MinWidth = 620;
        MinHeight = 440;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var newSite = Ui.Button("New site", "primary");
        var newFolder = Ui.Button("New folder");
        var rename = Ui.Button("Rename…");
        var move = Ui.Button("Move…");
        var delete = Ui.Button("Delete…", "destructive");
        var open = Ui.Button("Open site");
        newSite.Click += async (_, _) => await NewSiteAsync();
        newFolder.Click += async (_, _) => await NewFolderAsync();
        rename.Click += async (_, _) => await RenameAsync();
        move.Click += async (_, _) => await MoveAsync();
        delete.Click += async (_, _) => await DeleteAsync();
        open.Click += (_, _) => OpenSelected();
        var toolbar = new WrapPanel();
        foreach (var button in new[] { newSite, newFolder, rename, move, delete, open }) { button.Margin = new Thickness(0, 0, Tokens.Space2, Tokens.Space2); toolbar.Children.Add(button); }

        Avalonia.Automation.AutomationProperties.SetName(_items, "Sites and folders");
        var card = Ui.Card(_items, Tokens.Space2);
        card.Margin = new Thickness(0, Tokens.Space2, 0, Tokens.Space3);
        var layout = new DockPanel { Margin = new Thickness(Tokens.Space6, Tokens.Space5) };
        var title = Ui.Title("Organize sites");
        title.Margin = new Thickness(0, 0, 0, Tokens.Space4);
        DockPanel.SetDock(title, Dock.Top);
        DockPanel.SetDock(toolbar, Dock.Top);
        DockPanel.SetDock(_details, Dock.Bottom);
        layout.Children.Add(title);
        layout.Children.Add(toolbar);
        layout.Children.Add(_details);
        layout.Children.Add(card);
        Content = layout;

        _items.SelectionChanged += (_, _) => _details.Text = Selected is { } selected
            ? selected.IsFolder ? $"Folder: {selected.Key}" : $"Site: {selected.Key} · {WatchdogEngine.Hosts(_engine.SiteDefinitions().FirstOrDefault(s => s.Name == selected.Key)?.Hosts.Count ?? 0)}"
            : "Select a site or folder.";
        _items.DoubleTapped += (_, _) => OpenSelected();
        _items.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.F2) { e.Handled = true; await RenameAsync(); }
            else if (e.Key == Key.Delete) { e.Handled = true; await DeleteAsync(); }
            else if (e.Key == Key.Enter) { e.Handled = true; OpenSelected(); }
        };
        Opened += (_, _) => { Refresh(); _items.Focus(); };
        _engine.Changed += EngineChanged;
        Closed += (_, _) => { _closed = true; _engine.Changed -= EngineChanged; };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Close(); } };
    }

    internal int ItemCount => TreeNodes(_items.Items.OfType<TreeViewItem>()).Count();

    private void EngineChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(Refresh);

    private sealed record OrgItem(bool IsFolder, string Key, string Display);

    private void Refresh()
    {
        if (_closed) return;
        string? selectedKey = Selected is { } previous ? $"{previous.IsFolder}:{previous.Key}" : null;
        var expanded = TreeNodes(_items.Items.OfType<TreeViewItem>())
            .Where(item => item.IsExpanded && item.DataContext is OrgItem).Select(item => ((OrgItem)item.DataContext!).Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        bool first = _items.ItemCount == 0;
        var roots = new List<TreeViewItem>();
        var folders = new Dictionary<string, TreeViewItem>(StringComparer.OrdinalIgnoreCase);
        TreeViewItem? selection = null;
        foreach (var folder in _engine.FolderSnapshot().OrderBy(f => f.Count(ch => ch == '/')).ThenBy(f => f))
        {
            var model = new OrgItem(true, folder, folder.Split('/').Last());
            var item = new TreeViewItem { Header = Ui.Text(model.Display, weight: FontWeight.SemiBold), DataContext = model, IsExpanded = first || expanded.Contains(folder) };
            folders[folder] = item;
            string parent = folder.Contains('/') ? folder[..folder.LastIndexOf('/')] : "";
            if (folders.TryGetValue(parent, out var parentItem)) parentItem.Items.Add(item); else roots.Add(item);
            if (selectedKey == $"True:{folder}") selection = item;
        }
        foreach (var site in _engine.SiteDefinitions().Where(s => !s.Temporary).OrderBy(s => s.FolderPath).ThenBy(s => s.Name))
        {
            var model = new OrgItem(false, site.Name, $"{site.Name} · {WatchdogEngine.Hosts(site.Hosts.Count)}");
            var item = new TreeViewItem { Header = Ui.Text(model.Display), DataContext = model };
            if (folders.TryGetValue(site.FolderPath, out var folder)) folder.Items.Add(item); else roots.Add(item);
            if (selectedKey == $"False:{site.Name}") selection = item;
        }
        _items.ItemsSource = roots;
        _items.SelectedItem = selection;
    }

    private static IEnumerable<TreeViewItem> TreeNodes(IEnumerable<TreeViewItem> items)
    {
        foreach (var item in items) { yield return item; foreach (var child in TreeNodes(item.Items.OfType<TreeViewItem>())) yield return child; }
    }

    private OrgItem? Selected => (_items.SelectedItem as TreeViewItem)?.DataContext as OrgItem;

    private string CurrentFolder()
    {
        var selected = Selected;
        if (selected is null) return string.Empty;
        if (selected.IsFolder) return selected.Key;
        return _engine.SiteDefinitions().FirstOrDefault(s => s.Name.Equals(selected.Key, StringComparison.OrdinalIgnoreCase))?.FolderPath ?? string.Empty;
    }

    private async Task NewSiteAsync()
    {
        string? name = await Dialogs.Prompt(this, "New site", "Site name", "", "Add");
        if (name is null) return;
        if (_engine.AddSite(name, CurrentFolder()) is { } error) await Dialogs.Message(this, "New site", error);
    }

    private async Task NewFolderAsync()
    {
        string? name = await Dialogs.Prompt(this, "New folder", "Folder name", "", "Add");
        if (name is null) return;
        if (_engine.AddFolder(CurrentFolder(), name) is { } error) await Dialogs.Message(this, "New folder", error);
    }

    private async Task RenameAsync()
    {
        var selected = Selected;
        if (selected is null) return;
        string current = selected.IsFolder ? selected.Key.Split('/').Last() : selected.Key;
        string? name = await Dialogs.Prompt(this, "Rename", selected.IsFolder ? "Folder name" : "Site name", current, "Rename");
        if (name is null) return;
        string? error = selected.IsFolder ? _engine.RenameFolder(selected.Key, name) : _engine.RenameSite(selected.Key, name);
        if (error is not null) await Dialogs.Message(this, "Rename", error);
    }

    private async Task MoveAsync()
    {
        var selected = Selected;
        if (selected is null) return;
        var folders = _engine.FolderSnapshot().Where(f => !selected.IsFolder || (!f.Equals(selected.Key, StringComparison.OrdinalIgnoreCase) && !f.StartsWith(selected.Key + "/", StringComparison.OrdinalIgnoreCase))).ToList();
        var dialog = new FolderChoiceDialog(new[] { "Top level" }.Concat(folders).ToList());
        string? destination = await dialog.ShowDialog<string?>(this);
        if (destination is null) return;
        destination = destination == "Top level" ? string.Empty : destination;
        string? error = selected.IsFolder ? _engine.MoveFolder(selected.Key, destination) : _engine.MoveSite(selected.Key, destination);
        if (error is not null) await Dialogs.Message(this, "Move", error);
    }

    private async Task DeleteAsync()
    {
        var selected = Selected;
        if (selected is null) return;
        string message = selected.IsFolder
            ? $"Delete folder {selected.Key}? Its contents move to the parent folder."
            : $"Delete site {selected.Key} and its saved hosts?";
        if (!await Dialogs.Confirm(this, "Delete", message, "Delete", destructive: true)) return;
        string? error = selected.IsFolder ? _engine.DeleteFolder(selected.Key) : _engine.DeleteSite(selected.Key);
        if (error is not null) await Dialogs.Message(this, "Delete", error);
    }

    private void OpenSelected()
    {
        var selected = Selected;
        if (selected is null || selected.IsFolder) return;
        _engine.SelectedSite = selected.Key;
    }

    private sealed class FolderChoiceDialog : DialogWindow
    {
        public FolderChoiceDialog(IReadOnlyList<string> choices) : base("Move")
        {
            var combo = new ComboBox { ItemsSource = choices, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
            Avalonia.Automation.AutomationProperties.SetName(combo, "Destination folder");
            Body.Children.Add(FieldLabel("Destination folder"));
            Body.Children.Add(combo);
            AddAction("Cancel", () => Close(null));
            AddAction("Move", () => Close(combo.SelectedItem as string), "primary", accept: true);
            Opened += (_, _) => combo.Focus();
        }
    }
}
