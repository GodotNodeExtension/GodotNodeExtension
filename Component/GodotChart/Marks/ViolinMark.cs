using System;
using System.Collections.Generic;
using Godot;

namespace GodotNodeExtension.Component.GodotChart.Marks;

/// <summary>
/// Violin mark. Renders symmetric density distributions for grouped data.
/// Data should have multiple rows per category; group by X (OrdinalScale), Y = numeric values.
/// The silhouette is a Gaussian kernel density estimate (bandwidth by Silverman's rule of thumb)
/// sampled along the value axis, so a group with only a handful of samples still draws a continuous
/// body instead of one sliver per non-empty histogram bin.
/// A category without spread (one sample, or every value equal) has no density to estimate: it is drawn
/// as a minimal horizontal line at its value, and reported with <c>GD.PushWarning</c> once.
/// Animation: violins grow from center outward.
/// </summary>
public class ViolinMark : Mark
{
    /// <inheritdoc />
    public override MarkCoordinate Coordinate => MarkCoordinate.Cartesian;

    /// <summary>
    /// Number of points the density outline is sampled at: higher is smoother (at least 8). It is a
    /// resolution, not a bin count - the outline is a kernel density estimate.
    /// </summary>
    public int BinCount { get; set; } = 20;

    /// <summary>Width ratio of the violin relative to the slot [0, 1].</summary>
    public float WidthRatio { get; set; } = 0.7f;

    /// <summary>Fill opacity for the violin body.</summary>
    public float FillOpacity { get; set; } = 0.5f;

    /// <summary>Whether to show the median marker: a dot at the median, styled with the theme's
    /// <see cref="ChartTheme.ViolinMedianDotRadius"/> / <see cref="ChartTheme.ViolinMedianDotColor"/>.</summary>
    public bool ShowMedian { get; set; } = true;

    /// <summary>Whether to show Q1/Q3 box indicator.</summary>
    public bool ShowBox { get; set; } = true;

    /// <summary>Stroke width of the outline.</summary>
    public float StrokeWidth { get; set; } = 2f;

    /// <summary>
    /// This mark keeps its interaction state in the data layer, so a chart carrying it renders single-pass
    /// (see <see cref="Chart.UseLayerCache"/>).
    /// <para>
    /// Its hover look is the silhouette's own colour, and the body is filled at
    /// <see cref="FillOpacity"/> (0.5 by default): repainting it on the overlay would blend the body twice
    /// (0.5 over 0.5) and darken the violin instead of highlighting it. The silhouette is a kernel density
    /// estimate over the whole group as well, so the overlay would have to re-run it per hover frame. The
    /// outline and the selection ring alone are not the state the reader sees.
    /// </para>
    /// </summary>
    public override bool InteractionStateInOverlay => false;

    // Reusable collections to reduce per-frame GC pressure.
    // _groupRowIndices mirrors _groups element-for-element: index j in both refers to the same row,
    // and both are kept in ascending value order (see EnsureGroupCache).
    private readonly Dictionary<string, List<double>> _groups = [];
    private readonly Dictionary<string, List<int>> _groupRowIndices = [];
    private LayoutCacheKey? _groupsKey;

    /// <summary>Whether the "group without spread" warning was already reported.</summary>
    private bool _warnedFlatGroup;

