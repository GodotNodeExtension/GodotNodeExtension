using System;
using System.Collections.Generic;
using Godot;
using GodotNodeExtension.Component.GodotChart.Canvas;

namespace GodotNodeExtension.Component.GodotChart.Marks;

/// <summary>
/// Radar (spider) chart mark. Encodes: X = dimension name (mapped to axis angle),
/// Y = value (mapped to radius), Color = series (for multi-series overlay).
/// Draws its own polar grid within the plot area.
/// </summary>
/// 
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

    /// <summary>Whether to show dimension labels at each axis.</summary>
    public bool ShowAxisLabels { get; set; } = true;

    /// <summary>Whether to show the polar grid.</summary>
    public bool ShowGrid { get; set; } = true;

    /// <summary>Radius of data point dots at each vertex.</summary>
    public float PointRadius { get; set; } = 3f;

    /// <summary>Outer radius as ratio of min(width, height)/2. Range (0, 1].</summary>
    public float RadiusFactor { get; set; } = 0.85f;

    // Cached dimension list to avoid per-frame recomputation
    private List<string>? _cachedDims;
    private int _cachedDimsVersion = -1;

    // Reusable collections to reduce per-frame GC pressure
    private readonly Dictionary<DataRow, int> _rowIndexMap = new();
    private readonly Dictionary<string, double> _dimValues = new();
    private readonly Dictionary<string, int> _dimRowIdx = new();
    private readonly List<(float x, float y)> _vertices = new();

    public override void Render(MarkContext ctx)
    {
        var dims = GetCachedDimensions(ctx);
        if (dims.Count < 3) return;

        float cx = ctx.Plot.X + ctx.Plot.Width / 2f;
        float cy = ctx.Plot.Y + ctx.Plot.Height / 2f;
        float maxR = MathF.Min(ctx.Plot.Width, ctx.Plot.Height) / 2f * RadiusFactor;
        float anim = ComputeAnimProgress(ctx);

        var yScale = ctx.Scales.TryGet(YChannel) as LinearScale;
        if (yScale == null) return;

        if (ShowGrid)
            DrawPolarGrid(ctx, cx, cy, maxR, dims, yScale);

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
                var dimName = ctx.Encodes.Resolve(Channel.X, row)?.ToString() ?? "";
                var yResolved = ctx.Encodes.Resolve(YChannel, row);
                if (yResolved == null) continue;
                _dimValues[dimName] = ToDouble(yResolved, "Y");
                _dimRowIdx[dimName] = _rowIndexMap.GetValueOrDefault(row, -1);
            }

            _vertices.Clear();
            for (int d = 0; d < dims.Count; d++)
            {
                float angle = -MathF.PI / 2f + MathF.Tau * d / dims.Count;
                double val  = _dimValues.GetValueOrDefault(dims[d], 0);
                float norm  = (float)yScale.Map(val) * anim;
                float r     = norm * maxR;
                _vertices.Add((cx + MathF.Cos(angle) * r, cy + MathF.Sin(angle) * r));
            }

            using var fillPath  = ctx.Canvas.CreatePath();
            using var fillPaint = ctx.Canvas.CreatePaint();
            fillPath.MoveTo(_vertices[0].x, _vertices[0].y);
            for (int v = 1; v < _vertices.Count; v++)
                fillPath.LineTo(_vertices[v].x, _vertices[v].y);
            fillPath.Close();
            fillPaint.SetColor(color).SetOpacity(FillOpacity * seriesOpacity).SetAntiAlias(true);
            ctx.Canvas.Fill(fillPath, fillPaint);

            using var strokePaint = ctx.Canvas.CreatePaint();
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
                    dotR *= (ctx.Theme?.RadarHoverDotScale ?? 1.5f) * ctx.Animation.HoverScale;

                using var dotPath  = ctx.Canvas.CreatePath();
                using var dotPaint = ctx.Canvas.CreatePaint();
                dotPath.Circle(vx, vy, dotR);
                dotPaint.SetColor(isHovered ? BrightenColor(color, GetHoverBrighten(ctx)) : color)
                        .SetAntiAlias(true).SetOpacity(seriesOpacity);
                ctx.Canvas.Fill(dotPath, dotPaint);

                if (dataIdx == ctx.SelectedRowIndex)
                {
                    using var selPaint = ctx.Canvas.CreatePaint();
                    ApplySelectionPaint(ctx, selPaint, seriesOpacity);
                    ctx.Canvas.Stroke(dotPath, selPaint);
                }
            }
        }
    }

    public override HitResult? HitTest(MarkContext ctx, Vector2 pos)
    {
        var dims = GetCachedDimensions(ctx);
        if (dims.Count < 3) return null;

        float cx = ctx.Plot.X + ctx.Plot.Width / 2f;
        float cy = ctx.Plot.Y + ctx.Plot.Height / 2f;
        float maxR = MathF.Min(ctx.Plot.Width, ctx.Plot.Height) / 2f * RadiusFactor;

        var yScale = ctx.Scales.TryGet(YChannel) as LinearScale;
        if (yScale == null) return null;

        float threshold = ctx.Theme?.HitTestSnapDistance ?? 12f;
        float bestDist = threshold;
        DataRow? bestRow = null;
        int bestIdx = -1;
        float bestX = 0, bestY = 0;

        // Pre-build dim name→index lookup
        var dimIndexMap = new Dictionary<string, int>(dims.Count);
        for (int d = 0; d < dims.Count; d++)
            dimIndexMap[dims[d]] = d;

        for (int i = 0; i < ctx.Data.Count; i++)
        {
            var row = ctx.Data[i];
            if (IsSeriesHidden(ctx, row)) continue;
            var dimName = ctx.Encodes.Resolve(Channel.X, row)?.ToString() ?? "";
            if (!dimIndexMap.TryGetValue(dimName, out int dimIdx)) continue;

            float angle = -MathF.PI / 2f + MathF.Tau * dimIdx / dims.Count;
            var yResolved = ctx.Encodes.Resolve(YChannel, row);
            if (yResolved == null) continue;
            double val  = ToDouble(yResolved, "Y");
            float norm  = (float)yScale.Map(val);
            float r     = norm * maxR;
            float px    = cx + MathF.Cos(angle) * r;
            float py    = cy + MathF.Sin(angle) * r;

            float d = pos.DistanceTo(new Vector2(px, py));
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
        using var gridPaint = ctx.Canvas.CreatePaint();
        gridPaint.SetColor(ctx.Theme?.RadarGridColor ?? new Color(1f, 1f, 1f, 0.08f)).SetStrokeWidth(1f);

        for (int ring = 1; ring <= GridRings; ring++)
        {
            float r = maxR * ring / GridRings;
            using var ringPath = ctx.Canvas.CreatePath();
            for (int d = 0; d < dims.Count; d++)
            {
                float angle = -MathF.PI / 2f + MathF.Tau * d / dims.Count;
                float px = cx + MathF.Cos(angle) * r;
                float py = cy + MathF.Sin(angle) * r;
                if (d == 0) ringPath.MoveTo(px, py);
                else ringPath.LineTo(px, py);
            }
            ringPath.Close();
            ctx.Canvas.Stroke(ringPath, gridPaint);
        }

        using var axisPaint = ctx.Canvas.CreatePaint();
        axisPaint.SetColor(ctx.Theme?.RadarAxisColor ?? new Color(1f, 1f, 1f, 0.15f)).SetStrokeWidth(1f);

        for (int d = 0; d < dims.Count; d++)
        {
            float angle = -MathF.PI / 2f + MathF.Tau * d / dims.Count;
            float ex = cx + MathF.Cos(angle) * maxR;
            float ey = cy + MathF.Sin(angle) * maxR;
            ctx.Canvas.DrawLine(cx, cy, ex, ey, axisPaint);

            if (ShowAxisLabels)
            {
                float labelR = maxR * 1.12f;
                float lx = cx + MathF.Cos(angle) * labelR;
                float ly = cy + MathF.Sin(angle) * labelR;

                using var labelPaint = ctx.Canvas.CreatePaint();
                labelPaint.SetColor(ctx.Theme?.RadarLabelColor ?? new Color(1f, 1f, 1f, 0.6f));
                ctx.Canvas.DrawText(dims[d], lx, ly, FontSettings.Default, labelPaint);
            }
        }

        using var tickPaint = ctx.Canvas.CreatePaint();
        tickPaint.SetColor(ctx.Theme?.RadarTickColor ?? new Color(1f, 1f, 1f, 0.35f));
        for (int ring = 1; ring <= GridRings; ring++)
        {
            float r = maxR * ring / GridRings;
            double val = yScale.Min + (yScale.Max - yScale.Min) * ring / GridRings;
            ctx.Canvas.DrawText(yScale.Format(val), cx + 4f, cy - r, FontSettings.Default, tickPaint);
        }
    }

    private static List<string> GetDimensions(MarkContext ctx)
    {
        var dims = new List<string>();
        var seen = new HashSet<string>();
        foreach (var row in ctx.Data)
        {
            var xRaw = ctx.Encodes.Resolve(Channel.X, row)?.ToString() ?? "";
            if (seen.Add(xRaw)) dims.Add(xRaw);
        }
        return dims;
    }

    private List<string> GetCachedDimensions(MarkContext ctx)
    {
        if (_cachedDims == null || _cachedDimsVersion != ctx.DataVersion)
        {
            _cachedDims = GetDimensions(ctx);
            _cachedDimsVersion = ctx.DataVersion;
        }
        return _cachedDims;
    }
}
