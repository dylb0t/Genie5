using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using Genie4.Core.Highlights;

namespace Genie5.Ui;

public partial class HighlightStringsPanel : UserControl
{
    private static readonly string[] MatchTypes =
        ["String", "Line", "BeginsWith", "Regex"];

    private HighlightEngine? _engine;

    public HighlightStringsPanel()
    {
        InitializeComponent();
        MatchTypeBox.ItemsSource   = MatchTypes;
        MatchTypeBox.SelectedIndex = 0;
        FgColorPicker.Color        = Colors.Yellow;
        BgNoneCheck.IsChecked      = true;
    }

    public void Initialize(HighlightEngine engine)
    {
        _engine = engine;
        Refresh();
    }

    private void Refresh()
    {
        if (_engine is null) return;
        ItemsList.ItemsSource = _engine.Rules
            .Select(r =>
            {
                var bg  = string.IsNullOrEmpty(r.BackgroundColor) ? "" : $"/{r.BackgroundColor}";
                var cls = string.IsNullOrEmpty(r.ClassName) ? "" : $"  [{r.ClassName}]";
                return $"{(r.IsEnabled ? "✓" : "✗")}  [{r.MatchType}]  [{r.ForegroundColor}{bg}]  {r.Pattern}{cls}";
            })
            .ToList();
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var idx = ItemsList.SelectedIndex;
        if (_engine is null || idx < 0 || idx >= _engine.Rules.Count) return;
        var rule = _engine.Rules[idx];
        PatternBox.Text              = rule.Pattern;
        ColorPickerHelpers.LoadColor(FgColorPicker, FgDefaultCheck, rule.ForegroundColor, "Default");
        ColorPickerHelpers.LoadColor(BgColorPicker, BgNoneCheck,    rule.BackgroundColor, "");
        MatchTypeBox.SelectedItem    = rule.MatchType.ToString();
        ClassBox.Text                = rule.ClassName;
        CaseSensitiveCheck.IsChecked = rule.CaseSensitive;
        EnabledCheck.IsChecked       = rule.IsEnabled;
        StatusText.Text              = string.Empty;
    }

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        if (_engine is null) return;
        var pattern       = PatternBox.Text?.Trim() ?? string.Empty;
        var color         = ColorPickerHelpers.ReadColor(FgColorPicker, FgDefaultCheck, "Default");
        var bgColor       = ColorPickerHelpers.ReadColor(BgColorPicker, BgNoneCheck,    "");
        var matchTypeStr  = MatchTypeBox.SelectedItem as string ?? "String";
        var matchType     = Enum.TryParse<HighlightMatchType>(matchTypeStr, out var mt) ? mt : HighlightMatchType.String;
        var className     = ClassBox.Text?.Trim() ?? string.Empty;
        var caseSensitive = CaseSensitiveCheck.IsChecked == true;
        var enabled       = EnabledCheck.IsChecked == true;

        if (string.IsNullOrEmpty(pattern)) { StatusText.Text = "Pattern is required."; return; }

        if (matchType == HighlightMatchType.Regex)
        {
            try { _ = new Regex(pattern); }
            catch (RegexParseException ex) { StatusText.Text = $"Invalid regex: {ex.Message}"; return; }
        }

