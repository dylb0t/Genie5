using System;
using Avalonia.Interactivity;
using Genie4.Core.Commanding;
using Genie4.Core.Config;
using Genie4.Core.Networking;
using Genie4.Core.Queue;
using Genie4.Core.Runtime;
using Genie4.Core.Triggers;
using Genie4.Core.Aliases;
using Genie4.Core.Variables;

namespace Genie5.Ui;

public partial class MainWindow
{
    private readonly TcpGameClient _client = new();
    private readonly CommandEngine _engine;
    private readonly TriggerEngineFinal _triggers;
    private readonly AliasEngine _aliases;
    private readonly VariableEngine _variables;

    private void InitializeEngine()
    {
        var baseDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Genie5UI");
        var config = new GenieConfig(new LocalDirectoryService("Genie5", baseDir));

        var queue = new CommandQueue();
        var events = new EventQueue();

        _engine = new CommandEngine(config, queue, events, new UiHost(this));
        _triggers = new TriggerEngineFinal(new UiHost(this), _engine);
        _aliases = new AliasEngine(_engine);
        _variables = new VariableEngine(_engine);

        _client.LineReceived += line =>
        {
            AppendOutput(line);
            _triggers.ProcessLine(line);
        };
    }

    private async void OnConnect(object? sender, RoutedEventArgs e)
    {
        InitializeEngine();

        var host = HostBox.Text ?? "aardmud.org";
        var port = int.TryParse(PortBox.Text, out var p) ? p : 4000;

        await _client.ConnectAsync(new GameConnectionOptions { Host = host, Port = port });
        AppendOutput("[connected]");
    }

    private void OnSend(object? sender, RoutedEventArgs e)
    {
        var input = InputBox.Text ?? string.Empty;
        InputBox.Text = string.Empty;

        if (_variables.TryProcess(input)) return;

        var expanded = _variables.Expand(input);

        if (!_aliases.TryProcess(expanded))
        {
            _engine.ProcessInput(expanded);
        }
    }

    private sealed class UiHost : ICommandHost
    {
        private readonly MainWindow _window;

        public UiHost(MainWindow window)
        {
            _window = window;
        }

        public void Echo(string text)
        {
            _window.AppendOutput("[echo] " + text);
        }

        public void SendToGame(string text, bool userInput = false, string origin = "")
        {
            _ = _window._client.SendAsync(text);
        }

        public void RunScript(string text)
        {
            _window.AppendOutput("[script] " + text);
        }
    }
}
