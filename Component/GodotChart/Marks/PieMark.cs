using System;
using System.Collections.Generic;
using Godot;
using GodotNodeExtension.Component.GodotChart.Canvas;

namespace GodotNodeExtension.Component.GodotChart.Marks;

/// <summary>
/// Pie chart mark. Encodes: X = category (label), Y = value (arc proportion),
/// Color = category (colors). Set <see cref="InnerRadius"/> &gt; 0 for donut chart.
/// Animation: arcs grow from center outward.
/// </summary>
public class PieMark : Mark
{
    /// <inheritdoc />
    public override MarkCoordinate Coordinate => MarkCoordinate.Polar;

    /// <summary>
    /// Inner radius ratio [0, 1). 0 = full pie, 0.5 = donut with half-size hole.
    /// </summary>
    public float InnerRadius { get; set; } = 0f;

    /// <summary>Start angle in radians. Default is -π/2 (top of circle).</summary>
    public float StartAngle { get; set; } = -MathF.PI / 2f;

    /// <summary>Whether to show category labels around the pie.</summary>
    public override bool ShowLabel { get; set; } = true;

    /// <summary>Label distance from center as ratio of outer radius.</summary>
    public float LabelDistance { get; set; } = 1.15f;

    /// <summary>Hover explode offset ratio relative to outer radius. Default 0.03.</summary>
    public float ExplodeRatio { get; set; } = 0.03f;

    /// <summary>Outer radius as ratio of min(width, height)/2. Range (0, 1].</summary>
    public float RadiusFactor { get; set; } = 0.85f;

    /// <summary>
    /// Custom content builder for the donut center area.
    /// Receives a LabelContext with all data rows and returns rich text lines.
    /// Only rendered when InnerRadius &gt; 0 and animation is complete.
    /// </summary>
    public Func<LabelContext, IReadOnlyList<TooltipLine>>? CenterContentBuilder { get; set; }

    /// <summary>
    /// Static text for the donut center. Use \n for line breaks.
    /// First line renders larger (title), subsequent lines render smaller (subtitle).
    /// Ignored if CenterContentBuilder is set.
    /// </summary>
    public string? CenterText { get; set; }

    /// <summary>Font size for the center primary text. Default 18.</summary>
    public float CenterFontSize { get; set; } = 18f;

    /// <summary>Font size for the center subtitle lines. Default 12.</summary>
    public float CenterSubFontSize { get; set; } = 12f;

    /// <summary>
    /// Custom label builder for each pie/donut segment.
    /// If set, overrides default label (category name).
    /// </summary>
    public new Func<LabelContext, string?>? LabelContentBuilder { get; set; }

    private float _cachedTotal;
    private int _cachedVersion = -1;

    private float EnsureTotal(MarkContext ctx)
    {
        if (_cachedVersion == ctx.LayoutVersion) return _cachedTotal;
        float total = 0;
        for (int i = 0; i < ctx.Data.Count; i++)
        {
            if (IsSeriesHidden(ctx, ctx.Data[i])) continue;
            var yResolved = ctx.Encodes.Resolve(YChannel, ctx.Data[i]);
            if (yResolved != null) total += ToSingle(yResolved, "Y");
        }
        _cachedTotal = total;
        _cachedVersion = ctx.LayoutVersion;
        return total;
    }

