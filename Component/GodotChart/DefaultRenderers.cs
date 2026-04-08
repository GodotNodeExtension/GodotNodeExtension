using System;
using Godot;
using GodotNodeExtension.Component.GodotChart.Canvas;

namespace GodotNodeExtension.Component.GodotChart;

/// <summary>
/// Built-in default renderer implementations for all chart rendering stages.
/// These methods are assigned to Chart renderer slots by default.
/// Users can reference these to compose custom renderers that extend default behavior.
/// </summary>
public static class DefaultRenderers
{
    private static float AxisTitleMargin(RenderContext ctx) => ctx.Theme.AxisTitleMargin;

    /// <summary>Default number of grid/label divisions for TimeScale.</summary>
    private const int TimeScaleDefaultTicks = 5;

    // Pre-built font settings to avoid per-frame struct copies
    private static readonly FontSettings RightAlignedFont =
        new() { Size = FontSettings.Default.Size, Align = TextAlign.Right };
    private static readonly FontSettings LeftAlignedFont =
        new() { Size = FontSettings.Default.Size, Align = TextAlign.Left };
    private static readonly FontSettings CenteredFont =
        new() { Size = FontSettings.Default.Size, Align = TextAlign.Center };

    /// <summary>Draw a rounded-rect background with the theme's background color.</summary>
    public static void DrawBackground(RenderContext ctx)
    {
        using var path = ctx.Canvas.CreatePath();
        using var paint = ctx.Canvas.CreatePaint();
        path.RoundRect(ctx.OffsetX, ctx.OffsetY, ctx.Width, ctx.Height,
                       ctx.Theme.BackgroundCornerRadius);
        paint.SetColor(ctx.BackgroundColor);
        ctx.Canvas.Fill(path, paint);
    }

    /// <summary>Draw chart title text at the top of the chart area.</summary>
    public static void DrawTitle(RenderContext ctx)
    {
        if (ctx.Title == null) return;
        using var paint = ctx.Canvas.CreatePaint();
        paint.SetColor(ctx.Theme.TitleColor).SetStrokeWidth(1f);
        ctx.Canvas.DrawText(ctx.Title, ctx.OffsetX + ctx.PaddingLeft,
                            ctx.OffsetY + ctx.Theme.TitleYOffset, FontSettings.Default, paint);
    }

    /// <summary>Draw grid lines from X/Y scales.</summary>
    public static void DrawGrid(RenderContext ctx)
    {
        var plot = ctx.Plot;
        using var paint = ctx.Canvas.CreatePaint();
        paint.SetColor(ctx.GridColor).SetStrokeWidth(ctx.Theme.GridLineWidth);

        // Horizontal grid lines from Y scale
        if (ctx.Scales.TryGet(Channel.Y) is LinearScale ys)
        {
            int count = ys.NiceTicks.Count > 0 ? ys.NiceTicks.Count : 5;
            for (int i = 0; i <= count; i++)
            {
                float py = plot.MapY((double)i / count);
                ctx.Canvas.DrawLine(plot.X, py, plot.X + plot.Width, py, paint);
            }
        }

        // Vertical grid lines from X scale
        var xScale = ctx.Scales.TryGet(Channel.X);
        switch (xScale)
        {
            case OrdinalScale xs:
                foreach (var label in xs.Domain)
                {
                    float px = plot.MapX(xs.Map(label));
                    ctx.Canvas.DrawLine(px, plot.Y, px, plot.Y + plot.Height, paint);
                }
                break;
            case LinearScale lx:
            {
                int count = lx.NiceTicks.Count > 0 ? lx.NiceTicks.Count : 5;
                for (int i = 0; i <= count; i++)
                {
                    float px = plot.MapX((double)i / count);
                    ctx.Canvas.DrawLine(px, plot.Y, px, plot.Y + plot.Height, paint);
                }
                break;
            }
            case TimeScale:
            {
                for (int i = 0; i <= TimeScaleDefaultTicks; i++)
                {
                    float px = plot.MapX(i / (double)TimeScaleDefaultTicks);
                    ctx.Canvas.DrawLine(px, plot.Y, px, plot.Y + plot.Height, paint);
                }
                break;
            }
        }
    }

