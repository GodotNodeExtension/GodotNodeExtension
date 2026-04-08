
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Godot;
using SkiaSharp;

namespace GodotNodeExtension.Component.GodotChart.Canvas;

/// <summary>
/// SkiaSharp-backed implementation of <see cref="IPath2D"/>.
/// </summary>
public class SkiaPath2D : IPath2D
{
    internal readonly SKPath SkPath = new();
    internal Action<SkiaPath2D>? ReturnToPool;

    public IPath2D MoveTo(float x, float y)   { SkPath.MoveTo(x, y); return this; }
    public IPath2D LineTo(float x, float y)   { SkPath.LineTo(x, y); return this; }
    public IPath2D CubicTo(float cx1, float cy1, float cx2, float cy2,
                           float x, float y)
    { SkPath.CubicTo(cx1, cy1, cx2, cy2, x, y); return this; }
    public IPath2D QuadTo(float cx, float cy, float x, float y)
    { SkPath.QuadTo(cx, cy, x, y); return this; }
    public IPath2D ArcTo(float cx, float cy, float r,
                         float startAngle, float endAngle, bool cw = false)
    {
        var rect = new SKRect(cx - r, cy - r, cx + r, cy + r);
        float sweep = cw
            ? -(((startAngle - endAngle) % MathF.Tau + MathF.Tau) % MathF.Tau)
            : ((endAngle - startAngle) % MathF.Tau + MathF.Tau) % MathF.Tau;
        SkPath.ArcTo(rect,
            startAngle * 180f / MathF.PI,
            sweep     * 180f / MathF.PI, false);
        return this;
    }
    public IPath2D Rect(float x, float y, float w, float h)
    { SkPath.AddRect(new SKRect(x, y, x + w, y + h)); return this; }
    public IPath2D RoundRect(float x, float y, float w, float h, float r)
    { SkPath.AddRoundRect(new SKRect(x, y, x + w, y + h), r, r); return this; }
    public IPath2D Circle(float cx, float cy, float r)
    { SkPath.AddCircle(cx, cy, r); return this; }
    public IPath2D Close()  { SkPath.Close(); return this; }
    public IPath2D Reset()  { SkPath.Reset(); return this; }

    public void Dispose()
    {
        if (ReturnToPool != null)
        {
            SkPath.Reset();
            ReturnToPool(this);
        }
        else
        {
            SkPath.Dispose();
        }
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

    public IPaint2D SetColor(Color c)
    {
        _baseAlpha = (byte)(c.A * 255);
        Paint.Color = new SKColor(
            (byte)(c.R * 255), (byte)(c.G * 255),
            (byte)(c.B * 255), _baseAlpha);
        return this;
    }
    public IPaint2D SetStrokeWidth(float w)  { Paint.StrokeWidth = w; return this; }
    public IPaint2D SetAntiAlias(bool aa)    { Paint.IsAntialias = aa; return this; }
    public IPaint2D SetLineCap(LineCap cap)
    {
        Paint.StrokeCap = cap switch {
            LineCap.Round  => SKStrokeCap.Round,
            LineCap.Square => SKStrokeCap.Square,
            _              => SKStrokeCap.Butt,
        };
        return this;
    }
    public IPaint2D SetLineJoin(LineJoin join)
    {
        Paint.StrokeJoin = join switch {
            LineJoin.Round => SKStrokeJoin.Round,
            LineJoin.Bevel => SKStrokeJoin.Bevel,
            _              => SKStrokeJoin.Miter,
        };
        return this;
    }
    public IPaint2D SetMiterLimit(float l)   { Paint.StrokeMiter = l; return this; }
    public IPaint2D SetOpacity(float a)
    {
        // Apply opacity relative to the base alpha set by SetColor,
        // so repeated calls don't cause exponential decay.
        Paint.Color = Paint.Color.WithAlpha((byte)(_baseAlpha * Math.Clamp(a, 0f, 1f)));
        return this;
    }
    public IPaint2D SetLineDash(float[] pattern, float offset = 0f)
    {
        Paint.PathEffect?.Dispose();
        Paint.PathEffect = SKPathEffect.CreateDash(pattern, offset);
        return this;
    }
    public IPaint2D SetLinearGradient(float x0, float y0, float x1, float y1,
                                      GradientStop[] stops)
    {
        var colors    = Array.ConvertAll(stops, s => new SKColor(
            (byte)(s.Color.R*255),(byte)(s.Color.G*255),
            (byte)(s.Color.B*255),(byte)(s.Color.A*255)));
        var positions = Array.ConvertAll(stops, s => s.Position);
        Paint.Shader?.Dispose();
        Paint.Shader = SKShader.CreateLinearGradient(
            new SKPoint(x0, y0), new SKPoint(x1, y1),
            colors, positions, SKShaderTileMode.Clamp);
        return this;
    }
    public IPaint2D SetRadialGradient(float cx, float cy, float r,
                                      GradientStop[] stops)
    {
        var colors    = Array.ConvertAll(stops, s => new SKColor(
            (byte)(s.Color.R*255),(byte)(s.Color.G*255),
            (byte)(s.Color.B*255),(byte)(s.Color.A*255)));
        var positions = Array.ConvertAll(stops, s => s.Position);
        Paint.Shader?.Dispose();
        Paint.Shader = SKShader.CreateRadialGradient(
            new SKPoint(cx, cy), r,
            colors, positions, SKShaderTileMode.Clamp);
        return this;
    }
    public void Dispose()
    {
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
    }
}

/// <summary>
/// SkiaSharp image handle wrapping an SKBitmap.
/// </summary>
internal class SkiaImageHandle : IImageHandle
{
    public SKBitmap Bitmap { get; }
    public int Width => Bitmap.Width;
    public int Height => Bitmap.Height;

