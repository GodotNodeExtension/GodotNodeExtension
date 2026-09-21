using System.Collections.Generic;
using System.Linq;
using Godot;

namespace GodotNodeExtension.Component.GodotChart;

/// <summary>
/// Centralized theme configuration for all chart visual styles.
/// Attach to a Chart via <see cref="Chart.Theme(ChartTheme)"/>.
/// Properties are organized with <c>[ExportGroup]</c> for clear Inspector display.
/// <para>
/// <see cref="ToolAttribute"/> is required, not cosmetic: without it the editor only creates a
/// placeholder script instance, so a theme resource that is created, loaded or duplicated in the
/// editor is a bare <see cref="Resource"/> to C# - its exported properties are neither shown nor
/// editable, and assigning it to a <see cref="ChartView.CustomTheme"/> throws an
/// <c>InvalidCastException</c> in the generated property setter.
/// </para>
/// <para>
/// The two static palettes, <see cref="DefaultPalette"/> and <see cref="DefaultSequentialGradient"/>,
/// are <b>read-only by copy</b>: every read returns a fresh array, so writing into the result can
/// never affect <see cref="Default"/>, an existing theme or another caller. A theme's own
/// <see cref="Palette"/> / <see cref="SequentialGradient"/> are per-instance copies for the same
/// reason - change those, not the static defaults.
/// </para>
/// </summary>
[Tool]
[GlobalClass]
public partial class ChartTheme : Resource
{
    /// <summary>
    /// Assign an exported value and notify listeners. The editor does not emit <see cref="Resource.Changed"/>
    /// for a C# <c>[Export]</c> assignment, so a theme edited in the inspector (or from code) would otherwise
    /// leave every preview - and every chart using the resource - stale.
    /// </summary>
    private void Set<T>(ref T field, T value)
    {
        if (ValuesEqual(field, value)) return;

        field = value;
        EmitChanged();
    }

    /// <summary>
    /// Value equality that also works for the two array-valued exports: the editor assigns a fresh array,
    /// so reference equality would notify on every reassignment. Editing an array <i>in place</i> (one
    /// element at a time) still needs an explicit <see cref="Resource.EmitChanged"/>.
    /// </summary>
    private static bool ValuesEqual<T>(T left, T right)
    {
        if (left is null || right is null) return left is null && right is null;

        if (left is System.Array array && right is System.Array other)
            return array.Length == other.Length && array.Cast<object>().SequenceEqual(other.Cast<object>());

        return EqualityComparer<T>.Default.Equals(left, right);
    }

    // ═══════════════════════════════════════════════════════
    //  Color Palette
    // ═══════════════════════════════════════════════════════

    /// <summary>Default categorical color palette used by <see cref="ColorScale"/>.</summary>
    [ExportGroup("Color Palette")]
    [Export] public Color[] Palette { get => _palette; set => Set(ref _palette, value); }

    // One array per theme: DefaultPalette already hands out a fresh copy per read, so the field can
    // take it as-is instead of cloning a clone - writing into it stays local to this instance.
    private Color[] _palette = DefaultPalette;

    /// <summary>Sequential gradient colors for continuous data (<see cref="SequentialColorScale"/>).</summary>
    [Export] public Color[] SequentialGradient { get => _sequentialGradient; set => Set(ref _sequentialGradient, value); }

    private Color[] _sequentialGradient = DefaultSequentialGradient;

    // ═══════════════════════════════════════════════════════
    //  Chart Frame
    // ═══════════════════════════════════════════════════════

    /// <summary>Chart background fill color.</summary>
    [ExportGroup("Chart Frame")]
    [Export] public Color BackgroundColor { get => _backgroundColor; set => Set(ref _backgroundColor, value); }

    private Color _backgroundColor = new(0.08f, 0.08f, 0.12f);

    /// <summary>Background rectangle corner radius in pixels.</summary>
    [Export] public float BackgroundCornerRadius { get => _backgroundCornerRadius; set => Set(ref _backgroundCornerRadius, value); }

    private float _backgroundCornerRadius = 8f;

    /// <summary>Grid line color.</summary>
    [Export] public Color GridColor { get => _gridColor; set => Set(ref _gridColor, value); }

    private Color _gridColor = new(1f, 1f, 1f, 0.08f);

    /// <summary>Grid line stroke width in pixels.</summary>
    [Export] public float GridLineWidth { get => _gridLineWidth; set => Set(ref _gridLineWidth, value); }

    private float _gridLineWidth = 1f;

    /// <summary>Axis line color.</summary>
    [Export] public Color AxisColor { get => _axisColor; set => Set(ref _axisColor, value); }

    private Color _axisColor = new(1f, 1f, 1f, 0.4f);

    /// <summary>Main axis line stroke width in pixels.</summary>
    [Export] public float AxisLineWidth { get => _axisLineWidth; set => Set(ref _axisLineWidth, value); }

    private float _axisLineWidth = 2f;

    // ═══════════════════════════════════════════════════════
    //  Layout
    // ═══════════════════════════════════════════════════════

    /// <summary>Margin between chart edge and axis title text in pixels.</summary>
    [ExportGroup("Layout")]
    [Export] public float AxisTitleMargin { get => _axisTitleMargin; set => Set(ref _axisTitleMargin, value); }

    private float _axisTitleMargin = 6f;

    /// <summary>Height reserved for the title text area in pixels.</summary>
    [Export] public float TitleReservedHeight { get => _titleReservedHeight; set => Set(ref _titleReservedHeight, value); }

    private float _titleReservedHeight = 24f;

    /// <summary>Width reserved for Y2 axis labels in pixels.</summary>
    [Export] public float Y2LabelReservedWidth { get => _y2LabelReservedWidth; set => Set(ref _y2LabelReservedWidth, value); }

