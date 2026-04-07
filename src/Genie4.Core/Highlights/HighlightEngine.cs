namespace Genie4.Core.Highlights;

public sealed class HighlightEngine
{
    private readonly List<HighlightRule> _rules = new();

    public IReadOnlyList<HighlightRule> Rules => _rules;

    public HighlightRule AddRule(string pattern, string foregroundColor, string backgroundColor = "", bool isRegex = false, bool caseSensitive = false, bool isEnabled = true)
    {
        var rule = new HighlightRule(pattern, foregroundColor, backgroundColor, isRegex, caseSensitive, isEnabled);
        _rules.Add(rule);
        return rule;
    }

    public bool RemoveRule(string pattern)
        => _rules.RemoveAll(r => r.Pattern == pattern) > 0;

    // Returns the first matching enabled rule for a plain-text line, or null.
    public HighlightRule? Match(string plainText)
    {
        foreach (var rule in _rules)
        {
            if (rule.IsEnabled && rule.Matches(plainText))
                return rule;
        }
        return null;
    }
}
