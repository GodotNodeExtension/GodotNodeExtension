using Godot;

namespace GodotNodeExtension.Component.GodotChart;

/// <summary>
/// Centralized theme configuration for all chart visual styles.
/// Attach to a Chart via <see cref="Chart.Theme(ChartTheme)"/>.
/// Properties are organized with <c>[ExportGroup]</c> for clear Inspector display.
/// </summary>
[GlobalClass]
public partial class ChartTheme : Resource
{
    // ═══════════════════════════════════════════════════════
    //  Color Palette
    // ═══════════════════════════════════════════════════════

    /// <summary>Default categorical color palette used by <see cref="ColorScale"/>.</summary>
    [ExportGroup("Color Palette")]
    [Export] public Color[] Palette { get; set; } = DefaultPalette;

    /// <summary>Sequential gradient colors for continuous data (<see cref="SequentialColorScale"/>).</summary>
    [Export] public Color[] SequentialGradient { get; set; } = DefaultSequentialGradient;

    // ═══════════════════════════════════════════════════════
    //  Chart Frame
    // ═══════════════════════════════════════════════════════

    /// <summary>Chart background fill color.</summary>
    [ExportGroup("Chart Frame")]
    [Export] public Color BackgroundColor { get; set; } = new(0.08f, 0.08f, 0.12f);

    /// <summary>Background rectangle corner radius in pixels.</summary>
    [Export] public float BackgroundCornerRadius { get; set; } = 8f;

    /// <summary>Grid line color.</summary>
    [Export] public Color GridColor { get; set; } = new(1f, 1f, 1f, 0.08f);

    /// <summary>Grid line stroke width in pixels.</summary>
    [Export] public float GridLineWidth { get; set; } = 1f;

    /// <summary>Axis line color.</summary>
    [Export] public Color AxisColor { get; set; } = new(1f, 1f, 1f, 0.4f);

    /// <summary>Main axis line stroke width in pixels.</summary>
    [Export] public float AxisLineWidth { get; set; } = 2f;

    // ═══════════════════════════════════════════════════════
    //  Layout
    // ═══════════════════════════════════════════════════════

    /// <summary>Margin between chart edge and axis title text in pixels.</summary>
    [ExportGroup("Layout")]
    [Export] public float AxisTitleMargin { get; set; } = 6f;

    /// <summary>Height reserved for the title text area in pixels.</summary>
    [Export] public float TitleReservedHeight { get; set; } = 24f;

    /// <summary>Width reserved for Y2 axis labels in pixels.</summary>
    [Export] public float Y2LabelReservedWidth { get; set; } = 35f;

    /// <summary>Title text Y offset from top of chart in pixels.</summary>
    [Export] public float TitleYOffset { get; set; } = 16f;

    /// <summary>X-axis label Y offset below plot area bottom in pixels.</summary>
    [Export] public float XAxisLabelOffset { get; set; } = 15f;

    /// <summary>Gap between Y-axis labels and plot edge in pixels.</summary>
    [Export] public float YAxisLabelGap { get; set; } = 5f;

    // ═══════════════════════════════════════════════════════
    //  Typography
    // ═══════════════════════════════════════════════════════

    /// <summary>Default font family name. Null uses system default.</summary>
    [ExportGroup("Typography")]
    [Export] public string? FontFamily { get; set; }

    /// <summary>Default Godot Font resource. Null uses system default.</summary>
    [Export] public Font? Font { get; set; }

    /// <summary>Title text color.</summary>
    [Export] public Color TitleColor { get; set; } = new(1f, 1f, 1f, 0.85f);

    /// <summary>Title font size in pixels.</summary>
    [Export] public float TitleFontSize { get; set; } = 13f;

    /// <summary>Axis label text color.</summary>
    [Export] public Color LabelColor { get; set; } = new(1f, 1f, 1f, 0.6f);

    /// <summary>Axis label font size in pixels.</summary>
    [Export] public float LabelFontSize { get; set; } = 13f;

    /// <summary>Data label color drawn on marks (pie slices, treemap cells, etc.).</summary>
    [Export] public Color DataLabelColor { get; set; } = new(1f, 1f, 1f, 0.9f);

