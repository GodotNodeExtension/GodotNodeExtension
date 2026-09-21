using Godot;
using GodotNodeExtension.Component.GodotChart;
using GodotNodeExtension.Component.GodotChart.Marks;

namespace GodotNodeExtension.Example.GodotChart;

/// <summary>
/// The <b>per-mark knobs</b> of the four hierarchy / flow marks, one chart each, set from code through
/// <see cref="ChartView.ConfigureMark"/> (the scene only holds the layout, the kind and the title). Every
/// cell lists the members it turns in its caption:
/// <list type="bullet">
/// <item>two <see cref="TreemapMark"/> charts over the same rows, one per
/// <see cref="TreemapLayoutMode"/> (<see cref="TreemapMark.LayoutMode"/>), plus
/// <see cref="TreemapMark.CellGap"/>, <see cref="TreemapMark.CornerRadius"/>,
/// <see cref="TreemapMark.GroupHeaderHeight"/> and <see cref="TreemapMark.SiblingShadeStep"/>;</item>
/// <item>a <see cref="SunburstMark"/> with <see cref="SunburstMark.RadiusFactor"/>,
/// <see cref="SunburstMark.InnerRadiusRatio"/>, <see cref="SunburstMark.RingGap"/>,
/// <see cref="SunburstMark.ArcGap"/> and <see cref="SunburstMark.DepthShadeStep"/>;</item>
/// <item>a <see cref="SankeyMark"/> with <see cref="SankeyMark.ColumnGap"/>, <see cref="SankeyMark.NodeGap"/>,
/// <see cref="SankeyMark.NodeWidth"/> and <see cref="SankeyMark.FlowOpacity"/>;</item>
/// <item>a <see cref="ChordMark"/> with <see cref="ChordMark.ArcWidthRatio"/>, <see cref="ChordMark.ArcGap"/>,
/// <see cref="ChordMark.ChordOpacity"/> and <see cref="ChordMark.RadiusFactor"/>.</item>
/// </list>
/// <para>
/// The two row shapes are the ones these marks expect. Treemap and sunburst read <c>label</c> / <c>parent</c>
/// / <c>value</c> rows, where a row whose <c>parent</c> is empty is a top level branch and a branch with
/// value 0 takes the sum of its children - the rows below are three levels deep (root, two branches,
/// leaves). Sankey and chord read <c>source</c> / <c>target</c> / <c>value</c> rows: <see cref="ChartView"/>
/// binds only the Y channel (the flow weight) for those two kinds, and the mark looks the two node fields
/// up <b>by name</b>, which is why <see cref="SankeyMark.SourceField"/> /
/// <see cref="SankeyMark.TargetField"/> (and <see cref="ChordMark.SourceField"/> /
/// <see cref="ChordMark.TargetField"/>) are knobs: the data here uses <c>from</c> / <c>to</c> and
/// <c>src</c> / <c>dst</c> to prove the names are yours to pick.
/// </para>
/// <para>
/// The scene captions are the short form - just the knobs and the values this demo sets. The why, and the
/// library defaults those values are read against, lives here. A <see cref="TreemapMark"/> defaults to
/// <see cref="TreemapLayoutMode.BinarySplit"/> (fast, but it does not optimise aspect ratios),
/// <see cref="TreemapMark.CellGap"/> 2, <see cref="TreemapMark.CornerRadius"/> 3,
/// <see cref="TreemapMark.GroupHeaderHeight"/> 16 and <see cref="TreemapMark.SiblingShadeStep"/> 0.12, and
/// <see cref="TreemapMark.ParentField"/> is the field that turns the flat rows into the tree.
/// <see cref="TreemapLayoutMode.Squarify"/> packs a row of items along the shorter side (which keeps the cells
/// closer to square) and is the layout <see cref="ChartKind.Treemap"/> picks on its own;
/// <see cref="TreemapMark.CornerRadius"/> 0 gives square cells, <see cref="TreemapMark.GroupHeaderHeight"/> 0
/// drops the group label strip (a group is then only visible through its children) and a larger
/// <see cref="TreemapMark.SiblingShadeStep"/> separates the siblings more strongly. A
/// <see cref="SunburstMark"/> defaults to <see cref="SunburstMark.RadiusFactor"/> 0.9 (a ratio of half the
/// plot's short side), <see cref="SunburstMark.InnerRadiusRatio"/> 0.15, <see cref="SunburstMark.RingGap"/> 2
/// (pixels), <see cref="SunburstMark.ArcGap"/> 0.02 (radians) and
/// <see cref="SunburstMark.DepthShadeStep"/> 0.18 - the step that keeps the depth readable by darkening every
/// ring below a branch while it keeps that branch's palette colour; a single root row is treated as the frame,
/// so the <c>Root</c> ring stays neutral.
/// </para>
/// <para>
/// A <see cref="SankeyMark"/> defaults to <see cref="SankeyMark.SourceField"/> <c>source</c> /
/// <see cref="SankeyMark.TargetField"/> <c>target</c>, <see cref="SankeyMark.ColumnGap"/> 0.3,
/// <see cref="SankeyMark.NodeGap"/> 8 (the gap between the bars of one column),
/// <see cref="SankeyMark.NodeWidth"/> 16 and <see cref="SankeyMark.FlowOpacity"/> 0.35;
/// <see cref="SankeyMark.ColumnGap"/> is how tightly the columns are packed (0 spreads them, 1 packs them edge
/// to edge against the right-aligned last column), the mark derives the columns and each node's height from the
/// largest of its incoming / outgoing totals, and a ribbon leaves with its source node's colour. A
/// <see cref="ChordMark"/> defaults to <see cref="ChordMark.SourceField"/> <c>source</c> /
/// <see cref="ChordMark.TargetField"/> <c>target</c>, <see cref="ChordMark.ArcWidthRatio"/> 0.06 (the node arc
/// thickness as a ratio of the radius), <see cref="ChordMark.ArcGap"/> 0.04 (radians),
/// <see cref="ChordMark.ChordOpacity"/> 0.4 (the chord alpha) and <see cref="ChordMark.RadiusFactor"/> 0.85.
/// The chord rows run two of the handovers in both directions, which is what makes those chords bow both ways.
/// </para>
/// </summary>
public partial class ChartMarksHierarchyDemo : Control
{
    /// <summary>Treemap chart laid out with <see cref="TreemapLayoutMode.BinarySplit"/>.</summary>
    [Export] public ChartView TreemapBinaryChart { get; set; } = null!;

