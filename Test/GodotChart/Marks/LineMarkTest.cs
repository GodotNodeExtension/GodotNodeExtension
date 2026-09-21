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

/// <summary>
/// Behaviour specification for <see cref="LineMark"/>: step and smoothing modes, the optional area
/// fill, stacking bands and their shared edges, stacked hit testing, hover highlighting and labels,
/// and how rows with missing values are skipped without shifting the points or the labels. The
/// skip of a non-finite value is pinned once for every mark by <see cref="MarkDataSafetyTest"/>.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class LineMarkTest
{
    private static readonly string[] SingleCategory = { "A" };
    private static readonly string[] ThreeCategories = { "A", "B", "C" };

    private static DataRow D(params (string Field, object? Value)[] fields) => TestContexts.Row(fields);

    private static List<DataRow> Simple() =>
    [
        D(("cat", "A"), ("value", 10.0)),
        D(("cat", "B"), ("value", 20.0)),
        D(("cat", "C"), ("value", 15.0)),
    ];

    private static List<DataRow> Series() =>
    [
        D(("cat", "A"), ("value", 10.0), ("series", "S1")),
        D(("cat", "B"), ("value", 20.0), ("series", "S1")),
        D(("cat", "C"), ("value", 15.0), ("series", "S1")),
        D(("cat", "A"), ("value", 8.0), ("series", "S2")),
        D(("cat", "B"), ("value", 14.0), ("series", "S2")),
        D(("cat", "C"), ("value", 18.0), ("series", "S2")),
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

    /// <summary>Path operations produced by one line through the three default categories.</summary>
    private static int LinePathOpCount(StepMode step, bool smooth = false, StackMode stack = StackMode.None)
    {
        var canvas = new FakeCanvas2D();
        var scales = TestContexts.CategoryScales(ThreeCategories, 0, 25);
        new LineMark { Step = step, Smooth = smooth, Stack = stack }.Render(
            TestContexts.Mark(canvas, Simple(), TestContexts.XyEncodes("cat", "value"), scales));
        return canvas.PathOpCount;
    }

    // ── Step & smoothing ─────────────────────────────────────────────────────

    [TestCase]
    public void LineStepModesProduceTheirCharacteristicPathOpCounts()
    {
        // Three points: straight = Move + 2 lines; each step mode inserts 2 (or 3) ops per segment.
        AssertThat(LinePathOpCount(StepMode.None)).IsEqual(3);
        AssertThat(LinePathOpCount(StepMode.Before)).IsEqual(5);
        AssertThat(LinePathOpCount(StepMode.After)).IsEqual(5);
        AssertThat(LinePathOpCount(StepMode.Center)).IsEqual(7);
    }

    [TestCase]
    public void LineSmoothDrawsCubicSegmentsAndStraightDoesNot()
    {
        var smoothCanvas = new FakeCanvas2D();
        var scales = TestContexts.CategoryScales(ThreeCategories, 0, 25);
        new LineMark { Smooth = true, Step = StepMode.None }.Render(
            TestContexts.Mark(smoothCanvas, Simple(), TestContexts.XyEncodes("cat", "value"), scales));

        var straightCanvas = new FakeCanvas2D();
        new LineMark { Smooth = false, Step = StepMode.None }.Render(
            TestContexts.Mark(straightCanvas, Simple(), TestContexts.XyEncodes("cat", "value"), scales));

        AssertThat(smoothCanvas.CubicCount).IsEqual(2);   // one curve per segment
        AssertThat(straightCanvas.CubicCount).IsEqual(0);
    }

    // ── Area & stacking ──────────────────────────────────────────────────────

    /// <summary>
    /// The area gradient goes into a paint of its own. Reusing the mark's pooled shape paint would keep
    /// the shader set on it (Skia lets a shader win over SetColor) and stroke the line with the fading
    /// gradient instead of the series colour - so the two paints must stay separate, and the fill must
    /// not carry the stripe colour the stroke uses.
    /// </summary>
    [TestCase]
    public void LineAreaGradientDoesNotShareTheStrokePaint()
    {
        var canvas = new FakeCanvas2D();
        var scales = TestContexts.CategoryScales(ThreeCategories, 0, 25);
        canvas.PaintCreateCount = 0;

        new LineMark { ShowArea = true, Smooth = false }.Render(
            TestContexts.Mark(canvas, Simple(), TestContexts.XyEncodes("cat", "value"), scales));

        AssertThat(canvas.FillCount).IsEqual(1);       // the gradient area
        AssertThat(canvas.StrokeCount).IsEqual(1);     // the line
        // Different paint state: the gradient paint is never given a solid colour, the stroke paint is.
        AssertThat(canvas.FillColors[0].ToHtml()).IsNotEqual(canvas.StrokeColors[0].ToHtml());
        // ... and it really is a second paint object (the pooled one plus the gradient one).
        AssertThat(canvas.PaintCreateCount >= 2).IsTrue();
    }

    [TestCase]
    public void LineShowAreaAddsAFill()
    {
        var scales = TestContexts.CategoryScales(ThreeCategories, 0, 25);

        var withArea = new FakeCanvas2D();
        new LineMark { ShowArea = true, Smooth = false }.Render(
            TestContexts.Mark(withArea, Simple(), TestContexts.XyEncodes("cat", "value"), scales));

        var withoutArea = new FakeCanvas2D();
        new LineMark { ShowArea = false, Smooth = false }.Render(
            TestContexts.Mark(withoutArea, Simple(), TestContexts.XyEncodes("cat", "value"), scales));

        AssertThat(withArea.FillCount).IsEqual(1);
        AssertThat(withoutArea.FillCount).IsEqual(0);
    }

    [TestCase]
    public void LineMultiSeriesStackDrawsOneBandPerSeries()
    {
        var encodes = TestContexts.XyEncodes("cat", "value");
        encodes.Set(Channel.Color, new FieldEncode("series"));

        var scales = new ScaleSet();
        scales.Set(Channel.X, Ordinal(ThreeCategories));
        scales.Set(Channel.Y, new LinearScale(0, 40));

        var canvas = new FakeCanvas2D();
        new LineMark { Stack = StackMode.Stack, Smooth = false }.Render(
            TestContexts.Mark(canvas, Series(), encodes, scales));

        AssertThat(canvas.FillCount).IsEqual(2);    // one band per series
        AssertThat(canvas.StrokeCount).IsEqual(2);  // and one top line per series
    }

    /// <summary>
    /// A stacked band maps its X through the same guard as every other path. On a numeric X axis a row whose X
    /// is text (a colour string in a numeric column) had no answer to give: the raw <c>Map</c> threw, which cost
    /// the whole mark for that frame, and the NaN it produced elsewhere made the band claim a non-finite
    /// coordinate. The row is skipped, and the band still covers the rows that do map.
    /// </summary>
    [TestCase]
    public void LineStackedBandSkipsANonNumericX()
    {
        var data = new List<DataRow>
        {
            D(("x", 0.0), ("value", 10.0), ("series", "S1")),
            D(("x", 1.0), ("value", 20.0), ("series", "S1")),
            D(("x", "oops"), ("value", 30.0), ("series", "S1")),
        };
        var encodes = TestContexts.XyEncodes("x", "value");
        encodes.Set(Channel.Color, new FieldEncode("series"));

        var scales = new ScaleSet();
        scales.Set(Channel.X, new LinearScale(0, 2));
        scales.Set(Channel.Y, new LinearScale(0, 30));

        var canvas = new FakeCanvas2D();
        new LineMark { Stack = StackMode.Stack, Smooth = false }.Render(
            TestContexts.Mark(canvas, data, encodes, scales));

        AssertThat(canvas.NonFiniteCoordinateCount).IsEqual(0);
        AssertThat(canvas.FillCount).IsEqual(1);    // the band of the two usable points is still filled
    }

    /// <summary>Path operations of one single-series stacked band: the area fill and the top stroke.</summary>
    private static int StackedBandPathOpCount(StepMode step, bool smooth)
    {
        var canvas = new FakeCanvas2D();
        var scales = TestContexts.CategoryScales(ThreeCategories, 0, 25);
        new LineMark { Step = step, Smooth = smooth, Stack = StackMode.Stack }.Render(
            TestContexts.Mark(canvas, Simple(), TestContexts.XyEncodes("cat", "value"), scales));
        return canvas.PathOpCount;
    }

    [TestCase]
    public void LineStackedBandUsesItsStepPathForBothEdges()
    {
        // One band = the area fill (upper edge + lower edge) plus the top line, so a band counts
        // three edges. The lower edge used to fall back to straight segments whenever Step was set
        // while the upper one stepped, which is what left a seam against the layer below.
        var configs = new[]
        {
            (Step: StepMode.Before, Smooth: false),
            (Step: StepMode.After,  Smooth: false),
            (Step: StepMode.Center, Smooth: false),
            (Step: StepMode.None,   Smooth: true),   // smoothed band: both edges are cubic
        };

        foreach (var (step, smooth) in configs)
        {
            int edge = LinePathOpCount(step, smooth);
            AssertThat(StackedBandPathOpCount(step, smooth)).IsEqual(edge * 3);
        }
    }

    [TestCase]
    public void LineStackedHitTestFollowsTheAccumulatedBaseline()
    {
        // S1 is 20 in every category and S2 about 10, so the upper band is drawn near 30 while S2's
        // own value maps to half that height. Hit testing used to measure at the raw value, so
        // hovering the band found nothing (or a row of the wrong layer).
        var data = new List<DataRow>
        {
            D(("cat", "A"), ("value", 20.0), ("series", "S1")),
            D(("cat", "B"), ("value", 20.0), ("series", "S1")),
            D(("cat", "C"), ("value", 20.0), ("series", "S1")),
            D(("cat", "A"), ("value", 10.0), ("series", "S2")),
            D(("cat", "B"), ("value", 10.4), ("series", "S2")),
            D(("cat", "C"), ("value", 10.0), ("series", "S2")),
        };
        var encodes = TestContexts.XyEncodes("cat", "value");
        encodes.Set(Channel.Color, new FieldEncode("series"));

        var scales = new ScaleSet();
        scales.Set(Channel.X, Ordinal(ThreeCategories));
        scales.Set(Channel.Y, new LinearScale(0, 40));

        var mark = new LineMark { Stack = StackMode.Stack, Smooth = false };
        // A narrow plot puts the categories 6.7px apart, so a point between two of them is within the
        // 12px snap distance of both.
        var ctx = TestContexts.Mark(new FakeCanvas2D(), data, encodes, scales,
                                    plot: new PlotArea(0, 0, 40, 300));

        // S2's band runs at y = 300 - 30/40 * 300 = 75 (category A) and y = 72 (category B); the
        // point between the two must answer with one of those two rows, not with S1's.
        var hit = mark.HitTest(ctx, new Vector2(13.33f, 73.5f));
        AssertThat(hit).IsNotNull();
        AssertThat(hit!.SeriesKey).IsEqual("S2");
        AssertThat(hit.RowIndex is 3 or 4).IsTrue();

        // The raw value height of S2 (10.4 -> y = 222) is empty: nothing is drawn there.
        AssertThat(mark.HitTest(ctx, new Vector2(20f, 222f)) is null).IsTrue();
    }

    // ── Hover & labels ───────────────────────────────────────────────────────

    [TestCase]
    public void LineHoverHighlightsTheRowAfterAGap()
    {
        var canvas = new FakeCanvas2D();
        var chart = Chart2D(canvas,
        [
            D(("cat", "A"), ("value", 1.0)),
            D(("cat", "B")), // value intentionally missing -> skipped by the point builder
            D(("cat", "C"), ("value", 3.0)),
        ], new LineMark { ShowLabel = false }, "cat", "value");

        chart.Render();               // establishes the point list
        canvas.Circles.Clear();

        chart.Hover(2);               // the row after the gap
        chart.Render();

        AssertThat(canvas.Circles.Count).IsEqual(1);
        // Category C sits at 5/6 of the plot width; if the rows and points had drifted apart the
        // highlight would be missing or drawn at another category.
        var plot = chart.CurrentPlotArea!.Value;
        float expectedX = plot.X + plot.Width * (5f / 6f);
        AssertThat(MathF.Abs(canvas.Circles[0].Cx - expectedX) < 1f).IsTrue();
    }

    /// <summary>
    /// <see cref="Chart.Select(int)"/> alone has to draw the selection ring. The row-to-index map the ring
    /// needs was built only while a row was <i>hovered</i>, so a programmatic selection drew nothing until
    /// the pointer happened to be over the chart - and the ring disappeared again as soon as it left.
    /// </summary>
    [TestCase]
    public void LineSelectionRingDoesNotNeedAHover()
    {
        var canvas = new FakeCanvas2D();
        var chart = Chart2D(canvas, Simple(), new LineMark { ShowLabel = false }, "cat", "value");

        chart.Render();
        canvas.Circles.Clear();

        chart.Select(1);              // no hover anywhere
        chart.Render();

        // The selection ring is a stroked circle at the selected vertex (category B, the middle category).
        AssertThat(canvas.Circles.Count).IsEqual(1);
        var plot = chart.CurrentPlotArea!.Value;
        float expectedX = plot.X + plot.Width * (3f / 6f);
        AssertThat(MathF.Abs(canvas.Circles[0].Cx - expectedX) < 1f).IsTrue();

        // ...and clearing the selection removes it again.
        canvas.Circles.Clear();
        chart.Select(-1);
        chart.Render();
        AssertThat(canvas.Circles.Count).IsEqual(0);
    }

    [TestCase]
    public void LineLabelsStayAlignedWhenARowIsSkipped()
    {
        var canvas = new FakeCanvas2D();
        var chart = Chart2D(canvas,
        [
            D(("cat", "A"), ("value", 1.0)),
            D(("cat", "B")),
            D(("cat", "C"), ("value", 3.0)),
        ], new LineMark { ShowLabel = true, LabelFormat = "v{0}" }, "cat", "value");

        chart.Render();

        // Two points are drawn, so exactly two data labels are produced (no phantom third label).
        var valueLabels = canvas.Texts.Where(t => t == "v1" || t == "v3").ToList();
        AssertThat(valueLabels.Count).IsEqual(2);
        AssertThat(canvas.Texts.Contains("v0")).IsFalse();
    }

    // ── Degenerate data ──────────────────────────────────────────────────────

    [TestCase]
    public void LineWithASingleRowDrawsNothing()
    {
        var canvas = new FakeCanvas2D();
        var scales = TestContexts.CategoryScales(SingleCategory, 0, 25);
        var data = new List<DataRow> { D(("cat", "A"), ("value", 10.0)) };

        new LineMark().Render(
            TestContexts.Mark(canvas, data, TestContexts.XyEncodes("cat", "value"), scales));

        AssertThat(canvas.DrewAnything).IsFalse();
    }

    /// <summary>
    /// A continuous colour scale installed on the chart while the mark does not encode the colour
    /// channel at all: the mark groups its rows under a placeholder key, and handing that key to a ramp
    /// used to throw inside the render stage ("Cannot convert value '__default__' to double"). The mark
    /// keeps the default colour instead, exactly like the per-row path does.
    /// </summary>
    [TestCase]
    public void AColourScaleWithoutAColourEncodeKeepsTheDefaultColour()
    {
        var canvas = new FakeCanvas2D();
        var chart = Chart2D(canvas, Simple(), new LineMark { Smooth = false }, "cat", "value");
        chart.Scale(Channel.Color, new SequentialColorScale(0, 100));

        chart.Render();

        AssertThat(canvas.DrewAnything).IsTrue();
        AssertThat(canvas.StrokeColors.Contains(ChartTheme.Dark().DefaultMarkColor)).IsTrue();
    }
}