    // ═══════════════════════════════════════════════════════
    //  Mark Defaults
    // ═══════════════════════════════════════════════════════

    /// <summary>Fallback fill color for marks when no Color channel is encoded.</summary>
    [ExportGroup("Mark Defaults")]
    [Export] public Color DefaultMarkColor { get; set; } = new(0.29f, 0.59f, 0.98f);

    /// <summary>Default corner radius for rectangular marks.</summary>
    [Export] public float CornerRadius { get; set; } = 3f;

    /// <summary>Default stroke width for line-based marks.</summary>
    [Export] public float StrokeWidth { get; set; } = 2f;

    // ═══════════════════════════════════════════════════════
    //  Selection & Hover
    // ═══════════════════════════════════════════════════════

    /// <summary>Selection highlight ring color.</summary>
    [ExportGroup("Selection & Hover")]
    [Export] public Color SelectionColor { get; set; } = new(1f, 1f, 1f, 0.8f);

    /// <summary>Selection highlight ring stroke width in pixels.</summary>
    [Export] public float SelectionStrokeWidth { get; set; } = 2f;

    /// <summary>Hover brightness multiplier (1.0 = no change). Applied via BrightenColor.</summary>
    [Export] public float HoverBrighten { get; set; } = 1.2f;

    /// <summary>Non-focused series opacity multiplier during series focus.</summary>
    [Export] public float UnfocusedOpacity { get; set; } = 0.15f;

    /// <summary>Hover size scale for bars, dots, etc.</summary>
    [Export] public float HoverScale { get; set; } = 1.05f;

    // ═══════════════════════════════════════════════════════
    //  Polar / Segment
    // ═══════════════════════════════════════════════════════

    /// <summary>Border color between polar chart segments (pie slices, sunburst arcs).</summary>
    [ExportGroup("Polar / Segment")]
    [Export] public Color SegmentBorderColor { get; set; } = new(0.08f, 0.08f, 0.12f);

    /// <summary>Border stroke width between segments. Set 0 to hide borders.</summary>
    [Export] public float SegmentBorderWidth { get; set; } = 1f;

    /// <summary>Default arc gap between polar chart segments in radians.</summary>
    [Export] public float ArcGap { get; set; } = 0.02f;

    /// <summary>Ring gap between concentric rings in pixels (Sunburst).</summary>
    [Export] public float RingGap { get; set; } = 2f;

    /// <summary>Hover explode offset ratio relative to outer radius for pie/donut charts.</summary>
    [Export] public float PieExplodeRatio { get; set; } = 0.03f;

    // ═══════════════════════════════════════════════════════
    //  Tooltip
    // ═══════════════════════════════════════════════════════

    /// <summary>Tooltip background color.</summary>
    [ExportGroup("Tooltip")]
    [Export] public Color TooltipBackground { get; set; } = new(0.12f, 0.12f, 0.18f, 0.92f);

    /// <summary>Tooltip text color.</summary>
    [Export] public Color TooltipTextColor { get; set; } = new(1f, 1f, 1f, 0.9f);

    /// <summary>Tooltip border color.</summary>
    [Export] public Color TooltipBorderColor { get; set; } = new(1f, 1f, 1f, 0.2f);

    /// <summary>Tooltip border stroke width in pixels.</summary>
    [Export] public float TooltipBorderWidth { get; set; } = 1f;

    /// <summary>Tooltip corner radius in pixels.</summary>
    [Export] public float TooltipCornerRadius { get; set; } = 6f;

    /// <summary>Tooltip text font size in pixels.</summary>
    [Export] public float TooltipFontSize { get; set; } = 12f;

    /// <summary>Tooltip inner padding in pixels.</summary>
    [Export] public float TooltipPadding { get; set; } = 8f;

    // ═══════════════════════════════════════════════════════
    //  Crosshair
    // ═══════════════════════════════════════════════════════

    /// <summary>Crosshair line color.</summary>
    [ExportGroup("Crosshair")]
    [Export] public Color CrosshairColor { get; set; } = new(1f, 1f, 1f, 0.3f);

