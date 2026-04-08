# Chart Types

GodotChart supports 19 chart types across four coordinate systems. This guide provides property descriptions and complete construction examples for each type.

---

## Cartesian Charts

### Bar Chart — IntervalMark

The fundamental categorical comparison chart, supporting vertical/horizontal orientation and stacking.

**Properties:**

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `BarPadding` | float | 0.2 | Spacing ratio between bars |
| `CornerRadius` | float | 3 | Rounded corner radius |
| `ShowLabel` | bool | false | Display data labels |
| `ShowValue` | bool | false | Display value text |
| `Stack` | StackMode | None | Stacking mode (None / Stack / Normalize) |
| `Orientation` | BarOrientation | Vertical | Direction (Vertical / Horizontal) |

**Basic Bar Chart:**

```csharp
new Chart(canvas)
    .Data(data)
    .Mark(new IntervalMark { CornerRadius = 3, BarPadding = 0.25f, ShowLabel = true })
    .Encode(Channel.X, "month")
    .Encode(Channel.Y, "value")
    .Encode(Channel.Color, "type")
    .XAxis(new AxisConfig { Title = "Month" })
    .YAxis(new AxisConfig { Title = "Amount", Unit = "USD" })
    .Legend(new LegendConfig { Position = LegendPosition.Top })
    .Render();
```

**Horizontal Bar Chart:**

```csharp
new Chart(canvas)
    .Data(data)
    .Mark(new IntervalMark
    {
        CornerRadius = 3,
        BarPadding = 0.25f,
        Orientation = BarOrientation.Horizontal,
    })
    .Encode(Channel.X, "value")    // X = numeric
    .Encode(Channel.Y, "month")    // Y = category
    .XAxis(new AxisConfig { Title = "Amount", Unit = "USD" })
    .YAxis(new AxisConfig { Title = "Month" })
    .Render();
```

**Stacked Bar Chart:**

```csharp
new Chart(canvas)
    .Data(combined)
    .Mark(new IntervalMark
    {
        CornerRadius = 2,
        BarPadding = 0.25f,
        Stack = StackMode.Stack,    // ← key option
    })
    .Encode(Channel.X, "month")
    .Encode(Channel.Y, "value")
    .Encode(Channel.Color, "type")
    .Legend(new LegendConfig { Position = LegendPosition.Top })
    .Render();
```

**Normalized (100%) Stacked:** Set `Stack` to `StackMode.Normalize` to normalize the Y axis to 0-100%.

---

### Line Chart — LineMark

Continuous data trend visualization with smooth curves, area fills, and step lines.

**Properties:**

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `StrokeWidth` | float | 2 | Line thickness |
| `Smooth` | bool | true | Catmull-Rom curve smoothing |
| `ShowArea` | bool | false | Fill area under line |
| `AreaOpacity` | float | 0.15 | Area fill opacity |
| `Stack` | StackMode | None | Stacking mode |
| `Step` | StepMode | None | Step mode (None / After / Before / Center) |
| `YChannel` | Channel | Y | Y channel binding (for dual axis) |

**Multi-Series Line:**

```csharp
new Chart(canvas)
    .Data(multiSeriesData)
    .Mark(new LineMark { Smooth = true, StrokeWidth = 2.5f })
    .Encode(Channel.X, "month")
    .Encode(Channel.Y, "value")
    .Encode(Channel.Color, "series")
    .XAxis(new AxisConfig { Title = "Month" })
    .YAxis(new AxisConfig { Title = "Sales", Unit = "Units" })
    .Legend(new LegendConfig { Position = LegendPosition.Top })
    .Render();
```

**Area Chart:**

```csharp
new Chart(canvas)
    .Data(data)
    .Mark(new LineMark
    {
        Smooth = true,
        ShowArea = true,
        AreaOpacity = 0.25f,
        StrokeWidth = 3f,
    })
    .Encode(Channel.X, "month")
    .Encode(Channel.Y, "value")
    .Render();
```

**Stacked Area:**

```csharp
new Chart(canvas)
    .Data(multiSeriesData)
    .Mark(new LineMark
    {
        Smooth = true,
        ShowArea = true,
        AreaOpacity = 0.4f,
        Stack = StackMode.Stack,
    })
    .Encode(Channel.X, "month")
    .Encode(Channel.Y, "value")
    .Encode(Channel.Color, "series")
    .Render();
```

**Step Line:**

