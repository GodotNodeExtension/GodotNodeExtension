using System;
using Godot;
using GodotNodeExtension.Component.GodotSkia;

namespace GodotNodeExtension.Component.GodotChart.Canvas;

/// <summary>
/// Available canvas rendering backend types.
/// </summary>
public enum CanvasBackendType
{
    /// <summary>SkiaSharp backend (the current default).</summary>
    Skia,

    /// <summary>
    /// Godot's native vector API (reserved, not implemented): a request for it is served by
    /// <see cref="Skia"/> after a <c>GD.PushWarning</c>, it does not throw.
    /// </summary>
    Godot,

    /// <summary>
    /// GPU compute backend (reserved, long term): a request for it is served by <see cref="Skia"/>
    /// after a <c>GD.PushWarning</c>, it does not throw.
    /// </summary>
    Vello,

    /// <summary>Pick the best available backend (currently always <see cref="Skia"/>).</summary>
    Auto,
}

/// <summary>
/// Factory for canvas backends. A canvas renders into its own texture; it is not attached to the
/// scene tree. Present the texture yourself (sprite / UI node) or host the canvas in a
/// <see cref="Canvas2DControl"/>, which creates, resizes, ticks and presents it for you.
/// </summary>
public static class Canvas2DFactory
{
    /// <summary>
    /// Create a canvas of the given pixel size.
    /// <para>
    /// The reserved backends (<see cref="CanvasBackendType.Godot"/>,
    /// <see cref="CanvasBackendType.Vello"/>) are not implemented: asking for one is reported with a
    /// warning and served by <see cref="CanvasBackendType.Skia"/> - silently substituting another
    /// backend made callers believe the switch had worked.
    /// </para>
    /// </summary>
    /// <param name="width">Surface width in pixels.</param>
    /// <param name="height">Surface height in pixels.</param>
    /// <param name="backend">Backend to use; <see cref="CanvasBackendType.Auto"/> picks the best one.</param>
    /// <returns>The canvas to hand to <see cref="Chart"/>; its <see cref="ICanvas2D.Texture"/> is the
    /// surface the chart draws into. Never null: a failure to create the backend propagates as an
    /// exception.</returns>
    /// <exception cref="InvalidOperationException">The engine has no rendering device (headless run, or
    /// <c>--rendering-driver dummy</c>), so no surface can be created.</exception>
    /// <param name="maxPoolSize">
    /// Upper bound on the backend's pooled paint/path objects (default 64), for a host that draws very complex
    /// charts and wants a bigger pool. It is a parameter of the factory because the pool belongs to the surface
    /// the factory builds: the property used to sit on the backend with no way to reach it from a page.
    /// </param>
    public static ICanvas2D Create(
        int width, int height,
        CanvasBackendType backend = CanvasBackendType.Auto,
        int maxPoolSize = 64)
    {
        // Auto currently means Skia. When the engine grows native vector graphics, this one line is what
        // has to learn about it - the old DetectBestBackend() helper only ever returned the same value.
        var resolved = backend == CanvasBackendType.Auto ? CanvasBackendType.Skia : backend;

        // The reserved backends are not implemented (see the enum members): they are served by the one that
        // works, and saying so is the difference between them and everything else - an out-of-range cast and
        // the already-resolved Auto land on Skia silently, which is what a caller asked for.
        if (resolved is CanvasBackendType.Godot or CanvasBackendType.Vello)
            GD.PushWarning(
                $"{nameof(Canvas2DFactory)}: backend '{resolved}' is not implemented yet; " +
                $"using '{CanvasBackendType.Skia}' instead.");

        return CreateSkia(width, height, maxPoolSize);
    }

    private static SkiaCanvas2DBackend CreateSkia(int w, int h, int maxPoolSize)
    {
        // A surface needs a rendering device. Without one (a headless run, or --rendering-driver dummy)
        // the low-level failure would reach the caller with no hint about what to do, so it is reported
        // here with the chart-facing alternative: a Canvas2DControl node turns it into a warning and
        // keeps the node alive.
        if (!SkiaCanvasTexture2D.HasRenderingDevice)
            throw new InvalidOperationException(
                $"{nameof(Canvas2DFactory)}: no rendering device is available (headless run, or " +
                "--rendering-driver dummy), so no canvas can be created. A Canvas2DControl node logs " +
                "this as a warning and stays usable; a direct Create call has to handle it.");

        var backend = new SkiaCanvas2DBackend { MaxPoolSize = Math.Max(1, maxPoolSize) };
        try
        {
            backend.Initialize(w, h);
        }
        catch
        {
            // Initialize builds the surface and its texture; when that throws (no rendering device,
            // GPU surface creation failed) the half-built backend still owns native resources, so
            // they are released before the exception is let out.
            backend.Dispose();
            throw;
        }
        return backend;
    }
}
