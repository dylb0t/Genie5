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
        var block = new TextBlock { TextWrapping = TextWrapping.Wrap };

        foreach (var span in parsed.Spans)
        {
            block.Inlines!.Add(new Run(span.Text)
            {
                Foreground = ToBrush(span.Foreground),
                FontWeight = span.Bold ? FontWeight.Bold : FontWeight.Normal
            });
        }

        OutputPanel.Children.Add(block);

        // Defer scroll until layout is measured
        Dispatcher.UIThread.Post(() => OutputScrollViewer.ScrollToEnd(),
            DispatcherPriority.Loaded);
    }

    private static IBrush ToBrush(string color) => color switch
    {
        "Black"   => Brushes.DimGray,       // pure black invisible on dark bg
        "Red"     => Brushes.IndianRed,
        "Green"   => Brushes.LightGreen,
        "Yellow"  => Brushes.Khaki,
        "Blue"    => Brushes.CornflowerBlue,
        "Magenta" => Brushes.Orchid,
        "Cyan"    => Brushes.PaleTurquoise,
        "White"   => Brushes.WhiteSmoke,
        _         => Brushes.LightGray      // Default
    };
}
