﻿using System;
using System.Collections.Generic;
using Godot;
using GodotNodeExtension.Component.GodotChart.Canvas;

namespace GodotNodeExtension.Component.GodotChart.Marks;

/// <summary>
/// Radar (spider) chart mark. Encodes: X = dimension name (mapped to axis angle),
/// Y = value (mapped to radius), Color = series (for multi-series overlay).
/// Draws its own polar grid within the plot area.
/// </summary>
public class RadarMark : Mark
{
    /// <inheritdoc />
    public override MarkCoordinate Coordinate => MarkCoordinate.Polar;

    /// <summary>Fill opacity for the radar polygon area.</summary>
    public float FillOpacity { get; set; } = 0.15f;

    /// <summary>Stroke width for the polygon outline.</summary>
    public float StrokeWidth { get; set; } = 2f;

    /// <summary>Number of concentric grid rings to draw.</summary>
    public int GridRings { get; set; } = 5;

    /// <summary>
    /// Whether to show the dimension label at each axis. Independent of <see cref="ShowGrid"/>:
    /// the labels stay visible even when the grid is turned off.
    /// </summary>
    public bool ShowAxisLabels { get; set; } = true;

    /// <summary>Whether to show the polar grid (rings, axis lines and radius ticks).</summary>
    public bool ShowGrid { get; set; } = true;

    /// <summary>Radius of data point dots at each vertex.</summary>
    public float PointRadius { get; set; } = 3f;

    /// <summary>
    /// This mark keeps its interaction state in the data layer, so a chart carrying it renders single-pass
    /// (see <see cref="Chart.UseLayerCache"/>).
    /// <para>
    /// Its hover look is one vertex dot per series, and a vertex position comes from the per-series
    /// dimension walk <see cref="Render"/> rebuilds every frame (the mark's reusable dimension buffers hold
    /// the last series only). The overlay would therefore have to re-walk every series' rows to place a
    /// single dot, which is the whole data layer's cost again - for a mark that draws a handful of points.
    /// </para>
    /// </summary>
    public override bool InteractionStateInOverlay => false;

    /// <summary>
    /// A radar is drawn from the plot's <b>short</b> edge (<see cref="RadiusFactor"/> of
    /// <c>min(width, height) / 2</c>) plus the dimension names just outside it, so it asks for a square
    /// content area: a wide canvas otherwise leaves the web floating in the middle of an empty band.
    /// </summary>
    public override float? PreferredAspectRatio => 1f;

    /// <summary>Outer radius as ratio of min(width, height)/2. Range (0, 1].</summary>
    public float RadiusFactor { get; set; } = 0.85f;

    // Cached dimension list to avoid per-frame recomputation. Keyed with the shared layout cache key
    // (owner + layout version + data list + hidden series), so one mark instance shared by two charts -
    // or a theme edit that changes the layout version - cannot keep a stale dimension list alive.
    private List<string>? _cachedDims;
    private LayoutCacheKey? _cachedDimsKey;

    // Reusable collections to reduce per-frame GC pressure
    private readonly Dictionary<DataRow, int> _rowIndexMap = [];
    private readonly Dictionary<string, double> _dimValues = [];
    private readonly Dictionary<string, int> _dimRowIdx = [];
    private readonly List<(float x, float y)> _vertices = [];

    /// <summary>
    /// Radius the dimension names sit at, as a factor of the web's own radius: the web is drawn inside it and
    /// <c>DrawAxisLabels</c> places the names on it.
    /// </summary>
    private const float RadarLabelRing = 1.12f;

