**English** | [中文](README.cn.md)

# GodotSkia

A GPU-accelerated 2D rendering bridge between SkiaSharp and Godot. Shares the Vulkan backend between Godot's RenderingDevice and Skia's GRContext, enabling high-performance, zero-copy GPU texture interop. Also supports CPU fallback mode for non-Vulkan renderers.

## Features

- **GPU Texture Sharing**: Direct Vulkan VkImage sharing between Godot and Skia — no pixel readback required
- **Automatic Layout Transitions**: Vulkan pipeline barriers with Skia-accurate access masks and stage flags
- **CPU Fallback**: Transparent fallback to CPU-based rendering when Vulkan is unavailable
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
3. **UpdateTexture()** — Flushes Skia GPU commands, waits for completion, transitions to `SHADER_READ_ONLY_OPTIMAL`
4. **Godot Sampling** — Godot samples the texture as a standard `Texture2D`

## Components

### SkiaCanvasTexture2D

The core component. A Godot `Texture2D` backed by a SkiaSharp `SKSurface` with shared Vulkan memory.

#### Properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Width` | int | 512 | Texture width in pixels. Changes trigger rebuild on next Canvas access |
| `Height` | int | 512 | Texture height in pixels. Changes trigger rebuild on next Canvas access |
| `Canvas` | SKCanvas? | — | The Skia canvas for drawing (read-only) |
| `Dirty` | bool | true | Whether the texture needs redrawing |
| `IsGpuMode` | bool | true | Whether GPU rendering is active |

#### Methods

```csharp
// Update the texture after drawing. Call once per frame.
void UpdateTexture()

// Resize the texture immediately. Previous content is discarded.
void Resize(int width, int height)

// Mark the canvas as needing a redraw.
void MarkDirty()

// Get the current image as a Godot Image.
Image GetImage()
```

#### Usage Example

```csharp
public partial class MyNode : Control
{
    private SkiaCanvasTexture2D _skiaTex;

    public override void _Ready()
    {
        _skiaTex = new SkiaCanvasTexture2D(800, 600);
    }

    public override void _Process(double delta)
    {
        if (!_skiaTex.Dirty) return;

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
}
```

### SkiaGodotConverter

Static utility class for bidirectional type conversions.

#### Color

```csharp
SKColor skColor = godotColor.ToSKColor();
Color godotColor = skColor.ToGodotColor();
```

#### Geometry

```csharp
SKPoint point = vector2.ToSKPoint();
Vector2 vec = point.ToVector2();

SKSize size = vector2.ToSKSize();
Vector2 vec = size.ToVector2();

SKRect rect = rect2.ToSKRect();
Rect2 rect2 = rect.ToRect2();

SKMatrix matrix = transform2D.ToSKMatrix();
Transform2D transform = matrix.ToTransform2D();
```

#### Font

```csharp
// Convert Godot Font to SKFont (supports FontFile and SystemFont)
SKFont skFont = godotFont.ToSKFont(size: 24f);

// Create an SKPaint + SKFont pair for text rendering
var (textPaint, textFont) = SkiaGodotConverter.CreateTextPaintAndFont(font, 16f, Colors.White);
```

#### Image

```csharp
SKBitmap bitmap = godotImage.ToSKBitmap();
Image image = skBitmap.ToGodotImage();

SKImage skImage = texture2D.ToSKImage();
Image image = skImage.ToGodotImage();
```

#### Paint

```csharp
SKPaint fill = SkiaGodotConverter.CreatePaint(Colors.Blue);
SKPaint stroke = SkiaGodotConverter.CreateStrokePaint(Colors.Red, strokeWidth: 2f);
```

### VkBarrierHelper

Manages Vulkan pipeline barrier submissions with reusable command buffers and fences. Used internally by `SkiaCanvasTexture2D` for image layout transitions. Also provides Skia-accurate barrier parameter computation (access masks and stage flags) based on GrVkImage.cpp.

### VkDeviceApi

Loads Vulkan device-level function pointers at runtime. Used internally by `VkBarrierHelper`.

### VkInterop

Vulkan interop type definitions: structs, enums, constants, and native function pointer signatures used throughout the component.

## Requirements

- **Godot** 4.0+ with .NET support
- **.NET** 9.0+
- **SkiaSharp** 3.116.0+
- **Rendering**: Vulkan Forward+ (recommended) or any renderer (CPU fallback)

## License

MIT
