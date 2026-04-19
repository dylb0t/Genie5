using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
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

        var imported = ImportFromCfg(paths[0], _engine);
        Refresh();
        StatusText.Text = $"Imported {imported} alias(es).";
    }

    // Parses Genie4-style "#alias {name} {expansion}" lines. An optional
    // add/delete keyword between #alias and the name is accepted and treated
    // as an add (delete lines are ignored to avoid destroying existing rules).
    internal static int ImportFromCfg(string path, AliasEngine engine)
    {
        var pattern = new Regex(
            @"^\s*#alias(?:\s+(?<verb>add|delete))?\s+\{(?<name>[^}]*)\}\s+\{(?<expansion>.*)\}\s*$",
            RegexOptions.IgnoreCase);

        int count = 0;
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("//") || line.StartsWith("#!")) continue;

            var m = pattern.Match(line);
            if (!m.Success) continue;

            var verb      = m.Groups["verb"].Value.ToLowerInvariant();
            if (verb == "delete") continue;

            var name      = m.Groups["name"].Value;
            var expansion = m.Groups["expansion"].Value;
            if (string.IsNullOrEmpty(name)) continue;

            engine.RemoveAlias(name);
            engine.AddAlias(name, expansion, isEnabled: true);
            count++;
        }
        return count;
    }
}
