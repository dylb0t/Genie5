using System;
using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Threading;

namespace Genie5.Ui;

public partial class GameOutputView : UserControl
{
    private GameOutputViewModel? _vm;

    public GameOutputView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm is not null)
            _vm.Lines.CollectionChanged -= OnLinesChanged;

        _vm = DataContext as GameOutputViewModel;

        if (_vm is not null)
            _vm.Lines.CollectionChanged += OnLinesChanged;
    }

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

    private static IBrush ToBrush(string color)
    {
        if (string.IsNullOrEmpty(color) || color == "Default")
            return Brushes.LightGray;

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
