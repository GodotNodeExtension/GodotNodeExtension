using System;
using System.Runtime.InteropServices;
using System.Threading;
using Godot;
using SkiaSharp;
using static GodotNodeExtension.Component.GodotSkia.VkInterop;
using Environment = System.Environment;

namespace GodotNodeExtension.Component.GodotSkia;

// source: https://github.com/MrJul/Estragonia/blob/main/src/JLeb.Estragonia/GodotVkSkiaGpu.cs#L94
// author: MrJul
// License: MIT

/// <summary>
/// SkiaCanvasTexture2D is a Texture2D that uses SkiaSharp for rendering.
/// It supports both GPU and CPU rendering modes, depending on the available rendering driver.
/// <para>
/// The type is <b>not</b> thread-safe and must be used from the main thread only: the canvas,
/// the layout transitions and the rebuild path all mutate unsynchronised state. The one lock it takes (the
/// process-wide Vulkan queue lock, held while a barrier is submitted) keeps the submissions of different
/// instances apart inside the frame - it is not a promise that an instance may be driven from another thread.
/// </para>
/// <para>
/// Cost model: all instances <b>share one reference-counted GRContext</b> (and therefore one Skia GPU
/// resource cache); set <see cref="ShareGrContext"/> to false for one context per texture. Each
/// instance still owns its own command pool/fences and performs one blocking <c>Flush/Submit(true)</c>
/// per frame (inherent to flush-based presentation), while barrier submissions are serialised by a
/// process-wide lock — the GPU fence wait happens <b>outside</b> that lock, so the submission path is never
/// held across a wait.
/// </para>
/// </summary>
[Tool]
[GlobalClass]
public partial class SkiaCanvasTexture2D : Texture2D
{
    private static string? _renderingDriverName;
    private static RenderingDevice? _renderingDevice;
    private static VkDevice? _vkDevice;
    private static VkPhysicalDevice? _vkPhysicalDevice;
    private static VkInstance? _vkInstance;
    private static VkQueue? _vkQueue;
    private static IntPtr? _vkQueueFamilyIndex;
    private static IntPtr? _vkLibrary;
    private static readonly Lock QueueLock = new();
    private static string RenderingDriverName =>
        RenderingDriverOverride is { } forced ? forced
        : _renderingDriverName ??= RenderingServer.GetCurrentRenderingDriverName();

    /// <summary>
    /// Test seam: forces the driver name <see cref="RenderingDriverName"/> reports, so the CPU fallback
    /// (<see cref="UseImageCopy"/>) can be exercised on a machine that has a real rendering device - the
    /// branch it selects is "a device, but not a Vulkan run", which cannot be produced by picking another
    /// driver. Null (the default) asks the engine. <see cref="ResetStaticState"/> clears it along with the
    /// rest of the process-wide state.
    /// </summary>
    internal static string? RenderingDriverOverride { get; set; }

    /// <summary>
    /// Message used whenever a surface cannot be created because this engine instance has no rendering
    /// device at all - a headless run (<c>--headless</c>) or <c>--rendering-driver dummy</c>.
    /// </summary>
    private const string NoRenderingDeviceMessage =
        "SkiaCanvasTexture2D needs a rendering device and this Godot instance has none (headless run, " +
        "or --rendering-driver dummy), so no Skia surface can be created. Run with a real rendering " +
        "driver, or use a host that tolerates the missing device (Canvas2DControl reports it as a " +
        "warning and keeps the node usable).";

    /// <summary>
    /// True when the engine has a rendering device, i.e. when a Skia surface can be created at all.
    /// <para>
    /// Check this before constructing a texture when the caller has to keep working without one (a
    /// tool script, a headless test, a demo page): the constructors themselves throw
    /// <see cref="InvalidOperationException"/> in that case, which is the right answer for a caller
    /// that cannot continue, but a poor one for a caller that can.
    /// </para>
    /// </summary>
    public static bool HasRenderingDevice => RenderingServer.GetRenderingDevice() is not null;

    /// <summary>
    /// The engine's rendering device. Throws instead of handing out <c>null</c>: every use below
    /// dereferences it, and the old null reference surfaced as a bare <c>NullReferenceException</c> with
    /// nothing in the message.
    /// </summary>
    /// <exception cref="InvalidOperationException">The engine has no rendering device.</exception>
    private static RenderingDevice RenderingDevice =>
        _renderingDevice ??= RenderingServer.GetRenderingDevice()
                            ?? throw new InvalidOperationException(NoRenderingDeviceMessage);

