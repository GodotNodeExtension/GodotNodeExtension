using System.Collections.Generic;
using Godot;
using GodotNodeExtension.Component.GodotChart.Canvas;

namespace GodotNodeExtension.Component.GodotChart.Marks;

/// <summary>
/// Timeline mark. Renders horizontal colored bars on a vertical category axis.
/// Useful for Buff/Debuff timelines, Gantt charts, and combat log visualization.
/// Encodes: Y = category (OrdinalScale), Color = optional series.
/// Additional fields: StartField, EndField for bar range on X axis (LinearScale).
/// <para>
/// Bar labels are formatted with <see cref="Mark.LabelFormat"/> ({0} = the bar's label - the Label
/// channel, else the category - and {1} = its start value). The label sits in the middle of its bar, so
/// <see cref="Mark.LabelPosition"/> does not apply to this mark.
/// </para>
/// <para>
/// An interval whose end lies before its start (a reversed or negative span) is still drawn: the two
/// endpoints are swapped so the bar reads left to right, and the situation is reported once with
/// <c>GD.PushWarning</c>.
/// </para>
/// </summary>
public class TimelineMark : Mark
{
    /// <inheritdoc />
    public override MarkCoordinate Coordinate => MarkCoordinate.Cartesian;

    /// <summary>Field name for the start value (left edge of bar).</summary>
    public string StartField { get; set; } = "start";

    /// <summary>Field name for the end value (right edge of bar).</summary>
    public string EndField { get; set; } = "end";

    /// <summary>Bar height relative to the slot [0, 1].</summary>
    public float BarHeightRatio { get; set; } = 0.6f;

    /// <summary>Corner radius for each bar.</summary>
    public float CornerRadius { get; set; } = 3f;

    /// <summary>Whether to show labels inside bars.</summary>
    public override bool ShowLabel { get; set; }

    /// <summary>Whether a reversed interval (end before start) was already reported.</summary>
    private bool _warnedReversedRange;

    /// <summary>
    /// Left edge and width of one bar, shared by <see cref="Render"/> and <see cref="HitTest"/>: the
    /// entry animation reveals the width from the start edge. A row whose end lies before its start
    /// keeps its bar - the two endpoints are swapped, so the row is drawn left-to-right instead of
    /// being dropped silently - and the situation is reported once.
    /// </summary>
    /// <param name="ctx">Context holding the plot area.</param>
    /// <param name="xScale">Scale of the interval axis.</param>
    /// <param name="startRaw">Start value of the bar.</param>
    /// <param name="endRaw">End value of the bar.</param>
    /// <param name="anim">Entry animation progress.</param>
    /// <returns>
    /// <c>false</c> when the row has no drawable interval - a missing endpoint or a value that is not a
    /// finite number. The caller skips that row instead of painting at <c>NaN</c>.
    /// </returns>
    private (bool HasGeometry, float StartX, float BarW) BarGeometry(
        MarkContext ctx, LinearScale xScale, object? startRaw, object? endRaw, float anim)
    {
        // A missing or non-numeric endpoint has no position. Both are "no value" rather than zero, so
        // the row is skipped - the same rule the other marks follow for their coordinates.
        if (!ScaleConvert.TryToDouble(startRaw, out double startValue)
            || !ScaleConvert.TryToDouble(endRaw, out double endValue))
            return (false, 0f, 0f);

        float startX = ctx.Plot.MapX((float)xScale.Map(startValue));
        float endX   = ctx.Plot.MapX((float)xScale.Map(endValue));
        if (endX < startX)
        {
            (startX, endX) = (endX, startX);
            if (!_warnedReversedRange)
            {
                _warnedReversedRange = true;
                GD.PushWarning(
                    "TimelineMark: an interval ends before it starts (end < start); " +
                    "the bar is drawn with its endpoints swapped.");
            }
        }
        return (true, startX, (endX - startX) * anim);
    }

