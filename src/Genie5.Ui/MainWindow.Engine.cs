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
    private CommandEngine? _engine;
    private TriggerEngineFinal? _triggers;
    private AliasEngine? _aliases;
    private VariableEngine? _variables;

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
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                AppendOutput(line);
                _triggers?.ProcessLine(line);
            });
        };

        _client.Connected += () =>
            Avalonia.Threading.Dispatcher.UIThread.Post(() => AppendOutput("[connected]"));

        _client.Disconnected += () =>
            Avalonia.Threading.Dispatcher.UIThread.Post(() => AppendOutput("[disconnected]"));
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
            Avalonia.Threading.Dispatcher.UIThread.Post(() => _window.AppendOutput("[echo] " + text));
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
