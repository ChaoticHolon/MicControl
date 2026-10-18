using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace MicControl.Security;

/// <summary>
/// Checks signatures made by <c>ssh-keygen -Y sign</c> (OpenSSH's SSHSIG format) with an Ed25519 key, so a file
/// signed on the developer's PC can be verified anywhere without OpenSSH. Same result as
/// <c>ssh-keygen -Y verify</c> against an allowed-signers list of <paramref name="trustedKeys"/>.
/// </summary>
public static class SshSignature
{
    private const string Armor = "SSH SIGNATURE";
    private const string KeyType = "ssh-ed25519";
    private static readonly byte[] Magic = "SSHSIG"u8.ToArray();

    /// <param name="armored">The <c>.sig</c> file: <c>-----BEGIN SSH SIGNATURE-----</c> … <c>-----END SSH SIGNATURE-----</c>.</param>
    /// <param name="namespace">What the signature is for (<c>ssh-keygen -n</c>), so one made for something else is refused.</param>
    /// <param name="trustedKeys">Public keys as <c>ssh-keygen</c> prints them: <c>ssh-ed25519 AAAA… comment</c>.</param>
    public static bool Verify(ReadOnlySpan<byte> message, string armored, string @namespace, IEnumerable<string> trustedKeys)
    {
        ArgumentNullException.ThrowIfNull(trustedKeys);
        if (Decode(armored) is not { } blob)
        {
            return false;
        }

        var reader = new Reader(blob);
        if (!reader.TryRaw(Magic.Length, out var magic) || !magic.SequenceEqual(Magic)
            || !reader.TryUInt32(out var version) || version != 1
            || !reader.TryString(out var publicKey)
            || !reader.TryString(out var signedNamespace) || !signedNamespace.SequenceEqual(Encoding.UTF8.GetBytes(@namespace))
            || !reader.TryString(out var reserved)
            || !reader.TryString(out var hashName)
            || !reader.TryString(out var signatureBlob) || !reader.IsAtEnd)
        {
            return false;
        }

        var publicKeyBytes = publicKey.ToArray();
        if (!trustedKeys.Any(k => ParsePublicKey(k) is { } trusted && trusted.AsSpan().SequenceEqual(publicKeyBytes))
            || KeyBytes(publicKey) is not { } key || SignatureBytes(signatureBlob) is not { } signature)
        {
            return false;
        }

        byte[] digest;
        switch (Encoding.ASCII.GetString(hashName))
        {
            case "sha512":
                digest = SHA512.HashData(message);
                break;
            case "sha256":
                digest = SHA256.HashData(message);
                break;
            default:
                return false;
        }

        var signed = new MemoryStream();
        signed.Write(Magic);
        WriteString(signed, signedNamespace);
        WriteString(signed, reserved);
        WriteString(signed, hashName);
        WriteString(signed, digest);
        return Ed25519.Verify(key, signed.ToArray(), signature);
    }

    /// <summary>The key blob of an <c>ssh-ed25519 AAAA… comment</c> line, or <c>null</c> if it isn't one.</summary>
    public static byte[]? ParsePublicKey(string line)
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2 || parts[0] != KeyType)
        {
            return null;
        }

        try
        {
            var blob = Convert.FromBase64String(parts[1]);
            return KeyBytes(blob) is null ? null : blob;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static byte[]? Decode(string armored)
    {
        var lines = armored.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length < 3 || lines[0] != $"-----BEGIN {Armor}-----" || lines[^1] != $"-----END {Armor}-----")
        {
            return null;
        }

        try
        {
            return Convert.FromBase64String(string.Concat(lines[1..^1]));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    // string "ssh-ed25519", string key (32 bytes)
    private static byte[]? KeyBytes(ReadOnlySpan<byte> blob)
    {
        var reader = new Reader(blob);
        return reader.TryString(out var type) && type.SequenceEqual(Encoding.ASCII.GetBytes(KeyType))
            && reader.TryString(out var key) && key.Length == Ed25519.PublicKeySize && reader.IsAtEnd
            ? key.ToArray()
            : null;
    }

    // string "ssh-ed25519", string signature (64 bytes)
    private static byte[]? SignatureBytes(ReadOnlySpan<byte> blob)
    {
        var reader = new Reader(blob);
        return reader.TryString(out var type) && type.SequenceEqual(Encoding.ASCII.GetBytes(KeyType))
            && reader.TryString(out var signature) && signature.Length == Ed25519.SignatureSize && reader.IsAtEnd
            ? signature.ToArray()
            : null;
    }

    private static void WriteString(Stream stream, ReadOnlySpan<byte> value)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, (uint)value.Length);
        stream.Write(length);
        stream.Write(value);
    }

    // SSH wire format (RFC 4251): uint32 big-endian, and strings as a uint32 length then the bytes.
    private ref struct Reader(ReadOnlySpan<byte> data)
    {
        private ReadOnlySpan<byte> rest = data;

        public readonly bool IsAtEnd => rest.IsEmpty;

        public bool TryRaw(int length, out ReadOnlySpan<byte> value)
        {
            if (length < 0 || length > rest.Length)
            {
                value = default;
                return false;
            }

            value = rest[..length];
            rest = rest[length..];
            return true;
        }

        public bool TryUInt32(out uint value)
        {
            value = 0;
            if (!TryRaw(4, out var bytes))
            {
                return false;
            }

            value = BinaryPrimitives.ReadUInt32BigEndian(bytes);
            return true;
        }

        public bool TryString(out ReadOnlySpan<byte> value)
        {
            value = default;
            return TryUInt32(out var length) && length <= int.MaxValue && TryRaw((int)length, out value);
        }
    }
}
