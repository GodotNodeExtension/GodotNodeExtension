namespace GodotNodeExtension.Component.GodotChart;

using System;
using System.Collections.Generic;
using Godot;
using Canvas;

/// <summary>
/// Fills geographic regions with a colour: a value per province, district, cell or zone - the most common
/// map chart there is.
/// <para>
/// One mark covers the two roles the two-stage picture is made of. With a colour channel bound it is the
/// data layer: every feature is filled with the colour of the row that joins to it, and the features whose
/// row is missing are filled with <see cref="NoDataColor"/> so "no data" is visible instead of implied.
/// With no colour channel it is the geometry layer: outlines only, no fill, no legend entry - the base map a
/// bubble or a flow layer is drawn on.
/// </para>
/// <para>
/// Polygons with holes are filled correctly whatever winding the source used: the projected outer ring is
/// wound one way and its holes the other, which is what carves them out (the fill rule is non-zero, and the
/// canvas has no per-fill rule to set). Which ring is a hole is the geometry's answer
/// (<see cref="GeoRing.IsHole"/>), and how a row finds its feature is <see cref="RowField"/> against
/// <see cref="FeatureKey"/> - the same join the rest of the library uses, see <see cref="GeoDataJoiner"/>.
/// </para>
/// </summary>
public sealed class GeoAreaMark : GeoMark
{
    private ProjectionCache<RegionLayer> _projection;
    private int _configHash;
    private bool _joinReported;

    /// <summary>
    /// The regions to draw, in paint order (the last one is on top and is hit-tested first). Empty until
    /// the caller hands some over - nothing is read from the scene, because a map is data.
    /// </summary>
    public IReadOnlyList<GeoFeature> Features { get; set; } = [];

    /// <summary>
    /// Field of the data row that holds the join key, matched against <see cref="FeatureKey"/> of each
    /// feature. The default pairs a table's <c>name</c> column with the feature's name (which falls back to
    /// its identifier).
    /// </summary>
    public string RowField { get; set; } = "name";

    /// <summary>
    /// Key read from each feature: <c>name</c> (its fallback is the identifier), <c>id</c>, or the name of
    /// any property the source carries.
    /// </summary>
    public string FeatureKey { get; set; } = "name";

    /// <summary>
    /// How two keys are compared, or null for an exact comparison. <see cref="GeoDataJoiner.LooseKeys"/>
    /// ignores case and surrounding whitespace, which is what a table out of a spreadsheet needs.
    /// </summary>
    public IEqualityComparer<string>? KeyComparer { get; set; }

    /// <summary>What a region whose row is missing is filled with, or drawn with in outline mode.</summary>
    public GeoJoinMissing Missing { get; set; } = GeoJoinMissing.Report;

    /// <summary>Fill of a region no row joined to; the visible half of "this region has no data".</summary>
    public Color NoDataColor { get; set; } = new(0.45f, 0.47f, 0.5f, 0.35f);

    /// <summary>Outline drawn around each region in geometry-layer mode (no colour channel bound).</summary>
    public Color OutlineColor { get; set; } = new(0.55f, 0.58f, 0.62f);

    /// <summary>
    /// Whether regions are filled with their row's colour (true, the default) or drawn as outlines only.
    /// The second setting is the geometry layer of a two-stage map - a base map under a bubble or flow layer -
    /// and it wins over a bound colour channel, so a page can draw the same map twice without wiring a second
    /// chart differently.
    /// </summary>
    public bool Shade { get; set; } = true;

    /// <summary>
    /// Border width of a region. In data mode the border is a darkened version of the fill, so it reads on
    /// any palette; 0 draws no border at all.
    /// </summary>
    public float StrokeWidth { get; set; } = 1f;

    /// <summary>
    /// A geographic mark with its own geometry stops the layer cache from being used, because the hover look
    /// (a brightened region) is painted in the data layer rather than in a second pass; a chart that turns the
    /// cache on gets the single pass and one warning instead of a frozen highlight.
    /// </summary>
    public override bool InteractionStateInOverlay => false;

    /// <inheritdoc />
    public override GeoGeometrySource Source => GeoGeometrySource.Feature;

