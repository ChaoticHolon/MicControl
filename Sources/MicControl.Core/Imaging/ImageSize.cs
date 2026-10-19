using System.Buffers.Binary;

namespace MicControl.Imaging;

/// <summary>
/// Reads an image's pixel size from its header (PNG, GIF, BMP, JPEG, ICO), so an icon whose decoded pixels would need
/// gigabytes (a "decompression bomb": a small PNG that claims 40000 × 40000) is refused before anything decodes it.
/// </summary>
public static class ImageSize
{
    /// <summary>The largest picture accepted as an icon: a 64-megapixel photo (256 MB once decoded).</summary>
    public const long MaxPixels = 64L * 1024 * 1024;

    private static ReadOnlySpan<byte> PngSignature => [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>The width and height the header declares, or <c>null</c> for a format this doesn't read.</summary>
    public static (long Width, long Height)? Read(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 24 && data[..8].SequenceEqual(PngSignature))
        {
            return (BinaryPrimitives.ReadUInt32BigEndian(data[16..]), BinaryPrimitives.ReadUInt32BigEndian(data[20..]));
        }

        if (data.Length >= 10 && (data[..6].SequenceEqual("GIF87a"u8) || data[..6].SequenceEqual("GIF89a"u8)))
        {
            return (BinaryPrimitives.ReadUInt16LittleEndian(data[6..]), BinaryPrimitives.ReadUInt16LittleEndian(data[8..]));
        }

        if (data.Length >= 26 && data[0] == 'B' && data[1] == 'M')
        {
            return (Math.Abs((long)BinaryPrimitives.ReadInt32LittleEndian(data[18..])), Math.Abs((long)BinaryPrimitives.ReadInt32LittleEndian(data[22..])));
        }

        if (data.Length >= 4 && data[0] == 0xFF && data[1] == 0xD8)
        {
            return Jpeg(data);
        }

        if (data.Length >= 6 && BinaryPrimitives.ReadUInt16LittleEndian(data) == 0 && BinaryPrimitives.ReadUInt16LittleEndian(data[2..]) == 1)
        {
            return Ico(data);
        }

        return null;
    }

    /// <summary>True when the header is readable and declares no more than <see cref="MaxPixels"/>, or the format is unknown.</summary>
    public static bool IsReasonable(ReadOnlySpan<byte> header) =>
        Read(header) is not { } size
        || size.Width is > 0 and <= MaxPixels && size.Height is > 0 and <= MaxPixels && size.Width * size.Height <= MaxPixels;

    // Markers until a start-of-frame (SOF0-SOF15, except DHT 0xC4, JPG 0xC8 and DAC 0xCC), which holds the size.
    private static (long, long)? Jpeg(ReadOnlySpan<byte> data)
    {
        var at = 2;
        while (at + 9 <= data.Length)
        {
            if (data[at] != 0xFF)
            {
                return null;
            }

            var marker = data[at + 1];
            if (marker == 0xFF)
            {
                at++;
                continue;
            }

            var length = BinaryPrimitives.ReadUInt16BigEndian(data[(at + 2)..]);
            if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
            {
                return (BinaryPrimitives.ReadUInt16BigEndian(data[(at + 7)..]), BinaryPrimitives.ReadUInt16BigEndian(data[(at + 5)..]));
            }

            if (length < 2)
            {
                return null;
            }

            at += 2 + length;
        }

        return null;
    }

    // An icon's entries are at most 256 × 256, but an entry can be a whole PNG with a size of its own: the largest wins.
    private static (long, long)? Ico(ReadOnlySpan<byte> data)
    {
        var count = BinaryPrimitives.ReadUInt16LittleEndian(data[4..]);
        (long Width, long Height) largest = (0, 0);
        for (var i = 0; i < count && 6 + (i + 1) * 16 <= data.Length; i++)
        {
            var entry = data.Slice(6 + i * 16, 16);
            (long, long) size = (entry[0] == 0 ? 256 : entry[0], entry[1] == 0 ? 256 : entry[1]);
            var offset = BinaryPrimitives.ReadUInt32LittleEndian(entry[12..]);
            // Only a PNG is read inside: an icon pointing at itself would otherwise recurse forever.
            if (offset < data.Length && data[(int)offset] == 0x89 && Read(data[(int)offset..]) is { } embedded)
            {
                size = embedded;
            }

            if (size.Item1 * size.Item2 > largest.Width * largest.Height)
            {
                largest = size;
            }
        }

        return largest;
    }
}