    private static VkDevice VkDevice => _vkDevice ??= new VkDevice((IntPtr)RenderingDevice.GetDriverResource(RenderingDevice.DriverResource.LogicalDevice, default, 0UL));
    private static VkPhysicalDevice VkPhysicalDevice => _vkPhysicalDevice ??= new VkPhysicalDevice((IntPtr)RenderingDevice.GetDriverResource(RenderingDevice.DriverResource.PhysicalDevice, default, 0UL));
    private static VkInstance VkInstance => _vkInstance ??= new VkInstance((IntPtr)RenderingDevice.GetDriverResource(RenderingDevice.DriverResource.TopmostObject, default, 0UL));
    private static VkQueue VkQueue => _vkQueue ??= new VkQueue((IntPtr)RenderingDevice.GetDriverResource(RenderingDevice.DriverResource.CommandQueue, default, 0UL));
    private static IntPtr VkQueueFamilyIndex => _vkQueueFamilyIndex ??= (IntPtr)RenderingDevice.GetDriverResource(RenderingDevice.DriverResource.QueueFamily, default, 0UL);
    private static IntPtr VkLibrary => _vkLibrary ??= TryLoadVulkanLibrary(out var vkLibrary) ? vkLibrary : throw new InvalidOperationException("Failed to load Vulkan library");


    private static bool TryLoadVulkanLibrary(out IntPtr handle)
    {
        if (OperatingSystem.IsWindows())
            return TryLoadByName("vulkan-1.dll", out handle);

        if (OperatingSystem.IsMacOS() || OperatingSystem.IsIOS())
        {
            return TryLoadByName("libvulkan.dylib", out handle)
                   || TryLoadByName("libvulkan.1.dylib", out handle)
                   || TryLoadByName("libMoltenVK.dylib", out handle)
                   || TryLoadByPath("vulkan.framework/vulkan", out handle)
                   || TryLoadByPath("MoltenVK.framework/MoltenVK", out handle)
                   || (Environment.GetEnvironmentVariable("DYLD_FALLBACK_LIBRARY_PATH") is null
                       && TryLoadByPath("/usr/local/lib/libvulkan.dylib", out handle)
                   );
        }

        return TryLoadByName("libvulkan.so.1", out handle)
               || TryLoadByName("libvulkan.so", out handle);

        static bool TryLoadByName(string libraryName, out IntPtr handle)
            => NativeLibrary.TryLoad(libraryName, typeof(SkiaCanvasTexture2D).Assembly, null, out handle);

        static bool TryLoadByPath(string libraryPath, out IntPtr handle)
            => NativeLibrary.TryLoad(libraryPath, out handle);
    }

    /// <summary>
    /// Texture width in pixels. Changes take effect on the next
    /// <see cref="Canvas"/> access or when <see cref="Resize"/> is called.
    /// Previous canvas content is discarded on rebuild.
    /// </summary>
    [Export]
    public int Width
    {
        get => _width;
        set
        {
            var clamped = Math.Max(value, 1);
            if (_width == clamped) return;
            _width = clamped;
            _needsRebuild = true;
        }
    }

    /// <summary>
    /// Texture height in pixels. Changes take effect on the next
    /// <see cref="Canvas"/> access or when <see cref="Resize"/> is called.
    /// Previous canvas content is discarded on rebuild.
    /// </summary>
    [Export]
    public int Height
    {
        get => _height;
        set
        {
            var clamped = Math.Max(value, 1);
            if (_height == clamped) return;
            _height = clamped;
            _needsRebuild = true;
        }
    }

    /// <summary>
    /// Gets the Skia canvas for drawing. In GPU mode, on first access after
    /// <see cref="UpdateTexture"/>, transitions the image layout to
    /// COLOR_ATTACHMENT_OPTIMAL. Subsequent accesses in the same draw batch
    /// return the canvas directly without repeating the transition.
    /// </summary>
    public SKCanvas? Canvas
    {
        get
        {
            if (_needsRebuild) Rebuild();
            if (_grContext?.IsAbandoned ?? false) return null;
            if (_needsTransition && _isGpuMode && _barrierHelper != null)
            {
                EnsureDrawLayout(waitForCompletion: false);
                _needsTransition = false;
            }
            return _skCanvas;
        }
    }
    /// <summary>
    /// Whether this texture is using GPU-accelerated rendering (true) or CPU fallback (false).
    /// </summary>
    public bool IsGpuMode => _isGpuMode;

    private Rid _rdTextureRid;
    private Rid _textureRid;
    private SKCanvas? _skCanvas;

    /// <summary>
    /// True when <see cref="_skCanvas"/> was created here (CPU fallback) and has to be disposed with it.
    /// A GPU path only borrows <c>SKSurface.Canvas</c>, which the surface owns.
    /// </summary>
    private bool _ownsCanvas;
    private SKSurface? _skSurface;
    private GRBackendRenderTarget? _backendRenderTarget;
    private GRBackendTexture? _backendTexture;
    private SKBitmap? _skBitmap;
    private int _width;
    private int _height;
    private bool _isGpuMode = true;
    private bool _disposed;
    private bool _needsRebuild;
    private GRContext? _grContext;
    private bool _usesSharedGrContext;

    /// <summary>Generation of the shared context this instance holds a reference to (see
    /// <see cref="_sGrContextGeneration"/>). -1 when the instance has a private context.</summary>
    private int _acquiredGrContextGeneration = -1;

