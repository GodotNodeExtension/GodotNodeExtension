using System;
using System.Collections.Generic;
using Godot;
using GodotNodeExtension.Component.GodotChart.Canvas;

namespace GodotNodeExtension.Component.GodotChart;

/// <summary>
/// Configuration for a chart axis, including title, description, and tooltip content.
/// </summary>
public class AxisConfig
{
    /// <summary>Axis title displayed alongside the axis (e.g. "Time", "Revenue").</summary>
    public string? Title { get; init; }

    /// <summary>Detailed description shown in tooltip when hovering over the axis area.</summary>
    public string? Description { get; init; }

    /// <summary>Unit label (e.g. "USD", "ms").</summary>
    public string? Unit { get; init; }

    /// <summary>
    /// Step between ticks, in data units (for a year axis: <c>10</c> draws 1970, 1980, 1990 ...). Overrides the
    /// automatic step, which refines through whole multiples - 10, then 5, then 2, then 1 - as the axis gets
    /// longer. Ticks still land on values the data has: a multiple the table does not contain snaps to the
    /// nearest value it does.
    /// </summary>
    public double? TickStep { get; init; }

    /// <summary>
    /// Number of ticks to aim for on this axis, over the automatic count (which comes from the axis length and
    /// <see cref="ChartTheme.TickLabelSpacing"/>, never below <see cref="ChartTheme.MinTickCount"/>).
    /// </summary>
    public int? TickCount { get; init; }

    /// <summary>
    /// Format string for this axis' tick labels, applied to the tick value (a numeric axis: <c>"0.0 °C"</c>,
    /// <c>"{0:N0}"</c>). Null (the default) keeps the scale's own formatting. Only numeric axes are
    /// reformatted - a category, log or time axis labels categories and dates, not numbers.
    /// </summary>
    public string? LabelFormat { get; init; }

    /// <summary>
    /// Rotation of this axis' tick labels in degrees, over the theme's
    /// <see cref="ChartTheme.XAxisLabelRotation"/>. Only the X axis draws rotated labels (a rotated Y label
    /// would also change how much room the label column reserves).
    /// </summary>
    public float? LabelRotation { get; init; }

    /// <summary>
    /// Pixels this axis gives one tick label, over the theme's <see cref="ChartTheme.TickLabelSpacing"/>. A
    /// larger value means fewer labels, which is what a short axis or a crowded card wants.
    /// </summary>
    public float? TickLabelSpacing { get; init; }

    /// <summary>
    /// Margin, as a fraction of the domain width, that a sticky auto-scaled axis keeps around the data: while
    /// the rows stay inside it the domain does not move, so a quote feed stops jittering its ticks; when a row
    /// leaves it the axis refits (and, with <see cref="NiceDomain"/>, jumps to the next nice step). Null or 0
    /// (the default) keeps the plain refit-every-change behaviour.
    /// </summary>
    public float? AutoScaleMargin { get; init; }

    /// <summary>
    /// Round the fitted domain out to a {1, 2, 5} x 10^n step, so the ticks and the grid change in jumps instead
    /// of drifting by a pixel every frame. Default false: the domain is exactly the data's range.
    /// </summary>
    public bool NiceDomain { get; init; }

    /// <summary>
    /// Pin the lower end of the axis and keep fitting the upper one (the counterpart of
    /// <see cref="MaxLimit"/>). Both null (the default) means the axis is fitted from the data.
    /// </summary>
    public double? MinLimit { get; init; }

    /// <summary>Pin the upper end of the axis and keep fitting the lower one ("cap the axis, let it grow".)</summary>
    public double? MaxLimit { get; init; }

    /// <summary>
    /// Exact tick values for this axis, over everything else: what the caller lists is what gets drawn (values
    /// outside the visible window are skipped). This is the escape hatch for an axis whose ticks are known.
    /// </summary>
    public double[]? Ticks { get; init; }

    // NOTE: a TooltipBuilder property was removed here: the axis hit test always built a plain
    // label from Title + Description + Unit, so the builder never had any effect.
}

/// <summary>Legend position relative to the chart plot area.</summary>
public enum LegendPosition
{
    /// <summary>Legend above the plot area.</summary>
    Top,

    /// <summary>Legend below the plot area.</summary>
    Bottom,

    /// <summary>Legend left of the plot area.</summary>
    Left,

    /// <summary>Legend right of the plot area.</summary>
    Right,

    /// <summary>No legend is rendered (use for an external legend UI).</summary>
    None,
}

/// <summary>
/// Configuration for the chart legend. When enabled, the legend auto-generates
/// entries from the Color channel's scale domain.
/// </summary>
public class LegendConfig
{
    /// <summary>Position of the legend (default Top).</summary>
    public LegendPosition Position { get; init; } = LegendPosition.Top;

    /// <summary>Horizontal spacing between legend items (px).</summary>
    public float ItemSpacing { get; set; } = 16f;

    /// <summary>Color swatch size (px).</summary>
    public float SwatchSize { get; init; } = 10f;

    /// <summary>Padding around the legend area (px).</summary>
    public float Padding { get; init; } = 6f;
}

public partial class Chart : IDisposable
{
    /// <summary>
    /// When true (default), the plot area is widened when the Y/Y2 tick labels need more room than
    /// <see cref="PaddingLeft"/> / <see cref="PaddingRight"/> provide, so long labels are not
    /// clipped by the chart edge.
    /// </summary>
    public bool AutoPadding
    {
        get => _autoPadding;
        set { if (_autoPadding == value) return; _autoPadding = value; InvalidateLayout(); }
    }

    private bool _autoPadding = true;

