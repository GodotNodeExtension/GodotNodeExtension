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

    /// <summary>Total number of cells in the waffle grid. Default 100.</summary>
    public int TotalCells { get; set; } = 100;

    /// <summary>Number of columns in the waffle grid. Default 10.</summary>
    public int Columns { get; set; } = 10;

    /// <summary>Gap between cells in pixels. Default 2.</summary>
    public float CellGap { get; set; } = 2f;

    /// <summary>Corner radius of each cell. Default 2.</summary>
    public float CellRadius { get; set; } = 2f;

    // Cached layout — uses LayoutVersion (not DataVersion) because cell layout
    // depends on scale domains and encode bindings, not just data values.
    private List<WaffleCellInfo>? _cachedCells;
    private int _cachedVersion = -1;

    private struct WaffleCellInfo
    {
        public int RowIndex;
        public int CellCol;
        public int CellRow;
        public Color Color;
    }

    public override void Render(MarkContext ctx)
    {
        if (ctx.Data.Count == 0) return;
        var cells = BuildCellLayout(ctx);

        int rows = (int)Math.Ceiling((double)TotalCells / Columns);
        float cellW = (ctx.Plot.Width - CellGap * (Columns - 1)) / Columns;
        float cellH = (ctx.Plot.Height - CellGap * (rows - 1)) / rows;
        float cellSize = MathF.Min(cellW, cellH);

        // Center the grid in the plot area
        float gridW = Columns * cellSize + (Columns - 1) * CellGap;
        float gridH = rows * cellSize + (rows - 1) * CellGap;
        float offsetX = ctx.Plot.X + (ctx.Plot.Width - gridW) * 0.5f;
        float offsetY = ctx.Plot.Y + (ctx.Plot.Height - gridH) * 0.5f;

        float anim = ComputeAnimProgress(ctx);

        foreach (var cell in cells)
        {
            float px = offsetX + cell.CellCol * (cellSize + CellGap);
            // Render bottom-to-top (row 0 at bottom)
            float py = offsetY + gridH - (cell.CellRow + 1) * cellSize - cell.CellRow * CellGap;

            float opacity = ComputeEffectiveOpacity(ctx, ctx.Data[cell.RowIndex]);
            bool isHovered = cell.RowIndex == ctx.HoveredRowIndex;
            var color = isHovered ? BrightenColor(cell.Color, GetHoverBrighten(ctx)) : cell.Color;

            // Animation: scale from 0 to 1 with stagger
            int cellIndex = cell.CellRow * Columns + cell.CellCol;
            float stagger = (float)cellIndex / TotalCells;
            float cellAnim = Math.Clamp((anim - stagger * 0.5f) / 0.5f, 0f, 1f);
            if (cellAnim <= 0f) continue;

            float drawSize = cellSize * cellAnim;
            float drawOffset = (cellSize - drawSize) * 0.5f;

            using var path = ctx.Canvas.CreatePath();
            using var paint = ctx.Canvas.CreatePaint();

            if (CellRadius > 0)
                path.RoundRect(px + drawOffset, py + drawOffset, drawSize, drawSize, CellRadius);
            else
                path.Rect(px + drawOffset, py + drawOffset, drawSize, drawSize);

            paint.SetColor(color).SetAntiAlias(true).SetOpacity(opacity);
            ctx.Canvas.Fill(path, paint);

            // Selected: highlight stroke
            if (cell.RowIndex == ctx.SelectedRowIndex)
            {
                using var strokePaint = ctx.Canvas.CreatePaint();
                ApplySelectionPaint(ctx, strokePaint, opacity);
                ctx.Canvas.Stroke(path, strokePaint);
            }
        }
    }

    public override HitResult? HitTest(MarkContext ctx, Vector2 screenPos)
    {
        if (ctx.Data.Count == 0) return null;
        var cells = BuildCellLayout(ctx);

        int rows = (int)Math.Ceiling((double)TotalCells / Columns);
        float cellW = (ctx.Plot.Width - CellGap * (Columns - 1)) / Columns;
        float cellH = (ctx.Plot.Height - CellGap * (rows - 1)) / rows;
        float cellSize = MathF.Min(cellW, cellH);

        float gridW = Columns * cellSize + (Columns - 1) * CellGap;
        float gridH = rows * cellSize + (rows - 1) * CellGap;
        float offsetX = ctx.Plot.X + (ctx.Plot.Width - gridW) * 0.5f;
        float offsetY = ctx.Plot.Y + (ctx.Plot.Height - gridH) * 0.5f;

        foreach (var cell in cells)
        {
            float px = offsetX + cell.CellCol * (cellSize + CellGap);
            float py = offsetY + gridH - (cell.CellRow + 1) * cellSize - cell.CellRow * CellGap;

            if (screenPos.X >= px && screenPos.X <= px + cellSize &&
                screenPos.Y >= py && screenPos.Y <= py + cellSize)
            {
                var row = ctx.Data[cell.RowIndex];
                var label = ctx.Encodes.Has(Channel.X)
                    ? ctx.Encodes.Resolve(Channel.X, row)?.ToString() ?? ""
                    : "";
                return new HitResult
                {
                    Hit = true, Row = row, RowIndex = cell.RowIndex,
                    ScreenX = px + cellSize * 0.5f, ScreenY = py + cellSize * 0.5f,
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
        if (_cachedCells != null && _cachedVersion == ctx.LayoutVersion)
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
            var color = ResolveColorWithOverride(ctx, row, i, GetDefaultColor(ctx));
            entries.Add((i, val, color));
            total += val;
        }

        var cells = new List<WaffleCellInfo>(TotalCells);
        if (total <= 0 || entries.Count == 0)
        {
            _cachedCells = cells;
            _cachedVersion = ctx.LayoutVersion;
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
        _cachedVersion = ctx.LayoutVersion;
        return cells;
    }
}
