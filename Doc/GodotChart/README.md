**English** | [中文](README.cn.md)

# GodotChart Documentation

GodotChart is a declarative chart library for Godot built on **Grammar of Graphics** principles, using C# and SkiaSharp. It provides a fluent API for creating rich, interactive charts: 22 `ChartKind` values backed by 20 `Mark` classes (the annotation mark `SectionMark` is the 21st subclass), an animation system, theme customization, and full user interaction support.

## Chart gallery

Every picture below is one cell of the component's basics example (`BasicsDemo` in the example
browser): each cell is a `ChartView` node whose `Kind`, `Rows` and channel fields are set right in the
scene, so you can copy a cell into your own scene and edit it. Cartesian charts are shown landscape and
the polar, hierarchical and flow ones square - that is the shape their marks ask for.

### Cartesian

| | |
|---|---|
| ![Bar](assets/bar.png) | ![Grouped bar](assets/grouped-bar.png) |
| ![Stacked bar](assets/stacked-bar.png) | ![Line](assets/line.png) |
| ![Area](assets/area.png) | ![Stacked area](assets/stacked-area.png) |
| ![Bubble](assets/bubble.png) | ![Range area](assets/range-area.png) |
| ![Violin](assets/violin.png) | ![Box](assets/box.png) |
| ![Candlestick](assets/candlestick.png) | ![Heatmap](assets/heatmap.png) |
| ![Waffle](assets/waffle.png) | ![Timeline](assets/timeline.png) |
| ![Lollipop](assets/lollipop.png) | ![Milestone](assets/milestone.png) |
| ![Reference lines](assets/reference-lines.png) | |

### Polar

| | |
|---|---|
| ![Pie](assets/pie.png) | ![Donut](assets/donut.png) |
| ![Radar](assets/radar.png) | ![Gauge](assets/gauge.png) |
| ![Funnel](assets/funnel.png) | |

### Hierarchical

| | |
|---|---|
| ![Treemap](assets/treemap.png) | ![Sunburst](assets/sunburst.png) |

### Flow

| | |
|---|---|
| ![Sankey](assets/sankey.png) | ![Chord](assets/chord.png) |

## Architecture

GodotChart employs a four-layer architecture:

| Layer | Responsibility | Core Classes |
|-------|---------------|-------------|
| **Data** | Data modeling & transforms | `DataRow`, `BinTransform` |
| **Encoding** | Map data fields to visual channels | `Channel`, `FieldEncode`, `ConstantEncode` |
| **Scale** | Map data values to [0, 1] range | `LinearScale`, `OrdinalScale`, `LogScale`, etc. |
| **Mark** | Render encodings as graphics | 20 `Mark` classes for the kinds (21 subclasses in total) |

Charts are assembled through a **Fluent API** on the `Chart` class:

```csharp compile
var rows = new List<DataRow>();
var theme = ChartTheme.Dark();

new Chart(canvas)
    .Data(rows)              // data
    .Mark(new IntervalMark())// mark type
    .Encode(Channel.X, "x") // channel encoding
    .Scale(Channel.Y, new LinearScale(0, 3000)) // optional: scales are auto-inferred by default
    .Theme(theme)            // theme
    .Render();               // render
```

## Table of Contents

| Document | Content |
|----------|---------|
| [Getting Started](getting-started.md) | Setup, first chart, core concepts |
| [Chart Types](chart-types.md) | Detailed guide for all 22 chart kinds with examples |
| [Customization & Theming](customization.md) | Theme system, scales, axes, legends, custom renderers |
| [Advanced Features](advanced.md) | Animation, interaction events, tooltips, real-time streaming, data transforms, composite charts |
| [API Reference](api-reference.md) | Complete public class, method, and property listing |

## Supported Chart Types

