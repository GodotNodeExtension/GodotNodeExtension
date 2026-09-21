namespace GodotNodeExtension.Tests.GodotChart.Marks;

using System;
using GdUnit4;
using Godot;
using GodotNodeExtension.Component.GodotChart;
using GodotNodeExtension.Component.GodotChart.Marks;
using GodotNodeExtension.Tests.Support;
using Support;
using static GdUnit4.Assertions;

/// <summary>
/// <see cref="HeatmapMark.MaxCells"/>: a heatmap draws one cell per row, so the row count is the element count.
/// Past the cap the mark draws the first rows only and warns (once - the mark is rendered every frame); the
/// rows past it keep their cells in hit testing, because a heatmap cell's place comes from the scales rather
/// than from the capped layout.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class HeatmapCapTest
{
    /// <summary>A heatmap chart of <paramref name="rows"/> cells, capped at <paramref name="maxCells"/>.</summary>
    private static Chart HeatmapChart(FakeCanvas2D canvas, int rows, int maxCells)
    {
        var chart = new Chart(canvas) { Width = 300f, Height = 300f };
        var data = new System.Collections.Generic.List<DataRow>(rows);
        for (int i = 0; i < rows; i++)
        {
            data.Add(TestContexts.Row(("c", $"c{i % 4}"), ("r", $"r{i / 4}"), ("v", (double)i)));
        }

        chart.Data(data);
        chart.Encode(Channel.X, "c");
        chart.Encode(Channel.Y, "r");
        chart.Encode(Channel.Color, "v");
        chart.Mark(new HeatmapMark { MaxCells = maxCells });
        return chart;
    }

    private static FakeCanvas2D Heatmap(int rows, int maxCells)
    {
        var canvas = new FakeCanvas2D();
        HeatmapChart(canvas, rows, maxCells).Render();
        return canvas;
    }

    [TestCase]
    public void TheCapLimitsHowManyCellsAreDrawn()
    {
        var capped = Heatmap(40, 6);
        var full = Heatmap(40, 1000);

        AssertThat(full.Rects.Count).IsGreater(0);
        AssertThat(capped.Rects.Count < full.Rects.Count).IsTrue();
    }

    [TestCase]
    public void UnderTheCapNothingChanges()
    {
        var few = Heatmap(4, 1000);
        var capped = Heatmap(4, 6);

        AssertThat(capped.Rects.Count).IsEqual(few.Rects.Count);
    }

    /// <summary>
    /// The cap warns once - the heatmap renders every frame, and the warning used to be pushed on each of them -
    /// and the rows past it answer hit testing, which is what <see cref="HeatmapMark.MaxCells"/> promises: the
    /// cell has a place in the grid, it is simply not painted.
    /// </summary>
    [TestCase]
    public void TheCapWarnsOnceAndItsRowsStayHittable()
    {
        var log = EngineMessageLog.Attach();
        try
        {
            var chart = HeatmapChart(new FakeCanvas2D(), rows: 40, maxCells: 6);
            chart.Render();
            chart.Render();                          // the second frame is the per-frame warning regression

            var warnings = log.WarningsContaining("MaxCells");
            AssertThat(warnings.Length).IsEqual(1);
            AssertThat(warnings[0].Contains("HeatmapMark", StringComparison.Ordinal)).IsTrue();

            // Cells are 4 columns x 10 rows, six of them inside the cap: the grid below lands on rows past it.
            int hits = 0, maxRow = -1;
            for (float y = 0f; y < 300f; y += 3f)
            {
                for (float x = 0f; x < 300f; x += 3f)
                {
                    if (chart.HitTest(new Vector2(x, y)) is { } hit)
                    {
                        hits++;
                        maxRow = Math.Max(maxRow, hit.RowIndex);
                    }
                }
            }

            AssertThat(hits).IsGreater(0);
            AssertThat(maxRow).IsGreater(6);          // a row the cap did not paint still answers
        }
        finally
        {
            log.Detach();
        }
    }
}
