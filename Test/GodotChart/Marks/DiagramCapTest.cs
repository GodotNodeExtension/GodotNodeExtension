namespace GodotNodeExtension.Tests.GodotChart.Marks;

using System;
using System.Collections.Generic;
using GdUnit4;
using Godot;
using GodotNodeExtension.Component.GodotChart;
using GodotNodeExtension.Component.GodotChart.Marks;
using GodotNodeExtension.Tests.Support;
using Support;
using static GdUnit4.Assertions;

/// <summary>
/// The node budget of the "one element per row" diagram marks: a treemap or a sankey draws a shape per row, so
/// the budget is what stands between a hundred-thousand-row table and a page at single-digit fps. Past the cap
/// the mark draws the first rows only and warns (once - the mark is rendered every frame); the rows it dropped
/// are neither painted nor hittable, because both walk the same capped layout.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class DiagramCapTest
{
    /// <summary>A treemap chart over <paramref name="rows"/> nodes, capped at <paramref name="maxNodes"/>.</summary>
    private static Chart TreemapChart(FakeCanvas2D canvas, int rows, int maxNodes)
    {
        var chart = new Chart(canvas) { Width = 300f, Height = 300f };
        var data = new List<DataRow> { TestContexts.Row(("label", "Root"), ("parent", ""), ("value", 0.0)) };
        for (int i = 0; i < rows; i++)
            data.Add(TestContexts.Row(("label", $"n{i}"), ("parent", "Root"), ("value", (double)(i + 1))));

        chart.Data(data);
        chart.Encode(Channel.X, "label");
        chart.Encode(Channel.Y, "value");
        chart.Mark(new TreemapMark { MaxNodes = maxNodes });
        return chart;
    }

    /// <summary>A sankey chart over <paramref name="rows"/> links, capped at <paramref name="maxNodes"/>.</summary>
    private static Chart SankeyChart(FakeCanvas2D canvas, int rows, int maxNodes)
    {
        var chart = new Chart(canvas) { Width = 300f, Height = 300f };
        var data = new List<DataRow>();
        for (int i = 0; i < rows; i++)
            data.Add(TestContexts.Row(("source", $"a{i}"), ("target", $"b{i}"), ("value", (double)(i + 1))));

        chart.Data(data);
        chart.Encode(Channel.Y, "value");
        chart.Mark(new SankeyMark { MaxNodes = maxNodes });
        return chart;
    }

    private static FakeCanvas2D Treemap(int rows, int maxNodes)
    {
        var canvas = new FakeCanvas2D();
        TreemapChart(canvas, rows, maxNodes).Render();
        return canvas;
    }

    private static FakeCanvas2D Sankey(int rows, int maxNodes)
    {
        var canvas = new FakeCanvas2D();
        SankeyChart(canvas, rows, maxNodes).Render();
        return canvas;
    }

    [TestCase]
    public void TheTreemapCapLimitsWhatIsDrawn()
    {
        var capped = Treemap(60, 8);
        var full = Treemap(60, 1000);

        AssertThat(full.DrewAnything).IsTrue();
        AssertThat(capped.Rects.Count < full.Rects.Count).IsTrue();
    }

    [TestCase]
    public void TheSankeyCapLimitsWhatIsDrawn()
    {
        var capped = Sankey(60, 8);
        var full = Sankey(60, 1000);

        AssertThat(full.DrewAnything).IsTrue();
        AssertThat(capped.StrokeCount + capped.FillCount).IsLess(full.StrokeCount + full.FillCount);
    }

    [TestCase]
    public void UnderTheCapNothingChanges()
    {
        var few = Treemap(6, 1000);
        var capped = Treemap(6, 8);

        AssertThat(capped.Rects.Count).IsEqual(few.Rects.Count);
    }

    /// <summary>
    /// The cap is the only sign the frame dropped rows, so it warns - once: these marks render every frame, and
    /// a warning per frame drowns the engine log. Under the cap both marks stay quiet.
    /// </summary>
    [TestCase]
    public void TheCapWarnsOncePerMarkAndNotUnderIt()
    {
        var log = EngineMessageLog.Attach();
        try
        {
            var treemap = TreemapChart(new FakeCanvas2D(), rows: 60, maxNodes: 8);
            treemap.Render();
            treemap.Render();

            var sankey = SankeyChart(new FakeCanvas2D(), rows: 60, maxNodes: 8);
            sankey.Render();
            sankey.Render();

            Treemap(6, 1000);                        // under the cap: nothing to report
            Sankey(6, 1000);

            var warnings = log.WarningsContaining("exceed");
            AssertThat(warnings.Length).IsEqual(2);  // one per mark, not one per frame
            AssertThat(warnings[0].Contains("TreemapMark", StringComparison.Ordinal)).IsTrue();
            AssertThat(warnings[1].Contains("SankeyMark", StringComparison.Ordinal)).IsTrue();
        }
        finally
        {
            log.Detach();
        }
    }

    /// <summary>
    /// A row the cap pushed out is not hittable: nothing was painted for it, and hit testing walks the same
    /// capped layout the drawing does.
    /// </summary>
    [TestCase]
    public void ARowPastTheCapIsNotHittable()
    {
        var capped = TreemapChart(new FakeCanvas2D(), rows: 60, maxNodes: 8);
        capped.Render();

        // The same scan on the uncapped chart reaches those rows, so a zero here is the cap and not an empty scan.
        var full = TreemapChart(new FakeCanvas2D(), rows: 60, maxNodes: 1000);
        full.Render();

        AssertThat(ScanPastRow(capped, 8)).IsEqual(0);
        AssertThat(ScanPastRow(full, 8)).IsGreater(0);
    }

    /// <summary>Grid-scan a chart and count the hits that land on a row index above <paramref name="row"/>.</summary>
    private static int ScanPastRow(Chart chart, int row)
    {
        int hits = 0;
        for (float y = 0f; y < 300f; y += 4f)
        {
            for (float x = 0f; x < 300f; x += 4f)
            {
                if (chart.HitTest(new Vector2(x, y)) is { } hit && hit.RowIndex > row) hits++;
            }
        }
        return hits;
    }
}
