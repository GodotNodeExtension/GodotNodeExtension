using System;
using System.Collections.Generic;
using Godot;

namespace GodotNodeExtension.Component.GodotChart.Marks;

// ── IntervalMark (bar chart) ──────────────────────────────────

/// <summary>
/// Bar chart mark. Renders a bar for each data row, vertical by default or horizontal when
/// <see cref="Orientation"/> is <see cref="BarOrientation.Horizontal"/>.
/// Supports stacking via <see cref="Stack"/> property.
/// Animation: bars grow from baseline to target height.
/// </summary>
public class IntervalMark : Mark
{
    /// <inheritdoc />
    public override MarkCoordinate Coordinate => MarkCoordinate.Cartesian;

    /// <summary>Padding between bars as fraction of band width [0, 1).</summary>
    public float BarPadding   { get; set; } = 0.2f;

    /// <summary>Corner radius applied to all four corners of each bar, in pixels. Default 3.</summary>
    public float CornerRadius { get; set; } = 3f;

    /// <summary>
    /// Alias of <see cref="Mark.ShowLabel"/>: show the value text on each bar.
    /// Kept for backward compatibility; setting it has the same effect as <c>ShowLabel</c>.
    /// </summary>
    public bool ShowValue
    {
        get => ShowLabel;
        set => ShowLabel = value;
    }

    /// <summary>Stacking mode. Default is None (overlapping or grouped).</summary>
    public StackMode Stack { get; set; } = StackMode.None;

    /// <summary>Bar orientation. Default is Vertical. Set to Horizontal for horizontal bars.</summary>
    public BarOrientation Orientation { get; set; } = BarOrientation.Vertical;

    /// <summary>
    /// Lay the series of one category out <b>side by side</b> instead of on top of each other. The
    /// series of a row is the one the legend and the colour scale use (<see cref="Channel.Color"/>,
    /// or the series grouping when that channel is unbound); with the flag off - the default - every
    /// series keeps the full band and the bars of a category overlap, so which one you see depends on
    /// the draw order. Only meaningful with <see cref="Stack"/> left at <see cref="StackMode.None"/>.
    /// </summary>
    public bool GroupedBars { get; set; }

    // Cached stacked layout to avoid recomputation between Render and HitTest
    private StackedLayout? _cachedStackedLayout;
    private int _cachedStackedVersion = -1;
    private int _cachedStackedDataVersion = -1;
    private StackMode _cachedStackedMode = StackMode.None;

    // Cached series order for the grouped layout (one dictionary per data + layout version)
    private Dictionary<string, int>? _seriesBandIndex;
    private int _seriesBandCount;
    private int _seriesBandLayoutVersion = -1;
    private int _seriesBandDataVersion = -1;

    // Reusable buffers: the stacked render and hit test used to allocate these per frame /per call
    private readonly Dictionary<string, double> _stackedBaselines = new();
    private readonly List<(int idx, DataRow row, float px, float py, float w, float h)> _stackedHitCandidates = [];

    /// <inheritdoc />
    public override void ContributeScales(
        ScaleSet scales, EncodeSet encodes, List<DataRow> data)
    {
        // In the horizontal orientation the value axis is X and the categories are on Y, so both the
        // scaled channel and the grouping channel swap.
        bool horizontal = Orientation == BarOrientation.Horizontal;
        StackScaleHelper.ContributeStackedYScale(
            Stack, scales, encodes, data,
            horizontal ? Channel.X : YChannel,
            horizontal ? YChannel : Channel.X);
    }

    /// <inheritdoc />
    public override void Render(MarkContext ctx)
    {
        if (Stack != StackMode.None)
        {
            RenderStacked(ctx);
            return;
        }

        if (Orientation == BarOrientation.Horizontal)
        {
            RenderHorizontal(ctx);
            return;
        }

        var xScale = ctx.Scales.TryGet(Channel.X) as OrdinalScale;
        var yScale = ctx.Scales.TryGet(YChannel) as LinearScale;
        if (xScale == null || yScale == null) return;
        if (xScale.Domain.Count == 0) return;

        float slotW = ctx.Plot.Width / xScale.Domain.Count;
        float barW  = slotW * (1f - BarPadding);
        float anim  = ComputeAnimProgress(ctx);

        // Reused buffer: a fresh list per frame was a measurable allocation source.
        List<LabelElement>? labels = BeginLabelCollection();

        // The interaction-state visuals stay in the data layer only while the chart does not cache it; with
        // the cache on the overlay paints them (see Mark.InteractionStateInOverlay).
        bool stateHere = !ctx.StateInOverlay;

        for (int i = 0; i < ctx.Data.Count; i++)
        {
            var row   = ctx.Data[i];
            if (IsSeriesHidden(ctx, row)) continue;
            var xRaw  = ctx.Encodes.Resolve(Channel.X, row);
            var yRaw  = ctx.Encodes.Resolve(YChannel, row);
            if (xRaw == null || yRaw == null) continue;
            var color = ResolveFill(ctx, row, i, GetDefaultColor(ctx));
            float opacity = ComputeElementOpacity(ctx, row, i);

            bool isHovered  = stateHere && i == ctx.HoveredRowIndex;
            bool isSelected = stateHere && i == ctx.SelectedRowIndex;

            DrawVerticalBar(ctx, i, row, xRaw, yRaw, xScale, yScale, barW, anim,
                isHovered, isSelected, opacity, color, labels);
        }

        if (labels != null)
            DrawLabels(ctx, labels);
    }

