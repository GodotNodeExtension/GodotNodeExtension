namespace GodotNodeExtension.Tests.GodotChart.Marks;

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
/// Behaviour specification for <see cref="PointMark"/>: the dot radius and the Size channel, the
/// per-row colour channel, value labels, hover scaling, hit testing (radius plus theme padding)
/// and hot-path allocation. The skip of a non-finite row is pinned once for every mark by
/// <see cref="MarkDataSafetyTest"/>.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class PointMarkTest
{
    private static readonly string[] TwoCategories = { "A", "B" };


    private static DataRow D(params (string Field, object? Value)[] fields) => TestContexts.Row(fields);

    private static List<DataRow> Points() =>

    [
        D(("x", 1.0), ("y", 5.0), ("size", 3.0)),
        D(("x", 2.0), ("y", 9.0), ("size", 6.0)),
        D(("x", 3.0), ("y", 7.0), ("size", 4.0)),
    ];

    private static ColorScale ColorBy(IEnumerable<string> keys)
    {
        var scale = new ColorScale();
        scale.Fit(keys);
        return scale;
    }

    // ── Radius & size channel ────────────────────────────────────────────────

    [TestCase]
    public void PointDefaultRadiusReachesTheFirstCircle()
    {
        var canvas = new FakeCanvas2D();
        var scales = new ScaleSet();
        scales.Set(Channel.X, new LinearScale(0, 4));
        scales.Set(Channel.Y, new LinearScale(0, 10));

        new PointMark { DefaultRadius = 7f }.Render(
            TestContexts.Mark(canvas, Points(), TestContexts.XyEncodes("x", "y"), scales));

        AssertThat(canvas.Circles.Count).IsEqual(3);
        AssertThat(Approx(canvas.Circles[0].Radius, 7f)).IsTrue();
    }

    [TestCase]
    public void PointSizeChannelMakesTheRadiusGrowWithTheValue()
    {
        var encodes = TestContexts.XyEncodes("x", "y");
        encodes.Set(Channel.Size, new FieldEncode("size"));

        var sizeScale = new LinearScale();
        sizeScale.Fit(new object[] { 3.0, 6.0, 4.0 });

        var scales = new ScaleSet();
        scales.Set(Channel.X, new LinearScale(0, 4));
        scales.Set(Channel.Y, new LinearScale(0, 10));
        scales.Set(Channel.Size, sizeScale);

        var canvas = new FakeCanvas2D();
        new PointMark().Render(
            TestContexts.Mark(canvas, Points(), encodes, scales, theme: ChartTheme.Dark()));

        // sizes 3 < 4 < 6 -> radii strictly increasing
        AssertThat(canvas.Circles[1].Radius > canvas.Circles[2].Radius).IsTrue();
        AssertThat(canvas.Circles[2].Radius > canvas.Circles[0].Radius).IsTrue();
    }

    [TestCase]
    public void AMarkLevelConstantEncodeOverridesTheChartChannel()
    {
        var canvas = new FakeCanvas2D();
        var chart = new Chart(canvas) { Width = 400f, Height = 300f };
        chart.Data(new List<DataRow>
        {
            TestContexts.Row(("x", 1.0), ("y", 1.0), ("series", "A")),
            TestContexts.Row(("x", 2.0), ("y", 2.0), ("series", "B")),
        });
        // The chart binds a colour channel, the mark pins its own colour: the mark wins.
        chart.Mark(new PointMark().Encode(Channel.Color, Colors.Red));
        chart.Encode(Channel.X, "x");
        chart.Encode(Channel.Y, "y");
        chart.Encode(Channel.Color, "series");

        chart.Render();

        string background = ChartTheme.Dark().BackgroundColor.ToHtml();
        var dotFills = canvas.FillColors
            .Where(c => c.ToHtml() != background)
            .Select(c => c.ToHtml())
            .Distinct()
            .ToList();

        AssertThat(dotFills.Count).IsEqual(1);
        AssertThat(dotFills[0]).IsEqual(Colors.Red.ToHtml());
    }

    [TestCase]
    public void AutoFittedSizeScaleSpansTheDataSoTheSmallestDotStaysSmallest()
    {
        // The size channel is fitted to the data extent, not to zero: a bubble's radius says where the
        // value sits inside the column, so the smallest value has to reach the theme's PointSizeMin.
        var canvas = new FakeCanvas2D();
        var chart = new Chart(canvas) { Width = 400f, Height = 300f };
        chart.Data(new List<DataRow>
        {
            TestContexts.Row(("x", 1.0), ("y", 1.0), ("size", 20.0)),
            TestContexts.Row(("x", 2.0), ("y", 2.0), ("size", 30.0)),
        });
        chart.Mark(new PointMark());
        chart.Encode(Channel.X, "x");
        chart.Encode(Channel.Y, "y");
        chart.Encode(Channel.Size, "size");

        chart.Render();

        var theme = ChartTheme.Dark();
        AssertThat(canvas.Circles.Count).IsEqual(2);
        AssertThat(Approx(canvas.Circles[0].Radius, theme.PointSizeMin)).IsTrue();
        AssertThat(Approx(canvas.Circles[1].Radius, theme.PointSizeMin + theme.PointSizeRange)).IsTrue();
    }

    [TestCase]
    public void NonFiniteSizeKeepsTheDefaultRadius()
    {
        var encodes = TestContexts.XyEncodes("x", "y");
        encodes.Set(Channel.Size, new FieldEncode("size"));

        var sizeScale = new LinearScale();
        sizeScale.Fit(new object[] { 3.0, 6.0 });

        var scales = new ScaleSet();
        scales.Set(Channel.X, new LinearScale(0, 4));
        scales.Set(Channel.Y, new LinearScale(0, 10));
        scales.Set(Channel.Size, sizeScale);

        var rows = new List<DataRow>
        {
            TestContexts.Row(("x", 1.0), ("y", 1.0), ("size", double.NaN)),
        };

        var canvas = new FakeCanvas2D();
        new PointMark { DefaultRadius = 7f }.Render(
            TestContexts.Mark(canvas, rows, encodes, scales, theme: ChartTheme.Dark()));

        // A NaN size must not turn into a NaN radius (the backend would choke on it).
        AssertThat(Approx(canvas.Circles[0].Radius, 7f)).IsTrue();
    }

    [TestCase]
    public void NonFiniteOpacityFallsBackToTheDefault()
    {
        var encodes = TestContexts.XyEncodes("x", "y");
        encodes.Set(Channel.Opacity, new FieldEncode("alpha"));

        var opacityScale = new LinearScale();
        opacityScale.Fit(new object[] { 0.5, 1.0 });

        var scales = new ScaleSet();
        scales.Set(Channel.X, new LinearScale(0, 4));
        scales.Set(Channel.Y, new LinearScale(0, 10));
        scales.Set(Channel.Opacity, opacityScale);

        var rows = new List<DataRow>
        {
            TestContexts.Row(("x", 1.0), ("y", 1.0), ("alpha", double.NaN)),
        };

        var canvas = new FakeCanvas2D();
        new PointMark().Render(
            TestContexts.Mark(canvas, rows, encodes, scales, theme: ChartTheme.Dark()));

        AssertThat(canvas.FillOpacities.Count).IsEqual(1);
        AssertThat(float.IsFinite(canvas.FillOpacities[0])).IsTrue();
        AssertThat(canvas.FillOpacities[0] > 0f).IsTrue();
    }

    // ── Colour & labels ──────────────────────────────────────────────────────

    [TestCase]
    public void PointColorChannelGivesEachRowItsOwnFillColour()
    {
        var data = new List<DataRow>
        {
            D(("cat", "A"), ("x", 1.0), ("y", 5.0)),
            D(("cat", "B"), ("x", 2.0), ("y", 9.0)),
        };
        var encodes = TestContexts.XyEncodes("x", "y");
        encodes.Set(Channel.Color, new FieldEncode("cat"));

        var scales = new ScaleSet();
        scales.Set(Channel.X, new LinearScale(0, 3));
        scales.Set(Channel.Y, new LinearScale(0, 10));
        scales.Set(Channel.Color, ColorBy(TwoCategories));

        var canvas = new FakeCanvas2D();
        new PointMark().Render(TestContexts.Mark(canvas, data, encodes, scales));

        AssertThat(canvas.FillColors.Count).IsEqual(2);
        AssertThat(canvas.FillColors[0] != canvas.FillColors[1]).IsTrue();
    }

    [TestCase]
    public void PointShowLabelIsDrawnAboveTheDot()
    {
        var scales = new ScaleSet();
        scales.Set(Channel.X, new LinearScale(0, 4));
        scales.Set(Channel.Y, new LinearScale(0, 10));

        var canvas = new FakeCanvas2D();
        new PointMark { ShowLabel = true }.Render(
            TestContexts.Mark(canvas, Points(), TestContexts.XyEncodes("x", "y"), scales));

        AssertThat(canvas.Texts.Contains("5")).IsTrue();
        AssertThat(canvas.TextDraws.Count).IsEqual(3);
        AssertThat(canvas.TextDraws[0].Y < canvas.Circles[0].Cy).IsTrue();
    }

    // ── Hover & hit testing ──────────────────────────────────────────────────

    [TestCase]
    public void PointHoverMultipliesTheRadius()
    {
        var theme = ChartTheme.Dark();
        theme.PointHoverRadiusRatio = 1.5f;

        var scales = new ScaleSet();
        scales.Set(Channel.X, new LinearScale(0, 4));
        scales.Set(Channel.Y, new LinearScale(0, 10));

        var canvas = new FakeCanvas2D();
        new PointMark { DefaultRadius = 5f }.Render(TestContexts.Mark(
            canvas, Points(), TestContexts.XyEncodes("x", "y"), scales,
            theme: theme, hoveredRowIndex: 0));

        AssertThat(Approx(canvas.Circles[0].Radius, 5f * 1.5f)).IsTrue();
    }

    [TestCase]
    public void PointHitTestUsesTheRadiusPlusTheThemePadding()
    {
        var theme = ChartTheme.Dark();
        theme.HitTestPointPadding = 4f;

        var scales = new ScaleSet();
        scales.Set(Channel.X, new LinearScale(0, 2));
        scales.Set(Channel.Y, new LinearScale(0, 10));

        var data = new List<DataRow> { D(("x", 1.0), ("y", 5.0)) };
        var canvas = new FakeCanvas2D();
        var mark = new PointMark { DefaultRadius = 5f };
        var ctx = TestContexts.Mark(canvas, data, TestContexts.XyEncodes("x", "y"), scales, theme: theme);
        mark.Render(ctx);

        // dot centre sits at (200, 150); radius 5 + padding 4 = 9
        AssertThat(mark.HitTest(ctx, new Vector2(200f, 150f)) is not null).IsTrue();
        AssertThat(mark.HitTest(ctx, new Vector2(200f, 160f)) is null).IsTrue();
    }

    // ── Allocation ───────────────────────────────────────────────────────────

    /// <summary>Objects a whole frame may create regardless of the element count (axes, grid, background).</summary>
    private const int FrameOverheadBudget = 12;

    [TestCase]
    public void PointMarkDoesNotAllocatePerPoint()
    {
        var small = RenderOnce(new PointMark(), MarkCases.ManyPoints(8), "x", "y");
        var large = RenderOnce(new PointMark(), MarkCases.ManyPoints(300), "x", "y");

        int delta = (large.Paths + large.Paints) - (small.Paths + small.Paints);
        AssertThat(delta <= FrameOverheadBudget).IsTrue();
    }

    private static (int Paths, int Paints) RenderOnce(Mark mark, List<DataRow> data,
        string x, string y, string? color = null, int frames = 1)
    {
        var canvas = new FakeCanvas2D();
        var chart = new Chart(canvas) { Width = 400f, Height = 300f };
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
}
