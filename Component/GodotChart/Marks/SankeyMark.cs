using System;
using System.Collections.Generic;
using Godot;
using GodotNodeExtension.Component.GodotChart.Canvas;

namespace GodotNodeExtension.Component.GodotChart.Marks;

/// <summary>
/// Sankey diagram mark. Renders node-link flow visualization.
/// Each data row represents a flow: SourceField → TargetField with a value weight.
/// Nodes are automatically positioned in columns by topological order; when the relations form a
/// cycle, the edge that closes it is ignored so the affected nodes still get their own column (see
/// <c>AssignCycleColumns</c>) and a warning is reported.
/// Classified as polar-only (no Cartesian grid).
/// <para>
/// Node labels are formatted with <see cref="Mark.LabelFormat"/> ({0} = node name, {1} = the node's
/// total flow). The label sits beside its node, so <see cref="Mark.LabelPosition"/> does not apply to
/// this mark.
/// </para>
/// </summary>
public class SankeyMark : Mark
{
    /// <inheritdoc />
    public override MarkCoordinate Coordinate => MarkCoordinate.Flow;

    /// <summary>Field name for the source node label.</summary>
    public string SourceField { get; set; } = "source";

    /// <summary>
    /// Upper bound on the links this mark draws: a sankey draws one element per row, so a table of a hundred
    /// thousand rows is a hundred thousand shapes. Rows past the cap are not drawn and the mark warns once -
    /// aggregate the data (or raise the cap) instead of watching the page drop to single-digit fps. The cap
    /// covers hit testing too: a row that is never painted has nowhere to be hovered, so it is not hittable
    /// either.
    /// </summary>
    public int MaxNodes { get; set; } = 65_536;

    /// <summary>Field name for the target node label.</summary>
    public string TargetField { get; set; } = "target";

    /// <summary>
    /// How tightly the node columns are packed, as a ratio [0, 1]. 0 keeps the default spread
    /// across the whole plot width (columns evenly spaced, the last one flush with the right edge);
    /// 1 packs the columns edge to edge against that right-aligned last column. The last column is
    /// anchored to the plot's right edge for every value.
    /// </summary>
    public float ColumnGap { get; set; } = 0.3f;

    /// <summary>Vertical gap between nodes in pixels.</summary>
    public float NodeGap { get; set; } = 8f;

    /// <summary>Width of each node bar in pixels.</summary>
    public float NodeWidth { get; set; } = 16f;

    /// <summary>Flow path opacity.</summary>
    public float FlowOpacity { get; set; } = 0.35f;

    /// <summary>Whether to show node labels.</summary>
    public override bool ShowLabel { get; set; } = true;

    /// <summary>
    /// This mark keeps its interaction state in the data layer, so a chart carrying it renders single-pass
    /// (see <see cref="Chart.UseLayerCache"/>).
    /// <para>
    /// The hover look of a flow is the ribbon's own colour, and a ribbon is drawn at
    /// <see cref="FlowOpacity"/> (0.35 by default): repainting it on the overlay would blend it twice
    /// (0.35 over 0.35) and darken the very flow the pointer is on. Its geometry also lives in the cached
    /// layout's flow list, which is keyed to the data layer.
    /// </para>
    /// </summary>
    public override bool InteractionStateInOverlay => false;

    // Cached layout to avoid recomputation in HitTest
    private SankeyLayout? _cachedLayout;
    private LayoutCacheKey? _cacheKey;
    // <see cref="LayoutConfigHash"/> of the frame the cached layout was built for. Null while nothing is cached.
    private int? _layoutConfigHash;

    /// <summary>Whether the "rows past <see cref="MaxNodes"/>" warning was already reported.</summary>
    private bool _warnedCap;

    /// <summary>Number of times the layout was rebuilt — used by tests to verify caching.</summary>
    internal int LayoutBuildCount { get; private set; }

