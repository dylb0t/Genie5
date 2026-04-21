using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
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

        var (imported, skipped) = ImportFromCfg(paths[0], _engine);
        Refresh();
        _onChanged?.Invoke();
        StatusText.Text = skipped > 0
            ? $"Imported {imported} substitute(s), skipped {skipped}."
            : $"Imported {imported} substitute(s).";
    }

    // Parses Genie4 "#subs {pattern} {replacement} [{class}]" lines.
    // Recognises the legacy /pattern/i ignore-case suffix on the first token.
    internal static (int Imported, int Skipped) ImportFromCfg(string path, SubstituteEngine engine)
    {
        var pattern = new Regex(
            @"^\s*#subs\s+\{(?<pat>[^{}]*)\}\s+\{(?<repl>[^{}]*)\}(?:\s+\{(?<cls>[^{}]*)\})?\s*$",
            RegexOptions.IgnoreCase);

        int imported = 0, skipped = 0;
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("//") || line.StartsWith("#!")) continue;
            if (!line.StartsWith("#subs", StringComparison.OrdinalIgnoreCase)) continue;

            var m = pattern.Match(line);
            if (!m.Success) { skipped++; continue; }

            var pat = m.Groups["pat"].Value;
            bool caseInsensitive = false;
            if (pat.StartsWith('/')) pat = pat[1..];
            if (pat.EndsWith("/i", StringComparison.OrdinalIgnoreCase)) { caseInsensitive = true; pat = pat[..^2]; }
            else if (pat.EndsWith('/')) pat = pat[..^1];

            if (string.IsNullOrEmpty(pat)) { skipped++; continue; }
            try { _ = new Regex(pat); }
            catch (RegexParseException) { skipped++; continue; }

            var repl = m.Groups["repl"].Value;
            var cls  = m.Groups["cls"].Success ? m.Groups["cls"].Value : string.Empty;

            engine.RemoveRule(pat);
            engine.AddRule(pat, repl, caseSensitive: !caseInsensitive, isEnabled: true, className: cls);
            imported++;
        }
        return (imported, skipped);
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
