using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Genie4.Core.Presets;

namespace Genie5.Ui;

public partial class PresetsPanel : UserControl
{
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
        PresetList.ItemsSource = _engine.Presets.Keys.OrderBy(k => k).ToList();
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_engine is null || PresetList.SelectedItem is not string id) return;
        var rule = _engine.Get(id);
        if (rule is null) return;

        IdLabel.Text = id;
        ColorPickerHelpers.LoadColor(FgColorPicker, FgDefaultCheck, rule.ForegroundColor, "Default");
        ColorPickerHelpers.LoadColor(BgColorPicker, BgNoneCheck,    rule.BackgroundColor, "");
        HighlightLineCheck.IsChecked = rule.HighlightLine;
        UpdatePreview(rule.ForegroundColor, rule.BackgroundColor);
        StatusText.Text = string.Empty;
    }

    private void OnApply(object? sender, RoutedEventArgs e)
    {
        if (_engine is null || PresetList.SelectedItem is not string id) return;

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
        _onChanged?.Invoke();
        StatusText.Text = "Applied.";
    }

    private void OnReset(object? sender, RoutedEventArgs e)
    {
        if (_engine is null || PresetList.SelectedItem is not string id) return;

        var fresh = new PresetEngine();
        var rule  = fresh.Get(id);
        if (rule is null) { StatusText.Text = "No default for this preset."; return; }

        _engine.Apply(rule);
        ColorPickerHelpers.LoadColor(FgColorPicker, FgDefaultCheck, rule.ForegroundColor, "Default");
        ColorPickerHelpers.LoadColor(BgColorPicker, BgNoneCheck,    rule.BackgroundColor, "");
        HighlightLineCheck.IsChecked = rule.HighlightLine;
        UpdatePreview(rule.ForegroundColor, rule.BackgroundColor);
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
