using System.Collections.Generic;
using Godot;
using GodotNodeExtension.Component.GodotChart.Canvas;

namespace GodotNodeExtension.Component.GodotChart.Marks;

/// <summary>
/// Treemap mark. Renders a squarified treemap where rectangle areas represent values.
/// Encodes: X = label, Y = value, Color = optional category.
/// Classified as a polar-only mark; no Cartesian grid is drawn.
/// Animation: cells grow from center outward.
/// </summary>
public class TreemapMark : Mark
{
    /// <inheritdoc />
    public override MarkCoordinate Coordinate => MarkCoordinate.Hierarchical;

    /// <summary>Gap between treemap cells in pixels.</summary>
    public float CellGap { get; set; } = 2f;

    /// <summary>Corner radius for each cell.</summary>
    public float CornerRadius { get; set; } = 3f;

    /// <summary>Whether to show value labels inside cells.</summary>
    public override bool ShowLabel { get; set; } = true;

    // Cached layout to avoid recomputation in HitTest
    private (List<(int idx, DataRow row, object? label, double value)> items,
             List<(float x, float y, float w, float h)> rects)? _cachedLayout;
    private int _cachedDataVersion = -1;

    /// <summary>Invalidate cached layout. Call when data or encodes change externally.</summary>
    public void InvalidateCache() { _cachedLayout = null; _cachedDataVersion = -1; }

    public override void Render(MarkContext ctx)
    {
        if (ctx.Data.Count == 0) return;
        float anim = ComputeAnimProgress(ctx);

        _cachedLayout = PrepareLayout(ctx);
        _cachedDataVersion = ctx.LayoutVersion;
        if (_cachedLayout == null) return;
        var (items, rects) = _cachedLayout.Value;

        for (int i = 0; i < items.Count && i < rects.Count; i++)
        {
            var (idx, row, label, _) = items[i];
            var (rx, ry, rw, rh)     = rects[i];

            // Animate from center
            float cx = rx + rw / 2f;
            float cy = ry + rh / 2f;
            float aw = rw * anim;
            float ah = rh * anim;

            var color = ResolveColorWithOverride(ctx, row, i, GetDefaultColor(ctx));
            float opacity = ComputeEffectiveOpacity(ctx, row);

            bool isHovered = idx == ctx.HoveredRowIndex;
            if (isHovered) color = BrightenColor(color, GetHoverBrighten(ctx));

            using var path  = ctx.Canvas.CreatePath();
            using var paint = ctx.Canvas.CreatePaint();
            if (CornerRadius > 0)
                path.RoundRect(cx - aw / 2f, cy - ah / 2f, aw, ah, CornerRadius);
            else
                path.Rect(cx - aw / 2f, cy - ah / 2f, aw, ah);
            paint.SetColor(color).SetAntiAlias(true).SetOpacity(opacity);
            ctx.Canvas.Fill(path, paint);

            if (idx == ctx.SelectedRowIndex)
            {
                using var selPaint = ctx.Canvas.CreatePaint();
                ApplySelectionPaint(ctx, selPaint, opacity);
                ctx.Canvas.Stroke(path, selPaint);
            }

            if (ShowLabel && aw > 20 && ah > 14)
            {
                using var labelPaint = ctx.Canvas.CreatePaint();
                labelPaint.SetColor(GetDataLabelColor(ctx)).SetOpacity(opacity);
                string text = label?.ToString() ?? "";
                ctx.Canvas.DrawText(text, cx, cy, FontSettings.Default, labelPaint);
            }
        }
    }

