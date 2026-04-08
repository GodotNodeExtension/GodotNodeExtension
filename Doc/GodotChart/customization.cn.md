# 定制与主题

本文档介绍如何对 GodotChart 进行视觉定制，包括主题系统、度量配置、坐标轴和图例、以及自定义渲染器。

---

## 主题系统 — ChartTheme

`ChartTheme` 是一个 Godot `Resource`，所有视觉属性均标记 `[Export]`，可以在 Inspector 中可视化编辑。

### 使用内置主题

```csharp
// Dark theme (default)
var theme = ChartTheme.Dark();

// Light theme
var theme = ChartTheme.Light();

new Chart(canvas)
    .Theme(theme)
    .Data(data)
    .Mark(new IntervalMark())
    .Encode(Channel.X, "x")
    .Encode(Channel.Y, "y")
    .Render();
```

### 克隆与自定义

```csharp
var customTheme = ChartTheme.Dark().Clone();
customTheme.Palette = new Color[]
{
    new(0.2f, 0.6f, 1.0f),
    new(1.0f, 0.4f, 0.3f),
    new(0.3f, 0.9f, 0.5f),
};
customTheme.BackgroundColor = new Color(0.05f, 0.05f, 0.08f);
customTheme.CornerRadius = 6f;
```

### 主题属性分组

ChartTheme 包含 15+ 个 Export Group，以下是主要分组：

#### Color Palette — 色板

| 属性 | 类型 | 说明 |
|------|------|------|
| `Palette` | Color[] | 分类色板（默认 6 色） |
| `SequentialGradient` | Color[] | 连续渐变色阶（5 色） |

#### Chart Frame — 图表框架

| 属性 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `BackgroundColor` | Color | (0.08, 0.08, 0.12) | 背景色 |
| `BackgroundCornerRadius` | float | 8 | 背景圆角 |
| `GridColor` | Color | (1,1,1,0.08) | 网格线颜色 |
| `GridLineWidth` | float | 1 | 网格线宽度 |
| `AxisColor` | Color | (1,1,1,0.4) | 坐标轴颜色 |
| `AxisLineWidth` | float | 2 | 坐标轴线宽 |

#### Typography — 字体排版

| 属性 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `FontFamily` | string? | null | 字体族名称 |
| `Font` | Font? | null | Godot Font 资源 |
| `TitleColor` | Color | — | 标题颜色 |
| `TitleFontSize` | float | 13 | 标题字号 |
| `LabelColor` | Color | — | 标签颜色 |
| `LabelFontSize` | float | 13 | 标签字号 |
| `DataLabelColor` | Color | — | 数据标签颜色 |

#### Layout — 布局

| 属性 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `AxisTitleMargin` | float | 6 | 轴标题外边距 |
| `TitleReservedHeight` | float | 24 | 标题保留高度 |
| `Y2LabelReservedWidth` | float | 35 | 右轴标签保留宽度 |
| `XAxisLabelOffset` | float | 15 | X 轴标签偏移 |
| `YAxisLabelGap` | float | 5 | Y 轴标签间距 |

#### Mark Defaults — 标记默认值

| 属性 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `DefaultMarkColor` | Color | — | 默认标记颜色 |
| `CornerRadius` | float | 3 | 默认圆角 |
| `StrokeWidth` | float | 2 | 默认描边宽度 |

#### Selection & Hover — 选中与悬停

| 属性 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `SelectionColor` | Color | — | 选中高亮色 |
| `SelectionStrokeWidth` | float | 2 | 选中描边宽度 |
| `HoverBrighten` | float | 1.2 | 悬停亮度提升 |
| `UnfocusedOpacity` | float | 0.15 | 非聚焦系列透明度 |
| `HoverScale` | float | 1.05 | 悬停缩放 |

#### Tooltip — 工具提示

| 属性 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `TooltipBackground` | Color | — | 背景色 |
| `TooltipTextColor` | Color | — | 文字颜色 |
| `TooltipBorderColor` | Color | — | 边框颜色 |
| `TooltipBorderWidth` | float | 1 | 边框宽度 |
| `TooltipCornerRadius` | float | 6 | 圆角 |
| `TooltipFontSize` | float | 12 | 字号 |
| `TooltipPadding` | float | 8 | 内边距 |

#### Crosshair — 十字线

| 属性 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `CrosshairColor` | Color | — | 十字线颜色 |
| `CrosshairStrokeWidth` | float | 1 | 线宽 |
| `CrosshairDashLength` | float | 4 | 虚线长度 |
| `EnableCrosshair` | bool | true | 是否启用 |

#### Legend — 图例

| 属性 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `LegendSwatchTextGap` | float | 4 | 色块与文字间距 |
| `LegendDimmedOpacity` | float | 0.3 | 隐藏系列透明度 |
| `LegendSwatchCornerRadius` | float | 2 | 色块圆角 |
| `LegendVerticalItemSpacing` | float | 4 | 垂直图例间距 |
| `LegendBottomGap` | float | 10 | 图例底部间距 |

#### Polar / Segment — 极坐标

| 属性 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `SegmentBorderColor` | Color | — | 分段边框色 |
| `SegmentBorderWidth` | float | 1 | 边框宽度 |
| `ArcGap` | float | 0.02 | 弧间距（弧度） |
| `RingGap` | float | 2 | 旭日图环间距 |
| `PieExplodeRatio` | float | 0.03 | 饼图弹出比 |

此外还有 **Line/Point、Radar、Box、Violin** 等 Mark 专属样式分组。

---

## 度量 (Scale)

Scale 负责将原始数据值映射到 `[0, 1]` 标准化范围。大多数情况下会自动推断，但你可以手动指定以获得精确控制。

### LinearScale — 线性度量

