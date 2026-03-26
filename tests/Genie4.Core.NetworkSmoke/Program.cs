using Genie4.Core.Commanding;
using Genie4.Core.Config;
using Genie4.Core.Networking;
using Genie4.Core.Queue;
using Genie4.Core.Runtime;

var baseDir = Path.Combine(Path.GetTempPath(), "Genie5NetTest");
Directory.CreateDirectory(baseDir);

var local = new LocalDirectoryService("Genie5", baseDir);
var config = new GenieConfig(local);
var commandQueue = new CommandQueue();
var eventQueue = new EventQueue();

var client = new TcpGameClient();

var host = new NetworkCommandHost(client);
var engine = new CommandEngine(config, commandQueue, eventQueue, host);

client.LineReceived += line => Console.WriteLine(line);
client.Connected += () => Console.WriteLine("[connected]");
client.Disconnected += () => Console.WriteLine("[disconnected]");

Console.Write("Host: ");
var hostName = Console.ReadLine() ?? "localhost";

Console.Write("Port: ");
var port = int.TryParse(Console.ReadLine(), out var p) ? p : 23;

await client.ConnectAsync(new GameConnectionOptions { Host = hostName, Port = port });

Console.WriteLine("Connected. Type commands. Ctrl+C to exit.\n");

while (true)
{
    if (Console.KeyAvailable)
    {
        var input = Console.ReadLine();
        if (!string.IsNullOrWhiteSpace(input))
        {
            engine.ProcessInput(input);
        }
    }

    engine.Tick();
    await Task.Delay(50);
}

public sealed class NetworkCommandHost : ICommandHost
{
    private readonly TcpGameClient _client;

    public NetworkCommandHost(TcpGameClient client)
    {
        _client = client;
    }

    public void Echo(string text)
    {
        Console.WriteLine($"[echo] {text}");
    }

    public void SendToGame(string text, bool userInput = false, string origin = "")
    {
        _ = _client.SendAsync(text);
    }

    public void RunScript(string text)
    {
        Console.WriteLine($"[script] {text}");
    }
}
