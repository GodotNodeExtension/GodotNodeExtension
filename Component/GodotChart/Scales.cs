using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;

namespace GodotNodeExtension.Component.GodotChart;
/// <summary>
/// Numeric helpers shared by the scale implementations.
/// </summary>
internal static class ScaleMath
{
    /// <summary>
    /// Whether a domain range is degenerate (all values equal).
    /// Uses a <b>relative</b> tolerance: an absolute epsilon either never triggers for large
    /// domains (division by a near-zero range produces infinity) or triggers immediately for tiny
    /// ones.
    /// </summary>
    public static bool IsDegenerate(double min, double max)
    {
        double scale = Math.Max(Math.Abs(min), Math.Abs(max));
        return Math.Abs(max - min) <= Math.Max(scale, 1.0) * 1e-12;
    }

    /// <summary>
    /// The numeric range of a domain, with the values that are not usable numbers (null, a non-numeric string,
    /// a non-finite number) skipped: that is the rule every scale's <c>Fit</c> follows, so one dirty field can
    /// neither lose the whole fit nor poison it with NaN. <paramref name="positiveOnly"/> is the logarithm's
    /// extra rule - a log has no value at or below zero.
    /// </summary>
    /// <param name="domain">Raw values to scan.</param>
    /// <param name="min">Smallest usable value (meaningless when the answer is false).</param>
    /// <param name="max">Largest usable value.</param>
    /// <param name="positiveOnly">Skip values at or below zero as well.</param>
    /// <param name="values">When given, collects every usable value in scan order.</param>
    /// <returns>True when at least one usable value was seen.</returns>
    public static bool TryMinMax(IEnumerable<object> domain, out double min, out double max,
        bool positiveOnly = false, List<double>? values = null)
    {
        min = double.MaxValue;
        max = double.MinValue;
        bool any = false;

        foreach (var item in domain)
        {
            if (!ScaleConvert.TryToDouble(item, out double v)) continue;
            if (positiveOnly && v <= 0) continue;

            if (v < min) min = v;
            if (v > max) max = v;
            any = true;
            values?.Add(v);
        }
        return any;
    }
}

// ── Scale base interface: input raw value, output [0,1] normalized value ────────
/// <summary>
/// Base interface for scales. Maps raw data values to normalized [0, 1] range.
/// </summary>
public interface IScale
{
    /// <summary>Infer the domain range from data.</summary>
    void   Fit(IEnumerable<object> domain);
    /// <summary>Map a raw value to [0, 1].</summary>
    double Map(object value);
    /// <summary>Format a raw value for display.</summary>
    string Format(object value);
}

/// <summary>
/// A scale that maps raw values to colors.
/// Consumers must test for this interface instead of a concrete color scale type, so that
/// categorical, sequential, diverging and user-defined color scales all keep working.
/// </summary>
public interface IColorScale : IScale
{
    /// <summary>Map a raw value to a color.</summary>
    Color MapColor(object value);
}

/// <summary>
/// A color scale with discrete categories. Only scales implementing this interface can drive a
/// legend, because a legend needs the ordered category keys.
/// </summary>
public interface ICategoricalColorScale : IColorScale
{
    /// <summary>Category keys in legend order.</summary>
    IReadOnlyList<string> Domain { get; }
}

/// <summary>
/// Shared helper for ordinal domain collection used by OrdinalScale, ColorScale, RadialScale, etc.
/// </summary>
internal static class OrdinalDomainHelper
{
    /// <summary>
    /// Clear and rebuild the ordinal domain collections from the given data.
    /// </summary>
    public static void Fit(
        List<string> domain, HashSet<string> domainSet,
        Dictionary<string, int> indexMap, IEnumerable<object> data)
    {
        domain.Clear();
        domainSet.Clear();
        indexMap.Clear();
        foreach (var v in data)
        {
            // A null has no category; skip it instead of turning it into an empty-string key.
            if (v is not { }) continue;
            var s = v.ToString()!;
            if (domainSet.Add(s))
            {
                indexMap[s] = domain.Count;
                domain.Add(s);
            }
        }
    }

    /// <summary>
    /// The category key of a raw channel value: its text, or an empty string when the value is null (or
    /// its <c>ToString()</c> is). A row may hold a null even though a channel value is typed as a
    /// non-nullable <see cref="object"/>, and a scale maps such a value to its "unknown" category.
    /// </summary>
    public static string KeyOf(object? value)
        => value is { } raw ? raw.ToString() ?? string.Empty : string.Empty;
}


/// <summary>
/// State and behaviour shared by every scale that maps a <b>category</b> to a position: the domain (the keys in
/// the order they were fitted, first category at the low end), the key → index lookup, and the conversion from
/// a raw channel value to its category key.
/// <para>
/// What a category index <i>means</i> is the derived scale's decision: <see cref="OrdinalScale"/> centres it in
/// its cell, <see cref="BandScale"/> gives it a padded band, <see cref="RadialScale"/> starts at the origin,
/// and the colour/shape scales index into their palette. They share this base instead of each carrying its own
/// copy of the three collections and the fit rule.
/// </para>
/// </summary>
public abstract class OrdinalScaleBase : IScale
{
    private readonly List<string> _domain = [];
    private readonly HashSet<string> _domainSet = [];
    private readonly Dictionary<string, int> _indexMap = [];

    /// <summary>Categories in fitted order; the first category sits at the low end of the axis.</summary>
    public IReadOnlyList<string> Domain => _domain;

    /// <summary>Number of categories in the domain.</summary>
    protected int Count => _domain.Count;

    /// <summary>Returns the zero-based index of <paramref name="key"/> in the domain, or -1 if not found.</summary>
    public int IndexOf(string key) => _indexMap.GetValueOrDefault(key, -1);

    /// <summary>
    /// The category's position in the domain spread over [0, 1] (0 for an unknown category). One category sits
    /// in the middle: a degenerate domain has no ends to spread over. The positional scales of a legend
    /// (<see cref="ColorScale"/>, <see cref="ShapeScale"/>) both answer exactly this.
    /// </summary>
    protected double MapToSpan(object value)
    {
        if (!TryGetIndex(value, out int idx)) return 0;
        if (Count <= 1) return 0.5;
        return (double)idx / (Count - 1);
    }

