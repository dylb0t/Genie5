using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Threading;
using Genie4.Core.Gsl;
using Genie4.Core.Mapper;
using Genie4.Core.Scripting;

namespace Genie5.Ui;

/// <summary>
/// Glues the AutoMapperEngine, on-disk zone files, the floating MapWindow, and
/// the user-facing #goto command together. Owned by MainWindow but contains
/// no UI dependencies beyond the callbacks it's handed.
/// </summary>
public sealed class MapperController
{
    private readonly AutoMapperEngine  _engine;
    private readonly MapZoneRepository _repo;
    private readonly GslGameState      _gameState;
    private readonly Action<string>    _appendOutput;
    private readonly Action<string>    _sendCommand;
    private readonly string            _mapsDir;
    private readonly TypeAheadSession  _typeAhead;

    // ── In-memory zone index (built once at startup) ──────────────────────
    // Avoids reading 92 JSON files from disk on every zone miss.
    private sealed class ZoneIndexEntry
    {
        public string FilePath = string.Empty;
        public string ZoneName = string.Empty;
        // title → list of (description prefix, connected-exit count)
        public Dictionary<string, List<(string DescPrefix, int ConnectedExits)>> NodesByTitle = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> ServerRoomIds = new(StringComparer.OrdinalIgnoreCase);
    }
    private List<ZoneIndexEntry>? _zoneIndex;

    /// <summary>
    /// Emits a synthetic game line that flows through the script engine's
    /// match/waitfor pipeline, just like server text. Set by MainWindow
    /// after construction.
    /// </summary>
    public Action<string>? EmitGameLine { get; set; }

    /// <summary>
    /// Launches a script by name with arguments, e.g. ("crossingtrainerfix", ["go", "haberdashery"]).
    /// Set by MainWindow after construction.
    /// </summary>
    public Action<string, IReadOnlyList<string>>? RunScript { get; set; }

    /// <summary>When true, #goto launches a script instead of the built-in walk engine.</summary>
    public bool UseScriptForGoto { get; set; }

    /// <summary>Script name to run for #goto walks (default "automapper").</summary>
    public string GotoScriptName { get; set; } = "automapper";

    public MapperController(
        AutoMapperEngine engine,
        MapZoneRepository repo,
        GslGameState gameState,
        string mapsDir,
        Action<string> appendOutput,
        Action<string> sendCommand,
        TypeAheadSession typeAhead)
    {
        _engine       = engine;
        _repo         = repo;
        _gameState    = gameState;
        _mapsDir      = mapsDir;
        _appendOutput = appendOutput;
        _sendCommand  = sendCommand;
        _typeAhead    = typeAhead;

        // When the engine can't find the current room in the active zone,
        // scan other imported zones and auto-switch if one contains it.
        _engine.RoomNotFoundInZone += OnRoomNotFoundInZone;

        BuildZoneIndex();
    }

    /// <summary>
    /// Build a lightweight in-memory index of every zone file's node titles,
    /// descriptions, and server room IDs. Called once at startup so that
    /// zone-switching is a dictionary lookup, not 92 file reads.
    /// </summary>
    public void BuildZoneIndex()
    {
        _zoneIndex = new List<ZoneIndexEntry>();
        if (!Directory.Exists(_mapsDir)) return;

        foreach (var file in Directory.GetFiles(_mapsDir, "*.json"))
        {
            var zone = _repo.Load(file);
            if (zone is null) continue;

            var entry = new ZoneIndexEntry
            {
                FilePath = file,
                ZoneName = zone.Name,
            };

            foreach (var n in zone.Nodes.Values)
            {
                if (!entry.NodesByTitle.TryGetValue(n.Title, out var list))
                {
                    list = new List<(string, int)>();
                    entry.NodesByTitle[n.Title] = list;
                }
                var descPrefix = string.IsNullOrEmpty(n.Description)
                    ? string.Empty
                    : n.Description[..Math.Min(n.Description.Length, 80)];
                int connected = n.Exits.Count(e => e.DestinationId.HasValue);
                list.Add((descPrefix, connected));

                if (!string.IsNullOrEmpty(n.ServerRoomId))
                    entry.ServerRoomIds.Add(n.ServerRoomId);
            }

            _zoneIndex.Add(entry);
        }
    }

