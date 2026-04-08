namespace Genie4.Core.Mapper;

public sealed class MapZone
{
    public Guid   Id    { get; set; } = Guid.NewGuid();
    public string Name  { get; set; } = "Zone 1";

    /// <summary>
    /// Original Genie4 zone id (e.g. "1", "47", "2d") preserved from the
    /// imported XML's <c>id</c> attribute. Exposed to scripts as <c>$zoneid</c>.
    /// </summary>
    public string Genie4Id { get; set; } = string.Empty;

    public Dictionary<int, MapNode> Nodes { get; set; } = new();
}
