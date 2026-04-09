using System;
using System.Collections.Concurrent;
using Godot;
using SkiaSharp;

namespace GodotNodeExtension.Component.GodotSkia;

/// <summary>
/// Provides conversion utilities between Skia types and Godot types
/// </summary>
public static class SkiaGodotConverter
{
    #region Color Conversion

    /// <summary>
    /// Convert Godot Color to Skia SKColor
    /// </summary>
    public static SKColor ToSKColor(this Color godotColor)
    {
        return new SKColor(
            (byte)(godotColor.R * 255),
            (byte)(godotColor.G * 255),
            (byte)(godotColor.B * 255),
            (byte)(godotColor.A * 255)
        );
    }

    /// <summary>
    /// Convert Skia SKColor to Godot Color
    /// </summary>
    public static Color ToGodotColor(this SKColor skColor)
    {
        return new Color(
            skColor.Red / 255f,
            skColor.Green / 255f,
            skColor.Blue / 255f,
            skColor.Alpha / 255f
        );
    }

    #endregion

    #region Vector Conversion

    /// <summary>
    /// Convert Godot Vector2 to Skia SKPoint
    /// </summary>
    public static SKPoint ToSKPoint(this Vector2 vector)
    {
        return new SKPoint(vector.X, vector.Y);
    }

    /// <summary>
    /// Convert Skia SKPoint to Godot Vector2
    /// </summary>
    public static Vector2 ToVector2(this SKPoint point)
    {
        return new Vector2(point.X, point.Y);
    }

    /// <summary>
    /// Convert Godot Vector2 to Skia SKSize
    /// </summary>
    public static SKSize ToSKSize(this Vector2 vector)
    {
        return new SKSize(vector.X, vector.Y);
    }

    /// <summary>
    /// Convert Skia SKSize to Godot Vector2
    /// </summary>
    public static Vector2 ToVector2(this SKSize size)
    {
        return new Vector2(size.Width, size.Height);
    }

    #endregion

    #region Rect Conversion

    /// <summary>
    /// Convert Godot Rect2 to Skia SKRect
    /// </summary>
    public static SKRect ToSKRect(this Rect2 rect)
    {
        return new SKRect(
            rect.Position.X,
            rect.Position.Y,
            rect.Position.X + rect.Size.X,
            rect.Position.Y + rect.Size.Y
        );
    }

    /// <summary>
    /// Convert Skia SKRect to Godot Rect2
    /// </summary>
    public static Rect2 ToRect2(this SKRect rect)
    {
        return new Rect2(
            rect.Left,
            rect.Top,
            rect.Width,
            rect.Height
        );
    }

    #endregion

    #region Font Conversion

    // Thread-safe typeface cache: Godot Font instance ID → SKTypeface
    private static readonly ConcurrentDictionary<ulong, SKTypeface> s_typefaceCache = new();

    /// <summary>
    /// Converts a Godot <see cref="Font"/> to a Skia <see cref="SKTypeface"/>.
    /// Supports <see cref="FontFile"/>, <see cref="FontVariation"/> (unwraps chain),
    /// and <see cref="SystemFont"/>. Results are cached by Godot instance ID.
    /// Thread-safe.
    /// </summary>
    /// <param name="godotFont">The Godot font to convert.</param>
    /// <returns>A cached <see cref="SKTypeface"/>. Do NOT dispose — owned by the cache.</returns>
    public static SKTypeface ToSKTypeface(this Font godotFont)
    {
        var key = godotFont.GetInstanceId();
        if (s_typefaceCache.TryGetValue(key, out var cached))
            return cached;

        var typeface = CreateTypefaceFromGodotFont(godotFont);
        s_typefaceCache[key] = typeface;
        return typeface;
    }

    /// <summary>
    /// Converts a Godot <see cref="Font"/> to a Skia <see cref="SKFont"/>.
    /// Supports <see cref="FontFile"/>, <see cref="FontVariation"/> (unwraps chain),
    /// and <see cref="SystemFont"/>.
    /// Falls back to the Godot default font typeface if the specified font cannot be loaded.
    /// The returned font uses Full hinting and SubpixelAntiAlias edging to match
    /// Godot's FreeType rendering appearance.
    /// </summary>
    /// <param name="godotFont">The Godot font to convert.</param>
    /// <param name="size">Font size in pixels.</param>
    public static SKFont ToSKFont(this Font godotFont, float size = 16f)
    {
        var typeface = godotFont.ToSKTypeface();
        return ConfigureFont(new SKFont(typeface, size));
    }

