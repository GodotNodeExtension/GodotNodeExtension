# 图表类型

GodotChart 支持 19 种图表类型，按坐标系分为四大类。本文档为每种图表提供属性说明和完整的构造示例。

---

## 笛卡尔坐标系 (Cartesian)

### 柱状图 — IntervalMark

最基础的分类对比图表，支持垂直/水平方向和堆叠模式。

**属性：**

| 属性 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `BarPadding` | float | 0.2 | 柱间距比例 |
| `CornerRadius` | float | 3 | 圆角半径 |
| `ShowLabel` | bool | false | 显示数值标签 |
| `ShowValue` | bool | false | 显示值文本 |
| `Stack` | StackMode | None | 堆叠模式 (None / Stack / Normalize) |
| `Orientation` | BarOrientation | Vertical | 方向 (Vertical / Horizontal) |

**基本柱状图：**

```csharp
new Chart(canvas)
    .Data(data)
    .Mark(new IntervalMark { CornerRadius = 3, BarPadding = 0.25f, ShowLabel = true })
    .Encode(Channel.X, "month")
    .Encode(Channel.Y, "value")
    .Encode(Channel.Color, "type")
    .XAxis(new AxisConfig { Title = "Month" })
    .YAxis(new AxisConfig { Title = "Amount", Unit = "USD" })
    .Legend(new LegendConfig { Position = LegendPosition.Top })
    .Render();
```

**水平柱状图：**

```csharp
new Chart(canvas)
    .Data(data)
    .Mark(new IntervalMark
    {
        CornerRadius = 3,
        BarPadding = 0.25f,
        Orientation = BarOrientation.Horizontal,
    })
    .Encode(Channel.X, "value")    // X = numeric
    .Encode(Channel.Y, "month")    // Y = category
    .XAxis(new AxisConfig { Title = "Amount", Unit = "USD" })
    .YAxis(new AxisConfig { Title = "Month" })
    .Render();
```

**堆叠柱状图：**

```csharp
new Chart(canvas)
    .Data(combined)
    .Mark(new IntervalMark
    {
        CornerRadius = 2,
        BarPadding = 0.25f,
        Stack = StackMode.Stack,    // ← key option
    })
    .Encode(Channel.X, "month")
    .Encode(Channel.Y, "value")
    .Encode(Channel.Color, "type")
    .Legend(new LegendConfig { Position = LegendPosition.Top })
    .Render();
```

**百分比堆叠：** 将 `Stack` 设为 `StackMode.Normalize` 即可将 Y 轴范围归一化到 0-100%。

---

### 折线图 — LineMark

连续数据趋势可视化，支持平滑曲线、面积填充和阶梯线。

**属性：**

| 属性 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `StrokeWidth` | float | 2 | 线宽 |
| `Smooth` | bool | true | Catmull-Rom 平滑 |
| `ShowArea` | bool | false | 填充线下面积 |
| `AreaOpacity` | float | 0.15 | 面积填充透明度 |
| `Stack` | StackMode | None | 堆叠模式 |
| `Step` | StepMode | None | 阶梯模式 (None / After / Before / Center) |
| `YChannel` | Channel | Y | Y 通道绑定 (用于双轴) |

**多系列折线：**

```csharp
new Chart(canvas)
    .Data(multiSeriesData)
    .Mark(new LineMark { Smooth = true, StrokeWidth = 2.5f })
    .Encode(Channel.X, "month")
    .Encode(Channel.Y, "value")
    .Encode(Channel.Color, "series")
    .XAxis(new AxisConfig { Title = "Month" })
    .YAxis(new AxisConfig { Title = "Sales", Unit = "Units" })
    .Legend(new LegendConfig { Position = LegendPosition.Top })
    .Render();
```

**面积图：**

```csharp
new Chart(canvas)
    .Data(data)
    .Mark(new LineMark
    {
        Smooth = true,
        ShowArea = true,
        AreaOpacity = 0.25f,
        StrokeWidth = 3f,
    })
    .Encode(Channel.X, "month")
    .Encode(Channel.Y, "value")
    .Render();
```

