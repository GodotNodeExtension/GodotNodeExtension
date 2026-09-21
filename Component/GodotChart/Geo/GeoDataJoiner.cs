namespace GodotNodeExtension.Component.GodotChart;

using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;

/// <summary>
/// What a join does when a row and a geometry do not find each other.
/// </summary>
public enum GeoJoinMissing
{
    /// <summary>
    /// Draw the geometry without a row (the mark paints it as "no data") and print one warning with the
    /// counts, so a table whose keys drifted is visible instead of silently half empty.
    /// </summary>
    Report,

    /// <summary>Same result, no warning: for a chart that intentionally shows only part of the map.</summary>
    Silent,

    /// <summary>Throw instead: for a build step or a test where a mismatch is a bug, not a picture.</summary>
    Strict,
}

/// <summary>
/// The outcome of a join.
/// </summary>
/// <param name="RowOfElement">
/// One entry per feature (node, edge), in the order they were given: the row that matched it, or null
/// when none did. Those nulls are what a mark paints in its "no data" style.
/// </param>
/// <param name="UnmatchedRows">The rows that matched no element, in their input order.</param>
/// <param name="MatchedElementCount">How many elements got a row.</param>
public sealed record GeoJoinResult(IReadOnlyList<DataRow?> RowOfElement,
                                   IReadOnlyList<DataRow> UnmatchedRows,
                                   int MatchedElementCount)
{
    /// <summary>Number of elements the join was asked about.</summary>
    public int ElementCount => RowOfElement.Count;

    /// <summary>How many elements no row matched.</summary>
    public int MissingElementCount => ElementCount - MatchedElementCount;
}

/// <summary>
/// Joins a data table to a geometry: the question a chart has to answer and a map library does not -
/// "which row belongs to this region?".
/// <para>
/// The join is by a string key: a field of the row (default <c>name</c>) against a key of the feature
/// (<c>id</c>, <c>name</c> - whose fallback is the identifier, so map data that carries only an id is
/// still reachable - or the name of any property the source has). Real tables rarely agree on the key
/// down to the character ("Zhejiang" against "Zhejiang Province", "浙江" against "浙江省"), so a
/// comparer can be supplied - and neither side is ever dropped silently: a mismatch is reported, and can
/// be made an error.
/// </para>
/// <para>
/// The result keeps rows and elements apart instead of merging them, because a mark needs both: the
/// geometry to draw and the row to colour it with. It is the row's own <see cref="DataRow"/>, so scales,
/// legend and tooltip work on joined data exactly like they do on plain data.
/// </para>
/// </summary>
public sealed class GeoDataJoiner
{
    private readonly IEqualityComparer<string> _keys;

    /// <summary>Create a joiner.</summary>
    /// <param name="rowField">Field of the data row that holds the key.</param>
    /// <param name="featureKey">
    /// Key of the feature: <c>id</c>, <c>name</c>, or the name of any property the source carries.
    /// </param>
    /// <param name="missing">What to do about a row or an element that finds no partner.</param>
    /// <param name="keyComparer">
    /// How two keys are compared; null uses an exact (ordinal) comparison. See <see cref="LooseKeys"/>
    /// for the usual lenient one.
    /// </param>
    public GeoDataJoiner(string rowField = "name", string featureKey = "name",
                         GeoJoinMissing missing = GeoJoinMissing.Report,
                         IEqualityComparer<string>? keyComparer = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(rowField);
        ArgumentException.ThrowIfNullOrEmpty(featureKey);
        RowField = rowField;
        FeatureKey = featureKey;
        Missing = missing;
        _keys = keyComparer ?? StringComparer.Ordinal;
    }

    /// <summary>The field of the data row that holds the key.</summary>
    public string RowField { get; }

    /// <summary>The key read from the feature.</summary>
    public string FeatureKey { get; }

    /// <summary>What happens when a row and an element do not find each other.</summary>
    public GeoJoinMissing Missing { get; }

    /// <summary>
    /// A comparer that ignores case and surrounding whitespace - the lenient default for data that came
    /// out of a spreadsheet. It does not know the suffixes real place names carry: that comparer is a few
    /// lines of <see cref="IEqualityComparer{T}"/> and has to be written per data set, because deciding
    /// that "Zhejiang" and "Zhejiang Province" are the same place is a domain decision.
    /// </summary>
    public static IEqualityComparer<string> LooseKeys { get; } = new LooseKeyComparer();

    /// <summary>
    /// Join a table to features. A key that appears more than once matches its first element; a second row
    /// for an element that already has one is reported as unmatched instead of overwriting the first.
    /// </summary>
    /// <param name="rows">The table.</param>
    /// <param name="features">The geometry it describes.</param>
    public GeoJoinResult Join(IReadOnlyList<DataRow> rows, IReadOnlyList<GeoFeature> features)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(features);

        var index = new Dictionary<string, int>(features.Count, _keys);
        for (int i = 0; i < features.Count; i++)
        {
            string? key = features[i].LookupKey(FeatureKey);
            if (key is not null) index.TryAdd(key, i);
        }

