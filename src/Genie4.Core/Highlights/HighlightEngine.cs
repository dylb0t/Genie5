using Genie4.Core.Classes;

namespace Genie4.Core.Highlights;

public sealed class HighlightEngine
{
    private readonly List<HighlightRule> _rules = new();

    public IReadOnlyList<HighlightRule> Rules => _rules;

    /// <summary>Optional class gate — when set, rules whose class is inactive are skipped.</summary>
    public ClassEngine? Classes { get; set; }

    public HighlightRule AddRule(string pattern, string foregroundColor, string backgroundColor = "",
                                 HighlightMatchType matchType = HighlightMatchType.String,
                                 bool caseSensitive = false, bool isEnabled = true,
                                 string className = "")
    {
        var rule = new HighlightRule(pattern, foregroundColor, backgroundColor, matchType, caseSensitive, isEnabled, className);
        _rules.Add(rule);
        if (!string.IsNullOrEmpty(className)) Classes?.Ensure(className);
        return rule;
    }

    public bool RemoveRule(string pattern)
        => _rules.RemoveAll(r => r.Pattern == pattern) > 0;

    public void Clear() => _rules.Clear();

    private bool IsActive(HighlightRule rule)
        => rule.IsEnabled && (Classes?.IsActive(rule.ClassName) ?? true);

    // Returns the first matching active rule for a plain-text line, or null.
    public HighlightRule? Match(string plainText)
    {
        foreach (var rule in _rules)
        {
            if (IsActive(rule) && rule.Matches(plainText))
                return rule;
        }
        return null;
    }
}
