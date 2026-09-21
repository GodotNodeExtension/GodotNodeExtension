using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using GodotNodeExtension.Component.GodotChart.Canvas;

namespace GodotNodeExtension.Component.GodotChart;

// ── Tooltip RichText types ───────────────────────────────────

/// <summary>
/// Built-in icon shapes for tooltip series indicators.
/// Rendered as vector graphics using IPath2D.
/// </summary>
public enum TooltipIcon
{
    /// <summary>No icon.</summary>
    None,

    /// <summary>Filled circle indicator.</summary>
    Circle,

    /// <summary>Filled square indicator.</summary>
    Square,

    /// <summary>Diamond (rotated square) indicator.</summary>
    Diamond,

    /// <summary>Upward-pointing triangle indicator.</summary>
    Triangle,
}

/// <summary>
/// A styled text span within a tooltip line.
/// Supports color, bold, italic, decoration, font size, letter spacing,
/// font family, Godot font, vector icons, and bitmap images.
/// </summary>
public readonly struct TooltipSpan()
{
    /// <summary>Text content of the span.</summary>
    public string Text { get; init; } = "";

    /// <summary>Text color override. Null uses the default tooltip text color.</summary>
    public Color? Color { get; init; }

    /// <summary>Whether to render in bold.</summary>
    public bool Bold { get; init; }

    /// <summary>Whether to render in italic.</summary>
    public bool Italic { get; init; }

    /// <summary>Font size override. Null uses the default tooltip font size.</summary>
    public float? FontSize { get; init; }

    /// <summary>Text decoration (underline, strikethrough, or both).</summary>
    public TextDecoration Decoration { get; init; }

    /// <summary>Extra spacing between characters in logical pixels. Default 0.</summary>
    public float LetterSpacing { get; init; }

    /// <summary>
    /// Font family name override (e.g. "Arial", "Consolas").
    /// Null uses the default tooltip font.
    /// </summary>
    public string? Family { get; init; }

    /// <summary>
    /// Godot Font resource override. When set, takes priority over <see cref="Family"/>.
    /// </summary>
    public Font? GodotFont { get; init; }

    /// <summary>
    /// Vector icon to draw before this span's text. The icon uses the span's Color.
    /// Typical usage: series color indicator dot before series name.
    /// </summary>
    public TooltipIcon Icon { get; init; }

    /// <summary>
    /// Bitmap image icon to draw before this span's text.
    /// Takes priority over the vector Icon if both are set.
    /// The image is scaled to fit the line height.
    /// </summary>
    public IImageHandle? Image { get; init; }

    /// <summary>
    /// Convert this span's font properties into a <see cref="FontSettings"/> instance.
    /// </summary>
    /// <param name="defaultFontSize">Fallback font size when <see cref="FontSize"/> is null.</param>
    public FontSettings ToFontSettings(float defaultFontSize) => new()
    {
        Size = FontSize ?? defaultFontSize,
        Bold = Bold,
        Italic = Italic,
        Decoration = Decoration,
        LetterSpacing = LetterSpacing,
        Family = Family,
        GodotFont = GodotFont,
    };
}

/// <summary>
/// A single line of tooltip content, composed of styled spans.
/// </summary>
public struct TooltipLine()
{
    /// <summary>The spans composing this line. Never null (defaults to an empty array).</summary>
    public TooltipSpan[] Spans { get; init; } = [];

    /// <summary>Create a plain text line (convenience). Null text becomes an empty string.</summary>
    public static TooltipLine Plain(string? text) =>
        new() { Spans = [new TooltipSpan { Text = text ?? "" }] };

    /// <summary>Create a line with a colored vector icon indicator and label text.</summary>
    public static TooltipLine WithIcon(TooltipIcon icon, Color color, string text) =>
        new() { Spans = [new TooltipSpan { Text = text, Color = color, Icon = icon }] };
}

/// <summary>
/// Result of a hit test against chart elements.
/// </summary>
public class HitResult
{
    /// <summary>Whether a chart element was hit.</summary>
    public bool Hit { get; init; }

    /// <summary>The data row of the hit element.</summary>
    public DataRow? Row { get; init; }

    /// <summary>Index of the hit data row.</summary>
    public int RowIndex { get; init; }

