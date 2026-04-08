using System;
using System.Collections.Generic;
using Godot;

namespace GodotNodeExtension.Component.GodotChart.Marks;

// ── IntervalMark (bar chart) ──────────────────────────────────

/// <summary>
/// Bar chart mark. Renders vertical bars for each data row.
/// Supports stacking via <see cref="Stack"/> property.
/// Animation: bars grow from baseline to target height.
/// </summary>
public class IntervalMark : Mark
{
    /// <inheritdoc />
    public override MarkCoordinate Coordinate => MarkCoordinate.Cartesian;

    /// <summary>Padding between bars as fraction of band width [0, 1).</summary>
    public float BarPadding   { get; set; } = 0.2f;

    /// <summary>Corner radius for rounded bar tops in pixels.</summary>
    public float CornerRadius { get; set; } = 3f;

    /// <summary>Whether to display the numeric value above each bar.</summary>
    public bool  ShowValue    { get; set; } = false;

    /// <summary>Stacking mode. Default is None (overlapping or grouped).</summary>
    public StackMode Stack { get; set; } = StackMode.None;

    /// <summary>Bar orientation. Default is Vertical. Set to Horizontal for horizontal bars.</summary>
    public BarOrientation Orientation { get; set; } = BarOrientation.Vertical;

    // Cached stacked layout to avoid recomputation between Render and HitTest
    private StackedLayout? _cachedStackedLayout;
    private int _cachedStackedVersion = -1;

    /// <inheritdoc />
    public override void ContributeScales(
        ScaleSet scales, EncodeSet encodes, List<DataRow> data)
    {
        StackScaleHelper.ContributeStackedYScale(Stack, scales, encodes, data, YChannel);
    }

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

        List<LabelElement>? labels = ShowLabel ? new List<LabelElement>() : null;

        for (int i = 0; i < ctx.Data.Count; i++)
        {
            var row   = ctx.Data[i];
            if (IsSeriesHidden(ctx, row)) continue;
            var xRaw  = ctx.Encodes.Resolve(Channel.X, row);
            var yRaw  = ctx.Encodes.Resolve(YChannel, row);
            if (xRaw == null || yRaw == null) continue;
            var color = ResolveColorWithOverride(ctx, row, i, GetDefaultColor(ctx));
            float opacity = ComputeEffectiveOpacity(ctx, row);

            bool isHovered  = i == ctx.HoveredRowIndex;
            bool isSelected = i == ctx.SelectedRowIndex;

            // Hover: brighten color and widen bar
            float drawBarW = barW;
            if (isHovered)
            {
                color = BrightenColor(color, GetHoverBrighten(ctx));
                drawBarW *= GetHoverScale(ctx);
            }

            float xNorm = (float)xScale.Map(xRaw);
            float yNorm = (float)yScale.Map(yRaw);

            float baseline = ctx.Plot.MapY(0);
            float targetY  = ctx.Plot.MapY(yNorm);
            // Animation: interpolate from baseline to target
            float py = baseline + (targetY - baseline) * anim;
            float ph = baseline - py;

            float px = ctx.Plot.MapX(xNorm) - drawBarW * 0.5f;

            using var path  = ctx.Canvas.CreatePath();
            using var paint = ctx.Canvas.CreatePaint();

            if (CornerRadius > 0)
                path.RoundRect(px, py, drawBarW, ph, CornerRadius);
            else
                path.Rect(px, py, drawBarW, ph);

            paint.SetColor(color).SetAntiAlias(true).SetOpacity(opacity);
            ctx.Canvas.Fill(path, paint);

            // Selected: draw highlight stroke
            if (isSelected)
            {
                using var strokePaint = ctx.Canvas.CreatePaint();
                ApplySelectionPaint(ctx, strokePaint, opacity);
                ctx.Canvas.Stroke(path, strokePaint);
            }

            // Collect label element
            if (labels != null)
            {
                string text = string.Format(LabelFormat, yRaw, xRaw);
                float labelX = px + drawBarW * 0.5f;
                labels.Add(new LabelElement(labelX, py, text, opacity));
            }
        }

