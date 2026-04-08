# API Reference

Complete listing of all public classes, interfaces, enums, and their members in GodotChart.

---

## Chart (Main Entry Point)

```csharp
public partial class Chart : IDisposable
```

### Construction & Disposal

| Method | Returns | Description |
|--------|---------|-------------|
| `Chart(ICanvas2D canvas)` | — | Constructor |
| `Dispose()` | void | Release resources |

### Fluent Builder Methods

| Method | Returns | Description |
|--------|---------|-------------|
| `Theme(ChartTheme theme)` | Chart | Set theme |
| `Mark(Mark mark)` | Chart | Add visual mark |
| `Mark<T>() where T : Mark, new()` | Chart | Add mark by type |
| `ApplyToAllMarks(Action<Mark> action)` | Chart | Batch configure marks |
| `Data(IEnumerable<DataRow> rows)` | Chart | Set data |
| `AppendData(DataRow row)` | Chart | Append single data row |
| `AppendData(IEnumerable<DataRow> rows)` | Chart | Append multiple rows |
| `Encode(Channel ch, string field)` | Chart | Bind field encoding |
| `Encode(Channel ch, object constant)` | Chart | Bind constant encoding |
| `Scale(Channel ch, IScale scale)` | Chart | Set scale |
| `Transform(IDataTransform transform)` | Chart | Add data transform |
| `Animate(float progress)` | Chart | Entry animation progress |
| `Animate(AnimationContext? ctx)` | Chart | Full animation context |
| `XAxis(AxisConfig config)` | Chart | X axis configuration |
| `YAxis(AxisConfig config)` | Chart | Y axis configuration |
| `Y2Axis(AxisConfig config)` | Chart | Secondary Y axis |
| `Legend(LegendConfig config)` | Chart | Legend configuration |
| `Render()` | Chart | Execute rendering |

### Interaction Methods

| Method | Returns | Description |
|--------|---------|-------------|
| `HitTest(Vector2 pos)` | HitResult? | Hit test |
| `Select(int rowIndex)` | Chart | Select data row |
| `Interaction(Vector2 mousePos)` | Chart | Update interaction state |

### Series Visibility

| Method | Returns | Description |
|--------|---------|-------------|
| `HideSeries(string key)` | Chart | Hide series |
| `ShowSeries(string key)` | Chart | Show series |
| `ToggleSeriesVisibility(string key)` | Chart | Toggle visibility |
| `ShowAllSeries()` | Chart | Show all series |
| `IsSeriesHidden(string key)` | bool | Query hidden state |

### State Queries

| Property / Method | Type | Description |
|-------------------|------|-------------|
| `GetSeriesInfo()` | IReadOnlyList<(string, Color)> | All series with colors |
| `GetRenderDataSnapshot()` | IReadOnlyList\<DataRow\> | Render data snapshot |
| `CurrentFocusedSeries` | string? | Currently focused series |
| `CurrentSelectedRowIndex` | int | Selected row index |
| `CurrentHoveredRowIndex` | int | Hovered row index |
| `CurrentPlotArea` | PlotArea? | Current plot area |

### Layout Properties

| Property | Type | Description |
|----------|------|-------------|
| `Width` | float | Width |
| `Height` | float | Height |
| `OffsetX` | float | X offset |
| `OffsetY` | float | Y offset |
| `PaddingLeft/Right/Top/Bottom` | float | Padding |
| `Title` | string? | Chart title |
| `WindowSize` | int | Rolling window size (0 = unlimited) |

### Renderer Slots

| Property | Type | Description |
|----------|------|-------------|
| `BackgroundRenderer` | ChartRenderer? | Background renderer |
| `TitleRenderer` | ChartRenderer? | Title renderer |
| `GridRenderer` | ChartRenderer? | Grid renderer |
| `AxisRenderer` | ChartRenderer? | Axis renderer |
| `AxisLabelRenderer` | ChartRenderer? | Axis label renderer |
| `LegendRenderer` | ChartRenderer? | Legend renderer |
| `CrosshairRenderer` | ChartRenderer? | Crosshair renderer |

