using System;
using System.Collections.Generic;
using Godot;
using GodotNodeExtension.Component.GodotChart.Canvas;

namespace GodotNodeExtension.Component.GodotChart.Marks;

/// <summary>
/// Chord diagram mark. Renders circular relationship connections between categories.
/// Data: each row has SourceField = origin, TargetField = destination, Y = flow value.
/// Nodes are distributed as arcs around a circle; chords connect them.
/// Classified as polar-only (no Cartesian grid).
/// <para>
/// Node labels are formatted with <see cref="Mark.LabelFormat"/> ({0} = node name, {1} = the node's
/// total flow). The label position follows the arcs, so <see cref="Mark.LabelPosition"/> does not
/// apply to this mark.
/// </para>
/// </summary>
public class ChordMark : Mark
{
    /// <inheritdoc />
    public override MarkCoordinate Coordinate => MarkCoordinate.Flow;

    /// <summary>Field name for the source node.</summary>
    public string SourceField { get; set; } = "source";

    /// <summary>Field name for the target node.</summary>
    public string TargetField { get; set; } = "target";

    /// <summary>Width of node arcs as ratio of radius [0, 0.3].</summary>
    public float ArcWidthRatio { get; set; } = 0.06f;

    /// <summary>Gap between node arcs in radians.</summary>
    public float ArcGap { get; set; } = 0.04f;

    /// <summary>Chord fill opacity.</summary>
    public float ChordOpacity { get; set; } = 0.4f;

    /// <summary>Whether to show node labels.</summary>
    public override bool ShowLabel { get; set; } = true;

    /// <summary>Outer radius as ratio of min(width, height)/2. Range (0, 1].</summary>
    public float RadiusFactor { get; set; } = 0.85f;

    /// <summary>
    /// This mark keeps its interaction state in the data layer, so a chart carrying it renders single-pass
    /// (see <see cref="Chart.UseLayerCache"/>).
    /// <para>
    /// A chord's hover look is the chord's own colour, and a chord is drawn at
    /// <see cref="ChordOpacity"/> (0.4 by default): repainting it on the overlay would blend it twice
    /// (0.4 over 0.4) and darken the chord the pointer is on. Its geometry comes from the layout the data
    /// layer built (node angles and chord spans over the whole ring).
    /// </para>
    /// </summary>
    public override bool InteractionStateInOverlay => false;

    /// <summary>
    /// A chord diagram is drawn from the plot's <b>short</b> edge (<see cref="RadiusFactor"/> of
    /// <c>min(width, height) / 2</c>) plus the node labels around it, so it asks for a square content area:
    /// a wide canvas otherwise leaves the ring floating in the middle of an empty band.
    /// </summary>
    public override float? PreferredAspectRatio => 1f;

    // Cached layout to avoid recomputation in HitTest
    private ChordLayout? _cachedLayout;
    private LayoutCacheKey? _cacheKey;
    // <see cref="LayoutConfigHash"/> of the frame the cached layout was built for. Null while nothing is cached.
    private int? _layoutConfigHash;

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
        => HashCode.Combine(ArcGap, ArcWidthRatio, RadiusFactor, SourceField, TargetField, YChannel);

