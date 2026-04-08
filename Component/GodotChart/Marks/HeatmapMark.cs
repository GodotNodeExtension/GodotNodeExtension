using System.Collections.Generic;
using Godot;
using GodotNodeExtension.Component.GodotChart.Canvas;

namespace GodotNodeExtension.Component.GodotChart.Marks;

/// <summary>
/// Heatmap chart mark. Renders a color-coded matrix grid.
/// Encodes: X = column category (OrdinalScale), Y = row category (OrdinalScale),
/// Color = continuous value (SequentialColorScale).
/// Animation: cells fade in from zero opacity.
/// </summary>
public class HeatmapMark : Mark
{
    /// <inheritdoc />
    public override MarkCoordinate Coordinate => MarkCoordinate.Cartesian;

    /// <summary>Gap between cells in pixels.</summary>
    public float CellGap { get; set; } = 1f;

    /// <summary>Corner radius for cells.</summary>
    public float CornerRadius { get; set; } = 0f;

    /// <summary>Whether to show the value label inside each cell.</summary>
    public override bool ShowLabel { get; set; } = false;

    private (float cellW, float cellH) ComputeCellSize(MarkContext ctx, int cols, int rows)
    {
        float cellW = (ctx.Plot.Width  - CellGap * (cols - 1)) / cols;
        float cellH = (ctx.Plot.Height - CellGap * (rows - 1)) / rows;
        return (cellW, cellH);
    }

    /// <inheritdoc />
    public override void ContributeScales(
        ScaleSet scales, EncodeSet encodes, List<DataRow> data)
    {
        // Heatmap always needs a SequentialColorScale for the Color channel.
        // Override any auto-fitted scale (e.g. LinearScale chosen for numeric data).
        if (encodes.Has(Channel.Color) && scales.TryGet(Channel.Color) is not SequentialColorScale)
        {
            var values = new List<object>();
            foreach (var row in data)
            {
                var v = encodes.Resolve(Channel.Color, row);
                if (v != null) values.Add(v);
            }
            if (values.Count > 0)
            {
                var seqScale = new SequentialColorScale();
                seqScale.Fit(values);
                scales.Set(Channel.Color, seqScale);
            }
        }
    }

    public override void Render(MarkContext ctx)
    {
        var xScale = ctx.Scales.TryGet(Channel.X) as OrdinalScale;
        var yScale = ctx.Scales.TryGet(YChannel) as OrdinalScale;
        var colorScale = ctx.Scales.TryGet(Channel.Color) as SequentialColorScale;
        if (xScale == null || yScale == null || colorScale == null) return;

        int cols = xScale.Domain.Count;
        int rows = yScale.Domain.Count;
        if (cols == 0 || rows == 0) return;

        var (cellW, cellH) = ComputeCellSize(ctx, cols, rows);
        float anim  = ComputeAnimProgress(ctx);

        for (int i = 0; i < ctx.Data.Count; i++)
        {
            var row   = ctx.Data[i];
            if (IsSeriesHidden(ctx, row)) continue;
            var xRaw  = ctx.Encodes.Resolve(Channel.X, row);
            var yRaw  = ctx.Encodes.Resolve(YChannel, row);
            var cRaw  = ctx.Encodes.Resolve(Channel.Color, row);
            if (xRaw == null || yRaw == null || cRaw == null) continue;

            int colIdx = xScale.IndexOf(xRaw.ToString()!);
            int rowIdx = yScale.IndexOf(yRaw.ToString()!);
            if (colIdx < 0 || rowIdx < 0) continue;

            Color color = colorScale.MapColor(cRaw);
            float opacity = anim * ComputeEffectiveOpacity(ctx, row);

            bool isHovered  = i == ctx.HoveredRowIndex;
            bool isSelected = i == ctx.SelectedRowIndex;

            if (isHovered) color = BrightenColor(color, GetHoverBrighten(ctx));

            // Cell position: Y axis goes top-down for matrix layout
            float px = ctx.Plot.X + colIdx * (cellW + CellGap);
            float py = ctx.Plot.Y + rowIdx * (cellH + CellGap);

            using var path  = ctx.Canvas.CreatePath();
            using var paint = ctx.Canvas.CreatePaint();

            if (CornerRadius > 0)
                path.RoundRect(px, py, cellW, cellH, CornerRadius);
            else
                path.Rect(px, py, cellW, cellH);

            paint.SetColor(color).SetAntiAlias(true).SetOpacity(opacity);
            ctx.Canvas.Fill(path, paint);

            if (isSelected)
            {
                using var strokePaint = ctx.Canvas.CreatePaint();
                ApplySelectionPaint(ctx, strokePaint, opacity);
                ctx.Canvas.Stroke(path, strokePaint);
            }

            if (ShowLabel)
            {
                using var labelPaint = ctx.Canvas.CreatePaint();
                var dlc = GetDataLabelColor(ctx);
                labelPaint.SetColor(dlc with { A = dlc.A * opacity });
                string text = colorScale.Format(cRaw);
                ctx.Canvas.DrawText(text, px + cellW / 2f, py + cellH / 2f, FontSettings.Default, labelPaint);
            }
        }
    }

    public override HitResult? HitTest(MarkContext ctx, Vector2 pos)
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
            if (xRaw == null || yRaw == null) continue;

            int colIdx = xScale.IndexOf(xRaw.ToString()!);
            int rowIdx = yScale.IndexOf(yRaw.ToString()!);
            if (colIdx < 0 || rowIdx < 0) continue;

            float px = ctx.Plot.X + colIdx * (cellW + CellGap);
            float py = ctx.Plot.Y + rowIdx * (cellH + CellGap);

            if (pos.X >= px && pos.X <= px + cellW &&
                pos.Y >= py && pos.Y <= py + cellH)
            {
                var cRaw = ctx.Encodes.Resolve(Channel.Color, row);
                return new HitResult
                {
                    Hit = true, Row = row, RowIndex = i,
                    ScreenX = px + cellW / 2f, ScreenY = py,
                    Label = $"{xRaw}/{yRaw}: {cRaw}",
                    MarkType = nameof(HeatmapMark),
                };
            }
        }
        return null;
    }
}
