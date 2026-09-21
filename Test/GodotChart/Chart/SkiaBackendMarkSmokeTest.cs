namespace GodotNodeExtension.Tests.GodotChart;

using System;
using System.Collections.Generic;
using System.Reflection;
using GdUnit4;
using Godot;
using GodotNodeExtension.Component.GodotChart;
using GodotNodeExtension.Component.GodotChart.Canvas;
using GodotNodeExtension.Component.GodotSkia;
using GodotNodeExtension.Tests.Support;
using SkiaSharp;
using Support;
using static GdUnit4.Assertions;
using static GodotNodeExtension.Tests.GodotChart.Support.Asserts;

/// <summary>
/// Behaviour specification for the graphics side of <see cref="SkiaCanvas2DBackend"/>.
/// <para>
/// The first group renders every mark on the real backend twice: the marks reuse one cached path and
/// one cached paint per mark, and disposing either of them frees the native object the mark still
/// holds - the next draw then crashes the process inside Skia. The fake canvas cannot see that
/// (disposing a fake is a no-op), so the marks run on the actual backend.
/// </para>
/// <para>
/// The second group pins the backend's own API: the surface lifecycle, the pixel-buffer and image
/// contract, the transform/clip plumbing, the reuse pool limits, and the paint/path handles that move
/// through that pool. Cases that need a surface skip themselves without a rendering device; the ones
/// marked "headless" work on a backend that was never initialized.
/// </para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class SkiaBackendMarkSmokeTest
{
    /// <summary>True when the run has a rendering device; otherwise the case skips itself.</summary>
    private static bool HasDevice(string caseName)
    {
        if (RenderingServer.GetRenderingDevice() is not null) return true;
        GD.Print($"[skip] {caseName}: no rendering device in this run");
        return false;
    }

    /// <summary>Read one pixel of the backend's surface through the backend's own texture.</summary>
    private static Color PixelAt(SkiaCanvas2DBackend backend, int x, int y)
    {
        using var image = backend.SkiaTexture.GetImage();
        return image.GetPixel(x, y);
    }

    private static bool IsRed(Color c) => c.R > 0.6f && c.G < 0.3f && c.B < 0.3f;
    private static bool IsGreen(Color c) => c.G > 0.6f && c.R < 0.3f && c.B < 0.3f;
    private static bool IsBlue(Color c) => c.B > 0.6f && c.R < 0.3f && c.G < 0.3f;
    private static bool IsBlack(Color c) => c.R < 0.1f && c.G < 0.1f && c.B < 0.1f;

    private static void AssertStaticCapabilityFlags(CanvasCapabilities caps, bool gpuBacked)
    {
        AssertThat(caps.SupportsGradients).IsTrue();
        AssertThat(caps.SupportsClipping).IsTrue();
        AssertThat(caps.SupportsTransforms).IsTrue();
        AssertThat(caps.SupportsLineDash).IsTrue();
        AssertThat(caps.SupportsImages).IsTrue();
        AssertThat(caps.IsGpuBacked).IsEqual(gpuBacked);
    }

    [TestCase]
    public void EveryMarkRendersTwoFramesOnTheRealBackend()
    {
        if (!HasDevice(nameof(EveryMarkRendersTwoFramesOnTheRealBackend))) return;

        var canvas = Canvas2DFactory.Create(320, 200);
        var log = EngineMessageLog.Attach();
        var empty = new List<string>();
        int rendered = 0;
        try
        {
            foreach (var c in MarkCases.All)
            {
                var chart = new Chart(canvas) { Width = 320, Height = 200 };
                chart.Data(c.Data);
                chart.Encode(Channel.X, c.XField);
                chart.Encode(Channel.Y, c.YField);
                if (c.ColorField is not null)
                    chart.Encode(Channel.Color, c.ColorField);

                // The frame without the mark is the baseline: the frame is never blank (the background and
                // the axes are painted by the chart itself), so only the difference between the two frames
                // says whether the mark contributed anything.
                byte[] bare = RenderFrame(canvas, chart);

                chart.Mark(c.Create());

                // Two frames: state that leaks between frames (a paint disposed on the first one) only
                // shows up on the second.
                byte[] drawn = bare;
                for (int frame = 0; frame < 2; frame++)
                    drawn = RenderFrame(canvas, chart);
                rendered++;

                if (drawn.AsSpan().SequenceEqual(bare))
                    empty.Add(c.Name);
            }
        }
        finally
        {
            log.Detach();
            canvas.Dispose();
        }

        // "Getting here without a native crash" used to be the whole assertion, and an emptied loop or a
        // mark that drew nothing passed it. These three pin what the run actually did: every case was
        // rendered, every case changed the frame, and no case pushed an engine error.
        AssertThat(rendered).IsEqual(MarkCases.All.Length);
        AssertThat(string.Join("; ", empty)).IsEqual("");
        AssertThat(string.Join("; ", log.ErrorsContaining(""))).IsEqual("");
    }

    /// <summary>
    /// Render one frame on <paramref name="canvas"/> and return the surface's pixels (RGBA8), so the frame
    /// with a mark and the frame without it can be compared byte for byte.
    /// </summary>
    private static byte[] RenderFrame(ICanvas2D canvas, Chart chart)
    {
        canvas.BeginFrame();
        canvas.Clear(Colors.Black);
        chart.Render();
        canvas.EndFrame();

        using var image = canvas.Texture?.GetImage();
        return image?.GetData() ?? [];
    }

    [TestCase]
    public void MarksReuseTheirCachedDrawingObjects()
    {
        // The cheap, GPU-free counterpart: the objects a mark asked for through ShapePath/ShapePaint
        // must still be usable on the next frame.
        var canvas = new FakeCanvas2D();
        foreach (var c in MarkCases.All)
        {
            var chart = new Chart(canvas) { Width = 320, Height = 200 };
            chart.Data(c.Data);
            chart.Mark(c.Create());
            chart.Encode(Channel.X, c.XField);
            chart.Encode(Channel.Y, c.YField);
            if (c.ColorField is not null)
                chart.Encode(Channel.Color, c.ColorField);

            chart.Render();
            chart.Render();
        }

        AssertThat(canvas.FillCount + canvas.StrokeCount > 0).IsTrue();
    }

    // ── Surface lifecycle ────────────────────────────────────────────────────

    /// <summary>
    /// <see cref="SkiaCanvas2DBackend.Initialize"/> builds the one surface the backend owns: a second
    /// call would replace it and leak the texture (and its GPU RIDs) built by the first one. The guard
    /// has to name the documented alternative, and a disposed backend has to refuse the call as well.
    /// </summary>
    [TestCase]
    public void InitializingTwiceOrAfterDisposeIsRefused()
    {
        if (!HasDevice(nameof(InitializingTwiceOrAfterDisposeIsRefused))) return;

        var backend = new SkiaCanvas2DBackend();
        try
        {
            backend.Initialize(32, 24);

            var second = Throws<InvalidOperationException>(() => backend.Initialize(16, 16));
            AssertThat(second is not null).IsTrue();
            AssertThat(second!.Message.Contains("already initialized")).IsTrue();
            AssertThat(second.Message.Contains("Resize")).IsTrue();

            // The refused call must not have replaced the surface it complained about.
            AssertThat(backend.SkiaTexture.Width).IsEqual(32);
            AssertThat(backend.SkiaTexture.Height).IsEqual(24);
        }
        finally
        {
            backend.Dispose();
        }

        var disposed = new SkiaCanvas2DBackend();
        disposed.Initialize(8, 8);
        disposed.Dispose();

        AssertThat(Throws<ObjectDisposedException>(() => disposed.Initialize(8, 8)) is not null).IsTrue();
    }

    /// <summary>
    /// A backend with no surface refuses the typed accessor and answers the null-tolerant one with
    /// null: a host may check for "nothing to present" without catching, and the typed accessor is for
    /// the hosts that cannot continue either way. Reached without a device (headless-safe).
    /// </summary>
    [TestCase]
    public void AMissingSurfaceReportsItselfInsteadOfReturningNull()
    {
        var backend = new SkiaCanvas2DBackend();

        AssertThat(Throws<ObjectDisposedException>(() => _ = backend.SkiaTexture) is not null).IsTrue();
        AssertThat(backend.Texture is null).IsTrue();

        backend.Dispose();

        AssertThat(Throws<ObjectDisposedException>(() => _ = backend.SkiaTexture) is not null).IsTrue();
        AssertThat(backend.Texture is null).IsTrue();
    }

    // ── Capabilities ─────────────────────────────────────────────────────────

    [TestCase]
    public void CapabilitiesReportTheSkiaFeaturesWithoutASurface()
    {
        var backend = new SkiaCanvas2DBackend();

        var caps = backend.Capabilities;
        AssertStaticCapabilityFlags(caps, gpuBacked: false);

        // Every flag is fixed for the backend's lifetime, so the record is cached and reads hand the
        // same instance out (marks read these flags per element).
        AssertThat(ReferenceEquals(backend.Capabilities, caps)).IsTrue();

        backend.Dispose();

        // The GPU flag reports false instead of touching the released texture.
        AssertThat(backend.Capabilities.IsGpuBacked).IsFalse();
    }

    [TestCase]
    public void CapabilitiesFollowTheRealSurfaceGpuMode()
    {
        if (!HasDevice(nameof(CapabilitiesFollowTheRealSurfaceGpuMode))) return;

        var backend = new SkiaCanvas2DBackend();
        try
        {
            var withoutSurface = backend.Capabilities;
            AssertStaticCapabilityFlags(withoutSurface, gpuBacked: false);

            backend.Initialize(16, 16);

            bool gpuMode = backend.SkiaTexture.IsGpuMode;
            var withSurface = backend.Capabilities;
            AssertStaticCapabilityFlags(withSurface, gpuMode);

            // Same flag state -> the cached record; a flipped GPU flag -> a rebuilt one.
            AssertThat(ReferenceEquals(backend.Capabilities, withSurface)).IsTrue();
            AssertThat(ReferenceEquals(withoutSurface, withSurface)).IsEqual(!gpuMode);
        }
        finally
        {
            backend.Dispose();
        }

        AssertThat(backend.Capabilities.IsGpuBacked).IsFalse();
    }

    // ── Resize ───────────────────────────────────────────────────────────────

    [TestCase]
    public void ResizingToTheSameSizeDoesNotRebuildTheSurface()
    {
        if (!HasDevice(nameof(ResizingToTheSameSizeDoesNotRebuildTheSurface))) return;

        var backend = new SkiaCanvas2DBackend();
        try
        {
            backend.Initialize(40, 30);
            int creates = SkiaCanvasTexture2D.GrContextCreateCount;

            backend.Resize(40, 30);   // documented no-op

            // A rebuild would release and re-create the shared GPU context; a no-op must not.
            AssertThat(SkiaCanvasTexture2D.GrContextCreateCount).IsEqual(creates);
            AssertThat(backend.SkiaTexture.Width).IsEqual(40);
            AssertThat(backend.SkiaTexture.Height).IsEqual(30);

            // A real size change does rebuild, and the texture reports the new size.
            backend.Resize(50, 30);
            AssertThat(backend.SkiaTexture.Width).IsEqual(50);
            AssertThat(backend.SkiaTexture.Height).IsEqual(30);
        }
        finally
        {
            backend.Dispose();
        }
    }

    [TestCase]
    public void ResizeAfterDisposeIsANoOp()
    {
        if (!HasDevice(nameof(ResizeAfterDisposeIsANoOp))) return;

        var backend = new SkiaCanvas2DBackend();
        backend.Initialize(24, 24);
        backend.Dispose();

        backend.Resize(64, 64);   // must neither touch the released texture nor throw

        AssertThat(backend.Texture is null).IsTrue();
        AssertThat(Throws<ObjectDisposedException>(() => _ = backend.SkiaTexture) is not null).IsTrue();
    }

    /// <summary>
    /// A whole frame's worth of drawing calls after <see cref="SkiaCanvas2DBackend.Dispose"/>: the surface calls
    /// are silent no-ops (there is nowhere to draw), the frame still balances, and the two ways of reaching the
    /// texture keep answering the documented way - <c>Texture</c> is null, <c>SkiaTexture</c> throws.
    /// </summary>
    [TestCase]
    public void TheCanvasSurfaceIsInertAfterDispose()
    {
        if (!HasDevice(nameof(TheCanvasSurfaceIsInertAfterDispose))) return;

        var backend = new SkiaCanvas2DBackend();
        backend.Initialize(24, 24);
        backend.Dispose();

        backend.BeginFrame();
        backend.Clear(Colors.Red);
        using (var path = backend.CreatePath())
        using (var paint = backend.CreatePaint())
        {
            paint.SetColor(Colors.Blue).SetStrokeWidth(3f);
            path.Rect(0f, 0f, 4f, 4f);
            backend.Fill(path, paint);
            backend.Stroke(path, paint);
            backend.DrawLine(0f, 0f, 4f, 4f, paint);
            backend.ClipRect(0f, 0f, 8f, 8f);
            backend.MeasureText("after dispose", FontSettings.Default);
        }
        backend.EndFrame();

        AssertThat(backend.Texture is null).IsTrue();
        AssertThat(Throws<ObjectDisposedException>(() => _ = backend.SkiaTexture) is not null).IsTrue();
    }

    // ── Images ───────────────────────────────────────────────────────────────

    /// <summary>
    /// The raw-pixel entry point validates what it is given: a null buffer, a non-positive size and an
    /// undersized buffer are rejected with the documented exceptions, while a <b>longer</b> buffer is
    /// accepted (only the leading pixels belong to the image). Headless-safe: no surface is involved.
    /// </summary>
    [TestCase]
    public void LoadImageValidatesThePixelBuffer()
    {
        var backend = new SkiaCanvas2DBackend();
        try
        {
            AssertThat(Throws<ArgumentNullException>(() => backend.LoadImage(1, 1, null!)) is not null).IsTrue();
            AssertThat(Throws<ArgumentOutOfRangeException>(() => backend.LoadImage(0, 1, new byte[4])) is not null).IsTrue();
            AssertThat(Throws<ArgumentOutOfRangeException>(() => backend.LoadImage(1, -1, new byte[4])) is not null).IsTrue();

            // One byte short of 2x2x4: an undersized buffer used to be silently truncated.
            var shortBuffer = Throws<ArgumentException>(() => backend.LoadImage(2, 2, new byte[15]));
            AssertThat(shortBuffer is not null).IsTrue();
            AssertThat(shortBuffer!.Message.Contains("needs 16")).IsTrue();

            // A longer buffer is fine: the first 16 bytes are the image.
            var pixels = new byte[20];
            pixels[0] = 255;   // red of pixel (0,0)
            using var handle = backend.LoadImage(2, 2, pixels);
            AssertThat(handle.Width).IsEqual(2);
            AssertThat(handle.Height).IsEqual(2);
        }
        finally
        {
            backend.Dispose();
        }
    }

    /// <summary>
    /// A texture type without pixel data (a placeholder, a third-party texture) used to surface as a
    /// bare <c>NullReferenceException</c>; the guard has to name what is missing. Headless-safe.
    /// </summary>
    [TestCase]
    public void LoadImageFromATextureWithoutPixelsExplainsTheProblem()
    {
        var backend = new SkiaCanvas2DBackend();
        try
        {
            // PlaceholderTexture2D reports a size but has no image data, so GetImage() returns null.
            using var texture = new PlaceholderTexture2D { Size = new Vector2I(4, 4) };
            AssertThat(texture.GetImage() is null).IsTrue();

            var ex = Throws<InvalidOperationException>(() => backend.LoadImage(texture));
            AssertThat(ex is not null).IsTrue();
            AssertThat(ex!.Message.Contains("no pixel data")).IsTrue();
        }
        finally
        {
            backend.Dispose();
        }
    }

    [TestCase]
    public void AnImageHandleIsSafeToDisposeTwice()
    {
        var backend = new SkiaCanvas2DBackend();
        try
        {
            var handle = backend.LoadImage(2, 2, new byte[16]);
            AssertThat(handle.Width).IsEqual(2);
            AssertThat(handle.Height).IsEqual(2);

            handle.Dispose();
            handle.Dispose();   // no second free of the native bitmap
        }
        finally
        {
            backend.Dispose();
        }
    }

    [TestCase]
    public void DrawImageAppliesTheOpacityToThePixels()
    {
        if (!HasDevice(nameof(DrawImageAppliesTheOpacityToThePixels))) return;

        var backend = new SkiaCanvas2DBackend();
        try
        {
            backend.Initialize(32, 8);
            using var image = backend.LoadImage(1, 1, new byte[] { 255, 255, 255, 255 });   // one opaque white pixel

            backend.BeginFrame();
            backend.Clear(Colors.Black);
            backend.DrawImage(image, 0, 0, 8, 8, 1f);
            backend.DrawImage(image, 8, 0, 8, 8, 0.5f);
            backend.DrawImage(image, 16, 0, 8, 8, 0f);
            backend.EndFrame();

            AssertThat(PixelAt(backend, 4, 4).R > 0.9f).IsTrue();
            var half = PixelAt(backend, 12, 4);
            AssertThat(Math.Abs(half.R - 0.5f) < 0.05f).IsTrue();
            AssertThat(PixelAt(backend, 20, 4).R < 0.02f).IsTrue();
        }
        finally
        {
            backend.Dispose();
        }
    }

    // ── Transform + clip plumbing ────────────────────────────────────────────

    /// <summary>
    /// <c>Save/Translate/Scale/Rotate</c> have to reach the <c>SKCanvas</c>, not only the base class's
    /// bookkeeping: the base transform stack can be perfectly balanced while the device draws
    /// everything at the origin. The pixels are the only place that shows up.
    /// </summary>
    [TestCase]
    public void TransformsReachTheSkiaCanvas()
    {
        if (!HasDevice(nameof(TransformsReachTheSkiaCanvas))) return;

        var backend = new SkiaCanvas2DBackend();
        try
        {
            backend.Initialize(96, 96);
            backend.BeginFrame();
            backend.Clear(Colors.Black);

            // Translate: a rect drawn at the origin lands where it was moved to.
            backend.Save();
            backend.Translate(16, 16);
            FillRect(backend, 0, 0, 8, 8, Colors.Red);
            backend.Restore();

            // Scale: the local rect is drawn twice as large.
            backend.Save();
            backend.Scale(2, 2);
            FillRect(backend, 30, 4, 4, 4, Colors.Green);
            backend.Restore();

            // Rotate: a quarter turn maps the local x axis onto screen y.
            backend.Save();
            backend.Translate(16, 48);
            backend.Rotate(MathF.PI / 2f);
            FillRect(backend, 0, 0, 16, 8, Colors.Blue);
            backend.Restore();

            backend.EndFrame();

            AssertThat(IsRed(PixelAt(backend, 20, 20))).IsTrue();      // translated rect
            AssertThat(IsRed(PixelAt(backend, 4, 4))).IsFalse();       // ... not at the origin
            AssertThat(IsGreen(PixelAt(backend, 64, 12))).IsTrue();    // scaled rect (60..68 x 8..16)
            AssertThat(IsGreen(PixelAt(backend, 32, 6))).IsFalse();    // ... not at the unscaled position
            AssertThat(IsBlue(PixelAt(backend, 12, 56))).IsTrue();     // rotated rect (8..16 x 48..64)
            AssertThat(IsBlack(PixelAt(backend, 90, 90))).IsTrue();    // background untouched elsewhere
        }
        finally
        {
            backend.Dispose();
        }
    }

    /// <summary>
    /// <see cref="ICanvas2D.ClipRect"/> cuts the drawing on the device (the base implementation is a
    /// no-op), and <c>Restore</c> lifts the clip again - a clip that survived its Restore would hide
    /// later frames.
    /// </summary>
    [TestCase]
    public void ClipRectCutsTheDrawingAndRestoreLiftsIt()
    {
        if (!HasDevice(nameof(ClipRectCutsTheDrawingAndRestoreLiftsIt))) return;

        var backend = new SkiaCanvas2DBackend();
        try
        {
            backend.Initialize(96, 96);
            backend.BeginFrame();
            backend.Clear(Colors.Black);

            backend.Save();
            backend.ClipRect(0, 0, 24, 96);
            FillRect(backend, 0, 0, 96, 96, Colors.Green);   // clipped to the left quarter
            backend.Restore();

            // Drawn after Restore: the clip must be gone.
            FillRect(backend, 60, 60, 20, 20, Colors.Red);

            backend.EndFrame();

            AssertThat(IsGreen(PixelAt(backend, 10, 40))).IsTrue();    // inside the clip
            AssertThat(IsBlack(PixelAt(backend, 40, 40))).IsTrue();    // cut away
            AssertThat(IsRed(PixelAt(backend, 70, 70))).IsTrue();      // drawn after the clip was lifted
        }
        finally
        {
            backend.Dispose();
        }
    }

    private static void FillRect(SkiaCanvas2DBackend backend, float x, float y, float w, float h, Color color)
    {
        using var path = backend.CreatePath();
        using var paint = backend.CreatePaint();
        path.Rect(x, y, w, h);
        paint.SetColor(color);
        backend.Fill(path, paint);
    }

    // ── Handle reuse pool ────────────────────────────────────────────────────

    /// <summary>
    /// Number of handles currently parked in one of the backend's private pools. White-box on
    /// purpose: the pool is the only place a leaked handle could hide, and the leak itself (a native
    /// SKPath/SKPaint that is never freed) has no behaviour left to observe.
    /// </summary>
    private static int PooledCount(SkiaCanvas2DBackend backend, string fieldName)
    {
        var field = typeof(SkiaCanvas2DBackend).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance)!;
        return ((System.Collections.ICollection)field.GetValue(backend)!).Count;
    }

    [TestCase]
    public void AHandleDisposedTwiceNeverLeavesThePoolTwice()
    {
        // The pool hands out one native SKPath/SKPaint per instance, so an instance that entered it
        // twice would be handed to two callers - one path, two charts, corrupted geometry. No surface
        // is created: the pool never touches the texture, which keeps this case runnable headless.
        var backend = new SkiaCanvas2DBackend();
        try
        {
            var path = backend.CreatePath();
            path.Dispose();
            path.Dispose();   // must be a no-op, not a second return to the pool

            var recycledPath = backend.CreatePath();
            var freshPath = backend.CreatePath();

            AssertThat(ReferenceEquals(recycledPath, path)).IsTrue();          // the first return did pool it
            AssertThat(ReferenceEquals(freshPath, recycledPath)).IsFalse();    // ...but only once

            var paint = backend.CreatePaint();
            paint.Dispose();
            paint.Dispose();

            var recycledPaint = backend.CreatePaint();
            var freshPaint = backend.CreatePaint();

            AssertThat(ReferenceEquals(recycledPaint, paint)).IsTrue();
            AssertThat(ReferenceEquals(freshPaint, recycledPaint)).IsFalse();

            recycledPath.Dispose();
            freshPath.Dispose();
            recycledPaint.Dispose();
            freshPaint.Dispose();
        }
        finally
        {
            backend.Dispose();
        }
    }

    [TestCase]
    public void HandlesReturnedAfterTheBackendWasDisposedAreFreedNotPooled()
    {
        // A handle created before Dispose can be returned after it: the pool was drained then, and
        // anything pushed into it afterwards is never released again (the native object leaks).
        var backend = new SkiaCanvas2DBackend();
        var path = backend.CreatePath();
        var paint = backend.CreatePaint();

        backend.Dispose();

        path.Dispose();
        paint.Dispose();

        AssertThat(PooledCount(backend, "_pathPool")).IsEqual(0);
        AssertThat(PooledCount(backend, "_paintPool")).IsEqual(0);

        // Releasing the finished handles again stays safe and still leaves the pools empty.
        path.Dispose();
        paint.Dispose();

        AssertThat(PooledCount(backend, "_pathPool")).IsEqual(0);
        AssertThat(PooledCount(backend, "_paintPool")).IsEqual(0);
    }

    /// <summary>
    /// <see cref="SkiaCanvas2DBackend.MaxPoolSize"/> caps what the pool keeps: the surplus handles have
    /// to be freed instead of growing the pool without bound, and the pool has to keep working
    /// afterwards. No surface is involved, so this runs headless.
    /// </summary>
    [TestCase]
    public void TheHandlePoolStaysWithinItsConfiguredLimit()
    {
        var backend = new SkiaCanvas2DBackend { MaxPoolSize = 2 };
        try
        {
            var paths = new IPath2D[6];
            var paints = new IPaint2D[6];
            for (int i = 0; i < paths.Length; i++)
            {
                paths[i] = backend.CreatePath();
                paints[i] = backend.CreatePaint();
            }

            foreach (var path in paths) path.Dispose();      // six returns into a pool capped at two
            foreach (var paint in paints) paint.Dispose();

            AssertThat(PooledCount(backend, "_pathPool")).IsEqual(2);
            AssertThat(PooledCount(backend, "_paintPool")).IsEqual(2);

            // Still usable: the next round of handles is handed out and returned exactly as before.
            for (int i = 0; i < paths.Length; i++)
            {
                var path = backend.CreatePath();
                var paint = backend.CreatePaint();
                path.Dispose();
                paint.Dispose();
            }

            AssertThat(PooledCount(backend, "_pathPool")).IsEqual(2);
            AssertThat(PooledCount(backend, "_paintPool")).IsEqual(2);
        }
        finally
        {
            backend.Dispose();
        }
    }

    // ── Handle state after pooling ───────────────────────────────────────────

    /// <summary>
    /// A paint handed back to the pool is reset before it is handed out again: the next caller must see
    /// the documented defaults (antialiased, black, no stroke, base alpha 255) instead of the previous
    /// caller's state - the old instance shipped its stroke width and its half-transparent base colour
    /// on. Headless-safe.
    /// </summary>
    [TestCase]
    public void APaintReturnedToThePoolIsHandedOutReset()
    {
        var backend = new SkiaCanvas2DBackend();
        try
        {
            var paint = backend.CreatePaint();
            paint.SetColor(new Color(0.5f, 0.25f, 0.125f, 0.4f))
                 .SetStrokeWidth(9f)
                 .SetAntiAlias(false)
                 .SetOpacity(0.5f)
                 .SetLineDash([4f, 2f]);
            paint.Dispose();

            var reused = backend.CreatePaint();
            AssertThat(ReferenceEquals(reused, paint)).IsTrue();   // it really is the pooled instance

            var skPaint = ((SkiaPaint2D)reused).Paint;
            AssertThat(skPaint.IsAntialias).IsTrue();
            AssertThat(skPaint.Color).IsEqual(SKColors.Black);
            AssertThat(skPaint.StrokeWidth).IsEqual(0f);
            AssertThat(skPaint.PathEffect is null).IsTrue();
            AssertThat(skPaint.Shader is null).IsTrue();

            // SetOpacity is relative to the base alpha SetColor stored, so a single call reaches the
            // requested value instead of decaying from the previous caller's paint.
            reused.SetColor(Colors.White);
            reused.SetOpacity(0.5f);
            AssertThat(skPaint.Color.Alpha).IsEqual(127);
            reused.SetOpacity(0.5f);   // raised twice: still relative to the base alpha, no decay
            AssertThat(skPaint.Color.Alpha).IsEqual(127);

            reused.Dispose();
            reused.Dispose();   // idempotent
        }
        finally
        {
            backend.Dispose();
        }
    }

    // ── Factory failures must not free the live objects ──────────────────────

    /// <summary>
    /// A dash pattern the factory rejects must not take the paint's live objects with it: the old order
    /// disposed the current effect <em>before</em> it called the factory, so a throw left the paint pointing
    /// at freed native memory - and a pooled paint is handed to the next caller, which then drew from it.
    /// <c>SetLinearGradient</c> / <c>SetRadialGradient</c> had the same order (a missing stop list throws
    /// after the shader was released). Headless-safe.
    /// </summary>
    [TestCase]
    public void ARejectedDashOrGradientUpdateKeepsThePaintsLiveObjects()
    {
        var backend = new SkiaCanvas2DBackend();
        try
        {
            var paint = backend.CreatePaint();
            paint.SetColor(Colors.White).SetLineDash([4f, 2f]);
            var skPaint = ((SkiaPaint2D)paint).Paint;
            var effect = skPaint.PathEffect;
            AssertThat(effect is not null).IsTrue();

            paint.SetLinearGradient(0f, 0f, 10f, 10f,
                [new GradientStop(0f, Colors.Black), new GradientStop(1f, Colors.White)]);
            var shader = skPaint.Shader;
            AssertThat(shader is not null).IsTrue();

            // A null pattern never reaches Skia: the binding rejects it, and the paint has to come out of it
            // holding the same live effect (the Handle of a disposed SkiaSharp object is Zero).
            AssertThat(Throws<ArgumentNullException>(() => paint.SetLineDash(null!)) is not null).IsTrue();
            AssertThat(ReferenceEquals(skPaint.PathEffect, effect)).IsTrue();
            AssertThat(skPaint.PathEffect!.Handle != IntPtr.Zero).IsTrue();

            // Same for a gradient without stops: the shader it had stays in place.
            AssertThat(Throws<ArgumentNullException>(() => paint.SetLinearGradient(0f, 0f, 1f, 1f, null!))
                is not null).IsTrue();
            AssertThat(ReferenceEquals(skPaint.Shader, shader)).IsTrue();
            AssertThat(skPaint.Shader!.Handle != IntPtr.Zero).IsTrue();

            paint.Dispose();
        }
        finally
        {
            backend.Dispose();
        }
    }

    /// <summary>
    /// <see cref="IPath2D.ArcTo"/> takes the sweep modulo a full turn, so <c>2π</c> collapses to an
    /// empty arc. The documented workaround is <c>2π - ε</c> (a complete circle); this pins both halves
    /// through the path's bounding box. Headless-safe.
    /// </summary>
    [TestCase]
    public void AnArcOfAWholeTurnCollapsesWhileJustUnderItDoesNot()
    {
        var backend = new SkiaCanvas2DBackend();
        try
        {
            using (var full = backend.CreatePath())
            {
                full.ArcTo(20f, 20f, 10f, 0f, MathF.Tau);
                AssertThat(((SkiaPath2D)full).SkPath.Bounds.Width).IsEqual(0f);
                AssertThat(((SkiaPath2D)full).SkPath.Bounds.Height).IsEqual(0f);
            }

            using (var almost = backend.CreatePath())
            {
                almost.ArcTo(20f, 20f, 10f, 0f, MathF.Tau - 1e-4f);
                var bounds = ((SkiaPath2D)almost).SkPath.Bounds;

                // A circle of radius 10: the box is ~20x20, not the empty box of the collapsed case.
                AssertThat(bounds.Width > 19f).IsTrue();
                AssertThat(bounds.Height > 19f).IsTrue();
            }
        }
        finally
        {
            backend.Dispose();
        }
    }
}
