using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Genie4.Core.Import;

namespace Genie5.Ui;

public partial class DialogImportGenie4 : Window
{
    // Result returned to caller via ShowDialog<Result?>
    public sealed class Result
    {
        public required string Directory { get; init; }
        public required ImportMode Mode { get; init; }
        public required Genie4ImportTypes Types { get; init; }
        public required Dictionary<Genie4ImportTypes, string?> IndividualFiles { get; init; }
    }

    // One UI row per importable type. Ties checkbox, detected-file label, and
    // "browse…" override into one object so we can rebuild state easily.
    private sealed class TypeRow
    {
        public required Genie4ImportTypes Type { get; init; }
        public required string Label { get; init; }
        public required string DefaultFileName { get; init; }
        public required CheckBox EnableCheck { get; init; }
        public required TextBlock CountText { get; init; }
        public required TextBlock FileText { get; init; }
        public string? OverrideFile { get; set; }
    }

    private readonly List<TypeRow> _rows = new();

    public DialogImportGenie4(string? initialDirectory = null)
    {
        InitializeComponent();

        BuildRow(Genie4ImportTypes.Aliases,     "Aliases",     "aliases.cfg");
        BuildRow(Genie4ImportTypes.Triggers,    "Triggers",    "triggers.cfg");
        BuildRow(Genie4ImportTypes.Highlights,  "Highlights",  "highlights.cfg");
        BuildRow(Genie4ImportTypes.Substitutes, "Substitutes", "substitutes.cfg");
        BuildRow(Genie4ImportTypes.Gags,        "Gags",        "gags.cfg");
        BuildRow(Genie4ImportTypes.Macros,      "Macros",      "macros.cfg");
        BuildRow(Genie4ImportTypes.Names,       "Names",       "names.cfg");
        BuildRow(Genie4ImportTypes.Presets,     "Presets",     "presets.cfg");
        BuildRow(Genie4ImportTypes.Variables,   "Variables",   "variables.cfg");
        BuildRow(Genie4ImportTypes.Classes,     "Classes",     "classes.cfg");

        if (!string.IsNullOrWhiteSpace(initialDirectory))
        {
            FolderBox.Text = initialDirectory;
            RefreshProbe();
        }
    }

    private void BuildRow(Genie4ImportTypes type, string label, string defaultFile)
    {
        var check     = new CheckBox { IsChecked = true, VerticalAlignment = VerticalAlignment.Center };
        var labelTb   = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
        var countTb   = new TextBlock { Text = "—",   VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.Gray };
        var fileTb    = new TextBlock { Text = "—",   VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.Gray, TextTrimming = TextTrimming.CharacterEllipsis };
        var browseBtn = new Button    { Content = "File...", Padding = new Avalonia.Thickness(8, 2) };

        var row = new TypeRow
        {
            Type            = type,
            Label           = label,
            DefaultFileName = defaultFile,
            EnableCheck     = check,
            CountText       = countTb,
            FileText        = fileTb,
        };

        browseBtn.Click += async (_, _) =>
        {
            var dialog = new OpenFileDialog
            {
                Title         = $"Select {label} file",
                AllowMultiple = false,
                Filters       = [new FileDialogFilter { Name = $"{label} files", Extensions = ["cfg", "txt"] }],
            };
            var paths = await dialog.ShowAsync(this);
            if (paths is null || paths.Length == 0) return;
            row.OverrideFile     = paths[0];
            row.FileText.Text    = Path.GetFileName(paths[0]);
            row.EnableCheck.IsChecked = true;
            UpdateRowCount(row);
        };

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,140,80,*,Auto"),
            Margin            = new Avalonia.Thickness(0, 2),
        };
        Grid.SetColumn(check,     0); check.Margin   = new Avalonia.Thickness(0, 0, 12, 0); grid.Children.Add(check);
        Grid.SetColumn(labelTb,   1); grid.Children.Add(labelTb);
        Grid.SetColumn(countTb,   2); grid.Children.Add(countTb);
        Grid.SetColumn(fileTb,    3); grid.Children.Add(fileTb);
        Grid.SetColumn(browseBtn, 4); grid.Children.Add(browseBtn);