### Color Overrides

| Property | Type | Description |
|----------|------|-------------|
| `BackgroundColor` | Color | Background color |
| `GridColor` | Color | Grid color |
| `AxisColor` | Color | Axis color |

### Events

| Event | Args Type | Description |
|-------|-----------|-------------|
| `OnSelectionChanged` | ChartClickEventArgs | Selection changed |
| `OnHover` | ChartClickEventArgs | Hover changed |

---

## DataRow

```csharp
public class DataRow
```

| Method | Returns | Description |
|--------|---------|-------------|
| `DataRow()` | — | Default constructor |
| `DataRow(int fieldCapacity)` | — | Constructor with capacity |
| `Set(string field, object value)` | DataRow | Set field (chainable) |
| `Get<T>(string field)` | T | Type-safe read |
| `Get(string field)` | object | Read as object |
| `TryGet<T>(string field, out T result)` | bool | Safe try-read |
| `Has(string field)` | bool | Check field existence |

---

## Channel (Enum)

```csharp
public enum Channel
{
    X,        // Horizontal position
    Y,        // Vertical position (left axis)
    Y2,       // Vertical position (right axis)
    Color,    // Color
    Size,     // Size
    Opacity,  // Opacity
    Shape,    // Shape
    Label,    // Label
}
```

---

## MarkCoordinate (Enum)

```csharp
public enum MarkCoordinate
{
    Cartesian,      // X/Y grid
    Polar,          // Radial
    Hierarchical,   // Tree layout
    Flow,           // Network flow
}
```

---

## Mark Base Class

```csharp
public abstract class Mark
```

| Property | Type | Description |
|----------|------|-------------|
| `ShowLabel` | bool | Show data labels |
| `Coordinate` | MarkCoordinate | Coordinate system (readonly) |

---

## All Mark Subclasses

### IntervalMark

| Property | Type | Default |
|----------|------|---------|
| `BarPadding` | float | 0.2 |
| `CornerRadius` | float | 3 |
| `ShowLabel` | bool | false |
| `ShowValue` | bool | false |
| `Stack` | StackMode | None |
| `Orientation` | BarOrientation | Vertical |

### LineMark

| Property | Type | Default |
|----------|------|---------|
| `StrokeWidth` | float | 2 |
| `Smooth` | bool | true |
| `ShowArea` | bool | false |
| `AreaOpacity` | float | 0.15 |
| `Stack` | StackMode | None |
| `Step` | StepMode | None |
| `YChannel` | Channel | Y |

### PointMark

| Property | Type | Default |
|----------|------|---------|
| `DefaultRadius` | float | 5 |

### PieMark

| Property | Type | Default |
|----------|------|---------|
| `InnerRadius` | float | 0 |
| `StartAngle` | float | -π/2 |
| `ShowLabel` | bool | true |
| `LabelDistance` | float | 1.15 |
| `ExplodeRatio` | float | 0.03 |
| `RadiusFactor` | float | 0.85 |
| `CenterText` | string? | null |
| `CenterFontSize` | float | 18 |
| `CenterSubFontSize` | float | 12 |
| `CenterContentBuilder` | Func? | null |

### RadarMark

| Property | Type | Default |
|----------|------|---------|
| `FillOpacity` | float | 0.2 |
| `StrokeWidth` | float | 2.5 |
| `PointRadius` | float | 4 |
| `GridRings` | int | — |
| `ShowAxisLabels` | bool | — |

### CandlestickMark

| Property | Type | Default |
|----------|------|---------|
| `OpenField` | string | "open" |
| `HighField` | string | "high" |
| `LowField` | string | "low" |
| `CloseField` | string | "close" |
| `BodyWidthRatio` | float | 0.55 |
| `WickWidth` | float | 1.5 |
| `CornerRadius` | float | 1 |
| `BullishColor` | Color | — |
| `BearishColor` | Color | — |

### BoxMark

