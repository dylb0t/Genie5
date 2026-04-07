namespace Genie4.Core.Mapper;

public sealed class MapNode
{
    public Guid   Id          { get; set; } = Guid.NewGuid();
    public string Title       { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int    X           { get; set; }
    public int    Y           { get; set; }
    public int    Z           { get; set; }
    public string Notes       { get; set; } = string.Empty;
    public string Color       { get; set; } = string.Empty;

    public List<MapExit> Exits { get; set; } = new();

    public MapExit? GetExit(Direction dir)
        => Exits.FirstOrDefault(e => e.Direction == dir);

    public MapExit GetOrAddExit(Direction dir, string moveCommand)
    {
        var ex = GetExit(dir);
        if (ex is null)
        {
            ex = new MapExit { Direction = dir, MoveCommand = moveCommand };
            Exits.Add(ex);
        }
        return ex;
    }
}
