namespace GodotNodeExtension.Tests.Typography;

using System;
using GdUnit4;
using GodotNodeExtension.Component.Typography.Core;
using GodotNodeExtension.Component.Typography.Core.Model;
using GodotNodeExtension.Component.Typography.Languages;
using static GdUnit4.Assertions;

/// <summary>
/// Specification for the prohibition class sets: which characters a language refuses to start or end a line
/// with.
/// <para>
/// The engine used to carry one hard-coded pair of strings, so every document obeyed the same conventions and a
/// Japanese document could not stop a line at a small kana. The cases below pin the differences that are the
/// point of the layer — Chinese does not prohibit the ellipsis at line start at its basic level, Japanese and
/// Korean prohibit characters Chinese does not — and the two registration failures a language pack must not be
/// able to slip past.
/// </para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class TypographyKinsokuClassSetTest
{
    /// <summary>Chinese: the ellipsis is only prohibited at line start from the strict level on (clreq §6.1.1).</summary>
    [TestCase]
    public void ChineseProhibitsTheEllipsisAtLineStartOnlyWhenStrict()
    {
        KinsokuClassSet chinese = KinsokuClassSets.Find(KinsokuClassSets.ChineseId)!;

        AssertThat(CharClassifier.IsProhibitedAtLineStart('\u2026', chinese, ProhibitionLevel.Basic)).IsFalse();
        AssertThat(CharClassifier.IsProhibitedAtLineStart('\u2026', chinese, ProhibitionLevel.Strict)).IsTrue();

        // The solidus is the other way round: GB-style adds it at line *end*, not line start.
        AssertThat(CharClassifier.IsProhibitedAtLineEnd('\uFF0F', chinese, ProhibitionLevel.Basic)).IsFalse();
        AssertThat(CharClassifier.IsProhibitedAtLineEnd('\uFF0F', chinese, ProhibitionLevel.Gb)).IsTrue();
    }

    /// <summary>
    /// Japanese stops a line at a small kana, a prolonged sound mark and an iteration mark (jlreq §3.1.7);
    /// Chinese does not, because its classes do not name them.
    /// </summary>
    [TestCase]
    public void JapaneseProhibitsSmallKanaAndChineseDoesNot()
    {
        KinsokuClassSet japanese = KinsokuClassSets.Find(KinsokuClassSets.JapaneseId)!;
        KinsokuClassSet chinese = KinsokuClassSets.Find(KinsokuClassSets.ChineseId)!;

        foreach (char c in new[] { '\u3063', '\u30FC', '\u3005', '\u30FB' })
        {
            AssertThat(CharClassifier.IsProhibitedAtLineStart(c, japanese, ProhibitionLevel.Basic))
                .OverrideFailureMessage($"Japanese must not start a line with U+{(int)c:X4}").IsTrue();
        }

        AssertThat(CharClassifier.IsProhibitedAtLineStart('\u3063', chinese, ProhibitionLevel.Basic))
            .OverrideFailureMessage("Chinese does not prohibit a small kana at line start").IsFalse();
    }

    /// <summary>Korean stops a line at its closing parentheses, hyphens, stops and iteration marks (klreq §7.1.2).</summary>
    [TestCase]
    public void KoreanProhibitsItsOwnClasses()
    {
        KinsokuClassSet korean = KinsokuClassSets.Find(KinsokuClassSets.KoreanId)!;

        AssertThat(CharClassifier.IsProhibitedAtLineStart('\uFF09', korean, ProhibitionLevel.Basic)).IsTrue();
        AssertThat(CharClassifier.IsProhibitedAtLineStart('\u2013', korean, ProhibitionLevel.Basic)).IsTrue();
        AssertThat(CharClassifier.IsProhibitedAtLineStart('\u3002', korean, ProhibitionLevel.Basic)).IsTrue();
        AssertThat(CharClassifier.IsProhibitedAtLineEnd('\uFF08', korean, ProhibitionLevel.Basic)).IsTrue();
    }

    /// <summary>
    /// A level of <see cref="ProhibitionLevel.None"/> switches every set off, which is how a document (or a
    /// language) says it applies no prohibition rules at all.
    /// </summary>
    [TestCase]
    public void TheNoneLevelSwitchesEverySetOff()
    {
        foreach (string id in KinsokuClassSets.RegisteredIds)
        {
            KinsokuClassSet set = KinsokuClassSets.Find(id)!;

            AssertThat(set.LineStartAt(ProhibitionLevel.None).Length).IsEqual(0);
            AssertThat(set.LineEndAt(ProhibitionLevel.None).Length).IsEqual(0);
            AssertThat(CharClassifier.IsProhibitedAtLineStart('\u3002', set, ProhibitionLevel.None)).IsFalse();
        }
    }

    /// <summary>
    /// A profile naming a class set nobody registered is rejected when it is registered, not when it lays text
    /// out: "the set is missing" would otherwise quietly mean "no prohibition rules".
    /// </summary>
    [TestCase]
    public void AProfileNamingAnUnknownClassSetIsRejected()
    {
        var registry = LanguageProfileRegistry.CreateDefault();

        ArgumentException error = AssertThrows<ArgumentException>(() => registry.Register(new LanguageProfile(
            "xx-test",
            ["und"],
            new TypographyParameters { ProhibitionClassSetId = "not-a-set" },
            "test profile")));

        AssertThat(error.Message.Contains("not-a-set", StringComparison.Ordinal)).IsTrue();
    }

    /// <summary>Vertical writing has contracts but no implementation, so a profile asking for it is rejected.</summary>
    [TestCase]
    public void AProfileAskingForLeftToRightColumnsIsRejected()
    {
        var registry = LanguageProfileRegistry.CreateDefault();

        // Right-to-left columns are the mode the axis plumbing lays out, so a profile may declare it...
        registry.Register(new LanguageProfile(
            "xx-vertical",
            ["und"],
            new TypographyParameters { WritingMode = WritingMode.VerticalRl },
            "test profile"));

        // ...while the other vertical mode has nothing behind it and is refused where the other conflicts are.
        ArgumentException error = AssertThrows<ArgumentException>(() => registry.Register(new LanguageProfile(
            "xx-vertical-lr",
            ["und"],
            new TypographyParameters { WritingMode = WritingMode.VerticalLr },
            "test profile")));

        AssertThat(error.Message.Contains("VerticalLr", StringComparison.Ordinal)).IsTrue();
    }

    /// <summary>
    /// A band reserved above the line cannot carry an annotation set down a column: the band is as deep as the
    /// annotation is tall, while a column runs along the base character and needs room beside it. The two settings
    /// together describe a layout that does not exist, so the profile is refused where the other conflicts are.
    /// </summary>
    [TestCase]
    public void AProfileReservingAboveForAColumnAnnotationIsRejected()
    {
        var registry = LanguageProfileRegistry.CreateDefault();

        ArgumentException error = AssertThrows<ArgumentException>(() => registry.Register(new LanguageProfile(
            "xx-column-above",
            ["und"],
            new TypographyParameters
            {
                RubyPlacement = RubyPlacement.ReserveAbove,
                RubyOrientation = RubyOrientation.Vertical,
            },
            "test profile")));

        AssertThat(error.Message.Contains(nameof(RubyOrientation.Vertical), StringComparison.Ordinal)).IsTrue();

        // The placement that does have room for a column registers without complaint, so the check is about the
        // combination rather than about columns as such.
        registry.Register(new LanguageProfile(
            "xx-column-beside",
            ["und"],
            new TypographyParameters
            {
                RubyPlacement = RubyPlacement.ReserveBeside,
                RubyOrientation = RubyOrientation.Vertical,
            },
            "test profile"));
    }

    private static ArgumentException AssertThrows<TException>(Action action) where TException : ArgumentException
    {
        try
        {
            action();
        }
        catch (TException error)
        {
            return error;
        }

        throw new InvalidOperationException($"expected {typeof(TException).Name}, but nothing was thrown");
    }
}