    /// <summary>
    /// The category key of a raw channel value: its text, or an empty string when the value is null (or its
    /// <c>ToString()</c> is) - the "unknown" category a scale maps such a value to.
    /// </summary>
    /// <param name="value">Raw channel value.</param>
    /// <returns>The key the domain would hold for it.</returns>
    protected static string KeyOf(object? value) => OrdinalDomainHelper.KeyOf(value);

    /// <summary>
    /// Look up the category index of a raw channel value.
    /// </summary>
    /// <param name="value">Raw channel value.</param>
    /// <param name="index">Index in <see cref="Domain"/> when the value is a category of this domain.</param>
    /// <returns>False when the value is not in the domain; the caller decides what "no category" means (all of
    /// them map it to the low end rather than inventing a position).</returns>
    protected bool TryGetIndex(object? value, out int index)
    {
        var key = KeyOf(value);
        return _indexMap.TryGetValue(key, out index);
    }

    /// <inheritdoc />
    public void Fit(IEnumerable<object> domain)
        => OrdinalDomainHelper.Fit(_domain, _domainSet, _indexMap, domain);

    /// <summary>
    /// Position of a category on the normalized [0, 1] axis, or the low end when the value is not a category of
    /// this domain. The rule is the derived scale's.
    /// </summary>
    /// <param name="value">Raw channel value.</param>
    /// <returns>The normalized position.</returns>
    public abstract double Map(object value);

    /// <summary>The category key of a value - the domain is made of keys, so this is what "format" means here.</summary>
    /// <param name="value">Raw channel value.</param>
    /// <returns>The key.</returns>
    public string Format(object value) => KeyOf(value);
}

/// <summary>
/// Shared numeric conversion helper for scale classes.
/// Provides a fast path for double/float values and a safe fallback with diagnostics.
/// </summary>
internal static class ScaleConvert
{
    /// <summary>Convert a data value to double with fast-path optimization and safe error handling.</summary>
    public static double ToDouble(object value, string scaleName)
    {
        if (value is double d) return d;
        if (value is float f) return f;
        if (value is int i) return i;
        if (value is long l) return l;
        try { return Convert.ToDouble(value, CultureInfo.InvariantCulture); }
        catch (Exception ex)
        {
            throw new InvalidCastException(
                $"[{scaleName}] Cannot convert value '{value}' ({value.GetType().Name}) to double.", ex);
        }
    }

    /// <summary>
    /// Convert a data value to double, reporting failure instead of throwing: null, a non-numeric value
    /// (a colour string fed to a numeric channel, say) and a non-finite number all have no place on a
    /// numeric axis. The scale <c>Fit</c> implementations use this so one dirty field can neither throw
    /// mid-fit nor poison the domain with <c>NaN</c>, while <see cref="ToDouble"/> keeps its throwing
    /// contract for the mapping path.
    /// </summary>
    public static bool TryToDouble(object? value, out double result)
    {
        result = 0;
        if (value is null) return false;

        bool converted;
        double convertedValue;
        switch (value)
        {
            case double d: convertedValue = d; converted = true; break;
            case float f: convertedValue = f; converted = true; break;
            case int i: convertedValue = i; converted = true; break;
            case long l: convertedValue = l; converted = true; break;
            case decimal m: convertedValue = (double)m; converted = true; break;
            case string s:
                converted = double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture,
                                            out convertedValue);
                break;
            default:
                try { convertedValue = Convert.ToDouble(value, CultureInfo.InvariantCulture); converted = true; }
                catch (Exception) { convertedValue = 0; converted = false; }
                break;
        }

        if (!converted || !double.IsFinite(convertedValue)) return false;
        result = convertedValue;
        return true;
    }
}

/// <summary>
/// Continuous numeric scale that maps a [Min, Max] domain linearly to [0, 1].
/// </summary>
public class LinearScale : IScale
{
    /// <summary>Minimum of the domain (set by the constructor or <see cref="Fit"/>).</summary>
    public double Min { get; private set; }
    /// <summary>Maximum of the domain (set by the constructor or <see cref="Fit"/>).</summary>
    public double Max { get; private set; }
    /// <summary>
    /// When true (default), <see cref="Fit"/> extends a non-negative domain down to 0 so bars and
    /// areas have a natural baseline. Only the lower bound is affected.
    /// </summary>
    public bool   IncludeZero { get; init; } = true;

    /// <summary>
    /// Keep the values an axis may label: sorted, deduplicated, and sampled down to
    /// <see cref="MaxFittedValues"/> when the table holds more values than an axis could ever show. The
    /// sampling runs over the *distinct* values (keeping every k-th row would keep duplicates and drop the
    /// end of the range), and the first and last value always survive.
    /// </summary>
    private void RecordFittedValues(List<double> values)
    {
        _fittedValues.Clear();
        if (values.Count == 0) return;

        values.Sort();
        _fittedValues.Add(values[0]);
        for (int i = 1; i < values.Count; i++)
        {
            // .Equals, not ==: dropping a repeat has to be exact - a tolerance would merge distinct data values
            // the axis is meant to tick on.
            if (values[i].Equals(_fittedValues[^1])) continue;
            _fittedValues.Add(values[i]);
        }
        if (_fittedValues.Count <= MaxFittedValues) return;

        int stride = (int)Math.Ceiling(_fittedValues.Count / (double)MaxFittedValues);
        var sampled = new List<double>(MaxFittedValues + 1);
        for (int i = 0; i < _fittedValues.Count; i += stride) sampled.Add(_fittedValues[i]);
        if (!sampled[^1].Equals(_fittedValues[^1])) sampled.Add(_fittedValues[^1]);

        _fittedValues.Clear();
        _fittedValues.AddRange(sampled);
    }

    /// <summary>The nice tick result computed by the constructor or by <see cref="Fit"/>.</summary>
    public NiceTickResult NiceTicks { get; private set; }

