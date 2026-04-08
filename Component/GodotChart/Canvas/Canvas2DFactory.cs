using Godot;

namespace GodotNodeExtension.Component.GodotChart.Canvas;

/// <summary>
/// Available canvas rendering backend types.
/// </summary>
public enum CanvasBackendType
{
    Skia,       // Current default
    Godot,      // Official native (future)
    Vello,      // GPU Compute (long-term)
    Auto,       // Auto-select optimal
}

public static class Canvas2DFactory
{
    public static ICanvas2D Create(
        Node parent,
        int width, int height,
        CanvasBackendType backend = CanvasBackendType.Auto)
    {
        var resolved = backend == CanvasBackendType.Auto
            ? DetectBestBackend()
            : backend;

        return resolved switch
        {
            CanvasBackendType.Skia => CreateSkia(parent, width, height),
            // CanvasBackendType.Godot  => CreateGodotNative(...),
            // CanvasBackendType.Vello  => CreateVello(...),
            _ => CreateSkia(parent, width, height),
        };
    }

    private static CanvasBackendType DetectBestBackend()
    {
        // Future: detect if Godot version has built-in vector graphics support
        // if (GodotVersion.HasNativeVectorGraphics) return CanvasBackendType.Godot;
        return CanvasBackendType.Skia;
    }

    private static ICanvas2D CreateSkia(Node parent, int w, int h)
    {
        var backend = new SkiaCanvas2DBackend();
        backend.Initialize(parent, w, h);
        return backend;
    }
}