    /// <summary>
    /// Apply standard font rendering settings to match Godot's text appearance.
    /// Uses Full hinting for crisp glyph shapes and SubpixelAntiAlias edging
    /// for smooth rendering with subpixel positioning.
    /// </summary>
    /// <param name="font">The SKFont to configure.</param>
    /// <returns>The same font instance (for chaining).</returns>
    public static SKFont ConfigureFont(SKFont font)
    {
        font.Hinting = SKFontHinting.Full;
        font.Edging = SKFontEdging.SubpixelAntialias;
        font.Subpixel = true;
        return font;
    }

    /// <summary>
    /// Extract font data from a Godot Font and create an SKTypeface.
    /// Handles FontVariation chain unwrapping, FontFile data extraction,
    /// SystemFont family lookup, and Godot default font fallback.
    /// </summary>
    private static SKTypeface CreateTypefaceFromGodotFont(Font godotFont)
    {
        // Determine style from FontVariation properties
        var weight = SKFontStyleWeight.Normal;
        var slant = SKFontStyleSlant.Upright;

        var source = godotFont;
        while (source is FontVariation fv)
        {
            // Detect bold via embolden
            if (fv.VariationEmbolden > 0.1f)
                weight = SKFontStyleWeight.Bold;

            // Detect italic via transform skew
            var transform = fv.VariationTransform;
            if (Math.Abs(transform.Y.X) > 0.05f)
                slant = SKFontStyleSlant.Italic;

            source = fv.BaseFont;
        }

        // Try to extract raw font data from FontFile
        if (source is FontFile ff)
        {
            var data = ff.Data;
            if (data is { Length: > 0 })
            {
                var skData = SKData.CreateCopy(data);
                var tf = SKTypeface.FromData(skData);
                if (tf != null) return tf;
            }
        }

        // Try SystemFont family names
        if (source is SystemFont sf && sf.FontNames.Length > 0)
        {
            return SKTypeface.FromFamilyName(
                sf.FontNames[0], weight, SKFontStyleWidth.Normal, slant);
        }

        // Fallback: try to extract font data from the Godot default font
        // (ThemeDB.FallbackFont is typically "Noto Sans" embedded in Godot)
        var fallbackFont = ThemeDB.FallbackFont;
        if (fallbackFont != null && fallbackFont.GetInstanceId() != godotFont.GetInstanceId())
        {
            var unwrapped = fallbackFont;
            while (unwrapped is FontVariation fv2)
                unwrapped = fv2.BaseFont;

            if (unwrapped is FontFile fallbackFf)
            {
                var data = fallbackFf.Data;
                if (data is { Length: > 0 })
                {
                    var skData = SKData.CreateCopy(data);
                    var tf = SKTypeface.FromData(skData);
                    if (tf != null) return tf;
                }
            }
        }

        // Last resort: use SKFontManager to find a CJK-capable font
        var style = new SKFontStyle(weight, SKFontStyleWidth.Normal, slant);
        var cjkFallback = SKFontManager.Default.MatchCharacter(null, style, null, '中');
        return cjkFallback ?? SKTypeface.FromFamilyName(null, weight, SKFontStyleWidth.Normal, slant);
    }

    /// <summary>
    /// Clear the typeface cache. Call when fonts are changed or on application exit.
    /// </summary>
    public static void ClearTypefaceCache()
    {
        foreach (var kvp in s_typefaceCache)
            kvp.Value.Dispose();
        s_typefaceCache.Clear();
    }

    /// <summary>
    /// Creates an <see cref="SKPaint"/> and <see cref="SKFont"/> pair configured for text rendering.
    /// In SkiaSharp 3.x, font properties (typeface, size) are on <see cref="SKFont"/>, not <see cref="SKPaint"/>.
    /// Use the returned font with <c>SKCanvas.DrawText(text, x, y, font, paint)</c>.
    /// </summary>
    /// <param name="godotFont">The font to use for text rendering.</param>
    /// <param name="fontSize">Font size in pixels.</param>
    /// <param name="color">Text color.</param>
    /// <param name="antiAlias">Whether to enable anti-aliasing.</param>
    /// <returns>A tuple of (SKPaint, SKFont). Caller is responsible for disposing both.</returns>
    public static (SKPaint Paint, SKFont Font) CreateTextPaintAndFont(Font godotFont, float fontSize, Color color, bool antiAlias = true)
    {
        var paint = new SKPaint
        {
            IsAntialias = antiAlias,
            Color = color.ToSKColor(),
        };
        var font = godotFont.ToSKFont(fontSize);

        return (paint, font);
    }

