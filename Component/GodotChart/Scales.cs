using System;
using System.Collections.Generic;
using Godot;

namespace GodotNodeExtension.Component.GodotChart;
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
            var s = v.ToString() ?? string.Empty;
            if (domainSet.Add(s))
            {
                indexMap[s] = domain.Count;
                domain.Add(s);
            }
        }
    }
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
        try { return Convert.ToDouble(value); }
        catch (Exception ex)
        {
            throw new InvalidCastException(
                $"[{scaleName}] Cannot convert value '{value}' ({value.GetType().Name}) to double.", ex);
        }
    }
}

/// <summary>
/// Continuous numeric scale that maps a [Min, Max] domain linearly to [0, 1].
/// </summary>
public class LinearScale : IScale
{
    public double Min { get; private set; }
    public double Max { get; private set; }
    public bool   IncludeZero { get; set; } = true;

    /// <summary>
    /// Cached nice tick result computed during <see cref="Fit"/> or on first access.
    /// </summary>
    public NiceTickResult NiceTicks { get; private set; }

    public LinearScale() { }
    public LinearScale(double min, double max)
    {
        Min = min; Max = max;
        NiceTicks = NiceTickGenerator.Compute(min, max);
    }

    public void Fit(IEnumerable<object> domain)
    {
        double dataMin = double.MaxValue, dataMax = double.MinValue;
        foreach (var v in domain)
        {
            double d = ScaleConvert.ToDouble(v, nameof(LinearScale));
            if (d < dataMin) dataMin = d;
            if (d > dataMax) dataMax = d;
        }
        if (dataMin > dataMax) return; // empty

        if (IncludeZero) dataMin = Math.Min(0, dataMin);

        var nice = NiceTickGenerator.Compute(dataMin, dataMax);
        Min = nice.Min;
        Max = nice.Max;
        NiceTicks = nice;
    }

    public double Map(object value)
    {
        double v = ScaleConvert.ToDouble(value, nameof(LinearScale));
        if (Math.Abs(Max - Min) < 1e-10) return 0;
        return (v - Min) / (Max - Min);
    }

    public string Format(object value) => ScaleConvert.ToDouble(value, nameof(LinearScale)).ToString("G4");
}

/// <summary>
/// Ordinal scale that maps discrete categories to evenly spaced positions [0, 1].
/// </summary>
public class OrdinalScale : IScale
{
    private readonly List<string> _domain = new();
    private readonly HashSet<string> _domainSet = new();
    private readonly Dictionary<string, int> _indexMap = new();

    public IReadOnlyList<string> Domain => _domain;

    /// <summary>Returns the zero-based index of <paramref name="key"/> in the domain, or -1 if not found.</summary>
    public int IndexOf(string key) => _indexMap.GetValueOrDefault(key, -1);

    public void Fit(IEnumerable<object> domain)
        => OrdinalDomainHelper.Fit(_domain, _domainSet, _indexMap, domain);

    public double Map(object value)
    {
        var key = value.ToString() ?? string.Empty;
        if (!_indexMap.TryGetValue(key, out int idx)) return 0;
        // Center-aligned: return the center of each cell
        return _domain.Count == 1 ? 0.5 : (idx + 0.5) / _domain.Count;
    }

    public string Format(object value) => value.ToString() ?? string.Empty;
}

// ── Color scale (category → color) ───────────────────────────
/// <summary>
/// Color scale that maps discrete categories to colors.
/// </summary>
public class ColorScale : IScale
{
    private readonly List<string> _domain = new();
    private readonly HashSet<string> _domainSet = new();
    private readonly Dictionary<string, int> _indexMap = new();

    /// <summary>The ordered list of category names in this color scale.</summary>
    public IReadOnlyList<string> Domain => _domain;

    public Color[] Palette { get; set; } = ChartTheme.DefaultPalette;

    public void Fit(IEnumerable<object> domain)
        => OrdinalDomainHelper.Fit(_domain, _domainSet, _indexMap, domain);

    // Returns [0,1] index (actual color obtained via MapColor)
    public double Map(object value)
    {
        var key = value.ToString() ?? string.Empty;
        if (!_indexMap.TryGetValue(key, out int idx)) return 0;
        return (double)idx / Math.Max(1, _domain.Count - 1);
    }

    public Color MapColor(object value)
    {
        var key = value.ToString() ?? string.Empty;
        if (!_indexMap.TryGetValue(key, out int idx)) return Palette[0];
        return Palette[idx % Palette.Length];
    }

