namespace GodotNodeExtension.Tests.GodotChart.Geo;

using System.Collections.Generic;
using System.Linq;
using GdUnit4;
using Godot;
using GodotNodeExtension.Component.GodotChart;
using Support;
using static GdUnit4.Assertions;

/// <summary>
/// Behaviour specification for <see cref="GeoFlowMark"/>: one curve per row between the two coordinates the
/// row carries, the size channel driving the width, a broken end skipping only its own row, the curve bending
/// away from the straight line, and the hit test that reads the row back out.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class GeoFlowMarkTest
{
    /// <summary>Three routes inside the framed window, with three different weights.</summary>
    private static List<DataRow> Routes() =>
    [
        TestContexts.Row(("start_lon", 2.35), ("start_lat", 48.85), ("end_lon", 13.4), ("end_lat", 52.5), ("value", 20.0)),
        TestContexts.Row(("start_lon", -9.1), ("start_lat", 38.7), ("end_lon", 4.9), ("end_lat", 52.37), ("value", 60.0)),
        TestContexts.Row(("start_lon", 12.5), ("start_lat", 41.9), ("end_lon", 16.37), ("end_lat", 48.21), ("value", 100.0)),
    ];

    private static Chart ChartWith(FakeCanvas2D canvas, Mark mark, List<DataRow> rows, bool bindSize = true)
    {
        var chart = new Chart(canvas) { Width = 400f, Height = 300f };
        chart.Data(rows);
        chart.Mark(mark);
        chart.Encode(Channel.X, "start_lon");
        chart.Encode(Channel.Y, "start_lat");
        if (bindSize) chart.Encode(Channel.Size, "value");
        chart.FitGeoBounds(-14.0, 34.0, 22.0, 57.0, new PlotArea(0f, 0f, 400f, 300f), 0.05f);
        return chart;
    }

    /// <summary>Draw one chart and report what the mark itself stroked (the baseline draws the background).</summary>
    private static (int Strokes, FakeCanvas2D Canvas) Draw(Mark mark, List<DataRow> rows, bool bindSize = true)
    {
        var baselineCanvas = new FakeCanvas2D();
        using (var baseline = ChartWith(baselineCanvas, new GeoAreaMark { Features = [] }, rows, bindSize))
            baseline.Render();

        var canvas = new FakeCanvas2D();
        using (var chart = ChartWith(canvas, mark, rows, bindSize)) chart.Render();
        return (canvas.StrokeCount - baselineCanvas.StrokeCount, canvas);
    }

    /// <summary>A point on the curve of a row, half way between its ends in screen space.</summary>
    private static Vector2 Midpoint(Chart chart, DataRow row)
    {
        var viewport = chart.GeoViewport!;
        var plot = chart.CurrentPlotArea!.Value;
        var start = viewport.Project(row.Get<double>("start_lon"), row.Get<double>("start_lat"), plot);
        var end = viewport.Project(row.Get<double>("end_lon"), row.Get<double>("end_lat"), plot);
        return (start + end) / 2f;
    }

    // ── One curve per row ──────────────────────────────────────────────────

    [TestCase]
    public void EveryRowWithTwoCoordinatesBecomesACurve()
    {
        var (strokes, canvas) = Draw(new GeoFlowMark(), Routes());

        AssertThat(strokes).IsEqual(3);
        AssertThat(canvas.NonFiniteCoordinateCount).IsEqual(0);
    }

    [TestCase]
    public void ARowWithABrokenEndIsSkippedAndTheRestKeepDrawing()
    {
        var rows = Routes();
        rows[1].Set("end_lon", double.NaN);

        var (strokes, canvas) = Draw(new GeoFlowMark(), rows);

        AssertThat(strokes).IsEqual(2);
        AssertThat(canvas.NonFiniteCoordinateCount).IsEqual(0);
    }

    [TestCase]
    public void ARowWithoutTheCoordinateFieldsIsSkipped()
    {
        var rows = new List<DataRow> { TestContexts.Row(("route", "LIS-CDG"), ("value", 20.0)) };

        var (strokes, _) = Draw(new GeoFlowMark(), rows);

        AssertThat(strokes).IsEqual(0);
    }

    [TestCase]
    public void TheFieldNamesAreConfigurable()
    {
        var rows = new List<DataRow>
        {
            TestContexts.Row(("from_x", 2.35), ("from_y", 48.85), ("to_x", 13.4), ("to_y", 52.5), ("value", 30.0)),
        };
        var mark = new GeoFlowMark
        {
            SourceLonField = "from_x",
            SourceLatField = "from_y",
            TargetLonField = "to_x",
            TargetLatField = "to_y",
        };

        var (strokes, _) = Draw(mark, rows);

        AssertThat(strokes).IsEqual(1);
    }

    // ── Width ──────────────────────────────────────────────────────────────

    [TestCase]
    public void TheSizeChannelDrivesTheWidth()
    {
        var canvas = new FakeCanvas2D();
        var mark = new GeoFlowMark { MinWidth = 2f, MaxWidth = 20f };
        using var chart = ChartWith(canvas, mark, Routes());
        chart.Render();

        // The widths the mark asked the canvas to stroke with, in the order the rows were drawn.
        var widths = canvas.StrokeWidths.ToList();
        AssertThat(widths.Count).IsEqual(3);
        AssertThat(widths.Min()).IsEqual(2f);
        AssertThat(widths.Max()).IsEqual(20f);
    }

    [TestCase]
    public void WithoutASizeChannelEveryCurveGetsTheSmallestWidth()
    {
        var canvas = new FakeCanvas2D();
        var mark = new GeoFlowMark { MinWidth = 3f, MaxWidth = 20f };
        using var chart = ChartWith(canvas, mark, Routes(), bindSize: false);
        chart.Render();

        AssertThat(canvas.StrokeWidths.All(w => w == 3f)).IsTrue();
    }

    // ── Shape and hit testing ──────────────────────────────────────────────

    [TestCase]
    public void TheCurveBendsAwayFromTheStraightLine()
    {
        var row = Routes()[0];

        // Straight: the middle of the line is on the curve.
        var straightCanvas = new FakeCanvas2D();
        using (var straight = ChartWith(straightCanvas, new GeoFlowMark { Curvature = 0f }, Routes()))
        {
            straight.Render();
            AssertThat(straight.HitTest(Midpoint(straight, row)) is not null).IsTrue();
        }

        // Bent: the same point is no longer on the curve (it moved aside), and the curve is still there to be
        // hit somewhere near it.
        var bentCanvas = new FakeCanvas2D();
        using var bent = ChartWith(bentCanvas, new GeoFlowMark { Curvature = 0.4f }, Routes());
        bent.Render();

        var mid = Midpoint(bent, row);
        AssertThat(bent.HitTest(mid) is null).IsTrue();

        bool hitNearby = false;
        for (float dx = -30f; dx <= 30f && !hitNearby; dx += 5f)
        for (float dy = -30f; dy <= 30f && !hitNearby; dy += 5f)
            hitNearby = bent.HitTest(mid + new Vector2(dx, dy)) is not null;
        AssertThat(hitNearby).IsTrue();
    }

    [TestCase]
    public void AHitReportsTheRowTheCurveCameFrom()
    {
        var canvas = new FakeCanvas2D();
        var mark = new GeoFlowMark();
        using var chart = ChartWith(canvas, mark, Routes());
        chart.Render();

        // The curve bends away from the straight line, so the probe walks a small window around the line's
        // midpoint until it lands on the curve (the row it reports is what this case is about).
        var mid = Midpoint(chart, Routes()[1]);
        HitResult? hit = null;
        for (float dx = -40f; dx <= 40f && hit is null; dx += 5f)
        for (float dy = -40f; dy <= 40f && hit is null; dy += 5f)
            hit = chart.HitTest(mid + new Vector2(dx, dy));

        AssertThat(hit is not null).IsTrue();
        AssertThat(hit!.RowIndex).IsEqual(1);
        AssertThat(hit.Row!.Get<double>("value")).IsEqual(60.0);
        AssertThat(hit.MarkType).IsEqual(nameof(GeoFlowMark));
    }

    [TestCase]
    public void AClickAwayFromTheCurvesHitsNothing()
    {
        var canvas = new FakeCanvas2D();
        var mark = new GeoFlowMark();
        using var chart = ChartWith(canvas, mark, Routes());
        chart.Render();

        AssertThat(chart.HitTest(new Vector2(10f, 290f)) is null).IsTrue();
    }
}
