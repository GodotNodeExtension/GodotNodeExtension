[英文](customization.md) | **中文**

# 定制与主题

本文档介绍如何对 GodotChart 进行视觉定制，包括主题系统、标度配置、坐标轴和图例、以及自定义渲染器。

---

## 主题系统 — ChartTheme

`ChartTheme` 是一个 Godot `Resource`，所有视觉属性均标记 `[Export]`，可以在 Inspector 中可视化编辑。

### 使用内置主题

```csharp compile
var data = new List<DataRow>
{
    new DataRow().Set("category", "A").Set("value", 10),
    new DataRow().Set("category", "B").Set("value", 20),
};
// Dark theme (default)
var darkTheme = ChartTheme.Dark();

// Light theme
var lightTheme = ChartTheme.Light();

new Chart(canvas)
    .Theme(lightTheme)
    .Data(data)
    .Mark(new IntervalMark())
    .Encode(Channel.X, "category")
    .Encode(Channel.Y, "value")
    .Render();
```

### 克隆与自定义

```csharp compile
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

ChartTheme 有 107 个 `[Export]` 属性，分在 25 个 `[ExportGroup]` 里。以下是主要分组：

#### Color Palette — 色板

| 属性 | 类型 | 说明 |
|------|------|------|
| `Palette` | Color[] | 分类色板（默认 6 色） |
| `SequentialGradient` | Color[] | 连续渐变色阶（5 色） |

这两个数组属性**每个实例各持一份拷贝**（连默认值也是拷贝出来的，所以改一个主题不会污染其它主题）。
两个静态默认色板同样是**按拷贝只读**：每次读取都返回一份新数组，`ChartTheme.DefaultPalette[0] = Colors.Red;`
改的只是那份临时拷贝，什么都不会变（要真改颜色请改主题的 `Palette`）。
注意：**就地改数组元素不会发 `Changed`**——`theme.Palette[0] = Colors.Red;` 只动了数组，节点收不到通知；
请整体重新赋值（`theme.Palette = …`），或自己调 `EmitChanged()`。

#### Chart Frame — 图表框架

| 属性 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `BackgroundColor` | Color | (0.08, 0.08, 0.12) | 背景色 |
| `BackgroundCornerRadius` | float | 8 | 背景圆角 |
| `GridColor` | Color | (1,1,1,0.08) | 网格线颜色 |
| `GridLineWidth` | float | 1 | 网格线宽度 |
| `AxisColor` | Color | (1,1,1,0.4) | 坐标轴颜色 |
| `AxisLineWidth` | float | 2 | 坐标轴线宽 |

这两个框架属性画的是那块**背景矩形**：`ChartView` 的 surface 每次重绘都被清成**透明**，节点本身也没有
背景导出，所以看到的背景正是背景渲染器用 `BackgroundColor` 填充、按 `BackgroundCornerRadius` 收角的矩形。
要透出后面的画面，就让这块矩形透明——`view.ConfigureChart = c => c.BackgroundColor = Colors.Transparent;`，
或把本主题的 `BackgroundColor` alpha 设为 0（该资源对所有引用它的图都生效）。

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

图例文字用主题字体（`ChartTheme.LabelFontSize` / `Family` / `Font`）测量，所以改字号时量宽与行高会一起
变化，不会互相重叠。

#### Polar / Segment — 极坐标

| 属性 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `SegmentBorderColor` | Color | — | 分段边框色 |
| `SegmentBorderWidth` | float | 1 | 边框宽度 |
| `PieExplodeRatio` | float | 0.03 | 饼图弹出比 |

此外还有按所配置 mark 命名的专属样式分组：**Line Mark、Point Mark、Radar Mark、Box Mark、Violin Mark、
Gauge Mark、Sankey Mark、Candlestick Mark、Lollipop Mark、Range Area Mark、Chord Mark、Sunburst Mark、
Treemap Mark**（另有 **Feature Toggles** 与 **Hit Test**）。只有主题里确实带有该 mark 的 token 时才会有对应
分组：Interval、Heatmap、Funnel、Waffle、Milestone、Timeline 直接用上面的共享默认值。上面的表只列了主要属性；
完整清单按这些分组名显示在 Inspector 里。

示例页 `ChartThemeDemo` 按格子逐组走了一遍：两套内置主题、`Clone`、边框、排版、线宽与提示框度量、功能开关、
运行中改主题资源、Chart 级覆盖、**Layout** 预留（标题带与 Y2 列），以及一个按 mark 分的组（小提琴那组，
连同 **Hit Test** 的几个旋钮）。没有自己格子的那些组——其余按 mark 分的组、以及上表里的提示框偏移与图例间距——
配置方式和有格子的一模一样：同一个主题资源、Inspector 里的同一个分组。

---

## 自定义主题资源（ChartView）

`ChartView` 可以直接把一个完整的 `ChartTheme` 当作场景资源使用：做一个 `.tres`、在 Inspector 里改好、
把节点指过去即可——不用写代码，编辑器预览会跟着每次改动更新。

### 在编辑器里操作

1. 在文件系统面板右键 → **New Resource…** → 选 `ChartTheme` → 存成如 `my_theme.tres`。
2. 打开 `my_theme.tres`，在 Inspector 里改颜色、线宽、字号、圆角与调色板——属性按分组排列
   （Color Palette / Chart Frame / Typography / …）。
3. 选中场景里的 `ChartView`，把它的 `CustomTheme` 槽指向该 `.tres`；编辑器预览立即更新。

### 优先级：CustomTheme 优先于 ThemeKind

- `CustomTheme` 非空 → 它就是本图的主题，`ThemeKind` 被忽略。
- `CustomTheme` 为 null（默认）→ 使用 `ThemeKind` 选出的内置配色（`Dark` / `Light`）。

### 不会改写你的资源

渲染管线（`Chart`、各 mark、各 renderer、tooltip）只**读**主题，节点自己不写任何字段，也不会往 `.tres`
里回写——所以同一个主题资源可以同时给任意多个 `ChartView` 用。

因为读的是资源本身，**在 Inspector 里改 `my_theme.tres` 会立刻反映到所有引用它的图上**（`ChartView` 节点与手搭的 `Chart`
都会跟随资源的 `changed` 信号：节点重建，图表丢弃已缓存的布局；只比引用是看不到原地修改的）。

### 只有主题这一处入口

节点上**没有** `Font` / `FontFamily` / 颜色之类的样式导出：字体的入口是 `ChartTheme.Font` /
`ChartTheme.FontFamily`（Typography 分组），颜色、线宽、圆角同理。样式只在一个地方改，避免两套设置打架。

> 注意：`ChartTheme` 带 `[Tool]`。C# 资源类少了它，编辑器里只会建一个占位脚本实例——资源在编辑器里
> 既看不到自己的导出属性、赋给 `CustomTheme` 还会抛 `InvalidCastException`。自己派生主题类时请照样加上。

### 用代码设置

```csharp
var view = new ChartView
{
    Kind = ChartKind.Bar,
    CustomTheme = GD.Load<ChartTheme>("res://my_theme.tres"),
};

