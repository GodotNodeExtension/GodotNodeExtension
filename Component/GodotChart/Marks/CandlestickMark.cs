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

    /// <summary>Field holding the period's opening price. Default "open".</summary>
    public string OpenField  { get; set; } = "open";

    /// <summary>Field holding the period's highest price. Default "high".</summary>
    public string HighField  { get; set; } = "high";

    /// <summary>Field holding the period's lowest price. Default "low".</summary>
    public string LowField   { get; set; } = "low";

    /// <summary>Field holding the period's closing price. Default "close".</summary>
    public string CloseField { get; set; } = "close";

    /// <summary>Color for rising candles. Null = use <see cref="ChartTheme.CandlestickBullishColor"/>.</summary>
    public Color?  BullishColor   { get; set; }

    /// <summary>Color for falling candles. Null = use <see cref="ChartTheme.CandlestickBearishColor"/>.</summary>
    public Color?  BearishColor   { get; set; }

    /// <summary>Resolve bullish color considering theme fallback.</summary>
    private Color ResolveBullishColor(MarkContext ctx) =>
        BullishColor ?? (ctx.Theme ?? ChartTheme.Default).CandlestickBullishColor;

    /// <summary>Resolve bearish color considering theme fallback.</summary>
    private Color ResolveBearishColor(MarkContext ctx) =>
        BearishColor ?? (ctx.Theme ?? ChartTheme.Default).CandlestickBearishColor;
    /// <summary>Body width as a fraction of the category slot (0-1). Default 0.6.</summary>
    public float  BodyWidthRatio { get; set; } = 0.6f;

    /// <summary>Width of the high/low wicks in pixels. Default 1.5.</summary>
    public float  WickWidth      { get; set; } = 1.5f;

    /// <summary>Corner radius of the candle body in pixels. Default 3.</summary>
    public float  CornerRadius   { get; set; } = 3f;

    /// <summary>
    /// Whether rising candles are filled. When false they are drawn as outlines only (hollow bodies).
    /// </summary>
    public bool   FillBullish    { get; set; } = true;

    /// <inheritdoc />
    public override void ContributeScales(ScaleSet scales, EncodeSet encodes, List<DataRow> data)
    {
        ScaleContributionHelper.ContributeMinMaxScale(scales, data, LowField, HighField, YChannel);
    }

    /// <summary>
    /// Screen geometry of one candle, shared by <see cref="Render"/> and <see cref="HitTest"/>: the
    /// candle's high/low wick span (top and bottom, in screen coordinates) and its body rectangle, with
    /// the entry animation applied.
    /// <para>
    /// The body is clamped into the wick span, so dirty data (a high below the body's top, or a low
    /// above its bottom) cannot paint the body outside the candle's own range.
    /// </para>
    /// </summary>
    /// <param name="ctx">Context holding the plot area.</param>
    /// <param name="yScale">Value scale of the candle.</param>
    /// <param name="open">Opening price.</param>
    /// <param name="high">Highest price.</param>
    /// <param name="low">Lowest price.</param>
    /// <param name="close">Closing price.</param>
    /// <param name="anim">Entry animation progress.</param>
    private static (float wickTop, float wickBottom, float bodyTop, float bodyBottom) CandleGeometry(
        MarkContext ctx, LinearScale yScale, double open, double high, double low, double close, float anim)
    {
        // Map to screen Y
        float yHigh  = ctx.Plot.MapY(yScale.Map(high));
        float yLow   = ctx.Plot.MapY(yScale.Map(low));
        float yOpen  = ctx.Plot.MapY(yScale.Map(open));
        float yClose = ctx.Plot.MapY(yScale.Map(close));

        // Animation: lerp from midpoint outward
        float mid = (yOpen + yClose) * 0.5f;
        yOpen  = mid + (yOpen  - mid) * anim;
        yClose = mid + (yClose - mid) * anim;
        float midHl = (yHigh + yLow) * 0.5f;
        yHigh = midHl + (yHigh - midHl) * anim;
        yLow  = midHl + (yLow  - midHl) * anim;

        // Screen Y grows downwards, so the high sits at the smaller coordinate.
        float wickTop    = MathF.Min(yHigh, yLow);
        float wickBottom = MathF.Max(yHigh, yLow);
        float bodyTop    = Math.Clamp(Math.Min(yOpen, yClose), wickTop, wickBottom);
        float bodyBottom = Math.Clamp(Math.Max(yOpen, yClose), wickTop, wickBottom);
        return (wickTop, wickBottom, bodyTop, bodyBottom);
    }

    /// <summary>
    /// Screen geometry of one candle: the candle's screen X, its high/low wick span (top and bottom,
    /// in screen coordinates) and its body rectangle, with the entry animation applied.
    /// </summary>
    /// <param name="Cx">Screen X of the candle's axis.</param>
    /// <param name="WickTop">Top of the high/low wick span.</param>
    /// <param name="WickBottom">Bottom of the high/low wick span.</param>
    /// <param name="BodyTop">Top of the candle body.</param>
    /// <param name="BodyBottom">Bottom of the candle body.</param>
    /// <param name="Bullish">True when the candle closes at or above its open (its colour follows).</param>
    private readonly record struct RowGeometry(
        float Cx, float WickTop, float WickBottom, float BodyTop, float BodyBottom, bool Bullish);

    /// <summary>
    /// Screen geometry of one candle, or <c>false</c> when the row cannot be drawn: hidden, missing one of
    /// the four OHLC fields, carrying a non-finite price, or standing on a category the X scale does not
    /// know (<see cref="OrdinalScale.Map"/> answers 0 for it - the *first* category - so the candle would
    /// be drawn on top of another one).
    /// </summary>
    /// <param name="ctx">Context of the frame.</param>
    /// <param name="xScale">Category scale of the candle.</param>
    /// <param name="yScale">Price scale of the candle.</param>
    /// <param name="index">Index of the row in the rendered data.</param>
    /// <param name="anim">Entry animation progress.</param>
    /// <param name="geometry">Geometry of the row when the method returns true.</param>
    private bool TryRowGeometry(MarkContext ctx, OrdinalScale xScale, LinearScale yScale,
        int index, float anim, out RowGeometry geometry)
    {
        geometry = default;
        if (index < 0 || index >= ctx.Data.Count) return false;

        var row = ctx.Data[index];
        if (IsSeriesHidden(ctx, row)) return false;
        var xRaw = ctx.Encodes.Resolve(Channel.X, row);
        if (xRaw == null) return false;
        // All four OHLC fields must exist with non-null values; otherwise the row cannot be drawn.
        if (!HasFields(row, OpenField, HighField, LowField, CloseField)) return false;

        double open  = GetDouble(row, OpenField);
        double high  = GetDouble(row, HighField);
        double low   = GetDouble(row, LowField);
        double close = GetDouble(row, CloseField);
        // IsFinite, not IsNaN: an infinite OHLC value maps to a non-finite coordinate and the mark
        // would draw geometry outside every bound (the shared data-safety tests check this).
        if (!double.IsFinite(open) || !double.IsFinite(high) ||
            !double.IsFinite(low) || !double.IsFinite(close))
            return false;

        // See BoxMark: an unknown category maps to 0 (the first one) instead of "nowhere", so it is
        // skipped here rather than drawn on top of another candle.
        if (xScale.IndexOf(xRaw.ToString() ?? "") < 0) return false;

        float cx = ctx.Plot.MapX(xScale.Map(xRaw));
        // Shared geometry: wick span and body rectangle (see CandleGeometry).
        var (wickTop, wickBottom, bodyTop, bodyBottom) =
            CandleGeometry(ctx, yScale, open, high, low, close, anim);

        geometry = new RowGeometry(cx, wickTop, wickBottom, bodyTop, bodyBottom, close >= open);
        return true;
    }

    /// <summary>
    /// Draw one candle: its two wicks and its body, plus the selection ring when the row is the selected
    /// one. The fill the caller passes in is the one the pass owns: the data layer hands over what
    /// <see cref="Mark.ResolveFill"/> answered (the hover look while it owns the state), and the overlay
    /// pass hands over <see cref="Mark.ActiveFillOf"/> for the hovered candle. Both go through the same
    /// geometry (<see cref="TryRowGeometry"/>), so the highlight cannot drift away from the candle.
    /// </summary>
    /// <param name="ctx">Context of the frame.</param>
    /// <param name="geometry">Geometry of the candle, from <see cref="TryRowGeometry"/>.</param>
    /// <param name="bodyW">Drawn body width.</param>
    /// <param name="color">Fill (and wick) colour of the candle.</param>
    /// <param name="opacity">Opacity of the candle.</param>
    /// <param name="hovered">True when the row is the hovered one (its wick is widened here).</param>
    /// <param name="selected">True when the row is the selected one (its ring is drawn here).</param>
    private void DrawCandle(MarkContext ctx, RowGeometry geometry, float bodyW, Color color, float opacity,
        bool hovered, bool selected)
    {
        // Hover: widen the wick (the colour highlight comes from the fill the caller passed in).
        float drawWickW = WickWidth;
        if (hovered)
            drawWickW *= HoverScaled(ctx, (ctx.Theme ?? ChartTheme.Default).CandlestickHoverWickScale);

        // Both wicks share the mark's cached paint (it must not be disposed here: the mark reuses
        // it for the next candle and, on the Skia backend, disposing it would free the native
        // paint the mark still holds).
        var wickPaint = ShapePaint(ctx);
        wickPaint.SetColor(color).SetStrokeWidth(drawWickW).SetOpacity(opacity);
        ctx.Canvas.DrawLine(geometry.Cx, geometry.WickTop, geometry.Cx, geometry.BodyTop, wickPaint);
        ctx.Canvas.DrawLine(geometry.Cx, geometry.BodyBottom, geometry.Cx, geometry.WickBottom, wickPaint);

        // Body
        var bodyPath = ShapePath(ctx);
        var bodyPaint = ShapePaint(ctx);
        float bx = geometry.Cx - bodyW * 0.5f;
        float bodyHeight = MathF.Max(geometry.BodyBottom - geometry.BodyTop, 1f);
        if (CornerRadius > 0)
            bodyPath.RoundRect(bx, geometry.BodyTop, bodyW, bodyHeight, CornerRadius);
        else
            bodyPath.Rect(bx, geometry.BodyTop, bodyW, bodyHeight);

        bodyPaint.SetColor(color).SetAntiAlias(true).SetOpacity(opacity);
        if (geometry.Bullish && !FillBullish)
        {
            bodyPaint.SetStrokeWidth((ctx.Theme ?? ChartTheme.Default).CandlestickHollowStrokeWidth);
            ctx.Canvas.Stroke(bodyPath, bodyPaint);
        }
        else
        {
            ctx.Canvas.Fill(bodyPath, bodyPaint);
        }

        // Selected: draw highlight stroke
        if (selected)
        {
            var strokePaint = ShapePaint(ctx);
            ApplySelectionPaint(ctx, strokePaint, opacity);
            ctx.Canvas.Stroke(bodyPath, strokePaint);
        }
    }

    /// <summary>
    /// The hover visual of a candle is a widened wick plus the brightened fill, both opaque, so the
    /// overlay pass can redraw the candle exactly as the data layer drew it without touching the layer.
    /// </summary>
    public override bool InteractionStateInOverlay => true;

    /// <inheritdoc />
    public override void Render(MarkContext ctx)
    {
        var xScale = ctx.Scales.TryGet(Channel.X) as OrdinalScale;
        var yScale = ctx.Scales.TryGet(YChannel) as LinearScale;
        if (xScale == null || yScale == null) return;
        if (xScale.Domain.Count == 0) return;

        float slotW = ctx.Plot.Width / xScale.Domain.Count;
        float bodyW = slotW * BodyWidthRatio;
        float anim  = ComputeAnimProgress(ctx);

        // The interaction state stays in the data layer only while the chart does not cache it; with the
        // cache on the overlay paints it (see InteractionStateInOverlay / RenderOverlay).
        bool stateHere = !ctx.StateInOverlay;

        for (int i = 0; i < ctx.Data.Count; i++)
        {
            if (!TryRowGeometry(ctx, xScale, yScale, i, anim, out var geometry)) continue;

            var row = ctx.Data[i];
            // Bull/bear colour as the default; ResolveFill applies the style callback plus the hover
            // brighten, and the result drives both the body fill and the wicks (as before).
            var color = ResolveFill(ctx, row, i,
                geometry.Bullish ? ResolveBullishColor(ctx) : ResolveBearishColor(ctx));

            // Per-element opacity: the Opacity channel (or the mark's default), the style callback,
            // the interaction state and the global animation opacity. The helper applies the global
            // animation opacity itself and needs the row index for the hover/selection/focus styles.
            float opacity = ComputeElementOpacity(ctx, row, i);

            DrawCandle(ctx, geometry, bodyW, color, opacity,
                hovered: stateHere && i == ctx.HoveredRowIndex,
                selected: stateHere && i == ctx.SelectedRowIndex);
        }
    }

    /// <inheritdoc />
    public override void RenderOverlay(MarkContext ctx)
    {
        // Only while the chart keeps the data layer in an image: with the cache off Render painted the state.
        if (!OverlayRows(ctx, out int hovered, out int selected)) return;

        var xScale = ctx.Scales.TryGet(Channel.X) as OrdinalScale;
        var yScale = ctx.Scales.TryGet(YChannel) as LinearScale;
        if (xScale == null || yScale == null || xScale.Domain.Count == 0) return;

        float bodyW = ctx.Plot.Width / xScale.Domain.Count * BodyWidthRatio;
        float anim = ComputeAnimProgress(ctx);

        // Only the interactive rows are drawn, from the same geometry the data layer used.
        if (hovered >= 0) DrawInteractive(ctx, xScale, yScale, hovered, bodyW, anim, hovered: true);
        if (selected >= 0 && selected != hovered)
            DrawInteractive(ctx, xScale, yScale, selected, bodyW, anim, hovered: false);
    }

    /// <summary>
    /// Draw one interactive candle on the overlay pass: the hovered one takes the active fill (the data
    /// layer answered the default one while the state lives here) and the selected one gets its ring.
    /// </summary>
    private void DrawInteractive(MarkContext ctx, OrdinalScale xScale, LinearScale yScale, int index,
        float bodyW, float anim, bool hovered)
    {
        if (!TryRowGeometry(ctx, xScale, yScale, index, anim, out var geometry)) return;

        var row = ctx.Data[index];
        var color = ResolveFill(ctx, row, index,
            geometry.Bullish ? ResolveBullishColor(ctx) : ResolveBearishColor(ctx));
        if (hovered) color = ActiveFillOf(ctx, color);
        float opacity = ComputeElementOpacity(ctx, row, index);

        DrawCandle(ctx, geometry, bodyW, color, opacity, hovered: hovered,
            selected: index == ctx.SelectedRowIndex);
    }

    /// <inheritdoc />
    public override HitResult? HitTest(MarkContext ctx, Vector2 screenPos)
    {
        var xScale = ctx.Scales.TryGet(Channel.X) as OrdinalScale;
        var yScale = ctx.Scales.TryGet(YChannel) as LinearScale;
        if (xScale == null || yScale == null) return null;
        if (xScale.Domain.Count == 0) return null;

        float slotW = ctx.Plot.Width / xScale.Domain.Count;
        float bodyW = slotW * BodyWidthRatio;
        float anim  = ComputeAnimProgress(ctx);
        float padding = (ctx.Theme ?? ChartTheme.Default).HitTestPointPadding;

        for (int i = 0; i < ctx.Data.Count; i++)
        {
            var row  = ctx.Data[i];
            if (IsSeriesHidden(ctx, row)) continue;
            var xRaw = ctx.Encodes.Resolve(Channel.X, row);
            if (xRaw == null) continue;
            // Mirror the Render() guard: rows without a complete OHLC set are never drawn,
            // so they must not be hit-testable either.
            if (!HasFields(row, OpenField, HighField, LowField, CloseField)) continue;

            double o = GetDouble(row, OpenField);
            double h = GetDouble(row, HighField);
            double l = GetDouble(row, LowField);
            double c = GetDouble(row, CloseField);
            // Same non-finite guard as Render: a row with an infinite bound is not drawn at all.
            if (!double.IsFinite(o) || !double.IsFinite(h) || !double.IsFinite(l) || !double.IsFinite(c))
                continue;

            float cx = ctx.Plot.MapX(xScale.Map(xRaw));

            // The hit area is the drawn candle: the body's width around the candle's axis and the
            // wick span, both from the same geometry Render fills (see CandleGeometry). A hit used to
            // be reported for any point in the candle's slot at any height of the plot.
            var (wickTop, wickBottom, _, _) = CandleGeometry(ctx, yScale, o, h, l, c, anim);

            if (MathF.Abs(screenPos.X - cx) <= bodyW * 0.5f + padding &&
                screenPos.Y >= wickTop - padding && screenPos.Y <= wickBottom + padding)
            {
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
