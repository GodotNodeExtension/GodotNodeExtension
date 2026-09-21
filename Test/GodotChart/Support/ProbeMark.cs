namespace GodotNodeExtension.Tests.GodotChart.Support;

using System;
using System.Collections.Generic;
using Godot;
using GodotNodeExtension.Component.GodotChart;
using GodotNodeExtension.Component.GodotChart.Canvas;
using static GodotNodeExtension.Component.GodotChart.MarkCoordinate;

/// <summary>
/// Minimal <see cref="Mark"/> implementation that draws nothing.
/// It exposes the protected base-class field helpers so their contracts can be unit tested
/// without going through a concrete mark's rendering logic.
/// </summary>
public sealed class ProbeMark : Mark
{
    /// <inheritdoc />
    public override MarkCoordinate Coordinate => Cartesian;

    /// <inheritdoc />
    public override void Render(MarkContext ctx)
        => RenderedY = ctx.Data.Count > 0 ? ctx.Encodes.Resolve(Channel.Y, ctx.Data[0]) : null;

    /// <summary>
    /// Y value the chart handed to this mark on its last <see cref="Render"/> - the field the mark
    /// actually draws from, which is what a mark-level encode is supposed to change.
    /// </summary>
    public object? RenderedY { get; private set; }

    /// <summary>When set, returned by <see cref="HitTest"/> so a test can inspect the context it is given.</summary>
    public Func<MarkContext, HitResult?>? HitTestOverride { get; set; }

    /// <inheritdoc />
    public override HitResult? HitTest(MarkContext ctx, Vector2 screenPos) => HitTestOverride?.Invoke(ctx);

    /// <summary>Exposes <see cref="Mark.GetDouble"/>.</summary>
    public double ReadDouble(DataRow row, string field) => GetDouble(row, field);

    /// <summary>Exposes <see cref="Mark.ToDouble"/>.</summary>
    public double ConvertToDouble(object? value, string field) => ToDouble(value!, field);

    /// <summary>Exposes <see cref="Mark.ToSingle"/>.</summary>
    public float ConvertToSingle(object? value, string field) => ToSingle(value!, field);

    /// <summary>Exposes <see cref="Mark.HasField"/>.</summary>
    public bool HasOne(DataRow row, string field) => HasField(row, field);

    /// <summary>Exposes the five-argument <see cref="Mark.HasFields(DataRow, string, string, string, string, string)"/>.</summary>
    public bool HasAll(DataRow row, string a, string b, string c, string d, string e)
        => HasFields(row, a, b, c, d, e);

    /// <summary>Exposes <see cref="Mark.GetStringOrNull"/>.</summary>
    public string? ReadString(DataRow row, string field) => GetStringOrNull(row, field);

    /// <summary>Exposes the shared centred-text helper so its geometry can be asserted.</summary>
    public void DrawCentered(MarkContext ctx, IPaint2D paint, string text, float x, float y,
                             FontSettings font)
        => DrawTextCentered(ctx, paint, text, x, y, font);

    // ── Encode / channel resolution ──────────────────────────────────────────────

    /// <summary>Exposes <see cref="Mark.ResolveEncode"/> (local encode wins over the chart encode).</summary>
    public object? ReadEncode(MarkContext ctx, Channel channel, DataRow row)
        => ResolveEncode(ctx, channel, row);

    /// <summary>Exposes <see cref="Mark.HasEncode"/>.</summary>
    public bool EncodesChannel(MarkContext ctx, Channel channel) => HasEncode(ctx, channel);

    /// <summary>Exposes <see cref="Mark.ResolveY"/>.</summary>
    public object? ReadY(MarkContext ctx, DataRow row) => ResolveY(ctx, row);

    /// <summary>Exposes <see cref="Mark.GetYScale"/>.</summary>
    public IScale? YScaleOf(MarkContext ctx) => GetYScale(ctx);

