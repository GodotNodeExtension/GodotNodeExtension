using System;
using System.Collections.Generic;
using Godot;

namespace GodotNodeExtension.Component.GodotChart.Canvas;

/// <summary>
/// Provides default implementations for convenience methods.
/// Backends must implement the abstract members below; everything else is a shared convenience default.
/// </summary>
public abstract class Canvas2DBase : ICanvas2D
{
    // Transform stack (backends may use this default or implement their own)
    private readonly Stack<Transform2D> _transformStack = new();

    /// <summary>
    /// Accumulated transform of the current save/restore state. The base class tracks the stack; a
    /// backend that does not apply transforms itself reads this to do so.
    /// </summary>
    protected Transform2D CurrentTransform { get; set; } = Transform2D.Identity;

    /// <summary>
    /// Whether this backend needs the base class to mirror the transform stack. A backend that hands
    /// transforms to its own drawing API (<see cref="SkiaCanvas2DBackend"/> calls the canvas) overrides this
    /// with false: the bookkeeping would then be written on every Save/Restore and never read, which is pure
    /// overhead on the drawing path.
    /// </summary>
    protected virtual bool MirrorsTransformsInBase => true;

    /// <inheritdoc />
    public abstract void Resize(int width, int height);
    /// <inheritdoc />
    public abstract void BeginFrame();
    /// <inheritdoc />
    public abstract void EndFrame();
    /// <inheritdoc />
    public abstract void Clear(Color color);
    /// <inheritdoc />
    public abstract IPath2D CreatePath();
    /// <inheritdoc />
    public abstract IPaint2D CreatePaint();
    /// <inheritdoc />
    public abstract void Stroke(IPath2D path, IPaint2D paint);
    /// <inheritdoc />
    public abstract void Fill(IPath2D path, IPaint2D paint);
    /// <inheritdoc />
    public virtual void Tick() => OnTick?.Invoke();

    /// <summary>
    /// Work a host wants run once per frame, invoked by <see cref="Tick"/>. Null (the default) does nothing,
    /// and the built-in backends need nothing: this is the hook for a canvas a host did not create itself (one
    /// that came out of <see cref="Canvas2DFactory"/>), where assigning a delegate is the only way in. A canvas
    /// that owns its type overrides <see cref="Tick"/> instead.
    /// </summary>
    public Action? OnTick { get; set; }
    /// <inheritdoc />
    public abstract CanvasCapabilities Capabilities { get; }

    // ── Convenience methods (shared by all backends; virtual so a backend can do them natively) ──

    /// <inheritdoc />
    public virtual void StrokeAndFill(IPath2D path, IPaint2D strokePaint, IPaint2D fillPaint)
    {
        Fill(path, fillPaint);
        Stroke(path, strokePaint);
    }

    /// <inheritdoc />
    public virtual void DrawLine(float x0, float y0, float x1, float y1, IPaint2D paint)
    {
        using var p = CreatePath();
        p.MoveTo(x0, y0).LineTo(x1, y1);
        Stroke(p, paint);
    }

    /// <inheritdoc />
    public virtual void DrawRect(float x, float y, float w, float h, IPaint2D paint)
    {
        using var p = CreatePath();
        p.Rect(x, y, w, h);
        Stroke(p, paint);
    }

    /// <inheritdoc />
    public virtual void DrawCircle(float cx, float cy, float r, IPaint2D paint)
    {
        using var p = CreatePath();
        p.Circle(cx, cy, r);
        Stroke(p, paint);
    }

#if !DEBUG
    private bool _drawTextWarned;
#endif

    /// <inheritdoc />
    /// <remarks>
    /// The default implementation does not render anything: in DEBUG builds it throws
    /// <see cref="NotImplementedException"/>, while in Release builds it logs a single
    /// <c>GD.PushError</c> (suppressed after the first call). Override to support text.
    /// </remarks>
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
    /// <remarks>This default implementation throws <see cref="NotSupportedException"/>.</remarks>
    public virtual IImageHandle LoadImage(int width, int height, byte[] rgbaPixels)
        => throw new NotSupportedException("This canvas backend does not support images.");

