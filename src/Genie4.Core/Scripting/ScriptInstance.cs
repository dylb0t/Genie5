namespace Genie4.Core.Scripting;

public sealed record ScriptLine(int LineNumber, string Origin, string Raw, string Trimmed, int Indent);

public sealed class ScriptInstance
{
    public string Name = string.Empty;
    public List<ScriptLine> Lines = new();
    public Dictionary<string, int> Labels = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> Vars = new(StringComparer.OrdinalIgnoreCase);
    public Stack<int> GosubStack = new();

    // if-block jump tables (built at parse time)
    public Dictionary<int, int> IfFalseJump = new(); // if-line idx → target when condition false
    public Dictionary<int, int> ElseJump   = new(); // else-line idx → target after else block

    public int  Pc;
    public bool Running = true;

    // Pause / sleep state
    public bool     Paused;
    public DateTime PauseUntil = DateTime.MinValue;

    // match / matchwait state
    public bool     InMatchWait;
    public DateTime MatchWaitDeadline = DateTime.MaxValue;
    public List<(string Label, string Pattern, bool IsRegex)> PendingMatches = new();

    // waitfor / waitforre state
    public string?  WaitForPattern;
    public bool     WaitForIsRegex;
    public DateTime WaitForDeadline = DateTime.MaxValue;

    public bool IsBlocked => Paused || InMatchWait || WaitForPattern != null;

    // action triggers — persistent, fire whenever a matching line arrives
    public List<ScriptAction> Actions = new();
    public bool ActionsEnabled = true;

    // Pending sends from a single put/send statement that contained multiple
    // semicolon-separated commands; drained one-per-tick respecting type-ahead.
    public Queue<string> PendingSends = new();

    // 'timer start' baseline for the %timer pseudo-variable.
    public DateTime? TimerStart;
}

public sealed class ScriptAction
{
    public string Label    = string.Empty; // Genie 'action (label) ...' name; "" = anonymous
    public string Pattern  = string.Empty;
    public string Command  = string.Empty; // statement to run on match (script-level, not raw send)
    public bool   IsRegex;
    public bool   Enabled  = true;
}
