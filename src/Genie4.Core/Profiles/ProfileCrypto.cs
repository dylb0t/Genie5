using System.Security.Cryptography;
using System.Text;

namespace Genie4.Core.Profiles;

/// <summary>
/// AES-256-GCM encryption for profile passwords, keyed to the current machine + user account.
/// Passwords cannot be decrypted on a different machine or user account.
/// </summary>
public static class ProfileCrypto
{
    private static readonly byte[] AppSalt = Encoding.UTF8.GetBytes("Genie5_ProfileSalt_v1_3f8a2c");
    private const int NonceSize = 12;
    private const int TagSize   = 16;
    private const int Iterations = 100_000;

    private static byte[] DeriveKey()
    {
        var keyMaterial = Encoding.UTF8.GetBytes(
            Environment.MachineName + "|" + Environment.UserName);
        using var pbkdf2 = new Rfc2898DeriveBytes(
            keyMaterial, AppSalt, Iterations, HashAlgorithmName.SHA256);
        return pbkdf2.GetBytes(32);
    }

    public static string Encrypt(string plainText)
    {
        if (string.IsNullOrEmpty(plainText)) return string.Empty;

        var key       = DeriveKey();
        var nonce     = RandomNumberGenerator.GetBytes(NonceSize);
        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var cipher    = new byte[plainBytes.Length];
        var tag       = new byte[TagSize];

        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plainBytes, cipher, tag);

        // Layout: nonce | tag | ciphertext
        var blob = new byte[NonceSize + TagSize + cipher.Length];
        Buffer.BlockCopy(nonce,  0, blob, 0,                     NonceSize);
        Buffer.BlockCopy(tag,    0, blob, NonceSize,             TagSize);
        Buffer.BlockCopy(cipher, 0, blob, NonceSize + TagSize,   cipher.Length);

        return Convert.ToBase64String(blob);
    }

    public static string Decrypt(string encryptedBase64)
    {
        if (string.IsNullOrEmpty(encryptedBase64)) return string.Empty;

        try
        {
            var blob = Convert.FromBase64String(encryptedBase64);
            if (blob.Length < NonceSize + TagSize) return string.Empty;

            var nonce      = blob[..NonceSize];
            var tag        = blob[NonceSize..(NonceSize + TagSize)];
            var cipher     = blob[(NonceSize + TagSize)..];
            var plain      = new byte[cipher.Length];
            var key        = DeriveKey();

            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(nonce, cipher, tag, plain);

            return Encoding.UTF8.GetString(plain);
        }
        catch
        {
            // Wrong machine/user or corrupted data
            return string.Empty;
        }
    }
}
