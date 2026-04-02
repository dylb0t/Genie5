namespace Genie4.Core.Sge;

public sealed class SgeLoginResult
{
    public bool Success { get; init; }
    public string Error { get; init; } = string.Empty;

    public string GameHost { get; init; } = string.Empty;
    public int    GamePort { get; init; }
    public string Key      { get; init; } = string.Empty;

    // Populated when character is empty — lets caller show a picker
    public IReadOnlyList<string> Characters { get; init; } = [];

    public static SgeLoginResult Fail(string error) => new() { Success = false, Error = error };
}
