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
/// Rendering context passed to all renderer functions during <c>Chart.Render()</c>.
/// It is a read-only <b>view</b> of the chart state (the data, scales and encodes are the live
/// objects), and the instance itself is reused between frames.
/// </summary>
public class RenderContext
{
    // The reference-type properties below are assigned by the chart render pipeline through their
    // internal setters before any renderer reads them, so they are never null at use time.
    /// <summary>The canvas to draw on.</summary>
    public ICanvas2D Canvas { get; internal set; } = null!;

    /// <summary>The computed plot area rectangle.</summary>
    public PlotArea Plot { get; internal set; }

    /// <summary>The active chart theme.</summary>
    public ChartTheme Theme { get; internal set; } = null!;

    /// <summary>All resolved scales (X, Y, Color, etc.).</summary>
    public ScaleSet Scales { get; internal set; } = null!;

    /// <summary>All resolved encodes.</summary>
    public EncodeSet Encodes { get; internal set; } = null!;

    /// <summary>Render data (post-transform).</summary>
    public IReadOnlyList<DataRow> Data { get; internal set; } = null!;

    /// <summary>Chart offset X for multi-chart layouts.</summary>
    public float OffsetX { get; internal set; }

    /// <summary>Chart offset Y for multi-chart layouts.</summary>
    public float OffsetY { get; internal set; }

    /// <summary>Total chart width.</summary>
    public float Width { get; internal set; }

    /// <summary>Total chart height.</summary>
    public float Height { get; internal set; }

    /// <summary>Chart title. Null if none.</summary>
    public string? Title { get; internal set; }

    /// <summary>Chart left padding; horizontal inset reserving space for the Y-axis labels.</summary>
    public float PaddingLeft { get; internal set; }

    /// <summary>Chart right padding.</summary>
    public float PaddingRight { get; internal set; }

    /// <summary>Chart top padding.</summary>
    public float PaddingTop { get; internal set; }

    /// <summary>Chart bottom padding.</summary>
    public float PaddingBottom { get; internal set; }

    /// <summary>Background color (theme-resolved).</summary>
    public Color BackgroundColor { get; internal set; }

    /// <summary>Grid color (theme-resolved).</summary>
    public Color GridColor { get; internal set; }

    /// <summary>Axis color (theme-resolved).</summary>
    public Color AxisColor { get; internal set; }

    /// <summary>Axis configurations (may be null).</summary>
    public AxisConfig? XAxisConfig { get; internal set; }

    /// <summary>Y axis configuration.</summary>
    public AxisConfig? YAxisConfig { get; internal set; }

    /// <summary>Secondary Y axis configuration.</summary>
    public AxisConfig? Y2AxisConfig { get; internal set; }

    /// <summary>Legend configuration (may be null).</summary>
    public LegendConfig? LegendConfig { get; internal set; }

    /// <summary>Current focused series key for legend highlight. Null = no focus.</summary>
    public string? FocusedSeries { get; internal set; }

    /// <summary>Hidden series set. Null = all visible.</summary>
    public IReadOnlySet<string>? HiddenSeries { get; internal set; }

    /// <summary>Mouse position for crosshair. Null = no crosshair.</summary>
    public Vector2? MousePos { get; internal set; }

    /// <summary>The categorical color scale (for legend rendering). Null if not available.</summary>
    public ICategoricalColorScale? ColorScale { get; internal set; }

    /// <summary>
    /// The plot area before <see cref="Chart.PlotAspectRatio"/> shaped the content: the rectangle the
    /// decorations (title, legend, axis labels and their titles) are laid out against, while
    /// <see cref="Plot"/> is the box the marks and the grid were given. The two are the same rectangle while
    /// nothing shapes the content.
    /// </summary>
    public PlotArea FullPlot { get; internal set; }

    /// <summary>
    /// The rectangle the chart actually used this frame, decorations included: the same value
    /// <see cref="Chart.DrawnBounds"/> reports, so a renderer that takes over a stage can see where the rest of
    /// the chart sits without measuring it again. Null before the first frame.
    /// </summary>
    public Rect2? DrawnBounds { get; internal set; }

