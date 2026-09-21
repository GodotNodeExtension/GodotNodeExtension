namespace GodotNodeExtension.Tests.GodotChart;

using System.Collections.Generic;
using GdUnit4;
using Godot;
using GodotNodeExtension.Component.GodotChart;
using static GdUnit4.Assertions;

/// <summary>
/// Where the chart says an element is versus where it is actually painted, on the engine's real backend.
/// The unit cases hit-test against a fake canvas, so a systematic offset between the drawing and the hit
/// region - the classic "tooltip appears one bar to the left" bug - could not show up there.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class ChartHitTestMappingIntegrationTest
{
    private static readonly Vector2 ViewSize = new(320f, 200f);

    /// <summary>The bar case: three categories, one bar each, no colour channel.</summary>
    private static ChartRenderCase Bars => ChartRenderCase.All[0];

    private static ChartView AddBarView(out ChartTheme theme)
    {
        var c = Bars;
        theme = ChartTheme.Dark().Clone();
        var view = new ChartView
        {
            Kind = c.Kind,
            XField = c.XField,
            YField = c.YField,
            ColorField = c.ColorField,
            CustomTheme = theme,
            Size = ViewSize,
        };
        ((SceneTree)Engine.GetMainLoop()).Root.AddChild(view);
        view.SetData(c.Rows);
        ChartRenderHarness.Pump(view, 2);
        return view;
    }

    /// <summary>Centre of the band of the <paramref name="index"/>-th ordinal category (the scale convention).</summary>
    private static float BandCentre(PlotArea plot, int index, int count)
        => plot.X + plot.Width * (float)((index + 0.5) / count);

    /// <summary>
    /// For every bar, the pixel at the middle of its band is the bar (not the background) and the hit test at
    /// that exact point reports the same row. One rule drives both, so a drift between them fails here.
    /// </summary>
    [TestCase]
    public void EveryBarIsHitWhereItIsPainted()
    {
        const string name = nameof(EveryBarIsHitWhereItIsPainted);
        if (ChartRenderHarness.NoRenderingDevice(name)) return;

        var view = AddBarView(out var theme);
        try
        {
            var image = ChartRenderHarness.Pixels(view);
            AssertThat(image is not null).IsTrue();
            var chart = view.Chart!;
            var plot = chart.CurrentPlotArea!.Value;
            int count = Bars.Rows.Count;
            var report = new List<string>();

            for (int i = 0; i < count; i++)
            {
                // A quarter of the way up the plot: the shortest bar of the case still covers it.
                float x = BandCentre(plot, i, count);
                float y = plot.Y + plot.Height * 0.75f;
                var point = new Vector2(x, y);

                var pixel = image!.GetPixel((int)x, (int)y);
                if (!ChartRenderHarness.IsStrongContent(pixel, theme.BackgroundColor))
                    report.Add($"row {i}: nothing painted at ({x:F0},{y:F0})");

                var hit = chart.HitTest(point);
                if (hit is null || !hit.Hit)
                    report.Add($"row {i}: no hit at ({x:F0},{y:F0})");
                else if (hit.RowIndex != i)
                    report.Add($"row {i}: hit reports row {hit.RowIndex}");
            }

            AssertThat(string.Join("; ", report)).IsEqual("");
        }
        finally
        {
            ChartRenderHarness.Release(view);
        }
    }

    /// <summary>
    /// Above the shortest bar there is nothing to hit: the plot area is painted (grid, axis lines) but no
    /// element covers it, so the hit test must not report a row there. This is the other half of the mapping
    /// rule - a hit region that is too generous is as wrong as one that is too small.
    /// </summary>
    [TestCase]
    public void TheEmptyPlotAreaAboveAShortBarHitsNothing()
    {
        const string name = nameof(TheEmptyPlotAreaAboveAShortBarHitsNothing);
        if (ChartRenderHarness.NoRenderingDevice(name)) return;

        var view = AddBarView(out var theme);
        try
        {
            var chart = view.Chart!;
            var plot = chart.CurrentPlotArea!.Value;
            int count = Bars.Rows.Count;

            // The shortest bar of the case: the value axis is fitted to the tallest one, so the top of the
            // plot is empty above this bar (the tallest one reaches it, which is why it is not probed here).
            int shortest = 0;
            double lowest = double.MaxValue;
            for (int i = 0; i < count; i++)
            {
                if (Bars.Rows[i].Get<double>(Bars.YField) is var value && value < lowest)
                {
                    lowest = value;
                    shortest = i;
                }
            }

            var top = new Vector2(BandCentre(plot, shortest, count), plot.Y + 1f);
            var image = ChartRenderHarness.Pixels(view);
            AssertThat(image is not null).IsTrue();
            AssertThat(ChartRenderHarness.IsStrongContent(image!.GetPixel((int)top.X, (int)top.Y),
                theme.BackgroundColor)).IsFalse();

            var hit = chart.HitTest(top);
            if (hit is { Hit: true } && hit.RowIndex >= 0)
                AssertThat($"row {hit.RowIndex} claims the empty top of the plot").IsEqual("");
        }
        finally
        {
            ChartRenderHarness.Release(view);
        }
    }

    /// <summary>
    /// Hovering a bar through the node's own input path settles on that bar, raises the hover event once, and
    /// paints the crosshair/tooltip overlay: the pixels change even though the data did not.
    /// </summary>
    [TestCase]
    public void HoveringABarSettlesOnItAndPaintsTheOverlay()
    {
        const string name = nameof(HoveringABarSettlesOnItAndPaintsTheOverlay);
        if (ChartRenderHarness.NoRenderingDevice(name)) return;

        var view = AddBarView(out _);
        try
        {
            var before = ChartRenderHarness.Pixels(view);
            AssertThat(before is not null).IsTrue();
            ulong beforePrint = ChartRenderHarness.PixelFingerprint(before!);

            var chart = view.Chart!;
            var plot = chart.CurrentPlotArea!.Value;
            var rows = new List<int>();
            chart.OnHover += (_, e) => rows.Add(e.RowIndex);

            int count = Bars.Rows.Count;
            const int target = 1;

            view._GuiInput(new InputEventMouseMotion
            {
                Position = new Vector2(BandCentre(plot, target, count), plot.Y + plot.Height * 0.75f),
            });
            ChartRenderHarness.Pump(view, 1);

            AssertThat(chart.CurrentHoveredRowIndex).IsEqual(target);
            AssertThat(rows.Count).IsEqual(1);
            AssertThat(rows[0]).IsEqual(target);

            var after = ChartRenderHarness.Pixels(view);
            AssertThat(after is not null).IsTrue();
            AssertThat(ChartRenderHarness.PixelFingerprint(after!)).IsNotEqual(beforePrint);
            AssertThat(view.Tooltip.IsVisible).IsTrue();
        }
        finally
        {
            ChartRenderHarness.Release(view);
        }
    }

    /// <summary>
    /// Pointer input outside the plot area (the axis bands) is reported as an axis hit rather than as a row:
    /// the axis tooltip and the data tooltip must not trade places.
    /// </summary>
    [TestCase]
    public void AxisBandsAreHitAsAxesNotAsRows()
    {
        const string name = nameof(AxisBandsAreHitAsAxesNotAsRows);
        if (ChartRenderHarness.NoRenderingDevice(name)) return;

        var c = Bars;
        var view = new ChartView
        {
            Kind = c.Kind,
            XField = c.XField,
            YField = c.YField,
            ColorField = c.ColorField,
            Size = ViewSize,
            XAxisTitle = "hour",
            YAxisTitle = "load",
        };
        ((SceneTree)Engine.GetMainLoop()).Root.AddChild(view);
        try
        {
            view.SetData(c.Rows);
            ChartRenderHarness.Pump(view, 2);

            var chart = view.Chart!;
            var plot = chart.CurrentPlotArea!.Value;

            var xAxisHit = chart.HitTest(new Vector2(plot.X + plot.Width * 0.5f, plot.Y + plot.Height + 4f));
            AssertThat(xAxisHit is not null).IsTrue();
            AssertThat(xAxisHit!.MarkType).IsEqual("XAxis");

            var yAxisHit = chart.HitTest(new Vector2(plot.X - 4f, plot.Y + plot.Height * 0.5f));
            AssertThat(yAxisHit is not null).IsTrue();
            AssertThat(yAxisHit!.MarkType).IsEqual("YAxis");
        }
        finally
        {
            ChartRenderHarness.Release(view);
        }
    }
}