    /// <summary>
    /// Width needed by the widest tick label of an axis, measured with the real text metrics and the
    /// same tick set the renderers use (<see cref="ChartTheme.FallbackTickCount"/> included).
    /// Returns 0 when the scale is missing or nothing can be labelled. The value is the space the axis
    /// needs as a whole - see <see cref="DefaultRenderers.AxisLabelReservedWidth"/>, which is also what
    /// the drawing side derives its label width from.
    /// </summary>
    internal float MeasureAxisLabelWidth(Channel channel, TextAlign align)
    {
        var scale = _scales.TryGet(channel);
        if (scale == null) return 0f;

        // Measuring every tick label on every frame is wasteful (the Skia backend creates a font and
        // a text blob per call), and the result only changes with the layout version. The version is
        // tracked per channel: the widths share one dictionary, so a single shared version made the
        // second channel measured after a layout change (`Y`, then `Y2`) return its stale entry.
        int version = EffectiveLayoutVersion;
        if (_labelWidthVersions.TryGetValue(channel, out int cachedVersion) && cachedVersion == version
            && _labelWidths.TryGetValue(channel, out float cached))
            return cached;

        var font = new FontSettings
        {
            Size = _theme.LabelFontSize > 0f ? _theme.LabelFontSize : FontSettings.Default.Size,
            Family = _theme.FontFamily,
            GodotFont = _theme.Font,
            Align = align,
        };
        // The same budget the renderer uses (see DefaultRenderers.TicksFor): the label column has to be
        // measured against the ticks that will actually be drawn, or the plot is laid out for one set of
        // labels and drawn with another. The axis length is approximated here because this runs while the
        // plot rectangle is still being built - an axis is at most the chart's height (Y) or width (X).
        float axisLength = channel == Channel.X ? Width : Height;
        int maxTicks = (int)Math.Clamp(axisLength / MathF.Max(1f, _theme.TickLabelSpacing),
            _theme.MinTickCount, _theme.MaxTickCount);
        var axisConfig = ChannelRoles.AxisConfigOf(channel, _xAxisConfig, _yAxisConfig, _y2AxisConfig);
        float widest = 0f;
        foreach (var (_, text) in DefaultRenderers.ComputeTicks(
                     scale, maxTicks: axisConfig?.TickCount ?? maxTicks,
                     fallbackTickCount: _theme.FallbackTickCount,
                     minTicks: axisConfig?.TickCount ?? _theme.MinTickCount,
                     forcedStep: axisConfig?.TickStep, explicitTicks: axisConfig?.Ticks))
            widest = MathF.Max(widest, _canvas.MeasureText(text, font).Width);

        float result = DefaultRenderers.AxisLabelReservedWidth(_theme, widest);
        _labelWidths[channel] = result;
        _labelWidthVersions[channel] = version;
        return result;
    }

    /// <summary>
    /// Width reserved for each channel's axis labels, and the layout version it was measured at, so a
    /// resize, a padding change or another font does not keep serving the previous number.
    /// </summary>
    private readonly Dictionary<Channel, float> _labelWidths = [];

    /// <summary>
    /// Layout version at which each channel's entry in <see cref="_labelWidths"/> was measured, so
    /// every channel is invalidated on its own.
    /// </summary>
    private readonly Dictionary<Channel, int> _labelWidthVersions = [];

    private readonly ICanvas2D        _canvas;
    private readonly bool             _ownsCanvas;
    private bool                      _disposed;
    private readonly List<DataRow>    _data    = [];
    private readonly List<Mark>       _marks   = [];
    private readonly EncodeSet        _encodes = new();
    private readonly ScaleSet         _scales  = new();
    private readonly List<IDataTransform> _transforms = [];

    // Track which scales were auto-fitted (vs manually set) so they can be cleared on data change
    private readonly HashSet<Channel> _autoFittedChannels = [];

    /// <summary>Pinned domains per channel, applied after every fit (see <see cref="ScaleDomain"/>).</summary>
    private readonly Dictionary<Channel, (double Min, double Max)> _domainLocks = [];

    // Monotonically increasing version for cache invalidation
    private int _dataVersion;

    // Combined version: incremented when data, encode, scale, or layout changes
    private int _layoutVersion;

    /// <summary>
    /// Invalidate everything that depends on the plot layout or on the mark list: cached mark
    /// geometries, the mark-compatibility check result and the Cartesian-decoration probe.
    /// Data changes use <see cref="InvalidateData"/> instead, which also resets auto-fitted scales.
    /// </summary>
    private void InvalidateLayout()
    {
        _layoutVersion++;
        _skippedMarks = null;
        _skippedMarksLayoutVersion = -1;
        _skipCartesianDecorations = null;
    }

    /// <summary>
    /// Version handed to marks for layout caching. Besides <see cref="_layoutVersion"/> it folds in
    /// every input that changes the plot rectangle or the theme metrics, so expensive cached
    /// geometries (Treemap/Sankey/Chord/Sunburst/Waffle/stacked bars) are rebuilt after a resize,
    /// a padding change or a theme swap instead of reusing stale coordinates.
    /// <para>
    /// A theme edited <i>in place</i> keeps its instance - and with it <see cref="_layoutVersion"/> -
    /// so that case is covered by <see cref="ObserveTheme"/>: the chart follows the resource's
    /// <see cref="Resource.Changed"/> signal and calls <see cref="InvalidateLayout"/>.
    /// </para>
    /// </summary>
    internal int EffectiveLayoutVersion
    {
        get
        {
            int plotHash = HashCode.Combine(
                Width, Height, PaddingLeft, PaddingRight, PaddingTop, PaddingBottom);
            // Everything else BuildPlot reads: a title reserves height, AutoPadding widens the plot for
            // long tick labels, an axis title reserves space on its side and a side legend reserves
            // width. The setters invalidate the layout as well; folding them in here also covers a
            // configuration object edited in place after it was handed to the chart.
            // The content shape and where it sits decide the plot rectangle the marks get, so a change drops
            // the layout caches - and the cached layer, which captures exactly that rectangle.
            int shapeHash = HashCode.Combine(PlotAspectRatio, PlotAlignHorizontal, PlotAlignVertical);
            int decorHash = HashCode.Combine(
                _title, _autoPadding, _xAxisConfig?.Title, _yAxisConfig?.Title, _y2AxisConfig?.Title,
                _legendConfig?.Position, LegendRenderer != null, shapeHash);
            return HashCode.Combine(_layoutVersion, plotHash, decorHash, OffsetX, OffsetY,
                                    _theme.GetHashCode());
        }
    }

    // Cached check: true when no Cartesian marks exist (skip grid/axes/crosshair)
    private bool? _skipCartesianDecorations;

    // Cached transformed data, rebuilt when data version changes
    private List<DataRow>? _transformedData;

    // Series visibility control
    private HashSet<string>? _hiddenSeries;
    private int _transformedDataVersion = -1;

    // The legend geometry of the last frame (null when no legend is shown): one cache, read by Render, by
    // HitTest and by the renderer slots (through RenderContext.CachedLegendLayout) alike.
    internal LegendLayout? CachedLegendLayout => _cachedLegendLayout;

    // Cached legend layout: recomputed only when the layout version, the plot, the domain or the
    // legend configuration changes (in between, the labels would be re-measured on every frame).
    // The key itself is declared in Chart.Render.cs, next to the layout it identifies.
    private LegendLayout? _cachedLegendLayout;
    private float _lastLegendReservedHeight;
    private LegendLayoutKey _cachedLegendLayoutKey;