    /// <summary>
    /// Ensure the group cache is up-to-date with the current layout cache key: the owning chart, the
    /// layout version, the data list instance and the hidden series. The key - not the data version
    /// alone - is what makes a shared mark instance and a theme edit (which bumps the layout version)
    /// drop the cached groups.
    /// </summary>
    private void EnsureGroupCache(MarkContext ctx)
    {
        var cacheKey = CacheKey(ctx);
        if (_groupsKey == cacheKey) return;

        _groups.Clear();
        _groupRowIndices.Clear();
        for (int i = 0; i < ctx.Data.Count; i++)
        {
            var row = ctx.Data[i];
            if (IsSeriesHidden(ctx, row)) continue;
            var xRaw = ctx.Encodes.Resolve(Channel.X, row);
            var yRaw = ctx.Encodes.Resolve(YChannel, row);
            if (xRaw == null || yRaw == null) continue;

            // A non-finite value has no position: drop it from the distribution and the geometry,
            // the same dirty-value guard the other marks use (see IntervalMark.Render). Filtering
            // here covers both Render and HitTest, which share this cache.
            double value = ToDouble(yRaw, "Y");
            if (!double.IsFinite(value)) continue;

            string key = xRaw.ToString() ?? "";
            if (!_groups.TryGetValue(key, out var group))
            {
                group = [];
                _groups[key] = group;
                _groupRowIndices[key] = [];
            }
            group.Add(value);
            _groupRowIndices[key].Add(i);
        }

        // Sort value and source row index together (once per data version) instead of sorting the
        // bare values on every frame in Render. Sorting them as a pair keeps _groupRowIndices[j]
        // pointing at the row _groups[j] came from, so Render draws in ascending order while
        // HitTest still maps a hit back to the row that sits at that position.
        foreach (var key in _groups.Keys)
        {
            var values = _groups[key];
            var rows = _groupRowIndices[key];
            var pairs = new List<(double Value, int RowIndex)>(values.Count);
            for (int j = 0; j < values.Count; j++)
                pairs.Add((values[j], rows[j]));
            pairs.Sort((a, b) => a.Value.CompareTo(b.Value));
            for (int j = 0; j < pairs.Count; j++)
            {
                values[j] = pairs[j].Value;
                rows[j] = pairs[j].RowIndex;
            }
        }

        _groupsKey = cacheKey;
    }

    /// <inheritdoc />
    public override void ContributeScales(ScaleSet scales, EncodeSet encodes, List<DataRow> data)
    {
        // The density tails reach three bandwidths past the samples and nothing clips a mark, so the
        // value axis has to cover them; otherwise the tips are drawn outside the plot.
        var stats = new Dictionary<string, (int Count, double Sum, double SumSq, double Min, double Max)>();
        foreach (var row in data)
        {
            var xRaw = encodes.Resolve(Channel.X, row);
            var yRaw = encodes.Resolve(YChannel, row);
            if (xRaw == null || yRaw == null) continue;
            // A value that is not a number (a colour string, a null, a non-finite one) has no position:
            // it is skipped instead of throwing in the middle of the contribution.
            if (!ScaleConvert.TryToDouble(yRaw, out double value)) continue;

            string key = xRaw.ToString() ?? "";
            if (!stats.TryGetValue(key, out var s)) s = (0, 0, 0, double.MaxValue, double.MinValue);
            stats[key] = (s.Count + 1, s.Sum + value, s.SumSq + value * value,
                          Math.Min(s.Min, value), Math.Max(s.Max, value));
        }

        double lo = double.MaxValue, hi = double.MinValue;
        foreach (var s in stats.Values)
        {
            if (s.Count < 2 || s.Max <= s.Min) continue;
            double bandwidth = Bandwidth(s.Count, s.Sum / s.Count, s.SumSq, s.Max - s.Min);
            lo = Math.Min(lo, s.Min - 3 * bandwidth);
            hi = Math.Max(hi, s.Max + 3 * bandwidth);
        }
        if (lo > hi) return;

        ScaleContributionHelper.ContributeRange(scales, YChannel, lo, hi);
    }

    /// <summary>
    /// Silverman's rule of thumb for the kernel bandwidth: <c>1.06 * sigma * n^(-1/5)</c>, from a
    /// running sum/sum-of-squares accumulator. A group without spread falls back to a tenth of its range.
    /// </summary>
    private static double Bandwidth(int n, double mean, double sumSq, double range)
    {
        double variance = n > 1 ? Math.Max(0, (sumSq - n * mean * mean) / (n - 1)) : 0;
        double sigma = Math.Sqrt(variance);
        return sigma > 0 ? 1.06 * sigma * Math.Pow(n, -0.2) : range * 0.1;
    }

    /// <summary>
    /// A group's density outline: the value range it spans and the density sampled on a grid, normalised
    /// so the peak is 1. Renderer and hit test share it, so the silhouette they use cannot drift apart.
    /// </summary>
    internal readonly record struct DensityCurve(double Lo, double Hi, double[] Density)
    {
        /// <summary>Normalised density at a value (peak = 1), linearly interpolated between samples.</summary>
        internal double At(double value)
        {
            if (Density.Length == 0 || Hi <= Lo) return 0;
            double t = (value - Lo) / (Hi - Lo) * (Density.Length - 1);
            if (t <= 0) return Density[0];
            if (t >= Density.Length - 1) return Density[^1];
            int i = (int)t;
            double f = t - i;
            return Density[i] * (1 - f) + Density[i + 1] * f;
        }
    }