    public SkiaImageHandle(SKBitmap bitmap) => Bitmap = bitmap;
    public void Dispose() => Bitmap.Dispose();
}

/// <summary>
/// SkiaSharp backend: async thread rendering, main thread texture upload.
/// Uses dual SKBitmap/SKCanvas to avoid frame-level race conditions:
/// main thread always draws to _drawBitmap/_drawCanvas,
/// render thread always reads from _readBitmap/_readCanvas.
/// References are swapped under lock in EndFrame().
/// </summary>
public partial class SkiaCanvas2DBackend : Canvas2DBase
{
    private int _width, _height;
    private bool _disposed;
    private volatile bool _isDrawing;

    // Dual bitmap/canvas: main thread draws to _draw*, render thread reads from _read*
    private SKBitmap?  _drawBitmap;
    private SKCanvas?  _drawCanvas;
    private SKBitmap?  _readBitmap;
    private SKCanvas?  _readCanvas;

    private ImageTexture  _texture  = null!;
    private Sprite2D      _sprite   = null!;

    // Double-buffered pixel arrays for texture upload
    private byte[] _frontBuf = null!, _backBuf = null!;
    private readonly Lock _lock = new();
    private bool _newFrame;
    private Thread _thread = null!;
    private volatile bool _running = true;
    private readonly SemaphoreSlim _signal = new(0, 1);

    // Cache: Godot Font instance ID → SKTypeface (avoids re-parsing font data every frame)
    private readonly Dictionary<ulong, SKTypeface> _typefaceCache = new();

    // Object pools for reusable Path/Paint objects to reduce GC pressure

    /// <summary>
    /// Maximum number of reusable Path/Paint objects kept in the pool.
    /// Increase for complex charts (e.g. Sankey/Chord with hundreds of paths).
    /// Default is 64.
    /// </summary>
    public int MaxPoolSize { get; init; } = 64;

    private readonly System.Collections.Concurrent.ConcurrentBag<SkiaPath2D> _pathPool = new();
    private readonly System.Collections.Concurrent.ConcurrentBag<SkiaPaint2D> _paintPool = new();

    public override CanvasCapabilities Capabilities => new(
        SupportsGradients:  true,
        SupportsClipping:   true,
        SupportsTransforms: true,
        IsGpuBacked:        false,
        SupportsLineDash:   true,
        SupportsImages:     true
    );

    public void Initialize(Node parent, int w, int h)
    {
        Resize(w, h);
        _texture = new ImageTexture();
        _sprite = new Sprite2D { Texture = _texture, Centered = false,
            TextureFilter = CanvasItem.TextureFilterEnum.Linear };
        parent.AddChild(_sprite);

        _thread = new Thread(RenderLoop) { IsBackground = true };
        _thread.Start();
    }

