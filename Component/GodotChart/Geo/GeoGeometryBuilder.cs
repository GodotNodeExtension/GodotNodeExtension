namespace GodotNodeExtension.Component.GodotChart;

using System;
using System.Collections.Generic;
using System.Globalization;

/// <summary>
/// Builds geographic geometry in code: a game level, a procedurally generated map, an abstract layout
/// that was arranged in code and now needs a frame to be drawn in.
/// <para>
/// Parts are added one call at a time and <see cref="Feature"/> closes the feature and hands it out;
/// the builder is then empty again for the next one. A geometry holds one kind of part - polygons (with
/// holes), lines, or points - and mixing them is a mistake in the calling code, so it throws instead of
/// producing something no mark could read. Geometry that comes from a file goes through
/// <see cref="GeoJsonReader"/> instead, which is deliberately lenient because field data is not.
/// </para>
/// </summary>
public sealed class GeoGeometryBuilder
{
    private readonly List<GeoRing> _parts = [];
    private bool _hasPolygon;
    private bool _hasLine;
    private bool _hasPoint;
    private bool _openPolygon;

    /// <summary>
    /// Add a polygon: an outer ring, plus the holes added by <see cref="Hole"/> before the next polygon.
    /// </summary>
    /// <param name="points">At least three corners, in order. The ring is closed implicitly.</param>
    public GeoGeometryBuilder Polygon(params (double X, double Y)[] points)
    {
        Append(points, holes: false, minimum: 3, "polygon");
        _hasPolygon = true;
        _openPolygon = true;
        return this;
    }

    /// <summary>
    /// Add a hole to the polygon opened last. A hole is drawn as a hole - the fill rule uses the ring
    /// order, so a hole whose winding matches its outer ring is corrected instead of filled.
    /// </summary>
    /// <param name="points">At least three corners, in order.</param>
    public GeoGeometryBuilder Hole(params (double X, double Y)[] points)
    {
        if (!_openPolygon)
            throw new InvalidOperationException(
                "GeoGeometryBuilder.Hole needs a polygon first: add the outer ring with Polygon(...).");
        Append(points, holes: true, minimum: 3, "hole");
        return this;
    }

    /// <summary>Add a polyline.</summary>
    /// <param name="points">At least two points, in order.</param>
    public GeoGeometryBuilder Line(params (double X, double Y)[] points)
    {
        Append(points, holes: false, minimum: 2, "line");
        _hasLine = true;
        return this;
    }

    /// <summary>Add a point.</summary>
    /// <param name="x">Horizontal coordinate.</param>
    /// <param name="y">Vertical coordinate.</param>
    public GeoGeometryBuilder Point(double x, double y)
    {
        _parts.Add(new GeoRing([new GeoPoint(x, y)], false));
        _hasPoint = true;
        return this;
    }

    /// <summary>
    /// Close the feature and hand it out. The builder is empty afterwards, so one builder can produce a
    /// whole map.
    /// </summary>
    /// <param name="id">Identifier a data row is joined to (see <see cref="GeoDataJoiner"/>), or null.</param>
    /// <param name="name">
    /// Display name, or null. A join on <c>name</c> falls back to the identifier, so a feature with only
    /// an id is still reachable by both.
    /// </param>
    /// <exception cref="InvalidOperationException">No part was added, or two kinds of part were mixed.</exception>
    public GeoFeature Feature(string? id = null, string? name = null)
    {
        if (_parts.Count == 0)
            throw new InvalidOperationException("GeoGeometryBuilder.Feature needs at least one part.");

        int kinds = (_hasPolygon ? 1 : 0) + (_hasLine ? 1 : 0) + (_hasPoint ? 1 : 0);
        if (kinds > 1)
            throw new InvalidOperationException(
                "A geometry holds polygons, lines or points - not a mix of them.");

        var geometry = new GeoGeometry(_hasPolygon ? GeoShapeKind.Polygon
            : _hasLine ? GeoShapeKind.LineString
            : GeoShapeKind.Point, [.. _parts]);

        _parts.Clear();
        _hasPolygon = _hasLine = _hasPoint = _openPolygon = false;
        return new GeoFeature(id, name, geometry, new Dictionary<string, object?>());
    }

    /// <summary>
    /// A regular grid, one square feature per cell - the coordinate side of a tile map. Each cell is
    /// identified by <paramref name="idOf"/>, so a table of per cell values joins to it the same way a
    /// table of per province values joins to a map.
    /// </summary>
    /// <param name="rows">Number of rows, counted upwards from the origin.</param>
    /// <param name="columns">Number of columns, counted to the right of the origin.</param>
    /// <param name="cellWidth">Width of one cell, in frame units.</param>
    /// <param name="cellHeight">Height of one cell, in frame units.</param>
    /// <param name="idOf">
    /// Identifier of the cell at a row and a column; null uses <c>r{row}c{column}</c>. The name of the
    /// feature is the same string, so a join by either works.
    /// </param>
    /// <returns>One feature per cell, row by row.</returns>
    public static IReadOnlyList<GeoFeature> Grid(int rows, int columns,
                                                 double cellWidth = 1, double cellHeight = 1,
                                                 Func<int, int, string>? idOf = null)
    {
        if (rows <= 0) throw new ArgumentOutOfRangeException(nameof(rows), rows, "A grid needs at least one row.");
        if (columns <= 0) throw new ArgumentOutOfRangeException(nameof(columns), columns, "A grid needs at least one column.");
        if (!(cellWidth > 0)) throw new ArgumentOutOfRangeException(nameof(cellWidth), cellWidth, "Cell width must be positive.");
        if (!(cellHeight > 0)) throw new ArgumentOutOfRangeException(nameof(cellHeight), cellHeight, "Cell height must be positive.");

        var features = new List<GeoFeature>(rows * columns);
        var builder = new GeoGeometryBuilder();
        for (int row = 0; row < rows; row++)
        {
            for (int column = 0; column < columns; column++)
            {
                double x = column * cellWidth;
                double y = row * cellHeight;
                string id = idOf?.Invoke(row, column)
                    ?? string.Create(CultureInfo.InvariantCulture, $"r{row}c{column}");
                features.Add(builder
                    .Polygon((x, y), (x + cellWidth, y), (x + cellWidth, y + cellHeight), (x, y + cellHeight))
                    .Feature(id, id));
            }
        }
        return features;
    }

    private void Append((double X, double Y)[] points, bool holes, int minimum, string what)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (points.Length < minimum)
            throw new ArgumentException(
                $"A {what} needs at least {minimum} points, {points.Length} given.", nameof(points));

        var coordinates = new List<GeoPoint>(points.Length);
        foreach (var (x, y) in points)
        {
            if (!double.IsFinite(x) || !double.IsFinite(y))
                throw new ArgumentException($"A {what} coordinate has to be a finite number.", nameof(points));
            coordinates.Add(new GeoPoint(x, y));
        }
        _parts.Add(new GeoRing(coordinates, holes));
    }
}