    /// <summary>
    /// Gaussian kernel density estimate of one sorted group, evaluated on <paramref name="resolution"/>
    /// points spread over the samples extended by three bandwidths on each side (that is where the tails
    /// have faded, so the silhouette ends in a point rather than a flat cap).
    /// </summary>
    internal static DensityCurve BuildDensityCurve(List<double> sorted, int resolution)
    {
        int points = Math.Max(8, resolution);
        int n = sorted.Count;
        if (n == 0) return new DensityCurve(0, 1, []);

        double min = sorted[0], max = sorted[^1];
        if (max <= min) return new DensityCurve(min, min, new double[points]);

        double sum = 0, sumSq = 0;
        for (int i = 0; i < n; i++) { sum += sorted[i]; sumSq += sorted[i] * sorted[i]; }
        double bandwidth = Bandwidth(n, sum / n, sumSq, max - min);

        double lo = min - 3 * bandwidth;
        double hi = max + 3 * bandwidth;
        double step = (hi - lo) / (points - 1);
        double norm = 1.0 / (n * bandwidth * Math.Sqrt(2 * Math.PI));

        var density = new double[points];
        double peak = 0;
        for (int g = 0; g < points; g++)
        {
            double x = lo + g * step;
            double acc = 0;
            for (int i = 0; i < n; i++)
            {
                double z = (x - sorted[i]) / bandwidth;
                acc += Math.Exp(-0.5 * z * z);
            }
            density[g] = acc * norm;
            if (density[g] > peak) peak = density[g];
        }
        if (peak > 0)
            for (int g = 0; g < points; g++) density[g] /= peak;

        return new DensityCurve(lo, hi, density);
    }

    /// <inheritdoc />
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
            if (!_groups.TryGetValue(cat, out var vals) || vals.Count == 0) continue;

            float cx = ctx.Plot.MapX((float)xScale.Map(cat));
            var firstRow = ctx.Data[_groupRowIndices[cat][0]];
            int firstIndex = _groupRowIndices[cat][0];
            // A violin is one element per category, so the hover state is the group's - the same membership the
            // selected ring below uses. Asking about the group's smallest row (which is what passing firstIndex
            // did) lit the violin up only while the pointer was on that one sample.
            int stateIndex = _groupRowIndices[cat].Contains(ctx.HoveredRowIndex)
                ? ctx.HoveredRowIndex
                : firstIndex;
            var color = ResolveFill(ctx, firstRow, stateIndex, GetDefaultColor(ctx));
            float opacity = ComputeElementOpacity(ctx, firstRow, firstIndex);

            // Sorted once when the group cache is built (see EnsureGroupCache).
            double yMin = vals[0];
            double yMax = vals[^1];
            // A group without spread (a single sample, or all values equal) has no density outline:
            // the estimate has no interval to spread over, so the category used to disappear from the
            // chart without a word. Draw a minimal visible line at its value instead.
            if (vals.Count < 2 || yMax <= yMin)
            {
                DrawFlatGroup(ctx, cx, ctx.Plot.MapY((float)yScale.Map(yMin)), violinW * anim,
                              color, opacity, cat);
                continue;
            }

            // Density outline (Gaussian kernel estimate, peak = 1)
            var curve = BuildDensityCurve(vals, BinCount);
            int points = curve.Density.Length;
            if (points == 0 || curve.Hi <= curve.Lo) continue;

            var fillPath = ShapePath(ctx);
            var fillPaint = ShapePaint(ctx);

            // Right side (low to high value), then the left side back down; the tails have faded to
            // almost nothing at the ends, so the body tapers instead of ending in a flat cap.
            fillPath.MoveTo(cx, ctx.Plot.MapY((float)yScale.Map(curve.Lo)));
            for (int g = 0; g < points; g++)
            {
                double value = curve.Lo + (curve.Hi - curve.Lo) * g / (points - 1);
                float py = ctx.Plot.MapY((float)yScale.Map(value));
                float pw = (float)curve.Density[g] * violinW * anim;
                fillPath.LineTo(cx + pw, py);
            }
            fillPath.LineTo(cx, ctx.Plot.MapY((float)yScale.Map(curve.Hi)));

