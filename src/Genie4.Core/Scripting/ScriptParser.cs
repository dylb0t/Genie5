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

            // Block form: find first non-empty line whose indent ≤ this line's indent.
            int end = inst.Lines.Count;
            for (int j = i + 1; j < inst.Lines.Count; j++)
            {
                if (inst.Lines[j].Trimmed.Length == 0) continue;
                if (inst.Lines[j].Indent <= line.Indent) { end = j; break; }
            }

            // Optional else at the same indent as the if
            if (end < inst.Lines.Count
                && inst.Lines[end].Indent == line.Indent
                && IsElseLine(inst.Lines[end].Trimmed))
            {
                int elseLine = end;
                int elseEnd  = inst.Lines.Count;
                for (int j = elseLine + 1; j < inst.Lines.Count; j++)
                {
                    if (inst.Lines[j].Trimmed.Length == 0) continue;
                    if (inst.Lines[j].Indent <= line.Indent) { elseEnd = j; break; }
                }
                inst.IfFalseJump[i]      = elseLine + 1;
                inst.ElseJump[elseLine]  = elseEnd;
            }
            else
            {
                inst.IfFalseJump[i] = end;
            }
        }
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