    /// <summary>
    /// Force the cached layout to be rebuilt.
    /// The cache key already covers the chart, the layout version, the data list identity and the
    /// mark's own layout configuration, so this is only needed when the contents of the very same
    /// data list are mutated in place.
    /// </summary>
    public void InvalidateCache()
    {
        _cachedLayout = null;
        _cacheKey = null;
        _layoutConfigHash = null;
    }

    /// <summary>
    /// Hash of the mark properties that shape the layout but are invisible to <see cref="Mark.CacheKey"/>:
    /// they are plain auto-properties without a setter hook, so the cache has to notice a changed
    /// configuration by comparing this value instead of trusting the layout version.
    /// </summary>
    private int LayoutConfigHash()
        => HashCode.Combine(NodeWidth, NodeGap, ColumnGap, SourceField, TargetField, YChannel);

    /// <summary>
    /// Layout for the current context, built once per cache key and layout configuration instead of
    /// once per frame.
    /// </summary>
    private SankeyLayout? CachedLayout(MarkContext ctx)
    {
        var key = CacheKey(ctx);
        int configHash = LayoutConfigHash();
        if (_cachedLayout != null && _cacheKey == key && _layoutConfigHash == configHash)
            return _cachedLayout;

        _cachedLayout = BuildSankeyLayout(ctx);
        _cacheKey = key;
        _layoutConfigHash = configHash;
        LayoutBuildCount++;
        return _cachedLayout;
    }

    /// <inheritdoc />
    public override void Render(MarkContext ctx)
    {
        if (ctx.Data.Count == 0) return;
        float anim = ComputeAnimProgress(ctx);

        var layout = CachedLayout(ctx);
        if (layout == null) return;

        float globalOpacity = ctx.Animation.GlobalOpacity;

        // Draw flows (bezier curves)
        foreach (var flow in layout.Flows)
        {
            var srcNode = layout.Nodes[flow.Source];
            var tgtNode = layout.Nodes[flow.Target];

            float sx = srcNode.X + NodeWidth;
            float tx = tgtNode.X;
            float midX = (sx + tx) / 2f;

            float srcW = flow.SourceWidth * anim;
            float tgtW = flow.TargetWidth * anim;
            if (srcW <= 0 && tgtW <= 0) continue;

            var flowPath = ShapePath(ctx);
            var flowPaint = ShapePaint(ctx);

            // Upper edge
            flowPath.MoveTo(sx, flow.SourceY);
            flowPath.CubicTo(midX, flow.SourceY, midX, flow.TargetY, tx, flow.TargetY);
            // Lower edge
            flowPath.LineTo(tx, flow.TargetY + tgtW);
            flowPath.CubicTo(midX, flow.TargetY + tgtW, midX, flow.SourceY + srcW, sx, flow.SourceY + srcW);
            flowPath.Close();

            var flowRow = ctx.Data[flow.RowIndex];
            // A flow reads as "something leaving the source node", so it carries that node's colour.
            var color = ResolveFill(ctx, flowRow, flow.RowIndex, srcNode.Color);
            float opacity = ComputeElementOpacity(ctx, flowRow, flow.RowIndex);

            flowPaint.SetColor(color).SetAntiAlias(true).SetOpacity(FlowOpacity * opacity);
            ctx.Canvas.Fill(flowPath, flowPaint);

            // Selected: outline the flow of the selected row (States.SelectedStroke / width).
            if (flow.RowIndex == ctx.SelectedRowIndex)
            {
                var selPaint = ShapePaint(ctx);
                ApplySelectionPaint(ctx, selPaint, opacity);
                ctx.Canvas.Stroke(flowPath, selPaint);
            }
        }

        // Draw nodes
        foreach (var (_, node) in layout.Nodes)
        {
            float nodeH = node.Height * anim;
            if (nodeH <= 0) continue;

            var nodePath = ShapePath(ctx);
            var nodePaint = ShapePaint(ctx);
            var snc = (ctx.Theme ?? ChartTheme.Default).SankeyNodeColor;
            nodePath.RoundRect(node.X, node.Y, NodeWidth, nodeH, (ctx.Theme ?? ChartTheme.Default).SankeyNodeCornerRadius);
            nodePaint.SetColor(snc with { A = globalOpacity }).SetAntiAlias(true);
            ctx.Canvas.Fill(nodePath, nodePaint);

            if (ShowLabel)
            {
                var labelPaint = ShapePaint(ctx);
                labelPaint.SetColor(GetDataLabelColor(ctx)).SetOpacity(globalOpacity);
                bool lastColumn = node.Column == layout.MaxColumn;
                float labelGap = (ctx.Theme ?? ChartTheme.Default).SankeyLabelGap;
                float lx = lastColumn
                    ? node.X - labelGap : node.X + NodeWidth + labelGap;
                // Labels on the last column hang to the left of the node and used to be drawn
                // right-aligned: DrawTextCentered anchors the text on its centre, so the anchor is
                // moved left by half the measured width to keep that placement.
                string text = FormatLabel(LabelFormat, node.Label, node.Value);
                if (lastColumn)
                    lx -= ctx.Canvas.MeasureText(text, ThemedFont(ctx, FontSettings.Default)).Width / 2f;
                // {0} = node name, {1} = the node's total flow, so "{0} ({1})" and friends work.
                DrawTextCentered(ctx, labelPaint, text, lx, node.Y + nodeH / 2f, FontSettings.Default);
            }
        }
    }

