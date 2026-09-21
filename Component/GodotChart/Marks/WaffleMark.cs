using System;
using System.Collections.Generic;
using Godot;

namespace GodotNodeExtension.Component.GodotChart.Marks;

/// <summary>
/// Waffle chart mark. Displays proportions as a grid of colored cells.
/// Each cell represents a unit of the total, colored by category.
/// </summary>
public class WaffleMark : Mark
{
    /// <inheritdoc />
    public override MarkCoordinate Coordinate => MarkCoordinate.Cartesian;

    /// <inheritdoc />
    /// <remarks>
    /// A waffle needs no axes: it fills a grid inside the plot rectangle and never maps a value through
    /// a scale, so the chart leaves the axis and grid decorations out when this is the only mark.
    /// </remarks>
    public override bool UsesAxes => false;

    private int _totalCells = 100;
    private int _columns = 10;
    private float _cellGap = 2f;

    /// <summary>Total number of cells in the grid. Default 100.</summary>
    public int TotalCells
    {
        get => _totalCells;
        set { _totalCells = Math.Max(1, value); InvalidateCellCache(); }
    }

    /// <summary>Number of columns in the waffle grid. Default 10.</summary>
    public int Columns
    {
        get => _columns;
        set { _columns = Math.Max(1, value); InvalidateCellCache(); }
    }

    /// <summary>Gap between cells in pixels. Default 2.</summary>
    public float CellGap
    {
        get => _cellGap;
        set { _cellGap = MathF.Max(0f, value); InvalidateCellCache(); }
    }

    /// <summary>Corner radius of each cell. Default 2.</summary>
    public float CellRadius { get; set; } = 2f;

    // Cached layout — keyed on the owning chart, the layout version and the data list, plus the
    // grid properties above (their setters invalidate explicitly).
    private List<WaffleCellInfo>? _cachedCells;
    private LayoutCacheKey? _cacheKey;

    /// <summary>Number of times the cell layout was rebuilt — used by tests to verify caching.</summary>
    internal int LayoutBuildCount { get; private set; }

    /// <summary>Drop the cached cell layout (called when a layout property changes).</summary>
    private void InvalidateCellCache() { _cachedCells = null; _cacheKey = null; }

    private struct WaffleCellInfo
    {
        public int RowIndex;
        public int CellCol;
        public int CellRow;
        public Color Color;
    }

    /// <summary>
    /// Grid geometry shared by <see cref="Render"/> and <see cref="HitTest"/>: the number of rows the
    /// placed cells need, the side of one cell and the offset of the grid inside the plot area.
    /// <para>
    /// The row count comes from the cells that were actually placed, so a grid that does not fill
    /// <see cref="TotalCells"/> cells does not reserve a row of empty space. The cell side is clamped to
    /// 0: a plot area narrower than the gaps (or <c>Width == 0</c>) would otherwise produce cells of a
    /// negative size.
    /// </para>
    /// </summary>
    /// <param name="ctx">Context holding the plot area.</param>
    /// <param name="cellCount">Number of cells that will be drawn.</param>
    private (int rows, float cellSize, float offsetX, float offsetY) GridGeometry(MarkContext ctx, int cellCount)
    {
        int rows = Math.Max(1, (int)Math.Ceiling((double)Math.Max(cellCount, 1) / Columns));
        float cellW = MathF.Max(0f, (ctx.Plot.Width - CellGap * (Columns - 1)) / Columns);
        float cellH = MathF.Max(0f, (ctx.Plot.Height - CellGap * (rows - 1)) / rows);
        float cellSize = MathF.Max(0f, MathF.Min(cellW, cellH));

        // Center the grid in the plot area.
        float gridW = Columns * cellSize + (Columns - 1) * CellGap;
        float gridH = rows * cellSize + (rows - 1) * CellGap;
        return (rows, cellSize,
                ctx.Plot.X + (ctx.Plot.Width - gridW) * 0.5f,
                ctx.Plot.Y + (ctx.Plot.Height - gridH) * 0.5f);
    }

    /// <summary>
    /// Drawn square of one cell, shared by <see cref="Render"/> and <see cref="HitTest"/>: the cell box
    /// shrunk toward its centre by the staggered entry animation, so a hit cannot land on a part of the
    /// cell that is not drawn yet (the top-left corner of the grid starts growing first).
    /// </summary>
    /// <param name="cell">Cell the square is computed for.</param>
    /// <param name="rows">Row count from <see cref="GridGeometry"/>.</param>
    /// <param name="cellSize">Cell side from <see cref="GridGeometry"/>.</param>
    /// <param name="offsetX">Grid origin on the X axis.</param>
    /// <param name="offsetY">Grid origin on the Y axis.</param>
    /// <param name="anim">Entry animation progress.</param>
    private (float x, float y, float size) CellSquare(
        WaffleCellInfo cell, int rows, float cellSize, float offsetX, float offsetY, float anim)
    {
        float gridH = rows * cellSize + (rows - 1) * CellGap;
        float px = offsetX + cell.CellCol * (cellSize + CellGap);
        // Render bottom-to-top (row 0 at bottom)
        float py = offsetY + gridH - (cell.CellRow + 1) * cellSize - cell.CellRow * CellGap;

        // Animation: scale from 0 to 1 with stagger
        int cellIndex = cell.CellRow * Columns + cell.CellCol;
        float stagger = (float)cellIndex / TotalCells;
        float cellAnim = Math.Clamp((anim - stagger * 0.5f) / 0.5f, 0f, 1f);

        float drawSize = cellSize * cellAnim;
        float drawOffset = (cellSize - drawSize) * 0.5f;
        return (px + drawOffset, py + drawOffset, drawSize);
    }

