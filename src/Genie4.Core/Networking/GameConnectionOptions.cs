namespace Genie4.Core.Networking;

public sealed class GameConnectionOptions
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; }
    public bool UseSsl { get; set; }
    public int ReadBufferSize { get; set; } = 4096;
}
