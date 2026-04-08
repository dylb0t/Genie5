namespace Genie4.Core.Mapper;

internal static class MapFingerprint
{
    /// <summary>
    /// Title + sorted set of canonical cardinal direction names. Non-cardinal
    /// exits ("go shop", climb, etc.) are intentionally ignored so that an
    /// imported Genie4 room with custom-move exits still fingerprints to the
    /// same key as the GSL compass list, which only contains cardinals.
    /// </summary>
    internal static string Compute(string title, IEnumerable<string> exits)
    {
        var canonical = exits
            .Select(DirectionHelper.Parse)
            .Where(d => d != Direction.None)
            .Select(d => d.ToString().ToLowerInvariant())
            .Distinct()
            .Order(StringComparer.OrdinalIgnoreCase);
        return $"{title.Trim()}|{string.Join(",", canonical)}";
    }

    /// <summary>Compute from MapExit list (used by RebuildIndex).</summary>
    internal static string Compute(string title, IEnumerable<MapExit> exits)
        => Compute(title, exits.Select(e => e.Direction.ToString()));
}