    /// <inheritdoc />
    public override void Render(MarkContext ctx)
    {
        var layer = Projected(ctx);
        if (layer.Regions.Count == 0) return;

        float animation = ComputeAnimProgress(ctx);
        var path = ShapePath(ctx);
        var paint = ShapePaint(ctx);

        foreach (var region in layer.Regions)
        {
            float opacity = region.Row is { } row
                ? ComputeElementOpacity(ctx, row, region.RowIndex)
                : ctx.Animation.GlobalOpacity;
            opacity = Mathf.Clamp(opacity * animation, 0f, 1f);
            if (opacity <= 0f) continue;

            BuildPath(path, region);
            if (region.Fill is { } fill)
            {
                paint.SetColor(fill).SetAntiAlias(true).SetOpacity(opacity);
                ctx.Canvas.Fill(path, paint);
            }
            if (StrokeWidth > 0f)
            {
                var stroke = region.Stroke;
                paint.SetColor(stroke with { A = stroke.A * opacity })
                     .SetStrokeWidth(StrokeWidth)
                     .SetAntiAlias(true)
                     .SetLineJoin(LineJoin.Round);
                ctx.Canvas.Stroke(path, paint);
            }
        }
    }

    /// <inheritdoc />
    public override HitResult? HitTest(MarkContext ctx, Vector2 screenPos)
    {
        var layer = Projected(ctx);
        var probe = new GeoPoint(screenPos.X, screenPos.Y);

        // Back to front: the region painted last is the one the pointer is over.
        for (int i = layer.Regions.Count - 1; i >= 0; i--)
        {
            var region = layer.Regions[i];
            if (!Contains(region, probe)) continue;

            return new HitResult
            {
                Hit = true,
                Row = region.Row,
                RowIndex = region.RowIndex,
                ScreenX = (float)region.Anchor.X,
                ScreenY = (float)region.Anchor.Y,
                Label = region.Label,
                MarkType = nameof(GeoAreaMark),
            };
        }
        return null;
    }

    // ── Projection ─────────────────────────────────────────────

    /// <summary>
    /// The screen geometry of every region, built once per layout - and once more whenever a property that
    /// changes the picture changes, which the layout version alone cannot see (the features and the join keys
    /// are the mark's own).
    /// </summary>
    private RegionLayer Projected(MarkContext ctx)
    {
        int config = HashCode.Combine(
            HashCode.Combine(Features, RowField, FeatureKey, KeyComparer),
            HashCode.Combine(Missing, NoDataColor, OutlineColor, StrokeWidth, Shade));
        if (config != _configHash)
        {
            _projection = default;
            _configHash = config;
        }
        return GetProjection(ctx, ref _projection, Build);
    }

    private RegionLayer Build(MarkContext ctx)
    {
        if (ctx.GeoViewport is not { } viewport || Features.Count == 0)
            return new RegionLayer([]);

        bool shades = Shade && HasEncode(ctx, Channel.Color);
        var join = new GeoDataJoiner(RowField, FeatureKey, GeoJoinMissing.Silent, KeyComparer)
            .Join(ctx.Data, Features);
        ReportJoin(join);

        // Where each joined row sits in the table: what a hit result reports as RowIndex, and what the
        // interaction state is read from. Built once per layout instead of scanning the table per region.
        var positions = new Dictionary<DataRow, int>(ctx.Data.Count);
        for (int i = 0; i < ctx.Data.Count; i++) positions.TryAdd(ctx.Data[i], i);

        var regions = new List<Region>(Features.Count);
        for (int i = 0; i < Features.Count; i++)
        {
            var feature = Features[i];
            var geometry = feature.Geometry;
            if (geometry.IsEmpty) continue;

            var rings = new List<GeoRing>(geometry.Parts.Count);
            foreach (var part in geometry.Parts)
            {
                if (part.Points.Count < 3) continue; // a point or a line says nothing about an area
                rings.Add(ProjectRing(part, viewport, ctx.Plot));
            }
            if (rings.Count == 0) continue;

            var row = i < join.RowOfElement.Count ? join.RowOfElement[i] : null;
            int rowIndex = row is not null && positions.TryGetValue(row, out int at) ? at : -1;
            Color? fill = null;
            Color stroke = OutlineColor;
            if (shades)
            {
                fill = row is not null ? ResolveFill(ctx, row, rowIndex, NoDataColor) : NoDataColor;
                stroke = BrightenColor(fill.Value, -0.25f);
            }

            regions.Add(new Region(row, rowIndex, fill, stroke, rings,
                                   CentreOf(rings[0]), LabelOf(ctx, feature, row)));
        }

        return new RegionLayer(regions);
    }

