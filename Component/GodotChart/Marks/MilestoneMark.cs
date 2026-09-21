using System;
using System.Collections.Generic;
using Godot;
using GodotNodeExtension.Component.GodotChart.Canvas;

namespace GodotNodeExtension.Component.GodotChart.Marks;

/// <summary>
/// Milestone mark - the event timeline: every row is an event placed on a time (or numeric) axis, drawn
/// as a marker with its label, and optionally one horizontal lane per category so several streams of
/// events can share the chart.
/// <para>
/// Encodes: X = position on the axis (numeric or time scale), Y = optional lane (OrdinalScale),
/// Label = event text, Color = marker colour, Shape = marker symbol.
/// Additional fields: <see cref="LabelField"/> (default <c>"label"</c>) is read by name when the Label
/// channel is not encoded.
/// </para>
/// <para>
/// The axis is the X scale the caller supplies, so a real time axis is a
/// <see cref="GodotNodeExtension.Component.GodotChart.TimeScale"/> on <see cref="Channel.X"/>.
/// Animation: markers grow from the axis outward. Labels are not clipped by the chart, so their anchor
/// is clamped into the plot area (an event near an edge would otherwise draw its text outside it).
/// </para>
/// </summary>
public class MilestoneMark : Mark
{
    /// <inheritdoc />
    public override MarkCoordinate Coordinate => MarkCoordinate.Cartesian;

    /// <summary>Field holding the event text, used when the Label channel is not encoded.</summary>
    public string LabelField { get; set; } = "label";

    /// <summary>Marker radius in pixels.</summary>
    public float MarkerRadius { get; set; } = 6f;

    /// <summary>Symbol used when the Shape channel is not encoded.</summary>
    public ShapeKind MarkerShape { get; set; } = ShapeKind.Circle;

    /// <summary>Whether to draw the horizontal line the markers sit on (one per lane).</summary>
    public bool ShowAxisLine { get; set; } = true;

    /// <summary>Stroke width of that line.</summary>
    public float AxisLineWidth { get; } = 1f;

    /// <summary>Gap between a marker and its label, in pixels.</summary>
    public float LabelOffset { get; } = 8f;

    /// <summary>
    /// Alternate the labels above and below the marker. Neighbouring events on one lane are usually close
    /// together, and stacking every label on the same side made them overlap.
    /// </summary>
    public bool AlternateLabels { get; set; } = true;

    /// <summary>Whether to show the event text.</summary>
    public override bool ShowLabel { get; set; } = true;

    // Placed markers of the current context, shared by Render and HitTest so both agree on the geometry.
    private readonly List<(int RowIndex, DataRow Row, float X, float Y)> _placed = [];

    /// <summary>Resolve the screen position of every event; returns false when nothing can be drawn.</summary>
    private bool Place(MarkContext ctx)
    {
        _placed.Clear();

        // The position axis is whatever scale the caller put on X: linear, log or time.
        var xScale = ctx.Scales.TryGet(Channel.X);
        if (xScale is null) return false;

        // Lanes: one row of the ordinal Y axis per category. Without them everything sits on the middle
        // line, which is what a plain milestone timeline looks like.
        var laneScale = ctx.Scales.TryGet(YChannel) as OrdinalScale;
        float centerY = ctx.Plot.Y + ctx.Plot.Height / 2f;

        for (int i = 0; i < ctx.Data.Count; i++)
        {
            var row = ctx.Data[i];
            if (IsSeriesHidden(ctx, row)) continue;

            var xRaw = ctx.Encodes.Resolve(Channel.X, row);
            if (xRaw == null) continue;
            // The event axis is numeric, so a value it cannot read (text in the time column) is skipped
            // rather than raised: see Mark.MapSafely.
            float xNorm = (float)MapSafely(xScale, xRaw);
            if (!float.IsFinite(xNorm)) continue;

            float y = centerY;
            if (laneScale is not null)
            {
                var laneRaw = ctx.Encodes.Resolve(YChannel, row);
                // The lane is a category: one the scale does not know would be mapped to 0 (the first lane),
                // so the event is skipped rather than drawn in the wrong lane.
                int laneIndex = laneRaw is null ? -1 : laneScale.IndexOf(laneRaw.ToString() ?? "");
                if (laneIndex >= 0)
                    y = ctx.Plot.MapY((float)laneScale.Map(laneRaw!));
            }

            _placed.Add((i, row, ctx.Plot.MapX(xNorm), y));
        }

        return _placed.Count > 0;
    }

