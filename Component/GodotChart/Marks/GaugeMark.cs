using System;
using Godot;
using GodotNodeExtension.Component.GodotChart.Canvas;

namespace GodotNodeExtension.Component.GodotChart.Marks;

/// <summary>
/// Gauge chart mark. Renders an arc indicator with optional center label.
/// Encodes: Y = value (mapped to arc sweep), Color = optional category for color.
/// Useful for progress rings, speedometers, and single-value indicators.
/// Animation: arc sweeps from zero to target angle.
/// Classified as a polar-only mark; no Cartesian grid is drawn.
/// NOTE: only the first data row is rendered. Multiple rows are ignored.
/// </summary>
public class GaugeMark : Mark
{
    /// <inheritdoc />
    public override MarkCoordinate Coordinate => MarkCoordinate.Polar;

    /// <summary>Start angle in degrees. Default is -210 (roughly 7 o'clock position).</summary>
    public float StartAngleDeg { get; set; } = -210f;

    /// <summary>End angle in degrees. Default is 30 (roughly 1 o'clock position).</summary>
    public float EndAngleDeg { get; set; } = 30f;

    /// <summary>Arc track width relative to the outer radius.</summary>
    public float ArcWidth { get; set; } = 0.12f;

    /// <summary>Background arc color (the track). Null = use theme color.</summary>
    public Color? TrackColor { get; set; }

    /// <summary>Default value arc color.</summary>
    public Color ValueColor { get; set; } = new(0.29f, 0.59f, 0.98f);

    /// <summary>Resolve track color considering theme fallback.</summary>
    private Color ResolveTrackColor(MarkContext ctx) =>
        TrackColor ?? ctx.Theme?.GaugeTrackColor ?? new Color(1f, 1f, 1f, 0.08f);

    /// <summary>Whether to show the value text at the center.</summary>
    public bool ShowCenterLabel { get; set; } = true;

    /// <summary>Whether to show min/max labels at the arc endpoints.</summary>
    public bool ShowMinMaxLabels { get; set; } = true;

    /// <summary>Inner radius ratio for donut-style gauge [0, 1). 0 = use ArcWidth from outer.</summary>
    public float InnerRadiusRatio { get; set; } = 0f;

    /// <summary>Outer radius as ratio of min(width, height)/2. Range (0, 1].</summary>
    public float RadiusFactor { get; set; } = 0.85f;

    private (float startRad, float totalSweep, float outerR, float innerR) ComputeArcParams(MarkContext ctx)
    {
        float maxR = MathF.Min(ctx.Plot.Width, ctx.Plot.Height) / 2f * RadiusFactor;
        float startRad = StartAngleDeg * MathF.PI / 180f;
        float endRad   = EndAngleDeg   * MathF.PI / 180f;
        float totalSweep = endRad - startRad;
        if (totalSweep < 0) totalSweep += MathF.Tau;
        float outerR = maxR;
        float innerR = InnerRadiusRatio > 0 ? outerR * InnerRadiusRatio : outerR * (1f - ArcWidth);
        return (startRad, totalSweep, outerR, innerR);
    }

