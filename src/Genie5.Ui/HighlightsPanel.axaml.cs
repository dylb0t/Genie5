using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Genie4.Core.Highlights;

namespace Genie5.Ui;

public partial class HighlightsPanel : UserControl
{
    private static readonly string[] Colors =
        ["Yellow", "Red", "Green", "Cyan", "Magenta", "Blue", "White"];

    private HighlightEngine? _engine;

    public HighlightsPanel()
    {
        InitializeComponent();
        ColorBox.ItemsSource   = Colors;
        ColorBox.SelectedIndex = 0;
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
            .Select(r => $"{(r.IsEnabled ? "✓" : "✗")}  [{r.ForegroundColor}]  {r.Pattern}")
            .ToList();
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var idx = ItemsList.SelectedIndex;
        if (_engine is null || idx < 0 || idx >= _engine.Rules.Count) return;
        var rule = _engine.Rules[idx];
        PatternBox.Text              = rule.Pattern;
        ColorBox.SelectedItem        = rule.ForegroundColor;
        IsRegexCheck.IsChecked       = rule.IsRegex;
        CaseSensitiveCheck.IsChecked = rule.CaseSensitive;
        EnabledCheck.IsChecked       = rule.IsEnabled;
        StatusText.Text              = string.Empty;
    }

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        if (_engine is null) return;
        var pattern       = PatternBox.Text?.Trim() ?? string.Empty;
        var color         = ColorBox.SelectedItem as string ?? "Yellow";
        var isRegex       = IsRegexCheck.IsChecked == true;
        var caseSensitive = CaseSensitiveCheck.IsChecked == true;
        var enabled       = EnabledCheck.IsChecked == true;

        if (string.IsNullOrEmpty(pattern)) { StatusText.Text = "Pattern is required."; return; }

        if (isRegex)
        {
            try { _ = new Regex(pattern); }
            catch (RegexParseException ex) { StatusText.Text = $"Invalid regex: {ex.Message}"; return; }
        }

        _engine.RemoveRule(pattern);
        _engine.AddRule(pattern, color, isRegex, caseSensitive, enabled);
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

    private void ClearForm()
    {
        ItemsList.SelectedIndex      = -1;
        PatternBox.Text              = string.Empty;
        ColorBox.SelectedIndex       = 0;
        IsRegexCheck.IsChecked       = false;
        CaseSensitiveCheck.IsChecked = false;
        EnabledCheck.IsChecked       = true;
        StatusText.Text              = string.Empty;
    }
}
