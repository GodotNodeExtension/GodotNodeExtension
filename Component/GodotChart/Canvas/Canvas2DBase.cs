using System;
using System.Collections.Generic;
using Godot;

namespace GodotNodeExtension.Component.GodotChart.Canvas;

/// <summary>
/// Provides default implementations for convenience methods.
/// Backends only need to implement core Stroke/Fill.
/// </summary>
public abstract class Canvas2DBase : ICanvas2D
{
    // Transform stack (backends may use this default or implement their own)
    private readonly Stack<Transform2D> _transformStack = new();
    protected Transform2D CurrentTransform = Transform2D.Identity;

    public abstract void Resize(int width, int height);
    public abstract void BeginFrame();
    public abstract void EndFrame();
    public abstract void Clear(Color color);
    public abstract IPath2D CreatePath();
    public abstract IPaint2D CreatePaint();
    public abstract void Stroke(IPath2D path, IPaint2D paint);
    public abstract void Fill(IPath2D path, IPaint2D paint);
    public virtual void Tick() { }
    public abstract CanvasCapabilities Capabilities { get; }

    // ── Convenience methods (shared by all backends, no need to reimplement) ─────────────────

    public void StrokeAndFill(IPath2D path, IPaint2D strokePaint, IPaint2D fillPaint)
    {
        Fill(path, fillPaint);
        Stroke(path, strokePaint);
    }

    public void DrawLine(float x0, float y0, float x1, float y1, IPaint2D paint)
    {
        using var p = CreatePath();
        p.MoveTo(x0, y0).LineTo(x1, y1);
        Stroke(p, paint);
    }

    public void DrawRect(float x, float y, float w, float h, IPaint2D paint)
    {
        using var p = CreatePath();
        p.Rect(x, y, w, h);
        Stroke(p, paint);
    }

    public void DrawCircle(float cx, float cy, float r, IPaint2D paint)
    {
        using var p = CreatePath();
        p.Circle(cx, cy, r);
        Stroke(p, paint);
    }

#if !DEBUG
    private bool _drawTextWarned;
#endif

    public virtual void DrawText(string text, float x, float y, FontSettings font, IPaint2D paint)
    {
#if DEBUG
        throw new NotImplementedException(
            $"{GetType().Name}: DrawText is not implemented. " +
            "Override this method in your canvas backend to support text rendering.");
#else
        // Use PushError (not PushWarning) because text rendering is a core chart feature.
        // Suppressed after first call to avoid log flooding.
        if (!_drawTextWarned)
        {
            GD.PushError($"{GetType().Name}: DrawText is not implemented. " +
                "Chart labels and text will not be rendered. " +
                "Override DrawText in your canvas backend.");
            _drawTextWarned = true;
        }
#endif
    }


    /// <summary>
    /// Load an image from raw RGBA8 pixel data. Override in backends that support images.
    /// </summary>
    public virtual IImageHandle LoadImage(int width, int height, byte[] rgbaPixels)
        => throw new NotSupportedException("This canvas backend does not support images.");

    /// <summary>
    /// Load an image from a Godot Texture2D. Override in backends that support images.
    /// </summary>
    public virtual IImageHandle LoadImage(Texture2D texture)
        => throw new NotSupportedException("This canvas backend does not support images.");

    /// <summary>
    /// Draw an image at the specified position and size. Override in backends that support images.
    /// </summary>
    public virtual void DrawImage(IImageHandle image, float x, float y,
                                  float dstW, float dstH, float opacity = 1f) { }


    public virtual void Save()
    {
        _transformStack.Push(CurrentTransform);
    }

    public virtual void Restore()
    {
        if (_transformStack.Count > 0)
            CurrentTransform = _transformStack.Pop();
    }

    public virtual void Translate(float x, float y)
    {
        CurrentTransform = CurrentTransform.Translated(new Vector2(x, y));
    }

    public virtual void Scale(float sx, float sy)
    {
        CurrentTransform = CurrentTransform.Scaled(new Vector2(sx, sy));
    }

    public virtual void Rotate(float angle)
    {
        CurrentTransform = CurrentTransform.Rotated(angle);
    }

    public virtual void ClipRect(float x, float y, float w, float h) { }

    /// <summary>
    /// Default no-op Dispose. Override in backends that manage native resources.
    /// </summary>
    public virtual void Dispose() { }

    /// <summary>
    /// Measure the rendered dimensions of the given text with the specified font settings.
    /// Must be implemented by concrete backends.
    /// </summary>
    public abstract TextMetrics MeasureText(string text, FontSettings font);
}