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
/// Node IDs in Genie4 are integers local to the zone file. Genie5 preserves
/// them as-is so users can reference rooms by their Genie4 ID (e.g. #goto 232).
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

        var zone = new MapZone { Name = zoneName };

        // ── Pass 1: build all nodes (preserve Genie4 integer IDs) ────────────
        foreach (XmlElement nodeEl in zoneEl.SelectNodes("node")!)
        {
            if (!int.TryParse(nodeEl.GetAttribute("id"), out int nodeId)) continue;

            var node = new MapNode
            {
                Id    = nodeId,
                Title = nodeEl.GetAttribute("name"),
            };

            // Description — take the first <description> child
            var descEl = nodeEl.SelectSingleNode("description");
            if (descEl != null)
                node.Description = descEl.InnerText.Trim();

            // Note — Genie4 stores it as an attribute on <node>, with multiple
            // labels separated by '|' (used by #goto for label lookup).
            var noteAttr = nodeEl.GetAttribute("note");
            if (!string.IsNullOrEmpty(noteAttr))
                node.Notes = noteAttr.Trim();

            // Color — also a node attribute (e.g. "#FF00FF")
            var colorAttr = nodeEl.GetAttribute("color");
            if (!string.IsNullOrEmpty(colorAttr))
                node.Color = colorAttr.Trim();

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

            zone.Nodes[nodeId] = node;
        }

        // ── Pass 2: resolve arcs ─────────────────────────────────────────────
        foreach (XmlElement nodeEl in zoneEl.SelectNodes("node")!)
        {
            if (!int.TryParse(nodeEl.GetAttribute("id"), out int nodeId)) continue;
            if (!zone.Nodes.TryGetValue(nodeId, out var node)) continue;

            foreach (XmlElement arcEl in nodeEl.SelectNodes("arc")!)
            {
                var exitStr  = arcEl.GetAttribute("exit");
                var moveStr  = arcEl.GetAttribute("move");
                var destStr  = arcEl.GetAttribute("destination");

                var dir = DirectionHelper.Parse(exitStr);

                int? destId = null;
                if (int.TryParse(destStr, out int parsedDest) && zone.Nodes.ContainsKey(parsedDest))
                    destId = parsedDest;

                node.Exits.Add(new MapExit
                {
                    Direction     = dir,
                    MoveCommand   = string.IsNullOrEmpty(moveStr) ? exitStr : moveStr,
                    DestinationId = destId,
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

}
