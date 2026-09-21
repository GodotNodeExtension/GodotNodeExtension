using System;
using Godot;
using GodotNodeExtension.Component.GodotChart.Canvas;

namespace GodotNodeExtension.Component.GodotChart.Marks;

/// <summary>
/// Funnel chart mark. Renders centered rectangular stages that narrow from top to bottom.
/// Encodes: X = stage label (OrdinalScale), Y = value (LinearScale).
/// Uses Polar coordinate to suppress Cartesian grid/axes; the funnel is self-contained.
/// Animation: stage widths grow from the centre outward (the entry animation scales the width, it does
/// not fade the stage in - the opacity of a stage and of its label follows the global animation opacity
/// and the interaction state only).
/// <para>
/// Stage labels are formatted with <see cref="Mark.LabelFormat"/> ({0} = the stage's value, {1} = its
/// category); the label sits in the middle of its stage, so <see cref="Mark.LabelPosition"/> does not
/// apply to this mark.
/// </para>
/// </summary>
public class FunnelMark : Mark
{
    /// <inheritdoc />
    /// <remarks>Polar coordinate is used so no Cartesian grid or axes are drawn.</remarks>
    public override MarkCoordinate Coordinate => MarkCoordinate.Polar;

    /// <summary>Gap between funnel stages in pixels.</summary>
    public float StageGap { get; set; } = 4f;

    /// <summary>Minimum width ratio of the narrowest stage relative to plot width [0, 1].</summary>
    public float MinWidthRatio { get; set; } = 0.15f;

    /// <summary>Corner radius for each funnel stage.</summary>
    public float CornerRadius { get; set; } = 3f;

    /// <summary>Whether to show value labels inside each stage.</summary>
    public override bool ShowLabel { get; set; } = true;

    // Cached maximum value; the key covers the owning chart, the layout version, the data list and the
    // hidden series, so a hidden stage (and a theme edit that bumps the layout version) rebuild it.
    private double _cachedMaxVal;
    private LayoutCacheKey? _cacheKey;

    private double EnsureMaxVal(MarkContext ctx)
    {
        var key = CacheKey(ctx);
        if (_cacheKey == key) return _cachedMaxVal;
        double maxVal = 0;
        for (int i = 0; i < ctx.Data.Count; i++)
        {
            if (IsSeriesHidden(ctx, ctx.Data[i])) continue;
            var yRaw = ctx.Encodes.Resolve(YChannel, ctx.Data[i]);
            if (yRaw != null)
            {
                double v = ToDouble(yRaw, "Y");
                if (double.IsFinite(v) && v > maxVal) maxVal = v;
            }
        }
        _cachedMaxVal = maxVal;
        _cacheKey = key;
        return maxVal;
    }

    /// <summary>
    /// Stage height and gap shared by <see cref="Render"/> and <see cref="HitTest"/>: the stages plus
    /// their gaps always fit into the plot height, so the last stage (and its label) can never be drawn
    /// outside the plot area. With many stages the gap is shrunk first, then the height is clamped to a
    /// visible minimum of one pixel.
    /// </summary>
    /// <param name="ctx">Context holding the plot area.</param>
    /// <param name="count">Number of stages.</param>
    private (float stageH, float gap) StageGeometry(MarkContext ctx, int count)
    {
        if (count <= 0) return (0f, 0f);
        float plotH = ctx.Plot.Height;
        float gap = count > 1
            ? MathF.Min(MathF.Max(0f, StageGap), MathF.Max(0f, (plotH - count) / (count - 1)))
            : 0f;
        float stageH = MathF.Max(1f, (plotH - gap * (count - 1)) / count);
        return (stageH, gap);
    }

    /// <summary>
    /// Top edge of the <paramref name="index"/>-th stage, clamped into the plot area (a plot shorter
    /// than the stage count leaves no room for the minimum stage height).
    /// </summary>
    /// <param name="ctx">Context holding the plot area.</param>
    /// <param name="index">Zero-based stage index.</param>
    /// <param name="stageH">Stage height from <see cref="StageGeometry"/>.</param>
    /// <param name="gap">Gap from <see cref="StageGeometry"/>.</param>
    private static float StageTop(MarkContext ctx, int index, float stageH, float gap)
        => Math.Clamp(ctx.Plot.Y + index * (stageH + gap), ctx.Plot.Y,
                      MathF.Max(ctx.Plot.Y, ctx.Plot.Y + ctx.Plot.Height - stageH));

    /// <summary>
    /// Width of one stage, shared by <see cref="Render"/> and <see cref="HitTest"/>: the value ratio is
    /// clamped to [0, 1] first, so a value above the maximum cannot draw past the plot width and a
    /// negative one cannot end up narrower than the minimum-width stage.
    /// </summary>
    /// <param name="ctx">Context holding the plot area.</param>
    /// <param name="value">Stage value.</param>
    /// <param name="maxValue">Maximum value of all stages.</param>
    /// <param name="anim">Entry animation progress.</param>
    private float StageWidth(MarkContext ctx, double value, double maxValue, float anim)
    {
        float widthRatio = Math.Clamp((float)(value / maxValue), 0f, 1f);
        // Lerp between full width and min width based on value ratio
        return ctx.Plot.Width * (MinWidthRatio + (1f - MinWidthRatio) * widthRatio) * anim;
    }