    private (OrdinalScale yScale, LinearScale xScale, float barH)?
        ResolveScalesAndBar(MarkContext ctx)
    {
        var yScale = ctx.Scales.TryGet(YChannel) as OrdinalScale ?? ctx.Scales.TryGet(Channel.X) as OrdinalScale;
        var xScale = ctx.Scales.TryGet(Channel.X) as LinearScale ?? ctx.Scales.TryGet(YChannel) as LinearScale;
        if (yScale == null || xScale == null || yScale.Domain.Count == 0) return null;
        float slotH = ctx.Plot.Height / yScale.Domain.Count;
        float barH  = slotH * BarHeightRatio;
        return (yScale, xScale, barH);
    }

    /// <summary>
    /// Contributes the range the bars span to the axis they are measured on (see
    /// <see cref="StartField"/> / <see cref="EndField"/>), instead of leaving the axis fitted to
    /// whatever the value channel happens to hold.
    /// </summary>
    public override void ContributeScales(ScaleSet scales, EncodeSet encodes, List<DataRow> data)
    {
        // The bars span StartField..EndField, so the range axis has to cover those values. Without
        // this the axis was fitted to whatever the value channel happened to hold and every bar that
        // reached past it was clipped.
        var rangeChannel = scales.TryGet(Channel.X) is OrdinalScale ? Channel.Y : Channel.X;
        ScaleContributionHelper.ContributeMinMaxScale(scales, data, StartField, EndField, rangeChannel);
    }

    /// <summary>Screen geometry of one bar: the category it sits in, its interval and its label anchor.</summary>
    /// <param name="CategoryRaw">Raw category value of the row.</param>
    /// <param name="StartRaw">Raw start value of the bar.</param>
    /// <param name="EndRaw">Raw end value of the bar (the hit label quotes the interval).</param>
    /// <param name="StartX">Screen X of the bar's left edge.</param>
    /// <param name="BarW">Drawn width of the bar (animation applied).</param>
    /// <param name="Py">Screen Y of the bar's top edge.</param>
    /// <param name="BarH">Drawn height of the bar.</param>
    private readonly record struct RowGeometry(
        object CategoryRaw, object? StartRaw, object? EndRaw, float StartX, float BarW, float Py, float BarH);

    /// <summary>
    /// Screen geometry of one bar, or <c>false</c> when the row cannot be drawn: hidden, without its
    /// interval endpoints, or standing on a category the lane scale does not know (an unknown category
    /// maps to 0, i.e. onto the first lane).
    /// <para>
    /// <see cref="Render"/> walks the whole table through this and <see cref="RenderOverlay"/> asks for the
    /// interactive rows only: both draw the bar from the same numbers, so the highlight cannot drift away
    /// from it.
    /// </para>
    /// </summary>
    /// <param name="ctx">Context of the frame.</param>
    /// <param name="yScale">Lane scale of the rows.</param>
    /// <param name="xScale">Scale the intervals are measured on.</param>
    /// <param name="barH">Drawn bar height.</param>
    /// <param name="index">Index of the row in the rendered data.</param>
    /// <param name="anim">Entry animation progress.</param>
    /// <param name="geometry">Geometry of the row when the method returns true.</param>
    private bool TryRowGeometry(MarkContext ctx, OrdinalScale yScale, LinearScale xScale, float barH,
        int index, float anim, out RowGeometry geometry)
    {
        geometry = default;
        if (index < 0 || index >= ctx.Data.Count) return false;

        var row = ctx.Data[index];
        if (IsSeriesHidden(ctx, row)) return false;

        // The category lives on whichever channel carries the ordinal scale: the chart binds the
        // category to Y and the value to X (Timeline is horizontal), but a host that hands the marks its
        // own scales can have it the other way round - reading a fixed channel then picked up the numeric
        // value field as a category.
        Channel catChannel = ReferenceEquals(ctx.Scales.TryGet(YChannel), yScale) ? YChannel : Channel.X;
        var catRaw   = ctx.Encodes.Resolve(catChannel, row) ?? ctx.Encodes.Resolve(Channel.X, row);
        if (catRaw == null) return false;
        if (!row.TryGet<object>(StartField, out var startRaw) ||
            !row.TryGet<object>(EndField, out var endRaw)) return false;

        // An unknown category would be mapped to 0, i.e. onto the first lane: skip the row instead.
        if (yScale.IndexOf(catRaw.ToString() ?? "") < 0) return false;

        float yNorm   = (float)yScale.Map(catRaw);
        // Shared with HitTest: a reversed interval is drawn (endpoints swapped), not dropped.
        var (hasGeometry, startX, barW) = BarGeometry(ctx, xScale, startRaw, endRaw, anim);
        if (!hasGeometry) return false;
        if (barW <= 0) return false;

        geometry = new RowGeometry(catRaw, startRaw, endRaw, startX, barW,
            ctx.Plot.MapY(yNorm) - barH / 2f, barH);
        return true;
    }

