namespace Genie4.Core.Scripting;

public static class ScriptParser
{
    public static ScriptInstance Parse(string name, string scriptsDir, string source)
    {
        var inst = new ScriptInstance { Name = name };

        // 1. Recursive include expansion → flat raw line list.
        var raw = new List<(string Origin, int LineNo, string Raw)>();
        ExpandIncludes(name, source, scriptsDir, raw,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        // 2. Build ScriptLine list with indent + label table.
        for (int i = 0; i < raw.Count; i++)
        {
            var (origin, lineNo, line) = raw[i];
            int indent = 0;
            while (indent < line.Length && (line[indent] == ' ' || line[indent] == '\t'))
                indent++;
            var trimmed = line.Trim();
            inst.Lines.Add(new ScriptLine(lineNo, origin, line, trimmed, indent));

            if (trimmed.Length > 1 && !trimmed.Contains(' '))
            {
                if (trimmed[0] == ':')      inst.Labels[trimmed[1..]]   = i;
                else if (trimmed[^1] == ':') inst.Labels[trimmed[..^1]] = i;
            }
        }

        // 3. Build if/else jump maps for block-form conditionals.
        BuildIfMaps(inst);
        return inst;
    }

    private static void ExpandIncludes(
        string origin, string source, string scriptsDir,
        List<(string, int, string)> output, HashSet<string> visited)
    {
        if (!visited.Add(origin)) return;

        var lines = source.Replace("\r\n", "\n").Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            var raw = lines[i];
            var t   = raw.Trim();

            if (t.StartsWith("include ", StringComparison.OrdinalIgnoreCase))
            {
                var incName = t[8..].Trim();
                var path    = ResolveIncludePath(scriptsDir, incName);
                if (path != null)
                {
                    var subOrigin = Path.GetFileNameWithoutExtension(path);
                    ExpandIncludes(subOrigin, File.ReadAllText(path), scriptsDir, output, visited);
                }
                else
                {
                    output.Add((origin, i + 1, $"echo [script] include not found: {incName}"));
                }
                continue;
            }

            output.Add((origin, i + 1, raw));
        }
    }

    private static string? ResolveIncludePath(string dir, string name)
    {
        foreach (var ext in new[] { "", ".inc", ".cmd" })
        {
            var p = Path.Combine(dir, name + ext);
            if (File.Exists(p)) return p;
        }
        return null;
    }