    /// <summary>Treemap chart laid out with <see cref="TreemapLayoutMode.Squarify"/>, over the same rows.</summary>
    [Export] public ChartView TreemapSquarifyChart { get; set; } = null!;

    /// <summary>Sunburst chart of the same three level tree.</summary>
    [Export] public ChartView SunburstChart { get; set; } = null!;

    /// <summary>Flow chart fed by <c>from</c> / <c>to</c> / <c>value</c> rows.</summary>
    [Export] public ChartView SankeyChart { get; set; } = null!;

    /// <summary>Relationship chart fed by <c>src</c> / <c>dst</c> / <c>value</c> rows.</summary>
    [Export] public ChartView ChordChart { get; set; } = null!;

    /// <inheritdoc />
    public override void _Ready()
    {
        ConfigureTreemaps();
        ConfigureSunburst();
        ConfigureSankey();
        ConfigureChord();
    }

    // ── Treemap: LayoutMode and the cell look ───────────────────────────────

    /// <summary>
    /// The same tree laid out twice, so the one knob that changes everything is easy to see:
    /// <see cref="TreemapLayoutMode.BinarySplit"/> cuts the items in half by value and recurses (fast, aspect
    /// ratios not optimised), while <see cref="TreemapLayoutMode.Squarify"/> packs a row of items along the
    /// shorter side and only starts a new row when the worst aspect ratio would get worse (the layout
    /// <see cref="ChartKind.Treemap"/> uses on its own). <see cref="TreemapMark.ParentField"/> names the
    /// field that links a row to its group; <see cref="TreemapMark.CellGap"/> and
    /// <see cref="TreemapMark.CornerRadius"/> are the per-cell spacing and rounding,
    /// <see cref="TreemapMark.GroupHeaderHeight"/> is the strip a group reserves for its label (0 turns it
    /// off) and <see cref="TreemapMark.SiblingShadeStep"/> darkens the n-th child of a family.
    /// </summary>
    private void ConfigureTreemaps()
    {
        TreemapBinaryChart.XField = "label";
        TreemapBinaryChart.YField = "value";
        TreemapBinaryChart.ConfigureMark(mark =>
        {
            if (mark is not TreemapMark treemap) return;
            treemap.LayoutMode = TreemapLayoutMode.BinarySplit;
            treemap.CellGap = 4f;
            treemap.CornerRadius = 8f;
            treemap.ParentField = "parent";
            treemap.GroupHeaderHeight = 18f;
            treemap.SiblingShadeStep = 0.12f;
        });
        TreemapBinaryChart.SetData(HierarchyRows());

        TreemapSquarifyChart.XField = "label";
        TreemapSquarifyChart.YField = "value";
        TreemapSquarifyChart.ConfigureMark(mark =>
        {
            if (mark is not TreemapMark treemap) return;
            treemap.LayoutMode = TreemapLayoutMode.Squarify;
            treemap.CellGap = 1.5f;
            treemap.CornerRadius = 0f;          // square cells instead of the rounded ones on the left
            treemap.ParentField = "parent";
            treemap.GroupHeaderHeight = 0f;     // no label strip: a group is only seen through its children
            treemap.SiblingShadeStep = 0.3f;
        });
        TreemapSquarifyChart.SetData(HierarchyRows());
    }

