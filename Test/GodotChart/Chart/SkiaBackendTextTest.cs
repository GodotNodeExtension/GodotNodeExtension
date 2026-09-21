namespace GodotNodeExtension.Tests.GodotChart;

using System;
using GdUnit4;
using Godot;
using GodotNodeExtension.Component.GodotChart.Canvas;
using SkiaSharp;
using static GdUnit4.Assertions;

/// <summary>
/// Behaviour specification for the text path of <see cref="SkiaCanvas2DBackend"/>: glyph fallback for a
/// font that cannot draw the requested text (it must not turn into empty boxes), how the alignment and
/// the decorations land on the surface, what <c>MeasureText</c> reports, and the paint state the text
/// path has to hand back.
/// <para>
/// The pixel-level cases need a surface, so they skip themselves without a rendering device; the
/// typeface and metric cases work headless.
/// </para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class SkiaBackendTextTest
{
    private const int CjkCodepoint = 0x4E2D;   // 中

    /// <summary>Text used for the geometry cases: tall, straight strokes and a wide gap in the middle.</summary>
    private const string ProbeText = "VVVV";

    /// <summary>True when the run has a rendering device; otherwise the case skips itself.</summary>
    private static bool HasDevice(string caseName)
    {
        if (RenderingServer.GetRenderingDevice() is not null) return true;
        GD.Print($"[skip] {caseName}: no rendering device in this run");
        return false;
    }

    private static bool HasInk(Color c) => c.R > 0.5f;

    /// <summary>Bounding box of the ink in one band of the surface, or (-1,-1,-1,-1) when it is empty.</summary>
    private static (int Left, int Right, int Top, int Bottom) InkBounds(Image image, int y0, int y1, int x1)
    {
        int left = int.MaxValue, right = -1, top = int.MaxValue, bottom = -1;
        for (int y = y0; y < y1; y++)
        {
            for (int x = 0; x < x1; x++)
            {
                if (!HasInk(image.GetPixel(x, y))) continue;
                if (x < left) left = x;
                if (x > right) right = x;
                if (y < top) top = y;
                if (y > bottom) bottom = y;
            }
        }
        return right < 0 ? (-1, -1, -1, -1) : (left, right, top, bottom);
    }

    /// <summary>Number of inked pixels in the given row band.</summary>
    private static int InkCountInRows(Image image, int y0, int y1, int x1)
    {
        int count = 0;
        for (int y = y0; y < y1; y++)
            for (int x = 0; x < x1; x++)
                if (HasInk(image.GetPixel(x, y))) count++;
        return count;
    }

    private static SkiaCanvas2DBackend NewSurface(int width, int height)
    {
        var backend = new SkiaCanvas2DBackend();
        backend.Initialize(width, height);
        return backend;
    }

    // ── Typeface fallback ────────────────────────────────────────────────────

    [TestCase]
    public void AFontMissingTheGlyphsFallsBackToASystemFont()
    {
        var fallback = SKFontManager.Default.MatchCharacter(CjkCodepoint);
        if (fallback is null)
        {
            GD.Print("[skip] AFontMissingTheGlyphsFallsBackToASystemFont: no system font covers CJK here");
            return;
        }

        using var latin = SKTypeface.FromFamilyName("Arial");
        if (latin is null || latin.ContainsGlyph(CjkCodepoint))
        {
            GD.Print("[skip] AFontMissingTheGlyphsFallsBackToASystemFont: no Latin-only font to fall back from");
            return;
        }

        var resolved = SkiaCanvas2DBackend.TypefaceWithFallback(latin, "中");

        AssertThat(resolved.ContainsGlyph(CjkCodepoint)).IsTrue();
    }

    [TestCase]
    public void TextThePrimaryFontCoversKeepsThatFont()
    {
        using var latin = SKTypeface.FromFamilyName("Arial");
        if (latin is null)
        {
            GD.Print("[skip] no Arial on this machine");
            return;
        }

        var resolved = SkiaCanvas2DBackend.TypefaceWithFallback(latin, "Revenue 2024");

        AssertThat(ReferenceEquals(resolved, latin)).IsTrue();
    }

    [TestCase]
    public void EmptyTextKeepsThePrimaryFont()
    {
        using var latin = SKTypeface.FromFamilyName("Arial");
        if (latin is null)
        {
            GD.Print("[skip] no Arial on this machine");
            return;
        }

        AssertThat(ReferenceEquals(SkiaCanvas2DBackend.TypefaceWithFallback(latin, ""), latin)).IsTrue();
    }

    // ── Measurement ──────────────────────────────────────────────────────────

    /// <summary>
    /// The height a caller aligns against is <c>Size * LineHeightMultiplier</c> (the backend draws one
    /// unshaped run, so it reports the line box rather than the tight glyph box), and
    /// <see cref="FontSettings.LetterSpacing"/> must <b>not</b> widen it: the text is drawn as a single
    /// <c>SKTextBlob</c> without per-character spacing, so measuring it would offset every centred or
    /// right-aligned label by spacing that is never drawn.
    /// </summary>
    [TestCase]
    public void MeasureTextReportsTheLineBoxAndIgnoresLetterSpacing()
    {
        var backend = new SkiaCanvas2DBackend();
        try
        {
            var font = new FontSettings { Size = 20f, LineHeightMultiplier = 1.5f };

            var metrics = backend.MeasureText("Revenue", font);
            AssertThat(Math.Abs(metrics.Height - 30f) < 1e-4f).IsTrue();
            AssertThat(metrics.Width > 0f).IsTrue();

            var spaced = backend.MeasureText("Revenue", font with { LetterSpacing = 12f });
            AssertThat(Math.Abs(spaced.Width - metrics.Width) < 1e-4f).IsTrue();
            AssertThat(Math.Abs(spaced.Height - metrics.Height) < 1e-4f).IsTrue();
        }
        finally
        {
            backend.Dispose();
        }
    }

    // ── Alignment ────────────────────────────────────────────────────────────

    /// <summary>
    /// Left/Center/Right move the <b>ink</b> relative to the anchor, not the anchor itself: the same run
    /// drawn three times has to end at the anchor (left), straddle it (centre) and start from it
    /// (right). The bounding box of the drawn pixels is the only place that shows up.
    /// </summary>
    [TestCase]
    public void DrawTextAlignmentPositionsTheInkAroundTheAnchor()
    {
        if (!HasDevice(nameof(DrawTextAlignmentPositionsTheInkAroundTheAnchor))) return;

        var backend = NewSurface(160, 90);
        try
        {
            backend.BeginFrame();
            backend.Clear(Colors.Black);

            var font = new FontSettings { Size = 20f, Align = TextAlign.Left };
            using (var paint = backend.CreatePaint())
            {
                paint.SetColor(Colors.White);
                backend.DrawText("HH", 10f, 25f, font, paint);
                backend.DrawText("HH", 80f, 55f, font with { Align = TextAlign.Center }, paint);
                backend.DrawText("HH", 150f, 85f, font with { Align = TextAlign.Right }, paint);
            }

            backend.EndFrame();

            float width = backend.MeasureText("HH", font).Width;
            using var image = backend.SkiaTexture.GetImage();

            var left = InkBounds(image, 0, 35, 160);
            var center = InkBounds(image, 35, 65, 160);
            var right = InkBounds(image, 65, 90, 160);
            GD.Print($"{nameof(DrawTextAlignmentPositionsTheInkAroundTheAnchor)}: width {width:F1}, " +
                     $"left {left}, center {center}, right {right}");

            AssertThat(left.Left >= 0).IsTrue();
            AssertThat(Math.Abs(left.Left - 10) <= 3).IsTrue();
            AssertThat(Math.Abs((center.Left + center.Right) / 2f - 80f) <= 3f).IsTrue();
            AssertThat(Math.Abs(right.Right - 150) <= 3).IsTrue();

            // The three runs are the same text: alignment must not change their extent.
            AssertThat(Math.Abs((left.Right - left.Left) - (center.Right - center.Left)) <= 2).IsTrue();
            AssertThat(Math.Abs((left.Right - left.Left) - (right.Right - right.Left)) <= 2).IsTrue();
        }
        finally
        {
            backend.Dispose();
        }
    }

    // ── Decorations ──────────────────────────────────────────────────────────

    /// <summary>
    /// The underline is drawn below the baseline and the strikethrough crosses the run; both take their
    /// own stroke width, and the width handed in with the paint is restored afterwards (the paint is
    /// sticky and callers reuse it for other geometry).
    /// </summary>
    [TestCase]
    public void TextDecorationsUseTheirOwnStrokeAndRestoreThePaint()
    {
        if (!HasDevice(nameof(TextDecorationsUseTheirOwnStrokeAndRestoreThePaint))) return;

        const float size = 20f;
        var backend = NewSurface(200, 120);
        try
        {
            backend.BeginFrame();
            backend.Clear(Colors.Black);

            var font = new FontSettings { Size = size, Align = TextAlign.Left };
            var paint = backend.CreatePaint();
            paint.SetColor(Colors.White).SetStrokeWidth(7f);

            backend.DrawText(ProbeText, 10f, 30f, font, paint);
            backend.DrawText(ProbeText, 10f, 70f, font with { Decoration = TextDecoration.Underline }, paint);
            backend.DrawText(ProbeText, 10f, 110f, font with { Decoration = TextDecoration.Strikethrough }, paint);

            // The decorations use ~7% of the font size (with a 1 px floor) instead of the caller's 7 px,
            // and the caller's width has to be back by the time DrawText returns.
            AssertThat(((SkiaPaint2D)paint).Paint.StrokeWidth).IsEqual(7f);
            paint.Dispose();

            backend.EndFrame();

            using var image = backend.SkiaTexture.GetImage();
            var plain = InkBounds(image, 0, 40, 200);
            var underlined = InkBounds(image, 40, 80, 200);
            var struck = InkBounds(image, 80, 120, 200);
            AssertThat(plain.Right >= 0).IsTrue();
            AssertThat(underlined.Right >= 0).IsTrue();
            AssertThat(struck.Right >= 0).IsTrue();

            int strikethroughY = (int)(110f - size * 0.3f);   // where the backend draws the line
            int plainRowInk = InkCountInRows(image, 23, 26, 200);            // same height in the plain band
            int struckRowInk = InkCountInRows(image, strikethroughY - 1, strikethroughY + 2, 200);
            GD.Print($"{nameof(TextDecorationsUseTheirOwnStrokeAndRestoreThePaint)}: plain {plain}, " +
                     $"underline {underlined}, struck {struck}, ink {plainRowInk} -> {struckRowInk}");

            // No decoration: nothing is drawn below the baseline.
            AssertThat(plain.Bottom <= 31).IsTrue();
            // The underline sits at baseline + 15% of the font size.
            AssertThat(underlined.Bottom >= 72).IsTrue();
            // The strikethrough adds a full-width line where the strokes alone leave a wide gap.
            AssertThat(struckRowInk > plainRowInk * 1.5f).IsTrue();
        }
        finally
        {
            backend.Dispose();
        }
    }
}
