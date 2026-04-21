using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Genie4.Core.Import;
using Genie4.Core.Variables;

namespace Genie5.Ui;

public partial class VariablesPanel : UserControl
{
    public sealed record VariableRow(string Name, string Value);

    private VariableStore? _store;
    private Action?        _onChanged;

    public VariablesPanel() => InitializeComponent();

    public void Initialize(VariableStore store, Action onChanged)
    {
        _store     = store;
        _onChanged = onChanged;
        Refresh();
    }

    private void Refresh()
    {
        if (_store is null) return;
        var keep = (ItemsList.SelectedItem as VariableRow)?.Name;
        ItemsList.ItemsSource = _store.GetAll().Values
            .OrderBy(v => v.Name, StringComparer.OrdinalIgnoreCase)
            .Select(v => new VariableRow(v.Name, v.Value))
            .ToList();
        if (keep is not null)
            ItemsList.SelectedItem = ((IEnumerable<VariableRow>)ItemsList.ItemsSource)
                .FirstOrDefault(r => r.Name == keep);
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_store is null || ItemsList.SelectedItem is not VariableRow row) return;
        NameBox.Text    = row.Name;
        ValueBox.Text   = row.Value;
        StatusText.Text = string.Empty;
    }

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        if (_store is null) return;
        var name  = NameBox.Text?.Trim() ?? string.Empty;
        var value = ValueBox.Text ?? string.Empty;

        if (string.IsNullOrEmpty(name)) { StatusText.Text = "Name is required."; return; }

        _store.Set(name, value);
        _onChanged?.Invoke();
        Refresh();
        StatusText.Text = $"Saved '{name}'.";
    }

    private void OnDelete(object? sender, RoutedEventArgs e)
    {
        if (_store is null) return;
        if (ItemsList.SelectedItem is not VariableRow row)
        {
            StatusText.Text = "Select a variable to delete.";
            return;
        }
        _store.Remove(row.Name);
        _onChanged?.Invoke();
        ClearForm();
        Refresh();
        StatusText.Text = $"Deleted '{row.Name}'.";
    }

    private void OnAdd(object? sender, RoutedEventArgs e) => ClearForm();

    private void OnClear(object? sender, RoutedEventArgs e) => ClearForm();

    private void ClearForm()
    {
        ItemsList.SelectedItem = null;
        NameBox.Text           = string.Empty;
        ValueBox.Text          = string.Empty;
        StatusText.Text        = string.Empty;
    }

    private async void OnImport(object? sender, RoutedEventArgs e)
    {
        if (_store is null) return;

        var parent = this.GetVisualRoot() as Window;
        if (parent is null) return;

        var dialog = new OpenFileDialog
        {
            Title         = "Import Variables",
            AllowMultiple = false,
            Filters       = [new FileDialogFilter { Name = "Variable files", Extensions = ["cfg", "txt"] }],
        };

        var paths = await dialog.ShowAsync(parent);
        if (paths is null || paths.Length == 0) return;

        var result = Genie4Importer.ImportVariables(paths[0], _store, ImportMode.Merge);
        _onChanged?.Invoke();
        Refresh();
        StatusText.Text = result.Skipped > 0
            ? $"Imported {result.Imported} variable(s), skipped {result.Skipped}."
            : $"Imported {result.Imported} variable(s).";
    }
}
