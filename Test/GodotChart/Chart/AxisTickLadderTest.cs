namespace GodotNodeExtension.Tests.GodotChart;

using System.Collections.Generic;
using System.Globalization;
using GdUnit4;
using GodotNodeExtension.Component.GodotChart;
using GodotNodeExtension.Component.GodotChart.Marks;
using Support;
using static GdUnit4.Assertions;

/// <summary>
/// The tick budget of a value axis, seen from the labels it draws: when the automatic ladder is finer than the
/// count the caller asked for, the axis switches to a coarser rung of the {1, 2, 5} x 10^n ladder instead of
/// dropping entries from the fine one (dropping entries leaves one pair adjacent and one gap, which a reader
/// sees as a missing label - 10/20/40/50 with 30 gone). See <c>DefaultRenderers.LadderTicks</c>.
/// <para>
/// The axis here is pinned to the domain 10..90 and the data holds every ten of it, so the labels can be
/// written out by hand: a budget of four has to read 20/40/60/80, a budget of nine has to read 10..90.
/// </para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class AxisTickLadderTest
{
    /// <summary>
    /// The Y axis labels the chart draws with the given tick budget. Both the node and the plot are tall on
    /// purpose: an axis also thins labels that do not fit, while these cases are about the tick set itself, so
    /// every tick has to be drawn for the labels to say anything.
    /// </summary>
    private static List<string> YLabels(int tickCount)
    {
        var canvas = new FakeCanvas2D();
        var chart = new Chart(canvas) { Width = 700f, Height = 900f };
        chart.YAxis(new AxisConfig { TickCount = tickCount });
        chart.Encode(Channel.X, "x");
        chart.Encode(Channel.Y, "y");
        chart.Mark(new LineMark());
        chart.ScaleDomain(Channel.Y, 10, 90);

        var rows = new List<DataRow>();
        for (int value = 10; value <= 90; value += 10)
            rows.Add(TestContexts.Row(("x", (double)value), ("y", (double)value)));
        chart.Data(rows);
        chart.Render();

        return [.. canvas.YAxisLabels];
    }

    /// <summary>
    /// Four labels over 10..90: the fine ladder (every two) does not fit, and the coarser one that does is 20 -
    /// four labels, evenly spaced, with no gap in the middle and no end missing.
    /// </summary>
    [TestCase]
    public void AFourTickBudgetSwitchesToACoarserEvenLadder()
    {
        var labels = YLabels(4);

        AssertThat(labels.Count).IsEqual(4);
        AssertThat(labels).Contains("20");
        AssertThat(labels).Contains("40");
        AssertThat(labels).Contains("60");
        AssertThat(labels).Contains("80");
    }

    /// <summary>A budget of nine covers 10..90 exactly: the ladder rung is ten, and every end is labelled.</summary>
    [TestCase]
    public void ANineTickBudgetLabelsEveryTen()
    {
        var labels = YLabels(9);

        AssertThat(labels.Count).IsEqual(9);
        for (int value = 10; value <= 90; value += 10)
            AssertThat(labels).Contains(value.ToString("0", CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// A budget the ladder cannot satisfy (three labels cannot cover 10..90 evenly) keeps the fine ladder
    /// rather than the budget: both ends of the domain stay labelled, which is the part a reader needs. The
    /// count is the least important of the three, so it is only checked to be a usable axis.
    /// </summary>
    [TestCase]
    public void ABudgetTheLadderCannotMeetKeepsBothEnds()
    {
        var labels = YLabels(3);

        AssertThat(labels.Count).IsGreater(2);
        AssertThat(labels[0]).IsEqual("10");
        AssertThat(labels[^1]).IsEqual("90");
    }
}
