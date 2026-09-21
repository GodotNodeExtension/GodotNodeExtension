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

    /// <summary>Whether to smooth the line with cubic Bézier segments that have a horizontal tangent at every data point.</summary>
    public bool  Smooth      { get; set; } = true;

    /// <summary>Whether to fill the area under the line.</summary>
    public bool  ShowArea    { get; set; }

    /// <summary>Opacity of the area fill [0, 1].</summary>
    public float AreaOpacity { get; set; } = 0.15f;

    // Cached row-to-index lookup to avoid per-frame allocation
    private Dictionary<DataRow, int>? _rowIndexMap;
    private int _rowIndexMapVersion = -1;

    // Reused bucket output of DecimateSeries (see there): a second view of the same series for the path only.
    private readonly List<(float x, float y)> _decimatedPoints = [];

    /// <summary>
    /// Everything the collected screen points of one series depend on. Used to skip the collection when
    /// nothing changed: see <see cref="GeometryFor"/>. The series key compares by value, so it belongs in
    /// here - one mark instance collects several series.
    /// <para>
    /// The pointer state and the animation progress are deliberately <b>not</b> part of it: mapping a value
    /// through the scales cannot depend on either (the entry animation only clips the reveal, the hover
    /// marker is drawn on top), and keeping them in the key re-mapped the whole series on every pointer
    /// move - which is exactly the cost the overlay pass exists to avoid.
    /// </para>
    /// </summary>
    private readonly record struct PointsKey(string? Series, int Layout, int Data, float PlotX, float PlotY,
        float PlotWidth, float PlotHeight, bool Area, StackMode Stack,
        StepMode Step, DecimateMode Decimate, float PointsPerPixel, bool Smooth);

    /// <summary>
    /// Screen geometry of one series: the mapped points, the row behind each of them, and the inputs they
    /// were collected from. Kept per series rather than in one shared buffer - with a single buffer the
    /// second series always overwrote the first one's key, so a chart with two series never hit the cache
    /// and re-mapped every series on every frame. It is also what lets the overlay pass place the hover
    /// marker without mapping anything.
    /// </summary>
    private sealed class SeriesGeometry
    {
        /// <summary>Mapped screen points, one per row that produced one.</summary>
        public readonly List<(float x, float y)> Points = [];

        /// <summary>
        /// Rows that produced a point, in the same order as <see cref="Points"/>. A row whose value is
        /// missing is skipped in both lists, so hover highlighting and labels cannot drift onto a
        /// neighbouring point.
        /// </summary>
        public readonly List<DataRow> Rows = [];

        /// <summary>Inputs the points were collected from; stale geometry is never drawn.</summary>
        public PointsKey Inputs;

        /// <summary>True once <see cref="Points"/> holds a collected series.</summary>
        public bool Collected;

        /// <summary>True when the geometry was collected from exactly these inputs.</summary>
        public bool Matches(in PointsKey inputs) => Collected && Inputs == inputs;
    }

    private readonly Dictionary<string, SeriesGeometry> _geometry = new(StringComparer.Ordinal);

    /// <summary>Inputs the screen points of one series depend on (see <see cref="PointsKey"/>).</summary>
    private PointsKey GeometryKey(MarkContext ctx, string? seriesKey)
        => new(seriesKey, ctx.LayoutVersion, ctx.DataVersion, ctx.Plot.X, ctx.Plot.Y,
            ctx.Plot.Width, ctx.Plot.Height, ShowArea, Stack, Step, Decimate, DecimatePointsPerPixel, Smooth);

    /// <summary>
    /// The screen points of one series for this frame: collected when the layout, the data or the plotting
    /// area changed, reused otherwise. Mapping a 100k-row series costs tens of milliseconds, and a page that
    /// is not animating and not being interacted with redraws the same geometry frame after frame - a static
    /// chart pays once per data change (or zoom) instead of once per frame.
    /// <para>
    /// Shared by the data layer (<see cref="Render"/>) and the overlay pass
    /// (<see cref="RenderOverlay"/>), which is why the geometry belongs to the mark rather than to one call:
    /// the highlight reads the same points the path was built from.
    /// </para>
    /// </summary>
    private SeriesGeometry GeometryFor(MarkContext ctx, string key, string? seriesKey, List<DataRow> rows)
    {
        if (!_geometry.TryGetValue(key, out var geometry))
        {
            geometry = new SeriesGeometry();
            _geometry[key] = geometry;
        }

        var inputs = GeometryKey(ctx, seriesKey);
        if (geometry.Matches(inputs)) return geometry;

        var xScale = ctx.Scales.TryGet(Channel.X);
        var yScale = ctx.Scales.TryGet(YChannel);
        if (xScale is null || yScale is null)
        {
            geometry.Collected = false;
            return geometry;
        }

        geometry.Points.Clear();
        geometry.Rows.Clear();
        if (geometry.Points.Capacity < rows.Count) geometry.Points.Capacity = rows.Count;

        // Resolve the two field names once per series instead of per row. Going through the encode table
        // and then a dictionary lookup for every point is what made a 100k-row series cost tens of
        // milliseconds before any geometry work - the Y2 auto-fit hoists its lookup the same way. Encodes
        // that are not plain fields (constants, computed values) keep the general path.
        var xEncode = ctx.Encodes.TryGet(Channel.X) as FieldEncode;
        var yEncode = ctx.Encodes.TryGet(YChannel) as FieldEncode;

        foreach (var row in rows)
        {
            var xRaw = xEncode is null ? ctx.Encodes.Resolve(Channel.X, row)
                                       : (row.Has(xEncode.FieldName) ? row.Get(xEncode.FieldName) : null);
            var yRaw = yEncode is null ? ctx.Encodes.Resolve(YChannel, row)
                                       : (row.Has(yEncode.FieldName) ? row.Get(yEncode.FieldName) : null);
            if (xRaw == null || yRaw == null) continue;
            double xN = MapSafely(xScale, xRaw);
            double yN = MapSafely(yScale, yRaw);
            // Non-finite (a NaN value, or one the scale cannot read at all): no screen position exists,
            // skip the point.
            if (!double.IsFinite(xN) || !double.IsFinite(yN)) continue;
            geometry.Points.Add((ctx.Plot.MapX(xN), ctx.Plot.MapY(yN)));
            geometry.Rows.Add(row);
        }

        geometry.Inputs = inputs;
        geometry.Collected = true;
        return geometry;
    }

    /// <summary>
    /// Index in a series' point list of the row the chart reports under <paramref name="rowIndex"/>, or -1
    /// when the series has no such row. Comparing the global index keeps one mark per element: two marks of
    /// one chart highlight the element with the same index in each of them.
    /// </summary>
    private static int PointIndexOfRow(SeriesGeometry geometry, Dictionary<DataRow, int> rowIndexMap, int rowIndex)
    {
        if (rowIndex < 0) return -1;

        // The rows are collected in table order, so a series' global row indices are non-decreasing: the lookup
        // is a binary search. The linear scan this used to be made every hover and every selection frame cost
        // O(rows) - 10-29 ms at 100k-200k points - which is exactly what the overlay pass must not do, since the
        // whole point of drawing the interactive state in the overlay is that it is cheap.
        int low = 0, high = geometry.Rows.Count - 1;
        while (low <= high)
        {
            int middle = low + ((high - low) >> 1);
            if (!rowIndexMap.TryGetValue(geometry.Rows[middle], out int globalIndex))
            {
                // Defensive: a row the map does not know (or a geometry not in table order) keeps the old,
                // always-correct scan, so this optimisation cannot change what is found.
                return ScanPointIndexOfRow(geometry, rowIndexMap, rowIndex);
            }

            if (globalIndex == rowIndex) return middle;
            if (globalIndex < rowIndex) low = middle + 1;
            else high = middle - 1;
        }

        return -1;
    }

    /// <summary>Linear fallback for <see cref="PointIndexOfRow"/>, used when the table order cannot be trusted.</summary>
    private static int ScanPointIndexOfRow(SeriesGeometry geometry, Dictionary<DataRow, int> rowIndexMap, int rowIndex)
    {
        for (int i = 0; i < geometry.Rows.Count; i++)
            if (rowIndexMap.TryGetValue(geometry.Rows[i], out int globalIndex) && globalIndex == rowIndex)
                return i;
        return -1;
    }
    // Reusable buffers for the stacked layout (RenderStacked used to allocate ~6 collections per
    // series per frame). RenderStacked and HitTestStacked drive the same computation, so these
    // buffers hold the one layout both of them consume.
    private readonly List<object> _stackedXValues = [];
    private readonly HashSet<string> _stackedXSeen = [];
    private readonly Dictionary<string, float> _stackedCategoryTotals = new();
    private readonly Dictionary<string, double> _stackedXToY = new();
    private readonly Dictionary<string, (DataRow Row, int Index)?> _stackedXToRow = new();
    private readonly Dictionary<string, double> _stackedBaselines = new();
    private readonly List<StackedBand> _stackedBands = [];
    private readonly Dictionary<string, StackedBand> _stackedBandCache = new();
    private readonly List<(float x, float y)> _stackedBottomReverse = [];

    /// <summary>
    /// Display-level reduction of the points the path is built from. Default <see cref="DecimateMode.Auto"/>:
    /// once the series has more points than the plot has columns (see <see cref="DecimatePointsPerPixel"/>),
    /// each pixel column contributes only its lowest and its highest point. The shape survives - including
    /// spikes, which a stride sampler would step over - while the path stays proportional to the canvas
    /// instead of to the table. Hit testing, tooltips and the snapshots keep reading the full table.
    /// </summary>
    public DecimateMode Decimate { get; set; } = DecimateMode.Auto;

    /// <summary>Points per pixel column above which <see cref="DecimateMode.Auto"/> reduces the series (default 2).</summary>
    public float DecimatePointsPerPixel { get; set; } = 2f;

    /// <summary>
    /// Stacking mode. Default is None.
    /// <para>
    /// Settable, like <see cref="IntervalMark.Stack"/>: a host that configures the mark the chart built
    /// (<c>ChartView.ConfigureMark</c>) has to be able to turn stacking on, and a stacked area is only
    /// reachable through this property.
    /// </para>
    /// </summary>
    public StackMode Stack { get; set; } = StackMode.None;

    /// <summary>Step line mode. Default is None (normal line).</summary>
    public StepMode Step { get; set; } = StepMode.None;

    /// <inheritdoc />
    public override void ContributeScales(
        ScaleSet scales, EncodeSet encodes, List<DataRow> data)
    {
        StackScaleHelper.ContributeStackedYScale(Stack, scales, encodes, data, YChannel);
    }

    /// <summary>
    /// Bucket the series by pixel column and keep each column's lowest and highest point, in the order they
    /// appear, so the reduced path still covers spikes a stride sampler would drop. Returns null when there
    /// is nothing to reduce (mode Off, or the series fits the plot).
    /// <para>
    /// <paramref name="upperEnvelope"/> is for shapes that fill from the baseline (an area, a step line, a
    /// stacked band): only the highest point of a column is kept, because the fill of an area runs to the
    /// top of the band and a low point in the same column would tear its edge.
    /// </para>
    /// </summary>
    private List<(float x, float y)>? DecimateSeries(
        MarkContext ctx, List<(float x, float y)> points, bool upperEnvelope)
    {
        int count = points.Count;
        if (Decimate == DecimateMode.Off || count < 2) return null;

        float width = ctx.Plot.Width;
        if (!(width > 1f)) return null;
        if (Decimate == DecimateMode.Auto && count / width <= DecimatePointsPerPixel) return null;

        var reduced = _decimatedPoints;
        reduced.Clear();

        int bucketIndex = int.MinValue;
        float minY = 0f, maxY = 0f;
        int minAt = 0, maxAt = 0;

        void Flush()
        {
            if (bucketIndex == int.MinValue) return;
            if (upperEnvelope)
            {
                reduced.Add((x: bucketIndex + ctx.Plot.X, y: maxY));
                return;
            }

            // Both extremes, in the order the data produced them: the path keeps moving to the right.
            if (minAt <= maxAt)
            {
                reduced.Add((x: bucketIndex + ctx.Plot.X, y: minY));
                if (maxAt != minAt) reduced.Add((x: bucketIndex + ctx.Plot.X, y: maxY));
            }
            else
            {
                reduced.Add((x: bucketIndex + ctx.Plot.X, y: maxY));
                reduced.Add((x: bucketIndex + ctx.Plot.X, y: minY));
            }
        }

        for (int i = 0; i < count; i++)
        {
            var (x, y) = points[i];
            int bucket = (int)(x - ctx.Plot.X);
            if (bucket != bucketIndex)
            {
                Flush();
                bucketIndex = bucket;
                minY = maxY = y;
                minAt = maxAt = i;
                continue;
            }

            if (y < minY) { minY = y; minAt = i; }
            if (y > maxY) { maxY = y; maxAt = i; }
        }
        Flush();

        return reduced;
    }

    /// <inheritdoc />
    public override void Render(MarkContext ctx)
    {
        if (Stack != StackMode.None)
        {
            RenderStacked(ctx);
            return;
        }

        var xScl = ctx.Scales.TryGet(Channel.X);
        var yScl = ctx.Scales.TryGet(YChannel);
        // An unfitted channel (empty data, missing encode) skips the mark instead of throwing.
        if (xScl is null || yScl is null) return;

        var groups = CachedGroupByChannel(ctx, Channel.Color);
        float anim = ComputeAnimProgress(ctx);
        float globalOpacity = ctx.Animation.GlobalOpacity;

        // Reused buffer: a fresh list per frame was a measurable allocation source.
        List<LabelElement>? labels = BeginLabelCollection();

        // The interaction-state visuals are painted here only while the data layer is the only layer. With
        // the chart's layer cache on they belong to RenderOverlay, which runs after the cached layer - see
        // Mark.InteractionStateInOverlay.
        bool stateHere = !ctx.StateInOverlay;

        // Build row-to-index lookup once, cached across frames. The hover ring and the selection ring both
        // read it, so gating it on the hover alone made Chart.Select() draw no ring until the pointer
        // happened to be over the chart - and made the ring vanish as soon as it left.
        Dictionary<DataRow, int>? rowIndexMap = stateHere
            && (ctx.HoveredRowIndex >= 0 || ctx.SelectedRowIndex >= 0)
            ? GetRowIndexMap(ctx)
            : null;

        foreach (var (key, rows) in groups)
        {
            // Skip hidden series
            string? seriesKey = key.ToString();
            if (ctx.HiddenSeries != null && ctx.HiddenSeries.Contains(seriesKey ?? ""))
                continue;

            var color = ResolveSeriesColor(ctx, key);

            // Series focus: reduce opacity for non-focused series
            float seriesOpacity = ComputeSeriesOpacity(ctx, seriesKey, globalOpacity);

            // The screen points are collected when the layout, the data or the plotting area changed and
            // reused otherwise (see GeometryFor).
            var geometry = GeometryFor(ctx, seriesKey ?? "", seriesKey, rows);
            if (geometry.Points.Count < 2) continue;

            // Animation: clip-based reveal from left to right (pixel-smooth)
            bool clipping = anim < 1f && ctx.Canvas.Capabilities.SupportsClipping;
            IDisposable? clipScope = null;
            if (clipping)
            {
                float clipW = ctx.Plot.Width * anim;
                clipScope = ctx.Canvas.SaveScope();
                ctx.Canvas.ClipRect(ctx.Plot.X, ctx.Plot.Y, clipW, ctx.Plot.Height);
            }

            try
            {
                // Decimation is for the path only: the interaction-state and label loops below stay on the
                // full point list, so they keep pairing with the rows and report real values.
                var points = DecimateSeries(ctx, geometry.Points, ShowArea || Step != StepMode.None)
                             ?? geometry.Points;

                // Area/line baseline: the data zero line, not the bottom of the plot.
                float zeroY = ctx.Plot.MapY(Math.Clamp((float)yScl.Map(0), 0f, 1f));

                // Area fill
                if (ShowArea && ctx.Canvas.Capabilities.SupportsGradients)
                {
                    var areaPath = ShapePath(ctx);
                    BuildLinePath(points, areaPath);
                    areaPath.LineTo(points[^1].x, zeroY);
                    areaPath.LineTo(points[0].x,  zeroY);
                    areaPath.Close();
                    // The gradient gets a paint of its own: a shader sticks to the paint it was set on
                    // (SetColor does not clear it, and Skia lets the shader win over the colour), so
                    // reusing this mark's shape paint would stroke the line below with the fading
                    // gradient instead of the series colour. The pooled paint is cleaned on return.
                    using var areaPaint = ctx.Canvas.CreatePaint();
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
                var linePath = ShapePath(ctx);
                var linePaint = ShapePaint(ctx);
                BuildLinePath(points, linePath);
                linePaint.SetColor(color)
                         .SetStrokeWidth(StrokeWidth)
                         .SetLineCap(LineCap.Round)
                         .SetLineJoin(LineJoin.Round)
                         .SetOpacity(seriesOpacity);
                ctx.Canvas.Stroke(linePath, linePaint);

                if (stateHere) DrawInteractionState(ctx, geometry, color, seriesOpacity, rowIndexMap);

                // Restore clip state if we were clipping for animation
            }
            finally
            {
                // Guarantees the clip is undone even if a drawing call throws (the old code
                // leaked the save/clip pair, which clipped every following frame).
                clipScope?.Dispose();
            }

            // Collect label elements for this series
            if (labels != null)
            {
                // Outside the try block above: refer to the geometry directly (the local alias is
                // scoped to the clipped section).
                for (int ri = 0; ri < geometry.Rows.Count; ri++)
                {
                    var yRaw = ctx.Encodes.Resolve(YChannel, geometry.Rows[ri]);
                    var xRaw = ctx.Encodes.Resolve(Channel.X, geometry.Rows[ri]);
                    if (yRaw == null) continue;
                    string text = FormatLabel(LabelFormat, yRaw, xRaw);
                    labels.Add(new LabelElement(geometry.Points[ri].x, geometry.Points[ri].y, text, seriesOpacity));
                }
            }
        }

        if (labels != null)
            DrawLabels(ctx, labels);
    }

    /// <summary>
    /// Paint one series' interaction-state visuals: the enlarged hover marker (its ring included) and the
    /// selection ring. Both read the series' collected screen points, so the highlight sits exactly on the
    /// vertex the path was built from - and nothing is mapped again.
    /// <para>
    /// <see cref="Render"/> calls this while the data layer is the only layer (the historical single pass);
    /// <see cref="RenderOverlay"/> calls it while the chart keeps that layer in an image, where the state
    /// must not be baked into it.
    /// </para>
    /// </summary>
    private void DrawInteractionState(MarkContext ctx, SeriesGeometry geometry,
        Color color, float seriesOpacity, Dictionary<DataRow, int>? rowIndexMap)
    {
        if (rowIndexMap is null) return;

        // Hover: highlight the hovered data point with an enlarged circle.
        int hoverAt = PointIndexOfRow(geometry, rowIndexMap, ctx.HoveredRowIndex);
        if (hoverAt >= 0)
        {
            var pt = geometry.Points[hoverAt];
            int hoverIndex = ctx.HoveredRowIndex;
            float hoverR = (ctx.Theme ?? ChartTheme.Default).LineHoverPointRadius * HoverScaled(ctx, ctx.Animation.HoverScale);
            var hPath = ShapePath(ctx);
            var hPaint = ShapePaint(ctx);
            hPath.Circle(pt.x, pt.y, hoverR);
            // Marker fill: series colour as the default; ResolveFill applies the style callback (and the
            // hover brighten while the state is drawn in the data layer), ActiveFillOf adds the brighten
            // while the overlay owns it - the two passes paint the same colour.
            Color fill = ResolveFill(ctx, geometry.Rows[hoverAt], hoverIndex, color);
            if (ctx.StateInOverlay) fill = ActiveFillOf(ctx, fill);
            hPaint.SetColor(fill)
                  .SetAntiAlias(true)
                  .SetOpacity(seriesOpacity);
            ctx.Canvas.Fill(hPath, hPaint);

            // Highlight ring
            var ringPaint = ShapePaint(ctx);
            var hrc = (ctx.Theme ?? ChartTheme.Default).LineHoverRingColor;
            ringPaint.SetColor(hrc with { A = hrc.A * seriesOpacity })
                     .SetStrokeWidth((ctx.Theme ?? ChartTheme.Default).LineHoverRingStrokeWidth);
            ctx.Canvas.Stroke(hPath, ringPaint);
        }

        // Selection: ring the selected vertex, like the hover ring above but with the theme's
        // selection paint (States.SelectedStroke / SelectedStrokeWidth). Row indices come from the
        // shared map, so this follows the same layout the hover snap uses.
        int selectedAt = PointIndexOfRow(geometry, rowIndexMap, ctx.SelectedRowIndex);
        if (selectedAt >= 0)
        {
            var pt = geometry.Points[selectedAt];
            float ringR = (ctx.Theme ?? ChartTheme.Default).LineHoverPointRadius;
            var sPath = ShapePath(ctx);
            var sPaint = ShapePaint(ctx);
            sPath.Circle(pt.x, pt.y, ringR);
            ApplySelectionPaint(ctx, sPaint, seriesOpacity);
            ctx.Canvas.Stroke(sPath, sPaint);
        }
    }

    /// <inheritdoc />
    public override bool InteractionStateInOverlay => true;

    /// <inheritdoc />
    public override void RenderOverlay(MarkContext ctx)
    {
        // Only while the chart keeps the data layer in an image: with the cache off Render painted the state.
        // The rows are read where they are needed (the marker looks them up in this frame's geometry).
        if (!OverlayRows(ctx, out _, out _)) return;

        // A stacked band carries no hover/selection visual of its own (RenderStacked draws none).
        if (Stack != StackMode.None) return;

        float globalOpacity = ctx.Animation.GlobalOpacity;
        var rowIndexMap = GetRowIndexMap(ctx);

        // The data layer draws the marks clipped to the plot; the overlay repeats the clip so a marker at
        // the plot edge paints the same pixels in both modes.
        using var clip = new CanvasSaveScope(ctx.Canvas);
        ctx.Canvas.ClipRect(ctx.Plot.X, ctx.Plot.Y, ctx.Plot.Width, ctx.Plot.Height);

        foreach (var (key, _) in CachedGroupByChannel(ctx, Channel.Color))
        {
            string? seriesKey = key.ToString();
            if (ctx.HiddenSeries != null && ctx.HiddenSeries.Contains(seriesKey ?? ""))
                continue;

            // A geometry that does not match this frame's inputs is not drawn: better no highlight than one
            // sitting on coordinates the path no longer has.
            if (!_geometry.TryGetValue(seriesKey ?? "", out var geometry)) continue;
            if (!geometry.Matches(GeometryKey(ctx, seriesKey)) || geometry.Points.Count < 2) continue;

            var color = ResolveSeriesColor(ctx, key);
            float seriesOpacity = ComputeSeriesOpacity(ctx, seriesKey, globalOpacity);
            DrawInteractionState(ctx, geometry, color, seriesOpacity, rowIndexMap);
        }
    }

    /// <summary>
    /// Row → index lookup over <c>ctx.Data</c>, cached per data version. Shared by the hover
    /// highlight and the stacked hit test, so both report the same row identity.
    /// </summary>
    private Dictionary<DataRow, int> GetRowIndexMap(MarkContext ctx)
    {
        if (_rowIndexMap == null || _rowIndexMapVersion != ctx.DataVersion)
        {
            _rowIndexMap = new Dictionary<DataRow, int>(ctx.Data.Count);
            for (int i = 0; i < ctx.Data.Count; i++)
                _rowIndexMap[ctx.Data[i]] = i;
            _rowIndexMapVersion = ctx.DataVersion;
        }
        return _rowIndexMap;
    }

    /// <summary>
    /// Screen geometry of one stacked band: the top polyline the series is drawn along, the bottom
    /// polyline (the accumulated top of the layers below) and the data row behind each point.
    /// <see cref="RenderStacked"/> draws this and <see cref="HitTestStacked"/> measures against it,
    /// so the hover position cannot drift away from the band that is on screen.
    /// </summary>
    private sealed class StackedBand
    {
        /// <summary>Series key as resolved from the colour channel (the grouping key).</summary>
        public object GroupKey = "__default__";

        /// <summary><see cref="GroupKey"/> as a string: colour lookup and hidden-series check.</summary>
        public string Key = "";

        /// <summary>Screen-space top polyline of the band (the series' accumulated values).</summary>
        public readonly List<(float x, float y)> Top = [];

        /// <summary>Screen-space bottom polyline of the band (the top of the layer below).</summary>
        public readonly List<(float x, float y)> Bottom = [];

        /// <summary>
        /// Data row behind the point at the same index, or null when that X carries no row of this
        /// series (or its index in <c>ctx.Data</c> is unknown).
        /// </summary>
        public readonly List<(DataRow Row, int Index)?> Rows = [];
    }

    /// <summary>
    /// Lay out every stacked band of the context in draw order (the first series sits on the bottom
    /// baseline). <see cref="RenderStacked"/> draws this layout and <see cref="HitTestStacked"/>
    /// queries it: hit testing used to map a raw value through the value scale on its own while the
    /// band was drawn from the accumulated baseline of the layers below it, so hovering a stacked
    /// line answered with a row from a different height.
    /// </summary>
    /// <param name="ctx">Context to lay out.</param>
    /// <param name="anim">Entry animation progress applied to the band height.</param>
    private List<StackedBand> ComputeStackedBands(MarkContext ctx, float anim)
    {
        var xScale = ctx.Scales.TryGet(Channel.X);
        var yScale = ctx.Scales.TryGet(YChannel);
        var bands = _stackedBands;
        bands.Clear();
        if (xScale is null || yScale is null) return bands;

        // Collect all unique X values in data order (reused buffers)
        var allXValues = _stackedXValues;
        var xSeen      = _stackedXSeen;
        allXValues.Clear();
        xSeen.Clear();
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
            categoryTotals = _stackedCategoryTotals;
            categoryTotals.Clear();
            foreach (var row in ctx.Data)
            {
                // Hidden series are not drawn, so they must not count towards the total either: with
                // them in it, hiding one series left the visible bands short of the full 1.0.
                if (IsSeriesHidden(ctx, row)) continue;
                var xKey = ctx.Encodes.Resolve(Channel.X, row)?.ToString() ?? "";
                var yResolved = ctx.Encodes.Resolve(YChannel, row);
                if (yResolved == null) continue;
                float y = ToSingle(yResolved, "Y");
                if (!float.IsFinite(y)) continue;
                categoryTotals[xKey] = categoryTotals.GetValueOrDefault(xKey) + y;
            }
        }

        // Baselines per X category (in data space)
        var baselines = _stackedBaselines;
        baselines.Clear();

        var rowIndexMap = GetRowIndexMap(ctx);

        foreach (var (seriesKey, rows) in CachedGroupByChannel(ctx, Channel.Color))
        {
            string key = seriesKey.ToString() ?? "";
            // Skip hidden series: they are neither drawn nor stacked onto, so the bands above them
            // stay where the render puts them.
            if (ctx.HiddenSeries != null && ctx.HiddenSeries.Contains(key))
                continue;

            var band = GetStackedBand(key);
            band.GroupKey = seriesKey;
            band.Key      = key;
            band.Top.Clear();
            band.Bottom.Clear();
            band.Rows.Clear();

            // Build the X→value and X→row maps of this series (reused buffers)
            var xToY   = _stackedXToY;
            var xToRow = _stackedXToRow;
            xToY.Clear();
            xToRow.Clear();
            foreach (var row in rows)
            {
                var xKey = ctx.Encodes.Resolve(Channel.X, row)?.ToString() ?? "";
                var yResolved = ctx.Encodes.Resolve(YChannel, row);
                if (yResolved == null) continue;
                double yVal = ToDouble(yResolved, "Y");
                // A value that is not a number (or NaN/Infinity) has no position: skip the row instead of
                // accumulating it into the band. ToDouble reports it as NaN rather than throwing, so the
                // guard has to be here - an unguarded NaN turned the whole band's geometry non-finite.
                if (!double.IsFinite(yVal)) continue;
                if (Stack == StackMode.Normalize)
                {
                    // A category whose visible total is zero (or negative) has no share to draw: keeping
                    // the raw value here painted it against the fixed [0, 1] axis of normalize mode, so
                    // the band ran far outside the plot.
                    float total = categoryTotals!.GetValueOrDefault(xKey, 0f);
                    if (total > 0)
                    {
                        yVal /= total;
                    }
                    else
                    {
                        WarnNormalizeZeroTotal();
                        yVal = 0;
                    }
                }
                xToY[xKey] = yVal;
                // A row whose index cannot be resolved is left out of the hit map instead of being
                // reported with an index the caller could not use.
                xToRow[xKey] = rowIndexMap.TryGetValue(row, out int rowIndex)
                    ? (row, rowIndex) : null;
            }

            // Build screen-space top & bottom point arrays (reused buffers)
            foreach (var xVal in allXValues)
            {
                string xKey  = xVal.ToString() ?? "";
                double baseline = baselines.GetValueOrDefault(xKey, 0);
                double yVal  = xToY.GetValueOrDefault(xKey, 0);
                double top   = baseline + yVal;

                // The same guard the rest of the file uses: on a numeric X axis a value that is not a number
                // (text in a numeric column) has no position - the raw Map used to throw and take the whole
                // mark with it, and the NaN it can answer with would leave the band claiming a coordinate that
                // does not exist. The row is skipped, and the stacking baseline stays where it was for it.
                double xNorm = MapSafely(xScale, xVal);
                if (!double.IsFinite(xNorm)) continue;

                float sx  = ctx.Plot.MapX((float)xNorm);
                float sBase = ctx.Plot.MapY(yScale.Map(baseline));
                float sTop  = ctx.Plot.MapY(yScale.Map(top));
                sTop = sBase + (sTop - sBase) * anim;

                band.Top.Add((sx, sTop));
                band.Bottom.Add((sx, sBase));
                band.Rows.Add(xToRow.GetValueOrDefault(xKey));

                baselines[xKey] = top;
            }

            bands.Add(band);
        }

        return bands;
    }

    /// <summary>
    /// The band object of a series, reused across frames instead of allocating one per layout.
    /// </summary>
    private StackedBand GetStackedBand(string key)
    {
        if (!_stackedBandCache.TryGetValue(key, out var band))
        {
            band = new StackedBand();
            _stackedBandCache[key] = band;
        }
        return band;
    }

    private void RenderStacked(MarkContext ctx)
    {
        var bands = ComputeStackedBands(ctx, ComputeAnimProgress(ctx));
        if (bands.Count == 0) return;

        float globalOpacity = ctx.Animation.GlobalOpacity;

        foreach (var band in bands)
        {
            if (band.Top.Count < 2) continue;

            var color = ResolveSeriesColor(ctx, band.GroupKey);
            float seriesOpacity = ComputeSeriesOpacity(ctx, band.Key, globalOpacity);

            // Stacked area fill: top line → bottom line reversed → close
            var areaPath = ShapePath(ctx);
            var areaPaint = ShapePaint(ctx);
            BuildLinePath(band.Top, areaPath);

            // The lower edge goes through the same builder as the upper one (only without its
            // initial MoveTo), so a stepped or smoothed band keeps one interpolation on both sides
            // and the fill can no longer leave a seam against the layer below.
            var bottomReverse = _stackedBottomReverse;
            bottomReverse.Clear();
            for (int i = band.Bottom.Count - 1; i >= 0; i--)
                bottomReverse.Add(band.Bottom[i]);
            AppendLinePath(bottomReverse, areaPath);

            areaPath.Close();
            areaPaint.SetColor(color).SetOpacity(AreaOpacity * seriesOpacity).SetAntiAlias(true);
            ctx.Canvas.Fill(areaPath, areaPaint);

            // Top line stroke
            var linePath = ShapePath(ctx);
            var linePaint = ShapePaint(ctx);
            BuildLinePath(band.Top, linePath);
            linePaint.SetColor(color).SetStrokeWidth(StrokeWidth)
                     .SetLineCap(LineCap.Round).SetLineJoin(LineJoin.Round)
                     .SetOpacity(seriesOpacity);
            ctx.Canvas.Stroke(linePath, linePaint);
        }
    }

    /// <inheritdoc />
    public override HitResult? HitTest(MarkContext ctx, Vector2 screenPos)
    {
        // A stacked band is not where its own value maps on the value scale, so it needs the
        // accumulated layout instead of the per-row mapping below.
        if (Stack != StackMode.None) return HitTestStacked(ctx, screenPos);

        var xScale = ctx.Scales.TryGet(Channel.X);
        var yScale = ctx.Scales.TryGet(YChannel);
        if (xScale is null || yScale is null) return null;

        float threshold = (ctx.Theme ?? ChartTheme.Default).HitTestSnapDistance;
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
            float px = ctx.Plot.MapX((float)MapSafely(xScale, xRaw));
            float py = ctx.Plot.MapY((float)MapSafely(yScale, yRaw));
            float d  = screenPos.DistanceTo(new Vector2(px, py));
            if (d < bestDist)
            {
                bestDist = d; bestRow = row; bestIdx = i;
                bestX = px; bestY = py;
            }
        }

        return bestRow == null ? null : BuildHitResult(ctx, bestRow, bestIdx, bestX, bestY);
    }

    /// <summary>
    /// Hit test for <see cref="StackMode.Stack"/> / <see cref="StackMode.Normalize"/>: a row is
    /// measured at the top polyline its series was actually drawn along, so hovering a stacked line
    /// answers with the row under the pointer instead of the one the raw value maps to. The entry
    /// animation applies here too, exactly as it does while drawing.
    /// </summary>
    private HitResult? HitTestStacked(MarkContext ctx, Vector2 screenPos)
    {
        float threshold = (ctx.Theme ?? ChartTheme.Default).HitTestSnapDistance;
        var bands = ComputeStackedBands(ctx, ComputeAnimProgress(ctx));

        float bestDist = threshold;
        DataRow? bestRow = null;
        int bestIdx = -1;
        float bestX = 0, bestY = 0;

        // Topmost band first: where two layers share an edge, the series drawn last is the one the
        // pointer is on.
        for (int b = bands.Count - 1; b >= 0; b--)
        {
            var band = bands[b];
            for (int i = 0; i < band.Top.Count; i++)
            {
                if (band.Rows[i] is not { } entry) continue;
                var (px, py) = band.Top[i];
                float d = screenPos.DistanceTo(new Vector2(px, py));
                if (d < bestDist)
                {
                    bestDist = d; bestRow = entry.Row; bestIdx = entry.Index;
                    bestX = px; bestY = py;
                }
            }
        }

        return bestRow == null ? null : BuildHitResult(ctx, bestRow, bestIdx, bestX, bestY);
    }

    /// <summary>Hit result describing one data row, shared by the plain and the stacked path.</summary>
    private HitResult BuildHitResult(MarkContext ctx, DataRow row, int index, float x, float y)
    {
        var xVal = ctx.Encodes.Resolve(Channel.X, row);
        var yVal = ctx.Encodes.Resolve(YChannel, row);
        return new HitResult
        {
            Hit = true, Row = row, RowIndex = index,
            ScreenX = x, ScreenY = y,
            Label = $"{xVal}: {yVal}",
            SeriesKey = ResolveSeriesKey(ctx, row),
            MarkType = nameof(LineMark),
        };
    }

    private static void BuildPath(
        List<(float x, float y)> pts, IPath2D path, bool smooth, bool connect = false)
    {
        if (connect) path.LineTo(pts[0].x, pts[0].y);
        else path.MoveTo(pts[0].x, pts[0].y);
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
    /// Continue an already started path through <paramref name="pts"/> with the interpolation
    /// <see cref="BuildLinePath"/> uses. Only the first point differs: it is joined with a line
    /// instead of starting a new subpath, which is what welding the lower edge of a stacked band to
    /// the upper one needs.
    /// </summary>
    private void AppendLinePath(List<(float x, float y)> pts, IPath2D path)
    {
        if (Step != StepMode.None)
            BuildStepPath(pts, path, Step, connect: true);
        else
            BuildPath(pts, path, Smooth, connect: true);
    }

    /// <summary>
    /// Build a step path through the given points with the specified step mode.
    /// </summary>
    /// <param name="pts">Points to walk, in order.</param>
    /// <param name="path">Path to build on.</param>
    /// <param name="mode">Step mode of the chart.</param>
    /// <param name="connect">Join the first point with a line instead of moving to it.</param>
    private static void BuildStepPath(
        List<(float x, float y)> pts, IPath2D path, StepMode mode, bool connect = false)
    {
        if (connect) path.LineTo(pts[0].x, pts[0].y);
        else path.MoveTo(pts[0].x, pts[0].y);
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
