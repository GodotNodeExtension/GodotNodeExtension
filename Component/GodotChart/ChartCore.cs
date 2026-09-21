using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace GodotNodeExtension.Component.GodotChart;

// ── Visual channel enum ──────────────────────────────────────────────────────
/// <summary>
/// Visual channels that map data fields to graphical properties.
/// </summary>
public enum Channel
{
    /// <summary>Horizontal position.</summary>
    X,
    /// <summary>Vertical position (left axis).</summary>
    Y,
    /// <summary>Secondary vertical position (right axis).</summary>
    Y2,
    /// <summary>Color encoding.</summary>
    Color,
    /// <summary>Size encoding.</summary>
    Size,
    /// <summary>Opacity encoding.</summary>
    Opacity,
    /// <summary>Shape encoding.</summary>
    Shape,
    /// <summary>Label text encoding.</summary>
    Label,
}

// ── Mark coordinate system ───────────────────────────────────────────────────
/// <summary>
/// Coordinate system type used by a mark. Marks with different coordinate
/// types cannot coexist in the same chart; the renderer will skip
/// incompatible marks and emit a warning.
/// </summary>
public enum MarkCoordinate
{
    /// <summary>Cartesian X/Y axis (Bar, Line, Point, Area, etc.).</summary>
    Cartesian,
    /// <summary>Polar/Radial layout (Pie, Radar, Gauge).</summary>
    Polar,
    /// <summary>Hierarchical tree layout (Treemap, Sunburst).</summary>
    Hierarchical,
    /// <summary>Relational flow layout (Sankey, Chord).</summary>
    Flow,
    /// <summary>Geographic layout: data placed in a coordinate frame and projected through it.</summary>
    Geographic,
}

// ── Channel roles ────────────────────────────────────────────────────────────
/// <summary>
/// What each <see cref="Channel"/> is used for, declared once instead of being restated at every call
/// site: which channels take part in the automatic fit, whether their fitted domain keeps the zero
/// baseline, and which axis (and axis configuration) they are drawn on. A new dimension or channel is
/// added here, not in the places that consume the answers.
/// </summary>
internal static class ChannelRoles
{
    /// <summary>
    /// Channels whose fitted domain is recorded, re-clamped and restored by the interactive window:
    /// the two positions the plot is laid out from and the three encodings driven by a magnitude.
    /// </summary>
    internal static ReadOnlySpan<Channel> DomainChannels =>
        [Channel.X, Channel.Y, Channel.Y2, Channel.Size, Channel.Opacity];

    /// <summary>
    /// Channels drawn on a value axis, in the order the renderers draw them.
    /// </summary>
    internal static ReadOnlySpan<Channel> ValueAxisChannels => [Channel.Y, Channel.Y2];

    /// <summary>
    /// Whether the automatically fitted domain of a channel keeps the zero baseline: the value and
    /// opacity axes do (0 value is the baseline, 0 opacity is invisible), while a numeric category axis
    /// (a sample index, a timestamp, a frequency) and the size channel do not - forcing zero would
    /// squeeze a rolling window into one corner of the plot, and a bubble's radius encodes the
    /// magnitude *inside* the column, so with zero included every bubble would sit near the top of the
    /// range and <c>PointSizeMin</c> would never be reached.
    /// </summary>
    internal static bool KeepsZeroBaseline(Channel channel) => channel is not (Channel.X or Channel.Size);

    /// <summary>
    /// The axis configuration a channel's axis reads. The secondary Y axis is the fallback because it
    /// is the only axis whose channel is not implied by its position; the X axis is the only other
    /// channel with an axis of its own.
    /// </summary>
    /// <param name="channel">Channel whose axis is asked for.</param>
    /// <param name="x">Configuration of the X axis.</param>
    /// <param name="y">Configuration of the primary Y axis.</param>
    /// <param name="secondary">Configuration of the secondary (Y2) axis.</param>
    internal static AxisConfig? AxisConfigOf(Channel channel, AxisConfig? x, AxisConfig? y, AxisConfig? secondary)
        => channel switch
        {
            Channel.X => x,
            Channel.Y => y,
            _ => secondary,
        };
}

