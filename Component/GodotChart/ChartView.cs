using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Godot;
using GodotNodeExtension.Component.GodotChart.Canvas;
using GodotNodeExtension.Component.GodotChart.Marks;

namespace GodotNodeExtension.Component.GodotChart;

/// <summary>
/// Chart type a <see cref="ChartView"/> builds: the kind is always explicit, nothing is guessed from the
/// data.
/// </summary>
public enum ChartKind
{
    // The numbering starts at 1 on purpose: the enum used to open with a removed "Auto" member, and the
    // Kind numbers already stored in scenes would otherwise shift by one.
    /// <summary>Vertical bars (one bar per row).</summary>
    Bar = 1,

    /// <summary>Polyline through the rows.</summary>
    Line = 2,

    /// <summary>Polyline with a filled area below it.</summary>
    Area = 3,

    /// <summary>Scatter plot (one dot per row).</summary>
    Scatter = 4,

    /// <summary>Band between a value and a lower bound.</summary>
    RangeArea = 5,

    /// <summary>Pie chart.</summary>
    Pie = 6,

    /// <summary>Donut chart (pie with an inner radius).</summary>
    Donut = 7,

    /// <summary>Radar / spider chart (one axis per category, one polygon per series).</summary>
    Radar = 8,

    /// <summary>Distribution of values per category.</summary>
    Violin = 9,

    /// <summary>Box-and-whisker plot.</summary>
    Box = 10,

    /// <summary>OHLC candles.</summary>
    Candlestick = 11,

    /// <summary>Value matrix (two category axes plus a colour value).</summary>
    Heatmap = 12,

    /// <summary>Squarified / binary-split area chart.</summary>
    Treemap = 13,

    /// <summary>Hierarchy rings.</summary>
    Sunburst = 14,

    /// <summary>Flow diagram between nodes.</summary>
    Sankey = 15,

    /// <summary>Relationship chords between nodes.</summary>
    Chord = 16,

    /// <summary>Single-value gauge.</summary>
    Gauge = 17,

    /// <summary>Funnel of decreasing stages.</summary>
    Funnel = 18,

    /// <summary>Waffle grid of counted cells.</summary>
    Waffle = 19,

    /// <summary>Timeline bars between a start and an end value.</summary>
    Timeline = 20,

    /// <summary>Stem plus dot per row.</summary>
    Lollipop = 21,

    /// <summary>Events on a time (or numeric) axis, one marker plus label each.</summary>
    Milestone = 22,

    /// <summary>Geographic areas shaded by a value: one region (or one grid cell) per row.</summary>
    GeoArea = 23,

    /// <summary>Geographic bubbles at the coordinate each row carries, sized by its value.</summary>
    GeoBubble = 24,
}

/// <summary>
/// Which pointer gestures zoom and pan a <see cref="ChartView"/>, as a flags enum so the axes can be
/// combined (mirrors the vocabulary LiveCharts2 users know, without the flags we do not need).
/// <para>
/// The wheel zooms, the <see cref="ChartView.PanButton"/> drags, and a double click resets - every gesture
/// works on the axes the flags name. <see cref="ChartView.ZoomMode"/> defaults to <see cref="None"/>, so a
/// page that does not ask for it keeps the plain hover/click behaviour (including letting the wheel scroll
/// the surrounding container).
/// </para>
/// </summary>
[Flags]
public enum ChartZoomMode
{
    /// <summary>No zoom, no pan: the wheel keeps scrolling the host.</summary>
    None = 0,

    /// <summary>Wheel zoom on the X axis.</summary>
    ZoomX = 1 << 0,

    /// <summary>Wheel zoom on the Y axis.</summary>
    ZoomY = 1 << 1,

    /// <summary>Drag pan on the X axis.</summary>
    PanX = 1 << 2,

    /// <summary>Drag pan on the Y axis.</summary>
    PanY = 1 << 3,

    /// <summary>Zoom and pan on the X axis (the common case for a time series).</summary>
    X = ZoomX | PanX,

    /// <summary>Zoom and pan on the Y axis.</summary>
    Y = ZoomY | PanY,

    /// <summary>Zoom and pan on both axes.</summary>
    Both = X | Y,
}
/// <summary>Which axis a <see cref="SectionMark"/>'s levels belong to, in inspector-readable form.</summary>
public enum ChartSectionTarget
{
    /// <summary>Levels are Y values: horizontal lines.</summary>
    Y,

    /// <summary>Levels are the right axis' values: horizontal lines against Y2.</summary>
    Y2,

    /// <summary>Levels are X values: vertical lines.</summary>
    X,
}

/// <summary>Built-in palette a <see cref="ChartView"/> uses.</summary>
public enum ChartThemeKind
{
    /// <summary>Dark background palette.</summary>
    Dark,

    /// <summary>Light background palette.</summary>
    Light,
}

/// <summary>
/// How the colour channel turns values into colours - the mapping side of the channel, next to the field
/// that feeds it (<see cref="ChartView.ColorField"/>). Mirrors G2's colour scale types.
/// </summary>
public enum ColorMappingKind
{
    /// <summary>
    /// Default: values that are colours (<c>Color</c> values, <c>"#rrggbb"</c> strings) are used as they
    /// are, anything else is a category and takes a palette colour.
    /// </summary>
    Auto,

    /// <summary>Always categorical: one palette colour per distinct value, and a legend.</summary>
    Category,

    /// <summary>Always the value itself as the colour (no legend: the values are not categories).</summary>
    Identity,

    /// <summary>Continuous: a numeric value is mapped onto the theme's sequential gradient.</summary>
    Sequential,

    /// <summary>Continuous around zero: negative values towards one pole, positive towards the other.</summary>
    Diverging,
}

/// <summary>
/// One node that shows a chart - the quick path. Set <see cref="Kind"/>, feed it rows, and it does the
/// rest: it creates the canvas, maps the channels, infers the scales, lays the chart out, keeps the
/// surface at the node size, draws the legend and the hover tooltip, and redraws when something
/// changes. Like a <see cref="TextureRect"/>, sizing and presentation are handled for you.
/// <para>
/// Rows carry typed values: <see cref="Rows"/> is an inspector-editable array of dictionaries whose
/// values are Godot variants (<c>int</c>, <c>float</c>, <c>string</c>, <c>bool</c>), and
/// <see cref="SetData(IEnumerable{DataRow})"/> takes <see cref="DataRow"/> instances from code. Either
/// way a value keeps its type - nothing is round-tripped through text.
/// </para>
/// <para>
/// Channels are configured by field name: <see cref="XField"/>, <see cref="YField"/>,
/// <see cref="ColorField"/>, <see cref="SizeField"/>, <see cref="OpacityField"/> and
/// <see cref="ShapeField"/>. Empty means "use the default for this kind";
/// kinds with extra data read
/// their conventional fields (<c>lower</c>, <c>min/q1/median/q3/max</c>, <c>open/high/low/close</c>,
/// <c>start/end</c>, <c>source/target</c>, <c>parent</c>, <c>label</c>, <c>lane</c>); Heatmap uses the X
/// field as its column, the Y field as its row and the colour field as the cell value. Use
/// <see cref="ConfigureMark"/> for anything else.
/// </para>
/// <para>
/// The chart has a legend (<see cref="Legend"/>) and, by default, a hover tooltip plus crosshair
/// (<see cref="ShowTooltip"/>). For custom drawing, extra marks or full control, drop down to the
/// pieces this node is built from: <see cref="Chart"/> / <see cref="Canvas"/> here, or
/// <see cref="Canvas2DControl"/> + <see cref="Chart"/> directly.
/// </para>
/// </summary>
[GlobalClass]
[Tool]
public partial class ChartView : Control
{
    // ── Chart ───────────────────────────────────────────────────────────────

    private ChartKind _kind = ChartKind.Bar;
    private string _title = "";
    private ChartThemeKind _themeKind = ChartThemeKind.Dark;
    private ChartTheme? _customTheme;
    private ColorMappingKind _colorMapping = ColorMappingKind.Auto;
    private Vector2 _xAxisRange;
    private Vector2 _yAxisRange;
    private Vector2 _sizeRange;
    private Vector2 _opacityRange;
    private Godot.Collections.Array<ShapeKind> _shapeSymbols = [];
    private bool _editorPreview = true;
    private bool _groupedBars;
    private StackMode _stack = StackMode.None;

    // ── Data ────────────────────────────────────────────────────────────────

    private Godot.Collections.Array<Godot.Collections.Dictionary> _rows = [];

    /// <summary>
    /// Keep at most this many rows while streaming: appending past the limit drops the oldest rows, so
    /// the data (and the memory) stays bounded - the oscilloscope pattern. 0 (default) keeps every row.
    /// </summary>
    private int _windowSize;

    // ── Channels ────────────────────────────────────────────────────────────

    private string _xField = "";
    private string _yField = "";
    private string _colorField = "";
    private string _sizeField = "";
    private string _opacityField = "";
    private string _shapeField = "";

    // ── Axes / metrics ──────────────────────────────────────────────────────

    private string _xAxisTitle = "";
    private string _xAxisUnit = "";
    private string _yAxisTitle = "";
    private string _yAxisUnit = "";
    private LegendPosition _legend = LegendPosition.Bottom;

    // ── Interaction ─────────────────────────────────────────────────────────

    private bool _showTooltip = true;
    private bool _showCrosshair = true;
    private ChartZoomMode _zoomMode = ChartZoomMode.None;
    private float _zoomFactor = 1.2f;
    // Left by default: a chart that asks for pan (ZoomMode != None) is dragged with the left button, which is
    // the gesture a reader tries first. With ZoomMode.None no drag is consumed at all, so nothing else changes.
    private MouseButton _panButton = MouseButton.Left;
    private bool _resetZoomOnDoubleClick = true;
    private bool _panning;

    /// <summary>Chart type to draw. Every kind is explicit - the node never guesses it from the rows.</summary>
    [ExportGroup("Chart")]
    [Export] public ChartKind Kind
    {
        get => _kind;
        set { if (_kind == value) return; _kind = value; Invalidate(); }
    }

    /// <summary>Chart title. Empty draws no title.</summary>
    [Export] public string Title
    {
        get => _title;
        set { if (_title == value) return; _title = value; Invalidate(); }
    }

    /// <summary>Built-in palette to use. Ignored while <see cref="CustomTheme"/> is set.</summary>
    [Export] public ChartThemeKind ThemeKind
    {
        get => _themeKind;
        set { if (_themeKind == value) return; _themeKind = value; Invalidate(); }
    }

    /// <summary>
    /// Theme resource for this chart: assign a <see cref="ChartTheme"/> - created in the editor
    /// (<i>New Resource…</i>, saved as a <c>.tres</c>) or loaded from code - to edit every colour, line
    /// width, font, font size, corner radius and the palette in the Inspector. The chart follows the
    /// edits: the resource's <see cref="Resource.Changed"/> signal triggers a rebuild, so the editor
    /// preview and the running game stay in step.
    /// <para>
    /// Null uses the built-in palette selected by <see cref="ThemeKind"/>. The node only reads the
    /// resource and never writes to it, so one <c>.tres</c> can back any number of views - typography
    /// included: <see cref="ChartTheme.Font"/> and <see cref="ChartTheme.FontFamily"/> are what the chart
    /// text uses.
    /// </para>
    /// </summary>
    [Export] public ChartTheme? CustomTheme
    {
        get => _customTheme;
        set
        {
            if (ReferenceEquals(_customTheme, value)) return;
            _customTheme = value;
            Invalidate();
        }
    }