    /// <summary>Layout for the current context, built once per cache key and layout configuration.</summary>
    private ChordLayout? CachedLayout(MarkContext ctx)
    {
        var key = CacheKey(ctx);
        int configHash = LayoutConfigHash();
        if (_cachedLayout != null && _cacheKey == key && _layoutConfigHash == configHash)
            return _cachedLayout;

        _cachedLayout = BuildChordLayout(ctx);
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

        var (cx, cy, radius) = PolarGeometry.CenterAndRadius(ctx.Plot, RadiusFactor);

        float arcW = radius * ArcWidthRatio;
        float innerR = radius - arcW;
        float globalOpacity = ctx.Animation.GlobalOpacity;

        // Draw chords first (behind arcs)
        foreach (var chord in layout.Chords)
        {
            float sa1 = chord.SourceStartAngle;
            float sa2 = chord.SourceEndAngle;
            float ta1 = chord.TargetStartAngle;
            float ta2 = chord.TargetEndAngle;

            // Animate sweep
            float mid1 = (sa1 + sa2) / 2f;
            float mid2 = (ta1 + ta2) / 2f;
            sa1 = mid1 + (sa1 - mid1) * anim;
            sa2 = mid1 + (sa2 - mid1) * anim;
            ta1 = mid2 + (ta1 - mid2) * anim;
            ta2 = mid2 + (ta2 - mid2) * anim;

            var chordPath = ShapePath(ctx);
            var chordPaint = ShapePaint(ctx);

            // Source arc on inner circle
            chordPath.ArcTo(cx, cy, innerR, sa1, sa2);
            // Bezier to target with tangent-offset control points
            float cpR = innerR * 0.4f;
            float tx1 = cx + innerR * MathF.Cos(ta2);
            float ty1 = cy + innerR * MathF.Sin(ta2);
            float cp1X = cx + cpR * MathF.Cos(sa2);
            float cp1Y = cy + cpR * MathF.Sin(sa2);
            float cp2X = cx + cpR * MathF.Cos(ta2);
            float cp2Y = cy + cpR * MathF.Sin(ta2);
            chordPath.CubicTo(cp1X, cp1Y, cp2X, cp2Y, tx1, ty1);
            // Target arc on inner circle
            chordPath.ArcTo(cx, cy, innerR, ta2, ta1, clockwise: true);
            // Bezier back to source with tangent-offset control points
            float sx1 = cx + innerR * MathF.Cos(sa1);
            float sy1 = cy + innerR * MathF.Sin(sa1);
            float cp3X = cx + cpR * MathF.Cos(ta1);
            float cp3Y = cy + cpR * MathF.Sin(ta1);
            float cp4X = cx + cpR * MathF.Cos(sa1);
            float cp4Y = cy + cpR * MathF.Sin(sa1);
            chordPath.CubicTo(cp3X, cp3Y, cp4X, cp4Y, sx1, sy1);
            chordPath.Close();

            var chordRow = ctx.Data[chord.RowIndex];
            // A chord reads as "something leaving the source node", so it carries that node's colour.
            var color = ResolveFill(ctx, chordRow, chord.RowIndex, chord.SourceColor);
            float opacity = ComputeElementOpacity(ctx, chordRow, chord.RowIndex);

            chordPaint.SetColor(color).SetAntiAlias(true).SetOpacity(ChordOpacity * opacity);
            ctx.Canvas.Fill(chordPath, chordPaint);

            // Selected: outline the chord of the selected row (States.SelectedStroke / width).
            if (chord.RowIndex == ctx.SelectedRowIndex)
            {
                var selPaint = ShapePaint(ctx);
                ApplySelectionPaint(ctx, selPaint, opacity);
                ctx.Canvas.Stroke(chordPath, selPaint);
            }
        }

        // Draw node arcs
        foreach (var node in layout.Nodes)
        {
            float end = node.StartAngle + (node.EndAngle - node.StartAngle) * anim;
            var arcPath = ShapePath(ctx);
            var arcPaint = ShapePaint(ctx);

            ShapeGeometry.AddRingBand(arcPath, cx, cy, radius, innerR, node.StartAngle, end);

            arcPaint.SetColor(node.Color).SetAntiAlias(true).SetOpacity(globalOpacity);
            ctx.Canvas.Fill(arcPath, arcPaint);

            if (ShowLabel)
            {
                float labelAngle = (node.StartAngle + end) / 2f;
                float labelR = radius + (ctx.Theme ?? ChartTheme.Default).ChordLabelDistance;
                float lx = cx + labelR * MathF.Cos(labelAngle);
                float ly = cy + labelR * MathF.Sin(labelAngle);
                var labelPaint = ShapePaint(ctx);
                labelPaint.SetColor(GetDataLabelColor(ctx)).SetOpacity(globalOpacity);
                // {0} = node name, {1} = the node's total flow, so "{0} ({1})" and friends work.
                DrawTextCentered(ctx, labelPaint, FormatLabel(LabelFormat, node.Label, node.Total),
                    lx, ly, FontSettings.Default);
            }
        }
    }

