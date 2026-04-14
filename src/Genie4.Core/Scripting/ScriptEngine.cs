using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Genie4.Core.Extensions;
using Genie4.Core.Extensions.Builtin;

namespace Genie4.Core.Scripting;

/// <summary>
///   Genie .cmd script runner. Supports:
///   put/send, echo, pause/wait, goto, gosub/return, label (:name | name:),
///   match/matchre/matchwait, waitfor/waitforre, var/setvariable, exit,
///   if <expr>; then ... (inline + indented block w/ optional else),
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
    private readonly Action<string>?      _handleHashCmd;
    private readonly string               _scriptsDir;

    /// <summary>
    /// Directed echo: (message, windowName, hexColor). When window or color
    /// are null the host falls back to the main game output / default colour.
    /// Set after construction by the UI layer.
    /// </summary>
    public Action<string, string?, string?>? EchoTo { get; set; }

    /// <summary>
    /// Echoes a command sent by a script to the game window. Args: (scriptName, command).
    /// Set by the UI layer to render with the "scriptecho" preset colour.
    /// </summary>
    public Action<string, string>? EchoCommand { get; set; }

    /// <summary>
    /// Returns true when the game character is currently in roundtime.
    /// Set by the UI layer; used by <c>pause</c> and <c>wait</c> to hold
    /// until roundtime resolves.
    /// </summary>
    public Func<bool>? InRoundtime { get; set; }

    /// <summary>
    /// Schedule a <see cref="Tick"/> call after the given delay. Set by the
    /// UI layer (e.g. a DispatcherTimer) so that time-based unblocks (delay,
    /// pause) don't depend on the next server event arriving.
    /// </summary>
    public Action<TimeSpan>? ScheduleTick { get; set; }

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
                        Action<string> sendCommand, Action<string> echo,
                        Action<string>? handleHashCmd = null)
    {
        _scriptsDir   = scriptsDir;
        _typeAhead    = typeAhead;
        _sendCommand  = sendCommand;
        _echo         = echo;
        _handleHashCmd = handleHashCmd;
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

    /// <summary>Fired when a script finishes (done or stopped). Arg is the script name.</summary>
    public event Action<string>? ScriptFinished;

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
        inst.Vars["0"] = string.Join(" ", args);
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
        var names = _instances.Select(i => i.Name).ToList();
        foreach (var i in _instances) i.Running = false;
        _instances.Clear();
        _echo("[script] all scripts stopped");
        foreach (var n in names) ScriptFinished?.Invoke(n);
    }

    public void Stop(string name)
    {
        for (int i = _instances.Count - 1; i >= 0; i--)
        {
            if (!_instances[i].Name.Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
            _instances[i].Running = false;
            _instances.RemoveAt(i);
            _echo($"[script] {name} stopped");
            ScriptFinished?.Invoke(name);
        }
    }

    public void PauseScript(string name)
    {
        foreach (var inst in _instances)
            if (inst.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            { inst.UserPaused = true; _echo($"[script] {name} paused"); }
    }

    public void ResumeScript(string name)
    {
        foreach (var inst in _instances)
            if (inst.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            { inst.UserPaused = false; _echo($"[script] {name} resumed"); }
    }

    public void PauseAll()
    {
        foreach (var inst in _instances) inst.UserPaused = true;
        _echo("[script] all scripts paused");
    }

    public void ResumeAll()
    {
        foreach (var inst in _instances) inst.UserPaused = false;
        _echo("[script] all scripts resumed");
        Tick();
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

            FireActions(inst, line);

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

        // Signal 'wait'-paused scripts that a prompt has arrived. The tick
        // loop will then check roundtime before actually unblocking.
        for (int i = 0; i < _instances.Count; i++)
        {
            var inst = _instances[i];
            if (inst.Paused && inst.PauseMode == PauseMode.Wait &&
                inst.PauseUntil == DateTime.MinValue)
            {
                inst.PauseUntil = DateTime.UtcNow;
            }
        }

        // Re-check eval-form actions each prompt so transitions in globals
        // (e.g. preset timers) fire even without a corresponding game line.
        for (int i = 0; i < _instances.Count; i++)
            FireActions(_instances[i], null);
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
                // The list can shrink during iteration (e.g. an action
                // calls StopAll/Stop, or a script exits). Re-check bounds.
                if (i >= _instances.Count) continue;
                var inst = _instances[i];
                if (!inst.Running) { _instances.RemoveAt(i); continue; }
                if (inst.UserPaused) continue;

                if (inst.Paused)
                {
                    bool rt = InRoundtime?.Invoke() ?? false;
                    bool unblock = inst.PauseMode switch
                    {
                        // Pause: timer expired AND roundtime resolved
                        PauseMode.Pause => inst.PauseUntil != DateTime.MinValue
                                           && DateTime.UtcNow >= inst.PauseUntil && !rt,
                        // Wait: prompt received (PauseUntil set by OnPrompt) AND roundtime resolved
                        PauseMode.Wait  => inst.PauseUntil != DateTime.MinValue
                                           && DateTime.UtcNow >= inst.PauseUntil && !rt,
                        // Delay: timer expired only (ignores roundtime)
                        PauseMode.Delay => inst.PauseUntil != DateTime.MinValue
                                           && DateTime.UtcNow >= inst.PauseUntil,
                        _ => false,
                    };
                    if (unblock)
                    { inst.Paused = false; inst.PauseMode = PauseMode.None; inst.PauseUntil = DateTime.MinValue; }
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

                if (inst.WaitEvalExpr != null)
                {
                    // Re-evaluate each tick — cheap and matches Genie4 semantics
                    // of "unblock as soon as the condition flips to true".
                    bool done = false;
                    try { done = ScriptExpression.EvalBool(inst.WaitEvalExpr, inst, Globals); }
                    catch { /* treat parse error as still-waiting */ }
                    if (done || DateTime.UtcNow >= inst.WaitEvalDeadline)
                    { inst.WaitEvalExpr = null; inst.WaitEvalDeadline = DateTime.MaxValue; }
                    else continue;
                }

                if (StepOne(inst)) progress = true;
            }
        }
    }

    /// <summary>Run registered actions against either a game line (regex/literal
    /// patterns) or, when <paramref name="line"/> is null, against eval-form
    /// actions only. Eval actions fire on rising edge (false → true).</summary>
    private void FireActions(ScriptInstance inst, string? line)
    {
        if (!inst.ActionsEnabled || inst.Actions.Count == 0) return;
        var snapshot = inst.Actions.ToArray();
        foreach (var act in snapshot)
        {
            if (!act.Enabled) continue;
            bool fire;
            if (act.IsEval)
            {
                bool cur;
                try { cur = ScriptExpression.EvalBool(act.Pattern, inst, Globals); }
                catch { cur = false; }
                fire = cur && !act.LastEvalResult;
                act.LastEvalResult = cur;
            }
            else
            {
                if (line is null) continue;
                fire = TryMatch(line, act.Pattern, act.IsRegex, inst, capture: true);
            }
            if (!fire) continue;
            DbgEcho(inst, 5, $"action fired: \"{act.Command}\" (pattern: \"{act.Pattern}\")");
            var sub = SubstituteVars(act.Command, inst);
            try { Dispatch(sub, inst, 0, -1); }
            catch (Exception ex)
            { _echo($"[script] {inst.Name} action error: {ex.Message}"); }
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
                    EchoCommand?.Invoke(inst.Name, next);
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
            ScriptFinished?.Invoke(inst.Name);
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

        // Level 10: trace every executed line
        DbgEcho(inst, 10, $"{inst.Name}:{line.LineNumber} {substituted}");

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
            DbgEcho(inst, 3, $"if_{key} (%%{key}=\"{v ?? ""}\") = {present}");
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
                DbgEcho(inst, 3, $"if ({condText}) = {cond}");
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
                    EchoCommand?.Invoke(inst.Name, first);
                    Extensions.DispatchCommand(first);
                    _sendCommand(first);
                }
                return true;
            }

            case "echo":
                _echo(rest);
                return true;

            case "pause":
            {
                // pause [N] — block for N seconds (default 1) AND until
                // roundtime resolves, whichever finishes last.
                double secs = 1.0;
                if (!string.IsNullOrWhiteSpace(rest) &&
                    double.TryParse(rest.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var p))
                    secs = p;
                inst.Paused     = true;
                inst.PauseMode  = PauseMode.Pause;
                inst.PauseUntil = DateTime.UtcNow.AddSeconds(secs);
                ScheduleTick?.Invoke(TimeSpan.FromSeconds(secs + 0.05));
                DbgEcho(inst, 2, $"pause {secs}s (+ roundtime)");
                return false;
            }

            case "wait":
            {
                // wait — block until next game prompt AND until roundtime
                // resolves. No timer component.
                inst.Paused     = true;
                inst.PauseMode  = PauseMode.Wait;
                inst.PauseUntil = DateTime.MinValue; // no timer — prompt-driven
                DbgEcho(inst, 2, "wait (prompt + roundtime)");
                return false;
            }

            case "delay":
            {
                // delay [N] — block for N seconds (default 1). Ignores
                // roundtime and game prompts entirely.
                double secs = 1.0;
                if (!string.IsNullOrWhiteSpace(rest) &&
                    double.TryParse(rest.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var p))
                    secs = p;
                inst.Paused     = true;
                inst.PauseMode  = PauseMode.Delay;
                inst.PauseUntil = DateTime.UtcNow.AddSeconds(secs);
                ScheduleTick?.Invoke(TimeSpan.FromSeconds(secs + 0.05));
                DbgEcho(inst, 2, $"delay {secs}s (ignoring RT)");
                return false;
            }

            case "goto":
                if (inst.Labels.TryGetValue(rest.Trim(), out var gi))
                {
                    inst.Pc = gi + 1;
                    DbgEcho(inst, 1, $"goto {rest.Trim()} → line {gi + 1}");
                }
                else { _echo($"[script] unknown label: {rest}"); inst.Running = false; }
                return true;

            case "gosub":
            {
                var (label, gosubArgs) = SplitCmd(rest);
                if (!inst.Labels.TryGetValue(label.Trim(), out var ss))
                { _echo($"[script] unknown label: {label}"); inst.Running = false; return false; }
                inst.GosubStack.Push(inst.Pc);
                inst.Pc = ss + 1;
                DbgEcho(inst, 1, $"gosub {label.Trim()} → line {ss + 1}" +
                    (string.IsNullOrEmpty(gosubArgs) ? "" : $" args: {gosubArgs}"));
                // Gosub arguments: %0/$0 = full arg string, %1/$1..%9/$9 = individual args.
                // These overwrite the caller's values; Genie4 does the same.
                if (!string.IsNullOrEmpty(gosubArgs))
                {
                    inst.Vars["0"] = gosubArgs;
                    var parts = gosubArgs.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    for (int a = 0; a < 9; a++)
                        inst.Vars[(a + 1).ToString()] = a < parts.Length ? parts[a] : string.Empty;
                }
                return true;
            }

            case "return":
                if (inst.GosubStack.Count > 0)
                {
                    var retPc = inst.GosubStack.Pop();
                    DbgEcho(inst, 1, $"return → line {retPc}");
                    inst.Pc = retPc;
                }
                else inst.Running = false;
                return true;

            case "exit":
                inst.Running = false;
                ScriptFinished?.Invoke(inst.Name);
                return false;

            case "match":
            {
                var (label, pat) = SplitCmd(rest);
                if (!string.IsNullOrEmpty(label))
                {
                    inst.PendingMatches.Add((label.Trim(), pat, false));
                    DbgEcho(inst, 2, $"match {label.Trim()} \"{pat}\"");
                }
                return true;
            }

            case "matchre":
            {
                var (label, pat) = SplitCmd(rest);
                if (!string.IsNullOrEmpty(label))
                {
                    inst.PendingMatches.Add((label.Trim(), pat, true));
                    DbgEcho(inst, 2, $"matchre {label.Trim()} \"{pat}\"");
                }
                return true;
            }

            case "matchwait":
                inst.InMatchWait = true;
                if (double.TryParse(rest.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var mw))
                    inst.MatchWaitDeadline = DateTime.UtcNow.AddSeconds(mw);
                else
                    inst.MatchWaitDeadline = DateTime.MaxValue;
                DbgEcho(inst, 2, $"matchwait ({inst.PendingMatches.Count} patterns" +
                    (mw > 0 ? $", timeout {mw}s)" : ")"));
                return false;

            case "waitfor":
                inst.WaitForPattern  = rest;
                inst.WaitForIsRegex  = false;
                inst.WaitForDeadline = DateTime.MaxValue;
                DbgEcho(inst, 2, $"waitfor \"{rest}\"");
                return false;

            case "waitforre":
                inst.WaitForPattern  = rest;
                inst.WaitForIsRegex  = true;
                inst.WaitForDeadline = DateTime.MaxValue;
                DbgEcho(inst, 2, $"waitforre \"{rest}\"");
                return false;

            case "waiteval":
                // waiteval <expression> — block until expression evaluates true.
                // The expression is stored raw (not substituted) so live
                // variable state is re-read on each evaluation.
                inst.WaitEvalExpr     = rest;
                inst.WaitEvalDeadline = DateTime.MaxValue;
                DbgEcho(inst, 2, $"waiteval {rest}");
                return false;

            case "debug":
            {
                var level = rest.Trim();
                if (int.TryParse(level, NumberStyles.Integer, CultureInfo.InvariantCulture, out var dl))
                    inst.DebugLevel = dl;
                else
                    inst.DebugLevel = 0;
                _echo($"[script] {inst.Name} debug level set to {inst.DebugLevel}");
                return true;
            }

            case "shift":
                // Shifts %1→drop, %2→%1, etc. — for walking command-line args.
                for (int k = 1; k <= 9; k++)
                {
                    if (inst.Vars.TryGetValue((k + 1).ToString(), out var nv))
                        inst.Vars[k.ToString()] = nv;
                    else
                        inst.Vars.Remove(k.ToString());
                }
                return true;

            case "var":
            case "setvariable":
            {
                var (vn, vv) = SplitCmd(rest);
                inst.Vars[vn.Trim()] = vv;
                DbgEcho(inst, 4, $"var {vn.Trim()} = \"{vv}\"");
                return true;
            }

            case "unvar":
            case "deletevariable":
                DbgEcho(inst, 4, $"unvar {rest.Trim()}");
                inst.Vars.Remove(rest.Trim());
                return true;

            case "random":
            {
                // random <min> <max>  → picks an integer in [min, max] (inclusive)
                // and stores it in %r, matching Genie3/4 semantics.
                var (aStr, bStr) = SplitCmd(rest);
                if (!int.TryParse(aStr.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var lo) ||
                    !int.TryParse(bStr.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var hi))
                {
                    _echo($"[script] {inst.Name}:{lineNo} random: usage 'random <min> <max>'");
                    return true;
                }
                if (hi < lo) (lo, hi) = (hi, lo);
                inst.Vars["r"] = inst.Rng.Next(lo, hi + 1)
                    .ToString(CultureInfo.InvariantCulture);
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
                var resultStr = result == Math.Floor(result) && !double.IsInfinity(result)
                    ? ((long)result).ToString(CultureInfo.InvariantCulture)
                    : result.ToString("0.################", CultureInfo.InvariantCulture);
                inst.Vars[vn] = resultStr;
                DbgEcho(inst, 4, $"math {vn} {op} {arg.Trim()} → {resultStr}");
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
                DbgEcho(inst, 4, $"{lower} {vn.Trim()} = \"{inst.Vars.GetValueOrDefault(vn.Trim(), "")}\" (expr: {expr})");
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
                // #echo [>Window] [#RRGGBB] message
                string? window = null;
                string? color  = null;
                var msg = rest;
                while (msg.Length > 0)
                {
                    var (tok, after) = SplitCmd(msg);
                    if (tok.Length > 0 && tok[0] == '>')
                    { window = tok[1..]; msg = after; continue; }
                    if (tok.Length > 0 && tok[0] == '#' && tok.Length >= 4)
                    { color = tok; msg = after; continue; }
                    break;
                }
                if ((window != null || color != null) && EchoTo != null)
                    EchoTo(msg, window, color);
                else
                    _echo(msg);
                return;
            }
            //case "#goto":
                    //Extensions.DispatchCommand(rest);
                    //_sendCommand(rest);
                //return;
            case "#mapper":
                // No-op for now; mapper reset is informational only.
                return;
            case "#parse":
                // Inject fake game text — feeds match/action/waitfor pipelines
                // as if the server had emitted it.
                OnGameLine(rest);
                return;
            default:
                // Forward unhandled # commands (e.g. #goto, #script) to the
                // host so the UI / mapper can process them.
                _handleHashCmd?.Invoke(text);
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

        // 'action X when eval <expr>' — eval form. Fires each tick where expr
        // transitions from false → true (rising edge), so repeated matches on
        // a persistent truthy condition don't spam.
        bool isEval = false;
        if (!isRegex && pattern.StartsWith("eval ", StringComparison.OrdinalIgnoreCase))
        {
            isEval  = true;
            pattern = pattern[5..].Trim();
        }

        inst.Actions.Add(new ScriptAction
        {
            Label   = label,
            Command = cmd,
            Pattern = pattern,
            IsRegex = isRegex,
            IsEval  = isEval,
            Enabled = true,
        });
        DbgEcho(inst, 5, $"action registered: cmd=\"{cmd}\" " +
            (isEval ? "when eval" : isRegex ? "whenre" : "when") +
            $" \"{pattern}\"" + (label.Length > 0 ? $" label=({label})" : ""));
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

    // ── Debug helper ───────────────────────────────────────────────────────

    /// <summary>
    /// Emit a debug trace line if the script's debug level is at or above
    /// <paramref name="minLevel"/>. Levels: 1=goto/gosub/return,
    /// 2=pause/wait, 3=if, 4=var/math, 5=actions, 10=all lines.
    /// </summary>
    private void DbgEcho(ScriptInstance inst, int minLevel, string msg)
    {
        if (inst.DebugLevel >= minLevel)
            _echo($"[dbg:{inst.DebugLevel}] {msg}");
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
            // `%%name` / `$$name` forces a second lookup: first fetch the
            // named var's value, then use that value as a variable name for
            // a second lookup. Matches Genie4's double-evaluation semantics.
            bool doubleEval = false;
            int nameStart = i + 1;
            if (i + 1 < text.Length && text[i + 1] == c)
            { doubleEval = true; nameStart = i + 2; }
            int j = nameStart;
            // Allow letters, digits, _ and . in variable names so identifiers
            // like Athletics.Ranks resolve as a single token.
            while (j < text.Length &&
                   (char.IsLetterOrDigit(text[j]) || text[j] == '_' || text[j] == '.'))
                j++;
            if (j == nameStart) { sb.Append(c); continue; }
            var name = text[nameStart..j];
            string value;
            // Pseudo-variables: computed on each substitution rather than stored.
            if (name.Equals("timer", StringComparison.OrdinalIgnoreCase))
            {
                value = inst.TimerStart is { } t
                    ? ((int)(DateTime.UtcNow - t).TotalSeconds).ToString(CultureInfo.InvariantCulture)
                    : "0";
            }
            else if (c == '$')
            {
                // $0..$9 are script-local (gosub args / regex captures) —
                // check inst.Vars first before falling through to Globals.
                if (inst.Vars.TryGetValue(name, out var sv) && !string.IsNullOrEmpty(sv))
                    value = sv;
                else
                    value = Globals.TryGetValue(name, out var gv) ? gv : string.Empty;
            }
            else
                value = inst.Vars.TryGetValue(name, out var lv) ? lv : string.Empty;
            if (doubleEval && !string.IsNullOrEmpty(value))
            {
                // Use the fetched value as the key for a second lookup,
                // pulling from the same scope as the original sigil.
                value = c == '$'
                    ? (Globals.TryGetValue(value, out var g2) ? g2 : string.Empty)
                    : (inst.Vars.TryGetValue(value, out var l2) ? l2 : string.Empty);
            }
            // Array indexing: %Bags(0) splits the pipe-delimited value and
            // returns the element at that index (0-based). The index itself
            // may already have been substituted (e.g. %Bags(%BagLoop)).
            if (j < text.Length && text[j] == '(')
            {
                int close = text.IndexOf(')', j + 1);
                if (close > j)
                {
                    var idxRaw = text[(j + 1)..close].Trim();
                    // The index may itself contain %vars (e.g. %Bags(%BagLoop)),
                    // so substitute before parsing as an integer.
                    var idxStr = SubstituteVars(idxRaw, inst);
                    if (int.TryParse(idxStr, NumberStyles.Integer,
                                     CultureInfo.InvariantCulture, out var arrIdx))
                    {
                        var parts = value.Split('|');
                        value = arrIdx >= 0 && arrIdx < parts.Length
                            ? parts[arrIdx]
                            : string.Empty;
                    }
                    j = close + 1;
                }
            }
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