    /// <summary>
    /// Draw the chart inside the editor as well (default). This node is a tool script, so the preview
    /// follows the exports live. Turn it off for scenes with many views, or when the editor should not
    /// allocate GPU surfaces.
    /// </summary>
    [Export] public bool EditorPreview
    {
        get => _editorPreview;
        set { if (_editorPreview == value) return; _editorPreview = value; Invalidate(); }
    }

    /// <summary>
    /// Cap on the number of rows kept (<see cref="AddRow"/> drops the oldest ones past it). 0 keeps
    /// everything.
    /// </summary>
    [Export] public int WindowSize
    {
        get => _windowSize;
        set
        {
            if (_windowSize == value) return;
            _windowSize = Math.Max(0, value);
            TrimWindow();
            PushData();
        }
    }

    /// <summary>
    /// How the colour channel maps values to colours - the mapping side of <see cref="ColorField"/>.
    /// <see cref="ColorMappingKind.Auto"/> (the default) infers it from the values: a colour field of
    /// <see cref="Color"/> values or <c>"#rrggbb"</c> strings is used as it is, anything else is a category
    /// and takes a palette colour. The other members force one mapping:
    /// <see cref="ColorMappingKind.Identity"/> for a mixed column whose colours should still win,
    /// <see cref="ColorMappingKind.Sequential"/> or <see cref="ColorMappingKind.Diverging"/> to colour a
    /// numeric column continuously (gradient from the theme). For a scale the node cannot express, use
    /// <see cref="ConfigureChart"/>.
    /// </summary>
    [ExportGroup("Scales")]
    [Export] public ColorMappingKind ColorMapping
    {
        get => _colorMapping;
        set { if (_colorMapping == value) return; _colorMapping = value; Invalidate(); }
    }

    /// <summary>
    /// Pinned X (category / value) domain as <c>(min, max)</c>. <c>(0, 0)</c> - the default - lets the data
    /// fit the axis. Only a numeric axis is pinned; a category axis keeps its categories, so setting a range
    /// never breaks one.
    /// </summary>
    [Export] public Vector2 XAxisRange
    {
        get => _xAxisRange;
        set { if (_xAxisRange.IsEqualApprox(value)) return; _xAxisRange = value; Invalidate(); }
    }

    /// <summary>
    /// Pinned domain of the left value axis as <c>(min, max)</c>; <c>(0, 0)</c> fits the data. Use it to
    /// keep several charts comparable, or to stop one outlier from squashing everything else. Only a
    /// numeric axis is pinned (see <see cref="XAxisRange"/>).
    /// </summary>
    [Export] public Vector2 YAxisRange
    {
        get => _yAxisRange;
        set { if (_yAxisRange.IsEqualApprox(value)) return; _yAxisRange = value; Invalidate(); }
    }

    /// <summary>
    /// Radius range of the <see cref="SizeField"/> channel as <c>(min, max)</c> in pixels.
    /// <c>(0, 0)</c> - the default - uses the theme's <see cref="ChartTheme.PointSizeMin"/> and
    /// <see cref="ChartTheme.PointSizeRange"/>, so the smallest value draws the smallest dot.
    /// </summary>
    [Export] public Vector2 SizeRange
    {
        get => _sizeRange;
        set { if (_sizeRange.IsEqualApprox(value)) return; _sizeRange = value; Invalidate(); }
    }

    /// <summary>
    /// Opacity range of the <see cref="OpacityField"/> channel as <c>(min, max)</c>.
    /// <c>(0, 0)</c> - the default - maps the column onto the full <c>0…1</c>, so the largest value is
    /// opaque and the smallest is invisible. Set it to keep the faintest element visible, e.g. `(0.35, 1)`.
    /// </summary>
    [Export] public Vector2 OpacityRange
    {
        get => _opacityRange;
        set { if (_opacityRange.IsEqualApprox(value)) return; _opacityRange = value; Invalidate(); }
    }

    /// <summary>
    /// Symbols the <see cref="ShapeField"/> channel cycles through, in order of first appearance. Empty
    /// (the default) uses <see cref="ShapeScale.DefaultShapes"/> (circle, square, triangle, diamond, cross,
    /// star). It drives the point symbols, the lollipop dots and the legend swatches.
    /// </summary>
    [Export] public Godot.Collections.Array<ShapeKind> ShapeSymbols
    {
        get => _shapeSymbols;
        set { _shapeSymbols = value is { } symbols ? symbols : []; Invalidate(); }
    }

    /// <summary>
    /// Rows as typed key/value dictionaries - the inspector-editable form of the data. Values keep
    /// their variant type (<c>int</c>, <c>float</c>, <c>string</c>, <c>bool</c>).
    /// <para>
    /// Assigning it replaces the rows shown by the view; <see cref="SetData(IEnumerable{DataRow})"/> does
    /// the same from code and clears this array so the two views never disagree.
    /// </para>
    /// </summary>
    [ExportGroup("Data")]
    [Export] public Godot.Collections.Array<Godot.Collections.Dictionary> Rows
    {
        get => _rows;
        set
        {
            // The inspector (or a script) can hand over nothing at all; every other member here assumes
            // a live array, so an empty one takes its place instead of failing on the next read.
            _rows = value is { } rows ? rows : [];
            LoadVariantRows();
            // The model now *is* this array, so record its signature: without this the next editor frame
            // compared the two, called the difference an inspector edit and converted every row again.
            SyncRowsSignature();
        }
    }

    /// <summary>Field mapped to the X channel (category / label). Empty uses the kind default.</summary>
    [ExportGroup("Channels")]
    [Export] public string XField
    {
        get => _xField;
        set { if (_xField == value) return; _xField = value; Invalidate(); }
    }

    /// <summary>Field mapped to the Y channel (value). Empty uses the kind default.</summary>
    [Export] public string YField
    {
        get => _yField;
        set { if (_yField == value) return; _yField = value; Invalidate(); }
    }

    /// <summary>
    /// Field mapped to the colour channel; it also drives the legend. Empty means the kind default
    /// (<c>series</c> when the rows carry it, otherwise the category for Pie / Donut / Funnel / Waffle; see
    /// the documentation). Two special values are worth knowing:
    /// <list type="bullet">
    /// <item>a field whose values are colours (<see cref="Color"/> values or <c>"#rrggbb"</c> strings) is
    /// used as they are, so the data decides the colour;</item>
    /// <item><c>constant:#ff8800</c> paints the whole chart in that one colour.</item>
    /// </list>
    /// A colour name such as <c>"red"</c> is <b>not</b> parsed as a colour: it is a category like any
    /// other label and takes a palette colour (use a <see cref="Color"/> or a <c>#rrggbb</c> string, or
    /// <see cref="ConfigureMark"/> for a fixed colour).
    /// </summary>
    [Export] public string ColorField
    {
        get => _colorField;
        set { if (_colorField == value) return; _colorField = value; Invalidate(); }
    }

    /// <summary>Field mapped to the size channel (bubble charts). Empty disables it.</summary>
    [Export] public string SizeField
    {
        get => _sizeField;
        set { if (_sizeField == value) return; _sizeField = value; Invalidate(); }
    }

    /// <summary>Field mapped to the opacity channel. Empty disables it.</summary>
    [Export] public string OpacityField
    {
        get => _opacityField;
        set { if (_opacityField == value) return; _opacityField = value; Invalidate(); }
    }

    /// <summary>
    /// Field mapped to the shape channel: one symbol per category (points, lollipop dots and the
    /// legend swatches). Empty means every element keeps the default symbol.
    /// </summary>
    [Export] public string ShapeField
    {
        get => _shapeField;
        set { if (_shapeField == value) return; _shapeField = value; Invalidate(); }
    }

    /// <summary>Title drawn under the X axis. Empty draws none.</summary>
    [ExportGroup("Axes")]
    [Export] public string XAxisTitle
    {
        get => _xAxisTitle;
        set { if (_xAxisTitle == value) return; _xAxisTitle = value; Invalidate(); }
    }

    /// <summary>Unit appended to the X axis ticks. Empty draws none.</summary>
    [Export] public string XAxisUnit
    {
        get => _xAxisUnit;
        set { if (_xAxisUnit == value) return; _xAxisUnit = value; Invalidate(); }
    }

    /// <summary>Title drawn beside the Y axis. Empty draws none.</summary>
    [Export] public string YAxisTitle
    {
        get => _yAxisTitle;
        set { if (_yAxisTitle == value) return; _yAxisTitle = value; Invalidate(); }
    }

    /// <summary>Unit appended to the Y axis ticks. Empty draws none.</summary>
    [Export] public string YAxisUnit
    {
        get => _yAxisUnit;
        set { if (_yAxisUnit == value) return; _yAxisUnit = value; Invalidate(); }
    }

    /// <summary>Where to draw the legend. The legend appears when the colour channel resolves series.</summary>
    [Export] public LegendPosition Legend
    {
        get => _legend;
        set { if (_legend == value) return; _legend = value; Invalidate(); }
    }

    /// <summary>
    /// Lay the series of a category side by side instead of on top of each other (bar kinds only; the
    /// series is the colour field's value). Off is the historical layout: every series keeps the whole
    /// band, so which bar you see depends on the draw order. Ignored while the chart is stacked, since
    /// stacking has no sub-bands.
    /// </summary>
    [ExportGroup("Layout")]
    [Export] public bool GroupedBars
    {
        get => _groupedBars;
        set { if (_groupedBars == value) return; _groupedBars = value; Invalidate(); }
    }

    /// <summary>
    /// Stack the series of a category on top of each other instead of overlaying them (the series is the
    /// colour field's value). <see cref="StackMode.Stack"/> piles the values, <see cref="StackMode.Normalize"/>
    /// scales every category to 1. Set on the node's kind by default (<see cref="StackMode.None"/>), and it
    /// wins over <see cref="GroupedBars"/>, which then has no sub-bands to lay out.
    /// <para>
    /// Bars and areas are the kinds that read it: the stacked-area form of a line is this switch plus
    /// <see cref="ChartKind.Area"/>.
    /// </para>
    /// </summary>
    [Export] public StackMode Stack
    {
        get => _stack;
        set { if (_stack == value) return; _stack = value; Invalidate(); }
    }

    /// <summary>Draw the hover tooltip (default true).</summary>
    [ExportGroup("Interaction")]
    [Export] public bool ShowTooltip
    {
        get => _showTooltip;
        set { if (_showTooltip == value) return; _showTooltip = value; Invalidate(); }
    }

    /// <summary>Draw the crosshair that follows the pointer (default true).</summary>
    [Export] public bool ShowCrosshair
    {
        get => _showCrosshair;
        set { if (_showCrosshair == value) return; _showCrosshair = value; Invalidate(); }
    }