    /// <summary>
    /// Draw one vertical bar with the interaction state the row has right now (a hovered bar is widened and
    /// painted with the active fill, a selected one gets the ring) and collect its label element.
    /// <para>
    /// <see cref="Render"/> walks the table through this and the overlay pass
    /// (<see cref="RenderOverlay"/>) calls it for the interactive rows only. Both go through the same
    /// geometry (<see cref="BarBand"/> / <see cref="VerticalBarRect"/>), so the highlight cannot drift away
    /// from the bar it belongs to.
    /// </para>
    /// </summary>
    private void DrawVerticalBar(MarkContext ctx, int index, DataRow row, object xRaw, object yRaw,
        OrdinalScale xScale, LinearScale yScale, float barW, float anim,
        bool hovered, bool selected, float opacity, Color color, List<LabelElement>? labels)
    {
        // Hover: widen the bar (the fill brightening is applied below / by ResolveFill).
        var (xNorm, barWidth) = BarBand(ctx, row, (float)MapSafely(xScale, xRaw), barW, ctx.Plot.Width);
        float drawBarW = hovered ? barWidth * GetHoverScale(ctx) : barWidth;

        // A non-finite value - or one the scale cannot read at all - has no position: skip the row
        // instead of drawing at NaN.
        if (!double.IsFinite(MapSafely(yScale, yRaw))) return;

        // Grows from the data zero line, not from the plot bottom (see VerticalBarRect).
        var (px, py, rectW, ph) = VerticalBarRect(ctx, yScale, yRaw, xNorm, anim, drawBarW);

        var path = ShapePath(ctx);
        var paint = ShapePaint(ctx);

        if (CornerRadius > 0)
            path.RoundRect(px, py, rectW, ph, CornerRadius);
        else
            path.Rect(px, py, rectW, ph);

        // While the overlay owns the state, ResolveFill paints the element's default fill - the hover
        // brighten is added here, exactly as it is for a bar the data layer highlights itself.
        paint.SetColor(hovered && ctx.StateInOverlay ? ActiveFillOf(ctx, color) : color)
             .SetAntiAlias(true).SetOpacity(opacity);
        ctx.Canvas.Fill(path, paint);

        // Selected: draw highlight stroke
        if (selected)
        {
            var strokePaint = ShapePaint(ctx);
            ApplySelectionPaint(ctx, strokePaint, opacity);
            ctx.Canvas.Stroke(path, strokePaint);
        }

        // Collect label element
        labels?.Add(new LabelElement(px + drawBarW * 0.5f, py,
            FormatLabel(LabelFormat, yRaw, xRaw), opacity, row, index, color, LabelValue(yRaw)));
    }

    private void RenderHorizontal(MarkContext ctx)
    {
        // Horizontal bars: Y = categories (OrdinalScale), X = values (LinearScale)
        var yScale = ctx.Scales.TryGet(YChannel) as OrdinalScale ?? ctx.Scales.TryGet(Channel.X) as OrdinalScale;
        var xScale = ctx.Scales.TryGet(Channel.X) as LinearScale ?? ctx.Scales.TryGet(YChannel) as LinearScale;
        if (yScale == null || xScale == null) return;
        if (yScale.Domain.Count == 0) return;

        float slotH = ctx.Plot.Height / yScale.Domain.Count;
        float barH  = slotH * (1f - BarPadding);
        float anim  = ComputeAnimProgress(ctx);

        // Reused buffer: a fresh list per frame was a measurable allocation source.
        List<LabelElement>? labels = BeginLabelCollection();
        bool stateHere = !ctx.StateInOverlay;

        for (int i = 0; i < ctx.Data.Count; i++)
        {
            var row   = ctx.Data[i];
            if (IsSeriesHidden(ctx, row)) continue;
            var catRaw = ctx.Encodes.Resolve(YChannel, row) ?? ctx.Encodes.Resolve(Channel.X, row);
            var valRaw = ctx.Encodes.Resolve(Channel.X, row) ?? ctx.Encodes.Resolve(YChannel, row);
            if (catRaw == null || valRaw == null) continue;
            var color = ResolveFill(ctx, row, i, GetDefaultColor(ctx));
            float opacity = ComputeElementOpacity(ctx, row, i);

            bool isHovered  = stateHere && i == ctx.HoveredRowIndex;
            bool isSelected = stateHere && i == ctx.SelectedRowIndex;

            DrawHorizontalBar(ctx, i, row, catRaw, valRaw, yScale, xScale, barH, anim,
                isHovered, isSelected, opacity, color, labels);
        }

        if (labels != null)
            DrawLabels(ctx, labels);
    }

