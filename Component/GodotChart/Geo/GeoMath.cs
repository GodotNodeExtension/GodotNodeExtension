namespace GodotNodeExtension.Component.GodotChart;

using System;
using System.Collections.Generic;

/// <summary>
/// An axis-aligned rectangle in frame coordinates (longitude/latitude for a WGS84 frame, world units
/// for a plane frame). Deliberately not a <c>Rect2</c>: the numbers a map works with are degrees and
/// world units, and single precision loses a meridian's worth of detail at high zoom.
/// </summary>
/// <param name="MinX">Left edge.</param>
/// <param name="MinY">Bottom edge.</param>
/// <param name="MaxX">Right edge.</param>
/// <param name="MaxY">Top edge.</param>
public readonly record struct GeoBounds(double MinX, double MinY, double MaxX, double MaxY)
{
    /// <summary>Horizontal extent; negative for an inverted rectangle.</summary>
    public double Width => MaxX - MinX;

    /// <summary>Vertical extent; negative for an inverted rectangle.</summary>
    public double Height => MaxY - MinY;

    /// <summary>Whether the rectangle has no interior to fit a viewport to.</summary>
    public bool IsEmpty => !(Width > 0) || !(Height > 0);

    /// <summary>Horizontal midpoint.</summary>
    public double CenterX => (MinX + MaxX) / 2.0;

    /// <summary>Vertical midpoint.</summary>
    public double CenterY => (MinY + MaxY) / 2.0;

    /// <summary>
    /// Build a rectangle from two opposite corners in any order, so a caller may pass its corners as
    /// it happens to have them.
    /// </summary>
    /// <param name="x0">First corner, horizontal coordinate.</param>
    /// <param name="y0">First corner, vertical coordinate.</param>
    /// <param name="x1">Opposite corner, horizontal coordinate.</param>
    /// <param name="y1">Opposite corner, vertical coordinate.</param>
    public static GeoBounds FromCorners(double x0, double y0, double x1, double y1)
        => new(Math.Min(x0, x1), Math.Min(y0, y1), Math.Max(x0, x1), Math.Max(y0, y1));
}

/// <summary>
/// The arithmetic a geographic chart needs besides a frame and a viewport: how a zoom level relates to
/// a resolution, and the constants behind it. Every member is a pure function of its arguments, so the
/// projection side of a chart can be tested without a canvas or a device.
/// </summary>
public static class GeoMath
{
    /// <summary>
    /// Pixels a frame's whole world spans at zoom 0 - the 256 px tile the mainstream map schemes are
    /// defined on. The zoom level of a <see cref="GeoViewport"/> doubles this with every step.
    /// </summary>
    public const double WorldSizeAtZoomZero = 256.0;

    /// <summary>
    /// Length of the equator in metres, the scale Web Mercator is defined on. It is what turns the
    /// tile-based zoom of a map service into metres per pixel at the equator:
    /// <c>EarthCircumference / (256 * 2^zoom)</c>, i.e. 156543.03 m/px at zoom 0.
    /// </summary>
    public const double EarthCircumference = 40075016.685578488;

    /// <summary>
    /// Metres one degree of longitude covers at the equator: the conversion between the degrees a
    /// sphere frame is measured in and the metres a scale bar is labelled in.
    /// </summary>
    public const double MercatorMetersPerDegree = EarthCircumference / 360.0;

    /// <summary>
    /// Lowest zoom level a viewport clamps to. It exists to keep every projection finite: the range is
    /// far wider than any real map view, and a zoom beyond it would overflow the world's pixel size.
    /// </summary>
    public const double MinZoomLevel = -64.0;

    /// <summary>Highest zoom level a viewport clamps to; see <see cref="MinZoomLevel"/>.</summary>
    public const double MaxZoomLevel = 64.0;

    /// <summary>
    /// Resolution of a zoom level: how many of the frame's own units one pixel covers (degrees of
    /// longitude per pixel for a WGS84 frame, world units per pixel for a plane frame). Multiply by
    /// <see cref="MercatorMetersPerDegree"/> to get metres per pixel at the equator.
    /// </summary>
    /// <param name="zoom">Zoom level; clamped to the supported range.</param>
    /// <param name="frame">Frame whose world the resolution is measured in.</param>
    /// <returns>
    /// Units per pixel, or 0 for a frame whose world has no width (a degenerate plane range), which
    /// cannot be given a resolution.
    /// </returns>
    public static double ZoomToResolution(double zoom, IGeoFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        double worldPixels = WorldSizeAtZoomZero * Math.Pow(2.0, ClampZoom(zoom));
        return frame.WorldWidth > 0 ? frame.WorldWidth / worldPixels : 0.0;
    }

    /// <summary>
    /// The inverse of <see cref="ZoomToResolution"/>: the zoom level that gives this resolution.
    /// </summary>
    /// <param name="resolution">Frame units per pixel.</param>
    /// <param name="frame">Frame the resolution was measured in.</param>
    /// <returns>
    /// A zoom level inside the supported range: a resolution of zero or less is "infinitely far in"
    /// and clamps to <see cref="MaxZoomLevel"/>, an enormous one clamps to <see cref="MinZoomLevel"/>.
    /// </returns>
    public static double ResolutionToZoom(double resolution, IGeoFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (!(resolution > 0) || !(frame.WorldWidth > 0)) return ClampZoom(double.PositiveInfinity);
        return ClampZoom(Math.Log2(frame.WorldWidth / (WorldSizeAtZoomZero * resolution)));
    }

