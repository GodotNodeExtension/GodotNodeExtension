namespace GodotNodeExtension.Tests.Typography;

using GdUnit4;
using Godot;
using GodotNodeExtension.Component.Typography.Core;
using GodotNodeExtension.Component.Typography.Core.Fonts;
using GodotNodeExtension.Component.Typography.Core.Model;
using SkiaSharp;
using static GdUnit4.Assertions;

/// <summary>
/// Specification for the direction a run is shaped in (the first slice of RTL support).
/// <para>
/// Shaping is where direction first matters: HarfBuzz decides glyph order and cluster values from it, so a run that
/// is written right to left and shaped as left to right comes out reversed. Nothing else about layout changes here —
/// which line edge the text starts at is a separate slice, and the mirrored geometry is what that one is for.
/// </para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class TypographyBidiShapingTest
{
    /// <summary>Hebrew, so the run is right-to-left with no Latin characters mixed in.</summary>
    private const string RightToLeftText = "\u05D0\u05D1\u05D2";

    /// <summary>Shaping the same run in the two directions gives the same glyphs in the opposite order.</summary>
    [TestCase]
    public void ShapingRightToLeftReversesTheGlyphOrder()
    {
        SKTypeface typeface = EngineTypeface();

        uint[] ltr = Shape(typeface, TextDirection.LeftToRight);
        uint[] rtl = Shape(typeface, TextDirection.RightToLeft);

        AssertThat(ltr.Length).IsEqual(rtl.Length);
        AssertThat(ltr.Length > 0).IsTrue();

        for (int i = 0; i < ltr.Length; i++)
        {
            AssertThat(rtl[i]).OverrideFailureMessage(
                $"glyph {i}: left-to-right produced {ltr[i]} and right-to-left {rtl[i]}").IsEqual(ltr[^(i + 1)]);
        }
    }

    /// <summary>
    /// A run is shaped in its own direction, whatever base direction the request declares: a Hebrew run comes out
    /// right to left either way, because that is how it is written. The base direction still decides the line (which
    /// edge it starts at, and the order of the runs), which the mixed-direction cases cover.
    /// </summary>
    [TestCase]
    public void ARunIsShapedInItsOwnDirection()
    {
        LayoutElement ltrBase = LayOutFirstTextElement(TextDirection.LeftToRight);
        LayoutElement rtlBase = LayOutFirstTextElement(TextDirection.RightToLeft);

        AssertThat(ltrBase.Text ?? string.Empty).IsEqual(rtlBase.Text ?? string.Empty);
        AssertThat(ltrBase.SourceRange.ToString()).IsEqual(rtlBase.SourceRange.ToString());

        Glyph[] fromLeftToRightBase = ltrBase.GlyphRun!.Value.Glyphs;
        Glyph[] fromRightToLeftBase = rtlBase.GlyphRun!.Value.Glyphs;

        AssertThat(fromLeftToRightBase.Length).IsEqual(fromRightToLeftBase.Length);

        for (int i = 0; i < fromLeftToRightBase.Length; i++)
        {
            AssertThat(fromRightToLeftBase[i].Id).OverrideFailureMessage(
                $"glyph {i}: the Hebrew run must be shaped the same way under either base direction")
                .IsEqual(fromLeftToRightBase[i].Id);
        }

        // And it really is the right-to-left order: the same run shaped through the path the layout uses.
        // Comparing against GlyphIdResolver directly compared different faces - the engine font does not cover
        // Hebrew, so the layout shapes that run with a fallback face while a direct call would use the primary one.
        uint[] expected = ShapeRightToLeftThroughTheLayoutPath(ltrBase.Text ?? string.Empty);
        AssertThat(fromLeftToRightBase.Length).IsEqual(expected.Length);

        for (int i = 0; i < expected.Length; i++)
        {
            AssertThat(fromLeftToRightBase[i].Id).OverrideFailureMessage(
                $"glyph {i}: the element's run must be what the layout's shaping produces").IsEqual(expected[i]);
        }
    }

    /// <summary>
    /// Shape a run the way <c>ContentPreparer</c> does: through <see cref="HarfBuzzTextShaper"/>, which resolves the
    /// fallback face, rather than through the glyph resolver directly.
    /// </summary>
    /// <param name="text">Text to shape.</param>
    /// <returns>The glyph indices, in drawing order.</returns>
    private static uint[] ShapeRightToLeftThroughTheLayoutPath(string text)
    {
        ulong fontId = FontCatalog.Shared.Resolve(ThemeDB.FallbackFont);

        if (!FontCatalog.Shared.TryGetTypeface(fontId, out SKTypeface? typeface) || typeface is null)
            throw new System.InvalidOperationException("the engine font has no typeface");

        using var shaper = new HarfBuzzTextShaper(
            typeface, 16f, new ShapingOptions { Direction = TextDirection.RightToLeft });

        Glyph[] glyphs = shaper.ShapeGlyphs(text, fontId);
        var ids = new uint[glyphs.Length];

        for (int i = 0; i < ids.Length; i++)
            ids[i] = glyphs[i].Id;

        return ids;
    }

    // ── Helpers ──

    private static uint[] Shape(SKTypeface typeface, TextDirection direction)
    {
        GlyphIdResolver resolver = GlyphIdResolver.For(typeface);
        resolver.Shape(
            RightToLeftText,
            16f,
            new ShapingOptions { Direction = direction },
            out uint[] ids,
            out _,
            out _,
            out _,
            out _);

        return ids;
    }

    private static SKTypeface EngineTypeface()
    {
        ulong fontId = FontCatalog.Shared.Resolve(ThemeDB.FallbackFont);

        if (!FontCatalog.Shared.TryGetTypeface(fontId, out SKTypeface? typeface) || typeface is null)
            throw new System.InvalidOperationException("the engine font has no typeface");

        return typeface;
    }

    private static LayoutElement LayOutFirstTextElement(TextDirection direction)
    {
        DrawElement[] elements =
        [
            new()
            {
                Type = DrawElement.ElementType.Text,
                Text = RightToLeftText,
                Font = ThemeDB.FallbackFont,
                FontSize = 16,
                Color = new Color(0.1f, 0.1f, 0.1f),
            },
        ];

        using var engine = new TypographyEngine(new TypographySettings
        {
            MaxWidth = 200f,
            Direction = direction,
            FirstLineIndent = 0,
            Alignment = TextAlignment.Left,
        });

        engine.PrepareAndLayout(elements, out _);

        foreach (LayoutElement element in engine.GetLayoutElements(elements))
        {
            if (element.GlyphRun is { Glyphs.Length: > 0 })
                return element;
        }

        throw new System.InvalidOperationException("no text element with glyphs was laid out");
    }
}
