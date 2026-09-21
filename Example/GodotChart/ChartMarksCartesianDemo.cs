using System.Collections.Generic;
using Godot;
using GodotNodeExtension.Component.GodotChart;
using GodotNodeExtension.Component.GodotChart.Marks;

namespace GodotNodeExtension.Example.GodotChart;

/// <summary>
/// Tour of the Cartesian marks: one grid cell per mark (or per knob group), each cell a
/// <see cref="ChartView"/> node whose mark is reached through <see cref="ChartView.ConfigureMark"/> -
/// so every knob named in a caption is a property of the mark class under
/// <c>Component/GodotChart/Marks</c>, not of the node. The scene holds the layout and the node
/// references, this script holds the data and the knobs.
/// <list type="bullet">
/// <item><see cref="IntervalMark"/>: BarPadding, CornerRadius, Stack, Orientation;</item>
/// <item><see cref="LineMark"/>: Smooth, Step, ShowArea, AreaOpacity, StrokeWidth, Stack;</item>
/// <item><see cref="PointMark"/>: DefaultRadius, and MinRadius + RadiusRange through the Size channel;</item>
/// <item><see cref="RangeAreaMark"/>: LowerField, FillOpacity, ShowBorderLines, Smooth;</item>
/// <item><see cref="BoxMark"/>: the five field names, BoxWidthRatio, WhiskerWidth, CornerRadius, BoxColor, LineColor;</item>
/// <item><see cref="CandlestickMark"/>: the four OHLC field names, BullishColor / BearishColor, BodyWidthRatio, WickWidth, FillBullish;</item>
/// <item><see cref="HeatmapMark"/>: CellGap, CornerRadius;</item>
/// <item><see cref="TimelineMark"/>: StartField / EndField, BarHeightRatio, CornerRadius;</item>
/// <item><see cref="MilestoneMark"/>: LabelField, MarkerRadius, MarkerShape, ShowAxisLine, AlternateLabels;</item>
/// <item><see cref="LollipopMark"/>: DotRadius, StemWidth, Orientation;</item>
/// <item><see cref="ViolinMark"/>: BinCount, WidthRatio, FillOpacity, ShowMedian, ShowBox;</item>
/// <item><see cref="WaffleMark"/>: TotalCells, Columns, CellGap, CellRadius.</item>
/// </list>
/// <para>
/// The scene captions are deliberately terse - knob name, value, separator - so the layouts stay
/// comparable at a glance; the list above plus these notes are the full story of every cell:
/// <list type="bullet">
/// <item>the rows always carry the field names the mark reads itself (<c>lower</c>,
/// <c>min/q1/median/q3/max</c>, <c>open/high/low/close</c>, <c>start/end</c>, <c>label</c>), so the
/// field-name knobs are the interesting ones: LowerField, MinField..MaxField,
/// OpenField..CloseField, StartField / EndField, LabelField;</item>
/// <item><see cref="BoxMark"/> and <see cref="CandlestickMark"/> define their own value range
/// (<see cref="Mark.ContributeScales"/>), so ChartView binds the category channel only for those
/// kinds - a value field on the chart would drag the axis down to zero and flatten them;</item>
/// <item>library defaults the demo moves away from: BarPadding 0.2, CornerRadius 3, StrokeWidth 2,
/// AreaOpacity 0.15, ShowArea false, DefaultRadius 5, FillOpacity 0.3 (RangeArea) / 0.5 (Violin),
/// BoxWidthRatio 0.5, WhiskerWidth 1.5, BodyWidthRatio 0.6, WickWidth 1.5, CellGap 1 (Heatmap) /
/// 2 (Waffle), CellRadius 2, BarHeightRatio 0.6, MarkerRadius 6, BinCount 20, WidthRatio 0.7,
/// Columns 10; MinRadius / RadiusRange default to the theme's PointSizeMin (3) / PointSizeRange (20);
/// </item>
/// <item>knobs a caption drops to stay inside its two line budget, all of them set in this script:
/// CornerRadius 2 (stacked bar), BarPadding 0.3 / CornerRadius 4 (horizontal bar), YField = upper
/// (RangeArea), WhiskerWidth 3 / CornerRadius 6 / BoxColor / LineColor (Box), BodyWidthRatio 0.5 /
/// WickWidth 2.5 / BullishColor / BearishColor (Candlestick), CornerRadius 6 (Heatmap),
/// XField = lane / YField = to / ColorField = lane (Timeline), ShowAxisLine / AlternateLabels and the
/// two axis fields (Milestone), YField = category (horizontal lollipop), ShowMedian = true (Violin);</item>
/// <item>the size-mapping cell sets no SizeRange on the node: ChartView overwrites a PointMark's
/// MinRadius / RadiusRange from that export, and the inferred size scale already normalises the field
/// to 0..1, which is exactly what the radius pair expects;</item>
/// <item><see cref="LineMark"/> gives Step priority over Smooth (BuildLinePath), which is why the step
/// cell switches Smooth off;</item>
/// <item>the two stacking modes share one data set, so the difference is only the pile: <see cref="StackMode.Stack"/>
/// draws the totals against the value axis, <see cref="StackMode.Normalize"/> scales every category to 1
/// (hidden series do not count towards the total, and a category whose visible total is &lt;= 0 is drawn as
/// zero with a warning). <see cref="LineMark.Stack"/> is the same knob on the line mark - it is what makes the
/// stacked-area cell, and the hover snap there follows the accumulated baseline the renderer draws;</item>
/// <item><see cref="BarOrientation.Horizontal"/> swaps the axes inside the mark, so those two cells bind
/// XField to the number and YField to the category; ChartKind.Timeline is horizontal by design and maps
/// XField to the lane axis and YField to the value axis;</item>
/// <item><see cref="MilestoneMark"/> reads LabelField by name because the node never encodes the Label
/// channel; <see cref="HeatmapMark"/> installs its own sequential colour scale (not the palette) and
/// fills its rows top-down in first-appearance order; <see cref="WaffleMark"/> reports
/// <see cref="Mark.UsesAxes"/> as false, so that cell draws no axes or grid.</item>
/// </list>
/// </para>
/// </summary>
public partial class ChartMarksCartesianDemo : Control
{
    /// <summary>Bars with a wide gap and rounded corners.</summary>
    [Export] public ChartView IntervalChart { get; set; } = null!;

