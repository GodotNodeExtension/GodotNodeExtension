namespace GodotNodeExtension.Tests.Typography;

using System;
using System.Collections.Generic;
using GdUnit4;
using Godot;
using GodotNodeExtension.Component.Typography.Core;
using GodotNodeExtension.Component.Typography.Core.Fonts;
using GodotNodeExtension.Component.Typography.Core.Model;
using static GdUnit4.Assertions;

/// <summary>
/// Specification for a line whose text mixes directions: the runs are put in visual order, and the line still hugs
/// the edge its paragraph starts at.
/// <para>
/// This is what the whole bidi chain is for. A right-to-left paragraph with a Latin word in it must draw the Latin
/// word to the left of the Hebrew one — not because anything was mirrored, but because the levels the algorithm
/// reported say so: the Latin run is the nested (even) one inside a right-to-left paragraph, and the reversal the
/// algorithm prescribes for the line puts it there.
/// </para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class TypographyMixedDirectionTest
{
    /// <summary>Hebrew then a Latin word: a right-to-left paragraph that quotes something Latin.</summary>
    private const string HebrewThenLatin = "\u05D0\u05D1\u05D2 abc";

    /// <summary>
    /// The line is drawn in visual order: the Latin run ends up on the left of the Hebrew one, and the whole line
    /// still starts at the right edge because the paragraph does.
    /// </summary>
    [TestCase]
    public void ARightToLeftParagraphPlacesItsLatinRunInVisualOrder()
    {
        List<LayoutLine> lines = LayOut(HebrewThenLatin, "he");
        LayoutLine line = lines[0];

        AssertThat(line.Elements.Count).IsGreater(1);

        LayoutElement leftmost = line.Elements[0];
        LayoutElement rightmost = line.Elements[^1];

        // Display order is the element order now, left to right.
        AssertThat(leftmost.Text).OverrideFailureMessage(
            "the leftmost element is where the Latin run belongs").IsEqual("abc");
        AssertThat(rightmost.Position.X + rightmost.Size.X).OverrideFailureMessage(
            "a right-to-left line still ends at the content's right edge").IsEqual(200f);

        for (int i = 1; i < line.Elements.Count; i++)
            AssertThat(line.Elements[i].Position.X >= line.Elements[i - 1].Position.X).IsTrue();
    }

    /// <summary>
    /// A left-to-right paragraph that contains a right-to-left word keeps its own edge: the mixed line is reordered,
    /// not mirrored.
    /// </summary>
    [TestCase]
    public void ALeftToRightParagraphKeepsItsLeftEdgeWhenItQuotesRightToLeftText()
    {
        List<LayoutLine> lines = LayOut("abc " + "\u05D0\u05D1\u05D2", "en");
        LayoutLine line = lines[0];

        AssertThat(line.Elements[0].Position.X).OverrideFailureMessage(
            "a left-to-right paragraph starts at its own edge").IsEqual(0f);

        float right = 0f;

        foreach (LayoutElement element in line.Elements)
            right = Math.Max(right, element.Position.X + element.Size.X);

        AssertThat(right < 200f).OverrideFailureMessage(
            "the line must not be pushed to the right edge by the quoted word").IsTrue();
    }

    /// <summary>
    /// Each run is shaped in its own direction: the Latin word inside a right-to-left paragraph is shaped left to
    /// right (its glyphs are not reversed), while the Hebrew run around it is shaped right to left.
    /// </summary>
    [TestCase]
    public void EachRunIsShapedInItsOwnDirection()
    {
        List<LayoutLine> lines = LayOut(HebrewThenLatin, "he");
        uint[] latinGlyphs = GlyphsOf(lines[0], "abc");

        // The same word shaped the way a left-to-right run is: this is what the Latin run must look like.
        uint[] expectedLatin = ShapeAlone("abc", TextDirection.LeftToRight);

        AssertThat(latinGlyphs.Length).IsEqual(expectedLatin.Length);

        for (int i = 0; i < expectedLatin.Length; i++)
        {
            AssertThat(latinGlyphs[i]).OverrideFailureMessage(
                $"the Latin run's glyph {i} was shaped the paragraph's way, not its own").IsEqual(expectedLatin[i]);
        }
    }

    /// <summary>The glyph ids of the element whose text is exactly <paramref name="text"/>.</summary>
    private static uint[] GlyphsOf(LayoutLine line, string text)
    {
        foreach (LayoutElement element in line.Elements)
        {
            if (element.Text == text && element.GlyphRun is { } run)
            {
                var ids = new uint[run.Glyphs.Length];

                for (int i = 0; i < ids.Length; i++)
                    ids[i] = run.Glyphs[i].Id;

                return ids;
            }
        }

        throw new InvalidOperationException($"no element with the text {text} and a glyph run was laid out");
    }

    /// <summary>Shape a run on its own, for comparison.</summary>
    private static uint[] ShapeAlone(string text, TextDirection direction)
    {
        ulong fontId = FontCatalog.Shared.Resolve(ThemeDB.FallbackFont);

        if (!FontCatalog.Shared.TryGetTypeface(fontId, out SkiaSharp.SKTypeface? typeface) || typeface is null)
            throw new InvalidOperationException("the engine font has no typeface");

        GlyphIdResolver.For(typeface).Shape(
            text,
            16f,
            new ShapingOptions { Direction = direction },
            out uint[] ids,
            out _,
            out _,
            out _,
            out _);

        return ids;
    }

    /// <summary>
    /// Rule L4: a character whose resolved direction is right to left is drawn as its mirror image, so the bracket
    /// pair around a Hebrew word comes out swapped. The substitution goes through the display-form channel, so the
    /// source text and its ranges stay what the caller wrote.
    /// <para>
    /// The brackets sit in the same segment as the letters here, because an ASCII bracket is classified as Latin
    /// text rather than as CJK punctuation: the substitution is asserted on the segment's display text, at the
    /// positions the brackets occupy.
    /// </para>
    /// </summary>
    [TestCase]
    public void BracketsAreMirroredInARightToLeftRun()
    {
        List<LayoutLine> lines = LayOut("(" + "אבג" + ")", "he");

        string? display = null;
        string? source = null;

        foreach (LayoutElement element in lines[0].Elements)
        {
            if ((element.Text ?? string.Empty).Contains('('))
            {
                source = element.Text;
                display = element.DisplayText;
            }
        }

        AssertThat(source).OverrideFailureMessage("the bracketed run was not laid out").IsNotNull();
        AssertThat(display).OverrideFailureMessage(
            "the run carries brackets whose resolved direction is right to left, so it must be substituted")
            .IsNotNull();
        // Both brackets swapped, the letters between them untouched (GdUnit's assert takes a string, not a char).
        AssertThat(display).IsEqual(")" + "אבג" + "(");
    }

    /// <summary>
    /// The same text in a left-to-right paragraph is left alone: mirroring belongs to the resolved direction of the
    /// character, not to the character itself.
    /// </summary>
    [TestCase]
    public void BracketsKeepTheirFormInALeftToRightRun()
    {
        List<LayoutLine> lines = LayOut("(abc)", "en");
        bool sawBracket = false;

        foreach (LayoutElement element in lines[0].Elements)
        {
            if (!(element.Text ?? string.Empty).Contains('('))
                continue;

            sawBracket = true;
            AssertThat((object?)element.DisplayText).OverrideFailureMessage(
                "a left-to-right run must not be substituted").IsNull();
        }

        AssertThat(sawBracket).OverrideFailureMessage("the bracketed run was not laid out").IsTrue();
    }

    // ── Helpers ──

    private static List<LayoutLine> LayOut(string text, string languageTag)
    {
        DrawElement[] elements =
        [
            new()
            {
                Type = DrawElement.ElementType.Text,
                Text = text,
                Font = ThemeDB.FallbackFont,
                FontSize = 16,
                Color = new Color(0.1f, 0.1f, 0.1f),
            },
        ];

        using var engine = new TypographyEngine(new TypographySettings
        {
            MaxWidth = 200f,
            LanguageTag = languageTag,
            FirstLineIndent = 0,
            Alignment = TextAlignment.Left,
        });

        return engine.PrepareAndLayout(elements, out _);
    }
}
