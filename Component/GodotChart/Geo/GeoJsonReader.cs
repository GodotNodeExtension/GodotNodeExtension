namespace GodotNodeExtension.Component.GodotChart;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;

/// <summary>
/// The kind of geometry a <see cref="GeoGeometry"/> holds. It says how to read the parts, not how many
/// there are: a multi point is a <see cref="Point"/> geometry with one part per point, a multi polygon a
/// <see cref="Polygon"/> with several outer rings.
/// </summary>
public enum GeoShapeKind
{
    /// <summary>Points; each part holds one point.</summary>
    Point,

    /// <summary>Polylines; each part is one line.</summary>
    LineString,

    /// <summary>Polygons: each part is a ring, an outer ring followed by its holes (the file's order).</summary>
    Polygon,

    /// <summary>Several polygons, flattened into <see cref="Polygon"/>'s part order.</summary>
    MultiPolygon,
}

/// <summary>
/// One coordinate of a geographic geometry, in the units of the frame it will be drawn in (longitude and
/// latitude, degrees of an invented sphere, or world units). A <c>double</c> pair rather than a
/// <c>Vector2</c>: a geometry is source data, and single precision would lose a meridian at a deep zoom
/// before the viewport ever sees it.
/// </summary>
/// <param name="X">Horizontal coordinate.</param>
/// <param name="Y">Vertical coordinate.</param>
public readonly record struct GeoPoint(double X, double Y);

/// <summary>
/// One ring, line or point of a <see cref="GeoGeometry"/>.
/// </summary>
/// <param name="Points">The coordinates, in the order the source gave them (a polygon ring's closing
/// repeat of its first point is kept as it came in).</param>
/// <param name="IsHole">
/// True for an interior ring of a polygon. Which outer ring it belongs to follows the part order: a ring
/// that is not a hole opens a polygon, and the holes after it belong to that one.
/// </param>
public readonly record struct GeoRing(IReadOnlyList<GeoPoint> Points, bool IsHole);

/// <summary>
/// The geometry of one <see cref="GeoFeature"/>: a list of parts (rings, lines, points) plus the shape
/// kind that says how to read them. Coordinates are in the feature's own units and are not projected -
/// the frame does that when the geometry is drawn.
/// </summary>
public sealed class GeoGeometry
{
    /// <summary>Build a geometry from its parts. Used by the reader and the geometry builder.</summary>
    /// <param name="kind">How the parts are to be read.</param>
    /// <param name="parts">Rings, lines or points, in source order.</param>
    public GeoGeometry(GeoShapeKind kind, IReadOnlyList<GeoRing> parts)
    {
        ArgumentNullException.ThrowIfNull(parts);
        Kind = kind;
        Parts = parts;
        Bounds = ComputeBounds(parts);
    }

    /// <summary>How the parts are to be read.</summary>
    public GeoShapeKind Kind { get; }

    /// <summary>Rings, lines or points, in source order.</summary>
    public IReadOnlyList<GeoRing> Parts { get; }

    /// <summary>Whether the geometry carries no part at all (nothing to draw).</summary>
    public bool IsEmpty => Parts.Count == 0;

    /// <summary>
    /// Bounding rectangle of every part, in the geometry's own coordinates, or null for an empty
    /// geometry. A geometry that reaches past the antimeridian reports the rectangle it spans when
    /// unrolled, which is what <see cref="GeoViewport.Fit"/> wants.
    /// </summary>
    public GeoBounds? Bounds { get; }

    private static GeoBounds? ComputeBounds(IReadOnlyList<GeoRing> parts)
    {
        bool any = false;
        double minX = 0, minY = 0, maxX = 0, maxY = 0;
        foreach (var part in parts)
        {
            foreach (var point in part.Points)
            {
                if (!any)
                {
                    minX = maxX = point.X;
                    minY = maxY = point.Y;
                    any = true;
                    continue;
                }
                minX = Math.Min(minX, point.X);
                minY = Math.Min(minY, point.Y);
                maxX = Math.Max(maxX, point.X);
                maxY = Math.Max(maxY, point.Y);
            }
        }
        return any ? new GeoBounds(minX, minY, maxX, maxY) : null;
    }
}