    /// <summary>Bars stacked per category (<see cref="IntervalMark.Stack"/>).</summary>
    [Export] public ChartView IntervalStackedChart { get; set; } = null!;

    /// <summary>The same pile normalised, so every category sums to one (<see cref="StackMode.Normalize"/>).</summary>
    [Export] public ChartView IntervalNormalizeChart { get; set; } = null!;

    /// <summary>Bars laid out flat (<see cref="IntervalMark.Orientation"/>).</summary>
    [Export] public ChartView IntervalHorizontalChart { get; set; } = null!;

    /// <summary>A smoothed line with an area fill under it.</summary>
    [Export] public ChartView LineSmoothChart { get; set; } = null!;

    /// <summary>A staircase line (<see cref="LineMark.Step"/>).</summary>
    [Export] public ChartView LineStepChart { get; set; } = null!;

    /// <summary>A stacked area: piling bands instead of overlaying them (<see cref="LineMark.Stack"/>).</summary>
    [Export] public ChartView LineStackChart { get; set; } = null!;

    /// <summary>Dots at a fixed radius (<see cref="PointMark.DefaultRadius"/>).</summary>
    [Export] public ChartView PointRadiusChart { get; set; } = null!;

    /// <summary>Dots sized by a data field (<see cref="PointMark.MinRadius"/> / <see cref="PointMark.RadiusRange"/>).</summary>
    [Export] public ChartView PointSizeChart { get; set; } = null!;