// 等价写法：Chart API 直接接受该资源。
var chart = new Chart(canvas).Theme(myTheme);
```

---

## 标度 (Scale)

Scale 负责将原始数据值映射到 `[0, 1]` 标准化范围。大多数情况下会自动推断，但你可以手动指定以获得精确控制。

### LinearScale — 线性标度

```csharp compile
// Auto-fit from data (default behavior)
// Or manually specify range:
chart.Scale(Channel.Y, new LinearScale(0, 100));
```

属性：
- `Min` / `Max` — 数据域范围
- `IncludeZero` — 是否强制包含零点（默认 true）

### OrdinalScale — 分类标度

自动从数据推断分类值，无需手动设置。用于文字类别型的 X 轴。

### LogScale — 对数标度

适合数据跨多个数量级的场景：

```csharp compile
chart.Scale(Channel.Y, new LogScale(1, 10000));
```

### ColorScale — 颜色标度

将分类值映射到色板颜色：

```csharp compile
// Usually auto-inferred from theme palette
chart.Scale(Channel.Color, new ColorScale());
```

颜色由**颜色通道**决定：`ColorField` 指名的字段（留空则退回 `series`）会按主题 `Palette` 建一个分类色标，
取值相同的元素同色、类目多于色板长度时循环取色；图例也由同一个通道驱动。

**饼图 / 环图 / 漏斗 / 华夫饼**在没有指定字段时，`ChartView` 直接把颜色通道绑到类目上：一行就是一个扇形 /
阶段 / 方格，于是每块取下一个色板颜色，图例列出类目。没有颜色通道时这些类型保持 mark 的单一默认色
（`ChartTheme.DefaultMarkColor`）——所以行里没有 `series` 字段的饼图看起来就是一整块同色。

结构类与流类 mark 自带色板默认，不会整块同色：**Sunburst** 每个分支一个色板色（只有单一根时根作框架，
否则整张图会缩成一个颜色），外环沿用该色并按 `DepthShadeStep` 逐环变暗；**Treemap** 每个顶层格子一个色板色，同一分组内按 `SiblingShadeStep` 变暗；
**Sankey** 每条流用它源节点的颜色；**Chord** 每个节点弧取一个色板色、每条弦用它离开的那个节点的颜色。
这几类里显式绑了 `Color` 通道时同样是通道优先。

#### 直接写颜色（identity 色标）

`ColorField` 放的是**字段名，不是颜色**：它指的数据行字典里的某个键，每一行在那一列里的值才是该元素的颜色。
算「颜色」的取值只有两类：

| 取值 | 例子 | 结果 |
|---|---|---|
| Godot 的 `Color` 对象 | `Colors.Teal`、`new Color("#ff8800")` | 原样使用 |
| `#` + 3/4/6/8 位十六进制的字符串 | `"#f00"`、`"#ff8800"`、`"#ff8800cc"` | 解析后原样使用 |