    /// <summary>Project one ring and wind it for the fill rule: outer rings one way, holes the other.</summary>
    private static GeoRing ProjectRing(GeoRing part, GeoViewport viewport, in PlotArea plot)
    {
        var points = new List<GeoPoint>(part.Points.Count);
        foreach (var point in part.Points)
        {
            var screen = viewport.Project(point.X, point.Y, plot);
            points.Add(new GeoPoint(screen.X, screen.Y));
        }

        // The projection flips Y, so the source winding is not the screen winding - which is the one that
        // decides whether a ring carves a hole or fills it.
        bool clockwise = GeoMath.RingIsClockwise(points);
        if (clockwise == part.IsHole) points.Reverse();
        return new GeoRing(points, part.IsHole);
    }

    /// <summary>Build the path of one region: every ring in one path, so the holes are carved out of it.</summary>
    private static void BuildPath(IPath2D path, Region region)
    {
        foreach (var ring in region.Rings)
        {
            var points = ring.Points;
            path.MoveTo((float)points[0].X, (float)points[0].Y);
            for (int i = 1; i < points.Count; i++)
                path.LineTo((float)points[i].X, (float)points[i].Y);
            path.Close();
        }
    }

    /// <summary>Whether a screen point is inside a region: inside its outer ring and outside every hole.</summary>
    private static bool Contains(Region region, GeoPoint point)
    {
        bool inside = false;
        foreach (var ring in region.Rings)
        {
            if (!GeoMath.PointInPolygon(point, ring.Points)) continue;
            if (ring.IsHole) return false;
            inside = true;
        }
        return inside;
    }

    /// <summary>Midpoint of the outer ring's box: the element centre a tooltip and a label anchor to.</summary>
    private static GeoPoint CentreOf(GeoRing ring)
    {
        double minX = ring.Points[0].X, maxX = minX, minY = ring.Points[0].Y, maxY = minY;
        foreach (var point in ring.Points)
        {
            minX = Math.Min(minX, point.X);
            maxX = Math.Max(maxX, point.X);
            minY = Math.Min(minY, point.Y);
            maxY = Math.Max(maxY, point.Y);
        }
        return new GeoPoint((minX + maxX) / 2.0, (minY + maxY) / 2.0);
    }

    /// <summary>The label of a region: its name, and the value of the row that joined to it.</summary>
    private string? LabelOf(MarkContext ctx, GeoFeature feature, DataRow? row)
    {
        string? name = feature.Name ?? feature.Id;
        object? value = row is null ? null : ResolveEncode(ctx, YChannel, row);
        if (name is null) return value?.ToString();
        return value is null ? name : $"{name}: {value}";
    }

    /// <summary>
    /// Report a join that left rows or regions behind, once per mark: the geometry is rebuilt on every layout
    /// change (a pan changes it on every frame), and a warning repeated per frame is console noise.
    /// </summary>
    private void ReportJoin(GeoJoinResult join)
    {
        if (_joinReported || Missing != GeoJoinMissing.Report) return;
        if (join.MissingElementCount == 0 && join.UnmatchedRows.Count == 0) return;

        _joinReported = true;
        GD.PushWarning(
            $"[GodotChart] {nameof(GeoAreaMark)}: {join.UnmatchedRows.Count} row(s) matched no feature and " +
            $"{join.MissingElementCount} of {join.ElementCount} region(s) got no row; those are drawn in the " +
            "no-data colour.");
    }

    /// <summary>One projected region: what to fill, what to stroke, and where its centre is.</summary>
    private sealed record Region(DataRow? Row, int RowIndex, Color? Fill, Color Stroke,
                                 IReadOnlyList<GeoRing> Rings, GeoPoint Anchor, string? Label);

    /// <summary>Every projected region of one layout.</summary>
    private sealed record RegionLayer(IReadOnlyList<Region> Regions);
}
