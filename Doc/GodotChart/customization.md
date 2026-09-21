**English** | [中文](customization.cn.md)

# Customization & Theming

This guide covers visual customization of GodotChart, including the theme system, scale configuration, axes, legends, and custom renderers.

---

## Theme System — ChartTheme

`ChartTheme` is a Godot `Resource` with all visual properties marked `[Export]`, editable in the Godot Inspector.

### Built-in Themes

```csharp compile
var data = new List<DataRow>
{
    new DataRow().Set("category", "A").Set("value", 10),
    new DataRow().Set("category", "B").Set("value", 20),
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

### Clone & Customize

```csharp compile
var customTheme = ChartTheme.Dark().Clone();
customTheme.Palette = new Color[]
{
    new(0.2f, 0.6f, 1.0f),
    new(1.0f, 0.4f, 0.3f),
    new(0.3f, 0.9f, 0.5f),
};
customTheme.BackgroundColor = new Color(0.05f, 0.05f, 0.08f);
customTheme.CornerRadius = 6f;
```

### Theme Property Groups

ChartTheme has 107 `[Export]` properties in 25 `[ExportGroup]`s. Here are the major ones:

#### Color Palette

| Property | Type | Description |
|----------|------|-------------|
| `Palette` | Color[] | Categorical palette (default 6 colors) |
| `SequentialGradient` | Color[] | Sequential gradient (5 colors) |

#### Chart Frame

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `BackgroundColor` | Color | (0.08, 0.08, 0.12) | Background color |
| `BackgroundCornerRadius` | float | 8 | Background corner radius |
| `GridColor` | Color | (1,1,1,0.08) | Grid line color |
| `GridLineWidth` | float | 1 | Grid line width |
| `AxisColor` | Color | (1,1,1,0.4) | Axis line color |
| `AxisLineWidth` | float | 2 | Axis line width |

#### Typography

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `FontFamily` | string? | null | Font family name |
| `Font` | Font? | null | Godot Font resource |
| `TitleColor` | Color | — | Title color |
| `TitleFontSize` | float | 13 | Title font size |
| `LabelColor` | Color | — | Label color |
| `LabelFontSize` | float | 13 | Label font size |
| `DataLabelColor` | Color | — | Data label color |

#### Layout

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `AxisTitleMargin` | float | 6 | Axis title margin |
| `TitleReservedHeight` | float | 24 | Title reserved height |
| `Y2LabelReservedWidth` | float | 35 | Right axis label reserved width |
| `XAxisLabelOffset` | float | 15 | X axis label offset |
| `YAxisLabelGap` | float | 5 | Y axis label gap |

#### Mark Defaults

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `DefaultMarkColor` | Color | — | Default mark color |
| `CornerRadius` | float | 3 | Mark default: `ChartView` applies it to the mark it builds (a `ConfigureMark` callback wins) |
| `StrokeWidth` | float | 2 | Mark default for line-like marks, applied the same way |

#### Selection & Hover

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `SelectionColor` | Color | — | Selection highlight color |
| `SelectionStrokeWidth` | float | 2 | Selection stroke width |
| `HoverBrighten` | float | 1.2 | Hover brightness boost |
| `UnfocusedOpacity` | float | 0.15 | Unfocused series opacity |
| `HoverScale` | float | 1.05 | Hover scale factor |

#### Tooltip

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `TooltipBackground` | Color | — | Background color |
| `TooltipTextColor` | Color | — | Text color |
| `TooltipBorderColor` | Color | — | Border color |
| `TooltipBorderWidth` | float | 1 | Border width |
| `TooltipCornerRadius` | float | 6 | Corner radius |
| `TooltipFontSize` | float | 12 | Font size |
| `TooltipPadding` | float | 8 | Padding |

#### Crosshair

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `CrosshairColor` | Color | — | Crosshair color |
| `CrosshairStrokeWidth` | float | 1 | Line width |
| `CrosshairDashLength` | float | 4 | Dash length |
| `EnableCrosshair` | bool | true | Enable crosshair |

#### Legend

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `LegendSwatchTextGap` | float | 4 | Swatch-to-text gap |
| `LegendDimmedOpacity` | float | 0.3 | Hidden series opacity |
| `LegendSwatchCornerRadius` | float | 2 | Swatch corner radius |
| `LegendVerticalItemSpacing` | float | 4 | Vertical item spacing |
| `LegendBottomGap` | float | 10 | Bottom gap |

Legend text is measured with the theme font (`ChartTheme.LabelFontSize` / `Family` / `Font`), so item
widths and row heights follow a font-size change instead of overlapping.

#### Polar / Segment

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `SegmentBorderColor` | Color | — | Segment border color |
| `SegmentBorderWidth` | float | 1 | Border width |
| `PieExplodeRatio` | float | 0.03 | Pie explode ratio |

Additional Export Groups exist for the mark-specific styles, each named after the mark it configures:
**Line Mark, Point Mark, Radar Mark, Box Mark, Violin Mark, Gauge Mark, Sankey Mark, Candlestick Mark,
Lollipop Mark, Range Area Mark, Chord Mark, Sunburst Mark, Treemap Mark** (plus **Feature Toggles** and
**Hit Test**). Groups exist only where the theme actually carries a token for that mark: Interval, Heatmap,
Funnel, Waffle, Milestone and Timeline read the shared defaults above.
The tables above list the main properties only; the Inspector shows the full set under these group names.

The example page `ChartThemeDemo` walks the groups cell by cell: the two built-in themes, `Clone`, the frame,
typography, the line widths and tooltip metrics, the feature toggles, a resource edited while the page runs,
chart-level overrides, the **Layout** reservations (the title band and the Y2 column) and one per-mark group
(the violin's, together with the **Hit Test** knobs). The groups without a cell of their own - the other
per-mark ones, and the tooltip offsets and legend spacing inside the tables above - are configured exactly
like the ones that have one: same theme resource, same groups in the Inspector.

---

## Custom Theme Resource (ChartView)

`ChartView` can take a whole `ChartTheme` as a scene asset: create a `.tres`, edit it in the Inspector and
point the node at it - no code, and the editor preview follows every edit.

### In the editor

1. Right-click in the FileSystem panel → **New Resource…** → pick `ChartTheme` → save it as e.g. `my_theme.tres`.
2. Open `my_theme.tres` and edit the colours, line widths, font sizes, corner radii and the palette in the
   Inspector - the properties sit in groups (Color Palette / Chart Frame / Typography / …).
3. Select the `ChartView` in the scene and point its `CustomTheme` slot at that `.tres`; the editor preview
   updates right away.

### Priority: CustomTheme over ThemeKind

- `CustomTheme` is not null → it is the theme of this chart and `ThemeKind` is ignored.
- `CustomTheme` is null (the default) → the built-in palette picked by `ThemeKind` (`Dark` / `Light`) is used.

### Your resource is never rewritten

The render pipeline (`Chart`, every mark, every renderer, the tooltip) only **reads** the theme, and the node
writes no field back into your `.tres` - so one theme resource can back any number of `ChartView` nodes.

Because the resource itself is what the chart reads, **editing `my_theme.tres` in the Inspector updates every
chart that points at it right away** (both the `ChartView` node and a hand-built `Chart` follow the
resource's `changed` signal - the node rebuilds, the chart drops its cached layout - because a reference
comparison alone would not see an in-place edit).

The two array exports are per-instance copies, never shared state: `Palette` and `SequentialGradient` each
start from the static default (`ChartTheme.DefaultPalette` / `DefaultSequentialGradient`), so writing into
one theme's array cannot leak into another theme or into the defaults. Those two statics are **read-only by
copy** as well - every read returns a fresh array, so `ChartTheme.DefaultPalette[0] = Colors.Red;` changes
nothing (use `theme.Palette` for a real change). The flip side of the
`changed` signal is that an **in-place** edit of an array element goes unnoticed: assigning a value (or a
whole array) calls `EmitChanged()`, so `theme.Palette = newColors` and `theme.Palette[0] = Colors.Red` are
not the same thing - the second one needs an explicit `theme.EmitChanged()` (or a re-assignment) to reach
the charts that use the theme.

### The theme is the only place for styling

The node has **no** style exports of its own - no `Font`, `FontFamily` or colour overrides. The font entry
points are `ChartTheme.Font` / `ChartTheme.FontFamily` (the *Typography* group), and colours, line widths and
corner radii work the same way. One place to change, no two settings fighting each other.

> Note: `ChartTheme` carries `[Tool]`. Without it a C# resource class only gets a placeholder script instance
> in the editor - the resource shows none of its exported properties there, and assigning it to `CustomTheme`
> throws an `InvalidCastException`. Keep the attribute on your own theme subclasses.

### From code

```csharp
var view = new ChartView
{
    Kind = ChartKind.Bar,
    CustomTheme = GD.Load<ChartTheme>("res://my_theme.tres"),
};

