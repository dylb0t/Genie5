using System.Net.Security;
using System.Net.Sockets;
using System.Text;

namespace Genie4.Core.Sge;

/// <summary>
/// Handles the Simutronics eAccess/SGE authentication protocol.
/// Connects to eaccess.play.net:7910 over TLS 1.2, authenticates the account,
/// selects the game instance and character, then returns the game server
/// host/port/key needed to open the actual game connection.
/// </summary>
public sealed class SgeClient
{
    private const string EAccessHost = "eaccess.play.net";
    private const int    EAccessPort = 7910;
    private const int    BufferSize  = 2048;

    // Known Simutronics game codes and their display names
    public static readonly IReadOnlyList<(string Code, string DisplayName)> Games =
    [
        ("DR",  "DragonRealms"),
        ("DRX", "DragonRealms (Platinum)"),
        ("DRF", "DragonRealms (French)"),
        ("DRT", "DragonRealms (Test)"),
        ("GS3", "Gemstone IV"),
        ("GSX", "Gemstone IV (Platinum)"),
        ("GSF", "Gemstone IV (French)"),
        ("GST", "Gemstone IV (Test)"),
    ];

    /// <summary>
    /// Authenticates against eAccess and retrieves the game server connection details.
    /// If <paramref name="character"/> is empty, returns the character list in
    /// <see cref="SgeLoginResult.Characters"/> without completing the login.
    /// </summary>
    public async Task<SgeLoginResult> AuthenticateAsync(
        string account, string password, string gameCode, string character,
        CancellationToken cancellationToken = default)
    {
        using var tcp = new TcpClient();
        try
        {
            await tcp.ConnectAsync(EAccessHost, EAccessPort, cancellationToken);
        }
        catch (Exception ex)
        {
            return SgeLoginResult.Fail($"Could not reach eAccess server: {ex.Message}");
        }

        using var ssl = new SslStream(tcp.GetStream(), leaveInnerStreamOpen: false,
            userCertificateValidationCallback: (_, _, _, _) => true); // Simutronics uses self-signed
        try
        {
            await ssl.AuthenticateAsClientAsync(EAccessHost, null,
                System.Security.Authentication.SslProtocols.Tls12, false);
        }
        catch (Exception ex)
        {
            return SgeLoginResult.Fail($"TLS handshake failed: {ex.Message}");
        }

        // Step 1: Request 32-byte XOR key
        await WriteAsync(ssl, "K\n");
        var keyBytes = new byte[32];
        var read = await ssl.ReadAsync(keyBytes.AsMemory(0, 32), cancellationToken);
        if (read != 32)
            return SgeLoginResult.Fail("Unexpected key length from eAccess.");

        // Step 2: Send account + XOR-encrypted password
        var accountPart  = Encoding.ASCII.GetBytes("A\t" + account.ToUpper() + "\t");
        var encPassword  = EncryptPassword(keyBytes, password);
        var authMessage  = new byte[accountPart.Length + encPassword.Length];
        Buffer.BlockCopy(accountPart, 0, authMessage, 0, accountPart.Length);
        Buffer.BlockCopy(encPassword, 0, authMessage, accountPart.Length, encPassword.Length);
        await ssl.WriteAsync(authMessage, cancellationToken);
        await ssl.FlushAsync(cancellationToken);

        var authResponse = await ReadStringAsync(ssl, cancellationToken);
        if (!authResponse.Contains("\tKEY\t"))
            return SgeLoginResult.Fail("Authentication failed. Check account name and password.");

        // Step 3: Select game instance
        await WriteAsync(ssl, "G\t" + gameCode.ToUpper());
        var gameResponse = await ReadStringAsync(ssl, cancellationToken);
        if (gameResponse.Trim('\0').ToUpper() == "PROBLEM")
            return SgeLoginResult.Fail("Account access problem. Please visit play.net.");

        // Step 4: Request character list
        await WriteAsync(ssl, "C");
        var charResponse = await ReadStringAsync(ssl, cancellationToken);
        var parts = charResponse.TrimEnd('\0').Split('\t',
            StringSplitOptions.RemoveEmptyEntries);

        // Response format: [count]\t[?]\tW_NAME_NNN\tCharName\tW_NAME_NNN\tCharName...
        // Keys match pattern W_XXXX_NNN; the element immediately after each key is the name.
        var characterNames = new List<string>();
        for (int i = 0; i < parts.Length - 1; i++)
        {
            if (System.Text.RegularExpressions.Regex.IsMatch(parts[i], @"^W_[A-Z0-9]+_[A-Z0-9]+$"))
                characterNames.Add(parts[i + 1]);
        }

        // If no character requested, return the list
        if (string.IsNullOrWhiteSpace(character))
        {
            return new SgeLoginResult
            {
                Success    = true,
                Characters = characterNames
            };
        }

        // Step 5: Find the character key
        string characterKey = string.Empty;
        string lastPart     = string.Empty;
        foreach (var part in parts)
        {
            if (part.Equals(character, StringComparison.OrdinalIgnoreCase))
            {
                characterKey = lastPart;
                break;
            }
            lastPart = part;
        }

        if (string.IsNullOrEmpty(characterKey))
            return SgeLoginResult.Fail(
                $"Character '{character}' not found. Available: {string.Join(", ", characterNames)}");

        // Step 6: Request login key
        await WriteAsync(ssl, "L\t" + characterKey + "\tSTORM");
        var loginResponse = await ReadStringAsync(ssl, cancellationToken);

        // Parse: L\tOK\tGAMEHOST=...\tGAMEPORT=...\tKEY=...
        var fields = loginResponse.TrimEnd('\0').Split('\t');
        if (fields.Length < 2 || fields[1] != "OK")
            return SgeLoginResult.Fail($"Login key request failed: {loginResponse.Trim('\0', '\r', '\n')}");

        string gameHost = string.Empty;
        int    gamePort = 0;
        string loginKey = string.Empty;

        foreach (var field in fields)
        {
            if (field.StartsWith("GAMEHOST="))  gameHost = field[9..];
            else if (field.StartsWith("GAMEPORT=")) gamePort = int.Parse(field[9..]);
            else if (field.StartsWith("KEY="))    loginKey = field[4..];
        }

        if (string.IsNullOrEmpty(gameHost) || gamePort == 0 || string.IsNullOrEmpty(loginKey))
            return SgeLoginResult.Fail("Incomplete login response from eAccess.");

        return new SgeLoginResult
        {
            Success    = true,
            GameHost   = gameHost,
            GamePort   = gamePort,
            Key        = loginKey,
            Characters = characterNames
        };
    }

    private static byte[] EncryptPassword(byte[] key, string password)
    {
        var result = new byte[password.Length];
        for (int i = 0; i < password.Length; i++)
            result[i] = (byte)(((password[i] - 32) ^ key[i]) + 32);
        return result;
    }

    private static Task WriteAsync(SslStream ssl, string text)
    {
        var bytes = Encoding.ASCII.GetBytes(text);
        return ssl.WriteAsync(bytes).AsTask();
    }

    private static async Task<string> ReadStringAsync(SslStream ssl,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[BufferSize];
        var read   = await ssl.ReadAsync(buffer.AsMemory(), cancellationToken);
        return Encoding.ASCII.GetString(buffer, 0, read);
    }
}