    /// <summary>A band between two bounds (<see cref="RangeAreaMark"/>).</summary>
    [Export] public ChartView RangeAreaChart { get; set; } = null!;

    /// <summary>Box-and-whisker plots with renamed quartile fields.</summary>
    [Export] public ChartView BoxChart { get; set; } = null!;

    /// <summary>OHLC candles with renamed price fields and hollow bodies.</summary>
    [Export] public ChartView CandlestickChart { get; set; } = null!;

    /// <summary>A value matrix (<see cref="HeatmapMark"/>).</summary>
    [Export] public ChartView HeatmapChart { get; set; } = null!;

    /// <summary>Bars spanning a start and an end value (<see cref="TimelineMark"/>).</summary>
    [Export] public ChartView TimelineChart { get; set; } = null!;

    /// <summary>Events on a numeric axis (<see cref="MilestoneMark"/>).</summary>
    [Export] public ChartView MilestoneChart { get; set; } = null!;

    /// <summary>Stems with a dot on top (<see cref="LollipopMark"/>), vertical.</summary>
    [Export] public ChartView LollipopChart { get; set; } = null!;

    /// <summary>The same mark laid out flat (<see cref="LollipopMark.Orientation"/>).</summary>
    [Export] public ChartView LollipopHorizontalChart { get; set; } = null!;

    /// <summary>Density silhouettes per category (<see cref="ViolinMark"/>).</summary>
    [Export] public ChartView ViolinChart { get; set; } = null!;

    /// <summary>A proportion grid (<see cref="WaffleMark"/>).</summary>
    [Export] public ChartView WaffleChart { get; set; } = null!;

    /// <inheritdoc />
    public override void _Ready()
    {
        // The scene captions name only the knobs a cell sets (knob + value); the full per-cell list,
        // the values that differ from the library defaults and the quirks behind them live in the class
        // summary above, which is the checklist to read alongside the captions.
        ConfigureInterval();
        ConfigureLine();
        ConfigurePoint();
        ConfigureRangeArea();
        ConfigureBox();
        ConfigureCandlestick();
        ConfigureHeatmap();
        ConfigureTimeline();
        ConfigureMilestone();
        ConfigureLollipop();
        ConfigureViolin();
        ConfigureWaffle();
    }