    /// <summary>Screen geometry of one funnel stage: its centre, its top edge and its width.</summary>
    /// <param name="Cx">Screen X of the stage centre.</param>
    /// <param name="Py">Screen Y of the stage's top edge.</param>
    /// <param name="StageW">Drawn width of the stage.</param>
    /// <param name="StageH">Drawn height of the stage.</param>
    /// <param name="XRaw">Raw category of the stage (its label reads it).</param>
    /// <param name="YRaw">Raw value of the stage (its label formats it).</param>
    private readonly record struct RowGeometry(
        float Cx, float Py, float StageW, float StageH, object? XRaw, object YRaw);

    /// <summary>
    /// Screen geometry of one stage, or <c>false</c> when the row cannot be drawn: hidden, without a value,
    /// carrying a non-finite one, or standing past the end of the data.
    /// <para>
    /// <see cref="Render"/> walks the stages through this and <see cref="RenderOverlay"/> asks for the
    /// interactive rows only: both draw the stage from the same numbers, so the highlight cannot drift away
    /// from it.
    /// </para>
    /// </summary>
    /// <param name="ctx">Context of the frame.</param>
    /// <param name="index">Index of the row in the rendered data.</param>
    /// <param name="stageH">Stage height from <see cref="StageGeometry"/>.</param>
    /// <param name="gap">Gap from <see cref="StageGeometry"/>.</param>
    /// <param name="maxValue">Maximum value of all stages.</param>
    /// <param name="anim">Entry animation progress.</param>
    /// <param name="geometry">Geometry of the stage when the method returns true.</param>
    private bool TryRowGeometry(MarkContext ctx, int index, float stageH, float gap, double maxValue,
        float anim, out RowGeometry geometry)
    {
        geometry = default;
        if (index < 0 || index >= ctx.Data.Count) return false;

        var row = ctx.Data[index];
        if (IsSeriesHidden(ctx, row)) return false;
        var xRaw = ctx.Encodes.Resolve(Channel.X, row);
        var yRaw = ctx.Encodes.Resolve(YChannel, row);
        if (yRaw == null) return false;

        double val = ToDouble(yRaw, "Y");
        if (!double.IsFinite(val)) return false;

        float stageW = StageWidth(ctx, val, maxValue, anim);
        geometry = new RowGeometry(ctx.Plot.X + ctx.Plot.Width / 2f, StageTop(ctx, index, stageH, gap),
            stageW, stageH, xRaw, yRaw);
        return true;
    }

    /// <summary>
    /// Draw one funnel stage: its rectangle, the selection stroke and (when
    /// <see cref="Mark.ShowLabel"/> is on) the label in its middle. The fill the caller passes in is the one
    /// the pass owns: the data layer hands over what <see cref="Mark.ResolveFill"/> answered (the hover look
    /// while it owns the state), and the overlay pass hands over <see cref="Mark.ActiveFillOf"/>.
    /// <para>
    /// The label is part of this helper because it sits <i>inside</i> the stage: an overlay pass that
    /// repainted the stage without it would erase the text the cached layer holds.
    /// </para>
    /// </summary>
    /// <param name="ctx">Context of the frame.</param>
    /// <param name="geometry">Geometry of the stage, from <see cref="TryRowGeometry"/>.</param>
    /// <param name="color">Fill of the stage.</param>
    /// <param name="opacity">Opacity of the stage.</param>
    /// <param name="selected">True when the stage is the selected one (its ring is drawn here).</param>
    private void DrawStage(MarkContext ctx, RowGeometry geometry, Color color, float opacity, bool selected)
    {
        var path = ShapePath(ctx);
        var paint = ShapePaint(ctx);

        float left = geometry.Cx - geometry.StageW / 2f;
        if (CornerRadius > 0)
            path.RoundRect(left, geometry.Py, geometry.StageW, geometry.StageH, CornerRadius);
        else
            path.Rect(left, geometry.Py, geometry.StageW, geometry.StageH);

        paint.SetColor(color).SetAntiAlias(true).SetOpacity(opacity);
        ctx.Canvas.Fill(path, paint);

        // Selection outline
        if (selected)
        {
            var selPaint = ShapePaint(ctx);
            ApplySelectionPaint(ctx, selPaint, opacity);
            ctx.Canvas.Stroke(path, selPaint);
        }

        // Value label
        if (ShowLabel)
        {
            var labelPaint = ShapePaint(ctx);
            labelPaint.SetColor(GetDataLabelColor(ctx)).SetOpacity(opacity);
            // {0} = the stage's value, {1} = its category. The category prefix is kept outside the
            // format string, so the default "{0}" still reads "category: value".
            string value = FormatLabel(LabelFormat, geometry.YRaw, geometry.XRaw);
            string label = geometry.XRaw != null ? $"{geometry.XRaw}: {value}" : value;
            DrawTextCentered(ctx, labelPaint, label, geometry.Cx, geometry.Py + geometry.StageH / 2f,
                FontSettings.Default);
        }
    }

