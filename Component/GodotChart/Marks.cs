using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;
using GodotNodeExtension.Component.GodotChart.Canvas;

namespace GodotNodeExtension.Component.GodotChart;

/// <summary>
/// Rendering context passed to each Mark during chart drawing.
/// </summary>
public class MarkContext
{
    // The reference-type properties below are assigned by the render pipeline through their
    // internal setters before any mark reads them, so they are never null at use time.
    /// <summary>The canvas to draw on.</summary>
    public ICanvas2D     Canvas            { get; internal set; } = null!;

    /// <summary>The computed plot area rectangle with coordinate mapping.</summary>
    public PlotArea      Plot              { get; internal set; }

    /// <summary>
    /// The projection of this frame's coordinate system. The value a Cartesian chart hands out is a
    /// <see cref="PlanarMapper"/> over <see cref="Plot"/>; a mark that brings a coordinate system of
    /// its own (a map viewport, a camera) reads it from here.
    /// </summary>
    public ICoordinateMapper Mapper        { get; internal set; } = null!;

    /// <summary>All resolved scales (X, Y, Color, etc.).</summary>
    public ScaleSet      Scales            { get; internal set; } = null!;

    /// <summary>All resolved encode mappings.</summary>
    public EncodeSet     Encodes           { get; internal set; } = null!;

    /// <summary>The data rows to render.</summary>
    public List<DataRow> Data              { get; internal set; } = null!;

    /// <summary>
    /// Monotonically increasing version number that changes whenever data is updated.
    /// Marks can use this to invalidate layout caches accurately.
    /// </summary>
    public int DataVersion { get; internal set; }

    /// <summary>
    /// Combined version that changes when data, encodes, scales, or plot area change.
    /// Used by marks with expensive cached layouts (e.g. Chord, Sankey, Treemap).
    /// </summary>
    public int LayoutVersion { get; internal set; }

    /// <summary>
    /// Animation progress [0,1]. 1.0 = fully visible (no animation).
    /// Marks should use this to scale their rendering.
    /// </summary>
    public float AnimationProgress { get; internal set; } = 1f;

    /// <summary>
    /// Animation context containing all animation parameters (opacity, exit, hover, etc.).
    /// </summary>
    public AnimationContext Animation { get; internal set; } = AnimationContext.Default;

    /// <summary>Index of the data row currently hovered. -1 if none.</summary>
    public int HoveredRowIndex { get; internal set; } = -1;

    /// <summary>
    /// Identity of the chart this context belongs to (set by <c>Chart.Render</c>). Marks include it
    /// in their cache keys, so one mark instance can safely be reused by several charts.
    /// </summary>
    internal object? OwnerId { get; set; }

    /// <summary>Index of the data row currently selected. -1 if none.</summary>
    public int SelectedRowIndex { get; internal set; } = -1;

    /// <summary>
    /// True while the chart keeps the data layer in an image and repaints only the overlay
    /// (<c>Chart.UseLayerCache</c>).
    /// <para>
    /// In that frame the data layer must contain nothing that depends on the pointer, or the cached image
    /// would freeze it: <see cref="Mark.Render"/> paints the non-interactive state only, and the
    /// interaction-state visuals - hover, selection - are painted afterwards by
    /// <see cref="Mark.RenderOverlay"/>, once per frame, on top of the cached layer. That is what "the data
    /// layer is cacheable" means for a mark: it is the flag both halves of the split read
    /// (<see cref="Mark.InteractionStateInOverlay"/> says which marks have made the split).
    /// </para>
    /// <para>
    /// False (the default, cache off) is the historical single pass: <see cref="Mark.Render"/> paints
    /// everything, including the interactive state, and <see cref="Mark.RenderOverlay"/> draws nothing -
    /// which is what keeps a chart that does not ask for the cache pixel-identical to before the option
    /// existed.
    /// </para>
    /// </summary>
    public bool StateInOverlay { get; internal set; }

    /// <summary>The chart theme providing default visual styles.</summary>
    public ChartTheme? Theme { get; internal set; }

    /// <summary>
    /// The series key currently focused (e.g. via legend click).
    /// Null means no series is focused and all render at full opacity.
    /// </summary>
    public string? FocusedSeries { get; internal set; }

    /// <summary>
    /// Set of hidden series keys. Hidden series are excluded from rendering and hit testing.
    /// Null means all series are visible.
    /// </summary>
    public IReadOnlySet<string>? HiddenSeries { get; internal set; }

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
            Mapper            = Mapper,
            Scales            = Scales,
            Encodes           = Encodes,
            Data              = data,
            DataVersion       = DataVersion,
            LayoutVersion     = LayoutVersion,
            AnimationProgress = AnimationProgress,
            Animation         = Animation,
            HoveredRowIndex   = HoveredRowIndex,
            SelectedRowIndex  = SelectedRowIndex,
            StateInOverlay    = StateInOverlay,
            Theme             = Theme,
            FocusedSeries     = FocusedSeries,
            HiddenSeries      = HiddenSeries,
            OwnerId           = OwnerId,
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

    /// <summary>Set the scale for a channel (replacing any previous one).</summary>
    public void   Set(Channel ch, IScale scale) => _scales[ch] = scale;

    /// <summary>Get the scale for a channel. Throws if not set — call Has() first for optional channels.</summary>
    public IScale Get(Channel ch) =>
        _scales.TryGetValue(ch, out var s) ? s
        : throw new InvalidOperationException($"No scale set for channel {ch}. Did you forget Encode({ch}, ...)?");

    /// <summary>Try to get the scale for a channel. Returns null if not set.</summary>
    public IScale? TryGet(Channel ch) => _scales.GetValueOrDefault(ch);

    /// <summary>Whether a scale is configured for the channel.</summary>
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

    /// <summary>Set the encode for a channel (replacing any previous one).</summary>
    public void        Set(Channel ch, IEncodeValue enc) => _encodes[ch] = enc;

    /// <summary>Get the encode for a channel. Throws if not set.</summary>
    public IEncodeValue Get(Channel ch) =>
        _encodes.TryGetValue(ch, out var e) ? e
        : throw new InvalidOperationException($"No encode set for channel {ch}.");

    /// <summary>Try to get the encode for a channel. Returns null if not set.</summary>
    public IEncodeValue? TryGet(Channel ch) => _encodes.GetValueOrDefault(ch);

    /// <summary>Whether an encode is configured for the channel.</summary>
    public bool        Has(Channel ch) => _encodes.ContainsKey(ch);

    /// <summary>Enumerate all channels that have encodes set.</summary>
    public IEnumerable<Channel> Channels => _encodes.Keys;

    /// <summary>Resolve the raw value for a channel from a data row.</summary>
    public object? Resolve(Channel ch, DataRow row)
    {
        if (!_encodes.TryGetValue(ch, out var enc)) return null;
        return enc switch
        {
            // Has-guard: rows in heterogeneous data sets do not all carry every field, and a
            // missing field means "no value" rather than an exception mid-render.
            FieldEncode f    => row.Has(f.FieldName) ? row.Get(f.FieldName) : null,
            ConstantEncode c => c.Value,
            _                => null,
        };
    }

    /// <summary>
    /// This set with <paramref name="overlay"/> on top: a channel resolves through the overlay where it
    /// defines one and through this set otherwise. Used to give a mark its own channel bindings while
    /// keeping the chart-wide ones as the fallback - the same precedence G2 gives a mark's `encode`.
    /// </summary>
    public EncodeSet MergedWith(EncodeSet? overlay)
    {
        if (overlay is null || overlay._encodes.Count == 0) return this;

        var merged = new EncodeSet();
        foreach (var (channel, encode) in _encodes) merged._encodes[channel] = encode;
        foreach (var (channel, encode) in overlay._encodes) merged._encodes[channel] = encode;
        return merged;
    }