    /// <inheritdoc />
    public override HitResult? HitTest(MarkContext ctx, Vector2 screenPos)
    {
        if (ctx.Data.Count == 0) return null;
        // The mark draws inside the plot rectangle: a point outside it cannot hit a flow or a node.
        if (!ctx.Plot.Contains(screenPos.X, screenPos.Y)) return null;

        var layout = CachedLayout(ctx);
        if (layout == null) return null;

        // Same entry progress as Render: the ribbons and node bars grow to their full thickness with
        // it, so a point in the part that is not drawn yet must not hit.
        float anim = ComputeAnimProgress(ctx);
        if (anim <= 0f) return null;

        // Check flows
        foreach (var flow in layout.Flows)
        {
            var srcNode = layout.Nodes[flow.Source];
            var tgtNode = layout.Nodes[flow.Target];

            float sx = srcNode.X + NodeWidth;
            float tx = tgtNode.X;
            float srcW = flow.SourceWidth * anim;
            float tgtW = flow.TargetWidth * anim;

            // The ribbon is the area between the two cubic edges Render draws; sampling the curve at
            // the pointer's x gives the same band the flow path covers there, instead of a bounding
            // box that claimed the whole empty area between two columns.
            if (!RibbonContains(sx, flow.SourceY, srcW, tx, flow.TargetY, tgtW, screenPos.X, screenPos.Y))
                continue;

            // The cached layout can be built from mark-local data while HitTest runs against the
            // chart's data (see ChartInteraction.TestAll); stay in bounds instead of throwing.
            if (flow.RowIndex < 0 || flow.RowIndex >= ctx.Data.Count) continue;
            var row = ctx.Data[flow.RowIndex];
            return new HitResult
            {
                Hit = true, Row = row, RowIndex = flow.RowIndex,
                ScreenX = (sx + tx) / 2f,
                ScreenY = (flow.SourceY + flow.TargetY) / 2f,
                Label = $"{flow.Source} → {flow.Target}: {flow.Value:G4}",
                MarkType = nameof(SankeyMark),
            };
        }

        // Check nodes
        foreach (var (name, node) in layout.Nodes)
        {
            float nodeH = node.Height * anim;
            if (screenPos.X >= node.X && screenPos.X <= node.X + NodeWidth &&
                screenPos.Y >= node.Y && screenPos.Y <= node.Y + nodeH)
            {
                return new HitResult
                {
                    // A node is not backed by a data row: the index says so (-1), and the row must not claim one
                    // either - a consumer reading Row (ChartHoverEventArgs.Row, a tooltip) used to get the first
                    // row of the table, which has nothing to do with the node under the pointer.
                    Hit = true, Row = null, RowIndex = -1,
                    ScreenX = node.X + NodeWidth / 2f,
                    ScreenY = node.Y + nodeH / 2f,
                    Label = name,
                    MarkType = nameof(SankeyMark),
                };
            }
        }
        return null;
    }

