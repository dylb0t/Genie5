namespace Genie4.Core.Triggers;

using Genie4.Core.Commanding;

public sealed class TriggerEngine
{
    private readonly List<TriggerRule> _triggers = new();
    private readonly ICommandHost _host;
    private readonly CommandEngine _commandEngine;

    public TriggerEngine(ICommandHost host, CommandEngine commandEngine)
    {
        _host = host;
        _commandEngine = commandEngine;
    }

    public void AddTrigger(string pattern, string action, bool caseSensitive = false)
    {
        _triggers.Add(new TriggerRule(pattern, action, caseSensitive));
    }

    public void ProcessLine(string line)
    {
        foreach (var trigger in _triggers)
        {
            if (trigger.IsMatch(line))
            {
                _host.Echo($"[trigger] {trigger.Pattern}");
                _commandEngine.ProcessInput(trigger.Action);
            }
        }
    }

    public IReadOnlyList<TriggerRule> GetTriggers() => _triggers;
}
