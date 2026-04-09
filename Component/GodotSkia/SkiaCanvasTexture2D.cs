using System;
using System.Buffers;
using System.Runtime.InteropServices;
using System.Text.Unicode;
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
    private static readonly Lock _queueLock = new();
    private static string RenderingDriverName => _renderingDriverName ??= RenderingServer.GetCurrentRenderingDriverName();
    private static RenderingDevice RenderingDevice => _renderingDevice ??= RenderingServer.GetRenderingDevice();
    private static VkDevice VkDevice => _vkDevice ??= new VkDevice((IntPtr)RenderingDevice.GetDriverResource(RenderingDevice.DriverResource.LogicalDevice, default, 0UL));
    private static VkPhysicalDevice VkPhysicalDevice => _vkPhysicalDevice ??= new VkPhysicalDevice((IntPtr)RenderingDevice.GetDriverResource(RenderingDevice.DriverResource.PhysicalDevice, default, 0UL));
    private static VkInstance VkInstance => _vkInstance ??= new VkInstance((IntPtr)RenderingDevice.GetDriverResource(RenderingDevice.DriverResource.TopmostObject, default, 0UL));
    private static VkQueue VkQueue => _vkQueue ??= new VkQueue((IntPtr)RenderingDevice.GetDriverResource(RenderingDevice.DriverResource.CommandQueue, default, 0UL));
    private static IntPtr VkQueueFamilyIndex => _vkQueueFamilyIndex ??= (IntPtr)RenderingDevice.GetDriverResource(RenderingDevice.DriverResource.QueueFamily, default, 0UL);
    private static IntPtr VkLibrary => _vkLibrary ??= TryLoadVulkanLibrary(out var vkLibrary) ? vkLibrary : throw new InvalidOperationException("Failed to load Vulkan library");

    private static readonly unsafe delegate* unmanaged[Stdcall]<VkInstance, byte*, IntPtr> VkGetInstanceProcAddr =
        (delegate* unmanaged[Stdcall]<VkInstance, byte*, IntPtr>)NativeLibrary.GetExport(VkLibrary, "vkGetInstanceProcAddr");
    private static readonly unsafe delegate* unmanaged[Stdcall]<VkDevice, byte*, IntPtr> VkGetDeviceProcAddr =
        (delegate* unmanaged[Stdcall]<VkDevice, byte*, IntPtr>)NativeLibrary.GetExport(VkLibrary, "vkGetDeviceProcAddr");

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
            if (_grContext?.IsAbandoned == true) return null;
            if (_needsTransition && _isGpuMode && _barrierHelper != null)
            {
                EnsureDrawLayout(waitForCompletion: false);
                _needsTransition = false;
            }
            return _skCanvas;
        }
    }
    /// <summary>
    /// Whether the canvas content has changed and needs to be redrawn.
    /// </summary>
    public bool Dirty => _dirty;

    /// <summary>
    /// Whether this texture is using GPU-accelerated rendering (true) or CPU fallback (false).
    /// </summary>
    public bool IsGpuMode => _isGpuMode;

    private Rid _rdTextureRid;
    private Rid _textureRid;
    private SKCanvas? _skCanvas;
    private SKSurface? _skSurface;
    private GRBackendRenderTarget? _backendRenderTarget;
    private GRBackendTexture? _backendTexture;
    private SKBitmap? _skBitmap;
    private int _width;
    private int _height;
    private bool _dirty = true;
    private bool _isGpuMode = true;
    private bool _disposed;
    private bool _needsRebuild;
    private GRContext? _grContext;
    private VkBarrierHelper? _barrierHelper;
    private VkImage _vkImage;
    private VkImageLayout _lastLayout;
    private bool _needsTransition = true; // set after UpdateTexture; image layout needs transition back to COLOR_ATTACHMENT_OPTIMAL
    private bool _firstGpuUpdate = true; // first GPU update uses CPU sync to establish Godot's layout tracking

    public SkiaCanvasTexture2D()
    {
        _width = 512;
        _height = 512;
        Initialize();
    }

    public SkiaCanvasTexture2D(int width, int height)
    {
        _width = Math.Max(width, 1);
        _height = Math.Max(height, 1);
        Initialize();
    }

    private void Initialize()
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
                        UseImageCopy();
                    }
                    break;
                case "opengl3":
                    try
                    {
                        var texHandle = RenderingServer.TextureGetNativeHandle(_textureRid);
                        if (texHandle != 0UL)
                            OpenGlCreateTexture(texHandle);
                        else
                            UseImageCopy();
                    }
                    catch (Exception ex)
                    {
                        GD.PrintErr("Error creating OpenGL Skia canvas: ", ex.Message);
                        UseImageCopy();
                    }
                    break;
                default:
                    UseImageCopy();
                    break;
            }
        }
        catch
        {
            if (_rdTextureRid.IsValid) RenderingDevice.FreeRid(_rdTextureRid);
            if (_textureRid.IsValid) RenderingServer.FreeRid(_textureRid);
            throw;
        }
    }

    public override int _GetWidth()
    {
        return _width;
    }

    public override int _GetHeight()
    {
        return _height;
    }

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
        var godotImage = snapshot.ToGodotImage();
        var data = godotImage.GetData();
        RenderingDevice.TextureUpdate(_rdTextureRid, 0, data);
    }

    private void UpdateTextureCpu()
    {
        if (_skBitmap == null) return;
        IntPtr pixels = _skBitmap.GetPixels();
        int dataSize = _skBitmap.ByteCount;
        var pixelBytes = ArrayPool<byte>.Shared.Rent(dataSize);
        try
        {
            Marshal.Copy(pixels, pixelBytes, 0, dataSize);
            var image = Image.CreateFromData(_skBitmap.Width, _skBitmap.Height, false, Image.Format.Rgba8, pixelBytes);
            RenderingServer.Texture2DUpdate(_textureRid, image, 0);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(pixelBytes);
        }
    }

    private void TransitionLayoutTo(VkImageLayout newLayout, bool waitForCompletion = true)
    {
        if (_barrierHelper == null || _lastLayout == newLayout) return;

        // Use access masks that mirror Skia's internal LayoutToSrcAccessMask
        // and LayoutToDstAccessMask (from GrVkImage.cpp) for correct synchronization.
        var sourceAccessMask = VkBarrierHelper.LayoutToSrcAccessMask(_lastLayout);
        var destAccessMask = VkBarrierHelper.LayoutToDstAccessMask(newLayout);

        lock (_queueLock)
        {
            _barrierHelper.TransitionImageLayout(_vkImage, _lastLayout, sourceAccessMask, newLayout, destAccessMask,
                waitForCompletion: waitForCompletion);
        }
        _lastLayout = newLayout;
    }

    /// <summary>
    ///  Marks the canvas as dirty, indicating that it needs to be redrawn.
    /// </summary>
    public void MarkDirty()
    {
        _dirty = true;
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
        _needsRebuild = false;

        // Release existing resources (skipping if never initialized)
        _barrierHelper?.Dispose();
        _barrierHelper = null;
        _skBitmap?.Dispose();
        _skBitmap = null;
        _skCanvas = null;
        _skSurface?.Dispose();
        _skSurface = null;
        _backendRenderTarget?.Dispose();
        _backendRenderTarget = null;
        _backendTexture?.Dispose();
        _backendTexture = null;
        _grContext?.Dispose();
        _grContext = null;
        if (_rdTextureRid.IsValid) RenderingDevice.FreeRid(_rdTextureRid);
        if (_textureRid.IsValid) RenderingServer.FreeRid(_textureRid);

        // Reset state flags
        _isGpuMode = true;
        _dirty = true;
        _needsTransition = true;
        _firstGpuUpdate = true;
        _lastLayout = VkImageLayout.UNDEFINED;

        // Re-create all resources
        Initialize();
    }

    /// <summary>
    /// Gets the current image from the Skia canvas.
    /// </summary>
    /// <returns>An Image object representing the current state of the Skia canvas.</returns>
    public new Image GetImage()
    {
        if (_isGpuMode)
        {
            using var snapshot = _skSurface?.Snapshot();
            return snapshot?.ToGodotImage() ?? Image.CreateEmpty(Width, Height, false, Image.Format.Rgba8);
        }
        return _skBitmap?.ToGodotImage() ?? Image.CreateEmpty(Width, Height, false, Image.Format.Rgba8);
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

    private void ReleaseResources()
    {
        if (_disposed) return;
        _disposed = true;

        GD.Print("Free skTex");
        _barrierHelper?.Dispose();
        _skBitmap?.Dispose();
        _skCanvas = null;
        _skSurface?.Dispose();
        _backendRenderTarget?.Dispose();
        _backendTexture?.Dispose();
        _grContext?.Dispose();
        if (_rdTextureRid.IsValid) RenderingDevice.FreeRid(_rdTextureRid);
        if (_textureRid.IsValid) RenderingServer.FreeRid(_textureRid);
    }
    private void VulkanCreateTexture()
    {
        var queueFamilyIndex = (uint)VkQueueFamilyIndex;
        _vkImage = GetVulkanImage();
        var vkFormat = GetVulkanFormat();

        _grContext = CreateVulkanGRContext(queueFamilyIndex);
        CreateVulkanSurface(queueFamilyIndex, vkFormat);
        InitializeVulkanBarrier(queueFamilyIndex, vkFormat);
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

    private GRContext CreateVulkanGRContext(uint queueFamilyIndex)
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

        var context = GRContext.CreateVulkan(vkBackendContext);
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
            _grContext!.Dispose();
            _grContext = null;
            throw new InvalidOperationException("Vulkan SKSurface creation failed");
        }

        _skCanvas = _skSurface.Canvas;
        _isGpuMode = true;
    }

    private unsafe void InitializeVulkanBarrier(uint queueFamilyIndex, uint vkFormat)
    {
        var deviceApi = new VkDeviceApi(VkDevice, VkGetDeviceProcAddr);
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

        // The stackalloc buffer should always be sufficient for proc names
        if (Utf8.FromUtf16(name, utf8Name[..^1], out _, out var bytesWritten) != OperationStatus.Done)
            throw new InvalidOperationException($"Invalid proc name {name}");

        utf8Name[bytesWritten] = 0;

        fixed (byte* utf8NamePtr = utf8Name)
        {
            return device != IntPtr.Zero
                ? VkGetDeviceProcAddr(new VkDevice(device), utf8NamePtr)
                : VkGetInstanceProcAddr(new VkInstance(instance), utf8NamePtr);
        }
    }

    private void OpenGlCreateTexture(ulong texHandle)
    {
        var context = GRContext.CreateGl();
        if (context == null)
        {
            throw new InvalidOperationException("OpenGL GRContext creation failed");
        }

        GRGlTextureInfo glTextureInfo = new GRGlTextureInfo
        {
            Id = (uint)texHandle,
            Target = 0x0DE1, // GL_TEXTURE_2D
            Format = 0x8058  // GL_RGBA8
        };

        _backendTexture = new GRBackendTexture(Width, Height, false, glTextureInfo);
        _skSurface = SKSurface.Create(context, _backendTexture, GRSurfaceOrigin.TopLeft, SKColorType.Rgba8888);
        if (_skSurface == null)
        {
            _backendTexture.Dispose();
            context.Dispose();
            throw new InvalidOperationException("OpenGL SKSurface creation failed");
        }
        _grContext = context;
        _skCanvas = _skSurface.Canvas;
        _isGpuMode = true;
    }

    private void UseImageCopy()
    {
        _skBitmap = new SKBitmap(Width, Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        _skCanvas = new SKCanvas(_skBitmap);
        _isGpuMode = false;
    }
}