using System;
using System.Collections.Generic;
using Godot;
using GodotNodeExtension.Component.GodotChart.Canvas;

namespace GodotNodeExtension.Component.GodotChart;

// ── Renderer delegate ────────────────────────────────────────

/// <summary>
/// Delegate type for chart renderer functions.
/// Each renderer receives the full rendering context to perform its drawing.
/// </summary>
public delegate void ChartRenderer(RenderContext ctx);

// ── Render context ───────────────────────────────────────────

/// <summary>
/// Immutable rendering context passed to all renderer functions during Chart.Render().
/// Contains all information needed to draw any part of the chart.
/// </summary>
public class RenderContext
{
    /// <summary>The canvas to draw on.</summary>
    public required ICanvas2D Canvas { get; init; }

    /// <summary>The computed plot area rectangle.</summary>
    public required PlotArea Plot { get; init; }

    /// <summary>The active chart theme.</summary>
    public required ChartTheme Theme { get; init; }

    /// <summary>All resolved scales (X, Y, Color, etc.).</summary>
    public required ScaleSet Scales { get; init; }

    /// <summary>All resolved encodes.</summary>
    public required EncodeSet Encodes { get; init; }

    /// <summary>Render data (post-transform).</summary>
    public required IReadOnlyList<DataRow> Data { get; init; }

    /// <summary>Chart offset X for multi-chart layouts.</summary>
    public float OffsetX { get; init; }

    /// <summary>Chart offset Y for multi-chart layouts.</summary>
    public float OffsetY { get; init; }

    /// <summary>Total chart width.</summary>
    public float Width { get; init; }

    /// <summary>Total chart height.</summary>
    public float Height { get; init; }

    /// <summary>Chart title. Null if none.</summary>
    public string? Title { get; init; }

    /// <summary>Chart paddings.</summary>
    public float PaddingLeft { get; init; }

    /// <summary>Chart right padding.</summary>
    public float PaddingRight { get; init; }

    /// <summary>Chart top padding.</summary>
    public float PaddingTop { get; init; }

    /// <summary>Chart bottom padding.</summary>
    public float PaddingBottom { get; init; }

    /// <summary>Background color (theme-resolved).</summary>
    public Color BackgroundColor { get; init; }

    /// <summary>Grid color (theme-resolved).</summary>
    public Color GridColor { get; init; }

    /// <summary>Axis color (theme-resolved).</summary>
    public Color AxisColor { get; init; }

    /// <summary>Axis configurations (may be null).</summary>
    public AxisConfig? XAxisConfig { get; init; }

    /// <summary>Y axis configuration.</summary>
    public AxisConfig? YAxisConfig { get; init; }

    /// <summary>Secondary Y axis configuration.</summary>
    public AxisConfig? Y2AxisConfig { get; init; }

    /// <summary>Legend configuration (may be null).</summary>
    public LegendConfig? LegendConfig { get; init; }

    /// <summary>Current focused series key for legend highlight. Null = no focus.</summary>
    public string? FocusedSeries { get; init; }

    /// <summary>Hidden series set. Null = all visible.</summary>
    public IReadOnlySet<string>? HiddenSeries { get; init; }

    /// <summary>Mouse position for crosshair. Null = no crosshair.</summary>
    public Vector2? MousePos { get; init; }

    /// <summary>Whether to skip Cartesian decorations (grid, axes, crosshair) because no Cartesian marks exist.</summary>
    public bool SkipCartesianDecorations { get; init; }

    /// <summary>The color scale (for legend rendering). Null if not available.</summary>
    public ColorScale? ColorScale { get; init; }

    /// <summary>Pre-computed legend item layouts. Null if no legend or no color scale.</summary>
    internal IReadOnlyList<LegendItemLayout>? CachedLegendItems { get; init; }
}

// ── Legend layout helper ─────────────────────────────────────

/// <summary>
/// Layout data for a single legend item (position + bounds + key).
/// </summary>
internal readonly record struct LegendItemLayout(
    float X, float Y, float Width, float Height, string Key);