    /// <summary>
    /// Draw one horizontal bar with the interaction state the row has right now and collect its label
    /// element - the horizontal twin of <see cref="DrawVerticalBar"/>.
    /// </summary>
    private void DrawHorizontalBar(MarkContext ctx, int index, DataRow row, object catRaw, object valRaw,
        OrdinalScale yScale, LinearScale xScale, float barH, float anim,
        bool hovered, bool selected, float opacity, Color color, List<LabelElement>? labels)
    {
        var (yNorm, barHeight) = BarBand(ctx, row, (float)MapSafely(yScale, catRaw), barH, ctx.Plot.Height);

        // Hover: widen the bar (the fill brightening is applied below / by ResolveFill).
        float drawBarH = hovered ? barHeight * GetHoverScale(ctx) : barHeight;

        if (!double.IsFinite(MapSafely(xScale, valRaw))) return;

        // Same baseline/sign rules as the vertical orientation (see HorizontalBarRect).
        var (barLeft, py, barWidth, rectH) =
            HorizontalBarRect(ctx, xScale, valRaw, yNorm, anim, drawBarH);

        var path = ShapePath(ctx);
        var paint = ShapePaint(ctx);

        if (CornerRadius > 0)
            path.RoundRect(barLeft, py, barWidth, rectH, CornerRadius);
        else
            path.Rect(barLeft, py, barWidth, rectH);

        paint.SetColor(hovered && ctx.StateInOverlay ? ActiveFillOf(ctx, color) : color)
             .SetAntiAlias(true).SetOpacity(opacity);
        ctx.Canvas.Fill(path, paint);

        if (selected)
        {
            var strokePaint = ShapePaint(ctx);
            ApplySelectionPaint(ctx, strokePaint, opacity);
            ctx.Canvas.Stroke(path, strokePaint);
        }

        // Labels are produced here as well: ShowLabel used to be ignored for horizontal bars.
        labels?.Add(new LabelElement(barLeft + barWidth, py + rectH * 0.5f,
            FormatLabel(LabelFormat, valRaw, catRaw), opacity, row, index, color, LabelValue(valRaw)));
    }

    /// <summary>
    /// True when the interaction-state visuals of this mark live on the overlay pass. Both layouts do:
    /// the plain bars draw their state through <see cref="DrawVerticalBar"/>, and a stacked segment
    /// through <see cref="DrawStackedSegments"/>, which the overlay re-walks for the interactive rows
    /// (the segment's rectangle starts where the segments below it end, so its baseline is the
    /// accumulation, not a value the overlay can read off the row).
    /// </summary>
    public override bool InteractionStateInOverlay => true;

    /// <inheritdoc />
    public override void RenderOverlay(MarkContext ctx)
    {
        // Only while the chart keeps the data layer in an image: with the cache off Render painted the state.
        // The rows are read where they are needed (the stacked branch walks the layout, the plain one the
        // interaction rows).
        if (!OverlayRows(ctx, out _, out _)) return;

        // A stacked segment's geometry depends on the accumulation below it: the overlay walks the same
        // layout, keeps only the baselines and draws the interactive segment(s) - no label buffer, the
        // labels of a cached frame are already in the layer.
        if (Stack != StackMode.None)
        {
            DrawStackedSegments(ctx, stateHere: true, labelBuffer: null, interactiveOnly: true);
            return;
        }

        foreach (int index in InteractionRows(ctx))
        {
            if (index < 0 || index >= ctx.Data.Count) continue;
            var row = ctx.Data[index];
            if (IsSeriesHidden(ctx, row)) continue;

            bool hovered = index == ctx.HoveredRowIndex;
            bool selected = index == ctx.SelectedRowIndex;
            bool horizontal = Orientation == BarOrientation.Horizontal;

            // The value/category channels swap with the orientation, exactly like Render does it.
            var catRaw = horizontal
                ? ctx.Encodes.Resolve(YChannel, row) ?? ctx.Encodes.Resolve(Channel.X, row)
                : ctx.Encodes.Resolve(Channel.X, row);
            var valRaw = horizontal
                ? ctx.Encodes.Resolve(Channel.X, row) ?? ctx.Encodes.Resolve(YChannel, row)
                : ctx.Encodes.Resolve(YChannel, row);
            if (catRaw == null || valRaw == null) continue;

            var color = ResolveFill(ctx, row, index, GetDefaultColor(ctx));
            float opacity = ComputeElementOpacity(ctx, row, index);
            float anim = ComputeAnimProgress(ctx);

            if (horizontal)
            {
                var yScale = ctx.Scales.TryGet(YChannel) as OrdinalScale ?? ctx.Scales.TryGet(Channel.X) as OrdinalScale;
                var xScale = ctx.Scales.TryGet(Channel.X) as LinearScale ?? ctx.Scales.TryGet(YChannel) as LinearScale;
                if (yScale == null || xScale == null || yScale.Domain.Count == 0) return;

                DrawHorizontalBar(ctx, index, row, catRaw, valRaw, yScale, xScale,
                    ctx.Plot.Height / yScale.Domain.Count * (1f - BarPadding), anim,
                    hovered, selected, opacity, color, null);
            }
            else
            {
                var xScale = ctx.Scales.TryGet(Channel.X) as OrdinalScale;
                var yScale = ctx.Scales.TryGet(YChannel) as LinearScale;
                if (xScale == null || yScale == null || xScale.Domain.Count == 0) return;

                DrawVerticalBar(ctx, index, row, catRaw, valRaw, xScale, yScale,
                    ctx.Plot.Width / xScale.Domain.Count * (1f - BarPadding), anim,
                    hovered, selected, opacity, color, null);
            }
        }
    }