    /// <summary><see cref="IntervalMark"/>: bar geometry, then the stack mode and the horizontal orientation.</summary>
    private void ConfigureInterval()
    {
        IntervalChart.ConfigureMark(mark =>
        {
            var bars = (IntervalMark)mark;
            bars.BarPadding = 0.45f;      // default 0.2: fraction of the band left empty
            bars.CornerRadius = 9f;       // default 3: pixels, all four corners
        });
        IntervalChart.SetValues(
            [("Mon", 24.0), ("Tue", 38.0), ("Wed", 31.0), ("Thu", 47.0), ("Fri", 29.0), ("Sat", 52.0)]);

        // Stacking groups the rows by the colour channel and piles them per category; the mark also
        // refits the value axis to the tallest category total (ContributeScales).
        IntervalStackedChart.ColorField = "series";
        IntervalStackedChart.ConfigureMark(mark =>
        {
            var bars = (IntervalMark)mark;
            bars.Stack = StackMode.Stack;   // default StackMode.None
            bars.BarPadding = 0.3f;
            bars.CornerRadius = 2f;
        });
        DataRow[] stackedRows =
        [
            Row(("category", "Q1"), ("value", 24.0), ("series", "retail")),
            Row(("category", "Q1"), ("value", 16.0), ("series", "online")),
            Row(("category", "Q1"), ("value", 9.0), ("series", "wholesale")),
            Row(("category", "Q2"), ("value", 28.0), ("series", "retail")),
            Row(("category", "Q2"), ("value", 12.0), ("series", "online")),
            Row(("category", "Q2"), ("value", 14.0), ("series", "wholesale")),
            Row(("category", "Q3"), ("value", 22.0), ("series", "retail")),
            Row(("category", "Q3"), ("value", 19.0), ("series", "online")),
            Row(("category", "Q3"), ("value", 8.0), ("series", "wholesale")),
        ];
        IntervalStackedChart.SetData(stackedRows);

        // Normalize draws the same pile scaled so every category reaches 1, which is why its axis reads as
        // fractions; the zero-total rule (a category whose visible total is <= 0) is the one from Stack mode.
        IntervalNormalizeChart.ColorField = "series";
        IntervalNormalizeChart.ConfigureMark(mark =>
        {
            var bars = (IntervalMark)mark;
            bars.Stack = StackMode.Normalize;   // default StackMode.None
            bars.BarPadding = 0.3f;
            bars.CornerRadius = 2f;
        });
        IntervalNormalizeChart.SetData(stackedRows);

        IntervalHorizontalChart.XField = "value";       // horizontal: X holds the number ...
        IntervalHorizontalChart.YField = "category";    // ... and Y the category the mark reads
        IntervalHorizontalChart.ConfigureMark(mark =>
        {
            var bars = (IntervalMark)mark;
            bars.Orientation = BarOrientation.Horizontal;   // default BarOrientation.Vertical
            bars.BarPadding = 0.3f;
            bars.CornerRadius = 4f;
        });
        IntervalHorizontalChart.SetData(
        [
            Row(("category", "Mon"), ("value", 24.0)),
            Row(("category", "Tue"), ("value", 38.0)),
            Row(("category", "Wed"), ("value", 31.0)),
            Row(("category", "Thu"), ("value", 47.0)),
            Row(("category", "Fri"), ("value", 29.0)),
        ]);
    }

    /// <summary><see cref="LineMark"/>: smoothing, stroke width, area fill, then the step mode (it wins over Smooth).</summary>
    private void ConfigureLine()
    {
        LineSmoothChart.ConfigureMark(mark =>
        {
            var line = (LineMark)mark;
            line.Smooth = true;         // default true: cubic segments, horizontal tangent per point
            line.StrokeWidth = 3.5f;    // default 2
            line.ShowArea = true;       // default false
            line.AreaOpacity = 0.35f;   // default 0.15
        });
        LineSmoothChart.SetValues(
            [("Mon", 18.0), ("Tue", 34.0), ("Wed", 26.0), ("Thu", 44.0), ("Fri", 38.0), ("Sat", 58.0)]);

        LineStepChart.ConfigureMark(mark =>
        {
            var line = (LineMark)mark;
            line.Step = StepMode.Center;    // default StepMode.None: step at the midpoint of each segment
            line.Smooth = false;            // kept off to show which of the two wins
            line.StrokeWidth = 2f;
            line.ShowArea = false;
        });
        LineStepChart.SetValues(
            [("Mon", 22.0), ("Tue", 22.0), ("Wed", 41.0), ("Thu", 41.0), ("Fri", 30.0), ("Sat", 30.0)]);

        // The one Line knob with its own layout: the bands pile up per category instead of overlaying each
        // other (RenderStacked / HitTestStacked - the hover snap follows the accumulated baseline), and the
        // fill is what makes the pile readable, so ShowArea is on here. The same data as the stacked bar
        // cell, so the two stacking modes can be compared side by side.
        LineStackChart.ColorField = "series";
        LineStackChart.ConfigureMark(mark =>
        {
            var line = (LineMark)mark;
            line.Stack = StackMode.Stack;   // default StackMode.None
            line.ShowArea = true;           // default false
            line.AreaOpacity = 0.4f;        // default 0.15
            line.StrokeWidth = 2f;
        });
        LineStackChart.SetData(
        [
            Row(("category", "Jan"), ("value", 32.0), ("series", "Product A")),
            Row(("category", "Jan"), ("value", 21.0), ("series", "Product B")),
            Row(("category", "Jan"), ("value", 14.0), ("series", "Product C")),
            Row(("category", "Feb"), ("value", 38.0), ("series", "Product A")),
            Row(("category", "Feb"), ("value", 26.0), ("series", "Product B")),
            Row(("category", "Feb"), ("value", 11.0), ("series", "Product C")),
            Row(("category", "Mar"), ("value", 30.0), ("series", "Product A")),
            Row(("category", "Mar"), ("value", 24.0), ("series", "Product B")),
            Row(("category", "Mar"), ("value", 19.0), ("series", "Product C")),
            Row(("category", "Apr"), ("value", 44.0), ("series", "Product A")),
            Row(("category", "Apr"), ("value", 18.0), ("series", "Product B")),
            Row(("category", "Apr"), ("value", 22.0), ("series", "Product C")),
        ]);
    }

