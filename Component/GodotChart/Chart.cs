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
    public string? Title { get; set; }

    /// <summary>Detailed description shown in tooltip when hovering over the axis area.</summary>
    public string? Description { get; set; }

    /// <summary>Unit label (e.g. "USD", "ms").</summary>
    public string? Unit { get; set; }

    /// <summary>
    /// Custom rich tooltip content builder for axis hover.
    /// If null, a default tooltip is built from Title + Description + Unit.
    /// </summary>
    public Func<IReadOnlyList<TooltipLine>>? TooltipBuilder { get; set; }
}

/// <summary>Legend position relative to the chart plot area.</summary>
public enum LegendPosition { Top, Bottom, Left, Right, None }

/// <summary>
/// Configuration for the chart legend. When enabled, the legend auto-generates
/// entries from the Color channel's scale domain.
/// </summary>
public class LegendConfig
{
    /// <summary>Position of the legend (default Top).</summary>
    public LegendPosition Position { get; set; } = LegendPosition.Top;

    /// <summary>Horizontal spacing between legend items (px).</summary>
    public float ItemSpacing { get; set; } = 16f;

    /// <summary>Color swatch size (px).</summary>
    public float SwatchSize { get; set; } = 10f;

    /// <summary>Padding around the legend area (px).</summary>
    public float Padding { get; set; } = 6f;
}

public partial class Chart : IDisposable
{
    /// <summary>
    /// Shared margin (in pixels) between the chart edge and axis title text.
    /// Used symmetrically for both X and Y axis titles so visual gaps are consistent.
    /// Referenced by <see cref="DefaultRenderers"/> for consistent layout.
    /// </summary>
    internal float AxisTitleMargin => _theme.AxisTitleMargin;

    /// <summary>Height reserved for the title text area in pixels.</summary>
    private float TitleReservedHeight => _theme.TitleReservedHeight;

    /// <summary>Width reserved for Y2 axis labels in pixels.</summary>
    private float Y2LabelReservedWidth => _theme.Y2LabelReservedWidth;

    private readonly ICanvas2D        _canvas;
    private readonly List<DataRow>    _data    = new();
    private readonly List<Mark>       _marks   = new();
    private readonly EncodeSet        _encodes = new();
    private readonly ScaleSet         _scales  = new();
    private readonly List<IDataTransform> _transforms = new();

    // Track which scales were auto-fitted (vs manually set) so they can be cleared on data change
    private readonly HashSet<Channel> _autoFittedChannels = new();

    // Monotonically increasing version for cache invalidation
    private int _dataVersion;

    // Combined version: incremented when data, encode, scale, or layout changes
    private int _layoutVersion;

    // Cached check: true when no Cartesian marks exist (skip grid/axes/crosshair)
    private bool? _skipCartesianDecorations;

    // Cached transformed data, rebuilt when data version changes
    private List<DataRow>? _transformedData;

    // Series visibility control
    private HashSet<string>? _hiddenSeries;
    private int _transformedDataVersion = -1;

    // Cached legend layout to avoid recomputing in both Render and HitTest
    internal List<LegendItemLayout>? _cachedLegendItems;

    // Layout
    public float PaddingLeft   { get; set; } = 50f;
    public float PaddingRight  { get; set; } = 20f;
    public float PaddingTop    { get; set; } = 20f;
    public float PaddingBottom { get; set; } = 40f;
    public float Width         { get; set; } = 600f;
    public float Height        { get; set; } = 400f;

    // Theme
    private ChartTheme _theme = ChartTheme.Dark();

    // Visual properties — null means "use theme default"
    private Color? _backgroundColor;
    private Color? _gridColor;
    private Color? _axisColor;

    public Color BackgroundColor
    {
        get => _backgroundColor ?? _theme.BackgroundColor;
        set => _backgroundColor = value;
    }
    public Color GridColor
    {
        get => _gridColor ?? _theme.GridColor;
        set => _gridColor = value;
    }
    public Color AxisColor
    {
        get => _axisColor ?? _theme.AxisColor;
        set => _axisColor = value;
    }

    public float  OffsetX { get; set; } = 0f;
    public float  OffsetY { get; set; } = 0f;
    public string? Title   { get; set; } = null;

    // Interaction state
    private float            _animationProgress = 1f;
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
    private PlotArea? _lastPlot;

    /// <summary>Expose marks list for external hit testing.</summary>
    internal IReadOnlyList<Mark> Marks => _marks;