    /// <summary>
    /// Draw one waffle cell: its square, plus the selection stroke when the cell belongs to the selected
    /// row. The fill the caller passes in is the one the pass owns: the data layer hands over what
    /// <see cref="Mark.ResolveFill"/> answered (the hover look while it owns the state), and the overlay
    /// pass hands over <see cref="Mark.ActiveFillOf"/> for the cells of the hovered row.
    /// </summary>
    /// <param name="ctx">Context of the frame.</param>
    /// <param name="cell">Cell to draw (its row decides the colour and the state).</param>
    /// <param name="rows">Row count from <see cref="GridGeometry"/>.</param>
    /// <param name="cellSize">Cell side from <see cref="GridGeometry"/>.</param>
    /// <param name="offsetX">Grid origin on the X axis.</param>
    /// <param name="offsetY">Grid origin on the Y axis.</param>
    /// <param name="anim">Entry animation progress.</param>
    /// <param name="hovered">True when the cell's row is the hovered one.</param>
    /// <param name="selected">True when the cell's row is the selected one.</param>
    private void DrawCell(MarkContext ctx, WaffleCellInfo cell, int rows, float cellSize,
        float offsetX, float offsetY, float anim, bool hovered, bool selected)
    {
        // Same animated square HitTest uses: a cell that has not started growing is invisible and
        // therefore not hittable.
        var (px, py, drawSize) = CellSquare(cell, rows, cellSize, offsetX, offsetY, anim);
        if (drawSize <= 0f) return;

        // A layout cached for another data list would index out of bounds; skipping the cell beats
        // losing the frame (the overlay pass reads the same table).
        if (cell.RowIndex < 0 || cell.RowIndex >= ctx.Data.Count) return;

        float opacity = ComputeElementOpacity(ctx, ctx.Data[cell.RowIndex], cell.RowIndex);
        // Cell fill: the cached base colour is the default and ResolveFill applies the style
        // callback plus the hover brighten. While the overlay owns the state, ResolveFill answers
        // the default fill and the hover brighten is added here.
        var color = ResolveFill(ctx, ctx.Data[cell.RowIndex], cell.RowIndex, cell.Color);
        if (hovered && ctx.StateInOverlay) color = ActiveFillOf(ctx, color);

        var path = ShapePath(ctx);
        var paint = ShapePaint(ctx);

        if (CellRadius > 0)
            path.RoundRect(px, py, drawSize, drawSize, CellRadius);
        else
            path.Rect(px, py, drawSize, drawSize);

        paint.SetColor(color).SetAntiAlias(true).SetOpacity(opacity);
        ctx.Canvas.Fill(path, paint);

        // Selected: highlight stroke
        if (selected)
        {
            var strokePaint = ShapePaint(ctx);
            ApplySelectionPaint(ctx, strokePaint, opacity);
            ctx.Canvas.Stroke(path, strokePaint);
        }
    }

    /// <summary>
    /// A waffle cell is an opaque square, so its hover fill can be painted on the overlay pass without
    /// touching the cached layer: the overlay redraws exactly the squares of the interactive row (a row
    /// owns <see cref="WaffleCellInfo"/>-cells, all of them carry that row's state) from the same cached
    /// layout.
    /// </summary>
    public override bool InteractionStateInOverlay => true;

    /// <inheritdoc />
    public override void Render(MarkContext ctx)
    {
        if (ctx.Data.Count == 0) return;
        var cells = BuildCellLayout(ctx);
        if (cells.Count == 0) return;

        var (rows, cellSize, offsetX, offsetY) = GridGeometry(ctx, cells.Count);
        float anim = ComputeAnimProgress(ctx);

        // The interaction state stays in the data layer only while the chart does not cache it; with the
        // cache on the overlay paints it (see InteractionStateInOverlay / RenderOverlay).
        bool stateHere = !ctx.StateInOverlay;

        foreach (var cell in cells)
        {
            DrawCell(ctx, cell, rows, cellSize, offsetX, offsetY, anim,
                hovered: stateHere && cell.RowIndex == ctx.HoveredRowIndex,
                selected: stateHere && cell.RowIndex == ctx.SelectedRowIndex);
        }
    }

