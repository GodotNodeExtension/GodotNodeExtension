[英文](advanced.md) | **中文**

# 高级功能

本文档涵盖 GodotChart 的动画系统、用户交互、工具提示、实时数据流和数据变换等高级特性。

---

## 动画系统

GodotChart 提供完整的动画管线：入场动画、数据过渡、悬停动画和退场动画。

### AnimationController — 动画控制器

`AnimationController` 主管所有动画状态：

```csharp compile-members
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

```csharp compile
// Start entry animation (call once)
_anim.StartEntry(host, seriesCount: 3, totalElementCount: 100);

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

通过 `SeriesStagger` 让多系列依次出现——示例把默认的 `0.1`（100ms）改成了 `0.15`（150ms）：

```csharp compile
_anim.SeriesStagger = 0.15f;  // 150ms per series delay
_anim.StartEntry(host, seriesCount: 3);

// Each series gets independent progress:
// _anim.SeriesProgress[0], [1], [2] stagger over time
```

没有任何内置 mark 消费 `SeriesProgress`：内置 mark 全部只读一维的 `EntryProgress`，所以上面这个错峰数组
只有读 `ctx.Animation.SeriesProgress` 的自定义 mark 才会用到。

### 数据过渡

> **尚未接线：** `AnimationContext.DataTransitionProgress` 已提供，`AnimationController.StartDataTransition` 也会驱动它，但**没有任何内置 Mark 消费这个值**——当前数据变化是瞬时生效的。如果你需要补间动画，可以在自定义 Mark 中读取该进度值。
当数据更新时，可触发平滑过渡：

```csharp compile
_anim.StartDataTransition(host, duration: 0.4f);

// In Render loop:
var ctx = new AnimationContext
{
    DataTransitionProgress = _anim.DataTransitionProgress,
};
chart.Animate(ctx).Render();
```

### 悬停动画

响应鼠标悬停的缩放效果：

```csharp compile
// On hover event:
_anim.AnimateHover(host, targetScale: 1.05f);

var ctx = new AnimationContext
{
    HoverScale = _anim.HoverScale,
};
```

### 退场动画

```csharp compile
_anim.StartExit(host, onComplete: () =>
{
    GD.Print("Exit animation complete!");
    // Clean up or switch chart
});
```

### AnimationContext — 简化 API

也可以直接使用 `float` 进度传入（仅入场动画）：

```csharp compile
chart.Animate(0.5f).Render();  // 50% entry progress
```

或使用 `AnimationContext.Default` 跳过所有动画。

### 缓动函数

```csharp compile-members
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
```

```csharp compile
float eased = Ease.Apply(t, EaseType.EaseOutCubic);
```

## 滚动数据与稳定的坐标轴

如果数据流每追加一行就重拟合一次数值轴，刻度、网格线、比例都会跟着每根新数据跳 —— 读者看到画面在动，而数据其实
没有动。有四种办法可以止住它，从最强到最弱：

1. **钉住域**：`ChartView.YAxisRange = Vector2(min, max)`（场景里）或 `Chart.ScaleDomain(Channel.Y, min, max)`
   （代码里）。锁定会在每次拟合之后重新生效，刻度与网格因此固定不动。数据方通常对合理区间有**预先认知**；
   落在区间外的数据**不画**（图表绝不会为它拉长轴），这正是"超出我们约定范围"的诚实呈现；
2. **钉住刻度位置**：`AxisConfig.TickStep = 20`（或 `AxisConfig.Ticks` 给显式列表）。轴仍随数据走，但刻度的
   间距与取值保持稳定 —— 行情类页面的常用选择：价格的绝对水平会漂，而读者要的是一把稳定的尺子；
3. **钉住个数**：`AxisConfig.TickCount = 6`。标签数量不再变化；位置仍会移动。