**堆叠面积：**

```csharp
new Chart(canvas)
    .Data(multiSeriesData)
    .Mark(new LineMark
    {
        Smooth = true,
        ShowArea = true,
        AreaOpacity = 0.4f,
        Stack = StackMode.Stack,
    })
    .Encode(Channel.X, "month")
    .Encode(Channel.Y, "value")
    .Encode(Channel.Color, "series")
    .Render();
```

**阶梯线：**

```csharp
new Chart(canvas)
    .Data(data)
    .Mark(new LineMark
    {
        Smooth = false,
        StrokeWidth = 2.5f,
        Step = StepMode.After,  // After / Before / Center
    })
    .Encode(Channel.X, "month")
    .Encode(Channel.Y, "value")
    .Encode(Channel.Color, "series")
    .Render();
```

---

### 散点图 / 气泡图 — PointMark

用于展示两个或多个变量之间的关系。通过 `Size` 和 `Opacity` 通道可扩展为气泡图。

**属性：**

| 属性 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `DefaultRadius` | float | 5 | 默认点半径 |

**气泡图：**

```csharp
new Chart(canvas)
    .Data(bubbleData)
    .Mark(new PointMark { DefaultRadius = 6f })
    .Encode(Channel.X, "x")
    .Encode(Channel.Y, "y")
    .Encode(Channel.Size, "marketShare")       // ← size by field
    .Encode(Channel.Color, "category")
    .Encode(Channel.Opacity, "confidence")     // ← opacity by field
    .Scale(Channel.X, new LinearScale(0, 100))
    .Scale(Channel.Y, new LinearScale(0, 100))
    .Render();
```

---

### K 线图 — CandlestickMark

金融 OHLC（Open/High/Low/Close）数据可视化。

**属性：**

| 属性 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `OpenField` | string | "open" | 开盘字段名 |
| `HighField` | string | "high" | 最高价字段名 |
| `LowField` | string | "low" | 最低价字段名 |
| `CloseField` | string | "close" | 收盘字段名 |
| `BodyWidthRatio` | float | 0.55 | 蜡烛体宽度比 |
| `WickWidth` | float | 1.5 | 影线宽度 |
| `CornerRadius` | float | 1 | 蜡烛体圆角 |
| `BullishColor` | Color | — | 阳线颜色 |
| `BearishColor` | Color | — | 阴线颜色 |

```csharp
var stockData = new List<DataRow>
{
    new DataRow().Set("date", "Day1").Set("open", 100).Set("high", 115)
                 .Set("low", 95).Set("close", 110),
    // ...
};

new Chart(canvas)
    .Data(stockData)
    .Mark(new CandlestickMark
    {
        OpenField = "open",
        HighField = "high",
        LowField = "low",
        CloseField = "close",
        BodyWidthRatio = 0.55f,
        WickWidth = 1.5f,
    })
    .Encode(Channel.X, "date")
    .Render();
```

---

### 箱线图 — BoxMark

统计分布的五数概括（Min、Q1、Median、Q3、Max）。

**属性：**

| 属性 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `MinField` | string | "min" | 最小值字段 |
| `Q1Field` | string | "q1" | 下四分位字段 |
| `MedianField` | string | "median" | 中位数字段 |
| `Q3Field` | string | "q3" | 上四分位字段 |
| `MaxField` | string | "max" | 最大值字段 |
| `BoxWidthRatio` | float | 0.5 | 箱体宽度比 |
| `CornerRadius` | float | 2 | 圆角 |

```csharp
new Chart(canvas)
    .Data(boxPlotData)
    .Mark(new BoxMark
    {
        MinField = "min", Q1Field = "q1",
        MedianField = "median", Q3Field = "q3", MaxField = "max",
        BoxWidthRatio = 0.5f,
    })
    .Encode(Channel.X, "stat")
    .Encode(Channel.Y, "median")
    .Scale(Channel.Y, new LinearScale(0, 100))
    .XAxis(new AxisConfig { Title = "Stat" })
    .YAxis(new AxisConfig { Title = "Value" })
    .Render();
```