    public override HitResult? HitTest(MarkContext ctx, Vector2 pos)
    {
        // Invalidate cache if layout version changed (data, encodes, scales, or plot changed)
        if (_cachedDataVersion != ctx.LayoutVersion)
            _cachedLayout = null;
        var layout = _cachedLayout ?? PrepareLayout(ctx);
        if (layout == null) return null;
        var (items, rects) = layout.Value;

        for (int i = 0; i < items.Count && i < rects.Count; i++)
        {
            var (idx, row, label, _) = items[i];
            var (rx, ry, rw, rh)     = rects[i];
            if (pos.X >= rx && pos.X <= rx + rw && pos.Y >= ry && pos.Y <= ry + rh)
            {
                var yRaw = ctx.Encodes.Resolve(YChannel, row);
                return new HitResult
                {
                    Hit = true, Row = row, RowIndex = idx,
                    ScreenX = rx + rw / 2f, ScreenY = ry + rh / 2f,
                    Label = $"{label}: {yRaw}",
                    MarkType = nameof(TreemapMark),
                };
            }
        }
        return null;
    }

    private (List<(int idx, DataRow row, object? label, double value)> items,
             List<(float x, float y, float w, float h)> rects)?
        PrepareLayout(MarkContext ctx)
    {
        if (ctx.Data.Count == 0) return null;
        var items = new List<(int idx, DataRow row, object? label, double value)>();
        double total = 0;
        for (int i = 0; i < ctx.Data.Count; i++)
        {
            var row  = ctx.Data[i];
            if (IsSeriesHidden(ctx, row)) continue;
            var xRaw = ctx.Encodes.Resolve(Channel.X, row);
            var yRaw = ctx.Encodes.Resolve(YChannel, row);
            if (yRaw == null) continue;
            double val = ToDouble(yRaw, "Y");
            if (val <= 0) continue;
            items.Add((i, row, xRaw, val));
            total += val;
        }
        if (items.Count == 0 || total <= 0) return null;
        items.Sort((a, b) => b.value.CompareTo(a.value));
        var values = new List<double>(items.Count);
        foreach (var it in items) values.Add(it.value);
        var rects = SquarifyLayout(values, total,
            ctx.Plot.X, ctx.Plot.Y, ctx.Plot.Width, ctx.Plot.Height, CellGap);
        return (items, rects);
    }

    /// <summary>Simple squarified treemap layout algorithm.</summary>
    private static List<(float x, float y, float w, float h)> SquarifyLayout(
        List<double> values, double total, float x, float y, float w, float h, float gap)
    {
        var result = new List<(float x, float y, float w, float h)>();
        SquarifyRecurse(values, 0, values.Count, total, x, y, w, h, gap, result);
        return result;
    }

    private static void SquarifyRecurse(
        List<double> values, int start, int end, double total,
        float x, float y, float w, float h, float gap,
        List<(float x, float y, float w, float h)> result)
    {
        int count = end - start;
        if (count <= 0) return;
        if (count == 1)
        {
            result.Add((x + gap / 2f, y + gap / 2f, w - gap, h - gap));
            return;
        }

        // Split: find partition that gives best aspect ratio
        double halfTotal = 0;
        double sumSoFar  = 0;
        double halfTarget = total / 2.0;
        int splitIdx = start + 1;

        for (int i = start; i < end; i++)
        {
            sumSoFar += values[i];
            if (sumSoFar >= halfTarget)
            {
                splitIdx = i + 1;
                halfTotal = sumSoFar;
                break;
            }
        }
        if (splitIdx >= end) { splitIdx = end - 1; halfTotal = total - values[end - 1]; }

        double otherHalf = total - halfTotal;

        // Layout left partition and right partition
        if (w >= h)
        {
            // Split horizontally
            float leftW = (float)(halfTotal / total * w);
            SquarifyRecurse(values, start, splitIdx, halfTotal, x, y, leftW, h, gap, result);
            SquarifyRecurse(values, splitIdx, end, otherHalf, x + leftW, y, w - leftW, h, gap, result);
        }
        else
        {
            // Split vertically
            float topH = (float)(halfTotal / total * h);
            SquarifyRecurse(values, start, splitIdx, halfTotal, x, y, w, topH, gap, result);
            SquarifyRecurse(values, splitIdx, end, otherHalf, x, y + topH, w, h - topH, gap, result);
        }
    }
}