    private float _y2LabelReservedWidth = 35f;

    /// <summary>Title text Y offset from top of chart in pixels.</summary>
    [Export] public float TitleYOffset { get => _titleYOffset; set => Set(ref _titleYOffset, value); }

    private float _titleYOffset = 16f;

    /// <summary>X-axis label Y offset below plot area bottom in pixels.</summary>
    [Export] public float XAxisLabelOffset { get => _xAxisLabelOffset; set => Set(ref _xAxisLabelOffset, value); }

    private float _xAxisLabelOffset = 15f;

    /// <summary>Gap between Y-axis labels and plot edge in pixels.</summary>
    [Export] public float YAxisLabelGap { get => _yAxisLabelGap; set => Set(ref _yAxisLabelGap, value); }

    private float _yAxisLabelGap = 5f;

    // ═══════════════════════════════════════════════════════
    //  Typography
    // ═══════════════════════════════════════════════════════

    /// <summary>Default for <see cref="FallbackTickCount"/>, shared with the tick ladder's own default.</summary>
    internal const int FallbackTickCountDefault = 5;

    /// <summary>Number of ticks used when a scale cannot provide a nice tick count.</summary>
    [Export] public int FallbackTickCount { get => _fallbackTickCount; set => Set(ref _fallbackTickCount, value); }

    private int _fallbackTickCount = FallbackTickCountDefault;

    /// <summary>
    /// Pixels an axis gives one tick label. The number of ticks an axis draws is derived from its own length
    /// divided by this, so a short axis (a zoomed X window, a flat Y axis) gets fewer labels instead of a
    /// column of overlapping ones.
    /// </summary>
    [Export] public float TickLabelSpacing { get => _tickLabelSpacing; set => Set(ref _tickLabelSpacing, value); }

    /// <summary>
    /// Rotation of the X-axis tick labels in degrees; 0 (the default) keeps them horizontal. Rotating them is
    /// what lets a category axis with long names show every name instead of every second one.
    /// </summary>
    [Export] public float XAxisLabelRotation { get => _xAxisLabelRotation; set => Set(ref _xAxisLabelRotation, value); }

    private float _xAxisLabelRotation;

    private float _tickLabelSpacing = 72f;

    /// <summary>Fewest ticks an axis draws, however short it is (the floor of the automatic count).</summary>
    [Export] public int MinTickCount { get => _minTickCount; set => Set(ref _minTickCount, value); }

    private int _minTickCount = 6;

    /// <summary>Most ticks an axis draws, however long it is (the ceiling of the automatic count).</summary>
    [Export] public int MaxTickCount { get => _maxTickCount; set => Set(ref _maxTickCount, value); }

    private int _maxTickCount = 24;

    /// <summary>Baseline nudge for a bottom-anchored axis title, as a factor of the font size.</summary>
    [Export] public float AxisTitleBaselineNudge { get => _axisTitleBaselineNudge; set => Set(ref _axisTitleBaselineNudge, value); }

    private float _axisTitleBaselineNudge = 0.2f;

    /// <summary>Font family name to look up a system font by, used when no Godot <see cref="Font"/> resource
    /// is set. Null uses the system default.</summary>
    [ExportGroup("Typography")]
    [Export] public string? FontFamily { get => _fontFamily; set => Set(ref _fontFamily, value); }

    private string? _fontFamily;

    /// <summary>Default Godot Font resource. Null uses system default.</summary>
    [Export] public Font? Font { get => _font; set => Set(ref _font, value); }

    private Font? _font;

    /// <summary>Title text color.</summary>
    [Export] public Color TitleColor { get => _titleColor; set => Set(ref _titleColor, value); }

    private Color _titleColor = new(1f, 1f, 1f, 0.85f);

    /// <summary>Title font size in pixels.</summary>
    [Export] public float TitleFontSize { get => _titleFontSize; set => Set(ref _titleFontSize, value); }

    private float _titleFontSize = 13f;

    /// <summary>Axis label text color.</summary>
    [Export] public Color LabelColor { get => _labelColor; set => Set(ref _labelColor, value); }

    private Color _labelColor = new(1f, 1f, 1f, 0.6f);

    /// <summary>Axis label font size in pixels.</summary>
    [Export] public float LabelFontSize { get => _labelFontSize; set => Set(ref _labelFontSize, value); }

    private float _labelFontSize = 13f;

    /// <summary>Data label color drawn on marks (pie slices, treemap cells, etc.).</summary>
    [Export] public Color DataLabelColor { get => _dataLabelColor; set => Set(ref _dataLabelColor, value); }

    private Color _dataLabelColor = new(1f, 1f, 1f, 0.9f);

    // ═══════════════════════════════════════════════════════
    //  Mark Defaults
    // ═══════════════════════════════════════════════════════

    /// <summary>Fallback fill color for marks when no Color channel is encoded.</summary>
    [ExportGroup("Mark Defaults")]
    [Export] public Color DefaultMarkColor { get => _defaultMarkColor; set => Set(ref _defaultMarkColor, value); }

    private Color _defaultMarkColor = new(0.29f, 0.59f, 0.98f);

    /// <summary>
    /// Default corner radius for rectangular marks. <see cref="ChartView"/> copies it onto the mark it
    /// builds for a kind whose mark carries a corner radius (bar, box, candlestick, funnel, heatmap,
    /// timeline, treemap) before <see cref="ChartView.ConfigureMark"/> runs, so a mark value set
    /// explicitly - by the configure callback or on a hand-built <see cref="Chart"/> - still wins.
    /// </summary>
    [Export] public float CornerRadius { get => _cornerRadius; set => Set(ref _cornerRadius, value); }

