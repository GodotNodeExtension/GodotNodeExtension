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
/// <para>
/// Arc labels are formatted with <see cref="Mark.LabelFormat"/> ({0} = node label, {1} = its value).
/// Each label sits in the middle of its arc, so <see cref="Mark.LabelPosition"/> does not apply to
/// this mark.
/// </para>
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

    /// <summary>
    /// Shading step per ring: the arcs of one top-level branch keep that branch's palette colour while
    /// every deeper ring darkens by this factor, so the depth stays readable. 0 draws one flat colour
    /// per branch.
    /// </summary>
    public float DepthShadeStep { get; set; } = 0.18f;

    /// <summary>Whether to show labels on arcs.</summary>
    public override bool ShowLabel { get; set; } = true;

    /// <summary>
    /// This mark keeps its interaction state in the data layer, so a chart carrying it renders single-pass
    /// (see <see cref="Chart.UseLayerCache"/>).
    /// <para>
    /// An arc's hover look is the arc's own colour, drawn at the theme's
    /// <see cref="ChartTheme.SunburstArcOpacity"/> (0.85 by default, so a doubled blend is plainly visible),
    /// and an arc's angles come from a recursive walk that distributes the ring's gaps over all the nodes:
    /// the overlay would have to replay that walk for every hover frame.
    /// </para>
    /// </summary>
    public override bool InteractionStateInOverlay => false;

    /// <summary>
    /// A sunburst is drawn from the plot's <b>short</b> edge (<see cref="RadiusFactor"/> of
    /// <c>min(width, height) / 2</c>), so it asks for a square content area: a wide canvas otherwise leaves the
    /// rings floating in the middle of an empty band.
    /// </summary>
    public override float? PreferredAspectRatio => 1f;

    // Cached tree layout to avoid recomputation in HitTest
    private SunburstTree? _cachedTree;
    private LayoutCacheKey? _cacheKey;
    // <see cref="LayoutConfigHash"/> of the frame the cached tree was built for. Null while nothing is cached.
    private int? _layoutConfigHash;

    /// <summary>Number of times the tree was rebuilt — used by tests to verify caching.</summary>
    internal int LayoutBuildCount { get; private set; }

    /// <summary>
    /// Force the cached layout to be rebuilt.
    /// The cache key already covers the chart, the layout version, the data list identity and the
    /// mark's own layout configuration, so this is only needed when the contents of the very same
    /// data list are mutated in place.
    /// </summary>
    public void InvalidateCache()
    {
        _cachedTree = null;
        _cacheKey = null;
        _layoutConfigHash = null;
    }

    /// <summary>
    /// Hash of the mark properties that shape the layout but are invisible to <see cref="Mark.CacheKey"/>:
    /// they are plain auto-properties without a setter hook, so the cache has to notice a changed
    /// configuration by comparing this value instead of trusting the layout version.
    /// </summary>
    private int LayoutConfigHash()
        => HashCode.Combine(ParentField, RadiusFactor, InnerRadiusRatio, RingGap, ArcGap, YChannel);

    /// <summary>Hierarchy for the current context, built once per cache key and layout configuration.</summary>
    private SunburstTree CachedTree(MarkContext ctx)
    {
        var key = CacheKey(ctx);
        int configHash = LayoutConfigHash();
        if (_cachedTree != null && _cacheKey == key && _layoutConfigHash == configHash)
            return _cachedTree;

        _cachedTree = BuildTree(ctx);
        _cacheKey = key;
        _layoutConfigHash = configHash;
        LayoutBuildCount++;
        return _cachedTree;
    }

    /// <summary>
    /// Ring geometry shared by <see cref="Render"/> and <see cref="HitTest"/>: the inner radius of the
    /// first ring, the width of one ring and the gap used between two rings.
    /// <para>
    /// The rings plus their gaps never reach past <paramref name="maxRadius"/>. When a large
    /// <see cref="RingGap"/> (or a deep hierarchy) leaves less than a pixel per ring, the gap is
    /// shrunk so the outermost ring still ends on the plot's radius - a fixed minimum ring width used
    /// to push the last rings outside the plot radius, where they were drawn (and hit) beyond the arc
    /// band the plot can show.
    /// </para>
    /// </summary>
    private (float innerR, float ringW, float gap) RingGeometry(float maxRadius, int depth)
    {
        float innerR = maxRadius * InnerRadiusRatio;
        if (depth <= 0) return (innerR, 0f, 0f);

        float available = MathF.Max(0f, maxRadius - innerR);
        float gap = MathF.Max(0f, RingGap);
        const float minRingW = 1f;
        if (depth > 1 && available - gap * (depth - 1) < minRingW * depth)
        {
            // Not enough room for the requested gap: shrink it so every ring still fits.
            gap = MathF.Max(0f, (available - minRingW * depth) / (depth - 1));
        }
        float ringW = MathF.Max(0f, (available - gap * (depth - 1)) / depth);
        return (innerR, ringW, gap);
    }

    /// <inheritdoc />
    public override void Render(MarkContext ctx)
    {
        if (ctx.Data.Count == 0) return;
        float anim = ComputeAnimProgress(ctx);

        float minDim = Math.Min(ctx.Plot.Width, ctx.Plot.Height);
        var (cxP, cyP) = PolarGeometry.Center(ctx.Plot);
        float maxRadius = minDim / 2f * RadiusFactor;

        // Build hierarchy: group by parent (cached)
        var tree = CachedTree(ctx);
        if (tree.Roots.Count == 0) return;

        int depth = tree.MaxDepth;
        var (innerR, ringW, ringGap) = RingGeometry(maxRadius, depth);

        // Which ring carries the branches and their palette colours. Several roots are branches
        // themselves; a single root is the frame (the usual "total" row), so its children are.
        int branchDepth = tree.Roots.Count > 1 ? 0 : 1;

        // Render layer by layer (clip to plot area to prevent label overflow)
        using (ctx.Canvas.SaveScope())
        {
            ctx.Canvas.ClipRect(ctx.Plot.X, ctx.Plot.Y, ctx.Plot.Width, ctx.Plot.Height);
            RenderLayer(ctx, tree.Roots, 0, 0f, MathF.PI * 2f * anim,
                cxP, cyP, innerR, ringW, ringGap, branchDepth, branchColor: null, isRoot: true);
        }
    }

    private void RenderLayer(MarkContext ctx,
        List<SunburstNode> nodes, int layerIdx,
        float startAngle, float sweepAngle,
        float cx, float cy, float innerR, float ringW, float ringGap,
        int branchDepth, Color? branchColor, bool isRoot = false)
    {
        if (nodes.Count == 0 || sweepAngle <= 0) return;

        float r1 = innerR + layerIdx * (ringW + ringGap);
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
        int index = 0;
        bool branchRing = layerIdx == branchDepth;
        var palette = PaletteOf(ctx);

        foreach (var node in nodes)
        {
            float nodeSweep = (float)(node.Value / total * availableSweep);
            if (nodeSweep <= 0) { continue; }

            // Three roles, one rule each: the branch ring gives every branch its own palette colour, rings
            // below it keep that colour and darken with the depth, and the frame above it (a single root)
            // stays neutral - shaded once, so it never reads as the first branch.
            Color fallback = branchRing
                ? palette[index % palette.Length]
                : branchColor is { } inherited
                    ? DepthShaded(inherited, layerIdx - branchDepth)
                    : DepthShaded(GetDefaultColor(ctx), 1);
            index++;
            rendered++;

            float midAngle = angle + nodeSweep / 2f;
            var color = ResolveFill(ctx, node.Row, node.RowIndex, fallback);
            float opacity = ComputeElementOpacity(ctx, node.Row, node.RowIndex);

            // Draw arc band
            var arcPath = ShapePath(ctx);
            var arcPaint = ShapePaint(ctx);

            ShapeGeometry.AddRingBand(arcPath, cx, cy, r2, r1, angle, angle + nodeSweep);

            arcPaint.SetColor(color).SetAntiAlias(true).SetOpacity(opacity * (ctx.Theme ?? ChartTheme.Default).SunburstArcOpacity);
            ctx.Canvas.Fill(arcPath, arcPaint);

            // Selection outline
            if (node.RowIndex == ctx.SelectedRowIndex)
            {
                var selPaint = ShapePaint(ctx);
                ApplySelectionPaint(ctx, selPaint, opacity);
                ctx.Canvas.Stroke(arcPath, selPaint);
            }

            // Label (clipped to plot area by parent Save/ClipRect)
            if (ShowLabel && r2 - r1 > 12 && nodeSweep > (ctx.Theme ?? ChartTheme.Default).SunburstLabelMinSweep)
            {
                float labelR = (r1 + r2) / 2f;
                float lx = cx + labelR * MathF.Cos(midAngle);
                float ly = cy + labelR * MathF.Sin(midAngle);
                var labelPaint = ShapePaint(ctx);
                labelPaint.SetColor(GetDataLabelColor(ctx)).SetOpacity(opacity);
                // {0} = node label, {1} = its value, so "{0} ({1})" and friends work.
                DrawTextCentered(ctx, labelPaint, FormatLabel(LabelFormat, node.Label, node.Value),
                    lx, ly, FontSettings.Default);
            }

            // Recurse into children
            if (node.Children.Count > 0)
            {
                RenderLayer(ctx, node.Children, layerIdx + 1,
                    angle, nodeSweep, cx, cy, innerR, ringW, ringGap,
                    branchDepth, branchRing ? fallback : branchColor);
            }

            angle += nodeSweep + (rendered < visibleCount ? ArcGap : 0);
        }
    }

    /// <summary>
    /// Shade a colour by how many rings below the branch ring it sits: 0 is the branch itself (kept as
    /// it is), every further ring darkens by <see cref="DepthShadeStep"/>.
    /// </summary>
    private Color DepthShaded(Color color, int ringsBelowBranch)
        => ringsBelowBranch <= 0 || DepthShadeStep <= 0f
            ? color
            : BrightenColor(color, MathF.Max(0.4f, 1f - DepthShadeStep * ringsBelowBranch));

    /// <inheritdoc />
    public override HitResult? HitTest(MarkContext ctx, Vector2 screenPos)
    {
        if (ctx.Data.Count == 0) return null;
        // Render is clipped to the plot rectangle (see Render); a point outside it can never be part
        // of a drawn (and therefore hittable) arc.
        if (!ctx.Plot.Contains(screenPos.X, screenPos.Y)) return null;

        float minDim = Math.Min(ctx.Plot.Width, ctx.Plot.Height);
        var (cx, cy) = PolarGeometry.Center(ctx.Plot);
        float maxRadius = minDim / 2f * RadiusFactor;

        // Use cached tree if available and data version matches
        var tree = CachedTree(ctx);
        if (tree.Roots.Count == 0) return null;

        // Same ring geometry and the same animation progress as Render: an arc is only hittable once
        // the entry animation has swept over it.
        var (innerR, ringW, ringGap) = RingGeometry(maxRadius, tree.MaxDepth);
        float anim = ComputeAnimProgress(ctx);

        float dist = PolarGeometry.Distance(cx, cy, screenPos.X, screenPos.Y);
        float angle = PolarGeometry.AngleOf(cx, cy, screenPos.X, screenPos.Y);

        return HitTestLayer(ctx, tree.Roots, 0, 0f, MathF.PI * 2f * anim,
            innerR, ringW, ringGap, dist, angle, isRoot: true);
    }

    private HitResult? HitTestLayer(MarkContext ctx, List<SunburstNode> nodes,
        int layerIdx, float startAngle, float sweepAngle,
        float innerR, float ringW, float ringGap, float dist, float hitAngle,
        bool isRoot = false)
    {
        if (nodes.Count == 0) return null;

        float r1 = innerR + layerIdx * (ringW + ringGap);
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
                    angle, nodeSweep, innerR, ringW, ringGap, dist, hitAngle);
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
        int duplicateKeys = 0;

        // First pass: create all nodes with composite key (parent/label) to avoid collision
        for (int i = 0; i < ctx.Data.Count; i++)
        {
            var row   = ctx.Data[i];
            if (IsSeriesHidden(ctx, row)) continue;
            var label = ctx.Encodes.Resolve(Channel.X, row)?.ToString() ?? $"Item{i}";
            var yRaw  = ctx.Encodes.Resolve(YChannel, row);
            double val = yRaw != null ? ToDouble(yRaw, "Y") : 0;
            // A non-finite value would poison every total and sweep: skip the row entirely (it
            // takes part neither in the layout nor in hit testing). A null value stays 0 so a pure
            // grouping node can still derive its value from its children.
            if (!double.IsFinite(val)) continue;
            // A negative value would make a layer's total smaller than the sum of its positive sweeps, so
            // the arcs would overrun the span their parent gave them (and hit testing would report the
            // wrong node). Clamp it away instead of dropping the row: a grouping row with no value is 0
            // on purpose, and TreemapMark clamps the same way.
            if (val < 0) val = 0;

            // A null parent field means "root level"; GetStringOrNull keeps that from throwing.
            string parentKey = GetStringOrNull(row, ParentField) ?? "";

            string nodeKey = string.IsNullOrEmpty(parentKey) ? label : $"{parentKey}/{label}";

            var node = new SunburstNode
            {
                Label = label, Value = val, Row = row,
                RowIndex = i, ParentKey = parentKey
            };
            // Two rows under the same parent with the same label share one key: the later row would
            // replace the earlier one and disappear from the chart without a word. The last row wins
            // (as before), but the loss is reported once per build.
            if (!nodeMap.TryAdd(nodeKey, node)) duplicateKeys++;
        }
        if (duplicateKeys > 0)
        {
            GD.PushWarning(
                $"SunburstMark: {duplicateKeys} row(s) share the same parent and label; " +
                "only the last one of each key is drawn. Give the rows distinct labels.");
        }

        // Build label index for O(1) parent lookup
        var labelIndex = new Dictionary<string, List<SunburstNode>>();
        foreach (var node in nodeMap.Values)
        {
            if (!labelIndex.TryGetValue(node.Label, out var list))
                labelIndex[node.Label] = list = [];
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

        // Break parent cycles before traversing: a row that names itself as parent, or a
        // parent chain that loops, would make the recursive traversals below run forever
        // (StackOverflowException, which cannot be caught).
        int brokenLinks = BreakCycles(nodeMap, roots);
        if (brokenLinks > 0)
        {
            GD.PushWarning(
                $"SunburstMark: ignored {brokenLinks} cyclic parent reference(s); " +
                "the affected nodes were promoted to root level.");
        }

        // Propagate values: parent value = sum of children if it has children and value is 0
        PropagateValues(roots);

        // Calculate max depth
        int maxDepth = CalcDepth(roots);

        return new SunburstTree { Roots = roots, MaxDepth = maxDepth };
    }

    /// <summary>
    /// Detach every parent link that does not lead up to a root, so the resulting forest is acyclic.
    /// A node whose ancestry loops back to itself is promoted to a root instead of being dropped.
    /// </summary>
    /// <returns>The number of detached links (0 when the input was already a forest).</returns>
    internal static int BreakCycles(Dictionary<string, SunburstNode> nodeMap, List<SunburstNode> roots)
    {
        var parentOf = new Dictionary<SunburstNode, SunburstNode>();
        foreach (var node in nodeMap.Values)
        {
            foreach (var child in node.Children)
                parentOf[child] = node;
        }

        int broken = 0;
        var visited = new HashSet<SunburstNode>();
        foreach (var node in nodeMap.Values)
        {
            if (!parentOf.TryGetValue(node, out var parent)) continue; // already a root

            // Walk up from the parent: if the chain leads back to `node`, its own incoming
            // link closes a cycle and has to be removed.
            bool inCycle = false;
            visited.Clear();
            var current = parent;
            while (true)
            {
                if (ReferenceEquals(current, node)) { inCycle = true; break; }
                if (!visited.Add(current)) break;                    // loop that excludes `node`
                if (!parentOf.TryGetValue(current, out var next)) break; // reached a root
                current = next;
            }

            if (!inCycle) continue;

            // Cut the incoming link of `node` and promote it: it stays reachable and keeps
            // its own children, so no subtree is dropped.
            parent.Children.Remove(node);
            parentOf.Remove(node);
            roots.Add(node);
            broken++;
        }

        return broken;
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

    /// <summary>
    /// A node of the sunburst hierarchy.
    /// Internal (not private) so the tree building and cycle breaking can be unit tested.
    /// </summary>
    internal sealed class SunburstNode
    {
        public string Label { get; init; } = "";
        public double Value { get; set; }
        public DataRow Row { get; init; } = null!;
        public int RowIndex { get; init; }
        public string ParentKey { get; init; } = "";
        public List<SunburstNode> Children { get; } = [];
    }

    /// <summary>
    /// The built hierarchy: root nodes plus the maximum nesting depth.
    /// </summary>
    private sealed class SunburstTree
    {
        public List<SunburstNode> Roots { get; init; } = [];
        public int MaxDepth { get; init; }
    }
}