    /// <summary>Field name a channel is bound to, or null when it is unbound or a constant.</summary>
    public string? FieldOf(Channel ch)
        => _encodes.TryGetValue(ch, out var enc) && enc is FieldEncode f ? f.FieldName : null;
}

// ── Data label position ───────────────────────────────────────

/// <summary>
/// Controls where the data label is placed relative to the mark element.
/// </summary>
public enum LabelPosition
{
    /// <summary>Above the element.</summary>
    Top,

    /// <summary>Inside the element.</summary>
    Inside,

    /// <summary>Below the element.</summary>
    Bottom,

    /// <summary>Left of the element.</summary>
    Left,

    /// <summary>Right of the element.</summary>
    Right,
}

// ── Element style and interaction state ───────────────────────

/// <summary>
/// How one element (bar, slice, point, ...) looks: the fill it is painted with and its opacity.
/// The split follows G2: the <em>encode</em> decides what an element is, the <em>style</em> decides how
/// it is drawn.
/// </summary>
/// <param name="Fill">Fill colour of the element.</param>
/// <param name="Opacity">Opacity of the element, 0..1.</param>
public readonly record struct ElementStyle(Color Fill, float Opacity)
{
    /// <summary>The same style with another fill.</summary>
    public ElementStyle WithFill(Color fill) => this with { Fill = fill };

    /// <summary>The same style with another opacity.</summary>
    public ElementStyle WithOpacity(float opacity) => this with { Opacity = opacity };
}

/// <summary>
/// Interaction state of one element - G2's states under this library's names: the pointer is over it
/// (<see cref="Active"/>), it is the selected one (<see cref="Selected"/>) or it is dimmed because
/// another series is focused (<see cref="Inactive"/>).
/// </summary>
public enum ElementState
{
    /// <summary>Nothing special: the element is drawn with its data style.</summary>
    Default,

    /// <summary>The pointer is over the element (G2's <c>active</c> state).</summary>
    Active,

    /// <summary>The element is the selected one.</summary>
    Selected,

    /// <summary>Another series is focused, so this element is dimmed (G2's <c>inactive</c> state).</summary>
    Inactive,
}

/// <summary>
/// Declarative per-state styling (G2's state styles): set only what should differ from the theme. An
/// unset member keeps the theme value, so declaring nothing reproduces the default look.
/// </summary>
public sealed class ElementStateStyles
{
    /// <summary>Fill of the hovered element; null brightens the data fill instead.</summary>
    public Color? ActiveFill { get; set; }

    /// <summary>Brightness factor for the hovered element's fill (theme <c>HoverBrighten</c>).</summary>
    public float? ActiveBrighten { get; set; }

    /// <summary>Ring colour of the selected element (theme <c>SelectionColor</c>).</summary>
    public Color? SelectedStroke { get; set; }

    /// <summary>Ring width of the selected element (theme <c>SelectionStrokeWidth</c>).</summary>
    public float? SelectedStrokeWidth { get; set; }

    /// <summary>Opacity multiplier for elements outside the focused series (theme <c>UnfocusedOpacity</c>).</summary>
    public float? InactiveOpacity { get; set; }
}

// ── Mark base class ───────────────────────────────────────────

/// <summary>
/// Abstract base for all chart mark types (bar, line, point, etc.).
/// </summary>
public abstract class Mark
{
    /// <summary>
    /// Key identifying the inputs a cached mark layout depends on: the owning chart, the layout
    /// version, the exact data list instance and the set of hidden series. Comparing this instead of
    /// a bare version drops the cache when the mark switches to its own data
    /// (<see cref="Mark.Data"/>), when the same mark instance is reused by another chart, and when a
    /// series is hidden or shown again (the cached geometry still contains it).
    /// <para>
    /// This is the only cache key a mark may use. A mark that compares <c>DataVersion</c> or
    /// <c>LayoutVersion</c> alone keeps painting its cached geometry - and the colours baked into it -
    /// after a theme edit that did not change the data.
    /// </para>
    /// </summary>
    protected readonly record struct LayoutCacheKey(
        object? Owner, int LayoutVersion, object Data, IReadOnlySet<string>? HiddenSeries);

    /// <summary>Build the layout cache key for the given context.</summary>
    protected static LayoutCacheKey CacheKey(MarkContext ctx)
        => new(ctx.OwnerId, ctx.LayoutVersion, ctx.Data, ctx.HiddenSeries);

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
    public virtual bool ShowLabel { get; set; }

    /// <summary>
    /// Whether this mark draws against the chart's axes and grid. A mark that lays itself out inside the
    /// plot rectangle without using any scale - a waffle grid, for example - overrides this to false, and
    /// a chart whose marks all do so skips the axis and grid decorations, exactly like a chart built from
    /// non-Cartesian marks.
    /// </summary>
    public virtual bool UsesAxes => true;

    /// <summary>
    /// Data label format string. {0} = Y value, {1} = X value.
    /// <para>
    /// Honoured by every mark that draws data labels, including the ones that place their labels
    /// themselves (<c>PieMark</c>, <c>ChordMark</c>, <c>SankeyMark</c>, <c>FunnelMark</c>,
    /// <c>SunburstMark</c>, <c>TreemapMark</c>, <c>HeatmapMark</c>, <c>TimelineMark</c>): those put the
    /// text they show by default in {0} and the element's value in {1}, so a default format of
    /// <c>"{0}"</c> keeps their look unchanged. What {0}/{1} hold is documented on each mark.
    /// </para>
    /// <para>
    /// It does not apply to marks that draw no data labels, nor to scale readouts such as
    /// <c>GaugeMark</c>'s centre and min/max text, which are formatted by the scale itself.
    /// </para>
    /// </summary>
    public string LabelFormat { get; set; } = "{0}";

    /// <summary>
    /// Label position relative to the mark element. Read by the marks that lay their labels out
    /// through <see cref="DrawLabels"/> (interval/bars, line, point, milestone, custom marks). A mark
    /// that places its labels itself (<c>PieMark</c>, <c>ChordMark</c>, <c>SankeyMark</c>,
    /// <c>FunnelMark</c>, <c>SunburstMark</c>, <c>TreemapMark</c>, <c>HeatmapMark</c>,
    /// <c>TimelineMark</c>, <c>GaugeMark</c>) derives the position from its geometry and does not read
    /// this property.
    /// </summary>
    public LabelPosition LabelPosition { get; set; } = LabelPosition.Top;

    /// <summary>
    /// Per-element style callback - G2's <c>style</c> callback. Receives the row, its index and the
    /// style resolved from the data (colour channel + defaults) and returns the style to draw with,
    /// for example <c>(row, i, style) =&gt; style.WithFill(...)</c>.
    /// </summary>
    public Func<DataRow, int, ElementStyle, ElementStyle>? StyleOverride { get; set; }

    /// <summary>
    /// Declarative style per interaction state (G2's state styles). Hover, selection and series focus
    /// are already applied by the base helpers; this only overrides how they look.
    /// </summary>
    public ElementStateStyles States { get; } = new();

    /// <summary>
    /// Custom data label content builder for this mark: it replaces the text every element label of
    /// this mark is drawn with. Return null (or an empty list) for an element to keep the text the
    /// mark computed for it (<see cref="LabelFormat"/>).
    /// <para>
    /// Only the marks that draw their labels through <see cref="DrawLabels"/> read it: the built-in
    /// interval/line/point/milestone marks and any custom mark that calls <see cref="DrawLabels"/>. A
    /// mark that paints its labels itself uses its own builder instead - a pie/donut has
    /// <c>PieMark.SliceLabelBuilder</c> and <c>PieMark.CenterContentBuilder</c>, for example.
    /// </para>
    /// <para>
    /// The <see cref="LabelContext"/> handed over carries the element's row and row index when the mark
    /// knows them, the default text it computed and the element's fill colour. The returned lines are
    /// drawn one below the other, centred on the element's label anchor (which <see cref="LabelPosition"/>
    /// still shifts). Span colour, bold/italic, decoration, letter spacing and font size are applied;
    /// span icons and images are not - labels are plain text, the tooltip renderer draws those.
    /// </para>
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