    public override void Render(MarkContext ctx)
    {
        float total = EnsureTotal(ctx);
        if (total <= 0) return;

        float cx = ctx.Plot.X + ctx.Plot.Width / 2f;
        float cy = ctx.Plot.Y + ctx.Plot.Height / 2f;
        float sizeFactor = ShowLabel ? RadiusFactor / MathF.Max(LabelDistance, 1f) : RadiusFactor;
        float maxR = MathF.Min(ctx.Plot.Width, ctx.Plot.Height) / 2f * sizeFactor;

        float anim = ComputeAnimProgress(ctx);
        float outerR = maxR;
        float innerR = outerR * InnerRadius;
        float totalSweep = MathF.Tau * anim;  // sweep fill animation
        float sweepEnd = StartAngle + totalSweep;

        float currentAngle = StartAngle;

        for (int i = 0; i < ctx.Data.Count; i++)
        {
            var row  = ctx.Data[i];
            if (IsSeriesHidden(ctx, row)) continue;
            var yRaw = ctx.Encodes.Resolve(YChannel, row);
            if (yRaw == null) continue;
            float value = ToSingle(yRaw, "Y");
            float sweepAngle = (value / total) * MathF.Tau;

            // Clip arc to current sweep position
            if (currentAngle >= sweepEnd) break;
            float clippedSweep = MathF.Min(sweepAngle, sweepEnd - currentAngle);
            bool fullyRevealed = clippedSweep >= sweepAngle - 0.001f;

            var color = ResolveColorWithOverride(ctx, row, i, GetDefaultColor(ctx));
            float opacity = ComputeEffectiveOpacity(ctx, row);

            bool isHovered  = i == ctx.HoveredRowIndex;
            bool isSelected = i == ctx.SelectedRowIndex;

            float offsetX = 0f, offsetY = 0f;
            if (isHovered)
            {
                color = BrightenColor(color, GetHoverBrighten(ctx));
                if (IsHoverExplodeEnabled(ctx))
                {
                    float midAngle = currentAngle + clippedSweep / 2f;
                    float explodeRatio = MathF.Abs(ExplodeRatio - 0.03f) > 1e-6f ? ExplodeRatio
                        : ctx.Theme?.PieExplodeRatio ?? 0.03f;
                    float explode  = outerR * explodeRatio;
                    offsetX = MathF.Cos(midAngle) * explode;
                    offsetY = MathF.Sin(midAngle) * explode;
                }
            }

            float endAngle = currentAngle + clippedSweep;

            using var path  = ctx.Canvas.CreatePath();
            using var paint = ctx.Canvas.CreatePaint();

            if (innerR <= 1f)
            {
                path.MoveTo(cx + offsetX, cy + offsetY);
                path.ArcTo(cx + offsetX, cy + offsetY, outerR,
                           currentAngle, endAngle);
                path.Close();
            }
            else
            {
                float osx = cx + offsetX + MathF.Cos(currentAngle) * outerR;
                float osy = cy + offsetY + MathF.Sin(currentAngle) * outerR;
                path.MoveTo(osx, osy);
                path.ArcTo(cx + offsetX, cy + offsetY, outerR,
                           currentAngle, endAngle);
                path.ArcTo(cx + offsetX, cy + offsetY, innerR,
                           endAngle, currentAngle, clockwise: true);
                path.Close();
            }

            paint.SetColor(color).SetAntiAlias(true).SetOpacity(opacity);
            ctx.Canvas.Fill(path, paint);

            using var borderPaint = ctx.Canvas.CreatePaint();
            var sbc = GetSegmentBorderColor(ctx);
            borderPaint.SetColor(sbc with { A = sbc.A * opacity })
                       .SetStrokeWidth(GetSegmentBorderWidth(ctx)).SetAntiAlias(true);
            ctx.Canvas.Stroke(path, borderPaint);

            if (isSelected)
            {
                using var selPaint = ctx.Canvas.CreatePaint();
                ApplySelectionPaint(ctx, selPaint, opacity);
                ctx.Canvas.Stroke(path, selPaint);
            }

            if (ShowLabel && fullyRevealed)
            {
                float midAngle = currentAngle + sweepAngle / 2f;
                float labelR = outerR * LabelDistance;
                float lx = cx + offsetX + MathF.Cos(midAngle) * labelR;
                float ly = cy + offsetY + MathF.Sin(midAngle) * labelR;

                var xRaw = ctx.Encodes.Has(Channel.X)
                    ? ctx.Encodes.Resolve(Channel.X, row) : null;
                string defaultLabel = xRaw?.ToString() ?? $"{value:G4}";

                string labelText = defaultLabel;
                if (LabelContentBuilder != null)
                {
                    var lctx = new LabelContext
                    {
                        Data = ctx.Data,
                        Row = row,
                        RowIndex = i,
                        ElementColor = color,
                        SeriesKey = ResolveSeriesKey(ctx, row),
                        DefaultText = defaultLabel,
                        Value = value,
                        Percentage = value / total,
                    };
                    labelText = LabelContentBuilder(lctx) ?? defaultLabel;
                }

                using var labelPaint = ctx.Canvas.CreatePaint();
                var dlc = GetDataLabelColor(ctx);
                labelPaint.SetColor(dlc with { A = dlc.A * opacity });
                ctx.Canvas.DrawText(labelText, lx, ly, FontSettings.Default, labelPaint);
            }

            currentAngle += sweepAngle;
        }

        // Donut center content
        if (InnerRadius > 0 && anim >= 1f)
            DrawCenterContent(ctx, cx, cy);
    }

