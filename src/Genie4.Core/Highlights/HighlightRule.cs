using System.Text.RegularExpressions;

namespace Genie4.Core.Highlights;

public sealed class HighlightRule
{
    private Regex? _regex;

    public HighlightRule(string pattern, string foregroundColor, bool isRegex = false, bool caseSensitive = false, bool isEnabled = true)
    {
        Pattern = pattern;
        ForegroundColor = foregroundColor;
        IsRegex = isRegex;
        CaseSensitive = caseSensitive;
        IsEnabled = isEnabled;
        RebuildRegex();
    }

    public string Pattern { get; }
    public string ForegroundColor { get; }
    public bool IsRegex { get; }
    public bool CaseSensitive { get; }
    public bool IsEnabled { get; set; }

    public bool Matches(string line)
    {
        if (IsRegex)
            return _regex?.IsMatch(line) ?? false;

        var comparison = CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        return line.Contains(Pattern, comparison);
    }

    private void RebuildRegex()
    {
        if (!IsRegex) return;
        var opts = RegexOptions.Compiled | (CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase);
        _regex = new Regex(Pattern, opts);
    }
}
