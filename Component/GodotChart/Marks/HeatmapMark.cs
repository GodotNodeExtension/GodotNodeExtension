using System;
using System.Collections.Generic;
using Godot;
using GodotNodeExtension.Component.GodotChart.Canvas;

namespace GodotNodeExtension.Component.GodotChart.Marks;

/// <summary>
/// Heatmap chart mark. Renders a color-coded matrix grid.
/// Encodes: X = column category (OrdinalScale), Y = row category (OrdinalScale),
/// Color = continuous value (SequentialColorScale).
/// Animation: cells fade in from zero opacity.
/// <para>
/// Rows follow the Y scale, which runs bottom-up: the first category of the Y domain sits at the
/// bottom of the plot, exactly where its axis label is, and the last one at the top. Cells are centred
/// on their category band and their <see cref="CellGap"/> is split between the neighbouring bands.
/// </para>
/// <para>
/// Cell labels are formatted with <see cref="Mark.LabelFormat"/> ({0} = the value, already formatted by
/// the color scale, {1} = the column category). The label sits in the middle of its cell, so
/// <see cref="Mark.LabelPosition"/> does not apply to this mark.
/// </para>
/// </summary>
public class HeatmapMark : Mark
{
    /// <inheritdoc />
    public override MarkCoordinate Coordinate => MarkCoordinate.Cartesian;

    /// <summary>Gap between cells in pixels.</summary>
    public float CellGap { get; set; } = 1f;

    /// <summary>
    /// Upper bound on the cells this mark draws: a heatmap draws one cell per row, so a 1000x1000 grid is a
    /// million cells and no frame budget survives that. Rows past the cap are not drawn and a warning says so
    /// once - aggregate the data (or raise the cap) instead of watching the page drop to single-digit fps. Hit
    /// testing still sees every row: the cap is about what gets painted, not about what the data is.
    /// </summary>
    public int MaxCells { get; set; } = 65_536;

    /// <summary>Corner radius for cells.</summary>
    public float CornerRadius { get; set; } = 3f;

    /// <summary>Whether to show the value label inside each cell.</summary>
    public override bool ShowLabel { get; set; }

    /// <summary>
    /// Size of one cell, shared by <see cref="Render"/> and <see cref="HitTest"/>. A plot area smaller
    /// than the gaps would give a negative size, which is clamped to 0 (an empty cell) instead.
    /// </summary>
    private (float cellW, float cellH) ComputeCellSize(MarkContext ctx, int cols, int rows)
    {
        float cellW = MathF.Max(0f, (ctx.Plot.Width  - CellGap * (cols - 1)) / cols);
        float cellH = MathF.Max(0f, (ctx.Plot.Height - CellGap * (rows - 1)) / rows);
        return (cellW, cellH);
    }

    /// <summary>
    /// Top-left corner of one cell, shared by <see cref="Render"/> and <see cref="HitTest"/>.
    /// <para>
    /// Columns advance left to right along the X scale. Rows follow the Y scale, which is drawn
    /// bottom-up: <see cref="OrdinalScale.Map"/> returns the middle of a category's band and
    /// <see cref="PlotArea.MapY"/> turns that into a screen position, so a cell is centred on its band
    /// (hence the half cell offset) and sits next to the same category's axis label.
    /// </para>
    /// </summary>
    /// <param name="ctx">Context holding the plot area.</param>
    /// <param name="yScale">Row scale the row category is mapped through.</param>
    /// <param name="yRaw">Raw row category value.</param>
    /// <param name="colIdx">Zero-based column index.</param>
    /// <param name="cellW">Cell width from <see cref="ComputeCellSize"/>.</param>
    /// <param name="cellH">Cell height from <see cref="ComputeCellSize"/>.</param>
    private (float x, float y) CellOrigin(MarkContext ctx, OrdinalScale yScale, object yRaw,
                                          int colIdx, float cellW, float cellH)
    {
        float px = ctx.Plot.X + colIdx * (cellW + CellGap);
        float py = ctx.Plot.MapY(yScale.Map(yRaw)) - cellH / 2f;
        return (px, py);
    }

    /// <inheritdoc />
    public override void ContributeScales(
        ScaleSet scales, EncodeSet encodes, List<DataRow> data)
    {
        // A heatmap needs a continuous color scale. Respect a scale the caller provided
        // (sequential, diverging or custom); replace only non-color scales and the categorical
        // fallback that auto-fit installs for the Color channel.
        if (encodes.Has(Channel.Color) && !HasContinuousColorScale(scales))
        {
            var values = new List<object>();
            foreach (var row in data)
            {
                var v = encodes.Resolve(Channel.Color, row);
                // Only numeric values can define a ramp: a null, a colour string or a non-finite
                // number would throw in Fit or poison the domain, so they are filtered out here
                // (Fit applies the same rule again for anything that reaches it).
                if (ScaleConvert.TryToDouble(v, out _)) values.Add(v!);
            }
            if (values.Count > 0)
            {
                var seqScale = new SequentialColorScale();
                seqScale.Fit(values);
                scales.Set(Channel.Color, seqScale);
            }
        }
    }