    /// <summary>
    /// Bind a mark-level encode to a constant instead of a field - the same constant the chart-level
    /// <see cref="Chart.Encode(Channel, object)"/> takes, so a mark can be given its own fixed value
    /// (e.g. a colour) without reading a column.
    /// </summary>
    /// <param name="channel">Channel to bind.</param>
    /// <param name="constant">Value every row resolves to for this channel.</param>
    public Mark Encode(Channel channel, object constant)
    {
        ArgumentNullException.ThrowIfNull(constant);
        _localEncodes[channel] = new ConstantEncode(constant);
        return this;
    }

    // Cached merged encodes, rebuilt when the chart's encode set or the layout version changes.
    private EncodeSet? _bindEncodes;
    private EncodeSet? _bindEncodesFrom;
    private int _bindEncodesVersion = -1;
    private readonly HashSet<Channel> _warnedScaleConflicts = [];

    /// <summary>
    /// The encodes this mark draws with: the chart's set with this mark's own bindings applied on top.
    /// The mark-level binding wins for this mark only - the same precedence G2 gives a mark's `encode`,
    /// which is also why a mark with its own data can use its own field names.
    /// <para>
    /// Scales stay per channel (one auto-fitted scale is shared by every mark), so a mark that binds a
    /// channel to a different field than the chart does warns once: fit the scale explicitly with
    /// <c>Chart.Scale(...)</c> when the two fields do not share a value kind.
    /// </para>
    /// </summary>
    internal EncodeSet ResolveEncodes(EncodeSet chartEncodes, int layoutVersion)
    {
        if (_bindEncodes is null || !ReferenceEquals(_bindEncodesFrom, chartEncodes)
            || _bindEncodesVersion != layoutVersion)
        {
            EncodeSet? overlay = null;
            foreach (var channel in LocalEncodeChannels)
            {
                if (GetLocalEncode(channel) is not { } encode) continue;
                overlay ??= new EncodeSet();
                overlay.Set(channel, encode);

                if (chartEncodes.Has(channel) && chartEncodes.FieldOf(channel) is { } chartField
                    && encode is FieldEncode localField && localField.FieldName != chartField
                    && _warnedScaleConflicts.Add(channel))
                {
                    GD.PushWarning(
                        $"{GetType().Name}: channel {channel} is bound to \"{localField.FieldName}\" here " +
                        $"and to \"{chartField}\" on the chart; both share one scale. " +
                        "Set it explicitly with Chart.Scale(...) if they are not the same kind of value.");
                }
            }

            _bindEncodes = chartEncodes.MergedWith(overlay);
            _bindEncodesFrom = chartEncodes;
            _bindEncodesVersion = layoutVersion;
        }
        return _bindEncodes!;
    }

    /// <summary>
    /// Give a context this mark's encodes. The context is shared by every mark of one frame, so the
    /// chart's set is restored on every call: a mark that binds channels itself must not leak them
    /// into the marks that follow it.
    /// </summary>
    internal MarkContext BindEncodes(MarkContext ctx, EncodeSet chartEncodes, int layoutVersion)
    {
        ctx.Encodes = _localEncodes.Count > 0
            ? ResolveEncodes(chartEncodes, layoutVersion)
            : chartEncodes;
        return ctx;
    }

    /// <summary>Render this mark onto the canvas.</summary>
    public abstract void Render(MarkContext ctx);

    /// <summary>
    /// True when this mark paints its interaction-state visuals (hover, selection) on the overlay pass
    /// (<see cref="RenderOverlay"/>) instead of inside <see cref="Render"/>. Default false: the historical
    /// layout, where the highlight is part of the data layer.
    /// <para>
    /// <c>Chart.UseLayerCache</c> only engages when <b>every</b> mark of the chart answers true. A cached
    /// layer that holds one frozen highlight is exactly the "the picture stopped following the input"
    /// failure the option must not introduce, so a chart with an unmigrated mark renders single-pass
    /// instead (and says so once).
    /// </para>
    /// <para>
    /// The answer may depend on the configuration rather than on the type, and every built-in mark that
    /// answers false on purpose says why on its own declaration - their highlight cannot be reproduced on the
    /// overlay without erasing or double-blending what the cached layer holds.
    /// </para>
    /// </summary>
    public virtual bool InteractionStateInOverlay => false;

    /// <summary>
    /// Paint this mark's interaction-state visuals (hover, selection) for a frame whose data layer is kept
    /// in an image. Runs next to the crosshair, after the cached layer has been presented and after the axis
    /// labels and the legend, under the same plot clipping the data layer draws with.
    /// <para>
    /// Only called with <see cref="MarkContext.StateInOverlay"/> set: a mark that draws its state in
    /// <see cref="Render"/> (the default) has nothing to do here, and a mark that has moved the state out
    /// of <see cref="Render"/> must do nothing when the flag is clear - the two halves never both paint, so
    /// a chart with the cache off stays pixel-identical to the historical single pass.
    /// </para>
    /// </summary>
    public virtual void RenderOverlay(MarkContext ctx) { }

    /// <summary>
    /// The shape this mark's content wants, as width divided by height (1 = square), or null (the default)
    /// for a mark that fills the plot rectangle it is given.
    /// <para>
    /// A mark that draws from the plot's <b>short</b> edge - every polar mark does, its radius is
    /// <c>min(width, height) / 2</c> times a factor - wastes the long edge: on a wide cell the circle sits in
    /// the middle with a wide band of empty canvas on either side. Declaring the shape here lets the chart
    /// give such a mark the largest rectangle of that shape instead
    /// (<see cref="Chart.PlotAspectRatio"/>, <see cref="Chart.PlotAlignHorizontal"/>,
    /// <see cref="Chart.PlotAlignVertical"/>), without the page having to guess a size.
    /// </para>
    /// <para>
    /// It is a <i>preference</i>, and only honoured while the marks of a chart agree: one mark that fills the
    /// plot is enough reason to keep the whole rectangle (the filling mark needs it). The host always wins
    /// through <see cref="Chart.PlotAspectRatio"/>.
    /// </para>
    /// </summary>
    public virtual float? PreferredAspectRatio => null;


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

    /// <summary>
    /// Normalised position of one encoded value on a scale, or <see cref="double.NaN"/> when the scale cannot
    /// read the value at all.
    /// <para>
    /// A numeric scale throws on a value that is not a number, and the exception came straight out of
    /// <c>IScale.Map</c>: one method call away from the mark, so the mark's own guards (which all test the
    /// <i>result</i> for <c>NaN</c>) could not see it. The render stage caught the exception and logged a
    /// single error, which is how one colour string in a numeric column made a whole mark disappear from the
    /// frame. The input is therefore pre-checked for the numeric scales; a categorical (ordinal, colour,
    /// shape) scale compares keys and takes the value as it is, which is what keeps one call usable for both
    /// kinds of axis.
    /// </para>
    /// </summary>
    protected static double MapSafely(IScale scale, object value)
    {
        if (scale is LinearScale or LogScale)
        {
            if (!ScaleConvert.TryToDouble(value, out double numeric)) return double.NaN;
            return scale.Map(numeric);
        }
        return scale.Map(value);
    }

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

