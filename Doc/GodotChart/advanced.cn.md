# 高级功能

本文档涵盖 GodotChart 的动画系统、用户交互、工具提示、实时数据流和数据变换等高级特性。

---

## 动画系统

GodotChart 提供完整的动画管线：入场动画、数据过渡、悬停动画和退场动画。

### AnimationController — 动画控制器

`AnimationController` 主管所有动画状态：

```csharp
private AnimationController _anim = new();
```

**核心属性：**

| 属性 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `EntryProgress` | float | 1 | 入场进度 [0,1] |
| `SeriesProgress` | float[] | — | 每系列独立进度 |
| `GlobalOpacity` | float | 1 | 整体透明度 [0,1] |
| `EntryDuration` | float | 0.6 | 入场动画时长（秒） |
| `SeriesStagger` | float | 0.1 | 系列间错峰延迟 |
| `AnimationThreshold` | int | 2000 | 超过此数据量自动禁用动画 |
| `HoverScale` | float | 1 | 悬停缩放因子 |
| `ExitDuration` | float | 0.3 | 退场动画时长 |
| `IsAnimating` | bool | — | 是否正在动画中 |

### 入场动画

```csharp
// Start entry animation (call once)
_anim.StartEntry(this, seriesCount: 3, totalElementCount: 100);

// In _Process(double delta) — build AnimationContext
var ctx = new AnimationContext
{
    EntryProgress = _anim.EntryProgress,
    GlobalOpacity = _anim.GlobalOpacity,
};

chart.Animate(ctx).Render();
```

入场动画效果因 Mark 类型不同：
- **IntervalMark** — 柱体从基线向上生长
- **LineMark** — 从左到右裁剪揭示
- **PieMark** — 扇形从起始角度扫过
- **PointMark** — 淡入并缩放

### 系列错峰

通过 `SeriesStagger` 让多系列依次出现：

```csharp
_anim.SeriesStagger = 0.15f;  // 150ms per series delay
_anim.StartEntry(this, seriesCount: 3);

// Each series gets independent progress:
// _anim.SeriesProgress[0], [1], [2] stagger over time
```

### 数据过渡

当数据更新时，可触发平滑过渡：

```csharp
_anim.StartDataTransition(this, duration: 0.4f);

// In Render loop:
var ctx = new AnimationContext
{
    DataTransitionProgress = _anim.DataTransitionProgress,
};
chart.Animate(ctx).Render();
```

### 悬停动画

响应鼠标悬停的缩放效果：

```csharp
// On hover event:
_anim.AnimateHover(this, targetScale: 1.05f);

var ctx = new AnimationContext
{
    HoverScale = _anim.HoverScale,
};
```

### 退场动画

```csharp
_anim.StartExit(this, onComplete: () =>
{
    GD.Print("Exit animation complete!");
    // Clean up or switch chart
});
```

### AnimationContext — 简化 API

也可以直接使用 `float` 进度传入（仅入场动画）：

```csharp
chart.Animate(0.5f).Render();  // 50% entry progress
```

或使用 `AnimationContext.Default` 跳过所有动画。

### 缓动函数

```csharp
public enum EaseType
{
    Linear,
    EaseInQuad,
    EaseOutQuad,
    EaseOutCubic,
    EaseInOutCubic,
    EaseOutBack,
    EaseOutElastic,
}

float eased = Ease.Apply(t, EaseType.EaseOutCubic);
```

### 性能保护

当数据量超过 `AnimationThreshold`（默认 2000）时，动画自动禁用：

```csharp
if (_anim.ShouldAnimate(elementCount))
{
    chart.Animate(ctx);
}
chart.Render();
```

---

## 用户交互

### HitTest — 命中测试

在鼠标位置检测用户点击/悬停到哪个数据元素：

```csharp
var hit = chart.HitTest(mousePosition);
if (hit?.Hit == true)
{
    DataRow row = hit.Row;
    int rowIndex = hit.RowIndex;
    string series = hit.SeriesKey;
    string markType = hit.MarkType;
    Color color = hit.ElementColor;
}
```

**HitResult 属性：**

| 属性 | 类型 | 说明 |
|------|------|------|
| `Hit` | bool | 是否命中 |
| `Row` | DataRow? | 命中的数据行 |
| `RowIndex` | int | 数据行索引 |
| `ScreenX` / `ScreenY` | float | 屏幕坐标 |
| `Label` | string? | 标签文本 |
| `SeriesKey` | string? | 系列名 |
| `MarkType` | string? | Mark 类型名 |
| `ElementColor` | Color | 元素颜色 |

### 选中与悬停

```csharp
// Select a data row
chart.Select(hit.RowIndex);

// Update interaction state (hover glow, crosshair position)
chart.Interaction(mousePosition.ToVector2());

// Query current state
int selectedRow = chart.CurrentSelectedRowIndex;
int hoveredRow = chart.CurrentHoveredRowIndex;
string? focusedSeries = chart.CurrentFocusedSeries;
```

