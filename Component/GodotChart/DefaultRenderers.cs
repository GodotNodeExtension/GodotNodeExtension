using System;
using System.Collections.Generic;
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

    /// <summary>
    /// Label a linear axis with the values the scale was fitted with, spread across the window. Returns false
    /// when there is nothing to sample - a domain fitted by hand, or a window holding fewer than two of those
    /// values - so the caller falls back to the arithmetic step.
    /// </summary>
    private static bool TryAddFittedTicks(LinearScale scale, int maxTicks, int minTicks, double? forcedStep, List<(double Norm, string Text)> ticks)
    {
        var values = scale.FittedValues;
        if (values.Count < 2) return false;

        int first = LowerBound(values, scale.Min);
        int last = UpperBound(values, scale.Max);
        if (last - first < 2) return false;

        // The step between ticks is a whole multiple - 10, 5, 2, 1, 0.5 ... - picked so the labels fill the
        // axis without exceeding maxTicks, and refined (10 -> 5 -> 1) while the axis is long enough for more:
        // a year axis therefore reads 1970/1980/1990, then 1970/1975/1980, then every year, never 1970/1977.
        double span = values[last - 1] - values[first];
        double step = forcedStep ?? NiceStep(span / Math.Max(1, maxTicks - 1));
        if (forcedStep is null)
        {
            for (int guard = 0; guard < 8 && CountMultiples(values, first, last, step) > maxTicks; guard++)
                step = NextCoarser(step);
            for (int guard = 0; guard < 8 && minTicks > 0 && CountMultiples(values, first, last, step) < minTicks; guard++)
                step = NextFiner(step);
        }

        // Every tick is a value the table really holds: a multiple the data skips snaps to the nearest one.
        double next = Math.Ceiling(values[first] / step) * step;
        int cursor = first;
        while (next <= values[last - 1] + step * 1e-9 && ticks.Count < maxTicks)
        {
            while (cursor < last - 1 && Math.Abs(values[cursor + 1] - next) <= Math.Abs(values[cursor] - next)) cursor++;
            // .Equals, not ==: the values come from the same sorted list and a repeat has to be dropped exactly
            // (a tolerance would merge ticks the axis really has).
            if (ticks.Count == 0 || !values[cursor].Equals(TicksValues[ticks.Count - 1]))
            {
                ticks.Add((scale.Map(values[cursor]), scale.Format(values[cursor])));
                TicksValues.Add(values[cursor]);
            }
            next += step;
        }
        TicksValues.Clear();

        return ticks.Count > 0;
    }

    /// <summary>Scratch list for the de-duplication in <see cref="TryAddFittedTicks"/> (per call, no alloc).</summary>
    private static readonly List<double> TicksValues = [];

    /// <summary>Ticks the ladder step would produce inside the window, without building them.</summary>
    private static int CountMultiples(IReadOnlyList<double> values, int first, int last, double step)
    {
        if (!(step > 0)) return 0;
        double from = Math.Ceiling(values[first] / step) * step;
        double to = values[last - 1];
        return from > to ? 0 : (int)Math.Floor((to - from) / step) + 1;
    }

    /// <summary>The whole multiple a desired spacing rounds down to: 1, 2, 5, 10, 20, 50 ...</summary>
    private static double NiceStep(double desired)
    {
        if (!(desired > 0) || double.IsInfinity(desired)) return 1;

        double magnitude = Math.Pow(10, Math.Floor(Math.Log10(desired)));
        double normalized = desired / magnitude;
        double factor = normalized <= 1 ? 1 : normalized <= 2 ? 2 : normalized <= 5 ? 5 : 10;
        return factor * magnitude;
    }

    /// <summary>One rung finer on the ladder: 10 -> 5 -> 2 -> 1 -> 0.5.</summary>
    private static double NextFiner(double step) => NiceStep(step / 2.05) < step ? NiceStep(step / 2.05) : step / 2;

    /// <summary>One rung coarser on the ladder: 1 -> 2 -> 5 -> 10.</summary>
    private static double NextCoarser(double step) => NiceStep(step * 2.05);

    /// <summary>Index of the first value &gt;= <paramref name="value"/> in a sorted list.</summary>
    private static int LowerBound(IReadOnlyList<double> values, double value)
    {
        int low = 0, high = values.Count;
        while (low < high)
        {
            int mid = (low + high) / 2;
            if (values[mid] < value) low = mid + 1;
            else high = mid;
        }
        return low;
    }

    /// <summary>Index after the last value &lt;= <paramref name="value"/> in a sorted list.</summary>
    private static int UpperBound(IReadOnlyList<double> values, double value)
    {
        int low = 0, high = values.Count;
        while (low < high)
        {
            int mid = (low + high) / 2;
            if (values[mid] <= value) low = mid + 1;
            else high = mid;
        }
        return low;
    }

    /// <summary>
    /// Total horizontal space a Y-axis tick label column needs, for a column whose widest label is
    /// <paramref name="widestLabelWidth"/> pixels wide (0 when there is nothing to label).
    /// <para>
    /// The axis reserves this much and <see cref="DrawAxisLabels"/> derives the width it may draw into
    /// with <see cref="AxisLabelDrawWidth"/> - the two must be the same formula, because they measure
    /// with the same font but from different sides. They used to disagree by a pixel
    /// (<c>widest + 6</c> reserved, <c>widest + YAxisLabelGap + 2</c> spent), so the widest label of
    /// every chart was always trimmed into an ellipsis.
    /// </para>
    /// </summary>
    internal static float AxisLabelReservedWidth(ChartTheme theme, float widestLabelWidth)
        => widestLabelWidth > 0f ? widestLabelWidth + theme.YAxisLabelGap + AxisLabelMargin : 0f;

    /// <summary>
    /// Width left for a Y-axis tick label when <paramref name="reservedWidth"/> pixels are reserved for
    /// the label column (the inverse of <see cref="AxisLabelReservedWidth"/>). A label that is exactly
    /// as wide as the widest measured one therefore still fits, with <see cref="AxisLabelMargin"/> of
    /// slack.
    /// </summary>
    private static float AxisLabelDrawWidth(ChartTheme theme, float reservedWidth)
        => MathF.Max(MinAxisLabelDrawWidth, reservedWidth - theme.YAxisLabelGap - AxisLabelMargin * 0.5f);

    /// <summary>Margin around a tick label: half of it on either side of the label column.</summary>
    private const float AxisLabelMargin = 4f;

    /// <summary>Floor for <see cref="AxisLabelDrawWidth"/>: even a cramped axis gets a readable slot.</summary>
    private const float MinAxisLabelDrawWidth = 16f;

    /// <summary>
    /// Default number of grid/label divisions, used when a scale provides no tick count - the theme's own
    /// default, so the two cannot drift apart.
    /// </summary>
    private const int TimeScaleDefaultTicks = ChartTheme.FallbackTickCountDefault;

    /// <summary>Upper bound for the time axis division count, so a misconfigured theme cannot emit
    /// thousands of labels (and grid lines) in one frame.</summary>
    private const int MaxTimeScaleDivisions = 64;

    /// <summary>
    /// Font for axis/legend/tooltip text, taken from the theme: size
    /// (<see cref="ChartTheme.LabelFontSize"/>), family (<see cref="ChartTheme.FontFamily"/>) and
    /// Godot font resource (<see cref="ChartTheme.Font"/>). These theme fields used to be ignored
    /// by every renderer, so changing the theme had no visible effect.
    /// </summary>
    internal static FontSettings ThemedFont(RenderContext ctx, TextAlign align, float? size = null)
        => new()
        {
            Size = size ?? (ctx.Theme.LabelFontSize > 0f



                ? ctx.Theme.LabelFontSize



                : FontSettings.Default.Size),
            Family = ctx.Theme.FontFamily,
            GodotFont = ctx.Theme.Font,
            Align = align,
        };

    /// <summary>Font for the chart title (theme title size).</summary>
    private static FontSettings TitleFont(RenderContext ctx)
        => ThemedFont(ctx, TextAlign.Left,
            ctx.Theme.TitleFontSize > 0f ? ctx.Theme.TitleFontSize : FontSettings.Default.Size);

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
                            ctx.OffsetY + ctx.Theme.TitleYOffset, TitleFont(ctx), paint);
    }

    /// <summary>
    /// Compute the ticks of an axis scale: normalized position in [0, 1] plus the label text.
    /// Linear scales use their nice tick step (round numbers) constrained to the domain; log
    /// scales use powers of ten; ordinal scales label every category; a time scale divides the
    /// range into <paramref name="fallbackTickCount"/> equal parts (a range without a span
    /// collapses to a single tick).
    /// An empty result means the axis cannot be labelled and is skipped.
    /// </summary>
    /// <param name="scale">Scale to label; null yields no ticks.</param>
    /// <param name="maxTicks">Upper bound on the returned tick count.</param>
    /// <param name="fallbackTickCount">Divisions to use when the scale has no nice step (and the
    /// division count of a time axis).</param>
    /// <param name="minTicks">Fewest ticks the automatic step may end up with (the ladder is refined until
    /// it fits). Zero means no floor.</param>
    /// <param name="forcedStep">Step in data units that overrides the automatic one (<c>AxisConfig.TickStep</c>).</param>
    /// <param name="explicitTicks">Values to draw as they are, over everything else (<c>AxisConfig.Ticks</c>).</param>
    /// <param name="labelFormat">Format string applied to numeric tick labels (<c>AxisConfig.LabelFormat</c>);
    /// null keeps the scale's own text.</param>
    internal static List<(double Norm, string Text)> ComputeTicks(
        IScale? scale, int maxTicks = 32, int fallbackTickCount = TimeScaleDefaultTicks,
        int minTicks = 0, double? forcedStep = null, double[]? explicitTicks = null,
        string? labelFormat = null)
    {
        var ticks = new List<(double, string)>();
        if (scale == null) return ticks;

        if (explicitTicks is { Length: > 0 })
        {
            // A caller who knows the ticks wins: values outside the visible window are dropped rather than
            // pinned to the edge, and everything else is left alone.
            foreach (double value in explicitTicks)
            {
                if (scale is LinearScale linearAxis && (value < linearAxis.Min || value > linearAxis.Max)) continue;
                if (ticks.Count >= maxTicks) break;
                ticks.Add((scale.Map(value), scale.Format(value)));
            }
            return ticks;
        }

        switch (scale)
        {
            case LinearScale linear:
            {
                if (linear.Max <= linear.Min)
                {
                    // Degenerate domain (all values equal): a single tick in the middle.
                    ticks.Add((0.5, linear.Format(linear.Min)));
                    break;
                }
                // Real values first: a linear axis over discrete data (years, sample indices, ids) labels the
                // values the table really has. The step below is only for a domain with nothing to sample
                // from - a continuous range the host pinned by hand, or a window that holds fewer than two
                // real values.
                // Only a channel whose values are sparse uses them as ticks (a yearly axis with 54 values, a
                // small category-like domain). A continuous channel has thousands of distinct values, and
                // sampling a handful of them produces labels clustered in one part of the range - the exact
                // opposite of what an axis is for - so it keeps the arithmetic nice step.
                if (linear.FittedValues.Count <= 64
                    && TryAddFittedTicks(linear, maxTicks, minTicks, forcedStep, ticks)
                    && TicksCoverTheAxis(ticks))
                    break;

                // Sampled values that only cover a corner of the range are not ticks: three labels bunched at the
                // bottom of a tall axis say less than an arithmetic step does. Cleared, so the nice step below
                // starts from an empty list.
                ticks.Clear();

                double step = linear.NiceTicks.Step;
                if (step <= 0 || double.IsNaN(step) || double.IsInfinity(step))
                    step = (linear.Max - linear.Min) / Math.Max(1, fallbackTickCount);
                // Start at the first nice value inside the domain, so no tick leaves the plot.
                double start = Math.Ceiling(linear.Min / step) * step;
                for (double v = start; v <= linear.Max + step * 1e-9 && ticks.Count < maxTicks; v += step)
                    ticks.Add((linear.Map(v), linear.Format(v)));
                break;
            }
            case LogScale log:
            {
                if (log.Min <= 0 || log.Max <= 0) break;
                double from = Math.Floor(Math.Log10(log.Min));
                double to = Math.Ceiling(Math.Log10(log.Max));
                for (double e = from; e <= to && ticks.Count < maxTicks; e += 1)
                {
                    double v = Math.Pow(10, e);
                    if (v < log.Min * (1 - 1e-9) || v > log.Max * (1 + 1e-9)) continue;
                    ticks.Add((log.Map(v), log.Format(v)));
                }
                break;
            }
            case OrdinalScale ordinal:
            {
                // Spread the ticks over the whole domain: taking the first maxTicks entries would label
                // (and grid) only the left part of a long axis, with the labels crammed together.
                var domain = ordinal.Domain;
                int step = domain.Count > maxTicks
                    ? Math.Max(1, (int)Math.Ceiling(domain.Count / (double)maxTicks))
                    : 1;
                for (int i = 0; i < domain.Count; i += step)
                    ticks.Add((ordinal.Map(domain[i]), ordinal.Format(domain[i])));
                break;
            }
            case TimeScale time:
            {
                // The division count comes from the caller (<see cref="ChartTheme.FallbackTickCount"/>,
                // clamped to a sane range) - this branch used to ignore it and always emit
                // TimeScaleDefaultTicks divisions.
                int divisions = Math.Clamp(fallbackTickCount, 1, MaxTimeScaleDivisions);
                if (time.Max <= time.Min)
                {
                    // Empty, single-point or reversed range: label the one position in the middle.
                    // Dividing such a range spread the default tick count of identical labels (and
                    // grid lines) over the whole plot even though the axis covers a single instant.
                    ticks.Add((0.5, time.Format(time.Min)));
                    break;
                }
                long span = time.Max.Ticks - time.Min.Ticks;
                for (int i = 0; i <= divisions && ticks.Count < maxTicks; i++)
                {
                    double t = i / (double)divisions;
                    ticks.Add((t, time.Format(new DateTime(time.Min.Ticks + (long)(span * t)))));
                }
                break;
            }
        }

        // One place reformats the labels instead of threading the format through every branch: a linear tick
        // is a position on the domain, so its value can be recovered from that position. Category, log and
        // time axes label something other than a plain number and keep their own text.
        if (!string.IsNullOrEmpty(labelFormat) && scale is LinearScale formatted)
        {
            for (int i = 0; i < ticks.Count; i++)
            {
                double value = formatted.Min + ticks[i].Item1 * (formatted.Max - formatted.Min);
                ticks[i] = (ticks[i].Item1,
                    string.Format(System.Globalization.CultureInfo.InvariantCulture, labelFormat, value));
            }
        }

        return ticks;
    }

    /// <summary>
    /// How many ticks to skip between labels so they do not collide: 1 when the widest one fits, more
    /// when it does not. Only a few ticks are measured - axis labels are close enough in width that
    /// sampling them is enough to decide, and measuring every one on every frame is wasteful.
    /// </summary>
    private static int LabelStride(
        RenderContext ctx, List<(double Norm, string Text)> ticks, FontSettings font, float slotWidth)
    {
        if (ticks.Count < 2 || slotWidth <= 1f) return 1;

        int measureCount = Math.Min(ticks.Count, 8);
        float widest = 0f;
        for (int i = 0; i < measureCount; i++)
            widest = MathF.Max(widest, ctx.Canvas.MeasureText(ticks[i].Text, font).Width);

        return widest <= slotWidth ? 1 : Math.Max(1, (int)MathF.Ceiling(widest / slotWidth));
    }

    /// <summary>
    /// Vertical counterpart of <see cref="LabelStride"/>: a category axis (horizontal bars, a heatmap
    /// row axis) with more rows than the plot height can label is thinned the same way.
    /// </summary>
    private static int YLabelStride(List<(double Norm, string Text)> ticks, PlotArea plot, FontSettings font)
    {
        if (ticks.Count < 2 || plot.Height <= 1f) return 1;

        float spacing = plot.Height / ticks.Count;
        float needed = font.Size * font.LineHeightMultiplier * 0.95f;
        return spacing >= needed ? 1 : Math.Max(1, (int)MathF.Ceiling(needed / spacing));
    }

    /// <summary>
    /// Shorten text so it fits into <paramref name="maxWidth"/>, appending an ellipsis.
    /// Backends draw text as a single run, so long category names used to overlap their neighbours.
    /// </summary>
    internal static string FitText(RenderContext ctx, string text, FontSettings font, float maxWidth)
    {
        if (maxWidth <= 0f || text.Length == 0) return text;
        if (ctx.Canvas.MeasureText(text, font).Width <= maxWidth) return text;

        const string ellipsis = "\u2026";
        for (int length = text.Length - 1; length > 0; length--)
        {
            string candidate = string.Concat(text.AsSpan(0, length), ellipsis);
            if (ctx.Canvas.MeasureText(candidate, font).Width <= maxWidth) return candidate;
        }
        return ellipsis;
    }

    /// <summary>Draw grid lines from X/Y scales.</summary>
    public static void DrawGrid(RenderContext ctx)
    {
        var plot = ctx.Plot;
        using var paint = ctx.Canvas.CreatePaint();
        paint.SetColor(ctx.GridColor).SetStrokeWidth(ctx.Theme.GridLineWidth);

        // Horizontal grid lines from the Y scale
        foreach (var (norm, _) in TicksFor(ctx, Channel.Y))
        {
            float py = plot.MapY(norm);
            ctx.Canvas.DrawLine(plot.X, py, plot.X + plot.Width, py, paint);
        }

        // Vertical grid lines from the X scale
        foreach (var (norm, _) in TicksFor(ctx, Channel.X))
        {
            float px = plot.MapX(norm);
            ctx.Canvas.DrawLine(px, plot.Y, px, plot.Y + plot.Height, paint);
        }
    }

    /// <summary>
    /// Ticks for one axis, served from <see cref="RenderContext.TickCache"/> when the axis has already been
    /// asked for in this frame - the grid pass and the label pass want the same axes, and computing them twice
    /// costs a ladder plus a format per entry for nothing.
    /// </summary>
    private static List<(double Norm, string Text)> TicksFor(RenderContext ctx, Channel channel)
    {
        if (ctx.TickCache.TryGetValue(channel, out var cached)) return cached;

        var ticks = BuildTicks(ctx, channel);
        ctx.TickCache[channel] = ticks;
        return ticks;
    }

    /// <summary>
    /// Ticks for one axis, honouring what the axis asks for: <see cref="AxisConfig.Ticks"/> as given,
    /// <see cref="AxisConfig.TickStep"/> as the step, or the automatic count - the axis's own length divided by
    /// <see cref="ChartTheme.TickLabelSpacing"/>, never below <see cref="ChartTheme.MinTickCount"/> nor above
    /// <see cref="ChartTheme.MaxTickCount"/>.
    /// </summary>
    private static List<(double Norm, string Text)> BuildTicks(RenderContext ctx, Channel channel)
    {
        var config = channel switch
        {
            Channel.X => ctx.XAxisConfig,
            Channel.Y => ctx.YAxisConfig,
            _ => ctx.Y2AxisConfig,
        };

        float length = channel == Channel.X ? ctx.Plot.Width : ctx.Plot.Height;
        var theme = ctx.Theme;
        // A floor of MinTickCount labels only holds where that many fit: on a short axis (a scope strip, a
        // squeezed card) forcing six of them draws a column of overlapping text, which is worse than three
        // readable ones. Two is the hard floor - an axis with one label is not an axis.
        // The reader's only constraint is "the labels must not touch", and what that costs depends on the axis:
        // a vertical label is as tall as its line height, a horizontal one is as wide as its text. Using the
        // theme's TickLabelSpacing (a width-scale number) for a vertical axis left a 248 px tall chart with four
        // labels and room for eight; the horizontal axis keeps it, and its stride drops every n-th label anyway.
        float labelSize = theme.LabelFontSize > 0f ? theme.LabelFontSize : FontSettings.Default.Size;
        float defaultSpacing = channel == Channel.X ? theme.TickLabelSpacing : labelSize * 1.8f;
        float spacing = config?.TickLabelSpacing ?? defaultSpacing;
        int fits = (int)(length / MathF.Max(1f, spacing)) + 1;
        int floor = fits >= theme.MinTickCount ? theme.MinTickCount : Math.Max(2, Math.Min(fits, theme.MaxTickCount));
        int automatic = Math.Clamp(fits, floor, theme.MaxTickCount);
        int maxTicks = config?.TickCount ?? automatic;
        int minTicks = config?.TickCount ?? theme.MinTickCount;

        // Build the whole ladder first and thin it afterwards. Asking ComputeTicks for `maxTicks` directly made
        // it fill upwards from the bottom of the domain and stop there, which is how a 4-label axis ended up with
        // 10/20/30/40 covering the lower third of a 10..90 domain: the step was chosen for the floor (six) while
        // the emission was capped at four.
        var ticks = ComputeTicks(ctx.Scales.TryGet(channel), maxTicks: Math.Max(64, maxTicks),
            fallbackTickCount: theme.FallbackTickCount, minTicks: minTicks,
            forcedStep: config?.TickStep, explicitTicks: config?.Ticks, labelFormat: config?.LabelFormat);

        // When the ladder is finer than the budget, pick a coarser one instead of thinning this one: dropping
        // entries from an even ladder leaves one pair adjacent and one gap, which reads as a missing label
        // (10/20/40/50 with 30 gone). A coarser step covers the domain evenly with every end in place.
        if (ticks.Count > maxTicks && ticks.Count >= 2 && ctx.Scales.TryGet(channel) is LinearScale axis)
        {
            var coarser = LadderTicks(axis, maxTicks, config);
            if (coarser.Count >= 2) return coarser;
        }

        return ticks;
    }

    /// <summary>
    /// An evenly spaced tick set that fits <paramref name="maxTicks"/> and covers the axis: the coarsest step from
    /// the {1, 2, 5} x 10^n ladder whose count still fits is used, and the ticks run from the first step below the
    /// domain to the first one above it. Both ends are therefore labelled and no entry is missing in between.
    /// </summary>
    private static List<(double Norm, string Text)> LadderTicks(LinearScale axis, int maxTicks, AxisConfig? config)
    {
        var result = new List<(double, string)>();
        double span = axis.Max - axis.Min;
        if (!(span > 0) || !double.IsFinite(span) || maxTicks < 2) return result;

        double magnitude = Math.Pow(10, Math.Floor(Math.Log10(span / Math.Max(1, maxTicks - 1))));
        double[] ladder = [1, 2, 5, 10, 20, 50, 100];
        // Fine to coarse: the first step whose count fits is the coarsest one that does, which is what an axis
        // wants (the fewest labels that still say where the data is).
        double step = 0;
        foreach (double factor in ladder)
        {
            double candidate = factor * magnitude;
            double count = Math.Floor(axis.Max / candidate) - Math.Ceiling(axis.Min / candidate) + 1;
            if (count <= maxTicks)
            {
                step = candidate;
                break;
            }
        }

        if (step <= 0) return result;     // nothing in the ladder fits this budget; the caller keeps its ticks

        double first = Math.Ceiling(axis.Min / step) * step;
        for (double value = first; value <= axis.Max + step * 1e-6; value += step)
        {
            // Pattern, not `config!.LabelFormat`: the non-empty case binds the format into a non-null local, so
            // the null-forgiving operator the earlier form needed is gone (RedundantSuppressNullableWarningExpression).
            string text = config?.LabelFormat is { Length: > 0 } format
                ? string.Format(System.Globalization.CultureInfo.InvariantCulture, format, value)
                : axis.Format(value);
            result.Add((axis.Map(value), text));
            if (result.Count >= maxTicks) break;
        }
        return result;
    }

    /// <summary>
    /// Whether a tick set covers its axis: values taken from the data are only worth using as ticks when they
    /// reach both ends of the domain, which is what makes a yearly axis label its years. A continuous channel
    /// sampled down to a few labels can cover a corner of the range instead, and an arithmetic step that spans
    /// the axis is the better answer then.
    /// </summary>
    private static bool TicksCoverTheAxis(List<(double Norm, string Text)> ticks)
        => ticks.Count >= 2 && ticks[0].Norm <= 0.15 && ticks[^1].Norm >= 0.80;

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
        // Tick labels use the theme's label color (AxisColor stays for the axis lines/titles).
        paint.SetColor(ctx.Theme.LabelColor).SetStrokeWidth(1f);

        var centeredFont = ThemedFont(ctx, TextAlign.Center);
        var rightAlignedFont = ThemedFont(ctx, TextAlign.Right);
        var leftAlignedFont = ThemedFont(ctx, TextAlign.Left);

        // X axis labels: centred under their tick. When the labels are wider than the space between
        // ticks (a 60 category time axis, a dense log axis) only every n-th one is drawn - truncating
        // all of them into "09:3…" just produces a smear. The grid keeps every tick.
        float xLabelY = plot.Y + plot.Height + ctx.Theme.XAxisLabelOffset;
        var xTicks = TicksFor(ctx, Channel.X);
        float slotW = xTicks.Count > 1 ? plot.Width / xTicks.Count : plot.Width;
        // A rotated label occupies less of the axis horizontally, so more of them fit before the stride has to
        // skip any: the room offered to LabelStride grows by 1/cos(angle), with a floor so a near-vertical axis
        // does not thin the labels to nothing (the label is then as tall as the font, not as wide as its text).
        float strideRoom = slotW * 0.95f;
        float axisRotation = ctx.XAxisConfig?.LabelRotation ?? ctx.Theme.XAxisLabelRotation;
        if (axisRotation != 0f)
        {
            float rotationRadians = axisRotation * MathF.PI / 180f;
            strideRoom /= MathF.Max(0.4f, MathF.Abs(MathF.Cos(rotationRadians)));
        }
        int xStride = LabelStride(ctx, xTicks, centeredFont, strideRoom);
        // Only thin when a usable number of labels survives: with three categories a stride of three
        // would leave a single name on the axis, where truncating all three says much more.
        if (xTicks.Count / xStride < 4) xStride = 1;
        // Rotated labels are opt-in (theme or per-axis). A rotated label hangs from its tick: the anchor is the
        // tick, the text turns around it and is drawn from there, so a positive angle reads down-right. Zero
        // degrees takes the plain path, so a chart that does not ask for rotation draws identical pixels.
        float rotationDegrees = axisRotation;
        for (int i = 0; i < xTicks.Count; i += xStride)
        {
            var (norm, text) = xTicks[i];
            string fitted = FitText(ctx, text, centeredFont, slotW * 0.95f * xStride);
            if (rotationDegrees == 0f)
            {
                ctx.Canvas.DrawText(fitted, plot.MapX(norm), xLabelY, centeredFont, paint);
                continue;
            }

            ctx.Canvas.Save();
            ctx.Canvas.Translate(plot.MapX(norm), xLabelY);
            ctx.Canvas.Rotate(rotationDegrees * MathF.PI / 180f);
            ctx.Canvas.DrawText(fitted, 0f, 0f, leftAlignedFont, paint);
            ctx.Canvas.Restore();
        }

        // Y axis labels (right-aligned, vertically centred on their grid line). Ordinal and log
        // scales are labelled too, so horizontal bars get their category names and log axes get
        // their decade values.
        float leftBand = plot.X - ctx.OffsetX;
        DrawYAxisLabels(ctx, plot, Channel.Y, rightAlignedFont,
            AxisLabelDrawWidth(ctx.Theme, leftBand), plot.X - ctx.Theme.YAxisLabelGap, paint);

        // Y2 axis labels on the right side, mirrored: the same column as Y, so a label that does not fit the
        // band the right edge reserved is fitted instead of drawn past the chart edge, and a dense Y2 axis is
        // thinned by the same stride rule instead of piling its labels up. The band is the mirror of the left
        // one, so the two columns are measured the same way.
        if (ctx.Scales.Has(Channel.Y2))
        {
            float rightBand = ctx.Width - leftBand - plot.Width;
            DrawYAxisLabels(ctx, plot, Channel.Y2, leftAlignedFont,
                AxisLabelDrawWidth(ctx.Theme, rightBand), plot.X + plot.Width + ctx.Theme.YAxisLabelGap, paint);
        }

        // Axis titles
        float lineH = centeredFont.Size * centeredFont.LineHeightMultiplier;
        if (ctx.XAxisConfig?.Title != null)
        {
            float tx = plot.X + plot.Width * 0.5f;
            float ty = ctx.OffsetY + ctx.Height - AxisTitleMargin(ctx)
                     - centeredFont.Size * ctx.Theme.AxisTitleBaselineNudge;
            ctx.Canvas.DrawText(ctx.XAxisConfig.Title, tx, ty, centeredFont, paint);
        }
        if (ctx.YAxisConfig?.Title != null)
        {
            float tyCenter = plot.Y + plot.Height / 2f;
            float txPos = ctx.OffsetX + AxisTitleMargin(ctx);
            using (ctx.Canvas.SaveScope())
            {
                ctx.Canvas.Translate(txPos, tyCenter);
                ctx.Canvas.Rotate(-MathF.PI / 2f);
                ctx.Canvas.DrawText(ctx.YAxisConfig.Title, 0, lineH, centeredFont, paint);
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
                ctx.Canvas.DrawText(ctx.Y2AxisConfig.Title, 0, lineH, centeredFont, paint);
            }
        }
    }

    /// <summary>
    /// Draw one vertical axis' tick labels: the Y column right-aligned to the left of the plot, the Y2 column
    /// left-aligned to its right. Both sides get the same treatment - the label is fitted to the room its band
    /// offers and the column is thinned by <see cref="YLabelStride"/> when the ticks stand closer than a line of
    /// text - so neither side reaches over the axis nor piles its labels up.
    /// </summary>
    /// <param name="ctx">Rendering context.</param>
    /// <param name="plot">Plot rectangle the labels are positioned against.</param>
    /// <param name="channel">Vertical channel to label: <see cref="Channel.Y"/> or <see cref="Channel.Y2"/>.</param>
    /// <param name="font">Font the labels are drawn with, alignment included.</param>
    /// <param name="labelWidth">Width the label may occupy (see <see cref="AxisLabelDrawWidth"/>).</param>
    /// <param name="anchorX">X the label is anchored at: its right edge for Y, its left edge for Y2.</param>
    /// <param name="paint">Paint to draw with.</param>
    private static void DrawYAxisLabels(RenderContext ctx, PlotArea plot, Channel channel, FontSettings font,
        float labelWidth, float anchorX, IPaint2D paint)
    {
        var ticks = TicksFor(ctx, channel);
        for (int i = 0; i < ticks.Count; i += YLabelStride(ticks, plot, font))
        {
            var (norm, text) = ticks[i];
            string fitted = FitText(ctx, text, font, labelWidth);
            ctx.Canvas.DrawText(fitted, anchorX, plot.MapY(norm) + font.Size * 0.35f, font, paint);
        }
    }

    /// <summary>Draw the legend (swatch + label items).</summary>
    public static void DrawLegend(RenderContext ctx)
    {
        var colorScale = ctx.ColorScale;
        if (colorScale == null || colorScale.Domain.Count == 0) return;

        var cfg = ctx.LegendConfig;
        if (cfg is not { Position: not LegendPosition.None }) return;

        var layout = ctx.LegendLayout ?? LegendLayoutHelper.Compute(ctx.Plot, cfg, colorScale, ctx.OffsetX, ctx.Canvas, ctx.Theme);
        if (layout == null) return;
        var items = layout.Value.Items;

        using var paint = ctx.Canvas.CreatePaint();
        var font = ThemedFont(ctx, TextAlign.Left);
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

            // Draw color swatch - as the symbol of the Shape channel when it drives the same categories
            // (G2 shows the shape in the legend too), otherwise as the usual rounded square.
            swatchPath.Reset();
            if (ShapeForLegend(ctx, item.Key) is { } shape)
                ShapeGeometry.Build(swatchPath, shape, item.X + swatchSize / 2f, item.Y + swatchSize / 2f,
                                    swatchSize / 2f);
            else
                swatchPath.RoundRect(item.X, item.Y, swatchSize, swatchSize, ctx.Theme.LegendSwatchCornerRadius);
            paint.SetColor(new Color(color.R, color.G, color.B, alpha));
            ctx.Canvas.Fill(swatchPath, paint);

            // Draw label text
            paint.SetColor(new Color(ctx.AxisColor.R, ctx.AxisColor.G, ctx.AxisColor.B, alpha));
            ctx.Canvas.DrawText(item.Key, item.X + textOffsetX,
                                item.Y + swatchSize * ctx.Theme.LegendTextBaselineRatio, font, paint);
        }
    }

    /// <summary>
    /// Symbol the legend should show for a series key, or null when the Shape channel does not cover it
    /// (then the swatch stays a square).
    /// </summary>
    private static ShapeKind? ShapeForLegend(RenderContext ctx, string key)
    {
        if (!ctx.Encodes.Has(Channel.Shape)) return null;
        if (ctx.Scales.TryGet(Channel.Shape) is not ICategoricalShapeScale shapeScale) return null;

        foreach (var category in shapeScale.Domain)
            if (category == key) return shapeScale.MapShape(key);
        return null;
    }

    /// <summary>Draw crosshair overlay at mouse position.</summary>
    public static void DrawCrosshair(RenderContext ctx)
    {
        if (!ctx.MousePos.HasValue) return;
        ChartInteraction.DrawCrosshair(ctx.Canvas, ctx.MousePos.Value, ctx.Plot, ctx.Theme);
    }
}
