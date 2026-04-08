using System;
using System.Collections.Generic;
using Godot;
using GodotNodeExtension.Component.GodotChart.Canvas;

namespace GodotNodeExtension.Component.GodotChart.Marks;

/// <summary>
/// Sankey diagram mark. Renders node-link flow visualization.
/// Each data row represents a flow: SourceField → TargetField with a value weight.
/// Nodes are automatically positioned in columns by topological order.
/// Classified as polar-only (no Cartesian grid).
/// </summary>
public class SankeyMark : Mark
{
    /// <inheritdoc />
    public override MarkCoordinate Coordinate => MarkCoordinate.Flow;

    /// <summary>Field name for the source node label.</summary>
    public string SourceField { get; set; } = "source";

    /// <summary>Field name for the target node label.</summary>
    public string TargetField { get; set; } = "target";

    /// <summary>Horizontal gap between node columns as ratio [0, 1].</summary>
    public float ColumnGap { get; set; } = 0.3f;

    /// <summary>Vertical gap between nodes in pixels.</summary>
    public float NodeGap { get; set; } = 8f;

    /// <summary>Width of each node bar in pixels.</summary>
    public float NodeWidth { get; set; } = 16f;

    /// <summary>Flow path opacity.</summary>
    public float FlowOpacity { get; set; } = 0.35f;

    /// <summary>Whether to show node labels.</summary>
    public override bool ShowLabel { get; set; } = true;

    // Cached layout to avoid recomputation in HitTest
    private SankeyLayout? _cachedLayout;
    private int _cachedDataVersion = -1;

    /// <summary>Invalidate cached layout. Call when data or encodes change externally.</summary>
    public void InvalidateCache() { _cachedLayout = null; _cachedDataVersion = -1; }

    public override void Render(MarkContext ctx)
    {
        if (ctx.Data.Count == 0) return;
        float anim = ComputeAnimProgress(ctx);

        _cachedDataVersion = ctx.LayoutVersion;
        _cachedLayout = BuildSankeyLayout(ctx);
        var layout = _cachedLayout;
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

            using var flowPath = ctx.Canvas.CreatePath();
            using var flowPaint = ctx.Canvas.CreatePaint();

            // Upper edge
            flowPath.MoveTo(sx, flow.SourceY);
            flowPath.CubicTo(midX, flow.SourceY, midX, flow.TargetY, tx, flow.TargetY);
            // Lower edge
            flowPath.LineTo(tx, flow.TargetY + tgtW);
            flowPath.CubicTo(midX, flow.TargetY + tgtW, midX, flow.SourceY + srcW, sx, flow.SourceY + srcW);
            flowPath.Close();

            var flowRow = ctx.Data[flow.RowIndex];
            var color = ResolveColorWithOverride(ctx, flowRow, flow.RowIndex, GetDefaultColor(ctx));
            bool isHovered = flow.RowIndex == ctx.HoveredRowIndex;
            if (isHovered) color = BrightenColor(color, GetHoverBrighten(ctx));
            float opacity = ComputeEffectiveOpacity(ctx, flowRow);

            flowPaint.SetColor(color).SetAntiAlias(true).SetOpacity(FlowOpacity * opacity);
            ctx.Canvas.Fill(flowPath, flowPaint);
        }

