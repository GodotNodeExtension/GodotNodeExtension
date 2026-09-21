**English** | [中文](advanced.cn.md)

# Advanced Features

This guide covers GodotChart's animation system, user interaction, tooltips, real-time data streaming, and data transforms.

---

## Animation System

GodotChart provides a complete animation pipeline: entry animations, data transitions, hover effects, and exit animations.

### AnimationController

`AnimationController` manages all animation state:

```csharp compile-members
private AnimationController _anim = new();
```

**Core Properties:**

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `EntryProgress` | float | 1 | Entry progress [0,1] |
| `SeriesProgress` | float[] | — | Per-series independent progress |
| `GlobalOpacity` | float | 1 | Overall opacity [0,1] |
| `EntryDuration` | float | 0.6 | Entry animation duration (seconds) |
| `SeriesStagger` | float | 0.1 | Delay between series |
| `AnimationThreshold` | int | 2000 | Auto-disable above this count |
| `HoverScale` | float | 1 | Hover scale factor |
| `ExitDuration` | float | 0.3 | Exit animation duration |
| `IsAnimating` | bool | — | Whether any animation is active |

### Entry Animation

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

Entry effects vary by Mark type:
- **IntervalMark** — Bars grow from baseline
- **LineMark** — Left-to-right clip reveal
- **PieMark** — Sweep from start angle
- **PointMark** — Fade in with scale

### Series Stagger

Use `SeriesStagger` for sequential series appearance - the example raises the default `0.1` (100 ms) to
`0.15` (150 ms):

```csharp compile
_anim.SeriesStagger = 0.15f;  // 150ms per series delay
_anim.StartEntry(host, seriesCount: 3);

// Each series gets independent progress:
// _anim.SeriesProgress[0], [1], [2] stagger over time
```

No built-in mark consumes `SeriesProgress`: every built-in mark animates off the one-dimensional
`EntryProgress`, so the staggered array above only matters to a custom mark that reads
`ctx.Animation.SeriesProgress`.

### Data Transition

> **Not wired yet:** `AnimationContext.DataTransitionProgress` is provided and
> `AnimationController.StartDataTransition` animates it, but no built-in mark consumes the value —
> a data change is currently applied instantly. Use the progress value from a custom mark if you
> need the interpolation today.
Trigger smooth transitions when data updates:

```csharp compile
_anim.StartDataTransition(host, duration: 0.4f);

// In render loop:
var ctx = new AnimationContext
{
    DataTransitionProgress = _anim.DataTransitionProgress,
};
chart.Animate(ctx).Render();
```

### Hover Animation

Scale effect responding to mouse hover:

```csharp compile
// On hover event:
_anim.AnimateHover(host, targetScale: 1.05f);

var ctx = new AnimationContext
{
    HoverScale = _anim.HoverScale,
};
```

### Exit Animation

```csharp compile
_anim.StartExit(host, onComplete: () =>
{
    GD.Print("Exit animation complete!");
    // Clean up or switch chart
});
```

### AnimationContext — Simplified API

You can also pass a simple `float` for entry-only animation:

```csharp compile
chart.Animate(0.5f).Render();  // 50% entry progress
```

Or use `AnimationContext.Default` to skip all animations.

### Easing Functions

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

## Rolling data with a steady axis

A stream that refits its value axis on every append makes the ticks, the grid lines and the scale jump with each
new row - the reader sees the chart move while the data does not. There are four ways to stop that, from
strongest to weakest:

1. **Pin the domain**: `ChartView.YAxisRange = Vector2(min, max)` (the scene) or
   `Chart.ScaleDomain(Channel.Y, min, max)` (code). The lock survives every refit, so ticks and grid stay put.
   The feeder usually knows the plausible band in advance; data that falls outside it is **not drawn** (the
   chart never stretches the axis for it), which is the honest presentation of "outside the range we agreed on".
