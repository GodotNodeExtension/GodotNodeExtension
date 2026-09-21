namespace GodotNodeExtension.Tests.GodotChart.Support;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using GodotNodeExtension.Component.GodotChart.Canvas;

/// <summary>
/// Minimal <see cref="ICanvas2D"/> test double that records what a mark drew and flags broken
/// geometry (non-finite coordinates, negative-size rectangles).
/// It never touches Skia or the GPU, so marks and renderers can be exercised headless
/// without a scene tree.
/// </summary>
public sealed class FakeCanvas2D : ICanvas2D
{
    /// <summary>A single recorded <see cref="DrawText"/> call.</summary>
    public readonly record struct TextDraw(
        string Text, float X, float Y, TextAlign Align, FontSettings Font, Color Color);

    /// <summary>Number of Fill() calls.</summary>
    public int FillCount { get; private set; }

    /// <summary>Number of Stroke() calls.</summary>
    public int StrokeCount { get; private set; }

    /// <summary>Number of path construction operations (MoveTo/LineTo/Rect/...).</summary>
    public int PathOpCount { get; private set; }

    /// <summary>
    /// Number of coordinates registered as NaN or infinity. A mark must never emit those: they
    /// mean geometry was computed from a missing value or from an empty domain.
    /// </summary>
    public int NonFiniteCoordinateCount { get; private set; }

    /// <summary>
    /// Number of rectangles registered with a negative width or height. Such a rect renders as
    /// nothing (or worse), so it always indicates a geometry bug in the mark.
    /// </summary>
    public int NegativeSizeRectCount { get; private set; }

    /// <summary>Texts passed to DrawText, in order.</summary>
    public List<string> Texts { get; } = [];

    /// <summary>DrawText calls with position and alignment.</summary>
    public List<TextDraw> TextDraws { get; } = [];

    /// <summary>Colors of the recorded Fill() calls, in order.</summary>
    public List<Color> FillColors { get; } = [];

    /// <summary>
    /// Opacity applied to the paint of each Fill(), in the same order as <see cref="FillColors"/>.
    /// Lets a test assert that an alpha option actually reached the canvas.
    /// </summary>
    public List<float> FillOpacities { get; } = [];

    /// <summary>Colors of the recorded Stroke() calls, in order.</summary>
    public List<Color> StrokeColors { get; } = [];

    /// <summary>Stroke width of the recorded Stroke() calls, in the same order as <see cref="StrokeColors"/>.</summary>
    public List<float> StrokeWidths { get; } = [];

    /// <summary>Stored stroke width of every <c>DrawLine</c> call, in call order.</summary>
    public List<float> LineWidths { get; } = [];

    /// <summary>Lines drawn with <see cref="DrawLine"/>: (x0, y0, x1, y1, colour).</summary>
    public List<(float X0, float Y0, float X1, float Y1, Color Color)> Lines { get; } = [];

    /// <summary>Rounded rects: (X, Y, W, H, Radius). Also recorded in <see cref="Rects"/>.</summary>
    public List<(float X, float Y, float W, float H, float Radius)> RoundRects { get; } = [];

    /// <summary>Number of RoundRect() path calls (rounded rectangles), i.e. corner radii that were applied.</summary>
    public int RoundRectCount { get; private set; }

    /// <summary>Number of CubicTo() path calls (curve segments; distinguishes smoothing from straight lines).</summary>
    public int CubicCount { get; private set; }

    /// <summary>Images drawn: (X, Y, Width, Height, Opacity), in order.</summary>
    public List<(float X, float Y, float W, float H, float Opacity)> ImageDraws { get; } = [];

    /// <summary>Rects passed to DrawRect / path Rect() calls.</summary>
    public List<(float X, float Y, float W, float H)> Rects { get; } = [];

    /// <summary>Recorded ArcTo calls: (centerX, centerY, radius, startAngle, endAngle).</summary>
    public List<(float Cx, float Cy, float Radius, float Start, float End)> Arcs { get; } = [];

    /// <summary>Recorded circles (path Circle() and DrawCircle): (centerX, centerY, radius).</summary>
    public List<(float Cx, float Cy, float Radius)> Circles { get; } = [];