    /// <summary>
    /// Steps used to walk the two cubic ribbon edges when a hit test looks for the band at a given x.
    /// The curve is monotone in x, so a fine fixed sampling resolves the band to well under a pixel.
    /// </summary>
    private const int RibbonHitSamples = 24;

    /// <summary>
    /// Whether the point <paramref name="py"/> at <paramref name="px"/> lies inside the flow ribbon
    /// drawn from <c>(x0, y0)</c> to <c>(x1, y1)</c> with the given thicknesses at its two ends.
    /// Mirrors the two cubic edges of the path Render fills (same control points: the x midpoint),
    /// so the hit area is the ribbon itself and not its bounding box.
    /// </summary>
    private static bool RibbonContains(float x0, float y0, float w0, float x1, float y1, float w1,
                                       float px, float py)
    {
        if (px < MathF.Min(x0, x1) || px > MathF.Max(x0, x1)) return false;
        if (w0 <= 0f && w1 <= 0f) return false;

        float midX = (x0 + x1) / 2f;
        float prevX = x0, prevTop = y0, prevBottom = y0 + w0;
        for (int i = 1; i <= RibbonHitSamples; i++)
        {
            float t = (float)i / RibbonHitSamples;
            float x = Cubic(x0, midX, midX, x1, t);
            float top = Cubic(y0, y0, y1, y1, t);
            float bottom = Cubic(y0 + w0, y0 + w0, y1 + w1, y1 + w1, t);

            float lo = MathF.Min(prevX, x);
            float hi = MathF.Max(prevX, x);
            if (px >= lo && px <= hi)
            {
                float span = x - prevX;
                float f = MathF.Abs(span) > 1e-6f ? (px - prevX) / span : 0f;
                float topAt = prevTop + (top - prevTop) * f;
                float bottomAt = prevBottom + (bottom - prevBottom) * f;
                return py >= MathF.Min(topAt, bottomAt) && py <= MathF.Max(topAt, bottomAt);
            }

            prevX = x;
            prevTop = top;
            prevBottom = bottom;
        }
        return false;
    }

    /// <summary>Value of the cubic Bézier with the given control points at parameter <paramref name="t"/>.</summary>
    private static float Cubic(float p0, float p1, float p2, float p3, float t)
    {
        float u = 1f - t;
        return u * u * u * p0 + 3f * u * u * t * p1 + 3f * u * t * t * p2 + t * t * t * p3;
    }

