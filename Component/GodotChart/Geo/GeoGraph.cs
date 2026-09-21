namespace GodotNodeExtension.Component.GodotChart;

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Godot;

/// <summary>
/// One node of a <see cref="GeoGraph"/>: a thing at a position - a station, a star, a junction.
/// <para>
/// What a node is <i>coloured</i> by comes from the data table joined to it (see
/// <see cref="GeoDataJoiner"/>), not from here: the graph carries the positions and the connections, the
/// table carries the values. A node read from a table keeps the identifier, the coordinates and the name.
/// </para>
/// </summary>
/// <param name="Id">Identifier a data row is joined to (see <see cref="GeoDataJoiner"/>).</param>
/// <param name="X">Horizontal coordinate, in the frame's own units.</param>
/// <param name="Y">Vertical coordinate, in the frame's own units.</param>
/// <param name="Name">Display name, or null when the identifier is what should be shown.</param>
public sealed record GeoNode(string Id, double X, double Y, string? Name = null)
{
    /// <summary>What a label shows: the name, or the identifier when there is none.</summary>
    public string Label => string.IsNullOrEmpty(Name) ? Id : Name;
}

/// <summary>
/// One edge of a <see cref="GeoGraph"/>: a connection between two nodes.
/// </summary>
/// <param name="SourceId">Identifier of the node the edge starts at.</param>
/// <param name="TargetId">Identifier of the node the edge ends at.</param>
/// <param name="Weight">Whatever the edge measures (passengers, traffic, distance), or null. A mark that
/// encodes an edge by width reads it from here; a table joined to the edge reaches the same values through
/// its own rows.</param>
/// <param name="Directed">Whether the edge has a direction to draw an arrow for.</param>
public sealed record GeoEdge(string SourceId, string TargetId, double? Weight = null, bool Directed = false);

/// <summary>
/// A topological geometry: nodes and the edges between them, with the positions the nodes carry.
/// <para>
/// This is the geometry of a map that does not pretend to be a shape - a metro diagram, a star chart, a
/// relation graph. The nodes have coordinates like any other feature, so the same frame and the same
/// viewport place them; what differs is that the geometry is the connection, not the outline, and that a
/// chart of one has no continuous space to interpolate a field over (see <see cref="GeoMark.Source"/>).
/// </para>
/// <para>
/// An edge that names a node the graph does not contain is dropped with one warning: a diagram missing a
/// line is a smaller problem than a render that throws halfway through the frame.
/// </para>
/// </summary>
public sealed class GeoGraph
{
    private readonly Dictionary<string, GeoNode> _nodesById;

    /// <summary>Build a graph, dropping edges whose ends are not both present.</summary>
    /// <param name="nodes">The nodes.</param>
    /// <param name="edges">The edges.</param>
    public GeoGraph(IReadOnlyList<GeoNode> nodes, IReadOnlyList<GeoEdge> edges)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(edges);

        Nodes = nodes;
        _nodesById = new Dictionary<string, GeoNode>(nodes.Count, StringComparer.Ordinal);
        foreach (var node in nodes)
            _nodesById[node.Id] = node;

        var kept = new List<GeoEdge>(edges.Count);
        int dangling = 0;
        foreach (var edge in edges)
        {
            if (_nodesById.ContainsKey(edge.SourceId) && _nodesById.ContainsKey(edge.TargetId))
                kept.Add(edge);
            else
                dangling++;
        }
        if (dangling > 0)
        {
            GD.PushWarning(
                $"[GodotChart] GeoGraph: {dangling} edge(s) reference a node the graph does not contain " +
                $"and were dropped ({nodes.Count} node(s), {edges.Count} edge(s) given).");
        }
        Edges = kept;

        Bounds = ComputeBounds(nodes);
    }

    /// <summary>The nodes, in the order they were added.</summary>
    public IReadOnlyList<GeoNode> Nodes { get; }

    /// <summary>The edges, in the order they were added, minus the ones with a missing end.</summary>
    public IReadOnlyList<GeoEdge> Edges { get; }

    /// <summary>
    /// Bounding rectangle of the node positions, or null for an empty graph. The frame's own coordinates
    /// (see <see cref="GeoGeometry.Bounds"/> for the geometry side).
    /// </summary>
    public GeoBounds? Bounds { get; }

    /// <summary>Look a node up by identifier.</summary>
    /// <param name="id">Identifier to look for.</param>
    /// <param name="node">The node, when it is in the graph.</param>
    public bool TryGetNode(string id, [NotNullWhen(true)] out GeoNode? node)
        => _nodesById.TryGetValue(id, out node);

    private static GeoBounds? ComputeBounds(IReadOnlyList<GeoNode> nodes)
    {
        if (nodes.Count == 0) return null;

        double minX = nodes[0].X, maxX = nodes[0].X, minY = nodes[0].Y, maxY = nodes[0].Y;
        for (int i = 1; i < nodes.Count; i++)
        {
            var node = nodes[i];
            minX = Math.Min(minX, node.X);
            maxX = Math.Max(maxX, node.X);
            minY = Math.Min(minY, node.Y);
            maxY = Math.Max(maxY, node.Y);
        }
        return new GeoBounds(minX, minY, maxX, maxY);
    }
}