2. **Pin the tick positions**: `AxisConfig.TickStep = 20` (or `AxisConfig.Ticks` for an explicit list). The axis
   still follows the data, but the ticks keep their spacing and values - the usual choice for a quote feed, whose
   level drifts while the reader wants a stable ladder.
3. **Pin the count**: `AxisConfig.TickCount = 6`. The number of labels stops changing; their positions still move.

4. **Let the axis follow, but stickily**: `AxisConfig.AutoScaleMargin` (a fraction of the domain width) keeps the
   domain the axis is already showing while the rows stay inside it, and refits only when a value leaves the
   margin - short-term shape stays readable, a real move is still followed, and the ticks stop jittering.
   `AxisConfig.NiceDomain` rounds the refitted domain out to a `{1, 2, 5} x 10^n` step, so the axis changes in
   jumps instead of drifting. For "cap the axis and let it grow", `AxisConfig.MaxLimit` (or `MinLimit`) pins one
   end only. Both are exports on `ChartView` (`YAxisAutoScaleMargin`, `YAxisNiceDomain`, `YAxisMaxLimit`,
   `YAxisMinLimit`); the price feed on the rolling-window example uses sticky auto-scaling, and a chart whose
   band is known (a scope, a gauge) pins the domain instead.

The rolling-window example pins the domain (1) for its oscilloscope, and its price feed uses sticky
auto-scaling (4). All four kinds of knob are exports on `ChartView` (`XAxisRange` / `YAxisRange`,
`XAxisTickStep` / `YAxisTickStep`, `XAxisTickCount` / `YAxisTickCount`, and `YAxisAutoScaleMargin` /
`YAxisNiceDomain` / `YAxisMinLimit` / `YAxisMaxLimit`), so a page can be configured without code.

## When the chart repaints

A chart does not repaint on its own: the host calls `Invalidate()` on the view when something it draws has
changed, and several calls in one frame cost one repaint. `ChartView` does that for you from its setters (each
one compares before it invalidates), and the hand-built pages do it when their own animation is running - which
is why a page that is not animating and not being interacted with costs nothing per frame, whatever the table
size. `Repaint()` forces a frame anyway, and `Refresh()` rebuilds the marks.

## Missing values

A row whose value is missing is **skipped**: the point is not drawn, and the line runs straight from the last
point before the gap to the first one after it. Hover, selection and labels skip it too, so they cannot drift
onto a neighbouring point. A series that must show the gap as a gap (a sensor that went offline, a market that
did not trade) has to be split into two series, or drawn with a mark that does not connect its points.

## Reference lines and bands

A reader often needs a line to compare the series against: a target, a threshold, last year's average. That is
a mark of its own, `SectionMark`, and it is annotation rather than data - the levels are mapped through the axis
they name, they do not contribute to any scale (a level far outside the table cannot stretch the axis) and the
mark never shows up in the legend. Zooming and panning move the lines with the data, and a level outside the
visible window is skipped instead of being pinned to the edge.

```csharp compile
new Chart(canvas)
    .Data(monthlySales)
    .Mark(new IntervalMark())
    .Encode(Channel.X, "month")
    .Encode(Channel.Y, "revenue")
    // Targets, plus a band for the acceptable range. Both are read against Channel.Y by default; Channel.Y2
    // draws against the right axis and Channel.X draws vertical lines.
    .Mark(new SectionMark
    {
        Levels = [1500, 2000],
        BandFrom = 1200,
        BandTo = 2600,
        Color = new Color(1f, 0.78f, 0.35f, 0.9f),
        LabelFormat = "{0:N0}",          // leaving it unset keeps the base mark's "{0}"; an explicit null or "" draws no label
        Dashed = true,
    })
    .Render();
```

A `ChartView` node can put the same lines on a chart without any code: `SectionLevels` (an array of values),
`SectionBandFrom` / `SectionBandTo`, `SectionTarget` (`Y`, `Y2` or `X`), `SectionColor`, `SectionDashed` and
`SectionLabelFormat` are exports, so the page is configured in the scene like every other part of it.

