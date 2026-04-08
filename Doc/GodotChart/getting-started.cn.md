# 快速入门

本文档帮助你从零开始创建第一个 GodotChart 图表。

## 前置条件

- Godot 4.4+ 工程并启用 C# 支持
- .NET 9.0 SDK
- 项目已引用 GodotNodeExtension 和 SkiaSharp

## 核心概念

### DataRow — 数据行

GodotChart 使用 `DataRow` 作为数据单位，每行包含若干命名字段：

```csharp
var row = new DataRow()
    .Set("month", "Jan")
    .Set("revenue", 1200)
    .Set("cost", 800);

// Read data
string month = row.Get<string>("month");  // "Jan"
int revenue = row.Get<int>("revenue");    // 1200
```

可用 `TryGet<T>()` 安全地读取可能缺失的字段，用 `Has()` 检查字段是否存在。

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
| `Channel.Shape` | 形状 | 标记形状 |
| `Channel.Label` | 文本标签 | 数据点标注 |

### Mark — 图形标记

Mark 决定数据如何**可视化**。每种 Mark 对应一类图表：

```csharp
new IntervalMark()  // 柱状图
new LineMark()      // 折线图
new PieMark()       // 饼图
new PointMark()     // 散点图
```

### Scale — 度量

Scale 控制数据值到视觉空间的映射方式：

```csharp
new LinearScale(0, 100)       // 线性映射 [0, 100] → [0, 1]
new LogScale(1, 10000)        // 对数映射
new OrdinalScale()            // 分类映射（自动推断）
new DivergingColorScale(-1,1) // 发散色阶
```

大多数情况下 Scale 会**自动推断**，无需手动指定。

## 第一个图表：柱状图

### 步骤 1：准备数据

```csharp
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

```csharp
var chart = new Chart(canvas)
    .Data(data)
    .Mark(new IntervalMark { CornerRadius = 3 })
    .Encode(Channel.X, "month")
    .Encode(Channel.Y, "revenue")
    .XAxis(new AxisConfig { Title = "Month" })
    .YAxis(new AxisConfig { Title = "Revenue", Unit = "USD" })
    .Render();
```

这几行代码完成了：
1. **Data** — 绑定数据源
2. **Mark** — 选择柱状图标记
3. **Encode** — 将 `month` 映射到 X 轴、`revenue` 映射到 Y 轴
4. **Axis** — 配置坐标轴标题
5. **Render** — 执行渲染

### 步骤 3：添加颜色维度

若数据包含多个系列（如 Revenue 和 Cost），用 `Channel.Color` 将系列名映射到颜色：

```csharp
var combined = new List<DataRow>();

// Revenue rows
foreach (var sale in monthlySales)
    combined.Add(new DataRow()
        .Set("month", sale.Get<string>("month"))
        .Set("value", sale.Get<int>("revenue"))
        .Set("type", "Revenue"));

// Cost rows
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
    .Encode(Channel.Color, "type")   // ← color by series
    .Legend(new LegendConfig { Position = LegendPosition.Top })
    .Render();
```

## 第一个图表：折线图

```csharp
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

```csharp
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

```csharp
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

主题可以在 Godot Inspector 中作为 `Resource` 编辑，所有属性均标记 `[Export]`。

## 添加动画

```csharp
// In _Process(double delta):
_animController.StartEntry(this, seriesCount: 2);

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

## 下一步

- [图表类型](chart-types.cn.md) — 了解全部 19 种图表的构造方法
- [定制与主题](customization.cn.md) — 深入主题、度量、坐标轴配置
- [高级功能](advanced.cn.md) — 动画、交互事件、实时数据流
