// Generates MicControl's app, window and tray icons: the Home page's status badge (a green "live" or red "muted"
// circle with a white microphone outline) drawn from simple shapes, so they are ours to ship.
// Run from the repository root:  dotnet run tools/GenerateAppIcons.cs
// Optional argument: output folder for a preview sheet (icon-preview.png); by default no preview is written.
//
// Outputs (never edit them by hand; change this generator and run it again):
// - Sources/MicControl.UI/Assets/microphoneEnabled.ico, microphoneDisabled.ico: 16-256 px, for the window and tray;
//   the exe (and so its shortcut and Apps entry) uses the live one
// - Sources/MicControl.UI/Assets/microphoneEnabled.png, microphoneDisabled.png: 256 px, for the sidebar

#:package SkiaSharp@3.119.4
#:package SkiaSharp.NativeAssets.Linux@3.119.4

using SkiaSharp;

var live = new SKColor(0x0F, 0x7B, 0x0F);
var muted = new SKColor(0xC4, 0x2B, 0x1C);
var assets = Path.Combine("Sources", "MicControl.UI", "Assets");
int[] icoSizes = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256];

foreach (var (name, color, slash) in new[] { ("microphoneEnabled", live, false), ("microphoneDisabled", muted, true) })
{
    File.WriteAllBytes(Path.Combine(assets, name + ".png"), Icon.Png(256, color, slash));
    Ico.Write(Path.Combine(assets, name + ".ico"), icoSizes.Select(s => (s, Icon.Png(s, color, slash))).ToList());
    Console.WriteLine(name);
}

if (args.Length > 0)
{
    // One row per state, at the sizes Windows shows (tray 16-24, taskbar 24-48, Start and Explorer up to 256).
    int[] preview = [16, 20, 24, 32, 48, 64, 128, 256];
    using var sheet = new SKBitmap(preview.Sum() + 20 * (preview.Length + 1), 2 * 256 + 60);
    using var canvas = new SKCanvas(sheet);
    canvas.Clear(new SKColor(0xF3, 0xF3, 0xF3));
    for (var row = 0; row < 2; row++)
    {
        var x = 20;
        foreach (var size in preview)
        {
            using var image = SKImage.FromEncodedData(Icon.Png(size, row == 0 ? live : muted, row == 1));
            canvas.DrawImage(image, x, 20 + row * 276);
            x += size + 20;
        }
    }

    using var data = sheet.Encode(SKEncodedImageFormat.Png, 100);
    File.WriteAllBytes(Path.Combine(args[0], "icon-preview.png"), data.ToArray());
}

static class Icon
{
    /// <summary>
    /// Draws the badge at <paramref name="size"/> px. Shapes are laid out on a 256-unit grid; small sizes get thicker
    /// strokes so the microphone stays readable in the tray.
    /// </summary>
    public static byte[] Png(int size, SKColor color, bool slash)
    {
        using var bitmap = new SKBitmap(size, size, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        canvas.Scale(size / 256f);

        using var fill = new SKPaint { IsAntialias = true, Color = color, Style = SKPaintStyle.Fill };
        canvas.DrawCircle(128, 128, 128, fill);

        var stroke = size switch { <= 20 => 26f, <= 32 => 22f, <= 48 => 19f, _ => 16f };
        using var pen = new SKPaint
        {
            IsAntialias = true, Color = SKColors.White, Style = SKPaintStyle.Stroke, StrokeWidth = stroke,
            StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round,
        };

        // Tray sizes get a solid capsule and a slightly larger glyph: an outline turns to mush at 16-24 px.
        var small = size <= 32;
        if (small)
        {
            canvas.Scale(1.1f, 1.1f, 128, 128);
        }

        // Capsule, the U-shaped holder under it, and the stem.
        using (var solid = new SKPaint { IsAntialias = true, Color = SKColors.White, Style = SKPaintStyle.Fill })
        {
            if (small)
            {
                // Narrower than the outline's outer edge, so a gap stays between the capsule and the holder.
                canvas.DrawRoundRect(new SKRect(103, 40, 153, 142), 25, 25, solid);
            }
            else
            {
                canvas.DrawRoundRect(new SKRect(100, 52, 156, 146), 28, 28, pen);
            }
        }

        using (var holder = new SKPath())
        {
            holder.MoveTo(76, 118);
            holder.ArcTo(new SKRect(76, 66, 180, 170), 180, -180, false);
            canvas.DrawPath(holder, pen);
        }

        canvas.DrawLine(128, 170, 128, 202, pen);

        if (slash)
        {
            // Cut a gap in the badge colour first, so the slash reads as crossing over the microphone.
            using var gap = new SKPaint
            {
                IsAntialias = true, Color = color, Style = SKPaintStyle.Stroke, StrokeWidth = stroke * (small ? 1.5f : 1.9f),
                StrokeCap = SKStrokeCap.Round,
            };
            canvas.DrawLine(74, 66, 182, 190, gap);
            canvas.DrawLine(74, 66, 182, 190, pen);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}

static class Ico
{
    /// <summary>Writes a Windows .ico with PNG-compressed images (Windows Vista and later read these).</summary>
    public static void Write(string path, IReadOnlyList<(int Size, byte[] Png)> images)
    {
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)images.Count);
        var offset = 6 + 16 * images.Count;
        foreach (var (size, png) in images)
        {
            writer.Write((byte)(size >= 256 ? 0 : size));
            writer.Write((byte)(size >= 256 ? 0 : size));
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            writer.Write(png.Length);
            writer.Write(offset);
            offset += png.Length;
        }

        foreach (var (_, png) in images)
        {
            writer.Write(png);
        }
    }
}