    // ── Shared GRContext (M44) ────────────────────────────────────────────────
    // Every texture used to create its own GRContext, i.e. its own Skia GPU resource cache: N live
    // textures (chart + rich text + map + demos) meant N contexts. One context can drive several
    // surfaces, so it is now shared and reference counted; the last owner disposes it.
    private static readonly Lock SGrContextLock = new();
    private static GRContext? _sSharedGrContext;
    private static int _sSharedGrContextRefs;

    /// <summary>
    /// Bumped whenever the shared context is (re)created <b>and</b> whenever
    /// <see cref="ResetStaticState"/> drops it. An instance remembers the generation it acquired its
    /// reference from; a reference whose generation is no longer current must not decrement
    /// <see cref="_sSharedGrContextRefs"/>, otherwise a texture from before the reset would dispose the
    /// context the instances created after it are drawing into.
    /// </summary>
    private static int _sGrContextGeneration;

    /// <summary>
    /// Whether all textures share one GRContext (default). Set to false to fall back to one context
    /// per texture, e.g. to isolate a backend-specific problem.
    /// </summary>
    public static bool ShareGrContext { get; set; } = true;

    /// <summary>Number of live textures currently sharing the GRContext (diagnostics).</summary>
    internal static int SharedGrContextRefCount
    {
        get { lock (SGrContextLock) return _sSharedGrContextRefs; }
    }

    /// <summary>How often a GRContext was created (diagnostics: expected to stay at 1 per process).</summary>
    internal static int GrContextCreateCount { get; private set; }


    /// <summary>Acquire the shared context, creating it on first use.</summary>
    private GRContext AcquireSharedGrContext(Func<GRContext> create)
    {
        if (!ShareGrContext)
        {
            _usesSharedGrContext = false;
            return create();
        }

        lock (SGrContextLock)
        {
            if (_sSharedGrContext == null)
            {
                _sSharedGrContext = create();
                GrContextCreateCount++;
                _sGrContextGeneration++;
            }
            else if (_sSharedGrContext.IsAbandoned)
            {
                // An abandoned context cannot drive a new surface. While other textures still hold a
                // reference it stays in place (they release it on their way out, which is when refs reach
                // zero and it is disposed); this texture gets a private one instead. Disposing it here
                // would either be a use-after-free for those owners or, once the count was corrupted,
                // keep the process-wide context alive forever.
                if (_sSharedGrContextRefs > 0)
                {
                    _usesSharedGrContext = false;
                    return create();
                }

                _sSharedGrContext.Dispose();
                _sSharedGrContext = create();
                GrContextCreateCount++;
                _sGrContextGeneration++;
            }
            _sSharedGrContextRefs++;
            _usesSharedGrContext = true;
            _acquiredGrContextGeneration = _sGrContextGeneration;
            return _sSharedGrContext;
        }
    }

    /// <summary>Drop this texture's reference; the last owner disposes the shared context.</summary>
    private void ReleaseGrContext()
    {
        if (_usesSharedGrContext)
        {
            _usesSharedGrContext = false;
            lock (SGrContextLock)
            {
                // A reference from a generation that ResetStaticState() already dropped describes a
                // context that no longer exists: decrementing the current count would let this instance
                // dispose a context that other (newer) textures are still drawing into.
                if (_acquiredGrContextGeneration == _sGrContextGeneration
                    && --_sSharedGrContextRefs <= 0)
                {
                    _sSharedGrContextRefs = 0;
                    _sSharedGrContext?.Dispose();
                    _sSharedGrContext = null;
                }
            }
            _grContext = null;
            return;
        }

        _grContext?.Dispose();
        _grContext = null;
    }
    private VkBarrierHelper? _barrierHelper;
    private VkImage _vkImage;
    private VkImageLayout _lastLayout;
    private bool _needsTransition = true; // set after UpdateTexture; image layout needs transition back to COLOR_ATTACHMENT_OPTIMAL
    private bool _firstGpuUpdate = true; // first GPU update uses CPU sync to establish Godot's layout tracking

    /// <summary>Create a 512x512 canvas texture.</summary>
    /// <exception cref="InvalidOperationException">The engine has no rendering device (headless run).</exception>
    public SkiaCanvasTexture2D()
    {
        _width = 512;
        _height = 512;
        Initialize();
    }

    /// <summary>Create a canvas texture with the given pixel size (clamped to &gt;= 1).</summary>
    /// <param name="width">Texture width in pixels.</param>
    /// <param name="height">Texture height in pixels.</param>
    /// <exception cref="InvalidOperationException">The engine has no rendering device (headless run).</exception>
    public SkiaCanvasTexture2D(int width, int height)
    {
        _width = Math.Max(width, 1);
        _height = Math.Max(height, 1);
        Initialize();
    }

