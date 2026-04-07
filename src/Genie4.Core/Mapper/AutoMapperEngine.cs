using Genie4.Core.Gsl;

namespace Genie4.Core.Mapper;

public sealed class AutoMapperEngine
{
    private MapZone _zone;
    private GslGameState? _state;

    // Fingerprint → NodeId for fast room matching
    private readonly Dictionary<string, Guid> _fingerprintIndex = new();

    // State between room transitions
    private string  _lastTitle = string.Empty;
    private string  _lastExitKey = string.Empty; // sorted join used for change detection

    // The movement command that was sent before the last room change fired
    private Direction _pendingDirection = Direction.None;

    public MapNode?  CurrentNode  { get; private set; }
    public MapZone   ActiveZone   => _zone;
    public bool      IsEnabled    { get; set; } = true;

    public event Action? MapChanged;
    public event Action? CurrentNodeChanged;

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
        if (!IsEnabled || _state is null) return;

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
        var fingerprint = MapFingerprint.Compute(title, exits);
        var prevNode    = CurrentNode;
        var usedDir     = _pendingDirection;

        // Always clear pending direction after consuming it
        _pendingDirection = Direction.None;

        bool zoneChanged = false;

        // ── 1. Find or create the node ───────────────────────────────────────
        MapNode node;
        if (_fingerprintIndex.TryGetValue(fingerprint, out var existingId) &&
            _zone.Nodes.TryGetValue(existingId, out var existingNode))
        {
            node = existingNode;
            // Update description if it arrives after first visit
            if (string.IsNullOrEmpty(node.Description) && !string.IsNullOrEmpty(description))
            {
                node.Description = description;
                zoneChanged = true;
            }
        }
        else
        {
            // New room — create node and assign coordinates
            node = new MapNode
            {
                Title       = title,
                Description = description,
            };

            AssignCoordinates(node, prevNode, usedDir);
            _zone.Nodes[node.Id] = node;
            _fingerprintIndex[fingerprint] = node.Id;
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

    private void RebuildIndex()
    {
        _fingerprintIndex.Clear();
        foreach (var node in _zone.Nodes.Values)
        {
            // Reconstruct exit strings from the exit directions for fingerprinting
            var exitStrings = node.Exits.Select(e => e.MoveCommand);
            var fp = MapFingerprint.Compute(node.Title, exitStrings);
            _fingerprintIndex.TryAdd(fp, node.Id);
        }
    }
}
