namespace Genie4.Core.Variables;

public sealed class VariableStore
{
    private readonly Dictionary<string, VariableValue> _variables = new(StringComparer.OrdinalIgnoreCase);

    public void Set(string name, string value, VariableScope scope = VariableScope.User)
    {
        if (_variables.ContainsKey(name))
        {
            _variables[name].Value = value;
        }
        else
        {
            _variables[name] = new VariableValue(name, value, scope);
        }
    }

    public string? Get(string name)
    {
        return _variables.TryGetValue(name, out var value) ? value.Value : null;
    }

    public bool Remove(string name)
    {
        return _variables.Remove(name);
    }

    public IReadOnlyDictionary<string, VariableValue> GetAll() => _variables;

    public string Expand(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;

        var result = input;
        foreach (var kvp in _variables)
        {
            result = result.Replace("$" + kvp.Key, kvp.Value.Value);
        }

        return result;
    }
}
