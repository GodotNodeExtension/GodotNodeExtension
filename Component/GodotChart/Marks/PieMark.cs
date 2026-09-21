using System;
using System.Collections.Generic;
using Godot;
using GodotNodeExtension.Component.GodotChart.Canvas;

namespace GodotNodeExtension.Component.GodotChart.Marks;

/// <summary>
/// Pie chart mark. Encodes: X = category (label), Y = value (arc proportion),
/// Color = category (colors). Set <see cref="InnerRadius"/> &gt; 0 for donut chart.
/// Animation: arcs grow from center outward.
/// <para>
/// Values without a slice - zero, negative and non-finite ones - are skipped in both the total and the
/// drawing, and a slice that would span the whole circle is kept a hair below a full turn (a 2*pi sweep
/// is turned into 0 by the backend, which made a lone 100% slice disappear).
/// </para>
/// </summary>
public class PieMark : Mark
{
    /// <inheritdoc />
    public override MarkCoordinate Coordinate => MarkCoordinate.Polar;

    /// <summary>
    /// Inner radius ratio [0, 1). 0 = full pie, 0.5 = donut with half-size hole.
    /// </summary>
    public float InnerRadius { get; set; }

    /// <summary>Start angle in radians. Default is -π/2 (top of circle).</summary>
    public float StartAngle { get; set; } = -MathF.PI / 2f;

    /// <summary>Whether to show category labels around the pie.</summary>
    public override bool ShowLabel { get; set; } = true;

    /// <summary>Label distance from center as ratio of outer radius.</summary>
    public float LabelDistance { get; set; } = 1.15f;

    /// <summary>
    /// Hover explode offset ratio relative to the outer radius.
    /// Null (the default) means "use <see cref="ChartTheme.PieExplodeRatio"/>" — the previous magic
    /// default 0.03 made an explicitly configured 0.03 indistinguishable from "not set".
    /// </summary>
    public float? ExplodeRatio { get; set; }

    /// <summary>Outer radius as ratio of min(width, height)/2. Range (0, 1].</summary>
    public float RadiusFactor { get; set; } = 0.85f;

    /// <summary>
    /// This mark keeps its interaction state in the data layer, so a chart carrying it renders single-pass
    /// (see <see cref="Chart.UseLayerCache"/>).
    /// <para>
    /// The hover visual moves the slice <b>and its label</b> outward (the explode offset is applied to both
    /// the wedge and the text anchored outside it), while a cached layer holds that label at its
    /// un-exploded place: the overlay can move a slice, but it cannot take a label back out of the image
    /// the chart already drew, so the cached frame would show the text twice.
    /// </para>
    /// </summary>
    public override bool InteractionStateInOverlay => false;

    /// <summary>
    /// A pie is drawn from the plot's <b>short</b> edge (<see cref="RadiusFactor"/> of
    /// <c>min(width, height) / 2</c>), so it asks for a square content area: a wide canvas otherwise leaves a
    /// band of empty background on either side of the circle.
    /// </summary>
    public override float? PreferredAspectRatio => 1f;

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
    /// Custom label builder for each pie/donut segment (overrides the default category name label).
    /// <para>
    /// This used to be declared with <c>new</c> over the base <see cref="Mark.LabelContentBuilder"/>,
    /// which has a different signature — setting the base property on a PieMark therefore silently
    /// did nothing. The dedicated name removes that trap.
    /// </para>
    /// </summary>
    public Func<LabelContext, string?>? SliceLabelBuilder { get; set; }

    private float _cachedTotal;
    private LayoutCacheKey? _totalKey;
    private int? _totalConfigHash;
    private bool _warnedNegativeValue;

    /// <summary>
    /// Sum of the slice values for the current context.
    /// <para>
    /// Non-finite values never enter the total (they would make every slice angle NaN), and neither do
    /// non-positive ones: a value without a slice must not shift the shares of the others - a negative
    /// value used to subtract from the total and draw a backwards wedge over the whole pie.
    /// </para>
    /// <para>
    /// The total is cached per layout cache key and layout configuration, not per layout version alone:
    /// another chart reusing this mark instance, or a changed <see cref="Mark.YChannel"/>, needs its own
    /// total even when the versions match.
    /// </para>
    /// </summary>
    private float EnsureTotal(MarkContext ctx)
    {
        var key = CacheKey(ctx);
        int configHash = HashCode.Combine(YChannel);
        if (_totalKey == key && _totalConfigHash == configHash) return _cachedTotal;

        float total = 0;
        bool sawNegative = false;
        for (int i = 0; i < ctx.Data.Count; i++)
        {
            if (IsSeriesHidden(ctx, ctx.Data[i])) continue;
            var yResolved = ctx.Encodes.Resolve(YChannel, ctx.Data[i]);
            if (yResolved == null) continue;
            float value = ToSingle(yResolved, "Y");
            if (!float.IsFinite(value)) continue;
            if (value < 0f) { sawNegative = true; continue; }
            total += value;
        }
        if (sawNegative && !_warnedNegativeValue)
        {
            _warnedNegativeValue = true;
            GD.PushWarning("PieMark: negative values have no slice and are ignored " +
                           "(the total and the slices leave them out).");
        }

        _cachedTotal = total;
        _totalKey = key;
        _totalConfigHash = configHash;
        return total;
    }

