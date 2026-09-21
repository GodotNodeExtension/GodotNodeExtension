[英文](chart-types.md) | **中文**

# 图表类型

GodotChart 支持 22 种图表类型（`ChartKind`，由 20 个 `Mark` 类实现；另有不承载任何类型、只画参考线的注解 mark `SectionMark`，它是第 21 个子类），按坐标系分为四大类。本文档为每种图表提供属性说明和完整的构造示例。

## 数据结构一览

每张图都由一行行键值字段喂入。下表说明每种图表里**一行**代表什么、会读取哪些字段。通道（`X`、`Y`、`Color` 等）通过
`Chart.Encode(channel, "field")` 绑定到你自己的字段名，下表中的名字是 `ChartView` 默认使用的约定。
通道是**绑定而不是值**：`Encode` 和节点上的 `XField` / `YField` / `ColorField` / … 放的都是字段**名**，
每一行在该字段里的值才驱动那个元素。读取自身固定字段
（`open`、`min`、`parent`、`start` 等）的类型按这些固定名字列出——它们可以通过对应属性改名，而不是通过通道。

| 图表 | 一行代表 | 读取字段 |
|------|----------|----------|
| 柱状图（`IntervalMark`） | 一根柱子 | `X` 类目、`Y` 数值、`Color` 系列（用于堆叠） |
| 折线图（`LineMark`） | 一个顶点 | `X`、`Y`、`Color` 系列 |
| 面积图（`LineMark`） | 一个顶点 | 同折线图，另加一条填充基线 |
| 散点图 / 气泡图（`PointMark`） | 一个点 | `X`、`Y`、`Size`（图表级编码）、`Color`、`Shape` 符号 |
| K 线图 | 一个周期 | `X` 周期 + `open` `high` `low` `close` |
| 箱线图 | 一组五数概括 | `X` 类目 + `min` `q1` `median` `q3` `max` |
| 小提琴图 | 一个样本（每组 >= 2 行） | `X` 分组、`Y` 数值 |
| 热力图 | 一个单元格 | `X` 列、`Y` 行、`Color` 数值 |
| 范围面积图 | 单条带的一个分段 | `X`、`Y` 上界、`lower` |
| 时间轴 | 一个区间 | 类目（在 `Y` 上）+ `start` `end` |
| 里程碑图（`MilestoneMark`） | 一个事件 | `X` 位置 + `label`，可选 `lane`、`Color`、`Shape` |
| 棒棒糖图 | 一根线段及其圆点 | `X` 类目、`Y` 数值、`Shape` 圆点符号 |
| 饼图 / 圆环图 | 一个扇区 | `Y` 数值、`X` 标签 |
| 雷达图 | 某系列的一个顶点 | `X` 维度、`Y` 数值、`Color` 系列 |
| 仪表盘 | 整张图（**仅第一行**） | `Y` 数值 |
| 漏斗图 | 一层（行序有意义） | `Y` 数值、`X` 标签 |
| 矩形树图 | 一个节点 | `X` 标签、`Y` 数值、`parent` |
| 旭日图 | 一个环上的节点 | `X` 标签、`Y` 数值、`parent` |
| 桑基图 | 一条流 | `source` `target` + `Y` 权重 |
| 弦图 | 一条弦 | `source` `target` + `Y` 权重 |
| 华夫饼图 | 某个类目在网格中占的份额 | `Y` 权重、`Color` |

---

## 笛卡尔坐标系 (Cartesian)

### 柱状图 — IntervalMark

最基础的分类对比图表，支持垂直/水平方向和堆叠模式。

**数据：** 一行 = 一根柱子。

| 通道 / 字段 | 是否必需 | 含义 |
|-------------|----------|------|
| `X` → 类目字段 | 是 | 类目槽位。该列最终必须落在**序数（ordinal）刻度**上：数值型类目列会被自动适配为线性刻度，此时该 Mark 什么都不画。 |
| `Y` → 数值字段 | 是 | 柱高数值。缺少任一字段的行会被跳过；数值非法的行同样被跳过，且该字段只告警一次。 |
| `Color` → 系列字段 | 使用 `Stack` 时必需 | 系列键。堆叠依赖该通道：没有它，所有行都落到同一系列，同类目的柱子只会互相重叠。 |
| `Opacity` → 透明度字段 | 否 | 逐行透明度。 |

```json
{ "category": "Jan", "value": 120 }                        // 普通柱子
{ "category": "Jan", "series": "North", "value": 120 }     // 堆叠分段
```

**水平**柱状图把数值放在 `X`、类目放在 `Y`（`Encode(Channel.X, "value")`、
`Encode(Channel.Y, "category")`）——方向只交换坐标轴，不会替你交换字段。

