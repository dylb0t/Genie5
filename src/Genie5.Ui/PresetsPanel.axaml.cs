using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Genie4.Core.Presets;

namespace Genie5.Ui;

public partial class PresetsPanel : UserControl
{
    private static readonly string[] ColorOptions =
    [
        "Default",
        "White", "WhiteSmoke", "LightGray", "Silver", "Gray",
        "Black", "DimGray",
        "Yellow", "Khaki", "Gold", "Orange",
        "Red", "IndianRed", "Maroon",
        "LightGreen", "Green", "PaleGreen", "GreenYellow",
        "Cyan", "PaleTurquoise",
        "CornflowerBlue", "Blue", "Navy", "MediumBlue",
        "Magenta", "Orchid", "Purple",
    ];

    private PresetEngine? _engine;

    public PresetsPanel()
    {
        InitializeComponent();
        FgColorBox.ItemsSource = ColorOptions;
        BgColorBox.ItemsSource = new[] { "" }.Concat(ColorOptions).ToArray();
    }

    public void Initialize(PresetEngine engine)
    {
        _engine = engine;
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
        FgColorBox.SelectedItem        = rule.ForegroundColor;
        BgColorBox.SelectedItem        = string.IsNullOrEmpty(rule.BackgroundColor) ? "" : rule.BackgroundColor;
        HighlightLineCheck.IsChecked   = rule.HighlightLine;
        UpdatePreview(rule.ForegroundColor, rule.BackgroundColor);
        StatusText.Text = string.Empty;
    }

    private void OnApply(object? sender, RoutedEventArgs e)
    {
        if (_engine is null || PresetList.SelectedItem is not string id) return;

        var fg = FgColorBox.SelectedItem as string ?? "Default";
        var bg = BgColorBox.SelectedItem as string ?? string.Empty;

        _engine.Apply(new PresetRule
        {
            Id              = id,
            ForegroundColor = fg,
            BackgroundColor = bg,
            HighlightLine   = HighlightLineCheck.IsChecked == true,
        });

        UpdatePreview(fg, bg);
        StatusText.Text = "Applied.";
    }

    private void OnReset(object? sender, RoutedEventArgs e)
    {
        if (_engine is null || PresetList.SelectedItem is not string id) return;

        var fresh = new PresetEngine();
        var rule  = fresh.Get(id);
        if (rule is null) { StatusText.Text = "No default for this preset."; return; }

        _engine.Apply(rule);
        FgColorBox.SelectedItem      = rule.ForegroundColor;
        BgColorBox.SelectedItem      = string.IsNullOrEmpty(rule.BackgroundColor) ? "" : rule.BackgroundColor;
        HighlightLineCheck.IsChecked = rule.HighlightLine;
        UpdatePreview(rule.ForegroundColor, rule.BackgroundColor);
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
