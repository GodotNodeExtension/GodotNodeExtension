using System;
using System.Collections.Generic;
using Godot;
using GodotNodeExtension.Component.GodotChart;
using GodotNodeExtension.Component.GodotChart.Marks;

namespace GodotNodeExtension.Example.GodotChart;

/// <summary>
/// The polar / proportion marks - <see cref="PieMark"/>, <see cref="GaugeMark"/>, <see cref="RadarMark"/> and
/// <see cref="FunnelMark"/> - one <see cref="ChartView"/> per cell. The scene holds the layout and the nodes
/// (wired through <c>[Export]</c> + <c>node_paths</c>); this script does the configuring and every cell goes
/// through <see cref="ChartView.ConfigureMark"/>, so the file reads as that mark's own knob list:
/// <list type="bullet">
/// <item><see cref="PieMark"/>: <see cref="PieMark.InnerRadius"/> (0 = pie, greater than 0 = ring),
/// <see cref="PieMark.StartAngle"/>, <see cref="PieMark.ExplodeRatio"/>, <see cref="PieMark.RadiusFactor"/>,
/// <see cref="PieMark.ShowLabel"/>, <see cref="PieMark.LabelDistance"/>,
/// <see cref="PieMark.SliceLabelBuilder"/>, and the donut center
/// (<see cref="PieMark.CenterText"/>, <see cref="PieMark.CenterFontSize"/>,
/// <see cref="PieMark.CenterSubFontSize"/>, <see cref="PieMark.CenterContentBuilder"/>);</item>
/// <item><see cref="GaugeMark"/>: <see cref="GaugeMark.StartAngleDeg"/> / <see cref="GaugeMark.EndAngleDeg"/>,
/// <see cref="GaugeMark.ArcWidth"/>, <see cref="GaugeMark.TrackColor"/> / <see cref="GaugeMark.ValueColor"/>,
/// <see cref="GaugeMark.ShowCenterLabel"/>, <see cref="GaugeMark.ShowMinMaxLabels"/>,
/// <see cref="GaugeMark.InnerRadiusRatio"/> and <see cref="GaugeMark.RadiusFactor"/>;</item>
/// <item><see cref="RadarMark"/>: <see cref="RadarMark.GridRings"/>, <see cref="RadarMark.ShowGrid"/>,
/// <see cref="RadarMark.ShowAxisLabels"/>, <see cref="RadarMark.FillOpacity"/>,
/// <see cref="RadarMark.StrokeWidth"/>, <see cref="RadarMark.PointRadius"/> and
/// <see cref="RadarMark.RadiusFactor"/>, over two series with a legend;</item>
/// <item><see cref="FunnelMark"/>: <see cref="FunnelMark.StageGap"/>, <see cref="FunnelMark.MinWidthRatio"/>
/// and <see cref="FunnelMark.CornerRadius"/>.</item>
/// </list>
/// <para>
/// All four marks report <see cref="MarkCoordinate.Polar"/>, so the chart draws no Cartesian axes and no grid
/// for them - a radar or funnel brings its own polar grid, a pie or gauge needs none. Pie, donut, gauge and
/// funnel read <c>category</c> / <c>value</c> rows; the radar reads <c>category</c> / <c>value</c> /
/// <c>series</c> rows, one dimension per row and one polygon per series.
/// </para>
/// <para>
/// The scene captions are the short form - just the knobs and the values this demo sets. The why, and the
/// library defaults those values are read against, lives here: <see cref="PieMark.InnerRadius"/> 0,
/// <see cref="PieMark.StartAngle"/> -PI/2 (radians), <see cref="PieMark.ExplodeRatio"/> null (which means
/// theme.PieExplodeRatio, 0.03), <see cref="PieMark.RadiusFactor"/> 0.85, <see cref="PieMark.ShowLabel"/>
/// true, <see cref="PieMark.LabelDistance"/> 1.15, <see cref="PieMark.SliceLabelBuilder"/> null,
/// <see cref="PieMark.CenterFontSize"/> 18 and <see cref="PieMark.CenterSubFontSize"/> 12 - with
/// <see cref="ChartKind.Donut"/> presetting <see cref="PieMark.InnerRadius"/> to 0.55. A
/// <see cref="GaugeMark"/> defaults to <see cref="GaugeMark.StartAngleDeg"/> -210 and
/// <see cref="GaugeMark.EndAngleDeg"/> 30 (degrees from the positive X axis, growing clockwise on screen, so
/// the pair sweeps 240 degrees), <see cref="GaugeMark.ArcWidth"/> 0.12, <see cref="GaugeMark.TrackColor"/>
/// null (theme.GaugeTrackColor), <see cref="GaugeMark.ShowCenterLabel"/> /
/// <see cref="GaugeMark.ShowMinMaxLabels"/> true, <see cref="GaugeMark.InnerRadiusRatio"/> 0 and
/// <see cref="GaugeMark.RadiusFactor"/> 0.85; a <see cref="RadarMark"/> to <see cref="RadarMark.GridRings"/> 5,
/// <see cref="RadarMark.ShowGrid"/> / <see cref="RadarMark.ShowAxisLabels"/> true,
/// <see cref="RadarMark.FillOpacity"/> 0.15, <see cref="RadarMark.StrokeWidth"/> 2,
/// <see cref="RadarMark.PointRadius"/> 3 and <see cref="RadarMark.RadiusFactor"/> 0.85; a
/// <see cref="FunnelMark"/> to <see cref="FunnelMark.StageGap"/> 4,
/// <see cref="FunnelMark.MinWidthRatio"/> 0.15 and <see cref="FunnelMark.CornerRadius"/> 3.
/// </para>
/// <para>
/// What the short captions leave out. A pie divides <see cref="PieMark.RadiusFactor"/> by
/// <see cref="PieMark.LabelDistance"/> while <see cref="PieMark.ShowLabel"/> is on, so the outside labels fit.
/// The donut hole text is drawn only while <see cref="PieMark.InnerRadius"/> is above 0 and the opening
/// animation has finished; <see cref="PieMark.CenterContentBuilder"/> wins over
/// <see cref="PieMark.CenterText"/>, and both render line one at <see cref="PieMark.CenterFontSize"/> and the
/// later lines at <see cref="PieMark.CenterSubFontSize"/> (the rich one lays out its spans, each with its own
/// colour and weight, itself); <see cref="PieMark.SliceLabelBuilder"/> replaces the category text of a slice
/// and is handed that slice's value and share. A <see cref="GaugeMark"/> draws the first row only, and because
/// it brings no domain of its own the view pins <see cref="ChartView.YAxisRange"/> to 0..100 - that pin is
/// what makes the value read as a percentage and what <see cref="GaugeMark.ShowMinMaxLabels"/> prints - and
/// <see cref="GaugeMark.ArcWidth"/> is only used while <see cref="GaugeMark.InnerRadiusRatio"/> is 0.
/// <see cref="RadarMark"/> needs at least three dimensions, <see cref="RadarMark.GridRings"/> has nothing to
/// count while <see cref="RadarMark.ShowGrid"/> is off - <see cref="RadarMark.ShowAxisLabels"/> is independent
/// of it, so the dimension names survive - and the colour field both colours the polygons and fills the
/// legend. A <see cref="FunnelMark"/> stage takes what is left of the plot height after the gaps, its width
/// lerps between <see cref="FunnelMark.MinWidthRatio"/> and the full width by value over the largest value,
/// and <see cref="FunnelMark.ShowLabel"/> prints <c>label: value</c> inside each stage. All of these marks are
/// <see cref="MarkCoordinate.Polar"/>: no Cartesian axes and no Cartesian grid anywhere on this page - the
/// radar and the funnel are self-contained, the pie and the gauge need neither.
/// </para>
/// </summary>
public partial class ChartMarksPolarDemo : MarginContainer
{
    /// <summary>Pie cell: the plain pie knobs (inner radius 0, start angle, explode, radius, labels).</summary>
    [Export] public ChartView PieChart { get; set; } = null!;

