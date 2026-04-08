using System;
using System.Collections.Generic;
using Godot;
using GodotNodeExtension.Component.GodotChart.Canvas;

namespace GodotNodeExtension.Component.GodotChart.Marks;

/// <summary>
/// Line chart mark. Renders data as connected line segments with optional area fill.
/// Supports smoothing, stepping, and stacking modes.
/// </summary>
public class LineMark : Mark
{
    /// <inheritdoc />
    public override MarkCoordinate Coordinate => MarkCoordinate.Cartesian;

    /// <summary>Line stroke width in pixels.</summary>
    public float StrokeWidth { get; set; } = 2f;

    /// <summary>Whether to use Catmull-Rom smoothing between points.</summary>
    public bool  Smooth      { get; set; } = true;

    /// <summary>Whether to fill the area under the line.</summary>
    public bool  ShowArea    { get; set; } = false;

    /// <summary>Opacity of the area fill [0, 1].</summary>
    public float AreaOpacity { get; set; } = 0.15f;

    // Cached row-to-index lookup to avoid per-frame allocation
    private Dictionary<DataRow, int>? _rowIndexMap;
    private int _rowIndexMapVersion = -1;

    // Reusable point buffer to avoid per-series allocation
    private readonly List<(float x, float y)> _allPoints = new();

    /// <summary>Stacking mode. Default is None.</summary>
    public StackMode Stack { get; set; } = StackMode.None;

    /// <summary>Step line mode. Default is None (normal line).</summary>
    public StepMode Step { get; set; } = StepMode.None;

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

        var groups = CachedGroupByChannel(ctx, Channel.Color);
        float anim = ComputeAnimProgress(ctx);
        float globalOpacity = ctx.Animation.GlobalOpacity;

        List<LabelElement>? labels = ShowLabel ? new List<LabelElement>() : null;

        // Build row-to-index lookup once, cached across frames
        Dictionary<DataRow, int>? rowIndexMap = null;
        if (ctx.HoveredRowIndex >= 0)
        {
            if (_rowIndexMap == null || _rowIndexMapVersion != ctx.DataVersion)
            {
                _rowIndexMap = new Dictionary<DataRow, int>(ctx.Data.Count);
                for (int i = 0; i < ctx.Data.Count; i++)
                    _rowIndexMap[ctx.Data[i]] = i;
                _rowIndexMapVersion = ctx.DataVersion;
            }
            rowIndexMap = _rowIndexMap;
        }

