using System;
using System.Collections.Concurrent;
using Godot;
using SkiaSharp;

namespace GodotNodeExtension.Component.GodotSkia;

/// <summary>
/// Provides conversion utilities between Skia types and Godot types.
/// <para>
/// This is the single implementation point of the shared tool layer (M43): colour conversion,
/// image conversion and the typeface/font caches live here and are used by GodotChart,
/// RichTextCanvas, GodotMapsui and MarkdownView alike.
/// </para>
/// <para>
/// Text measurement intentionally remains split by purpose: <c>SkiaCanvasTexture2D</c>'s backend
/// measures labels (a single run, no glyph fallback) while <c>SkiaTextMeasurer</c> /
/// <c>HarfBuzzTextShaper</c> measure for layout (glyph coverage checks, shaped advances). Both now
/// build their <see cref="SKFont"/> through <see cref="ConfigureFont(SKFont, SkiaFontStyle)"/>, so
/// hinting/edging and synthetic bold/italic are identical on both paths.
/// </para>
/// </summary>
public static class SkiaGodotConverter
{
    #region Color Conversion

    /// <summary>
    /// Convert Godot Color to Skia SKColor
    /// </summary>
    public static SKColor ToSkColor(this Color godotColor)
    {
        return new SKColor(
            Channel8(godotColor.R),
            Channel8(godotColor.G),
            Channel8(godotColor.B),
            Channel8(godotColor.A)
        );
    }

    /// <summary>
    /// One colour channel as an 8-bit value. Clamped rather than cast: a byte cast wraps a channel above 1
    /// (an HDR red of <c>1.5</c> came out as 126, a mid tone) and an infinite one, while negative channels
    /// wrapped the other way. A NaN channel is not a colour at all and reads as 0.
    /// </summary>
    private static byte Channel8(float value)
        => (byte)(Mathf.Clamp(float.IsNaN(value) ? 0f : value, 0f, 1f) * 255f);

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
    public static SKPoint ToSkPoint(this Vector2 vector)
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
    public static SKSize ToSkSize(this Vector2 vector)
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
    public static SKRect ToSkRect(this Rect2 rect)
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

    /// <summary>
    /// A text style that has to be applied <b>synthetically</b> at the SKFont level, because the
    /// resolved typeface does not provide a matching face (see <see cref="ConfigureFont(SKFont, SkiaFontStyle)"/>).
    /// </summary>
    /// <param name="Bold">Synthesise a bold weight.</param>
    /// <param name="Italic">Synthesise an oblique slant.</param>
    public readonly record struct SkiaFontStyle(bool Bold, bool Italic);

    /// <summary>Skew factor used to synthesise an italic face (matches Godot's FontVariation default).</summary>
    private const float SyntheticItalicSkew = -0.25f;

    /// <summary>
    /// Synthetic style requested by a Godot font: a <see cref="FontVariation"/> that emboldens
    /// (<c>VariationEmbolden</c>) or slants (<c>VariationTransform</c>) its base font.
    /// <para>
    /// Godot expresses "fake" bold/italic as variations, which a Skia typeface cannot carry, so the
    /// style has to be re-applied to the <see cref="SKFont"/>. Detecting it here — in one place, used
    /// by both drawing and measurement — is what keeps the text width consistent between the two.
    /// </para>
    /// </summary>
    public static SkiaFontStyle SyntheticStyleOf(Font? godotFont)
    {
        if (godotFont is null) return new SkiaFontStyle(false, false);

        var (_, bold, italic) = DetectSyntheticStyle(godotFont);
        return new SkiaFontStyle(bold, italic);
    }

    /// <summary>
    /// Walk the <see cref="FontVariation"/> chain once and report the base font plus the synthetic style it
    /// asks for. Both the drawing path (<see cref="SyntheticStyleOf"/>) and typeface creation must agree on
    /// these thresholds, and keeping the walk in one place is what makes that true.
    /// </summary>
    /// <param name="godotFont">Font to unwrap.</param>
    /// <returns>The base font (variations removed) and whether embolden/skew ask for a synthetic style.</returns>
    private static (Font Root, bool Bold, bool Italic) DetectSyntheticStyle(Font godotFont)
    {
        bool bold = false, italic = false;
        var source = godotFont;
        while (source is FontVariation variation)
        {
            if (variation.VariationEmbolden > 0.1f) bold = true;
            if (Math.Abs(variation.VariationTransform.Y.X) > 0.05f) italic = true;
            source = variation.BaseFont;
        }
        return (source, bold, italic);
    }

    /// <summary>Whether a typeface already provides a bold face.</summary>
    private static bool IsBoldTypeface(SKTypeface? typeface)
        => typeface != null && typeface.FontStyle.Weight >= (int)SKFontStyleWeight.SemiBold;