    /// <summary>Donut cell whose hole is a static multi-line <c>CenterText</c>.</summary>
    [Export] public ChartView DonutTextChart { get; set; } = null!;

    /// <summary>Donut cell whose hole is drawn by a rich <c>CenterContentBuilder</c>.</summary>
    [Export] public ChartView DonutRichChart { get; set; } = null!;

    /// <summary>Gauge cell using the library default sweep and the color knobs.</summary>
    [Export] public ChartView GaugeChart { get; set; } = null!;

    /// <summary>Gauge cell using an explicit inner radius and the label switches off.</summary>
    [Export] public ChartView GaugeRingChart { get; set; } = null!;

    /// <summary>Radar cell with two series, four grid rings and both polar-grid switches on.</summary>
    [Export] public ChartView RadarChart { get; set; } = null!;

    /// <summary>Radar cell with the grid off but the axis labels still on.</summary>
    [Export] public ChartView RadarGridlessChart { get; set; } = null!;

    /// <summary>Funnel cell showing the stage knobs (gap, minimum width ratio, corner radius).</summary>
    [Export] public ChartView FunnelChart { get; set; } = null!;

    /// <inheritdoc />
    public override void _Ready()
    {
        ConfigurePie();
        ConfigureDonutCenterText();
        ConfigureDonutRichCenter();
        ConfigureGauge();
        ConfigureGaugeRing();
        ConfigureRadar();
        ConfigureRadarWithoutGrid();
        ConfigureFunnel();
    }