            for (int g = points - 1; g >= 0; g--)
            {
                double value = curve.Lo + (curve.Hi - curve.Lo) * g / (points - 1);
                float py = ctx.Plot.MapY((float)yScale.Map(value));
                float pw = (float)curve.Density[g] * violinW * anim;
                fillPath.LineTo(cx - pw, py);
            }
            fillPath.Close();

            fillPaint.SetColor(color).SetAntiAlias(true).SetOpacity(FillOpacity * opacity);
            ctx.Canvas.Fill(fillPath, fillPaint);

            // Outline
            var strokePaint = ShapePaint(ctx);
            strokePaint.SetColor(color).SetStrokeWidth(StrokeWidth).SetAntiAlias(true).SetOpacity(opacity);
            ctx.Canvas.Stroke(fillPath, strokePaint);

            // Selected: a violin is one element per category, so the selected row rings the violin it
            // belongs to (States.SelectedStroke / States.SelectedStrokeWidth).
            if (ctx.SelectedRowIndex >= 0 && _groupRowIndices[cat].Contains(ctx.SelectedRowIndex))
            {
                var selPaint = ShapePaint(ctx);
                ApplySelectionPaint(ctx, selPaint, opacity);
                ctx.Canvas.Stroke(fillPath, selPaint);
            }

