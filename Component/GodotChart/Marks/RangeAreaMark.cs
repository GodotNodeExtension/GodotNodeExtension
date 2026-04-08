using System;
using System.Collections.Generic;
using Godot;

namespace GodotNodeExtension.Component.GodotChart.Marks;

/// <summary>
/// Range area mark. Renders a filled band between upper and lower value bounds.
/// Encodes: X = category/time, Y = upper bound value.
/// Additional field: LowerField for the lower bound value.
/// Animation: band grows from center line outward.
/// </summary>
public class RangeAreaMark : Mark
{
    /// <inheritdoc />
    public override MarkCoordinate Coordinate => MarkCoordinate.Cartesian;

    /// <summary>Field name for the lower bound value.</summary>
    public string LowerField { get; set; } = "lower";

    /// <summary>Fill opacity for the range band.</summary>
    public float FillOpacity { get; set; } = 0.3f;

    /// <summary>Whether to draw border lines on upper/lower edges.</summary>
    public bool ShowBorderLines { get; set; } = true;

    /// <summary>Border line stroke width.</summary>
    public float StrokeWidth { get; set; } = 1.5f;

    /// <summary>Whether to use smooth (cubic) curves.</summary>
    public bool Smooth { get; set; } = false;

    public override void Render(MarkContext ctx)
    {
        if (ctx.Data.Count < 2) return;

        var xScale = ctx.Scales.TryGet(Channel.X);
        var yScale = ctx.Scales.TryGet(YChannel) as LinearScale;
        if (xScale == null || yScale == null) return;

        float anim = ComputeAnimProgress(ctx);
        var color = ResolveColorWithOverride(ctx, ctx.Data[0], 0, GetDefaultColor(ctx));
        float opacity = ctx.Animation.GlobalOpacity;

        // Collect upper and lower points with global index mapping
        var upperPts = new List<(float x, float y)>();
        var lowerPts = new List<(float x, float y)>();
        var pointIndices = new List<int>();

        for (int i = 0; i < ctx.Data.Count; i++)
        {
            var row = ctx.Data[i];
            if (IsSeriesHidden(ctx, row)) continue;
            var xRaw = ctx.Encodes.Resolve(Channel.X, row);
            var yRaw = ctx.Encodes.Resolve(YChannel, row);
            if (xRaw == null || yRaw == null) continue;
            if (!row.TryGet<object>(LowerField, out var lRaw)) continue;

            float xNorm    = (float)xScale.Map(xRaw);
            float upperNorm = (float)yScale.Map(yRaw);
            float lowerNorm = (float)yScale.Map(lRaw);

            float px = ctx.Plot.MapX(xNorm);
            float rawUpperY = ctx.Plot.MapY(upperNorm);
            float rawLowerY = ctx.Plot.MapY(lowerNorm);
            // Ensure upper is visually above lower (smaller Y in screen coords)
            float topY = Math.Min(rawUpperY, rawLowerY);
            float botY = Math.Max(rawUpperY, rawLowerY);
            float midY = (topY + botY) / 2f;
            float upperY = midY + (topY - midY) * anim;
            float lowerY = midY + (botY - midY) * anim;

            upperPts.Add((px, upperY));
            lowerPts.Add((px, lowerY));
            pointIndices.Add(i);

        }

        if (upperPts.Count < 2) return;

        // Fill: upper line forward, then lower line reversed → closed shape
        using (var fillPath = ctx.Canvas.CreatePath())
        using (var fillPaint = ctx.Canvas.CreatePaint())
        {
            // Upper edge
            fillPath.MoveTo(upperPts[0].x, upperPts[0].y);
            if (Smooth)
                for (int i = 1; i < upperPts.Count; i++)
                {
                    float mx = (upperPts[i - 1].x + upperPts[i].x) / 2f;
                    fillPath.CubicTo(mx, upperPts[i - 1].y, mx, upperPts[i].y,
                                     upperPts[i].x, upperPts[i].y);
                }
            else
                for (int i = 1; i < upperPts.Count; i++)
                    fillPath.LineTo(upperPts[i].x, upperPts[i].y);

            // Lower edge reversed
            for (int i = lowerPts.Count - 1; i >= 0; i--)
            {
                if (i == lowerPts.Count - 1)
                    fillPath.LineTo(lowerPts[i].x, lowerPts[i].y);
                else if (Smooth)
                {
                    float mx = (lowerPts[i + 1].x + lowerPts[i].x) / 2f;
                    fillPath.CubicTo(mx, lowerPts[i + 1].y, mx, lowerPts[i].y,
                                     lowerPts[i].x, lowerPts[i].y);
                }
                else
                    fillPath.LineTo(lowerPts[i].x, lowerPts[i].y);
            }
            fillPath.Close();

            fillPaint.SetColor(color).SetAntiAlias(true).SetOpacity(FillOpacity * opacity);
            ctx.Canvas.Fill(fillPath, fillPaint);
        }

        // Border lines
        if (ShowBorderLines)
        {
            using var strokePaint = ctx.Canvas.CreatePaint();
            strokePaint.SetColor(color).SetStrokeWidth(StrokeWidth).SetAntiAlias(true).SetOpacity(opacity);

            void DrawEdge(List<(float x, float y)> pts)
            {
                using var edgePath = ctx.Canvas.CreatePath();
                edgePath.MoveTo(pts[0].x, pts[0].y);
                if (Smooth)
                    for (int i = 1; i < pts.Count; i++)
                    {
                        float mx = (pts[i - 1].x + pts[i].x) / 2f;
                        edgePath.CubicTo(mx, pts[i - 1].y, mx, pts[i].y, pts[i].x, pts[i].y);
                    }
                else
                    for (int i = 1; i < pts.Count; i++)
                        edgePath.LineTo(pts[i].x, pts[i].y);
                ctx.Canvas.Stroke(edgePath, strokePaint);
            }

            DrawEdge(upperPts);
            DrawEdge(lowerPts);
        }

        // Hover highlight: draw points at hovered index using global-to-point index mapping
        if (ctx.HoveredRowIndex >= 0)
        {
            int ptIdx = pointIndices.IndexOf(ctx.HoveredRowIndex);
            if (ptIdx >= 0 && ptIdx < upperPts.Count)
            {
                using var ptPath = ctx.Canvas.CreatePath();
                using var ptPaint = ctx.Canvas.CreatePaint();
                ptPath.Circle(upperPts[ptIdx].x, upperPts[ptIdx].y, ctx.Theme?.RangeAreaPointRadius ?? 4f);
                ptPath.Circle(lowerPts[ptIdx].x, lowerPts[ptIdx].y, ctx.Theme?.RangeAreaPointRadius ?? 4f);
                ptPaint.SetColor(color).SetAntiAlias(true).SetOpacity(opacity);
                ctx.Canvas.Fill(ptPath, ptPaint);
            }
        }
    }