    private float _cornerRadius = 3f;

    /// <summary>
    /// Default stroke width for line-based marks. <see cref="ChartView"/> copies it onto the mark it
    /// builds for a kind whose mark carries a stroke width (line/area, radar, violin) before
    /// <see cref="ChartView.ConfigureMark"/> runs, so a mark value set explicitly still wins.
    /// </summary>
    [Export] public float StrokeWidth { get => _strokeWidth; set => Set(ref _strokeWidth, value); }

    private float _strokeWidth = 2f;

    // ═══════════════════════════════════════════════════════
    //  Selection & Hover
    // ═══════════════════════════════════════════════════════

    /// <summary>Selection highlight ring color.</summary>
    [ExportGroup("Selection & Hover")]
    [Export] public Color SelectionColor { get => _selectionColor; set => Set(ref _selectionColor, value); }

    private Color _selectionColor = new(1f, 1f, 1f, 0.8f);

    /// <summary>Selection highlight ring stroke width in pixels.</summary>
    [Export] public float SelectionStrokeWidth { get => _selectionStrokeWidth; set => Set(ref _selectionStrokeWidth, value); }

    private float _selectionStrokeWidth = 2f;

    /// <summary>Hover brightness multiplier (1.0 = no change). Applied via BrightenColor.</summary>
    [Export] public float HoverBrighten { get => _hoverBrighten; set => Set(ref _hoverBrighten, value); }

    private float _hoverBrighten = 1.2f;

    /// <summary>Non-focused series opacity multiplier during series focus.</summary>
    [Export] public float UnfocusedOpacity { get => _unfocusedOpacity; set => Set(ref _unfocusedOpacity, value); }

    private float _unfocusedOpacity = 0.15f;

    /// <summary>Hover size scale for bars, dots, etc.</summary>
    [Export] public float HoverScale { get => _hoverScale; set => Set(ref _hoverScale, value); }

    private float _hoverScale = 1.05f;

    // ═══════════════════════════════════════════════════════
    //  Polar / Segment
    // ═══════════════════════════════════════════════════════

    /// <summary>Border color between polar chart segments (pie slices, sunburst arcs).</summary>
    [ExportGroup("Polar / Segment")]
    [Export] public Color SegmentBorderColor { get => _segmentBorderColor; set => Set(ref _segmentBorderColor, value); }

    private Color _segmentBorderColor = new(0.08f, 0.08f, 0.12f);

    /// <summary>Border stroke width between segments. Set 0 to hide borders.</summary>
    [Export] public float SegmentBorderWidth { get => _segmentBorderWidth; set => Set(ref _segmentBorderWidth, value); }

    private float _segmentBorderWidth = 1f;

    // ── Layout / measurement knobs (previously hardcoded in the renderers) ──

    // No theme-level arc/ring gap exists on purpose: the gap between arcs (Marks.ChordMark.ArcGap,
    // Marks.SunburstMark.ArcGap) and between the sunburst rings (Marks.SunburstMark.RingGap) is a
    // per-mark setting, and a theme default that no renderer ever read used to sit here and disagree
    // with the mark values.

    /// <summary>Hover explode offset ratio relative to outer radius for pie/donut charts.</summary>
    [Export] public float PieExplodeRatio { get => _pieExplodeRatio; set => Set(ref _pieExplodeRatio, value); }

    private float _pieExplodeRatio = 0.03f;

    // ═══════════════════════════════════════════════════════
    //  Tooltip
    // ═══════════════════════════════════════════════════════

    /// <summary>Tooltip background color.</summary>
    [ExportGroup("Tooltip")]
    [Export] public Color TooltipBackground { get => _tooltipBackground; set => Set(ref _tooltipBackground, value); }

    private Color _tooltipBackground = new(0.12f, 0.12f, 0.18f, 0.92f);

    /// <summary>Tooltip text color.</summary>
    [Export] public Color TooltipTextColor { get => _tooltipTextColor; set => Set(ref _tooltipTextColor, value); }

    private Color _tooltipTextColor = new(1f, 1f, 1f, 0.9f);

    /// <summary>Tooltip border color.</summary>
    [Export] public Color TooltipBorderColor { get => _tooltipBorderColor; set => Set(ref _tooltipBorderColor, value); }

    private Color _tooltipBorderColor = new(1f, 1f, 1f, 0.2f);

    /// <summary>Tooltip border stroke width in pixels.</summary>
    [Export] public float TooltipBorderWidth { get => _tooltipBorderWidth; set => Set(ref _tooltipBorderWidth, value); }

    private float _tooltipBorderWidth = 1f;

    /// <summary>Tooltip corner radius in pixels.</summary>
    [Export] public float TooltipCornerRadius { get => _tooltipCornerRadius; set => Set(ref _tooltipCornerRadius, value); }

    private float _tooltipCornerRadius = 6f;

    /// <summary>Tooltip text font size in pixels.</summary>
    [Export] public float TooltipFontSize { get => _tooltipFontSize; set => Set(ref _tooltipFontSize, value); }

    private float _tooltipFontSize = 12f;

    /// <summary>Tooltip inner padding in pixels.</summary>
    [Export] public float TooltipPadding { get => _tooltipPadding; set => Set(ref _tooltipPadding, value); }

    private float _tooltipPadding = 8f;


    /// <summary>Horizontal distance between the pointer and the tooltip bubble, in pixels.</summary>
    [Export] public float TooltipOffsetX { get => _tooltipOffsetX; set => Set(ref _tooltipOffsetX, value); }

    private float _tooltipOffsetX = 10f;