    // ── Sunburst: rings ─────────────────────────────────────────────────────

    /// <summary>
    /// The same tree as rings. <see cref="SunburstMark.ParentField"/> is the hierarchy again,
    /// <see cref="SunburstMark.RadiusFactor"/> is the outer radius (a ratio of half the plot's short side),
    /// <see cref="SunburstMark.InnerRadiusRatio"/> the hole in the middle,
    /// <see cref="SunburstMark.RingGap"/> the space between rings in pixels and
    /// <see cref="SunburstMark.ArcGap"/> the space between arcs in radians;
    /// <see cref="SunburstMark.DepthShadeStep"/> is what keeps the depth readable - every ring below a
    /// branch keeps that branch's palette colour and darkens by this step. A single root row is treated as
    /// the frame, so the <c>Root</c> ring stays neutral and its children are the branches.
    /// </summary>
    private void ConfigureSunburst()
    {
        SunburstChart.XField = "label";
        SunburstChart.YField = "value";
        SunburstChart.ConfigureMark(mark =>
        {
            if (mark is not SunburstMark sunburst) return;
            sunburst.ParentField = "parent";
            sunburst.RadiusFactor = 0.9f;
            sunburst.InnerRadiusRatio = 0.2f;
            sunburst.RingGap = 3f;
            sunburst.ArcGap = 0.03f;
            sunburst.DepthShadeStep = 0.2f;
        });
        SunburstChart.SetData(HierarchyRows());
    }

    // ── Sankey: node columns and ribbons ───────────────────────────────────

    /// <summary>
    /// Flows between nodes: each row is one <c>from</c> → <c>to</c> hop with a weight, and the mark derives
    /// the columns (and each node's height from the largest of its incoming / outgoing totals) itself.
    /// <see cref="SankeyMark.SourceField"/> / <see cref="SankeyMark.TargetField"/> name those two fields,
    /// <see cref="SankeyMark.ColumnGap"/> is how tightly the columns are packed (0 = spread across the whole
    /// plot, 1 = edge to edge), <see cref="SankeyMark.NodeGap"/> is the gap between the bars of one column,
    /// <see cref="SankeyMark.NodeWidth"/> their width and <see cref="SankeyMark.FlowOpacity"/> the ribbon
    /// alpha. <see cref="ChartView"/> binds only Y = <c>value</c> for this kind, so the two node fields are
    /// read by the mark, not by a channel.
    /// </summary>
    private void ConfigureSankey()
    {
        SankeyChart.YField = "value";           // the only channel a flow kind binds; X stays unbound
        SankeyChart.ConfigureMark(mark =>
        {
            if (mark is not SankeyMark sankey) return;
            sankey.SourceField = "from";
            sankey.TargetField = "to";
            sankey.ColumnGap = 0.3f;
            sankey.NodeGap = 10f;
            sankey.NodeWidth = 18f;
            sankey.FlowOpacity = 0.3f;
        });
        SankeyChart.SetData(SankeyRows());
    }