    private void Initialize()
    {
        // Fail with the reason before touching the device: without one every call below would throw a
        // NullReferenceException that says nothing about what is missing. Callers that can live without
        // a surface check HasRenderingDevice first.
        if (!HasRenderingDevice)
            throw new InvalidOperationException(NoRenderingDeviceMessage);

        try
        {
            _rdTextureRid = RenderingDevice.TextureCreate(
                new RDTextureFormat
            {
                Format = RenderingDevice.DataFormat.R8G8B8A8Unorm,
                TextureType = RenderingDevice.TextureType.Type2D,
                Samples = RenderingDevice.TextureSamples.Samples1,
                UsageBits = RenderingDevice.TextureUsageBits.CanUpdateBit |
                            RenderingDevice.TextureUsageBits.SamplingBit |
                            RenderingDevice.TextureUsageBits.CanCopyFromBit |
                            RenderingDevice.TextureUsageBits.CanCopyToBit |
                            RenderingDevice.TextureUsageBits.ColorAttachmentBit,
                Width = (uint)Width,
                Height = (uint)Height,
                Depth = 1,
                Mipmaps = 1,
                ArrayLayers = 1
            },
                new RDTextureView(), []);
            _textureRid = RenderingServer.TextureRdCreate(_rdTextureRid);
        }
        catch
        {
            // A half-created pair would leak the RD texture (TextureRdCreate can still fail on a lost
            // device), and the outer cleanup cannot see it from here.
            if (_rdTextureRid.IsValid) RenderingDevice.FreeRid(_rdTextureRid);
            _rdTextureRid = default;
            throw;
        }

        try
        {
            switch (RenderingDriverName)
            {
                case "vulkan":
                    try
                    {
                        VulkanCreateTexture();
                    }
                    catch (Exception ex)
                    {
                        GD.PrintErr("Error creating Skia canvas: ", ex.Message);
                        ReleaseGpuObjects();
                        UseImageCopy();
                    }
                    break;
                default:
                    // Every other renderer - including OpenGL (the compatibility renderer), which this
                    // component does not wrap: it is a mobile/web renderer, and a GL surface cannot be built
                    // or tested on a Vulkan desktop, where this component is developed.
                    UseImageCopy();
                    break;
            }
        }
        catch
        {
            if (_rdTextureRid.IsValid) RenderingDevice.FreeRid(_rdTextureRid);
            if (_textureRid.IsValid) RenderingServer.FreeRid(_textureRid);
            _rdTextureRid = default;
            _textureRid = default;
            // A surface may already hold a shared-context reference (the barrier/GL paths acquire before
            // they can fail): releasing it here keeps the process-wide count in step with the instances.
            ReleaseGrContext();
            throw;
        }
    }

    /// <summary>Texture width reported to Godot.</summary>
    public override int _GetWidth()
    {
        return _width;
    }

    /// <summary>Texture height reported to Godot.</summary>
    public override int _GetHeight()
    {
        return _height;
    }

    /// <summary>Godot texture RID backing this canvas.</summary>
    public override Rid _GetRid()
    {
        return _textureRid;
    }

    private void EnsureDrawLayout(bool waitForCompletion = true)
    {
        // Transition to COLOR_ATTACHMENT_OPTIMAL if not already there.
        // waitForCompletion=false is safe when Skia's Submit(true) follows,
        // as queue ordering guarantees the barrier completes before Skia's commands.
        TransitionLayoutTo(VkImageLayout.COLOR_ATTACHMENT_OPTIMAL, waitForCompletion);
    }

    /// <summary>
    /// Updates the texture with the current rendering data.
    /// In GPU mode, flushes Skia GPU commands, waits for completion, then
    /// transitions the Vulkan image layout for Godot sampling.
    /// In CPU mode, uploads the pixel data to the GPU.
    /// </summary>
    public void UpdateTexture()
    {
        // A released texture has nothing to upload, and touching its Skia objects after ReleaseResources
        // ranged from ObjectDisposedException to a native access violation.
        if (_disposed) return;

        if (_isGpuMode)
        {
            // Flush and WAIT for all Skia GPU work to complete.
            // Submit(true) blocks until the GPU finishes, ensuring the image
            // is fully written before we transition layout.
            _grContext?.Flush();
            _grContext?.Submit(true);

            // Transition to SHADER_READ_ONLY_OPTIMAL for Godot sampling
            TransitionLayoutTo(VkImageLayout.SHADER_READ_ONLY_OPTIMAL);
            _needsTransition = true;

            // On the first GPU update, also push pixel data through Godot's own
            // RenderingDevice.TextureUpdate pipeline. This establishes Godot's
            // internal VkImage layout tracking, preventing Godot from inserting
            // a destructive UNDEFINED -> SHADER_READ_ONLY barrier that discards
            // pixel content. After this sync, subsequent frames rely solely on
            // our own pipeline barriers.
            if (_firstGpuUpdate)
            {
                _firstGpuUpdate = false;
                SyncGodotTextureState();
            }
            return;
        }
        UpdateTextureCpu();
    }

