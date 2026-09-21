namespace GodotNodeExtension.Component.GodotChart;

using System;

/// <summary>
/// A sphere projection: longitude/latitude to the normalized [0, 1]² world of a geographic frame,
/// and back.
/// <para>
/// Both normalized axes run left to right and bottom to top, so north is up and a host never has to
/// flip anything. A projection whose world is square at equal physical scale (Web Mercator, whose
/// ±85.05112878° cutoff makes the projected extent as tall as it is wide) reports
/// <see cref="Aspect"/> = 1, which is what keeps a map undistorted: equal pixels per normalized unit
/// then means equal metres in both directions.
/// </para>
/// <para>
/// Implementations are stateless and pure, so the projection part of a chart is testable without a
/// canvas, a plot rectangle or a device.
/// </para>
/// </summary>
public interface IGeoProjection
{
    /// <summary>Name of the projection, for diagnostics and documentation.</summary>
    string Name { get; }

    /// <summary>
    /// Largest latitude the projection can represent, in degrees. Web Mercator truncates at
    /// ±85.05112878° because the poles are infinitely far away; <see cref="Forward"/> clamps to it
    /// instead of producing an infinity.
    /// </summary>
    double MaxLatitude { get; }

    /// <summary>
    /// Width / height of the projection's world at equal physical scale. 1 for a conformal square
    /// world (Web Mercator); 2 for a plate carrée world of 360° x 180°, which is twice as wide as it
    /// is tall when a degree of longitude and a degree of latitude are the same length.
    /// </summary>
    double Aspect { get; }

    /// <summary>
    /// Project a geographic coordinate to the normalized world.
    /// </summary>
    /// <param name="longitude">Longitude in degrees. Values outside ±180 are allowed: they are the
    /// same meridian one world to the left or right.</param>
    /// <param name="latitude">Latitude in degrees, clamped to ±<see cref="MaxLatitude"/>.</param>
    /// <returns>Normalized coordinates: both may fall outside [0, 1] for a wrapped longitude.</returns>
    (double U, double V) Forward(double longitude, double latitude);

    /// <summary>
    /// The inverse of <see cref="Forward"/>.
    /// </summary>
    /// <param name="u">Normalized horizontal position.</param>
    /// <param name="v">Normalized vertical position.</param>
    /// <returns>Longitude and latitude in degrees.</returns>
    (double Longitude, double Latitude) Inverse(double u, double v);
}

/// <summary>
/// The built-in projections. The default is <see cref="WebMercator"/>, the scheme mainstream map
/// services and tile sets use, so a frame's coordinates line up with map data taken from them.
/// </summary>
public static class GeoProjections
{
    /// <summary>
    /// Web Mercator (EPSG:3857), the default: a conformal cylindrical projection that keeps local
    /// shape and angle, truncating the poles at ±85.05112878° where the world becomes square.
    /// </summary>
    public static IGeoProjection WebMercator { get; } = new WebMercatorProjection();

    /// <summary>
    /// Equirectangular (plate carrée): longitude and latitude are used as flat coordinates, and the
    /// world is 360° x 180°. Distorts more than Mercator at high latitudes but shows the poles and
    /// keeps areas comparable to the degree grid, which is what world overview maps want.
    /// </summary>
    public static IGeoProjection Equirectangular { get; } = new EquirectangularProjection();

    /// <summary>
    /// Identity: the given longitude/latitude ranges are the world, with both axes equally scaled in
    /// degrees. This is the projection a frame of an invented sphere uses - a planet whose map is
    /// simply its own coordinate ranges, whether that is a 360° x 180° world or a 720° wide one.
    /// </summary>
    /// <param name="minLongitude">Longitude at the left edge of the world.</param>
    /// <param name="maxLongitude">Longitude at the right edge of the world.</param>
    /// <param name="minLatitude">Latitude at the bottom edge of the world.</param>
    /// <param name="maxLatitude">Latitude at the top edge of the world.</param>
    public static IGeoProjection Identity(double minLongitude, double maxLongitude,
                                          double minLatitude, double maxLatitude)
        => new IdentityProjection(minLongitude, maxLongitude, minLatitude, maxLatitude);
}

