using System;
using Avalonia.Controls;

namespace Genie5.Ui;

public partial class MainWindow : Window
{
    private readonly ScrollbackBuffer _scrollback = new();
    private readonly GameOutputViewModel _gameOutputVm;

    public MainWindow()
    {
        InitializeComponent();

        _gameOutputVm = new GameOutputViewModel();

        var factory = new GenieDockFactory(_gameOutputVm);
        var layout  = factory.CreateLayout();
        factory.InitLayout(layout);

        MainDockControl.Factory = factory;
        MainDockControl.Layout  = layout;

        // Demo output
        AppendOutput("Normal text");
        AppendOutput("\x1b[31mRed text\x1b[0m back to normal");
        AppendOutput("\x1b[32mGreen\x1b[0m and \x1b[34mBlue\x1b[0m");
        AppendOutput("\x1b[1mBold text\x1b[0m");
    }

    internal void AppendOutput(string text)
    {
        var parsed = AnsiParser.Parse(text);
        _scrollback.Add(parsed);
        _gameOutputVm.AppendLine(parsed);
    }

    private async void OnConnect(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        InitializeEngine();

        var host = HostBox.Text ?? "aardmud.org";
        var port = int.TryParse(PortBox.Text, out var p) ? p : 4000;

        AppendOutput($"[connecting to {host}:{port}]");
        try
        {
            await _client.ConnectAsync(new Genie4.Core.Networking.GameConnectionOptions
            {
                Host = host,
                Port = port
            });
        }
        catch (Exception ex)
        {
            AppendOutput($"[connection failed: {ex.Message}]");
        }
    }

    private void OnSend(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_engine is null) return;

        var input = InputBox.Text ?? string.Empty;
        InputBox.Text = string.Empty;

        if (_variables?.TryProcess(input) == true)
            return;

        var expanded = _variables?.Expand(input) ?? input;

        if (_aliases?.TryProcess(expanded) != true)
            _engine.ProcessInput(expanded);
    }
}
