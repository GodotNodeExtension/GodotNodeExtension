
using System;
using System.Collections.Generic;
using System.Threading;
using Godot;
using GodotNodeExtension.Component.GodotSkia;
using SkiaSharp;

namespace GodotNodeExtension.Component.GodotChart.Canvas;

/// <summary>
/// SkiaSharp-backed implementation of <see cref="IPath2D"/>.
/// </summary>
public class SkiaPath2D : IPath2D
{
    internal readonly SKPath SkPath = new();
    internal Action<SkiaPath2D>? ReturnToPool;

    /// <summary>
    /// Set by <see cref="Dispose"/> so a second call is a no-op: without it the same instance would
    /// enter the object pool twice and two callers would then share one native <c>SKPath</c>.
    /// The backend clears the flag when it hands the instance out of the pool again.
    /// </summary>
    internal bool Returned;

    /// <inheritdoc />
    public IPath2D MoveTo(float x, float y)   { SkPath.MoveTo(x, y); return this; }
    /// <inheritdoc />
    public IPath2D LineTo(float x, float y)   { SkPath.LineTo(x, y); return this; }
    /// <inheritdoc />
    public IPath2D CubicTo(float cx1, float cy1, float cx2, float cy2,
                           float x, float y)
    { SkPath.CubicTo(cx1, cy1, cx2, cy2, x, y); return this; }
    /// <inheritdoc />
    public IPath2D QuadTo(float cx, float cy, float x, float y)
    { SkPath.QuadTo(cx, cy, x, y); return this; }
    /// <inheritdoc />
    public IPath2D ArcTo(float cx, float cy, float radius,
                         float startAngle, float endAngle, bool clockwise = false)
    {
        var rect = new SKRect(cx - radius, cy - radius, cx + radius, cy + radius);
        float sweep = clockwise
            ? -(((startAngle - endAngle) % MathF.Tau + MathF.Tau) % MathF.Tau)
            : ((endAngle - startAngle) % MathF.Tau + MathF.Tau) % MathF.Tau;
        SkPath.ArcTo(rect,
            startAngle * 180f / MathF.PI,
            sweep     * 180f / MathF.PI, false);
        return this;
    }
    /// <inheritdoc />
    public IPath2D Rect(float x, float y, float w, float h)
    { SkPath.AddRect(new SKRect(x, y, x + w, y + h)); return this; }
    /// <inheritdoc />
    public IPath2D RoundRect(float x, float y, float w, float h, float radius)
    { SkPath.AddRoundRect(new SKRect(x, y, x + w, y + h), radius, radius); return this; }
    /// <inheritdoc />
    public IPath2D Circle(float cx, float cy, float radius)
    { SkPath.AddCircle(cx, cy, radius); return this; }
    /// <inheritdoc />
    public IPath2D Close()  { SkPath.Close(); return this; }
    /// <inheritdoc />
    public IPath2D Reset()  { SkPath.Reset(); return this; }