    /// <summary>
    /// Centre and outer radius of the pie: the one geometry <see cref="Render"/> draws with and
    /// <see cref="HitTest"/> measures against. While labels are shown the radius shrinks by
    /// <see cref="LabelDistance"/> to leave room for the label ring - hit testing used to ignore
    /// that factor, which made its circle about 15% wider than the drawn pie.
    /// </summary>
    private (float centreX, float centreY, float radius) PieGeometry(MarkContext ctx)
    {
        float sizeFactor = ShowLabel ? RadiusFactor / MathF.Max(LabelDistance, 1f) : RadiusFactor;
        float maxR = MathF.Min(ctx.Plot.Width, ctx.Plot.Height) / 2f * sizeFactor;
        return (ctx.Plot.X + ctx.Plot.Width / 2f, ctx.Plot.Y + ctx.Plot.Height / 2f, maxR);
    }

    /// <summary>
    /// Angle up to which the entry animation has revealed arcs: <see cref="Render"/> clips every arc
    /// at it and <see cref="HitTest"/> refuses arcs beyond it, so a slice can only be hit once it has
    /// been drawn.
    /// </summary>
    /// <param name="progress">Entry progress in [0, 1].</param>
    private float SweepEnd(float progress) => StartAngle + MathF.Tau * progress;