        foreach (var (key, rows) in groups)
        {
            // Skip hidden series
            string? seriesKey = key.ToString();
            if (ctx.HiddenSeries != null && ctx.HiddenSeries.Contains(seriesKey ?? ""))
                continue;

            var color = ResolveSeriesColor(ctx, key);

            // Series focus: reduce opacity for non-focused series
            float seriesOpacity = ComputeSeriesOpacity(ctx, seriesKey, globalOpacity);

            // Map data rows to screen points (avoid LINQ allocation)
            _allPoints.Clear();
            if (_allPoints.Capacity < rows.Count) _allPoints.Capacity = rows.Count;
            var allPoints = _allPoints;
            var xScl = ctx.Scales.Get(Channel.X);
            var yScl = ctx.Scales.Get(YChannel);
            foreach (var row in rows)
            {
                var xRaw  = ctx.Encodes.Resolve(Channel.X, row);
                var yRaw  = ctx.Encodes.Resolve(YChannel, row);
                if (xRaw == null || yRaw == null) continue;
                double xN = xScl.Map(xRaw);
                double yN = yScl.Map(yRaw);
                allPoints.Add((ctx.Plot.MapX(xN), ctx.Plot.MapY(yN)));
            }

            if (allPoints.Count < 2) continue;

            // Animation: clip-based reveal from left to right (pixel-smooth)
            bool clipping = anim < 1f && ctx.Canvas.Capabilities.SupportsClipping;
            IDisposable? clipScope = null;
            if (clipping)
            {
                float clipW = ctx.Plot.Width * anim;
                clipScope = ctx.Canvas.SaveScope();
                ctx.Canvas.ClipRect(ctx.Plot.X, ctx.Plot.Y, clipW, ctx.Plot.Height);
            }

            var points = allPoints;

            // Area fill
            if (ShowArea && ctx.Canvas.Capabilities.SupportsGradients)
            {
                using var areaPath  = ctx.Canvas.CreatePath();
                using var areaPaint = ctx.Canvas.CreatePaint();
                BuildLinePath(points, areaPath);
                areaPath.LineTo(points[^1].x, ctx.Plot.MapY(0));
                areaPath.LineTo(points[0].x,  ctx.Plot.MapY(0));
                areaPath.Close();
                areaPaint.SetLinearGradient(
                    ctx.Plot.X, ctx.Plot.Y,
                    ctx.Plot.X, ctx.Plot.Y + ctx.Plot.Height,
                    [
                        new GradientStop(0f, color with { A = AreaOpacity * seriesOpacity }),
                        new GradientStop(1f, color with { A = 0f })
                    ]);
                ctx.Canvas.Fill(areaPath, areaPaint);
            }

            // Line stroke
            using var linePath  = ctx.Canvas.CreatePath();
            using var linePaint = ctx.Canvas.CreatePaint();
            BuildLinePath(points, linePath);
            linePaint.SetColor(color)
                     .SetStrokeWidth(StrokeWidth)
                     .SetLineCap(LineCap.Round)
                     .SetLineJoin(LineJoin.Round)
                     .SetOpacity(seriesOpacity);
            ctx.Canvas.Stroke(linePath, linePaint);

            // Hover: highlight nearest data point with enlarged circle
            if (rowIndexMap != null)
            {
                for (int ri = 0; ri < rows.Count && ri < points.Count; ri++)
                {
                    if (rowIndexMap.TryGetValue(rows[ri], out int globalIdx) && globalIdx == ctx.HoveredRowIndex)
                    {
                        var pt = points[ri];
                        float hoverR = (ctx.Theme?.LineHoverPointRadius ?? 6f) * ctx.Animation.HoverScale;
                        using var hPath  = ctx.Canvas.CreatePath();
                        using var hPaint = ctx.Canvas.CreatePaint();
                        hPath.Circle(pt.x, pt.y, hoverR);
                        hPaint.SetColor(BrightenColor(color, GetHoverBrighten(ctx)))
                              .SetAntiAlias(true)
                              .SetOpacity(seriesOpacity);
                        ctx.Canvas.Fill(hPath, hPaint);

                        // Highlight ring
                        using var ringPaint = ctx.Canvas.CreatePaint();
                        var hrc = ctx.Theme?.LineHoverRingColor ?? new Color(1f, 1f, 1f, 0.6f);
                        ringPaint.SetColor(hrc with { A = hrc.A * seriesOpacity })
                                 .SetStrokeWidth(ctx.Theme?.LineHoverRingStrokeWidth ?? 2f);
                        ctx.Canvas.Stroke(hPath, ringPaint);
                        break;
                    }
                }
            }

            // Restore clip state if we were clipping for animation
            clipScope?.Dispose();

            // Collect label elements for this series
            if (labels != null)
            {
                for (int ri = 0; ri < rows.Count && ri < points.Count; ri++)
                {
                    var yRaw = ctx.Encodes.Resolve(YChannel, rows[ri]);
                    var xRaw = ctx.Encodes.Resolve(Channel.X, rows[ri]);
                    if (yRaw == null) continue;
                    string text = string.Format(LabelFormat, yRaw, xRaw);
                    labels.Add(new LabelElement(points[ri].x, points[ri].y, text, seriesOpacity));
                }
            }
        }