    /// <summary>
    /// True when the Color channel already holds a continuous color scale
    /// (sequential/diverging/custom), i.e. one that is not the categorical fallback.
    /// </summary>
    private static bool HasContinuousColorScale(ScaleSet scales)
        => scales.TryGet(Channel.Color) is IColorScale and not ICategoricalColorScale;

    /// <summary>
    /// Draw one heatmap cell: its rectangle, the selection stroke and (when
    /// <see cref="Mark.ShowLabel"/> is on) the value label in its middle. The fill the caller passes in is
    /// the one the pass owns: the data layer hands over what <see cref="Mark.ResolveFill"/> answered (the
    /// hover look while it owns the state), and the overlay pass hands over
    /// <see cref="Mark.ActiveFillOf"/> for the hovered cell.
    /// <para>
    /// The label is part of this helper because it sits <i>inside</i> the cell: an overlay pass that
    /// repainted the cell without it would erase the text the cached layer holds.
    /// </para>
    /// </summary>
    /// <param name="ctx">Context of the frame.</param>
    /// <param name="xRaw">Raw column category of the cell.</param>
    /// <param name="cRaw">Raw colour value of the cell.</param>
    /// <param name="colorScale">Colour scale of the cell's value (used to format the label).</param>
    /// <param name="px">Left edge of the cell.</param>
    /// <param name="py">Top edge of the cell.</param>
    /// <param name="cellW">Cell width.</param>
    /// <param name="cellH">Cell height.</param>
    /// <param name="color">Fill of the cell.</param>
    /// <param name="opacity">Opacity of the cell.</param>
    /// <param name="selected">True when this is the selected cell (its ring is drawn here).</param>
    private void DrawCell(MarkContext ctx, object xRaw, object cRaw, IColorScale colorScale,
        float px, float py, float cellW, float cellH, Color color, float opacity, bool selected)
    {
        var path = ShapePath(ctx);
        var paint = ShapePaint(ctx);

        if (CornerRadius > 0)
            path.RoundRect(px, py, cellW, cellH, CornerRadius);
        else
            path.Rect(px, py, cellW, cellH);

        paint.SetColor(color).SetAntiAlias(true).SetOpacity(opacity);
        ctx.Canvas.Fill(path, paint);

        if (selected)
        {
            var strokePaint = ShapePaint(ctx);
            ApplySelectionPaint(ctx, strokePaint, opacity);
            ctx.Canvas.Stroke(path, strokePaint);
        }

        if (ShowLabel)
        {
            var labelPaint = ShapePaint(ctx);
            var dlc = GetDataLabelColor(ctx);
            labelPaint.SetColor(dlc with { A = dlc.A * opacity });
            // {0} = the value as the color scale formats it, {1} = the column category; the default
            // "{0}" keeps the previous cell text.
            string text = FormatLabel(LabelFormat, colorScale.Format(cRaw), xRaw);
            DrawTextCentered(ctx, labelPaint, text, px + cellW / 2f, py + cellH / 2f, FontSettings.Default);
        }
    }

    /// <summary>
    /// A heatmap cell is opaque, so its hover fill can be painted on the overlay pass without touching the
    /// cached layer - including the label in its middle, which the same helper redraws. The overlay only
    /// walks the interactive rows, and only while they are inside <see cref="MaxCells"/> (a row past the cap
    /// is not drawn by the data layer either).
    /// </summary>
    public override bool InteractionStateInOverlay => true;

    /// <summary>Whether the "rows past <see cref="MaxCells"/>" warning was already reported.</summary>
    private bool _warnedCap;

