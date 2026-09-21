namespace GodotNodeExtension.Tests.GodotChart.Marks;

using System.Collections.Generic;
using System.Linq;
using GdUnit4;
using Godot;
using GodotNodeExtension.Component.GodotChart;
using Support;
using static GdUnit4.Assertions;

/// <summary>
/// Cross-implementation invariants of the <see cref="Mark"/> subsystem.
/// <para>
/// Every mark in <see cref="MarkCases.All"/> (all shipped mark types) must obey the same
/// contract no matter which data model it renders: it draws something for a valid configuration,
/// it never emits a non-finite coordinate, it never emits a negative-size rectangle, and its hit
/// testing never reports a row index outside the data it just rendered.
/// </para>
/// <para>
/// Missing, named-null and non-finite values are treated as "no value" and skipped by every mark,
/// so these invariants hold for all implementations at once.
/// </para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class MarkRenderingInvariantsTest
{
    /// <summary>
    /// Whether a hit's reported position lies inside the plot rectangle, with a margin for the element's own
    /// size (a dot's radius, a ribbon's width).
    /// </summary>
    private static bool InPlot(Chart chart, HitResult hit, float margin = 24f)
    {
        if (chart.CurrentPlotArea is not { } plot) return true;   // nothing to compare against
        return hit.ScreenX >= plot.X - margin && hit.ScreenX <= plot.X + plot.Width + margin &&
               hit.ScreenY >= plot.Y - margin && hit.ScreenY <= plot.Y + plot.Height + margin;
    }

    private static string Report(IEnumerable<string> problems)
    {
        var list = problems.ToList();
        return list.Count == 0 ? "" : string.Join("\n", list);
    }

    [TestCase]
    public void EveryMarkDrawsSomething()
    {
        var problems = new List<string>();
        foreach (var c in MarkCases.All)
        {
            var canvas = new FakeCanvas2D();
            MarkCases.Build(canvas, c).Render();
            if (canvas.FillCount + canvas.StrokeCount == 0)
                problems.Add($"{c.Name}: nothing was drawn");
        }

        AssertThat(Report(problems)).IsEqual("");
    }

    [TestCase]
    public void NoMarkEmitsNonFiniteCoordinates()
    {
        var problems = new List<string>();
        foreach (var c in MarkCases.All)
        {
            var canvas = new FakeCanvas2D();
            MarkCases.Build(canvas, c).Render();
            if (canvas.NonFiniteCoordinateCount > 0)
                problems.Add($"{c.Name}: {canvas.NonFiniteCoordinateCount} non-finite coordinate(s)");
        }

        AssertThat(Report(problems)).IsEqual("");
    }

    [TestCase]
    public void NoMarkEmitsNegativeSizeRectangles()
    {
        var problems = new List<string>();
        foreach (var c in MarkCases.All)
        {
            var canvas = new FakeCanvas2D();
            MarkCases.Build(canvas, c).Render();
            if (canvas.NegativeSizeRectCount > 0)
                problems.Add($"{c.Name}: {canvas.NegativeSizeRectCount} negative-size rect(s)");
        }

        AssertThat(Report(problems)).IsEqual("");
    }

    [TestCase]
    public void HitTestNeverEscapesTheDataRange()
    {
        var problems = new List<string>();
        foreach (var c in MarkCases.All)
        {
            var canvas = new FakeCanvas2D();
            var chart = MarkCases.Build(canvas, c);
            chart.Render();

            bool found = false;
            for (float y = -20; y <= 320 && !found; y += 10)
            {
                for (float x = -20; x <= 420; x += 10)
                {
                    var hit = chart.HitTest(new Vector2(x, y));
                    if (hit is null) continue;
                    if (hit.RowIndex >= c.Data.Count)
                    {
                        problems.Add($"{c.Name}: hit RowIndex {hit.RowIndex} >= {c.Data.Count}");
                        found = true;
                        break;
                    }
                    // The reported position has to be where an element is: the scan starts outside the plot,
                    // and a hit that points past it (beyond the margin an element's own size needs) is a hit
                    // on nothing.
                    if (!InPlot(chart, hit))
                    {
                        problems.Add($"{c.Name}: hit reported ({hit.ScreenX:F0},{hit.ScreenY:F0}), outside the plot");
                        found = true;
                        break;
                    }
                }
            }
        }

        AssertThat(Report(problems)).IsEqual("");
    }
}