    private bool   _autoLoadingZone;
    private string _lastAutoLoadTitle = string.Empty;
    private string _lastAutoLoadDesc  = string.Empty;

    private void OnRoomNotFoundInZone()
    {
        // Guard against reentrancy: LoadZone → Recalculate → OnStateChanged
        // could fire RoomNotFoundInZone again if the newly loaded zone also
        // doesn't contain the room.
        if (_autoLoadingZone) return;

        // Don't re-scan disk if we already tried for this exact room.
        // Reset when the room actually changes (title + description differ).
        var title = _gameState.RoomTitle;
        var desc  = _gameState.RoomDescription;
        if (title == _lastAutoLoadTitle && desc == _lastAutoLoadDesc) return;
        _lastAutoLoadTitle = title;
        _lastAutoLoadDesc  = desc;

        DebugLog($"room not in zone, searching index for \"{title}\"");
        _autoLoadingZone = true;
        try { TryAutoLoadZoneForCurrentRoom(); }
        finally { _autoLoadingZone = false; }
    }

    // ── Walk state machine ─────────────────────────────────────────────────
    //
    // Walks are driven by a queue of move commands and a counter of in-flight
    // sends. We send up to TypeAheadLimit commands at once, then wait for
    // server prompts (one prompt = one acknowledged command). If the server
    // ever rejects a send with "you may only type ahead N commands", we
    // capture N as the new limit, requeue the rejected command, briefly
    // wait, and continue.

    private readonly LinkedList<string> _pathQueue = new();
    private MapNode? _walkDestination;
    private bool     _walking;
    private bool     _scriptMode;        // true while an external script owns the walk
    private int      _inFlight;
    private int      _consecutiveFailures;
    private int      _lastArrivalNodeId = -1;
    private bool     _replanOnDrain;     // replan once all in-flight commands have drained

    private DispatcherTimer? _retryTimer;

    /// <summary>Max consecutive movement failures before the mapper stops and replans.</summary>
    private const int MaxConsecutiveFailures = 3;

    /// <summary>
    /// Returns the remaining roundtime in seconds (0 = not in RT). Set by the
    /// UI layer. The mapper holds off pumping moves until RT resolves.
    /// </summary>
    public Func<int>? RoundTimeRemaining { get; set; }

    /// <summary>
    /// Maximum number of moves that may be in-flight at once. Auto-calibrated
    /// downward when the server reports the type-ahead cap. Persists for the
    /// lifetime of the session.
    /// </summary>
    public int TypeAheadLimit
    {
        get => _typeAhead.Limit;
        private set => _typeAhead.Limit = value;
    }

    /// <summary>True while a #goto walk is in progress.</summary>
    public bool IsWalking => _walking;

    /// <summary>When enabled, echoes commands sent, room detection, zone switches, etc.</summary>
    public bool Debug { get; set; }

    private void DebugLog(string msg)
    {
        if (Debug) _appendOutput($"[mapper debug] {msg}");
    }