    /// <summary>Create a linear scale whose domain is fitted from the data.</summary>
    public LinearScale() { }

    /// <summary>Create a linear scale with a fixed domain and pre-computed nice ticks.</summary>
    /// <param name="min">Domain minimum.</param>
    /// <param name="max">Domain maximum.</param>
    public LinearScale(double min, double max)
    {
        Min = min; Max = max;
        NiceTicks = NiceTickGenerator.Compute(min, max);
    }

    /// <summary>
    /// The distinct values the scale was fitted with, sorted (see <see cref="Fit"/>). An axis over such a
    /// channel puts its ticks on values that really exist in the data instead of on arithmetic "nice numbers"
    /// between them: over 1970-2023 the domain-derived ticks are 1982.25 / 1982.5 / 1982.75, and none of
    /// those is a year in the table.
    /// </summary>
    public IReadOnlyList<double> FittedValues => _fittedValues;

    private readonly List<double> _fittedValues = [];

    /// <summary>Values kept for <see cref="FittedValues"/>: far more than an axis can label, and bounded.</summary>
    private const int MaxFittedValues = 2048;

    /// <inheritdoc />
    public void Fit(IEnumerable<object> domain)
    {
        var values = new List<double>();
        if (!ScaleMath.TryMinMax(domain, out double dataMin, out double dataMax, values: values))
        {
            _fittedValues.Clear();
            return; // empty
        }
        RecordFittedValues(values);

        if (IncludeZero) dataMin = Math.Min(0, dataMin);

        var nice = NiceTickGenerator.Compute(dataMin, dataMax);
        Min = nice.Min;
        Max = nice.Max;
        NiceTicks = nice;
    }

    /// <summary>
    /// Pin the domain, overriding whatever <see cref="Fit"/> computed (and recomputing the axis ticks).
    /// Used by <see cref="Chart.ScaleDomain(Channel, double, double)"/> so an axis can be locked without
    /// the caller having to keep a scale instance around. The arguments are ordered, so a reversed call is
    /// swapped rather than producing a negative range.
    /// </summary>
    public void SetDomain(double min, double max)
    {
        if (max < min) (min, max) = (max, min);
        Min = min;
        Max = max;
        NiceTicks = NiceTickGenerator.Compute(min, max);
    }

    /// <summary>
    /// Normalized position of <paramref name="value"/>, or <see cref="double.NaN"/> when it has none (null,
    /// text, a non-finite number) - the answer every other scale gives for the same input, so a caller can
    /// skip the element instead of drawing at a garbage coordinate.
    /// </summary>
    /// <param name="value">Value to map.</param>
    /// <returns>The position in <c>[0, 1]</c>, or NaN for a value without one.</returns>
    public double Map(object value)
    {
        if (!ScaleConvert.TryToDouble(value, out double v)) return double.NaN;
        // A degenerate domain (every value equal) has no ends to spread over, so the value sits in the middle -
        // the convention the ordinal, colour and time scales already used, and the position the single tick of
        // such an axis is drawn at (see ComputeTicks). Returning 0 put the mark at the start of the axis while
        // its label sat in the middle.
        if (ScaleMath.IsDegenerate(Min, Max)) return 0.5;
        return (v - Min) / (Max - Min);
    }

    /// <inheritdoc />
    /// <summary>
    /// Tick label text. Six significant digits, not four: at a deep zoom the ticks of a 1970-2023 axis land
    /// on fractional years (1982.25, 1982.5, ...) and four digits collapsed all of them to "1982" - three
    /// identical labels on one axis. Six keeps them apart (and also stops 50000 from turning into "5E+04").
    /// </summary>
    public string Format(object value) => ScaleConvert.ToDouble(value, nameof(LinearScale)).ToString("G6", CultureInfo.InvariantCulture);
}

/// <summary>
/// Ordinal scale that maps discrete categories to evenly spaced positions [0, 1].
/// </summary>
public class OrdinalScale : OrdinalScaleBase
{
    /// <inheritdoc />
    public override double Map(object value)
    {
        if (!TryGetIndex(value, out int idx)) return 0;
        // Center-aligned: return the center of each cell
        return Count == 1 ? 0.5 : (idx + 0.5) / Count;
    }
}

// ── Color scale (category → color) ───────────────────────────
/// <summary>
/// Reads colours out of raw channel values: a <see cref="Color"/> is taken as it is, and a string in
/// HTML hexadecimal form (<c>#rgb</c>, <c>#rgba</c>, <c>#rrggbb</c>, <c>#rrggbbaa</c>) is parsed.
/// <para>
/// Only these two forms count, on purpose: naming a category <c>red</c> or <c>blue</c> must not turn it
/// into a colour, which is why the named-colour parser is not used here. Everything else is not a colour.
/// </para>
/// </summary>
internal static class ColorValues
{
    /// <summary>Try to read <paramref name="value"/> as a colour.</summary>
    public static bool TryParse(object? value, out Color color)
    {
        switch (value)
        {
            case Color direct:
                color = direct;
                return true;
            case string { Length: > 0 } text when text[0] == '#' && Color.HtmlIsValid(text):
                color = Color.FromHtml(text);
                return true;
            default:
                color = default;
                return false;
        }
    }
}

/// <summary>
/// Colour scale that uses the value itself when the value is a colour (<see cref="Color"/>, or an HTML
/// hexadecimal string such as <c>"#rrggbb"</c>), and a categorical palette colour for values that are
/// not. This is G2's <c>identity</c> colour scale: the data carries the colours.
/// <para>
/// It is not a categorical scale, so it never drives a legend - the values are colours, not categories -
/// and the palette is only the fallback for the values that are not colours.
/// </para>
/// </summary>
public class IdentityColorScale : IColorScale
{
    private readonly ColorScale _fallback = new();

    /// <summary>
    /// Colours used for values that are not colours, in order of first appearance (cycled when there are
    /// more values than entries). Defaults to <see cref="ChartTheme.DefaultPalette"/>.
    /// </summary>
    public Color[] Palette
    {
        get => _fallback.Palette;
        set => _fallback.Palette = value;
    }

