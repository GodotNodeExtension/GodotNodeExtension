# Advanced Features

This guide covers GodotChart's animation system, user interaction, tooltips, real-time data streaming, and data transforms.

---

## Animation System

GodotChart provides a complete animation pipeline: entry animations, data transitions, hover effects, and exit animations.

### AnimationController

`AnimationController` manages all animation state:

```csharp
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

```csharp
// Start entry animation (call once)
_anim.StartEntry(this, seriesCount: 3, totalElementCount: 100);

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

Use `SeriesStagger` for sequential series appearance:

```csharp
_anim.SeriesStagger = 0.15f;  // 150ms per series delay
_anim.StartEntry(this, seriesCount: 3);

// Each series gets independent progress:
// _anim.SeriesProgress[0], [1], [2] stagger over time
```

### Data Transition

Trigger smooth transitions when data updates:

```csharp
_anim.StartDataTransition(this, duration: 0.4f);

// In render loop:
var ctx = new AnimationContext
{
    DataTransitionProgress = _anim.DataTransitionProgress,
};
chart.Animate(ctx).Render();
```

### Hover Animation

Scale effect responding to mouse hover:

```csharp
// On hover event:
_anim.AnimateHover(this, targetScale: 1.05f);

var ctx = new AnimationContext
{
    HoverScale = _anim.HoverScale,
};
```

### Exit Animation

```csharp
_anim.StartExit(this, onComplete: () =>
{
    GD.Print("Exit animation complete!");
    // Clean up or switch chart
});
```

### AnimationContext — Simplified API

You can also pass a simple `float` for entry-only animation:

```csharp
chart.Animate(0.5f).Render();  // 50% entry progress
```

Or use `AnimationContext.Default` to skip all animations.

### Easing Functions

```csharp
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

float eased = Ease.Apply(t, EaseType.EaseOutCubic);
```

### Performance Guard

Animations auto-disable when data exceeds `AnimationThreshold` (default 2000):

```csharp
if (_anim.ShouldAnimate(elementCount))
{
    chart.Animate(ctx);
}
chart.Render();
```

---

## User Interaction

### HitTest

Detect which data element the user clicked/hovered:

```csharp
var hit = chart.HitTest(mousePosition);
if (hit?.Hit == true)
{
    DataRow row = hit.Row;
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

### Selection & Hover

```csharp
// Select a data row
chart.Select(hit.RowIndex);

// Update interaction state (hover glow, crosshair position)
chart.Interaction(mousePosition.ToVector2());

// Query current state
int selectedRow = chart.CurrentSelectedRowIndex;
int hoveredRow = chart.CurrentHoveredRowIndex;
string? focusedSeries = chart.CurrentFocusedSeries;
```

### Event System

```csharp
// Click event
chart.OnSelectionChanged += (sender, e) =>
{
    GD.Print($"Selected: row {e.RowIndex}, series={e.SeriesKey}");
    // e.Row — the data row
    // e.ScreenPosition — click screen position
    // e.MarkType — e.g. "IntervalMark"
};

