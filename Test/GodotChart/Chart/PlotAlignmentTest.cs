namespace GodotNodeExtension.Tests.GodotChart;

using System;
using System.Linq;
using System.Collections.Generic;
using GdUnit4;
using Godot;
using GodotNodeExtension.Component.GodotChart;
using GodotNodeExtension.Component.GodotChart.Canvas;
using GodotNodeExtension.Component.GodotChart.Marks;
using Support;
using static GdUnit4.Assertions;

/// <summary>
/// The shape and the placement of the box the content is laid out in:
/// <see cref="Chart.PlotAspectRatio"/>, <see cref="Chart.PlotAlignHorizontal"/> and
/// <see cref="Chart.PlotAlignVertical"/>, which is what <see cref="Chart.CurrentPlotArea"/> reports - plus
/// <see cref="Chart.DrawnBounds"/>, the rectangle the whole chart used (decorations included), and the layout
/// state a renderer slot receives (<see cref="RenderContext"/>, the class the <c>PlotAlignmentTest</c> also
/// covers).
/// <para>
/// A mark that draws from the plot's short edge (a pie's radius is <c>min(width, height) / 2</c> times a
/// factor) wastes the long one; the charts here are built on canvases that are deliberately not square, so
/// every number below is a hand-checkable one. <c>AutoPadding</c> is off and there is no title and no legend,
/// which makes the unshaped plot area exactly <c>(PaddingLeft, PaddingTop, W - PaddingLeft - PaddingRight,
/// H - PaddingTop - PaddingBottom)</c>.
/// </para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class PlotAlignmentTest
{
    /// <summary>Two categories, enough for a bar or a pie to draw something.</summary>
    private static List<DataRow> Rows() =>
    [
        TestContexts.Row(("cat", "A"), ("value", 10.0)),
        TestContexts.Row(("cat", "B"), ("value", 20.0)),
    ];

    /// <summary>A chart with one mark, no decorations and AutoPadding off, rendered once.</summary>
    private static Chart Build(FakeCanvas2D canvas, Mark mark, float width, float height)
    {
        var chart = new Chart(canvas) { Width = width, Height = height, AutoPadding = false };
        chart.Data(Rows());
        chart.Mark(mark);
        chart.Encode(Channel.X, "cat");
        chart.Encode(Channel.Y, "value");
        chart.Render();
        return chart;
    }

    /// <summary>The plot rectangle the frame used, asserting the chart laid one out.</summary>
    private static PlotArea Plot(Chart chart)
    {
        AssertThat(chart.CurrentPlotArea is not null).IsTrue();
        return chart.CurrentPlotArea!.Value;
    }

    /// <summary>Floating point comparison: the rectangles come out of measured layout numbers.</summary>
    private static void Approx(float actual, float expected)
        => AssertThat(MathF.Abs(actual - expected) <= 0.01f).IsTrue();

    /// <summary>The four numbers of a rectangle, compared with the shared tolerance.</summary>
    private static void ApproxRect(Rect2 rect, float x, float y, float width, float height)
    {
        Approx(rect.Position.X, x);
        Approx(rect.Position.Y, y);
        Approx(rect.Size.X, width);
        Approx(rect.Size.Y, height);
    }

    private static void ApproxArea(PlotArea area, float x, float y, float width, float height)
    {
        Approx(area.X, x);
        Approx(area.Y, y);
        Approx(area.Width, width);
        Approx(area.Height, height);
    }

    // ── Nothing shapes the content ──────────────────────────────────────────

    /// <summary>
    /// A mark that fills the plot (<see cref="Mark.PreferredAspectRatio"/> null, the default) keeps the whole
    /// of it: the content box is the plot area itself, and saying so explicitly ("fill") changes nothing.
    /// </summary>
    [TestCase]
    public void AFillingMarkKeepsTheWholePlotArea()
    {
        var automatic = Plot(Build(new FakeCanvas2D(), new IntervalMark(), 400f, 300f));
        var forced = new Chart(new FakeCanvas2D()) { Width = 400f, Height = 300f, AutoPadding = false };
        forced.PlotAspectRatio = 0f;        // 0 = fill, over what the marks might ask for
        forced.Data(Rows());
        forced.Mark(new IntervalMark());
        forced.Encode(Channel.X, "cat");
        forced.Encode(Channel.Y, "value");
        forced.Render();

        // 400 - 50 - 20 = 330 wide, 300 - 20 - 40 = 240 tall, at (50, 20).
        ApproxArea(automatic, 50f, 20f, 330f, 240f);
        ApproxArea(Plot(forced), automatic.X, automatic.Y, automatic.Width, automatic.Height);
    }

    // ── The marks' own shape ────────────────────────────────────────────────

    /// <summary>
    /// The headline case: a pie on a wide canvas asks for a square content box, so the chart stops laying the
    /// mark out in a rectangle it can only use a fraction of. Centred, the circle is where it always was - the
    /// box around it is what changed.
    /// </summary>
    [TestCase]
    public void APieGetsASquareContentBox()
    {
        var chart = Build(new FakeCanvas2D(), new PieMark(), 400f, 300f);

        // min(330, 240) = 240, so the square is 240 x 240, centred in the 330 x 240 plot area.
        ApproxArea(Plot(chart), 50f + (330f - 240f) / 2f, 20f, 240f, 240f);
    }

    /// <summary>
    /// One mark that fills the rectangle is reason enough to keep it: a chart mixing a pie with a line keeps
    /// the whole plot area, because the line needs it.
    /// </summary>
    [TestCase]
    public void OneFillingMarkKeepsTheWholePlotArea()
    {
        var canvas = new FakeCanvas2D();
        var chart = new Chart(canvas) { Width = 400f, Height = 300f, AutoPadding = false };
        chart.Data(Rows());
        chart.Mark(new PieMark());
        chart.Mark(new LineMark());
        chart.Encode(Channel.X, "cat");
        chart.Encode(Channel.Y, "value");
        chart.Render();

        ApproxArea(Plot(chart), 50f, 20f, 330f, 240f);
    }

    /// <summary>
    /// The host's word wins over the marks': a pie in a canvas the page wants filled keeps the whole plot area.
    /// </summary>
    [TestCase]
    public void TheHostCanForceTheContentToFillThePlot()
    {
        var canvas = new FakeCanvas2D();
        var chart = new Chart(canvas) { Width = 400f, Height = 300f, AutoPadding = false, PlotAspectRatio = 0f };
        chart.Data(Rows());
        chart.Mark(new PieMark());
        chart.Encode(Channel.X, "cat");
        chart.Encode(Channel.Y, "value");
        chart.Render();

        ApproxArea(Plot(chart), 50f, 20f, 330f, 240f);
    }

    // ── The shape the host asks for ─────────────────────────────────────────

    /// <summary>An aspect of 2 on a plot area that is wide already takes the full width and half the height.</summary>
    [TestCase]
    public void AHalfTheHeightAspectTakesTheFullWidth()
    {
        var canvas = new FakeCanvas2D();
        var chart = new Chart(canvas) { Width = 400f, Height = 300f, AutoPadding = false, PlotAspectRatio = 2f };
        chart.Data(Rows());
        chart.Mark(new IntervalMark());
        chart.Encode(Channel.X, "cat");
        chart.Encode(Channel.Y, "value");
        chart.Render();

        // The plot area is 330 x 240: 330 wide is 2:1 at 165 tall, which fits, so the width decides.
        ApproxArea(Plot(chart), 50f, 20f + (240f - 165f) / 2f, 330f, 165f);
    }

    /// <summary>... and on a short canvas the height is what limits the same ratio.</summary>
    [TestCase]
    public void AHalfTheHeightAspectIsLimitedByTheHeightOnAShortCanvas()
    {
        var canvas = new FakeCanvas2D();
        var chart = new Chart(canvas) { Width = 400f, Height = 160f, AutoPadding = false, PlotAspectRatio = 2f };
        chart.Data(Rows());
        chart.Mark(new IntervalMark());
        chart.Encode(Channel.X, "cat");
        chart.Encode(Channel.Y, "value");
        chart.Render();

        // The plot area is 330 x 100: 2:1 inside it is 200 x 100, centred horizontally.
        ApproxArea(Plot(chart), 50f + (330f - 200f) / 2f, 20f, 200f, 100f);
    }

    // ── Alignment ───────────────────────────────────────────────────────────

    /// <summary>
    /// Left / centre / right move the square inside a wide plot area; the vertical slack is zero there, so the
    /// three vertical alignments cannot be told apart (the tall case below covers them).
    /// </summary>
    [TestCase]
    public void TheHorizontalAlignmentPlacesTheContentBox()
    {
        // 330 x 240 plot area, 240 x 240 content box: 90 px of slack to the side.
        foreach (var (align, expectedX) in new (HorizontalAlignment, float)[]
                 {
                     (HorizontalAlignment.Left, 50f),
                     (HorizontalAlignment.Center, 95f),
                     (HorizontalAlignment.Right, 140f),
                 })
        {
            var canvas = new FakeCanvas2D();
            var chart = new Chart(canvas)
            {
                Width = 400f,
                Height = 300f,
                AutoPadding = false,
                PlotAspectRatio = 1f,
                PlotAlignHorizontal = align,
            };
            chart.Data(Rows());
            chart.Mark(new PieMark());
            chart.Encode(Channel.X, "cat");
            chart.Encode(Channel.Y, "value");
            chart.Render();

            var plot = Plot(chart);
            Approx(plot.X, expectedX);
            Approx(plot.Width, 240f);
            Approx(plot.Y, 20f);
        }
    }

    /// <summary>Top / centre / bottom on a tall canvas, where the vertical slack is the one that exists.</summary>
    [TestCase]
    public void TheVerticalAlignmentPlacesTheContentBox()
    {
        // 300 - 50 - 20 = 230 wide, 500 - 20 - 40 = 440 tall; the square is 230 x 230, 210 px of slack.
        foreach (var (align, expectedY) in new (VerticalAlignment, float)[]
                 {
                     (VerticalAlignment.Top, 20f),
                     (VerticalAlignment.Center, 125f),
                     (VerticalAlignment.Bottom, 230f),
                 })
        {
            var canvas = new FakeCanvas2D();
            var chart = new Chart(canvas)
            {
                Width = 300f,
                Height = 500f,
                AutoPadding = false,
                PlotAspectRatio = 1f,
                PlotAlignVertical = align,
            };
            chart.Data(Rows());
            chart.Mark(new PieMark());
            chart.Encode(Channel.X, "cat");
            chart.Encode(Channel.Y, "value");
            chart.Render();

            var plot = Plot(chart);
            Approx(plot.Y, expectedY);
            Approx(plot.Height, 230f);
            Approx(plot.X, 50f + (230f - 230f) / 2f);
        }
    }

    /// <summary>
    /// The legend keeps the width of the whole plot area, not of the content box: it wraps to the width it is
    /// given, so a square content box would break a one-row legend into four.
    /// </summary>
    [TestCase]
    public void TheLegendStillWrapsToTheWholePlotArea()
    {
        var canvas = new FakeCanvas2D();
        var chart = new Chart(canvas)
        {
            Width = 400f,
            Height = 300f,
            AutoPadding = false,
            PlotAspectRatio = 1f,
        };
        chart.Data(
        [
            TestContexts.Row(("cat", "A"), ("value", 10.0), ("series", "north")),
            TestContexts.Row(("cat", "B"), ("value", 20.0), ("series", "south")),
        ]);
        chart.Mark(new PieMark());
        chart.Encode(Channel.X, "cat");
        chart.Encode(Channel.Y, "value");
        chart.Encode(Channel.Color, "series");
        chart.Legend(new LegendConfig { Position = LegendPosition.Bottom });
        chart.Render();

        // The chart exposes no legend geometry of its own, so it is read through the legend renderer slot -
        // exactly what a custom legend renderer receives (see LegendLayoutTest).
        LegendLayout? layout = null;
        chart.LegendRenderer = ctx => layout = ctx.LegendLayout;
        chart.Render();

        AssertThat(layout is not null).IsTrue();
        // Both items on one row (one distinct Y): the legend had the 330 px of the plot area to fit them, not
        // the 240 px of the content box.
        AssertThat(layout!.Value.Items.Select(i => MathF.Round(i.Y, 1)).Distinct().Count()).IsEqual(1);
        AssertThat(layout.Value.Items.Count).IsEqual(2);
    }

    // ── How much of the canvas the chart used ───────────────────────────────

    /// <summary>
    /// <see cref="Chart.DrawnBounds"/> is the content box plus every decoration band the layout reserved, and it
    /// follows the decorations: a chart with nothing around it reports its content box, a title reserves a band
    /// at the top, a bottom legend a band below, and the axis labels their columns on the sides.
    /// </summary>
    [TestCase]
    public void DrawnBoundsFollowsTheDecorations()
    {
        // Nothing around the marks: for a pie the content box is all the chart needs - the marks are clipped to
        // it (Chart.Render clips the mark stages to the content box), so there is no separate ink box to report.
        var bare = Build(new FakeCanvas2D(), new PieMark(), 400f, 300f);
        var barePlot = Plot(bare);
        AssertThat(bare.DrawnBounds is not null).IsTrue();
        ApproxRect(bare.DrawnBounds!.Value, barePlot.X, barePlot.Y, barePlot.Width, barePlot.Height);

        // A title reserves a band at the top of the node...
        var titled = new Chart(new FakeCanvas2D()) { Width = 400f, Height = 300f, AutoPadding = false, Title = "Revenue" };
        titled.Data(Rows());
        titled.Mark(new PieMark());
        titled.Encode(Channel.X, "cat");
        titled.Encode(Channel.Y, "value");
        titled.Render();

        var titledBounds = titled.DrawnBounds!.Value;
        Approx(titledBounds.Position.Y, 0f);
        Approx(titledBounds.Size.Y, Plot(titled).Height + ChartDefaults.PaddingTop + ChartTheme.Dark().TitleReservedHeight);

        // ... and a bottom legend a band below, so the box grows in height while the content box shrinks.
        var withLegend = new Chart(new FakeCanvas2D()) { Width = 400f, Height = 300f, AutoPadding = false };
        withLegend.Data(
        [
            TestContexts.Row(("cat", "A"), ("value", 10.0), ("series", "north")),
            TestContexts.Row(("cat", "B"), ("value", 20.0), ("series", "south")),
        ]);
        withLegend.Mark(new PieMark());
        withLegend.Encode(Channel.X, "cat");
        withLegend.Encode(Channel.Y, "value");
        withLegend.Encode(Channel.Color, "series");
        withLegend.Legend(new LegendConfig { Position = LegendPosition.Bottom });
        withLegend.Render();

        var withoutLegend = new Chart(new FakeCanvas2D()) { Width = 400f, Height = 300f, AutoPadding = false };
        withoutLegend.Data(Rows());
        withoutLegend.Mark(new PieMark());
        withoutLegend.Encode(Channel.X, "cat");
        withoutLegend.Encode(Channel.Y, "value");
        withoutLegend.Render();

        // A legend takes its room from the content box (it is reserved out of the node), so the whole-chart
        // rectangle covers the legend instead of growing: what changes is the content box, and the proof is
        // that the rectangle reaches the legend items.
        var legendItems = LegendItemsOf(withLegend);
        AssertThat(legendItems.Count).IsEqual(2);
        AssertThat(withoutLegend.DrawnBounds!.Value.Size.Y).IsEqual(240f);
        foreach (var item in legendItems)
        {
            float itemBottom = item.Y + item.Height;
            AssertThat(withLegend.DrawnBounds!.Value.Position.Y + withLegend.DrawnBounds.Value.Size.Y
                >= itemBottom - 0.01f).IsTrue();
        }
        AssertThat(Plot(withLegend).Height).IsLess(Plot(withoutLegend).Height);

        // A Cartesian chart includes the axis bands: the Y label column reaches the node's left edge.
        var bars = Build(new FakeCanvas2D(), new IntervalMark(), 400f, 300f);
        var barPlot = Plot(bars);
        var barBounds = bars.DrawnBounds!.Value;
        Approx(barBounds.Position.X, 0f);                     // the Y label column reaches the node's left edge
        Approx(barBounds.Position.Y, barPlot.Y);              // the top padding stays empty: there is no title
        AssertThat(barBounds.Size.X >= barPlot.Width + barPlot.X).IsTrue();
        AssertThat(barBounds.Size.Y > barPlot.Height).IsTrue();
    }

    /// <summary>Before the first frame there is nothing to report.</summary>
    [TestCase]
    public void DrawnBoundsIsNullBeforeTheFirstFrame()
    {
        var chart = new Chart(new FakeCanvas2D()) { Width = 400f, Height = 300f };
        chart.Data(Rows());
        chart.Mark(new PieMark());
        chart.Encode(Channel.X, "cat");
        chart.Encode(Channel.Y, "value");

        AssertThat(chart.DrawnBounds is null).IsTrue();
        AssertThat(chart.CurrentPlotArea is null).IsTrue();

        chart.Render();
        AssertThat(chart.DrawnBounds is not null).IsTrue();
    }

    // ── What a renderer slot receives ───────────────────────────────────────

    /// <summary>
    /// A host that takes over one of the stages draws against the chart's own layout instead of measuring it
    /// again: the context carries the content box, the plot area before the content shape was applied, the
    /// rectangle the whole chart used, and the legend geometry.
    /// </summary>
    [TestCase]
    public void ARendererReceivesTheLayoutState()
    {
        var canvas = new FakeCanvas2D();
        var chart = new Chart(canvas)
        {
            Width = 400f,
            Height = 300f,
            AutoPadding = false,
            PlotAspectRatio = 1f,
            Title = "Revenue",
        };
        chart.Data(
        [
            TestContexts.Row(("cat", "A"), ("value", 10.0), ("series", "north")),
            TestContexts.Row(("cat", "B"), ("value", 20.0), ("series", "south")),
        ]);
        chart.Mark(new PieMark());
        chart.Encode(Channel.X, "cat");
        chart.Encode(Channel.Y, "value");
        chart.Encode(Channel.Color, "series");
        chart.Legend(new LegendConfig { Position = LegendPosition.Bottom });

        RenderContext? seen = null;
        // The background stage runs for every chart; the grid stage does not (a pie has no axes to draw).
        chart.BackgroundRenderer = ctx => seen = ctx;
        chart.Render();

        AssertThat(seen is not null).IsTrue();
        var context = seen!;

        // The content box is the square the shape asked for, and the full plot area is the rectangle it was
        // carved out of: the two differ exactly by the shaping.
        var plot = Plot(chart);
        AssertThat(context.Plot.Width).IsEqual(plot.Width);
        AssertThat(context.FullPlot.Width > context.Plot.Width + 1f).IsTrue();
        Approx(context.FullPlot.Height, context.Plot.Height);

        // The whole-chart rectangle and the legend geometry come with the context.
        AssertThat(context.DrawnBounds is not null).IsTrue();
        AssertThat(context.DrawnBounds!.Value.Size.Y > plot.Height).IsTrue();       // the title band
        AssertThat(context.LegendLayout is not null).IsTrue();
        AssertThat(context.LegendLayout!.Value.Items.Count).IsEqual(2);

        foreach (var item in context.LegendLayout.Value.Items)
        {
            AssertThat(item.Y > plot.Y + plot.Height).IsTrue();                      // below the content box
            AssertThat(context.DrawnBounds.Value.Size.Y >= item.Y + item.Height - chart.OffsetY).IsTrue();
        }
    }

    // ── Where the content really is ─────────────────────────────────────────

    /// <summary>
    /// The content box must not move between two redraws of the same chart. It is derived from the plot area,
    /// which is what the previous frame measured (the legend height is corrected on the frame that discovered
    /// it), so a shape that is only *almost* stable - the square a pie gets is <c>min(width, height)</c> of that
    /// area - would make the pie's circle and its label ring shift by a pixel on the second frame. The
    /// two-consecutive-frames integration case catches it on the real backend; this is the fast version.
    /// </summary>
    [TestCase]
    public void TheContentBoxIsStableAcrossRedraws()
    {
        if (!HasSceneTree()) return;

        var fake = new FakeCanvas2D();
        var view = AddView(fake, new Vector2(320f, 200f));
        try
        {
            // The same shape the integration case uses: a pie whose legend comes from the category channel.
            view.Kind = ChartKind.Pie;
            view.ColorField = "category";
            view.SetData(Categories());
            PumpFrame(view);

            AssertThat(view.Chart is not null).IsTrue();
            var first = view.Chart!.CurrentPlotArea!.Value;

            view.Repaint();
            PumpFrame(view);
            var second = view.Chart!.CurrentPlotArea!.Value;

            Approx(second.X, first.X);
            Approx(second.Y, first.Y);
            Approx(second.Width, first.Width);
            Approx(second.Height, first.Height);
        }
        finally
        {
            Release(view);
        }
    }

    /// <summary>The legend geometry the chart computed, read through the legend renderer slot.</summary>
    private static IReadOnlyList<LegendItemLayout> LegendItemsOf(Chart chart)
    {
        LegendLayout? captured = null;
        chart.LegendRenderer = ctx => captured = ctx.LegendLayout;
        chart.Render();
        return captured?.Items ?? [];
    }

    /// <summary>Three categories with a series each, so the legend has items to lay out.</summary>
    private static List<DataRow> Categories() =>
    [
        TestContexts.Row(("category", "A"), ("value", 10.0), ("series", "north")),
        TestContexts.Row(("category", "B"), ("value", 20.0), ("series", "south")),
        TestContexts.Row(("category", "C"), ("value", 15.0), ("series", "east")),
    ];

    // ── The node's exports ──────────────────────────────────────────────────
    /// <summary>
    /// A scene configures the content shape through the node (nothing here needs code): 0 leaves it to the
    /// marks, a positive value is the ratio and a negative one forces filling.
    /// </summary>
    [TestCase]
    public void TheViewExportsReachTheChart()
    {
        if (!HasSceneTree()) return;

        var view = AddView(new FakeCanvas2D(), new Vector2(400f, 300f));
        try
        {
            view.Kind = ChartKind.Pie;
            view.SetValues(new[] { ("A", 10.0), ("B", 20.0) });
            PumpFrame(view);

            // The default (0) lets the pie ask for a square.
            AssertThat(view.Chart is not null).IsTrue();
            var plot = view.Chart!.CurrentPlotArea!.Value;
            Approx(plot.Width, plot.Height);

            // A negative ratio is the explicit "fill the plot area".
            view.PlotAspectRatio = -1f;
            PumpFrame(view);
            var filled = view.Chart!.CurrentPlotArea!.Value;
            AssertThat(filled.Width > filled.Height + 1f).IsTrue();

            // ... and a positive one is the ratio, placed by the alignment exports.
            view.PlotAspectRatio = 2f;
            view.PlotAlignHorizontal = HorizontalAlignment.Left;
            PumpFrame(view);
            var shaped = view.Chart!.CurrentPlotArea!.Value;
            Approx(shaped.Width, shaped.Height * 2f);
            Approx(shaped.X, 50f);
        }
        finally
        {
            Release(view);
        }
    }

    // ── Plumbing ────────────────────────────────────────────────────────────

    private static bool HasSceneTree()
    {
        if (Engine.GetMainLoop() is SceneTree) return true;
        GD.Print("[skip] PlotAlignment tests need a SceneTree; none in this run");
        return false;
    }

    private static ChartView AddView(FakeCanvas2D fake, Vector2 size)
    {
        var view = new ChartView { Size = size, CanvasFactory = (_, _) => fake };
        ((SceneTree)Engine.GetMainLoop()).Root.AddChild(view);
        return view;
    }

    private static void PumpFrame(ChartView view, int frames = 2, float delta = 0.016f)
    {
        for (int frame = 0; frame < frames; frame++)
        {
            view._Process(delta);
            foreach (var child in view.GetChildren(includeInternal: true))
                if (child is Canvas2DControl surface) surface._Process(delta);
        }
    }

    private static void Release(ChartView view)
    {
        try
        {
            view.GetParent()?.RemoveChild(view);
        }
        finally
        {
            view.Free();
        }
    }
}