    /// <summary>Screen X coordinate of the element center.</summary>
    public float ScreenX { get; init; }

    /// <summary>Screen Y coordinate of the element center.</summary>
    public float ScreenY { get; init; }

    /// <summary>Tooltip label text.</summary>
    public string? Label { get; init; }

    /// <summary>The series key the element belongs to.</summary>
    public string? SeriesKey { get; init; }

    /// <summary>The mark type identifier (e.g. "IntervalMark").</summary>
    public string? MarkType { get; init; }

    /// <summary>Resolved fill color of the hit element.</summary>
    public Color ElementColor { get; init; }

    /// <summary>
    /// The focused series key after this interaction.
    /// Set by <see cref="Chart.HandleClick"/> when a legend item is clicked.
    /// Null means no series is focused.
    /// </summary>
    public string? FocusedSeries { get; set; }

    /// <summary>
    /// Rich tooltip content produced by the hit mark's <see cref="Mark.TooltipContentBuilder"/>.
    /// When set, the tooltip renderer uses it instead of building content from the label.
    /// </summary>
    public IReadOnlyList<TooltipLine>? TooltipLines { get; set; }
}

/// <summary>
/// Event args for chart element click events.
/// </summary>
public class ChartClickEventArgs : EventArgs
{
    /// <summary>The clicked data row with all field values.</summary>
    public DataRow? Row { get; init; }

    /// <summary>Index of the clicked row in the dataset.</summary>
    public int RowIndex { get; init; }

    /// <summary>
    /// Mouse button that produced the click. Only <see cref="MouseButton.Left"/> changes the selection and
    /// the focused series - a host can use the other buttons for its own gestures (a context menu, drilling
    /// back out of a hierarchy, ...) without the chart reacting to them.
    /// </summary>
    public MouseButton Button { get; init; } = MouseButton.Left;

    /// <summary>The mark type that was clicked (e.g. "IntervalMark").</summary>
    public string? MarkType { get; init; }

    /// <summary>Screen position of the click.</summary>
    public Vector2 ScreenPosition { get; init; }

    /// <summary>The series key the clicked element belongs to.</summary>
    public string? SeriesKey { get; init; }
}

/// <summary>
/// Context information passed to custom tooltip builders, containing
/// selection/hover details beyond the raw DataRow.
/// </summary>
public class TooltipContext
{
    /// <summary>The data row of the hovered element.</summary>
    public DataRow Row { get; init; } = null!;

    /// <summary>Index of the hovered data row.</summary>
    public int RowIndex { get; init; }

    /// <summary>The mark type that produced this hit (e.g. "IntervalMark").</summary>
    public string? MarkType { get; init; }

    /// <summary>The series key the element belongs to.</summary>
    public string? SeriesKey { get; init; }

    /// <summary>Resolved fill color of the hovered element.</summary>
    public Color ElementColor { get; init; }

    /// <summary>Tooltip label from HitTest.</summary>
    public string? Label { get; init; }
}

/// <summary>
/// Context passed to custom label/content builder callbacks.
/// Provides data and metadata about the element being labeled.
/// </summary>
public class LabelContext
{
    /// <summary>All data rows relevant to this label (e.g. all rows for a pie/donut center).</summary>
    public IReadOnlyList<DataRow> Data { get; init; } = [];

    /// <summary>The specific data row for single-item labels (pie slice, bar, point).</summary>
    public DataRow? Row { get; init; }

    /// <summary>Index of the data row. -1 if not applicable.</summary>
    public int RowIndex { get; init; } = -1;

    /// <summary>Resolved fill color of the element.</summary>
    public Color ElementColor { get; init; }

    /// <summary>The series key this label belongs to.</summary>
    public string? SeriesKey { get; init; }

    /// <summary>Default label text that would be rendered without customization.</summary>
    public string? DefaultText { get; init; }

    /// <summary>Numeric value of the element (if applicable).</summary>
    public float Value { get; init; }

    /// <summary>Percentage of element value relative to total (e.g. pie slice ratio). Range [0, 1].</summary>
    public float Percentage { get; init; }
}

