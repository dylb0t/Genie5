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
    private int      _inFlight;
    private string?  _lastSentMove;
    private DispatcherTimer? _retryTimer;

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

    /// <summary>
    /// Inspect each line of game text for type-ahead errors. Wire this to the
    /// network LineReceived path.
    /// </summary>
    public void OnGameLine(string line)
    {
        if (string.IsNullOrEmpty(line)) return;

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

        // Requeue the command that was just rejected (server didn't run it)
        if (_lastSentMove != null)
            _pathQueue.AddFirst(_lastSentMove);

        // Reset in-flight to the new limit (the server has accepted that many
        // already; the rejection means we tried to send one more).
        _inFlight = TypeAheadLimit;
        _lastSentMove = null;

        ScheduleResume(TimeSpan.FromSeconds(1.0));
    }

    /// <summary>
    /// Called whenever the parser sees a server prompt. One prompt indicates
    /// the server has finished processing one previously-sent command.
    /// </summary>
    public void OnPrompt()
    {
        if (!_walking) return;
        if (_inFlight > 0) _inFlight--;

        if (_engine.CurrentNode != null && _walkDestination != null &&
            _engine.CurrentNode.Id == _walkDestination.Id)
        {
            _appendOutput("[mapper] Arrived.");
            StopWalk();
            return;
        }

        Pump();
    }

    /// <summary>Send queued moves until we reach the type-ahead limit.</summary>
    private void Pump()
    {
        while (_walking && _inFlight < TypeAheadLimit && _pathQueue.Count > 0)
        {
            var move = _pathQueue.First!.Value;
            _pathQueue.RemoveFirst();
            _lastSentMove = move;
            _inFlight++;
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
                _appendOutput("[mapper] Arrived.");
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
            StopWalk();
            return;
        }

        if (current.Id == _walkDestination.Id)
        {
            _appendOutput("[mapper] Arrived.");
            StopWalk();
            return;
        }

        var path = _engine.FindPath(current, _walkDestination);
        if (path is null || path.Count == 0)
        {
            _appendOutput($"[mapper] No path to \"{_walkDestination.Title}\" from current location.");
            StopWalk();
            return;
        }

        _pathQueue.Clear();
        foreach (var move in path) _pathQueue.AddLast(move);
        Pump();
    }

    private void StopWalk()
    {
        _walking         = false;
        _walkDestination = null;
        _lastSentMove    = null;
        _inFlight        = 0;
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
            return true;
        }

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
        var title = _gameState.RoomTitle;
        if (string.IsNullOrWhiteSpace(title)) return false;
        if (!Directory.Exists(_mapsDir)) return false;

        foreach (var file in Directory.GetFiles(_mapsDir, "*.json"))
        {
            var zone = _repo.Load(file);
            if (zone is null) continue;
            if (!zone.Nodes.Values.Any(n => string.Equals(n.Title, title, StringComparison.OrdinalIgnoreCase)))
                continue;

            _engine.LoadZone(zone);
            CurrentZonePath = file;
            ZoneChanged?.Invoke();
            _appendOutput($"[mapper] Loaded zone \"{zone.Name}\" containing current room, \"{title}\".");
            return _engine.CurrentNode is not null;
        }
        return false;
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