    /// <summary>
    /// Reads pixel data from the Skia GPU surface and uploads it through
    /// Godot's RenderingDevice.TextureUpdate to synchronize Godot's internal
    /// layout tracking. Only called once (first frame).
    /// </summary>
    private void SyncGodotTextureState()
    {
        using var snapshot = _skSurface?.Snapshot();
        if (snapshot == null) return;
        // The image is only needed to seed Godot's layout tracking: dispose it right away
        // instead of leaking a full-size native image per texture.
        using var godotImage = snapshot.ToGodotImage();
        var data = godotImage.GetData();
        RenderingDevice.TextureUpdate(_rdTextureRid, 0, data);
    }

    private void UpdateTextureCpu()
    {
        if (_skBitmap == null) return;
        // Godot checks the byte count of the image it is handed and drops the whole upload when it does not
        // match the frame exactly, so the data has to be the frame's own bytes: the ArrayPool buffer this
        // used to pass on whole was rounded up (4096 bytes for a 37x21 frame), and every upload of such a
        // size failed with "Expected Image data size ... got ... instead" - the texture silently kept the
        // previous frame. Converting through the converter also leaves one SKBitmap -> Image implementation
        // instead of two.
        using var image = _skBitmap.ToGodotImage();
        RenderingServer.Texture2DUpdate(_textureRid, image, 0);
    }

    private void TransitionLayoutTo(VkImageLayout newLayout, bool waitForCompletion = true)
    {
        if (_barrierHelper == null || _lastLayout == newLayout) return;

        // Use access masks that mirror Skia's internal LayoutToSrcAccessMask
        // and LayoutToDstAccessMask (from GrVkImage.cpp) for correct synchronization.
        var sourceAccessMask = VkBarrierHelper.LayoutToSrcAccessMask(_lastLayout);
        var destAccessMask = VkBarrierHelper.LayoutToDstAccessMask(newLayout);

        lock (QueueLock)
        {
            // Record + submit only: the (potentially long) GPU fence wait happens outside the lock, so
            // the lock is never held across a GPU wait.
            _barrierHelper.TransitionImageLayout(_vkImage, _lastLayout, sourceAccessMask, newLayout, destAccessMask,
                waitForCompletion: false);
        }
        if (waitForCompletion)
        {
            bool completed = _barrierHelper.WaitForPendingBarrier();
            // Only record the new layout when the transition is known to have completed. On a fence
            // timeout the GPU may still be running the barrier, so claiming the new layout would make the
            // next transition skip a barrier that was never established - and pass the wrong oldLayout to
            // the one after that. UNDEFINED forces the next transition to state the layout from scratch.
            _lastLayout = completed ? newLayout : VkImageLayout.UNDEFINED;
            return;
        }

        _lastLayout = newLayout;
    }

    /// <summary>
    /// Resizes the texture to the specified dimensions immediately.
    /// Previous canvas content is discarded.
    /// </summary>
    /// <param name="width">New width in pixels (clamped to >= 1).</param>
    /// <param name="height">New height in pixels (clamped to >= 1).</param>
    public void Resize(int width, int height)
    {
        _width = Math.Max(width, 1);
        _height = Math.Max(height, 1);
        Rebuild();
    }

    private void Rebuild()
    {
        // Rebuilding after the resources were released would free the RIDs a second time.
        if (_disposed) return;

        _needsRebuild = false;

        // Release existing resources (skipping if never initialized)
        _barrierHelper?.Dispose();
        _barrierHelper = null;
        _skBitmap?.Dispose();
        _skBitmap = null;
        if (_ownsCanvas)
        {
            _skCanvas?.Dispose();
            _ownsCanvas = false;
        }
        _skCanvas = null;
        _skSurface?.Dispose();
        _skSurface = null;
        _backendRenderTarget?.Dispose();
        _backendRenderTarget = null;
        _backendTexture?.Dispose();
        _backendTexture = null;
        ReleaseGrContext();
        if (_rdTextureRid.IsValid) RenderingDevice.FreeRid(_rdTextureRid);
        if (_textureRid.IsValid) RenderingServer.FreeRid(_textureRid);
        // Invalidate them before Initialize() runs: a throwing re-init would otherwise leave freed RIDs
        // behind, and the next ReleaseResources/Rebuild would free them a second time.
        _rdTextureRid = default;
        _textureRid = default;

        // Reset state flags
        _isGpuMode = true;
        _needsTransition = true;
        _firstGpuUpdate = true;
        _lastLayout = VkImageLayout.UNDEFINED;

        // Re-create all resources
        Initialize();

        // A rebuild replaces the RD texture and therefore this texture's RID. The nodes presenting it
        // (TextureRect, Sprite2D, a custom _Draw, ...) generated their draw commands once and those commands
        // hold the RID they saw; they are connected to this signal so they re-issue the commands when it
        // fires. Without it a node keeps sampling the freed texture, and Godot answers with its default
        // texture - a blank white surface, while this texture's own pixels (GetImage) are perfectly fine.
        // ImageTexture.Update() announces its new content the same way.
        EmitChanged();
    }

