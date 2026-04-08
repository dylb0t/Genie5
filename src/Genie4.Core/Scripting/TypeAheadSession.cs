namespace Genie4.Core.Scripting;

/// <summary>
/// Session-wide type-ahead limit, shared by the mapper and the script engine.
/// Auto-calibrated downward when the server reports its cap.
/// </summary>
public sealed class TypeAheadSession
{
    public int Limit { get; set; } = 3;
}