    /// <inheritdoc />
    public override HitResult? HitTest(MarkContext ctx, Vector2 screenPos)
    {
        if (ctx.Data.Count == 0) return null;

        var layout = CachedLayout(ctx);
        if (layout == null) return null;

        // Same entry progress as Render: the node arcs grow toward their end angle and the chords
        // shrink toward their own mid angle and disappear at 0, so nothing is hittable before the
        // animation has drawn it.
        float anim = ComputeAnimProgress(ctx);
        if (anim <= 0f) return null;

        var (cxP, cyP, radius) = PolarGeometry.CenterAndRadius(ctx.Plot, RadiusFactor);
        float innerR = radius - radius * ArcWidthRatio;

        float dist = PolarGeometry.Distance(cxP, cyP, screenPos.X, screenPos.Y);
        float hitAngle = PolarGeometry.AngleOf(cxP, cyP, screenPos.X, screenPos.Y);

        // Check node arcs (outer ring)
        if (dist >= innerR && dist <= radius)
        {
            foreach (var node in layout.Nodes)
            {
                // Only the arc the animation has already swept is drawn, so only that part is hittable.
                float end = node.StartAngle + (node.EndAngle - node.StartAngle) * anim;
                if (AngleInRange(hitAngle, node.StartAngle, end))
                {
                    return new HitResult
                    {
                        Hit = true, Row = ctx.Data[0], RowIndex = -1,
                        ScreenX = cxP, ScreenY = cyP,
                        Label = node.Label,
                        MarkType = nameof(ChordMark),
                    };
                }
            }
            return null;
        }

        if (dist > innerR) return null;

        // Check chords by proximity to source/target midpoints
        foreach (var chord in layout.Chords)
        {
            float sMid = (chord.SourceStartAngle + chord.SourceEndAngle) / 2f;
            float tMid = (chord.TargetStartAngle + chord.TargetEndAngle) / 2f;
            float tolerance = (ctx.Theme ?? ChartTheme.Default).HitTestAngleTolerance;
            if ((AngleDistance(hitAngle, sMid) < tolerance || AngleDistance(hitAngle, tMid) < tolerance)
                && dist < innerR * 0.9f)
            {
                // See SankeyMark: the layout indices refer to the data the layout was built from.
                if (chord.RowIndex < 0 || chord.RowIndex >= ctx.Data.Count) continue;
                var row = ctx.Data[chord.RowIndex];
                return new HitResult
                {
                    Hit = true,
                    Row = row,
                    RowIndex = chord.RowIndex,
                    ScreenX = cxP,
                    ScreenY = cyP,
                    Label = $"{chord.Source} → {chord.Target}: {chord.Value:G4}",
                    // Same key Render resolves for the chord's row, so hover/focus behaves identically.
                    SeriesKey = ResolveSeriesKey(ctx, row),
                    MarkType = nameof(ChordMark),
                };
            }
        }
        return null;
    }

    private static float AngleDistance(float a, float b)
    {
        float diff = MathF.Abs(a - b);
        return MathF.Min(diff, MathF.PI * 2f - diff);
    }

    /// <summary>
    /// Whether an angle falls inside a span that runs forwards: the caller passes a start plus a non-negative
    /// sweep, so an end below the start cannot come out of it.
    /// </summary>
    private static bool AngleInRange(float angle, float start, float end)
        => angle >= start && angle <= end;

