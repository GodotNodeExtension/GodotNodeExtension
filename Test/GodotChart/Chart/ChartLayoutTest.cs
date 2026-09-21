namespace GodotNodeExtension.Tests.GodotChart;

using System;
using System.Collections.Generic;
using System.Linq;
using GdUnit4;
using GodotNodeExtension.Component.GodotChart;
using GodotNodeExtension.Component.GodotChart.Canvas;
using GodotNodeExtension.Component.GodotChart.Marks;
using Support;
using static GdUnit4.Assertions;

/// <summary>
/// Tests for the plot geometry <see cref="Chart"/> computes each frame: the title pushing the plot
/// down, the X axis title compressing its height, and the secondary Y axis reserving space on the
/// right.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class ChartLayoutTest
{
    private static DataRow D(params (string Field, object Value)[] fields)
    {
        var row = new DataRow(fields.Length);
        foreach (var (field, value) in fields)
            row.Set(field, value);
        return row;
    }

    private static List<DataRow> Bars() =>
    [
        D(("cat", "A"), ("value", 10.0)),
        D(("cat", "B"), ("value", 20.0)),
    ];

    private static Chart Cartesian(FakeCanvas2D canvas, Mark mark, List<DataRow>? data = null)
    {
        var chart = new Chart(canvas) { Width = 400f, Height = 300f };
        chart.Data(data ?? Bars());
        chart.Mark(mark);
        chart.Encode(Channel.X, "cat");
        chart.Encode(Channel.Y, "value");
        return chart;
    }

    private static void Approx(float actual, float expected, float tolerance = 1e-3f)
        => AssertThat(MathF.Abs(actual - expected) <= tolerance).IsTrue();

    [TestCase]
    public void TitlePushesThePlotDown()
    {
        float PlotYFor(string? title)
        {
            var canvas = new FakeCanvas2D();
            var probe = new CapturingMark();
            var chart = Cartesian(canvas, probe);
            chart.Title = title;
            chart.Render();
            return probe.LastPlot!.Value.Y;
        }

        float without = PlotYFor(null);
        float with = PlotYFor("Revenue");

        // The theme reserves 24px for the title area.
        Approx(with - without, ChartTheme.Dark().TitleReservedHeight);
    }

    [TestCase]
    public void XAxisTitleCompressesThePlotHeight()
    {
        float PlotHeight(bool withTitle)
        {
            var canvas = new FakeCanvas2D();
            var probe = new CapturingMark();
            var chart = Cartesian(canvas, probe);
            if (withTitle) chart.XAxis(new AxisConfig { Title = "Time" });
            chart.Render();
            return probe.LastPlot!.Value.Height;
        }

        float plain = PlotHeight(false);
        float titled = PlotHeight(true);

        // One label line (13 * 1.2) plus the axis-title margin (6).
        var theme = ChartTheme.Dark();
        float expected = theme.LabelFontSize * FontSettings.Default.LineHeightMultiplier
                       + theme.AxisTitleMargin;
        Approx(plain - titled, expected);
    }

    [TestCase]
    public void Y2ScaleReservesRightSpace()
    {
        float PlotWidth(bool withY2)
        {
            var canvas = new FakeCanvas2D();
            var chart = Cartesian(canvas, new IntervalMark());
            if (withY2) chart.Scale(Channel.Y2, new LinearScale(0, 1));
            chart.Render();
            return chart.CurrentPlotArea!.Value.Width;
        }

        float plain = PlotWidth(false);
        float y2 = PlotWidth(true);

        Approx(plain - y2, ChartTheme.Dark().Y2LabelReservedWidth);
    }

    /// <summary>
    /// The renderer slots share one tick set per axis inside a frame (the grid and the label pass ask for the
    /// same axes), so the cache has to belong to the frame: a later frame that draws another axis must not be
    /// served the tick set of the one before.
    /// </summary>
    [TestCase]
    public void TheTickSetsOfOneFrameDoNotOutliveIt()
    {
        var canvas = new FakeCanvas2D();
        var chart = Cartesian(canvas, new IntervalMark());
        chart.YAxis(new AxisConfig { Ticks = [2, 4, 6] });
        chart.Render();

        int drawn = canvas.TextDraws.Count;
        chart.YAxis(new AxisConfig { Ticks = [12, 14, 16] });
        chart.Render();

        var secondFrame = canvas.TextDraws.Skip(drawn)
            .Where(d => d.Align == TextAlign.Right).Select(d => d.Text).ToList();
        AssertThat(secondFrame.Contains("12")).IsTrue();
        AssertThat(secondFrame.Contains("14")).IsTrue();
        AssertThat(secondFrame.Contains("2")).IsFalse();    // the first frame's tick set is gone
    }

    /// <summary>
    /// A legend on the right is drawn <see cref="ChartTheme.LegendRightOffset"/> past the plot edge
    /// (plus its own padding), so the plot has to give up that offset as well - the reservation used to
    /// miss it and the labels hung over the chart edge.
    /// </summary>
    [TestCase]
    public void ARightLegendReservesTheOffsetItIsDrawnAt()
    {
        var theme = ChartTheme.Dark();
        var canvas = new FakeCanvas2D();
        var chart = Cartesian(canvas, new IntervalMark(),
        [
            D(("cat", "A"), ("value", 10.0), ("series", "north")),
            D(("cat", "B"), ("value", 20.0), ("series", "north")),
            D(("cat", "C"), ("value", 15.0), ("series", "south")),
        ]);
        chart.Theme(theme);
        chart.Encode(Channel.Color, "series");
        var legend = new LegendConfig { Position = LegendPosition.Right };
        chart.Legend(legend);

        chart.Render();

        // What the drawing side asks for: the legend's own width (swatch + widest label) plus the
        // offset it starts at past the plot edge.
        var scale = new ColorScale();
        scale.Fit(new object[] { "north", "south" });
        float needed = LegendLayoutHelper.EstimateWidth(legend, scale, canvas) + theme.LegendRightOffset;

        var plot = chart.CurrentPlotArea!.Value;
        float reserved = chart.Width - (plot.X + plot.Width);

        AssertThat(reserved >= needed - 1e-3f).IsTrue();
    }

    /// <summary>
    /// The auto-padding label-width cache is keyed by channel *and* layout version. With a single
    /// shared version the channel measured second in a frame (Y first, then Y2, exactly the order
    /// <c>Render</c> uses) was served its entry from the previous layout, so the plot was padded for
    /// a width that no longer matched the labels.
    /// </summary>
    [TestCase]
    public void LabelWidthCacheInvalidatesEachChannelOnItsOwn()
    {
        var canvas = new FakeCanvas2D();
        var theme = ChartTheme.Dark();
        var chart = new Chart(canvas) { Width = 400f, Height = 300f };
        chart.Theme(theme);
        var narrow = new LinearScale(0, 10);
        chart.Scale(Channel.Y, new LinearScale(0, 10));
        chart.Scale(Channel.Y2, narrow);

        // Prime both channels at the same layout version.
        chart.MeasureAxisLabelWidth(Channel.Y, TextAlign.Right);
        chart.MeasureAxisLabelWidth(Channel.Y2, TextAlign.Left);

        // Installing a new scale bumps the layout version; the new labels are much wider.
        var wide = new LinearScale(0, 1_000_000);
        chart.Scale(Channel.Y2, wide);

        // Y is measured first, which (with the bug) marked the shared cache as fresh again.
        chart.MeasureAxisLabelWidth(Channel.Y, TextAlign.Right);
        float measured = chart.MeasureAxisLabelWidth(Channel.Y2, TextAlign.Left);

        float expectedNarrow = ExpectedLabelWidth(canvas, theme, narrow);
        float expectedWide = ExpectedLabelWidth(canvas, theme, wide);
        AssertThat(expectedWide > expectedNarrow + 1f).IsTrue(); // the change is observable at all

        Approx(measured, expectedWide);
    }

    /// <summary>
    /// Width <see cref="Chart.MeasureAxisLabelWidth"/> is expected to report for a scale: the widest
    /// tick label plus the gap to the plot edge and the label margin
    /// (<see cref="DefaultRenderers.AxisLabelReservedWidth"/>).
    /// </summary>
    private static float ExpectedLabelWidth(FakeCanvas2D canvas, ChartTheme theme, IScale scale)
    {
        var font = new FontSettings
        {
            Size = theme.LabelFontSize > 0f ? theme.LabelFontSize : FontSettings.Default.Size,
            Family = theme.FontFamily,
            GodotFont = theme.Font,
            Align = TextAlign.Left,
        };
        float widest = 0f;
        foreach (var (_, text) in DefaultRenderers.ComputeTicks(
                     scale, fallbackTickCount: theme.FallbackTickCount))
            widest = MathF.Max(widest, canvas.MeasureText(text, font).Width);
        return DefaultRenderers.AxisLabelReservedWidth(theme, widest);
    }
}
