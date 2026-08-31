using SkiaSharp;

namespace Fabolus.Tests.Diagnostics;

/// <summary>An 8-bit colour. Kept separate from <see cref="SKColor"/> so the inner loops of the
/// rasterizer never touch Skia - only the encode and the 2D annotation do.</summary>
internal readonly record struct Rgb(byte R, byte G, byte B)
{
    public Rgb Scale(float k) => new(Channel(R * k), Channel(G * k), Channel(B * k));

    public static Rgb Lerp(Rgb a, Rgb b, float t) => new(
        Channel(a.R + ((b.R - a.R) * t)),
        Channel(a.G + ((b.G - a.G) * t)),
        Channel(a.B + ((b.B - a.B) * t)));

    public SKColor ToSKColor() => new(R, G, B);

    private static byte Channel(float v) => (byte)Math.Clamp(MathF.Round(v), 0f, 255f);
}

/// <summary>
/// A colour buffer with a matching depth buffer.
///
/// <para>
/// The depth buffer is why this exists rather than a Skia canvas. Drawing a ridge contour correctly
/// means knowing whether the surface it describes is in front of it at that pixel, and no 2D canvas
/// can answer that. Skia is used for what it is good at - encoding, text, and compositing tiles into
/// a sheet - and everything with a Z in it happens here.
/// </para>
/// </summary>
internal sealed class Raster
{
    public int Width { get; }
    public int Height { get; }

    /// <summary>Row-major RGB triples, top row first.</summary>
    public byte[] Pixels { get; }

    /// <summary>View-space depth per pixel; larger is further from the camera.</summary>
    public float[] Depth { get; }

    public Raster(int width, int height, Rgb background)
    {
        Width = width;
        Height = height;
        Pixels = new byte[width * height * 3];
        Depth = new float[width * height];

        Array.Fill(Depth, float.PositiveInfinity);
        for (int i = 0; i < Pixels.Length; i += 3)
        {
            Pixels[i] = background.R;
            Pixels[i + 1] = background.G;
            Pixels[i + 2] = background.B;
        }
    }

    public bool Contains(int x, int y) => (uint)x < (uint)Width && (uint)y < (uint)Height;

    public void Set(int x, int y, Rgb colour)
    {
        if (!Contains(x, y)) return;
        int i = ((y * Width) + x) * 3;
        Pixels[i] = colour.R;
        Pixels[i + 1] = colour.G;
        Pixels[i + 2] = colour.B;
    }

    public Rgb Get(int x, int y)
    {
        int i = ((y * Width) + x) * 3;
        return new Rgb(Pixels[i], Pixels[i + 1], Pixels[i + 2]);
    }

    public void Blend(int x, int y, Rgb colour, float alpha)
    {
        if (!Contains(x, y)) return;
        Set(x, y, Rgb.Lerp(Get(x, y), colour, Math.Clamp(alpha, 0f, 1f)));
    }

    /// <summary>Non-background pixel count, for the rasterizer's own sanity checks.</summary>
    public int CountDrawn()
    {
        int count = 0;
        for (int i = 0; i < Depth.Length; i++)
            if (!float.IsPositiveInfinity(Depth[i])) count++;
        return count;
    }

    public SKBitmap ToBitmap()
    {
        var bitmap = new SKBitmap(Width, Height, SKColorType.Rgba8888, SKAlphaType.Opaque);
        var rgba = new byte[Width * Height * 4];
        for (int p = 0, s = 0; p < rgba.Length; p += 4, s += 3)
        {
            rgba[p] = Pixels[s];
            rgba[p + 1] = Pixels[s + 1];
            rgba[p + 2] = Pixels[s + 2];
            rgba[p + 3] = 255;
        }

        System.Runtime.InteropServices.Marshal.Copy(rgba, 0, bitmap.GetPixels(), rgba.Length);
        return bitmap;
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var bitmap = ToBitmap();
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(path);
        data.SaveTo(stream);
    }
}

/// <summary>One labelled tile in a contact sheet.</summary>
internal sealed record Tile(string Label, Raster Image, string? Note = null);

/// <summary>Composites rendered views into one labelled sheet, and writes legends onto them.</summary>
internal static class ContactSheet
{
    public static void Save(
        string path, IReadOnlyList<Tile> tiles, int columns, int tileSize,
        Rgb background, string title, IReadOnlyList<(string Text, Rgb Colour)> legend)
    {
        int rows = (int)Math.Ceiling(tiles.Count / (double)columns);
        const int headerHeight = 62;
        const int labelHeight = 26;
        const int pad = 6;

        int width = (columns * tileSize) + ((columns + 1) * pad);
        int height = headerHeight + (rows * (tileSize + labelHeight)) + ((rows + 1) * pad);

        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Opaque);
        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;
        canvas.Clear(background.ToSKColor());

        using var text = new SKPaint { Color = new SKColor(235, 237, 240), IsAntialias = true };
        using var titleFont = new SKFont(SKTypeface.Default, 26f);
        using var labelFont = new SKFont(SKTypeface.Default, 17f);
        using var noteFont = new SKFont(SKTypeface.Default, 14f);

        canvas.DrawText(title, pad + 4, 32, SKTextAlign.Left, titleFont, text);

        // Legend along the header, so a sheet is readable without the report beside it.
        float x = pad + 4;
        using var swatch = new SKPaint { IsAntialias = true };
        foreach (var (entry, colour) in legend)
        {
            swatch.Color = colour.ToSKColor();
            canvas.DrawRect(x, 44, 14, 14, swatch);
            canvas.DrawText(entry, x + 20, 56, SKTextAlign.Left, noteFont, text);
            x += 26 + noteFont.MeasureText(entry) + 18;
        }

        for (int i = 0; i < tiles.Count; i++)
        {
            int column = i % columns;
            int row = i / columns;
            float left = pad + (column * (tileSize + pad));
            float top = headerHeight + pad + (row * (tileSize + labelHeight + pad));

            using var bitmap = tiles[i].Image.ToBitmap();
            canvas.DrawImage(SKImage.FromBitmap(bitmap),
                new SKRect(left, top, left + tileSize, top + tileSize),
                new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));

            canvas.DrawText(tiles[i].Label, left + 2, top + tileSize + 17, SKTextAlign.Left, labelFont, text);
            if (tiles[i].Note is { Length: > 0 } note)
                canvas.DrawText(note, left + tileSize - 2, top + tileSize + 17, SKTextAlign.Right, noteFont, text);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(path);
        data.SaveTo(stream);
    }
}
