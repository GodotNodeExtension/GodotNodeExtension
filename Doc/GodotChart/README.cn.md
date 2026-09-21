[English](README.md) | **中文**

# GodotChart 文档

GodotChart 是一个基于 **Grammar of Graphics** 设计理念的 Godot 声明式图表库，使用 C# 和 SkiaSharp 构建。它提供流畅的 API 来创建丰富的交互式图表：22 个 `ChartKind`（由 20 个 `Mark` 类实现，另有注解 mark `SectionMark`）、动画系统、主题定制及完整的用户交互体验。

## 图表一览

下面每张图都是组件自带基础示例（示例浏览器里的 `BasicsDemo`）中的一个 cell：每个 cell 就是一个
`ChartView` 节点，它的 `Kind`、`Rows` 与各通道字段都直接写在场景里，所以可以把某个 cell 复制进自己的
场景再改。直角坐标系的图表按横向展示，极坐标、层次与关系流按正方形展示——那正是它们的 mark 要求的形状。

### 直角坐标系

| | |
|---|---|
| ![柱状图](assets/bar.png) | ![分组柱状图](assets/grouped-bar.png) |
| ![堆叠柱状图](assets/stacked-bar.png) | ![折线图](assets/line.png) |
| ![面积图](assets/area.png) | ![堆叠面积图](assets/stacked-area.png) |
| ![气泡图](assets/bubble.png) | ![区间面积图](assets/range-area.png) |
| ![小提琴图](assets/violin.png) | ![箱线图](assets/box.png) |
| ![K 线图](assets/candlestick.png) | ![热力图](assets/heatmap.png) |
| ![华夫图](assets/waffle.png) | ![时间线](assets/timeline.png) |
| ![棒棒糖图](assets/lollipop.png) | ![里程碑图](assets/milestone.png) |
| ![参考线](assets/reference-lines.png) | |

### 极坐标

| | |
|---|---|
| ![饼图](assets/pie.png) | ![圆环图](assets/donut.png) |
| ![雷达图](assets/radar.png) | ![仪表盘](assets/gauge.png) |
| ![漏斗图](assets/funnel.png) | |

### 层次结构

| | |
|---|---|
| ![矩形树图](assets/treemap.png) | ![旭日图](assets/sunburst.png) |

### 关系流

| | |
|---|---|
| ![桑基图](assets/sankey.png) | ![和弦图](assets/chord.png) |

## 核心架构

GodotChart 采用四层架构设计：

| 层级 | 职责 | 核心类 |
|------|------|--------|
| **数据层** | 数据建模与变换 | `DataRow`, `BinTransform` |
| **编码层** | 将数据字段映射到视觉通道 | `Channel`, `FieldEncode`, `ConstantEncode` |
| **标度层** | 将数据值映射到 [0,1] 范围 | `LinearScale`, `OrdinalScale`, `LogScale` 等 |
| **标记层** | 将编码生成具体图形 | 承载类型的 20 个 `Mark` 类（共 21 个子类） |

整个图表通过 `Chart` 类以 **Fluent API** 风格组装：

```csharp compile
var rows = new List<DataRow>();
var theme = ChartTheme.Dark();

new Chart(canvas)
    .Data(rows)              // 数据
    .Mark(new IntervalMark())// 标记类型
    .Encode(Channel.X, "x") // 通道编码
    .Scale(Channel.Y, new LinearScale(0, 3000)) // 可选：默认会自动推断标度
    .Theme(theme)            // 主题
    .Render();               // 渲染
```

## 文档目录

| 文档 | 内容 |
|------|------|
| [快速入门](getting-started.cn.md) | 环境准备、第一个图表、基本概念 |
| [图表类型](chart-types.cn.md) | 22 种图表类型的详细说明与示例代码 |
| [定制与主题](customization.cn.md) | 主题系统、标度（Scale）、坐标轴、图例、自定义渲染器 |
| [高级功能](advanced.cn.md) | 动画系统、交互事件、工具提示、实时数据流、数据变换、复合图表 |
| [API 参考](api-reference.cn.md) | 所有公共类、方法、属性的完整列表 |

