namespace GodotNodeExtension.Tests.GodotChart;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using GdUnit4;
using Godot;
using GodotNodeExtension.Component.GodotChart;
using GodotNodeExtension.Component.GodotChart.Canvas;
using GodotNodeExtension.Component.GodotChart.Marks;
using GodotNodeExtension.Tests.Support;
using Support;
using static GdUnit4.Assertions;

/// <summary>
/// <see cref="Chart.MinimumSize"/> - the size the content (title, legend, axis labels, axis titles and a
/// plot of <see cref="Chart.MinimumPlotSize"/>) actually needs - and the two things that hang off it:
/// <see cref="ChartView._GetMinimumSize"/> reporting it to the engine, and the single warning a chart
/// gives when it is drawn below it (<c>Chart.Render.cs</c>).
/// <para>
/// The point of the number is that a page no longer has to guess: a container sizes the chart from it. It
/// is an estimate that errs generous (below it the axis thins its own labels), which is why the cases here
/// check that every reserved part moves it in the right direction instead of pinning one total.
/// </para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class MinimumSizeTest
{
    /// <summary>Fragment of the warning a chart pushes when it is too small; see <c>Chart.Render.cs</c>.</summary>
    private const string TooSmall = "below the content minimum";

    /// <summary>Categories the charts in this suite are built over.</summary>
    private static List<DataRow> Categories() =>
    [
        TestContexts.Row(("category", "A"), ("value", 10.0), ("series", "north")),
        TestContexts.Row(("category", "B"), ("value", 20.0), ("series", "south")),
        TestContexts.Row(("category", "C"), ("value", 15.0), ("series", "north")),
    ];

    /// <summary>
    /// A bar chart over three categories, rendered once so its layout (and with it the minimum) is known.
    /// <paramref name="configure"/> runs before the render, the way a page configures the chart.
    /// </summary>
    private static Chart Build(FakeCanvas2D canvas, ChartTheme theme, Action<Chart>? configure = null)
    {
        var chart = new Chart(canvas) { Width = 400f, Height = 300f };
        chart.Theme(theme);
        chart.Encode(Channel.X, "category");
        chart.Encode(Channel.Y, "value");
        chart.Mark(new IntervalMark());
        chart.Data(Categories());
        configure?.Invoke(chart);
        chart.Render();
        return chart;
    }

    private static void Approx(float actual, float expected, float tolerance = 1f)
        => AssertThat(MathF.Abs(actual - expected) <= tolerance).IsTrue();

    // ── What the minimum is made of ─────────────────────────────────────────

    /// <summary>
    /// Every decoration and the plot itself are in the total, and each one of them moves it: this is what a
    /// container relies on when it lets the chart decide how much room it needs.
    /// </summary>
    [TestCase]
    public void MinimumSizeCoversEveryReservedPart()
    {
        var theme = ChartTheme.Dark();
        float lineH = theme.LabelFontSize * FontSettings.Default.LineHeightMultiplier;

        var plain = Build(new FakeCanvas2D(), theme);
        float baseX = plain.MinimumSize.X;
        float baseY = plain.MinimumSize.Y;

        // The floor: the plot plus the padding, plus whatever the labels and their titles needed.
        AssertThat(baseX >= ChartDefaults.PaddingLeft + ChartDefaults.PaddingRight + Chart.MinimumPlotSize.X)
            .IsTrue();
        AssertThat(baseY >= ChartDefaults.PaddingTop + ChartDefaults.PaddingBottom + Chart.MinimumPlotSize.Y)
            .IsTrue();

        // A title costs its own band at the top, and no width.
        var titled = Build(new FakeCanvas2D(), theme, c => c.Title = "Revenue");
        AssertThat(titled.MinimumSize.X).IsEqual(baseX);
        AssertThat(titled.MinimumSize.Y - baseY).IsGreater(theme.TitleReservedHeight - 1f);

        // An X axis title needs its line *and* a second label line (one to draw the title in, one for the
        // labels), which is what the minimum reserves for it.
        var xTitled = Build(new FakeCanvas2D(), theme, c => c.XAxis(new AxisConfig { Title = "Month" }));
        Approx(xTitled.MinimumSize.Y - baseY, theme.AxisTitleMargin + lineH * 2f);

        // A Y axis title reserves width for its band next to the label column, and no height.
        var yTitled = Build(new FakeCanvas2D(), theme, c => c.YAxis(new AxisConfig { Title = "Revenue" }));
        AssertThat(yTitled.MinimumSize.Y).IsEqual(baseY);
        AssertThat(yTitled.MinimumSize.X - baseX >= lineH + theme.AxisTitleMargin).IsTrue();

        // A legend above the plot costs height only (its own measured rows).
        var legend = Build(new FakeCanvas2D(), theme, c =>
        {
            c.Encode(Channel.Color, "series");
            c.Legend(new LegendConfig { Position = LegendPosition.Top });
        });
        AssertThat(legend.MinimumSize.X).IsEqual(baseX);
        AssertThat(legend.MinimumSize.Y - baseY).IsGreater(0f);

        // A second value axis reserves its label column on the right.
        var twoAxes = Build(new FakeCanvas2D(), theme, c => c.Scale(Channel.Y2, new LinearScale(0, 1)));
        AssertThat(twoAxes.MinimumSize.X - baseX >= theme.Y2LabelReservedWidth - 1f).IsTrue();
        AssertThat(twoAxes.MinimumSize.Y).IsEqual(baseY);
    }

    /// <summary>
    /// A bigger label font makes every measured reservation bigger (the line height of the labels and the
    /// width of the label column), so the minimum follows the theme.
    /// </summary>
    [TestCase]
    public void AThemeFontSizeChangesTheMinimum()
    {
        var small = ChartTheme.Dark();
        var large = ChartTheme.Dark();
        large.LabelFontSize = 26f;

        var compact = Build(new FakeCanvas2D(), small);
        var roomy = Build(new FakeCanvas2D(), large);

        AssertThat(roomy.MinimumSize.X).IsGreater(compact.MinimumSize.X);
        AssertThat(roomy.MinimumSize.Y).IsGreater(compact.MinimumSize.Y);
    }

    /// <summary>
    /// The minimum is a whole number of pixels: a fractional one cannot be reached by a real node size, so a
    /// node set exactly to it would still be 0.4 px short - and trip the too-small warning forever.
    /// </summary>
    [TestCase]
    public void MinimumSizeIsWholePixels()
    {
        var theme = ChartTheme.Dark();
        var chart = Build(new FakeCanvas2D(), theme, c =>
        {
            c.Title = "Revenue";
            c.Encode(Channel.Color, "series");
            c.Legend(new LegendConfig { Position = LegendPosition.Right });
            c.XAxis(new AxisConfig { Title = "Month" });
            c.YAxis(new AxisConfig { Title = "Revenue" });
        });

        AssertThat(chart.MinimumSize.X).IsGreater(0f);
        AssertThat(chart.MinimumSize.Y).IsGreater(0f);
        AssertThat(chart.MinimumSize.X).IsEqual(MathF.Ceiling(chart.MinimumSize.X));
        AssertThat(chart.MinimumSize.Y).IsEqual(MathF.Ceiling(chart.MinimumSize.Y));
    }

    // ── The engine side ─────────────────────────────────────────────────────

    /// <summary>
    /// The node reports the content minimum to the engine, which is what makes a <c>VBoxContainer</c> or a
    /// <c>ScrollContainer</c> stop squeezing the chart - and gives the page its own size back with
    /// <see cref="ChartView.IgnoreContentMinimumSize"/> (the "I want a small chart and will crop it" way out).
    /// </summary>
    [TestCase]
    public void TheViewReportsTheContentMinimumToTheEngine()
    {
        if (!HasSceneTree()) return;

        var view = AddView(new FakeCanvas2D(), new Vector2(360f, 260f));
        try
        {
            view.SetData(Categories());
            PumpFrame(view);

            AssertThat(view.Chart is not null).IsTrue();
            var minimum = view.Chart!.MinimumSize;
            AssertThat(minimum.X > 0f && minimum.Y > 0f).IsTrue();
            AssertThat(view._GetMinimumSize()).IsEqual(minimum);

            // Ignoring it falls back to the plain Control value (no children are collapsed into it).
            view.IgnoreContentMinimumSize = true;
            var bare = new Control();
            try
            {
                AssertThat(view._GetMinimumSize()).IsEqual(bare._GetMinimumSize());
            }
            finally
            {
                bare.Free();
            }
        }
        finally
        {
            Release(view);
        }
    }

    // ── The minimum before the chart exists ─────────────────────────────────

    /// <summary>
    /// A node that has not drawn a frame - and whose chart therefore does not exist yet - still reports a
    /// minimum. Zero would be fatal for a container: the first layout happens before any chart exists, so a
    /// container would collapse the node to a pixel and the chart would be built on a 1 px surface it can never
    /// grow out of (measured: every cell of the gallery pages collapsed this way when the scenes' own
    /// <c>custom_minimum_size</c> was removed).
    /// </summary>
    [TestCase]
    public void AViewWithoutAChartStillReportsAMinimum()
    {
        if (!HasSceneTree()) return;

        var view = AddView(new FakeCanvas2D(), new Vector2(320f, 240f));
        try
        {
            // No frame yet: the chart is built by _Process (and it needs a canvas, which needs a size).
            AssertThat(view.Chart is null).IsTrue();

            var estimate = view._GetMinimumSize();
            AssertThat(estimate.X > 0f && estimate.Y > 0f).IsTrue();
            AssertThat(estimate.X >= Chart.MinimumPlotSize.X).IsTrue();
            AssertThat(estimate.Y >= Chart.MinimumPlotSize.Y).IsTrue();

            // The estimate reserves what the node is configured to show, so it grows with the content.
            view.Title = "Revenue";
            var titled = view._GetMinimumSize();
            AssertThat(titled.Y).IsGreater(estimate.Y);

            view.XAxisTitle = "Month";
            var withAxisTitle = view._GetMinimumSize();
            AssertThat(withAxisTitle.Y).IsGreater(titled.Y);
        }
        finally
        {
            Release(view);
        }
    }

    /// <summary>
    /// The estimate is a stand-in, not the answer: once a frame has drawn, the node reports the chart's own
    /// measurement - which is also what makes the container lay it out again (see the case below).
    /// </summary>
    [TestCase]
    public void TheMeasuredMinimumReplacesTheEstimate()
    {
        if (!HasSceneTree()) return;

        var view = AddView(new FakeCanvas2D(), new Vector2(320f, 240f));
        try
        {
            view.SetData(Categories());
            var estimate = view._GetMinimumSize();
            PumpFrame(view);

            AssertThat(view.Chart is not null).IsTrue();
            var measured = view.Chart!.MinimumSize;
            AssertThat(measured.X > 0f && measured.Y > 0f).IsTrue();
            AssertThat(view._GetMinimumSize()).IsEqual(measured);

            // The estimate has to be in the same ballpark, or a container reserves visibly wrong on the first
            // frame: it is allowed to be generous, not to be nonsense.
            AssertThat(estimate.X <= measured.X * 1.5f).IsTrue();
            AssertThat(estimate.Y <= measured.Y * 1.5f).IsTrue();
            AssertThat(estimate.X >= measured.X * 0.4f).IsTrue();
            AssertThat(estimate.Y >= measured.Y * 0.4f).IsTrue();
        }
        finally
        {
            Release(view);
        }
    }

    /// <summary>
    /// The case the whole first-frame path exists for: a <see cref="ChartView"/> with no
    /// <c>custom_minimum_size</c> in a container that sizes its children from their minimum. Real frames are
    /// awaited because the container sorts on the engine's own layout pass, not inside one frame.
    /// </summary>
    [TestCase]
    public async Task AContainerDoesNotCollapseAViewWithoutACustomMinimum()
    {
        if (!HasSceneTree()) return;

        var fake = new FakeCanvas2D();
        var box = new VBoxContainer { Size = new Vector2(400f, 320f) };
        var view = new ChartView { CanvasFactory = (_, _) => fake };
        box.AddChild(view);
        ((SceneTree)Engine.GetMainLoop()).Root.AddChild(box);
        try
        {
            view.SetData(Categories());

            // A few real frames: the first layout runs before the chart exists (the estimate answers it), the
            // chart then measures itself and tells the container to lay out again.
            var tree = (SceneTree)Engine.GetMainLoop();
            for (int frame = 0; frame < 5; frame++) await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);

            AssertThat(view.Chart is not null).IsTrue();
            AssertThat(view.Size.Y > 1f).IsTrue();
            AssertThat(view.Size.Y >= view.Chart!.MinimumSize.Y - 1f).IsTrue();
        }
        finally
        {
            box.GetParent()?.RemoveChild(box);
            box.Free();
        }
    }

    // ── The too-small warning ───────────────────────────────────────────────

    /// <summary>
    /// Drawn below its content minimum a chart says so - once. The warning is a hint (the chart keeps
    /// drawing, the axis thins its own labels), so repeating it every frame would only flood the log; and it
    /// has to come back after the chart fits again, because the next shrink is news again.
    /// </summary>
    [TestCase]
    public void TooSmallCanvasWarnsOnce()
    {
        var log = EngineMessageLog.Attach();
        try
        {
            var chart = Build(new FakeCanvas2D(), ChartTheme.Dark());   // 400x300: above its minimum
            AssertThat(log.WarningsContaining(TooSmall).Length).IsEqual(0);

            chart.Width = 120f;
            chart.Height = 60f;
            chart.Render();
            AssertThat(log.WarningsContaining(TooSmall).Length).IsEqual(1);

            // It keeps drawing, and stays quiet about being small.
            chart.Render();
            chart.Render();
            AssertThat(log.WarningsContaining(TooSmall).Length).IsEqual(1);

            // Room enough again: the warning is armed once more.
            chart.Width = 400f;
            chart.Height = 300f;
            chart.Render();
            AssertThat(log.WarningsContaining(TooSmall).Length).IsEqual(1);

            chart.Width = 120f;
            chart.Height = 60f;
            chart.Render();
            AssertThat(log.WarningsContaining(TooSmall).Length).IsEqual(2);
        }
        finally
        {
            log.Detach();
        }
    }

    /// <summary>
    /// A canvas smaller than its own padding - which is what "the chart was squeezed to nothing" leaves
    /// behind - still produces a plot rectangle of at least one pixel: a negative one draws nothing at all
    /// (and used to be handed to the marks as a negative size).
    /// </summary>
    [TestCase]
    public void AnExtremelySmallCanvasKeepsAPositivePlotArea()
    {
        var canvas = new FakeCanvas2D();
        var chart = new Chart(canvas) { Width = 20f, Height = 12f };
        chart.Encode(Channel.X, "category");
        chart.Encode(Channel.Y, "value");
        chart.Mark(new IntervalMark());
        chart.Data(Categories());
        chart.Render();

        var plot = chart.CurrentPlotArea;
        AssertThat(plot is not null).IsTrue();
        AssertThat(plot!.Value.Width >= 1f).IsTrue();
        AssertThat(plot.Value.Height >= 1f).IsTrue();
    }

    // ── Plumbing ────────────────────────────────────────────────────────────

    private static bool HasSceneTree()
    {
        if (Engine.GetMainLoop() is SceneTree) return true;
        GD.Print("[skip] MinimumSize tests need a SceneTree; none in this run");
        return false;
    }

    /// <summary>Create a view with an injected canvas, sized and added to the tree.</summary>
    private static ChartView AddView(FakeCanvas2D fake, Vector2 size)
    {
        var view = new ChartView { Size = size, CanvasFactory = (_, _) => fake };
        ((SceneTree)Engine.GetMainLoop()).Root.AddChild(view);
        return view;
    }

    /// <summary>Run one frame the way the engine does: the view rebuilds, its surface then draws.</summary>
    private static void PumpFrame(ChartView view, int frames = 2, float delta = 0.016f)
    {
        for (int frame = 0; frame < frames; frame++)
        {
            view._Process(delta);
            foreach (var child in view.GetChildren(includeInternal: true))
                if (child is Canvas2DControl surface) surface._Process(delta);
        }
    }

    /// <summary>Remove the view from the tree and free it.</summary>
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