    /// <summary>Event text: the Label channel first, then <see cref="LabelField"/>, then the X value.</summary>
    private string TextOf(MarkContext ctx, DataRow row)
        => ctx.Encodes.Resolve(Channel.Label, row)?.ToString()
           ?? GetStringOrNull(row, LabelField)
           ?? ctx.Encodes.Resolve(Channel.X, row)?.ToString()
           ?? "";

    /// <summary>Symbol of one event: the Shape channel when encoded, <see cref="MarkerShape"/> otherwise.</summary>
    private ShapeKind ShapeOf(MarkContext ctx, DataRow row)
        => ctx.Encodes.Has(Channel.Shape) ? ResolveShape(ctx, row) : MarkerShape;

    /// <summary>
    /// Draw one event marker, plus the selection ring when the event is the selected one. The fill the
    /// caller passes in is the one the pass owns: the data layer hands over what
    /// <see cref="Mark.ResolveFill"/> answered (the hover look while it owns the state), and the overlay
    /// pass hands over <see cref="Mark.ActiveFillOf"/> for the hovered event.
    /// </summary>
    /// <param name="ctx">Context of the frame.</param>
    /// <param name="row">The row (its Shape channel decides the symbol).</param>
    /// <param name="x">Screen X of the marker.</param>
    /// <param name="y">Screen Y of the marker.</param>
    /// <param name="radius">Radius of the marker (animation applied).</param>
    /// <param name="color">Fill of the marker.</param>
    /// <param name="opacity">Opacity of the marker.</param>
    /// <param name="selected">True when the event is the selected one (its ring is drawn here).</param>
    private void DrawMarker(MarkContext ctx, DataRow row, float x, float y, float radius, Color color,
        float opacity, bool selected)
    {
        var path = ShapePath(ctx);
        var paint = ShapePaint(ctx);
        ShapeGeometry.Build(path, ShapeOf(ctx, row), x, y, radius);
        paint.SetColor(color).SetAntiAlias(true).SetOpacity(opacity);
        ctx.Canvas.Fill(path, paint);

        if (selected)
        {
            var selPaint = ShapePaint(ctx);
            ApplySelectionPaint(ctx, selPaint, opacity);
            ctx.Canvas.Stroke(path, selPaint);
        }
    }

    /// <summary>
    /// A milestone marker is opaque, so its hover fill can be painted on the overlay pass without touching
    /// the cached layer: the overlay redraws exactly the symbol the layer already holds, at the position
    /// <see cref="Place"/> gave it.
    /// </summary>
    public override bool InteractionStateInOverlay => true;

