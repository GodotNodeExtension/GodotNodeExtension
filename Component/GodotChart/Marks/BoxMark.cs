using System;
using System.Collections.Generic;
using Godot;

namespace GodotNodeExtension.Component.GodotChart.Marks;

/// <summary>
/// Box plot mark. Renders box-and-whisker diagrams showing data distribution.
/// Requires fields: X = category, plus five statistical fields configured via properties:
/// MinField, Q1Field, MedianField, Q3Field, MaxField.
/// Animation: boxes grow from median outward.
/// </summary>
public class BoxMark : Mark
{
    /// <inheritdoc />
    public override MarkCoordinate Coordinate => MarkCoordinate.Cartesian;

    /// <summary>Field name for the minimum (lower whisker) value.</summary>
    public string MinField { get; set; } = "min";

    /// <summary>Field name for the first quartile (Q1 / 25th percentile) value.</summary>
    public string Q1Field { get; set; } = "q1";

    /// <summary>Field name for the median (Q2 / 50th percentile) value.</summary>
    public string MedianField { get; set; } = "median";

    /// <summary>Field name for the third quartile (Q3 / 75th percentile) value.</summary>
    public string Q3Field { get; set; } = "q3";

    /// <summary>Field name for the maximum (upper whisker) value.</summary>
    public string MaxField { get; set; } = "max";

    /// <summary>Width ratio of the box relative to the slot [0, 1].</summary>
    public float BoxWidthRatio { get; set; } = 0.5f;

    /// <summary>Width of the whisker lines in pixels.</summary>
    public float WhiskerWidth { get; set; } = 1.5f;

    /// <summary>Corner radius for the box rect.</summary>
    public float CornerRadius { get; set; } = 2f;

    /// <summary>Default box fill color. Null = use theme color.</summary>
    public Color? BoxColor { get; set; }

    /// <summary>Median line and whisker color. Null = use theme color.</summary>
    public Color? LineColor { get; set; }

    /// <summary>Resolve box fill color considering theme fallback.</summary>
    private Color ResolveBoxColor(MarkContext ctx) =>
        BoxColor ?? ctx.Theme?.BoxFillColor ?? new Color(0.29f, 0.59f, 0.98f, 0.6f);

    /// <summary>Resolve line color considering theme fallback.</summary>
    private Color ResolveLineColor(MarkContext ctx) =>
        LineColor ?? ctx.Theme?.BoxLineColor ?? new Color(1f, 1f, 1f, 0.9f);

    /// <inheritdoc />
    public override void ContributeScales(ScaleSet scales, EncodeSet encodes, List<DataRow> data)
    {
        ScaleContributionHelper.ContributeMinMaxScale(scales, data, MinField, MaxField, YChannel);
    }

