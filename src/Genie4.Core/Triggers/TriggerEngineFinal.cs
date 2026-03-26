namespace Genie4.Core.Triggers;

using System.Text.RegularExpressions;
using Genie4.Core.Commanding;

public sealed class TriggerEngineFinal
{
    private readonly List<TriggerRule> _triggers = new();
    private readonly ICommandHost _host;
    private readonly CommandEngine _commandEngine;

    public TriggerEngineFinal(ICommandHost host, CommandEngine commandEngine)
    {
        _host = host;
        _commandEngine = commandEngine;
    }

    public IReadOnlyList<TriggerRule> Triggers => _triggers;

    public TriggerRule AddTrigger(string pattern, string action, bool caseSensitive = false, bool isEnabled = true)
    {
        var trigger = new TriggerRule(pattern, action, caseSensitive, isEnabled);
        _triggers.Add(trigger);
        return trigger;
    }

    public bool RemoveTrigger(string pattern)
    {
        var removed = _triggers.RemoveAll(t => t.Pattern == pattern);
        return removed > 0;
    }

    public bool SetEnabled(string pattern, bool isEnabled)
    {
        var trigger = _triggers.FirstOrDefault(t => t.Pattern == pattern);
        if (trigger is null)
        {
            return false;
        }

        trigger.IsEnabled = isEnabled;
        return true;
    }

    public void ProcessLine(string line, bool echoTriggerDebug = true)
    {
        foreach (var trigger in _triggers)
        {
            var match = trigger.Regex.Match(line);
            if (!trigger.IsEnabled || !match.Success)
            {
                continue;
            }

            var expandedAction = ExpandAction(trigger.Action, match);
            if (echoTriggerDebug)
            {
                _host.Echo($"[trigger] {trigger.Pattern} => {expandedAction}");
            }
            _commandEngine.ProcessInput(expandedAction);
        }
    }

    private static string ExpandAction(string action, Match match)
    {
        var result = action;
        for (var i = match.Groups.Count - 1; i >= 0; i--)
        {
            result = result.Replace("$" + i, match.Groups[i].Value);
        }

        return result;
    }
}
