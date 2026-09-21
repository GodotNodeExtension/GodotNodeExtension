namespace GodotNodeExtension.Tests.Typography;

using System;
using System.Collections.Generic;
using GdUnit4;
using Godot;
using GodotNodeExtension.Component.Typography.Core;
using GodotNodeExtension.Component.Typography.Core.Model;
using GodotNodeExtension.Component.Typography.Languages;
using static GdUnit4.Assertions;

/// <summary>
/// Specification for the boundary model: it must describe the behaviour the break and adjustment stages
/// apply today, exactly.
/// <para>
/// This is what makes "produce boundaries but do not consume them yet" a safe step. The assertions below
/// re-derive each decision from the rules the stages use (<see cref="CharClassifier"/> and
/// <see cref="TextSegment.CanBreakAfter"/>) and compare it with what the boundary claims, so a boundary
/// that quietly disagrees with the behaviour it is meant to replace fails here instead of surfacing
/// later as a silent layout change.
/// </para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class TypographyBoundaryTest
{
    /// <summary>Every adjacent pair has a boundary, in order, and no pair is skipped.</summary>
    [TestCase]
    public void EveryAdjacentPairHasABoundary()
    {
        var (prepared, typography) = Prepare(MixedCjkLatin());
        var boundaries = BoundaryBuilder.Build(prepared, typography);

        AssertThat(boundaries.Count).IsEqual(prepared.Segments.Count - 1);

        for (int i = 0; i < boundaries.Count; i++)
        {
            AssertThat(boundaries[i].LeftCluster).IsEqual(i);
            AssertThat(boundaries[i].RightCluster).IsEqual(i + 1);
        }
    }

    /// <summary>
    /// The gap decision must match the existing rule: a gap exists exactly where
    /// <c>LineAdjuster</c>'s CJK/Latin boundary test says it does, and its size is the em-based spacing.
    /// Digits are covered on purpose — the classifier treats them as Latin, so a CJK/digit pair gets a
    /// gap today.
    /// </summary>
    [TestCase]
    public void GapDecisionsMatchTheExistingAdjustmentRule()
    {
        foreach (var (name, elements, settings) in AllScenarios())
        {
            var (prepared, typography) = Prepare(elements, settings);
            var boundaries = BoundaryBuilder.Build(prepared, typography);
            var segments = prepared.Segments;

            for (int i = 0; i < boundaries.Count; i++)
            {
                // A number with its unit is one unit, not a script boundary: the CJK/Latin gap is not inserted
                // between 12 and 个, which is the point of treating them as a pair.
                char left = LastChar(segments[i]);
                char right = FirstChar(segments[i + 1]);

                bool numericPair = left != '\0' && right != '\0'
                                   && (CharClassifier.IsUnbreakablePair(left, right)
                                       || NumberUnitBinding.Binds(
                                           CharBefore(segments, i), left, right));

                bool expectedGap = typography.EnableCjkLatinSpacing
                                   && !numericPair
                                   && IsCjkLatinBoundary(segments[i], segments[i + 1]);
                bool reportedGap = boundaries[i].BaseSpacing > 0f;

                AssertThat(reportedGap).OverrideFailureMessage(
                        $"{name}: boundary {i} between \"{segments[i].Text}\" and \"{segments[i + 1].Text}\" " +
                        $"reports spacing {boundaries[i].BaseSpacing} but the adjustment rule says {expectedGap}")
                    .IsEqual(expectedGap);

                if (expectedGap)
                {
                    float expectedSpacing = segments[i].Source.FontSize * typography.CjkLatinSpacingEm;
                    AssertThat(Math.Abs(boundaries[i].BaseSpacing - expectedSpacing) < 0.001f)
                        .OverrideFailureMessage($"{name}: boundary {i} gap size").IsTrue();
                }
            }
        }
    }

    /// <summary>
    /// Prohibition must match the classifier at the resolved strictness, including the case where the
    /// request switched prohibition off entirely.
    /// </summary>
    [TestCase]
    public void ProhibitionDecisionsMatchTheClassifier()
    {
        foreach (var (name, elements, settings) in AllScenarios())
        {
            var (prepared, typography) = Prepare(elements, settings);
            var boundaries = BoundaryBuilder.Build(prepared, typography);
            var segments = prepared.Segments;
            var level = typography.EnableLineProhibition ? typography.ProhibitionLevel : ProhibitionLevel.None;

            for (int i = 0; i < boundaries.Count; i++)
            {
                char right = FirstChar(segments[i + 1]);
                char left = LastChar(segments[i]);

                if (right != '\0')
                {
                    AssertThat(boundaries[i].ForbiddenAtLineStart).OverrideFailureMessage(
                            $"{name}: boundary {i} line-start prohibition for '{right}'")
                        .IsEqual(CharClassifier.IsProhibitedAtLineStart(right, level));
                }

                if (left != '\0')
                {
                    AssertThat(boundaries[i].ForbiddenAtLineEnd).OverrideFailureMessage(
                            $"{name}: boundary {i} line-end prohibition for '{left}'")
                        .IsEqual(CharClassifier.IsProhibitedAtLineEnd(left, level));
                }

                // The breaker refuses a candidate when the left segment cannot end a line, and treats an
                // unbreakable pair (——, ……, a digit with its affix, a number with its unit) as unsplittable.
                bool expectedBreakable = segments[i].CanBreakAfter
                                         && !(left != '\0' && right != '\0'
                                              && (CharClassifier.IsUnbreakablePair(left, right)
                                                  || NumberUnitBinding.Binds(left, right)));

                AssertThat(!boundaries[i].ForbiddenToBreak).OverrideFailureMessage(
                        $"{name}: boundary {i} break permission between \"{segments[i].Text}\" and " +
                        $"\"{segments[i + 1].Text}\"")
                    .IsEqual(expectedBreakable);
            }
        }
    }

    /// <summary>
    /// A boundary that switches scripts is reported as such, and a plain pair is not. This pins the
    /// classification itself rather than its consequences.
    /// </summary>
    [TestCase]
    public void ScriptChangesAreClassifiedAsSuch()
    {
        var (prepared, typography) = Prepare(MixedCjkLatin());
        var boundaries = BoundaryBuilder.Build(prepared, typography);
        int scriptChanges = 0;

        foreach (var boundary in boundaries)
        {
            bool rolesDiffer = boundary.LeftScript != boundary.RightScript;
            if (boundary.Kind == BoundaryKind.ScriptChange)
            {
                scriptChanges++;
                AssertThat(rolesDiffer).OverrideFailureMessage(
                    $"boundary {boundary.LeftCluster} claims a script change but both roles are " +
                    $"{boundary.LeftScript}").IsTrue();
            }
        }

        AssertThat(scriptChanges > 0).OverrideFailureMessage(
            "the mixed CJK/Latin sample must produce script-change boundaries").IsTrue();
    }

    /// <summary>
    /// Turning prohibition off in the request must be visible in the boundaries, since the profile/request
    /// merge is what the stage will read once it consumes them.
    /// </summary>
    [TestCase]
    public void RequestOverridesAreVisibleInBoundaries()
    {
        var settings = new TypographySettings { MaxWidth = 200f, LanguageTag = "zh-Hans", EnableLineProhibition = false };
        var (prepared, typography) = Prepare(PunctuationHeavy(), settings);
        var boundaries = BoundaryBuilder.Build(prepared, typography);

        foreach (var boundary in boundaries)
        {
            AssertThat(boundary.ForbiddenAtLineStart).OverrideFailureMessage(
                "prohibition was switched off for this request").IsFalse();
            AssertThat(boundary.ForbiddenAtLineEnd).IsFalse();
        }
    }

    /// <summary>
    /// A soft hyphen is a break opportunity the author placed. UAX #14 gives it the BA class and the segmenter
    /// splits the word there, so author-controlled hyphenation — the kind an editor inserts — already works
    /// through the Unicode path, without any pattern dictionary.
    /// </summary>
    [TestCase]
    public void SoftHyphenOffersABreakOpportunity()
    {
        const string text = "co\u00ADoperate";
        var elements = new List<DrawElement> { Text(text) };
        var settings = new TypographySettings { MaxWidth = 200f, LanguageTag = "en" };

        using var preparer = new ContentPreparer();
        var prepared = preparer.Prepare(elements.ToArray().AsSpan());

        AssertThat(prepared.Segments.Count).OverrideFailureMessage(
            "the segmenter did not split the word at the soft hyphen").IsEqual(2);

        var boundaries = BoundaryBuilder.Build(prepared, LanguageProfileRegistry.Shared.Resolve(settings));

        AssertThat(boundaries.Count).IsEqual(1);
        AssertThat(boundaries[0].ForbiddenToBreak).OverrideFailureMessage(
            "the soft hyphen must be a break opportunity, not a forbidden position").IsFalse();

        // The hyphen itself stays with the part before the break, as UAX #14's LB21 requires.
        AssertThat(boundaries[0].ForbiddenAtLineEnd).IsFalse();
    }

    /// <summary>
    /// A number keeps its unit, and this holds under every rule set: the Unicode algorithm covers the Western
    /// cases (10%, $10) and this covers the CJK ones (10米, 第3号).
    /// </summary>
    [TestCase]
    public void NumberStaysWithItsUnit()
    {
        string[] bound = ["10米", "5公斤", "100元", "第3号", "2024年"];
        string[] free = ["月光", "办公", "三米"];

        foreach (string profile in new[] { "zh-Hans", "en" })
        {
            var settings = new TypographySettings { MaxWidth = 200f, LanguageTag = profile };
            var typography = LanguageProfileRegistry.Shared.Resolve(settings);

            foreach (string text in bound)
            {
                var boundaries = BoundariesOf(text, typography);

                AssertThat(boundaries.Count > 0).OverrideFailureMessage(
                    $"{profile}: \"{text}\" produced no boundary").IsTrue();

                foreach (var boundary in boundaries)
                {
                    AssertThat(boundary.ForbiddenToBreak).OverrideFailureMessage(
                        $"{profile}: \"{text}\" must not break between a number and its unit").IsTrue();
                }
            }

            foreach (string text in free)
            {
                foreach (var boundary in BoundariesOf(text, typography))
                {
                    AssertThat(boundary.ForbiddenToBreak).OverrideFailureMessage(
                        $"{profile}: \"{text}\" has nothing to bind").IsFalse();
                }
            }
        }
    }

    /// <summary>The pair is also classified as a numeric pair, so a dump shows why it does not break.</summary>
    [TestCase]
    public void ANumberWithItsUnitIsClassifiedAsNumeric()
    {
        var settings = new TypographySettings { MaxWidth = 200f, LanguageTag = "zh-Hans" };
        var typography = LanguageProfileRegistry.Shared.Resolve(settings);
        var boundaries = BoundariesOf("10米", typography);

        AssertThat(boundaries.Count).IsEqual(1);
        AssertThat(boundaries[0].Kind).IsEqual(BoundaryKind.Numeric);
    }

    private static List<Boundary> BoundariesOf(string text, ResolvedTypography typography)
    {
        using var preparer = new ContentPreparer();
        var prepared = preparer.Prepare([Text(text)]);
        return BoundaryBuilder.Build(prepared, typography);
    }

    // ── Helpers ──

    private static (PreparedContent Prepared, ResolvedTypography Typography) Prepare(
        List<DrawElement> elements,
        TypographySettings? settings = null)
    {
        var effective = settings ?? new TypographySettings { MaxWidth = 200f };
        using var preparer = new ContentPreparer();
        var prepared = preparer.Prepare(elements.ToArray().AsSpan());
        return (prepared, LanguageProfileRegistry.Shared.Resolve(effective));
    }

    private static IEnumerable<(string Name, List<DrawElement> Elements, TypographySettings Settings)> AllScenarios()
    {
        yield return ("mixed-cjk-latin", MixedCjkLatin(), new TypographySettings { MaxWidth = 200f });
        yield return ("punctuation", PunctuationHeavy(), new TypographySettings { MaxWidth = 200f });
        yield return ("digits", Digits(), new TypographySettings { MaxWidth = 200f });
    }

    private static List<DrawElement> MixedCjkLatin() => [Text("在Godot引擎中使用C#开发时，可以用SkiaSharp渲染。")];

    private static List<DrawElement> PunctuationHeavy() => [Text("测试：「引号」的（括号），，。。！！？？、、；；")];

    private static List<DrawElement> Digits() => [Text("共12个文件，占25%，金额￥100元。")];

    private static DrawElement Text(string text) => new()
    {
        Type = DrawElement.ElementType.Text,
        Text = text,
        Font = ThemeDB.FallbackFont,
        FontSize = 16,
        Color = new Color(0.1f, 0.1f, 0.1f),
    };

    /// <summary>
    /// The CJK/Latin boundary rule as the adjustment stage applies it today: the two neighbours are on
    /// opposite sides of the ideograph/Latin split, with digits counting as Latin.
    /// </summary>
    private static bool IsCjkLatinBoundary(in TextSegment left, in TextSegment right)
    {
        if (left.CharClass is CharacterClass.NonText or CharacterClass.Block
            or CharacterClass.BlockStart or CharacterClass.BlockEnd or CharacterClass.Break)
        {
            return false;
        }

        if (right.CharClass is CharacterClass.NonText or CharacterClass.Block
            or CharacterClass.BlockStart or CharacterClass.BlockEnd or CharacterClass.Break)
        {
            return false;
        }

        CharacterClass clsLeft = left.CharClass;
        CharacterClass clsRight = right.CharClass;

        return (clsLeft == CharacterClass.Ideograph && clsRight == CharacterClass.Latin)
               || (clsLeft == CharacterClass.Latin && clsRight == CharacterClass.Ideograph);
    }

    /// <summary>
    /// The character before the left character of a pair straddling two segments, mirroring the position the
    /// boundary rules use: the code point just before the left cluster's last one.
    /// </summary>
    /// <param name="segments">The prepared segments.</param>
    /// <param name="index">Index of the left segment of the pair.</param>
    /// <returns>The character, or '\0'.</returns>
    private static char CharBefore(List<TextSegment> segments, int index)
    {
        string left = segments[index].Text;

        if (left.Length >= 2)
            return left[^2];

        return index > 0 && segments[index - 1].Text.Length > 0 ? segments[index - 1].Text[^1] : '\0';
    }

    private static char FirstChar(in TextSegment segment) =>
        string.IsNullOrEmpty(segment.Text) ? '\0' : segment.Text[0];

    private static char LastChar(in TextSegment segment) =>
        string.IsNullOrEmpty(segment.Text) ? '\0' : segment.Text[^1];
}
