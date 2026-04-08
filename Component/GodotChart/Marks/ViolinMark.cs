using System;
using System.Collections.Generic;
using Godot;

namespace GodotNodeExtension.Component.GodotChart.Marks;

/// <summary>
/// Violin mark. Renders symmetric density distributions for grouped data.
/// Data should have multiple rows per category; group by X (OrdinalScale), Y = numeric values.
/// Uses histogram binning for density estimation (simplified KDE).
/// Animation: violins grow from center outward.
/// </summary>
public class ViolinMark : Mark
{
    /// <inheritdoc />
    public override MarkCoordinate Coordinate => MarkCoordinate.Cartesian;

    /// <summary>Number of histogram bins for density estimation.</summary>
    public int BinCount { get; set; } = 20;

    /// <summary>Width ratio of the violin relative to the slot [0, 1].</summary>
    public float WidthRatio { get; set; } = 0.7f;

    /// <summary>Fill opacity for the violin body.</summary>
    public float FillOpacity { get; set; } = 0.5f;

    /// <summary>Whether to show the median line.</summary>
    public bool ShowMedian { get; set; } = true;

    /// <summary>Whether to show Q1/Q3 box indicator.</summary>
    public bool ShowBox { get; set; } = true;

    /// <summary>Stroke width of the outline.</summary>
    public float StrokeWidth { get; set; } = 1.5f;

    // Reusable collections to reduce per-frame GC pressure
    private readonly Dictionary<string, List<double>> _groups = new();
    private readonly Dictionary<string, List<int>> _groupRowIndices = new();
    private int _groupsCacheVersion = -1;

    /// <summary>Ensure the group cache is up-to-date with the current data version.</summary>
    private void EnsureGroupCache(MarkContext ctx)
    {
        if (_groupsCacheVersion == ctx.DataVersion) return;

        _groups.Clear();
        _groupRowIndices.Clear();
        for (int i = 0; i < ctx.Data.Count; i++)
        {
            var row = ctx.Data[i];
            if (IsSeriesHidden(ctx, row)) continue;
            var xRaw = ctx.Encodes.Resolve(Channel.X, row);
            var yRaw = ctx.Encodes.Resolve(YChannel, row);
            if (xRaw == null || yRaw == null) continue;
            string key = xRaw.ToString() ?? "";
            if (!_groups.ContainsKey(key))
            {
                _groups[key] = new List<double>();
                _groupRowIndices[key] = new List<int>();
            }
            _groups[key].Add(ToDouble(yRaw, "Y"));
            _groupRowIndices[key].Add(i);
        }
        _groupsCacheVersion = ctx.DataVersion;
    }