```csharp
new Chart(canvas)
    .Data(data)
    .Mark(new LineMark
    {
        Smooth = false,
        StrokeWidth = 2.5f,
        Step = StepMode.After,  // After / Before / Center
    })
    .Encode(Channel.X, "month")
    .Encode(Channel.Y, "value")
    .Encode(Channel.Color, "series")
    .Render();
```

---

### Scatter / Bubble — PointMark

Visualize relationships between two or more variables. Add `Size` and `Opacity` channels for bubble charts.

**Properties:**

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `DefaultRadius` | float | 5 | Default point radius |

**Bubble Chart:**

```csharp
new Chart(canvas)
    .Data(bubbleData)
    .Mark(new PointMark { DefaultRadius = 6f })
    .Encode(Channel.X, "x")
    .Encode(Channel.Y, "y")
    .Encode(Channel.Size, "marketShare")       // ← size by field
    .Encode(Channel.Color, "category")
    .Encode(Channel.Opacity, "confidence")     // ← opacity by field
    .Scale(Channel.X, new LinearScale(0, 100))
    .Scale(Channel.Y, new LinearScale(0, 100))
    .Render();
```

---

### Candlestick — CandlestickMark

Financial OHLC (Open/High/Low/Close) data visualization.

**Properties:**

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `OpenField` | string | "open" | Open price field |
| `HighField` | string | "high" | High price field |
| `LowField` | string | "low" | Low price field |
| `CloseField` | string | "close" | Close price field |
| `BodyWidthRatio` | float | 0.55 | Candle body width ratio |
| `WickWidth` | float | 1.5 | Wick line width |
| `CornerRadius` | float | 1 | Body corner radius |
| `BullishColor` | Color | — | Bullish candle color |
| `BearishColor` | Color | — | Bearish candle color |

```csharp
new Chart(canvas)
    .Data(stockData)
    .Mark(new CandlestickMark
    {
        OpenField = "open",
        HighField = "high",
        LowField = "low",
        CloseField = "close",
        BodyWidthRatio = 0.55f,
        WickWidth = 1.5f,
    })
    .Encode(Channel.X, "date")
    .Render();
```

---

### Box Plot — BoxMark

Five-number summary (Min, Q1, Median, Q3, Max) statistical distribution.

**Properties:**

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `MinField` | string | "min" | Min value field |
| `Q1Field` | string | "q1" | First quartile field |
| `MedianField` | string | "median" | Median field |
| `Q3Field` | string | "q3" | Third quartile field |
| `MaxField` | string | "max" | Max value field |
| `BoxWidthRatio` | float | 0.5 | Box width ratio |
| `CornerRadius` | float | 2 | Corner radius |

```csharp
new Chart(canvas)
    .Data(boxPlotData)
    .Mark(new BoxMark
    {
        MinField = "min", Q1Field = "q1",
        MedianField = "median", Q3Field = "q3", MaxField = "max",
        BoxWidthRatio = 0.5f,
    })
    .Encode(Channel.X, "stat")
    .Encode(Channel.Y, "median")
    .Scale(Channel.Y, new LinearScale(0, 100))
    .XAxis(new AxisConfig { Title = "Stat" })
    .YAxis(new AxisConfig { Title = "Value" })
    .Render();
```

---

### Violin — ViolinMark

Probability density distribution visualization — richer than box plots.

**Properties:**

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `BinCount` | int | 15 | Number of histogram bins |
| `WidthRatio` | float | — | Violin width ratio |
| `FillOpacity` | float | — | Fill opacity |
| `ShowMedian` | bool | true | Show median line |
| `ShowBox` | bool | true | Show embedded box |

```csharp
new Chart(canvas)
    .Data(violinData)
    .Mark(new ViolinMark { BinCount = 15, ShowMedian = true, ShowBox = true })
    .Encode(Channel.X, "weapon")
    .Encode(Channel.Y, "damage")
    .Encode(Channel.Color, "weapon")
    .XAxis(new AxisConfig { Title = "Weapon" })
    .YAxis(new AxisConfig { Title = "Damage" })
    .Render();
```

---

### Heatmap — HeatmapMark

Matrix-style colored cell chart with color-encoded values.

**Properties:**

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `CellGap` | float | 2 | Grid gap |
| `CornerRadius` | float | 3 | Corner radius |
| `ShowLabel` | bool | — | Show values |

```csharp
new Chart(canvas)
    .Data(heatmapData)
    .Mark(new HeatmapMark { CellGap = 2f, CornerRadius = 3f, ShowLabel = true })
    .Encode(Channel.X, "x")
    .Encode(Channel.Y, "y")
    .Encode(Channel.Color, "value")
    .Render();
```