    /// <summary>Whether a typeface already provides an italic/oblique face.</summary>
    private static bool IsItalicTypeface(SKTypeface? typeface)
        => typeface != null && typeface.FontStyle.Slant != SKFontStyleSlant.Upright;

    // Thread-safe typeface cache: Godot Font instance ID → SKTypeface
    private static readonly ConcurrentDictionary<ulong, SKTypeface> STypefaceCache = new();

    // Family-name typefaces (used when no Godot Font resource is given) keyed by style.
    private static readonly ConcurrentDictionary<(string? Family, SKFontStyleWeight Weight, SKFontStyleSlant Slant), SKTypeface>
        SFamilyTypefaceCache = new();

    /// <summary>
    /// Resolve a typeface by family name, cached per (family, weight, slant).
    /// Creating one per measurement call leaked a native typeface every frame.
    /// </summary>
    public static SKTypeface GetOrCreateFamilyTypeface(
        string? family, SKFontStyleWeight weight = SKFontStyleWeight.Normal,
        SKFontStyleSlant slant = SKFontStyleSlant.Upright)
        => SFamilyTypefaceCache.GetOrAdd((family, weight, slant), static key =>
            SKTypeface.FromFamilyName(key.Family, key.Weight, SKFontStyleWidth.Normal, key.Slant)
            ?? SKTypeface.Default);

    /// <summary>
    /// Resolve the Skia typeface for a Godot font, cached per Godot instance.
    /// <para>
    /// A <c>FontVariation</c> that synthesises bold/italic is reported by
    /// <see cref="SyntheticStyleOf"/> and applied to the font by <see cref="ConfigureFont(SKFont, SkiaFontStyle)"/>;
    /// a <c>FontFile</c> without a family name cannot be styled at the typeface level, which is why the
    /// synthetic fallback exists.
    /// </para>
    /// </summary>
    public static SKTypeface ToSkTypeface(this Font godotFont)
    {
        var key = godotFont.GetInstanceId();
        if (STypefaceCache.TryGetValue(key, out var cached))
            return cached;

        var typeface = CreateTypefaceFromGodotFont(godotFont);
        STypefaceCache[key] = typeface;
        return typeface;
    }

    /// <summary>
    /// Converts a Godot <see cref="Font"/> to a Skia <see cref="SKFont"/>.
    /// Supports <see cref="FontFile"/>, <see cref="FontVariation"/> (unwraps chain),
    /// and <see cref="SystemFont"/>. Falls back to the Godot default font typeface if the specified
    /// font cannot be loaded. Synthetic bold/italic variations are applied to the returned font.
    /// </summary>
    /// <param name="godotFont">The Godot font to convert.</param>
    /// <param name="size">Font size in pixels.</param>
    public static SKFont ToSkFont(this Font godotFont, float size = 16f)
        => ConfigureFont(new SKFont(godotFont.ToSkTypeface(), size), SyntheticStyleOf(godotFont));

    /// <summary>
    /// Apply standard font rendering settings to match Godot's text appearance, plus the requested
    /// synthetic style when the typeface cannot provide it.
    /// </summary>
    /// <param name="font">The SKFont to configure.</param>
    /// <param name="style">Synthetic style to apply when the typeface lacks it.</param>
    /// <returns>The same font instance (for chaining).</returns>
    public static SKFont ConfigureFont(SKFont font, SkiaFontStyle style = default)
    {
        font.Hinting = SKFontHinting.Full;
        font.Edging = SKFontEdging.SubpixelAntialias;
        font.Subpixel = true;

        if (style.Bold && !IsBoldTypeface(font.Typeface))
            font.Embolden = true;
        if (style.Italic && !IsItalicTypeface(font.Typeface))
            font.SkewX = SyntheticItalicSkew;

        return font;
    }

