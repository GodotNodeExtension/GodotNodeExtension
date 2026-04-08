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

    // Cached layout to avoid recomputation in HitTest
    private ChordLayout? _cachedLayout;
    private int _cachedDataVersion = -1;

    /// <summary>Invalidate cached layout. Call when data or encodes change externally.</summary>
    public void InvalidateCache() { _cachedLayout = null; _cachedDataVersion = -1; }

    public override void Render(MarkContext ctx)
    {
        if (ctx.Data.Count == 0) return;
        float anim = ComputeAnimProgress(ctx);

        _cachedDataVersion = ctx.LayoutVersion;
        _cachedLayout = BuildChordLayout(ctx);
        var layout = _cachedLayout;
        if (layout == null) return;

        float cx = ctx.Plot.X + ctx.Plot.Width / 2f;
        float cy = ctx.Plot.Y + ctx.Plot.Height / 2f;
        float radius = Math.Min(ctx.Plot.Width, ctx.Plot.Height) / 2f * RadiusFactor;
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

            using var chordPath = ctx.Canvas.CreatePath();
            using var chordPaint = ctx.Canvas.CreatePaint();

            // Source arc on inner circle
            chordPath.ArcTo(cx, cy, innerR, sa1, sa2);
            // Bezier to target with tangent-offset control points
            float cpR = innerR * 0.4f;
            float tx1 = cx + innerR * MathF.Cos(ta2);
            float ty1 = cy + innerR * MathF.Sin(ta2);
            float cp1x = cx + cpR * MathF.Cos(sa2);
            float cp1y = cy + cpR * MathF.Sin(sa2);
            float cp2x = cx + cpR * MathF.Cos(ta2);
            float cp2y = cy + cpR * MathF.Sin(ta2);
            chordPath.CubicTo(cp1x, cp1y, cp2x, cp2y, tx1, ty1);
            // Target arc on inner circle
            chordPath.ArcTo(cx, cy, innerR, ta2, ta1, clockwise: true);
            // Bezier back to source with tangent-offset control points
            float sx1 = cx + innerR * MathF.Cos(sa1);
            float sy1 = cy + innerR * MathF.Sin(sa1);
            float cp3x = cx + cpR * MathF.Cos(ta1);
            float cp3y = cy + cpR * MathF.Sin(ta1);
            float cp4x = cx + cpR * MathF.Cos(sa1);
            float cp4y = cy + cpR * MathF.Sin(sa1);
            chordPath.CubicTo(cp3x, cp3y, cp4x, cp4y, sx1, sy1);
            chordPath.Close();

            var chordRow = ctx.Data[chord.RowIndex];
            var color = ResolveColorWithOverride(ctx, chordRow, chord.RowIndex, GetDefaultColor(ctx));
            bool isHovered = chord.RowIndex == ctx.HoveredRowIndex;
            if (isHovered) color = BrightenColor(color, GetHoverBrighten(ctx));
            float opacity = ComputeEffectiveOpacity(ctx, chordRow);

            chordPaint.SetColor(color).SetAntiAlias(true).SetOpacity(ChordOpacity * opacity);
            ctx.Canvas.Fill(chordPath, chordPaint);
        }

        // Draw node arcs
        foreach (var node in layout.Nodes)
        {
            float end = node.StartAngle + (node.EndAngle - node.StartAngle) * anim;
            using var arcPath = ctx.Canvas.CreatePath();
            using var arcPaint = ctx.Canvas.CreatePaint();

            arcPath.ArcTo(cx, cy, radius, node.StartAngle, end);
            arcPath.ArcTo(cx, cy, innerR, end, node.StartAngle, clockwise: true);
            arcPath.Close();

            arcPaint.SetColor(node.Color).SetAntiAlias(true).SetOpacity(globalOpacity);
            ctx.Canvas.Fill(arcPath, arcPaint);

            if (ShowLabel)
            {
                float labelAngle = (node.StartAngle + end) / 2f;
                float labelR = radius + (ctx.Theme?.ChordLabelDistance ?? 8f);
                float lx = cx + labelR * MathF.Cos(labelAngle);
                float ly = cy + labelR * MathF.Sin(labelAngle);
                using var labelPaint = ctx.Canvas.CreatePaint();
                labelPaint.SetColor(GetDataLabelColor(ctx)).SetOpacity(globalOpacity);
                ctx.Canvas.DrawText(node.Label, lx, ly, FontSettings.Default, labelPaint);
            }
        }
    }

    public override HitResult? HitTest(MarkContext ctx, Vector2 pos)
    {
        if (ctx.Data.Count == 0) return null;

        if (_cachedDataVersion != ctx.LayoutVersion)
            _cachedLayout = null;
        var layout = _cachedLayout ?? BuildChordLayout(ctx);
        if (layout == null) return null;

        float cxP = ctx.Plot.X + ctx.Plot.Width / 2f;
        float cyP = ctx.Plot.Y + ctx.Plot.Height / 2f;
        float radius = Math.Min(ctx.Plot.Width, ctx.Plot.Height) / 2f * RadiusFactor;
        float innerR = radius - radius * ArcWidthRatio;

        float dx = pos.X - cxP;
        float dy = pos.Y - cyP;
        float dist = MathF.Sqrt(dx * dx + dy * dy);

        float hitAngle = MathF.Atan2(dy, dx);
        if (hitAngle < 0) hitAngle += MathF.PI * 2f;

        // Check node arcs (outer ring)
        if (dist >= innerR && dist <= radius)
        {
            foreach (var node in layout.Nodes)
            {
                if (AngleInRange(hitAngle, node.StartAngle, node.EndAngle))
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
            float tolerance = ctx.Theme?.HitTestAngleTolerance ?? 0.2f;
            if ((AngleDistance(hitAngle, sMid) < tolerance || AngleDistance(hitAngle, tMid) < tolerance)
                && dist < innerR * 0.9f)
            {
                var row = ctx.Data[chord.RowIndex];
                return new HitResult
                {
                    Hit = true,
                    Row = row,
                    RowIndex = chord.RowIndex,
                    ScreenX = cxP,
                    ScreenY = cyP,
                    Label = $"{chord.Source} → {chord.Target}: {chord.Value:G4}",
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

    private static bool AngleInRange(float angle, float start, float end)
    {
        if (start <= end)
            return angle >= start && angle <= end;
        return angle >= start || angle <= end;
    }

    private ChordLayout? BuildChordLayout(MarkContext ctx)
    {
        var nodeNames = new List<string>();
        var nodeSet   = new HashSet<string>();
        var flows     = new List<(string src, string tgt, double val, int rowIdx)>();

        for (int i = 0; i < ctx.Data.Count; i++)
        {
            var row = ctx.Data[i];
            if (IsSeriesHidden(ctx, row)) continue;
            if (!row.Has(SourceField) || !row.Has(TargetField)) continue;
            string src = row.Get<object>(SourceField).ToString() ?? "";
            string tgt = row.Get<object>(TargetField).ToString() ?? "";
            var yRaw = ctx.Encodes.Resolve(YChannel, row);
            double val = yRaw != null ? ToDouble(yRaw, "Y") : 1;
            if (val <= 0) continue;

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

        float totalGap = ArcGap * nodeNames.Count;
        float available = MathF.PI * 2f - totalGap;

        // Assign arc angles
        var nodeArcs = new List<ChordNode>();
        var nodeAngleStarts = new Dictionary<string, float>();
        var nodeAngleEnds   = new Dictionary<string, float>();
        var palette = ctx.Theme?.Palette ?? ChartTheme.DefaultPalette;

        float angle = 0;
        for (int i = 0; i < nodeNames.Count; i++)
        {
            string name = nodeNames[i];
            float sweep = (float)(nodeTotals[name] / grandTotal * available);
            nodeAngleStarts[name] = angle;
            nodeAngleEnds[name]   = angle + sweep;
            nodeArcs.Add(new ChordNode
            {
                Label = name, StartAngle = angle, EndAngle = angle + sweep,
                Color = palette[i % palette.Length],
            });
            angle += sweep + ArcGap;
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

    private class ChordNode
    {
        public string Label { get; init; } = "";
        public float StartAngle { get; init; }
        public float EndAngle { get; init; }
        public Color Color { get; init; }
    }

    private class ChordData
    {
        public string Source { get; init; } = "";
        public string Target { get; init; } = "";
        public double Value { get; init; }
        public int RowIndex { get; init; }
        public float SourceStartAngle { get; init; }
        public float SourceEndAngle { get; init; }
        public float TargetStartAngle { get; init; }
        public float TargetEndAngle { get; init; }
    }

    private class ChordLayout
    {
        public List<ChordNode> Nodes { get; init; } = new();
        public List<ChordData> Chords { get; init; } = new();
    }
}