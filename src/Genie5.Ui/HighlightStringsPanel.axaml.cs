using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using Genie4.Core.Highlights;
using Genie4.Core.Import;

namespace Genie5.Ui;

public partial class HighlightStringsPanel : UserControl
{
    public sealed record HighlightRow(
        string EnabledGlyph, string MatchType, string ForegroundColor, string BackgroundColor,
        string Pattern, string ClassName);

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
        var keep = (ItemsList.SelectedItem as HighlightRow)?.Pattern;
        ItemsList.ItemsSource = _engine.Rules
            .Select(r => new HighlightRow(
                r.IsEnabled ? "✓" : "✗",
                r.MatchType.ToString(),
                r.ForegroundColor,
                r.BackgroundColor,
                r.Pattern,
                r.ClassName))
            .ToList();
        if (keep is not null)
            ItemsList.SelectedItem = ((IEnumerable<HighlightRow>)ItemsList.ItemsSource)
                .FirstOrDefault(r => r.Pattern == keep);
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_engine is null || ItemsList.SelectedItem is not HighlightRow row) return;
        var rule = _engine.Rules.FirstOrDefault(r => r.Pattern == row.Pattern);
        if (rule is null) return;
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
        if (ItemsList.SelectedItem is not HighlightRow row) { StatusText.Text = "Select a highlight to delete."; return; }
        _engine.RemoveRule(row.Pattern);
        ClearForm();
        Refresh();
        StatusText.Text = "Deleted.";
    }

    private void OnToggle(object? sender, RoutedEventArgs e)
    {
        if (_engine is null) return;
        if (ItemsList.SelectedItem is not HighlightRow row) { StatusText.Text = "Select a highlight to toggle."; return; }
        var rule = _engine.Rules.FirstOrDefault(r => r.Pattern == row.Pattern);
        if (rule is null) return;
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

        var result = Genie4Importer.ImportHighlights(paths[0], _engine, ImportMode.Merge);
        Refresh();
        StatusText.Text = result.Skipped > 0
            ? $"Imported {result.Imported} highlight(s), skipped {result.Skipped}."
            : $"Imported {result.Imported} highlight(s).";
    }

    private void ClearForm()
    {
        ItemsList.SelectedItem       = null;
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
