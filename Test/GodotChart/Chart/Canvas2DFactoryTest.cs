namespace GodotNodeExtension.Tests.GodotChart;

using System;
using GdUnit4;
using GodotNodeExtension.Component.GodotChart.Canvas;
using GodotNodeExtension.Component.GodotSkia;
using GodotNodeExtension.Tests.Support;
using static GdUnit4.Assertions;

/// <summary>
/// Tests for <see cref="Canvas2DFactory"/>'s contract: which backend a request resolves to, and the
/// failure it reports when it cannot build one.
/// <para>
/// A surface needs a rendering device, so a headless run has none - and that is exactly the case worth
/// pinning: the caller used to receive a bare <c>NullReferenceException</c> from three layers down,
/// which said nothing about the cause or about the alternative (<see cref="Canvas2DControl"/>, which
/// reports the same situation as a warning and keeps the node usable).
/// </para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class Canvas2DFactoryTest
{
    /// <summary>The exception the factory raises, or null when it did not raise one.</summary>
    private static InvalidOperationException? ThrowsInvalidOperation(Action action)
    {
        try { action(); }
        catch (InvalidOperationException ex) { return ex; }
        return null;
    }

    /// <summary>
    /// Without a rendering device the factory fails with an actionable message; with one it builds a
    /// canvas that really is the Skia backend at the requested size, so the check is not a guard that
    /// blocks the working path.
    /// </summary>
    [TestCase]
    public void NoRenderingDeviceIsReportedWithTheCause()
    {
        if (SkiaCanvasTexture2D.HasRenderingDevice)
        {
            using var canvas = Canvas2DFactory.Create(64, 48);
            AssertThat(canvas is SkiaCanvas2DBackend).IsTrue();

            var backend = (SkiaCanvas2DBackend)canvas;
            AssertThat(backend.SkiaTexture.Width).IsEqual(64);
            AssertThat(backend.SkiaTexture.Height).IsEqual(48);
            AssertThat(backend.Texture is not null).IsTrue();
            AssertThat(backend.Texture!.GetWidth()).IsEqual(64);
            AssertThat(backend.Texture.GetHeight()).IsEqual(48);

            // The Skia backend's documented capability set - a factory that handed out a different
            // backend, or a half-initialised one, would show up here.
            var caps = backend.Capabilities;
            AssertThat(caps.SupportsGradients).IsTrue();
            AssertThat(caps.SupportsClipping).IsTrue();
            AssertThat(caps.SupportsTransforms).IsTrue();
            AssertThat(caps.SupportsLineDash).IsTrue();
            AssertThat(caps.SupportsImages).IsTrue();
            return;
        }

        var ex = ThrowsInvalidOperation(() => Canvas2DFactory.Create(64, 48));

        AssertThat(ex is not null).IsTrue();
        AssertThat(ex!.Message.Contains("rendering device")).IsTrue();
        AssertThat(ex.Message.Contains("Canvas2DControl")).IsTrue();
    }

    /// <summary>
    /// The reserved backends (<see cref="CanvasBackendType.Godot"/>, <see cref="CanvasBackendType.Vello"/>)
    /// keep their documented behaviour: they are served by Skia, and the substitution is reported as a
    /// warning - silently switching the backend made callers believe the switch had worked.
    /// </summary>
    [TestCase]
    public void ReservedBackendsWarnAndAreServedBySkia()
    {
        var log = EngineMessageLog.Attach();
        try
        {
            if (SkiaCanvasTexture2D.HasRenderingDevice)
            {
                using (var godot = Canvas2DFactory.Create(32, 32, CanvasBackendType.Godot))
                    AssertThat(godot is SkiaCanvas2DBackend).IsTrue();
                using (var vello = Canvas2DFactory.Create(32, 32, CanvasBackendType.Vello))
                    AssertThat(vello is SkiaCanvas2DBackend).IsTrue();
            }
            else
            {
                // The warning comes before the missing-device failure, so this half is observable headless.
                var ex = ThrowsInvalidOperation(() => Canvas2DFactory.Create(32, 32, CanvasBackendType.Vello));
                AssertThat(ex is not null).IsTrue();
                AssertThat(ex!.Message.Contains("rendering device")).IsTrue();
            }

            // One warning per reserved request: the substitution is never silent.
            int expected = SkiaCanvasTexture2D.HasRenderingDevice ? 2 : 1;
            AssertThat(log.WarningsContaining("is not implemented").Length).IsEqual(expected);
        }
        finally
        {
            log.Detach();
        }
    }

    /// <summary>
    /// <see cref="CanvasBackendType.Auto"/> (and an out-of-range cast) resolve to the working backend
    /// without the reserved-backend warning: the warning belongs to "you asked for something that is not
    /// implemented", not to "pick for me".
    /// </summary>
    [TestCase]
    public void AutoAndOutOfRangeBackendsResolveToSkiaWithoutAWarning()
    {
        var log = EngineMessageLog.Attach();
        try
        {
            if (SkiaCanvasTexture2D.HasRenderingDevice)
            {
                using var auto = Canvas2DFactory.Create(16, 16, CanvasBackendType.Auto);
                AssertThat(auto is SkiaCanvas2DBackend).IsTrue();
                using var bogus = Canvas2DFactory.Create(16, 16, (CanvasBackendType)99);
                AssertThat(bogus is SkiaCanvas2DBackend).IsTrue();
            }
            else
            {
                // The backend choice happens before the surface is built, so the "no warning" half is
                // observable even when the creation itself has to fail.
                AssertThat(ThrowsInvalidOperation(() => Canvas2DFactory.Create(16, 16)) is not null).IsTrue();
                AssertThat(ThrowsInvalidOperation(() => Canvas2DFactory.Create(16, 16, (CanvasBackendType)99)) is not null).IsTrue();
            }

            AssertThat(log.WarningsContaining("is not implemented").Length).IsEqual(0);
        }
        finally
        {
            log.Detach();
        }
    }
}