    // Reused contexts: the renderer slots and marks receive one stable instance per frame instead of
    // a freshly allocated one.
    private readonly RenderContext _renderContext = new();
    private readonly MarkContext _markContext = new();

    // The 2D projection, handed to every mark through MarkContext.Mapper. One instance per chart: the
    // plot rectangle it projects into is rewritten in place on each frame.
    private readonly PlanarMapper _planarMapper = new();

    // Layout — the defaults live in one place (see ChartDefaults). Every member is writable: the plot
    // rectangle is recomputed from them on each frame and EffectiveLayoutVersion hashes them, so the
    // caches drop themselves. Making two of them init-only (PaddingRight/Top/Bottom, OffsetX) was an
    // oversight that also made the documented "set the padding after construction" example impossible.
    /// <summary>Space reserved on the left for the Y axis labels.</summary>
    public float PaddingLeft   { get; set; } = ChartDefaults.PaddingLeft;
    /// <summary>Space reserved on the right.</summary>
    public float PaddingRight  { get; set; } = ChartDefaults.PaddingRight;
    /// <summary>Space reserved at the top.</summary>
    public float PaddingTop    { get; set; } = ChartDefaults.PaddingTop;
    /// <summary>Space reserved at the bottom for the X axis labels.</summary>
    public float PaddingBottom { get; set; } = ChartDefaults.PaddingBottom;
    /// <summary>Chart width in pixels.</summary>
    public float Width         { get; set; } = ChartDefaults.Width;
    /// <summary>Chart height in pixels.</summary>
    public float Height        { get; set; } = ChartDefaults.Height;

    /// <summary>
    /// Shape the chart's content wants, as width divided by height (1 = square), or null (the default) to
    /// fill the whole plot rectangle. Null is also "let the marks decide": a chart whose marks all declare the
    /// same <see cref="Mark.PreferredAspectRatio"/> takes it (polar marks ask for a square), while one mark
    /// that fills the rectangle - or two marks that disagree - keeps the whole rectangle. Set 0 (or NaN) to
    /// force filling and ignore what the marks ask for.
    /// <para>
    /// The content rectangle is the largest rectangle of that shape inside the plot area, placed by
    /// <see cref="PlotAlignHorizontal"/> and <see cref="PlotAlignVertical"/>; it is what
    /// <see cref="CurrentPlotArea"/> reports. A mark that draws from the plot's short edge (a pie, a radar, a
    /// gauge) therefore stops wasting the long one: on a wide canvas the square it lives in can be aligned
    /// instead of floating in the middle.
    /// </para>
    /// </summary>
    public float? PlotAspectRatio { get; set; }

    /// <summary>Where the content rectangle sits inside the plot area when <see cref="PlotAspectRatio"/> shapes it.</summary>
    public HorizontalAlignment PlotAlignHorizontal { get; set; } = HorizontalAlignment.Center;

    /// <summary>Where the content rectangle sits inside the plot area when <see cref="PlotAspectRatio"/> shapes it.</summary>
    public VerticalAlignment PlotAlignVertical { get; set; } = VerticalAlignment.Center;

    /// <summary>
    /// The rectangle the chart actually used in its canvas in the last frame: the content rectangle (marks, grid
    /// and axes) united with the decoration bands the layout really reserved - the title, the legend, the axis
    /// label columns and the axis titles. Null before the first frame.
    /// <para>
    /// It answers "how much of the canvas is the chart" - a small chart in a large box reports a small
    /// rectangle, and it grows and shrinks with the decorations: turning the legend or a title off moves it, and
    /// on a polar chart whose content box is a square it is that square unless something is drawn around it.
    /// </para>
    /// <para>
    /// Two things are deliberately outside it: the background fill, which covers the whole canvas by design (it
    /// would make this the node rectangle and say nothing), and the host's tooltip, which is drawn next to the
    /// pointer and may leave the plot area. It is the range the <i>layout</i> reserved, not a per-pixel
    /// measurement of the ink - a side with no decoration on it is not included.
    /// </para>
    /// </summary>
    public Rect2? DrawnBounds => _drawnBounds;

    private Rect2? _drawnBounds;

    /// <summary>Theme</summary>
    private ChartTheme _theme = ChartTheme.Dark();

    /// <summary>
    /// Theme currently subscribed to (<see cref="_themeObserver"/>), null when nothing is watched.
    /// Kept so <see cref="Theme"/> and <see cref="Dispose"/> can unsubscribe the same instance.
    /// </summary>
    private ChartTheme? _observedTheme;

    private ThemeObserver? _themeObserver;

    // Visual properties — null means "use theme default"
    private Color? _backgroundColor;
    private Color? _gridColor;
    private Color? _axisColor;

    /// <summary>Background colour; falls back to the theme when not set.</summary>
    public Color BackgroundColor
    {
        get => _backgroundColor ?? _theme.BackgroundColor;
        set => _backgroundColor = value;
    }
    /// <summary>Grid colour; falls back to the theme when not set.</summary>
    public Color GridColor
    {
        get => _gridColor ?? _theme.GridColor;
        set => _gridColor = value;
    }
    /// <summary>Axis line/title colour; falls back to the theme when not set.</summary>
    public Color AxisColor
    {
        get => _axisColor ?? _theme.AxisColor;
        set => _axisColor = value;
    }

    /// <summary>Horizontal offset of the whole chart inside the canvas.</summary>
    public float  OffsetX { get; set; }

    /// <summary>Vertical offset of the whole chart inside the canvas.</summary>
    public float  OffsetY { get; set; }

    /// <summary>Optional chart title; null hides the title area.</summary>
    public string? Title
    {
        get => _title;
        set { if (_title == value) return; _title = value; InvalidateLayout(); }
    }

    private string? _title;

    // Interaction state
    private Vector2?         _mousePos;
    private int              _hoveredRowIndex  = -1;
    private int              _selectedRowIndex = -1;
    private string?           _focusedSeries;
    private AnimationContext _animationContext = AnimationContext.Default;

    // Axis configuration
    private AxisConfig? _xAxisConfig;
    private AxisConfig? _yAxisConfig;
    private AxisConfig? _y2AxisConfig;

    // Legend configuration
    private LegendConfig? _legendConfig;

    // Mark compatibility validation cache
    private HashSet<Mark>? _skippedMarks;
    private int _skippedMarksLayoutVersion = -1;