    /// <summary>
    /// Clamp a zoom level to the supported range, with a NaN read as "no zoom": a viewport built from
    /// a calculation that went wrong still projects a finite picture.
    /// </summary>
    /// <param name="zoom">Zoom level as computed.</param>
    public static double ClampZoom(double zoom)
        => double.IsNaN(zoom) ? 0.0 : Math.Clamp(zoom, MinZoomLevel, MaxZoomLevel);

    /// <summary>
    /// Whether a point is inside a ring, by the even-odd rule (a ray is cast to the right and its crossings
    /// are counted). This is what a geographic mark hit-tests a region with: the projected ring, once, and
    /// then one call per pointer position.
    /// </summary>
    /// <param name="point">Point to test, in the ring's own coordinates.</param>
    /// <param name="ring">The ring; a closed ring (last point repeated) is fine, as is an open one.</param>
    /// <returns>
    /// True when the point is inside. A point exactly on the boundary may be reported either way, which is
    /// what a mark that wants a tolerant hit test adds its own margin for.
    /// </returns>
    public static bool PointInPolygon(GeoPoint point, IReadOnlyList<GeoPoint> ring)
    {
        ArgumentNullException.ThrowIfNull(ring);
        if (ring.Count < 3) return false;

        bool inside = false;
        for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
        {
            GeoPoint a = ring[i];
            GeoPoint b = ring[j];
            // Count the edges the horizontal ray from the point towards +X crosses. The comparison is
            // written so a vertex exactly at the ray's height counts once, not twice.
            if (a.Y > point.Y != b.Y > point.Y &&
                point.X < (b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y) + a.X)
            {
                inside = !inside;
            }
        }
        return inside;
    }

    /// <summary>
    /// Whether a ring winds clockwise, by the sign of its signed area (the shoelace formula).
    /// <para>
    /// Frames are y-up, so a clockwise ring has a negative area. Real map data is not consistent about it
    /// - the GeoJSON specification asks for counterclockwise outer rings and field data ignores that
    /// regularly - which is why a mark that fills polygons reads this instead of trusting the file, and why
    /// the geometry builder documents that a hole with the wrong winding is corrected rather than filled.
    /// </para>
    /// </summary>
    /// <param name="ring">The ring; a closed ring (last point repeated) is fine.</param>
    /// <returns>True for a clockwise ring; false for a counterclockwise or degenerate one.</returns>
    public static bool RingIsClockwise(IReadOnlyList<GeoPoint> ring)
    {
        ArgumentNullException.ThrowIfNull(ring);
        if (ring.Count < 3) return false;

        double twiceArea = 0;
        for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
            twiceArea += (ring[j].X * ring[i].Y) - (ring[i].X * ring[j].Y);

        return twiceArea < 0;
    }

    /// <summary>
    /// Distance from a point to a line segment - the measurement a mark hit-testing a line (a route, a flow,
    /// a metro edge) compares against its own tolerance.
    /// </summary>
    /// <param name="point">The point.</param>
    /// <param name="a">Start of the segment.</param>
    /// <param name="b">End of the segment; equal to <paramref name="a"/> is a degenerate segment and the
    /// distance to that point is returned.</param>
    public static double DistanceToSegment(GeoPoint point, GeoPoint a, GeoPoint b)
    {
        double dx = b.X - a.X;
        double dy = b.Y - a.Y;
        double lengthSquared = (dx * dx) + (dy * dy);
        if (!(lengthSquared > 0)) return Distance(point, a);

        double t = (((point.X - a.X) * dx) + ((point.Y - a.Y) * dy)) / lengthSquared;
        t = Math.Clamp(t, 0.0, 1.0);
        return Distance(point, new GeoPoint(a.X + (t * dx), a.Y + (t * dy)));
    }

    /// <summary>
    /// Bounding rectangle of a set of features, in frame coordinates: the union of the geometries, or null
    /// when nothing has a coordinate. What a chart fits its viewport to when the data itself should decide
    /// how far the map is zoomed out.
    /// </summary>
    /// <param name="features">The features to measure.</param>
    public static GeoBounds? BoundsOf(IEnumerable<GeoFeature> features)
    {
        ArgumentNullException.ThrowIfNull(features);

        GeoBounds? bounds = null;
        foreach (var feature in features)
        {
            if (feature.Geometry.Bounds is not { } geometry) continue;
            bounds = bounds is { } current
                ? new GeoBounds(Math.Min(current.MinX, geometry.MinX), Math.Min(current.MinY, geometry.MinY),
                                Math.Max(current.MaxX, geometry.MaxX), Math.Max(current.MaxY, geometry.MaxY))
                : geometry;
        }
        return bounds;
    }

    private static double Distance(GeoPoint a, GeoPoint b)
    {
        double dx = a.X - b.X;
        double dy = a.Y - b.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }
}