    // ── Colour / opacity resolution ──────────────────────────────────────────────

    /// <summary>Exposes <see cref="Mark.ResolveColor"/>.</summary>
    public Color ReadColor(MarkContext ctx, DataRow row, Color fallback)
        => ResolveColor(ctx, row, fallback);

    /// <summary>Exposes <see cref="Mark.ResolveSeriesColor"/>.</summary>
    public Color ReadSeriesColor(MarkContext ctx, object seriesKey)
        => ResolveSeriesColor(ctx, seriesKey);

    /// <summary>Exposes <see cref="Mark.ResolveOpacity"/>.</summary>
    public float ReadOpacity(MarkContext ctx, DataRow row, float fallback = 1f)
        => ResolveOpacity(ctx, row, fallback);

    /// <summary>
    /// Exposes <see cref="Mark.ResolveFill"/>: the colour channel / mark default, then the
    /// <see cref="Mark.StyleOverride"/> callback and the hover state. Replaces the removed per-element
    /// colour override (<c>ResolveColorWithOverride</c>); the public name is kept because tests call it.
    /// </summary>
    public Color ReadColorWithOverride(MarkContext ctx, DataRow row, int index, Color fallback)
        => ResolveFill(ctx, row, index, fallback);

    /// <summary>
    /// Exposes <see cref="Mark.ComputeElementOpacity"/>: the resolved opacity folded with the animation
    /// opacity, the focus dimming and the <see cref="Mark.StyleOverride"/> callback. Replaces the removed
    /// per-element opacity override (<c>ResolveOpacityWithOverride</c>); the public name is kept.
    /// </summary>
    public float ReadOpacityWithOverride(MarkContext ctx, DataRow row, int index, float fallback = 1f)
        => ComputeElementOpacity(ctx, row, index, fallback);

    /// <summary>
    /// Exposes the fill half of <see cref="Mark.ResolveStyle"/> - the removed
    /// <c>ApplyColorOverride</c>, which is now just the style callback applied to a resolved colour.
    /// Kept under its old name for the contract tests.
    /// </summary>
    public Color OverrideColor(DataRow row, int index, Color resolved)
        => ResolveStyle(row, index, new ElementStyle(resolved, 1f)).Fill;

    /// <summary>
    /// Exposes the opacity half of <see cref="Mark.ResolveStyle"/> - the removed
    /// <c>ApplyOpacityOverride</c>, which is now just the style callback applied to a resolved opacity.
    /// Kept under its old name for the contract tests.
    /// </summary>
    public float OverrideOpacity(DataRow row, int index, float resolved)
        => ResolveStyle(row, index, new ElementStyle(default, resolved)).Opacity;

    /// <summary>Exposes <see cref="Mark.StateOf"/>: the interaction state the base class derives for one element.</summary>
    public ElementState ReadState(MarkContext ctx, DataRow row, int index) => StateOf(ctx, row, index);

    /// <summary>
    /// Exposes <see cref="Mark.ApplySelectionPaint"/> so a test can read back the selected ring style
    /// (colour and width) the base class would paint.
    /// </summary>
    public void ApplySelectionRing(MarkContext ctx, IPaint2D paint, float opacity = 1f)
        => ApplySelectionPaint(ctx, paint, opacity);

    /// <summary>Exposes <see cref="Mark.BrightenColor"/>.</summary>
    public Color Brighten(Color color, float factor) => BrightenColor(color, factor);

    /// <summary>Exposes <see cref="Mark.GetDefaultColor"/>.</summary>
    public Color DefaultColorOf(MarkContext ctx) => GetDefaultColor(ctx);

    /// <summary>Exposes <see cref="Mark.GetSelectionColor"/>.</summary>
    public Color SelectionColorOf(MarkContext ctx) => GetSelectionColor(ctx);