// ── Data row (a single record) ────────────────────────────────────
/// <summary>
/// A single data record: a bag of named fields. Rows in one data set may have different fields;
/// <see cref="Has"/> distinguishes "missing" from "present but null".
/// </summary>
public class DataRow
{
    private readonly Dictionary<string, object> _fields;

    /// <summary>Create a new data row with default capacity.</summary>
    public DataRow() { _fields = []; }

    /// <summary>Create a new data row with the given initial field capacity.</summary>
    public DataRow(int fieldCapacity) { _fields = new(fieldCapacity); }

    /// <summary>Set a field value on this row. Returns this row for fluent chaining.</summary>
    public DataRow Set(string field, object value)
    { _fields[field] = value; return this; }

    /// <summary>
    /// Get a field value cast to <typeparamref name="T"/>.
    /// Throws <see cref="KeyNotFoundException"/> if the field does not exist,
    /// or <see cref="InvalidCastException"/> if the type conversion fails.
    /// </summary>
    public T Get<T>(string field)
    {
        if (!_fields.TryGetValue(field, out var value))
            throw new KeyNotFoundException($"DataRow does not contain field '{field}'.");
        if (value is T typed) return typed;
        try { return (T)Convert.ChangeType(value, typeof(T), CultureInfo.InvariantCulture); }
        catch (Exception ex)
        {
            // A named-but-null field cannot be converted to a value type; report that as a normal
            // conversion failure instead of dereferencing null while building the message.
            string text = value is { } present ? present.ToString() ?? "null" : "null";
            string type = value is { } known ? known.GetType().Name : "null";
            throw new InvalidCastException(
                $"Cannot convert field '{field}' value '{text}' ({type}) to {typeof(T).Name}.", ex);
        }
    }

    /// <summary>
    /// Get a field value as object. Throws if the field does not exist.
    /// <para>
    /// The return type stays non-nullable for the common case (a row whose value is a number or a
    /// string), but a row may also store an explicit null - <see cref="Set"/> accepts one - so the
    /// return value carries a <c>[return: MaybeNull]</c> annotation: callers that need the value itself
    /// check for it, and the ones that only pass it on keep their signatures.
    /// </para>
    /// </summary>
    [return: MaybeNull]
    public object Get(string field)
    {
        if (!_fields.TryGetValue(field, out var value))
            throw new KeyNotFoundException($"DataRow does not contain field '{field}'.");
        return value;
    }

    /// <summary>Try to get a typed field value. Returns false if missing or type mismatch.</summary>
    public bool TryGet<T>(string field, out T result)
    {
        result = default!;
        if (!_fields.TryGetValue(field, out var value)) return false;
        if (value is T typed) { result = typed; return true; }
        try { result = (T)Convert.ChangeType(value, typeof(T), CultureInfo.InvariantCulture); return true; }
        catch { return false; }
    }

    /// <summary>Check whether this row contains a field with the given name.</summary>
    public bool Has(string field)  => _fields.ContainsKey(field);

    /// <summary>
    /// The fields of this row, keyed by field name. A read-only view of the stored values, for
    /// serialisation and for inspecting what a row carries; values keep the type they were set with.
    /// </summary>
    public IReadOnlyDictionary<string, object> Fields => _fields;
}

// ── Encode spec: field name or constant value ────────────────────────────

/// <summary>
/// Marker interface for encode value specifications.
/// An encode maps a visual channel to either a data field or a constant.
/// </summary>
public interface IEncodeValue;

/// <summary>
/// Encodes a channel from a data field by name.
/// </summary>
public class FieldEncode : IEncodeValue
{
    /// <summary>Create an encode that reads the channel value from the named data field.</summary>
    public FieldEncode(string fieldName) => FieldName = fieldName;

