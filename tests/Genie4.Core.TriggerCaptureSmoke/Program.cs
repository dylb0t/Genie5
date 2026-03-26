using Genie4.Core.Commanding;
using Genie4.Core.Config;
using Genie4.Core.Queue;
using Genie4.Core.Runtime;
using Genie4.Core.Triggers;

var baseDir = Path.Combine(Path.GetTempPath(), "Genie5TriggerTest");
Directory.CreateDirectory(baseDir);

var local = new LocalDirectoryService("Genie5", baseDir);
var config = new GenieConfig(local);
var commandQueue = new CommandQueue();
var eventQueue = new EventQueue();

var host = new ConsoleHost();
var engine = new CommandEngine(config, commandQueue, eventQueue, host);
var triggers = new TriggerEngineWithCaptures(host, engine);

triggers.AddTrigger("You hit (.*) for (\\d+) damage", "#echo Target: $1 Damage: $2");

Console.WriteLine("Type a line to simulate server input:\n");

while (true)
{
    var line = Console.ReadLine();
    if (string.IsNullOrWhiteSpace(line)) continue;

    triggers.ProcessLine(line);
    engine.Tick();
}

public class ConsoleHost : ICommandHost
{
    public void Echo(string text) => Console.WriteLine(text);
    public void SendToGame(string text, bool userInput = false, string origin = "") => Console.WriteLine($"SEND: {text}");
    public void RunScript(string text) => Console.WriteLine($"SCRIPT: {text}");
}
