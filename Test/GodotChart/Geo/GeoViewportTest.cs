namespace GodotNodeExtension.Tests.GodotChart.Geo;

using System;
using GdUnit4;
using Godot;
using GodotNodeExtension.Component.GodotChart;
using static GdUnit4.Assertions;

/// <summary>
/// Behaviour specification for <see cref="GeoViewport"/>: the single-scale (zoom and centre) window a
/// geographic chart looks through. Covers the projection round trip, the anchor-correct zoom, panning,
/// fitting, wrapping at the antimeridian, the pan limits and the change notification a host invalidates
/// its caches from.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class GeoViewportTest
{
    private static readonly PlotArea Plot = new(10f, 20f, 400f, 300f);

    /// <summary>Longitudes a projection round trip is sampled at.</summary>
    private static readonly double[] Longitudes = [-120.0, -33.3, 0.0, 45.5, 120.0];

    /// <summary>Latitudes a projection round trip is sampled at.</summary>
    private static readonly double[] Latitudes = [-60.0, -12.5, 0.0, 23.4, 60.0];

    /// <summary>Assert two doubles are within <paramref name="eps"/> (IsEqual is exact for doubles).</summary>
    private static void Approx(double actual, double expected, double eps = 1e-9)
        => AssertThat(Math.Abs(actual - expected) <= eps).IsTrue();

    // ── Scale ──────────────────────────────────────────────────────────────

    [TestCase]
    public void TheWorldIsATileAtZoomZeroAndDoublesFromThere()
    {
        var viewport = new GeoViewport(GeoFrames.Wgs84());

        Approx(viewport.WorldWidthPixels, 256.0);
        Approx(viewport.WorldHeightPixels, 256.0);
        Approx(viewport.PixelsPerUnit, 256.0 / 360.0);

        viewport.SetZoom(1.0);
        Approx(viewport.WorldWidthPixels, 512.0);

        viewport.SetZoom(4.5); // zoom levels are continuous
        Approx(viewport.WorldWidthPixels, 256.0 * Math.Pow(2.0, 4.5));
    }

    [TestCase]
    public void APaneFrameScalesBothAxesByTheSamePixelSize()
    {
        // A 1000 x 500 world: one world unit must be the same number of pixels horizontally and
        // vertically, or the map is stretched (which is what a pair of per-axis windows would do).
        var viewport = new GeoViewport(GeoFrames.CustomPlane(0.0, 0.0, 1000.0, 500.0));
        viewport.SetZoom(2.0);

        double perUnitX = viewport.PixelsPerUnit;
        double perUnitY = viewport.WorldHeightPixels / 500.0;

        Approx(perUnitX, 1024.0 / 1000.0);
        Approx(perUnitY, perUnitX);
    }

    [TestCase]
    public void TheViewportClampsItsZoomAndNeverReportsAnInfinity()
    {
        var viewport = new GeoViewport(GeoFrames.Wgs84());

        viewport.SetZoom(1e9);
        Approx(viewport.ZoomLevel, GeoMath.MaxZoomLevel);
        AssertThat(double.IsFinite(viewport.WorldWidthPixels)).IsTrue();

        viewport.SetZoom(-1e9);
        Approx(viewport.ZoomLevel, GeoMath.MinZoomLevel);
        AssertThat(double.IsFinite(viewport.WorldWidthPixels)).IsTrue();

        viewport.SetZoom(3.0);
        viewport.SetZoom(double.NaN); // a calculation that went wrong is read as "no zoom"
        Approx(viewport.ZoomLevel, 0.0);
    }

    [TestCase]
    public void ZoomAndResolutionAreInverses()
    {
        var frame = GeoFrames.Wgs84();

        foreach (double zoom in new[] { -4.0, 0.0, 4.5, 12.0 })
            Approx(GeoMath.ResolutionToZoom(GeoMath.ZoomToResolution(zoom, frame), frame), zoom, 1e-9);

        // The classic anchor: at zoom 0 one pixel covers 156543 m of the equator.
        Approx(GeoMath.ZoomToResolution(0.0, frame) * GeoMath.MercatorMetersPerDegree, 156543.03392804097, 1e-6);
        Approx(GeoMath.ZoomToResolution(0.0, frame), 360.0 / 256.0);
    }

    [TestCase]
    public void ADegenerateResolutionClampsInsteadOfProducingInfinity()
    {
        var frame = GeoFrames.Wgs84();

        // Zero units per pixel is infinitely far in; a resolution beyond the world is infinitely far out.
        Approx(GeoMath.ResolutionToZoom(0.0, frame), GeoMath.MaxZoomLevel);
        Approx(GeoMath.ResolutionToZoom(-2.0, frame), GeoMath.MaxZoomLevel);
        Approx(GeoMath.ResolutionToZoom(double.PositiveInfinity, frame), GeoMath.MinZoomLevel);
        Approx(GeoMath.ResolutionToZoom(360.0, GeoFrames.CustomPlane(5.0, 5.0, 5.0, 5.0)), GeoMath.MaxZoomLevel);

        // And a zoom level beyond the supported range still gives a resolution that is finite.
        AssertThat(double.IsFinite(GeoMath.ZoomToResolution(1e9, frame))).IsTrue();
        Approx(GeoMath.ZoomToResolution(1e9, frame), 360.0 / (256.0 * Math.Pow(2.0, GeoMath.MaxZoomLevel)));
    }

    // ── Projection ─────────────────────────────────────────────────────────

    [TestCase]
    public void TheCentreCoordinateLandsInTheCentreOfThePlot()
    {
        var viewport = new GeoViewport(GeoFrames.Wgs84());
        viewport.SetView(0.0, 0.0, 3.0);

        var projected = viewport.Project(0.0, 0.0, Plot);

        Approx(projected.X, Plot.X + Plot.Width / 2f, 1e-3);
        Approx(projected.Y, Plot.Y + Plot.Height / 2f, 1e-3);
    }

    [TestCase]
    public void NorthIsUpAndEastIsRight()
    {
        var viewport = new GeoViewport(GeoFrames.Wgs84());
        viewport.SetView(0.0, 0.0, 2.0);

        var origin = viewport.Project(0.0, 0.0, Plot);

        AssertThat(viewport.Project(0.0, 60.0, Plot).Y < origin.Y).IsTrue();
        AssertThat(viewport.Project(60.0, 0.0, Plot).X > origin.X).IsTrue();
    }

    [TestCase]
    public void ProjectAndUnprojectAreInverses()
    {
        var viewport = new GeoViewport(GeoFrames.Wgs84());
        viewport.SetView(20.0, 30.0, 4.0);

        // Round trip in pixels: the screen position is a float, so half a pixel is what a projection
        // can promise for coordinates far outside the plot (which the clip then cuts away anyway).
        foreach (double longitude in Longitudes)
        foreach (double latitude in Latitudes)
        {
            var projected = viewport.Project(longitude, latitude, Plot);
            var (backLongitude, backLatitude) = viewport.Unproject(projected, Plot);
            var reprojected = viewport.Project(backLongitude, backLatitude, Plot);

            Approx(reprojected.X, projected.X, 0.5);
            Approx(reprojected.Y, projected.Y, 0.5);
        }
    }

    [TestCase]
    public void ProjectAndUnprojectAreExactInTheVisibleWindow()
    {
        var viewport = new GeoViewport(GeoFrames.Wgs84());
        viewport.SetView(20.0, 30.0, 4.0);
        var visible = viewport.VisibleBounds(Plot);

        // Inside the window the round trip is limited by the arithmetic, not by the screen: a
        // coordinate comes back where it was.
        foreach (double longitude in new[] { visible.MinX, 20.0, visible.MaxX })
        foreach (double latitude in new[] { visible.MinY, 30.0, visible.MaxY })
        {
            var projected = viewport.Project(longitude, latitude, Plot);
            var (backLongitude, backLatitude) = viewport.Unproject(projected, Plot);

            Approx(backLongitude, longitude, 1e-4);
            Approx(backLatitude, latitude, 1e-4);
        }
    }

    [TestCase]
    public void APlaneViewportProjectsAndUnprojectsItsOwnUnits()
    {
        var frame = GeoFrames.CustomPlane(0.0, 0.0, 1000.0, 500.0);
        var viewport = new GeoViewport(frame);
        viewport.SetView(500.0, 250.0, 2.0);

        var center = viewport.Project(500.0, 250.0, Plot);
        Approx(center.X, Plot.X + Plot.Width / 2f, 1e-3);
        Approx(center.Y, Plot.Y + Plot.Height / 2f, 1e-3);

        // The top right corner of the world is half a world right of the centre and half a world up.
        var corner = viewport.Project(1000.0, 500.0, Plot);
        Approx(corner.X, center.X + (float)(viewport.WorldWidthPixels / 2.0), 1e-2);
        Approx(corner.Y, center.Y - (float)(viewport.WorldHeightPixels / 2.0), 1e-2);

        var (x, y) = viewport.Unproject(corner, Plot);
        Approx(x, 1000.0, 1e-6);
        Approx(y, 500.0, 1e-6);
    }

    [TestCase]
    public void ADegeneratePlotRectangleStillProjectsFinitePixels()
    {
        var viewport = new GeoViewport(GeoFrames.Wgs84());
        viewport.SetView(0.0, 0.0, 3.0);

        foreach (var plot in new[] { new PlotArea(0f, 0f, 0f, 0f), new PlotArea(5f, 5f, 0f, 300f) })
        {
            var projected = viewport.Project(10.0, 10.0, plot);
            AssertThat(float.IsFinite(projected.X) && float.IsFinite(projected.Y)).IsTrue();
            AssertThat(double.IsFinite(viewport.Unproject(projected, plot).X)).IsTrue();
        }
    }

    // ── Gestures ───────────────────────────────────────────────────────────

    [TestCase]
    public void ZoomingKeepsTheCoordinateUnderTheAnchor()
    {
        var viewport = new GeoViewport(GeoFrames.Wgs84());
        viewport.SetView(30.0, 20.0, 4.0);
        var anchor = new Vector2(Plot.X + 120f, Plot.Y + 90f);
        var before = viewport.Unproject(anchor, Plot);

        viewport.ZoomBy(2.0, anchor, Plot);

        var after = viewport.Unproject(anchor, Plot);
        Approx(after.X, before.X, 1e-6);
        Approx(after.Y, before.Y, 1e-6);
        Approx(viewport.ZoomLevel, 5.0);
    }

    [TestCase]
    public void ZoomingOutKeepsTheAnchorToo()
    {
        var viewport = new GeoViewport(GeoFrames.Wgs84());
        viewport.SetView(-40.0, 10.0, 6.0);
        var anchor = new Vector2(Plot.X + 320f, Plot.Y + 40f);
        var before = viewport.Unproject(anchor, Plot);

        viewport.ZoomBy(0.5, anchor, Plot);

        var after = viewport.Unproject(anchor, Plot);
        Approx(after.X, before.X, 1e-6);
        Approx(after.Y, before.Y, 1e-6);
        Approx(viewport.ZoomLevel, 5.0);
    }

    [TestCase]
    public void PanningMovesTheContentByThePixelsGiven()
    {
        var viewport = new GeoViewport(GeoFrames.Wgs84());
        viewport.SetView(0.0, 0.0, 4.0);
        var before = viewport.Project(0.0, 0.0, Plot);

        viewport.PanBy(new Vector2(25f, -10f));

        var after = viewport.Project(0.0, 0.0, Plot);
        Approx(after.X, before.X + 25f, 1e-3);
        Approx(after.Y, before.Y - 10f, 1e-3);
    }

    // ── Fitting ────────────────────────────────────────────────────────────

    [TestCase]
    public void FittingARegionShowsItFillingTheTighterAxis()
    {
        var viewport = new GeoViewport(GeoFrames.Wgs84());
        var target = new GeoBounds(100.0, 20.0, 110.0, 30.0);

        AssertThat(viewport.Fit(target, Plot, paddingRatio: 0f)).IsTrue();

        var visible = viewport.VisibleBounds(Plot);
        AssertThat(visible.MinX <= target.MinX + 1e-6 && visible.MaxX >= target.MaxX - 1e-6).IsTrue();
        AssertThat(visible.MinY <= target.MinY + 1e-6 && visible.MaxY >= target.MaxY - 1e-6).IsTrue();

        // One axis is fitted exactly (the 400 x 300 plot is wider than the 10° x 10° region, so the
        // vertical one is the binding constraint) and the other one has room to spare.
        double longitudeRatio = visible.Width / target.Width;
        double latitudeRatio = visible.Height / target.Height;
        Approx(Math.Min(longitudeRatio, latitudeRatio), 1.0, 1e-6);
        AssertThat(Math.Max(longitudeRatio, latitudeRatio) > 1.0).IsTrue();
    }

    [TestCase]
    public void FittingKeepsTheCornersInsideThePlot()
    {
        var viewport = new GeoViewport(GeoFrames.Wgs84());
        var target = new GeoBounds(100.0, 20.0, 110.0, 30.0);

        AssertThat(viewport.Fit(target, Plot, paddingRatio: 0.05f)).IsTrue();

        foreach (var (x, y) in new[]
                 {
                     (target.MinX, target.MinY), (target.MaxX, target.MinY),
                     (target.MinX, target.MaxY), (target.MaxX, target.MaxY),
                 })
        {
            var projected = viewport.Project(x, y, Plot);
            AssertThat(Plot.Contains(projected.X, projected.Y)).IsTrue();
        }
    }

    [TestCase]
    public void FittingTheWorldShowsTheWholeFrame()
    {
        var viewport = new GeoViewport(GeoFrames.Wgs84());

        AssertThat(viewport.FitWorld(Plot, paddingRatio: 0f)).IsTrue();

        var visible = viewport.VisibleBounds(Plot);
        Approx(viewport.CenterX, 0.0, 1e-9);
        Approx(viewport.CenterY, 0.0, 1e-9);
        // The 400 x 300 plot is wider than the world it fits (300 px of world height), so the vertical
        // axis is exact and the horizontal one shows the neighbouring copy of the world as well.
        Approx(visible.MinY, -85.05112878, 1e-6);
        Approx(visible.MaxY, 85.05112878, 1e-6);
        AssertThat(visible.MinX <= -180.0 && visible.MaxX >= 180.0).IsTrue();
    }

    [TestCase]
    public void FittingLeavesTheViewportAloneWhenThereIsNothingToFit()
    {
        var viewport = new GeoViewport(GeoFrames.Wgs84());
        viewport.SetView(12.0, 34.0, 5.0);

        AssertThat(viewport.Fit(new GeoBounds(5.0, 5.0, 5.0, 5.0), Plot)).IsFalse();
        AssertThat(viewport.Fit(new GeoBounds(1.0, 2.0, 3.0, 4.0), new PlotArea(0f, 0f, 0f, 0f))).IsFalse();

        Approx(viewport.CenterX, 12.0);
        Approx(viewport.CenterY, 34.0);
        Approx(viewport.ZoomLevel, 5.0);
    }

    // ── Wrapping ───────────────────────────────────────────────────────────

    [TestCase]
    public void AWrappingViewportKeepsTheNeighbouringWorldInView()
    {
        var viewport = new GeoViewport(GeoFrames.Wgs84());
        viewport.SetView(179.0, 0.0, 4.0);

        var justEastOfTheLine = viewport.Project(179.0, 0.0, Plot);
        var justWestOfTheLine = viewport.Project(-179.0, 0.0, Plot);

        // Two degrees away, on the screen - not one world away on the left edge of the picture.
        Approx(justWestOfTheLine.X - justEastOfTheLine.X, 2.0 * viewport.PixelsPerUnit, 1e-2);
        AssertThat(justWestOfTheLine.X > justEastOfTheLine.X).IsTrue();
    }

    [TestCase]
    public void WrappingCanBeTurnedOffToShowTheSplit()
    {
        var viewport = new GeoViewport(GeoFrames.Wgs84());
        viewport.SetView(179.0, 0.0, 4.0);
        viewport.SetWrapX(false);

        var farLeft = viewport.Project(-179.0, 0.0, Plot);

        AssertThat(farLeft.X < Plot.X).IsTrue();
    }

    [TestCase]
    public void APlaneFrameNeverWraps()
    {
        var viewport = new GeoViewport(GeoFrames.CustomPlane(0.0, 0.0, 100.0, 100.0));

        AssertThat(viewport.WrapsX).IsFalse();
    }

    // ── Pan limits and notification ────────────────────────────────────────

    [TestCase]
    public void PanBoundsKeepTheCentreInsideThem()
    {
        var viewport = new GeoViewport(GeoFrames.Wgs84());
        viewport.SetPanBounds(new GeoBounds(-10.0, -20.0, 10.0, 20.0));

        viewport.SetCenter(50.0, 50.0);
        Approx(viewport.CenterX, 10.0);
        Approx(viewport.CenterY, 20.0);

        viewport.SetCenter(-50.0, -50.0);
        Approx(viewport.CenterX, -10.0);
        Approx(viewport.CenterY, -20.0);

        viewport.SetPanBounds(null);
        viewport.SetCenter(-50.0, -50.0);
        Approx(viewport.CenterX, -50.0);
        Approx(viewport.CenterY, -50.0);
    }

    [TestCase]
    public void EveryChangeIsReportedOnce()
    {
        var viewport = new GeoViewport(GeoFrames.Wgs84());
        int changes = 0;
        viewport.Changed += () => changes++;

        viewport.SetView(10.0, 10.0, 2.0);
        AssertThat(changes).IsEqual(1);

        viewport.SetCenter(11.0, 10.0);
        AssertThat(changes).IsEqual(2);

        viewport.SetZoom(3.0);
        AssertThat(changes).IsEqual(3);

        viewport.PanBy(new Vector2(10f, 0f));
        AssertThat(changes).IsEqual(4);

        viewport.SetWrapX(false);
        AssertThat(changes).IsEqual(5);
    }

    [TestCase]
    public void ANoOpChangeIsNotReported()
    {
        var viewport = new GeoViewport(GeoFrames.Wgs84());
        int changes = 0;
        viewport.Changed += () => changes++;

        viewport.SetView(10.0, 10.0, 2.0);
        viewport.SetView(10.0, 10.0, 2.0); // same view, nothing to redraw
        viewport.SetWrapX(viewport.WrapsX);
        AssertThat(changes).IsEqual(1);
    }

    [TestCase]
    public void InvalidNumbersAreIgnoredInsteadOfMovingTheViewportToNowhere()
    {
        var viewport = new GeoViewport(GeoFrames.Wgs84());
        viewport.SetView(10.0, 10.0, 2.0);

        viewport.SetView(double.NaN, 10.0, 3.0);
        viewport.PanBy(new Vector2(float.NaN, 0f));
        viewport.ZoomBy(0.0, Vector2.Zero, Plot);

        Approx(viewport.CenterX, 10.0);
        Approx(viewport.CenterY, 10.0);
        Approx(viewport.ZoomLevel, 2.0);
    }
}
