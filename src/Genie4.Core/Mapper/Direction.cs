namespace Genie4.Core.Mapper;

public enum Direction
{
    None = 0,
    North, NorthEast, East, SouthEast,
    South, SouthWest, West, NorthWest,
    Up, Down, Out, In
}

public static class DirectionHelper
{
    public static readonly IReadOnlyDictionary<string, Direction> StringToDirection =
        new Dictionary<string, Direction>(StringComparer.OrdinalIgnoreCase)
        {
            ["n"]         = Direction.North,
            ["north"]     = Direction.North,
            ["ne"]        = Direction.NorthEast,
            ["northeast"] = Direction.NorthEast,
            ["e"]         = Direction.East,
            ["east"]      = Direction.East,
            ["se"]        = Direction.SouthEast,
            ["southeast"] = Direction.SouthEast,
            ["s"]         = Direction.South,
            ["south"]     = Direction.South,
            ["sw"]        = Direction.SouthWest,
            ["southwest"] = Direction.SouthWest,
            ["w"]         = Direction.West,
            ["west"]      = Direction.West,
            ["nw"]        = Direction.NorthWest,
            ["northwest"] = Direction.NorthWest,
            ["u"]         = Direction.Up,
            ["up"]        = Direction.Up,
            ["d"]         = Direction.Down,
            ["down"]      = Direction.Down,
            ["out"]       = Direction.Out,
            ["in"]        = Direction.In,
        };

    // (dx, dy, dz) — positive Y is south on the grid
    public static readonly IReadOnlyDictionary<Direction, (int dx, int dy, int dz)> Delta =
        new Dictionary<Direction, (int, int, int)>
        {
            [Direction.North]     = ( 0, -1,  0),
            [Direction.NorthEast] = ( 1, -1,  0),
            [Direction.East]      = ( 1,  0,  0),
            [Direction.SouthEast] = ( 1,  1,  0),
            [Direction.South]     = ( 0,  1,  0),
            [Direction.SouthWest] = (-1,  1,  0),
            [Direction.West]      = (-1,  0,  0),
            [Direction.NorthWest] = (-1, -1,  0),
            [Direction.Up]        = ( 0,  0,  1),
            [Direction.Down]      = ( 0,  0, -1),
            [Direction.Out]       = ( 0,  0,  0),
            [Direction.In]        = ( 0,  0,  0),
            [Direction.None]      = ( 0,  0,  0),
        };

    public static readonly IReadOnlyDictionary<Direction, Direction> Opposite =
        new Dictionary<Direction, Direction>
        {
            [Direction.North]     = Direction.South,
            [Direction.NorthEast] = Direction.SouthWest,
            [Direction.East]      = Direction.West,
            [Direction.SouthEast] = Direction.NorthWest,
            [Direction.South]     = Direction.North,
            [Direction.SouthWest] = Direction.NorthEast,
            [Direction.West]      = Direction.East,
            [Direction.NorthWest] = Direction.SouthEast,
            [Direction.Up]        = Direction.Down,
            [Direction.Down]      = Direction.Up,
            [Direction.Out]       = Direction.In,
            [Direction.In]        = Direction.Out,
            [Direction.None]      = Direction.None,
        };

    // Screen-space angle in degrees for drawing exit stubs (0 = east, clockwise)
    public static readonly IReadOnlyDictionary<Direction, double> Angle =
        new Dictionary<Direction, double>
        {
            [Direction.North]     = 270,
            [Direction.NorthEast] = 315,
            [Direction.East]      =   0,
            [Direction.SouthEast] =  45,
            [Direction.South]     =  90,
            [Direction.SouthWest] = 135,
            [Direction.West]      = 180,
            [Direction.NorthWest] = 225,
        };

    public static Direction Parse(string token)
    {
        token = token.Trim().ToLowerInvariant();
        return StringToDirection.TryGetValue(token, out var d) ? d : Direction.None;
    }
}
