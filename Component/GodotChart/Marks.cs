using System;
using System.Collections.Generic;
using Godot;
using GodotNodeExtension.Component.GodotChart.Canvas;

namespace GodotNodeExtension.Component.GodotChart;

/// <summary>
/// Rendering context passed to each Mark during chart drawing.
/// </summary>
public class MarkContext
{
    /// <summary>The canvas to draw on.</summary>
    public required ICanvas2D     Canvas            { get; init; }

    /// <summary>The computed plot area rectangle with coordinate mapping.</summary>
    public required PlotArea      Plot              { get; init; }

    /// <summary>All resolved scales (X, Y, Color, etc.).</summary>
    public required ScaleSet      Scales            { get; init; }

    /// <summary>All resolved encode mappings.</summary>
    public required EncodeSet     Encodes           { get; init; }

    /// <summary>The data rows to render.</summary>
    public required List<DataRow> Data              { get; init; }

    /// <summary>
    /// Monotonically increasing version number that changes whenever data is updated.
    /// Marks can use this to invalidate layout caches accurately.
    /// </summary>
    public int DataVersion { get; init; }

    /// <summary>
    /// Combined version that changes when data, encodes, scales, or plot area change.
    /// Used by marks with expensive cached layouts (e.g. Chord, Sankey, Treemap).
    /// </summary>
    public int LayoutVersion { get; init; }

    /// <summary>
    /// Animation progress [0,1]. 1.0 = fully visible (no animation).
    /// Marks should use this to scale their rendering.
    /// </summary>
    public float AnimationProgress { get; init; } = 1f;

    /// <summary>
    /// Animation context containing all animation parameters (opacity, exit, hover, etc.).
    /// </summary>
    public AnimationContext Animation { get; init; } = AnimationContext.Default;

    /// <summary>Index of the data row currently hovered. -1 if none.</summary>
    public int HoveredRowIndex { get; init; } = -1;

    /// <summary>Index of the data row currently selected. -1 if none.</summary>
    public int SelectedRowIndex { get; init; } = -1;

    /// <summary>The chart theme providing default visual styles.</summary>
    public ChartTheme? Theme { get; init; }

    /// <summary>
    /// The series key currently focused (e.g. via legend click).
    /// Null means no series is focused and all render at full opacity.
    /// </summary>
    public string? FocusedSeries { get; init; }

    /// <summary>
    /// Set of hidden series keys. Hidden series are excluded from rendering and hit testing.
    /// Null means all series are visible.
    /// </summary>
    public IReadOnlySet<string>? HiddenSeries { get; init; }

    /// <summary>
    /// Create a shallow copy with a different <see cref="Data"/> list.
    /// All other reference-type properties share the same underlying objects.
    /// This avoids fragile manual field-by-field copies when only Data differs.
    /// </summary>
    public MarkContext WithData(List<DataRow> data)
    {
        return new MarkContext
        {
            Canvas            = Canvas,
            Plot              = Plot,
            Scales            = Scales,
            Encodes           = Encodes,
            Data              = data,
            DataVersion       = DataVersion,
            LayoutVersion     = LayoutVersion,
            AnimationProgress = AnimationProgress,
            Animation         = Animation,
            HoveredRowIndex   = HoveredRowIndex,
            SelectedRowIndex  = SelectedRowIndex,
            Theme             = Theme,
            FocusedSeries     = FocusedSeries,
            HiddenSeries      = HiddenSeries,
        };
    }
}

/// <summary>
/// Describes the drawable plot area with coordinate mapping helpers.
/// </summary>
public readonly record struct PlotArea(float X, float Y, float Width, float Height)
{
    /// <summary>Map normalized [0,1] value to screen X coordinate.</summary>
    public float MapX(double t) => X + (float)t * Width;

    /// <summary>Map normalized [0,1] value to screen Y coordinate (Y-axis flipped).</summary>
    public float MapY(double t) => Y + Height - (float)t * Height;

    /// <summary>Check whether a point lies within this area.</summary>
    public bool Contains(float px, float py)
        => px >= X && px <= X + Width && py >= Y && py <= Y + Height;
}

/// <summary>
/// Container that holds the resolved scale for each <see cref="Channel"/>.
/// </summary>
public class ScaleSet
{
    private readonly Dictionary<Channel, IScale> _scales = new();

