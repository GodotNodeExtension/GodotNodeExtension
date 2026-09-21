namespace GodotNodeExtension.Tests.GodotChart;

using System;
using System.Linq;
using GdUnit4;
using GodotNodeExtension.Component.GodotChart;
using static GdUnit4.Assertions;

/// <summary>
/// Completeness of the integration matrix: <see cref="ChartRenderCase.All"/> has to describe every
/// <see cref="ChartKind"/> exactly once.
/// <para>
/// The integration suite iterates the matrix, so a kind that is added to the enum but not to the table
/// would silently lose its end-to-end coverage - the same failure mode the mark snapshot table guards
/// against with its key-set self-check. This case makes the two sets have to match.
/// </para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class ChartKindCoverageTest
{
    [TestCase]
    public void EveryChartKindHasExactlyOneRenderCase()
    {
        var kinds = Enum.GetValues<ChartKind>().OrderBy(k => k).ToArray();
        var covered = ChartRenderCase.All.Select(c => c.Kind).OrderBy(k => k).ToArray();

        var problems = "";
        if (!covered.SequenceEqual(kinds))
            problems = "only in ChartKind: " + string.Join(",", kinds.Except(covered)) +
                       "; only in ChartRenderCase.All: " + string.Join(",", covered.Except(kinds));
        if (covered.Distinct().Count() != covered.Length)
            problems += (problems.Length > 0 ? "; " : "") + "ChartRenderCase.All lists a kind twice";

        AssertThat(problems).IsEqual("");
    }
}