        // Draw nodes
        foreach (var (_, node) in layout.Nodes)
        {
            float nodeH = node.Height * anim;
            if (nodeH <= 0) continue;

            using var nodePath = ctx.Canvas.CreatePath();
            using var nodePaint = ctx.Canvas.CreatePaint();
            var snc = ctx.Theme?.SankeyNodeColor ?? new Color(0.85f, 0.85f, 0.9f);
            nodePath.RoundRect(node.X, node.Y, NodeWidth, nodeH, ctx.Theme?.SankeyNodeCornerRadius ?? 2f);
            nodePaint.SetColor(snc with { A = globalOpacity }).SetAntiAlias(true);
            ctx.Canvas.Fill(nodePath, nodePaint);

            if (ShowLabel)
            {
                using var labelPaint = ctx.Canvas.CreatePaint();
                labelPaint.SetColor(GetDataLabelColor(ctx)).SetOpacity(globalOpacity);
                float lx = node.Column == layout.MaxColumn
                    ? node.X - (ctx.Theme?.SankeyLabelGap ?? 4f) : node.X + NodeWidth + (ctx.Theme?.SankeyLabelGap ?? 4f);
                ctx.Canvas.DrawText(node.Label, lx, node.Y + nodeH / 2f, FontSettings.Default, labelPaint);
            }
        }
    }

    public override HitResult? HitTest(MarkContext ctx, Vector2 pos)
    {
        if (ctx.Data.Count == 0) return null;

        if (_cachedDataVersion != ctx.LayoutVersion)
            _cachedLayout = null;
        var layout = _cachedLayout ?? BuildSankeyLayout(ctx);
        if (layout == null) return null;

        // Check flows
        foreach (var flow in layout.Flows)
        {
            var srcNode = layout.Nodes[flow.Source];
            var tgtNode = layout.Nodes[flow.Target];

            float sx = srcNode.X + NodeWidth;
            float tx = tgtNode.X;

            // Simple bounding box check
            float left = Math.Min(sx, tx);
            float right = Math.Max(sx, tx);
            float top = Math.Min(flow.SourceY, flow.TargetY);
            float bottom = Math.Max(flow.SourceY + flow.SourceWidth, flow.TargetY + flow.TargetWidth);

            if (pos.X >= left && pos.X <= right && pos.Y >= top && pos.Y <= bottom)
            {
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

        }

        // Check nodes
        foreach (var (name, node) in layout.Nodes)
        {
            if (pos.X >= node.X && pos.X <= node.X + NodeWidth &&
                pos.Y >= node.Y && pos.Y <= node.Y + node.Height)
            {
                return new HitResult
                {
                    Hit = true, Row = ctx.Data[0], RowIndex = -1,
                    ScreenX = node.X + NodeWidth / 2f,
                    ScreenY = node.Y + node.Height / 2f,
                    Label = name,
                    MarkType = nameof(SankeyMark),
                };
            }
        }
        return null;
    }

    private SankeyLayout? BuildSankeyLayout(MarkContext ctx)
    {
        // Collect unique nodes and assign columns
        var nodeNames = new HashSet<string>();
        var sources = new HashSet<string>();
        var targets = new HashSet<string>();
        var flows = new List<(string src, string tgt, double val, int rowIdx)>();

        for (int i = 0; i < ctx.Data.Count; i++)
        {
            var row = ctx.Data[i];
            if (IsSeriesHidden(ctx, row)) continue;
            if (!row.Has(SourceField) || !row.Has(TargetField)) continue;
            string src = row.Get<object>(SourceField).ToString() ?? "";
            string tgt = row.Get<object>(TargetField).ToString() ?? "";
            var yRaw = ctx.Encodes.Resolve(YChannel, row);
            double val = yRaw != null ? ToDouble(yRaw, "Y") : 1;
            if (val <= 0 || src == tgt || string.IsNullOrEmpty(src) || string.IsNullOrEmpty(tgt))
                continue; // skip non-positive, self-loops, and empty node names

            nodeNames.Add(src);
            nodeNames.Add(tgt);
            sources.Add(src);
            targets.Add(tgt);
            flows.Add((src, tgt, val, i));
        }
        if (flows.Count == 0) return null;

        // Assign columns via topological sort (Kahn's algorithm) — handles cycles gracefully
        var columns = new Dictionary<string, int>();
        var pureSources = new List<string>();
        foreach (var s in sources)
        {
            if (!targets.Contains(s)) pureSources.Add(s);
        }
        if (pureSources.Count == 0 && sources.Count > 0)
        {
            // Take any source as a fallback starting point
            foreach (var s in sources) { pureSources.Add(s); break; }
        }

        // Build adjacency list and in-degree map for Kahn's algorithm
        var adjacency = new Dictionary<string, List<(string tgt, double val, int rowIdx)>>();
        var inDegree = new Dictionary<string, int>();
        foreach (var name in nodeNames) inDegree[name] = 0;
        foreach (var (src, tgt, val, rowIdx) in flows)
        {
            if (!adjacency.TryGetValue(src, out var list))
                adjacency[src] = list = new();
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
                if (!columns.ContainsKey(tgt) || columns[tgt] < nextCol)
                    columns[tgt] = nextCol;

                inDegree[tgt]--;
                if (inDegree[tgt] == 0)
                    queue.Enqueue(tgt);
            }
        }

        // Nodes still unvisited belong to a cycle — warn and assign column 0
        if (columns.Count < nodeNames.Count)
            GD.PushWarning("SankeyMark: cycle detected in data — affected nodes assigned to column 0.");

        // Assign column 0 to any unvisited nodes
        foreach (var name in nodeNames)
            columns.TryAdd(name, 0);

        int maxCol = 0;
        foreach (var col in columns.Values) { if (col > maxCol) maxCol = col; }
        float usableW = ctx.Plot.Width - NodeWidth;
        float colStep = maxCol > 0 ? usableW / maxCol : 0;

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

        // Position nodes per column — group by column, sort by value descending
        var colGroups = new SortedDictionary<int, List<string>>();
        foreach (var name in nodeNames)
        {
            int col = columns[name];
            if (!colGroups.TryGetValue(col, out var list))
                colGroups[col] = list = new List<string>();
            list.Add(name);
        }
        foreach (var group in colGroups.Values)
        {
            group.Sort((a, b) => nodeValues[b].CompareTo(nodeValues[a]));
            double colTotal = 0;
            foreach (var n in group) colTotal += nodeValues[n];
            float availH = totalNodeH - NodeGap * (group.Count - 1);
            float yOff = ctx.Plot.Y;

            foreach (var name in group)
            {
                float h = (float)(nodeValues[name] / colTotal * availH);
                nodes[name] = new SankeyNode
                {
                    Label = name, X = ctx.Plot.X + columns[name] * colStep,
                    Y = yOff, Height = h, Column = columns[name],
                };
                yOff += h + NodeGap;
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

    private class SankeyNode
    {
        public string Label { get; init; } = "";
        public float X { get; init; }
        public float Y { get; init; }
        public float Height { get; init; }
        public int Column { get; init; }
    }

    private class SankeyFlow
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

    private class SankeyLayout
    {
        public Dictionary<string, SankeyNode> Nodes { get; init; } = new();
        public List<SankeyFlow> Flows { get; init; } = new();
        public int MaxColumn { get; init; }
    }
}