        _engine.RemoveRule(pattern);
        _engine.AddRule(pattern, color, bgColor, matchType, caseSensitive, enabled, className);
        Refresh();
        StatusText.Text = "Saved.";
    }

    private void OnDelete(object? sender, RoutedEventArgs e)
    {
        if (_engine is null) return;
        var idx = ItemsList.SelectedIndex;
        if (idx < 0 || idx >= _engine.Rules.Count) { StatusText.Text = "Select a highlight to delete."; return; }
        _engine.RemoveRule(_engine.Rules[idx].Pattern);
        ClearForm();
        Refresh();
        StatusText.Text = "Deleted.";
    }

    private void OnToggle(object? sender, RoutedEventArgs e)
    {
        if (_engine is null) return;
        var idx = ItemsList.SelectedIndex;
        if (idx < 0 || idx >= _engine.Rules.Count) { StatusText.Text = "Select a highlight to toggle."; return; }
        var rule = _engine.Rules[idx];
        rule.IsEnabled = !rule.IsEnabled;
        Refresh();
        StatusText.Text = $"Highlight {(rule.IsEnabled ? "enabled" : "disabled")}.";
    }

    private void OnAdd(object? sender, RoutedEventArgs e) => ClearForm();

    private void OnClear(object? sender, RoutedEventArgs e) => ClearForm();

    private async void OnImport(object? sender, RoutedEventArgs e)
    {
        if (_engine is null) return;

        var parent = this.GetVisualRoot() as Window;
        if (parent is null) return;

        var dialog = new OpenFileDialog
        {
            Title         = "Import Highlights",
            AllowMultiple = false,
            Filters       = [new FileDialogFilter { Name = "Highlight files", Extensions = ["cfg", "txt"] }],
        };

        var paths = await dialog.ShowAsync(parent);
        if (paths is null || paths.Length == 0) return;

        var (imported, skipped) = ImportFromCfg(paths[0], _engine);
        Refresh();
        StatusText.Text = skipped > 0
            ? $"Imported {imported} highlight(s), skipped {skipped}."
            : $"Imported {imported} highlight(s).";
    }

    // Parses Genie4-style "#highlight {type} {fg[, bg]} {pattern} [{class}] [{sound}]"
    // lines. Returns (imported, skipped) — skipped counts unparseable or invalid
    // (e.g. bad regex) entries. Sound metadata is ignored; class is captured.
    internal static (int Imported, int Skipped) ImportFromCfg(string path, HighlightEngine engine)
    {
        var pattern = new Regex(
            @"^\s*#highlight\s+\{(?<type>[^{}]*)\}\s+\{(?<colors>[^{}]*)\}\s+\{(?<pattern>[^{}]*)\}(?:\s+\{(?<cls>[^{}]*)\})?(?:\s+\{[^{}]*\})?\s*$",
            RegexOptions.IgnoreCase);

        int imported = 0, skipped = 0;
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("//") || line.StartsWith("#!")) continue;
            if (!line.StartsWith("#highlight", StringComparison.OrdinalIgnoreCase)) continue;

            var m = pattern.Match(line);
            if (!m.Success) { skipped++; continue; }

            var matchType = ParseMatchType(m.Groups["type"].Value);
            if (matchType is null) { skipped++; continue; }

            var (fg, bg) = ParseColors(m.Groups["colors"].Value);
            var rulePattern = m.Groups["pattern"].Value;
            if (string.IsNullOrEmpty(rulePattern) || string.IsNullOrEmpty(fg)) { skipped++; continue; }

            if (matchType == HighlightMatchType.Regex)
            {
                try { _ = new Regex(rulePattern); }
                catch (RegexParseException) { skipped++; continue; }
            }

            var cls = m.Groups["cls"].Success ? m.Groups["cls"].Value : string.Empty;

            engine.RemoveRule(rulePattern);
            engine.AddRule(rulePattern, fg, bg, matchType.Value, caseSensitive: false, isEnabled: true, className: cls);
            imported++;
        }
        return (imported, skipped);
    }

    private static HighlightMatchType? ParseMatchType(string raw) =>
        raw.Trim().ToLowerInvariant() switch
        {
            "string"     => HighlightMatchType.String,
            "line"       => HighlightMatchType.Line,
            "beginswith" => HighlightMatchType.BeginsWith,
            "regexp"     => HighlightMatchType.Regex,
            "regex"      => HighlightMatchType.Regex,
            _            => null,
        };

    private static (string Fg, string Bg) ParseColors(string raw)
    {
        var parts = raw.Split(',', 2);
        var fg = parts[0].Trim();
        var bg = parts.Length > 1 ? parts[1].Trim() : string.Empty;
        return (fg, bg);
    }

    private void ClearForm()
    {
        ItemsList.SelectedIndex      = -1;
        PatternBox.Text              = string.Empty;
        FgColorPicker.Color          = Colors.Yellow;
        FgDefaultCheck.IsChecked     = false;
        BgNoneCheck.IsChecked        = true;
        MatchTypeBox.SelectedIndex   = 0;
        ClassBox.Text                = string.Empty;
        CaseSensitiveCheck.IsChecked = false;
        EnabledCheck.IsChecked       = true;
        StatusText.Text              = string.Empty;
    }
}
