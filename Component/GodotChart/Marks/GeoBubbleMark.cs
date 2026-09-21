namespace GodotNodeExtension.Component.GodotChart;

using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// Draws one bubble per data row at the coordinate the row carries: the classic "cities, sized by
/// population" layer over a map.
/// <para>
/// The position is the ordinary position channel - <see cref="Channel.X"/> is the longitude and
/// <see cref="Channel.Y"/> the latitude - because a coordinate is a coordinate: the frame says what the two
/// numbers mean, and the scales that fit them are the ones a chart already has (a linear scale with
/// <c>IncludeZero = false</c>, which is what the automatic fit does for a position axis). The magnitude comes
/// from <see cref="Channel.Size"/>, mapped to a radius between <see cref="MinRadius"/> and
/// <see cref="MaxRadius"/>, and the colour from <see cref="Channel.Color"/> like every other mark.
/// </para>
/// <para>
/// A row whose coordinate is missing or not a number is skipped, and the rest of the rows keep drawing: one
/// broken row in a table of cities must not take the layer down.
/// </para>
/// </summary>
public sealed class GeoBubbleMark : GeoMark
{
    private ProjectionCache<BubbleLayer> _projection;
    private int _configHash;

    /// <summary>
    /// A geographic mark stops the layer cache from being used, because the hover look (a brighter bubble) is
    /// painted in the data layer rather than in a second pass.
    /// </summary>
    public override bool InteractionStateInOverlay => false;

    /// <inheritdoc />
    public override GeoGeometrySource Source => GeoGeometrySource.Points;

    /// <summary>Radius of the smallest bubble, in pixels.</summary>
    public float MinRadius { get; set; } = 7f;

    /// <summary>Radius of the largest bubble, in pixels (a row with no size gets <see cref="MinRadius"/>).</summary>
    public float MaxRadius { get; set; } = 26f;

    /// <summary>Width of the ring drawn around each bubble; 0 draws no ring.</summary>
    public float StrokeWidth { get; set; } = 1f;

    /// <summary>Colour of a bubble whose row carries no colour of its own.</summary>
    public Color BubbleColor { get; set; } = new(0.29f, 0.56f, 0.89f, 0.85f);

    /// <inheritdoc />
    public override void Render(MarkContext ctx)
    {
        var layer = Projected(ctx);
        if (layer.Bubbles.Count == 0) return;

        float animation = ComputeAnimProgress(ctx);
        var paint = ShapePaint(ctx);

        foreach (var bubble in layer.Bubbles)
        {
            float opacity = Mathf.Clamp(ComputeElementOpacity(ctx, bubble.Row, bubble.RowIndex) * animation, 0f, 1f);
            if (opacity <= 0f) continue;
            float radius = bubble.Radius * animation;
            if (radius <= 0f) continue;

            // One reset path per bubble: a Fill leaves the path in place, and the next circle would be filled
            // together with this one otherwise.
            var path = ShapePath(ctx);
            path.Circle((float)bubble.Centre.X, (float)bubble.Centre.Y, radius);
            paint.SetColor(bubble.Fill).SetAntiAlias(true).SetOpacity(opacity);
            ctx.Canvas.Fill(path, paint);

            if (StrokeWidth <= 0f) continue;
            var ring = BrightenColor(bubble.Fill, -0.3f);
            paint.SetColor(ring with { A = ring.A * opacity })
                 .SetStrokeWidth(StrokeWidth)
                 .SetAntiAlias(true);
            ctx.Canvas.Stroke(path, paint);
        }
    }

    /// <inheritdoc />
    public override HitResult? HitTest(MarkContext ctx, Vector2 screenPos)
    {
        var layer = Projected(ctx);

        // Back to front: the bubble drawn last is the one the pointer is over.
        for (int i = layer.Bubbles.Count - 1; i >= 0; i--)
        {
            var bubble = layer.Bubbles[i];
            float dx = screenPos.X - (float)bubble.Centre.X;
            float dy = screenPos.Y - (float)bubble.Centre.Y;
            float reach = bubble.Radius + (StrokeWidth / 2f);
            if ((dx * dx) + (dy * dy) > reach * reach) continue;

            object? value = ResolveEncode(ctx, YChannel, bubble.Row);
            return new HitResult
            {
                Hit = true,
                Row = bubble.Row,
                RowIndex = bubble.RowIndex,
                ScreenX = (float)bubble.Centre.X,
                ScreenY = (float)bubble.Centre.Y,
                Label = value is null ? null : $"{value}",
                MarkType = nameof(GeoBubbleMark),
            };
        }
        return null;
    }

    /// <summary>The projected bubbles, built once per layout and once per change of the mark's own knobs.</summary>
    private BubbleLayer Projected(MarkContext ctx)
    {
        int config = HashCode.Combine(MinRadius, MaxRadius, StrokeWidth, BubbleColor);
        if (config != _configHash)
        {
            _projection = default;
            _configHash = config;
        }
        return GetProjection(ctx, ref _projection, Build);
    }

    private BubbleLayer Build(MarkContext ctx)
    {
        if (ctx.GeoViewport is not { } viewport || ctx.Data.Count == 0)
            return new BubbleLayer([]);

        var sizeScale = ctx.Scales.TryGet(Channel.Size);
        var bubbles = new List<Bubble>(ctx.Data.Count);

        for (int index = 0; index < ctx.Data.Count; index++)
        {
            // The row's own coordinate, in the frame's units: the scales fit the axes, but what a geographic mark
            // projects is the coordinate itself (a row outside the visible window is cut by the plot clip).
            var row = ctx.Data[index];
            if (!TryCoordinate(row, ctx, out double x, out double y)) continue;

            float radius = MinRadius;
            if (sizeScale is not null && ResolveEncode(ctx, Channel.Size, row) is { } size)
            {
                double t = sizeScale.Map(size);
                if (double.IsFinite(t)) radius = MinRadius + ((float)t * (MaxRadius - MinRadius));
            }

            var centre = viewport.Project(x, y, ctx.Plot);
            bubbles.Add(new Bubble(row, index, new GeoPoint(centre.X, centre.Y),
                                   Mathf.Max(1f, radius), ResolveFill(ctx, row, index, BubbleColor)));
        }

        return new BubbleLayer(bubbles);
    }

    /// <summary>The row's own coordinate in the frame's units, or false when the row has no usable one.</summary>
    private bool TryCoordinate(DataRow row, MarkContext ctx, out double x, out double y)
    {
        x = 0;
        y = 0;
        object? rawX = ResolveEncode(ctx, Channel.X, row);
        object? rawY = ResolveEncode(ctx, YChannel, row);
        if (rawX is null || rawY is null) return false;

        x = ToDouble(rawX, "longitude");
        y = ToDouble(rawY, "latitude");
        return double.IsFinite(x) && double.IsFinite(y);
    }

    /// <summary>One projected bubble: where it sits, how big it is and what colour it is.</summary>
    private sealed record Bubble(DataRow Row, int RowIndex, GeoPoint Centre, float Radius, Color Fill);

    /// <summary>Every projected bubble of one layout.</summary>
    private sealed record BubbleLayer(IReadOnlyList<Bubble> Bubbles);
}
