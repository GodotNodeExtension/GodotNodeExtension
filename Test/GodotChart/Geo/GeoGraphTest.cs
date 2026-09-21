namespace GodotNodeExtension.Tests.GodotChart.Geo;

using System;
using System.Collections.Generic;
using GdUnit4;
using GodotNodeExtension.Component.GodotChart;
using GodotNodeExtension.Tests.Support;
using Support;
using static GdUnit4.Assertions;

/// <summary>
/// Behaviour specification for the topological geometry: <see cref="GeoGraphBuilder"/> (in code and from
/// the two tables a graph usually arrives as), what a graph does with the edges and nodes that do not fit
/// together, and the lookups a mark draws and hit-tests with.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class GeoGraphTest
{
    /// <summary>Assert two doubles are within <paramref name="eps"/> (IsEqual is exact for doubles).</summary>
    private static void Approx(double actual, double expected, double eps = 1e-9)
        => AssertThat(Math.Abs(actual - expected) <= eps).IsTrue();

    // ── Building ───────────────────────────────────────────────────────────

    [TestCase]
    public void NodesAndEdgesAreKeptInTheOrderTheyWereAdded()
    {
        var graph = new GeoGraphBuilder()
            .Node("a", 0, 0, "Alpha")
            .Node("b", 10, 0)
            .Node("c", 20, 5)
            .Edge("a", "b", weight: 3)
            .Edge("b", "c", weight: 7, directed: true)
            .Build();

        AssertThat(graph.Nodes.Count).IsEqual(3);
        AssertThat(graph.Edges.Count).IsEqual(2);
        AssertThat(graph.Nodes[0].Name).IsEqual("Alpha");
        AssertThat(graph.Nodes[0].Label).IsEqual("Alpha");
        AssertThat(graph.Nodes[1].Label).IsEqual("b"); // no name: the identifier is what a label shows
        AssertThat(graph.Edges[1].Weight).IsEqual(7.0);
        AssertThat(graph.Edges[1].Directed).IsTrue();
    }

    [TestCase]
    public void ANodesIdentifierIsUnique()
    {
        var builder = new GeoGraphBuilder().Node("a", 0, 0);
        _ = Asserts.Throws<InvalidOperationException>(() => builder.Node("a", 1, 1));
    }

    [TestCase]
    public void AnEdgeMayBeAddedBeforeItsNodes()
    {
        var graph = new GeoGraphBuilder()
            .Edge("a", "b")
            .Node("a", 0, 0)
            .Node("b", 1, 1)
            .Build();

        AssertThat(graph.Edges.Count).IsEqual(1);
    }

    [TestCase]
    public void GraphsAreLookedUpByIdentifier()
    {
        var graph = new GeoGraphBuilder().Node("a", 1, 2).Node("b", 3, 4).Build();

        AssertThat(graph.TryGetNode("a", out var node)).IsTrue();
        AssertThat(node!.X).IsEqual(1.0);
        AssertThat(graph.TryGetNode("missing", out _)).IsFalse();
    }

    [TestCase]
    public void BoundsCoverTheNodePositions()
    {
        var graph = new GeoGraphBuilder().Node("a", -5, 0).Node("b", 3, 8).Build();

        Approx(graph.Bounds!.Value.MinX, -5.0);
        Approx(graph.Bounds!.Value.MaxX, 3.0);
        Approx(graph.Bounds!.Value.MaxY, 8.0);
        AssertThat(new GeoGraphBuilder().Build().Bounds is null).IsTrue();
    }

    // ── Shapes a graph may legitimately have ───────────────────────────────

    [TestCase]
    public void AnIsolatedNodeIsKept()
    {
        var graph = new GeoGraphBuilder().Node("a", 0, 0).Node("b", 1, 1).Edge("a", "b").Build();

        AssertThat(graph.Nodes.Count).IsEqual(2);
        AssertThat(graph.Edges.Count).IsEqual(1);
    }

    [TestCase]
    public void ASelfLoopAndARepeatedEdgeAreKept()
    {
        var graph = new GeoGraphBuilder()
            .Node("a", 0, 0)
            .Node("b", 1, 0)
            .Edge("a", "a")   // a self loop
            .Edge("a", "b")
            .Edge("a", "b")   // the same connection twice: two edges, not an error
            .Build();

        AssertThat(graph.Edges.Count).IsEqual(3);
    }

    [TestCase]
    public void AnEdgeWithoutBothEndsIsDroppedWithOneWarning()
    {
        var log = EngineMessageLog.Attach();
        try
        {
            var graph = new GeoGraphBuilder()
                .Node("a", 0, 0)
                .Node("b", 1, 0)
                .Edge("a", "b")
                .Edge("a", "ghost")
                .Build();

            AssertThat(graph.Edges.Count).IsEqual(1);
            var warnings = log.WarningsContaining("GeoGraph");
            AssertThat(warnings.Length).IsEqual(1);
            AssertThat(warnings[0].Contains("1 edge(s)", StringComparison.Ordinal)).IsTrue();
        }
        finally
        {
            log.Detach();
        }
    }

    // ── From two tables ────────────────────────────────────────────────────

    [TestCase]
    public void ATwoTableGraphIsBuilt()
    {
        var nodes = new List<DataRow>
        {
            TestContexts.Row(("id", "waterloo"), ("x", 0.0), ("y", 0.0), ("title", "Waterloo")),
            TestContexts.Row(("id", "bank"), ("x", 5.0), ("y", 1.0)),
        };
        var edges = new List<DataRow>
        {
            TestContexts.Row(("source", "waterloo"), ("target", "bank"), ("rides", 20.0)),
        };

        var graph = GeoGraphBuilder.FromRows(nodes, edges, nameField: "title", weightField: "rides");

        AssertThat(graph.Nodes.Count).IsEqual(2);
        AssertThat(graph.Nodes[0].Name).IsEqual("Waterloo");
        AssertThat(graph.Edges[0].Weight).IsEqual(20.0);
    }

    [TestCase]
    public void RowsThatCannotBeReadAreSkippedAndReported()
    {
        var issues = new List<string>();
        var nodes = new List<DataRow>
        {
            TestContexts.Row(("id", "a"), ("x", 0.0), ("y", 0.0)),
            TestContexts.Row(("id", "b"), ("x", 1.0)),                 // no y
            TestContexts.Row(("x", 2.0), ("y", 2.0)),                  // no id
            TestContexts.Row(("id", "a"), ("x", 3.0), ("y", 3.0)),     // the identifier again
        };
        var edges = new List<DataRow>
        {
            TestContexts.Row(("source", "a"), ("target", "b")),
            TestContexts.Row(("source", "a")),                          // no target
        };

        var graph = GeoGraphBuilder.FromRows(nodes, edges, issues: issues);

        AssertThat(graph.Nodes.Count).IsEqual(1);
        AssertThat(graph.Edges.Count).IsEqual(0); // "b" was never built, so its edge is dangling
        AssertThat(issues.Count).IsEqual(4);
    }
}