    /// <summary>Vertical distance between the pointer and the tooltip bubble, in pixels.</summary>
    [Export] public float TooltipOffsetY { get => _tooltipOffsetY; set => Set(ref _tooltipOffsetY, value); }

    private float _tooltipOffsetY = 8f;

    /// <summary>Vertical offset used when the tooltip has to flip below the pointer, in pixels.</summary>
    [Export] public float TooltipFlipOffsetY { get => _tooltipFlipOffsetY; set => Set(ref _tooltipFlipOffsetY, value); }

    private float _tooltipFlipOffsetY = 12f;

    /// <summary>Smallest distance a tooltip keeps to the canvas edge, in pixels.</summary>
    [Export] public float TooltipEdgeMargin { get => _tooltipEdgeMargin; set => Set(ref _tooltipEdgeMargin, value); }

    private float _tooltipEdgeMargin = 4f;

    /// <summary>Extra line spacing inside the tooltip bubble, in pixels.</summary>
    [Export] public float TooltipLineSpacing { get => _tooltipLineSpacing; set => Set(ref _tooltipLineSpacing, value); }

    private float _tooltipLineSpacing = 4f;

    /// <summary>Crosshair line colour (used when the theme's crosshair switch is on and the pointer is inside
    /// the plot).</summary>
    // ═══════════════════════════════════════════════════════
    //  Crosshair
    // ═══════════════════════════════════════════════════════
    [ExportGroup("Crosshair")]
    [Export] public Color CrosshairColor { get => _crosshairColor; set => Set(ref _crosshairColor, value); }

    private Color _crosshairColor = new(1f, 1f, 1f, 0.3f);

    /// <summary>Crosshair line stroke width in pixels.</summary>
    [Export] public float CrosshairStrokeWidth { get => _crosshairStrokeWidth; set => Set(ref _crosshairStrokeWidth, value); }

    private float _crosshairStrokeWidth = 1f;

    /// <summary>Crosshair dash and gap length in pixels.</summary>
    [Export] public float CrosshairDashLength { get => _crosshairDashLength; set => Set(ref _crosshairDashLength, value); }

    private float _crosshairDashLength = 4f;

    // ═══════════════════════════════════════════════════════
    //  Legend
    // ═══════════════════════════════════════════════════════

    /// <summary>Gap between legend color swatch and text label in pixels.</summary>
    [ExportGroup("Legend")]
    [Export] public float LegendSwatchTextGap { get => _legendSwatchTextGap; set => Set(ref _legendSwatchTextGap, value); }

    private float _legendSwatchTextGap = 4f;

    /// <summary>Opacity for dimmed/hidden legend items.</summary>
    [Export] public float LegendDimmedOpacity { get => _legendDimmedOpacity; set => Set(ref _legendDimmedOpacity, value); }

    private float _legendDimmedOpacity = 0.3f;

    /// <summary>Legend color swatch corner radius in pixels.</summary>
    [Export] public float LegendSwatchCornerRadius { get => _legendSwatchCornerRadius; set => Set(ref _legendSwatchCornerRadius, value); }

    private float _legendSwatchCornerRadius = 2f;

    /// <summary>Vertical spacing between legend items in pixels.</summary>
    [Export] public float LegendVerticalItemSpacing { get => _legendVerticalItemSpacing; set => Set(ref _legendVerticalItemSpacing, value); }

    private float _legendVerticalItemSpacing = 4f;

    /// <summary>Extra gap below plot area for bottom-positioned legend in pixels.</summary>
    [Export] public float LegendBottomGap { get => _legendBottomGap; set => Set(ref _legendBottomGap, value); }

    private float _legendBottomGap = 10f;


    /// <summary>Horizontal offset applied to a right-positioned legend before it is clamped.</summary>
    [Export] public float LegendRightOffset { get => _legendRightOffset; set => Set(ref _legendRightOffset, value); }

    private float _legendRightOffset = 40f;

    /// <summary>
    /// Fallback text width factor (per character, relative to the font size) used when no canvas is
    /// available to measure the legend labels.
    /// </summary>
    [Export] public float LegendTextWidthFallbackRatio { get => _legendTextWidthFallbackRatio; set => Set(ref _legendTextWidthFallbackRatio, value); }

    private float _legendTextWidthFallbackRatio = 0.6f;

    /// <summary>Baseline nudge for legend text inside its swatch row, as a factor of the swatch size.</summary>
    [Export] public float LegendTextBaselineRatio { get => _legendTextBaselineRatio; set => Set(ref _legendTextBaselineRatio, value); }

    private float _legendTextBaselineRatio = 0.85f;

    /// <summary>Hover ring colour drawn around the hovered vertex of a line mark.</summary>
    // ═══════════════════════════════════════════════════════
    //  Line Mark
    // ═══════════════════════════════════════════════════════
    [ExportGroup("Line Mark")]
    [Export] public Color LineHoverRingColor { get => _lineHoverRingColor; set => Set(ref _lineHoverRingColor, value); }

    private Color _lineHoverRingColor = new(1f, 1f, 1f, 0.6f);

    /// <summary>Hover ring stroke width for LineMark data points.</summary>
    [Export] public float LineHoverRingStrokeWidth { get => _lineHoverRingStrokeWidth; set => Set(ref _lineHoverRingStrokeWidth, value); }

    private float _lineHoverRingStrokeWidth = 2f;

    /// <summary>Hover point display radius for LineMark.</summary>
    [Export] public float LineHoverPointRadius { get => _lineHoverPointRadius; set => Set(ref _lineHoverPointRadius, value); }

    private float _lineHoverPointRadius = 6f;

    // ═══════════════════════════════════════════════════════
    //  Point Mark
    // ═══════════════════════════════════════════════════════

