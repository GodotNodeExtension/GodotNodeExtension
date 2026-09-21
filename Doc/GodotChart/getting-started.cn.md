[英文](getting-started.md) | **中文**

# 快速入门

本文档帮助你从零开始创建第一个 GodotChart 图表。

## 前置条件

- 一个启用 C# 支持的 Godot 工程——本组件在 Godot 4.7+ 下开发与测试
- .NET——需要 .NET SDK 10.0+（组件自身源码目标框架 `net10.0`）
- 项目已引用 GodotNodeExtension 和 SkiaSharp

## 核心概念

### DataRow — 数据行

GodotChart 使用 `DataRow` 作为数据单位。一行就是一组命名字段——一个没有 schema 的
`字段名 → 值` 包，所以同一个图表里两行可以带不同的字段：

```csharp compile
var row = new DataRow()
    .Set("month", "Jan")
    .Set("revenue", 1200)
    .Set("cost", 800);

// 读取数据
string month = row.Get<string>("month");  // "Jan"
int revenue = row.Get<int>("revenue");    // 1200
```

`Set(...)` 写入（并覆盖）一个字段，返回该行本身，所以可以链式调用。`Get<T>(field)` 读回字段值：
请求类型不同时会转换，字段缺失时抛 `KeyNotFoundException`；`TryGet<T>()` 不抛异常而是返回
`false`，`Has()` 则只判断字段是否存在。字段名**大小写敏感**（`"Value"` 不等于 `"value"`），
而且名字只是约定——没有任何硬编码，你用 `Chart.Encode(channel, "your field")` 把通道绑定到任意
字段即可。

值可以是任意对象，普通 .NET 类型会保留自身类型，所以 `DataRow` 的类型能一路传到标度层。
如果在检查器里填同一份数据，`Rows` 是 Godot 的键值字典数组，读入时会映射为：`Bool → bool`、
`Int → long`、`Float → double`、`String → string`。

`Has()` 能区分**字段缺失**与**字段存在但值为 `null`**，但大多数 mark 把两者同等对待：拿不到
可用值的行会被跳过，自动推断的标度也一视同仁——因此 `null` 绝不会被当成 0 画出来。

喂数据用 `Chart.Data(rows)`（整体替换数据），或用 `Chart.AppendData(row)` 逐行追加（实时/流式
场景用后者）。

### Channel — 视觉通道

视觉通道决定数据如何映射到图形属性：

| 通道 | 用途 | 示例 |
|------|------|------|
| `Channel.X` | 水平位置 | 月份、类别 |
| `Channel.Y` | 垂直位置（左轴） | 数值、金额 |
| `Channel.Y2` | 垂直位置（右轴） | 复合图表第二轴 |
| `Channel.Color` | 颜色 | 分类着色 |
| `Channel.Size` | 大小 | 气泡图半径 |
| `Channel.Opacity` | 透明度 | 置信度映射 |
| `Channel.Shape` | 形状（符号） | 散点 / 气泡的符号、棒棒糖图的圆点 |
| `Channel.Label` | 文本标签 | 数据点标注 |

### Field Conventions — 字段约定

字段名分两类。

**通道字段**用 `Chart.Encode(channel, "field")` 绑定到视觉通道——`X`、`Y`、`Y2`、`Color`、
`Size`、`Opacity`、`Shape`。（`Label` 仅 `TimelineMark` 与 `MilestoneMark` 使用。）绑定决定哪个字段喂哪个通道，名字任你
取。`ChartView` 会预绑定库内约定：X 通道用 `category`，Y 通道用 `value`，Color 通道用 `series`。

mark 还可以用 `mark.Encode(channel, "field")` 只给自己绑定通道。这条绑定**只对这个 mark 生效**：图表级编码
仍是其它 mark 的回退，于是可以一个 mark 用自己的字段、其余 mark 继续用图表的字段。刻度仍然每个通道一个
——它会同时从两边的字段拟合——两个字段不是同一种值时请显式加 `Chart.Scale(...)`。

**固定字段**由 mark 在内部**按名读取**，不走通道，所以只能通过该 mark 上对应的属性改名：