    /// <summary>Expose last computed plot area.</summary>
    internal PlotArea? LastPlot => _lastPlot;

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
    public int WindowSize { get; set; } = 0;

    public Chart(ICanvas2D canvas) => _canvas = canvas;

    /// <summary>Set the chart theme for centralized style control.</summary>
    public Chart Theme(ChartTheme theme) { _theme = theme; return this; }

    // ── Mark ──────────────────────────────────────────────────
    public Chart Mark(Mark mark)
    { _marks.Add(mark); _skipCartesianDecorations = null; return this; }

    public Chart Mark<T>() where T : Mark, new()
    { _marks.Add(new T()); _skipCartesianDecorations = null; return this; }

    /// <summary>
    /// Apply an action to every mark currently registered on this chart.
    /// Useful for batch-configuring properties across all marks.
    /// </summary>
    public Chart ApplyToAllMarks(Action<Mark> action)
    {
        foreach (var mark in _marks) action(mark);
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
        const string prefix = "constant:";
        if (field.StartsWith(prefix, StringComparison.Ordinal))
            _encodes.Set(channel, new ConstantEncode(field.Substring(prefix.Length)));
        else
            _encodes.Set(channel, new FieldEncode(field));
        _layoutVersion++;
        return this;
    }

    /// <summary>
    /// Encode a visual channel with a constant value. The value type depends on the channel:
    /// - For Color channel, provide a Godot Color or a hex string (e.g. "#ff0000").
    /// - For Size channel, provide a numeric value (int or float).
    /// - For Shape channel, provide a string (e.g. "circle", "square").
    /// - For other channels, the value is treated as a string label.
    /// </summary>
    public Chart Encode(Channel channel, object constant)
    { _encodes.Set(channel, new ConstantEncode(constant)); _layoutVersion++; return this; }

    // ── Scale override (optional) ─────────────────────────────
    public Chart Scale(Channel channel, IScale scale)
    { _scales.Set(channel, scale); _layoutVersion++; return this; }

    // ── Animation ─────────────────────────────────────────────

    /// <summary>
    /// Set animation progress for entry animation [0,1].
    /// <param name="progress">0 = start state (e.g. all marks at zero height), 1 = end state (fully rendered).</param>
    /// </summary>
    public Chart Animate(float progress)
    { _animationProgress = progress; return this; }

    /// <summary>
    /// Set the full animation context (entry, exit, opacity, hover scale, etc.).
    /// </summary>
    public Chart Animate(AnimationContext? context)
    { _animationContext = context ?? AnimationContext.Default; return this; }

    // ── Axis configuration ────────────────────────────────────

    /// <summary>
    /// Configure the X axis (title, description, tooltip).
    /// </summary>
    public Chart XAxis(AxisConfig config)
    { _xAxisConfig = config; return this; }

    /// <summary>
    /// Configure the Y axis (title, description, tooltip).
    /// </summary>
    public Chart YAxis(AxisConfig config)
    { _yAxisConfig = config; return this; }

    /// <summary>
    /// Configure the secondary Y axis on the right side (title, description, tooltip).
    /// Only rendered when at least one mark uses <see cref="Channel.Y2"/>.
    /// </summary>
    public Chart Y2Axis(AxisConfig config)
    { _y2AxisConfig = config; return this; }

    /// <summary>
    /// Enable and configure the chart legend.
    /// Legend items are auto-generated from the Color channel's scale domain.
    /// </summary>
    public Chart Legend(LegendConfig config)
    { _legendConfig = config; return this; }

    // ── State query (read-only) ───────────────────────────────

    /// <summary>
    /// Get the current color scale domain (series names) and their mapped colors.
    /// Useful for building external legend UI. Returns empty if no Color channel is encoded.
    /// </summary>
    public IReadOnlyList<(string Key, Color Color)> GetSeriesInfo()
    {
        if (_scales.TryGet(Channel.Color) is not ColorScale cs || cs.Domain.Count == 0)
            return Array.Empty<(string, Color)>();
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

    /// <summary>Render data snapshot (post-transform). Read-only.</summary>
    public IReadOnlyList<DataRow> GetRenderDataSnapshot() => GetRenderData().AsReadOnly();

    // ── IDisposable ───────────────────────────────────────────

    /// <summary>
    /// Dispose the chart and its owned canvas (including background threads and native resources).
    /// </summary>
    public void Dispose()
    {
        _canvas.Dispose();
        GC.SuppressFinalize(this);
    }

}