    /// <summary>
    /// Resolve the category (ordinal) and value (linear) axes for the stacked paths. In the
    /// horizontal orientation the roles of X and Y are swapped, so the caller must not assume them.
    /// </summary>
    private (OrdinalScale? Category, LinearScale? Value, Channel CategoryChannel, Channel ValueChannel)
        ResolveStackedAxes(MarkContext ctx)
    {
        if (Orientation == BarOrientation.Horizontal)
        {
            bool catOnY = ctx.Scales.TryGet(YChannel) is OrdinalScale;
            bool valOnX = ctx.Scales.TryGet(Channel.X) is LinearScale;
            return (
                ctx.Scales.TryGet(YChannel) as OrdinalScale ?? ctx.Scales.TryGet(Channel.X) as OrdinalScale,
                ctx.Scales.TryGet(Channel.X) as LinearScale ?? ctx.Scales.TryGet(YChannel) as LinearScale,
                catOnY ? YChannel : Channel.X,
                valOnX ? Channel.X : YChannel);
        }

        return (
            ctx.Scales.TryGet(Channel.X) as OrdinalScale,
            ctx.Scales.TryGet(YChannel) as LinearScale,
            Channel.X,
            YChannel);
    }

    /// <summary>
    /// Screen rectangle of one stacked segment: it starts at <b>its own</b> baseline (the accumulated
    /// value of the series below) and grows by its value, animated from that baseline. A vertical
    /// segment grows along Y, a horizontal one along X. Render and HitTest share this so the hit area
    /// always matches the drawing.
    /// </summary>
    private static (float X, float Y, float W, float H) StackedSegmentRect(
        MarkContext ctx, LinearScale valueScale, float categoryNorm,
        double baseValue, double value, float thickness, float anim, bool horizontal)
    {
        if (horizontal)
        {
            float segBase = ctx.Plot.MapX((float)valueScale.Map(baseValue));
            float target  = ctx.Plot.MapX((float)valueScale.Map(baseValue + value));
            float current = segBase + (target - segBase) * anim;
            float top     = ctx.Plot.MapY(categoryNorm) - thickness * 0.5f;
            return (Math.Min(current, segBase), top, Math.Abs(segBase - current), thickness);
        }

        float baseY = ctx.Plot.MapY((float)valueScale.Map(baseValue));
        float targetY = ctx.Plot.MapY((float)valueScale.Map(baseValue + value));
        float curY = baseY + (targetY - baseY) * anim;
        float left = ctx.Plot.MapX(categoryNorm) - thickness * 0.5f;
        return (left, Math.Min(curY, baseY), thickness, Math.Abs(baseY - curY));
    }

    private sealed record StackedLayout(
        List<object> SeriesOrder,
        Dictionary<object, List<(int idx, DataRow row)>> SeriesData,
        Dictionary<string, float>? CategoryTotals);

    private StackedLayout ComputeStackedLayout(MarkContext ctx)
    {
        var seriesOrder = new List<object>();
        var seriesData  = new Dictionary<object, List<(int idx, DataRow row)>>();

        for (int i = 0; i < ctx.Data.Count; i++)
        {
            var row = ctx.Data[i];
            object seriesKey = ctx.Encodes.Has(Channel.Color)
                ? ctx.Encodes.Resolve(Channel.Color, row) ?? "__default__"
                : "__default__";

            if (!seriesData.TryGetValue(seriesKey, out var seriesRows))
            {
                seriesOrder.Add(seriesKey);
                seriesRows = [];
                seriesData[seriesKey] = seriesRows;
            }
            seriesRows.Add((i, row));
        }

        Dictionary<string, float>? categoryTotals = null;
        if (Stack == StackMode.Normalize)
        {
            var (_, _, catCh, valCh) = ResolveStackedAxes(ctx);
            categoryTotals = new Dictionary<string, float>();
            foreach (var row in ctx.Data)
            {
                // Hidden series are not drawn, so they must not count towards the total either: with
                // them in it, hiding one series left the visible bands short of the full 1.0.
                if (IsSeriesHidden(ctx, row)) continue;
                var key = ctx.Encodes.Resolve(catCh, row)?.ToString() ?? "";
                var valueRaw = ctx.Encodes.Resolve(valCh, row);
                if (valueRaw == null) continue;
                float value = ToSingle(valueRaw, "value");
                if (!float.IsFinite(value)) continue;
                categoryTotals[key] = categoryTotals.GetValueOrDefault(key) + value;
            }
        }

        return new StackedLayout(seriesOrder, seriesData, categoryTotals);
    }