    /// <summary>
    /// Which pointer gestures zoom and pan the chart (default <see cref="ChartZoomMode.None"/>). See
    /// <see cref="ChartZoomMode"/>: the wheel zooms around the pointer, <see cref="PanButton"/> (left by
    /// default) drags, and a double click resets unless <see cref="ResetZoomOnDoubleClick"/> is off.
    /// <para>
    /// The wheel is only consumed while one of the <c>Zoom</c> flags is set: with <see cref="ChartZoomMode.None"/>
    /// the event stays unhandled, which is what lets a <c>ScrollContainer</c> scroll a page of charts.
    /// </para>
    /// </summary>
    [ExportGroup("Interaction")]
    [Export] public ChartZoomMode ZoomMode
    {
        get => _zoomMode;
        set { if (_zoomMode == value) return; _zoomMode = value; Invalidate(); }
    }

    /// <summary>How much one wheel notch narrows the window (default 1.2 = 20% closer per notch).</summary>
    [Export] public float ZoomFactor
    {
        get => _zoomFactor;
        set { if (Mathf.IsEqualApprox(_zoomFactor, value)) return; _zoomFactor = Mathf.Max(1.01f, value); }
    }

    /// <summary>
    /// Button that drags the view to pan (default <see cref="MouseButton.Middle"/>, so the left button keeps
    /// selecting). Point it at the left button on a page where selection is not used - that button then no
    /// longer selects, because a gesture cannot be both.
    /// </summary>
    [Export] public MouseButton PanButton
    {
        get => _panButton;
        set { if (_panButton == value) return; _panButton = value; Invalidate(); }
    }

    /// <summary>
    /// Display-level reduction of a line/area series (<see cref="LineMark.Decimate"/>): with
    /// <see cref="DecimateMode.Auto"/> the default, a table with far more rows than the plot has columns is
    /// drawn from per-column extremes instead of every point, which keeps a big data page interactive.
    /// <see cref="ConfigureMark"/> still has the last word - it can set the mark's own value.
    /// </summary>
    [Export] public DecimateMode Decimate
    {
        get => _decimate;
        set { if (_decimate == value) return; _decimate = value; Invalidate(); }
    }

    private DecimateMode _decimate = DecimateMode.Auto;

    private bool _ignoreContentMinimumSize;

    /// <summary>
    /// Ignore the chart's content minimum size (title, legend, axis labels and titles) and let the node be
    /// squeezed: a page that draws into a small card on purpose turns this on instead of fighting
    /// <see cref="Chart.MinimumSize"/>.
    /// </summary>
    [Export] public bool IgnoreContentMinimumSize
    {
        get => _ignoreContentMinimumSize;
        set
        {
            if (_ignoreContentMinimumSize == value) return;
            _ignoreContentMinimumSize = value;
            // The container queries the minimum size; without this the node keeps the old one.
            UpdateMinimumSize();
            Invalidate();
        }
    }

    /// <summary>Reset the view when the chart is double clicked (default true; only while zoom is enabled).</summary>
    [Export] public bool ResetZoomOnDoubleClick
    {
        get => _resetZoomOnDoubleClick;
        // No invalidation: the switch is read by the next gesture, so nothing on screen changes with it.
        set => _resetZoomOnDoubleClick = value;
    }

    /// <summary>
    /// Shape the chart's content is given, as width divided by height (<c>1</c> = square). <c>0</c> - the
    /// default - leaves the decision to the marks: a pie, radar, gauge, chord or sunburst needs a square and
    /// gets one, while any mark that fills the plot area keeps the whole of it. A negative value forces the
    /// content to fill the plot area, a positive one is the ratio to use. See
    /// <see cref="Chart.PlotAspectRatio"/>.
    /// </summary>
    [Export] public float PlotAspectRatio
    {
        get => _plotAspectRatio;
        set { if (Mathf.IsEqualApprox(_plotAspectRatio, value)) return; _plotAspectRatio = value; Invalidate(); }
    }

    /// <summary>Where the content box sits inside the plot area while <see cref="PlotAspectRatio"/> shapes it.</summary>
    [Export] public HorizontalAlignment PlotAlignHorizontal
    {
        get => _plotAlignHorizontal;
        set { if (_plotAlignHorizontal == value) return; _plotAlignHorizontal = value; Invalidate(); }
    }

    /// <summary>Where the content box sits inside the plot area while <see cref="PlotAspectRatio"/> shapes it.</summary>
    [Export] public VerticalAlignment PlotAlignVertical
    {
        get => _plotAlignVertical;
        set { if (_plotAlignVertical == value) return; _plotAlignVertical = value; Invalidate(); }
    }

    private float _plotAspectRatio;
    private HorizontalAlignment _plotAlignHorizontal = HorizontalAlignment.Center;
    private VerticalAlignment _plotAlignVertical = VerticalAlignment.Center;

    private bool _layeredRendering;

    /// <summary>
    /// Keep the chart's non-interactive layer - background, grid, axes, legend and the data - in an image and
    /// repaint only the overlay (the hover/selection highlight, the crosshair, the tooltip) while the pointer
    /// moves. Default <c>false</c>: with it off the node draws exactly the historical single pass.
    /// <para>
    /// This is what makes a hover cheap on a large table: a pointer move no longer costs the point mapping
    /// plus the path of the whole series. It reaches the chart as <see cref="Chart.UseLayerCache"/>, where the
    /// cost (one extra image of the chart rectangle), the invalidation rules and the fall-back are documented
    /// - including the fact that it only engages when every mark of the chart paints its interaction state on
    /// the overlay and the canvas backend can capture its surface.
    /// </para>
    /// </summary>
    [ExportGroup("Performance")]
    [Export] public bool LayeredRendering
    {
        get => _layeredRendering;
        set { if (_layeredRendering == value) return; _layeredRendering = value; Invalidate(); }
    }

    /// <summary>
    /// Optional canvas factory override - a custom backend, a shared canvas, or a headless canvas in
    /// tests. Set it before the node enters the tree.
    /// </summary>
    public Func<int, int, ICanvas2D>? CanvasFactory { get; set; }

    /// <summary>
    /// Configure the chart instance itself, after the node applied its channels, scales and axes - the
    /// escape hatch for anything the exports do not cover (a hand-built scale, a second mark, a pinned
    /// axis domain). It runs on every rebuild, like <see cref="ConfigureMark"/> does for the mark.
    /// <para>
    /// This is also where a per-node background override belongs, since the node has no background export
    /// of its own: <c>view.ConfigureChart = c =&gt; c.BackgroundColor = Colors.Transparent;</c> makes this
    /// chart see-through, and the theme's <see cref="ChartTheme.BackgroundColor"/> with a zero alpha does
    /// the same for every chart using that resource. (The surface itself is always cleared transparent -
    /// the background is what the background renderer paints.)
    /// </para>
    /// </summary>
    public Action<Chart>? ConfigureChart { get; set; }

    /// <summary>The chart this node draws. Rebuilt when the kind, channels, data or styling change.</summary>
    public Chart? Chart { get; private set; }

    /// <summary>
    /// The drawing surface the chart renders into, as the backend-agnostic canvas (null before the node
    /// enters the tree). This is the one to use for own drawing in <see cref="ConfigureChart"/> time or for
    /// handing the surface to something else that draws.
    /// </summary>
    public ICanvas2D? Canvas => _canvas?.Canvas;

    /// <summary>
    /// The texture <see cref="Canvas"/> renders into - a convenience alias of <c>Canvas.Texture</c> for a
    /// host that only wants to present it (a <c>TextureRect</c>, a <c>Sprite2D</c>, an exporter).
    /// </summary>
    public Texture2D? Texture => _canvas?.Texture;

    /// <summary>
    /// The node that hosts the canvas: it creates it, keeps it at the node size, runs the frame loop and
    /// presents the texture. Exposed for advanced use - it is an internal child of this node, created and
    /// owned here; <see cref="Canvas"/> and <see cref="Texture"/> are what most hosts need.
    /// </summary>
    public Canvas2DControl? Surface => _canvas;

    /// <summary>The tooltip renderer, exposed for styling (colours, fonts, custom content).</summary>
    public TooltipRenderer Tooltip => _tooltip;

    /// <summary>The rows currently shown.</summary>
    public IReadOnlyList<DataRow> DataRows => _rowsData;

    /// <summary>
    /// Ask for a full rebuild with the current settings (data, channels, size, theme). Use it after
    /// changing something the node cannot observe by itself.
    /// </summary>
    public void Refresh() => Invalidate();

    /// <summary>
    /// Redraw the current frame without rebuilding the chart. For state that lives on the chart
    /// instance - entry animation, hover, crosshair, custom per-frame tweaks through
    /// <see cref="Chart"/> - the rebuild that <see cref="Refresh"/> performs would throw that state
    /// away, so call this instead.
    /// </summary>
    public void Repaint() => _canvas?.Invalidate();

    private readonly List<DataRow> _rowsData = [];
    private readonly TooltipRenderer _tooltip = new();
    private Canvas2DControl? _canvas;
    private ChartTheme _theme = ChartTheme.Dark();

    private ChartTheme? _watchedTheme;
    private Action<Mark>? _configureMark;
    private bool _dirty = true;

    /// <summary>
    /// The minimum this node last reported to the engine. A container only re-sorts its children when a child
    /// tells it its minimum changed (<see cref="Control.UpdateMinimumSize"/>), so the value is kept to call it
    /// exactly when it moved - once, when the chart's measurement replaces the first-frame estimate.
    /// </summary>
    private Vector2 _reportedMinimum;

    /// <summary>Signature of the exported row array, so inspector edits of it are noticed.</summary>
    private string _rowsSignature = "";

    /// <summary>
    /// Size of the drawing surface the current chart was built for (see the per-frame comparison in
    /// <see cref="_Process"/>): the surface resizes the canvas when the node is resized, and nothing on this node
    /// is told about it.
    /// </summary>
    private Vector2I _builtSurfaceSize;

    /// <summary>Signature of the exported symbol array, for the same reason.</summary>
    private string _shapeSymbolsSignature = "";
    private HitResult? _lastHit;
    private Vector2 _mousePos;
    private bool _pointerInside;

    // ── Data entry ──────────────────────────────────────────────────────────

    /// <summary>Replace the rows shown by this view (typed <see cref="DataRow"/> values; the exported array is cleared).</summary>
    public ChartView SetData(IEnumerable<DataRow> rows)
    {
        _rowsData.Clear();
        _rowsData.AddRange(rows);
        _rows.Clear();          // the model and the inspector view stay in sync
        TrimWindow();
        SyncRowsSignature();
        PushData();
        return this;
    }

    /// <summary>Replace the rows shown by this view.</summary>
    public ChartView SetData(params DataRow[] rows) => SetData((IEnumerable<DataRow>)rows);

    /// <summary>
    /// Append one row (the streaming path). Rows past <see cref="WindowSize"/> drop the oldest ones.
    /// Only the data changes, so the chart instance - and with it the animation and hover state - stays.
    /// </summary>
    public ChartView AddRow(DataRow row)
    {
        _rowsData.Add(row);
        _rows.Add(ToDictionary(row));
        TrimWindow();
        SyncRowsSignature();
        PushData();
        return this;
    }

    /// <summary>Drop the oldest rows until the window fits.</summary>
    private void TrimWindow()
    {
        if (_windowSize <= 0 || _rowsData.Count <= _windowSize) return;

        _rowsData.RemoveRange(0, _rowsData.Count - _windowSize);
        var drop = _rows.Count - Math.Min(_rows.Count, _windowSize);
        for (int i = 0; i < drop; i++)
            _rows.RemoveAt(0);
        // The exported array changed as a side effect, so its recorded signature has to follow: without
        // this the editor check found "inspector rows differ from the model" on the next frame and
        // reloaded the very rows we just trimmed.
        SyncRowsSignature();
    }

