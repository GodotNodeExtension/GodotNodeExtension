namespace GodotNodeExtension.Component.GodotChart;

using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// What geometry a geographic mark consumes. It is the mark's side of a contract with the chart: a chart
/// that carries only a graph has no continuous space to interpolate a field over, and
/// <see cref="GeoMark.RequiresContinuousSpace"/> is how a mark says it would need one.
/// </summary>
public enum GeoGeometrySource
{
    /// <summary>Features: polygons, lines and points (see <see cref="GeoJsonReader"/> and <see cref="GeoGeometryBuilder"/>).</summary>
    Feature,

    /// <summary>The nodes and edges of a <see cref="GeoGraph"/> - a diagram, not a shape.</summary>
    Graph,

    /// <summary>Points from the data rows themselves: a scatter, a bubble, a binned density.</summary>
    Points,
}

/// <summary>
/// Base class for the marks that place their data in a coordinate frame instead of on a pair of scales.
/// <para>
/// Four things come from here: the coordinate system (the chart draws no Cartesian axes for these marks),
/// the geometry source a chart validates combinations against, the projection cache a mark builds its
/// screen geometry in, and the frame the mark's own coordinates are expressed in (through
/// <see cref="MarkContext.GeoViewport"/>).
/// </para>
/// <para>
/// The cache is the important part. A geographic projection is expensive enough to want caching and
/// interactive enough to go stale immediately: every pan and zoom moves the viewport, the chart drops its
/// layout version for that, and the key <see cref="Mark.CacheKey"/> compares is what makes the cached
/// screen geometry follow the map instead of freezing on the frame it was built in.
/// </para>
/// </summary>
public abstract class GeoMark : Mark
{
    /// <summary>
    /// A geographic mark is always drawn against a frame, never against the chart's axes, and a chart whose
    /// marks all answer this way draws no grid, no axes and no crosshair.
    /// </summary>
    public sealed override MarkCoordinate Coordinate => MarkCoordinate.Geographic;

    /// <inheritdoc />
    public sealed override bool UsesAxes => false;

    /// <summary>
    /// The geometry this mark consumes. The chart uses it to report a combination whose parts do not
    /// belong together (a field over a topological graph, for instance).
    /// </summary>
    public abstract GeoGeometrySource Source { get; }

    /// <summary>
    /// Whether this mark needs a space it can interpolate over. False for a mark that places discrete
    /// elements (a region, a node, an edge); true for one that reads a scalar field, which only means
    /// something where "between two positions" means something - not on a graph.
    /// </summary>
    public virtual bool RequiresContinuousSpace => false;

    /// <summary>
    /// One cached projection of a mark: the layout key it was built for and the value itself.
    /// <para>
    /// Declare one field per projection a mark builds (<c>private ProjectionCache&lt;List&lt;Vector2&gt;&gt; _points;</c>)
    /// and read it through <see cref="GetProjection{T}"/>; the default value simply means "nothing cached
    /// yet", so a mark needs no constructor and no reset call.
    /// </para>
    /// </summary>
    /// <typeparam name="T">What is cached: a screen geometry, a set of points, a binned grid.</typeparam>
    /// <param name="Key">Layout key the value was built for.</param>
    /// <param name="Value">The value, or null while nothing is cached.</param>
    protected readonly record struct ProjectionCache<T>(LayoutCacheKey Key, T? Value) where T : class;

    /// <summary>
    /// The projection for this frame, rebuilt when the layout changed and reused otherwise - the pattern
    /// the whole design rests on, because a mark projects thousands of coordinates per frame and a pan
    /// changes the projection of every one of them.
    /// </summary>
    /// <typeparam name="T">What is cached.</typeparam>
    /// <param name="ctx">The context of this frame.</param>
    /// <param name="cache">The mark's cache slot for this projection (passed by reference).</param>
    /// <param name="build">Builds the value; must return a value (a mark's projection always exists).</param>
    /// <returns>The cached or freshly built value.</returns>
    protected static T GetProjection<T>(MarkContext ctx, ref ProjectionCache<T> cache, Func<MarkContext, T> build)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(build);

        LayoutCacheKey key = CacheKey(ctx);
        if (cache.Value is not null && cache.Key.Equals(key)) return cache.Value;

        T value = build(ctx);
        cache = new ProjectionCache<T>(key, value);
        return value;
    }
}

/// <summary>
/// The geographic half of the chart's mark compatibility pass: a mark that needs a continuous space next
/// to a mark whose geometry is a topological graph is a combination that renders something meaningless
/// rather than something wrong, which is exactly the kind of mistake a picture does not show. The chart
/// asks once per chart (not once per frame: the pass runs again on every layout change, and a pan changes
/// the layout on every frame).
/// </summary>
internal static class GeoSourceCheck
{
    /// <summary>Report the geographic incompatibilities of a mark list; true when it warned.</summary>
    /// <param name="marks">The chart's marks.</param>
    internal static bool Report(IReadOnlyList<Mark> marks)
    {
        ArgumentNullException.ThrowIfNull(marks);

        string? continuous = null;
        bool hasGraph = false;
        foreach (var mark in marks)
        {
            if (mark is not GeoMark geo) continue;
            if (geo.Source == GeoGeometrySource.Graph) hasGraph = true;
            if (geo.RequiresContinuousSpace) continuous ??= mark.GetType().Name;
        }

        if (!hasGraph || continuous is null) return false;

        GD.PushWarning(
            $"[GodotChart] {continuous} needs a continuous space, but this chart's geometry is a " +
            "topological graph: a field over the connections between nodes is interpolated between node " +
            "positions, which is rarely what the data means. Encode the value with colour or size instead.");
        return true;
    }
}