    // Plot area cache
    /// <summary>Set while <see cref="Render"/> runs: a reentrant call (a callback that renders again) is
    /// ignored instead of nesting into the layout state.</summary>
    private bool _isRendering;

    private PlotArea? _lastPlot;

    /// <summary>Plot area of the last frame before the content shape was applied (see <see cref="_lastPlot"/>).</summary>
    private PlotArea _fullPlot;

    /// <summary>Expose marks list for external hit testing.</summary>
    internal IReadOnlyList<Mark> Marks => _marks;

    // ── Renderer slots ─────────────────────────────────────────

    /// <summary>Renders the chart background. Default: rounded rect with BackgroundColor.</summary>
    public ChartRenderer? BackgroundRenderer { get; set; } = DefaultRenderers.DrawBackground;

    /// <summary>Renders the chart title. Only called when Title is not null.</summary>
    public ChartRenderer? TitleRenderer { get; set; } = DefaultRenderers.DrawTitle;

    /// <summary>Renders grid lines. Skipped for polar-only charts.</summary>
    public ChartRenderer? GridRenderer { get; set; } = DefaultRenderers.DrawGrid;

    /// <summary>Renders axis lines. Skipped for polar-only charts.</summary>
    public ChartRenderer? AxisRenderer { get; set; } = DefaultRenderers.DrawAxes;

    /// <summary>Renders axis labels and titles. Skipped for polar-only charts.</summary>
    public ChartRenderer? AxisLabelRenderer { get; set; } = DefaultRenderers.DrawAxisLabels;

    /// <summary>Renders the legend. Set null for external legend UI.</summary>
    public ChartRenderer? LegendRenderer { get; set; } = DefaultRenderers.DrawLegend;

    /// <summary>Renders the crosshair overlay.</summary>
    public ChartRenderer? CrosshairRenderer { get; set; } = DefaultRenderers.DrawCrosshair;

    /// <summary>
    /// Maximum number of data points to retain. 0 = unlimited.
    /// When set and data exceeds this limit, oldest rows are trimmed.
    /// </summary>
    public int WindowSize { get; set; }

    /// <summary>
    /// Keep the non-interactive part of the frame (background, title, grid, axes, axis labels, legend and the
    /// marks' non-interactive state) in an image, and repaint only the overlay - the marks' hover/selection
    /// visuals and the crosshair - while the pointer moves. Default <c>false</c>.
    /// <para>
    /// With the cache on, a frame that does not change any of the layer's inputs (data, layout, plot area,
    /// theme, animation, focused series, legend configuration) presents the cached image and runs the overlay
    /// pass alone, so a pointer move stops costing a full redraw: on a 100k-point line the hover frame drops
    /// from the point mapping plus the path to the single hover marker. That is what the option is for - a
    /// static or interactive page, not a streaming one, which changes its data every frame and would pay the
    /// per-rebuild capture on top of the redraw.
    /// </para>
    /// <para>
    /// It only engages when the canvas backend can capture its surface
    /// (<see cref="CanvasCapabilities.SupportsSurfaceCapture"/>) and every mark of the chart paints its
    /// interaction state on the overlay (<see cref="Mark.InteractionStateInOverlay"/>); otherwise the frame
    /// is drawn single-pass, and the chart says why once.
    /// </para>
    /// <para>
    /// Cost and preconditions: one extra image of the chart rectangle (<c>width × height × 4</c> bytes -
    /// about 1.7 MB for 830×520, about 33 MB for 4K) plus one surface readback per rebuild, and the host has
    /// to clear the surface before every frame (<see cref="Canvas2DControl.ClearBeforeDraw"/>, on by default)
    /// - the cached layer carries its own alpha and would otherwise be composited onto the previous frame.
    /// Hover and selection never rebuild the layer, so the highlight is drawn over a cached layer instead of
    /// replacing it: for an element whose effective opacity is below 1 that composite differs from the
    /// single-pass picture by at most one 8-bit step, and an element at full opacity is identical.
    /// </para>
    /// </summary>
    public bool UseLayerCache
    {
        get => _useLayerCache;
        set
        {
            if (_useLayerCache == value) return;
            _useLayerCache = value;
            // The image is the whole cost of the option: dropping it with the switch keeps a host that toggles
            // the cache from keeping a full-size bitmap around for nothing.
            if (!value) ReleaseLayer();
        }
    }

    private bool _useLayerCache;

    /// <summary>
    /// Drop the cached data layer, so the next <see cref="Render"/> builds it again.
    /// <para>
    /// The layer rebuilds itself when the data, the layout, the theme, the plot area, the animation progress,
    /// the focused series or the legend configuration change - the inputs the chart can observe. A mark
    /// property edited directly (<c>mark.StrokeWidth = …</c>, or anything else set outside the chart's own
    /// setters) is not one of them, so a host that changes one calls this to say "the layer is stale".
    /// <see cref="ApplyToAllMarks"/>, <see cref="Mark(GodotNodeExtension.Component.GodotChart.Mark)"/>,
    /// <see cref="Data(IEnumerable{DataRow})"/>,
    /// <see cref="Theme"/> and the other chainable setters already invalidate the layout and need no call.
    /// </para>
    /// </summary>
    public Chart InvalidateLayerCache()
    {
        ReleaseLayer();
        // A backend that failed to capture is given another chance: it may have been resized, or the chart
        // may have moved back inside the surface.
        _layerUnavailable = false;
        return this;
    }