    public void   Set(Channel ch, IScale scale) => _scales[ch] = scale;

    /// <summary>Get the scale for a channel. Throws if not set — call Has() first for optional channels.</summary>
    public IScale Get(Channel ch) =>
        _scales.TryGetValue(ch, out var s) ? s
        : throw new InvalidOperationException($"No scale set for channel {ch}. Did you forget Encode({ch}, ...)?");

    /// <summary>Try to get the scale for a channel. Returns null if not set.</summary>
    public IScale? TryGet(Channel ch) => _scales.GetValueOrDefault(ch);

    public bool   Has(Channel ch) => _scales.ContainsKey(ch);

    /// <summary>Remove the scale for a channel. Returns true if it was present.</summary>
    public bool   Remove(Channel ch) => _scales.Remove(ch);
}

/// <summary>
/// Container that holds the resolved encode mapping for each <see cref="Channel"/>.
/// </summary>
public class EncodeSet
{
    private readonly Dictionary<Channel, IEncodeValue> _encodes = new();

    public void        Set(Channel ch, IEncodeValue enc) => _encodes[ch] = enc;

    /// <summary>Get the encode for a channel. Throws if not set.</summary>
    public IEncodeValue Get(Channel ch) =>
        _encodes.TryGetValue(ch, out var e) ? e
        : throw new InvalidOperationException($"No encode set for channel {ch}.");

    /// <summary>Try to get the encode for a channel. Returns null if not set.</summary>
    public IEncodeValue? TryGet(Channel ch) => _encodes.GetValueOrDefault(ch);

    public bool        Has(Channel ch) => _encodes.ContainsKey(ch);

    /// <summary>Enumerate all channels that have encodes set.</summary>
    public IEnumerable<Channel> Channels => _encodes.Keys;

    /// <summary>Resolve the raw value for a channel from a data row.</summary>
    public object? Resolve(Channel ch, DataRow row)
    {
        if (!_encodes.TryGetValue(ch, out var enc)) return null;
        return enc switch
        {
            FieldEncode f    => row.Get(f.FieldName),
            ConstantEncode c => c.Value,
            _                => null,
        };
    }
}

// ── Data label position ───────────────────────────────────────

/// <summary>
/// Controls where the data label is placed relative to the mark element.
/// </summary>
public enum LabelPosition { Top, Inside, Bottom, Left, Right }

// ── Mark base class ───────────────────────────────────────────

/// <summary>
/// Abstract base for all chart mark types (bar, line, point, etc.).
/// </summary>
public abstract class Mark
{
    // ── Mark-level encode overrides ────────────────────────────
    private readonly Dictionary<Channel, IEncodeValue> _localEncodes = new();

    /// <summary>
    /// The coordinate system this mark requires.
    /// Used for composite chart compatibility validation.
    /// </summary>
    public abstract MarkCoordinate Coordinate { get; }

    /// <summary>
    /// Which Y channel this mark uses for vertical positioning.
    /// Defaults to <see cref="Channel.Y"/> (left axis).
    /// Set to <see cref="Channel.Y2"/> for the right axis.
    /// </summary>
    public Channel YChannel { get; set; } = Channel.Y;

    /// <summary>
    /// Optional mark-level data. When set, this mark uses its own data
    /// instead of the chart's shared data.
    /// </summary>
    public List<DataRow>? Data { get; set; }

    /// <summary>Enable data labels on mark elements.</summary>
    public virtual bool ShowLabel { get; set; } = false;

    /// <summary>Data label format string. {0} = Y value, {1} = X value.</summary>
    public string LabelFormat { get; set; } = "{0}";

    /// <summary>Label position relative to the mark element.</summary>
    public LabelPosition LabelPosition { get; set; } = LabelPosition.Top;

    /// <summary>
    /// Per-element color override. Receives (row, index, defaultColor).
    /// Return null to use the default color.
    /// </summary>
    public Func<DataRow, int, Color, Color?>? ColorOverride { get; set; }

    /// <summary>
    /// Per-element opacity override. Receives (row, index, defaultOpacity).
    /// Return null to use the default opacity.
    /// </summary>
    public Func<DataRow, int, float, float?>? OpacityOverride { get; set; }

