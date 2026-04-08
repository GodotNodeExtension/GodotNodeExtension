using System;
using Godot;

namespace GodotNodeExtension.Component.GodotChart.Canvas;

/// <summary>
/// Text horizontal alignment relative to the draw position.
/// </summary>
public enum TextAlign { Left, Center, Right }

/// <summary>
/// Text decoration options (can be combined via flags).
/// </summary>
[Flags]
public enum TextDecoration
{
    None          = 0,
    Underline     = 1,
    Strikethrough = 2,
}

/// <summary>
/// Font configuration for text rendering.
/// Encapsulates font size, style, and advanced typographic settings.
/// </summary>
public readonly record struct FontSettings
{
    /// <summary>Font size in logical pixels. Default 13.</summary>
    public float Size { get; init; }

    /// <summary>
    /// Font family name (e.g. "Arial", "Noto Sans", "Consolas").
    /// Null or empty uses the system/backend default font.
    /// </summary>
    public string? Family { get; init; }

    /// <summary>Whether the text is bold.</summary>
    public bool Bold { get; init; }

    /// <summary>Whether the text is italic.</summary>
    public bool Italic { get; init; }

    /// <summary>Extra spacing between characters in logical pixels. Default 0.</summary>
    public float LetterSpacing { get; init; }

    /// <summary>Line height multiplier (e.g. 1.2 = 120% of font size). Default 1.2.</summary>
    public float LineHeightMultiplier { get; init; }

    /// <summary>Horizontal text alignment relative to the draw position.</summary>
    public TextAlign Align { get; init; }

    /// <summary>Text decoration (underline, strikethrough, or both).</summary>
    public TextDecoration Decoration { get; init; }

    /// <summary>
    /// Godot Font resource for text rendering.
    /// When set, takes priority over <see cref="Family"/> for font selection.
    /// Supports FontFile, FontVariation, and SystemFont.
    /// </summary>
    public Font? GodotFont { get; init; }

    /// <summary>Default font settings: 13px, no bold/italic, left-aligned, no decoration.</summary>
    public static FontSettings Default => new();

    /// <summary>
    /// Creates default font settings (13px, 1.2x line height).
    /// </summary>
    public FontSettings() { Size = 13f; LineHeightMultiplier = 1.2f; }
}

public interface IPath2D : IDisposable
{
    IPath2D MoveTo(float x, float y);
    IPath2D LineTo(float x, float y);
    IPath2D CubicTo(float cx1, float cy1, float cx2, float cy2, float x, float y);
    IPath2D QuadTo(float cx, float cy, float x, float y);
    IPath2D ArcTo(float cx, float cy, float radius,
        float startAngle, float endAngle, bool clockwise = false);
    IPath2D Rect(float x, float y, float w, float h);
    IPath2D RoundRect(float x, float y, float w, float h, float radius);
    IPath2D Circle(float cx, float cy, float radius);
    IPath2D Close();
    IPath2D Reset();
}

public interface IPaint2D : IDisposable
{
    IPaint2D SetColor(Color color);
    IPaint2D SetStrokeWidth(float width);
    IPaint2D SetAntiAlias(bool aa);
    IPaint2D SetLineCap(LineCap cap);
    IPaint2D SetLineJoin(LineJoin join);
    IPaint2D SetMiterLimit(float limit);
    IPaint2D SetLineDash(float[] pattern, float offset = 0f);
    IPaint2D SetOpacity(float alpha);
    // Gradient (optional, depends on backend capabilities)
    IPaint2D SetLinearGradient(float x0, float y0, float x1, float y1,
        GradientStop[] stops);
    IPaint2D SetRadialGradient(float cx, float cy, float radius,
        GradientStop[] stops);
}

/// <summary>
/// Backend-agnostic image handle for use with ICanvas2D.DrawImage.
/// Created via ICanvas2D.LoadImage and should be disposed when no longer needed.
/// </summary>
public interface IImageHandle : IDisposable
{
    /// <summary>Native width of the image in pixels.</summary>
    int Width { get; }

    /// <summary>Native height of the image in pixels.</summary>
    int Height { get; }
}

/// <summary>
/// Measured text dimensions returned by <see cref="ICanvas2D.MeasureText"/>.
/// </summary>
public readonly record struct TextMetrics(float Width, float Height);

public interface ICanvas2D : IDisposable
{
    // Lifecycle
    void Resize(int width, int height);
    void BeginFrame();   // Called at the start of each frame (clear/prepare)
    void EndFrame();     // Called at the end of each frame (commit/flush display)
    void Clear(Color color);

