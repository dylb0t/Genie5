namespace Genie4.Core.Aliases;

public sealed class AliasRule
{
    public AliasRule(string name, string expansion, bool isEnabled = true)
    {
        Name = name;
        Expansion = expansion;
        IsEnabled = isEnabled;
    }

    public string Name { get; }
    public string Expansion { get; }
    public bool IsEnabled { get; set; }
}
