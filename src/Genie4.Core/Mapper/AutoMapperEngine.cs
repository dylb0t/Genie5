using Genie4.Core.Gsl;

namespace Genie4.Core.Mapper;

public sealed class AutoMapperEngine
{
    private MapZone _zone;
    private GslGameState? _state;

    // Fingerprint → NodeId for fast room matching
    private readonly Dictionary<string, int> _fingerprintIndex = new();

    // ServerRoomId → NodeId for exact room matching via <nav rm="..."/>
    private readonly Dictionary<string, int> _serverRoomIndex = new(StringComparer.OrdinalIgnoreCase);

    // State between room transitions
    private string  _lastTitle = string.Empty;
    private string  _lastExitKey = string.Empty; // sorted join used for change detection

    // The movement command that was sent before the last room change fired
    private Direction _pendingDirection = Direction.None;

    public MapNode?  CurrentNode  { get; private set; }
    public MapZone   ActiveZone   => _zone;
    public bool      IsEnabled    { get; set; } = false;

    public event Action? MapChanged;
    public event Action? CurrentNodeChanged;
    /// <summary>
    /// Raised when the engine can't find the current room in the active zone
    /// (lookup-only mode). The controller should try loading a different zone
    /// that contains the room.
    /// </summary>
    public event Action? RoomNotFoundInZone;

    public AutoMapperEngine(MapZone zone)
    {
        _zone = zone;
        RebuildIndex();
    }

    // ── Public API ──────────────────────────────────────────────────────────

    public void Attach(GslGameState state)
    {
        _state = state;
        state.StateChanged += OnStateChanged;
    }

    /// <summary>Called by the command intercept layer before each command reaches the server.</summary>
    public void OnCommandSent(string rawCommand)
    {
        if (!IsEnabled) return;

        // Strip leading/trailing whitespace; handle semicolon-separated commands
        // by looking at the first token only.
        var first = rawCommand.Split(';')[0].Trim();
        var dir   = DirectionHelper.Parse(first);
        if (dir != Direction.None)
            _pendingDirection = dir;
    }

    public void LoadZone(MapZone zone)
    {
        _zone = zone;
        RebuildIndex();
        CurrentNode = null;
        CurrentNodeChanged?.Invoke();
        MapChanged?.Invoke();
        Recalculate();
    }

    /// <summary>
    /// Force a re-evaluation of the current room from the latest GslGameState,
    /// even if title/exits haven't changed since the last check. Used after
    /// loading a zone or when the user issues #goto and CurrentNode is null.
    /// </summary>
    public void Recalculate()
    {
        if (_state is null) return;
        _lastTitle   = string.Empty;
        _lastExitKey = string.Empty;
        OnStateChanged();
    }

    public MapZone NewZone(string name)
    {
        var zone = new MapZone { Name = name };
        LoadZone(zone);
        return zone;
    }

    // ── Internal ────────────────────────────────────────────────────────────

    private void OnStateChanged()
    {
        if (_state is null) return;

        var title   = _state.RoomTitle;
        var exits   = _state.Exits;
        var exitKey = string.Join(",", exits.Order(StringComparer.OrdinalIgnoreCase));

        // Require at least a title before tracking
        if (string.IsNullOrWhiteSpace(title)) return;

        // Only process if title or exits changed (description updates, vitals, etc.
        // also fire StateChanged — ignore those).
        bool titleChanged = title   != _lastTitle;
        bool exitsChanged = exitKey != _lastExitKey;
        if (!titleChanged && !exitsChanged) return;

        _lastTitle   = title;
        _lastExitKey = exitKey;

        OnRoomChanged(title, _state.RoomDescription, exits);
    }

