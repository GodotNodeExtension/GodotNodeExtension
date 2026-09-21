**English** | [中文](api-reference.cn.md)

# API Reference

Complete listing of all public classes, interfaces, enums, and their members in GodotChart.

---

## ChartView (Quick Path)

```csharp
[GlobalClass]
public partial class ChartView : Control
```

One node that shows a chart: set the kind, feed it rows, and it builds the mark, maps the channels,
configures the axes and the legend, handles hover with a tooltip, keeps the surface at the node size
and redraws itself. Sizing and presentation are internal - it behaves like a `TextureRect`.

### Exported properties

Every `*Field` property below holds a **field name**, not a value: the name is looked up in every row (the
keys of the `Rows` dictionaries, or of `DataRow.Set(...)`), and the row's value in that field drives the
channel. `XField = "month"` therefore means "read the `month` field of each row"; a row that does not
carry the field simply contributes no value for that channel.

| Property | Type | Default | Description |
|---|---|---|---|
| `Kind` | `ChartKind` | `Bar` | Chart type - always explicit, the node never guesses it from the rows |
| `Rows` | `Array<Dictionary>` | empty | Typed rows - the inspector-editable form of the data; each dictionary's **keys** are the field names the `*Field` properties refer to |
| `WindowSize` | `int` | `0` | Keep at most this many rows while streaming; `AddRow` drops the oldest past it (0 = unlimited) |
| `XField` | `string` | `""` | Field mapped to the X channel (category); empty uses the kind default |
| `YField` | `string` | `""` | Field mapped to the Y channel (value) |
| `ColorField` | `string` | `""` | Field mapped to the colour channel (drives the legend); empty uses `series`, or the category for Pie / Donut / Funnel / Waffle. A field of colours (`Color` values or `"#rrggbb"` strings) is used as it is, and `constant:#ff8800` paints the whole chart in one colour |
| `SizeField` | `string` | `""` | Field mapped to the size channel (bubble charts): the value maps `PointSizeMin`…`PointSizeMin + PointSizeRange` px over the column's data extent |
| `ShapeField` | `string` | `""` | Field mapped to the shape channel (symbol per category) |
| `OpacityField` | `string` | `""` | Field mapped to the opacity channel: the value maps `0`…`max` of the column onto `0`…`1` (clamped) |
| `XAxisTitle` / `XAxisUnit` | `string` | `""` | X axis title / unit (the unit shows in the axis tooltip) |
| `YAxisTitle` / `YAxisUnit` | `string` | `""` | Y axis title / unit |
| `Legend` | `LegendPosition` | `Bottom` | Legend placement (`Top` / `Bottom` / `Left` / `Right` / `None`) |
| `ShowTooltip` | `bool` | `true` | Hover tooltip |
| `ShowCrosshair` | `bool` | `true` | Crosshair that follows the pointer |
| `XAxisTickStep` / `YAxisTickStep` | `float` | `0` | Tick step in data units (`10` = 1970, 1980, …); `0` keeps the automatic step, which refines through whole multiples (10 → 5 → 2 → 1) |
| `XAxisTickCount` / `YAxisTickCount` | `int` | `0` | Exact tick count per axis; `0` derives it from the axis length and `TickLabelSpacing` |
| `XAxisTickSpacing` / `YAxisTickSpacing` | `float` | `0` | Pixels between the tick labels of that axis; `0` keeps the automatic spacing (X: `ChartTheme.TickLabelSpacing`, Y: the label line height) |
| `XAxisLabelFormat` / `YAxisLabelFormat` | `string` | `""` | Format string for numeric tick labels (`"0.0 °C"`); empty keeps the scale's own text |
| `XAxisLabelRotation` | `float` | `0` | Rotation of the X-axis tick labels in degrees; `0` keeps them horizontal (long category names show in full instead of every second one) |
| `ZoomMode` | `ChartZoomMode` | `None` | Wheel zoom and drag pan (`ZoomX` / `PanX` / `X` / `Y` / `Both`); `None` leaves the wheel to the host |
| `ZoomFactor` | `float` | `1.2` | Zoom per wheel notch |
| `PanButton` | `MouseButton` | `Left` | Button that drags the window |
| `ResetZoomOnDoubleClick` | `bool` | `true` | Double click resets the zoom |
| `SectionLevels` | `Array[float]` | empty | Reference lines, see `SectionMark` |
| `SectionBandFrom` / `SectionBandTo` | `float` | `0` | Ends of a reference band; equal values draw none |
| `SectionTarget` | `ChartSectionTarget` | `Y` | Axis the sections read (`Y`, `Y2`, `X`) |
| `SectionColor` | `Color` | orange | Colour of the lines, the band and their labels |
| `SectionDashed` | `bool` | `true` | Dashed reference lines |
| `SectionLabelFormat` | `string` | `""` | Format string for the level labels; empty draws none |
| `HeatmapMaxCells` | `int` | `0` | Cell budget for a heatmap (`0` keeps `HeatmapMark.MaxCells`); rows past it are not drawn and the mark warns |
| `DiagramMaxNodes` | `int` | `0` | Node budget for a treemap or sankey (`0` keeps the mark's own default); rows past it are not drawn and the mark warns |
| `Title` | `string` | `""` | Chart title |
| `ThemeKind` | `ChartThemeKind` | `Dark` | Built-in palette (`Dark` / `Light`) |
| `ColorMapping` | `ColorMappingKind` | `Auto` | How the colour channel maps values: `Auto` (colours as they are, everything else categorical), `Category`, `Identity`, `Sequential`, `Diverging` |
| `XAxisRange` / `YAxisRange` | `Vector2` | `(0, 0)` | Pinned `(min, max)` domain of that axis; `(0, 0)` fits the data. Only a numeric axis is pinned - a category axis ignores it |
| `YAxisAutoScaleMargin` | `float` | `0` | Fraction of the domain the auto-fit keeps as a margin, so small moves do not refit the axis (`0` refits whenever the data leaves the domain) |
| `YAxisNiceDomain` | `bool` | `false` | Round the auto-fitted domain out to a `{1, 2, 5} x 10^n` step |
| `YAxisMinLimit` / `YAxisMaxLimit` | `float` | `NaN` | Pin one end of the Y axis (`NaN` keeps it free); the other end still follows the data |
| `SizeRange` | `Vector2` | `(0, 0)` | `(min, max)` radius in pixels for the size channel; `(0, 0)` uses `PointSizeMin`…`PointSizeMin + PointSizeRange` |
| `OpacityRange` | `Vector2` | `(0, 0)` | `(min, max)` opacity for the opacity channel; `(0, 0)` maps onto the full `0…1` |
| `ShapeSymbols` | `Array<ShapeKind>` | empty | Symbols the shape channel cycles through; empty uses the default vocabulary |
| `GroupedBars` | `bool` | `false` | Bar kinds: lay the series of one category side by side instead of overlapping them (ignored while the mark is stacked) |
| `Stack` | `StackMode` | `None` | Bar and area kinds: `None` / `Stack` / `Normalize` |
| `Decimate` | `DecimateMode` | `Auto` | Point reduction for line and area kinds: `Auto` keeps the lowest and highest point of every pixel column, `On` reduces even when the series fits, `Off` draws every row |
| `CustomTheme` | `ChartTheme?` | null | Theme resource (colours, line widths, sizes, font, palette); null uses the built-in palette of `ThemeKind`. The node only reads it, and follows its `changed` signal |
| `LayeredRendering` | `bool` | `false` | Keep the non-interactive layer in an image and repaint only the overlay (hover/selection, crosshair, tooltip) while the pointer moves. Reaches the chart as `Chart.UseLayerCache` - see [Layered rendering](#layered-rendering) |
| `PlotAspectRatio` | `float` | `0` | Shape the content is given, as width / height (`1` = square); `0` leaves it to the marks (a pie, radar, gauge, chord or sunburst asks for a square), a negative value forces filling the plot area |
| `PlotAlignHorizontal` | `HorizontalAlignment` | `Center` | Where the content box sits inside the plot area while `PlotAspectRatio` shapes it |
| `PlotAlignVertical` | `VerticalAlignment` | `Center` | Same, vertically |
| `EditorPreview` | `bool` | `true` | Also draw the chart inside the editor (the node is a tool script) |
| `IgnoreContentMinimumSize` | `bool` | `false` | Report the plot's own minimum size instead of the content's (title, legend, axis labels), so a page may squeeze the node |

```csharp compile
// The ChartView exports a page either writes in the scene or sets from code. Every one of them is shown on a
// page of its own (ChartLayoutDemo, ChartBigDataDemo, BasicsDemo); this is the whole set at once.
view.XAxisLabelRotation = 30f;          // long category names stay readable at an angle
view.XAxisTickStep = 500f;              // one step in data units; 0 lets the axis decide
view.XAxisTickSpacing = 40f;            // pixels one label may take; 0 keeps the theme's spacing
view.XAxisTickCount = 12;               // an exact count; 0 derives it from the axis length
view.XAxisLabelFormat = "0.0 °C";       // empty keeps the scale's own text
view.YAxisLabelFormat = "0.0 °C";       // both axes are labelled by the same code
view.YAxisMinLimit = 0f;                // NaN fits that end
view.YAxisMaxLimit = 100f;
view.PlotAspectRatio = 1f;              // width / height of the content box (1 = square); 0 leaves it to the marks
view.PlotAlignVertical = VerticalAlignment.Center;
view.Decimate = DecimateMode.On;        // point reduction for line/area kinds
view.ZoomFactor = 1.5f;                 // how far one wheel step goes
view.PanButton = MouseButton.Middle;
view.ResetZoomOnDoubleClick = false;
view.HeatmapMaxCells = 64;              // cell budget; the mark warns when the rows exceed it
view.DiagramMaxNodes = 24;              // node budget for treemap and sankey
view.SectionTarget = ChartSectionTarget.Y;  // the axis the reference lines belong to
view.SectionDashed = false;             // solid instead of dashed
```

### Default Field Bindings

`Kind` is always explicit - the node never infers it from the rows (see the `ChartKind` list below).
These are the fields `ChartView` binds per kind; an explicit `XField` / `YField` / `ColorField` always wins.

| Kind | Encodings |
|------|-----------|
| Heatmap | `X` ← `x`, `Y` ← `y`, `Color` ← `value` |
| Timeline | `Y` ← `category`, `X` ← `value` (categories vertical, ranges horizontal) |
| Milestone | `X` ← `time`, `Y` ← `lane`; `Color` ← `series` when the rows carry that field - no value channel is bound |
| Sankey, Chord | `Y` ← `value`; the marks read `source` / `target` by name |
| Box, Candlestick | `X` ← `category` only - the value domain comes from the mark's own fields (`min`…`max`, `open`…`close`), so encoding Y would drag the axis to zero |
| Pie, Donut, Funnel, Waffle | `X` ← `category`, `Y` ← `value`; `Color` ← `category`, because one row *is* one slice / stage / cell and the category names it - one palette colour each, and the legend lists the categories |
| Every other kind | `X` ← `category`, `Y` ← `value`; `Color` ← `series` when the rows carry that field |

`SizeField`, `OpacityField` and `ShapeField` are encoded only when the rows actually carry the field. A second
value axis (`Channel.Y2`) is not part of the node: build it with the `Chart` API.

### Members

| Member | Returns | Description |
|---|---|---|
| `SetData(IEnumerable<DataRow> rows)` | ChartView | Replace the rows (typed values; clears the exported array) |
| `SetData(params DataRow[] rows)` | ChartView | Replace the rows |
| `AddRow(DataRow row)` | ChartView | Append one row (mirrored into the exported array) |
| `SetValues(IEnumerable<(string, double)> values)` | ChartView | Quick path for category/value pairs |
| `SetCsv(string csv)` / `ParseCsv(string csv)` | ChartView / `List<DataRow>` | CSV remains available from code |
| `Repaint()` | void | Redraw the current frame without rebuilding (per-frame state such as animation or hover survives) |
| `Clear()` | ChartView | Drop the rows |
| `Refresh()` | void | Redraw with the current settings |
| `ConfigureMark(Action<Mark> configure)` | ChartView | Tweak the built mark (extra fields, styling) |
| `ConfigureChart` | `Action<Chart>?` | Configure the built chart itself: a **property** (assign a delegate), not a method like `ConfigureMark`. Use it for scales the exports cannot express or an extra mark; the delegate is **invoked on every rebuild** |
| `Kind` | `ChartKind` | Chart type (explicit) |
| `DataRows` | `IReadOnlyList<DataRow>` | Rows currently shown |
| `Rows` | `Array<Dictionary>` | The typed rows as Godot variants |
| `Chart` | `Chart` | The underlying chart (escape hatch) |
| `Canvas` / `Texture` / `Surface` | `ICanvas2D` / `Texture2D` / `Canvas2DControl` | The hosted canvas and its surface |
| `Tooltip` | `TooltipRenderer` | The tooltip renderer, for styling |
| `CanvasFactory` | `Func<int,int,ICanvas2D>` | Custom / injected / shared canvas |
| `SavePng(string path)` | `bool` | Writes the surface the view is showing to a PNG (what a tool, a build script or a doc shot needs); `false` without a rendering device |

### ChartKind (Enum)

`Bar`, `Line`, `Area`, `Scatter`, `RangeArea`, `Pie`, `Donut`, `Radar`, `Violin`, `Box`,
`Candlestick`, `Heatmap`, `Treemap`, `Sunburst`, `Sankey`, `Chord`, `Gauge`, `Funnel`, `Waffle`,
`Timeline`, `Lollipop`, `Milestone`.

### ChartThemeKind (Enum)

`Dark`, `Light`.

### ColorMappingKind (Enum)

`Auto`, `Category`, `Identity`, `Sequential`, `Diverging` - how the colour channel turns values into
colours (see `ChartView.ColorMapping`; the mapping is configured per view, not per mark).

---
### SectionMark (Mark)

Annotation mark: reference lines and a band over the plot, for a target or a threshold. Mapped through
`Target`, it contributes to no scale (a level far outside the table cannot stretch the axis), never appears in
the legend, and a level outside the visible window is skipped rather than pinned to the edge. Zooming and
panning move the lines with the data.

| Member | Type | Default | Description |
|---|---|---|---|
| `Levels` | `double[]` | empty | Values to draw a line at |
| `BandFrom` / `BandTo` | `double?` | `null` | Band ends; both are needed |
| `Target` | `Channel` | `Channel.Y` | `Y` (horizontal), `Y2` (right axis), `X` (vertical) |
| `Thickness` | `float` | `1` | Line width in pixels |
| `Dashed` | `bool` | `true` | Dashed lines |
| `DashLength` / `DashGap` | `float` | `6` / `4` | Dash geometry in pixels |
| `Color` | `Color?` | `null` | `null` uses the theme's grid colour |
| `BandOpacity` | `float` | `0.12` | Opacity of the band fill |
| `LabelFormat` | `string` | `"{0}"` | Inherited from `Mark`: the level value, or no label when empty. `{1}` comes out empty - a section spans the other axis and has no single value there |
| `LabelInset` | `float` | `6` | Label distance from the plot edge |

### AxisConfig

Every axis is configured through an `AxisConfig` (`chart.XAxis(...)`, `chart.YAxis(...)`, `chart.Y2Axis(...)`).

| Member | Type | Default | Description |
|---|---|---|---|
| `Title` / `Description` / `Unit` | `string?` | `null` | Heading, tooltip text and unit shown with the axis |
| `TickStep` | `double?` | `null` | Step between ticks in data units (`10` = 1970, 1980, …); overrides the automatic step, which refines through whole multiples |
| `TickCount` | `int?` | `null` | Number of ticks to aim for, over the count derived from the axis length |
| `Ticks` | `double[]?` | `null` | Exact tick values, over everything else; values outside the window are skipped |
| `LabelFormat` | `string?` | `null` | Format string for numeric tick labels |
| `LabelRotation` | `float?` | `null` | Rotation of this axis' tick labels in degrees (X axis only) |
| `AutoScaleMargin` | `float?` | `null` | Margin a sticky auto-scaled axis keeps around the data; inside it the domain does not move |
| `NiceDomain` | `bool` | `false` | Round the fitted domain out to a {1, 2, 5} x 10^n step |
| `MinLimit` / `MaxLimit` | `double?` | `null` | Pin one end and keep fitting the other |

## Chart (Main Entry Point)

```csharp
public partial class Chart : IDisposable
```

### Construction & Disposal

| Method | Returns | Description |
|--------|---------|-------------|
| `Chart(ICanvas2D canvas, bool ownsCanvas = false)` | — | Constructor: draws onto `canvas` (created via `Canvas2DFactory.Create`). With `ownsCanvas: true`, `Dispose()` disposes that canvas too - leave it false (the default) when several charts share one canvas |
| `Dispose()` | void | Release resources |

### Layout and sizes

The chart can say how much room its content needs, which is what keeps a small card from squeezing the axis
labels away:

| Member | Type | Description |
|---|---|---|
| `MinimumSize` | `Vector2` | Smallest size the current content stays readable at: title, legend, axis labels and axis titles, plus `MinimumPlotSize`. Recomputed with the layout; an estimate that errs generous |
| `MinimumPlotSize` | `Vector2` (static) | Plot rectangle a `MinimumSize` keeps (120x80); everything else in it is decorations |
| `CurrentPlotArea` | `PlotArea?` | The plot rectangle the last frame used |
| `Width` / `Height` | `float` | Surface size the chart draws into |
| `DrawnBounds` | `Rect2?` | The rectangle the chart actually used in the last frame: the content rectangle united with the decoration bands the layout reserved (title, legend, axis label columns, axis titles). It follows the decorations - turning one off moves it - and it excludes the background fill (which covers the whole canvas by design) and the host's tooltip. Null before the first frame |

A chart below its `MinimumSize` pushes one warning (not one per frame) and keeps drawing with thinned labels.
`ChartView` reports the same number to the engine through `_GetMinimumSize()`, so neither a container nor the
engine lets the node shrink past it; `IgnoreContentMinimumSize` (an export, default off) opts a node out of that.
Nothing has to be drawn first for it to work: until the chart exists the node reports an estimate of the same
reservations (theme bands, a plausible label column, one legend row), and the chart's own measurement replaces it
once the first frame has drawn - the container is then laid out again.

Two rectangles are worth telling apart. The **plot area** is what the chart's padding and reservations leave; the
**content box** is what the marks are laid out in, and it is the rectangle `CurrentPlotArea` reports. A mark that
draws from the plot's *short* edge - every polar mark, whose radius is `min(width, height) / 2` times a factor -
asks for a square content box (`Mark.PreferredAspectRatio`), so on a wide canvas the circle is no longer laid out
in a rectangle it can only use a fraction of. `PlotAspectRatio` overrides the shape (`0` forces the historical
"fill the plot area" behaviour), and `PlotAlignHorizontal` / `PlotAlignVertical` place the box inside the plot
area. The decorations keep the whole plot area: a legend still wraps to its full width, and the title stays where
it was. Where the chart ends as a whole is `DrawnBounds`, which follows the decorations around it.

### Fluent Builder Methods

| Method | Returns | Description |
|--------|---------|-------------|
| `Theme(ChartTheme theme)` | Chart | Set the theme; the chart follows its `changed` signal (cached layout and mark geometry are dropped) and `Dispose()` stops following it |
| `Mark(Mark mark)` | Chart | Add visual mark |
| `Mark<T>() where T : Mark, new()` | Chart | Add mark by type |
| `ApplyToAllMarks(Action<Mark> action)` | Chart | Batch configure marks |
| `Data(IEnumerable<DataRow> rows)` | Chart | Set data |
| `AppendData(DataRow row)` | Chart | Append single data row |
| `AppendData(IEnumerable<DataRow> rows)` | Chart | Append multiple rows |
| `Encode(Channel ch, string field)` | Chart | Bind field encoding |
| `Encode(Channel ch, object constant)` | Chart | Bind constant encoding |
| `ScaleDomain(Channel, double min, double max)` | Chart | Pin a channel's domain (linear scales only); applied after the automatic fit **and** after the marks' scale contributions, so the lock survives data changes and is not overridden by stacking or box-plot marks |
| `Scale(Channel ch, IScale scale)` | Chart | Set scale |
| `Transform(IDataTransform transform)` | Chart | Add data transform |
| `Animate(float progress)` | Chart | Entry animation progress |
| `Animate(AnimationContext? ctx)` | Chart | Full animation context |
| `XAxis(AxisConfig config)` | Chart | X axis configuration |
| `YAxis(AxisConfig config)` | Chart | Y axis configuration |
| `Y2Axis(AxisConfig config)` | Chart | Secondary Y axis |
| `Legend(LegendConfig config)` | Chart | Legend configuration |
| `Render()` | void | Draws one frame onto the canvas (not chainable) |

### Interaction Methods

| Method | Returns | Description |
|--------|---------|-------------|
| `HitTest(Vector2 pos)` | HitResult? | Hit test |
| `HandleClick(Vector2 pos, MouseButton button = MouseButton.Left)` | HitResult? | Raise `OnClick` with that button; only the left button updates selection/focus |
| `Select(int rowIndex)` | Chart | Select data row (uses the rendered, transformed data) |
| `Hover(int rowIndex)` | Chart | Set the hovered row without raising `OnHover` |
| `NotifyHoverChanged(int rowIndex, HitResult? hit)` | void | Set the hovered row and raise `OnHover` |
| `FocusSeries(string? seriesKey)` | Chart | Focus a series (dims the others) |
| `Interaction(Vector2? mousePos)` | Chart | Update interaction state (`null` clears the crosshair / hover) |

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
| `AutoPadding` | bool | Widen `PaddingLeft` / `PaddingRight` on demand so long Y/Y2 tick labels are not clipped (default true) |
| `PlotAspectRatio` | float? | Shape the content is given, as width / height (`1` = square). `null` (default) takes what the marks ask for while they agree (polar marks ask for a square); `0` forces filling the plot area |
| `PlotAlignHorizontal` | HorizontalAlignment | Where the content box sits in the plot area (`Left` / `Center` / `Right`, default `Center`) |
| `PlotAlignVertical` | VerticalAlignment | Same vertically (`Top` / `Center` / `Bottom`, default `Center`) |

The frame starts from the `ChartDefaults` metrics: a 600 x 400 px chart padded left 50 / right 20 / top 20
/ bottom 40 - every one of those is a property above, so the defaults are just a starting point.

### Layered rendering

A frame has two layers: the **data layer** (background, title, grid, axes, axis labels, legend and the marks
without their interaction state) and the **overlay** (the marks' hover/selection visuals and the crosshair).
The data layer can be kept in an image and presented again on every frame whose inputs did not change, which
is what makes moving the pointer cheap on a large table.

| Member | Type | Description |
|--------|------|-------------|
| `UseLayerCache` | bool | Keep the data layer in an image and repaint only the overlay; default `false` |
| `InvalidateLayerCache()` | Chart | Drop the cached layer, so the next `Render()` builds it again |

`UseLayerCache` only engages when the canvas backend can read its surface back
(`CanvasCapabilities.SupportsSurfaceCapture`) **and** every mark of the chart paints its interaction state on
the overlay (`Mark.InteractionStateInOverlay`); otherwise the chart renders single-pass and says why once. It
rebuilds the layer when the data, the layout, the plot area, the theme, the animation progress, the focused
series or the legend configuration change - and **never** for a pointer move, which is the point. Call
`InvalidateLayerCache()` after changing a mark's own setting directly (`mark.StrokeWidth = …`): the chart
cannot observe those. See [Layered rendering](advanced.md#layered-rendering) for the cost, the preconditions
and the fall-backs of the option.

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

Every one of them receives the same `RenderContext`, which carries the layout state of the frame - so a
renderer that takes over a stage draws against the chart's own numbers instead of measuring them again:

| Context member | Type | Meaning |
|---|---|---|
| `Plot` | `PlotArea` | The content box: the rectangle the marks, the grid and the axes are laid out in (`Chart.CurrentPlotArea`) |
| `FullPlot` | `PlotArea` | The plot area before `PlotAspectRatio` shaped the content - what the decorations were laid out against. The same rectangle as `Plot` while nothing shapes the content |
| `DrawnBounds` | `Rect2?` | The rectangle the whole chart used, the same value as `Chart.DrawnBounds` |
| `LegendLayout` | `LegendLayout?` | The legend geometry: the positioned item rectangles (`Items`) and the box they occupy. Null when the chart has no legend (or nothing to draw it with) |
| `OffsetX` / `OffsetY`, `Width` / `Height`, `PaddingLeft/Right/Top/Bottom` | `float` | The node's own rectangle and insets |
| `Theme`, `Scales`, `Encodes`, `Data`, `Title`, `XAxisConfig` / `YAxisConfig` / `Y2AxisConfig`, `LegendConfig`, `ColorScale`, `FocusedSeries`, `HiddenSeries`, `MousePos` | | Everything the built-in renderers read |

### Color Overrides

| Property | Type | Description |
|----------|------|-------------|
| `BackgroundColor` | Color | Background color |
| `GridColor` | Color | Grid color |
| `AxisColor` | Color | Axis color |

These are chart-wide renderer colors. Per-element styling lives on `Mark` instead: `Mark.StyleOverride`
is the style callback every built-in mark runs (the `ElementStyle` resolved from the data goes in, the
style to paint comes out) and `Mark.States` declares the hover / selected / inactive look. With no
callback the `Color` / `Opacity` channels decide, and a mark's own opacity properties (`FillOpacity`, ...)
are the fallback - nothing here is "reserved for custom marks". See **Element Style & States** under
[Mark Base Class](#mark-base-class).

### Events

| Event | Args Type | Description |
|-------|-----------|-------------|
| `OnClick` | ChartClickEventArgs | Element clicked (carries ScreenPosition/MarkType) |
| `OnSelectionChanged` | ChartSelectionEventArgs | Selection changed |
| `OnHover` | ChartHoverEventArgs | Hover changed |
| `OnFocusChanged` | ChartFocusEventArgs | Focused series changed |
| `OnLegendClick` | ChartLegendClickEventArgs | Legend item clicked |

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

Channels are slots, and each mark decides which ones it reads. The built-in marks consume `X`, `Y`,
`Y2`, `Color`, `Size`, `Opacity` and `Shape`; `Label` is used by `TimelineMark` and `MilestoneMark`. `Shape` draws the
symbol of a scatter/bubble point and of a lollipop dot, and the legend repeats that symbol instead of a
square swatch when the shape channel covers the same categories as the colour channel. A channel with no
encode - or a row without that field - simply has no value: a mark that needs it skips the row or draws
nothing.

An encode is set chart-wide with `Chart.Encode(channel, "field")` or for a single mark with
`Mark.Encode(channel, "field")`. The mark-level binding wins **for that mark only** and the chart-wide one
stays the fallback for the others; the scale is still one per channel (fitted from both fields), so a
channel whose marks bind different fields should be fitted explicitly with `Chart.Scale(...)`.

---

## MarkCoordinate (Enum)

```csharp
public enum MarkCoordinate
{
    Cartesian,      // X/Y grid
    Polar,          // Radial
    Hierarchical,   // Tree layout
    Flow,           // Network flow
    Geographic,     // A coordinate frame: a map, a globe, a game world
}
```

Marks of different coordinate systems cannot be combined in one chart (see
[Coordinate System Compatibility](advanced.md#coordinate-system-compatibility)). A geographic mark places its data
through a coordinate frame instead of through the scales, and draws no X/Y axes.

---

## Geographic Coordinates

Data does not always live on a pair of scales: it can have a position in a geography - a latitude and a
longitude, the map of an invented planet, or a game world in its own units. Two objects carry that. A
**frame** says what a coordinate means and how it lands in the normalized world marks are drawn in; a
**viewport** says which part of that world is shown and at what scale.

Both are public, so the projection is available to your own code as well: turning a coordinate into a
screen position, or a click back into a coordinate, is a call each. The built-in marks read the scales; a
frame with a viewport is how a position in a geography is placed, and a custom mark or a custom drawing
uses exactly the same two objects.

### IGeoFrame

```csharp
public interface IGeoFrame
{
    string  Name       { get; }   // for diagnostics
    double  Aspect     { get; }   // world width / height at equal scale
    double  WorldWidth { get; }   // horizontal extent, in the frame's own units
    bool    WrapsX     { get; }   // the horizontal axis wraps (a sphere's 360°)
    double? MaxAbsY    { get; }   // vertical truncation (±85.05112878° for Web Mercator, null on a plane)

    (double U, double V) Normalize(double x, double y);    // coordinate -> normalized [0, 1]², north up
    (double X, double Y) Denormalize(double u, double v);  // normalized -> coordinate
    double WrapX(double x);                                // fold a coordinate into the frame's own range
}
```

| Factory | Coordinates | Projection | `WrapsX` |
|---------|-------------|------------|----------|
| `GeoFrames.Wgs84()` | longitude, latitude | Web Mercator | true |
| `GeoFrames.Wgs84(GeoProjections.Equirectangular)` | longitude, latitude, poles included | plate carrée | true |
| `GeoFrames.CustomSphere(minLng, maxLng, minLat, maxLat)` | degrees over the ranges given | identity over those ranges | true |
| `GeoFrames.CustomPlane(minX, minY, maxX, maxY)` | world units, pixels, an abstract layout | none | false |

Normalized coordinates run bottom to top (north is up), and a frame's `Aspect` is what keeps its world
from being stretched: a viewport scales both axes by the same number of pixels per unit, so a map is not
distorted by a wide or a tall plot. A sphere frame's `Normalize` leaves the poles to the projection
(Web Mercator truncates them) and a wrapped longitude may fall outside `[0, 1]`, which is what lets the
same meridian be drawn one world to the left or right.

### IGeoProjection

A sphere frame normalizes through a projection: longitude and latitude in, normalized world out.

```csharp
public interface IGeoProjection
{
    string Name        { get; }
    double MaxLatitude { get; }   // 85.05112878 for Web Mercator, 90 for plate carrée
    double Aspect      { get; }   // 1 (a square world) for Web Mercator, 2 for plate carrée

    (double U, double V) Forward(double longitude, double latitude);
    (double Longitude, double Latitude) Inverse(double u, double v);
}
```

| Projection | Description |
|------------|-------------|
| `GeoProjections.WebMercator` | The default, and the scheme mainstream map services and tile sets use: conformal, with the poles truncated at ±85.05112878° |
| `GeoProjections.Equirectangular` | Longitude and latitude used as flat coordinates (a 360° x 180° world): shows the poles and keeps areas comparable to the degree grid |
| `GeoProjections.Identity(minLng, maxLng, minLat, maxLat)` | The ranges given are the world, equally scaled in degrees; the default projection of `CustomSphere` |

A projection is a pure function of its input, so one instance can be shared and the arithmetic can be
reused outside a chart. Implement the interface for another scheme and pass it to
`GeoFrames.Wgs84(projection)` or `GeoFrames.CustomSphere(..., projection)`.

### GeoViewport

The window a chart looks at its frame through: a centre, in the frame's own coordinates, and a zoom level.
One zoom level is one resolution for both axes, which is what keeps a map's shape - a pair of per-axis
windows (`ZoomDomain` / `PanDomain`) could not.

```csharp
public sealed class GeoViewport
{
    public GeoViewport(IGeoFrame frame);

    public IGeoFrame  Frame     { get; }
    public double     CenterX   { get; private set; }
    public double     CenterY   { get; private set; }
    public double     ZoomLevel { get; private set; }   // every step doubles the world's pixel size
    public bool       WrapsX    { get; private set; }
    public GeoBounds? PanBounds { get; private set; }
    public event Action? Changed;                       // raised whenever the view moves

    public double WorldWidthPixels  { get; }
    public double WorldHeightPixels { get; }
    public double PixelsPerUnit     { get; }            // pixels per horizontal unit of the frame
}
```

| Member | Description |
|--------|-------------|
| `Project(double x, double y, in PlotArea plot)` | A frame coordinate to screen pixels |
| `Unproject(Vector2 screen, in PlotArea plot)` | Screen pixels to a frame coordinate |
| `VisibleBounds(in PlotArea plot)` | The frame coordinates the plot rectangle shows |
| `SetView(double centerX, double centerY, double zoomLevel)` | Move the viewport in one step; the centre is clamped to `PanBounds` and folded by a wrapping frame |
| `SetCenter(double centerX, double centerY)` | Centre on a coordinate, keeping the zoom |
| `SetZoom(double zoomLevel)` | Set the zoom, keeping the centre |
| `ZoomBy(double factor, Vector2 anchorPixels, in PlotArea plot)` | Zoom around a screen anchor: the coordinate under it stays under it |
| `PanBy(Vector2 deltaPixels)` | Pan by a screen displacement, so the content follows a drag |
| `Fit(GeoBounds bounds, in PlotArea plot, float paddingRatio = 0.05f)` | Fit a rectangle of coordinates, keeping its shape |
| `FitWorld(in PlotArea plot, float paddingRatio = 0.05f)` | Fit the frame's own world |
| `SetWrapX(bool wrapsX)` | Turn wrapping off to see the split at the antimeridian |
| `SetPanBounds(GeoBounds? bounds)` | Limit where the centre can move, or clear the limit |
| `Changed` | Raised whenever the view changes; drop whatever was cached from the projection here |

A zoom level is continuous (`4.5` is a valid step), and `256 * 2^zoom` is the pixel size of the whole
world (see `GeoMath` below). The viewport reports a change rather than being polled, which is what lets a
host - or the chart itself - drop a cached projection when the map moves.

### Chart Geo Methods

| Method | Returns | Description |
|--------|---------|-------------|
| `GeoFrame` | IGeoFrame | The frame geographic layers are placed in: WGS84 with Web Mercator until set otherwise |
| `GeoViewport` | GeoViewport? | The viewport, or null while nothing has asked for one |
| `SetGeoFrame(IGeoFrame frame)` | Chart | Use another frame; the viewport is dropped with it, since its coordinates belonged to the old frame |
| `SetGeoViewport(double centerX, double centerY, double zoomLevel)` | Chart | Centre and zoom the viewport, creating it if needed |
| `ZoomGeo(double factor, Vector2 anchorPixels, in PlotArea plot)` | Chart | Zoom around a screen anchor |
| `PanGeo(Vector2 deltaPixels)` | Chart | Pan by a screen displacement |
| `FitGeoBounds(double minX, double minY, double maxX, double maxY, in PlotArea plot, float paddingRatio = 0.05f)` | Chart | Fit a rectangle of coordinates |
| `FitGeoWorld(in PlotArea plot, float paddingRatio = 0.05f)` | Chart | Fit the frame's world |
| `SetGeoWrapX(bool wrapsX)` | Chart | Horizontal wrapping on or off |
| `SetGeoPanBounds(GeoBounds? bounds)` | Chart | Limit where the centre can move |

Each of them invalidates the chart's layout, so a cached projection never survives a view change.

### GeoFeature and GeoJsonReader

A feature is one drawn thing: a geometry, what it is called, and whatever the source said about it.

```csharp
public sealed class GeoFeature
{
    public string? Id { get; }                     // GeoJSON's "id" (a string or a number)
    public string? Name { get; }                   // the "name" property, or the name you gave
    public GeoGeometry Geometry { get; }
    public IReadOnlyDictionary<string, object?> Properties { get; }
}

public sealed class GeoGeometry
{
    public GeoShapeKind Kind { get; }               // Point, LineString, Polygon, MultiPolygon
    public IReadOnlyList<GeoRing> Parts { get; }    // rings, lines or points, in source order
    public bool IsEmpty { get; }
    public GeoBounds? Bounds { get; }               // in the geometry's own coordinates
}

public readonly record struct GeoRing(IReadOnlyList<GeoPoint> Points, bool IsHole);
public readonly record struct GeoPoint(double X, double Y);
```

For a polygon the parts are one outer ring followed by its holes (the order the file gave them), which
`IsHole` marks. `GeoJsonReader` reads a `FeatureCollection`, a single `Feature`, or a bare geometry:

```csharp
public static IReadOnlyList<GeoFeature> Parse(string json, ICollection<string>? issues = null);
public static IReadOnlyList<GeoFeature> Parse(ReadOnlySpan<byte> utf8Json, ICollection<string>? issues = null);
public static IReadOnlyList<GeoFeature> ParseFile(string path, ICollection<string>? issues = null);
```

Points, lines and polygons (with holes) and their multi forms are read; a `GeometryCollection` is not.
Data that is not quite valid is skipped with a note - in the `issues` collection if you passed one, and
once as a warning either way - instead of taking a whole file down: field data is rarely perfect. What it
does not do: it does not know whether your coordinates are degrees or world units (the frame decides), it
does not repair ring winding (see `GeoMath.RingIsClockwise`), and it does not read TopoJSON.
`ParseFile` goes through Godot's file access, so a `res://` path works in an exported game.

### GeoGeometryBuilder

For geometry that does not come from a file: a level, a procedurally generated map, a layout computed in
code.

```csharp
var zone = new GeoGeometryBuilder()
    .Polygon((0, 0), (40, 0), (40, 30), (0, 30))
    .Hole((10, 10), (10, 20), (20, 20), (20, 10))    // a courtyard
    .Feature("zone-1", "North zone");

var route = new GeoGeometryBuilder().Line((0, 0), (20, 10), (40, 5)).Feature("route-1");

// A tile map is a grid of square features, each identified for a join.
IReadOnlyList<GeoFeature> cells = GeoGeometryBuilder.Grid(rows: 8, columns: 12,
                                                          idOf: (row, column) => $"r{row}c{column}");
```

A geometry holds polygons, lines or points - not a mix of them - and mixing them, adding a hole before an
outer ring, or giving a ring too few points throws instead of producing something no mark could read.

### GeoGraph

A metro diagram, a star chart, a relation graph: the geometry is the connection, not the outline.

```csharp
public sealed class GeoGraph
{
    public IReadOnlyList<GeoNode> Nodes { get; }
    public IReadOnlyList<GeoEdge> Edges { get; }    // edges with a missing end are dropped
    public GeoBounds? Bounds { get; }
    public bool TryGetNode(string id, out GeoNode? node);
}

public sealed record GeoNode(string Id, double X, double Y, string? Name = null, ...);
public sealed record GeoEdge(string SourceId, string TargetId, double? Weight = null, bool Directed = false, ...);
```

`GeoGraphBuilder` builds a graph in code (`Node`, `Edge`, `Build`) or from the two tables a graph usually
arrives as:

```csharp
var graph = GeoGraphBuilder.FromRows(nodeRows, edgeRows,
                                     idField: "id", xField: "x", yField: "y",
                                     sourceField: "source", targetField: "target");
```

An edge that names a node the graph does not contain is dropped with one warning - a diagram that is
missing a line is a smaller problem than a render that throws halfway through a frame. A self loop, a
repeated edge and an isolated node are all kept.

### GeoDataJoiner

The join is the question a chart has to answer and a map library does not: which row belongs to this
region.

```csharp
var joiner = new GeoDataJoiner(rowField: "province", featureKey: "name",
                               missing: GeoJoinMissing.Report,
                               keyComparer: GeoDataJoiner.LooseKeys);

GeoJoinResult joined = joiner.Join(rows, features);
// joined.RowOfElement[i]   -> the row of feature i, or null (drawn in the "no data" style)
// joined.UnmatchedRows     -> the rows no element matched
// joined.MatchedElementCount / MissingElementCount
```

`featureKey` is `name` (which falls back to the identifier, so map data carrying only an id still joins),
`id`, or the name of any property the source has. Overloads join nodes (by id) and edges (by their two
ends). Neither side is ever dropped silently: the default strategy prints one warning with the counts,
`Silent` says nothing and `Strict` throws. A second row for an element that already has one is reported
as unmatched instead of overwriting the first.

### GeoMark

The base class for a geographic mark of your own: the coordinate system, the geometry source, and the
projection cache. The chart hands the mark its frame through `MarkContext.GeoViewport`.

```csharp
public abstract class GeoMark : Mark
{
    public sealed override MarkCoordinate Coordinate => MarkCoordinate.Geographic;
    public sealed override bool UsesAxes => false;
    public abstract GeoGeometrySource Source { get; }              // Feature, Graph or Points
    public virtual bool RequiresContinuousSpace => false;

    protected readonly record struct ProjectionCache<T>(LayoutCacheKey Key, T? Value) where T : class;
    protected static T GetProjection<T>(MarkContext ctx, ref ProjectionCache<T> cache,
                                        Func<MarkContext, T> build) where T : class;
}
```

`GetProjection` is the cache a projection wants: the value is built once and reused until the layout
changes - and a viewport move counts as one, which is what keeps a dragged map from painting a stale
projection. `Source` and `RequiresContinuousSpace` are what the chart checks mark combinations with: a
mark that needs a continuous space next to a graph is reported, because a field over the connections
between nodes renders something meaningless rather than something wrong.

### GeoBounds and GeoMath

```csharp
public readonly record struct GeoBounds(double MinX, double MinY, double MaxX, double MaxY)
{
    public double Width   { get; }
    public double Height  { get; }
    public bool   IsEmpty { get; }   // no interior to fit a viewport to
    public double CenterX { get; }
    public double CenterY { get; }

    public static GeoBounds FromCorners(double x0, double y0, double x1, double y1);
}
```

`GeoBounds` is in frame coordinates (degrees, or world units) and holds `double`, not `Rect2`'s single
precision: a meridian is a lot of detail to lose at a deep zoom. `GeoMath` is the arithmetic a zoom level
comes with - `WorldSizeAtZoomZero` (256 px), `MinZoomLevel` / `MaxZoomLevel` (the range a viewport clamps
to, so a projection stays finite), `EarthCircumference`, `MercatorMetersPerDegree`, and
`ZoomToResolution` / `ResolutionToZoom`, which say how much of the frame one pixel covers.

### Example

```csharp compile
// A viewport of its own: fit a rectangle, project a coordinate, read a screen position back.
var plot = new PlotArea(0, 0, 400, 300);
var viewport = new GeoViewport(GeoFrames.Wgs84());
viewport.Fit(new GeoBounds(-10, 35, 30, 60), plot);          // a region of Europe

Vector2 london = viewport.Project(-0.12, 51.5, plot);        // longitude, latitude -> pixels
viewport.Unproject(london, plot);                            // pixels -> longitude, latitude

// Or let the chart own it, and navigate through the chart - the plot to work in is the caller's.
chart.SetGeoViewport(-0.12, 51.5, 6.0)
     .ZoomGeo(1.2, mousePos, plot)
     .PanGeo(new Vector2(10, 0));

chart.SetGeoFrame(GeoFrames.CustomPlane(0, 0, 1000, 500));   // the viewport goes with the frame
chart.FitGeoWorld(plot);
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
| `UsesAxes` | bool | Whether the mark draws against the chart's axes and grid (virtual, default true); a chart whose marks all answer false draws no axes at all, and `WaffleMark` overrides it to false |
| `YChannel` | Channel | Y channel used for positioning (`Y` or `Y2`) |
| `Data` | List<DataRow>? | Mark-local data (overrides the chart data) |
| `LabelFormat` | string | Format string for data labels; `{0}`/`{1}` are defined by each mark, and a mark that draws its own labels ignores `LabelPosition` (Gauge formats its labels from the ticks) |
| `LabelPosition` | LabelPosition | Placement of data labels |
| `StyleOverride` | Func<DataRow, int, ElementStyle, ElementStyle>? | Per-element style callback; **every built-in mark** runs it |
| `States` | ElementStateStyles | Declarative hover / selected / inactive styling, read-only (`{ get; }`: set its members, do not assign the property); an unset member keeps the theme value |
| `LabelContentBuilder` | Func<LabelContext, IReadOnlyList<TooltipLine>?>? | Replaces the text of every label drawn through `DrawLabels` (the built-in Interval/Line/Point/Milestone marks and any custom mark); returning null falls back to `LabelFormat`. Marks that draw their own labels use their own builders (a pie has `PieMark.SliceLabelBuilder`) |
| `TooltipContentBuilder` | Func<TooltipContext, IReadOnlyList<TooltipLine>>? | Custom tooltip content |
| `Encode(Channel, string)` | Mark | Mark-level encode; wins over the chart's encode for this mark only |
| `Encode(Channel, object)` | Mark | Mark-level constant encode (e.g. one fixed colour for this mark) |
| `HitTest(MarkContext, Vector2)` | HitResult? | Hit test one element |
| `ContributeScales(...)` | void | Let the mark widen the chart's scales |
| `InteractionStateInOverlay` | bool | True when the mark paints its interaction-state visuals on the overlay pass instead of in `Render` (virtual, default false). The marks that answer true are `LineMark`, `PointMark`, `IntervalMark` (stacked included: the overlay walks the same accumulation), `BoxMark`, `CandlestickMark`, `HeatmapMark`, `LollipopMark`, `MilestoneMark`, `TimelineMark`, `WaffleMark`, `FunnelMark`, `GaugeMark`, `TreemapMark` and `SectionMark` (an annotation mark: it has no interaction state of its own, so its overlay is empty by design). The marks that answer false on purpose - `RangeAreaMark`, `ViolinMark`, `PieMark`, `RadarMark`, `SankeyMark`, `ChordMark` and `SunburstMark` - carry the reason on their own declaration: their hover look is the element's own translucent fill, or geometry the overlay cannot reproduce without erasing something the cached layer holds. A chart whose marks all answer true can keep its data layer in an image (`Chart.UseLayerCache`) |
| `PreferredAspectRatio` | float? | Shape this mark's content wants, as width / height (virtual, default null = fill the plot area). The polar marks (`PieMark`, `RadarMark`, `GaugeMark`, `ChordMark`, `SunburstMark`) ask for a square, and the chart honours it while every mark agrees - see `Chart.PlotAspectRatio` |
| `RenderOverlay(MarkContext ctx)` | void | Paint the mark's hover/selection visuals for a frame whose data layer is cached (virtual, default: nothing). Runs next to the crosshair, after the cached layer, under the plot clip; `MarkContext.StateInOverlay` tells the two halves of the split which one paints the state |
| `Render(MarkContext ctx)` | void | The only abstract member: a mark paints its elements here. The context carries the canvas, the plot area, the projection that maps a normalized coordinate to that plot area (`Mapper`), the map viewport when the chart has a geographic layer (`GeoViewport`), the resolved scales / encodes / data, the per-frame `Animation` and the interaction state (hovered / selected row, `StateInOverlay`) |

`CreatePath()` / `CreatePaint()` and the `protected` helpers on top of them are what a custom mark builds
with: `ShapePath(ctx)` / `ShapePaint(ctx)` (the pooled ones - see the canvas abstraction), `ShapeGeometry`
for the shared glyph vocabulary, `ToDouble` / `ToSingle` / `GetDouble` for field values (a value that cannot
be read as a number comes back as `NaN`, reported once per field), and `PaletteOf(ctx)` for the palette its
elements are coloured from - the theme's own when it has one, the built-in default otherwise.

### Element Style & States

The data decides *what* an element is (the colour channel plus the mark's defaults), the style decides
*how* it is drawn. `Mark.StyleOverride` is that callback and `Mark.States` is the declarative per-state
look - set only what should differ from the theme:

```csharp compile
new IntervalMark
{
    // Style callback: the style resolved from the data comes in, the style to paint goes out.
    StyleOverride = (row, i, style) => i == 3 ? style.WithFill(Colors.Orange) : style,

    // Declarative states: an unset member keeps the theme value.
    States =
    {
        ActiveFill          = Colors.White,   // hovered fill (default: brighten the data fill)
        SelectedStroke      = Colors.Yellow,  // selection ring colour
        SelectedStrokeWidth = 3f,             // selection ring width
        InactiveOpacity     = 0.4f,           // opacity while another series is focused
    },
};
```

| `ElementStyle` member | Type | Description |
|-----------------------|------|-------------|
| `Fill` | Color | Fill colour of the element |
| `Opacity` | float | Opacity of the element (0..1) |
| `WithFill(Color)` | ElementStyle | The same style with another fill |
| `WithOpacity(float)` | ElementStyle | The same style with another opacity |

| `ElementState` value | Meaning |
|----------------------|---------|
| `Default` | Nothing special: the element is drawn with its data style |
| `Active` | The pointer is over the element (G2's `active` state) |
| `Selected` | The element is the selected one |
| `Inactive` | Another series is focused, so the element is dimmed (G2's `inactive` state) |

| `ElementStateStyles` member | Type | Value used when unset |
|-----------------------------|------|-----------------------|
| `ActiveFill` | Color? | Brighten the data fill by `ActiveBrighten` |
| `ActiveBrighten` | float? | Theme `HoverBrighten` |
| `SelectedStroke` | Color? | Theme `SelectionColor` |
| `SelectedStrokeWidth` | float? | Theme `SelectionStrokeWidth` |
| `InactiveOpacity` | float? | Theme `UnfocusedOpacity` |

Nothing has to be configured: declaring no state at all reproduces the default look. The base helpers
`ResolveFill` and `ComputeElementOpacity` apply the callback and then the state, which is why the same
two properties work for every built-in mark.

---

## All Mark Subclasses

The 20 subclasses that back a `ChartKind`. The annotation mark `SectionMark` - reference lines and a
band, backing no kind - is documented with the `ChartView` `Section*` exports it is configured through.

### IntervalMark

| Property | Type | Default |
|----------|------|---------|
| `BarPadding` | float | 0.2 |
| `CornerRadius` | float | 3 |
| `ShowLabel` | bool | false |
| `ShowValue` | bool | false |
| `Stack` | StackMode | None |
| `Orientation` | BarOrientation | Vertical |
| `GroupedBars` | bool | false |

**Data:** one row = one bar (one stacked segment under `Stack`): `X` category and `Y` value, plus optional
`Color` (series key - required for stacking) and `Opacity` (no fixed field of its own). With `GroupedBars`
the series of one category stop overlapping and each takes a sub-band of the same category (`Stack` has to
stay `None`; `ChartView.GroupedBars` is the same switch in the inspector). A numeric category column is
auto-fitted as a linear scale and nothing is drawn. See [Chart Types](chart-types.md#bar-chart--intervalmark).

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

**Data:** one row = one vertex; every row of a `Color` group joins into one line. `X` and `Y` are required
(any scale) and `Color` is optional. Points are connected in row order - nothing is sorted - and a series
with fewer than two valid points is not drawn. See [Chart Types](chart-types.md#line-chart--linemark).

### PointMark

| Property | Type | Default |
|----------|------|---------|
| `DefaultRadius` | float | 5 |
| `MinRadius` | float? | null |
| `RadiusRange` | float? | null |

**Data:** one row = one point (a bubble once `Size` is encoded on the chart, where it maps linearly onto a
3-23 px radius). `X` / `Y` are required; `Color`, `Opacity` and `Shape` (the point symbol - a circle when
the row has no shape value) are optional, and a row without the `Size` field falls back to
`DefaultRadius`. `MinRadius` / `RadiusRange` override the bottom of the size range and the pixels added on
top of it for this mark only (null uses `ChartTheme.PointSizeMin` / `ChartTheme.PointSizeRange`).
See [Chart Types](chart-types.md#scatter--bubble--pointmark).

### PieMark

| Property | Type | Default |
|----------|------|---------|
| `InnerRadius` | float | 0 |
| `StartAngle` | float | -π/2 |
| `ShowLabel` | bool | true |
| `LabelDistance` | float | 1.15 |
| `ExplodeRatio` | float? | null | Hover explode offset ratio relative to the outer radius; null falls back to `ChartTheme.PieExplodeRatio` (0.03) |
| `RadiusFactor` | float | 0.85 |
| `CenterText` | string? | null |
| `CenterFontSize` | float | 18 |
| `CenterSubFontSize` | float | 12 |
| `CenterContentBuilder` | Func<LabelContext, IReadOnlyList<TooltipLine>>? | null |
| `SliceLabelBuilder` | Func<LabelContext, string?>? | null |

**Data:** one row = one slice: `Y` value is required (`X` is the label) and rows with a non-finite value
are dropped. The whole chart is skipped when the total is not positive; a slice that is 100 % of the
total is still drawn (its sweep is kept a hair below a full turn), and a negative row has no slice - it is
skipped with a warning instead of sweeping backwards. `SliceLabelBuilder` builds a slice's label text
(returning null falls back to `LabelFormat`) and `CenterContentBuilder` draws rich content in a donut's
centre (it wins over `CenterText`). See [Chart Types](chart-types.md#pie--donut--piemark).

### RadarMark

| Property | Type | Default |
|----------|------|---------|
| `FillOpacity` | float | 0.15 |
| `StrokeWidth` | float | 2 |
| `PointRadius` | float | 3 |
| `GridRings` | int | 5 |
| `ShowAxisLabels` | bool | true |
| `ShowGrid` | bool | true |
| `RadiusFactor` | float | 0.85 |

**Data:** one row = one vertex (a series' value on one dimension): `X` dimension, `Y` numeric radius on a
linear scale, optional `Color` series. The dimension list is the union of all `X` values, so every series
must use the same names; fewer than three dimensions draws nothing. See [Chart Types](chart-types.md#radar--radarmark).

### CandlestickMark

| Property | Type | Default |
|----------|------|---------|
| `OpenField` | string | "open" |
| `HighField` | string | "high" |
| `LowField` | string | "low" |
| `CloseField` | string | "close" |
| `BodyWidthRatio` | float | 0.6 |
| `WickWidth` | float | 1.5 |
| `CornerRadius` | float | 3 |
| `BullishColor` | Color | — |
| `BearishColor` | Color | — |
| `FillBullish` | bool | true |

**Data:** one row = one period: `X` period plus the four OHLC fields read by name - `OpenField`, `HighField`,
`LowField` and `CloseField` (defaults `"open"` `"high"` `"low"` `"close"`). A row missing any of the four is
skipped; `Y` only feeds the auto-fitted scale. The optional `Color` channel replaces the up/down colour of
the body and the wick; without it the bull/bear colours (or `BullishColor` / `BearishColor`) are used.
See [Chart Types](chart-types.md#candlestick--candlestickmark).

### BoxMark

| Property | Type | Default |
|----------|------|---------|
| `MinField` | string | "min" |
| `Q1Field` | string | "q1" |
| `MedianField` | string | "median" |
| `Q3Field` | string | "q3" |
| `MaxField` | string | "max" |
| `BoxWidthRatio` | float | 0.5 |
| `WhiskerWidth` | float | 1.5 |
| `CornerRadius` | float | 3 |
| `BoxColor` | Color? | null |
| `LineColor` | Color? | null |

**Data:** one row = one pre-computed five-number summary: `X` category plus `MinField`, `Q1Field`,
`MedianField`, `Q3Field` and `MaxField` (defaults `"min"` `"q1"` `"median"` `"q3"` `"max"`). A row missing a
value, or holding a non-finite one, disappears. See [Chart Types](chart-types.md#box-plot--boxmark).

### HeatmapMark

| Property | Type | Default |
|----------|------|---------|
| `CellGap` | float | 1 |
| `CornerRadius` | float | 3 |
| `ShowLabel` | bool | false |
| `MaxCells` | int | 65536 |

**Data:** one row = one cell: `X` column and `Y` row, both on an ordinal scale, plus the numeric `Color`
value that the mark clamps onto a sequential scale. Rows follow the Y axis, which runs bottom-up: the
first category of the Y domain sits at the bottom, next to its label. Duplicate `(X, Y)` pairs are painted
in order - the last one wins, they are not summed - and a non-finite value is neither painted nor
labelled (the cell size is clamped to >= 0). Past `MaxCells` (65536) the remaining rows are not painted
and the mark warns once. See [Chart Types](chart-types.md#heatmap--heatmapmark).

### GaugeMark

| Property | Type | Default |
|----------|------|---------|
| `ArcWidth` | float | 0.12 |
| `StartAngleDeg` | float | -210 |
| `EndAngleDeg` | float | 30 |
| `ShowCenterLabel` | bool | true |
| `ShowMinMaxLabels` | bool | true |
| `ValueColor` | Color? | — |
| `TrackColor` | Color? | — |
| `InnerRadiusRatio` | float | 0 |
| `RadiusFactor` | float | 0.85 |

**Data:** the whole chart is drawn from the **first row only** - extra rows are ignored. `Y` is the needle
value on a linear scale and `X` is unused; set the scale range explicitly, otherwise the auto-fitted
domain makes almost any value look full. `ValueColor` and `TrackColor` are optional: leaving either at
`null` (the default) takes the theme's `DefaultMarkColor` / `GaugeTrackColor`, like every other mark.
See [Chart Types](chart-types.md#gauge--gaugemark).

### FunnelMark

| Property | Type | Default |
|----------|------|---------|
| `StageGap` | float | 4 |
| `MinWidthRatio` | float | 0.15 |
| `CornerRadius` | float | 3 |
| `ShowLabel` | bool | true |

**Data:** one row = one stage, drawn top-to-bottom in **row order** (nothing is sorted): `Y` value is
required, `X` label and `Color` are optional. Width is interpolated (`MinWidthRatio` is the floor) and the
stage height is split evenly across rows. See [Chart Types](chart-types.md#funnel--funnelmark).

### ViolinMark

| Property | Type | Default |
|----------|------|---------|
| `BinCount` | int | 20 |
| `WidthRatio` | float | 0.7 |
| `FillOpacity` | float | 0.5 |
| `ShowMedian` | bool | true |
| `ShowBox` | bool | true |
| `StrokeWidth` | float | 2 |

**Data:** one row = one sample; rows sharing an `X` category pool into one violin and `Color` only tints
that group (it never splits it). A group with a single row, or whose values are all equal, has no density
outline: it is drawn as a minimal visible line at its value and reported with a warning. Otherwise the
outline is a Gaussian kernel density estimate sampled at `BinCount` points, and the median is drawn as a
dot (`ChartTheme.ViolinMedianDotColor` / `ChartTheme.ViolinMedianDotRadius`).
See [Chart Types](chart-types.md#violin--violinmark).

### TreemapMark

`LayoutMode` is a `TreemapLayoutMode`: `BinarySplit` (the default: fast, no aspect-ratio optimisation) or
`Squarify` (packs rows along the shorter side, which keeps cells closer to square).

| Property | Type | Default |
|----------|------|---------|
| `CellGap` | float | 2 |
| `CornerRadius` | float | 3 |
| `LayoutMode` | `TreemapLayoutMode` | `BinarySplit` |
| `ParentField` | string | `"parent"` |
| `GroupHeaderHeight` | float | 16 |
| `SiblingShadeStep` | float | 0.12 |
| `ShowLabel` | bool | true |
| `MaxNodes` | int | 65536 |

`ParentField` links rows into a tree: a group owns a rectangle, its children split what the header
strip (`GroupHeaderHeight`) leaves. Rows whose parent is unknown, self-referencing or part of a cycle
are promoted to the top level.

**Data:** one row = one node rectangle: `X` label, `Y` value (clamped to >= 0) and an optional parent key
read by name - `ParentField`, default `"parent"`. A group whose own value is not positive takes the sum
of its children, and rows in a parent cycle are promoted to the top level. Past `MaxNodes` (65536) the
remaining rows are not painted and the mark warns once.
See [Chart Types](chart-types.md#treemap--treemapmark).

### SunburstMark

| Property | Type | Default |
|----------|------|---------|
| `ParentField` | string | "parent" |
| `RadiusFactor` | float | 0.9 |
| `InnerRadiusRatio` | float | 0.15 |
| `RingGap` | float | 2 |
| `ArcGap` | float | 0.02 |
| `DepthShadeStep` | float | 0.18 |
| `ShowLabel` | bool | true |

**Data:** one row = one ring segment: `X` label, `Y` value and an optional parent label read by name -
`ParentField`, default `"parent"`. A group without a positive value of its own takes the sum of its
children; rows naming the same label under one parent overwrite each other. See [Chart Types](chart-types.md#sunburst--sunburstmark).

### SankeyMark

| Property | Type | Default |
|----------|------|---------|
| `SourceField` | string | "source" |
| `TargetField` | string | "target" |
| `NodeWidth` | float | 16 |
| `ColumnGap` | float | 0.3 |
| `NodeGap` | float | 8 |
| `FlowOpacity` | float | 0.35 |
| `ShowLabel` | bool | true |
| `MaxNodes` | int | 65536 |

`ColumnGap` is a column packing ratio in [0, 1]: 0 spreads the columns evenly across the full plot width and 1 packs them edge to edge against the last column, which always stays flush with the right edge.

**Data:** one row = one flow: `source` and `target` are read by name (`SourceField` / `TargetField`,
defaults `"source"` / `"target"`) and `Y` is the weight, 1 when the field is missing. Non-positive flows
are dropped - a zero-traffic branch cannot be drawn - and self loops are dropped silently. Past `MaxNodes`
(65536) the remaining rows are not painted and the mark warns once.
See [Chart Types](chart-types.md#sankey--sankeymark).

### ChordMark

| Property | Type | Default |
|----------|------|---------|
| `SourceField` | string | "source" |
| `TargetField` | string | "target" |
| `ArcWidthRatio` | float | 0.06 |
| `ArcGap` | float | 0.04 |
| `ChordOpacity` | float | 0.4 |
| `RadiusFactor` | float | 0.85 |
| `ShowLabel` | bool | true |

**Data:** one row = one chord: `source` and `target` are read by name (`SourceField` / `TargetField`,
defaults `"source"` / `"target"`) and `Y` is the weight, 1 when the field is missing. A node's angle covers
the sum of its chords, and self loops are not filtered (they count twice). See [Chart Types](chart-types.md#chord--chordmark).

### RangeAreaMark

| Property | Type | Default |
|----------|------|---------|
| `LowerField` | string | "lower" |
| `FillOpacity` | float | 0.3 |
| `ShowBorderLines` | bool | true |
| `StrokeWidth` | float | 2 |
| `Smooth` | bool | false |

**Data:** one row = one segment of the band: `X` position, `Y` upper bound (a linear scale is required) and
`lower` read by name - `LowerField`, default `"lower"`. The mark draws **one** band, so `Color` only tints it
(from the first row); `Opacity` is ignored here - use `FillOpacity`. See [Chart Types](chart-types.md#range-area--rangeareamark).

### TimelineMark

| Property | Type | Default |
|----------|------|---------|
| `StartField` | string | "start" |
| `EndField` | string | "end" |
| `BarHeightRatio` | float | 0.6 |
| `CornerRadius` | float | 3 |
| `ShowLabel` | bool | false |

**Data:** one row = one interval: the ordinal channel (`Y`, or `X` with an ordinal scale) is the category,
the other one carries the range on a numeric scale, and `start` / `end` are read by name - `StartField` /
`EndField`, defaults `"start"` / `"end"`; a row with `end < start` is drawn with its endpoints swapped
and reported once with a warning, and a zero-length interval (`end == start`) draws no bar.
See [Chart Types](chart-types.md#timeline--timelinemark).

### LollipopMark

| Property | Type | Default |
|----------|------|---------|
| `DotRadius` | float | 5 |
| `StemWidth` | float | 2 |
| `Orientation` | BarOrientation | Vertical |

**Data:** one row = one stem plus its dot: `X` category (an ordinal scale) and `Y` value are both required -
even in the orientation that uses only one of them - plus optional `Color` / `Opacity` (no grouping) and
`Shape` for the dot. See [Chart Types](chart-types.md#lollipop--lollipopmark).

### MilestoneMark

| Property | Type | Default |
|----------|------|---------|
| `LabelField` | string | `"label"` |
| `MarkerRadius` | float | 6 |
| `MarkerShape` | `ShapeKind` | `Circle` |
| `ShowAxisLine` | bool | true |
| `AxisLineWidth` | float | 1 |
| `LabelOffset` | float | 8 |
| `AlternateLabels` | bool | true |
| `ShowLabel` | bool | true |

**Data:** one row = one event: `X` is the position on the axis (numeric, or a `TimeScale` for real dates)
and optional `Y` is the lane (`OrdinalScale` - the first category at the bottom, like `TimelineMark`).
`Color` and `Shape` are optional marker styling; the label is the `Label` channel first, then
`LabelField` (default `"label"`), then the X value. No value channel is bound, so an event needs no value
axis; without lanes every event sits on the middle line and each lane draws a single line. A row whose
position is missing or non-finite is skipped. See [Chart Types](chart-types.md#milestones--milestonemark).

### WaffleMark

| Property | Type | Default |
|----------|------|---------|
| `TotalCells` | int | 100 |
| `Columns` | int | 10 |
| `CellGap` | float | 2 |
| `CornerRadius` | float | 3 |

**Data:** one row = one category, not one cell: `Y` is its weight (rows with a value <= 0 or no value are
dropped) and the `TotalCells` cells (100 by default) are shared out by the largest-remainder method -
`X` only feeds the tooltip and `Color` the cells (without a colour every cell uses the mark's default
colour, so the split is invisible). The mark declares itself axis-free (`UsesAxes`), so a waffle-only chart draws no axes and no
grid. See [Chart Types](chart-types.md#waffle--wafflemark).

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
    public bool IncludeZero { get; init; }  // default: true
    public NiceTickResult NiceTicks { get; }        // ticks of the current domain
    public void SetDomain(double min, double max);  // pin the domain (skips the automatic fit)
}
```

### OrdinalScale

```csharp
public class OrdinalScale : OrdinalScaleBase      // the shared base owns Domain and IndexOf
{
    public IReadOnlyList<string> Domain { get; }
    public int IndexOf(string key);   // -1 when the key is not in the domain
}
```

### LogScale

```csharp
public class LogScale : IScale
{
    public LogScale();                        // domain 1..100
    public LogScale(double min, double max);
    public double Min { get; }
    public double Max { get; }
}
```

### ColorScale

Consumers always test the **interfaces**, never a concrete class: `IColorScale` has
`Color MapColor(object value)` (raw value → colour) and `ICategoricalColorScale : IColorScale` adds
`IReadOnlyList<string> Domain` - only a scale that implements the categorical one can drive a legend,
because the legend needs the ordered category keys. Every built-in colour scale below implements one of
them, and implementing them is all a custom colour scale has to do.

```csharp
public class ColorScale : OrdinalScaleBase, ICategoricalColorScale
{
    public IReadOnlyList<string> Domain { get; }
    public Color[] Palette { get; set; }
    public Color MapColor(object value);   // category → palette colour
}
```

### IdentityColorScale

G2's `identity` colour scale: the value **is** the colour. A `Color` value is taken as it is and an HTML
hexadecimal string (`"#rrggbb"`, also `#rgb` / `#rgba` / `#rrggbbaa`) is parsed; any other value falls
back to the palette, in order of first appearance. It is not a categorical scale, so it never draws a
legend. `Chart` picks it automatically when every value of the colour channel is a colour - otherwise the
categorical `ColorScale` is used, exactly as before.

```csharp
public class IdentityColorScale : IColorScale
{
    public Color[] Palette { get; set; }   // fallback for values that are not colours
    public Color MapColor(object value);
}
```

### SequentialColorScale

```csharp
public class SequentialColorScale : IColorScale
{
    public double Min { get; }
    public double Max { get; }
    public Color[] Gradient { get; set; }
    public Color MapColor(object value);
}
```

`Fit` skips a null, a non-numeric and a non-finite value - they count as *missing*, not as zero, so one
dirty field cannot drag a gradient (or a date axis) to 0. A diverging scale whose domain collapses (for
example all-zero data) maps every value to its neutral `MidColor`.

### DivergingColorScale

```csharp
public class DivergingColorScale : IColorScale
{
    public DivergingColorScale();
    public DivergingColorScale(double min, double max);
    public double Min { get; }
    public double Max { get; }
    public double MidPoint { get; init; }      // default: 0
    public bool Symmetric { get; init; }       // default: true (domain mirrored around MidPoint)
    public Color NegativeColor { get; }  // blue
    public Color MidColor { get; }       // near-white
    public Color PositiveColor { get; }  // red
    public Color MapColor(object value);
}
```

### BandScale

```csharp
public class BandScale : OrdinalScaleBase
{
    public IReadOnlyList<string> Domain { get; }
    public int SubBandCount { get; set; }       // default: 1
    public float Padding { get; init; }          // default: 0.2
    public float InnerPadding { get; init; }     // default: 0.1
    public double BandWidth { get; }
    public double SubBandWidth { get; }
    public double MapSubBand(object value, int subIndex);
}
```

### RadialScale

```csharp
public class RadialScale : OrdinalScaleBase;
```

Reserved for custom marks that map a value onto a radius; no built-in mark consumes it (the polar marks
compute their own geometry).

### TimeScale

```csharp
public class TimeScale : IScale
{
    public DateTime Min { get; }
    public DateTime Max { get; }
    public bool Clamp { get; init; }  // default: true
}
```

### ShapeScale

```csharp
public class ShapeScale : OrdinalScaleBase, ICategoricalShapeScale
{
    public IReadOnlyList<string> Domain { get; }
    public ShapeKind[] Shapes { get; set; }          // default: ShapeScale.DefaultShapes
    public static ShapeKind[] DefaultShapes { get; } // Circle, Square, Triangle, Diamond, Cross, Star
    public ShapeKind MapShape(object? value);
}
```

Symbols of the `Channel.Shape` channel; auto-fitted for that channel. Categories take the vocabulary in
first-seen order and the range cycles when there are more categories than shapes - set `Shapes` to use
your own list. A missing value or an unknown category maps to the first shape of the vocabulary
(`Circle`). Marks and the legend consume the channel through `IShapeScale`
(and `ICategoricalShapeScale`, which adds `Domain`, for the legend).

```csharp
public enum ShapeKind
{
    Circle,     // filled disc (the default)
    Square,     // axis-aligned square
    Triangle,   // triangle pointing up
    Diamond,    // rotated square
    Cross,      // plus sign
    Star,       // five-pointed star
}
```

`ShapeGeometry.Build(path, shape, cx, cy, radius)` draws one symbol of this vocabulary onto a path, so
custom marks paint the same glyphs as the built-in ones. `ShapeGeometry.AddRingBand(…)` adds the closed ring
band the round marks fill (outer arc, inner arc reversed, close), and `ShapeGeometry.AngleAt(index, count)` is
the vertex angle the symbols and the radar's rings and series share: the first vertex points up, the rest run
clockwise.

---

### OutputRangeScale

```csharp
public class OutputRangeScale : IScale
{
    public OutputRangeScale(IScale inner, double min, double max);
    public IScale Inner { get; }
    public double OutputMin { get; }
    public double OutputMax { get; }
}
```

A decorator that remaps the inner scale's normalized `0...1` answer onto `min...max` - what the size and the
opacity channels use to turn a mapped value into pixels or alpha. `min` and `max` are swapped when given in
reverse, a non-finite answer from the inner scale stays `NaN` (putting it at the bottom of the range would
make it look like real data), and `Fit` is delegated to the inner scale, so the decorator never owns a domain
of its own.

## AxisConfig

```csharp
public class AxisConfig
{
    // Tooltip strings - all three are joined under the pointer (one per line)
    public string? Title { get; init; }
    public string? Description { get; init; }
    public string? Unit { get; init; }

    // Tick control - null keeps the automatic behaviour
    public double? TickStep { get; init; }        // step in data units; a value the data has not got snaps to one it has
    public double[]? Ticks { get; init; }         // explicit values to label
    public int? TickCount { get; init; }          // aim for this many labels
    public string? LabelFormat { get; init; }     // numeric axes only; null keeps the scale's own text
    public float? LabelRotation { get; init; }    // only the X axis draws rotated labels
    public float? TickLabelSpacing { get; init; } // pixels between two labels of this axis

    // Domain control
    public float? AutoScaleMargin { get; init; }  // margin kept while the data stays inside it
    public bool NiceDomain { get; init; }         // round the fitted domain out to a nice step
    public double? MinLimit { get; init; }        // pin one end; null keeps both following the data
    public double? MaxLimit { get; init; }
}
```

The tick and domain members mirror the `ChartView` exports of the same name (`TickStep` / `TickCount` / `LabelFormat` / `LabelRotation` /
`AutoScaleMargin` / `NiceDomain` / `MinLimit` / `MaxLimit`); they exist for charts built by hand, where there is no
inspector to set them in.

All three strings end up in the axis tooltip: hovering an axis shows the `Title`, then `(Unit)`, then the
`Description`, one per line - the axis hit test joins whatever is set and leaves a null or empty entry out,
so an axis with only a title shows just that title.

---

## LegendConfig

```csharp
public class LegendConfig
{
    public LegendPosition Position { get; init; }  // default: Top
    public float ItemSpacing { get; set; }        // default: 16
    public float SwatchSize { get; init; }         // default: 10
    public float Padding { get; init; }            // default: 6
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
    After,   // Step at the new X: a horizontal run first, then a vertical segment there
    Before,  // Step at the old X: a vertical segment first, then a horizontal run
    Center,  // Step at the midpoint X
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
    public string Field { get; init; }    // default: "value"
    public int? BinCount { get; init; }   // Sturges' rule if null
    public double? BinWidth { get; init; } // priority over BinCount
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
    public float ExitDuration { get; init; }      // default: 0.3
    public int ElementCount { get; set; }        // default: -1 (set once, then ShouldAnimate() reads it)

    // Methods
    public bool ShouldAnimate(int elementCount = -1);
    public void StartEntry(Node owner, int seriesCount, int totalElementCount = -1);
    public void AnimateHover(Node owner, float targetScale);
    public void StartDataTransition(Node owner, float duration = 0.4f);
    public void StartExit(Node owner, Action? onComplete = null);
    public void Reset();
    public void Dispose();
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
    public float[] SeriesProgress { get; init; }        // per-series entry progress (empty = no stagger)

    public static AnimationContext Default { get; }
}
```

`EntryProgress` is `init`, not `set`: an `AnimationContext` is fixed once it is built, so pass every value
through an object initializer - a later `ctx.EntryProgress = 0.5f` no longer compiles. `Default` is a
shared, immutable all-animations-off instance; do not write into it.

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
    public IReadOnlyList<TooltipLine>? TooltipLines { get; set; }
}
```

`FocusedSeries` and `TooltipLines` are set by the interaction layer after the hit: the focused series key
(a legend click) and the rich tooltip content of the hit mark, which wins over `Label` when it is present.

### ChartClickEventArgs

```csharp
public class ChartClickEventArgs : EventArgs
{
    public DataRow? Row { get; init; }
    public int RowIndex { get; init; }
    public string? MarkType { get; init; }
    public Vector2 ScreenPosition { get; init; }
    public string? SeriesKey { get; init; }
    public MouseButton Button { get; init; } = MouseButton.Left;
}
```

### ChartInteraction (Static Utility)

```csharp
public static class ChartInteraction
{
    public static void DrawCrosshair(ICanvas2D canvas, Vector2 mousePos, PlotArea plot, ChartTheme? theme = null);
    public static HitResult? TestAll(List<Mark> marks, MarkContext ctx, Vector2 mousePos,
                                     IReadOnlySet<Mark>? skipped = null);
}
```

---

## Tooltip

### TooltipRenderer

```csharp
public class TooltipRenderer
{
    public float SmoothSpeed { get; }   // default: 12
    public float FadeSpeed { get; }     // default: 8
    public bool IsVisible { get; }
    public TooltipOptions Options { get; }
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
    public float? BorderWidth { get; set; }      // null = theme TooltipBorderWidth (1)
    public float? CornerRadius { get; set; }     // null = theme TooltipCornerRadius (6)
    public float? Padding { get; set; }          // null = theme TooltipPadding (8)
    public float? FontSize { get; set; }         // null = theme TooltipFontSize (12)
}
```

Every `null` falls back to the theme: `BackgroundColor`, `TextColor` and `BorderColor` to
`TooltipBackground` / `TooltipTextColor` / `TooltipBorderColor`, the four metrics above to
`TooltipBorderWidth` / `TooltipCornerRadius` / `TooltipPadding` / `TooltipFontSize`.

### TooltipLine & TooltipSpan

```csharp
public struct TooltipLine
{
    public TooltipSpan[] Spans { get; init; }
    public static TooltipLine Plain(string text);
    public static TooltipLine WithIcon(TooltipIcon icon, Color color, string text);
}

public readonly struct TooltipSpan
{
    public string Text { get; init; }
    public Color? Color { get; init; }
    public bool Bold { get; init; }
    public bool Italic { get; init; }
    public float? FontSize { get; init; }
    public TextDecoration Decoration { get; init; }
    public float LetterSpacing { get; init; }         // ignored by the Skia backend (drawing and measurement)
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
    public static ChartTheme Default { get; }   // the shared fallback; treat it as read-only (Clone it to edit)
    public ChartTheme Clone();

    // Static palettes (read-only by copy: every read returns a fresh array,
    // so writing into the result changes nothing)
    public static Color[] DefaultPalette { get; }
    public static Color[] DefaultSequentialGradient { get; }

    // 107 [Export] properties organized in 25 groups:
    // Color Palette, Chart Frame, Layout, Typography,
    // Mark Defaults, Selection & Hover, Polar / Segment,
    // Tooltip, Crosshair, Legend, Line Mark, Point Mark,
    // Radar Mark, Box Mark, Violin Mark, Gauge Mark,
    // Sankey Mark, Candlestick Mark, Feature Toggles,
    // Lollipop Mark, Range Area Mark, Chord Mark,
    // Sunburst Mark, Treemap Mark, Hit Test
}
```

See [Customization & Theming](customization.md#theme-property-groups) for a grouping overview of the
theme properties - the Inspector shows them all, grouped by these names.

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
    IImageHandle CaptureRegion(int x, int y, int width, int height);   // read a region of the surface back

    void Save();
    void Restore();
    IDisposable SaveScope();
    void Translate(float x, float y);
    void Scale(float sx, float sy);
    void Rotate(float angle);
    void ClipRect(float x, float y, float w, float h);

    void Tick();
    TextMetrics MeasureText(string text, FontSettings font);
    Texture2D? Texture { get; }          // surface the canvas renders into (null if it has none)
    CanvasCapabilities Capabilities { get; }
}
```

`Texture` is what makes a canvas usable anywhere: the canvas itself never touches the scene tree.
Present the surface with a `Sprite2D`, a `TextureRect` or any `CanvasItem`, or let
`Canvas2DControl` host it.

`LoadImage(int, int, byte[])` rejects a `null` buffer with `ArgumentNullException`, non-positive
dimensions with `ArgumentOutOfRangeException` and a buffer too short for them with `ArgumentException` -
it never silently truncates. A buffer that is *longer* than needed is fine: only the first
`width * height * 4` bytes are read. The pixels are taken as RGBA8 with **premultiplied** alpha, unlike
`LoadImage(Texture2D)`, which converts the texture to RGBA8 with straight (unpremultiplied) alpha.
`SaveScope()`'s `Dispose` is idempotent, so a double dispose is harmless.

`CaptureRegion` copies a region of the surface into an image handle, which is what a caller that keeps a
rendered layer and hands it back to `DrawImage` later needs (`Chart.UseLayerCache` is that caller). The pixels
are the ones the surface holds at the moment of the call, the rectangle is clamped to the surface (a handle
smaller than requested means "clipped", so the caller has to treat it as no usable capture), and the handle
belongs to the caller, who disposes it. Check `CanvasCapabilities.SupportsSurfaceCapture` before calling: the
`Canvas2DBase` default throws `NotSupportedException`, like `LoadImage`.

### CanvasBackendType (Enum)

```csharp
public enum CanvasBackendType
{
    Skia,    // SkiaSharp backend (the implementation that is shipped)
    Godot,   // Godot's native vector API (reserved, not implemented)
    Vello,   // GPU compute backend (reserved, long term)
    Auto,    // pick the best available backend (currently always Skia)
}
```

`Canvas2DFactory.Create` and `Canvas2DControl.Backend` both take one of these. Only `Skia` is implemented:
a request for `Godot`, `Vello` or `Auto` is served by Skia, and the two reserved backends log a warning so
the fallback is visible instead of silent.

### Canvas2DBase

```csharp
public abstract class Canvas2DBase : ICanvas2D
{
    // A backend implements only these; the rest is a shared convenience default.
    public abstract void Resize(int width, int height);
    public abstract void BeginFrame();
    public abstract void EndFrame();
    public abstract void Clear(Color color);
    public abstract IPath2D CreatePath();
    public abstract IPaint2D CreatePaint();
    public abstract void Stroke(IPath2D path, IPaint2D paint);
    public abstract void Fill(IPath2D path, IPaint2D paint);
    public abstract TextMetrics MeasureText(string text, FontSettings font);
    public abstract CanvasCapabilities Capabilities { get; }

    protected Transform2D CurrentTransform { get; set; }   // kept by Save / Restore

    // False when the backend transforms natively: the base then skips its own transform stack.
    protected virtual bool MirrorsTransformsInBase => true;
}
```

A custom backend derives from `Canvas2DBase` and fills in those members; the base class adds
`DrawLine` / `DrawRect` / `DrawCircle` / `DrawImage`, the save-restore stack, the transform helpers and the
clipping entry points on top of them. `Tick()`, `DrawImage` and `Texture` are virtual with no-op / null
defaults, so a minimal backend only has to implement the abstract list. `DrawText` is the exception: its
default throws `NotImplementedException` in DEBUG builds and logs a single `GD.PushError` in Release builds,
while `LoadImage` and `CaptureRegion` throw `NotSupportedException` until the backend overrides them (and
reports the matching capability flag).

### Drawing Primitives

The small value types the drawing surface speaks:

```csharp
[Flags] public enum TextDecoration { None = 0, Underline = 1, Strikethrough = 2 }

public enum LineCap  { Butt, Round, Square }
public enum LineJoin { Miter, Round, Bevel }

public readonly struct GradientStop
{
    public float Position { get; }   // [0, 1]
    public Color Color { get; }
    public GradientStop(float pos, Color color);
}

public interface IImageHandle : IDisposable
{
    int Width { get; }
    int Height { get; }
}

public readonly record struct TextMetrics(float Width, float Height);

// CanvasCapabilities: what the backend can do
public record CanvasCapabilities(
    bool SupportsGradients,
    bool SupportsClipping,
    bool SupportsTransforms,
    bool IsGpuBacked,
    bool SupportsLineDash,
    bool SupportsImages = false,
    bool SupportsSurfaceCapture = false);
```

`CanvasCapabilities` (returned by `ICanvas2D.Capabilities`) lets a renderer branch on what the backend can
do - gradients, clipping, affine transforms, line dash, raster images, GPU, surface readback - instead of
assuming Skia; `LoadImage` hands back an `IImageHandle` (dispose it with the frame) and `MeasureText` a
`TextMetrics`. `SupportsSurfaceCapture` is what a layered renderer checks before it keeps a layer
(see [Layered rendering](advanced.md#layered-rendering)); the Skia backend reports `true`, the
`Canvas2DBase` default of `CaptureRegion` throws `NotSupportedException`.

### DefaultRenderers

```csharp
public static class DefaultRenderers
{
    public static void DrawBackground(RenderContext ctx);
    public static void DrawTitle(RenderContext ctx);
    public static void DrawGrid(RenderContext ctx);
    public static void DrawAxes(RenderContext ctx);
    public static void DrawAxisLabels(RenderContext ctx);
    public static void DrawLegend(RenderContext ctx);
    public static void DrawCrosshair(RenderContext ctx);
}
```

These are the functions the chart installs in its renderer slots by default. Assign one to a slot to keep
the built-in look, or wrap it in a lambda that calls it and then draws something of its own.

### Canvas2DFactory / Canvas2DControl

```csharp
// A canvas of a given pixel size; it renders into ICanvas2D.Texture.
public static ICanvas2D Create(int width, int height,
                               CanvasBackendType backend = CanvasBackendType.Auto);

// A Control that hosts a canvas: creates it, follows the node size, runs the frame loop
// (tick -> optional clear -> draw -> commit) and presents the texture in _Draw.
public partial class Canvas2DControl : Control
{
    public CanvasBackendType Backend { get; set; }      // default Auto
    public bool AutoResize { get; set; }                // default true: surface == node size
    public bool ClearBeforeDraw { get; set; }           // default true
    public Color BackgroundColor { get; set; }          // used by ClearBeforeDraw
    public bool StretchToNodeSize { get; set; }         // default true
    public bool OwnsCanvas { get; set; }                // default true: dispose with the node
    public bool LayeredRendering { get; set; }          // default false: the component may cache a layer
    public Func<int, int, ICanvas2D>? CanvasFactory { get; set; } // custom / injected canvas

    public ICanvas2D? Canvas { get; }
    public Texture2D? Texture { get; }
    public Vector2I CanvasSize { get; }
    public bool IsReady { get; }

    public event Action<Canvas2DControl, ICanvas2D>? CanvasDraw;  // draw callback
    public void Invalidate();                                     // redraw next frame
    public void ResizeCanvas(Vector2I size);                      // fixed resolution
    protected virtual void OnCanvasDraw(ICanvas2D canvas);        // or override
}
```

The Skia backend is one-shot about its surface: `SkiaCanvas2DBackend.Initialize` throws if it is called a
second time or after the canvas has been disposed - use `Resize` to change the size of a live canvas - and
`SkiaTexture` throws `ObjectDisposedException` while uninitialised or after disposal, so read the nullable
`ICanvas2D.Texture` when you need "maybe no surface yet" semantics.

`CanvasFactory` is read when the node enters the tree, so set it before adding the node - assigning it
later only takes effect when the node re-enters. A drawing callback that throws does not break the node:
`EndFrame` still runs in a `finally`, the error is reported, and the frame stays dirty so the next
`_Process` retries it.

`ResizeCanvas` clamps both dimensions to at least 1, and with `AutoResize = false` the first surface is
still built at the node size - only a later `ResizeCanvas` call moves it to another resolution.

`LayeredRendering` is an advisory switch for the component drawn on the canvas: it says that the component
may keep the part of its frame that does not depend on the pointer in an image (`ChartView.LayeredRendering`
mirrors its own value here). The control does not change how it draws; it checks the precondition such a
cache has - the surface has to be cleared before every redraw (`ClearBeforeDraw`, on by default) - and reports
once when it is not.

`Canvas2DFactory.Create` throws `InvalidOperationException` when the engine has no rendering device (a
`--headless` run, or `--rendering-driver dummy`); `Canvas2DControl` catches that, logs a warning and keeps
the node usable so the host can draw a placeholder.

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

`ArcTo` reduces angles modulo a full turn, so a sweep of exactly `2π` collapses to an empty arc - pass
`2π - ε` when a complete circle is intended (or use `Circle`).

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

`CreatePath()` and `CreatePaint()` hand out pooled objects: a returned instance is recycled rather than
fresh, so dispose every one of them and never keep them across frames - a recycled object goes back to the
pool and is handed to another caller. `IPaint2D` state is sticky, so set every property you rely on instead
of assuming a fresh default.

### FontSettings

```csharp
public readonly record struct FontSettings
{
    public float Size { get; init; }                  // default: 13
    public string? Family { get; init; }
    public bool Bold { get; init; }
    public bool Italic { get; init; }
    public float LetterSpacing { get; init; }         // ignored by the Skia backend (drawing and measurement)
    public float LineHeightMultiplier { get; init; }  // default: 1.2
    public TextAlign Align { get; init; }
    public TextDecoration Decoration { get; init; }
    public Font? GodotFont { get; init; }

    public static FontSettings Default { get; }
}

public enum TextAlign { Left, Center, Right }
```