    /// <summary>
    /// Channels this mark encodes itself. Used by Chart during auto-scale inference, so that a
    /// mark-level encode is fitted even when the chart has no encode for that channel.
    /// </summary>
    internal IEnumerable<Channel> LocalEncodeChannels => _localEncodes.Keys;

    /// <summary>The mark-level encode for a channel, or null when the mark does not set one.</summary>
    internal IEncodeValue? GetLocalEncode(Channel channel)
        => _localEncodes.GetValueOrDefault(channel);

    /// <summary>
    /// Shape of one element, taken from the <see cref="Channel.Shape"/> channel through the shape scale -
    /// the same treatment G2 gives its shape channel. Falls back to <see cref="ShapeKind.Circle"/> when
    /// the channel is not encoded, the scale is missing, or the element is not shape-aware.
    /// </summary>
    protected static ShapeKind ResolveShape(MarkContext ctx, DataRow row)
    {
        if (!ctx.Encodes.Has(Channel.Shape)) return ShapeKind.Circle;
        if (ctx.Scales.TryGet(Channel.Shape) is not IShapeScale shapeScale) return ShapeKind.Circle;
        return shapeScale.MapShape(ctx.Encodes.Resolve(Channel.Shape, row));
    }

    /// <summary>Resolve color from the Color channel, or return default.</summary>
    protected static Color ResolveColor(MarkContext ctx, DataRow row, Color defaultColor)
    {
        if (!ctx.Encodes.Has(Channel.Color)) return defaultColor;

        // A pinned colour is the colour to draw with, not a value to look up in the scale: this is what
        // makes Encode(Channel.Color, Colors.Red) / "constant:#ff0000" - chart-wide or mark-level - paint
        // in that colour instead of a palette entry.
        if (ctx.Encodes.TryGet(Channel.Color) is ConstantEncode pinned)
            return ColorValues.TryParse(pinned.Value, out var literal) ? literal : defaultColor;

        var raw = ctx.Encodes.Resolve(Channel.Color, row);
        if (raw == null) return defaultColor;
        // Interface check: sequential/diverging/custom color scales must work here too.
        if (ctx.Scales.TryGet(Channel.Color) is IColorScale cs)
            return cs.MapColor(raw);
        // No scale at all (an empty data set): a value that is a colour is still the best answer.
        return ColorValues.TryParse(raw, out var fallback) ? fallback : defaultColor;
    }

    /// <summary>
    /// Resolve color for a series key (not a row). Used by stacked/grouped marks
    /// where color is determined per-series rather than per-row.
    /// </summary>
    protected static Color ResolveSeriesColor(MarkContext ctx, object seriesKey)
    {
        // A pinned colour paints every series the same colour, key or no key.
        if (ctx.Encodes.TryGet(Channel.Color) is ConstantEncode pinned)
            return ColorValues.TryParse(pinned.Value, out var literal) ? literal : GetDefaultColor(ctx);

        // No colour encode at all: the callers group their rows under a placeholder key (see
        // GroupByChannel), which says nothing about colour. Asking a colour scale to map it would throw
        // for a continuous ramp, so the mark keeps its default colour - the same fallback
        // ResolveColor uses for a row.
        if (!ctx.Encodes.Has(Channel.Color)) return GetDefaultColor(ctx);

        if (ctx.Scales.TryGet(Channel.Color) is IColorScale cs)
            return cs.MapColor(seriesKey);
        return GetDefaultColor(ctx);
    }

    /// <summary>Resolve opacity from the Opacity channel, or return default.</summary>
    protected static float ResolveOpacity(MarkContext ctx, DataRow row, float defaultOpacity = 1f)
    {
        if (!ctx.Encodes.Has(Channel.Opacity)) return defaultOpacity;
        if (ctx.Encodes.TryGet(Channel.Opacity) is ConstantEncode constant)
            return constant.Value is { } fixedOpacity
                ? FiniteOr(ToFloatOr(fixedOpacity, defaultOpacity), defaultOpacity)
                : defaultOpacity;

        var raw = ctx.Encodes.Resolve(Channel.Opacity, row);
        if (raw == null) return defaultOpacity;

        // An unset scale (e.g. an empty data set) must not throw mid-render: stay visible instead.
        var scale = ctx.Scales.TryGet(Channel.Opacity);
        return scale is null ? defaultOpacity : FiniteOr((float)scale.Map(raw), defaultOpacity);
    }

    /// <summary>
    /// Warn once when a normalized stack meets a category whose visible total is not positive: such a
    /// category has no share to draw, so its segments collapse to zero instead of being drawn with
    /// their raw value against the fixed [0, 1] axis of normalize mode. A hidden series and a
    /// negative-only category are the two usual causes.
    /// </summary>
    protected void WarnNormalizeZeroTotal()
    {
        if (_warnedNormalizeZeroTotal) return;
        _warnedNormalizeZeroTotal = true;
        GD.PushWarning(
            $"{GetType().Name}: StackMode.Normalize found a category whose visible total is not positive; "
            + "its segments are drawn as zero.");
    }

    private bool _warnedNormalizeZeroTotal;

    /// <summary>Best-effort conversion of an encoded constant to float.</summary>
    private static float ToFloatOr(object value, float fallback)
    {
        try { return Convert.ToSingle(value, CultureInfo.InvariantCulture); }
        catch (Exception) { return fallback; }
    }

    /// <summary>A NaN/infinity would reach the backend as a broken paint: stay visible instead.</summary>
    private static float FiniteOr(float value, float fallback) => float.IsFinite(value) ? value : fallback;

    /// <summary>Brighten a color by the given factor (greater than 1 = brighter).</summary>
    protected static Color BrightenColor(Color color, float factor) => new(
        Mathf.Min(1f, color.R * factor),
        Mathf.Min(1f, color.G * factor),
        Mathf.Min(1f, color.B * factor),
        color.A);

    // ── Theme-aware helper methods ────────────────────────────

    /// <summary>Get the default mark color from the active theme (<see cref="ChartTheme.Default"/> when none is attached).</summary>
    protected static Color GetDefaultColor(MarkContext ctx)
        => (ctx.Theme ?? ChartTheme.Default).DefaultMarkColor;

    /// <summary>Get the selection highlight ring color from theme (<see cref="ChartTheme.Default"/> when none is attached).</summary>
    protected static Color GetSelectionColor(MarkContext ctx)
        => (ctx.Theme ?? ChartTheme.Default).SelectionColor;

    /// <summary>Get the selection highlight ring stroke width from theme (<see cref="ChartTheme.Default"/> when none is attached).</summary>
    protected static float GetSelectionStrokeWidth(MarkContext ctx)
        => (ctx.Theme ?? ChartTheme.Default).SelectionStrokeWidth;

    /// <summary>Get the data label text color from theme (<see cref="ChartTheme.Default"/> when none is attached).</summary>
    protected static Color GetDataLabelColor(MarkContext ctx)
        => (ctx.Theme ?? ChartTheme.Default).DataLabelColor;

    /// <summary>
    /// Get the hover brightness multiplier from the active theme
    /// (<see cref="ChartTheme.Default"/> when none is attached).
    /// Returns 1 (no change) when hover highlight is disabled.
    /// </summary>
    protected static float GetHoverBrighten(MarkContext ctx)
    {
        var theme = ctx.Theme ?? ChartTheme.Default;
        return theme.EnableHoverHighlight ? theme.HoverBrighten : 1f;
    }

    /// <summary>
    /// Get the hover size scale factor from the active theme
    /// (<see cref="ChartTheme.Default"/> when none is attached).
    /// Returns 1 (no change) when hover highlight is disabled.
    /// </summary>
    protected static float GetHoverScale(MarkContext ctx)
    {
        var theme = ctx.Theme ?? ChartTheme.Default;
        return theme.EnableHoverHighlight ? theme.HoverScale : 1f;
    }

