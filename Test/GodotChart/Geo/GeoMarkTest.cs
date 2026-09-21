namespace GodotNodeExtension.Tests.GodotChart.Geo;

using System.Collections.Generic;
using System.Linq;
using GdUnit4;
using Godot;
using GodotNodeExtension.Component.GodotChart;
using GodotNodeExtension.Tests.Support;
using Support;
using static GdUnit4.Assertions;

/// <summary>
/// Behaviour specification for the base a geographic mark is built on (<see cref="GeoMark"/>): the
/// coordinate system it declares, the cache that is built once per layout and dropped when the map moves,
/// the viewport the chart hands it, and the check that a chart with only a graph should not be asked for a
/// field.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class GeoMarkTest
{
    private static List<DataRow> TwoPoints() =>
    [
        TestContexts.Row(("x", 1.0), ("y", 2.0)),
        TestContexts.Row(("x", 2.0), ("y", 4.0)),
    ];

    private static Chart ChartWith(FakeCanvas2D canvas, params Mark[] marks)
    {
        var chart = new Chart(canvas) { Width = 400f, Height = 300f };
        chart.Data(TwoPoints());
        chart.Encode(Channel.X, "x");
        chart.Encode(Channel.Y, "y");
        foreach (var mark in marks) chart.Mark(mark);
        return chart;
    }

    // ── What a geographic mark declares ────────────────────────────────────

    [TestCase]
    public void AGeographicMarkUsesAFrameInsteadOfTheAxes()
    {
        var mark = new CountingGeoMark();

        AssertThat(mark.Coordinate).IsEqual(MarkCoordinate.Geographic);
        AssertThat(mark.UsesAxes).IsFalse();
        AssertThat(mark.Source).IsEqual(GeoGeometrySource.Feature);
        AssertThat(mark.RequiresContinuousSpace).IsFalse();
    }

    [TestCase]
    public void AChartOfGeographicMarksDrawsNoCartesianAxes()
    {
        var geographic = new FakeCanvas2D();
        using var geoChart = ChartWith(geographic, new CountingGeoMark());
        geoChart.Render();

        var cartesian = new FakeCanvas2D();
        using var cartesianChart = ChartWith(cartesian, new CartesianProbe());
        cartesianChart.Render();

        AssertThat(cartesian.YAxisLabels.Any()).IsTrue();
        AssertThat(geographic.YAxisLabels.Count()).IsEqual(0);
        AssertThat(geographic.XAxisLabels.Count()).IsEqual(0);
    }

    // ── The viewport the chart hands out ───────────────────────────────────

    [TestCase]
    public void AGeographicMarkMakesTheChartCreateAViewport()
    {
        var canvas = new FakeCanvas2D();
        var mark = new CountingGeoMark();
        using var chart = ChartWith(canvas, mark);

        AssertThat(chart.GeoViewport is not null).IsTrue();
        AssertThat(chart.GeoFrame.WrapsX).IsTrue();

        chart.Render();

        AssertThat(ReferenceEquals(mark.SeenViewport, chart.GeoViewport)).IsTrue();
    }

    [TestCase]
    public void AChartWithoutAGeographicMarkKeepsTheViewportUnset()
    {
        var canvas = new FakeCanvas2D();
        var mark = new CartesianProbe();
        using var chart = ChartWith(canvas, mark);

        chart.Render();

        AssertThat(chart.GeoViewport is null).IsTrue();
        AssertThat(mark.SeenViewport is null).IsTrue();
    }

    // ── The projection cache ───────────────────────────────────────────────

    [TestCase]
    public void AProjectionIsBuiltOnceUntilTheLayoutChanges()
    {
        var canvas = new FakeCanvas2D();
        var mark = new CountingGeoMark();
        using var chart = ChartWith(canvas, mark);

        chart.Render();
        AssertThat(mark.Builds).IsEqual(1);
        AssertThat(mark.LastPointCount).IsEqual(1);

        chart.Render(); // same layout: the projection is reused, not rebuilt
        AssertThat(mark.Builds).IsEqual(1);

        chart.SetGeoViewport(10.0, 20.0, 3.0);
        chart.Render();
        AssertThat(mark.Builds).IsEqual(2);

        chart.GeoViewport!.SetZoom(5.0);
        chart.Render();
        AssertThat(mark.Builds).IsEqual(3);

        chart.PanGeo(new Vector2(5f, 0f));
        chart.Render();
        AssertThat(mark.Builds).IsEqual(4);

        chart.SetGeoFrame(GeoFrames.CustomPlane(0.0, 0.0, 100.0, 50.0));
        chart.Render();
        AssertThat(mark.Builds).IsEqual(5);

        chart.Width = 500f; // a resize is a layout change like any other
        chart.Render();
        AssertThat(mark.Builds).IsEqual(6);
    }

    [TestCase]
    public void ADataChangeRebuildsTheProjection()
    {
        var canvas = new FakeCanvas2D();
        var mark = new CountingGeoMark();
        using var chart = ChartWith(canvas, mark);

        chart.Render();
        chart.SetGeoViewport(10.0, 20.0, 3.0);
        chart.Render();
        AssertThat(mark.Builds).IsEqual(2);

        chart.Data(TwoPoints());
        chart.Render();
        AssertThat(mark.Builds).IsEqual(3);
    }

    // ── The position channels of a geographic layer ────────────────────────

    [TestCase]
    public void AGeographicLayerMayBindItsOwnPositionFields()
    {
        var log = EngineMessageLog.Attach();
        try
        {
            var canvas = new FakeCanvas2D();
            using var chart = new Chart(canvas) { Width = 400f, Height = 300f };
            chart.Data(CoordinatesAsWellAsCategories());
            chart.Encode(Channel.X, "category");
            chart.Encode(Channel.Y, "value");
            var mark = new CountingGeoMark();
            mark.Encode(Channel.X, "lon");
            mark.Encode(Channel.Y, "lat");
            chart.Mark(mark);

            chart.Render();

            // The mark projects the coordinates itself: the chart's position scale is not what it reads, so a
            // second layer with its own coordinate fields is not a shared scale.
            AssertThat(log.WarningsContaining("share one scale").Length).IsEqual(0);
        }
        finally
        {
            log.Detach();
        }
    }

    [TestCase]
    public void ACartesianLayerStillWarnsAboutASharedPositionScale()
    {
        var log = EngineMessageLog.Attach();
        try
        {
            var canvas = new FakeCanvas2D();
            using var chart = new Chart(canvas) { Width = 400f, Height = 300f };
            chart.Data(CoordinatesAsWellAsCategories());
            chart.Encode(Channel.X, "category");
            chart.Encode(Channel.Y, "value");
            var mark = new CartesianProbe();
            mark.Encode(Channel.X, "lon");
            mark.Encode(Channel.Y, "lat");
            chart.Mark(mark);

            chart.Render();

            // The exemption is the geographic mark's own: over a pair of axes the two fields really do share
            // one scale, and the warning is the point.
            AssertThat(log.WarningsContaining("share one scale").Length).IsEqual(2);
        }
        finally
        {
            log.Detach();
        }
    }

    /// <summary>Rows that carry a category and a value as well as a coordinate.</summary>
    private static List<DataRow> CoordinatesAsWellAsCategories() =>
    [
        TestContexts.Row(("category", "A"), ("value", 10.0), ("lon", 2.35), ("lat", 48.85)),
        TestContexts.Row(("category", "B"), ("value", 20.0), ("lon", 13.4), ("lat", 52.5)),
    ];

    // ── The geometry source check ──────────────────────────────────────────

    [TestCase]
    public void AFieldOverATopologicalGraphIsReportedOncePerMarkList()
    {
        var log = EngineMessageLog.Attach();
        try
        {
            var canvas = new FakeCanvas2D();
            using var chart = new Chart(canvas) { Width = 400f, Height = 300f };
            chart.Mark(new CountingGeoMark(GeoGeometrySource.Graph));
            chart.Mark(new CountingGeoMark(GeoGeometrySource.Feature, continuous: true));

            chart.Render();
            chart.Render(); // the pass runs again per layout change, the warning does not repeat
            AssertThat(log.WarningsContaining("continuous space").Length).IsEqual(1);

            // A new mark makes the combination a different one, so the check speaks again.
            chart.Mark(new CountingGeoMark(GeoGeometrySource.Feature));
            chart.Render();
            AssertThat(log.WarningsContaining("continuous space").Length).IsEqual(2);
        }
        finally
        {
            log.Detach();
        }
    }

    [TestCase]
    public void AFieldOverFeaturesIsNotReported()
    {
        var log = EngineMessageLog.Attach();
        try
        {
            var canvas = new FakeCanvas2D();
            using var chart = ChartWith(canvas,
                new CountingGeoMark(GeoGeometrySource.Feature),
                new CountingGeoMark(GeoGeometrySource.Feature, continuous: true));

            chart.Render();

            AssertThat(log.WarningsContaining("continuous space").Length).IsEqual(0);
        }
        finally
        {
            log.Detach();
        }
    }

    /// <summary>
    /// A geographic mark that counts how often its projection is built, which is the contract the cache
    /// exists for.
    /// </summary>
    private sealed class CountingGeoMark(GeoGeometrySource source = GeoGeometrySource.Feature,
                                         bool continuous = false) : GeoMark
    {
        private ProjectionCache<List<Vector2>> _cache;

        /// <inheritdoc />
        public override GeoGeometrySource Source => source;

        /// <inheritdoc />
        public override bool RequiresContinuousSpace => continuous;

        /// <summary>How often the projection was built.</summary>
        public int Builds { get; private set; }

        /// <summary>How many points the last projection held.</summary>
        public int LastPointCount { get; private set; }

        /// <summary>The viewport the chart handed out in the last render.</summary>
        public GeoViewport? SeenViewport { get; private set; }

        /// <inheritdoc />
        public override void Render(MarkContext ctx)
        {
            SeenViewport = ctx.GeoViewport;
            var points = GetProjection(ctx, ref _cache, context =>
            {
                Builds++;
                return context.GeoViewport is { } viewport
                    ? [viewport.Project(0.0, 0.0, context.Plot)]
                    : [];
            });
            LastPointCount = points.Count;
        }
    }

    /// <summary>A Cartesian mark that reports the viewport it was handed, as a control case.</summary>
    private sealed class CartesianProbe : Mark
    {
        /// <inheritdoc />
        public override MarkCoordinate Coordinate => MarkCoordinate.Cartesian;

        /// <summary>The viewport the chart handed out in the last render.</summary>
        public GeoViewport? SeenViewport { get; private set; }

        /// <inheritdoc />
        public override void Render(MarkContext ctx) => SeenViewport = ctx.GeoViewport;
    }
}
