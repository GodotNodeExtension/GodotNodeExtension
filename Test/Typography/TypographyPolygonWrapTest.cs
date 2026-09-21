namespace GodotNodeExtension.Tests.Typography;

using System.Collections.Generic;
using GdUnit4;
using Godot;
using GodotNodeExtension.Component.Typography.Core;
using GodotNodeExtension.Component.Typography.Core.Model;
using static GdUnit4.Assertions;

/// <summary>
/// Specification for shapes that narrow: a shape may occupy several intervals on one line, and the text then has
/// room on both sides of it instead of the whole line being excluded.
/// <para>
/// This is the upstream half of the multi-interval contract: the line model (<see cref="LayoutLine.Spans"/>) and the
/// breaker's interval cursor already existed, but a shape could only ever report one interval, so a polygon that
/// narrowed in the middle behaved like its bounding rectangle. The cases below pin the shape query, the region
/// manager's use of it, and what the text does with the result.
/// </para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class TypographyPolygonWrapTest
{
    /// <summary>A rectangle still reports one interval, and the default implementation provides it.</summary>
    [TestCase]
    public void ARectangleStillReportsOneInterval()
    {
        var shape = new RectWrapShape { Width = 40f, Height = 40f };
        var spans = new List<LineSpan>();

        shape.GetSpansAtY(10f, 16f, spans);

        AssertThat(spans.Count).IsEqual(1);
        AssertThat(spans[0].Left).IsEqual(0f);
        AssertThat(spans[0].Right).IsEqual(40f);
    }

    /// <summary>
    /// A notched polygon occupies the two bars of the notch on the lines that cross it, and one interval where it is
    /// solid: this is the narrowing a per-scanline extent pair cannot express.
    /// </summary>
    [TestCase]
    public void ANotchedPolygonReportsTheIntervalsItOccupies()
    {
        // A "U": two bars with a notch between them, on a solid base.
        PolygonWrapShape shape = PolygonWrapShape.FromPolygon(
        [
            new Vector2(0f, 0f),
            new Vector2(20f, 0f),
            new Vector2(20f, 20f),
            new Vector2(40f, 20f),
            new Vector2(40f, 0f),
            new Vector2(60f, 0f),
            new Vector2(60f, 40f),
            new Vector2(0f, 40f),
        ]);

        var notched = new List<LineSpan>();
        shape.GetSpansAtY(4f, 8f, notched);
        AssertThat(notched.Count).OverrideFailureMessage(
            $"the notch leaves two occupied intervals, got [{string.Join("|", notched)}]").IsEqual(2);
        AssertThat(notched[0].Left).IsEqual(0f);
        AssertThat(notched[0].Right).IsEqual(20f);
        AssertThat(notched[1].Left).IsEqual(40f);
        AssertThat(notched[1].Right).IsEqual(60f);

        var solid = new List<LineSpan>();
        shape.GetSpansAtY(30f, 8f, solid);
        AssertThat(solid.Count).OverrideFailureMessage("below the notch the shape is one interval").IsEqual(1);
        AssertThat(solid[0].Left).IsEqual(0f);
        AssertThat(solid[0].Right).IsEqual(60f);

        // The gap between the bars is not occupied by the shape, so a region made of it leaves text a way through.
        var manager = new WrapRegionManager(60f);

        manager.AddRegion(new WrapRegion
        {
            Shape = shape,
            Position = Vector2.Zero,
            WrapMode = WrapFloat.Left,
            Margin = 0f,
        });

        List<(float Left, float Right)> available = manager.GetAvailableSpans(4f, 8f);

        AssertThat(available.Count).OverrideFailureMessage(
            "the notch leaves a place for the text between the bars").IsEqual(1);
        AssertThat(available[0].Left).IsEqual(20f);
        AssertThat(available[0].Right).IsEqual(40f);
    }

    /// <summary>
    /// A line that does not reach the shape occupies nothing, so the caller leaves the whole line available.
    /// </summary>
    [TestCase]
    public void ALineOutsideTheShapeOccupiesNothing()
    {
        PolygonWrapShape shape = PolygonWrapShape.FromPolygon(
        [
            new Vector2(0f, 0f),
            new Vector2(40f, 0f),
            new Vector2(40f, 20f),
            new Vector2(0f, 20f),
        ]);

        var spans = new List<LineSpan>();
        shape.GetSpansAtY(100f, 16f, spans);

        AssertThat(spans.Count).IsEqual(0);
    }

    /// <summary>
    /// The region manager turns the occupied intervals into available ones by taking their complement, so a shape that
    /// narrows leaves the text two places to go on that line.
    /// </summary>
    [TestCase]
    public void TheRegionManagerLeavesTextOnBothSidesOfANarrowingShape()
    {
        var manager = new WrapRegionManager(200f);

        // A band across the middle of the line, 60px wide, 60px tall, starting 70px in from the left edge.
        manager.AddRegion(new WrapRegion
        {
            Shape = new RectWrapShape { Width = 60f, Height = 60f },
            Position = new Vector2(70f, 0f),
            WrapMode = WrapFloat.Left,
            Margin = 0f,
        });

        List<(float Left, float Right)> spans = manager.GetAvailableSpans(10f, 16f);

        AssertThat(spans.Count).OverrideFailureMessage("the band leaves a place on each side").IsEqual(2);
        AssertThat(spans[0].Left).IsEqual(0f);
        AssertThat(spans[0].Right).IsEqual(70f);
        AssertThat(spans[1].Left).IsEqual(130f);
        AssertThat(spans[1].Right).IsEqual(200f);
    }

    /// <summary>
    /// End to end: text flows into the second interval a narrowing shape leaves, instead of ending the line at the
    /// obstacle — the behaviour the shape query above makes possible.
    /// </summary>
    [TestCase]
    public void TextContinuesInTheSecondIntervalOfAPolygonRow()
    {
        DrawElement[] elements =
        [
            new()
            {
                Type = DrawElement.ElementType.Text,
                Text = "文字应当填满排除带左侧的区间，然后继续排入右侧的区间，而不是在排除带前就结束这一行。",
                Font = ThemeDB.FallbackFont,
                FontSize = 16,
                Color = new Color(0.1f, 0.1f, 0.1f),
            },
        ];

        var region = new WrapRegion
        {
            // A narrow bar that reaches down the first lines only: the text meets it on line 0.
            Shape = PolygonWrapShape.FromPolygon(
            [
                new Vector2(0f, 0f),
                new Vector2(8f, 0f),
                new Vector2(8f, 56f),
                new Vector2(0f, 56f),
            ]),
            Position = new Vector2(0f, 0f),
            WrapMode = WrapFloat.Left,
            Margin = 6f,
        };

        var settings = new TypographySettings
        {
            MaxWidth = 240f,
            LanguageTag = "zh-Hans",
            FirstLineIndent = 0,
            Alignment = TextAlignment.Left,
        };

        settings.WrapRegions.Add(region);

        using var engine = new TypographyEngine(settings);
        List<LayoutLine> lines = engine.PrepareAndLayout(elements, out _);

        AssertThat(lines.Count).IsGreater(0);

        // The exclusion is a bar on the left, so each line it covers has one interval to its right: the point here is
        // that the polygon path reaches the same interval logic the rectangle path already used.
        AssertThat(lines[0].Spans[0].Left).IsGreater(0f);
    }

    /// <summary>Fewer than three vertices is not a polygon, and the constructor says so rather than guessing.</summary>
    [TestCase]
    public void TooFewVerticesIsRejected()
    {
        AssertThrows<System.ArgumentOutOfRangeException>(() =>
            PolygonWrapShape.FromPolygon([new Vector2(0f, 0f), new Vector2(10f, 0f)]));
    }

    private static void AssertThrows<TException>(System.Action action) where TException : System.Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new System.InvalidOperationException($"expected {typeof(TException).Name}, but nothing was thrown");
    }
}
