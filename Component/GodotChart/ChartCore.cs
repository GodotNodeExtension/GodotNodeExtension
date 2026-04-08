using System;
using System.Collections.Generic;

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
}

// ── Data row (a single record) ────────────────────────────────────
public class DataRow
{
    private readonly Dictionary<string, object> _fields;

    /// <summary>Create a new data row with default capacity.</summary>
    public DataRow() { _fields = new(); }

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
        try { return (T)Convert.ChangeType(value, typeof(T)); }
        catch (Exception ex)
        {
            throw new InvalidCastException(
                $"Cannot convert field '{field}' value '{value}' ({value.GetType().Name}) to {typeof(T).Name}.", ex);
        }
    }

    /// <summary>Get a field value as object. Throws if the field does not exist.</summary>
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
        try { result = (T)Convert.ChangeType(value, typeof(T)); return true; }
        catch { return false; }
    }

    /// <summary>Check whether this row contains a field with the given name.</summary>
    public bool Has(string field)  => _fields.ContainsKey(field);
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
    /// <summary>Name of the data field to read values from.</summary>
    public string FieldName { get; }
    public FieldEncode(string fieldName) => FieldName = fieldName;
}

/// <summary>
/// Encodes a channel with a constant value.
/// </summary>
public class ConstantEncode : IEncodeValue
{
    /// <summary>The constant value for this channel.</summary>
    public object Value { get; }
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
    public string Field { get; set; } = "value";

    /// <summary>
    /// Number of bins. If null, uses Sturges' rule: ceil(1 + log2(n)).
    /// Ignored if <see cref="BinWidth"/> is set.
    /// </summary>
    public int? BinCount { get; set; }

    /// <summary>
    /// Fixed bin width. Takes priority over <see cref="BinCount"/> if set.
    /// </summary>
    public double? BinWidth { get; set; }

    /// <inheritdoc />
    public List<DataRow> Apply(List<DataRow> data)
    {
        if (data.Count == 0) return new List<DataRow>();

        // Extract numeric values
        var values = new List<double>(data.Count);
        foreach (var row in data)
        {
            if (row.TryGet<double>(Field, out var v))
                values.Add(v);
        }
        if (values.Count == 0) return new List<DataRow>();

        double min = double.MaxValue, max = double.MinValue;
        foreach (var v in values)
        {
            if (v < min) min = v;
            if (v > max) max = v;
        }

        // Determine bin count and width
        double range = max - min;
        if (range < 1e-10)
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