    /// <summary>
    /// Pre-computed legend geometry - the item rectangles the legend renderer draws with, and the box they
    /// occupy. Null when the chart has no legend, no colour scale, or no legend renderer to draw with; a custom
    /// renderer can lay the legend out itself with <see cref="LegendLayoutHelper.Compute"/> in that case.
    /// </summary>
    public LegendLayout? LegendLayout { get; internal set; }

    /// <summary>
    /// Tick sets already built for this context, keyed by channel. The grid pass and the label pass ask for the
    /// same axes, and building a ladder and formatting every entry is the expensive part of a frame with a long
    /// axis; a renderer that wants the ticks a third time gets them for free too.
    /// <para>
    /// The cache belongs to one <see cref="Plot"/> and one set of axes: the chart clears it whenever it rebuilds
    /// the context (see <c>Chart.BuildRenderContext</c>), so the entries never outlive the layout they were
    /// computed from. It is emptied, never reallocated, so a frame that asks twice still allocates nothing here.
    /// </para>
    /// </summary>
    internal Dictionary<Channel, List<(double Norm, string Text)>> TickCache { get; } = [];
}

// ── Legend layout helper ─────────────────────────────────────

/// <summary>
/// Layout of one legend item: where its swatch and its text sit, and the key it stands for.
/// </summary>
/// <param name="X">Left edge of the item (the swatch).</param>
/// <param name="Y">Top edge of the item.</param>
/// <param name="Width">Width the item occupies, swatch and gap and text included.</param>
/// <param name="Height">Height of the item's row.</param>
/// <param name="Key">Category (legend entry) the item stands for.</param>
public readonly record struct LegendItemLayout(
    float X, float Y, float Width, float Height, string Key);

/// <summary>
/// Computed legend geometry: the positioned items plus the total space they occupy.
/// The height is reported so the plot can reserve exactly as much room as the legend needs
/// (a horizontal legend wraps into several rows when the series do not fit into one).
/// </summary>
/// <param name="Items">The positioned items, in domain order.</param>
/// <param name="Width">Width of the widest row (a horizontal legend) or of the column (a vertical one).</param>
/// <param name="Height">Total height of the rows.</param>
public readonly record struct LegendLayout(
    IReadOnlyList<LegendItemLayout> Items, float Width, float Height);

/// <summary>
/// Shared legend layout computation used by both DrawLegend and TestLegendHit.
/// </summary>
internal static class LegendLayoutHelper
{
    /// <summary>
    /// Width a vertical (left/right) legend needs: the swatch column plus the widest label, so the
    /// plot can reserve horizontal space before it is computed. The label font is the theme's - the
    /// same <see cref="FontSettings"/> the legend renderer draws with - so a theme with a larger label
    /// font or another family reserves the space the text actually needs.
    /// </summary>
    public static float EstimateWidth(
        LegendConfig cfg, ICategoricalColorScale colorScale, ICanvas2D? canvas,
        ChartTheme? theme = null)
    {
        var font = LegendFont(theme);
        float widest = 0f;
        foreach (var key in colorScale.Domain)
        {
            float w = canvas != null
                ? canvas.MeasureText(key, font).Width
                : key.Length * font.Size * (theme ?? ChartTheme.Default).LegendTextWidthFallbackRatio;
            if (w > widest) widest = w;
        }
        return cfg.SwatchSize + font.Size * 0.4f + widest;
    }

    /// <summary>
    /// The font the legend labels are drawn with, mirroring <c>DefaultRenderers.ThemedFont</c>: the
    /// theme's label size (falling back to the default when it is not positive), family and resource.
    /// Measuring with <see cref="FontSettings.Default"/> while drawing with the theme's font made the
    /// items overlap as soon as a theme changed either.
    /// </summary>
    private static FontSettings LegendFont(ChartTheme? theme)
    {
        var effective = theme ?? ChartTheme.Default;
        return new FontSettings
        {
            Size = effective.LabelFontSize > 0f ? effective.LabelFontSize : FontSettings.Default.Size,
            Family = effective.FontFamily,
            GodotFont = effective.Font,
            Align = TextAlign.Left,
        };
    }

