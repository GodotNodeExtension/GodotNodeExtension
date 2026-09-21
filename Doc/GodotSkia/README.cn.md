[English](README.md) | **中文**

# GodotSkia

基于 GPU 加速的 SkiaSharp 与 Godot 之间的 2D 渲染桥接组件。Vulkan 渲染器与 Skia 的 GRContext 共享
GPU 表面，实现高性能、零拷贝的 GPU 纹理互操作；其它渲染驱动（含 OpenGL 兼容渲染器）则回退到 CPU 表面。

## 功能特性

- **GPU 纹理共享**：Godot 与 Skia 之间直接共享 Vulkan VkImage——绘制无需逐帧像素回读（只有 GPU 路径的首帧会整幅回读并上传一次，以对齐 Godot 的布局跟踪）
- **自动布局转换**：使用 Skia 精确的访问掩码和管线阶段标志进行 Vulkan 管线屏障操作
- **按渲染器选择后端**：Vulkan 走 GPU，其它渲染驱动走 CPU 表面——完全没有渲染设备时则根本建不出表面
- **无渲染设备**：两个构造函数都会抛 `InvalidOperationException`；调用方若能继续工作，先查 `SkiaCanvasTexture2D.HasRenderingDevice`
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
3. **UpdateTexture()** — GPU 路径：刷新 Skia GPU 命令，等待完成，再转换为 `SHADER_READ_ONLY_OPTIMAL`。CPU 路径（`UpdateTextureCpu()`，非 Vulkan 的驱动一律使用）：拷贝 Skia 位图并通过 `RenderingServer.Texture2DUpdate` 上传
4. **Godot 采样** — Godot 将纹理作为标准 `Texture2D` 进行采样

## 组件说明

### SkiaCanvasTexture2D

核心组件。一个由 SkiaSharp `SKSurface` 支持的 Godot `Texture2D`，通过共享 Vulkan 内存实现。

它是 `Width` / `Height` 为 `[Export]` 的 `[Tool]` `[GlobalClass]` 资源，因此可以在检查器里创建与调整尺寸
（也可以存成 `.tres`）：场景可以直接持有这张纹理并交给负责呈现它的节点，不必写代码来构造。

#### 属性

| 属性 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `Width` | int | 512 | 纹理宽度（像素），修改后在下次访问 Canvas 时重建 |
| `Height` | int | 512 | 纹理高度（像素），修改后在下次访问 Canvas 时重建 |
| `Canvas` | SKCanvas? | — | 用于绘图的 Skia 画布（只读） |
| `IsGpuMode` | bool | true | 是否启用 GPU 渲染模式 |

#### 方法

```csharp
// 绘制完成后更新纹理，每帧调用一次
void UpdateTexture()

// 立即调整纹理尺寸，旧内容将被丢弃
void Resize(int width, int height)

// 当前图像。覆写的是 Godot 的 Texture2D.GetImage() 虚方法：把纹理当作 Texture2D 用即可，
// 不需要转型成 SkiaCanvasTexture2D。已释放或尚未创建表面的情况下返回空图，绝不返回 null。
Image _GetImage()
```

本组件不跟踪重绘状态：纹理没有用于查询「表面是否需要某一帧」的标志，也没有对应的设置入口。每次
`UpdateTexture()` 都会提交一帧，宿主只需在自己想绘制的帧里绘制即可。

#### 读取像素

`GetImage()` 实现的是 Godot 的 `Texture2D.GetImage()` 虚方法，因此任何把纹理当作 `Texture2D` 的代码——
消费纹理的组件、转换器、编辑器缩略图——都能直接读到画好的像素，无需认识本类型。（此前该类另声明了一个
`GetImage()`，只是隐藏了基类方法，这类调用方会拿到 `null`。）回读纹理会触发一次 GPU 回读或位图拷贝，
请按需调用，不要每帧都读。

#### 使用示例

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
        // 释放表面、两个 RID，以及本纹理持有的共享 GRContext 引用。
        _skiaTex.ReleaseResources();
    }
}
```

### SkiaGodotConverter

静态工具类，用于 Godot 与 Skia 类型的双向转换。

#### 颜色

```csharp compile
Color godotColor = Colors.Red;
SKColor skColor = godotColor.ToSkColor();

skColor = new SKColor(30, 60, 90, 255);
godotColor = skColor.ToGodotColor();
```

Godot 的调色板在 Skia 自己的类型里同样可用：`SkiaGodotConverter.Colors` 把七个调色板颜色以 `SKColor` 常量提供
（只有 `Transparent` 不是不透明的），在 `SKColor` 里作画的代码不必手工转换。

```csharp compile
SKColor white = SkiaGodotConverter.Colors.White;
SKColor transparent = SkiaGodotConverter.Colors.Transparent;
// 其它颜色仍然走转换器：
SKColor custom = new Color(0.2f, 0.4f, 0.6f, 0.5f).ToSkColor();
```

#### 几何

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

#### 字体

```csharp compile
// 将 Godot Font 转换为 SKFont（支持 FontFile 和 SystemFont），size 默认为 16f
Font font = ThemeDB.FallbackFont;
SKFont skFont = font.ToSkFont(size: 24f);

// 创建用于文本渲染的 SKPaint + SKFont 组合，antiAlias 默认为 true
var (textPaint, textFont) = SkiaGodotConverter.CreateTextPaintAndFont(font, 16f, Colors.White);
```

#### 图像

```csharp compile
SKBitmap bitmap = godotImage.ToSkBitmap();
Image image = bitmap.ToGodotImage();

