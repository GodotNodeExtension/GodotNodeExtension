using System;
using Godot;
using GodotNodeExtension.Component.GodotChart.Canvas;

namespace GodotNodeExtension.Component.GodotChart.Marks;

/// <summary>
/// A reference line or band drawn over the plot - what other charting libraries call "sections": a level a
/// reader compares the series against (a target, a threshold, a historical average) or a band between two
/// levels.
/// <para>
/// It is an annotation, not data: the values are mapped through the axis it names, they do not contribute to
/// any scale (a level far outside the table must not stretch the axis) and the mark never appears in the
/// legend. Zooming and panning move the lines with the data, and a level outside the visible window is
/// skipped instead of being pinned to the edge.
/// </para>
/// <para>
/// <see cref="Mark.LabelFormat"/> labels the levels: {0} is the level and {1} is empty, because a section
/// spans the whole other axis and has no single value there. An empty format draws no labels at all; leaving
/// the property alone keeps the inherited <c>"{0}"</c>.
/// </para>
/// </summary>
public sealed class SectionMark : Mark
{
    /// <summary>Values to draw a line at, on <see cref="Target"/>. Empty draws nothing.</summary>
    public double[] Levels { get; init; } = [];

    /// <summary>Lower end of the band, with <see cref="BandTo"/>; the band needs both.</summary>
    public double? BandFrom { get; init; }

    /// <summary>Upper end of the band, with <see cref="BandFrom"/>.</summary>
    public double? BandTo { get; init; }

    /// <summary>Axis the levels belong to: <see cref="Channel.Y"/> (horizontal, the default), Y2 or X (vertical).</summary>
    public Channel Target { get; init; } = Channel.Y;

    /// <summary>Line width in pixels.</summary>
    public float Thickness { get; init; } = 1f;

    /// <summary>Draw the lines dashed (default) or solid.</summary>
    public bool Dashed { get; init; } = true;

    /// <summary>Length of one dash in pixels, when <see cref="Dashed"/>.</summary>
    public float DashLength { get; init; } = 6f;

    /// <summary>Gap between dashes in pixels, when <see cref="Dashed"/>.</summary>
    public float DashGap { get; init; } = 4f;

    /// <summary>Line and band colour; null uses the theme's grid colour.</summary>
    public Color? Color { get; init; }

    /// <summary>Opacity of the band fill (the lines use the colour as it is).</summary>
    public float BandOpacity { get; init; } = 0.12f;

    // The label format is the one <see cref="Mark"/> already declares: a reference line labels its level the
    // same way any other mark labels a value, and a second property would only hide the inherited one.

    /// <summary>Distance of a label from the plot area's left (or top) edge, in pixels.</summary>
    public float LabelInset { get; init; } = 6f;

    /// <summary>
    /// True: a section draws lines, a band and their labels - annotation, with no hover or selection look of its
    /// own. Declaring it lets the chart keep its data layer cached on a page that shows reference lines (the
    /// safety valve disables the cache for any mark that has not made the split). The one thing it gives up is
    /// the focus dimming a data mark applies through
    /// <see cref="Mark.ComputeElementOpacity(MarkContext, DataRow, int, float)"/>: the line stays at its default
    /// appearance, which for a reference line is what a reader expects anyway.
    /// </summary>
    public override bool InteractionStateInOverlay => true;

    /// <inheritdoc />
    public override MarkCoordinate Coordinate => MarkCoordinate.Cartesian;

    /// <inheritdoc />
    public override void Render(MarkContext ctx)
    {
        if (ctx.Scales.TryGet(Target) is not { } scale) return;

        var theme = ctx.Theme ?? ChartTheme.Default;
        Color colour = Color ?? theme.GridColor;
        bool vertical = Target == Channel.X;

        DrawBand(ctx, scale, colour, vertical);
        foreach (double level in Levels) DrawLevel(ctx, scale, colour, vertical, level);
    }

    /// <summary>Map a level: false when the scale cannot read it or it sits outside the visible window.</summary>
    private static bool TryMap(IScale scale, double value, out double norm)
    {
        norm = scale.Map(value);
        return double.IsFinite(norm) && norm >= 0.0 && norm <= 1.0;
    }

