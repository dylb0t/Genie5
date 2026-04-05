using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Genie4.Core.Gsl;
using Genie4.Core.Profiles;
using Genie4.Core.Sge;

namespace Genie5.Ui;

public partial class MainWindow : Window
{
    private readonly ScrollbackBuffer _scrollback = new();
    private readonly GameOutputViewModel _gameOutputVm;
    private readonly GameOutputViewModel _rawOutputVm;
    private readonly RoomViewModel _roomVm = new();

    // Sub-stream panels keyed by GSL stream id
    private readonly Dictionary<string, GameOutputViewModel> _streamVms = new();

    private readonly List<string> _history = new();
    private int _historyIndex = -1;

    private string  _lastHost     = string.Empty;
    private int     _lastPort     = 4000;
    private string? _lastGameCode = "DR";

    public MainWindow()
    {
        InitializeComponent();

        _gameOutputVm = new GameOutputViewModel();
        _rawOutputVm  = new GameOutputViewModel("RawOutput", "Raw");

        // Create a dockable panel for each sub-stream
        var streams = new (string id, string title)[]
        {
            ("thoughts",   "Thoughts"),
            ("logons",     "Arrivals"),
            ("death",      "Deaths"),
            ("combat",     "Combat"),
            ("inv",        "Inventory"),
            ("familiar",   "Familiar"),
            ("percWindow", "Perception"),
        };
        var streamVmArray = new GameOutputViewModel[streams.Length];
        for (int i = 0; i < streams.Length; i++)
        {
            var vm = new GameOutputViewModel(streams[i].id, streams[i].title);
            _streamVms[streams[i].id] = vm;
            streamVmArray[i] = vm;
        }

        var factory = new GenieDockFactory(_gameOutputVm, _rawOutputVm, streamVmArray, _roomVm);
        var layout  = factory.CreateLayout();
        factory.InitLayout(layout);

        MainDockControl.Factory = factory;
        MainDockControl.Layout  = layout;

        InitializeEngines();

        StatusBar.Attach(_gslGameState);
        _roomVm.Attach(_gslGameState);

        InputBox.KeyDown += OnInputKeyDown;

        Closing += (_, _) => SaveData();

        // Demo output
        AppendOutput("Normal text");
        AppendOutput("\x1b[31mRed text\x1b[0m back to normal");
        AppendOutput("\x1b[32mGreen\x1b[0m and \x1b[34mBlue\x1b[0m");
        AppendOutput("\x1b[1mBold text\x1b[0m");
    }

    // Called for plain-text lines (demo output, echo, connection messages)
    internal void AppendOutput(string text)
    {
        var parsed = AnsiParser.Parse(text);
        ApplyHighlightAndAppend(parsed);
    }

    // Called for GSL-parsed lines with styled segments
    internal void AppendSegments(IReadOnlyList<GslSegment> segments)
    {
        var line = new RenderLine();
        foreach (var seg in segments)
        {
            var parsed = AnsiParser.Parse(seg.Text, seg.GslBold, seg.GslColor);
            foreach (var span in parsed.Spans)
                line.Spans.Add(span);
        }
        ApplyHighlightAndAppend(line);
    }

    private void ApplyHighlightAndAppend(RenderLine line)
    {
        var rule = _highlights.Match(line.PlainText);
        if (rule != null)
        {
            var highlighted = new RenderLine();
            foreach (var span in line.Spans)
                highlighted.Spans.Add(new AnsiSpan
                    { Text = span.Text, Foreground = rule.ForegroundColor, Bold = span.Bold });
            line = highlighted;
        }

        _scrollback.Add(line);
        _gameOutputVm.AppendLine(line);
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

    private async void OnMenuConnectProfile(object? sender, RoutedEventArgs e)
    {
        var dialog = new DialogProfileConnect(_profiles, ProfilesPath);
        var ok = await dialog.ShowDialog<bool>(this);
        if (!ok || dialog.SelectedProfile is null) return;

        var p        = dialog.SelectedProfile;
        var password = _profiles.GetPassword(p);
        await ConnectProfileAsync(p, password);
    }

    private async void OnMenuConnect(object? sender, RoutedEventArgs e)
    {
        var dialog = new DialogConnect(_lastGameCode, _lastHost, _lastPort, _profiles, ProfilesPath);
        var ok = await dialog.ShowDialog<bool>(this);
        if (!ok) return;

        _lastGameCode = dialog.IsSimutronics ? dialog.ResultGameCode : null;
        _lastHost     = dialog.ResultHost;
        _lastPort     = dialog.ResultPort;

        if (dialog.IsSimutronics)
            await ConnectSgeAsync(dialog.ResultGameCode, dialog.ResultCharacter,
                                  dialog.ResultAccount, dialog.ResultPassword);
        else
            await ConnectDirectAsync(dialog.ResultHost, dialog.ResultPort);
    }

    private void OnMenuDisconnect(object? sender, RoutedEventArgs e) => _client.Disconnect();

    // Connect using a saved profile
    private async Task ConnectProfileAsync(ConnectionProfile p, string password)
    {
        if (p.IsSimutronics)
            await ConnectSgeAsync(p.GameCode, p.CharacterName, p.AccountName, password);
        else
            await ConnectDirectAsync(p.Host, p.Port);
    }

    // Simutronics SGE auth → game server
    private async Task ConnectSgeAsync(string gameCode, string character,
                                        string account, string password)
    {
        AppendOutput($"[authenticating as {account} for {gameCode}...]");

        SgeLoginResult result;
        try
        {
            result = await new SgeClient().AuthenticateAsync(account, password, gameCode, character);
        }
        catch (Exception ex)
        {
            AppendOutput($"[authentication error: {ex.Message}]");
            return;
        }

        if (!result.Success)
        {
            AppendOutput($"[authentication failed: {result.Error}]");
            return;
        }

        // No character specified — show picker
        if (result.Characters.Count > 0 && string.IsNullOrEmpty(result.Key))
        {
            var picked = await PickCharacterAsync(result.Characters);
            if (picked is null) return;
            await ConnectSgeAsync(gameCode, picked, account, password);
            return;
        }

        AppendOutput($"[connecting to {result.GameHost}:{result.GamePort}]");
        try
        {
            await _client.ConnectWithKeyAsync(result.GameHost, result.GamePort, result.Key);
        }
        catch (Exception ex)
        {
            AppendOutput($"[connection failed: {ex.Message}]");
        }
    }

    // Direct TCP (non-Simutronics MUD)
    private async Task ConnectDirectAsync(string host, int port)
    {
        AppendOutput($"[connecting to {host}:{port}]");
        try
        {
            await _client.ConnectAsync(new Genie4.Core.Networking.GameConnectionOptions
                { Host = host, Port = port });
        }
        catch (Exception ex)
        {
            AppendOutput($"[connection failed: {ex.Message}]");
        }
    }

    // Simple character picker dialog (inline window)
    private async Task<string?> PickCharacterAsync(IReadOnlyList<string> characters)
    {
        var win = new Window
        {
            Title = "Select Character",
            Width = 280, Height = 260,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        var list   = new ListBox { ItemsSource = characters, Margin = new(8) };
        var btn    = new Avalonia.Controls.Button
            { Content = "Connect", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
              Margin = new(8) };
        var panel  = new Avalonia.Controls.StackPanel();
        panel.Children.Add(list);
        panel.Children.Add(btn);
        win.Content = panel;

        string? picked = null;
        btn.Click += (_, _) =>
        {
            picked = list.SelectedItem as string;
            win.Close(picked is not null);
        };

        await win.ShowDialog<bool>(this);
        return picked;
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
