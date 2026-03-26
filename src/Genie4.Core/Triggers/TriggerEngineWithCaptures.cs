namespace Genie4.Core.Triggers;

using System.Text.RegularExpressions;
using Genie4.Core.Commanding;

public sealed class TriggerEngineWithCaptures
{
    private readonly List<TriggerRule> _triggers = new();
    private readonly ICommandHost _host;
    private readonly CommandEngine _commandEngine;

    public TriggerEngineWithCaptures(ICommandHost host, CommandEngine commandEngine)
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
            var match = trigger.Regex.Match(line);
            if (!trigger.IsEnabled || !match.Success)
            {
                continue;
            }

            var expandedAction = ExpandAction(trigger.Action, match);
            _host.Echo($"[trigger] {trigger.Pattern}");
            _commandEngine.ProcessInput(expandedAction);
        }
    }

    public IReadOnlyList<TriggerRule> GetTriggers() => _triggers;

    private static string ExpandAction(string action, Match match)
    {
        var result = action;
        for (var i = 0; i < match.Groups.Count; i++)
        {
            result = result.Replace("$" + i, match.Groups[i].Value);
        }

        return result;
    }
}