    public string Format(object value) => value.ToString() ?? string.Empty;
}

// ── Radial angle scale (category → evenly spaced angle [0,1], multiply by 2π for radians) ──
/// <summary>
/// Radial scale that maps ordinal categories to evenly spaced positions [0, 1].
/// Multiply by 2π to convert to radians for polar coordinate charts.
/// </summary>
public class RadialScale : IScale
{
    private readonly List<string> _domain = new();
    private readonly HashSet<string> _domainSet = new();
    private readonly Dictionary<string, int> _indexMap = new();

    /// <summary>The ordered list of category names.</summary>
    public IReadOnlyList<string> Domain => _domain;

    public void Fit(IEnumerable<object> domain)
        => OrdinalDomainHelper.Fit(_domain, _domainSet, _indexMap, domain);

    /// <summary>
    /// Map a category to its normalized position [0, 1).
    /// Each category occupies an equal arc segment.
    /// </summary>
    public double Map(object value)
    {
        var key = value.ToString() ?? string.Empty;
        if (!_indexMap.TryGetValue(key, out int idx)) return 0;
        return (double)idx / _domain.Count;
    }

    public string Format(object value) => value.ToString() ?? string.Empty;
}

/// <summary>
/// Sequential color scale that maps continuous numeric values to a color gradient.
/// Useful for heatmaps and other color-intensity visualizations.
/// </summary>
public class SequentialColorScale : IScale
{
    /// <summary>Minimum value of the domain.</summary>
    public double Min { get; private set; }

    /// <summary>Maximum value of the domain.</summary>
    public double Max { get; private set; }

    /// <summary>Color stops defining the gradient from low to high values.</summary>
    public Color[] Gradient { get; set; } =
    [
        new(0.12f, 0.07f, 0.53f), // dark purple
        new(0.29f, 0.00f, 0.73f), // purple
        new(0.85f, 0.24f, 0.31f), // red
        new(0.99f, 0.68f, 0.38f), // orange
        new(0.99f, 0.95f, 0.70f), // light yellow
    ];

    public SequentialColorScale() { }

    public SequentialColorScale(double min, double max) { Min = min; Max = max; }

    public void Fit(IEnumerable<object> domain)
    {
        double min = double.MaxValue, max = double.MinValue;
        bool any = false;
        foreach (var item in domain)
        {
            double v = ScaleConvert.ToDouble(item, nameof(SequentialColorScale));
            if (v < min) min = v;
            if (v > max) max = v;
            any = true;
        }
        if (!any) return;
        Min = min;
        Max = max;
    }

    /// <summary>Map a value to [0, 1] within the domain range.</summary>
    public double Map(object value)
    {
        double v = ScaleConvert.ToDouble(value, nameof(SequentialColorScale));
        if (Math.Abs(Max - Min) < 1e-10) return 0;
        return Math.Clamp((v - Min) / (Max - Min), 0, 1);
    }

    /// <summary>Map a value directly to an interpolated gradient color.</summary>
    public Color MapColor(object value)
    {
        double t = Map(value);
        if (Gradient.Length == 0) return new Color(1f, 1f, 1f);
        if (Gradient.Length == 1) return Gradient[0];

        float pos = (float)t * (Gradient.Length - 1);
        int idx = Math.Min((int)pos, Gradient.Length - 2);
        float frac = pos - idx;
        return Gradient[idx].Lerp(Gradient[idx + 1], frac);
    }

    public string Format(object value) => ScaleConvert.ToDouble(value, nameof(SequentialColorScale)).ToString("G4");
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

    public TimeScale() { }

    public TimeScale(DateTime min, DateTime max) { Min = min; Max = max; }

    public void Fit(IEnumerable<object> domain)
    {
        DateTime min = DateTime.MaxValue, max = DateTime.MinValue;
        bool any = false;
        foreach (var item in domain)
        {
            var dt = ToDateTime(item);
            if (dt < min) min = dt;
            if (dt > max) max = dt;
            any = true;
        }
        if (!any) return;
        Min = min;
        Max = max;
    }

    /// <summary>Map a DateTime to normalized [0, 1] position.</summary>
    public double Map(object value)
    {
        var dt = ToDateTime(value);
        double rangeMs = (Max - Min).TotalMilliseconds;
        if (rangeMs < 1e-10) return 0;
        return Math.Clamp((dt - Min).TotalMilliseconds / rangeMs, 0, 1);
    }