## Exporting the chart

`ChartView.SavePng(path)` and `Canvas2DControl.SavePng(path)` write what the view is showing to a PNG, which is
what a tool, a build script or a documentation shot needs. It saves the surface the chart already draws into:
a `Chart` is bound to its canvas when it is constructed, so rendering the same chart into a second surface of
another size would be a different chart. A run without a rendering device has no surface, and the call reports
`false` instead of throwing.

## Performance and large data

A table much larger than the plot can resolve does not have to be drawn point by point. Line and area series
reduce what they draw by default: with `LineMark.Decimate = DecimateMode.Auto`, one pixel column contributes
only its lowest and its highest point, so the path stays proportional to the canvas instead of to the table -
and unlike a stride sampler, the extremes are what survives, so a spike cannot be stepped over.
`DecimatePointsPerPixel` (default 2) is the density it starts at, `DecimateMode.Off` draws every point and
`On` reduces even a series the plot could show in full. Decimation is a drawing decision only: hit testing,
tooltips and `GetRenderDataSnapshot()` keep reading the full rows, so the values a reader is shown stay real.

Axis ticks follow the axis they label. The X axis draws one label per `TickLabelSpacing` pixels of its own
length (the Y axis measures the label line height instead), so a short axis gets fewer labels instead of a
column of overlapping ones; the count stays under `MaxTickCount` and, while the labels fit, at or above
`MinTickCount` (6 by default - a cramped axis can drop to 2). The step refines through whole multiples -
10, then 5, then 2, then 1 - as the
axis grows. `AxisConfig.TickStep` fixes the step, `AxisConfig.TickCount` fixes the number and
`AxisConfig.Ticks` draws exactly the values given; ticks always land on values the data has.

The cost of the table itself is worth knowing before the chart is blamed for it: a `DataRow` is a small
dictionary, about 290 bytes per row retained, so a million rows is on the order of 280 MB before the chart
draws anything. The Layered rendering example page measures both - the draw time, the frame time and the bytes per
row - for 1k to 200k rows, and lets you switch the smoothing and the entry animation on and off to see what
each one costs. Measured on a desktop GPU through that page (60 warm frames): 100 000 points of a smoothed
line cost 72-76 ms per frame with `DecimateMode.Off` and 22 ms with the default `Auto`; 200 000 points cost 189 ms
and 37 ms. Screen points are also cached per series: while the layout, the data and the plot rectangle are
unchanged, a redraw reuses the points it collected last frame (the pointer and the animation progress do not
move a point), so a static chart pays for mapping its table once instead of once per frame.

## Layered rendering

A chart frame is really two layers: the **data layer** - background, title, grid, axes, axis labels, legend and
the marks without their interaction state - and the **overlay** - the marks' hover and selection visuals plus the
crosshair. Only the overlay follows the pointer, so the data layer can be kept in an image and presented again on
the frames that change none of its inputs:

```csharp compile
// Keep the data layer in an image: moving the pointer then costs the overlay alone.
chart.UseLayerCache = true;
chart.Render();
```

The same switch exists on a `ChartView` node as `LayeredRendering` (off by default), so a page can be configured
in the scene like every other part of it.

**What it is worth.** A pointer move stops paying for the data layer: on a large line the hover frame drops from
mapping the table and rebuilding the path to drawing one marker. **What it costs.** One image of the chart
rectangle (`width × height × 4` bytes - about 1.7 MB for 830×520, about 33 MB for 4K), one surface readback per
rebuild, and the precondition that the host clears the surface before every frame (`Canvas2DControl` does, with
`ClearBeforeDraw` on by default).

The layer is rebuilt whenever one of its inputs changes: the data, the layout, the plot rectangle, the theme, the
animation progress, the focused series or the legend configuration. It is **never** rebuilt for hover or
selection - those are what the overlay is for. A mark setting changed directly (`mark.StrokeWidth = 2f`) is not an
input the chart can observe, so call `Chart.InvalidateLayerCache()` after such an edit if the layer should follow
it.