    public override void Render(MarkContext ctx)
    {
        var yScale = ctx.Scales.TryGet(YChannel) as LinearScale;
        if (yScale == null || ctx.Data.Count == 0) return;

        float cx = ctx.Plot.X + ctx.Plot.Width / 2f;
        float cy = ctx.Plot.Y + ctx.Plot.Height / 2f;
        float anim = ComputeAnimProgress(ctx);

        var (startRad, totalSweep, outerR, innerR) = ComputeArcParams(ctx);

        // Draw background track arc
        DrawArcBand(ctx, cx, cy, outerR, innerR, startRad, startRad + totalSweep,
                    ResolveTrackColor(ctx), ctx.Animation.GlobalOpacity);

        // Render only the first data row (gauge is a single-value mark)
        for (int i = 0; i < Math.Min(ctx.Data.Count, 1); i++)
        {
            var row  = ctx.Data[i];
            if (IsSeriesHidden(ctx, row)) continue;
            var yRaw = ctx.Encodes.Resolve(YChannel, row);
            if (yRaw == null) continue;

            float norm = (float)yScale.Map(yRaw);
            float sweepTarget = totalSweep * norm;
            float sweepAnimated = sweepTarget * anim;

            var color = ResolveColorWithOverride(ctx, row, i, ValueColor);
            float opacity = ComputeEffectiveOpacity(ctx, row);

            bool isHovered = i == ctx.HoveredRowIndex;
            if (isHovered) color = BrightenColor(color, GetHoverBrighten(ctx));

            float valueEndRad = startRad + sweepAnimated;

            DrawArcBand(ctx, cx, cy, outerR, innerR, startRad, valueEndRad,
                        color, opacity);

            if (ShowCenterLabel && i == 0)
            {
                using var labelPaint = ctx.Canvas.CreatePaint();
                var glc = ctx.Theme?.GaugeLabelColor ?? new Color(1f, 1f, 1f, 0.9f);
                labelPaint.SetColor(glc with { A = glc.A * ctx.Animation.GlobalOpacity });
                string text = yScale.Format(yRaw);
                ctx.Canvas.DrawText(text, cx, cy, FontSettings.Default, labelPaint);
            }
        }

        // Min/Max labels
        if (ShowMinMaxLabels)
        {
            float halfDim = MathF.Min(ctx.Plot.Width, ctx.Plot.Height) / 2f;
            float availableMargin = halfDim - outerR;
            float labelOffset = MathF.Min(ctx.Theme?.GaugeLabelOffset ?? 12f, availableMargin * 0.8f);
            float labelR = outerR + labelOffset;
            using var minPaint = ctx.Canvas.CreatePaint();
            var mmlc = ctx.Theme?.GaugeMinMaxLabelColor ?? new Color(1f, 1f, 1f, 0.4f);
            minPaint.SetColor(mmlc with { A = mmlc.A * ctx.Animation.GlobalOpacity });
            ctx.Canvas.DrawText(yScale.Format(yScale.Min),
                cx + MathF.Cos(startRad) * labelR,
                cy + MathF.Sin(startRad) * labelR,
                FontSettings.Default, minPaint);

            float arcEnd = startRad + totalSweep;
            ctx.Canvas.DrawText(yScale.Format(yScale.Max),
                cx + MathF.Cos(arcEnd) * labelR,
                cy + MathF.Sin(arcEnd) * labelR,
                FontSettings.Default, minPaint);
        }
    }

    private static void DrawArcBand(MarkContext ctx,
        float cx, float cy, float outerR, float innerR,
        float startAngle, float endAngle, Color color, float opacity)
    {
        using var path  = ctx.Canvas.CreatePath();
        using var paint = ctx.Canvas.CreatePaint();

        // Outer arc
        float osx = cx + MathF.Cos(startAngle) * outerR;
        float osy = cy + MathF.Sin(startAngle) * outerR;
        path.MoveTo(osx, osy);
        path.ArcTo(cx, cy, outerR, startAngle, endAngle);

        // Inner arc reversed
        path.ArcTo(cx, cy, innerR, endAngle, startAngle, clockwise: true);
        path.Close();

        paint.SetColor(color).SetAntiAlias(true).SetOpacity(opacity);
        ctx.Canvas.Fill(path, paint);
    }

    public override HitResult? HitTest(MarkContext ctx, Vector2 pos)
    {
        var yScale = ctx.Scales.TryGet(YChannel) as LinearScale;
        if (yScale == null || ctx.Data.Count == 0) return null;

        float cx = ctx.Plot.X + ctx.Plot.Width / 2f;
        float cy = ctx.Plot.Y + ctx.Plot.Height / 2f;

        var (startRad, totalSweep, outerR, innerR) = ComputeArcParams(ctx);

        float dx = pos.X - cx;
        float dy = pos.Y - cy;
        float dist = MathF.Sqrt(dx * dx + dy * dy);
        if (dist < innerR || dist > outerR) return null;

        float angle = MathF.Atan2(dy, dx);
        float relAngle = ((angle - startRad) % MathF.Tau + MathF.Tau) % MathF.Tau;
        if (relAngle > totalSweep) return null;

        // Only test the first data row (gauge is a single-value mark)
        for (int i = 0; i < Math.Min(ctx.Data.Count, 1); i++)
        {
            var row  = ctx.Data[i];
            if (IsSeriesHidden(ctx, row)) continue;
            var yRaw = ctx.Encodes.Resolve(YChannel, row);
            if (yRaw == null) continue;

            float norm = (float)yScale.Map(yRaw);
            float valueSweep = totalSweep * norm;

            if (relAngle <= valueSweep)
            {
                return new HitResult
                {
                    Hit = true, Row = row, RowIndex = i,
                    ScreenX = cx, ScreenY = cy,
                    Label = $"{yScale.Format(yRaw)}",
                    MarkType = nameof(GaugeMark),
                };
            }
        }
        return null;
    }
}