    // ── PieMark ─────────────────────────────────────────────────────────────

    /// <summary>
    /// <see cref="PieMark.InnerRadius"/> stays 0 (the library default: a full pie),
    /// <see cref="PieMark.StartAngle"/> rotates the whole layout,
    /// <see cref="PieMark.ExplodeRatio"/> is the hover explode distance and
    /// <see cref="PieMark.RadiusFactor"/> with <see cref="PieMark.LabelDistance"/> sizes the pie so the
    /// outside labels fit. <see cref="PieMark.SliceLabelBuilder"/> replaces the default category label.
    /// </summary>
    private void ConfigurePie()
    {
        PieChart.ColorField = "category";   // one palette colour per slice; the colour field also fills the legend
        PieChart.SetData(PieRows());
        PieChart.ConfigureMark(mark =>
        {
            var pie = (PieMark)mark;
            pie.InnerRadius = 0f;            // the library default: 0 = pie, > 0 = donut ring
            pie.StartAngle = MathF.PI / 2f;  // the default -PI/2 starts at the top; PI/2 starts the first slice at the bottom
            pie.ExplodeRatio = 0.08f;        // null (the default) falls back to theme.PieExplodeRatio (0.03)
            pie.RadiusFactor = 0.8f;         // default 0.85
            pie.ShowLabel = true;            // the library default
            pie.LabelDistance = 1.2f;        // default 1.15
            pie.SliceLabelBuilder = ctx => $"{ctx.DefaultText} {ctx.Value:0}/{ctx.Percentage * 100f:0.#}%";
        });
    }

    /// <summary>
    /// The donut ring plus the static center text: <see cref="PieMark.CenterText"/> splits on <c>\n</c>, the
    /// first line renders at <see cref="PieMark.CenterFontSize"/> and every later line at
    /// <see cref="PieMark.CenterSubFontSize"/>. Shown only while the inner radius is above 0.
    /// </summary>
    private void ConfigureDonutCenterText()
    {
        DonutTextChart.ColorField = "category";
        DonutTextChart.SetData(PieRows());
        DonutTextChart.ConfigureMark(mark =>
        {
            var pie = (PieMark)mark;
            pie.InnerRadius = 0.62f;          // the Kind = Donut preset is 0.55; 0 would be a full pie again
            pie.RadiusFactor = 0.95f;         // default 0.85
            pie.ShowLabel = false;            // the outside labels (and LabelDistance) are off
            pie.CenterText = "1200\nvisits this week";  // '\n' starts the second, smaller line
            pie.CenterFontSize = 20f;         // default 18, used by the first line
            pie.CenterSubFontSize = 13f;      // default 12, used by the later lines
        });
    }