    /// <summary>
    /// Custom data label content builder for this mark.
    /// Receives LabelContext. Return null to use default label formatting.
    /// </summary>
    public Func<LabelContext, IReadOnlyList<TooltipLine>?>? LabelContentBuilder { get; set; }

    /// <summary>
    /// Custom tooltip builder for this mark's elements.
    /// If set, takes priority over chart-level TooltipOptions.
    /// </summary>
    public Func<TooltipContext, IReadOnlyList<TooltipLine>>? TooltipContentBuilder { get; set; }

    /// <summary>
    /// Set a mark-level encode that overrides the chart's global encode for this channel.
    /// Returns this mark for fluent chaining.
    /// </summary>
    public Mark Encode(Channel channel, string fieldName)
    {
        _localEncodes[channel] = new FieldEncode(fieldName);
        return this;
    }

    /// <summary>Render this mark onto the canvas.</summary>
    public abstract void Render(MarkContext ctx);

    /// <summary>
    /// Override to contribute custom scale ranges.
    /// Called after AutoFitScales, before Render.
    /// </summary>
    public virtual void ContributeScales(
        ScaleSet scales, EncodeSet encodes, List<DataRow> data) { }

    /// <summary>
    /// Test if the given screen position hits any element rendered by this mark.
    /// Returns null if nothing was hit.
    /// </summary>
    public virtual HitResult? HitTest(MarkContext ctx, Vector2 screenPos) => null;

    // ── Y-channel helpers ─────────────────────────────────────

    /// <summary>Get the Y scale for this mark (respects <see cref="YChannel"/> binding).</summary>
    protected IScale? GetYScale(MarkContext ctx)
        => ctx.Scales.TryGet(YChannel);

    /// <summary>Resolve the Y value for this mark from a data row.</summary>
    protected object? ResolveY(MarkContext ctx, DataRow row)
        => ResolveEncode(ctx, YChannel, row);

    /// <summary>
    /// Resolve encode value with mark-local first, fallback to chart-level encodes.
    /// </summary>
    protected object? ResolveEncode(MarkContext ctx, Channel channel, DataRow row)
    {
        if (_localEncodes.TryGetValue(channel, out var local))
            return local switch
            {
                FieldEncode f    => row.Has(f.FieldName) ? row.Get(f.FieldName) : null,
                ConstantEncode c => c.Value,
                _                => null,
            };
        return ctx.Encodes.Resolve(channel, row);
    }

    /// <summary>
    /// Check whether a mark-local or chart-level encode is set for the given channel.
    /// </summary>
    protected bool HasEncode(MarkContext ctx, Channel channel)
        => _localEncodes.ContainsKey(channel) || ctx.Encodes.Has(channel);

    /// <summary>
    /// Get the field name from a mark-local encode, or null if not set or not a field encode.
    /// Used by Chart during auto-scale inference for Y2.
    /// </summary>
    internal string? GetLocalEncodeField(Channel channel)
        => _localEncodes.TryGetValue(channel, out var enc) && enc is FieldEncode f
            ? f.FieldName : null;

    /// <summary>Resolve color from the Color channel, or return default.</summary>
    protected static Color ResolveColor(MarkContext ctx, DataRow row, Color defaultColor)
    {
        if (!ctx.Encodes.Has(Channel.Color)) return defaultColor;
        var raw = ctx.Encodes.Resolve(Channel.Color, row);
        if (raw == null) return defaultColor;
        if (ctx.Scales.TryGet(Channel.Color) is ColorScale cs)
            return cs.MapColor(raw);
        return defaultColor;
    }

    /// <summary>
    /// Resolve color for a series key (not a row). Used by stacked/grouped marks
    /// where color is determined per-series rather than per-row.
    /// </summary>
    protected static Color ResolveSeriesColor(MarkContext ctx, object seriesKey)
    {
        if (ctx.Scales.TryGet(Channel.Color) is ColorScale cs)
            return cs.MapColor(seriesKey);
        return GetDefaultColor(ctx);
    }