/// <summary>
/// Configuration options for tooltip rendering behavior and content.
/// </summary>
public class TooltipOptions
{
    /// <summary>
    /// Rich content builder function. Receives a TooltipContext with full selection info
    /// and returns structured lines with styled spans.
    /// Takes priority over ContentBuilder if both are set.
    /// </summary>
    public Func<TooltipContext, IReadOnlyList<TooltipLine>>? RichContentBuilder { get; set; }

    /// <summary>
    /// Plain text content builder function. Receives a TooltipContext with full selection info
    /// and returns lines of text to display in the tooltip.
    /// If null and RichContentBuilder is also null, default content (label) is used.
    /// </summary>
    public Func<TooltipContext, IReadOnlyList<string>>? ContentBuilder { get; set; }

    /// <summary>Background color of the tooltip bubble. Null = use theme default.</summary>
    public Color? BackgroundColor { get; set; }

    /// <summary>Text color inside the tooltip. Null = use theme default.</summary>
    public Color? TextColor { get; set; }

    /// <summary>Corner radius for tooltip rounded rect. Null = use the theme value.</summary>
    public float? CornerRadius { get; set; }

    /// <summary>Padding inside the tooltip bubble. Null = use the theme value.</summary>
    public float? Padding { get; set; }

    /// <summary>Border color drawn around the tooltip. Null = use theme default.</summary>
    public Color? BorderColor { get; set; }

    /// <summary>Border width. Null = use the theme value.</summary>
    public float? BorderWidth { get; set; }

    /// <summary>Font size for tooltip text. Null uses theme default (12f fallback).</summary>
    public float? FontSize { get; set; }
}

/// <summary>
/// Renders tooltip with smooth follow and fade animation.
/// </summary>
public class TooltipRenderer
{
    private Vector2 _currentPos;
    private Vector2 _targetPos;
    private float _opacity;
    private float _targetOpacity;
    private HitResult? _currentHit;

    /// <summary>Smoothing factor for position interpolation. Lower = smoother.</summary>
    public float SmoothSpeed { get; } = 12f;

    /// <summary>Fade speed in units per second.</summary>
    public float FadeSpeed { get; } = 8f;

    /// <summary>Whether the tooltip is currently visible (opacity > 0).</summary>
    public bool IsVisible => _opacity > 0.01f;

    /// <summary>Tooltip rendering options.</summary>
    public TooltipOptions Options { get; } = new();

    /// <summary>
    /// Chart theme for fallback colors when <see cref="TooltipOptions"/> values are null.
    /// Null falls back to the shared <see cref="ChartTheme.Default"/> instance.
    /// </summary>
    public ChartTheme? Theme { get; set; }

    /// <summary>
    /// Update tooltip state each frame.
    /// </summary>
    public void Update(float delta, HitResult? hit)
    {
        // Clamp delta to avoid jumps after window minimize/restore
        delta = MathF.Min(delta, 0.1f);

        _currentHit = hit;
        if (hit is { Hit: true })
        {
            _targetPos = new Vector2(hit.ScreenX, hit.ScreenY);
            _targetOpacity = 1f;
        }
        else
        {
            _targetOpacity = 0f;
        }

        // Smooth position follow (exponential lerp)
        _currentPos = _currentPos.Lerp(_targetPos, 1f - MathF.Exp(-SmoothSpeed * delta));

        // Fade in/out
        _opacity = Mathf.MoveToward(_opacity, _targetOpacity, FadeSpeed * delta);
    }

