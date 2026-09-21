namespace GodotNodeExtension.Tests.GodotChart;

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
/// Tests for the legend geometry of <see cref="Chart"/> and <c>LegendLayoutHelper</c>
/// (RenderContext.cs): how a horizontal legend wraps into rows and reserves its real height, that
/// a vertical legend stays in one column, that a side legend reserves horizontal plot space, that
/// the layout is cached across frames and follows the <see cref="LegendConfig"/> values, and that a
/// chart without a legend renderer builds no legend layout and keeps no phantom hit region.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class LegendLayoutTest
{
    private static DataRow D(params (string Field, object Value)[] fields)
    {
        var row = new DataRow(fields.Length);
        foreach (var (field, value) in fields)
            row.Set(field, value);
        return row;
    }

    private static List<DataRow> Series(int seriesCount)
    {
        var rows = new List<DataRow>();
        for (int s = 0; s < seriesCount; s++)
        {
            for (int c = 0; c < 3; c++)
                rows.Add(D(("cat", $"C{c}"), ("value", 10.0 + s + c), ("series", $"series-{s}")));
        }
        return rows;
    }

    private static Chart LegendChart(FakeCanvas2D canvas, int seriesCount, LegendPosition position,
                                     float width = 400f)
    {
        var chart = BuildLegendChart(canvas, seriesCount, width,
            new LegendConfig { Position = position });
        return chart;
    }

    /// <summary>Legend chart sharing the configured <see cref="LegendConfig"/> instance with the caller.</summary>
    private static Chart LegendChart(FakeCanvas2D canvas, int seriesCount, LegendConfig config,
                                     float width = 400f)
    {
        return BuildLegendChart(canvas, seriesCount, width, config);
    }

    private static Chart BuildLegendChart(FakeCanvas2D canvas, int seriesCount, float width,
                                          LegendConfig config)
    {
        var chart = new Chart(canvas) { Width = width, Height = 300f };
        chart.Data(Series(seriesCount));
        chart.Mark(new PointMark());
        chart.Encode(Channel.X, "cat");
        chart.Encode(Channel.Y, "value");
        chart.Encode(Channel.Color, "series");
        chart.Legend(config);
        return chart;
    }

    /// <summary>
    /// The legend geometry the chart computed while rendering, read through the legend renderer slot -
    /// exactly the <see cref="RenderContext.LegendLayout"/> a custom legend renderer receives (see
    /// <c>Example/GodotChart/ChartRendererDemo.cs</c>). <see cref="Chart"/> deliberately exposes no
    /// legend geometry of its own: the pipeline and the legend hit test are its only readers.
    /// </summary>
    private static LegendLayout? LegendGeometryOf(Chart chart)
    {
        LegendLayout? captured = null;
        chart.LegendRenderer = ctx => captured = ctx.LegendLayout;
        chart.Render();
        return captured;
    }

    // ── Horizontal legend wrapping ──────────────────────────────────────────

    [TestCase]
    public void HorizontalLegendWrapsIntoRowsInsteadOfRunningOffTheChart()
    {
        var canvas = new FakeCanvas2D();
        var chart = LegendChart(canvas, seriesCount: 8, LegendPosition.Bottom, width: 320f);

        var layout = LegendGeometryOf(chart);
        AssertThat(layout is not null).IsTrue();

        var items = layout!.Value.Items;
        AssertThat(items.Count).IsEqual(8);

        float maxRight = items.Max(i => i.X + i.Width);
        AssertThat(maxRight <= chart.Width + 1f).IsTrue();

        // More than one distinct row Y means the legend actually wrapped.
        int rowCount = items.Select(i => MathF.Round(i.Y, 1)).Distinct().Count();
        AssertThat(rowCount > 1).IsTrue();
    }

    [TestCase]
    public void WrappedLegendReservesItsRealHeight()
    {
        var canvas = new FakeCanvas2D();
        var chart = LegendChart(canvas, seriesCount: 8, LegendPosition.Bottom, width: 320f);
        var layout = LegendGeometryOf(chart)!.Value;
        float plotBottom = chart.CurrentPlotArea!.Value.Y + chart.CurrentPlotArea.Value.Height;

        // The legend starts below the reserved area, i.e. the plot does not overlap it.
        float legendTop = layout.Items.Min(i => i.Y);
        AssertThat(legendTop >= plotBottom).IsTrue();
    }

    [TestCase]
    public void SingleRowLegendStaysOnOneRow()
    {
        var canvas = new FakeCanvas2D();
        var chart = LegendChart(canvas, seriesCount: 2, LegendPosition.Top, width: 900f);

        var items = LegendGeometryOf(chart)!.Value.Items;
        AssertThat(items.Select(i => MathF.Round(i.Y, 1)).Distinct().Count()).IsEqual(1);
    }

    [TestCase]
    public void LegendLayoutIsNotRecomputedEveryFrame()
    {
        // Compare against the same chart without a legend: after the first frame the legend must not
        // add any per-frame text measurement (it must reuse its cached layout).
        var withLegend = new FakeCanvas2D();
        var chartA = LegendChart(withLegend, seriesCount: 4, LegendPosition.Bottom);
        chartA.Render();
        int baseA = withLegend.MeasureTextCallCount;
        chartA.Render();
        int steadyA = withLegend.MeasureTextCallCount - baseA;

        var noLegend = new FakeCanvas2D();
        var chartB = LegendChart(noLegend, seriesCount: 4, LegendPosition.None);
        chartB.Render();
        int baseB = noLegend.MeasureTextCallCount;
        chartB.Render();
        int steadyB = noLegend.MeasureTextCallCount - baseB;

        AssertThat(steadyB > 0).IsTrue();           // axis labels are measured while drawing
        AssertThat(steadyA).IsEqual(steadyB);       // the legend adds nothing per frame
    }

    [TestCase]
    public void VerticalLegendIsUnaffectedByWrapping()
    {
        var canvas = new FakeCanvas2D();
        var chart = LegendChart(canvas, seriesCount: 4, LegendPosition.Right);

        var items = LegendGeometryOf(chart)!.Value.Items;
        AssertThat(items.Count).IsEqual(4);
        AssertThat(items.Select(i => MathF.Round(i.X, 1)).Distinct().Count()).IsEqual(1);
    }

    // ── Side legends reserve plot space ─────────────────────────────────────

    [TestCase]
    public void LeftLegendReservesHorizontalSpace()
    {
        var canvas = new FakeCanvas2D();
        var chart = LegendChart(canvas, seriesCount: 4, LegendPosition.Left);

        chart.Render();

        // The plot starts further right than the plain PaddingLeft (50) to make room for the legend.
        AssertThat(chart.CurrentPlotArea!.Value.X > ChartDefaults.PaddingLeft).IsTrue();
    }

    [TestCase]
    public void RightLegendReservesHorizontalSpace()
    {
        var canvas = new FakeCanvas2D();
        var chart = LegendChart(canvas, seriesCount: 4, LegendPosition.Right);

        chart.Render();

        var plot = chart.CurrentPlotArea!.Value;
        AssertThat(plot.X + plot.Width < chart.Width - ChartDefaults.PaddingRight).IsTrue();
    }

    /// <summary>
    /// The width a side legend reserves is the width it really occupies: the pre-plot estimate cannot
    /// know theme metrics such as <see cref="ChartTheme.LegendSwatchTextGap"/>, so a wide swatch gap
    /// used to push the labels past the chart edge. The reservation is refined with the width of the
    /// computed layout (plus its padding and the offset it is drawn at).
    /// </summary>
    [TestCase]
    public void ASideLegendReservesTheWidthItActuallyUses()
    {
        var canvas = new FakeCanvas2D();
        var theme = ChartTheme.Dark();
        theme.LegendSwatchTextGap = 40f;   // far wider than the estimate's swatch-to-text allowance
        var config = new LegendConfig { Position = LegendPosition.Right };
        var chart = LegendChart(canvas, seriesCount: 4, config);
        chart.Theme(theme);

        var layout = LegendGeometryOf(chart)!.Value;
        var plot = chart.CurrentPlotArea!.Value;
        float reserved = chart.Width - (plot.X + plot.Width);

        // Everything the legend occupies past the plot edge has to fit inside the reservation.
        float needed = layout.Width + config.Padding * 2f + theme.LegendRightOffset;
        AssertThat(reserved >= needed - 1f).IsTrue();
    }

    // ── The layout cache follows the LegendConfig ───────────────────────────

    /// <summary>
    /// The cached legend layout is keyed on the layout-relevant values of the config, not on the
    /// config instance: <see cref="LegendConfig"/> is normally the same object throughout (an
    /// inspector edit or <c>cfg.ItemSpacing = …</c>), so the reference alone kept the item positions
    /// - and the legend's click regions - of the previous spacing.
    /// </summary>
    [TestCase]
    public void ChangingTheLegendItemSpacingRelayoutsTheLegend()
    {
        var canvas = new FakeCanvas2D();
        var cfg = new LegendConfig { Position = LegendPosition.Bottom, ItemSpacing = 4f };
        // Two items in a single row: only the spacing changes, the swatch size - and with it the
        // height the plot reserves - stays. A wide chart keeps the row from wrapping.
        var chart = LegendChart(canvas, seriesCount: 2, cfg, width: 900f);

        // Read the geometry the way the legend renderer does (the renderer slot sees the layout the
        // pipeline computed), so the test does not depend on how the chart exposes it.
        LegendLayout? layout = null;
        chart.LegendRenderer = ctx => layout = ctx.LegendLayout;

        chart.Render();
        var before = layout!.Value.Items;
        AssertThat(before.Count).IsEqual(2);
        AssertThat(before.Select(i => MathF.Round(i.Y, 1)).Distinct().Count()).IsEqual(1);

        cfg.ItemSpacing = 40f;   // same instance, different value
        chart.Render();
        var after = layout!.Value.Items;

        // Same row and same item size, but the wider row is centred further left: the positions moved.
        AssertThat(MathF.Round(after[0].Y, 1)).IsEqual(MathF.Round(before[0].Y, 1));
        AssertThat(after[0].X < before[0].X).IsTrue();
    }

    /// <summary>
    /// <see cref="Chart.Legend"/> invalidates the layout as well: the config also decides how much
    /// space the plot reserves for the legend, which lives outside the legend layout cache.
    /// </summary>
    [TestCase]
    public void ReplacingTheLegendConfigInvalidatesTheLayout()
    {
        var canvas = new FakeCanvas2D();
        var chart = LegendChart(canvas, seriesCount: 3, LegendPosition.Bottom);
        chart.Render();
        int before = chart.EffectiveLayoutVersion;

        chart.Legend(new LegendConfig { Position = LegendPosition.Bottom, Padding = 20f });

        AssertThat(chart.EffectiveLayoutVersion != before).IsTrue();
    }

    // ── No legend renderer → no layout, no phantom hits ─────────────────────

    [TestCase]
    public void WithoutALegendRendererNoLegendLayoutIsBuilt()
    {
        var canvas = new FakeCanvas2D();
        var chart = LegendChart(canvas, seriesCount: 4, LegendPosition.Bottom);
        chart.LegendRenderer = null;   // external legend UI

        // The background slot always runs, so reading the context through it shows what the pipeline
        // handed to the renderers: with no legend renderer there is no layout to hand over at all.
        LegendLayout? handedToRenderers = null;
        chart.BackgroundRenderer = ctx => handedToRenderers = ctx.LegendLayout;

        chart.Render();

        AssertThat(handedToRenderers is null).IsTrue();

        // And clicking where the legend would have been must not report a legend hit.
        var plot = chart.CurrentPlotArea!.Value;
        var hit = chart.HitTest(new Vector2(plot.X + plot.Width / 2f, chart.Height - 5f));
        AssertThat(hit?.MarkType).IsNotEqual("Legend");
    }
}
