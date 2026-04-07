using Avalonia.Controls;
using Avalonia.Interactivity;
using Genie4.Core.Layout;

namespace Genie5.Ui;

public partial class LayoutPanel : UserControl
{
    private static readonly string[] FgColors =
    [
        "Default", "LightGray", "White", "WhiteSmoke",
        "Yellow", "Khaki", "Red", "IndianRed",
        "Green", "LightGreen", "Cyan", "PaleTurquoise",
        "Blue", "CornflowerBlue", "Magenta", "Orchid",
        "Silver", "Gray", "Black",
    ];

    private static readonly string[] BgColors =
    [
        "(none)", "Black", "DimGray", "Navy", "DarkRed", "DarkGreen",
        "DarkCyan", "DarkMagenta", "DarkBlue", "Maroon", "DarkSlateGray",
        "MidnightBlue", "DarkOliveGreen",
    ];

    private WindowSettingsStore? _store;
    private WindowSettings? _current;

    public LayoutPanel()
    {
        InitializeComponent();
        FgColorBox.ItemsSource = FgColors;
        BgColorBox.ItemsSource = BgColors;
    }

    public void Initialize(WindowSettingsStore store)
    {
        _store = store;
        WindowList.ItemsSource = store.All.Values
            .Select(s => s.DefaultTitle)
            .ToList();
        WindowList.SelectedIndex = 0;
    }

    private void OnWindowSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (_store is null || WindowList.SelectedIndex < 0) return;

        var id = _store.All.Keys.ElementAtOrDefault(WindowList.SelectedIndex);
        if (id is null) return;

        _current = _store.Get(id);
        LoadForm(_current);
        StatusText.Text = string.Empty;
    }

    private void LoadForm(WindowSettings s)
    {
        TitleBox.Text             = s.DisplayTitle;
        FontFamilyBox.Text        = s.FontFamily;
        FontSizeBox.Text          = s.FontSize.ToString("G");
        FgColorBox.SelectedItem   = FgColors.Contains(s.Foreground) ? s.Foreground : "Default";
        BgColorBox.SelectedItem   = string.IsNullOrEmpty(s.Background) ? "(none)"
                                    : BgColors.Contains(s.Background) ? s.Background : "(none)";
        TimestampCheck.IsChecked  = s.Timestamp;
    }

    private void OnApply(object? sender, RoutedEventArgs e)
    {
        if (_current is null) { StatusText.Text = "Select a window first."; return; }

        var title     = TitleBox.Text?.Trim() ?? string.Empty;
        var fontFam   = FontFamilyBox.Text?.Trim() ?? string.Empty;
        var fontSzTxt = FontSizeBox.Text?.Trim() ?? string.Empty;
        var fg        = FgColorBox.SelectedItem as string ?? "Default";
        var bgRaw     = BgColorBox.SelectedItem as string ?? "(none)";
        var bg        = bgRaw == "(none)" ? string.Empty : bgRaw;
        var ts        = TimestampCheck.IsChecked == true;

        if (!double.TryParse(fontSzTxt, out double fontSize) || fontSize < 6 || fontSize > 72)
        {
            StatusText.Text = "Font size must be a number between 6 and 72.";
            return;
        }

        _current.DisplayTitle = string.IsNullOrEmpty(title) ? _current.DefaultTitle : title;
        _current.FontFamily   = string.IsNullOrEmpty(fontFam) ? _current.FontFamily : fontFam;
        _current.FontSize     = fontSize;
        _current.Foreground   = fg;
        _current.Background   = bg;
        _current.Timestamp    = ts;
        _current.NotifyChanged();

        StatusText.Text = "Applied.";
    }

    private void OnReset(object? sender, RoutedEventArgs e)
    {
        if (_current is null) return;

        _current.DisplayTitle = _current.DefaultTitle;
        _current.FontFamily   = "Cascadia Mono,Consolas,Courier New,monospace";
        _current.FontSize     = 13;
        _current.Foreground   = "Default";
        _current.Background   = "";
        _current.Timestamp    = false;
        _current.NotifyChanged();

        LoadForm(_current);
        StatusText.Text = "Reset to defaults.";
    }
}