/// <summary>
/// Shared legend layout computation used by both DrawLegend and TestLegendHit.
/// </summary>
internal static class LegendLayoutHelper
{
    /// <summary>
    /// Compute legend item positions given plot area and legend config.
    /// When <paramref name="canvas"/> is provided, text widths are measured precisely
    /// via <see cref="ICanvas2D.MeasureText"/>; otherwise a character-count heuristic is used.
    /// Returns null if legend should not be rendered.
    /// </summary>
    public static List<LegendItemLayout>? Compute(
        PlotArea plot, LegendConfig cfg, ColorScale colorScale,
        float offsetX, float offsetWidth, ICanvas2D? canvas = null,
        ChartTheme? theme = null)
    {
        if (cfg.Position == LegendPosition.None) return null;
        var domain = colorScale.Domain;
        if (domain.Count == 0) return null;

        var font = FontSettings.Default;
        float swatchSize = cfg.SwatchSize;
        float textOffsetX = swatchSize + (theme?.LegendSwatchTextGap ?? 4f);
        float itemHeight = MathF.Max(swatchSize, font.Size * font.LineHeightMultiplier);
        float itemSpacing = cfg.ItemSpacing;
        bool horizontal = cfg.Position is LegendPosition.Top or LegendPosition.Bottom;

        // Pre-measure text widths (precise when canvas available, heuristic fallback)
        var textWidths = new float[domain.Count];
        for (int i = 0; i < domain.Count; i++)
        {
            textWidths[i] = canvas != null
                ? canvas.MeasureText(domain[i], font).Width
                : domain[i].Length * font.Size * 0.6f;
        }

        // Measure totals
        float totalWidth = 0f;
        float totalHeight;
        if (horizontal)
        {
            for (int i = 0; i < domain.Count; i++)
            {
                totalWidth += textOffsetX + textWidths[i];
                if (i < domain.Count - 1) totalWidth += itemSpacing;
            }
            totalHeight = itemHeight;
        }
        else
        {
            float maxTw = 0f;
            for (int i = 0; i < domain.Count; i++)
                maxTw = MathF.Max(maxTw, textWidths[i]);
            totalWidth = textOffsetX + maxTw;
            totalHeight = domain.Count * itemHeight + (domain.Count - 1) * (theme?.LegendVerticalItemSpacing ?? 4f);
        }

        // Determine start position
        float lx, ly;
        float legendBottomGap = theme?.LegendBottomGap ?? 10f;
        switch (cfg.Position)
        {
            case LegendPosition.Top:
                lx = plot.X + (plot.Width - totalWidth) * 0.5f;
                ly = plot.Y - cfg.Padding - itemHeight;
                break;
            case LegendPosition.Bottom:
                lx = plot.X + (plot.Width - totalWidth) * 0.5f;
                ly = plot.Y + plot.Height + cfg.Padding + font.Size * font.LineHeightMultiplier + legendBottomGap;
                break;
            case LegendPosition.Left:
                lx = offsetX + cfg.Padding;
                ly = plot.Y + (plot.Height - totalHeight) * 0.5f;
                break;
            default: // Right
                lx = plot.X + plot.Width + cfg.Padding + 40f;
                ly = plot.Y + (plot.Height - totalHeight) * 0.5f;
                break;
        }

        // Build item layouts
        var items = new List<LegendItemLayout>(domain.Count);
        float curX = lx;
        float curY = ly;
        for (int i = 0; i < domain.Count; i++)
        {
            string key = domain[i];
            float tw = textWidths[i];
            float itemWidth = textOffsetX + tw;
            items.Add(new LegendItemLayout(curX, curY, itemWidth, itemHeight, key));

            if (horizontal)
                curX += itemWidth + itemSpacing;
            else
                curY += itemHeight + (theme?.LegendVerticalItemSpacing ?? 4f);
        }

        return items;
    }
}

// ── Chart interaction event args ─────────────────────────────

/// <summary>
/// Event args for chart hover state changes.
/// </summary>
public class ChartHoverEventArgs : EventArgs
{
    /// <summary>Previously hovered row index. -1 if none.</summary>
    public int PreviousRowIndex { get; init; }

    /// <summary>Newly hovered row index. -1 if none.</summary>
    public int RowIndex { get; init; }

    /// <summary>The data row of the newly hovered element.</summary>
    public DataRow? Row { get; init; }

    /// <summary>The series key the hovered element belongs to.</summary>
    public string? SeriesKey { get; init; }

    /// <summary>The mark type that was hovered.</summary>
    public string? MarkType { get; init; }

    /// <summary>Screen position of the hovered element center.</summary>
    public Vector2 ScreenPosition { get; init; }
}

/// <summary>
/// Event args for chart focus series changes.
/// </summary>
public class ChartFocusEventArgs : EventArgs
{
    /// <summary>Previously focused series key. Null if none.</summary>
    public string? PreviousSeriesKey { get; init; }

    /// <summary>Newly focused series key. Null if cleared.</summary>
    public string? SeriesKey { get; init; }
}

/// <summary>
/// Event args for chart selection changes.
/// </summary>
public class ChartSelectionEventArgs : EventArgs
{
    /// <summary>Previously selected row index. -1 if none.</summary>
    public int PreviousRowIndex { get; init; }

    /// <summary>Newly selected row index. -1 if cleared.</summary>
    public int RowIndex { get; init; }

    /// <summary>The data row of the newly selected element.</summary>
    public DataRow? Row { get; init; }

    /// <summary>The series key the selected element belongs to.</summary>
    public string? SeriesKey { get; init; }
}

/// <summary>
/// Event args for legend item click. Set Handled to prevent default focus toggle.
/// </summary>
public class ChartLegendClickEventArgs : EventArgs
{
    /// <summary>The legend item series key that was clicked.</summary>
    public string SeriesKey { get; init; } = "";

    /// <summary>Current focused series before this click.</summary>
    public string? CurrentFocusedSeries { get; init; }

    /// <summary>
    /// Set to true in the event handler to prevent default focus toggle behavior.
    /// This allows external code to fully control focus logic.
    /// </summary>
    public bool Handled { get; set; }
}
