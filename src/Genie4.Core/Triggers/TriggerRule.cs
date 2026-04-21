using System.Text.RegularExpressions;

namespace Genie4.Core.Triggers;

public sealed class TriggerRule
{
    public TriggerRule(string pattern, string action, bool caseSensitive = false,
                       bool isEnabled = true, string className = "")
    {
        Pattern = pattern;
        Action = action;
        CaseSensitive = caseSensitive;
        IsEnabled = isEnabled;
        ClassName = className;

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
    public string ClassName { get; set; }
    public Regex Regex { get; }

    public bool IsMatch(string line) => IsEnabled && Regex.IsMatch(line);
}