        _rows.Add(row);
        TypeRows.Children.Add(grid);
    }

    private void OnFolderChanged(object? sender, TextChangedEventArgs e) => RefreshProbe();

    private async void OnBrowseFolder(object? sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Select Genie4 Config directory" };
        var dir    = await dialog.ShowAsync(this);
        if (string.IsNullOrEmpty(dir)) return;
        FolderBox.Text = dir;
        RefreshProbe();
    }

    private void RefreshProbe()
    {
        var dir = FolderBox.Text?.Trim() ?? string.Empty;
        var counts = Directory.Exists(dir)
            ? Genie4Importer.ProbeDirectory(dir)
            : new Dictionary<Genie4ImportTypes, int>();

        foreach (var row in _rows)
        {
            if (row.OverrideFile is not null)
            {
                UpdateRowCount(row);
                continue;
            }

            if (counts.TryGetValue(row.Type, out var n))
            {
                row.CountText.Text = n.ToString();
                row.FileText.Text  = row.DefaultFileName;
                row.FileText.Foreground = Brushes.Gray;
            }
            else
            {
                row.CountText.Text = "—";
                row.FileText.Text  = Directory.Exists(dir) ? "(not found)" : "—";
                row.FileText.Foreground = Brushes.Gray;
            }
        }
    }

    private static void UpdateRowCount(TypeRow row)
    {
        if (row.OverrideFile is null) return;
        try
        {
            var directive = row.Type switch
            {
                Genie4ImportTypes.Aliases     => "#alias",
                Genie4ImportTypes.Triggers    => "#trigger",
                Genie4ImportTypes.Highlights  => "#highlight",
                Genie4ImportTypes.Substitutes => "#subs",
                Genie4ImportTypes.Gags        => "#gag",
                Genie4ImportTypes.Macros      => "#macro",
                Genie4ImportTypes.Names       => "#name",
                Genie4ImportTypes.Presets     => "#preset",
                Genie4ImportTypes.Variables   => "#var",
                Genie4ImportTypes.Classes     => "#class",
                _                             => "",
            };
            int n = 0;
            foreach (var raw in File.ReadAllLines(row.OverrideFile))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("//") || line.StartsWith("#!")) continue;
                if (line.StartsWith(directive, StringComparison.OrdinalIgnoreCase)) n++;
            }
            row.CountText.Text = n.ToString();
        }
        catch
        {
            row.CountText.Text = "?";
        }
    }

    private void OnImport(object? sender, RoutedEventArgs e)
    {
        var dir = FolderBox.Text?.Trim() ?? string.Empty;

        // Selected types and collected per-type file overrides
        var types = Genie4ImportTypes.None;
        var individual = new Dictionary<Genie4ImportTypes, string?>();
        foreach (var row in _rows)
        {
            if (row.EnableCheck.IsChecked != true) continue;
            types |= row.Type;
            individual[row.Type] = row.OverrideFile;
        }

        if (types == Genie4ImportTypes.None)
        {
            StatusText.Text = "Select at least one type to import.";
            return;
        }

        // Folder is only required if any selected row has no override
        var needsFolder = individual.Values.Any(v => v is null);
        if (needsFolder && !Directory.Exists(dir))
        {
            StatusText.Text = "Choose a valid Genie4 folder, or specify a file for every selected row.";
            return;
        }

        var mode = ModeReplace.IsChecked == true ? ImportMode.Replace
                 : ModeAddOnly.IsChecked == true ? ImportMode.AddOnly
                 :                                 ImportMode.Merge;

        Close(new Result
        {
            Directory       = dir,
            Mode            = mode,
            Types           = types,
            IndividualFiles = individual,
        });
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close((Result?)null);
}