    /// <summary>
    /// A mark's own hover factor (a radius ratio, a wick-width scale, ...), or 1 when the theme's
    /// <see cref="ChartTheme.EnableHoverHighlight"/> is off. Every mark that enlarges something on hover goes
    /// through this, so that one switch turns them all off together instead of only the ones that happen to
    /// use <see cref="GetHoverScale"/>.
    /// </summary>
    protected static float HoverScaled(MarkContext ctx, float factor)
        => (ctx.Theme ?? ChartTheme.Default).EnableHoverHighlight ? factor : 1f;

    /// <summary>
    /// Whether an overlay pass has anything to paint, and for which rows: false while the chart keeps the data
    /// layer in an image (<see cref="MarkContext.StateInOverlay"/> is off - with the cache off
    /// <see cref="Render"/> painted the state itself) and false while neither the hovered nor the selected row
    /// is known.
    /// <para>
    /// Every mark's overlay pass starts here instead of repeating the two conditions and the row lookups, so
    /// none of them can gate on something else than the others (the thirteen copies had already drifted in what
    /// they checked *after* the gate, never in the gate itself - which is exactly the kind of copy that drifts).
    /// </para>
    /// </summary>
    /// <param name="ctx">Context of the frame.</param>
    /// <param name="hovered">The hovered row, or -1.</param>
    /// <param name="selected">The selected row, or -1.</param>
    protected static bool OverlayRows(MarkContext ctx, out int hovered, out int selected)
    {
        hovered = ctx.StateInOverlay ? ctx.HoveredRowIndex : -1;
        selected = ctx.StateInOverlay ? ctx.SelectedRowIndex : -1;
        return hovered >= 0 || selected >= 0;
    }

    /// <summary>
    /// Row indices whose interaction state an overlay pass has to paint: the hovered and the selected one, in
    /// that order and without a duplicate. Only these two are drawn, which keeps a hover frame O(1) in the
    /// number of elements instead of O(rows).
    /// </summary>
    protected static int[] InteractionRows(MarkContext ctx)
    {
        int hovered = ctx.HoveredRowIndex;
        int selected = ctx.SelectedRowIndex;
        if (hovered < 0) return selected < 0 ? [] : [selected];
        if (selected < 0 || selected == hovered) return [hovered];
        return [hovered, selected];
    }
    /// <summary>Get the segment border color for polar charts from theme (<see cref="ChartTheme.Default"/> when none is attached).</summary>
    protected static Color GetSegmentBorderColor(MarkContext ctx)
        => (ctx.Theme ?? ChartTheme.Default).SegmentBorderColor;

    /// <summary>Get the segment border stroke width from theme (<see cref="ChartTheme.Default"/> when none is attached).</summary>
    protected static float GetSegmentBorderWidth(MarkContext ctx)
        => (ctx.Theme ?? ChartTheme.Default).SegmentBorderWidth;

    /// <summary>Apply the selection ring style to a paint object. No-op when selection is disabled.</summary>
    protected void ApplySelectionPaint(MarkContext ctx, IPaint2D paint, float opacity)
    {
        if (!(ctx.Theme ?? ChartTheme.Default).EnableSelection) return;
        var sc = States.SelectedStroke ?? GetSelectionColor(ctx);
        paint.SetColor(sc with { A = sc.A * opacity })
             .SetStrokeWidth(States.SelectedStrokeWidth ?? GetSelectionStrokeWidth(ctx))
             .SetAntiAlias(true);
    }

    /// <summary>Check if hover explode effect is enabled (<see cref="ChartTheme.Default"/> when none is attached).</summary>
    protected static bool IsHoverExplodeEnabled(MarkContext ctx)
        => (ctx.Theme ?? ChartTheme.Default).EnableHoverExplode;

    /// <summary>Resolve series key for a data row from the Color channel encoding.</summary>
    protected static string? ResolveSeriesKey(MarkContext ctx, DataRow row)
    {
        if (!ctx.Encodes.Has(Channel.Color)) return null;
        return ctx.Encodes.Resolve(Channel.Color, row)?.ToString();
    }

    /// <summary>
    /// Safely convert a data field value to double.
    /// Includes fast paths for common numeric types to avoid IConvertible interface dispatch.
    /// <para>
    /// A value that cannot be read as a number comes back as <see cref="double.NaN"/> (with a one-time
    /// warning naming the field and the mark), the same answer a null field gets: the caller skips the
    /// element it belongs to. Throwing was the old contract, and it took the whole frame down - one
    /// colour string in a numeric column made every element of the mark disappear behind a single
    /// <c>GD.PushError</c> from the render stage's catch-all.
    /// </para>
    /// </summary>
    protected double ToDouble(object? value, string fieldName)
    {
        // A null field value means "missing", not zero. Report it as NaN so callers can skip
        // the element instead of silently plotting a 0 for it.
        if (value is not { }) return double.NaN;

        // A non-finite value passes straight through: NaN and ±Infinity are "no position" (the caller skips
        // the element), where the shared ladder would call them unreadable and report the field. Everything
        // else goes through that ladder - the one ScaleConvert owns for the scales as well.
        if (value is double rawDouble) return rawDouble;
        if (value is float rawFloat) return rawFloat;
        if (ScaleConvert.TryToDouble(value, out double parsed)) return parsed;

        return WarnUnusableValue(fieldName, value);
    }

    /// <summary>
    /// Safely convert a data field value to float: the double ladder narrowed, so a value that cannot be read
    /// as a number comes back as <see cref="float.NaN"/> (see <see cref="ToDouble(object, string)"/>).
    /// </summary>
    protected float ToSingle(object? value, string fieldName) => (float)ToDouble(value, fieldName);

    /// <summary>
    /// Safely get a field from a DataRow and convert it to double (see <see cref="ToDouble(object, string)"/>
    /// for the contract: a value that cannot be read as a number comes back as <see cref="double.NaN"/>).
    /// </summary>
    protected double GetDouble(DataRow row, string fieldName) => ToDouble(row.Get(fieldName), fieldName);

    /// <summary>
    /// The palette the mark draws from: the theme's own when it has one, the built-in default otherwise. Four
    /// round/diagram marks resolve their element colours through this, so "the theme's palette, else the
    /// default one" lives in one place.
    /// </summary>
    protected static Color[] PaletteOf(MarkContext ctx)
        => ctx.Theme?.Palette is { Length: > 0 } themePalette ? themePalette : ChartTheme.DefaultPalette;

    /// <summary>
    /// Fields this mark could not read as a number, so the warning is raised once per field instead of
    /// once per row per frame.
    /// </summary>
    private HashSet<string>? _warnedUnusableValues;

    /// <summary>
    /// Report a field value that is not a number: it has no position on an axis, so the elements that
    /// carry it are skipped (it is reported as <see cref="double.NaN"/>). The message names the mark, the
    /// field and the value, which is what the old exception message carried - the value is only no longer
    /// fatal to the frame.
    /// </summary>
    private double WarnUnusableValue(string fieldName, object value)
    {
        _warnedUnusableValues ??= [];
        if (_warnedUnusableValues.Add(fieldName))
        {
            GD.PushWarning(
                $"{GetType().Name}: field '{fieldName}' holds a value that is not a number " +
                $"('{value}', {value.GetType().Name}); the elements using it are skipped.");
        }
        return double.NaN;
    }