```csharp
// Auto-fit from data (default behavior)
// Or manually specify range:
.Scale(Channel.Y, new LinearScale(0, 100))
```

属性：
- `Min` / `Max` — 数据域范围
- `IncludeZero` — 是否强制包含零点（默认 true）

### OrdinalScale — 分类度量

自动从数据推断分类值，无需手动设置。用于文字类别型的 X 轴。

### LogScale — 对数度量

适合数据跨多个数量级的场景：

```csharp
.Scale(Channel.Y, new LogScale(1, 10000))
```

### ColorScale — 颜色度量

将分类值映射到色板颜色：

```csharp
// Usually auto-inferred from theme palette
.Scale(Channel.Color, new ColorScale())
```

### SequentialColorScale — 连续色阶

将连续数值映射到渐变色：

```csharp
// Used by HeatmapMark automatically
// Can customize gradient colors:
var scale = new SequentialColorScale();
scale.Gradient = new Color[]
{
    new(0.1f, 0.1f, 0.3f),  // low
    new(0.2f, 0.6f, 1.0f),  // mid
    new(1.0f, 0.9f, 0.3f),  // high
};
```

### DivergingColorScale — 发散色阶

以中心点为基准，正负两侧使用不同颜色方向：

```csharp
.Scale(Channel.Color, new DivergingColorScale(-1, 1))
```

### RadialScale — 径向度量

用于极坐标图表中的半径映射。

### TimeScale — 时间度量

将 `DateTime` 值映射到位置，格式会根据时间跨度自动调整。

### BandScale — 分组条带度量

用于分组柱状图的子带布局：

```csharp
var band = new BandScale();
band.SubBandCount = 3;       // 3 series per group
band.Padding = 0.2f;         // outer padding
band.InnerPadding = 0.1f;    // padding between sub-bands
```

---

## 坐标轴配置 — AxisConfig

```csharp
new Chart(canvas)
    .XAxis(new AxisConfig
    {
        Title = "Month",
        Description = "Monthly sales data for 2024",  // tooltip hover
        Unit = "USD",
    })
    .YAxis(new AxisConfig
    {
        Title = "Revenue",
        Unit = "USD",
        Description = "Revenue in US dollars",
    })
    .Y2Axis(new AxisConfig { Title = "Cost" })  // secondary Y axis
    .Render();
```

| 属性 | 类型 | 说明 |
|------|------|------|
| `Title` | string? | 坐标轴标题 |
| `Description` | string? | 悬停时的详细描述 |
| `Unit` | string? | 单位标签（如 "USD", "ms"） |
| `TooltipBuilder` | Func? | 自定义坐标轴工具提示内容 |

---

## 图例配置 — LegendConfig

```csharp
.Legend(new LegendConfig
{
    Position = LegendPosition.Top,   // Top / Bottom / Left / Right / None
    ItemSpacing = 16f,               // 图例项间距
    SwatchSize = 10f,                // 色块大小
    Padding = 6f,                    // 图例区域内边距
})
```

将 `Position` 设为 `LegendPosition.None` 可隐藏图例。

---

## 布局与尺寸

```csharp
var chart = new Chart(canvas);

// Content padding (within chart frame)
chart.PaddingLeft = 60f;
chart.PaddingRight = 20f;
chart.PaddingTop = 40f;
chart.PaddingBottom = 40f;

// Overall chart dimensions
chart.Width = 800f;
chart.Height = 600f;

// Position offset
chart.OffsetX = 10f;
chart.OffsetY = 10f;

// Title
chart.Title = "My Chart";
```

---

## 自定义渲染器

Chart 提供多个渲染器槽位，允许你用自定义函数替换默认绘制行为：

```csharp
var chart = new Chart(canvas);

// Custom background
chart.BackgroundRenderer = ctx =>
{
    // ctx provides: Canvas, PlotArea, Theme, etc.
    ctx.Canvas.DrawRect(0, 0, ctx.Width, ctx.Height, bgPaint);
};

// Custom title
chart.TitleRenderer = ctx => { /* ... */ };

// Custom grid
chart.GridRenderer = ctx => { /* ... */ };

// Custom axis lines
chart.AxisRenderer = ctx => { /* ... */ };

// Custom axis labels
chart.AxisLabelRenderer = ctx => { /* ... */ };

// Custom legend
chart.LegendRenderer = ctx => { /* ... */ };

// Custom crosshair
chart.CrosshairRenderer = ctx => { /* ... */ };
```

渲染器委托类型为 `ChartRenderer`，接收一个包含绘制所需上下文信息的参数。

---

## 批量配置 Mark

使用 `ApplyToAllMarks` 对所有已添加的 Mark 统一设置属性：

```csharp
new Chart(canvas)
    .Data(data)
    .Mark(new IntervalMark())
    .Mark(new LineMark())
    .ApplyToAllMarks(m => m.ShowLabel = true)  // apply to both marks
    .Render();
```

---

## 颜色直接覆盖

可以直接在 Chart 上设置颜色属性覆盖主题值：

```csharp
chart.BackgroundColor = new Color(0.1f, 0.1f, 0.15f);
chart.GridColor = new Color(1f, 1f, 1f, 0.05f);
chart.AxisColor = new Color(1f, 1f, 1f, 0.3f);
```

---

## 系列可见性控制

运行时动态显示/隐藏系列：

```csharp
chart.HideSeries("ProductA");          // Hide specific series
chart.ShowSeries("ProductA");          // Show it back
chart.ToggleSeriesVisibility("ProductA"); // Toggle
chart.ShowAllSeries();                 // Show all

bool hidden = chart.IsSeriesHidden("ProductA");
```

隐藏的系列不参与渲染和 HitTest。