        if (labels != null)
            DrawLabels(ctx, labels);
    }

    private void RenderHorizontal(MarkContext ctx)
    {
        // Horizontal bars: Y = categories (OrdinalScale), X = values (LinearScale)
        var yScale = ctx.Scales.TryGet(YChannel) as OrdinalScale
                  ?? ctx.Scales.TryGet(Channel.X) as OrdinalScale;
        var xScale = ctx.Scales.TryGet(Channel.X) as LinearScale
                  ?? ctx.Scales.TryGet(YChannel) as LinearScale;
        if (yScale == null || xScale == null) return;
        if (yScale.Domain.Count == 0) return;

        float slotH = ctx.Plot.Height / yScale.Domain.Count;
        float barH  = slotH * (1f - BarPadding);
        float anim  = ComputeAnimProgress(ctx);

        for (int i = 0; i < ctx.Data.Count; i++)
        {
            var row   = ctx.Data[i];
            if (IsSeriesHidden(ctx, row)) continue;
            var catRaw = ctx.Encodes.Resolve(YChannel, row)
                      ?? ctx.Encodes.Resolve(Channel.X, row);
            var valRaw = ctx.Encodes.Resolve(Channel.X, row)
                      ?? ctx.Encodes.Resolve(YChannel, row);
            if (catRaw == null || valRaw == null) continue;
            var color = ResolveColorWithOverride(ctx, row, i, GetDefaultColor(ctx));
            float opacity = ComputeEffectiveOpacity(ctx, row);

            bool isHovered  = i == ctx.HoveredRowIndex;
            bool isSelected = i == ctx.SelectedRowIndex;

            float drawBarH = barH;
            if (isHovered)
            {
                color = BrightenColor(color, GetHoverBrighten(ctx));
                drawBarH *= GetHoverScale(ctx);
            }

            float yNorm  = (float)yScale.Map(catRaw);
            float xNorm  = (float)xScale.Map(valRaw);
            float baseline = ctx.Plot.MapX(0);
            float targetX  = ctx.Plot.MapX(xNorm);
            // Normalize to always produce a valid positive-width rect
            float barLeft  = Math.Min(baseline, baseline + (targetX - baseline) * anim);
            float barWidth = Math.Abs(targetX - baseline) * anim;

            // Y position: ordinal maps top-down
            float py = ctx.Plot.MapY(yNorm) - drawBarH * 0.5f;

            using var path  = ctx.Canvas.CreatePath();
            using var paint = ctx.Canvas.CreatePaint();

            if (CornerRadius > 0)
                path.RoundRect(barLeft, py, barWidth, drawBarH, CornerRadius);
            else
                path.Rect(barLeft, py, barWidth, drawBarH);

            paint.SetColor(color).SetAntiAlias(true).SetOpacity(opacity);
            ctx.Canvas.Fill(path, paint);

            if (isSelected)
            {
                using var strokePaint = ctx.Canvas.CreatePaint();
                ApplySelectionPaint(ctx, strokePaint, opacity);
                ctx.Canvas.Stroke(path, strokePaint);
            }
        }
    }

    private record StackedLayout(
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

            if (!seriesData.ContainsKey(seriesKey))
            {
                seriesOrder.Add(seriesKey);
                seriesData[seriesKey] = new();
            }
            seriesData[seriesKey].Add((i, row));
        }

        Dictionary<string, float>? categoryTotals = null;
        if (Stack == StackMode.Normalize)
        {
            categoryTotals = new Dictionary<string, float>();
            foreach (var row in ctx.Data)
            {
                var xKey = ctx.Encodes.Resolve(Channel.X, row)?.ToString() ?? "";
                var yResolved = ctx.Encodes.Resolve(YChannel, row);
                if (yResolved == null) continue;
                float yVal = ToSingle(yResolved, "Y");
                categoryTotals[xKey] = categoryTotals.GetValueOrDefault(xKey) + yVal;
            }
        }

        return new StackedLayout(seriesOrder, seriesData, categoryTotals);
    }

    private StackedLayout GetOrComputeStackedLayout(MarkContext ctx)
    {
        if (_cachedStackedLayout == null || _cachedStackedVersion != ctx.LayoutVersion)
        {
            _cachedStackedLayout = ComputeStackedLayout(ctx);
            _cachedStackedVersion = ctx.LayoutVersion;
        }
        return _cachedStackedLayout;
    }

    private double NormalizeY(double yVal, string xKey, StackedLayout layout)
    {
        if (Stack == StackMode.Normalize && layout.CategoryTotals != null)
        {
            float total = layout.CategoryTotals.GetValueOrDefault(xKey, 1f);
            if (total > 0) yVal /= total;
        }
        return yVal;
    }

    private void RenderStacked(MarkContext ctx)
    {
        var xScale = ctx.Scales.TryGet(Channel.X) as OrdinalScale;
        var yScale = ctx.Scales.TryGet(YChannel) as LinearScale;
        if (xScale == null || yScale == null) return;
        if (xScale.Domain.Count == 0) return;

        float slotW = ctx.Plot.Width / xScale.Domain.Count;
        float barW  = slotW * (1f - BarPadding);
        float anim  = ComputeAnimProgress(ctx);

        var layout = GetOrComputeStackedLayout(ctx);

        // Accumulated baselines per X in data space
        var baselines = new Dictionary<string, double>();

        foreach (var sk in layout.SeriesOrder)
        {
            // Skip hidden series
            string? skStr = sk.ToString();
            if (ctx.HiddenSeries != null && ctx.HiddenSeries.Contains(skStr ?? ""))
                continue;

            var color = ResolveSeriesColor(ctx, sk);

            foreach (var (dataIdx, row) in layout.SeriesData[sk])
            {
                var xRaw = ctx.Encodes.Resolve(Channel.X, row);
                if (xRaw == null) continue;
                string xKey = xRaw.ToString() ?? "";
                var yResolve = ctx.Encodes.Resolve(YChannel, row);
                if (yResolve == null) continue;
                double yVal = NormalizeY(ToDouble(yResolve, "Y"), xKey, layout);
                float opacity = ComputeEffectiveOpacity(ctx, row);

                double baseline = baselines.GetValueOrDefault(xKey, 0);
                double top = baseline + yVal;

                float xNorm = (float)xScale.Map(xRaw);
                float screenBaseline = ctx.Plot.MapY(yScale.Map(baseline));
                float screenTop      = ctx.Plot.MapY(yScale.Map(top));
                float animTop = screenBaseline + (screenTop - screenBaseline) * anim;
                float barH   = screenBaseline - animTop;
                float px     = ctx.Plot.MapX(xNorm) - barW * 0.5f;

                bool isHovered  = dataIdx == ctx.HoveredRowIndex;
                bool isSelected = dataIdx == ctx.SelectedRowIndex;

                Color drawColor = color;
                float drawBarW  = barW;
                if (isHovered)
                {
                    drawColor = BrightenColor(drawColor, GetHoverBrighten(ctx));
                    drawBarW *= GetHoverScale(ctx);
                    px = ctx.Plot.MapX(xNorm) - drawBarW * 0.5f;
                }

                using var path  = ctx.Canvas.CreatePath();
                using var paint = ctx.Canvas.CreatePaint();
                if (CornerRadius > 0)
                    path.RoundRect(px, animTop, drawBarW, barH, CornerRadius);
                else
                    path.Rect(px, animTop, drawBarW, barH);

                paint.SetColor(drawColor).SetAntiAlias(true).SetOpacity(opacity);
                ctx.Canvas.Fill(path, paint);

                if (isSelected)
                {
                    using var strokePaint = ctx.Canvas.CreatePaint();
                    ApplySelectionPaint(ctx, strokePaint, opacity);
                    ctx.Canvas.Stroke(path, strokePaint);
                }

                baselines[xKey] = top;
            }
        }
    }

    public override HitResult? HitTest(MarkContext ctx, Vector2 pos)
    {
        if (Stack != StackMode.None)
            return HitTestStacked(ctx, pos);

        if (Orientation == BarOrientation.Horizontal)
            return HitTestHorizontal(ctx, pos);

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

            float xNorm = (float)xScale.Map(xRaw);
            float yNorm = (float)yScale.Map(yRaw);

            float baseline = ctx.Plot.MapY(0);
            float targetY  = ctx.Plot.MapY(yNorm);
            // Apply animation factor consistent with Render
            float px = ctx.Plot.MapX(xNorm) - barW * 0.5f;
            float py = baseline + (targetY - baseline) * anim;
            float ph = baseline - py;

            if (pos.X >= px && pos.X <= px + barW && pos.Y >= py && pos.Y <= py + ph)
            {
                return new HitResult
                {
                    Hit = true, Row = row, RowIndex = i,
                    ScreenX = px + barW / 2f, ScreenY = py,
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
        var yScale = ctx.Scales.TryGet(YChannel) as OrdinalScale
                  ?? ctx.Scales.TryGet(Channel.X) as OrdinalScale;
        var xScale = ctx.Scales.TryGet(Channel.X) as LinearScale
                  ?? ctx.Scales.TryGet(YChannel) as LinearScale;
        if (yScale == null || xScale == null) return null;

        float slotH = ctx.Plot.Height / yScale.Domain.Count;
        float barH  = slotH * (1f - BarPadding);

        // Iterate in reverse so the topmost (last-rendered) bar is hit first
        for (int i = ctx.Data.Count - 1; i >= 0; i--)
        {
            var row    = ctx.Data[i];
            if (IsSeriesHidden(ctx, row)) continue;
            var catRaw = ctx.Encodes.Resolve(YChannel, row)
                      ?? ctx.Encodes.Resolve(Channel.X, row);
            var valRaw = ctx.Encodes.Resolve(Channel.X, row)
                      ?? ctx.Encodes.Resolve(YChannel, row);
            if (catRaw == null || valRaw == null) continue;

            float yNorm  = (float)yScale.Map(catRaw);
            float xNorm  = (float)xScale.Map(valRaw);
            float baseline = ctx.Plot.MapX(0);
            float targetX  = ctx.Plot.MapX(xNorm);
            float barLeft  = Math.Min(baseline, targetX);
            float barRight = Math.Max(baseline, targetX);
            float py = ctx.Plot.MapY(yNorm) - barH * 0.5f;

            if (pos.X >= barLeft && pos.X <= barRight &&
                pos.Y >= py && pos.Y <= py + barH)
            {
                return new HitResult
                {
                    Hit = true, Row = row, RowIndex = i,
                    ScreenX = targetX, ScreenY = py + barH / 2f,
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
        var xScale = ctx.Scales.TryGet(Channel.X) as OrdinalScale;
        var yScale = ctx.Scales.TryGet(YChannel) as LinearScale;
        if (xScale == null || yScale == null) return null;
        if (xScale.Domain.Count == 0) return null;

        float slotW = ctx.Plot.Width / xScale.Domain.Count;
        float barW  = slotW * (1f - BarPadding);
        float anim  = ComputeAnimProgress(ctx);

        var layout = GetOrComputeStackedLayout(ctx);
        var baselines = new Dictionary<string, double>();
        var candidates = new List<(int idx, DataRow row, float px, float top, float h)>();

        foreach (var sk in layout.SeriesOrder)
        {
            // Skip hidden series
            string? skStr = sk.ToString();
            if (ctx.HiddenSeries != null && ctx.HiddenSeries.Contains(skStr ?? ""))
                continue;

            foreach (var (dataIdx, row) in layout.SeriesData[sk])
            {
                var xRaw = ctx.Encodes.Resolve(Channel.X, row);
                if (xRaw == null) continue;
                string xKey = xRaw.ToString() ?? "";
                var yResolve = ctx.Encodes.Resolve(YChannel, row);
                if (yResolve == null) continue;
                double yVal = NormalizeY(ToDouble(yResolve, "Y"), xKey, layout);

                double baseline = baselines.GetValueOrDefault(xKey, 0);
                double top = baseline + yVal;

                float xNorm = (float)xScale.Map(xRaw);
                float screenBase = ctx.Plot.MapY(yScale.Map(baseline));
                float screenTop  = ctx.Plot.MapY(yScale.Map(top));
                // Apply animation factor consistent with RenderStacked
                float animTop = screenBase + (screenTop - screenBase) * anim;
                float px = ctx.Plot.MapX(xNorm) - barW * 0.5f;

                candidates.Add((dataIdx, row, px, animTop, screenBase - animTop));
                baselines[xKey] = top;
            }
        }

        // Check in reverse order so topmost bar is hit first
        for (int i = candidates.Count - 1; i >= 0; i--)
        {
            var (idx, row, px, top, h) = candidates[i];
            if (pos.X >= px && pos.X <= px + barW && pos.Y >= top && pos.Y <= top + h)
            {
                return new HitResult
                {
                    Hit = true, Row = row, RowIndex = idx,
                    ScreenX = px + barW / 2f, ScreenY = top,
                    Label = $"{ctx.Encodes.Resolve(Channel.X, row)}: {ctx.Encodes.Resolve(YChannel, row)}",
                    SeriesKey = ResolveSeriesKey(ctx, row),
                    MarkType = nameof(IntervalMark),
                };
            }
        }
        return null;
    }
}
