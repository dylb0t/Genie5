namespace Genie4.Core.Classes;

/// <summary>
/// Tracks named on/off toggles that gate highlights/triggers/substitutes/gags.
/// Mirrors Genie4's classes.cfg: empty class name is treated as the "default"
/// class, which is always considered active.
/// </summary>
public sealed class ClassEngine
{
    private readonly Dictionary<string, bool> _classes = new(StringComparer.OrdinalIgnoreCase);

    public ClassEngine()
    {
        _classes["default"] = true;
    }

    /// <summary>All class names in insertion/load order, lowercased for display.</summary>
    public IReadOnlyCollection<string> Names => _classes.Keys;

    /// <summary>Returns true when the class is active; empty name or "default" is always true.</summary>
    public bool IsActive(string? className)
    {
        if (string.IsNullOrEmpty(className)) return true;
        if (className.Equals("default", StringComparison.OrdinalIgnoreCase)) return true;
        return _classes.TryGetValue(className, out var v) && v;
    }

    /// <summary>Adds the class if missing; returns current active state.</summary>
    public bool Ensure(string className, bool defaultActive = true)
    {
        if (string.IsNullOrEmpty(className)) return true;
        if (className.Equals("default", StringComparison.OrdinalIgnoreCase)) return true;
        if (!_classes.TryGetValue(className, out var v))
        {
            _classes[className] = defaultActive;
            return defaultActive;
        }
        return v;
    }

    public void Set(string className, bool active)
    {
        if (string.IsNullOrEmpty(className)) return;
        if (className.Equals("default", StringComparison.OrdinalIgnoreCase)) { _classes["default"] = true; return; }
        _classes[className] = active;
    }

    public bool Remove(string className)
    {
        if (string.IsNullOrEmpty(className)) return false;
        if (className.Equals("default", StringComparison.OrdinalIgnoreCase)) return false;
        return _classes.Remove(className);
    }

    /// <summary>Removes every class except the always-present "default".</summary>
    public void Clear()
    {
        _classes.Clear();
        _classes["default"] = true;
    }

    public void ActivateAll()
    {
        foreach (var key in _classes.Keys.ToList()) _classes[key] = true;
    }

    public void DeactivateAll()
    {
        foreach (var key in _classes.Keys.ToList())
        {
            if (!key.Equals("default", StringComparison.OrdinalIgnoreCase))
                _classes[key] = false;
        }
    }

    public IReadOnlyDictionary<string, bool> GetAll() => _classes;
}