    /// <inheritdoc />
    public override void RenderOverlay(MarkContext ctx)
    {
        // Only while the chart keeps the data layer in an image: with the cache off Render painted the state.
        if (!ctx.StateInOverlay) return;

        int hovered = ctx.HoveredRowIndex;
        int selected = ctx.SelectedRowIndex;
        if (hovered < 0 && selected < 0) return;
        if (ctx.Data.Count == 0) return;

        var cells = BuildCellLayout(ctx);
        if (cells.Count == 0) return;

        var (rows, cellSize, offsetX, offsetY) = GridGeometry(ctx, cells.Count);
        float anim = ComputeAnimProgress(ctx);

        // Only the cells of the interactive rows are drawn, from the same layout the data layer used.
        foreach (var cell in cells)
        {
            bool isHovered  = cell.RowIndex == hovered;
            bool isSelected = cell.RowIndex == selected;
            if (!isHovered && !isSelected) continue;

            DrawCell(ctx, cell, rows, cellSize, offsetX, offsetY, anim,
                hovered: isHovered, selected: isSelected);
        }
    }

    /// <inheritdoc />
    public override HitResult? HitTest(MarkContext ctx, Vector2 screenPos)
    {
        if (ctx.Data.Count == 0) return null;
        var cells = BuildCellLayout(ctx);
        if (cells.Count == 0) return null;

        var (rows, cellSize, offsetX, offsetY) = GridGeometry(ctx, cells.Count);
        // Same animation as Render: the hit area is the drawn (possibly still growing) square.
        float anim = ComputeAnimProgress(ctx);

        foreach (var cell in cells)
        {
            var (px, py, drawSize) = CellSquare(cell, rows, cellSize, offsetX, offsetY, anim);
            if (drawSize <= 0f) continue;

            if (screenPos.X >= px && screenPos.X <= px + drawSize &&
                screenPos.Y >= py && screenPos.Y <= py + drawSize)
            {
                // See SankeyMark: guard against a layout built from a different data list.
                if (cell.RowIndex < 0 || cell.RowIndex >= ctx.Data.Count) continue;
                var row = ctx.Data[cell.RowIndex];
                var label = ctx.Encodes.Has(Channel.X)
                    ? ctx.Encodes.Resolve(Channel.X, row)?.ToString() ?? ""
                    : "";
                return new HitResult
                {
                    Hit = true, Row = row, RowIndex = cell.RowIndex,
                    ScreenX = px + drawSize * 0.5f, ScreenY = py + drawSize * 0.5f,
                    Label = label,
                    SeriesKey = ResolveSeriesKey(ctx, row),
                    MarkType = nameof(WaffleMark),
                };
            }
        }
        return null;
    }

    private List<WaffleCellInfo> BuildCellLayout(MarkContext ctx)
    {
        var key = CacheKey(ctx);
        if (_cachedCells != null && _cacheKey == key)
            return _cachedCells;

        // Compute proportions from Y channel values
        var entries = new List<(int rowIndex, double value, Color color)>();
        double total = 0;
        for (int i = 0; i < ctx.Data.Count; i++)
        {
            var row = ctx.Data[i];
            if (IsSeriesHidden(ctx, row)) continue;
            var yRaw = ctx.Encodes.Resolve(YChannel, row);
            double val = yRaw != null ? ToDouble(yRaw, "Y") : 0;
            if (val <= 0) continue;
            // Cache the plain channel colour (no hover): hover is applied per frame in Render through
            // ResolveFill, because the cell layout is cached across hover changes.
            var color = ResolveColor(ctx, row, GetDefaultColor(ctx));
            entries.Add((i, val, color));
            total += val;
        }

        var cells = new List<WaffleCellInfo>(TotalCells);
        if (total <= 0 || entries.Count == 0)
        {
            _cachedCells = cells;
            _cacheKey = key;
            LayoutBuildCount++;
            return cells;
        }

        // Allocate cells proportionally (largest remainder method)
        var allocations = new int[entries.Count];
        int allocated = 0;
        var remainders = new double[entries.Count];
        for (int i = 0; i < entries.Count; i++)
        {
            double exact = entries[i].value / total * TotalCells;
            allocations[i] = (int)Math.Floor(exact);
            remainders[i] = exact - allocations[i];
            allocated += allocations[i];
        }
        // Distribute remaining cells by largest remainder
        int remaining = TotalCells - allocated;
        while (remaining > 0)
        {
            int maxIdx = 0;
            double maxR = -1;
            for (int i = 0; i < remainders.Length; i++)
            {
                if (remainders[i] > maxR)
                {
                    maxR = remainders[i];
                    maxIdx = i;
                }
            }
            allocations[maxIdx]++;
            remainders[maxIdx] = -1; // consumed
            remaining--;
        }

        // Fill cells row by row
        int cellIdx = 0;
        for (int e = 0; e < entries.Count; e++)
        {
            for (int c = 0; c < allocations[e]; c++)
            {
                if (cellIdx >= TotalCells) break;
                int col = cellIdx % Columns;
                int row = cellIdx / Columns;
                cells.Add(new WaffleCellInfo
                {
                    RowIndex = entries[e].rowIndex,
                    CellCol = col,
                    CellRow = row,
                    Color = entries[e].color,
                });
                cellIdx++;
            }
        }

        _cachedCells = cells;
        _cacheKey = key;
        LayoutBuildCount++;
        return cells;
    }
}
