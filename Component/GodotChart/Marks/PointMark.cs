using System.Collections.Generic;
using Godot;
using GodotNodeExtension.Component.GodotChart.Canvas;

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

    /// <summary>
    /// Radius in pixels for the bottom of the size range. Null (the default) uses
    /// <see cref="ChartTheme.PointSizeMin"/>.
    /// </summary>
    public float? MinRadius { get; set; }

    /// <summary>
    /// Pixels added at the top of the size range, so the largest value draws at
    /// <c>MinRadius + RadiusRange</c>. Null uses <see cref="ChartTheme.PointSizeRange"/>.
    /// </summary>
    public float? RadiusRange { get; set; }

    /// <summary>
    /// The hover and selection visuals of a point (the enlarged dot and the selection ring) are painted on
    /// the overlay pass, so the data layer of a scatter chart can be kept in an image: only the interactive
    /// rows are drawn there, which is O(1) in the number of points.
    /// </summary>
    public override bool InteractionStateInOverlay => true;

    /// <summary>
    /// The opacity a point draws with when the element is not told otherwise: the theme's
    /// <see cref="ChartTheme.PointDefaultOpacity"/>. It is the *default* handed to
    /// <see cref="Mark.ComputeElementOpacity"/>, which resolves the opacity channel itself - resolving it here
    /// as well made every point look the channel up twice.
    /// </summary>
    private static float PointOpacity(MarkContext ctx) => (ctx.Theme ?? ChartTheme.Default).PointDefaultOpacity;

    /// <inheritdoc />
    public override void Render(MarkContext ctx)
    {
        var xScale = ctx.Scales.TryGet(Channel.X);
        var yScale = ctx.Scales.TryGet(YChannel);
        if (xScale is null || yScale is null) return;
        var sScale = ctx.Scales.TryGet(Channel.Size);
        float anim = ComputeAnimProgress(ctx);

        // Reused buffer: a fresh list per frame was a measurable allocation source.
        List<LabelElement>? labels = BeginLabelCollection();

        // The interaction-state visuals stay in the data layer only while the chart does not keep it; with
        // the layer cache on they belong to the overlay pass (see InteractionStateInOverlay).
        bool stateHere = !ctx.StateInOverlay;

        for (int i = 0; i < ctx.Data.Count; i++)
        {
            var row   = ctx.Data[i];
            if (IsSeriesHidden(ctx, row)) continue;
            var xRaw  = ctx.Encodes.Resolve(Channel.X, row);
            var yRaw  = ctx.Encodes.Resolve(YChannel, row);
            if (xRaw == null || yRaw == null) continue;
            var color = ResolveFill(ctx, row, i, GetDefaultColor(ctx));
            // ResolveOpacity runs *inside* ComputeElementOpacity: passing an already-resolved value would
            // look up the channel twice per element (and the second lookup is the one that counts).
            float opacity = ComputeElementOpacity(ctx, row, i, PointOpacity(ctx));
            var shape = ResolveShape(ctx, row);

            bool isHovered  = stateHere && i == ctx.HoveredRowIndex;
            bool isSelected = stateHere && i == ctx.SelectedRowIndex;

            // Non-finite (a NaN value, or one the scale cannot read at all) has no screen position: skip
            // the element.
            float xNorm = (float)MapSafely(xScale, xRaw);
            float yNorm = (float)MapSafely(yScale, yRaw);
            if (!float.IsFinite(xNorm) || !float.IsFinite(yNorm)) continue;

            float px = ctx.Plot.MapX(xNorm);
            float py = ctx.Plot.MapY(yNorm);

            DrawPoint(ctx, i, row, xRaw, yRaw, px, py, shape,
                SizeRadius(ctx, sScale, row) ?? DefaultRadius,
                isHovered, isSelected, opacity, color, anim, labels);
        }

        if (labels != null)
            DrawLabels(ctx, labels);
    }

    /// <summary>
    /// Draw one point with the interaction state it has right now (a hovered point is enlarged and painted
    /// with the active fill, a selected one gets the ring) and collect its label element.
    /// <para>
    /// <see cref="Render"/> walks the whole table through this, and <see cref="RenderOverlay"/> calls it for
    /// the interactive rows only: both draw the dot from the same numbers, so the highlight cannot drift away
    /// from it.
    /// </para>
    /// </summary>
    /// <param name="ctx">Context of the frame.</param>
    /// <param name="index">Index of the row in the rendered data.</param>
    /// <param name="row">The row itself (label formatting reads it).</param>
    /// <param name="xRaw">Resolved X value of the row.</param>
    /// <param name="yRaw">Resolved Y value of the row.</param>
    /// <param name="px">Screen X of the point.</param>
    /// <param name="py">Screen Y of the point.</param>
    /// <param name="shape">Symbol of the point (the shape channel).</param>
    /// <param name="radius">Radius before animation and hover scaling.</param>
    /// <param name="hovered">True when this is the hovered row of the frame.</param>
    /// <param name="selected">True when this is the selected row of the frame.</param>
    /// <param name="opacity">Opacity of the element.</param>
    /// <param name="color">The element's own fill (see <see cref="Mark.ResolveFill"/>).</param>
    /// <param name="anim">Entry animation progress applied to the radius.</param>
    /// <param name="labels">Label buffer to collect into, or null when the mark shows no labels.</param>
    private void DrawPoint(MarkContext ctx, int index, DataRow row, object xRaw, object yRaw,
        float px, float py, ShapeKind shape, float radius,
        bool hovered, bool selected, float opacity, Color color, float anim,
        List<LabelElement>? labels)
    {
        // Animation: scale radius
        radius *= anim;

        // Hover: enlarge (the fill highlight comes from ResolveFill / the Active state style, or from
        // ActiveFillOf while the overlay owns the state).
        if (hovered)
            radius *= HoverScaled(ctx, (ctx.Theme ?? ChartTheme.Default).PointHoverRadiusRatio * ctx.Animation.HoverScale);

        var path = ShapePath(ctx);
        var paint = ShapePaint(ctx);
        ShapeGeometry.Build(path, shape, px, py, radius);
        paint.SetColor(hovered && ctx.StateInOverlay ? ActiveFillOf(ctx, color) : color)
             .SetAntiAlias(true).SetOpacity(opacity);
        ctx.Canvas.Fill(path, paint);

        // Selected: draw highlight ring
        if (selected)
        {
            var strokePaint = ShapePaint(ctx);
            ApplySelectionPaint(ctx, strokePaint, opacity);
            ctx.Canvas.Stroke(path, strokePaint);
        }

        // Collect label element
        labels?.Add(new LabelElement(px, py - radius, FormatLabel(LabelFormat, yRaw, xRaw), opacity,
            row, index, color, LabelValue(yRaw)));
    }

    /// <inheritdoc />
    public override void RenderOverlay(MarkContext ctx)
    {
        // Only while the chart keeps the data layer in an image: with the cache off Render painted the state.
        // The rows are read where they are needed (the loop walks the interaction rows).
        if (!OverlayRows(ctx, out _, out _)) return;

        var xScale = ctx.Scales.TryGet(Channel.X);
        var yScale = ctx.Scales.TryGet(YChannel);
        if (xScale is null || yScale is null) return;
        var sScale = ctx.Scales.TryGet(Channel.Size);
        float anim = ComputeAnimProgress(ctx);

        // The data layer draws the marks clipped to the plot; the overlay repeats the clip so a dot at the
        // plot edge paints the same pixels in both modes.
        using var clip = new CanvasSaveScope(ctx.Canvas);
        ctx.Canvas.ClipRect(ctx.Plot.X, ctx.Plot.Y, ctx.Plot.Width, ctx.Plot.Height);

        // Only the interactive rows are drawn: a hover frame costs O(1) in the number of points, not O(rows).
        foreach (int index in InteractionRows(ctx))
        {
            if (index < 0 || index >= ctx.Data.Count) continue;
            var row = ctx.Data[index];
            if (IsSeriesHidden(ctx, row)) continue;

            var xRaw = ctx.Encodes.Resolve(Channel.X, row);
            var yRaw = ctx.Encodes.Resolve(YChannel, row);
            if (xRaw == null || yRaw == null) continue;

            float xNorm = (float)MapSafely(xScale, xRaw);
            float yNorm = (float)MapSafely(yScale, yRaw);
            if (!float.IsFinite(xNorm) || !float.IsFinite(yNorm)) continue;

            var color = ResolveFill(ctx, row, index, GetDefaultColor(ctx));
            float opacity = ComputeElementOpacity(ctx, row, index, PointOpacity(ctx));

            DrawPoint(ctx, index, row, xRaw, yRaw, ctx.Plot.MapX(xNorm), ctx.Plot.MapY(yNorm),
                ResolveShape(ctx, row), SizeRadius(ctx, sScale, row) ?? DefaultRadius,
                index == ctx.HoveredRowIndex, index == ctx.SelectedRowIndex,
                opacity, color, anim, null);
        }
    }


    /// <summary>
    /// Radius the Size channel gives this row, or null when the channel does not size this point (no
    /// channel, no value, or a non-finite one). <see cref="MinRadius"/> / <see cref="RadiusRange"/> win
    /// over the theme's <see cref="ChartTheme.PointSizeMin"/> / <see cref="ChartTheme.PointSizeRange"/>.
    /// </summary>
    private float? SizeRadius(MarkContext ctx, IScale? sizeScale, DataRow row)
    {
        if (sizeScale is null || !ctx.Encodes.Has(Channel.Size)) return null;
        var raw = ctx.Encodes.Resolve(Channel.Size, row);
        if (raw is null) return null;

        float mapped = (float)MapSafely(sizeScale, raw);
        // A non-finite size has no radius: the caller keeps its default instead of drawing a NaN circle.
        if (!float.IsFinite(mapped)) return null;

        float min = MinRadius ?? (ctx.Theme ?? ChartTheme.Default).PointSizeMin;
        float range = RadiusRange ?? (ctx.Theme ?? ChartTheme.Default).PointSizeRange;
        return min + mapped * range;
    }

    /// <inheritdoc />
    public override HitResult? HitTest(MarkContext ctx, Vector2 screenPos)
    {
        var xScale = ctx.Scales.TryGet(Channel.X);
        var yScale = ctx.Scales.TryGet(YChannel);
        if (xScale is null || yScale is null) return null;
        var sScale = ctx.Scales.TryGet(Channel.Size);

        // Same geometry as Render: the radius follows the entry animation and the hover enlargement, so
        // a hit cannot land on a dot that is not (or no longer) that size.
        float anim = ComputeAnimProgress(ctx);

        for (int i = 0; i < ctx.Data.Count; i++)
        {
            var row  = ctx.Data[i];
            if (IsSeriesHidden(ctx, row)) continue;
            var xRaw = ctx.Encodes.Resolve(Channel.X, row);
            var yRaw = ctx.Encodes.Resolve(YChannel, row);
            if (xRaw == null || yRaw == null) continue;
            // Non-finite (e.g. NaN data) has no screen position: skip the element.
            float xNorm = (float)MapSafely(xScale, xRaw);
            float yNorm = (float)MapSafely(yScale, yRaw);
            if (!float.IsFinite(xNorm) || !float.IsFinite(yNorm)) continue;

            float px = ctx.Plot.MapX(xNorm);
            float py = ctx.Plot.MapY(yNorm);
            float r = SizeRadius(ctx, sScale, row) ?? DefaultRadius;

            // Animation: scale radius
            r *= anim;

            // Hover: enlarge (the fill highlight comes from ResolveFill / the Active state style).
            if (i == ctx.HoveredRowIndex)
                r *= HoverScaled(ctx, (ctx.Theme ?? ChartTheme.Default).PointHoverRadiusRatio * ctx.Animation.HoverScale);

            if (screenPos.DistanceTo(new Vector2(px, py)) <= r + (ctx.Theme ?? ChartTheme.Default).HitTestPointPadding)
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
