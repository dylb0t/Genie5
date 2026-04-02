namespace Genie4.Core.Profiles;

public sealed class ConnectionProfile
{
    public Guid   Id              { get; set; } = Guid.NewGuid();
    public string Name            { get; set; } = string.Empty;

    // Simutronics SGE fields (used when IsSimutronics = true)
    public bool   IsSimutronics   { get; set; } = true;
    public string GameCode        { get; set; } = "DR";
    public string CharacterName   { get; set; } = string.Empty;
    public string AccountName     { get; set; } = string.Empty;

    // Direct TCP fields (used when IsSimutronics = false)
    public string Host            { get; set; } = string.Empty;
    public int    Port            { get; set; } = 4000;

    // AES-GCM encrypted, base64: nonce(12) + tag(16) + ciphertext
    public string EncryptedPassword { get; set; } = string.Empty;
}
