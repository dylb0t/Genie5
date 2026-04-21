using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Genie4.Core.Classes;
using Genie4.Core.Import;

namespace Genie5.Ui;

public partial class ClassesPanel : UserControl
{
    public sealed record ClassRow(string EnabledGlyph, string Name, bool IsActive);

    private ClassEngine? _engine;
    private Action?      _onChanged;

    public ClassesPanel() => InitializeComponent();

    public void Initialize(ClassEngine engine, Action? onChanged = null)
    {
        _engine    = engine;
        _onChanged = onChanged;
        Refresh();
    }

    private void Refresh()
    {
        if (_engine is null) return;
        var keep = (ItemsList.SelectedItem as ClassRow)?.Name;
        ItemsList.ItemsSource = _engine.GetAll()
            .Select(kv => new ClassRow(kv.Value ? "✓" : "✗", kv.Key, kv.Value))
            .ToList();
        if (keep is not null)
            ItemsList.SelectedItem = ((IEnumerable<ClassRow>)ItemsList.ItemsSource)
                .FirstOrDefault(r => r.Name == keep);
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_engine is null || ItemsList.SelectedItem is not ClassRow row) return;
        NameBox.Text          = row.Name;
        ActiveCheck.IsChecked = row.IsActive;
        StatusText.Text       = string.Empty;
    }

    private void OnRefresh(object? sender, RoutedEventArgs e) => Refresh();

    private void OnActivateAll(object? sender, RoutedEventArgs e)
    {
        if (_engine is null) return;
        _engine.ActivateAll();
        Refresh();
        _onChanged?.Invoke();
        StatusText.Text = "Activated all classes.";
    }

    private void OnDeactivateAll(object? sender, RoutedEventArgs e)
    {
        if (_engine is null) return;
        _engine.DeactivateAll();
        Refresh();
        _onChanged?.Invoke();
        StatusText.Text = "Deactivated all classes.";
    }

    private void OnRemove(object? sender, RoutedEventArgs e)
    {
        if (_engine is null) return;
        var name = NameBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(name))
        {
            if (ItemsList.SelectedItem is not ClassRow row) { StatusText.Text = "Select a class to remove."; return; }
            name = row.Name;
        }
        if (name.Equals("default", StringComparison.OrdinalIgnoreCase))
        {
            StatusText.Text = "Cannot remove the default class.";
            return;
        }
        if (_engine.Remove(name))
        {
            ClearForm();
            Refresh();
            _onChanged?.Invoke();
            StatusText.Text = $"Removed '{name}'.";
        }
        else
        {
            StatusText.Text = $"'{name}' not found.";
        }
    }

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        if (_engine is null) return;
        var name = NameBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(name)) { StatusText.Text = "Class name is required."; return; }
        if (name.Equals("default", StringComparison.OrdinalIgnoreCase))
        {
            StatusText.Text = "The default class is always active.";
            return;
        }
        _engine.Set(name, ActiveCheck.IsChecked == true);
        Refresh();
        _onChanged?.Invoke();
        StatusText.Text = "Saved.";
    }

    private void OnClear(object? sender, RoutedEventArgs e) => ClearForm();

    private async void OnImport(object? sender, RoutedEventArgs e)
    {
        if (_engine is null) return;

        var parent = this.GetVisualRoot() as Window;
        if (parent is null) return;

        var dialog = new OpenFileDialog
        {
            Title         = "Import Classes",
            AllowMultiple = false,
            Filters       = [new FileDialogFilter { Name = "Class files", Extensions = ["cfg", "txt"] }],
        };

        var paths = await dialog.ShowAsync(parent);
        if (paths is null || paths.Length == 0) return;

        var result = Genie4Importer.ImportClasses(paths[0], _engine, ImportMode.Merge);
        Refresh();
        _onChanged?.Invoke();
        StatusText.Text = result.Skipped > 0
            ? $"Imported {result.Imported} class(es), skipped {result.Skipped}."
            : $"Imported {result.Imported} class(es).";
    }

    private void ClearForm()
    {
        ItemsList.SelectedItem = null;
        NameBox.Text           = string.Empty;
        ActiveCheck.IsChecked  = true;
        StatusText.Text        = string.Empty;
    }
}