    /// <summary><see cref="PointMark"/>: a fixed DefaultRadius, then MinRadius + RadiusRange from the size field.</summary>
    private void ConfigurePoint()
    {
        PointRadiusChart.XField = "t";
        PointRadiusChart.ConfigureMark(mark => ((PointMark)mark).DefaultRadius = 9f);   // default 5
        PointRadiusChart.SetData(
        [
            Row(("t", 1.0), ("value", 22.0)),
            Row(("t", 2.0), ("value", 41.0)),
            Row(("t", 3.0), ("value", 33.0)),
            Row(("t", 4.0), ("value", 55.0)),
            Row(("t", 5.0), ("value", 46.0)),
            Row(("t", 6.0), ("value", 28.0)),
        ]);

        // No SizeRange export here on purpose: the node then leaves the size scale to the inferred one,
        // which normalises the field to 0..1 exactly the way MinRadius + RadiusRange expect.
        PointSizeChart.XField = "t";
        PointSizeChart.SizeField = "weight";
        PointSizeChart.ConfigureMark(mark =>
        {
            var points = (PointMark)mark;
            points.MinRadius = 4f;      // default: theme PointSizeMin (3)
            points.RadiusRange = 26f;   // default: theme PointSizeRange (20) -> largest dot at 30 px
        });
        PointSizeChart.SetData(
        [
            Row(("t", 1.0), ("value", 22.0), ("weight", 0.15)),
            Row(("t", 2.0), ("value", 41.0), ("weight", 0.9)),
            Row(("t", 3.0), ("value", 33.0), ("weight", 0.45)),
            Row(("t", 4.0), ("value", 55.0), ("weight", 1.0)),
            Row(("t", 5.0), ("value", 46.0), ("weight", 0.7)),
            Row(("t", 6.0), ("value", 28.0), ("weight", 0.3)),
        ]);
    }

    /// <summary><see cref="RangeAreaMark"/>: the two bounds, the fill opacity, the edge lines and their smoothing.</summary>
    private void ConfigureRangeArea()
    {
        RangeAreaChart.YField = "upper";
        RangeAreaChart.ConfigureMark(mark =>
        {
            var band = (RangeAreaMark)mark;
            band.LowerField = "lower";      // default "lower"
            band.FillOpacity = 0.45f;       // default 0.3
            band.ShowBorderLines = true;    // default true: stroke both edges
            band.Smooth = true;             // default false
        });
        RangeAreaChart.SetData(
        [
            Row(("category", "Mon"), ("upper", 42.0), ("lower", 26.0)),
            Row(("category", "Tue"), ("upper", 51.0), ("lower", 30.0)),
            Row(("category", "Wed"), ("upper", 47.0), ("lower", 35.0)),
            Row(("category", "Thu"), ("upper", 63.0), ("lower", 38.0)),
            Row(("category", "Fri"), ("upper", 55.0), ("lower", 41.0)),
            Row(("category", "Sat"), ("upper", 68.0), ("lower", 44.0)),
        ]);
    }