    /// <inheritdoc />
    public override void Render(MarkContext ctx)
    {
        var xScale = ctx.Scales.TryGet(Channel.X) as OrdinalScale;
        var yScale = ctx.Scales.TryGet(YChannel) as OrdinalScale;
        var colorScale = ctx.Scales.TryGet(Channel.Color) as IColorScale;
        if (xScale == null || yScale == null || colorScale == null) return;

        int cellLimit = Math.Min(ctx.Data.Count, MaxCells);
        if (ctx.Data.Count > MaxCells && !_warnedCap)
        {
            _warnedCap = true;
            GD.PushWarning(
                $"{nameof(HeatmapMark)}: {ctx.Data.Count} rows exceed MaxCells ({MaxCells}); drawing the first " +
                $"{MaxCells}. Aggregate the data or raise MaxCells - one cell per row is what a heatmap costs.");
        }

        // ContributeScales has no access to the theme, so the themed ramp is applied here — but only
        // while the scale still uses the built-in default (a caller-provided gradient wins). The
        // gradient is copied: the scale outlives this frame, and a theme edit (or a second heatmap)
        // must not be able to change the colours of a layout that is already drawn.
        if (colorScale is SequentialColorScale seqScale &&
            seqScale.Gradient.SequenceEqual(ChartTheme.DefaultSequentialGradient) &&
            ctx.Theme?.SequentialGradient is { Length: > 1 } themedGradient)
        {
            seqScale.Gradient = (Color[])themedGradient.Clone();
        }

        int cols = xScale.Domain.Count;
        int rows = yScale.Domain.Count;
        if (cols == 0 || rows == 0) return;

        var (cellW, cellH) = ComputeCellSize(ctx, cols, rows);
        float anim  = ComputeAnimProgress(ctx);

        // The interaction state stays in the data layer only while the chart does not cache it; with the
        // cache on the overlay paints it (see InteractionStateInOverlay / RenderOverlay).
        bool stateHere = !ctx.StateInOverlay;

        for (int i = 0; i < cellLimit; i++)
        {
            if (!TryCellGeometry(ctx, xScale, yScale, i, cellW, cellH, out var geometry)) continue;

            var color = ResolveFill(ctx, geometry.Row, i, colorScale.MapColor(geometry.ColorRaw));
            float opacity = anim * ComputeElementOpacity(ctx, geometry.Row, i);

            DrawCell(ctx, geometry.XRaw, geometry.ColorRaw, colorScale, geometry.Px, geometry.Py,
                cellW, cellH, color, opacity, selected: stateHere && i == ctx.SelectedRowIndex);
        }
    }

    /// <summary>Screen geometry of one heatmap cell: its row, its raw category/colour values and its place.</summary>
    /// <param name="Row">The data row of the cell.</param>
    /// <param name="XRaw">Raw column category.</param>
    /// <param name="ColorRaw">Raw colour value (on the ramp).</param>
    /// <param name="Px">Left edge of the cell.</param>
    /// <param name="Py">Top edge of the cell.</param>
    private readonly record struct RowGeometry(DataRow Row, object XRaw, object ColorRaw, float Px, float Py);

    /// <summary>
    /// Screen geometry of one cell, or <c>false</c> when the row cannot be drawn: hidden, missing one of
    /// the three encoded fields, carrying a colour value that is not a finite number (it has no place on
    /// the ramp), or standing on a category pair the scales do not know.
    /// </summary>
    /// <param name="ctx">Context of the frame.</param>
    /// <param name="xScale">Column scale of the grid.</param>
    /// <param name="yScale">Row scale of the grid.</param>
    /// <param name="index">Index of the row in the rendered data.</param>
    /// <param name="cellW">Cell width.</param>
    /// <param name="cellH">Cell height.</param>
    /// <param name="geometry">Geometry of the cell when the method returns true.</param>
    private bool TryCellGeometry(MarkContext ctx, OrdinalScale xScale, OrdinalScale yScale,
        int index, float cellW, float cellH, out RowGeometry geometry)
    {
        geometry = default;
        if (index < 0 || index >= ctx.Data.Count) return false;

        var row   = ctx.Data[index];
        if (IsSeriesHidden(ctx, row)) return false;
        var xRaw  = ctx.Encodes.Resolve(Channel.X, row);
        var yRaw  = ctx.Encodes.Resolve(YChannel, row);
        var cRaw  = ctx.Encodes.Resolve(Channel.Color, row);
        if (xRaw == null || yRaw == null || cRaw == null) return false;
        // Only a numeric, finite colour value has a place on the ramp: a NaN or +Inf cell used to be
        // painted with the middle colour of the gradient and labelled "NaN".
        if (!ScaleConvert.TryToDouble(cRaw, out _)) return false;

        int colIdx = xScale.IndexOf(xRaw.ToString()!);
        int rowIdx = yScale.IndexOf(yRaw.ToString()!);
        if (colIdx < 0 || rowIdx < 0) return false;

        // Cell position, shared with HitTest (see CellOrigin): the row follows the Y scale.
        var (px, py) = CellOrigin(ctx, yScale, yRaw, colIdx, cellW, cellH);
        geometry = new RowGeometry(row, xRaw, cRaw, px, py);
        return true;
    }

