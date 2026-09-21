namespace GodotNodeExtension.Tests.Typography;

using System.Collections.Generic;
using System.Text;
using GdUnit4;
using Godot;
using GodotNodeExtension.Component.Typography.Core;
using GodotNodeExtension.Component.Typography.Core.Model;
using static GdUnit4.Assertions;

/// <summary>
/// Specification for automatic hyphenation as the layout uses it: a long word in a narrow column breaks inside
/// itself instead of dropping to the next line whole.
/// <para>
/// The algorithm's own conformance lives in <c>TypographyHyphenationTest</c>; this file is about the pipeline — that
/// the break opportunities the patterns produce reach the line breaker, that the request can switch the behaviour
/// off, and that a language without patterns is untouched (which is what keeps the CJK goldens identical).
/// </para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class TypographyHyphenationLayoutTest
{
    private const string LongWord = "implementation";

    /// <summary>A word too long for the column is broken inside itself rather than moved to the next line.</summary>
    [TestCase]
    public void ALongWordBreaksInsideItselfWhenItHasTo()
    {
        List<LayoutLine> lines = LayOut("A " + LongWord + " of a layout engine, with enough words to fill lines.",
            languageTag: "en", maxWidth: 90f, hyphenation: true);

        AssertThat(lines.Count).IsGreater(1);

        // The word is broken inside itself: the document still carries it whole once the lines are concatenated, but
        // no single line does, and at least one of the lines ends with a hyphen the assembler placed. Which pieces the
        // patterns produce is their business, so the assertions are about that shape.
        AssertThat(LineText(lines)).OverrideFailureMessage("the text lost the word")
            .Contains(LongWord);

        foreach (LayoutLine line in lines)
        {
            AssertThat(LineText(line).Contains(LongWord, System.StringComparison.Ordinal))
                .OverrideFailureMessage($"the word was not broken at all: {Describe(lines)}").IsFalse();
        }

        int withHyphen = 0;

        foreach (LayoutLine line in lines)
        {
            foreach (LayoutElement element in line.Elements)
            {
                if (element.Reason != "HyphenationBreak")
                    continue;

                withHyphen++;
                AssertThat(element.Text).IsEqual("-");
                AssertThat(element.GlyphRun is { Glyphs.Length: > 0 }).OverrideFailureMessage(
                    "the hyphen has shaped glyphs to draw").IsTrue();
            }
        }

        AssertThat(withHyphen).OverrideFailureMessage(
            $"the word was broken across lines without a hyphen: {Describe(lines)}").IsGreater(0);
    }

    /// <summary>Switching hyphenation off keeps the word whole and moves it to the next line.</summary>
    [TestCase]
    public void WithoutHyphenationTheWordMovesToTheNextLineWhole()
    {
        List<LayoutLine> lines = LayOut("A " + LongWord + " of a layout engine, with enough words to fill lines.",
            languageTag: "en", maxWidth: 90f, hyphenation: false);

        AssertThat(lines.Count).IsGreater(1);

        bool wordSeenWhole = false;

        foreach (LayoutLine line in lines)
        {
            if (LineText(line).Contains(LongWord, System.StringComparison.Ordinal))
                wordSeenWhole = true;
        }

        AssertThat(wordSeenWhole).OverrideFailureMessage(
            "without hyphenation the word has to appear whole on one line").IsTrue();
    }

    /// <summary>
    /// A language without patterns is untouched: the same document in Chinese splits into the same elements whether
    /// the request enables hyphenation or not.
    /// </summary>
    [TestCase]
    public void ALanguageWithoutPatternsIsUntouched()
    {
        const string text = "中文排版不使用断词，因为中文可以在任意两个字之间换行，因此这个开关对它没有影响。";

        List<LayoutLine> withHyphenation = LayOut(text, "zh-Hans", 160f, hyphenation: true);
        List<LayoutLine> without = LayOut(text, "zh-Hans", 160f, hyphenation: false);

        AssertThat(ElementCount(withHyphenation)).IsEqual(ElementCount(without));
        AssertThat(Lines(withHyphenation)).IsEqual(Lines(without));
    }

    /// <summary>German hyphenates too: the profile points at German patterns, not at the English ones.</summary>
    [TestCase]
    public void AnotherLanguageBreaksByItsOwnPatterns()
    {
        List<LayoutLine> lines = LayOut("Eine Donaudampfschifffahrtsgesellschaft und mehr Text zum Umbrechen.",
            "de", 110f, hyphenation: true);

        AssertThat(ElementCount(lines)).OverrideFailureMessage(
            "German did not produce any word pieces").IsGreater(7);
    }

    // ── Helpers ──

    private static List<LayoutLine> LayOut(string text, string languageTag, float maxWidth, bool hyphenation)
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
            MaxWidth = maxWidth,
            LanguageTag = languageTag,
            FirstLineIndent = 0,
            Alignment = TextAlignment.Left,
            EnableHyphenation = hyphenation,
        });

        return engine.PrepareAndLayout(elements, out _);
    }

    private static string LineText(LayoutLine line)
    {
        var text = new StringBuilder();

        foreach (LayoutElement element in line.Elements)
        {
            // Elements the assembler synthesised (the hyphen a break adds) carry a reason; the text the word is made
            // of is what the assertions here are about.
            if (element.Type == DrawElement.ElementType.Text && element.Reason is null)
                text.Append(element.Text);
        }

        return text.ToString();
    }

    private static string LineText(List<LayoutLine> lines)
    {
        var text = new StringBuilder();

        foreach (LayoutLine line in lines)
            text.Append(LineText(line));

        return text.ToString();
    }

    private static string Lines(List<LayoutLine> lines)
    {
        var text = new StringBuilder();

        foreach (LayoutLine line in lines)
            text.Append(LineText(line)).Append('|').Append(line.Elements.Count).Append('\n');

        return text.ToString();
    }

    /// <summary>The text of every line, for a failure message that shows what the layout actually did.</summary>
    /// <param name="lines">Lines to describe.</param>
    /// <returns>One line per line, with its text.</returns>
    private static string Describe(List<LayoutLine> lines)
    {
        var text = new StringBuilder();

        foreach (LayoutLine line in lines)
            text.Append('[').Append(LineText(line)).Append(']');

        return text.ToString();
    }

    private static int ElementCount(List<LayoutLine> lines)
    {
        int count = 0;

        foreach (LayoutLine line in lines)
            count += line.Elements.Count;

        return count;
    }
}