| 字段 | 哪些图表用 | 改名用的属性 |
|------|-----------|-------------|
| `source` / `target` | 桑基图、弦图 | `SourceField` / `TargetField` |
| `start` / `end` | 时间轴 | `StartField` / `EndField` |
| `lower` | 范围面积图 | `LowerField` |
| `open` / `high` / `low` / `close` | K 线图 | `OpenField` / `HighField` / `LowField` / `CloseField` |
| `min` / `q1` / `median` / `q3` / `max` | 箱线图 | `MinField` / `Q1Field` / `MedianField` / `Q3Field` / `MaxField` |
| `parent` | 矩形树图、旭日图 | `ParentField` |
| `label` | 里程碑图 | `LabelField` |
| `lane` | 里程碑图（默认 `Y` 字段） | `YField` |

每种图表「一行代表什么」、读哪些字段的完整对照，见 [图表类型](chart-types.cn.md) 的『数据结构一览』一节。

### Mark — 图形标记

Mark 决定数据如何**可视化**。每种 Mark 对应一类图表：

```csharp compile
new IntervalMark();  // 柱状图
new LineMark();      // 折线图
new PieMark();       // 饼图
new PointMark();     // 散点图
```

### Scale — 标度

Scale 控制数据值到视觉空间的映射方式：

```csharp compile
new LinearScale(0, 100); // 线性映射 [0, 100] → [0, 1]
new LogScale(1, 10000); // 对数映射
new OrdinalScale(); // 分类映射（自动推断）
new ShapeScale(); // 类目 → 符号（Shape 通道自动推断）
new DivergingColorScale(-1,1); // 发散色阶
```

常见情况下 Scale 会**自动推断**：数值字段变为 `LinearScale`，文本字段变为 `OrdinalScale`，Color 通道变为 `ColorScale`，Shape 通道变为 `ShapeScale`（类目按出现顺序依次取内置符号）。`TimeScale`、`LogScale`、`BandScale` 以及连续型色阶（`SequentialColorScale`、`DivergingColorScale`）**不会**被推断，需要用 `.Scale(...)` 显式配置。

## 第一个图表：柱状图

### 步骤 1：准备数据

```csharp compile
var data = new List<DataRow>
{
    new DataRow().Set("month", "Jan").Set("revenue", 1200),
    new DataRow().Set("month", "Feb").Set("revenue", 1800),
    new DataRow().Set("month", "Mar").Set("revenue", 1500),
    new DataRow().Set("month", "Apr").Set("revenue", 2200),
    new DataRow().Set("month", "May").Set("revenue", 1900),
    new DataRow().Set("month", "Jun").Set("revenue", 2600),
};
```

### 步骤 2：构建图表

快捷方式是 `ChartView` 节点：选图表类型、把数据交给它，剩下的（画布、标度、布局、尺寸、重绘）
它自己搞定。在场景里加一个 `ChartView`（或继承它），在 `_Ready` 里配置：

```csharp compile-members
public override void _Ready()
{
    var view = new ChartView { Kind = ChartKind.Bar, Title = "Revenue" };
    view.SetAnchorsPreset(LayoutPreset.FullRect);
    AddChild(view);

    // 数据行是强类型 DataRow：int / float / string / bool 保留各自的类型；
    // 检查器里的 `Rows` 是同一份数据（键值字典数组），可以直接编辑。
    view.SetData(new[]
    {
        new DataRow().Set("month", "Jan").Set("revenue", 1200),
        new DataRow().Set("month", "Feb").Set("revenue", 1800),
        new DataRow().Set("month", "Mar").Set("revenue", 1500),
    });
}
```

