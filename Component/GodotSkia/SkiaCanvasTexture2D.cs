using Godot;
using System;
using System.Buffers;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Unicode;
using Godot.Collections;
using SkiaSharp;
using Environment = System.Environment;
using static GodotGuiExtension.GodotSkia.VkInterop;
using Array = System.Array;

namespace GodotGuiExtension.GodotSkia;

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

    [Export]
    public int Width { get; set; }

    [Export]
    public int Height { get; set; }

    public SKCanvas? Canvas => _skCanvas;
    public bool Dirty => _dirty;
    public bool IsGpuMode => _isGpuMode;

    private Rid _rdTextureRid;
    private Rid _textureRid;
    private SKCanvas? _skCanvas;
    private SKBitmap? _skBitmap;
    private bool _dirty = true;
    private bool _isGpuMode = true;
    private GRContext? _grContext;

    public SkiaCanvasTexture2D()
    {
        Width = 512;
        Height = 512;
        Initialize();
    }

    public SkiaCanvasTexture2D(int width, int height)
    {
        Width = Math.Max(width, 1);
        Height = Math.Max(height, 1);
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
        var texHandle = RenderingServer.TextureGetNativeHandle(_textureRid);

        switch (RenderingDriverName)
        {
            case "vulkan":
                try
                {
                    VulkanCreateTexture(texHandle);
                }
                catch (Exception ex)
                {
                    GD.PrintErr("Error creating Skia canvas: ", ex.Message);
                    UseImageCopy();
                }
                break;
            default:
                UseImageCopy();
                break;
        }
    }

    public override int _GetWidth()
    {
        return Width;
    }

    public override int _GetHeight()
    {
        return Height;
    }

    public override Rid _GetRid()
    {
        return _textureRid;
    }

    /// <summary>
    /// Updates the texture with the current Skia bitmap data.
    /// </summary>
    public void UpdateTexture()
    {
        if (_isGpuMode)
        {
            _grContext?.Flush();
            return;
        }
        if (_skBitmap == null) return;
        IntPtr pixels = _skBitmap.GetPixels();
        int dataSize = _skBitmap.ByteCount;
        byte[] pixelBytes = new byte[dataSize];
        Marshal.Copy(pixels, pixelBytes, 0, dataSize);
        var image = Image.CreateFromData(_skBitmap.Width, _skBitmap.Height, false, Image.Format.Rgba8, pixelBytes);
        RenderingServer.Texture2DUpdate(_textureRid, image, 0);
    }
    
    /// <summary>
    ///  Marks the canvas as dirty, indicating that it needs to be redrawn.
    /// </summary>
    public void MarkDirty()
    {
        _dirty = true;
    }

    /// <summary>
    /// Gets the current image from the Skia canvas.
    /// </summary>
    /// <returns>An Image object representing the current state of the Skia canvas.</returns>
    public new Image GetImage()
    {
        if (_isGpuMode)
        {
            return _skCanvas?.Surface?.Snapshot().ToGodotImage() ?? Image.CreateEmpty(Width, Height, false, Image.Format.Rgba8);
        }
        return _skBitmap?.ToGodotImage() ?? Image.CreateEmpty(Width, Height, false, Image.Format.Rgba8);
    }

    public new void Free()
    {
        GD.Print("Free skTex");
        _skBitmap?.Dispose();
        _skCanvas?.Dispose();
        _grContext?.Dispose();
        RenderingDevice.FreeRid(_rdTextureRid);
        RenderingServer.FreeRid(_textureRid);
    }

    public new void Dispose()
    {
        Free();
        base.Dispose();
    }
    private void VulkanCreateTexture(ulong textureHandle)
    {
        var vkFormat =
            RenderingDevice.GetDriverResource(RenderingDevice.DriverResource.TextureDataFormat, _rdTextureRid, 0UL);
        GRVkImageInfo vkImageInfo = new GRVkImageInfo
        {
            CurrentQueueFamily = (uint)VkQueueFamilyIndex,
            Format = (uint)vkFormat,
            Image = textureHandle,
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

        GRVkBackendContext vkBackendContext = new GRVkBackendContext
        {
            VkDevice = VkDevice.Handle,
            VkPhysicalDevice = VkPhysicalDevice.Handle,
            VkInstance = VkInstance.Handle,
            VkQueue = VkQueue.Handle,
            GraphicsQueueIndex = (uint)VkQueueFamilyIndex,
            GetProcedureAddress = GetProcedureAddress
        };

        _grContext = GRContext.CreateVulkan(vkBackendContext);

        if (_grContext == null)
        {
            throw new InvalidOperationException("Vulkan GRContext creation failed");
        }

        SKSurface surface = SKSurface.Create(_grContext,
            new GRBackendRenderTarget(Width, Height, vkImageInfo),
            GRSurfaceOrigin.TopLeft,
            SKColorType.Rgba8888,
            new SKSurfaceProperties(SKPixelGeometry.RgbHorizontal));

        if (surface == null)
        {
            _grContext.Dispose();
            throw new InvalidOperationException("Vulkan SKSurface creation failed");
        }

        _skCanvas = surface.Canvas;
        _isGpuMode = true;
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

        GRBackendTexture backendTexture = new GRBackendTexture(Width, Height, false, glTextureInfo);
        SKSurface surface = SKSurface.Create(context, backendTexture, GRSurfaceOrigin.TopLeft, SKColorType.Rgba8888);
        if (surface == null)
        {
            context.Dispose();
            throw new InvalidOperationException("OpenGL SKSurface creation failed");
        }
        _skCanvas = surface.Canvas;
        _isGpuMode = true;
    }

    private void UseImageCopy()
    {
        _skBitmap = new SKBitmap(Width, Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        _skCanvas = new SKCanvas(_skBitmap);
        _isGpuMode = false;
    }
}