### 事件系统

```csharp
// Click event
chart.OnSelectionChanged += (sender, e) =>
{
    GD.Print($"Selected: row {e.RowIndex}, series={e.SeriesKey}");
    // e.Row — the data row
    // e.ScreenPosition — click screen position
    // e.MarkType — e.g. "IntervalMark"
};

// Hover event
chart.OnHover += (sender, e) =>
{
    if (e.RowIndex >= 0)
        GD.Print($"Hovering: row {e.RowIndex}");
};
```

**ChartClickEventArgs 属性：**

| 属性 | 类型 | 说明 |
|------|------|------|
| `Row` | DataRow? | 数据行 |
| `RowIndex` | int | 行索引 |
| `MarkType` | string? | Mark 类型 |
| `ScreenPosition` | Vector2 | 屏幕位置 |
| `SeriesKey` | string? | 系列名 |

### 系列聚焦

当鼠标悬停在某系列上时，其他系列会自动降低透明度，由 `ChartTheme.UnfocusedOpacity` 控制。

---

## 工具提示 (Tooltip)

### 基本使用

```csharp
private TooltipRenderer _tooltip = new();

// In _Process:
_tooltip.Theme = _theme;
_tooltip.Update((float)delta, chart.HitTest(mousePos));
_tooltip.Draw(canvas, canvasWidth, canvasHeight);
```

### TooltipOptions — 配置

```csharp
_tooltip.Options = new TooltipOptions
{
    BackgroundColor = new Color(0.1f, 0.1f, 0.15f, 0.95f),
    TextColor = Colors.White,
    BorderColor = new Color(0.3f, 0.3f, 0.4f),
    CornerRadius = 6f,
    Padding = 8f,
    FontSize = 12f,
};
```

### 自定义内容构建器

**简单文本模式：**

```csharp
_tooltip.Options.ContentBuilder = ctx =>
{
    return new List<string>
    {
        $"Category: {ctx.Row.Get<string>("category")}",
        $"Value: {ctx.Row.Get<int>("value")}",
    };
};
```

**富文本模式：**

```csharp
_tooltip.Options.RichContentBuilder = ctx =>
{
    return new List<TooltipLine>
    {
        TooltipLine.WithIcon(TooltipIcon.Circle, ctx.Color, ctx.Label ?? ""),
        TooltipLine.Plain($"Value: {ctx.Row.Get<int>("value")}"),
    };
};
```

### TooltipLine & TooltipSpan

`TooltipLine` 支持精细控制每行的文字段落样式：

```csharp
var line = new TooltipLine
{
    Spans = new[]
    {
        new TooltipSpan { Text = "Revenue: ", Bold = true },
        new TooltipSpan { Text = "$1,200", Color = Colors.Green },
    }
};
```

**TooltipSpan 属性：**

| 属性 | 类型 | 说明 |
|------|------|------|
| `Text` | string | 文本内容 |
| `Color` | Color? | 文字颜色 |
| `Bold` | bool | 粗体 |
| `Italic` | bool | 斜体 |
| `FontSize` | float? | 字号 |
| `Icon` | TooltipIcon | 图标 (None/Circle/Square/Diamond/Triangle) |
| `Image` | IImageHandle? | 嵌入图像 |

### 动画效果

Tooltip 内置平滑跟随和淡入淡出：

```csharp
_tooltip.SmoothSpeed = 12f;   // position smoothing
_tooltip.FadeSpeed = 8f;      // fade in/out speed
```

---

## 十字线 (Crosshair)

在鼠标位置绘制参考十字线：

```csharp
// After chart.Render():
if (chart.CurrentPlotArea is { } plot)
{
    ChartInteraction.DrawCrosshair(canvas, mousePos, plot, _theme);
}
```

可通过主题配置十字线样式（颜色、宽度、虚线长度），或通过 `EnableCrosshair = false` 禁用。

---

## 实时数据流

### AppendData — 追加数据

```csharp
// Add single row
chart.AppendData(new DataRow().Set("time", now).Set("value", reading));

// Add multiple rows
chart.AppendData(newBatch);
```

### WindowSize — 滚动窗口

设置滚动窗口自动裁剪旧数据：

```csharp
chart.WindowSize = 100;  // keep only latest 100 rows

// Combined with append for real-time streaming:
chart.AppendData(newRow);    // oldest rows auto-trimmed
chart.Render();
```

`WindowSize = 0` 表示不限制（默认）。

### 数据过渡动画

结合 `AnimationController.StartDataTransition()` 实现数据更新时的平滑过渡：

```csharp
chart.Data(newData);
_anim.StartDataTransition(this, duration: 0.4f);
```

---

## 数据变换 (Transform)

### BinTransform — 分箱变换

将连续数值分箱为直方图数据：