    internal void RegisterArc(float cx, float cy, float radius, float start, float end)
        => Arcs.Add((cx, cy, radius, start, end));

    /// <summary>Whether anything at all was drawn.</summary>
    public bool DrewAnything => FillCount > 0 || StrokeCount > 0 || PathOpCount > 0 || Texts.Count > 0;

    /// <summary>Labels drawn right-aligned (i.e. the Y axis labels of the default renderers).</summary>
    public IEnumerable<string> YAxisLabels =>
        TextDraws.Where(d => d.Align == TextAlign.Right).Select(d => d.Text);

    /// <summary>Labels drawn left-aligned (i.e. the X axis labels of the default renderers).</summary>
    public IEnumerable<string> XAxisLabels =>
        TextDraws.Where(d => d.Align == TextAlign.Left).Select(d => d.Text);

    /// <summary>Number of times Dispose() was called.</summary>
    public int DisposeCount { get; private set; }

    /// <summary>Number of BeginFrame() calls (frame loop bookkeeping).</summary>
    public int BeginFrameCount { get; private set; }

    /// <summary>Number of EndFrame() calls.</summary>
    public int EndFrameCount { get; private set; }

    /// <summary>Number of Tick() calls.</summary>
    public int TickCount { get; private set; }

    /// <summary>Colours passed to Clear(), in order.</summary>
    public List<Color> ClearColors { get; } = [];

    /// <summary>Sizes passed to Resize(), in order.</summary>
    public List<(int Width, int Height)> ResizeCalls { get; } = [];

    /// <summary>Number of IPath2D objects handed out (allocation counter for the hot path).</summary>
    public int PathCreateCount { get; set; }

    /// <summary>Number of IPaint2D objects handed out.</summary>
    public int PaintCreateCount { get; set; }

    /// <summary>Number of MeasureText calls (text measurement is expensive on the Skia backend).</summary>
    public int MeasureTextCallCount { get; set; }

    /// <summary>Number of Save() calls (used to verify that Save/Restore stay balanced).</summary>
    public int SaveCount { get; private set; }

    /// <summary>Number of Restore() calls.</summary>
    public int RestoreCount { get; private set; }

    /// <summary>Number of DrawImage() calls.</summary>
    public int ImageDrawCount { get; private set; }

    /// <summary>When true, Fill() throws — used to check cleanup on the error path.</summary>
    public bool ThrowOnFill { get; set; }

    /// <summary>True when every Save() has a matching Restore().</summary>
    public bool SaveRestoreBalanced => SaveCount == RestoreCount;

    public CanvasCapabilities Capabilities { get; init; } = new(
        SupportsGradients: true,
        SupportsClipping: true,
        SupportsTransforms: true,
        IsGpuBacked: false,
        SupportsLineDash: true,
        SupportsImages: true,
        SupportsSurfaceCapture: true);

    /// <summary>Number of <see cref="CaptureRegion"/> calls (i.e. how often a layer was captured).</summary>
    public int CaptureCount { get; private set; }

    /// <summary>Regions passed to <see cref="CaptureRegion"/>, in order.</summary>
    public List<(int X, int Y, int W, int H)> Captures { get; } = [];

    /// <summary>Number of captured handles the callers disposed.</summary>
    public int CapturedHandleDisposeCount { get; private set; }

    /// <summary>
    /// What had been drawn at the moment of each <see cref="CaptureRegion"/> call: the fill and stroke counts
    /// plus the fill colours so far. The layer a capture returns is the drawing up to that point, so this is
    /// the closest a pixel-less fake can get to "what the cached layer holds" - which is how a test can tell
    /// an interaction-state visual apart from the marks' plain drawing.
    /// </summary>
    public List<(int Fills, int Strokes, string FillColors)> DrawnAtCapture { get; } = [];

    /// <summary>
    /// When set, <see cref="CaptureRegion"/> answers with a handle of this size instead of the requested one -
    /// the "the surface clipped the region" case a caller has to fall back from.
    /// </summary>
    public (int W, int H)? CaptureSizeOverride { get; set; }