        return JoinCore(rows, features.Count, index, row => StringKey(Text(row, RowField)));
    }

    /// <summary>Join a table to the nodes of a graph, by <see cref="GeoNode.Id"/>.</summary>
    /// <param name="rows">The table.</param>
    /// <param name="nodes">The nodes it describes.</param>
    public GeoJoinResult Join(IReadOnlyList<DataRow> rows, IReadOnlyList<GeoNode> nodes)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(nodes);

        var index = new Dictionary<string, int>(nodes.Count, _keys);
        for (int i = 0; i < nodes.Count; i++)
            index.TryAdd(nodes[i].Id, i);

        return JoinCore(rows, nodes.Count, index, row => StringKey(Text(row, RowField)));
    }

    /// <summary>
    /// Join a table to the edges of a graph: a row matches the edge whose two ends are its
    /// <paramref name="sourceField"/> and <paramref name="targetField"/> values.
    /// </summary>
    /// <param name="rows">The table.</param>
    /// <param name="edges">The edges it describes.</param>
    /// <param name="sourceField">Field holding an edge's start node.</param>
    /// <param name="targetField">Field holding an edge's end node.</param>
    public GeoJoinResult Join(IReadOnlyList<DataRow> rows, IReadOnlyList<GeoEdge> edges,
                              string sourceField = "source", string targetField = "target")
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(edges);
        ArgumentException.ThrowIfNullOrEmpty(sourceField);
        ArgumentException.ThrowIfNullOrEmpty(targetField);

        var index = new Dictionary<(string Source, string Target), int>(edges.Count, new PairComparer(_keys));
        for (int i = 0; i < edges.Count; i++)
            index.TryAdd((edges[i].SourceId, edges[i].TargetId), i);

        return JoinCore(rows, edges.Count, index, row => PairKey(Text(row, sourceField), Text(row, targetField)));
    }

    /// <summary>
    /// Look up a row's key, or say that it has none: a table is allowed to be missing the field a join
    /// needs, and that row is then reported as unmatched rather than throwing.
    /// </summary>
    /// <typeparam name="TKey">The key type.</typeparam>
    /// <param name="Found">Whether the row carries the key.</param>
    /// <param name="Key">The key, meaningless when it does not.</param>
    private readonly record struct KeyLookup<TKey>(bool Found, TKey Key) where TKey : notnull;

    /// <summary>
    /// The one matching pass the three overloads share: read a key from a row, look it up among the
    /// elements, place the row - then apply the missing strategy.
    /// </summary>
    private GeoJoinResult JoinCore<TKey>(IReadOnlyList<DataRow> rows, int elementCount,
                                         Dictionary<TKey, int> index,
                                         Func<DataRow, KeyLookup<TKey>> keyOfRow) where TKey : notnull
    {
        var rowOfElement = new DataRow?[elementCount];
        var unmatched = new List<DataRow>();
        int matched = 0;

        foreach (var row in rows)
        {
            var lookup = keyOfRow(row);
            int position = lookup.Found && index.TryGetValue(lookup.Key, out int found) ? found : -1;
            if (position < 0 || rowOfElement[position] is not null)
            {
                unmatched.Add(row);
                continue;
            }
            rowOfElement[position] = row;
            matched++;
        }

        var result = new GeoJoinResult(rowOfElement, unmatched, matched);
        Apply(result, rows.Count);
        return result;
    }

    /// <summary>A required string key, or "the row has none".</summary>
    private static KeyLookup<string> StringKey(string? value)
        => new(value is not null, value ?? string.Empty);

    /// <summary>A required (source, target) pair, or "the row has none".</summary>
    private static KeyLookup<(string Source, string Target)> PairKey(string? source, string? target)
        => new(source is not null && target is not null, (source ?? string.Empty, target ?? string.Empty));

    /// <summary>Report, or refuse, an incomplete join; see <see cref="GeoJoinMissing"/>.</summary>
    private void Apply(GeoJoinResult result, int rowCount)
    {
        if (result.MissingElementCount == 0 && result.UnmatchedRows.Count == 0) return;

        string message =
            $"{result.UnmatchedRows.Count} of {rowCount} row(s) matched no '{FeatureKey}', " +
            $"{result.MissingElementCount} of {result.ElementCount} element(s) got no row";

        switch (Missing)
        {
            case GeoJoinMissing.Report:
                GD.PushWarning($"[GodotChart] GeoDataJoiner: {message}.");
                break;
            case GeoJoinMissing.Strict:
                throw new InvalidOperationException(
                    $"GeoDataJoiner: {message} (GeoJoinMissing.Strict).");
        }
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

    /// <summary>Trim + case-insensitive comparison; see <see cref="LooseKeys"/>.</summary>
    private sealed class LooseKeyComparer : IEqualityComparer<string>
    {
        public bool Equals(string? a, string? b)
            => string.Equals((a ?? string.Empty).Trim(), (b ?? string.Empty).Trim(),
                             StringComparison.OrdinalIgnoreCase);

        public int GetHashCode(string value) => StringComparer.OrdinalIgnoreCase.GetHashCode(value.Trim());
    }

    /// <summary>Compares two (source, target) pairs with the configured key comparer.</summary>
    private sealed class PairComparer(IEqualityComparer<string> keys)
        : IEqualityComparer<(string Source, string Target)>
    {
        public bool Equals((string Source, string Target) a, (string Source, string Target) b)
            => keys.Equals(a.Source, b.Source) && keys.Equals(a.Target, b.Target);

        public int GetHashCode((string Source, string Target) pair)
            => HashCode.Combine(keys.GetHashCode(pair.Source), keys.GetHashCode(pair.Target));
    }
}
