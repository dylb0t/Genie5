using System.Text.Json;
using System.Text.Json.Serialization;

namespace Genie4.Core.Mapper;

public sealed class MapZoneRepository
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented    = true,
        Converters       = { new JsonStringEnumConverter() },
    };

    public void Save(string path, MapZone zone)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(zone, Options));
    }

    public MapZone? Load(string path)
    {
        if (!File.Exists(path)) return null;
        try { return JsonSerializer.Deserialize<MapZone>(File.ReadAllText(path), Options); }
        catch { return null; }
    }

    public IReadOnlyList<string> ListZoneFiles(string directory)
    {
        if (!Directory.Exists(directory)) return [];
        return Directory.GetFiles(directory, "*.json");
    }
}