    /// <inheritdoc />
    public override void Render(MarkContext ctx)
    {
        float total = EnsureTotal(ctx);
        if (total <= 0) return;

        var (cx, cy, outerR) = PieGeometry(ctx);

        float anim = ComputeAnimProgress(ctx);
        float innerR = outerR * InnerRadius;
        float sweepEnd = SweepEnd(anim);

        float currentAngle = StartAngle;

        for (int i = 0; i < ctx.Data.Count; i++)
        {
            var row  = ctx.Data[i];
            if (IsSeriesHidden(ctx, row)) continue;
            var yRaw = ctx.Encodes.Resolve(YChannel, row);
            if (yRaw == null) continue;
            float value = ToSingle(yRaw, "Y");
            // A non-finite value has no angle, and a non-positive one has no slice: skipping it keeps
            // the wedge (and its outline) out of the picture instead of stacking it on the first slice.
            if (!float.IsFinite(value) || value <= 0f) continue;
            // A slice that spans the whole circle (a single row, or one left after hiding the others)
            // would ask the backend for a 2*pi sweep, which it turns into 0 (angles are taken modulo a
            // full turn) - and the whole pie/ring disappeared. Keep a hair below a full turn, the same
            // rule GaugeMark and ChordMark follow.
            float sweepAngle = MathF.Min((value / total) * MathF.Tau, PolarGeometry.FullTurnCap);

            // Clip arc to current sweep position
            if (currentAngle >= sweepEnd) break;
            float clippedSweep = MathF.Min(sweepAngle, sweepEnd - currentAngle);
            bool fullyRevealed = clippedSweep >= sweepAngle - 0.001f;

            var color = ResolveFill(ctx, row, i, GetDefaultColor(ctx));
            float opacity = ComputeElementOpacity(ctx, row, i);

            bool isHovered  = i == ctx.HoveredRowIndex;
            bool isSelected = i == ctx.SelectedRowIndex;

            float offsetX = 0f, offsetY = 0f;
            if (isHovered && IsHoverExplodeEnabled(ctx))
            {
                float midAngle = currentAngle + clippedSweep / 2f;
                float explodeRatio = ExplodeRatio ?? (ctx.Theme ?? ChartTheme.Default).PieExplodeRatio;
                float explode  = outerR * explodeRatio;
                offsetX = MathF.Cos(midAngle) * explode;
                offsetY = MathF.Sin(midAngle) * explode;
            }

            float endAngle = currentAngle + clippedSweep;

            var path = ShapePath(ctx);
            var paint = ShapePaint(ctx);

            if (innerR <= 1f)
            {
                path.MoveTo(cx + offsetX, cy + offsetY);
                path.ArcTo(cx + offsetX, cy + offsetY, outerR,
                           currentAngle, endAngle);
                path.Close();
            }
            else
            {
                ShapeGeometry.AddRingBand(path, cx, cy, outerR, innerR, currentAngle, endAngle,
                                          offsetX, offsetY);
            }

            paint.SetColor(color).SetAntiAlias(true).SetOpacity(opacity);
            ctx.Canvas.Fill(path, paint);

            var borderPaint = ShapePaint(ctx);
            var sbc = GetSegmentBorderColor(ctx);
            borderPaint.SetColor(sbc with { A = sbc.A * opacity })
                       .SetStrokeWidth(GetSegmentBorderWidth(ctx)).SetAntiAlias(true);
            ctx.Canvas.Stroke(path, borderPaint);

            if (isSelected)
            {
                var selPaint = ShapePaint(ctx);
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
                if (SliceLabelBuilder != null)
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
                    // The builder runs once; calling it again for the null check would double every
                    // side effect the host put in it.
                    string? built = SliceLabelBuilder(lctx);
                    labelText = built ?? defaultLabel;
                }

                var labelPaint = ShapePaint(ctx);
                var dlc = GetDataLabelColor(ctx);
                labelPaint.SetColor(dlc with { A = dlc.A * opacity });
                DrawTextCentered(ctx, labelPaint, labelText, lx, ly, FontSettings.Default);
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

            var paint = ShapePaint(ctx);
            for (int j = 0; j < richLines.Count; j++)
            {
                float ly = startY + j * lineHeight;
                var spans = richLines[j].Spans;

                // Measure the whole line first, then lay the spans out from its left edge: the spans
                // used to be drawn at the same x, so "icon + value + unit" overlapped into one blob.
                var fonts = new FontSettings[spans.Length];
                float lineWidth = 0f;
                for (int s = 0; s < spans.Length; s++)
                {
                    fonts[s] = ThemedFont(ctx, new FontSettings
                    {
                        Size = j == 0 ? CenterFontSize : CenterSubFontSize,
                        Bold = spans[s].Bold,
                        Italic = spans[s].Italic,
                        Align = TextAlign.Left,
                    });
                    lineWidth += ctx.Canvas.MeasureText(spans[s].Text, fonts[s]).Width;
                }

                float lx = cx - lineWidth / 2f;
                float baseline = ly + ctx.Canvas.MeasureText("0", fonts[0]).Height * 0.35f;
                for (int s = 0; s < spans.Length; s++)
                {
                    paint.SetColor(spans[s].Color ?? GetDataLabelColor(ctx));
                    ctx.Canvas.DrawText(spans[s].Text, lx, baseline, fonts[s], paint);
                    lx += ctx.Canvas.MeasureText(spans[s].Text, fonts[s]).Width;
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

            var paint = ShapePaint(ctx);
            var dlc = GetDataLabelColor(ctx);
            paint.SetColor(dlc);

            var mainFont = new FontSettings { Size = CenterFontSize, Bold = true, Align = TextAlign.Center };
            DrawTextCentered(ctx, paint, lines[0], cx, startY, mainFont);

            if (lines.Length > 1)
            {
                var subFont = new FontSettings { Size = CenterSubFontSize, Align = TextAlign.Center };
                for (int j = 1; j < lines.Length; j++)
                {
                    float ly = startY + j * subH;
                    DrawTextCentered(ctx, paint, lines[j], cx, ly, subFont);
                }
            }
        }
    }

    /// <inheritdoc />
    public override HitResult? HitTest(MarkContext ctx, Vector2 screenPos)
    {
        float total = EnsureTotal(ctx);
        if (total <= 0) return null;

        var (cx, cy, outerR) = PieGeometry(ctx);
        float innerR = outerR * InnerRadius;

        float dist = PolarGeometry.Distance(cx, cy, screenPos.X, screenPos.Y);
        if (dist > outerR || dist < innerR) return null;

        // Only the part of the sweep the entry animation has already drawn is hittable: the same
        // limit Render clips every arc at.
        float revealed = SweepEnd(ComputeAnimProgress(ctx)) - StartAngle;
        if (revealed <= 0f) return null;

        float relAngle = PolarGeometry.RelativeAngle(
            PolarGeometry.AngleOf(cx, cy, screenPos.X, screenPos.Y), StartAngle);

        float currentAngle = 0f;
        for (int i = 0; i < ctx.Data.Count; i++)
        {
            var row   = ctx.Data[i];
            if (IsSeriesHidden(ctx, row)) continue;
            var yResolved = ctx.Encodes.Resolve(YChannel, row);
            if (yResolved == null) continue;
            float val = ToSingle(yResolved, "Y");
            // Mirror Render: non-finite and non-positive values have no slice, and the sweep is capped
            // just below a full turn so a lone slice is not reported as a zero-width one.
            if (!float.IsFinite(val) || val <= 0f) continue;
            float sweep = MathF.Min((val / total) * MathF.Tau, PolarGeometry.FullTurnCap);

            if (currentAngle >= revealed) break;
            float clippedSweep = MathF.Min(sweep, revealed - currentAngle);

            if (relAngle >= currentAngle && relAngle < currentAngle + clippedSweep)
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
                    // Same key Render resolves for this row (Color channel), so hover/focus agrees.
                    SeriesKey = ResolveSeriesKey(ctx, row),
                    MarkType = nameof(PieMark),
                };
            }
            currentAngle += sweep;
        }
        return null;
    }
}