    public override void Resize(int w, int h)
    {
        if (_disposed) return;

        // Wait for any in-progress frame to finish before replacing canvases,
        // otherwise _frameCanvas (captured at BeginFrame) would point to a disposed canvas.
        var spin = new SpinWait();
        while (_isDrawing && !_disposed) spin.SpinOnce();

        var newDrawBitmap = new SKBitmap(w, h, SKColorType.Rgba8888, SKAlphaType.Premul);
        var newDrawCanvas = new SKCanvas(newDrawBitmap);
        var newReadBitmap = new SKBitmap(w, h, SKColorType.Rgba8888, SKAlphaType.Premul);
        var newReadCanvas = new SKCanvas(newReadBitmap);
        int sz = w * h * 4;
        var newFront = new byte[sz];
        var newBack  = new byte[sz];

        lock (_lock)
        {
            var oldDrawBitmap = _drawBitmap;
            var oldDrawCanvas = _drawCanvas;
            var oldReadBitmap = _readBitmap;
            var oldReadCanvas = _readCanvas;

            _width      = w;
            _height     = h;
            _drawBitmap = newDrawBitmap;
            _drawCanvas = newDrawCanvas;
            _readBitmap = newReadBitmap;
            _readCanvas = newReadCanvas;
            _frontBuf   = newFront;
            _backBuf    = newBack;
            _cachedImage = null;

            oldDrawCanvas?.Dispose();
            oldDrawBitmap?.Dispose();
            oldReadCanvas?.Dispose();
            oldReadBitmap?.Dispose();
        }
    }
    
    public override void Tick() => FlushToTexture();

    // Per-frame canvas reference captured at BeginFrame to guard against
    // _drawCanvas being replaced by a concurrent Resize call.
    private SKCanvas? _frameCanvas;

    /// <summary>
    /// Canvas reference for this drawing frame. Uses <see cref="_frameCanvas"/>
    /// captured at BeginFrame, falling back to <see cref="_drawCanvas"/> for
    /// calls outside a frame pair (e.g. Clear before BeginFrame).
    /// Returns null if the backend has been disposed.
    /// </summary>
    private SKCanvas? ActiveCanvas => _disposed ? null : (_frameCanvas ?? _drawCanvas);

    public override void BeginFrame()
    {
        if (_disposed) return;
        _isDrawing = true;
        // Acquire lock to prevent capturing a stale canvas from a concurrent Resize()
        lock (_lock) { _frameCanvas = _drawCanvas; }
    }

    public override void EndFrame()
    {
        if (_disposed) return;
        Debug.Assert(_isDrawing, "EndFrame called without matching BeginFrame.");
        _isDrawing = false;
        _frameCanvas = null;
        // Swap draw and read surfaces, then signal render thread
        lock (_lock)
        {
            (_drawBitmap, _readBitmap) = (_readBitmap, _drawBitmap);
            (_drawCanvas, _readCanvas) = (_readCanvas, _drawCanvas);
        }
        // Atomic signal: catch instead of check-then-release to avoid TOCTOU race
        try { _signal.Release(); }
        catch (SemaphoreFullException) { /* render thread hasn't consumed previous signal yet — OK */ }
    }

    public override void Clear(Color c)
    {
        ActiveCanvas?.Clear(new SKColor(
            (byte)(c.R*255),(byte)(c.G*255),(byte)(c.B*255),(byte)(c.A*255)));
    }

    public override IPath2D CreatePath()
    {
        if (_pathPool.TryTake(out var pooled))
            return pooled;
        return new SkiaPath2D { ReturnToPool = ReturnPath };
    }

    public override IPaint2D CreatePaint()
    {
        if (_paintPool.TryTake(out var pooled))
            return pooled;
        return new SkiaPaint2D { ReturnToPool = ReturnPaint };
    }

    private void ReturnPath(SkiaPath2D path)
    {
        if (_pathPool.Count < MaxPoolSize)
            _pathPool.Add(path);
        else
            path.SkPath.Dispose();
    }

