using System.Globalization;
using System.Text.RegularExpressions;

namespace Genie4.Core.Extensions.Builtin;

/// <summary>
/// Native port of the Genie3/4 EXPTracker plugin
/// (https://code.google.com/archive/p/g3-standalone-xp/). Watches the output
/// of the DragonRealms <c>experience</c> command and writes session globals
/// scripts can read. The set of globals matches the original plugin so
/// existing user scripts (e.g. travel.cmd) work unchanged:
///
///   $&lt;Skill&gt;.Ranks            integer rank count, e.g. "1734"
///   $&lt;Skill&gt;.LearningRate     numeric mind-state level 0–34
///   $&lt;Skill&gt;.LearningRateName mind-state text, e.g. "mind lock"
///
///   $TDPs           total TDPs from "You currently have N Trait Points..."
///   $Sleeping       "y" / "n"  (from sleep/awaken indicator lines)
///   $Concentrating  "y" / "n"  (from "preparing a spell" / "you stop preparing")
///
/// Skill names are normalised: spaces are replaced with underscores so the
/// global is a single dotted token (Shield_Usage.Ranks etc.). The original
/// plugin did the same.
/// </summary>
public sealed class ExpTrackerExtension : IGameExtension
{
    public string Name        => "EXPTracker";
    public string Version     => "1.0";
    public string Description => "Tracks DragonRealms experience output and exposes per-skill globals.";
    public bool   Enabled     { get; set; } = true;

    private IExtensionHost _host = null!;

    // ── Mind state table — index = LearningRate value (0–34) ────────────────
    // Order matches Simutronics' DR mind-state progression. Lookups are
    // case-insensitive and tolerate the long-form names ("very focused",
    // "nearly locked"). Anything not in the table maps to -1.
    private static readonly string[] MindStates =
    {
        "clear",
        "dabbling",
        "perusing",
        "learning",
        "thoughtful",
        "thinking",
        "considering",
        "pondering",
        "ruminating",
        "concentrating",
        "attentive",
        "deliberative",
        "interested",
        "examining",
        "understanding",
        "absorbing",
        "studious",
        "focused",
        "very focused",
        "engaged",
        "very engaged",
        "cogitating",
        "fascinated",
        "captivated",
        "engrossed",
        "riveted",
        "very riveted",
        "rapt",
        "very rapt",
        "enthralled",
        "nearly locked",
        "mind lock",
        "mind lock",   // 32 — DR sometimes reports 32/34 with text "mind lock"
        "mind lock",   // 33
        "mind lock",   // 34
    };

    // Each skill block:  Name: <ranks> <pct>% <mindstate>  (n/34)
    // - Name allows spaces (we capture lazily up to ':')
    // - The (n/34) is optional — Genie3's brief format omits it
    private static readonly Regex SkillRegex = new(
        @"(?<name>[A-Z][A-Za-z][A-Za-z _'-]*?)\s*:\s*(?<ranks>\d+)\s+(?<pct>\d{1,3})%\s+(?<state>[a-z][a-z ]*?)(?=\s{2,}|\s*\(\s*\d+\s*/\s*34\s*\)|$)",
        RegexOptions.Compiled);

    // "You currently have 5 Trait Points (TPs) and 12 unused..." (DR varies the wording)
    private static readonly Regex TdpRegex = new(
        @"\b(?<n>\d+)\s+(?:Trait\s+Points|TDPs?|TPs?)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public void Initialize(IExtensionHost host) { _host = host; }
    public void OnCommandSent(string command) { }
    public void OnPrompt()                    { }
    public void Shutdown()                    { }

    public void OnGameLine(string line)
    {
        if (string.IsNullOrEmpty(line)) return;

        // EXP block lines must contain ':' and '%'.
        if (line.IndexOf(':') >= 0 && line.IndexOf('%') >= 0)
        {
            foreach (Match m in SkillRegex.Matches(line))
            {
                var rawName = m.Groups["name"].Value.Trim();
                if (rawName.Length < 3)               continue; // skip "Roundtime:" etc.
                if (LooksLikeNoiseField(rawName))     continue;

                var name      = rawName.Replace(' ', '_');
                var ranks     = m.Groups["ranks"].Value;
                var stateText = m.Groups["state"].Value.Trim();

                _host.Globals[name + ".Ranks"]            = ranks;
                _host.Globals[name + ".LearningRateName"] = stateText;
                _host.Globals[name + ".LearningRate"]     =
                    LookupMindStateValue(stateText).ToString(CultureInfo.InvariantCulture);
            }
        }

        // TDPs / Trait Points
        var tdp = TdpRegex.Match(line);
        if (tdp.Success && (line.IndexOf("Trait", StringComparison.OrdinalIgnoreCase) >= 0
                         || line.IndexOf("TDP",  StringComparison.OrdinalIgnoreCase) >= 0))
        {
            _host.Globals["TDPs"] = tdp.Groups["n"].Value;
        }

        // Sleeping / waking
        if (line.Contains("You go to sleep", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("You are sleeping",  StringComparison.OrdinalIgnoreCase))
            _host.Globals["Sleeping"] = "y";
        else if (line.Contains("You wake up",       StringComparison.OrdinalIgnoreCase) ||
                 line.Contains("You are no longer sleeping", StringComparison.OrdinalIgnoreCase))
            _host.Globals["Sleeping"] = "n";

        // Concentration / spell prep state
        if (line.Contains("You begin to chant", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("you start preparing", StringComparison.OrdinalIgnoreCase))
            _host.Globals["Concentrating"] = "y";
        else if (line.Contains("you let your concentration lapse", StringComparison.OrdinalIgnoreCase) ||
                 line.Contains("you stop chanting", StringComparison.OrdinalIgnoreCase))
            _host.Globals["Concentrating"] = "n";
    }

    /// <summary>
    /// Reject common false positives that match the skill regex shape but
    /// are not actually skill rows ("Time:", "Roundtime:", "Health:", etc.).
    /// </summary>
    private static bool LooksLikeNoiseField(string name) => name switch
    {
        "Time"              => true,
        "Roundtime"         => true,
        "RT"                => true,
        "Health"            => true,
        "Mana"              => true,
        "Stamina"           => true,
        "Spirit"            => true,
        "Concentration"     => true,
        _                    => false,
    };

    private static int LookupMindStateValue(string state)
    {
        if (string.IsNullOrEmpty(state)) return -1;
        for (int i = 0; i < MindStates.Length; i++)
        {
            if (MindStates[i].Equals(state, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return -1;
    }
}