**属性：** 每个属性的默认值只列一次，见 [IntervalMark](api-reference.cn.md#intervalmark)。上面的示例用到了柱体几何（`BarPadding`、`CornerRadius`）、标签（`ShowLabel` / `ShowValue`）与布局（`Stack` / `Orientation`）。

**基本柱状图：**

```csharp compile
var data = new List<DataRow>
{
    new DataRow().Set("month", "A").Set("value", 10).Set("type", "s1"),
    new DataRow().Set("month", "B").Set("value", 20).Set("type", "s1"),
};
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

```csharp compile
var data = new List<DataRow>
{
    new DataRow().Set("value", 10).Set("month", "A"),
    new DataRow().Set("value", 20).Set("month", "B"),
};
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

```csharp compile
var combined = new List<DataRow>
{
    new DataRow().Set("month", "A").Set("value", 10).Set("type", "s1"),
    new DataRow().Set("month", "B").Set("value", 20).Set("type", "s1"),
};
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

**归一化堆叠：** 将 `Stack` 设为 `StackMode.Normalize` 即可将 Y 轴归一化到 0–1（轴上显示小数，每个类别之和为 1）。**可见**总量 <= 0 的类目按 0 绘制并告警（隐藏系列不计入总量）；整组全为负值时，堆叠轴的下限会落到负值。

**分组柱状（Grouped Bars）：** 把 `GroupedBars` 设为 `true`（检查器里是 `ChartView.GroupedBars`），同一类目的多个系列就会并排显示而不是互相重叠——子带按颜色字段的系列划分；`Stack` 打开时该开关无效。

---

### 折线图 — LineMark

连续数据趋势可视化，支持平滑曲线、面积填充和阶梯线。

**数据：** 一行 = 一个顶点；同一系列（`Color` 分组）的所有行连成一条线。

| 通道 / 字段 | 是否必需 | 含义 |
|-------------|----------|------|
| `X` → 位置字段 | 是 | 任意刻度（类目、数值或时间）。 |
| `Y` → 数值字段 | 是 | 数值。缺字段的行会被跳过，线在缺口处直接相连。 |
| `Color` → 系列字段 | 否 | 每个不同取值一条线；不设置时所有行连成同一条线。 |

```json
{ "category": "Jan", "series": "North", "value": 120 }
```

点按**行序**连接（不会按 X 排序），有效点少于两个的系列完全不绘制。`Smooth` 默认开启（曲线）；要直线段请设
`Smooth = false`。`ShowArea`（面积图）向下填充到数据零线，需要后端支持渐变。

**属性：** 带默认值的完整列表见 [LineMark](api-reference.cn.md#linemark)。上面的示例用到 `StrokeWidth` 与 `Smooth`，填充用 `ShowArea` / `AreaOpacity`，阶梯线用 `Step`，堆叠面积用 `Stack`（`StackMode.Normalize` 遵循与柱状图相同的零总量规则）；`YChannel` 把线指到右轴——见[双 Y 轴](advanced.cn.md#双-y-轴)。

**多系列折线：**

```csharp compile
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

```csharp compile
var data = new List<DataRow>
{
    new DataRow().Set("month", "A").Set("value", 10),
    new DataRow().Set("month", "B").Set("value", 20),
};
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

```csharp compile
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

```csharp compile
var data = new List<DataRow>
{
    new DataRow().Set("month", "A").Set("value", 10).Set("series", "s1"),
    new DataRow().Set("month", "B").Set("value", 20).Set("series", "s1"),
};
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

**数据：** 一行 = 一个点（气泡图另加尺寸通道）。

| 通道 / 字段 | 是否必需 | 含义 |
|-------------|----------|------|
| `X` / `Y` → 位置字段 | 是 | 数值或类目；缺少任一字段的行会被跳过。 |
| `Size` → 尺寸字段 | 否 | 气泡大小。需要在**图表级**编码（`Encode(Channel.Size, "size")`）；半径线性映射到 3-23 px。缺少该字段的行回退到 `DefaultRadius`。 |
| `Color` → 颜色字段 | 否 | 点的颜色与图例。 |
| `Opacity` → 透明度字段 | 否 | 存在时替换默认的 0.8 透明度。 |
| `Shape` → 形状字段 | 否 | 点的符号：图表形状标度的类目按出现顺序取内置词汇（圆、方、三角、菱形、十字、五角星）；缺值或未知类目画圆形。 |

```json
{ "category": "A", "value": 12, "size": 30 }
```

**属性：** 带默认值的完整列表见 [PointMark](api-reference.cn.md#pointmark)。`DefaultRadius` 是行里没有 `Size` 值时圆点的半径；气泡的其它一切都来自图表级的 `Size` 编码（`SizeRange` / `SizeField`）。

**气泡图：**

```csharp compile
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

**数据：** 一行 = 一个周期，需带完整的 OHLC 四元组。

| 通道 / 字段 | 是否必需 | 含义 |
|-------------|----------|------|
| `X` → 周期字段 | 是 | 类目（序数刻度）。 |
| `open` `high` `low` `close` | 是 | 按名读取（`OpenField` … `CloseField`）。四个字段缺任一个的行会被跳过——没有只取收盘价的回退逻辑。 |
| `Y` → 数值字段 | 否 | 只用于自动适配刻度；坐标轴范围来自 low/high。 |
| `Color` → 颜色字段 | 否 | 替换蜡烛体与影线的涨/跌色；不编码时仍用涨/跌色。 |

```json
{ "date": "2024-01-02", "open": 102, "high": 110, "low": 99, "close": 107 }
```

`close >= open` 视为阳线，因此十字星（`open == close`）使用阳线颜色、实体高度 1 px。

**属性：** 带默认值的完整列表见 [CandlestickMark](api-reference.cn.md#candlestickmark)。行里用别的键名时要改的就是那四个字段名（`OpenField` … `CloseField`）；`BodyWidthRatio`、`WickWidth` 与 `CornerRadius` 决定蜡烛形状，`BullishColor` / `BearishColor` 覆盖涨/跌色。

```csharp compile
new Chart(canvas)
    .Data(stockData)
    .Mark(new CandlestickMark
    {
        OpenField = "open",
        HighField = "high",
        LowField = "low",
        CloseField = "close",
        BodyWidthRatio = 0.6f,
        WickWidth = 1.5f,
    })
    .Encode(Channel.X, "date")
    .Render();
```

---

### 箱线图 — BoxMark

统计分布的五数概括（Min、Q1、Median、Q3、Max）。

**数据：** 一行 = 一组已经算好的五数概括（该 Mark 不做统计）。

| 通道 / 字段 | 是否必需 | 含义 |
|-------------|----------|------|
| `X` → 类目字段 | 是 | 类目（序数刻度）。 |
| `min` `q1` `median` `q3` `max` | 是 | 按名读取（`MinField` … `MaxField`）；缺任一个、或含非有限数值的行都会消失。 |
| `Y` → 数值字段 | 否 | 不参与几何绘制；Y 轴范围由该 Mark 自己贡献（这样坐标轴不会被压成零范围）。 |

```json
{ "stat": "Attack", "min": 12, "q1": 25, "median": 34, "q3": 41, "max": 55 }
```

**属性：** 带默认值的完整列表见 [BoxMark](api-reference.cn.md#boxmark)。要改的逐行键名就是那五个字段名（`MinField` … `MaxField`）；`BoxWidthRatio` 与 `CornerRadius` 决定箱体形状。

```csharp compile
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

**数据：** 一行 = 一个样本；X 类目相同的行汇成一把小提琴。

| 通道 / 字段 | 是否必需 | 含义 |
|-------------|----------|------|
| `X` → 分组字段 | 是 | 类目（序数刻度）；它把样本分组。 |
| `Y` → 数值字段 | 是 | 样本数值；非有限值会被丢弃。 |
| `Color` → 颜色字段 | 否 | 只给分组上色（不会把一组拆成多把小提琴）。 |

```json
{ "weapon": "Sword", "damage": 13 }
{ "weapon": "Sword", "damage": 21 }
{ "weapon": "Sword", "damage": 17 }        // 一把小提琴 = 多行相同的 X
```

一组至少需要两个不同的样本：单行、或取值全部相同的一组没有密度轮廓，会画一条最小可见的线并告警一次。
其余分组的轮廓是对样本做高斯核密度估计后在 `BinCount` 个网格点上采样得到的，因此一组只有少量样本时依然是
连续的小提琴形状（数值轴也会为密度尾部留出余量）。中位数画成一个圆点，颜色与半径取自
`ChartTheme.ViolinMedianDotColor` / `ChartTheme.ViolinMedianDotRadius`。

**属性：** 带默认值的完整列表见 [ViolinMark](api-reference.cn.md#violinmark)。`BinCount` 是密度轮廓的分辨率（最少 8），`WidthRatio` 是小提琴占其槽位的比例，`FillOpacity` 是填充，`ShowMedian` / `ShowBox` 控制内嵌的概括图形。

```csharp compile
new Chart(canvas)
    .Data(violinData)
    .Mark(new ViolinMark { BinCount = 20, ShowMedian = true, ShowBox = true })
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

**数据：** 一行 = 一个矩阵单元格。

| 通道 / 字段 | 是否必需 | 含义 |
|-------------|----------|------|
| `X` → 列字段 | 是 | 列类目。必须是序数刻度：数值型列会被自动适配为线性刻度，于是什么都不画。 |
| `Y` → 行字段 | 是 | 行类目（序数）。行跟随 Y 轴刻度（自下而上）：第一个类目在底部，紧挨它的轴标签。 |
| `Color` → 数值字段 | 是 | 单元格数值；该 Mark 会把颜色通道换成连续色阶，并对超出范围的值做截断。 |

```json
{ "x": "Mon", "y": "09:00", "value": 12.5 }
```

重复的 `(x, y)` 行按顺序覆盖绘制（后画的胜出），不会相加。

**属性：** 带默认值的完整列表见 [HeatmapMark](api-reference.cn.md#heatmapmark)。`CellGap` 与 `CornerRadius` 决定格子形状，`ShowLabel` 在格子里打印数值。

```csharp compile
new Chart(canvas)
    .Data(heatmapData)
    .Mark(new HeatmapMark { CellGap = 2f, CornerRadius = 3f, ShowLabel = true })
    .Encode(Channel.X, "x")
    .Encode(Channel.Y, "y")
    .Encode(Channel.Color, "value")
    .Render();
```

**发散色阶热力图：**

```csharp compile
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

**数据：** 一行 = 单条带（上下界）的一个分段。

| 通道 / 字段 | 是否必需 | 含义 |
|-------------|----------|------|
| `X` → 位置字段 | 是 | 任意刻度。 |
| `Y` → 上界 | 是 | 数值；要求该通道使用数值（`LinearScale`）刻度。 |
| `lower` | 是 | 下界，按名读取（`LowerField`）。该 Mark 始终只画**一条**带：`Color` 只给它上色（取第一行的值）。 |

```json
{ "category": "Jan", "value": 30, "lower": 18 }
```

上下两边都参与 Y 轴的自动适配——Mark 把 `Y` 通道的值与 `lower` 一起交给坐标轴，因此整条带子都落在绘图区内。此处
忽略 `Opacity`——请用 `FillOpacity`。

**属性：** 带默认值的完整列表见 [RangeAreaMark](api-reference.cn.md#rangeareamark)。`LowerField` 改的是下界键名，`FillOpacity` 设置带的填充（该 Mark 不读 `Opacity` 通道），`ShowBorderLines` / `StrokeWidth` / `Smooth` 控制边界样式。

```csharp compile
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

**数据：** 一行 = 某条类目线上的一个区间。

| 通道 / 字段 | 是否必需 | 含义 |
|-------------|----------|------|
| 类目字段 | 是 | 把它编码到 `Y`（或用序数刻度编码到 `X`）：该 Mark 取其中处于序数刻度的通道作为类目，并放到竖直轴上。 |
| `start` `end` | 是 | 按名读取（`StartField` / `EndField`）；即水平方向的范围。`end < start` 的行会把两端交换后绘制并告警一次；`end == start` 的区间不画出任何柱子。 |
| `Y`/`X` 数值字段 | 是 | 非类目的那个通道承载数值刻度上的范围（该刻度由 Mark 贡献）。 |
| `Color` → 系列字段 | 否 | 条的颜色与系列可见性。 |

```json
{ "buff": "Haste", "start": 2, "end": 6 }
```

类目相同的行会互相重叠——该 Mark 不会把它们并排摆开。注意坐标轴是刻意交叉的（类目竖直、范围水平），因此字段形态是
`Encode(Channel.Y, "buff")` 加一个数值范围通道——这也正是 `ChartView` 在 `Kind = Timeline` 时绑定的形式
（`Y` ← 类目，`X` ← 数值）。

**属性：** 带默认值的完整列表见 [TimelineMark](api-reference.cn.md#timelinemark)。`StartField` / `EndField` 改范围键名，`BarHeightRatio` 是条在其泳道中的高度占比，`CornerRadius` 是条的圆角。

```csharp compile
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

### 里程碑图 — MilestoneMark

事件时间轴：每一行是一个落在位置轴上的**单一事件**，画成一个标记加它的标签，可选地按类目排成泳道。

**数据：** 一行 = 一个事件。

| 通道 / 字段 | 是否必需 | 含义 |
|-------------|----------|------|
| `X` → 位置字段 | 是 | 事件在轴上的位置——任意刻度，真实日期用 `TimeScale`。位置缺失或非有限的行会被跳过。 |
| `Y` → 泳道字段 | 否 | 可选泳道（`OrdinalScale`）。不设置时所有事件都在绘图区中线上；设置后每个类目一条线，第一类目在下方（与 `TimelineMark` 的序数方向一致）。 |
| `Label` → 文本字段 | 否 | 事件文本。编码了它时优先于 `LabelField`。 |
| `label` | 否 | 按名读取的事件文本（`LabelField`，默认 `"label"`）；仅在未编码 `Label` 通道时使用。 |
| `Color` → 颜色字段 | 否 | 标记颜色（以及图例）。 |
| `Shape` → 形状字段 | 否 | 标记符号，与散点图同一套词汇。`MarkerShape` 仅在通道**未编码**时生效；编码之后，取值未知——包括缺值——的行回落到 `Shapes[0]`（圆）。 |

```json
{ "week": 2, "lane": "Release", "label": "v0.9" }
{ "week": 7, "lane": "Release", "label": "v1.0" }
{ "week": 3, "lane": "Infra",   "label": "CI" }
```

标签文本依次取 `Label` 通道、`LabelField`、X 值的渲染文本——所以没有标签的行也能看出它落在哪。事件**没有数值字段**：
`ChartView` 在 `Kind = Milestone` 时**不绑**数值通道（`X` ← `XField` 或 `"time"`，`Y` ← `YField` 或 `"lane"`，
仅在行里确实带颜色字段时 `Color` ← 该字段或 `"series"`）。每条泳道只画**一条**水平线（不是每个事件一条）；没有泳道时
一条线穿过中线。标签在标记上下交替（`AlternateLabels`），相邻事件就不会挤在一起；入场动画是标记从线向外生长。
该 Mark 保留坐标轴（`UsesAxes`），所以里程碑时间轴是相对它共用的 X 轴来读的。位置轴不必是数值轴：给 `X` 用序数刻度时，
事件会落在各自类目的槽位上（阶段/发布列车式时间轴），`LogScale` 同理。

**属性：** 带默认值的完整列表见 [MilestoneMark](api-reference.cn.md#milestonemark)。`LabelField` 改事件文本键名，`MarkerRadius` / `MarkerShape` 决定标记的大小与形状（形状只在 `Shape` 通道未编码时生效），`ShowAxisLine` / `AxisLineWidth` 控制泳道线，`LabelOffset` / `AlternateLabels` / `ShowLabel` 控制标签。

真实日期轴是 `Channel.X` 上的 `TimeScale`。`ChartView` 不暴露标度配置，所以要在代码里设置——并注意
`.Scale(...)` 传入的标度是按原样使用的、**不会**被自动适配，因此 `TimeScale` 必须给它范围（或先 `Fit`）：

```csharp compile
new Chart(canvas)
    .Data(releases)
    .Mark(new MilestoneMark
    {
        LabelField = "label",
        MarkerRadius = 6f,
        MarkerShape = ShapeKind.Circle,
        AlternateLabels = true,
    })
    .Encode(Channel.X, "time")
    .Encode(Channel.Y, "lane")
    .Encode(Channel.Color, "lane")
    .Scale(Channel.X, new TimeScale(new DateTime(2024, 1, 1), new DateTime(2024, 12, 31)))  // ← real date axis
    .XAxis(new AxisConfig { Title = "Date" })
    .YAxis(new AxisConfig { Title = "Stream" })
    .Render();
```

`TimeScale` 把取值读作 `DateTime`、`DateTimeOffset`、.NET ticks（任意整型）、Unix 毫秒时间戳（浮点值）或
不变文化下的日期字符串；其它取值会抛异常——非日期字符串请改用数值或序数刻度。

---

### 棒棒糖图 — LollipopMark

圆点 + 线段组合的柱状图变体。

**数据：** 一行 = 一根线段加上它的圆点。

| 通道 / 字段 | 是否必需 | 含义 |
|-------------|----------|------|
| `X` → 类目字段 | 是 | 类目（序数刻度），用于竖直方向的棒棒糖图。 |
| `Y` → 数值字段 | 是 | 数值。即使每种方向只用到其中一个，每行仍需**两个**字段都有。 |
| `Color` / `Opacity` | 否 | 逐行的颜色与透明度（不分组）。 |
| `Shape` → 形状字段 | 否 | 圆点的符号（与散点图同一套词汇）；缺值或未知类目画圆形。 |

```json
{ "category": "A", "value": 12 }
```

**属性：** 带默认值的完整列表见 [LollipopMark](api-reference.cn.md#lollipopmark)。`DotRadius` 与 `StemWidth` 决定两个部分的大小，`Orientation` 翻转图表方向。

```csharp compile
var data = new List<DataRow>
{
    new DataRow().Set("month", "A").Set("value", 10),
    new DataRow().Set("month", "B").Set("value", 20),
};
new Chart(canvas)
    .Data(data)
    .Mark(new LollipopMark { DotRadius = 5f, StemWidth = 2f })
    .Encode(Channel.X, "month")
    .Encode(Channel.Y, "value")
    .Encode(Channel.Color, "constant:#ff8800")
    .XAxis(new AxisConfig { Title = "Month" })
    .YAxis(new AxisConfig { Title = "Revenue", Unit = "USD" })
    .Render();
```

> **提示：** `"constant:"` 前缀为编码绑定一个常量值而非数据字段；常量 **Color** 通道会把整个 Mark 画成该颜色
> —— `Encode(Channel.Color, Colors.Orange)`（类型安全的重载）或它的字符串写法 `"constant:#ff8800"`。
> **不是颜色**的常量（例如 `"constant:Revenue"` 这种名字）不会被当作颜色解析，Mark 会保持默认颜色；
> 颜色**字段**的值本身就是颜色（`Color` 值或 `"#rrggbb"` 字符串）时，按原样使用。
> 需要按索引从代码上色时，仍然用 `StyleOverride`，例如 `(row, i, style) => style.WithFill(color)`。

---

## 极坐标系 (Polar)

### 饼图 / 圆环图 — PieMark

分类占比可视化。设置 `InnerRadius > 0` 变为圆环（甜甜圈）图。

**数据：** 一行 = 一个扇区（`InnerRadius > 0` 时是圆环的一段）。

| 通道 / 字段 | 是否必需 | 含义 |
|-------------|----------|------|
| `Y` → 数值字段 | 是 | 扇区大小数值；非有限值的行会被丢弃，总和不为正时整张图都会被跳过。 |
| `X` → 标签字段 | 否 | 扇区标签；没有它时直接打印数值。 |
| `Color` → 颜色字段 | 否 | 扇区颜色与图例。不绑它时每个扇区都用主题的单一默认色，所以通常绑到类目上。 |

```json
{ "category": "A", "amount": 40 }
```

负值**没有**扇区：负值行会被跳过并告警（此前是反向扫出）。总量必须为正，但占 100% 的单个扇区仍然绘制——
它的扫角会钳在整圈之下一点点。请保证扇区数值 >= 0。

**属性：** 带默认值的完整列表见 [PieMark](api-reference.cn.md#piemark)。`InnerRadius` 把饼图变成圆环，`StartAngle` / `LabelDistance` / `RadiusFactor` 决定几何（也就决定了命中半径：标签环不可命中，入场动画还没揭示的扇区也不可命中），`CenterText`（以及 `CenterFontSize` / `CenterSubFontSize` / `CenterContentBuilder`）填充圆环中心，`ExplodeRatio` 是悬停弹出偏移（null = `ChartTheme.PieExplodeRatio`）。

**饼图：**

```csharp compile
var data = new List<DataRow>
{
    new DataRow().Set("type", "A").Set("value", 10),
    new DataRow().Set("type", "B").Set("value", 20),
};
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

```csharp compile
var data = new List<DataRow>
{
    new DataRow().Set("category", "A").Set("value", 10),
    new DataRow().Set("category", "B").Set("value", 20),
};
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

**数据：** 一行 = 一个顶点：某系列在某个维度上的取值。

| 通道 / 字段 | 是否必需 | 含义 |
|-------------|----------|------|
| `X` → 维度字段 | 是 | 轴/维度名。维度列表是所有 X 取值的并集，因此各系列必须使用相同的名字。 |
| `Y` → 数值字段 | 是 | 半径数值。某系列没有提供的维度会画在中心（0）。 |
| `Color` → 系列字段 | 否 | 每个不同取值一个多边形。 |

```json
{ "dim": "Speed", "value": 80, "team": "A" }
```

维度少于三个，或 Y 通道不在线性刻度上，都不会绘制任何内容。

**属性：** 带默认值的完整列表见 [RadarMark](api-reference.cn.md#radarmark)。`GridRings` 是环数，`FillOpacity` / `StrokeWidth` / `PointRadius` 决定多边形，`ShowAxisLabels` 控制维度标签。

```csharp compile
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

**数据：** 该 Mark 只画**第一行**——多余的行会被忽略。

| 通道 / 字段 | 是否必需 | 含义 |
|-------------|----------|------|
| `Y` → 数值字段 | 是 | 指针值，需在线性刻度上。 |
| `X` → 标签字段 | 否 | 该 Mark 不使用。 |

```json
{ "label": "HP", "value": 75 }
```

圆弧表示数值落在 **Y 刻度内**的位置，所以请显式给刻度设定范围
（`.Scale(Channel.Y, new LinearScale(0, 100))`）；否则自动适配出的域（单个值 75 会得到 0..80）会让指针看起来几乎满了。

**属性：** 带默认值的完整列表见 [GaugeMark](api-reference.cn.md#gaugemark)。弧的几何是 `ArcWidth` / `StartAngleDeg` / `EndAngleDeg`，颜色是 `ValueColor` / `TrackColor`，`ShowCenterLabel` 开关中心的数值。

```csharp compile
new Chart(canvas)
    .Data(new List<DataRow> { new DataRow().Set("label", "HP").Set("value", 75) })
    .Mark(new GaugeMark
    {
        ArcWidth = 0.12f,
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

**数据：** 一行 = 一层，按**行序**自上而下绘制（不做排序）。

| 通道 / 字段 | 是否必需 | 含义 |
|-------------|----------|------|
| `Y` → 数值字段 | 是 | 层的大小；最宽的一层是剩余取值中最大的那个。 |
| `X` → 标签字段 | 否 | 层标签。 |
| `Color` → 颜色字段 | 否 | 逐层的颜色。 |

```json
{ "stage": "Visit", "count": 10000 }
{ "stage": "Signup", "count": 6000 }
```

宽度是插值出来的（`MinWidthRatio` 是下限），不与面积成比例；层高按行数均分——所以形状由行序决定，而不是由数值决定。每层都是矩形（没有梯形收边），入场动画只缩放它的宽度。

**属性：** 带默认值的完整列表见 [FunnelMark](api-reference.cn.md#funnelmark)。`StageGap` 分隔各层，`MinWidthRatio` 是最窄层的宽度下限，`CornerRadius` / `ShowLabel` 决定条带的形状与标签。

```csharp compile
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

用矩形面积表示数值。行可以通过 `ParentField` 指明自己的父节点，从而把扁平数据连成树（与
`SunburstMark` 同一套约定）：分组占据一个矩形，顶部留出标签条，子节点再递归瓜分剩余区域。没有父
字段的行保持单层布局，扁平数据与过去完全一致。

**数据：** 一行 = 一个节点矩形；`parent` 把各行连成树。

| 通道 / 字段 | 是否必需 | 含义 |
|-------------|----------|------|
| `X` → 标签字段 | 是 | 节点标签。 |
| `Y` → 数值字段 | 是 | 节点数值（截断到 >= 0）。自身值不为正的分组取子节点之和。 |
| `parent` | 否 | 父节点键，按名读取（`ParentField`）。父为空或未知即为顶层；完全没有父值时就保持单层。 |
| `Color` → 颜色字段 | 否 | 格子颜色。不绑它时，顶层格子各取一个色板颜色（按行序），同一分组的格子共用分组色并按 `SiblingShadeStep` 变暗。 |

```json
{ "label": "North", "value": 0,   "parent": "" }
{ "label": "Q1",    "value": 30,  "parent": "North" }
{ "label": "Q2",    "value": 45,  "parent": "North" }
```

分组会在顶部预留 `GroupHeaderHeight` 像素用于标签，子节点再瓜分剩余区域。处于父子环中的行会被提升到顶层（同时
记录一条警告），因此不会有行从图中消失。

**属性：** 带默认值的完整列表见 [TreemapMark](api-reference.cn.md#treemapmark)。`ParentField` 指定父键，`LayoutMode` 选择铺排算法（`BinarySplit` / `Squarify`），`CellGap` / `CornerRadius` 决定格子形状，`GroupHeaderHeight` 预留分组标签条，`SiblingShadeStep` 控制同级明暗。

```csharp compile
// 扁平数据：单层，一行一个矩形。
new Chart(canvas)
    .Data(treemapData)
    .Mark(new TreemapMark { ShowLabel = true, CellGap = 3f, CornerRadius = 4f })
    .Encode(Channel.X, "category")
    .Encode(Channel.Y, "amount")
    .Encode(Channel.Color, "category")
    .Render();

// 层级数据：同样的行加上父字段即可成为树。
new Chart(canvas)
    .Data(diskUsage)   // folder / parent / size
    .Mark(new TreemapMark { ParentField = "parent", LayoutMode = TreemapLayoutMode.Squarify })
    .Encode(Channel.X, "folder")
    .Encode(Channel.Y, "size")
    .Render();
```

分组自身值不为正时取子节点之和（自身值为正则用该值）。父节点未知、指向自身或处于父子环中的行会被提升到
顶层，不会从图中消失。

---

### 旭日图 — SunburstMark

层级饼图，从内到外展开层级结构。

**数据：** 一行 = 一个环上的分段；`parent` 把各行连成树。

| 通道 / 字段 | 是否必需 | 含义 |
|-------------|----------|------|
| `X` → 标签字段 | 是 | 节点标签。 |
| `Y` → 数值字段 | 是 | 节点数值（截断到 >= 0）；自身数值不为正的分组取其子节点之和。 |
| `parent` | 否 | 父节点标签，按名读取（`ParentField`）。父为空或未知即为根节点。 |
| `Color` → 颜色字段 | 否 | 弧的颜色。不绑它时由某一环承载分支、每个分支取一个色板颜色，更深环沿用该色并按深度变暗。有多个根时根就是分支；**只有一个**根时它只是框架（常见的 `total` 行），分支是它的子节点，根保持中性的 Mark 默认色。 |

```json
{ "label": "Asia",  "value": 0,   "parent": "" }
{ "label": "China", "value": 120, "parent": "Asia" }
```

同一父节点下标签相同的行会互相覆盖，因此请保证同一父节点内标签唯一。断裂的父子链会被剪断，受影响的节点会被提升为
根节点（同时记录一条警告）。

**属性：** 带默认值的完整列表见 [SunburstMark](api-reference.cn.md#sunburstmark)。`ParentField` 指定父键，`RadiusFactor` / `InnerRadiusRatio` / `RingGap` 决定几何，`DepthShadeStep` 决定更深的环变暗多少（0 = 每个分支一个纯色）。

```csharp compile
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

**数据：** 一行 = 两个节点之间的一条流。

| 通道 / 字段 | 是否必需 | 含义 |
|-------------|----------|------|
| `source` `target` | 是 | 节点名，按名读取（`SourceField` / `TargetField`）。缺任一个的行会被丢弃。 |
| `Y` → 权重字段 | 否 | 流的权重（字段缺失时为 1）。非正的流会被丢弃，因此零流量的分支画不出来。 |
| `Color` → 颜色字段 | 否 | 流的颜色。不绑它时每条流取其**源节点**的颜色（每个节点一个色板颜色，按首次出现顺序）；节点条仍用主题的 `SankeyNodeColor`。 |

```json
{ "source": "Landing", "target": "Signup", "value": 40 }
```

节点按拓扑序摆放。出现环会记录一条警告：布局会沿着环走一遍、丢掉闭合它的那条回边，于是环上的每个节点都有
各自的列号，不再全部堆到第一列。自环会被静默丢弃。节点高度取其入流与出流总量中**较大**的那个；节点标签是
居中文本，放在节点旁边（最后一列的节点放在其左侧）。

**属性：** 带默认值的完整列表见 [SankeyMark](api-reference.cn.md#sankeymark)。`SourceField` / `TargetField` 改流的键名，`NodeWidth` / `NodeGap` 决定节点尺寸，`ColumnGap` 决定列的紧凑度（0 = 铺开，1 = 紧贴）。

```csharp compile
var sankeyData = new List<DataRow>
{
    new DataRow().Set("source", "Mine").Set("target", "Furnace").Set("amount", 100),
    new DataRow().Set("source", "Furnace").Set("target", "Workshop").Set("amount", 60),
    new DataRow().Set("source", "Furnace").Set("target", "Market").Set("amount", 40),
};

new Chart(canvas)
    .Data(sankeyData)
    .Mark(new SankeyMark { ShowLabel = true, NodeWidth = 16f })
    .Encode(Channel.Y, "amount")
    .Encode(Channel.Color, "source")
    .Render();
```

---

### 弦图 — ChordMark

环形关系图：展示实体间关系强度。

**数据：** 一行 = 一条弦（两个节点之间的一段关系）。

| 通道 / 字段 | 是否必需 | 含义 |
|-------------|----------|------|
| `source` `target` | 是 | 节点名，按名读取（`SourceField` / `TargetField`）；缺字段的行会被丢弃。 |
| `Y` → 权重字段 | 否 | 弦的权重（字段缺失时为 1）。 |
| `Color` → 颜色字段 | 否 | 弦的颜色。不绑它时每个节点弧取一个色板颜色，每条弦取它**离开**的那个节点的颜色。 |

```json
{ "source": "Client", "target": "Backend", "value": 5 }
```

节点按首次出现的顺序顺时针排布，某个节点的角覆盖其入弦与出弦的**总和**（不同于桑基图的取最大）。自环不会被
过滤，会被计两次。半径按绘图区较短的一边计算，因此非正方形的面板会让图变小。

**属性：** 带默认值的完整列表见 [ChordMark](api-reference.cn.md#chordmark)。`SourceField` / `TargetField` 改弦的键名，`ArcWidthRatio` 是弧的粗细，`ArcGap` 是弧间距。

```csharp compile
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

**数据：** 一行 = 一个类目，其数值是它在固定网格中占的份额。

| 通道 / 字段 | 是否必需 | 含义 |
|-------------|----------|------|
| `Y` → 权重字段 | 是 | 占比权重。取值 <= 0（或无值）的行会被丢弃。 |
| `X` → 提示标签字段 | 否 | 只用于悬停提示文本。 |
| `Color` → 颜色字段 | 否 | 每个类目的格子颜色。 |

```json
{ "category": "A", "value": 30 }
{ "category": "B", "value": 10 }
```

`TotalCells` 个格子（默认 100）按最大余数法分配，所以一行**不是**一个格子。计数从左下角开始。网格本身不使用任何刻度，
该 Mark 也声明为「不用坐标轴」（`UsesAxes`），因此只放华夫饼图的图表不会绘制坐标轴与网格；与柱状图等混用时才会保留。

**属性：** 带默认值的完整列表见 [WaffleMark](api-reference.cn.md#wafflemark)。`TotalCells` 是网格的格子数（按最大余数法分配），`Columns` 是网格形状；`CellGap` / `CornerRadius` 控制格子样式。

```csharp compile
var data = new List<DataRow>
{
    new DataRow().Set("type", "A").Set("value", 10),
    new DataRow().Set("type", "B").Set("value", 20),
};
new Chart(canvas)
    .Data(data)
    .Mark(new WaffleMark { TotalCells = 100, Columns = 10, CellGap = 2f, CornerRadius = 2f })
    .Encode(Channel.X, "type")
    .Encode(Channel.Y, "value")
    .Encode(Channel.Color, "type")
    .Render();
```

---

## 直方图 — 数据变换

直方图不是独立的 Mark：它是 `IntervalMark` 前面加一道 `BinTransform`，于是每个箱各自成为一根柱子。完整做法——
变换调用、`BinStart` / `BinEnd` / `BinMid` / `Count` 输出字段，以及柱状图在类目轴上需要的 `OrdinalScale`——见
[高级功能 → 数据变换](advanced.cn.md#数据变换-transform)；这里不再重复。

---

## 复合图表 — 多 Mark 叠加

同一张图里放多个 Mark，各自有自己的编码、颜色和标度（例如柱状图加一条右轴折线）。各种做法、坐标系规则与坑都汇集在
[高级功能 → 复合图表](advanced.cn.md#复合图表)。