    /// <summary><see cref="BoxMark"/>: renamed quartile fields, box/whisker geometry, corner radius, two colours.</summary>
    private void ConfigureBox()
    {
        BoxChart.XField = "team";
        BoxChart.ConfigureMark(mark =>
        {
            var box = (BoxMark)mark;
            box.MinField = "low";           // default "min"
            box.Q1Field = "p25";            // default "q1"
            box.MedianField = "p50";        // default "median"
            box.Q3Field = "p75";            // default "q3"
            box.MaxField = "high";          // default "max"
            box.BoxWidthRatio = 0.45f;      // default 0.5: of the category slot
            box.WhiskerWidth = 3f;          // default 1.5: median line and whiskers, in pixels
            box.CornerRadius = 6f;          // default 2
            box.BoxColor = new Color(0.36f, 0.68f, 0.98f, 0.75f);   // default: theme BoxFillColor
            box.LineColor = new Color(1f, 0.86f, 0.55f);            // default: theme BoxLineColor
        });
        BoxChart.SetData(
        [
            Row(("team", "Alpha"), ("low", 8.0), ("p25", 16.0), ("p50", 24.0), ("p75", 31.0), ("high", 44.0)),
            Row(("team", "Beta"), ("low", 18.0), ("p25", 24.0), ("p50", 27.0), ("p75", 29.0), ("high", 34.0)),
            Row(("team", "Gamma"), ("low", 5.0), ("p25", 14.0), ("p50", 22.0), ("p75", 29.0), ("high", 38.0)),
            Row(("team", "Delta"), ("low", 26.0), ("p25", 33.0), ("p50", 41.0), ("p75", 52.0), ("high", 61.0)),
            Row(("team", "Epsilon"), ("low", 12.0), ("p25", 19.0), ("p50", 20.0), ("p75", 21.0), ("high", 25.0)),
        ]);
    }

    /// <summary><see cref="CandlestickMark"/>: renamed OHLC fields, bull/bear colours, body/wick geometry, hollow candles.</summary>
    private void ConfigureCandlestick()
    {
        CandlestickChart.XField = "session";
        CandlestickChart.ConfigureMark(mark =>
        {
            var candles = (CandlestickMark)mark;
            candles.OpenField = "o";        // default "open"
            candles.HighField = "h";        // default "high"
            candles.LowField = "l";         // default "low"
            candles.CloseField = "c";       // default "close"
            candles.BullishColor = new Color(0.35f, 0.90f, 0.62f);  // default: theme CandlestickBullishColor
            candles.BearishColor = new Color(0.98f, 0.45f, 0.35f);  // default: theme CandlestickBearishColor
            candles.BodyWidthRatio = 0.5f;  // default 0.6: of the category slot
            candles.WickWidth = 2.5f;       // default 1.5
            candles.FillBullish = false;    // default true: false draws rising candles as outlines
        });
        CandlestickChart.SetData(
        [
            Row(("session", "S1"), ("o", 100.0), ("h", 104.0), ("l", 98.0), ("c", 103.0)),
            Row(("session", "S2"), ("o", 103.0), ("h", 105.0), ("l", 99.0), ("c", 100.0)),
            Row(("session", "S3"), ("o", 100.0), ("h", 102.0), ("l", 95.0), ("c", 96.0)),
            Row(("session", "S4"), ("o", 96.0), ("h", 101.0), ("l", 95.0), ("c", 100.0)),
            Row(("session", "S5"), ("o", 100.0), ("h", 107.0), ("l", 100.0), ("c", 106.0)),
            Row(("session", "S6"), ("o", 106.0), ("h", 108.0), ("l", 102.0), ("c", 103.0)),
            Row(("session", "S7"), ("o", 103.0), ("h", 104.0), ("l", 97.0), ("c", 98.0)),
            Row(("session", "S8"), ("o", 98.0), ("h", 105.0), ("l", 97.0), ("c", 104.0)),
        ]);
    }

