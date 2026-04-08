# API 参考

本文档列出 GodotChart 所有公共类、接口、枚举及其成员签名。

---

## Chart (主入口)

```csharp
public partial class Chart : IDisposable
```

### 构造与销毁

| 方法 | 返回 | 说明 |
|------|------|------|
| `Chart(ICanvas2D canvas)` | — | 构造函数 |
| `Dispose()` | void | 释放资源 |

### Fluent 构建方法

| 方法 | 返回 | 说明 |
|------|------|------|
| `Theme(ChartTheme theme)` | Chart | 设置主题 |
| `Mark(Mark mark)` | Chart | 添加图形标记 |
| `Mark<T>() where T : Mark, new()` | Chart | 按类型添加标记 |
| `ApplyToAllMarks(Action<Mark> action)` | Chart | 批量配置标记 |
| `Data(IEnumerable<DataRow> rows)` | Chart | 设置数据 |
| `AppendData(DataRow row)` | Chart | 追加单行数据 |
| `AppendData(IEnumerable<DataRow> rows)` | Chart | 追加多行数据 |
| `Encode(Channel ch, string field)` | Chart | 绑定字段编码 |
| `Encode(Channel ch, object constant)` | Chart | 绑定常量编码 |
| `Scale(Channel ch, IScale scale)` | Chart | 设置度量 |
| `Transform(IDataTransform transform)` | Chart | 添加数据变换 |
| `Animate(float progress)` | Chart | 入场动画进度 |
| `Animate(AnimationContext? ctx)` | Chart | 完整动画上下文 |
| `XAxis(AxisConfig config)` | Chart | X 轴配置 |
| `YAxis(AxisConfig config)` | Chart | Y 轴配置 |
| `Y2Axis(AxisConfig config)` | Chart | 第二 Y 轴配置 |
| `Legend(LegendConfig config)` | Chart | 图例配置 |
| `Render()` | Chart | 执行渲染 |

### 交互方法

| 方法 | 返回 | 说明 |
|------|------|------|
| `HitTest(Vector2 pos)` | HitResult? | 命中测试 |
| `Select(int rowIndex)` | Chart | 选中数据行 |
| `Interaction(Vector2 mousePos)` | Chart | 更新交互状态 |

### 系列可见性

| 方法 | 返回 | 说明 |
|------|------|------|
| `HideSeries(string key)` | Chart | 隐藏系列 |
| `ShowSeries(string key)` | Chart | 显示系列 |
| `ToggleSeriesVisibility(string key)` | Chart | 切换系列可见性 |
| `ShowAllSeries()` | Chart | 显示所有系列 |
| `IsSeriesHidden(string key)` | bool | 查询是否隐藏 |

### 状态查询

| 属性/方法 | 类型 | 说明 |
|-----------|------|------|
| `GetSeriesInfo()` | IReadOnlyList<(string, Color)> | 所有系列及颜色 |
| `GetRenderDataSnapshot()` | IReadOnlyList\<DataRow\> | 渲染数据快照 |
| `CurrentFocusedSeries` | string? | 当前聚焦系列 |
| `CurrentSelectedRowIndex` | int | 选中行索引 |
| `CurrentHoveredRowIndex` | int | 悬停行索引 |
| `CurrentPlotArea` | PlotArea? | 当前绘图区 |

### 布局属性

| 属性 | 类型 | 说明 |
|------|------|------|
| `Width` | float | 宽度 |
| `Height` | float | 高度 |
| `OffsetX` | float | X 偏移 |
| `OffsetY` | float | Y 偏移 |
| `PaddingLeft/Right/Top/Bottom` | float | 内边距 |
| `Title` | string? | 图表标题 |
| `WindowSize` | int | 滚动窗口大小（0=不限） |

### 渲染器槽位

| 属性 | 类型 | 说明 |
|------|------|------|
| `BackgroundRenderer` | ChartRenderer? | 背景渲染器 |
| `TitleRenderer` | ChartRenderer? | 标题渲染器 |
| `GridRenderer` | ChartRenderer? | 网格渲染器 |
| `AxisRenderer` | ChartRenderer? | 坐标轴渲染器 |
| `AxisLabelRenderer` | ChartRenderer? | 轴标签渲染器 |
| `LegendRenderer` | ChartRenderer? | 图例渲染器 |
| `CrosshairRenderer` | ChartRenderer? | 十字线渲染器 |