    /// <summary>Format a DateTime label based on the current time range.</summary>
    public string Format(object value)
    {
        var dt = ToDateTime(value);
        var range = Max - Min;
        if (range.TotalHours < 1) return dt.ToString("mm:ss");
        if (range.TotalDays < 1) return dt.ToString("HH:mm");
        if (range.TotalDays < 30) return dt.ToString("MM-dd");
        return dt.ToString("yy-MM");
    }

    private static DateTime ToDateTime(object value)
    {
        return value switch
        {
            DateTime dt => dt,
            DateTimeOffset dto => dto.DateTime,
            long ticks => new DateTime(ticks),
            double unixMs => DateTimeOffset.FromUnixTimeMilliseconds((long)unixMs).DateTime,
            _ => DateTime.TryParse(value.ToString(), out var parsed)
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
public class BandScale : IScale
{
    private readonly List<string> _domain = new();
    private readonly HashSet<string> _domainSet = new();
    private readonly Dictionary<string, int> _indexMap = new();

    /// <summary>The ordered list of category names.</summary>
    public IReadOnlyList<string> Domain => _domain;

    /// <summary>Number of sub-bands (series) within each band.</summary>
    public int SubBandCount { get; set; } = 1;

    /// <summary>Padding between bands as a fraction of bandwidth [0, 1).</summary>
    public float Padding { get; set; } = 0.2f;

    /// <summary>Inner padding between sub-bands as fraction of band [0, 1).</summary>
    public float InnerPadding { get; set; } = 0.1f;

    /// <summary>Width of one band in normalized [0, 1] space.</summary>
    public double BandWidth => _domain.Count == 0 ? 0 : (1.0 - Padding) / _domain.Count;

    /// <summary>Width of one sub-band in normalized [0, 1] space.</summary>
    public double SubBandWidth => SubBandCount <= 1
        ? BandWidth * (1.0 - InnerPadding)
        : BandWidth * (1.0 - InnerPadding) / SubBandCount;

    public void Fit(IEnumerable<object> domain)
        => OrdinalDomainHelper.Fit(_domain, _domainSet, _indexMap, domain);

    /// <summary>Map a category to the center of its band in [0, 1].</summary>
    public double Map(object value)
    {
        var key = value.ToString() ?? string.Empty;
        if (!_indexMap.TryGetValue(key, out int idx)) return 0;
        double bandStart = Padding / 2.0 + (double)idx / _domain.Count;
        return bandStart + BandWidth / 2.0;
    }

    /// <summary>Map a category + sub-band index to the center of its sub-band.</summary>
    public double MapSubBand(object value, int subIndex)
    {
        var key = value.ToString() ?? string.Empty;
        if (!_indexMap.TryGetValue(key, out int idx)) return 0;
        double bandStart = Padding / 2.0 + (double)idx / _domain.Count;
        double innerStart = bandStart + InnerPadding * BandWidth / 2.0;
        return innerStart + SubBandWidth * (subIndex + 0.5);
    }

    public string Format(object value) => value.ToString() ?? string.Empty;
}

// ── Log scale (logarithmic coordinates) ─────────────────────
/// <summary>
/// Logarithmic scale that maps positive values to [0, 1] using log10.
/// Useful for data spanning multiple orders of magnitude (e.g., damage values 1–999999).
/// Values &lt;= 0 are clamped to a small positive epsilon.
/// </summary>
public class LogScale : IScale
{
    /// <summary>Minimum value of the domain (must be &gt; 0).</summary>
    public double Min { get; private set; } = 1;

    /// <summary>Maximum value of the domain.</summary>
    public double Max { get; private set; } = 100;

    // Cached log values for performance
    private double _logMin;
    private double _logMax;
    private double _logRange;

    public LogScale() { UpdateCache(); }

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

    public void Fit(IEnumerable<object> domain)
    {
        double min = double.MaxValue, max = double.MinValue;
        bool any = false;
        foreach (var item in domain)
        {
            double v = ScaleConvert.ToDouble(item, nameof(LogScale));
            if (v <= 0) continue;
            if (v < min) min = v;
            if (v > max) max = v;
            any = true;
        }
        if (!any)
        {
            GD.PushWarning($"LogScale: all values <= 0, keeping current range [{Min}, {Max}]");
            return;
        }
        Min = min;
        Max = max;
        if (Max <= Min) Max = Min * 10;
        UpdateCache();
    }

    public double Map(object value)
    {
        double v = ScaleConvert.ToDouble(value, nameof(LogScale));
        if (v <= 0) v = Min;
        if (_logRange < 1e-10) return 0;
        return Math.Clamp((Math.Log10(v) - _logMin) / _logRange, 0, 1);
    }

    public string Format(object value)
    {
        double v = ScaleConvert.ToDouble(value, nameof(LogScale));
        if (v >= 1_000_000) return $"{v / 1_000_000:G3}M";
        if (v >= 1_000) return $"{v / 1_000:G3}K";
        return v.ToString("G4");
    }
}

// ── Diverging color scale ─────────────────────────────────────────────

/// <summary>
/// Diverging color scale for values with a meaningful midpoint.
/// Maps values below the midpoint to the negative gradient,
/// values above to the positive gradient, with the midpoint as a neutral color.
/// </summary>
public class DivergingColorScale : IScale
{
    /// <summary>Minimum value of the domain.</summary>
    public double Min { get; private set; }

    /// <summary>Maximum value of the domain.</summary>
    public double Max { get; private set; }

    /// <summary>
    /// Midpoint value. Default 0. Values below map toward <see cref="NegativeColor"/>,
    /// values above map toward <see cref="PositiveColor"/>.
    /// </summary>
    public double MidPoint { get; set; } = 0;

    /// <summary>
    /// If true, the domain is made symmetric around the midpoint after Fit.
    /// Default true.
    /// </summary>
    public bool Symmetric { get; set; } = true;

    /// <summary>Color for the most negative (minimum) values.</summary>
    public Color NegativeColor { get; set; } = new(0.12f, 0.47f, 0.71f); // blue

    /// <summary>Color at the midpoint (neutral).</summary>
    public Color MidColor { get; set; } = new(0.97f, 0.97f, 0.97f); // near-white

    /// <summary>Color for the most positive (maximum) values.</summary>
    public Color PositiveColor { get; set; } = new(0.84f, 0.19f, 0.15f); // red

    public DivergingColorScale() { Min = -1; Max = 1; }

    public DivergingColorScale(double min, double max)
    {
        Min = min;
        Max = max;
    }

    public void Fit(IEnumerable<object> domain)
    {
        double min = double.MaxValue, max = double.MinValue;
        bool any = false;
        foreach (var item in domain)
        {
            double v = ScaleConvert.ToDouble(item, nameof(DivergingColorScale));
            if (v < min) min = v;
            if (v > max) max = v;
            any = true;
        }
        if (!any) return;
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
        double range = Max - Min;
        if (Math.Abs(range) < 1e-10) return 0.5;
        return Math.Clamp((v - Min) / range, 0, 1);
    }

    /// <summary>Map a value to an interpolated diverging color.</summary>
    public Color MapColor(object value)
    {
        double v = ScaleConvert.ToDouble(value, nameof(DivergingColorScale));
        if (v <= Min) return NegativeColor;
        if (v >= Max) return PositiveColor;
        if (Math.Abs(v - MidPoint) < 1e-10) return MidColor;

        if (v < MidPoint)
        {
            double range = MidPoint - Min;
            if (Math.Abs(range) < 1e-10) return MidColor;
            float t = (float)((v - Min) / range);
            return NegativeColor.Lerp(MidColor, t);
        }
        else
        {
            double range = Max - MidPoint;
            if (Math.Abs(range) < 1e-10) return MidColor;
            float t = (float)((v - MidPoint) / range);
            return MidColor.Lerp(PositiveColor, t);
        }
    }

    public string Format(object value) => ScaleConvert.ToDouble(value, nameof(DivergingColorScale)).ToString("G4");
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
        double range = dataMax - dataMin;
        if (range < 1e-12)
        {
            // Degenerate: single value
            double v = dataMin;
            return new NiceTickResult(v - 1, v + 1, 1, 2);
        }

        double roughStep = range / maxTicks;
        double step = NiceNumber(roughStep, ceil: true);

        double niceMin = Math.Floor(dataMin / step) * step;
        double niceMax = Math.Ceiling(dataMax / step) * step;

        // Clamp min to 0 when data is non-negative and close to 0
        if (dataMin >= 0 && niceMin < 0) niceMin = 0;

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