    /// <summary>
    /// Compute legend item positions given plot area and legend config.
    /// When <paramref name="canvas"/> is provided, text widths are measured precisely
    /// via <see cref="ICanvas2D.MeasureText"/>; otherwise a character-count heuristic is used.
    /// Returns null if legend should not be rendered.
    /// </summary>
    public static LegendLayout? Compute(
        PlotArea plot, LegendConfig cfg, ICategoricalColorScale colorScale,
        float offsetX, ICanvas2D? canvas = null,
        ChartTheme? theme = null)
    {
        if (cfg.Position == LegendPosition.None) return null;
        var domain = colorScale.Domain;
        if (domain.Count == 0) return null;

        // Legend spacing and layout fall back to the theme's own initializers when the caller has no
        // theme to pass, so an unthemed legend and a ChartTheme.Default one lay out identically.
        var legendTheme = theme ?? ChartTheme.Default;

        // The metrics come from the same font the renderer draws the labels with, so a themed label
        // size or family cannot make the items overlap or the rows too short.
        var font = LegendFont(theme);
        float swatchSize = cfg.SwatchSize;
        float textOffsetX = swatchSize + legendTheme.LegendSwatchTextGap;
        float itemHeight = MathF.Max(swatchSize, font.Size * font.LineHeightMultiplier);
        float itemSpacing = cfg.ItemSpacing;
        float rowSpacing = legendTheme.LegendVerticalItemSpacing;
        bool horizontal = cfg.Position is LegendPosition.Top or LegendPosition.Bottom;

        // Pre-measure text widths (precise when canvas available, heuristic fallback)
        var textWidths = new float[domain.Count];
        var itemWidths = new float[domain.Count];
        for (int i = 0; i < domain.Count; i++)
        {
            textWidths[i] = canvas != null
                ? canvas.MeasureText(domain[i], font).Width
                : domain[i].Length * font.Size * legendTheme.LegendTextWidthFallbackRatio;
            itemWidths[i] = textOffsetX + textWidths[i];
        }

        // Split the items into rows for horizontal legends: a legend must never run off the plot.
        var rows = new List<(int Start, int End, float Width)>();
        float totalWidth;
        float totalHeight;
        if (horizontal)
        {
            float widestItem = 0f;
            for (int i = 0; i < itemWidths.Length; i++)
                widestItem = MathF.Max(widestItem, itemWidths[i]);
            float available = MathF.Max(plot.Width, widestItem);
            int rowStart = 0;
            float rowWidth = 0f;
            float widestRow = 0f;
            for (int i = 0; i < domain.Count; i++)
            {
                float add = i == rowStart ? itemWidths[i] : itemSpacing + itemWidths[i];
                if (i > rowStart && rowWidth + add > available)
                {
                    rows.Add((rowStart, i, rowWidth));
                    widestRow = MathF.Max(widestRow, rowWidth);
                    rowStart = i;
                    add = itemWidths[i];
                }
                rowWidth = i == rowStart ? add : rowWidth + add;
            }
            rows.Add((rowStart, domain.Count, rowWidth));
            widestRow = MathF.Max(widestRow, rowWidth);

            totalWidth = widestRow;
            totalHeight = rows.Count * itemHeight + (rows.Count - 1) * rowSpacing;
        }
        else
        {
            float maxTw = 0f;
            for (int i = 0; i < domain.Count; i++)
                maxTw = MathF.Max(maxTw, textWidths[i]);
            totalWidth = textOffsetX + maxTw;
            totalHeight = domain.Count * itemHeight + (domain.Count - 1) * rowSpacing;
        }

        // Determine the first row position
        float firstRowX, firstRowY;
        float legendBottomGap = legendTheme.LegendBottomGap;
        switch (cfg.Position)
        {
            case LegendPosition.Top:
                firstRowX = plot.X;
                firstRowY = plot.Y - cfg.Padding - totalHeight;
                break;
            case LegendPosition.Bottom:
                firstRowX = plot.X;
                firstRowY = plot.Y + plot.Height + cfg.Padding + font.Size * font.LineHeightMultiplier + legendBottomGap;
                break;
            case LegendPosition.Left:
                firstRowX = offsetX + cfg.Padding;
                firstRowY = plot.Y + (plot.Height - totalHeight) * 0.5f;
                break;
            default: // Right
                firstRowX = plot.X + plot.Width + cfg.Padding + legendTheme.LegendRightOffset;
                firstRowY = plot.Y + (plot.Height - totalHeight) * 0.5f;
                break;
        }

        // Build item layouts (horizontal legends centre each row inside the plot area)
        var items = new List<LegendItemLayout>(domain.Count);
        if (horizontal)
        {
            for (int r = 0; r < rows.Count; r++)
            {
                var (start, end, width) = rows[r];
                float x = plot.X + (plot.Width - width) * 0.5f;
                float y = firstRowY + r * (itemHeight + rowSpacing);
                for (int i = start; i < end; i++)
                {
                    items.Add(new LegendItemLayout(x, y, itemWidths[i], itemHeight, domain[i]));
                    x += itemWidths[i] + itemSpacing;
                }
            }
        }
        else
        {
            float x = firstRowX;
            float y = firstRowY;
            for (int i = 0; i < domain.Count; i++)
            {
                items.Add(new LegendItemLayout(x, y, itemWidths[i], itemHeight, domain[i]));
                y += itemHeight + rowSpacing;
            }
        }

        return new LegendLayout(items, totalWidth, totalHeight);
    }
}

