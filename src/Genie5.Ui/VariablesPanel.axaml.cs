using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Genie4.Core.Import;
using Genie4.Core.Variables;

namespace Genie5.Ui;

public partial class VariablesPanel : UserControl
{
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
        ItemsList.ItemsSource = _store.GetAll().Values
            .OrderBy(v => v.Name, StringComparer.OrdinalIgnoreCase)
            .Select(v => $"{v.Name} = {v.Value}")
            .ToList();
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var idx = ItemsList.SelectedIndex;
        if (_store is null || idx < 0) return;
        var ordered = _store.GetAll().Values
            .OrderBy(v => v.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (idx >= ordered.Count) return;
        var v = ordered[idx];
        NameBox.Text    = v.Name;
        ValueBox.Text   = v.Value;
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
        var idx = ItemsList.SelectedIndex;
        if (idx < 0)
        {
            StatusText.Text = "Select a variable to delete.";
            return;
        }
        var ordered = _store.GetAll().Values
            .OrderBy(v => v.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (idx >= ordered.Count) return;
        var name = ordered[idx].Name;
        _store.Remove(name);
        _onChanged?.Invoke();
        ClearForm();
        Refresh();
        StatusText.Text = $"Deleted '{name}'.";
    }

    private void OnAdd(object? sender, RoutedEventArgs e) => ClearForm();

    private void OnClear(object? sender, RoutedEventArgs e) => ClearForm();

    private void ClearForm()
    {
        ItemsList.SelectedIndex = -1;
        NameBox.Text            = string.Empty;
        ValueBox.Text           = string.Empty;
        StatusText.Text         = string.Empty;
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
