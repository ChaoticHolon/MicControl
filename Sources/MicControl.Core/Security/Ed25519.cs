using System.Numerics;
using System.Security.Cryptography;

namespace MicControl.Security;

/// <summary>
/// Ed25519 signature verification (RFC 8032, section 5.1.7), which .NET doesn't provide. Verify only: it handles
/// public data, so it needs no constant-time arithmetic. Signing stays with <c>ssh-keygen</c>, which holds the key.
/// </summary>
public static class Ed25519
{
    public const int PublicKeySize = 32;
    public const int SignatureSize = 64;

    private static readonly BigInteger P = BigInteger.Pow(2, 255) - 19;
    private static readonly BigInteger L = BigInteger.Pow(2, 252) + BigInteger.Parse("27742317777372353535851937790883648493", System.Globalization.CultureInfo.InvariantCulture);
    private static readonly BigInteger D = Mod(-121665 * Inverse(121666));
    private static readonly BigInteger SqrtMinusOne = BigInteger.ModPow(2, (P - 1) / 4, P);
    private static readonly Point Identity = new(0, 1, 1, 0);
    private static readonly Point Base = CreateBase();

    public static bool Verify(ReadOnlySpan<byte> publicKey, ReadOnlySpan<byte> message, ReadOnlySpan<byte> signature)
    {
        if (publicKey.Length != PublicKeySize || signature.Length != SignatureSize
            || Decompress(publicKey) is not { } a || Decompress(signature[..32]) is not { } r)
        {
            return false;
        }

        var s = ToInteger(signature[32..]);
        if (s >= L)
        {
            return false;
        }

        var hashInput = new byte[64 + message.Length];
        signature[..32].CopyTo(hashInput);
        publicKey.CopyTo(hashInput.AsSpan(32));
        message.CopyTo(hashInput.AsSpan(64));
        var h = ToInteger(SHA512.HashData(hashInput)) % L;

        return AreEqual(Multiply(s, Base), Add(r, Multiply(h, a)));
    }

    private static Point CreateBase()
    {
        var y = Mod(4 * Inverse(5));
        return new Point(RecoverX(y, 0)!.Value, y, 1, Mod(RecoverX(y, 0)!.Value * y));
    }

    private static Point? Decompress(ReadOnlySpan<byte> encoded)
    {
        var y = ToInteger(encoded);
        var sign = (int)(y >> 255);
        y &= (BigInteger.One << 255) - 1;
        return RecoverX(y, sign) is { } x ? new Point(x, y, 1, Mod(x * y)) : null;
    }

    private static BigInteger? RecoverX(BigInteger y, int sign)
    {
        if (y >= P)
        {
            return null;
        }

        var x2 = Mod((y * y - 1) * Inverse(D * y * y + 1));
        if (x2.IsZero)
        {
            return sign == 0 ? BigInteger.Zero : null;
        }

        var x = BigInteger.ModPow(x2, (P + 3) / 8, P);
        if (!Mod(x * x - x2).IsZero)
        {
            x = Mod(x * SqrtMinusOne);
        }

        if (!Mod(x * x - x2).IsZero)
        {
            return null;
        }

        return (int)(x & 1) != sign ? P - x : x;
    }

    // Extended coordinates: x = X/Z, y = Y/Z, x*y = T/Z.
    private static Point Add(Point p, Point q)
    {
        var a = Mod((p.Y - p.X) * (q.Y - q.X));
        var b = Mod((p.Y + p.X) * (q.Y + q.X));
        var c = Mod(2 * p.T * q.T * D);
        var d = Mod(2 * p.Z * q.Z);
        var (e, f, g, h) = (b - a, d - c, d + c, b + a);
        return new Point(Mod(e * f), Mod(g * h), Mod(f * g), Mod(e * h));
    }

    private static Point Multiply(BigInteger scalar, Point point)
    {
        var result = Identity;
        while (scalar > 0)
        {
            if (!scalar.IsEven)
            {
                result = Add(result, point);
            }

            point = Add(point, point);
            scalar >>= 1;
        }

        return result;
    }

    private static bool AreEqual(Point p, Point q) =>
        Mod(p.X * q.Z - q.X * p.Z).IsZero && Mod(p.Y * q.Z - q.Y * p.Z).IsZero;

    private static BigInteger ToInteger(ReadOnlySpan<byte> littleEndian) => new(littleEndian, isUnsigned: true, isBigEndian: false);

    private static BigInteger Inverse(BigInteger value) => BigInteger.ModPow(Mod(value), P - 2, P);

    private static BigInteger Mod(BigInteger value)
    {
        var result = value % P;
        return result.Sign < 0 ? result + P : result;
    }

    private readonly record struct Point(BigInteger X, BigInteger Y, BigInteger Z, BigInteger T);
}
