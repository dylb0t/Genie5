using Avalonia.Controls;
using Avalonia.Interactivity;
using Genie4.Core.Aliases;

namespace Genie5.Ui;

public partial class AliasesPanel : UserControl
{
    private AliasEngine? _engine;

    public AliasesPanel() => InitializeComponent();

    public void Initialize(AliasEngine engine)
    {
        _engine = engine;
        Refresh();
    }

    private void Refresh()
    {
        if (_engine is null) return;
        ItemsList.ItemsSource = _engine.Aliases
            .Select(a => $"{(a.IsEnabled ? "✓" : "✗")}  {a.Name}  →  {a.Expansion}")
            .ToList();
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var idx = ItemsList.SelectedIndex;
        if (_engine is null || idx < 0 || idx >= _engine.Aliases.Count) return;
        var alias = _engine.Aliases[idx];
        NameBox.Text        = alias.Name;
        ExpansionBox.Text   = alias.Expansion;
        EnabledCheck.IsChecked = alias.IsEnabled;
        StatusText.Text     = string.Empty;
    }

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        if (_engine is null) return;
        var name      = NameBox.Text?.Trim() ?? string.Empty;
        var expansion = ExpansionBox.Text?.Trim() ?? string.Empty;
        var enabled   = EnabledCheck.IsChecked == true;

        if (string.IsNullOrEmpty(name)) { StatusText.Text = "Name is required."; return; }

        _engine.RemoveAlias(name);
        _engine.AddAlias(name, expansion, enabled);
        Refresh();
        StatusText.Text = $"Saved '{name}'.";
    }

    private void OnDelete(object? sender, RoutedEventArgs e)
    {
        if (_engine is null) return;
        var idx = ItemsList.SelectedIndex;
        if (idx < 0 || idx >= _engine.Aliases.Count) { StatusText.Text = "Select an alias to delete."; return; }
        var name = _engine.Aliases[idx].Name;
        _engine.RemoveAlias(name);
        ClearForm();
        Refresh();
        StatusText.Text = $"Deleted '{name}'.";
    }

    private void OnToggle(object? sender, RoutedEventArgs e)
    {
        if (_engine is null) return;
        var idx = ItemsList.SelectedIndex;
        if (idx < 0 || idx >= _engine.Aliases.Count) { StatusText.Text = "Select an alias to toggle."; return; }
        var alias = _engine.Aliases[idx];
        _engine.SetEnabled(alias.Name, !alias.IsEnabled);
        Refresh();
        StatusText.Text = $"'{alias.Name}' {(alias.IsEnabled ? "enabled" : "disabled")}.";
    }

    private void OnAdd(object? sender, RoutedEventArgs e) => ClearForm();

    private void OnClear(object? sender, RoutedEventArgs e) => ClearForm();

    private void ClearForm()
    {
        ItemsList.SelectedIndex = -1;
        NameBox.Text            = string.Empty;
        ExpansionBox.Text       = string.Empty;
        EnabledCheck.IsChecked  = true;
        StatusText.Text         = string.Empty;
    }
}
