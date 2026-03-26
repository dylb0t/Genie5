using Genie4.Core.Commanding;

namespace Genie4.Core.Aliases;

public sealed class AliasEngine
{
    private readonly List<AliasRule> _aliases = new();
    private readonly CommandEngine _commandEngine;

    public AliasEngine(CommandEngine commandEngine)
    {
        _commandEngine = commandEngine;
    }

    public IReadOnlyList<AliasRule> Aliases => _aliases;

    public AliasRule AddAlias(string name, string expansion, bool isEnabled = true)
    {
        var alias = new AliasRule(name, expansion, isEnabled);
        _aliases.Add(alias);
        return alias;
    }

    public bool RemoveAlias(string name)
    {
        var removed = _aliases.RemoveAll(a => a.Name == name);
        return removed > 0;
    }

    public bool SetEnabled(string name, bool enabled)
    {
        var alias = _aliases.FirstOrDefault(a => a.Name == name);
        if (alias == null) return false;
        alias.IsEnabled = enabled;
        return true;
    }

    public bool TryProcess(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return false;

        var parts = input.Split(' ', 2);
        var name = parts[0];

        var alias = _aliases.FirstOrDefault(a => a.IsEnabled && a.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (alias == null) return false;

        var args = parts.Length > 1 ? parts[1] : string.Empty;
        var expanded = Expand(alias.Expansion, args);

        _commandEngine.ProcessInput(expanded);
        return true;
    }

    private static string Expand(string expansion, string args)
    {
        return expansion.Replace("$*", args);
    }
}
