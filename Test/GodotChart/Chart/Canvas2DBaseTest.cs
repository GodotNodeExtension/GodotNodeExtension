namespace GodotNodeExtension.Tests.GodotChart;

using System;
using GdUnit4;
using Godot;
using GodotNodeExtension.Component.GodotChart.Canvas;
using Support;
using static GdUnit4.Assertions;

/// <summary>
/// Tests for the shared convenience contract of <see cref="Canvas2DBase"/> / <see cref="ICanvas2D"/>:
/// the transform stack (translate/scale/rotate plus save/restore), the default draw helpers that
/// stroke without filling, the default text/image/clip behaviour, and the capabilities contract of
/// the fake backend.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class Canvas2DBaseTest
{
    private static void Approx(float actual, float expected, float tolerance = 1e-4f)
        => AssertThat(MathF.Abs(actual - expected) <= tolerance).IsTrue();

    /// <summary>
    /// Minimal <see cref="Canvas2DBase"/> subclass that counts stroke/fill calls and exposes the
    /// protected <c>CurrentTransform</c> field so the transform stack can be asserted.
    /// </summary>
    private sealed class RecordingCanvas : Canvas2DBase
    {
        public int StrokeCalls { get; private set; }
        public int FillCalls { get; private set; }
        public Transform2D Transform => CurrentTransform;

        public override void Resize(int width, int height) { }
        public override void BeginFrame() { }
        public override void EndFrame() { }
        public override void Clear(Color color) { }
        public override IPath2D CreatePath() => new NoopPath();
        public override IPaint2D CreatePaint() => new NoopPaint();
        public override void Stroke(IPath2D path, IPaint2D paint) => StrokeCalls++;
        public override void Fill(IPath2D path, IPaint2D paint) => FillCalls++;
        public override CanvasCapabilities Capabilities => new(false, false, false, false, false);
        public override TextMetrics MeasureText(string text, FontSettings font) => new(0f, 0f);
    }

    private sealed class NoopPath : IPath2D
    {
        public IPath2D MoveTo(float x, float y) => this;
        public IPath2D LineTo(float x, float y) => this;
        public IPath2D CubicTo(float cx1, float cy1, float cx2, float cy2, float x, float y) => this;
        public IPath2D QuadTo(float cx, float cy, float x, float y) => this;
        public IPath2D ArcTo(float cx, float cy, float radius, float startAngle, float endAngle,
                             bool clockwise = false) => this;
        public IPath2D Rect(float x, float y, float w, float h) => this;
        public IPath2D RoundRect(float x, float y, float w, float h, float radius) => this;
        public IPath2D Circle(float cx, float cy, float radius) => this;
        public IPath2D Close() => this;
        public IPath2D Reset() => this;
        public void Dispose() { }
    }

    private sealed class NoopPaint : IPaint2D
    {
        public IPaint2D SetColor(Color color) => this;
        public IPaint2D SetStrokeWidth(float width) => this;
        public IPaint2D SetAntiAlias(bool aa) => this;
        public IPaint2D SetLineCap(LineCap cap) => this;
        public IPaint2D SetLineJoin(LineJoin join) => this;
        public IPaint2D SetMiterLimit(float limit) => this;
        public IPaint2D SetLineDash(float[] pattern, float offset = 0f) => this;
        public IPaint2D SetOpacity(float alpha) => this;
        public IPaint2D SetLinearGradient(float x0, float y0, float x1, float y1, GradientStop[] stops) => this;
        public IPaint2D SetRadialGradient(float cx, float cy, float radius, GradientStop[] stops) => this;
        public void Dispose() { }
    }

    // ── Transform stack ─────────────────────────────────────────────────────

    [TestCase]
    public void TranslateAccumulatesAndSaveRestoreRollsBack()
    {
        var canvas = new RecordingCanvas();

        canvas.Translate(10f, 20f);
        Approx(canvas.Transform.Origin.X, 10f);
        Approx(canvas.Transform.Origin.Y, 20f);

        canvas.Translate(5f, 5f);
        Approx(canvas.Transform.Origin.X, 15f);
        Approx(canvas.Transform.Origin.Y, 25f);

        canvas.Save();
        canvas.Translate(100f, 100f);
        Approx(canvas.Transform.Origin.X, 115f);
        Approx(canvas.Transform.Origin.Y, 125f);

        canvas.Restore();
        Approx(canvas.Transform.Origin.X, 15f);
        Approx(canvas.Transform.Origin.Y, 25f);
    }

    [TestCase]
    public void ScaleComposesIntoTheTransform()
    {
        var canvas = new RecordingCanvas();
        canvas.Scale(2f, 3f);

        var point = canvas.Transform * new Vector2(1f, 1f);
        Approx(point.X, 2f);
        Approx(point.Y, 3f);

        // The scale is rolled back by Restore.
        canvas.Save();
        canvas.Scale(4f, 4f);
        Approx((canvas.Transform * new Vector2(1f, 0f)).X, 8f);
        canvas.Restore();
        Approx((canvas.Transform * new Vector2(1f, 0f)).X, 2f);
    }

    [TestCase]
    public void RotateComposesIntoTheTransform()
    {
        var canvas = new RecordingCanvas();
        canvas.Rotate(MathF.PI / 2f);

        var x = canvas.Transform * new Vector2(1f, 0f);
        Approx(x.X, 0f);
        Approx(x.Y, 1f);

        var y = canvas.Transform * new Vector2(0f, 1f);
        Approx(y.X, -1f);
        Approx(y.Y, 0f);
    }

    [TestCase]
    public void RestoreWithoutSaveIsSafe()
    {
        var canvas = new RecordingCanvas();
        canvas.Translate(3f, 4f);
        canvas.Restore(); // empty stack: must stay put

        Approx(canvas.Transform.Origin.X, 3f);
        Approx(canvas.Transform.Origin.Y, 4f);

        var fresh = new RecordingCanvas();
        fresh.Restore();
        Approx(fresh.Transform.Origin.X, 0f);
        Approx((fresh.Transform * new Vector2(1f, 0f)).X, 1f);
    }

    // ── Default draw helpers ────────────────────────────────────────────────

    [TestCase]
    public void DrawRectStrokesWithoutFilling()
    {
        var canvas = new RecordingCanvas();
        canvas.DrawRect(0f, 0f, 10f, 10f, new NoopPaint());

        AssertThat(canvas.StrokeCalls).IsEqual(1);
        AssertThat(canvas.FillCalls).IsEqual(0);
    }

    [TestCase]
    public void DrawCircleStrokesWithoutFilling()
    {
        var canvas = new RecordingCanvas();
        canvas.DrawCircle(5f, 5f, 2f, new NoopPaint());

        AssertThat(canvas.StrokeCalls).IsEqual(1);
        AssertThat(canvas.FillCalls).IsEqual(0);
    }

    [TestCase]
    public void DrawLineStrokesWithoutFilling()
    {
        var canvas = new RecordingCanvas();
        canvas.DrawLine(0f, 0f, 1f, 1f, new NoopPaint());

        AssertThat(canvas.StrokeCalls).IsEqual(1);
        AssertThat(canvas.FillCalls).IsEqual(0);
    }

    [TestCase]
    public void StrokeAndFillDoesBoth()
    {
        var canvas = new RecordingCanvas();
        canvas.StrokeAndFill(new NoopPath(), new NoopPaint(), new NoopPaint());

        AssertThat(canvas.StrokeCalls).IsEqual(1);
        AssertThat(canvas.FillCalls).IsEqual(1);
    }

    // ── Default (unsupported) behaviour ─────────────────────────────────────

    [TestCase]
    public void DefaultDrawTextThrowsInDebug()
    {
        var canvas = new RecordingCanvas();
        bool threw = false;
        try
        {
            // RecordingCanvas deliberately does not override DrawText.
            canvas.DrawText("x", 0f, 0f, FontSettings.Default, new NoopPaint());
        }
        catch (NotImplementedException)
        {
            threw = true;
        }

#if DEBUG
        AssertThat(threw).IsTrue();
#else
        // In release builds the base implementation degrades to a single logged error.
        AssertThat(threw).IsFalse();
#endif
    }

    [TestCase]
    public void DefaultLoadImageThrowsNotSupported()
    {
        var canvas = new RecordingCanvas();

        bool pixelsThrew = false, textureThrew = false;
        try { canvas.LoadImage(1, 1, new byte[4]); }
        catch (NotSupportedException) { pixelsThrew = true; }
        try { canvas.LoadImage(null!); }
        catch (NotSupportedException) { textureThrew = true; }

        AssertThat(pixelsThrew).IsTrue();
        AssertThat(textureThrew).IsTrue();
    }

    [TestCase]
    public void DefaultDrawImageIsSilentNoOp()
    {
        var canvas = new RecordingCanvas();
        bool threw = false;

        try { canvas.DrawImage(null!, 0f, 0f, 4f, 4f); }
        catch (Exception) { threw = true; }

        AssertThat(threw).IsFalse();
    }

    [TestCase]
    public void DefaultClipTickAndDisposeDoNotThrow()
    {
        var canvas = new RecordingCanvas();
        bool threw = false;

        try
        {
            canvas.ClipRect(0f, 0f, 10f, 10f);
            canvas.Tick();
            canvas.Dispose();
        }
        catch (Exception) { threw = true; }

        AssertThat(threw).IsFalse();
    }

    // ── Capabilities ────────────────────────────────────────────────────────

    [TestCase]
    public void FakeCanvasCapabilitiesBaseline()
    {
        var canvas = new FakeCanvas2D();

        AssertThat(canvas.Capabilities.SupportsGradients).IsTrue();
        AssertThat(canvas.Capabilities.SupportsClipping).IsTrue();
        AssertThat(canvas.Capabilities.SupportsTransforms).IsTrue();
        AssertThat(canvas.Capabilities.IsGpuBacked).IsFalse();
        AssertThat(canvas.Capabilities.SupportsLineDash).IsTrue();
        AssertThat(canvas.Capabilities.SupportsImages).IsTrue();

        // The capabilities can be overridden per test instance.
        var partial = new FakeCanvas2D
        {
            Capabilities = new CanvasCapabilities(true, false, false, false, false),
        };
        AssertThat(partial.Capabilities.SupportsImages).IsFalse();
        AssertThat(partial.Capabilities.SupportsGradients).IsTrue();
    }
}