    /// <summary>
    /// Draw one timeline bar: its rectangle, the selection stroke and (when
    /// <see cref="Mark.ShowLabel"/> is on) the label inside it. The fill the caller passes in is the one
    /// the pass owns: the data layer hands over what <see cref="Mark.ResolveFill"/> answered (the hover
    /// look while it owns the state), and the overlay pass hands over <see cref="Mark.ActiveFillOf"/>.
    /// <para>
    /// The label is part of this helper because it sits <i>inside</i> the bar: an overlay pass that
    /// repainted the bar without it would erase the text the cached layer holds.
    /// </para>
    /// </summary>
    /// <param name="ctx">Context of the frame.</param>
    /// <param name="row">The row itself (the label reads it).</param>
    /// <param name="geometry">Geometry of the bar, from <see cref="TryRowGeometry"/>.</param>
    /// <param name="color">Fill of the bar.</param>
    /// <param name="opacity">Opacity of the bar.</param>
    /// <param name="selected">True when the row is the selected one (its ring is drawn here).</param>
    private void DrawBar(MarkContext ctx, DataRow row, RowGeometry geometry, Color color, float opacity,
        bool selected)
    {
        var path = ShapePath(ctx);
        var paint = ShapePaint(ctx);

        if (CornerRadius > 0)
            path.RoundRect(geometry.StartX, geometry.Py, geometry.BarW, geometry.BarH, CornerRadius);
        else
            path.Rect(geometry.StartX, geometry.Py, geometry.BarW, geometry.BarH);

        paint.SetColor(color).SetAntiAlias(true).SetOpacity(opacity);
        ctx.Canvas.Fill(path, paint);

        if (selected)
        {
            var selPaint = ShapePaint(ctx);
            ApplySelectionPaint(ctx, selPaint, opacity);
            ctx.Canvas.Stroke(path, selPaint);
        }

        if (ShowLabel)
        {
            var labelPaint = ShapePaint(ctx);
            labelPaint.SetColor(GetDataLabelColor(ctx)).SetOpacity(opacity);
            string label = ctx.Encodes.Resolve(Channel.Label, row)?.ToString()
                           ?? geometry.CategoryRaw.ToString() ?? "";
            // {0} = the bar's label, {1} = its start value, so "{0} ({1})" and friends work.
            label = FormatLabel(LabelFormat, label, geometry.StartRaw);
            DrawTextCentered(ctx, labelPaint, label,
                geometry.StartX + geometry.BarW / 2f, geometry.Py + geometry.BarH / 2f, FontSettings.Default);
        }
    }

    /// <summary>
    /// A timeline bar is opaque, so its hover fill can be painted on the overlay pass without touching the
    /// cached layer - including the label that sits inside it, which the same helper redraws.
    /// </summary>
    public override bool InteractionStateInOverlay => true;