```csharp
new Chart(canvas)
    .Data(rawData)
    .Transform(new BinTransform
    {
        Field = "value",
        BinCount = 20,        // or use BinWidth = 5.0
    })
    .Mark(new IntervalMark())
    .Encode(Channel.X, "BinMid")
    .Encode(Channel.Y, "Count")
    .Render();
```

BinTransform 输出字段：
- `BinStart` — 分箱左边界
- `BinEnd` — 分箱右边界
- `BinMid` — 分箱中点
- `Count` — 数据计数

当 `BinCount` 和 `BinWidth` 均未指定时，使用 Sturges 规则自动计算。

### 自定义变换

实现 `IDataTransform` 接口创建自定义变换：

```csharp
public class MovingAverageTransform : IDataTransform
{
    public string Field { get; set; } = "value";
    public int Window { get; set; } = 5;

    public List<DataRow> Apply(List<DataRow> data)
    {
        // Your transform logic here
        // Return modified data rows
    }
}

// Usage:
chart.Transform(new MovingAverageTransform { Field = "price", Window = 7 });
```

变换可以链式组合，按添加顺序依次执行。

---

## 复合图表

### 多 Mark 叠加

在同一 Chart 中叠加多种 Mark：

```csharp
new Chart(canvas)
    .Data(data)
    .Mark(new IntervalMark())  // bars
    .Mark(new LineMark())      // line overlay
    .Encode(Channel.X, "month")
    .Encode(Channel.Y, "revenue")
    .Render();
```

### 双 Y 轴

使用 `Channel.Y2` 和 `LineMark.YChannel` 实现双轴：

```csharp
var lineMark = new LineMark
{
    Smooth = true,
    YChannel = Channel.Y2,  // bind to secondary axis
};

new Chart(canvas)
    .Data(data)
    .Mark(new IntervalMark())
    .Mark(lineMark)
    .Encode(Channel.X, "month")
    .Encode(Channel.Y, "revenue")
    .Encode(Channel.Y2, "cost")
    .YAxis(new AxisConfig { Title = "Revenue" })
    .Y2Axis(new AxisConfig { Title = "Cost" })
    .Render();
```

### 坐标系兼容性

同一 Chart 中的所有 Mark 必须使用兼容坐标系：

| 可以叠加 | 不可叠加 |
|----------|----------|
| IntervalMark + LineMark | IntervalMark + PieMark |
| LineMark + PointMark | LineMark + RadarMark |
| 任意多个笛卡尔 Mark | 笛卡尔 + 极坐标 |

不兼容的 Mark 会被 `ValidateMarkCompatibility()` 自动跳过并输出警告。

---

## 系列信息查询

```csharp
// Get all series with their colors
IReadOnlyList<(string Key, Color Color)> series = chart.GetSeriesInfo();

// Get current render data snapshot
IReadOnlyList<DataRow> snapshot = chart.GetRenderDataSnapshot();

// Current plot area (for custom drawing)
PlotArea? area = chart.CurrentPlotArea;
```

---

## 画布抽象 — ICanvas2D

GodotChart 通过 `ICanvas2D` 接口与底层渲染引擎解耦。当前实现为 `SkiaCanvas2DBackend`（基于 SkiaSharp）。

### 基本绘制

```csharp
using var paint = canvas.CreatePaint()
    .SetColor(Colors.Red)
    .SetStrokeWidth(2f);

canvas.DrawLine(0, 0, 100, 100, paint);
canvas.DrawRect(10, 10, 50, 30, paint);
canvas.DrawCircle(200, 200, 25, paint);
canvas.DrawText("Hello", 10, 50, FontSettings.Default, paint);
```

### 路径绘制

```csharp
using var path = canvas.CreatePath()
    .MoveTo(0, 0)
    .LineTo(100, 50)
    .CubicTo(150, 0, 200, 100, 250, 50)
    .Close();

canvas.Fill(path, fillPaint);
canvas.Stroke(path, strokePaint);
```

### 渐变

```csharp
using var paint = canvas.CreatePaint()
    .SetLinearGradient(0, 0, 100, 0, new[]
    {
        new GradientStop { Offset = 0, Color = Colors.Blue },
        new GradientStop { Offset = 1, Color = Colors.Red },
    });
```

### 变换与裁剪

```csharp
using (canvas.SaveScope())
{
    canvas.Translate(100, 100);
    canvas.Rotate(MathF.PI / 4);    // 45 degrees
    canvas.Scale(2f, 2f);
    canvas.DrawRect(0, 0, 50, 50, paint);
}
// transforms auto-restored
```

---

## 字体设置 — FontSettings

```csharp
var font = new FontSettings
{
    Size = 16f,
    Bold = true,
    Italic = false,
    Family = "Roboto",
    Align = TextAlign.Center,
    LetterSpacing = 1.5f,
    LineHeightMultiplier = 1.2f,
    Decoration = TextDecoration.Underline,
    GodotFont = myGodotFont,  // optional Godot Font resource
};

var metrics = canvas.MeasureText("Hello World", font);
// metrics.Width, metrics.Height
```
