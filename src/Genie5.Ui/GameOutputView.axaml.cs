using System;
using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Threading;
using Genie4.Core.Layout;

namespace Genie5.Ui;

public partial class GameOutputView : UserControl
{
    private GameOutputViewModel? _vm;
    private WindowSettings?      _settings;

    public GameOutputView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;

        SizeChanged += (_, e) =>
        {
            OutputScrollViewer.Width  = e.NewSize.Width;
            OutputScrollViewer.Height = e.NewSize.Height;
        };

        OutputScrollViewer.LayoutUpdated += (_, _) =>
        {
            var sv = OutputScrollViewer;
            // Console.Error.WriteLine(
            //     $"[ScrollDiag:{_vm?.Id ?? "?"}] " +
            //     $"Root={Bounds.Width:F0}x{Bounds.Height:F0} " +
            //     $"SV={sv.Bounds.Width:F0}x{sv.Bounds.Height:F0} " +
            //     $"VP={sv.Viewport.Width:F0}x{sv.Viewport.Height:F0} " +
            //     $"EXT={sv.Extent.Width:F0}x{sv.Extent.Height:F0} " +
            //     $"Lines={OutputPanel.Children.Count}");
        };
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm is not null)
        {
            _vm.Lines.CollectionChanged -= OnLinesChanged;
            if (_settings is not null) _settings.Changed -= OnSettingsChanged;
        }

        _vm       = DataContext as GameOutputViewModel;
        _settings = _vm?.Settings;

        ApplySettings();
        OutputPanel.Children.Clear();

        if (_vm is not null)
        {
            foreach (var line in _vm.Lines)
                RenderLine(line);
            _vm.Lines.CollectionChanged += OnLinesChanged;
            if (_settings is not null) _settings.Changed += OnSettingsChanged;
        }
    }

    private void OnSettingsChanged()
    {
        _settings = _vm?.Settings;
        ApplySettings();
        // Re-render all existing lines so timestamp changes take effect.
        OutputPanel.Children.Clear();
        if (_vm is not null)
            foreach (var line in _vm.Lines)
                RenderLine(line);
    }

    private void ApplySettings()
    {
        var ff  = _settings?.FontFamily ?? "Cascadia Mono,Consolas,Courier New,monospace";
        var fs  = _settings?.FontSize   > 0 ? _settings.FontSize : 13;
        var fg  = _settings?.Foreground;
        var bg  = _settings?.Background;

        FontFamily = new FontFamily(ff);
        FontSize   = fs;

        OutputScrollViewer.Background = string.IsNullOrEmpty(bg) ? null : ToBrush(bg);

        // Store the configured default foreground so ToBrush("Default") can use it.
        _defaultForeground = (fg is null || fg == "Default") ? "LightGray" : fg;
    }

    private string _defaultForeground = "LightGray";

    private void OnLinesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems is not null)
        {
            foreach (RenderLine line in e.NewItems)
                RenderLine(line);
        }
        else if (e.Action == NotifyCollectionChangedAction.Remove)
        {
            while (OutputPanel.Children.Count > (_vm?.Lines.Count ?? 0))
                OutputPanel.Children.RemoveAt(0);
        }
    }

    private void RenderLine(RenderLine parsed)
    {
        var block = new SelectableTextBlock { TextWrapping = TextWrapping.Wrap };

        if (_settings?.Timestamp == true)
        {
            block.Inlines!.Add(new Run(DateTime.Now.ToString("[HH:mm:ss] "))
            {
                Foreground = Brushes.Gray,
                FontWeight = FontWeight.Normal,
            });
        }

        foreach (var span in parsed.Spans)
        {
            var run = new Run(span.Text)
            {
                Foreground = ToBrush(span.Foreground),
                FontWeight = span.Bold ? FontWeight.Bold : FontWeight.Normal,
            };
            if (!string.IsNullOrEmpty(span.Background))
                run.Background = ToBrush(span.Background);
            block.Inlines!.Add(run);
        }

        OutputPanel.Children.Add(block);

        // Defer scroll until layout is measured
        Dispatcher.UIThread.Post(() => OutputScrollViewer.ScrollToEnd(),
            DispatcherPriority.Loaded);
    }

    private static readonly Dictionary<string, IBrush> BrushCache = new(StringComparer.OrdinalIgnoreCase);

    private IBrush ToBrush(string color)
    {
        if (string.IsNullOrEmpty(color) || color == "Default")
            return ParseBrush(_defaultForeground);

        return ParseBrush(color);
    }

    private static IBrush ParseBrush(string color)
    {
        // ANSI names mapped to readable equivalents on a dark background
        switch (color)
        {
            case "Black":   return Brushes.DimGray;
            case "Red":     return Brushes.IndianRed;
            case "Green":   return Brushes.LightGreen;
            case "Yellow":  return Brushes.Khaki;
            case "Blue":    return Brushes.CornflowerBlue;
            case "Magenta": return Brushes.Orchid;
            case "Cyan":    return Brushes.PaleTurquoise;
            case "White":   return Brushes.WhiteSmoke;
        }

        if (BrushCache.TryGetValue(color, out var cached))
            return cached;

        try
        {
            var brush = (IBrush)Brush.Parse(color);
            BrushCache[color] = brush;
            return brush;
        }
        catch
        {
            return Brushes.LightGray;
        }
    }
}