    /// <summary>
    /// Draw tooltip on canvas with current opacity and position.
    /// Supports RichContentBuilder (styled spans), ContentBuilder (plain text), and default label.
    /// </summary>
    public void Draw(ICanvas2D canvas, int canvasW, int canvasH)
    {
        if (_opacity <= 0.01f || _currentHit is not { Hit: true }) return;

        // The attached theme supplies every value the caller did not override; an unthemed tooltip
        // reads the shared <see cref="ChartTheme.Default"/> instance, so the built-in bubble metrics
        // and colors live in exactly one place (the theme's own initializers).
        var theme = Theme ?? ChartTheme.Default;

        float fontSize = Options.FontSize ?? theme.TooltipFontSize;
        float lineHeight = fontSize + theme.TooltipLineSpacing;
        float pad = Options.Padding ?? theme.TooltipPadding;
        float cornerRadius = Options.CornerRadius ?? theme.TooltipCornerRadius;
        float borderWidth = Options.BorderWidth ?? theme.TooltipBorderWidth;
        float alpha = _opacity;

        var bgColor = Options.BackgroundColor ?? theme.TooltipBackground;
        var borderColor = Options.BorderColor ?? theme.TooltipBorderColor;
        var textColor = Options.TextColor ?? theme.TooltipTextColor;

        // Resolve content: RichContentBuilder > ContentBuilder > default
        var richLines = ResolveRichLines(_currentHit);
        if (richLines.Count == 0) return;

        // Measure box size
        float iconInset = fontSize + 2f; // space for icon before text
        float maxLineW = 0f;
        foreach (var line in richLines)
        {
            float lineW = MeasureLineWidth(canvas, line, fontSize, iconInset);
            maxLineW = MathF.Max(maxLineW, lineW);
        }

        float boxW = maxLineW + pad * 2;
        float boxH = richLines.Count * lineHeight + pad * 2;

        // Position: prefer above-right of current smooth position
        float offsetX = theme.TooltipOffsetX;
        float offsetY = theme.TooltipOffsetY;
        float flipY   = theme.TooltipFlipOffsetY;
        float margin  = theme.TooltipEdgeMargin;
        float bx = _currentPos.X + offsetX;
        float by = _currentPos.Y - boxH - offsetY;

        // Horizontal flip: if overflows right, try left side
        if (bx + boxW > canvasW) bx = _currentPos.X - boxW - offsetX;
        // Clamp to left edge
        if (bx < margin) bx = margin;

        // Vertical flip: if overflows top, place below cursor
        if (by < margin) by = _currentPos.Y + flipY;
        // If also overflows bottom, clamp to bottom edge
        if (by + boxH > canvasH) by = canvasH - boxH - margin;
        // Final clamp to top edge
        if (by < margin) by = margin;

        // Background
        using var bgPath = canvas.CreatePath();
        using var bgPaint = canvas.CreatePaint();
        bgPath.RoundRect(bx, by, boxW, boxH, cornerRadius);
        bgPaint.SetColor(bgColor).SetOpacity(alpha);
        canvas.Fill(bgPath, bgPaint);

        // Border
        using var borderPaint = canvas.CreatePaint();
        borderPaint.SetColor(new Color(
            borderColor.R,
            borderColor.G,
            borderColor.B,
            borderColor.A * alpha)).SetStrokeWidth(borderWidth);
        canvas.Stroke(bgPath, borderPaint);

        // Draw each line with spans
        for (int i = 0; i < richLines.Count; i++)
        {
            float ty = by + pad + fontSize - 2f + i * lineHeight;
            float tx = bx + pad;
            DrawRichLine(canvas, richLines[i], tx, ty, fontSize, iconInset, alpha, textColor);
        }
    }

    /// <summary>Context describing a hit, shared by mark-level builders and the tooltip renderer.</summary>
    internal static TooltipContext? BuildTooltipContext(HitResult hit)
        => hit.Row != null ? new TooltipContext
        {
            Row = hit.Row,
            RowIndex = hit.RowIndex,
            MarkType = hit.MarkType,
            SeriesKey = hit.SeriesKey,
            ElementColor = hit.ElementColor,
            Label = hit.Label,
        } : null;

    private IReadOnlyList<TooltipLine> ResolveRichLines(HitResult hit)
    {
        // Build context for custom builders
        var ctx = BuildTooltipContext(hit);

        // 0. Per-mark content (Mark.TooltipContentBuilder), attached to the hit while hit testing
        if (hit.TooltipLines is { Count: > 0 } markLines)
            return SplitNewlines(markLines);

        // 1. RichContentBuilder
        if (Options.RichContentBuilder != null && ctx != null)
        {
            // One call: the builder belongs to the host, so running it twice would repeat its side
            // effects (and pay for its work twice) just to test the result for null.
            var lines = Options.RichContentBuilder(ctx);
            return lines is { } built ? built : [];
        }

        // 2. ContentBuilder → wrap as plain TooltipLines
        if (Options.ContentBuilder != null && ctx != null)
        {
            var content = Options.ContentBuilder(ctx);
            return SplitNewlines((content is { } items ? items : Array.Empty<string>()).Select(TooltipLine.Plain));
        }

        // 3. Default from label
        if (string.IsNullOrEmpty(hit.Label)) return [];
        return SplitNewlines([TooltipLine.Plain(hit.Label)]);
    }