**Diverging Color Heatmap:**

```csharp
new Chart(canvas)
    .Data(heatmapData)
    .Mark(new HeatmapMark { CellGap = 2f, CornerRadius = 3f, ShowLabel = true })
    .Encode(Channel.X, "x")
    .Encode(Channel.Y, "y")
    .Encode(Channel.Color, "value")
    .Scale(Channel.Color, new DivergingColorScale(-1, 1)) // ← diverging scale
    .Render();
```

---

### Range Area — RangeAreaMark

Display upper/lower bounds (temperature ranges, confidence intervals).

**Properties:**

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `LowerField` | string | — | Lower bound field name |
| `FillOpacity` | float | 0.3 | Fill opacity |
| `ShowBorderLines` | bool | true | Show border lines |
| `StrokeWidth` | float | 2 | Border line width |
| `Smooth` | bool | true | Smooth curves |

```csharp
new Chart(canvas)
    .Data(rangeData)
    .Mark(new RangeAreaMark
    {
        LowerField = "lower",
        FillOpacity = 0.3f,
        ShowBorderLines = true,
        Smooth = true,
    })
    .Encode(Channel.X, "day")
    .Encode(Channel.Y, "high")   // upper bound
    .Scale(Channel.Y, new LinearScale(10, 40))
    .XAxis(new AxisConfig { Title = "Day" })
    .YAxis(new AxisConfig { Title = "Temperature", Unit = "°C" })
    .Render();
```

---

### Timeline — TimelineMark

Gantt-style time interval chart.

**Properties:**

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `StartField` | string | — | Start time field |
| `EndField` | string | — | End time field |
| `BarHeightRatio` | float | 0.6 | Bar height ratio |
| `CornerRadius` | float | 4 | Corner radius |
| `ShowLabel` | bool | — | Show labels |

```csharp
new Chart(canvas)
    .Data(timelineData)
    .Mark(new TimelineMark
    {
        StartField = "start",
        EndField = "end",
        BarHeightRatio = 0.6f,
        CornerRadius = 4f,
        ShowLabel = true,
    })
    .Encode(Channel.Y, "buff")
    .Encode(Channel.Color, "type")
    .Scale(Channel.X, new LinearScale(0, 14))
    .XAxis(new AxisConfig { Title = "Time", Unit = "s" })
    .YAxis(new AxisConfig { Title = "Buff" })
    .Render();
```

---

### Lollipop — LollipopMark

Dot + stem combination bar chart variant.

**Properties:**

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `DotRadius` | float | 7 | Dot radius |
| `StemWidth` | float | 2.5 | Stem width |
| `Orientation` | BarOrientation | Vertical | Direction |

```csharp
new Chart(canvas)
    .Data(data)
    .Mark(new LollipopMark { DotRadius = 7f, StemWidth = 2.5f })
    .Encode(Channel.X, "month")
    .Encode(Channel.Y, "value")
    .Encode(Channel.Color, "constant:Revenue")
    .XAxis(new AxisConfig { Title = "Month" })
    .YAxis(new AxisConfig { Title = "Revenue", Unit = "USD" })
    .Render();
```

> **Tip:** The `"constant:Revenue"` syntax binds a constant value instead of a data field — useful for fixed color labels in single-series charts.

---

## Polar Charts

### Pie / Donut — PieMark

Categorical proportion visualization. Set `InnerRadius > 0` for donut chart.

