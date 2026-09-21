namespace GodotNodeExtension.Tests.GodotChart.Marks;

using System;
using System.Collections.Generic;
using System.Linq;
using GdUnit4;
using Godot;
using GodotNodeExtension.Component.GodotChart;
using GodotNodeExtension.Component.GodotChart.Marks;
using Support;
using static GdUnit4.Assertions;
using static GodotNodeExtension.Tests.GodotChart.Support.Asserts;

/// <summary>
/// Behaviour specification for <see cref="IntervalMark"/>: bar width and padding, the horizontal
/// orientation, the normalize/stack modes, hover highlighting and value labels, hit testing, how
/// degenerate data (negative values) is handled, and hot-path allocation. The skip of a
/// non-finite value is pinned once for every mark by <see cref="MarkDataSafetyTest"/>.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class IntervalMarkTest
{
    private static readonly string[] SingleCategory = { "A" };
    private static readonly string[] TwoCategories = { "A", "B" };
    private static readonly string[] ThreeCategories = { "A", "B", "C" };


    private static DataRow D(params (string Field, object? Value)[] fields) => TestContexts.Row(fields);

    private static List<DataRow> Simple() =>
    [
        D(("cat", "A"), ("value", 10.0)),
        D(("cat", "B"), ("value", 20.0)),
        D(("cat", "C"), ("value", 15.0)),
    ];

    private static List<DataRow> MixedSigns() =>
    [
        D(("cat", "profit"), ("value", 25.0)),
        D(("cat", "loss"), ("value", -18.0)),
        D(("cat", "flat"), ("value", 0.0)),
    ];

    private static List<DataRow> TwoSeries() =>
    [
        D(("cat", "A"), ("value", 10.0), ("series", "S1")),
        D(("cat", "B"), ("value", 20.0), ("series", "S1")),
        D(("cat", "A"), ("value", 5.0), ("series", "S2")),
        D(("cat", "B"), ("value", 7.0), ("series", "S2")),
    ];

    private static List<DataRow> TwoSeriesTwoCategories() =>
    [
        D(("cat", "A"), ("value", 10.0), ("series", "S1")),
        D(("cat", "B"), ("value", 20.0), ("series", "S1")),
        D(("cat", "A"), ("value", 5.0), ("series", "S2")),
        D(("cat", "B"), ("value", 25.0), ("series", "S2")),
    ];

    private static OrdinalScale Ordinal(IEnumerable<string> keys)
    {
        var scale = new OrdinalScale();
        scale.Fit(keys);
        return scale;
    }

    private static Chart Chart2D(FakeCanvas2D canvas, List<DataRow> data, Mark mark, string x, string y,
                                 float width = 400f, float height = 300f)
    {
        var chart = new Chart(canvas) { Width = width, Height = height };
        chart.Data(data);
        chart.Mark(mark);
        chart.Encode(Channel.X, x);
        chart.Encode(Channel.Y, y);
        return chart;
    }

    private static Chart BarChart(FakeCanvas2D canvas, List<DataRow> data)
        => Chart2D(canvas, data, new IntervalMark(), "cat", "value");

    private static Chart StackedChart(FakeCanvas2D canvas, BarOrientation orientation)
    {
        var chart = new Chart(canvas) { Width = 400f, Height = 300f };
        chart.Data(TwoSeriesTwoCategories());
        chart.Mark(new IntervalMark { Stack = StackMode.Stack, Orientation = orientation });
        chart.Encode(Channel.Color, "series");
        if (orientation == BarOrientation.Horizontal)
        {
            chart.Encode(Channel.X, "value");
            chart.Encode(Channel.Y, "cat");
        }
        else
        {
            chart.Encode(Channel.X, "cat");
            chart.Encode(Channel.Y, "value");
        }
        return chart;
    }

    /// <summary>Data labels use a "v" prefix so axis labels cannot be counted by mistake.</summary>
    private static int DataLabelCount(FakeCanvas2D canvas)
        => canvas.Texts.Count(t => t.StartsWith('v') && t.Length > 1);

    // ── Bar geometry & padding ───────────────────────────────────────────────

    [TestCase]
    public void IntervalBarPaddingControlsTheBarWidth()
    {
        var scales = TestContexts.CategoryScales(ThreeCategories, 0, 25);

        var noPadding = new FakeCanvas2D();
        new IntervalMark { BarPadding = 0f }.Render(
            TestContexts.Mark(noPadding, Simple(), TestContexts.XyEncodes("cat", "value"), scales));

        var halfPadding = new FakeCanvas2D();
        new IntervalMark { BarPadding = 0.5f }.Render(
            TestContexts.Mark(halfPadding, Simple(), TestContexts.XyEncodes("cat", "value"), scales));

        float slot = 400f / 3f;
        AssertThat(noPadding.Rects.Count).IsEqual(3);
        AssertThat(Approx(noPadding.Rects[0].W, slot)).IsTrue();
        AssertThat(Approx(halfPadding.Rects[0].W, slot * 0.5f)).IsTrue();
    }

    // ── Orientation ──────────────────────────────────────────────────────────

    [TestCase]
    public void IntervalHorizontalNonStackedBarsUseTheValueForWidthAndShareTheBaselineX()
    {
        var data = new List<DataRow>
        {
            D(("cat", "A"), ("value", 10.0)),
            D(("cat", "B"), ("value", 20.0)),
        };
        var encodes = TestContexts.XyEncodes("value", "cat");

        var scales = new ScaleSet();
        scales.Set(Channel.Y, Ordinal(TwoCategories));
        scales.Set(Channel.X, new LinearScale(0, 25));

        var canvas = new FakeCanvas2D();
        new IntervalMark { Orientation = BarOrientation.Horizontal }.Render(
            TestContexts.Mark(canvas, data, encodes, scales));

        // Both bars start on the data zero line (same X) and grow with the value.
        AssertThat(canvas.Rects.Count).IsEqual(2);
        AssertThat(Approx(canvas.Rects[0].X, canvas.Rects[1].X)).IsTrue();
        AssertThat(Approx(canvas.Rects[0].W, 160f)).IsTrue();
        AssertThat(Approx(canvas.Rects[1].W, 320f)).IsTrue();
    }

    [TestCase]
    public void HorizontalBarsDrawLabels()
    {
        var canvas = new FakeCanvas2D();
        using var chart = Chart2D(canvas,
        [
            D(("cat", "A"), ("value", 10.0)),
            D(("cat", "B"), ("value", 20.0)),
        ], new IntervalMark { Orientation = BarOrientation.Horizontal, ShowLabel = true, LabelFormat = "v{0}" }, "value", "cat");

        chart.Render();

        AssertThat(DataLabelCount(canvas)).IsEqual(2);
    }

    [TestCase]
    public void HorizontalStackedBarsGrowAlongX()
    {
        var canvas = new FakeCanvas2D();
        using var chart = StackedChart(canvas, BarOrientation.Horizontal);

        chart.Render();

        var bars = canvas.Rects.Where(r => r is { W: > 0.5f, H: > 0.5f, W: < 380f, H: < 280f }).ToList();
        AssertThat(bars.Count).IsEqual(4);   // 2 categories x 2 series

        // The two segments of one category start at different x (the second follows the first) but
        // share the same y band.
        var firstBand = bars.Where(b => MathF.Abs(b.Y - bars[0].Y) < 1f).ToList();
        AssertThat(firstBand.Count).IsEqual(2);
        AssertThat(MathF.Abs(firstBand[0].X - firstBand[1].X) > 1f).IsTrue();
    }

    [TestCase]
    public void HorizontalStackedBarsAreHitTestable()
    {
        var canvas = new FakeCanvas2D();
        using var chart = StackedChart(canvas, BarOrientation.Horizontal);
        chart.Render();

        var bars = canvas.Rects.Where(r => r is { W: > 0.5f, H: > 0.5f, W: < 380f, H: < 280f }).ToList();
        var target = bars[0];
        var hit = chart.HitTest(new Vector2(target.X + target.W * 0.5f, target.Y + target.H * 0.5f));

        AssertThat(hit is not null).IsTrue();
        AssertThat(hit!.MarkType).IsEqual(nameof(IntervalMark));
    }

    [TestCase]
    public void VerticalStackedBarsStillGrowAlongY()
    {
        var canvas = new FakeCanvas2D();
        using var chart = StackedChart(canvas, BarOrientation.Vertical);
        chart.Render();

        var bars = canvas.Rects.Where(r => r is { W: > 0.5f, H: > 0.5f, W: < 380f, H: < 280f }).ToList();
        AssertThat(bars.Count).IsEqual(4);

        // Segments of one category share the same x band and differ in y.
        var firstBand = bars.Where(b => MathF.Abs(b.X - bars[0].X) < 1f).ToList();
        AssertThat(firstBand.Count).IsEqual(2);
        AssertThat(MathF.Abs(firstBand[0].Y - firstBand[1].Y) > 1f).IsTrue();
    }

    // ── Stacking ─────────────────────────────────────────────────────────────

    [TestCase]
    public void IntervalNormalizeSplitsEachCategoryIntoTwoHalves()
    {
        var data = new List<DataRow>
        {
            D(("cat", "A"), ("value", 50.0), ("series", "S1")),
            D(("cat", "A"), ("value", 50.0), ("series", "S2")),
        };
        var encodes = TestContexts.XyEncodes("cat", "value");
        encodes.Set(Channel.Color, new FieldEncode("series"));

        var scales = new ScaleSet();
        scales.Set(Channel.X, Ordinal(SingleCategory));
        scales.Set(Channel.Y, new LinearScale(0, 1));

        var canvas = new FakeCanvas2D();
        new IntervalMark { Stack = StackMode.Normalize }.Render(
            TestContexts.Mark(canvas, data, encodes, scales));

        // Two 50% segments, each half of the plot height...
        AssertThat(canvas.Rects.Count).IsEqual(2);
        AssertThat(Approx(canvas.Rects[0].H, 150f)).IsTrue();
        AssertThat(Approx(canvas.Rects[1].H, 150f)).IsTrue();
        // ...stacked on the same band, the first starting where the second ends.
        AssertThat(Approx(canvas.Rects[0].X, canvas.Rects[1].X)).IsTrue();
        AssertThat(Approx(canvas.Rects[0].Y, canvas.Rects[1].Y + canvas.Rects[1].H)).IsTrue();
    }

    [TestCase]
    public void IntervalStackedNegativeValuesProduceNoNegativeSizeRects()
    {
        var data = new List<DataRow>
        {
            D(("cat", "A"), ("value", 30.0), ("series", "S1")),
            D(("cat", "A"), ("value", -20.0), ("series", "S2")),
        };
        var encodes = TestContexts.XyEncodes("cat", "value");
        encodes.Set(Channel.Color, new FieldEncode("series"));

        var scales = new ScaleSet();
        scales.Set(Channel.X, Ordinal(SingleCategory));
        scales.Set(Channel.Y, new LinearScale(-25, 35));

        var canvas = new FakeCanvas2D();
        new IntervalMark { Stack = StackMode.Stack }.Render(
            TestContexts.Mark(canvas, data, encodes, scales));

        AssertThat(canvas.Rects.Count).IsEqual(2);
        AssertThat(canvas.NegativeSizeRectCount).IsEqual(0);
        AssertThat(canvas.NonFiniteCoordinateCount).IsEqual(0);
    }

    [TestCase]
    public void StackedBarsDrawLabelsWhenShowValueIsSet()
    {
        var canvas = new FakeCanvas2D();
        using var chart = Chart2D(canvas, TwoSeries(),
            new IntervalMark { Stack = StackMode.Stack, ShowValue = true, LabelFormat = "v{0}" }, "cat", "value");
        chart.Encode(Channel.Color, "series");

        chart.Render();

        AssertThat(DataLabelCount(canvas)).IsEqual(4);
    }

    // ── Grouped bars ─────────────────────────────────────────────────────────

    /// <summary>Three series sharing one category, the shape a grouped bar chart needs.</summary>
    private static List<DataRow> ThreeSeriesOneCategory() =>
    [
        D(("cat", "A"), ("value", 10.0), ("series", "S1")),
        D(("cat", "A"), ("value", 20.0), ("series", "S2")),
        D(("cat", "A"), ("value", 30.0), ("series", "S3")),
    ];

    /// <summary>
    /// <see cref="IntervalMark.GroupedBars"/> gives every series a sub-band of the category slot, in both
    /// the render and the hit-test path (they share <c>BarBand</c>). Without it the series keep the whole
    /// band and the bars overlap - the classic "three bars stacked on one x" bug, kept here as the
    /// regression guard for the non-grouped default.
    /// </summary>
    [TestCase]
    public void GroupedBarsSplitTheCategoryBandAndStayHitTestable()
    {
        var data = ThreeSeriesOneCategory();
        var encodes = TestContexts.XyEncodes("cat", "value");
        encodes.Set(Channel.Color, new FieldEncode("series"));
        var scales = TestContexts.CategoryScales(SingleCategory, 0, 30);

        var canvas = new FakeCanvas2D();
        var mark = new IntervalMark { GroupedBars = true };
        var ctx = TestContexts.Mark(canvas, data, encodes, scales);
        mark.Render(ctx);

        AssertThat(canvas.Rects.Count).IsEqual(3);
        float expectedW = 400f * (1f - mark.BarPadding) / 3f;
        AssertThat(canvas.Rects.Select(r => r.X).Distinct().Count()).IsEqual(3);
        AssertThat(canvas.Rects.All(r => Approx(r.W, expectedW))).IsTrue();

        var sorted = canvas.Rects.OrderBy(r => r.X).ToList();
        AssertThat(Approx(sorted[1].X, sorted[0].X + expectedW)).IsTrue();
        AssertThat(Approx(sorted[2].X, sorted[1].X + expectedW)).IsTrue();

        // Hit testing goes through the same band geometry: each sub-band reports its own row.
        var hitRows = new List<int>();
        foreach (var r in sorted)
        {
            var hit = mark.HitTest(ctx, new Vector2(r.X + r.W / 2f, r.Y + r.H / 2f));
            AssertThat(hit is not null).IsTrue();
            hitRows.Add(hit!.RowIndex);
        }
        AssertThat(hitRows.Distinct().Count()).IsEqual(3);

        // Regression guard: the default (GroupedBars = false) still overlaps the series.
        var overlapping = new FakeCanvas2D();
        var plain = new IntervalMark();
        var plainCtx = TestContexts.Mark(overlapping, data, encodes, scales);
        plain.Render(plainCtx);

        AssertThat(overlapping.Rects.Count).IsEqual(3);
        AssertThat(overlapping.Rects.Select(r => r.X).Distinct().Count()).IsEqual(1);
        AssertThat(Approx(overlapping.Rects[0].W, 400f * (1f - plain.BarPadding))).IsTrue();

        var top = overlapping.Rects[0];
        var overlapHit = plain.HitTest(plainCtx, new Vector2(top.X + top.W / 2f, top.Y + top.H / 2f));
        AssertThat(overlapHit is not null).IsTrue();
        AssertThat(overlapHit!.RowIndex).IsEqual(2);   // the last drawn bar covers the others
    }

    /// <summary>
    /// A hidden series gives up its sub-band instead of keeping an empty stripe: the band index is built from
    /// the series that are actually drawn, so hiding one of three leaves the other two at the slot's halves -
    /// and the hit test moves with them, because both paths share <c>GetSeriesBands</c>.
    /// </summary>
    [TestCase]
    public void AHiddenSeriesDoesNotShiftTheGroupedBands()
    {
        var data = ThreeSeriesOneCategory();
        var encodes = TestContexts.XyEncodes("cat", "value");
        encodes.Set(Channel.Color, new FieldEncode("series"));
        var scales = TestContexts.CategoryScales(SingleCategory, 0, 30);
        var mark = new IntervalMark { GroupedBars = true };

        var canvas = new FakeCanvas2D();
        var ctx = TestContexts.Mark(canvas, data, encodes, scales, hiddenSeries: new HashSet<string> { "S2" });
        mark.Render(ctx);

        AssertThat(canvas.Rects.Count).IsEqual(2);
        float expectedW = 400f * (1f - mark.BarPadding) / 2f;
        var sorted = canvas.Rects.OrderBy(r => r.X).ToList();
        AssertThat(sorted.All(r => Approx(r.W, expectedW))).IsTrue();
        AssertThat(Approx(sorted[1].X, sorted[0].X + expectedW)).IsTrue();

        // The right sub-band belongs to the third series, which is now the second visible one.
        var right = sorted[^1];
        var hit = mark.HitTest(ctx, new Vector2(right.X + right.W / 2f, right.Y + right.H / 2f));
        AssertThat(hit).IsNotNull();
        AssertThat(hit!.RowIndex).IsEqual(2);
    }

    /// <summary>
    /// A pinned domain survives the stacked contribution. The stack helper installs its own
    /// <see cref="LinearScale"/> (0..largest stack), which used to drop a <c>ScaleDomain</c> lock; the
    /// lock is re-applied afterwards, so the segments stay sized against the pinned 0..100 axis.
    /// </summary>
    [TestCase]
    public void ScaleDomainSurvivesAStackedContribution()
    {
        var canvas = new FakeCanvas2D();
        using var chart = new Chart(canvas) { Width = 400f, Height = 300f };
        chart.Data(new List<DataRow>
        {
            D(("cat", "A"), ("value", 30.0), ("series", "S1")),
            D(("cat", "A"), ("value", 20.0), ("series", "S2")),
        });
        chart.Mark(new IntervalMark { Stack = StackMode.Stack });
        chart.Encode(Channel.X, "cat");
        chart.Encode(Channel.Y, "value");
        chart.Encode(Channel.Color, "series");
        chart.ScaleDomain(Channel.Y, 0, 100);
        chart.Render();

        var plot = chart.CurrentPlotArea!.Value;
        var bars = canvas.Rects.Where(r => r.W is > 0.5f and < 380f && r.H is > 0.5f and < 280f)
                               .OrderByDescending(r => r.H).ToList();
        AssertThat(bars.Count).IsEqual(2);
        AssertThat(Approx(bars[0].H, plot.Height * 0.30f, 0.5f)).IsTrue();
        AssertThat(Approx(bars[1].H, plot.Height * 0.20f, 0.5f)).IsTrue();
        // Not the stack helper's own 0..(50 * 1.05) axis, which would make the tallest bar ~171 px.
        AssertThat(bars[0].H < plot.Height * 0.4f).IsTrue();
    }

    // ── Hover & labels ───────────────────────────────────────────────────────

    [TestCase]
    public void IntervalHoverWidensAndBrightensTheBar()
    {
        var data = new List<DataRow> { D(("cat", "A"), ("value", 10.0)) };
        var scales = TestContexts.CategoryScales(SingleCategory, 0, 20);

        var plain = new FakeCanvas2D();
        new IntervalMark().Render(
            TestContexts.Mark(plain, data, TestContexts.XyEncodes("cat", "value"), scales));

        var theme = ChartTheme.Dark();
        theme.HoverScale = 1.5f;
        theme.HoverBrighten = 1.5f;

        var hovered = new FakeCanvas2D();
        new IntervalMark().Render(TestContexts.Mark(
            hovered, data, TestContexts.XyEncodes("cat", "value"), scales,
            theme: theme, hoveredRowIndex: 0));

        AssertThat(Approx(hovered.Rects[0].W, plain.Rects[0].W * 1.5f)).IsTrue();
        AssertThat(hovered.FillColors[0].R > plain.FillColors[0].R).IsTrue();
    }

    [TestCase]
    public void IntervalLabelFormatReachesTheBarLabels()
    {
        var canvas = new FakeCanvas2D();
        var scales = TestContexts.CategoryScales(ThreeCategories, 0, 25);

        new IntervalMark { ShowLabel = true, LabelFormat = "v{0}" }.Render(
            TestContexts.Mark(canvas, Simple(), TestContexts.XyEncodes("cat", "value"), scales));

        AssertThat(canvas.Texts.Count).IsEqual(3);
        AssertThat(canvas.Texts.Contains("v10")).IsTrue();
        AssertThat(canvas.Texts.Contains("v20")).IsTrue();
        AssertThat(canvas.Texts.Contains("v15")).IsTrue();
    }

    // ── Degenerate data ──────────────────────────────────────────────────────

    [TestCase]
    public void NegativeValuesDoNotProduceNegativeSizeBars()
    {
        var canvas = new FakeCanvas2D();
        BarChart(canvas, MixedSigns()).Render();

        AssertThat(canvas.NegativeSizeRectCount).IsEqual(0);
        // Three bar rects (the chart background spans the full width, so filter it out).
        AssertThat(canvas.Rects.Count(r => r.W is > 0f and < 200f)).IsEqual(3);
    }

    [TestCase]
    public void NegativeAndPositiveBarsGrowInOppositeDirections()
    {
        var canvas = new FakeCanvas2D();
        BarChart(canvas, MixedSigns()).Render();

        var bars = canvas.Rects.Where(r => r.W is > 0f and < 200f).ToList();
        AssertThat(bars.Count).IsEqual(3);

        // The chart is 400x300 with the plot inside; a negative bar must start lower (larger Y)
        // than a positive one because it grows downwards from the zero line.
        var positive = bars[0];
        var negative = bars[1];
        AssertThat(negative.Y > positive.Y).IsTrue();
    }

    // ── Allocation ───────────────────────────────────────────────────────────

    /// <summary>Objects a whole frame may create regardless of the element count (axes, grid, background).</summary>
    private const int FrameOverheadBudget = 12;

    private static (int Paths, int Paints) RenderOnce(Mark mark, List<DataRow> data,
        string x, string y, string? color = null, int frames = 1)
    {
        var canvas = new FakeCanvas2D();
        using var chart = new Chart(canvas) { Width = 400f, Height = 300f };
        chart.Data(data);
        chart.Mark(mark);
        chart.Encode(Channel.X, x);
        chart.Encode(Channel.Y, y);
        if (color != null) chart.Encode(Channel.Color, color);

        canvas.PathCreateCount = 0;
        canvas.PaintCreateCount = 0;
        for (int i = 0; i < frames; i++)
            chart.Render();
        return (canvas.PathCreateCount, canvas.PaintCreateCount);
    }

    [TestCase]
    public void IntervalMarkDoesNotAllocatePerBar()
    {
        var small = RenderOnce(new IntervalMark(), MarkCases.ManyBars(8), "cat", "value");
        var large = RenderOnce(new IntervalMark(), MarkCases.ManyBars(200), "cat", "value");

        int delta = (large.Paths + large.Paints) - (small.Paths + small.Paints);
        AssertThat(delta <= FrameOverheadBudget).IsTrue();
    }

    // RepeatedFramesReuseTheDrawingObjects lived here too; it is identical to the case of the same name
    // in MarkAllocationTest (the dedicated allocation suite), so it was kept in one place only.
}