    /// <inheritdoc />
    public override void RenderOverlay(MarkContext ctx)
    {
        // Only while the chart keeps the data layer in an image: with the cache off Render painted the state.
        if (!ctx.StateInOverlay) return;

        int hovered = ctx.HoveredRowIndex;
        int selected = ctx.SelectedRowIndex;
        if (hovered < 0 && selected < 0) return;

        var xScale = ctx.Scales.TryGet(Channel.X) as OrdinalScale;
        var yScale = ctx.Scales.TryGet(YChannel) as OrdinalScale;
        var colorScale = ctx.Scales.TryGet(Channel.Color) as IColorScale;
        if (xScale == null || yScale == null || colorScale == null) return;

        int cols = xScale.Domain.Count;
        int rows = yScale.Domain.Count;
        if (cols == 0 || rows == 0) return;

        // A row past MaxCells is not drawn by the data layer, so the overlay must not highlight it either.
        int cellLimit = Math.Min(ctx.Data.Count, MaxCells);

        var (cellW, cellH) = ComputeCellSize(ctx, cols, rows);
        float anim = ComputeAnimProgress(ctx);

        // Only the interactive cells are drawn, from the same geometry the data layer used.
        if (hovered >= 0 && hovered < cellLimit)
            DrawInteractive(ctx, xScale, yScale, colorScale, hovered, cellW, cellH, anim, hovered: true);
        if (selected >= 0 && selected != hovered && selected < cellLimit)
            DrawInteractive(ctx, xScale, yScale, colorScale, selected, cellW, cellH, anim, hovered: false);
    }

    /// <summary>
    /// Draw one interactive cell on the overlay pass: the hovered one takes the active fill (the data layer
    /// answered the default one while the state lives here) and the selected one gets its ring.
    /// </summary>
    private void DrawInteractive(MarkContext ctx, OrdinalScale xScale, OrdinalScale yScale,
        IColorScale colorScale, int index, float cellW, float cellH, float anim, bool hovered)
    {
        if (!TryCellGeometry(ctx, xScale, yScale, index, cellW, cellH, out var geometry)) return;

        var color = ResolveFill(ctx, geometry.Row, index, colorScale.MapColor(geometry.ColorRaw));
        if (hovered) color = ActiveFillOf(ctx, color);
        float opacity = anim * ComputeElementOpacity(ctx, geometry.Row, index);

        DrawCell(ctx, geometry.XRaw, geometry.ColorRaw, colorScale, geometry.Px, geometry.Py,
            cellW, cellH, color, opacity, selected: index == ctx.SelectedRowIndex);
    }

    /// <inheritdoc />
    public override HitResult? HitTest(MarkContext ctx, Vector2 screenPos)
    {
        var xScale = ctx.Scales.TryGet(Channel.X) as OrdinalScale;
        var yScale = ctx.Scales.TryGet(YChannel) as OrdinalScale;
        if (xScale == null || yScale == null) return null;

        int cols = xScale.Domain.Count;
        int rows = yScale.Domain.Count;
        if (cols == 0 || rows == 0) return null;

        var (cellW, cellH) = ComputeCellSize(ctx, cols, rows);

        for (int i = 0; i < ctx.Data.Count; i++)
        {
            var row  = ctx.Data[i];
            if (IsSeriesHidden(ctx, row)) continue;
            var xRaw = ctx.Encodes.Resolve(Channel.X, row);
            var yRaw = ctx.Encodes.Resolve(YChannel, row);
            var cRaw = ctx.Encodes.Resolve(Channel.Color, row);
            // Mirror Render: a cell whose colour value is not a finite number is not drawn, so it must
            // not be hittable either.
            if (xRaw == null || yRaw == null || cRaw == null) continue;
            if (!ScaleConvert.TryToDouble(cRaw, out _)) continue;

            int colIdx = xScale.IndexOf(xRaw.ToString()!);
            int rowIdx = yScale.IndexOf(yRaw.ToString()!);
            if (colIdx < 0 || rowIdx < 0) continue;

            // Same geometry as Render (see CellOrigin).
            var (px, py) = CellOrigin(ctx, yScale, yRaw, colIdx, cellW, cellH);

            if (screenPos.X >= px && screenPos.X <= px + cellW &&
                screenPos.Y >= py && screenPos.Y <= py + cellH)
            {
                return new HitResult
                {
                    Hit = true, Row = row, RowIndex = i,
                    // Report the cell centre on both axes, matching Render's label anchor
                    // (px + cellW / 2, py + cellH / 2).
                    ScreenX = px + cellW / 2f, ScreenY = py + cellH / 2f,
                    Label = $"{xRaw}/{yRaw}: {cRaw}",
                    MarkType = nameof(HeatmapMark),
                };
            }
        }
        return null;
    }
}
