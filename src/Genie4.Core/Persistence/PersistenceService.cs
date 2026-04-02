using System.Text.Json;
using Genie4.Core.Aliases;
using Genie4.Core.Highlights;
using Genie4.Core.Triggers;
using Genie4.Core.Variables;

namespace Genie4.Core.Persistence;

public sealed class PersistenceService
{
    private readonly JsonSerializerOptions _options = new()
    {
        WriteIndented = true
    };

    public void SaveAliases(string path, IEnumerable<AliasRule> aliases)
    {
        var data = aliases.Select(a => new AliasPersistenceModel
        {
            Name = a.Name,
            Expansion = a.Expansion,
            IsEnabled = a.IsEnabled
        });

        File.WriteAllText(path, JsonSerializer.Serialize(data, _options));
    }

    public List<AliasPersistenceModel> LoadAliases(string path)
    {
        if (!File.Exists(path)) return new();
        return JsonSerializer.Deserialize<List<AliasPersistenceModel>>(File.ReadAllText(path)) ?? new();
    }

    public void SaveTriggers(string path, IEnumerable<TriggerRule> triggers)
    {
        var data = triggers.Select(t => new TriggerPersistenceModel
        {
            Pattern = t.Pattern,
            Action = t.Action,
            CaseSensitive = t.CaseSensitive,
            IsEnabled = t.IsEnabled
        });

        File.WriteAllText(path, JsonSerializer.Serialize(data, _options));
    }

    public List<TriggerPersistenceModel> LoadTriggers(string path)
    {
        if (!File.Exists(path)) return new();
        return JsonSerializer.Deserialize<List<TriggerPersistenceModel>>(File.ReadAllText(path)) ?? new();
    }

    public void SaveVariables(string path, VariableStore store)
    {
        var data = store.GetAll().Values.Select(v => new VariablePersistenceModel
        {
            Name = v.Name,
            Value = v.Value,
            Scope = v.Scope.ToString()
        });

        File.WriteAllText(path, JsonSerializer.Serialize(data, _options));
    }

    public List<VariablePersistenceModel> LoadVariables(string path)
    {
        if (!File.Exists(path)) return new();
        return JsonSerializer.Deserialize<List<VariablePersistenceModel>>(File.ReadAllText(path)) ?? new();
    }

    public void SaveHighlights(string path, IEnumerable<HighlightRule> rules)
    {
        var data = rules.Select(r => new HighlightPersistenceModel
        {
            Pattern = r.Pattern,
            ForegroundColor = r.ForegroundColor,
            IsRegex = r.IsRegex,
            CaseSensitive = r.CaseSensitive,
            IsEnabled = r.IsEnabled
        });
        File.WriteAllText(path, JsonSerializer.Serialize(data, _options));
    }

    public List<HighlightPersistenceModel> LoadHighlights(string path)
    {
        if (!File.Exists(path)) return new();
        return JsonSerializer.Deserialize<List<HighlightPersistenceModel>>(File.ReadAllText(path)) ?? new();
    }
}
