using System.Text.RegularExpressions;

namespace Genie4.Core.Extensions.Builtin;

/// <summary>
/// Watches the output of the DragonRealms <c>info</c> command and exposes
/// character identity globals: <c>$charname</c>, <c>$guild</c>, <c>$race</c>,
/// <c>$gender</c>, <c>$age</c>, <c>$circle</c>.
/// </summary>
public sealed class InfoTrackerExtension : IGameExtension
{
    public string Name        => "InfoTracker";
    public string Version     => "1.0";
    public string Description => "Tracks DragonRealms 'info' output and exposes character identity globals.";
    public bool   Enabled     { get; set; } = true;

    private IExtensionHost _host = null!;

    private static readonly Regex Field = new(
        @"(?<key>Name|Guild|Race|Gender|Age|Circle)\s*:\s*(?<val>[^:]*?)(?=\s{2,}\w+\s*:|$)",
        RegexOptions.Compiled);

    public void Initialize(IExtensionHost host) { _host = host; }
    public void OnCommandSent(string command) { }
    public void OnPrompt()                    { }
    public void Shutdown()                    { }

    public void OnGameLine(string line)
    {
        if (string.IsNullOrEmpty(line)) return;
        if (line.IndexOf(':') < 0) return;

        foreach (Match m in Field.Matches(line))
        {
            var key = m.Groups["key"].Value;
            var val = m.Groups["val"].Value.Trim();
            if (val.Length == 0) continue;

            switch (key)
            {
                case "Name":   _host.Globals["charname"] = val; break;
                case "Guild":  _host.Globals["guild"]    = val; break;
                case "Race":   _host.Globals["race"]     = val; break;
                case "Gender": _host.Globals["gender"]   = val; break;
                case "Age":    _host.Globals["age"]      = val; break;
                case "Circle": _host.Globals["circle"]   = val; break;
            }
        }
    }
}
