namespace GodotNodeExtension.Component.GodotChart;

using System;

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
}
