using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Genie5.Ui;

public partial class MainWindow : Window
{
    private readonly ScrollbackBuffer _scrollback = new();
    private readonly GameOutputViewModel _gameOutputVm;

    private readonly List<string> _history = new();
    private int _historyIndex = -1;

    private string _lastHost = "aardmud.org";
    private int    _lastPort = 4000;

    public MainWindow()
    {
        InitializeComponent();

        _gameOutputVm = new GameOutputViewModel();

        var factory = new GenieDockFactory(_gameOutputVm);
        var layout  = factory.CreateLayout();
        factory.InitLayout(layout);

        MainDockControl.Factory = factory;
        MainDockControl.Layout  = layout;

        InitializeEngines();

        InputBox.KeyDown += OnInputKeyDown;

        Closing += (_, _) => SaveData();

        // Demo output
        AppendOutput("Normal text");
        AppendOutput("\x1b[31mRed text\x1b[0m back to normal");
        AppendOutput("\x1b[32mGreen\x1b[0m and \x1b[34mBlue\x1b[0m");
        AppendOutput("\x1b[1mBold text\x1b[0m");
    }

    internal void AppendOutput(string text)
    {
        var parsed = AnsiParser.Parse(text);

        var rule = _highlights.Match(parsed.PlainText);
        if (rule != null)
        {
            var highlighted = new RenderLine();
            foreach (var span in parsed.Spans)
                highlighted.Spans.Add(new AnsiSpan { Text = span.Text, Foreground = rule.ForegroundColor, Bold = span.Bold });
            parsed = highlighted;
        }

        _scrollback.Add(parsed);
        _gameOutputVm.AppendLine(parsed);
    }

    private void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Return:
                SendInput();
                e.Handled = true;
                break;

            case Key.Up:
                if (_history.Count == 0) break;
                if (_historyIndex < 0) _historyIndex = _history.Count - 1;
                else if (_historyIndex > 0) _historyIndex--;
                InputBox.Text = _history[_historyIndex];
                InputBox.CaretIndex = InputBox.Text.Length;
                e.Handled = true;
                break;

            case Key.Down:
                if (_historyIndex < 0) break;
                _historyIndex++;
                if (_historyIndex >= _history.Count)
                {
                    _historyIndex = -1;
                    InputBox.Text = string.Empty;
                }
                else
                {
                    InputBox.Text = _history[_historyIndex];
                    InputBox.CaretIndex = InputBox.Text.Length;
                }
                e.Handled = true;
                break;
        }
    }

    private void SendInput()
    {
        var input = InputBox.Text ?? string.Empty;
        InputBox.Text = string.Empty;
        _historyIndex = -1;

        if (string.IsNullOrWhiteSpace(input)) return;

        if (_history.Count == 0 || _history[^1] != input)
            _history.Add(input);

        if (_variables.TryProcess(input)) return;

        var expanded = _variables.Expand(input);

        if (!_aliases.TryProcess(expanded))
            _engine.ProcessInput(expanded);
    }

    private async void OnMenuConnect(object? sender, RoutedEventArgs e)
    {
        var dialog = new DialogConnect(_lastHost, _lastPort);
        var result = await dialog.ShowDialog<bool>(this);
        if (!result || dialog.ResultHost is null) return;

        _lastHost = dialog.ResultHost;
        _lastPort = dialog.ResultPort;

        AppendOutput($"[connecting to {_lastHost}:{_lastPort}]");
        try
        {
            await _client.ConnectAsync(new Genie4.Core.Networking.GameConnectionOptions
            {
                Host = _lastHost,
                Port = _lastPort
            });
        }
        catch (Exception ex)
        {
            AppendOutput($"[connection failed: {ex.Message}]");
        }
    }

    private void OnMenuDisconnect(object? sender, RoutedEventArgs e)
    {
        _client.Disconnect();
    }

    private void OnSend(object? sender, RoutedEventArgs e) => SendInput();

    private void OnSettings(object? sender, RoutedEventArgs e)
    {
        var config = new FormConfig(_aliases, _triggers, _highlights);
        config.Show();
    }

    private void OnMenuExit(object? sender, RoutedEventArgs e) => Close();

    private void OnMenuGitHub(object? sender, RoutedEventArgs e)
    {
        var url = "https://github.com/dylb0t/Genie5";
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url)
        {
            UseShellExecute = true
        });
    }
}