| Property | Type | Default |
|----------|------|---------|
| `MinField` | string | "min" |
| `Q1Field` | string | "q1" |
| `MedianField` | string | "median" |
| `Q3Field` | string | "q3" |
| `MaxField` | string | "max" |
| `BoxWidthRatio` | float | 0.5 |
| `CornerRadius` | float | 2 |

### HeatmapMark

| Property | Type | Default |
|----------|------|---------|
| `CellGap` | float | 2 |
| `CornerRadius` | float | 3 |
| `ShowLabel` | bool | — |

### GaugeMark

| Property | Type | Default |
|----------|------|---------|
| `ArcWidth` | float | 0.14 |
| `StartAngleDeg` | float | — |
| `EndAngleDeg` | float | — |
| `ShowCenterLabel` | bool | — |
| `ValueColor` | Color | — |
| `TrackColor` | Color | — |

### FunnelMark

| Property | Type | Default |
|----------|------|---------|
| `StageGap` | float | — |
| `MinWidthRatio` | float | — |
| `CornerRadius` | float | — |
| `ShowLabel` | bool | — |

### ViolinMark

| Property | Type | Default |
|----------|------|---------|
| `BinCount` | int | 15 |
| `WidthRatio` | float | — |
| `FillOpacity` | float | — |
| `ShowMedian` | bool | true |
| `ShowBox` | bool | true |

### TreemapMark

| Property | Type | Default |
|----------|------|---------|
| `CellGap` | float | 3 |
| `CornerRadius` | float | 4 |
| `ShowLabel` | bool | — |

### SunburstMark

| Property | Type | Default |
|----------|------|---------|
| `ParentField` | string | — |
| `RadiusFactor` | float | — |
| `InnerRadiusRatio` | float | — |
| `RingGap` | float | — |
| `ShowLabel` | bool | — |

### SankeyMark

| Property | Type | Default |
|----------|------|---------|
| `SourceField` | string | — |
| `TargetField` | string | — |
| `NodeWidth` | float | 14 |
| `ColumnGap` | float | — |
| `NodeGap` | float | — |
| `ShowLabel` | bool | — |

### ChordMark

| Property | Type | Default |
|----------|------|---------|
| `SourceField` | string | — |
| `TargetField` | string | — |
| `ArcWidthRatio` | float | — |
| `ArcGap` | float | — |
| `ShowLabel` | bool | — |

### RangeAreaMark

| Property | Type | Default |
|----------|------|---------|
| `LowerField` | string | — |
| `FillOpacity` | float | 0.3 |
| `ShowBorderLines` | bool | true |
| `StrokeWidth` | float | 2 |
| `Smooth` | bool | true |

### TimelineMark

| Property | Type | Default |
|----------|------|---------|
| `StartField` | string | — |
| `EndField` | string | — |
| `BarHeightRatio` | float | 0.6 |
| `CornerRadius` | float | 4 |
| `ShowLabel` | bool | — |

### LollipopMark

| Property | Type | Default |
|----------|------|---------|
| `DotRadius` | float | 7 |
| `StemWidth` | float | 2.5 |
| `Orientation` | BarOrientation | Vertical |

### WaffleMark

| Property | Type | Default |
|----------|------|---------|
| `TotalCells` | int | 100 |
| `Columns` | int | 10 |
| `CellGap` | float | 3 |
| `CellRadius` | float | 3 |

---

## Scale Interface & Implementations

### IScale

```csharp
public interface IScale
{
    void Fit(IEnumerable<object> domain);
    double Map(object value);
    string Format(object value);
}
```

### LinearScale

```csharp
public class LinearScale : IScale
{
    public LinearScale();
    public LinearScale(double min, double max);
    public double Min { get; }
    public double Max { get; }
    public bool IncludeZero { get; set; }  // default: true
}
```

### OrdinalScale

```csharp
public class OrdinalScale : IScale
{
    public IReadOnlyList<string> Domain { get; }
}
```

### LogScale

```csharp
public class LogScale : IScale
{
    public LogScale(double min, double max);
    public double Min { get; }
    public double Max { get; }
}
```

