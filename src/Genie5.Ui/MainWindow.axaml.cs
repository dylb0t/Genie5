using Avalonia.Controls;
using Genie4.Core.Commanding;
using Genie4.Core.Config;
using Genie4.Core.Networking;
using Genie4.Core.Queue;
using Genie4.Core.Runtime;
using Genie4.Core.Triggers;
using Genie4.Core.Aliases;
using Genie4.Core.Variables;

namespace Genie5.Ui;

public partial class MainWindow : Window, ICommandHost
{
    private readonly TcpGameClient _client = new();
    private readonly CommandEngine _engine;
    private readonly TriggerEngineFinal _triggers;
    private readonly AliasEngine _aliases;
    private readonly VariableEngine _variables;

    public MainWindow()
    {
        InitializeComponent();

        var baseDir = Path.Combine(Path.GetTempPath(), "Genie5UI");
        var config = new GenieConfig(new LocalDirectoryService("Genie5", baseDir));

        var queue = new CommandQueue();
        var events = new EventQueue();

        _engine = new CommandEngine(config, queue, events, this);
        _triggers = new TriggerEngineFinal(this, _engine);
        _aliases = new AliasEngine(_engine);
        _variables = new VariableEngine(_engine);

        _client.LineReceived += line =>
        {
            AppendOutput(line);
            _triggers.ProcessLine(line);
        };
    }

    private async void OnConnect(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var host = HostBox.Text ?? "localhost";
        var port = int.TryParse(PortBox.Text, out var p) ? p : 23;

        await _client.ConnectAsync(new GameConnectionOptions { Host = host, Port = port });
        AppendOutput("[connected]");
    }

    private void OnSend(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
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

    public void Echo(string text) => AppendOutput("[echo] " + text);

    public void SendToGame(string text, bool userInput = false, string origin = "")
        => _ = _client.SendAsync(text);

    public void RunScript(string text) => AppendOutput("[script] " + text);

    private void AppendOutput(string text)
    {
        OutputBox.Text += text + "\n";
    }
}
