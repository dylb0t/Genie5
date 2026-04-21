using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Genie4.Core.Aliases;
using Genie4.Core.Import;

namespace Genie5.Ui;

public partial class AliasesPanel : UserControl
{
    public sealed record AliasRow(string EnabledGlyph, string Name, string Expansion, bool IsEnabled);

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
        var keep = (ItemsList.SelectedItem as AliasRow)?.Name;
        ItemsList.ItemsSource = _engine.Aliases
            .Select(a => new AliasRow(a.IsEnabled ? "✓" : "✗", a.Name, a.Expansion, a.IsEnabled))
            .ToList();
        if (keep is not null)
            ItemsList.SelectedItem = ((IEnumerable<AliasRow>)ItemsList.ItemsSource)
                .FirstOrDefault(r => r.Name == keep);
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_engine is null || ItemsList.SelectedItem is not AliasRow row) return;
        var alias = _engine.Aliases.FirstOrDefault(a => a.Name == row.Name);
        if (alias is null) return;
        NameBox.Text           = alias.Name;
        ExpansionBox.Text      = alias.Expansion;
        EnabledCheck.IsChecked = alias.IsEnabled;
        StatusText.Text        = string.Empty;
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
        if (ItemsList.SelectedItem is not AliasRow row) { StatusText.Text = "Select an alias to delete."; return; }
        _engine.RemoveAlias(row.Name);
        ClearForm();
        Refresh();
        StatusText.Text = $"Deleted '{row.Name}'.";
    }

    private void OnToggle(object? sender, RoutedEventArgs e)
    {
        if (_engine is null) return;
        if (ItemsList.SelectedItem is not AliasRow row) { StatusText.Text = "Select an alias to toggle."; return; }
        var alias = _engine.Aliases.FirstOrDefault(a => a.Name == row.Name);
        if (alias is null) return;
        _engine.SetEnabled(alias.Name, !alias.IsEnabled);
        Refresh();
        StatusText.Text = $"'{alias.Name}' {(alias.IsEnabled ? "enabled" : "disabled")}.";
    }

    private void OnAdd(object? sender, RoutedEventArgs e) => ClearForm();

    private void OnClear(object? sender, RoutedEventArgs e) => ClearForm();

    private void ClearForm()
    {
        ItemsList.SelectedItem = null;
        NameBox.Text           = string.Empty;
        ExpansionBox.Text      = string.Empty;
        EnabledCheck.IsChecked = true;
        StatusText.Text        = string.Empty;
    }

    private async void OnImport(object? sender, RoutedEventArgs e)
    {
        if (_engine is null) return;

        var parent = this.GetVisualRoot() as Window;
        if (parent is null) return;

        var dialog = new OpenFileDialog
        {
            Title         = "Import Aliases",
            AllowMultiple = false,
            Filters       = [new FileDialogFilter { Name = "Alias files", Extensions = ["cfg", "txt"] }],
        };

        var paths = await dialog.ShowAsync(parent);
        if (paths is null || paths.Length == 0) return;

        var result = Genie4Importer.ImportAliases(paths[0], _engine, ImportMode.Merge);
        Refresh();
        StatusText.Text = $"Imported {result.Imported} alias(es).";
    }
}