### ColorScale

```csharp
public class ColorScale : IScale
{
    public Color MapColor(object value);
}
```

### SequentialColorScale

```csharp
public class SequentialColorScale : IScale
{
    public double Min { get; }
    public double Max { get; }
    public Color[] Gradient { get; set; }
    public Color MapColor(object value);
}
```

### DivergingColorScale

```csharp
public class DivergingColorScale : IScale
{
    public DivergingColorScale(double min, double max);
}
```

### BandScale

```csharp
public class BandScale : IScale
{
    public IReadOnlyList<string> Domain { get; }
    public int SubBandCount { get; set; }       // default: 1
    public float Padding { get; set; }          // default: 0.2
    public float InnerPadding { get; set; }     // default: 0.1
    public double BandWidth { get; }
    public double SubBandWidth { get; }
    public double MapSubBand(object value, int subIndex);
}
```

### RadialScale

```csharp
public class RadialScale : IScale;
```

### TimeScale

```csharp
public class TimeScale : IScale
{
    public DateTime Min { get; }
    public DateTime Max { get; }
}
```

---

## AxisConfig

```csharp
public class AxisConfig
{
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? Unit { get; set; }
    public Func<IReadOnlyList<TooltipLine>>? TooltipBuilder { get; set; }
}
```

---

## LegendConfig

```csharp
public class LegendConfig
{
    public LegendPosition Position { get; set; }  // default: Top
    public float ItemSpacing { get; set; }        // default: 16
    public float SwatchSize { get; set; }         // default: 10
    public float Padding { get; set; }            // default: 6
}

public enum LegendPosition { Top, Bottom, Left, Right, None }
```

---

## StackMode (Enum)

```csharp
public enum StackMode
{
    None,       // No stacking
    Stack,      // Additive stacking
    Normalize,  // Percentage normalization
}
```

---

## StepMode (Enum)

```csharp
public enum StepMode
{
    None,    // Continuous line
    After,   // Step after point
    Before,  // Step before point
    Center,  // Center step
}
```

---

## BarOrientation (Enum)

```csharp
public enum BarOrientation { Vertical, Horizontal }
```

---

## Data Transforms

### IDataTransform

```csharp
public interface IDataTransform
{
    List<DataRow> Apply(List<DataRow> data);
}
```

### BinTransform

```csharp
public class BinTransform : IDataTransform
{
    public string Field { get; set; }    // default: "value"
    public int? BinCount { get; set; }   // Sturges' rule if null
    public double? BinWidth { get; set; } // priority over BinCount
}
```

Output fields: `BinStart`, `BinEnd`, `BinMid`, `Count`

---

## Encoding

### IEncodeValue

```csharp
public interface IEncodeValue;
```

### FieldEncode

```csharp
public class FieldEncode : IEncodeValue
{
    public string FieldName { get; }
}
```

### ConstantEncode

```csharp
public class ConstantEncode : IEncodeValue
{
    public object Value { get; }
}
```

---

## Animation

### AnimationController

```csharp
public class AnimationController
{
    // State
    public float EntryProgress { get; }         // [0,1]
    public float[] SeriesProgress { get; }
    public float GlobalOpacity { get; }         // [0,1]
    public float HoverScale { get; }
    public float DataTransitionProgress { get; }// [0,1]
    public float ExitProgress { get; }          // [0,1]
    public bool IsAnimating { get; }

    // Configuration
    public float EntryDuration { get; set; }     // default: 0.6
    public float SeriesStagger { get; set; }     // default: 0.1
    public int AnimationThreshold { get; set; }  // default: 2000
    public float ExitDuration { get; set; }      // default: 0.3

    // Methods
    public bool ShouldAnimate(int elementCount);
    public void StartEntry(Node owner, int seriesCount, int totalElementCount = 0);
    public void AnimateHover(Node owner, float targetScale);
    public void StartDataTransition(Node owner, float duration = 0.4f);
    public void StartExit(Node owner, Action? onComplete = null);
    public void Reset();
}
```

