namespace GodotNodeExtension.Tests.GodotChart.Marks;

using System.Collections.Generic;
using System.Linq;
using GdUnit4;
using GodotNodeExtension.Component.GodotChart;
using GodotNodeExtension.Component.GodotChart.Canvas;
using GodotNodeExtension.Component.GodotChart.Marks;
using Support;
using static GdUnit4.Assertions;

/// <summary>
/// Hot-path allocation budget for marks. The number of drawing objects a mark requests must not
/// grow with the element count: <see cref="Mark.ShapePath"/> / <see cref="Mark.ShapePaint"/> hand
/// out one reusable pair per mark instead of creating an <see cref="IPath2D"/> / <see cref="IPaint2D"/>
/// per element, and repeated frames reuse both.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class MarkAllocationTest
{
    /// <summary>
    /// Objects a whole frame may create regardless of the element count (axes, grid, background). Measured
    /// with a 50-row IntervalMark: 5 per frame - the constant was 12, which let a per-element allocation hide
    /// in the slack.
    /// </summary>
    private const int FrameOverheadBudget = 5;

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

    /// <summary>Mark that asks the base class for a path/paint pair several times per render.</summary>
    private sealed class ShapeProbe : Mark
    {
        /// <inheritdoc />
        public override MarkCoordinate Coordinate => MarkCoordinate.Cartesian;

        /// <summary>Paths returned by <see cref="Mark.ShapePath"/> during the last render.</summary>
        public readonly List<IPath2D> Paths = [];

        /// <summary>Paints returned by <see cref="Mark.ShapePaint"/> during the last render.</summary>
        public readonly List<IPaint2D> Paints = [];

        /// <summary>Number of path/paint pairs requested per render.</summary>
        public int Requests { get; set; } = 4;

        /// <inheritdoc />
        public override void Render(MarkContext ctx)
        {
            Paths.Clear();
            Paints.Clear();
            for (int i = 0; i < Requests; i++)
            {
                Paths.Add(ShapePath(ctx));
                Paints.Add(ShapePaint(ctx));
            }
        }
    }

    private static MarkContext ShapeContext(ICanvas2D canvas)
        => TestContexts.Mark(canvas, [], new EncodeSet(), new ScaleSet());

    [TestCase]
    public void RepeatedFramesReuseTheDrawingObjects()
    {
        var oneFrame = RenderOnce(new IntervalMark(), MarkCases.ManyBars(50), "cat", "value", frames: 1);
        var fiveFrames = RenderOnce(new IntervalMark(), MarkCases.ManyBars(50), "cat", "value", frames: 5);

        // Extra frames may allocate their own per-frame objects (axis/grid), but not per element.
        int delta = (fiveFrames.Paths + fiveFrames.Paints) - (oneFrame.Paths + oneFrame.Paints);
        AssertThat(delta <= FrameOverheadBudget * 4).IsTrue();
    }

    [TestCase]
    public void ShapePathIsReusedAcrossElements()
    {
        var canvas = new FakeCanvas2D();
        var probe = new ShapeProbe { Requests = 6 };

        probe.Render(ShapeContext(canvas));

        AssertThat(probe.Paths.Count).IsEqual(6);
        AssertThat(probe.Paths.All(p => ReferenceEquals(p, probe.Paths[0]))).IsTrue();
        AssertThat(canvas.PathCreateCount).IsEqual(1);
    }

    [TestCase]
    public void ShapePaintIsReusedAcrossElements()
    {
        var canvas = new FakeCanvas2D();
        var probe = new ShapeProbe { Requests = 6 };

        probe.Render(ShapeContext(canvas));

        AssertThat(probe.Paints.Count).IsEqual(6);
        AssertThat(probe.Paints.All(p => ReferenceEquals(p, probe.Paints[0]))).IsTrue();
        AssertThat(canvas.PaintCreateCount).IsEqual(1);
    }

    [TestCase]
    public void ShapeObjectsAreCreatedOncePerCanvas()
    {
        var canvas = new FakeCanvas2D();
        var probe = new ShapeProbe();

        probe.Render(ShapeContext(canvas));
        probe.Render(ShapeContext(canvas));

        // Same canvas → the pair is kept; only the first render creates it.
        AssertThat(canvas.PathCreateCount).IsEqual(1);
        AssertThat(canvas.PaintCreateCount).IsEqual(1);
    }

    [TestCase]
    public void ShapeObjectsAreRecreatedWhenTheCanvasChanges()
    {
        var probe = new ShapeProbe();
        var first = new FakeCanvas2D();
        var second = new FakeCanvas2D();

        probe.Render(ShapeContext(first));
        var firstPath = probe.Paths[0];
        var firstPaint = probe.Paints[0];

        probe.Render(ShapeContext(second));

        // Drawing objects are canvas-bound: a new canvas needs a new pair.
        AssertThat(second.PathCreateCount).IsEqual(1);
        AssertThat(second.PaintCreateCount).IsEqual(1);
        AssertThat(ReferenceEquals(probe.Paths[0], firstPath)).IsFalse();
        AssertThat(ReferenceEquals(probe.Paints[0], firstPaint)).IsFalse();
    }
}