    /// <inheritdoc />
    public void Fit(IEnumerable<object> domain)
        => _fallback.Fit(domain.Where(value => !ColorValues.TryParse(value, out _)));

    // Returns [0,1] index (actual color obtained via MapColor)
    /// <inheritdoc />
    public double Map(object value) => _fallback.Map(value);

    /// <inheritdoc />
    public Color MapColor(object value)
        => ColorValues.TryParse(value, out var color) ? color : _fallback.MapColor(value);

    /// <inheritdoc />
    public string Format(object value) => OrdinalDomainHelper.KeyOf(value);
}

/// <summary>
/// Color scale that maps discrete categories to colors.
/// </summary>
public class ColorScale : OrdinalScaleBase, ICategoricalColorScale
{
    /// <summary>
    /// Colours assigned to the categories in domain order (cycled when there are more categories).
    /// Defaults to <see cref="ChartTheme.DefaultPalette"/> (which hands out a fresh copy, so this
    /// array is never aliased by another scale or by the static default); an empty array is treated
    /// as the default instead of being an error.
    /// </summary>
    public Color[] Palette { get; set; } = ChartTheme.DefaultPalette;

    // Returns [0,1] index (actual color obtained via MapColor)
    /// <inheritdoc />
    public override double Map(object value) => MapToSpan(value);

    /// <inheritdoc />
    public Color MapColor(object value)
    {
        var palette = Palette is { Length: > 0 } ? Palette : ChartTheme.DefaultPalette;
        if (!TryGetIndex(value, out int idx)) return palette[0];
        return palette[idx % palette.Length];
    }
}

// ── Radial angle scale (category → evenly spaced angle [0,1], multiply by 2π for radians) ──
/// <summary>
/// Radial scale that maps ordinal categories to evenly spaced positions [0, 1].
/// Multiply by 2π to convert to radians for polar coordinate charts.
/// </summary>
/// <remarks>
/// Not consumed by the built-in marks: the polar marks compute their radii themselves. Informational,
/// reserved for custom marks.
/// </remarks>
public class RadialScale : OrdinalScaleBase
{
    /// <summary>
    /// Map a category to its normalized position [0, 1).
    /// Each category occupies an equal arc segment.
    /// </summary>
    public override double Map(object value)
    {
        if (!TryGetIndex(value, out int idx)) return 0;
        return Count == 0 ? 0 : (double)idx / Count;
    }
}

/// <summary>
/// Symbol shapes a mark can draw. This is the vocabulary of the <see cref="Channel.Shape"/> channel and
/// of <see cref="ShapeScale"/>.
/// </summary>
public enum ShapeKind
{
    /// <summary>Filled disc (the default).</summary>
    Circle,

    /// <summary>Axis-aligned square.</summary>
    Square,

    /// <summary>Triangle pointing up.</summary>
    Triangle,

    /// <summary>Rotated square.</summary>
    Diamond,

    /// <summary>Plus sign.</summary>
    Cross,

    /// <summary>Five-pointed star.</summary>
    Star,
}

/// <summary>
/// A scale that maps raw values to shapes - the <see cref="Channel.Shape"/> channel's counterpart of a
/// colour scale. Consumers must test for this interface instead of a concrete shape scale type.
/// </summary>
public interface IShapeScale : IScale
{
    /// <summary>Map a raw value to a shape.</summary>
    ShapeKind MapShape(object? value);
}

/// <summary>
/// A shape scale with discrete categories. Only scales implementing this interface can drive a
/// legend, because a legend needs the ordered category keys.
/// </summary>
public interface ICategoricalShapeScale : IShapeScale
{
    /// <summary>Category keys in legend order.</summary>
    IReadOnlyList<string> Domain { get; }
}

/// <summary>
/// Categorical shape scale: every distinct value gets the next shape of the range, cycling when there
/// are more categories than shapes.
/// </summary>
public class ShapeScale : OrdinalScaleBase, ICategoricalShapeScale
{
    /// <summary>
    /// Shapes assigned to the categories in domain order (cycled when there are more categories).
    /// Defaults to <see cref="DefaultShapes"/>; an empty array falls back to the default.
    /// </summary>
    public ShapeKind[] Shapes { get; set; } = (ShapeKind[])DefaultShapes.Clone();

    /// <summary>
    /// The built-in vocabulary, ordered so that neighbouring categories look different.
    /// <para>
    /// Read-only by copy, like <see cref="ChartTheme.DefaultPalette"/>: every read hands out a fresh
    /// array, so writing into the result cannot change the default of a scale built afterwards.
    /// </para>
    /// </summary>
    public static ShapeKind[] DefaultShapes => (ShapeKind[])DefaultShapesSource.Clone();

    private static readonly ShapeKind[] DefaultShapesSource =
    [
        ShapeKind.Circle, ShapeKind.Square, ShapeKind.Triangle,
        ShapeKind.Diamond, ShapeKind.Cross, ShapeKind.Star
    ];

    /// <inheritdoc />
    public override double Map(object value) => MapToSpan(value);

    /// <inheritdoc />
    public ShapeKind MapShape(object? value)
    {
        // The private source, not the public property: this runs once per element and the property hands
        // out a defensive copy.
        var shapes = Shapes is { Length: > 0 } ? Shapes : DefaultShapesSource;
        if (!TryGetIndex(value, out int idx)) return shapes[0];
        return shapes[idx % shapes.Length];
    }
}

/// <summary>
/// Sequential color scale that maps continuous numeric values to a color gradient.
/// Useful for heatmaps and other color-intensity visualizations.
/// </summary>
public class SequentialColorScale : IColorScale
{
    /// <summary>Minimum value of the domain.</summary>
    public double Min { get; private set; }

    /// <summary>Maximum value of the domain.</summary>
    public double Max { get; private set; }