    /// <summary>Name of the data field to read values from.</summary>
    public string FieldName { get; }
}

/// <summary>
/// Encodes a channel with a constant value.
/// </summary>
public class ConstantEncode : IEncodeValue
{
    /// <summary>The constant value for this channel.</summary>
    public object Value { get; }
    /// <summary>Create an encode that always yields the same value for the channel.</summary>
    public ConstantEncode(object value) => Value = value;
}

// ── Data Transform ───────────────────────────────────────────────────────

/// <summary>
/// Data transform pipeline: processes raw data rows before mark rendering.
/// </summary>
public interface IDataTransform
{
    /// <summary>Transform input rows into output rows.</summary>
    List<DataRow> Apply(List<DataRow> data);
}

/// <summary>
/// Bins continuous numeric data into equal-width intervals.
/// Output rows contain "BinStart", "BinEnd", "BinMid", and "Count" fields.
/// </summary>
public class BinTransform : IDataTransform
{
    /// <summary>Field name containing the numeric values to bin.</summary>
    public string Field { get; init; } = "value";

    /// <summary>
    /// Number of bins. If null, uses Sturges' rule: ceil(1 + log2(n)).
    /// Ignored when <see cref="BinWidth"/> is a positive value.
    /// </summary>
    public int? BinCount { get; init; }

    /// <summary>
    /// Fixed bin width. Takes priority over <see cref="BinCount"/> if set.
    /// </summary>
    public double? BinWidth { get; init; }

    /// <inheritdoc />
    public List<DataRow> Apply(List<DataRow> data)
    {
        if (data.Count == 0) return [];

        // Extract numeric values. Non-finite entries (NaN / infinity, typically a failed parse or a
        // missing measurement) are skipped: they cannot be compared or binned, and would otherwise
        // poison the range and the bin index.
        var values = new List<double>(data.Count);
        foreach (var row in data)
        {
            if (row.TryGet<double>(Field, out var v) && double.IsFinite(v))
                values.Add(v);
        }
        if (values.Count == 0) return [];

        double min = double.MaxValue, max = double.MinValue;
        foreach (var v in values)
        {
            if (v < min) min = v;
            if (v > max) max = v;
        }

        // Determine bin count and width. Relative degeneracy test: an absolute epsilon misses
        // equal-value data in a huge range and misfires in a tiny one (see ScaleMath).
        double range = max - min;
        if (ScaleMath.IsDegenerate(min, max))
        {
            // All values are the same — return a single bin
            return
            [
                new DataRow()
                    .Set("BinStart", min - 0.5)
                    .Set("BinEnd", min + 0.5)
                    .Set("BinMid", min)
                    .Set("Count", values.Count)
            ];
        }

        int binCount;
        double binWidth;
        if (BinWidth is > 0)
        {
            binWidth = BinWidth.Value;
            binCount = (int)Math.Ceiling(range / binWidth);
        }
        else
        {
            binCount = BinCount ?? (int)Math.Ceiling(1.0 + Math.Log2(values.Count));
            binCount = Math.Max(1, binCount);
            binWidth = range / binCount;
        }

        // Count values in each bin
        var counts = new int[binCount];
        foreach (var v in values)
        {
            int idx = (int)((v - min) / binWidth);
            if (idx >= binCount) idx = binCount - 1; // clamp max value
            counts[idx]++;
        }

        // Build output rows
        var result = new List<DataRow>(binCount);
        for (int i = 0; i < binCount; i++)
        {
            double start = min + i * binWidth;
            double end = start + binWidth;
            result.Add(new DataRow()
                .Set("BinStart", start)
                .Set("BinEnd", end)
                .Set("BinMid", (start + end) / 2.0)
                .Set("Count", counts[i]));
        }
        return result;
    }
}