## 支持的图表类型一览

每一类都注明了该图表「一行代表什么」；逐字段的完整表格见 [图表类型](chart-types.cn.md#数据结构一览) 的
『数据结构一览』一节。

### 笛卡尔坐标系 (Cartesian)
- **柱状图** (IntervalMark) — 一行一根柱；垂直/水平、堆叠
- **折线图** (LineMark) — 一行一个顶点；平滑曲线、面积图、阶梯线、堆叠面积
- **散点图** (PointMark) — 一行一个点；气泡图用 Size 通道
- **K 线图** (CandlestickMark) — 一行一个周期（OHLC）
- **箱线图** (BoxMark) — 一行一个五数概括
- **小提琴图** (ViolinMark) — 一行一个样本；同组多行聚成一个小提琴
- **热力图** (HeatmapMark) — 一行一个单元格
- **范围面积图** (RangeAreaMark) — 一行一段范围带（上界 + `lower`）
- **时间轴** (TimelineMark) — 一行一个区间（`start`..`end`）
- **棒棒糖图** (LollipopMark) — 一行一根茎+点
- **里程碑图** (MilestoneMark) — 一行一个事件；时间轴上的标记 + 标签，可选泳道

### 极坐标系 (Polar)
- **饼图/圆环图** (PieMark) — 一行一个扇区；支持内圈文本
- **雷达图** (RadarMark) — 一行某系列的一个顶点
- **仪表盘** (GaugeMark) — 整图只取第一行
- **漏斗图** (FunnelMark) — 一行一个阶段，按行序绘制

### 层级结构 (Hierarchical)
- **矩形树图** (TreemapMark) — 一行一个节点（可选 `parent` 连成树）
- **旭日图** (SunburstMark) — 一行一个环段（可选 `parent`）

### 流向关系 (Flow)
- **桑基图** (SankeyMark) — 一行一条流（`source` → `target`）
- **弦图** (ChordMark) — 一行一条弦（`source` → `target`）

### 特殊类型
- **华夫饼图** (WaffleMark) — 一行一个类别的网格占比

## 最小示例

一个节点就够（它是 tool 脚本，**编辑器里也会实时预览**：改 `Kind`、`Rows` 等导出属性立刻重绘；视图很多时可以把
`EditorPreview` 关掉）：

```csharp compile-class
using System.Collections.Generic;
using Godot;
using GodotNodeExtension.Component.GodotChart;   // ChartView, ChartKind, DataRow

/// <summary>在自有区域里显示一张柱状图：选好类型，把强类型数据行交给它。</summary>
public partial class RevenueChart : ChartView
{
    public override void _Ready()
    {
        Kind  = ChartKind.Bar;      // 或 Line / Area / Scatter / Pie / Donut / Heatmap / Sankey / ...
        Title = "Revenue";

        // 数据行是强类型的：int / float / string / bool 一路保留自己的类型到标度层——
        // 不经过文本往返。同一份数据也可以写在检查器里，
        // 那里 Rows 是键值字典的数组。
        SetData(new[]
        {
            new DataRow().Set("month", "Jan").Set("revenue", 1200).Set("series", "North"),
            new DataRow().Set("month", "Feb").Set("revenue", 1800).Set("series", "South"),
        });
    }
}
```

`ChartView` 会创建画布、映射通道、推断标度、排版、让表面跟随节点尺寸，并在设置变化时重绘——行为类似
`TextureRect`，尺寸、帧循环与呈现都在内部。

背景是**画**出来的，不是清屏清出来的：节点没有背景导出，画布每次重绘都被清成**透明**，看到的那块背景矩形由
背景渲染器按主题绘制（颜色 `ChartTheme.BackgroundColor`、圆角 `ChartTheme.BackgroundCornerRadius`）。所以要让
图表透出后面的画面，就让这块矩形透明——`view.ConfigureChart = c => c.BackgroundColor = Colors.Transparent;`，
或把主题 `BackgroundColor` 的 alpha 设为 0（该主题资源对所有引用它的图都生效）。

### 通道、坐标轴、图例与悬停提示

图表需要的一切都在这个节点上，不用自己接线：

```csharp compile
// 通道（字段名留空时用该图表类型的默认字段）
view.XField = "month";          // 类别
view.YField = "revenue";        // 数值
view.ColorField = "series";     // 驱动图例与分系列配色
view.SizeField = "weight";      // 气泡图
view.OpacityField = "confidence";

// 坐标轴 / 标度 与图例
view.XAxisTitle = "Month";
view.YAxisTitle = "Revenue";
view.YAxisUnit = "USD";
view.Legend = LegendPosition.Bottom;   // Top / Bottom / Left / Right / None

// 指针反馈
view.ShowTooltip = true;        // 悬停提示（样式见 view.Tooltip）
view.ShowCrosshair = true;      // 跟随指针的准星
```

节点只接手自己处理的指针事件：它接受需要响应的移动与点击事件，而把滚轮留作**未处理**
（`MouseFilter = Pass`），所以指针停在图表上时，宿主的 `ScrollContainer` 照常滚动。

### 字体（含中文）

图表所有文字共用一个字体：标题、坐标轴刻度与标题、图例、数据标签、悬停提示。字体属于**主题**
（`ChartTheme.Font` / `ChartTheme.FontFamily`），没有 `CustomTheme` 的节点用后端默认字体。**所选字体没有
的字形会自动由系统字体补齐**——即使默认字体只含拉丁字形，中文标签也能正常显示：

```csharp compile
view.CustomTheme = new ChartTheme
{
    Font = GD.Load<Font>("res://fonts/NotoSansSC-Regular.ttf"),   // 或 SystemFont 资源
    FontFamily = "Microsoft YaHei",                              // Font 为空时使用
};
```

在编辑器里：建一个主题资源，在 *Typography* 分组里设置 `Font`（或 `FontFamily`），再把节点的
`CustomTheme` 指向它——见 [customization.cn.md](customization.cn.md#自定义主题资源chartview)。

## 示例

浏览器左侧是组件树，每个组件下面列出它的**全部**演示场景，所以本组件按功能拆成多个聚焦的场景
（`Example/GodotChart/demos.json` 负责顺序与说明文案）：

| 场景 | 展示内容 |
|---|---|
| `BasicsDemo.tscn` | 库里的全部图表类型（22 种；柱状与面积另有分组/堆叠变体格），以及 `SectionMark` 参考线 |
| `ChartViewFieldsDemo.tscn` | 用代码配置 `ChartView`：六个通道及其区间、四种 `ColorMapping`、锁定轴域、`ThemeKind` 与主题资源、轴标题/单位/图例、全部数据入口（`SetValues` / `SetData` / `SetCsv` + `ParseCsv` / `AddRow` + `WindowSize` / `Clear`），以及 `Refresh` 与 `Repaint` 的差别 |
| `ChartViewHooksDemo.tscn` | `ConfigureMark`、`Tooltip.Options` 的两种内容构建器、让两个视图共享一张画布的 `CanvasFactory`、`Surface` / `Canvas` / `Texture` 三件套，以及编辑器专用的 `EditorPreview`（`ConfigureChart` 本身在 `ChartViewFieldsDemo` 与 `ChartCallbacksDemo` 上） |
| `ChartCallbacksDemo.tscn` | `OnHover` / `OnClick` / `OnSelectionChanged` / `OnFocusChanged` / `OnLegendClick`（含 `Handled` 拦截）、`Select` / `FocusSeries` / `HideSeries` / `ShowAllSeries`、用 `GetSeriesInfo()` 自建外部图例，以及宿主自己用 `HitTest` + `Interaction` + `Hover` + `NotifyHoverChanged` 处理指针 |
| `ChartThemeDemo.tscn` | 逐组演示 `ChartTheme`：调色板与渐变、边框配色、排版、线宽与提示框度量、`Enable*` 开关、`Clone()` 与 `Dark()` / `Light()` 对照、运行中改主题资源、以及 `Chart` 级颜色覆盖 |
| `ChartAnimationDemo.tscn` | 动画的**宿主侧**闭环：每帧一个 `AnimationController`、把 `AnimationContext` 交给 `Chart.Animate(ctx)`、入场/悬停/数据过渡/退场按钮，以及用自定义 mark 画出七条 `EaseType` 曲线 |
| `ChartStreamingDemo.tscn` | 两条**速率不同**的实时图：60 Hz 示波器走**环形缓冲**（轴固定 0..N），以及每 10 秒补一根 K 线的**股价行情**走 **`AddRow` + `WindowSize`**；另有实时仪表盘 |
| `ChartCustomizationDemo.tscn` | 纯代码构建：完整的 `Channel.Y2` 链路（`Encode` + `Scale` + `Y2Axis` + `ScaleDomain`）、三个自定义 `Mark` 子类、mark 级编码、`ApplyToAllMarks`、自定义 `IDataTransform`，以及背景/网格/标题三个渲染槽的替换 |
| `ChartScaleDemo.tscn` | 该页演示的各个标度：`TimeScale`、`SequentialColorScale`、`OrdinalScale` + `ColorScale` + `ShapeScale`、`IdentityColorScale`、`DivergingColorScale`，以及由两个自定义 mark 消费的 `BandScale` / `RadialScale`（`LogScale` 在 `ChartCustomizationDemo` 页） |
| `ChartCanvasDemo.tscn` | 宿主闭环：`Interaction` + `NotifyHoverChanged`、`HandleClick`、若干查询属性、`AppendData` 的两个重载（A / B 键），以及由 `Canvas2DFactory.Create` 造出、被两个图表共享的一张画布 |
| `ChartRendererDemo.tscn` | 七个渲染槽逐个替换，外加绘制 API：`IPath2D` 几何、`IPaint2D` 虚线与渐变、带裁剪与变换的 save/restore 栈、`MeasureText`、`DrawImage` 与 `Capabilities` |
| `ChartMarksCartesianDemo.tscn` | 笛卡尔坐标系 mark 的专有旋钮（柱/线/点/区间面积/箱线/K 线/热力/时间轴/里程碑/棒棒糖/小提琴/华夫格） |
| `ChartBigDataDemo.tscn` | 真实数据（24 国、1970–2023），滚轮缩放、拖拽平移、图例筛选与参考线 |
| `ChartLayeredRenderingDemo.tscn` | 同一张图表两种渲染方式（分层 / 不分层）并排对比耗时与图层内存 |
| `ChartLayoutDemo.tscn` | 单个 `ChartView` 的布局预算：`Chart.MinimumSize` 与 `Chart.MinimumPlotSize`、布局实际给节点的尺寸，以及 `Chart.CurrentPlotArea` 四周的内缩量；按键可切标题、图例位置、轴标题、第二 Y 轴、主题字号，并把节点压到最小尺寸（及其以下） |
| `ChartMarksPolarDemo.tscn` | 极坐标系 mark 的专有旋钮（饼图/环图/仪表盘/雷达/漏斗） |
| `ChartMarksHierarchyDemo.tscn` | 矩形树图、旭日图、桑基图、弦图四种 mark 的专有旋钮 |

**两类示例。** `BasicsDemo` 保留"纯场景、不用代码"这条路：普通的 `ChartView` 节点（挂 `ChartView` 脚本的
`Control`），`Kind`、`Rows` 与通道字段直接写在场景中——打开场景就能读、能复制、能直接改。其余页面都是
代码路径：场景负责布局与节点，脚本负责调 API，包括只在代码里存在的能力（回调、渲染槽、动画循环等）；
`ChartStreamingDemo` 介于两者之间（三个图表同样声明在场景里，脚本只负责喂数据）。这些脚本是普通脚本
（不是 `[Tool]`），所以编辑器预览只显示场景里声明的那部分 —— 按 **Play（F5）** 才能看到代码页配置完成、
真正跑起来的样子。无论哪一页，都要记住
**`ChartView` 每次重建都会替换 `Chart` 实例**，
所以挂在 `view.Chart` 上的订阅只活到下一次 `Refresh()` —— 这正是 `ConfigureChart` 的用途
（订阅它的页面是 `ChartCallbacksDemo` 与 `ChartViewFieldsDemo`）。

## 自绘（下层 API）

图表底层是在 `ICanvas2D` 上画的，从不碰场景树，所以怎么呈现这张纹理由你选择：`Canvas2DControl` 自带一张画布并
替你展示，而 `Chart` 根本不是 Node——它只负责往画布上逐帧绘制。需要额外 mark、tooltip、命中测试，或把纹理交给
别处消费（`Sprite2D`、`TextureRect`、共享画布）时用这一层；完整走法（托管画布、自己驱动
`BeginFrame`/`EndFrame` 循环、呈现纹理）在
[快速入门 → 需要更多控制](getting-started.cn.md#需要更多控制)。这一层有个细节：`ChartView` 的 surface 始终
清成**透明**，背景交给图表的背景渲染器去画，所以主题（或图表）背景的 alpha 为 0 时是真的能透过去。

## 编辑器热重载

`ChartView` 是 `[Tool]` 脚本（`ChartTheme` 也是 `[Tool]` 资源），所以编辑器打开过的场景里会留着活着的托管对象：
节点本身、它的 `Canvas2DControl` 子节点、背后的表面，以及它在树上期间持有的 `ChartTheme.Changed` 订阅。
当含这类节点的场景是**当前编辑的场景**时，下一次编译 C# 可能看到：

```
ERROR: .NET: Failed to unload assemblies.
ERROR: .NET: Giving up on assembly reloading. Please restart the editor if unloading was failing.
```

有两点值得知道：

- 这个失败在**本次编辑器会话里是粘住的**：Godot 之后不再尝试卸载，后续每次重新编译都会再报一遍；
- 触发条件是「本次会话里**曾经**被当作当前编辑场景打开过」的场景。只停在后台页签的场景无害，但事后切走也解不掉。

实际做法：改 C# 代码的阶段，让当前编辑场景保持**不含图表**；一旦碰到这条报错就**重启编辑器** —— 只把场景关掉
并不能可靠释放「实例化过图表」留下的东西。

## 环境要求

- Godot——本组件在 Godot 4.7+ 下开发与测试
- .NET——需要 .NET SDK 10.0+（组件自身源码目标框架 `net10.0`）
- SkiaSharp 3.x（通过 SkiaCanvas2DBackend 桥接）

### 哪些东西来自哪里

图表是声明式描述的、经画布绘制：本组件既拥有图表层（`Chart`、`ChartView`、各 mark 与标度），也拥有它
下面的绘制抽象（`ICanvas2D`、`Canvas2DControl`、`Canvas2DFactory`、`IPath2D`、`IPaint2D`）。这套抽象是对外
公开的——宿主可以在图表旁边画自己的内容，也可以接入别的后端——它本身不绑定某个渲染器。

它**不**拥有的是默认后端所依赖的那层 Skia/Godot 桥：surface、共享 GPU 上下文与类型转换器来自本组件依赖的
**GodotSkia**（`SkiaCanvasTexture2D`、`SkiaGodotConverter`）。装上 GodotSkia 才能让默认后端工作；上面的画布
抽象无需知道底下是哪座桥。
