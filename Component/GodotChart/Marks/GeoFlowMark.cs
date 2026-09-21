namespace GodotNodeExtension.Component.GodotChart;

using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;
using Canvas;

/// <summary>
/// Draws one curve per data row from where it starts to where it ends - flight routes, shipments, commuter
/// flows - with the weight of the row as its width.
/// <para>
/// A flow carries two coordinates, so it does not use the position channels: the four ends are read from
/// <see cref="SourceLonField"/>, <see cref="SourceLatField"/>, <see cref="TargetLonField"/> and
/// <see cref="TargetLatField"/> (longitude first, latitude second, in the frame's units). The magnitude comes
/// from <see cref="Channel.Size"/> - mapped to a stroke width between <see cref="MinWidth"/> and
/// <see cref="MaxWidth"/> - and the colour from <see cref="Channel.Color"/>.
/// </para>
/// <para>
/// The curve bends away from the straight line by <see cref="Curvature"/> times its length, which is what
/// makes a dozen routes over the same map readable instead of one thick line. A row whose coordinates are
/// missing or not numbers is skipped, and the rest keep drawing.
/// </para>
/// </summary>
public sealed class GeoFlowMark : GeoMark
{
    private ProjectionCache<FlowLayer> _projection;
    private int _configHash;

    /// <summary>The mark's hover look (a brighter curve) stays in the data layer, so a layer cache cannot take it.</summary>
    public override bool InteractionStateInOverlay => false;

    /// <inheritdoc />
    public override GeoGeometrySource Source => GeoGeometrySource.Points;

    /// <summary>Field holding the source longitude.</summary>
    public string SourceLonField { get; set; } = "start_lon";

    /// <summary>Field holding the source latitude.</summary>
    public string SourceLatField { get; set; } = "start_lat";

    /// <summary>Field holding the target longitude.</summary>
    public string TargetLonField { get; set; } = "end_lon";

    /// <summary>Field holding the target latitude.</summary>
    public string TargetLatField { get; set; } = "end_lat";

    /// <summary>Width of the thinnest curve, in pixels.</summary>
    public float MinWidth { get; set; } = 2f;

    /// <summary>Width of the widest curve, in pixels (a row with no weight gets <see cref="MinWidth"/>).</summary>
    public float MaxWidth { get; set; } = 12f;

    /// <summary>
    /// How far a curve bulges away from the straight line between its ends, as a fraction of that line's
    /// length (0 draws straight lines, a negative value bends the other way).
    /// </summary>
    public float Curvature { get; set; } = 0.2f;

    /// <summary>Colour of a flow whose row carries no colour of its own.</summary>
    public Color FlowColor { get; set; } = new(0.42f, 0.55f, 0.85f, 0.6f);

    /// <summary>Extra room around a curve, in pixels, within which a pointer counts as on it.</summary>
    public float HitTolerance { get; set; } = 4f;

    /// <inheritdoc />
    public override void Render(MarkContext ctx)
    {
        var layer = Projected(ctx);
        if (layer.Flows.Count == 0) return;

        float animation = ComputeAnimProgress(ctx);
        var paint = ShapePaint(ctx);

        foreach (var flow in layer.Flows)
        {
            float opacity = Mathf.Clamp(ComputeElementOpacity(ctx, flow.Row, flow.RowIndex) * animation, 0f, 1f);
            if (opacity <= 0f) continue;

            // A fill does not consume the path, and these curves are stroked rather than filled: each flow
            // starts from a reset one so the previous curve is not stroked again at this width.
            var path = ShapePath(ctx);
            path.MoveTo((float)flow.Start.X, (float)flow.Start.Y)
                .QuadTo((float)flow.Control.X, (float)flow.Control.Y,
                        (float)flow.End.X, (float)flow.End.Y);
            var colour = flow.Colour;
            paint.SetColor(colour with { A = colour.A * opacity })
                 .SetStrokeWidth(flow.Width)
                 .SetAntiAlias(true)
                 .SetLineCap(LineCap.Round);
            ctx.Canvas.Stroke(path, paint);
        }
    }

    /// <inheritdoc />
    public override HitResult? HitTest(MarkContext ctx, Vector2 screenPos)
    {
        var layer = Projected(ctx);
        var probe = new GeoPoint(screenPos.X, screenPos.Y);

        // Back to front: the flow drawn last is the one the pointer is over.
        for (int i = layer.Flows.Count - 1; i >= 0; i--)
        {
            var flow = layer.Flows[i];
            if (DistanceTo(flow, probe) > (flow.Width / 2f) + HitTolerance) continue;

            var mid = flow.At(0.5);
            return new HitResult
            {
                Hit = true,
                Row = flow.Row,
                RowIndex = flow.RowIndex,
                ScreenX = (float)mid.X,
                ScreenY = (float)mid.Y,
                MarkType = nameof(GeoFlowMark),
            };
        }
        return null;
    }

