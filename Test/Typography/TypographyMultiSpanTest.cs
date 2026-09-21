namespace GodotNodeExtension.Tests.Typography;

using System;
using System.Collections.Generic;
using GdUnit4;
using Godot;
using GodotNodeExtension.Component.Typography.Core;
using GodotNodeExtension.Component.Typography.Core.Model;
using static GdUnit4.Assertions;

/// <summary>
/// Specification for lines split into more than one interval (C15): text flowing around an exclusion that
/// covers only part of the line.
/// <para>
/// Before this existed a line had one usable interval and the breaker only ever read the first one, so an
/// exclusion in the middle of a line wasted everything to the right of it. The cases below pin the two
/// properties that make the feature real: text continues in the next interval, and nothing is placed inside
/// the exclusion.
/// </para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class TypographyMultiSpanTest
{
    /// <summary>The exclusion: a band from x=90 to x=174 (the shape at 96 is 72 wide with a 6px margin).</summary>
    private const float BandLeft = 90f;

    private const float BandRight = 174f;

    /// <summary>
    /// A line whose only obstacle sits in its middle offers one interval on each side, and the text uses both
    /// instead of ending at the shape.
    /// </summary>
    [TestCase]
    public void TextContinuesInTheSecondInterval()
    {
        var layout = Run(Settings(TextAlignment.Left));
        var line = layout.Lines[0];

        AssertThat(line.Spans.Length).OverrideFailureMessage(
            $"expected two intervals, got {line.Spans.Length}: {string.Join("|", line.Spans)}").IsEqual(2);
        AssertThat(Math.Abs(line.Spans[0].Left - 0f) < 0.01f).IsTrue();
        AssertThat(Math.Abs(line.Spans[0].Right - BandLeft) < 0.01f).IsTrue();
        AssertThat(Math.Abs(line.Spans[1].Left - BandRight) < 0.01f).IsTrue();

        int inFirst = 0;
        int inSecond = 0;

        foreach (var element in line.Elements)
        {
            if (element.SpanIndex == 0)
                inFirst++;
            else if (element.SpanIndex == 1)
                inSecond++;
        }

        AssertThat(inFirst > 0).OverrideFailureMessage("nothing was placed in the left interval").IsTrue();
        AssertThat(inSecond > 0).OverrideFailureMessage(
            "nothing was placed in the right interval, so the line ended at the exclusion").IsTrue();

        // The first interval is narrower than the text, so it must be full: five 16px characters fit in 90px.
        AssertThat(inFirst).IsEqual(5);
    }

    /// <summary>
    /// Every element lies inside the interval it was assigned to, and no element touches the exclusion band.
    /// </summary>
    [TestCase]
    public void NothingIsPlacedInsideTheExclusion()
    {
        foreach (var alignment in new[] { TextAlignment.Left, TextAlignment.Center, TextAlignment.Right })
        {
            var layout = Run(Settings(alignment));

            foreach (var line in layout.Lines)
            {
                foreach (var element in line.Elements)
                {
                    if (element.SpanIndex < 0 || element.SpanIndex >= line.Spans.Length)
                    {
                        AssertThat(false).OverrideFailureMessage(
                            $"{alignment}: element span {element.SpanIndex} is outside {line.Spans.Length} " +
                            "intervals").IsTrue();
                        continue;
                    }

                    LineSpan span = line.Spans[element.SpanIndex];
                    float left = element.Position.X;
                    float right = left + element.Size.X;

                    AssertThat(left >= span.Left - 0.01f && right <= span.Right + 0.01f)
                        .OverrideFailureMessage(
                            $"{alignment}: \"{element.Text}\" at [{left:F2},{right:F2}) is outside its " +
                            $"interval {span}").IsTrue();

                    // The band itself is excluded, so no element may fall inside it.
                    AssertThat(right <= BandLeft + 0.01f || left >= BandRight - 0.01f)
                        .OverrideFailureMessage(
                            $"{alignment}: \"{element.Text}\" at [{left:F2},{right:F2}) reaches into the " +
                            "exclusion").IsTrue();

                    // The laid-out glyphs hang off the element's box and the renderer draws them from their own
                    // origin, so an adjustment has to move both: moving the box alone drew the text where the line
                    // was before the alignment. Multi-interval lines are exactly where that half of the fix went
                    // missing - a dump cannot see it, because a dump prints the box and not the origin - so the
                    // agreement is asserted here, right next to the interval the element was placed in.
                    if (element.GlyphRun is { } run)
                    {
                        AssertThat(Math.Abs(run.Origin.X - element.Position.X) < 0.01f)
                            .OverrideFailureMessage(
                                $"{alignment}: \"{element.Text}\" sits at x={element.Position.X:F2} but its " +
                                $"glyphs start at x={run.Origin.X:F2}")
                            .IsTrue();
                    }
                }
            }
        }
    }

    /// <summary>
    /// Each interval starts at its own left edge: an exclusion in the middle does not shift what sits to its
    /// right, which is what "each interval is a line of its own" means for alignment.
    /// </summary>
    [TestCase]
    public void EachIntervalStartsAtItsOwnEdge()
    {
        foreach (var alignment in new[] { TextAlignment.Left, TextAlignment.Center, TextAlignment.Right })
        {
            var layout = Run(Settings(alignment));

            foreach (var line in layout.Lines)
            {
                for (int spanIndex = 0; spanIndex < line.Spans.Length; spanIndex++)
                {
                    float? first = null;

                    foreach (var element in line.Elements)
                    {
                        if (element.SpanIndex == spanIndex && first is null)
                            first = element.Position.X;
                    }

                    if (first is null)
                        continue;

                    float spanWidth = line.Spans[spanIndex].Right - line.Spans[spanIndex].Left;
                    float content = 0f;

                    foreach (var element in line.Elements)
                    {
                        if (element.SpanIndex == spanIndex)
                            content += element.Size.X;
                    }

                    float slack = spanWidth - content;
                    float expected = alignment switch
                    {
                        TextAlignment.Center => line.Spans[spanIndex].Left + slack * 0.5f,
                        TextAlignment.Right => line.Spans[spanIndex].Left + slack,
                        _ => line.Spans[spanIndex].Left,
                    };

                    AssertThat(Math.Abs(first.Value - expected) < 0.5f).OverrideFailureMessage(
                            $"{alignment}: interval {spanIndex} starts at {first.Value:F2} instead of " +
                            $"{expected:F2} (slack {slack:F2})")
                        .IsTrue();
                }
            }
        }
    }

    /// <summary>
    /// A split line is not stretched when justification asks for it: distributing space between the gaps of a
    /// line that is really two lines is a separate problem, so the elements keep their natural widths and the
    /// intervals stay left-aligned. Documented limitation, asserted so it cannot change unnoticed.
    /// </summary>
    [TestCase]
    public void JustificationDoesNotStretchASplitLine()
    {
        var layout = Run(Settings(TextAlignment.Justify));
        var line = layout.Lines[0];

        AssertThat(line.Spans.Length).IsEqual(2);

        foreach (var element in line.Elements)
        {
            if (element.Type != DrawElement.ElementType.Text)
                continue;

            // Every character is one 16px CJK glyph: a stretched line would be wider than that.
            AssertThat(Math.Abs(element.Size.X - 16f) < 0.01f).OverrideFailureMessage(
                $"\"{element.Text}\" was stretched to {element.Size.X:F2}px on a split line").IsTrue();
        }

        float firstElement = line.Elements[0].Position.X;
        AssertThat(Math.Abs(firstElement - line.Spans[0].Left) < 0.5f).IsTrue();
    }

    // ── Helpers ──

    private sealed record Layout(List<LayoutLine> Lines);

    private static Layout Run(TypographySettings settings)
    {
        var elements = new[]
        {
            Text("文字应当填满排除带左侧的区间，然后继续排入右侧的区间，而不是在排除带前就结束这一行。"),
            Text("排除带结束之后，后续文字恢复整行宽度继续排布。"),
        };

        using var engine = new TypographyEngine(settings);
        var lines = engine.PrepareAndLayout(elements.AsSpan(), out _);
        return new Layout(lines);
    }

    private static TypographySettings Settings(TextAlignment alignment)
    {
        var settings = new TypographySettings
        {
            MaxWidth = 240f,
            Alignment = alignment,
            LanguageTag = "zh-Hans",
            // The test is about intervals, not about the language's indentation: clreq's two-character first-line
            // indent would move the first interval and change how much fits in it.
            FirstLineIndent = 0,
        };

        settings.WrapRegions.Add(new WrapRegion
        {
            Shape = new RectWrapShape { Width = 72f, Height = 200f },
            Position = new Vector2(96f, 0f),
            WrapMode = WrapFloat.Left,
            Margin = 6f,
        });

        return settings;
    }

    private static DrawElement Text(string text) => new()
    {
        Type = DrawElement.ElementType.Text,
        Text = text,
        Font = ThemeDB.FallbackFont,
        FontSize = 16,
        Color = new Color(0.1f, 0.1f, 0.1f),
    };
}