    private ChordLayout? BuildChordLayout(MarkContext ctx)
    {
        var nodeNames = new List<string>();
        var nodeSet   = new HashSet<string>();
        var flows     = new List<(string src, string tgt, double val, int rowIdx)>();

        for (int i = 0; i < ctx.Data.Count; i++)
        {
            var row = ctx.Data[i];
            if (IsSeriesHidden(ctx, row)) continue;
            // A null source/target means "missing relation": skip the row rather than
            // dereferencing a null reference (GetStringOrNull returns null for null values).
            string? src = GetStringOrNull(row, SourceField);
            string? tgt = GetStringOrNull(row, TargetField);
            if (src is null || tgt is null) continue;
            var yRaw = ctx.Encodes.Resolve(YChannel, row);
            double val = yRaw != null ? ToDouble(yRaw, "Y") : 1;
            // A non-finite value has no angle: skip the flow instead of poisoning the totals.
            if (!double.IsFinite(val) || val <= 0) continue;

            if (nodeSet.Add(src)) nodeNames.Add(src);
            if (nodeSet.Add(tgt)) nodeNames.Add(tgt);
            flows.Add((src, tgt, val, i));
        }
        if (flows.Count == 0) return null;

        // Calculate total per node
        var nodeTotals = new Dictionary<string, double>(nodeNames.Count);
        foreach (var n in nodeNames) nodeTotals[n] = 0.0;
        foreach (var (src, tgt, val, _) in flows)
        {
            nodeTotals[src] += val;
            nodeTotals[tgt] += val;
        }

        double grandTotal = 0;
        foreach (var v in nodeTotals.Values) grandTotal += v;
        if (grandTotal <= 0) return null;

        // The gaps are part of the sweep budget: node sweeps plus gaps must never exceed one turn, and
        // a sweep of exactly 2*pi is turned into 0 by the backend (angles are taken modulo a full
        // turn), which would make the node disappear. Many nodes with a large ArcGap used to overrun
        // the circle (each arc advanced by the *uncapped* gap), overlapping the first nodes and
        // making HitTest report the wrong one; the capped total is therefore divided over the gaps.
        float gapCount = nodeNames.Count;
        float totalGap = MathF.Min(MathF.Max(0f, ArcGap) * gapCount, MathF.PI * 0.5f);
        float gap = gapCount > 0 ? totalGap / gapCount : 0f;
        float available = MathF.Min(MathF.Tau - totalGap, PolarGeometry.FullTurnCap);

        // Assign arc angles
        var nodeArcs = new List<ChordNode>();
        var nodeAngleStarts = new Dictionary<string, float>();
        var nodeAngleEnds   = new Dictionary<string, float>();
        var nodeColors      = new Dictionary<string, Color>();
        var palette = PaletteOf(ctx);

        float angle = 0;
        for (int i = 0; i < nodeNames.Count; i++)
        {
            string name = nodeNames[i];
            float sweep = (float)(nodeTotals[name] / grandTotal * available);
            nodeAngleStarts[name] = angle;
            nodeAngleEnds[name]   = angle + sweep;
            nodeColors[name]      = palette[i % palette.Length];
            nodeArcs.Add(new ChordNode
            {
                Label = name, StartAngle = angle, EndAngle = angle + sweep,
                Color = nodeColors[name], Total = nodeTotals[name],
            });
            angle += sweep + gap;
        }

        // Build chord data
        var chords = new List<ChordData>();
        var nodeOffsets = new Dictionary<string, float>(nodeNames.Count);
        foreach (var n in nodeNames) nodeOffsets[n] = nodeAngleStarts[n];

        foreach (var (src, tgt, val, rowIdx) in flows)
        {
            float srcSweep = (float)(val / nodeTotals[src] * (nodeAngleEnds[src] - nodeAngleStarts[src]));
            float tgtSweep = (float)(val / nodeTotals[tgt] * (nodeAngleEnds[tgt] - nodeAngleStarts[tgt]));

            chords.Add(new ChordData
            {
                Source = src, Target = tgt, Value = val, RowIndex = rowIdx,
                SourceColor = nodeColors[src],
                SourceStartAngle = nodeOffsets[src],
                SourceEndAngle   = nodeOffsets[src] + srcSweep,
                TargetStartAngle = nodeOffsets[tgt],
                TargetEndAngle   = nodeOffsets[tgt] + tgtSweep,
            });

            nodeOffsets[src] += srcSweep;
            nodeOffsets[tgt] += tgtSweep;
        }

        return new ChordLayout { Nodes = nodeArcs, Chords = chords };
    }

    private sealed class ChordNode
    {
        public string Label { get; init; } = "";
        public float StartAngle { get; init; }
        public float EndAngle { get; init; }
        public Color Color { get; init; }

        /// <summary>Total flow through this node; the second placeholder of the node label.</summary>
        public double Total { get; init; }
    }

    private sealed class ChordData
    {
        public string Source { get; init; } = "";
        public string Target { get; init; } = "";
        public double Value { get; init; }
        public int RowIndex { get; init; }

        /// <summary>Colour of the source node, used as the chord's fallback colour.</summary>
        public Color SourceColor { get; init; }

        public float SourceStartAngle { get; init; }
        public float SourceEndAngle { get; init; }
        public float TargetStartAngle { get; init; }
        public float TargetEndAngle { get; init; }
    }

    private sealed class ChordLayout
    {
        public List<ChordNode> Nodes { get; init; } = [];
        public List<ChordData> Chords { get; init; } = [];
    }
}