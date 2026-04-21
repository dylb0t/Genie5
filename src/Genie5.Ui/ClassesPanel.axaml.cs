using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Genie4.Core.Classes;

namespace Genie5.Ui;

public partial class ClassesPanel : UserControl
{
    private ClassEngine? _engine;
    private Action?      _onChanged;

    public ClassesPanel() => InitializeComponent();

    public void Initialize(ClassEngine engine, Action? onChanged = null)
    {
        _engine    = engine;
        _onChanged = onChanged;
        Refresh();
    }

    private void Refresh()
    {
        if (_engine is null) return;
        ItemsList.ItemsSource = _engine.GetAll()
            .Select(kv => $"{(kv.Value ? "✓" : "✗")}  {kv.Key}")
            .ToList();
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_engine is null) return;
        var idx = ItemsList.SelectedIndex;
        if (idx < 0) return;
        var entries = _engine.GetAll().ToList();
        if (idx >= entries.Count) return;
        var kv = entries[idx];
        NameBox.Text         = kv.Key;
        ActiveCheck.IsChecked = kv.Value;
        StatusText.Text      = string.Empty;
    }

    private void OnRefresh(object? sender, RoutedEventArgs e) => Refresh();

    private void OnActivateAll(object? sender, RoutedEventArgs e)
    {
        if (_engine is null) return;
        _engine.ActivateAll();
        Refresh();
        _onChanged?.Invoke();
        StatusText.Text = "Activated all classes.";
    }

    private void OnDeactivateAll(object? sender, RoutedEventArgs e)
    {
        if (_engine is null) return;
        _engine.DeactivateAll();
        Refresh();
        _onChanged?.Invoke();
        StatusText.Text = "Deactivated all classes.";
    }

    private void OnRemove(object? sender, RoutedEventArgs e)
    {
        if (_engine is null) return;
        var name = NameBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(name))
        {
            var idx = ItemsList.SelectedIndex;
            if (idx < 0) { StatusText.Text = "Select a class to remove."; return; }
            var entries = _engine.GetAll().ToList();
            if (idx >= entries.Count) return;
            name = entries[idx].Key;
        }
        if (name.Equals("default", StringComparison.OrdinalIgnoreCase))
        {
            StatusText.Text = "Cannot remove the default class.";
            return;
        }
        if (_engine.Remove(name))
        {
            ClearForm();
            Refresh();
            _onChanged?.Invoke();
            StatusText.Text = $"Removed '{name}'.";
        }
        else
        {
            StatusText.Text = $"'{name}' not found.";
        }
    }

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        if (_engine is null) return;
        var name = NameBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(name)) { StatusText.Text = "Class name is required."; return; }
        if (name.Equals("default", StringComparison.OrdinalIgnoreCase))
        {
            StatusText.Text = "The default class is always active.";
            return;
        }
        _engine.Set(name, ActiveCheck.IsChecked == true);
        Refresh();
        _onChanged?.Invoke();
        StatusText.Text = "Saved.";
    }

    private void OnClear(object? sender, RoutedEventArgs e) => ClearForm();

    private async void OnImport(object? sender, RoutedEventArgs e)
    {
        if (_engine is null) return;

        var parent = this.GetVisualRoot() as Window;
        if (parent is null) return;

        var dialog = new OpenFileDialog
        {
            Title         = "Import Classes",
            AllowMultiple = false,
            Filters       = [new FileDialogFilter { Name = "Class files", Extensions = ["cfg", "txt"] }],
        };

        var paths = await dialog.ShowAsync(parent);
        if (paths is null || paths.Length == 0) return;

        var (imported, skipped) = ImportFromCfg(paths[0], _engine);
        Refresh();
        _onChanged?.Invoke();
        StatusText.Text = skipped > 0
            ? $"Imported {imported} class(es), skipped {skipped}."
            : $"Imported {imported} class(es).";
    }

    // Parses Genie4 "#class {name} {True|False|on|off}" lines.
    internal static (int Imported, int Skipped) ImportFromCfg(string path, ClassEngine engine)
    {
        var pattern = new Regex(
            @"^\s*#class\s+\{(?<name>[^{}]*)\}\s+\{(?<state>[^{}]*)\}\s*$",
            RegexOptions.IgnoreCase);

        int imported = 0, skipped = 0;
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("//") || line.StartsWith("#!")) continue;
            if (!line.StartsWith("#class", StringComparison.OrdinalIgnoreCase)) continue;

            var m = pattern.Match(line);
            if (!m.Success) { skipped++; continue; }

            var name  = m.Groups["name"].Value.Trim();
            var state = m.Groups["state"].Value.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(name) || name.Equals("default", StringComparison.OrdinalIgnoreCase))
            {
                skipped++;
                continue;
            }

            bool active = state switch
            {
                "true" or "on"  or "yes" or "1" => true,
                "false" or "off" or "no"  or "0" => false,
                _ => true,
            };
            engine.Set(name, active);
            imported++;
        }
        return (imported, skipped);
    }

    private void ClearForm()
    {
        ItemsList.SelectedIndex = -1;
        NameBox.Text            = string.Empty;
        ActiveCheck.IsChecked   = true;
        StatusText.Text         = string.Empty;
    }
}