SKImage skImage = texture2D.ToSkImage();
image = skImage.ToGodotImage();
```

#### 画笔

```csharp compile
using var fill = SkiaGodotConverter.CreatePaint(Colors.Blue);
using var stroke = SkiaGodotConverter.CreateStrokePaint(Colors.Red, strokeWidth: 2f);
```

### VkBarrierHelper

内部实现，不属于公开 API。管理 Vulkan 管线屏障提交，使用可复用的命令缓冲区和栅栏。由 `SkiaCanvasTexture2D` 内部用于图像布局转换。同时提供基于 GrVkImage.cpp 的 Skia 精确屏障参数计算（访问掩码和管线阶段标志）。

### VkDeviceApi

内部实现，不属于公开 API。本组件的 Vulkan 函数指针签名（原生 `delegate*` 声明）以及运行时解析它们的加载器。由 `VkBarrierHelper` 内部使用。

### VkInterop

内部实现，不属于公开 API。Vulkan 互操作类型定义：组件中使用的结构体、枚举和常量。

## 编辑器热重载

`SkiaCanvasTexture2D` 缓存了进程级状态：Godot 的 `RenderingDevice`、创建 surface 用的 Vulkan
句柄、共享的 Skia `GRContext`，以及按 Godot 字体索引的 typeface 缓存。C# 重建会换掉这些缓存所描述的
设备，旧的那一份只要还在，编辑器就无法完成这次重载——它会报出「无法卸载程序集、放弃程序集重载」。

这一步已自动完成：类型第一次被使用时，释放这份状态就被登记为"程序集重载时执行"，因此缓存是被丢弃而不是
被带走。宿主若要自己替换程序集、或在退出时清理，可以手动调用 `SkiaCanvasTexture2D.ResetStaticState()`；
重复调用是安全的，下一次使用时一切都会重新创建。

`ResetStaticState()` **释放什么**：缓存的 Godot `RenderingDevice`、Vulkan 的
device / physical device / instance / queue 句柄、共享的 Skia `GRContext`，以及按 Godot 字体 ID
索引的 typeface 缓存。**刻意保留什么**：按字体**家族名**索引的 typeface 缓存，以及缓存下来的 Vulkan
库句柄与从它解析出的过程指针（它们只对那个库有效，既无需清理、也不必重新解析）。

它与**存活的纹理互斥**：那些实例正持有它要清掉的状态。在还有纹理存活时调用会以警告报出，而那些纹理之后
无法再渲染——编辑器重载正是这种情况，此时先释放纹理是宿主的责任。

## 使用约束

### 无渲染设备

`SkiaCanvasTexture2D` 需要真正的渲染设备：没有设备就完全建不出 Skia 表面。
`SkiaCanvasTexture2D.HasRenderingDevice` 报告当前进程是否有设备，而两个构造函数在无设备时都会抛
`InvalidOperationException`（`--headless` 运行、或 `--rendering-driver dummy`）。无法继续的调用方
应让异常直接抛出；能够继续工作的调用方请先查该属性。替你创建并呈现表面的画布宿主（canvas host）会把这种
失败降级为一条 warning 并保持自身节点可用，而直接创建表面的代码必须自己处理 `InvalidOperationException`。
所以构造前先探测：

```csharp compile
if (!SkiaCanvasTexture2D.HasRenderingDevice)
{
    // 这里建不出 Skia 表面——直接跳过纹理，而不是去 catch 构造函数。
    return;
}

var texture = new SkiaCanvasTexture2D(800, 600);
```

### 生命周期

纹理持有原生资源（Skia 表面、Godot RID，以及一份共享 `GRContext` 引用）。请确定性地释放它们：调用
`ReleaseResources()`（或 `Dispose()`，它内部会调用前者），例如放在 `_ExitTree` 里；若交给
垃圾回收器，表面及其背后的原生内存会比节点存活更久。

`ResetStaticState()` 是进程级的对应物，不是逐实例调用：它会释放缓存的设备句柄与共享 `GRContext`，
因此与存活实例互斥——活着的纹理正持有它要清掉的状态。只在退出、或程序集重载前调用它。

### 线程

`SkiaCanvasTexture2D` 只能在主线程使用：共享的 `GRContext`、Vulkan 队列与屏障辅助器都是进程级、非线程
安全的，而且 Godot 自身的 API 也必须由主线程调用。不要从 `Task`、工作线程或 `System.Threading` 作业里
创建纹理、绘制、调整尺寸或更新。替你往该纹理里绘制的上层（画布宿主、包装层）都受同一条规则约束。

### Skia 对象归属

`Canvas` 由纹理自己拥有：不要 Dispose 它，也不要在 `Resize`、修改宽高或调用 `ReleaseResources()` 之后继续
持有它——每次重建都会释放它所属的 surface，下一次访问 `Canvas` 会新建一个。你自己创建（或传入）的 SkiaSharp
对象——`SKPaint`、`SKPath`、`SKImage`——仍归你所有：本组件既不池化也不释放它们，用完请自行 Dispose。

## 环境要求

- **Godot** 4.7+（.NET 版本）
- **.NET** SDK 10.0+（源码目标 `net10.0`）
- **SkiaSharp** 3.119.0+
- **渲染器**：Vulkan Forward+ 以共享 GPU 表面；其它渲染驱动走 CPU 表面——包括本组件并不封装的 OpenGL 兼容渲染器——且必须有真正的渲染设备

## 许可证

MIT