    /// <summary>Crosshair line stroke width in pixels.</summary>
    [Export] public float CrosshairStrokeWidth { get; set; } = 1f;

    /// <summary>Crosshair dash and gap length in pixels.</summary>
    [Export] public float CrosshairDashLength { get; set; } = 4f;

    // ═══════════════════════════════════════════════════════
    //  Legend
    // ═══════════════════════════════════════════════════════

    /// <summary>Gap between legend color swatch and text label in pixels.</summary>
    [ExportGroup("Legend")]
    [Export] public float LegendSwatchTextGap { get; set; } = 4f;

    /// <summary>Opacity for dimmed/hidden legend items.</summary>
    [Export] public float LegendDimmedOpacity { get; set; } = 0.3f;

    /// <summary>Legend color swatch corner radius in pixels.</summary>
    [Export] public float LegendSwatchCornerRadius { get; set; } = 2f;

    /// <summary>Vertical spacing between legend items in pixels.</summary>
    [Export] public float LegendVerticalItemSpacing { get; set; } = 4f;

    /// <summary>Extra gap below plot area for bottom-positioned legend in pixels.</summary>
    [Export] public float LegendBottomGap { get; set; } = 10f;

    // ═══════════════════════════════════════════════════════
    //  Line Mark
    // ═══════════════════════════════════════════════════════

    /// <summary>Hover ring color on data points for LineMark.</summary>
    [ExportGroup("Line Mark")]
    [Export] public Color LineHoverRingColor { get; set; } = new(1f, 1f, 1f, 0.6f);

    /// <summary>Hover ring stroke width for LineMark data points.</summary>
    [Export] public float LineHoverRingStrokeWidth { get; set; } = 2f;

    /// <summary>Hover point display radius for LineMark.</summary>
    [Export] public float LineHoverPointRadius { get; set; } = 6f;

    // ═══════════════════════════════════════════════════════
    //  Point Mark
    // ═══════════════════════════════════════════════════════

    /// <summary>Default point opacity when no Opacity channel is encoded.</summary>
    [ExportGroup("Point Mark")]
    [Export] public float PointDefaultOpacity { get; set; } = 0.8f;

    /// <summary>Hover radius multiplier for points.</summary>
    [Export] public float PointHoverRadiusRatio { get; set; } = 1.3f;

    /// <summary>Minimum point radius when using Size channel mapping.</summary>
    [Export] public float PointSizeMin { get; set; } = 3f;

    /// <summary>Size range for Size channel mapping (radius = min + map * range).</summary>
    [Export] public float PointSizeRange { get; set; } = 20f;

    // ═══════════════════════════════════════════════════════
    //  Radar Mark
    // ═══════════════════════════════════════════════════════

    /// <summary>Radar chart grid ring color.</summary>
    [ExportGroup("Radar Mark")]
    [Export] public Color RadarGridColor { get; set; } = new(1f, 1f, 1f, 0.08f);

    /// <summary>Radar chart axis spoke color.</summary>
    [Export] public Color RadarAxisColor { get; set; } = new(1f, 1f, 1f, 0.15f);

    /// <summary>Radar chart axis label color.</summary>
    [Export] public Color RadarLabelColor { get; set; } = new(1f, 1f, 1f, 0.6f);

    /// <summary>Radar chart tick value color.</summary>
    [Export] public Color RadarTickColor { get; set; } = new(1f, 1f, 1f, 0.35f);

    /// <summary>Hover dot scale multiplier for radar chart vertices.</summary>
    [Export] public float RadarHoverDotScale { get; set; } = 1.5f;

    // ═══════════════════════════════════════════════════════
    //  Box Mark
    // ═══════════════════════════════════════════════════════

    /// <summary>Box plot fill color.</summary>
    [ExportGroup("Box Mark")]
    [Export] public Color BoxFillColor { get; set; } = new(0.29f, 0.59f, 0.98f, 0.6f);

    /// <summary>Box plot whisker and median line color.</summary>
    [Export] public Color BoxLineColor { get; set; } = new(1f, 1f, 1f, 0.9f);

