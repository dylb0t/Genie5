namespace Genie4.Core.Persistence;

public sealed class AliasPersistenceModel
{
    public string Name { get; set; } = string.Empty;
    public string Expansion { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
}

public sealed class TriggerPersistenceModel
{
    public string Pattern { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public bool CaseSensitive { get; set; }
    public bool IsEnabled { get; set; } = true;
}

public sealed class VariablePersistenceModel
{
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string Scope { get; set; } = "User";
}
