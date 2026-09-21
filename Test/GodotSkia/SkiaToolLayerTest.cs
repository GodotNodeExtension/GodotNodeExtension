namespace GodotNodeExtension.Tests.GodotSkia;

using System;
using GdUnit4;
using Godot;
using GodotNodeExtension.Component.GodotSkia;
using SkiaSharp;
using static GdUnit4.Assertions;

/// <summary>
/// Behaviour specification for the shared Skia tool layer of <see cref="SkiaGodotConverter"/> and
/// <see cref="SkiaCanvasTexture2D"/>: the single colour, geometry and image conversion implementation,
/// the typeface caches, and the shared GPU context plumbing.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class SkiaToolLayerTest
{
    /// <summary>True when the run has a rendering device; otherwise the case skips itself.</summary>
    private static bool HasDevice(string caseName)
    {
        if (RenderingServer.GetRenderingDevice() is not null) return true;
        GD.Print($"[skip] {caseName}: no rendering device in this run");
        return false;
    }

    // ── Colour conversion ───────────────────────────────────────────────────

    [TestCase]
    public void ColorRoundTripsThroughSkia()
    {
        var godot = new Color(0.25f, 0.5f, 0.75f);

        var skia = godot.ToSkColor();
        var back = skia.ToGodotColor();

        AssertThat(MathF.Abs(back.R - godot.R) <= 1f / 255f).IsTrue();
        AssertThat(MathF.Abs(back.G - godot.G) <= 1f / 255f).IsTrue();
        AssertThat(MathF.Abs(back.B - godot.B) <= 1f / 255f).IsTrue();
        AssertThat(back.A).IsEqual(1f);
    }

    [TestCase]
    public void TranslucentColorRoundTripsThroughSkia()
    {
        // Alpha is the channel a "drop it" regression is easiest to hide in, and the round trip has to
        // survive the 8-bit quantisation of both directions.
        var godot = new Color(0.25f, 0.5f, 0.75f, 0.5f);

        var skia = godot.ToSkColor();
        AssertThat(skia.Alpha).IsEqual(127);   // 0.5 * 255 truncated to a byte

        var back = skia.ToGodotColor();
        AssertThat(MathF.Abs(back.R - godot.R) <= 1f / 255f).IsTrue();
        AssertThat(MathF.Abs(back.G - godot.G) <= 1f / 255f).IsTrue();
        AssertThat(MathF.Abs(back.B - godot.B) <= 1f / 255f).IsTrue();
        AssertThat(MathF.Abs(back.A - godot.A) <= 1f / 255f).IsTrue();
    }

    // ── Geometry conversion ─────────────────────────────────────────────────

    /// <summary>
    /// <see cref="SkiaGodotConverter.Colors"/>: the Godot palette in Skia's own type. The names are the promise
    /// - every entry but <c>Transparent</c> is opaque - and the values have to be the Godot palette.
    /// </summary>
    [TestCase]
    public void TheConverterPaletteMirrorsTheGodotPalette()
    {
        AssertThat(SkiaGodotConverter.Colors.White).IsEqual(Godot.Colors.White.ToSkColor());
        AssertThat(SkiaGodotConverter.Colors.Blue.ToGodotColor()).IsEqual(Godot.Colors.Blue);
        AssertThat(SkiaGodotConverter.Colors.Transparent.Alpha).IsEqual((byte)0);

        foreach (var opaque in new[]
                 {
                     SkiaGodotConverter.Colors.White, SkiaGodotConverter.Colors.Black,
                     SkiaGodotConverter.Colors.Red, SkiaGodotConverter.Colors.Green,
                     SkiaGodotConverter.Colors.Blue, SkiaGodotConverter.Colors.Yellow,
                 })
        {
            AssertThat(opaque.Alpha).IsEqual((byte)255);
        }
    }

    [TestCase]
    public void GeometryConversionsRoundTripNegativeAndOutOfRangeValues()
    {
        // Negative origins and far-out coordinates: the converters must neither clamp nor reorder them
        // (a normalising SKRect would turn the negative position into a positive one).
        var point = new Vector2(-12.5f, 4096.25f);
        var backPoint = point.ToSkPoint().ToVector2();
        AssertThat(backPoint.X).IsEqual(-12.5f);
        AssertThat(backPoint.Y).IsEqual(4096.25f);

        var size = new Vector2(-3.5f, 0.125f);
        var backSize = size.ToSkSize().ToVector2();
        AssertThat(backSize.X).IsEqual(-3.5f);
        AssertThat(backSize.Y).IsEqual(0.125f);

        var rect = new Rect2(new Vector2(-40.5f, -8.25f), new Vector2(2048f, 4096f));
        var backRect = rect.ToSkRect().ToRect2();
        AssertThat(backRect.Position.X).IsEqual(-40.5f);
        AssertThat(backRect.Position.Y).IsEqual(-8.25f);
        AssertThat(backRect.Size.X).IsEqual(2048f);
        AssertThat(backRect.Size.Y).IsEqual(4096f);
    }

    [TestCase]
    public void TransformRoundTripsThroughSkiaMatrix()
    {
        var transform = new Transform2D(0.35f, new Vector2(-7f, 15.5f));

        var back = transform.ToSkMatrix().ToTransform2D();

        // The matrix carries the two basis vectors and the origin; a transposed copy would swap the
        // skew terms, which shows up here as a mismatched Y.X / X.Y.
        AssertThat(MathF.Abs(back.X.X - transform.X.X) <= 1e-5f).IsTrue();
        AssertThat(MathF.Abs(back.X.Y - transform.X.Y) <= 1e-5f).IsTrue();
        AssertThat(MathF.Abs(back.Y.X - transform.Y.X) <= 1e-5f).IsTrue();
        AssertThat(MathF.Abs(back.Y.Y - transform.Y.Y) <= 1e-5f).IsTrue();
        AssertThat(MathF.Abs(back.Origin.X - transform.Origin.X) <= 1e-5f).IsTrue();
        AssertThat(MathF.Abs(back.Origin.Y - transform.Origin.Y) <= 1e-5f).IsTrue();
    }

    // ── Image conversion ────────────────────────────────────────────────────

    [TestCase]
    public void GodotImageConvertsToSkBitmapWithTheSamePixels()
    {
        var image = Image.CreateEmpty(2, 1, false, Image.Format.Rgba8);
        image.SetPixel(0, 0, Colors.Red);
        image.SetPixel(1, 0, Colors.Blue);

        using var bitmap = image.ToSkBitmap();

        AssertThat(bitmap.Width).IsEqual(2);
        AssertThat(bitmap.Height).IsEqual(1);
        AssertThat(bitmap.GetPixel(0, 0)).IsEqual(Colors.Red.ToSkColor());
        AssertThat(bitmap.GetPixel(1, 0)).IsEqual(Colors.Blue.ToSkColor());
    }

    [TestCase]
    public void BitmapRoundTripsThroughGodotImage()
    {
        using var bitmap = new SKBitmap(2, 1, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        bitmap.SetPixel(0, 0, new SKColor(255, 0, 0, 255));
        bitmap.SetPixel(1, 0, new SKColor(0, 0, 255, 255));

        using var image = bitmap.ToGodotImage();

        AssertThat(image.GetWidth()).IsEqual(2);
        AssertThat(image.GetHeight()).IsEqual(1);
        AssertThat(image.GetPixel(0, 0)).IsEqual(Colors.Red);
    }

    [TestCase]
    public void BitmapWithTheNativeSkiaLayoutKeepsItsChannels()
    {
        // The platform-native 8888 layout is BGRA on Windows/macOS; converting must not swap red and
        // blue (the RGBA round-trip above cannot catch that because it starts from RGBA).
        using var bitmap = new SKBitmap(new SKImageInfo(2, 1, SKColorType.Bgra8888, SKAlphaType.Premul));
        bitmap.SetPixel(0, 0, new SKColor(255, 0, 0, 255));
        bitmap.SetPixel(1, 0, new SKColor(0, 0, 255, 255));

        using var image = bitmap.ToGodotImage();

        AssertThat(image.GetFormat()).IsEqual(Image.Format.Rgba8);
        AssertThat(image.GetPixel(0, 0)).IsEqual(Colors.Red);
        AssertThat(image.GetPixel(1, 0)).IsEqual(Colors.Blue);
    }

    [TestCase]
    public void TranslucentPremultipliedPixelsSurviveTheConversion()
    {
        using var bitmap = new SKBitmap(new SKImageInfo(1, 1, SKColorType.Bgra8888, SKAlphaType.Premul));
        bitmap.SetPixel(0, 0, new SKColor(255, 0, 0, 128).WithAlpha(128));   // premultiplied red @50%

        using var image = bitmap.ToGodotImage();

        var pixel = image.GetPixel(0, 0);
        AssertThat(Math.Abs(pixel.R - 1f) < 0.02f).IsTrue();
        AssertThat(pixel.G < 0.02f).IsTrue();
        AssertThat(pixel.B < 0.02f).IsTrue();
        AssertThat(Math.Abs(pixel.A - (128f / 255f)) < 0.02f).IsTrue();
    }


    [TestCase]
    public void AnImageInAnotherFormatConvertsWithoutTouchingTheSource()
    {
        // The non-RGBA8 path: ToSkBitmap duplicates and converts, and the caller's image has to come out
        // untouched (its own format and pixels are what the next hash of it reads).
        var image = Image.CreateEmpty(2, 1, false, Image.Format.Rgb8);
        image.SetPixel(0, 0, Colors.Red);
        image.SetPixel(1, 0, Colors.Blue);

        using var bitmap = image.ToSkBitmap();

        AssertThat(bitmap.Width).IsEqual(2);
        AssertThat(bitmap.GetPixel(0, 0)).IsEqual(Colors.Red.ToSkColor());
        AssertThat(bitmap.GetPixel(1, 0)).IsEqual(Colors.Blue.ToSkColor());
        AssertThat(image.GetFormat()).IsEqual(Image.Format.Rgb8);
        AssertThat(image.GetPixel(0, 0)).IsEqual(Colors.Red);
    }

    [TestCase]
    public void AnUnallocatedBitmapConvertsToAnEmptyImage()
    {
        // PeekPixels has nothing to read, so the conversion answers an empty image instead of throwing
        // (the caller then draws nothing rather than dying).
        using var bitmap = new SKBitmap();
        using var image = bitmap.ToGodotImage();

        // A transparent 1x1 image: Godot refuses a zero-sized Image, and there is nothing to read anyway.
        AssertThat(image.GetWidth()).IsEqual(1);
        AssertThat(image.GetHeight()).IsEqual(1);
        AssertThat(image.GetPixel(0, 0).A).IsEqual(0f);
    }

    /// <summary>
    /// The three paint factories: what each caller gets is a paint it can hand to the canvas as it is - the
    /// colour, the style and the anti-aliasing flag the arguments asked for.
    /// </summary>
    [TestCase]
    public void PaintFactoriesBuildTheRequestedPaint()
    {
        using var fill = SkiaGodotConverter.CreatePaint(Colors.Red);
        AssertThat(fill.Color).IsEqual(Colors.Red.ToSkColor());
        AssertThat(fill.Style).IsEqual(SKPaintStyle.Fill);
        AssertThat(fill.IsAntialias).IsTrue();

        using var flat = SkiaGodotConverter.CreatePaint(Colors.Blue, antiAlias: false, style: SKPaintStyle.Stroke);
        AssertThat(flat.Color).IsEqual(Colors.Blue.ToSkColor());
        AssertThat(flat.Style).IsEqual(SKPaintStyle.Stroke);
        AssertThat(flat.IsAntialias).IsFalse();

        using var stroke = SkiaGodotConverter.CreateStrokePaint(Colors.Green, strokeWidth: 3.5f);
        AssertThat(stroke.Style).IsEqual(SKPaintStyle.Stroke);
        AssertThat(stroke.StrokeWidth).IsEqual(3.5f);
        AssertThat(stroke.Color).IsEqual(Colors.Green.ToSkColor());

        // The text pair: the paint carries the colour, the font the size - and the font is usable for the
        // measurement every caller does with it next.
        var (paint, font) = SkiaGodotConverter.CreateTextPaintAndFont(ThemeDB.FallbackFont, 18f, Colors.Yellow);
        using (paint)
        using (font)
        {
            AssertThat(paint.Color).IsEqual(Colors.Yellow.ToSkColor());
            AssertThat(paint.IsAntialias).IsTrue();
            AssertThat(font.Size).IsEqual(18f);
            AssertThat(font.MeasureText("GodotSkia")).IsGreater(0f);
        }
    }

    /// <summary>
    /// A channel outside <c>0...1</c> is clamped, not wrapped: the byte cast this used would turn an HDR red
    /// (1.5) into 126 - a mid tone - and a NaN alpha into 0 by accident. An infinite channel is "brighter than
    /// anything", so it clamps like a large value does.
    /// </summary>
    [TestCase]
    public void ConversionClampsHighDynamicRangeAndNonFiniteColors()
    {
        var hdr = new Color(1.5f, -0.25f, 0.5f, 2f).ToSkColor();
        AssertThat(hdr.Red).IsEqual((byte)255);
        AssertThat(hdr.Green).IsEqual((byte)0);
        AssertThat(hdr.Blue).IsEqual((byte)127);
        AssertThat(hdr.Alpha).IsEqual((byte)255);

        var nonFinite = new Color(float.NaN, 0.5f, float.PositiveInfinity, 1f).ToSkColor();
        AssertThat(nonFinite.Red).IsEqual((byte)0);
        AssertThat(nonFinite.Green).IsEqual((byte)127);
        AssertThat(nonFinite.Blue).IsEqual((byte)255);
    }

    // ── Typeface caches ─────────────────────────────────────────────────────

    [TestCase]
    public void FamilyTypefaceCacheIsSharedAndStable()
    {
        var first = SkiaGodotConverter.GetOrCreateFamilyTypeface("Arial", SKFontStyleWeight.Bold);
        var second = SkiaGodotConverter.GetOrCreateFamilyTypeface("Arial", SKFontStyleWeight.Bold);

        AssertThat(ReferenceEquals(first, second)).IsTrue();
        // Different styles are different entries.
        var regular = SkiaGodotConverter.GetOrCreateFamilyTypeface("Arial");
        AssertThat(ReferenceEquals(first, regular)).IsFalse();
    }

    [TestCase]
    public void ANameWithNoSystemFontStillResolvesToATypeface()
    {
        // A family the machine does not have used to reach the caller as null (SKTypeface.FromFamilyName
        // returns null for an unknown name), which then propagated into every later measurement.
        var font = new SystemFont { FontNames = ["No Such Family 9c1f4a", "Also Missing 8ab3d2"] };

        using var raw = SKTypeface.FromFamilyName(font.FontNames[0]);
        GD.Print($"{nameof(ANameWithNoSystemFontStillResolvesToATypeface)}: raw FromFamilyName -> " +
                 $"{(raw is null ? "null" : "non-null")}");

        var typeface = font.ToSkTypeface();

        AssertThat(typeface.Handle != IntPtr.Zero).IsTrue();
        AssertThat(typeface.ContainsGlyph('A')).IsTrue();
    }

    [TestCase]
    public void ClearingTheTypefaceCacheKeepsHandedOutTypefacesUsable()
    {
        Font font = ThemeDB.FallbackFont!;

        var held = font.ToSkTypeface();
        AssertThat(held.Handle != IntPtr.Zero).IsTrue();

        SkiaGodotConverter.ClearTypefaceCache();

        // A held typeface outlives the cache clear: disposing it here would leave every other module
        // (the typography catalog, the rich-text measurer) with a dead native object. A disposed
        // SkiaSharp object reports Handle == 0, so that is the direct check.
        AssertThat(held.Handle != IntPtr.Zero).IsTrue();
        using var skFont = new SKFont(held, 20f);
        AssertThat(skFont.MeasureText("Revenue") > 0f).IsTrue();

        // The cache repopulates on the next request.
        var again = font.ToSkTypeface();
        AssertThat(again.Handle != IntPtr.Zero).IsTrue();
    }

    // ── Shared GPU context plumbing ─────────────────────────────────────────

    /// <summary>
    /// <see cref="SkiaCanvasTexture2D.ShareGrContext"/> set to false gives every texture its own context and
    /// touches neither the shared reference count nor the shared creation count - the branch that used to be
    /// unreachable from the tests. (The sharing side itself, with several textures alive at once, is covered by
    /// <c>SkiaResourceLifecycleTest.LiveSurfacesShareOneContextAndTheLastOneReleasesIt</c>.)
    /// </summary>
    [TestCase]
    public void ShareGrContextFalseGivesEachTextureItsOwnContext()
    {
        if (!HasDevice(nameof(ShareGrContextFalseGivesEachTextureItsOwnContext))) return;

        int baselineRefs = SkiaCanvasTexture2D.SharedGrContextRefCount;
        int baselineCreates = SkiaCanvasTexture2D.GrContextCreateCount;

        var textures = new SkiaCanvasTexture2D[3];
        int created = 0;
        try
        {
            SkiaCanvasTexture2D.ShareGrContext = false;

            for (; created < textures.Length; created++)
                textures[created] = new SkiaCanvasTexture2D(8, 8);

            // Nothing was added to the shared counter, and no texture holds a shared reference.
            AssertThat(SkiaCanvasTexture2D.SharedGrContextRefCount).IsEqual(baselineRefs);
            AssertThat(SkiaCanvasTexture2D.GrContextCreateCount).IsEqual(baselineCreates);

            // They still draw: the private context is a working one.
            foreach (var texture in textures)
            {
                texture.Canvas!.Clear(new SKColor(7, 8, 9, 255));
                texture.UpdateTexture();
            }
            using var image = textures[0].GetImage();
            AssertThat(image.GetPixel(4, 4).ToSkColor().Red).IsEqual((byte)7);
        }
        finally
        {
            for (int i = 0; i < created; i++)
                textures[i].Dispose();
            SkiaCanvasTexture2D.ShareGrContext = true;
        }

        AssertThat(SkiaCanvasTexture2D.SharedGrContextRefCount).IsEqual(baselineRefs);
    }
}