    /// <inheritdoc />
    public override void Render(MarkContext ctx)
    {
        var resolved = ResolveScalesAndBar(ctx);
        if (resolved == null) return;
        var (yScale, xScale, barH) = resolved.Value;
        float anim  = ComputeAnimProgress(ctx);

        // The interaction state stays in the data layer only while the chart does not cache it; with the
        // cache on the overlay paints it (see InteractionStateInOverlay / RenderOverlay).
        bool stateHere = !ctx.StateInOverlay;

        for (int i = 0; i < ctx.Data.Count; i++)
        {
            if (!TryRowGeometry(ctx, yScale, xScale, barH, i, anim, out var geometry)) continue;

            var row = ctx.Data[i];
            var color = ResolveFill(ctx, row, i, GetDefaultColor(ctx));
            float opacity = ComputeElementOpacity(ctx, row, i);

            DrawBar(ctx, row, geometry, color, opacity, selected: stateHere && i == ctx.SelectedRowIndex);
        }
    }

    /// <inheritdoc />
    public override void RenderOverlay(MarkContext ctx)
    {
        // Only while the chart keeps the data layer in an image: with the cache off Render painted the state.
        if (!OverlayRows(ctx, out int hovered, out int selected)) return;

        var resolved = ResolveScalesAndBar(ctx);
        if (resolved == null) return;
        var (yScale, xScale, barH) = resolved.Value;
        float anim = ComputeAnimProgress(ctx);

        // Only the interactive rows are drawn, from the same geometry the data layer used.
        if (hovered >= 0) DrawInteractive(ctx, yScale, xScale, barH, anim, hovered, true);
        if (selected >= 0 && selected != hovered)
            DrawInteractive(ctx, yScale, xScale, barH, anim, selected, false);
    }

    /// <summary>
    /// Draw one interactive bar on the overlay pass: the hovered one takes the active fill (the data layer
    /// answered the default one while the state lives here) and the selected one gets its ring.
    /// </summary>
    private void DrawInteractive(MarkContext ctx, OrdinalScale yScale, LinearScale xScale, float barH,
        float anim, int index, bool hovered)
    {
        if (!TryRowGeometry(ctx, yScale, xScale, barH, index, anim, out var geometry)) return;

        var row = ctx.Data[index];
        var color = ResolveFill(ctx, row, index, GetDefaultColor(ctx));
        if (hovered) color = ActiveFillOf(ctx, color);
        float opacity = ComputeElementOpacity(ctx, row, index);

        DrawBar(ctx, row, geometry, color, opacity, selected: index == ctx.SelectedRowIndex);
    }

    /// <inheritdoc />
    public override HitResult? HitTest(MarkContext ctx, Vector2 screenPos)
    {
        var resolved = ResolveScalesAndBar(ctx);
        if (resolved == null) return null;
        var (yScale, xScale, barH) = resolved.Value;
        // Render reveals the bars with the entry animation; the hit area must not be wider.
        float anim = ComputeAnimProgress(ctx);

        for (int i = 0; i < ctx.Data.Count; i++)
        {
            // One walk, one geometry: a row the data layer does not draw - hidden, without its interval
            // endpoints, or standing on a category the lane scale does not know (an unknown category maps
            // to 0, i.e. onto the first lane) - must not be hittable either.
            if (!TryRowGeometry(ctx, yScale, xScale, barH, i, anim, out var geometry)) continue;

            float endX = geometry.StartX + geometry.BarW;
            if (screenPos.X >= geometry.StartX && screenPos.X <= endX &&
                screenPos.Y >= geometry.Py && screenPos.Y <= geometry.Py + geometry.BarH)
            {
                return new HitResult
                {
                    Hit = true, Row = ctx.Data[i], RowIndex = i,
                    ScreenX = (geometry.StartX + endX) / 2f,
                    ScreenY = geometry.Py + geometry.BarH / 2f,
                    Label = $"{geometry.CategoryRaw}: {geometry.StartRaw}–{geometry.EndRaw}",
                    MarkType = nameof(TimelineMark),
                };
            }
        }
        return null;
    }
}