    /// <summary>When true, <see cref="CaptureRegion"/> throws, like a backend that cannot read its surface.</summary>
    public bool ThrowOnCapture { get; set; }

    /// <inheritdoc />
    /// <remarks>
    /// The fake holds no pixels: it records the region and hands back a handle of the requested size, so a
    /// test can observe <i>that</i> a layer was captured and where, not what it contains.
    /// </remarks>
    public IImageHandle CaptureRegion(int x, int y, int width, int height)
    {
        if (ThrowOnCapture)
            throw new NotSupportedException("the fake canvas was told to fail its capture");

        CaptureCount++;
        Captures.Add((x, y, width, height));
        // The capture takes the surface as it is right now, so everything recorded so far is the layer.
        var colors = new System.Text.StringBuilder();
        foreach (var color in FillColors)
            colors.Append(color.ToHtml(false)).Append(',');
        DrawnAtCapture.Add((FillCount, StrokeCount, colors.ToString()));
        var size = CaptureSizeOverride ?? (width, height);
        return new FakeCapturedHandle(size.W, size.H, () => CapturedHandleDisposeCount++);
    }

    /// <summary>Handle handed out by <see cref="CaptureRegion"/>: reports its size and counts its disposal.</summary>
    private sealed class FakeCapturedHandle(int width, int height, Action onDispose) : IImageHandle
    {
        /// <inheritdoc />
        public int Width { get; } = width;

        /// <inheritdoc />
        public int Height { get; } = height;

        /// <inheritdoc />
        public void Dispose() => onDispose();
    }

    /// <summary>Always null: this test double renders nowhere, so it has no texture to present.</summary>
    public Texture2D? Texture => null;

    internal void RegisterPathOp() => PathOpCount++;

    internal void RegisterCubic() => CubicCount++;

    internal void RegisterRoundRect(float x, float y, float w, float h, float radius)
    {
        RoundRectCount++;
        RoundRects.Add((x, y, w, h, radius));
        RegisterRect(x, y, w, h);
    }

    internal void RegisterPoint(float x, float y)
    {
        if (!float.IsFinite(x) || !float.IsFinite(y)) NonFiniteCoordinateCount++;
    }

    internal void RegisterRect(float x, float y, float w, float h)
    {
        RegisterPoint(x, y);
        RegisterPoint(w, h);
        if (w < 0f || h < 0f) NegativeSizeRectCount++;
        Rects.Add((x, y, w, h));
    }

    public void Resize(int width, int height) => ResizeCalls.Add((width, height));
    public void BeginFrame() => BeginFrameCount++;
    public void EndFrame() => EndFrameCount++;
    public void Clear(Color color) => ClearColors.Add(color);

    public IPath2D CreatePath()
    {
        PathCreateCount++;
        return new FakePath2D(this);
    }

    public IPaint2D CreatePaint()
    {
        PaintCreateCount++;
        return new FakePaint2D();
    }