    // ── Chord: one arc per node, one chord per relation ─────────────────────

    /// <summary>
    /// Relationships around a circle: the same source/target/value rows, so a relation appears in both
    /// directions here (<c>Design</c> → <c>PM</c> and <c>PM</c> → <c>Design</c>) - the mark gives every node
    /// an arc proportional to its total and draws one chord per row.
    /// <see cref="ChordMark.SourceField"/> / <see cref="ChordMark.TargetField"/> are the field names (this
    /// chart uses <c>src</c> / <c>dst</c> to show they are free), <see cref="ChordMark.ArcWidthRatio"/> is
    /// the arc thickness as a ratio of the radius, <see cref="ChordMark.ArcGap"/> the gap between arcs in
    /// radians, <see cref="ChordMark.ChordOpacity"/> the chord alpha and
    /// <see cref="ChordMark.RadiusFactor"/> the outer radius (a ratio of half the plot's short side).
    /// </summary>
    private void ConfigureChord()
    {
        ChordChart.YField = "value";
        ChordChart.ConfigureMark(mark =>
        {
            if (mark is not ChordMark chord) return;
            chord.SourceField = "src";
            chord.TargetField = "dst";
            chord.ArcWidthRatio = 0.08f;
            chord.ArcGap = 0.04f;
            chord.ChordOpacity = 0.45f;
            chord.RadiusFactor = 0.8f;
        });
        ChordChart.SetData(ChordRows());
    }

    // ── The two row shapes ─────────────────────────────────────────────────

    /// <summary>
    /// A three level <c>label</c> / <c>parent</c> / <c>value</c> tree: one root, its two branches and the
    /// leaves carrying the values. A branch row keeps value 0 on purpose - treemap and sunburst then take
    /// the sum of its children, so the branch is exactly as big as what it holds.
    /// </summary>
    private static DataRow[] HierarchyRows() =>
    [
        TreeRow("Root", "", 0),
        TreeRow("Design", "Root", 0),
        TreeRow("Shell", "Design", 38),
        TreeRow("Icons", "Design", 22),
        TreeRow("Engine", "Root", 0),
        TreeRow("Render", "Engine", 30),
        TreeRow("Physics", "Engine", 18),
        TreeRow("Audio", "Engine", 12),
    ];

    /// <summary>One <c>label</c> / <c>parent</c> / <c>value</c> row; an empty parent means top level.</summary>
    private static DataRow TreeRow(string label, string parent, double value)
        => new DataRow(3).Set("label", label).Set("parent", parent).Set("value", value);

    /// <summary>
    /// Flows of a three column pipeline (<c>Ingest</c> → <c>Model</c> / <c>Archive</c> → <c>Serving</c> /
    /// <c>Retrain</c>), as <c>from</c> / <c>to</c> / <c>value</c> rows.
    /// </summary>
    private static DataRow[] SankeyRows() =>
    [
        FlowRow("Ingest", "Model", 42),
        FlowRow("Ingest", "Archive", 18),
        FlowRow("Model", "Serving", 34),
        FlowRow("Model", "Retrain", 12),
        FlowRow("Archive", "Serving", 10),
        FlowRow("Archive", "Retrain", 8),
    ];

    /// <summary>
    /// Handovers between four teams as <c>src</c> / <c>dst</c> / <c>value</c> rows. Two rows run the same
    /// pair in opposite directions, which is what makes the chords bow both ways.
    /// </summary>
    private static DataRow[] ChordRows() =>
    [
        ArcRow("PM", "Design", 12),
        ArcRow("Design", "PM", 4),
        ArcRow("Design", "Backend", 9),
        ArcRow("Backend", "Design", 6),
        ArcRow("Backend", "QA", 7),
        ArcRow("QA", "PM", 5),
    ];

    /// <summary>One sankey row: <c>from</c> / <c>to</c> / <c>value</c>.</summary>
    private static DataRow FlowRow(string from, string to, double value)
        => new DataRow(3).Set("from", from).Set("to", to).Set("value", value);

    /// <summary>One chord row: <c>src</c> / <c>dst</c> / <c>value</c>.</summary>
    private static DataRow ArcRow(string src, string dst, double value)
        => new DataRow(3).Set("src", src).Set("dst", dst).Set("value", value);
}