    /// <summary>Resolve opacity from the Opacity channel, or return default.</summary>
    protected static float ResolveOpacity(MarkContext ctx, DataRow row, float defaultOpacity = 1f)
    {
        if (!ctx.Encodes.Has(Channel.Opacity)) return defaultOpacity;
        var raw = ctx.Encodes.Resolve(Channel.Opacity, row);
        if (raw == null) return defaultOpacity;
        return (float)ctx.Scales.Get(Channel.Opacity).Map(raw);
    }

    /// <summary>Brighten a color by the given factor (greater than 1 = brighter).</summary>
    protected static Color BrightenColor(Color color, float factor) => new(
        Mathf.Min(1f, color.R * factor),
        Mathf.Min(1f, color.G * factor),
        Mathf.Min(1f, color.B * factor),
        color.A);

    // ── Theme-aware helper methods ────────────────────────────

    /// <summary>Get the default mark color from the active theme.</summary>
    protected static Color GetDefaultColor(MarkContext ctx)
        => ctx.Theme?.DefaultMarkColor ?? new Color(0.29f, 0.59f, 0.98f);

    /// <summary>Get the selection highlight ring color from theme.</summary>
    protected static Color GetSelectionColor(MarkContext ctx)
        => ctx.Theme?.SelectionColor ?? new Color(1f, 1f, 1f, 0.8f);

    /// <summary>Get the selection highlight ring stroke width from theme.</summary>
    protected static float GetSelectionStrokeWidth(MarkContext ctx)
        => ctx.Theme?.SelectionStrokeWidth ?? 2f;

    /// <summary>Get the data label text color from theme.</summary>
    protected static Color GetDataLabelColor(MarkContext ctx)
        => ctx.Theme?.DataLabelColor ?? new Color(1f, 1f, 1f, 0.9f);

    /// <summary>Get the hover brightness multiplier from theme. Returns 1 (no change) when hover highlight is disabled.</summary>
    protected static float GetHoverBrighten(MarkContext ctx)
        => ctx.Theme?.EnableHoverHighlight == false ? 1f : ctx.Theme?.HoverBrighten ?? 1.2f;

    /// <summary>Get the hover size scale factor from theme. Returns 1 (no change) when hover highlight is disabled.</summary>
    protected static float GetHoverScale(MarkContext ctx)
        => ctx.Theme?.EnableHoverHighlight == false ? 1f : ctx.Theme?.HoverScale ?? 1.05f;

    /// <summary>Get the segment border color for polar charts from theme.</summary>
    protected static Color GetSegmentBorderColor(MarkContext ctx)
        => ctx.Theme?.SegmentBorderColor ?? new Color(0.08f, 0.08f, 0.12f);

    /// <summary>Get the segment border stroke width from theme.</summary>
    protected static float GetSegmentBorderWidth(MarkContext ctx)
        => ctx.Theme?.SegmentBorderWidth ?? 1f;

    /// <summary>Apply theme selection highlight style to a paint object. No-op when selection is disabled.</summary>
    protected static void ApplySelectionPaint(MarkContext ctx, IPaint2D paint, float opacity)
    {
        if (ctx.Theme?.EnableSelection == false) return;
        var sc = GetSelectionColor(ctx);
        paint.SetColor(sc with { A = sc.A * opacity })
             .SetStrokeWidth(GetSelectionStrokeWidth(ctx))
             .SetAntiAlias(true);
    }

    /// <summary>Check if hover explode effect is enabled.</summary>
    protected static bool IsHoverExplodeEnabled(MarkContext ctx)
        => ctx.Theme?.EnableHoverExplode ?? true;

    /// <summary>Resolve series key for a data row from the Color channel encoding.</summary>
    protected static string? ResolveSeriesKey(MarkContext ctx, DataRow row)
    {
        if (!ctx.Encodes.Has(Channel.Color)) return null;
        return ctx.Encodes.Resolve(Channel.Color, row)?.ToString();
    }

    /// <summary>
    /// Safely convert a data field value to double with a descriptive error message.
    /// Includes fast paths for common numeric types to avoid IConvertible interface dispatch.
    /// </summary>
    protected double ToDouble(object value, string fieldName)
    {
        if (value is double d) return d;
        if (value is float f) return f;
        if (value is int i) return i;
        if (value is long l) return l;
        try { return Convert.ToDouble(value); }
        catch (Exception ex)
        {
            throw new InvalidCastException(
                $"[{GetType().Name}] Cannot convert field '{fieldName}' value '{value}' " +
                $"({value.GetType().Name}) to double.", ex);
        }
    }

