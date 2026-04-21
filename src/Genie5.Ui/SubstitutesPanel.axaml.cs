using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Genie4.Core.Import;
using Genie4.Core.Substitutes;

namespace Genie5.Ui;

public partial class SubstitutesPanel : UserControl
{
    private SubstituteEngine? _engine;
    private Action?           _onChanged;

    public SubstitutesPanel() => InitializeComponent();

    public void Initialize(SubstituteEngine engine, Action? onChanged = null)
    {
        _engine    = engine;
        _onChanged = onChanged;
        Refresh();
    }

    private void Refresh()
    {
        if (_engine is null) return;
        ItemsList.ItemsSource = _engine.Rules
            .Select(r =>
            {
                var cls = string.IsNullOrEmpty(r.ClassName) ? "" : $"  [{r.ClassName}]";
                return $"{(r.IsEnabled ? "✓" : "✗")}  {r.Pattern}  →  {r.Replacement}{cls}";
            })
            .ToList();
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var idx = ItemsList.SelectedIndex;
        if (_engine is null || idx < 0 || idx >= _engine.Rules.Count) return;
        var rule = _engine.Rules[idx];
        PatternBox.Text              = rule.Pattern;
        ReplacementBox.Text          = rule.Replacement;
        ClassBox.Text                = rule.ClassName;
        CaseSensitiveCheck.IsChecked = rule.CaseSensitive;
        EnabledCheck.IsChecked       = rule.IsEnabled;
        StatusText.Text              = string.Empty;
    }

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        if (_engine is null) return;
        var pattern       = PatternBox.Text?.Trim() ?? string.Empty;
        var replacement   = ReplacementBox.Text ?? string.Empty;
        var className     = ClassBox.Text?.Trim() ?? string.Empty;
        var caseSensitive = CaseSensitiveCheck.IsChecked == true;
        var enabled       = EnabledCheck.IsChecked == true;

        if (string.IsNullOrEmpty(pattern)) { StatusText.Text = "Pattern is required."; return; }

        try { _ = new Regex(pattern); }
        catch (RegexParseException ex) { StatusText.Text = $"Invalid regex: {ex.Message}"; return; }

        _engine.RemoveRule(pattern);
        _engine.AddRule(pattern, replacement, caseSensitive, enabled, className);
        Refresh();
        _onChanged?.Invoke();
        StatusText.Text = "Saved.";
    }

    private void OnDelete(object? sender, RoutedEventArgs e)
    {
        if (_engine is null) return;
        var idx = ItemsList.SelectedIndex;
        if (idx < 0 || idx >= _engine.Rules.Count) { StatusText.Text = "Select a substitute to delete."; return; }
        _engine.RemoveRule(_engine.Rules[idx].Pattern);
        ClearForm();
        Refresh();
        _onChanged?.Invoke();
        StatusText.Text = "Deleted.";
    }

    private void OnToggle(object? sender, RoutedEventArgs e)
    {
        if (_engine is null) return;
        var idx = ItemsList.SelectedIndex;
        if (idx < 0 || idx >= _engine.Rules.Count) { StatusText.Text = "Select a substitute to toggle."; return; }
        var rule = _engine.Rules[idx];
        rule.IsEnabled = !rule.IsEnabled;
        Refresh();
        _onChanged?.Invoke();
        StatusText.Text = $"Substitute {(rule.IsEnabled ? "enabled" : "disabled")}.";
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
            Title         = "Import Substitutes",
            AllowMultiple = false,
            Filters       = [new FileDialogFilter { Name = "Substitute files", Extensions = ["cfg", "txt"] }],
        };

        var paths = await dialog.ShowAsync(parent);
        if (paths is null || paths.Length == 0) return;

        var result = Genie4Importer.ImportSubstitutes(paths[0], _engine, ImportMode.Merge);
        Refresh();
        _onChanged?.Invoke();
        StatusText.Text = result.Skipped > 0
            ? $"Imported {result.Imported} substitute(s), skipped {result.Skipped}."
            : $"Imported {result.Imported} substitute(s).";
    }

    private void ClearForm()
    {
        ItemsList.SelectedIndex      = -1;
        PatternBox.Text              = string.Empty;
        ReplacementBox.Text          = string.Empty;
        ClassBox.Text                = string.Empty;
        CaseSensitiveCheck.IsChecked = false;
        EnabledCheck.IsChecked       = true;
        StatusText.Text              = string.Empty;
    }
}
