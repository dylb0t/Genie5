namespace Genie4.Core.Scripting;

public enum PauseMode { None, Pause, Wait, Delay }

public sealed record ScriptLine(int LineNumber, string Origin, string Raw, string Trimmed, int Indent);

public sealed class ScriptInstance
{
    public string Name = string.Empty;
    public List<ScriptLine> Lines = new();
    public Dictionary<string, int> Labels = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> Vars = new(StringComparer.OrdinalIgnoreCase);
    public Stack<int> GosubStack = new();

    // $0..$9 are a SEPARATE scope from %0..%9. They hold gosub arguments or
    // the most recent regex capture groups, and are pushed/popped with the
    // gosub stack — unlike script args (%N), which live in Vars for the
    // entire script lifetime. Matches Genie4's per-frame ArgList semantics.
    public Stack<string[]> DollarStack = new();

    // if-block jump tables (built at parse time)
    public Dictionary<int, int> IfFalseJump = new(); // if-line idx → target when condition false
    public Dictionary<int, int> ElseJump   = new(); // else-line idx → target after else block

    public int  Pc;
    public bool Running = true;

    /// <summary>Debug verbosity: 0=off, 1=goto/gosub/return, 2=+pause/wait,
    /// 3=+if, 4=+var/math, 5=+actions, 10=all rows.</summary>
    public int  DebugLevel;

    // Pause / sleep state
    // PauseMode distinguishes the three blocking commands:
    //   None  — not paused
    //   Pause — blocks for duration AND until roundtime resolves (whichever is last)
    //   Wait  — blocks until next game prompt AND until roundtime resolves
    //   Delay — blocks for duration only (ignores roundtime and prompts)
    public PauseMode PauseMode;
    public bool      Paused;
    public DateTime  PauseUntil = DateTime.MinValue;

    // match / matchwait state
    public bool     InMatchWait;
    public DateTime MatchWaitDeadline = DateTime.MaxValue;
    public List<(string Label, string Pattern, bool IsRegex)> PendingMatches = new();

    // waitfor / waitforre state
    public string?  WaitForPattern;
    public bool     WaitForIsRegex;
    public DateTime WaitForDeadline = DateTime.MaxValue;

    // waiteval state — block until expression becomes true
    public string?  WaitEvalExpr;
    public DateTime WaitEvalDeadline = DateTime.MaxValue;

    public bool IsBlocked => Paused || InMatchWait || WaitForPattern != null || WaitEvalExpr != null;

    /// <summary>User-initiated pause from the script bar. While true the
    /// tick loop skips this instance entirely.</summary>
    public bool UserPaused;

    // action triggers — persistent, fire whenever a matching line arrives
    public List<ScriptAction> Actions = new();
    public bool ActionsEnabled = true;

    // Pending sends from a single put/send statement that contained multiple
    // semicolon-separated commands; drained one-per-tick respecting type-ahead.
    public Queue<string> PendingSends = new();

    // 'timer start' baseline for the %timer pseudo-variable.
    public DateTime? TimerStart;

    // Shared RNG for the 'random' command.
    public Random Rng = new();
}

public sealed class ScriptAction
{
    public string Label    = string.Empty; // Genie 'action (label) ...' name; "" = anonymous
    public string Pattern  = string.Empty;
    public string Command  = string.Empty; // statement to run on match (script-level, not raw send)
    public bool   IsRegex;
    public bool   IsEval;                  // 'when eval <expr>' form — Pattern holds the expression
    public bool   Enabled  = true;
    public bool   LastEvalResult;          // used to detect rising-edge for when-eval actions
}
