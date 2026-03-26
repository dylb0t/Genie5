using Genie4.Core.Config;
using Genie4.Core.Queue;
using Genie4.Core.Parsing;

namespace Genie4.Core.Commanding;

public sealed class CommandEngine
{
    private readonly GenieConfig _config;
    private readonly CommandQueue _commandQueue;
    private readonly EventQueue _eventQueue;
    private readonly ICommandHost _host;

    public CommandEngine(GenieConfig config, CommandQueue commandQueue, EventQueue eventQueue, ICommandHost host)
    {
        _config = config;
        _commandQueue = commandQueue;
        _eventQueue = eventQueue;
        _host = host;
    }

    public void ProcessInput(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return;

        // Split by separator (basic for now)
        var commands = input.Split(_config.SeparatorChar);

        foreach (var raw in commands)
        {
            var command = raw.Trim();
            if (command.Length == 0) continue;

            if (command[0] == _config.CommandChar)
            {
                HandleInternalCommand(command.Substring(1));
            }
            else
            {
                _host.SendToGame(command, true);
            }
        }
    }

    private void HandleInternalCommand(string command)
    {
        var parts = ArgumentParser.ParseArgs(command);
        if (parts.Count == 0) return;

        var name = parts[0].ToLowerInvariant();

        switch (name)
        {
            case "echo":
                if (parts.Count > 1)
                {
                    _host.Echo(string.Join(" ", parts.Skip(1)));
                }
                break;

            case "send":
                if (parts.Count > 1)
                {
                    _host.SendToGame(string.Join(" ", parts.Skip(1)));
                }
                break;

            case "wait":
                if (parts.Count > 2 && double.TryParse(parts[1], out var delay))
                {
                    var action = string.Join(" ", parts.Skip(2));
                    _commandQueue.AddToQueue(delay, action, false, false, false);
                }
                break;

            case "event":
                if (parts.Count > 2 && double.TryParse(parts[1], out var evDelay))
                {
                    var action = string.Join(" ", parts.Skip(2));
                    _eventQueue.Add(evDelay, action);
                }
                break;

            case "script":
                if (parts.Count > 1)
                {
                    _host.RunScript(parts[1]);
                }
                break;

            default:
                _host.Echo($"Unknown command: {name}");
                break;
        }
    }

    public void Tick(bool inRoundtime = false, bool isWebbed = false, bool isStunned = false)
    {
        var queued = _commandQueue.Poll(inRoundtime, isWebbed, isStunned);
        if (!string.IsNullOrEmpty(queued))
        {
            ProcessInput(queued);
        }

        var ev = _eventQueue.Poll();
        if (!string.IsNullOrEmpty(ev))
        {
            ProcessInput(ev);
        }
    }
}