**Properties:**

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `InnerRadius` | float | 0 | 0=pie, >0=donut |
| `StartAngle` | float | -π/2 | Start angle (12 o'clock) |
| `ShowLabel` | bool | true | Show segment labels |
| `LabelDistance` | float | 1.15 | Label distance from edge |
| `ExplodeRatio` | float | 0.03 | Hover explode distance |
| `RadiusFactor` | float | 0.85 | Max radius factor |
| `CenterText` | string? | null | Donut center text |
| `CenterFontSize` | float | 18 | Center text font size |
| `CenterContentBuilder` | Func? | null | Custom center content |

**Pie Chart:**

```csharp
new Chart(canvas)
    .Data(data)
    .Mark(new PieMark { ShowLabel = true, LabelDistance = 1.18f })
    .Encode(Channel.X, "type")
    .Encode(Channel.Y, "value")
    .Encode(Channel.Color, "type")
    .Legend(new LegendConfig { Position = LegendPosition.Top })
    .Render();
```

**Donut with Center Text:**

```csharp
new Chart(canvas)
    .Data(data)
    .Mark(new PieMark
    {
        InnerRadius = 0.5f,
        ShowLabel = true,
        CenterText = "Total:\n100K",
    })
    .Encode(Channel.X, "category")
    .Encode(Channel.Y, "value")
    .Render();
```

---

### Radar — RadarMark

Multi-dimensional spider web chart for attribute comparison.

**Properties:**

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `FillOpacity` | float | 0.2 | Area fill opacity |
| `StrokeWidth` | float | 2.5 | Line width |
| `PointRadius` | float | 4 | Data point radius |
| `GridRings` | int | — | Number of grid rings |
| `ShowAxisLabels` | bool | — | Show axis labels |

```csharp
new Chart(canvas)
    .Data(radarData)
    .Mark(new RadarMark { FillOpacity = 0.2f, StrokeWidth = 2.5f, PointRadius = 4f })
    .Encode(Channel.X, "stat")
    .Encode(Channel.Y, "value")
    .Encode(Channel.Color, "player")
    .Scale(Channel.Y, new LinearScale(0, 100))
    .Legend(new LegendConfig { Position = LegendPosition.Top })
    .Render();
```

---

### Gauge — GaugeMark

Single-value arc meter for progress, health, and metric displays.

**Properties:**

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `ArcWidth` | float | 0.14 | Arc width ratio |
| `StartAngleDeg` | float | — | Start angle (degrees) |
| `EndAngleDeg` | float | — | End angle (degrees) |
| `ShowCenterLabel` | bool | — | Show center value |
| `ValueColor` | Color | — | Value arc color |
| `TrackColor` | Color | — | Track background color |

```csharp
new Chart(canvas)
    .Data(new List<DataRow> { new DataRow().Set("label", "HP").Set("value", 75) })
    .Mark(new GaugeMark
    {
        ArcWidth = 0.14f,
        ShowCenterLabel = true,
        ValueColor = new Color(0.29f, 0.85f, 0.60f),
    })
    .Encode(Channel.X, "label")
    .Encode(Channel.Y, "value")
    .Scale(Channel.Y, new LinearScale(0, 100))
    .Render();
```

---

### Funnel — FunnelMark

Conversion pipeline visualization with decreasing stages.

**Properties:**

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `StageGap` | float | — | Gap between stages |
| `MinWidthRatio` | float | — | Minimum stage width ratio |
| `CornerRadius` | float | — | Corner radius |
| `ShowLabel` | bool | — | Show labels |

```csharp
var funnelData = new List<DataRow>
{
    new DataRow().Set("stage", "Visit").Set("count", 10000),
    new DataRow().Set("stage", "Browse").Set("count", 6000),
    new DataRow().Set("stage", "Cart").Set("count", 3000),
    new DataRow().Set("stage", "Purchase").Set("count", 1200),
};

new Chart(canvas)
    .Data(funnelData)
    .Mark(new FunnelMark { ShowLabel = true })
    .Encode(Channel.X, "stage")
    .Encode(Channel.Y, "count")
    .Render();
```

---

## Hierarchical Charts

### Treemap — TreemapMark

Nested rectangles representing hierarchical data proportions.

**Properties:**

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `CellGap` | float | 3 | Gap between cells |
| `CornerRadius` | float | 4 | Corner radius |
| `ShowLabel` | bool | — | Show labels |

```csharp
new Chart(canvas)
    .Data(treemapData)
    .Mark(new TreemapMark { ShowLabel = true, CellGap = 3f, CornerRadius = 4f })
    .Encode(Channel.X, "category")
    .Encode(Channel.Y, "amount")
    .Encode(Channel.Color, "category")
    .Render();
```

---

### Sunburst — SunburstMark

Hierarchical pie chart expanding from center outward.

**Properties:**

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `ParentField` | string | — | Parent node field |
| `RadiusFactor` | float | — | Max radius factor |
| `InnerRadiusRatio` | float | — | Inner circle radius ratio |
| `RingGap` | float | — | Gap between rings |
| `ShowLabel` | bool | — | Show labels |

```csharp
new Chart(canvas)
    .Data(sunburstData)
    .Mark(new SunburstMark { ShowLabel = true })
    .Encode(Channel.X, "skill")
    .Encode(Channel.Y, "points")
    .Encode(Channel.Color, "skill")
    .Render();
```

---

## Flow Charts

### Sankey — SankeyMark

Visualize flow distribution between nodes.

**Properties:**

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `SourceField` | string | — | Source node field |
| `TargetField` | string | — | Target node field |
| `NodeWidth` | float | 14 | Node width |
| `ColumnGap` | float | — | Column gap |
| `NodeGap` | float | — | Node gap |
| `ShowLabel` | bool | — | Show labels |

```csharp
var sankeyData = new List<DataRow>
{
    new DataRow().Set("source", "Mine").Set("target", "Furnace").Set("amount", 100),
    new DataRow().Set("source", "Furnace").Set("target", "Workshop").Set("amount", 60),
    new DataRow().Set("source", "Furnace").Set("target", "Market").Set("amount", 40),
};

new Chart(canvas)
    .Data(sankeyData)
    .Mark(new SankeyMark { ShowLabel = true, NodeWidth = 14f })
    .Encode(Channel.Y, "amount")
    .Encode(Channel.Color, "source")
    .Render();
```

---

### Chord — ChordMark

Circular relationship diagram showing inter-entity relationship strength.

**Properties:**

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `SourceField` | string | — | Source entity field |
| `TargetField` | string | — | Target entity field |
| `ArcWidthRatio` | float | — | Arc width ratio |
| `ArcGap` | float | — | Arc gap |
| `ShowLabel` | bool | — | Show labels |

```csharp
var chordData = new List<DataRow>
{
    new DataRow().Set("source", "Alliance").Set("target", "Horde").Set("trade", 50),
    new DataRow().Set("source", "Alliance").Set("target", "Neutral").Set("trade", 30),
    new DataRow().Set("source", "Horde").Set("target", "Neutral").Set("trade", 40),
};

new Chart(canvas)
    .Data(chordData)
    .Mark(new ChordMark { ShowLabel = true })
    .Encode(Channel.Y, "trade")
    .Encode(Channel.Color, "source")
    .Render();
```

---

## Special Charts

### Waffle — WaffleMark

Grid-based percentage/proportion display.

**Properties:**

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `TotalCells` | int | 100 | Total number of cells |
| `Columns` | int | 10 | Number of columns |
| `CellGap` | float | 3 | Cell gap |
| `CellRadius` | float | 3 | Cell corner radius |

```csharp
new Chart(canvas)
    .Data(data)
    .Mark(new WaffleMark { TotalCells = 100, Columns = 10, CellGap = 3f, CellRadius = 3f })
    .Encode(Channel.X, "type")
    .Encode(Channel.Y, "value")
    .Encode(Channel.Color, "type")
    .Render();
```

---

## Histogram — Data Transform

Histograms are not a standalone Mark but built using `BinTransform` + `IntervalMark`:

```csharp
var rawData = measurements.ConvertAll(r =>
    new DataRow().Set("value", r.Get<double>("damage")));

new Chart(canvas)
    .Data(rawData)
    .Transform(new BinTransform { Field = "value", BinCount = 12 })
    .Mark(new IntervalMark { CornerRadius = 2, BarPadding = 0.05f })
    .Encode(Channel.X, "BinMid")    // BinTransform outputs: BinStart, BinEnd, BinMid, Count
    .Encode(Channel.Y, "Count")
    .XAxis(new AxisConfig { Title = "Value" })
    .YAxis(new AxisConfig { Title = "Frequency" })
    .Render();
```

`BinTransform` automatically bins continuous values and outputs:
- `BinStart` — Bin left edge
- `BinEnd` — Bin right edge
- `BinMid` — Bin midpoint
- `Count` — Number of rows in the bin

---

## Composite Charts — Multiple Marks

Layer multiple Marks in the same chart (with compatible coordinate systems):

```csharp
var barMark = new IntervalMark { CornerRadius = 3, BarPadding = 0.3f };
var lineMark = new LineMark
{
    Smooth = true,
    StrokeWidth = 2.5f,
    YChannel = Channel.Y2,  // ← use secondary Y axis
};

new Chart(canvas)
    .Data(monthlySales)
    .Mark(barMark)    // first mark
    .Mark(lineMark)   // second mark
    .Encode(Channel.X, "month")
    .Encode(Channel.Y, "revenue")     // bar uses Y
    .Encode(Channel.Y2, "cost")       // line uses Y2
    .XAxis(new AxisConfig { Title = "Month" })
    .YAxis(new AxisConfig { Title = "Revenue", Unit = "USD" })
    .Y2Axis(new AxisConfig { Title = "Cost", Unit = "USD" })
    .Render();
```

> **Note:** Marks with different coordinate systems cannot be mixed (e.g., Cartesian + Polar). `ValidateMarkCompatibility()` automatically skips incompatible marks with a warning.
