**English** | [中文](getting-started.cn.md)

# Getting Started

This guide walks you through creating your first GodotChart chart from scratch.

## Prerequisites

- A Godot project with C# support enabled - this component is developed and tested with Godot 4.7+
- .NET - the .NET SDK 10.0+ (the component's sources target the `net10.0` framework)
- Project references to GodotNodeExtension and SkiaSharp

## Core Concepts

### DataRow — Data Record

GodotChart uses `DataRow` as the data unit. One row is just a set of named fields — a `field → value`
bag with no schema, so two rows of the same chart need not carry the same fields:

```csharp compile
var row = new DataRow()
    .Set("month", "Jan")
    .Set("revenue", 1200)
    .Set("cost", 800);

// Read data
string month = row.Get<string>("month");  // "Jan"
int revenue = row.Get<int>("revenue");    // 1200
```

`Set(...)` writes (and overwrites) a field and returns the row, so the calls chain. `Get<T>(field)`
returns the stored value, converting it when the requested type differs and throwing
`KeyNotFoundException` when the field is missing; `TryGet<T>()` returns `false` instead of throwing,
and `Has()` only checks whether the field exists. Field names are **case-sensitive** (`"Value"` is not
`"value"`), and a name is only a convention — nothing is hard-wired: you bind a channel to whatever
field you like with `Chart.Encode(channel, "your field")`.

A value may be any object and plain .NET types keep their type, so a `DataRow` is typed all the way to
the scales. When the same data is authored in the inspector, `Rows` is an array of Godot key/value
dictionaries and the values are mapped on load: `Bool → bool`, `Int → long`, `Float → double`,
`String → string`.

`Has()` tells a **missing field** apart from a **field present but `null`**, but most marks treat both
the same way: a row without a usable value is skipped, and the auto-fitted scales ignore both cases
too — so a `null` never plots as zero.

Feed a chart with `Chart.Data(rows)`, which replaces the whole data set, or `Chart.AppendData(row)` to
add rows one at a time (the one to reach for in a live/streaming chart).

### Channel — Visual Channel

Channels determine how data maps to visual properties:

| Channel | Purpose | Example |
|---------|---------|---------|
| `Channel.X` | Horizontal position | Month, category |
| `Channel.Y` | Vertical position (left axis) | Amount, value |
| `Channel.Y2` | Vertical position (right axis) | Composite chart secondary axis |
| `Channel.Color` | Color | Series coloring |
| `Channel.Size` | Size | Bubble chart radius |
| `Channel.Opacity` | Opacity | Confidence mapping |
| `Channel.Shape` | Shape (symbol) | Scatter / bubble glyph, lollipop dot |
| `Channel.Label` | Text label | Data point annotation |

### Field Conventions

Field names come in two flavours.

**Channel fields** are bound to a visual channel with `Chart.Encode(channel, "field")` — `X`, `Y`,
`Y2`, `Color`, `Size`, `Opacity`, `Shape`. (`Label` is only read by `TimelineMark` and `MilestoneMark`.) The binding
decides which field feeds which channel, so the name is yours to choose. `ChartView` pre-binds the
library conventions: `category` for the X channel, `value` for Y and `series` for Color.

A mark can bind a channel for itself with `mark.Encode(channel, "field")`. That binding wins **for that
mark only**: the chart-level encode stays the fallback for every other mark, so one mark can draw its
own field while the rest keep the chart's. The scale is still one per channel - it is fitted from both
fields - so add an explicit `Chart.Scale(...)` when the two fields are not the same kind of value.

**Fixed fields** are read by name *inside* a mark, not through a channel, so the only way to rename one
is the matching property on that mark:

| Field | Used by | Rename with |
|-------|---------|-------------|
| `source` / `target` | Sankey, Chord | `SourceField` / `TargetField` |
| `start` / `end` | Timeline | `StartField` / `EndField` |
| `lower` | Range area | `LowerField` |
| `open` / `high` / `low` / `close` | Candlestick | `OpenField` / `HighField` / `LowField` / `CloseField` |
| `min` / `q1` / `median` / `q3` / `max` | Box plot | `MinField` / `Q1Field` / `MedianField` / `Q3Field` / `MaxField` |
| `parent` | Treemap, Sunburst | `ParentField` |
| `label` | Milestone | `LabelField` |
| `lane` | Milestone (the default `Y` field) | `YField` |

For the full per-type breakdown — what one row means and which fields it reads — see
[Data Shapes at a Glance](chart-types.md#data-shapes-at-a-glance).

### Mark — Visual Mark

Marks determine how data is **visualized**. Each Mark type corresponds to a chart type:

```csharp compile
new IntervalMark();  // bar chart
new LineMark();      // line chart
new PieMark();       // pie chart
new PointMark();     // scatter plot
```

### Scale — Data Mapping

Scales control how data values map to visual space:

```csharp compile
new LinearScale(0, 100);       // linear mapping [0, 100] → [0, 1]
new LogScale(1, 10000);        // logarithmic mapping
new OrdinalScale();            // categorical mapping (auto-inferred)
new ShapeScale();              // category → symbol (auto-inferred for the Shape channel)
new DivergingColorScale(-1,1); // diverging color scale
```

Scales are **auto-inferred** for the common cases: a numeric field becomes a `LinearScale`, a
text/date-less field becomes an `OrdinalScale`, the Color channel becomes a `ColorScale` and the Shape
channel becomes a `ShapeScale` (its categories take the built-in symbol vocabulary in first-seen order).
`TimeScale`, `LogScale`, `BandScale` and the continuous colour scales (`SequentialColorScale`,
`DivergingColorScale`) are never inferred — configure them explicitly with `.Scale(...)`.

## Your First Chart: Bar Chart

### Step 1: Prepare Data

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

### Step 2: Build the Chart

The quick path is the `ChartView` node: pick a chart kind, hand over the rows, and it does the rest
(canvas, scales, layout, sizing, redraw). Add a `ChartView` to your scene (or derive from it) and set
it up in `_Ready`:

```csharp compile-members
public override void _Ready()
{
    var view = new ChartView { Kind = ChartKind.Bar, Title = "Revenue" };
    view.SetAnchorsPreset(LayoutPreset.FullRect);
    AddChild(view);

    // Rows are typed DataRow instances: int / float / string / bool keep their type. The inspector
    // shows the same data as `Rows` (an array of key/value dictionaries).
    view.SetData(new[]
    {
        new DataRow().Set("month", "Jan").Set("revenue", 1200),
        new DataRow().Set("month", "Feb").Set("revenue", 1800),
        new DataRow().Set("month", "Mar").Set("revenue", 1500),
    });
}
```

Channels, axes, the legend and the pointer feedback are part of the node: `XField` / `YField` /
`ColorField` / `SizeField` / `OpacityField` / `ShapeField` map fields to channels (an empty name uses the
kind's default). How those values become visual properties is configured next to the binding:
`ColorMapping`, `SizeRange`, `OpacityRange`, `ShapeSymbols` and the axis ranges - see
[Customization](customization.md#choosing-the-mapping-scales). `XAxisTitle` / `YAxisTitle` / `YAxisUnit`
configure the axes, `Legend` places the legend and `ShowTooltip` / `ShowCrosshair` control the hover
feedback. Dual axes are not part of the node - build them with the `Chart` API.

Every one of those properties holds a **field name**, never a value - the key that is looked up in every
row, exactly the names used by `DataRow.Set(...)` / the keys of the inspector's dictionaries:

```csharp compile
view.SetData(new[]
{
    new DataRow().Set("month", "Jan").Set("revenue", 1200),
    new DataRow().Set("month", "Feb").Set("revenue", 1800),
});

view.XField = "month";       // "month" is a key of the rows above, not a category name
view.YField = "revenue";     // ... and this one is read for every row
```

A row that does not carry the named field just contributes no value for that channel (no error), which is
why rows of one chart may carry different field sets.

`ChartKind` covers all 25 built-in kinds (`Bar`, `Line`, `Area`, `Scatter`, `RangeArea`, `Pie`, `Donut`,
`Radar`, `Violin`, `Box`, `Candlestick`, `Heatmap`, `Treemap`, `Sunburst`, `Sankey`, `Chord`,
`Gauge`, `Funnel`, `Waffle`, `Timeline`, `Lollipop`, `Milestone`, `GeoArea`, `GeoBubble`, `GeoFlow`) - 25 kinds backed by 23 `Mark`
classes (`Line` and `Area` share `LineMark`, `Pie` and `Donut` share `PieMark`; the annotation mark
`SectionMark` is the 21st subclass and backs no kind). The kind is always
explicit - there is no auto-detection. Field names default to the library conventions (`category`, `value`,
`series`); kinds with extra data read `lower`, `min/q1/median/q3/max`, `open/high/low/close`,
`start/end`, `source/target` and `parent` — use `ConfigureMark` if your rows use other names.

### Step 3: Add Color Dimension

When data contains multiple series (e.g., Revenue and Cost), use `Channel.Color` to map series names to colors:

```csharp compile
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

## Your First Chart: Line Chart

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

## Your First Chart: Pie Chart

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

## Adding a Theme

GodotChart ships with built-in dark and light themes:

```csharp compile
var data = new List<DataRow>
{
    new DataRow().Set("month", "Jan").Set("value", 10),
    new DataRow().Set("month", "Feb").Set("value", 20),
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

Themes can be edited visually in the Godot Inspector as `Resource` objects — all properties are `[Export]` decorated.

## Adding Animation

```csharp compile
var data = new List<DataRow>
{
    new DataRow().Set("month", "Jan").Set("value", 10),
    new DataRow().Set("month", "Feb").Set("value", 20),
};
// In _Process(double delta):
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

## Need more control

Drop down to the canvas when you want extra marks, tooltips, hit testing or a texture consumed
elsewhere. A canvas renders into its own Godot texture and never touches the scene tree, so
presenting that texture is your decision. `Canvas2DControl` hosts a canvas for you — it creates it,
keeps the surface in step with the node size, runs the frame loop and presents the texture:

```csharp compile-members
public override void _Ready()
{
    // The view creates a canvas sized to this Control and presents its texture.
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
    view.Invalidate(); // draw on the next frame
}
```

`Invalidate()` schedules one redraw; a `Canvas2DControl` clears the surface with its `BackgroundColor`
before the draw callback (turn `ClearBeforeDraw` off for content that accumulates) and commits the frame
itself. `Render()` is not part of the builder chain (it returns `void`), so it runs inside the draw
callback.

A `ChartView`, by contrast, always keeps its surface cleared to **transparent** and lets the chart's
background renderer paint the background (a rounded rect by default), so a background with alpha 0 really
is see-through: use `ConfigureChart` (`view.ConfigureChart = c => c.BackgroundColor = Colors.Transparent;`)
for one node, or a theme whose `BackgroundColor` has a zero alpha for every chart using that resource.

If the texture has to live somewhere else — a `Sprite2D`, a `TextureRect`, a custom `CanvasItem`, or
several views sharing one canvas — create the canvas directly and drive the frame yourself:

```csharp compile-members
public override void _Ready()
{
    // No scene-tree coupling: the canvas hands you the texture it draws into.
    _canvas = Canvas2DFactory.Create(width, height);

    var sprite = new Sprite2D { Texture = _canvas.Texture, Centered = false };
    AddChild(sprite);
}

public override void _Process(double delta)
{
    if (_canvas == null || _chart == null) return;

    _canvas.BeginFrame();
    _canvas.Clear(Colors.Transparent); // the chart paints its own background
    _chart.Render();
    _canvas.EndFrame();                // uploads the frame to the texture
}
```

The lower level is chainable: **Data** binds the rows, **Mark** picks the chart type, **Encode** maps
fields to channels, **Axis** styles the axes, and a frame is committed inside the
`BeginFrame`/`EndFrame` pair. Dispose the canvas in `_ExitTree`; the chart does not own it.

## Next Steps

- [Need more control](#need-more-control) (above) — drop to the canvas when `ChartView` is not enough
- [Chart Types](chart-types.md) — Learn how to build all 25 chart kinds
- [Customization & Theming](customization.md) — Deep dive into themes, scales, axes
- [Advanced Features](advanced.md) — Animation, interaction, real-time streaming
