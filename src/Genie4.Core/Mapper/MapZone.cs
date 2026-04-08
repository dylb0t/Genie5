namespace Genie4.Core.Mapper;

public sealed class MapZone
{
    public Guid   Id    { get; set; } = Guid.NewGuid();
    public string Name  { get; set; } = "Zone 1";
    public Dictionary<int, MapNode> Nodes { get; set; } = new();
}
