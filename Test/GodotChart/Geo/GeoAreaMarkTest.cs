namespace GodotNodeExtension.Tests.GodotChart.Geo;

using System;
using System.Collections.Generic;
using System.Linq;
using GdUnit4;
using Godot;
using GodotNodeExtension.Component.GodotChart;
using GodotNodeExtension.Tests.Support;
using Support;
using static GdUnit4.Assertions;

/// <summary>
/// Behaviour specification for <see cref="GeoAreaMark"/>: shading regions by the row that joins to them,
/// the visible "no data" fill, the geometry-layer mode (no colour channel: outlines only), holes, and the
/// hit test that turns a pointer position back into a row.
/// <para>
/// The cases drive the mark through a <see cref="Chart"/>, so the plot rectangle, the scales and the
/// viewport are the ones a real chart hands out: a hit test at a screen position is then comparing against
/// exactly what was drawn.
/// </para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class GeoAreaMarkTest
{
    /// <summary>
    /// A plane frame of 0..100 units, so the geometry below is readable as pixels: the cases centre the
    /// viewport on the middle of a 0..100 square, whose hole sits where the chart's centre is.
    /// </summary>
    private static Chart ChartWith(FakeCanvas2D canvas, GeoAreaMark mark, List<DataRow> rows, bool shade = true)
    {
        var chart = new Chart(canvas) { Width = 400f, Height = 300f };
        chart.SetGeoFrame(GeoFrames.CustomPlane(0.0, 0.0, 100.0, 100.0));
        // No legend: the cases count the mark's own fills, and a categorical colour channel would add a
        // swatch per value (the legend itself is covered elsewhere).
        chart.Legend(new LegendConfig { Position = LegendPosition.None });
        chart.Data(rows);
        chart.Mark(mark);
        chart.Encode(Channel.X, "cat");
        chart.Encode(Channel.Y, "value");
        if (shade) chart.Encode(Channel.Color, "value");
        chart.SetGeoViewport(50.0, 50.0, ZoomForAbout120Units());
        return chart;
    }

    /// <summary>
    /// Draw one chart and report what the mark itself added: the same chart without a mark draws the
    /// background (and whatever else a frame always draws), which is not what these cases are about.
    /// </summary>
    private static (int Fills, int Strokes, FakeCanvas2D Canvas) Draw(GeoAreaMark mark, List<DataRow> rows,
                                                                     bool shade = true)
    {
        var baselineCanvas = new FakeCanvas2D();
        using (var baseline = ChartWith(baselineCanvas, new GeoAreaMark { Features = [], RowField = "cat" },
                                        rows, shade))
        {
            baseline.Render();
        }

        var canvas = new FakeCanvas2D();
        using (var chart = ChartWith(canvas, mark, rows, shade)) chart.Render();
        return (canvas.FillCount - baselineCanvas.FillCount,
                canvas.StrokeCount - baselineCanvas.StrokeCount,
                canvas);
    }

    /// <summary>Zoom that shows roughly a quarter more than the 100 unit square across a 400 px plot.</summary>
    private static double ZoomForAbout120Units() => Math.Log2(100.0 / 120.0 * 400.0 / 256.0);

    /// <summary>Two square regions in the frame's units, side by side.</summary>
    private static List<GeoFeature> TwoAreas()
    {
        var builder = new GeoGeometryBuilder();
        return
        [
            builder.Polygon((10, 35), (50, 35), (50, 65), (10, 65)).Feature("A", "A"),
            builder.Polygon((55, 35), (95, 35), (95, 65), (55, 65)).Feature("B", "B"),
        ];
    }

    /// <summary>One big square with a small square hole where the chart's centre is.</summary>
    private static List<GeoFeature> Donut() =>
    [
        new GeoGeometryBuilder()
            .Polygon((10, 10), (90, 10), (90, 90), (10, 90))
            .Hole((45, 45), (45, 55), (55, 55), (55, 45))
            .Feature("A", "A"),
    ];

    private static List<DataRow> TwoRows() =>
    [
        TestContexts.Row(("cat", "A"), ("value", 10.0)),
        TestContexts.Row(("cat", "B"), ("value", 90.0)),
    ];

    /// <summary>A screen position inside region A, well away from its edges.</summary>
    private static Vector2 InsideA() => new(150f, 150f);

    // ── Shading ────────────────────────────────────────────────────────────

    [TestCase]
    public void EveryRegionIsFilledWithTheColourOfItsRow()
    {
        var mark = new GeoAreaMark { Features = TwoAreas(), RowField = "cat" };

        var (fills, _, canvas) = Draw(mark, TwoRows());

        AssertThat(fills).IsEqual(2);
        AssertThat(canvas.FillColors[0]).IsNotEqual(canvas.FillColors[1]); // the value shades them
        AssertThat(canvas.NonFiniteCoordinateCount).IsEqual(0);
    }

    [TestCase]
    public void ARegionWithoutARowIsFilledWithTheNoDataColour()
    {
        var mark = new GeoAreaMark { Features = TwoAreas(), RowField = "cat", Missing = GeoJoinMissing.Silent };

        var (fills, _, canvas) = Draw(mark, [TestContexts.Row(("cat", "A"), ("value", 10.0))]);

        AssertThat(fills).IsEqual(2);
        AssertThat(canvas.FillColors.Any(c => c == mark.NoDataColor)).IsTrue();
    }

    [TestCase]
    public void WithoutAColourChannelOnlyTheOutlinesAreDrawn()
    {
        var mark = new GeoAreaMark { Features = TwoAreas(), RowField = "cat" };

        var (fills, strokes, canvas) = Draw(mark, TwoRows(), shade: false);

        // The geometry layer of a two-stage map: no fill at all, one outline per region.
        AssertThat(fills).IsEqual(0);
        AssertThat(strokes).IsEqual(2);
        AssertThat(canvas.StrokeColors.Any(c => c == mark.OutlineColor)).IsTrue();
    }

    [TestCase]
    public void AZeroBorderDrawsNoOutline()
    {
        var mark = new GeoAreaMark { Features = TwoAreas(), RowField = "cat", StrokeWidth = 0f };

        var (fills, strokes, _) = Draw(mark, TwoRows());

        AssertThat(fills).IsEqual(2);
        AssertThat(strokes).IsEqual(0);
    }

    // ── Holes ──────────────────────────────────────────────────────────────

    [TestCase]
    public void AHoleIsNeitherFilledNorHit()
    {
        var canvas = new FakeCanvas2D();
        var mark = new GeoAreaMark { Features = Donut(), RowField = "cat" };
        using var chart = ChartWith(canvas, mark, [TestContexts.Row(("cat", "A"), ("value", 10.0))]);
        chart.Render();

        var centre = new Vector2(200f, 150f);          // the hole sits here
        var inRing = centre + new Vector2(-60f, 0f);   // inside the square, outside the hole

        AssertThat(chart.HitTest(centre) is null).IsTrue();
        var hit = chart.HitTest(inRing);
        AssertThat(hit is not null).IsTrue();
        AssertThat(hit!.RowIndex).IsEqual(0);
    }

    // ── Hit testing ────────────────────────────────────────────────────────

    [TestCase]
    public void AHitReportsTheJoinedRowAndTheMarkItCameFrom()
    {
        var canvas = new FakeCanvas2D();
        var mark = new GeoAreaMark { Features = TwoAreas(), RowField = "cat" };
        using var chart = ChartWith(canvas, mark, TwoRows());
        chart.Render();

        var hit = chart.HitTest(InsideA());

        AssertThat(hit is not null).IsTrue();
        AssertThat(hit!.RowIndex).IsEqual(0);
        AssertThat(hit.Row!.Get<string>("cat")).IsEqual("A");
        AssertThat(hit.Label).Contains("A");
        AssertThat(hit.MarkType).IsEqual(nameof(GeoAreaMark));
    }

    [TestCase]
    public void ARegionWithoutARowStillHits()
    {
        var canvas = new FakeCanvas2D();
        var mark = new GeoAreaMark { Features = TwoAreas(), RowField = "cat", Missing = GeoJoinMissing.Silent };
        using var chart = ChartWith(canvas, mark, [TestContexts.Row(("cat", "A"), ("value", 10.0))]);
        chart.Render();

        // The screen position of region B: no row joined to it, and it is still a region.
        var hit = chart.HitTest(new Vector2(330f, 150f));

        AssertThat(hit is not null).IsTrue();
        AssertThat(hit!.Row is null).IsTrue();
        AssertThat(hit.RowIndex).IsEqual(-1);
    }

    [TestCase]
    public void MovingTheMapMovesTheRegions()
    {
        var canvas = new FakeCanvas2D();
        var mark = new GeoAreaMark { Features = TwoAreas(), RowField = "cat" };
        using var chart = ChartWith(canvas, mark, TwoRows());
        chart.Render();
        AssertThat(chart.HitTest(InsideA()) is not null).IsTrue();

        chart.SetGeoViewport(95.0, 95.0, chart.GeoViewport!.ZoomLevel);
        chart.Render();

        AssertThat(chart.HitTest(InsideA()) is null).IsTrue();
    }

    // ── What the join left behind ──────────────────────────────────────────

    [TestCase]
    public void AMismatchedJoinIsReportedOnce()
    {
        var log = EngineMessageLog.Attach();
        try
        {
            var canvas = new FakeCanvas2D();
            var mark = new GeoAreaMark { Features = TwoAreas(), RowField = "cat" };
            using var chart = ChartWith(canvas, mark, [TestContexts.Row(("cat", "Nowhere"), ("value", 10.0))]);

            chart.Render();
            chart.Render(); // the projection is rebuilt per layout change; the warning is not repeated

            var warnings = log.WarningsContaining(nameof(GeoAreaMark));
            AssertThat(warnings.Length).IsEqual(1);
            AssertThat(warnings[0].Contains("no-data", StringComparison.Ordinal)).IsTrue();
        }
        finally
        {
            log.Detach();
        }
    }

    [TestCase]
    public void SilentDoesNotReport()
    {
        var log = EngineMessageLog.Attach();
        try
        {
            var canvas = new FakeCanvas2D();
            var mark = new GeoAreaMark { Features = TwoAreas(), RowField = "cat", Missing = GeoJoinMissing.Silent };
            using var chart = ChartWith(canvas, mark, [TestContexts.Row(("cat", "Nowhere"), ("value", 10.0))]);

            chart.Render();

            AssertThat(log.WarningsContaining(nameof(GeoAreaMark)).Length).IsEqual(0);
        }
        finally
        {
            log.Detach();
        }
    }
}
