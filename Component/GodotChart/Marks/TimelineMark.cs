using Godot;
using GodotNodeExtension.Component.GodotChart.Canvas;

namespace GodotNodeExtension.Component.GodotChart.Marks;

/// <summary>
/// Timeline mark. Renders horizontal colored bars on a vertical category axis.
/// Useful for Buff/Debuff timelines, Gantt charts, and combat log visualization.
/// Encodes: Y = category (OrdinalScale), Color = optional series.
/// Additional fields: StartField, EndField for bar range on X axis (LinearScale).
/// </summary>
public class TimelineMark : Mark
{
    /// <inheritdoc />
    public override MarkCoordinate Coordinate => MarkCoordinate.Cartesian;

    /// <summary>Field name for the start value (left edge of bar).</summary>
    public string StartField { get; set; } = "start";

    /// <summary>Field name for the end value (right edge of bar).</summary>
    public string EndField { get; set; } = "end";

    /// <summary>Bar height relative to the slot [0, 1].</summary>
    public float BarHeightRatio { get; set; } = 0.6f;

    /// <summary>Corner radius for each bar.</summary>
    public float CornerRadius { get; set; } = 3f;

    /// <summary>Whether to show labels inside bars.</summary>
    public override bool ShowLabel { get; set; } = false;

    private (OrdinalScale yScale, LinearScale xScale, float barH)?
        ResolveScalesAndBar(MarkContext ctx)
    {
        var yScale = ctx.Scales.TryGet(YChannel) as OrdinalScale
                  ?? ctx.Scales.TryGet(Channel.X) as OrdinalScale;
        var xScale = ctx.Scales.TryGet(Channel.X) as LinearScale
                  ?? ctx.Scales.TryGet(YChannel) as LinearScale;
        if (yScale == null || xScale == null || yScale.Domain.Count == 0) return null;
        float slotH = ctx.Plot.Height / yScale.Domain.Count;
        float barH  = slotH * BarHeightRatio;
        return (yScale, xScale, barH);
    }

    public override void Render(MarkContext ctx)
    {
        var resolved = ResolveScalesAndBar(ctx);
        if (resolved == null) return;
        var (yScale, xScale, barH) = resolved.Value;
        float anim  = ComputeAnimProgress(ctx);

        for (int i = 0; i < ctx.Data.Count; i++)
        {
            var row = ctx.Data[i];
            if (IsSeriesHidden(ctx, row)) continue;
            var catRaw   = ctx.Encodes.Resolve(YChannel, row)
                        ?? ctx.Encodes.Resolve(Channel.X, row);
            if (catRaw == null) continue;
            if (!row.TryGet<object>(StartField, out var startRaw) ||
                !row.TryGet<object>(EndField, out var endRaw)) continue;

            float yNorm   = (float)yScale.Map(catRaw);
            float startX  = ctx.Plot.MapX((float)xScale.Map(startRaw));
            float endX    = ctx.Plot.MapX((float)xScale.Map(endRaw));
            float barW    = (endX - startX) * anim;
            if (barW <= 0) continue;

            float py = ctx.Plot.MapY(yNorm) - barH / 2f;

            var color = ResolveColorWithOverride(ctx, row, i, GetDefaultColor(ctx));
            float opacity = ComputeEffectiveOpacity(ctx, row);

            bool isHovered = i == ctx.HoveredRowIndex;
            if (isHovered) color = BrightenColor(color, GetHoverBrighten(ctx));

            using var path  = ctx.Canvas.CreatePath();
            using var paint = ctx.Canvas.CreatePaint();

            if (CornerRadius > 0)
                path.RoundRect(startX, py, barW, barH, CornerRadius);
            else
                path.Rect(startX, py, barW, barH);

            paint.SetColor(color).SetAntiAlias(true).SetOpacity(opacity);
            ctx.Canvas.Fill(path, paint);

            if (i == ctx.SelectedRowIndex)
            {
                using var selPaint = ctx.Canvas.CreatePaint();
                ApplySelectionPaint(ctx, selPaint, opacity);
                ctx.Canvas.Stroke(path, selPaint);
            }

            if (ShowLabel)
            {
                using var labelPaint = ctx.Canvas.CreatePaint();
                labelPaint.SetColor(GetDataLabelColor(ctx)).SetOpacity(opacity);
                string label = ctx.Encodes.Resolve(Channel.Label, row)?.ToString()
                             ?? catRaw.ToString() ?? "";
                ctx.Canvas.DrawText(label, startX + barW / 2f, py + barH / 2f, FontSettings.Default, labelPaint);
            }
        }
    }

    public override HitResult? HitTest(MarkContext ctx, Vector2 pos)
    {
        var resolved = ResolveScalesAndBar(ctx);
        if (resolved == null) return null;
        var (yScale, xScale, barH) = resolved.Value;

        for (int i = 0; i < ctx.Data.Count; i++)
        {
            var row = ctx.Data[i];
            if (IsSeriesHidden(ctx, row)) continue;
            var catRaw   = ctx.Encodes.Resolve(YChannel, row)
                        ?? ctx.Encodes.Resolve(Channel.X, row);
            if (catRaw == null) continue;
            if (!row.TryGet<object>(StartField, out var startRaw) ||
                !row.TryGet<object>(EndField, out var endRaw)) continue;

            float yNorm  = (float)yScale.Map(catRaw);
            float startX = ctx.Plot.MapX((float)xScale.Map(startRaw));
            float endX   = ctx.Plot.MapX((float)xScale.Map(endRaw));
            float py     = ctx.Plot.MapY(yNorm) - barH / 2f;

            if (pos.X >= startX && pos.X <= endX &&
                pos.Y >= py && pos.Y <= py + barH)
            {
                return new HitResult
                {
                    Hit = true, Row = row, RowIndex = i,
                    ScreenX = (startX + endX) / 2f, ScreenY = py + barH / 2f,
                    Label = $"{catRaw}: {startRaw}–{endRaw}",
                    MarkType = nameof(TimelineMark),
                };
            }
        }
        return null;
    }
}