---

### 小提琴图 — ViolinMark

展示数据的概率密度分布，比箱线图信息更丰富。

**属性：**

| 属性 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `BinCount` | int | 15 | 直方图分箱数 |
| `WidthRatio` | float | — | 小提琴宽度比 |
| `FillOpacity` | float | — | 填充透明度 |
| `ShowMedian` | bool | true | 显示中位线 |
| `ShowBox` | bool | true | 显示内嵌箱体 |

```csharp
new Chart(canvas)
    .Data(violinData)
    .Mark(new ViolinMark { BinCount = 15, ShowMedian = true, ShowBox = true })
    .Encode(Channel.X, "weapon")
    .Encode(Channel.Y, "damage")
    .Encode(Channel.Color, "weapon")
    .XAxis(new AxisConfig { Title = "Weapon" })
    .YAxis(new AxisConfig { Title = "Damage" })
    .Render();
```

---

### 热力图 — HeatmapMark

矩阵风格的色块图，颜色编码数值大小。

**属性：**

| 属性 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `CellGap` | float | 2 | 网格间距 |
| `CornerRadius` | float | 3 | 圆角 |
| `ShowLabel` | bool | — | 显示数值 |

```csharp
new Chart(canvas)
    .Data(heatmapData)
    .Mark(new HeatmapMark { CellGap = 2f, CornerRadius = 3f, ShowLabel = true })
    .Encode(Channel.X, "x")
    .Encode(Channel.Y, "y")
    .Encode(Channel.Color, "value")
    .Render();
```

**发散色阶热力图：**

```csharp
new Chart(canvas)
    .Data(heatmapData)
    .Mark(new HeatmapMark { CellGap = 2f, CornerRadius = 3f, ShowLabel = true })
    .Encode(Channel.X, "x")
    .Encode(Channel.Y, "y")
    .Encode(Channel.Color, "value")
    .Scale(Channel.Color, new DivergingColorScale(-1, 1)) // ← diverging scale
    .Render();
```

---

### 范围面积图 — RangeAreaMark

展示数据的上下界范围（如温度区间、置信区间）。

**属性：**

| 属性 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `LowerField` | string | — | 下界字段名 |
| `FillOpacity` | float | 0.3 | 填充透明度 |
| `ShowBorderLines` | bool | true | 显示边界线 |
| `StrokeWidth` | float | 2 | 边界线宽度 |
| `Smooth` | bool | true | 平滑曲线 |

```csharp
new Chart(canvas)
    .Data(rangeData)
    .Mark(new RangeAreaMark
    {
        LowerField = "lower",
        FillOpacity = 0.3f,
        ShowBorderLines = true,
        Smooth = true,
    })
    .Encode(Channel.X, "day")
    .Encode(Channel.Y, "high")   // upper bound
    .Scale(Channel.Y, new LinearScale(10, 40))
    .XAxis(new AxisConfig { Title = "Day" })
    .YAxis(new AxisConfig { Title = "Temperature", Unit = "°C" })
    .Render();
```

---

### 时间轴 — TimelineMark

甘特图风格的时间区间图。

**属性：**

| 属性 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `StartField` | string | — | 起始时间字段 |
| `EndField` | string | — | 结束时间字段 |
| `BarHeightRatio` | float | 0.6 | 条高度比 |
| `CornerRadius` | float | 4 | 圆角 |
| `ShowLabel` | bool | — | 显示标签 |

```csharp
new Chart(canvas)
    .Data(timelineData)
    .Mark(new TimelineMark
    {
        StartField = "start",
        EndField = "end",
        BarHeightRatio = 0.6f,
        CornerRadius = 4f,
        ShowLabel = true,
    })
    .Encode(Channel.Y, "buff")
    .Encode(Channel.Color, "type")
    .Scale(Channel.X, new LinearScale(0, 14))
    .XAxis(new AxisConfig { Title = "Time", Unit = "s" })
    .YAxis(new AxisConfig { Title = "Buff" })
    .Render();
```