/// <summary>
/// One geographic feature: a geometry plus what it is called and whatever the source said about it.
/// <para>
/// A feature is what a mark draws and what a data row is joined to (<see cref="GeoDataJoiner"/>), so the
/// two identifiers it carries are the two ways a table can refer to it: <see cref="Id"/> (GeoJSON's
/// <c>id</c>) and <see cref="Name"/> (the <c>name</c> property, the usual convention of map data).
/// </para>
/// <para>
/// Create one with <see cref="GeoGeometryBuilder"/> - inline geometry, a generated grid - or read a
/// GeoJSON file with <see cref="GeoJsonReader"/>; both hand out features, and the marks never see the
/// difference.
/// </para>
/// </summary>
public sealed class GeoFeature
{
    internal GeoFeature(string? id, string? name, GeoGeometry geometry,
                        IReadOnlyDictionary<string, object?> properties)
    {
        Id = id;
        Name = name;
        Geometry = geometry;
        Properties = properties;
    }

    /// <summary>The feature's identifier (GeoJSON's <c>id</c>), or null when the source has none.</summary>
    public string? Id { get; }

    /// <summary>
    /// The feature's display name: the <c>name</c> property of a GeoJSON feature, or the name the
    /// builder was given. Null when the source has none.
    /// </summary>
    public string? Name { get; }

    /// <summary>The geometry, in the frame's own coordinates.</summary>
    public GeoGeometry Geometry { get; }

    /// <summary>
    /// The source's properties: strings, numbers (<c>double</c>), booleans and nulls as they are, and
    /// nested objects and arrays as their JSON text. A row can be built from this dictionary, which is
    /// how a value carried by the map data itself reaches the colour and size channels.
    /// </summary>
    public IReadOnlyDictionary<string, object?> Properties { get; }

    /// <summary>
    /// The value a join is matched on, for one key name: the identifier, the name, or any property.
    /// Asking for <c>name</c> falls back to the identifier (map data often carries only an id, and the id
    /// is what a person would recognise anyway); asking for <c>id</c> gets the id or nothing.
    /// </summary>
    /// <param name="featureKey">The key name the join was configured with.</param>
    internal string? LookupKey(string featureKey)
    {
        if (string.Equals(featureKey, "name", StringComparison.Ordinal))
            return Name ?? Id;
        if (string.Equals(featureKey, "id", StringComparison.Ordinal))
            return Id;
        return Properties.TryGetValue(featureKey, out object? value)
            ? value switch
            {
                null => null,
                string text => text,
                double number => number.ToString("R", CultureInfo.InvariantCulture),
                bool flag => flag ? "true" : "false",
                _ => null,
            }
            : null;
    }
}