    /// <summary>
    /// The same ring with a <see cref="PieMark.CenterContentBuilder"/> instead of
    /// <see cref="PieMark.CenterText"/>: it receives the whole <see cref="LabelContext"/> and returns rich
    /// <see cref="TooltipLine"/> spans, so every span of the hole text carries its own colour and weight.
    /// The builder wins over the still-set <c>CenterText</c>.
    /// </summary>
    private void ConfigureDonutRichCenter()
    {
        DonutRichChart.ColorField = "category";
        DonutRichChart.SetData(PieRows());
        DonutRichChart.ConfigureMark(mark =>
        {
            var pie = (PieMark)mark;
            pie.InnerRadius = 0.58f;         // default 0 (pie); the Kind = Donut preset is 0.55
            pie.RadiusFactor = 0.9f;         // default 0.85
            pie.ShowLabel = true;            // the library default: the slices keep their category labels
            pie.LabelDistance = 1.15f;       // the library default
            pie.CenterFontSize = 22f;        // default 18
            pie.CenterSubFontSize = 12f;     // the library default
            pie.CenterText = "not drawn";    // set on purpose: CenterContentBuilder takes priority
            pie.CenterContentBuilder = ctx => new List<TooltipLine>
            {
                new() { Spans = [new TooltipSpan { Text = "Sessions", Bold = true }] },
                new()
                {
                    Spans =
                    [
                        new TooltipSpan { Text = "1.2M", Color = new Color(0.98f, 0.72f, 0.31f), Bold = true },
                        new TooltipSpan { Text = $" / {ctx.Data.Count} channels" },
                    ],
                },
            };
        });
    }

    // ── GaugeMark ───────────────────────────────────────────────────────────

    /// <summary>
    /// The default sweep of a <see cref="GaugeMark"/> with the two arc colours.
    /// <see cref="GaugeMark.StartAngleDeg"/> / <see cref="GaugeMark.EndAngleDeg"/> are degrees measured from
    /// the positive X axis and growing clockwise on screen; <see cref="GaugeMark.ArcWidth"/> only applies
    /// while <see cref="GaugeMark.InnerRadiusRatio"/> is 0. The view pins the Y domain, which is what turns
    /// the single value into a percentage.
    /// </summary>
    private void ConfigureGauge()
    {
        GaugeChart.YAxisRange = new Vector2(0f, 100f);   // a view export, not a mark knob: pin the 0..100 domain
        GaugeChart.SetData(GaugeRows());
        GaugeChart.ConfigureMark(mark =>
        {
            var gauge = (GaugeMark)mark;
            gauge.StartAngleDeg = -210f;                 // the library default (roughly the 8 o'clock position)
            gauge.EndAngleDeg = 30f;                     // the library default (roughly 4 o'clock): 240 degrees
            gauge.ArcWidth = 0.14f;                      // default 0.12, a fraction of the outer radius
            gauge.TrackColor = new Color(0.24f, 0.34f, 0.5f, 0.55f);   // null (the default) = theme.GaugeTrackColor
            gauge.ValueColor = new Color(0.36f, 0.83f, 0.72f);         // null (the default) = theme.DefaultMarkColor
            gauge.ShowCenterLabel = true;                // the library default
            gauge.ShowMinMaxLabels = true;               // the library default
            gauge.InnerRadiusRatio = 0f;                 // the library default: the ring comes from ArcWidth
            gauge.RadiusFactor = 0.85f;                  // the library default
        });
    }