    /// <inheritdoc />
    public override void Render(MarkContext ctx)
    {
        if (!Place(ctx)) return;

        float anim = ComputeAnimProgress(ctx);
        float radius = MarkerRadius * anim;
        bool lanes = ctx.Scales.TryGet(YChannel) is OrdinalScale;

        // The interaction state stays in the data layer only while the chart does not cache it; with the
        // cache on the overlay paints it (see InteractionStateInOverlay / RenderOverlay).
        bool stateHere = !ctx.StateInOverlay;

        // One line per lane, or a single line through the middle for a lane-less timeline.
        if (ShowAxisLine)
        {
            var linePaint = ShapePaint(ctx);
            linePaint.SetColor((ctx.Theme ?? ChartTheme.Default).AxisColor)
                     .SetStrokeWidth(AxisLineWidth)
                     .SetAntiAlias(true);

            var linePath = ShapePath(ctx);
            if (lanes)
            {
                var drawn = new List<float>();
                foreach (var (_, _, _, y) in _placed)
                {
                    bool seen = false;
                    foreach (float other in drawn)
                        if (Mathf.Abs(other - y) < 0.5f) { seen = true; break; }
                    if (seen) continue;
                    drawn.Add(y);
                    linePath.Reset();
                    linePath.MoveTo(ctx.Plot.X, y);
                    linePath.LineTo(ctx.Plot.X + ctx.Plot.Width, y);
                    ctx.Canvas.Stroke(linePath, linePaint);
                }
            }
            else
            {
                float y = ctx.Plot.Y + ctx.Plot.Height / 2f;
                linePath.MoveTo(ctx.Plot.X, y);
                linePath.LineTo(ctx.Plot.X + ctx.Plot.Width, y);
                ctx.Canvas.Stroke(linePath, linePaint);
            }
        }

        // Labels ride the shared label pipeline (theme colour, opacity, ShowLabel), anchored on the side
        // of the marker this event uses.
        List<LabelElement>? labels = BeginLabelCollection();
        float lineHeight = labels is null
            ? 0f
            : ctx.Canvas.MeasureText("0", ThemedFont(ctx, new FontSettings
              {
                  Size = FontSettings.Default.Size * 0.85f,
                  Align = TextAlign.Center,
              })).Height;

        for (int i = 0; i < _placed.Count; i++)
        {
            var (rowIndex, row, x, y) = _placed[i];
            var color = ResolveFill(ctx, row, rowIndex, GetDefaultColor(ctx));
            float opacity = ComputeElementOpacity(ctx, row, rowIndex);

            DrawMarker(ctx, row, x, y, radius, color, opacity,
                selected: stateHere && rowIndex == ctx.SelectedRowIndex);

            if (labels is null) continue;

            bool below = AlternateLabels && i % 2 == 1;
            float anchorY = below
                ? y + MarkerRadius + LabelOffset + lineHeight          // text ends up under the marker
                : y - (MarkerRadius + LabelOffset);                    // ... and above it otherwise
            // Labels are not clipped by anything, so their anchor is clamped into the plot area: an
            // event near an edge used to push its text outside the chart.
            float anchorX = Math.Clamp(x, ctx.Plot.X, ctx.Plot.X + ctx.Plot.Width);
            anchorY = Math.Clamp(anchorY, ctx.Plot.Y, ctx.Plot.Y + ctx.Plot.Height);
            // {0} = the event text, {1} = its position on the axis.
            var xRaw = ctx.Encodes.Resolve(Channel.X, row);
            labels.Add(new LabelElement(anchorX, anchorY,
                FormatLabel(LabelFormat, TextOf(ctx, row), xRaw), opacity,
                row, rowIndex, color, LabelValue(xRaw)));
        }

        if (labels is not null) DrawLabels(ctx, labels);
    }

    /// <inheritdoc />
    public override void RenderOverlay(MarkContext ctx)
    {
        // Only while the chart keeps the data layer in an image: with the cache off Render painted the state.
        if (!ctx.StateInOverlay) return;

        int hovered = ctx.HoveredRowIndex;
        int selected = ctx.SelectedRowIndex;
        if (hovered < 0 && selected < 0) return;

        // The placed events are the ones the data layer drew this frame (Place ran there, and the layer
        // cache is only hit while the layout and the data are unchanged), so the marker is redrawn where
        // the layer holds it instead of placing the whole table again.
        float radius = MarkerRadius * ComputeAnimProgress(ctx);

        foreach (var (rowIndex, row, x, y) in _placed)
        {
            bool isHovered  = rowIndex == hovered;
            bool isSelected = rowIndex == selected;
            if (!isHovered && !isSelected) continue;

            var color = ResolveFill(ctx, row, rowIndex, GetDefaultColor(ctx));
            if (isHovered) color = ActiveFillOf(ctx, color);
            float opacity = ComputeElementOpacity(ctx, row, rowIndex);

            DrawMarker(ctx, row, x, y, radius, color, opacity, selected: isSelected);
        }
    }

    /// <inheritdoc />
    public override HitResult? HitTest(MarkContext ctx, Vector2 screenPos)
    {
        if (!Place(ctx)) return null;

        // Same geometry as Render: the marker radius scaled by the entry animation, so a marker that
        // is still growing is not hit over its full final size.
        float radius = MarkerRadius * ComputeAnimProgress(ctx);
        float tolerance = radius + (ctx.Theme ?? ChartTheme.Default).HitTestPointPadding;

        foreach (var (rowIndex, row, x, y) in _placed)
        {
            if (screenPos.DistanceTo(new Vector2(x, y)) > tolerance) continue;

            return new HitResult
            {
                Hit = true, Row = row, RowIndex = rowIndex,
                ScreenX = x, ScreenY = y,
                Label = TextOf(ctx, row),
                // Reported like the other marks do: Render resolves the same key through the Color
                // channel for the element's fill and focus state.
                SeriesKey = ResolveSeriesKey(ctx, row),
                MarkType = nameof(MilestoneMark),
            };
        }

        return null;
    }
}