4. **让轴跟随，但要"粘"**：`AxisConfig.AutoScaleMargin`（占域宽的比例）在数据仍落在边距内时**保持当前域不动**，
   只有越界才重新拟合 —— 短期形状稳定可读、真的大波动照样跟随、刻度不再抖。`AxisConfig.NiceDomain` 把重拟合后的域
   向外扩整到 `{1, 2, 5} × 10ⁿ` 台阶，于是轴是**跳档**变化而不是逐像素漂移。想要"封住上限、下端自由"就用
   `AxisConfig.MaxLimit`（或 `MinLimit`）**只钉一端**。这几条在 `ChartView` 上都有导出（`YAxisAutoScaleMargin`、
   `YAxisNiceDomain`、`YAxisMaxLimit`、`YAxisMinLimit`）；滚动窗口示例的价格图用的就是粘性自动缩放，而区间已知的图
   （示波器、仪表）则直接钉住域。

滚动窗口示例给示波器钉住域 (1)，而它的价格图用的是粘性自动缩放 (4)。这四类旋钮在 `ChartView` 上都有同名导出
（`XAxisRange` / `YAxisRange`、`XAxisTickStep` / `YAxisTickStep`、`XAxisTickCount` / `YAxisTickCount`，以及
`YAxisAutoScaleMargin` / `YAxisNiceDomain` / `YAxisMinLimit` / `YAxisMaxLimit`），所以页面不写代码也能配出来。

## 图表什么时候重绘

图表**不会自己重绘**：宿主在"它画的东西变了"时调用视图的 `Invalidate()`，一帧内多次调用只花一次重绘。`ChartView`
的各个 setter 已经替你做了这件事（每个都先比对再失效），手搓页则在自身动画进行中才调用 —— 所以只要页面不在播动画、
也不在被交互，**无论数据表多大，每帧的成本都是零**。`Repaint()` 强制重绘一帧，`Refresh()` 重建 mark。

## 缺失值

某一行的值缺失时，该点会被**跳过**：不画这个点，折线从缺口前最后一个点直接连到缺口后第一个点。hover、选中与标签
同样跳过它，因此不会漂到相邻点上。若某个序列必须**把缺口画成缺口**（传感器掉线、当天没有成交），就把它拆成两个序列，
或改用不连线的那种 mark。

## 参考线与参考带

读者常常需要一条用来对照的线：目标值、阈值、去年的平均。这是独立的 mark `SectionMark`，属于**注释**而不是数据——
线值通过它声明的轴映射，不参与任何标度（远在表外的线值不会把轴拉长），也不会出现在图例里。缩放与平移时线随数据移动，
落在可见窗口之外的线会被**跳过**，而不是钉在边缘上。

```csharp compile
new Chart(canvas)
    .Data(monthlySales)
    .Mark(new IntervalMark())
    .Encode(Channel.X, "month")
    .Encode(Channel.Y, "revenue")
    // 目标线，外加一条可接受区间带。两者默认都读 Channel.Y；Channel.Y2 则贴着右轴，
    // Channel.X 画出垂直线。
    .Mark(new SectionMark
    {
        Levels = [1500, 2000],
        BandFrom = 1200,
        BandTo = 2600,
        Color = new Color(1f, 0.78f, 0.35f, 0.9f),
        LabelFormat = "{0:N0}",          // 不设置时用基类的 "{0}"（即数值本身）；显式传 null 或空串都不标标签
        Dashed = true,
    })
    .Render();
```

`ChartView` 节点**不用写代码**也能画同样的线：`SectionLevels`（一组线值）、`SectionBandFrom` /
`SectionBandTo`、`SectionTarget`（`Y` / `Y2` / `X`）、`SectionColor`、`SectionDashed`、`SectionLabelFormat`
都是导出属性，页面和其它部分一样在场景里配置。

## 导出图表

`ChartView.SavePng(path)` 与 `Canvas2DControl.SavePng(path)` 把视图**当前显示的画面**写成 PNG，正是工具、构建脚本
或文档配图需要的。它保存的就是图表已经在画的那块表面：`Chart` 在构造时就绑定了自己的画布，所以"把同一张图表按另一个
尺寸重渲"其实是另一张图表。没有渲染设备的运行没有表面，此时该调用返回 `false`，不抛异常。

## 性能与大数据

