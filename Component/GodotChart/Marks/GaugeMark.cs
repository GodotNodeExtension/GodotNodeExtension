using System;
using Godot;
using GodotNodeExtension.Component.GodotChart.Canvas;

namespace GodotNodeExtension.Component.GodotChart.Marks;

/// <summary>
/// Gauge chart mark. Renders an arc indicator with optional center label.
/// Encodes: Y = value (mapped to arc sweep), Color = optional category for color.
/// Useful for progress rings, speedometers, and single-value indicators.
/// Animation: arc sweeps from zero to target angle.
/// Classified as a polar-only mark; no Cartesian grid is drawn.
/// NOTE: only the first data row is rendered. Multiple rows are ignored.
/// <para>
/// The centre value and the min/max labels are formatted by the scale, not by
/// <see cref="Mark.LabelFormat"/>, and their position follows the arc geometry, so
/// <see cref="Mark.LabelPosition"/> does not apply here either.
/// </para>
/// </summary>
public class GaugeMark : Mark
{
    /// <inheritdoc />
    public override MarkCoordinate Coordinate => MarkCoordinate.Polar;

    /// <summary>Start angle in degrees. Default is -210 (roughly the 8 o'clock position); angles are measured from the positive X axis and positive angles rotate clockwise on screen (y grows downward).</summary>
    public float StartAngleDeg { get; set; } = -210f;

    /// <summary>End angle in degrees. Default is 30 (roughly the 4 o'clock position), using the same convention as <see cref="StartAngleDeg"/>.</summary>
    public float EndAngleDeg { get; set; } = 30f;

    /// <summary>Arc track width relative to the outer radius.</summary>
    public float ArcWidth { get; set; } = 0.12f;

    /// <summary>Background arc color (the track). Null = use theme color.</summary>
    public Color? TrackColor { get; set; }

    /// <summary>
    /// Value arc color. Null (the default) takes the theme's <see cref="ChartTheme.DefaultMarkColor"/>, which is
    /// what every other mark does - the mark used to carry a blue of its own here, so a light chart kept drawing
    /// the base theme's blue.
    /// </summary>
    public Color? ValueColor { get; set; }

    /// <summary>Resolve track color considering theme fallback.</summary>
    private Color ResolveTrackColor(MarkContext ctx) =>
        TrackColor ?? (ctx.Theme ?? ChartTheme.Default).GaugeTrackColor;

    /// <summary>Whether to show the value text at the center.</summary>
    public bool ShowCenterLabel { get; set; } = true;

    /// <summary>Whether to show min/max labels at the arc endpoints.</summary>
    public bool ShowMinMaxLabels { get; set; } = true;

    /// <summary>Inner radius ratio for donut-style gauge [0, 1). 0 = use ArcWidth from outer.</summary>
    public float InnerRadiusRatio { get; set; }

    /// <summary>Outer radius as ratio of min(width, height)/2. Range (0, 1].</summary>
    public float RadiusFactor { get; set; } = 0.85f;

    private (float startRad, float totalSweep, float outerR, float innerR) ComputeArcParams(MarkContext ctx)
    {
        float maxR = PolarGeometry.Radius(ctx.Plot, RadiusFactor);
        float startRad = StartAngleDeg * MathF.PI / 180f;
        float endRad   = EndAngleDeg   * MathF.PI / 180f;
        float totalSweep = endRad - startRad;
        if (totalSweep < 0) totalSweep += MathF.Tau;
        // A sweep of exactly 2*pi is turned into 0 by the backend (angles are taken modulo a full
        // turn), which would make the whole arc disappear. Keep a hair below a full turn.
        totalSweep = MathF.Min(totalSweep, PolarGeometry.FullTurnCap);
        float outerR = maxR;
        float innerR = InnerRadiusRatio > 0 ? outerR * InnerRadiusRatio : outerR * (1f - ArcWidth);
        return (startRad, totalSweep, outerR, innerR);
    }

    /// <summary>
    /// Draw the swept value arc of the gauge's single row, plus the selection ring when that row is the
    /// selected one. The fill the caller passes in is the one the pass owns: the data layer hands over what
    /// <see cref="Mark.ResolveFill"/> answered (the hover look while it owns the state), and the overlay
    /// pass hands over <see cref="Mark.ActiveFillOf"/>.
    /// </summary>
    /// <param name="ctx">Context of the frame.</param>
    /// <param name="cx">Centre of the gauge on the X axis.</param>
    /// <param name="cy">Centre of the gauge on the Y axis.</param>
    /// <param name="outerR">Outer radius of the band.</param>
    /// <param name="innerR">Inner radius of the band.</param>
    /// <param name="startRad">Start angle of the arc, in radians.</param>
    /// <param name="sweep">Swept angle of the value, in radians.</param>
    /// <param name="color">Fill of the arc.</param>
    /// <param name="opacity">Opacity of the arc.</param>
    /// <param name="selected">True when the row is the selected one (its ring is drawn here).</param>
    private void DrawValueArc(MarkContext ctx, float cx, float cy, float outerR, float innerR,
        float startRad, float sweep, Color color, float opacity, bool selected)
    {
        var valuePath = BuildArcBand(ctx, cx, cy, outerR, innerR, startRad, startRad + sweep);
        DrawArcBand(ctx, valuePath, color, opacity);

        // Selected: outline the value arc with the selection ring (States.SelectedStroke / width).
        if (selected)
        {
            var selPaint = ShapePaint(ctx);
            ApplySelectionPaint(ctx, selPaint, opacity);
            ctx.Canvas.Stroke(valuePath, selPaint);
        }
    }