    /// <summary>
    /// Color stops defining the gradient from low to high values.
    /// <para>
    /// Interpolation happens in the stored sRGB components (like <c>Color.Lerp</c> in Godot), which
    /// can look slightly dark/muddy in the middle of a long ramp. A perceptually uniform
    /// interpolation (e.g. Oklab) is not implemented yet.
    /// </para>
    /// </summary>
    public Color[] Gradient { get; set; } = (Color[])DefaultGradient.Clone();

    /// <summary>
    /// Gradient used when <see cref="Gradient"/> is empty (dark purple → purple → red → orange →
    /// light yellow). An empty array means "use this default", never an error.
    /// <para>
    /// Read-only by copy, like <see cref="ChartTheme.DefaultPalette"/>: every read hands out a fresh
    /// array, so writing into the result cannot change the default of a scale built afterwards.
    /// </para>
    /// </summary>
    public static Color[] DefaultGradient => (Color[])DefaultGradientSource.Clone();

    private static readonly Color[] DefaultGradientSource =
    [
        new(0.12f, 0.07f, 0.53f), // dark purple
        new(0.29f, 0.00f, 0.73f), // purple
        new(0.85f, 0.24f, 0.31f), // red
        new(0.99f, 0.68f, 0.38f), // orange
        new(0.99f, 0.95f, 0.70f), // light yellow
    ];

    /// <summary>Create a sequential colour scale; the domain is fitted from the data.</summary>
    public SequentialColorScale() { }

    /// <summary>Create a sequential colour scale with a fixed domain.</summary>
    /// <param name="min">Value mapped to the start of the gradient.</param>
    /// <param name="max">Value mapped to the end of the gradient.</param>
    public SequentialColorScale(double min, double max) { Min = min; Max = max; }

    /// <inheritdoc />
    public void Fit(IEnumerable<object> domain)
    {
        if (!ScaleMath.TryMinMax(domain, out double min, out double max)) return;
        Min = min;
        Max = max;
    }

    /// <summary>Map a value to [0, 1] within the domain range.</summary>
    public double Map(object value)
    {
        double v = ScaleConvert.ToDouble(value, nameof(SequentialColorScale));
        if (!double.IsFinite(v)) return double.NaN;
        // The one degenerate-domain convention of the family (see LinearScale.Map): the middle of the ramp.
        if (ScaleMath.IsDegenerate(Min, Max)) return 0.5;
        return Math.Clamp((v - Min) / (Max - Min), 0, 1);
    }

    /// <summary>Map a value directly to an interpolated gradient color.</summary>
    public Color MapColor(object value)
    {
        double t = Map(value);
        // The private source, not the public property: this runs once per element and the property hands
        // out a defensive copy.
        var gradient = Gradient is { Length: > 0 } ? Gradient : DefaultGradientSource;
        // A value without a position (NaN / infinity) has no gradient position either; use the
        // middle of the ramp, the same "no position" convention as the diverging colour scale.
        if (!double.IsFinite(t)) return gradient[gradient.Length / 2];
        if (gradient.Length == 1) return gradient[0];

        float pos = (float)t * (gradient.Length - 1);
        int idx = Math.Min((int)pos, gradient.Length - 2);
        float frac = pos - idx;
        return gradient[idx].Lerp(gradient[idx + 1], frac);
    }

    /// <inheritdoc />
    public string Format(object value) => ScaleConvert.ToDouble(value, nameof(SequentialColorScale)).ToString("G4", CultureInfo.InvariantCulture);
}

/// <summary>
/// Time scale that maps DateTime values to [0, 1].
/// Automatically formats tick labels based on the time range.
/// </summary>
public class TimeScale : IScale
{
    /// <summary>Start of the time range.</summary>
    public DateTime Min { get; private set; }

    /// <summary>End of the time range.</summary>
    public DateTime Max { get; private set; }

    /// <summary>
    /// Whether dates outside the domain are clamped to the axis edges. Default true.
    /// <para>
    /// Only the time, log and colour scales clamp to [0, 1]; the linear scale reports values outside
    /// [0, 1] unchanged. A date outside a time axis would otherwise be placed outside the plot area.
    /// Set this to false when the caller wants to position out-of-range timestamps itself.
    /// </para>
    /// </summary>
    public bool Clamp { get; init; } = true;

    /// <summary>Create a time scale; the domain is fitted from the data.</summary>
    public TimeScale() { }

    /// <summary>Create a time scale with a fixed range.</summary>
    /// <param name="min">Start of the range.</param>
    /// <param name="max">End of the range.</param>
    public TimeScale(DateTime min, DateTime max) { Min = min; Max = max; }

    /// <inheritdoc />
    public void Fit(IEnumerable<object> domain)
    {
        DateTime min = DateTime.MaxValue, max = DateTime.MinValue;
        bool any = false;
        foreach (var item in domain)
        {
            // A null or unparseable value is missing, not the epoch: skipping it keeps one dirty field
            // from throwing here.
            if (!TryToDateTime(item, out var dt)) continue;
            if (dt < min) min = dt;
            if (dt > max) max = dt;
            any = true;
        }
        if (!any) return;
        Min = min;
        Max = max;
    }

    /// <summary>Parse without throwing: <see cref="Fit"/> treats an unparseable value as missing.</summary>
    private static bool TryToDateTime(object? value, out DateTime result)
    {
        if (value is null) { result = default; return false; }
        try { result = ToDateTime(value); return true; }
        catch (FormatException) { result = default; return false; }
        catch (ArgumentOutOfRangeException) { result = default; return false; }
    }

    /// <summary>
    /// Map a date-like value to its normalized [0, 1] position, or <see cref="double.NaN"/> when it is not a
    /// date at all (null, text that does not parse) - the answer the other scales give for a value without a
    /// position, so a caller skips the element instead of losing the frame to an exception.
    /// <para>
    /// When <see cref="Clamp"/> is true (the default) a date <i>outside</i> the domain is clamped to the axis;
    /// when it is false the raw position is returned (unlike <see cref="LinearScale"/>, which always reports
    /// values outside [0, 1]).
    /// </para>
    /// </summary>
    /// <param name="value">Value to map (a <see cref="DateTime"/>, <see cref="DateTimeOffset"/>, an ISO
    /// string, or an integral tick count).</param>
    /// <returns>The position in <c>[0, 1]</c>, or NaN for a value that is not a date.</returns>
    public double Map(object value)
    {
        if (!TryToDateTime(value, out var dt)) return double.NaN;
        double rangeMs = (Max - Min).TotalMilliseconds;
        if (rangeMs <= 0) return 0.5; // degenerate range: centre the single timestamp
        double norm = (dt - Min).TotalMilliseconds / rangeMs;
        return Clamp ? Math.Clamp(norm, 0, 1) : norm;
    }

