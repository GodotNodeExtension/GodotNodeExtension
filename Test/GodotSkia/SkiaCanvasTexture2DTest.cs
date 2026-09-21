namespace GodotNodeExtension.Tests.GodotSkia;

using System;
using GdUnit4;
using Godot;
using GodotNodeExtension.Component.GodotSkia;
using SkiaSharp;
using static GdUnit4.Assertions;

/// <summary>
/// Behaviour specification for the pixel access of <see cref="SkiaCanvasTexture2D"/>: the texture has to
/// answer the *standard* Godot API. It used to declare its own <c>GetImage()</c> that only hid
/// <see cref="Texture2D.GetImage"/>, so a caller holding the texture as a <see cref="Texture2D"/> (a
/// component reading pixels, a converter, an editor thumbnail) received <c>null</c> and crashed; the
/// texture now implements the virtual instead.
/// <para>
/// Creating the surface needs a rendering device, so these cases skip themselves in a headless run.
/// </para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class SkiaCanvasTexture2DTest
{
    /// <summary>True when the run has a rendering device; otherwise the case skips itself.</summary>
    private static bool HasDevice(string caseName)
    {
        if (RenderingServer.GetRenderingDevice() is not null) return true;
        GD.Print($"[skip] {caseName}: no rendering device in this run");
        return false;
    }

    private static SKColor ReadPixel(SkiaCanvasTexture2D texture, int x, int y)
    {
        using var image = texture.GetImage();
        return image.GetPixel(x, y).ToSkColor();
    }

    [TestCase]
    public void TheStandardTextureApiReturnsTheSurfacePixels()
    {
        if (!HasDevice(nameof(TheStandardTextureApiReturnsTheSurfacePixels))) return;

        using var texture = new SkiaCanvasTexture2D(4, 3);
        var expected = new SKColor(200, 30, 90, 255);

        texture.Canvas!.Clear(expected);
        texture.UpdateTexture();

        // The base-typed call is the one that used to return null.
        using var image = texture.GetImage();
        AssertThat(image is not null).IsTrue();
        AssertThat(image!.GetWidth()).IsEqual(4);
        AssertThat(image.GetHeight()).IsEqual(3);

        var pixel = ReadPixel(texture, 2, 1);
        AssertThat(pixel.Red).IsEqual(200);
        AssertThat(pixel.Green).IsEqual(30);
        AssertThat(pixel.Blue).IsEqual(90);
    }

    [TestCase]
    public void TheBaseApiReportsSizeAndFollowsResize()
    {
        if (!HasDevice(nameof(TheBaseApiReportsSizeAndFollowsResize))) return;

        using var texture = new SkiaCanvasTexture2D(6, 2);
        var asBase = (Texture2D)texture;

        AssertThat(asBase.GetWidth()).IsEqual(6);
        AssertThat(asBase.GetHeight()).IsEqual(2);

        texture.Resize(8, 5);

        AssertThat(asBase.GetWidth()).IsEqual(8);
        AssertThat(asBase.GetHeight()).IsEqual(5);
        using var image = asBase.GetImage();
        AssertThat(image.GetWidth()).IsEqual(8);
        AssertThat(image.GetHeight()).IsEqual(5);
    }

    /// <summary>
    /// The RID Godot presents for this texture: valid, stable, one per instance, and the same one
    /// <c>GetRid()</c> reports - a wrapper that handed out someone else's RID would make the
    /// engine draw another texture's pixels.
    /// </summary>
    [TestCase]
    public void TheRidIsValidAndOwnedByTheInstance()
    {
        if (!HasDevice(nameof(TheRidIsValidAndOwnedByTheInstance))) return;

        using var first = new SkiaCanvasTexture2D(8, 8);
        using var second = new SkiaCanvasTexture2D(8, 8);

        var rid = first._GetRid();

        AssertThat(rid.IsValid).IsTrue();
        AssertThat(first._GetRid() == rid).IsTrue();
        AssertThat(second._GetRid() == rid).IsFalse();
        AssertThat(first.GetRid() == rid).IsTrue();
    }

    /// <summary>
    /// Sizes are clamped to at least one pixel: a zero-sized Skia surface cannot be created, so 0 (or a
    /// negative size, e.g. from a layout that has not been sized yet) has to land on a usable 1x1 surface
    /// instead of failing later.
    /// </summary>
    [TestCase]
    public void SizesAreClampedToAtLeastOnePixel()
    {
        if (!HasDevice(nameof(SizesAreClampedToAtLeastOnePixel))) return;

        using var texture = new SkiaCanvasTexture2D(0, -4);
        AssertThat(texture.Width).IsEqual(1);
        AssertThat(texture.Height).IsEqual(1);

        texture.Width = -3;
        texture.Height = 0;
        AssertThat(texture.Width).IsEqual(1);
        AssertThat(texture.Height).IsEqual(1);

        texture.Resize(0, -9);
        AssertThat(texture.Width).IsEqual(1);
        AssertThat(texture.Height).IsEqual(1);

        // ...and the clamped size is a usable surface, not just a property value.
        using var canvas = new SkiaCanvasTexture2D(-2, 0);
        canvas.Canvas!.Clear(new SKColor(11, 22, 33, 255));
        canvas.UpdateTexture();
        AssertThat(ReadPixel(canvas, 0, 0).Red).IsEqual((byte)11);
    }

    [TestCase]
    public void TheAssemblyReloadHookReleasesTheCachedDeviceState()
    {
        AssertThat(SkiaCanvasTexture2D.UnloadHookInstalled).IsTrue();

        if (!HasDevice(nameof(TheAssemblyReloadHookReleasesTheCachedDeviceState))) return;

        // Drawing caches the rendering device, the Vulkan handles and the shared GPU context.
        var texture = new SkiaCanvasTexture2D(4, 4);
        texture.Canvas!.Clear(new SKColor(10, 20, 30, 255));
        texture.UpdateTexture();
        AssertThat(SkiaCanvasTexture2D.HasCachedDeviceState).IsTrue();

        // Release the texture first: the shared context the cache holds is about to be disposed.
        texture.Dispose();

        // This is what the unload hook calls, and what keeps the editor able to unload the assembly.
        SkiaCanvasTexture2D.ResetStaticState();
        AssertThat(SkiaCanvasTexture2D.HasCachedDeviceState).IsFalse();

        // The hook can run after an explicit reset, so the release has to be idempotent.
        SkiaCanvasTexture2D.ResetStaticState();
        AssertThat(SkiaCanvasTexture2D.HasCachedDeviceState).IsFalse();
    }

    [TestCase]
    public void AReleasedTextureAnswersWithAnEmptyImageOfItsSize()
    {
        if (!HasDevice(nameof(AReleasedTextureAnswersWithAnEmptyImageOfItsSize))) return;

        var texture = new SkiaCanvasTexture2D(3, 3);
        texture.ReleaseResources();

        // Callers must not have to special-case the released state: the fallback image keeps the
        // texture's size and is fully transparent, so a pixel read-back sees "nothing was drawn"
        // rather than a black square or an off-by-one placeholder.
        using var image = texture.GetImage();
        AssertThat(image is not null).IsTrue();
        AssertThat(image!.GetWidth()).IsEqual(3);
        AssertThat(image.GetHeight()).IsEqual(3);
        AssertThat(image.GetPixel(0, 0).A).IsEqual(0f);
        AssertThat(image.GetPixel(2, 2).A).IsEqual(0f);

        texture.Dispose();
    }

    [TestCase]
    public void UpdatingAReleasedTextureIsSafe()
    {
        if (!HasDevice(nameof(UpdatingAReleasedTextureIsSafe))) return;

        var texture = new SkiaCanvasTexture2D(4, 4);
        texture.Canvas!.Clear(new SKColor(1, 2, 3, 255));
        texture.UpdateTexture();

        texture.ReleaseResources();

        // A host that released the surface (an _ExitTree handler, the assembly-reload hook) can still
        // reach a frame: the upload has to be a no-op instead of touching freed Skia objects.
        texture.UpdateTexture();

        using var image = texture.GetImage();
        AssertThat(image.GetWidth()).IsEqual(4);
        texture.Dispose();
    }

    [TestCase]
    public void AWidthOrHeightChangeTakesEffectOnTheNextCanvasAccess()
    {
        if (!HasDevice(nameof(AWidthOrHeightChangeTakesEffectOnTheNextCanvasAccess))) return;

        using var texture = new SkiaCanvasTexture2D(4, 2);
        texture.Canvas!.Clear(SKColors.Red);   // the surface exists from here on

        texture.Width = 6;

        // Godot sees the new size immediately ...
        AssertThat(texture.GetWidth()).IsEqual(6);
        // ... while the surface behind it is rebuilt lazily: until the next Canvas access it is still
        // the old one, and only that access throws the old content away and resizes it.
        AssertThat(texture.GetImage().GetWidth()).IsEqual(4);
        AssertThat(texture.Canvas is not null).IsTrue();
        using var rebuilt = texture.GetImage();
        AssertThat(rebuilt.GetWidth()).IsEqual(6);
        AssertThat(rebuilt.GetHeight()).IsEqual(2);

        // Same contract for the height setter.
        texture.Height = 5;
        AssertThat(texture.GetHeight()).IsEqual(5);
        using var stale = texture.GetImage();
        AssertThat(stale.GetHeight()).IsEqual(2);
        AssertThat(texture.Canvas is not null).IsTrue();
        using var rebuiltAgain = texture.GetImage();
        AssertThat(rebuiltAgain.GetWidth()).IsEqual(6);
        AssertThat(rebuiltAgain.GetHeight()).IsEqual(5);
    }

    [TestCase]
    public void PredeleteReleasesTheSurfaceAndLeavesTheTextureSafe()
    {
        if (!HasDevice(nameof(PredeleteReleasesTheSurfaceAndLeavesTheTextureSafe))) return;

        var texture = new SkiaCanvasTexture2D(4, 4);
        texture.Canvas!.Clear(SKColors.Red);

        texture._Notification((int)GodotObject.NotificationPredelete);

        // Same state as an explicit release: reads still answer and the later Dispose is a no-op
        // instead of freeing the RIDs a second time.
        using var image = texture.GetImage();
        AssertThat(image.GetWidth()).IsEqual(4);
        texture.UpdateTexture();
        texture.Dispose();
        texture.Dispose();
    }

    /// <summary>
    /// The reload hook drops the process-wide device state, not the ability to render: the next surface
    /// has to rebuild that state (a fresh GRContext) and produce real pixels again.
    /// </summary>
    [TestCase]
    public void TheDeviceStateIsRebuiltAfterAReset()
    {
        if (!HasDevice(nameof(TheDeviceStateIsRebuiltAfterAReset))) return;

        SkiaCanvasTexture2D.ResetStaticState();
        AssertThat(SkiaCanvasTexture2D.HasCachedDeviceState).IsFalse();

        int createsBefore = SkiaCanvasTexture2D.GrContextCreateCount;
        var expected = new SKColor(20, 120, 200, 255);

        using var texture = new SkiaCanvasTexture2D(8, 6);
        texture.Canvas!.Clear(expected);
        texture.UpdateTexture();

        AssertThat(SkiaCanvasTexture2D.HasCachedDeviceState).IsTrue();
        AssertThat(SkiaCanvasTexture2D.GrContextCreateCount).IsEqual(createsBefore + 1);

        using var image = texture.GetImage();
        var pixel = image.GetPixel(3, 3).ToSkColor();
        AssertThat(pixel.Red).IsEqual((byte)20);
        AssertThat(pixel.Green).IsEqual((byte)120);
        AssertThat(pixel.Blue).IsEqual((byte)200);
    }

    /// <summary>
    /// The constructors need a rendering device, so a headless run cannot build a surface. That failure
    /// has to name the cause - it used to be a <c>NullReferenceException</c> raised while dereferencing
    /// the missing device, which pointed at an engine call and explained nothing.
    /// </summary>
    [TestCase]
    public void CreatingASurfaceWithoutARenderingDeviceReportsTheCause()
    {
        if (SkiaCanvasTexture2D.HasRenderingDevice)
        {
            // With a device the probe and the construction agree: the guard does not block the real path.
            using var texture = new SkiaCanvasTexture2D(4, 3);
            AssertThat(texture.Width).IsEqual(4);
            AssertThat(texture.Height).IsEqual(3);
            return;
        }

        var ex = ThrowsInvalidOperation(() => _ = new SkiaCanvasTexture2D(4, 3));

        AssertThat(ex is not null).IsTrue();
        AssertThat(ex!.Message.Contains("rendering device")).IsTrue();
        AssertThat(ex.Message.Contains("headless")).IsTrue();
    }

    private static InvalidOperationException? ThrowsInvalidOperation(Action action)
    {
        try { action(); }
        catch (InvalidOperationException ex) { return ex; }
        return null;
    }

    /// <summary>
    /// Changing the size announces the new texture. A node that presents this texture (a <c>TextureRect</c>, a
    /// <c>Sprite2D</c>, a custom <c>_Draw</c>) generated its draw commands once, and those commands hold the
    /// RID they saw; the node types subscribe to the texture's <c>changed</c> signal to re-issue them. A
    /// rebuild replaces both RIDs, so a silent rebuild left the presenter sampling a freed texture - which
    /// Godot answers with its default texture, i.e. a blank white surface, while this texture's own pixels
    /// stayed correct. <c>ImageTexture.Update()</c> announces itself the same way.
    /// </summary>
    [TestCase]
    public void ChangingTheSizeAnnouncesTheNewTexture()
    {
        if (!HasDevice(nameof(ChangingTheSizeAnnouncesTheNewTexture))) return;

        using var texture = new SkiaCanvasTexture2D(16, 12);
        int announced = 0;
        texture.Changed += () => announced++;

        texture.Resize(40, 30);
        AssertThat(texture.Width).IsEqual(40);
        AssertThat(texture.Height).IsEqual(30);
        AssertThat(announced).IsEqual(1);

        // The lazy path announces it too: a property change rebuilds on the next Canvas access, which is the
        // documented trigger (reading pixels without touching Canvas still reflects the old surface).
        texture.Width = 50;
        AssertThat(texture.Canvas is not null).IsTrue();
        AssertThat(texture.Width).IsEqual(50);
        AssertThat(announced).IsEqual(2);

        // Resize() always rebuilds - even to a size the surface already has - so it announces that rebuild as
        // well: the signal has to arrive exactly when the RIDs change, and that is what this one did. (The
        // canvas host, Canvas2DControl.ResizeCanvas, is the layer that avoids the pointless rebuild.)
        texture.Resize(50, texture.Height);
        AssertThat(announced).IsEqual(3);
    }
}
