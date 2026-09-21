namespace GodotNodeExtension.Tests.GodotChart.Geo;

using System;
using GdUnit4;
using GodotNodeExtension.Component.GodotChart;
using static GdUnit4.Assertions;

/// <summary>
/// Behaviour specification for the coordinate layer: the sphere projections
/// (<see cref="GeoProjections"/>) and the frames (<see cref="GeoFrames"/>) that give coordinates their
/// meaning. Pure arithmetic, no canvas and no device - the point of keeping the projection layer
/// stateless.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class GeoFrameTest
{
    /// <summary>Assert two doubles are within <paramref name="eps"/> (IsEqual is exact for doubles).</summary>
    private static void Approx(double actual, double expected, double eps = 1e-9)
        => AssertThat(Math.Abs(actual - expected) <= eps).IsTrue();

    /// <summary>Latitudes a projection round trip is sampled at, including both truncation limits.</summary>
    private static readonly double[] Latitudes = [-85.05112878, -66.5, -23.4378, 0, 12.3, 45.0, 66.5, 85.05112878];

    /// <summary>Longitudes a projection round trip is sampled at, including the wrap edges.</summary>
    private static readonly double[] Longitudes = [-180, -120.5, -0.5, 0, 33.3, 97.0, 179.9, 180];

    // ── Web Mercator ───────────────────────────────────────────────────────

    [TestCase]
    public void WebMercatorPutsTheOriginInTheMiddleOfItsWorld()
    {
        var projection = GeoProjections.WebMercator;

        var (u, v) = projection.Forward(0.0, 0.0);

        Approx(u, 0.5);
        Approx(v, 0.5);
        Approx(projection.MaxLatitude, 85.05112878);
    }

    [TestCase]
    public void WebMercatorStretchesLatitudeToMakeItsWorldSquare()
    {
        var projection = GeoProjections.WebMercator;

        // Web Mercator is conformal, so equal pixels per normalized unit means equal metres in both
        // directions - which is exactly what its world being 1:1 wide states.
        Approx(projection.Aspect, 1.0);
        Approx(projection.Forward(-180.0, -85.05112878).U, 0.0);
        Approx(projection.Forward(-180.0, -85.05112878).V, 0.0, 1e-6);
        Approx(projection.Forward(180.0, 85.05112878).U, 1.0);
        Approx(projection.Forward(180.0, 85.05112878).V, 1.0, 1e-6);
    }

    [TestCase]
    public void WebMercatorClampsLatitudesBeyondItsLimitInsteadOfProducingInfinity()
    {
        var projection = GeoProjections.WebMercator;

        var clamped = projection.Forward(12.0, 89.9);
        var limit = projection.Forward(12.0, projection.MaxLatitude);

        Approx(clamped.U, limit.U);
        Approx(clamped.V, limit.V);
        AssertThat(double.IsFinite(clamped.V)).IsTrue();
    }

    [TestCase]
    public void WebMercatorRoundTripsEverywhereInsideItsLimits()
    {
        var projection = GeoProjections.WebMercator;

        foreach (double latitude in Latitudes)
        foreach (double longitude in Longitudes)
        {
            var (u, v) = projection.Forward(longitude, latitude);
            var (backLongitude, backLatitude) = projection.Inverse(u, v);

            Approx(backLongitude, longitude, 1e-9);
            Approx(backLatitude, latitude, 1e-9);
        }
    }

    // ── Equirectangular ────────────────────────────────────────────────────

    [TestCase]
    public void EquirectangularUsesTheDegreeGrid()
    {
        var projection = GeoProjections.Equirectangular;

        Approx(projection.Aspect, 2.0); // 360° wide against 180° tall
        var (u, v) = projection.Forward(0.0, 0.0);
        Approx(u, 0.5);
        Approx(v, 0.5);
        Approx(projection.Forward(-180.0, -90.0).U, 0.0);
        Approx(projection.Forward(-180.0, -90.0).V, 0.0);
        Approx(projection.Forward(180.0, 90.0).U, 1.0);
        Approx(projection.Forward(180.0, 90.0).V, 1.0);
    }

    [TestCase]
    public void EquirectangularRoundTripsUpToThePoles()
    {
        var projection = GeoProjections.Equirectangular;

        foreach (double latitude in new[] { -90.0, -45.0, 0.0, 45.0, 90.0 })
        {
            var (u, v) = projection.Forward(33.3, latitude);
            var (longitude, backLatitude) = projection.Inverse(u, v);

            Approx(longitude, 33.3);
            Approx(backLatitude, latitude);
        }
    }

    // ── Identity ───────────────────────────────────────────────────────────

    [TestCase]
    public void IdentityLaysTheGivenRangesOutFlat()
    {
        var projection = GeoProjections.Identity(-100.0, 100.0, -50.0, 50.0);

        Approx(projection.Aspect, 2.0); // 200° wide against 100° tall, equally scaled in degrees
        var (u, v) = projection.Forward(0.0, 0.0);
        Approx(u, 0.5);
        Approx(v, 0.5);
        Approx(projection.Forward(-100.0, -50.0).U, 0.0);
        Approx(projection.Forward(-100.0, -50.0).V, 0.0);
        Approx(projection.Forward(100.0, 50.0).U, 1.0);
        Approx(projection.Forward(100.0, 50.0).V, 1.0);
        Approx(projection.MaxLatitude, 50.0);
    }

    [TestCase]
    public void IdentityRoundTripsAcrossAnInventedPlanet()
    {
        var projection = GeoProjections.Identity(0.0, 720.0, -90.0, 90.0);

        var (u, v) = projection.Forward(540.0, 12.3);
        var (longitude, latitude) = projection.Inverse(u, v);

        Approx(longitude, 540.0);
        Approx(latitude, 12.3);
        Approx(projection.Aspect, 4.0);
    }

    [TestCase]
    public void ADegenerateIdentityRangeFallsBackToTheMiddleInsteadOfNaN()
    {
        var projection = GeoProjections.Identity(5.0, 5.0, 1.0, 1.0);

        var (u, v) = projection.Forward(5.0, 1.0);

        Approx(u, 0.5);
        Approx(v, 0.5);
        AssertThat(double.IsFinite(projection.Aspect)).IsTrue();
    }

    // ── Frames ─────────────────────────────────────────────────────────────

    [TestCase]
    public void Wgs84IsTheRealWorldSphere()
    {
        var frame = GeoFrames.Wgs84();

        Approx(frame.Aspect, 1.0);
        Approx(frame.WorldWidth, 360.0);
        AssertThat(frame.WrapsX).IsTrue();
        Approx(frame.MaxAbsY!.Value, 85.05112878);

        var (u, v) = frame.Normalize(0.0, 0.0);
        Approx(u, 0.5);
        Approx(v, 0.5);

        var (longitude, latitude) = frame.Denormalize(u, v);
        Approx(longitude, 0.0);
        Approx(latitude, 0.0);
    }

    [TestCase]
    public void Wgs84FoldsLongitudeIntoItsOwnRange()
    {
        var frame = GeoFrames.Wgs84();

        Approx(frame.WrapX(190.0), -170.0);
        Approx(frame.WrapX(-190.0), 170.0);
        Approx(frame.WrapX(540.0), -180.0);
        Approx(frame.WrapX(-170.0), -170.0);
    }

    [TestCase]
    public void Wgs84TruncatesLatitudeAtTheProjectionLimit()
    {
        var frame = GeoFrames.Wgs84();

        Approx(frame.Normalize(0.0, 89.9).V, frame.Normalize(0.0, frame.MaxAbsY!.Value).V);
        AssertThat(double.IsFinite(frame.Normalize(0.0, -89.9).V)).IsTrue();
    }

    [TestCase]
    public void AWgs84FrameCanUseAnotherProjection()
    {
        var frame = GeoFrames.Wgs84(GeoProjections.Equirectangular);

        Approx(frame.Aspect, 2.0);
        Approx(frame.MaxAbsY!.Value, 90.0);
        Approx(frame.Normalize(-180.0, -90.0).V, 0.0);
    }

    [TestCase]
    public void ACustomSphereUsesItsOwnRangesAndStillWraps()
    {
        var frame = GeoFrames.CustomSphere(0.0, 720.0, -90.0, 90.0);

        Approx(frame.WorldWidth, 720.0);
        Approx(frame.Aspect, 4.0);
        AssertThat(frame.WrapsX).IsTrue();
        Approx(frame.Normalize(0.0, -90.0).U, 0.0);
        Approx(frame.Normalize(0.0, -90.0).V, 0.0);
        Approx(frame.Normalize(720.0, 90.0).U, 1.0);
        Approx(frame.Normalize(720.0, 90.0).V, 1.0);
        Approx(frame.WrapX(800.0), 80.0);
        Approx(frame.WrapX(-10.0), 710.0);
    }

    [TestCase]
    public void ACustomSphereCanProjectLikeTheRealWorld()
    {
        var frame = GeoFrames.CustomSphere(-180.0, 180.0, -85.05112878, 85.05112878,
                                           GeoProjections.WebMercator);

        Approx(frame.Normalize(0.0, 0.0).U, 0.5);
        Approx(frame.Normalize(0.0, 0.0).V, 0.5);
        Approx(frame.Aspect, 1.0);
        Approx(frame.Normalize(0.0, 89.0).V, frame.Normalize(0.0, 85.05112878).V);
    }

    [TestCase]
    public void APlaneFrameNeedsNoLongitudeOrLatitude()
    {
        var frame = GeoFrames.CustomPlane(-50.0, -50.0, 150.0, 50.0);

        Approx(frame.Aspect, 2.0);
        Approx(frame.WorldWidth, 200.0);
        AssertThat(frame.WrapsX).IsFalse();
        AssertThat(frame.MaxAbsY is null).IsTrue();

        var (u, v) = frame.Normalize(-50.0, -50.0);
        Approx(u, 0.0);
        Approx(v, 0.0);
        Approx(frame.Normalize(150.0, 50.0).U, 1.0);
        Approx(frame.Normalize(150.0, 50.0).V, 1.0);

        // A plane frame's edge is an edge: wrapping is the identity.
        Approx(frame.WrapX(400.0), 400.0);

        var (x, y) = frame.Denormalize(0.25, 0.75);
        Approx(x, 0.0);
        Approx(y, 25.0);
    }

    [TestCase]
    public void ADegeneratePlaneRangeStaysFinite()
    {
        var frame = GeoFrames.CustomPlane(5.0, 5.0, 5.0, 5.0);

        var (u, v) = frame.Normalize(5.0, 5.0);

        Approx(u, 0.5);
        Approx(v, 0.5);
        Approx(frame.Aspect, 1.0);
        Approx(frame.WorldWidth, 0.0);
        AssertThat(double.IsFinite(frame.Denormalize(0.3, 0.3).X)).IsTrue();
    }

    [TestCase]
    public void FramesAreNamed()
    {
        AssertThat(GeoFrames.Wgs84().Name).Contains("WGS84");
        AssertThat(GeoFrames.Wgs84().Name).Contains("Web Mercator");
        AssertThat(GeoFrames.CustomSphere(0.0, 360.0, -90.0, 90.0).Name).Contains("Custom sphere");
        AssertThat(GeoFrames.CustomPlane(0.0, 0.0, 1.0, 1.0).Name).IsEqual("Custom plane");
    }
}