    /// <summary>
    /// The value arc is an opaque band, so its hover fill can be painted on the overlay pass on top of the
    /// exact same geometry the data layer drew (the centre and the min/max labels sit inside the band's hole
    /// and are not touched by it).
    /// </summary>
    public override bool InteractionStateInOverlay => true;

    /// <summary>
    /// A gauge is drawn from the plot's <b>short</b> edge (<see cref="RadiusFactor"/> of
    /// <c>min(width, height) / 2</c>), so it asks for a square content area: a wide canvas otherwise leaves the
    /// arc floating in the middle of an empty band.
    /// </summary>
    public override float? PreferredAspectRatio => 1f;

    /// <inheritdoc />
    public override void Render(MarkContext ctx)
    {
        var yScale = ctx.Scales.TryGet(YChannel) as LinearScale;
        if (yScale == null || ctx.Data.Count == 0) return;

        var (cx, cy) = PolarGeometry.Center(ctx.Plot);
        float anim = ComputeAnimProgress(ctx);

        var (startRad, totalSweep, outerR, innerR) = ComputeArcParams(ctx);

        // Draw background track arc
        DrawArcBand(ctx, BuildArcBand(ctx, cx, cy, outerR, innerR, startRad, startRad + totalSweep),
                    ResolveTrackColor(ctx), ctx.Animation.GlobalOpacity);

        // The interaction state stays in the data layer only while the chart does not cache it; with the
        // cache on the overlay paints it (see InteractionStateInOverlay / RenderOverlay).
        bool stateHere = !ctx.StateInOverlay;

        // Render only the first data row (gauge is a single-value mark)
        for (int i = 0; i < Math.Min(ctx.Data.Count, 1); i++)
        {
            var row  = ctx.Data[i];
            if (IsSeriesHidden(ctx, row)) continue;
            var yRaw = ctx.Encodes.Resolve(YChannel, row);
            if (yRaw == null) continue;

            // A non-finite value - or one the scale cannot read at all - has no position: skip the row
            // instead of sweeping a NaN arc.
            double mapped = MapSafely(yScale, yRaw);
            if (!double.IsFinite(mapped)) continue;
            // Clamp the normalized value: an out-of-range value must not sweep past the end of the
            // scale, and a negative one must not wrap into a backwards arc.
            float norm = Math.Clamp((float)mapped, 0f, 1f);
            float sweepAnimated = totalSweep * norm * anim;

            var color = ResolveFill(ctx, row, i, ValueColor ?? GetDefaultColor(ctx));
            float opacity = ComputeElementOpacity(ctx, row, i);

            DrawValueArc(ctx, cx, cy, outerR, innerR, startRad, sweepAnimated, color, opacity,
                selected: stateHere && i == ctx.SelectedRowIndex);

            if (ShowCenterLabel && i == 0)
            {
                var labelPaint = ShapePaint(ctx);
                var glc = (ctx.Theme ?? ChartTheme.Default).GaugeLabelColor;
                labelPaint.SetColor(glc with { A = glc.A * ctx.Animation.GlobalOpacity });
                string text = yScale.Format(yRaw);
                DrawTextCentered(ctx, labelPaint, text, cx, cy, FontSettings.Default);
            }
        }

        // Min/Max labels
        if (ShowMinMaxLabels)
        {
            float halfDim = MathF.Min(ctx.Plot.Width, ctx.Plot.Height) / 2f;
            float availableMargin = halfDim - outerR;
            float labelOffset = MathF.Min((ctx.Theme ?? ChartTheme.Default).GaugeLabelOffset, availableMargin * 0.8f);
            float labelR = outerR + labelOffset;
            var minPaint = ShapePaint(ctx);
            var mmlc = (ctx.Theme ?? ChartTheme.Default).GaugeMinMaxLabelColor;
            minPaint.SetColor(mmlc with { A = mmlc.A * ctx.Animation.GlobalOpacity });
            DrawTextCentered(ctx, minPaint, yScale.Format(yScale.Min),
                cx + MathF.Cos(startRad) * labelR,
                cy + MathF.Sin(startRad) * labelR,
                FontSettings.Default);

            float arcEnd = startRad + totalSweep;
            DrawTextCentered(ctx, minPaint, yScale.Format(yScale.Max),
                cx + MathF.Cos(arcEnd) * labelR,
                cy + MathF.Sin(arcEnd) * labelR,
                FontSettings.Default);
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

        var yScale = ctx.Scales.TryGet(YChannel) as LinearScale;
        if (yScale == null || ctx.Data.Count == 0) return;

        // A gauge draws its first row only, so a hover on any other row has no arc to highlight.
        if (hovered > 0 && selected > 0) return;

        var (cx, cy) = PolarGeometry.Center(ctx.Plot);
        float anim = ComputeAnimProgress(ctx);
        var (startRad, totalSweep, outerR, innerR) = ComputeArcParams(ctx);

        if (hovered == 0) DrawInteractive(ctx, yScale, cx, cy, outerR, innerR, startRad, totalSweep, anim, 0, true);
        if (selected == 0 && selected != hovered)
            DrawInteractive(ctx, yScale, cx, cy, outerR, innerR, startRad, totalSweep, anim, 0, false);
    }

    /// <summary>
    /// Draw the interactive value arc on the overlay pass: the hovered row takes the active fill (the data
    /// layer answered the default one while the state lives here) and the selected one gets its ring.
    /// </summary>
    private void DrawInteractive(MarkContext ctx, LinearScale yScale, float cx, float cy, float outerR,
        float innerR, float startRad, float totalSweep, float anim, int index, bool hovered)
    {
        var row = ctx.Data[index];
        if (IsSeriesHidden(ctx, row)) return;
        var yRaw = ctx.Encodes.Resolve(YChannel, row);
        if (yRaw == null) return;

        double mapped = MapSafely(yScale, yRaw);
        if (!double.IsFinite(mapped)) return;
        float norm = Math.Clamp((float)mapped, 0f, 1f);

        var color = ResolveFill(ctx, row, index, ValueColor ?? GetDefaultColor(ctx));
        if (hovered) color = ActiveFillOf(ctx, color);
        float opacity = ComputeElementOpacity(ctx, row, index);

        DrawValueArc(ctx, cx, cy, outerR, innerR, startRad, totalSweep * norm * anim, color, opacity,
            selected: index == ctx.SelectedRowIndex);
    }

    // The band path is built separately from its paint so the selection ring can outline the very same
    // geometry that was filled (see the value arc in Render).
    private IPath2D BuildArcBand(MarkContext ctx,
        float cx, float cy, float outerR, float innerR, float startAngle, float endAngle)
    {
        var path = ShapePath(ctx);
        ShapeGeometry.AddRingBand(path, cx, cy, outerR, innerR, startAngle, endAngle);
        return path;
    }

    // Instance method: it uses the mark's reusable paint object.
    private void DrawArcBand(MarkContext ctx, IPath2D path, Color color, float opacity)
    {
        var paint = ShapePaint(ctx);
        paint.SetColor(color).SetAntiAlias(true).SetOpacity(opacity);
        ctx.Canvas.Fill(path, paint);
    }

    /// <inheritdoc />
    public override HitResult? HitTest(MarkContext ctx, Vector2 screenPos)
    {
        var yScale = ctx.Scales.TryGet(YChannel) as LinearScale;
        if (yScale == null || ctx.Data.Count == 0) return null;

        var (cx, cy) = PolarGeometry.Center(ctx.Plot);

        var (startRad, totalSweep, outerR, innerR) = ComputeArcParams(ctx);

        float dist = PolarGeometry.Distance(cx, cy, screenPos.X, screenPos.Y);
        if (dist < innerR || dist > outerR) return null;

        float relAngle = PolarGeometry.RelativeAngle(
            PolarGeometry.AngleOf(cx, cy, screenPos.X, screenPos.Y), startRad);
        if (relAngle > totalSweep) return null;

        // Only test the first data row (gauge is a single-value mark)
        for (int i = 0; i < Math.Min(ctx.Data.Count, 1); i++)
        {
            var row  = ctx.Data[i];
            if (IsSeriesHidden(ctx, row)) continue;
            var yRaw = ctx.Encodes.Resolve(YChannel, row);
            if (yRaw == null) continue;

            // Mirror Render: non-finite values are not drawn, the normalized value is clamped to the
            // same range, and the entry animation has to have swept the arc before it can be hit.
            double mapped = MapSafely(yScale, yRaw);
            if (!double.IsFinite(mapped)) continue;
            float norm = Math.Clamp((float)mapped, 0f, 1f);
            float valueSweep = totalSweep * norm * ComputeAnimProgress(ctx);

            if (relAngle <= valueSweep)
            {
                return new HitResult
                {
                    Hit = true, Row = row, RowIndex = i,
                    ScreenX = cx, ScreenY = cy,
                    Label = $"{yScale.Format(yRaw)}",
                    MarkType = nameof(GaugeMark),
                };
            }
        }
        return null;
    }
}