    /// <summary>Default point opacity when no Opacity channel is encoded.</summary>
    [ExportGroup("Point Mark")]
    [Export] public float PointDefaultOpacity { get => _pointDefaultOpacity; set => Set(ref _pointDefaultOpacity, value); }

    private float _pointDefaultOpacity = 0.8f;

    /// <summary>Hover radius multiplier for points.</summary>
    [Export] public float PointHoverRadiusRatio { get => _pointHoverRadiusRatio; set => Set(ref _pointHoverRadiusRatio, value); }

    private float _pointHoverRadiusRatio = 1.3f;

    /// <summary>Minimum point radius when using Size channel mapping.</summary>
    [Export] public float PointSizeMin { get => _pointSizeMin; set => Set(ref _pointSizeMin, value); }

    private float _pointSizeMin = 3f;

    /// <summary>Size range for Size channel mapping (radius = min + map * range).</summary>
    [Export] public float PointSizeRange { get => _pointSizeRange; set => Set(ref _pointSizeRange, value); }

    private float _pointSizeRange = 20f;

    // ═══════════════════════════════════════════════════════
    //  Radar Mark
    // ═══════════════════════════════════════════════════════

    /// <summary>Radar chart grid ring color.</summary>
    [ExportGroup("Radar Mark")]
    [Export] public Color RadarGridColor { get => _radarGridColor; set => Set(ref _radarGridColor, value); }

    private Color _radarGridColor = new(1f, 1f, 1f, 0.08f);

    /// <summary>Radar chart axis spoke color.</summary>
    [Export] public Color RadarAxisColor { get => _radarAxisColor; set => Set(ref _radarAxisColor, value); }

    private Color _radarAxisColor = new(1f, 1f, 1f, 0.15f);

    /// <summary>Radar chart axis label color.</summary>
    [Export] public Color RadarLabelColor { get => _radarLabelColor; set => Set(ref _radarLabelColor, value); }

    private Color _radarLabelColor = new(1f, 1f, 1f, 0.6f);

    /// <summary>Radar chart tick value color.</summary>
    [Export] public Color RadarTickColor { get => _radarTickColor; set => Set(ref _radarTickColor, value); }

    private Color _radarTickColor = new(1f, 1f, 1f, 0.35f);

    /// <summary>Hover dot scale multiplier for radar chart vertices.</summary>
    [Export] public float RadarHoverDotScale { get => _radarHoverDotScale; set => Set(ref _radarHoverDotScale, value); }

    private float _radarHoverDotScale = 1.5f;

    // ═══════════════════════════════════════════════════════
    //  Box Mark
    // ═══════════════════════════════════════════════════════

    /// <summary>Box plot fill color.</summary>
    [ExportGroup("Box Mark")]
    [Export] public Color BoxFillColor { get => _boxFillColor; set => Set(ref _boxFillColor, value); }

    private Color _boxFillColor = new(0.29f, 0.59f, 0.98f, 0.6f);

    /// <summary>Box plot whisker and median line color.</summary>
    [Export] public Color BoxLineColor { get => _boxLineColor; set => Set(ref _boxLineColor, value); }

    private Color _boxLineColor = new(1f, 1f, 1f, 0.9f);

    // ═══════════════════════════════════════════════════════
    //  Violin Mark
    // ═══════════════════════════════════════════════════════

    /// <summary>Violin chart inner box color.</summary>
    [ExportGroup("Violin Mark")]
    [Export] public Color ViolinBoxColor { get => _violinBoxColor; set => Set(ref _violinBoxColor, value); }

    private Color _violinBoxColor = new(0.2f, 0.2f, 0.25f, 0.8f);

    /// <summary>Violin chart median dot color.</summary>
    [Export] public Color ViolinMedianDotColor { get => _violinMedianDotColor; set => Set(ref _violinMedianDotColor, value); }

    private Color _violinMedianDotColor = new(1f, 1f, 1f);

    /// <summary>Violin chart median dot radius in pixels.</summary>
    [Export] public float ViolinMedianDotRadius { get => _violinMedianDotRadius; set => Set(ref _violinMedianDotRadius, value); }

    private float _violinMedianDotRadius = 3f;

    /// <summary>Inner quartile box half-width as a ratio of violin half-width.</summary>
    [Export] public float ViolinBoxWidthRatio { get => _violinBoxWidthRatio; set => Set(ref _violinBoxWidthRatio, value); }

    private float _violinBoxWidthRatio = 0.15f;

    // ═══════════════════════════════════════════════════════
    //  Gauge Mark
    // ═══════════════════════════════════════════════════════

    /// <summary>Gauge track (background arc) color.</summary>
    [ExportGroup("Gauge Mark")]
    [Export] public Color GaugeTrackColor { get => _gaugeTrackColor; set => Set(ref _gaugeTrackColor, value); }

    private Color _gaugeTrackColor = new(1f, 1f, 1f, 0.08f);

    /// <summary>Gauge center label color.</summary>
    [Export] public Color GaugeLabelColor { get => _gaugeLabelColor; set => Set(ref _gaugeLabelColor, value); }

    private Color _gaugeLabelColor = new(1f, 1f, 1f, 0.9f);

    /// <summary>Gauge min/max boundary label color.</summary>
    [Export] public Color GaugeMinMaxLabelColor { get => _gaugeMinMaxLabelColor; set => Set(ref _gaugeMinMaxLabelColor, value); }

    private Color _gaugeMinMaxLabelColor = new(1f, 1f, 1f, 0.4f);

    /// <summary>Offset for min/max labels from outer arc edge in pixels.</summary>
    [Export] public float GaugeLabelOffset { get => _gaugeLabelOffset; set => Set(ref _gaugeLabelOffset, value); }

    private float _gaugeLabelOffset = 12f;