**When it does not engage.** The option only keeps a layer when the canvas backend can read its surface back
(`CanvasCapabilities.SupportsSurfaceCapture`) **and** every mark of the chart paints its interaction state on the
overlay (`Mark.InteractionStateInOverlay`). Among the built-in marks those are `LineMark`, `PointMark`,
`IntervalMark` (stacked included), `BoxMark`, `CandlestickMark`, `HeatmapMark`, `LollipopMark`, `MilestoneMark`,
`TimelineMark`, `WaffleMark`, `FunnelMark`, `GaugeMark`, `TreemapMark` and `SectionMark` (an annotation mark, whose overlay stays empty because it has no state of its own). Seven marks answer false on purpose -
`RangeAreaMark`, `ViolinMark`, `PieMark`, `RadarMark`, `SankeyMark`, `ChordMark`, `SunburstMark` - because their
hover look is the element's own fill at a **translucent** opacity (repainting it on the overlay would composite it
twice and darken it), because the highlight moves a label the cached layer already holds (`PieMark`), or because
the geometry costs a full walk of the table (`RadarMark`, `SunburstMark`). Each of them says so on its own
declaration, and a chart with any of them renders single-pass and says why once. A custom mark
opts in by drawing its hover/selection look in `RenderOverlay` and answering true - `MarkContext.StateInOverlay`
tells the two halves apart, so a mark may well keep painting its state in `Render` while the chart is not caching,
which is what keeps a chart that never turns the option on identical to what it always looked like.

One detail worth knowing: the overlay is drawn **over** the presented layer, so a hovered element whose effective
opacity is below 1 is composited twice (with the option off it is drawn once) and looks denser than in the single
pass (0.3 becomes 0.51, 0.5 becomes 0.75); at full opacity the two pictures are identical. That is exactly why the
marks whose *state itself* is a translucent fill keep their state in the data layer instead (see above).

Streaming pages are not what this is for: a page that appends a row per frame changes the data every frame, so the
layer would be rebuilt - and captured - every frame. It pays off where the data stands still and the pointer moves.
The same data rendered both ways side by side - the milliseconds each kind of frame costs and the bytes the layer
takes - is what the example browser's Layered rendering page measures.


### Performance Guard

Animations auto-disable when data exceeds `AnimationThreshold` (default 2000). The animation state is
passed as an `AnimationContext`, built from the controller:

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

## User Interaction

### HitTest

Detect which data element the user clicked/hovered:

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

**HitResult Properties:**

| Property | Type | Description |
|----------|------|-------------|
| `Hit` | bool | Whether something was hit |
| `Row` | DataRow? | The hit data row |
| `RowIndex` | int | Data row index |
| `ScreenX` / `ScreenY` | float | Screen coordinates |
| `Label` | string? | Label text |
| `SeriesKey` | string? | Series name |
| `MarkType` | string? | Mark type name |
| `ElementColor` | Color | Element color |
| `FocusedSeries` | string? | Focused series key after this interaction (set on a legend click) |
| `TooltipLines` | IReadOnlyList<TooltipLine>? | Rich tooltip content built by the hit mark; wins over `Label` when present |

### Selection & Hover

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

### Event System

```csharp compile
// Click event - ChartClickEventArgs carries the position, the mark type and the mouse button
chart.OnClick += (sender, e) =>
{
    GD.Print($"Clicked: row {e.RowIndex} of {e.MarkType} with {e.Button} at {e.ScreenPosition}");
    // e.Row - the data row (may be null for element-less hits)
    // e.ScreenPosition - click screen position
    // e.MarkType - e.g. "IntervalMark"
    // e.Button - MouseButton.Left also selects the row; the other buttons only report the click
    //            (a host can use them for its own gestures, e.g. drilling back out of a hierarchy)
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

// Focus event - ChartFocusEventArgs reports which series the focus moved from and to
chart.OnFocusChanged += (sender, e) =>
{
    GD.Print($"Focus: {e.PreviousSeriesKey} -> {e.SeriesKey}");
};

// Legend click - ChartLegendClickEventArgs; set Handled to intercept the built-in focus toggle
chart.OnLegendClick += (sender, e) =>
{
    GD.Print($"Legend click: {e.SeriesKey} (focused: {e.CurrentFocusedSeries})");
    e.Handled = true;   // take over the focus logic yourself
};
```