    private StackedLayout GetOrComputeStackedLayout(MarkContext ctx)
    {
        // The cached layout depends on the rows as much as on the layout version (a mark instance can be
        // moved to another chart, or handed another data list) and on the mode it was built with.
        if (_cachedStackedLayout == null || _cachedStackedVersion != ctx.LayoutVersion
            || _cachedStackedDataVersion != ctx.DataVersion || _cachedStackedMode != Stack)
        {
            _cachedStackedLayout = ComputeStackedLayout(ctx);
            _cachedStackedVersion = ctx.LayoutVersion;
            _cachedStackedDataVersion = ctx.DataVersion;
            _cachedStackedMode = Stack;
        }
        return _cachedStackedLayout;
    }

    private double NormalizeY(double yVal, string xKey, StackedLayout layout)
    {
        if (Stack != StackMode.Normalize || layout.CategoryTotals == null) return yVal;

        // A category whose visible total is not positive has no share to draw: keeping the raw value
        // would paint it against the fixed [0, 1] axis and run outside the plot.
        float total = layout.CategoryTotals.GetValueOrDefault(xKey, 0f);
        if (total > 0) return yVal / total;

        WarnNormalizeZeroTotal();
        return 0;
    }

    private void RenderStacked(MarkContext ctx)
    {
        // The interaction-state visuals stay in the data layer only while the chart does not cache it
        // (see Mark.InteractionStateInOverlay); with the cache on the overlay pass paints them, from the
        // very same loop (see RenderOverlay / DrawStackedSegments).
        DrawStackedSegments(ctx, stateHere: !ctx.StateInOverlay,
            labelBuffer: BeginLabelCollection(), interactiveOnly: false);
    }

    /// <summary>
    /// Draw every stacked segment of the frame (or only the interactive ones) and, while the data layer
    /// owns the interaction state, collect the label elements as well.
    /// <para>
    /// A segment's rectangle starts at the accumulated value of the series below it, so the pass always
    /// walks the whole layout; <paramref name="interactiveOnly"/> only skips the drawing of the segments
    /// that carry no interaction state, which is what the overlay pass needs (it reuses the accumulation
    /// to place the hover/selection visual of the interactive row).
    /// </para>
    /// </summary>
    /// <param name="ctx">Context of the frame.</param>
    /// <param name="stateHere">True when this pass paints the interaction state (the data layer while the
    /// chart does not cache it, the overlay pass otherwise).</param>
    /// <param name="labelBuffer">Label buffer to collect into, or null when no labels are drawn.</param>
    /// <param name="interactiveOnly">True when only the hovered/selected segments are drawn (overlay pass).</param>
    private void DrawStackedSegments(MarkContext ctx, bool stateHere, List<LabelElement>? labelBuffer,
        bool interactiveOnly)
    {
        var (catScale, valScale, catCh, valCh) = ResolveStackedAxes(ctx);
        if (catScale == null || valScale == null) return;
        if (catScale.Domain.Count == 0) return;

        bool horizontal = Orientation == BarOrientation.Horizontal;
        float slot = horizontal
            ? ctx.Plot.Height / catScale.Domain.Count
            : ctx.Plot.Width / catScale.Domain.Count;
        float barW = slot * (1f - BarPadding);
        float anim = ComputeAnimProgress(ctx);

        var layout = GetOrComputeStackedLayout(ctx);

        // Accumulated baselines per X in data space (reused buffer)
        var baselines = _stackedBaselines;
        baselines.Clear();

        List<LabelElement>? labels = labelBuffer;

        foreach (var sk in layout.SeriesOrder)
        {
            // Skip hidden series
            string? skStr = sk.ToString();
            if (ctx.HiddenSeries != null && ctx.HiddenSeries.Contains(skStr ?? ""))
                continue;

            var color = ResolveSeriesColor(ctx, sk);

            foreach (var (dataIdx, row) in layout.SeriesData[sk])
            {
                var catRaw = ctx.Encodes.Resolve(catCh, row);
                if (catRaw == null) continue;
                string catKey = catRaw.ToString() ?? "";
                var valueRaw = ctx.Encodes.Resolve(valCh, row);
                if (valueRaw == null) continue;
                double value = NormalizeY(ToDouble(valueRaw, "value"), catKey, layout);
                // ToDouble reports a value that is not a number as NaN instead of throwing, so the row is
                // skipped here: an unguarded NaN would make this segment's rectangle non-finite.
                if (!double.IsFinite(value)) continue;
                float opacity = ComputeElementOpacity(ctx, row, dataIdx);

                double baseline = baselines.GetValueOrDefault(catKey, 0);

                bool isHovered  = stateHere && dataIdx == ctx.HoveredRowIndex;
                bool isSelected = stateHere && dataIdx == ctx.SelectedRowIndex;

                // The accumulation has to happen for every segment, including the ones the overlay pass
                // does not draw: the segments above it start where it ends.
                if (interactiveOnly && !isHovered && !isSelected)
                {
                    baselines[catKey] = baseline + value;
                    continue;
                }

                // Series colour as the default: ResolveFill keeps the per-series colour and applies
                // the style callback plus the hover brighten (which stays in the data layer while the
                // chart does not cache it, and belongs to this pass otherwise).
                Color drawColor = ResolveFill(ctx, row, dataIdx, color);
                float drawThickness = barW;
                if (isHovered)
                {
                    drawThickness *= GetHoverScale(ctx);
                    // While the overlay owns the state, ResolveFill answered the default fill: the hover
                    // brighten is added here, exactly as it is for a bar the data layer highlights itself.
                    if (ctx.StateInOverlay) drawColor = ActiveFillOf(ctx, drawColor);
                }

                // Shared with HitTestStacked: the same helper produces the drawn and the hit rectangle.
                // The segment's own size (segW/segH) is separate from the slot width barW above, which is
                // its un-scaled source.
                var (px, pyTop, segW, segH) = StackedSegmentRect(
                    ctx, valScale, (float)MapSafely(catScale, catRaw), baseline, value, drawThickness, anim, horizontal);

                var path = ShapePath(ctx);
                var paint = ShapePaint(ctx);
                if (CornerRadius > 0)
                    path.RoundRect(px, pyTop, segW, segH, CornerRadius);
                else
                    path.Rect(px, pyTop, segW, segH);

                paint.SetColor(drawColor).SetAntiAlias(true).SetOpacity(opacity);
                ctx.Canvas.Fill(path, paint);

                if (isSelected)
                {
                    var strokePaint = ShapePaint(ctx);
                    ApplySelectionPaint(ctx, strokePaint, opacity);
                    ctx.Canvas.Stroke(path, strokePaint);
                }

                // ShowLabel used to be ignored in stacked mode.
                if (labels != null)
                {
                    string text = FormatLabel(LabelFormat, valueRaw, catRaw);
                    labels.Add(new LabelElement(px + segW * 0.5f, pyTop, text, opacity,
                        row, dataIdx, drawColor, LabelValue(valueRaw)));
                }

                baselines[catKey] = baseline + value;
            }
        }

        if (labels != null)
            DrawLabels(ctx, labels);
    }