    /// <summary>
    /// Create a chart that draws onto <paramref name="canvas"/>.
    /// </summary>
    /// <param name="canvas">Canvas to draw on (created via <c>Canvas2DFactory.Create</c>).</param>
    /// <param name="ownsCanvas">
    /// When true, <see cref="Dispose"/> also disposes the canvas. Leave false (default) when several
    /// charts share one canvas - otherwise disposing a single chart would kill the others.
    /// </param>
    public Chart(ICanvas2D canvas, bool ownsCanvas = false)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        _canvas = canvas;
        _ownsCanvas = ownsCanvas;
    }

    /// <summary>
    /// Set the chart theme for centralized style control. The chart follows the resource: editing a
    /// theme <i>in place</i> (the inspector, or any setter / <see cref="Resource.EmitChanged"/>) keeps
    /// the same instance, which the chart cannot notice on its own, so it listens to the resource's
    /// <see cref="Resource.Changed"/> signal and drops its cached layout when it fires.
    /// <para>
    /// This subscription serves a chart that a host built by hand. A <see cref="ChartView"/> additionally
    /// watches the resource itself (<c>ChartView.WatchTheme</c>) because it has to <i>rebuild</i> its chart
    /// on a theme edit, not only drop a cache - so the two subscriptions exist for two different owners and
    /// are not duplicates.
    /// </para>
    /// </summary>
    public Chart Theme(ChartTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        if (!ReferenceEquals(_theme, theme))
        {
            ObserveTheme(null);      // stop following the theme this chart is leaving behind
            _theme = theme;
            ObserveTheme(theme);
        }
        InvalidateLayout();
        return this;
    }

    /// <summary>
    /// Follow <paramref name="theme"/>'s <see cref="Resource.Changed"/> signal, or stop following the
    /// previously observed one when <paramref name="theme"/> is null. Symmetric by construction: a
    /// chart subscribes to exactly one theme at a time, and <see cref="Dispose"/> unsubscribes.
    /// </summary>
    private void ObserveTheme(ChartTheme? theme)
    {
        if (ReferenceEquals(_observedTheme, theme)) return;

        if (_observedTheme is not null && _themeObserver is not null)
            _observedTheme.Changed -= _themeObserver.OnChanged;

        _observedTheme = theme;
        if (theme is null) return;

        _themeObserver = new ThemeObserver(this, theme);
        theme.Changed += _themeObserver.OnChanged;
    }

    /// <summary>
    /// Bridges one theme resource's <see cref="Resource.Changed"/> signal to the chart that watches it.
    /// <para>
    /// The indirection is deliberate: a <see cref="Chart"/> is a plain object Godot does not manage,
    /// while the theme is a resource that outlives the charts built on it (a shared theme, the
    /// <c>.tres</c> in the inspector, or the theme a <see cref="ChartView"/> hands to every chart it
    /// rebuilds). Connecting the chart's own handler would let that resource keep the chart - with its
    /// canvas and its data - alive forever, so the observer holds a weak reference instead and
    /// disconnects itself once the chart is gone.
    /// </para>
    /// </summary>
    private sealed class ThemeObserver
    {
        private readonly WeakReference<Chart> _chart;
        private readonly ChartTheme _theme;

        public ThemeObserver(Chart chart, ChartTheme theme)
        {
            _chart = new WeakReference<Chart>(chart);
            _theme = theme;
        }

        /// <summary>The theme changed underneath the chart: every cached layout built from it is stale.</summary>
        public void OnChanged()
        {
            if (_chart.TryGetTarget(out var chart) && !chart._disposed)
            {
                chart.InvalidateLayout();
                return;
            }

            // The chart was collected or disposed: drop the connection, so a long-lived resource does
            // not accumulate handlers for charts that no longer exist.
            _theme.Changed -= OnChanged;
        }
    }

    // ── Mark ──────────────────────────────────────────────────
    /// <summary>
    /// Add a mark to the chart. The first added mark defines the coordinate system.
    /// Use <see cref="Mark{T}()"/> for a mark that only needs its defaults, or when the mark is built by
    /// <see cref="ChartView.ConfigureMark"/> from a type the caller does not name.
    /// </summary>
    public Chart Mark(Mark mark)
    {
        ArgumentNullException.ThrowIfNull(mark);
        _marks.Add(mark);
        InvalidateLayout();
        return this;
    }

    /// <summary>Add a mark of the given type (created with its default settings).</summary>
    public Chart Mark<T>() where T : Mark, new()
    {
        _marks.Add(new T());
        InvalidateLayout();
        return this;
    }

    /// <summary>
    /// Apply an action to every mark currently registered on this chart.
    /// Useful for batch-configuring properties across all marks.
    /// </summary>
    public Chart ApplyToAllMarks(Action<Mark> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        foreach (var mark in _marks) action(mark);
        // Whatever the action configured (a layout mode, a gap, a cap) most likely feeds a cached
        // geometry: drop the caches so the next frame rebuilds instead of drawing the old layout.
        InvalidateLayout();
        return this;
    }

    // ── Encode ────────────────────────────────────────────────

    /// <summary>
    /// Bind a visual channel to a data field.
    /// If <paramref name="field"/> starts with <c>"constant:"</c>, the remainder
    /// is treated as a literal constant value (e.g. <c>"constant:10"</c>).
    /// Prefer the <see cref="Encode(Channel, object)"/> overload for type-safe constants.
    /// </summary>
    public Chart Encode(Channel channel, string field)
    {
        ArgumentNullException.ThrowIfNull(field);

        const string prefix = "constant:";
        return field.StartsWith(prefix, StringComparison.Ordinal)
            ? SetEncode(channel, new ConstantEncode(field.Substring(prefix.Length)))
            : SetEncode(channel, new FieldEncode(field));
    }

    /// <summary>
    /// Encode a visual channel with a constant value. The value type depends on the channel:
    /// - For Color channel, provide a Godot Color or a hex string (e.g. "#ff0000").
    /// - For Size channel, provide a numeric value (int or float).
    /// - For Shape channel, provide a string (e.g. "circle", "square").
    /// - For other channels, the value is treated as a string label.
    /// </summary>
    public Chart Encode(Channel channel, object constant)
        => SetEncode(channel, new ConstantEncode(constant));

    /// <summary>
    /// Store one binding and invalidate what it changes.
    /// <para>
    /// Both <c>Encode</c> overloads go through here: the channel now refers to a different field (or to no
    /// field at all), so an auto-fitted scale has to go - otherwise <see cref="AutoFitScales"/> saw
    /// <c>_scales.Has(channel)</c> and kept the <i>previous</i> field's domain, leaving the legend and the
    /// coordinates mapped to data the chart no longer draws. A manually configured scale is kept: the
    /// caller asked for it explicitly.
    /// </para>
    /// </summary>
    private Chart SetEncode(Channel channel, IEncodeValue encode)
    {
        _encodes.Set(channel, encode);
        if (_autoFittedChannels.Remove(channel))
            _scales.Remove(channel);
        _layoutVersion++;
        return this;
    }

    // ── Scale override (optional) ─────────────────────────────
    /// <summary>
    /// Configure a scale explicitly for a channel. An explicit scale is never replaced by the
    /// automatic inference, and it survives later data changes.
    /// </summary>
    public Chart Scale(Channel channel, IScale scale)
    {
        ArgumentNullException.ThrowIfNull(scale);
        // Remember the channel as user-configured: InvalidateData() only drops auto-fitted scales,
        // so a manual scale survives later data updates.
        _autoFittedChannels.Remove(channel);
        _scales.Set(channel, scale);
        _layoutVersion++;
        return this;
    }

    /// <summary>
    /// Pin the domain of a channel's scale - G2's <c>xAxis.min/max</c>. The lock is applied after the
    /// automatic fit and survives later data changes, so two charts stay comparable (both 0…100) or one
    /// spike cannot squash everything else. It only affects a linear scale: pinning a categorical axis
    /// (categories, colours, shapes) is a no-op rather than an error, which is what makes it safe to call
    /// without knowing what the data turned out to be.
    /// </summary>
    /// <param name="channel">Channel whose scale is pinned.</param>
    /// <param name="min">Lower bound of the domain.</param>
    /// <param name="max">Upper bound of the domain (swapped with <paramref name="min"/> when reversed).</param>
    public Chart ScaleDomain(Channel channel, double min, double max)
    {
        if (max < min) (min, max) = (max, min);
        _domainLocks[channel] = (min, max);
        _layoutVersion++;
        return this;
    }

    // ── Animation ─────────────────────────────────────────────

    /// <summary>
    /// Set animation progress for entry animation [0,1].
    /// <para>
    /// <see cref="ChartTheme.EnableAnimation"/> is the theme-level switch: with it off the chart settles
    /// on the end state immediately (entry progress 1, i.e. <see cref="AnimationContext.Default"/>) and
    /// this call does not animate.
    /// </para>
    /// </summary>
    /// <param name="progress">
    /// 0 = start state (e.g. all marks at zero height), 1 = end state (fully rendered).
    /// The value is stored as-is; it is not clamped to [0,1].
    /// </param>
    /// <remarks>
    /// This overload drives the entry phase only; <see cref="Animate(AnimationContext?)"/> sets the whole
    /// context (entry, exit, opacity, hover scale) when a host animates those as well.
    /// </remarks>
    public Chart Animate(float progress)
    {
        if (!_theme.EnableAnimation)
        {
            _animationContext = AnimationContext.Default;
            return this;
        }

        // The AnimationContext is the single source of truth: marks read both
        // ctx.AnimationProgress and ctx.Animation.EntryProgress, and they must never disagree.
        _animationContext = new AnimationContext
        {
            EntryProgress           = progress,
            GlobalOpacity           = _animationContext.GlobalOpacity,
            HoverScale              = _animationContext.HoverScale,
            ExitProgress            = _animationContext.ExitProgress,
            DataTransitionProgress  = _animationContext.DataTransitionProgress,
            SeriesProgress          = _animationContext.SeriesProgress,
        };
        return this;
    }

    /// <summary>
    /// Set the full animation context (entry, exit, opacity, hover scale, etc.).
    /// <para>
    /// <see cref="ChartTheme.EnableAnimation"/> is the theme-level switch: with it off the chart settles
    /// on the end state instead - the call is the same as passing <see cref="AnimationContext.Default"/>
    /// (entry progress 1, global opacity 1), so a host that keeps driving a controller cannot animate a
    /// chart whose theme has animations disabled.
    /// </para>
    /// </summary>
    public Chart Animate(AnimationContext? context)
    {
        _animationContext = _theme.EnableAnimation ? context ?? AnimationContext.Default : AnimationContext.Default;
        return this;
    }

    // ── Axis configuration ────────────────────────────────────

    /// <summary>
    /// Configure the X axis (title, description, tooltip).
    /// </summary>
    public Chart XAxis(AxisConfig config)
    {
        _xAxisConfig = config;
        // The axis title reserves space in the plot rectangle, so a new (or edited) configuration has to
        // drop the layout caches - the geometry caches of marks are keyed on the layout version.
        InvalidateLayout();
        return this;
    }

    /// <summary>
    /// Configure the Y axis (title, description, tooltip).
    /// </summary>
    public Chart YAxis(AxisConfig config)
    {
        _yAxisConfig = config;
        InvalidateLayout();
        return this;
    }

    /// <summary>
    /// Configure the secondary Y axis on the right side (title, description, tooltip).
    /// Only rendered when at least one mark uses <see cref="Channel.Y2"/>.
    /// </summary>
    public Chart Y2Axis(AxisConfig config)
    {
        _y2AxisConfig = config;
        InvalidateLayout();
        return this;
    }

    /// <summary>
    /// Enable and configure the chart legend.
    /// Legend items are auto-generated from the Color channel's scale domain.
    /// </summary>
    public Chart Legend(LegendConfig config)
    {
        _legendConfig = config;
        // The config feeds both the legend layout and the space the plot reserves for it, so a new (or
        // edited) configuration invalidates the layout caches instead of leaving a stale legend - and
        // stale legend click regions - behind.
        InvalidateLayout();
        return this;
    }

    // ── State query (read-only) ───────────────────────────────

    /// <summary>
    /// Get the current color scale domain (series names) and their mapped colors.
    /// Useful for building external legend UI. Returns empty if no Color channel is encoded.
    /// </summary>
    public IReadOnlyList<(string Key, Color Color)> GetSeriesInfo()
    {
        if (_scales.TryGet(Channel.Color) is not ICategoricalColorScale cs || cs.Domain.Count == 0)
            return [];
        var result = new (string, Color)[cs.Domain.Count];
        for (int i = 0; i < cs.Domain.Count; i++)
            result[i] = (cs.Domain[i], cs.MapColor(cs.Domain[i]));
        return result;
    }

    /// <summary>Currently focused series key. Null = no focus.</summary>
    public string? CurrentFocusedSeries => _focusedSeries;

    /// <summary>Currently selected row index. -1 = none.</summary>
    public int CurrentSelectedRowIndex => _selectedRowIndex;

    /// <summary>Currently hovered row index. -1 = none.</summary>
    public int CurrentHoveredRowIndex => _hoveredRowIndex;

    /// <summary>Last computed plot area. Null before first Render().</summary>
    public PlotArea? CurrentPlotArea => _lastPlot;

    /// <summary>
    /// The smallest size this chart can be drawn at and still be readable: the title, the legend, the axis
    /// labels and axis titles, plus <see cref="MinimumPlotSize"/> for the plot itself. Recomputed with the
    /// layout, so a theme or content change moves it.
    /// <para>
    /// It is an estimate that errs generous: below it the axis starts thinning its labels (see
    /// <see cref="ChartTheme.TickLabelSpacing"/>), it does not mean drawing breaks.
    /// </para>
    /// <para>
    /// Before the first <see cref="Render"/> this is <see cref="EstimateMinimumSize"/> - the reservations a
    /// frame cannot measure yet are estimated - so a host that has to report a size before a chart exists
    /// (<see cref="ChartView"/> during the first layout of a container) still reports something usable rather
    /// than zero. A frame replaces it with the measured value.
    /// </para>
    /// </summary>
    public Vector2 MinimumSize => _minimumSize.X > 0f ? _minimumSize : EstimatedMinimumSize;

    /// <summary>Plot rectangle a <see cref="MinimumSize"/> keeps; everything else in it is decorations.</summary>
    public static Vector2 MinimumPlotSize => new(120f, 80f);

    /// <summary>
    /// The rectangle the content is laid out in for a plot area of <paramref name="full"/>: that rectangle
    /// itself while nothing shapes the content, otherwise the largest rectangle of
    /// <see cref="EffectivePlotAspect"/> inside it, placed by the two alignment properties.
    /// </summary>
    /// <param name="full">Plot area the decorations were laid out against.</param>
    private PlotArea ContentBox(PlotArea full)
    {
        float aspect = EffectivePlotAspect();
        // Defensive, unreachable by contract: EffectivePlotAspect only answers 0 or a finite positive ratio.
        if (!(aspect > 0f) || !float.IsFinite(aspect)) return full;

        // Floored at one pixel per side, the floor BuildPlot itself keeps: an extreme ratio (a very wide or a
        // very tall box) would otherwise hand the marks a rectangle of zero height or width, and the axes would
        // all map onto a single point.
        float width = MathF.Max(1f, MathF.Min(full.Width, full.Height * aspect));
        float height = MathF.Max(1f, width / aspect);
        float x = PlotAlignHorizontal switch
        {
            HorizontalAlignment.Left => full.X,
            HorizontalAlignment.Right => full.X + full.Width - width,
            _ => full.X + (full.Width - width) / 2f,
        };
        float y = PlotAlignVertical switch
        {
            VerticalAlignment.Top => full.Y,
            VerticalAlignment.Bottom => full.Y + full.Height - height,
            _ => full.Y + (full.Height - height) / 2f,
        };
        return new PlotArea(x, y, width, height);
    }

    /// <summary>
    /// The shape the content is given: <see cref="PlotAspectRatio"/> when the host decided (a positive value,
    /// or 0/NaN for "fill the plot"), otherwise what the marks ask for - and only while they agree, because one
    /// mark that fills the rectangle is reason enough to keep it.
    /// </summary>
    private float EffectivePlotAspect()
    {
        if (PlotAspectRatio is { } decided) return decided > 0f && float.IsFinite(decided) ? decided : 0f;

        float? agreed = null;
        foreach (var mark in _marks)
        {
            if (mark.PreferredAspectRatio is not { } wanted || !(wanted > 0f) || !float.IsFinite(wanted)) return 0f;
            if (agreed is null) agreed = wanted;
            else if (MathF.Abs(agreed.Value - wanted) > 1e-3f) return 0f;
        }
        return agreed ?? 0f;
    }

    private Vector2 _minimumSize;

    /// <summary>Whether the too-small warning has been issued for the current layout (it is not issued per frame).</summary>
    private bool _warnedAboutMinimumSize;

    /// <summary><see cref="MinimumSize"/> as it can be told before a frame has measured anything.</summary>
    private Vector2 EstimatedMinimumSize
        => EstimateMinimumSize(_theme, Title != null, _xAxisConfig?.Title != null, _yAxisConfig?.Title != null,
            _legendConfig?.Position ?? LegendPosition.None, ThemedLabelFontSize(_theme),
            // The chart's own paddings: a host that moved them gets an estimate that matches its layout.
            // The legend's padding comes from the config it was handed, so the estimate and the measuring side
            // add the same amount (the constant is only the fallback for a chart without a config).
            PaddingLeft, PaddingRight, PaddingTop, PaddingBottom,
            _legendConfig?.Padding ?? EstimatedLegendRowPadding);

    /// <summary>Label font size a theme asks for, or the library default when it leaves the size at zero.</summary>
    internal static float ThemedLabelFontSize(ChartTheme theme)
        => theme.LabelFontSize > 0f ? theme.LabelFontSize : FontSettings.Default.Size;

    /// <summary>
    /// <see cref="MinimumSize"/> before anything has been measured: the theme's own reservations (padding, the
    /// title band, the axis title bands, one legend row) plus <see cref="MinimumPlotSize"/>, with the two parts
    /// a frame measures - the Y label column and the legend height - estimated instead.
    /// <para>
    /// It errs generous on purpose: reporting too little is what a container turns into a collapsed node
    /// (the chart does not exist before the first layout, so nothing else would tell it a size), while a few
    /// pixels too much only reserve a little more room for the one frame before the measured value arrives.
    /// </para>
    /// </summary>
    /// <param name="theme">Theme the reservations come from.</param>
    /// <param name="hasTitle">Whether the chart has a title (its band is reserved).</param>
    /// <param name="hasXAxisTitle">Whether the X axis has a title (it needs a line of its own).</param>
    /// <param name="hasYAxisTitle">Whether the Y axis has a title (it needs a column of its own).</param>
    /// <param name="legend">Legend position (top/bottom legends reserve a row of height).</param>
    /// <param name="labelFontSize">Label font size the tick labels are drawn with.</param>
    /// <param name="legendPadding">
    /// Padding a top/bottom legend adds around its text; pass the legend's own <see cref="LegendConfig.Padding"/>
    /// when there is one, so the estimate and the measuring side add the same amount.
    /// </param>
    /// <param name="paddingLeft">Chart padding on the left; the defaults are used when the chart's own are not known yet.</param>
    /// <param name="paddingRight">Chart padding on the right.</param>
    /// <param name="paddingTop">Chart padding at the top.</param>
    /// <param name="paddingBottom">Chart padding at the bottom.</param>
    internal static Vector2 EstimateMinimumSize(ChartTheme theme, bool hasTitle, bool hasXAxisTitle,
        bool hasYAxisTitle, LegendPosition legend, float labelFontSize,
        float paddingLeft = ChartDefaults.PaddingLeft, float paddingRight = ChartDefaults.PaddingRight,
        float paddingTop = ChartDefaults.PaddingTop, float paddingBottom = ChartDefaults.PaddingBottom,
        float legendPadding = EstimatedLegendRowPadding)
    {
        float labelLine = LabelLineHeight(labelFontSize);
        // Five characters at the label font ("1200", "-40.5", "1.2k"), measured through the axis' own
        // reservation formula so the two cannot drift apart.
        float labelColumn = DefaultRenderers.AxisLabelReservedWidth(theme, labelFontSize * 0.6f * 5f);
        float titleLeftPad = hasYAxisTitle ? AxisTitleBand(labelLine, theme, withLabelClearance: true) : 0f;
        bool legendRow = legend is LegendPosition.Top or LegendPosition.Bottom;

        return ComposeMinimumSize(
            paddingLeft, paddingRight, paddingTop, paddingBottom,
            MathF.Max(titleLeftPad, labelColumn + titleLeftPad - paddingLeft),
            0f,
            hasXAxisTitle ? AxisTitleBand(labelLine, theme) : 0f,
            labelColumn, 0f,
            hasTitle ? theme.TitleReservedHeight : 0f,
            legendRow ? labelLine + 2f * legendPadding : 0f,
            labelLine, hasXAxisTitle);
    }

    /// <summary>Height of one line of label text at <paramref name="fontSize"/> (the axis labels' own font size).</summary>
    private static float LabelLineHeight(float fontSize) => fontSize * FontSettings.Default.LineHeightMultiplier;

    /// <summary>
    /// Height one axis title band needs: a line of label text plus the margin the title renderer leaves at the
    /// chart edge, and - on a side an axis shares with its tick labels, which is the case for Y - the clearance
    /// that keeps the title off the label column (<see cref="TitleLabelClearance"/>).
    /// <para>
    /// The frame that measures the band with the real themed font and the estimate that runs before anything has
    /// been measured both go through here: they used to add the same terms up on their own, so a theme edit could
    /// move one of the two and leave the other behind.
    /// </para>
    /// </summary>
    /// <param name="labelLine">Height of one line of label text (<see cref="LabelLineHeight"/>).</param>
    /// <param name="theme">Theme the title margin comes from.</param>
    /// <param name="withLabelClearance">Whether the tick labels share this side of the plot (Y does, X does not).</param>
    private static float AxisTitleBand(float labelLine, ChartTheme theme, bool withLabelClearance = false)
        => labelLine + theme.AxisTitleMargin + (withLabelClearance ? TitleLabelClearance : 0f);

    /// <summary>
    /// <see cref="MinimumSize"/> assembled from its parts. The one place the sum lives on purpose: the frame
    /// that measured everything and the estimate that had nothing to measure must not drift apart.
    /// </summary>
    /// <param name="paddingLeft">Chart padding on the left.</param>
    /// <param name="paddingRight">Chart padding on the right.</param>
    /// <param name="paddingTop">Chart padding at the top.</param>
    /// <param name="paddingBottom">Chart padding at the bottom.</param>
    /// <param name="extraLeftPad">Room the Y axis title band and the label column need beyond the padding.</param>
    /// <param name="extraRightPad">Room the Y2 axis needs beyond the padding.</param>
    /// <param name="extraBottomPad">Room the X axis title needs beyond the padding.</param>
    /// <param name="yLabelColumn">Width the Y axis label column needs.</param>
    /// <param name="y2Column">Width the Y2 axis label column needs (0 without a second axis).</param>
    /// <param name="titleBand">Height the title reserves (0 without a title).</param>
    /// <param name="legendHeight">Height a top/bottom legend needs (0 for any other position).</param>
    /// <param name="labelLine">Height of one line of axis label text.</param>
    /// <param name="hasXAxisTitle">Whether the X axis title needs a second line.</param>
    internal static Vector2 ComposeMinimumSize(float paddingLeft, float paddingRight, float paddingTop,
        float paddingBottom, float extraLeftPad, float extraRightPad, float extraBottomPad, float yLabelColumn,
        float y2Column, float titleBand, float legendHeight, float labelLine, bool hasXAxisTitle)
        => new(
            // Ceiled to whole pixels: a fractional minimum cannot be reached by a real node size, so setting a
            // node exactly to it would still be 0.4 px short and trip the too-small warning.
            MathF.Ceiling(paddingLeft + paddingRight + extraLeftPad + extraRightPad + yLabelColumn + y2Column
                + MinimumPlotSize.X),
            MathF.Ceiling(paddingTop + paddingBottom + extraBottomPad + MinimumPlotSize.Y + titleBand
                + legendHeight + labelLine * (hasXAxisTitle ? 2f : 1f)));

    /// <summary>
    /// Padding an estimated legend row adds around its text when the caller does not hand over the legend's own
    /// (<see cref="LegendConfig.Padding"/>'s default). A chart that has a <see cref="LegendConfig"/> passes its
    /// own padding instead: the estimate used to add this constant's 6 while the measuring side added the
    /// configured value, so a page with another padding got an estimate and a measurement that disagreed.
    /// </summary>
    internal const float EstimatedLegendRowPadding = 6f;

    /// <summary>
    /// Clearance between the axis title band and the tick label column, shared by the layout and the estimate.
    /// Without it the widest label (the seven-character axis of the performance page, for instance) ends exactly
    /// where the rotated title ends and the two touch on screen.
    /// </summary>
    private const float TitleLabelClearance = 6f;

    /// <summary>
    /// Render data (post-transform) as it is right now: a <b>copy</b> of the row list, so the caller can
    /// keep it while the chart keeps rendering. Read-only in the sense that the caller cannot add or remove
    /// rows - the rows themselves are the live <see cref="DataRow"/> instances the marks read.
    /// <para>
    /// It used to hand out <c>AsReadOnly()</c> over the chart's own list, which is not a snapshot: the
    /// "copy" changed whenever the chart's data did, and feeding it back
    /// (<c>chart.Data(chart.GetRenderDataSnapshot())</c>) cleared the list it was enumerating, so the chart
    /// ended up with no rows at all.
    /// </para>
    /// </summary>
    public IReadOnlyList<DataRow> GetRenderDataSnapshot() => [.. GetRenderData()];

    // ── IDisposable ───────────────────────────────────────────

    /// <summary>
    /// Dispose the chart. The injected canvas is only disposed when this chart was told that it
    /// owns it (<c>ownsCanvas: true</c> in the constructor); a shared canvas stays alive, because
    /// several charts commonly render into the same canvas (e.g. one chart per tab).
    /// <para>Calling this more than once is a no-op: an owned canvas is released exactly once.</para>
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        // Drop the theme hook: a disposed chart must not be kept reachable by a resource that is still
        // alive, and it has nothing left to invalidate.
        ObserveTheme(null);
        // The cached layer is an image of this chart's canvas: release it with the chart instead of waiting
        // for the finalizer of a handle nobody can reach any more.
        ReleaseLayer();
        if (_ownsCanvas)
            _canvas.Dispose();
        GC.SuppressFinalize(this);
    }

}