    // ═══════════════════════════════════════════════════════
    //  Violin Mark
    // ═══════════════════════════════════════════════════════

    /// <summary>Violin chart inner box color.</summary>
    [ExportGroup("Violin Mark")]
    [Export] public Color ViolinBoxColor { get; set; } = new(0.2f, 0.2f, 0.25f, 0.8f);

    /// <summary>Violin chart median dot color.</summary>
    [Export] public Color ViolinMedianDotColor { get; set; } = new(1f, 1f, 1f);

    /// <summary>Violin chart median dot radius in pixels.</summary>
    [Export] public float ViolinMedianDotRadius { get; set; } = 3f;

    /// <summary>Inner quartile box half-width as a ratio of violin half-width.</summary>
    [Export] public float ViolinBoxWidthRatio { get; set; } = 0.15f;

    // ═══════════════════════════════════════════════════════
    //  Gauge Mark
    // ═══════════════════════════════════════════════════════

    /// <summary>Gauge track (background arc) color.</summary>
    [ExportGroup("Gauge Mark")]
    [Export] public Color GaugeTrackColor { get; set; } = new(1f, 1f, 1f, 0.08f);

    /// <summary>Gauge center label color.</summary>
    [Export] public Color GaugeLabelColor { get; set; } = new(1f, 1f, 1f, 0.9f);

    /// <summary>Gauge min/max boundary label color.</summary>
    [Export] public Color GaugeMinMaxLabelColor { get; set; } = new(1f, 1f, 1f, 0.4f);

    /// <summary>Offset for min/max labels from outer arc edge in pixels.</summary>
    [Export] public float GaugeLabelOffset { get; set; } = 12f;

    // ═══════════════════════════════════════════════════════
    //  Sankey Mark
    // ═══════════════════════════════════════════════════════

    /// <summary>Sankey node rectangle color.</summary>
    [ExportGroup("Sankey Mark")]
    [Export] public Color SankeyNodeColor { get; set; } = new(0.85f, 0.85f, 0.9f);

    /// <summary>Sankey node rectangle corner radius in pixels.</summary>
    [Export] public float SankeyNodeCornerRadius { get; set; } = 2f;

    /// <summary>Gap between Sankey node and its label in pixels.</summary>
    [Export] public float SankeyLabelGap { get; set; } = 4f;

    // ═══════════════════════════════════════════════════════
    //  Candlestick Mark
    // ═══════════════════════════════════════════════════════

    /// <summary>Candlestick bullish (close > open) color.</summary>
    [ExportGroup("Candlestick Mark")]
    [Export] public Color CandlestickBullishColor { get; set; } = new(0.29f, 0.85f, 0.60f);

    /// <summary>Candlestick bearish (close &lt; open) color.</summary>
    [Export] public Color CandlestickBearishColor { get; set; } = new(0.98f, 0.45f, 0.29f);

    /// <summary>Hover wick width multiplier for candlestick marks.</summary>
    [Export] public float CandlestickHoverWickScale { get; set; } = 1.5f;

    /// <summary>Stroke width for hollow (bullish) candlestick bodies.</summary>
    [Export] public float CandlestickHollowStrokeWidth { get; set; } = 1.5f;

    // ═══════════════════════════════════════════════════════
    //  Feature Toggles
    // ═══════════════════════════════════════════════════════

    /// <summary>Enable entry/exit animations.</summary>
    [ExportGroup("Feature Toggles")]
    [Export] public bool EnableAnimation { get; set; } = true;

    /// <summary>Enable hover highlight effect (brighten + scale).</summary>
    [Export] public bool EnableHoverHighlight { get; set; } = true;

    /// <summary>Enable hover explode offset on polar segments (pie, sunburst).</summary>
    [Export] public bool EnableHoverExplode { get; set; } = true;

    /// <summary>Enable selection stroke outline.</summary>
    [Export] public bool EnableSelection { get; set; } = true;

    /// <summary>Enable crosshair overlay on hover.</summary>
    [Export] public bool EnableCrosshair { get; set; } = true;