Every entry below says what **one row** means for that chart; the field-by-field table is in
[Data Shapes at a Glance](chart-types.md#data-shapes-at-a-glance).

### Cartesian
- **Bar Chart** (IntervalMark) — one row per bar; vertical/horizontal, stacked
- **Line Chart** (LineMark) — one row per vertex; smooth curves, area fill, step lines, stacked area
- **Scatter / Bubble** (PointMark) — one row per point; Size channel for bubbles
- **Candlestick** (CandlestickMark) — one row per period (OHLC)
- **Box Plot** (BoxMark) — one row per five-number summary
- **Violin** (ViolinMark) — one row per sample; several rows per group become one violin
- **Heatmap** (HeatmapMark) — one row per cell
- **Range Area** (RangeAreaMark) — one row per band segment (upper bound + `lower`)
- **Timeline** (TimelineMark) — one row per interval (`start`..`end`)
- **Lollipop** (LollipopMark) — one row per stem + dot
- **Milestones** (MilestoneMark) — one row per event; markers + labels on a time axis, optional lanes

### Polar
- **Pie / Donut** (PieMark) — one row per slice; center text support
- **Radar** (RadarMark) — one row per vertex of a series
- **Gauge** (GaugeMark) — the whole chart from the first row only
- **Funnel** (FunnelMark) — one row per stage, drawn in row order

### Hierarchical
- **Treemap** (TreemapMark) — one row per node (optional `parent` links a tree)
- **Sunburst** (SunburstMark) — one row per ring node (optional `parent`)

### Flow
- **Sankey** (SankeyMark) — one row per flow (`source` → `target`)
- **Chord** (ChordMark) — one row per chord (`source` → `target`)

### Special
- **Waffle** (WaffleMark) — one row per category's share of the grid

## Minimal Example

One node is enough:

```csharp compile-class
using System.Collections.Generic;
using Godot;
using GodotNodeExtension.Component.GodotChart;   // ChartView, ChartKind, DataRow

/// <summary>Shows a bar chart in its own area: pick the kind, hand over typed rows.</summary>
public partial class RevenueChart : ChartView
{
    public override void _Ready()
    {
        Kind  = ChartKind.Bar;      // or Line / Area / Scatter / Pie / Donut / Heatmap / Sankey / ...
        Title = "Revenue";

        // Rows are typed: int / float / string / bool keep their type all the way to the scales -
        // nothing is round-tripped through text. The same rows can be authored in the inspector,
        // where Rows is an array of key/value dictionaries.
        SetData(new[]
        {
            new DataRow().Set("month", "Jan").Set("revenue", 1200).Set("series", "North"),
            new DataRow().Set("month", "Feb").Set("revenue", 1800).Set("series", "South"),
        });
    }
}
```

That is the whole setup: `ChartView` creates the canvas, maps the channels, infers the scales, lays the
chart out, keeps the surface at the node size and redraws when a setting changes. It works like a
`TextureRect` - sizing, the frame loop and presentation are internal.

The node is a tool script: it also draws inside the editor, so the preview follows `Kind`, `Rows` and
the other exports as you edit them (turn `EditorPreview` off for scenes with many views).

### Channels, axes, legend and tooltip

Everything the chart needs is on the node - no plumbing:

```csharp compile
// Channels (an empty field name uses the default for this kind)
view.XField = "month";          // category
view.YField = "revenue";        // value
view.ColorField = "series";     // drives the legend and the per-series colours
view.SizeField = "weight";      // bubble charts
view.OpacityField = "confidence";

// Axes / metrics and legend
view.XAxisTitle = "Month";
view.YAxisTitle = "Revenue";
view.YAxisUnit = "USD";
view.Legend = LegendPosition.Bottom;   // Top / Bottom / Left / Right / None

// Pointer feedback
view.ShowTooltip = true;        // hover tooltip (styling through view.Tooltip)
view.ShowCrosshair = true;      // crosshair following the pointer
```

The node keeps the mouse wheel out of its own handling: it accepts the motion and click events it acts
on and leaves the wheel unhandled (`MouseFilter = Pass`), so a hosting `ScrollContainer` still scrolls
while the pointer sits on a chart.

### Fonts (including CJK)

Every piece of chart text uses one font: title, axis labels and titles, legend, data labels and the
tooltip. The font is part of the **theme** (`ChartTheme.Font` / `ChartTheme.FontFamily`), so a view with
no `CustomTheme` uses the backend default. Glyphs the chosen font does not have are covered by a system
font automatically - a Latin-only default still draws 中文 labels:

```csharp compile
view.CustomTheme = new ChartTheme
{
    Font = GD.Load<Font>("res://fonts/NotoSansSC-Regular.ttf"),   // or a SystemFont resource
    FontFamily = "Microsoft YaHei",                              // used when Font is null
};
```

In the editor: create the theme resource, set its `Font` (or `FontFamily`) in the *Typography* group and
point the node's `CustomTheme` at it - see [customization.md](customization.md#custom-theme-resource-chartview).

## Examples

The browser groups every component in a tree and lists **all** of its demo scenes underneath, so this
component ships feature-focused scenes instead of one giant demo (`Example/GodotChart/demos.json`
orders them and carries the descriptions):

| Scene | Shows |
|---|---|
| `BasicsDemo.tscn` | every chart kind the library ships (all 22; the bar and area cells repeat as grouped and stacked variants), plus the `SectionMark` reference lines |
| `ChartStreamingDemo.tscn` | two feeds at **different rates**: a 60 Hz oscilloscope over a fixed-size **ring buffer** (axis pinned at 0..N) and a **price feed** appending one 10 s candle every 10 s through **`AddRow` + `WindowSize`**, plus a live gauge |
| `ChartViewFieldsDemo.tscn` | `ChartView` configured from code: all six channels with their ranges, four of the five `ColorMapping` kinds (the fifth, `Auto`, is the default the page leaves alone), pinned axis domains, `ThemeKind` and a theme resource, axes/units/legend, every data entry point (`SetValues` / `SetData` / `SetCsv` + `ParseCsv` / `AddRow` + `WindowSize` / `Clear`) and `Refresh` versus `Repaint` |
| `ChartViewHooksDemo.tscn` | `ConfigureMark`, `Tooltip.Options` with both content builders, a `CanvasFactory` that shares one canvas between two views, the `Surface` / `Canvas` / `Texture` trio, and the editor-only `EditorPreview` (`ConfigureChart` itself is on `ChartViewFieldsDemo` and `ChartCallbacksDemo`) |
| `ChartCallbacksDemo.tscn` | `OnHover` / `OnClick` / `OnSelectionChanged` / `OnFocusChanged` / `OnLegendClick` (with `Handled`), `Select` / `FocusSeries` / `HideSeries` / `ShowAllSeries`, an external legend built from `GetSeriesInfo()`, and a host that walks the pointer itself through `HitTest` + `Interaction` + `Hover` + `NotifyHoverChanged` |
| `ChartAnimationDemo.tscn` | the animation **host** loop: an `AnimationController` per frame, `AnimationContext` handed to `Chart.Animate(ctx)`, entry / hover / data transition / exit buttons, and the seven `EaseType` curves drawn by a custom mark |
| `ChartThemeDemo.tscn` | `ChartTheme` group by group: palette and gradient, frame colours, typography, line widths and tooltip metrics, the `Enable*` toggles, `Clone()` next to `Dark()` and `Light()`, editing the resource live, and `Chart` colour overrides |
| `ChartCustomizationDemo.tscn` | hand-built chart: the full `Channel.Y2` chain (`Encode` + `Scale` + `Y2Axis` + `ScaleDomain`), three custom `Mark` subclasses, mark-level encodes, `ApplyToAllMarks`, a custom `IDataTransform`, and background/grid/title renderer slots |
| `ChartScaleDemo.tscn` | the scales the page demonstrates: `TimeScale`, `SequentialColorScale`, `OrdinalScale` + `ColorScale` + `ShapeScale`, `IdentityColorScale`, `DivergingColorScale`, and `BandScale` / `RadialScale` consumed by two custom marks (`LogScale` is on `ChartCustomizationDemo`) |
| `ChartCanvasDemo.tscn` | host loop: `Interaction` + `NotifyHoverChanged`, `HandleClick`, the query properties, both `AppendData` overloads (A / B keys), and one canvas from `Canvas2DFactory.Create` shared by two charts |
| `ChartRendererDemo.tscn` | all seven renderer slots replaced together with the page's own implementations (and put back to the defaults with one more click), plus the drawing API: `IPath2D` geometry, `IPaint2D` dashes and gradients, the save/restore stack with clip and transforms, `MeasureText`, `DrawImage` and `Capabilities` |
| `ChartMarksCartesianDemo.tscn` | per-mark knobs of the Cartesian marks (bars, lines, points, range areas, box plots, candles, heatmap, timeline, milestone, lollipop, violin, waffle) |
| `ChartBigDataDemo.tscn` | a real data set (24 countries, 1970-2023) with wheel zoom, pan, legend filtering and a reference line, plus the decimation, zoom-factor, pan-button and double-click-reset exports under **D** / **Z** / **B** / **X** |
| `ChartLayeredRenderingDemo.tscn` | the same chart rendered both ways (layered / not) with the draw time and the layer memory side by side |
| `ChartLayoutDemo.tscn` | the layout budget of one `ChartView`: `Chart.MinimumSize` and `Chart.MinimumPlotSize`, the size the layout gave the node, and the insets around `Chart.CurrentPlotArea` - with keys for the title, the legend position, the axis titles, a second Y axis, the theme font size and a node pinned to its minimum (and below it), plus six axis and content **exports** (label rotation, tick density, label formats, content shape and alignment, pinned Y ends) |
| `ChartMarksPolarDemo.tscn` | per-mark knobs of the polar marks (pie, donut, gauge, radar, funnel) |
| `ChartMarksHierarchyDemo.tscn` | per-mark knobs of the treemap, sunburst, sankey and chord marks |

**Two families of demos.** `BasicsDemo` keeps the no-code path: plain `ChartView` nodes (a `Control` with
the `ChartView` script) whose `Kind`, `Rows` and channel fields are written right in the scene, so you can
read, copy and edit a chart without touching any code. Every other page is the code path: the scene keeps
the layout and the nodes and the script drives the API - including everything that only exists in code,
such as the callbacks, the renderer slots and the animation loop (`ChartStreamingDemo` sits in between: its
three charts are declared in the scene too, and the script only feeds them data). Those scripts are ordinary
(non-tool) scripts, so the editor preview only shows what the scene declares: press **Play (F5)** to see a
code page configured and running. Whatever a page does in code, remember that a `ChartView`
**replaces its `Chart` instance on every rebuild**, so a subscription made on `view.Chart` lasts only
until the next `Refresh()` - that is what `ConfigureChart` is for (the pages that subscribe are
`ChartCallbacksDemo` and `ChartViewFieldsDemo`). The pages are deliberately **standalone**: each one carries its
own small helpers (a `Row(...)` row factory, a `Report(...)` status line, its own input handling) instead of
sharing a support file, so a single script can be read, copied and edited on its own - the same few helpers
appearing in more than one page is the price of that.

## Custom Drawing

Under the hood a chart draws onto an `ICanvas2D` and never touches the scene tree, so presenting that
texture is your choice: `Canvas2DControl` hosts a canvas and presents it for you, while `Chart` is not a
Node at all — it only draws onto a canvas, once per frame. That path is useful for extra marks,
tooltips, hit testing or a texture consumed elsewhere (a `Sprite2D`, a `TextureRect`, a shared canvas);
the walkthrough — hosting a canvas, driving the `BeginFrame`/`EndFrame` loop yourself and presenting the
texture — lives in
[Getting Started → Need more control](getting-started.md#need-more-control). One detail of that level:
a `ChartView` keeps its surface cleared to **transparent** and lets the chart's background renderer paint
the background, so a theme (or chart) background with alpha 0 really is see-through.

## Editor Hot Reload

`ChartView` is a `[Tool]` script (and `ChartTheme` a `[Tool]` resource), so a scene the editor has opened
keeps live managed objects around: the node itself, its `Canvas2DControl` child, the surface behind it and
the `ChartTheme.Changed` subscription it holds while it is in a tree. While a scene containing such a node
is the **edited** scene, the next C# build can end with:

```
ERROR: .NET: Failed to unload assemblies.
ERROR: .NET: Giving up on assembly reloading. Please restart the editor if unloading was failing.
```

Two consequences are worth knowing:

- the failure is **sticky for the rest of the editor session**: Godot stops trying to unload and reports the
  message again on every later build;
- what triggers it is a scene that has **ever been the edited one** in that session. Scenes that only sit in
  background tabs are harmless, but switching away afterwards does not undo it.

In practice: while you are iterating on C# code, keep a scene **without** chart nodes as the edited scene,
and restart the editor when you hit the message - closing the scene again does not reliably release what
instantiating a chart view left behind.

## Requirements

- Godot - this component is developed and tested with Godot 4.7+
- .NET - the .NET SDK 10.0+ (the component's sources target the `net10.0` framework)
- SkiaSharp 3.x (via SkiaCanvas2DBackend)

### What comes from where

Charts are described declaratively and drawn through a canvas: this component owns the chart layer
(`Chart`, `ChartView`, the marks and scales) **and** the drawing abstraction under it (`ICanvas2D`,
`Canvas2DControl`, `Canvas2DFactory`, `IPath2D`, `IPaint2D`). The abstraction is public - a host can draw its
own content beside a chart, or plug in another backend - and it is not tied to one renderer.

What it does **not** own is the Skia/Godot bridge the default backend sits on: the surface, the shared GPU
context and the type converters come from the **GodotSkia** component this one depends on
(`SkiaCanvasTexture2D`, `SkiaGodotConverter`). Install GodotSkia to get a working backend; the canvas
abstraction above it stays usable without knowing which bridge is underneath.