    private SankeyLayout? BuildSankeyLayout(MarkContext ctx)
    {
        // Collect unique nodes and assign columns
        var nodeNames = new HashSet<string>();
        // First-appearance order of the nodes: the HashSet carries no order, and the palette colour of
        // a node has to be stable (and meaningful) across frames.
        var nodeOrder = new List<string>();
        var flows = new List<(string src, string tgt, double val, int rowIdx)>();

        int limit = Math.Min(ctx.Data.Count, MaxNodes);
        if (ctx.Data.Count > MaxNodes && !_warnedCap)
        {
            _warnedCap = true;
            GD.PushWarning(
                $"{nameof(SankeyMark)}: {ctx.Data.Count} rows exceed MaxNodes ({MaxNodes}); laying out the " +
                $"first {MaxNodes}, the rest are not drawn. Aggregate the data or raise MaxNodes.");
        }
        for (int i = 0; i < limit; i++)
        {
            var row = ctx.Data[i];
            if (IsSeriesHidden(ctx, row)) continue;
            if (!row.Has(SourceField) || !row.Has(TargetField)) continue;
            // See ChordMark: null values mean the relation is missing.
            string? src = GetStringOrNull(row, SourceField);
            string? tgt = GetStringOrNull(row, TargetField);
            if (src is null || tgt is null) continue;
            var yRaw = ctx.Encodes.Resolve(YChannel, row);
            double val = yRaw != null ? ToDouble(yRaw, "Y") : 1;
            // A non-finite value would propagate into every downstream total: skip the flow.
            if (!double.IsFinite(val) || val <= 0
                || src == tgt || string.IsNullOrEmpty(src) || string.IsNullOrEmpty(tgt))
                continue; // skip non-finite/non-positive values, self-loops, and empty node names

            if (nodeNames.Add(src)) nodeOrder.Add(src);
            if (nodeNames.Add(tgt)) nodeOrder.Add(tgt);
            flows.Add((src, tgt, val, i));
        }
        if (flows.Count == 0) return null;

        // Assign columns via topological sort (Kahn's algorithm). Nodes that a cycle keeps out of the
        // order are placed by AssignCycleColumns below.
        var columns = new Dictionary<string, int>();
        // (A "pure sources" list used to be computed here but was never read: the column assignment
        // below is driven purely by Kahn's algorithm.)

        // Build adjacency list and in-degree map for Kahn's algorithm
        var adjacency = new Dictionary<string, List<(string tgt, double val, int rowIdx)>>();
        var inDegree = new Dictionary<string, int>();
        foreach (var name in nodeNames) inDegree[name] = 0;
        foreach (var (src, tgt, val, rowIdx) in flows)
        {
            if (!adjacency.TryGetValue(src, out var list))
                adjacency[src] = list = [];
            list.Add((tgt, val, rowIdx));
            inDegree[tgt] = inDegree.GetValueOrDefault(tgt) + 1;
        }

        // Kahn's: start from nodes with in-degree 0
        var queue = new Queue<string>();
        foreach (var name in nodeNames)
        {
            if (inDegree[name] == 0)
            {
                columns[name] = 0;
                queue.Enqueue(name);
            }
        }

        while (queue.Count > 0)
        {
            string cur = queue.Dequeue();
            if (!adjacency.TryGetValue(cur, out var neighbors)) continue;
            foreach (var (tgt, _, _) in neighbors)
            {
                int nextCol = columns[cur] + 1;
                if (!columns.TryGetValue(tgt, out var targetColumn) || targetColumn < nextCol)
                    columns[tgt] = nextCol;

                inDegree[tgt]--;
                if (inDegree[tgt] == 0)
                    queue.Enqueue(tgt);
            }
        }

        // Nodes still unvisited belong to a cycle: Kahn's algorithm cannot order them, so the layout
        // steps through the cycle and drops the back edge that closes it. Every node of the cycle
        // then gets the column of its position in that walk - stacking them all on column 0 used to
        // draw every node of the cycle on top of each other at the left edge of the plot.
        if (columns.Count < nodeNames.Count)
        {
            GD.PushWarning(
                "SankeyMark: cycle detected in the flow data — the cycle's closing edge(s) were " +
                "ignored to assign the columns of the affected nodes.");
            AssignCycleColumns(adjacency, columns, nodeOrder);
        }

        int maxCol = 0;
        foreach (var col in columns.Values) { if (col > maxCol) maxCol = col; }
        // Horizontal layout: maxCol + 1 evenly spaced columns. ColumnGap [0, 1] shrinks the
        // inter-column pitch from the default full-width spread (0) toward the packed edge-to-edge
        // minimum NodeWidth (1). The last column always stays flush with the plot's right edge, so
        // the whole band slides left as the pitch shrinks.
        float usableW = ctx.Plot.Width - NodeWidth;
        float evenStep = maxCol > 0 ? usableW / maxCol : 0f;
        float colStep = evenStep + Math.Clamp(ColumnGap, 0f, 1f) * (NodeWidth - evenStep);

        // Calculate node heights based on total flow (single-pass)
        var outSums = new Dictionary<string, double>();
        var inSums  = new Dictionary<string, double>();
        foreach (var name in nodeNames) { outSums[name] = 0; inSums[name] = 0; }
        foreach (var (src, tgt, val, _) in flows)
        {
            outSums[src] += val;
            inSums[tgt]  += val;
        }
        var nodeValues = new Dictionary<string, double>();
        foreach (var name in nodeNames)
            nodeValues[name] = Math.Max(outSums[name], inSums[name]);

        double maxNodeVal = 0;
        foreach (var v in nodeValues.Values) { if (v > maxNodeVal) maxNodeVal = v; }
        if (maxNodeVal <= 0) return null;

        float totalNodeH = ctx.Plot.Height;
        var nodes = new Dictionary<string, SankeyNode>();

        // One palette colour per node, in discovery order: a flow reads as "something leaving its
        // source node", so the flow uses that node's colour.
        var palette = PaletteOf(ctx);
        var nodeColors = new Dictionary<string, Color>(nodeOrder.Count);
        for (int i = 0; i < nodeOrder.Count; i++)
            nodeColors[nodeOrder[i]] = palette[i % palette.Length];

        // Position nodes per column — group by column, sort by value descending
        var colGroups = new SortedDictionary<int, List<string>>();
        foreach (var name in nodeNames)
        {
            int col = columns[name];
            if (!colGroups.TryGetValue(col, out var list))
                colGroups[col] = list = [];
            list.Add(name);
        }
        foreach (var group in colGroups.Values)
        {
            group.Sort((a, b) => nodeValues[b].CompareTo(nodeValues[a]));
            double colTotal = 0;
            foreach (var n in group) colTotal += nodeValues[n];
            // The gaps alone can exceed the plot height (many nodes, or a large NodeGap): shrink them
            // so the nodes still fit, instead of pushing the last ones - and their labels - outside
            // the plot area. What is left over is what the node heights are scaled into.
            float gap = group.Count > 1
                ? MathF.Min(MathF.Max(0f, NodeGap), MathF.Max(0f, (totalNodeH - group.Count) / (group.Count - 1)))
                : 0f;
            float availH = MathF.Max(1f, totalNodeH - gap * (group.Count - 1));
            float yOff = ctx.Plot.Y;

            foreach (var name in group)
            {
                float h = (float)(nodeValues[name] / colTotal * availH);
                nodes[name] = new SankeyNode
                {
                    Label = name,
                    // The last column stays right-aligned; earlier columns step left from it.
                    X = maxCol > 0
                        ? ctx.Plot.X + usableW - (maxCol - columns[name]) * colStep
                        : ctx.Plot.X,
                    // Clamp into the plot: a node can only reach the bottom edge through rounding,
                    // but it must never leave the plot area its labels are placed against.
                    Y = MathF.Min(yOff, ctx.Plot.Y + totalNodeH - h), Height = h, Column = columns[name],
                    Color = nodeColors[name], Value = nodeValues[name],
                };
                yOff += h + gap;
            }
        }

        // Build flow rendering data
        var renderedFlows = new List<SankeyFlow>();
        var srcOffsets = new Dictionary<string, float>(nodeNames.Count);
        var tgtOffsets = new Dictionary<string, float>(nodeNames.Count);
        foreach (var n in nodeNames) { srcOffsets[n] = 0f; tgtOffsets[n] = 0f; }

        foreach (var (src, tgt, val, rowIdx) in flows)
        {
            if (!nodes.ContainsKey(src) || !nodes.ContainsKey(tgt)) continue;
            var srcN = nodes[src];
            var tgtN = nodes[tgt];

            float srcW = (float)(val / nodeValues[src] * srcN.Height);
            float tgtW = (float)(val / nodeValues[tgt] * tgtN.Height);
            float sY = srcN.Y + srcOffsets[src];
            float tY = tgtN.Y + tgtOffsets[tgt];

            renderedFlows.Add(new SankeyFlow
            {
                Source = src, Target = tgt, Value = val, RowIndex = rowIdx,
                SourceY = sY, TargetY = tY, SourceWidth = srcW, TargetWidth = tgtW,
            });

            srcOffsets[src] += srcW;
            tgtOffsets[tgt] += tgtW;
        }

        return new SankeyLayout { Nodes = nodes, Flows = renderedFlows, MaxColumn = maxCol };
    }

