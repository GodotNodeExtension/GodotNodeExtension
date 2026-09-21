namespace GodotNodeExtension.Tests.GodotChart;

using System.Collections.Generic;
using System.Linq;
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
/// The case reads the <b>scene</b> rather than running it - the kinds are read off the instantiated nodes, so
/// a cell that is hidden (or one whose default-valued <c>Kind</c> a re-save left out, which is what Godot does
/// when it packs a scene) still counts. It used to scan the file text for <c>Kind = n</c> lines, and a re-save
/// that omitted the default <c>Kind = 1</c> of the four bar cells turned the page red although every cell was
/// there. <c>ExampleScenesIntegrationTest</c> is what proves the pages actually run.
/// </para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class BasicsKindCoverageTest
{
    /// <summary>The sheet that has to show every kind.</summary>
    private const string ScenePath = "res://Example/GodotChart/BasicsDemo.tscn";

    /// <summary>Kinds the scene configures, in tree order.</summary>
    private static List<ChartKind> KindsInScene()
    {
        var kinds = new List<ChartKind>();

        var packed = GD.Load<PackedScene>(ScenePath);
        if (packed is null) return kinds;

        var root = packed.Instantiate();

        try
        {
            foreach (var view in Descendants(root).OfType<ChartView>())
                kinds.Add(view.Kind);
        }
        finally
        {
            root.Free();
        }

        return kinds;
    }

    /// <summary>Every descendant of <paramref name="node"/>, depth first.</summary>
    /// <param name="node">Node to walk.</param>
    private static IEnumerable<Node> Descendants(Node node)
    {
        foreach (var child in node.GetChildren())
        {
            yield return child;

            foreach (var grandChild in Descendants(child))
                yield return grandChild;
        }
    }

    /// <summary>Every kind is shown by the page; a variant cell may repeat a kind (bar and grouped bar are one kind).</summary>
    [TestCase]
    public void EveryChartKindHasACellOnTheBasicsPage()
    {
        var shown = KindsInScene();
        var kinds = System.Enum.GetValues<ChartKind>().OrderBy(kind => kind).ToArray();
        GD.Print($"Basics page: {shown.Count} cell(s) for {kinds.Length} kind(s)");

        // Guard against a vacuous pass: a scene that does not load would otherwise look like full coverage.
        AssertThat(shown.Count).IsGreater(0);

        var missing = kinds.Where(kind => !shown.Contains(kind)).ToArray();
        AssertThat(missing.Length == 0 ? "" : "no cell for: " + string.Join(", ", missing)).IsEqual("");
    }
}