    /// <summary>
    /// Data-only update: hand the rows to the existing chart. A rebuild would throw away the chart
    /// instance, which is what animation and hover live on, so it is reserved for structural changes.
    /// </summary>
    private void PushData()
    {
        if (Chart is null)
        {
            Invalidate();
            return;
        }

        Chart.Data(_rowsData);
        _canvas?.Invalidate();
    }

    /// <summary>
    /// Quick path for the common case: categories plus values, under the channel field names
    /// (<see cref="XField"/> / <see cref="YField"/> when set, otherwise <c>category</c> / <c>value</c>).
    /// </summary>
    public ChartView SetValues(IEnumerable<(string Category, double Value)> values)
    {
        var rows = new List<DataRow>();
        foreach (var (category, value) in values)
            rows.Add(new DataRow(2).Set(XFieldName(), category).Set(YFieldName(), value));
        return SetData(rows);
    }

    /// <summary>Replace the rows from CSV text (a convenience for code; the inspector uses typed rows).</summary>
    public ChartView SetCsv(string csv) => SetData(ParseCsv(csv));

    /// <summary>Drop the rows and clear the surface.</summary>
    public ChartView Clear()
    {
        _rowsData.Clear();
        _rows.Clear();
        SyncRowsSignature();
        PushData();
        return this;
    }

    /// <summary>
    /// Tweak the mark this view builds (extra fields, styling, ...). Applied on every rebuild.
    /// </summary>
    public ChartView ConfigureMark(Action<Mark> configure)
    {
        _configureMark = configure;
        Invalidate();
        return this;
    }

    /// <summary>Parse CSV data (header line + one row per line) into rows.</summary>
    public static List<DataRow> ParseCsv(string csv)
    {
        var rows = new List<DataRow>();
        string[]? header = null;

        foreach (var rawLine in (csv).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;

            var cells = line.Split(',');
            if (header is null)
            {
                header = new string[cells.Length];
                for (int i = 0; i < cells.Length; i++) header[i] = cells[i].Trim();
                continue;
            }

            var row = new DataRow(cells.Length);
            for (int i = 0; i < cells.Length; i++)
            {
                var field = i < header.Length ? header[i] : $"column{i}";
                var text = cells[i].Trim();
                row.Set(field, double.TryParse(text, NumberStyles.Float,
                                               CultureInfo.InvariantCulture, out var number)
                    ? number
                    : text);
            }
            rows.Add(row);
        }
        return rows;
    }

    // ── Lifecycle ───────────────────────────────────────────────────────────

    /// <inheritdoc />
    public override void _EnterTree()
    {
        // Pass, not Stop: the chart accepts the pointer events it handles (motion, press, release) and
        // lets everything else - the mouse wheel above all - bubble up, so a host ScrollContainer still
        // scrolls while the pointer sits on a chart. Set before the placeholder branch: it is a property
        // of the node, not of the preview.
        MouseFilter = MouseFilterEnum.Pass;

        // In the editor the node only builds a surface when the preview is enabled; without it a
        // placeholder names the kind and the row count instead (see _Draw).
        if (Engine.IsEditorHint() && !_editorPreview)
        {
            Invalidate();
            return;
        }

        _canvas ??= new Canvas2DControl { Name = "ChartSurface" };
        _canvas.AutoResize = true;
        _canvas.MouseFilter = MouseFilterEnum.Ignore;
        _canvas.CanvasFactory = CanvasFactory;
        // Mirrored onto the surface as well: the control is where the precondition of a cached layer (a
        // surface that is cleared every frame) is checked and reported.
        _canvas.LayeredRendering = _layeredRendering;
        _canvas.CanvasDraw += OnSurfaceDraw;

        if (_canvas.GetParent() != this)
        {
            // The host drives the surface rect explicitly (SyncSurfaceSize); anchors would fight that and
            // leave the presentation rect at zero until the next layout pass.
            //
            // Size the child *before* it enters the tree: Canvas2DControl builds its canvas during
            // _EnterTree from its own size, so adding it first made every view start with a 1x1 surface that
            // the first frame then resized - a second canvas, a second Skia surface and, as the integration
            // lifecycle case measures, a second shared GRContext per view.
            _canvas.Size = Size;
            // Internal child: it is an implementation detail, so keep it out of the editor's tree.
            AddChild(_canvas, false, InternalMode.Back);
        }

        if (_rowsData.Count == 0 && _rows.Count > 0)
            LoadVariantRows();

        _dirty = true;
    }

    /// <summary>
    /// Write what this canvas currently shows to a PNG: the export path a tool, a build script or a test can
    /// call, and the one to use for documentation shots.
    /// <para>
    /// It saves the surface the chart already draws into, so the pixels are exactly what the page shows - the
    /// chart is bound to its canvas at construction, so rendering the same chart into a <i>second</i> surface of
    /// another size would be a different chart. A headless run has no surface at all; the call reports false
    /// instead of throwing.
    /// </para>
    /// </summary>
    /// <param name="path">Destination, a Godot path (<c>res://…</c> or <c>user://…</c>).</param>
    /// <returns>True when the image was written.</returns>
    public bool SavePng(string path)
    {
        if (string.IsNullOrEmpty(path)) return false;

        var image = Texture?.GetImage();
        return image is not null && image.SavePng(path) == Error.Ok;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Before a frame has drawn there is no <see cref="Chart"/> yet (it needs a canvas, and the canvas needs a
    /// size), so the estimate is reported instead of zero: a container sizes this node from what it reports
    /// here, and a zero would collapse it to a pixel - the chart would then be built on a 1 px surface and had
    /// no way to grow back. <see cref="_Process"/> calls <see cref="Control.UpdateMinimumSize"/> as soon as the
    /// measured value replaces the estimate, which is what makes the container lay the node out again.
    /// </remarks>
    public override Vector2 _GetMinimumSize()
    {
        if (IgnoreContentMinimumSize) return base._GetMinimumSize();
        if (Chart is { } chart) return chart.MinimumSize;

        // The editor without preview, or a run without a rendering device: the node still knows what it is
        // configured to show, so the estimate stands in for the chart.
        var theme = EffectiveTheme;
        // No legend padding is handed over: the chart this node builds takes its legend config from the node
        // itself (see ApplyAxesAndLegend), and that config keeps LegendConfig's default padding.
        return Chart.EstimateMinimumSize(theme, !string.IsNullOrEmpty(_title),
            !string.IsNullOrEmpty(_xAxisTitle), !string.IsNullOrEmpty(_yAxisTitle), _legend,
            Chart.ThemedLabelFontSize(theme));
    }

    /// <summary>The theme this node draws with: the assigned resource, or the built-in one for its kind.</summary>
    private ChartTheme EffectiveTheme
        => _customTheme ?? (_themeKind == ChartThemeKind.Light ? ChartTheme.Light() : ChartTheme.Dark());

    /// <inheritdoc />
    public override void _ExitTree()
    {
        if (_canvas is not null)
            _canvas.CanvasDraw -= OnSurfaceDraw;

        // The surface disposes its canvas when it leaves the tree, so the chart built on it is stale.
        Chart = null;
        // The reported minimum belonged to that chart: a re-entry reports it again (see _Process).
        _reportedMinimum = Vector2.Zero;

        WatchTheme(null);
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        // The node size is authoritative: aligning here (instead of only relying on the surface's
        // own Resized signal) keeps the chart correct whatever the order of layout and process is.
        SyncSurfaceSize();

        // One detector per kind of edit, and no second one for the same change. Every scalar export invalidates
        // from its own setter (the node used to *also* compare a snapshot of all of them every frame, which could
        // only ever agree with the setter or fight it), the two exported arrays are compared in the editor, where
        // an inspector edit in place happens - and what is left here is the surface size: the canvas is resized
        // by the surface the node hosts, which no setter of this node sees, so a frame that finds another size
        // rebuilds the chart for it.
        if (_canvas is { } surface && surface.CanvasSize != _builtSurfaceSize) Invalidate();
        if (Engine.IsEditorHint()) CheckVariantRows();

        // Tooltip fade / follow needs a frame even when nothing else changed. The theme's
        // EnableTooltip is the master switch: while it is off the renderer is fed no hit, so a bubble
        // that is still fading out cannot follow the pointer or stay up.
        if (_showTooltip)
        {
            bool wasVisible = _tooltip.IsVisible;
            _tooltip.Update((float)delta, TooltipsEnabled ? _lastHit : null);
            if (wasVisible != _tooltip.IsVisible) Repaint();
        }

        if (_dirty) Rebuild();

        // The engine lays a container's children out from what _GetMinimumSize reports, and it only asks again
        // when the node says the value changed. The first layout happens before a chart exists (the estimate
        // answers it), so without this the measured minimum would never reach the container and the node would
        // stay at whatever the estimate reserved.
        Vector2 minimum = _GetMinimumSize();
        if (minimum.IsEqualApprox(_reportedMinimum)) return;

        _reportedMinimum = minimum;
        UpdateMinimumSize();
    }

    /// <inheritdoc />
    public override void _GuiInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseMotion motion:
                _pointerInside = true;
                _mousePos = motion.Position;

                // A drag with the pan button moves the window instead of hovering: the pointer is still over
                // some row, but the row under it is changing as the content slides.
                if (_panning && PanWith(motion.Position, motion.Relative)) break;

                UpdateHover();
                AcceptEvent();
                break;

            case InputEventMouseButton { Pressed: true, DoubleClick: true } doubleClick:
                if (!CanResetZoom || doubleClick.ButtonIndex != MouseButton.Left) break;

                Chart?.ResetZoom();
                Repaint();
                AcceptEvent();
                break;

            case InputEventMouseButton { Pressed: true } click:
                // The wheel arrives as a mouse button too. It is not a click: leave the event unhandled
                // (no AcceptEvent) so the host's ScrollContainer gets it and scrolls - unless this node zooms,
                // in which case the wheel is ours.
                if (IsWheel(click.ButtonIndex))
                {
                    if (!ZoomWith(click.ButtonIndex, click.Position)) break;
                    AcceptEvent();
                    break;
                }

                _mousePos = click.Position;

                // The pan button starts a drag; it never selects, because one press cannot be two gestures.
                if (CanPan && click.ButtonIndex == _panButton)
                {
                    _panning = true;
                    AcceptEvent();
                    break;
                }

                // Forward the button: the chart only changes its selection for the left one, and a host can
                // use the others for its own gestures (a context menu, drilling out of a hierarchy, ...).
                Chart?.HandleClick(click.Position, click.ButtonIndex);
                UpdateHover();
                AcceptEvent();
                break;

            case InputEventMouseButton { Pressed: false } release:
                if (!_panning || release.ButtonIndex != _panButton) break;

                _panning = false;
                UpdateHover();
                AcceptEvent();
                break;
        }
    }

    /// <summary>True for the four wheel "buttons", which scroll rather than select.</summary>
    private static bool IsWheel(MouseButton button)
        => button is MouseButton.WheelUp or MouseButton.WheelDown
            or MouseButton.WheelLeft or MouseButton.WheelRight;

