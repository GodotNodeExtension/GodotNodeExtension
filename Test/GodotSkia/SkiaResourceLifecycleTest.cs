namespace GodotNodeExtension.Tests.GodotSkia;

using System;
using GdUnit4;
using Godot;
using GodotNodeExtension.Component.GodotSkia;
using SkiaSharp;
using static GdUnit4.Assertions;

/// <summary>
/// Resource-lifecycle cases for <see cref="SkiaCanvasTexture2D"/>: creating and releasing surfaces must
/// not accumulate native or managed state. The types own the counters these cases assert on (shared
/// GRContext reference count, GRContext creation count, cached static state), so a leak shows up as a
/// number that drifts instead of a machine that slowly runs out of memory.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class SkiaResourceLifecycleTest
{
    /// <summary>
    /// Cycles per case: enough that a per-instance leak is visible, small enough to stay quick. Measured
    /// on 2026-09-17: the managed heap growth is flat with the cycle count (472 KB after 50 cycles, 484 KB
    /// after 200), i.e. the budget below catches a per-instance leak, not GC noise.
    /// </summary>
    private const int Cycles = 50;

    /// <summary>Managed heap growth a hundred-surface worth of cycles may leave behind (bytes).</summary>
    private const long ManagedGrowthBudget = 2L * 1024 * 1024;

    /// <summary>True when the run has a rendering device; otherwise the case skips itself.</summary>
    private static bool HasDevice(string caseName)
    {
        if (RenderingServer.GetRenderingDevice() is not null) return true;
        GD.Print($"[skip] {caseName}: no rendering device in this run");
        return false;
    }

    private static void DrawOneFrame(SkiaCanvasTexture2D texture)
    {
        texture.Canvas!.Clear(new SKColor(12, 34, 56, 255));
        texture.UpdateTexture();
    }

    /// <summary>
    /// Fifty surfaces on and off the heap: the shared GRContext has to be acquired and released once per
    /// instance (never left behind), and the managed side must not grow with the cycle count.
    /// <para>
    /// Every release of the last owner disposes the context by design, so a strictly sequential loop
    /// creates one per cycle - that is the contract, not a leak. The sharing itself is asserted separately
    /// below, with several surfaces alive at once.
    /// </para>
    /// </summary>
    [TestCase]
    public void RepeatedCreateAndReleaseKeepsTheSharedContextBalanced()
    {
        if (!HasDevice(nameof(RepeatedCreateAndReleaseKeepsTheSharedContextBalanced))) return;

        int baselineRefs = SkiaCanvasTexture2D.SharedGrContextRefCount;
        int baselineCreates = SkiaCanvasTexture2D.GrContextCreateCount;
        long beforeBytes = GC.GetTotalMemory(forceFullCollection: true);

        for (int i = 0; i < Cycles; i++)
        {
            using var texture = new SkiaCanvasTexture2D(32, 32);
            DrawOneFrame(texture);

            // While the instance lives it holds exactly one reference to the shared context.
            AssertThat(SkiaCanvasTexture2D.SharedGrContextRefCount).IsEqual(baselineRefs + 1);
        }

        // Released every time: no reference survives the loop.
        AssertThat(SkiaCanvasTexture2D.SharedGrContextRefCount).IsEqual(baselineRefs);

        // Each cycle is the only owner, so it disposes the context when it goes - one create per cycle.
        int createGrowth = SkiaCanvasTexture2D.GrContextCreateCount - baselineCreates;
        GD.Print($"cycles {Cycles}: grcontext creates +{createGrowth}, refs {baselineRefs} -> " +
                 $"{SkiaCanvasTexture2D.SharedGrContextRefCount}");
        AssertThat(createGrowth).IsEqual(Cycles);

        long growth = GC.GetTotalMemory(forceFullCollection: true) - beforeBytes;
        GD.Print($"managed heap growth after {Cycles} cycles: {growth} bytes");
        AssertThat(growth < ManagedGrowthBudget).IsTrue();
    }

    /// <summary>
    /// Several surfaces alive at the same time share <b>one</b> GRContext (that is the point of the shared
    /// context: N charts used to mean N Skia GPU resource caches), and the last one to be released leaves
    /// the process clean.
    /// </summary>
    [TestCase]
    public void LiveSurfacesShareOneContextAndTheLastOneReleasesIt()
    {
        if (!HasDevice(nameof(LiveSurfacesShareOneContextAndTheLastOneReleasesIt))) return;

        int baselineRefs = SkiaCanvasTexture2D.SharedGrContextRefCount;
        int baselineCreates = SkiaCanvasTexture2D.GrContextCreateCount;

        var textures = new SkiaCanvasTexture2D[5];
        for (int i = 0; i < textures.Length; i++)
        {
            textures[i] = new SkiaCanvasTexture2D(16, 16);
            DrawOneFrame(textures[i]);
        }

        AssertThat(SkiaCanvasTexture2D.SharedGrContextRefCount).IsEqual(baselineRefs + textures.Length);

        // All of them fit in one context - the shared cache is not rebuilt per surface.
        int createGrowth = SkiaCanvasTexture2D.GrContextCreateCount - baselineCreates;
        GD.Print($"{textures.Length} live surfaces: grcontext creates +{createGrowth}");
        AssertThat(createGrowth).IsEqual(1);

        foreach (var texture in textures)
            texture.Dispose();

        AssertThat(SkiaCanvasTexture2D.SharedGrContextRefCount).IsEqual(baselineRefs);
    }

    /// <summary>
    /// Releasing twice is a no-op: <c>Dispose</c>, <c>ReleaseResources</c> and the assembly-reload hook can
    /// run in any order (a host that disposes explicitly, the hook, then the finalizer path), and none of
    /// them may release the same Skia object, RID or context reference twice.
    /// </summary>
    [TestCase]
    public void ReleasingTwiceIsANoOp()
    {
        if (!HasDevice(nameof(ReleasingTwiceIsANoOp))) return;

        int baselineRefs = SkiaCanvasTexture2D.SharedGrContextRefCount;

        var texture = new SkiaCanvasTexture2D(16, 16);
        DrawOneFrame(texture);
        AssertThat(SkiaCanvasTexture2D.SharedGrContextRefCount).IsEqual(baselineRefs + 1);

        texture.Dispose();
        AssertThat(SkiaCanvasTexture2D.SharedGrContextRefCount).IsEqual(baselineRefs);

        // The extra calls must not throw and must not touch the reference count again.
        texture.Dispose();
        texture.ReleaseResources();
        AssertThat(SkiaCanvasTexture2D.SharedGrContextRefCount).IsEqual(baselineRefs);
    }

    /// <summary>
    /// With every surface released, the reload hook drops the process-wide caches and leaves the <b>counters</b>
    /// consistent. The cached state itself is asserted by
    /// <c>SkiaCanvasTexture2DTest.TheAssemblyReloadHookReleasesTheCachedDeviceState</c> (which is also where the
    /// hook registration is checked): this case owns the reference count, that one owns the cache.
    /// </summary>
    [TestCase]
    public void ResettingTheStaticStateDropsTheSharedContext()
    {
        if (!HasDevice(nameof(ResettingTheStaticStateDropsTheSharedContext))) return;

        int baselineRefs = SkiaCanvasTexture2D.SharedGrContextRefCount;
        using (var texture = new SkiaCanvasTexture2D(8, 8))
        {
            DrawOneFrame(texture);
            AssertThat(SkiaCanvasTexture2D.SharedGrContextRefCount).IsEqual(baselineRefs + 1);
        }

        AssertThat(SkiaCanvasTexture2D.SharedGrContextRefCount).IsEqual(baselineRefs);

        SkiaCanvasTexture2D.ResetStaticState();

        AssertThat(SkiaCanvasTexture2D.SharedGrContextRefCount).IsEqual(baselineRefs);
    }

    /// <summary>
    /// The reload hook runs while surfaces are still alive (the editor reloads the assembly without asking
    /// the scene first). The instances from before the reset hold a reference to the context the reset
    /// dropped, so releasing them later must not decrement - let alone dispose - the context that the
    /// instances created after the reset are drawing into.
    /// </summary>
    [TestCase]
    public void ResettingWithLiveSurfacesDoesNotLetThemReleaseTheNextContext()
    {
        if (!HasDevice(nameof(ResettingWithLiveSurfacesDoesNotLetThemReleaseTheNextContext))) return;

        int baselineRefs = SkiaCanvasTexture2D.SharedGrContextRefCount;

        var stale = new SkiaCanvasTexture2D(16, 16);
        var alsoStale = new SkiaCanvasTexture2D(16, 16);
        DrawOneFrame(stale);
        DrawOneFrame(alsoStale);
        AssertThat(SkiaCanvasTexture2D.SharedGrContextRefCount).IsEqual(baselineRefs + 2);

        // The hook drops the shared context; the warning it pushes about the still-live instances is
        // expected here and is what makes the situation visible in the editor log.
        SkiaCanvasTexture2D.ResetStaticState();
        AssertThat(SkiaCanvasTexture2D.SharedGrContextRefCount).IsEqual(baselineRefs);

        // The next surface builds a fresh context and is its only owner.
        var fresh = new SkiaCanvasTexture2D(16, 16);
        DrawOneFrame(fresh);
        AssertThat(SkiaCanvasTexture2D.SharedGrContextRefCount).IsEqual(baselineRefs + 1);

        // Releasing the stale instances leaves the fresh context alone (a shared reference count that was
        // zeroed by the reset must not be decremented by the generation it no longer belongs to).
        stale.ReleaseResources();
        alsoStale.Dispose();
        AssertThat(SkiaCanvasTexture2D.SharedGrContextRefCount).IsEqual(baselineRefs + 1);

        // ...and it still renders, which is what "nobody disposed it" means in practice.
        fresh.Canvas!.Clear(new SKColor(7, 8, 9, 255));
        fresh.UpdateTexture();
        using var image = fresh.GetImage();
        SKColor pixel = image.GetPixel(8, 8).ToSkColor();
        AssertThat(pixel.Red).IsEqual((byte)7);
        AssertThat(pixel.Green).IsEqual((byte)8);
        AssertThat(pixel.Blue).IsEqual((byte)9);

        fresh.Dispose();
        AssertThat(SkiaCanvasTexture2D.SharedGrContextRefCount).IsEqual(baselineRefs);
    }
}
