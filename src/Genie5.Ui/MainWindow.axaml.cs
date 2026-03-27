using System;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace Genie5.Ui;

public partial class MainWindow : Window
{
    private readonly ScrollbackBuffer _scrollback = new();

    public MainWindow()
    {
        InitializeComponent();

        // Demo output so you can immediately see colors working
        AppendOutput("Normal text");
        AppendOutput("\x1b[31mRed text\x1b[0m back to normal");
        AppendOutput("\x1b[32mGreen\x1b[0m and \x1b[34mBlue\x1b[0m");
        AppendOutput("\x1b[1mBold text\x1b[0m");
    }

    private void AppendOutput(string text)
    {
        var parsed = AnsiParser.Parse(text);
        _scrollback.Add(parsed);

        var block = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap
        };

        foreach (var span in parsed.Spans)
        {
            var run = new Run(span.Text)
            {
                Foreground = ToBrush(span.Foreground),
                FontWeight = span.Bold ? FontWeight.Bold : FontWeight.Normal
            };

            block.Inlines!.Add(run);
        }

        OutputPanel.Children.Add(block);

        // Trim UI elements (prevents slowdown)
        while (OutputPanel.Children.Count > 2000)
        {
            OutputPanel.Children.RemoveAt(0);
        }

        OutputScrollViewer.ScrollToEnd();
    }

    private void OnConnect(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        AppendOutput("[connect clicked]");
    }

    private void OnSend(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var text = InputBox.Text ?? "";
        InputBox.Text = "";

        AppendOutput("> " + text);
    }

    private static IBrush ToBrush(string color)
    {
        return color switch
        {
            "Red" => Brushes.Red,
            "Green" => Brushes.Green,
            "Yellow" => Brushes.Yellow,
            "Blue" => Brushes.DodgerBlue,
            "Cyan" => Brushes.Cyan,
            "White" => Brushes.White,
            _ => Brushes.LightGray
        };
    }
}