颜色通道的**值全部是颜色**时，`Chart` 按原样使用它们——即 G2 的 *identity* 色标 `IdentityColorScale`——
并且不画图例（这些值是颜色，不是类目）。

**其它一切都还是类目**，走分类色板：数字、名字，以及**故意**包括只是**看起来**像颜色的字符串
（`"red"`、`"add"`——只有 `#` 形式才会被解析）。这样类目名永远不会被意外当成颜色。

这个切换是**整列全有或全无**：必须**所有**值都是颜色，否则整列按类目走（混合列会连带丢掉图例，所以一个
漏网的值不该把整列翻过去）。数据与这个猜测不符时，用 `ColorMapping` 强制指定——见下面的
[选择映射方式](#选择映射方式scales)。

```csharp compile
var rows = new List<DataRow>();
rows.Add(new DataRow().Set("category", "Q1").Set("value", 30).Set("color", "#ff8800"));
rows.Add(new DataRow().Set("category", "Q2").Set("value", 45).Set("color", Colors.Teal));

view.ColorField = "color";              // ChartView：填字段名即可，不用配 scale
chart.Encode(Channel.Color, "color");   // Chart API
```

**整张图一个颜色**：不绑字段而绑常量——`Encode(Channel.Color, Colors.Red)`（类型安全的那个重载），
或 `Encode(Channel.Color, "constant:#ff0000")`；在 `ChartView` 上把 `ColorField` 直接填成
`constant:#ff0000` 也行。

**在代码里按元素上色**：`mark.StyleOverride = (row, index, style) => style.WithFill(color)`；在 `ChartView` 上
通过 `ConfigureMark` 拿到 mark。按索引或按计算规则上色时仍然用这条。

#### 尺寸、透明度与形状

`SizeField` 只对散点/气泡图有效：值经线性标度（按该列的**数据范围** `min`…`max` 拟合，**不含 0**）映射到
`ChartTheme.PointSizeMin`（3 px）到 `PointSizeMin + PointSizeRange`（23 px）之间的半径——最小值画最小的点，
最大值画最大的点。没有尺寸通道时每个点用 `PointMark.DefaultRadius`（5 px），悬停时半径再乘
`PointHoverRadiusRatio`（1.3）。

`OpacityField` 由真正解析逐元素透明度的 mark 读取：柱子、散点、热力图格子、蜡烛、箱体、弦、漏斗层、仪表、
棒棒糖茎、里程碑、饼图扇区、桑基流、旭日环、时间轴条、矩形树图格子、小提琴，以及**华夫饼的格子**。值经线性
标度按该列的 `0`…`max` 拟合，所以最大值完全不透明、`0` 完全透明（`0`…`255` 的 alpha 字节可以直接放，
`0`…`1` 的小数也可以；结果会钳制到 `0`…`1`）。常量——`"constant:0.5"`——直接按原值使用。有三个 mark
刻意不读该通道、改用各自的旋钮：`LineMark` 与 `RadarMark` 按系列应用全局 / 聚焦透明度，`RangeAreaMark`
用 `FillOpacity`（它行里的 `Opacity` 被忽略，只有 `Color` 通道给它那一条带取色）。让非聚焦系列变暗又是另一个
开关：`ChartTheme.UnfocusedOpacity`。

`ShapeField` 与颜色类目列一样是分类的：每个不同取值按 `ShapeScale.DefaultShapes` 的顺序取下一个符号
（圆、方、三角、菱形、叉、星，循环）。它驱动散点符号、棒棒糖图的圆点和图例色块。

这三个通道里，某行没有该字段就保持 mark 默认值（不报错），非有限值也一样——NaN 不会以坏掉半径或透明度
的形式送到画布上。

### 选择映射方式（Scales）

`*Field` 属性配的是**绑定**（哪一列喂给哪个通道）；那些值怎么变成颜色/大小/透明度，靠的是**标度**。
`Chart` 会自动推断标度（颜色列分类、尺寸/透明度/数值列线性），`ColorMapping` 就是场景覆盖这个推断的地方：

| `ColorMapping` | 颜色会变成什么 |
|---|---|
| `Auto`（默认） | 值全是颜色时用值本身，否则用分类色板 |
| `Category` | 一律分类色板：每个不同取值一个颜色，并出图例——即使这一列是颜色 |
| `Identity` | 一律用值本身的颜色——**混合列**想保留其中颜色的办法 |
| `Sequential` | 数值列映射到 `ChartTheme.SequentialGradient`（热力感） |
| `Diverging` | 数值列映射到蓝 → 中性 → 红的双向色阶，以 0 为中心 |

```csharp compile
view.ColorField = "delta";
view.ColorMapping = ColorMappingKind.Diverging;   // -20 … +20 围绕 0
```

导出属性覆盖不了的，走 `ConfigureChart`：它在图表构建完成、开始绘制之前拿到 `Chart` 本体，
和 `ConfigureMark` 对 mark 的作用一样：

```csharp compile
view.ConfigureChart = chart =>
{
    chart.Scale(Channel.Y, new LinearScale(0, 100));   // 锁定坐标轴范围
    chart.Mark(new MilestoneMark());                   // 追加一个 mark
};
```


场景里能直接设的映射：`XAxisRange` / `YAxisRange`（`(0, 0)` = 按数据拟合；类目轴会忽略它，不会把轴弄坏）、
`SizeRange`（`(最小, 最大)` 半径像素）、`OpacityRange` 和 `ShapeSymbols`。双轴不在节点上：`Channel.Y2` 是 API 的能力。

##### 节点到哪为止

节点覆盖「场景配置得出来」的部分，其余交给 `Chart` API（在节点上通过 `ConfigureChart` / `ConfigureMark`
够到）。这条线是刻意的——把 `Chart` 的每个选项都搬到节点上，编辑器体验会比直接用 API 更差。

| `ChartView` 覆盖 | 改用 `Chart` API |
|---|---|
| 一个类型 + 它的通道 + 颜色映射（`ColorMapping`） | 多个 mark 各有各的编码和颜色（柱子 **加** 一条独立配色的折线） |
| 轴范围（`XAxisRange` / `YAxisRange`）、尺寸/透明度范围（`SizeRange` / `OpacityRange`）、符号词汇表（`ShapeSymbols`） | 自定义标度（对数、每个 mark 一个标度）、自定义刻度 |
| — | 第二数值轴（`Channel.Y2`）：所有双轴图都用 API 搭 |
| 主题级样式（`ChartTheme` 资源） | 逐元素样式规则（`StyleOverride`）、自定义 mark、共享画布、自定义 tooltip |

### SequentialColorScale — 连续色阶

将连续数值映射到渐变色：

```csharp compile
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

```csharp compile
chart.Scale(Channel.Color, new DivergingColorScale(-1, 1));
```

### RadialScale — 径向标度

用于极坐标图表中的半径映射。

### TimeScale — 时间标度

将 `DateTime` 值映射到位置，格式会根据时间跨度自动调整。

### BandScale — 分组条带标度

用于分组柱状图的子带布局：

```csharp compile
var band = new BandScale
{
    SubBandCount = 3,       // 3 series per group
    Padding = 0.2f,         // outer padding
    InnerPadding = 0.1f,    // padding between sub-bands
};
```

内置的分组柱状图不走它：`IntervalMark.GroupedBars`（以及 `ChartView.GroupedBars`）自己把类目切成子带。
`BandScale` 与 `RadialScale` 目前只服务**自定义 mark**——没有内置 mark 消费它们。

### ShapeScale

形状标度：把类目映射到符号（`Channel.Shape`）。编码该通道时图表会自动推断出一个，只有需要指定符号或顺序时才需要手动设置：

```csharp compile
chart.Scale(Channel.Shape, new ShapeScale { Shapes = new[] { ShapeKind.Cross, ShapeKind.Star } });
```

`ShapeKind` 提供 `Circle`、`Square`、`Triangle`、`Diamond`、`Cross`、`Star` 六种符号；默认按此顺序取用，
类目多于符号时循环。散点、棒棒糖图的圆点以及图例色块都会画成对应符号（见 `ShapeGeometry.Build`）。

---

## 坐标轴配置 — AxisConfig

```csharp compile
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

---

## 图例配置 — LegendConfig

```csharp compile
chart.Legend(new LegendConfig
{
    Position = LegendPosition.Top,   // Top / Bottom / Left / Right / None
    ItemSpacing = 16f,               // 图例项间距
    SwatchSize = 10f,                // 色块大小
    Padding = 6f,                    // 图例区域内边距
});
```

将 `Position` 设为 `LegendPosition.None` 可隐藏图例。

---

## 布局与尺寸

```csharp compile
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

Chart 提供多个渲染器槽位，允许你用自定义函数替换默认绘制行为。纯极坐标图表（无笛卡尔 Mark）**不会**调用网格、坐标轴、轴标签与十字准线槽位：

```csharp compile
// Custom background
chart.BackgroundRenderer = ctx =>
{
    // ctx provides: Canvas, Plot, Theme, etc.
    using var bgPaint = ctx.Canvas.CreatePaint();
    bgPaint.SetColor(Colors.White);
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

```csharp compile
var data = new List<DataRow>
{
    new DataRow().Set("category", "A").Set("value", 10),
    new DataRow().Set("category", "B").Set("value", 20),
};
new Chart(canvas)
    .Data(data)
    .Mark(new IntervalMark())
    .Mark(new LineMark())
    .Encode(Channel.X, "category")             // the marks need the fields the chart draws from
    .Encode(Channel.Y, "value")
    .ApplyToAllMarks(m => m.ShowLabel = true)  // apply to both marks
    .Render();
```

---

## 颜色直接覆盖

可以直接在 Chart 上设置颜色属性覆盖主题值：

```csharp compile
chart.BackgroundColor = new Color(0.1f, 0.1f, 0.15f);
chart.GridColor = new Color(1f, 1f, 1f, 0.05f);
chart.AxisColor = new Color(1f, 1f, 1f, 0.3f);
```

---

## 系列可见性控制

运行时动态显示/隐藏系列：

```csharp compile
chart.HideSeries("ProductA");          // Hide specific series
chart.ShowSeries("ProductA");          // Show it back
chart.ToggleSeriesVisibility("ProductA"); // Toggle
chart.ShowAllSeries();                 // Show all

bool hidden = chart.IsSeriesHidden("ProductA");
```

隐藏的系列不参与渲染和 HitTest。