    /// <summary>
    /// Pixel copy of the surface. This implements Godot's virtual, i.e. the callback behind
    /// <see cref="Texture2D.GetImage"/>, so the standard texture API works for every caller.
    /// <para>
    /// The class used to declare its own <c>GetImage()</c>, which merely hid the base method: code that
    /// held the texture as a <see cref="Texture2D"/> (a component reading pixels, a converter, a host)
    /// got <c>null</c> from <c>GetImage()</c> and crashed on the first dereference. Overriding the
    /// virtual fixes all of those call sites at once.
    /// </para>
    /// </summary>
    /// <returns>
    /// The current surface contents; an empty image when the surface is gone (disposed) or not created
    /// yet, so callers never have to special-case null.
    /// </returns>
    public override Image _GetImage()
    {
        if (_disposed)
            return CreateEmptyImage();

        if (_isGpuMode)
        {
            using var snapshot = _skSurface?.Snapshot();
            return snapshot != null ? snapshot.ToGodotImage() : CreateEmptyImage();
        }

        return _skBitmap != null ? _skBitmap.ToGodotImage() : CreateEmptyImage();
    }

    /// <summary>An empty RGBA8 image with the texture's current size (never smaller than one pixel).</summary>
    private Image CreateEmptyImage()
        => Image.CreateEmpty(Math.Max(1, Width), Math.Max(1, Height), false, Image.Format.Rgba8);

    /// <summary>
    /// Release deterministically on <c>Dispose()</c>. The type must not wait for
    /// <c>NotificationPredelete</c>: this texture's own RID keeps the native object alive, so the
    /// notification only fires once that RID is gone - which is precisely what
    /// <see cref="ReleaseResources"/> does. Without this override a plain <c>Dispose()</c> (the usual
    /// <c>using</c> idiom, and what the tests do) leaks the surface, both RIDs and one shared-GRContext
    /// reference per instance - measured by Test/GodotSkia/SkiaResourceLifecycleTest.cs.
    /// </summary>
    /// <param name="disposing">True when called from <c>Dispose()</c>.</param>
    protected override void Dispose(bool disposing)
    {
        if (disposing) ReleaseResources();
        base.Dispose(disposing);
    }

    /// <summary>
    /// Called by Godot when the object is about to be deleted.
    /// Releases all GPU and native resources.
    /// </summary>
    public override void _Notification(int what)
    {
        if (what == NotificationPredelete)
        {
            ReleaseResources();
        }
    }

    /// <summary>
    /// Explicitly release all GPU and native resources held by this texture.
    /// Safe to call multiple times. After calling, the texture is no longer usable.
    /// Call this before <see cref="GodotObject.Dispose()"/> when deterministic cleanup is needed
    /// (e.g., in <c>_ExitTree</c>) instead of relying on the GC → PreDelete chain.
    /// </summary>
    public void ReleaseResources()
    {
        if (_disposed) return;
        _disposed = true;

        _barrierHelper?.Dispose();
        _skBitmap?.Dispose();
        if (_ownsCanvas)
        {
            _skCanvas?.Dispose();
            _ownsCanvas = false;
        }
        _skCanvas = null;
        _skSurface?.Dispose();
        _backendRenderTarget?.Dispose();
        _backendTexture?.Dispose();
        ReleaseGrContext();

        // Drop the references as well: leaving them behind allowed a later Canvas/GetImage call to
        // touch already-disposed Skia objects (UpdateTextureCpu guards on exactly this field).
        _skBitmap = null;
        _skSurface = null;
        _backendRenderTarget = null;
        _backendTexture = null;
        _grContext = null;
        _barrierHelper = null;

        if (_rdTextureRid.IsValid) RenderingDevice.FreeRid(_rdTextureRid);
        if (_textureRid.IsValid) RenderingServer.FreeRid(_textureRid);
        // Invalidate both RIDs so a later Rebuild/FreeRid cannot free them twice.
        _rdTextureRid = default;
        _textureRid = default;
    }

    /// <summary>
    /// The two Vulkan entry points, in a type of their own. The enclosing type has an explicit static
    /// constructor, so its initializer runs on *any* static member access - including
    /// <see cref="HasRenderingDevice"/>. Resolving them there made that probe throw
    /// <c>TypeInitializationException</c> on a machine without a Vulkan loader (and cached the failure for
    /// the process) instead of answering "no device"; a nested type is only initialized when the Vulkan
    /// path actually asks for the pointers.
    /// </summary>
    private static class VkProcs
    {
        internal static readonly unsafe delegate* unmanaged[Stdcall]<VkInstance, byte*, IntPtr> GetInstanceProcAddr =
            (delegate* unmanaged[Stdcall]<VkInstance, byte*, IntPtr>)NativeLibrary.GetExport(VkLibrary, "vkGetInstanceProcAddr");