    /// <summary>Exposes <see cref="Mark.GetSelectionStrokeWidth"/>.</summary>
    public float SelectionStrokeWidthOf(MarkContext ctx) => GetSelectionStrokeWidth(ctx);

    /// <summary>Exposes <see cref="Mark.GetDataLabelColor"/>.</summary>
    public Color DataLabelColorOf(MarkContext ctx) => GetDataLabelColor(ctx);

    /// <summary>Exposes <see cref="Mark.GetHoverBrighten"/>.</summary>
    public float HoverBrightenOf(MarkContext ctx) => GetHoverBrighten(ctx);

    /// <summary>Exposes <see cref="Mark.GetHoverScale"/>.</summary>
    public float HoverScaleOf(MarkContext ctx) => GetHoverScale(ctx);

    /// <summary>Exposes <see cref="Mark.GetSegmentBorderColor"/>.</summary>
    public Color SegmentBorderColorOf(MarkContext ctx) => GetSegmentBorderColor(ctx);

    /// <summary>Exposes <see cref="Mark.GetSegmentBorderWidth"/>.</summary>
    public float SegmentBorderWidthOf(MarkContext ctx) => GetSegmentBorderWidth(ctx);

    /// <summary>Exposes <see cref="Mark.IsHoverExplodeEnabled"/>.</summary>
    public bool HoverExplodeEnabled(MarkContext ctx) => IsHoverExplodeEnabled(ctx);

    // ── Series / opacity math ────────────────────────────────────────────────────

    /// <summary>Exposes <see cref="Mark.ResolveSeriesKey"/>.</summary>
    public string? SeriesKeyOf(MarkContext ctx, DataRow row) => ResolveSeriesKey(ctx, row);

    /// <summary>Exposes <see cref="Mark.IsSeriesHidden"/>.</summary>
    public bool RowIsHidden(MarkContext ctx, DataRow row) => IsSeriesHidden(ctx, row);

    /// <summary>
    /// Exposes <see cref="Mark.ComputeElementOpacity"/> without a row index - the shape the removed
    /// <c>Mark.ComputeEffectiveOpacity</c> had (<c>index = -1</c> disables the style callback).
    /// </summary>
    public float EffectiveOpacity(MarkContext ctx, DataRow row, float baseOpacity = 1f)
        => ComputeElementOpacity(ctx, row, -1, baseOpacity);

    /// <summary>Exposes <see cref="Mark.ComputeSeriesOpacity"/>.</summary>
    public float SeriesOpacity(MarkContext ctx, string? seriesKey, float baseOpacity)
        => ComputeSeriesOpacity(ctx, seriesKey, baseOpacity);

    /// <summary>Exposes <see cref="Mark.ComputeAnimProgress"/>.</summary>
    public float AnimProgress(MarkContext ctx) => ComputeAnimProgress(ctx);

    // ── Grouping / labels ────────────────────────────────────────────────────────

    /// <summary>Exposes <see cref="Mark.GroupByChannel"/>.</summary>
    public Dictionary<object, List<DataRow>> Groups(MarkContext ctx, Channel channel)
        => GroupByChannel(ctx, channel);

    /// <summary>Exposes <see cref="Mark.CachedGroupByChannel"/>.</summary>
    public Dictionary<object, List<DataRow>> CachedGroups(MarkContext ctx, Channel channel)
        => CachedGroupByChannel(ctx, channel);

    /// <summary>Exposes <see cref="Mark.FormatLabel"/>.</summary>
    public string Format(string format, object? yValue, object? xValue)
        => FormatLabel(format, yValue, xValue);

    /// <summary>Exposes the two-argument <see cref="Mark.HasFields(DataRow, string, string)"/>.</summary>
    public bool HasBoth(DataRow row, string a, string b) => HasFields(row, a, b);

    /// <summary>Exposes the four-argument overload.</summary>
    public bool HasFour(DataRow row, string a, string b, string c, string d)
        => HasFields(row, a, b, c, d);
}