    public override HitResult? HitTest(MarkContext ctx, Vector2 pos)
    {
        if (ctx.Data.Count < 2) return null;

        var xScale = ctx.Scales.TryGet(Channel.X);
        var yScale = ctx.Scales.TryGet(YChannel) as LinearScale;
        if (xScale == null || yScale == null) return null;

        float bestDist = ctx.Theme?.HitTestAreaSnapDistance ?? 20f; // snap distance
        int bestIdx = -1;

        for (int i = 0; i < ctx.Data.Count; i++)
        {
            var row  = ctx.Data[i];
            if (IsSeriesHidden(ctx, row)) continue;
            var xRaw = ctx.Encodes.Resolve(Channel.X, row);
            if (xRaw == null) continue;

            float xNorm = (float)xScale.Map(xRaw);
            float px = ctx.Plot.MapX(xNorm);
            float dx = Math.Abs(pos.X - px);
            if (dx < bestDist)
            {
                bestDist = dx;
                bestIdx = i;
            }
        }

        if (bestIdx < 0) return null;

        var hitRow = ctx.Data[bestIdx];
        var hitX   = ctx.Encodes.Resolve(Channel.X, hitRow);
        var hitY   = ctx.Encodes.Resolve(YChannel, hitRow);
        if (hitX == null || hitY == null) return null;
        if (!hitRow.TryGet<object>(LowerField, out var hitL)) return null;

        // Verify Y position is within upper-lower band
        float upperY = ctx.Plot.MapY((float)yScale.Map(hitY));
        float lowerY = ctx.Plot.MapY((float)yScale.Map(hitL));
        float bandTop = Math.Min(upperY, lowerY);
        float bandBot = Math.Max(upperY, lowerY);
        if (pos.Y < bandTop || pos.Y > bandBot) return null;

        return new HitResult
        {
            Hit = true, Row = hitRow, RowIndex = bestIdx,
            ScreenX = ctx.Plot.MapX((float)xScale.Map(hitX)),
            ScreenY = pos.Y,
            Label = $"{hitX}: {hitL}–{hitY}",
            MarkType = nameof(RangeAreaMark),
        };
    }
}
