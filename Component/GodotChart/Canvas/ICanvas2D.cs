using System;
using Godot;

namespace GodotNodeExtension.Component.GodotChart.Canvas;

/// <summary>
/// Text horizontal alignment relative to the draw position.
/// </summary>
public enum TextAlign
{
    /// <summary>Text starts at the draw position and extends to the right.</summary>
    Left,

    /// <summary>Text is centred horizontally on the draw position.</summary>
    Center,

    /// <summary>Text ends at the draw position (extends to the left).</summary>
    Right,
}

/// <summary>
/// Text decoration options (can be combined via flags).
/// </summary>
[Flags]
public enum TextDecoration
{
    /// <summary>No decoration.</summary>
    None          = 0,

    /// <summary>Draw a line under the text.</summary>
    Underline     = 1,

    /// <summary>Draw a line through the text.</summary>
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

    /// <summary>
    /// Extra spacing between characters in logical pixels. Default 0.
    /// <para>
    /// Not implemented by the Skia backend: text is drawn as a single run, so this value is ignored
    /// by both drawing and measurement (measurement ignoring it is deliberate — otherwise centred
    /// and right-aligned text would be offset by the unused spacing).
    /// </para>
    /// </summary>
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

/// <summary>
/// Backend-agnostic path builder. Instances are created by <see cref="ICanvas2D.CreatePath"/> and
/// must be disposed; backends may recycle them (see <see cref="Reset"/>).
/// </summary>
public interface IPath2D : IDisposable
{
    /// <summary>Start a new sub-path at the given point.</summary>
    IPath2D MoveTo(float x, float y);

    /// <summary>Add a straight line from the current point to the given point.</summary>
    IPath2D LineTo(float x, float y);

    /// <summary>Add a cubic Bézier curve with two control points.</summary>
    IPath2D CubicTo(float cx1, float cy1, float cx2, float cy2, float x, float y);

    /// <summary>Add a quadratic Bézier curve with one control point.</summary>
    IPath2D QuadTo(float cx, float cy, float x, float y);

    /// <summary>
    /// Add a circular arc around (<paramref name="cx"/>, <paramref name="cy"/>).
    /// <para>
    /// A sweep of exactly 2π is <b>not</b> usable: angles are taken modulo a full turn, so a complete
    /// circle collapses to an empty arc. Pass <c>2π - ε</c> when a full turn is intended.
    /// </para>
    /// </summary>
    IPath2D ArcTo(float cx, float cy, float radius,
        float startAngle, float endAngle, bool clockwise = false);

    /// <summary>Add an axis-aligned rectangle.</summary>
    IPath2D Rect(float x, float y, float w, float h);

    /// <summary>Add a rectangle with rounded corners.</summary>
    IPath2D RoundRect(float x, float y, float w, float h, float radius);

    /// <summary>Add a full circle.</summary>
    IPath2D Circle(float cx, float cy, float radius);

    /// <summary>Close the current sub-path back to its start point.</summary>
    IPath2D Close();

    /// <summary>
    /// Discard the current geometry so the instance can be reused for the next shape instead of
    /// allocating a new path (marks use this on their shared path object).
    /// </summary>
    IPath2D Reset();
}

/// <summary>
/// Backend-agnostic paint state (colour, stroke, shader). Instances are created by
/// <see cref="ICanvas2D.CreatePaint"/> and must be disposed; state is sticky, so callers must set
/// every property they rely on.
/// </summary>
public interface IPaint2D : IDisposable
{
    /// <summary>Set the fill/stroke colour.</summary>
    IPaint2D SetColor(Color color);

    /// <summary>Set the stroke width in logical pixels.</summary>
    IPaint2D SetStrokeWidth(float width);

    /// <summary>Enable or disable anti-aliasing.</summary>
    IPaint2D SetAntiAlias(bool aa);

    /// <summary>Set how stroke ends are drawn.</summary>
    IPaint2D SetLineCap(LineCap cap);