    /// <summary>
    /// Safely convert a data field value to float with a descriptive error message.
    /// Includes fast paths for common numeric types to avoid IConvertible interface dispatch.
    /// </summary>
    protected float ToSingle(object value, string fieldName)
    {
        if (value is float f) return f;
        if (value is double d) return (float)d;
        if (value is int i) return i;
        if (value is long l) return l;
        try { return Convert.ToSingle(value); }
        catch (Exception ex)
        {
            throw new InvalidCastException(
                $"[{GetType().Name}] Cannot convert field '{fieldName}' value '{value}' " +
                $"({value.GetType().Name}) to float.", ex);
        }
    }

    /// <summary>
    /// Safely get a field from a DataRow and convert to double with a descriptive error message.
    /// Includes fast paths for common numeric types to avoid IConvertible interface dispatch.
    /// </summary>
    protected double GetDouble(DataRow row, string fieldName)
    {
        var value = row.Get(fieldName);
        if (value is double d) return d;
        if (value is float f) return f;
        if (value is int i) return i;
        if (value is long l) return l;
        try { return Convert.ToDouble(value); }
        catch (Exception ex)
        {
            throw new InvalidCastException(
                $"[{GetType().Name}] Cannot convert field '{fieldName}' value '{value}' " +
                $"({value.GetType().Name}) to double.", ex);
        }
    }

    /// <summary>
    /// Check if the data row's series is hidden.
    /// </summary>
    protected static bool IsSeriesHidden(MarkContext ctx, DataRow row)
    {
        if (ctx.HiddenSeries is not { Count: not 0 }) return false;
        string? key = ResolveSeriesKey(ctx, row);
        return key != null && ctx.HiddenSeries.Contains(key);
    }

    /// <summary>
    /// Apply the mark's ColorOverride callback if set.
    /// </summary>
    protected Color ApplyColorOverride(DataRow row, int index, Color resolved)
    {
        if (ColorOverride == null) return resolved;
        var overridden = ColorOverride(row, index, resolved);
        return overridden ?? resolved;
    }

    /// <summary>
    /// Apply the mark's OpacityOverride callback if set.
    /// </summary>
    protected float ApplyOpacityOverride(DataRow row, int index, float resolved)
    {
        if (OpacityOverride == null) return resolved;
        var overridden = OpacityOverride(row, index, resolved);
        return overridden ?? resolved;
    }

    /// <summary>
    /// Resolve the final color for a data element: channel lookup + override callback.
    /// Combines <see cref="ResolveColor"/> and <see cref="ApplyColorOverride"/> in one call.
    /// </summary>
    protected Color ResolveColorWithOverride(MarkContext ctx, DataRow row, int index, Color defaultColor)
    {
        var color = ResolveColor(ctx, row, defaultColor);
        return ApplyColorOverride(row, index, color);
    }

    /// <summary>
    /// Resolve the final opacity for a data element: channel lookup + override callback.
    /// Combines <see cref="ResolveOpacity"/> and <see cref="ApplyOpacityOverride"/> in one call.
    /// </summary>
    protected float ResolveOpacityWithOverride(MarkContext ctx, DataRow row, int index, float defaultOpacity = 1f)
    {
        var opacity = ResolveOpacity(ctx, row, defaultOpacity);
        return ApplyOpacityOverride(row, index, opacity);
    }

    /// <summary>
    /// Compute effective opacity considering global animation opacity and series focus.
    /// </summary>
    protected static float ComputeEffectiveOpacity(
        MarkContext ctx, DataRow row, float baseOpacity = 1f)
    {
        float opacity = baseOpacity * ctx.Animation.GlobalOpacity;
        if (ctx.FocusedSeries != null)
        {
            string? seriesKey = ResolveSeriesKey(ctx, row);
            if (seriesKey != ctx.FocusedSeries)
                opacity *= ctx.Theme?.UnfocusedOpacity ?? 0.15f;
        }
        return opacity;
    }

