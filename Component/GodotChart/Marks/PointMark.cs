using System.Collections.Generic;
using Godot;

namespace GodotNodeExtension.Component.GodotChart.Marks;

/// <summary>
/// Scatter/bubble chart mark.
/// Animation: points scale from zero radius to target.
/// </summary>
public class PointMark : Mark
{
    /// <inheritdoc />
    public override MarkCoordinate Coordinate => MarkCoordinate.Cartesian;

    /// <summary>Default point radius in pixels when no Size channel is mapped.</summary>
    public float DefaultRadius { get; set; } = 5f;

    public override void Render(MarkContext ctx)
    {
        var xScale = ctx.Scales.Get(Channel.X);
        var yScale = ctx.Scales.Get(YChannel);
        var sScale = ctx.Scales.TryGet(Channel.Size);
        float anim = ComputeAnimProgress(ctx);

        List<LabelElement>? labels = ShowLabel ? new List<LabelElement>() : null;

        for (int i = 0; i < ctx.Data.Count; i++)
        {
            var row   = ctx.Data[i];
            if (IsSeriesHidden(ctx, row)) continue;
            var xRaw  = ctx.Encodes.Resolve(Channel.X, row);
            var yRaw  = ctx.Encodes.Resolve(YChannel, row);
            if (xRaw == null || yRaw == null) continue;
            var color = ResolveColorWithOverride(ctx, row, i, GetDefaultColor(ctx));
            var alpha = ResolveOpacity(ctx, row, ctx.Theme?.PointDefaultOpacity ?? 0.8f);
            float opacity = ComputeEffectiveOpacity(ctx, row, alpha);

            bool isHovered  = i == ctx.HoveredRowIndex;
            bool isSelected = i == ctx.SelectedRowIndex;

            float px = ctx.Plot.MapX(xScale.Map(xRaw));
            float py = ctx.Plot.MapY(yScale.Map(yRaw));
            float r  = DefaultRadius;

            if (sScale != null && ctx.Encodes.Has(Channel.Size))
            {
                var sRaw = ctx.Encodes.Resolve(Channel.Size, row);
                if (sRaw != null)
                    r = (ctx.Theme?.PointSizeMin ?? 3f)
                      + (float)sScale.Map(sRaw) * (ctx.Theme?.PointSizeRange ?? 20f);
            }

            // Animation: scale radius
            r *= anim;

            // Hover: enlarge and brighten
            if (isHovered)
            {
                r *= (ctx.Theme?.PointHoverRadiusRatio ?? 1.3f) * ctx.Animation.HoverScale;
                color = BrightenColor(color, GetHoverBrighten(ctx));
            }

            using var path  = ctx.Canvas.CreatePath();
            using var paint = ctx.Canvas.CreatePaint();
            path.Circle(px, py, r);
            paint.SetColor(color).SetAntiAlias(true).SetOpacity(opacity);
            ctx.Canvas.Fill(path, paint);

            // Selected: draw highlight ring
            if (isSelected)
            {
                using var strokePaint = ctx.Canvas.CreatePaint();
                ApplySelectionPaint(ctx, strokePaint, opacity);
                ctx.Canvas.Stroke(path, strokePaint);
            }

            // Collect label element
            if (labels != null)
            {
                string text = string.Format(LabelFormat, yRaw, xRaw);
                labels.Add(new LabelElement(px, py - r, text, opacity));
            }
        }

        if (labels != null)
            DrawLabels(ctx, labels);
    }

    public override HitResult? HitTest(MarkContext ctx, Vector2 pos)
    {
        var xScale = ctx.Scales.Get(Channel.X);
        var yScale = ctx.Scales.Get(YChannel);
        var sScale = ctx.Scales.TryGet(Channel.Size);

        for (int i = 0; i < ctx.Data.Count; i++)
        {
            var row  = ctx.Data[i];
            if (IsSeriesHidden(ctx, row)) continue;
            var xRaw = ctx.Encodes.Resolve(Channel.X, row);
            var yRaw = ctx.Encodes.Resolve(YChannel, row);
            if (xRaw == null || yRaw == null) continue;
            float px = ctx.Plot.MapX(xScale.Map(xRaw));
            float py = ctx.Plot.MapY(yScale.Map(yRaw));
            float r  = DefaultRadius;
            if (sScale != null && ctx.Encodes.Has(Channel.Size))
            {
                var sRaw = ctx.Encodes.Resolve(Channel.Size, row);
                if (sRaw != null) r = (ctx.Theme?.PointSizeMin ?? 3f) + (float)sScale.Map(sRaw) * (ctx.Theme?.PointSizeRange ?? 20f);
            }

            if (pos.DistanceTo(new Vector2(px, py)) <= r + (ctx.Theme?.HitTestPointPadding ?? 4f))
            {
                return new HitResult
                {
                    Hit = true, Row = row, RowIndex = i,
                    ScreenX = px, ScreenY = py,
                    Label = $"({ctx.Encodes.Resolve(Channel.X, row)}, {ctx.Encodes.Resolve(YChannel, row)})",
                    SeriesKey = ResolveSeriesKey(ctx, row),
                    MarkType = nameof(PointMark),
                };
            }
        }
        return null;
    }
}