        internal static readonly unsafe delegate* unmanaged[Stdcall]<VkDevice, byte*, IntPtr> GetDeviceProcAddr =
            (delegate* unmanaged[Stdcall]<VkDevice, byte*, IntPtr>)NativeLibrary.GetExport(VkLibrary, "vkGetDeviceProcAddr");
    }

    /// <summary>
    /// Register the static-state release with the load context. The editor reloads this assembly in place
    /// (godot#78513): the cached rendering device, the Vulkan handles and the shared GRContext describe
    /// the device of the context that created them, so keeping them across a reload leaves native
    /// resources (and objects the engine already freed) behind.
    /// </summary>
    static SkiaCanvasTexture2D()
    {
        var context = System.Runtime.Loader.AssemblyLoadContext
            .GetLoadContext(typeof(SkiaCanvasTexture2D).Assembly);
        if (context is null) return;

        context.Unloading += _ => ResetStaticState();
        UnloadHookInstalled = true;
    }

    /// <summary>
    /// Whether the unload hook is registered. Test seam: the unload event cannot be raised from inside the
    /// process that is still using the assembly.
    /// </summary>
    internal static bool UnloadHookInstalled { get; private set; }

    /// <summary>Whether any device state is cached. Test seam for <see cref="ResetStaticState"/>.</summary>
    internal static bool HasCachedDeviceState =>
        _renderingDevice is not null || _vkDevice is not null || _sSharedGrContext is not null;

    /// <summary>
    /// Release the process-wide cached Godot/Vulkan device handles and clear the Godot-Font-ID
    /// typeface cache (only that one: the caches keyed by a family name or a code point - and the canvas
    /// backend's glyph-fallback cache - are deliberately kept, because their keys survive a reload and their
    /// typefaces belong to the process-wide font manager). The cached Vulkan
    /// library handle and procedure pointers are intentionally left in place, since they are only
    /// valid for the library they were resolved from.
    /// Call it from a shutdown path (e.g. before reloading the assembly) so the static references
    /// do not keep native resources alive. Everything is recreated on next use.
    /// </summary>
    public static void ResetStaticState()
    {
        if (SharedGrContextRefCount > 0)
            GD.PushWarning(
                $"SkiaCanvasTexture2D.ResetStaticState() ran while {SharedGrContextRefCount} texture(s) " +
                "still held the shared GRContext; those instances cannot render afterwards. Called from the " +
                "assembly reload hook this is expected - from anywhere else, release the textures first.");

        _renderingDriverName = null;
        RenderingDriverOverride = null;
        _renderingDevice = null;
        _vkDevice = null;
        _vkPhysicalDevice = null;
        _vkInstance = null;
        _vkQueue = null;
        _vkQueueFamilyIndex = null;

        // The shared GPU context dies with the process state it was created from. The reference count is
        // reset with it: the instances that still hold references belong to the generation being dropped,
        // and ReleaseGrContext() ignores their release (see _sGrContextGeneration) - so the count that
        // starts here covers only the instances created after this call.
        lock (SGrContextLock)
        {
            _sSharedGrContext?.Dispose();
            _sSharedGrContext = null;
            _sSharedGrContextRefs = 0;
            _sGrContextGeneration++;
        }

        SkiaGodotConverter.ClearTypefaceCache();
        // _vkLibrary and the cached procedure pointers stay: they are only valid for the library
        // handle they were resolved from.
    }
    private void VulkanCreateTexture()
    {
        var queueFamilyIndex = (uint)VkQueueFamilyIndex;
        _vkImage = GetVulkanImage();
        var vkFormat = GetVulkanFormat();

        _grContext = AcquireSharedGrContext(() => CreateVulkanGrContext(queueFamilyIndex));
        CreateVulkanSurface(queueFamilyIndex, vkFormat);
        InitializeVulkanBarrier(queueFamilyIndex);
    }

    private VkImage GetVulkanImage()
    {
        var texHandle = RenderingServer.TextureGetNativeHandle(_textureRid);
        if (texHandle == 0UL)
            throw new InvalidOperationException("Couldn't get Vulkan image from Godot texture");
        return new VkImage(texHandle);
    }

    private uint GetVulkanFormat()
    {
        var vkFormat = (uint)
            RenderingDevice.GetDriverResource(RenderingDevice.DriverResource.TextureDataFormat, _rdTextureRid, 0UL);
        if (vkFormat == 0U)
            throw new InvalidOperationException("Couldn't get Vulkan format from Godot texture");
        return vkFormat;
    }

