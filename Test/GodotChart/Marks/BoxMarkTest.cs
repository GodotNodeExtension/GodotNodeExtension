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
/// Behaviour specification for <see cref="BoxMark"/>.
/// <para>
/// Covers the box geometry (width ratio, colour) and the configurable statistic fields, the
/// scale contribution over min..max, the guard for reversed quartiles (a row that cannot be
/// drawn is skipped by both Render and HitTest), and the box-slot/whisker hit region. The skip
/// of a non-finite quartile is pinned once for every mark by <see cref="MarkDataSafetyTest"/>.
/// </para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class BoxMarkTest
{
    private static readonly string[] SingleCategory = { "A" };
    private static readonly string[] TwoCategories = { "A", "B" };

    // ── shared helpers ──

    /// <summary>Local floating point comparison: mark geometry is never bit-exact.</summary>
    private static bool Approx(double a, double b, double tol = 0.01) => Math.Abs(a - b) <= tol;

    private static DataRow D(params (string Field, object? Value)[] fields) => TestContexts.Row(fields);

    /// <summary>Two-category box context; each slot is 200px wide over a Y domain of [0, 100].</summary>
    private static (BoxMark Mark, FakeCanvas2D Canvas, MarkContext Ctx) BoxCtx(List<DataRow> rows)
    {
        var canvas = new FakeCanvas2D();
        var ctx = TestContexts.Mark(canvas, rows, TestContexts.XyEncodes("cat", "median"),
            TestContexts.CategoryScales(TwoCategories));
        return (new BoxMark(), canvas, ctx);
    }

    // ── Options ──

    [TestCase]
    public void BoxWidthRatioAndColorReachTheBox()
    {
        var color = new Color(0.1f, 0.4f, 0.2f, 0.5f);
        var (mark, canvas, ctx) = BoxCtx(MarkCases.Boxes());
        mark.BoxWidthRatio = 0.25f;
        mark.BoxColor = color;
        mark.Render(ctx);

        // slot width = 400 / 2 categories = 200.
        AssertThat(canvas.Rects.Count).IsEqual(2);
        AssertThat(Approx(canvas.Rects[0].W, 200 * 0.25f, 0.05)).IsTrue();
        AssertThat(canvas.FillColors[0].ToHtml()).IsEqual(color.ToHtml());
    }

    [TestCase]
    public void BoxUsesTheConfiguredStatisticFields()
    {
        var rows = new List<DataRow>
        {
            D(("cat", "A"), ("lo", 1.0), ("a", 3.0), ("med", 5.0), ("b", 7.0), ("hi", 9.0)),
        };
        var canvas = new FakeCanvas2D();
        var mark = new BoxMark
        {
            MinField = "lo", Q1Field = "a", MedianField = "med", Q3Field = "b", MaxField = "hi",
        };
        var ctx = TestContexts.Mark(canvas, rows, TestContexts.XyEncodes("cat", "med"),
            TestContexts.CategoryScales(SingleCategory));

        mark.Render(ctx);
        AssertThat(canvas.Rects.Count).IsEqual(1);

        var box = canvas.Rects[0];
        var hit = mark.HitTest(ctx, new Vector2(box.X + box.W / 2f, box.Y + box.H / 2f));
        AssertThat(hit is not null).IsTrue();
        AssertThat(hit!.Label).IsEqual("A: Med=5");
    }

    [TestCase]
    public void BoxContributeScalesCoversMinToMax()
    {
        var scales = new ScaleSet();
        new BoxMark().ContributeScales(scales, TestContexts.XyEncodes("cat", "median"), MarkCases.Boxes());

        var y = scales.TryGet(Channel.Y) as LinearScale;
        AssertThat(y is not null).IsTrue();
        // MarkCases.Boxes() spans 1 .. 11 and the contribution pads the domain by 5% of it on both ends.
        AssertThat(Approx(y!.Min, 0.5f)).IsTrue();
        AssertThat(Approx(y.Max, 11.5f)).IsTrue();
    }

    // ── Hit testing ──

    [TestCase]
    public void BoxHitTestIsLimitedToTheBoxSlotAndWhiskerRange()
    {
        var (mark, canvas, ctx) = BoxCtx(MarkCases.Boxes());
        mark.Render(ctx);

        var box = canvas.Rects[0];
        var hit = mark.HitTest(ctx, new Vector2(box.X + box.W / 2f, box.Y + box.H / 2f));
        AssertThat(hit is not null).IsTrue();
        AssertThat(hit!.RowIndex).IsEqual(0);

        AssertThat(mark.HitTest(ctx, new Vector2(200f, 285f)) is null).IsTrue();   // between the slots
        AssertThat(mark.HitTest(ctx, new Vector2(100f, 100f)) is null).IsTrue();   // above the whiskers
    }

    [TestCase]
    public void BoxDoesNotHitTestRowThatIsNeverDrawn()
    {
        var rows = new List<DataRow>
        {
            // Min/Max/Median present but Q1/Q3 missing: Render() skips it, so HitTest must too.
            D(("cat", "A"), ("min", 1.0), ("max", 9.0), ("median", 5.0)),
        };
        var canvas = new FakeCanvas2D();
        var ctx = TestContexts.Mark(canvas, rows, TestContexts.XyEncodes("cat", "median"),
            TestContexts.CategoryScales(SingleCategory));

        var hit = new BoxMark().HitTest(ctx, new Vector2(200f, 150f));

        AssertThat(hit is null).IsTrue();
    }

    // ── Degenerate data ──

    [TestCase]
    public void BoxHandlesReversedQuartilesWithoutNegativeSizes()
    {
        var rows = new List<DataRow>
        {
            D(("cat", "A"), ("min", 1.0), ("q1", 3.0), ("median", 5.0), ("q3", 7.0), ("max", 9.0)),
            D(("cat", "B"), ("min", 2.0), ("q1", 7.0), ("median", 5.0), ("q3", 3.0), ("max", 11.0)),
        };
        var (mark, canvas, ctx) = BoxCtx(rows);
        mark.Render(ctx);

        AssertThat(canvas.NegativeSizeRectCount).IsEqual(0);
        AssertThat(canvas.Rects.Count).IsEqual(2);
        // Swapping Q1/Q3 mirrors the box but keeps its height.
        AssertThat(Approx(canvas.Rects[0].H, canvas.Rects[1].H)).IsTrue();
    }

    [TestCase]
    public void BoxDrawsNothingWithoutData()
    {
        var (mark, canvas, ctx) = BoxCtx([]);
        mark.Render(ctx);

        AssertThat(canvas.DrewAnything).IsFalse();
        AssertThat(mark.HitTest(ctx, new Vector2(100f, 150f)) is null).IsTrue();
    }

    /// <summary>
    /// The two knobs the whisker line is drawn with: <see cref="BoxMark.WhiskerWidth"/> is its stroke width and
    /// <see cref="BoxMark.LineColor"/> its colour (null keeps the theme's).
    /// </summary>
    [TestCase]
    public void BoxWhiskerWidthAndLineColorReachTheLine()
    {
        var (mark, canvas, ctx) = BoxCtx(MarkCases.Boxes());
        mark.WhiskerWidth = 4f;
        mark.LineColor = new Color(0.9f, 0.1f, 0.1f);
        mark.Render(ctx);

        AssertThat(canvas.StrokeWidths).Contains(4f);
        AssertThat(canvas.StrokeColors).Contains(new Color(0.9f, 0.1f, 0.1f));
    }
}