/// <summary>
/// Reads GeoJSON into <see cref="GeoFeature"/> values: a <c>FeatureCollection</c>, a single
/// <c>Feature</c>, or a bare geometry.
/// <para>
/// The reader is deliberately small and offline: no network, no coordinate system, no topology, no
/// simplification. It understands the geometry types a chart can draw (point, line, polygons with holes,
/// and their multi forms), keeps every property, and records what it could not use in the caller's
/// <c>issues</c> collection - an unreadable coordinate is skipped with a note instead of taking the whole
/// file down, because real map data from the field is rarely perfectly valid.
/// </para>
/// <para>
/// What it does not do: it does not know whether the coordinates are degrees or world units (the frame
/// decides), it does not repair ring winding (see <see cref="GeoMath.RingIsClockwise"/>), and it does not
/// read TopoJSON.
/// </para>
/// </summary>
public static class GeoJsonReader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    /// <summary>
    /// Parse GeoJSON text.
    /// </summary>
    /// <param name="json">The document; UTF-8 or a .NET string.</param>
    /// <param name="issues">
    /// Optional collection that receives one line per problem found (an unsupported geometry type, a
    /// coordinate that is not a finite number, a ring too short to draw). Problems are skipped, never
    /// thrown: the features that could be read are returned either way.
    /// </param>
    /// <returns>The features that could be read, in file order; empty when the document holds none.</returns>
    public static IReadOnlyList<GeoFeature> Parse(string json, ICollection<string>? issues = null)
    {
        ArgumentNullException.ThrowIfNull(json);
        GeoJsonDocumentDto? document;
        try
        {
            document = JsonSerializer.Deserialize<GeoJsonDocumentDto>(json, Options);
        }
        catch (JsonException exception)
        {
            return ParseDocument(null, issues, $"not valid JSON: {exception.Message}");
        }
        return ParseDocument(document, issues);
    }

    /// <summary>Parse GeoJSON from UTF-8 bytes; see <see cref="Parse(string, ICollection{string}?)"/>.</summary>
    /// <param name="utf8Json">The document as UTF-8 bytes.</param>
    /// <param name="issues">Optional collection that receives one line per problem found.</param>
    public static IReadOnlyList<GeoFeature> Parse(ReadOnlySpan<byte> utf8Json, ICollection<string>? issues = null)
    {
        GeoJsonDocumentDto? document;
        try
        {
            document = JsonSerializer.Deserialize<GeoJsonDocumentDto>(utf8Json, Options);
        }
        catch (JsonException exception)
        {
            return ParseDocument(null, issues, $"not valid JSON: {exception.Message}");
        }
        return ParseDocument(document, issues);
    }

    /// <summary>
    /// Read a GeoJSON file. The path goes through Godot's file access, so a <c>res://</c> path works in
    /// an exported game and a plain path works on a desktop tool.
    /// </summary>
    /// <param name="path">Path of the file to read.</param>
    /// <param name="issues">Optional collection that receives one line per problem found.</param>
    /// <exception cref="System.IO.FileNotFoundException">The file does not exist or cannot be read.</exception>
    public static IReadOnlyList<GeoFeature> ParseFile(string path, ICollection<string>? issues = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        byte[] bytes = FileAccess.GetFileAsBytes(path);
        if (bytes.Length == 0)
            throw new System.IO.FileNotFoundException($"GeoJSON file not found or empty: {path}", path);
        return Parse(bytes, issues);
    }

    /// <summary>
    /// Turn a parsed document into features, collecting the problems and reporting them once. The warning
    /// is pushed whether or not the caller asked for the details: map data that half loaded has to be
    /// visible in the console, while the collection is for the caller that wants to handle it.
    /// </summary>
    private static List<GeoFeature> ParseDocument(GeoJsonDocumentDto? document,
                                                  ICollection<string>? issues,
                                                  string? problem = null)
    {
        var local = issues is null ? new List<string>() : null;
        ICollection<string> sink = issues ?? local!;

        int before = sink.Count;
        if (problem is not null) sink.Add(problem);

        var features = document is null ? [] : Convert(document, sink);

        int found = sink.Count - before;
        if (found > 0)
        {
            GD.PushWarning(
                $"[GodotChart] GeoJSON: {found} problem(s) skipped while reading " +
                $"({string.Join("; ", FirstFew(sink, before))}).");
        }
        return features;
    }

    private static List<GeoFeature> Convert(GeoJsonDocumentDto? document, ICollection<string>? issues)
    {
        if (document is null)
        {
            issues?.Add("the document is empty");
            return [];
        }

        var features = new List<GeoFeature>();

        switch (document.Type)
        {
            case "FeatureCollection":
                if (document.Features is null)
                {
                    issues?.Add("FeatureCollection without a features array");
                    break;
                }
                foreach (var feature in document.Features)
                    AddFeature(features, feature, issues);
                break;

            case "Feature":
                AddFeature(features, new GeoJsonFeatureDto
                {
                    Id = document.Id,
                    Properties = document.Properties,
                    Geometry = document.Geometry,
                }, issues);
                break;

            default:
                // A bare geometry document: the top level is the geometry itself.
                var geometry = ConvertGeometry(document.Geometry ?? new GeoJsonGeometryDto
                {
                    Type = document.Type,
                    Coordinates = document.Coordinates,
                }, issues, "geometry");
                if (geometry is not null)
                    features.Add(new GeoFeature(ReadId(document.Id), null, geometry, new Dictionary<string, object?>()));
                break;
        }

        return features;
    }

    private static void AddFeature(List<GeoFeature> features, GeoJsonFeatureDto? dto, ICollection<string>? issues)
    {
        if (dto is null)
        {
            issues?.Add("feature entry is null");
            return;
        }

        var geometry = ConvertGeometry(dto.Geometry, issues, "feature");
        if (geometry is null) return;

        var properties = new Dictionary<string, object?>();
        if (dto.Properties is { } source)
        {
            foreach (var (key, value) in source)
                properties[key] = ReadProperty(value);
        }

        string? name = properties.TryGetValue("name", out object? nameValue)
            ? nameValue?.ToString()
            : null;

        features.Add(new GeoFeature(ReadId(dto.Id), name, geometry, properties));
    }

    private static GeoGeometry? ConvertGeometry(GeoJsonGeometryDto? dto, ICollection<string>? issues, string where)
    {
        if (dto?.Type is null)
        {
            issues?.Add($"{where}: geometry without a type");
            return null;
        }

        switch (dto.Type)
        {
            case "Point":
            {
                var point = ReadPosition(dto.Coordinates, issues, $"{where} point");
                return point is null
                    ? null
                    : new GeoGeometry(GeoShapeKind.Point, [new GeoRing([point.Value], false)]);
            }

            case "MultiPoint":
            {
                var parts = ReadPositions(dto.Coordinates, issues, $"{where} multi point");
                return parts.Count == 0 ? null : new GeoGeometry(GeoShapeKind.Point, ToParts(parts));
            }

            case "LineString":
            {
                var line = ReadLine(dto.Coordinates, issues, $"{where} line");
                return line is null ? null : new GeoGeometry(GeoShapeKind.LineString, [line.Value]);
            }

            case "MultiLineString":
            {
                var lines = ReadLines(dto.Coordinates, issues, $"{where} multi line");
                return lines.Count == 0 ? null : new GeoGeometry(GeoShapeKind.LineString, lines);
            }

            case "Polygon":
            {
                var rings = ReadRings(dto.Coordinates, issues, $"{where} polygon");
                return rings.Count == 0 ? null : new GeoGeometry(GeoShapeKind.Polygon, rings);
            }

            case "MultiPolygon":
            {
                var parts = new List<GeoRing>();
                foreach (var polygon in Elements(dto.Coordinates))
                    parts.AddRange(ReadRings(polygon, issues, $"{where} polygon"));
                return parts.Count == 0 ? null : new GeoGeometry(GeoShapeKind.MultiPolygon, parts);
            }

            default:
                issues?.Add($"{where}: unsupported geometry type '{dto.Type}'");
                return null;
        }
    }

    /// <summary>One part per point, which is how a multi point is read.</summary>
    private static List<GeoRing> ToParts(List<GeoPoint> points)
    {
        var parts = new List<GeoRing>(points.Count);
        foreach (var point in points)
            parts.Add(new GeoRing([point], false));
        return parts;
    }

    /// <summary>Read <c>[[x, y], ...]</c>: a list of coordinates.</summary>
    private static List<GeoPoint> ReadPositions(JsonElement? coordinates, ICollection<string>? issues, string where)
    {
        var points = new List<GeoPoint>();
        foreach (var element in Elements(coordinates))
        {
            var point = ReadPosition(element, issues, where);
            if (point is not null) points.Add(point.Value);
        }
        return points;
    }

    /// <summary>Read <c>[x, y]</c>.</summary>
    private static GeoPoint? ReadPosition(JsonElement? coordinates, ICollection<string>? issues, string where)
    {
        if (coordinates is null || coordinates.Value.ValueKind != JsonValueKind.Array
            || coordinates.Value.GetArrayLength() < 2)
        {
            issues?.Add($"{where}: a coordinate needs two numbers");
            return null;
        }

        JsonElement array = coordinates.Value;
        double x = ReadNumber(array[0]);
        double y = ReadNumber(array[1]);
        if (!double.IsFinite(x) || !double.IsFinite(y))
        {
            issues?.Add($"{where}: coordinate is not a finite number");
            return null;
        }
        return new GeoPoint(x, y);
    }

    private static double ReadNumber(JsonElement element)
        => element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out double value)
            ? value
            : double.NaN;

    /// <summary>Read <c>[[x, y], ...]</c> as one line, or null when it has too few points to be one.</summary>
    private static GeoRing? ReadLine(JsonElement? coordinates, ICollection<string>? issues, string where)
    {
        var points = ReadPositions(coordinates, issues, where);
        if (points.Count < 2)
        {
            issues?.Add($"{where}: a line needs at least two points, {points.Count} found");
            return null;
        }
        return new GeoRing(points, false);
    }

    private static List<GeoRing> ReadLines(JsonElement? coordinates, ICollection<string>? issues, string where)
    {
        var lines = new List<GeoRing>();
        foreach (var element in Elements(coordinates))
        {
            var line = ReadLine(element, issues, where);
            if (line is not null) lines.Add(line.Value);
        }
        return lines;
    }

    /// <summary>Read <c>[[[x, y], ...], ...]</c> as a polygon: the first ring is the outer one, the rest are holes.</summary>
    private static List<GeoRing> ReadRings(JsonElement? coordinates, ICollection<string>? issues, string where)
    {
        var rings = new List<GeoRing>();
        foreach (var element in Elements(coordinates))
        {
            var points = ReadPositions(element, issues, where);
            if (points.Count < 3)
            {
                issues?.Add($"{where}: a ring needs at least three points, {points.Count} found");
                continue;
            }
            rings.Add(new GeoRing(points, rings.Count > 0));
        }
        return rings;
    }

    /// <summary>Enumerate the elements of a JSON array; a missing or non-array value yields nothing.</summary>
    private static IEnumerable<JsonElement> Elements(JsonElement? coordinates)
    {
        if (coordinates is null || coordinates.Value.ValueKind != JsonValueKind.Array) yield break;
        foreach (var element in coordinates.Value.EnumerateArray())
            yield return element;
    }

    /// <summary>JSON scalars as CLR values; a nested object or array is kept as its JSON text.</summary>
    private static object? ReadProperty(JsonElement element)
        => element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => element.GetRawText(),
        };

    /// <summary>GeoJSON allows a string or a number as an identifier.</summary>
    private static string? ReadId(JsonElement? id)
        => id switch
        {
            null => null,
            { ValueKind: JsonValueKind.String } element => element.GetString(),
            { ValueKind: JsonValueKind.Number } element => element.GetRawText(),
            _ => null,
        };

    /// <summary>
    /// The first few problems of a collection, for the one-line warning: the details are in the caller's
    /// collection, the console gets a summary.
    /// </summary>
    private static IEnumerable<string> FirstFew(ICollection<string> issues, int skip)
    {
        int shown = 0;
        int seen = 0;
        foreach (var issue in issues)
        {
            if (seen++ < skip) continue;
            if (shown++ == 3)
            {
                yield return $"... {issues.Count - skip - 3} more";
                yield break;
            }
            yield return issue;
        }
    }

    // ── The document shape ─────────────────────────────────────
    // One DTO covers all three top level shapes (a collection, a single feature, a bare geometry): which
    // members are set is what "type" decides, and everything the reader does not know is ignored.

    private sealed class GeoJsonDocumentDto
    {
        [JsonPropertyName("type")] public string? Type { get; set; }
        [JsonPropertyName("features")] public List<GeoJsonFeatureDto>? Features { get; set; }
        [JsonPropertyName("id")] public JsonElement? Id { get; set; }
        [JsonPropertyName("properties")] public Dictionary<string, JsonElement>? Properties { get; set; }
        [JsonPropertyName("geometry")] public GeoJsonGeometryDto? Geometry { get; set; }
        [JsonPropertyName("coordinates")] public JsonElement? Coordinates { get; set; }
    }

    private sealed class GeoJsonFeatureDto
    {
        [JsonPropertyName("id")] public JsonElement? Id { get; set; }
        [JsonPropertyName("properties")] public Dictionary<string, JsonElement>? Properties { get; set; }
        [JsonPropertyName("geometry")] public GeoJsonGeometryDto? Geometry { get; set; }
    }

    private sealed class GeoJsonGeometryDto
    {
        [JsonPropertyName("type")] public string? Type { get; set; }
        [JsonPropertyName("coordinates")] public JsonElement? Coordinates { get; set; }
    }
}