    /// <summary>
    /// Format a DateTime label based on the current time range.
    /// Uses the invariant culture so labels do not change with the host locale.
    /// </summary>
    public string Format(object value)
    {
        var dt = ToDateTime(value);
        var range = Max - Min;
        if (range.TotalHours < 1)  return dt.ToString("mm:ss", CultureInfo.InvariantCulture);
        if (range.TotalDays < 1)   return dt.ToString("HH:mm", CultureInfo.InvariantCulture);
        if (range.TotalDays < 30)  return dt.ToString("MM-dd", CultureInfo.InvariantCulture);
        return dt.ToString("yy-MM", CultureInfo.InvariantCulture);
    }

    private static DateTime ToDateTime(object value)
    {
        return value switch
        {
            DateTime dt => dt,
            DateTimeOffset dto => dto.DateTime,
            // Convention: any integral type is .NET ticks, a floating point value is a Unix
            // timestamp in milliseconds.
            long ticks => new DateTime(ticks),
            int ticks32 => new DateTime(ticks32),
            short ticks16 => new DateTime(ticks16),
            sbyte ticks8 => new DateTime(ticks8),
            byte ticksU8 => new DateTime(ticksU8),
            ushort ticksU16 => new DateTime(ticksU16),
            uint ticksU32 => new DateTime(ticksU32),
            ulong ticksU64 => new DateTime((long)ticksU64),
            double unixMs => DateTimeOffset.FromUnixTimeMilliseconds((long)unixMs).DateTime,
            float unixMs => DateTimeOffset.FromUnixTimeMilliseconds((long)unixMs).DateTime,
            null => throw new FormatException("Cannot parse a null value as DateTime."),
            _ => DateTime.TryParse(value.ToString(), CultureInfo.InvariantCulture,
                                   DateTimeStyles.None, out var parsed)
                ? parsed
                : throw new FormatException(
                    $"Cannot parse '{value}' ({value.GetType().Name}) as DateTime."),
        };
    }
}

// ── Band scale (grouped bandwidth) ──────────────────────────

/// <summary>
/// Band scale for grouped bar charts.
/// Maps ordinal categories to positions [0, 1] and computes
/// per-band and per-sub-band widths for grouped series layout.
/// </summary>
/// <remarks>
/// Not consumed by the built-in marks: grouped bars are implemented inside <c>IntervalMark</c>,
/// which requires an <see cref="OrdinalScale"/> on its category axis. Setting a BandScale on a
/// chart therefore has no effect yet; it is reserved for custom marks.
/// </remarks>
public class BandScale : OrdinalScaleBase
{
    /// <summary>Number of sub-bands (series) within each band.</summary>
    public int SubBandCount { get; set; } = 1;

    /// <summary>Outer margin: each end of the axis is inset by <c>Padding/2</c> of its width, so the bands themselves sit flush against each other. Default 0.2.</summary>
    public float Padding { get; init; } = 0.2f;

    /// <summary>Inner padding between sub-bands as fraction of band [0, 1).</summary>
    public float InnerPadding { get; init; } = 0.1f;

    /// <summary>Width of one band in normalized [0, 1] space.</summary>
    public double BandWidth => Count == 0 ? 0 : (1.0 - Padding) / Count;

    /// <summary>Width of one sub-band in normalized [0, 1] space.</summary>
    public double SubBandWidth => SubBandCount <= 1
        ? BandWidth * (1.0 - InnerPadding)
        : BandWidth * (1.0 - InnerPadding) / SubBandCount;

    /// <summary>Map a category to the center of its band in [0, 1].</summary>
    public override double Map(object value)
    {
        if (!TryGetIndex(value, out int idx)) return 0;
        // Step by the band width (not by 1/Count): with padding the two differ and the bands would
        // drift past the right edge of the axis.
        double bandStart = Padding / 2.0 + idx * BandWidth;
        return bandStart + BandWidth / 2.0;
    }

    /// <summary>Map a category + sub-band index to the center of its sub-band.</summary>
    public double MapSubBand(object value, int subIndex)
    {
        if (!TryGetIndex(value, out int idx)) return 0;
        double bandStart = Padding / 2.0 + idx * BandWidth;
        double innerStart = bandStart + InnerPadding * BandWidth / 2.0;
        return innerStart + SubBandWidth * (subIndex + 0.5);
    }
}

// ── Log scale (logarithmic coordinates) ─────────────────────
/// <summary>
/// Logarithmic scale that maps positive values to [0, 1] using log10.
/// Useful for data spanning multiple orders of magnitude (e.g., damage values 1–999999).
/// Non-positive values have no logarithm: <see cref="Map"/> substitutes <see cref="Min"/> for them,
/// while <see cref="Fit"/> ignores them.
/// </summary>
public class LogScale : IScale
{
    /// <summary>
    /// Minimum value of the domain. The two-argument constructor clamps a non-positive bound to 1;
    /// <see cref="Fit"/> instead skips non-positive values and derives the bound from the remaining
    /// data (the power of ten below the smallest value, or a tenth of it when there is a single
    /// distinct value), so it is always &gt; 0.
    /// </summary>
    public double Min { get; private set; } = 1;

    /// <summary>Maximum value of the domain.</summary>
    public double Max { get; private set; } = 100;

    // Cached log values for performance
    private double _logMin;
    private double _logMax;
    private double _logRange;

    /// <summary>Create a logarithmic scale spanning the default domain [1, 100].</summary>
    public LogScale() { UpdateCache(); }