当表格远大于绘图区能分辨的粒度时，不必逐点绘制。折线与面积序列默认就会缩减要画的点：`LineMark.Decimate =
DecimateMode.Auto` 下，**每个像素列只贡献最低点与最高点**，于是路径长度与画布成正比、而不是与表格成正比；与
"等间隔抽样"不同的是**极值被保留**，尖峰不会被跨过去。`DecimatePointsPerPixel`（默认 2）是开始缩减的密度，
`DecimateMode.Off` 画出每一个点，`On` 即使在绘图区显示得下时也缩减。抽稀只影响绘制：命中测试、tooltip 与
`GetRenderDataSnapshot()` 仍然读全量行，使用者看到的值始终是真实值。

坐标轴刻度跟随它自己的长度：**X 轴**每 `TickLabelSpacing` 像素一个标签（Y 轴改按标签行高计算），短轴因此得到
更少的标签、而不是挤成一列；数量上限是 `MaxTickCount`，在装得下的前提下不低于 `MinTickCount`（默认 6——地方很挤时
可以降到 2）。步长随轴变长按**整倍数**细化——10、5、2、1。
`AxisConfig.TickStep` 固定步长、`AxisConfig.TickCount` 固定个数、`AxisConfig.Ticks` 精确给出要画的那些值；
刻度永远落在数据里出现过的值上。

在把慢归咎于图表之前，先知道表格本身的成本：`DataRow` 是个小字典，每行常驻约 290 字节，一百万行在图表还没画
之前就是约 280 MB。示例浏览器里的 Layered rendering 页把"绘制耗时 / 帧时间 / 每行字节数"都量出来（1k 到 200k 行），
并且可以开关平滑与入场动画，看看各自花掉多少。桌面 GPU 上通过该页实测（60 帧热态）：10 万个点的平滑折线，`DecimateMode.Off` 时每帧
72–76 ms，默认 `Auto` 时 **22 ms**；20 万个点分别是 **189 ms** 与 **37 ms**。屏幕点也按系列做了缓存：布局、数据、
绘图区都没变时，重绘直接复用上一帧采集的点表（指针与动画进度不会让点移动）——静态大图只为它的表格付一次映射成本，
而不是每帧一次。

## 分层渲染

一帧其实有两层：**数据层**（背景、标题、网格、轴、轴标签、图例，以及 mark 不含交互态的那部分）与**覆盖层**
（mark 的 hover/选中视觉，加上准星）。跟着指针走的只有覆盖层，所以数据层可以存成一张图，在"没有任何输入变化"的帧上
直接贴回来：

```csharp compile
// 把数据层存成一张图：之后移动指针只花覆盖层。
chart.UseLayerCache = true;
chart.Render();
```

`ChartView` 节点上有同一个开关 `LayeredRendering`（默认关），因此页面可以像其它部分一样在场景里配置。

**收益。** 移动指针不再为数据层付钱：大折线上，hover 帧从"映射整张表 + 重建路径"降到"画一个标记"。
**代价。** 多占一张图表矩形的图（`宽 × 高 × 4` 字节：830×520 约 1.7 MB，4K 约 33 MB）、每次重建多一次表面读回，
以及"宿主每帧清屏"这个前提（`Canvas2DControl` 默认 `ClearBeforeDraw` 就是开着的）。

以下任一输入变化都会重建图层：数据、布局、绘图区、主题、动画进度、聚焦系列、图例配置。hover 与选中**永远不重建** ——
那正是覆盖层存在的理由。直接改 mark 自身的设置（`mark.StrokeWidth = 2f`）是图表观察不到的输入，若希望图层跟上，
改完调用 `Chart.InvalidateLayerCache()`。