    /// <summary>
    /// Check whether a row contains the field with a non-null value.
    /// Numeric reads must be guarded with this (or <see cref="HasFields(DataRow, string, string)"/>):
    /// <see cref="DataRow.Get(string)"/> throws when the key is absent, and a null value is
    /// reported as <see cref="double.NaN"/> by <see cref="GetDouble"/>.
    /// <para>
    /// The non-null test uses the <c>{ }</c> pattern: a row may store a null even though
    /// <see cref="DataRow.Get(string)"/> is typed as a non-nullable <see cref="object"/>.
    /// </para>
    /// </summary>
    protected static bool HasField(DataRow row, string fieldName)
        => row.Has(fieldName) && row.Get(fieldName) is { };

    /// <inheritdoc cref="HasField"/>
    protected static bool HasFields(DataRow row, string a, string b)
        => HasField(row, a) && HasField(row, b);

    /// <inheritdoc cref="HasField"/>
    protected static bool HasFields(DataRow row, string a, string b, string c, string d)
        => HasField(row, a) && HasField(row, b) && HasField(row, c) && HasField(row, d);

    /// <inheritdoc cref="HasField"/>
    protected static bool HasFields(DataRow row, string a, string b, string c, string d, string e)
        => HasField(row, a) && HasField(row, b) && HasField(row, c) && HasField(row, d) && HasField(row, e);

    /// <summary>
    /// Safely read a field value that is used as a category/label string.
    /// Returns null when the field is absent or its value is null, so callers can skip the row
    /// instead of dereferencing a null reference.
    /// </summary>
    protected static string? GetStringOrNull(DataRow row, string fieldName)
    {
        if (!row.Has(fieldName)) return null;
        return row.Get(fieldName) is { } value ? value.ToString() : null;
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
    /// Run the mark's style callback (G2's <c>style</c>): the data decides the base style, the callback
    /// gets the last word.
    /// </summary>
    protected ElementStyle ResolveStyle(DataRow row, int index, ElementStyle style)
        => StyleOverride?.Invoke(row, index, style) ?? style;

    /// <summary>
    /// Interaction state of one element, from the chart's hover/selection/focus state.
    /// <para>
    /// Hover and selection are keyed on the <b>row index</b> the interaction layer reports, and that
    /// index belongs to the chart's render data - not to this mark. A chart carrying several marks
    /// therefore highlights the element with the same index in every one of them, and a mark with its
    /// own data should have its own hover handling if that matters.
    /// </para>
    /// </summary>
    protected static ElementState StateOf(MarkContext ctx, DataRow row, int index)
    {
        if (index >= 0 && index == ctx.HoveredRowIndex) return ElementState.Active;
        if (index >= 0 && index == ctx.SelectedRowIndex) return ElementState.Selected;
        if (ctx.FocusedSeries != null && ResolveSeriesKey(ctx, row) != ctx.FocusedSeries)
            return ElementState.Inactive;
        return ElementState.Default;
    }

    /// <summary>
    /// Fill to paint one element with: the colour channel / mark default, then the style callback, then
    /// the <see cref="ElementState.Active"/> state (hover brighten or <see cref="ElementStateStyles.ActiveFill"/>).
    /// <para>
    /// While <see cref="MarkContext.StateInOverlay"/> is set the data layer paints every element in its
    /// default state: the hover look belongs to <see cref="RenderOverlay"/> then, and a cached layer that
    /// baked it in would keep showing it after the pointer moved on. A mark that needs the hover fill on the
    /// overlay pass asks for it with <see cref="ActiveFillOf"/>.
    /// </para>
    /// </summary>
    protected Color ResolveFill(MarkContext ctx, DataRow row, int index, Color defaultColor)
    {
        var fill = ResolveColor(ctx, row, defaultColor);
        var style = ResolveStyle(row, index, new ElementStyle(fill, 1f));
        if (ctx.StateInOverlay || StateOf(ctx, row, index) != ElementState.Active) return style.Fill;

        return ActiveFillOf(ctx, style.Fill);
    }

    /// <summary>
    /// Fill the <b>hovered</b> element is painted with: <see cref="ElementStateStyles.ActiveFill"/> when the
    /// host declared one, otherwise the element's own fill brightened by
    /// <see cref="ElementStateStyles.ActiveBrighten"/> or the theme's <see cref="ChartTheme.HoverBrighten"/>.
    /// <para>
    /// This is the value <see cref="ResolveFill"/> returns for the hovered element while the state is drawn
    /// in the data layer. A mark that paints its hover state on the overlay pass
    /// (<see cref="InteractionStateInOverlay"/>) asks for it directly, because there the state is no longer
    /// part of <see cref="ResolveFill"/>.
    /// </para>
    /// </summary>
    /// <param name="ctx">Context of the frame (theme and state styles).</param>
    /// <param name="dataFill">The element's own fill, as resolved from the data (style callback applied).</param>
    protected Color ActiveFillOf(MarkContext ctx, Color dataFill)
        => States.ActiveFill ?? BrightenColor(dataFill, States.ActiveBrighten ?? GetHoverBrighten(ctx));

    /// <summary>
    /// Opacity of one element: the <see cref="Channel.Opacity"/> value (it replaces
    /// <paramref name="baseOpacity"/>, the mark's own default) times the global animation opacity and
    /// the focus dimming, then the style callback and the state's opacity. Pass <paramref name="index"/>
    /// when the element's row index is known so hover/selection/focus styling can apply.
    /// </summary>
    protected float ComputeElementOpacity(
        MarkContext ctx, DataRow row, int index, float baseOpacity = 1f)
    {
        float opacity = ResolveOpacity(ctx, row, baseOpacity) * ctx.Animation.GlobalOpacity;
        if (ctx.FocusedSeries != null && ResolveSeriesKey(ctx, row) != ctx.FocusedSeries)
            opacity *= States.InactiveOpacity ?? (ctx.Theme ?? ChartTheme.Default).UnfocusedOpacity;

        return Mathf.Clamp(ResolveStyle(row, index, new ElementStyle(default, opacity)).Opacity, 0f, 1f);
    }

    /// <summary>
    /// Apply focus dimming to <paramref name="baseOpacity"/> for an already-resolved series key;
    /// global animation opacity must be folded into <paramref name="baseOpacity"/> by the caller.
    /// </summary>
    protected float ComputeSeriesOpacity(MarkContext ctx, string? seriesKey, float baseOpacity)
    {
        float opacity = baseOpacity;
        if (ctx.FocusedSeries != null && seriesKey != ctx.FocusedSeries)
            opacity *= States.InactiveOpacity ?? (ctx.Theme ?? ChartTheme.Default).UnfocusedOpacity;
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
            if (!result.ContainsKey(k)) result[k] = [];
            result[k].Add(row);
        }
        return result;
    }

    // Instance-level GroupByChannel cache to avoid per-frame allocation
    private Dictionary<object, List<DataRow>>? _cachedGroups;
    private int _cachedGroupsVersion = -1;
    private Channel _cachedGroupsChannel;

    /// <summary>
    /// Cached version of <see cref="GroupByChannel"/>. Rebuilt when <c>DataVersion</c> or the
    /// channel argument changes.
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

    // ── Reusable drawing objects ──────────────────────────────

    private IPath2D? _shapePath;
    private IPaint2D? _shapePaint;
    private ICanvas2D? _shapeCanvas;

    /// <summary>
    /// A path owned by this mark, reset and ready for the next element. Do <b>not</b> dispose it.
    /// <para>
    /// Marks used to create an <see cref="IPath2D"/>/<see cref="IPaint2D"/> per element per frame.
    /// Both are pooled by the backend, but the pool churns (and its native objects are recreated)
    /// once more objects are alive than the pool holds, which used to be a per-frame allocation
    /// hotspot. One pair per mark removes that.
    /// </para>
    /// </summary>
    protected IPath2D ShapePath(MarkContext ctx)
    {
        EnsureShapeObjects(ctx);
        return _shapePath!.Reset();
    }