    /// <summary>Enable tooltip display on hover.</summary>
    [Export] public bool EnableTooltip { get; set; } = true;

    // ═══════════════════════════════════════════════════════
    //  Lollipop Mark
    // ═══════════════════════════════════════════════════════

    /// <summary>Hover dot scale multiplier for lollipop marks.</summary>
    [ExportGroup("Lollipop Mark")]
    [Export] public float LollipopHoverScale { get; set; } = 1.3f;

    // ═══════════════════════════════════════════════════════
    //  Range Area Mark
    // ═══════════════════════════════════════════════════════

    /// <summary>Hover point indicator radius for range area marks in pixels.</summary>
    [ExportGroup("Range Area Mark")]
    [Export] public float RangeAreaPointRadius { get; set; } = 4f;

    // ═══════════════════════════════════════════════════════
    //  Chord Mark
    // ═══════════════════════════════════════════════════════

    /// <summary>Distance from outer arc to node label in pixels.</summary>
    [ExportGroup("Chord Mark")]
    [Export] public float ChordLabelDistance { get; set; } = 8f;

    // ═══════════════════════════════════════════════════════
    //  Sunburst Mark
    // ═══════════════════════════════════════════════════════

    /// <summary>Base opacity for Sunburst arc segments.</summary>
    [ExportGroup("Sunburst Mark")]
    [Export] public float SunburstArcOpacity { get; set; } = 0.85f;

    /// <summary>Minimum sweep angle in radians for label display on sunburst arcs.</summary>
    [Export] public float SunburstLabelMinSweep { get; set; } = 0.15f;

    // ═══════════════════════════════════════════════════════
    //  Hit Test Tolerances
    // ═══════════════════════════════════════════════════════

    /// <summary>Extra padding (in pixels) added to point/dot radius for hit detection.</summary>
    [ExportGroup("Hit Test")]
    [Export] public float HitTestPointPadding { get; set; } = 4f;

    /// <summary>Pixel distance threshold for nearest-point snapping (line, radar marks).</summary>
    [Export] public float HitTestSnapDistance { get; set; } = 12f;

    /// <summary>Pixel distance for area/range mark X-axis snapping.</summary>
    [Export] public float HitTestAreaSnapDistance { get; set; } = 20f;

    /// <summary>Angle tolerance in radians for chord arc hit detection.</summary>
    [Export] public float HitTestAngleTolerance { get; set; } = 0.2f;

    // ═══════════════════════════════════════════════════════
    //  Factory Methods
    // ═══════════════════════════════════════════════════════

    /// <summary>Default categorical 6-color palette.</summary>
    public static readonly Color[] DefaultPalette =
    [
        new(0.29f, 0.59f, 0.98f), // blue
        new(0.98f, 0.45f, 0.29f), // orange
        new(0.29f, 0.85f, 0.60f), // green
        new(0.98f, 0.80f, 0.29f), // yellow
        new(0.75f, 0.29f, 0.98f), // purple
        new(0.29f, 0.92f, 0.98f), // cyan
    ];

    /// <summary>Default 5-stop sequential gradient.</summary>
    public static readonly Color[] DefaultSequentialGradient =
    [
        new(0.12f, 0.07f, 0.53f), // deep purple
        new(0.29f, 0.00f, 0.73f), // purple
        new(0.85f, 0.24f, 0.31f), // red
        new(0.99f, 0.68f, 0.38f), // orange
        new(0.99f, 0.95f, 0.70f), // light yellow
    ];

    /// <summary>Create the default dark theme matching the current hardcoded values.</summary>
    public static ChartTheme Dark() => new();

    /// <summary>
    /// Create a deep clone of this theme. Useful for deriving custom themes
    /// from an existing one without affecting the original.
    /// </summary>
    public ChartTheme Clone()
    {
        var clone = (ChartTheme)MemberwiseClone();
        // Deep copy array properties to prevent shared mutation
        clone.Palette = (Color[])Palette.Clone();
        clone.SequentialGradient = (Color[])SequentialGradient.Clone();
        return clone;
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
