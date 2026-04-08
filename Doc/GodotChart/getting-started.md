# Getting Started

This guide walks you through creating your first GodotChart chart from scratch.

## Prerequisites

- Godot 4.4+ project with C# support enabled
- .NET 9.0 SDK
- Project references to GodotNodeExtension and SkiaSharp

## Core Concepts

### DataRow — Data Record

GodotChart uses `DataRow` as the data unit. Each row contains named fields:

```csharp
var row = new DataRow()
    .Set("month", "Jan")
    .Set("revenue", 1200)
    .Set("cost", 800);

// Read data
string month = row.Get<string>("month");  // "Jan"
int revenue = row.Get<int>("revenue");    // 1200
```

Use `TryGet<T>()` for safe access to potentially missing fields, and `Has()` to check field existence.

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
| `Channel.Shape` | Shape | Marker shape |
| `Channel.Label` | Text label | Data point annotation |

### Mark — Visual Mark

Marks determine how data is **visualized**. Each Mark type corresponds to a chart type:

```csharp
new IntervalMark()  // bar chart
new LineMark()      // line chart
new PieMark()       // pie chart
new PointMark()     // scatter plot
```

### Scale — Data Mapping

Scales control how data values map to visual space:

```csharp
new LinearScale(0, 100)       // linear mapping [0, 100] → [0, 1]
new LogScale(1, 10000)        // logarithmic mapping
new OrdinalScale()            // categorical mapping (auto-inferred)
new DivergingColorScale(-1,1) // diverging color scale
```

In most cases scales are **auto-inferred** — no manual specification needed.

## Your First Chart: Bar Chart

### Step 1: Prepare Data

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

### Step 2: Build the Chart

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

These few lines accomplish:
1. **Data** — bind the data source
2. **Mark** — select bar chart mark
3. **Encode** — map `month` to X axis, `revenue` to Y axis
4. **Axis** — configure axis titles
5. **Render** — execute rendering

### Step 3: Add Color Dimension

When data contains multiple series (e.g., Revenue and Cost), use `Channel.Color` to map series names to colors:

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

## Your First Chart: Line Chart

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

## Your First Chart: Pie Chart

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

## Adding a Theme

GodotChart ships with built-in dark and light themes:

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

Themes can be edited visually in the Godot Inspector as `Resource` objects — all properties are `[Export]` decorated.

## Adding Animation

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

## Next Steps

- [Chart Types](chart-types.md) — Learn how to build all 19 chart types
- [Customization & Theming](customization.md) — Deep dive into themes, scales, axes
- [Advanced Features](advanced.md) — Animation, interaction, real-time streaming