    /// <summary>
    /// Turn each embedded newline of every span into its own line. The tooltip box height is
    /// computed from the line count, so a richer builder that returns "a\nb" in one line used to
    /// overflow its background; splitting here keeps measurement and drawing consistent.
    /// </summary>
    private static TooltipLine[] SplitNewlines(IEnumerable<TooltipLine> lines)
    {
        var result = new List<TooltipLine>();
        foreach (var line in lines)
        {
            var spans = line.Spans;
            if (spans.Length == 0)
            {
                result.Add(line with { Spans = [] });
                continue;
            }

            var current = new List<TooltipSpan>();
            bool wrapped = false;
            foreach (var span in spans)
            {
                string[] parts = (span.Text != null ? span.Text : "").Split('\n');
                for (int i = 0; i < parts.Length; i++)
                {
                    if (i > 0)
                    {
                        result.Add(line with { Spans = [.. current] });
                        current.Clear();
                        wrapped = true;
                    }
                    if (parts[i].Length > 0 || parts.Length == 1)
                        current.Add(span with { Text = parts[i] });
                }
            }
            if (current.Count > 0 || !wrapped)
                result.Add(line with { Spans = [.. current] });
        }
        return [.. result];
    }

    private static float MeasureLineWidth(ICanvas2D canvas, TooltipLine line, float fontSize, float iconInset)
    {
        float width = 0f;
        foreach (var span in line.Spans)
        {
            // Must use the same condition as DrawRichLine, otherwise the tooltip background is
            // sized for an icon that is never drawn (or vice versa).
            if (HasVisibleIcon(canvas, span))
                width += iconInset;
            width += canvas.MeasureText(span.Text, span.ToFontSettings(fontSize)).Width;
        }
        return width;
    }

    /// <summary>
    /// Whether a span's icon is actually drawn by <see cref="DrawRichLine"/>: raster images require
    /// the backend to support them, vector icons always draw.
    /// </summary>
    private static bool HasVisibleIcon(ICanvas2D canvas, TooltipSpan span)
        => (span.Image != null && canvas.Capabilities.SupportsImages) || span.Icon != TooltipIcon.None;

    private void DrawRichLine(ICanvas2D canvas, TooltipLine line, float x, float y,
                              float fontSize, float iconInset, float alpha, Color textColor)
    {
        float cx = x;
        // One paint for the whole line: the colour is the only thing that changes per span, and a
        // tooltip can hold dozens of them.
        using var paint = canvas.CreatePaint();
        foreach (var span in line.Spans)
        {
            var spanColor = span.Color ?? textColor;
            // The theme font is the fallback for spans that do not name one, so a chart-wide font also
            // covers tooltip text (which is why CJK tooltips used to show boxes).
            var spanFont = span.ToFontSettings(fontSize) with
            {
                GodotFont = span.GodotFont ?? Theme?.Font,
            };

            // Draw image icon (priority) or vector icon
            if (span.Image != null && canvas.Capabilities.SupportsImages)
            {
                float iconSize = spanFont.Size;
                canvas.DrawImage(span.Image, cx, y - spanFont.Size + 2f, iconSize, iconSize, alpha);
                cx += iconInset;
            }
            else if (span.Icon != TooltipIcon.None)
            {
                DrawVectorIcon(canvas, span.Icon, cx + spanFont.Size * 0.3f, y - spanFont.Size * 0.3f,
                               spanFont.Size * 0.3f, spanColor, alpha);
                cx += iconInset;
            }

            // Draw text
            paint.SetColor(spanColor).SetOpacity(alpha);
            canvas.DrawText(span.Text, cx, y, spanFont, paint);
            cx += canvas.MeasureText(span.Text, spanFont).Width;
        }
    }

