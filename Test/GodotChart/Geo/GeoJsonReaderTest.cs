namespace GodotNodeExtension.Tests.GodotChart.Geo;

using System;
using System.Collections.Generic;
using GdUnit4;
using Godot;
using GodotNodeExtension.Component.GodotChart;
using GodotNodeExtension.Tests.Support;
using static GdUnit4.Assertions;

/// <summary>
/// Behaviour specification for <see cref="GeoJsonReader"/>: the three document shapes it accepts, how the
/// geometry types map onto <see cref="GeoGeometry"/> (rings, holes, multi forms), what it does with real
/// world data that is not quite valid, and the properties it keeps for a join.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class GeoJsonReaderTest
{
    private const string Square = """[[0,0],[10,0],[10,10],[0,10],[0,0]]""";
    private const string Hole = """[[2,2],[2,4],[4,4],[4,2],[2,2]]""";

    /// <summary>Assert two doubles are within <paramref name="eps"/> (IsEqual is exact for doubles).</summary>
    private static void Approx(double actual, double expected, double eps = 1e-9)
        => AssertThat(Math.Abs(actual - expected) <= eps).IsTrue();

    // ── Document shapes ────────────────────────────────────────────────────

    [TestCase]
    public void AFeatureCollectionIsReadIntoFeaturesInFileOrder()
    {
        var issues = new List<string>();
        var features = GeoJsonReader.Parse(
            $$"""
            { "type": "FeatureCollection", "features": [
                { "type": "Feature", "id": "north",
                  "properties": { "name": "North", "value": 12.5 },
                  "geometry": { "type": "Polygon", "coordinates": [ {{Square}} ] } },
                { "type": "Feature",
                  "properties": {},
                  "geometry": { "type": "Point", "coordinates": [3, 4] } }
            ] }
            """, issues);

        AssertThat(issues.Count).IsEqual(0);
        AssertThat(features.Count).IsEqual(2);

        var north = features[0];
        AssertThat(north.Id).IsEqual("north");
        AssertThat(north.Name).IsEqual("North");
        AssertThat(north.Geometry.Kind).IsEqual(GeoShapeKind.Polygon);
        AssertThat(north.Geometry.Parts.Count).IsEqual(1);
        AssertThat(north.Geometry.Parts[0].Points.Count).IsEqual(5);
        Approx(north.Geometry.Parts[0].Points[2].X, 10.0);
        Approx(north.Geometry.Parts[0].Points[2].Y, 10.0);

        // No name property: the identifier is what a join falls back to.
        var second = features[1];
        AssertThat(second.Name is null).IsTrue();
        AssertThat(second.LookupKey("name") is null).IsTrue();
        AssertThat(second.Geometry.Kind).IsEqual(GeoShapeKind.Point);
        Approx(second.Geometry.Parts[0].Points[0].Y, 4.0);
    }

    [TestCase]
    public void ASingleFeatureIsAccepted()
    {
        var features = GeoJsonReader.Parse(
            """
            { "type": "Feature", "properties": { "name": "Point de Vue" },
              "geometry": { "type": "Point", "coordinates": [1, 2] } }
            """);

        AssertThat(features.Count).IsEqual(1);
        AssertThat(features[0].Name).IsEqual("Point de Vue");
    }

    [TestCase]
    public void ABareGeometryIsAccepted()
    {
        var features = GeoJsonReader.Parse("""{ "type": "LineString", "coordinates": [[0,0],[1,1],[2,0]] }""");

        AssertThat(features.Count).IsEqual(1);
        AssertThat(features[0].Id is null).IsTrue();
        AssertThat(features[0].Geometry.Kind).IsEqual(GeoShapeKind.LineString);
        AssertThat(features[0].Geometry.Parts.Count).IsEqual(1);
        AssertThat(features[0].Geometry.Parts[0].Points.Count).IsEqual(3);
    }

    // ── Geometry mapping ───────────────────────────────────────────────────

    [TestCase]
    public void PolygonHolesAreMarkedAndFollowTheirOuterRing()
    {
        var features = GeoJsonReader.Parse(
            $$"""
            { "type": "Polygon", "coordinates": [ {{Square}}, {{Hole}} ] }
            """);

        var geometry = features[0].Geometry;
        AssertThat(geometry.Kind).IsEqual(GeoShapeKind.Polygon);
        AssertThat(geometry.Parts.Count).IsEqual(2);
        AssertThat(geometry.Parts[0].IsHole).IsFalse();
        AssertThat(geometry.Parts[1].IsHole).IsTrue();
    }

    [TestCase]
    public void AMultiPolygonIsFlattenedInFileOrder()
    {
        var features = GeoJsonReader.Parse(
            $$"""
            { "type": "MultiPolygon", "coordinates": [
                [ {{Square}} ],
                [ {{Square}}, {{Hole}} ]
            ] }
            """);

        var geometry = features[0].Geometry;
        AssertThat(geometry.Kind).IsEqual(GeoShapeKind.MultiPolygon);
        AssertThat(geometry.Parts.Count).IsEqual(3);
        AssertThat(geometry.Parts[0].IsHole).IsFalse();
        AssertThat(geometry.Parts[1].IsHole).IsFalse();
        AssertThat(geometry.Parts[2].IsHole).IsTrue();
    }

    [TestCase]
    public void MultiPointsAndMultiLinesBecomeOnePartPerElement()
    {
        var points = GeoJsonReader.Parse("""{ "type": "MultiPoint", "coordinates": [[1,2],[3,4]] }""");
        AssertThat(points[0].Geometry.Kind).IsEqual(GeoShapeKind.Point);
        AssertThat(points[0].Geometry.Parts.Count).IsEqual(2);
        AssertThat(points[0].Geometry.Parts[1].Points.Count).IsEqual(1);

        var lines = GeoJsonReader.Parse("""{ "type": "MultiLineString", "coordinates": [[[0,0],[1,1]],[[2,2],[3,3]]] }""");
        AssertThat(lines[0].Geometry.Kind).IsEqual(GeoShapeKind.LineString);
        AssertThat(lines[0].Geometry.Parts.Count).IsEqual(2);
        AssertThat(lines[0].Geometry.Parts[1].Points[1].X).IsEqual(3.0);
    }

    [TestCase]
    public void AGeometryReportsItsOwnBounds()
    {
        var features = GeoJsonReader.Parse($$"""{ "type": "Polygon", "coordinates": [ {{Square}} ] }""");

        var bounds = features[0].Geometry.Bounds;
        AssertThat(bounds is not null).IsTrue();
        Approx(bounds!.Value.MinX, 0.0);
        Approx(bounds.Value.MinY, 0.0);
        Approx(bounds.Value.MaxX, 10.0);
        Approx(bounds.Value.MaxY, 10.0);
    }

    // ── Properties ─────────────────────────────────────────────────────────

    [TestCase]
    public void PropertiesKeepTheirTypesAndNestedValuesKeepTheirJson()
    {
        var features = GeoJsonReader.Parse(
            """
            { "type": "Feature", "properties": {
                "name": "Mixed", "value": 12.5, "flag": true, "missing": null, "tags": ["a","b"] },
              "geometry": { "type": "Point", "coordinates": [0, 0] } }
            """);

        var properties = features[0].Properties;
        AssertThat(properties["name"]).IsEqual("Mixed");
        AssertThat(properties["value"]).IsEqual(12.5);
        AssertThat(properties["flag"]).IsEqual(true);
        AssertThat(properties["missing"] is null).IsTrue();
        AssertThat(properties["tags"]).IsEqual("""["a","b"]""");
    }

    [TestCase]
    public void AnIdentifierMayBeAStringOrANumber()
    {
        var text = GeoJsonReader.Parse(
            """
            { "type": "Feature", "id": "abc",
              "geometry": { "type": "Point", "coordinates": [0, 0] } }
            """);
        var number = GeoJsonReader.Parse(
            """
            { "type": "Feature", "id": 42,
              "geometry": { "type": "Point", "coordinates": [0, 0] } }
            """);

        AssertThat(text[0].Id).IsEqual("abc");
        AssertThat(number[0].Id).IsEqual("42");
    }

    [TestCase]
    public void AJoinKeyCanBeAnyProperty()
    {
        var features = GeoJsonReader.Parse(
            """
            { "type": "Feature", "properties": { "code": "CH-ZH", "name": "Zürich" },
              "geometry": { "type": "Point", "coordinates": [8.5, 47.4] } }
            """);

        AssertThat(features[0].LookupKey("code")).IsEqual("CH-ZH");
        AssertThat(features[0].LookupKey("id") is null).IsTrue();
    }

    // ── Data that is not valid ─────────────────────────────────────────────

    [TestCase]
    public void TextThatIsNotJsonIsReportedInsteadOfThrown()
    {
        var issues = new List<string>();
        var features = GeoJsonReader.Parse("this is not geojson", issues);

        AssertThat(features.Count).IsEqual(0);
        AssertThat(issues.Count).IsEqual(1);
        AssertThat(issues[0].Contains("not valid JSON", StringComparison.Ordinal)).IsTrue();
    }

    [TestCase]
    public void AnUnsupportedGeometryTypeIsSkippedWithAnIssue()
    {
        var issues = new List<string>();
        var features = GeoJsonReader.Parse(
            """
            { "type": "GeometryCollection", "geometries": [
                { "type": "Point", "coordinates": [0, 0] } ] }
            """, issues);

        AssertThat(features.Count).IsEqual(0);
        AssertThat(issues.Count).IsEqual(1);
        AssertThat(issues[0].Contains("unsupported geometry type", StringComparison.Ordinal)).IsTrue();
    }

    [TestCase]
    public void AFeatureCollectionWithoutFeaturesIsReported()
    {
        var issues = new List<string>();
        var features = GeoJsonReader.Parse("""{ "type": "FeatureCollection" }""", issues);

        AssertThat(features.Count).IsEqual(0);
        AssertThat(issues.Count).IsEqual(1);
        AssertThat(issues[0].Contains("features array", StringComparison.Ordinal)).IsTrue();
    }

    [TestCase]
    public void RingsAndLinesTooShortToDrawAreSkipped()
    {
        var issues = new List<string>();
        var shortLine = GeoJsonReader.Parse("""{ "type": "LineString", "coordinates": [[1,1]] }""", issues);
        var shortRing = GeoJsonReader.Parse("""{ "type": "Polygon", "coordinates": [[[0,0],[1,1]]] }""", issues);

        AssertThat(shortLine.Count).IsEqual(0);
        AssertThat(shortRing.Count).IsEqual(0);
        AssertThat(issues.Count).IsEqual(2);
        AssertThat(issues[0].Contains("at least two points", StringComparison.Ordinal)).IsTrue();
        AssertThat(issues[1].Contains("at least three points", StringComparison.Ordinal)).IsTrue();
    }

    [TestCase]
    public void ACoordinateThatIsNotTwoNumbersIsSkippedNotThrown()
    {
        var issues = new List<string>();
        var features = GeoJsonReader.Parse(
            """
            { "type": "MultiPoint", "coordinates": [[1,2],[3],[5,"x"],[7,8]] }
            """, issues);

        // Two of the four points survive; the reason for each of the other two is recorded.
        AssertThat(features.Count).IsEqual(1);
        AssertThat(features[0].Geometry.Parts.Count).IsEqual(2);
        AssertThat(issues.Count).IsEqual(2);
    }

    [TestCase]
    public void AnEmptyGeometryIsReadAsNoFeatures()
    {
        var features = GeoJsonReader.Parse("""{ "type": "Polygon", "coordinates": [] }""");

        AssertThat(features.Count).IsEqual(0);
    }

    // ── Reading a file ─────────────────────────────────────────────────────

    [TestCase]
    public void AFileIsReadThroughGodotsFileAccess()
    {
        const string folder = "res://tmp/GodotChart";
        const string path = folder + "/geo-reader-test.geojson";
        DirAccess.MakeDirRecursiveAbsolute(folder);
        using (var file = FileAccess.Open(path, FileAccess.ModeFlags.Write))
        {
            AssertThat(file is not null).IsTrue();
            file!.StoreString($$"""{ "type": "Polygon", "coordinates": [ {{Square}} ] }""");
        }

        try
        {
            var features = GeoJsonReader.ParseFile(path);

            AssertThat(features.Count).IsEqual(1);
            AssertThat(features[0].Geometry.Kind).IsEqual(GeoShapeKind.Polygon);
        }
        finally
        {
            DirAccess.RemoveAbsolute(path);
        }
    }

    [TestCase]
    public void ASkippedProblemIsReportedOnceAsAWarning()
    {
        var log = EngineMessageLog.Attach();
        try
        {
            GeoJsonReader.Parse("""{ "type": "MultiPoint", "coordinates": [[3],[7,8]] }""");

            var warnings = log.WarningsContaining("GeoJSON");
            AssertThat(warnings.Length).IsEqual(1);
            AssertThat(warnings[0].Contains("1 problem(s)", StringComparison.Ordinal)).IsTrue();
        }
        finally
        {
            log.Detach();
        }
    }
}
