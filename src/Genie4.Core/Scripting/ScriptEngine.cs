using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Genie4.Core.Scripting;

/// <summary>
/// v2 Genie .cmd script runner. Supports:
///   put/send, echo, pause/wait, goto, gosub/return, label (:name | name:),
///   match/matchre/matchwait, waitfor/waitforre, var/setvariable, exit,
///   if &lt;expr&gt; then ... (inline + indented block w/ optional else),
///   if_1..if_9, eval, evalmath, include (resolved at parse time).
///
/// All <c>put</c>/<c>send</c> calls flow through the shared
/// <see cref="TypeAheadSession"/>: scripts honor the same in-flight budget the
/// mapper uses, and re-pump on each game prompt.
/// </summary>
public sealed class ScriptEngine
{
    private readonly List<ScriptInstance> _instances = new();
    private readonly TypeAheadSession     _typeAhead;
    private readonly Action<string>       _sendCommand;
    private readonly Action<string>       _echo;
    private readonly string               _scriptsDir;

    private int _inFlight;

    public ScriptEngine(string scriptsDir, TypeAheadSession typeAhead,
                        Action<string> sendCommand, Action<string> echo)
    {
        _scriptsDir  = scriptsDir;
        _typeAhead   = typeAhead;
        _sendCommand = sendCommand;
        _echo        = echo;
        Directory.CreateDirectory(_scriptsDir);
    }

    public string ScriptsDir => _scriptsDir;
    public IReadOnlyList<ScriptInstance> Instances => _instances;
    public bool AnyRunning => _instances.Any(i => i.Running);

    public bool TryStart(string name, IReadOnlyList<string> args)
    {
        var path = ResolveScriptPath(name);
        if (path is null)
        {
            _echo($"[script] not found: {name}");
            return false;
        }

        ScriptInstance inst;
        try
        {
            inst = ScriptParser.Parse(name, _scriptsDir, File.ReadAllText(path));
        }
        catch (Exception ex)
        {
            _echo($"[script] parse error in {name}: {ex.Message}");
            return false;
        }

        for (int i = 0; i < args.Count; i++)
            inst.Vars[(i + 1).ToString()] = args[i];
        inst.Vars["scriptname"] = name;

        _instances.Add(inst);
        _echo($"[script] {name} started");
        Tick();
        return true;
    }

    private string? ResolveScriptPath(string name)
    {
        foreach (var ext in new[] { "", ".cmd", ".inc" })
        {
            var p = Path.Combine(_scriptsDir, name + ext);
            if (File.Exists(p)) return p;
        }
        return null;
    }

    public void StopAll()
    {
        if (_instances.Count == 0) return;
        foreach (var i in _instances) i.Running = false;
        _instances.Clear();
        _echo("[script] all scripts stopped");
    }