    /// <summary>Create a logarithmic scale with a fixed domain (non-positive bounds are clamped).</summary>
    /// <param name="min">Domain minimum; values &lt;= 0 become 1.</param>
    /// <param name="max">Domain maximum; must be greater than <paramref name="min"/>.</param>
    public LogScale(double min, double max)
    {
        Min = min > 0 ? min : 1;
        Max = max > Min ? max : Min * 10;
        UpdateCache();
    }

    private void UpdateCache()
    {
        _logMin = Math.Log10(Min);
        _logMax = Math.Log10(Max);
        _logRange = _logMax - _logMin;
    }

    /// <inheritdoc />
    public void Fit(IEnumerable<object> domain)
    {
        if (!ScaleMath.TryMinMax(domain, out double min, out double max, positiveOnly: true))
        {
            GD.PushWarning($"LogScale: no positive finite values, keeping current range [{Min}, {Max}]");
            return;
        }
        // Expand to the surrounding decades so the axis covers round powers of ten, and keep a
        // single distinct value in the middle of the axis instead of pinning it to the bottom.
        if (max <= min)
        {
            Min = min / 10.0;
            Max = min * 10.0;
        }
        else
        {
            Min = Math.Pow(10, Math.Floor(Math.Log10(min)));
            Max = Math.Pow(10, Math.Ceiling(Math.Log10(max)));
            if (Max <= Min) Max = Min * 10.0;
        }
        UpdateCache();
    }

    /// <inheritdoc />
    public double Map(object value)
    {
        // Zero and negative values have no position on a logarithmic axis. They used to be replaced by
        // `Min`, which drew them at the bottom of the axis as if they were real data points; NaN is the
        // honest answer (and the one the other scales give).
        if (!ScaleConvert.TryToDouble(value, out double v) || v <= 0) return double.NaN;
        // A degenerate range (max == min) collapses to the middle, like every other scale (see LinearScale.Map).
        if (_logRange < 1e-10) return 0.5;
        return Math.Clamp((Math.Log10(v) - _logMin) / _logRange, 0, 1);
    }

    /// <inheritdoc />
    public string Format(object value)
    {
        if (!ScaleConvert.TryToDouble(value, out double v)) return "";
        // Fixed-point mantissa: "G3" would render 999_999 as "1E+03K", which reads as scientific
        // notation in an axis label.
        // The thresholds are chosen one rounding step below the next unit (999_999 would otherwise print
        // its mantissa as "1000" while keeping the "K" suffix, i.e. "1000K" for what is a mega value).
        if (v >= 999_950) return (v / 1_000_000).ToString("0.##", CultureInfo.InvariantCulture) + "M";
        if (v >= 999.5) return (v / 1_000).ToString("0.##", CultureInfo.InvariantCulture) + "K";
        return v.ToString("G4", CultureInfo.InvariantCulture);
    }
}

// ── Diverging color scale ─────────────────────────────────────────────

/// <summary>
/// Diverging color scale for values with a meaningful midpoint.
/// Maps values below the midpoint to the negative gradient,
/// values above to the positive gradient, with the midpoint as a neutral color.
/// </summary>
public class DivergingColorScale : IColorScale
{
    /// <summary>Minimum value of the domain.</summary>
    public double Min { get; private set; }

    /// <summary>Maximum value of the domain.</summary>
    public double Max { get; private set; }

    /// <summary>
    /// Midpoint value. Default 0. Values below map toward <see cref="NegativeColor"/>,
    /// values above map toward <see cref="PositiveColor"/>.
    /// </summary>
    public double MidPoint { get; init; }

    /// <summary>
    /// If true, the domain is made symmetric around the midpoint after Fit.
    /// Default true.
    /// </summary>
    public bool Symmetric { get; init; } = true;

    /// <summary>Color for the most negative (minimum) values.</summary>
    public Color NegativeColor { get; } = new(0.12f, 0.47f, 0.71f); // blue

    /// <summary>Color at the midpoint (neutral).</summary>
    public Color MidColor { get; } = new(0.97f, 0.97f, 0.97f); // near-white

    /// <summary>Color for the most positive (maximum) values.</summary>
    public Color PositiveColor { get; } = new(0.84f, 0.19f, 0.15f); // red

    /// <summary>Create a diverging colour scale spanning [-1, 1] with the midpoint at 0.</summary>
    public DivergingColorScale() { Min = -1; Max = 1; }

    /// <summary>Create a diverging colour scale with a fixed domain.</summary>
    /// <param name="min">Most negative value (mapped to <see cref="NegativeColor"/>).</param>
    /// <param name="max">Most positive value (mapped to <see cref="PositiveColor"/>).</param>
    public DivergingColorScale(double min, double max)
    {
        Min = min;
        Max = max;
    }

    /// <inheritdoc />
    public void Fit(IEnumerable<object> domain)
    {
        // Same rule as the sequential ramp: a value that is not a number is missing, not zero.
        if (!ScaleMath.TryMinMax(domain, out double min, out double max)) return;
        Min = min;
        Max = max;
        if (Symmetric)
        {
            double extent = Math.Max(Math.Abs(Min - MidPoint), Math.Abs(Max - MidPoint));
            Min = MidPoint - extent;
            Max = MidPoint + extent;
        }
    }

    /// <summary>Map a value to [0, 1] within the domain range.</summary>
    public double Map(object value)
    {
        double v = ScaleConvert.ToDouble(value, nameof(DivergingColorScale));
        if (!double.IsFinite(v)) return double.NaN;
        double range = Max - Min;
        if (ScaleMath.IsDegenerate(Min, Max)) return 0.5;
        return Math.Clamp((v - Min) / range, 0, 1);
    }