---

### 棒棒糖图 — LollipopMark

圆点 + 线段组合的柱状图变体。

**属性：**

| 属性 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `DotRadius` | float | 7 | 圆点半径 |
| `StemWidth` | float | 2.5 | 线段宽度 |
| `Orientation` | BarOrientation | Vertical | 方向 |

```csharp
new Chart(canvas)
    .Data(data)
    .Mark(new LollipopMark { DotRadius = 7f, StemWidth = 2.5f })
    .Encode(Channel.X, "month")
    .Encode(Channel.Y, "value")
    .Encode(Channel.Color, "constant:Revenue")
    .XAxis(new AxisConfig { Title = "Month" })
    .YAxis(new AxisConfig { Title = "Revenue", Unit = "USD" })
    .Render();
```

> **提示：** `"constant:Revenue"` 语法可以为编码绑定一个常量值而非数据字段，常用于单系列图表的固定颜色标签。

---

## 极坐标系 (Polar)

### 饼图 / 圆环图 — PieMark

分类占比可视化。设置 `InnerRadius > 0` 变为圆环（甜甜圈）图。

**属性：**

| 属性 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `InnerRadius` | float | 0 | 0=饼图，>0=圆环图 |
| `StartAngle` | float | -π/2 | 起始角度（从 12 点方向） |
| `ShowLabel` | bool | true | 显示分段标签 |
| `LabelDistance` | float | 1.15 | 标签到边缘距离比 |
| `ExplodeRatio` | float | 0.03 | 悬停时弹出距离 |
| `RadiusFactor` | float | 0.85 | 最大半径因子 |
| `CenterText` | string? | null | 圆环中心文本 |
| `CenterFontSize` | float | 18 | 中心文本字号 |
| `CenterContentBuilder` | Func? | null | 自定义中心内容 |

**饼图：**

```csharp
new Chart(canvas)
    .Data(data)
    .Mark(new PieMark { ShowLabel = true, LabelDistance = 1.18f })
    .Encode(Channel.X, "type")
    .Encode(Channel.Y, "value")
    .Encode(Channel.Color, "type")
    .Legend(new LegendConfig { Position = LegendPosition.Top })
    .Render();
```

**圆环图 + 中心文本：**

```csharp
new Chart(canvas)
    .Data(data)
    .Mark(new PieMark
    {
        InnerRadius = 0.5f,
        ShowLabel = true,
        CenterText = "Total:\n100K",
    })
    .Encode(Channel.X, "category")
    .Encode(Channel.Y, "value")
    .Render();
```

---

### 雷达图 — RadarMark

多维属性对比蛛网图，适用于角色属性、产品评分等场景。

**属性：**

| 属性 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `FillOpacity` | float | 0.2 | 区域填充透明度 |
| `StrokeWidth` | float | 2.5 | 线宽 |
| `PointRadius` | float | 4 | 数据点半径 |
| `GridRings` | int | — | 网格环数 |
| `ShowAxisLabels` | bool | — | 显示轴标签 |

```csharp
new Chart(canvas)
    .Data(radarData)
    .Mark(new RadarMark { FillOpacity = 0.2f, StrokeWidth = 2.5f, PointRadius = 4f })
    .Encode(Channel.X, "stat")
    .Encode(Channel.Y, "value")
    .Encode(Channel.Color, "player")
    .Scale(Channel.Y, new LinearScale(0, 100))
    .Legend(new LegendConfig { Position = LegendPosition.Top })
    .Render();
```

---

### 仪表盘 — GaugeMark

单值指标的弧形仪表，适用于进度、健康值等场景。

**属性：**

