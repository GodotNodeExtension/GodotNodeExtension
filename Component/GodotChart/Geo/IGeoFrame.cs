namespace GodotNodeExtension.Component.GodotChart;

using System;

/// <summary>
/// A coordinate frame: the meaning of the coordinates a geographic mark is placed with, and the bridge
/// from those coordinates to the normalized [0, 1]² world a <see cref="GeoViewport"/> projects from.
/// <para>
/// "Geographic" does not have to mean longitude and latitude. A frame decides what its coordinates
/// mean, so the same mark and the same viewport serve a real map (WGS84), the map of an invented
/// planet (custom sphere) and a game world in its own units (custom plane).
/// </para>
/// <para>
/// Normalized coordinates run bottom to top on the vertical axis (north is up), so a point of a plane
/// frame whose Y grows upwards needs no special case. <see cref="Aspect"/> states how wide the frame's
/// world is relative to its height at equal physical scale, which is what makes one zoom level - and
/// therefore one pixel scale - usable on a conformal world, a degree grid and a game world alike.
/// </para>
/// <para>
/// Implementations are stateless and pure. Use <see cref="GeoFrames"/> to build one.
/// </para>
/// </summary>
public interface IGeoFrame
{
    /// <summary>Name of the frame, for diagnostics and documentation.</summary>
    string Name { get; }

    /// <summary>
    /// Width / height of the frame's world at equal physical scale. Keeps the viewport from stretching
    /// the world when the plot is not the world's shape (see <see cref="GeoViewport"/>).
    /// </summary>
    double Aspect { get; }

    /// <summary>
    /// Horizontal extent of the frame's world in the frame's own units: 360 for a WGS84 frame (degrees
    /// of longitude), <c>maxX - minX</c> for a plane frame. It turns a zoom level into a resolution
    /// (see <see cref="GeoMath.ZoomToResolution"/>). It is deliberately not paired with a vertical
    /// counterpart: a sphere frame's vertical mapping is not linear in latitude.
    /// </summary>
    double WorldWidth { get; }

    /// <summary>
    /// Whether the horizontal axis wraps around after <see cref="WorldWidth"/>: true for a sphere
    /// frame, where the meridian one world to the left is the same meridian. A plane frame's edge is
    /// an edge.
    /// </summary>
    bool WrapsX { get; }

    /// <summary>
    /// Largest absolute vertical coordinate the frame represents - the truncation latitude of a sphere
    /// frame (85.05112878 for Web Mercator, 90 for a plate carrée world) - or null for a plane frame,
    /// which has no vertical limit.
    /// </summary>
    double? MaxAbsY { get; }

    /// <summary>
    /// Map a frame coordinate to the normalized world. Horizontal results may fall outside [0, 1] (a
    /// wrapped longitude); a sphere frame clamps the vertical one to <see cref="MaxAbsY"/>.
    /// </summary>
    /// <param name="x">Horizontal frame coordinate (longitude for a WGS84 frame).</param>
    /// <param name="y">Vertical frame coordinate (latitude for a WGS84 frame).</param>
    (double U, double V) Normalize(double x, double y);

    /// <summary>The inverse of <see cref="Normalize"/>.</summary>
    /// <param name="u">Normalized horizontal position.</param>
    /// <param name="v">Normalized vertical position.</param>
    (double X, double Y) Denormalize(double u, double v);

    /// <summary>
    /// Fold a horizontal coordinate into the frame's own range (the equivalent meridian inside the
    /// world). A wrapping frame cannot be panned out of its world with this, and a plane frame is
    /// unchanged.
    /// </summary>
    /// <param name="x">Horizontal frame coordinate.</param>
    double WrapX(double x);
}

/// <summary>
/// The built-in frames: real geography, an invented sphere and an arbitrary plane.
/// </summary>
public static class GeoFrames
{
    /// <summary>
    /// The real world: longitude/latitude, projected with <see cref="GeoProjections.WebMercator"/> by
    /// default. Longitudes wrap at ±180°, latitudes are truncated at the projection's limit.
    /// </summary>
    /// <param name="projection">Projection to use; null (the default) means Web Mercator.</param>
    public static IGeoFrame Wgs84(IGeoProjection? projection = null)
    {
        var used = projection ?? GeoProjections.WebMercator;
        return new SphereFrame($"WGS84 / {used.Name}", -180.0, 180.0, used);
    }

