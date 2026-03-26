using Genie4.Core.Commanding;

namespace Genie4.Core.Variables;

public sealed class VariableEngine
{
    private readonly VariableStore _store = new();
    private readonly CommandEngine _commandEngine;

    public VariableEngine(CommandEngine commandEngine)
    {
        _commandEngine = commandEngine;
    }

    public VariableStore Store => _store;

    public bool TryProcess(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return false;

        if (input.StartsWith("#var ", StringComparison.OrdinalIgnoreCase))
        {
            var parts = input.Substring(5).Split(' ', 2);
            if (parts.Length == 2)
            {
                _store.Set(parts[0], parts[1]);
            }
            return true;
        }

        if (input.StartsWith("#unset ", StringComparison.OrdinalIgnoreCase))
        {
            var name = input.Substring(7).Trim();
            _store.Remove(name);
            return true;
        }

        return false;
    }

    public string Expand(string input)
    {
        return _store.Expand(input);
    }
}