    /// <summary>Draw axis lines (left Y, bottom X, optional right Y2).</summary>
    public static void DrawAxes(RenderContext ctx)
    {
        var plot = ctx.Plot;
        using var paint = ctx.Canvas.CreatePaint();
        paint.SetColor(ctx.AxisColor).SetStrokeWidth(ctx.Theme.AxisLineWidth);
        // Left Y axis
        ctx.Canvas.DrawLine(plot.X, plot.Y, plot.X, plot.Y + plot.Height, paint);
        // Bottom X axis
        ctx.Canvas.DrawLine(plot.X, plot.Y + plot.Height,
                            plot.X + plot.Width, plot.Y + plot.Height, paint);
        // Right Y2 axis (only when Y2 scale exists)
        if (ctx.Scales.Has(Channel.Y2))
        {
            ctx.Canvas.DrawLine(plot.X + plot.Width, plot.Y,
                                plot.X + plot.Width, plot.Y + plot.Height, paint);
        }
    }

    /// <summary>Draw axis tick labels and rotated axis titles.</summary>
    public static void DrawAxisLabels(RenderContext ctx)
    {
        var plot = ctx.Plot;
        using var paint = ctx.Canvas.CreatePaint();
        paint.SetColor(ctx.AxisColor).SetStrokeWidth(1f);

        // X axis labels
        var xScale = ctx.Scales.TryGet(Channel.X);
        switch (xScale)
        {
            case OrdinalScale xs:
                foreach (var label in xs.Domain)
                {
                    float px = plot.MapX(xs.Map(label));
                    float py = plot.Y + plot.Height + ctx.Theme.XAxisLabelOffset;
                    ctx.Canvas.DrawText(label, px, py, FontSettings.Default, paint);
                }
                break;
            case LinearScale lx:
            {
                int count = lx.NiceTicks.Count > 0 ? lx.NiceTicks.Count : 5;
                for (int i = 0; i <= count; i++)
                {
                    double v = lx.Min + (lx.Max - lx.Min) * i / count;
                    float px = plot.MapX((double)i / count);
                    float py = plot.Y + plot.Height + ctx.Theme.XAxisLabelOffset;
                    ctx.Canvas.DrawText(lx.Format(v), px, py, FontSettings.Default, paint);
                }
                break;
            }
            case TimeScale tx:
                for (int i = 0; i <= TimeScaleDefaultTicks; i++)
                {
                    double t = i / (double)TimeScaleDefaultTicks;
                    long ticks = tx.Min.Ticks + (long)((tx.Max.Ticks - tx.Min.Ticks) * t);
                    var dt = new DateTime(ticks);
                    float px = plot.MapX(t);
                    float py = plot.Y + plot.Height + ctx.Theme.XAxisLabelOffset;
                    ctx.Canvas.DrawText(tx.Format(dt), px, py, FontSettings.Default, paint);
                }
                break;
        }

        // Y axis labels (right-aligned)
        if (ctx.Scales.TryGet(Channel.Y) is LinearScale ys)
        {
            var yLabelFont = RightAlignedFont;
            int yCount = ys.NiceTicks.Count > 0 ? ys.NiceTicks.Count : 5;
            for (int i = 0; i <= yCount; i++)
            {
                double v = ys.Min + (ys.Max - ys.Min) * i / yCount;
                float py = plot.MapY((double)i / yCount);
                ctx.Canvas.DrawText(ys.Format(v), plot.X - ctx.Theme.YAxisLabelGap, py, yLabelFont, paint);
            }
        }

        // Y2 axis labels on the right side
        if (ctx.Scales.TryGet(Channel.Y2) is LinearScale y2s)
        {
            var y2LabelFont = LeftAlignedFont;
            int y2Count = y2s.NiceTicks.Count > 0 ? y2s.NiceTicks.Count : 5;
            for (int i = 0; i <= y2Count; i++)
            {
                double v = y2s.Min + (y2s.Max - y2s.Min) * i / y2Count;
                float py = plot.MapY((double)i / y2Count);
                ctx.Canvas.DrawText(y2s.Format(v), plot.X + plot.Width + ctx.Theme.YAxisLabelGap, py, y2LabelFont, paint);
            }
        }

        // Axis titles
        float lineH = CenteredFont.Size * CenteredFont.LineHeightMultiplier;
        if (ctx.XAxisConfig?.Title != null)
        {
            float tx = plot.X + plot.Width * 0.5f;
            float ty = ctx.OffsetY + ctx.Height - AxisTitleMargin(ctx) - CenteredFont.Size * 0.2f;
            ctx.Canvas.DrawText(ctx.XAxisConfig.Title, tx, ty, CenteredFont, paint);
        }
        if (ctx.YAxisConfig?.Title != null)
        {
            float tyCenter = plot.Y + plot.Height / 2f;
            float txPos = ctx.OffsetX + AxisTitleMargin(ctx);
            using (ctx.Canvas.SaveScope())
            {
                ctx.Canvas.Translate(txPos, tyCenter);
                ctx.Canvas.Rotate(-MathF.PI / 2f);
                ctx.Canvas.DrawText(ctx.YAxisConfig.Title, 0, lineH, CenteredFont, paint);
            }
        }
        if (ctx.Y2AxisConfig?.Title != null && ctx.Scales.Has(Channel.Y2))
        {
            float tyCenter = plot.Y + plot.Height / 2f;
            float txPos = ctx.OffsetX + ctx.Width - AxisTitleMargin(ctx);
            using (ctx.Canvas.SaveScope())
            {
                ctx.Canvas.Translate(txPos, tyCenter);
                ctx.Canvas.Rotate(MathF.PI / 2f);
                ctx.Canvas.DrawText(ctx.Y2AxisConfig.Title, 0, lineH, CenteredFont, paint);
            }
        }
    }