    /// <summary>True when any zoom flag is set, so a wheel notch is a zoom rather than a scroll.</summary>
    private bool CanZoom => _zoomMode.HasFlag(ChartZoomMode.ZoomX) || _zoomMode.HasFlag(ChartZoomMode.ZoomY);

    /// <summary>True when any pan flag is set, so the pan button starts a drag.</summary>
    private bool CanPan => _zoomMode.HasFlag(ChartZoomMode.PanX) || _zoomMode.HasFlag(ChartZoomMode.PanY);

    private bool CanResetZoom => _zoomMode != ChartZoomMode.None && _resetZoomOnDoubleClick;

    /// <summary>
    /// One wheel notch: narrow (or widen) the window around the pointer, so the value under the cursor stays
    /// put. Returns false when this node does not zoom, which leaves the event to the host.
    /// </summary>
    private bool ZoomWith(MouseButton button, Vector2 position)
    {
        if (Chart is not { } chart || !CanZoom) return false;

        bool zoomIn = button == MouseButton.WheelUp;
        double factor = zoomIn ? 1.0 / Mathf.Max(1.01f, _zoomFactor) : Mathf.Max(1.01f, _zoomFactor);
        var (focusX, focusY) = FocusOf(position);

        bool changed = false;
        if (_zoomMode.HasFlag(ChartZoomMode.ZoomX)) changed |= chart.ZoomDomain(Channel.X, factor, focusX);
        if (_zoomMode.HasFlag(ChartZoomMode.ZoomY)) changed |= chart.ZoomDomain(Channel.Y, factor, focusY);
        if (!changed) return false;

        Repaint();
        return true;
    }

    /// <summary>
    /// One drag step: shift the window by the pointer delta, so the data follows the hand. Returns false when
    /// this node does not pan.
    /// </summary>
    private bool PanWith(Vector2 position, Vector2 relative)
    {
        if (Chart is not { } chart || !CanPan) return false;

        _mousePos = position;
        if (chart.CurrentPlotArea is not { } plot || plot.Width <= 0f || plot.Height <= 0f) return false;

        bool changed = false;
        if (_zoomMode.HasFlag(ChartZoomMode.PanX) && relative.X != 0f)
            changed |= chart.PanDomain(Channel.X, -relative.X / plot.Width);
        if (_zoomMode.HasFlag(ChartZoomMode.PanY) && relative.Y != 0f)
            changed |= chart.PanDomain(Channel.Y, relative.Y / plot.Height);

        if (changed) Repaint();
        return true;    // the drag is ours even when it hit a boundary: the event must not reach the host
    }

    /// <summary>
    /// Where inside the plot the pointer is, as the two normalized focuses a zoom keeps fixed. Y is flipped
    /// because the value axis grows upwards while the screen grows down.
    /// </summary>
    private (double X, double Y) FocusOf(Vector2 position)
    {
        if (Chart?.CurrentPlotArea is not { } plot || plot.Width <= 0f || plot.Height <= 0f) return (0.5, 0.5);

        return (
            Math.Clamp((position.X - plot.X) / plot.Width, 0f, 1f),
            Math.Clamp(1f - (position.Y - plot.Y) / plot.Height, 0f, 1f));
    }

    /// <inheritdoc />
    public override void _Notification(int what)
    {
        if (what != NotificationMouseExit) return;

        _pointerInside = false;
        _lastHit = null;
        Chart?.Interaction(null);
        // NotifyHoverChanged stores the row and raises OnHover when it changed - calling Hover(-1) first
        // would set the stored row to -1 and silence the event, so leaving the chart never reported
        // "no element is hovered any more" (the same trap UpdateHover() documents).
        Chart?.NotifyHoverChanged(-1, null);
        Repaint();
    }

