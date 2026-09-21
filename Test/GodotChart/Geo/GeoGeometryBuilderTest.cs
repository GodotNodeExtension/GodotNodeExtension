namespace GodotNodeExtension.Tests.GodotChart.Geo;

using System;
using System.Collections.Generic;
using GdUnit4;
using GodotNodeExtension.Component.GodotChart;
using Support;
using static GdUnit4.Assertions;

/// <summary>
/// Behaviour specification for <see cref="GeoGeometryBuilder"/>: building polygons (with holes), lines and
/// points in code, the grid that turns a tile map into features, and the mistakes the builder refuses
/// instead of turning into geometry no mark could read.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class GeoGeometryBuilderTest
{
    /// <summary>Assert two doubles are within <paramref name="eps"/> (IsEqual is exact for doubles).</summary>
    private static void Approx(double actual, double expected, double eps = 1e-9)
        => AssertThat(Math.Abs(actual - expected) <= eps).IsTrue();

    // ── Polygons ───────────────────────────────────────────────────────────

    [TestCase]
    public void APolygonWithAHoleIsOneFeature()
    {
        var feature = new GeoGeometryBuilder()
            .Polygon((0, 0), (10, 0), (10, 10), (0, 10))
            .Hole((2, 2), (2, 4), (4, 4), (4, 2))
            .Feature("plot-7", "Plot 7");

        AssertThat(feature.Id).IsEqual("plot-7");
        AssertThat(feature.Name).IsEqual("Plot 7");
        AssertThat(feature.Geometry.Kind).IsEqual(GeoShapeKind.Polygon);
        AssertThat(feature.Geometry.Parts.Count).IsEqual(2);
        AssertThat(feature.Geometry.Parts[0].IsHole).IsFalse();
        AssertThat(feature.Geometry.Parts[1].IsHole).IsTrue();
        AssertThat(feature.Geometry.Parts[0].Points.Count).IsEqual(4);
        Approx(feature.Geometry.Bounds!.Value.MaxX, 10.0);
    }

    [TestCase]
    public void FeatureClosesTheGeometryAndEmptiesTheBuilder()
    {
        var builder = new GeoGeometryBuilder();
        var first = builder.Polygon((0, 0), (1, 0), (1, 1)).Feature("a");
        var second = builder.Polygon((5, 5), (6, 5), (6, 6)).Feature("b");

        AssertThat(first.Geometry.Parts.Count).IsEqual(1);
        AssertThat(second.Geometry.Parts.Count).IsEqual(1);
        Approx(first.Geometry.Bounds!.Value.MinX, 0.0);
        Approx(second.Geometry.Bounds!.Value.MinX, 5.0);
    }

    [TestCase]
    public void LinesAndPointsBecomeTheirOwnKind()
    {
        var line = new GeoGeometryBuilder().Line((0, 0), (5, 0), (5, 5)).Feature("route");
        var point = new GeoGeometryBuilder().Point(3, 4).Feature("station");

        AssertThat(line.Geometry.Kind).IsEqual(GeoShapeKind.LineString);
        AssertThat(line.Geometry.Parts[0].Points.Count).IsEqual(3);
        AssertThat(point.Geometry.Kind).IsEqual(GeoShapeKind.Point);
        AssertThat(point.Geometry.Parts[0].Points.Count).IsEqual(1);
    }

    // ── Refusals ───────────────────────────────────────────────────────────

    [TestCase]
    public void AFeatureWithoutAPartIsRefused()
    {
        _ = Asserts.Throws<InvalidOperationException>(() => new GeoGeometryBuilder().Feature("empty"));
    }

    [TestCase]
    public void MixingGeometryKindsIsRefused()
    {
        _ = Asserts.Throws<InvalidOperationException>(() => new GeoGeometryBuilder()
            .Polygon((0, 0), (1, 0), (1, 1))
            .Line((0, 0), (1, 1))
            .Feature("mixed"));
    }

    [TestCase]
    public void AHoleNeedsAnOuterRingFirst()
    {
        _ = Asserts.Throws<InvalidOperationException>(() => new GeoGeometryBuilder()
            .Hole((0, 0), (1, 0), (1, 1)));
    }

    [TestCase]
    public void TooFewPointsAreRefused()
    {
        _ = Asserts.Throws<ArgumentException>(() => new GeoGeometryBuilder().Polygon((0, 0), (1, 0)));
        _ = Asserts.Throws<ArgumentException>(() => new GeoGeometryBuilder().Line((0, 0)));
    }

    [TestCase]
    public void ACoordinateThatIsNotFiniteIsRefused()
    {
        _ = Asserts.Throws<ArgumentException>(() => new GeoGeometryBuilder()
            .Point(double.NaN, 0));
    }

    // ── Grid ───────────────────────────────────────────────────────────────

    [TestCase]
    public void AGridIsOneSquareFeaturePerCell()
    {
        var cells = GeoGeometryBuilder.Grid(rows: 2, columns: 3);

        AssertThat(cells.Count).IsEqual(6);
        AssertThat(cells[0].Id).IsEqual("r0c0");
        AssertThat(cells[0].Name).IsEqual("r0c0");
        AssertThat(cells[5].Id).IsEqual("r1c2");
        AssertThat(cells[0].Geometry.Kind).IsEqual(GeoShapeKind.Polygon);

        // Cell (row 1, column 2) with unit cells sits at x 2..3, y 1..2.
        var top = cells[5].Geometry.Bounds!.Value;
        Approx(top.MinX, 2.0);
        Approx(top.MaxX, 3.0);
        Approx(top.MinY, 1.0);
        Approx(top.MaxY, 2.0);
    }

    [TestCase]
    public void AGridCanBeSizedAndNamedByTheCaller()
    {
        var cells = GeoGeometryBuilder.Grid(rows: 1, columns: 2, cellWidth: 10, cellHeight: 5,
                                            idOf: (row, column) => $"tile-{row}-{column}");

        AssertThat(cells[1].Id).IsEqual("tile-0-1");
        Approx(cells[1].Geometry.Bounds!.Value.MinX, 10.0);
        Approx(cells[1].Geometry.Bounds!.Value.MaxY, 5.0);
    }

    [TestCase]
    public void ADegenerateGridIsRefused()
    {
        _ = Asserts.Throws<ArgumentOutOfRangeException>(() => GeoGeometryBuilder.Grid(rows: 0, columns: 1));
        _ = Asserts.Throws<ArgumentOutOfRangeException>(() => GeoGeometryBuilder.Grid(rows: 1, columns: 0));
        _ = Asserts.Throws<ArgumentOutOfRangeException>(
            () => GeoGeometryBuilder.Grid(rows: 1, columns: 1, cellWidth: 0));
    }

    [TestCase]
    public void AGeneratedGridJoinsLikeAReadMap()
    {
        var cells = GeoGeometryBuilder.Grid(rows: 2, columns: 2);
        var rows = new List<DataRow>
        {
            TestContexts.Row(("name", "r0c1"), ("value", 4.0)),
            TestContexts.Row(("name", "r1c0"), ("value", 9.0)),
        };

        var joined = new GeoDataJoiner(missing: GeoJoinMissing.Silent).Join(rows, cells);

        AssertThat(joined.MatchedElementCount).IsEqual(2);
        AssertThat(joined.RowOfElement[0] is null).IsTrue();
        AssertThat(joined.RowOfElement[1]!.Get<double>("value")).IsEqual(4.0);
        AssertThat(joined.RowOfElement[2]!.Get<double>("value")).IsEqual(9.0);
        AssertThat(joined.MissingElementCount).IsEqual(2);
    }
}