    /// <summary>Set how stroke corners are joined.</summary>
    IPaint2D SetLineJoin(LineJoin join);

    /// <summary>Set the miter limit used by <see cref="LineJoin.Miter"/>.</summary>
    IPaint2D SetMiterLimit(float limit);

    /// <summary>Set a dash pattern (dash lengths) and the offset into it.</summary>
    IPaint2D SetLineDash(float[] pattern, float offset = 0f);

    /// <summary>Set the paint opacity in [0, 1] (multiplied with the colour alpha).</summary>
    IPaint2D SetOpacity(float alpha);

    /// <summary>Use a linear gradient shader; requires <c>Capabilities.SupportsGradients</c>.</summary>
    IPaint2D SetLinearGradient(float x0, float y0, float x1, float y1,
        GradientStop[] stops);

    /// <summary>Use a radial gradient shader; requires <c>Capabilities.SupportsGradients</c>.</summary>
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

/// <summary>
/// Backend-agnostic drawing surface used by every renderer and mark. The chart library never talks
/// to Skia (or any other rasteriser) directly, so a custom backend only has to implement this
/// interface.
/// </summary>
public interface ICanvas2D : IDisposable
{
    /// <summary>Resize the backing surface. Callers must re-render afterwards.</summary>
    void Resize(int width, int height);

    /// <summary>Begin a frame (prepare backend state). Paired with <see cref="EndFrame"/>.</summary>
    void BeginFrame();

    /// <summary>End a frame: commit/flush everything drawn since <see cref="BeginFrame"/>.</summary>
    void EndFrame();

    /// <summary>Clear the whole surface with the given colour.</summary>
    void Clear(Color color);

    /// <summary>Create a path builder. Dispose it when done (or reuse it via <see cref="IPath2D.Reset"/>).</summary>
    IPath2D CreatePath();

    /// <summary>Create a paint state object. Dispose it when done.</summary>
    IPaint2D CreatePaint();

    /// <summary>Stroke the path with the paint.</summary>
    void Stroke(IPath2D path, IPaint2D paint);

    /// <summary>Fill the path with the paint.</summary>
    void Fill(IPath2D path, IPaint2D paint);

    /// <summary>
    /// Fill the path and stroke its outline. The two paints are separate, so the default implementation
    /// fills and then strokes (two rasterization passes); a backend whose native API can do both with one
    /// paint may merge them.
    /// </summary>
    void StrokeAndFill(IPath2D path, IPaint2D strokePaint, IPaint2D fillPaint);

    /// <summary>Draw a straight line segment.</summary>
    void DrawLine(float x0, float y0, float x1, float y1, IPaint2D paint);

    /// <summary>Stroke the outline of an axis-aligned rectangle; this variant always strokes and never fills. Use <see cref="Fill"/> with a <c>CreatePath().Rect()</c> path to paint a filled rectangle.</summary>
    void DrawRect(float x, float y, float w, float h, IPaint2D paint);

    /// <summary>Stroke the outline of a circle of radius <paramref name="r"/>; this variant never fills. Use <see cref="Fill"/> with a <c>CreatePath().Circle()</c> path to paint a filled disc.</summary>
    void DrawCircle(float cx, float cy, float r, IPaint2D paint);
    /// <summary>
    /// Draw text at the specified position with font settings and paint.
    /// <para>
    /// Text is drawn as a single run: a newline is not treated as a line break, so callers must
    /// split multi-line content themselves (see <c>TooltipRenderer</c>, which measures the same
    /// lines it draws). <paramref name="y"/> is the baseline.
    /// </para>
    /// </summary>
    /// <remarks>The default <see cref="Canvas2DBase"/> implementation does not render text: it throws in DEBUG builds and logs a single error in Release builds.</remarks>
    void DrawText(string text, float x, float y, FontSettings font, IPaint2D paint);