**什么时候不会启用。** 只有当画布后端能读回自己的表面（`CanvasCapabilities.SupportsSurfaceCapture`）**并且**图上每个
mark 都把交互态画在覆盖层（`Mark.InteractionStateInOverlay`）时，图层才会被保留。内置 mark 里回答 true 的是 `LineMark`、
`PointMark`、`IntervalMark`（含堆叠）、`BoxMark`、`CandlestickMark`、`HeatmapMark`、`LollipopMark`、`MilestoneMark`、
`TimelineMark`、`WaffleMark`、`FunnelMark`、`GaugeMark`、`TreemapMark` 与 `SectionMark`（注解 mark，没有自己的交互态，覆盖层保持为空）。有七个 mark 是**刻意**回答 false 的 ——
`RangeAreaMark`、`ViolinMark`、`PieMark`、`RadarMark`、`SankeyMark`、`ChordMark`、`SunburstMark` —— 原因是它们的 hover
视觉就是元素自身**半透明**的填充（在覆盖层重画会合成两次、越描越深）、或者高亮要搬动缓存图里已经有的标签（`PieMark`）、
或者几何本身要重走整表（`RadarMark`、`SunburstMark`）；这些原因都写在各自声明的注释里。图上只要有这类 mark，就退回单遍
渲染并只提示一次原因。自定义 mark 只要把
hover/选中的画法写进 `RenderOverlay` 并回答 true 就加入了这一侧 —— `MarkContext.StateInOverlay` 用来区分两半，所以
一个 mark 完全可以在图表没开缓存时继续在 `Render` 里画交互态，这也正是"从不开这个开关的图表与它一贯的样子完全一致"的
原因。

有一个细节值得知道：覆盖层是**叠在**贴回来的图层之上的，所以有效不透明度小于 1 的 hover 元素会被合成两次（不开开关时
只画一次），看起来会比单遍路径更实（0.3 会变成 0.51，0.5 会变成 0.75）；不透明元素两者像素完全一致。也正因如此，
"交互态本身就是一层半透明填充"的 mark 一律把状态留在数据层（见上）。

流式页面不是它的目标：每帧追加一行的页面每帧都在改数据，图层会被每帧重建（并每帧读回一次）。它划算的地方是
**数据静止、指针在动**的页面。同一份数据两种渲染方式的对照（每类帧的耗时、图层占用的字节数）见示例浏览器里的
**Layered rendering** 页。

### 性能保护

当数据量超过 `AnimationThreshold`（默认 2000）时，动画自动禁用：

动画状态通过 `AnimationContext` 传入（从控制器里构建）：

```csharp compile
if (_anim.ShouldAnimate(elementCount))
{
    chart.Animate(new AnimationContext
    {
        EntryProgress = _anim.EntryProgress,
        GlobalOpacity = _anim.GlobalOpacity,
        HoverScale    = _anim.HoverScale,
    });
}
chart.Render();
```

---

## 用户交互

### HitTest — 命中测试

在鼠标位置检测用户点击/悬停到哪个数据元素：

```csharp compile
var hit = chart.HitTest(mousePosition);
if (hit?.Hit == true)
{
    DataRow? row = hit.Row;
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
| `FocusedSeries` | string? | 本次交互后聚焦的系列键（点击图例时设置） |
| `TooltipLines` | IReadOnlyList<TooltipLine>? | 命中 mark 生成的富文本 tooltip 内容；存在时优先于 `Label` |

### 选中与悬停

```csharp compile
HitResult hit = null!;
// Select a data row
chart.Select(hit.RowIndex);

// Update interaction state (hover glow, crosshair position)
chart.Interaction(mousePosition);

// Query current state
int selectedRow = chart.CurrentSelectedRowIndex;
int hoveredRow = chart.CurrentHoveredRowIndex;
string? focusedSeries = chart.CurrentFocusedSeries;
```

### 事件系统

```csharp compile
// 点击事件：ChartClickEventArgs 带上位置、Mark 类型与鼠标按键
chart.OnClick += (sender, e) =>
{
    GD.Print($"Clicked: row {e.RowIndex} of {e.MarkType} at {e.ScreenPosition}");
    // e.Row - the data row (may be null for element-less hits)
    // e.ScreenPosition - click screen position
    // e.MarkType - e.g. "IntervalMark"
};