    /// <summary>The band between <see cref="BandFrom"/> and <see cref="BandTo"/>, drawn under the lines.</summary>
    private void DrawBand(MarkContext ctx, IScale scale, Color colour, bool vertical)
    {
        if (BandFrom is not { } from || BandTo is not { } to) return;

        double lo = Math.Min(from, to), hi = Math.Max(from, to);
        if (!TryMap(scale, lo, out double normLo) || !TryMap(scale, hi, out double normHi)) return;

        var plot = ctx.Plot;
        float a = vertical ? plot.MapX(normLo) : plot.MapY(normLo);
        float b = vertical ? plot.MapX(normHi) : plot.MapY(normHi);

        // A rectangle built from lines: the canvas abstraction has MoveTo/LineTo everywhere, and this keeps the
        // mark independent of any rounded-rect helper.
        var path = ShapePath(ctx);
        if (vertical)
        {
            path.Reset().MoveTo(a, plot.Y).LineTo(b, plot.Y).LineTo(b, plot.Y + plot.Height)
                .LineTo(a, plot.Y + plot.Height).Close();
        }
        else
        {
            path.Reset().MoveTo(plot.X, a).LineTo(plot.X + plot.Width, a)
                .LineTo(plot.X + plot.Width, b).LineTo(plot.X, b).Close();
        }

        using var paint = ctx.Canvas.CreatePaint();
        paint.SetColor(colour).SetOpacity(Math.Clamp(BandOpacity, 0f, 1f));
        ctx.Canvas.Fill(path, paint);
    }

    /// <summary>One level: a dashed or solid line across the plot, plus its label when a format is given.</summary>
    private void DrawLevel(MarkContext ctx, IScale scale, Color colour, bool vertical, double level)
    {
        if (!TryMap(scale, level, out double norm)) return;

        var plot = ctx.Plot;
        float at = vertical ? plot.MapX(norm) : plot.MapY(norm);
        float from = vertical ? plot.Y : plot.X;
        float to = vertical ? plot.Y + plot.Height : plot.X + plot.Width;

        var paint = ShapePaint(ctx);
        paint.SetColor(colour).SetStrokeWidth(Thickness).SetAntiAlias(true);

        if (Dashed)
        {
            float dash = MathF.Max(1f, DashLength), gap = MathF.Max(1f, DashGap);
            for (float cursor = from; cursor < to; cursor += dash + gap)
            {
                float end = MathF.Min(cursor + dash, to);
                if (vertical) ctx.Canvas.DrawLine(at, cursor, at, end, paint);
                else ctx.Canvas.DrawLine(cursor, at, end, at, paint);
            }
        }
        else if (vertical)
        {
            ctx.Canvas.DrawLine(at, from, at, to, paint);
        }
        else
        {
            ctx.Canvas.DrawLine(from, at, to, at, paint);
        }

        if (string.IsNullOrEmpty(LabelFormat)) return;

        var theme = ctx.Theme ?? ChartTheme.Default;
        float size = theme.LabelFontSize > 0f ? theme.LabelFontSize : FontSettings.Default.Size;
        var font = new FontSettings
        {
            Size = size,
            Family = theme.FontFamily,
            GodotFont = theme.Font,
            Align = TextAlign.Left,
        };

        using var labelPaint = ctx.Canvas.CreatePaint();
        labelPaint.SetColor(colour).SetOpacity(0.9f);

        // The base format helper: it accepts both placeholders, so a format written the way Mark.LabelFormat is
        // documented ("{0}: {1}") is formatted instead of throwing on the single argument this used to pass
        // (which cost the label of the stage, and with it the rest of what the stage had to draw). A section
        // spans the other axis and has no single value for {1}, so that one comes out empty.
        string text = FormatLabel(LabelFormat, level, null);
        if (vertical) ctx.Canvas.DrawText(text, at + LabelInset, plot.Y + size + 2f, font, labelPaint);
        else ctx.Canvas.DrawText(text, plot.X + LabelInset, at - 3f, font, labelPaint);
    }
}
