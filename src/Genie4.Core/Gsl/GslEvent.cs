namespace Genie4.Core.Gsl;

/// <summary>Base type for all events emitted by the GSL/XML parser.</summary>
public abstract record GslEvent;

/// <summary><roundTime value="unix_epoch"/></summary>
public sealed record RoundTimeEvent(int UnixEpoch) : GslEvent;

/// <summary><castTime value="unix_epoch"/></summary>
public sealed record CastTimeEvent(int UnixEpoch) : GslEvent;

/// <summary><prompt time="unix_epoch">text</prompt></summary>
public sealed record PromptEvent(string Text, int GameTime) : GslEvent;

/// <summary><spell>name</spell></summary>
public sealed record SpellEvent(string SpellName) : GslEvent;

/// <summary><indicator id="IconSTUNNED" visible="y"/></summary>
public sealed record IndicatorEvent(string Id, bool Visible) : GslEvent;

/// <summary>
/// Fired once per compass block with all available exits.
/// e.g. ["n","e","sw","out"]
/// </summary>
public sealed record CompassEvent(IReadOnlyList<string> Exits) : GslEvent;

/// <summary><pushStream id="thoughts"/> — route subsequent text to a named stream.</summary>
public sealed record PushStreamEvent(string StreamId) : GslEvent;

/// <summary><popStream/> — return output to main.</summary>
public sealed record PopStreamEvent : GslEvent;

/// <summary><output class="mono"/> or <output class=""/></summary>
public sealed record OutputModeEvent(bool Mono) : GslEvent;

/// <summary><pushBold/> — begin bold/creature region.</summary>
public sealed record PushBoldEvent : GslEvent;

/// <summary><popBold/> — end bold/creature region.</summary>
public sealed record PopBoldEvent : GslEvent;

/// <summary><preset id="roomDesc">text</preset> or whisper/thought presets.</summary>
public sealed record PresetEvent(string PresetId, string Text) : GslEvent;

/// <summary><streamWindow id="room" subtitle="..."/> — room title update.</summary>
public sealed record RoomTitleEvent(string Title) : GslEvent;

/// <summary><progressBar id="health" value="80" text="80"/> — vitals update (0-100).</summary>
public sealed record VitalsEvent(string Id, int Value) : GslEvent;

/// <summary><component id="room desc">...</component> — in-place component update.</summary>
public sealed record ComponentEvent(string Id, string Text) : GslEvent;

/// <summary><inv id="stow">a worn leather ...</inv> — container inventory line, route to inv stream.</summary>
public sealed record InvLineEvent(string ContainerId, string Text) : GslEvent;

/// <summary><clearContainer id="stow"/> — clear the inv stream window for a fresh listing.</summary>
public sealed record ClearContainerEvent(string ContainerId) : GslEvent;

/// <summary><nav rm="12345"/> — server-provided unique room ID.</summary>
public sealed record NavEvent(string RoomId) : GslEvent;