    /// <inheritdoc />
    public override void Render(MarkContext ctx)
    {
        var dims = GetCachedDimensions(ctx);
        if (dims.Count < 3) return;

        var (cx, cy, maxR) = PolarGeometry.CenterAndRadius(ctx.Plot, RadiusFactor);

        float anim = ComputeAnimProgress(ctx);

        var yScale = ctx.Scales.TryGet(YChannel) as LinearScale;
        if (yScale == null) return;

        // The grid and the axis labels are independent: ShowGrid controls only the grid/axis
        // lines and ticks, while ShowAxisLabels controls only the dimension labels.
        if (ShowGrid)
            DrawPolarGrid(ctx, cx, cy, maxR, dims, yScale);

        if (ShowAxisLabels)
            DrawAxisLabels(ctx, cx, cy, maxR, dims);

        // Pre-build row→index lookup to avoid O(n²) IndexOf calls
        _rowIndexMap.Clear();
        for (int i = 0; i < ctx.Data.Count; i++)
            _rowIndexMap[ctx.Data[i]] = i;

        var groups = CachedGroupByChannel(ctx, Channel.Color);

        foreach (var (seriesKey, rows) in groups)
        {
            // Skip hidden series
            string? sk = seriesKey.ToString();
            if (ctx.HiddenSeries != null && ctx.HiddenSeries.Contains(sk ?? ""))
                continue;

            var color = ResolveSeriesColor(ctx, seriesKey);

            float seriesOpacity = ComputeSeriesOpacity(ctx, sk, ctx.Animation.GlobalOpacity);

            _dimValues.Clear();
            _dimRowIdx.Clear();
            foreach (var row in rows)
            {
                var dimRaw = ctx.Encodes.Resolve(Channel.X, row);
                var yResolved = ctx.Encodes.Resolve(YChannel, row);
                // A row without a dimension name (or without a value) has no vertex: it used to be
                // filed under the dimension "" and then looked up as if it were a real dimension.
                if (dimRaw is null || yResolved == null) continue;
                double value = ToDouble(yResolved, "Y");
                // A non-finite value is treated like a missing dimension (vertex at the centre)
                // rather than pushing NaN into the polygon.
                if (!double.IsFinite(value)) continue;
                string dimName = dimRaw.ToString() ?? "";
                _dimValues[dimName] = value;
                _dimRowIdx[dimName] = _rowIndexMap.GetValueOrDefault(row, -1);
            }

            _vertices.Clear();
            for (int d = 0; d < dims.Count; d++)
            {
                float angle = ShapeGeometry.AngleAt(d, dims.Count);
                double val  = _dimValues.GetValueOrDefault(dims[d], 0);
                // The mapped value is clamped to [0, 1] before the animation is applied: an explicit
                // Scale / ScaleDomain can map a value past the outer ring (or mirror a negative one to
                // the opposite axis), which drew vertices away from the grid - the same rule GaugeMark
                // uses for its sweep.
                float norm  = Math.Clamp((float)yScale.Map(val), 0f, 1f) * anim;
                float r     = norm * maxR;
                _vertices.Add((cx + MathF.Cos(angle) * r, cy + MathF.Sin(angle) * r));
            }

            var fillPath = ShapePath(ctx);
            var fillPaint = ShapePaint(ctx);
            fillPath.MoveTo(_vertices[0].x, _vertices[0].y);
            for (int v = 1; v < _vertices.Count; v++)
                fillPath.LineTo(_vertices[v].x, _vertices[v].y);
            fillPath.Close();
            fillPaint.SetColor(color).SetOpacity(FillOpacity * seriesOpacity).SetAntiAlias(true);
            ctx.Canvas.Fill(fillPath, fillPaint);

            var strokePaint = ShapePaint(ctx);
            strokePaint.SetColor(color).SetStrokeWidth(StrokeWidth)
                       .SetOpacity(seriesOpacity).SetAntiAlias(true)
                       .SetLineJoin(LineJoin.Round);
            ctx.Canvas.Stroke(fillPath, strokePaint);

            for (int d = 0; d < _vertices.Count; d++)
            {
                var (vx, vy) = _vertices[d];
                float dotR = PointRadius;
                int dataIdx = _dimRowIdx.GetValueOrDefault(dims[d], -1);

                bool isHovered = dataIdx == ctx.HoveredRowIndex;
                if (isHovered)
                    dotR *= HoverScaled(ctx, (ctx.Theme ?? ChartTheme.Default).RadarHoverDotScale * ctx.Animation.HoverScale);

                var dotPath = ShapePath(ctx);
                var dotPaint = ShapePaint(ctx);
                dotPath.Circle(vx, vy, dotR);
                // Vertex fill: series colour as the default and ResolveFill applies the style callback
                // plus the hover brighten that used to happen here (the radius above is kept).
                var dotFill = dataIdx >= 0
                    ? ResolveFill(ctx, ctx.Data[dataIdx], dataIdx, color) : color;
                dotPaint.SetColor(dotFill)
                        .SetAntiAlias(true).SetOpacity(seriesOpacity);
                ctx.Canvas.Fill(dotPath, dotPaint);

                if (dataIdx == ctx.SelectedRowIndex)
                {
                    var selPaint = ShapePaint(ctx);
                    ApplySelectionPaint(ctx, selPaint, seriesOpacity);
                    ctx.Canvas.Stroke(dotPath, selPaint);
                }
            }
        }
    }

    /// <inheritdoc />
    public override HitResult? HitTest(MarkContext ctx, Vector2 screenPos)
    {
        var dims = GetCachedDimensions(ctx);
        if (dims.Count < 3) return null;

        var (cx, cy, maxR) = PolarGeometry.CenterAndRadius(ctx.Plot, RadiusFactor);

        var yScale = ctx.Scales.TryGet(YChannel) as LinearScale;
        if (yScale == null) return null;

        float threshold = (ctx.Theme ?? ChartTheme.Default).HitTestSnapDistance;
        float bestDist = threshold;
        DataRow? bestRow = null;
        int bestIdx = -1;
        float bestX = 0, bestY = 0;

        // Pre-build dim name→index lookup
        var dimIndexMap = new Dictionary<string, int>(dims.Count);
        for (int d = 0; d < dims.Count; d++)
            dimIndexMap[dims[d]] = d;

        // Same geometry as Render: the vertices shrink toward the centre with the entry animation and
        // the mapped value is clamped the same way, so a hit can only land on a drawn vertex.
        float anim = ComputeAnimProgress(ctx);

        for (int i = 0; i < ctx.Data.Count; i++)
        {
            var row = ctx.Data[i];
            if (IsSeriesHidden(ctx, row)) continue;
            var dimRaw = ctx.Encodes.Resolve(Channel.X, row);
            if (dimRaw is null) continue;
            var dimName = dimRaw.ToString() ?? "";
            if (!dimIndexMap.TryGetValue(dimName, out int dimIdx)) continue;

            float angle = ShapeGeometry.AngleAt(dimIdx, dims.Count);
            var yResolved = ctx.Encodes.Resolve(YChannel, row);
            if (yResolved == null) continue;
            double val  = ToDouble(yResolved, "Y");
            if (!double.IsFinite(val)) continue;
            float norm  = Math.Clamp((float)yScale.Map(val), 0f, 1f) * anim;
            float r     = norm * maxR;
            float px    = cx + MathF.Cos(angle) * r;
            float py    = cy + MathF.Sin(angle) * r;

            float d = screenPos.DistanceTo(new Vector2(px, py));
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
            MarkType  = nameof(RadarMark),
        };
    }