    // Path factory (backend creates concrete path objects)
    IPath2D CreatePath();

    // Paint factory
    IPaint2D CreatePaint();

    // Draw operations
    void Stroke(IPath2D path, IPaint2D paint);
    void Fill(IPath2D path, IPaint2D paint);
    void StrokeAndFill(IPath2D path, IPaint2D strokePaint, IPaint2D fillPaint);

    // Convenience methods (can be implemented by abstract base class)
    void DrawLine(float x0, float y0, float x1, float y1, IPaint2D paint);
    void DrawRect(float x, float y, float w, float h, IPaint2D paint);
    void DrawCircle(float cx, float cy, float r, IPaint2D paint);
    /// <summary>
    /// Draw text at the specified position with font settings and paint.
    /// </summary>
    void DrawText(string text, float x, float y, FontSettings font, IPaint2D paint);

    // Image drawing
    /// <summary>
    /// Load an image from raw RGBA8 pixel data.
    /// The returned handle is backend-specific and must be disposed when no longer needed.
    /// </summary>
    IImageHandle LoadImage(int width, int height, byte[] rgbaPixels);

    /// <summary>
    /// Load an image from a Godot Texture2D. Convenience method for game resources.
    /// </summary>
    IImageHandle LoadImage(Texture2D texture);

    /// <summary>
    /// Draw an image at the specified position and size.
    /// If dstW/dstH differ from the image's native size, the image is scaled.
    /// </summary>
    void DrawImage(IImageHandle image, float x, float y, float dstW, float dstH,
                   float opacity = 1f);

    // Transforms (chart library needs local coordinate transforms)
    void Save();
    void Restore();

    /// <summary>
    /// Returns an <see cref="IDisposable"/> scope that calls <see cref="Save"/>
    /// on creation and <see cref="Restore"/> on disposal, guaranteeing the
    /// transform stack is balanced even when exceptions occur.
    /// </summary>
    IDisposable SaveScope() => new CanvasSaveScope(this);

    void Translate(float x, float y);
    void Scale(float sx, float sy);
    void Rotate(float angle);

    // Clipping
    void ClipRect(float x, float y, float w, float h);

    /// <summary>
    /// Measure the rendered dimensions of the given text with the specified font settings.
    /// Backends with real text metrics should override; default uses a heuristic.
    /// </summary>
    TextMetrics MeasureText(string text, FontSettings font);

    /// <summary>
    /// Called once per frame on the main thread (upload textures, sync state, etc.).
    /// Backends that do not need this can leave it empty.
    /// </summary>
    void Tick();
    
    // Capability query (backend-specific handling)
    CanvasCapabilities Capabilities { get; }
}

/// <summary>
/// Disposable scope that pairs <see cref="ICanvas2D.Save"/> and
/// <see cref="ICanvas2D.Restore"/> calls, ensuring the transform stack
/// is balanced even if an exception is thrown during drawing.
/// </summary>
internal sealed class CanvasSaveScope : IDisposable
{
    private readonly ICanvas2D _canvas;
    public CanvasSaveScope(ICanvas2D canvas) { _canvas = canvas; canvas.Save(); }
    public void Dispose() => _canvas.Restore();
}

/// <summary>Line ending cap style.</summary>
public enum LineCap   { Butt, Round, Square }

/// <summary>Line join style at corners.</summary>
public enum LineJoin  { Miter, Round, Bevel }

/// <summary>
/// A single color stop in a linear or radial gradient.
/// </summary>
public readonly struct GradientStop
{
    /// <summary>Position of this stop within the gradient, in the range [0, 1].</summary>
    public float Position { get; }

    /// <summary>Color at this gradient stop.</summary>
    public Color Color { get; }

    public GradientStop(float pos, Color color) { Position = pos; Color = color; }
}

/// <summary>
/// Describes the capabilities of a canvas backend.
/// </summary>
/// <param name="SupportsGradients">Whether the backend supports linear and radial gradient fills.</param>
/// <param name="SupportsClipping">Whether the backend supports clip regions.</param>
/// <param name="SupportsTransforms">Whether the backend supports affine transforms (translate, rotate, scale).</param>
/// <param name="IsGpuBacked">Whether the backend renders directly on the GPU (no CPU pixel upload).</param>
/// <param name="SupportsLineDash">Whether the backend supports dashed line strokes.</param>
/// <param name="SupportsImages">Whether the backend supports drawing raster images.</param>
public record CanvasCapabilities(
    bool SupportsGradients,
    bool SupportsClipping,
    bool SupportsTransforms,
    bool IsGpuBacked,
    bool SupportsLineDash,
    bool SupportsImages = false
);