### AnimationContext

```csharp
public class AnimationContext
{
    public float EntryProgress { get; init; }           // default: 1
    public float GlobalOpacity { get; init; }           // default: 1
    public float HoverScale { get; init; }              // default: 1
    public float DataTransitionProgress { get; init; }  // default: 1
    public float ExitProgress { get; init; }            // default: 0

    public static AnimationContext Default { get; }
}
```

### EaseType & Ease

```csharp
public enum EaseType
{
    Linear, EaseInQuad, EaseOutQuad, EaseOutCubic,
    EaseInOutCubic, EaseOutBack, EaseOutElastic,
}

public static class Ease
{
    public static float Apply(float t, EaseType type);
}
```

---

## Interaction

### HitResult

```csharp
public class HitResult
{
    public bool Hit { get; init; }
    public DataRow? Row { get; init; }
    public int RowIndex { get; init; }
    public float ScreenX { get; init; }
    public float ScreenY { get; init; }
    public string? Label { get; init; }
    public string? SeriesKey { get; init; }
    public string? MarkType { get; init; }
    public Color ElementColor { get; init; }
    public string? FocusedSeries { get; set; }
}
```

### ChartClickEventArgs

```csharp
public class ChartClickEventArgs : EventArgs
{
    public DataRow? Row { get; init; }
    public int RowIndex { get; init; }
    public string? MarkType { get; init; }
    public Vector2 ScreenPosition { get; init; }
    public string? SeriesKey { get; init; }
}
```

### InteractionState (Enum)

```csharp
public enum InteractionState { Normal, Hovered, Selected }
```

### ChartInteraction (Static Utility)

```csharp
public static class ChartInteraction
{
    public static void DrawCrosshair(ICanvas2D canvas, Vector2 mousePos, PlotArea plot, ChartTheme? theme = null);
    public static HitResult? TestAll(List<Mark> marks, MarkContext ctx, Vector2 mousePos);
}
```

---

## Tooltip

### TooltipRenderer

```csharp
public class TooltipRenderer
{
    public float SmoothSpeed { get; set; }   // default: 12
    public float FadeSpeed { get; set; }     // default: 8
    public bool IsVisible { get; }
    public TooltipOptions Options { get; set; }
    public ChartTheme? Theme { get; set; }

    public void Update(float delta, HitResult? hit);
    public void Draw(ICanvas2D canvas, int canvasW, int canvasH);
}
```

### TooltipOptions

```csharp
public class TooltipOptions
{
    public Func<TooltipContext, IReadOnlyList<TooltipLine>>? RichContentBuilder { get; set; }
    public Func<TooltipContext, IReadOnlyList<string>>? ContentBuilder { get; set; }
    public Color? BackgroundColor { get; set; }
    public Color? TextColor { get; set; }
    public Color? BorderColor { get; set; }
    public float CornerRadius { get; set; }  // default: 6
    public float Padding { get; set; }       // default: 8
    public float? FontSize { get; set; }
}
```

### TooltipLine & TooltipSpan

```csharp
public struct TooltipLine
{
    public TooltipSpan[] Spans { get; init; }
    public static TooltipLine Plain(string text);
    public static TooltipLine WithIcon(TooltipIcon icon, Color color, string text);
}

public struct TooltipSpan
{
    public string Text { get; init; }
    public Color? Color { get; init; }
    public bool Bold { get; init; }
    public bool Italic { get; init; }
    public float? FontSize { get; init; }
    public TextDecoration Decoration { get; init; }
    public float LetterSpacing { get; init; }
    public string? Family { get; init; }
    public Font? GodotFont { get; init; }
    public TooltipIcon Icon { get; init; }
    public IImageHandle? Image { get; init; }
}

public enum TooltipIcon { None, Circle, Square, Diamond, Triangle }
```

---

## ChartTheme