    private void ReturnPaint(SkiaPaint2D paint)
    {
        if (_paintPool.Count < MaxPoolSize)
            _paintPool.Add(paint);
        else
        {
            paint.ReturnToPool = null;
            paint.Paint.Dispose();
        }
    }

    public override void Stroke(IPath2D path, IPaint2D paint)
    {
        if (path is SkiaPath2D sp && paint is SkiaPaint2D sk)
        {
            sk.Paint.Style = SKPaintStyle.Stroke;
            ActiveCanvas?.DrawPath(sp.SkPath, sk.Paint);
        }
    }

    public override void Fill(IPath2D path, IPaint2D paint)
    {
        if (path is SkiaPath2D sp && paint is SkiaPaint2D sk)
        {
            sk.Paint.Style = SKPaintStyle.Fill;
            ActiveCanvas?.DrawPath(sp.SkPath, sk.Paint);
        }
    }

    public override void DrawText(string text, float x, float y, FontSettings font, IPaint2D paint)
    {
        if (paint is SkiaPaint2D sk)
        {
            var prevStyle = sk.Paint.Style;
            sk.Paint.Style = SKPaintStyle.Fill;
            using var skFont = CreateSkFont(font);
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
                }
            }
            sk.Paint.Style = prevStyle;
        }
    }

    // ── Image support ─────────────────────────────────────────

    /// <summary>
    /// Load an image from raw RGBA8 pixel data into a SkiaSharp bitmap.
    /// </summary>
    public override IImageHandle LoadImage(int width, int height, byte[] rgbaPixels)
    {
        var bmp = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        var span = bmp.GetPixelSpan();
        rgbaPixels.AsSpan(0, Math.Min(rgbaPixels.Length, span.Length)).CopyTo(span);
        return new SkiaImageHandle(bmp);
    }

    /// <summary>
    /// Load an image from a Godot Texture2D by converting to RGBA8 pixel data.
    /// </summary>
    public override IImageHandle LoadImage(Texture2D texture)
    {
        var img = texture.GetImage();
        img.Convert(Image.Format.Rgba8);
        return LoadImage(img.GetWidth(), img.GetHeight(), img.GetData());
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
            using var paint = new SKPaint();
            paint.Color = paint.Color.WithAlpha((byte)(opacity * 255));
            ActiveCanvas?.DrawBitmap(ski.Bitmap, src, dst, paint);
        }
    }

    // Cached image to reduce per-frame GC allocation
    private Image? _cachedImage;

    // Cached dimensions for FlushToTexture to detect resize
    private int _cachedW, _cachedH;

    // ── Async upload (called on main thread, checks each frame) ────────────
    public void FlushToTexture()
    {
        bool hasNew;
        int w, h;
        lock (_lock)
        {
            hasNew = _newFrame;
            if (hasNew) { (_frontBuf, _backBuf) = (_backBuf, _frontBuf); _newFrame = false; }
            w = _width;
            h = _height;
        }
        if (!hasNew) return;

        if (_cachedImage == null || _cachedW != w || _cachedH != h)
        {
            _cachedImage = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
            _cachedW = w;
            _cachedH = h;
        }
        _cachedImage.SetData(w, h, false, Image.Format.Rgba8, _frontBuf);
        if (_texture.GetSize() == Vector2.Zero) _texture.SetImage(_cachedImage);
        else _texture.Update(_cachedImage);
    }

    private void RenderLoop()
    {
        while (_running)
        {
            try
            {
                _signal.Wait();
                if (!_running) break;
                lock (_lock)
                {
                    _readCanvas?.Flush();
                    if (_readBitmap != null)
                    {
                        // Use GetPixelSpan() instead of Bytes to avoid temporary byte[] allocation
                        var px = _readBitmap.GetPixelSpan();
                        if (px.Length > 0)
                        {
                            px.CopyTo(_backBuf.AsSpan());
                            _newFrame = true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                GD.PrintErr($"SkiaCanvas render thread error: {ex}");
            }
        }
    }

    // ── Transform + Clip synchronization with SKCanvas ──────────

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
    /// Measure text dimensions using SkiaSharp text metrics for accurate results.
    /// </summary>
    public override TextMetrics MeasureText(string text, FontSettings font)
    {
        using var skFont = CreateSkFont(font);
        float width = skFont.MeasureText(text);
        if (font.LetterSpacing != 0f && text.Length > 1)
            width += font.LetterSpacing * (text.Length - 1);
        return new TextMetrics(width, font.Size * font.LineHeightMultiplier);
    }

    /// <summary>
    /// Create an SKFont configured from <see cref="FontSettings"/>.
    /// </summary>
    private SKFont CreateSkFont(FontSettings font)
    {
        var typeface = ResolveTypeface(font);
        return new SKFont(typeface, font.Size);
    }

    /// <summary>
    /// Resolve the SKTypeface for the given font settings.
    /// Priority: GodotFont > Family string > system default.
    /// </summary>
    private SKTypeface ResolveTypeface(FontSettings font)
    {
        var weight = font.Bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal;
        var slant  = font.Italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright;

        // Priority 1: Godot Font resource
        if (font.GodotFont is { } godotFont)
            return GetOrCreateTypeface(godotFont, weight, slant);

        // Priority 2: Family name string / system default
        return SKTypeface.FromFamilyName(
            string.IsNullOrEmpty(font.Family) ? null : font.Family,
            weight, SKFontStyleWidth.Normal, slant);
    }

    /// <summary>
    /// Get or create a cached SKTypeface from a Godot Font resource.
    /// Supports FontFile, FontVariation (recursively extracts base font), and SystemFont.
    /// </summary>
    private SKTypeface GetOrCreateTypeface(Font godotFont,
        SKFontStyleWeight weight, SKFontStyleSlant slant)
    {
        ulong key = godotFont.GetInstanceId();
        if (_typefaceCache.TryGetValue(key, out var cached))
            return cached;

        var typeface = CreateTypefaceFromGodotFont(godotFont, weight, slant);
        _typefaceCache[key] = typeface;
        return typeface;
    }

    /// <summary>
    /// Extract font data from a Godot Font and create an SKTypeface.
    /// </summary>
    private static SKTypeface CreateTypefaceFromGodotFont(Font godotFont,
        SKFontStyleWeight weight, SKFontStyleSlant slant)
    {
        // Unwrap FontVariation to reach the underlying FontFile
        var source = godotFont;
        while (source is FontVariation fv)
            source = fv.BaseFont;

        if (source is FontFile ff)
        {
            var data = ff.Data;
            if (data is { Length: > 0 })
            {
                var skData = SKData.CreateCopy(data);
                var tf = SKTypeface.FromData(skData);
                if (tf != null) return tf;
            }
        }

        if (source is SystemFont sf && sf.FontNames.Length > 0)
        {
            return SKTypeface.FromFamilyName(
                sf.FontNames[0], weight, SKFontStyleWidth.Normal, slant);
        }

        // Fallback to default
        return SKTypeface.FromFamilyName(null, weight, SKFontStyleWidth.Normal, slant);
    }

    /// <summary>
    /// Disposes the backend, stopping the render thread and releasing all resources.
    /// </summary>
    public override void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _running = false;
        try { _signal.Release(); } catch (SemaphoreFullException) { }
        _thread.Join(500);

        // Explicitly dispose both canvas/bitmap pairs to prevent native memory leaks.
        // ActiveCanvas only covers one of them; the other could be missed after swap.
        _drawCanvas?.Dispose();
        _drawBitmap?.Dispose();
        _readCanvas?.Dispose();
        _readBitmap?.Dispose();
        _signal.Dispose();

        // Drain and dispose pooled objects to release native SKPath/SKPaint handles
        while (_pathPool.TryTake(out var path))
            path.SkPath.Dispose();
        while (_paintPool.TryTake(out var paint))
        {
            paint.ReturnToPool = null;
            paint.Paint.PathEffect?.Dispose();
            paint.Paint.Shader?.Dispose();
            paint.Paint.Dispose();
        }

        // Release cached typefaces
        foreach (var tf in _typefaceCache.Values)
            tf.Dispose();
        _typefaceCache.Clear();

        // Release Godot objects
        if (GodotObject.IsInstanceValid(_sprite))
        {
            _sprite.GetParent()?.RemoveChild(_sprite);
            _sprite.QueueFree();
        }
        _texture.Dispose();
    }
}