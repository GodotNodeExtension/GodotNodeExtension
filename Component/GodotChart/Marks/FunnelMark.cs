using Godot;
using GodotNodeExtension.Component.GodotChart.Canvas;

namespace GodotNodeExtension.Component.GodotChart.Marks;

/// <summary>
/// Funnel chart mark. Renders centered trapezoid stages that narrow from top to bottom.
/// Encodes: X = stage label (OrdinalScale), Y = value (LinearScale).
/// Uses Polar coordinate to suppress Cartesian grid/axes; the funnel is self-contained.
/// Animation: stages fade in and grow from center.
/// </summary>
public class FunnelMark : Mark
{
    /// <inheritdoc />
    /// <remarks>Polar coordinate is used so no Cartesian grid or axes are drawn.</remarks>
    public override MarkCoordinate Coordinate => MarkCoordinate.Polar;

    /// <summary>Gap between funnel stages in pixels.</summary>
    public float StageGap { get; set; } = 4f;

    /// <summary>Minimum width ratio of the narrowest stage relative to plot width [0, 1].</summary>
    public float MinWidthRatio { get; set; } = 0.15f;

    /// <summary>Corner radius for each funnel stage.</summary>
    public float CornerRadius { get; set; } = 3f;

    /// <summary>Whether to show value labels inside each stage.</summary>
    public override bool ShowLabel { get; set; } = true;

    private double _cachedMaxVal;
    private int _cachedVersion = -1;

    private double EnsureMaxVal(MarkContext ctx)
    {
        if (_cachedVersion == ctx.LayoutVersion) return _cachedMaxVal;
        double maxVal = 0;
        for (int i = 0; i < ctx.Data.Count; i++)
        {
            if (IsSeriesHidden(ctx, ctx.Data[i])) continue;
            var yRaw = ctx.Encodes.Resolve(YChannel, ctx.Data[i]);
            if (yRaw != null)
            {
                double v = ToDouble(yRaw, "Y");
                if (v > maxVal) maxVal = v;
            }
        }
        _cachedMaxVal = maxVal;
        _cachedVersion = ctx.LayoutVersion;
        return maxVal;
    }

    public override void Render(MarkContext ctx)
    {
        if (ctx.Data.Count == 0) return;

        float anim = ComputeAnimProgress(ctx);
        float plotW = ctx.Plot.Width;
        float plotH = ctx.Plot.Height;

        double maxVal = EnsureMaxVal(ctx);
        if (maxVal <= 0) return;

        int count = ctx.Data.Count;
        float totalGap = StageGap * (count - 1);
        float stageH = (plotH - totalGap) / count;

        for (int i = 0; i < count; i++)
        {
            var row  = ctx.Data[i];
            if (IsSeriesHidden(ctx, row)) continue;
            var xRaw = ctx.Encodes.Resolve(Channel.X, row);
            var yRaw = ctx.Encodes.Resolve(YChannel, row);
            if (yRaw == null) continue;

            double val = ToDouble(yRaw, "Y");
            float widthRatio = (float)(val / maxVal);
            // Lerp between full width and min width based on value ratio
            float stageW = plotW * (MinWidthRatio + (1f - MinWidthRatio) * widthRatio) * anim;

            float cx = ctx.Plot.X + plotW / 2f;
            float py = ctx.Plot.Y + i * (stageH + StageGap);

            var color = ResolveColorWithOverride(ctx, row, i, GetDefaultColor(ctx));
            float opacity = ComputeEffectiveOpacity(ctx, row);

            bool isHovered = i == ctx.HoveredRowIndex;
            if (isHovered) color = BrightenColor(color, GetHoverBrighten(ctx));

            using var path  = ctx.Canvas.CreatePath();
            using var paint = ctx.Canvas.CreatePaint();

            float left = cx - stageW / 2f;
            if (CornerRadius > 0)
                path.RoundRect(left, py, stageW, stageH, CornerRadius);
            else
                path.Rect(left, py, stageW, stageH);

            paint.SetColor(color).SetAntiAlias(true).SetOpacity(opacity);
            ctx.Canvas.Fill(path, paint);

            // Selection outline
            if (i == ctx.SelectedRowIndex)
            {
                using var selPaint = ctx.Canvas.CreatePaint();
                ApplySelectionPaint(ctx, selPaint, opacity);
                ctx.Canvas.Stroke(path, selPaint);
            }

            // Value label
            if (ShowLabel)
            {
                using var labelPaint = ctx.Canvas.CreatePaint();
                labelPaint.SetColor(GetDataLabelColor(ctx)).SetOpacity(opacity);
                string label = xRaw != null ? $"{xRaw}: {yRaw}" : $"{yRaw}";
                ctx.Canvas.DrawText(label, cx, py + stageH / 2f, FontSettings.Default, labelPaint);
            }
        }
    }

    public override HitResult? HitTest(MarkContext ctx, Vector2 pos)
    {
        if (ctx.Data.Count == 0) return null;

        float plotW = ctx.Plot.Width;
        float plotH = ctx.Plot.Height;

        double maxVal = EnsureMaxVal(ctx);
        if (maxVal <= 0) return null;

        int count = ctx.Data.Count;
        float totalGap = StageGap * (count - 1);
        float stageH = (plotH - totalGap) / count;
        float anim = ComputeAnimProgress(ctx);

        for (int i = 0; i < count; i++)
        {
            var row  = ctx.Data[i];
            if (IsSeriesHidden(ctx, row)) continue;
            var yRaw = ctx.Encodes.Resolve(YChannel, row);
            if (yRaw == null) continue;

            double val = ToDouble(yRaw, "Y");
            float widthRatio = (float)(val / maxVal);
            // Apply animation factor consistent with Render
            float stageW = plotW * (MinWidthRatio + (1f - MinWidthRatio) * widthRatio) * anim;

            float cx = ctx.Plot.X + plotW / 2f;
            float py = ctx.Plot.Y + i * (stageH + StageGap);
            float left = cx - stageW / 2f;

            if (pos.X >= left && pos.X <= left + stageW &&
                pos.Y >= py && pos.Y <= py + stageH)
            {
                return new HitResult
                {
                    Hit = true, Row = row, RowIndex = i,
                    ScreenX = cx, ScreenY = py + stageH / 2f,
                    Label = $"{ctx.Encodes.Resolve(Channel.X, row)}: {yRaw}",
                    MarkType = nameof(FunnelMark),
                };
            }
        }
        return null;
    }
}
