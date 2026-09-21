namespace GodotNodeExtension.Tests.Typography;

using System.Collections.Generic;
using GdUnit4;
using Godot;
using GodotNodeExtension.Component.Typography.Core;
using GodotNodeExtension.Component.Typography.Core.Model;
using GodotNodeExtension.Component.Typography.Languages;
using static GdUnit4.Assertions;

/// <summary>
/// Specification for paragraph-level language arbitration: a paragraph may name its own language, and that
/// language's rule set governs it while the request keeps the geometry.
/// <para>
/// This is the layer the three scopes of the model were missing: the request owned the language, so a Chinese
/// document quoting a block of English had to choose one convention for both. The cases below pin what changes per
/// paragraph — the boundary rules and the script-spacing switch — and what does not: an explicit request override
/// still wins, and the paragraph never gets its own box.
/// </para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class TypographyParagraphLanguageTest
{
    /// <summary>
    /// The middle paragraph is English, so its breaks come from the Unicode algorithm while the Chinese
    /// paragraphs keep their prohibition tailoring.
    /// </summary>
    [TestCase]
    public void ParagraphsChooseTheirOwnBoundaryRules()
    {
        PreparedContent prepared = Prepare();
        var typography = LanguageProfileRegistry.Shared.Resolve(SwitchRequest());

        List<Boundary> boundaries = BoundaryBuilder.Build(
            prepared, typography, PerParagraph(SwitchRequest(), prepared));

        AssertThat(ParagraphUsesUnicodeRule(boundaries, prepared, 1)).OverrideFailureMessage(
            "the English paragraph must break by UAX #14").IsTrue();
        AssertThat(ParagraphUsesUnicodeRule(boundaries, prepared, 0)).OverrideFailureMessage(
            "the Chinese paragraph must keep its prohibition tailoring").IsFalse();
        AssertThat(ParagraphUsesUnicodeRule(boundaries, prepared, 2)).IsFalse();
    }

    /// <summary>
    /// The CJK/Latin gap follows the same rule: the Chinese paragraphs insert it, the English one does not.
    /// </summary>
    [TestCase]
    public void ParagraphsChooseTheirOwnScriptSpacing()
    {
        PreparedContent prepared = Prepare();
        var typography = LanguageProfileRegistry.Shared.Resolve(SwitchRequest());

        List<Boundary> boundaries = BoundaryBuilder.Build(
            prepared, typography, PerParagraph(SwitchRequest(), prepared));

        float chineseGap = WidestGapInParagraph(boundaries, prepared, 0);
        float englishGap = WidestGapInParagraph(boundaries, prepared, 1);

        AssertThat(chineseGap > 0f).OverrideFailureMessage(
            "the Chinese paragraph inserts a CJK/Latin gap").IsTrue();
        AssertThat(englishGap).OverrideFailureMessage(
            "the English paragraph must not insert a CJK/Latin gap").IsEqual(0f);
    }

    /// <summary>
    /// An explicit request override survives a paragraph's own language: the caller asked for that value, and a
    /// paragraph naming a language does not silently undo it.
    /// </summary>
    [TestCase]
    public void RequestOverridesSurviveAParagraphLanguage()
    {
        TypographySettings settings = SwitchRequest();
        settings.EnableCjkLatinSpacing = true; // explicit, applies to every paragraph

        PreparedContent prepared = Prepare();
        var typography = LanguageProfileRegistry.Shared.Resolve(settings);

        List<Boundary> boundaries = BoundaryBuilder.Build(
            prepared, typography, PerParagraph(settings, prepared));

        AssertThat(WidestGapInParagraph(boundaries, prepared, 1) > 0f).OverrideFailureMessage(
            "an explicit request value must still apply to the English paragraph").IsTrue();
    }

    /// <summary>
    /// The compile-phase cache must notice a paragraph switching language, or a relayout would reuse decisions made
    /// for a different convention.
    /// </summary>
    [TestCase]
    public void SwitchingAParagraphLanguageRebuildsTheBoundaries()
    {
        TypographySettings settings = SwitchRequest();
        var elements = Elements(languageOfSecondParagraph: null);

        using var engine = new TypographyEngine(settings);
        engine.PrepareAndLayout(Clone(elements), out _);
        IReadOnlyList<Boundary> first = engine.LastBoundaries;

        // Same request, same text, but the second paragraph now names English.
        engine.Reset();
        engine.Settings = settings;
        engine.PrepareAndLayout(Elements(languageOfSecondParagraph: "en"), out _);
        IReadOnlyList<Boundary> second = engine.LastBoundaries;

        AssertThat(ReferenceEquals(first, second)).OverrideFailureMessage(
            "the boundaries must be rebuilt when a paragraph switches language").IsFalse();

        foreach (Boundary boundary in first)
        {
            AssertThat(boundary.Reason.EndsWith(":UAX14", System.StringComparison.Ordinal)).IsFalse();
        }

        bool anyUnicode = false;

        foreach (Boundary boundary in second)
        {
            if (boundary.Reason.EndsWith(":UAX14", System.StringComparison.Ordinal))
                anyUnicode = true;
        }

        AssertThat(anyUnicode).OverrideFailureMessage(
            "the rebuilt boundaries must use the rule the paragraph named").IsTrue();
    }

    /// <summary>
    /// Every segment carries the language of its paragraph. The segment needs it because shaping comes before
    /// paragraph-level resolution: a shaper that does not know the language cannot ask a font for that
    /// language's forms, and display forms are chosen in the same phase.
    /// </summary>
    [TestCase]
    public void SegmentsCarryTheLanguageOfTheirParagraph()
    {
        using var preparer = new ContentPreparer();
        PreparedContent prepared = preparer.Prepare(Elements(languageOfSecondParagraph: "en"));

        var languageOfParagraph = new Dictionary<int, string?>();

        foreach (TextSegment segment in prepared.Segments)
        {
            if (!languageOfParagraph.ContainsKey(segment.ParagraphIndex))
                languageOfParagraph[segment.ParagraphIndex] = segment.LanguageTag;

            // Empty stands for "no language": the assertion compares tags, and null is not a tag.
            string expected = languageOfParagraph[segment.ParagraphIndex] ?? string.Empty;

            AssertThat(segment.LanguageTag ?? string.Empty).OverrideFailureMessage(
                    $"a segment of paragraph {segment.ParagraphIndex} carries that paragraph's language")
                .IsEqual(expected);
        }

        AssertThat(languageOfParagraph[1]).IsEqual("en");
        AssertThat((object?)languageOfParagraph[0]).IsNull();
    }

    // ── Helpers ──

    /// <summary>A Chinese request with a middle paragraph that names English.</summary>
    private static TypographySettings SwitchRequest() => new()
    {
        MaxWidth = 200f,
        LanguageTag = "zh-Hans",
    };

    private static ResolvedTypography[] PerParagraph(
        TypographySettings settings,
        PreparedContent prepared)
    {
        var resolved = new ResolvedTypography[prepared.Paragraphs.Count];

        for (int i = 0; i < prepared.Paragraphs.Count; i++)
            resolved[i] = LanguageProfileRegistry.Shared.Resolve(settings, prepared.Paragraphs[i].Settings);

        return resolved;
    }

    private static PreparedContent Prepare()
    {
        using var preparer = new ContentPreparer();
        return preparer.Prepare(Elements(languageOfSecondParagraph: "en"));
    }

    private static DrawElement[] Elements(string? languageOfSecondParagraph) =>
    [
        Text("中文第一段，包含Latin词。", null),
        Break(),
        // No space between the Latin word and the Chinese one: that adjacency is what the script-spacing rule is
        // about, and a space would hide it.
        Text("English paragraph with CJK中文mixed in.",
            languageOfSecondParagraph is null ? null : new ParagraphSettings { LanguageTag = languageOfSecondParagraph }),
        Break(),
        Text("中文第三段，也包含Latin词。", null),
    ];

    private static DrawElement[] Clone(DrawElement[] source) => source;

    private static DrawElement Text(string text, ParagraphSettings? paragraph) => new()
    {
        Type = DrawElement.ElementType.Text,
        Text = text,
        Font = ThemeDB.FallbackFont,
        FontSize = 16,
        Color = new Color(0.1f, 0.1f, 0.1f),
        ParagraphSettings = paragraph,
    };

    private static DrawElement Break() => new()
    {
        Type = DrawElement.ElementType.Text,
        Font = ThemeDB.FallbackFont,
        FontSize = 16,
        IsParagraphBreak = true,
    };

    private static bool ParagraphUsesUnicodeRule(
        List<Boundary> boundaries,
        PreparedContent prepared,
        int paragraphIndex)
    {
        for (int i = 0; i + 1 < prepared.Segments.Count; i++)
        {
            if (prepared.Segments[i].ParagraphIndex != paragraphIndex)
                continue;

            if (boundaries[i].Reason.EndsWith(":UAX14", System.StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static float WidestGapInParagraph(
        List<Boundary> boundaries,
        PreparedContent prepared,
        int paragraphIndex)
    {
        float widest = 0f;

        for (int i = 0; i + 1 < prepared.Segments.Count; i++)
        {
            if (prepared.Segments[i].ParagraphIndex != paragraphIndex)
                continue;

            if (boundaries[i].BaseSpacing > widest)
                widest = boundaries[i].BaseSpacing;
        }

        return widest;
    }
}