    // Image drawing
    /// <summary>
    /// Load an image from raw RGBA8 pixel data. Pixels are expected in RGBA byte order with
    /// premultiplied alpha (the default Skia backend reads them as Rgba8888/Premul).
    /// The returned handle is backend-specific and must be disposed when no longer needed.
    /// </summary>
    /// <remarks>The default <see cref="Canvas2DBase"/> implementation throws <see cref="System.NotSupportedException"/>.</remarks>
    IImageHandle LoadImage(int width, int height, byte[] rgbaPixels);

    /// <summary>
    /// Load an image from a Godot Texture2D. Convenience method for game resources.
    /// </summary>
    /// <remarks>The default <see cref="Canvas2DBase"/> implementation throws <see cref="System.NotSupportedException"/>.</remarks>
    IImageHandle LoadImage(Texture2D texture);

    /// <summary>
    /// Draw an image at the specified position and size.
    /// If dstW/dstH differ from the image's native size, the image is scaled.
    /// </summary>
    /// <remarks>The default <see cref="Canvas2DBase"/> implementation is a silent no-op.</remarks>
    void DrawImage(IImageHandle image, float x, float y, float dstW, float dstH,
                   float opacity = 1f);

    /// <summary>
    /// Copy part of the surface into an image handle, for a caller that keeps a rendered layer and hands it
    /// back to <see cref="DrawImage"/> later - the layer cache of <c>Chart.UseLayerCache</c> is what does this.
    /// <para>
    /// The pixels are the ones the surface holds <b>at the moment of the call</b>, so the caller asks for the
    /// region it just drew, before it draws anything on top of it. The returned handle owns its pixels, is
    /// drawn by <see cref="DrawImage"/> like any other image of this backend, and must be disposed by the
    /// caller. The rectangle is clamped to the surface: a handle smaller than requested means the region was
    /// clipped, which the caller has to treat as "no usable capture".
    /// </para>
    /// </summary>
    /// <param name="x">Left edge of the region in surface pixels.</param>
    /// <param name="y">Top edge of the region in surface pixels.</param>
    /// <param name="width">Width of the region in pixels; must be positive.</param>
    /// <param name="height">Height of the region in pixels; must be positive.</param>
    /// <remarks>
    /// The default <see cref="Canvas2DBase"/> implementation throws <see cref="NotSupportedException"/>.
    /// Check <see cref="CanvasCapabilities.SupportsSurfaceCapture"/> before calling: a backend that reports
    /// false has to be driven as "render every layer every frame".
    /// </remarks>
    IImageHandle CaptureRegion(int x, int y, int width, int height);

    /// <summary>Push the current transform/clip state onto the stack.</summary>
    void Save();

    /// <summary>Pop the transform/clip state pushed by <see cref="Save"/>.</summary>
    void Restore();

    /// <summary>
    /// Returns an <see cref="IDisposable"/> scope that calls <see cref="Save"/>
    /// on creation and <see cref="Restore"/> on disposal, guaranteeing the
    /// transform stack is balanced even when exceptions occur.
    /// </summary>
    IDisposable SaveScope() => new CanvasSaveScope(this);

    /// <summary>Translate the coordinate system by the given offset.</summary>
    void Translate(float x, float y);

    /// <summary>Scale the coordinate system.</summary>
    void Scale(float sx, float sy);

    /// <summary>Rotate the coordinate system by the given angle in radians.</summary>
    void Rotate(float angle);

    /// <summary>
    /// Intersect the clip region with the given rectangle (in current coordinates).
    /// Only meaningful after <see cref="Save"/>; <see cref="Restore"/> undoes it.
    /// </summary>
    void ClipRect(float x, float y, float w, float h);

    /// <summary>
    /// Report the rectangle this frame changed, so a backend may upload only that part of its surface. Several
    /// calls in one frame widen the region; a frame that reports nothing is uploaded whole, which is what keeps
    /// a backend that ignores this call correct.
    /// </summary>
    /// <param name="x">Left edge in surface pixels.</param>
    /// <param name="y">Top edge in surface pixels.</param>
    /// <param name="w">Width in pixels.</param>
    /// <param name="h">Height in pixels.</param>
    void InvalidateRegion(float x, float y, float w, float h);

