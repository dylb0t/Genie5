namespace Genie4.Core.Mapper;

public sealed class MapExit
{
    public Direction Direction    { get; set; }
    public string    MoveCommand  { get; set; } = string.Empty;
    public int?      DestinationId { get; set; }
}