### 颜色覆盖

| 属性 | 类型 | 说明 |
|------|------|------|
| `BackgroundColor` | Color | 背景色 |
| `GridColor` | Color | 网格色 |
| `AxisColor` | Color | 轴色 |

### 事件

| 事件 | 参数类型 | 说明 |
|------|----------|------|
| `OnSelectionChanged` | ChartClickEventArgs | 选中变化 |
| `OnHover` | ChartClickEventArgs | 悬停变化 |

---

## DataRow

```csharp
public class DataRow
```

| 方法 | 返回 | 说明 |
|------|------|------|
| `DataRow()` | — | 默认构造 |
| `DataRow(int fieldCapacity)` | — | 指定初始容量 |
| `Set(string field, object value)` | DataRow | 设置字段（支持链式） |
| `Get<T>(string field)` | T | 类型安全读取 |
| `Get(string field)` | object | 读取为 object |
| `TryGet<T>(string field, out T result)` | bool | 安全尝试读取 |
| `Has(string field)` | bool | 检查字段是否存在 |

---

## Channel (枚举)

```csharp
public enum Channel
{
    X,        // 水平位置
    Y,        // 垂直位置（左轴）
    Y2,       // 垂直位置（右轴）
    Color,    // 颜色
    Size,     // 大小
    Opacity,  // 透明度
    Shape,    // 形状
    Label,    // 标签
}
```

---

## MarkCoordinate (枚举)

```csharp
public enum MarkCoordinate
{
    Cartesian,      // 笛卡尔坐标
    Polar,          // 极坐标
    Hierarchical,   // 层级
    Flow,           // 流向
}
```

---

## Mark 基类

```csharp
public abstract class Mark
```

| 属性 | 类型 | 说明 |
|------|------|------|
| `ShowLabel` | bool | 显示数据标签 |
| `Coordinate` | MarkCoordinate | 坐标系 (readonly) |

---

## 所有 Mark 子类

### IntervalMark

| 属性 | 类型 | 默认值 |
|------|------|--------|
| `BarPadding` | float | 0.2 |
| `CornerRadius` | float | 3 |
| `ShowLabel` | bool | false |
| `ShowValue` | bool | false |
| `Stack` | StackMode | None |
| `Orientation` | BarOrientation | Vertical |

### LineMark

| 属性 | 类型 | 默认值 |
|------|------|--------|
| `StrokeWidth` | float | 2 |
| `Smooth` | bool | true |
| `ShowArea` | bool | false |
| `AreaOpacity` | float | 0.15 |
| `Stack` | StackMode | None |
| `Step` | StepMode | None |
| `YChannel` | Channel | Y |

### PointMark

| 属性 | 类型 | 默认值 |
|------|------|--------|
| `DefaultRadius` | float | 5 |

### PieMark

| 属性 | 类型 | 默认值 |
|------|------|--------|
| `InnerRadius` | float | 0 |
| `StartAngle` | float | -π/2 |
| `ShowLabel` | bool | true |
| `LabelDistance` | float | 1.15 |
| `ExplodeRatio` | float | 0.03 |
| `RadiusFactor` | float | 0.85 |
| `CenterText` | string? | null |
| `CenterFontSize` | float | 18 |
| `CenterSubFontSize` | float | 12 |
| `CenterContentBuilder` | Func? | null |

### RadarMark

| 属性 | 类型 | 默认值 |
|------|------|--------|
| `FillOpacity` | float | 0.2 |
| `StrokeWidth` | float | 2.5 |
| `PointRadius` | float | 4 |
| `GridRings` | int | — |
| `ShowAxisLabels` | bool | — |

### CandlestickMark

