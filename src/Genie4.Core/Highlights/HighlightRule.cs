using System.Text.RegularExpressions;

namespace Genie4.Core.Highlights;

public sealed class HighlightRule
{
    private Regex? _regex;

    public HighlightRule(string pattern, string foregroundColor, string backgroundColor = "", bool isRegex = false, bool caseSensitive = false, bool isEnabled = true)
    {
        Pattern = pattern;
        ForegroundColor = foregroundColor;
        BackgroundColor = backgroundColor;
        IsRegex = isRegex;
        CaseSensitive = caseSensitive;
        IsEnabled = isEnabled;
        RebuildRegex();
    }

    public string Pattern { get; }
    public string ForegroundColor { get; }
    public string BackgroundColor { get; }
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

    /// <summary>
    /// Returns the character ranges that should be coloured.
    /// For regex patterns with capture groups, only the captured substrings are returned.
    /// For plain-text or capture-group-free regex patterns, the full match is returned.
    /// </summary>
    public IReadOnlyList<(int Start, int Length)> GetHighlightRanges(string line)
    {
        if (!IsRegex)
        {
            var cmp = CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            int idx = line.IndexOf(Pattern, cmp);
            if (idx < 0) return [];
            return [(idx, Pattern.Length)];
        }

        if (_regex is null) return [];
        var match = _regex.Match(line);
        if (!match.Success) return [];

        // If the pattern has capture groups, colour only those ranges.
        if (match.Groups.Count > 1)
        {
            var result = new List<(int, int)>();
            for (int i = 1; i < match.Groups.Count; i++)
            {
                var g = match.Groups[i];
                if (g.Success && g.Length > 0)
                    result.Add((g.Index, g.Length));
            }
            if (result.Count > 0) return result;
        }

        // No capture groups — colour the full match.
        return [(match.Index, match.Length)];
    }

    private void RebuildRegex()
    {
        if (!IsRegex) return;
        var opts = RegexOptions.Compiled | (CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase);
        _regex = new Regex(Pattern, opts);
    }
}