| 属性 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `ArcWidth` | float | 0.14 | 弧宽比 |
| `StartAngleDeg` | float | — | 起始角度（度） |
| `EndAngleDeg` | float | — | 结束角度（度） |
| `ShowCenterLabel` | bool | — | 显示中心数值 |
| `ValueColor` | Color | — | 指标弧颜色 |
| `TrackColor` | Color | — | 轨道背景色 |

```csharp
new Chart(canvas)
    .Data(new List<DataRow> { new DataRow().Set("label", "HP").Set("value", 75) })
    .Mark(new GaugeMark
    {
        ArcWidth = 0.14f,
        ShowCenterLabel = true,
        ValueColor = new Color(0.29f, 0.85f, 0.60f),
    })
    .Encode(Channel.X, "label")
    .Encode(Channel.Y, "value")
    .Scale(Channel.Y, new LinearScale(0, 100))
    .Render();
```

---

### 漏斗图 — FunnelMark

转化流程可视化，从上到下递减。

**属性：**

| 属性 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `StageGap` | float | — | 层间距 |
| `MinWidthRatio` | float | — | 最窄层宽度比 |
| `CornerRadius` | float | — | 圆角 |
| `ShowLabel` | bool | — | 显示标签 |

```csharp
var funnelData = new List<DataRow>
{
    new DataRow().Set("stage", "Visit").Set("count", 10000),
    new DataRow().Set("stage", "Browse").Set("count", 6000),
    new DataRow().Set("stage", "Cart").Set("count", 3000),
    new DataRow().Set("stage", "Purchase").Set("count", 1200),
};

new Chart(canvas)
    .Data(funnelData)
    .Mark(new FunnelMark { ShowLabel = true })
    .Encode(Channel.X, "stage")
    .Encode(Channel.Y, "count")
    .Render();
```

---

## 层级结构 (Hierarchical)

### 矩形树图 — TreemapMark

用嵌套矩形表示层级数据的占比关系。

**属性：**

| 属性 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `CellGap` | float | 3 | 矩形间距 |
| `CornerRadius` | float | 4 | 圆角 |
| `ShowLabel` | bool | — | 显示标签 |

```csharp
new Chart(canvas)
    .Data(treemapData)
    .Mark(new TreemapMark { ShowLabel = true, CellGap = 3f, CornerRadius = 4f })
    .Encode(Channel.X, "category")
    .Encode(Channel.Y, "amount")
    .Encode(Channel.Color, "category")
    .Render();
```

---

### 旭日图 — SunburstMark

层级饼图，从内到外展开层级结构。

**属性：**

| 属性 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `ParentField` | string | — | 父节点字段名 |
| `RadiusFactor` | float | — | 最大半径因子 |
| `InnerRadiusRatio` | float | — | 内圈半径比 |
| `RingGap` | float | — | 环间距 |
| `ShowLabel` | bool | — | 显示标签 |

```csharp
new Chart(canvas)
    .Data(sunburstData)
    .Mark(new SunburstMark { ShowLabel = true })
    .Encode(Channel.X, "skill")
    .Encode(Channel.Y, "points")
    .Encode(Channel.Color, "skill")
    .Render();
```

---

## 流向关系 (Flow)

### 桑基图 — SankeyMark

展示流量在节点间的分布和转移。

**属性：**

| 属性 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `SourceField` | string | — | 源节点字段 |
| `TargetField` | string | — | 目标节点字段 |
| `NodeWidth` | float | 14 | 节点宽度 |
| `ColumnGap` | float | — | 列间距 |
| `NodeGap` | float | — | 节点间距 |
| `ShowLabel` | bool | — | 显示标签 |

```csharp
var sankeyData = new List<DataRow>
{
    new DataRow().Set("source", "Mine").Set("target", "Furnace").Set("amount", 100),
    new DataRow().Set("source", "Furnace").Set("target", "Workshop").Set("amount", 60),
    new DataRow().Set("source", "Furnace").Set("target", "Market").Set("amount", 40),
};

new Chart(canvas)
    .Data(sankeyData)
    .Mark(new SankeyMark { ShowLabel = true, NodeWidth = 14f })
    .Encode(Channel.Y, "amount")
    .Encode(Channel.Color, "source")
    .Render();
```