| 属性 | 类型 | 默认值 |
|------|------|--------|
| `OpenField` | string | "open" |
| `HighField` | string | "high" |
| `LowField` | string | "low" |
| `CloseField` | string | "close" |
| `BodyWidthRatio` | float | 0.55 |
| `WickWidth` | float | 1.5 |
| `CornerRadius` | float | 1 |
| `BullishColor` | Color | — |
| `BearishColor` | Color | — |

### BoxMark

| 属性 | 类型 | 默认值 |
|------|------|--------|
| `MinField` | string | "min" |
| `Q1Field` | string | "q1" |
| `MedianField` | string | "median" |
| `Q3Field` | string | "q3" |
| `MaxField` | string | "max" |
| `BoxWidthRatio` | float | 0.5 |
| `CornerRadius` | float | 2 |

### HeatmapMark

| 属性 | 类型 | 默认值 |
|------|------|--------|
| `CellGap` | float | 2 |
| `CornerRadius` | float | 3 |
| `ShowLabel` | bool | — |

### GaugeMark

| 属性 | 类型 | 默认值 |
|------|------|--------|
| `ArcWidth` | float | 0.14 |
| `StartAngleDeg` | float | — |
| `EndAngleDeg` | float | — |
| `ShowCenterLabel` | bool | — |
| `ValueColor` | Color | — |
| `TrackColor` | Color | — |

### FunnelMark

| 属性 | 类型 | 默认值 |
|------|------|--------|
| `StageGap` | float | — |
| `MinWidthRatio` | float | — |
| `CornerRadius` | float | — |
| `ShowLabel` | bool | — |

### ViolinMark

| 属性 | 类型 | 默认值 |
|------|------|--------|
| `BinCount` | int | 15 |
| `WidthRatio` | float | — |
| `FillOpacity` | float | — |
| `ShowMedian` | bool | true |
| `ShowBox` | bool | true |

### TreemapMark

| 属性 | 类型 | 默认值 |
|------|------|--------|
| `CellGap` | float | 3 |
| `CornerRadius` | float | 4 |
| `ShowLabel` | bool | — |

### SunburstMark

| 属性 | 类型 | 默认值 |
|------|------|--------|
| `ParentField` | string | — |
| `RadiusFactor` | float | — |
| `InnerRadiusRatio` | float | — |
| `RingGap` | float | — |
| `ShowLabel` | bool | — |

### SankeyMark

| 属性 | 类型 | 默认值 |
|------|------|--------|
| `SourceField` | string | — |
| `TargetField` | string | — |
| `NodeWidth` | float | 14 |
| `ColumnGap` | float | — |
| `NodeGap` | float | — |
| `ShowLabel` | bool | — |

### ChordMark

| 属性 | 类型 | 默认值 |
|------|------|--------|
| `SourceField` | string | — |
| `TargetField` | string | — |
| `ArcWidthRatio` | float | — |
| `ArcGap` | float | — |
| `ShowLabel` | bool | — |

### RangeAreaMark

| 属性 | 类型 | 默认值 |
|------|------|--------|
| `LowerField` | string | — |
| `FillOpacity` | float | 0.3 |
| `ShowBorderLines` | bool | true |
| `StrokeWidth` | float | 2 |
| `Smooth` | bool | true |

### TimelineMark

| 属性 | 类型 | 默认值 |
|------|------|--------|
| `StartField` | string | — |
| `EndField` | string | — |
| `BarHeightRatio` | float | 0.6 |
| `CornerRadius` | float | 4 |
| `ShowLabel` | bool | — |

### LollipopMark

| 属性 | 类型 | 默认值 |
|------|------|--------|
| `DotRadius` | float | 7 |
| `StemWidth` | float | 2.5 |
| `Orientation` | BarOrientation | Vertical |

### WaffleMark

| 属性 | 类型 | 默认值 |
|------|------|--------|
| `TotalCells` | int | 100 |
| `Columns` | int | 10 |
| `CellGap` | float | 3 |
| `CellRadius` | float | 3 |

---

## Scale 接口与实现

### IScale

```csharp
public interface IScale
{
    void Fit(IEnumerable<object> domain);
    double Map(object value);
    string Format(object value);
}
```

### LinearScale

