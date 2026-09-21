namespace GodotNodeExtension.Tests.GodotSkia;

using System.Collections.Generic;
using GdUnit4;
using Godot;
using GodotNodeExtension.Component.GodotSkia;
using GodotNodeExtension.Tests.Support;
using SkiaSharp;
using static GdUnit4.Assertions;

/// <summary>
/// End-to-end cases for <see cref="SkiaCanvasTexture2D"/> on the engine's <b>real</b> rendering device:
/// the surfaces are drawn into, uploaded, read back and compared, so the parts the unit suites cannot see -
/// the Vulkan layout bookkeeping, the command-buffer pool, the shared context and the CPU fallback - are
/// exercised by what they produce rather than by what they record.
/// <para>
/// The dedicated unit suites already cover the API contract (pixel access, size tracking, lifecycle
/// counters) and skip without a device; this one is the "does it still come out right" suite.
/// </para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class SkiaCanvasIntegrationTest
{
    /// <summary>Colours the multi-surface cases use: distinct enough that a mixed-up surface is obvious.</summary>
    private static readonly SKColor[] Palette =
    {
        new(220, 30, 30, 255),
        new(30, 220, 30, 255),
        new(30, 30, 220, 255),
        new(220, 220, 30, 255),
        new(220, 30, 220, 255),
        new(30, 220, 220, 255),
        new(255, 140, 0, 255),
        new(120, 60, 200, 255),
    };

    /// <summary>True when the run has a rendering device; otherwise the case skips itself.</summary>
    private static bool HasDevice(string caseName)
    {
        if (RenderingServer.GetRenderingDevice() is not null) return true;
        GD.Print($"[skip] {caseName}: no rendering device in this run");
        return false;
    }

    /// <summary>Draw a recognisable picture in <paramref name="colour"/> so a wrong surface is visible.</summary>
    private static void Draw(SkiaCanvasTexture2D texture, SKColor colour)
    {
        var canvas = texture.Canvas;
        if (canvas is null) return;

        canvas.Clear(colour);

        // A dark bar through the middle as well: two flat colours would hide a vertically mirrored upload.
        using var paint = new SKPaint { Color = new SKColor(10, 10, 10, 255), IsAntialias = false };
        canvas.DrawRect(new SKRect(4, 12, 28, 20), paint);

        texture.UpdateTexture();
    }

    /// <summary>The colour in the middle of the upper half, where only the clear colour landed.</summary>
    private static SKColor ReadTopHalf(SkiaCanvasTexture2D texture)
    {
        using var image = texture.GetImage();
        return image.GetPixel(8, 4).ToSkColor();
    }

    /// <summary>Number of pixels that differ between two images of the same size (-1 on a size mismatch).</summary>
    private static int DifferingPixels(Image first, Image second)
    {
        if (first.GetWidth() != second.GetWidth() || first.GetHeight() != second.GetHeight()) return -1;

        int differing = 0;
        for (int y = 0; y < first.GetHeight(); y++)
            for (int x = 0; x < first.GetWidth(); x++)
                if (first.GetPixel(x, y) != second.GetPixel(x, y))
                    differing++;
        return differing;
    }

    /// <summary>
    /// Several surfaces alive at once, each drawn every frame in turn: the per-instance layout bookkeeping and
    /// the shared queue have to keep them apart. A texture whose recorded layout drifted (or whose barrier was
    /// skipped) shows up as another texture's colour.
    /// </summary>
    [TestCase]
    public void InterleavedUpdatesKeepEverySurfaceOnItsOwnPixels()
    {
        if (!HasDevice(nameof(InterleavedUpdatesKeepEverySurfaceOnItsOwnPixels))) return;

        var textures = new List<SkiaCanvasTexture2D>();
        try
        {
            foreach (SKColor colour in Palette)
            {
                var texture = new SkiaCanvasTexture2D(32, 32);
                Draw(texture, colour);
                textures.Add(texture);
            }

            // Twenty frames of round-robin redrawing: every frame touches all of them, so their barriers
            // interleave on the shared queue and each one's `_lastLayout` bookkeeping is exercised.
            for (int frame = 0; frame < 20; frame++)
                for (int i = 0; i < textures.Count; i++)
                    Draw(textures[i], Palette[i]);

            var report = new List<string>();
            for (int i = 0; i < textures.Count; i++)
            {
                SKColor actual = ReadTopHalf(textures[i]);
                if (actual != Palette[i])
                    report.Add($"surface {i}: {actual} instead of {Palette[i]}");
            }

            AssertThat(string.Join("; ", report)).IsEqual("");
        }
        finally
        {
            foreach (var texture in textures) texture.Dispose();
        }
    }

    /// <summary>
    /// Resizing rebuilds the surface (and its RIDs): the rebuilt surface has to accept drawing again and
    /// report the new size, for both a growing and a shrinking texture.
    /// </summary>
    [TestCase]
    public void ResizingKeepsTheSurfaceDrawable()
    {
        if (!HasDevice(nameof(ResizingKeepsTheSurfaceDrawable))) return;

        using var texture = new SkiaCanvasTexture2D(24, 16);
        Draw(texture, Palette[0]);

        foreach (var (width, height) in new[] { (64, 48), (12, 9), (48, 32) })
        {
            texture.Resize(width, height);
            Draw(texture, Palette[1]);

            using var image = texture.GetImage();
            AssertThat(image.GetWidth()).IsEqual(width);
            AssertThat(image.GetHeight()).IsEqual(height);
            // Above the dark bar (which starts at y = 12), where only the clear colour landed - the middle of
            // a small texture is inside the bar, so the probe is tied to the picture, not to the centre.
            AssertThat(image.GetPixel(width / 2, 4).ToSkColor()).IsEqual(Palette[1]);
        }
    }

    /// <summary>
    /// The CPU fallback path (a rendering device, but not a Vulkan run - the branch
    /// <see cref="SkiaCanvasTexture2D.RenderingDriverOverride"/> exists to reach) must produce the same pixels
    /// as the GPU path, and must not hold a GPU context while it does.
    /// </summary>
    [TestCase]
    public void TheCpuFallbackPathProducesTheSamePixelsAsTheGpuPath()
    {
        if (!HasDevice(nameof(TheCpuFallbackPathProducesTheSamePixelsAsTheGpuPath))) return;

        int refsBefore = SkiaCanvasTexture2D.SharedGrContextRefCount;

        Image gpuPixels;
        using (var gpu = new SkiaCanvasTexture2D(32, 32))
        {
            AssertThat(gpu.IsGpuMode).IsTrue();
            Draw(gpu, Palette[3]);
            using var image = gpu.GetImage();
            gpuPixels = Image.CreateFromData(image.GetWidth(), image.GetHeight(), false, image.GetFormat(),
                image.GetData());
        }

        try
        {
            SkiaCanvasTexture2D.RenderingDriverOverride = "dummy";   // "a device", but not a Vulkan run

            using var cpu = new SkiaCanvasTexture2D(32, 32);
            AssertThat(cpu.IsGpuMode).IsFalse();

            // Pixel-reading surfaces are bitmap-backed, so the fallback must not take a GPU context.
            AssertThat(SkiaCanvasTexture2D.SharedGrContextRefCount).IsEqual(refsBefore);

            Draw(cpu, Palette[3]);
            using var cpuPixels = cpu.GetImage();

            AssertThat(DifferingPixels(gpuPixels, cpuPixels)).IsEqual(0);
        }
        finally
        {
            SkiaCanvasTexture2D.RenderingDriverOverride = null;
            SkiaCanvasTexture2D.ResetStaticState();
        }
    }

    /// <summary>
    /// The node presenting a resized surface is told about it: <see cref="TextureRect"/> subscribes itself to
    /// the texture's <c>changed</c> signal when the texture is assigned (that is how it knows to re-issue the
    /// draw command holding the old RID). This case pins both halves - the subscription exists, and the resize
    /// raises the signal it waits for - because "the surface's own pixels are fine" says nothing about what
    /// Godot samples (see the analysis note on presentation vs. <c>GetImage</c>).
    /// </summary>
    [TestCase]
    public void AResizedSurfaceAnnouncesItselfToTheNodePresentingIt()
    {
        if (!HasDevice(nameof(AResizedSurfaceAnnouncesItselfToTheNodePresentingIt))) return;

        var node = new TextureRect { Size = new Vector2(64, 48), Name = "PresentingNode" };
        ((SceneTree)Engine.GetMainLoop()).Root.AddChild(node);
        try
        {
            using var texture = new SkiaCanvasTexture2D(32, 32);

            int connectionsBefore = texture.GetSignalConnectionList(Resource.SignalName.Changed).Count;
            node.Texture = texture;
            int connectionsAfter = texture.GetSignalConnectionList(Resource.SignalName.Changed).Count;

            // The presenter listened up on its own - that is the mechanism the fix relies on - and it
            // listened exactly once (a "more than before" check would not notice a double subscription,
            // which would re-issue the draw command twice per resize).
            AssertThat(connectionsAfter).IsEqual(connectionsBefore + 1);

            int announced = 0;
            texture.Changed += () => announced++;

            texture.Resize(64, 48);
            AssertThat(announced).IsEqual(1);

            Draw(texture, Palette[2]);
            AssertThat(texture.Width).IsEqual(64);
            AssertThat(texture.Height).IsEqual(48);
        }
        finally
        {
            node.GetParent()?.RemoveChild(node);
            node.Free();
        }
    }

    /// <summary>
    /// Releasing a texture that went through the fallback has to be as clean as the GPU one: the bitmap and
    /// the canvas it owns are native objects too, and a leaked reference would show up in the counters.
    /// </summary>
    [TestCase]
    public void TheCpuFallbackIsReleasedWithTheTexture()
    {
        if (!HasDevice(nameof(TheCpuFallbackIsReleasedWithTheTexture))) return;

        int refsBefore = SkiaCanvasTexture2D.SharedGrContextRefCount;

        try
        {
            SkiaCanvasTexture2D.RenderingDriverOverride = "dummy";

            var texture = new SkiaCanvasTexture2D(16, 16);
            Draw(texture, Palette[5]);
            AssertThat(texture.IsGpuMode).IsFalse();

            texture.ReleaseResources();

            // Released twice on purpose: the fallback's own cleanup must be idempotent like the GPU one's.
            texture.ReleaseResources();
            texture.Dispose();
        }
        finally
        {
            SkiaCanvasTexture2D.RenderingDriverOverride = null;
            SkiaCanvasTexture2D.ResetStaticState();
        }

        AssertThat(SkiaCanvasTexture2D.SharedGrContextRefCount).IsEqual(refsBefore);
    }

    /// <summary>
    /// The CPU fallback uploads a frame whose byte count is <b>not</b> a power of two. Godot rejects an
    /// image whose length does not match the frame ("Expected Image data size ... got ... instead") and
    /// drops the upload, so a staging buffer that was rounded up (37x21 pixels is 3 108 bytes, an
    /// <c>ArrayPool</c> rent of it is 4 096) silently pins the texture to its previous frame.
    /// </summary>
    [TestCase]
    public void TheCpuFallbackUploadsAFrameThatIsNotAPowerOfTwo()
    {
        if (!HasDevice(nameof(TheCpuFallbackUploadsAFrameThatIsNotAPowerOfTwo))) return;

        try
        {
            SkiaCanvasTexture2D.RenderingDriverOverride = "dummy";   // "a device", but not a Vulkan run

            using var cpu = new SkiaCanvasTexture2D(37, 21);
            AssertThat(cpu.IsGpuMode).IsFalse();

            // The failed upload announces itself on the engine's own message stream, not in the pixel
            // readback: on this path GetImage() answers from the Skia-side bitmap either way.
            var log = EngineMessageLog.Attach();
            try
            {
                Draw(cpu, Palette[3]);

                using var pixels = cpu.GetImage();
                AssertThat(pixels.GetWidth()).IsEqual(37);
                AssertThat(pixels.GetHeight()).IsEqual(21);

                // Above the dark bar (which starts at y = 12), where only the clear colour landed.
                AssertThat(pixels.GetPixel(8, 4).ToSkColor()).IsEqual(Palette[3]);

                AssertThat(string.Join("; ", log.ErrorsContaining("Image data size"))).IsEqual("");
            }
            finally
            {
                log.Detach();
            }
        }
        finally
        {
            SkiaCanvasTexture2D.RenderingDriverOverride = null;
        }
    }

    /// <summary>
    /// A non-Vulkan renderer gets the CPU surface, and that includes OpenGL: the GL compatibility renderer is a
    /// mobile/web target this component does not wrap (a GL surface cannot be built or tested on the Vulkan
    /// desktops it is developed on), so a GL run degrades to the same CPU copy as any other driver -
    /// correctly, without an error and without taking a shared GRContext reference.
    /// </summary>
    [TestCase]
    public void ANonVulkanRendererUsesTheCpuSurface()
    {
        if (!HasDevice(nameof(ANonVulkanRendererUsesTheCpuSurface))) return;

        int refsBefore = SkiaCanvasTexture2D.SharedGrContextRefCount;

        try
        {
            SkiaCanvasTexture2D.RenderingDriverOverride = "opengl3";

            using var texture = new SkiaCanvasTexture2D(32, 32);
            AssertThat(texture.IsGpuMode).IsFalse();

            Draw(texture, Palette[2]);
            using var pixels = texture.GetImage();
            AssertThat(pixels.GetPixel(8, 4).ToSkColor()).IsEqual(Palette[2]);

            // The CPU path is bitmap-backed: it must not hold the shared GPU context.
            AssertThat(SkiaCanvasTexture2D.SharedGrContextRefCount).IsEqual(refsBefore);
        }
        finally
        {
            SkiaCanvasTexture2D.RenderingDriverOverride = null;
            SkiaCanvasTexture2D.ResetStaticState();
        }
    }

    /// <summary>
    /// <see cref="SkiaGodotConverter.ToSkImage"/>: the direction that reads a Godot texture. The pixels have to
    /// survive the conversion (it copies them explicitly), and the result has to be usable as a Skia image - so
    /// it is read back through the other direction instead of only having its size checked.
    /// </summary>
    [TestCase]
    public void ATextureConvertsToAnSkImageAndKeepsItsPixels()
    {
        if (!HasDevice(nameof(ATextureConvertsToAnSkImageAndKeepsItsPixels))) return;

        using var texture = new SkiaCanvasTexture2D(32, 32);
        Draw(texture, Palette[4]);                 // a known clear colour plus the dark bar through the middle

        using var skImage = ((Texture2D)texture).ToSkImage();
        AssertThat(skImage.Width).IsEqual(32);
        AssertThat(skImage.Height).IsEqual(32);

        using var back = skImage.ToGodotImage();
        AssertThat(back.GetWidth()).IsEqual(32);
        AssertThat(back.GetPixel(8, 4).ToSkColor()).IsEqual(Palette[4]);
        // The bar as well: the clear colour alone would also come back from a conversion that dropped the
        // drawing entirely (it is the surface's own clear colour).
        AssertThat(back.GetPixel(16, 16).ToSkColor()).IsEqual(new SKColor(10, 10, 10, 255));
    }

    /// <summary>
    /// Switching the reported driver between two lives of one texture (the override is consulted by
    /// <c>Initialize</c>, which <see cref="SkiaCanvasTexture2D.Resize"/> runs again) must not leave the old
    /// path's objects behind: the pixel readback has to follow the path that built the current surface.
    /// </summary>
    [TestCase]
    public void SwitchingPathsOnRebuildReadsTheRightPixels()
    {
        if (!HasDevice(nameof(SwitchingPathsOnRebuildReadsTheRightPixels))) return;

        using var texture = new SkiaCanvasTexture2D(32, 32);
        Draw(texture, Palette[1]);
        AssertThat(texture.IsGpuMode).IsTrue();
        using (var gpuImage = texture.GetImage())
            AssertThat(gpuImage.GetPixel(8, 4).ToSkColor()).IsEqual(Palette[1]);

        try
        {
            SkiaCanvasTexture2D.RenderingDriverOverride = "dummy";
            texture.Resize(32, 32);                  // rebuilds through the fallback
            AssertThat(texture.IsGpuMode).IsFalse();

            Draw(texture, Palette[6]);
            using var cpuImage = texture.GetImage();
            AssertThat(cpuImage.GetPixel(8, 4).ToSkColor()).IsEqual(Palette[6]);
        }
        finally
        {
            SkiaCanvasTexture2D.RenderingDriverOverride = null;
        }
    }
}
