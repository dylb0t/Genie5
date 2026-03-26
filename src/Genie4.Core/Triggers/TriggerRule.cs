using System.Text.RegularExpressions;

namespace Genie4.Core.Triggers;

public sealed class TriggerRule
{
    public TriggerRule(string pattern, string action, bool caseSensitive = false, bool isEnabled = true)
    {
        Pattern = pattern;
        Action = action;
        CaseSensitive = caseSensitive;
        IsEnabled = isEnabled;

        var options = RegexOptions.Compiled;
        if (!caseSensitive)
        {
            options |= RegexOptions.IgnoreCase;
        }

        Regex = new Regex(pattern, options);
    }

    public string Pattern { get; }
    public string Action { get; }
    public bool CaseSensitive { get; }
    public bool IsEnabled { get; set; }
    public Regex Regex { get; }

    public bool IsMatch(string line) => IsEnabled && Regex.IsMatch(line);
}
