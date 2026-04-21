using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using Genie4.Core.Highlights;
using Genie4.Core.Import;

namespace Genie5.Ui;

public partial class NamesPanel : UserControl
{
    private NameHighlightEngine? _engine;
    private Func<string>?         _configPath;
    private Action?               _onChanged;

    public NamesPanel()
    {
        InitializeComponent();
        FgColorPicker.Color   = Colors.Yellow;
        BgNoneCheck.IsChecked = true;
    }

    public void Initialize(NameHighlightEngine engine, Func<string> configPath, Action? onChanged = null)
    {
        _engine     = engine;
        _configPath = configPath;
        _onChanged  = onChanged;
        Refresh();
    }

    private void Refresh()
    {
        if (_engine is null) return;
        ItemsList.ItemsSource = _engine.Rules
            .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .Select(r =>
            {
                var bg = string.IsNullOrEmpty(r.BackgroundColor) ? "" : $"/{r.BackgroundColor}";
                return $"{r.Name}  [{r.ForegroundColor}{bg}]";
            })
            .ToList();
    }

    private NameRule? SelectedRule()
    {
        if (_engine is null) return null;
        var idx = ItemsList.SelectedIndex;
        if (idx < 0) return null;
        return _engine.Rules
            .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ElementAtOrDefault(idx);
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var rule = SelectedRule();
        if (rule is null) return;
        NameBox.Text = rule.Name;
        ColorPickerHelpers.LoadColor(FgColorPicker, FgDefaultCheck, rule.ForegroundColor, "Default");
        ColorPickerHelpers.LoadColor(BgColorPicker, BgNoneCheck,    rule.BackgroundColor, "");
        StatusText.Text = string.Empty;
    }

    private void OnRefresh(object? sender, RoutedEventArgs e)
    {
        Refresh();
        StatusText.Text = "Refreshed.";
    }

    private void OnAdd(object? sender, RoutedEventArgs e) => ClearForm();

    private void OnSaveRule(object? sender, RoutedEventArgs e)
    {
        if (_engine is null) return;
        var name = NameBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(name)) { StatusText.Text = "Name is required."; return; }

        var fg = ColorPickerHelpers.ReadColor(FgColorPicker, FgDefaultCheck, "Default");
        var bg = ColorPickerHelpers.ReadColor(BgColorPicker, BgNoneCheck,    "");

        _engine.Add(name, fg, bg);
        Refresh();
        _onChanged?.Invoke();
        StatusText.Text = $"Saved '{name}'.";
    }

    private void OnRemove(object? sender, RoutedEventArgs e)
    {
        if (_engine is null) return;
        var rule = SelectedRule();
        if (rule is null) { StatusText.Text = "Select a name to remove."; return; }
        _engine.Remove(rule.Name);
        ClearForm();
        Refresh();
        _onChanged?.Invoke();
        StatusText.Text = $"Removed '{rule.Name}'.";
    }

    private void OnClear(object? sender, RoutedEventArgs e) => ClearForm();

    private void ClearForm()
    {
        ItemsList.SelectedIndex  = -1;
        NameBox.Text             = string.Empty;
        FgColorPicker.Color      = Colors.Yellow;
        FgDefaultCheck.IsChecked = false;
        BgNoneCheck.IsChecked    = true;
        StatusText.Text          = string.Empty;
    }

    private void OnLoad(object? sender, RoutedEventArgs e)
    {
        if (_engine is null || _configPath is null) return;
        var path = _configPath();
        _onChanged?.Invoke();
        Refresh();
        StatusText.Text = File.Exists(path)
            ? $"Loaded {_engine.Rules.Count} name(s)."
            : "No saved names to load.";
    }

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        _onChanged?.Invoke();
        StatusText.Text = "Saved.";
    }

    private async void OnImport(object? sender, RoutedEventArgs e)
    {
        if (_engine is null) return;

        var parent = this.GetVisualRoot() as Window;
        if (parent is null) return;

        var dialog = new OpenFileDialog
        {
            Title         = "Import Names",
            AllowMultiple = false,
            Filters       = [new FileDialogFilter { Name = "Name files", Extensions = ["cfg", "txt"] }],
        };

        var paths = await dialog.ShowAsync(parent);
        if (paths is null || paths.Length == 0) return;

        var result = Genie4Importer.ImportNames(paths[0], _engine, ImportMode.Merge);
        Refresh();
        _onChanged?.Invoke();
        StatusText.Text = result.Skipped > 0
            ? $"Imported {result.Imported} name(s), skipped {result.Skipped}."
            : $"Imported {result.Imported} name(s).";
    }
}
