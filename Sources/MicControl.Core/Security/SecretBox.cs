using System.Security.Cryptography;

namespace MicControl.Security;

public enum SecretBoxAlgorithm : byte
{
    ChaCha20Poly1305 = 1,
    Aes256Gcm = 2,
}

/// <summary>
/// Authenticated encryption for local data: ChaCha20-Poly1305 where the OS has it (Windows 11, Linux), otherwise
/// AES-256-GCM (Windows 10, macOS). Sealed layout: algorithm (1 byte) · nonce (12) · ciphertext · tag (16). Opening
/// reads the algorithm byte, so data sealed on one machine opens on another that has that algorithm.
/// </summary>
public static class SecretBox
{
    public const int KeySize = 32;
    public const int NonceSize = 12;
    public const int TagSize = 16;
    public const int Overhead = 1 + NonceSize + TagSize;

    public static SecretBoxAlgorithm PreferredAlgorithm =>
        ChaCha20Poly1305.IsSupported ? SecretBoxAlgorithm.ChaCha20Poly1305 : SecretBoxAlgorithm.Aes256Gcm;

    public static bool IsSupported(SecretBoxAlgorithm algorithm) => algorithm switch
    {
        SecretBoxAlgorithm.ChaCha20Poly1305 => ChaCha20Poly1305.IsSupported,
        SecretBoxAlgorithm.Aes256Gcm => AesGcm.IsSupported,
        _ => false,
    };

    public static byte[] NewKey() => RandomNumberGenerator.GetBytes(KeySize);

    /// <param name="associatedData">Authenticated but not encrypted (e.g. a file name or format version); opening needs the same.</param>
    public static byte[] Seal(ReadOnlySpan<byte> key, ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> associatedData = default, SecretBoxAlgorithm? algorithm = null)
    {
        var chosen = algorithm ?? PreferredAlgorithm;
        Check(chosen, key);
        var sealedData = new byte[Overhead + plaintext.Length];
        sealedData[0] = (byte)chosen;
        var nonce = sealedData.AsSpan(1, NonceSize);
        var ciphertext = sealedData.AsSpan(1 + NonceSize, plaintext.Length);
        var tag = sealedData.AsSpan(1 + NonceSize + plaintext.Length);
        RandomNumberGenerator.Fill(nonce);
        if (chosen == SecretBoxAlgorithm.ChaCha20Poly1305)
        {
            using var cipher = new ChaCha20Poly1305(key);
            cipher.Encrypt(nonce, plaintext, ciphertext, tag, associatedData);
        }
        else
        {
            using var cipher = new AesGcm(key, TagSize);
            cipher.Encrypt(nonce, plaintext, ciphertext, tag, associatedData);
        }

        return sealedData;
    }

    /// <exception cref="AuthenticationTagMismatchException">Wrong key, changed data or different associated data.</exception>
    /// <exception cref="CryptographicException">Not sealed data, or sealed with an algorithm this OS lacks.</exception>
    public static byte[] Open(ReadOnlySpan<byte> key, ReadOnlySpan<byte> sealedData, ReadOnlySpan<byte> associatedData = default)
    {
        if (sealedData.Length < Overhead || !Enum.IsDefined((SecretBoxAlgorithm)sealedData[0]))
        {
            throw new CryptographicException("This isn't data sealed by SecretBox.");
        }

        var algorithm = (SecretBoxAlgorithm)sealedData[0];
        Check(algorithm, key);
        var nonce = sealedData.Slice(1, NonceSize);
        var ciphertext = sealedData[(1 + NonceSize)..^TagSize];
        var tag = sealedData[^TagSize..];
        var plaintext = new byte[ciphertext.Length];
        if (algorithm == SecretBoxAlgorithm.ChaCha20Poly1305)
        {
            using var cipher = new ChaCha20Poly1305(key);
            cipher.Decrypt(nonce, ciphertext, tag, plaintext, associatedData);
        }
        else
        {
            using var cipher = new AesGcm(key, TagSize);
            cipher.Decrypt(nonce, ciphertext, tag, plaintext, associatedData);
        }

        return plaintext;
    }

    private static void Check(SecretBoxAlgorithm algorithm, ReadOnlySpan<byte> key)
    {
        if (key.Length != KeySize)
        {
            throw new ArgumentException($"The key must be {KeySize} bytes.", nameof(key));
        }

        if (!IsSupported(algorithm))
        {
            throw new CryptographicException($"{algorithm} isn't available on this system.");
        }
    }
}