通道、坐标轴、图例与指针反馈都在节点上：`XField` / `YField` / `ColorField` / `SizeField` /
`OpacityField` / `ShapeField` 把字段映射到通道（留空用类型默认字段）。这些值怎么变成视觉属性，在绑定旁边配：
`ColorMapping`、`SizeRange`、`OpacityRange`、`ShapeSymbols` 以及轴范围——见
[自定义](customization.cn.md#选择映射方式scales)。`XAxisTitle` / `YAxisTitle` / `YAxisUnit` 配置坐标轴，
`Legend` 决定图例位置，`ShowTooltip` / `ShowCrosshair` 控制悬停反馈。双轴不在节点上——用 `Chart` API 搭。

这些属性放的都是**字段名**，永远不是值本身——就是 `DataRow.Set(...)` 里的名字／检查器字典里的键，
每一行都会用这个名字去查一次：

```csharp compile
view.SetData(new[]
{
    new DataRow().Set("month", "Jan").Set("revenue", 1200),
    new DataRow().Set("month", "Feb").Set("revenue", 1800),
});

view.XField = "month";       // "month" 是上面那些行里的键，不是类目名
view.YField = "revenue";     // 这一列会被逐行读取
```

某一行没有这个字段时，只是那个通道没有值（不会报错）——所以同一张图里的行可以带不同的字段集合。

`ChartKind` 覆盖全部 22 种内置类型（`Bar`、`Line`、`Area`、`Scatter`、`RangeArea`、`Pie`、`Donut`、
`Radar`、`Violin`、`Box`、`Candlestick`、`Heatmap`、`Treemap`、`Sunburst`、`Sankey`、`Chord`、`Gauge`、
`Funnel`、`Waffle`、`Timeline`、`Lollipop`、`Milestone`）——22 种类型由 20 个 `Mark` 类实现
（`Line` 与 `Area` 共用 `LineMark`，`Pie` 与 `Donut` 共用 `PieMark`；注解 mark `SectionMark` 是第 21 个子类，
不承载任何类型）。类型始终显式指定，没有自动判断。
字段名默认沿用库内约定（`category`、`value`、`series`）；需要额外数据的类型读 `lower`、
`min/q1/median/q3/max`、`open/high/low/close`、`start/end`、`source/target`、`parent`——
如果你的字段名不同，用 `ConfigureMark` 改。

### 步骤 3：添加颜色维度

若数据包含多个系列（如 Revenue 和 Cost），用 `Channel.Color` 将系列名映射到颜色：

```csharp compile
var combined = new List<DataRow>();

// 收入行
foreach (var sale in monthlySales)
    combined.Add(new DataRow()
        .Set("month", sale.Get<string>("month"))
        .Set("value", sale.Get<int>("revenue"))
        .Set("type", "Revenue"));

// 成本行
foreach (var sale in monthlySales)
    combined.Add(new DataRow()
        .Set("month", sale.Get<string>("month"))
        .Set("value", sale.Get<int>("cost"))
        .Set("type", "Cost"));

new Chart(canvas)
    .Data(combined)
    .Mark(new IntervalMark { CornerRadius = 3, BarPadding = 0.25f })
    .Encode(Channel.X, "month")
    .Encode(Channel.Y, "value")
    .Encode(Channel.Color, "type")   // ← 按系列着色
    .Legend(new LegendConfig { Position = LegendPosition.Top })
    .Render();
```

## 第一个图表：折线图

```csharp compile
var data = new List<DataRow>
{
    new DataRow().Set("month", "Jan").Set("value", 30).Set("series", "ProductA"),
    new DataRow().Set("month", "Feb").Set("value", 45).Set("series", "ProductA"),
    new DataRow().Set("month", "Mar").Set("value", 38).Set("series", "ProductA"),
    new DataRow().Set("month", "Jan").Set("value", 20).Set("series", "ProductB"),
    new DataRow().Set("month", "Feb").Set("value", 35).Set("series", "ProductB"),
    new DataRow().Set("month", "Mar").Set("value", 50).Set("series", "ProductB"),
};

new Chart(canvas)
    .Data(data)
    .Mark(new LineMark { Smooth = true, StrokeWidth = 2.5f })
    .Encode(Channel.X, "month")
    .Encode(Channel.Y, "value")
    .Encode(Channel.Color, "series")
    .Legend(new LegendConfig { Position = LegendPosition.Top })
    .Render();
```

## 第一个图表：饼图

```csharp compile
var data = new List<DataRow>
{
    new DataRow().Set("type", "Physical").Set("value", 45),
    new DataRow().Set("type", "Magic").Set("value", 30),
    new DataRow().Set("type", "True").Set("value", 15),
    new DataRow().Set("type", "Pure").Set("value", 10),
};

new Chart(canvas)
    .Data(data)
    .Mark(new PieMark { ShowLabel = true })
    .Encode(Channel.X, "type")
    .Encode(Channel.Y, "value")
    .Encode(Channel.Color, "type")
    .Render();
```

## 添加主题

GodotChart 提供内置暗色和亮色主题：

```csharp compile
var data = new List<DataRow>
{
    new DataRow().Set("month", "Jan").Set("value", 10),
    new DataRow().Set("month", "Feb").Set("value", 20),
};
// 深色主题（默认）
var darkTheme = ChartTheme.Dark();

// 浅色主题
var lightTheme = ChartTheme.Light();

new Chart(canvas)
    .Theme(lightTheme)
    .Data(data)
    .Mark(new IntervalMark())
    .Encode(Channel.X, "category")
    .Encode(Channel.Y, "value")
    .Render();
```

主题可以在 Godot Inspector 中作为 `Resource` 编辑，所有属性均标记 `[Export]`。

## 添加动画

```csharp compile
var data = new List<DataRow>
{
    new DataRow().Set("month", "Jan").Set("value", 10),
    new DataRow().Set("month", "Feb").Set("value", 20),
};
// 在 _Process(double delta) 里：
_animController.StartEntry(host, seriesCount: 2);

var animCtx = new AnimationContext
{
    EntryProgress = _animController.EntryProgress,
    GlobalOpacity = _animController.GlobalOpacity,
};

new Chart(canvas)
    .Data(data)
    .Mark(new IntervalMark())
    .Encode(Channel.X, "month")
    .Encode(Channel.Y, "value")
    .Animate(animCtx)
    .Render();
```

## 需要更多控制

要额外 mark、tooltip、命中测试，或把纹理交给别的消费者时，就下到画布这一层。画布渲染到它自己的
Godot 纹理上，不碰场景树，所以怎么展示这张纹理由你决定。`Canvas2DControl` 自带一个 canvas 并替你
展示——它创建画布、让表面跟随节点尺寸、跑帧循环并把纹理画出来：

```csharp compile-members
public override void _Ready()
{
    // 视图会创建一张与本 Control 同尺寸的画布，并呈现它的纹理。
    var view = new Canvas2DControl { BackgroundColor = Colors.Transparent };
    view.SetAnchorsPreset(LayoutPreset.FullRect);
    AddChild(view);

    _chart = new Chart(view.Canvas!)
        .Mark(new IntervalMark { CornerRadius = 3 })
        .Encode(Channel.X, "month")
        .Encode(Channel.Y, "revenue")
        .XAxis(new AxisConfig { Title = "Month" })
        .YAxis(new AxisConfig { Title = "Revenue", Unit = "USD" });

    view.CanvasDraw += (_, canvas) => _chart.Render();
    view.Invalidate(); // 下一帧绘制
}
```

`Invalidate()` 安排一帧重绘；节点会先用 `BackgroundColor` 清屏（需要累积画面时把 `ClearBeforeDraw`
关掉），然后自己提交这一帧。`Render()` 不属于链式构建器（它返回 `void`），所以放在绘制回调里。
节点**没有**背景导出：surface 始终被清成**透明**，看到的那块背景矩形是画出来的（`BackgroundRenderer`，
圆角取 `ChartTheme.BackgroundCornerRadius`）——所以要透出宿主画面，就让这块矩形透明：
`view.ConfigureChart = c => c.BackgroundColor = Colors.Transparent;`，或把主题 `BackgroundColor` 的
alpha 设为 0。

如果纹理需要放到别处——`Sprite2D`、`TextureRect`、自定义 `CanvasItem`，或者多个视图共享一张画布
——就直接创建画布并自己驱动帧循环：

```csharp compile-members
public override void _Ready()
{
    // 不与场景树耦合：画布把它绘制出的纹理交给你。
    _canvas = Canvas2DFactory.Create(width, height);

    var sprite = new Sprite2D { Texture = _canvas.Texture, Centered = false };
    AddChild(sprite);
}

public override void _Process(double delta)
{
    if (_canvas == null || _chart == null) return;

    _canvas.BeginFrame();
    _canvas.Clear(Colors.Transparent); // 背景由图表自己绘制
    _chart.Render();
    _canvas.EndFrame();                // 把这一帧上传到纹理
}
```

下层是链式 API：**Data** 绑定数据、**Mark** 选图表类型、**Encode** 把字段映射到通道、**Axis** 配置
坐标轴，一帧在 `BeginFrame`/`EndFrame` 之间提交。画布在 `_ExitTree` 里释放；图表不拥有它。

## 下一步

- [需要更多控制](#需要更多控制)（上一节）—— `ChartView` 不够用时的画布层走法
- [图表类型](chart-types.cn.md) — 了解全部 22 种图表类型的构造方法
- [定制与主题](customization.cn.md) — 深入主题、标度、坐标轴配置
- [高级功能](advanced.cn.md) — 动画、交互事件、实时数据流
