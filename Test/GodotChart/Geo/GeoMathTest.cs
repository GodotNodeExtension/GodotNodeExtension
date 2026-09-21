namespace GodotNodeExtension.Tests.GodotChart.Geo;

using System;
using System.Collections.Generic;
using GdUnit4;
using GodotNodeExtension.Component.GodotChart;
using static GdUnit4.Assertions;

/// <summary>
/// Behaviour specification for the plane geometry <see cref="GeoMath"/> offers a geographic mark: point in
/// polygon (the region hit test), ring winding (which real map data gets wrong), point to segment distance
/// (the line hit test) and the bounding rectangle of a feature set.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class GeoMathTest
{
    /// <summary>A 10 x 10 square, counterclockwise.</summary>
    private static readonly IReadOnlyList<GeoPoint> Square =
        [new(0, 0), new(10, 0), new(10, 10), new(0, 10), new(0, 0)];

    /// <summary>An L shaped ring: the square with a notch cut out of its top right corner.</summary>
    private static readonly IReadOnlyList<GeoPoint> Notched =
        [new(0, 0), new(10, 0), new(10, 5), new(5, 5), new(5, 10), new(0, 10), new(0, 0)];

    /// <summary>Assert two doubles are within <paramref name="eps"/> (IsEqual is exact for doubles).</summary>
    private static void Approx(double actual, double expected, double eps = 1e-9)
        => AssertThat(Math.Abs(actual - expected) <= eps).IsTrue();

    // ── Point in polygon ───────────────────────────────────────────────────

    [TestCase]
    public void APointInsideARingIsInside()
    {
        AssertThat(GeoMath.PointInPolygon(new GeoPoint(5, 5), Square)).IsTrue();
        AssertThat(GeoMath.PointInPolygon(new GeoPoint(0.5, 9.5), Square)).IsTrue();
    }

    [TestCase]
    public void APointOutsideARingIsOutside()
    {
        AssertThat(GeoMath.PointInPolygon(new GeoPoint(-1, 5), Square)).IsFalse();
        AssertThat(GeoMath.PointInPolygon(new GeoPoint(5, -1), Square)).IsFalse();
        AssertThat(GeoMath.PointInPolygon(new GeoPoint(11, 5), Square)).IsFalse();
        AssertThat(GeoMath.PointInPolygon(new GeoPoint(5, 11), Square)).IsFalse();
    }

    [TestCase]
    public void AConcaveRingIsReadCorrectly()
    {
        // The notch is outside the shape even though it is inside the ring's bounding box.
        AssertThat(GeoMath.PointInPolygon(new GeoPoint(2, 2), Notched)).IsTrue();
        AssertThat(GeoMath.PointInPolygon(new GeoPoint(2, 8), Notched)).IsTrue();
        AssertThat(GeoMath.PointInPolygon(new GeoPoint(8, 2), Notched)).IsTrue();
        AssertThat(GeoMath.PointInPolygon(new GeoPoint(8, 8), Notched)).IsFalse();
    }

    [TestCase]
    public void OneRingIsTestedAtATime()
    {
        // A hole is a ring like any other: the point is inside both the outer ring and the hole, and it is
        // the caller that knows a hole subtracts. That is what "even-odd per ring" means here.
        var hole = new List<GeoPoint> { new(4, 4), new(6, 4), new(6, 6), new(4, 6), new(4, 4) };
        var point = new GeoPoint(5, 5);

        AssertThat(GeoMath.PointInPolygon(point, Square)).IsTrue();
        AssertThat(GeoMath.PointInPolygon(point, hole)).IsTrue();
        AssertThat(GeoMath.PointInPolygon(point, [new GeoPoint(0, 0), new GeoPoint(1, 0), new GeoPoint(1, 1)]))
            .IsFalse();
    }

    [TestCase]
    public void ARingTooShortToHaveAnInsideIsNeverHit()
    {
        AssertThat(GeoMath.PointInPolygon(new GeoPoint(0, 0),
            [new GeoPoint(0, 0), new GeoPoint(0, 0)])).IsFalse();
        AssertThat(GeoMath.PointInPolygon(new GeoPoint(0, 0), [])).IsFalse();
    }

    // ── Winding ────────────────────────────────────────────────────────────

    [TestCase]
    public void WindingFollowsTheSignedArea()
    {
        var clockwise = new List<GeoPoint> { new(0, 0), new(0, 10), new(10, 10), new(10, 0), new(0, 0) };

        AssertThat(GeoMath.RingIsClockwise(Square)).IsFalse();     // counterclockwise: the GeoJSON convention
        AssertThat(GeoMath.RingIsClockwise(clockwise)).IsTrue();
        AssertThat(GeoMath.RingIsClockwise(Notched)).IsFalse();
    }

    [TestCase]
    public void ADegenerateRingHasNoWinding()
    {
        AssertThat(GeoMath.RingIsClockwise([new GeoPoint(1, 1), new GeoPoint(2, 2)])).IsFalse();
        AssertThat(GeoMath.RingIsClockwise([new GeoPoint(1, 1), new GeoPoint(2, 1), new GeoPoint(1, 1)])).IsFalse();
    }

    // ── Distance to a segment ──────────────────────────────────────────────

    [TestCase]
    public void DistanceToASegmentCoversTheEndsAndTheMiddle()
    {
        var a = new GeoPoint(0, 0);
        var b = new GeoPoint(10, 0);

        Approx(GeoMath.DistanceToSegment(new GeoPoint(5, 3), a, b), 3.0);   // perpendicular
        Approx(GeoMath.DistanceToSegment(new GeoPoint(-4, 0), a, b), 4.0);  // before the start
        Approx(GeoMath.DistanceToSegment(new GeoPoint(14, 0), a, b), 4.0);  // beyond the end
        Approx(GeoMath.DistanceToSegment(new GeoPoint(3, 4), a, b), 4.0);
    }

    [TestCase]
    public void ADegenerateSegmentIsAPoint()
    {
        var a = new GeoPoint(2, 2);

        Approx(GeoMath.DistanceToSegment(new GeoPoint(2, 7), a, a), 5.0);
    }

    // ── Feature bounds ─────────────────────────────────────────────────────

    [TestCase]
    public void BoundsOfAFeatureSetCoverEveryGeometry()
    {
        var builder = new GeoGeometryBuilder();
        var features = new List<GeoFeature>
        {
            builder.Polygon((0, 0), (10, 0), (10, 10), (0, 10)).Feature("a"),
            builder.Point(-5, 20).Feature("b"),
        };

        var bounds = GeoMath.BoundsOf(features);

        AssertThat(bounds is not null).IsTrue();
        Approx(bounds!.Value.MinX, -5.0);
        Approx(bounds.Value.MinY, 0.0);
        Approx(bounds.Value.MaxX, 10.0);
        Approx(bounds.Value.MaxY, 20.0);
    }

    [TestCase]
    public void BoundsOfNothingIsNothing()
    {
        AssertThat(GeoMath.BoundsOf([]) is null).IsTrue();
    }
}