    public void Stop(string name)
    {
        for (int i = _instances.Count - 1; i >= 0; i--)
        {
            if (!_instances[i].Name.Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
            _instances[i].Running = false;
            _instances.RemoveAt(i);
            _echo($"[script] {name} stopped");
        }
    }

    public void OnGameLine(string line)
    {
        if (string.IsNullOrEmpty(line) || _instances.Count == 0) return;

        for (int i = 0; i < _instances.Count; i++)
        {
            var inst = _instances[i];
            if (!inst.Running) continue;

            // waitfor / waitforre
            if (inst.WaitForPattern != null)
            {
                if (TryMatch(line, inst.WaitForPattern, inst.WaitForIsRegex, inst, capture: true))
                {
                    inst.WaitForPattern  = null;
                    inst.WaitForDeadline = DateTime.MaxValue;
                }
            }

            // match / matchre + matchwait
            if (inst.InMatchWait)
            {
                foreach (var (label, pattern, isRegex) in inst.PendingMatches)
                {
                    if (!TryMatch(line, pattern, isRegex, inst, capture: true)) continue;
                    if (!inst.Labels.TryGetValue(label, out var idx)) continue;

                    inst.Pc                = idx + 1;
                    inst.InMatchWait       = false;
                    inst.MatchWaitDeadline = DateTime.MaxValue;
                    inst.PendingMatches.Clear();
                    break;
                }
            }
        }
        Tick();
    }

    public void OnPrompt()
    {
        if (_inFlight > 0) _inFlight--;
        Tick();
    }

    public void Tick()
    {
        bool progress = true;
        int  guard    = 0;
        while (progress && guard++ < 10_000)
        {
            progress = false;
            for (int i = _instances.Count - 1; i >= 0; i--)
            {
                var inst = _instances[i];
                if (!inst.Running) { _instances.RemoveAt(i); continue; }

                if (inst.Paused)
                {
                    if (inst.PauseUntil != DateTime.MinValue && DateTime.UtcNow >= inst.PauseUntil)
                    { inst.Paused = false; inst.PauseUntil = DateTime.MinValue; }
                    else continue;
                }

                if (inst.InMatchWait)
                {
                    if (DateTime.UtcNow >= inst.MatchWaitDeadline)
                    {
                        inst.InMatchWait       = false;
                        inst.MatchWaitDeadline = DateTime.MaxValue;
                        inst.PendingMatches.Clear();
                    }
                    else continue;
                }

                if (inst.WaitForPattern != null)
                {
                    if (DateTime.UtcNow >= inst.WaitForDeadline)
                    { inst.WaitForPattern = null; inst.WaitForDeadline = DateTime.MaxValue; }
                    else continue;
                }

                if (StepOne(inst)) progress = true;
            }
        }
    }

    // ── Statement dispatch ──────────────────────────────────────────────────

    private bool StepOne(ScriptInstance inst)
    {
        if (inst.Pc >= inst.Lines.Count)
        {
            inst.Running = false;
            _echo($"[script] {inst.Name} done");
            return false;
        }

        var line = inst.Lines[inst.Pc];
        int currentIdx = inst.Pc;
        inst.Pc++;

        var t = line.Trimmed;
        if (t.Length == 0 || t[0] == '#') return true;
        if ((t[0] == ':' || t[^1] == ':') && !t.Contains(' ')) return true;

        var substituted = SubstituteVars(t, inst);
        return Dispatch(substituted, inst, line.LineNumber, currentIdx);
    }

    /// <param name="text">Statement text, already %var/$var-substituted.</param>
    /// <param name="currentIdx">Index in inst.Lines of the source line, used for if/else jump lookups.</param>
    private bool Dispatch(string text, ScriptInstance inst, int lineNo, int currentIdx)
    {
        var (cmd, rest) = SplitCmd(text);
        var lower = cmd.ToLowerInvariant();

        // if_1..if_9
        if (lower.Length == 4 && lower.StartsWith("if_") && char.IsDigit(lower[3]))
        {
            var key = lower[3..];
            bool present = inst.Vars.TryGetValue(key, out var v) && !string.IsNullOrEmpty(v);
            return HandleConditional(present, rest, inst, lineNo, currentIdx);
        }

        switch (lower)
        {
            case "if":
            {
                int thenIdx = ScriptParser.FindThenKeyword(rest);
                if (thenIdx < 0)
                {
                    _echo($"[script] {inst.Name}:{lineNo} 'if' missing 'then'");
                    return true;
                }
                var condText  = rest[..thenIdx].Trim();
                var afterThen = rest[(thenIdx + 4)..].Trim();
                bool cond;
                try { cond = ScriptExpression.EvalBool(condText, inst); }
                catch (Exception ex)
                {
                    _echo($"[script] {inst.Name}:{lineNo} expr error: {ex.Message}");
                    return true;
                }
                return HandleConditional(cond, afterThen, inst, lineNo, currentIdx);
            }

            case "else":
                if (inst.ElseJump.TryGetValue(currentIdx, out var elseTarget))
                    inst.Pc = elseTarget;
                return true;

            case "put":
            case "send":
                if (_inFlight >= _typeAhead.Limit)
                {
                    inst.Pc--; // re-execute next tick when budget frees up
                    return false;
                }
                _inFlight++;
                _sendCommand(rest);
                return true;

            case "echo":
                _echo(rest);
                return true;

            case "pause":
            case "wait":
            {
                double secs = 1.0;
                if (!string.IsNullOrWhiteSpace(rest) &&
                    double.TryParse(rest.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var p))
                    secs = p;
                inst.Paused     = true;
                inst.PauseUntil = DateTime.UtcNow.AddSeconds(secs);
                return false;
            }

            case "goto":
                if (inst.Labels.TryGetValue(rest.Trim(), out var gi))
                    inst.Pc = gi + 1;
                else { _echo($"[script] unknown label: {rest}"); inst.Running = false; }
                return true;

            case "gosub":
            {
                var (label, _) = SplitCmd(rest);
                if (!inst.Labels.TryGetValue(label.Trim(), out var ss))
                { _echo($"[script] unknown label: {label}"); inst.Running = false; return false; }
                inst.GosubStack.Push(inst.Pc);
                inst.Pc = ss + 1;
                return true;
            }

            case "return":
                if (inst.GosubStack.Count > 0) inst.Pc = inst.GosubStack.Pop();
                else inst.Running = false;
                return true;

            case "exit":
                inst.Running = false;
                return false;

            case "match":
            {
                var (label, pat) = SplitCmd(rest);
                if (!string.IsNullOrEmpty(label))
                    inst.PendingMatches.Add((label.Trim(), pat, false));
                return true;
            }

            case "matchre":
            {
                var (label, pat) = SplitCmd(rest);
                if (!string.IsNullOrEmpty(label))
                    inst.PendingMatches.Add((label.Trim(), pat, true));
                return true;
            }

            case "matchwait":
                inst.InMatchWait = true;
                if (double.TryParse(rest.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var mw))
                    inst.MatchWaitDeadline = DateTime.UtcNow.AddSeconds(mw);
                else
                    inst.MatchWaitDeadline = DateTime.MaxValue;
                return false;

            case "waitfor":
                inst.WaitForPattern  = rest;
                inst.WaitForIsRegex  = false;
                inst.WaitForDeadline = DateTime.MaxValue;
                return false;

            case "waitforre":
                inst.WaitForPattern  = rest;
                inst.WaitForIsRegex  = true;
                inst.WaitForDeadline = DateTime.MaxValue;
                return false;

            case "var":
            case "setvariable":
            {
                var (vn, vv) = SplitCmd(rest);
                inst.Vars[vn.Trim()] = vv;
                return true;
            }

            case "eval":
            case "evalmath":
            {
                var (vn, expr) = SplitCmd(rest);
                if (string.IsNullOrEmpty(vn))
                {
                    _echo($"[script] {inst.Name}:{lineNo} {lower} needs varname");
                    return true;
                }
                try
                {
                    var result = ScriptExpression.Eval(expr, inst);
                    inst.Vars[vn.Trim()] = lower == "evalmath"
                        ? ScriptExpression.ToNum(result).ToString("0.################", CultureInfo.InvariantCulture)
                        : ScriptExpression.ToStr(result);
                }
                catch (Exception ex)
                {
                    _echo($"[script] {inst.Name}:{lineNo} eval error: {ex.Message}");
                }
                return true;
            }

            default:
                _echo($"[script] {inst.Name}:{lineNo} unknown command: {cmd}");
                return true;
        }
    }

    private bool HandleConditional(bool cond, string afterThen,
                                    ScriptInstance inst, int lineNo, int currentIdx)
    {
        if (afterThen.Length > 0)
        {
            // inline form: execute the after-then as a statement (only when true)
            if (cond) return Dispatch(afterThen, inst, lineNo, currentIdx);
            return true;
        }

        // block form
        if (cond) return true; // fall through into the block
        if (inst.IfFalseJump.TryGetValue(currentIdx, out var j)) inst.Pc = j;
        return true;
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static (string cmd, string rest) SplitCmd(string s)
    {
        if (string.IsNullOrEmpty(s)) return (string.Empty, string.Empty);
        var i = s.IndexOf(' ');
        if (i < 0) return (s, string.Empty);
        return (s[..i], s[(i + 1)..].Trim());
    }

    private static string SubstituteVars(string text, ScriptInstance inst)
    {
        if (text.IndexOf('%') < 0 && text.IndexOf('$') < 0) return text;
        var sb = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c != '%' && c != '$') { sb.Append(c); continue; }
            int j = i + 1;
            while (j < text.Length && (char.IsLetterOrDigit(text[j]) || text[j] == '_')) j++;
            if (j == i + 1) { sb.Append(c); continue; }
            var name = text[(i + 1)..j];
            sb.Append(inst.Vars.TryGetValue(name, out var v) ? v : string.Empty);
            i = j - 1;
        }
        return sb.ToString();
    }

    private static bool TryMatch(string line, string pattern, bool isRegex,
                                  ScriptInstance inst, bool capture)
    {
        if (string.IsNullOrEmpty(pattern)) return false;
        if (!isRegex)
            return line.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0;

        Match m;
        try { m = Regex.Match(line, pattern, RegexOptions.IgnoreCase); }
        catch { return false; }
        if (!m.Success) return false;
        if (capture)
        {
            inst.Vars["0"] = m.Value;
            for (int i = 1; i < m.Groups.Count && i <= 9; i++)
                inst.Vars[i.ToString()] = m.Groups[i].Value;
        }
        return true;
    }
}