    /// <summary>
    /// The same mark, the other half of its knobs: <see cref="GaugeMark.InnerRadiusRatio"/> sets the hole
    /// directly and replaces <see cref="GaugeMark.ArcWidth"/>, a different angle pair changes the sweep, and
    /// both text switches are off.
    /// </summary>
    private void ConfigureGaugeRing()
    {
        GaugeRingChart.YAxisRange = new Vector2(0f, 100f);
        GaugeRingChart.SetData(GaugeRows());
        GaugeRingChart.ConfigureMark(mark =>
        {
            var gauge = (GaugeMark)mark;
            gauge.StartAngleDeg = 120f;                  // default -210
            gauge.EndAngleDeg = 60f;                     // default 30: this pair sweeps 300 instead of 240 degrees
            gauge.InnerRadiusRatio = 0.72f;              // default 0: the hole takes over from ArcWidth
            gauge.ArcWidth = 0.12f;                      // the library default, ignored while InnerRadiusRatio > 0
            gauge.ShowMinMaxLabels = false;              // default true: the min/max texts at the arc ends go
            gauge.ShowCenterLabel = false;               // default true: the value in the middle goes
            gauge.TrackColor = null;                     // null (the default) = the theme track colour
            gauge.ValueColor = new Color(0.98f, 0.72f, 0.31f);   // null (the default) = theme.DefaultMarkColor
            gauge.RadiusFactor = 0.95f;                  // default 0.85
        });
    }

    // ── RadarMark ───────────────────────────────────────────────────────────

    /// <summary>
    /// Two series of a <see cref="RadarMark"/> (one polygon per <c>series</c> value, and the same colour
    /// field fills the legend). The polar grid is the mark's own: <see cref="RadarMark.GridRings"/> counts
    /// the rings, <see cref="RadarMark.ShowGrid"/> draws them and <see cref="RadarMark.ShowAxisLabels"/> the
    /// dimension names. The polygons are styled by <see cref="RadarMark.FillOpacity"/>,
    /// <see cref="RadarMark.StrokeWidth"/> and <see cref="RadarMark.PointRadius"/>.
    /// </summary>
    private void ConfigureRadar()
    {
        RadarChart.ColorField = "series";
        RadarChart.ColorMapping = ColorMappingKind.Category;   // one palette colour per series, and the legend
        RadarChart.Legend = LegendPosition.Bottom;             // the view default, spelled out: the colour field fills it
        RadarChart.SetData(RadarRows());
        RadarChart.ConfigureMark(mark =>
        {
            var radar = (RadarMark)mark;
            radar.GridRings = 4;          // default 5 concentric rings
            radar.ShowGrid = true;        // the library default
            radar.ShowAxisLabels = true;  // the library default
            radar.FillOpacity = 0.25f;    // default 0.15
            radar.StrokeWidth = 2.5f;     // default 2, the polygon outline
            radar.PointRadius = 4f;       // default 3, the dot at every vertex
            radar.RadiusFactor = 0.8f;    // default 0.85
        });
    }

    /// <summary>
    /// The grid switches are independent: <see cref="RadarMark.ShowGrid"/> off leaves the polygons, their
    /// dots and the dimension labels, because <see cref="RadarMark.ShowAxisLabels"/> is still on.
    /// <see cref="RadarMark.GridRings"/> then has nothing to count.
    /// </summary>
    private void ConfigureRadarWithoutGrid()
    {
        RadarGridlessChart.ColorField = "series";
        RadarGridlessChart.ColorMapping = ColorMappingKind.Category;
        RadarGridlessChart.Legend = LegendPosition.Bottom;
        RadarGridlessChart.SetData(RadarRows());
        RadarGridlessChart.ConfigureMark(mark =>
        {
            var radar = (RadarMark)mark;
            radar.ShowGrid = false;       // no rings, no spokes, no radius ticks
            radar.ShowAxisLabels = true;  // unaffected by ShowGrid: the dimension names stay
            radar.GridRings = 5;          // the library default - no effect while ShowGrid is false
            radar.FillOpacity = 0.35f;    // default 0.15
            radar.StrokeWidth = 3f;       // default 2
            radar.PointRadius = 0f;       // default 3: the vertex dots are gone
            radar.RadiusFactor = 0.85f;   // the library default
        });
    }