// Hover event
chart.OnHover += (sender, e) =>
{
    if (e.RowIndex >= 0)
        GD.Print($"Hovering: row {e.RowIndex}");
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

### Series Focus

When hovering over a series, other series automatically reduce opacity, controlled by `ChartTheme.UnfocusedOpacity`.

---

## Tooltips

### Basic Usage

```csharp
private TooltipRenderer _tooltip = new();

// In _Process:
_tooltip.Theme = _theme;
_tooltip.Update((float)delta, chart.HitTest(mousePos));
_tooltip.Draw(canvas, canvasWidth, canvasHeight);
```

### TooltipOptions

```csharp
_tooltip.Options = new TooltipOptions
{
    BackgroundColor = new Color(0.1f, 0.1f, 0.15f, 0.95f),
    TextColor = Colors.White,
    BorderColor = new Color(0.3f, 0.3f, 0.4f),
    CornerRadius = 6f,
    Padding = 8f,
    FontSize = 12f,
};
```

### Custom Content Builders

**Simple text mode:**

```csharp
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

```csharp
_tooltip.Options.RichContentBuilder = ctx =>
{
    return new List<TooltipLine>
    {
        TooltipLine.WithIcon(TooltipIcon.Circle, ctx.Color, ctx.Label ?? ""),
        TooltipLine.Plain($"Value: {ctx.Row.Get<int>("value")}"),
    };
};
```

### TooltipLine & TooltipSpan

`TooltipLine` supports fine-grained control over each text segment:

```csharp
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
| `Icon` | TooltipIcon | Icon type (None/Circle/Square/Diamond/Triangle) |
| `Image` | IImageHandle? | Embedded image |

### Animation

Tooltip has built-in smooth following and fade effects:

```csharp
_tooltip.SmoothSpeed = 12f;   // position smoothing
_tooltip.FadeSpeed = 8f;      // fade in/out speed
```

---

## Crosshair

Draw reference crosshairs at mouse position:

```csharp
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

```csharp
// Add single row
chart.AppendData(new DataRow().Set("time", now).Set("value", reading));

// Add multiple rows
chart.AppendData(newBatch);
```

### WindowSize — Rolling Window

Set a rolling window to auto-trim old data:

```csharp
chart.WindowSize = 100;  // keep only latest 100 rows

// Combined with append for real-time streaming:
chart.AppendData(newRow);    // oldest rows auto-trimmed
chart.Render();
```

`WindowSize = 0` means unlimited (default).

### Data Transition Animation

Combine with `AnimationController.StartDataTransition()` for smooth data update transitions:

```csharp
chart.Data(newData);
_anim.StartDataTransition(this, duration: 0.4f);
```

---

## Data Transforms

### BinTransform

Bin continuous values into histogram buckets:

```csharp
new Chart(canvas)
    .Data(rawData)
    .Transform(new BinTransform
    {
        Field = "value",
        BinCount = 20,        // or use BinWidth = 5.0
    })
    .Mark(new IntervalMark())
    .Encode(Channel.X, "BinMid")
    .Encode(Channel.Y, "Count")
    .Render();
```

BinTransform output fields:
- `BinStart` — Bin left edge
- `BinEnd` — Bin right edge
- `BinMid` — Bin midpoint
- `Count` — Data count

When neither `BinCount` nor `BinWidth` is specified, Sturges' rule is used automatically.

### Custom Transforms

Implement the `IDataTransform` interface for custom transforms:

```csharp
public class MovingAverageTransform : IDataTransform
{
    public string Field { get; set; } = "value";
    public int Window { get; set; } = 5;

    public List<DataRow> Apply(List<DataRow> data)
    {
        // Your transform logic here
        // Return modified data rows
    }
}

// Usage:
chart.Transform(new MovingAverageTransform { Field = "price", Window = 7 });
```

Transforms can be chained and execute in the order they are added.

---

## Composite Charts

### Multiple Marks

Layer multiple Mark types in a single Chart:

```csharp
new Chart(canvas)
    .Data(data)
    .Mark(new IntervalMark())  // bars
    .Mark(new LineMark())      // line overlay
    .Encode(Channel.X, "month")
    .Encode(Channel.Y, "revenue")
    .Render();
```

### Dual Y Axis

Use `Channel.Y2` and `LineMark.YChannel` for dual axes:

```csharp
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

Incompatible Marks are automatically skipped by `ValidateMarkCompatibility()` with a warning.

---

## Series Information Query

```csharp
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

```csharp
using var paint = canvas.CreatePaint()
    .SetColor(Colors.Red)
    .SetStrokeWidth(2f);

canvas.DrawLine(0, 0, 100, 100, paint);
canvas.DrawRect(10, 10, 50, 30, paint);
canvas.DrawCircle(200, 200, 25, paint);
canvas.DrawText("Hello", 10, 50, FontSettings.Default, paint);
```

### Path Drawing

```csharp
using var path = canvas.CreatePath()
    .MoveTo(0, 0)
    .LineTo(100, 50)
    .CubicTo(150, 0, 200, 100, 250, 50)
    .Close();

canvas.Fill(path, fillPaint);
canvas.Stroke(path, strokePaint);
```

### Gradients

```csharp
using var paint = canvas.CreatePaint()
    .SetLinearGradient(0, 0, 100, 0, new[]
    {
        new GradientStop { Offset = 0, Color = Colors.Blue },
        new GradientStop { Offset = 1, Color = Colors.Red },
    });
```

### Transforms & Clipping

```csharp
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

```csharp
var font = new FontSettings
{
    Size = 16f,
    Bold = true,
    Italic = false,
    Family = "Roboto",
    Align = TextAlign.Center,
    LetterSpacing = 1.5f,
    LineHeightMultiplier = 1.2f,
    Decoration = TextDecoration.Underline,
    GodotFont = myGodotFont,  // optional Godot Font resource
};

var metrics = canvas.MeasureText("Hello World", font);
// metrics.Width, metrics.Height
```