**ChartClickEventArgs Properties:**

| Property | Type | Description |
|----------|------|-------------|
| `Row` | DataRow? | Data row |
| `RowIndex` | int | Row index |
| `MarkType` | string? | Mark type |
| `ScreenPosition` | Vector2 | Screen position |
| `SeriesKey` | string? | Series name |
| `Button` | MouseButton | Button that produced the click; only `Left` changes the selection/focus |

**ChartSelectionEventArgs Properties:**

| Property | Type | Description |
|----------|------|-------------|
| `Row` | DataRow? | Data row |
| `RowIndex` | int | Newly selected row index |
| `PreviousRowIndex` | int | Previously selected row index |
| `SeriesKey` | string? | Series name |

`ChartFocusEventArgs` (`PreviousSeriesKey` / `SeriesKey`) and `ChartLegendClickEventArgs`
(`SeriesKey` / `CurrentFocusedSeries` / `Button` / `Handled`) have only a few fields; the full signatures
are in the [API Reference](api-reference.md#events). Setting `Handled = true` in `OnLegendClick` intercepts
the built-in focus toggle, so you can drive the focus yourself.

### Series Focus

When a series is **focused** (`Chart.FocusSeries(...)` or a legend click), the other series
reduce their opacity, controlled by `ChartTheme.UnfocusedOpacity`. Merely hovering does not focus a
series — `OnHover` only reports the hovered row.

---

## Tooltips

### Basic Usage

```csharp compile-members
private TooltipRenderer _tooltip = new();
```

```csharp compile
// In _Process:
_tooltip.Theme = _theme;
_tooltip.Update((float)delta, chart.HitTest(mousePos));
_tooltip.Draw(canvas, canvasWidth, canvasHeight);
```

### TooltipOptions

```csharp compile
// Options is read-only: the instance is created with the renderer, its members are writable.
_tooltip.Options.BackgroundColor = new Color(0.1f, 0.1f, 0.15f, 0.95f);
_tooltip.Options.TextColor = Colors.White;
_tooltip.Options.BorderColor = new Color(0.3f, 0.3f, 0.4f);
_tooltip.Options.CornerRadius = 6f;
_tooltip.Options.Padding = 8f;
_tooltip.Options.FontSize = 12f;
```

### Custom Content Builders

**Simple text mode:**

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

**Rich text mode:**

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

`TooltipLine` supports fine-grained control over each text segment:

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

**TooltipSpan Properties:**

| Property | Type | Description |
|----------|------|-------------|
| `Text` | string | Text content |
| `Color` | Color? | Text color |
| `Bold` | bool | Bold style |
| `Italic` | bool | Italic style |
| `FontSize` | float? | Font size |
| `Decoration` | TextDecoration | Underline / strikethrough (default `None`) |
| `LetterSpacing` | float | Extra spacing between characters, in px (default 0). **Ignored by the Skia backend** - neither drawing nor measurement applies it |
| `Family` | string? | Font family override; null uses the tooltip font |
| `GodotFont` | Font? | Godot `Font` resource override; wins over `Family` |
| `Icon` | TooltipIcon | Icon type (None/Circle/Square/Diamond/Triangle) |
| `Image` | IImageHandle? | Embedded image |

### Animation

The tooltip has smooth following and fade in/out built in. `SmoothSpeed` and `FadeSpeed` are readable
properties with fixed values (12 and 8) - they are not configurable per renderer; the theme's
`EnableTooltip` is the switch that turns the tooltip off entirely.

---

## Crosshair

Draw reference crosshairs at mouse position:

```csharp compile
// After chart.Render():
if (chart.CurrentPlotArea is { } plot)
{
    ChartInteraction.DrawCrosshair(canvas, mousePos, plot, _theme);
}
```

Configure crosshair style through the theme (color, width, dash length), or disable with `EnableCrosshair = false`.

---

## Real-Time Data Streaming

### AppendData

```csharp compile
// Add single row
chart.AppendData(new DataRow().Set("time", now).Set("value", reading));

// Add multiple rows
chart.AppendData(newBatch);
```

### WindowSize — Rolling Window

Set a rolling window to auto-trim old data:

```csharp compile
chart.WindowSize = 100;  // keep only latest 100 rows

// Combined with append for real-time streaming:
chart.AppendData(newRow);    // oldest rows auto-trimmed
chart.Render();
```

`WindowSize = 0` means unlimited (default). It applies to `Data()` as well: a replaced data set is trimmed to the window too, so "keep the newest N rows" holds whichever way rows arrive.

### Data Transition Animation

> See the note in [Data Transition](#data-transition): the progress value is available, but no
> built-in mark interpolates between the old and the new data yet.

Combine with `AnimationController.StartDataTransition()` for smooth data update transitions:

```csharp compile
chart.Data(newData);
_anim.StartDataTransition(host, duration: 0.4f);
```

---

## Data Transforms

### BinTransform

Bin continuous values into histogram buckets - an `IntervalMark` over the binned field is what makes the
histogram:

```csharp compile
chart.Data(rawData);
chart.Transform(new BinTransform
{
    Field = "value",
    BinCount = 20,        // or use BinWidth = 5.0
});
chart.Mark(new IntervalMark());
chart.Encode(Channel.X, "BinMid");   // BinTransform outputs: BinStart, BinEnd, BinMid, Count
chart.Encode(Channel.Y, "Count");
chart.XAxis(new AxisConfig { Title = "Value" });
chart.YAxis(new AxisConfig { Title = "Frequency" });

// IntervalMark draws bars on category bands, so the numeric BinMid column needs an ordinal axis - and a
// scale installed by hand is never fitted by the chart (only the scales it infers itself are), so the bins
// are fitted here. Left to itself the column would be auto-fitted to a linear scale and no bar would be
// drawn at all.
var bins = new OrdinalScale();
var keys = new List<object>();
foreach (var row in chart.GetRenderDataSnapshot())
    if (row.TryGet<double>("BinMid", out double mid)) keys.Add(mid);
bins.Fit(keys);
chart.Scale(Channel.X, bins);
chart.Render();
```

An ordinal axis keys its categories by the *text* of the value the mark reads, so the labels above are the
raw bin midpoints. To show rounded bins instead, have a transform write the formatted label into a field of
its own and point `Channel.X` at that field - the Custom charts example page does exactly that.

BinTransform output fields:
- `BinStart` — Bin left edge
- `BinEnd` — Bin right edge
- `BinMid` — Bin midpoint
- `Count` — Number of rows in the bin

When neither `BinCount` nor `BinWidth` is specified, Sturges' rule is used automatically.

### Custom Transforms

Implement the `IDataTransform` interface for custom transforms:

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

Usage:

```csharp compile
// Any IDataTransform is registered the same way - the custom one above, or a built-in:
chart.Transform(new BinTransform { Field = "value", BinCount = 4 });
```

Transforms can be chained and execute in the order they are added.

---

## Composite Charts

### Multiple Marks

Layer multiple Mark types in a single Chart:

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

### Dual Y Axis

`ChartView` has no second axis: a dual-axis chart is built with the API. `Channel.Y2` and a mark
whose `YChannel` points at it are the recipe - the second series keeps its own encoding, its own
colour and its own scale, which is exactly what a node export could not express:

```csharp compile
var rows = new List<DataRow>
{
    new DataRow().Set("month", "Jan").Set("revenue", 120).Set("cost", 80),
    new DataRow().Set("month", "Feb").Set("revenue", 180).Set("cost", 95),
};
// bars on the left axis, a line with its own scale and colour on the right one
new Chart(canvas)
    .Data(rows)
    .Mark(new IntervalMark())
    .Mark(new LineMark { YChannel = Channel.Y2 }.Encode(Channel.Color, Colors.Orange))
    .Encode(Channel.X, "month")
    .Encode(Channel.Y, "revenue")
    .Encode(Channel.Y2, "cost")
    .ScaleDomain(Channel.Y2, 0, 100)   // pin the right axis
    .Render();
```

The same two marks, without the colour and scale extras - `Channel.Y2` plus `LineMark.YChannel` is the
whole recipe:

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

### Coordinate System Compatibility

All Marks in a Chart must use compatible coordinate systems:

| Can combine | Cannot combine |
|-------------|---------------|
| IntervalMark + LineMark | IntervalMark + PieMark |
| LineMark + PointMark | LineMark + RadarMark |
| Any multiple Cartesian Marks | Cartesian + Polar |
| Geographic + Geographic | Geographic + Cartesian |

Incompatible Marks are **skipped** and a warning is printed to the Godot console
(`[GodotChart] Incompatible mark combination: ...`) - the coordinate check is internal
(`Chart.ValidateMarkCompatibility()` is private, not an API to call).
The **first added mark** defines the coordinate system of the chart: every later mark with a
different one is the one that gets skipped, so add the primary mark first.

A mark that declares `MarkCoordinate.Geographic` places its data through a coordinate frame (see
[Geographic Coordinates](api-reference.md#geographic-coordinates)) rather than through the scales, so a chart
whose marks are all geographic draws no grid, no axes and no crosshair, and reserves no label column for them.

---

## Series Information Query

```csharp compile
// Get all series with their colors
IReadOnlyList<(string Key, Color Color)> series = chart.GetSeriesInfo();

// Get current render data snapshot
IReadOnlyList<DataRow> snapshot = chart.GetRenderDataSnapshot();

// Current plot area (for custom drawing)
PlotArea? area = chart.CurrentPlotArea;
```

---

## Canvas Abstraction — ICanvas2D

GodotChart decouples from the rendering backend through the `ICanvas2D` interface. The current implementation is `SkiaCanvas2DBackend` (based on SkiaSharp).

### Basic Drawing

```csharp compile
using var paint = canvas.CreatePaint()
    .SetColor(Colors.Red)
    .SetStrokeWidth(2f);

canvas.DrawLine(0, 0, 100, 100, paint);
canvas.DrawRect(10, 10, 50, 30, paint);
canvas.DrawCircle(200, 200, 25, paint);
canvas.DrawText("Hello", 10, 50, FontSettings.Default, paint);
```

### Path Drawing

```csharp compile
using var path = canvas.CreatePath()
    .MoveTo(0, 0)
    .LineTo(100, 50)
    .CubicTo(150, 0, 200, 100, 250, 50)
    .Close();

canvas.Fill(path, fillPaint);
canvas.Stroke(path, strokePaint);
```

### Gradients

```csharp compile
using var paint = canvas.CreatePaint()
    .SetLinearGradient(0, 0, 100, 0, new[]
    {
        new GradientStop(0f, Colors.Blue),
        new GradientStop(1f, Colors.Red),
    });
```

### Transforms & Clipping

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

## Font Settings — FontSettings

```csharp compile
var font = new FontSettings
{
    Size = 16f,
    Bold = true,
    Italic = false,
    Family = "Roboto",
    Align = TextAlign.Center,
    LetterSpacing = 1.5f,  // accepted but ignored by the Skia backend
    LineHeightMultiplier = 1.2f,
    Decoration = TextDecoration.Underline,
    GodotFont = myGodotFont,  // optional Godot Font resource
};

var metrics = canvas.MeasureText("Hello World", font);
// metrics.Width, metrics.Height
```
