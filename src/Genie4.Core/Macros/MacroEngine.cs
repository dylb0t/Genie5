namespace Genie4.Core.Macros;

public sealed class MacroRule
{
    public MacroRule(string key, string action)
    {
        Key    = key;
        Action = action;
    }

    /// <summary>Canonical key identifier (e.g., "F1", "Control+F2", "NumPad5").</summary>
    public string Key    { get; }
    /// <summary>Command sent when the key fires.</summary>
    public string Action { get; }
}

/// <summary>
/// Mirrors Genie4's macros.cfg: maps a canonical key identifier to a command
/// string. Callers resolve modifier+key to a canonical form before lookup.
/// </summary>
public sealed class MacroEngine
{
    private readonly Dictionary<string, MacroRule> _rules = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<MacroRule> Rules => _rules.Values;

    public void Add(string key, string action)
    {
        if (string.IsNullOrWhiteSpace(key)) return;
        _rules[key] = new MacroRule(key, action);
    }

    public bool Remove(string key) => _rules.Remove(key);

    public void Clear() => _rules.Clear();

    public MacroRule? Get(string key)
        => _rules.TryGetValue(key, out var r) ? r : null;
}