    /// <summary>Draw the legend (swatch + label items).</summary>
    public static void DrawLegend(RenderContext ctx)
    {
        var colorScale = ctx.ColorScale;
        if (colorScale == null || colorScale.Domain.Count == 0) return;

        var cfg = ctx.LegendConfig;
        if (cfg is not { Position: not LegendPosition.None }) return;

        var items = ctx.CachedLegendItems
            ?? LegendLayoutHelper.Compute(ctx.Plot, cfg, colorScale, ctx.OffsetX, ctx.Width, ctx.Canvas, ctx.Theme);
        if (items == null) return;

        using var paint = ctx.Canvas.CreatePaint();
        var font = new FontSettings { Size = FontSettings.Default.Size, Align = TextAlign.Left };
        float swatchSize = cfg.SwatchSize;
        float textOffsetX = swatchSize + ctx.Theme.LegendSwatchTextGap;
        using var swatchPath = ctx.Canvas.CreatePath();

        foreach (var item in items)
        {
            Color color = colorScale.MapColor(item.Key);

            // Dim non-focused items
            bool dimmed = ctx.FocusedSeries != null && ctx.FocusedSeries != item.Key;
            // Hidden items shown with strikethrough style
            bool hidden = ctx.HiddenSeries != null && ctx.HiddenSeries.Contains(item.Key);
            float alpha = dimmed || hidden ? ctx.Theme.LegendDimmedOpacity : 1f;

            // Draw color swatch
            swatchPath.Reset();
            swatchPath.RoundRect(item.X, item.Y, swatchSize, swatchSize, ctx.Theme.LegendSwatchCornerRadius);
            paint.SetColor(new Color(color.R, color.G, color.B, alpha));
            ctx.Canvas.Fill(swatchPath, paint);

            // Draw label text
            paint.SetColor(new Color(ctx.AxisColor.R, ctx.AxisColor.G, ctx.AxisColor.B, alpha));
            ctx.Canvas.DrawText(item.Key, item.X + textOffsetX, item.Y + swatchSize * 0.85f, font, paint);
        }
    }

    /// <summary>Draw crosshair overlay at mouse position.</summary>
    public static void DrawCrosshair(RenderContext ctx)
    {
        if (!ctx.MousePos.HasValue) return;
        ChartInteraction.DrawCrosshair(ctx.Canvas, ctx.MousePos.Value, ctx.Plot, ctx.Theme);
    }
}
