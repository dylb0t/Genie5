using System.Net.Sockets;
using System.Text;

namespace Genie4.Core.Networking;

public sealed class TcpGameClient
{
    private TcpClient? _client;
    private NetworkStream? _stream;
    private readonly byte[] _buffer;

    public event Action<string>? LineReceived;
    public event Action? Connected;
    public event Action? Disconnected;

    public TcpGameClient(int bufferSize = 4096)
    {
        _buffer = new byte[bufferSize];
    }

    public bool IsConnected => _client?.Connected == true;

    public async Task ConnectAsync(GameConnectionOptions options, CancellationToken cancellationToken = default)
    {
        _client = new TcpClient();
        await _client.ConnectAsync(options.Host, options.Port, cancellationToken);
        _stream = _client.GetStream();

        Connected?.Invoke();

        _ = Task.Run(() => ReceiveLoop(cancellationToken));
    }

    /// <summary>
    /// Connects to a Simutronics game server using the key obtained from SGE auth.
    /// Sends the Wrayth/StormFront handshake immediately on connect.
    /// </summary>
    public async Task ConnectWithKeyAsync(string host, int port, string loginKey,
        CancellationToken cancellationToken = default)
    {
        _client = new TcpClient();
        await _client.ConnectAsync(host, port, cancellationToken);
        _stream = _client.GetStream();

        // SGE game server handshake: key line + frontend identifier
        var handshake = loginKey + "\nFE:WRAYTH /VERSION:1.0.1.22 /P:WIN_UNKNOWN /XML\n";
        var bytes = Encoding.UTF8.GetBytes(handshake);
        await _stream.WriteAsync(bytes, 0, bytes.Length, cancellationToken);
        await _stream.FlushAsync(cancellationToken);

        Connected?.Invoke();

        _ = Task.Run(() => ReceiveLoop(cancellationToken));
    }

    public async Task SendAsync(string text)
    {
        if (_stream == null) return;

        var data = Encoding.UTF8.GetBytes(text + "\n");
        await _stream.WriteAsync(data, 0, data.Length);
    }

    public void Disconnect()
    {
        try
        {
            _stream?.Close();
            _client?.Close();
        }
        finally
        {
            Disconnected?.Invoke();
        }
    }

    private async Task ReceiveLoop(CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();

        try
        {
            while (!cancellationToken.IsCancellationRequested && _client != null && _client.Connected)
            {
                if (_stream == null) break;

                var bytesRead = await _stream.ReadAsync(_buffer, 0, _buffer.Length, cancellationToken);
                if (bytesRead <= 0) break;

                var text = Encoding.UTF8.GetString(_buffer, 0, bytesRead);

                foreach (var ch in text)
                {
                    if (ch == '\n')
                    {
                        var line = sb.ToString();
                        sb.Clear();
                        LineReceived?.Invoke(line);
                    }
                    else if (ch != '\r')
                    {
                        sb.Append(ch);
                    }
                }
            }
        }
        catch
        {
            // swallow for now
        }
        finally
        {
            Disconnect();
        }
    }
}