    private static void BuildIfMaps(ScriptInstance inst)
    {
        for (int i = 0; i < inst.Lines.Count; i++)
        {
            var line = inst.Lines[i];
            var t    = line.Trimmed;
            if (t.Length == 0) continue;

            // First token
            int sp = t.IndexOf(' ');
            var first = sp < 0 ? t : t[..sp];
            bool isPlainIf = first.Equals("if", StringComparison.OrdinalIgnoreCase);
            bool isIfN     = first.Length == 4
                          && first.StartsWith("if_", StringComparison.OrdinalIgnoreCase)
                          && char.IsDigit(first[3]);
            if (!isPlainIf && !isIfN) continue;

            var rest = sp < 0 ? "" : t[(sp + 1)..];
            int thenIdx = FindThenKeyword(rest);
            if (thenIdx < 0) continue;
            var afterThen = rest[(thenIdx + 4)..].Trim();
            if (afterThen.Length > 0) continue; // inline form, no block

            // Detect brace style: next non-empty line is "{" alone.
            int braceOpen = NextNonEmpty(inst, i + 1);
            bool useBraces = braceOpen >= 0 && inst.Lines[braceOpen].Trimmed == "{";

            int bodyStart, bodyEnd;
            if (useBraces)
            {
                bodyStart = braceOpen + 1;
                bodyEnd   = FindMatchingBrace(inst, braceOpen);
                if (bodyEnd < 0) continue; // unmatched
            }
            else
            {
                // Indent-based block: lines indented further than the if.
                bodyStart = i + 1;
                bodyEnd   = inst.Lines.Count;
                for (int j = bodyStart; j < inst.Lines.Count; j++)
                {
                    if (inst.Lines[j].Trimmed.Length == 0) continue;
                    if (inst.Lines[j].Indent <= line.Indent) { bodyEnd = j; break; }
                }
            }

            // Optional else: the next non-empty token after the body that
            // begins with "else" (at the same indent as the if for indent
            // form, or any indent for brace form).
            int afterBody  = useBraces ? bodyEnd + 1 : bodyEnd;
            int elseLineIx = NextNonEmpty(inst, afterBody);
            bool hasElse = elseLineIx >= 0
                        && IsElseLine(inst.Lines[elseLineIx].Trimmed)
                        && (useBraces || inst.Lines[elseLineIx].Indent == line.Indent);

            if (!hasElse)
            {
                // false-branch: jump straight past the body
                inst.IfFalseJump[i] = useBraces ? bodyEnd : bodyEnd;
                continue;
            }

            // Else body
            int elseBodyStart, elseBodyEnd;
            int elseBraceOpen = NextNonEmpty(inst, elseLineIx + 1);
            bool elseUseBraces = elseBraceOpen >= 0
                              && inst.Lines[elseBraceOpen].Trimmed == "{";
            if (elseUseBraces)
            {
                elseBodyStart = elseBraceOpen + 1;
                elseBodyEnd   = FindMatchingBrace(inst, elseBraceOpen);
                if (elseBodyEnd < 0) { inst.IfFalseJump[i] = bodyEnd; continue; }
            }
            else
            {
                elseBodyStart = elseLineIx + 1;
                elseBodyEnd   = inst.Lines.Count;
                for (int j = elseBodyStart; j < inst.Lines.Count; j++)
                {
                    if (inst.Lines[j].Trimmed.Length == 0) continue;
                    if (inst.Lines[j].Indent <= line.Indent) { elseBodyEnd = j; break; }
                }
            }

            // When the if-condition is false: jump to the start of the else
            // body (skip past the else line itself and any opening brace).
            inst.IfFalseJump[i]         = elseBodyStart;
            // When the true branch finishes, the else line is reached: skip
            // past the entire else body.
            inst.ElseJump[elseLineIx]   = elseUseBraces ? elseBodyEnd + 1 : elseBodyEnd;
        }
    }

    private static int NextNonEmpty(ScriptInstance inst, int from)
    {
        for (int j = from; j < inst.Lines.Count; j++)
            if (inst.Lines[j].Trimmed.Length > 0) return j;
        return -1;
    }

    /// <summary>Find the matching '}' for a '{' at <paramref name="openIdx"/>.</summary>
    private static int FindMatchingBrace(ScriptInstance inst, int openIdx)
    {
        int depth = 1;
        for (int j = openIdx + 1; j < inst.Lines.Count; j++)
        {
            var t = inst.Lines[j].Trimmed;
            if (t == "{") depth++;
            else if (t == "}")
            {
                depth--;
                if (depth == 0) return j;
            }
        }
        return -1;
    }

    private static bool IsElseLine(string t)
    {
        if (!t.StartsWith("else", StringComparison.OrdinalIgnoreCase)) return false;
        if (t.Length == 4) return true;
        return t[4] == ' ' || t[4] == '\t';
    }

    /// <summary>
    /// Locate the keyword "then" outside of double-quoted strings, with whitespace
    /// (or string boundary) on both sides. Returns -1 if not found.
    /// </summary>
    public static int FindThenKeyword(string s)
    {
        bool inStr = false;
        for (int i = 0; i + 4 <= s.Length; i++)
        {
            if (s[i] == '"') { inStr = !inStr; continue; }
            if (inStr) continue;
            bool leftOk  = i == 0 || char.IsWhiteSpace(s[i - 1]);
            if (!leftOk) continue;
            if (!string.Equals(s.Substring(i, 4), "then", StringComparison.OrdinalIgnoreCase)) continue;
            bool rightOk = i + 4 == s.Length || char.IsWhiteSpace(s[i + 4]);
            if (!rightOk) continue;
            return i;
        }
        return -1;
    }
}