    public override void Render(MarkContext ctx)
    {
        var xScale = ctx.Scales.TryGet(Channel.X) as OrdinalScale;
        var yScale = ctx.Scales.TryGet(YChannel) as LinearScale;
        if (xScale == null || yScale == null) return;
        if (xScale.Domain.Count == 0) return;

        float slotW = ctx.Plot.Width / xScale.Domain.Count;
        float boxW  = slotW * BoxWidthRatio;
        float anim  = ComputeAnimProgress(ctx);

        for (int i = 0; i < ctx.Data.Count; i++)
        {
            var row = ctx.Data[i];
            if (IsSeriesHidden(ctx, row)) continue;
            var xRaw = ctx.Encodes.Resolve(Channel.X, row);
            if (xRaw == null) continue;
            if (!row.Has(MinField) || !row.Has(Q1Field) || !row.Has(MedianField) ||
                !row.Has(Q3Field) || !row.Has(MaxField)) continue;

            double vMin    = GetDouble(row, MinField);
            double vQ1     = GetDouble(row, Q1Field);
            double vMedian = GetDouble(row, MedianField);
            double vQ3     = GetDouble(row, Q3Field);
            double vMax    = GetDouble(row, MaxField);

            float xNorm = (float)xScale.Map(xRaw);
            float cx = ctx.Plot.MapX(xNorm);

            // Map Y values — animate from median outward
            float medianY = ctx.Plot.MapY((float)yScale.Map(vMedian));
            float q1Y  = medianY + (ctx.Plot.MapY((float)yScale.Map(vQ1)) - medianY) * anim;
            float q3Y  = medianY + (ctx.Plot.MapY((float)yScale.Map(vQ3)) - medianY) * anim;
            float minY = medianY + (ctx.Plot.MapY((float)yScale.Map(vMin)) - medianY) * anim;
            float maxY = medianY + (ctx.Plot.MapY((float)yScale.Map(vMax)) - medianY) * anim;

            var color = ResolveColorWithOverride(ctx, row, i, ResolveBoxColor(ctx));
            float opacity = ComputeEffectiveOpacity(ctx, row);

            bool isHovered = i == ctx.HoveredRowIndex;
            if (isHovered) color = BrightenColor(color, GetHoverBrighten(ctx));

            float halfBox = boxW / 2f;

            // Box (Q1 to Q3)
            using (var boxPath = ctx.Canvas.CreatePath())
            using (var boxPaint = ctx.Canvas.CreatePaint())
            {
                float boxTop = Math.Min(q1Y, q3Y);
                float boxH   = Math.Abs(q3Y - q1Y);
                if (CornerRadius > 0)
                    boxPath.RoundRect(cx - halfBox, boxTop, boxW, boxH, CornerRadius);
                else
                    boxPath.Rect(cx - halfBox, boxTop, boxW, boxH);
                boxPaint.SetColor(color).SetAntiAlias(true).SetOpacity(opacity);
                ctx.Canvas.Fill(boxPath, boxPaint);
            }

            using var linePaint = ctx.Canvas.CreatePaint();
            linePaint.SetColor(ResolveLineColor(ctx)).SetStrokeWidth(WhiskerWidth).SetAntiAlias(true).SetOpacity(opacity);

            // Median line
            using (var medPath = ctx.Canvas.CreatePath())
            {
                medPath.MoveTo(cx - halfBox, medianY);
                medPath.LineTo(cx + halfBox, medianY);
                ctx.Canvas.Stroke(medPath, linePaint);
            }

            // Upper whisker (Q3 to Max)
            using (var upPath = ctx.Canvas.CreatePath())
            {
                upPath.MoveTo(cx, q3Y);
                upPath.LineTo(cx, maxY);
                upPath.MoveTo(cx - halfBox * 0.5f, maxY);
                upPath.LineTo(cx + halfBox * 0.5f, maxY);
                ctx.Canvas.Stroke(upPath, linePaint);
            }

            // Lower whisker (Q1 to Min)
            using (var downPath = ctx.Canvas.CreatePath())
            {
                downPath.MoveTo(cx, q1Y);
                downPath.LineTo(cx, minY);
                downPath.MoveTo(cx - halfBox * 0.5f, minY);
                downPath.LineTo(cx + halfBox * 0.5f, minY);
                ctx.Canvas.Stroke(downPath, linePaint);
            }

            // Selection ring
            if (i == ctx.SelectedRowIndex)
            {
                using var selPath  = ctx.Canvas.CreatePath();
                using var selPaint = ctx.Canvas.CreatePaint();
                float boxTop = Math.Min(q1Y, q3Y);
                float boxH   = Math.Abs(q3Y - q1Y);
                if (CornerRadius > 0)
                    selPath.RoundRect(cx - halfBox, boxTop, boxW, boxH, CornerRadius);
                else
                    selPath.Rect(cx - halfBox, boxTop, boxW, boxH);
                ApplySelectionPaint(ctx, selPaint, opacity);
                ctx.Canvas.Stroke(selPath, selPaint);
            }
        }
    }

    public override HitResult? HitTest(MarkContext ctx, Vector2 pos)
    {
        var xScale = ctx.Scales.TryGet(Channel.X) as OrdinalScale;
        var yScale = ctx.Scales.TryGet(YChannel) as LinearScale;
        if (xScale == null || yScale == null) return null;
        if (xScale.Domain.Count == 0) return null;

        float slotW = ctx.Plot.Width / xScale.Domain.Count;
        float boxW  = slotW * BoxWidthRatio;
        float anim  = ComputeAnimProgress(ctx);

        for (int i = 0; i < ctx.Data.Count; i++)
        {
            var row = ctx.Data[i];
            if (IsSeriesHidden(ctx, row)) continue;
            if (!row.Has(MinField) || !row.Has(MaxField) || !row.Has(MedianField)) continue;
            var xRaw = ctx.Encodes.Resolve(Channel.X, row);
            if (xRaw == null) continue;

            double vMin = GetDouble(row, MinField);
            double vMax = GetDouble(row, MaxField);
            double vMedian = GetDouble(row, MedianField);

            float xNorm = (float)xScale.Map(xRaw);
            float cx = ctx.Plot.MapX(xNorm);
            // Apply animation: expand from median outward, consistent with Render
            float medianY = ctx.Plot.MapY((float)yScale.Map(vMedian));
            float minY = medianY + (ctx.Plot.MapY((float)yScale.Map(vMin)) - medianY) * anim;
            float maxY = medianY + (ctx.Plot.MapY((float)yScale.Map(vMax)) - medianY) * anim;
            float top = Math.Min(minY, maxY);
            float bottom = Math.Max(minY, maxY);

            if (pos.X >= cx - boxW / 2f && pos.X <= cx + boxW / 2f &&
                pos.Y >= top && pos.Y <= bottom)
            {
                return new HitResult
                {
                    Hit = true, Row = row, RowIndex = i,
                    ScreenX = cx, ScreenY = medianY,
                    Label = $"{xRaw}: Med={yScale.Format(vMedian)}",
                    MarkType = nameof(BoxMark),
                };
            }
        }
        return null;
    }
}
