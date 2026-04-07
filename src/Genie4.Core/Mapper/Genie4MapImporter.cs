using System.Xml;

namespace Genie4.Core.Mapper;

/// <summary>
/// Imports Genie4 XML map files into the Genie5 MapZone format.
///
/// Genie4 XML structure:
///   &lt;zone name="..." id="10"&gt;
///     &lt;node id="1" name="Room Title"&gt;
///       &lt;description&gt;...&lt;/description&gt;
///       &lt;position x="260" y="100" z="0" /&gt;
///       &lt;arc exit="north" move="north" destination="2" /&gt;
///     &lt;/node&gt;
///   &lt;/zone&gt;
///
/// Node IDs in Genie4 are integers local to the zone file.
/// We generate stable Guids from (zoneName + nodeId) so the same file always
/// produces the same Guids — enabling re-import without duplicating nodes.
/// </summary>
public static class Genie4MapImporter
{
    public static MapZone Import(string xmlPath)
    {
        var doc = new XmlDocument();
        doc.Load(xmlPath);

        var zoneEl = doc.DocumentElement
            ?? throw new InvalidDataException("XML has no root element.");

        var zoneName = zoneEl.GetAttribute("name");
        if (string.IsNullOrEmpty(zoneName))
            zoneName = Path.GetFileNameWithoutExtension(xmlPath);

        var zoneIdStr = zoneEl.GetAttribute("id");

        var zone = new MapZone
        {
            Id   = StableGuid($"zone:{zoneIdStr}:{zoneName}"),
            Name = zoneName,
        };

        // ── Pass 1: build all nodes ──────────────────────────────────────────
        // Map Genie4 integer IDs → Guids so arcs can be resolved in pass 2.
        var idMap = new Dictionary<string, Guid>(); // genie4 id → guid

        foreach (XmlElement nodeEl in zoneEl.SelectNodes("node")!)
        {
            var g4Id = nodeEl.GetAttribute("id");
            var guid = StableGuid($"{zoneIdStr}:{zoneName}:node:{g4Id}");
            idMap[g4Id] = guid;

            var node = new MapNode
            {
                Id    = guid,
                Title = nodeEl.GetAttribute("name"),
            };

            // Description — take the first <description> child
            var descEl = nodeEl.SelectSingleNode("description");
            if (descEl != null)
                node.Description = descEl.InnerText.Trim();

            // Position
            var posEl = nodeEl.SelectSingleNode("position") as XmlElement;
            if (posEl != null)
            {
                int.TryParse(posEl.GetAttribute("x"), out int px);
                int.TryParse(posEl.GetAttribute("y"), out int py);
                int.TryParse(posEl.GetAttribute("z"), out int pz);
                // Genie4 uses pixel coordinates (multiples of ~20).
                // Divide by 20 to convert to grid units.
                node.X = px / 20;
                node.Y = py / 20;
                node.Z = pz;
            }

            zone.Nodes[guid] = node;
        }

        // ── Pass 2: resolve arcs ─────────────────────────────────────────────
        foreach (XmlElement nodeEl in zoneEl.SelectNodes("node")!)
        {
            var g4Id = nodeEl.GetAttribute("id");
            if (!idMap.TryGetValue(g4Id, out var nodeGuid)) continue;
            var node = zone.Nodes[nodeGuid];

            foreach (XmlElement arcEl in nodeEl.SelectNodes("arc")!)
            {
                var exitStr  = arcEl.GetAttribute("exit");
                var moveStr  = arcEl.GetAttribute("move");
                var destStr  = arcEl.GetAttribute("destination");

                var dir = DirectionHelper.Parse(exitStr);

                Guid? destGuid = null;
                if (!string.IsNullOrEmpty(destStr) && idMap.TryGetValue(destStr, out var dg))
                    destGuid = dg;

                node.Exits.Add(new MapExit
                {
                    Direction     = dir,
                    MoveCommand   = string.IsNullOrEmpty(moveStr) ? exitStr : moveStr,
                    DestinationId = destGuid,
                });
            }
        }

        return zone;
    }

    /// <summary>
    /// Imports all .xml files in a directory, returning one MapZone per file.
    /// </summary>
    public static IReadOnlyList<MapZone> ImportDirectory(string directory)
    {
        if (!Directory.Exists(directory)) return [];

        var results = new List<MapZone>();
        foreach (var file in Directory.GetFiles(directory, "*.xml"))
        {
            try   { results.Add(Import(file)); }
            catch { /* skip malformed files */ }
        }
        return results;
    }

    // Produces a deterministic Guid from a string key via MD5.
    private static Guid StableGuid(string key)
    {
        var hash = System.Security.Cryptography.MD5.HashData(
            System.Text.Encoding.UTF8.GetBytes(key));
        return new Guid(hash);
    }
}
