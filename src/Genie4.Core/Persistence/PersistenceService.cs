using System.Text.Json;
using Genie4.Core.Aliases;
using Genie4.Core.Classes;
using Genie4.Core.Gags;
using Genie4.Core.Highlights;
using Genie4.Core.Layout;
using Genie4.Core.Macros;
using Genie4.Core.Presets;
using Genie4.Core.Substitutes;
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
            IsEnabled = t.IsEnabled,
            ClassName = t.ClassName,
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
            BackgroundColor = r.BackgroundColor,
            MatchType = r.MatchType.ToString(),
            CaseSensitive = r.CaseSensitive,
            IsEnabled = r.IsEnabled,
            ClassName = r.ClassName,
        });
        File.WriteAllText(path, JsonSerializer.Serialize(data, _options));
    }

    public void SaveClasses(string path, ClassEngine engine)
    {
        var data = engine.GetAll()
            .Where(kv => !kv.Key.Equals("default", StringComparison.OrdinalIgnoreCase))
            .Select(kv => new ClassPersistenceModel { Name = kv.Key, IsActive = kv.Value });
        File.WriteAllText(path, JsonSerializer.Serialize(data, _options));
    }

    public List<ClassPersistenceModel> LoadClasses(string path)
    {
        if (!File.Exists(path)) return new();
        try { return JsonSerializer.Deserialize<List<ClassPersistenceModel>>(File.ReadAllText(path)) ?? new(); }
        catch { return new(); }
    }

    public List<HighlightPersistenceModel> LoadHighlights(string path)
    {
        if (!File.Exists(path)) return new();
        return JsonSerializer.Deserialize<List<HighlightPersistenceModel>>(File.ReadAllText(path)) ?? new();
    }

    public void SaveNames(string path, IEnumerable<NameRule> rules)
    {
        var data = rules.Select(r => new NamePersistenceModel
        {
            Name            = r.Name,
            ForegroundColor = r.ForegroundColor,
            BackgroundColor = r.BackgroundColor,
        });
        File.WriteAllText(path, JsonSerializer.Serialize(data, _options));
    }

    public List<NamePersistenceModel> LoadNames(string path)
    {
        if (!File.Exists(path)) return new();
        try { return JsonSerializer.Deserialize<List<NamePersistenceModel>>(File.ReadAllText(path)) ?? new(); }
        catch { return new(); }
    }

    public void SaveSubstitutes(string path, IEnumerable<SubstituteRule> rules)
    {
        var data = rules.Select(r => new SubstitutePersistenceModel
        {
            Pattern       = r.Pattern,
            Replacement   = r.Replacement,
            CaseSensitive = r.CaseSensitive,
            IsEnabled     = r.IsEnabled,
            ClassName     = r.ClassName,
        });
        File.WriteAllText(path, JsonSerializer.Serialize(data, _options));
    }

    public List<SubstitutePersistenceModel> LoadSubstitutes(string path)
    {
        if (!File.Exists(path)) return new();
        try { return JsonSerializer.Deserialize<List<SubstitutePersistenceModel>>(File.ReadAllText(path)) ?? new(); }
        catch { return new(); }
    }

    public void SaveGags(string path, IEnumerable<GagRule> rules)
    {
        var data = rules.Select(r => new GagPersistenceModel
        {
            Pattern       = r.Pattern,
            CaseSensitive = r.CaseSensitive,
            IsEnabled     = r.IsEnabled,
            ClassName     = r.ClassName,
        });
        File.WriteAllText(path, JsonSerializer.Serialize(data, _options));
    }

    public List<GagPersistenceModel> LoadGags(string path)
    {
        if (!File.Exists(path)) return new();
        try { return JsonSerializer.Deserialize<List<GagPersistenceModel>>(File.ReadAllText(path)) ?? new(); }
        catch { return new(); }
    }

    public void SaveMacros(string path, IEnumerable<MacroRule> rules)
    {
        var data = rules.Select(r => new MacroPersistenceModel
        {
            Key    = r.Key,
            Action = r.Action,
        });
        File.WriteAllText(path, JsonSerializer.Serialize(data, _options));
    }

    public List<MacroPersistenceModel> LoadMacros(string path)
    {
        if (!File.Exists(path)) return new();
        try { return JsonSerializer.Deserialize<List<MacroPersistenceModel>>(File.ReadAllText(path)) ?? new(); }
        catch { return new(); }
    }

    public void SavePresets(string path, PresetEngine engine)
    {
        var data = engine.Presets.Values.Select(r => new PresetPersistenceModel
        {
            Id              = r.Id,
            ForegroundColor = r.ForegroundColor,
            BackgroundColor = r.BackgroundColor,
            HighlightLine   = r.HighlightLine,
        });
        File.WriteAllText(path, JsonSerializer.Serialize(data, _options));
    }

    public List<PresetPersistenceModel> LoadPresets(string path)
    {
        if (!File.Exists(path)) return new();
        try { return JsonSerializer.Deserialize<List<PresetPersistenceModel>>(File.ReadAllText(path)) ?? new(); }
        catch { return new(); }
    }

    public void SaveLayout(string path, LayoutState state)
        => File.WriteAllText(path, JsonSerializer.Serialize(state, _options));

    public LayoutState? LoadLayout(string path)
    {
        if (!File.Exists(path)) return null;
        try { return JsonSerializer.Deserialize<LayoutState>(File.ReadAllText(path)); }
        catch { return null; }
    }

    public void SaveWindowSettings(string path, WindowSettingsStore store)
    {
        var data = store.All.Values.Select(s => new WindowSettingsPersistenceModel
        {
            Id           = s.Id,
            DisplayTitle = s.DisplayTitle,
            FontFamily   = s.FontFamily,
            FontSize     = s.FontSize,
            Foreground   = s.Foreground,
            Background   = s.Background,
            Timestamp    = s.Timestamp,
        });
        File.WriteAllText(path, JsonSerializer.Serialize(data, _options));
    }

    public List<WindowSettingsPersistenceModel> LoadWindowSettings(string path)
    {
        if (!File.Exists(path)) return new();
        try { return JsonSerializer.Deserialize<List<WindowSettingsPersistenceModel>>(File.ReadAllText(path)) ?? new(); }
        catch { return new(); }
    }

    // ── Client state ────────────────────────────────────────────────────────

    public void SaveClientState(string path, ClientState state)
        => File.WriteAllText(path, JsonSerializer.Serialize(state, _options));

    public ClientState LoadClientState(string path)
    {
        if (!File.Exists(path)) return new();
        try { return JsonSerializer.Deserialize<ClientState>(File.ReadAllText(path)) ?? new(); }
        catch { return new(); }
    }
}
