using System;
using System.Collections.Generic;
using Godot;

namespace GodotNodeExtension.Component.GodotChart.Marks;

/// <summary>
/// Candlestick chart mark for financial OHLC data visualization.
/// Animation: candles expand from mid-price outward.
/// </summary>
public class CandlestickMark : Mark
{
    /// <inheritdoc />
    public override MarkCoordinate Coordinate => MarkCoordinate.Cartesian;

    public string OpenField  { get; set; } = "open";
    public string HighField  { get; set; } = "high";
    public string LowField   { get; set; } = "low";
    public string CloseField { get; set; } = "close";

    public Color?  BullishColor   { get; set; }
    public Color?  BearishColor   { get; set; }

    /// <summary>Resolve bullish color considering theme fallback.</summary>
    private Color ResolveBullishColor(MarkContext ctx) =>
        BullishColor ?? ctx.Theme?.CandlestickBullishColor ?? new Color(0.29f, 0.85f, 0.60f);

    /// <summary>Resolve bearish color considering theme fallback.</summary>
    private Color ResolveBearishColor(MarkContext ctx) =>
        BearishColor ?? ctx.Theme?.CandlestickBearishColor ?? new Color(0.98f, 0.45f, 0.29f);
    public float  BodyWidthRatio { get; set; } = 0.6f;
    public float  WickWidth      { get; set; } = 1.5f;
    public float  CornerRadius   { get; set; } = 1f;
    public bool   FillBullish    { get; set; } = true;

    /// <inheritdoc />
    public override void ContributeScales(ScaleSet scales, EncodeSet encodes, List<DataRow> data)
    {
        ScaleContributionHelper.ContributeMinMaxScale(scales, data, LowField, HighField, YChannel);
    }

    /// <inheritdoc />
    public override void Render(MarkContext ctx)
    {
        var xScale = ctx.Scales.TryGet(Channel.X) as OrdinalScale;
        var yScale = ctx.Scales.TryGet(YChannel) as LinearScale;
        if (xScale == null || yScale == null) return;

        float slotW = ctx.Plot.Width / xScale.Domain.Count;
        float bodyW = slotW * BodyWidthRatio;
        float anim  = ComputeAnimProgress(ctx);
        float globalOpacity = ctx.Animation.GlobalOpacity;

        for (int i = 0; i < ctx.Data.Count; i++)
        {
            var row  = ctx.Data[i];
            if (IsSeriesHidden(ctx, row)) continue;
            var xRaw = ctx.Encodes.Resolve(Channel.X, row);
            if (xRaw == null) continue;

            double open  = GetDouble(row, OpenField);
            double high  = GetDouble(row, HighField);
            double low   = GetDouble(row, LowField);
            double close = GetDouble(row, CloseField);

            bool bullish = close >= open;
            var  color   = bullish ? ResolveBullishColor(ctx) : ResolveBearishColor(ctx);
            float cx     = ctx.Plot.MapX(xScale.Map(xRaw));

            bool isHovered  = i == ctx.HoveredRowIndex;
            bool isSelected = i == ctx.SelectedRowIndex;

            // Series focus opacity (candlestick has no Color channel, use full opacity)
            float opacity = globalOpacity;

            // Hover: brighten color
            float drawBodyW  = bodyW;
            float drawWickW  = WickWidth;
            if (isHovered)
            {
                color = BrightenColor(color, GetHoverBrighten(ctx));
                drawWickW *= ctx.Theme?.CandlestickHoverWickScale ?? 1.5f;
            }

            // Map to screen Y
            float yHigh  = ctx.Plot.MapY(yScale.Map(high));
            float yLow   = ctx.Plot.MapY(yScale.Map(low));
            float yOpen  = ctx.Plot.MapY(yScale.Map(open));
            float yClose = ctx.Plot.MapY(yScale.Map(close));

            // Animation: lerp from midpoint outward
            float mid = (yOpen + yClose) * 0.5f;
            yOpen  = mid + (yOpen  - mid) * anim;
            yClose = mid + (yClose - mid) * anim;
            float midHL = (yHigh + yLow) * 0.5f;
            yHigh = midHL + (yHigh - midHL) * anim;
            yLow  = midHL + (yLow  - midHL) * anim;

            float bodyTop    = Math.Min(yOpen, yClose);
            float bodyBottom = Math.Max(yOpen, yClose);
            float bodyHeight = Math.Max(bodyBottom - bodyTop, 1f);

            // Upper wick
            using (var wp = ctx.Canvas.CreatePaint())
            {
                wp.SetColor(color).SetStrokeWidth(drawWickW).SetOpacity(opacity);
                ctx.Canvas.DrawLine(cx, yHigh, cx, bodyTop, wp);
            }
            // Lower wick
            using (var wp = ctx.Canvas.CreatePaint())
            {
                wp.SetColor(color).SetStrokeWidth(drawWickW).SetOpacity(opacity);
                ctx.Canvas.DrawLine(cx, bodyBottom, cx, yLow, wp);
            }

            // Body
            using var bodyPath  = ctx.Canvas.CreatePath();
            using var bodyPaint = ctx.Canvas.CreatePaint();
            float bx = cx - drawBodyW * 0.5f;
            if (CornerRadius > 0)
                bodyPath.RoundRect(bx, bodyTop, drawBodyW, bodyHeight, CornerRadius);
            else
                bodyPath.Rect(bx, bodyTop, drawBodyW, bodyHeight);

            bodyPaint.SetColor(color).SetAntiAlias(true).SetOpacity(opacity);
            if (bullish && !FillBullish)
            {
                bodyPaint.SetStrokeWidth(ctx.Theme?.CandlestickHollowStrokeWidth ?? 1.5f);
                ctx.Canvas.Stroke(bodyPath, bodyPaint);
            }
            else
            {
                ctx.Canvas.Fill(bodyPath, bodyPaint);
            }

            // Selected: draw highlight stroke
            if (isSelected)
            {
                using var strokePaint = ctx.Canvas.CreatePaint();
                ApplySelectionPaint(ctx, strokePaint, opacity);
                ctx.Canvas.Stroke(bodyPath, strokePaint);
            }
        }
    }

    public override HitResult? HitTest(MarkContext ctx, Vector2 pos)
    {
        var xScale = ctx.Scales.TryGet(Channel.X) as OrdinalScale;
        var yScale = ctx.Scales.TryGet(YChannel) as LinearScale;
        if (xScale == null || yScale == null) return null;

        float slotW = ctx.Plot.Width / xScale.Domain.Count;

        for (int i = 0; i < ctx.Data.Count; i++)
        {
            var row  = ctx.Data[i];
            if (IsSeriesHidden(ctx, row)) continue;
            var xRaw = ctx.Encodes.Resolve(Channel.X, row);
            if (xRaw == null) continue;
            float cx = ctx.Plot.MapX(xScale.Map(xRaw));

            if (Math.Abs(pos.X - cx) <= slotW * 0.5f &&
                pos.Y >= ctx.Plot.Y && pos.Y <= ctx.Plot.Y + ctx.Plot.Height)
            {
                double o = GetDouble(row, OpenField);
                double h = GetDouble(row, HighField);
                double l = GetDouble(row, LowField);
                double c = GetDouble(row, CloseField);
                return new HitResult
                {
                    Hit = true, Row = row, RowIndex = i,
                    ScreenX = cx, ScreenY = ctx.Plot.MapY(yScale.Map(c)),
                    Label = $"O:{o:F1} H:{h:F1} L:{l:F1} C:{c:F1}",
                    MarkType = nameof(CandlestickMark),
                };
            }
        }
        return null;
    }
}
