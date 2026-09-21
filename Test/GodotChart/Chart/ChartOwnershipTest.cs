namespace GodotNodeExtension.Tests.GodotChart;

using System;
using GdUnit4;
using Godot;
using GodotNodeExtension.Component.GodotChart;
using GodotNodeExtension.Component.GodotChart.Canvas;
using GodotNodeExtension.Component.GodotChart.Marks;
using Support;
using static GdUnit4.Assertions;

/// <summary>
/// Tests for the resource ownership of <see cref="Chart"/> and the disposal safety of the canvas
/// backends it draws on: a chart only disposes the canvas it owns, shared canvases stay alive,
/// <c>Dispose</c> is idempotent, the constructor rejects a null canvas, and a disposed Skia backend
/// still answers capability and factory queries.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class ChartOwnershipTest
{
    // ── Canvas ownership ────────────────────────────────────────────────────

    [TestCase]
    public void DisposeKeepsAnInjectedCanvasAlive()
    {
        var canvas = new FakeCanvas2D();
        var chart = new Chart(canvas);

        chart.Dispose();

        AssertThat(canvas.DisposeCount).IsEqual(0);
    }

    [TestCase]
    public void DisposeReleasesAnOwnedCanvas()
    {
        var canvas = new FakeCanvas2D();
        var chart = new Chart(canvas, ownsCanvas: true);

        chart.Dispose();

        AssertThat(canvas.DisposeCount).IsEqual(1);
    }

    [TestCase]
    public void TwoChartsCanShareOneCanvas()
    {
        var canvas = new FakeCanvas2D();
        var first = new Chart(canvas);
        var second = new Chart(canvas);

        first.Dispose();

        // The second chart must still be able to render into the shared canvas.
        second.Data(new[] { TestContexts.Row(("cat", "A"), ("value", 1.0)) })
              .Mark(new IntervalMark())
              .Encode(Channel.X, "cat")
              .Encode(Channel.Y, "value");

        second.Render();

        AssertThat(canvas.DisposeCount).IsEqual(0);
    }

    [TestCase]
    public void DisposeIsIdempotent()
    {
        var shared = new FakeCanvas2D();
        var sharedChart = new Chart(shared);
        sharedChart.Dispose();
        sharedChart.Dispose(); // must not throw
        AssertThat(shared.DisposeCount).IsEqual(0);

        var owned = new FakeCanvas2D();
        var owner = new Chart(owned, ownsCanvas: true);
        owner.Dispose();
        owner.Dispose();
        AssertThat(owned.DisposeCount).IsEqual(1);   // released exactly once
    }

    [TestCase]
    public void NullCanvasIsRejected()
    {
        AssertThat(ThrowsArgumentNull(() => _ = new Chart(null!))).IsTrue();
    }

    private static bool ThrowsArgumentNull(Action action)
    {
        try
        {
            action();
            return false;
        }
        catch (ArgumentNullException)
        {
            return true;
        }
    }

    // ── Disposal safety of the canvas backend ───────────────────────────────

    [TestCase]
    public void CapabilitiesAreSafeAfterDispose()
    {
        // The Skia backend needs a rendering device, which a headless run does not provide.
        // In that case the guard is not exercised here (it is covered by GPU-capable runs).
        if (RenderingServer.GetRenderingDevice() is null)
        {
            GD.Print("[skip] CapabilitiesAreSafeAfterDispose: no rendering device in this run");
            return;
        }

        var backend = new SkiaCanvas2DBackend();
        backend.Initialize(32, 32);

        backend.Dispose();
        var caps = backend.Capabilities;

        AssertThat(caps.IsGpuBacked).IsFalse();
        AssertThat(caps.SupportsGradients).IsTrue();

        // Path/paint factories must not hand out pooled objects into a drained pool.
        using (var path = backend.CreatePath())
        using (var paint = backend.CreatePaint())
        {
            path.MoveTo(0, 0).LineTo(1, 1);
            paint.SetColor(Colors.Red);
            AssertThat(path).IsNotNull();
        }
    }
}