---

### 弦图 — ChordMark

环形 inter-关系图，展示实体间相互关系的强度。

**属性：**

| 属性 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `SourceField` | string | — | 源实体字段 |
| `TargetField` | string | — | 目标实体字段 |
| `ArcWidthRatio` | float | — | 弧宽比 |
| `ArcGap` | float | — | 弧间距 |
| `ShowLabel` | bool | — | 显示标签 |

```csharp
var chordData = new List<DataRow>
{
    new DataRow().Set("source", "Alliance").Set("target", "Horde").Set("trade", 50),
    new DataRow().Set("source", "Alliance").Set("target", "Neutral").Set("trade", 30),
    new DataRow().Set("source", "Horde").Set("target", "Neutral").Set("trade", 40),
};

new Chart(canvas)
    .Data(chordData)
    .Mark(new ChordMark { ShowLabel = true })
    .Encode(Channel.Y, "trade")
    .Encode(Channel.Color, "source")
    .Render();
```

---

## 特殊类型

### 华夫饼图 — WaffleMark

用方格矩阵表示百分比/占比。

**属性：**

| 属性 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `TotalCells` | int | 100 | 总格子数 |
| `Columns` | int | 10 | 列数 |
| `CellGap` | float | 3 | 格间距 |
| `CellRadius` | float | 3 | 格圆角 |

```csharp
new Chart(canvas)
    .Data(data)
    .Mark(new WaffleMark { TotalCells = 100, Columns = 10, CellGap = 3f, CellRadius = 3f })
    .Encode(Channel.X, "type")
    .Encode(Channel.Y, "value")
    .Encode(Channel.Color, "type")
    .Render();
```

---

## 直方图 — 数据变换

直方图并非独立的 Mark，而是通过 `BinTransform` 数据变换 + `IntervalMark` 实现：

```csharp
var rawData = measurements.ConvertAll(r =>
    new DataRow().Set("value", r.Get<double>("damage")));

new Chart(canvas)
    .Data(rawData)
    .Transform(new BinTransform { Field = "value", BinCount = 12 })
    .Mark(new IntervalMark { CornerRadius = 2, BarPadding = 0.05f })
    .Encode(Channel.X, "BinMid")    // BinTransform outputs: BinStart, BinEnd, BinMid, Count
    .Encode(Channel.Y, "Count")
    .XAxis(new AxisConfig { Title = "Value" })
    .YAxis(new AxisConfig { Title = "Frequency" })
    .Render();
```

`BinTransform` 会将连续数值字段自动分箱，输出以下字段：
- `BinStart` — 分箱起始值
- `BinEnd` — 分箱结束值
- `BinMid` — 分箱中点值
- `Count` — 落入该箱的数据行数

---

## 复合图表 — 多 Mark 叠加

可以在同一图表中叠加多个 Mark（需使用兼容的坐标系）：

```csharp
var barMark = new IntervalMark { CornerRadius = 3, BarPadding = 0.3f };
var lineMark = new LineMark
{
    Smooth = true,
    StrokeWidth = 2.5f,
    YChannel = Channel.Y2,  // ← use secondary Y axis
};

new Chart(canvas)
    .Data(monthlySales)
    .Mark(barMark)    // first mark
    .Mark(lineMark)   // second mark
    .Encode(Channel.X, "month")
    .Encode(Channel.Y, "revenue")     // bar uses Y
    .Encode(Channel.Y2, "cost")       // line uses Y2
    .XAxis(new AxisConfig { Title = "Month" })
    .YAxis(new AxisConfig { Title = "Revenue", Unit = "USD" })
    .Y2Axis(new AxisConfig { Title = "Cost", Unit = "USD" })
    .Render();
```

> **注意：** 不同坐标系的 Mark 不能混用（如笛卡尔 + 极坐标）。`ValidateMarkCompatibility()` 会自动跳过不兼容的 Mark 并输出警告。
