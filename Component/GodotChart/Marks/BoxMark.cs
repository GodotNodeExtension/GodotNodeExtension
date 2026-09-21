using System;
using System.Collections.Generic;
using Godot;

namespace GodotNodeExtension.Component.GodotChart.Marks;

/// <summary>
/// Box plot mark. Renders box-and-whisker diagrams showing data distribution.
/// Requires fields: X = category, plus five statistical fields configured via properties:
/// MinField, Q1Field, MedianField, Q3Field, MaxField.
/// <para>
/// The quartiles and the outlier range come from the upstream data through those fields: this mark
/// neither computes them from raw samples nor applies a whisker rule. A non-finite field makes the row
/// undrawable, and a box that would be thinner than a pixel (a constant series, Q1 == Q3) is drawn one
/// pixel tall so it stays visible.
/// </para>
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
    public float CornerRadius { get; set; } = 3f;

    /// <summary>Default box fill color. Null = use theme color.</summary>
    public Color? BoxColor { get; set; }

    /// <summary>Median line and whisker color. Null = use theme color.</summary>
    public Color? LineColor { get; set; }

    /// <summary>Resolve box fill color considering theme fallback.</summary>
    private Color ResolveBoxColor(MarkContext ctx) =>
        BoxColor ?? (ctx.Theme ?? ChartTheme.Default).BoxFillColor;

    /// <summary>Resolve line color considering theme fallback.</summary>
    private Color ResolveLineColor(MarkContext ctx) =>
        LineColor ?? (ctx.Theme ?? ChartTheme.Default).BoxLineColor;

    /// <inheritdoc />
    public override void ContributeScales(ScaleSet scales, EncodeSet encodes, List<DataRow> data)
    {
        ScaleContributionHelper.ContributeMinMaxScale(scales, data, MinField, MaxField, YChannel);
    }

    /// <summary>
    /// Top edge and height of the box, shared by the fill and the selection ring. The height has a one
    /// pixel floor: a constant series (Q1 == Q3, or a fully animated box at progress 0) is otherwise
    /// invisible.
    /// </summary>
    /// <param name="q1Y">Screen Y of the first quartile.</param>
    /// <param name="q3Y">Screen Y of the third quartile.</param>
    private static (float top, float height) BoxRect(float q1Y, float q3Y)
        => (Math.Min(q1Y, q3Y), MathF.Max(Math.Abs(q3Y - q1Y), 1f));

    /// <summary>
    /// Screen geometry of one box of the frame, from the row and the scales.
    /// <para>
    /// <see cref="Render"/> walks the whole table through this and <see cref="RenderOverlay"/> asks for
    /// the interactive rows only: both draw the box from the same numbers, so the highlight cannot
    /// drift away from the box it belongs to.
    /// </para>
    /// </summary>
    /// <param name="Cx">Screen X of the category centre.</param>
    /// <param name="HalfBox">Half the drawn box width.</param>
    /// <param name="Q1Y">Screen Y of the first quartile (animation applied).</param>
    /// <param name="Q3Y">Screen Y of the third quartile (animation applied).</param>
    /// <param name="MedianY">Screen Y of the median.</param>
    /// <param name="MinY">Screen Y of the minimum (lower whisker end).</param>
    /// <param name="MaxY">Screen Y of the maximum (upper whisker end).</param>
    private readonly record struct RowGeometry(
        float Cx, float HalfBox, float Q1Y, float Q3Y, float MedianY, float MinY, float MaxY);

    /// <summary>
    /// Screen geometry of one row, or <c>false</c> when the row cannot be drawn at all: hidden, missing
    /// one of the five statistical fields, carrying a non-finite quartile, or standing on a category the
    /// X scale does not know (<see cref="OrdinalScale.Map"/> answers 0 for it - the *first* category -
    /// so the box would be drawn on top of another one).
    /// </summary>
    /// <param name="ctx">Context of the frame.</param>
    /// <param name="xScale">Category scale of the box.</param>
    /// <param name="yScale">Value scale of the box.</param>
    /// <param name="index">Index of the row in the rendered data.</param>
    /// <param name="boxW">Drawn box width.</param>
    /// <param name="anim">Entry animation progress.</param>
    /// <param name="geometry">Geometry of the row when the method returns true.</param>
    private bool TryRowGeometry(MarkContext ctx, OrdinalScale xScale, LinearScale yScale,
        int index, float boxW, float anim, out RowGeometry geometry)
    {
        geometry = default;
        if (index < 0 || index >= ctx.Data.Count) return false;

        var row = ctx.Data[index];
        if (IsSeriesHidden(ctx, row)) return false;
        var xRaw = ctx.Encodes.Resolve(Channel.X, row);
        if (xRaw == null) return false;
        if (!HasFields(row, MinField, Q1Field, MedianField, Q3Field, MaxField)) return false;

        double vMin    = GetDouble(row, MinField);
        double vQ1     = GetDouble(row, Q1Field);
        double vMedian = GetDouble(row, MedianField);
        double vQ3     = GetDouble(row, Q3Field);
        double vMax    = GetDouble(row, MaxField);
        // A non-finite quartile (NaN/Infinity) has no screen position: skip the whole row.
        if (!double.IsFinite(vMin) || !double.IsFinite(vQ1) || !double.IsFinite(vMedian)
            || !double.IsFinite(vQ3) || !double.IsFinite(vMax))
            return false;

        // A category the X scale does not know has no position: OrdinalScale.Map answers 0 for it.
        if (xScale.IndexOf(xRaw.ToString() ?? "") < 0) return false;

        float xNorm = (float)xScale.Map(xRaw);
        float cx = ctx.Plot.MapX(xNorm);

        // Map Y values — animate from median outward
        float medianY = ctx.Plot.MapY((float)yScale.Map(vMedian));
        float q1Y  = medianY + (ctx.Plot.MapY((float)yScale.Map(vQ1)) - medianY) * anim;
        float q3Y  = medianY + (ctx.Plot.MapY((float)yScale.Map(vQ3)) - medianY) * anim;
        float minY = medianY + (ctx.Plot.MapY((float)yScale.Map(vMin)) - medianY) * anim;
        float maxY = medianY + (ctx.Plot.MapY((float)yScale.Map(vMax)) - medianY) * anim;

        geometry = new RowGeometry(cx, boxW / 2f, q1Y, q3Y, medianY, minY, maxY);
        return true;
    }

    /// <summary>
    /// Draw one box: the body, the median line and the two whiskers, plus the selection ring when the row
    /// is the selected one. The fill the caller passes in is the one the pass owns: the data layer hands
    /// over what <see cref="Mark.ResolveFill"/> answered (the hover look while it owns the state), and the
    /// overlay pass hands over <see cref="Mark.ActiveFillOf"/> for the hovered box.
    /// </summary>
    /// <param name="ctx">Context of the frame.</param>
    /// <param name="geometry">Geometry of the box, from <see cref="TryRowGeometry"/>.</param>
    /// <param name="boxW">Drawn box width.</param>
    /// <param name="color">Fill the box body is painted with.</param>
    /// <param name="opacity">Opacity of the box.</param>
    /// <param name="selected">True when the row is the selected one (its ring is drawn here).</param>
    private void DrawBox(MarkContext ctx, RowGeometry geometry, float boxW, Color color, float opacity,
        bool selected)
    {
        float cx = geometry.Cx;
        float halfBox = geometry.HalfBox;
        float q1Y = geometry.Q1Y;
        float q3Y = geometry.Q3Y;
        float medianY = geometry.MedianY;

        // Box (Q1 to Q3), never thinner than a pixel so a constant series still shows it.
        var boxPath = ShapePath(ctx);
        var boxPaint = ShapePaint(ctx);
        {
            var (boxTop, boxH) = BoxRect(q1Y, q3Y);
            if (CornerRadius > 0)
                boxPath.RoundRect(cx - halfBox, boxTop, boxW, boxH, CornerRadius);
            else
                boxPath.Rect(cx - halfBox, boxTop, boxW, boxH);
            boxPaint.SetColor(color).SetAntiAlias(true).SetOpacity(opacity);
            ctx.Canvas.Fill(boxPath, boxPaint);
        }

        var linePaint = ShapePaint(ctx);
        linePaint.SetColor(ResolveLineColor(ctx)).SetStrokeWidth(WhiskerWidth).SetAntiAlias(true).SetOpacity(opacity);

        // Median line
        var medPath = ShapePath(ctx);
        {
            medPath.MoveTo(cx - halfBox, medianY);
            medPath.LineTo(cx + halfBox, medianY);
            ctx.Canvas.Stroke(medPath, linePaint);
        }

        // Upper whisker (Q3 to Max)
        var upPath = ShapePath(ctx);
        {
            upPath.MoveTo(cx, q3Y);
            upPath.LineTo(cx, geometry.MaxY);
            upPath.MoveTo(cx - halfBox * 0.5f, geometry.MaxY);
            upPath.LineTo(cx + halfBox * 0.5f, geometry.MaxY);
            ctx.Canvas.Stroke(upPath, linePaint);
        }

        // Lower whisker (Q1 to Min)
        var downPath = ShapePath(ctx);
        {
            downPath.MoveTo(cx, q1Y);
            downPath.LineTo(cx, geometry.MinY);
            downPath.MoveTo(cx - halfBox * 0.5f, geometry.MinY);
            downPath.LineTo(cx + halfBox * 0.5f, geometry.MinY);
            ctx.Canvas.Stroke(downPath, linePaint);
        }

        // Selection ring
        if (selected)
        {
            var selPath = ShapePath(ctx);
            var selPaint = ShapePaint(ctx);
            var (boxTop, boxH) = BoxRect(q1Y, q3Y);
            if (CornerRadius > 0)
                selPath.RoundRect(cx - halfBox, boxTop, boxW, boxH, CornerRadius);
            else
                selPath.Rect(cx - halfBox, boxTop, boxW, boxH);
            ApplySelectionPaint(ctx, selPaint, opacity);
            ctx.Canvas.Stroke(selPath, selPaint);
        }
    }

    /// <summary>
    /// The hover fill of a box is the brightened body colour, so it can be painted on the overlay pass
    /// without touching the cached layer: the box body is opaque and the overlay redraws exactly the
    /// rectangle the layer already holds.
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
        float boxW  = slotW * BoxWidthRatio;
        float anim  = ComputeAnimProgress(ctx);

        // The interaction state stays in the data layer only while the chart does not cache it; with the
        // cache on the overlay paints it (see InteractionStateInOverlay / RenderOverlay).
        bool stateHere = !ctx.StateInOverlay;

        for (int i = 0; i < ctx.Data.Count; i++)
        {
            if (!TryRowGeometry(ctx, xScale, yScale, i, boxW, anim, out var geometry)) continue;

            var row = ctx.Data[i];
            var color = ResolveFill(ctx, row, i, ResolveBoxColor(ctx));
            float opacity = ComputeElementOpacity(ctx, row, i);

            DrawBox(ctx, geometry, boxW, color, opacity, selected: stateHere && i == ctx.SelectedRowIndex);
        }
    }

    /// <inheritdoc />
    public override void RenderOverlay(MarkContext ctx)
    {
        // Only while the chart keeps the data layer in an image: with the cache off Render painted the state.
        if (!ctx.StateInOverlay) return;

        int hovered = ctx.HoveredRowIndex;
        int selected = ctx.SelectedRowIndex;
        if (hovered < 0 && selected < 0) return;

        var xScale = ctx.Scales.TryGet(Channel.X) as OrdinalScale;
        var yScale = ctx.Scales.TryGet(YChannel) as LinearScale;
        if (xScale == null || yScale == null || xScale.Domain.Count == 0) return;

        float boxW = ctx.Plot.Width / xScale.Domain.Count * BoxWidthRatio;
        float anim = ComputeAnimProgress(ctx);

        // Only the interactive rows are drawn, from the same geometry the data layer used.
        if (hovered >= 0) DrawInteractive(ctx, xScale, yScale, hovered, boxW, anim, hovered: true);
        if (selected >= 0 && selected != hovered)
            DrawInteractive(ctx, xScale, yScale, selected, boxW, anim, hovered: false);
    }

    /// <summary>
    /// Draw one interactive box on the overlay pass: the box body takes the hover fill for the hovered
    /// row (the data layer answered the default fill while the state lives here) and the selection ring
    /// is added for the selected one.
    /// </summary>
    private void DrawInteractive(MarkContext ctx, OrdinalScale xScale, LinearScale yScale, int index,
        float boxW, float anim, bool hovered)
    {
        if (!TryRowGeometry(ctx, xScale, yScale, index, boxW, anim, out var geometry)) return;

        var row = ctx.Data[index];
        var color = ResolveFill(ctx, row, index, ResolveBoxColor(ctx));
        if (hovered) color = ActiveFillOf(ctx, color);
        float opacity = ComputeElementOpacity(ctx, row, index);

        DrawBox(ctx, geometry, boxW, color, opacity, selected: index == ctx.SelectedRowIndex);
    }

    /// <inheritdoc />
    public override HitResult? HitTest(MarkContext ctx, Vector2 screenPos)
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
            // Mirror the Render() guard so that rows which are never drawn are not hit-testable either.
            if (!HasFields(row, MinField, Q1Field, MedianField, Q3Field, MaxField)) continue;
            var xRaw = ctx.Encodes.Resolve(Channel.X, row);
            if (xRaw == null) continue;

            double vMin = GetDouble(row, MinField);
            double vMax = GetDouble(row, MaxField);
            double vMedian = GetDouble(row, MedianField);
            double vQ1 = GetDouble(row, Q1Field);
            double vQ3 = GetDouble(row, Q3Field);
            // Non-finite quartiles are never drawn: skip the row here too.
            if (!double.IsFinite(vMin) || !double.IsFinite(vQ1) || !double.IsFinite(vMedian)
                || !double.IsFinite(vQ3) || !double.IsFinite(vMax))
                continue;

            // A category the X scale does not know has no position: OrdinalScale.Map answers 0 for it, which
            // is the *first* category, so the box would be drawn on top of another one. Skip the row.
            if (xScale.IndexOf(xRaw.ToString() ?? "") < 0) continue;

            float xNorm = (float)xScale.Map(xRaw);
            float cx = ctx.Plot.MapX(xNorm);
            // Apply animation: expand from median outward, consistent with Render
            float medianY = ctx.Plot.MapY((float)yScale.Map(vMedian));
            float minY = medianY + (ctx.Plot.MapY((float)yScale.Map(vMin)) - medianY) * anim;
            float maxY = medianY + (ctx.Plot.MapY((float)yScale.Map(vMax)) - medianY) * anim;
            float top = Math.Min(minY, maxY);
            float bottom = Math.Max(minY, maxY);

            // The same tolerance the candle uses: a box and a candle are both "category slot plus pixel box",
            // and BoxMark was the one that demanded a pixel-exact hit while its neighbour allowed 4px.
            float padding = (ctx.Theme ?? ChartTheme.Default).HitTestPointPadding;
            if (screenPos.X >= cx - boxW / 2f - padding && screenPos.X <= cx + boxW / 2f + padding &&
                screenPos.Y >= top - padding && screenPos.Y <= bottom + padding)
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