```csharp
public class LinearScale : IScale
{
    public LinearScale();
    public LinearScale(double min, double max);
    public double Min { get; }
    public double Max { get; }
    public bool IncludeZero { get; set; }  // default: true
}
```

### OrdinalScale

```csharp
public class OrdinalScale : IScale
{
    public IReadOnlyList<string> Domain { get; }
}
```

### LogScale

```csharp
public class LogScale : IScale
{
    public LogScale(double min, double max);
    public double Min { get; }
    public double Max { get; }
}
```

### ColorScale

```csharp
public class ColorScale : IScale
{
    public Color MapColor(object value);
}
```

### SequentialColorScale

```csharp
public class SequentialColorScale : IScale
{
    public double Min { get; }
    public double Max { get; }
    public Color[] Gradient { get; set; }
    public Color MapColor(object value);
}
```

### DivergingColorScale

```csharp
public class DivergingColorScale : IScale
{
    public DivergingColorScale(double min, double max);
}
```

### BandScale

```csharp
public class BandScale : IScale
{
    public IReadOnlyList<string> Domain { get; }
    public int SubBandCount { get; set; }       // default: 1
    public float Padding { get; set; }          // default: 0.2
    public float InnerPadding { get; set; }     // default: 0.1
    public double BandWidth { get; }
    public double SubBandWidth { get; }
    public double MapSubBand(object value, int subIndex);
}
```

### RadialScale

```csharp
public class RadialScale : IScale;
```

### TimeScale

```csharp
public class TimeScale : IScale
{
    public DateTime Min { get; }
    public DateTime Max { get; }
}
```

---

## AxisConfig

```csharp
public class AxisConfig
{
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? Unit { get; set; }
    public Func<IReadOnlyList<TooltipLine>>? TooltipBuilder { get; set; }
}
```

---

## LegendConfig

```csharp
public class LegendConfig
{
    public LegendPosition Position { get; set; }  // default: Top
    public float ItemSpacing { get; set; }        // default: 16
    public float SwatchSize { get; set; }         // default: 10
    public float Padding { get; set; }            // default: 6
}

public enum LegendPosition { Top, Bottom, Left, Right, None }
```

---

## StackMode (枚举)

```csharp
public enum StackMode
{
    None,       // 不堆叠
    Stack,      // 累加堆叠
    Normalize,  // 百分比归一化
}
```

---

## StepMode (枚举)

```csharp
public enum StepMode
{
    None,    // 连续线
    After,   // 在点后阶梯
    Before,  // 在点前阶梯
    Center,  // 居中阶梯
}
```

---

## BarOrientation (枚举)

```csharp
public enum BarOrientation { Vertical, Horizontal }
```

---

## 数据变换

### IDataTransform

```csharp
public interface IDataTransform
{
    List<DataRow> Apply(List<DataRow> data);
}
```

### BinTransform

```csharp
public class BinTransform : IDataTransform
{
    public string Field { get; set; }    // default: "value"
    public int? BinCount { get; set; }   // Sturges' rule if null
    public double? BinWidth { get; set; } // priority over BinCount
}
```

输出字段：`BinStart`, `BinEnd`, `BinMid`, `Count`

---

## 编码

### IEncodeValue

```csharp
public interface IEncodeValue;
```

### FieldEncode

```csharp
public class FieldEncode : IEncodeValue
{
    public string FieldName { get; }
}
```

### ConstantEncode

```csharp
public class ConstantEncode : IEncodeValue
{
    public object Value { get; }
}
```

---

## 动画

### AnimationController

```csharp
public class AnimationController
{
    // State
    public float EntryProgress { get; }         // [0,1]
    public float[] SeriesProgress { get; }
    public float GlobalOpacity { get; }         // [0,1]
    public float HoverScale { get; }
    public float DataTransitionProgress { get; }// [0,1]
    public float ExitProgress { get; }          // [0,1]
    public bool IsAnimating { get; }

    // Configuration
    public float EntryDuration { get; set; }     // default: 0.6
    public float SeriesStagger { get; set; }     // default: 0.1
    public int AnimationThreshold { get; set; }  // default: 2000
    public float ExitDuration { get; set; }      // default: 0.3

    // Methods
    public bool ShouldAnimate(int elementCount);
    public void StartEntry(Node owner, int seriesCount, int totalElementCount = 0);
    public void AnimateHover(Node owner, float targetScale);
    public void StartDataTransition(Node owner, float duration = 0.4f);
    public void StartExit(Node owner, Action? onComplete = null);
    public void Reset();
}
```

