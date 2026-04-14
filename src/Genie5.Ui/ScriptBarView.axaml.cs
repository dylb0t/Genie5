using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Genie4.Core.Scripting;

namespace Genie5.Ui;

/// <summary>
/// Toolbar displayed below the menu bar showing running scripts and mapper
/// walk status. Each entry has a label, a pause/resume toggle, and a stop
/// button — mirroring the Genie4 script bar.
/// </summary>
public partial class ScriptBarView : UserControl
{
    private ScriptEngine? _scripts;
    private MapperController? _mapper;
    private readonly DispatcherTimer _refreshTimer;

    public ScriptBarView()
    {
        InitializeComponent();
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _refreshTimer.Tick += (_, _) => Refresh();
    }

    public void Attach(ScriptEngine scripts, MapperController mapper)
    {
        _scripts = scripts;
        _mapper  = mapper;
        _refreshTimer.Start();
    }

    private void Refresh()
    {
        if (_scripts is null) return;

        var panel = ItemsPanel;
        panel.Children.Clear();

        bool hasItems = false;

        // Mapper walk entry
        if (_mapper?.IsWalking == true)
        {
            hasItems = true;
            panel.Children.Add(BuildEntry("mapper", isMapperWalk: true,
                isPaused: false, debugLevel: 0));
        }

        // Script entries
        foreach (var inst in _scripts.Instances)
        {
            if (!inst.Running) continue;
            hasItems = true;
            panel.Children.Add(BuildEntry(inst.Name, isMapperWalk: false,
                isPaused: inst.UserPaused, debugLevel: inst.DebugLevel));
        }

        IsVisible = hasItems;
    }

    private Border BuildEntry(string name, bool isMapperWalk, bool isPaused, int debugLevel)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3 };

        // Icon / label
        var label = new TextBlock
        {
            Text              = name,
            Foreground        = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center,
            Margin            = new Thickness(4, 0),
        };
        sp.Children.Add(label);

        // Debug level indicator (scripts only)
        if (!isMapperWalk && debugLevel > 0)
        {
            sp.Children.Add(new TextBlock
            {
                Text              = $"(Debug: {debugLevel})",
                Foreground        = Brushes.Gray,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize          = 10,
                Margin            = new Thickness(0, 0, 2, 0),
            });
        }

        if (isMapperWalk)
        {
            // Stop button for mapper walk
            var stopBtn = CreateButton("\u25A0", "Stop walk");
            stopBtn.Click += (_, _) => _mapper?.CancelWalk();
            sp.Children.Add(stopBtn);
        }
        else
        {
            // Pause / Resume toggle
            var pauseBtn = CreateButton(isPaused ? "\u25B6" : "\u23F8", isPaused ? "Resume" : "Pause");
            var scriptName = name; // capture
            pauseBtn.Click += (_, _) =>
            {
                if (_scripts is null) return;
                var inst = _scripts.Instances.FirstOrDefault(
                    i => i.Name.Equals(scriptName, StringComparison.OrdinalIgnoreCase) && i.Running);
                if (inst is null) return;
                if (inst.UserPaused) _scripts.ResumeScript(scriptName);
                else                 _scripts.PauseScript(scriptName);
                Refresh();
            };
            sp.Children.Add(pauseBtn);

            // Stop button
            var stopBtn = CreateButton("\u25A0", "Stop script");
            stopBtn.Click += (_, _) => { _scripts?.Stop(name); Refresh(); };
            sp.Children.Add(stopBtn);
        }

        return new Border
        {
            Background    = isPaused ? new SolidColorBrush(Color.Parse("#3A2A00")) : new SolidColorBrush(Color.Parse("#2A2A2A")),
            BorderBrush   = new SolidColorBrush(Color.Parse("#555555")),
            BorderThickness = new Thickness(1),
            CornerRadius  = new CornerRadius(3),
            Padding       = new Thickness(2, 1),
            Child         = sp,
        };
    }

    private static Button CreateButton(string text, string tooltip)
    {
        return new Button
        {
            Content   = text,
            FontSize  = 10,
            MinWidth  = 0,
            MinHeight = 0,
            Padding   = new Thickness(4, 1),
            VerticalAlignment = VerticalAlignment.Center,
            [ToolTip.TipProperty] = tooltip,
        };
    }
}