    /// <summary>Paint owned by this mark. Do <b>not</b> dispose it.</summary>
    protected IPaint2D ShapePaint(MarkContext ctx)
    {
        EnsureShapeObjects(ctx);
        return _shapePaint!;
    }

    private void EnsureShapeObjects(MarkContext ctx)
    {
        if (_shapePath != null && ReferenceEquals(_shapeCanvas, ctx.Canvas)) return;

        // The canvas changed (or this is the first frame): drop the old pair and make a new one.
        _shapePaint?.Dispose();
        _shapePath?.Dispose();
        _shapeCanvas = ctx.Canvas;
        _shapePath = ctx.Canvas.CreatePath();
        _shapePaint = ctx.Canvas.CreatePaint();
    }

    // ── Data label drawing ────────────────────────────────────

    private readonly List<LabelElement> _labelBuffer = [];

    /// <summary>
    /// Label buffer for the current frame, cleared and ready to fill. Returns null when the mark
    /// does not show labels, so the drawing loops stay allocation-free.
    /// </summary>
    protected List<LabelElement>? BeginLabelCollection()
    {
        if (!ShowLabel) return null;
        _labelBuffer.Clear();
        return _labelBuffer;
    }

    /// <summary>
    /// Format a data label. Avoids <see cref="string.Format(string,object,object)"/> (which boxes
    /// and parses the format string every call) for the two common shapes.
    /// </summary>
    protected static string FormatLabel(string format, object? yValue, object? xValue)
    {
        if (format == "{0}") return yValue?.ToString() ?? string.Empty;
        if (format == "{0}: {1}") return $"{yValue}: {xValue}";
        return string.Format(CultureInfo.InvariantCulture, format, yValue, xValue);
    }

    /// <summary>
    /// Numeric value to report for a label element - the <see cref="LabelContext.Value"/>
    /// <see cref="LabelContentBuilder"/> receives. A value that is not a number reports 0 instead of
    /// throwing in the middle of a frame.
    /// </summary>
    protected static float LabelValue(object? value) => value is null ? 0f : ToFloatOr(value, 0f);

    /// <summary>
    /// Font a mark should draw with: a font that names neither a Godot font nor a family inherits the
    /// theme's font, so setting <see cref="ChartTheme.Font"/> (or <see cref="ChartTheme.FontFamily"/>)
    /// once reaches every label - including the CJK scripts a Latin-only default cannot draw.
    /// </summary>
    /// <summary>
    /// Font a mark should draw with: a font that names neither a Godot font nor a family inherits the
    /// theme's font, so setting <see cref="ChartTheme.Font"/> (or <see cref="ChartTheme.FontFamily"/>)
    /// once reaches every label - including the CJK scripts a Latin-only default cannot draw.
    /// <para>
    /// A theme that sets <see cref="ChartTheme.LabelFontSize"/> scales the size the caller asked for by the
    /// same ratio, so a mark's in-plot labels grow with the axis labels while a label drawn at, say, 85% of the
    /// default stays at 85% of the themed size.
    /// </para>
    /// </summary>
    protected static FontSettings ThemedFont(MarkContext ctx, FontSettings font)
    {
        if (ctx.Theme is not { } theme) return font;

        var themed = font.GodotFont is null && string.IsNullOrEmpty(font.Family)
            ? font with { GodotFont = theme.Font, Family = theme.FontFamily }
            : font;

        return theme.LabelFontSize > 0f
            ? themed with { Size = themed.Size * (theme.LabelFontSize / FontSettings.Default.Size) }
            : themed;
    }

    /// <summary>
    /// Draw text horizontally centred on <paramref name="x"/> and vertically centred on
    /// <paramref name="y"/>. The canvas treats x/y as the baseline start, so the measured line
    /// height is used to lift the baseline into the middle of the line box; without this, centred
    /// labels sit about half a line too low and to the right. The font comes from
    /// <see cref="ThemedFont"/>, so mark labels follow the chart font.
    /// </summary>
    protected static void DrawTextCentered(
        MarkContext ctx, IPaint2D paint, string text, float x, float y, FontSettings font)
    {
        font = ThemedFont(ctx, font);
        float lineHeight = ctx.Canvas.MeasureText(text, font).Height;
        ctx.Canvas.DrawText(text, x, y + lineHeight * 0.35f, font with { Align = TextAlign.Center }, paint);
    }

    /// <summary>
    /// Describes a single data label element to be rendered by <see cref="DrawLabels"/>.
    /// <para>
    /// <paramref name="Row"/>, <paramref name="RowIndex"/>, <paramref name="Fill"/> and
    /// <paramref name="Value"/> only feed <see cref="LabelContentBuilder"/>: a mark that wants the
    /// callback to know which element it is labelling passes them, a mark that only formats text can
    /// leave them at their defaults.
    /// </para>
    /// </summary>
    /// <param name="X">Anchor X of the label in screen coordinates.</param>
    /// <param name="Y">Anchor Y of the label in screen coordinates.</param>
    /// <param name="Text">Text to draw unless <see cref="LabelContentBuilder"/> replaces it.</param>
    /// <param name="Opacity">Opacity of the label (the element's own opacity).</param>
    /// <param name="Row">Data row this label belongs to, when the mark knows it.</param>
    /// <param name="RowIndex">Index of the label's row, or -1 when unknown.</param>
    /// <param name="Fill">Fill colour of the element the label belongs to.</param>
    /// <param name="Value">Numeric value of the element, or 0 when it has none.</param>
    protected readonly record struct LabelElement(
        float X, float Y, string Text, float Opacity = 1f,
        DataRow? Row = null, int RowIndex = -1, Color Fill = default, float Value = 0f);