    /// <summary>
    /// Map a value to an interpolated diverging color.
    /// The neutral color is placed at <see cref="MidPoint"/>; when the midpoint lies outside
    /// [Min, Max] it is clamped, so a one-sided domain still fades towards the neutral color instead
    /// of reporting every value as fully negative/positive.
    /// </summary>
    public Color MapColor(object value)
    {
        double v = ScaleConvert.ToDouble(value, nameof(DivergingColorScale));
        if (!double.IsFinite(v)) return MidColor;

        // A collapsed domain (every value equal to the midpoint, e.g. all-zero data) has no side to
        // pick: the neutral colour is the only honest answer. Without this the "above the midpoint"
        // branch reports full positive and paints the whole chart red.
        if (ScaleMath.IsDegenerate(Min, Max)) return MidColor;

        double mid = Math.Clamp(MidPoint, Math.Min(Min, Max), Math.Max(Min, Max));

        if (v < mid)
        {
            double span = mid - Min;
            float t = ScaleMath.IsDegenerate(Min, mid) ? 1f : (float)Math.Clamp((v - Min) / span, 0, 1);
            return NegativeColor.Lerp(MidColor, t);
        }

        double spanUp = Max - mid;
        float tUp = ScaleMath.IsDegenerate(mid, Max) ? 1f : (float)Math.Clamp((v - mid) / spanUp, 0, 1);
        return MidColor.Lerp(PositiveColor, tUp);
    }

    /// <inheritdoc />
    public string Format(object value) => ScaleConvert.ToDouble(value, nameof(DivergingColorScale)).ToString("G4", CultureInfo.InvariantCulture);
}

// ── NiceTick generator ────────────────────────────────────────────────────

/// <summary>
/// Result of a nice tick computation: the axis range and tick step.
/// </summary>
public readonly record struct NiceTickResult(
    double Min, double Max, double Step, int Count);

/// <summary>
/// Utility for computing human-friendly axis tick marks.
/// Uses the classic "nice number" algorithm (Wilkinson extended).
/// </summary>
public static class NiceTickGenerator
{
    /// <summary>
    /// Compute nice tick marks for the given data range.
    /// </summary>
    /// <param name="dataMin">Minimum data value.</param>
    /// <param name="dataMax">Maximum data value.</param>
    /// <param name="maxTicks">Maximum desired number of tick intervals (default 8).</param>
    public static NiceTickResult Compute(double dataMin, double dataMax, int maxTicks = 8)
    {
        if (maxTicks < 2) maxTicks = 2;
        // Ticks are produced in ascending order; a reversed caller domain must not produce
        // Min > Max with a negative step.
        if (dataMax < dataMin) (dataMin, dataMax) = (dataMax, dataMin);
        double range = dataMax - dataMin;
        if (ScaleMath.IsDegenerate(dataMin, dataMax))
        {
            // Degenerate: single value
            double v = dataMin;
            return new NiceTickResult(v - 1, v + 1, 1, 2);
        }

        double roughStep = range / maxTicks;
        double step = NiceNumber(roughStep, ceil: true);

        // floor/ceil keep the range aligned to the step; for non-negative data the floor is
        // non-negative as well, so no extra clamping is needed.
        double niceMin = Math.Floor(dataMin / step) * step;
        double niceMax = Math.Ceiling(dataMax / step) * step;

        int count = Math.Max(1, (int)Math.Round((niceMax - niceMin) / step));
        return new NiceTickResult(niceMin, niceMax, step, count);
    }

    private static double NiceNumber(double v, bool ceil)
    {
        if (v <= 0) return v == 0 ? 1 : -NiceNumber(-v, !ceil);
        double exp = Math.Floor(Math.Log10(v));
        double f   = v / Math.Pow(10, exp);
        double nf  = ceil
            ? (f <= 1 ? 1 : f <= 2 ? 2 : f <= 5 ? 5 : 10)
            : (f < 1.5 ? 1 : f < 3 ? 2 : f < 7 ? 5 : 10);
        return nf * Math.Pow(10, exp);
    }
}

/// <summary>
/// Wraps another scale and remaps its output: the inner scale's <c>[0, 1]</c> becomes <c>[OutputMin,
/// OutputMax]</c>. This is the layer a host needs when a channel's usable output range is not the whole
/// range - an opacity channel that must stay visible (<c>ChartView.OpacityRange</c>), a bar width that should
/// never collapse to zero.
/// <para>
/// The difference to <see cref="LinearScale.SetDomain"/> matters: pinning a domain changes which <i>values</i> the
/// scale covers, while this changes what its normalized output <i>means</i>. Fitting and formatting are the
/// inner scale's, so the same domain keeps producing the same labels.
/// </para>
/// </summary>
public class OutputRangeScale : IScale
{
    private readonly IScale _inner;

    /// <summary>Create the decorator around <paramref name="inner"/>.</summary>
    /// <param name="inner">Scale whose output is remapped.</param>
    /// <param name="min">Value the bottom of the domain maps onto.</param>
    /// <param name="max">Value the top of the domain maps onto (the two are swapped when reversed).</param>
    /// <exception cref="ArgumentNullException"><paramref name="inner"/> is null.</exception>
    public OutputRangeScale(IScale inner, double min, double max)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
        OutputMin = Math.Min(min, max);
        OutputMax = Math.Max(min, max);
    }

    /// <summary>Scale this one remaps.</summary>
    public IScale Inner => _inner;

    /// <summary>Output value the bottom of the domain maps onto.</summary>
    public double OutputMin { get; }

    /// <summary>Output value the top of the domain maps onto.</summary>
    public double OutputMax { get; }

    /// <inheritdoc />
    public void Fit(IEnumerable<object> domain) => _inner.Fit(domain);

    /// <summary>
    /// The inner scale's normalized value, remapped onto the output range. A non-finite value - the inner
    /// scale's answer for "no position" - stays <see cref="double.NaN"/>: pretending it had a position would
    /// put it at the bottom of the range and make it look like real data.
    /// </summary>
    /// <param name="value">Value to map.</param>
    /// <returns>The remapped output value, or NaN for a value with no position.</returns>
    public double Map(object value)
    {
        double normalized = _inner.Map(value);
        return double.IsFinite(normalized)
            ? OutputMin + Math.Clamp(normalized, 0.0, 1.0) * (OutputMax - OutputMin)
            : double.NaN;
    }

    /// <inheritdoc />
    public string Format(object value) => _inner.Format(value);
}
