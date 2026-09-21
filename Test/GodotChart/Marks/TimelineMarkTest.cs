namespace GodotNodeExtension.Tests.GodotChart.Marks;

using System;
using System.Collections.Generic;
using GdUnit4;
using Godot;
using GodotNodeExtension.Component.GodotChart;
using GodotNodeExtension.Component.GodotChart.Marks;
using Support;
using static GdUnit4.Assertions;

/// <summary>
/// Behaviour specification for <see cref="TimelineMark"/>: the horizontal interval bars built from
/// <see cref="TimelineMark.StartField"/> / <see cref="TimelineMark.EndField"/> and
/// <see cref="TimelineMark.BarHeightRatio"/>, their labels, the scales the mark needs, degenerate
/// intervals and hit testing under the entry animation.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class TimelineMarkTest
{
    private static readonly string[] SingleCategory = { "A" };
    private static readonly string[] TwoCategories = { "A", "B" };

    // ── helpers ─────────────────────────────────────────────────────────────

    /// <summary>Local floating point comparison: mark geometry is never bit-exact.</summary>
    private static bool Approx(double a, double b, double tol = 0.01) => Math.Abs(a - b) <= tol;

    private static DataRow D(params (string Field, object? Value)[] fields) => TestContexts.Row(fields);

    private static OrdinalScale Ordinal(IEnumerable<string> keys)
    {
        var scale = new OrdinalScale();
        scale.Fit(keys);
        return scale;
    }

    /// <summary>Category axis on Y (ordinal) plus the interval axis on X (linear).</summary>
    private static (ScaleSet Scales, EncodeSet Encodes) TimelineAxes(IEnumerable<string>? categories = null)
    {
        var scales = new ScaleSet();
        scales.Set(Channel.X, Ordinal(categories ?? TwoCategories));
        scales.Set(Channel.Y, new LinearScale(0, 5));
        return (scales, TestContexts.XyEncodes("cat", "value"));
    }

    private static List<DataRow> Intervals() =>

    [
        D(("cat", "A"), ("start", 0.0), ("end", 3.0)),
        D(("cat", "B"), ("start", 1.0), ("end", 4.0)),
    ];

    // ── Categories the axis does not know ───────────────────────────────────

    /// <summary>
    /// The category is read from the channel that carries the ordinal scale, not from a fixed one. A timeline
    /// is horizontal (the category sits on Y), but a host that hands the mark its own scales may put the
    /// ordinal scale on X - reading a fixed channel then picked up the numeric interval field as a category,
    /// and the ordinal scale reports a category it does not know as 0, i.e. the first row's lane, so every bar
    /// landed on the same one.
    /// </summary>
    [TestCase]
    public void TimelineReadsTheCategoryFromWhicheverChannelIsOrdinal()
    {
        var problems = new List<string>();

        // The chart's own wiring for this kind: the category on Y, the interval on X (Timeline is horizontal).
        Check("category on Y", Channel.Y, problems);
        // ... and a host that hands the mark its own scales the other way round.
        Check("category on X", Channel.X, problems);

        AssertThat(string.Join("; ", problems)).IsEqual("");

        static void Check(string what, Channel ordinalChannel, List<string> problems)
        {
            Channel valueChannel = ordinalChannel == Channel.Y ? Channel.X : Channel.Y;
            var scales = new ScaleSet();
            scales.Set(ordinalChannel, Ordinal(TwoCategories));
            scales.Set(valueChannel, new LinearScale(0, 5));

            var encodes = new EncodeSet();
            encodes.Set(ordinalChannel, new FieldEncode("cat"));
            encodes.Set(valueChannel, new FieldEncode("value"));

            var canvas = new FakeCanvas2D();
            new TimelineMark().Render(TestContexts.Mark(canvas, Intervals(), encodes, scales));

            // Two different categories are two bars in two different rows. Reading the category from a fixed
            // channel put both of them in the first row (the unknown category mapped to 0).
            if (canvas.Rects.Count != 2)
                problems.Add($"{what}: {canvas.Rects.Count} bar(s) instead of 2");
            else if (Approx(canvas.Rects[0].Y, canvas.Rects[1].Y))
                problems.Add($"{what}: both bars landed in the same row");
        }
    }

    /// <summary>
    /// A category the axis does not know is skipped instead of being drawn on the first one, and it does not
    /// take the other rows with it.
    /// </summary>
    [TestCase]
    public void TimelineSkipsRowsWhoseCategoryTheAxisDoesNotKnow()
    {
        var canvas = new FakeCanvas2D();
        var (scales, encodes) = TimelineAxes(["A", "B"]);
        var rows = new List<DataRow>
        {
            D(("cat", "A"), ("start", 0.0), ("end", 3.0)),
            D(("cat", "ghost"), ("start", 1.0), ("end", 4.0)),   // not in the domain
            D(("cat", "B"), ("start", 1.0), ("end", 4.0)),
        };

        new TimelineMark().Render(TestContexts.Mark(canvas, rows, encodes, scales));

        AssertThat(canvas.Rects.Count).IsEqual(2);
        AssertThat(canvas.NonFiniteCoordinateCount).IsEqual(0);
    }

    // ── Default rendering ───────────────────────────────────────────────────

    [TestCase]
    public void TimelineBarHeightRatioControlsTheBarHeight()
    {
        var (scales, encodes) = TimelineAxes();
        var canvas = new FakeCanvas2D();
        new TimelineMark { BarHeightRatio = 0.5f }.Render(
            TestContexts.Mark(canvas, Intervals(), encodes, scales));

        // slot = 300/2 = 150, bar = 150 * 0.5 = 75
        AssertThat(canvas.RoundRects.Count).IsEqual(2);
        AssertThat(Approx(canvas.RoundRects[0].H, 75f)).IsTrue();
    }

    [TestCase]
    public void TimelineStartAndEndFieldsDefineTheBar()
    {
        var data = new List<DataRow>
        {
            D(("cat", "A"), ("s", 0.0), ("e", 3.0)),
            D(("cat", "B"), ("s", 1.0), ("e", 4.0)),
        };
        var (scales, encodes) = TimelineAxes();
        var canvas = new FakeCanvas2D();
        new TimelineMark { StartField = "s", EndField = "e" }.Render(
            TestContexts.Mark(canvas, data, encodes, scales));

        // 0 -> 3 over a [0,5] axis = 3/5 of 400 = 240
        AssertThat(canvas.RoundRects.Count).IsEqual(2);
        AssertThat(Approx(canvas.RoundRects[0].W, 240f)).IsTrue();
    }

    // ── Labels ──────────────────────────────────────────────────────────────

    [TestCase]
    public void TimelineShowLabelTogglesTheBarLabel()
    {
        var (scales, encodes) = TimelineAxes();

        var on = new FakeCanvas2D();
        new TimelineMark { ShowLabel = true }.Render(
            TestContexts.Mark(on, Intervals(), encodes, scales));

        var off = new FakeCanvas2D();
        new TimelineMark { ShowLabel = false }.Render(
            TestContexts.Mark(off, Intervals(), encodes, scales));

        AssertThat(on.Texts.Contains("A")).IsTrue();
        AssertThat(off.Texts.Count).IsEqual(0);
    }

    [TestCase]
    public void TimelineLabelsFollowTheLabelFormat()
    {
        var (scales, encodes) = TimelineAxes();
        var canvas = new FakeCanvas2D();
        new TimelineMark { ShowLabel = true, LabelFormat = "{0} ({1})" }.Render(
            TestContexts.Mark(canvas, Intervals(), encodes, scales));

        // {0} is the bar label (the category here), {1} its start value on the value axis.
        AssertThat(canvas.Texts).Contains("A (0)");
        AssertThat(canvas.Texts).Contains("B (1)");
    }

    // ── Degenerate intervals and scales ─────────────────────────────────────

    [TestCase]
    public void TimelineSwapsAReversedIntervalAndWarnsInsteadOfDroppingIt()
    {
        var data = new List<DataRow>
        {
            D(("cat", "A"), ("start", 3.0), ("end", 1.0)),   // end < start: drawn with the endpoints swapped
            D(("cat", "B"), ("start", 2.0), ("end", 2.0)),   // zero width: still nothing to draw
        };
        var (scales, encodes) = TimelineAxes();
        var canvas = new FakeCanvas2D();
        new TimelineMark().Render(TestContexts.Mark(canvas, data, encodes, scales));

        // A reversed interval used to be dropped silently. It is now drawn left to right, so its left
        // edge is min(start, end) and its width spans the distance between the two endpoints.
        AssertThat(canvas.RoundRects.Count).IsEqual(1);
        var bar = canvas.RoundRects[0];
        AssertThat(Approx(bar.X, 400f * 1f / 5f)).IsTrue();   // min(3, 1) = 1 over the [0,5] interval axis
        AssertThat(Approx(bar.W, 400f * 2f / 5f)).IsTrue();    // 3 - 1 = 2 over the same axis
    }

    [TestCase]
    public void TimelineWithoutALinearScaleDrawsNothing()
    {
        var scales = new ScaleSet();
        scales.Set(Channel.X, Ordinal(TwoCategories));   // no LinearScale anywhere

        var canvas = new FakeCanvas2D();
        new TimelineMark().Render(
            TestContexts.Mark(canvas, Intervals(), TestContexts.XyEncodes("cat", "value"), scales));

        AssertThat(canvas.FillCount).IsEqual(0);
        AssertThat(canvas.RoundRects.Count).IsEqual(0);
    }

    // ── Hit testing ─────────────────────────────────────────────────────────

    [TestCase]
    public void TimelineHitTestShrinksWithTheAnimation()
    {
        var data = new List<DataRow> { D(("cat", "A"), ("start", 0.0), ("end", 3.0)) };
        var (scales, encodes) = TimelineAxes(SingleCategory);

        var mark = new TimelineMark();
        var ctx = TestContexts.Mark(
            new FakeCanvas2D(), data, encodes, scales,
            animation: new AnimationContext { EntryProgress = 0.5f });
        mark.Render(ctx);

        // Bar only reaches x = 120 at 50% progress: a point beyond it must not be hit.
        AssertThat(mark.HitTest(ctx, new Vector2(200f, 225f)) is null).IsTrue();
        AssertThat(mark.HitTest(ctx, new Vector2(60f, 225f)) is not null).IsTrue();
    }

    /// <summary>
    /// Hit testing walks the same geometry as the data layer, so a row the layer skips is not hittable
    /// either. The lane scale knows one category and the table carries a second one, and an unknown category
    /// maps to 0 - the first lane - so that row used to answer for a rectangle nobody drew (and, because hit
    /// testing takes the first match, it shadowed the row that really sits in that lane).
    /// </summary>
    [TestCase]
    public void TimelineHitTestSkipsRowsOnCategoriesTheLaneScaleDoesNotKnow()
    {
        var data = new List<DataRow>
        {
            D(("cat", "A"), ("start", 0.0), ("end", 1.0)),
            D(("cat", "Z"), ("start", 4.0), ("end", 5.0)),
        };
        var (scales, encodes) = TimelineAxes(SingleCategory);

        var mark = new TimelineMark();
        // Plot (0, 0, 400, 300) with the Y axis flipped: A's bar (lane 0.5) spans x 0..80 / y 60..240, and
        // the rectangle the unknown row would occupy (an unknown category maps to 0, so its lane sits at the
        // bottom) is x 320..400 / y 210..390.
        var ctx = TestContexts.Mark(new FakeCanvas2D(), data, encodes, scales);

        AssertThat(mark.HitTest(ctx, new Vector2(360f, 300f)) is null).IsTrue();

        // ... while the row that is drawn stays hittable.
        var hit = mark.HitTest(ctx, new Vector2(40f, 150f));
        AssertThat(hit is not null).IsTrue();
        AssertThat(hit!.RowIndex).IsEqual(0);
    }
}
