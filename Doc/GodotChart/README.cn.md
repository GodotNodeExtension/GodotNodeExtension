[English](README.md) | **中文**

# GodotChart 文档

GodotChart 是一个基于 **Grammar of Graphics** 设计理念的 Godot 声明式图表库，使用 C# 和 SkiaSharp 构建。它提供流畅的 API 来创建丰富的交互式图表，支持 19 种图表类型、动画系统、主题定制及完整的用户交互体验。

## 核心架构

GodotChart 采用四层架构设计：

| 层级 | 职责 | 核心类 |
|------|------|--------|
| **数据层** | 数据建模与变换 | `DataRow`, `BinTransform` |
| **编码层** | 将数据字段映射到视觉通道 | `Channel`, `FieldEncode`, `ConstantEncode` |
| **度量层** | 将数据值映射到 [0,1] 范围 | `LinearScale`, `OrdinalScale`, `LogScale` 等 |
| **标记层** | 将编码生成具体图形 | 19 种 `Mark` 子类 |

整个图表通过 `Chart` 类以 **Fluent API** 风格组装：

```csharp
new Chart(canvas)
    .Data(rows)              // 数据
    .Mark(new IntervalMark())// 标记类型
    .Encode(Channel.X, "x") // 通道编码
    .Scale(Channel.Y, ...)  // 度量配置
    .Theme(theme)            // 主题
    .Render();               // 渲染
```

## 文档目录

| 文档 | 内容 |
|------|------|
| [快速入门](getting-started.cn.md) | 环境准备、第一个图表、基本概念 |
| [图表类型](chart-types.cn.md) | 19 种图表类型的详细说明与示例代码 |
| [定制与主题](customization.cn.md) | 主题系统、度量（Scale）、坐标轴、图例、自定义渲染器 |
| [高级功能](advanced.cn.md) | 动画系统、交互事件、工具提示、实时数据流、数据变换、复合图表 |
| [API 参考](api-reference.cn.md) | 所有公共类、方法、属性的完整列表 |

## 支持的图表类型一览

### 笛卡尔坐标系 (Cartesian)
- **柱状图** (IntervalMark) — 垂直/水平柱状图、堆叠柱状图
- **折线图** (LineMark) — 平滑曲线、面积图、阶梯线、堆叠面积
- **散点图** (PointMark) — 气泡图（Size 通道）
- **K 线图** (CandlestickMark) — OHLC 金融数据
- **箱线图** (BoxMark) — 统计分布
- **小提琴图** (ViolinMark) — 密度分布
- **热力图** (HeatmapMark) — 矩阵热图
- **范围面积图** (RangeAreaMark) — 置信区间/范围带
- **时间轴** (TimelineMark) — 甘特图/时间区间
- **棒棒糖图** (LollipopMark) — 点+线柱状图

### 极坐标系 (Polar)
- **饼图/圆环图** (PieMark) — 支持内圈文本
- **雷达图** (RadarMark) — 多维属性对比
- **仪表盘** (GaugeMark) — 单值指标
- **漏斗图** (FunnelMark) — 转化漏斗

### 层级结构 (Hierarchical)
- **矩形树图** (TreemapMark) — 空间占比
- **旭日图** (SunburstMark) — 层级饼图

### 流向关系 (Flow)
- **桑基图** (SankeyMark) — 流量分布
- **弦图** (ChordMark) — 关系网络

### 特殊类型
- **华夫饼图** (WaffleMark) — 百分比方格

## 最小示例

```csharp
using GodotNodeExtension;

// 1. Prepare data
var data = new List<DataRow>
{
    new DataRow().Set("category", "A").Set("value", 30),
    new DataRow().Set("category", "B").Set("value", 50),
    new DataRow().Set("category", "C").Set("value", 20),
};

// 2. Build and render chart
new Chart(canvas)
    .Data(data)
    .Mark(new IntervalMark())
    .Encode(Channel.X, "category")
    .Encode(Channel.Y, "value")
    .Render();
```

## 环境要求

- Godot 4.4+
- .NET 9.0
- SkiaSharp 3.x（通过 SkiaCanvas2DBackend 桥接）