// Selection event - ChartSelectionEventArgs reports index/row/series only
chart.OnSelectionChanged += (sender, e) =>
{
    GD.Print($"Selected: row {e.RowIndex} (was {e.PreviousRowIndex}), series={e.SeriesKey}");
};

// Hover event - ChartHoverEventArgs
chart.OnHover += (sender, e) =>
{
    if (e.RowIndex >= 0)
        GD.Print($"Hovering: row {e.RowIndex}");
};

// 聚焦事件 — ChartFocusEventArgs 报告焦点从哪个系列换到了哪个系列
chart.OnFocusChanged += (sender, e) =>
{
    GD.Print($"Focus: {e.PreviousSeriesKey} -> {e.SeriesKey}");
};

// 图例点击 — ChartLegendClickEventArgs；把 Handled 置为 true 可拦下内置的焦点切换
chart.OnLegendClick += (sender, e) =>
{
    GD.Print($"Legend click: {e.SeriesKey} (focused: {e.CurrentFocusedSeries})");
    e.Handled = true;   // 自己接管焦点逻辑
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
| `Button` | MouseButton | 产生该次点击的鼠标按键；只有 `Left` 会改变选中/聚焦 |

**ChartSelectionEventArgs 属性：**

| 属性 | 类型 | 说明 |
|------|------|------|
| `Row` | DataRow? | 数据行 |
| `RowIndex` | int | 新选中的行索引 |
| `PreviousRowIndex` | int | 之前选中的行索引 |
| `SeriesKey` | string? | 系列名 |

`ChartFocusEventArgs`（`PreviousSeriesKey` / `SeriesKey`）与 `ChartLegendClickEventArgs`
（`SeriesKey` / `CurrentFocusedSeries` / `Button` / `Handled`）同样只有几个字段，完整签名见
[API 参考](api-reference.cn.md#事件)；把 `Handled` 置为 true 可以拦下内置的聚焦切换，自己接管焦点逻辑。

### 系列聚焦

当某个系列被**聚焦**（`Chart.FocusSeries(...)` 或点击图例）时，其他系列会降低透明度，由 `ChartTheme.UnfocusedOpacity` 控制；仅仅悬停不会聚焦系列——`OnHover` 只会上报当前行。

---

## 工具提示 (Tooltip)

### 基本使用

```csharp compile-members
private TooltipRenderer _tooltip = new();
```

```csharp compile
// In _Process:
_tooltip.Theme = _theme;
_tooltip.Update((float)delta, chart.HitTest(mousePos));
_tooltip.Draw(canvas, canvasWidth, canvasHeight);
```

### TooltipOptions — 配置

```csharp compile
// Options is read-only: the instance is created with the renderer, its members are writable.
_tooltip.Options.BackgroundColor = new Color(0.1f, 0.1f, 0.15f, 0.95f);
_tooltip.Options.TextColor = Colors.White;
_tooltip.Options.BorderColor = new Color(0.3f, 0.3f, 0.4f);
_tooltip.Options.CornerRadius = 6f;
_tooltip.Options.Padding = 8f;
_tooltip.Options.FontSize = 12f;
```

### 自定义内容构建器

**简单文本模式：**

```csharp compile
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

```csharp compile
_tooltip.Options.RichContentBuilder = ctx =>
{
    return new List<TooltipLine>
    {
        TooltipLine.WithIcon(TooltipIcon.Circle, ctx.ElementColor, ctx.Label ?? ""),
        TooltipLine.Plain($"Value: {ctx.Row.Get<int>("value")}"),
    };
};
```

### TooltipLine & TooltipSpan

`TooltipLine` 支持精细控制每行的文字段落样式：

```csharp compile
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
| `Decoration` | TextDecoration | 下划线 / 删除线（默认 `None`） |
| `LetterSpacing` | float | 字符间距，单位 px（默认 0）。**Skia 后端会忽略它**——绘制与测量都不生效 |
| `Family` | string? | 字体族覆盖；null 用 tooltip 字体 |
| `GodotFont` | Font? | Godot `Font` 资源覆盖；优先于 `Family` |
| `Icon` | TooltipIcon | 图标 (None/Circle/Square/Diamond/Triangle) |
| `Image` | IImageHandle? | 嵌入图像 |

### 动画效果

Tooltip 内置平滑跟随与淡入淡出。`SmoothSpeed` 与 `FadeSpeed` 是可读属性、取固定值（12 / 8），
**不能**按 renderer 配置；要整体关闭提示气泡请用主题上的 `EnableTooltip`。

---

## 十字线 (Crosshair)

在鼠标位置绘制参考十字线：

```csharp compile
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

```csharp compile
// Add single row
chart.AppendData(new DataRow().Set("time", now).Set("value", reading));

// Add multiple rows
chart.AppendData(newBatch);
```

### WindowSize — 滚动窗口

设置滚动窗口自动裁剪旧数据：

```csharp compile
chart.WindowSize = 100;  // keep only latest 100 rows

// Combined with append for real-time streaming:
chart.AppendData(newRow);    // oldest rows auto-trimmed
chart.Render();
```

`WindowSize = 0` 表示不限制（默认）。它对 `Data()` 同样生效：整批替换的数据集也会被裁到窗口大小，因此"只保留最新 N 行"与行的到达方式无关。

### 数据过渡动画

> 详见[数据过渡](#数据过渡)的说明：进度值可用，但内置 Mark 尚未在新旧数据之间做插值。

结合 `AnimationController.StartDataTransition()` 实现数据更新时的平滑过渡：

```csharp compile
chart.Data(newData);
_anim.StartDataTransition(host, duration: 0.4f);
```

---

## 数据变换 (Transform)

### BinTransform — 分箱变换

将连续数值分箱为直方图数据：

```csharp compile
chart.Data(rawData);
chart.Transform(new BinTransform
{
    Field = "value",
    BinCount = 20,        // or use BinWidth = 5.0
});
chart.Mark(new IntervalMark());
chart.Encode(Channel.X, "BinMid");   // BinTransform 输出：BinStart、BinEnd、BinMid、Count
chart.Encode(Channel.Y, "Count");
chart.XAxis(new AxisConfig { Title = "Value" });
chart.YAxis(new AxisConfig { Title = "Frequency" });

// IntervalMark 在类目带上画柱子，所以数值型的 BinMid 列需要类目轴；而手工安装的标度永远不会被图表拟合
// （只有图表自己推断出来的才会），因此这里自己拟合分箱。不这么做的话，这一列会被自动推断成线性标度，
// 一根柱子都不会画出来。
var bins = new OrdinalScale();
var keys = new List<object>();
foreach (var row in chart.GetRenderDataSnapshot())
    if (row.TryGet<double>("BinMid", out double mid)) keys.Add(mid);
bins.Fit(keys);
chart.Scale(Channel.X, bins);
chart.Render();
```

类目轴是按 mark 读到的值的**文本**来归类目的，所以上面的轴标签是原始的分箱中点。想让标签显示成取整后的值，
就让一个变换把格式化好的标签写进单独的字段，再把 `Channel.X` 指向那个字段——Custom charts 示例页就是这么做的。

BinTransform 输出字段：
- `BinStart` — 分箱左边界
- `BinEnd` — 分箱右边界
- `BinMid` — 分箱中点
- `Count` — 数据计数

当 `BinCount` 和 `BinWidth` 均未指定时，使用 Sturges 规则自动计算。

### 自定义变换

实现 `IDataTransform` 接口创建自定义变换：

```csharp compile-class
public class MovingAverageTransform : IDataTransform
{
    public string Field { get; set; } = "value";
    public int Window { get; set; } = 5;

    public List<DataRow> Apply(List<DataRow> data)
    {
        // Your transform logic here, e.g. replace each row by its moving average.
        // The returned list is what the marks render.
        return data;
    }
}
```

用法：

```csharp compile
// Any IDataTransform is registered the same way - the custom one above, or a built-in:
chart.Transform(new BinTransform { Field = "value", BinCount = 4 });
```

变换可以链式组合，按添加顺序依次执行。

---

## 复合图表

### 多 Mark 叠加

在同一 Chart 中叠加多种 Mark：

```csharp compile
var data = new List<DataRow>
{
    new DataRow().Set("month", "A").Set("revenue", 10),
    new DataRow().Set("month", "B").Set("revenue", 20),
};
new Chart(canvas)
    .Data(data)
    .Mark(new IntervalMark())  // bars
    .Mark(new LineMark())      // line overlay
    .Encode(Channel.X, "month")
    .Encode(Channel.Y, "revenue")
    .Render();
```

### 双 Y 轴

`ChartView` 没有第二轴：双轴图用 API 搭。`Channel.Y2` 加一个 `YChannel` 指向它的 mark 就是做法——
第二个系列保留自己的编码、自己的颜色、自己的标度，这正是节点导出表达不了的部分：

```csharp compile
var rows = new List<DataRow>
{
    new DataRow().Set("month", "Jan").Set("revenue", 120).Set("cost", 80),
    new DataRow().Set("month", "Feb").Set("revenue", 180).Set("cost", 95),
};
// 左轴柱子，右轴一条自带标度和颜色的折线
new Chart(canvas)
    .Data(rows)
    .Mark(new IntervalMark())
    .Mark(new LineMark { YChannel = Channel.Y2 }.Encode(Channel.Color, Colors.Orange))
    .Encode(Channel.X, "month")
    .Encode(Channel.Y, "revenue")
    .Encode(Channel.Y2, "cost")
    .ScaleDomain(Channel.Y2, 0, 100)   // 锁住右轴
    .Render();
```

去掉颜色与标度这两处附加项，两个 mark 的完整做法就是 `Channel.Y2` 配 `LineMark.YChannel`：

```csharp compile
var data = new List<DataRow>
{
    new DataRow().Set("month", "A").Set("revenue", 10),
    new DataRow().Set("month", "B").Set("revenue", 20),
};
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
| Geographic + Geographic | Geographic + 笛卡尔 |

不兼容的 Mark 会在渲染时自动跳过并输出警告。
**首个添加的 Mark** 决定了整张图表的坐标系：后续坐标系不同的 Mark 会被跳过，因此请先添加主要 Mark。

声明 `MarkCoordinate.Geographic` 的 mark 通过坐标参照系放置数据（见[地理坐标](api-reference.cn.md#地理坐标)），
而不是通过标度，因此当一张图表的 mark 全是地理坐标时，它不画网格、不画轴、不画十字线，也不为轴标签留出空间。

---

## 系列信息查询

```csharp compile
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

```csharp compile
using var paint = canvas.CreatePaint()
    .SetColor(Colors.Red)
    .SetStrokeWidth(2f);

canvas.DrawLine(0, 0, 100, 100, paint);
canvas.DrawRect(10, 10, 50, 30, paint);
canvas.DrawCircle(200, 200, 25, paint);
canvas.DrawText("Hello", 10, 50, FontSettings.Default, paint);
```

### 路径绘制

```csharp compile
using var path = canvas.CreatePath()
    .MoveTo(0, 0)
    .LineTo(100, 50)
    .CubicTo(150, 0, 200, 100, 250, 50)
    .Close();

canvas.Fill(path, fillPaint);
canvas.Stroke(path, strokePaint);
```

### 渐变

```csharp compile
using var paint = canvas.CreatePaint()
    .SetLinearGradient(0, 0, 100, 0, new[]
    {
        new GradientStop(0f, Colors.Blue),
        new GradientStop(1f, Colors.Red),
    });
```

### 变换与裁剪

```csharp compile
IPaint2D paint = null!;
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

```csharp compile
var font = new FontSettings
{
    Size = 16f,
    Bold = true,
    Italic = false,
    Family = "Roboto",
    Align = TextAlign.Center,
    LetterSpacing = 1.5f,  // 接受但会被 Skia 后端忽略
    LineHeightMultiplier = 1.2f,
    Decoration = TextDecoration.Underline,
    GodotFont = myGodotFont,  // optional Godot Font resource
};

var metrics = canvas.MeasureText("Hello World", font);
// metrics.Width, metrics.Height
```