    /// <summary>Distance from a point to a flow's curve, measured against the polyline it is drawn as.</summary>
    private static double DistanceTo(Flow flow, GeoPoint point)
    {
        const int steps = 12;
        double best = double.MaxValue;
        var previous = flow.Start;
        for (int step = 1; step <= steps; step++)
        {
            var current = flow.At(step / (double)steps);
            best = Math.Min(best, GeoMath.DistanceToSegment(point, previous, current));
            previous = current;
        }
        return best;
    }

    /// <summary>The projected flows, built once per layout and once per change of the mark's own knobs.</summary>
    private FlowLayer Projected(MarkContext ctx)
    {
        int config = HashCode.Combine(
            HashCode.Combine(SourceLonField, SourceLatField, TargetLonField, TargetLatField),
            HashCode.Combine(MinWidth, MaxWidth, Curvature, FlowColor, HitTolerance));
        if (config != _configHash)
        {
            _projection = default;
            _configHash = config;
        }
        return GetProjection(ctx, ref _projection, Build);
    }

    private FlowLayer Build(MarkContext ctx)
    {
        if (ctx.GeoViewport is not { } viewport || ctx.Data.Count == 0)
            return new FlowLayer([]);

        var sizeScale = ctx.Scales.TryGet(Channel.Size);
        var flows = new List<Flow>(ctx.Data.Count);

        for (int index = 0; index < ctx.Data.Count; index++)
        {
            var row = ctx.Data[index];
            if (!TryPoint(row, SourceLonField, SourceLatField, out double sx, out double sy)) continue;
            if (!TryPoint(row, TargetLonField, TargetLatField, out double tx, out double ty)) continue;

            var start = ToGeoPoint(viewport.Project(sx, sy, ctx.Plot));
            var end = ToGeoPoint(viewport.Project(tx, ty, ctx.Plot));

            float width = MinWidth;
            if (sizeScale is not null && ResolveEncode(ctx, Channel.Size, row) is { } weight)
            {
                double t = sizeScale.Map(weight);
                if (double.IsFinite(t)) width = MinWidth + ((float)t * (MaxWidth - MinWidth));
            }

            flows.Add(new Flow(row, index, start, end, Control(start, end), Mathf.Max(0.5f, width),
                               ResolveFill(ctx, row, index, FlowColor)));
        }

        return new FlowLayer(flows);
    }

    /// <summary>The control point of the quadratic curve: the midpoint pushed sideways by <see cref="Curvature"/>.</summary>
    private GeoPoint Control(GeoPoint start, GeoPoint end)
    {
        double dx = end.X - start.X;
        double dy = end.Y - start.Y;
        double length = Math.Sqrt((dx * dx) + (dy * dy));
        if (!(length > 0)) return new GeoPoint((start.X + end.X) / 2.0, (start.Y + end.Y) / 2.0);

        // The normal of the line, scaled: a positive curvature bends the curve to the left of its direction.
        // A quadratic curve reaches only half of its control point's offset, so the factor is two.
        double offset = length * Curvature * 2.0;
        return new GeoPoint(
            ((start.X + end.X) / 2.0) - (dy / length * offset),
            ((start.Y + end.Y) / 2.0) + (dx / length * offset));
    }

    /// <summary>One end of a flow, read from its two fields.</summary>
    private static bool TryPoint(DataRow row, string lonField, string latField, out double lon, out double lat)
    {
        lon = 0;
        lat = 0;
        return TryNumber(row, lonField, out lon) && TryNumber(row, latField, out lat);
    }

    /// <summary>A row's field as a finite number.</summary>
    private static bool TryNumber(DataRow row, string field, out double value)
    {
        value = 0;
        if (string.IsNullOrEmpty(field) || !row.Has(field)) return false;
        object? raw = row.Get(field);
        switch (raw)
        {
            case double number:
                value = number;
                break;
            case null:
                return false;
            default:
                if (!double.TryParse(Convert.ToString(raw, CultureInfo.InvariantCulture),
                                     NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                    return false;
                break;
        }
        return double.IsFinite(value);
    }

    private static GeoPoint ToGeoPoint(Vector2 screen) => new(screen.X, screen.Y);

    /// <summary>One projected flow: its ends, the control point of its curve, its width and its colour.</summary>
    private sealed record Flow(DataRow Row, int RowIndex, GeoPoint Start, GeoPoint End, GeoPoint Control,
                               float Width, Color Colour)
    {
        /// <summary>The point at <paramref name="t"/> along the quadratic curve (0 = start, 1 = end).</summary>
        public GeoPoint At(double t)
        {
            double u = 1.0 - t;
            return new GeoPoint(
                (u * u * Start.X) + (2.0 * u * t * Control.X) + (t * t * End.X),
                (u * u * Start.Y) + (2.0 * u * t * Control.Y) + (t * t * End.Y));
        }
    }

    /// <summary>Every projected flow of one layout.</summary>
    private sealed record FlowLayer(IReadOnlyList<Flow> Flows);
}