    /// <summary><see cref="HeatmapMark"/>: cell gap and corner radius; the channels are column, row and value.</summary>
    private void ConfigureHeatmap()
    {
        HeatmapChart.XField = "weekday";     // column category
        HeatmapChart.YField = "slot";        // row category
        HeatmapChart.ColorField = "load";    // value behind the colour
        HeatmapChart.ConfigureMark(mark =>
        {
            var heat = (HeatmapMark)mark;
            heat.CellGap = 7f;          // default 1
            heat.CornerRadius = 6f;     // default 0: square cells
        });
        HeatmapChart.SetData(
        [
            Row(("weekday", "Mon"), ("slot", "AM"), ("load", 3.0)),
            Row(("weekday", "Mon"), ("slot", "PM"), ("load", 7.0)),
            Row(("weekday", "Tue"), ("slot", "AM"), ("load", 5.0)),
            Row(("weekday", "Tue"), ("slot", "PM"), ("load", 9.0)),
            Row(("weekday", "Wed"), ("slot", "AM"), ("load", 8.0)),
            Row(("weekday", "Wed"), ("slot", "PM"), ("load", 6.0)),
            Row(("weekday", "Thu"), ("slot", "AM"), ("load", 4.0)),
            Row(("weekday", "Thu"), ("slot", "PM"), ("load", 10.0)),
            Row(("weekday", "Thu"), ("slot", "Eve"), ("load", 5.0)),
        ]);
    }

    /// <summary><see cref="TimelineMark"/>: the range fields, the bar height in its lane and the corner radius.</summary>
    private void ConfigureTimeline()
    {
        TimelineChart.XField = "lane";       // category: one lane per row of the Y axis
        TimelineChart.YField = "to";         // value axis: fed the bars' end values
        TimelineChart.ColorField = "lane";   // one palette colour per lane
        TimelineChart.ConfigureMark(mark =>
        {
            var timeline = (TimelineMark)mark;
            timeline.StartField = "from";       // default "start": left edge of the bar
            timeline.EndField = "to";           // default "end": right edge of the bar
            timeline.BarHeightRatio = 0.45f;    // default 0.6: of the lane
            timeline.CornerRadius = 8f;         // default 3
        });
        TimelineChart.SetData(
        [
            Row(("lane", "Shield"), ("from", 0.0), ("to", 5.0)),
            Row(("lane", "Haste"), ("from", 2.0), ("to", 8.0)),
            Row(("lane", "Poison"), ("from", 3.0), ("to", 7.0)),
            Row(("lane", "Regen"), ("from", 6.0), ("to", 12.0)),
            Row(("lane", "Stun"), ("from", 9.0), ("to", 11.0)),
        ]);
    }

    /// <summary><see cref="MilestoneMark"/>: label field, marker geometry and symbol, lane line, alternating labels.</summary>
    private void ConfigureMilestone()
    {
        MilestoneChart.XField = "week";      // the moment on the X axis (numbers -> numeric scale)
        MilestoneChart.YField = "lane";      // lane category on the Y axis
        MilestoneChart.ColorField = "lane";
        MilestoneChart.ConfigureMark(mark =>
        {
            var milestones = (MilestoneMark)mark;
            milestones.LabelField = "event";                // default "label"
            milestones.MarkerRadius = 9f;                   // default 6
            milestones.MarkerShape = ShapeKind.Diamond;     // default ShapeKind.Circle
            milestones.ShowAxisLine = true;                 // default true: one line per lane
            milestones.AlternateLabels = true;              // default true: every second label below
        });
        MilestoneChart.SetData(
        [
            Row(("week", 2.0), ("lane", "Release"), ("event", "v0.9")),
            Row(("week", 7.0), ("lane", "Release"), ("event", "v1.0")),
            Row(("week", 3.0), ("lane", "Docs"), ("event", "guide")),
            Row(("week", 9.0), ("lane", "Docs"), ("event", "api ref")),
            Row(("week", 5.0), ("lane", "Infra"), ("event", "CI")),
            Row(("week", 11.0), ("lane", "Infra"), ("event", "CD")),
        ]);
    }

