**English** | [中文](README.cn.md)

# GodotSkia

A GPU-accelerated 2D rendering bridge between SkiaSharp and Godot. A Vulkan renderer shares a GPU surface
with Skia's GRContext, enabling high-performance, zero-copy GPU texture interop; every other rendering
driver - the OpenGL compatibility renderer included - falls back to a CPU surface.

## Features

- **GPU Texture Sharing**: Direct Vulkan VkImage sharing between Godot and Skia — drawing needs no per-frame pixel readback (the first GPU frame performs one full-surface readback and upload so Godot's layout tracking matches)
- **Automatic Layout Transitions**: Vulkan pipeline barriers with Skia-accurate access masks and stage flags
- **Renderer-aware backends**: Vulkan renders through the GPU, every other rendering driver uses a CPU surface — and a missing rendering device means no surface at all
- **No rendering device**: both constructors throw `InvalidOperationException`; check `SkiaCanvasTexture2D.HasRenderingDevice` first when the caller can continue without a surface
- **Type Converters**: Bidirectional conversions between Godot and Skia types (Color, Vector2, Rect2, Font, Image, etc.)

## Architecture

```
┌──────────────────────────────────┐
│         Your Drawing Code        │
│       (SKCanvas API calls)       │
└──────────┬───────────────────────┘
           │
┌──────────▼───────────────────────┐
│     SkiaCanvasTexture2D          │
│  ┌─────────────┐ ┌────────────┐ │
│  │  SKSurface   │ │ GRContext  │ │
│  │  (GPU mode)  │ │ (Vulkan)   │ │
│  └──────┬──────┘ └─────┬──────┘ │
│         │  Shared VkImage│       │
│  ┌──────▼──────────────▼──────┐ │
│  │     VkBarrierHelper        │ │
│  │  (layout transitions)      │ │
│  └────────────────────────────┘ │
└──────────┬───────────────────────┘
           │
┌──────────▼───────────────────────┐
│   Godot RenderingDevice (Vulkan) │
│        Texture2D sampling        │
└──────────────────────────────────┘
```

### Rendering Pipeline

1. **Canvas Access** — Transitions VkImage to `COLOR_ATTACHMENT_OPTIMAL` for Skia rendering
2. **Drawing** — Application draws via `SKCanvas` API
3. **UpdateTexture()** — GPU path: flushes Skia GPU commands, waits for completion, then transitions to `SHADER_READ_ONLY_OPTIMAL`. CPU path (`UpdateTextureCpu()`, chosen for every driver that is not Vulkan): copies the Skia bitmap and uploads it through `RenderingServer.Texture2DUpdate`
4. **Godot Sampling** — Godot samples the texture as a standard `Texture2D`

## Components

### SkiaCanvasTexture2D

The core component. A Godot `Texture2D` backed by a SkiaSharp `SKSurface` with shared Vulkan memory.

It is a `[Tool]` `[GlobalClass]` resource whose `Width` / `Height` are `[Export]`, so one can be created and
resized in the inspector (or kept in a `.tres`): a scene may own the texture and hand it to whatever presents
it, instead of building it from code.

#### Properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Width` | int | 512 | Texture width in pixels. Changes trigger rebuild on next Canvas access |
| `Height` | int | 512 | Texture height in pixels. Changes trigger rebuild on next Canvas access |
| `Canvas` | SKCanvas? | — | The Skia canvas for drawing (read-only) |
| `IsGpuMode` | bool | true | Whether GPU rendering is active |

#### Methods

```csharp
// Update the texture after drawing. Call once per frame.
void UpdateTexture()

// Resize the texture immediately. Previous content is discarded.
void Resize(int width, int height)

// Current image. Overrides Godot's Texture2D.GetImage() virtual: holding the texture as a
// Texture2D is enough, no cast to SkiaCanvasTexture2D is needed. A released or not-yet-created
// surface answers with an empty image, never null.
Image _GetImage()
```

There is no redraw state to track: the texture has no flag that reports whether the surface needs a
frame, and no method that sets one. Every `UpdateTexture()` commits a frame, so a host simply draws on
the frames it wants to draw.

#### Reading pixels

`GetImage()` is Godot's own `Texture2D.GetImage()` virtual, so any code that holds the texture as a
`Texture2D` - a component that consumes the surface, a converter, the editor's thumbnailer - reads the
drawn pixels without knowing this type. (Before, the class declared a separate `GetImage()` that only
hid the base method, and such callers received `null`.) Reading a texture back costs a GPU readback or a
bitmap copy, so do it on demand rather than every frame.

#### Usage Example

```csharp compile-class
public partial class MyNode : Control
{
    private SkiaCanvasTexture2D _skiaTex;

    public override void _Ready()
    {
        _skiaTex = new SkiaCanvasTexture2D(800, 600);
    }

    public override void _Process(double delta)
    {
        var canvas = _skiaTex.Canvas;
        if (canvas == null) return;

        canvas.Clear(SKColors.White);
        using var paint = new SKPaint { Color = SKColors.Red, IsAntialias = true };
        canvas.DrawCircle(400, 300, 100, paint);

        _skiaTex.UpdateTexture();
    }

    public override void _Draw()
    {
        DrawTexture(_skiaTex, Vector2.Zero);
    }

    public override void _ExitTree()
    {
        // Release the surface, the RIDs and this texture's shared-GRContext reference.
        _skiaTex.ReleaseResources();
    }
}
```

### SkiaGodotConverter

Static utility class for bidirectional type conversions.

#### Color

```csharp compile
Color godotColor = Colors.Red;
SKColor skColor = godotColor.ToSkColor();

skColor = new SKColor(30, 60, 90, 255);
godotColor = skColor.ToGodotColor();
```

The Godot palette is available in Skia's own type as well: `SkiaGodotConverter.Colors` holds the seven palette
entries as `SKColor` constants (`Transparent` is the only one that is not opaque), so a painter that works in
`SKColor` does not have to convert them by hand.

```csharp compile
SKColor white = SkiaGodotConverter.Colors.White;
SKColor transparent = SkiaGodotConverter.Colors.Transparent;
// Any other colour still goes through the converter:
SKColor custom = new Color(0.2f, 0.4f, 0.6f, 0.5f).ToSkColor();
```

#### Geometry

```csharp compile
Vector2 vec = new(3f, 4f);
SKPoint point = vec.ToSkPoint();
vec = point.ToVector2();

SKSize size = vec.ToSkSize();
vec = size.ToVector2();

Rect2 rect2 = new(1f, 2f, 3f, 4f);
SKRect rect = rect2.ToSkRect();
rect2 = rect.ToRect2();

Transform2D transform2D = Transform2D.Identity;
SKMatrix matrix = transform2D.ToSkMatrix();
transform2D = matrix.ToTransform2D();
```

#### Font

```csharp compile
// Convert Godot Font to SKFont (supports FontFile and SystemFont). The size defaults to 16f.
Font font = ThemeDB.FallbackFont;
SKFont skFont = font.ToSkFont(size: 24f);

// Create an SKPaint + SKFont pair for text rendering. Anti-aliasing defaults to true.
var (textPaint, textFont) = SkiaGodotConverter.CreateTextPaintAndFont(font, 16f, Colors.White);
```

#### Image

```csharp compile
SKBitmap bitmap = godotImage.ToSkBitmap();
Image image = bitmap.ToGodotImage();

SKImage skImage = texture2D.ToSkImage();
image = skImage.ToGodotImage();
```

#### Paint

```csharp compile
using var fill = SkiaGodotConverter.CreatePaint(Colors.Blue);
using var stroke = SkiaGodotConverter.CreateStrokePaint(Colors.Red, strokeWidth: 2f);
```

### VkBarrierHelper

Internal implementation, not part of the public API. Manages Vulkan pipeline barrier submissions with reusable command buffers and fences. Used internally by `SkiaCanvasTexture2D` for image layout transitions. Also provides Skia-accurate barrier parameter computation (access masks and stage flags) based on GrVkImage.cpp.

### VkDeviceApi

Internal implementation, not part of the public API. The component's Vulkan function-pointer signatures (the native `delegate*` declarations) and the loader that resolves them at runtime. Used internally by `VkBarrierHelper`.

### VkInterop

Internal implementation, not part of the public API. Vulkan interop type definitions: structs, enums and constants used throughout the component.

## Editor Hot Reload

`SkiaCanvasTexture2D` caches process-wide state: the Godot `RenderingDevice`, the Vulkan handles the
surfaces are created from, the shared Skia `GRContext` and the Godot-font typeface cache. A C# rebuild
replaces the device those caches describe, and an old one that is still around keeps the editor from
finishing the reload - it then reports that it could not unload the assemblies and gives up on assembly
reloading.

That is handled automatically: the first time the type is used, releasing this state is registered to run
when the assembly is reloaded, so the caches are dropped instead of being carried over. A host that swaps
assemblies itself, or cleans up on shutdown, may call `SkiaCanvasTexture2D.ResetStaticState()` directly;
calling it twice is safe, and everything is recreated on next use.

What `ResetStaticState()` releases: the cached Godot `RenderingDevice`, the Vulkan
device / physical-device / instance / queue handles, the shared Skia `GRContext`, and the
Godot-font-ID typeface cache. What it deliberately keeps: the typeface cache keyed by font family name,
and the cached Vulkan library handle together with the procedure pointers resolved from it (they are
only valid for that library, so there is nothing to clean up and re-resolving is unnecessary).

It is **mutually exclusive with live textures**: those instances hold the very state it clears. Called
while any texture is alive it is reported with a warning, and those textures cannot render afterwards -
which is what happens on an editor reload, where releasing them first is the host's job.

## Usage Constraints

### No rendering device

`SkiaCanvasTexture2D` needs a real rendering device: without one no Skia surface can be created at all.
`SkiaCanvasTexture2D.HasRenderingDevice` reports whether this process has one, and both constructors
throw `InvalidOperationException` when it is false (a `--headless` run, or `--rendering-driver dummy`).
A caller that cannot continue should let the exception propagate; a caller that can keep working checks
the property first. A canvas host that creates and presents the surface for you degrades the same failure
to a single warning and keeps its node usable, while code that creates the surface directly has to handle
the `InvalidOperationException` itself. So probe the property before constructing:

```csharp compile
if (!SkiaCanvasTexture2D.HasRenderingDevice)
{
    // No Skia surface is possible here - skip the texture instead of catching the constructor.
    return;
}

var texture = new SkiaCanvasTexture2D(800, 600);
```

### Lifecycle

A texture owns native resources (the Skia surface, Godot RIDs and one shared-`GRContext` reference).
Release them deterministically with `ReleaseResources()` - or `Dispose()`, which calls it - for example
in `_ExitTree`; relying on the garbage collector keeps the surface, and the native memory behind it,
alive longer than the node.

`ResetStaticState()` is the process-wide counterpart, not a per-instance call: it releases the cached
device handles and the shared `GRContext`, so it is mutually exclusive with live instances - they hold
the very state it clears. Call it only on shutdown or before the assembly is reloaded.

### Threading

`SkiaCanvasTexture2D` is main-thread only: the shared `GRContext`, the Vulkan queue and the barrier
helper are process-wide and not thread-safe, and Godot's own API must be called from the main thread. Do
not create, draw into, resize or update a texture from a `Task`, a worker thread or a `System.Threading`
job. Anything that draws into this texture on your behalf - a canvas host, a wrapper - is bound by the
same rule.

### Skia object ownership

`Canvas` is owned by the texture: do not dispose it, and do not keep it past a `Resize`, a width/height
change or a `ReleaseResources()` - each rebuild disposes the surface the canvas came from, and the next
`Canvas` access builds a new one. The SkiaSharp objects you create yourself (or pass in) - `SKPaint`,
`SKPath`, `SKImage` - stay yours: this component neither pools nor disposes them, so dispose them when
you are done with them.

## Requirements

- **Godot** 4.7+ with .NET support
- **.NET** SDK 10.0+ (the sources target `net10.0`)
- **SkiaSharp** 3.119.0+
- **Rendering**: Vulkan Forward+ for a shared GPU surface; every other rendering driver uses a CPU surface - including the OpenGL compatibility renderer, which this component does not wrap - and a real rendering device is required

## License

MIT
