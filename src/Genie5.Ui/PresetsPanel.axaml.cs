using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Genie4.Core.Presets;

namespace Genie5.Ui;

public partial class PresetsPanel : UserControl
{
    public sealed record PresetRow(string Id, string ForegroundColor, string BackgroundColor, string LineGlyph);

    private PresetEngine? _engine;
    private Action?       _onChanged;

    public PresetsPanel()
    {
        InitializeComponent();
    }

    public void Initialize(PresetEngine engine, Action? onChanged = null)
    {
        _engine    = engine;
        _onChanged = onChanged;
        Refresh();
    }

    private void Refresh()
    {
        if (_engine is null) return;
        var keep = (PresetList.SelectedItem as PresetRow)?.Id;
        PresetList.ItemsSource = _engine.Presets
            .OrderBy(kv => kv.Key)
            .Select(kv => new PresetRow(kv.Key, kv.Value.ForegroundColor, kv.Value.BackgroundColor, kv.Value.HighlightLine ? "✓" : ""))
            .ToList();
        if (keep is not null)
            PresetList.SelectedItem = ((IEnumerable<PresetRow>)PresetList.ItemsSource)
                .FirstOrDefault(r => r.Id == keep);
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_engine is null || PresetList.SelectedItem is not PresetRow row) return;
        var rule = _engine.Get(row.Id);
        if (rule is null) return;

        IdLabel.Text = row.Id;
        ColorPickerHelpers.LoadColor(FgColorPicker, FgDefaultCheck, rule.ForegroundColor, "Default");
        ColorPickerHelpers.LoadColor(BgColorPicker, BgNoneCheck,    rule.BackgroundColor, "");
        HighlightLineCheck.IsChecked = rule.HighlightLine;
        UpdatePreview(rule.ForegroundColor, rule.BackgroundColor);
        StatusText.Text = string.Empty;
    }

    private void OnApply(object? sender, RoutedEventArgs e)
    {
        if (_engine is null || PresetList.SelectedItem is not PresetRow row) return;
        var id = row.Id;

        var fg = ColorPickerHelpers.ReadColor(FgColorPicker, FgDefaultCheck, "Default");
        var bg = ColorPickerHelpers.ReadColor(BgColorPicker, BgNoneCheck,    "");

        _engine.Apply(new PresetRule
        {
            Id              = id,
            ForegroundColor = fg,
            BackgroundColor = bg,
            HighlightLine   = HighlightLineCheck.IsChecked == true,
        });

        UpdatePreview(fg, bg);
        Refresh();
        _onChanged?.Invoke();
        StatusText.Text = "Applied.";
    }

    private void OnReset(object? sender, RoutedEventArgs e)
    {
        if (_engine is null || PresetList.SelectedItem is not PresetRow row) return;
        var id = row.Id;

        var fresh = new PresetEngine();
        var rule  = fresh.Get(id);
        if (rule is null) { StatusText.Text = "No default for this preset."; return; }

        _engine.Apply(rule);
        ColorPickerHelpers.LoadColor(FgColorPicker, FgDefaultCheck, rule.ForegroundColor, "Default");
        ColorPickerHelpers.LoadColor(BgColorPicker, BgNoneCheck,    rule.BackgroundColor, "");
        HighlightLineCheck.IsChecked = rule.HighlightLine;
        UpdatePreview(rule.ForegroundColor, rule.BackgroundColor);
        Refresh();
        _onChanged?.Invoke();
        StatusText.Text = "Reset to default.";
    }

    private void UpdatePreview(string fg, string bg)
    {
        PreviewText.Foreground = ToBrush(fg) ?? Brushes.LightGray;
        PreviewBorder.Background = string.IsNullOrEmpty(bg) ? Brushes.Transparent : (ToBrush(bg) ?? Brushes.Transparent);
    }

    private static IBrush? ToBrush(string name)
    {
        if (string.IsNullOrEmpty(name) || name == "Default") return null;
        return Color.TryParse(name, out var c) ? new SolidColorBrush(c)
             : Brush.Parse(name);
    }
}
