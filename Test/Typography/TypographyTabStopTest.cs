namespace GodotNodeExtension.Tests.Typography;

using System.Collections.Generic;
using GdUnit4;
using Godot;
using GodotNodeExtension.Component.Typography.Core;
using GodotNodeExtension.Component.Typography.Core.Model;
using static GdUnit4.Assertions;

/// <summary>
/// Specification for tab stops: a tabulation character is an alignment instruction, not a character with a width.
/// <para>
/// What the layout has to get right is where the content after a tab stands — its left edge, its centre, its right
/// edge or its decimal separator at the stop — and that a tab never becomes a line break: the stop is a position the
/// author named, so the content that belongs to it stays with it.
/// </para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class TypographyTabStopTest
{
    /// <summary>A left stop sends the content after the tab to the stop's position.</summary>
    [TestCase]
    public void ALeftStopPlacesTheContentAtTheStop()
    {
        LayoutLine line = FirstLine("名称\t值", [new TabStop(96f)]);

        LayoutElement content = TextElementAfterTab(line);
        AssertThat(content.Position.X).IsEqual(96f);
    }

    /// <summary>A right stop lines the content's right edge up with the stop.</summary>
    [TestCase]
    public void ARightStopPlacesTheContentsRightEdgeAtTheStop()
    {
        LayoutLine line = FirstLine("名称\t值", [new TabStop(96f, TabAlignment.Right)]);

        LayoutElement content = TextElementAfterTab(line);
        AssertThat(content.Position.X + content.Size.X).IsEqual(96f);
    }

    /// <summary>A centre stop centres the content on the stop.</summary>
    [TestCase]
    public void ACentreStopCentresTheContentOnTheStop()
    {
        LayoutLine line = FirstLine("名称\t值", [new TabStop(96f, TabAlignment.Center)]);

        LayoutElement content = TextElementAfterTab(line);
        AssertThat(content.Position.X + (content.Size.X * 0.5f)).IsEqual(96f);
    }

    /// <summary>
    /// A decimal stop puts the separator at the stop, which is how a column of numbers lines up.
    /// </summary>
    [TestCase]
    public void ADecimalStopPutsTheSeparatorAtTheStop()
    {
        LayoutLine line = FirstLine("合计\t12.5", [new TabStop(96f, TabAlignment.DecimalPoint)]);

        LayoutElement content = TextElementAfterTab(line);

        // The separator sits at the stop, so the content starts before it: the element's left edge is left of the
        // stop by however much of it comes before the separator.
        AssertThat(content.Position.X < 96f).OverrideFailureMessage(
            $"the number starts at {content.Position.X:F3} and its separator belongs at 96").IsTrue();
        AssertThat(content.Position.X + content.Size.X > 96f).IsTrue();
    }

    /// <summary>With no explicit stop past the pen, the automatic stops take over (four ems by default here).</summary>
    [TestCase]
    public void AutomaticStopsFillInPastTheLastExplicitOne()
    {
        LayoutLine line = FirstLine("名称\t值", [], defaultTabStopEm: 4f);

        LayoutElement content = TextElementAfterTab(line);

        // Four ems at 16px from the line's start.
        AssertThat(content.Position.X).IsEqual(64f);
    }

    /// <summary>
    /// A tab is not a break: neither side of it may end a line, because the content after it belongs to the stop.
    /// </summary>
    [TestCase]
    public void ATabIsNotABreak()
    {
        DrawElement[] elements = [Text("名称\t值")];

        using var engine = new TypographyEngine(new TypographySettings
        {
            MaxWidth = 200f,
            LanguageTag = "zh-Hans",
            FirstLineIndent = 0,
            Alignment = TextAlignment.Left,
            TabStops = [new TabStop(96f)],
        });

        engine.PrepareAndLayout(elements, out _);

        IReadOnlyList<Boundary> boundaries = engine.LastBoundaries;
        bool sawTabBoundary = false;

        for (int i = 0; i + 1 < boundaries.Count; i++)
        {
            if (boundaries[i].Reason != "TabStop")
                continue;

            sawTabBoundary = true;
            AssertThat(boundaries[i].ForbiddenToBreak).OverrideFailureMessage(
                "a break on either side of a tab would move the content away from its stop").IsTrue();
        }

        AssertThat(sawTabBoundary).OverrideFailureMessage(
            "the tab produced no boundary, so nothing was checked").IsTrue();
    }

    /// <summary>
    /// A stop that names a leader hands the consumer the gap and the character: the tab's own element carries both,
    /// so the dots of a table of contents can be drawn without shaping anything at layout time.
    /// </summary>
    [TestCase]
    public void ALeaderRecordsTheGapAndTheCharacter()
    {
        LayoutLine line = FirstLine("名称	值", [new TabStop(96f, TabAlignment.Left, '.')]);

        LayoutElement marker = TabElement(line);

        // The text before the tab is two characters wide, so the gap is 96 - 32.
        AssertThat(marker.Size.X).OverrideFailureMessage(
            $"the gap is {marker.Size.X:F3}px wide instead of 64").IsEqual(64f);
        AssertThat(marker.Text).IsEqual(".");
    }

    /// <summary>
    /// A decimal stop on content with no separator falls back to a left stop, which is what a column of labels and
    /// a column of numbers should do when they share one set of stops.
    /// </summary>
    [TestCase]
    public void ADecimalStopWithoutASeparatorFallsBackToLeft()
    {
        LayoutLine line = FirstLine("合计	值", [new TabStop(96f, TabAlignment.DecimalPoint)]);

        LayoutElement content = TextElementAfterTab(line);
        AssertThat(content.Position.X).OverrideFailureMessage(
            "content with no separator lines up at the stop like a left stop").IsEqual(96f);
    }

    // ── Helpers ──

    private static LayoutLine FirstLine(string text, List<TabStop> stops, float? defaultTabStopEm = null)
    {
        DrawElement[] elements = [Text(text)];

        using var engine = new TypographyEngine(new TypographySettings
        {
            MaxWidth = 240f,
            LanguageTag = "zh-Hans",
            FirstLineIndent = 0,
            Alignment = TextAlignment.Left,
            TabStops = stops,
            DefaultTabStopEm = defaultTabStopEm,
        });

        List<LayoutLine> lines = engine.PrepareAndLayout(elements, out _);

        AssertThat(lines.Count).IsGreater(0);
        return lines[0];
    }

    private static DrawElement Text(string text) => new()
    {
        Type = DrawElement.ElementType.Text,
        Text = text,
        Font = ThemeDB.FallbackFont,
        FontSize = 16,
        Color = new Color(0.1f, 0.1f, 0.1f),
    };

    /// <summary>The tab's own marker element, which carries the gap and the leader character.</summary>
    private static LayoutElement TabElement(LayoutLine line)
    {
        foreach (LayoutElement element in line.Elements)
        {
            if (element.CharClass == CharacterClass.Tab)
                return element;
        }

        throw new System.InvalidOperationException("the line carried no tab");
    }

    /// <summary>
    /// The first element with content that stands after the tab, which is the one the stop places. (Taking the
    /// first element with content at all would take the text *before* the tab, which is what an earlier version of
    /// this helper did - and it made four of these cases fail against a correct implementation.)
    /// </summary>
    private static LayoutElement TextElementAfterTab(LayoutLine line)
    {
        bool pastTab = false;

        foreach (LayoutElement element in line.Elements)
        {
            if (element.CharClass == CharacterClass.Tab)
            {
                pastTab = true;
                continue;
            }

            if (pastTab && !string.IsNullOrEmpty(element.Text))
                return element;
        }

        throw new System.InvalidOperationException("the line carried no content after its tab");
    }
}
