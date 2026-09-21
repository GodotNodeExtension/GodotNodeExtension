namespace GodotNodeExtension.Tests.GodotChart;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using GdUnit4;
using Godot;
using GodotNodeExtension.Component.GodotChart;
using static GdUnit4.Assertions;

/// <summary>
/// The Basics page is the sheet that shows every chart kind the library ships - one cell per kind, all of
/// them plain <see cref="ChartView"/> nodes in the scene. A kind that exists in the enum but is not on that
/// page is a kind nobody can look at, and the specialization pages (the mark-knob galleries, the hand-built
/// ones) are not a substitute for it: they are allowed to go deeper, not to be the only place a kind appears.
/// <para>
/// The case reads the scene rather than instantiating it, because what it guards is the coverage of the
/// sheet itself - <c>ExampleScenesIntegrationTest</c> is what proves the pages actually run.
/// </para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class BasicsKindCoverageTest
{
    /// <summary>The sheet that has to show every kind.</summary>
    private const string ScenePath = "res://Example/GodotChart/BasicsDemo.tscn";

    /// <summary>A <c>Kind = 12</c> line of a ChartView node in a scene file.</summary>
    private static readonly Regex KindProperty = new(@"^Kind = (\d+)$", RegexOptions.Multiline);

    /// <summary>Kinds the scene configures, in file order.</summary>
    private static List<ChartKind> KindsInScene()
    {
        var kinds = new List<ChartKind>();

        using var handle = FileAccess.Open(ScenePath, FileAccess.ModeFlags.Read);
        if (handle is null) return kinds;

        foreach (Match match in KindProperty.Matches(handle.GetAsText()))
        {
            int value = match.Groups[1].Value.ToInt();
            if (Enum.IsDefined(typeof(ChartKind), value)) kinds.Add((ChartKind)value);
        }
        return kinds;
    }

    /// <summary>Every kind is shown by the page; a variant cell may repeat a kind (bar and grouped bar are one kind).</summary>
    [TestCase]
    public void EveryChartKindHasACellOnTheBasicsPage()
    {
        var shown = KindsInScene();
        var kinds = Enum.GetValues<ChartKind>().OrderBy(kind => kind).ToArray();
        GD.Print($"Basics page: {shown.Count} cell(s) for {kinds.Length} kind(s)");

        // Guard against a vacuous pass: an unreadable scene would otherwise look like full coverage of nothing.
        AssertThat(shown.Count).IsGreater(0);

        var missing = kinds.Where(kind => !shown.Contains(kind)).ToArray();
        AssertThat(missing.Length == 0 ? "" : "no cell for: " + string.Join(", ", missing)).IsEqual("");
    }
}
