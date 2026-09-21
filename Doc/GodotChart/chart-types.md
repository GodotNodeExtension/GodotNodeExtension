**English** | [中文](chart-types.cn.md)

# Chart Types

GodotChart supports 22 chart kinds (`ChartKind`) backed by 20 `Mark` classes (the annotation mark
`SectionMark` is the 21st subclass and backs no kind), grouped below by
coordinate system. The last group holds the waffle, which is a **Cartesian** mark that only turns the axes
off (`UsesAxes => false`) - it is listed separately because it draws a grid instead of a scale. This guide
gives the data shape, the field conventions and a construction example for every kind; the full property
list with its defaults lives in the [API Reference](api-reference.md#all-mark-subclasses).

## Data Shapes at a Glance

Every chart is fed by rows of key/value fields. The table says what **one row** means for each type and
which fields it reads. Channels (`X`, `Y`, `Color`, ...) are bound to your field names with
`Chart.Encode(channel, "field")`; the names below are the conventions `ChartView` uses by default.
A channel is a **binding, not a value**: both `Encode` and the node's `XField` / `YField` / `ColorField` /
... properties take the *name* of a field, and each row's value in that field drives that element. Types
that read their own fixed fields (`open`, `min`, `parent`, `start`, ...) are listed with those names -
they can be renamed through the matching property, not through a channel.

| Chart | One row is | Reads |
|-------|------------|-------|
| Bar (`IntervalMark`) | one bar | `X` category, `Y` value, `Color` series (for stacking) |
| Line (`LineMark`) | one vertex | `X`, `Y`, `Color` series |
| Area (`LineMark`) | one vertex | same as Line, plus a filled baseline |
| Scatter / Bubble (`PointMark`) | one point | `X`, `Y`, `Size` (chart-level encode), `Color`, `Shape` symbol |
| Candlestick | one period | `X` period + `open` `high` `low` `close` |
| Box plot | one five-number summary | `X` category + `min` `q1` `median` `q3` `max` |
| Violin | one sample (>= 2 rows per group) | `X` group, `Y` value |
| Heatmap | one cell | `X` column, `Y` row, `Color` value |
| Range area | one segment of one band | `X`, `Y` upper, `lower` |
| Timeline | one interval | category (on `Y`) + `start` `end` |
| Milestones (`MilestoneMark`) | one event | `X` position + `label`, optional `lane`, `Color`, `Shape` |
| Lollipop | one stem and dot | `X` category, `Y` value, `Shape` dot |
| Pie / Donut | one slice | `Y` value, `X` label |
| Radar | one vertex of a series | `X` dimension, `Y` value, `Color` series |
| Gauge | the whole chart (**first row only**) | `Y` value |
| Funnel | one stage (row order matters) | `Y` value, `X` label |
| Treemap | one node | `X` label, `Y` value, `parent` |
| Sunburst | one ring node | `X` label, `Y` value, `parent` |
| Sankey | one flow | `source` `target` + `Y` weight |
| Chord | one chord | `source` `target` + `Y` weight |
| Waffle | one category's share of the grid | `Y` weight, `Color` |

---

## Cartesian Charts

### Bar Chart — IntervalMark

The fundamental categorical comparison chart, supporting vertical/horizontal orientation and stacking.

**Data:** one row = one bar.

| Channel / field | Required | Meaning |
|-----------------|----------|---------|
| `X` → category field | yes | The category slot. This column must end up on an **ordinal** scale: a numeric category column is auto-fitted as a linear scale and the mark then draws nothing. |
| `Y` → value field | yes | Numeric bar height. A row missing either field is skipped; a row whose value is not numeric is skipped too, and the field warns once. |
| `Color` → series field | with `Stack` | Series key. Stacking needs this channel: without it every row lands in one series and bars of the same category simply overlap. |
| `Opacity` → opacity field | no | Per-row opacity. |

```json
{ "category": "Jan", "value": 120 }                        // plain bar
{ "category": "Jan", "series": "North", "value": 120 }     // stacked segment
```

For a **horizontal** bar the value goes on `X` and the category on `Y` (`Encode(Channel.X, "value")`,
`Encode(Channel.Y, "category")`) - the orientation only swaps the axes, it does not swap the fields for
you.

**Properties:** every property with its default is listed once, in
[IntervalMark](api-reference.md#intervalmark). The examples above set the bar geometry
(`BarPadding`, `CornerRadius`), the labels (`ShowLabel` / `ShowValue`) and the layout (`Stack` /
`Orientation`).

**Basic Bar Chart:**

```csharp compile
var data = new List<DataRow>
{
    new DataRow().Set("month", "A").Set("value", 10).Set("type", "s1"),
    new DataRow().Set("month", "B").Set("value", 20).Set("type", "s1"),
};
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

```csharp compile
var data = new List<DataRow>
{
    new DataRow().Set("value", 10).Set("month", "A"),
    new DataRow().Set("value", 20).Set("month", "B"),
};
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

```csharp compile
var combined = new List<DataRow>
{
    new DataRow().Set("month", "A").Set("value", 10).Set("type", "s1"),
    new DataRow().Set("month", "B").Set("value", 20).Set("type", "s1"),
};
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

**Normalized Stacked:** Set `Stack` to `StackMode.Normalize` to normalize the Y axis to 0–1 (the axis shows decimals; each category sums to 1). A category whose **visible** total is <= 0 is drawn as zero and reported with a warning (hidden series do not count towards the total), and an all-negative stack puts the axis floor below zero.

**Grouped Bars:** set `GroupedBars = true` (`ChartView.GroupedBars` in the inspector) to lay the series of one category side by side instead of letting them overlap - the sub-bands follow the colour field's series, and the flag has no effect while `Stack` is on.

---

### Line Chart — LineMark

Continuous data trend visualization with smooth curves, area fills, and step lines.

**Data:** one row = one vertex; all rows of a series (`Color` group) join into one line.

| Channel / field | Required | Meaning |
|-----------------|----------|---------|
| `X` → position field | yes | Any scale (category, numeric or time). |
| `Y` → value field | yes | Numeric. Rows missing a field are skipped and the line bridges the gap. |
| `Color` → series field | no | One line per distinct value; without it every row is one single line. |

```json
{ "category": "Jan", "series": "North", "value": 120 }
```

Points are connected in **row order** (nothing is sorted by X), and a series with fewer than two valid
points is not drawn at all. `Smooth` is on by default (a curved line); set `Smooth = false` for straight
segments. `ShowArea` (the Area chart) fills down to the data zero line and needs a backend with gradient
support.

**Properties:** the full list with defaults is in [LineMark](api-reference.md#linemark). The
examples above use `StrokeWidth` and `Smooth`, `ShowArea` / `AreaOpacity` for the fill, `Step` for a
step line and `Stack` for a stacked area (`StackMode.Normalize` follows the same zero-total rule as the
bar chart); `YChannel` points the line at the right axis - see [Dual Y
Axis](advanced.md#dual-y-axis). In `Stack` mode the hover snap follows the accumulated baseline (the same
layout the renderer draws), and with `Step` both edges of an area share one interpolation, so the band
never leaks.

**Multi-Series Line:**

```csharp compile
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

```csharp compile
var data = new List<DataRow>
{
    new DataRow().Set("month", "A").Set("value", 10),
    new DataRow().Set("month", "B").Set("value", 20),
};
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

```csharp compile
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

```csharp compile
var data = new List<DataRow>
{
    new DataRow().Set("month", "A").Set("value", 10).Set("series", "s1"),
    new DataRow().Set("month", "B").Set("value", 20).Set("series", "s1"),
};
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

**Data:** one row = one point (bubbles add the size channel).

| Channel / field | Required | Meaning |
|-----------------|----------|---------|
| `X` / `Y` → position fields | yes | Numeric or category; a row missing either field is skipped. |
| `Size` → size field | no | Bubble size. Encode it on the **chart** (`Encode(Channel.Size, "size")`); the radius maps linearly onto 3-23 px. A row without the field falls back to `DefaultRadius`. |
| `Color` → colour field | no | Point colour and legend. |
| `Opacity` → opacity field | no | Replaces the default 0.8 opacity when present. |
| `Shape` → shape field | no | Point symbol: the categories of the chart's shape scale take the built-in vocabulary (circle, square, triangle, diamond, cross, star) in first-seen order. A missing or unknown value draws a circle. |

```json
{ "category": "A", "value": 12, "size": 30 }
```

**Properties:** the full list with defaults is in [PointMark](api-reference.md#pointmark).
`DefaultRadius` is the dot radius used when a row carries no `Size` value; everything else about a
bubble comes from the chart-level `Size` encode (`SizeRange` / `SizeField`).

**Bubble Chart:**

```csharp compile
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

**Data:** one row = one period with a complete OHLC quadruple.

| Channel / field | Required | Meaning |
|-----------------|----------|---------|
| `X` → period field | yes | Category (an ordinal scale). |
| `open` `high` `low` `close` | yes | Read by name (`OpenField` … `CloseField`). A row missing any of the four is skipped - there is no close-only fallback. |
| `Y` → value field | no | Only feeds the auto-fitted scale; the axis range comes from low/high. |
| `Color` → colour field | no | Replaces the up/down colour of the body and the wick. Without it the bull/bear colours are used. |

```json
{ "date": "2024-01-02", "open": 102, "high": 110, "low": 99, "close": 107 }
```

`close >= open` counts as bullish, so a doji (`open == close`) uses the bullish colour and a 1 px body.

**Properties:** the full list with defaults is in
[CandlestickMark](api-reference.md#candlestickmark). The four field names (`OpenField` …
`CloseField`) are what you rename when your rows use other keys; `BodyWidthRatio`, `WickWidth` and
`CornerRadius` shape the candle, and `BullishColor` / `BearishColor` override the up/down colours.

```csharp compile
new Chart(canvas)
    .Data(stockData)
    .Mark(new CandlestickMark
    {
        OpenField = "open",
        HighField = "high",
        LowField = "low",
        CloseField = "close",
        BodyWidthRatio = 0.6f,
        WickWidth = 1.5f,
    })
    .Encode(Channel.X, "date")
    .Render();
```

---

### Box Plot — BoxMark

Five-number summary (Min, Q1, Median, Q3, Max) statistical distribution.

**Data:** one row = one already-computed five-number summary (the mark does no statistics).

| Channel / field | Required | Meaning |
|-----------------|----------|---------|
| `X` → category field | yes | Category (ordinal scale). |
| `min` `q1` `median` `q3` `max` | yes | Read by name (`MinField` … `MaxField`); a row missing any of them, or holding a non-finite value, disappears. |
| `Y` → value field | no | Not used for the geometry; the Y range is contributed by the mark itself (so the axis is not squeezed to zero). |

```json
{ "stat": "Attack", "min": 12, "q1": 25, "median": 34, "q3": 41, "max": 55 }
```

**Properties:** the full list with defaults is in [BoxMark](api-reference.md#boxmark). The five
field names (`MinField` … `MaxField`) are the per-row keys to rename; `BoxWidthRatio` and
`CornerRadius` shape the box.

```csharp compile
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

**Data:** one row = one sample; rows sharing an X category pool into one violin.

| Channel / field | Required | Meaning |
|-----------------|----------|---------|
| `X` → group field | yes | Category (ordinal scale); it groups the samples. |
| `Y` → value field | yes | Numeric sample value; non-finite values are dropped. |
| `Color` → colour field | no | Only colours the group (it does not split a group into several violins). |

```json
{ "weapon": "Sword", "damage": 13 }
{ "weapon": "Sword", "damage": 21 }
{ "weapon": "Sword", "damage": 17 }        // one violin = several rows with the same X
```

A group needs at least two distinct samples: a single row - or a group whose values are all equal - has no
density outline and is drawn as a minimal visible line at its value (reported once with a warning). Every
other group gets a Gaussian kernel density estimate evaluated on `BinCount` grid points, so it stays a
continuous body even when it only holds a handful of samples (and the value axis is widened to cover the
density tails). The median is drawn as a dot, coloured and sized by `ChartTheme.ViolinMedianDotColor` /
`ChartTheme.ViolinMedianDotRadius`.

**Properties:** the full list with defaults is in [ViolinMark](api-reference.md#violinmark).
`BinCount` is the resolution of the density outline (minimum 8), `WidthRatio` the violin's share of
its slot, `FillOpacity` the fill and `ShowMedian` / `ShowBox` the embedded summary.

```csharp compile
new Chart(canvas)
    .Data(violinData)
    .Mark(new ViolinMark { BinCount = 20, ShowMedian = true, ShowBox = true })
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

**Data:** one row = one matrix cell.

| Channel / field | Required | Meaning |
|-----------------|----------|---------|
| `X` → column field | yes | Column category. Must be an ordinal scale: numeric columns are auto-fitted as linear and nothing is drawn. |
| `Y` → row field | yes | Row category (ordinal). Rows follow the Y axis, which runs bottom-up: the first category sits at the bottom, next to its label. |
| `Color` → value field | yes | Numeric cell value; the mark swaps the colour channel to a sequential scale and clamps out-of-range values. |

```json
{ "x": "Mon", "y": "09:00", "value": 12.5 }
```

Duplicate `(x, y)` rows are painted in order (the last one wins); they are not summed.

**Properties:** the full list with defaults is in [HeatmapMark](api-reference.md#heatmapmark).
`CellGap` and `CornerRadius` shape the cells and `ShowLabel` prints the value inside a cell.

```csharp compile
new Chart(canvas)
    .Data(heatmapData)
    .Mark(new HeatmapMark { CellGap = 2f, CornerRadius = 3f, ShowLabel = true })
    .Encode(Channel.X, "x")
    .Encode(Channel.Y, "y")
    .Encode(Channel.Color, "value")
    .Render();
```

**Diverging Color Heatmap:**

```csharp compile
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

**Data:** one row = one segment of a single band (upper and lower bound).

| Channel / field | Required | Meaning |
|-----------------|----------|---------|
| `X` → position field | yes | Any scale. |
| `Y` → upper bound | yes | Numeric; a numeric (`LinearScale`) channel is required. |
| `lower` | yes | Lower bound, read by name (`LowerField`). The mark always draws **one** band: `Color` only tints it (taken from the first row). |

```json
{ "day": "Jan", "high": 30, "lower": 18 }
```

The band contributes **both** of its edges to the auto-fitted Y range - the value the `Y` channel encodes
and `lower` - so the whole band stays inside the plot. `Opacity` is ignored here - use `FillOpacity`.

**Properties:** the full list with defaults is in [RangeAreaMark](api-reference.md#rangeareamark).
`LowerField` renames the lower-bound key, `FillOpacity` sets the band's fill (this mark does not
read the `Opacity` channel), and `ShowBorderLines` / `StrokeWidth` / `Smooth` style its border.

```csharp compile
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

**Data:** one row = one interval on one category line.

| Channel / field | Required | Meaning |
|-----------------|----------|---------|
| category field | yes | Encode it on `Y` (or `X` with an ordinal scale): the mark takes whichever channel is ordinal as the category and puts it on the vertical axis. |
| `start` `end` | yes | Read by name (`StartField` / `EndField`); the horizontal range. `end < start` is drawn with its endpoints swapped (reported once with a warning); an interval with `end == start` draws nothing. |
| `Y`/`X` value field | yes | The channel that is not the category carries the range on a numeric scale (the mark contributes that scale). |
| `Color` → series field | no | Bar colour and series visibility. |

```json
{ "buff": "Haste", "start": 2, "end": 6 }
```

Rows sharing a category overlap - the mark does not lay them out side by side. Note the axes: they are
deliberately crossed (categories vertical, ranges horizontal), so the fields are `Encode(Channel.Y,
"buff")` plus a numeric range channel - which is exactly what `ChartView` binds for `Kind = Timeline`
(`Y` ← category, `X` ← value).

**Properties:** the full list with defaults is in [TimelineMark](api-reference.md#timelinemark).
`StartField` / `EndField` rename the range keys, `BarHeightRatio` the bar's share of its lane and
`CornerRadius` the bar's corners.

```csharp compile
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

### Milestones — MilestoneMark

Event timeline: each row is a **single event** on the position axis, drawn as a marker plus its label,
optionally arranged into one lane per category.

**Data:** one row = one event.

| Channel / field | Required | Meaning |
|-----------------|----------|---------|
| `X` → position field | yes | Where the event sits on the axis - any scale, a `TimeScale` for real dates. A row whose position is missing or non-finite is skipped. |
| `Y` → lane field | no | Optional lane (`OrdinalScale`). Without it every event sits on the plot's middle line; with it each category gets its own line, the first category at the bottom (`TimelineMark` uses the same ordinal direction). |
| `Label` → text field | no | Event text. When encoded it wins over `LabelField`. |
| `label` | no | Event text read by name (`LabelField`, default `"label"`); only consulted when the `Label` channel is not encoded. |
| `Color` → colour field | no | Marker colour (and the legend). |
| `Shape` → shape field | no | Marker symbol, the same vocabulary as a scatter point. `MarkerShape` applies while the channel is **not** encoded; with it encoded, a row whose value is unknown - a missing value included - falls back to `Shapes[0]` (a circle). |

```json
{ "week": 2, "lane": "Release", "label": "v0.9" }
{ "week": 7, "lane": "Release", "label": "v1.0" }
{ "week": 3, "lane": "Infra",   "label": "CI" }
```

The label text is the `Label` channel first, then `LabelField`, then the rendered X value - so a row
without a label still shows where it sits. An event has **no value field**: `ChartView` binds no value
channel for `Kind = Milestone` (`X` ← `XField` or `"time"`, `Y` ← `YField` or `"lane"`, and `Color` ← the
colour field or `"series"` only when the rows carry it). Each lane draws **one** horizontal line (not one
per event); without lanes a single line crosses the middle. Labels alternate above and below their marker
(`AlternateLabels`) so neighbouring events stay apart, and the entry animation grows every marker out from
its line. The mark keeps its axes (`UsesAxes`), so a milestone timeline is read against the X axis it
shares. The position axis does not have to be numeric: an ordinal X puts every event on its category slot
(a phase or release-train timeline), and a `LogScale` works the same way.

**Properties:** the full list with defaults is in [MilestoneMark](api-reference.md#milestonemark).
`LabelField` renames the event-text key, `MarkerRadius` / `MarkerShape` size and shape the marker
(the shape only applies while the `Shape` channel is not encoded), `ShowAxisLine` / `AxisLineWidth`
the lane line, and `LabelOffset` / `AlternateLabels` / `ShowLabel` the labels.

A real date axis is a `TimeScale` on `Channel.X`. `ChartView` does not expose scale configuration, so set
it in code - and mind that a scale handed to `.Scale(...)` is used as-is, never auto-fitted, so `TimeScale`
needs its range (or an explicit `Fit`):

```csharp compile
new Chart(canvas)
    .Data(releases)
    .Mark(new MilestoneMark
    {
        LabelField = "label",
        MarkerRadius = 6f,
        MarkerShape = ShapeKind.Circle,
        AlternateLabels = true,
    })
    .Encode(Channel.X, "time")
    .Encode(Channel.Y, "lane")
    .Encode(Channel.Color, "lane")
    .Scale(Channel.X, new TimeScale(new DateTime(2024, 1, 1), new DateTime(2024, 12, 31)))  // ← real date axis
    .XAxis(new AxisConfig { Title = "Date" })
    .YAxis(new AxisConfig { Title = "Stream" })
    .Render();
```

`TimeScale` reads a value as a `DateTime`, a `DateTimeOffset`, .NET ticks (any integral type), a Unix
millisecond stamp (a floating point value) or an invariant-culture date string; anything else throws -
keep non-date strings on a numeric or ordinal scale instead.

---

### Lollipop — LollipopMark

Dot + stem combination bar chart variant.

**Data:** one row = one stem plus its dot.

| Channel / field | Required | Meaning |
|-----------------|----------|---------|
| `X` → category field | yes | Category (ordinal scale) for vertical lollipops. |
| `Y` → value field | yes | Numeric. Every row needs **both** fields even though only one is used per orientation. |
| `Color` / `Opacity` | no | Per-row colour and opacity (no grouping). |
| `Shape` → shape field | no | Symbol of the dot (the same vocabulary as a scatter point); a missing or unknown value draws a circle. |

```json
{ "category": "A", "value": 12 }
```

**Properties:** the full list with defaults is in [LollipopMark](api-reference.md#lollipopmark).
`DotRadius` and `StemWidth` size the two parts and `Orientation` flips the chart.

```csharp compile
var data = new List<DataRow>
{
    new DataRow().Set("month", "A").Set("value", 10),
    new DataRow().Set("month", "B").Set("value", 20),
};
new Chart(canvas)
    .Data(data)
    .Mark(new LollipopMark { DotRadius = 5f, StemWidth = 2f })
    .Encode(Channel.X, "month")
    .Encode(Channel.Y, "value")
    .Encode(Channel.Color, "constant:#ff8800")
    .XAxis(new AxisConfig { Title = "Month" })
    .YAxis(new AxisConfig { Title = "Revenue", Unit = "USD" })
    .Render();
```

> **Tip:** The `"constant:"` prefix binds a constant value instead of a data field, and a constant
> **Color** channel paints the whole mark in that colour - `Encode(Channel.Color, Colors.Orange)` (the
> type-safe overload) or its string form `"constant:#ff8800"`. A constant that is *not* a colour (a name
> like `"constant:Revenue"`) is not parsed as one, so the mark keeps its default colour instead. A colour
> *field* whose values are colours (`Color` values or `"#rrggbb"` strings) uses them as they are.
> `StyleOverride`, e.g. `(row, i, style) => style.WithFill(color)`, stays the way to colour elements from
> code by index.

---

## Polar Charts

### Pie / Donut — PieMark

Categorical proportion visualization. Set `InnerRadius > 0` for donut chart.

**Data:** one row = one slice (a donut segment when `InnerRadius > 0`).

| Channel / field | Required | Meaning |
|-----------------|----------|---------|
| `Y` → value field | yes | Numeric slice size; rows with a non-finite value are dropped, and the whole chart is skipped when the total is not positive. |
| `X` → label field | no | Slice label; without it the value is printed. |
| `Color` → colour field | no | Slice colour and legend. Without it every slice keeps the theme's single default colour, so bind it (usually to the category). |

```json
{ "category": "A", "amount": 40 }
```

Negative values have **no** slice: a negative row is skipped and reported with a warning (it used to sweep
backwards). The total has to be positive, but a slice that is 100 % of it is still drawn - its sweep is
kept a hair below a full turn. Keep slice values >= 0.

**Properties:** the full list with defaults is in [PieMark](api-reference.md#piemark). `InnerRadius`
turns the pie into a donut, `StartAngle` / `LabelDistance` / `RadiusFactor` set the geometry (and with it the hit radius: the label ring is not hittable, and a sector the entry animation has not revealed yet cannot be hit),
`CenterText` (+ `CenterFontSize` / `CenterSubFontSize` / `CenterContentBuilder`) fills the donut
centre and `ExplodeRatio` is the hover explode offset (null = `ChartTheme.PieExplodeRatio`).

**Pie Chart:**

```csharp compile
var data = new List<DataRow>
{
    new DataRow().Set("type", "A").Set("value", 10),
    new DataRow().Set("type", "B").Set("value", 20),
};
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

```csharp compile
var data = new List<DataRow>
{
    new DataRow().Set("category", "A").Set("value", 10),
    new DataRow().Set("category", "B").Set("value", 20),
};
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

**Data:** one row = one vertex: a series' value on one dimension.

| Channel / field | Required | Meaning |
|-----------------|----------|---------|
| `X` → dimension field | yes | Axis/dimension name. The dimension list is the union of all X values, so every series must use the same names. |
| `Y` → value field | yes | Numeric radius. A dimension a series does not provide is drawn at the centre (0). |
| `Color` → series field | no | One polygon per distinct value. |

```json
{ "dim": "Speed", "value": 80, "team": "A" }
```

Fewer than three dimensions, or a Y channel that is not on a linear scale, means nothing is drawn.

**Properties:** the full list with defaults is in [RadarMark](api-reference.md#radarmark).
`GridRings` sets the number of rings, `FillOpacity` / `StrokeWidth` / `PointRadius` the polygon and
`ShowAxisLabels` the dimension labels.

```csharp compile
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

**Data:** the mark draws **the first row only** - extra rows are ignored.

| Channel / field | Required | Meaning |
|-----------------|----------|---------|
| `Y` → value field | yes | The needle value, on a linear scale. |
| `X` → label field | no | Not used by the mark. |

```json
{ "label": "HP", "value": 75 }
```

The arc shows where the value sits **inside the Y scale**, so give the scale its range explicitly
(`.Scale(Channel.Y, new LinearScale(0, 100))`); otherwise the auto-fitted domain (0..80 for a single
value of 75) makes the needle look almost full.

**Properties:** the full list with defaults is in [GaugeMark](api-reference.md#gaugemark). The arc
geometry is `ArcWidth` / `StartAngleDeg` / `EndAngleDeg`, the colours `ValueColor` / `TrackColor`,
and `ShowCenterLabel` toggles the value in the middle.

```csharp compile
new Chart(canvas)
    .Data(new List<DataRow> { new DataRow().Set("label", "HP").Set("value", 75) })
    .Mark(new GaugeMark
    {
        ArcWidth = 0.12f,
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

**Data:** one row = one stage, drawn top-to-bottom in **row order** (nothing is sorted).

| Channel / field | Required | Meaning |
|-----------------|----------|---------|
| `Y` → value field | yes | Stage size; the widest stage is the largest remaining value. |
| `X` → label field | no | Stage label. |
| `Color` → colour field | no | Per-stage colour. |

```json
{ "stage": "Visit", "count": 10000 }
{ "stage": "Signup", "count": 6000 }
```

Width is interpolated (`MinWidthRatio` is the floor), not area-proportional, and stage height is split
evenly across the rows - so the shape is driven by the row order, not by the values. Each stage is a
rectangle (there is no trapezoid taper) and the entry animation only scales its width.

**Properties:** the full list with defaults is in [FunnelMark](api-reference.md#funnelmark).
`StageGap` separates the stages, `MinWidthRatio` is the width floor of the narrowest one, and
`CornerRadius` / `ShowLabel` shape and label the bands.

```csharp compile
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

Rectangle areas represent values. Rows may name their parent through `ParentField`, which links them
into a tree (the same convention `SunburstMark` uses): a group owns a rectangle, keeps a header strip
for its label and its children split what is left, recursively. Rows without a parent stay a single
level, so flat data renders exactly as it always did.

**Data:** one row = one node rectangle; `parent` links rows into a tree.

| Channel / field | Required | Meaning |
|-----------------|----------|---------|
| `X` → label field | yes | Node label. |
| `Y` → value field | yes | Node value (clamped to >= 0). A group whose own value is not positive takes the sum of its children. |
| `parent` | no | Parent key, read by name (`ParentField`). Empty or unknown parents are top level; with no parent values at all the mark stays single-level. |
| `Color` → colour field | no | Cell colour. Without it a top-level cell takes a palette colour of its own (by row), while the cells of a group share the group's colour shaded by `SiblingShadeStep`. |

```json
{ "label": "North", "value": 0,   "parent": "" }
{ "label": "Q1",    "value": 30,  "parent": "North" }
{ "label": "Q2",    "value": 45,  "parent": "North" }
```

A group reserves `GroupHeaderHeight` pixels at the top for its label and its children split the rest. Rows
in a parent cycle are promoted to the top level (a warning is logged) so no row disappears.

**Properties:** the full list with defaults is in [TreemapMark](api-reference.md#treemapmark).
`ParentField` names the parent key, `LayoutMode` picks the tiling (`BinarySplit` / `Squarify`),
`CellGap` / `CornerRadius` shape the cells, `GroupHeaderHeight` reserves the group label strip and
`SiblingShadeStep` shades siblings.

```csharp compile
// Flat data: one level, one cell per row.
new Chart(canvas)
    .Data(treemapData)
    .Mark(new TreemapMark { ShowLabel = true, CellGap = 3f, CornerRadius = 4f })
    .Encode(Channel.X, "category")
    .Encode(Channel.Y, "amount")
    .Encode(Channel.Color, "category")
    .Render();

// Hierarchical data: the same kind of rows plus a parent field become a tree.
new Chart(canvas)
    .Data(diskUsage)   // folder / parent / size
    .Mark(new TreemapMark { ParentField = "parent", LayoutMode = TreemapLayoutMode.Squarify })
    .Encode(Channel.X, "folder")
    .Encode(Channel.Y, "size")
    .Render();
```

A group whose own value is not positive takes the sum of its children; a group row that carries a
positive value of its own uses that value instead. Rows whose parent is unknown, points at themselves
or sits in a parent cycle are promoted to the top level, so no row disappears from the chart.

---

### Sunburst — SunburstMark

Hierarchical pie chart expanding from center outward.

**Data:** one row = one ring segment; `parent` links rows into a tree.

| Channel / field | Required | Meaning |
|-----------------|----------|---------|
| `X` → label field | yes | Node label. |
| `Y` → value field | yes | Node value (clamped to >= 0); a group whose own value is not positive takes the sum of its children. |
| `parent` | no | Parent label, read by name (`ParentField`). Empty or unknown parents are roots. |
| `Color` → colour field | no | Arc colour. Without it one ring carries the branches and takes a palette colour per branch, while the rings below keep that colour shaded by depth. Several roots are the branches themselves; a **single** root is only the frame (the usual "total" row), so its children are the branches and it keeps the neutral mark colour. |

```json
{ "label": "Asia",  "value": 0,   "parent": "" }
{ "label": "China", "value": 120, "parent": "Asia" }
```

Rows that name the same label under the same parent overwrite each other, so keep labels unique inside a
parent. Broken parent links are cut and the affected nodes are promoted to the root (a warning is logged).

**Properties:** the full list with defaults is in [SunburstMark](api-reference.md#sunburstmark).
`ParentField` names the parent key, `RadiusFactor` / `InnerRadiusRatio` / `RingGap` set the geometry
and `DepthShadeStep` how much a deeper ring darkens (0 = one flat colour per branch).

```csharp compile
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

**Data:** one row = one flow between two nodes.

| Channel / field | Required | Meaning |
|-----------------|----------|---------|
| `source` `target` | yes | Node names, read by name (`SourceField` / `TargetField`). A row missing either is dropped. |
| `Y` → weight field | no | Flow weight (1 when the field is missing). Non-positive flows are dropped, so a zero-traffic branch cannot be drawn. |
| `Color` → colour field | no | Flow colour. Without it a flow takes the colour of its **source node** (one palette colour per node, in order of first appearance); the node bars keep the theme's `SankeyNodeColor`. |

```json
{ "source": "Landing", "target": "Signup", "value": 40 }
```

Nodes are placed by topological order. A cycle logs a warning: the layout walks the cycle and drops the
back edge that closes it, so every node of the cycle still gets a column of its own instead of stacking up
in the first one. Self loops are dropped silently. Node height follows the **larger** of the incoming and
outgoing totals, and a node's label is centred text placed beside it (to the left of the nodes in the last
column).

**Properties:** the full list with defaults is in [SankeyMark](api-reference.md#sankeymark).
`SourceField` / `TargetField` rename the flow keys, `NodeWidth` / `NodeGap` size the nodes and
`ColumnGap` packs the columns (0 = spread out, 1 = edge to edge).

```csharp compile
var sankeyData = new List<DataRow>
{
    new DataRow().Set("source", "Mine").Set("target", "Furnace").Set("amount", 100),
    new DataRow().Set("source", "Furnace").Set("target", "Workshop").Set("amount", 60),
    new DataRow().Set("source", "Furnace").Set("target", "Market").Set("amount", 40),
};

new Chart(canvas)
    .Data(sankeyData)
    .Mark(new SankeyMark { ShowLabel = true, NodeWidth = 16f })
    .Encode(Channel.Y, "amount")
    .Encode(Channel.Color, "source")
    .Render();
```

---

### Chord — ChordMark

Circular relationship diagram showing inter-entity relationship strength.

**Data:** one row = one chord (a relationship between two nodes).

| Channel / field | Required | Meaning |
|-----------------|----------|---------|
| `source` `target` | yes | Node names, read by name (`SourceField` / `TargetField`); a row with a missing field is dropped. |
| `Y` → weight field | no | Chord weight (1 when the field is missing). |
| `Color` → colour field | no | Chord colour. Without it the arcs take one palette colour per node and each chord carries the colour of the node it **leaves**. |

```json
{ "source": "Client", "target": "Backend", "value": 5 }
```

Nodes are laid out clockwise in order of first appearance, and a node angle covers the **sum** of its
incoming and outgoing chords (unlike Sankey's maximum). Self loops are not filtered and count twice.
The radius follows the smaller plot side, so a non-square panel makes the chart smaller.

**Properties:** the full list with defaults is in [ChordMark](api-reference.md#chordmark).
`SourceField` / `TargetField` rename the chord keys, `ArcWidthRatio` the thickness of the arcs and
`ArcGap` the gap between them.

```csharp compile
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

**Data:** one row = one category, whose value is its share of a fixed grid.

| Channel / field | Required | Meaning |
|-----------------|----------|---------|
| `Y` → weight field | yes | Share weight. Rows with a value <= 0 (or no value) are dropped. |
| `X` → tooltip label field | no | Only used for the hover text. |
| `Color` → colour field | no | Cell colour per category. |

```json
{ "category": "A", "value": 30 }
{ "category": "B", "value": 10 }
```

`TotalCells` cells (100 by default) are shared out by the largest-remainder method, so a row is **not** one
cell. Counting starts at the bottom-left corner. The grid uses no scale of its own and the mark declares
itself axis-free (`UsesAxes`), so a chart built from waffles draws no axes and no grid - but a chart that
mixes a waffle with a bar chart keeps them. That axis-free flag is the only thing special about the mark:
`WaffleMark.Coordinate` is `Cartesian` like a bar chart, it simply overrides `UsesAxes` to `false`.

**Properties:** the full list with defaults is in [WaffleMark](api-reference.md#wafflemark).
`TotalCells` is the size of the grid (shared out by the largest-remainder method) and `Columns` its
shape; `CellGap` / `CellRadius` style the cells.

```csharp compile
var data = new List<DataRow>
{
    new DataRow().Set("type", "A").Set("value", 10),
    new DataRow().Set("type", "B").Set("value", 20),
};
new Chart(canvas)
    .Data(data)
    .Mark(new WaffleMark { TotalCells = 100, Columns = 10, CellGap = 2f, CellRadius = 2f })
    .Encode(Channel.X, "type")
    .Encode(Channel.Y, "value")
    .Encode(Channel.Color, "type")
    .Render();
```

---

## Histogram — Data Transform

A histogram is not a standalone Mark: it is a `BinTransform` in front of an `IntervalMark`, so that the
bins become one bar each. The recipe - the transform call, the `BinStart` / `BinEnd` / `BinMid` / `Count`
output fields and the `OrdinalScale` a bar chart needs on its category axis - lives in
[Advanced Features → Data Transforms](advanced.md#data-transforms); it is not repeated here.

---

## Composite Charts — Multiple Marks

Several Marks in one chart, each with its own encoding, colour and scale (a bar chart plus a line on the
right axis, for instance). The recipes, the coordinate-system rules and the pitfalls are collected in
[Advanced Features → Composite Charts](advanced.md#composite-charts).
