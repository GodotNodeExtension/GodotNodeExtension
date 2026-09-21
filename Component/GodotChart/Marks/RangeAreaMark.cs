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
    public float StrokeWidth { get; set; } = 2f;

    /// <summary>Whether to use smooth (cubic) curves.</summary>
    public bool Smooth { get; set; }

    /// <summary>
    /// This mark keeps its interaction state in the data layer, so a chart carrying it renders single-pass
    /// (see <see cref="Chart.UseLayerCache"/>).
    /// <para>
    /// Its hover visual is not a separate element but the <b>colour of the band itself</b>: the fill and
    /// both border lines are painted with the colour resolved from the first visible row, so hovering that
    /// row brightens a 0.3-alpha area framed by full-opacity borders. The overlay could add the two hover
    /// dots and the selection rings, but the band's brighten would either be lost or require repainting the
    /// translucent band over itself (a double blend), and the dots alone are not the state the reader sees.
    /// </para>
    /// </summary>
    public override bool InteractionStateInOverlay => false;

    /// <summary>
    /// Contributes both edges of the band to the value axis: the value the Y channel encodes (the
    /// upper bound) and <see cref="LowerField"/>. The axis is auto-fitted from the encoded Y values
    /// alone, so without this the lower edge of every band fell outside the plot rectangle and was
    /// clipped away.
    /// </summary>
    /// <remarks>
    /// Series visibility is not part of the contribution stage - the chart hands the data over
    /// without its hidden-series set (its own auto-fit folds those rows in as well), so every row
    /// contributes here, exactly as it does for <see cref="BoxMark"/> and <see cref="CandlestickMark"/>.
    /// </remarks>
    public override void ContributeScales(ScaleSet scales, EncodeSet encodes, List<DataRow> data)
    {
        double min = double.MaxValue;
        double max = double.MinValue;

        foreach (var row in data)
        {
            ExtendBounds(encodes.Resolve(YChannel, row), ref min, ref max);
            ExtendBounds(HasField(row, LowerField) ? row.Get(LowerField) : null, ref min, ref max);
        }

        // An empty or zero-width range cannot define an axis (a [v, v] domain maps every value to one
        // point) and the Y values are auto-fitted already, so nothing is installed for it.
        if (min >= max) return;
        ScaleContributionHelper.ContributeRange(scales, YChannel, min, max);
    }

    /// <summary>
    /// Widen the running bounds with one optional value. A missing, non-numeric or non-finite value
    /// is skipped: it has no position on the axis and must not define the domain.
    /// </summary>
    private static void ExtendBounds(object? value, ref double min, ref double max)
    {
        if (value is null) return;
        if (!ScaleConvert.TryToDouble(value, out double bound)) return;

        if (bound < min) min = bound;
        if (bound > max) max = bound;
    }

    /// <summary>
    /// Screen bounds of one band slice, shared by <see cref="Render"/> and <see cref="HitTest"/>: the two
    /// edges ordered top-to-bottom, then pulled toward their midpoint by the entry animation. Hit testing
    /// with the un-animated edges used to accept a point the band does not cover yet.
    /// </summary>
    /// <param name="rawUpperY">Screen Y of the upper bound.</param>
    /// <param name="rawLowerY">Screen Y of the lower bound.</param>
    /// <param name="anim">Entry animation progress.</param>
    private static (float top, float bottom) BandBounds(float rawUpperY, float rawLowerY, float anim)
    {
        // Ensure upper is visually above lower (smaller Y in screen coords)
        float topY = Math.Min(rawUpperY, rawLowerY);
        float botY = Math.Max(rawUpperY, rawLowerY);
        float midY = (topY + botY) / 2f;
        return (midY + (topY - midY) * anim, midY + (botY - midY) * anim);
    }

    /// <summary>
    /// Screen geometry of one row's band, or false when the row cannot be drawn. <see cref="Render"/> and
    /// <see cref="HitTest"/> share it on purpose: a row one of them skips must be skipped by both, or the
    /// pointer answers with a band that was never painted. A lower bound that is missing, explicitly null or
    /// not a number is the case that used to slip through the hit test - it mapped the null as 0 and reported a
    /// band from the row's value down to the bottom of the axis.
    /// </summary>
    /// <param name="ctx">Context of the frame.</param>
    /// <param name="row">Row to place.</param>
    /// <param name="xScale">The mark's X scale.</param>
    /// <param name="yScale">The mark's value scale.</param>
    /// <param name="anim">Entry animation progress.</param>
    /// <param name="point">Screen x of the row plus the animated top and bottom of its band.</param>
    private bool TryBandPoint(MarkContext ctx, DataRow row, IScale xScale, IScale yScale, float anim,
        out (float Px, float Top, float Bottom) point)
    {
        point = default;
        var xRaw = ctx.Encodes.Resolve(Channel.X, row);
        var yRaw = ctx.Encodes.Resolve(YChannel, row);
        if (xRaw == null || yRaw == null) return false;
        // The pattern form, not `is null`: TryGet<object> is annotated as answering a non-null value, while a row
        // may hold an explicit null ("named null" is a value the library accepts).
        if (!row.TryGet<object>(LowerField, out var lowerValue) || lowerValue is not { } lowerRaw) return false;

        // Both mapped values have to be usable numbers: TryToDouble rejects null, a value that is not a number
        // (a colour string in a numeric column) and a non-finite number - none of them has a position on the
        // axis, and an infinite bound used to produce non-finite coordinates (the scale maps it, nothing clamps
        // it). MapSafely keeps the mapping itself from raising: ScaleConvert.ToDouble would throw and cost the
        // whole band, since the render stage catches the exception and logs a single error.
        double xNorm = MapSafely(xScale, xRaw);
        double upperNorm = MapSafely(yScale, yRaw);
        double lowerNorm = MapSafely(yScale, lowerRaw);
        if (!double.IsFinite(xNorm) || !double.IsFinite(upperNorm) || !double.IsFinite(lowerNorm)) return false;

        float px = ctx.Plot.MapX((float)xNorm);
        var (top, bottom) = BandBounds(ctx.Plot.MapY((float)upperNorm), ctx.Plot.MapY((float)lowerNorm), anim);
        point = (px, top, bottom);
        return true;
    }

    /// <inheritdoc />
    public override void Render(MarkContext ctx)
    {
        if (ctx.Data.Count < 2) return;

        var xScale = ctx.Scales.TryGet(Channel.X);
        var yScale = ctx.Scales.TryGet(YChannel) as LinearScale;
        if (xScale == null || yScale == null) return;

        float anim = ComputeAnimProgress(ctx);
        // Use the first *visible* row and its real index: the override callback used to always
        // receive index 0 (and a hidden row could drive the colour).
        int firstIndex = -1;
        for (int i = 0; i < ctx.Data.Count; i++)
        {
            if (IsSeriesHidden(ctx, ctx.Data[i])) continue;
            firstIndex = i;
            break;
        }
        if (firstIndex < 0) return;

        var color = ResolveFill(ctx, ctx.Data[firstIndex], firstIndex, GetDefaultColor(ctx));
        // Follow the focused-series dimming like the other marks do.
        float opacity = ComputeSeriesOpacity(
            ctx, ResolveSeriesKey(ctx, ctx.Data[firstIndex]), ctx.Animation.GlobalOpacity);

        // Collect upper and lower points with global index mapping
        var upperPts = new List<(float x, float y)>();
        var lowerPts = new List<(float x, float y)>();
        var pointIndices = new List<int>();

        for (int i = 0; i < ctx.Data.Count; i++)
        {
            var row = ctx.Data[i];
            if (IsSeriesHidden(ctx, row)) continue;
            if (!TryBandPoint(ctx, row, xScale, yScale, anim, out var point)) continue;

            upperPts.Add((point.Px, point.Top));
            lowerPts.Add((point.Px, point.Bottom));
            pointIndices.Add(i);
        }

        if (upperPts.Count < 2) return;

        // Fill: upper line forward, then lower line reversed → closed shape
        var fillPath = ShapePath(ctx);
        var fillPaint = ShapePaint(ctx);
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
            var strokePaint = ShapePaint(ctx);
            strokePaint.SetColor(color).SetStrokeWidth(StrokeWidth).SetAntiAlias(true).SetOpacity(opacity);

            void DrawEdge(List<(float x, float y)> pts)
            {
                var edgePath = ShapePath(ctx);
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
                var ptPath = ShapePath(ctx);
                var ptPaint = ShapePaint(ctx);
                ptPath.Circle(upperPts[ptIdx].x, upperPts[ptIdx].y, (ctx.Theme ?? ChartTheme.Default).RangeAreaPointRadius);
                ptPath.Circle(lowerPts[ptIdx].x, lowerPts[ptIdx].y, (ctx.Theme ?? ChartTheme.Default).RangeAreaPointRadius);
                ptPaint.SetColor(color).SetAntiAlias(true).SetOpacity(opacity);
                ctx.Canvas.Fill(ptPath, ptPaint);
            }
        }

        // Selection: ring both bounds of the selected row (States.SelectedStroke / width).
        if (ctx.SelectedRowIndex >= 0)
        {
            int selIdx = pointIndices.IndexOf(ctx.SelectedRowIndex);
            if (selIdx >= 0 && selIdx < upperPts.Count)
            {
                float ringR = (ctx.Theme ?? ChartTheme.Default).RangeAreaPointRadius;
                var selPath = ShapePath(ctx);
                var selPaint = ShapePaint(ctx);
                selPath.Circle(upperPts[selIdx].x, upperPts[selIdx].y, ringR);
                selPath.Circle(lowerPts[selIdx].x, lowerPts[selIdx].y, ringR);
                ApplySelectionPaint(ctx, selPaint, opacity);
                ctx.Canvas.Stroke(selPath, selPaint);
            }
        }
    }

    /// <inheritdoc />
    public override HitResult? HitTest(MarkContext ctx, Vector2 screenPos)
    {
        if (ctx.Data.Count < 2) return null;

        var xScale = ctx.Scales.TryGet(Channel.X);
        var yScale = ctx.Scales.TryGet(YChannel) as LinearScale;
        if (xScale == null || yScale == null) return null;

        float bestDist = (ctx.Theme ?? ChartTheme.Default).HitTestAreaSnapDistance; // snap distance
        int bestIdx = -1;
        float anim = ComputeAnimProgress(ctx);

        for (int i = 0; i < ctx.Data.Count; i++)
        {
            var row = ctx.Data[i];
            if (IsSeriesHidden(ctx, row)) continue;
            // Only a row that was painted can be hit: the same guard Render passes through.
            if (!TryBandPoint(ctx, row, xScale, yScale, anim, out var point)) continue;

            float dx = Math.Abs(screenPos.X - point.Px);
            if (dx < bestDist)
            {
                bestDist = dx;
                bestIdx = i;
            }
        }

        if (bestIdx < 0) return null;

        var hitRow = ctx.Data[bestIdx];
        if (!TryBandPoint(ctx, hitRow, xScale, yScale, anim, out var band)) return null;

        // Verify Y position is within upper-lower band, using the same animated bounds Render draws.
        if (screenPos.Y < band.Top || screenPos.Y > band.Bottom) return null;

        var hitX = ctx.Encodes.Resolve(Channel.X, hitRow);
        var hitY = ctx.Encodes.Resolve(YChannel, hitRow);
        return new HitResult
        {
            Hit = true, Row = hitRow, RowIndex = bestIdx,
            ScreenX = band.Px,
            ScreenY = screenPos.Y,
            Label = $"{hitX}: {hitRow.Get(LowerField)}–{hitY}",
            MarkType = nameof(RangeAreaMark),
        };
    }
}
