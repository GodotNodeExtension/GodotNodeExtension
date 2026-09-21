namespace GodotNodeExtension.Tests.GodotChart.Marks;

using System.Collections.Generic;
using GdUnit4;
using Godot;
using GodotNodeExtension.Component.GodotChart;
using GodotNodeExtension.Component.GodotChart.Marks;
using Support;
using static GdUnit4.Assertions;
using static GodotNodeExtension.Tests.GodotChart.Support.Asserts;

/// <summary>
/// Behaviour specification for <see cref="RangeAreaMark"/>: the optional edge strokes, the lower
/// bound field, the hover boundary dots, and the degenerate cases (inverted bounds, a single row).
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class RangeAreaMarkTest
{
    private static readonly string[] TwoCategories = { "A", "B" };


    private static DataRow D(params (string Field, object? Value)[] fields) => TestContexts.Row(fields);

    private static List<DataRow> Bands() =>

    [
        D(("cat", "A"), ("value", 12.0), ("lower", 8.0)),
        D(("cat", "B"), ("value", 22.0), ("lower", 16.0)),
    ];

    private static FakeCanvas2D RenderBands(RangeAreaMark mark, List<DataRow> data)
    {
        var canvas = new FakeCanvas2D();
        var scales = TestContexts.CategoryScales(TwoCategories, 0, 30);
        mark.Render(TestContexts.Mark(canvas, data, TestContexts.XyEncodes("cat", "value"), scales));
        return canvas;
    }

    // ── Borders & bounds ─────────────────────────────────────────────────────

    // ── Hit testing ──────────────────────────────────────────────────────────

    /// <summary>
    /// The band is the hit area: a point inside a column's upper/lower bounds hits that row, above the upper
    /// bound or below the lower one misses, a point too far to the side is beyond the snap distance, and a
    /// chart with a single row has no band to hit.
    /// </summary>
    [TestCase]
    public void RangeAreaHitTestFindsTheBandOfAColumn()
    {
        var mark = new RangeAreaMark();
        var scales = TestContexts.CategoryScales(TwoCategories, 0, 30);
        var ctx = TestContexts.Mark(new FakeCanvas2D(), Bands(), TestContexts.XyEncodes("cat", "value"), scales);

        var xScale = (OrdinalScale)scales.TryGet(Channel.X)!;
        var yScale = (LinearScale)scales.TryGet(Channel.Y)!;
        float px = TestContexts.DefaultPlot.MapX((float)xScale.Map("A"));
        float upper = TestContexts.DefaultPlot.MapY((float)yScale.Map(12.0));
        float lower = TestContexts.DefaultPlot.MapY((float)yScale.Map(8.0));
        var inside = new Vector2(px, (upper + lower) / 2f);

        var hit = mark.HitTest(ctx, inside);
        AssertThat(hit).IsNotNull();
        AssertThat(hit!.RowIndex).IsEqual(0);

        // Above the upper bound, below the lower one, and far enough to the side that no column is in reach.
        AssertThat(mark.HitTest(ctx, new Vector2(px, upper - 30f))).IsNull();
        AssertThat(mark.HitTest(ctx, new Vector2(px, lower + 30f))).IsNull();
        AssertThat(mark.HitTest(ctx, new Vector2(px + 200f, inside.Y))).IsNull();

        // A single row is not a band.
        var single = TestContexts.Mark(new FakeCanvas2D(),
            [D(("cat", "A"), ("value", 12.0), ("lower", 8.0))],
            TestContexts.XyEncodes("cat", "value"), scales);
        AssertThat(mark.HitTest(single, inside)).IsNull();
    }

    /// <summary>
    /// A lower bound that is present but null means "no value" here, exactly as it does in the render pass. The
    /// hit test mapped it anyway - <c>Convert.ToDouble(null)</c> is 0 - so a row the renderer skipped answered
    /// the pointer with a band from its value down to the bottom of the axis. The two passes share one guard now.
    /// </summary>
    [TestCase]
    public void RangeAreaHitTestSkipsANamedNullLowerBound()
    {
        var data = new List<DataRow>
        {
            D(("cat", "A"), ("value", 12.0), ("lower", null)),
            D(("cat", "B"), ("value", 26.0), ("lower", 18.0)),
        };
        var scales = TestContexts.CategoryScales(TwoCategories, 0, 30);
        var mark = new RangeAreaMark();
        var ctx = TestContexts.Mark(new FakeCanvas2D(), data, TestContexts.XyEncodes("cat", "value"), scales);

        var xScale = (OrdinalScale)scales.TryGet(Channel.X)!;
        var yScale = (LinearScale)scales.TryGet(Channel.Y)!;
        float pxA = TestContexts.DefaultPlot.MapX((float)xScale.Map("A"));

        // Somewhere inside the band row A would have drawn (12 down to 0): the row has no lower bound, so no
        // band was painted there and nothing may be hit.
        float phantomTop = TestContexts.DefaultPlot.MapY((float)yScale.Map(12.0));
        float phantomBottom = TestContexts.DefaultPlot.MapY((float)yScale.Map(0.0));
        AssertThat(mark.HitTest(ctx, new Vector2(pxA, (phantomTop + phantomBottom) / 2f))).IsNull();

        // The row that does have both bounds is still hit, so the guard did not just turn the band off.
        float pxB = TestContexts.DefaultPlot.MapX((float)xScale.Map("B"));
        float upper = TestContexts.DefaultPlot.MapY((float)yScale.Map(26.0));
        float lower = TestContexts.DefaultPlot.MapY((float)yScale.Map(18.0));
        var hit = mark.HitTest(ctx, new Vector2(pxB, (upper + lower) / 2f));
        AssertThat(hit).IsNotNull();
        AssertThat(hit!.RowIndex).IsEqual(1);
    }

    [TestCase]
    public void RangeAreaBorderLinesToggleTheEdgeStrokes()
    {
        var withBorders = RenderBands(new RangeAreaMark { ShowBorderLines = true }, Bands());
        var withoutBorders = RenderBands(new RangeAreaMark { ShowBorderLines = false }, Bands());

        AssertThat(withBorders.FillCount).IsEqual(1);
        AssertThat(withBorders.StrokeCount).IsEqual(2);      // one stroke per edge
        AssertThat(withoutBorders.FillCount).IsEqual(1);
        AssertThat(withoutBorders.StrokeCount).IsEqual(0);
    }

    [TestCase]
    public void RangeAreaLowerFieldSelectsTheLowerBound()
    {
        var data = new List<DataRow>
        {
            D(("cat", "A"), ("value", 12.0), ("lo", 8.0)),
            D(("cat", "B"), ("value", 22.0), ("lo", 16.0)),
        };

        var custom = RenderBands(new RangeAreaMark { LowerField = "lo" }, data);
        var missing = RenderBands(new RangeAreaMark(), data);   // default "lower" is absent

        AssertThat(custom.FillCount).IsEqual(1);
        AssertThat(missing.FillCount).IsEqual(0);
    }

    /// <summary>
    /// A bound that is not a number is skipped like a missing one, and it does not take the band with it.
    /// The render path used to convert both bounds with the throwing <c>ScaleConvert.ToDouble</c>, so one
    /// colour string in a numeric column raised <c>InvalidCastException</c> - caught by the chart's render
    /// stage and logged as a single error, with the whole band gone from the frame.
    /// </summary>
    [TestCase]
    public void RangeAreaSkipsANonNumericBoundInsteadOfThrowing()
    {
        var data = new List<DataRow>
        {
            D(("cat", "A"), ("value", 12.0), ("lower", 8.0)),
            D(("cat", "B"), ("value", "#ff0000"), ("lower", 16.0)),   // a colour string in the value column
            D(("cat", "A"), ("value", 22.0), ("lower", "n/a")),        // ...and one in the lower column
            D(("cat", "B"), ("value", 26.0), ("lower", 18.0)),
        };

        var canvas = RenderBands(new RangeAreaMark(), data);

        // The two usable rows still draw one band (one fill, no strokes by default); the dirty ones are
        // skipped instead of taking the band down.
        AssertThat(canvas.FillCount).IsEqual(1);
        AssertThat(canvas.NonFiniteCoordinateCount).IsEqual(0);
    }

    // ── Hover ────────────────────────────────────────────────────────────────

    [TestCase]
    public void RangeAreaHoverDrawsTwoBoundaryDots()
    {
        var theme = ChartTheme.Dark();
        theme.RangeAreaPointRadius = 6f;

        var canvas = new FakeCanvas2D();
        var scales = TestContexts.CategoryScales(TwoCategories, 0, 30);
        new RangeAreaMark().Render(TestContexts.Mark(
            canvas, Bands(), TestContexts.XyEncodes("cat", "value"), scales,
            theme: theme, hoveredRowIndex: 0));

        AssertThat(canvas.Circles.Count).IsEqual(2);   // upper + lower bound
        AssertThat(Approx(canvas.Circles[0].Radius, 6f)).IsTrue();
    }

    [TestCase]
    public void RangeAreaSelectionRingsBothBounds()
    {
        var theme = ChartTheme.Dark();
        theme.RangeAreaPointRadius = 6f;

        var canvas = new FakeCanvas2D();
        var scales = TestContexts.CategoryScales(TwoCategories, 0, 30);
        var mark = new RangeAreaMark { ShowBorderLines = false };
        mark.States.SelectedStroke = new Color(1f, 0f, 1f);
        mark.States.SelectedStrokeWidth = 4f;
        mark.Render(TestContexts.Mark(canvas, Bands(), TestContexts.XyEncodes("cat", "value"), scales,
            theme: theme, selectedRowIndex: 1));

        // Both bounds of the selected row are ringed, at the hover marker radius.
        AssertThat(canvas.Circles.Count).IsEqual(2);
        AssertThat(Approx(canvas.Circles[0].Radius, 6f)).IsTrue();
        AssertThat(canvas.StrokeCount).IsEqual(1);
        AssertThat(canvas.StrokeColors[0]).IsEqual(new Color(1f, 0f, 1f));
        AssertThat(canvas.StrokeWidths[0]).IsEqual(4f);

        // Without a selection the band is drawn (and, with the border lines off, not stroked).
        var plain = RenderBands(new RangeAreaMark { ShowBorderLines = false }, Bands());
        AssertThat(plain.StrokeCount).IsEqual(0);
    }

    // ── Degenerate data ──────────────────────────────────────────────────────

    [TestCase]
    public void RangeAreaInvertedBoundsDrawNoNonFiniteGeometry()
    {
        var data = new List<DataRow>
        {
            D(("cat", "A"), ("value", 5.0), ("lower", 15.0)),   // lower > upper
            D(("cat", "B"), ("value", 3.0), ("lower", 20.0)),
        };

        var canvas = RenderBands(new RangeAreaMark(), data);

        AssertThat(canvas.FillCount).IsEqual(1);
        AssertThat(canvas.NonFiniteCoordinateCount).IsEqual(0);
        AssertThat(canvas.NegativeSizeRectCount).IsEqual(0);
    }

    [TestCase]
    public void RangeAreaWithASingleRowDrawsNothing()
    {
        var data = new List<DataRow> { D(("cat", "A"), ("value", 12.0), ("lower", 8.0)) };

        var canvas = RenderBands(new RangeAreaMark(), data);

        AssertThat(canvas.FillCount).IsEqual(0);
        AssertThat(canvas.StrokeCount).IsEqual(0);
    }

    // ── Style callback receives the rendered row index ──────────────────────

    [TestCase]
    public void RangeAreaUsesTheFirstVisibleRowForColor()
    {
        var canvas = new FakeCanvas2D();
        var chart = new Chart(canvas) { Width = 400f, Height = 300f };
        chart.Data(new List<DataRow>
        {
            D(("cat", "A"), ("value", 12.0), ("lower", 8.0)),
            D(("cat", "B"), ("value", 22.0), ("lower", 16.0)),
        });

        int overrideIndex = -1;
        chart.Mark(new RangeAreaMark
        {
            StyleOverride = (_, index, style) => { overrideIndex = index; return style; },
        });
        chart.Encode(Channel.X, "cat");
        chart.Encode(Channel.Y, "value");
        chart.Render();

        // The callback must receive the *row* index of the row the colour came from.
        AssertThat(overrideIndex).IsEqual(0);
    }

    // ── Scale contribution ─────────────────────────────────────────────────

    [TestCase]
    public void RangeAreaContributesTheLowerBoundToTheValueAxis()
    {
        var data = new List<DataRow>
        {
            D(("cat", "A"), ("value", 12.0), ("lower", 8.0)),
            D(("cat", "B"), ("value", 22.0), ("lower", -30.0)),
        };

        var scales = new ScaleSet();
        new RangeAreaMark().ContributeScales(scales, TestContexts.XyEncodes("cat", "value"), data);

        // The value axis is fitted from the encoded Y column, which alone would end at -30 nowhere:
        // the lower bound has to widen it, otherwise the bottom of the band is clipped by the plot.
        var y = scales.TryGet(Channel.Y) as LinearScale;
        AssertThat(y is not null).IsTrue();
        AssertThat(y!.Min <= -30.0).IsTrue();
        AssertThat(y.Max >= 22.0).IsTrue();
    }

    [TestCase]
    public void RangeAreaScaleContributionWidensAnExistingScale()
    {
        var data = new List<DataRow>
        {
            D(("cat", "A"), ("value", 12.0), ("lower", 8.0)),
            D(("cat", "B"), ("value", 22.0), ("lower", 16.0)),
        };

        var scales = new ScaleSet();
        scales.Set(Channel.Y, new LinearScale(0, 100));

        new RangeAreaMark().ContributeScales(scales, TestContexts.XyEncodes("cat", "value"), data);

        // Already wider than the band: the scale is kept as it is.
        var y = scales.TryGet(Channel.Y) as LinearScale;
        AssertThat(y!.Min).IsEqual(0.0);
        AssertThat(y.Max).IsEqual(100.0);
    }

    [TestCase]
    public void RangeAreaScaleContributionSkipsMissingAndNonFiniteBounds()
    {
        var data = new List<DataRow>
        {
            D(("cat", "A"), ("value", 12.0), ("lower", null)),          // named null
            D(("cat", "B"), ("value", 22.0), ("lower", double.NaN)),
            D(("cat", "C"), ("value", 30.0)),                           // no lower field at all
            D(("cat", "D"), ("value", 40.0), ("lower", "n/a")),         // not a number
        };

        var scales = new ScaleSet();
        new RangeAreaMark().ContributeScales(scales, TestContexts.XyEncodes("cat", "value"), data);

        // Only the finite bounds count: the upper bounds 12..40, nothing from the broken lower ones.
        var y = scales.TryGet(Channel.Y) as LinearScale;
        AssertThat(y is not null).IsTrue();
        AssertThat(y!.Min).IsEqual(12.0);
        AssertThat(y.Max).IsEqual(40.0);
    }

    [TestCase]
    public void RangeAreaScaleContributionInstallsNothingForUnusableBounds()
    {
        var data = new List<DataRow>
        {
            D(("cat", "A"), ("lower", null)),
            D(("cat", "B"), ("lower", double.NaN)),
        };

        var scales = new ScaleSet();
        new RangeAreaMark().ContributeScales(scales, TestContexts.XyEncodes("cat", "value"), data);

        // Nothing usable: the mark leaves the axis to the auto-fit instead of installing an empty one.
        AssertThat(scales.Has(Channel.Y)).IsFalse();
    }

    [TestCase]
    public void RangeAreaScaleContributionKeepsAZeroWidthRangeOffTheAxis()
    {
        var data = new List<DataRow>
        {
            D(("cat", "A"), ("value", 8.0), ("lower", 8.0)),
            D(("cat", "B"), ("value", 8.0), ("lower", 8.0)),
        };

        var scales = new ScaleSet();
        new RangeAreaMark().ContributeScales(scales, TestContexts.XyEncodes("cat", "value"), data);

        // A [8, 8] domain maps everything to one point (and divides by zero); the auto-fit already
        // covers such a constant band, so no degenerate scale is installed here.
        AssertThat(scales.Has(Channel.Y)).IsFalse();
    }

    /// <summary>
    /// End-to-end: the chart fits its value axis through <see cref="Mark.ContributeScales"/>, so the
    /// lower bound has to reach the scale the mark is rendered with.
    /// </summary>
    [TestCase]
    public void RangeAreaChartFitsTheValueAxisToTheLowerBound()
    {
        var probe = new ScaleProbe();
        var chart = new Chart(new FakeCanvas2D()) { Width = 400f, Height = 300f };
        chart.Data(new List<DataRow>
        {
            D(("cat", "A"), ("value", 12.0), ("lower", 8.0)),
            D(("cat", "B"), ("value", 22.0), ("lower", -30.0)),
        });
        chart.Mark(new RangeAreaMark());
        chart.Mark(probe);
        chart.Encode(Channel.X, "cat");
        chart.Encode(Channel.Y, "value");

        chart.Render();

        AssertThat(probe.Y is not null).IsTrue();
        AssertThat(probe.Y!.Min <= -30.0).IsTrue();
    }

    /// <summary>Mark that reports the scales it was rendered with (the chart does not expose them).</summary>
    private sealed class ScaleProbe : Mark
    {
        /// <inheritdoc />
        public override MarkCoordinate Coordinate => MarkCoordinate.Cartesian;

        /// <summary>The Y scale of the last render.</summary>
        public LinearScale? Y { get; private set; }

        /// <inheritdoc />
        public override void Render(MarkContext ctx) => Y = ctx.Scales.TryGet(Channel.Y) as LinearScale;
    }
}