    /// <summary>
    /// Extract font data from a Godot Font and create an SKTypeface.
    /// Handles FontVariation chain unwrapping, FontFile data extraction,
    /// SystemFont family lookup, and Godot default font fallback.
    /// </summary>
    private static SKTypeface CreateTypefaceFromGodotFont(Font godotFont)
    {
        // The variation chain and its synthetic-style thresholds live in one helper (see
        // DetectSyntheticStyle), so drawing and typeface creation cannot drift apart.
        var (source, bold, italic) = DetectSyntheticStyle(godotFont);
        var weight = bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal;
        var slant = italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright;

        // Try to extract raw font data from FontFile
        if (source is FontFile ff)
        {
            var data = ff.Data;
            if (data is { Length: > 0 })
            {
                using var skData = SKData.CreateCopy(data);
                var tf = SKTypeface.FromData(skData);
                if (tf != null) return tf;
            }
        }

        // Try SystemFont family names
        if (source is SystemFont sf && sf.FontNames.Length > 0)
        {
            // FromFamilyName can return null when the machine has no such family (a scene naming "Arial"
            // on Linux): the cache below would then hand out null for every later measurement, and
            // SKFont/SKTypeface users assume non-null. Fall back to the default face instead.
            return SKTypeface.FromFamilyName(
                       sf.FontNames[0], weight, SKFontStyleWidth.Normal, slant)
                   ?? SKTypeface.Default;
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
                    using var skData = SKData.CreateCopy(data);
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
    /// Drop the Godot-font-id typeface cache; the separate family-name cache used by
    /// <see cref="GetOrCreateFamilyTypeface"/> is left untouched.
    /// <para>
    /// The cached typefaces are <b>not</b> disposed: the same instances are handed out to other modules
    /// (the typography engine's font catalog, the rich-text measurer) which keep using them, and this call
    /// runs from the assembly reload hook as well. They are released by the GC once no holder is left,
    /// which is the only safe point in time for an object shared across components.
    /// </para>
    /// </summary>
    public static void ClearTypefaceCache() => STypefaceCache.Clear();

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
            Color = color.ToSkColor(),
        };
        var font = godotFont.ToSkFont(fontSize);

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
            Color = color.ToSkColor(),
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
            Color = color.ToSkColor(),
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
    public static SKMatrix ToSkMatrix(this Transform2D transform)
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
    public static SKBitmap ToSkBitmap(this Image image)
    {
        var src = image;
        Image? duplicate = null;
        if (src.GetFormat() != Image.Format.Rgba8)
        {
            // The conversion must not touch the caller's image, so work on a copy - and release the
            // native pixels of that copy as soon as they have been read.
            duplicate = (Image)image.Duplicate();
            duplicate.Convert(Image.Format.Rgba8);
            src = duplicate;
        }

        using var _ = duplicate;
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
        // Godot images are RGBA8 with straight alpha, while a Skia bitmap is whatever layout Skia
        // picked - BGRA on Windows/macOS, and premultiplied alpha by default. Copying the raw bytes
        // and labelling them Rgba8 therefore swaps red and blue (and darkens translucent pixels), so
        // convert explicitly instead.
        var info = new SKImageInfo(bitmap.Width, bitmap.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var rgba = new SKBitmap(info);
        using var source = bitmap.PeekPixels();
        if (source is null || !source.ReadPixels(info, rgba.GetPixels(), rgba.RowBytes, 0, 0))
            // An unreadable bitmap - unallocated, or a layout Skia cannot convert - answers a transparent
            // image instead of throwing. At least one pixel: Godot rejects a zero-sized Image ("width must
            // be greater than 0"), and an empty bitmap is exactly what a caller with nothing to draw has.
            return Image.CreateEmpty(Math.Max(1, bitmap.Width), Math.Max(1, bitmap.Height), false,
                Image.Format.Rgba8);

        var pixelData = new byte[rgba.ByteCount];
        System.Runtime.InteropServices.Marshal.Copy(rgba.GetPixels(), pixelData, 0, pixelData.Length);
        return Image.CreateFromData(bitmap.Width, bitmap.Height, false, Image.Format.Rgba8, pixelData);
    }

    /// <summary>
    /// Convert Godot Texture2D to Skia SKImage
    /// </summary>
    public static SKImage ToSkImage(this Texture2D texture)
    {
        var image = texture.GetImage();
        if (image is null)
            throw new InvalidOperationException(
                $"Texture '{texture}' provides no pixel data (Texture2D.GetImage() returned null).");

        using var bitmap = image.ToSkBitmap();
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

    /// <summary>Pre-converted Skia colours for the most common Godot colours.</summary>
    public static class Colors
    {
        /// <summary>Opaque white.</summary>
        public static readonly SKColor White = Godot.Colors.White.ToSkColor();

        /// <summary>Opaque black.</summary>
        public static readonly SKColor Black = Godot.Colors.Black.ToSkColor();

        /// <summary>Opaque red.</summary>
        public static readonly SKColor Red = Godot.Colors.Red.ToSkColor();

        /// <summary>Opaque green.</summary>
        public static readonly SKColor Green = Godot.Colors.Green.ToSkColor();

        /// <summary>Opaque blue.</summary>
        public static readonly SKColor Blue = Godot.Colors.Blue.ToSkColor();

        /// <summary>Opaque yellow.</summary>
        public static readonly SKColor Yellow = Godot.Colors.Yellow.ToSkColor();

        /// <summary>Fully transparent.</summary>
        public static readonly SKColor Transparent = Godot.Colors.Transparent.ToSkColor();
    }

    #endregion
}