    /// <summary>Screen Y of the data zero line, clamped to the plot.</summary>
    private static float ZeroLineY(MarkContext ctx, LinearScale yScale)
        => ctx.Plot.MapY(Math.Clamp((float)yScale.Map(0), 0f, 1f));

    /// <summary>Screen X of the data zero line, clamped to the plot.</summary>
    private static float ZeroLineX(MarkContext ctx, LinearScale xScale)
        => ctx.Plot.MapX(Math.Clamp((float)xScale.Map(0), 0f, 1f));

    /// <summary>
    /// Rectangle of a vertical bar: it grows from the data zero line (not from the bottom of the
    /// plot) and always has a positive height, so negative values stay visible below the baseline.
    /// Render and HitTest share this helper so the hit area always matches the drawing.
    /// </summary>
    private static (float X, float Y, float W, float H) VerticalBarRect(
        MarkContext ctx, LinearScale yScale, object yRaw, float xNorm, float anim, float barWidth)
    {
        float baseline = ZeroLineY(ctx, yScale);
        float target   = ctx.Plot.MapY((float)yScale.Map(yRaw));
        float current  = baseline + (target - baseline) * anim;
        return (ctx.Plot.MapX(xNorm) - barWidth * 0.5f,
                Math.Min(current, baseline),
                barWidth,
                Math.Abs(baseline - current));
    }

    /// <summary>Rectangle of a horizontal bar; see <see cref="VerticalBarRect"/>.</summary>
    private static (float X, float Y, float W, float H) HorizontalBarRect(
        MarkContext ctx, LinearScale xScale, object xRaw, float yNorm, float anim, float barHeight)
    {
        float baseline = ZeroLineX(ctx, xScale);
        float target   = ctx.Plot.MapX((float)xScale.Map(xRaw));
        float current  = baseline + (target - baseline) * anim;
        return (Math.Min(current, baseline),
                ctx.Plot.MapY(yNorm) - barHeight * 0.5f,
                Math.Abs(baseline - current),
                barHeight);
    }