/// <summary>
/// Builds a <see cref="GeoGraph"/> in code, or from the two tables a graph usually arrives as (a node
/// table and an edge table - the shape metro data and network exports come in).
/// </summary>
public sealed class GeoGraphBuilder
{
    private readonly List<GeoNode> _nodes = [];
    private readonly List<GeoEdge> _edges = [];
    private readonly HashSet<string> _nodeIds = new(StringComparer.Ordinal);

    /// <summary>
    /// Add a node.
    /// </summary>
    /// <param name="id">Identifier; must be unique in the graph.</param>
    /// <param name="x">Horizontal coordinate.</param>
    /// <param name="y">Vertical coordinate.</param>
    /// <param name="name">Display name, or null.</param>
    /// <exception cref="InvalidOperationException">A node with that identifier already exists.</exception>
    public GeoGraphBuilder Node(string id, double x, double y, string? name = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        if (!double.IsFinite(x) || !double.IsFinite(y))
            throw new ArgumentException($"Node '{id}' needs finite coordinates.");
        if (!_nodeIds.Add(id))
            throw new InvalidOperationException($"A node with the identifier '{id}' is already in the graph.");

        _nodes.Add(new GeoNode(id, x, y, name));
        return this;
    }

    /// <summary>
    /// Add an edge. Its ends are checked when the graph is built, so an edge may be added before the
    /// nodes it connects.
    /// </summary>
    /// <param name="sourceId">Identifier of the node the edge starts at.</param>
    /// <param name="targetId">Identifier of the node the edge ends at.</param>
    /// <param name="weight">Value the edge carries, or null.</param>
    /// <param name="directed">Whether the edge has a direction.</param>
    public GeoGraphBuilder Edge(string sourceId, string targetId, double? weight = null, bool directed = false)
    {
        ArgumentException.ThrowIfNullOrEmpty(sourceId);
        ArgumentException.ThrowIfNullOrEmpty(targetId);
        _edges.Add(new GeoEdge(sourceId, targetId, weight, directed));
        return this;
    }

    /// <summary>Build the graph; edges with a missing end are dropped with one warning.</summary>
    public GeoGraph Build() => new(_nodes, _edges);

    /// <summary>
    /// Build a graph from a node table and an edge table: one row per node, one row per edge. A row whose
    /// coordinates or identifiers cannot be read is skipped and counted in
    /// <paramref name="issues"/>, because a table assembled by hand is the normal case here.
    /// </summary>
    /// <param name="nodes">Rows, one per node.</param>
    /// <param name="edges">Rows, one per edge.</param>
    /// <param name="idField">Field holding a node's identifier.</param>
    /// <param name="xField">Field holding a node's horizontal coordinate.</param>
    /// <param name="yField">Field holding a node's vertical coordinate.</param>
    /// <param name="sourceField">Field holding an edge's start node.</param>
    /// <param name="targetField">Field holding an edge's end node.</param>
    /// <param name="nameField">Field holding a node's display name, or null for none.</param>
    /// <param name="weightField">Field holding an edge's value, or null for none.</param>
    /// <param name="issues">Optional collection that receives one line per row that was skipped.</param>
    public static GeoGraph FromRows(IEnumerable<DataRow> nodes, IEnumerable<DataRow> edges,
                                    string idField = "id", string xField = "x", string yField = "y",
                                    string sourceField = "source", string targetField = "target",
                                    string? nameField = null, string? weightField = null,
                                    ICollection<string>? issues = null)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(edges);

        var builder = new GeoGraphBuilder();
        foreach (var row in nodes)
        {
            string? id = Text(row, idField);
            if (id is null || !TryNumber(row, xField, out double x) || !TryNumber(row, yField, out double y))
            {
                issues?.Add($"node row skipped: '{idField}', '{xField}' and '{yField}' are required");
                continue;
            }
            if (builder._nodeIds.Contains(id))
            {
                issues?.Add($"node '{id}' appears more than once; the later row was skipped");
                continue;
            }
            builder.Node(id, x, y, nameField is null ? null : Text(row, nameField));
        }

        foreach (var row in edges)
        {
            string? source = Text(row, sourceField);
            string? target = Text(row, targetField);
            if (source is null || target is null)
            {
                issues?.Add($"edge row skipped: '{sourceField}' and '{targetField}' are required");
                continue;
            }
            double? weight = weightField is not null && TryNumber(row, weightField, out double value) ? value : null;
            builder.Edge(source, target, weight);
        }

        return builder.Build();
    }

    /// <summary>A row's field as text, or null when the row has no usable value for it.</summary>
    private static string? Text(DataRow row, string field)
    {
        if (!row.Has(field)) return null;
        object? value = row.Get(field);
        if (value is null) return null;
        string text = value switch
        {
            string given => given,
            double number => number.ToString("R", CultureInfo.InvariantCulture),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
        };
        return text.Length == 0 ? null : text;
    }

    /// <summary>A row's field as a finite number.</summary>
    private static bool TryNumber(DataRow row, string field, out double value)
    {
        value = 0;
        if (!row.Has(field)) return false;
        object? raw = row.Get(field);
        switch (raw)
        {
            case double number:
                value = number;
                break;
            case null:
                return false;
            default:
                if (!double.TryParse(Convert.ToString(raw, CultureInfo.InvariantCulture),
                                     NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                {
                    return false;
                }
                break;
        }
        return double.IsFinite(value);
    }
}