    /// <summary>
    /// Compute series-level opacity considering global opacity and focus dimming.
    /// Use when the series key is already known to avoid redundant channel resolution.
    /// </summary>
    protected static float ComputeSeriesOpacity(MarkContext ctx, string? seriesKey, float baseOpacity)
    {
        float opacity = baseOpacity;
        if (ctx.FocusedSeries != null && seriesKey != ctx.FocusedSeries)
            opacity *= ctx.Theme?.UnfocusedOpacity ?? 0.15f;
        return opacity;
    }

    /// <summary>Compute effective entry/exit animation progress.</summary>
    protected static float ComputeAnimProgress(MarkContext ctx)
    {
        float entry = ctx.AnimationProgress;
        float exit = ctx.Animation.ExitProgress;
        return entry * (1f - exit);
    }

    /// <summary>
    /// Group data rows by the specified channel value, preserving insertion order.
    /// Rows without the channel encoded are grouped under "__default__".
    /// </summary>
    protected static Dictionary<object, List<DataRow>> GroupByChannel(
        MarkContext ctx, Channel ch)
    {
        var result = new Dictionary<object, List<DataRow>>();
        foreach (var row in ctx.Data)
        {
            object? key = ctx.Encodes.Has(ch) ? ctx.Encodes.Resolve(ch, row) : null;
            var k = key ?? "__default__";
            if (!result.ContainsKey(k)) result[k] = new();
            result[k].Add(row);
        }
        return result;
    }

    // Instance-level GroupByChannel cache to avoid per-frame allocation
    private Dictionary<object, List<DataRow>>? _cachedGroups;
    private int _cachedGroupsVersion = -1;
    private Channel _cachedGroupsChannel;

    /// <summary>
    /// Cached version of <see cref="GroupByChannel"/>. Rebuilt only when DataVersion changes.
    /// </summary>
    protected Dictionary<object, List<DataRow>> CachedGroupByChannel(
        MarkContext ctx, Channel ch)
    {
        if (_cachedGroups == null || _cachedGroupsVersion != ctx.DataVersion
            || _cachedGroupsChannel != ch)
        {
            _cachedGroups = GroupByChannel(ctx, ch);
            _cachedGroupsVersion = ctx.DataVersion;
            _cachedGroupsChannel = ch;
        }
        return _cachedGroups;
    }

    // ── Data label drawing ────────────────────────────────────

    /// <summary>
    /// Describes a single data label element to be rendered by <see cref="DrawLabels"/>.
    /// </summary>
    protected readonly record struct LabelElement(float X, float Y, string Text, float Opacity = 1f);

    /// <summary>
    /// Draw data labels at the specified screen positions.
    /// Call this at the end of Render() in subclasses when <see cref="ShowLabel"/> is true.
    /// </summary>
    protected void DrawLabels(MarkContext ctx, IReadOnlyList<LabelElement> elements)
    {
        if (elements.Count == 0) return;

        using var paint = ctx.Canvas.CreatePaint();
        var color = GetDataLabelColor(ctx);
        var font = new FontSettings
        {
            Size = FontSettings.Default.Size * 0.85f,
            Align = TextAlign.Center,
        };
        // Precompute Left/Right font variants outside the loop to avoid per-element struct allocation
        var fontLeft = font with { Align = TextAlign.Left };
        var fontRight = font with { Align = TextAlign.Right };
        float offsetY = font.Size * font.LineHeightMultiplier;

        foreach (var el in elements)
        {
            float lx = el.X;
            float ly = el.Y;
            var elFont = font;

            switch (LabelPosition)
            {
                case LabelPosition.Top:
                    ly -= offsetY * 0.4f;
                    break;
                case LabelPosition.Bottom:
                    ly += offsetY;
                    break;
                case LabelPosition.Inside:
                    ly += offsetY * 0.3f;
                    break;
                case LabelPosition.Left:
                    lx -= offsetY;
                    elFont = fontRight;
                    ly += offsetY * 0.3f;
                    break;
                case LabelPosition.Right:
                    lx += offsetY;
                    elFont = fontLeft;
                    ly += offsetY * 0.3f;
                    break;
            }

            paint.SetColor(color with { A = color.A * el.Opacity });
            ctx.Canvas.DrawText(el.Text, lx, ly, elFont, paint);
        }
    }
}

// ── StackMode ─────────────────────────────────────────────────