    /// <summary>
    /// Draw data labels at the specified screen positions.
    /// Call this at the end of Render() in subclasses when <see cref="ShowLabel"/> is true.
    /// <para>
    /// Each element's text comes from <see cref="LabelContentBuilder"/> when the mark sets one and the
    /// callback returns lines for that element; otherwise the element's own text (usually produced with
    /// <see cref="FormatLabel"/>) is drawn.
    /// </para>
    /// </summary>
    protected void DrawLabels(MarkContext ctx, IReadOnlyList<LabelElement> elements)
    {
        if (elements.Count == 0) return;

        var builder = LabelContentBuilder;

        using var paint = ctx.Canvas.CreatePaint();
        var color = GetDataLabelColor(ctx);
        var font = ThemedFont(ctx, new FontSettings
        {
            Size = FontSettings.Default.Size * 0.85f,
            Align = TextAlign.Center,
        });
        // Precompute Left/Right font variants outside the loop to avoid per-element struct allocation
        var fontLeft = font with { Align = TextAlign.Left };
        var fontRight = font with { Align = TextAlign.Right };
        // m25: take the line height from the canvas metrics so it stays correct for any backend.
        float offsetY = ctx.Canvas.MeasureText("0", font).Height;

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

            if (builder is not null && builder(LabelContextFor(ctx, el)) is { Count: > 0 } lines)
            {
                DrawLabelContent(ctx, paint, lines, lx, ly, elFont, color, el.Opacity, offsetY);
                continue;
            }

            paint.SetColor(color with { A = color.A * el.Opacity });
            ctx.Canvas.DrawText(el.Text, lx, ly, elFont, paint);
        }
    }

    /// <summary>The context handed to <see cref="LabelContentBuilder"/> for one label element.</summary>
    private static LabelContext LabelContextFor(MarkContext ctx, in LabelElement el) => new()
    {
        Data = ctx.Data,
        Row = el.Row,
        RowIndex = el.RowIndex,
        ElementColor = el.Fill,
        SeriesKey = el.Row is null ? null : ResolveSeriesKey(ctx, el.Row),
        DefaultText = el.Text,
        Value = el.Value,
    };

    /// <summary>
    /// Draw the lines a <see cref="LabelContentBuilder"/> returned for one element: every line is laid
    /// out from the centre of the element's label anchor, one line below the other, and the spans of a
    /// line are painted in order so their colours and font styles survive.
    /// </summary>
    private static void DrawLabelContent(
        MarkContext ctx, IPaint2D paint, IReadOnlyList<TooltipLine> lines,
        float lx, float ly, FontSettings font, Color color, float opacity, float lineHeight)
    {
        for (int i = 0; i < lines.Count; i++)
        {
            var spans = lines[i].Spans;
            if (spans.Length == 0) continue;

            // Same baseline rule as a plain label: the first line sits exactly where the element's own
            // text would have been, the following ones are pushed down by one line box each.
            float baseline = ly + i * lineHeight + lineHeight * 0.35f;

            // Measure the whole line first, then lay the spans out from its left edge; drawing them all
            // at the same x would overlap "icon + value + unit" into one blob.
            float width = 0f;
            foreach (var span in spans)
                width += ctx.Canvas.MeasureText(span.Text, SpanFont(ctx, span, font)).Width;

            float x = lx - width / 2f;
            foreach (var span in spans)
            {
                var spanFont = SpanFont(ctx, span, font);
                var spanColor = span.Color ?? color;
                paint.SetColor(spanColor with { A = spanColor.A * opacity });
                ctx.Canvas.DrawText(span.Text, x, baseline, spanFont, paint);
                x += ctx.Canvas.MeasureText(span.Text, spanFont).Width;
            }
        }
    }

    /// <summary>
    /// Font of one span of a custom label line: the label font with the span's own overrides. Every span of a
    /// <see cref="TooltipLine"/> goes through it, so a mark that lays a line out itself (the pie's centre
    /// content) cannot end up drawing the same line with fewer styles than the shared
    /// <see cref="DrawLabelContent"/> does.
    /// </summary>
    protected static FontSettings SpanFont(MarkContext ctx, in TooltipSpan span, FontSettings font)
        => ThemedFont(ctx, font with
        {
            Size = span.FontSize ?? font.Size,
            Bold = span.Bold,
            Italic = span.Italic,
            Decoration = span.Decoration,
            LetterSpacing = span.LetterSpacing,
            Family = span.Family,
            GodotFont = span.GodotFont,
        });
}

// ── StackMode ─────────────────────────────────────────────────

/// <summary>
/// Shared helper for computing stacked Y-axis scale ranges.
/// Used by IntervalMark and LineMark to avoid duplicating stacked scale logic.
/// </summary>
internal static class StackScaleHelper
{
    /// <summary>
    /// Contribute the scale range a stacked chart needs: the value axis must cover the largest
    /// per-category total, not the largest single value.
    /// </summary>
    /// <param name="stack">Stacking mode; <see cref="StackMode.None"/> returns without touching the scales.</param>
    /// <param name="scales">Scale set whose value scale is extended.</param>
    /// <param name="encodes">Encodings used to resolve the raw values of each row.</param>
    /// <param name="data">Rows to accumulate per category.</param>
    /// <param name="valueChannel">Channel holding the values (Y, or X for horizontal bars).</param>
    /// <param name="categoryChannel">Channel holding the categories (X, or Y for horizontal bars).</param>
    public static void ContributeStackedYScale(
        StackMode stack, ScaleSet scales, EncodeSet encodes, List<DataRow> data,
        Channel valueChannel = Channel.Y, Channel categoryChannel = Channel.X)
    {
        if (stack == StackMode.None || data.Count == 0) return;

        var totals = new Dictionary<string, double>();
        var negativeTotals = new Dictionary<string, double>();
        foreach (var row in data)
        {
            var key = encodes.Resolve(categoryChannel, row)?.ToString() ?? "";
            var valueRaw = encodes.Resolve(valueChannel, row);
            if (valueRaw is null || !ScaleConvert.TryToDouble(valueRaw, out double value)) continue;
            if (value < 0) negativeTotals[key] = negativeTotals.GetValueOrDefault(key, 0) + value;
            else totals[key] = totals.GetValueOrDefault(key, 0) + value;
        }

        if (stack == StackMode.Normalize)
        {
            scales.Set(valueChannel, new LinearScale(0, 1));
            return;
        }

        // A negative value does not shrink the stack, it grows it downwards: the axis has to cover the
        // largest positive and the largest negative stack, so signed summation would both clip a
        // mixed-sign stack and, for all-negative data, give a [0, 1] axis that draws nothing.
        double maxStacked = 0;
        foreach (var v in totals.Values)
            if (v > maxStacked) maxStacked = v;

        double minStacked = 0;
        foreach (var v in negativeTotals.Values)
            if (v < minStacked) minStacked = v;

        // 5% headroom so the tallest stack does not touch the plot border.
        double top = maxStacked > 0 ? maxStacked * 1.05 : 1;
        double bottom = minStacked < 0 ? minStacked * 1.05 : 0;
        scales.Set(valueChannel, new LinearScale(bottom, top));
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
            // A named-but-null field, a non-numeric value or a non-finite number is missing, not zero:
            // treating it as 0 would stretch the axis to the origin, and a NaN would poison every
            // coordinate that reads this scale.
            if (row.Has(maxField) && ScaleConvert.TryToDouble(row.Get(maxField), out double hi))
                maxVal = Math.Max(maxVal, hi);
            if (row.Has(minField) && ScaleConvert.TryToDouble(row.Get(minField), out double lo))
                minVal = Math.Min(minVal, lo);
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

    /// <summary>
    /// Widen (or create) the <paramref name="yChannel"/> linear scale so it covers the given value
    /// range. Marks whose geometry reaches beyond their samples use this - the violin density, for
    /// example, extends past the smallest and largest sample.
    /// </summary>
    public static void ContributeRange(
        ScaleSet scales, Channel yChannel, double min, double max, double padding = 0.0)
    {
        if (min > max) return;

        double pad = (max - min) * padding;
        double lo = min - pad;
        double hi = max + pad;

        if (scales.TryGet(yChannel) is LinearScale existing)
        {
            double newMin = Math.Min(existing.Min, lo);
            double newMax = Math.Max(existing.Max, hi);
            if (newMin < existing.Min || newMax > existing.Max)
                scales.Set(yChannel, new LinearScale(newMin, newMax));
        }
        else
        {
            scales.Set(yChannel, new LinearScale(lo, hi));
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
/// Display-level point reduction for a series: how a mark that draws a point per row copes with a table so
/// large that thousands of rows land in the same pixel column.
/// </summary>
public enum DecimateMode
{
    /// <summary>Draw every point. The path is as long as the table; use it to compare or to inspect.</summary>
    Off,

    /// <summary>Reduce only when the series has more points than the plot can resolve (the default).</summary>
    Auto,

    /// <summary>Always reduce, even when the plot could show every point.</summary>
    On,
}

/// <summary>
/// Step line mode for LineMark: how the path connects two points - with a straight segment, or with the
/// two half-steps that make a staircase.
/// </summary>
public enum StepMode
{
    /// <summary>No step, smooth or straight line segments.</summary>
    None,

    /// <summary>
    /// Step is drawn at the previous point: the vertical segment runs first (at the previous X), then
    /// the horizontal segment at the new Y.
    /// </summary>
    Before,

    /// <summary>
    /// Step is drawn at the next point: the horizontal segment runs first (at the previous Y), then the
    /// vertical segment at the new X.
    /// </summary>
    After,

    /// <summary>Step at midpoint between data points.</summary>
    Center,
}