        if (labels != null)
            DrawLabels(ctx, labels);
    }

    private void RenderStacked(MarkContext ctx)
    {
        var groups = CachedGroupByChannel(ctx, Channel.Color);
        float anim  = ComputeAnimProgress(ctx);
        var xScale  = ctx.Scales.Get(Channel.X);
        var yScale  = ctx.Scales.Get(YChannel);

        // Collect all unique X values in data order
        var allXValues = new List<object>();
        var xSeen      = new HashSet<string>();
        foreach (var row in ctx.Data)
        {
            var xRaw = ctx.Encodes.Resolve(Channel.X, row);
            if (xRaw == null) continue;
            string xKey = xRaw.ToString() ?? "";
            if (xSeen.Add(xKey)) allXValues.Add(xRaw);
        }

        // Category totals for normalize mode
        Dictionary<string, float>? categoryTotals = null;
        if (Stack == StackMode.Normalize)
        {
            categoryTotals = new Dictionary<string, float>();
            foreach (var row in ctx.Data)
            {
                var xKey = ctx.Encodes.Resolve(Channel.X, row)?.ToString() ?? "";
                var yResolved = ctx.Encodes.Resolve(YChannel, row);
                if (yResolved == null) continue;
                float y  = ToSingle(yResolved, "Y");
                categoryTotals[xKey] = categoryTotals.GetValueOrDefault(xKey) + y;
            }
        }

        // Baselines per X category (in data space)
        var baselines = new Dictionary<string, double>();

        foreach (var (seriesKey, rows) in groups)
        {
            // Skip hidden series
            string? sk = seriesKey.ToString();
            if (ctx.HiddenSeries != null && ctx.HiddenSeries.Contains(sk ?? ""))
                continue;

            var color = ResolveSeriesColor(ctx, seriesKey);

            float seriesOpacity = ComputeSeriesOpacity(ctx, sk, ctx.Animation.GlobalOpacity);

            // Build X→Y map for this series
            var xToY = new Dictionary<string, double>();
            foreach (var row in rows)
            {
                var xKey = ctx.Encodes.Resolve(Channel.X, row)?.ToString() ?? "";
                var yResolved = ctx.Encodes.Resolve(YChannel, row);
                if (yResolved == null) continue;
                double yVal = ToDouble(yResolved, "Y");
                if (Stack == StackMode.Normalize)
                {
                    float total = categoryTotals!.GetValueOrDefault(xKey, 1f);
                    if (total > 0) yVal /= total;
                }
                xToY[xKey] = yVal;
            }

            // Build screen-space top & bottom point arrays
            var topPts    = new List<(float x, float y)>(allXValues.Count);
            var bottomPts = new List<(float x, float y)>(allXValues.Count);

            foreach (var xVal in allXValues)
            {
                string xKey  = xVal.ToString() ?? "";
                double baseline = baselines.GetValueOrDefault(xKey, 0);
                double yVal  = xToY.GetValueOrDefault(xKey, 0);
                double top   = baseline + yVal;

                float sx  = ctx.Plot.MapX(xScale.Map(xVal));
                float sBase = ctx.Plot.MapY(yScale.Map(baseline));
                float sTop  = ctx.Plot.MapY(yScale.Map(top));
                sTop = sBase + (sTop - sBase) * anim;

                topPts.Add((sx, sTop));
                bottomPts.Add((sx, sBase));

                baselines[xKey] = top;
            }

            if (topPts.Count < 2) continue;

            // Stacked area fill: top line → bottom line reversed → close
            using var areaPath  = ctx.Canvas.CreatePath();
            using var areaPaint = ctx.Canvas.CreatePaint();
            BuildLinePath(topPts, areaPath);
            // Bottom edge must use the same curve interpolation as the top
            // so it matches the previous layer's top edge exactly.
            areaPath.LineTo(bottomPts[^1].x, bottomPts[^1].y);
            for (int i = bottomPts.Count - 2; i >= 0; i--)
            {
                if (Smooth && Step == StepMode.None)
                {
                    float cpx = (bottomPts[i + 1].x + bottomPts[i].x) * 0.5f;
                    areaPath.CubicTo(cpx, bottomPts[i + 1].y,
                                     cpx, bottomPts[i].y,
                                     bottomPts[i].x, bottomPts[i].y);
                }
                else
                {
                    areaPath.LineTo(bottomPts[i].x, bottomPts[i].y);
                }
            }
            areaPath.Close();
            areaPaint.SetColor(color).SetOpacity(AreaOpacity * seriesOpacity).SetAntiAlias(true);
            ctx.Canvas.Fill(areaPath, areaPaint);

            // Top line stroke
            using var linePath  = ctx.Canvas.CreatePath();
            using var linePaint = ctx.Canvas.CreatePaint();
            BuildLinePath(topPts, linePath);
            linePaint.SetColor(color).SetStrokeWidth(StrokeWidth)
                     .SetLineCap(LineCap.Round).SetLineJoin(LineJoin.Round)
                     .SetOpacity(seriesOpacity);
            ctx.Canvas.Stroke(linePath, linePaint);
        }
    }

    public override HitResult? HitTest(MarkContext ctx, Vector2 pos)
    {
        var xScale = ctx.Scales.Get(Channel.X);
        var yScale = ctx.Scales.Get(YChannel);

        float threshold = ctx.Theme?.HitTestSnapDistance ?? 12f;
        float bestDist = threshold;
        DataRow? bestRow = null;
        int bestIdx = -1;
        float bestX = 0, bestY = 0;

        for (int i = 0; i < ctx.Data.Count; i++)
        {
            var row  = ctx.Data[i];
            if (IsSeriesHidden(ctx, row)) continue;
            var xRaw = ctx.Encodes.Resolve(Channel.X, row);
            var yRaw = ctx.Encodes.Resolve(YChannel, row);
            if (xRaw == null || yRaw == null) continue;
            float px = ctx.Plot.MapX(xScale.Map(xRaw));
            float py = ctx.Plot.MapY(yScale.Map(yRaw));
            float d  = pos.DistanceTo(new Vector2(px, py));
            if (d < bestDist)
            {
                bestDist = d; bestRow = row; bestIdx = i;
                bestX = px; bestY = py;
            }
        }

        if (bestRow == null) return null;
        var xVal = ctx.Encodes.Resolve(Channel.X, bestRow);
        var yVal = ctx.Encodes.Resolve(YChannel, bestRow);
        return new HitResult
        {
            Hit = true, Row = bestRow, RowIndex = bestIdx,
            ScreenX = bestX, ScreenY = bestY,
            Label = $"{xVal}: {yVal}",
            SeriesKey = ResolveSeriesKey(ctx, bestRow),
            MarkType = nameof(LineMark),
        };
    }

    private static void BuildPath(List<(float x, float y)> pts, IPath2D path, bool smooth)
    {
        path.MoveTo(pts[0].x, pts[0].y);
        for (int i = 1; i < pts.Count; i++)
        {
            if (smooth)
            {
                float cpx = (pts[i - 1].x + pts[i].x) * 0.5f;
                path.CubicTo(cpx, pts[i - 1].y, cpx, pts[i].y, pts[i].x, pts[i].y);
            }
            else
            {
                path.LineTo(pts[i].x, pts[i].y);
            }
        }
    }

    /// <summary>
    /// Build a line path considering both Smooth and Step settings.
    /// Step mode takes priority over Smooth when Step is not None.
    /// </summary>
    private void BuildLinePath(
        List<(float x, float y)> pts, IPath2D path)
    {
        if (Step != StepMode.None)
            BuildStepPath(pts, path, Step);
        else
            BuildPath(pts, path, Smooth);
    }

    /// <summary>
    /// Build a step path through the given points with the specified step mode.
    /// </summary>
    private static void BuildStepPath(
        List<(float x, float y)> pts, IPath2D path, StepMode mode)
    {
        path.MoveTo(pts[0].x, pts[0].y);
        for (int i = 1; i < pts.Count; i++)
        {
            var (px, py) = pts[i - 1];
            var (nx, ny) = pts[i];

            switch (mode)
            {
                case StepMode.After:
                    // Horizontal first at previous Y, then vertical to next point
                    path.LineTo(nx, py);
                    path.LineTo(nx, ny);
                    break;
                case StepMode.Before:
                    // Vertical first to next Y, then horizontal to next X
                    path.LineTo(px, ny);
                    path.LineTo(nx, ny);
                    break;
                case StepMode.Center:
                    // Step at midpoint X
                    float midX = (px + nx) * 0.5f;
                    path.LineTo(midX, py);
                    path.LineTo(midX, ny);
                    path.LineTo(nx, ny);
                    break;
                default:
                    path.LineTo(nx, ny);
                    break;
            }
        }
    }
}
