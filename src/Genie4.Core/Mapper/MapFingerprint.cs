namespace Genie4.Core.Mapper;

internal static class MapFingerprint
{
    internal static string Compute(string title, IEnumerable<string> exits)
    {
        var sortedExits = string.Join(",", exits.Order(StringComparer.OrdinalIgnoreCase));
        return $"{title.Trim()}|{sortedExits}";
    }
}