    // ── FunnelMark ──────────────────────────────────────────────────────────

    /// <summary>
    /// The funnel stage knobs: <see cref="FunnelMark.StageGap"/> separates the stages,
    /// <see cref="FunnelMark.MinWidthRatio"/> is the width floor of a stage and
    /// <see cref="FunnelMark.CornerRadius"/> rounds it.
    /// </summary>
    private void ConfigureFunnel()
    {
        FunnelChart.ColorField = "category";   // one palette colour per stage, which also fills the legend
        FunnelChart.SetData(FunnelRows());
        FunnelChart.ConfigureMark(mark =>
        {
            var funnel = (FunnelMark)mark;
            funnel.StageGap = 10f;        // default 4 pixels between stages
            funnel.MinWidthRatio = 0.35f; // default 0.15: the narrowest stage as a fraction of the plot width
            funnel.CornerRadius = 8f;     // default 3; 0 would draw plain rectangles
            funnel.ShowLabel = true;      // the library default: "stage: value" inside each stage
        });
    }

    // ── Rows (code, not the scene) ──────────────────────────────────────────

    /// <summary>Category/value rows for the pie and donut cells; the values add up to 100.</summary>
    private static DataRow[] PieRows() =>
    [
        new DataRow(2).Set("category", "Search").Set("value", 42.0),
        new DataRow(2).Set("category", "Direct").Set("value", 28.0),
        new DataRow(2).Set("category", "Social").Set("value", 18.0),
        new DataRow(2).Set("category", "Email").Set("value", 12.0),
    ];

    /// <summary>
    /// Category/value rows for the gauge cells. A gauge reads the first row only, so the second row is
    /// supplied on purpose to show that it is ignored.
    /// </summary>
    private static DataRow[] GaugeRows() =>
    [
        new DataRow(2).Set("category", "Completion").Set("value", 68.0),
        new DataRow(2).Set("category", "Target").Set("value", 85.0),   // never drawn
    ];

    /// <summary>The six radar dimensions, in the order the axes are laid out around the circle.</summary>
    private static readonly string[] RadarDimensions =
        ["Speed", "Power", "Accuracy", "Stamina", "Agility", "Vision"];

    /// <summary>One value per <see cref="RadarDimensions"/> entry, for the first radar series.</summary>
    private static readonly double[] RadarSeriesA = [82.0, 74.0, 91.0, 68.0, 77.0, 85.0];

    /// <summary>One value per <see cref="RadarDimensions"/> entry, for the second radar series.</summary>
    private static readonly double[] RadarSeriesB = [70.0, 88.0, 64.0, 79.0, 83.0, 61.0];

    /// <summary>Six dimensions for two series: category/value/series rows, one row per dimension and series.</summary>
    private static DataRow[] RadarRows()
    {
        var rows = new List<DataRow>(12);
        AddRadarSeries(rows, "Player A", RadarSeriesA);
        AddRadarSeries(rows, "Player B", RadarSeriesB);
        return [.. rows];
    }

    /// <summary>Append one row per dimension for a single series.</summary>
    private static void AddRadarSeries(List<DataRow> rows, string series, double[] values)
    {
        for (int i = 0; i < RadarDimensions.Length; i++)
        {
            rows.Add(new DataRow(3)
                .Set("category", RadarDimensions[i])
                .Set("value", values[i])
                .Set("series", series));
        }
    }

    /// <summary>Category/value rows for the funnel, one decreasing stage each.</summary>
    private static DataRow[] FunnelRows() =>
    [
        new DataRow(2).Set("category", "Visit").Set("value", 1200.0),
        new DataRow(2).Set("category", "Signup").Set("value", 620.0),
        new DataRow(2).Set("category", "Trial").Set("value", 310.0),
        new DataRow(2).Set("category", "Paid").Set("value", 140.0),
    ];
}