// ── Chart interaction event args ─────────────────────────────

/// <summary>
/// Event args for chart hover state changes.
/// </summary>
public class ChartHoverEventArgs : EventArgs
{
    /// <summary>Previously hovered row index. -1 if none.</summary>
    public int PreviousRowIndex { get; internal init; }

    /// <summary>Newly hovered row index. -1 if none.</summary>
    public int RowIndex { get; internal init; }

    /// <summary>The data row of the newly hovered element.</summary>
    public DataRow? Row { get; internal init; }

    /// <summary>The series key the hovered element belongs to.</summary>
    public string? SeriesKey { get; internal init; }

    /// <summary>The mark type that was hovered.</summary>
    public string? MarkType { get; internal init; }

    /// <summary>Screen position of the hovered element center.</summary>
    public Vector2 ScreenPosition { get; internal init; }
}

/// <summary>
/// Event args for chart focus series changes.
/// </summary>
public class ChartFocusEventArgs : EventArgs
{
    /// <summary>Previously focused series key. Null if none.</summary>
    public string? PreviousSeriesKey { get; internal init; }

    /// <summary>Newly focused series key. Null if cleared.</summary>
    public string? SeriesKey { get; internal init; }
}

/// <summary>
/// Event args for chart selection changes.
/// </summary>
public class ChartSelectionEventArgs : EventArgs
{
    /// <summary>Previously selected row index. -1 if none.</summary>
    public int PreviousRowIndex { get; internal init; }

    /// <summary>Newly selected row index. -1 if cleared.</summary>
    public int RowIndex { get; internal init; }

    /// <summary>The data row of the newly selected element.</summary>
    public DataRow? Row { get; internal init; }

    /// <summary>The series key the selected element belongs to.</summary>
    public string? SeriesKey { get; internal init; }
}

/// <summary>
/// Event args for legend item click. Set Handled to prevent default focus toggle.
/// </summary>
public class ChartLegendClickEventArgs : EventArgs
{
    /// <summary>The legend item series key that was clicked.</summary>
    public string SeriesKey { get; internal init; } = "";

    /// <summary>Current focused series before this click.</summary>
    public string? CurrentFocusedSeries { get; internal init; }

    /// <summary>Mouse button that produced the click; only <see cref="MouseButton.Left"/> toggles the focus.</summary>
    public MouseButton Button { get; internal set; } = MouseButton.Left;

    /// <summary>
    /// Set to true in the event handler to prevent default focus toggle behavior.
    /// This allows external code to fully control focus logic.
    /// </summary>
    public bool Handled { get; set; }
}