    /// <summary>
    /// A sphere the caller defines: the given longitude/latitude ranges are the world, so a fantasy
    /// map, a planet map or a region map uses the same marks and the same viewport as a real one.
    /// </summary>
    /// <param name="minLongitude">Longitude at the left edge of the world.</param>
    /// <param name="maxLongitude">Longitude at the right edge of the world.</param>
    /// <param name="minLatitude">Latitude at the bottom edge of the world.</param>
    /// <param name="maxLatitude">Latitude at the top edge of the world.</param>
    /// <param name="projection">
    /// Projection to use; null (the default) uses <see cref="GeoProjections.Identity"/> over the given
    /// ranges, i.e. the ranges are laid out flat and equally scaled in degrees.
    /// </param>
    public static IGeoFrame CustomSphere(double minLongitude, double maxLongitude,
                                         double minLatitude, double maxLatitude,
                                         IGeoProjection? projection = null)
    {
        var used = projection ?? GeoProjections.Identity(
            minLongitude, maxLongitude, minLatitude, maxLatitude);
        return new SphereFrame($"Custom sphere / {used.Name}", minLongitude, maxLongitude, used);
    }

    /// <summary>
    /// Arbitrary plane coordinates: world units, pixels, an abstract layout, whatever the data is in.
    /// No projection, no wrapping, no vertical truncation - the ranges are the world.
    /// </summary>
    /// <param name="minX">Left edge.</param>
    /// <param name="minY">Bottom edge.</param>
    /// <param name="maxX">Right edge.</param>
    /// <param name="maxY">Top edge.</param>
    public static IGeoFrame CustomPlane(double minX, double minY, double maxX, double maxY)
        => new CustomPlaneFrame(minX, minY, maxX, maxY);
}

/// <summary>
/// A sphere frame: coordinates are longitude and latitude, the projection decides how they land in the
/// normalized world, and the horizontal axis wraps after <see cref="WorldWidth"/> degrees.
/// </summary>
internal sealed class SphereFrame : IGeoFrame
{
    private readonly IGeoProjection _projection;
    private readonly double _minLongitude;
    private readonly double _longitudeSpan;

    internal SphereFrame(string name, double minLongitude, double maxLongitude, IGeoProjection projection)
    {
        Name = name;
        _projection = projection;
        _minLongitude = minLongitude;
        _longitudeSpan = maxLongitude - minLongitude;
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public double Aspect => _projection.Aspect;

    /// <inheritdoc />
    public double WorldWidth => _longitudeSpan;

    /// <inheritdoc />
    public bool WrapsX => true;

    /// <inheritdoc />
    public double? MaxAbsY => _projection.MaxLatitude;

    /// <inheritdoc />
    public (double U, double V) Normalize(double x, double y) => _projection.Forward(x, y);

    /// <inheritdoc />
    public (double X, double Y) Denormalize(double u, double v) => _projection.Inverse(u, v);

    /// <inheritdoc />
    public double WrapX(double x)
    {
        if (!(_longitudeSpan > 0)) return _minLongitude;
        double offset = x - _minLongitude;
        return _minLongitude + offset - Math.Floor(offset / _longitudeSpan) * _longitudeSpan;
    }
}

/// <summary>Arbitrary plane coordinates; see <see cref="GeoFrames.CustomPlane"/>.</summary>
internal sealed class CustomPlaneFrame : IGeoFrame
{
    private readonly double _minX;
    private readonly double _minY;
    private readonly double _maxX;
    private readonly double _maxY;

    internal CustomPlaneFrame(double minX, double minY, double maxX, double maxY)
    {
        _minX = minX;
        _minY = minY;
        _maxX = maxX;
        _maxY = maxY;
    }

    /// <inheritdoc />
    public string Name => "Custom plane";

    /// <inheritdoc />
    public double Aspect
    {
        get
        {
            double width  = _maxX - _minX;
            double height = _maxY - _minY;
            return width > 0 && height > 0 ? width / height : 1.0;
        }
    }

    /// <inheritdoc />
    public double WorldWidth => Math.Max(0.0, _maxX - _minX);

    /// <inheritdoc />
    public bool WrapsX => false;

    /// <inheritdoc />
    public double? MaxAbsY => null;

    /// <inheritdoc />
    public (double U, double V) Normalize(double x, double y)
    {
        double width  = _maxX - _minX;
        double height = _maxY - _minY;
        // A degenerate range has no interior to land in: the middle of the axis keeps the arithmetic
        // finite instead of producing an infinity or a NaN (see IGeoProjection.Forward).
        return (width > 0 ? (x - _minX) / width : 0.5,
                height > 0 ? (y - _minY) / height : 0.5);
    }

    /// <inheritdoc />
    public (double X, double Y) Denormalize(double u, double v)
        => (_minX + u * (_maxX - _minX), _minY + v * (_maxY - _minY));

    /// <inheritdoc />
    public double WrapX(double x) => x;
}
