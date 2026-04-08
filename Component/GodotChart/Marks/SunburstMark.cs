using System;
using System.Collections.Generic;
using Godot;
using GodotNodeExtension.Component.GodotChart.Canvas;

namespace GodotNodeExtension.Component.GodotChart.Marks;

/// <summary>
/// Sunburst mark. Renders a multi-layer concentric donut chart for hierarchical data.
/// Each row has: X = label, Y = value, and an optional ParentField to define hierarchy.
/// Root-level items (no parent) form the inner ring; children form outer rings.
/// Classified as polar-only (no Cartesian grid).
/// Animation: arcs sweep from start angle.
/// </summary>
public class SunburstMark : Mark
{
    /// <inheritdoc />
    public override MarkCoordinate Coordinate => MarkCoordinate.Hierarchical;

    /// <summary>Field name for the parent label (null or empty = root level).</summary>
    public string ParentField { get; set; } = "parent";

    /// <summary>Outer radius as ratio of min(width, height)/2. Range (0, 1].</summary>
    public float RadiusFactor { get; set; } = 0.9f;

    /// <summary>Inner radius ratio relative to min(width, height) / 2.</summary>
    public float InnerRadiusRatio { get; set; } = 0.15f;

    /// <summary>Gap between rings in pixels.</summary>
    public float RingGap { get; set; } = 2f;

    /// <summary>Gap between arc segments in radians.</summary>
    public float ArcGap { get; set; } = 0.02f;

    /// <summary>Whether to show labels on arcs.</summary>
    public override bool ShowLabel { get; set; } = true;

    // Cached tree layout to avoid recomputation in HitTest
    private SunburstTree? _cachedTree;
    private int _cachedDataVersion = -1;

    /// <summary>Invalidate cached layout. Call when data or encodes change externally.</summary>
    public void InvalidateCache() { _cachedTree = null; _cachedDataVersion = -1; }

    public override void Render(MarkContext ctx)
    {
        if (ctx.Data.Count == 0) return;
        float anim = ComputeAnimProgress(ctx);

        float minDim = Math.Min(ctx.Plot.Width, ctx.Plot.Height);
        float cxP = ctx.Plot.X + ctx.Plot.Width / 2f;
        float cyP = ctx.Plot.Y + ctx.Plot.Height / 2f;
        float maxRadius = minDim / 2f * RadiusFactor;

        // Build hierarchy: group by parent (cached)
        _cachedDataVersion = ctx.LayoutVersion;
        _cachedTree = BuildTree(ctx);
        var tree = _cachedTree;
        if (tree.Roots.Count == 0) return;

        int depth = tree.MaxDepth;
        float innerR = maxRadius * InnerRadiusRatio;
        float ringW = (maxRadius - innerR - RingGap * (depth - 1)) / depth;

        // Render layer by layer (clip to plot area to prevent label overflow)
        using (ctx.Canvas.SaveScope())
        {
            ctx.Canvas.ClipRect(ctx.Plot.X, ctx.Plot.Y, ctx.Plot.Width, ctx.Plot.Height);
            RenderLayer(ctx, tree.Roots, 0, 0f, MathF.PI * 2f * anim,
                cxP, cyP, innerR, ringW, isRoot: true);
        }
    }

