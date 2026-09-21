namespace GodotNodeExtension.Tests.GodotChart.Marks;

using System.Collections.Generic;
using GdUnit4;
using GodotNodeExtension.Component.GodotChart;
using GodotNodeExtension.Component.GodotChart.Marks;
using Support;
using static GdUnit4.Assertions;

/// <summary>
/// Display-level reduction of a line series (<see cref="LineMark.Decimate"/>): the drawn path has to stop
/// growing with the table once the plot cannot resolve the points any more, while nothing about the data the
/// reader is shown changes (hit testing and tooltips keep reading the full rows).
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class LineMarkDecimationTest
{
    private const int PointCount = 5_000;

    /// <summary>A flat series with one spike, big enough that a 400 px plot cannot resolve every point.</summary>
    private static (FakeCanvas2D Canvas, Chart Chart) Series(DecimateMode mode)
    {
        var canvas = new FakeCanvas2D();
        var rows = new List<DataRow>(PointCount);
        for (int i = 0; i < PointCount; i++)
            rows.Add(TestContexts.Row(("x", (double)i), ("y", i == PointCount / 2 ? 100.0 : 10.0)));

        var chart = new Chart(canvas) { Width = 400f, Height = 300f };
        chart.Data(rows);
        chart.Encode(Channel.X, "x");
        chart.Encode(Channel.Y, "y");
        chart.Mark(new LineMark { Smooth = false, Decimate = mode });
        chart.Render();
        return (canvas, chart);
    }

    /// <summary>Auto reduces a table the plot cannot resolve: the path stays proportional to the canvas.</summary>
    [TestCase]
    public void AutoCapsTheDrawnPath()
    {
        var (reduced, chart) = Series(DecimateMode.Auto);
        int columns = (int)chart.CurrentPlotArea!.Value.Width;

        AssertThat(reduced.StrokeCount).IsGreater(0);
        AssertThat(reduced.PathOpCount).IsLess(PointCount / 4);
        AssertThat(reduced.PathOpCount <= columns * 2 + 4).IsTrue();
    }

    /// <summary>Off draws the table as it is - the control case the cap above is measured against.</summary>
    [TestCase]
    public void OffDrawsEveryPoint()
    {
        var (full, _) = Series(DecimateMode.Off);

        AssertThat(full.StrokeCount).IsGreater(0);
        AssertThat(full.PathOpCount).IsGreater(PointCount / 2);
    }

    /// <summary>
    /// On reduces a series that Auto leaves alone. The two modes differ only in the threshold they respect:
    /// raising <see cref="LineMark.DecimatePointsPerPixel"/> out of reach makes Auto draw every point while
    /// On still buckets the columns.
    /// </summary>
    [TestCase]
    public void OnReducesASeriesAutoLeavesAlone()
    {
        static int PathOps(DecimateMode mode)
        {
            var canvas = new FakeCanvas2D();
            var rows = new List<DataRow>();
            for (int i = 0; i < 6_000; i++) rows.Add(TestContexts.Row(("x", (double)i), ("y", (double)(i % 5))));

            var chart = new Chart(canvas) { Width = 440f, Height = 300f };
            chart.Data(rows);
            chart.Encode(Channel.X, "x");
            chart.Encode(Channel.Y, "y");
            chart.Mark(new LineMark
            {
                Smooth = false,
                Decimate = mode,
                DecimatePointsPerPixel = 1_000f,   // Auto stays out of the way; On ignores the threshold
            });
            chart.Render();
            return canvas.PathOpCount;
        }

        int auto = PathOps(DecimateMode.Auto);
        int on = PathOps(DecimateMode.On);

        AssertThat(on).IsGreater(0);
        AssertThat(on < auto).IsTrue();
    }
}