/// <summary>
/// Shared helper for computing stacked Y-axis scale ranges.
/// Used by IntervalMark and LineMark to avoid duplicating stacked scale logic.
/// </summary>
internal static class StackScaleHelper
{
    /// <summary>
    /// Contribute stacked Y-axis scale range based on stack mode.
    /// Calculates per-category totals and sets the Y scale accordingly.
    /// </summary>
    public static void ContributeStackedYScale(
        StackMode stack, ScaleSet scales, EncodeSet encodes, List<DataRow> data,
        Channel yChannel = Channel.Y)
    {
        if (stack == StackMode.None || data.Count == 0) return;

        var totals = new Dictionary<string, double>();
        foreach (var row in data)
        {
            var xKey = encodes.Resolve(Channel.X, row)?.ToString() ?? "";
            var yRaw = encodes.Resolve(yChannel, row);
            double yVal = yRaw != null ? ScaleConvert.ToDouble(yRaw, nameof(StackScaleHelper)) : 0;
            totals[xKey] = totals.GetValueOrDefault(xKey) + yVal;
        }

        if (stack == StackMode.Normalize)
            scales.Set(yChannel, new LinearScale(0, 1) { IncludeZero = true });
        else
        {
            double maxStacked = 0;
            foreach (var v in totals.Values)
                if (v > maxStacked) maxStacked = v;
            scales.Set(yChannel, new LinearScale(0, maxStacked));
        }
    }
}

/// <summary>
/// Shared helper for computing min/max Y-axis scale ranges from named data fields.
/// Used by BoxMark and CandlestickMark to avoid duplicating ContributeScales logic.
/// </summary>
internal static class ScaleContributionHelper
{
    /// <summary>
    /// Contribute a min/max Y scale range from two named data fields, with optional padding.
    /// Creates or widens an existing <see cref="LinearScale"/> on <paramref name="yChannel"/>.
    /// </summary>
    public static void ContributeMinMaxScale(
        ScaleSet scales, List<DataRow> data,
        string minField, string maxField,
        Channel yChannel = Channel.Y, double padding = 0.05)
    {
        if (data.Count == 0) return;

        double minVal = double.MaxValue, maxVal = double.MinValue;
        foreach (var row in data)
        {
            if (row.Has(maxField))
                maxVal = Math.Max(maxVal, ScaleConvert.ToDouble(row.Get(maxField), nameof(ScaleContributionHelper)));
            if (row.Has(minField))
                minVal = Math.Min(minVal, ScaleConvert.ToDouble(row.Get(minField), nameof(ScaleContributionHelper)));
        }
        if (minVal > maxVal) return;

        double range = maxVal - minVal;
        double pad = range * padding;

        if (scales.TryGet(yChannel) is LinearScale existing)
        {
            double newMin = Math.Min(existing.Min, minVal - pad);
            double newMax = Math.Max(existing.Max, maxVal + pad);
            if (newMin < existing.Min || newMax > existing.Max)
                scales.Set(yChannel, new LinearScale(newMin, newMax));
        }
        else
        {
            scales.Set(yChannel, new LinearScale(minVal - pad, maxVal + pad));
        }
    }
}

/// <summary>
/// Stacking mode for bar and area charts.
/// </summary>
public enum StackMode
{
    /// <summary>No stacking, series overlap or group side-by-side.</summary>
    None,

    /// <summary>Stack values: each series starts from the previous series top.</summary>
    Stack,

    /// <summary>Stack and normalize to 100% (each category sums to 1.0).</summary>
    Normalize,
}

/// <summary>
/// Bar orientation for IntervalMark.
/// </summary>
public enum BarOrientation
{
    /// <summary>Vertical bars (default).</summary>
    Vertical,

    /// <summary>Horizontal bars (swap X/Y).</summary>
    Horizontal,
}

/// <summary>
/// Step line mode for LineMark.
/// </summary>
public enum StepMode
{
    /// <summary>No step, smooth or straight line segments.</summary>
    None,

    /// <summary>Step occurs before the data point (horizontal first, then vertical).</summary>
    Before,

    /// <summary>Step occurs after the data point (vertical first, then horizontal).</summary>
    After,

    /// <summary>Step at midpoint between data points.</summary>
    Center,
}