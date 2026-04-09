[English](README.md) | **中文**

# GodotSkia

基于 GPU 加速的 SkiaSharp 与 Godot 之间的 2D 渲染桥接组件。在 Godot 的 RenderingDevice 和 Skia 的 GRContext 之间共享 Vulkan 后端，实现高性能、零拷贝的 GPU 纹理互操作。同时支持 CPU 回退模式，兼容非 Vulkan 渲染器。

## 功能特性

- **GPU 纹理共享**：Godot 与 Skia 之间直接共享 Vulkan VkImage，无需像素回读
- **自动布局转换**：使用 Skia 精确的访问掩码和管线阶段标志进行 Vulkan 管线屏障操作
- **CPU 回退**：Vulkan 不可用时自动切换至 CPU 渲染模式
- **类型转换器**：Godot 与 Skia 类型的双向转换（Color、Vector2、Rect2、Font、Image 等）

## 架构

```
┌──────────────────────────────────┐
│          你的绘图代码              │
│       (SKCanvas API 调用)         │
└──────────┬───────────────────────┘
           │
┌──────────▼───────────────────────┐
│     SkiaCanvasTexture2D          │
│  ┌─────────────┐ ┌────────────┐ │
│  │  SKSurface   │ │ GRContext  │ │
│  │  (GPU 模式)  │ │ (Vulkan)   │ │
│  └──────┬──────┘ └─────┬──────┘ │
│         │  共享 VkImage  │       │
│  ┌──────▼──────────────▼──────┐ │
│  │     VkBarrierHelper        │ │
│  │     (布局转换)              │ │
│  └────────────────────────────┘ │
└──────────┬───────────────────────┘
           │
┌──────────▼───────────────────────┐
│   Godot RenderingDevice (Vulkan) │
│        Texture2D 采样             │
└──────────────────────────────────┘
```

### 渲染管线工作流程

1. **获取画布** — 将 VkImage 转换为 `COLOR_ATTACHMENT_OPTIMAL` 布局供 Skia 渲染
2. **绘制** — 应用程序通过 `SKCanvas` API 绘制内容
3. **UpdateTexture()** — 刷新 Skia GPU 命令，等待完成，转换为 `SHADER_READ_ONLY_OPTIMAL`
4. **Godot 采样** — Godot 将纹理作为标准 `Texture2D` 进行采样

## 组件说明

### SkiaCanvasTexture2D

核心组件。一个由 SkiaSharp `SKSurface` 支持的 Godot `Texture2D`，通过共享 Vulkan 内存实现。

#### 属性

| 属性 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `Width` | int | 512 | 纹理宽度（像素），修改后在下次访问 Canvas 时重建 |
| `Height` | int | 512 | 纹理高度（像素），修改后在下次访问 Canvas 时重建 |
| `Canvas` | SKCanvas? | — | 用于绘图的 Skia 画布（只读） |
| `Dirty` | bool | true | 纹理是否需要重绘 |
| `IsGpuMode` | bool | true | 是否启用 GPU 渲染模式 |

#### 方法

```csharp
// 绘制完成后更新纹理，每帧调用一次
void UpdateTexture()

// 立即调整纹理尺寸，旧内容将被丢弃
void Resize(int width, int height)

// 标记画布需要重绘
void MarkDirty()

// 获取当前图像为 Godot Image
Image GetImage()
```

#### 使用示例

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

静态工具类，用于 Godot 与 Skia 类型的双向转换。

#### 颜色

```csharp
SKColor skColor = godotColor.ToSKColor();
Color godotColor = skColor.ToGodotColor();
```

#### 几何

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

#### 字体

```csharp
// 将 Godot Font 转换为 SKFont（支持 FontFile 和 SystemFont）
SKFont skFont = godotFont.ToSKFont(size: 24f);

// 创建用于文本渲染的 SKPaint + SKFont 组合
var (textPaint, textFont) = SkiaGodotConverter.CreateTextPaintAndFont(font, 16f, Colors.White);
```

#### 图像

```csharp
SKBitmap bitmap = godotImage.ToSKBitmap();
Image image = skBitmap.ToGodotImage();

SKImage skImage = texture2D.ToSKImage();
Image image = skImage.ToGodotImage();
```

#### 画笔

```csharp
SKPaint fill = SkiaGodotConverter.CreatePaint(Colors.Blue);
SKPaint stroke = SkiaGodotConverter.CreateStrokePaint(Colors.Red, strokeWidth: 2f);
```

### VkBarrierHelper

管理 Vulkan 管线屏障提交，使用可复用的命令缓冲区和栅栏。由 `SkiaCanvasTexture2D` 内部用于图像布局转换。同时提供基于 GrVkImage.cpp 的 Skia 精确屏障参数计算（访问掩码和管线阶段标志）。

### VkDeviceApi

运行时加载 Vulkan 设备级函数指针。由 `VkBarrierHelper` 内部使用。

### VkInterop

Vulkan 互操作类型定义：组件中使用的结构体、枚举、常量和原生函数指针签名。

## 环境要求

- **Godot** 4.0+ (.NET 版本)
- **.NET** 9.0+
- **SkiaSharp** 3.116.0+
- **渲染器**：Vulkan Forward+（推荐）或任意渲染器（CPU 回退模式）

## 许可证

MIT
