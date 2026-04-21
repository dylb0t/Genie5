using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
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

        var (imported, skipped) = ImportFromCfg(paths[0], _engine);
        Refresh();
        _onChanged?.Invoke();
        StatusText.Text = skipped > 0
            ? $"Imported {imported} macro(s), skipped {skipped}."
            : $"Imported {imported} macro(s).";
    }

    // Parses Genie4 "#macro {key} {action}" lines. The key token preserves
    // Genie4's serialized Keys form (e.g. "F1", "Control, F2", "Alt, NumPad5").
    internal static (int Imported, int Skipped) ImportFromCfg(string path, MacroEngine engine)
    {
        var pattern = new Regex(
            @"^\s*#macro\s+\{(?<key>[^{}]*)\}\s+\{(?<action>[^{}]*)\}\s*$",
            RegexOptions.IgnoreCase);

        int imported = 0, skipped = 0;
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("//") || line.StartsWith("#!")) continue;
            if (!line.StartsWith("#macro", StringComparison.OrdinalIgnoreCase)) continue;

            var m = pattern.Match(line);
            if (!m.Success) { skipped++; continue; }

            var key    = m.Groups["key"].Value.Trim();
            var action = m.Groups["action"].Value;
            if (string.IsNullOrEmpty(key)) { skipped++; continue; }

            engine.Add(key, action);
            imported++;
        }
        return (imported, skipped);
    }

    private void ClearForm()
    {
        ItemsList.SelectedIndex = -1;
        KeyBox.Text             = string.Empty;
        ActionBox.Text          = string.Empty;
        StatusText.Text         = string.Empty;
    }
}