    /// <summary>
    /// Band position and bar size of one row along the category axis. Every series of a category
    /// shares the category band; with <see cref="GroupedBars"/> each series gets a sub-band of its own
    /// (spread symmetrically around the category centre), otherwise each keeps the whole band and the
    /// bars overlap. Render and HitTest both go through here, so the hit area always matches the
    /// drawing.
    /// </summary>
    /// <param name="ctx">Context of the current frame (scales, plot, data version).</param>
    /// <param name="row">Row whose series decides the sub-band.</param>
    /// <param name="categoryNorm">Normalised category centre along the category axis.</param>
    /// <param name="bandSize">Full band size in pixels (the category slot minus <see cref="BarPadding"/>).</param>
    /// <param name="plotExtent">Pixel extent of the plot along the category axis.</param>
    private (float Norm, float Size) BarBand(
        MarkContext ctx, DataRow row, float categoryNorm, float bandSize, float plotExtent)
    {
        if (!GroupedBars || plotExtent <= 0f) return (categoryNorm, bandSize);

        var (index, count) = GetSeriesBands(ctx);
        if (count <= 1) return (categoryNorm, bandSize);

        int band = index.GetValueOrDefault(ResolveSeriesKey(ctx, row) ?? string.Empty);
        float subSize = bandSize / count;
        float offsetNorm = (band - (count - 1) * 0.5f) * subSize / plotExtent;
        return (categoryNorm + offsetNorm, subSize);
    }

    /// <summary>
    /// Series key → sub-band index, in first-seen order (the same order the colour scale and the
    /// legend use), plus how many there are. Cached per data and layout version, because Render and
    /// HitTest ask for it once per row.
    /// </summary>
    private (Dictionary<string, int> Index, int Count) GetSeriesBands(MarkContext ctx)
    {
        if (_seriesBandIndex == null || _seriesBandLayoutVersion != ctx.LayoutVersion
            || _seriesBandDataVersion != ctx.DataVersion)
        {
            _seriesBandIndex = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var row in ctx.Data)
            {
                // A hidden series draws no bar, so it must not hold a sub-band either: with it counted, the
                // visible bars were pushed into a wider slot and left an empty stripe where it would have been.
                if (IsSeriesHidden(ctx, row)) continue;
                string key = ResolveSeriesKey(ctx, row) ?? string.Empty;
                if (!_seriesBandIndex.ContainsKey(key)) _seriesBandIndex[key] = _seriesBandIndex.Count;
            }
            _seriesBandCount = _seriesBandIndex.Count;
            _seriesBandLayoutVersion = ctx.LayoutVersion;
            _seriesBandDataVersion = ctx.DataVersion;
        }

