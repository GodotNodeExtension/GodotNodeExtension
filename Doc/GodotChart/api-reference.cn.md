[英文](api-reference.md) | **中文**

# API 参考

本文档列出 GodotChart 所有公共类、接口、枚举及其成员签名。

---

## ChartView（快捷路径）

```csharp
[GlobalClass]
public partial class ChartView : Control
```

一个节点就是一张图：设置图表类型、把数据行交给它，它会自行构建 mark、映射通道、配置坐标轴与图例、
处理悬停提示、让表面跟随节点尺寸并重绘。尺寸与呈现都在内部完成——行为类似 `TextureRect`。

### 导出属性

下面所有 `*Field` 属性放的都**是字段名，不是值**：这个名字会在每一行里被查找（`Rows` 字典的键，或
`DataRow.Set(...)` 的名字），那一行在该字段里的值才是驱动通道的东西。所以 `XField = "month"` 的意思是
「读每行的 `month` 字段」；某行没有这个字段时，只是那个通道在这一行没有值。

| 属性 | 类型 | 默认值 | 说明 |
|---|---|---|---|
| `Kind` | `ChartKind` | `Bar` | 图表类型——始终显式指定，节点不会根据数据去猜 |
| `Rows` | `Array<Dictionary>` | 空 | 强类型数据行——可在检查器里编辑的数据形态；每个字典的**键**就是 `*Field` 属性所指的字段名 |
| `WindowSize` | `int` | `0` | 流式追加时保留的最大行数；`AddRow` 超过就丢弃最旧的（0 = 不限） |
| `XField` | `string` | `""` | 映射到 X 通道（类别）的字段；留空用类型默认值 |
| `YField` | `string` | `""` | 映射到 Y 通道（数值）的字段 |
| `ColorField` | `string` | `""` | 映射到颜色通道的字段（同时驱动图例）；留空时用 `series`，饼图 / 环图 / 漏斗 / 华夫饼用类目字段。字段值本身是颜色（`Color` 值或 `"#rrggbb"` 字符串）时按原样使用；`constant:#ff8800` 把整张图画成一个颜色 |
| `SizeField` | `string` | `""` | 映射到尺寸通道的字段（气泡图）：值按该列数据范围映射到 `PointSizeMin`…`PointSizeMin + PointSizeRange` 像素 |
| `ShapeField` | `string` | `""` | 映射到形状通道的字段（每个类目一个符号） |
| `OpacityField` | `string` | `""` | 映射到透明度通道的字段：值按该列 `0`…`max` 映射到 `0`…`1`（钳制） |
| `XAxisTitle` / `XAxisUnit` | `string` | `""` | X 轴标题 / 单位（单位显示在轴区域的悬停提示里） |
| `YAxisTitle` / `YAxisUnit` | `string` | `""` | Y 轴标题 / 单位 |
| `Legend` | `LegendPosition` | `Bottom` | 图例位置（`Top` / `Bottom` / `Left` / `Right` / `None`） |
| `ShowTooltip` | `bool` | `true` | 悬停提示 |
| `ShowCrosshair` | `bool` | `true` | 跟随指针的准星 |
| `XAxisTickStep` / `YAxisTickStep` | `float` | `0` | 刻度步长（数据单位，`10` = 1970/1980/…）；`0` 表示自动，按整倍数细化（10 → 5 → 2 → 1） |
| `XAxisTickCount` / `YAxisTickCount` | `int` | `0` | 该轴刻度个数；`0` 表示由轴长与 `TickLabelSpacing` 推导 |
| `XAxisTickSpacing` / `YAxisTickSpacing` | `float` | `0` | 该轴两个刻度标签之间的像素距离；`0` 用自动间距（X 轴取 `ChartTheme.TickLabelSpacing`，Y 轴取标签行高） |
| `XAxisLabelFormat` / `YAxisLabelFormat` | `string` | `""` | 数值轴刻度标签格式串（如 `"0.0 °C"`）；空表示沿用标度自身的文本 |
| `XAxisLabelRotation` | `float` | `0` | X 轴刻度标签旋转角度（度）；`0` 表示水平（长类目名可以完整显示，而不必隔一个画一个） |
| `ZoomMode` | `ChartZoomMode` | `None` | 滚轮缩放与拖拽平移（`ZoomX` / `PanX` / `X` / `Y` / `Both`）；`None` 时滚轮交给宿主 |
| `ZoomFactor` | `float` | `1.2` | 每格滚轮的缩放倍率 |
| `PanButton` | `MouseButton` | `Left` | 用于拖拽平移的鼠标键 |
| `ResetZoomOnDoubleClick` | `bool` | `true` | 双击复位缩放 |
| `SectionLevels` | `Array[float]` | 空 | 参考线，见 `SectionMark` |
| `SectionBandFrom` / `SectionBandTo` | `float` | `0` | 参考带两端；两者相等则不画 |
| `SectionTarget` | `ChartSectionTarget` | `Y` | 参考线读取的轴（`Y`、`Y2`、`X`） |
| `SectionColor` | `Color` | 橙色 | 参考线、参考带与标签的颜色 |
| `SectionDashed` | `bool` | `true` | 参考线是否虚线 |
| `SectionLabelFormat` | `string` | `""` | 线值标签格式串；空表示不标 |
| `HeatmapMaxCells` | `int` | `0` | 热力图格子预算（`0` 表示用 `HeatmapMark.MaxCells`）；超出部分不画并给出 warning |
| `DiagramMaxNodes` | `int` | `0` | treemap / sankey 的节点预算（`0` 表示用 mark 自己的默认值）；超出部分不画并给出 warning |
| `Title` | `string` | `""` | 图表标题 |
| `ThemeKind` | `ChartThemeKind` | `Dark` | 内置配色（`Dark` / `Light`） |
| `ColorMapping` | `ColorMappingKind` | `Auto` | 颜色通道的映射方式：`Auto`（值本身是颜色就用它，否则按类目）、`Category`、`Identity`、`Sequential`、`Diverging` |
| `XAxisRange` / `YAxisRange` | `Vector2` | `(0, 0)` | 锁定该轴的 `(最小值, 最大值)` 范围；`(0, 0)` 表示按数据拟合。只对数值轴生效——类目轴会忽略它 |
| `YAxisAutoScaleMargin` | `float` | `0` | 自动拟合时保留的域宽边距比例，小幅波动不再重拟合（`0` 表示数据一出域就重拟合） |
| `YAxisNiceDomain` | `bool` | `false` | 把自动拟合出的域向外扩整到 `{1, 2, 5} x 10^n` 台阶 |
| `YAxisMinLimit` / `YAxisMaxLimit` | `float` | `NaN` | 只钉住 Y 轴的一端（`NaN` 表示自由），另一端仍跟随数据 |
| `SizeRange` | `Vector2` | `(0, 0)` | 尺寸通道的 `(最小, 最大)` 半径像素；`(0, 0)` 用 `PointSizeMin`…`PointSizeMin + PointSizeRange` |
| `OpacityRange` | `Vector2` | `(0, 0)` | 透明度通道的 `(最小, 最大)`；`(0, 0)` 映射到完整 `0…1` |
| `ShapeSymbols` | `Array<ShapeKind>` | 空 | 形状通道循环使用的符号；空则用默认词汇表 |
| `GroupedBars` | `bool` | `false` | 柱状类型：同一类目的多个系列并排显示，而不是互相重叠（堆叠时忽略） |
| `Stack` | `StackMode` | `None` | 柱状与面积类：`None` / `Stack` / `Normalize` |
| `Decimate` | `DecimateMode` | `Auto` | 折线与面积类的抽稀：`Auto` 每个像素列保留最低与最高点，`On` 即使画得下也缩减，`Off` 逐行绘制 |
| `CustomTheme` | `ChartTheme?` | null | 主题资源（配色、线宽、尺寸、字体、调色板）；为 null 时用 `ThemeKind` 的内置配色。节点只读该资源，并跟随它的 `changed` 信号 |
| `LayeredRendering` | `bool` | `false` | 把非交互层存成一张图，移动指针时只重画覆盖层（hover/选中、准星、tooltip）。传给图表的等价物是 `Chart.UseLayerCache` —— 见[分层渲染](#分层渲染) |
| `PlotAspectRatio` | `float` | `0` | 内容被赋予的形状，宽 / 高（`1` = 方形）；`0` 交给 mark 决定（饼图、雷达、仪表、弦图、旭日图要求方形），负值强制填满绘图区 |
| `PlotAlignHorizontal` | `HorizontalAlignment` | `Center` | 内容框在绘图区里的横向位置（由 `PlotAspectRatio` 塑形时生效） |
| `PlotAlignVertical` | `VerticalAlignment` | `Center` | 纵向同理 |
| `EditorPreview` | `bool` | `true` | 在编辑器里也绘制图表（本节点是 tool 脚本） |
| `IgnoreContentMinimumSize` | `bool` | `false` | 改为报告绘图区自身的最小尺寸（而不是内容：标题、图例、轴标签），让页面可以把节点压小 |

```csharp compile
// ChartView 的导出：场景里能配，代码里也能设。每一条都在某个示例页上出现过
// （ChartLayoutDemo / ChartBigDataDemo / BasicsDemo），这里是整套一起设。
view.XAxisLabelRotation = 30f;          // 长类目名斜排后仍能看全
view.XAxisTickStep = 500f;              // 以数据单位计的步长；0 表示交给轴自己决定
view.XAxisTickSpacing = 40f;            // 一个标签可占的像素；0 表示用主题的间距
view.XAxisTickCount = 12;               // 精确刻度数；0 表示按轴长推导
view.XAxisLabelFormat = "0.0 °C";       // 空串表示沿用标度自身的文本
view.YAxisLabelFormat = "0.0 °C";       // 两条轴的标签由同一段代码生成
view.YAxisMinLimit = 0f;                // NaN 表示该端继续自适应
view.YAxisMaxLimit = 100f;
view.PlotAspectRatio = 1f;              // 内容框的宽高比（1 = 正方）；0 表示交给 mark
view.PlotAlignVertical = VerticalAlignment.Center;
view.Decimate = DecimateMode.On;        // 折线/面积类的抽点策略
view.ZoomFactor = 1.5f;                 // 滚轮一格缩放多少
view.PanButton = MouseButton.Middle;
view.ResetZoomOnDoubleClick = false;
view.HeatmapMaxCells = 64;              // 单元格预算；超出时 mark 会警告
view.DiagramMaxNodes = 24;              // treemap / sankey 的节点预算
view.SectionTarget = ChartSectionTarget.Y;  // 参考线属于哪条轴
view.SectionDashed = false;             // 实线（默认虚线）
```

### 各类默认字段绑定

`Kind` 始终显式指定——节点不会根据数据去推断（见上文 `ChartKind` 列表）。下面是 `ChartView` 为各类型绑定的字段；
显式设置 `XField` / `YField` / `ColorField` 优先于这些默认名。

| 类型 | 绑定 |
|------|------|
| Heatmap | `X` ← `x`、`Y` ← `y`、`Color` ← `value` |
| Timeline | `Y` ← `category`、`X` ← `value`（类目竖直、范围水平） |
| Milestone | `X` ← `time`、`Y` ← `lane`；行里确实带 `series` 字段时 `Color` ← `series`——不绑数值通道 |
| Sankey、Chord | `Y` ← `value`；`source` / `target` 由 Mark 自行按名读取 |
| Box、Candlestick | 只绑 `X` ← `category`——值域由 Mark 自身的字段提供（`min`…`max`、`open`…`close`），若再绑 Y 会把坐标轴压到 0 |
| 饼图、环图、漏斗、华夫饼 | `X` ← `category`、`Y` ← `value`；`Color` ← `category`——一行就是一个扇形 / 阶段 / 方格，类目就是它的身份，于是每块一色并带出类目图例 |
| 其它所有类型 | `X` ← `category`、`Y` ← `value`；当行里确实带 `series` 字段时，`Color` ← `series` |

`SizeField`、`OpacityField` 与 `ShapeField` 只在行里确实含有该字段时才会绑定。第二数值轴（`Channel.Y2`）不属于节点：
用 `Chart` API 搭。

### 成员

| 成员 | 返回 | 说明 |
|---|---|---|
| `SetData(IEnumerable<DataRow> rows)` | ChartView | 替换数据行（强类型值；会清空导出的数组） |
| `SetData(params DataRow[] rows)` | ChartView | 替换数据行 |
| `AddRow(DataRow row)` | ChartView | 追加一行（同步写入导出数组） |
| `SetValues(IEnumerable<(string, double)> values)` | ChartView | 类别/数值对的快捷写法 |
| `SetCsv(string csv)` / `ParseCsv(string csv)` | ChartView / `List<DataRow>` | CSV 仍可从代码使用 |
| `Repaint()` | void | 只重绘当前帧不重建（动画、悬停等逐帧状态因此保留） |
| `Clear()` | ChartView | 清空数据 |
| `Refresh()` | void | 用当前设置重绘 |
| `ConfigureMark(Action<Mark> configure)` | ChartView | 微调构建出的 mark（额外字段、样式） |
| `Kind` | `ChartKind` | 图表类型（显式指定） |
| `DataRows` | `IReadOnlyList<DataRow>` | 当前显示的数据行 |
| `Rows` | `Array<Dictionary>` | 数据行的 Godot 变体形态 |
| `Chart` | `Chart` | 底层图表（逃生口） |
| `Canvas` / `Texture` / `Surface` | `ICanvas2D` / `Texture2D` / `Canvas2DControl` | 内部画布与其表面 |
| `Tooltip` | `TooltipRenderer` | 提示框渲染器，用于自定义样式 |
| `CanvasFactory` | `Func<int,int,ICanvas2D>` | 自定义 / 注入 / 共享画布 |
| `ConfigureChart` | `Action<Chart>?` | 构建出的 Chart 本体的配置**属性**（赋值一个委托），不像方法式的 `ConfigureMark`：导出属性表达不了的标度、追加 mark；**每次重建都会调用** |
| `SavePng(string path)` | `bool` | 把视图当前显示的画面写成 PNG（工具/构建脚本/文档配图用）；无渲染设备时返回 `false` |

### ChartKind（枚举）

`Bar`、`Line`、`Area`、`Scatter`、`RangeArea`、`Pie`、`Donut`、`Radar`、`Violin`、`Box`、
`Candlestick`、`Heatmap`、`Treemap`、`Sunburst`、`Sankey`、`Chord`、`Gauge`、`Funnel`、`Waffle`、
`Timeline`、`Lollipop`、`Milestone`。

### ChartThemeKind（枚举）

`Dark`、`Light`。

### ColorMappingKind（枚举）

`Auto`、`Category`、`Identity`、`Sequential`、`Diverging`——颜色通道如何把取值变成颜色
（见 `ChartView.ColorMapping`；映射按节点配置，而不是按 mark）。

---
### SectionMark（Mark）

注释类 mark：在图上画参考线与参考带（目标值、阈值）。它通过 `Target` 映射，**不参与任何标度**（远在表外的线值不会把轴拉长），
不出现在图例里，落在可见窗口之外的线会被跳过而不是钉在边缘；缩放与平移时线随数据移动。

| 成员 | 类型 | 默认值 | 说明 |
|---|---|---|---|
| `Levels` | `double[]` | 空 | 要画线的值 |
| `BandFrom` / `BandTo` | `double?` | `null` | 参考带两端；两者都要给 |
| `Target` | `Channel` | `Channel.Y` | `Y`（水平）、`Y2`（右轴）、`X`（垂直） |
| `Thickness` | `float` | `1` | 线宽（像素） |
| `Dashed` | `bool` | `true` | 是否虚线 |
| `DashLength` / `DashGap` | `float` | `6` / `4` | 虚线段长/间隔（像素） |
| `Color` | `Color?` | `null` | `null` 表示用主题的网格色 |
| `BandOpacity` | `float` | `0.12` | 参考带填充不透明度 |
| `LabelFormat` | `string` | `"{0}"` | 继承自 `Mark`：线值本身，空串则不标；`{1}` 为空——参考线横跨另一条轴，那里没有单一线值 |
| `LabelInset` | `float` | `6` | 标签离绘图区边缘的距离 |

### AxisConfig

坐标轴通过 `AxisConfig` 配置（`chart.XAxis(...)`、`chart.YAxis(...)`、`chart.Y2Axis(...)`）。

| 成员 | 类型 | 默认值 | 说明 |
|---|---|---|---|
| `Title` / `Description` / `Unit` | `string?` | `null` | 轴标题、tooltip 文本与单位 |
| `TickStep` | `double?` | `null` | 刻度步长（数据单位，`10` = 1970/1980/…）；覆盖自动步长（自动步长按整倍数细化） |
| `TickCount` | `int?` | `null` | 期望的刻度个数，覆盖由轴长推导的个数 |
| `Ticks` | `double[]?` | `null` | 精确刻度值，优先级最高；窗口之外的值会被跳过 |
| `LabelFormat` | `string?` | `null` | 数值轴刻度标签格式串 |
| `LabelRotation` | `float?` | `null` | 该轴刻度标签旋转角度（度，仅 X 轴） |
| `TickLabelSpacing` | `float?` | `null` | 该轴两个刻度标签之间的像素距离；`null` 用自动间距（X 轴取 `ChartTheme.TickLabelSpacing`，Y 轴取标签行高） |
| `AutoScaleMargin` | `float?` | `null` | 粘性自动缩放的边距；数据在边距内时域不动 |
| `NiceDomain` | `bool` | `false` | 拟合后把域扩到 {1, 2, 5}×10ⁿ 台阶 |
| `MinLimit` / `MaxLimit` | `double?` | `null` | 只钉一端、另一端继续自动拟合 |

## Chart (主入口)

```csharp
public partial class Chart : IDisposable
```

### 构造与销毁

| 方法 | 返回 | 说明 |
|------|------|------|
| `Chart(ICanvas2D canvas, bool ownsCanvas = false)` | — | 构造函数；`ownsCanvas: true` 时 `Dispose()` 会连画布一起释放（共享的画布保持存活） |
| `Dispose()` | void | 释放资源 |

### 布局与尺寸

图表能报出"当前内容至少需要多大"，这正是小卡片不会把坐标轴标签挤掉的原因：

| 成员 | 类型 | 说明 |
|---|---|---|
| `MinimumSize` | `Vector2` | 当前内容保持可读所需的最小尺寸：标题 + 图例 + 轴标签 + 轴标题 + `MinimumPlotSize`。随布局重算；估算值宁可偏大 |
| `MinimumPlotSize` | `Vector2`（静态） | `MinimumSize` 里留给绘图区的那部分（120x80）；其余都是装饰 |
| `CurrentPlotArea` | `PlotArea?` | 上一帧实际使用的绘图区矩形 |
| `Width` / `Height` | `float` | 图表绘制的表面尺寸 |
| `DrawnBounds` | `Rect2?` | 上一帧图表实际用到的矩形：内容框 ∪ 本次真正排布出来的装饰带（标题、图例、轴标签列、轴标题）。它跟着装饰走——关掉一项它就变——**不含**背景填充（按设计铺满整个画布）与宿主的 tooltip；首帧之前为 null |

低于 `MinimumSize` 时图表会报**一条**警告（不是每帧 ✗）并继续绘制（标签会被抽稀 ✓）。`ChartView` 通过
`_GetMinimumSize()` 把同一个数字上报给引擎，因此容器与引擎都不会把它压得更小 ✓；`IgnoreContentMinimumSize`（导出属性，默认关）
可以让某个节点不受这条约束 ✓。这不需要先画过一帧：`Chart` 存在之前节点上报的是**同一批预留量的估算**
（主题各项预留 + 一个像样的标签列 + 一行图例），首帧画完后由图表自身的量测替换它 —— 容器随后会重新排版 ✓。

要区分两个矩形：**绘图区**是图表内边距与各项预留之后剩下的部分，**内容框**才是 mark 真正被排布的区域，
也就是 `CurrentPlotArea` 报告的那个矩形 ✓。只用绘图区**短边**作度量的 mark（所有极坐标 mark：半径 = `min(宽,高)/2` × 系数）
会要求一个方形内容框（`Mark.PreferredAspectRatio` ✓），于是宽画布上它不再被排进一个只能用掉一小块的矩形里；
`PlotAspectRatio` 可以覆盖这个形状（`0` = 强制回到"填满绘图区"的历史行为 ✓），`PlotAlignHorizontal` / `PlotAlignVertical`
决定这块内容框贴在哪边。装饰仍然按整个绘图区排：图例照旧按整个宽度换行 ✓，标题位置不变 ✓；
整张图到哪为止则看 `DrawnBounds`（它跟着外围装饰走 ✓）。

### Fluent 构建方法

| 方法 | 返回 | 说明 |
|------|------|------|
| `Theme(ChartTheme theme)` | Chart | 设置主题；图表会跟随它的 `changed` 信号（丢弃缓存的布局与 mark 几何），`Dispose()` 后不再跟随 |
| `Mark(Mark mark)` | Chart | 添加图形标记 |
| `Mark<T>() where T : Mark, new()` | Chart | 按类型添加标记 |
| `ApplyToAllMarks(Action<Mark> action)` | Chart | 批量配置标记 |
| `Data(IEnumerable<DataRow> rows)` | Chart | 设置数据 |
| `AppendData(DataRow row)` | Chart | 追加单行数据 |
| `AppendData(IEnumerable<DataRow> rows)` | Chart | 追加多行数据 |
| `Encode(Channel ch, string field)` | Chart | 绑定字段编码 |
| `Encode(Channel ch, object constant)` | Chart | 绑定常量编码 |
| `ScaleDomain(Channel, double min, double max)` | Chart | 锁定某通道的域（仅线性标度）；在自动 fit **以及各 mark 的刻度贡献之后**应用，所以锁定既能跨数据变更存活，也不会被堆叠 / 箱线等 mark 覆盖 |
| `Scale(Channel ch, IScale scale)` | Chart | 设置标度 |
| `Transform(IDataTransform transform)` | Chart | 添加数据变换 |
| `Animate(float progress)` | Chart | 入场动画进度 |
| `Animate(AnimationContext? ctx)` | Chart | 完整动画上下文 |
| `XAxis(AxisConfig config)` | Chart | X 轴配置 |
| `YAxis(AxisConfig config)` | Chart | Y 轴配置 |
| `Y2Axis(AxisConfig config)` | Chart | 第二 Y 轴配置 |
| `Legend(LegendConfig config)` | Chart | 图例配置 |
| `Render()` | void | 在画布上绘制一帧（不可链式调用） |

### 交互方法

| 方法 | 返回 | 说明 |
|------|------|------|
| `HitTest(Vector2 pos)` | HitResult? | 命中测试 |
| `HandleClick(Vector2 pos, MouseButton button = MouseButton.Left)` | HitResult? | 以该按键触发 `OnClick`；只有左键会更新选中/聚焦 |
| `Select(int rowIndex)` | Chart | 选中数据行（使用渲染后的数据） |
| `Hover(int rowIndex)` | Chart | 设置悬停行（不触发 `OnHover`） |
| `NotifyHoverChanged(int rowIndex, HitResult? hit)` | void | 设置悬停行并触发 `OnHover` |
| `FocusSeries(string? seriesKey)` | Chart | 聚焦某个系列（其他系列降低透明度） |
| `Interaction(Vector2? mousePos)` | Chart | 更新交互状态（传 `null` 清除准星 / 悬停） |

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
| `AutoPadding` | bool | 按需加宽 `PaddingLeft` / `PaddingRight`，避免较长的 Y/Y2 刻度标签被裁掉（默认 true） |
| `PlotAspectRatio` | float? | 内容被赋予的形状（宽 / 高，`1` = 方形）。`null`（默认）在各 mark 意见一致时采纳它们的要求（极坐标 mark 要求方形 ✓）；`0` 强制填满绘图区 |
| `PlotAlignHorizontal` | HorizontalAlignment | 内容框在绘图区里的横向位置（`Left` / `Center` / `Right`，默认 `Center`） |
| `PlotAlignVertical` | VerticalAlignment | 纵向同理（`Top` / `Center` / `Bottom`，默认 `Center`） |

框架尺寸从 `ChartDefaults` 的度量出发：600 x 400 px 的图，内边距左 50 / 右 20 / 上 20 / 下 40——这些也都是
上面的属性，所以默认值只是一个起点。

### 分层渲染

一帧有两层：**数据层**（背景、标题、网格、轴、轴标签、图例，以及 mark 不含交互态的那部分）与**覆盖层**
（mark 的 hover/选中视觉，加上准星）。数据层可以存成一张图，在输入没变的帧上直接贴回来 —— 这就是大表上移动指针
变便宜的原因。

| 成员 | 类型 | 说明 |
|------|------|------|
| `UseLayerCache` | bool | 把数据层存成一张图，只重画覆盖层；默认 `false` |
| `InvalidateLayerCache()` | Chart | 丢弃缓存的图层，下一次 `Render()` 会重建它 |

`UseLayerCache` 只有在"画布后端能读回自己的表面"（`CanvasCapabilities.SupportsSurfaceCapture`）**并且**图上每个
mark 都把交互态画在覆盖层（`Mark.InteractionStateInOverlay`）时才会真正启用；否则图表退回单遍渲染，并只说明一次原因。
数据、布局、绘图区、主题、动画进度、聚焦系列、图例配置任一变化都会重建图层，而**指针移动永远不会** —— 那正是这个开关
的意义。直接改 mark 自身的设置（`mark.StrokeWidth = …`）图表观察不到，改完请调用 `InvalidateLayerCache()`。
代价、前提与退回规则见[分层渲染](advanced.cn.md#分层渲染)。

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

它们拿到的都是同一个 `RenderContext`，其中带着本帧的排版状态 —— 接管某一段自绘时直接用图表自己的数字，
不必重新测量 ✓：

| 上下文成员 | 类型 | 含义 |
|---|---|---|
| `Plot` | `PlotArea` | 内容框：marks、网格与轴被排布的那个矩形（也就是 `Chart.CurrentPlotArea`） |
| `FullPlot` | `PlotArea` | 内容被 `PlotAspectRatio` 塑形之前的绘图区 —— 装饰就是按它排的；没塑形时与 `Plot` 相同 |
| `DrawnBounds` | `Rect2?` | 整张图实际用到的矩形，与 `Chart.DrawnBounds` 同一个值 |
| `LegendLayout` | `LegendLayout?` | 图例几何：每个条目的矩形（`Items`）与它们占的盒子。没有图例（或没有可画的渲染器）时为 null |
| `OffsetX` / `OffsetY`、`Width` / `Height`、`PaddingLeft/Right/Top/Bottom` | `float` | 节点自身的矩形与内边距 |
| `Theme`、`Scales`、`Encodes`、`Data`、`Title`、`XAxisConfig` / `YAxisConfig` / `Y2AxisConfig`、`LegendConfig`、`ColorScale`、`FocusedSeries`、`HiddenSeries`、`MousePos` | | 内置渲染器读的全部内容 |

### 颜色覆盖

| 属性 | 类型 | 说明 |
|------|------|------|
| `BackgroundColor` | Color | 背景色 |
| `GridColor` | Color | 网格色 |
| `AxisColor` | Color | 轴色 |

这些是图表级的渲染器颜色。逐元素样式在 `Mark` 上：`Mark.StyleOverride` 是每个内置 mark 都会调用的
style 回调（传入由数据解析出的 `ElementStyle`，返回真正要画的样式），`Mark.States` 声明悬停 / 选中 /
非激活状态的外观。不设回调时由 `Color` / `Opacity` 通道决定，mark 自身的透明度属性（如 `FillOpacity`）
是回退值——这里的成员没有一个是「保留给自定义 mark」的。用法见 [Mark 基类](#mark-基类) 下的
**元素样式与状态** 一节。

### 事件

| 事件 | 参数类型 | 说明 |
|------|----------|------|
| `OnClick` | ChartClickEventArgs | 元素被点击（包含 ScreenPosition/MarkType） |
| `OnSelectionChanged` | ChartSelectionEventArgs | 选中变化 |
| `OnHover` | ChartHoverEventArgs | 悬停变化 |
| `OnFocusChanged` | ChartFocusEventArgs | 聚焦系列变化 |
| `OnLegendClick` | ChartLegendClickEventArgs | 点击图例项 |

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

通道只是插槽，具体读哪些由 mark 决定：内置 mark 消费 `X`、`Y`、`Y2`、`Color`、`Size`、`Opacity` 与
`Shape`；`Label` 仅 `TimelineMark` 与 `MilestoneMark` 使用。`Shape` 决定散点 / 气泡的符号和棒棒糖图圆点的形状，当 shape
通道与颜色通道覆盖同一批类目时，图例也会用这个符号代替方形色块。通道未编码、或某行缺少该字段时取值
就是「无值」，需要它的 mark 会跳过该行或整帧不画。

编码可以用 `Chart.Encode(channel, "field")` 设在图表级，也可以用 `Mark.Encode(channel, "field")` 只设给
某一个 mark。mark 级编码**只对这个 mark 生效**，图表级编码仍是其它 mark 的回退；刻度仍然每个通道一个
（会同时从两边的字段拟合），所以同一个通道上各 mark 绑定的字段不同时，请用 `Chart.Scale(...)` 显式指定
刻度。

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
| `UsesAxes` | bool | 该 Mark 是否画在坐标轴与网格上（virtual，默认 true）；所有 Mark 都返回 false 的图完全不画坐标轴，`WaffleMark` 覆写为 false |
| `YChannel` | Channel | 定位使用的 Y 通道（`Y` 或 `Y2`） |
| `Data` | List<DataRow>? | Mark 自有数据（优先于图表数据） |
| `LabelFormat` | string | 数据标签格式串；`{0}`/`{1}` 由各 mark 定义，自绘标签的 mark 不读 `LabelPosition`（Gauge 的标签按刻度格式化） |
| `LabelPosition` | LabelPosition | 数据标签位置 |
| `StyleOverride` | Func<DataRow, int, ElementStyle, ElementStyle>? | 逐元素样式回调；**每个内置 mark** 都会调用 |
| `States` | ElementStateStyles | 声明式的悬停 / 选中 / 非激活样式，只读（`{ get; }`：配置它的成员，不要给属性整体赋值）；未设值的成员沿用主题 |
| `LabelContentBuilder` | Func<LabelContext, IReadOnlyList<TooltipLine>?>? | 替换所有走 `DrawLabels` 的标签文字（内置 Interval/Line/Point/Milestone 与任何自定义 mark）；返回 null 时回落到 `LabelFormat`。自绘标签的 mark 用各自的 builder（饼图是 `PieMark.SliceLabelBuilder`） |
| `TooltipContentBuilder` | Func<TooltipContext, IReadOnlyList<TooltipLine>>? | 自定义 tooltip 内容 |
| `Encode(Channel, string)` | Mark | Mark 级编码；只在本 mark 上优先于图表级编码 |
| `Encode(Channel, object)` | Mark | Mark 级常量编码（例如只给这个 mark 一个固定颜色） |
| `HitTest(MarkContext, Vector2)` | HitResult? | 单元素命中测试 |
| `ContributeScales(...)` | void | 允许 Mark 扩展图表的标度 |
| `InteractionStateInOverlay` | bool | 该 mark 把交互态视觉画在覆盖层而不是 `Render` 里时为 true（虚属性，默认 false）。回答 true 的有 `LineMark`、`PointMark`、`IntervalMark`（堆叠也算：覆盖层会走同一份累加）、`BoxMark`、`CandlestickMark`、`HeatmapMark`、`LollipopMark`、`MilestoneMark`、`TimelineMark`、`WaffleMark`、`FunnelMark`、`GaugeMark`、`TreemapMark` 与 `SectionMark`（注解 mark：它没有自己的交互态，覆盖层本就该为空）。刻意回答 false 的 —— `RangeAreaMark`、`ViolinMark`、`PieMark`、`RadarMark`、`SankeyMark`、`ChordMark`、`SunburstMark` —— 各自在声明处写明了原因：它们的 hover 视觉就是元素自身的半透明填充，或者是覆盖层无法在不擦掉缓存层内容的前提下复现的几何。图上每个 mark 都回答 true 时，图层缓存（`Chart.UseLayerCache`）才可能启用 |
| `PreferredAspectRatio` | float? | 该 mark 的内容想要的形状（宽 / 高；虚属性，默认 null = 填满绘图区）。极坐标 mark（`PieMark`、`RadarMark`、`GaugeMark`、`ChordMark`、`SunburstMark`）要求方形，图表在图上 mark 意见一致时采纳 —— 见 `Chart.PlotAspectRatio` |
| `RenderOverlay(MarkContext ctx)` | void | 在"数据层被缓存"的帧里绘制本 mark 的 hover/选中视觉（虚方法，默认什么都不画）。它紧挨着准星、在贴回图层之后、在与数据层相同的绘图区裁剪下运行；`MarkContext.StateInOverlay` 让这两半知道该由谁来画 |
| `Render(MarkContext ctx)` | void | 唯一的抽象成员：mark 在这里绘制自己的元素。context 带着画布、绘图区、已解析的标度 / 编码 / 数据、逐帧 `Animation` 与交互状态（悬停 / 选中行、`StateInOverlay`） |

自定义 mark 用 `CreatePath()` / `CreatePaint()` 以及基类在其之上的 `protected` 助手来绘制：`ShapePath(ctx)` /
`ShapePaint(ctx)`（池化对象——见画布抽象）、`ShapeGeometry`（共享字形词汇）、`ToDouble` / `ToSingle` /
`GetDouble`（字段取值：读不成数字时返回 `NaN`，并按字段只告警一次），以及 `PaletteOf(ctx)`
（元素配色用的调色板：主题自带就用它，否则用内置默认）。

### Element Style & States（元素样式与状态）

数据决定元素**是什么**（颜色通道加上 mark 自身的默认色），样式决定它**怎么画**。`Mark.StyleOverride`
就是那个回调，`Mark.States` 是声明式的状态外观——只写需要和主题不同的项：

```csharp compile
new IntervalMark
{
    // style 回调：传入由数据解析出的样式，返回真正要画的样式。
    StyleOverride = (row, i, style) => i == 3 ? style.WithFill(Colors.Orange) : style,

    // 声明式状态：未设值的成员沿用主题。
    States =
    {
        ActiveFill          = Colors.White,   // 悬停填充（默认：把数据填充色提亮）
        SelectedStroke      = Colors.Yellow,  // 选中描边颜色
        SelectedStrokeWidth = 3f,             // 选中描边宽度
        InactiveOpacity     = 0.4f,           // 聚焦其它系列时的透明度
    },
};
```

| `ElementStyle` 成员 | 类型 | 说明 |
|---------------------|------|------|
| `Fill` | Color | 元素填充色 |
| `Opacity` | float | 元素透明度（0..1） |
| `WithFill(Color)` | ElementStyle | 同一样式、换一个填充色 |
| `WithOpacity(float)` | ElementStyle | 同一样式、换一个透明度 |

| `ElementState` 取值 | 含义 |
|---------------------|------|
| `Default` | 无特殊状态：按数据样式绘制 |
| `Active` | 指针悬停在该元素上（G2 的 `active` 状态） |
| `Selected` | 该元素被选中 |
| `Inactive` | 聚焦了其它系列，该元素被淡化（G2 的 `inactive` 状态） |

| `ElementStateStyles` 成员 | 类型 | 未设值时使用的值 |
|---------------------------|------|------------------|
| `ActiveFill` | Color? | 按 `ActiveBrighten` 提亮数据填充色 |
| `ActiveBrighten` | float? | 主题 `HoverBrighten` |
| `SelectedStroke` | Color? | 主题 `SelectionColor` |
| `SelectedStrokeWidth` | float? | 主题 `SelectionStrokeWidth` |
| `InactiveOpacity` | float? | 主题 `UnfocusedOpacity` |

什么都不用配：一个状态都不声明就是默认观感。基类的 `ResolveFill` 与 `ComputeElementOpacity` 先应用
回调、再应用状态，所以这两个属性对每个内置 mark 都生效。

---

## 所有 Mark 子类

承载 `ChartKind` 的 20 个子类。注解 mark `SectionMark`——参考线与区间带，不对应任何类型——记在
`ChartView` 的 `Section*` 导出那一节。

### IntervalMark

| 属性 | 类型 | 默认值 |
|------|------|--------|
| `BarPadding` | float | 0.2 |
| `CornerRadius` | float | 3 |
| `ShowLabel` | bool | false |
| `ShowValue` | bool | false |
| `Stack` | StackMode | None |
| `Orientation` | BarOrientation | Vertical |
| `GroupedBars` | bool | false |

**数据：** 一行 = 一个柱（`Stack` 打开时 = 一个堆叠段）：必需 `X` 类目与 `Y` 数值，可选 `Color`（系列键，
堆叠必需）与 `Opacity`（无自有固定字段）。打开 `GroupedBars` 后，同一类目的多个系列不再重叠，而是各占该类目
的一个子带（`Stack` 需保持 `None`；检查器里的同一个开关是 `ChartView.GroupedBars`）。类目列若是数值列，
会被自动推断成线性刻度，此时一根柱都不画。
见 [图表类型](chart-types.cn.md#柱状图--intervalmark)。

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

**数据：** 一行 = 一个折点，同一个 `Color` 分组的多行连成一条线。必需 `X` 与 `Y`（任意刻度），可选 `Color`。
点按行顺序相连（不排序），有效点少于 2 个的系列整条不画。见 [图表类型](chart-types.cn.md#折线图--linemark)。

### PointMark

| 属性 | 类型 | 默认值 |
|------|------|--------|
| `DefaultRadius` | float | 5 |
| `MinRadius` | float? | null |
| `RadiusRange` | float? | null |

**数据：** 一行 = 一个点（在图表级编码 `Size` 后即气泡，半径线性映射到 3–23 px）。必需 `X` / `Y`，可选
`Color`、`Opacity` 与 `Shape`（点的符号，行内无形状值时画圆形）；缺少 `Size` 字段的行回退到 `DefaultRadius`。
`MinRadius` / `RadiusRange` 只对这个 mark 覆盖尺寸范围的下限与叠加在上面的像素增量
（null 时用 `ChartTheme.PointSizeMin` / `ChartTheme.PointSizeRange`）。
见 [图表类型](chart-types.cn.md#散点图--气泡图--pointmark)。

### PieMark

| 属性 | 类型 | 默认值 |
|------|------|--------|
| `InnerRadius` | float | 0 |
| `StartAngle` | float | -π/2 |
| `ShowLabel` | bool | true |
| `LabelDistance` | float | 1.15 |
| `ExplodeRatio` | float? | null | 悬停时弹出距离比（相对外半径）；`null`（默认）时回落主题 `ChartTheme.PieExplodeRatio`（0.03） |
| `RadiusFactor` | float | 0.85 |
| `CenterText` | string? | null |
| `CenterFontSize` | float | 18 |
| `CenterSubFontSize` | float | 12 |
| `CenterContentBuilder` | Func<LabelContext, IReadOnlyList<TooltipLine>>? | null |
| `SliceLabelBuilder` | Func<LabelContext, string?>? | null |

**数据：** 一行 = 一个扇区：必需 `Y` 数值（`X` 为标签），非有限值行被丢弃；总和不为正时整张图不画；
占总量 100% 的单个扇区仍然绘制（扫角钳在整圈之下一点点），负值行没有扇区——会被跳过并告警，不再反向扫出。
`SliceLabelBuilder` 生成扇区标签文字（返回 null 回落到 `LabelFormat`），`CenterContentBuilder` 在圆环中心
绘制富文本内容（优先于 `CenterText`）。
见 [图表类型](chart-types.cn.md#饼图--圆环图--piemark)。

### RadarMark

| 属性 | 类型 | 默认值 |
|------|------|--------|
| `FillOpacity` | float | 0.15 |
| `StrokeWidth` | float | 2 |
| `PointRadius` | float | 3 |
| `GridRings` | int | 5 |
| `ShowAxisLabels` | bool | true |
| `ShowGrid` | bool | true |
| `RadiusFactor` | float | 0.85 |

**数据：** 一行 = 一个顶点（某系列在某个维度上的值）：`X` 维度、`Y` 线性刻度上的数值半径，可选 `Color`
系列。维度表是全部 `X` 取值的并集，各系列必须用同名维度；少于 3 个维度不画。
见 [图表类型](chart-types.cn.md#雷达图--radarmark)。

### CandlestickMark

| 属性 | 类型 | 默认值 |
|------|------|--------|
| `OpenField` | string | "open" |
| `HighField` | string | "high" |
| `LowField` | string | "low" |
| `CloseField` | string | "close" |
| `BodyWidthRatio` | float | 0.6 |
| `WickWidth` | float | 1.5 |
| `CornerRadius` | float | 3 |
| `BullishColor` | Color | — |
| `BearishColor` | Color | — |
| `FillBullish` | bool | true |

**数据：** 一行 = 一个周期：`X` 周期，外加按名读取的四个 OHLC 字段——`OpenField`、`HighField`、`LowField`、
`CloseField`（默认 `"open"` `"high"` `"low"` `"close"`）。缺任一个字段的行会被跳过，`Y` 只用于推断刻度。
可选的 `Color` 通道会替换蜡烛体与影线的涨/跌色；不编码时仍用涨/跌色（`BullishColor` / `BearishColor` 或主题色）。
见 [图表类型](chart-types.cn.md#k-线图--candlestickmark)。

### BoxMark

| 属性 | 类型 | 默认值 |
|------|------|--------|
| `MinField` | string | "min" |
| `Q1Field` | string | "q1" |
| `MedianField` | string | "median" |
| `Q3Field` | string | "q3" |
| `MaxField` | string | "max" |
| `BoxWidthRatio` | float | 0.5 |
| `WhiskerWidth` | float | 1.5 |
| `CornerRadius` | float | 3 |
| `BoxColor` | Color? | null |
| `LineColor` | Color? | null |

**数据：** 一行 = 一组已经算好的五数概括：`X` 类目，外加 `MinField`、`Q1Field`、`MedianField`、
`Q3Field`、`MaxField`（默认 `"min"` `"q1"` `"median"` `"q3"` `"max"`）。缺值或值非有限的行会消失。
见 [图表类型](chart-types.cn.md#箱线图--boxmark)。

### HeatmapMark

| 属性 | 类型 | 默认值 |
|------|------|--------|
| `CellGap` | float | 1 |
| `CornerRadius` | float | 3 |
| `ShowLabel` | bool | false |
| `MaxCells` | int | 65536 | 格子预算：超出的行不画并告警 |

**数据：** 一行 = 一个格子：`X` 列、`Y` 行（都必须是类目刻度），外加数值 `Color`，mark 会把它钳制到
顺序色阶上。行跟随 Y 轴刻度，而 Y 轴是自下而上的：Y 域的第一个类目在底部，紧挨它的轴标签。重复的 `(X, Y)`
行按顺序绘制——后者覆盖前者，不会相加——非有限值既不着色也不打标签（格子尺寸钳到 >= 0）。超过 `MaxCells`（65536）
的行不画，并给出一次 warning。见 [图表类型](chart-types.cn.md#热力图--heatmapmark)。

### GaugeMark

| 属性 | 类型 | 默认值 |
|------|------|--------|
| `ArcWidth` | float | 0.12 |
| `StartAngleDeg` | float | -210 |
| `EndAngleDeg` | float | 30 |
| `ShowCenterLabel` | bool | true | 显示中心数值 |
| `ShowMinMaxLabels` | bool | true | 显示最小/最大刻度标签 |
| `ValueColor` | Color? | — | 指标弧颜色；为 `null`（默认）时取主题 `DefaultMarkColor` |
| `TrackColor` | Color? | — | 轨道背景色；为 `null`（默认）时取主题 `GaugeTrackColor` |
| `InnerRadiusRatio` | float | 0 | 内圈空出的半径占比 |
| `RadiusFactor` | float | 0.85 | 弧半径占可用尺寸的比例 |

**数据：** 整张图只取**第一行**——多余的行被忽略。`Y` 是线性刻度上的指针值，`X` 不使用；请显式设置刻度
范围，否则自动推断的域会让几乎任何值都看起来接近满格。`ValueColor` 与 `TrackColor` 都可选：留 `null`
（默认）时取主题的 `DefaultMarkColor` / `GaugeTrackColor`，与其它 mark 一致。
见 [图表类型](chart-types.cn.md#仪表盘--gaugemark)。

### FunnelMark

| 属性 | 类型 | 默认值 |
|------|------|--------|
| `StageGap` | float | 4 | 层间距 |
| `MinWidthRatio` | float | 0.15 | 最窄层宽度比 |
| `CornerRadius` | float | 3 | 圆角 |
| `ShowLabel` | bool | true | 显示标签 |

**数据：** 一行 = 一个阶段，按**行顺序**从上往下绘制（不排序）：必需 `Y` 数值，`X` 标签与 `Color` 可选。
宽度是插值出来的（`MinWidthRatio` 是下限），阶段高度在行之间均分。见 [图表类型](chart-types.cn.md#漏斗图--funnelmark)。

### ViolinMark

| 属性 | 类型 | 默认值 |
|------|------|--------|
| `BinCount` | int | 20 |
| `WidthRatio` | float | 0.7 |
| `FillOpacity` | float | 0.5 |
| `ShowMedian` | bool | true |
| `ShowBox` | bool | true |
| `StrokeWidth` | float | 2 |

**数据：** 一行 = 一个样本；`X` 类目相同的行汇成一把小提琴，`Color` 只给它上色（不会拆分分组）。
单行、或组内取值全部相等的一组没有密度轮廓：会画一条最小可见的线并告警。其余情况轮廓是对样本做高斯核密度
估计后在 `BinCount` 个网格点上的采样，中位数画成一个圆点（`ChartTheme.ViolinMedianDotColor` /
`ChartTheme.ViolinMedianDotRadius`）。见 [图表类型](chart-types.cn.md#小提琴图--violinmark)。

### TreemapMark

`LayoutMode` 取 `TreemapLayoutMode`：`BinarySplit`（默认：快，但不优化长宽比）或 `Squarify`
（沿较短边铺一行，格子更接近正方形）。

| 属性 | 类型 | 默认值 |
|------|------|--------|
| `CellGap` | float | 2 |
| `CornerRadius` | float | 3 |
| `LayoutMode` | `TreemapLayoutMode` | `BinarySplit` |
| `ParentField` | string | `"parent"` |
| `GroupHeaderHeight` | float | 16 |
| `SiblingShadeStep` | float | 0.12 |
| `ShowLabel` | bool | true |
| `MaxNodes` | int | 65536 | 节点预算：超出的行不画并告警 |

`ParentField` 把行连成树：分组占一个矩形，子节点瓜分标签条（`GroupHeaderHeight`）之外剩下的区域。
父节点未知、指向自身或处于父子环中的行会被提升到顶层。

**数据：** 一行 = 一个节点矩形：`X` 标签、`Y` 数值（钳制到 >= 0），以及可选父键——按名读取，属性
`ParentField`（默认 `"parent"`）。自身值不为正的分组取子节点之和；处于父子环中的行会被提升到顶层。
超过 `MaxNodes`（65536）的行不画，并给出一次 warning。
见 [图表类型](chart-types.cn.md#矩形树图--treemapmark)。

### SunburstMark

| 属性 | 类型 | 默认值 |
|------|------|--------|
| `ParentField` | string | "parent" |
| `RadiusFactor` | float | 0.9 |
| `InnerRadiusRatio` | float | 0.15 |
| `RingGap` | float | 2 |
| `ArcGap` | float | 0.02 |
| `DepthShadeStep` | float | 0.18 | 每环的明暗步长：越深的环按此系数变暗（0 = 每个分支一个纯色） |
| `ShowLabel` | bool | true |

**数据：** 一行 = 一段圆环：`X` 标签、`Y` 数值，以及可选父标签——按名读取，属性 `ParentField`
（默认 `"parent"`）。自身值不为正的分组取子节点之和；同一父节点下同名标签的行会互相覆盖。
见 [图表类型](chart-types.cn.md#旭日图--sunburstmark)。

### SankeyMark

| 属性 | 类型 | 默认值 |
|------|------|--------|
| `SourceField` | string | "source" |
| `TargetField` | string | "target" |
| `NodeWidth` | float | 16 |
| `ColumnGap` | float | 0.3 |
| `NodeGap` | float | 8 |
| `FlowOpacity` | float | 0.35 |
| `ShowLabel` | bool | true |
| `MaxNodes` | int | 65536 | 节点预算：超出的行不画并告警 |

`ColumnGap` 是 [0, 1] 的列紧凑度：0 表示各列在整幅宽度上均匀铺开，1 表示各列紧贴最右侧的最后一列；最后一列始终对齐绘图区右边缘。

**数据：** 一行 = 一条流：`source` 与 `target` 按名读取（`SourceField` / `TargetField`，默认 `"source"` /
`"target"`），`Y` 是权重，字段缺失时为 1。非正流量会被丢弃（零流量分支画不出来），自环静默丢弃。
超过 `MaxNodes`（65536）的行不画，并给出一次 warning。
见 [图表类型](chart-types.cn.md#桑基图--sankeymark)。

### ChordMark

| 属性 | 类型 | 默认值 |
|------|------|--------|
| `SourceField` | string | "source" |
| `TargetField` | string | "target" |
| `ArcWidthRatio` | float | 0.06 |
| `ArcGap` | float | 0.04 |
| `ChordOpacity` | float | 0.4 |
| `RadiusFactor` | float | 0.85 |
| `ShowLabel` | bool | true |

**数据：** 一行 = 一条弦：`source` 与 `target` 按名读取（`SourceField` / `TargetField`，默认 `"source"` /
`"target"`），`Y` 是权重，字段缺失时为 1。节点角度覆盖其进出弦之和；自环不过滤（会被计两次）。
见 [图表类型](chart-types.cn.md#弦图--chordmark)。

### RangeAreaMark

| 属性 | 类型 | 默认值 |
|------|------|--------|
| `LowerField` | string | "lower" |
| `FillOpacity` | float | 0.3 |
| `ShowBorderLines` | bool | true |
| `StrokeWidth` | float | 2 |
| `Smooth` | bool | false |

**数据：** 一行 = 区间带上的一个片段：`X` 位置、`Y` 上界（必须是线性刻度），以及按名读取的 `lower`——
属性 `LowerField`（默认 `"lower"`）。整根 mark 只画**一条**带，`Color` 只给它取色（取第一行）；
这里的 `Opacity` 无效，请用 `FillOpacity`。见 [图表类型](chart-types.cn.md#范围面积图--rangeareamark)。

### TimelineMark

| 属性 | 类型 | 默认值 |
|------|------|--------|
| `StartField` | string | "start" |
| `EndField` | string | "end" |
| `BarHeightRatio` | float | 0.6 |
| `CornerRadius` | float | 3 |
| `ShowLabel` | bool | false |

**数据：** 一行 = 一个区间：类目刻度所在的通道（`Y`，或 `X` 配类目刻度）是类别，另一个通道在数值刻度上
承载区间，`start` / `end` 按名读取（`StartField` / `EndField`，默认 `"start"` / `"end"`）；`end < start` 的行会把
两端交换后绘制并告警一次，长度为零的区间（`end == start`）不画出任何柱子。
见 [图表类型](chart-types.cn.md#时间轴--timelinemark)。

### LollipopMark

| 属性 | 类型 | 默认值 |
|------|------|--------|
| `DotRadius` | float | 5 |
| `StemWidth` | float | 2 |
| `Orientation` | BarOrientation | Vertical |

**数据：** 一行 = 一根茎加一个圆点：`X` 类目（类目刻度）与 `Y` 数值都必需——即使某个方向只用到其中一个——
可选 `Color` / `Opacity`（不分组），`Shape` 决定圆点的符号。见 [图表类型](chart-types.cn.md#棒棒糖图--lollipopmark)。

### MilestoneMark

| 属性 | 类型 | 默认值 |
|------|------|--------|
| `LabelField` | string | `"label"` |
| `MarkerRadius` | float | 6 |
| `MarkerShape` | `ShapeKind` | `Circle` |
| `ShowAxisLine` | bool | true |
| `AxisLineWidth` | float | 1 |
| `LabelOffset` | float | 8 |
| `AlternateLabels` | bool | true |
| `ShowLabel` | bool | true |

**数据：** 一行 = 一个事件：`X` 是事件在轴上的位置（数值，真实日期用 `TimeScale`），可选的 `Y` 是泳道
（`OrdinalScale`——第一类目在下方，与 `TimelineMark` 一致）。`Color` 与 `Shape` 是可选标记样式；标签依次取
`Label` 通道、`LabelField`（默认 `"label"`）、X 值。这里**不绑**数值通道，所以事件不需要数值轴；没有泳道时所有事件都在
中线上，每条泳道只画一条线。位置缺失或非有限的行会被跳过。见 [图表类型](chart-types.cn.md#里程碑图--milestonemark)。

### WaffleMark

| 属性 | 类型 | 默认值 |
|------|------|--------|
| `TotalCells` | int | 100 |
| `Columns` | int | 10 |
| `CellGap` | float | 2 |
| `CornerRadius` | float | 3 |

**数据：** 一行 = 一个类别，不是一个格子：`Y` 是权重（值 <= 0 或缺值的行被丢弃），`TotalCells` 个格子
（默认 100）按最大余数法分配；`X` 只用于悬停提示，`Color` 决定格子颜色（不绑颜色时每个格子都用 mark 的默认色，看不出分配比例）。
这个 mark 声明自己**不用坐标轴**（`UsesAxes`），所以只含华夫饼的图既不画坐标轴也不画网格；与柱状图等混用时这些装饰才会回来。
见 [图表类型](chart-types.cn.md#华夫饼图--wafflemark)。

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
    public bool IncludeZero { get; init; }  // default: true
    public NiceTickResult NiceTicks { get; }        // 当前域的刻度
    public void SetDomain(double min, double max);  // 锁定域（跳过自动适配）
}
```

### OrdinalScale

```csharp
public class OrdinalScale : OrdinalScaleBase      // the shared base owns Domain and IndexOf
{
    public IReadOnlyList<string> Domain { get; }
    public int IndexOf(string key);   // 键不在域里时返回 -1
}
```

### LogScale

```csharp
public class LogScale : IScale
{
    public LogScale();                        // 默认域 1..100
    public LogScale(double min, double max);
    public double Min { get; }
    public double Max { get; }
}
```

### ColorScale

消费方一律只测**接口**，从不测具体类：`IColorScale` 有 `Color MapColor(object value)`（原值 → 颜色），
`ICategoricalColorScale : IColorScale` 再加上 `IReadOnlyList<string> Domain`——只有实现了分类接口的色阶才能驱动图例，
因为图例需要有序的类目键。下面每个内置颜色标度都实现了其中之一，自定义颜色标度也只需要实现它们。

```csharp
public class ColorScale : OrdinalScaleBase, ICategoricalColorScale
{
    public IReadOnlyList<string> Domain { get; }
    public Color[] Palette { get; set; }
    public Color MapColor(object value);   // 类目 → 色板颜色
}
```

### IdentityColorScale

G2 的 `identity` 色标：**值本身就是颜色**。`Color` 值原样使用，HTML 十六进制字符串（`"#rrggbb"`，
也支持 `#rgb` / `#rgba` / `#rrggbbaa`）会被解析；其它值按首次出现顺序回退到色板。它不是分类色标，
因此不会画图例。颜色通道的值**全部**是颜色时 `Chart` 会自动选用它，否则维持原来的分类 `ColorScale`。

```csharp
public class IdentityColorScale : IColorScale
{
    public Color[] Palette { get; set; }   // 非颜色值的回退色板
    public Color MapColor(object value);
}
```

### SequentialColorScale

```csharp
public class SequentialColorScale : IColorScale
{
    public double Min { get; }
    public double Max { get; }
    public Color[] Gradient { get; set; }
    public Color MapColor(object value);
}
```

`Fit` 会跳过 null、非数值与非有限值——它们算**缺失**而不是 0，所以一列里的脏值不会把渐变色阶（或时间轴）
拉到 0。发散色阶的域如果塌缩（例如全 0 数据），所有值都映射到中性的 `MidColor`。

### DivergingColorScale

```csharp
public class DivergingColorScale : IColorScale
{
    public DivergingColorScale();
    public DivergingColorScale(double min, double max);
    public double Min { get; }
    public double Max { get; }
    public double MidPoint { get; init; }      // default: 0
    public bool Symmetric { get; init; }       // default: true（域围绕 MidPoint 取对称）
    public Color NegativeColor { get; }  // 蓝
    public Color MidColor { get; }       // 近白
    public Color PositiveColor { get; }  // 红
    public Color MapColor(object value);
}
```

### BandScale

```csharp
public class BandScale : OrdinalScaleBase
{
    public IReadOnlyList<string> Domain { get; }
    public int SubBandCount { get; set; }       // default: 1
    public float Padding { get; init; }          // default: 0.2
    public float InnerPadding { get; init; }     // default: 0.1
    public double BandWidth { get; }
    public double SubBandWidth { get; }
    public double MapSubBand(object value, int subIndex);
}
```

### RadialScale

```csharp
public class RadialScale : OrdinalScaleBase;
```

供自定义 mark 把值映射到半径；没有内置 mark 消费它（极坐标 mark 自己算几何）。

### TimeScale

```csharp
public class TimeScale : IScale
{
    public DateTime Min { get; }
    public DateTime Max { get; }
    public bool Clamp { get; init; }  // default: true
}
```

### ShapeScale

```csharp
public class ShapeScale : OrdinalScaleBase, ICategoricalShapeScale
{
    public IReadOnlyList<string> Domain { get; }
    public ShapeKind[] Shapes { get; set; }          // 默认 ShapeScale.DefaultShapes
    public static ShapeKind[] DefaultShapes { get; } // Circle, Square, Triangle, Diamond, Cross, Star
    public ShapeKind MapShape(object? value);
}
```

`Channel.Shape` 通道的符号表，该通道会自动推断出 `ShapeScale`。类目按出现顺序依次取词汇表里的形状，
类目多于形状时循环；要自定义就用 `Shapes` 指定列表。缺值或未知类目映射到词汇表第一项（`Circle`）。
mark 与图例都通过 `IShapeScale` 消费该通道（图例还需要带 `Domain` 的 `ICategoricalShapeScale`）。

```csharp
public enum ShapeKind
{
    Circle,     // 实心圆（默认）
    Square,     // 正方形
    Triangle,   // 向上的三角形
    Diamond,    // 旋转 45° 的方形
    Cross,      // 十字
    Star,       // 五角星
}
```

`ShapeGeometry.Build(path, shape, cx, cy, radius)` 能把词汇表中的任意一个符号加到路径上，自定义 mark
因此可以画出与内置 mark 相同的图形。`ShapeGeometry.AddRingBand(…)` 加的是圆形 mark 填充的闭合环带
（外弧、反向内弧、闭合），`ShapeGeometry.AngleAt(index, count)` 则是符号与雷达环/序列共用的顶点角度：
第一个顶点朝上，其余顺时针。

---

### OutputRangeScale

```csharp
public class OutputRangeScale : IScale
{
    public OutputRangeScale(IScale inner, double min, double max);
    public IScale Inner { get; }
    public double OutputMin { get; }
    public double OutputMax { get; }
}
```

一个装饰器：把内层标度归一化后的 `0...1` 结果重新映射到 `min...max` —— size 与 opacity 通道就是用它把映射值
变成像素或透明度。`min` 与 `max` 传反了会自动交换；内层标度给出非有限值（表示"没有位置"）时结果仍是 `NaN`
（放到域底会看起来像真实数据）；`Fit` 直接委托给内层标度，所以装饰器自己不持有任何域。

## AxisConfig

```csharp
public class AxisConfig
{
    public string? Title { get; init; }
    public string? Description { get; init; }
    public string? Unit { get; init; }
}
```

---

## LegendConfig

```csharp
public class LegendConfig
{
    public LegendPosition Position { get; init; }  // default: Top
    public float ItemSpacing { get; set; }        // default: 16
    public float SwatchSize { get; init; }         // default: 10
    public float Padding { get; init; }            // default: 6
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
    None,    // 连续直线
    After,   // 在新 X 处阶梯：先横线，再在新 X 处竖直
    Before,  // 在旧 X 处阶梯：先在旧 X 处竖直，再横线
    Center,  // 在中点 X 处阶梯
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
    public string Field { get; init; }    // default: "value"
    public int? BinCount { get; init; }   // Sturges' rule if null
    public double? BinWidth { get; init; } // priority over BinCount
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
    public float ExitDuration { get; init; }      // default: 0.3
    public int ElementCount { get; set; }        // default: -1（设一次，之后 ShouldAnimate() 读它）

    // Methods
    public bool ShouldAnimate(int elementCount = -1);
    public void StartEntry(Node owner, int seriesCount, int totalElementCount = -1);
    public void AnimateHover(Node owner, float targetScale);
    public void StartDataTransition(Node owner, float duration = 0.4f);
    public void StartExit(Node owner, Action? onComplete = null);
    public void Reset();
    public void Dispose();
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
    public float[] SeriesProgress { get; init; }        // 每系列的入场进度（空 = 不错峰）

    public static AnimationContext Default { get; }
}
```

`EntryProgress` 是 `init` 而不是 `set`：`AnimationContext` 构造完就固定，所有值都要用对象初始化器一起给——
之后再写 `ctx.EntryProgress = 0.5f` 已无法编译。`Default` 是共享且不可变的「全关动画」实例，不要往里写。

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
    public IReadOnlyList<TooltipLine>? TooltipLines { get; set; }
}
```

`FocusedSeries` 与 `TooltipLines` 是命中之后由交互层填上的：聚焦的系列键（点击图例），以及命中 mark 的富文本
tooltip 内容——它存在时优先于 `Label`。

### ChartClickEventArgs

```csharp
public class ChartClickEventArgs : EventArgs
{
    public DataRow? Row { get; init; }
    public int RowIndex { get; init; }
    public string? MarkType { get; init; }
    public Vector2 ScreenPosition { get; init; }
    public string? SeriesKey { get; init; }
    public MouseButton Button { get; init; } = MouseButton.Left;
}
```

### ChartInteraction (静态工具)

```csharp
public static class ChartInteraction
{
    public static void DrawCrosshair(ICanvas2D canvas, Vector2 mousePos, PlotArea plot, ChartTheme? theme = null);
    public static HitResult? TestAll(List<Mark> marks, MarkContext ctx, Vector2 mousePos,
                                     IReadOnlySet<Mark>? skipped = null);
}
```

---

## 工具提示

### TooltipRenderer

```csharp
public class TooltipRenderer
{
    public float SmoothSpeed { get; }   // default: 12
    public float FadeSpeed { get; }     // default: 8
    public bool IsVisible { get; }
    public TooltipOptions Options { get; }
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
    public float? BorderWidth { get; set; }      // null = 主题 TooltipBorderWidth（1）
    public float? CornerRadius { get; set; }     // null = 主题 TooltipCornerRadius（6）
    public float? Padding { get; set; }          // null = 主题 TooltipPadding（8）
    public float? FontSize { get; set; }         // null = 主题 TooltipFontSize（12）
}
```

每个 `null` 都回落主题：`BackgroundColor`、`TextColor`、`BorderColor` 分别落到
`TooltipBackground` / `TooltipTextColor` / `TooltipBorderColor`，上面那四个度量落到
`TooltipBorderWidth` / `TooltipCornerRadius` / `TooltipPadding` / `TooltipFontSize`。

### TooltipLine & TooltipSpan

```csharp
public struct TooltipLine
{
    public TooltipSpan[] Spans { get; init; }
    public static TooltipLine Plain(string text);
    public static TooltipLine WithIcon(TooltipIcon icon, Color color, string text);
}

public readonly struct TooltipSpan
{
    public string Text { get; init; }
    public Color? Color { get; init; }
    public bool Bold { get; init; }
    public bool Italic { get; init; }
    public float? FontSize { get; init; }
    public TextDecoration Decoration { get; init; }
    public float LetterSpacing { get; init; }         // Skia 后端会忽略它（绘制与测量都不生效）
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
    public static ChartTheme Default { get; }   // 共享的兜底主题；视为只读（要改先 Clone）
    public ChartTheme Clone();

    // Static palettes (read-only by copy: every read returns a fresh array,
    // so writing into the result changes nothing)
    public static Color[] DefaultPalette { get; }
    public static Color[] DefaultSequentialGradient { get; }

    // 107 个 [Export] 属性，分为 25 个分组：
    // Color Palette、Chart Frame、Layout、Typography、
    // Mark Defaults、Selection & Hover、Polar / Segment、
    // Tooltip、Crosshair、Legend、Line Mark、Point Mark、
    // Radar Mark、Box Mark、Violin Mark、Gauge Mark、
    // Sankey Mark、Candlestick Mark、Feature Toggles、
    // Lollipop Mark、Range Area Mark、Chord Mark、
    // Sunburst Mark、Treemap Mark、Hit Test
}
```

主题属性的分组概览见 [定制与主题](customization.cn.md#主题属性分组)——Inspector 会按这些分组名列出全部属性。

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
    IImageHandle CaptureRegion(int x, int y, int width, int height);   // 读回表面上的一块区域

    void Save();
    void Restore();
    IDisposable SaveScope();
    void Translate(float x, float y);
    void Scale(float sx, float sy);
    void Rotate(float angle);
    void ClipRect(float x, float y, float w, float h);

    void Tick();
    TextMetrics MeasureText(string text, FontSettings font);
    Texture2D? Texture { get; }          // 画布渲染到的那张表面（没有表面时为 null）
    CanvasCapabilities Capabilities { get; }
}
```

`Texture` 是画布随处可用的关键：画布本身从不接触场景树。用 `Sprite2D`、`TextureRect` 或任意
`CanvasItem` 呈现这张表面，或者交给 `Canvas2DControl` 托管。

`LoadImage(int, int, byte[])`：缓冲区为 `null` 抛 `ArgumentNullException`，尺寸非正抛
`ArgumentOutOfRangeException`，缓冲区不足以容纳该尺寸抛 `ArgumentException`——不会静默截断；而缓冲区
**过长是允许的**，只读取前 `width * height * 4` 字节。像素按 RGBA8、**预乘 alpha** 解释，这与
`LoadImage(Texture2D)` 不同（后者会把纹理转成 RGBA8、直通 alpha）。`SaveScope()` 的 `Dispose`
是幂等的，重复释放无害。

`CaptureRegion` 把表面的一块区域拷进一个图像句柄，供「保留已渲染图层、之后再交回 `DrawImage`」的调用方使用
（`Chart.UseLayerCache` 就是这样的调用方）。像素是调用那一刻表面上的内容，矩形会按表面尺寸夹取（返回的句柄比请求的
小就意味着被裁剪了，调用方只能当作「没有可用的捕获」），句柄归调用方所有并由它释放。调用前请先看
`CanvasCapabilities.SupportsSurfaceCapture`：`Canvas2DBase` 的默认实现与 `LoadImage` 一样抛
`NotSupportedException`。

### CanvasBackendType（枚举）

```csharp
public enum CanvasBackendType
{
    Skia,    // SkiaSharp 后端（当前交付的实现）
    Godot,   // Godot 原生矢量 API（保留，未实现）
    Vello,   // GPU 计算后端（保留，长期）
    Auto,    // 选择可用的最佳后端（当前恒为 Skia）
}
```

`Canvas2DFactory.Create` 与 `Canvas2DControl.Backend` 都用它。目前只有 `Skia` 实现：请求 `Godot`、`Vello`
或 `Auto` 都由 Skia 承接，其中两个保留后端还会打一条 warning，让回退可见而不是静默发生。

### Canvas2DBase

```csharp
public abstract class Canvas2DBase : ICanvas2D
{
    // 后端只需实现这些；其余都是共享的便捷默认实现。
    public abstract void Resize(int width, int height);
    public abstract void BeginFrame();
    public abstract void EndFrame();
    public abstract void Clear(Color color);
    public abstract IPath2D CreatePath();
    public abstract IPaint2D CreatePaint();
    public abstract void Stroke(IPath2D path, IPaint2D paint);
    public abstract void Fill(IPath2D path, IPaint2D paint);
    public abstract TextMetrics MeasureText(string text, FontSettings font);
    public abstract CanvasCapabilities Capabilities { get; }

    protected Transform2D CurrentTransform { get; set; }   // 由 Save / Restore 维护

    // 后端自己做变换时返回 false：基类就不再维护自己的变换栈。
    protected virtual bool MirrorsTransformsInBase => true;
}
```

自定义后端派生自 `Canvas2DBase` 并补上这些成员即可；基类在它们之上提供了
`DrawLine` / `DrawRect` / `DrawCircle` / `DrawImage`、保存-恢复栈、变换辅助与裁剪入口。`Tick()`、
`DrawImage` 与 `Texture` 是 virtual、默认空实现 / null，所以一个最小后端只需实现抽象清单。`DrawText`
是个例外：默认实现在 DEBUG 下抛 `NotImplementedException`，Release 下只打一次 `GD.PushError`；`LoadImage`
与 `CaptureRegion` 在未覆写前都会抛 `NotSupportedException`（并报出对应的能力位 false）。

### 绘图基础类型

绘图表面使用的小型值类型：

```csharp
[Flags] public enum TextDecoration { None = 0, Underline = 1, Strikethrough = 2 }

public enum LineCap  { Butt, Round, Square }
public enum LineJoin { Miter, Round, Bevel }

public readonly struct GradientStop
{
    public float Position { get; }   // [0, 1]
    public Color Color { get; }
    public GradientStop(float pos, Color color);
}

public interface IImageHandle : IDisposable
{
    int Width { get; }
    int Height { get; }
}

public readonly record struct TextMetrics(float Width, float Height);

// CanvasCapabilities：后端能做到什么
public record CanvasCapabilities(
    bool SupportsGradients,
    bool SupportsClipping,
    bool SupportsTransforms,
    bool IsGpuBacked,
    bool SupportsLineDash,
    bool SupportsImages = false,
    bool SupportsSurfaceCapture = false);
```

`ICanvas2D.Capabilities` 返回的 `CanvasCapabilities` 让渲染器按后端**实际能力**分支——渐变、裁剪、仿射变换、
虚线、位图、GPU、表面读回——而不是假定 Skia；`LoadImage` 返回 `IImageHandle`（随该帧释放），`MeasureText` 返回
`TextMetrics`。`SupportsSurfaceCapture` 是分层渲染保留图层之前要检查的能力位（见[分层渲染](advanced.cn.md#分层渲染)）：
Skia 后端报 true，而 `Canvas2DBase` 的 `CaptureRegion` 默认抛 `NotSupportedException`。

### DefaultRenderers

```csharp
public static class DefaultRenderers
{
    public static void DrawBackground(RenderContext ctx);
    public static void DrawTitle(RenderContext ctx);
    public static void DrawGrid(RenderContext ctx);
    public static void DrawAxes(RenderContext ctx);
    public static void DrawAxisLabels(RenderContext ctx);
    public static void DrawLegend(RenderContext ctx);
    public static void DrawCrosshair(RenderContext ctx);
}
```

这些就是图表默认装进各渲染槽位的函数。把其中一个赋给槽位即可保留内置观感，或用 lambda 包一层，先调用它
再画自己的东西。

### Canvas2DFactory / Canvas2DControl

```csharp
// 给定像素尺寸的一张画布；它渲染到 ICanvas2D.Texture。
public static ICanvas2D Create(int width, int height,
                               CanvasBackendType backend = CanvasBackendType.Auto);

// 托管画布的 Control：创建画布、跟随节点尺寸、跑帧循环
// （tick -> 可选清屏 -> 绘制 -> 提交）并在 _Draw 里呈现纹理。
public partial class Canvas2DControl : Control
{
    public CanvasBackendType Backend { get; set; }      // 默认 Auto
    public bool AutoResize { get; set; }                // 默认 true：表面 = 节点尺寸
    public bool ClearBeforeDraw { get; set; }           // 默认 true
    public Color BackgroundColor { get; set; }          // ClearBeforeDraw 用它清屏
    public bool StretchToNodeSize { get; set; }         // 默认 true
    public bool OwnsCanvas { get; set; }                // 默认 true：随节点一起释放
    public bool LayeredRendering { get; set; }          // 默认 false：组件可以缓存一层
    public Func<int, int, ICanvas2D>? CanvasFactory { get; set; } // 自定义 / 注入的画布

    public ICanvas2D? Canvas { get; }
    public Texture2D? Texture { get; }
    public Vector2I CanvasSize { get; }
    public bool IsReady { get; }

    public event Action<Canvas2DControl, ICanvas2D>? CanvasDraw;  // 绘制回调
    public void Invalidate();                                     // 下一帧重绘
    public void ResizeCanvas(Vector2I size);                      // 固定分辨率
    protected virtual void OnCanvasDraw(ICanvas2D canvas);        // 或覆写它
}
```

Skia 后端对表面的生命周期是一次性的：`SkiaCanvas2DBackend.Initialize` 二次调用、或在画布释放后调用都会
抛异常——要改活动画布的尺寸请用 `Resize`；`SkiaTexture` 在未初始化或已释放时抛 `ObjectDisposedException`，
所以需要「可能还没有表面」的语义时请读可空的 `ICanvas2D.Texture`。

`CanvasFactory` 在节点进树时读取，因此请在把节点加进场景树**之前**设置它——之后再赋值，要等节点重新进树
才会生效。绘制回调抛异常不会拖垮节点：`EndFrame` 仍在 `finally` 中执行，错误会被上报，且该帧保持脏状态，
下一次 `_Process` 会重试。

`ResizeCanvas` 会把两个维度都夹到至少 1；而 `AutoResize = false` 时首个表面仍按节点
尺寸创建，只有之后的 `ResizeCanvas` 调用才会切到别的分辨率。

`LayeredRendering` 是给画布上那个组件看的提示性开关：它表示组件可以把"与指针无关的那部分帧"存成一张图
（`ChartView.LayeredRendering` 会把自己的值镜像过来）。控件本身不改变绘制方式；它检查这种缓存的前提——
每帧重绘前表面必须被清屏（`ClearBeforeDraw`，默认开）——不满足时只上报一次。

引擎没有渲染设备时（`--headless` 运行、或 `--rendering-driver dummy`），`Canvas2DFactory.Create` 会抛
`InvalidOperationException`；`Canvas2DControl` 会捕获它、打一条 warning 并保持节点可用，让宿主自行绘制占位内容。

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

`ArcTo` 会把角度按整圈取模，因此恰好 `2π` 的扫掠会塌缩成空弧——要画整圆请传 `2π - ε`（或直接用 `Circle`）。

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

`CreatePath()` 与 `CreatePaint()` 发放的是**池化对象**：返回的实例会被复用而非全新，因此每一个都必须
Dispose，且不得跨帧持有——回收后的对象会回到池里、再交给另一位调用方。`IPaint2D` 的状态是**粘性**的，
每个依赖的属性都要自己设，不要假设是全新默认值。

### FontSettings

```csharp
public readonly record struct FontSettings
{
    public float Size { get; init; }                  // default: 13
    public string? Family { get; init; }
    public bool Bold { get; init; }
    public bool Italic { get; init; }
    public float LetterSpacing { get; init; }         // Skia 后端会忽略它（绘制与测量都不生效）
    public float LineHeightMultiplier { get; init; }  // default: 1.2
    public TextAlign Align { get; init; }
    public TextDecoration Decoration { get; init; }
    public Font? GodotFont { get; init; }

    public static FontSettings Default { get; }
}

public enum TextAlign { Left, Center, Right }
```
