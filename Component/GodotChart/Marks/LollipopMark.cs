using Godot;

namespace GodotNodeExtension.Component.GodotChart.Marks;

/// <summary>
/// Lollipop chart mark. Renders each data point as a thin stem line
/// with a filled circle at the top — a simplified alternative to bar charts.
/// </summary>
public class LollipopMark : Mark
{
    /// <inheritdoc />
    public override MarkCoordinate Coordinate => MarkCoordinate.Cartesian;

    /// <summary>Radius of the lollipop dot in pixels.</summary>
    public float DotRadius { get; set; } = 5f;

    /// <summary>Width of the stem line in pixels.</summary>
    public float StemWidth { get; set; } = 2f;

    /// <summary>Bar orientation. Default is Vertical.</summary>
    public BarOrientation Orientation { get; set; } = BarOrientation.Vertical;

    public override void Render(MarkContext ctx)
    {
        bool horizontal = Orientation == BarOrientation.Horizontal;

        OrdinalScale? ordinalScale;
        LinearScale?  valueScale;
        if (horizontal)
        {
            ordinalScale = ctx.Scales.TryGet(YChannel) as OrdinalScale
                        ?? ctx.Scales.TryGet(Channel.X) as OrdinalScale;
            valueScale   = ctx.Scales.TryGet(Channel.X) as LinearScale
                        ?? ctx.Scales.TryGet(YChannel) as LinearScale;
        }
        else
        {
            ordinalScale = ctx.Scales.TryGet(Channel.X) as OrdinalScale;
            valueScale   = ctx.Scales.TryGet(YChannel) as LinearScale;
        }
        if (ordinalScale == null || valueScale == null) return;
        if (ordinalScale.Domain.Count == 0) return;

        float anim     = ComputeAnimProgress(ctx);
        float baseline = horizontal ? ctx.Plot.MapX(0) : ctx.Plot.MapY(0);

        for (int i = 0; i < ctx.Data.Count; i++)
        {
            var row = ctx.Data[i];
            if (IsSeriesHidden(ctx, row)) continue;
            var xRaw = ctx.Encodes.Resolve(Channel.X, row);
            var yRaw = ctx.Encodes.Resolve(YChannel, row);
            if (xRaw == null || yRaw == null) continue;

            var color = ResolveColorWithOverride(ctx, row, i, GetDefaultColor(ctx));
            float opacity = ComputeEffectiveOpacity(ctx, row);

            bool isHovered  = i == ctx.HoveredRowIndex;
            bool isSelected = i == ctx.SelectedRowIndex;
            if (isHovered) color = BrightenColor(color, GetHoverBrighten(ctx));

            // Map to screen coordinates based on orientation
            float cx, cy;
            if (horizontal)
            {
                cy = ctx.Plot.MapY((float)ordinalScale.Map(yRaw));
                float target = ctx.Plot.MapX((float)valueScale.Map(xRaw));
                cx = baseline + (target - baseline) * anim;
            }
            else
            {
                cx = ctx.Plot.MapX((float)ordinalScale.Map(xRaw));
                float target = ctx.Plot.MapY((float)valueScale.Map(yRaw));
                cy = baseline + (target - baseline) * anim;
            }

            // Stem line
            using var stemPath  = ctx.Canvas.CreatePath();
            using var stemPaint = ctx.Canvas.CreatePaint();
            if (horizontal) { stemPath.MoveTo(baseline, cy); stemPath.LineTo(cx, cy); }
            else            { stemPath.MoveTo(cx, baseline); stemPath.LineTo(cx, cy); }
            stemPaint.SetColor(color).SetStrokeWidth(StemWidth).SetAntiAlias(true).SetOpacity(opacity);
            ctx.Canvas.Stroke(stemPath, stemPaint);

            // Dot
            float dotR = isHovered ? DotRadius * (ctx.Theme?.LollipopHoverScale ?? 1.3f) : DotRadius;
            using var dotPath  = ctx.Canvas.CreatePath();
            using var dotPaint = ctx.Canvas.CreatePaint();
            dotPath.Circle(cx, cy, dotR);
            dotPaint.SetColor(color).SetAntiAlias(true).SetOpacity(opacity);
            ctx.Canvas.Fill(dotPath, dotPaint);

            // Selected: highlight ring
            if (isSelected)
            {
                using var ringPaint = ctx.Canvas.CreatePaint();
                ApplySelectionPaint(ctx, ringPaint, opacity);
                ctx.Canvas.Stroke(dotPath, ringPaint);
            }
        }
    }

    public override HitResult? HitTest(MarkContext ctx, Vector2 screenPos)
    {
        bool horizontal = Orientation == BarOrientation.Horizontal;

        IScale? ordinalScale, valueScale;
        if (horizontal)
        {
            ordinalScale = ctx.Scales.TryGet(YChannel) as OrdinalScale
                        ?? ctx.Scales.TryGet(Channel.X) as OrdinalScale;
            valueScale   = ctx.Scales.TryGet(Channel.X) as LinearScale
                        ?? ctx.Scales.TryGet(YChannel) as LinearScale;
        }
        else
        {
            ordinalScale = ctx.Scales.TryGet(Channel.X) as OrdinalScale;
            valueScale   = ctx.Scales.TryGet(YChannel) as LinearScale;
        }
        if (ordinalScale == null || valueScale == null) return null;

        for (int i = 0; i < ctx.Data.Count; i++)
        {
            var row = ctx.Data[i];
            if (IsSeriesHidden(ctx, row)) continue;
            var xRaw = ctx.Encodes.Resolve(Channel.X, row);
            var yRaw = ctx.Encodes.Resolve(YChannel, row);
            if (xRaw == null || yRaw == null) continue;

            float cx, cy;
            if (horizontal)
            {
                cy = ctx.Plot.MapY((float)ordinalScale.Map(yRaw));
                cx = ctx.Plot.MapX((float)valueScale.Map(xRaw));
            }
            else
            {
                cx = ctx.Plot.MapX((float)ordinalScale.Map(xRaw));
                cy = ctx.Plot.MapY((float)valueScale.Map(yRaw));
            }

            float dist = screenPos.DistanceTo(new Vector2(cx, cy));
            if (dist <= DotRadius + (ctx.Theme?.HitTestPointPadding ?? 4f))
            {
                return new HitResult
                {
                    Hit = true, Row = row, RowIndex = i,
                    ScreenX = cx, ScreenY = cy,
                    Label = horizontal ? $"{yRaw}: {xRaw}" : $"{xRaw}: {yRaw}",
                    SeriesKey = ResolveSeriesKey(ctx, row),
                    MarkType = nameof(LollipopMark),
                };
            }
        }
        return null;
    }
}