/// <summary>Web Mercator; see <see cref="GeoProjections.WebMercator"/>.</summary>
internal sealed class WebMercatorProjection : IGeoProjection
{
    /// <inheritdoc />
    public string Name => "Web Mercator";

    /// <inheritdoc />
    public double MaxLatitude => 85.05112878;

    /// <inheritdoc />
    public double Aspect => 1.0;

    /// <inheritdoc />
    public (double U, double V) Forward(double longitude, double latitude)
    {
        double clamped = Math.Clamp(latitude, -MaxLatitude, MaxLatitude);
        double y = Math.Log(Math.Tan(Math.PI / 4.0 + clamped * Math.PI / 360.0));
        return ((longitude + 180.0) / 360.0, 0.5 + y / (2.0 * Math.PI));
    }

    /// <inheritdoc />
    public (double Longitude, double Latitude) Inverse(double u, double v)
    {
        double latitude = 2.0 * Math.Atan(Math.Exp((v - 0.5) * 2.0 * Math.PI)) - Math.PI / 2.0;
        return (u * 360.0 - 180.0, latitude * 180.0 / Math.PI);
    }
}

/// <summary>Equirectangular / plate carrée; see <see cref="GeoProjections.Equirectangular"/>.</summary>
internal sealed class EquirectangularProjection : IGeoProjection
{
    /// <inheritdoc />
    public string Name => "Equirectangular";

    /// <inheritdoc />
    public double MaxLatitude => 90.0;

    /// <inheritdoc />
    public double Aspect => 2.0;

    /// <inheritdoc />
    public (double U, double V) Forward(double longitude, double latitude)
    {
        double clamped = Math.Clamp(latitude, -MaxLatitude, MaxLatitude);
        return ((longitude + 180.0) / 360.0, (clamped + 90.0) / 180.0);
    }

    /// <inheritdoc />
    public (double Longitude, double Latitude) Inverse(double u, double v)
        => (u * 360.0 - 180.0, v * 180.0 - 90.0);
}

/// <summary>Identity over the frame's own ranges; see <see cref="GeoProjections.Identity"/>.</summary>
internal sealed class IdentityProjection : IGeoProjection
{
    private readonly double _minLongitude;
    private readonly double _minLatitude;
    private readonly double _maxLatitude;
    private readonly double _longitudeSpan;
    private readonly double _latitudeSpan;

    internal IdentityProjection(double minLongitude, double maxLongitude,
                                double minLatitude, double maxLatitude)
    {
        _minLongitude = minLongitude;
        _minLatitude  = minLatitude;
        _maxLatitude  = maxLatitude;
        _longitudeSpan = maxLongitude - minLongitude;
        _latitudeSpan  = maxLatitude - minLatitude;
    }

    /// <inheritdoc />
    public string Name => "Identity";

    /// <inheritdoc />
    public double MaxLatitude => _maxLatitude;

    /// <inheritdoc />
    public double Aspect => _latitudeSpan > 0 ? _longitudeSpan / _latitudeSpan : 1.0;

    /// <inheritdoc />
    public (double U, double V) Forward(double longitude, double latitude)
    {
        // A degenerate range (min == max) has no interior to land in: the middle of the axis is the
        // only answer that keeps the arithmetic finite, and the warning about it belongs to whoever
        // built the range.
        double u = _longitudeSpan > 0 ? (longitude - _minLongitude) / _longitudeSpan : 0.5;
        double v = _latitudeSpan > 0
            ? (Math.Clamp(latitude, _minLatitude, _maxLatitude) - _minLatitude) / _latitudeSpan
            : 0.5;
        return (u, v);
    }

    /// <inheritdoc />
    public (double Longitude, double Latitude) Inverse(double u, double v)
        => (_minLongitude + u * _longitudeSpan, _minLatitude + v * _latitudeSpan);
}
