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
/// The layered render path of <see cref="Chart"/>: the data layer (background, grid, axes, legend, the marks
/// without their interaction state) can be kept in an image and presented on the frames that do not change any
/// of its inputs, so moving the pointer only costs the overlay.
/// <para>
/// The observation is the canvas traffic - <c>CaptureRegion</c> is exactly one call per rebuild and
/// <c>DrawImage</c> exactly one per presented frame - rather than a counter on the chart: what matters is what
/// the chart asks the backend to do.
/// </para>
/// <para>
/// Off by default is a hard requirement here, not a preference: with the switch off the frame has to be the
/// historical single pass (same draw calls, same save/restore pairs), which these cases pin down.
/// </para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class LayerCacheTest
{
    /// <summary>Three categories, one series: a hover, a selection and a neighbour to compare against.</summary>
    private static List<DataRow> Rows() =>
    [
        TestContexts.Row(("cat", "A"), ("value", 10.0)),
        TestContexts.Row(("cat", "B"), ("value", 20.0)),
        TestContexts.Row(("cat", "C"), ("value", 15.0)),
    ];

    /// <summary>A line chart on the given canvas, with the layer switch set as asked.</summary>
    private static Chart LineChart(FakeCanvas2D canvas, bool layeredRendering)
    {
        var chart = new Chart(canvas) { Width = 400f, Height = 300f, UseLayerCache = layeredRendering };
        chart.Data(Rows());
        chart.Mark(new LineMark());
        chart.Encode(Channel.X, "cat");
        chart.Encode(Channel.Y, "value");
        return chart;
    }

    /// <summary>Circles of a frame, formatted so two frames can be compared as one string.</summary>
    private static string Circles(FakeCanvas2D canvas) => CirclesSince(canvas, 0);

    /// <summary>
    /// Circles one frame added, in order. The fake accumulates every call it ever received, so a single frame
    /// is the slice since the count recorded before it - comparing whole lists across frames silently compares
    /// frame 1 with frames 1+2.
    /// </summary>
    private static string CirclesSince(FakeCanvas2D canvas, int from) => string.Join(";",
        canvas.Circles.Skip(from).Select(c => $"{c.Cx:F2},{c.Cy:F2},{c.Radius:F2}"));

    // ── Off by default ────────────────────────────────────────

    /// <summary>
    /// The switch is off by default everywhere it exists, and an off frame captures nothing and presents
    /// nothing: a chart that never asks for the cache must not pay for it.
    /// </summary>
    [TestCase]
    public void LayeredRenderingIsOffByDefault()
    {
        // Registered for gdUnit: an instantiated node that nobody frees is reported as an orphan and turns
        // the whole run red, which is not what this case is about.
        var view = AutoFree(new ChartView());
        var control = AutoFree(new Canvas2DControl());
        AssertThat(view.LayeredRendering).IsFalse();
        AssertThat(control.LayeredRendering).IsFalse();

        var canvas = new FakeCanvas2D();
        var chart = LineChart(canvas, layeredRendering: false);
        AssertThat(chart.UseLayerCache).IsFalse();

        chart.Hover(1);
        chart.Render();
        chart.Render();

        AssertThat(canvas.CaptureCount).IsEqual(0);
        AssertThat(canvas.ImageDrawCount).IsEqual(0);

        // The hover marker is still painted by the data layer, as it always was.
        AssertThat(Circles(canvas)).IsNotEqual("");
    }

    /// <summary>
    /// With the switch off, a frame is the historical single pass: the interaction state is painted by the data
    /// layer (nothing double-blends, the overlay has nothing to add), the frame is reproducible for the same
    /// inputs, and its save/restore stack stays balanced.
    /// </summary>
    [TestCase]
    public void TheOffPathIsTheSinglePass()
    {
        static Chart Build(FakeCanvas2D canvas)
        {
            var chart = LineChart(canvas, layeredRendering: false);
            chart.Hover(1);
            chart.Select(2);
            return chart;
        }

        var canvas = new FakeCanvas2D();
        Build(canvas).Render();

        var second = new FakeCanvas2D();
        Build(second).Render();

        AssertThat(canvas.CaptureCount).IsEqual(0);
        AssertThat(canvas.ImageDrawCount).IsEqual(0);
        AssertThat(canvas.SaveRestoreBalanced).IsTrue();
        AssertThat(Circles(canvas)).IsNotEqual("");          // hover marker plus selection ring
        AssertThat(second.Snapshot()).IsEqual(canvas.Snapshot());
    }

    // ── Caching ───────────────────────────────────────────────

    /// <summary>
    /// The first frame captures the data layer, and a frame that changes none of its inputs presents that
    /// image instead of drawing the layer again.
    /// </summary>
    [TestCase]
    public void TheDataLayerIsCachedAndPresentedOnTheNextFrame()
    {
        var canvas = new FakeCanvas2D();
        var chart = LineChart(canvas, layeredRendering: true);

        chart.Render();
        AssertThat(canvas.CaptureCount).IsEqual(1);
        AssertThat(canvas.ImageDrawCount).IsEqual(0);

        chart.Render();
        AssertThat(canvas.CaptureCount).IsEqual(1);
        AssertThat(canvas.ImageDrawCount).IsEqual(1);

        // ...and the image is put back where it was captured, at its own size.
        var capture = canvas.Captures[0];
        var draw = canvas.ImageDraws[0];
        AssertThat(draw.X).IsEqual((float)capture.X);
        AssertThat(draw.Y).IsEqual((float)capture.Y);
        AssertThat(draw.W).IsEqual((float)capture.W);
        AssertThat(draw.H).IsEqual((float)capture.H);
    }

    /// <summary>
    /// Moving the pointer must not rebuild the layer - that is the whole point of the option - while the hover
    /// highlight still follows: the cached frame paints the same marker a single-pass frame paints.
    /// </summary>
    [TestCase]
    public void HoverDoesNotRebuildTheLayerAndTheHighlightFollows()
    {
        var canvas = new FakeCanvas2D();
        var chart = LineChart(canvas, layeredRendering: true);
        chart.Render();
        AssertThat(canvas.CaptureCount).IsEqual(1);
        int firstFrame = canvas.Circles.Count;
        AssertThat(firstFrame).IsEqual(0);                    // nothing hovered: no marker in the frame

        chart.Hover(1);
        chart.Render();
        string atRowOne = CirclesSince(canvas, firstFrame);
        AssertThat(canvas.CaptureCount).IsEqual(1);           // still the same layer
        AssertThat(canvas.ImageDrawCount).IsEqual(1);         // presented, not redrawn
        AssertThat(canvas.Circles.Count).IsEqual(firstFrame + 1);

        chart.Hover(2);
        chart.Render();
        string atRowTwo = CirclesSince(canvas, firstFrame + 1);
        AssertThat(canvas.CaptureCount).IsEqual(1);
        AssertThat(atRowTwo).IsNotEqual("");
        AssertThat(atRowTwo).IsNotEqual(atRowOne);            // the highlight moved with the pointer

        // The same frame without the cache: the same marker, painted by the data layer instead.
        var reference = new FakeCanvas2D();
        var singlePass = LineChart(reference, layeredRendering: false);
        singlePass.Hover(2);
        singlePass.Render();
        AssertThat(atRowTwo).IsEqual(Circles(reference));
    }

    /// <summary>
    /// A selection is part of the interaction state as well: selecting another row must not rebuild the layer,
    /// but the ring has to appear (a cached layer that froze the ring would be the same failure as a frozen
    /// hover).
    /// </summary>
    [TestCase]
    public void SelectionDoesNotRebuildTheLayerButDrawsItsRing()
    {
        var canvas = new FakeCanvas2D();
        var chart = LineChart(canvas, layeredRendering: true);
        chart.Render();

        int strokesBefore = canvas.StrokeCount;
        chart.Select(2);
        chart.Render();

        AssertThat(canvas.CaptureCount).IsEqual(1);
        AssertThat(canvas.StrokeCount > strokesBefore).IsTrue();
    }

    /// <summary>
    /// Every input the data layer is built from rebuilds it: the data, the size (the plot rectangle), the
    /// theme, the focused series (it dims the other series and the legend) and the legend configuration.
    /// Anything missing here would show up as a picture that stopped following its input - the one failure the
    /// cache must never have - so the key is written strictly and this case guards it.
    /// </summary>
    [TestCase]
    public void DataLayoutThemeFocusAndLegendChangesRebuildTheLayer()
    {
        var canvas = new FakeCanvas2D();
        var chart = LineChart(canvas, layeredRendering: true);
        chart.Legend(new LegendConfig { Position = LegendPosition.Bottom });
        chart.Render();
        int rebuilds = canvas.CaptureCount;

        void ExpectRebuild(string what, Action change)
        {
            change();
            chart.Render();
            AssertThat(canvas.CaptureCount).IsEqual(rebuilds + 1);
            rebuilds = canvas.CaptureCount;
        }

        ExpectRebuild("data", () => chart.AppendData(TestContexts.Row(("cat", "D"), ("value", 12.0))));
        ExpectRebuild("plot area", () => chart.Width = 520f);
        ExpectRebuild("theme", () => chart.Theme(ChartTheme.Light()));
        ExpectRebuild("focused series", () => chart.FocusSeries("A"));
        ExpectRebuild("legend configuration",
            () => chart.Legend(new LegendConfig { Position = LegendPosition.Right, SwatchSize = 22f }));

        // ...and a frame that changes nothing still presents the cached layer.
        chart.Render();
        AssertThat(canvas.CaptureCount).IsEqual(rebuilds);
        AssertThat(canvas.ImageDrawCount).IsGreater(0);
    }

    /// <summary>
    /// The animation progress is an input of the data layer (the entry clip, the growing bars): while an
    /// animation runs the layer is rebuilt every frame, and once it settles the frames present the cache again.
    /// </summary>
    [TestCase]
    public void TheAnimationRebuildsTheLayerWhileItRuns()
    {
        var canvas = new FakeCanvas2D();
        var chart = LineChart(canvas, layeredRendering: true);

        chart.Animate(0.25f);
        chart.Render();
        AssertThat(canvas.CaptureCount).IsEqual(1);

        chart.Animate(0.5f);
        chart.Render();
        AssertThat(canvas.CaptureCount).IsEqual(2);

        chart.Animate(1f);
        chart.Render();
        AssertThat(canvas.CaptureCount).IsEqual(3);

        // The animation settled: the next frame is the cached one.
        chart.Render();
        AssertThat(canvas.CaptureCount).IsEqual(3);
        AssertThat(canvas.ImageDrawCount).IsGreater(0);
    }

    // ── Fall-backs ────────────────────────────────────────────

    /// <summary>
    /// A backend that cannot read its surface back cannot keep a layer; the chart says so through the
    /// capability test and renders single-pass - including the hover highlight, which the data layer then
    /// paints itself.
    /// </summary>
    [TestCase]
    public void ABackendWithoutCaptureSupportFallsBackToTheSinglePass()
    {
        var canvas = new FakeCanvas2D
        {
            Capabilities = new CanvasCapabilities(
                SupportsGradients: true, SupportsClipping: true, SupportsTransforms: true,
                IsGpuBacked: false, SupportsLineDash: true, SupportsImages: true,
                SupportsSurfaceCapture: false),
        };
        var chart = LineChart(canvas, layeredRendering: true);
        chart.Hover(1);
        chart.Render();
        chart.Render();

        AssertThat(canvas.CaptureCount).IsEqual(0);
        AssertThat(canvas.ImageDrawCount).IsEqual(0);
        AssertThat(Circles(canvas)).IsNotEqual("");   // the state is in the data layer, so it still follows
    }

    /// <summary>
    /// A capture that comes back in another size than requested means the surface clipped the region: a
    /// partial layer blitted over the whole chart would be visible wrongness, so the chart drops the image,
    /// stops asking, and keeps rendering single-pass.
    /// </summary>
    [TestCase]
    public void ACaptureThatComesBackClippedTurnsTheCacheOff()
    {
        var canvas = new FakeCanvas2D { CaptureSizeOverride = (10, 10) };
        var chart = LineChart(canvas, layeredRendering: true);

        chart.Render();
        AssertThat(canvas.CaptureCount).IsEqual(1);
        AssertThat(canvas.CapturedHandleDisposeCount).IsEqual(1);   // the unusable handle was released

        chart.Render();
        AssertThat(canvas.CaptureCount).IsEqual(1);                 // ...and nothing is asked for again
        AssertThat(canvas.ImageDrawCount).IsEqual(0);

        // The frame is still a complete chart: the data layer is drawn, not presented.
        AssertThat(canvas.FillCount).IsGreater(0);
    }

    /// <summary>
    /// Turning the switch off releases the image: keeping a full-size bitmap for a chart that stopped caching
    /// would be the cost of the option without its benefit.
    /// </summary>
    [TestCase]
    public void TurningTheCacheOffReleasesTheLayer()
    {
        var canvas = new FakeCanvas2D();
        var chart = LineChart(canvas, layeredRendering: true);
        chart.Render();
        AssertThat(canvas.CapturedHandleDisposeCount).IsEqual(0);

        chart.UseLayerCache = false;
        AssertThat(canvas.CapturedHandleDisposeCount).IsEqual(1);

        chart.Render();
        AssertThat(canvas.CaptureCount).IsEqual(1);   // nothing new was captured
        AssertThat(canvas.ImageDrawCount).IsEqual(0);
    }

    /// <summary>
    /// Disposing the chart releases the cached image as well: it belongs to the canvas the chart draws on, and
    /// nothing else can reach it afterwards.
    /// </summary>
    [TestCase]
    public void DisposingTheChartReleasesTheLayer()
    {
        var canvas = new FakeCanvas2D();
        var chart = LineChart(canvas, layeredRendering: true);
        chart.Render();

        chart.Dispose();
        AssertThat(canvas.CapturedHandleDisposeCount).IsEqual(1);
        AssertThat(canvas.DisposeCount).IsEqual(0);   // the canvas is not owned by the chart
    }

    /// <summary>
    /// Both paths have to leave the canvas state balanced and the geometry finite for every migrated mark: a
    /// cached frame adds a save/clip pair of its own for the overlay, and a clip that is never popped clips
    /// every later frame.
    /// </summary>
    [TestCase]
    public void BothPathsKeepTheCanvasStateBalanced()
    {
        var problems = new List<string>();
        foreach (var (name, mark) in new (string Name, Mark Mark)[]
                 {
                     ("LineMark", new LineMark()),
                     ("IntervalMark", new IntervalMark()),
                     ("PointMark", new PointMark()),
                 })
        {
            foreach (bool layered in new[] { false, true })
            {
                var canvas = new FakeCanvas2D();
                var chart = new Chart(canvas) { Width = 400f, Height = 300f, UseLayerCache = layered };
                chart.Data(Rows());
                chart.Mark(mark);
                chart.Encode(Channel.X, "cat");
                chart.Encode(Channel.Y, "value");
                chart.Hover(1);
                chart.Select(2);
                chart.Render();
                chart.Render();

                string path = $"{name} (layered: {layered})";
                if (!canvas.SaveRestoreBalanced) problems.Add($"{path}: unbalanced save/restore");
                if (canvas.NonFiniteCoordinateCount > 0) problems.Add($"{path}: non-finite coordinate");
                if (canvas.NegativeSizeRectCount > 0) problems.Add($"{path}: negative-size rect");
            }
        }

        AssertThat(string.Join("\n", problems)).IsEqual("");
    }

    // ── The node ──────────────────────────────────────────────

    /// <summary>
    /// The export of the node reaches the chart it built (and the surface it draws on): that is the whole
    /// plumbing of <see cref="ChartView.LayeredRendering"/>.
    /// </summary>
    [TestCase]
    public void TheViewHandsItsSwitchToTheChart()
    {
        SceneTree tree = Asserts.RequireSceneTree();

        var canvas = new FakeCanvas2D();
        var view = new ChartView { Size = new Vector2(320f, 200f), CanvasFactory = (_, _) => canvas };
        tree.Root.AddChild(view);
        try
        {
            view.SetValues([("A", 10.0), ("B", 20.0)]);
            PumpFrame(view, 2);
            AssertThat(view.Chart).IsNotNull();
            AssertThat(view.Chart!.UseLayerCache).IsFalse();
            AssertThat(canvas.CaptureCount).IsEqual(0);

            view.LayeredRendering = true;
            PumpFrame(view, 2);
            AssertThat(view.Chart!.UseLayerCache).IsTrue();
            AssertThat(view.Surface!.LayeredRendering).IsTrue();
            AssertThat(canvas.CaptureCount).IsGreater(0);
        }
        finally
        {
            view.GetParent()?.RemoveChild(view);
            view.Free();
        }
    }

    /// <summary>Run one frame the way the engine does: the view rebuilds, its surface then draws.</summary>
    private static void PumpFrame(ChartView view, int frames)
    {
        for (int i = 0; i < frames; i++)
        {
            view._Process(0.016);
            foreach (var child in view.GetChildren(includeInternal: true))
                if (child is Canvas2DControl surface)
                    surface._Process(0.016);
        }
    }

    /// <summary>
    /// <see cref="Chart.InvalidateLayerCache"/>: a mark mutated behind the chart's back is not an input it can
    /// observe, so the cached layer has to be dropped by hand - after which the next frame rebuilds it (a second
    /// capture) instead of presenting the stale image.
    /// </summary>
    [TestCase]
    public void InvalidatingTheLayerCacheRebuildsTheNextFrame()
    {
        var canvas = new FakeCanvas2D();
        using var chart = LineChart(canvas, layeredRendering: true);

        chart.Render();
        AssertThat(canvas.CaptureCount).IsEqual(1);

        ((LineMark)chart.Marks[0]).StrokeWidth = 6f;    // invisible to the chart: it is not a tracked input
        chart.Render();
        AssertThat(canvas.CaptureCount).IsEqual(1);     // the layer is still presented

        chart.InvalidateLayerCache();
        chart.Render();
        AssertThat(canvas.CaptureCount).IsEqual(2);     // ...and now it was rebuilt
    }
}
