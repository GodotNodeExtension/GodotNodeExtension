using System;
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

    /// <summary>Screen position of one lollipop (stem end and dot centre).</summary>
    /// <param name="Cx">Screen X of the dot.</param>
    /// <param name="Cy">Screen Y of the dot.</param>
    private readonly record struct RowGeometry(float Cx, float Cy);

    /// <summary>
    /// Screen position of one row, or <c>false</c> when the row cannot be drawn: hidden, missing its
    /// category or value, or carrying a value the scale cannot read (no screen position).
    /// <para>
    /// <see cref="Render"/> walks the whole table through this and <see cref="RenderOverlay"/> asks for
    /// the interactive rows only: both draw the stem and the dot from the same numbers, so the highlight
    /// cannot drift away from the row it belongs to.
    /// </para>
    /// </summary>
    /// <param name="ctx">Context of the frame.</param>
    /// <param name="ordinalScale">Scale of the category axis.</param>
    /// <param name="valueScale">Scale of the value axis.</param>
    /// <param name="horizontal">True for the horizontal orientation (the two axes swap).</param>
    /// <param name="index">Index of the row in the rendered data.</param>
    /// <param name="baseline">Screen position of the zero line (the stem's other end).</param>
    /// <param name="anim">Entry animation progress.</param>
    /// <param name="geometry">Geometry of the row when the method returns true.</param>
    private bool TryRowGeometry(MarkContext ctx, OrdinalScale ordinalScale, LinearScale valueScale,
        bool horizontal, int index, float baseline, float anim, out RowGeometry geometry)
    {
        geometry = default;
        if (index < 0 || index >= ctx.Data.Count) return false;

        var row = ctx.Data[index];
        if (IsSeriesHidden(ctx, row)) return false;
        var xRaw = ctx.Encodes.Resolve(Channel.X, row);
        var yRaw = ctx.Encodes.Resolve(YChannel, row);
        if (xRaw == null || yRaw == null) return false;

        // A non-finite value - or one the scale cannot read at all - has no screen position: skip the
        // row instead of drawing at NaN.
        double valueNorm = MapSafely(valueScale, horizontal ? xRaw : yRaw);
        if (!double.IsFinite(valueNorm)) return false;

        // Map to screen coordinates based on orientation
        if (horizontal)
        {
            float cy = ctx.Plot.MapY((float)ordinalScale.Map(yRaw));
            float target = ctx.Plot.MapX((float)valueNorm);
            geometry = new RowGeometry(baseline + (target - baseline) * anim, cy);
        }
        else
        {
            float cx = ctx.Plot.MapX((float)ordinalScale.Map(xRaw));
            float target = ctx.Plot.MapY((float)valueNorm);
            geometry = new RowGeometry(cx, baseline + (target - baseline) * anim);
        }
        return true;
    }

    /// <summary>
    /// Draw one lollipop: its stem, its dot and (for the selected row) the selection ring. The fill the
    /// caller passes in is the one the pass owns: the data layer hands over what
    /// <see cref="Mark.ResolveFill"/> answered (the stem and the dot are painted with the same colour, as
    /// they always were), and the overlay pass hands over <see cref="Mark.ActiveFillOf"/> for the hovered
    /// row.
    /// </summary>
    /// <param name="ctx">Context of the frame.</param>
    /// <param name="geometry">Position of the lollipop, from <see cref="TryRowGeometry"/>.</param>
    /// <param name="baseline">Screen position of the zero line (the stem's other end).</param>
    /// <param name="horizontal">True for the horizontal orientation.</param>
    /// <param name="shape">Symbol of the dot.</param>
    /// <param name="color">Colour of the stem and the dot.</param>
    /// <param name="opacity">Opacity of the element.</param>
    /// <param name="hovered">True when the row is the hovered one (its dot is enlarged here).</param>
    /// <param name="selected">True when the row is the selected one (its ring is drawn here).</param>
    private void DrawLollipop(MarkContext ctx, RowGeometry geometry, float baseline, bool horizontal,
        ShapeKind shape, Color color, float opacity, bool hovered, bool selected)
    {
        float cx = geometry.Cx;
        float cy = geometry.Cy;

        // Stem line
        var stemPath = ShapePath(ctx);
        var stemPaint = ShapePaint(ctx);
        if (horizontal) { stemPath.MoveTo(baseline, cy); stemPath.LineTo(cx, cy); }
        else            { stemPath.MoveTo(cx, baseline); stemPath.LineTo(cx, cy); }
        stemPaint.SetColor(color).SetStrokeWidth(StemWidth).SetAntiAlias(true).SetOpacity(opacity);
        ctx.Canvas.Stroke(stemPath, stemPaint);

        // Dot: same shape vocabulary as PointMark (Shape channel / shape scale)
        float dotR = hovered ? DotRadius * HoverScaled(ctx, (ctx.Theme ?? ChartTheme.Default).LollipopHoverScale) : DotRadius;
        var dotPath = ShapePath(ctx);
        var dotPaint = ShapePaint(ctx);
        ShapeGeometry.Build(dotPath, shape, cx, cy, dotR);
        dotPaint.SetColor(color).SetAntiAlias(true).SetOpacity(opacity);
        ctx.Canvas.Fill(dotPath, dotPaint);

        // Selected: highlight ring
        if (selected)
        {
            var ringPaint = ShapePaint(ctx);
            ApplySelectionPaint(ctx, ringPaint, opacity);
            ctx.Canvas.Stroke(dotPath, ringPaint);
        }
    }

    /// <summary>
    /// The hover visual of a lollipop is a bigger dot plus the brightened stem and dot, all opaque, so the
    /// overlay pass can redraw the element exactly as the data layer drew it without touching the layer.
    /// </summary>
    public override bool InteractionStateInOverlay => true;

    /// <inheritdoc />
    public override void Render(MarkContext ctx)
    {
        bool horizontal = Orientation == BarOrientation.Horizontal;

        OrdinalScale? ordinalScale;
        LinearScale?  valueScale;
        if (horizontal)
        {
            ordinalScale = ctx.Scales.TryGet(YChannel) as OrdinalScale ?? ctx.Scales.TryGet(Channel.X) as OrdinalScale;
            valueScale   = ctx.Scales.TryGet(Channel.X) as LinearScale ?? ctx.Scales.TryGet(YChannel) as LinearScale;
        }
        else
        {
            ordinalScale = ctx.Scales.TryGet(Channel.X) as OrdinalScale;
            valueScale   = ctx.Scales.TryGet(YChannel) as LinearScale;
        }
        if (ordinalScale == null || valueScale == null) return;
        if (ordinalScale.Domain.Count == 0) return;

        float anim = ComputeAnimProgress(ctx);

        // Baseline is the data zero line (clamped to the plot) — the same normalized value the hit
        // test uses, so the stem/dot and the hit area always share one anchor even when the whole
        // value domain sits below zero.
        float zeroNorm = Math.Clamp((float)valueScale.Map(0), 0f, 1f);
        float baseline = horizontal ? ctx.Plot.MapX(zeroNorm) : ctx.Plot.MapY(zeroNorm);

        // The interaction state stays in the data layer only while the chart does not cache it; with the
        // cache on the overlay paints it (see InteractionStateInOverlay / RenderOverlay).
        bool stateHere = !ctx.StateInOverlay;

        // Data labels, the same contract as the other Cartesian marks: one element per row, drawn only while
        // ShowLabel is on (it is off by default). The label's own text is the value the stem is drawn from -
        // the X value for a horizontal lollipop, the Y value otherwise - and LabelPosition decides where it
        // sits relative to the dot.
        var labels = BeginLabelCollection();

        for (int i = 0; i < ctx.Data.Count; i++)
        {
            if (!TryRowGeometry(ctx, ordinalScale, valueScale, horizontal, i, baseline, anim, out var geometry))
                continue;

            var row = ctx.Data[i];
            var color = ResolveFill(ctx, row, i, GetDefaultColor(ctx));
            float opacity = ComputeElementOpacity(ctx, row, i);
            var shape = ResolveShape(ctx, row);

            DrawLollipop(ctx, geometry, baseline, horizontal, shape, color, opacity,
                hovered: stateHere && i == ctx.HoveredRowIndex,
                selected: stateHere && i == ctx.SelectedRowIndex);

            if (labels != null)
            {
                object? value = horizontal ? ctx.Encodes.Resolve(Channel.X, row) : ctx.Encodes.Resolve(YChannel, row);
                object? other = horizontal ? ctx.Encodes.Resolve(YChannel, row) : ctx.Encodes.Resolve(Channel.X, row);
                labels.Add(new LabelElement(geometry.Cx, geometry.Cy, FormatLabel(LabelFormat, value, other),
                    opacity, row, i, color, LabelValue(value)));
            }
        }

        if (labels != null)
            DrawLabels(ctx, labels);
    }

    /// <inheritdoc />
    public override void RenderOverlay(MarkContext ctx)
    {
        // Only while the chart keeps the data layer in an image: with the cache off Render painted the state.
        // The rows are read where they are needed (the loop walks the interaction rows).
        if (!OverlayRows(ctx, out _, out _)) return;

        var (ordinalScale, valueScale, horizontal, baseline) = ResolveAxes(ctx);
        if (ordinalScale == null || valueScale == null) return;

        float anim = ComputeAnimProgress(ctx);

        // Only the interactive rows are drawn, from the same geometry the data layer used.
        foreach (int index in InteractionRows(ctx))
        {
            DrawInteractive(ctx, ordinalScale, valueScale, horizontal, baseline, anim, index,
                            index == ctx.HoveredRowIndex);
        }
    }

    /// <summary>Category/value axes plus the orientation and the zero line, as <see cref="Render"/> reads them.</summary>
    private (OrdinalScale? Ordinal, LinearScale? Value, bool Horizontal, float Baseline) ResolveAxes(MarkContext ctx)
    {
        bool horizontal = Orientation == BarOrientation.Horizontal;

        OrdinalScale? ordinalScale;
        LinearScale?  valueScale;
        if (horizontal)
        {
            ordinalScale = ctx.Scales.TryGet(YChannel) as OrdinalScale ?? ctx.Scales.TryGet(Channel.X) as OrdinalScale;
            valueScale   = ctx.Scales.TryGet(Channel.X) as LinearScale ?? ctx.Scales.TryGet(YChannel) as LinearScale;
        }
        else
        {
            ordinalScale = ctx.Scales.TryGet(Channel.X) as OrdinalScale;
            valueScale   = ctx.Scales.TryGet(YChannel) as LinearScale;
        }

        float zeroNorm = valueScale == null ? 0f : Math.Clamp((float)valueScale.Map(0), 0f, 1f);
        float baseline = horizontal ? ctx.Plot.MapX(zeroNorm) : ctx.Plot.MapY(zeroNorm);
        return (ordinalScale, valueScale, horizontal, baseline);
    }

    /// <summary>
    /// Draw one interactive lollipop on the overlay pass: the hovered one takes the active fill and the
    /// enlarged dot, the selected one its ring.
    /// </summary>
    private void DrawInteractive(MarkContext ctx, OrdinalScale ordinalScale, LinearScale valueScale,
        bool horizontal, float baseline, float anim, int index, bool hovered)
    {
        if (!TryRowGeometry(ctx, ordinalScale, valueScale, horizontal, index, baseline, anim, out var geometry))
            return;

        var row = ctx.Data[index];
        var color = ResolveFill(ctx, row, index, GetDefaultColor(ctx));
        if (hovered) color = ActiveFillOf(ctx, color);
        float opacity = ComputeElementOpacity(ctx, row, index);

        DrawLollipop(ctx, geometry, baseline, horizontal, ResolveShape(ctx, row), color, opacity,
            hovered: hovered, selected: index == ctx.SelectedRowIndex);
    }

    /// <inheritdoc />
    public override HitResult? HitTest(MarkContext ctx, Vector2 screenPos)
    {
        bool horizontal = Orientation == BarOrientation.Horizontal;

        IScale? ordinalScale, valueScale;
        if (horizontal)
        {
            ordinalScale = ctx.Scales.TryGet(YChannel) as OrdinalScale ?? ctx.Scales.TryGet(Channel.X) as OrdinalScale;
            valueScale   = ctx.Scales.TryGet(Channel.X) as LinearScale ?? ctx.Scales.TryGet(YChannel) as LinearScale;
        }
        else
        {
            ordinalScale = ctx.Scales.TryGet(Channel.X) as OrdinalScale;
            valueScale   = ctx.Scales.TryGet(YChannel) as LinearScale;
        }
        if (ordinalScale == null || valueScale == null) return null;

        // The dot position is animated in Render (it grows out of the zero line): use the same
        // progress and the same baseline here so a hit cannot land on a point that is not drawn yet.
        float anim = ComputeAnimProgress(ctx);
        float zeroNorm = Math.Clamp((float)valueScale.Map(0), 0f, 1f);
        float baseline = horizontal ? ctx.Plot.MapX(zeroNorm) : ctx.Plot.MapY(zeroNorm);

        for (int i = 0; i < ctx.Data.Count; i++)
        {
            var row = ctx.Data[i];
            if (IsSeriesHidden(ctx, row)) continue;
            var xRaw = ctx.Encodes.Resolve(Channel.X, row);
            var yRaw = ctx.Encodes.Resolve(YChannel, row);
            if (xRaw == null || yRaw == null) continue;

            // Mirror the Render() guard: rows that are never drawn are not hit-testable either.
            double valueNorm = MapSafely(valueScale, horizontal ? xRaw : yRaw);
            if (!double.IsFinite(valueNorm)) continue;

            float cx, cy;
            if (horizontal)
            {
                cy = ctx.Plot.MapY((float)ordinalScale.Map(yRaw));
                float targetX = ctx.Plot.MapX((float)valueNorm);
                cx = baseline + (targetX - baseline) * anim;
            }
            else
            {
                cx = ctx.Plot.MapX((float)ordinalScale.Map(xRaw));
                float targetY = ctx.Plot.MapY((float)valueNorm);
                cy = baseline + (targetY - baseline) * anim;
            }

            // Same radius as the drawn dot: Render enlarges a hovered dot, so the hit area follows it
            // instead of stopping at the resting radius.
            float dotR = DotRadius;
            if (i == ctx.HoveredRowIndex)
                dotR *= HoverScaled(ctx, (ctx.Theme ?? ChartTheme.Default).LollipopHoverScale);

            float dist = screenPos.DistanceTo(new Vector2(cx, cy));
            if (dist <= dotR + (ctx.Theme ?? ChartTheme.Default).HitTestPointPadding)
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