### AnimationContext

```csharp
public class AnimationContext
{
    public float EntryProgress { get; init; }           // default: 1
    public float GlobalOpacity { get; init; }           // default: 1
    public float HoverScale { get; init; }              // default: 1
    public float DataTransitionProgress { get; init; }  // default: 1
    public float ExitProgress { get; init; }            // default: 0

    public static AnimationContext Default { get; }
}
```

### EaseType & Ease

```csharp
public enum EaseType
{
    Linear, EaseInQuad, EaseOutQuad, EaseOutCubic,
    EaseInOutCubic, EaseOutBack, EaseOutElastic,
}

public static class Ease
{
    public static float Apply(float t, EaseType type);
}
```

---

## 交互

### HitResult

```csharp
public class HitResult
{
    public bool Hit { get; init; }
    public DataRow? Row { get; init; }
    public int RowIndex { get; init; }
    public float ScreenX { get; init; }
    public float ScreenY { get; init; }
    public string? Label { get; init; }
    public string? SeriesKey { get; init; }
    public string? MarkType { get; init; }
    public Color ElementColor { get; init; }
    public string? FocusedSeries { get; set; }
}
```

### ChartClickEventArgs

```csharp
public class ChartClickEventArgs : EventArgs
{
    public DataRow? Row { get; init; }
    public int RowIndex { get; init; }
    public string? MarkType { get; init; }
    public Vector2 ScreenPosition { get; init; }
    public string? SeriesKey { get; init; }
}
```

### InteractionState (枚举)

```csharp
public enum InteractionState { Normal, Hovered, Selected }
```

### ChartInteraction (静态工具)

```csharp
public static class ChartInteraction
{
    public static void DrawCrosshair(ICanvas2D canvas, Vector2 mousePos, PlotArea plot, ChartTheme? theme = null);
    public static HitResult? TestAll(List<Mark> marks, MarkContext ctx, Vector2 mousePos);
}
```

---

## 工具提示

### TooltipRenderer

```csharp
public class TooltipRenderer
{
    public float SmoothSpeed { get; set; }   // default: 12
    public float FadeSpeed { get; set; }     // default: 8
    public bool IsVisible { get; }
    public TooltipOptions Options { get; set; }
    public ChartTheme? Theme { get; set; }

    public void Update(float delta, HitResult? hit);
    public void Draw(ICanvas2D canvas, int canvasW, int canvasH);
}
```

### TooltipOptions

```csharp
public class TooltipOptions
{
    public Func<TooltipContext, IReadOnlyList<TooltipLine>>? RichContentBuilder { get; set; }
    public Func<TooltipContext, IReadOnlyList<string>>? ContentBuilder { get; set; }
    public Color? BackgroundColor { get; set; }
    public Color? TextColor { get; set; }
    public Color? BorderColor { get; set; }
    public float CornerRadius { get; set; }  // default: 6
    public float Padding { get; set; }       // default: 8
    public float? FontSize { get; set; }
}
```

### TooltipLine & TooltipSpan

```csharp
public struct TooltipLine
{
    public TooltipSpan[] Spans { get; init; }
    public static TooltipLine Plain(string text);
    public static TooltipLine WithIcon(TooltipIcon icon, Color color, string text);
}

public struct TooltipSpan
{
    public string Text { get; init; }
    public Color? Color { get; init; }
    public bool Bold { get; init; }
    public bool Italic { get; init; }
    public float? FontSize { get; init; }
    public TextDecoration Decoration { get; init; }
    public float LetterSpacing { get; init; }
    public string? Family { get; init; }
    public Font? GodotFont { get; init; }
    public TooltipIcon Icon { get; init; }
    public IImageHandle? Image { get; init; }
}

public enum TooltipIcon { None, Circle, Square, Diamond, Triangle }
```

---

## ChartTheme

