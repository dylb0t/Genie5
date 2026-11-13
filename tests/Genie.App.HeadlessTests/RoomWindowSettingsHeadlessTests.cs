using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Genie.App.Docking;
using Genie.App.Highlighting;
using Genie.App.ViewModels;
using Genie.Core.Layout;
using Genie.Core.Presets;
using Xunit;

namespace Genie.App.HeadlessTests;

/// <summary>
/// #424: the Room window is drawn with the window's own font, size and colours,
/// as Genie 4's Room window was, over the PRODUCTION RoomTool DataTemplate in
/// App.axaml. It used to hardcode the UI font, size 11 and per-field theme
/// colours, and only the title took a preset.
/// </summary>
public class RoomWindowSettingsHeadlessTests : IDisposable
{
    private static Genie.App.App? _app;
    private readonly PresetEngine? _savedPresets = DefaultHighlights.PresetEngine;

    public void Dispose() => DefaultHighlights.PresetEngine = _savedPresets;

    private static void EnsureProductionTemplates()
    {
        _app ??= Build();
        static Genie.App.App Build() { var a = new Genie.App.App(); a.Initialize(); return a; }

        var cur = Application.Current!;
        if (!cur.DataTemplates.OfType<Avalonia.Markup.Xaml.Templates.DataTemplate>().Any(t => t.DataType == typeof(RoomTool)))
            foreach (var t in _app.DataTemplates) cur.DataTemplates.Add(t);
        if (!cur.Resources.ContainsKey("Theme.PanelBg"))
            cur.Resources.MergedDictionaries.Add(new ResourceInclude(new Uri("avares://Genie.App"))
            {
                Source = new Uri("avares://Genie5/Themes/ThemePalette.axaml")
            });
    }

    private static RoomViewModel Room()
    {
        var vm = new RoomViewModel();
        vm.Apply("room title",   "Himineldar Shel, Mountain Road", null);
        vm.Apply("room desc",    "Incessant storms rage throughout this region.", null);
        vm.Apply("room objs",    "You also see a snowdrop.", null);
        vm.Apply("room players", "Also here: Malbog.", null);
        vm.Apply("room exits",   "Obvious paths: east, northwest.", null);
        return vm;
    }

    private static (Window window, SelectableTextBlock[] fields, Grid root) Show(RoomTool tool)
    {
        var window = new Window { Width = 500, Height = 400, Content = new ContentControl { Content = tool } };
        window.Show();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        var fields = window.GetVisualDescendants().OfType<SelectableTextBlock>().ToArray();
        Assert.Equal(5, fields.Length);
        var root = fields[0].GetVisualAncestors().OfType<Grid>().Last(g => g.DataContext == tool);
        return (window, fields, root);
    }

    private static Color? RunColour(SelectableTextBlock tb, string contains)
        => tb.Inlines?.OfType<Run>().FirstOrDefault(r => r.Text?.Contains(contains) == true)?.Foreground is ISolidColorBrush b
            ? b.Color : null;

    [AvaloniaFact]
    public void Every_field_takes_the_window_font_size_and_text_colour()
    {
        EnsureProductionTemplates();
        DefaultHighlights.PresetEngine = new PresetEngine();
        var settings = new WindowSettings
        {
            FontFamily = "DejaVu Sans Mono",
            FontSize   = 17,
            Foreground = "#123456",
            Background = "#0A0B0C",
        };
        var tool = new RoomTool(Room(), settings);
        var (window, fields, root) = Show(tool);
        try
        {
            foreach (var f in fields)
            {
                Assert.Equal("DejaVu Sans Mono", f.FontFamily.Name);
                Assert.Equal(17, f.FontSize);
                Assert.Equal(Color.Parse("#123456"), ((ISolidColorBrush)f.Foreground!).Color);
            }
            Assert.Equal(Color.Parse("#0A0B0C"), ((ISolidColorBrush)root.Background!).Color);

            // A settings change (Layout tab, or a global font change rippling
            // through NotifyChanged) repaints live.
            settings.FontSize = 9;
            settings.NotifyChanged();
            window.UpdateLayout();
            Assert.All(fields, f => Assert.Equal(9, f.FontSize));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Title_and_description_take_the_roomname_and_roomdesc_presets()
    {
        EnsureProductionTemplates();
        var presets = new PresetEngine();
        presets.Apply(new PresetRule { Id = "roomname", ForegroundColor = "#FF8000" });
        presets.Apply(new PresetRule { Id = "roomdesc", ForegroundColor = "#00C0C0" });
        DefaultHighlights.PresetEngine = presets;

        var tool = new RoomTool(Room(), new WindowSettings { Foreground = "#123456" });
        var (window, fields, _) = Show(tool);
        try
        {
            Assert.Equal(Color.Parse("#FF8000"), RunColour(fields[0], "Himineldar"));
            Assert.Equal(Color.Parse("#00C0C0"), RunColour(fields[1], "Incessant"));

            // Exits, players and objects have no preset (as in Genie 4), so
            // they draw in the window's text colour.
            foreach (var (f, text) in new[] { (fields[2], "Obvious"), (fields[3], "Also here"), (fields[4], "snowdrop") })
                Assert.Equal(Color.Parse("#123456"), RunColour(f, text));
        }
        finally { window.Close(); }
    }
}