        return (_seriesBandIndex, _seriesBandCount);
    }

    /// <inheritdoc />
    public override HitResult? HitTest(MarkContext ctx, Vector2 screenPos)
    {
        if (Stack != StackMode.None)
            return HitTestStacked(ctx, screenPos);

        if (Orientation == BarOrientation.Horizontal)
            return HitTestHorizontal(ctx, screenPos);

        var xScale = ctx.Scales.TryGet(Channel.X) as OrdinalScale;
        var yScale = ctx.Scales.TryGet(YChannel) as LinearScale;
        if (xScale == null || yScale == null) return null;
        if (xScale.Domain.Count == 0) return null;

        float slotW = ctx.Plot.Width / xScale.Domain.Count;
        float barW  = slotW * (1f - BarPadding);
        float anim  = ComputeAnimProgress(ctx);

        // Iterate in reverse so the topmost (last-rendered) bar is hit first
        for (int i = ctx.Data.Count - 1; i >= 0; i--)
        {
            var row   = ctx.Data[i];
            if (IsSeriesHidden(ctx, row)) continue;
            var xRaw  = ctx.Encodes.Resolve(Channel.X, row);
            var yRaw  = ctx.Encodes.Resolve(YChannel, row);
            if (xRaw == null || yRaw == null) continue;

            var (xNorm, barWidth) = BarBand(ctx, row, (float)MapSafely(xScale, xRaw), barW, ctx.Plot.Width);
            // Render widens a hovered bar, so the hit area has to widen with it.
            if (i == ctx.HoveredRowIndex) barWidth *= GetHoverScale(ctx);
            if (!double.IsFinite(MapSafely(yScale, yRaw))) continue;

            // Identical rectangle to Render (shared helper), including the animation factor.
            var (px, py, rectW, ph) = VerticalBarRect(ctx, yScale, yRaw, xNorm, anim, barWidth);

            if (screenPos.X >= px && screenPos.X <= px + rectW && screenPos.Y >= py && screenPos.Y <= py + ph)
            {
                return new HitResult
                {
                    Hit = true, Row = row, RowIndex = i,
                    ScreenX = px + rectW / 2f, ScreenY = py,
                    Label = $"{xRaw}: {yRaw}",
                    SeriesKey = ResolveSeriesKey(ctx, row),
                    MarkType = nameof(IntervalMark),
                };
            }
        }
        return null;
    }

    private HitResult? HitTestHorizontal(MarkContext ctx, Vector2 pos)
    {
        var yScale = ctx.Scales.TryGet(YChannel) as OrdinalScale ?? ctx.Scales.TryGet(Channel.X) as OrdinalScale;
        var xScale = ctx.Scales.TryGet(Channel.X) as LinearScale ?? ctx.Scales.TryGet(YChannel) as LinearScale;
        if (yScale == null || xScale == null) return null;

        float slotH = ctx.Plot.Height / yScale.Domain.Count;
        float barH  = slotH * (1f - BarPadding);
        float anim  = ComputeAnimProgress(ctx);

        // Iterate in reverse so the topmost (last-rendered) bar is hit first
        for (int i = ctx.Data.Count - 1; i >= 0; i--)
        {
            var row    = ctx.Data[i];
            if (IsSeriesHidden(ctx, row)) continue;
            var catRaw = ctx.Encodes.Resolve(YChannel, row) ?? ctx.Encodes.Resolve(Channel.X, row);
            var valRaw = ctx.Encodes.Resolve(Channel.X, row) ?? ctx.Encodes.Resolve(YChannel, row);
            if (catRaw == null || valRaw == null) continue;

            var (yNorm, barHeight) = BarBand(ctx, row, (float)MapSafely(yScale, catRaw), barH, ctx.Plot.Height);
            // Render widens a hovered bar, so the hit area has to widen with it.
            if (i == ctx.HoveredRowIndex) barHeight *= GetHoverScale(ctx);
            if (!double.IsFinite(MapSafely(xScale, valRaw))) continue;

            // Identical rectangle to RenderHorizontal (shared helper), including the animation.
            var (barLeft, py, barWidth, rectH) =
                HorizontalBarRect(ctx, xScale, valRaw, yNorm, anim, barHeight);

            if (pos.X >= barLeft && pos.X <= barLeft + barWidth &&
                pos.Y >= py && pos.Y <= py + rectH)
            {
                return new HitResult
                {
                    Hit = true, Row = row, RowIndex = i,
                    ScreenX = barLeft + barWidth, ScreenY = py + rectH / 2f,
                    Label = $"{catRaw}: {valRaw}",
                    SeriesKey = ResolveSeriesKey(ctx, row),
                    MarkType = nameof(IntervalMark),
                };
            }
        }
        return null;
    }

    private HitResult? HitTestStacked(MarkContext ctx, Vector2 pos)
    {
        var (catScale, valScale, catCh, valCh) = ResolveStackedAxes(ctx);
        if (catScale == null || valScale == null) return null;
        if (catScale.Domain.Count == 0) return null;

        bool horizontal = Orientation == BarOrientation.Horizontal;
        float slot = horizontal
            ? ctx.Plot.Height / catScale.Domain.Count
            : ctx.Plot.Width / catScale.Domain.Count;
        float barW = slot * (1f - BarPadding);
        float anim = ComputeAnimProgress(ctx);

        var layout = GetOrComputeStackedLayout(ctx);
        var candidates = _stackedHitCandidates;
        candidates.Clear();
        var baselines = _stackedBaselines;
        baselines.Clear();

        foreach (var sk in layout.SeriesOrder)
        {
            // Skip hidden series
            string? skStr = sk.ToString();
            if (ctx.HiddenSeries != null && ctx.HiddenSeries.Contains(skStr ?? ""))
                continue;

            foreach (var (dataIdx, row) in layout.SeriesData[sk])
            {
                var catRaw = ctx.Encodes.Resolve(catCh, row);
                if (catRaw == null) continue;
                string catKey = catRaw.ToString() ?? "";
                var valueRaw = ctx.Encodes.Resolve(valCh, row);
                if (valueRaw == null) continue;
                double value = NormalizeY(ToDouble(valueRaw, "value"), catKey, layout);
                if (!double.IsFinite(value)) continue;   // see RenderStacked: a value without a number is skipped

                double baseline = baselines.GetValueOrDefault(catKey, 0);

                // Same rectangle as RenderStacked (shared helper), including the animation factor.
                var (px, py, w, h) = StackedSegmentRect(
                    ctx, valScale, (float)MapSafely(catScale, catRaw), baseline, value, barW, anim, horizontal);

                candidates.Add((dataIdx, row, px, py, w, h));
                baselines[catKey] = baseline + value;
            }
        }

        // Check in reverse order so topmost bar is hit first
        for (int i = candidates.Count - 1; i >= 0; i--)
        {
            var (idx, row, px, py, w, h) = candidates[i];
            if (pos.X >= px && pos.X <= px + w && pos.Y >= py && pos.Y <= py + h)
            {
                return new HitResult
                {
                    Hit = true, Row = row, RowIndex = idx,
                    ScreenX = px + w / 2f, ScreenY = py + h / 2f,
                    Label = $"{ctx.Encodes.Resolve(catCh, row)}: {ctx.Encodes.Resolve(valCh, row)}",
                    SeriesKey = ResolveSeriesKey(ctx, row),
                    MarkType = nameof(IntervalMark),
                };
            }
        }
        return null;
    }
}