```csharp
[GlobalClass]
public partial class ChartTheme : Resource
{
    // Factory methods
    public static ChartTheme Dark();
    public static ChartTheme Light();
    public ChartTheme Clone();

    // Static palettes
    public static readonly Color[] DefaultPalette;
    public static readonly Color[] DefaultSequentialGradient;

    // 80+ [Export] properties organized in groups:
    // Color Palette, Chart Frame, Layout, Typography,
    // Mark Defaults, Selection & Hover, Polar / Segment,
    // Tooltip, Crosshair, Legend, Line/Point, Radar,
    // Box, Violin, etc.
}
```

See [Customization & Theming](customization.md#theme-property-groups) for the full property listing.

---

## Canvas Abstraction

### ICanvas2D

```csharp
public interface ICanvas2D : IDisposable
{
    void Resize(int width, int height);
    void BeginFrame();
    void EndFrame();
    void Clear(Color color);

    IPath2D CreatePath();
    IPaint2D CreatePaint();

    void Stroke(IPath2D path, IPaint2D paint);
    void Fill(IPath2D path, IPaint2D paint);
    void StrokeAndFill(IPath2D path, IPaint2D stroke, IPaint2D fill);

    void DrawLine(float x0, float y0, float x1, float y1, IPaint2D paint);
    void DrawRect(float x, float y, float w, float h, IPaint2D paint);
    void DrawCircle(float cx, float cy, float r, IPaint2D paint);
    void DrawText(string text, float x, float y, FontSettings font, IPaint2D paint);

    IImageHandle LoadImage(int w, int h, byte[] rgbaPixels);
    IImageHandle LoadImage(Texture2D texture);
    void DrawImage(IImageHandle img, float x, float y, float dstW, float dstH, float opacity = 1f);

    void Save();
    void Restore();
    IDisposable SaveScope();
    void Translate(float x, float y);
    void Scale(float sx, float sy);
    void Rotate(float angle);
    void ClipRect(float x, float y, float w, float h);

    void Tick();
    TextMetrics MeasureText(string text, FontSettings font);
    CanvasCapabilities Capabilities { get; }
}
```

### IPath2D

```csharp
public interface IPath2D : IDisposable
{
    IPath2D MoveTo(float x, float y);
    IPath2D LineTo(float x, float y);
    IPath2D CubicTo(float cx1, float cy1, float cx2, float cy2, float x, float y);
    IPath2D QuadTo(float cx, float cy, float x, float y);
    IPath2D ArcTo(float cx, float cy, float radius, float startAngle, float endAngle, bool clockwise = false);
    IPath2D Rect(float x, float y, float w, float h);
    IPath2D RoundRect(float x, float y, float w, float h, float radius);
    IPath2D Circle(float cx, float cy, float radius);
    IPath2D Close();
    IPath2D Reset();
}
```

### IPaint2D

```csharp
public interface IPaint2D : IDisposable
{
    IPaint2D SetColor(Color color);
    IPaint2D SetStrokeWidth(float width);
    IPaint2D SetAntiAlias(bool aa);
    IPaint2D SetLineCap(LineCap cap);
    IPaint2D SetLineJoin(LineJoin join);
    IPaint2D SetMiterLimit(float limit);
    IPaint2D SetLineDash(float[] pattern, float offset = 0f);
    IPaint2D SetOpacity(float alpha);
    IPaint2D SetLinearGradient(float x0, float y0, float x1, float y1, GradientStop[] stops);
    IPaint2D SetRadialGradient(float cx, float cy, float radius, GradientStop[] stops);
}
```

### FontSettings

```csharp
public readonly record struct FontSettings
{
    public float Size { get; init; }                  // default: 13
    public string? Family { get; init; }
    public bool Bold { get; init; }
    public bool Italic { get; init; }
    public float LetterSpacing { get; init; }
    public float LineHeightMultiplier { get; init; }  // default: 1.2
    public TextAlign Align { get; init; }
    public TextDecoration Decoration { get; init; }
    public Font? GodotFont { get; init; }

    public static FontSettings Default { get; }
}

public enum TextAlign { Left, Center, Right }
```