    private GRContext CreateVulkanGrContext(uint queueFamilyIndex)
    {
        GRVkBackendContext vkBackendContext = new GRVkBackendContext
        {
            VkDevice = VkDevice.Handle,
            VkPhysicalDevice = VkPhysicalDevice.Handle,
            VkInstance = VkInstance.Handle,
            VkQueue = VkQueue.Handle,
            GraphicsQueueIndex = queueFamilyIndex,
            GetProcedureAddress = GetProcedureAddress
        };

        // Dispose the backend context once the GRContext has it: SkiaSharp pins the
        // GetProcedureAddress delegate (a strong GC handle) while the backend context is alive, and that
        // delegate's target is this texture - which keeps the whole assembly load context reachable, so a
        // hot reload of the project assembly can never unload it (godot#78513).
        GRContext? context = GRContext.CreateVulkan(vkBackendContext);
        vkBackendContext.Dispose();
        if (context == null)
            throw new InvalidOperationException("Vulkan GRContext creation failed");
        return context;
    }

    private void CreateVulkanSurface(uint queueFamilyIndex, uint vkFormat)
    {
        GRVkImageInfo vkImageInfo = new GRVkImageInfo
        {
            CurrentQueueFamily = queueFamilyIndex,
            Format = vkFormat,
            Image = _vkImage.Handle,
            ImageLayout = (uint)VkImageLayout.COLOR_ATTACHMENT_OPTIMAL,
            ImageTiling = (uint)VkImageTiling.OPTIMAL,
            ImageUsageFlags = (uint)(
                VkImageUsageFlags.SAMPLED_BIT |
                VkImageUsageFlags.TRANSFER_SRC_BIT |
                VkImageUsageFlags.TRANSFER_DST_BIT |
                VkImageUsageFlags.COLOR_ATTACHMENT_BIT
            ),
            LevelCount = 1,
            SampleCount = 1,
            Protected = false,
            SharingMode = (uint)VkSharingMode.EXCLUSIVE
        };

        // CRITICAL: Store SKSurface and GRBackendRenderTarget as fields to prevent
        // garbage collection. Without field references, GC can collect the surface
        // while the canvas is still in use, causing ACCESS_VIOLATION (0xC0000005)
        // in native Skia calls.
        _backendRenderTarget = new GRBackendRenderTarget(Width, Height, vkImageInfo);
        _skSurface = SKSurface.Create(_grContext,
            _backendRenderTarget,
            GRSurfaceOrigin.TopLeft,
            SKColorType.Rgba8888,
            new SKSurfaceProperties(SKPixelGeometry.RgbHorizontal));

        if (_skSurface == null)
        {
            _backendRenderTarget.Dispose();
            ReleaseGrContext();
            throw new InvalidOperationException("Vulkan SKSurface creation failed");
        }

        _skCanvas = _skSurface.Canvas;
        _isGpuMode = true;
    }

    private unsafe void InitializeVulkanBarrier(uint queueFamilyIndex)
    {
        var deviceApi = new VkDeviceApi(VkDevice, VkProcs.GetDeviceProcAddr);
        _barrierHelper = new VkBarrierHelper(VkDevice, VkQueue, deviceApi, queueFamilyIndex);

        _lastLayout = VkImageLayout.UNDEFINED;
        TransitionLayoutTo(VkImageLayout.COLOR_ATTACHMENT_OPTIMAL);
        _skCanvas!.Clear(SKColors.Transparent);
        _grContext!.Flush();
        _grContext.Submit(true);
        TransitionLayoutTo(VkImageLayout.SHADER_READ_ONLY_OPTIMAL);
    }

    private unsafe IntPtr GetProcedureAddress(string name, IntPtr instance, IntPtr device)
    {
        Span<byte> utf8Name = stackalloc byte[128];
        VkProcName.Encode(name, utf8Name);

        fixed (byte* utf8NamePtr = utf8Name)
        {
            return device != IntPtr.Zero
                ? VkProcs.GetDeviceProcAddr(new VkDevice(device), utf8NamePtr)
                : VkProcs.GetInstanceProcAddr(new VkInstance(instance), utf8NamePtr);
        }
    }

    /// <summary>
    /// Drop everything the GPU paths may have built (surface, render target, barrier helper, the context
    /// reference and the borrowed canvas) before another path takes over. The RIDs stay: they are created
    /// once per texture and are released by <see cref="ReleaseResources"/>.
    /// </summary>
    private void ReleaseGpuObjects()
    {
        _barrierHelper?.Dispose();
        _barrierHelper = null;
        _skSurface?.Dispose();
        _skSurface = null;
        _backendRenderTarget?.Dispose();
        _backendRenderTarget = null;
        _backendTexture?.Dispose();
        _backendTexture = null;
        _skCanvas = null;
        _ownsCanvas = false;
        _isGpuMode = false;
        ReleaseGrContext();
    }

    private void UseImageCopy()
    {
        // Unpremultiplied on purpose: UpdateTextureCpu copies the raw bytes into Godot's
        // Image.Format.Rgba8, which is straight alpha. Skia's default (Premul) storage would darken every
        // translucent pixel on the way in - the same trap SkiaGodotConverter.ToGodotImage documents.
        _skBitmap = new SKBitmap(Width, Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        _skCanvas = new SKCanvas(_skBitmap);
        _ownsCanvas = true;
        _isGpuMode = false;
    }
}