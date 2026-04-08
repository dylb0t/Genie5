using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Genie4.Core.Extensions;
using Genie4.Core.Extensions.Builtin;

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

    /// <summary>
    /// Session-wide global variables, accessible from scripts as <c>$Name</c>.
    /// Populated by <c>#tvar</c>, the EXP tracker, and host code (mapper, profile, etc).
    /// Case-insensitive.
    /// </summary>
    public Dictionary<string, string> Globals { get; }
        = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// In-process extension manager. Built-in trackers (EXP, info) are
    /// registered at construction; future plugins will register here too.
    /// </summary>
    public ExtensionManager Extensions { get; }

    public ScriptEngine(string scriptsDir, TypeAheadSession typeAhead,
                        Action<string> sendCommand, Action<string> echo)
    {
        _scriptsDir  = scriptsDir;
        _typeAhead   = typeAhead;
        _sendCommand = sendCommand;
        _echo        = echo;
        Extensions   = new ExtensionManager(new EngineExtensionHost(this));
        Extensions.Register(new ExpTrackerExtension());
        Extensions.Register(new InfoTrackerExtension());
        Directory.CreateDirectory(_scriptsDir);
    }

    /// <summary>
    /// Adapts <see cref="ScriptEngine"/> to the <see cref="IExtensionHost"/>
    /// surface. Extensions only see this — never the engine itself.
    /// </summary>
    private sealed class EngineExtensionHost : IExtensionHost
    {
        private readonly ScriptEngine _engine;
        public EngineExtensionHost(ScriptEngine engine) { _engine = engine; }
        public IDictionary<string, string> Globals  => _engine.Globals;
        public void Echo(string text)               => _engine._echo(text);
        public void SendCommand(string command)     => _engine._sendCommand(command);
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
        if (string.IsNullOrEmpty(line)) return;

        // Always dispatch to extensions — trackers must populate their
        // globals even when no scripts are running, so the next script that
        // starts sees up-to-date values.
        Extensions.DispatchGameLine(line);

        if (_instances.Count == 0) return;

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

            // action triggers — persistent, fire on every matching line
            if (inst.ActionsEnabled && inst.Actions.Count > 0)
            {
                // snapshot — actions may add/remove themselves while firing
                var snapshot = inst.Actions.ToArray();
                foreach (var act in snapshot)
                {
                    if (!act.Enabled) continue;
                    if (!TryMatch(line, act.Pattern, act.IsRegex, inst, capture: true)) continue;
                    var sub = SubstituteVars(act.Command, inst);
                    try { Dispatch(sub, inst, 0, -1); }
                    catch (Exception ex)
                    { _echo($"[script] {inst.Name} action error: {ex.Message}"); }
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
        Extensions.DispatchPrompt();
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
        // Drain any pending semicolon-split sends before advancing the PC.
        if (inst.PendingSends.Count > 0)
        {
            if (_inFlight >= _typeAhead.Limit) return false;
            var next = inst.PendingSends.Dequeue();
            if (next.Length > 0)
            {
                if (next[0] == '#')
                {
                    HandleMetaCommand(next, inst);
                }
                else
                {
                    _inFlight++;
                    Extensions.DispatchCommand(next);
                    _sendCommand(next);
                }
            }
            return true;
        }

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
        // Brace block delimiters are structural — the parser already mapped
        // jumps over them; at runtime they're no-ops.
        if (t == "{" || t == "}") return true;

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
                bool cond = EvalConditionSafe(condText, inst);
                return HandleConditional(cond, afterThen, inst, lineNo, currentIdx);
            }

            case "else":
                if (inst.ElseJump.TryGetValue(currentIdx, out var elseTarget))
                    inst.Pc = elseTarget;
                return true;

            case "put":
            case "send":
            {
                // Genie meta-commands routed via 'put' (#tvar, #echo, #mapper, ...)
                // are intercepted here instead of being sent to the game.
                if (rest.Length > 0 && rest[0] == '#')
                {
                    HandleMetaCommand(rest, inst);
                    return true;
                }

                // Genie's ';' separates multiple commands in a single put. Each
                // is queued and drained one-per-tick so the type-ahead budget
                // is respected per command, not per put statement.
                var parts = SplitSemicolons(rest);
                if (parts.Count == 0) return true;

                if (_inFlight >= _typeAhead.Limit)
                {
                    inst.Pc--; // re-execute next tick when budget frees up
                    return false;
                }

                var first = parts[0];
                for (int p = 1; p < parts.Count; p++)
                    inst.PendingSends.Enqueue(parts[p]);

                if (first.Length > 0 && first[0] == '#')
                {
                    HandleMetaCommand(first, inst);
                }
                else
                {
                    _inFlight++;
                    Extensions.DispatchCommand(first);
                    _sendCommand(first);
                }
                return true;
            }

            case "echo":
                _echo(rest);
                return true;

            case "pause":
            case "wait":
            case "delay":
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

            case "timer":
            {
                // timer start | stop | clear | reset
                // %timer reads the elapsed seconds since the last 'timer start'.
                var op = rest.Trim().ToLowerInvariant();
                switch (op)
                {
                    case "":
                    case "start": inst.TimerStart = DateTime.UtcNow; break;
                    case "stop":
                    case "clear":
                    case "reset": inst.TimerStart = null;            break;
                    default:
                        _echo($"[script] {inst.Name}:{lineNo} timer: unknown sub-command '{op}'");
                        break;
                }
                return true;
            }

            case "math":
            {
                // math <var> <op> <n>   ops: add | subtract | multiply | divide | set
                var (vn, tail) = SplitCmd(rest);
                var (op, arg)  = SplitCmd(tail);
                if (string.IsNullOrEmpty(vn) || string.IsNullOrEmpty(op))
                {
                    _echo($"[script] {inst.Name}:{lineNo} math: usage 'math <var> <op> <n>'");
                    return true;
                }
                double cur = inst.Vars.TryGetValue(vn, out var cv)
                          && double.TryParse(cv, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
                                ? d : 0;
                if (!double.TryParse(arg.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var n))
                    n = 0;
                double result = op.ToLowerInvariant() switch
                {
                    "add"      => cur + n,
                    "subtract" => cur - n,
                    "multiply" => cur * n,
                    "divide"   => n == 0 ? 0 : cur / n,
                    "modulus"  => n == 0 ? 0 : cur % n,
                    "set"      => n,
                    _          => cur,
                };
                inst.Vars[vn] = result == Math.Floor(result) && !double.IsInfinity(result)
                    ? ((long)result).ToString(CultureInfo.InvariantCulture)
                    : result.ToString("0.################", CultureInfo.InvariantCulture);
                return true;
            }

            case "action":
                HandleAction(rest, inst, lineNo);
                return true;

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
                    var result = ScriptExpression.Eval(expr, inst, Globals);
                    inst.Vars[vn.Trim()] = lower == "evalmath"
                        ? ScriptExpression.ToNum(result).ToString("0.################", CultureInfo.InvariantCulture)
                        : ScriptExpression.ToStr(result);
                }
                catch
                {
                    // Genie convention: failed eval leaves the var as empty string.
                    inst.Vars[vn.Trim()] = string.Empty;
                }
                return true;
            }

            default:
                _echo($"[script] {inst.Name}:{lineNo} unknown command: {cmd}");
                return true;
        }
    }

    /// <summary>
    /// Evaluate a condition, treating empty input and parse errors as false.
    /// Genie scripts routinely test variables that are not yet set; an undefined
    /// <c>$hidden</c> substitutes to <c>""</c> and we want <c>if ($hidden)</c>
    /// to silently mean false rather than crashing.
    /// </summary>
    private bool EvalConditionSafe(string condText, ScriptInstance inst)
    {
        if (string.IsNullOrWhiteSpace(condText)) return false;

        // Strip a single layer of empty parens / whitespace; "(  )" → false.
        var stripped = condText.Trim();
        if (stripped.Length >= 2 && stripped[0] == '(' && stripped[^1] == ')')
        {
            var inner = stripped[1..^1].Trim();
            if (inner.Length == 0) return false;
        }

        try { return ScriptExpression.EvalBool(condText, inst, Globals); }
        catch { return false; }
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

    /// <summary>
    /// Handle a Genie-style meta-command, e.g. <c>#tvar Foo 1</c>, <c>#var Foo 1</c>,
    /// <c>#echo &gt;Log #DAF7A6 ...</c>, <c>#mapper reset</c>. These arrive via
    /// <c>put #...</c> in scripts and never reach the game.
    /// </summary>
    private void HandleMetaCommand(string text, ScriptInstance inst)
    {
        var (cmd, rest) = SplitCmd(text); // cmd starts with '#'
        switch (cmd.ToLowerInvariant())
        {
            case "#tvar":
            {
                // #tvar Name Value
                var (name, value) = SplitCmd(rest);
                if (name.Length > 0) Globals[name] = value;
                return;
            }
            case "#var":
            {
                // #var Name Value — script-local
                var (name, value) = SplitCmd(rest);
                if (name.Length > 0) inst.Vars[name] = value;
                return;
            }
            case "#echo":
            {
                // #echo [>Window] [#RRGGBB] message — strip Window/colour args.
                var msg = rest;
                while (msg.Length > 0)
                {
                    var (tok, after) = SplitCmd(msg);
                    if (tok.Length > 0 && (tok[0] == '>' || tok[0] == '#'))
                    { msg = after; continue; }
                    break;
                }
                _echo(msg);
                return;
            }
            case "#mapper":
                // No-op for now; mapper reset is informational only.
                return;
            default:
                // Unknown meta-commands are silently ignored so scripts that
                // assume Genie4 plugins don't error out.
                return;
        }
    }

    /// <summary>
    /// Handle the <c>action</c> family. Forms:
    ///   action on | off
    ///   action clear
    ///   action remove &lt;pattern&gt;
    ///   action &lt;command&gt; when &lt;pattern&gt;
    ///   action &lt;command&gt; whenre &lt;regex&gt;
    /// The 'when' / 'whenre' keyword splits left/right; whatever comes before
    /// is the command body, whatever comes after is the trigger pattern. The
    /// command body is dispatched as a normal script statement on each fire.
    /// </summary>
    private void HandleAction(string rest, ScriptInstance inst, int lineNo)
    {
        var trimmed = rest.Trim();
        if (trimmed.Length == 0)
        {
            _echo($"[script] {inst.Name}:{lineNo} action: missing arguments");
            return;
        }

        // Optional leading "(label)" attaches a name to this action so it
        // can be toggled or removed by label later.
        string label = string.Empty;
        if (trimmed[0] == '(')
        {
            int close = trimmed.IndexOf(')');
            if (close > 0)
            {
                label   = trimmed[1..close].Trim();
                trimmed = trimmed[(close + 1)..].Trim();
            }
        }

        // Global on/off/clear (only meaningful when no label).
        if (label.Length == 0)
        {
            if (trimmed.Equals("on",    StringComparison.OrdinalIgnoreCase)) { inst.ActionsEnabled = true;  return; }
            if (trimmed.Equals("off",   StringComparison.OrdinalIgnoreCase)) { inst.ActionsEnabled = false; return; }
            if (trimmed.Equals("clear", StringComparison.OrdinalIgnoreCase)) { inst.Actions.Clear();        return; }
        }
        else
        {
            // Per-label control: enable/disable/remove all triggers with this label.
            if (trimmed.Equals("on",     StringComparison.OrdinalIgnoreCase))
            { foreach (var a in inst.Actions) if (LabelMatch(a, label)) a.Enabled = true;  return; }
            if (trimmed.Equals("off",    StringComparison.OrdinalIgnoreCase))
            { foreach (var a in inst.Actions) if (LabelMatch(a, label)) a.Enabled = false; return; }
            if (trimmed.Equals("remove", StringComparison.OrdinalIgnoreCase))
            { inst.Actions.RemoveAll(a => LabelMatch(a, label));                            return; }
        }

        if (trimmed.StartsWith("remove ", StringComparison.OrdinalIgnoreCase))
        {
            var pat = trimmed[7..].Trim();
            int n = inst.Actions.RemoveAll(a =>
                string.Equals(a.Pattern, pat, StringComparison.OrdinalIgnoreCase));
            if (n == 0) _echo($"[script] action: no trigger matched '{pat}'");
            return;
        }

        // Locate " when " or " whenre " outside of quoted strings.
        int whenIdx = FindKeywordOutsideQuotes(trimmed, "when");
        int whenreIdx = FindKeywordOutsideQuotes(trimmed, "whenre");
        bool isRegex = false;
        int kwIdx, kwLen;
        if (whenreIdx >= 0 && (whenIdx < 0 || whenreIdx <= whenIdx))
        { kwIdx = whenreIdx; kwLen = 6; isRegex = true; }
        else if (whenIdx >= 0)
        { kwIdx = whenIdx; kwLen = 4; }
        else
        {
            _echo($"[script] {inst.Name}:{lineNo} action: missing 'when' / 'whenre'");
            return;
        }

        var cmd = trimmed[..kwIdx].Trim();
        var pattern = trimmed[(kwIdx + kwLen)..].Trim();
        if (cmd.Length == 0 || pattern.Length == 0)
        {
            _echo($"[script] {inst.Name}:{lineNo} action: empty command or pattern");
            return;
        }

        inst.Actions.Add(new ScriptAction
        {
            Label   = label,
            Command = cmd,
            Pattern = pattern,
            IsRegex = isRegex,
            Enabled = true,
        });
    }

    private static bool LabelMatch(ScriptAction a, string label)
        => string.Equals(a.Label, label, StringComparison.OrdinalIgnoreCase);

    private static int FindKeywordOutsideQuotes(string s, string keyword)
    {
        bool inStr = false;
        for (int i = 0; i + keyword.Length <= s.Length; i++)
        {
            if (s[i] == '"') { inStr = !inStr; continue; }
            if (inStr) continue;
            if (i > 0 && !char.IsWhiteSpace(s[i - 1])) continue;
            if (!string.Equals(s.Substring(i, keyword.Length), keyword,
                               StringComparison.OrdinalIgnoreCase)) continue;
            int after = i + keyword.Length;
            if (after < s.Length && !char.IsWhiteSpace(s[after])) continue;
            return i;
        }
        return -1;
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Split a command string on unquoted semicolons, trimming each part and
    /// dropping empty pieces. Quoted segments survive intact so a regex like
    /// <c>"foo;bar"</c> isn't accidentally split.
    /// </summary>
    private static List<string> SplitSemicolons(string s)
    {
        var result = new List<string>();
        if (string.IsNullOrEmpty(s)) return result;

        bool inStr = false;
        int start = 0;
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '"') inStr = !inStr;
            else if (s[i] == ';' && !inStr)
            {
                var part = s[start..i].Trim();
                if (part.Length > 0) result.Add(part);
                start = i + 1;
            }
        }
        var tail = s[start..].Trim();
        if (tail.Length > 0) result.Add(tail);
        return result;
    }

    private static (string cmd, string rest) SplitCmd(string s)
    {
        if (string.IsNullOrEmpty(s)) return (string.Empty, string.Empty);
        var i = s.IndexOf(' ');
        if (i < 0) return (s, string.Empty);
        return (s[..i], s[(i + 1)..].Trim());
    }

    private string SubstituteVars(string text, ScriptInstance inst)
    {
        if (text.IndexOf('%') < 0 && text.IndexOf('$') < 0) return text;
        var sb = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c != '%' && c != '$') { sb.Append(c); continue; }
            int j = i + 1;
            // Allow letters, digits, _ and . in variable names so identifiers
            // like Athletics.Ranks resolve as a single token.
            while (j < text.Length &&
                   (char.IsLetterOrDigit(text[j]) || text[j] == '_' || text[j] == '.'))
                j++;
            if (j == i + 1) { sb.Append(c); continue; }
            var name = text[(i + 1)..j];
            string value;
            // Pseudo-variables: computed on each substitution rather than stored.
            if (name.Equals("timer", StringComparison.OrdinalIgnoreCase))
            {
                value = inst.TimerStart is { } t
                    ? ((int)(DateTime.UtcNow - t).TotalSeconds).ToString(CultureInfo.InvariantCulture)
                    : "0";
            }
            else if (c == '$')
                value = Globals.TryGetValue(name, out var gv) ? gv : string.Empty;
            else
                value = inst.Vars.TryGetValue(name, out var lv) ? lv : string.Empty;
            sb.Append(value);
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
