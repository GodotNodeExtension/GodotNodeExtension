namespace GodotNodeExtension.Tests.Typography;

using System.Collections.Generic;
using GdUnit4;
using Godot;
using GodotNodeExtension.Component.Typography.Core;
using GodotNodeExtension.Component.Typography.Core.Model;
using GodotNodeExtension.Component.Typography.Core.Unicode;
using static GdUnit4.Assertions;

/// <summary>
/// Specification for a paragraph that is written right to left (the single-direction half of RTL support).
/// <para>
/// A right-to-left paragraph has its start edge on the right, so the line is mirrored inside its own interval: text
/// begins at the right edge, the first-line indent appears on the right of the first line, and the element order
/// becomes display order. Text that mixes directions is a different problem — that is what the bidi pass and the
/// levels are for, and this file deliberately does not claim it.
/// </para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class TypographyRightToLeftTest
{
    private const string Hebrew = "\u05D0\u05D1\u05D2\u05D3";

    /// <summary>Two words, so the line has more than one element to order.</summary>
    private const string HebrewWords = "\u05D0\u05D1 \u05D2\u05D3";

    /// <summary>
    /// A right-to-left line fills from the right edge, and its elements come out in display order: left to right, as
    /// a reader of that line sees them, which puts the logical first element last in the array.
    /// </summary>
    [TestCase]
    public void ARightToLeftParagraphFillsFromTheRightEdge()
    {
        LayoutLine line = FirstLine(HebrewWords, TextDirection.RightToLeft, maxWidth: 200f, indent: 0);

        AssertThat(line.Elements.Count).IsGreater(1);

        LayoutElement rightmost = line.Elements[^1];

        AssertThat(rightmost.Position.X + rightmost.Size.X).OverrideFailureMessage(
            "the rightmost element must end at the content's right edge").IsEqual(200f);

        for (int i = 1; i < line.Elements.Count; i++)
        {
            AssertThat(line.Elements[i].Position.X >= line.Elements[i - 1].Position.X).OverrideFailureMessage(
                $"element {i} is left of element {i - 1}, so the order is not display order").IsTrue();
        }
    }

    /// <summary>The first-line indent moves to the right edge with the rest of the line.</summary>
    [TestCase]
    public void TheFirstLineIndentAppearsOnTheRight()
    {
        LayoutLine line = FirstLine(Hebrew, TextDirection.RightToLeft, maxWidth: 200f, indent: 1);

        LayoutElement rightmost = line.Elements[^1];
        float rightEdge = rightmost.Position.X + rightmost.Size.X;

        // One em at 16px, measured from the right edge.
        AssertThat(rightEdge).OverrideFailureMessage(
            $"the indented line ends at {rightEdge:F3} instead of 184.000").IsEqual(184f);
    }

    /// <summary>A left-to-right paragraph is untouched: this is the guard that keeps the mirror out of the old path.</summary>
    [TestCase]
    public void LeftToRightParagraphsAreNotMirrored()
    {
        LayoutLine line = FirstLine(Hebrew, TextDirection.LeftToRight, maxWidth: 200f, indent: 0);

        AssertThat(line.Elements[0].Position.X).IsEqual(0f);
    }

    /// <summary>Every character still reaches the output, in some order: mirroring must not lose or duplicate text.</summary>
    [TestCase]
    public void MirroringKeepsEveryCharacter()
    {
        LayoutLine line = FirstLine(Hebrew, TextDirection.RightToLeft, maxWidth: 200f, indent: 0);
        var produced = new List<char>();

        foreach (LayoutElement element in line.Elements)
        {
            if (element.Type != DrawElement.ElementType.Text)
                continue;

            foreach (char c in element.Text ?? string.Empty)
                produced.Add(c);
        }

        produced.Sort();

        var expected = new List<char>(Hebrew);
        expected.Sort();

        AssertThat(new string([.. produced])).OverrideFailureMessage(
            "the mirrored line must still carry every character once").IsEqual(new string([.. expected]));
    }

    /// <summary>
    /// The mirroring table: paired marks are drawn as their mirror image in right-to-left text, and a character
    /// without a mirror is left alone.
    /// </summary>
    [TestCase]
    public void MirrorPairsAreAvailable()
    {
        AssertThat(BidiMirroringData.HasMirror(0x0028)).IsTrue();
        AssertThat(BidiMirroringData.Mirror(0x0028)).IsEqual(0x0029);
        AssertThat(BidiMirroringData.Mirror(0x0029)).IsEqual(0x0028);
        AssertThat(BidiMirroringData.HasMirror(0x05D0)).IsFalse();
        AssertThat(BidiMirroringData.Mirror(0x05D0)).IsEqual(0x05D0);
    }

    // ── Helpers ──

    private static LayoutLine FirstLine(string text, TextDirection direction, float maxWidth, int indent)
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
            Direction = direction,
            FirstLineIndent = indent,
            Alignment = TextAlignment.Left,
        });

        List<LayoutLine> lines = engine.PrepareAndLayout(elements, out _);
        AssertThat(lines.Count).IsGreater(0);
        return lines[0];
    }
}