    private void RenderLayer(MarkContext ctx,
        List<SunburstNode> nodes, int layerIdx,
        float startAngle, float sweepAngle,
        float cx, float cy, float innerR, float ringW,
        bool isRoot = false)
    {
        if (nodes.Count == 0 || sweepAngle <= 0) return;

        float r1 = innerR + layerIdx * (ringW + RingGap);
        float r2 = r1 + ringW;
        double total = 0;
        foreach (var n in nodes) total += n.Value;
        if (total <= 0) return;

        // Count visible nodes and distribute gaps:
        // Root level uses N gaps (circle wraps around);
        // child levels use N-1 gaps (parent boundary already provides outer gap).
        int visibleCount = 0;
        foreach (var n in nodes)
        {
            float raw = (float)(n.Value / total * sweepAngle);
            if (raw > ArcGap) visibleCount++;
        }
        int gapCount = isRoot ? visibleCount : Math.Max(0, visibleCount - 1);
        float gapTotal = ArcGap * gapCount;
        float availableSweep = sweepAngle - gapTotal;
        if (availableSweep <= 0) return;

        float angle = startAngle;
        int rendered = 0;

        foreach (var node in nodes)
        {
            float nodeSweep = (float)(node.Value / total * availableSweep);
            if (nodeSweep <= 0) { continue; }
            rendered++;

            float midAngle = angle + nodeSweep / 2f;
            var color = ResolveColorWithOverride(ctx, node.Row, node.RowIndex, GetDefaultColor(ctx));
            float opacity = ComputeEffectiveOpacity(ctx, node.Row);

            bool isHovered = node.RowIndex == ctx.HoveredRowIndex;
            if (isHovered) color = BrightenColor(color, GetHoverBrighten(ctx));

            // Draw arc band
            using var arcPath = ctx.Canvas.CreatePath();
            using var arcPaint = ctx.Canvas.CreatePaint();

            // Outer arc
            arcPath.ArcTo(cx, cy, r2, angle, angle + nodeSweep);
            // Inner arc (reversed)
            arcPath.ArcTo(cx, cy, r1, angle + nodeSweep, angle, clockwise: true);
            arcPath.Close();

            arcPaint.SetColor(color).SetAntiAlias(true).SetOpacity(opacity * (ctx.Theme?.SunburstArcOpacity ?? 0.85f));
            ctx.Canvas.Fill(arcPath, arcPaint);

            // Selection outline
            if (node.RowIndex == ctx.SelectedRowIndex)
            {
                using var selPaint = ctx.Canvas.CreatePaint();
                ApplySelectionPaint(ctx, selPaint, opacity);
                ctx.Canvas.Stroke(arcPath, selPaint);
            }

            // Label (clipped to plot area by parent Save/ClipRect)
            if (ShowLabel && r2 - r1 > 12 && nodeSweep > (ctx.Theme?.SunburstLabelMinSweep ?? 0.15f))
            {
                float labelR = (r1 + r2) / 2f;
                float lx = cx + labelR * MathF.Cos(midAngle);
                float ly = cy + labelR * MathF.Sin(midAngle);
                var centeredFont = new FontSettings { Size = FontSettings.Default.Size, Align = TextAlign.Center };
                using var labelPaint = ctx.Canvas.CreatePaint();
                labelPaint.SetColor(GetDataLabelColor(ctx)).SetOpacity(opacity);
                ctx.Canvas.DrawText(node.Label, lx, ly, centeredFont, labelPaint);
            }

            // Recurse into children
            if (node.Children.Count > 0)
            {
                RenderLayer(ctx, node.Children, layerIdx + 1,
                    angle, nodeSweep, cx, cy, innerR, ringW);
            }

            angle += nodeSweep + (rendered < visibleCount ? ArcGap : 0);
        }
    }

    public override HitResult? HitTest(MarkContext ctx, Vector2 pos)
    {
        if (ctx.Data.Count == 0) return null;

        float minDim = Math.Min(ctx.Plot.Width, ctx.Plot.Height);
        float cx = ctx.Plot.X + ctx.Plot.Width / 2f;
        float cy = ctx.Plot.Y + ctx.Plot.Height / 2f;
        float maxRadius = minDim / 2f * RadiusFactor;

        // Use cached tree if available and data version matches
        if (_cachedDataVersion != ctx.LayoutVersion)
            _cachedTree = null;
        var tree = _cachedTree ?? BuildTree(ctx);
        if (tree.Roots.Count == 0) return null;

        int depth = tree.MaxDepth;
        float innerR = maxRadius * InnerRadiusRatio;
        float ringW = (maxRadius - innerR - RingGap * (depth - 1)) / depth;

        float dx = pos.X - cx;
        float dy = pos.Y - cy;
        float dist = MathF.Sqrt(dx * dx + dy * dy);
        float angle = MathF.Atan2(dy, dx);
        if (angle < 0) angle += MathF.PI * 2f;

        return HitTestLayer(ctx, tree.Roots, 0, 0f, MathF.PI * 2f,
            innerR, ringW, dist, angle, isRoot: true);
    }

    private HitResult? HitTestLayer(MarkContext ctx, List<SunburstNode> nodes,
        int layerIdx, float startAngle, float sweepAngle,
        float innerR, float ringW, float dist, float hitAngle,
        bool isRoot = false)
    {
        if (nodes.Count == 0) return null;

        float r1 = innerR + layerIdx * (ringW + RingGap);
        float r2 = r1 + ringW;
        double total = 0;
        foreach (var n in nodes) total += n.Value;
        if (total <= 0) return null;

        // Count visible nodes and distribute gaps (same logic as RenderLayer)
        int visibleCount = 0;
        foreach (var n in nodes)
        {
            float raw = (float)(n.Value / total * sweepAngle);
            if (raw > ArcGap) visibleCount++;
        }
        int gapCount = isRoot ? visibleCount : Math.Max(0, visibleCount - 1);
        float gapTotal = ArcGap * gapCount;
        float availableSweep = sweepAngle - gapTotal;
        if (availableSweep <= 0) return null;

        float angle = startAngle;
        int rendered = 0;
        foreach (var node in nodes)
        {
            float nodeSweep = (float)(node.Value / total * availableSweep);
            if (nodeSweep <= 0) { continue; }
            rendered++;

            // Check children first (outer layers)
            if (node.Children.Count > 0)
            {
                var childHit = HitTestLayer(ctx, node.Children, layerIdx + 1,
                    angle, nodeSweep, innerR, ringW, dist, hitAngle);
                if (childHit != null) return childHit;
            }

            // Check this arc
            if (dist >= r1 && dist <= r2)
            {
                float normAngle = hitAngle;
                if (normAngle >= angle && normAngle <= angle + nodeSweep)
                {
                    return new HitResult
                    {
                        Hit = true, Row = node.Row, RowIndex = node.RowIndex,
                        ScreenX = ctx.Plot.X + ctx.Plot.Width / 2f,
                        ScreenY = ctx.Plot.Y + ctx.Plot.Height / 2f,
                        Label = $"{node.Label}: {node.Value:G4}",
                        MarkType = nameof(SunburstMark),
                    };
                }
            }

            angle += nodeSweep + (rendered < visibleCount ? ArcGap : 0);
        }
        return null;
    }