    private void OnRoomChanged(string title, string description, IReadOnlyCollection<string> exits)
    {
        var fingerprint   = MapFingerprint.Compute(title, exits);
        var serverRoomId  = _state?.ServerRoomId ?? string.Empty;
        var prevNode      = CurrentNode;
        var usedDir       = _pendingDirection;

        // Always clear pending direction after consuming it
        _pendingDirection = Direction.None;

        bool zoneChanged = false;

        // ── 1. Find the node ─────────────────────────────────────────────────
        // Priority:
        //   a) Server room ID (definitive if present)
        //   b) Graph walk: follow linked arc from previous node in the
        //      movement direction and verify the destination title matches
        //   c) Fingerprint index (title + exits)
        //   d) Reverse-arc search: find a node matching the fingerprint that
        //      has a reverse exit linking back to prevNode
        //   e) Description tiebreaker among all nodes with the same title
        //   f) Create new (if mapper enabled) or signal zone miss

        MapNode? node = null;

        // (a) Server room ID — definitive match
        if (!string.IsNullOrEmpty(serverRoomId) &&
            _serverRoomIndex.TryGetValue(serverRoomId, out var srvId) &&
            _zone.Nodes.TryGetValue(srvId, out var srvNode))
        {
            node = srvNode;
        }

        // (b) Graph walk: if we know where we were and which direction we moved,
        //     follow the linked arc and verify the destination title matches.
        if (node is null && prevNode != null && usedDir != Direction.None)
        {
            var arc = prevNode.GetExit(usedDir);
            if (arc?.DestinationId is { } destId &&
                _zone.Nodes.TryGetValue(destId, out var arcDest) &&
                arcDest.Title.Equals(title, StringComparison.OrdinalIgnoreCase))
            {
                node = arcDest;
            }
        }

        // (c) Fingerprint index (title + cardinal exits)
        if (node is null &&
            _fingerprintIndex.TryGetValue(fingerprint, out var existingId) &&
            _zone.Nodes.TryGetValue(existingId, out var existingNode))
        {
            node = existingNode;
        }

        // (d) Reverse-arc search: among all nodes with matching title, find one
        //     that has a reverse exit linking back to prevNode.
        if (node is null && prevNode != null && usedDir != Direction.None &&
            DirectionHelper.Opposite.TryGetValue(usedDir, out var oppDir))
        {
            foreach (var candidate in _zone.Nodes.Values)
            {
                if (!candidate.Title.Equals(title, StringComparison.OrdinalIgnoreCase))
                    continue;
                var backArc = candidate.GetExit(oppDir);
                if (backArc?.DestinationId == prevNode.Id)
                { node = candidate; break; }
            }
        }

        // (e) Description tiebreaker: scan all nodes with matching title and
        //     pick the one whose description matches (handles duplicate titles).
        if (node is null && !string.IsNullOrEmpty(description))
        {
            foreach (var candidate in _zone.Nodes.Values)
            {
                if (!candidate.Title.Equals(title, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (string.IsNullOrEmpty(candidate.Description)) continue;
                // Compare the first 80 chars of description (handles minor
                // trailing variations like player names in room text).
                var descA = description.Length > 80 ? description[..80] : description;
                var descB = candidate.Description.Length > 80
                    ? candidate.Description[..80] : candidate.Description;
                if (descA.Equals(descB, StringComparison.OrdinalIgnoreCase))
                { node = candidate; break; }
            }
        }

        if (node != null)
        {
            // Update description if it arrives after first visit
            if (string.IsNullOrEmpty(node.Description) && !string.IsNullOrEmpty(description))
            {
                node.Description = description;
                zoneChanged = true;
            }
            // Stamp server room ID on nodes that don't have it yet
            if (!string.IsNullOrEmpty(serverRoomId) && string.IsNullOrEmpty(node.ServerRoomId))
            {
                node.ServerRoomId = serverRoomId;
                _serverRoomIndex.TryAdd(serverRoomId, node.Id);
                zoneChanged = true;
            }
        }
        else if (!IsEnabled)
        {
            // Lookup-only mode: don't create new nodes. Signal the controller
            // to try loading a zone that contains this room — but only once we
            // have both title and exits, so the fingerprint is complete.
            CurrentNode = null;
            CurrentNodeChanged?.Invoke();
            if (exits.Count > 0)
                RoomNotFoundInZone?.Invoke();
            return;
        }
        else
        {
            // New room — create node and assign coordinates
            node = new MapNode
            {
                Id            = NextNodeId(),
                Title         = title,
                Description   = description,
                ServerRoomId  = serverRoomId,
            };

            AssignCoordinates(node, prevNode, usedDir);
            _zone.Nodes[node.Id] = node;
            _fingerprintIndex[fingerprint] = node.Id;
            if (!string.IsNullOrEmpty(serverRoomId))
                _serverRoomIndex.TryAdd(serverRoomId, node.Id);
            zoneChanged = true;
        }

        // ── 2. Link exits between prevNode and node ──────────────────────────
        if (prevNode != null && usedDir != Direction.None && prevNode.Id != node.Id)
        {
            // Forward exit: prevNode → node
            var fwd = prevNode.GetOrAddExit(usedDir, usedDir.ToString().ToLowerInvariant());
            if (fwd.DestinationId != node.Id)
            {
                fwd.DestinationId = node.Id;
                zoneChanged = true;
            }

            // Back exit: node → prevNode
            if (DirectionHelper.Opposite.TryGetValue(usedDir, out var opp) && opp != Direction.None)
            {
                var back = node.GetOrAddExit(opp, opp.ToString().ToLowerInvariant());
                if (back.DestinationId != prevNode.Id)
                {
                    back.DestinationId = prevNode.Id;
                    zoneChanged = true;
                }
            }
        }

        // ── 3. Ensure all compass exits have stub entries on the node ────────
        foreach (var exitStr in exits)
        {
            var dir = DirectionHelper.Parse(exitStr);
            if (dir == Direction.None) continue;
            if (node.GetExit(dir) is null)
            {
                node.Exits.Add(new MapExit { Direction = dir, MoveCommand = exitStr });
                zoneChanged = true;
            }
        }

        // ── 4. Update current node and fire events ───────────────────────────
        CurrentNode = node;
        CurrentNodeChanged?.Invoke();
        if (zoneChanged) MapChanged?.Invoke();
    }

    private static void AssignCoordinates(MapNode node, MapNode? prev, Direction dir)
    {
        if (prev is null || dir == Direction.None ||
            !DirectionHelper.Delta.TryGetValue(dir, out var delta))
        {
            // No context — leave at origin; caller can reposition later
            node.X = 0;
            node.Y = 0;
            node.Z = 0;
            return;
        }

        node.X = prev.X + delta.dx;
        node.Y = prev.Y + delta.dy;
        node.Z = prev.Z + delta.dz;
    }

    /// <summary>
    /// BFS from <paramref name="start"/> to <paramref name="destination"/> through linked exits.
    /// Returns the ordered move commands to walk the path, or null if unreachable.
    /// </summary>
    public IReadOnlyList<string>? FindPath(MapNode start, MapNode destination)
    {
        if (start.Id == destination.Id) return Array.Empty<string>();

        var cameFromNode = new Dictionary<int, int>();
        var cameFromMove = new Dictionary<int, string>();
        var queue = new Queue<MapNode>();
        queue.Enqueue(start);
        var visited = new HashSet<int> { start.Id };

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var exit in current.Exits)
            {
                if (!exit.DestinationId.HasValue) continue;
                var destId = exit.DestinationId.Value;
                if (!visited.Add(destId)) continue;
                if (!_zone.Nodes.TryGetValue(destId, out var next)) continue;

                cameFromNode[destId] = current.Id;
                cameFromMove[destId] = string.IsNullOrEmpty(exit.MoveCommand)
                    ? exit.Direction.ToString().ToLowerInvariant()
                    : exit.MoveCommand;

                if (destId == destination.Id)
                {
                    // Reconstruct
                    var moves = new List<string>();
                    var cursor = destId;
                    while (cursor != start.Id)
                    {
                        moves.Add(cameFromMove[cursor]);
                        cursor = cameFromNode[cursor];
                    }
                    moves.Reverse();
                    return moves;
                }

                queue.Enqueue(next);
            }
        }
        return null;
    }

    private int NextNodeId()
    {
        int max = 0;
        foreach (var id in _zone.Nodes.Keys)
            if (id > max) max = id;
        return max + 1;
    }

    private void RebuildIndex()
    {
        _fingerprintIndex.Clear();
        _serverRoomIndex.Clear();
        foreach (var node in _zone.Nodes.Values)
        {
            var fp = MapFingerprint.Compute(node.Title, node.Exits);
            _fingerprintIndex.TryAdd(fp, node.Id);
            if (!string.IsNullOrEmpty(node.ServerRoomId))
                _serverRoomIndex.TryAdd(node.ServerRoomId, node.Id);
        }
    }
}