    private void DrawPolarGrid(MarkContext ctx, float cx, float cy, float maxR,
                               List<string> dims, LinearScale yScale)
    {
        var gridPaint = ShapePaint(ctx);
        gridPaint.SetColor((ctx.Theme ?? ChartTheme.Default).RadarGridColor).SetStrokeWidth(1f);

        for (int ring = 1; ring <= GridRings; ring++)
        {
            float r = maxR * ring / GridRings;
            var ringPath = ShapePath(ctx);
            for (int d = 0; d < dims.Count; d++)
            {
                float angle = ShapeGeometry.AngleAt(d, dims.Count);
                float px = cx + MathF.Cos(angle) * r;
                float py = cy + MathF.Sin(angle) * r;
                if (d == 0) ringPath.MoveTo(px, py);
                else ringPath.LineTo(px, py);
            }
            ringPath.Close();
            ctx.Canvas.Stroke(ringPath, gridPaint);
        }

        var axisPaint = ShapePaint(ctx);
        axisPaint.SetColor((ctx.Theme ?? ChartTheme.Default).RadarAxisColor).SetStrokeWidth(1f);

        for (int d = 0; d < dims.Count; d++)
        {
            float angle = ShapeGeometry.AngleAt(d, dims.Count);
            float ex = cx + MathF.Cos(angle) * maxR;
            float ey = cy + MathF.Sin(angle) * maxR;
            ctx.Canvas.DrawLine(cx, cy, ex, ey, axisPaint);
        }

        var tickPaint = ShapePaint(ctx);
        tickPaint.SetColor((ctx.Theme ?? ChartTheme.Default).RadarTickColor);
        for (int ring = 1; ring <= GridRings; ring++)
        {
            float r = maxR * ring / GridRings;
            double val = yScale.Min + (yScale.Max - yScale.Min) * ring / GridRings;
            ctx.Canvas.DrawText(yScale.Format(val), cx + 4f, cy - r, ThemedFont(ctx, FontSettings.Default), tickPaint);
        }
    }

    /// <summary>
    /// Draws the dimension label at the outer end of each axis. Kept separate from
    /// <see cref="DrawPolarGrid"/> so it stays visible when <see cref="ShowGrid"/> is disabled.
    /// </summary>
    private void DrawAxisLabels(MarkContext ctx, float cx, float cy, float maxR, List<string> dims)
    {
        for (int d = 0; d < dims.Count; d++)
        {
            float angle = ShapeGeometry.AngleAt(d, dims.Count);
            float labelR = maxR * RadarLabelRing;
            float lx = cx + MathF.Cos(angle) * labelR;
            float ly = cy + MathF.Sin(angle) * labelR;

            var labelPaint = ShapePaint(ctx);
            labelPaint.SetColor((ctx.Theme ?? ChartTheme.Default).RadarLabelColor);
            DrawTextCentered(ctx, labelPaint, dims[d], lx, ly, FontSettings.Default);
        }
    }

    /// <summary>
    /// Distinct dimension names of the data, in first-appearance order. A row whose X value is missing
    /// has no dimension: it used to enter the list as a dimension named <c>""</c>, which counted
    /// towards the "at least three dimensions" check and got an axis label of its own.
    /// </summary>
    private static List<string> GetDimensions(MarkContext ctx)
    {
        var dims = new List<string>();
        var seen = new HashSet<string>();
        foreach (var row in ctx.Data)
        {
            var xRaw = ctx.Encodes.Resolve(Channel.X, row);
            if (xRaw is null) continue;
            var xKey = xRaw.ToString() ?? "";
            if (seen.Add(xKey)) dims.Add(xKey);
        }
        return dims;
    }

    private List<string> GetCachedDimensions(MarkContext ctx)
    {
        var key = CacheKey(ctx);
        if (_cachedDims == null || _cachedDimsKey != key)
        {
            _cachedDims = GetDimensions(ctx);
            _cachedDimsKey = key;
        }
        return _cachedDims;
    }
}
