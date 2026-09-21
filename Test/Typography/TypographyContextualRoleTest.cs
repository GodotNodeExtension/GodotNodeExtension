namespace GodotNodeExtension.Tests.Typography;

using System.Collections.Generic;
using GdUnit4;
using Godot;
using GodotNodeExtension.Component.Typography.Core;
using GodotNodeExtension.Component.Typography.Core.Model;
using GodotNodeExtension.Component.Typography.Languages;
using static GdUnit4.Assertions;

/// <summary>
/// Specification for the characters whose role depends on the text around them: quotation marks, dashes and
/// ellipses are Common, so the code point alone does not say whether they obey the CJK convention or the Western
/// one.
/// <para>
/// This is the mixing place most likely to be wrong, and the failure is quiet: U+201C treated as CJK
/// punctuation in an English sentence forbids a line break that should be allowed, and an apostrophe treated as
/// CJK punctuation makes <c>don’t</c> break in the middle. The cases below pin the evidence rule — a mark follows
/// the content it belongs to — and the consequences for the boundary decisions.
/// </para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class TypographyContextualRoleTest
{
    /// <summary>
    /// A quotation mark next to Latin letters is Western, next to CJK letters it is CJK, whichever language the
    /// paragraph declares.
    /// </summary>
    [TestCase]
    public void QuotationMarksFollowTheContentTheyBelongTo()
    {
        // Opening mark: the content it opens decides — here the quoted phrase is English.
        List<TextSegment> mixed = Segment("中文“English”中文");
        TextSegment openingInMixed = SegmentStartingWith(mixed, '“');
        AssertThat(openingInMixed.CharClass).OverrideFailureMessage(
            "an opening mark followed by Latin letters must be Western").IsEqual(CharacterClass.PunctuationWestern);

        // Closing mark: the content it closes decides.
        TextSegment closingInMixed = SegmentStartingWith(mixed, '”');
        AssertThat(closingInMixed.CharClass).IsEqual(CharacterClass.PunctuationWestern);

        // The same marks around Chinese text keep the CJK role, so Chinese prohibition rules still apply.
        List<TextSegment> chinese = Segment("中文「引号」中文");
        AssertThat(SegmentStartingWith(chinese, '「').CharClass).IsEqual(CharacterClass.PunctuationOpen);
        AssertThat(SegmentStartingWith(chinese, '」').CharClass).IsEqual(CharacterClass.PunctuationClose);

        // A curly quotation mark used as an apostrophe inside a word is Western, so the word stays one word.
        List<TextSegment> apostrophe = Segment("don’t");
        AssertThat(SegmentStartingWith(apostrophe, '’').CharClass).IsEqual(CharacterClass.PunctuationWestern);
    }

    /// <summary>
    /// With no evidence either way the plain classification stands: the language-dependent fallback belongs to the
    /// rule set that governs the paragraph, and the prepare phase does not know the language.
    /// </summary>
    [TestCase]
    public void AMarkWithoutEvidenceKeepsItsPlainClass()
    {
        List<TextSegment> onlyMark = Segment("“");
        AssertThat(SegmentStartingWith(onlyMark, '“').CharClass).IsEqual(CharacterClass.PunctuationOpen);

        // Digits and spaces are not evidence: they carry no script.
        List<TextSegment> digits = Segment("“123”");
        AssertThat(SegmentStartingWith(digits, '“').CharClass).IsEqual(CharacterClass.PunctuationOpen);
    }

    /// <summary>
    /// The resolution changes what the boundary decides: a Western mark in a CJK paragraph is not subject to the
    /// CJK prohibition tables, while a CJK mark still is.
    /// </summary>
    [TestCase]
    public void AWesternMarkIsNotSubjectToCjkProhibition()
    {
        var chineseSettings = new TypographySettings { MaxWidth = 200f, LanguageTag = "zh-Hans" };

        using var preparer = new ContentPreparer();

        // U+201C before Latin letters: Western, so it may end a line (prohibition tables must not claim it).
        PreparedContent mixed = preparer.Prepare(Elements("中文“English”"));
        var typography = LanguageProfileRegistry.Shared.Resolve(chineseSettings);
        List<Boundary> boundaries = BoundaryBuilder.Build(mixed, typography);

        // The boundary at the mark is the one between the mark and what follows it: the line-end prohibition
        // speaks about the mark itself, not about its neighbour.
        Boundary afterOpening = boundaries[IndexOfSegmentStartingWith(mixed, '“')];
        AssertThat(afterOpening.ForbiddenAtLineEnd).OverrideFailureMessage(
            "a Western opening mark must not inherit the CJK line-end prohibition").IsFalse();

        // The CJK mark in the same position does inherit it.
        PreparedContent cjk = preparer.Prepare(Elements("中文「English」"));
        List<Boundary> cjkBoundaries = BoundaryBuilder.Build(cjk, typography);
        Boundary afterCjkOpening = cjkBoundaries[IndexOfSegmentStartingWith(cjk, '「')];

        AssertThat(afterCjkOpening.ForbiddenAtLineEnd).OverrideFailureMessage(
            "a CJK opening mark is forbidden at line end (CLREQ §6.1.1)").IsTrue();
    }

    // ── Helpers ──

    private static DrawElement[] Elements(string text) => [Text(text)];

    private static DrawElement Text(string text) => new()
    {
        Type = DrawElement.ElementType.Text,
        Text = text,
        Font = ThemeDB.FallbackFont,
        FontSize = 16,
        Color = new Color(0.1f, 0.1f, 0.1f),
    };

    private static List<TextSegment> Segment(string text)
    {
        using var preparer = new ContentPreparer();
        return preparer.Prepare(Elements(text)).Segments;
    }

    private static TextSegment SegmentStartingWith(List<TextSegment> segments, char character)
    {
        foreach (var segment in segments)
        {
            if (!string.IsNullOrEmpty(segment.Text) && segment.Text[0] == character)
                return segment;
        }

        AssertThat(false).OverrideFailureMessage(
            $"no segment starts with '{character}' in: {Describe(segments)}").IsTrue();

        return default;
    }

    private static int IndexOfSegmentStartingWith(PreparedContent content, char character)
    {
        for (int i = 0; i < content.Segments.Count; i++)
        {
            var segment = content.Segments[i];

            if (!string.IsNullOrEmpty(segment.Text) && segment.Text[0] == character)
                return i;
        }

        return -1;
    }

    private static string Describe(List<TextSegment> segments)
    {
        var parts = new List<string>(segments.Count);

        foreach (var segment in segments)
            parts.Add($"\"{segment.Text}\"({segment.CharClass})");

        return string.Join(" ", parts);
    }
}