    private void DrawCenterContent(MarkContext ctx, float cx, float cy)
    {
        // Priority: CenterContentBuilder (rich) > CenterText (static)
        if (CenterContentBuilder != null)
        {
            var lctx = new LabelContext
            {
                Data = ctx.Data,
                DefaultText = CenterText,
            };
            var richLines = CenterContentBuilder(lctx);
            if (richLines.Count == 0) return;

            float lineHeight = CenterFontSize + 4f;
            float totalH = richLines.Count * lineHeight;
            float startY = cy - totalH / 2f + CenterFontSize;

            using var paint = ctx.Canvas.CreatePaint();
            for (int j = 0; j < richLines.Count; j++)
            {
                float ly = startY + j * lineHeight;
                float lx = cx;
                foreach (var span in richLines[j].Spans)
                {
                    var spanColor = span.Color ?? GetDataLabelColor(ctx);
                    var spanFont = new FontSettings
                    {
                        Size = j == 0 ? CenterFontSize : CenterSubFontSize,
                        Bold = span.Bold,
                        Italic = span.Italic,
                        Align = TextAlign.Center,
                    };
                    paint.SetColor(spanColor);
                    ctx.Canvas.DrawText(span.Text, lx, ly, spanFont, paint);
                }
            }
        }
        else if (!string.IsNullOrEmpty(CenterText))
        {
            var lines = CenterText.Split('\n');
            float mainH = CenterFontSize;
            float subH = CenterSubFontSize + 4f;
            float totalH = mainH + (lines.Length - 1) * subH;
            float startY = cy - totalH / 2f + CenterFontSize;

            using var paint = ctx.Canvas.CreatePaint();
            var dlc = GetDataLabelColor(ctx);
            paint.SetColor(dlc);

            var mainFont = new FontSettings { Size = CenterFontSize, Bold = true, Align = TextAlign.Center };
            ctx.Canvas.DrawText(lines[0], cx, startY, mainFont, paint);

            if (lines.Length > 1)
            {
                var subFont = new FontSettings { Size = CenterSubFontSize, Align = TextAlign.Center };
                for (int j = 1; j < lines.Length; j++)
                {
                    float ly = startY + j * subH;
                    ctx.Canvas.DrawText(lines[j], cx, ly, subFont, paint);
                }
            }
        }
    }

    public override HitResult? HitTest(MarkContext ctx, Vector2 pos)
    {
        float total = EnsureTotal(ctx);
        if (total <= 0) return null;

        float cx = ctx.Plot.X + ctx.Plot.Width / 2f;
        float cy = ctx.Plot.Y + ctx.Plot.Height / 2f;
        float outerR = MathF.Min(ctx.Plot.Width, ctx.Plot.Height) / 2f * RadiusFactor;
        float innerR = outerR * InnerRadius;

        float dx = pos.X - cx;
        float dy = pos.Y - cy;
        float dist = MathF.Sqrt(dx * dx + dy * dy);
        if (dist > outerR || dist < innerR) return null;

        float angle = MathF.Atan2(dy, dx);
        float relAngle = ((angle - StartAngle) % MathF.Tau + MathF.Tau) % MathF.Tau;

        float currentAngle = 0f;
        for (int i = 0; i < ctx.Data.Count; i++)
        {
            var row   = ctx.Data[i];
            if (IsSeriesHidden(ctx, row)) continue;
            var yResolved = ctx.Encodes.Resolve(YChannel, row);
            if (yResolved == null) continue;
            float val = ToSingle(yResolved, "Y");
            float sweep = (val / total) * MathF.Tau;

            if (relAngle >= currentAngle && relAngle < currentAngle + sweep)
            {
                var xRaw = ctx.Encodes.Has(Channel.X)
                    ? ctx.Encodes.Resolve(Channel.X, row) : null;
                float midAngle = StartAngle + currentAngle + sweep / 2f;
                return new HitResult
                {
                    Hit      = true, Row = row, RowIndex = i,
                    ScreenX  = cx + MathF.Cos(midAngle) * outerR * 0.6f,
                    ScreenY  = cy + MathF.Sin(midAngle) * outerR * 0.6f,
                    Label    = $"{xRaw}: {val:G4}",
                    SeriesKey = xRaw?.ToString(),
                    MarkType = nameof(PieMark),
                };
            }
            currentAngle += sweep;
        }
        return null;
    }
}