    // ═══════════════════════════════════════════════════════
    //  Sankey Mark
    // ═══════════════════════════════════════════════════════

    /// <summary>Sankey node rectangle color.</summary>
    [ExportGroup("Sankey Mark")]
    [Export] public Color SankeyNodeColor { get => _sankeyNodeColor; set => Set(ref _sankeyNodeColor, value); }

    private Color _sankeyNodeColor = new(0.85f, 0.85f, 0.9f);

    /// <summary>Sankey node rectangle corner radius in pixels.</summary>
    [Export] public float SankeyNodeCornerRadius { get => _sankeyNodeCornerRadius; set => Set(ref _sankeyNodeCornerRadius, value); }

    private float _sankeyNodeCornerRadius = 2f;

    /// <summary>Gap between Sankey node and its label in pixels.</summary>
    [Export] public float SankeyLabelGap { get => _sankeyLabelGap; set => Set(ref _sankeyLabelGap, value); }

    private float _sankeyLabelGap = 4f;

    // ═══════════════════════════════════════════════════════
    //  Candlestick Mark
    // ═══════════════════════════════════════════════════════

    /// <summary>Candlestick bullish (close > open) color.</summary>
    [ExportGroup("Candlestick Mark")]
    [Export] public Color CandlestickBullishColor { get => _candlestickBullishColor; set => Set(ref _candlestickBullishColor, value); }

    private Color _candlestickBullishColor = new(0.29f, 0.85f, 0.60f);

    /// <summary>Candlestick bearish (close &lt; open) color.</summary>
    [Export] public Color CandlestickBearishColor { get => _candlestickBearishColor; set => Set(ref _candlestickBearishColor, value); }

    private Color _candlestickBearishColor = new(0.98f, 0.45f, 0.29f);

    /// <summary>Hover wick width multiplier for candlestick marks.</summary>
    [Export] public float CandlestickHoverWickScale { get => _candlestickHoverWickScale; set => Set(ref _candlestickHoverWickScale, value); }

    private float _candlestickHoverWickScale = 1.5f;

    /// <summary>Stroke width for hollow (bullish) candlestick bodies.</summary>
    [Export] public float CandlestickHollowStrokeWidth { get => _candlestickHollowStrokeWidth; set => Set(ref _candlestickHollowStrokeWidth, value); }

    private float _candlestickHollowStrokeWidth = 1.5f;

    // ═══════════════════════════════════════════════════════
    //  Feature Toggles
    // ═══════════════════════════════════════════════════════

    /// <summary>
    /// Enable entry/exit animations. Theme-level switch: with it off, <see cref="Chart.Animate(float)"/>
    /// and <see cref="Chart.Animate(AnimationContext)"/> settle on the end state
    /// (<see cref="AnimationContext.Default"/>, entry progress 1) instead of animating - whatever the
    /// host passes.
    /// </summary>
    [ExportGroup("Feature Toggles")]
    [Export] public bool EnableAnimation { get => _enableAnimation; set => Set(ref _enableAnimation, value); }

    private bool _enableAnimation = true;

    /// <summary>Enable hover highlight effect (brighten + scale).</summary>
    [Export] public bool EnableHoverHighlight { get => _enableHoverHighlight; set => Set(ref _enableHoverHighlight, value); }

    private bool _enableHoverHighlight = true;

    /// <summary>Enable hover explode offset on polar segments (pie, sunburst).</summary>
    [Export] public bool EnableHoverExplode { get => _enableHoverExplode; set => Set(ref _enableHoverExplode, value); }

    private bool _enableHoverExplode = true;

    /// <summary>Enable selection stroke outline.</summary>
    [Export] public bool EnableSelection { get => _enableSelection; set => Set(ref _enableSelection, value); }

    private bool _enableSelection = true;

    /// <summary>Enable crosshair overlay on hover.</summary>
    [Export] public bool EnableCrosshair { get => _enableCrosshair; set => Set(ref _enableCrosshair, value); }

    private bool _enableCrosshair = true;

    /// <summary>
    /// Theme-level master switch for the hover tooltip: with it off, <see cref="ChartView"/> neither
    /// updates nor draws the tooltip, whatever <see cref="ChartView.ShowTooltip"/> says.
    /// </summary>
    [Export] public bool EnableTooltip { get => _enableTooltip; set => Set(ref _enableTooltip, value); }

    private bool _enableTooltip = true;

    // ═══════════════════════════════════════════════════════
    //  Lollipop Mark
    // ═══════════════════════════════════════════════════════

    /// <summary>Hover dot scale multiplier for lollipop marks.</summary>
    [ExportGroup("Lollipop Mark")]
    [Export] public float LollipopHoverScale { get => _lollipopHoverScale; set => Set(ref _lollipopHoverScale, value); }

    private float _lollipopHoverScale = 1.3f;

    // ═══════════════════════════════════════════════════════
    //  Range Area Mark
    // ═══════════════════════════════════════════════════════

    /// <summary>Hover point indicator radius for range area marks in pixels.</summary>
    [ExportGroup("Range Area Mark")]
    [Export] public float RangeAreaPointRadius { get => _rangeAreaPointRadius; set => Set(ref _rangeAreaPointRadius, value); }

    private float _rangeAreaPointRadius = 4f;

    // ═══════════════════════════════════════════════════════
    //  Chord Mark
    // ═══════════════════════════════════════════════════════

    /// <summary>Distance from outer arc to node label in pixels.</summary>
    [ExportGroup("Chord Mark")]
    [Export] public float ChordLabelDistance { get => _chordLabelDistance; set => Set(ref _chordLabelDistance, value); }

    private float _chordLabelDistance = 8f;

