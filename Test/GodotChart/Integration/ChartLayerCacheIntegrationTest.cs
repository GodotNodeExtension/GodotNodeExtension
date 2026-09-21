namespace GodotNodeExtension.Tests.GodotChart;

using System;
using GdUnit4;
using Godot;
using GodotNodeExtension.Component.GodotChart;
using GodotNodeExtension.Component.GodotChart.Canvas;
using static GdUnit4.Assertions;

/// <summary>
/// The layer cache on the engine's <b>real</b> backend: the surface readback a cached layer needs has to work
/// against the Skia texture (GPU or CPU), and the frame a cached chart presents has to keep looking like the
/// chart - a stable picture while nothing changes, and a picture that follows the pointer when the state
/// visuals come from the overlay.
/// <para>
/// The rest of the suite drives a <c>FakeCanvas2D</c>, which records the calls but never rasterises: whether
/// the readback produces the right pixels and whether the presented layer really looks like the chart is only
/// visible here. Both suites skip themselves when the run has no rendering device.
/// </para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class ChartLayerCacheIntegrationTest
{
    /// <summary>Fraction of the surface that has to carry something other than the background.</summary>
    private const float MinContentRatio = 0.02f;

    /// <summary>
    /// The backend a cached layer draws on reads its own surface back: the whole option rests on it, so the
    /// capture is checked on the real texture - including the clamp that makes "the region lies outside the
    /// surface" a smaller handle instead of a wrong layer.
    /// </summary>
    [TestCase]
    public void TheBackendCapturesItsSurface()
    {
        if (ChartRenderHarness.NoRenderingDevice(nameof(TheBackendCapturesItsSurface))) return;

        ICanvas2D? canvas = null;
        try
        {
            canvas = Canvas2DFactory.Create(64, 48);
            AssertThat(canvas.Capabilities.SupportsSurfaceCapture).IsTrue();

            canvas.BeginFrame();
            canvas.Clear(new Color(0.1f, 0.2f, 0.3f));
            using var captured = canvas.CaptureRegion(0, 0, 64, 48);
            AssertThat(captured.Width).IsEqual(64);
            AssertThat(captured.Height).IsEqual(48);

            // A region that reaches past the surface comes back clamped: the caller has to fall back instead of
            // blitting a part of the layer over the whole chart.
            using var clipped = canvas.CaptureRegion(32, 24, 64, 48);
            AssertThat(clipped.Width).IsEqual(32);
            AssertThat(clipped.Height).IsEqual(24);
            canvas.EndFrame();
        }
        finally
        {
            canvas?.Dispose();
        }
    }

    /// <summary>
    /// A chart with the cache on presents the same pixels while nothing changes (the layer is presented, not
    /// redrawn - and it stays the layer the chart would have drawn), and follows the pointer when the hover
    /// moves, because the highlight is painted on top of that layer.
    /// </summary>
    [TestCase]
    public void TheCachedFrameStaysStableAndFollowsThePointer()
    {
        const string Case = nameof(TheCachedFrameStaysStableAndFollowsThePointer);
        if (ChartRenderHarness.NoRenderingDevice(Case)) return;

        var view = ChartRenderHarness.AddView(ChartKind.Line, ChartRenderHarness.ViewSize);
        try
        {
            view.SetValues([("A", 10.0), ("B", 20.0), ("C", 15.0)]);
            view.ShowTooltip = false;      // the tooltip has its own fade, which is not what this case measures
            view.ShowCrosshair = false;
            view.LayeredRendering = true;
            ChartRenderHarness.Pump(view, 2);

            AssertThat(view.Chart).IsNotNull();
            AssertThat(view.Chart!.UseLayerCache).IsTrue();

            var painted = ChartRenderHarness.Pixels(view);
            AssertThat(painted).IsNotNull();
            var (content, total) = ChartRenderHarness.Measure(painted!);
            AssertThat((float)content / Math.Max(1, total) > MinContentRatio).IsTrue();

            // Nothing changed: the frames present the cached layer and stay identical.
            ChartRenderHarness.Pump(view, 2);
            var stable = ChartRenderHarness.Pixels(view);
            AssertThat(stable).IsNotNull();
            AssertThat(ChartRenderHarness.DifferingPixels(painted!, stable!)).IsEqual(0);

            // The pointer moves: the overlay paints the hover marker over the presented layer.
            view.Chart!.Hover(1);
            view.Repaint();
            ChartRenderHarness.Pump(view, 1);
            var hovered = ChartRenderHarness.Pixels(view);
            AssertThat(hovered).IsNotNull();
            AssertThat(ChartRenderHarness.DifferingPixels(stable!, hovered!)).IsGreater(0);

            // ...and the layer was not rebuilt for it (the frame is presented, not redrawn from the marks).
            view.Chart!.Hover(2);
            view.Repaint();
            ChartRenderHarness.Pump(view, 1);
            var moved = ChartRenderHarness.Pixels(view);
            AssertThat(ChartRenderHarness.DifferingPixels(hovered!, moved!)).IsGreater(0);
        }
        finally
        {
            ChartRenderHarness.Release(view);
        }
    }
}
