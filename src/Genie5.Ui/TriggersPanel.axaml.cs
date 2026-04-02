using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Genie4.Core.Triggers;

namespace Genie5.Ui;

public partial class TriggersPanel : UserControl
{
    private TriggerEngineFinal? _engine;

    public TriggersPanel() => InitializeComponent();

    public void Initialize(TriggerEngineFinal engine)
    {
        _engine = engine;
        Refresh();
    }

    private void Refresh()
    {
        if (_engine is null) return;
        ItemsList.ItemsSource = _engine.Triggers
            .Select(t => $"{(t.IsEnabled ? "✓" : "✗")}  {t.Pattern}  →  {t.Action}")
            .ToList();
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var idx = ItemsList.SelectedIndex;
        if (_engine is null || idx < 0 || idx >= _engine.Triggers.Count) return;
        var trigger = _engine.Triggers[idx];
        PatternBox.Text             = trigger.Pattern;
        ActionBox.Text              = trigger.Action;
        CaseSensitiveCheck.IsChecked = trigger.CaseSensitive;
        EnabledCheck.IsChecked      = trigger.IsEnabled;
        StatusText.Text             = string.Empty;
    }

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        if (_engine is null) return;
        var pattern       = PatternBox.Text?.Trim() ?? string.Empty;
        var action        = ActionBox.Text?.Trim() ?? string.Empty;
        var caseSensitive = CaseSensitiveCheck.IsChecked == true;
        var enabled       = EnabledCheck.IsChecked == true;

        if (string.IsNullOrEmpty(pattern)) { StatusText.Text = "Pattern is required."; return; }

        try { _ = new Regex(pattern); }
        catch (RegexParseException ex) { StatusText.Text = $"Invalid regex: {ex.Message}"; return; }

        _engine.RemoveTrigger(pattern);
        _engine.AddTrigger(pattern, action, caseSensitive, enabled);
        Refresh();
        StatusText.Text = "Saved.";
    }

    private void OnDelete(object? sender, RoutedEventArgs e)
    {
        if (_engine is null) return;
        var idx = ItemsList.SelectedIndex;
        if (idx < 0 || idx >= _engine.Triggers.Count) { StatusText.Text = "Select a trigger to delete."; return; }
        var pattern = _engine.Triggers[idx].Pattern;
        _engine.RemoveTrigger(pattern);
        ClearForm();
        Refresh();
        StatusText.Text = "Deleted.";
    }

    private void OnToggle(object? sender, RoutedEventArgs e)
    {
        if (_engine is null) return;
        var idx = ItemsList.SelectedIndex;
        if (idx < 0 || idx >= _engine.Triggers.Count) { StatusText.Text = "Select a trigger to toggle."; return; }
        var trigger = _engine.Triggers[idx];
        _engine.SetEnabled(trigger.Pattern, !trigger.IsEnabled);
        Refresh();
        StatusText.Text = $"Trigger {(trigger.IsEnabled ? "enabled" : "disabled")}.";
    }

    private void OnAdd(object? sender, RoutedEventArgs e) => ClearForm();

    private void OnClear(object? sender, RoutedEventArgs e) => ClearForm();

    private void ClearForm()
    {
        ItemsList.SelectedIndex      = -1;
        PatternBox.Text              = string.Empty;
        ActionBox.Text               = string.Empty;
        CaseSensitiveCheck.IsChecked = false;
        EnabledCheck.IsChecked       = true;
        StatusText.Text              = string.Empty;
    }
}
