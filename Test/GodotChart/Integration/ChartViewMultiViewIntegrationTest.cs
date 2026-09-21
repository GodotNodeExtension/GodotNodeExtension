namespace GodotNodeExtension.Tests.GodotChart;

using System.Collections.Generic;
using GdUnit4;
using Godot;
using GodotNodeExtension.Component.GodotChart;
using GodotNodeExtension.Component.GodotChart.Canvas;
using GodotNodeExtension.Component.GodotChart.Marks;
using GodotNodeExtension.Component.GodotSkia;
using static GdUnit4.Assertions;

/// <summary>
/// Several charts on the engine's <b>real</b> backend at once. Every surface shares one GPU context and
/// serialises its barriers on a process-wide queue, which is the part no single-chart case can see: a
/// second chart used to mean a second Skia GPU resource cache, and a barrier submitted while another
/// surface was mid-transition is exactly where a layout bookkeeping mistake would show.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class ChartViewMultiViewIntegrationTest
{
    /// <summary>How many views the cases keep alive at the same time.</summary>
    private const int ViewCount = 8;

    private static ChartRenderCase CaseFor(int index) => ChartRenderCase.All[index % ChartRenderCase.All.Count];

    private static void AddChartViews(List<ChartView> views, List<ChartRenderCase> cases, int count)
    {
        for (int i = 0; i < count; i++)
        {
            var c = CaseFor(i);
            var view = ChartRenderHarness.AddView(c, ChartRenderHarness.ViewSize);
            view.SetData(c.Rows);
            views.Add(view);
            cases.Add(c);
        }
        foreach (var view in views) ChartRenderHarness.Pump(view);
    }

    /// <summary>
    /// Eight live views still share <b>one</b> GRContext (that is the point of the shared context), every one
    /// of them draws, and releasing them all leaves the process clean - the counters come back to where they
    /// started instead of drifting by the number of views ever created.
    /// </summary>
    [TestCase]
    public void LiveViewsShareOneContextAndAllReleaseIt()
    {
        const string name = nameof(LiveViewsShareOneContextAndAllReleaseIt);
        if (ChartRenderHarness.NoRenderingDevice(name)) return;

        int baselineRefs = SkiaCanvasTexture2D.SharedGrContextRefCount;
        int baselineCreates = SkiaCanvasTexture2D.GrContextCreateCount;

        var views = new List<ChartView>();
        var cases = new List<ChartRenderCase>();
        try
        {
            AddChartViews(views, cases, ViewCount);

            AssertThat(SkiaCanvasTexture2D.SharedGrContextRefCount).IsEqual(baselineRefs + ViewCount);

            int createGrowth = SkiaCanvasTexture2D.GrContextCreateCount - baselineCreates;
            GD.Print($"{ViewCount} live chart views: grcontext creates +{createGrowth}");
            AssertThat(createGrowth).IsEqual(1);

            // Every view published a surface of its own and drew something into it.
            var problems = new List<string>();
            for (int i = 0; i < views.Count; i++)
            {
                var image = ChartRenderHarness.Pixels(views[i]);
                if (image is null)
                {
                    problems.Add($"{cases[i].Label}: no texture");
                    continue;
                }
                float ratio = ChartRenderHarness.ContentRatio(views[i], out _);
                if (ratio <= 0f) problems.Add($"{cases[i].Label}: empty surface");
                if (views[i].Canvas is not SkiaCanvas2DBackend)
                    problems.Add($"{cases[i].Label}: not the Skia backend");
            }
            AssertThat(string.Join("; ", problems)).IsEqual("");
        }
        finally
        {
            foreach (var view in views) ChartRenderHarness.Release(view);
        }

        AssertThat(SkiaCanvasTexture2D.SharedGrContextRefCount).IsEqual(baselineRefs);
    }

    /// <summary>
    /// A chart renders the same pixels whether it is alone or surrounded by seven others: nothing about the
    /// presentation (surface size, pixel density, clear colour) may depend on how many views are alive.
    /// </summary>
    [TestCase]
    public void AChartRendersTheSamePixelsWithOtherViewsAround()
    {
        const string name = nameof(AChartRendersTheSamePixelsWithOtherViewsAround);
        if (ChartRenderHarness.NoRenderingDevice(name)) return;

        var c = CaseFor(0);

        // Reference: the case on its own.
        ulong alone;
        ChartView solo = ChartRenderHarness.AddView(c, ChartRenderHarness.ViewSize);
        try
        {
            solo.SetData(c.Rows);
            ChartRenderHarness.Pump(solo);
            var image = ChartRenderHarness.Pixels(solo);
            AssertThat(image is not null).IsTrue();
            alone = ChartRenderHarness.PixelFingerprint(image!);
        }
        finally
        {
            ChartRenderHarness.Release(solo);
        }

        // The same case with seven neighbours, all drawing in the same frames.
        var views = new List<ChartView>();
        var cases = new List<ChartRenderCase>();
        try
        {
            AddChartViews(views, cases, ViewCount - 1);

            var neighbour = ChartRenderHarness.AddView(c, ChartRenderHarness.ViewSize);
            neighbour.SetData(c.Rows);
            views.Add(neighbour);

            foreach (var view in views) ChartRenderHarness.Pump(view, 2);

            var crowded = ChartRenderHarness.Pixels(neighbour);
            AssertThat(crowded is not null).IsTrue();
            AssertThat(ChartRenderHarness.PixelFingerprint(crowded!)).IsEqual(alone);
        }
        finally
        {
            foreach (var view in views) ChartRenderHarness.Release(view);
        }
    }

    /// <summary>
    /// A chart sharing its canvas with a second chart (the documented "one chart per tab" pattern) must not
    /// dispose that canvas when it goes: the canvas belongs to the host, and the survivor keeps rendering.
    /// </summary>
    [TestCase]
    public void ChartsSharingACanvasDoNotDisposeItForEachOther()
    {
        const string name = nameof(ChartsSharingACanvasDoNotDisposeItForEachOther);
        if (ChartRenderHarness.NoRenderingDevice(name)) return;

        var node = new Canvas2DControl { Size = ChartRenderHarness.ViewSize };
        ((SceneTree)Engine.GetMainLoop()).Root.AddChild(node);
        try
        {
            if (node.Canvas is not { } canvas)
            {
                GD.Print("[skip] ChartsSharingACanvasDoNotDisposeItForEachOther: the host published no canvas");
                return;
            }

            var first = new Chart(canvas) { Width = 320f, Height = 200f };
            var c = CaseFor(1);
            first.Data(c.Rows);
            first.Mark(new IntervalMark());
            first.Encode(Channel.X, c.XField);
            first.Encode(Channel.Y, c.YField);
            first.Render();

            // A second chart on the same canvas, then the first one goes - without ownsCanvas the canvas has
            // to survive, which the second chart's render proves.
            var second = new Chart(canvas) { Width = 320f, Height = 200f };
            second.Data(c.Rows);
            second.Mark(new PointMark());
            second.Encode(Channel.X, c.XField);
            second.Encode(Channel.Y, c.YField);
            second.Render();
            first.Dispose();

            node._Process(0.016);
            second.Render();
            second.Dispose();

            AssertThat(node.Canvas is not null).IsTrue();
        }
        finally
        {
            node.GetParent()?.RemoveChild(node);
            node.Free();
        }
    }
}
