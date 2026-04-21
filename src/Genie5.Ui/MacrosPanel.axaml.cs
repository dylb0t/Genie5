using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Genie4.Core.Import;
using Genie4.Core.Macros;

namespace Genie5.Ui;

public partial class MacrosPanel : UserControl
{
    private MacroEngine? _engine;
    private Action?      _onChanged;

    public MacrosPanel() => InitializeComponent();

    public void Initialize(MacroEngine engine, Action? onChanged = null)
    {
        _engine    = engine;
        _onChanged = onChanged;
        Refresh();
    }

    private void Refresh()
    {
        if (_engine is null) return;
        ItemsList.ItemsSource = _engine.Rules
            .OrderBy(r => r.Key, StringComparer.OrdinalIgnoreCase)
            .Select(r => $"{r.Key}  →  {r.Action}")
            .ToList();
    }

    private MacroRule? SelectedRule()
    {
        if (_engine is null) return null;
        var idx = ItemsList.SelectedIndex;
        if (idx < 0) return null;
        return _engine.Rules
            .OrderBy(r => r.Key, StringComparer.OrdinalIgnoreCase)
            .ElementAtOrDefault(idx);
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var rule = SelectedRule();
        if (rule is null) return;
        KeyBox.Text     = rule.Key;
        ActionBox.Text  = rule.Action;
        StatusText.Text = string.Empty;
    }

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        if (_engine is null) return;
        var key    = KeyBox.Text?.Trim() ?? string.Empty;
        var action = ActionBox.Text ?? string.Empty;
        if (string.IsNullOrEmpty(key)) { StatusText.Text = "Key is required."; return; }

        _engine.Add(key, action);
        Refresh();
        _onChanged?.Invoke();
        StatusText.Text = $"Saved '{key}'.";
    }

    private void OnDelete(object? sender, RoutedEventArgs e)
    {
        if (_engine is null) return;
        var rule = SelectedRule();
        if (rule is null) { StatusText.Text = "Select a macro to delete."; return; }
        _engine.Remove(rule.Key);
        ClearForm();
        Refresh();
        _onChanged?.Invoke();
        StatusText.Text = $"Deleted '{rule.Key}'.";
    }

    private void OnAdd(object? sender, RoutedEventArgs e)   => ClearForm();
    private void OnClear(object? sender, RoutedEventArgs e) => ClearForm();

    private async void OnImport(object? sender, RoutedEventArgs e)
    {
        if (_engine is null) return;
        var parent = this.GetVisualRoot() as Window;
        if (parent is null) return;

        var dialog = new OpenFileDialog
        {
            Title         = "Import Macros",
            AllowMultiple = false,
            Filters       = [new FileDialogFilter { Name = "Macro files", Extensions = ["cfg", "txt"] }],
        };

        var paths = await dialog.ShowAsync(parent);
        if (paths is null || paths.Length == 0) return;

        var result = Genie4Importer.ImportMacros(paths[0], _engine, ImportMode.Merge);
        Refresh();
        _onChanged?.Invoke();
        StatusText.Text = result.Skipped > 0
            ? $"Imported {result.Imported} macro(s), skipped {result.Skipped}."
            : $"Imported {result.Imported} macro(s).";
    }

    private void ClearForm()
    {
        ItemsList.SelectedIndex = -1;
        KeyBox.Text             = string.Empty;
        ActionBox.Text          = string.Empty;
        StatusText.Text         = string.Empty;
    }
}
