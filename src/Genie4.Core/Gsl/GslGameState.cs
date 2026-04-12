namespace Genie4.Core.Gsl;

/// <summary>
/// Mutable game state derived from parsed GSL events.
/// Updated on the UI thread by the line pipeline.
/// Consumers (vitals panel, roundtime bar, status strip) read from this.
/// </summary>
public sealed class GslGameState
{
    // ── Time ────────────────────────────────────────────────────────────────

    /// Unix epoch when roundtime expires (0 = no roundtime).
    public int RoundTimeEpoch  { get; private set; }

    /// Unix epoch when cast time expires (0 = none).
    public int CastTimeEpoch   { get; private set; }

    /// Last game clock value from <prompt time="..."/>.
    public int GameTime        { get; private set; }

    /// Remaining roundtime in seconds (computed each prompt).
    public int RoundTimeRemaining { get; private set; }

    // ── Vitals (0-100) ──────────────────────────────────────────────────────

    public int Health        { get; private set; } = -1;
    public int Mana          { get; private set; } = -1;
    public int Stamina       { get; private set; } = -1;
    public int Spirit        { get; private set; } = -1;
    public int Concentration { get; private set; } = -1;

    // ── Spell ────────────────────────────────────────────────────────────────

    public string PreparedSpell { get; private set; } = string.Empty;

    // ── Status indicators ────────────────────────────────────────────────────

    public bool Standing  { get; private set; }
    public bool Sitting   { get; private set; }
    public bool Kneeling  { get; private set; }
    public bool Prone     { get; private set; }
    public bool Stunned   { get; private set; }
    public bool Hidden    { get; private set; }
    public bool Invisible { get; private set; }
    public bool Dead      { get; private set; }
    public bool Webbed    { get; private set; }
    public bool Joined    { get; private set; }
    public bool Bleeding  { get; private set; }
    public bool Poisoned  { get; private set; }
    public bool Diseased  { get; private set; }

    // ── Navigation ───────────────────────────────────────────────────────────

    /// Server-assigned unique room ID from <c>&lt;nav rm="..."/&gt;</c>.
    public string ServerRoomId { get; private set; } = string.Empty;

    public IReadOnlyList<string> Exits { get; private set; } = [];

    // ── Room ─────────────────────────────────────────────────────────────────

    public string RoomTitle       { get; private set; } = string.Empty;
    public string RoomDescription { get; private set; } = string.Empty;
    public string RoomObjects     { get; private set; } = string.Empty;
    public string RoomPlayers     { get; private set; } = string.Empty;

    // ── Hands ────────────────────────────────────────────────────────────────

    public string LeftHand  { get; private set; } = string.Empty;
    public string RightHand { get; private set; } = string.Empty;

    // ── Output routing ───────────────────────────────────────────────────────

    /// Current pushStream target ("" = main output).
    public string CurrentStream { get; private set; } = string.Empty;

    public bool MonoOutput { get; private set; }

    // ── Events ───────────────────────────────────────────────────────────────

    /// Fired whenever any state changes. Consumers use this to refresh UI.
    public event Action? StateChanged;

    // ── Apply ────────────────────────────────────────────────────────────────

    /// Apply a batch of events emitted by GslParser for one line.
    public void Apply(IReadOnlyList<GslEvent> events)
    {
        if (events.Count == 0) return;

        foreach (var ev in events)
        {
            switch (ev)
            {
                case RoundTimeEvent e:
                    RoundTimeEpoch = e.UnixEpoch;
                    break;

                case CastTimeEvent e:
                    CastTimeEpoch = e.UnixEpoch;
                    break;

                case PromptEvent e:
                    GameTime = e.GameTime;
                    RoundTimeRemaining = RoundTimeEpoch > 0
                        ? Math.Max(0, RoundTimeEpoch - GameTime)
                        : 0;
                    if (RoundTimeRemaining == 0) RoundTimeEpoch = 0;
                    break;

                case SpellEvent e:
                    PreparedSpell = e.SpellName == "None" ? string.Empty : e.SpellName;
                    break;

                case IndicatorEvent e:
                    ApplyIndicator(e.Id, e.Visible);
                    break;

                case CompassEvent e:
                    Exits = e.Exits;
                    break;

                case NavEvent e:
                    ServerRoomId = e.RoomId;
                    break;

                case PushStreamEvent e:
                    CurrentStream = e.StreamId;
                    break;

                case PopStreamEvent:
                    CurrentStream = string.Empty;
                    break;

                case OutputModeEvent e:
                    MonoOutput = e.Mono;
                    break;

                case RoomTitleEvent e:
                    RoomTitle = e.Title;
                    break;

                case ComponentEvent e:
                    switch (e.Id)
                    {
                        case "room desc":    RoomDescription = e.Text; break;
                        case "room objs":    RoomObjects     = e.Text; break;
                        case "room players": RoomPlayers     = e.Text; break;
                        case "lhand":        LeftHand        = e.Text; break;
                        case "rhand":        RightHand       = e.Text; break;
                    }
                    break;

                case PresetEvent e when e.PresetId == "roomdesc":
                    if (!string.IsNullOrWhiteSpace(e.Text))
                        RoomDescription = e.Text;
                    break;

                case VitalsEvent e:
                    switch (e.Id)
                    {
                        case "health":        Health        = e.Value; break;
                        case "mana":          Mana          = e.Value; break;
                        case "stamina":       Stamina       = e.Value; break;
                        case "spirit":        Spirit        = e.Value; break;
                        case "concentration": Concentration = e.Value; break;
                    }
                    break;
            }
        }

        StateChanged?.Invoke();
    }

    private void ApplyIndicator(string id, bool active)
    {
        switch (id)
        {
            case "IconSTANDING":  Standing  = active; break;
            case "IconSITTING":   Sitting   = active; break;
            case "IconKNEELING":  Kneeling  = active; break;
            case "IconPRONE":     Prone     = active; break;
            case "IconSTUNNED":   Stunned   = active; break;
            case "IconHIDDEN":    Hidden    = active; break;
            case "IconINVISIBLE": Invisible = active; break;
            case "IconDEAD":      Dead      = active; break;
            case "IconWEBBED":    Webbed    = active; break;
            case "IconJOINED":    Joined    = active; break;
            case "IconBLEEDING":  Bleeding  = active; break;
            case "IconPOISONED":  Poisoned  = active; break;
            case "IconDISEASED":  Diseased  = active; break;
        }
    }
}
