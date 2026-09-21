namespace GodotNodeExtension.Tests.GodotChart.Geo;

using System;
using System.Collections.Generic;
using GdUnit4;
using GodotNodeExtension.Component.GodotChart;
using GodotNodeExtension.Tests.Support;
using Support;
using static GdUnit4.Assertions;

/// <summary>
/// Behaviour specification for <see cref="GeoDataJoiner"/>: matching a table to features, nodes and edges,
/// the three missing strategies, the comparers that make real place names meet, and the two things a join
/// must never do - lose a row silently, or overwrite an element that already has one.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class GeoDataJoinerTest
{
    private static List<GeoFeature> Provinces(params (string Id, string Name)[] provinces)
    {
        var features = new List<GeoFeature>();
        var builder = new GeoGeometryBuilder();
        int index = 0;
        foreach (var (id, name) in provinces)
        {
            double x = index++ * 10;
            features.Add(builder.Polygon((x, 0), (x + 10, 0), (x + 10, 10), (x, 10)).Feature(id, name));
        }
        return features;
    }

    // ── Features ───────────────────────────────────────────────────────────

    [TestCase]
    public void RowsAreMatchedToTheFeatureWithTheSameName()
    {
        var features = Provinces(("zh", "Zhejiang"), ("js", "Jiangsu"));
        var rows = new List<DataRow>
        {
            TestContexts.Row(("name", "Jiangsu"), ("value", 20.0)),
            TestContexts.Row(("name", "Zhejiang"), ("value", 10.0)),
        };

        var joined = new GeoDataJoiner(missing: GeoJoinMissing.Silent).Join(rows, features);

        AssertThat(joined.ElementCount).IsEqual(2);
        AssertThat(joined.MatchedElementCount).IsEqual(2);
        AssertThat(joined.MissingElementCount).IsEqual(0);
        AssertThat(joined.UnmatchedRows.Count).IsEqual(0);
        // The order of the result follows the geometry, not the table.
        AssertThat(joined.RowOfElement[0]!.Get<double>("value")).IsEqual(10.0);
        AssertThat(joined.RowOfElement[1]!.Get<double>("value")).IsEqual(20.0);
    }

    [TestCase]
    public void TheIdentifierIsTheFallbackWhenAFeatureHasNoName()
    {
        var features = new List<GeoFeature>
        {
            new GeoGeometryBuilder().Polygon((0, 0), (1, 0), (1, 1)).Feature("zh"),
        };
        var rows = new List<DataRow> { TestContexts.Row(("name", "zh"), ("value", 1.0)) };

        var joined = new GeoDataJoiner(missing: GeoJoinMissing.Silent).Join(rows, features);

        AssertThat(features[0].Name is null).IsTrue();
        AssertThat(joined.MatchedElementCount).IsEqual(1);
    }

    [TestCase]
    public void AnyPropertyCanBeTheJoinKey()
    {
        var features = GeoJsonReader.Parse(
            """
            { "type": "Feature", "properties": { "code": "CH-ZH", "name": "Zürich" },
              "geometry": { "type": "Point", "coordinates": [8.5, 47.4] } }
            """);
        var rows = new List<DataRow> { TestContexts.Row(("code", "CH-ZH"), ("value", 3.0)) };

        var joined = new GeoDataJoiner(rowField: "code", featureKey: "code", missing: GeoJoinMissing.Silent)
            .Join(rows, features);

        AssertThat(joined.MatchedElementCount).IsEqual(1);
    }

    [TestCase]
    public void UnmatchedRowsAndUnfilledFeaturesAreBothCounted()
    {
        var features = Provinces(("zh", "Zhejiang"), ("js", "Jiangsu"), ("gd", "Guangdong"));
        var rows = new List<DataRow>
        {
            TestContexts.Row(("name", "Zhejiang"), ("value", 1.0)),
            TestContexts.Row(("name", "Nowhere"), ("value", 2.0)),
        };

        var joined = new GeoDataJoiner(missing: GeoJoinMissing.Silent).Join(rows, features);

        AssertThat(joined.MatchedElementCount).IsEqual(1);
        AssertThat(joined.MissingElementCount).IsEqual(2);
        AssertThat(joined.UnmatchedRows.Count).IsEqual(1);
        AssertThat(joined.RowOfElement[1] is null).IsTrue();
    }

    [TestCase]
    public void ASecondRowForOneFeatureIsNotOverwritten()
    {
        var features = Provinces(("zh", "Zhejiang"));
        var rows = new List<DataRow>
        {
            TestContexts.Row(("name", "Zhejiang"), ("value", 1.0)),
            TestContexts.Row(("name", "Zhejiang"), ("value", 2.0)),
        };

        var joined = new GeoDataJoiner(missing: GeoJoinMissing.Silent).Join(rows, features);

        AssertThat(joined.RowOfElement[0]!.Get<double>("value")).IsEqual(1.0);
        AssertThat(joined.UnmatchedRows.Count).IsEqual(1);
        AssertThat(joined.UnmatchedRows[0].Get<double>("value")).IsEqual(2.0);
    }

    [TestCase]
    public void ARowWithoutTheKeyFieldIsUnmatchedInsteadOfAThrow()
    {
        var features = Provinces(("zh", "Zhejiang"));
        var rows = new List<DataRow> { TestContexts.Row(("value", 1.0)) };

        var joined = new GeoDataJoiner(missing: GeoJoinMissing.Silent).Join(rows, features);

        AssertThat(joined.UnmatchedRows.Count).IsEqual(1);
    }

    // ── Comparers ──────────────────────────────────────────────────────────

    [TestCase]
    public void TheLooseComparerIgnoresCaseAndWhitespace()
    {
        var features = Provinces(("zh", "Zhejiang"));
        var rows = new List<DataRow> { TestContexts.Row(("name", "  zhejiang "), ("value", 1.0)) };

        var joined = new GeoDataJoiner(missing: GeoJoinMissing.Silent, keyComparer: GeoDataJoiner.LooseKeys)
            .Join(rows, features);

        AssertThat(joined.MatchedElementCount).IsEqual(1);
    }

    [TestCase]
    public void ASuffixInsensitiveComparerCanBeWrittenPerDataSet()
    {
        // The real world case: one side says "Zhejiang", the other "Zhejiang Province".
        var comparer = new SuffixComparer(" Province");
        var features = Provinces(("zh", "Zhejiang Province"));
        var rows = new List<DataRow> { TestContexts.Row(("name", "Zhejiang"), ("value", 1.0)) };

        var joined = new GeoDataJoiner(missing: GeoJoinMissing.Silent, keyComparer: comparer)
            .Join(rows, features);

        AssertThat(joined.MatchedElementCount).IsEqual(1);

        // And the same comparer reports a genuine mismatch rather than matching everything.
        var other = Provinces(("gd", "Guangdong Province"));
        AssertThat(new GeoDataJoiner(missing: GeoJoinMissing.Silent, keyComparer: comparer)
            .Join(rows, other).MatchedElementCount).IsEqual(0);
    }

    // ── The missing strategies ─────────────────────────────────────────────

    [TestCase]
    public void ReportWarnsOnceWithTheCounts()
    {
        var log = EngineMessageLog.Attach();
        try
        {
            var features = Provinces(("zh", "Zhejiang"), ("js", "Jiangsu"));
            var rows = new List<DataRow>
            {
                TestContexts.Row(("name", "Zhejiang"), ("value", 1.0)),
                TestContexts.Row(("name", "Nowhere"), ("value", 2.0)),
            };

            _ = new GeoDataJoiner().Join(rows, features);

            var warnings = log.WarningsContaining("GeoDataJoiner");
            AssertThat(warnings.Length).IsEqual(1);
            AssertThat(warnings[0].Contains("1 of 2 row(s)", StringComparison.Ordinal)).IsTrue();
            AssertThat(warnings[0].Contains("1 of 2 element(s)", StringComparison.Ordinal)).IsTrue();
        }
        finally
        {
            log.Detach();
        }
    }

    [TestCase]
    public void SilentSaysNothing()
    {
        var log = EngineMessageLog.Attach();
        try
        {
            var rows = new List<DataRow> { TestContexts.Row(("name", "Nowhere"), ("value", 2.0)) };

            _ = new GeoDataJoiner(missing: GeoJoinMissing.Silent).Join(rows, Provinces(("zh", "Zhejiang")));

            AssertThat(log.WarningsContaining("GeoDataJoiner").Length).IsEqual(0);
        }
        finally
        {
            log.Detach();
        }
    }

    [TestCase]
    public void ACompleteJoinIsQuietUnderTheDefaultStrategy()
    {
        var log = EngineMessageLog.Attach();
        try
        {
            var rows = new List<DataRow> { TestContexts.Row(("name", "Zhejiang"), ("value", 1.0)) };

            _ = new GeoDataJoiner().Join(rows, Provinces(("zh", "Zhejiang")));

            AssertThat(log.WarningsContaining("GeoDataJoiner").Length).IsEqual(0);
        }
        finally
        {
            log.Detach();
        }
    }

    [TestCase]
    public void StrictRefusesAnIncompleteJoin()
    {
        var rows = new List<DataRow> { TestContexts.Row(("name", "Nowhere"), ("value", 2.0)) };

        _ = Asserts.Throws<InvalidOperationException>(
            () => new GeoDataJoiner(missing: GeoJoinMissing.Strict).Join(rows, Provinces(("zh", "Zhejiang"))));
    }

    // ── Nodes and edges ────────────────────────────────────────────────────

    [TestCase]
    public void NodesAreMatchedByIdentifier()
    {
        var graph = new GeoGraphBuilder()
            .Node("waterloo", 0, 0)
            .Node("bank", 5, 1)
            .Edge("waterloo", "bank")
            .Build();
        var rows = new List<DataRow> { TestContexts.Row(("name", "bank"), ("value", 7.0)) };

        var joined = new GeoDataJoiner(missing: GeoJoinMissing.Silent).Join(rows, graph.Nodes);

        AssertThat(joined.MatchedElementCount).IsEqual(1);
        AssertThat(joined.RowOfElement[0] is null).IsTrue();
        AssertThat(joined.RowOfElement[1]!.Get<double>("value")).IsEqual(7.0);
    }

    [TestCase]
    public void EdgesAreMatchedByTheirTwoEnds()
    {
        var graph = new GeoGraphBuilder()
            .Node("a", 0, 0)
            .Node("b", 1, 0)
            .Node("c", 2, 0)
            .Edge("a", "b")
            .Edge("b", "c")
            .Build();
        var rows = new List<DataRow>
        {
            TestContexts.Row(("source", "b"), ("target", "c"), ("rides", 12.0)),
            TestContexts.Row(("source", "c"), ("target", "a"), ("rides", 99.0)), // no such edge
        };

        var joined = new GeoDataJoiner(missing: GeoJoinMissing.Silent).Join(rows, graph.Edges);

        AssertThat(joined.MatchedElementCount).IsEqual(1);
        AssertThat(joined.RowOfElement[0] is null).IsTrue();
        AssertThat(joined.RowOfElement[1]!.Get<double>("rides")).IsEqual(12.0);
        AssertThat(joined.UnmatchedRows.Count).IsEqual(1);
    }

    /// <summary>Compares keys after stripping one suffix from the end of either side.</summary>
    private sealed class SuffixComparer(string suffix) : IEqualityComparer<string>
    {
        public bool Equals(string? a, string? b)
            => string.Equals(Strip(a), Strip(b), StringComparison.OrdinalIgnoreCase);

        public int GetHashCode(string value)
            => StringComparer.OrdinalIgnoreCase.GetHashCode(Strip(value));

        private string Strip(string? value)
        {
            string text = (value ?? string.Empty).Trim();
            return text.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
                ? text[..^suffix.Length].Trim()
                : text;
        }
    }
}
