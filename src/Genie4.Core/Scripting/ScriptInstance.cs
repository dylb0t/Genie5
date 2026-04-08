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
}