    /// <inheritdoc />
    public override void _Draw()
    {
        if (Canvas is not null) return;   // the surface presents the chart itself

        const int fontSize = 14;
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.12f, 0.12f, 0.18f, 0.35f));
        DrawString(ThemeDB.FallbackFont, new Vector2(8f, fontSize + 6f),
                   $"ChartView · {_kind} · {_rowsData.Count} rows",
                   HorizontalAlignment.Left, -1f, fontSize, new Color(1f, 1f, 1f, 0.8f));
    }

    // ── Build ───────────────────────────────────────────────────────────────

    private void OnSurfaceDraw(Canvas2DControl control, ICanvas2D canvas)
    {
        Chart?.Render();
        if (TooltipsEnabled)
        {
            var size = control.CanvasSize;
            _tooltip.Draw(canvas, size.X, size.Y);
        }
    }

    /// <summary>
    /// Whether the hover tooltip is live: <see cref="ShowTooltip"/> for this node, and
    /// <see cref="ChartTheme.EnableTooltip"/> as the theme-level master switch - with either off, the
    /// tooltip is neither fed a hover hit nor drawn, so a chart-wide theme can turn it off for every
    /// view using it. The assigned resource is consulted before the rebuild applied it, so a theme
    /// swapped in this frame already counts.
    /// </summary>
    private bool TooltipsEnabled => _showTooltip && (_customTheme ?? _theme).EnableTooltip;

    private void SyncSurfaceSize()
    {
        if (_canvas is null) return;

        // The node size is authoritative both for the presented rect and for the drawing surface.
        if (!_canvas.Size.IsEqualApprox(Size))
            _canvas.Size = Size;

        var wanted = new Vector2I(Mathf.Max(1, (int)Size.X), Mathf.Max(1, (int)Size.Y));
        if (_canvas.CanvasSize != wanted)
        {
            _canvas.ResizeCanvas(wanted);
            Invalidate();
        }
    }

    private void Invalidate()
    {
        _dirty = true;
        QueueRedraw();   // keeps the placeholder / preview in step with the exports
    }

    /// <summary>
    /// Follow the theme resource itself. Editing a <c>.tres</c> in the Inspector keeps the same
    /// instance, so the snapshot comparison above cannot see it - the resource's
    /// <see cref="Resource.Changed"/> signal can, and it is what makes the preview follow the edits.
    /// <para>
    /// The chart built by <see cref="Rebuild"/> also subscribes to the theme (<see cref="Chart.Theme"/>) so
    /// that a hand-built chart drops its cached layout; this one invalidates and rebuilds the node's chart,
    /// which is the stronger reaction a live node needs. Both are wanted - see the note on
    /// <see cref="Chart.Theme"/>.
    /// </para>
    /// </summary>
    private void WatchTheme(ChartTheme? theme)
    {
        if (ReferenceEquals(_watchedTheme, theme)) return;

        if (_watchedTheme is not null)
            _watchedTheme.Changed -= OnThemeChanged;

        _watchedTheme = theme;

        if (_watchedTheme is not null)
            _watchedTheme.Changed += OnThemeChanged;
    }

    private void OnThemeChanged() => Invalidate();

    private void Rebuild()
    {
        _dirty = false;
        _builtSurfaceSize = _canvas?.CanvasSize ?? Vector2I.Zero;
        WatchTheme(_customTheme);

        if (_canvas?.Canvas is not { } canvas) return;

        // The previous chart goes away: it holds the theme subscription and the interaction state, and
        // leaving it alive would keep calling into a chart nobody draws. The canvas is owned by the
        // surface (the chart is built with ownsCanvas: false), so disposing the chart does not touch it.
        Chart?.Dispose();
        Chart = null;

        // The theme is read-only for the whole render pipeline (Chart, marks, renderers and the tooltip
        // only read it), so the assigned resource is used as it is - no copy. Everything about the look
        // lives in that one resource: the node has no style exports of its own to merge over it.
        _theme = EffectiveTheme;
        _tooltip.Theme = _theme;

        var kind = _kind;
        var size = _canvas.CanvasSize;
        var chart = new Chart(canvas)
        {
            Width = Math.Max(1, size.X),
            Height = Math.Max(1, size.Y),
            Title = string.IsNullOrEmpty(_title) ? null : _title,
        };
        chart.Theme(_theme);
        chart.Data(_rowsData);
        var mark = BuildMark(kind);
        ApplyRowGeometry(chart, mark);
        chart.Mark(mark);
        ApplyEncodes(chart, kind);
        ApplyChannelRanges(chart, mark);
        ApplyColorMapping(chart, kind);
        ApplyAxisRanges(chart);
        ApplyAxesAndLegend(chart);
        ApplyContentShape(chart);
        Chart = chart;
        // The node's switch, applied before ConfigureChart so that escape hatch still has the last word.
        chart.UseLayerCache = _layeredRendering;
        if (_canvas is { } surface) surface.LayeredRendering = _layeredRendering;
        ConfigureChart?.Invoke(chart);
        // The surface stays transparent: the background is what the theme's background renderer paints
        // (a rounded rect), so a zero-alpha BackgroundColor really is see-through and the corner radius
        // shows the page behind the surface. A host override on the chart paints there instead.
        _canvas.BackgroundColor = Colors.Transparent;

        _canvas.Invalidate();
    }

    /// <summary>
    /// Hand the scene's content shape to the chart: the aspect the content box gets and where that box sits
    /// inside the plot area (<see cref="Chart.PlotAspectRatio"/> and the two alignments).
    /// <para>
    /// The export is a <see cref="float"/> with three meanings, because the chart's own property has three:
    /// a positive value is the ratio (width / height, 1 = square), a negative one forces the content to fill
    /// the plot area, and 0 - the default - leaves the decision to the marks (a pie, radar, gauge, chord or
    /// sunburst asks for a square).
    /// </para>
    /// </summary>
    private void ApplyContentShape(Chart chart)
    {
        chart.PlotAspectRatio = _plotAspectRatio switch
        {
            > 0f => _plotAspectRatio,
            < 0f => 0f,
            _ => null,
        };
        chart.PlotAlignHorizontal = _plotAlignHorizontal;
        chart.PlotAlignVertical = _plotAlignVertical;
    }

    /// <summary>
    /// Pin the axis domains the exports ask for. The lock lives on the chart and is applied after the fit,
    /// so a categorical axis (which has no linear scale) simply ignores it.
    /// </summary>
    private void ApplyAxisRanges(Chart chart)
    {
        if (_xAxisRange != Vector2.Zero)
        {
            chart.ScaleDomain(Channel.X, _xAxisRange.X, _xAxisRange.Y);
        }
        if (_yAxisRange != Vector2.Zero)
        {
            chart.ScaleDomain(Channel.Y, _yAxisRange.X, _yAxisRange.Y);
        }
    }

    /// <summary>
    /// Apply the size / opacity / shape mapping knobs. Size lives on the mark (the only consumer of that
    /// channel), opacity and shape are installed as scales - and a scale the node installs has to be fitted
    /// here, because the chart only fits the scales it inferred itself.
    /// </summary>
    private void ApplyChannelRanges(Chart chart, Mark mark)
    {
        if (_sizeRange != Vector2.Zero && mark is PointMark pointMark)
        {
            pointMark.MinRadius = _sizeRange.X;
            pointMark.RadiusRange = Math.Max(0f, _sizeRange.Y - _sizeRange.X);
        }
        if (_opacityRange != Vector2.Zero && RowsCarryField(_opacityField))
        {
            var rangeScale = new OutputRangeScale(new LinearScale(), _opacityRange.X, _opacityRange.Y);
            rangeScale.Fit(FieldValues(_opacityField));
            chart.Scale(Channel.Opacity, rangeScale);
        }
        if (_shapeSymbols.Count > 0 && RowsCarryField(_shapeField))
        {
            ShapeKind[] array = new ShapeKind[_shapeSymbols.Count];
            for (int i = 0; i < array.Length; i++)
            {
                array[i] = _shapeSymbols[i];
            }
            ShapeScale shapeScale = new ShapeScale
            {
                Shapes = array
            };
            shapeScale.Fit(FieldValues(_shapeField));
            chart.Scale(Channel.Shape, shapeScale);
        }
    }

    /// <summary>A field name that is bound to rows (as opposed to empty or a <c>constant:</c> value).</summary>
    /// <summary>
    /// Whether the rows carry this field at all - the question a channel *range* asks, because a range needs
    /// values to look at. <see cref="IsBound"/> is the wider one (a field counts as bound when the rows carry
    /// it or when it is a <c>constant:</c> pin), and a constant has no range to fit: the two are deliberately
    /// different questions, which is why they no longer share a near-identical name.
    /// </summary>
    /// <param name="field">Field name to look for.</param>
    private bool RowsCarryField(string field)
    {
        return !string.IsNullOrEmpty(field) && RowsCarry(field);
    }

    /// <summary>Values of one field across the rows, in row order (for fitting a scale the node installs).</summary>
    private List<object> FieldValues(string field)
    {
        List<object> list = new List<object>(_rowsData.Count);
        foreach (DataRow rowsDatum in _rowsData)
        {
            if (rowsDatum.Has(field) && rowsDatum.Get(field) is { } value)
            {
                list.Add(value);
            }
        }
        return list;
    }

    /// <summary>
    /// Install the scale the colour mapping asks for. <see cref="ColorMappingKind.Auto"/> leaves the
    /// inference alone; the others replace it, which means the view has to fit the scale itself - the
    /// chart only fits the scales it inferred, and an unfitted colour scale would paint everything with
    /// its first palette entry (or leave a gradient without a domain).
    /// </summary>
    private void ApplyColorMapping(Chart chart, ChartKind kind)
    {
        if (_colorMapping == ColorMappingKind.Auto)
        {
            return;
        }
        string? text = ResolveColorField(kind);
        if (text is null || IsConstantField(text))
        {
            return;
        }
        // The same walk the channel ranges use - one implementation of "the values this field carries".
        List<object> list = FieldValues(text);
        if (list.Count != 0)
        {
            ColorMappingKind colorMapping = _colorMapping;
            IScale scale = colorMapping switch
            {
                ColorMappingKind.Category => new ColorScale
                {
                    Palette = (Color[])_theme.Palette.Clone()
                },
                ColorMappingKind.Identity => new IdentityColorScale
                {
                    Palette = (Color[])_theme.Palette.Clone()
                },
                ColorMappingKind.Sequential => new SequentialColorScale
                {
                    Gradient = (Color[])_theme.SequentialGradient.Clone()
                },
                _ => new DivergingColorScale(),
            };
            IScale scale2 = scale;
            scale2.Fit(list);
            chart.Scale(Channel.Color, scale2);
        }
    }

    /// <summary>Create the mark for a kind, with the styling that makes each kind say what it means.</summary>
    private Mark BuildMark(ChartKind kind)
    {
        Mark mark = kind switch
        {
            ChartKind.Bar => new IntervalMark(),
            ChartKind.Line => new LineMark(),
            ChartKind.Area => new LineMark
            {
                ShowArea = true
            },
            ChartKind.Scatter => new PointMark(),
            ChartKind.RangeArea => new RangeAreaMark(),
            ChartKind.Pie => new PieMark(),
            ChartKind.Donut => new PieMark
            {
                InnerRadius = 0.55f
            },
            ChartKind.Radar => new RadarMark(),
            ChartKind.Violin => new ViolinMark(),
            ChartKind.Box => new BoxMark(),
            ChartKind.Candlestick => new CandlestickMark(),
            ChartKind.Heatmap => new HeatmapMark(),
            ChartKind.Treemap => new TreemapMark
            {
                LayoutMode = TreemapLayoutMode.Squarify
            },
            ChartKind.Sunburst => new SunburstMark(),
            ChartKind.Sankey => new SankeyMark(),
            ChartKind.Chord => new ChordMark(),
            ChartKind.Gauge => new GaugeMark(),
            ChartKind.Funnel => new FunnelMark(),
            ChartKind.Waffle => new WaffleMark(),
            ChartKind.Timeline => new TimelineMark(),
            ChartKind.Lollipop => new LollipopMark(),
            ChartKind.Milestone => new MilestoneMark(),
            ChartKind.GeoArea => new GeoAreaMark(),
            ChartKind.GeoBubble => new GeoBubbleMark(),
            _ => new IntervalMark(),
        };
        if (mark is LineMark decimated) decimated.Decimate = _decimate;
        // A heatmap draws one cell per row, so its cell budget is worth exposing where a page is configured.
        if (mark is HeatmapMark heatmap && HeatmapMaxCells > 0) heatmap.MaxCells = HeatmapMaxCells;
        // The node-count marks have the same budget idea: one shape per row, so a big table is a big picture.
        if (DiagramMaxNodes > 0)
        {
            if (mark is TreemapMark treemap) treemap.MaxNodes = DiagramMaxNodes;
            if (mark is SankeyMark sankey) sankey.MaxNodes = DiagramMaxNodes;
        }
        ApplyThemeMarkDefaults(mark);
        _configureMark?.Invoke(mark);
        return mark;
    }

    /// <summary>
    /// Copy the theme's mark defaults onto the mark a kind builds: <see cref="ChartTheme.CornerRadius"/>
    /// for a mark with a corner radius, <see cref="ChartTheme.StrokeWidth"/> for a stroked one. Kinds
    /// whose mark has neither keep their own values.
    /// <para>
    /// This runs before <see cref="ConfigureMark"/>, so the precedence is mark-explicit &gt; theme
    /// default: a value the host sets on the mark - through the configure callback or on a hand-built
    /// <see cref="Chart"/> - always wins over the theme.
    /// </para>
    /// </summary>
    private void ApplyThemeMarkDefaults(Mark mark)
    {
        switch (mark)
        {
            case IntervalMark interval:
                interval.CornerRadius = _theme.CornerRadius;
                // The node's own switches, so a scene-only page can group or stack its bars without a
                // script. They run before ConfigureMark, which therefore still wins.
                interval.GroupedBars = _groupedBars;
                interval.Stack = _stack;
                break;
            case BoxMark box:                box.CornerRadius       = _theme.CornerRadius;  break;
            case CandlestickMark candle:     candle.CornerRadius    = _theme.CornerRadius;  break;
            case FunnelMark funnel:          funnel.CornerRadius    = _theme.CornerRadius;  break;
            case HeatmapMark heatmap:        heatmap.CornerRadius   = _theme.CornerRadius;  break;
            case TimelineMark timeline:      timeline.CornerRadius  = _theme.CornerRadius;  break;
            case WaffleMark waffle:          waffle.CornerRadius    = _theme.CornerRadius;  break;
            case TreemapMark treemap:        treemap.CornerRadius   = _theme.CornerRadius;  break;
            case LineMark line:
                line.StrokeWidth = _theme.StrokeWidth;
                line.Stack = _stack;        // the stacked-area form
                break;
            case RadarMark radar:            radar.StrokeWidth      = _theme.StrokeWidth;   break;
            case ViolinMark violin:          violin.StrokeWidth     = _theme.StrokeWidth;   break;
            case RangeAreaMark rangeArea:    rangeArea.StrokeWidth  = _theme.StrokeWidth;   break;
            // Every other kind builds a mark without either knob (points, slices, bands, flows, ...):
            // there is nothing to copy, and inventing a value would be a knob the renderer never reads.
        }
    }

    /// <summary>
    /// Give a mark that needs geometry of its own something to draw when the page only has a table.
    /// </summary>
    /// <remarks>
    /// A geographic area chart shades regions. With geometry handed to it, that geometry is what is drawn;
    /// without any, the rows themselves <i>are</i> the regions: the categories of the X field become the cells
    /// of a square-ish grid, in the order they first appear, the value column shades them, and the view is
    /// framed on the grid - in the frame's own units, so a table with no coordinates at all still draws a map.
    /// Real map data (see <see cref="GeoJsonReader"/>) goes in on the mark itself, through
    /// <see cref="ConfigureMark"/> or a hand-built <see cref="Chart"/>, which also owns the frame and the view.
    /// </remarks>
    private void ApplyRowGeometry(Chart chart, Mark mark)
    {
        if (mark is GeoBubbleMark)
        {
            FrameRowCoordinates(chart);
            return;
        }

        if (mark is not GeoAreaMark area) return;
        if (area.Features.Count > 0) return; // the caller brought geometry: it wins

        string field = XFieldName();
        var ids = new List<string>();
        foreach (var row in _rowsData)
        {
            if (!row.Has(field)) continue;
            string? id = Convert.ToString(row.Get(field), CultureInfo.InvariantCulture);
            if (!string.IsNullOrEmpty(id) && !ids.Contains(id)) ids.Add(id);
        }
        if (ids.Count == 0) return;

        int columns = (int)Math.Ceiling(Math.Sqrt(ids.Count));
        int rows = (int)Math.Ceiling(ids.Count / (double)columns);
        var builder = new GeoGeometryBuilder();
        var features = new List<GeoFeature>(ids.Count);
        for (int index = 0; index < ids.Count; index++)
        {
            double x = index % columns;
            double y = index / (double)columns;
            features.Add(builder
                .Polygon((x, y), (x + 1, y), (x + 1, y + 1), (x, y + 1))
                .Feature(ids[index], ids[index]));
        }

        area.Features = features;
        area.RowField = field;

        // Frame and view: a cell is one unit, and the grid should fill the surface it is drawn on. The plot is
        // approximated by the surface here (this runs while the chart is being built, before a layout exists),
        // which is close enough for a default view a page can override.
        chart.SetGeoFrame(GeoFrames.CustomPlane(0.0, 0.0, columns, rows));
        double surfaceWidth = Math.Max(1f, Size.X);
        double worldPixels = surfaceWidth / columns;
        chart.SetGeoViewport(columns / 2.0, rows / 2.0, Math.Log2(worldPixels / GeoMath.WorldSizeAtZoomZero));
    }

    /// <summary>
    /// Frame the coordinates the rows themselves carry, for a kind that places them geographically (bubbles):
    /// a table of longitudes and latitudes should show its points, not a corner of the world map.
    /// </summary>
    /// <remarks>
    /// Nothing happens when the page already has a view (a viewport it set, or a page that runs later through
    /// <see cref="ConfigureChart"/> and frames the map itself): the scene wins.
    /// </remarks>
    private void FrameRowCoordinates(Chart chart)
    {
        if (chart.GeoViewport is not null) return;

        string xField = XFieldName();
        string yField = YFieldName();
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var row in _rowsData)
        {
            if (!TryCoordinate(row, xField, out double x) || !TryCoordinate(row, yField, out double y)) continue;
            minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
            minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
        }
        if (minX > maxX) return;

        chart.SetGeoFrame(GeoFrames.Wgs84());
        chart.FitGeoBounds(minX, minY, maxX, maxY, EstimatedPlot(chart), 0.15f);
    }

    /// <summary>A row's field as a finite number, in the units the frame works in.</summary>
    private static bool TryCoordinate(DataRow row, string field, out double value)
    {
        value = 0;
        if (!row.Has(field)) return false;
        object? raw = row.Get(field);
        switch (raw)
        {
            case double number:
                value = number;
                break;
            case null:
                return false;
            default:
                if (!double.TryParse(Convert.ToString(raw, CultureInfo.InvariantCulture),
                                     NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                    return false;
                break;
        }
        return double.IsFinite(value);
    }

    /// <summary>
    /// The plot rectangle a chart of this size will lay itself out with, near enough for a default view (this
    /// runs while the chart is still being built, so there is no layout to read yet).
    /// </summary>
    private static PlotArea EstimatedPlot(Chart chart) => new(
        chart.PaddingLeft,
        chart.PaddingTop,
        MathF.Max(1f, chart.Width - chart.PaddingLeft - chart.PaddingRight),
        MathF.Max(1f, chart.Height - chart.PaddingTop - chart.PaddingBottom));

    /// <summary>Wire the channels. Explicit field names win; otherwise the kind's convention is used.</summary>
    private void ApplyEncodes(Chart chart, ChartKind kind)
    {
        switch (kind)
        {
        case ChartKind.Heatmap:
            chart.Encode(Channel.X, FieldOr(_xField, "x"));
            chart.Encode(Channel.Y, FieldOr(_yField, "y"));
            chart.Encode(Channel.Color, FieldOr(_colorField, "value"));
            break;
        case ChartKind.Timeline:
            chart.Encode(Channel.Y, XFieldName());
            chart.Encode(Channel.X, FieldOr(_yField, "value"));
            break;
        case ChartKind.Sankey:
        case ChartKind.Chord:
            chart.Encode(Channel.Y, FieldOr(_yField, "value"));
            break;
        case ChartKind.Milestone:
        {
            chart.Encode(Channel.X, FieldOr(_xField, "time"));
            chart.Encode(Channel.Y, FieldOr(_yField, "lane"));
            if (ResolveColorField(kind) is { } eventColor)
                chart.Encode(Channel.Color, eventColor);
            break;
        }
        case ChartKind.Box:
        case ChartKind.Candlestick:
            chart.Encode(Channel.X, XFieldName());
            break;
        default:
        {
            chart.Encode(Channel.X, XFieldName());
            chart.Encode(Channel.Y, YFieldName());
            string? text = ResolveColorField(kind);
            if (text is not null)
            {
                chart.Encode(Channel.Color, text);
            }
            break;
        }
        }
        if (!string.IsNullOrEmpty(_sizeField) && IsBound(_sizeField))
        {
            chart.Encode(Channel.Size, _sizeField);
        }
        if (!string.IsNullOrEmpty(_opacityField) && IsBound(_opacityField))
        {
            chart.Encode(Channel.Opacity, _opacityField);
        }
        if (!string.IsNullOrEmpty(_shapeField) && IsBound(_shapeField))
        {
            chart.Encode(Channel.Shape, _shapeField);
        }
    }

    /// <summary>
    /// Step between X-axis ticks in data units (for a year axis: <c>10</c> draws 1970, 1980, 1990 ...).
    /// <c>0</c> (the default) keeps the automatic step, which refines through whole multiples - 10, then 5,
    /// then 1 - as the axis gets longer.
    /// </summary>
    [Export] public float XAxisTickStep
    {
        get => _xAxisTickStep;
        set { if (Mathf.IsEqualApprox(_xAxisTickStep, value)) return; _xAxisTickStep = value; Invalidate(); }
    }

    /// <summary>
    /// Pixels one X-axis tick label may take; `0` (the default) keeps the theme's `TickLabelSpacing`. Raising it
    /// thins the labels out, which is the per-page knob for a crowded axis.
    /// </summary>
    [Export] public float XAxisTickSpacing
    {
        get => _xAxisTickSpacing;
        set { if (Mathf.IsEqualApprox(_xAxisTickSpacing, value)) return; _xAxisTickSpacing = value; Invalidate(); }
    }

    /// <summary>Exact number of X-axis ticks; <c>0</c> (the default) derives it from the axis length.</summary>
    [Export] public int XAxisTickCount
    {
        get => _xAxisTickCount;
        set { if (_xAxisTickCount == value) return; _xAxisTickCount = value; Invalidate(); }
    }

    /// <summary>Format string for the X-axis tick labels; empty keeps the scale's own formatting.</summary>
    [Export] public string XAxisLabelFormat
    {
        get => _xAxisLabelFormat;
        set { if (_xAxisLabelFormat == value) return; _xAxisLabelFormat = value; Invalidate(); }
    }

    /// <summary>
    /// Rotation of the X-axis tick labels in degrees (0 = horizontal): what lets a category axis with long names
    /// show every name instead of every second one.
    /// </summary>
    [Export] public float XAxisLabelRotation
    {
        get => _xAxisLabelRotation;
        set { if (Mathf.IsEqualApprox(_xAxisLabelRotation, value)) return; _xAxisLabelRotation = value; Invalidate(); }
    }

    /// <summary>
    /// Cell budget for a heatmap chart (<see cref="HeatmapMark.MaxCells"/>); 0 keeps the mark's own default.
    /// Rows past it are not drawn and the mark warns, instead of painting a million cells.
    /// </summary>
    [Export] public int HeatmapMaxCells
    {
        get => _heatmapMaxCells;
        set { if (_heatmapMaxCells == value) return; _heatmapMaxCells = value; Invalidate(); }
    }

    /// <summary>
    /// Node budget for a treemap or sankey chart (<see cref="TreemapMark.MaxNodes"/> /
    /// <see cref="SankeyMark.MaxNodes"/>); 0 keeps the mark's own default.
    /// </summary>
    [Export] public int DiagramMaxNodes
    {
        get => _diagramMaxNodes;
        set { if (_diagramMaxNodes == value) return; _diagramMaxNodes = value; Invalidate(); }
    }

    /// <summary>Format string for the Y-axis tick labels; empty keeps the scale's own formatting.</summary>
    [Export] public string YAxisLabelFormat
    {
        get => _yAxisLabelFormat;
        set { if (_yAxisLabelFormat == value) return; _yAxisLabelFormat = value; Invalidate(); }
    }

    /// <summary>Step between Y-axis ticks in data units; <c>0</c> (the default) keeps the automatic step.</summary>
    [Export] public float YAxisTickStep
    {
        get => _yAxisTickStep;
        set { if (Mathf.IsEqualApprox(_yAxisTickStep, value)) return; _yAxisTickStep = value; Invalidate(); }
    }

    /// <summary>Pixels one Y-axis tick label may take; `0` keeps the theme's value.</summary>
    [Export] public float YAxisTickSpacing
    {
        get => _yAxisTickSpacing;
        set { if (Mathf.IsEqualApprox(_yAxisTickSpacing, value)) return; _yAxisTickSpacing = value; Invalidate(); }
    }

    /// <summary>
    /// Margin the Y axis keeps around the data while it is auto-scaled (a fraction of the domain width): inside
    /// it the ticks do not move, outside it the axis refits. 0 (the default) refits on every change.
    /// </summary>
    [Export] public float YAxisAutoScaleMargin
    {
        get => _yAxisAutoScaleMargin;
        set { if (Mathf.IsEqualApprox(_yAxisAutoScaleMargin, value)) return; _yAxisAutoScaleMargin = value; Invalidate(); }
    }

    /// <summary>Round the auto-scaled Y domain out to a {1, 2, 5} x 10^n step (default off).</summary>
    [Export] public bool YAxisNiceDomain
    {
        get => _yAxisNiceDomain;
        set { if (_yAxisNiceDomain == value) return; _yAxisNiceDomain = value; Invalidate(); }
    }

    /// <summary>Pin the lower Y bound and keep fitting the upper one; <c>NaN</c> (the default) fits both ends.</summary>
    [Export] public float YAxisMinLimit
    {
        get => _yAxisMinLimit;
        // .Equals, not Mathf.IsEqualApprox: the default is NaN and a pinned NaN must not look like a change.
        set { if (_yAxisMinLimit.Equals(value)) return; _yAxisMinLimit = value; Invalidate(); }
    }

    /// <summary>Pin the upper Y bound and keep fitting the lower one; <c>NaN</c> (the default) fits both ends.</summary>
    [Export] public float YAxisMaxLimit
    {
        get => _yAxisMaxLimit;
        // .Equals, not Mathf.IsEqualApprox: see YAxisMinLimit.
        set { if (_yAxisMaxLimit.Equals(value)) return; _yAxisMaxLimit = value; Invalidate(); }
    }

    /// <summary>Exact number of Y-axis ticks; <c>0</c> (the default) derives it from the axis length.</summary>
    [Export] public int YAxisTickCount
    {
        get => _yAxisTickCount;
        set { if (_yAxisTickCount == value) return; _yAxisTickCount = value; Invalidate(); }
    }

    private float _xAxisTickStep;
    private int _xAxisTickCount;
    private float _xAxisTickSpacing;
    private string _xAxisLabelFormat = "";
    private float _xAxisLabelRotation;
    private int _heatmapMaxCells;
    private int _diagramMaxNodes;
    private float _yAxisTickStep;
    private int _yAxisTickCount;
    private float _yAxisTickSpacing;
    private string _yAxisLabelFormat = "";
    private float _yAxisAutoScaleMargin;
    private bool _yAxisNiceDomain;
    private float _yAxisMinLimit = float.NaN;
    private float _yAxisMaxLimit = float.NaN;
    private Godot.Collections.Array<float> _sectionLevels = [];
    private float _sectionBandFrom;
    private float _sectionBandTo;
    private ChartSectionTarget _sectionTarget = ChartSectionTarget.Y;
    private Color _sectionColor = new(1f, 0.72f, 0.3f, 0.9f);
    private bool _sectionDashed = true;
    private string _sectionLabelFormat = "";

    /// <summary>
    /// Reference lines drawn over the plot (a target, a threshold): the values go to
    /// <see cref="Marks.SectionMark.Levels"/> and are read against <see cref="SectionTarget"/>. Empty (the
    /// default) draws nothing.
    /// </summary>
    [Export] public Godot.Collections.Array<float> SectionLevels
    {
        get => _sectionLevels;
        // Like ShapeSymbols: no content comparison per frame, just invalidate on assignment; the editor
        // builds a new array when a level is edited.
        set { _sectionLevels = value is { } levels ? levels : []; Invalidate(); }
    }

    /// <summary>Band drawn over the plot between this value and <see cref="SectionBandTo"/>.</summary>
    [Export] public float SectionBandFrom
    {
        get => _sectionBandFrom;
        set { if (Mathf.IsEqualApprox(_sectionBandFrom, value)) return; _sectionBandFrom = value; Invalidate(); }
    }

    /// <summary>Other end of the band; the band needs both ends to differ.</summary>
    [Export] public float SectionBandTo
    {
        get => _sectionBandTo;
        set { if (Mathf.IsEqualApprox(_sectionBandTo, value)) return; _sectionBandTo = value; Invalidate(); }
    }

    /// <summary>Axis the reference lines and the band are read against.</summary>
    [Export] public ChartSectionTarget SectionTarget
    {
        get => _sectionTarget;
        set { if (_sectionTarget == value) return; _sectionTarget = value; Invalidate(); }
    }

    /// <summary>Colour of the reference lines, the band and their labels.</summary>
    [Export] public Color SectionColor
    {
        get => _sectionColor;
        set { if (_sectionColor == value) return; _sectionColor = value; Invalidate(); }
    }

    /// <summary>Draw the reference lines dashed (default) or solid.</summary>
    [Export] public bool SectionDashed
    {
        get => _sectionDashed;
        set { if (_sectionDashed == value) return; _sectionDashed = value; Invalidate(); }
    }

    /// <summary>Format string for the level labels (<c>"0.0 °C"</c>); empty draws none.</summary>
    [Export] public string SectionLabelFormat
    {
        get => _sectionLabelFormat;
        set { if (_sectionLabelFormat == value) return; _sectionLabelFormat = value; Invalidate(); }
    }

    private void ApplyAxesAndLegend(Chart chart)
    {
        // The knobs are a reason to build the config on their own: a page that only sets a tick step (with no
        // axis title) still has to reach the axis. Every export that lands in the config below belongs in the
        // test, or setting it alone is silently ignored - which is how a page ended up configuring nothing at
        // all (its tick spacing, or its auto-scale margin, never reached the axis).
        bool hasXAxis = !string.IsNullOrEmpty(_xAxisTitle) || !string.IsNullOrEmpty(_xAxisUnit)
                        || _xAxisTickStep > 0f || _xAxisTickCount > 0
                        || !string.IsNullOrEmpty(XAxisLabelFormat) || XAxisLabelRotation != 0f
                        || XAxisTickSpacing > 0f;
        if (hasXAxis)
        {
            chart.XAxis(new AxisConfig
            {
                Title = _xAxisTitle,
                Unit = _xAxisUnit,
                TickStep = _xAxisTickStep > 0f ? _xAxisTickStep : null,
                TickCount = _xAxisTickCount > 0 ? _xAxisTickCount : null,
                LabelFormat = string.IsNullOrEmpty(XAxisLabelFormat) ? null : XAxisLabelFormat,
                LabelRotation = XAxisLabelRotation == 0f ? null : XAxisLabelRotation,
                TickLabelSpacing = XAxisTickSpacing > 0f ? XAxisTickSpacing : null,
            });
        }

        bool hasYAxis = !string.IsNullOrEmpty(_yAxisTitle) || !string.IsNullOrEmpty(_yAxisUnit)
                        || _yAxisTickStep > 0f || _yAxisTickCount > 0
                        || !string.IsNullOrEmpty(YAxisLabelFormat) || YAxisTickSpacing > 0f
                        || YAxisAutoScaleMargin > 0f || YAxisNiceDomain
                        || !float.IsNaN(YAxisMinLimit) || !float.IsNaN(YAxisMaxLimit);
        if (hasYAxis)
        {
            chart.YAxis(new AxisConfig
            {
                Title = _yAxisTitle,
                Unit = _yAxisUnit,
                TickStep = _yAxisTickStep > 0f ? _yAxisTickStep : null,
                TickCount = _yAxisTickCount > 0 ? _yAxisTickCount : null,
                TickLabelSpacing = YAxisTickSpacing > 0f ? YAxisTickSpacing : null,
                AutoScaleMargin = YAxisAutoScaleMargin > 0f ? YAxisAutoScaleMargin : null,
                NiceDomain = YAxisNiceDomain,
                MinLimit = float.IsNaN(YAxisMinLimit) ? null : YAxisMinLimit,
                MaxLimit = float.IsNaN(YAxisMaxLimit) ? null : YAxisMaxLimit,
                LabelFormat = string.IsNullOrEmpty(YAxisLabelFormat) ? null : YAxisLabelFormat,
            });
        }
        chart.Legend(new LegendConfig
        {
            Position = _legend
        });

        // Reference lines are annotations, so they are added as a mark of their own - and only when the scene
        // asks for them: a mark with no levels and no band draws nothing and would still cost a render pass.
        bool hasBand = !Mathf.IsEqualApprox(SectionBandFrom, SectionBandTo);
        if (SectionLevels.Count > 0 || hasBand)
        {
            var levels = new double[SectionLevels.Count];
            for (int i = 0; i < SectionLevels.Count; i++) levels[i] = SectionLevels[i];

            chart.Mark(new SectionMark
            {
                Levels = levels,
                BandFrom = hasBand ? SectionBandFrom : null,
                BandTo = hasBand ? SectionBandTo : null,
                Target = SectionTarget switch
                {
                    ChartSectionTarget.Y2 => Channel.Y2,
                    ChartSectionTarget.X => Channel.X,
                    _ => Channel.Y,
                },
                Color = SectionColor,
                Dashed = SectionDashed,
                LabelFormat = SectionLabelFormat,   // the mark treats an empty format as "no label"
            });
        }
    }

    /// <summary>An explicitly named field wins; otherwise the kind's conventional field name.</summary>
    private static string FieldOr(string configured, string fallback)
    {
        return string.IsNullOrEmpty(configured) ? fallback : configured;
    }

    private string XFieldName()
    {
        return FieldOr(_xField, "category");
    }

    private string YFieldName()
    {
        if (!string.IsNullOrEmpty(_yField))
        {
            return _yField;
        }
        ChartKind kind = _kind;
        string result = kind switch
        {
            ChartKind.Box => "median",
            ChartKind.Candlestick => "close",
            _ => "value",
        };
        return result;
    }

    /// <summary>
    /// Field that feeds the colour channel (which also drives the legend), or null when the chart has no
    /// colour of its own to show. An explicit <see cref="ColorField"/> wins and is honoured only when the
    /// rows carry it - the node never invents a field. Otherwise the conventional <c>series</c> field is
    /// used, and for the kinds where one row <i>is</i> one element and the category names it (pie, donut,
    /// funnel, waffle) the category itself colours the element - the G2 default that gives every slice,
    /// stage and cell its own palette colour.
    /// </summary>
    private string? ResolveColorField(ChartKind kind)
    {
        if (!string.IsNullOrEmpty(_colorField))
        {
            return IsBound(_colorField) ? _colorField : null;
        }
        if (RowsCarry("series"))
        {
            return "series";
        }
        // A geographic area chart shades each region by the value it carries: the colour *is* the data.
        if (kind == ChartKind.GeoArea)
        {
            return YFieldName();
        }
        // A pie/donut/funnel/waffle names its own slices by the category field.
        return kind is ChartKind.Pie or ChartKind.Donut or ChartKind.Funnel or ChartKind.Waffle
            ? XFieldName()
            : null;
    }

    /// <summary>
    /// A field name is bound when the rows carry it, and a <c>constant:</c> value is bound because
    /// <see cref="Chart.Encode(Channel, string)"/> turns it into a constant instead of a field - that is
    /// how a colour field can say "one colour for the whole chart".
    /// </summary>
    private bool IsBound(string field)
    {
        return RowsCarry(field) || IsConstantField(field);
    }

    private static bool IsConstantField(string field)
    {
        return field.StartsWith("constant:", StringComparison.Ordinal);
    }

    private bool RowsCarry(string field)
    {
        foreach (DataRow rowsDatum in _rowsData)
        {
            if (rowsDatum.Has(field))
            {
                return true;
            }
        }
        return false;
    }

    private void UpdateHover()
    {
        if (Chart is not { } chart || !_pointerInside) return;

        chart.Interaction(_showCrosshair ? _mousePos : null);
        var hit = _lastHit = chart.HitTest(_mousePos);

        // The legend and the axis bands are hit regions without a data row: they clear the hover row.
        int rowIndex = hit is { Hit: true, MarkType: not ("Legend" or "XAxis" or "YAxis") }
            ? hit.RowIndex
            : -1;

        // NotifyHoverChanged both stores the row and raises OnHover when it changed - calling Hover()
        // first would update the stored row and silence the event.
        chart.NotifyHoverChanged(rowIndex, hit);

        Repaint();
    }

    /// <summary>Convert the exported variant rows into the row model.</summary>
    private void LoadVariantRows()
    {
        _rowsData.Clear();
        foreach (Godot.Collections.Dictionary row in _rows)
        {
            _rowsData.Add(FromDictionary(row));
        }
        Invalidate();
    }

    /// <summary>Keep the exported row array's signature in step with what the node just wrote.</summary>
    private void SyncRowsSignature() => _rowsSignature = SignatureOf(_rows);

    /// <summary>
    /// The exported arrays can be edited in place in the inspector, which does not go through the node's
    /// setters - so compare a signature of each to notice.
    /// </summary>
    internal void CheckVariantRows()
    {
        string signature = SignatureOf(_rows);
        if (signature != _rowsSignature)
        {
            _rowsSignature = signature;
            LoadVariantRows();
        }

        string symbols = string.Join(",", _shapeSymbols);
        if (symbols != _shapeSymbolsSignature)
        {
            _shapeSymbolsSignature = symbols;
            Invalidate();
        }
    }

    private static string SignatureOf(Godot.Collections.Array<Godot.Collections.Dictionary> rows)
    {
        var builder = new StringBuilder();
        builder.Append(rows.Count).Append('|');
        foreach (Godot.Collections.Dictionary row in rows)
        {
            builder.Append(row.Count).Append(':');
            foreach (var (key, value) in row)
            {
                builder.Append(key).Append('=').Append(value.VariantType).Append(':');
                builder.Append(value.Obj ?? value.AsString()).Append(',');
            }
            builder.Append(';');
        }
        return builder.ToString();
    }

    private static DataRow FromDictionary(Godot.Collections.Dictionary dictionary)
    {
        var row = new DataRow(dictionary.Count);
        foreach (var (key, value) in dictionary)
        {
            row.Set(key.AsString(), ToObject(value));
        }
        return row;
    }

    private static Godot.Collections.Dictionary ToDictionary(DataRow row)
    {
        var dictionary = new Godot.Collections.Dictionary();
        foreach (var (field, value) in row.Fields)
        {
            dictionary[field] = ToVariant(value);
        }
        return dictionary;
    }

    /// <summary>Map a typed value to a variant, so the inspector shows the same type.</summary>
    private static Variant ToVariant(object? value) => value switch
    {
        null => default,
        bool boolean => boolean,
        int number => number,
        long number => number,
        float number => number,
        double number => number,
        string text => text,
        Color color => color,   // a colour stays a colour: the colour channel uses it as it is
        _ => value.ToString() ?? "",
    };

    /// <summary>
    /// A variant keeps its type: integers stay <see cref="long"/>, decimals <see cref="double"/>,
    /// text <see cref="string"/> and flags <see cref="bool"/>. Nothing is stringified.
    /// </summary>
    private static object ToObject(Variant value) => value.VariantType switch
    {
        Variant.Type.Bool => value.AsBool(),
        Variant.Type.Int => value.AsInt64(),
        Variant.Type.Float => value.AsDouble(),
        Variant.Type.String => value.AsString(),
        Variant.Type.StringName => (string)value.AsStringName(),
        Variant.Type.NodePath => (string)value.AsNodePath(),
        Variant.Type.Color => value.AsColor(),
        _ => value.ToString(),
    };

}