    /// <summary><see cref="LollipopMark"/>: dot radius and stem width, then the horizontal orientation.</summary>
    private void ConfigureLollipop()
    {
        LollipopChart.ConfigureMark(mark =>
        {
            var lollipop = (LollipopMark)mark;
            lollipop.DotRadius = 9f;        // default 5
            lollipop.StemWidth = 4f;        // default 2
            lollipop.Orientation = BarOrientation.Vertical;     // the default
        });
        LollipopChart.SetValues(
            [("Mon", 24.0), ("Tue", 38.0), ("Wed", 31.0), ("Thu", 47.0), ("Fri", 29.0), ("Sat", 52.0)]);

        LollipopHorizontalChart.XField = "value";
        LollipopHorizontalChart.YField = "category";
        LollipopHorizontalChart.ConfigureMark(mark =>
        {
            var lollipop = (LollipopMark)mark;
            lollipop.Orientation = BarOrientation.Horizontal;
            lollipop.DotRadius = 7f;
            lollipop.StemWidth = 3f;
        });
        LollipopHorizontalChart.SetData(
        [
            Row(("category", "Mon"), ("value", 24.0)),
            Row(("category", "Tue"), ("value", 38.0)),
            Row(("category", "Wed"), ("value", 31.0)),
            Row(("category", "Thu"), ("value", 47.0)),
            Row(("category", "Fri"), ("value", 29.0)),
        ]);
    }

    /// <summary><see cref="ViolinMark"/>: density resolution, body width and opacity, statistical indicators.</summary>
    private void ConfigureViolin()
    {
        ViolinChart.ConfigureMark(mark =>
        {
            var violin = (ViolinMark)mark;
            violin.BinCount = 48;       // default 20: density samples, not histogram bins
            violin.WidthRatio = 0.85f;  // default 0.7: half of the category slot
            violin.FillOpacity = 0.4f;  // default 0.5
            violin.ShowMedian = true;   // default true: the median tick
            violin.ShowBox = false;     // default true: the Q1/Q3 box
        });

        var rows = new List<DataRow>();
        AddGroup(rows, "Alpha", 8.0, 11.0, 13.0, 14.0, 15.0, 17.0, 22.0, 25.0);
        AddGroup(rows, "Beta", 14.0, 15.0, 16.0, 17.0, 18.0, 19.0, 20.0, 21.0);
        AddGroup(rows, "Gamma", 5.0, 9.0, 12.0, 18.0, 24.0, 29.0, 31.0, 36.0);
        ViolinChart.SetData(rows);
    }

    /// <summary><see cref="WaffleMark"/>: grid shape, cell gap and cell radius; the mark draws no axes.</summary>
    private void ConfigureWaffle()
    {
        WaffleChart.ConfigureMark(mark =>
        {
            var waffle = (WaffleMark)mark;
            waffle.TotalCells = 100;    // default 100: one cell per unit of the total
            waffle.Columns = 20;        // default 10: 5 rows of 20 instead of 10 rows of 10
            waffle.CellGap = 4f;        // default 2
            waffle.CellRadius = 6f;     // default 2
        });
        WaffleChart.SetValues([("Segment A", 42.0), ("Segment B", 28.0), ("Segment C", 18.0), ("Segment D", 12.0)]);
    }

    /// <summary>Rows for one category of a grouped mark: the category plus one row per value.</summary>
    private static void AddGroup(List<DataRow> rows, string category, params double[] values)
    {
        foreach (double value in values)
            rows.Add(Row(("category", category), ("value", value)));
    }

    /// <summary>Build a row from field/value pairs, so the data reads like the row it is.</summary>
    private static DataRow Row(params (string Field, object Value)[] fields)
    {
        var row = new DataRow(fields.Length);
        foreach (var (field, value) in fields) row.Set(field, value);
        return row;
    }
}