    /// <summary>
    /// Canonical description of everything that was drawn, in order. Used as a characterisation
    /// snapshot: refactors that should not change the output must keep this string identical.
    /// </summary>
    public string Snapshot()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("rects:");
        foreach (var r in Rects)
            sb.Append('[').Append(r.X.ToString("F2", CultureInfo.InvariantCulture)).Append(',')
              .Append(r.Y.ToString("F2", CultureInfo.InvariantCulture)).Append(',')
              .Append(r.W.ToString("F2", CultureInfo.InvariantCulture)).Append(',')
              .Append(r.H.ToString("F2", CultureInfo.InvariantCulture)).Append(']');
        sb.Append("|fills:").Append(FillCount).Append('/').Append(StrokeCount);
        sb.Append("|colors:");
        foreach (var c in FillColors)
            sb.Append(c.ToHtml(false)).Append(',');
        sb.Append("|texts:");
        foreach (var d in TextDraws)
            sb.Append(d.Text).Append('@').Append(d.X.ToString("F1", CultureInfo.InvariantCulture))
              .Append(',').Append(d.Y.ToString("F1", CultureInfo.InvariantCulture))
              .Append('/').Append(d.Align).Append(';');
        sb.Append("|circles:");
        foreach (var c in Circles)
            sb.Append(c.Cx.ToString("F1", CultureInfo.InvariantCulture)).Append(',')
              .Append(c.Cy.ToString("F1", CultureInfo.InvariantCulture)).Append(',')
              .Append(c.Radius.ToString("F2", CultureInfo.InvariantCulture)).Append(';');
        sb.Append("|arcs:");
        foreach (var a in Arcs)
            sb.Append(a.Cx.ToString("F1", CultureInfo.InvariantCulture)).Append(',')
              .Append(a.Cy.ToString("F1", CultureInfo.InvariantCulture)).Append(',')
              .Append(a.Radius.ToString("F2", CultureInfo.InvariantCulture)).Append(',')
              .Append(a.Start.ToString("F3", CultureInfo.InvariantCulture)).Append(',')
              .Append(a.End.ToString("F3", CultureInfo.InvariantCulture)).Append(';');
        sb.Append("|pathOps:").Append(PathOpCount);
        // Extended dimensions. The fields above only cover geometry plus the fill colour, so a mark
        // could change its stroke colour/width, its corner radius, its alpha, its line segments or
        // its bitmaps - or unbalance the save/restore stack - without moving a single hash. These
        // fields close that blind spot.
        sb.Append("|strokes:");
        for (int i = 0; i < StrokeColors.Count; i++)
        {
            sb.Append(StrokeColors[i].ToHtml(false));
            if (i < StrokeWidths.Count)
                sb.Append('@').Append(StrokeWidths[i].ToString("F2", CultureInfo.InvariantCulture));
            sb.Append(';');
        }
        sb.Append("|fillOpacity:");
        foreach (var o in FillOpacities)
            sb.Append(o.ToString("F3", CultureInfo.InvariantCulture)).Append(',');
        sb.Append("|roundRects:");
        foreach (var r in RoundRects)
            sb.Append('[').Append(r.X.ToString("F2", CultureInfo.InvariantCulture)).Append(',')
              .Append(r.Y.ToString("F2", CultureInfo.InvariantCulture)).Append(',')
              .Append(r.W.ToString("F2", CultureInfo.InvariantCulture)).Append(',')
              .Append(r.H.ToString("F2", CultureInfo.InvariantCulture)).Append(',')
              .Append(r.Radius.ToString("F2", CultureInfo.InvariantCulture)).Append(']');
        sb.Append("|lines:");
        foreach (var l in Lines)
            sb.Append('[').Append(l.X0.ToString("F2", CultureInfo.InvariantCulture)).Append(',')
              .Append(l.Y0.ToString("F2", CultureInfo.InvariantCulture)).Append(',')
              .Append(l.X1.ToString("F2", CultureInfo.InvariantCulture)).Append(',')
              .Append(l.Y1.ToString("F2", CultureInfo.InvariantCulture)).Append(',')
              .Append(l.Color.ToHtml(false)).Append(']');
        sb.Append("|images:");
        foreach (var img in ImageDraws)
            sb.Append('[').Append(img.X.ToString("F2", CultureInfo.InvariantCulture)).Append(',')
              .Append(img.Y.ToString("F2", CultureInfo.InvariantCulture)).Append(',')
              .Append(img.W.ToString("F2", CultureInfo.InvariantCulture)).Append(',')
              .Append(img.H.ToString("F2", CultureInfo.InvariantCulture)).Append(',')
              .Append(img.Opacity.ToString("F3", CultureInfo.InvariantCulture)).Append(']');
        sb.Append("|saves:").Append(SaveCount).Append("/restores:").Append(RestoreCount);
        return sb.ToString();
    }

    /// <summary>Short, stable hash of <see cref="Snapshot"/>.</summary>
    public string SnapshotHash()
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(Snapshot());
        var hash = System.Security.Cryptography.SHA256.HashData(bytes);
        return Convert.ToHexString(hash)[..16];
    }

    public void Stroke(IPath2D path, IPaint2D paint)
    {
        StrokeCount++;
        if (paint is FakePaint2D fakePaint)
        {
            StrokeColors.Add(fakePaint.Current);
            StrokeWidths.Add(fakePaint.LastStrokeWidth);
        }
    }

    public void Fill(IPath2D path, IPaint2D paint)
    {
        if (ThrowOnFill)
            throw new InvalidOperationException("intentional fill failure for tests");
        FillCount++;
        if (paint is FakePaint2D fakePaint)
        {
            FillColors.Add(fakePaint.Current);
            FillOpacities.Add(fakePaint.LastOpacity);
        }
    }

    public void StrokeAndFill(IPath2D path, IPaint2D strokePaint, IPaint2D fillPaint)
    {
        Stroke(path, strokePaint);
        Fill(path, fillPaint);
    }

    public void DrawLine(float x0, float y0, float x1, float y1, IPaint2D paint)
    {
        RegisterPoint(x0, y0);
        RegisterPoint(x1, y1);
        StrokeCount++;
        Lines.Add((x0, y0, x1, y1, paint is FakePaint2D fakePaint ? fakePaint.Current : default));
        // The line's own width, kept apart from StrokeWidths: that list lines up element by element with the
        // path strokes, and a DrawLine is not one of them.
        LineWidths.Add(paint is FakePaint2D widthPaint ? widthPaint.LastStrokeWidth : 0f);
    }

    public void DrawRect(float x, float y, float w, float h, IPaint2D paint) => RegisterRect(x, y, w, h);

    public void DrawCircle(float cx, float cy, float r, IPaint2D paint)
    {
        RegisterPoint(cx, cy);
        RegisterPoint(r, r);
        Circles.Add((cx, cy, r));
        // Mirrors Canvas2DBase.DrawCircle: the outline is STROKED, never filled. Counting it as a fill
        // made the fake disagree with the real backend about the same call, so a case asserting "fill
        // count" could pass here and paint nothing on the device.
        StrokeCount++;
        if (paint is FakePaint2D fakePaint)
        {
            StrokeColors.Add(fakePaint.Current);
            StrokeWidths.Add(fakePaint.LastStrokeWidth);
        }
    }

    public void DrawText(string text, float x, float y, FontSettings font, IPaint2D paint)
    {
        RegisterPoint(x, y);
        Texts.Add(text);
        var color = paint is FakePaint2D fakePaint ? fakePaint.Current : Colors.White;
        TextDraws.Add(new TextDraw(text, x, y, font.Align, font, color));
    }

    /// <summary>Font used for the first draw of <paramref name="text"/>.</summary>
    public FontSettings FontOf(string text) => TextDraws.First(d => d.Text == text).Font;

    /// <summary>Colors used for every draw of <paramref name="text"/>.</summary>
    public IEnumerable<Color> TextColorsOf(string text)
        => TextDraws.Where(d => d.Text == text).Select(d => d.Color);

    public IImageHandle LoadImage(int width, int height, byte[] rgbaPixels) => throw new NotSupportedException();
    public IImageHandle LoadImage(Texture2D texture) => throw new NotSupportedException();
    public void DrawImage(IImageHandle image, float x, float y, float dstW, float dstH, float opacity = 1f)
    {
        RegisterPoint(x, y);
        RegisterPoint(dstW, dstH);
        if (dstW < 0f || dstH < 0f) NegativeSizeRectCount++;
        ImageDraws.Add((x, y, dstW, dstH, opacity));
        ImageDrawCount++;
    }

    public void Save() => SaveCount++;
    public void Restore() => RestoreCount++;
    public void Translate(float x, float y) => RegisterPoint(x, y);
    public void Scale(float sx, float sy) { }
    public void Rotate(float angle) { }
    /// <summary>
    /// The fake does not clip: it records what the marks draw, and the clip the chart puts around them is a
    /// property of the real backends (it is what keeps data outside a zoom window off the axes). Recording it
    /// would put a rectangle in front of every draw call and hide the geometry these tests exist to check.
    /// </summary>
    public void ClipRect(float x, float y, float w, float h) { }

    /// <summary>Regions the drawing code reported; the fake uploads nothing, it only records that they came.</summary>
    public int InvalidateRegionCount { get; private set; }

    /// <summary>The union of the reported regions, for assertions about what one frame changed.</summary>
    public Rect2 DirtyRegion { get; private set; }

    /// <inheritdoc />
    public void InvalidateRegion(float x, float y, float w, float h)
    {
        if (!(w > 0f) || !(h > 0f)) return;

        var region = new Rect2(x, y, w, h);
        DirtyRegion = InvalidateRegionCount == 0 ? region : DirtyRegion.Merge(region);
        InvalidateRegionCount++;
    }

    public TextMetrics MeasureText(string text, FontSettings font)
    {
        MeasureTextCallCount++;
        return new(text.Length * font.Size * 0.6f, font.Size * font.LineHeightMultiplier);
    }

    public void Tick() => TickCount++;
    public void Dispose() => DisposeCount++;

    private sealed class FakePath2D(FakeCanvas2D owner) : IPath2D
    {
        public IPath2D MoveTo(float x, float y) { owner.RegisterPathOp(); owner.RegisterPoint(x, y); return this; }
        public IPath2D LineTo(float x, float y) { owner.RegisterPathOp(); owner.RegisterPoint(x, y); return this; }
        public IPath2D CubicTo(float cx1, float cy1, float cx2, float cy2, float x, float y)
        {
            owner.RegisterPathOp();
            owner.RegisterCubic();
            owner.RegisterPoint(cx1, cy1);
            owner.RegisterPoint(cx2, cy2);
            owner.RegisterPoint(x, y);
            return this;
        }
        public IPath2D QuadTo(float cx, float cy, float x, float y)
        {
            owner.RegisterPathOp();
            owner.RegisterPoint(cx, cy);
            owner.RegisterPoint(x, y);
            return this;
        }
        public IPath2D ArcTo(float cx, float cy, float radius, float startAngle, float endAngle, bool clockwise = false)
        {
            owner.RegisterPathOp();
            owner.RegisterPoint(cx, cy);
            owner.RegisterPoint(radius, radius);
            owner.RegisterPoint(startAngle, endAngle);
            owner.RegisterArc(cx, cy, radius, startAngle, endAngle);
            return this;
        }
        public IPath2D Rect(float x, float y, float w, float h) { owner.RegisterPathOp(); owner.RegisterRect(x, y, w, h); return this; }
        public IPath2D RoundRect(float x, float y, float w, float h, float radius)
        {
            owner.RegisterPathOp();
            owner.RegisterRoundRect(x, y, w, h, radius);
            return this;
        }
        public IPath2D Circle(float cx, float cy, float radius)
        {
            owner.RegisterPathOp();
            owner.RegisterPoint(cx, cy);
            owner.RegisterPoint(radius, radius);
            owner.Circles.Add((cx, cy, radius));
            return this;
        }
        public IPath2D Close() { return this; }
        public IPath2D Reset() { return this; }
        public void Dispose() { }
    }

    private sealed class FakePaint2D : IPaint2D
    {
        /// <summary>Last color passed to SetColor — used to observe gradient/series colors.</summary>
        public Color Current { get; private set; } = Colors.White;

        /// <summary>Last width passed to SetStrokeWidth (0 until one is set).</summary>
        public float LastStrokeWidth { get; private set; }

        /// <summary>Last alpha passed to SetOpacity (1 until one is set).</summary>
        public float LastOpacity { get; private set; } = 1f;

        public IPaint2D SetColor(Color color) { Current = color; return this; }
        public IPaint2D SetStrokeWidth(float width) { LastStrokeWidth = width; return this; }
        public IPaint2D SetAntiAlias(bool aa) => this;
        public IPaint2D SetLineCap(LineCap cap) => this;
        public IPaint2D SetLineJoin(LineJoin join) => this;
        public IPaint2D SetMiterLimit(float limit) => this;
        public IPaint2D SetLineDash(float[] pattern, float offset = 0f) => this;
        public IPaint2D SetOpacity(float alpha) { LastOpacity = alpha; return this; }

        public IPaint2D SetLinearGradient(float x0, float y0, float x1, float y1, GradientStop[] stops) => this;
        public IPaint2D SetRadialGradient(float cx, float cy, float radius, GradientStop[] stops) => this;
        public void Dispose() { }
    }
}