    public override void Render(MarkContext ctx)
    {
        var xScale = ctx.Scales.TryGet(Channel.X) as OrdinalScale;
        var yScale = ctx.Scales.TryGet(YChannel) as LinearScale;
        if (xScale == null || yScale == null) return;
        if (xScale.Domain.Count == 0) return;

        float anim = ComputeAnimProgress(ctx);
        float slotW = ctx.Plot.Width / xScale.Domain.Count;
        float violinW = slotW * WidthRatio / 2f; // half-width for each side

        // Group data by X category (cached across frames when data unchanged)
        EnsureGroupCache(ctx);

        foreach (var cat in xScale.Domain)
        {
            if (!_groups.TryGetValue(cat, out var vals) || vals.Count < 2) continue;

            float cx = ctx.Plot.MapX((float)xScale.Map(cat));
            var firstRow = ctx.Data[_groupRowIndices[cat][0]];
            var color = ResolveColorWithOverride(ctx, firstRow,
                _groupRowIndices[cat][0], GetDefaultColor(ctx));
            float opacity = ComputeEffectiveOpacity(ctx, firstRow);

            vals.Sort();
            double yMin = vals[0];
            double yMax = vals[^1];
            if (yMax <= yMin) continue;

            // Build histogram bins
            int binCount = Math.Max(1, BinCount);
            var bins = new double[binCount];
            double binWidth = (yMax - yMin) / binCount;
            foreach (double v in vals)
            {
                int bin = Math.Clamp((int)((v - yMin) / binWidth), 0, binCount - 1);
                bins[bin]++;
            }
            double maxBin = 0;
            for (int b = 0; b < binCount; b++)
                if (bins[b] > maxBin) maxBin = bins[b];
            if (maxBin <= 0) continue;

            // Build symmetric violin path
            using var fillPath = ctx.Canvas.CreatePath();
            using var fillPaint = ctx.Canvas.CreatePaint();

            // Right side (top to bottom in Y)
            float firstY = ctx.Plot.MapY((float)yScale.Map(yMin));
            fillPath.MoveTo(cx, firstY);
            for (int b = 0; b < binCount; b++)
            {
                double binCenter = yMin + (b + 0.5) * binWidth;
                float py = ctx.Plot.MapY((float)yScale.Map(binCenter));
                float pw = (float)(bins[b] / maxBin) * violinW * anim;
                fillPath.LineTo(cx + pw, py);
            }
            float lastY = ctx.Plot.MapY((float)yScale.Map(yMax));
            fillPath.LineTo(cx, lastY);

            // Left side (bottom to top in Y)
            for (int b = binCount - 1; b >= 0; b--)
            {
                double binCenter = yMin + (b + 0.5) * binWidth;
                float py = ctx.Plot.MapY((float)yScale.Map(binCenter));
                float pw = (float)(bins[b] / maxBin) * violinW * anim;
                fillPath.LineTo(cx - pw, py);
            }
            fillPath.Close();

            fillPaint.SetColor(color).SetAntiAlias(true).SetOpacity(FillOpacity * opacity);
            ctx.Canvas.Fill(fillPath, fillPaint);

            // Outline
            using var strokePaint = ctx.Canvas.CreatePaint();
            strokePaint.SetColor(color).SetStrokeWidth(StrokeWidth).SetAntiAlias(true).SetOpacity(opacity);
            ctx.Canvas.Stroke(fillPath, strokePaint);

            // Median + IQR indicators
            if (ShowMedian || ShowBox)
            {
                double median = vals[vals.Count / 2];
                float medianY = ctx.Plot.MapY((float)yScale.Map(median));

                if (ShowBox && vals.Count >= 4)
                {
                    double q1 = vals[vals.Count / 4];
                    double q3 = vals[vals.Count * 3 / 4];
                    float q1Y = ctx.Plot.MapY((float)yScale.Map(q1));
                    float q3Y = ctx.Plot.MapY((float)yScale.Map(q3));
                    float boxHalfW = violinW * (ctx.Theme?.ViolinBoxWidthRatio ?? 0.15f) * anim;

                    using var boxPath = ctx.Canvas.CreatePath();
                    using var boxPaint = ctx.Canvas.CreatePaint();
                    float boxTop = Math.Min(q1Y, q3Y);
                    float boxH = Math.Abs(q3Y - q1Y);
                    boxPath.Rect(cx - boxHalfW, boxTop, boxHalfW * 2, boxH);
                    var vbc = ctx.Theme?.ViolinBoxColor ?? new Color(0.2f, 0.2f, 0.25f, 0.8f);
                    boxPaint.SetColor(vbc with { A = vbc.A * opacity }).SetAntiAlias(true);
                    ctx.Canvas.Fill(boxPath, boxPaint);
                }

                if (ShowMedian)
                {
                    using var dotPath = ctx.Canvas.CreatePath();
                    using var dotPaint = ctx.Canvas.CreatePaint();
                    var vmdr = ctx.Theme?.ViolinMedianDotRadius ?? 3f;
                    dotPath.Circle(cx, medianY, vmdr * anim);
                    var vmdc = ctx.Theme?.ViolinMedianDotColor ?? new Color(1f, 1f, 1f);
                    dotPaint.SetColor(vmdc with { A = vmdc.A * opacity }).SetAntiAlias(true);
                    ctx.Canvas.Fill(dotPath, dotPaint);
                }
            }
        }
    }

    public override HitResult? HitTest(MarkContext ctx, Vector2 pos)
    {
        var xScale = ctx.Scales.TryGet(Channel.X) as OrdinalScale;
        var yScale = ctx.Scales.TryGet(YChannel) as LinearScale;
        if (xScale == null || yScale == null) return null;
        if (xScale.Domain.Count == 0) return null;

        float slotW = ctx.Plot.Width / xScale.Domain.Count;

        // Reuse cached groups from Render() when data version matches
        EnsureGroupCache(ctx);

        foreach (var cat in xScale.Domain)
        {
            float cx = ctx.Plot.MapX((float)xScale.Map(cat));
            if (Math.Abs(pos.X - cx) > slotW / 2f) continue;

            if (!_groups.TryGetValue(cat, out var vals) || vals.Count < 2) continue;
            var indices = _groupRowIndices[cat];

            // Find closest data row by mapped Y position
            int bestIdx = -1;
            float bestDist = float.MaxValue;
            for (int j = 0; j < vals.Count; j++)
            {
                float rowY = ctx.Plot.MapY((float)yScale.Map(vals[j]));
                float d = Math.Abs(pos.Y - rowY);
                if (d < bestDist)
                {
                    bestDist = d;
                    bestIdx = indices[j];
                }
            }
            if (bestIdx >= 0)
            {
                return new HitResult
                {
                    Hit = true, Row = ctx.Data[bestIdx], RowIndex = bestIdx,
                    ScreenX = cx, ScreenY = pos.Y,
                    Label = cat,
                    MarkType = nameof(ViolinMark),
                };
            }
        }
        return null;
    }
}