    /// <summary>
    /// Load an image from a Godot Texture2D. Override in backends that support images.
    /// </summary>
    /// <remarks>This default implementation throws <see cref="NotSupportedException"/>.</remarks>
    public virtual IImageHandle LoadImage(Texture2D texture)
        => throw new NotSupportedException("This canvas backend does not support images.");

    /// <summary>
    /// Draw an image at the specified position and size. Override in backends that support images.
    /// </summary>
    /// <remarks>This default implementation is a silent no-op.</remarks>
    public virtual void DrawImage(IImageHandle image, float x, float y,
                                  float dstW, float dstH, float opacity = 1f) { }

    /// <summary>
    /// Copy part of the surface into an image handle. Override in backends that can read their surface back
    /// (and report <c>CanvasCapabilities.SupportsSurfaceCapture</c> when they can).
    /// </summary>
    /// <remarks>This default implementation throws <see cref="NotSupportedException"/>.</remarks>
    public virtual IImageHandle CaptureRegion(int x, int y, int width, int height)
        => throw new NotSupportedException(
            $"{GetType().Name} cannot capture a region of its surface; check " +
            "CanvasCapabilities.SupportsSurfaceCapture before asking for one.");


    /// <inheritdoc />
    public virtual void Save()
    {
        if (MirrorsTransformsInBase) _transformStack.Push(CurrentTransform);
    }

    /// <inheritdoc />
    public virtual void Restore()
    {
        if (!MirrorsTransformsInBase) return;
        if (_transformStack.Count > 0)
            CurrentTransform = _transformStack.Pop();
    }

    /// <inheritdoc />
    public virtual void Translate(float x, float y)
    {
        if (MirrorsTransformsInBase) CurrentTransform = CurrentTransform.Translated(new Vector2(x, y));
    }

    /// <inheritdoc />
    public virtual void Scale(float sx, float sy)
    {
        if (MirrorsTransformsInBase) CurrentTransform = CurrentTransform.Scaled(new Vector2(sx, sy));
    }

    /// <inheritdoc />
    public virtual void Rotate(float angle)
    {
        if (MirrorsTransformsInBase) CurrentTransform = CurrentTransform.Rotated(angle);
    }

    /// <summary>
    /// Default no-op clip. Backends that support clipping override this; the base implementation
    /// keeps the interface usable for backends without clipping (see <c>SupportsClipping</c>).
    /// </summary>
    public virtual void ClipRect(float x, float y, float w, float h) { }

    /// <summary>
    /// Drop every saved state and return to the identity transform. A backend calls this when it repairs a
    /// frame whose <c>Save</c>/<c>Restore</c> pair was broken by an exception, so this bookkeeping cannot
    /// drift away from the backend's own stack.
    /// </summary>
    protected void ResetTransformStack()
    {
        if (!MirrorsTransformsInBase) return;
        _transformStack.Clear();
        CurrentTransform = Transform2D.Identity;
    }

    /// <summary>
    /// Default no-op Dispose. Override in backends that manage native resources.
    /// </summary>
    public virtual void Dispose() => GC.SuppressFinalize(this);

    /// <summary>
    /// True once the canvas has been released. A disposed canvas keeps accepting draw calls and drops them
    /// silently (see the backend), so a host that shares a canvas with another node can check this instead
    /// of wondering why nothing appears.
    /// </summary>
    public virtual bool IsDisposed => false;

    /// <summary>
    /// Measure the rendered dimensions of the given text with the specified font settings.
    /// Must be implemented by concrete backends.
    /// </summary>
    public abstract TextMetrics MeasureText(string text, FontSettings font);

    /// <summary>
    /// The texture this canvas draws into. Backends that render into a texture override this; the
    /// default is null, which means "this canvas has nothing to present".
    /// </summary>
    public virtual Texture2D? Texture => null;
}