    /// <summary>
    /// Measure the rendered dimensions of the given text with the specified font settings.
    /// There is no default implementation: every backend must provide its own text metrics.
    /// <para>
    /// Like <see cref="DrawText"/> this measures a single run: embedded newlines are not counted as
    /// extra lines, so multi-line callers must split first and measure the widest line.
    /// </para>
    /// </summary>
    TextMetrics MeasureText(string text, FontSettings font);

    /// <summary>
    /// Optional per-frame hook (upload textures, sync state, etc.). The library itself never calls
    /// it; when needed, the host must invoke it on the main thread once per frame.
    /// <see cref="Canvas2DControl"/> does that for you. The built-in backends leave it empty.
    /// </summary>
    void Tick();

    /// <summary>
    /// The texture this canvas draws into, or null when the backend does not produce one (a headless
    /// or test double, for example).
    /// <para>
    /// The canvas never puts it into the scene tree: the host owns presentation and can hand the
    /// texture to a <see cref="Sprite2D"/>, a <see cref="TextureRect"/>, a custom
    /// <see cref="CanvasItem"/>, or use <see cref="Canvas2DControl"/> which does the plumbing.
    /// The texture is only up to date after <see cref="EndFrame"/>.
    /// </para>
    /// </summary>
    Texture2D? Texture { get; }

    /// <summary>
    /// What this backend supports. Callers must check the flags before using optional features
    /// (gradients, clipping, images) and degrade gracefully.
    /// </summary>
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
    private bool _disposed;

    /// <summary>Push the canvas state (see <see cref="ICanvas2D.Save"/>).</summary>
    public CanvasSaveScope(ICanvas2D canvas) { _canvas = canvas; canvas.Save(); }

    /// <summary>
    /// Pop the state pushed by the constructor (see <see cref="ICanvas2D.Restore"/>).
    /// <para>
    /// Idempotent: a second Dispose - a double <c>using</c>, or a manual Dispose in a finally block
    /// after the using - is a no-op. Without this guard it popped a second, unrelated Save, leaving
    /// the canvas one level short; <see cref="Canvas2DBase.Restore"/> ignores an empty stack, so that
    /// imbalance stayed silent.
    /// </para>
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _canvas.Restore();
    }
}

/// <summary>Line ending cap style.</summary>
public enum LineCap
{
    /// <summary>End the stroke exactly at the endpoint.</summary>
    Butt,

    /// <summary>Extend the stroke with a half-circle.</summary>
    Round,

    /// <summary>Extend the stroke by half its width.</summary>
    Square,
}

/// <summary>Line join style at corners.</summary>
public enum LineJoin
{
    /// <summary>Extend the outer edges until they meet (mitre limit applies).</summary>
    Miter,

    /// <summary>Round the corner.</summary>
    Round,

    /// <summary>Cut the corner off flat.</summary>
    Bevel,
}

/// <summary>
/// A single color stop in a linear or radial gradient.
/// </summary>
public readonly struct GradientStop
{
    /// <summary>Position of this stop within the gradient, in the range [0, 1].</summary>
    public float Position { get; }

    /// <summary>Color at this gradient stop.</summary>
    public Color Color { get; }

    /// <summary>Create a gradient stop at <paramref name="pos"/> ([0, 1]) with the given colour.</summary>
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
/// <param name="SupportsSurfaceCapture">
/// Whether the backend can read a region of its surface back into an image handle
/// (<see cref="ICanvas2D.CaptureRegion"/>), which is what a layered renderer needs to keep a layer.
/// </param>
public record CanvasCapabilities(
    bool SupportsGradients,
    bool SupportsClipping,
    bool SupportsTransforms,
    bool IsGpuBacked,
    bool SupportsLineDash,
    bool SupportsImages = false,
    bool SupportsSurfaceCapture = false
);