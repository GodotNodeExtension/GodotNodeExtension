namespace GodotNodeExtension.Tests.GodotChart;

using System;
using System.Collections.Generic;
using System.Linq;
using GdUnit4;
using Godot;
using GodotNodeExtension.Component.GodotChart;
using GodotNodeExtension.Component.GodotChart.Canvas;
using GodotNodeExtension.Component.GodotChart.Marks;
using Support;
using static GdUnit4.Assertions;

/// <summary>
/// Tests for the default renderers in <c>DefaultRenderers.cs</c> driven through a
/// <see cref="RenderContext"/>: the background, title, grid, axes and their labels, the tick-text
/// fitting helper, the tick computation for every supported scale, and the legend swatches
/// (including the dimmed / disabled / colour-scale-less paths).
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class RenderersTest
{
    private static List<DataRow> CategoryRows() =>

    [
        TestContexts.Row(("cat", "A"), ("value", 10.0)),
        TestContexts.Row(("cat", "B"), ("value", 20.0)),
    ];

    private static Chart CategoryChart(FakeCanvas2D canvas)
    {
        var chart = new Chart(canvas);
        chart.Data(CategoryRows());
        chart.Mark(new IntervalMark());
        chart.Encode(Channel.X, "cat");
        chart.Encode(Channel.Y, "value");
        return chart;
    }

    private static void Approx(float actual, float expected, float tolerance = 1e-3f)
        => AssertThat(MathF.Abs(actual - expected) <= tolerance).IsTrue();

    // ── RenderContext factory ───────────────────────────────────────────────

    private static RenderContext Ctx(FakeCanvas2D canvas, ScaleSet? scales = null,
        ChartTheme? theme = null, string? title = null,
        AxisConfig? x = null, AxisConfig? y = null, AxisConfig? y2 = null,
        LegendConfig? legend = null, ICategoricalColorScale? colorScale = null,
        Vector2? mouse = null)
    {
        var t = theme ?? ChartTheme.Dark();
        return new RenderContext
        {
            Canvas = canvas,
            Plot = new PlotArea(50f, 20f, 300f, 200f),
            Theme = t,
            Scales = scales ?? new ScaleSet(),
            Encodes = new EncodeSet(),
            Data = new List<DataRow>(),
            OffsetX = 0f, OffsetY = 0f, Width = 400f, Height = 300f,
            Title = title,
            PaddingLeft = 50f, PaddingRight = 20f, PaddingTop = 20f, PaddingBottom = 40f,
            BackgroundColor = t.BackgroundColor, GridColor = t.GridColor, AxisColor = t.AxisColor,
            XAxisConfig = x, YAxisConfig = y, Y2AxisConfig = y2, LegendConfig = legend,
            MousePos = mouse, ColorScale = colorScale,
        };
    }

    private static ScaleSet Scales(IScale? x = null, IScale? y = null, IScale? y2 = null)
    {
        var set = new ScaleSet();
        if (x != null) set.Set(Channel.X, x);
        if (y != null) set.Set(Channel.Y, y);
        if (y2 != null) set.Set(Channel.Y2, y2);
        return set;
    }

    // ── Background and title ────────────────────────────────────────────────

    [TestCase]
    public void DrawBackgroundDrawsTheThemedRect()
    {
        var canvas = new FakeCanvas2D();
        var theme = ChartTheme.Dark();
        theme.BackgroundCornerRadius = 12f;

        DefaultRenderers.DrawBackground(Ctx(canvas, theme: theme));

        AssertThat(canvas.RoundRects.Count).IsEqual(1);
        var rect = canvas.RoundRects[0];
        Approx(rect.X, 0f); Approx(rect.Y, 0f);
        Approx(rect.W, 400f); Approx(rect.H, 300f);
        Approx(rect.Radius, 12f);
        AssertThat(canvas.FillColors.Contains(theme.BackgroundColor)).IsTrue();
    }

    [TestCase]
    public void DrawTitlePositionsAndStylesTheTitle()
    {
        var canvas = new FakeCanvas2D();
        var theme = ChartTheme.Dark();
        theme.TitleColor = new Color(0.1f, 0.2f, 0.3f);
        theme.TitleYOffset = 19f;

        DefaultRenderers.DrawTitle(Ctx(canvas, theme: theme, title: "Hello"));

        AssertThat(canvas.TextDraws.Count).IsEqual(1);
        var draw = canvas.TextDraws[0];
        AssertThat(draw.Text).IsEqual("Hello");
        Approx(draw.X, 50f);
        Approx(draw.Y, 19f);
        AssertThat(draw.Font.Size).IsEqual(theme.TitleFontSize);
        AssertThat(draw.Color).IsEqual(new Color(0.1f, 0.2f, 0.3f));
    }

    [TestCase]
    public void DrawTitleDoesNothingWithoutATitle()
    {
        var canvas = new FakeCanvas2D();

        DefaultRenderers.DrawTitle(Ctx(canvas, title: null));

        AssertThat(canvas.DrewAnything).IsFalse();
    }

    // ── Grid and axes ───────────────────────────────────────────────────────

    [TestCase]
    public void DrawGridDrawsOneLinePerTick()
    {
        var canvas = new FakeCanvas2D();
        var xScale = new OrdinalScale();
        xScale.Fit(new object[] { "A", "B" });
        var yScale = new LinearScale(0, 100);

        DefaultRenderers.DrawGrid(Ctx(canvas, scales: Scales(xScale, yScale)));

        int expected = DefaultRenderers.ComputeTicks(yScale).Count
                     + DefaultRenderers.ComputeTicks(xScale).Count;
        AssertThat(expected).IsEqual(8); // 6 vertical-range ticks + 2 categories
        AssertThat(canvas.StrokeCount).IsEqual(expected);
    }

    [TestCase]
    public void DrawAxesDrawsTwoOrThreeLines()
    {
        var withoutY2 = new FakeCanvas2D();
        DefaultRenderers.DrawAxes(Ctx(withoutY2, scales: Scales(y: new LinearScale(0, 1))));
        AssertThat(withoutY2.StrokeCount).IsEqual(2);

        var withY2 = new FakeCanvas2D();
        DefaultRenderers.DrawAxes(Ctx(withY2, scales: Scales(
            y: new LinearScale(0, 1), y2: new LinearScale(0, 1))));
        AssertThat(withY2.StrokeCount).IsEqual(3);
    }

    [TestCase]
    public void DrawAxisLabelsDrawsBothAxisTitlesWithBalancedSaveRestore()
    {
        var canvas = new FakeCanvas2D();
        var xScale = new OrdinalScale();
        xScale.Fit(new object[] { "A", "B" });
        var ctx = Ctx(canvas,
            scales: Scales(xScale, new LinearScale(0, 100)),
            x: new AxisConfig { Title = "Time" },
            y: new AxisConfig { Title = "Value" });

        DefaultRenderers.DrawAxisLabels(ctx);

        AssertThat(canvas.Texts.Contains("A")).IsTrue();
        AssertThat(canvas.Texts.Contains("Time")).IsTrue();
        AssertThat(canvas.Texts.Contains("Value")).IsTrue();
        AssertThat(canvas.YAxisLabels.Contains("0")).IsTrue();
        AssertThat(canvas.YAxisLabels.Contains("100")).IsTrue();
        // The rotated Y-axis title opens one Save/Restore pair.
        AssertThat(canvas.SaveCount).IsEqual(1);
        AssertThat(canvas.SaveRestoreBalanced).IsTrue();

        // A Y2 title adds a second rotated title.
        var y2Canvas = new FakeCanvas2D();
        DefaultRenderers.DrawAxisLabels(Ctx(y2Canvas,
            scales: Scales(xScale, new LinearScale(0, 100), new LinearScale(0, 1)),
            y: new AxisConfig { Title = "Left" },
            y2: new AxisConfig { Title = "Right" }));
        AssertThat(y2Canvas.SaveCount).IsEqual(2);
        AssertThat(y2Canvas.SaveRestoreBalanced).IsTrue();
    }

    /// <summary>Labels drawn left-aligned: the Y2 column of the default renderers (the X column is centred).</summary>
    private static List<string> Y2Labels(FakeCanvas2D canvas)
        => [.. canvas.TextDraws.Where(d => d.Align == TextAlign.Left).Select(d => d.Text)];

    [TestCase]
    public void Y2LabelsAreFittedToTheBandTheRightEdgeOffers()
    {
        var canvas = new FakeCanvas2D();
        // Six-decimal labels are far wider than the 50px band the right edge offers here.
        var ctx = Ctx(canvas, scales: Scales(new LinearScale(0, 1), new LinearScale(0, 100),
                new LinearScale(0, 1)),
            y2: new AxisConfig { LabelFormat = "0.000000" });

        DefaultRenderers.DrawAxisLabels(ctx);

        var labels = Y2Labels(canvas);
        AssertThat(labels.Count).IsGreater(0);
        foreach (string label in labels)
            AssertThat(label.EndsWith('\u2026')).IsTrue();
    }

    [TestCase]
    public void Y2LabelsAreThinnedLikeTheYColumn()
    {
        var canvas = new FakeCanvas2D();
        // 51 categories over a 200px tall plot: a line of text does not fit between two of those ticks.
        var many = new OrdinalScale();
        many.Fit([.. Enumerable.Range(0, 51).Select(i => (object)$"C{i}")]);
        var ctx = Ctx(canvas, scales: Scales(new LinearScale(0, 1), many, many));

        DefaultRenderers.DrawAxisLabels(ctx);

        int ticks = DefaultRenderers.ComputeTicks(many, maxTicks: 64).Count;
        AssertThat(ticks).IsEqual(51);      // every category is a tick, far more than the plot can label
        int yDrawn = canvas.YAxisLabels.Count();
        int y2Drawn = Y2Labels(canvas).Count;
        AssertThat(yDrawn).IsLess(ticks);
        AssertThat(y2Drawn).IsEqual(yDrawn);    // the same stride rule on both columns
    }

    [TestCase]
    public void TheGridAndTheLabelPassShareOneTickSetPerAxis()
    {
        var canvas = new FakeCanvas2D();
        var xScale = new OrdinalScale();
        xScale.Fit(new object[] { "A", "B" });
        var ctx = Ctx(canvas, scales: Scales(xScale, new LinearScale(0, 100)));

        DefaultRenderers.DrawGrid(ctx);

        // The grid built the two axes it draws.
        AssertThat(ctx.TickCache.Count).IsEqual(2);
        var fromGrid = ctx.TickCache[Channel.Y];

        DefaultRenderers.DrawAxisLabels(ctx);

        // The label pass found them instead of rebuilding the ladder and reformatting every entry.
        AssertThat(ctx.TickCache[Channel.Y]).IsSame(fromGrid);
        AssertThat(ctx.TickCache.Count).IsEqual(2);     // no Y2 scale, so only the two axes it has were asked for
    }

    // ── Text fitting and tick computation ───────────────────────────────────

    [TestCase]
    public void FitTextDegradesToAnEllipsisInANarrowSlot()
    {
        var canvas = new FakeCanvas2D();
        var ctx = Ctx(canvas);
        var font = FontSettings.Default; // 13px

        AssertThat(DefaultRenderers.FitText(ctx, "ABCDEF", font, 1000f)).IsEqual("ABCDEF");
        AssertThat(DefaultRenderers.FitText(ctx, "ABCDEF", font, 10f)).IsEqual("\u2026");
        AssertThat(DefaultRenderers.FitText(ctx, "ABCDEF", font, 0f)).IsEqual("ABCDEF");
        AssertThat(DefaultRenderers.FitText(ctx, "", font, 10f)).IsEqual("");
    }

    [TestCase]
    public void ComputeTicksHandlesNullDegenerateAndLimits()
    {
        AssertThat(DefaultRenderers.ComputeTicks(null).Count).IsEqual(0);

        // A degenerate domain collapses to a single middle tick.
        var degenerate = DefaultRenderers.ComputeTicks(new LinearScale(5, 5));
        AssertThat(degenerate.Count).IsEqual(1);
        Approx((float)degenerate[0].Norm, 0.5f);

        // maxTicks caps the result.
        AssertThat(DefaultRenderers.ComputeTicks(new LinearScale(0, 1000), maxTicks: 2).Count).IsEqual(2);

        // A log scale is labelled with powers of ten.
        var log = DefaultRenderers.ComputeTicks(new LogScale(1, 10000));
        AssertThat(log.Select(t => t.Text).Contains("10")).IsTrue();
        AssertThat(log.Select(t => t.Text).Contains("1K")).IsTrue();
        AssertThat(log.Select(t => t.Text).Contains("10K")).IsTrue();

        // An ordinal scale labels every category.
        var ordinal = new OrdinalScale();
        ordinal.Fit(new object[] { "A", "B", "C" });
        var ticks = DefaultRenderers.ComputeTicks(ordinal);
        AssertThat(ticks.Count).IsEqual(3);
        AssertThat(ticks[0].Text).IsEqual("A");

        // A time scale emits one tick per division plus the end tick: the default 5 divisions
        // (TimeScaleDefaultTicks) give 6 ticks.
        var time = new TimeScale(new DateTime(2020, 1, 1), new DateTime(2020, 1, 2));
        AssertThat(DefaultRenderers.ComputeTicks(time).Count).IsEqual(6);
    }

    [TestCase]
    public void TimeScaleUsesTheFallbackTickCount()
    {
        // The time branch used to ignore the caller's tick count (ChartTheme.FallbackTickCount was
        // a dead knob for time axes) and always divided the range into TimeScaleDefaultTicks.
        var time = new TimeScale(new DateTime(2020, 1, 1), new DateTime(2020, 1, 2));

        AssertThat(DefaultRenderers.ComputeTicks(time, fallbackTickCount: 2).Count).IsEqual(3);
        AssertThat(DefaultRenderers.ComputeTicks(time, fallbackTickCount: 12).Count).IsEqual(13);

        // Out-of-range counts are clamped: at least one division, and never an unbounded number of
        // labels (the result is additionally capped by maxTicks).
        AssertThat(DefaultRenderers.ComputeTicks(time, fallbackTickCount: 0).Count).IsEqual(2);
        AssertThat(DefaultRenderers.ComputeTicks(time, fallbackTickCount: 10_000, maxTicks: 8).Count).IsEqual(8);

        // The first and the last tick sit exactly on the domain bounds.
        var ticks = DefaultRenderers.ComputeTicks(time, fallbackTickCount: 2);
        Approx((float)ticks[0].Norm, 0f);
        Approx((float)ticks[^1].Norm, 1f);
    }

    [TestCase]
    public void TimeScaleWithAnEmptyOrReversedRangeYieldsASingleTick()
    {
        // Max <= Min has no span to divide: it used to emit TimeScaleDefaultTicks identical labels
        // (and grid lines) spread over the whole plot instead of a single position.
        foreach (var time in new[]
                 {
                     new TimeScale(new DateTime(2020, 1, 1), new DateTime(2020, 1, 1)), // empty
                     new TimeScale(new DateTime(2020, 1, 2), new DateTime(2020, 1, 1)), // reversed
                     new TimeScale(),                                                   // never fitted
                 })
        {
            var ticks = DefaultRenderers.ComputeTicks(time);
            AssertThat(ticks.Count).IsEqual(1);
            Approx((float)ticks[0].Norm, 0.5f);
            AssertThat(ticks[0].Text.Length > 0).IsTrue();
        }
    }

    [TestCase]
    public void ADegenerateTimeAxisCollapsesToASingleTick()
    {
        // End to end: a time axis without a span has nothing to divide, so it labels and grids one
        // position instead of spreading 6 identical labels across the plot.
        var canvas = new FakeCanvas2D();
        var chart = new Chart(canvas);
        var later = new DateTime(2020, 1, 2);
        var earlier = new DateTime(2020, 1, 1);
        chart.Data(new List<DataRow>
        {
            TestContexts.Row(("t", earlier), ("value", 10.0)),
            TestContexts.Row(("t", later), ("value", 20.0)),
        });
        chart.Mark(new PointMark());
        chart.Encode(Channel.X, "t");
        chart.Encode(Channel.Y, "value");
        // Min after Max: no span to divide (a caller-side mistake).
        chart.Scale(Channel.X, new TimeScale(later, earlier));

        chart.Render();

        // Vertical lines come from the X grid scale (the left Y axis line sits exactly on the plot
        // X edge, so it is excluded): one tick means one vertical grid line.
        var plot = chart.CurrentPlotArea!.Value;
        int verticalGridLines = canvas.Lines.Count(line =>
            MathF.Abs(line.X1 - line.X0) < 1e-3f && MathF.Abs(line.X0 - plot.X) > 1e-3f);
        AssertThat(verticalGridLines).IsEqual(1);
        AssertThat(canvas.TextDraws.Count > 0).IsTrue();
    }

    // ── Legend ──────────────────────────────────────────────────────────────

    [TestCase]
    public void DrawLegendDrawsSwatchesAndLabels()
    {
        var canvas = new FakeCanvas2D();
        var colorScale = new ColorScale();
        colorScale.Fit(new object[] { "A", "B", "C" });

        DefaultRenderers.DrawLegend(Ctx(canvas,
            legend: new LegendConfig { Position = LegendPosition.Top },
            colorScale: colorScale));

        AssertThat(canvas.RoundRectCount).IsEqual(3); // one swatch per legend item
        AssertThat(canvas.FillCount).IsEqual(3);
        AssertThat(canvas.Texts.Contains("A")).IsTrue();
        AssertThat(canvas.Texts.Contains("B")).IsTrue();
        AssertThat(canvas.Texts.Contains("C")).IsTrue();
    }

    [TestCase]
    public void DrawLegendSkipsWithoutAColorScale()
    {
        var canvas = new FakeCanvas2D();

        DefaultRenderers.DrawLegend(Ctx(canvas,
            legend: new LegendConfig { Position = LegendPosition.Top },
            colorScale: null));

        AssertThat(canvas.DrewAnything).IsFalse();
    }

    [TestCase]
    public void DrawLegendSkipsWhenDisabled()
    {
        var canvas = new FakeCanvas2D();
        var colorScale = new ColorScale();
        colorScale.Fit(new object[] { "A", "B" });

        DefaultRenderers.DrawLegend(Ctx(canvas,
            legend: new LegendConfig { Position = LegendPosition.None },
            colorScale: colorScale));

        AssertThat(canvas.DrewAnything).IsFalse();
    }

    [TestCase]
    public void DrawLegendDimsUnfocusedItems()
    {
        var canvas = new FakeCanvas2D();
        var colorScale = new ColorScale();
        colorScale.Fit(new object[] { "A", "B" });
        var ctx = Ctx(canvas,
            legend: new LegendConfig { Position = LegendPosition.Top },
            colorScale: colorScale);
        ctx.FocusedSeries = "A";

        DefaultRenderers.DrawLegend(ctx);

        // The swatch of the unfocused item bakes the dimmed multiplier into its alpha.
        var theme = ChartTheme.Dark();
        float dimmed = theme.LegendDimmedOpacity;
        AssertThat(canvas.FillColors.Any(c => MathF.Abs(c.A - dimmed) < 1e-3f)).IsTrue();
        AssertThat(canvas.FillColors.Any(c => MathF.Abs(c.A - 1f) < 1e-3f)).IsTrue();
    }

    // ── Axis label / tick behaviour through Render ──────────────────────────

    [TestCase]
    public void LinearYAxisUsesNiceTickSteps()
    {
        var canvas = new FakeCanvas2D();
        var chart = CategoryChart(canvas);
        chart.Scale(Channel.Y, new LinearScale(0, 100));

        chart.Render();

        var labels = canvas.YAxisLabels.ToList();
        AssertThat(labels.Count > 2).IsTrue();
        AssertThat(labels).Contains("0");
        AssertThat(labels).Contains("100");
        // Nice steps of 20 (0, 20, 40, 60, 80, 100) instead of an arbitrary division.
        AssertThat(labels).Contains("20");
        AssertThat(labels).Contains("40");
    }

    [TestCase]
    public void LogYAxisIsLabelledWithPowersOfTen()
    {
        var canvas = new FakeCanvas2D();
        var chart = CategoryChart(canvas);
        chart.Scale(Channel.Y, new LogScale(1, 10000));

        chart.Render();

        var labels = canvas.YAxisLabels.ToList();
        AssertThat(labels).Contains("1");
        AssertThat(labels).Contains("10");
        AssertThat(labels).Contains("100");
        AssertThat(labels).Contains("1K");
        AssertThat(labels).Contains("10K");
        // The grid must be drawn for a log axis as well.
        AssertThat(canvas.StrokeCount > 0).IsTrue();
    }

    [TestCase]
    public void OrdinalYAxisIsLabelledWithCategoryNames()
    {
        var canvas = new FakeCanvas2D();
        var chart = new Chart(canvas);
        chart.Data(CategoryRows());
        chart.Mark(new IntervalMark { Orientation = BarOrientation.Horizontal });
        chart.Encode(Channel.X, "value");
        chart.Encode(Channel.Y, "cat");

        chart.Render();

        var labels = canvas.YAxisLabels.ToList();
        AssertThat(labels).Contains("A");
        AssertThat(labels).Contains("B");
    }

    [TestCase]
    public void TicksStayInsideAManuallyConfiguredDomain()
    {
        var canvas = new FakeCanvas2D();
        var chart = CategoryChart(canvas);
        chart.Scale(Channel.Y, new LinearScale(1, 10));

        chart.Render();

        // The nice step must be clipped to the domain: no tick at 0 (outside [1, 10]).
        var numeric = canvas.YAxisLabels
            .Select(text => double.TryParse(text, out var v) ? v : double.NaN)
            .Where(v => !double.IsNaN(v))
            .ToList();

        AssertThat(numeric.Count > 0).IsTrue();
        AssertThat(numeric.All(v => v is >= 1.0 and <= 10.0)).IsTrue();
    }

    [TestCase]
    public void DenseCategoryAxesThinTheirLabelsInsteadOfTruncatingThem()
    {
        // 60 categories in a narrow plot: drawing (and truncating) a label per tick produced an
        // unreadable smear, so only every n-th label is drawn - whole, never cut down to "09:3…".
        var canvas = new FakeCanvas2D();
        var chart = new Chart(canvas) { Width = 900f, Height = 300f };
        var rows = new List<DataRow>();
        for (int i = 0; i < 60; i++)
            rows.Add(TestContexts.Row(("t", $"09:{i:D2}:00"), ("value", 10.0 + i % 7)));
        chart.Data(rows);
        chart.Mark(new PointMark());
        chart.Encode(Channel.X, "t");
        chart.Encode(Channel.Y, "value");

        chart.Render();

        var labels = canvas.TextDraws.Where(d => d.Text.Contains(':')).Select(d => d.Text).ToList();
        AssertThat(labels.Count > 0).IsTrue();
        AssertThat(labels.Count < 20).IsTrue();                    // thinned, not one per category
        AssertThat(labels.All(text => !text.Contains('…'))).IsTrue();  // and drawn whole
    }

    [TestCase]
    public void AShortPlotWithManyRowsThinsItsRowLabels()
    {
        var canvas = new FakeCanvas2D();
        var chart = new Chart(canvas) { Width = 400f, Height = 140f };
        var rows = new List<DataRow>();
        for (int i = 0; i < 24; i++)
            rows.Add(TestContexts.Row(("row", $"r{i}"), ("value", 10.0 + i)));
        chart.Data(rows);
        chart.Mark(new IntervalMark());
        chart.Encode(Channel.Y, "row");
        chart.Encode(Channel.X, "value");

        chart.Render();

        var labels = canvas.YAxisLabels.ToList();
        AssertThat(labels.Count > 0).IsTrue();
        AssertThat(labels.Count < 24).IsTrue();
    }

    /// <summary>
    /// <see cref="DefaultRenderers.DrawCrosshair"/>, the slot's default: the theme's crosshair through the
    /// pointer when there is one, and nothing at all when there is not - which is what makes it safe to leave
    /// in the slot.
    /// </summary>
    [TestCase]
    public void TheDefaultCrosshairFollowsThePointerAndDrawsNothingWithoutIt()
    {
        var canvas = new FakeCanvas2D();
        DefaultRenderers.DrawCrosshair(Ctx(canvas, mouse: new Vector2(200f, 120f)));

        AssertThat(canvas.Lines.Count).IsGreater(0);                     // the two crosshair lines
        AssertThat(canvas.Lines[0].Color).IsEqual(ChartTheme.Dark().CrosshairColor);
        AssertThat(canvas.LineWidths).Contains(ChartTheme.Dark().CrosshairStrokeWidth);

        var plain = new FakeCanvas2D();
        DefaultRenderers.DrawCrosshair(Ctx(plain));
        AssertThat(plain.Lines.Count).IsEqual(0);
    }
}