// Equivalent: the Chart API takes the resource directly.
var chart = new Chart(canvas).Theme(myTheme);
```

---

## Scales

Scales map raw data values to the normalized `[0, 1]` range. Most are auto-inferred, but you can manually specify for precise control.

### LinearScale

```csharp compile
// Auto-fit from data (default behavior)
// Or manually specify range:
chart.Scale(Channel.Y, new LinearScale(0, 100));
```

Properties:
- `Min` / `Max` — Domain range
- `IncludeZero` — Force include zero (default: true)

### OrdinalScale

Auto-infers categorical values from data. Used for text-category X axes.

### LogScale

For data spanning multiple orders of magnitude:

```csharp compile
chart.Scale(Channel.Y, new LogScale(1, 10000));
```

### ColorScale

Maps categorical values to palette colors:

```csharp compile
// Usually auto-inferred from theme palette
chart.Scale(Channel.Color, new ColorScale());
```

Colours come from the **colour channel**: whatever field `ColorField` names (empty falls back to `series`)
gets a categorical scale over the theme's `Palette`, and equal values share a colour - cycling through the
palette when there are more categories than entries. The legend is driven by the same channel.

For **Pie / Donut / Funnel / Waffle** `ChartView` binds the colour channel to the category itself when no
field is given: one row is one slice / stage / cell, so each one takes the next palette colour and the
legend lists the categories. Without a colour channel those kinds keep the mark's single default colour
(`ChartTheme.DefaultMarkColor`), which is why a pie whose rows carry no `series` field looks monochrome.

The structure and flow marks have a palette default of their own, so they never come out monochrome:
**Sunburst** gives every branch a palette colour (with a lone root acting as the frame, so the chart does
not collapse into one colour) and keeps it while deeper rings darken with `DepthShadeStep`; **Treemap**
gives every top-level cell a palette colour and shades a group's children
with `SiblingShadeStep`; **Sankey** colours each flow with its source node's colour; **Chord** colours
each node arc from the palette and each chord with the colour of the node it leaves. An explicit `Color`
channel always wins in all of them.

#### Colours that are colours

`ColorField` holds a **field name**, not a colour: it names a key of the row dictionaries, and each row's
value in that field becomes that element's colour. Two kinds of value count as a colour:

| Value | Example | Result |
|---|---|---|
| a Godot `Color` | `Colors.Teal`, `new Color("#ff8800")` | used as it is |
| a string of `#` + 3, 4, 6 or 8 hexadecimal digits | `"#f00"`, `"#ff8800"`, `"#ff8800cc"` | parsed, then used as it is |

