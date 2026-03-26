namespace Genie4.Core.Variables;

public sealed class VariableValue
{
    public VariableValue(string name, string value, VariableScope scope = VariableScope.User)
    {
        Name = name;
        Value = value;
        Scope = scope;
    }

    public string Name { get; }
    public string Value { get; set; }
    public VariableScope Scope { get; }
}

public enum VariableScope
{
    User,
    Temporary,
    Reserved,
    Server
}
