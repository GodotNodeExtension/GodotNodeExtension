# Customization & Theming

This guide covers visual customization of GodotChart, including the theme system, scale configuration, axes, legends, and custom renderers.

---

## Theme System — ChartTheme

`ChartTheme` is a Godot `Resource` with all visual properties marked `[Export]`, editable in the Godot Inspector.

### Built-in Themes

```csharp
// Dark theme (default)
var theme = ChartTheme.Dark();

// Light theme
var theme = ChartTheme.Light();

new Chart(canvas)
    .Theme(theme)
    .Data(data)
    .Mark(new IntervalMark())
    .Encode(Channel.X, "x")
    .Encode(Channel.Y, "y")
    .Render();
```

### Clone & Customize

```csharp
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

ChartTheme includes 15+ Export Groups. Here are the major ones:

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
| `CornerRadius` | float | 3 | Default corner radius |
| `StrokeWidth` | float | 2 | Default stroke width |

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

#### Polar / Segment

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `SegmentBorderColor` | Color | — | Segment border color |
| `SegmentBorderWidth` | float | 1 | Border width |
| `ArcGap` | float | 0.02 | Arc gap (radians) |
| `RingGap` | float | 2 | Sunburst ring gap |
| `PieExplodeRatio` | float | 0.03 | Pie explode ratio |

Additional Export Groups exist for **Line/Point, Radar, Box, Violin**, and other Mark-specific styles.

---

## Scales

Scales map raw data values to the normalized `[0, 1]` range. Most are auto-inferred, but you can manually specify for precise control.

### LinearScale

```csharp
// Auto-fit from data (default behavior)
// Or manually specify range:
.Scale(Channel.Y, new LinearScale(0, 100))
```

Properties:
- `Min` / `Max` — Domain range
- `IncludeZero` — Force include zero (default: true)

### OrdinalScale

Auto-infers categorical values from data. Used for text-category X axes.

### LogScale

For data spanning multiple orders of magnitude:

```csharp
.Scale(Channel.Y, new LogScale(1, 10000))
```

### ColorScale

Maps categorical values to palette colors:

```csharp
// Usually auto-inferred from theme palette
.Scale(Channel.Color, new ColorScale())
```

### SequentialColorScale

Maps continuous values to a gradient:

```csharp
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

```csharp
.Scale(Channel.Color, new DivergingColorScale(-1, 1))
```

### RadialScale

For radius mapping in polar charts.

### TimeScale

Maps `DateTime` values to positions. Format auto-adapts based on time span.

### BandScale

For grouped bar layouts with sub-bands:

```csharp
var band = new BandScale();
band.SubBandCount = 3;       // 3 series per group
band.Padding = 0.2f;         // outer padding
band.InnerPadding = 0.1f;    // padding between sub-bands
```

---

## Axis Configuration — AxisConfig

```csharp
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
| `TooltipBuilder` | Func? | Custom axis tooltip content |

---

## Legend Configuration — LegendConfig

```csharp
.Legend(new LegendConfig
{
    Position = LegendPosition.Top,   // Top / Bottom / Left / Right / None
    ItemSpacing = 16f,               // spacing between items
    SwatchSize = 10f,                // color swatch size
    Padding = 6f,                    // legend area padding
})
```

Set `Position` to `LegendPosition.None` to hide the legend.

---

## Layout & Sizing

```csharp
var chart = new Chart(canvas);

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

Chart provides multiple renderer slots to replace default drawing behavior with custom functions:

```csharp
var chart = new Chart(canvas);

// Custom background
chart.BackgroundRenderer = ctx =>
{
    // ctx provides: Canvas, PlotArea, Theme, etc.
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

```csharp
new Chart(canvas)
    .Data(data)
    .Mark(new IntervalMark())
    .Mark(new LineMark())
    .ApplyToAllMarks(m => m.ShowLabel = true)  // apply to both marks
    .Render();
```

---

## Direct Color Overrides

Override theme colors directly on the Chart:

```csharp
chart.BackgroundColor = new Color(0.1f, 0.1f, 0.15f);
chart.GridColor = new Color(1f, 1f, 1f, 0.05f);
chart.AxisColor = new Color(1f, 1f, 1f, 0.3f);
```

---

## Series Visibility Control

Dynamically show/hide series at runtime:

```csharp
chart.HideSeries("ProductA");              // Hide specific series
chart.ShowSeries("ProductA");              // Show it back
chart.ToggleSeriesVisibility("ProductA");  // Toggle
chart.ShowAllSeries();                     // Show all

bool hidden = chart.IsSeriesHidden("ProductA");
```

Hidden series are excluded from rendering and hit testing.