```csharp
[GlobalClass]
public partial class ChartTheme : Resource
{
    // Factory methods
    public static ChartTheme Dark();
    public static ChartTheme Light();
    public ChartTheme Clone();

    // Static palettes
    public static readonly Color[] DefaultPalette;
    public static readonly Color[] DefaultSequentialGradient;

    // 80+ [Export] properties organized in groups:
    // Color Palette, Chart Frame, Layout, Typography,
    // Mark Defaults, Selection & Hover, Polar / Segment,
    // Tooltip, Crosshair, Legend, Line/Point, Radar,
    // Box, Violin, etc.
}
```

完整属性列表见 [定制与主题](customization.cn.md#主题属性分组)。

---

## Canvas 抽象

### ICanvas2D

```csharp
public interface ICanvas2D : IDisposable
{
    void Resize(int width, int height);
    void BeginFrame();
    void EndFrame();
    void Clear(Color color);

    IPath2D CreatePath();
    IPaint2D CreatePaint();

    void Stroke(IPath2D path, IPaint2D paint);
    void Fill(IPath2D path, IPaint2D paint);
    void StrokeAndFill(IPath2D path, IPaint2D stroke, IPaint2D fill);

    void DrawLine(float x0, float y0, float x1, float y1, IPaint2D paint);
    void DrawRect(float x, float y, float w, float h, IPaint2D paint);
    void DrawCircle(float cx, float cy, float r, IPaint2D paint);
    void DrawText(string text, float x, float y, FontSettings font, IPaint2D paint);

    IImageHandle LoadImage(int w, int h, byte[] rgbaPixels);
    IImageHandle LoadImage(Texture2D texture);
    void DrawImage(IImageHandle img, float x, float y, float dstW, float dstH, float opacity = 1f);

    void Save();
    void Restore();
    IDisposable SaveScope();
    void Translate(float x, float y);
    void Scale(float sx, float sy);
    void Rotate(float angle);
    void ClipRect(float x, float y, float w, float h);

    void Tick();
    TextMetrics MeasureText(string text, FontSettings font);
    CanvasCapabilities Capabilities { get; }
}
```

### IPath2D

```csharp
public interface IPath2D : IDisposable
{
    IPath2D MoveTo(float x, float y);
    IPath2D LineTo(float x, float y);
    IPath2D CubicTo(float cx1, float cy1, float cx2, float cy2, float x, float y);
    IPath2D QuadTo(float cx, float cy, float x, float y);
    IPath2D ArcTo(float cx, float cy, float radius, float startAngle, float endAngle, bool clockwise = false);
    IPath2D Rect(float x, float y, float w, float h);
    IPath2D RoundRect(float x, float y, float w, float h, float radius);
    IPath2D Circle(float cx, float cy, float radius);
    IPath2D Close();
    IPath2D Reset();
}
```

### IPaint2D

```csharp
public interface IPaint2D : IDisposable
{
    IPaint2D SetColor(Color color);
    IPaint2D SetStrokeWidth(float width);
    IPaint2D SetAntiAlias(bool aa);
    IPaint2D SetLineCap(LineCap cap);
    IPaint2D SetLineJoin(LineJoin join);
    IPaint2D SetMiterLimit(float limit);
    IPaint2D SetLineDash(float[] pattern, float offset = 0f);
    IPaint2D SetOpacity(float alpha);
    IPaint2D SetLinearGradient(float x0, float y0, float x1, float y1, GradientStop[] stops);
    IPaint2D SetRadialGradient(float cx, float cy, float radius, GradientStop[] stops);
}
```

### FontSettings

```csharp
public readonly record struct FontSettings
{
    public float Size { get; init; }                  // default: 13
    public string? Family { get; init; }
    public bool Bold { get; init; }
    public bool Italic { get; init; }
    public float LetterSpacing { get; init; }
    public float LineHeightMultiplier { get; init; }  // default: 1.2
    public TextAlign Align { get; init; }
    public TextDecoration Decoration { get; init; }
    public Font? GodotFont { get; init; }

    public static FontSettings Default { get; }
}

public enum TextAlign { Left, Center, Right }
```