    /// <summary>
    /// Inspect each line of game text for type-ahead errors. Wire this to the
    /// network LineReceived path.
    /// </summary>
    public void OnGameLine(string line)
    {
        if (string.IsNullOrEmpty(line)) return;

        // Script mode: the running script owns movement, failure recovery, and
        // replanning. The built-in engine must stay out of the way or it will
        // queue its own commands and fight the script.
        if (_scriptMode) return;

        // Detect movement failures while walking.
        if (_walking && IsMovementFailure(line))
        {
            _consecutiveFailures++;
            if (_inFlight > 0) _inFlight--;
            DebugLog($"movement failure ({_consecutiveFailures}/{MaxConsecutiveFailures}): \"{line}\"");

            if (_consecutiveFailures >= MaxConsecutiveFailures)
            {
                _appendOutput($"[mapper] {_consecutiveFailures} consecutive movement failures — replanning.");
                _consecutiveFailures = 0;
                _pathQueue.Clear();
                _inFlight = 0;
                ReplanFromCurrent();
                return;
            }
        }

        // Match "Sorry, you may only type ahead N commands."
        var idx = line.IndexOf("you may only type ahead", StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return;

        // Pull the integer that follows
        int newLimit = TypeAheadLimit;
        var rest = line[(idx + "you may only type ahead".Length)..];
        var digits = new string(rest.SkipWhile(c => !char.IsDigit(c))
                                    .TakeWhile(char.IsDigit).ToArray());
        if (int.TryParse(digits, out var parsed) && parsed > 0)
            newLimit = parsed;

        if (newLimit != TypeAheadLimit)
        {
            TypeAheadLimit = newLimit;
            _appendOutput($"[mapper] Calibrated type-ahead limit to {TypeAheadLimit}.");
        }

        if (!_walking) return;

        // The server rejected commands beyond the type-ahead cap. We don't
        // know exactly which queued moves the server accepted vs. silently
        // dropped, so the safest recovery is to let all in-flight commands
        // settle and then replan from wherever we end up.
        DebugLog("type-ahead rejection — will replan once in-flight commands drain");
        _pathQueue.Clear();
        _replanOnDrain = true;
    }

    /// <summary>
    /// Called whenever the parser sees a server prompt. One prompt indicates
    /// the server has finished processing one previously-sent command.
    /// </summary>
    public void OnPrompt()
    {
        if (!_walking) return;
        // Script mode: arrival/failure are reported via OnAutoMapperScriptFinished.
        // The mapper never pumps its own moves, so prompt-driven bookkeeping
        // (in-flight, Pump, ReplanFromCurrent) must be skipped.
        if (_scriptMode) return;
        if (_inFlight > 0) _inFlight--;

        if (_engine.CurrentNode != null && _walkDestination != null &&
            _engine.CurrentNode.Id == _walkDestination.Id)
        {
            DebugLog($"arrived at node {_engine.CurrentNode.Id} \"{_engine.CurrentNode.Title}\"");
            EmitGameLine?.Invoke("YOU HAVE ARRIVED!");
            StopWalk();
            return;
        }

        // Reset failure counter when we actually moved to a different node.
        if (_engine.CurrentNode != null && _lastArrivalNodeId != _engine.CurrentNode.Id)
        {
            _lastArrivalNodeId = _engine.CurrentNode.Id;
            _consecutiveFailures = 0;
        }

        DebugLog($"prompt: in-flight now {_inFlight}, current node: {_engine.CurrentNode?.Id.ToString() ?? "null"} \"{_engine.CurrentNode?.Title ?? ""}\"");

        // After a type-ahead rejection we wait for all in-flight to drain,
        // then replan from wherever we ended up.
        if (_replanOnDrain && _inFlight == 0)
        {
            _replanOnDrain = false;
            DebugLog("in-flight drained — replanning from current room");
            ReplanFromCurrent();
            return;
        }

        Pump();
    }

    /// <summary>Send queued moves until we reach the type-ahead limit.</summary>
    private void Pump()
    {
        // Don't send moves during roundtime — wait the exact remaining seconds.
        var rtSecs = RoundTimeRemaining?.Invoke() ?? 0;
        if (rtSecs > 0)
        {
            DebugLog($"pump: waiting {rtSecs}s for roundtime to resolve");
            ScheduleResume(TimeSpan.FromSeconds(rtSecs + 0.2));
            return;
        }

        while (_walking && _inFlight < TypeAheadLimit && _pathQueue.Count > 0)
        {
            var move = _pathQueue.First!.Value;
            _pathQueue.RemoveFirst();


            // "script <name> [args...]" — launch a .cmd script instead of
            // sending a raw command to the game server.
            if (move.StartsWith("script ", StringComparison.OrdinalIgnoreCase))
            {
                var scriptParts = move[7..].Trim()
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (scriptParts.Length > 0 && RunScript != null)
                {
                    var scriptName = scriptParts[0];
                    var scriptArgs = scriptParts.Skip(1).ToArray();
                    DebugLog($"script: \"{scriptName}\" args: [{string.Join(", ", scriptArgs)}] (queued: {_pathQueue.Count})");
                    // Pause walking — the script will move us; we resume on
                    // arrival (the next CurrentNodeChanged that matches the
                    // destination, or when the script signals completion).
                    _inFlight++;
                    RunScript(scriptName, scriptArgs);
                    return; // Don't pump further commands while a script is running
                }
            }

            _inFlight++;
            DebugLog($"send: \"{move}\" (in-flight: {_inFlight}/{TypeAheadLimit}, queued: {_pathQueue.Count})");
            _sendCommand(move);
        }

        if (_walking && _pathQueue.Count == 0 && _inFlight == 0)
        {
            // Path exhausted but we haven't seen the destination room update —
            // either we got there but the engine couldn't fingerprint it, or
            // the path took us somewhere unexpected. Try a re-plan once.
            if (_engine.CurrentNode != null && _walkDestination != null &&
                _engine.CurrentNode.Id == _walkDestination.Id)
            {
                _appendOutput("YOU HAVE ARRIVED!");
                StopWalk();
            }
            else
            {
                ReplanFromCurrent();
            }
        }
    }

    private void ScheduleResume(TimeSpan delay)
    {
        _retryTimer?.Stop();
        _retryTimer = new DispatcherTimer { Interval = delay };
        _retryTimer.Tick += (_, _) =>
        {
            _retryTimer!.Stop();
            _retryTimer = null;
            // After waiting, the previously-acknowledged commands have all
            // resolved; reset in-flight and resume.
            _inFlight = 0;
            Pump();
        };
        _retryTimer.Start();
    }

    private void ReplanFromCurrent()
    {
        if (_walkDestination is null) { StopWalk(); return; }

        _engine.Recalculate();
        var current = _engine.CurrentNode;
        if (current is null)
        {
            _appendOutput("[mapper] Lost track of current location while walking. Stopping.");
            EmitGameLine?.Invoke("MOVEMENT FAILED");
            StopWalk();
            return;
        }

        if (current.Id == _walkDestination.Id)
        {
            EmitGameLine?.Invoke("YOU HAVE ARRIVED!");
            StopWalk();
            return;
        }

        var path = _engine.FindPath(current, _walkDestination);
        if (path is null || path.Count == 0)
        {
            _appendOutput($"[mapper] No path to \"{_walkDestination.Title}\" from current location.");
            EmitGameLine?.Invoke("MOVEMENT FAILED");
            StopWalk();
            return;
        }

        if (_scriptMode && RunScript != null)
        {
            // Hand the fresh path back to the script and stay out of the way.
            var args = path.ToArray();
            _appendOutput($"[mapper] Replanning via {GotoScriptName}: {args.Length} moves");
            DebugLog($"script replan — relaunching {GotoScriptName} with {args.Length} moves");
            RunScript(GotoScriptName, args);
            return;
        }

        _pathQueue.Clear();
        foreach (var move in path) _pathQueue.AddLast(move);
        Pump();
    }

    private static bool IsMovementFailure(string line)
    {
        // Common DR server rejection messages when you can't move.
        return line.StartsWith("You can't go there", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith("You can't do that", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith("...wait", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith("Sorry, you may only", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith("You are still stunned", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith("You can't manage", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith("You are unable to move", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Called when the automapper script finishes. Checks if we arrived at
    /// the destination or need to signal failure.
    /// </summary>
    public void OnAutoMapperScriptFinished()
    {
        if (!_walking) return;

        if (_engine.CurrentNode != null && _walkDestination != null &&
            _engine.CurrentNode.Id == _walkDestination.Id)
        {
            DebugLog($"automapper script finished — arrived at node {_engine.CurrentNode.Id}");
            EmitGameLine?.Invoke("YOU HAVE ARRIVED!");
            StopWalk();
        }
        else
        {
            DebugLog("automapper script finished — not at destination, replanning");
            ReplanFromCurrent();
        }
    }

    public void CancelWalk()
    {
        if (!_walking) return;
        _appendOutput("[mapper] Walk cancelled.");
        EmitGameLine?.Invoke("MOVEMENT FAILED");
        StopWalk();
    }

    private void StopWalk()
    {
        _walking              = false;
        _scriptMode           = false;
        _walkDestination      = null;
        _inFlight             = 0;
        _consecutiveFailures  = 0;
        _replanOnDrain        = false;
        _pathQueue.Clear();
        _retryTimer?.Stop();
        _retryTimer = null;
    }

    private void StartWalk(MapNode destination, IReadOnlyList<string> path)
    {
        StopWalk();
        _walkDestination = destination;
        _walking         = true;
        _inFlight        = 0;

        // Script mode: hand the calculated moves off to the configured script
        // and back out entirely. The script owns movement, type-ahead, failure
        // recovery, and arrival detection from here until ScriptFinished.
        if (UseScriptForGoto && RunScript != null)
        {
            _scriptMode = true;
            var args = path.ToArray();
            _appendOutput($"[mapper] Launching {GotoScriptName} with {args.Length} moves: {string.Join(" ", args.Take(10))}{(args.Length > 10 ? "..." : "")}");
            DebugLog($"launching {GotoScriptName} script with {args.Length} moves");
            RunScript(GotoScriptName, args);
            return;
        }

        // Built-in walk engine
        foreach (var move in path) _pathQueue.AddLast(move);
        Pump();
    }

    /// <summary>Path of the currently loaded zone JSON, or empty.</summary>
    public string CurrentZonePath { get; private set; } = string.Empty;

    /// <summary>Raised whenever the active zone is replaced.</summary>
    public event Action? ZoneChanged;

    public void SetInitialZonePath(string path) => CurrentZonePath = path;

    public void SetCurrentZonePath(string path)
    {
        CurrentZonePath = path;
        ZoneChanged?.Invoke();
    }

    public void SaveActiveZone()
    {
        var path = string.IsNullOrEmpty(CurrentZonePath)
            ? Path.Combine(_mapsDir, "default.json")
            : CurrentZonePath;
        _repo.Save(path, _engine.ActiveZone);

        // Update the in-memory index for the saved zone so newly stamped
        // server room IDs are available for future lookups without a restart.
        if (_zoneIndex is not null)
        {
            var existing = _zoneIndex.Find(e =>
                string.Equals(e.FilePath, path, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                existing.ServerRoomIds.Clear();
                foreach (var n in _engine.ActiveZone.Nodes.Values)
                    if (!string.IsNullOrEmpty(n.ServerRoomId))
                        existing.ServerRoomIds.Add(n.ServerRoomId);
            }
        }
    }

    /// <summary>Import all .xml files in <paramref name="dir"/> as JSON zones.</summary>
    public int ImportGenie4Maps(string dir)
    {
        var zones = Genie4MapImporter.ImportDirectory(dir);
        foreach (var zone in zones)
            _repo.Save(Path.Combine(_mapsDir, zone.Name + ".json"), zone);
        return zones.Count;
    }

    /// <summary>
    /// Intercepts #goto / #go / #g / #walk / #walkto. Returns true if the
    /// input was handled (whether successfully or with an error message).
    /// </summary>
    public bool TryHandleGoto(string input)
    {
        var trimmed = input.TrimStart();
        if (trimmed.Length == 0 || trimmed[0] != '#') return false;

        var space = trimmed.IndexOf(' ');
        var name  = (space < 0 ? trimmed[1..] : trimmed[1..space]).ToLowerInvariant();
        var arg   = space < 0 ? string.Empty : trimmed[(space + 1)..].Trim();

        if (name == "mapper")
        {
            if (arg.Equals("debug", StringComparison.OrdinalIgnoreCase))
            {
                Debug = !Debug;
                _appendOutput($"[mapper] Debug {(Debug ? "ON" : "OFF")}");
                return true;
            }
            if (arg.StartsWith("debug ", StringComparison.OrdinalIgnoreCase))
            {
                var val = arg[6..].Trim();
                Debug = val is "1" or "on" or "true";
                _appendOutput($"[mapper] Debug {(Debug ? "ON" : "OFF")}");
                return true;
            }
            // Other #mapper subcommands (reset, etc.) — no-op for now.
            return true;
        }

        if (name is not ("goto" or "go" or "g" or "walk" or "walkto")) return false;

        if (string.IsNullOrEmpty(arg))
        {
            _appendOutput("[mapper] Goto - please specify a room id, label, or partial title to travel to.");
            return true;
        }

        var current = ResolveCurrentNode();
        if (current is null)
        {
            _appendOutput($"[mapper] Current location \"{_gameState.RoomTitle}\" not found in any imported map.");
            return true;
        }

        var target = FindTarget(arg);
        if (target is null)
        {
            _appendOutput($"[mapper] Destination \"{arg}\" not found.");
            EmitGameLine?.Invoke("DESTINATION NOT FOUND");
            return true;
        }
        if (target.Id == current.Id)
        {
            _appendOutput("[mapper] Already there.");
            return true;
        }

        var path = _engine.FindPath(current, target);
        if (path is null || path.Count == 0)
        {
            _appendOutput($"[mapper] No path to \"{target.Title}\".");
            EmitGameLine?.Invoke("MOVEMENT FAILED");
            return true;
        }

        DebugLog($"path: {string.Join(" → ", path)}");
        _appendOutput($"[mapper] Walking to \"{target.Title}\" ({path.Count} steps).");
        StartWalk(target, path);
        return true;
    }

    /// <summary>
    /// Try to identify the current node in the active zone, falling back to a
    /// scan of every imported zone for one containing the current room title.
    /// </summary>
    public MapNode? ResolveCurrentNode()
    {
        if (_engine.CurrentNode is { } node) return node;

        _engine.Recalculate();
        if (_engine.CurrentNode is { } afterRecalc) return afterRecalc;

        if (TryAutoLoadZoneForCurrentRoom())
            return _engine.CurrentNode;

        return null;
    }

    private bool TryAutoLoadZoneForCurrentRoom()
    {
        var title  = _gameState.RoomTitle;
        var desc   = _gameState.RoomDescription;
        var srvId  = _gameState.ServerRoomId;
        if (string.IsNullOrWhiteSpace(title)) return false;
        if (_zoneIndex is null || _zoneIndex.Count == 0) return false;

        // Score each candidate using the in-memory index (no disk I/O).
        //   100  = server room ID match (definitive)
        //   10+N = title+description match, N = connected exit count
        //   1+N  = title-only match, N = connected exit count
        ZoneIndexEntry? bestEntry = null;
        int bestScore = 0;

        foreach (var entry in _zoneIndex)
        {
            int score = 0;

            if (!string.IsNullOrEmpty(srvId) && entry.ServerRoomIds.Contains(srvId))
                score = 100;

            if (score == 0 && entry.NodesByTitle.TryGetValue(title, out var nodes))
            {
                foreach (var (descPrefix, connectedExits) in nodes)
                {
                    bool descMatch = !string.IsNullOrEmpty(desc) &&
                                     descPrefix.Length > 0 &&
                                     desc.StartsWith(descPrefix, StringComparison.OrdinalIgnoreCase);
                    int s = (descMatch ? 10 : 1) + connectedExits;
                    if (s > score) score = s;
                }
            }

            if (score > bestScore)
            {
                bestScore = score;
                bestEntry = entry;
                if (score >= 100) break;
            }
        }

        if (bestEntry is null) return false;

        // Don't reload the same zone we already have — just recalculate.
        if (string.Equals(bestEntry.FilePath, CurrentZonePath, StringComparison.OrdinalIgnoreCase))
        {
            _engine.Recalculate();
            return _engine.CurrentNode is not null;
        }

        // Only now read the full zone from disk.
        var zone = _repo.Load(bestEntry.FilePath);
        if (zone is null) return false;

        _engine.LoadZone(zone);
        CurrentZonePath = bestEntry.FilePath;
        ZoneChanged?.Invoke();
        _appendOutput($"[mapper] Loaded zone \"{zone.Name}\" containing current room, \"{title}\".");
        return _engine.CurrentNode is not null;
    }

    private MapNode? FindTarget(string arg)
    {
        var zone = _engine.ActiveZone;

        if (int.TryParse(arg, out int idArg) && zone.Nodes.TryGetValue(idArg, out var byId))
            return byId;

        bool NoteLabelMatches(string notes, Func<string, bool> pred)
            => !string.IsNullOrEmpty(notes) &&
               notes.Split('|').Any(label => pred(label.Trim()));

        return zone.Nodes.Values.FirstOrDefault(n =>
                   NoteLabelMatches(n.Notes, l => string.Equals(l, arg, StringComparison.OrdinalIgnoreCase)))
            ?? zone.Nodes.Values.FirstOrDefault(n =>
                   NoteLabelMatches(n.Notes, l => l.StartsWith(arg, StringComparison.OrdinalIgnoreCase)))
            ?? zone.Nodes.Values.FirstOrDefault(n => string.Equals(n.Title, arg, StringComparison.OrdinalIgnoreCase))
            ?? zone.Nodes.Values.FirstOrDefault(n => n.Title.StartsWith(arg, StringComparison.OrdinalIgnoreCase))
            ?? zone.Nodes.Values.FirstOrDefault(n => n.Title.Contains(arg, StringComparison.OrdinalIgnoreCase))
            ?? zone.Nodes.Values.FirstOrDefault(n => !string.IsNullOrEmpty(n.Notes) &&
                                                      n.Notes.Contains(arg, StringComparison.OrdinalIgnoreCase));
    }
}