    #endregion

    #region Paint Conversion

    /// <summary>
    /// Create basic SKPaint
    /// </summary>
    public static SKPaint CreatePaint(Color color, bool antiAlias = true, SKPaintStyle style = SKPaintStyle.Fill)
    {
        return new SKPaint
        {
            Color = color.ToSKColor(),
            IsAntialias = antiAlias,
            Style = style
        };
    }

    /// <summary>
    /// Create SKPaint with stroke
    /// </summary>
    public static SKPaint CreateStrokePaint(Color color, float strokeWidth, bool antiAlias = true)
    {
        return new SKPaint
        {
            Color = color.ToSKColor(),
            IsAntialias = antiAlias,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = strokeWidth
        };
    }

    #endregion

    #region Transform Conversion

    /// <summary>
    /// Convert Godot Transform2D to Skia SKMatrix
    /// </summary>
    public static SKMatrix ToSKMatrix(this Transform2D transform)
    {
        return new SKMatrix(
            transform.X.X, transform.Y.X, transform.Origin.X,
            transform.X.Y, transform.Y.Y, transform.Origin.Y,
            0, 0, 1
        );
    }

    /// <summary>
    /// Convert Skia SKMatrix to Godot Transform2D
    /// </summary>
    public static Transform2D ToTransform2D(this SKMatrix matrix)
    {
        return new Transform2D(
            new Vector2(matrix.ScaleX, matrix.SkewY),
            new Vector2(matrix.SkewX, matrix.ScaleY),
            new Vector2(matrix.TransX, matrix.TransY)
        );
    }

    #endregion

    #region Image Conversion

    /// <summary>
    /// Convert Godot Image to Skia SKBitmap.
    /// If the image format is not RGBA8, a duplicate is converted to avoid modifying the original.
    /// </summary>
    public static SKBitmap ToSKBitmap(this Image image)
    {
        var src = image;
        if (src.GetFormat() != Image.Format.Rgba8)
        {
            src = (Image)image.Duplicate();
            src.Convert(Image.Format.Rgba8);
        }

        var bitmap = new SKBitmap(src.GetWidth(), src.GetHeight(), SKColorType.Rgba8888, SKAlphaType.Unpremul);
        var pixelData = src.GetData();
        var destPtr = bitmap.GetPixels();
        System.Runtime.InteropServices.Marshal.Copy(pixelData, 0, destPtr, pixelData.Length);

        return bitmap;
    }

    /// <summary>
    /// Convert Skia SKBitmap to Godot Image
    /// </summary>
    public static Image ToGodotImage(this SKBitmap bitmap)
    {
        IntPtr pixels = bitmap.GetPixels();
        int dataSize = bitmap.ByteCount;
        var pixelData = new byte[dataSize];
        System.Runtime.InteropServices.Marshal.Copy(pixels, pixelData, 0, dataSize);
        return Image.CreateFromData(bitmap.Width, bitmap.Height, false, Image.Format.Rgba8, pixelData);
    }

    /// <summary>
    /// Convert Godot Texture2D to Skia SKImage
    /// </summary>
    public static SKImage ToSKImage(this Texture2D texture)
    {
        var image = texture.GetImage();
        using var bitmap = image.ToSKBitmap();
        return SKImage.FromBitmap(bitmap);
    }

    /// <summary>
    /// Convert Skia SKImage to Godot Image
    /// </summary>
    public static Image ToGodotImage(this SKImage skImage)
    {
        using var skBitmap = SKBitmap.FromImage(skImage);
        return skBitmap.ToGodotImage();
    }

    #endregion

    #region Predefined Common Colors

    public static class Colors
    {
        public static readonly SKColor White = Godot.Colors.White.ToSKColor();
        public static readonly SKColor Black = Godot.Colors.Black.ToSKColor();
        public static readonly SKColor Red = Godot.Colors.Red.ToSKColor();
        public static readonly SKColor Green = Godot.Colors.Green.ToSKColor();
        public static readonly SKColor Blue = Godot.Colors.Blue.ToSKColor();
        public static readonly SKColor Yellow = Godot.Colors.Yellow.ToSKColor();
        public static readonly SKColor Transparent = Godot.Colors.Transparent.ToSKColor();
    }

    #endregion
}