    /// <inheritdoc />
    public void Dispose()
    {
        // Idempotent: a second Dispose must neither pool the instance again (the pool would hand the
        // same SKPath to two callers) nor dispose the shared native object.
        if (Returned) return;
        Returned = true;

        if (ReturnToPool != null)
        {
            SkPath.Reset();
            ReturnToPool(this);
        }
        else
        {
            SkPath.Dispose();
        }
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// SkiaSharp-backed implementation of <see cref="IPaint2D"/>.
/// </summary>
public class SkiaPaint2D : IPaint2D
{
    internal readonly SKPaint Paint = new() { IsAntialias = true };
    private byte _baseAlpha = 255;
    internal Action<SkiaPaint2D>? ReturnToPool;

    /// <summary>
    /// Set by <see cref="Dispose"/> so a second call is a no-op: without it the same instance would
    /// enter the object pool twice and two callers would then share one native <c>SKPaint</c>.
    /// The backend clears the flag when it hands the instance out of the pool again.
    /// </summary>
    internal bool Returned;

    /// <inheritdoc />
    public IPaint2D SetColor(Color color)
    {
        _baseAlpha = (byte)(color.A * 255);
        Paint.Color = color.ToSkColor();
        return this;
    }
    /// <inheritdoc />
    public IPaint2D SetStrokeWidth(float width)  { Paint.StrokeWidth = width; return this; }
    /// <inheritdoc />
    public IPaint2D SetAntiAlias(bool aa)    { Paint.IsAntialias = aa; return this; }
    /// <inheritdoc />
    public IPaint2D SetLineCap(LineCap cap)
    {
        Paint.StrokeCap = cap switch {
            LineCap.Round  => SKStrokeCap.Round,
            LineCap.Square => SKStrokeCap.Square,
            _              => SKStrokeCap.Butt,
        };
        return this;
    }
    /// <inheritdoc />
    public IPaint2D SetLineJoin(LineJoin join)
    {
        Paint.StrokeJoin = join switch {
            LineJoin.Round => SKStrokeJoin.Round,
            LineJoin.Bevel => SKStrokeJoin.Bevel,
            _              => SKStrokeJoin.Miter,
        };
        return this;
    }
    /// <inheritdoc />
    public IPaint2D SetMiterLimit(float limit)   { Paint.StrokeMiter = limit; return this; }
    /// <inheritdoc />
    public IPaint2D SetOpacity(float alpha)
    {
        // Apply opacity relative to the base alpha set by SetColor,
        // so repeated calls don't cause exponential decay.
        Paint.Color = Paint.Color.WithAlpha((byte)(_baseAlpha * Math.Clamp(alpha, 0f, 1f)));
        return this;
    }
    /// <inheritdoc />
    public IPaint2D SetLineDash(float[] pattern, float offset = 0f)
    {
        // Build the replacement first: releasing the old effect before the factory ran would leave the
        // paint holding a disposed native object if it threw - and a pooled paint is handed to the next
        // caller, so the damage would outlive this frame.
        var effect = SKPathEffect.CreateDash(pattern, offset);
        Paint.PathEffect?.Dispose();
        Paint.PathEffect = effect;
        return this;
    }
    /// <inheritdoc />
    public IPaint2D SetLinearGradient(float x0, float y0, float x1, float y1,
                                      GradientStop[] stops)
    {
        var colors    = Array.ConvertAll(stops, s => s.Color.ToSkColor());
        var positions = Array.ConvertAll(stops, s => s.Position);
        // Build the replacement first: see SetLineDash.
        var shader = SKShader.CreateLinearGradient(
            new SKPoint(x0, y0), new SKPoint(x1, y1),
            colors, positions, SKShaderTileMode.Clamp);
        Paint.Shader?.Dispose();
        Paint.Shader = shader;
        return this;
    }
    /// <inheritdoc />
    public IPaint2D SetRadialGradient(float cx, float cy, float radius,
                                      GradientStop[] stops)
    {
        var colors    = Array.ConvertAll(stops, s => s.Color.ToSkColor());
        var positions = Array.ConvertAll(stops, s => s.Position);
        // Build the replacement first: see SetLineDash.
        var shader = SKShader.CreateRadialGradient(
            new SKPoint(cx, cy), radius,
            colors, positions, SKShaderTileMode.Clamp);
        Paint.Shader?.Dispose();
        Paint.Shader = shader;
        return this;
    }
    /// <inheritdoc />
    public void Dispose()
    {
        // Idempotent: a second Dispose must neither pool the instance again (the pool would hand the
        // same SKPaint to two callers) nor dispose the shared native object.
        if (Returned) return;
        Returned = true;

        if (ReturnToPool != null)
        {
            // Clean up transient state before returning to pool
            Paint.PathEffect?.Dispose();
            Paint.PathEffect = null;
            Paint.Shader?.Dispose();
            Paint.Shader = null;
            Paint.IsAntialias = true;
            Paint.StrokeWidth = 0f;
            Paint.StrokeCap = SKStrokeCap.Butt;
            Paint.StrokeJoin = SKStrokeJoin.Miter;
            // The value behind SetMiterLimit: without resetting it the next user of the pooled paint
            // inherits a miter limit nobody asked for (the other stroke properties were reset, this one was
            // missed). 4 is Skia's own default.
            Paint.StrokeMiter = 4f;
            Paint.Color = SKColors.Black;
            _baseAlpha = 255;
            ReturnToPool(this);
        }
        else
        {
            Paint.PathEffect?.Dispose();
            Paint.Shader?.Dispose();
            Paint.Dispose();
        }
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// SkiaSharp image handle wrapping an SKBitmap.
/// </summary>
internal sealed class SkiaImageHandle : IImageHandle
{
    public SKBitmap Bitmap { get; }
    public int Width => Bitmap.Width;
    public int Height => Bitmap.Height;

    /// <summary>Set by the first <see cref="Dispose"/>: two owners of one handle must not free it twice.</summary>
    private bool _disposed;

    public SkiaImageHandle(SKBitmap bitmap) => Bitmap = bitmap;

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Bitmap.Dispose();
        // No GC.SuppressFinalize here, unlike the pooled path/paint below: this type is sealed and neither it
        // nor a base declares a finalizer, so CA1816 does not ask for the call - and ReSharper flags it as
        // pointless when it is there (GCSuppressFinalizeForTypeWithoutDestructor, measured 2026-09-21).
    }
}

/// <summary>
/// SkiaSharp backend that renders into a <see cref="SkiaCanvasTexture2D"/>. Supports Vulkan and an
/// automatic CPU fallback for every other renderer.
/// <para>
/// The backend owns no scene-tree node: it only produces a texture. Present it however the host
/// wants - set it on a <see cref="Sprite2D"/> or a <see cref="TextureRect"/>, draw it from a custom
/// <see cref="CanvasItem"/>, or let <see cref="Canvas2DControl"/> do the plumbing. Drawing commands
/// are recorded on the main thread through the <c>SKCanvas</c> of the texture and committed by
/// <see cref="EndFrame"/>.
/// </para>
/// </summary>
public class SkiaCanvas2DBackend : Canvas2DBase
{
    private int _width, _height;
    private bool _disposed;

    // Null until Initialize builds the surface, and again once Dispose released it.
    private SkiaCanvasTexture2D? _skiaTexture;

    // CanvasCapabilities is a record class, so building it on every access - marks read
    // SupportsClipping/SupportsGradients each frame - allocated one object per read. Every flag is
    // fixed for the lifetime of the backend except the GPU one, which flips if the texture falls back
    // to CPU rendering, so the cached record is only rebuilt when that flag differs.
    private CanvasCapabilities? _capabilities;
    private bool _capabilitiesGpuBacked;

    /// <summary>
    /// Paint used by <see cref="DrawImage"/>. Owned by this backend and reused: the old code allocated a
    /// native <c>SKPaint</c> per image element per frame (and left antialiasing off, so scaled images were
    /// jagged).
    /// </summary>
    private readonly SKPaint _imagePaint = new() { IsAntialias = true };

    /// <summary>Save depth of the Skia canvas at <see cref="BeginFrame"/>, to repair an unbalanced frame.</summary>
    private int _frameSaveCount = -1;

    /// <summary>
    /// Whether this backend released its surface. The base class answers <c>false</c> for a backend that owns
    /// no resources of its own, and this one owns a Skia surface - without the override a host that asked
    /// whether the canvas it holds is still usable got "yes" for a disposed backend, and the first drawing call
    /// threw instead.
    /// </summary>
    public override bool IsDisposed => _disposed;

    // Object pools for reusable Path/Paint objects to reduce GC pressure

    /// <summary>
    /// Maximum number of reusable Path/Paint objects kept in the pool, for a host that draws very complex
    /// charts (a sankey or chord with hundreds of paths). Default 64.
    /// <para>
    /// Set it through <see cref="Canvas2DFactory.Create(int, int, CanvasBackendType, int)"/>: the pool belongs
    /// to the surface the factory builds, and nothing in a page reaches the backend itself (this property used
    /// to have no caller outside the tests, so the advice it documents had no way to be followed).
    /// </para>
    /// </summary>
    public int MaxPoolSize { get; init; } = 64;

    // Plain stacks guarded by a lock, deliberately not `ConcurrentBag`: a ConcurrentBag is implemented on
    // top of a `ThreadLocal<WorkStealingQueue>`, and those per-thread slots keep the pooled objects - and
    // through them this backend, the queue arrays and the pool's delegates - reachable *from the thread*.
    // The editor reloads this assembly in place, and a root that lives in the thread means the old load
    // context can never be unloaded (godot#78513); emptying the pool does not help, the container itself
    // has to go. A canvas is driven from one thread, so a lock is enough for the rare concurrent use.
    private readonly Lock _poolLock = new();
    private readonly Stack<SkiaPath2D> _pathPool = new();
    private readonly Stack<SkiaPaint2D> _paintPool = new();

    /// <inheritdoc />
    /// <remarks>
    /// The returned instance is cached: callers must not mutate it, and every access returns the same
    /// object until the GPU flag changes. After <see cref="Dispose"/> the GPU flag reports false
    /// instead of touching the released texture.
    /// </remarks>
    public override CanvasCapabilities Capabilities
    {
        get
        {
            // _skiaTexture is null before Initialize and after Dispose: report a safe default
            // instead of throwing.
            bool gpuBacked = !_disposed && _skiaTexture is { IsGpuMode: true };

            if (_capabilities is { } cached && gpuBacked == _capabilitiesGpuBacked)
                return cached;

            _capabilitiesGpuBacked = gpuBacked;
            _capabilities = new CanvasCapabilities(
                SupportsGradients:  true,
                SupportsClipping:   true,
                SupportsTransforms: true,
                IsGpuBacked:        gpuBacked,
                SupportsLineDash:   true,
                SupportsImages:     true,
                SupportsSurfaceCapture: true
            );
            return _capabilities;
        }
    }

    /// <summary>
    /// The texture this backend renders into, typed as <see cref="SkiaCanvasTexture2D"/> for hosts
    /// that need the Skia-specific surface (e.g. to draw with raw <c>SKCanvas</c> APIs).
    /// <para>
    /// The property is never null: it throws instead, so a host cannot get a handle that silently
    /// dereferences to a released (or never created) surface - use <see cref="Texture"/>, which
    /// returns null for a disposed backend, when a null check is what is wanted.
    /// </para>
    /// </summary>
    /// <exception cref="ObjectDisposedException">
    /// The backend has no surface: it was disposed, or <see cref="Initialize"/> was never called.
    /// </exception>
    internal SkiaCanvasTexture2D SkiaTexture
        => _skiaTexture ?? throw new ObjectDisposedException(
            nameof(SkiaCanvas2DBackend),
            "The backend has no Skia texture: it was disposed or never initialized.");

    /// <inheritdoc />
    public override Texture2D? Texture => _disposed ? null : _skiaTexture;

    /// <summary>
    /// Create the Skia surface and its texture. Nothing is added to the scene tree: presentation is
    /// the host's decision (sprite, UI node, or <see cref="Canvas2DControl"/>).
    /// <para>
    /// Call it once per instance: a second call would replace the surface and leak the texture (and
    /// its GPU RIDs) built by the first one, so it is rejected - use <see cref="Resize"/> to change
    /// the surface size instead. A disposed backend refuses the call as well.
    /// </para>
    /// </summary>
    /// <param name="w">Surface width in pixels.</param>
    /// <param name="h">Surface height in pixels.</param>
    /// <exception cref="InvalidOperationException">The backend has already been initialized.</exception>
    /// <exception cref="ObjectDisposedException">The backend has already been disposed.</exception>
    public void Initialize(int w, int h)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(SkiaCanvas2DBackend),
                "A disposed backend cannot be initialized again.");

        if (_skiaTexture is { } initialized)
            throw new InvalidOperationException(
                $"{nameof(SkiaCanvas2DBackend)} is already initialized " +
                $"({initialized.Width}x{initialized.Height} px); call {nameof(Resize)} to change the " +
                "surface size.");

        // Create the texture before touching the fields: a throwing constructor (no rendering device)
        // then leaves the backend uninitialized and retryable instead of half-configured.
        var texture = new SkiaCanvasTexture2D(w, h);
        _width = w;
        _height = h;
        _skiaTexture = texture;
    }

    /// <summary>
    /// The active SKCanvas from SkiaCanvasTexture2D.
    /// Returns null if the backend has not been initialized or has been disposed.
    /// </summary>
    private SKCanvas? ActiveCanvas => _disposed ? null : _skiaTexture?.Canvas;

    /// <inheritdoc />
    public override void Resize(int width, int height)
    {
        if (_disposed) return;
        if (_skiaTexture is not { } texture) return;   // no surface yet: nothing to resize
        if (_width == width && _height == height) return;

        _width = width;
        _height = height;
        texture.Resize(_width, _height);
    }

    /// <inheritdoc />
    public override void BeginFrame()
    {
        // Accessing Canvas triggers the GPU layout transition if needed, and the save depth is recorded
        // so EndFrame can repair a frame whose renderer threw between Save() and Restore().
        _frameSaveCount = ActiveCanvas?.SaveCount ?? -1;
    }

    /// <inheritdoc />
    public override void EndFrame()
    {
        if (_disposed || _skiaTexture is not { } texture) return;

        // A stage that threw between Save() and its Restore() (Chart.RenderStageSafely keeps going) would
        // leave a transform/clip on the canvas for *every* later frame. Restoring to the recorded depth
        // makes the frame boundary the repair point instead of the next Resize.
        if (_frameSaveCount >= 0 && ActiveCanvas is { } canvas && canvas.SaveCount > _frameSaveCount)
        {
            canvas.RestoreToCount(_frameSaveCount);
            ResetTransformStack();
        }
        _frameSaveCount = -1;

        // Flush rendering commands and update the Godot texture.
        texture.UpdateTexture();
    }

    /// <inheritdoc />
    public override void Clear(Color color)
    {
        ActiveCanvas?.Clear(color.ToSkColor());
    }

    /// <inheritdoc />
    public override IPath2D CreatePath()
    {
        // After Dispose the pool has been drained and must not receive handles again
        // (they would never be released). Hand out a detached object instead.
        if (_disposed) return new SkiaPath2D();

        lock (_poolLock)
        {
            if (_pathPool.TryPop(out var pooled))
            {
                pooled.Returned = false;   // in use again: it may be returned to the pool one more time
                return pooled;
            }
        }

        return new SkiaPath2D { ReturnToPool = ReturnPath };
    }

    /// <inheritdoc />
    public override IPaint2D CreatePaint()
    {
        if (_disposed) return new SkiaPaint2D();

        lock (_poolLock)
        {
            if (_paintPool.TryPop(out var pooled))
            {
                pooled.Returned = false;   // in use again: it may be returned to the pool one more time
                return pooled;
            }
        }

        return new SkiaPaint2D { ReturnToPool = ReturnPaint };
    }

    private void ReturnPath(SkiaPath2D path)
    {
        // A handle created before Dispose can be returned after it: the pool was drained then and
        // must stay empty (anything pushed into it would never be released). Free it right here.
        if (_disposed)
        {
            path.SkPath.Dispose();
            return;
        }

        lock (_poolLock)
        {
            if (_pathPool.Count < MaxPoolSize)
            {
                _pathPool.Push(path);
                return;
            }
        }

        path.SkPath.Dispose();
    }

    private void ReturnPaint(SkiaPaint2D paint)
    {
        // See ReturnPath: after Dispose the pool must stay empty, so the native paint is freed here.
        if (_disposed)
        {
            DisposePaint(paint);
            return;
        }

        lock (_poolLock)
        {
            if (_paintPool.Count < MaxPoolSize)
            {
                _paintPool.Push(paint);
                return;
            }
        }

        DisposePaint(paint);
    }

    /// <summary>
    /// Release the native resources of a paint handle that is not going back into the pool. The
    /// handle must not be used afterwards.
    /// </summary>
    private static void DisposePaint(SkiaPaint2D paint)
    {
        paint.ReturnToPool = null;
        paint.Paint.PathEffect?.Dispose();
        paint.Paint.Shader?.Dispose();
        paint.Paint.Dispose();
    }

    /// <inheritdoc />
    public override void Stroke(IPath2D path, IPaint2D paint)
    {
        if (path is SkiaPath2D sp && paint is SkiaPaint2D sk)
        {
            sk.Paint.Style = SKPaintStyle.Stroke;
            ActiveCanvas?.DrawPath(sp.SkPath, sk.Paint);
        }
        else
        {
            ReportForeignHandle();
        }
    }

    /// <inheritdoc />
    public override void Fill(IPath2D path, IPaint2D paint)
    {
        if (path is SkiaPath2D sp && paint is SkiaPaint2D sk)
        {
            sk.Paint.Style = SKPaintStyle.Fill;
            ActiveCanvas?.DrawPath(sp.SkPath, sk.Paint);
        }
        else
        {
            ReportForeignHandle();
        }
    }

    /// <inheritdoc />
    public override void DrawText(string text, float x, float y, FontSettings font, IPaint2D paint)
    {
        if (paint is SkiaPaint2D sk)
        {
            var prevStyle = sk.Paint.Style;
            var prevStrokeWidth = sk.Paint.StrokeWidth;
            sk.Paint.Style = SKPaintStyle.Fill;
            using var skFont = CreateSkFont(text, font);
            using var blob = SKTextBlob.Create(text, skFont);
            if (blob != null)
            {
                float drawX = font.Align switch
                {
                    TextAlign.Center => x - skFont.MeasureText(text) / 2f,
                    TextAlign.Right  => x - skFont.MeasureText(text),
                    _                => x,
                };
                ActiveCanvas?.DrawText(blob, drawX, y, sk.Paint);

                // Draw text decorations
                if (font.Decoration != TextDecoration.None)
                {
                    float textWidth = skFont.MeasureText(text);

                    // The decorations get their own stroke width instead of the caller's: the paint
                    // handed in is usually a fill paint (StrokeWidth 0, which Skia draws as a hairline
                    // that vanishes on scaled surfaces) or a stroke paint carrying a width meant for
                    // other geometry (which turned the line into a bar). ~7% of the font size is the
                    // usual underline weight, with one pixel as the floor for small text. The caller's
                    // width is restored below, since the paint is sticky and may be reused.
                    sk.Paint.StrokeWidth = MathF.Max(1f, font.Size * 0.07f);

                    // The paint is sticky: a dashed path effect or a gradient shader set by the caller
                    // would turn the underline into a dashed/gradient line, so the decorations draw with a
                    // plain stroke and the caller's state is restored below.
                    var prevPathEffect = sk.Paint.PathEffect;
                    var prevShader = sk.Paint.Shader;
                    sk.Paint.PathEffect = null;
                    sk.Paint.Shader = null;

                    if (font.Decoration.HasFlag(TextDecoration.Underline))
                    {
                        float uy = y + font.Size * 0.15f;
                        ActiveCanvas?.DrawLine(drawX, uy, drawX + textWidth, uy, sk.Paint);
                    }
                    if (font.Decoration.HasFlag(TextDecoration.Strikethrough))
                    {
                        float sy = y - font.Size * 0.3f;
                        ActiveCanvas?.DrawLine(drawX, sy, drawX + textWidth, sy, sk.Paint);
                    }

                    sk.Paint.StrokeWidth = prevStrokeWidth;
                    sk.Paint.PathEffect = prevPathEffect;
                    sk.Paint.Shader = prevShader;
                }
            }
            sk.Paint.Style = prevStyle;
        }
    }

    // ── Image support ─────────────────────────────────────────

    /// <summary>
    /// Load an image from raw RGBA8 pixel data into a SkiaSharp bitmap.
    /// <para>
    /// The buffer must hold the whole image: nothing is truncated or left blank, an undersized buffer
    /// is rejected (the silent <c>Math.Min</c> copy this replaces produced half-drawn images that were
    /// hard to attribute).
    /// </para>
    /// </summary>
    /// <param name="width">Image width in pixels; must be positive.</param>
    /// <param name="height">Image height in pixels; must be positive.</param>
    /// <param name="rgbaPixels">
    /// Row-major RGBA8 pixels (premultiplied alpha), at least <c>width * height * 4</c> bytes; a longer
    /// buffer is accepted and only the first pixels are used.
    /// </param>
    /// <returns>A handle owning a copy of the pixels; the caller disposes it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="rgbaPixels"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="width"/> or <paramref name="height"/> is not positive.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="rgbaPixels"/> is shorter than <c>width * height * 4</c> bytes.
    /// </exception>
    public override IImageHandle LoadImage(int width, int height, byte[] rgbaPixels)
    {
        ArgumentNullException.ThrowIfNull(rgbaPixels);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        // 64-bit math so an absurd size is a rejected buffer rather than an OverflowException.
        long requiredBytes = (long)width * height * 4;
        if (rgbaPixels.Length < requiredBytes)
            throw new ArgumentException(
                $"The pixel buffer holds {rgbaPixels.Length} bytes, but a {width}x{height} RGBA8 " +
                $"image needs {requiredBytes}.", nameof(rgbaPixels));

        var bmp = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        // Rgba8888 bitmaps are created without row padding, so the destination span is exactly
        // width * height * 4 bytes - which the check above guarantees the source covers.
        var span = bmp.GetPixelSpan();
        rgbaPixels.AsSpan(0, span.Length).CopyTo(span);
        return new SkiaImageHandle(bmp);
    }

    /// <summary>
    /// Load an image from a Godot Texture2D by converting to RGBA8 pixel data.
    /// </summary>
    public override IImageHandle LoadImage(Texture2D texture)
    {
        // A texture type without pixel data (a third-party subclass) would otherwise throw a
        // NullReferenceException here; say what happened instead.
        var image = texture.GetImage();
        if (image is null)
            throw new InvalidOperationException(
                $"Texture '{texture}' provides no pixel data (Texture2D.GetImage() returned null).");

        // Shared conversion (M43): ToSKBitmap handles the format conversion and is used by the other
        // components too. Ownership of the bitmap transfers to the returned handle.
        return new SkiaImageHandle(image.ToSkBitmap());
    }

    /// <summary>
    /// Draw an image at the specified position and size using SkiaSharp.
    /// </summary>
    public override void DrawImage(IImageHandle image, float x, float y,
                                   float dstW, float dstH, float opacity = 1f)
    {
        if (image is SkiaImageHandle ski)
        {
            var src = new SKRect(0, 0, ski.Width, ski.Height);
            var dst = new SKRect(x, y, x + dstW, y + dstH);
            byte alpha = (byte)Math.Clamp((int)(opacity * 255f), 0, 255);
            _imagePaint.Color = new SKColor(0, 0, 0, alpha);
            ActiveCanvas?.DrawBitmap(ski.Bitmap, src, dst, _imagePaint);
        }
        else
        {
            ReportForeignHandle();
        }
    }

    /// <summary>
    /// Copy a region of the surface into an image handle, which is what a caller that keeps a rendered layer
    /// and blits it back (<c>Chart.UseLayerCache</c>) captures its layer with.
    /// <para>
    /// The pixels come from the texture this backend renders into, so the GPU and the CPU surface are both
    /// covered by the one path (a ready-made surface readback: <see cref="SkiaCanvasTexture2D"/> takes a
    /// snapshot of its surface for <c>Texture2D.GetImage</c>). Godot images are RGBA8 with straight alpha
    /// while <see cref="LoadImage(int, int, byte[])"/> - and a Skia surface - work with premultiplied
    /// pixels, so the crop and the premultiply happen in the same pass: handing straight pixels to a
    /// premultiplied bitmap would darken every translucent pixel of the cached layer (area fills, the
    /// anti-aliased background edge).
    /// </para>
    /// <para>
    /// The rectangle is clamped to the surface. A handle smaller than requested means the region was
    /// clipped, and the caller has to treat that as "no usable capture" instead of blitting a part of the
    /// layer over the whole chart.
    /// </para>
    /// </summary>
    /// <param name="x">Left edge of the region in surface pixels.</param>
    /// <param name="y">Top edge of the region in surface pixels.</param>
    /// <param name="width">Width of the region in pixels; must be positive.</param>
    /// <param name="height">Height of the region in pixels; must be positive.</param>
    /// <returns>An image handle owning the copied pixels; the caller disposes it.</returns>
    /// <exception cref="ObjectDisposedException">The backend has no surface to capture.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="width"/> or <paramref name="height"/> is not positive, or the rectangle lies outside
    /// the surface.
    /// </exception>
    /// <exception cref="InvalidOperationException">The surface did not come back as RGBA8 pixel data.</exception>
    public override IImageHandle CaptureRegion(int x, int y, int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        if (_disposed || _skiaTexture is not { } texture)
            throw new ObjectDisposedException(nameof(SkiaCanvas2DBackend),
                "The backend has no surface: it was disposed or never initialized.");

        using var surface = texture.GetImage();
        if (surface is null || surface.GetFormat() != Image.Format.Rgba8)
            throw new InvalidOperationException(
                $"{nameof(SkiaCanvas2DBackend)}: the surface did not come back as RGBA8 pixel data.");

        int surfaceW = surface.GetWidth();
        int surfaceH = surface.GetHeight();
        int srcX = Math.Clamp(x, 0, Math.Max(0, surfaceW - 1));
        int srcY = Math.Clamp(y, 0, Math.Max(0, surfaceH - 1));
        int copyW = Math.Min(width, surfaceW - srcX);
        int copyH = Math.Min(height, surfaceH - srcY);
        if (copyW <= 0 || copyH <= 0)
            throw new ArgumentOutOfRangeException(nameof(x),
                $"The region ({x}, {y}, {width}x{height}) lies outside the {surfaceW}x{surfaceH} surface.");

        byte[] pixels = surface.GetData();
        var bitmap = new SKBitmap(copyW, copyH, SKColorType.Rgba8888, SKAlphaType.Premul);
        Span<byte> destination = bitmap.GetPixelSpan();
        for (int row = 0; row < copyH; row++)
        {
            int source = ((srcY + row) * surfaceW + srcX) * 4;
            int target = row * copyW * 4;
            for (int column = 0; column < copyW; column++)
            {
                byte alpha = pixels[source + 3];
                destination[target]     = (byte)(pixels[source]     * alpha / 255);
                destination[target + 1] = (byte)(pixels[source + 1] * alpha / 255);
                destination[target + 2] = (byte)(pixels[source + 2] * alpha / 255);
                destination[target + 3] = alpha;
                source += 4;
                target += 4;
            }
        }

        return new SkiaImageHandle(bitmap);
    }

    /// <summary>
    /// Report a path/paint/image handle that belongs to another backend exactly once. The draw call stays a
    /// no-op, but a silently ignored one is indistinguishable from "this renderer draws nothing".
    /// </summary>
    private void ReportForeignHandle()
    {
        if (_warnedForeignHandle) return;
        _warnedForeignHandle = true;
        GD.PushError(
            $"{nameof(SkiaCanvas2DBackend)}: a handle created by another backend was passed in; the draw " +
            "call is ignored. Create paths, paints and images with this canvas.");
    }

    private bool _warnedForeignHandle;

    // ── Transform + Clip synchronization with SKCanvas ──────────

    /// <summary>
    /// The transforms go to Skia itself (see <see cref="Translate"/>, <see cref="Scale"/>,
    /// <see cref="Rotate"/>), so the base class' mirror of the transform stack would be written on every
    /// Save/Restore and never read. The clip/transform stack this backend repairs in EndFrame is Skia's own
    /// (<c>SKCanvas.SaveCount</c> plus <c>ResetTransformStack</c>).
    /// </summary>
    protected override bool MirrorsTransformsInBase => false;

    /// <inheritdoc />
    public override void Save()
    {
        base.Save();
        ActiveCanvas?.Save();
    }

    /// <inheritdoc />
    public override void Restore()
    {
        base.Restore();
        ActiveCanvas?.Restore();
    }

    /// <inheritdoc />
    public override void Translate(float x, float y)
    {
        base.Translate(x, y);
        ActiveCanvas?.Translate(x, y);
    }

    /// <inheritdoc />
    public override void Scale(float sx, float sy)
    {
        base.Scale(sx, sy);
        ActiveCanvas?.Scale(sx, sy);
    }

    /// <inheritdoc />
    public override void Rotate(float angle)
    {
        base.Rotate(angle);
        ActiveCanvas?.RotateRadians(angle);
    }

    /// <inheritdoc />
    public override void ClipRect(float x, float y, float w, float h)
    {
        ActiveCanvas?.ClipRect(new SKRect(x, y, x + w, y + h));
    }

    /// <summary>
    /// The three primitives go straight to <see cref="SKCanvas"/> instead of building a path, stroking it and
    /// handing the path back to the pool - the grid alone draws one line per tick, so this is the busiest
    /// convenience call there is.
    /// <para>
    /// They keep the base class' contract that these primitives always <b>stroke</b> (the paint's own style is
    /// ignored, and the real-backend case that pins it is
    /// <c>ChartViewRenderIntegrationTest.TheRealBackendStrokesACircleInsteadOfFillingIt</c>), which is why the
    /// style is forced around the call rather than passed through. The caller's paint is left as it was.
    /// </para>
    /// </summary>
    private SKCanvas? BeginStroke(IPaint2D paint, out SKPaint? skPaint, out SKPaintStyle previousStyle)
    {
        skPaint = (paint as SkiaPaint2D)?.Paint;
        previousStyle = SKPaintStyle.Fill;
        if (skPaint is null) return null;

        previousStyle = skPaint.Style;
        skPaint.Style = SKPaintStyle.Stroke;
        return ActiveCanvas;
    }

    /// <summary>Put the paint's own style back after one of the stroking primitives.</summary>
    private static void EndStroke(SKPaint? skPaint, SKPaintStyle previousStyle)
    {
        if (skPaint is not null) skPaint.Style = previousStyle;
    }

    /// <inheritdoc />
    public override void DrawLine(float x0, float y0, float x1, float y1, IPaint2D paint)
    {
        var canvas = BeginStroke(paint, out var skPaint, out var style);
        try
        {
            canvas?.DrawLine(x0, y0, x1, y1, skPaint);
        }
        finally
        {
            EndStroke(skPaint, style);
        }
    }

    /// <inheritdoc />
    public override void DrawRect(float x, float y, float w, float h, IPaint2D paint)
    {
        var canvas = BeginStroke(paint, out var skPaint, out var style);
        try
        {
            canvas?.DrawRect(new SKRect(x, y, x + w, y + h), skPaint);
        }
        finally
        {
            EndStroke(skPaint, style);
        }
    }

    /// <inheritdoc />
    public override void DrawCircle(float cx, float cy, float r, IPaint2D paint)
    {
        var canvas = BeginStroke(paint, out var skPaint, out var style);
        try
        {
            canvas?.DrawCircle(cx, cy, r, skPaint);
        }
        finally
        {
            EndStroke(skPaint, style);
        }
    }

    /// <summary>
    /// Measure text dimensions using SkiaSharp text metrics for accurate results.
    /// </summary>
    public override TextMetrics MeasureText(string text, FontSettings font)
    {
        using var skFont = CreateSkFont(text, font);
        // LetterSpacing is intentionally not added: the text is drawn with a single SKTextBlob and
        // Skia does not apply per-character spacing there, so measuring it would break every
        // alignment computed from the measured width (see FontSettings.LetterSpacing).
        float width = skFont.MeasureText(text);
        return new TextMetrics(width, font.Size * font.LineHeightMultiplier);
    }

    /// <summary>
    /// Create an SKFont configured from <see cref="FontSettings"/>, with a system-font fallback for
    /// glyphs the resolved typeface cannot draw (see <see cref="TypefaceWithFallback"/>). Drawing and
    /// measurement both go through here, so the text width stays consistent with what is drawn.
    /// </summary>
    private static SKFont CreateSkFont(string text, FontSettings font)
    {
        var typeface = TypefaceWithFallback(ResolveTypeface(font), text);
        return SkiaGodotConverter.ConfigureFont(
            new SKFont(typeface, font.Size),
            new SkiaGodotConverter.SkiaFontStyle(font.Bold, font.Italic));
    }

    /// <summary>
    /// A typeface that can actually draw <paramref name="text"/>: when the primary one lacks a glyph
    /// (a Latin-only chart font asked to draw Chinese, for example) a system font that covers it is
    /// used instead. Godot's own text server falls back like this; the raw Skia path did not, which is
    /// why such labels used to render as empty boxes.
    /// <para>
    /// The fallback is per run, not per glyph: a label mixing scripts is drawn with the fallback font
    /// as a whole. That keeps drawing and measurement identical (both come through here), which matters
    /// more for chart geometry than mixed-font typography would.
    /// </para>
    /// </summary>
    internal static SKTypeface TypefaceWithFallback(SKTypeface primary, string text)
    {
        if (string.IsNullOrEmpty(text)) return primary;

        foreach (var rune in text.EnumerateRunes())
        {
            if (primary.ContainsGlyph(rune.Value)) continue;

            var fallback = FallbackTypefaces.GetOrAdd(
                rune.Value, static codepoint => SKFontManager.Default.MatchCharacter(codepoint));
            return fallback ?? primary;
        }
        return primary;
    }

    /// <summary>
    /// Glyph-fallback typefaces, keyed by code point. Never cleared on purpose: the key is a code point and
    /// the value is a typeface owned by Skia's process-wide font manager, so both stay valid across an
    /// assembly reload (unlike the cache keyed by Godot Font IDs, which
    /// <see cref="SkiaCanvasTexture2D.ResetStaticState"/> clears).
    /// </summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, SKTypeface?>
        FallbackTypefaces = new();

    /// <summary>
    /// Resolve the SKTypeface for the given font settings.
    /// Priority: GodotFont > Family string > system default.
    /// </summary>
    private static SKTypeface ResolveTypeface(FontSettings font)
    {
        // Priority 1: Godot Font resource — SkiaGodotConverter's cached conversion
        if (font.GodotFont is { } godotFont)
            return godotFont.ToSkTypeface();

        // Priority 2: family name string / system default — the cache lives in the converter, so
        // there is one family cache for every component (M43).
        var weight = font.Bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal;
        var slant  = font.Italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright;
        string? family = string.IsNullOrEmpty(font.Family) ? null : font.Family;

        return SkiaGodotConverter.GetOrCreateFamilyTypeface(family, weight, slant);
    }

    /// <summary>
    /// Disposes the backend, releasing all resources.
    /// </summary>
    public override void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Drain and dispose pooled objects to release native SKPath/SKPaint handles
        lock (_poolLock)
        {
            while (_pathPool.TryPop(out var path))
                path.SkPath.Dispose();
            while (_paintPool.TryPop(out var paint))
                DisposePaint(paint);
        }

        // Explicitly release GPU RIDs before dropping the reference.
        // Relying on GC → PreDelete at shutdown leaves RIDs leaked.
        var texture = _skiaTexture;
        texture?.ReleaseResources();
        if (texture is not null && GodotObject.IsInstanceValid(texture))
            texture.Dispose();

        // Back to "no surface": Texture reports null and SkiaTexture throws from here on.
        _skiaTexture = null;
        _imagePaint.Dispose();

        // The base Dispose is a no-op today, but calling it keeps derived Dispose chains correct.
        base.Dispose();
        // CA1816 requires this override's own body to call it, even though base.Dispose() already did and the
        // type has no finalizer (measured: removing this line brings the warning back). A no-op at runtime,
        // required by the analyzer set that keeps the build at zero warnings.
        GC.SuppressFinalize(this);
    }
}