    private SunburstTree BuildTree(MarkContext ctx)
    {
        var nodeMap = new Dictionary<string, SunburstNode>();
        var roots   = new List<SunburstNode>();

        // First pass: create all nodes with composite key (parent/label) to avoid collision
        for (int i = 0; i < ctx.Data.Count; i++)
        {
            var row   = ctx.Data[i];
            if (IsSeriesHidden(ctx, row)) continue;
            var label = ctx.Encodes.Resolve(Channel.X, row)?.ToString() ?? $"Item{i}";
            var yRaw  = ctx.Encodes.Resolve(YChannel, row);
            double val = yRaw != null ? ToDouble(yRaw, "Y") : 0;

            string parentKey = "";
            if (row.Has(ParentField))
                parentKey = row.Get<object>(ParentField).ToString() ?? "";

            string nodeKey = string.IsNullOrEmpty(parentKey) ? label : $"{parentKey}/{label}";

            var node = new SunburstNode
            {
                Label = label, Value = val, Row = row,
                RowIndex = i, ParentKey = parentKey
            };
            nodeMap[nodeKey] = node;
        }

        // Build label index for O(1) parent lookup
        var labelIndex = new Dictionary<string, List<SunburstNode>>();
        foreach (var node in nodeMap.Values)
        {
            if (!labelIndex.TryGetValue(node.Label, out var list))
                labelIndex[node.Label] = list = new List<SunburstNode>();
            list.Add(node);
        }

        // Second pass: link children
        foreach (var node in nodeMap.Values)
        {
            if (string.IsNullOrEmpty(node.ParentKey))
            {
                roots.Add(node);
            }
            else
            {
                SunburstNode? parent = null;
                if (labelIndex.TryGetValue(node.ParentKey, out var candidates))
                {
                    if (candidates.Count == 1)
                    {
                        parent = candidates[0];
                    }
                    else
                    {
                        // Prefer root-level parent; fallback to first candidate
                        foreach (var c in candidates)
                        {
                            if (string.IsNullOrEmpty(c.ParentKey))
                            {
                                parent = c;
                                break;
                            }
                        }
                        parent ??= candidates[0];
                    }
                }
                if (parent != null)
                    parent.Children.Add(node);
                else
                    roots.Add(node);
            }
        }

        // Propagate values: parent value = sum of children if it has children and value is 0
        PropagateValues(roots);

        // Calculate max depth
        int maxDepth = CalcDepth(roots);

        return new SunburstTree { Roots = roots, MaxDepth = maxDepth };
    }

    private static void PropagateValues(List<SunburstNode> nodes)
    {
        foreach (var node in nodes)
        {
            if (node.Children.Count > 0)
            {
                PropagateValues(node.Children);
                double childSum = 0;
                foreach (var c in node.Children) childSum += c.Value;
                if (node.Value <= 0) node.Value = childSum;
            }
        }
    }

    private static int CalcDepth(List<SunburstNode> nodes)
    {
        if (nodes.Count == 0) return 0;
        int maxChild = 0;
        foreach (var n in nodes)
        {
            int cd = CalcDepth(n.Children);
            if (cd > maxChild) maxChild = cd;
        }
        return 1 + maxChild;
    }

    private class SunburstNode
    {
        public string Label { get; init; } = "";
        public double Value { get; set; }
        public DataRow Row { get; init; } = null!;
        public int RowIndex { get; init; }
        public string ParentKey { get; init; } = "";
        public List<SunburstNode> Children { get; } = new();
    }

    private class SunburstTree
    {
        public List<SunburstNode> Roots { get; init; } = new();
        public int MaxDepth { get; init; }
    }
}