    // ═══════════════════════════════════════════════════════
    //  Sunburst Mark
    // ═══════════════════════════════════════════════════════

    /// <summary>Base opacity for Sunburst arc segments.</summary>
    [ExportGroup("Sunburst Mark")]
    [Export] public float SunburstArcOpacity { get => _sunburstArcOpacity; set => Set(ref _sunburstArcOpacity, value); }

    private float _sunburstArcOpacity = 0.85f;

    /// <summary>Minimum sweep angle in radians for label display on sunburst arcs.</summary>
    [Export] public float SunburstLabelMinSweep { get => _sunburstLabelMinSweep; set => Set(ref _sunburstLabelMinSweep, value); }

    private float _sunburstLabelMinSweep = 0.15f;

    /// <summary>Minimum treemap cell width required to draw a cell label, in pixels.</summary>
    [ExportGroup("Treemap Mark")]
    [Export] public float TreemapLabelMinWidth { get => _treemapLabelMinWidth; set => Set(ref _treemapLabelMinWidth, value); }

    private float _treemapLabelMinWidth = 20f;

    /// <summary>Minimum treemap cell height required to draw a cell label, in pixels.</summary>
    [Export] public float TreemapLabelMinHeight { get => _treemapLabelMinHeight; set => Set(ref _treemapLabelMinHeight, value); }

    private float _treemapLabelMinHeight = 14f;

    // ═══════════════════════════════════════════════════════
    //  Hit Test Tolerances
    // ═══════════════════════════════════════════════════════

    /// <summary>Extra padding (in pixels) added to point/dot radius for hit detection.</summary>
    [ExportGroup("Hit Test")]
    [Export] public float HitTestPointPadding { get => _hitTestPointPadding; set => Set(ref _hitTestPointPadding, value); }

    private float _hitTestPointPadding = 4f;

    /// <summary>Pixel distance threshold for nearest-point snapping (line, radar marks).</summary>
    [Export] public float HitTestSnapDistance { get => _hitTestSnapDistance; set => Set(ref _hitTestSnapDistance, value); }

    private float _hitTestSnapDistance = 12f;

    /// <summary>Pixel distance for area/range mark X-axis snapping.</summary>
    [Export] public float HitTestAreaSnapDistance { get => _hitTestAreaSnapDistance; set => Set(ref _hitTestAreaSnapDistance, value); }

    private float _hitTestAreaSnapDistance = 20f;

    /// <summary>Angle tolerance in radians for chord arc hit detection.</summary>
    [Export] public float HitTestAngleTolerance { get => _hitTestAngleTolerance; set => Set(ref _hitTestAngleTolerance, value); }

    private float _hitTestAngleTolerance = 0.2f;

    // ═══════════════════════════════════════════════════════
    //  Factory Methods
    // ═══════════════════════════════════════════════════════

    // The two sources below are the single, *private* definition of the built-in colours. They are
    // never handed out: the public members clone them per read (see DefaultPalette /
    // DefaultSequentialGradient). A `public static readonly Color[]` would lock only the reference -
    // `DefaultPalette[0] = …` would silently rewrite the default of every theme built afterwards.
    private static readonly Color[] PaletteSource =
    [
        new(0.29f, 0.59f, 0.98f), // blue
        new(0.98f, 0.45f, 0.29f), // orange
        new(0.29f, 0.85f, 0.60f), // green
        new(0.98f, 0.80f, 0.29f), // yellow
        new(0.75f, 0.29f, 0.98f), // purple
        new(0.29f, 0.92f, 0.98f), // cyan
    ];

    private static readonly Color[] SequentialGradientSource =
    [
        new(0.12f, 0.07f, 0.53f), // deep purple
        new(0.29f, 0.00f, 0.73f), // purple
        new(0.85f, 0.24f, 0.31f), // red
        new(0.99f, 0.68f, 0.38f), // orange
        new(0.99f, 0.95f, 0.70f), // light yellow
    ];

    /// <summary>
    /// Default categorical 6-color palette, as a <b>fresh copy on every read</b>: writing into the
    /// returned array changes nothing - not the next read, not <see cref="Default"/>, and not any
    /// existing theme (a theme copies the palette into its own <see cref="Palette"/> when it is
    /// constructed), nor any renderer that resolved this fallback.
    /// </summary>
    public static Color[] DefaultPalette => (Color[])PaletteSource.Clone();

    /// <summary>
    /// Default 5-stop sequential gradient, as a <b>fresh copy on every read</b> (see
    /// <see cref="DefaultPalette"/>: the same rule applies, writing into it is harmless).
    /// </summary>
    public static Color[] DefaultSequentialGradient => (Color[])SequentialGradientSource.Clone();

    // Declared *after* the two palette sources on purpose: C# runs static field initializers in
    // textual order, and the instance field initializers read DefaultPalette /
    // DefaultSequentialGradient. Moving this above them would leave both arrays null (a property
    // reading a null PaletteSource) while Default is constructed.
    /// <summary>
    /// The theme a renderer falls back to when it was not given one (<c>Theme == null</c>).
    /// <para>
    /// Not to be confused with <see cref="Dark"/>: <see cref="Dark"/> is the built-in dark palette
    /// <i>factory</i> (a fresh, mutable theme on every call), while <see cref="Default"/> is the single
    /// shared instance whose field initializers <i>are</i> the documented defaults. A site that needs a
    /// value when no theme is attached writes <c>(Theme ?? ChartTheme.Default).X</c> instead of restating
    /// the number, so the fallback can only ever have one source of truth.
    /// </para>
    /// <para>
    /// Treat this instance as <b>read-only</b>: it is shared by every mark, renderer and tooltip in the
    /// process, so mutating it (or its <see cref="Palette"/>) silently changes the fallback of all of
    /// them. Call <see cref="Clone"/> first when a value has to be changed.
    /// </para>
    /// </summary>
    public static ChartTheme Default { get; } = new();

