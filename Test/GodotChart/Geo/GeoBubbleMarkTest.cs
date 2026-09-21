namespace GodotNodeExtension.Tests.GodotChart.Geo;

using System;
using System.Collections.Generic;
using System.Linq;
using GdUnit4;
using Godot;
using GodotNodeExtension.Component.GodotChart;
using Support;
using static GdUnit4.Assertions;

/// <summary>
/// Behaviour specification for <see cref="GeoBubbleMark"/>: one bubble per row at the coordinate the row
/// carries, the size channel driving the radius, a broken coordinate skipping only its own row, the colour
/// coming from the row, and the hit test that reads the row back out.
/// <para>
/// The radius is measured through what the pointer can reach rather than by reading the drawing calls: a hit
/// at a fixed distance from a bubble's centre is a statement about the bubble's size that cannot pass by
/// accident.
/// </para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class GeoBubbleMarkTest
{
    /// <summary>Three cities inside the framed window below, with three different values.</summary>
    private static List<DataRow> Cities() =>
    [
        TestContexts.Row(("lon", 2.35), ("lat", 48.85), ("value", 20.0)),
        TestContexts.Row(("lon", 4.9), ("lat", 52.37), ("value", 60.0)),
        TestContexts.Row(("lon", 13.4), ("lat", 52.5), ("value", 100.0)),
    ];

    private static Chart ChartWith(FakeCanvas2D canvas, Mark mark, List<DataRow> rows,
                                   bool bindSize = true, bool bindColor = false)
    {
        var chart = new Chart(canvas) { Width = 400f, Height = 300f };
        chart.Data(rows);
        chart.Mark(mark);
        chart.Encode(Channel.X, "lon");
        chart.Encode(Channel.Y, "lat");
        if (bindSize) chart.Encode(Channel.Size, "value");
        if (bindColor) chart.Encode(Channel.Color, "value");
        // The framed view a page would get: the coordinates of the rows with room around them.
        chart.FitGeoBounds(-12.0, 35.0, 26.0, 60.0, new PlotArea(0f, 0f, 400f, 300f), 0.05f);
        return chart;
    }

    /// <summary>
    /// Draw one chart and report what the mark itself added: the baseline is the same chart with a geographic
    /// mark that draws nothing (an area mark without features), so the background and everything else a frame
    /// always draws cancels out and the map decorations stay off.
    /// </summary>
    private static (int Fills, int Strokes, FakeCanvas2D Canvas) Draw(GeoBubbleMark mark, List<DataRow> rows,
                                                                     bool bindSize = true, bool bindColor = false)
    {
        var baselineCanvas = new FakeCanvas2D();
        using (var baseline = ChartWith(baselineCanvas, new GeoAreaMark { Features = [] }, rows, bindSize, bindColor))
            baseline.Render();

        var canvas = new FakeCanvas2D();
        using (var chart = ChartWith(canvas, mark, rows, bindSize, bindColor)) chart.Render();
        return (canvas.FillCount - baselineCanvas.FillCount,
                canvas.StrokeCount - baselineCanvas.StrokeCount,
                canvas);
    }

    /// <summary>The screen position of a row's bubble: its coordinate projected through the chart's own view.</summary>
    private static Vector2 BubbleAt(Chart chart, double lon, double lat)
        => chart.GeoViewport!.Project(lon, lat, chart.CurrentPlotArea!.Value);

    // ── One bubble per row ─────────────────────────────────────────────────

    [TestCase]
    public void EveryRowWithACoordinateBecomesABubble()
    {
        var (fills, _, canvas) = Draw(new GeoBubbleMark(), Cities());

        AssertThat(fills).IsEqual(3);
        AssertThat(canvas.NonFiniteCoordinateCount).IsEqual(0);
    }

    [TestCase]
    public void ARowWithoutACoordinateIsSkippedAndTheRestKeepDrawing()
    {
        var rows = Cities();
        rows[1].Set("lat", double.NaN); // one broken row in the middle of the table

        var (fills, _, canvas) = Draw(new GeoBubbleMark(), rows);

        AssertThat(fills).IsEqual(2);
        AssertThat(canvas.NonFiniteCoordinateCount).IsEqual(0);
    }

    // ── Size ───────────────────────────────────────────────────────────────

    [TestCase]
    public void TheSizeChannelDrivesTheRadius()
    {
        var canvas = new FakeCanvas2D();
        var mark = new GeoBubbleMark { MinRadius = 6f, MaxRadius = 30f };
        using var chart = ChartWith(canvas, mark, Cities());
        chart.Render();

        var small = BubbleAt(chart, 2.35, 48.85);   // the smallest value of the table
        var big = BubbleAt(chart, 13.4, 52.5);      // the largest one

        // 12 px below each centre: inside the big bubble, outside the small one.
        AssertThat(chart.HitTest(big + new Vector2(0f, 12f)) is not null).IsTrue();
        AssertThat(chart.HitTest(small + new Vector2(0f, 12f)) is null).IsTrue();
    }

    [TestCase]
    public void WithoutASizeChannelEveryBubbleGetsTheSmallestRadius()
    {
        var canvas = new FakeCanvas2D();
        var mark = new GeoBubbleMark { MinRadius = 9f, MaxRadius = 30f };
        using var chart = ChartWith(canvas, mark, Cities(), bindSize: false);
        chart.Render();

        var first = BubbleAt(chart, 2.35, 48.85);

        // The bubbles are all the minimum size: 6 px away hits, 14 px away (well inside the maximum) misses.
        AssertThat(chart.HitTest(first + new Vector2(0f, 6f)) is not null).IsTrue();
        AssertThat(chart.HitTest(first + new Vector2(0f, 14f)) is null).IsTrue();
    }

    [TestCase]
    public void AZeroBorderStillFillsTheBubble()
    {
        var (fills, strokes, _) = Draw(new GeoBubbleMark { StrokeWidth = 0f }, Cities());

        AssertThat(fills).IsEqual(3);
        AssertThat(strokes).IsEqual(0);
    }

    // ── Hit testing ────────────────────────────────────────────────────────

    [TestCase]
    public void AHitReportsTheRowTheBubbleCameFrom()
    {
        var canvas = new FakeCanvas2D();
        var mark = new GeoBubbleMark();
        using var chart = ChartWith(canvas, mark, Cities());
        chart.Render();

        var centre = BubbleAt(chart, 2.35, 48.85);
        var hit = chart.HitTest(centre);

        AssertThat(hit is not null).IsTrue();
        AssertThat(hit!.RowIndex).IsEqual(0);
        AssertThat(hit.Row!.Get<double>("value")).IsEqual(20.0);
        AssertThat(hit.MarkType).IsEqual(nameof(GeoBubbleMark));
        AssertThat(hit.ScreenX).IsEqual(centre.X);
    }

    [TestCase]
    public void AClickBetweenTheBubblesHitsNothing()
    {
        var canvas = new FakeCanvas2D();
        var mark = new GeoBubbleMark();
        using var chart = ChartWith(canvas, mark, Cities());
        chart.Render();

        // A corner of the plot: no bubble reaches it.
        AssertThat(chart.HitTest(new Vector2(20f, 20f)) is null).IsTrue();
    }

    // ── Colour ─────────────────────────────────────────────────────────────

    [TestCase]
    public void TheColourComesFromTheRow()
    {
        var (fills, _, canvas) = Draw(new GeoBubbleMark(), Cities(), bindColor: true);

        AssertThat(fills).IsEqual(3);
        AssertThat(canvas.FillColors.Take(3).Distinct().Count()).IsEqual(3);
    }
}