    private static void DrawVectorIcon(ICanvas2D canvas, TooltipIcon icon,
                                       float cx, float cy, float r,
                                       Color color, float alpha)
    {
        using var path = canvas.CreatePath();
        using var paint = canvas.CreatePaint();
        paint.SetColor(color).SetOpacity(alpha).SetAntiAlias(true);

        switch (icon)
        {
            case TooltipIcon.Circle:
                path.Circle(cx, cy, r);
                break;
            case TooltipIcon.Square:
                path.Rect(cx - r, cy - r, r * 2f, r * 2f);
                break;
            case TooltipIcon.Diamond:
                path.MoveTo(cx, cy - r);
                path.LineTo(cx + r, cy);
                path.LineTo(cx, cy + r);
                path.LineTo(cx - r, cy);
                path.Close();
                break;
            case TooltipIcon.Triangle:
                path.MoveTo(cx, cy - r);
                path.LineTo(cx + r, cy + r);
                path.LineTo(cx - r, cy + r);
                path.Close();
                break;
        }

        canvas.Fill(path, paint);
    }
}

/// <summary>
/// Provides crosshair and utility methods for chart interaction.
/// </summary>
public static class ChartInteraction
{
    /// <summary>
    /// Draw a crosshair (dashed vertical + horizontal lines) at the mouse position.
    /// Only draws when the mouse is inside the plot area.
    /// <para>
    /// <see cref="DefaultRenderers.DrawCrosshair"/> is the adapter that hands this to a chart's
    /// <see cref="Chart.CrosshairRenderer"/> slot, so a custom slot can keep the built-in look and add to it.
    /// </para>
    /// </summary>
    public static void DrawCrosshair(
        ICanvas2D canvas, Vector2 mousePos, PlotArea plot, ChartTheme? theme = null)
    {
        if (mousePos.X < plot.X || mousePos.X > plot.X + plot.Width ||
            mousePos.Y < plot.Y || mousePos.Y > plot.Y + plot.Height)
            return;

        // Crosshair colours come from the theme; an unthemed call uses the shared default instance
        // instead of allocating a fresh theme per frame.
        var t = theme ?? ChartTheme.Default;
        using var paint = canvas.CreatePaint();
        paint.SetColor(t.CrosshairColor)
             .SetStrokeWidth(t.CrosshairStrokeWidth)
             .SetLineDash([t.CrosshairDashLength, t.CrosshairDashLength]);

        // Vertical line
        canvas.DrawLine(mousePos.X, plot.Y, mousePos.X, plot.Y + plot.Height, paint);
        // Horizontal line
        canvas.DrawLine(plot.X, mousePos.Y, plot.X + plot.Width, mousePos.Y, paint);
    }

    /// <summary>
    /// Run hit test against all marks and return the first hit.
    /// Marks are tested from top-most to bottom-most (the mark drawn last wins), marks that the
    /// renderer skipped (incompatible coordinate system) are ignored, and a mark that carries its
    /// own data is tested against that data — exactly like the renderer does.
    /// <para>
    /// This is the mark-only half: <see cref="Chart.HitTest"/> adds the legend and the axis bands on
    /// top (and is what a host normally calls); this overload exists for a caller that holds the marks
    /// itself and wants only them.
    /// </para>
    /// </summary>
    public static HitResult? TestAll(
        List<Mark> marks, MarkContext ctx, Vector2 mousePos, IReadOnlySet<Mark>? skipped = null)
    {
        // Marks may bind channels themselves; the chart's set stays the base for every one of them.
        var chartEncodes = ctx.Encodes;
        int layoutVersion = ctx.LayoutVersion;

        for (int i = marks.Count - 1; i >= 0; i--)
        {
            var mark = marks[i];
            if (skipped != null && skipped.Contains(mark)) continue;

            var markCtx = mark.BindEncodes(
                mark.Data != null ? ctx.WithData(mark.Data) : ctx, chartEncodes, layoutVersion);
            var result = mark.HitTest(markCtx, mousePos);
            if (result is not { Hit: true }) continue;

            // The mark's own tooltip builder travels with the hit, so the tooltip renderer can use it
            // without knowing which mark produced the hit.
            if (mark.TooltipContentBuilder != null && result.TooltipLines == null)
            {
                var tooltipCtx = TooltipRenderer.BuildTooltipContext(result);
                if (tooltipCtx != null)
                    result.TooltipLines = mark.TooltipContentBuilder(tooltipCtx);
            }

            return result;
        }
        return null;
    }
}