When every value of the colour channel is such a colour, `Chart` uses them as they are - G2's *identity*
colour scale, `IdentityColorScale` - and draws no legend, because the values are colours rather than
categories.

**Anything else stays a category** and takes the categorical palette: a number, a name, and deliberately
also a string that merely *reads* like a colour (`"red"`, `"add"` - only the `#` form is parsed). That way a
category never turns into a colour by accident.

The switch is all-or-nothing per channel: **every** value has to be a colour, otherwise the whole column is
categorical (a mixed column would lose its legend, which is why one stray value must not flip it). Force it
with `ColorMapping` when the data disagrees with the guess - see
[Choosing the mapping](#choosing-the-mapping-scales) below.

```csharp compile
var rows = new List<DataRow>();
rows.Add(new DataRow().Set("category", "Q1").Set("value", 30).Set("color", "#ff8800"));
rows.Add(new DataRow().Set("category", "Q2").Set("value", 45).Set("color", Colors.Teal));

view.ColorField = "color";              // ChartView: the field name is enough, no scale to configure
chart.Encode(Channel.Color, "color");   // Chart API
```

**One colour for the whole chart:** bind a constant instead of a field -
`Encode(Channel.Color, Colors.Red)` (the type-safe overload) or `Encode(Channel.Color, "constant:#ff0000")`.
On a `ChartView` the same is a `ColorField` of `constant:#ff0000`.

**Per element from code:** `mark.StyleOverride = (row, index, style) => style.WithFill(colour)`; on a
`ChartView` the mark is reached through `ConfigureMark`. This stays the way to colour by index or by a
computed rule.

#### Size, opacity and shape

`SizeField` only means something to the point/scatter mark: the value is mapped by a linear scale fitted to
that column's **data extent** (`min`…`max`, zero *not* included) into a radius between
`ChartTheme.PointSizeMin` (3 px) and `PointSizeMin + PointSizeRange` (23 px) - the smallest value draws the
smallest dot, the largest the biggest. Without a size channel every dot uses `PointMark.DefaultRadius` (5 px),
and hover multiplies the radius by `PointHoverRadiusRatio` (1.3).

`OpacityField` is read by the marks that resolve a per-element opacity: bars, points, heatmap cells,
candles, boxes, chords, funnel stages, gauges, lollipop stems, milestones, pie slices, sankey flows,
sunburst rings, timeline bars, treemap cells, violins and waffle cells. The value goes through a linear scale fitted to
`0`…`max` of the column, so the largest value is fully opaque and `0` is invisible (`0`…`255` alpha bytes
work as they are, and so does a `0`…`1` fraction - the result is clamped to `0`…`1`). A constant -
`"constant:0.5"` - is taken literally. Three marks deliberately ignore the channel and use their own
knobs instead: `LineMark` and `RadarMark` apply the global / focus opacity per series, and
`RangeAreaMark` uses `FillOpacity` (its rows' `Opacity` values are ignored; only the `Color` channel
tints its single band). Dimming an unfocused series is a different knob again,
`ChartTheme.UnfocusedOpacity`.

`ShapeField` is categorical, like a colour category column: each distinct value takes the next symbol from
`ShapeScale.DefaultShapes` (circle, square, triangle, diamond, cross, star - cycled). It drives point symbols,
lollipop dots and the legend swatches.

In all three, a row that does not carry the field keeps the mark's default (no error), and a non-finite value
falls back to it too - a NaN never reaches the canvas as a broken radius or opacity.

### Choosing the mapping (scales)

`*Field` properties configure the **binding** (which column feeds a channel); how those values become colours,
sizes or opacities is the **scale**. `Chart` infers the scale (categorical for a colour field, linear for a
size/opacity/value column), and `ColorMapping` is how a scene overrides that guess:

| `ColorMapping` | What the colours become |
|---|---|
| `Auto` (default) | identity when every value is a colour, otherwise the categorical palette |
| `Category` | always the palette, one colour per distinct value plus a legend - even for a column of colours |
| `Identity` | always the value's own colour - how a **mixed** column keeps the colours it carries |
| `Sequential` | a numeric column mapped onto `ChartTheme.SequentialGradient` (heat-like) |
| `Diverging` | a numeric column mapped onto the diverging blue → neutral → red ramp, centred on zero |

```csharp compile
view.ColorField = "delta";
view.ColorMapping = ColorMappingKind.Diverging;   // -20 … +20 around 0
```

Anything the exports do not cover goes through `ConfigureChart`, which receives the built chart right before it
is drawn - the same escape hatch `ConfigureMark` gives for the mark:

```csharp compile
view.ConfigureChart = chart =>
{
    chart.Scale(Channel.Y, new LinearScale(0, 100));   // pin an axis domain
    chart.Mark(new MilestoneMark());                   // add a second mark
};
```


The mappings a scene sets directly are `XAxisRange` / `YAxisRange` (`(0, 0)` fits the data; a category axis
ignores them instead of breaking), `SizeRange` (`(min, max)` radius in px), `OpacityRange` and
`ShapeSymbols`. Dual axes are not part of the node: `Channel.Y2` is an API feature.

##### Where the node stops

The node covers the cases a scene can express; the rest is the `Chart` API (reachable from a node through
`ConfigureChart` / `ConfigureMark`). That line is deliberate - a node carrying every option of the chart
class would be a worse editor experience than the API itself.

| Covered by `ChartView` | Use the `Chart` API instead |
|---|---|
| One kind, its channels and the colour mapping (`ColorMapping`) | Several marks with their own encodings and colours (bars **plus** a line with independent colours) |
| Axis domains (`XAxisRange` / `YAxisRange`), the ranges of size / opacity (`SizeRange` / `OpacityRange`) and the symbol vocabulary (`ShapeSymbols`) | Custom scales (log, one scale per mark), custom ticks |
| - | A second value axis (`Channel.Y2`): every dual-axis chart is built with the API |
| Theme-wide styling through a `ChartTheme` resource | Per-element styling rules (`StyleOverride`), custom marks, shared canvases, custom tooltips |

### SequentialColorScale

Maps continuous values to a gradient:

```csharp compile
// Used by HeatmapMark automatically
// Customize gradient colors:
var scale = new SequentialColorScale();
scale.Gradient = new Color[]
{
    new(0.1f, 0.1f, 0.3f),  // low
    new(0.2f, 0.6f, 1.0f),  // mid
    new(1.0f, 0.9f, 0.3f),  // high
};
```

### DivergingColorScale

Centered scale with different color directions for positive/negative values:

```csharp compile
chart.Scale(Channel.Color, new DivergingColorScale(-1, 1));
```

### RadialScale

For radius mapping in polar charts.

### TimeScale

Maps `DateTime` values to positions. Format auto-adapts based on time span.

### BandScale

For grouped bar layouts with sub-bands:

```csharp compile
var band = new BandScale
{
    SubBandCount = 3,       // 3 series per group
    Padding = 0.2f,         // outer padding
    InnerPadding = 0.1f,    // padding between sub-bands
};
```

The built-in grouped bars do not go through it: `IntervalMark.GroupedBars` (and `ChartView.GroupedBars`)
splits a category into sub-bands itself. `BandScale` and `RadialScale` currently only serve **custom
marks** - no built-in mark consumes them.

### ShapeScale

Maps categories to symbols for the shape channel; the chart infers one automatically when the channel is
encoded, so a scale is only needed to choose the symbols or their order:

```csharp compile
chart.Scale(Channel.Shape, new ShapeScale { Shapes = new[] { ShapeKind.Cross, ShapeKind.Star } });
```

`ShapeKind` offers `Circle`, `Square`, `Triangle`, `Diamond`, `Cross` and `Star`; the default vocabulary is
used in that order and cycles when there are more categories. Points, lollipop dots and the legend swatches
draw the symbol (`ShapeGeometry.Build`).

---

## Axis Configuration — AxisConfig

```csharp compile
new Chart(canvas)
    .XAxis(new AxisConfig
    {
        Title = "Month",
        Description = "Monthly sales data for 2024",  // tooltip hover
        Unit = "USD",
    })
    .YAxis(new AxisConfig
    {
        Title = "Revenue",
        Unit = "USD",
        Description = "Revenue in US dollars",
    })
    .Y2Axis(new AxisConfig { Title = "Cost" })  // secondary Y axis
    .Render();
```

| Property | Type | Description |
|----------|------|-------------|
| `Title` | string? | Axis title |
| `Description` | string? | Detailed description on hover |
| `Unit` | string? | Unit label (e.g., "USD", "ms") |

---

## Legend Configuration — LegendConfig

```csharp compile
chart.Legend(new LegendConfig
{
    Position = LegendPosition.Top,   // Top / Bottom / Left / Right / None
    ItemSpacing = 16f,               // spacing between items
    SwatchSize = 10f,                // color swatch size
    Padding = 6f,                    // legend area padding
});
```

Set `Position` to `LegendPosition.None` to hide the legend.

---

## Layout & Sizing

```csharp compile
// Content padding (within chart frame)
chart.PaddingLeft = 60f;
chart.PaddingRight = 20f;
chart.PaddingTop = 40f;
chart.PaddingBottom = 40f;

// Overall chart dimensions
chart.Width = 800f;
chart.Height = 600f;

// Position offset
chart.OffsetX = 10f;
chart.OffsetY = 10f;

// Title
chart.Title = "My Chart";
```

---

## Custom Renderers

Chart provides multiple renderer slots to replace default drawing behavior with custom functions.
Grid, axis, axis-label and crosshair slots are not called for a polar-only chart (no Cartesian
mark):

```csharp compile
// Custom background
chart.BackgroundRenderer = ctx =>
{
    // ctx provides: Canvas, Plot, Theme, etc.
    using var bgPaint = ctx.Canvas.CreatePaint();
    bgPaint.SetColor(Colors.White);
    ctx.Canvas.DrawRect(0, 0, ctx.Width, ctx.Height, bgPaint);
};

// Custom title
chart.TitleRenderer = ctx => { /* ... */ };

// Custom grid
chart.GridRenderer = ctx => { /* ... */ };

// Custom axis lines
chart.AxisRenderer = ctx => { /* ... */ };

// Custom axis labels
chart.AxisLabelRenderer = ctx => { /* ... */ };

// Custom legend
chart.LegendRenderer = ctx => { /* ... */ };

// Custom crosshair
chart.CrosshairRenderer = ctx => { /* ... */ };
```

The renderer delegate type is `ChartRenderer`, receiving a context parameter with all necessary drawing information.

---

## Batch Mark Configuration

Use `ApplyToAllMarks` to uniformly configure all added Marks:

```csharp compile
var data = new List<DataRow>
{
    new DataRow().Set("category", "A").Set("value", 10),
    new DataRow().Set("category", "B").Set("value", 20),
};
new Chart(canvas)
    .Data(data)
    .Mark(new IntervalMark())
    .Mark(new LineMark())
    .Encode(Channel.X, "category")             // the marks need the fields the chart draws from
    .Encode(Channel.Y, "value")
    .ApplyToAllMarks(m => m.ShowLabel = true)  // apply to both marks
    .Render();
```

---

## Direct Color Overrides

Override theme colors directly on the Chart:

```csharp compile
chart.BackgroundColor = new Color(0.1f, 0.1f, 0.15f);
chart.GridColor = new Color(1f, 1f, 1f, 0.05f);
chart.AxisColor = new Color(1f, 1f, 1f, 0.3f);
```

---

## Series Visibility Control

Dynamically show/hide series at runtime:

```csharp compile
chart.HideSeries("ProductA");              // Hide specific series
chart.ShowSeries("ProductA");              // Show it back
chart.ToggleSeriesVisibility("ProductA");  // Toggle
chart.ShowAllSeries();                     // Show all

bool hidden = chart.IsSeriesHidden("ProductA");
```

Hidden series are excluded from rendering and hit testing.