            // Median + IQR indicators
            if (ShowMedian || ShowBox)
            {
                double median = Quantile(vals, 0.5);
                float medianY = ctx.Plot.MapY((float)yScale.Map(median));

                if (ShowBox && vals.Count >= 4)
                {
                    double q1 = Quantile(vals, 0.25);
                    double q3 = Quantile(vals, 0.75);
                    float q1Y = ctx.Plot.MapY((float)yScale.Map(q1));
                    float q3Y = ctx.Plot.MapY((float)yScale.Map(q3));
                    float boxHalfW = violinW * (ctx.Theme ?? ChartTheme.Default).ViolinBoxWidthRatio * anim;

                    var boxPath = ShapePath(ctx);
                    var boxPaint = ShapePaint(ctx);
                    float boxTop = Math.Min(q1Y, q3Y);
                    float boxH = Math.Abs(q3Y - q1Y);
                    boxPath.Rect(cx - boxHalfW, boxTop, boxHalfW * 2, boxH);
                    var vbc = (ctx.Theme ?? ChartTheme.Default).ViolinBoxColor;
                    boxPaint.SetColor(vbc with { A = vbc.A * opacity }).SetAntiAlias(true);
                    ctx.Canvas.Fill(boxPath, boxPaint);
                }

                if (ShowMedian)
                {
                    var dotPath = ShapePath(ctx);
                    var dotPaint = ShapePaint(ctx);
                    var vmdr = (ctx.Theme ?? ChartTheme.Default).ViolinMedianDotRadius;
                    dotPath.Circle(cx, medianY, vmdr * anim);
                    var vmdc = (ctx.Theme ?? ChartTheme.Default).ViolinMedianDotColor;
                    dotPaint.SetColor(vmdc with { A = vmdc.A * opacity }).SetAntiAlias(true);
                    ctx.Canvas.Fill(dotPath, dotPaint);
                }
            }
        }
    }

    /// <summary>
    /// Marker drawn for a group without spread: a horizontal line at the group's value, as wide as the
    /// violin body would be, so the category stays visible on the chart. The situation is reported once
    /// per mark instance, because a flat group is a property of the data, not of the frame.
    /// </summary>
    /// <param name="ctx">Render context.</param>
    /// <param name="cx">Category centre on the X axis.</param>
    /// <param name="cy">Screen Y of the group's value.</param>
    /// <param name="halfWidth">Half the drawn violin width, already scaled by the animation.</param>
    /// <param name="color">Fill colour resolved for the group.</param>
    /// <param name="opacity">Opacity resolved for the group.</param>
    /// <param name="category">Category name, reported in the warning.</param>
    private void DrawFlatGroup(MarkContext ctx, float cx, float cy, float halfWidth,
                               Color color, float opacity, string category)
    {
        if (!_warnedFlatGroup)
        {
            _warnedFlatGroup = true;
            GD.PushWarning(
                $"ViolinMark: category '{category}' has no spread (one sample, or all values equal); " +
                "a minimal marker is drawn instead of a violin body.");
        }
        if (halfWidth <= 0f) return;

        var path = ShapePath(ctx);
        var paint = ShapePaint(ctx);
        path.MoveTo(cx - halfWidth, cy);
        path.LineTo(cx + halfWidth, cy);
        paint.SetColor(color).SetStrokeWidth(MathF.Max(1f, StrokeWidth)).SetAntiAlias(true)
             .SetOpacity(opacity);
        ctx.Canvas.Stroke(path, paint);
    }

    /// <summary>
    /// Linear-interpolated quantile (the "type 7" definition used by R/NumPy), so small samples do
    /// not snap q1/median/q3 onto list indices.
    /// </summary>
    internal static double Quantile(List<double> sorted, double p)
    {
        if (sorted.Count == 0) return 0;
        if (sorted.Count == 1) return sorted[0];

        double rank = p * (sorted.Count - 1);
        int lower = (int)Math.Floor(rank);
        int upper = Math.Min(lower + 1, sorted.Count - 1);
        double fraction = rank - lower;
        return sorted[lower] + (sorted[upper] - sorted[lower]) * fraction;
    }

    /// <summary>Data value at a screen Y position (inverse of <c>PlotArea.MapY</c> + scale).</summary>
    private static double InverseMap(IScale yScale, MarkContext ctx, float screenY)
    {
        var plot = ctx.Plot;
        if (plot.Height <= 0f) return 0;
        double norm = (plot.Y + plot.Height - screenY) / plot.Height;
        // The scale maps value -> [0,1]; invert it for the linear scales the violin supports.
        if (yScale is LinearScale linear && linear.Max > linear.Min)
            return linear.Min + norm * (linear.Max - linear.Min);
        return norm;
    }

    /// <inheritdoc />
    public override HitResult? HitTest(MarkContext ctx, Vector2 screenPos)
    {
        var xScale = ctx.Scales.TryGet(Channel.X) as OrdinalScale;
        var yScale = ctx.Scales.TryGet(YChannel) as LinearScale;
        if (xScale == null || yScale == null) return null;
        if (xScale.Domain.Count == 0) return null;

        float slotW = ctx.Plot.Width / xScale.Domain.Count;

        // Reuse cached groups from Render() when data version matches
        EnsureGroupCache(ctx);

        // Half of the drawn violin width at full density; the actual width at a given height comes
        // from the same density curve the renderer uses.
        float violinW = slotW * WidthRatio / 2f;

        foreach (var cat in xScale.Domain)
        {
            float cx = ctx.Plot.MapX((float)xScale.Map(cat));
            if (Math.Abs(screenPos.X - cx) > slotW / 2f) continue;

            if (!_groups.TryGetValue(cat, out var vals) || vals.Count == 0) continue;
            var indices = _groupRowIndices[cat];

            // Density contour check: only the painted silhouette is hit-testable (the empty space
            // beside a narrow violin used to report a hover on "nothing").
            double catMin = vals[0], catMax = vals[^1];
            if (catMax > catMin)
            {
                var curve = BuildDensityCurve(vals, BinCount);
                double yValue = InverseMap(yScale, ctx, screenPos.Y);
                if (yValue < curve.Lo || yValue > curve.Hi) continue;

                float halfWidth = (float)curve.At(yValue) * violinW * ComputeAnimProgress(ctx);
                if (Math.Abs(screenPos.X - cx) > halfWidth + 1f) continue;
            }
            else
            {
                // A flat group draws only its minimal line (see DrawFlatGroup): accept hits near that
                // line, not anywhere in the category slot.
                float flatY = ctx.Plot.MapY((float)yScale.Map(catMin));
                if (Math.Abs(screenPos.Y - flatY) > MathF.Max(2f, StrokeWidth)) continue;
            }

            // Find closest data row by mapped Y position
            int bestIdx = -1;
            float bestDist = float.MaxValue;
            for (int j = 0; j < vals.Count; j++)
            {
                float rowY = ctx.Plot.MapY((float)yScale.Map(vals[j]));
                float d = Math.Abs(screenPos.Y - rowY);
                if (d < bestDist)
                {
                    bestDist = d;
                    bestIdx = indices[j];
                }
            }
            if (bestIdx >= 0 && bestIdx < ctx.Data.Count)
            {
                return new HitResult
                {
                    Hit = true, Row = ctx.Data[bestIdx], RowIndex = bestIdx,
                    ScreenX = cx, ScreenY = screenPos.Y,
                    Label = cat,
                    MarkType = nameof(ViolinMark),
                };
            }
        }
        return null;
    }
}