    /// <summary>
    /// A funnel stage is opaque, so its hover fill can be painted on the overlay pass without touching the
    /// cached layer - including the label in its middle, which the same helper redraws.
    /// </summary>
    public override bool InteractionStateInOverlay => true;

    /// <inheritdoc />
    public override void Render(MarkContext ctx)
    {
        if (ctx.Data.Count == 0) return;

        float anim = ComputeAnimProgress(ctx);

        double maxVal = EnsureMaxVal(ctx);
        if (maxVal <= 0) return;

        int count = ctx.Data.Count;
        var (stageH, gap) = StageGeometry(ctx, count);

        // The interaction state stays in the data layer only while the chart does not cache it; with the
        // cache on the overlay paints it (see InteractionStateInOverlay / RenderOverlay).
        bool stateHere = !ctx.StateInOverlay;

        for (int i = 0; i < count; i++)
        {
            if (!TryRowGeometry(ctx, i, stageH, gap, maxVal, anim, out var geometry)) continue;

            var color = ResolveFill(ctx, ctx.Data[i], i, GetDefaultColor(ctx));
            float opacity = ComputeElementOpacity(ctx, ctx.Data[i], i);

            DrawStage(ctx, geometry, color, opacity, selected: stateHere && i == ctx.SelectedRowIndex);
        }
    }

    /// <inheritdoc />
    public override void RenderOverlay(MarkContext ctx)
    {
        // Only while the chart keeps the data layer in an image: with the cache off Render painted the state.
        if (!OverlayRows(ctx, out int hovered, out int selected)) return;
        if (ctx.Data.Count == 0) return;

        double maxVal = EnsureMaxVal(ctx);
        if (maxVal <= 0) return;

        int count = ctx.Data.Count;
        var (stageH, gap) = StageGeometry(ctx, count);
        float anim = ComputeAnimProgress(ctx);

        // Only the interactive stages are drawn, from the same geometry the data layer used.
        if (hovered >= 0 && hovered < count)
            DrawInteractive(ctx, hovered, stageH, gap, maxVal, anim, hovered: true);
        if (selected >= 0 && selected != hovered && selected < count)
            DrawInteractive(ctx, selected, stageH, gap, maxVal, anim, hovered: false);
    }

    /// <summary>
    /// Draw one interactive stage on the overlay pass: the hovered one takes the active fill (the data layer
    /// answered the default one while the state lives here) and the selected one gets its ring.
    /// </summary>
    private void DrawInteractive(MarkContext ctx, int index, float stageH, float gap, double maxValue,
        float anim, bool hovered)
    {
        if (!TryRowGeometry(ctx, index, stageH, gap, maxValue, anim, out var geometry)) return;

        var color = ResolveFill(ctx, ctx.Data[index], index, GetDefaultColor(ctx));
        if (hovered) color = ActiveFillOf(ctx, color);
        float opacity = ComputeElementOpacity(ctx, ctx.Data[index], index);

        DrawStage(ctx, geometry, color, opacity, selected: index == ctx.SelectedRowIndex);
    }

    /// <inheritdoc />
    public override HitResult? HitTest(MarkContext ctx, Vector2 screenPos)
    {
        if (ctx.Data.Count == 0) return null;
        // The funnel is drawn inside the plot rectangle; a point outside it cannot be on a stage.
        if (!ctx.Plot.Contains(screenPos.X, screenPos.Y)) return null;

        float plotW = ctx.Plot.Width;

        double maxVal = EnsureMaxVal(ctx);
        if (maxVal <= 0) return null;

        int count = ctx.Data.Count;
        var (stageH, gap) = StageGeometry(ctx, count);
        float anim = ComputeAnimProgress(ctx);

        for (int i = 0; i < count; i++)
        {
            var row  = ctx.Data[i];
            if (IsSeriesHidden(ctx, row)) continue;
            var yRaw = ctx.Encodes.Resolve(YChannel, row);
            if (yRaw == null) continue;

            double val = ToDouble(yRaw, "Y");
            if (!double.IsFinite(val)) continue;
            // Same width/position helpers as Render, so the hit area is exactly the drawn stage.
            float stageW = StageWidth(ctx, val, maxVal, anim);

            float cx = ctx.Plot.X + plotW / 2f;
            float py = StageTop(ctx, i, stageH, gap);
            float left = cx - stageW / 2f;

            if (screenPos.X >= left && screenPos.X <= left + stageW &&
                screenPos.Y >= py && screenPos.Y <= py + stageH)
            {
                return new HitResult
                {
                    Hit = true, Row = row, RowIndex = i,
                    ScreenX = cx, ScreenY = py + stageH / 2f,
                    Label = $"{ctx.Encodes.Resolve(Channel.X, row)}: {yRaw}",
                    MarkType = nameof(FunnelMark),
                };
            }
        }
        return null;
    }
}