    /// <summary>
    /// Column of every node that <see cref="BuildSankeyLayout"/> could not order topologically (its
    /// edges form a cycle): the flow graph is walked depth-first from each unresolved node and a node
    /// gets one column more than the node it is reached from, skipping the edges that point back into
    /// the path being walked - those are the edges that close the cycle, and they cannot both be
    /// honoured. Nodes that already have a column (a predecessor outside the cycle) keep it.
    /// </summary>
    /// <param name="adjacency">Flow edges per source node.</param>
    /// <param name="columns">Columns to fill; entries for already ordered nodes are only ever lowered
    /// when they are part of a cycle, never removed.</param>
    /// <param name="nodeOrder">Nodes in first-appearance order, so the result is deterministic.</param>
    private static void AssignCycleColumns(
        Dictionary<string, List<(string tgt, double val, int rowIdx)>> adjacency,
        Dictionary<string, int> columns,
        List<string> nodeOrder)
    {
        // Explicit stack instead of recursion: a pathological input can only ever make this loop
        // longer, never overflow the call stack.
        var frames = new Stack<(string Node, int Column, int Next)>();
        var onPath = new HashSet<string>();

        foreach (var root in nodeOrder)
        {
            if (!columns.TryAdd(root, 0)) continue;
            onPath.Add(root);
            frames.Push((root, 0, 0));

            while (frames.Count > 0)
            {
                var (node, column, next) = frames.Pop();
                if (!adjacency.TryGetValue(node, out var neighbors) || next >= neighbors.Count)
                {
                    onPath.Remove(node);   // the walk out of this node is finished
                    continue;
                }

                frames.Push((node, column, next + 1));
                var (target, _, _) = neighbors[next];
                if (onPath.Contains(target)) continue;   // back edge closing the cycle: skipped
                if (columns.TryGetValue(target, out int existing) && existing >= column + 1) continue;

                columns[target] = column + 1;
                onPath.Add(target);
                frames.Push((target, column + 1, 0));
            }
        }

        // A cycle is never entered twice, but make the invariant explicit: no node may stay without
        // a column, or the caller's max column would ignore it.
        foreach (var name in nodeOrder)
            columns.TryAdd(name, 0);
    }

    private sealed class SankeyNode
    {
        public string Label { get; init; } = "";
        public float X { get; init; }
        public float Y { get; init; }
        public float Height { get; init; }
        public int Column { get; init; }

        /// <summary>Total flow through this node; the second placeholder of the node label.</summary>
        public double Value { get; init; }

        /// <summary>Palette colour of this node; the flows leaving it inherit it.</summary>
        public Color Color { get; init; }
    }

    private sealed class SankeyFlow
    {
        public string Source { get; init; } = "";
        public string Target { get; init; } = "";
        public double Value { get; init; }
        public int RowIndex { get; init; }
        public float SourceY { get; init; }
        public float TargetY { get; init; }
        public float SourceWidth { get; init; }
        public float TargetWidth { get; init; }
    }

    private sealed class SankeyLayout
    {
        public Dictionary<string, SankeyNode> Nodes { get; init; } = [];
        public List<SankeyFlow> Flows { get; init; } = [];
        public int MaxColumn { get; init; }
    }
}