    /// <summary>Create the default dark theme matching the current hardcoded values.</summary>
    public static ChartTheme Dark() => new();

    /// <summary>
    /// Create a deep clone of this theme. Useful for deriving custom themes
    /// from an existing one without affecting the original.
    /// </summary>
    public ChartTheme Clone()
    {
        // Duplicate() creates a properly registered native instance of this theme.
        // MemberwiseClone must not be used here: it produces a second managed wrapper for the
        // same native object, which Godot's object tracking cannot handle (the unregistered
        // wrapper crashes the process on finalization).
        var duplicate = Duplicate(true);
        if (duplicate is ChartTheme clone)
        {
            // Deep copy the array properties so mutating one theme can never affect the other.
            clone.Palette = (Color[])Palette.Clone();
            clone.SequentialGradient = (Color[])SequentialGradient.Clone();
            return clone;
        }

        // Duplicate() hands back a bare Resource when the managed type is not (yet) registered for it,
        // which happens in the editor after a script reload. Copy the native property values into a
        // fresh managed instance instead of failing the whole theme.
        var copy = new ChartTheme();
        foreach (var property in duplicate.GetPropertyList())
        {
            var name = property["name"].AsString();
            if (string.IsNullOrEmpty(name)) continue;
            copy.Set(name, duplicate.Get(name));
        }
        copy.Palette = (Color[])Palette.Clone();
        copy.SequentialGradient = (Color[])SequentialGradient.Clone();
        duplicate.Dispose();
        return copy;
    }

    /// <summary>
    /// Create a light theme suitable for bright backgrounds.
    /// Uses <see cref="Dark"/> as a base and overrides visual properties.
    /// <para>
    /// To create a custom theme variant, follow the same pattern:
    /// <code>
    /// var custom = ChartTheme.Dark().Clone();
    /// custom.BackgroundColor = new Color(...);
    /// // ... override only the properties that differ
    /// </code>
    /// New properties added to <see cref="ChartTheme"/> are automatically inherited
    /// from the base via <see cref="Clone"/>. Only override what differs.
    /// </para>
    /// </summary>
    public static ChartTheme Light()
    {
        var theme = Dark().Clone();

        // Color Palette — slightly deeper for light backgrounds
        theme.Palette =
        [
            new(0.20f, 0.47f, 0.84f),
            new(0.90f, 0.38f, 0.20f),
            new(0.20f, 0.72f, 0.50f),
            new(0.85f, 0.68f, 0.20f),
            new(0.62f, 0.22f, 0.85f),
            new(0.20f, 0.78f, 0.84f),
        ];

        // Chart Frame
        theme.BackgroundColor        = new(0.98f, 0.98f, 0.96f);
        theme.GridColor              = new(0f, 0f, 0f, 0.08f);
        theme.AxisColor              = new(0f, 0f, 0f, 0.5f);

        // Typography
        theme.TitleColor             = new(0.1f, 0.1f, 0.1f);
        theme.LabelColor             = new(0.3f, 0.3f, 0.3f);
        theme.DataLabelColor         = new(0.15f, 0.15f, 0.15f);

        // Mark Defaults
        theme.DefaultMarkColor       = new(0.20f, 0.47f, 0.84f);

        // Selection & Hover
        theme.SelectionColor         = new(0f, 0f, 0f, 0.6f);
        theme.HoverBrighten          = 1.15f;
        theme.UnfocusedOpacity       = 0.2f;

        // Polar / Segment
        theme.SegmentBorderColor     = new(0.98f, 0.98f, 0.96f);

        // Tooltip
        theme.TooltipBackground      = new(1f, 1f, 1f, 0.95f);
        theme.TooltipTextColor       = new(0.1f, 0.1f, 0.1f);
        theme.TooltipBorderColor     = new(0f, 0f, 0f, 0.15f);

        // Crosshair
        theme.CrosshairColor         = new(0f, 0f, 0f, 0.3f);

        // Line Mark
        theme.LineHoverRingColor     = new(0f, 0f, 0f, 0.4f);

        // Point Mark
        theme.PointDefaultOpacity    = 0.85f;

        // Radar Mark
        theme.RadarGridColor         = new(0f, 0f, 0f, 0.08f);
        theme.RadarAxisColor         = new(0f, 0f, 0f, 0.15f);
        theme.RadarLabelColor        = new(0.3f, 0.3f, 0.3f);
        theme.RadarTickColor         = new(0f, 0f, 0f, 0.3f);

        // Box Mark
        theme.BoxFillColor           = new(0.20f, 0.47f, 0.84f, 0.5f);
        theme.BoxLineColor           = new(0.15f, 0.15f, 0.15f);

        // Violin Mark
        theme.ViolinBoxColor         = new(0.85f, 0.85f, 0.9f, 0.6f);
        theme.ViolinMedianDotColor   = new(0.1f, 0.1f, 0.1f);

        // Gauge Mark
        theme.GaugeTrackColor        = new(0f, 0f, 0f, 0.06f);
        theme.GaugeLabelColor        = new(0.1f, 0.1f, 0.1f);
        theme.GaugeMinMaxLabelColor  = new(0.3f, 0.3f, 0.3f);

        // Sankey Mark
        theme.SankeyNodeColor        = new(0.7f, 0.7f, 0.75f);

        // Candlestick Mark
        theme.CandlestickBullishColor = new(0.16f, 0.70f, 0.44f);
        theme.CandlestickBearishColor = new(0.85f, 0.32f, 0.18f);

        return theme;
    }
}
