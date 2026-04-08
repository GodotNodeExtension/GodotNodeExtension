**English** | [中文](README.cn.md)

# GodotChart Documentation

GodotChart is a declarative chart library for Godot built on **Grammar of Graphics** principles, using C# and SkiaSharp. It provides a fluent API for creating rich, interactive charts with 19 chart types, animation system, theme customization, and full user interaction support.

## Architecture

GodotChart employs a four-layer architecture:

| Layer | Responsibility | Core Classes |
|-------|---------------|-------------|
| **Data** | Data modeling & transforms | `DataRow`, `BinTransform` |
| **Encoding** | Map data fields to visual channels | `Channel`, `FieldEncode`, `ConstantEncode` |
| **Scale** | Map data values to [0, 1] range | `LinearScale`, `OrdinalScale`, `LogScale`, etc. |
| **Mark** | Render encodings as graphics | 19 `Mark` subclasses |

Charts are assembled through a **Fluent API** on the `Chart` class:

```csharp
new Chart(canvas)
    .Data(rows)              // data
    .Mark(new IntervalMark())// mark type
    .Encode(Channel.X, "x") // channel encoding
    .Scale(Channel.Y, ...)  // scale configuration
    .Theme(theme)            // theme
    .Render();               // render
```

## Table of Contents

| Document | Content |
|----------|---------|
| [Getting Started](getting-started.md) | Setup, first chart, core concepts |
| [Chart Types](chart-types.md) | Detailed guide for all 19 chart types with examples |
| [Customization & Theming](customization.md) | Theme system, scales, axes, legends, custom renderers |
| [Advanced Features](advanced.md) | Animation, interaction events, tooltips, real-time streaming, data transforms, composite charts |
| [API Reference](api-reference.md) | Complete public class, method, and property listing |

## Supported Chart Types

### Cartesian
- **Bar Chart** (IntervalMark) — Vertical/horizontal, stacked
- **Line Chart** (LineMark) — Smooth curves, area fill, step lines, stacked area
- **Scatter / Bubble** (PointMark) — With Size channel for bubbles
- **Candlestick** (CandlestickMark) — OHLC financial data
- **Box Plot** (BoxMark) — Statistical distributions
- **Violin** (ViolinMark) — Density distributions
- **Heatmap** (HeatmapMark) — Matrix heatmap
- **Range Area** (RangeAreaMark) — Confidence intervals / bands
- **Timeline** (TimelineMark) — Gantt chart / time intervals
- **Lollipop** (LollipopMark) — Dot + stem bar variant

### Polar
- **Pie / Donut** (PieMark) — With center text support
- **Radar** (RadarMark) — Multi-dimensional comparison
- **Gauge** (GaugeMark) — Single-value metric
- **Funnel** (FunnelMark) — Conversion funnel

### Hierarchical
- **Treemap** (TreemapMark) — Area proportions
- **Sunburst** (SunburstMark) — Hierarchical pie chart

### Flow
- **Sankey** (SankeyMark) — Flow distribution
- **Chord** (ChordMark) — Relationship network

### Special
- **Waffle** (WaffleMark) — Percentage grid

## Minimal Example

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

## Requirements

- Godot 4.4+
- .NET 9.0
- SkiaSharp 3.x (via SkiaCanvas2DBackend)
