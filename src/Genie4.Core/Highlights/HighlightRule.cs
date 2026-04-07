using System.Text.RegularExpressions;

namespace Genie4.Core.Highlights;

public enum HighlightMatchType
{
    /// <summary>Substring contains; colour only the matched text.</summary>
    String,
    /// <summary>Substring contains; colour the entire line.</summary>
    Line,
    /// <summary>Line starts with text; colour the entire line.</summary>
    BeginsWith,
    /// <summary>Regular expression; colour matched/captured ranges.</summary>
    Regex,
}

public sealed class HighlightRule
{
    private Regex? _regex;

    public HighlightRule(string pattern, string foregroundColor, string backgroundColor = "",
                         HighlightMatchType matchType = HighlightMatchType.String,
                         bool caseSensitive = false, bool isEnabled = true)
    {
        Pattern = pattern;
        ForegroundColor = foregroundColor;
        BackgroundColor = backgroundColor;
        MatchType = matchType;
        CaseSensitive = caseSensitive;
        IsEnabled = isEnabled;
        RebuildRegex();
    }

    public string Pattern { get; }
    public string ForegroundColor { get; }
    public string BackgroundColor { get; }
    public HighlightMatchType MatchType { get; }
    public bool CaseSensitive { get; }
    public bool IsEnabled { get; set; }

    public bool Matches(string line)
    {
        var cmp = CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        return MatchType switch
        {
            HighlightMatchType.Regex      => _regex?.IsMatch(line) ?? false,
            HighlightMatchType.BeginsWith => line.StartsWith(Pattern, cmp),
            _                             => line.Contains(Pattern, cmp),
        };
    }

    /// <summary>
    /// Returns the character ranges that should be coloured.
    /// String → matched substring only.  Line / BeginsWith → entire line.
    /// Regex with capture groups → only those captures; otherwise the full match.
    /// </summary>
    public IReadOnlyList<(int Start, int Length)> GetHighlightRanges(string line)
    {
        var cmp = CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        switch (MatchType)
        {
            case HighlightMatchType.String:
            {
                int idx = line.IndexOf(Pattern, cmp);
                if (idx < 0) return [];
                return [(idx, Pattern.Length)];
            }

            case HighlightMatchType.Line:
            {
                if (!line.Contains(Pattern, cmp)) return [];
                return [(0, line.Length)];
            }

            case HighlightMatchType.BeginsWith:
            {
                if (!line.StartsWith(Pattern, cmp)) return [];
                return [(0, line.Length)];
            }

            case HighlightMatchType.Regex:
            {
                if (_regex is null) return [];
                var match = _regex.Match(line);
                if (!match.Success) return [];

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

                return [(match.Index, match.Length)];
            }
        }

        return [];
    }

    private void RebuildRegex()
    {
        if (MatchType != HighlightMatchType.Regex) return;
        var opts = RegexOptions.Compiled | (CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase);
        _regex = new Regex(Pattern, opts);
    }
}
