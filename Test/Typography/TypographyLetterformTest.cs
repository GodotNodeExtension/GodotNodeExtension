namespace GodotNodeExtension.Tests.Typography;

using System;
using GdUnit4;
using Godot;
using GodotNodeExtension.Component.Typography.Core;
using GodotNodeExtension.Component.Typography.Core.Model;
using GodotNodeExtension.Component.Typography.Languages;
using static GdUnit4.Assertions;

/// <summary>
/// Specification for display forms: a language decides which form of a character is drawn without changing the
/// characters that were written.
/// <para>
/// The distinction matters because the engine's whole output contract is built on tracing back to the source:
/// selection, copy, hit testing and the typewriter all address the text the caller wrote. A language that
/// re-centers an ellipsis or swaps quotation marks must therefore change the shaped glyphs and nothing else.
/// </para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class TypographyLetterformTest
{
    /// <summary>Traditional Chinese draws corner brackets where the source wrote simplified ones.</summary>
    [TestCase]
    public void CjkQuotationMarksTakeTheLanguagesForm()
    {
        var table = LanguageProfileRegistry.Shared.Resolve(new TypographySettings { LanguageTag = "zh-Hant" })
            .Letterforms;

        AssertThat(LetterformResolver.Substitute("\u201C", CharacterClass.PunctuationOpen, table)).IsEqual("\u300C");
        AssertThat(LetterformResolver.Substitute("\u201D", CharacterClass.PunctuationClose, table)).IsEqual("\u300D");

        // Every rule carries where it comes from: the citation is data rather than a comment, so it travels with
        // the table and a reviewer can check a rule against its source.
        foreach (LetterformSubstitution substitution in table)
            AssertThat(substitution.Reason.Contains("clreq", StringComparison.Ordinal)).IsTrue();
    }

    /// <summary>
    /// A mark the segmenter classified as Western punctuation belongs to Western text and keeps its form, even
    /// inside a paragraph whose language prefers another one. This is why the substitution asks for the class
    /// rather than the code point alone.
    /// </summary>
    [TestCase]
    public void WesternPunctuationKeepsItsForm()
    {
        var table = LanguageProfileRegistry.Shared.Resolve(new TypographySettings { LanguageTag = "zh-Hant" })
            .Letterforms;

        AssertThat((object?)LetterformResolver.Substitute("\u201C", CharacterClass.PunctuationWestern, table))
            .IsNull();
    }

    /// <summary>A language without a table rewrites nothing: that is what keeps undeclared text unchanged.</summary>
    [TestCase]
    public void ALanguageWithoutDisplayFormsChangesNothing()
    {
        var table = LanguageProfileRegistry.Shared.Resolve(new TypographySettings()).Letterforms;

        AssertThat(table.Count).IsEqual(0);
        AssertThat((object?)LetterformResolver.Substitute("\u201C", CharacterClass.PunctuationOpen, table)).IsNull();
    }

    /// <summary>
    /// The chosen form reaches the output element while the source text and its range stay put: the element
    /// reports both, so a dump can show what was written next to what was drawn.
    /// </summary>
    [TestCase]
    public void TheDisplayFormReachesTheOutputWithoutMovingTheSourceRange()
    {
        LayoutElement element = LayOutEllipsis("引號……", new TypographySettings
        {
            MaxWidth = 200f,
            LanguageTag = "zh-Hant",
        });

        AssertThat(element.Text).IsEqual("……");
        AssertThat(element.DisplayText).IsEqual("⋯⋯");
        AssertThat(element.SourceRange.Start).IsEqual(2);
        AssertThat(element.SourceRange.End).IsEqual(4);
    }

    /// <summary>The request can switch the behaviour off, and then the written form is the drawn form.</summary>
    [TestCase]
    public void TheRequestCanSwitchDisplayFormsOff()
    {
        LayoutElement element = LayOutEllipsis("引號……", new TypographySettings
        {
            MaxWidth = 200f,
            LanguageTag = "zh-Hant",
            EnableLetterformSubstitution = false,
        });

        AssertThat((object?)element.DisplayText).IsNull();
    }

    /// <summary>Lay out the text and return the element that holds the pair of ellipsis characters.</summary>
    private static LayoutElement LayOutEllipsis(string text, TypographySettings settings)
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

        using var engine = new TypographyEngine(settings);
        engine.PrepareAndLayout(elements, out _);

        foreach (LayoutElement element in engine.GetLayoutElements(elements))
        {
            if (element.Text == "……")
                return element;
        }

        throw new InvalidOperationException("the ellipsis segment was not laid out");
    }
}
