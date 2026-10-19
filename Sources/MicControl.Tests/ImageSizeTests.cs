using System.Buffers.Binary;
using MicControl.Imaging;

namespace MicControl.Tests;

public sealed class ImageSizeTests
{
    internal static readonly byte[] OnePixelPng = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8DwHwAFBQIAX8jx0gAAAABJRU5ErkJggg==");

    /// <summary>A PNG header that claims a size its 1 pixel of data doesn't have: what a decompression bomb looks like.</summary>
    internal static byte[] PngClaiming(uint width, uint height)
    {
        var png = (byte[])OnePixelPng.Clone();
        BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(16), width);
        BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(20), height);
        return png;
    }

    [Fact]
    public void Read_KnownFormats()
    {
        byte[] gif = [.. "GIF89a"u8, 0x40, 0x01, 0xF0, 0x00, 0, 0];
        var bmp = new byte[54];
        (bmp[0], bmp[1]) = ((byte)'B', (byte)'M');
        BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(18), 800);
        BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(22), -600); // top-down
        byte[] jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0, 4, 0, 0, 0xFF, 0xC2, 0, 17, 8, 0x0F, 0xA0, 0x17, 0x70, 3, .. new byte[9]];
        byte[] ico = [0, 0, 1, 0, 2, 0, 16, 16, .. new byte[10], 0, 0, 0, 0, 0, 0, .. new byte[10], 38, 0, 0, 0, .. PngClaiming(512, 512)];

        Assert.Equal((1L, 1L), ImageSize.Read(OnePixelPng));
        Assert.Equal((320L, 240L), ImageSize.Read(gif));
        Assert.Equal((800L, 600L), ImageSize.Read(bmp));
        Assert.Equal((6000L, 4000L), ImageSize.Read(jpeg));
        Assert.Equal((512L, 512L), ImageSize.Read(ico));
        Assert.Null(ImageSize.Read("not an image"u8));
    }

    [Fact]
    public void IsReasonable_RefusesBombsAndEmptySizes_AcceptsPhotosAndUnknownFormats()
    {
        Assert.True(ImageSize.IsReasonable(PngClaiming(8000, 6000))); // a 48 MP phone photo
        Assert.False(ImageSize.IsReasonable(PngClaiming(40000, 40000)));
        Assert.False(ImageSize.IsReasonable(PngClaiming(0, 1)));
        Assert.False(ImageSize.IsReasonable(PngClaiming(uint.MaxValue, uint.MaxValue)));
        Assert.True(ImageSize.IsReasonable("RIFF....WEBP"u8));
    }

    [Fact]
    public void Read_IconThatPointsAtItself_DoesNotRecurse()
    {
        byte[] ico = [0, 0, 1, 0, 1, 0, 0, 0, .. new byte[10], 0, 0, 0, 0];

        Assert.Equal((256L, 256L), ImageSize.Read(ico));
    }

    [Fact]
    public void Read_MutatedHeaders_NeverThrow()
    {
        byte[] jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0, 16, .. new byte[14], 0xFF, 0xC0, 0, 17, 8, 0, 1, 0, 1, 3, .. new byte[9]];
        byte[] ico = [0, 0, 1, 0, 2, 0, .. new byte[12], 38, 0, 0, 0, .. new byte[12], 0, 0, 0, 0, .. OnePixelPng];
        byte[][] seeds = [OnePixelPng, [.. "GIF89a"u8, 1, 0, 1, 0], [(byte)'B', (byte)'M', .. new byte[52]], jpeg, ico];
        var r = new Random(31);
        for (var i = 0; i < 20_000; i++)
        {
            var b = (byte[])seeds[r.Next(seeds.Length)].Clone();
            for (var n = r.Next(1, 6); n > 0; n--)
            {
                if (r.Next(3) == 0)
                {
                    Array.Resize(ref b, r.Next(1, b.Length + 1));
                }
                else
                {
                    b[r.Next(b.Length)] = r.Next(2) == 0 ? (byte)r.Next(256) : new byte[] { 0, 0xFF, 0x89, 0xC0 }[r.Next(4)];
                }
            }

            ImageSize.IsReasonable(b);
        }
    }
}
