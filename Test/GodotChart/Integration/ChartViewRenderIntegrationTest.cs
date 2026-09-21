namespace GodotNodeExtension.Tests.GodotChart;

using System;
using System.Collections.Generic;
using System.IO;
using GdUnit4;
using Godot;
using GodotNodeExtension.Component.GodotChart;
using GodotNodeExtension.Component.GodotChart.Canvas;
using GodotNodeExtension.Component.GodotChart.Marks;
using static GdUnit4.Assertions;

/// <summary>
/// End-to-end rendering of <see cref="ChartView"/> on the engine's <b>real</b> rendering device: every
/// chart kind is built, laid out, drawn by the Skia backend into its texture and the resulting pixels
/// are read back and measured. The rest of the suite drives a <c>FakeCanvas2D</c>, which records the
/// calls but never rasterises - a broken upload, a stale surface, a texture that keeps its old size or a
/// mark that draws nothing at all is only visible here.
/// <para>
/// The suites skip themselves when the run has no rendering device (a headless run), so run them with a
/// device to execute them:
/// <c>python Tools/run_tests.py --component GodotChart --integration --render</c>.
/// </para>
/// <para>
/// <b>Optional artefact dump:</b> when the environment variable <c>CHART_INTEGRATION_OUT</c> points at a
/// directory (created if missing), every case that renders a chart writes one PNG per chart kind into it
/// (<c>&lt;Kind&gt;.png</c>, e.g. <c>Heatmap.png</c>) next to the measured numbers, for visual inspection -
/// the dump is off by default, so a run that does not ask for it writes nothing to disk.
/// </para>
/// <para>
/// "Background" always means the palette background the chart paints itself (<see
/// cref="ChartTheme.Dark"/>), not the colour the surface was cleared with: the background renderer
/// covers the surface anyway. Comparing whole colours needs a tolerance - the image is 8 bit per
/// channel, so an exactly equal <see cref="Color"/> float round-trip never matches a pixel.
/// </para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class ChartViewRenderIntegrationTest
{
    /// <summary>Fraction of the surface that must not be background for a chart to count as drawn.</summary>
    private const float MinContentRatio = 0.02f;

    /// <summary>
    /// How far a chart has to beat the same chart rendered without data. A chart that draws nothing at
    /// all still shows its grid, axes and labels, so "&gt; 2%" alone would pass on the decorations of an
    /// empty plot.
    /// </summary>
    private const float MinContentOverBaseline = 0.01f;

    /// <summary>
    /// Fraction of the surface a chart's own background has to cover. The plot area is inset by the
    /// padding and every decoration is thin, so the background is the dominant colour of a surface that was
    /// really painted with the theme - which makes "most of the surface is that colour" a statement about
    /// the whole repaint instead of about one lucky pixel.
    /// </summary>
    private const float MinBackgroundRatio = 0.5f;

    /// <summary>
    /// Fraction of the surface that may still carry the background of the <i>previous</i> frame. A palette
    /// colour of a mark could theoretically land within the 8 bit tolerance of the wrong background, so this
    /// is a "nothing meaningful is left" threshold rather than "exactly zero pixels".
    /// </summary>
    private const float MaxStaleBackgroundRatio = 0.01f;

    /// <summary>
    /// Floor for "the theme is still on the surface" after a resize. It is deliberately far below
    /// <see cref="MinBackgroundRatio"/>: Heatmap and Treemap really do fill more than half of their own
    /// surface with cells, so there the question is not whether the background dominates the chart but
    /// whether the repaint still used the theme's colour at all - a theme the resize dropped leaves ~0.
    /// </summary>
    private const float MinThemedBackgroundRatio = 0.2f;

    private static readonly Vector2 ViewSize = new(320f, 200f);
    private static readonly Vector2 ResizedViewSize = new(480f, 300f);

    /// <summary>
    /// Skip the case when this run has no rendering device (headless): the Skia backend allocates a
    /// RenderingDevice texture and cannot render without one.
    /// </summary>
    private static bool NoRenderingDevice(string caseName)
    {
        if (RenderingServer.GetRenderingDevice() is not null) return false;
        GD.Print($"[skip] {caseName}: no rendering device in this run");
        return true;
    }

    /// <summary>Create a view on the <b>default</b> backend (no fake canvas injected), sized, in the tree.</summary>
    private static ChartView AddView(ChartRenderCase c, Vector2 size)
    {
        var view = new ChartView
        {
            Kind = c.Kind,
            XField = c.XField,
            YField = c.YField,
            ColorField = c.ColorField,
            Size = size,
        };
        ((SceneTree)Engine.GetMainLoop()).Root.AddChild(view);
        return view;
    }

    /// <summary>
    /// Run the frames the engine would: the view rebuilds its chart, the surface it hosts then draws.
    /// </summary>
    private static void Pump(ChartView view, int frames = 2)
    {
        for (int i = 0; i < frames; i++)
        {
            view._Process(0.016);
            foreach (var child in view.GetChildren(includeInternal: true))
                if (child is Canvas2DControl surface)
                    surface._Process(0.016);
        }
    }

    private static void Release(ChartView view)
    {
        // Tolerant on purpose: a failing assertion must not turn into a cascade of secondary errors.
        try
        {
            view.GetParent()?.RemoveChild(view);
        }
        finally
        {
            view.Free();
        }
    }

    /// <summary>
    /// The pixels of the surface the view presents, or null when it has none. Read through the standard
    /// <see cref="Texture2D.GetImage"/> API: the Skia texture implements Godot's virtual, so the base
    /// call returns the surface it just drew into.
    /// </summary>
    private static Image? Pixels(ChartView view) => view.Texture?.GetImage();

    /// <summary>True when the pixel is the theme background within the rounding of an 8 bit channel.</summary>
    private static bool IsBackground(Color pixel, Color background)
        => ChartRenderHarness.IsBackground(pixel, background);

    /// <summary>Number of pixels that differ from the background, and the total pixel count.</summary>
    private static (int Content, int Total) Measure(Image image)
        => ChartRenderHarness.Measure(image);

    /// <summary>Number of pixels that differ from the given background colour, and the total pixel count.</summary>
    private static (int Content, int Total) Measure(Image image, Color background)
        => ChartRenderHarness.Measure(image, background);

    /// <summary>
    /// Fraction of the surface that carries <paramref name="colour"/> - the inverse of what
    /// <see cref="Measure(Image, Color)"/> counts, phrased for "the theme's background covers the surface".
    /// </summary>
    private static float BackgroundRatio(Image image, Color colour)
        => ChartRenderHarness.BackgroundRatio(image, colour);

    /// <summary>Number of pixels that differ between two images of the same size (any channel, -1 on mismatch).</summary>
    private static int DifferingPixels(Image first, Image second)
        => ChartRenderHarness.DifferingPixels(first, second);

    /// <summary>Content ratio of what the view currently presents (0 when there is nothing to read).</summary>
    private static float ContentRatio(ChartView view, out int content)
        => ChartRenderHarness.ContentRatio(view, out content);

    /// <summary>The directory <c>CHART_INTEGRATION_OUT</c> names, or an empty string when it is unset.</summary>
    private static string DumpDirectory()
        => ChartRenderHarness.DumpDirectory();

    /// <summary>Write one PNG per kind when the dump switch is on; returns the problem text, if any.</summary>
    private static string DumpPng(string directory, string label, Image image)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, label + ".png");
            // SavePng takes a Godot path: an absolute OS path is accepted with forward slashes only.
            image.SavePng(path.Replace('\\', '/'));
            return File.Exists(path) ? "" : $"{label}: {path} was not written";
        }
        catch (Exception ex)
        {
            return $"{label}: dump failed ({ex.GetType().Name}: {ex.Message})";
        }
    }

    // ── Every kind ──────────────────────────────────────────────────────────

    /// <summary>
    /// Every <see cref="ChartKind"/> renders from its conventional fields through the real backend: the
    /// surface has the node's size, it is the Skia backend, the render neither throws nor leaves the
    /// surface empty, and the marks cover noticeably more than an empty chart of the same kind does.
    /// </summary>
    [TestCase]
    public void EveryChartKindRendersContentThroughTheRealBackend()
    {
        const string name = nameof(EveryChartKindRendersContentThroughTheRealBackend);
        if (NoRenderingDevice(name)) return;

        string dumpDirectory = DumpDirectory();
        var report = new List<string>();
        var problems = new List<string>();

        foreach (var c in ChartRenderCase.All)
        {
            // Baseline: the same kind with no rows draws its grid, axes, legend and title - and nothing
            // else. Subtracting it is what makes "the chart rendered" say something about the marks.
            float baseline;
            var empty = AddView(c, ViewSize);
            try
            {
                Pump(empty);
                baseline = ContentRatio(empty, out _);
            }
            finally
            {
                Release(empty);
            }

            float ratio = 0f;
            int content = 0;
            var view = AddView(c, ViewSize);
            try
            {
                view.SetData(c.Rows);
                Pump(view);

                if (view.Canvas is not SkiaCanvas2DBackend)
                    problems.Add($"{c.Label}: canvas is {(view.Canvas != null ? view.Canvas.GetType().Name : "null")}, not the Skia backend");

                var image = Pixels(view);
                if (image is null)
                {
                    problems.Add($"{c.Label}: the surface published no texture");
                }
                else
                {
                    if (image.GetWidth() != (int)ViewSize.X || image.GetHeight() != (int)ViewSize.Y)
                        problems.Add($"{c.Label}: texture is {image.GetWidth()}x{image.GetHeight()}, want {(int)ViewSize.X}x{(int)ViewSize.Y}");

                    var measured = Measure(image);
                    content = measured.Content;
                    ratio = measured.Total == 0 ? 0f : (float)measured.Content / measured.Total;

                    if (ratio < MinContentRatio)
                        problems.Add($"{c.Label}: only {ratio:P2} of the surface is drawn");
                    if (ratio < baseline + MinContentOverBaseline)
                        problems.Add($"{c.Label}: marks add nothing over the empty chart ({ratio:P2} vs baseline {baseline:P2})");

                    if (dumpDirectory.Length > 0)
                    {
                        string problem = DumpPng(dumpDirectory, c.Label, image);
                        if (problem.Length > 0) problems.Add(problem);
                    }
                }
            }
            catch (Exception ex)
            {
                problems.Add($"{c.Label}: rendering threw {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                Release(view);
            }

            report.Add($"{c.Label,-12} content {content,6} px = {ratio,6:P2}  (empty baseline {baseline:P2})");
        }

        GD.Print($"{name}: {ChartRenderCase.All.Count} kinds\n" + string.Join("\n", report));
        if (dumpDirectory.Length > 0)
            GD.Print($"{name}: dumped one PNG per kind into {dumpDirectory}");

        AssertThat(string.Join("; ", problems)).IsEqual("");
    }

    // ── Per-kind feature pixels ─────────────────────────────────────────────

    /// <summary>
    /// Painted content that is clearly more than a decoration. The theme's grid and gauge-track lines are
    /// the background plus 8% white (~0.07 per channel) and the area gradient tops out at its 0.15
    /// opacity (~0.13), so 0.15 tells "a mark painted here" apart from "a decoration line crosses here".
    /// Several probes need that distinction, because the category grid line is drawn exactly through the
    /// point a probe aims at.
    /// </summary>
    private const float StrongContent = 0.15f;

    /// <summary>Largest per-channel difference of two colours (0 .. 1).</summary>
    private static float ColourDistance(Color a, Color b)
        => MathF.Max(MathF.Abs(a.R - b.R), MathF.Max(MathF.Abs(a.G - b.G), MathF.Abs(a.B - b.B)));

    /// <summary>Centre of the band of the <paramref name="index"/>-th ordinal category (the scale convention).</summary>
    private static double Band(int index, int count) => (index + 0.5) / count;

    /// <summary>Screen point at a polar coordinate around a centre (radians, screen Y grows downwards).</summary>
    private static Vector2 OnCircle(float cx, float cy, float radius, float angle)
        => new(cx + MathF.Cos(angle) * radius, cy + MathF.Sin(angle) * radius);

    /// <summary>
    /// Numeric field of one case row. The case data holds doubles; a value of any other kind has no
    /// number and is reported as NaN, the same "no position" convention the scales use.
    /// </summary>
    private static double Number(ChartRenderCase c, int row, string field)
        => c.Rows[row].Get(field) switch
        {
            double d => d,
            float f => f,
            int i => i,
            long l => l,
            _ => double.NaN,
        };

    /// <summary>Number of distinct values a field holds in the case (the size of its ordinal domain).</summary>
    private static int DistinctField(ChartRenderCase c, string field)
    {
        var seen = new HashSet<string>();
        foreach (var row in c.Rows)
            if (row.Has(field)) seen.Add(row.Get(field)?.ToString() ?? "");
        return seen.Count;
    }

    /// <summary>
    /// Every <see cref="ChartKind"/> is asked for the pixels that make it that kind - a bar body above the
    /// baseline, a ring with a hole, a band between two bounds, a widening violin, a candle body wider than
    /// its wick - measured from the live plot area the chart laid out, not from constants. The case reports
    /// the first missing feature of each kind instead of the content count the sibling case already checks.
    /// </summary>
    [TestCase]
    public void EveryKindDrawsItsCharacteristicPixels()
    {
        const string name = nameof(EveryKindDrawsItsCharacteristicPixels);
        if (NoRenderingDevice(name)) return;

        var background = ChartTheme.Dark().BackgroundColor;
        var report = new List<string>();
        var problems = new List<string>();

        foreach (var c in ChartRenderCase.All)
        {
            var view = AddView(c, ViewSize);
            try
            {
                view.SetData(c.Rows);
                Pump(view);

                var image = Pixels(view);
                if (image is null)
                {
                    problems.Add($"{c.Label}: the surface published no texture to measure");
                    report.Add($"{c.Label,-12} no texture");
                    continue;
                }
                if (view.Chart != null ? view.Chart.CurrentPlotArea is not { } plot : true)
                {
                    problems.Add($"{c.Label}: the chart published no plot area to probe");
                    report.Add($"{c.Label,-12} no plot area");
                    continue;
                }

                var probe = new KindProbe(c, image, background, plot, problems);
                string summary = c.Kind switch
                {
                    ChartKind.Bar         => CheckBar(probe, c),
                    ChartKind.Line        => CheckLine(probe, c),
                    ChartKind.Area        => CheckArea(probe, c),
                    ChartKind.Scatter     => CheckScatter(probe, c),
                    ChartKind.RangeArea   => CheckRangeArea(probe, c),
                    ChartKind.Pie         => CheckPie(probe, donut: false),
                    ChartKind.Donut       => CheckPie(probe, donut: true),
                    ChartKind.Radar       => CheckRadar(probe),
                    ChartKind.Violin      => CheckViolin(probe, c),
                    ChartKind.Box         => CheckBox(probe, c),
                    ChartKind.Candlestick => CheckCandlestick(probe, c),
                    ChartKind.Heatmap     => CheckHeatmap(probe, c),
                    ChartKind.Treemap     => CheckTreemap(probe),
                    ChartKind.Sunburst    => CheckSunburst(probe),
                    ChartKind.Sankey      => CheckFlows(probe, "the flow ribbons and node bars"),
                    ChartKind.Chord       => CheckChord(probe),
                    ChartKind.Gauge       => CheckGauge(probe, c),
                    ChartKind.Funnel      => CheckFunnel(probe),
                    ChartKind.Waffle      => CheckWaffle(probe),
                    ChartKind.Timeline    => CheckTimeline(probe, c),
                    ChartKind.Lollipop    => CheckLollipop(probe, c),
                    ChartKind.Milestone   => CheckMilestone(probe, c),
                    ChartKind.GeoArea     => CheckGeoArea(probe),
                    ChartKind.GeoBubble   => CheckGeoBubble(probe),
                    _ => Unprobed(probe),
                };
                report.Add($"{c.Label,-12} {summary}");
            }
            catch (Exception ex)
            {
                problems.Add($"{c.Label}: the feature probe threw {ex.GetType().Name}: {ex.Message}");
                report.Add($"{c.Label,-12} threw {ex.GetType().Name}");
            }
            finally
            {
                Release(view);
            }
        }

        GD.Print($"{name}: {ChartRenderCase.All.Count} kinds\n" + string.Join("\n", report));
        AssertThat(string.Join("; ", problems)).IsEqual("");
    }

    private static string Unprobed(KindProbe p)
    {
        p.Fail("feature probe", "this kind has no characteristic-pixel probe");
        return "unprobed";
    }

    // ── Bar ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Bar: the body rises from the data zero line, a short bar leaves the top of the plot empty and the
    /// padding between two neighbouring bars is a real gap. The body probe sits a quarter of a slot off
    /// the category centre, because the category grid line is drawn exactly through that centre.
    /// </summary>
    private static string CheckBar(KindProbe p, ChartRenderCase c)
    {
        int count = c.Rows.Count;
        float slot = p.Plot.Width / count;

        // The shortest bar: its top is at most (value / domain maximum) of the value axis, so the top of
        // the plot stays background whatever the nice-tick rounding did to the domain.
        int low = 0;
        double lowValue = double.MaxValue;
        for (int i = 0; i < count; i++)
        {
            double value = Number(c, i, c.YField);
            if (value < lowValue) { lowValue = value; low = i; }
        }

        float baseline = p.Plot.MapY(0);                     // the data zero line is the plot bottom here
        float xProbe = p.Plot.MapX(Band(low, count)) + slot * 0.25f;

        if (p.ContentRatio(xProbe - 2, baseline - 12, xProbe + 2, baseline - 4) < 0.5f)
            p.Fail("bar body", $"no bar fill just above the baseline at x={xProbe:F0}");

        float above = p.ContentRatio(xProbe - 3, p.Plot.Y + 5, xProbe + 3, p.Plot.Y + p.Plot.Height * 0.3f);
        if (above >= 0.2f)
            p.Fail("bar height", $"{above:P0} of the strip above the shortest bar (x={xProbe:F0}) is painted");

        double gapNorm = low < count - 1 ? (low + 1) / (double)count : low / (double)count;
        float xGap = p.Plot.MapX(gapNorm);
        float gap = p.ContentRatio(xGap - 5, baseline - p.Plot.Height * 0.3f, xGap + 5, baseline - p.Plot.Height * 0.05f);
        if (gap >= 0.2f)
            p.Fail("bar gap", $"{gap:P0} of the gap between two bars (x={xGap:F0}) is painted, want background");

        return $"body, height and gap ({gap:P0} in the gap)";
    }

    // ── Line ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Line: the path is a thin stroke, and at the mid X between two neighbouring categories it runs at
    /// the average of their rows - where both a straight segment and the smooth cubic interpolate to.
    /// The per-column runs ignore the faint category grid line, so they really are the stroke.
    /// </summary>
    private static string CheckLine(KindProbe p, ChartRenderCase c)
    {
        int n = c.Rows.Count;
        int left = n / 2 - 1, right = n / 2;
        float y0 = p.Plot.Y + 2f, y1 = p.Plot.Y + p.Plot.Height - 3f;

        var atLeft = p.ContentRunsInColumn(p.Plot.MapX(Band(left, n)), y0, y1, strong: true);
        var atRight = p.ContentRunsInColumn(p.Plot.MapX(Band(right, n)), y0, y1, strong: true);
        float xMid = p.Plot.MapX((Band(left, n) + Band(right, n)) / 2);
        var atMid = p.ContentRunsInColumn(xMid, y0, y1, strong: true);

        if (atLeft.Count != 1 || atRight.Count != 1 || atMid.Count != 1)
        {
            p.Fail("line path", $"one stroke run per probe column expected, got {atLeft.Count}/{atMid.Count}/{atRight.Count}");
            return $"runs {atLeft.Count}/{atMid.Count}/{atRight.Count}";
        }

        float leftY = (atLeft[0].Start + atLeft[0].End) / 2f;
        float rightY = (atRight[0].Start + atRight[0].End) / 2f;
        int midHeight = atMid[0].End - atMid[0].Start + 1;
        float midY = (atMid[0].Start + atMid[0].End) / 2f;
        float expected = (leftY + rightY) / 2f;

        if (midHeight > 8)
            p.Fail("line stroke", $"the stroke at the mid X is {midHeight}px tall, want a thin line");
        if (MathF.Abs(midY - expected) > 3f)
            p.Fail("line path", $"the path at the mid X is at y={midY:F0}, the category rows {leftY:F0}/{rightY:F0} expect y={expected:F0}");

        return $"path y={leftY:F0}/{midY:F0}/{rightY:F0}";
    }

    // ── Area ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Area: the fill reaches from the curve down to the data zero line and does not spill above the
    /// curve. The gradient fades towards the baseline, so the curve is found as the one strong run in the
    /// column and the fill is probed a quarter of the plot above the baseline, where the gradient is
    /// still opaque enough to read.
    /// </summary>
    private static string CheckArea(KindProbe p, ChartRenderCase c)
    {
        int n = c.Rows.Count;
        float xMid = p.Plot.MapX((Band(n / 2 - 1, n) + Band(n / 2, n)) / 2);
        float baseline = p.Plot.MapY(0);

        var runs = p.ContentRunsInColumn(xMid, p.Plot.Y + 2f, p.Plot.Y + p.Plot.Height - 3f, strong: true);
        if (runs.Count != 1)
        {
            p.Fail("area curve", $"expected exactly one strong run (the curve) at x={xMid:F0}, found {runs.Count}");
            return $"curve runs {runs.Count}";
        }

        int curveHeight = runs[0].End - runs[0].Start + 1;
        float curveY = (runs[0].Start + runs[0].End) / 2f;
        if (curveHeight > 8)
            p.Fail("area curve", $"the curve at x={xMid:F0} is {curveHeight}px tall, want a line");

        if (!p.IsContentAt(xMid, baseline - p.Plot.Height * 0.28f))
            p.Fail("area fill", $"the fill is missing {p.Plot.Height * 0.28f:F0}px above the baseline at x={xMid:F0}");

        float above = p.ContentRatio(xMid - 4, curveY - 20, xMid + 4, curveY - 8);
        if (above >= 0.25f)
            p.Fail("area fill bound", $"{above:P0} of the block above the curve at x={xMid:F0} is painted, want background");

        return $"curve y={curveY:F0}, {above:P0} above it";
    }

    // ── Scatter ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Scatter: a point is a small round symbol - about as tall as it is wide - sitting on its category's
    /// band centre. Both extents come from the image, so a point that was replaced by a bar (or a whole
    /// column of paint) cannot pass.
    /// </summary>
    private static string CheckScatter(KindProbe p, ChartRenderCase c)
    {
        int n = c.Rows.Count;
        float x = p.Plot.MapX(Band(0, n));
        float yTop = p.Plot.Y + 2f, yBottom = p.Plot.Y + p.Plot.Height - 3f;

        var column = p.ContentRunsInColumn(x, yTop, yBottom, strong: true);
        if (column.Count != 1)
        {
            p.Fail("scatter point", $"expected one symbol at x={x:F0}, found {column.Count} runs");
            return $"runs {column.Count}";
        }

        int height = column[0].End - column[0].Start + 1;
        float centreY = (column[0].Start + column[0].End) / 2f;

        // The symbol is a bounded run, a grid line is not: a run that reaches the scan window's edge is a
        // line (or a background change), so it is skipped. The dot may still sit exactly on a grid line - the
        // run then merges with it on the centre row - so a few rows around the centre are tried and the widest
        // bounded run wins. Which grid lines exist depends on the axis ticks, so this is also what keeps the
        // case honest when the tick set changes.
        const float reach = 30f;
        int width = 0;
        for (int dy = -2; dy <= 2 && width < 4; dy++)
        {
            foreach (var (start, end) in p.ContentRunsInRow(centreY + dy, x - reach, x + reach))
            {
                if (start <= x - reach || end >= x + reach) continue;
                width = Math.Max(width, end - start + 1);
            }
        }

        if (height < 4 || height > 20)
            p.Fail("scatter point", $"the symbol at x={x:F0} is {height}px tall");
        if (width < 4 || width > 20)
            p.Fail("scatter point", $"the symbol at x={x:F0} is {width}px wide, want a small dot");
        if (Math.Abs(width - height) > 5)
            p.Fail("scatter point", $"the symbol is {width}x{height}px, want a roughly round dot");

        return $"point {width}x{height}px at ({x:F0},{centreY:F0})";
    }

    // ── RangeArea ───────────────────────────────────────────────────────────

    /// <summary>
    /// RangeArea: the band is one filled run between its two bounds at a category-to-category X, nothing
    /// is drawn beyond the first category, and the run does not fill the whole plot.
    /// </summary>
    private static string CheckRangeArea(KindProbe p, ChartRenderCase c)
    {
        float yTop = p.Plot.Y + 2f, yBottom = p.Plot.Y + p.Plot.Height - 3f;
        float x = p.Plot.MapX(0.5);                       // between two categories, clear of every grid line

        var runs = p.ContentRunsInColumn(x, yTop, yBottom, strong: true);
        if (runs.Count != 1)
        {
            p.Fail("range band", $"expected one band at x={x:F0}, found {runs.Count} runs");
            return $"runs {runs.Count}";
        }

        var (top, bottom) = runs[0];
        int height = bottom - top + 1;
        if (height < 10)
            p.Fail("range band", $"the band at x={x:F0} is only {height}px tall");
        if (height > p.Plot.Height * 0.75f)
            p.Fail("range band", $"the band at x={x:F0} fills {height / p.Plot.Height:P0} of the plot, want a band");

        if (!p.IsContentAt(x, (top + bottom) / 2f))
            p.Fail("range band", $"the middle of the band at x={x:F0} is not painted");

        // The band starts at the first category: nothing is drawn to the left of it.
        float beforeX = p.Plot.MapX(Band(0, c.Rows.Count)) - 8;
        var before = p.ContentRunsInColumn(beforeX, yTop, yBottom, strong: true);
        if (before.Count != 0)
            p.Fail("range band bound", $"the band is painted at x={beforeX:F0}, left of the first category");

        return $"band {height}px tall at x={x:F0}";
    }

    // ── Pie / Donut ─────────────────────────────────────────────────────────

    /// <summary>
    /// Pie: the ring at 0.6 * min(plot side)/2 along +X and the centre are all filled. Donut: the same
    /// ring is filled but the centre is the hole.
    /// </summary>
    private static string CheckPie(KindProbe p, bool donut)
    {
        float minDim = MathF.Min(p.Plot.Width, p.Plot.Height);
        float cx = p.Plot.X + p.Plot.Width / 2f;
        float cy = p.Plot.Y + p.Plot.Height / 2f;
        float ring = minDim * 0.3f;                      // 0.6 * minDim / 2

        if (!p.IsContentAt(cx + ring, cy))
            p.Fail("pie ring", $"nothing is painted at {ring:F0}px right of the plot centre");
        if (donut)
        {
            if (p.IsContentAt(cx, cy))
                p.Fail("donut hole", "the plot centre of the donut is painted, want the hole");
        }
        else if (!p.IsContentAt(cx, cy))
        {
            p.Fail("pie disc", "the plot centre of the pie is background, want a filled disc");
        }

        return donut ? $"ring filled, hole {ring:F0}px wide" : $"disc filled, ring at {ring:F0}px";
    }

    // ── Radar ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Radar: the filled polygon covers a small disc around the plot centre in every direction, while the
    /// polar grid alone only paints its four spokes - so a ring of probes around the centre separates the
    /// polygon fill from the grid. Just outside the outer ring the plot is background again.
    /// </summary>
    private static string CheckRadar(KindProbe p)
    {
        float minDim = MathF.Min(p.Plot.Width, p.Plot.Height);
        float cx = p.Plot.X + p.Plot.Width / 2f;
        float cy = p.Plot.Y + p.Plot.Height / 2f;
        float maxR = minDim / 2f * 0.85f;               // RadarMark.RadiusFactor

        const int samples = 24;
        int covered = 0;
        for (int i = 0; i < samples; i++)
        {
            var point = OnCircle(cx, cy, maxR * 0.15f, MathF.Tau * i / samples);
            if (p.IsContentAt(point.X, point.Y)) covered++;
        }
        if (covered < samples * 5 / 6)
            p.Fail("radar polygon", $"only {covered}/{samples} probes around the plot centre are painted, want the polygon fill");

        // Halfway between two axes the grid stops at the outer ring: beyond it there is background only.
        var outside = OnCircle(cx, cy, maxR * 0.95f, -MathF.PI / 4f);
        if (p.IsContentAt(outside.X, outside.Y))
            p.Fail("radar extent", $"the polar grid reaches past its outer ring at ({outside.X:F0},{outside.Y:F0})");

        return $"polygon covers {covered}/{samples} centre probes";
    }

    // ── Violin ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Violin: the silhouette is wide at its body and tapers towards both ends of the value range. The
    /// widths are measured row by row from the image, so a shape that is equally wide over its whole
    /// height (a bar, say) cannot pass.
    /// </summary>
    /// <summary>
    /// Violin: the density body tapers from its widest point towards both value tails.
    /// <para>
    /// The width of a row is measured with <see cref="KindProbe.IsStrongAt"/> (<c>strong: true</c>), not with
    /// "differs from the background": the chart draws a HORIZONTAL grid line through every value tick, and
    /// inside the probe window such a line spans the window's whole width - counting it as the violin made a
    /// trim body look like a rectangle as wide as the window. The grid blend stays far below
    /// <see cref="StrongContent"/> while the body's fill (the palette colour at the violin's fill opacity) and
    /// its outline are well above it. Rows thinner than the grid line's own ~2 px are dropped, and a group
    /// without spread (drawn as one minimal horizontal marker, see ViolinMark.DrawFlatGroup) can never satisfy
    /// the 10-row floor, so the marker is not mistaken for a body either.
    /// </para>
    /// </summary>
    private static string CheckViolin(KindProbe p, ChartRenderCase c)
    {
        int categories = DistinctField(c, c.XField);
        float x = p.Plot.MapX(Band(0, categories));
        float reach = p.Plot.Width / categories * 0.45f;     // the violin's half width (0.35 of the slot) plus margin

        var widths = new List<int>();
        for (int y = (int)p.Plot.Y + 2; y <= (int)(p.Plot.Y + p.Plot.Height) - 3; y++)
        {
            int width = 0;
            foreach (var (start, end) in p.ContentRunsInRow(y, x - reach, x + reach, strong: true))
                width = Math.Max(width, end - start + 1);
            // The body's thinnest tail is a few pixels of strong ink; anything thinner is anti-aliasing noise.
            if (width > 5) widths.Add(width);
        }

        if (widths.Count < 10)
        {
            p.Fail("violin body", $"only {widths.Count} rows of the first violin carry body ink");
            return $"{widths.Count} violin rows";
        }

        int peak = 0;
        foreach (int width in widths) peak = Math.Max(peak, width);
        int head = Math.Max(widths[0], widths[Math.Min(2, widths.Count - 1)]);
        int tail = Math.Max(widths[^1], widths[Math.Max(0, widths.Count - 3)]);

        if (peak < head * 2 || peak < tail * 2)
            p.Fail("violin taper", $"the violin is {peak}px wide at its body but {head}/{tail}px at its ends, want a taper");

        return $"violin {widths.Count} rows, widest {peak}px, ends {head}/{tail}px";
    }

    // ── Box ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Box: a wide filled box between Q1 and Q3 with a whisker line that reaches past it on both sides to
    /// the min and max. The box is measured off the category centre (the whisker runs through the grid
    /// line there), the whisker span on the centre column itself.
    /// </summary>
    private static string CheckBox(KindProbe p, ChartRenderCase c)
    {
        int categories = DistinctField(c, c.XField);
        float slot = p.Plot.Width / categories;
        float x = p.Plot.MapX(Band(0, categories));
        float yTop = p.Plot.Y + 2f, yBottom = p.Plot.Y + p.Plot.Height - 3f;

        float xSide = x + slot * 0.18f;                   // inside the box, clear of the grid line and the caps
        var boxRuns = p.ContentRunsInColumn(xSide, yTop, yBottom, strong: true);
        if (boxRuns.Count != 1)
        {
            p.Fail("box body", $"expected one box at x={xSide:F0}, found {boxRuns.Count} runs");
            return $"box runs {boxRuns.Count}";
        }

        var (boxTop, boxBottom) = boxRuns[0];
        int boxHeight = boxBottom - boxTop + 1;
        if (boxHeight < 6)
            p.Fail("box body", $"the box at x={xSide:F0} is only {boxHeight}px tall");

        int boxWidth = 0;
        foreach (var (start, end) in p.ContentRunsInRow((boxTop + boxBottom) / 2f, x - slot, x + slot))
            boxWidth = Math.Max(boxWidth, end - start + 1);
        if (boxWidth < 10)
            p.Fail("box body", $"the box at x={xSide:F0} is only {boxWidth}px wide, want a filled rectangle");
        if (boxWidth > slot)
            p.Fail("box body", $"the box is {boxWidth}px wide, wider than its {slot:F0}px slot");

        var whisker = p.ContentRunsInColumn(x, yTop, yBottom, strong: true);
        if (whisker.Count != 1)
        {
            p.Fail("box whisker", $"expected one whisker line at x={x:F0}, found {whisker.Count} runs");
            return $"whisker runs {whisker.Count}";
        }
        var (whiskerTop, whiskerBottom) = whisker[0];
        if (whiskerTop > boxTop - 4 || whiskerBottom < boxBottom + 4)
            p.Fail("box whisker", $"the whisker {whiskerTop}..{whiskerBottom} does not reach past the box {boxTop}..{boxBottom}");

        return $"box {boxWidth}x{boxHeight}px, whisker {whiskerTop}..{whiskerBottom}";
    }

    // ── Candlestick ─────────────────────────────────────────────────────────

    /// <summary>
    /// Candlestick: the first candle has a narrow wick from its high to its low and a much wider body
    /// between its open and close, and the body's vertical span sits inside the high/low span. The wick is
    /// measured on the candle's centre column, where the faint category grid line runs too - which is why
    /// the probes ask for strong content only.
    /// </summary>
    private static string CheckCandlestick(KindProbe p, ChartRenderCase c)
    {
        int candles = c.Rows.Count;
        float slot = p.Plot.Width / candles;
        float x = p.Plot.MapX(Band(0, candles));
        float yTop = p.Plot.Y + 2f, yBottom = p.Plot.Y + p.Plot.Height - 3f;

        float xSide = x + slot * 0.2f;                    // inside the body, clear of the grid line
        var bodyRuns = p.ContentRunsInColumn(xSide, yTop, yBottom, strong: true);
        if (bodyRuns.Count != 1)
        {
            p.Fail("candle body", $"expected one body at x={xSide:F0}, found {bodyRuns.Count} runs");
            return $"body runs {bodyRuns.Count}";
        }
        var (bodyTop, bodyBottom) = bodyRuns[0];
        int bodyHeight = bodyBottom - bodyTop + 1;
        if (bodyHeight < 3)
            p.Fail("candle body", $"the body at x={xSide:F0} is only {bodyHeight}px tall");

        // Overall candle span (wick included) on the centre column.
        var candle = p.ContentRunsInColumn(x, yTop, yBottom, strong: true);
        if (candle.Count != 1)
        {
            p.Fail("candle wick", $"expected one candle at x={x:F0}, found {candle.Count} runs");
            return $"candle runs {candle.Count}";
        }
        var (wickTop, wickBottom) = candle[0];
        if (bodyTop < wickTop - 2 || bodyBottom > wickBottom + 2)
            p.Fail("candle span", $"the body {bodyTop}..{bodyBottom} is not inside the candle {wickTop}..{wickBottom}");
        if (wickTop > bodyTop - 4 || wickBottom < bodyBottom + 4)
            p.Fail("candle wick", $"the wick {wickTop}..{wickBottom} does not reach past the body {bodyTop}..{bodyBottom}");

        // Both widths are measured inside the body's own span, so the chart's Y axis (and the faint
        // category grid line) cannot be mistaken for the candle.
        int bodyWidth = 0;
        foreach (var (start, end) in p.ContentRunsInRow((bodyTop + bodyBottom) / 2f, x - slot * 0.3f, x + slot * 0.3f, strong: true))
            bodyWidth = Math.Max(bodyWidth, end - start + 1);

        int wickWidth = 0;
        foreach (var (start, end) in p.ContentRunsInRow((wickTop + bodyTop) / 2f, x - slot * 0.3f, x + slot * 0.3f, strong: true))
            wickWidth = Math.Max(wickWidth, end - start + 1);

        if (wickWidth == 0)
            p.Fail("candle wick", $"the upper wick of the first candle is not drawn at y={(wickTop + bodyTop) / 2f:F0}");
        if (bodyWidth < 10)
            p.Fail("candle body", $"the body at y={(bodyTop + bodyBottom) / 2f:F0} is only {bodyWidth}px wide");
        if (bodyWidth <= wickWidth)
            p.Fail("candle body", $"the body is {bodyWidth}px wide and the wick {wickWidth}px, want a wider body");

        return $"candle {wickTop}..{wickBottom}, body {bodyWidth}x{bodyHeight}px, wick {wickWidth}px";
    }

    // ── Heatmap ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Heatmap: every cell centre of the matrix carries a cell, neighbouring cells of one row hold
    /// different values and therefore different colours, and the grid has one cell per (column, row) pair
    /// of the data - the row bands come from the row category count.
    /// </summary>
    private static string CheckHeatmap(KindProbe p, ChartRenderCase c)
    {
        int cols = DistinctField(c, c.XField);            // Heatmap: X = column, Y = row
        int rows = DistinctField(c, c.YField);
        float gap = 1f;                                   // HeatmapMark.CellGap
        float cellW = (p.Plot.Width - gap * (cols - 1)) / cols;

        var colours = new Color[rows, cols];
        int distinct = 0;
        for (int row = 0; row < rows; row++)
        {
            float y = p.Plot.MapY(Band(row, rows));       // the cell is centred on its row band
            for (int col = 0; col < cols; col++)
            {
                float x = p.Plot.X + col * (cellW + gap) + cellW / 2f;
                colours[row, col] = p.At(x, y);
                if (!p.IsContentAt(x, y))
                {
                    p.Fail("heatmap cell", $"the cell centre of row {row}, column {col} is background");
                    continue;
                }
                distinct = Math.Max(distinct, p.NoteColour(colours[row, col]));
            }
        }

        for (int row = 0; row < rows; row++)
            for (int col = 0; col + 1 < cols; col++)
                if (ColourDistance(colours[row, col], colours[row, col + 1]) <= 0.05f)
                    p.Fail("heatmap colour", $"row {row} paints the cells {col} and {col + 1} in the same colour");

        if (distinct < 2)
            p.Fail("heatmap ramp", $"only {distinct} distinct cell colours over the {rows}x{cols} matrix, want a ramp");

        return $"{rows}x{cols} cells, {distinct} colours";
    }

    // ── Treemap ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Treemap: the cells tile the plot rectangle (most of it is painted) in several different colours.
    /// </summary>
    private static string CheckTreemap(KindProbe p)
    {
        float covered = p.ContentRatio(p.Plot.X, p.Plot.Y, p.Plot.X + p.Plot.Width, p.Plot.Y + p.Plot.Height);
        // A treemap tiles its plot: the shipped squarified layout lands at 97%, so the floor is set just
        // below that instead of at half the plot (which a badly broken tiling would still clear).
        if (covered < 0.85f)
            p.Fail("treemap cells", $"the cells cover only {covered:P0} of the plot rectangle");

        int distinct = 0;
        for (float y = p.Plot.Y + 4; y < p.Plot.Y + p.Plot.Height - 4; y += 7f)
            for (float x = p.Plot.X + 4; x < p.Plot.X + p.Plot.Width - 4; x += 7f)
                if (p.IsContentAt(x, y)) distinct = Math.Max(distinct, p.NoteColour(p.At(x, y)));
        if (distinct < 2)
            p.Fail("treemap colour", $"the sampled cells hold {distinct} distinct colours, want several");

        return $"{covered:P0} of the plot tiled, {distinct} colours";
    }

    // ── Sunburst ────────────────────────────────────────────────────────────

    /// <summary>
    /// Sunburst: the arcs fill a ring at four fifths of the outer radius, they come in the palette colours
    /// of the branches, and the hub is the hole in the middle. The rings live inside a disc, so the
    /// coverage is measured against the disc's bounding square (pi/4 of which is the disc itself) instead
    /// of the whole plot rectangle.
    /// </summary>
    private static string CheckSunburst(KindProbe p)
    {
        float minDim = MathF.Min(p.Plot.Width, p.Plot.Height);
        float cx = p.Plot.X + p.Plot.Width / 2f;
        float cy = p.Plot.Y + p.Plot.Height / 2f;
        float maxR = minDim / 2f * 0.9f;                 // SunburstMark.RadiusFactor
        float innerR = maxR * 0.15f;                     // SunburstMark.InnerRadiusRatio

        const int samples = 24;
        int covered = 0, distinct = 0;
        for (int i = 0; i < samples; i++)
        {
            var point = OnCircle(cx, cy, maxR * 0.85f, MathF.Tau * i / samples);
            if (!p.IsContentAt(point.X, point.Y)) continue;
            covered++;
            distinct = Math.Max(distinct, p.NoteColour(p.At(point.X, point.Y)));
        }
        if (covered < samples * 5 / 6)
            p.Fail("sunburst rings", $"only {covered}/{samples} probes on the outer ring are painted");
        if (distinct < 2)
            p.Fail("sunburst branches", $"the outer ring holds {distinct} distinct colours, want the branch palette");

        // The hub is the hole of the innermost ring. The probe sits inside that hole but clear of the
        // ring's label, which is drawn on the circle's left in this hierarchy.
        if (p.IsContentAt(cx + innerR * 0.5f, cy))
            p.Fail("sunburst hub", "the middle of the sunburst is painted, want the hole of the innermost ring");

        // The disc's bounding square: a ring structure paints most of it, an empty chart paints none. The
        // floor is set from what the shipped hierarchy paints (76% at the time of writing), not from "more
        // than nothing" - an accidental 0.5 would let a half-drawn sunburst through.
        float cover = p.ContentRatio(cx - maxR, cy - maxR, cx + maxR, cy + maxR);
        if (cover < 0.6f)
            p.Fail("sunburst coverage", $"only {cover:P0} of the disc's bounding square is painted");

        return $"ring {covered}/{samples}, {distinct} colours, {cover:P0} of the bbox";
    }

    // ── Sankey / Chord ──────────────────────────────────────────────────────

    /// <summary>
    /// Sankey: the ribbons and node bars cover a real part of the plot rectangle and nearly all of the
    /// paint lies inside it (the labels sit next to their nodes). A flow chart paints no grid, so the
    /// content inside the plot can only come from the mark.
    /// </summary>
    private static string CheckFlows(KindProbe p, string what)
    {
        int inside = p.CountContent(p.Plot.X, p.Plot.Y, p.Plot.X + p.Plot.Width, p.Plot.Y + p.Plot.Height);
        int total = p.CountContent(0, 0, p.Width - 1, p.Height - 1);
        float plotArea = p.Plot.Width * p.Plot.Height;
        float ratio = plotArea <= 0f ? 0f : inside / plotArea;

        // Sankey paints 73% of its plot rectangle and chord 53% (measured with the shipped data), so the floor
        // sits at 0.4 rather than at 0.05: "a few ribbons somewhere in the plot" is not a sankey or a chord.
        if (ratio < 0.4f)
            p.Fail(what, $"only {ratio:P1} of the plot rectangle is painted");
        if (total <= 0 || inside < total * 0.7f)
            p.Fail(what, $"only {inside} of the {total} painted pixels lie inside the plot rectangle");

        return $"{ratio:P1} of the plot painted, {inside}/{total} px inside it";
    }

    /// <summary>
    /// Chord: the node arcs form a nearly closed ring at the outer radius (only the small arc gaps are
    /// missing) and the ribbons fill the disc inside them - the node arcs live outside that disc, so the
    /// ink inside it can only be the chords. The paint as a whole lies inside the plot rectangle.
    /// </summary>
    private static string CheckChord(KindProbe p)
    {
        string flows = CheckFlows(p, "the chord ribbons and node arcs");

        float minDim = MathF.Min(p.Plot.Width, p.Plot.Height);
        float cx = p.Plot.X + p.Plot.Width / 2f;
        float cy = p.Plot.Y + p.Plot.Height / 2f;
        float radius = minDim / 2f * 0.85f;              // ChordMark.RadiusFactor
        float innerR = radius * (1f - 0.06f);            // inner edge of the node arc band (ArcWidthRatio)
        float bandR = radius * (1f - 0.06f / 2f);        // middle of that band

        const int samples = 36;
        int covered = 0;
        for (int i = 0; i < samples; i++)
        {
            var point = OnCircle(cx, cy, bandR, MathF.Tau * i / samples);
            if (p.IsContentAt(point.X, point.Y)) covered++;
        }
        if (covered < samples * 3 / 4)
            p.Fail("chord node arcs", $"only {covered}/{samples} probes on the outer ring are painted, want the arc ring");

        int inside = 0, total = 0;
        for (int y = (int)(cy - innerR); y <= (int)(cy + innerR); y++)
            for (int x = (int)(cx - innerR); x <= (int)(cx + innerR); x++)
            {
                float dx = x - cx, dy = y - cy;
                if (dx * dx + dy * dy > innerR * innerR) continue;
                total++;
                if (p.IsContentAt(x, y)) inside++;
            }
        float ribbon = total == 0 ? 0f : (float)inside / total;
        // The shipped chord fills 85% of the inner disc; a floor of 0.05 let a single ribbon pass.
        if (ribbon < 0.6f)
            p.Fail("chord ribbons", $"only {ribbon:P1} of the disc inside the node arcs is painted, want the ribbons");

        return $"{flows}; arc ring {covered}/{samples}, ribbons {ribbon:P1} of the inner disc";
    }

    // ── Gauge ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Gauge: the value arc ends where the value's position on the scale says it ends. Just past that
    /// angle only the faint track is left, and the pivot is not painted in the pointer's colour.
    /// </summary>
    private static string CheckGauge(KindProbe p, ChartRenderCase c)
    {
        // The value axis of a gauge is auto-fitted like any numeric channel, so the same public scale
        // reproduces the position the mark sweeps to.
        double value = Number(c, 0, c.YField);
        var scale = new LinearScale();
        scale.Fit(new object[] { value });
        float norm = Math.Clamp((float)scale.Map(value), 0f, 1f);

        float minDim = MathF.Min(p.Plot.Width, p.Plot.Height);
        float cx = p.Plot.X + p.Plot.Width / 2f;
        float cy = p.Plot.Y + p.Plot.Height / 2f;
        float outerR = minDim / 2f * 0.85f;              // GaugeMark.RadiusFactor
        float bandR = outerR * (1f - 0.12f / 2f);        // middle of the arc band (ArcWidth)

        float start = -210f * MathF.PI / 180f;
        float sweep = 240f * MathF.PI / 180f;            // StartAngleDeg .. EndAngleDeg
        float end = start + sweep * norm;

        var pointer = OnCircle(cx, cy, bandR, end - 0.06f);
        if (!p.IsStrongAt(pointer.X, pointer.Y))
            p.Fail("gauge pointer", $"the value arc does not reach the value's end at ({pointer.X:F0},{pointer.Y:F0})");

        var beyond = OnCircle(cx, cy, bandR, end + 0.15f);
        if (p.IsStrongAt(beyond.X, beyond.Y))
            p.Fail("gauge pointer", $"the value arc sweeps past the value's end at ({beyond.X:F0},{beyond.Y:F0})");

        if (ColourDistance(p.At(cx, cy), p.At(pointer.X, pointer.Y)) < 0.05f)
            p.Fail("gauge pivot", "the pivot is painted in the pointer's colour");

        return $"pointer at {norm:P0} of the scale";
    }

    // ── Funnel ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Funnel: the stages are stacked top to bottom and each one is narrower than the one above, measured
    /// as the horizontal run through the middle of every stage band. A funnel paints no grid, so a run is
    /// a stage.
    /// </summary>
    private static string CheckFunnel(KindProbe p)
    {
        float cx = p.Plot.X + p.Plot.Width / 2f;
        var stages = p.ContentRunsInColumn(cx, p.Plot.Y + 1f, p.Plot.Y + p.Plot.Height - 1f);
        if (stages.Count < 3)
        {
            p.Fail("funnel stages", $"found {stages.Count} stages in the middle column, want the stacked stages");
            return $"{stages.Count} stages";
        }

        var widths = new List<int>();
        foreach (var (top, bottom) in stages)
        {
            float y = top + Math.Min(3f, (bottom - top) / 4f);
            int width = 0;
            foreach (var (start, end) in p.ContentRunsInRow(y, p.Plot.X + 1, p.Plot.X + p.Plot.Width - 1))
                width = Math.Max(width, end - start + 1);
            widths.Add(width);
        }

        for (int i = 1; i < widths.Count; i++)
            if (widths[i] >= widths[i - 1])
                p.Fail("funnel width", $"stage {i} is {widths[i]}px wide, not narrower than stage {i - 1} at {widths[i - 1]}px");

        return string.Join(">", widths) + " px";
    }

    // ── Waffle ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Waffle: the cells of one row repeat on a fixed pitch. The row is a cell row (the grid's rows land
    /// on the plot height, so the middle of the plot can fall into a gap between two rows), and the runs
    /// in it are the individual cells.
    /// </summary>
    private static string CheckWaffle(KindProbe p)
    {
        float midY = p.Plot.Y + p.Plot.Height / 2f;

        // A column that really runs through a stack of cells, then the cell row nearest the middle.
        List<(int Start, int End)>? rows = null;
        for (int x = (int)p.Plot.X + 2; x < (int)(p.Plot.X + p.Plot.Width) - 2 && rows is null; x++)
        {
            var candidate = p.ContentRunsInColumn(x, p.Plot.Y + 1f, p.Plot.Y + p.Plot.Height - 1f);
            if (candidate.Count >= 5) rows = candidate;
        }
        if (rows is null)
        {
            p.Fail("waffle grid", "no column of the plot holds a stack of cells");
            return "no cell column";
        }

        (int Start, int End) band = rows[0];
        float best = float.MaxValue;
        foreach (var run in rows)
        {
            float distance = midY < run.Start ? run.Start - midY : midY > run.End ? midY - run.End : 0f;
            if (distance < best) { best = distance; band = run; }
        }

        float y = (band.Start + band.End) / 2f;
        var cells = p.ContentRunsInRow(y, p.Plot.X + 1, p.Plot.X + p.Plot.Width - 1);
        if (cells.Count < 4)
        {
            p.Fail("waffle cells", $"the middle cell row (y={y:F0}) holds only {cells.Count} cells");
            return $"{cells.Count} cells";
        }

        int pitch1 = cells[1].Start - cells[0].Start;
        int pitch2 = cells[2].Start - cells[1].Start;
        int pitch3 = cells[3].Start - cells[2].Start;
        if (Math.Abs(pitch1 - pitch2) > 2 || Math.Abs(pitch2 - pitch3) > 2)
            p.Fail("waffle pitch", $"the cell pitch {pitch1}/{pitch2}/{pitch3}px does not repeat along the row");
        if (pitch1 <= 0)
            p.Fail("waffle pitch", $"the cells overlap (pitch {pitch1}px)");

        return $"{cells.Count} cells, pitch {pitch1}/{pitch2}/{pitch3}px";
    }

    // ── Timeline ────────────────────────────────────────────────────────────

    /// <summary>
    /// Timeline: the first lane holds one horizontal bar that reaches from its start value to its end
    /// value, and the row is background before the earliest start. The bar is found as the one strong run
    /// in the lane (the faint value grid lines are not a bar).
    /// </summary>
    /// <summary>
    /// Timeline: the first lane holds exactly one bar, that bar is solid (no holes) and it starts at its own
    /// <c>start</c> value instead of being clipped onto the plot's left edge.
    /// <para>
    /// "Nothing is painted left of the bar" is checked with <see cref="KindProbe.IsStrongAt"/>, because the
    /// chart draws a horizontal grid line through the category band itself: on this lane the whole row from
    /// the plot's left edge is the grid blend (<c>#14141e</c> background → <c>#24242d</c>), while the bar is
    /// the palette colour (<c>#4996f9</c>). A plain "differs from the background" test therefore reported the
    /// chart's own decoration as a bar starting at the plot edge.
    /// </para>
    /// </summary>
    private static string CheckTimeline(KindProbe p, ChartRenderCase c)
    {
        int lanes = DistinctField(c, c.XField);           // Timeline: Y = categories, X = the interval
        float y = p.Plot.MapY(Band(0, lanes));

        var bars = p.ContentRunsInRow(y, p.Plot.X + 3, p.Plot.X + p.Plot.Width - 1, strong: true);
        if (bars.Count != 1)
        {
            p.Fail("timeline bar", $"the first lane (y={y:F0}) holds {bars.Count} bars, want one");
            return $"{bars.Count} bars";
        }

        var (start, end) = bars[0];
        int width = end - start + 1;
        if (width < p.Plot.Width * 0.2f)
            p.Fail("timeline bar", $"the first lane's bar is only {width}px wide");

        foreach (float fraction in new[] { 0.25f, 0.5f, 0.75f })
            if (!p.IsContentAt(start + width * fraction, y))
                p.Fail("timeline bar", $"the first lane's bar has a hole at x={start + width * fraction:F0}");

        // Nothing bar-like is drawn left of the earliest start: the bar starts after the plot's left edge
        // (the interval axis is padded) and the pixel just before it carries no strong ink. A weak grid line
        // running along the lane is chart decoration and must not be mistaken for the bar.
        if (start <= p.Plot.X + 3f)
            p.Fail("timeline start", $"the first bar starts at x={start} at the plot edge instead of its start value");
        if (p.IsStrongAt(start - 6f, y))
            p.Fail("timeline start", $"the lane at x={start - 6f:F0}, before the earliest start, carries bar ink");

        return $"first bar {start}..{end}px on lane y={y:F0}";
    }

    // ── Lollipop ────────────────────────────────────────────────────────────

    /// <summary>
    /// Lollipop: a stem rises from the data zero line to a dot, and the dot is a small round symbol at the
    /// stem's top - measured as the horizontal run just under that top, where a bare stem would only be a
    /// few pixels wide.
    /// </summary>
    private static string CheckLollipop(KindProbe p, ChartRenderCase c)
    {
        int categories = DistinctField(c, c.XField);
        float x = p.Plot.MapX(Band(0, categories));
        float baseline = p.Plot.MapY(0);

        var stem = p.ContentRunsInColumn(x, p.Plot.Y + 2f, p.Plot.Y + p.Plot.Height - 1f, strong: true);
        if (stem.Count != 1)
        {
            p.Fail("lollipop stem", $"expected one stem at x={x:F0}, found {stem.Count} runs");
            return $"stem runs {stem.Count}";
        }

        var (top, bottom) = stem[0];
        if (bottom < baseline - 4f)
            p.Fail("lollipop stem", $"the stem ends at y={bottom}, not at the baseline y={baseline:F0}");
        if (bottom - top < p.Plot.Height * 0.15f)
            p.Fail("lollipop stem", $"the stem is only {bottom - top}px tall");

        int dotWidth = 0;
        foreach (var (start, end) in p.ContentRunsInRow(top + 4f, x - 15, x + 15))
            dotWidth = Math.Max(dotWidth, end - start + 1);
        if (dotWidth < 5 || dotWidth > 20)
            p.Fail("lollipop dot", $"the symbol at the stem's top is {dotWidth}px wide, want a dot");

        return $"stem {top}..{bottom}px, dot {dotWidth}px wide";
    }

    // ── Milestone ───────────────────────────────────────────────────────────

    /// <summary>
    /// Milestone: every lane carries at least one marker, and the markers are small symbols - probed four
    /// pixels above the lane's axis line, where only a marker reaches and the line itself does not.
    /// </summary>
    /// <summary>
    /// A geographic bubble chart: round marks at the coordinates the rows carry, so the middle band of the
    /// plot holds several separate blobs of content rather than one continuous area.
    /// </summary>
    private static string CheckGeoBubble(KindProbe p)
    {
        // Bubbles are small: instead of counting runs along one line, mark the cells of a coarse grid that hold
        // any strong content, which is what tells "several separate marks are drawn" from "one shape is drawn".
        const int Columns = 6, Rows = 4;
        var filled = new bool[Columns * Rows];
        int cells = 0;
        for (float x = p.Plot.X + 1f; x < p.Plot.X + p.Plot.Width - 1f; x += 3f)
        {
            for (float y = p.Plot.Y + 1f; y < p.Plot.Y + p.Plot.Height - 1f; y += 3f)
            {
                if (!p.IsStrongAt(x, y)) continue;
                int column = Math.Min(Columns - 1, (int)((x - p.Plot.X) / p.Plot.Width * Columns));
                int row = Math.Min(Rows - 1, (int)((y - p.Plot.Y) / p.Plot.Height * Rows));
                int cell = (row * Columns) + column;
                if (filled[cell]) continue;
                filled[cell] = true;
                cells++;
            }
        }

        if (cells < 3)
        {
            p.Fail("bubbles", $"only {cells} cell(s) of the plot hold a bubble");
            return "no bubbles";
        }
        return $"{cells} cell(s) hold a bubble";
    }

    /// <summary>
    /// A geographic area chart: the rows became a grid of regions shaded by their value, so across the
    /// middle of the plot there are at least two distinctly shaded regions.
    /// </summary>
    private static string CheckGeoArea(KindProbe p)
    {
        // The grid has more than one row of cells, and a scan line through a border row would see nothing
        // but borders: three lines across the plot, so at least one of them runs through cell bodies.
        var shades = new List<Color>();
        foreach (float fraction in new[] { 0.3f, 0.5f, 0.7f })
        {
            float y = p.Plot.Y + (p.Plot.Height * fraction);
            for (float x = p.Plot.X + 2f; x < p.Plot.X + p.Plot.Width - 2f; x += 2f)
            {
                if (!p.IsStrongAt(x, y)) continue;
                var colour = p.At(x, y);
                if (!shades.Contains(colour)) shades.Add(colour);
            }
        }

        if (shades.Count < 2)
        {
            p.Fail("shaded regions", $"the plot shows {shades.Count} distinct region colour(s)");
            return "no region shading";
        }
        return $"{shades.Count} region shade(s) across the plot";
    }

    private static string CheckMilestone(KindProbe p, ChartRenderCase c)
    {
        int lanes = DistinctField(c, c.YField);
        int markers = 0, widest = 0;

        for (int lane = 0; lane < lanes; lane++)
        {
            float laneY = p.Plot.MapY(Band(lane, lanes));
            if (!p.Plot.Contains(p.Plot.X + p.Plot.Width / 2f, laneY))
            {
                p.Fail("milestone lane", $"lane {lane} sits at y={laneY:F0}, outside the plot");
                continue;
            }

            var runs = p.ContentRunsInRow(laneY - 4f, p.Plot.X + 3f, p.Plot.X + p.Plot.Width - 1f, strong: true);
            if (runs.Count == 0)
            {
                p.Fail("milestone marker", $"lane {lane} has no marker above its axis line (y={laneY:F0})");
                continue;
            }

            markers += runs.Count;
            foreach (var (start, end) in runs)
                widest = Math.Max(widest, end - start + 1);
        }

        if (widest > 16)
            p.Fail("milestone marker", $"a marker is {widest}px wide, want a small symbol");

        return $"{markers} markers on {lanes} lanes";
    }

    // ── The pixel probe ─────────────────────────────────────────────────────

    /// <summary>
    /// One kind's read access to the surface it just painted: the palette background to compare against,
    /// the plot area the chart laid out, and the per-kind problem list a failed probe writes into.
    /// Every measurement goes through here, so a probe cannot accidentally read outside the image.
    /// </summary>
    private sealed class KindProbe
    {
        private readonly ChartRenderCase _case;
        private readonly Image _image;
        private readonly Color _background;
        private readonly List<Color> _colours = new();

        public KindProbe(ChartRenderCase c, Image image, Color background, PlotArea plot, List<string> problems)
        {
            _case = c;
            _image = image;
            _background = background;
            Plot = plot;
            Problems = problems;
        }

        public PlotArea Plot { get; }
        public List<string> Problems { get; }

        public int Width => _image.GetWidth();
        public int Height => _image.GetHeight();

        /// <summary>The pixel colour at a position, with the position clamped to the surface.</summary>
        public Color At(float x, float y)
            => _image.GetPixel(Math.Clamp((int)MathF.Round(x), 0, Width - 1),
                               Math.Clamp((int)MathF.Round(y), 0, Height - 1));

        public bool IsBackgroundAt(float x, float y) => IsBackground(At(x, y), _background);

        public bool IsContentAt(float x, float y) => !IsBackgroundAt(x, y);

        /// <summary>Content that is more than a decoration line (see <see cref="StrongContent"/>).</summary>
        public bool IsStrongAt(float x, float y) => ColourDistance(At(x, y), _background) > StrongContent;

        /// <summary>Record a missing feature, naming the kind and the feature it protects.</summary>
        public void Fail(string feature, string detail) => Problems.Add($"{_case.Label}: {feature} - {detail}");

        /// <summary>How many distinct colours a probe has seen so far (within a small tolerance).</summary>
        public int NoteColour(Color colour)
        {
            foreach (var seen in _colours)
                if (ColourDistance(seen, colour) <= 0.05f) return _colours.Count;
            _colours.Add(colour);
            return _colours.Count;
        }

        /// <summary>Fraction of a pixel rectangle that is painted (optionally: painted strongly).</summary>
        public float ContentRatio(float x0, float y0, float x1, float y1, bool strong = false)
        {
            var (fromX, toX, fromY, toY) = PixelBounds(x0, y0, x1, y1);
            int total = 0, painted = 0;
            for (int y = fromY; y <= toY; y++)
                for (int x = fromX; x <= toX; x++)
                {
                    total++;
                    if (strong ? IsStrongAt(x, y) : IsContentAt(x, y)) painted++;
                }
            return total == 0 ? 0f : (float)painted / total;
        }

        /// <summary>Number of painted pixels in a rectangle.</summary>
        public int CountContent(float x0, float y0, float x1, float y1)
        {
            var (fromX, toX, fromY, toY) = PixelBounds(x0, y0, x1, y1);
            int painted = 0;
            for (int y = fromY; y <= toY; y++)
                for (int x = fromX; x <= toX; x++)
                    if (IsContentAt(x, y)) painted++;
            return painted;
        }

        /// <summary>Contiguous painted runs (inclusive pixel bounds) along a column, top to bottom.</summary>
        public List<(int Start, int End)> ContentRunsInColumn(float x, float y0, float y1, bool strong = false)
        {
            var runs = new List<(int Start, int End)>();
            int px = Math.Clamp((int)MathF.Round(x), 0, Width - 1);
            int start = -1;
            for (int y = ClampY(y0); y <= ClampY(y1); y++)
            {
                if (strong ? IsStrongAt(px, y) : IsContentAt(px, y))
                {
                    if (start < 0) start = y;
                }
                else if (start >= 0)
                {
                    runs.Add((start, y - 1));
                    start = -1;
                }
            }
            if (start >= 0) runs.Add((start, ClampY(y1)));
            return runs;
        }

        /// <summary>Contiguous painted runs (inclusive pixel bounds) along a row, left to right.</summary>
        public List<(int Start, int End)> ContentRunsInRow(float y, float x0, float x1, bool strong = false)
        {
            var runs = new List<(int Start, int End)>();
            int py = Math.Clamp((int)MathF.Round(y), 0, Height - 1);
            int start = -1;
            for (int x = ClampX(x0); x <= ClampX(x1); x++)
            {
                if (strong ? IsStrongAt(x, py) : IsContentAt(x, py))
                {
                    if (start < 0) start = x;
                }
                else if (start >= 0)
                {
                    runs.Add((start, x - 1));
                    start = -1;
                }
            }
            if (start >= 0) runs.Add((start, ClampX(x1)));
            return runs;
        }

        private int ClampX(float x) => Math.Clamp((int)MathF.Ceiling(x), 0, Width - 1);
        private int ClampY(float y) => Math.Clamp((int)MathF.Ceiling(y), 0, Height - 1);
        private int ClampXMax(float x) => Math.Clamp((int)MathF.Floor(x), 0, Width - 1);
        private int ClampYMax(float y) => Math.Clamp((int)MathF.Floor(y), 0, Height - 1);

        /// <summary>Inclusive pixel bounds of a rectangle, clamped to the surface.</summary>
        private (int FromX, int ToX, int FromY, int ToY) PixelBounds(float x0, float y0, float x1, float y1)
            => (ClampX(x0), ClampXMax(x1), ClampY(y0), ClampYMax(y1));
    }

// ── Surface transparency ────────────────────────────────────────────────

    /// <summary>
    /// The surface is always cleared transparent and the background is what the background renderer paints,
    /// so a theme whose <see cref="ChartTheme.BackgroundColor"/> has a zero alpha really is see-through on
    /// the real backend - the pixel keeps alpha 0 instead of the theme colour.
    /// </summary>
    [TestCase]
    public void ATransparentBackgroundLeavesTheSurfaceSeeThrough()
    {
        const string name = nameof(ATransparentBackgroundLeavesTheSurfaceSeeThrough);
        if (NoRenderingDevice(name)) return;

        var c = ChartRenderCase.All[0];   // the kind does not matter: only the background is measured
        var view = AddView(c, ViewSize);
        try
        {
            var theme = ChartTheme.Dark().Clone();
            theme.BackgroundColor = Colors.Transparent;
            view.CustomTheme = theme;
            view.SetData(c.Rows);
            Pump(view);

            var image = Pixels(view);
            AssertThat(image is not null).IsTrue();

            // Well inside the surface, above the plot area: only the background would paint here.
            // The comparison is a tolerance rather than an exact 0/1: the readback is 8 bit, so a value that
            // should be fully transparent can come back as one least-significant digit, and the antialiased
            // edge of a rounded rect lands somewhere in between on any driver.
            var pixel = image!.GetPixel(40, 12);
            AssertThat(pixel.A).IsLess(1f / 255f);
        }
        finally
        {
            Release(view);
        }
    }

    /// <summary>
    /// A rounded background is a real rounded rect now that the surface is transparent: the four corners
    /// stay alpha 0 while the middle of the surface is painted with the theme background.
    /// </summary>
    [TestCase]
    public void ARoundedBackgroundLeavesItsCornersTransparent()
    {
        const string name = nameof(ARoundedBackgroundLeavesItsCornersTransparent);
        if (NoRenderingDevice(name)) return;

        var c = ChartRenderCase.All[0];
        var view = AddView(c, ViewSize);
        try
        {
            var theme = ChartTheme.Dark().Clone();
            theme.BackgroundColor = new Color(0.20f, 0.10f, 0.30f);
            theme.BackgroundCornerRadius = 24f;
            view.CustomTheme = theme;
            view.SetData(c.Rows);
            Pump(view);

            var image = Pixels(view);
            AssertThat(image is not null).IsTrue();

            // With a 24 px radius the (2,2) and (6,6) pixels fall outside the rounded rect ...
            // Tolerance instead of exact 0/1: the readback is 8 bit and the corner edge is antialiased, so
            // "transparent" is a range and not a value (an exact comparison here fails on another driver).
            const float opaque = 1f - 1f / 255f;
            const float transparent = 1f / 255f;
            AssertThat(image!.GetPixel(2, 2).A).IsLess(transparent);
            AssertThat(image.GetPixel(6, 6).A).IsLess(transparent);

            // ... while the middle of the same surface is painted with the theme background, so the corner
            // is a notch rather than "nothing was drawn at all".
            var centre = image.GetPixel(image.GetWidth() / 2, image.GetHeight() - 6);
            AssertThat(centre.A).IsGreater(opaque);

            // A small radius is a different shape - (6,6) is inside the rect then - so this also proves the
            // radius itself is honoured instead of "some corner is transparent".
            theme.BackgroundCornerRadius = 8f;
            view.Refresh();
            Pump(view);
            var sharper = Pixels(view);
            AssertThat(sharper is not null).IsTrue();
            AssertThat(sharper!.GetPixel(6, 6).A).IsGreater(opaque);
            AssertThat(sharper.GetPixel(1, 1).A).IsLess(transparent);
        }
        finally
        {
            Release(view);
        }
    }


    // ── Theme changes ───────────────────────────────────────────────────────

    /// <summary>
    /// Editing the assigned theme resource has to reach the real surface on the next frame: the view
    /// watches the resource's <see cref="Resource.Changed"/> signal, rebuilds the chart on the new palette
    /// and the Skia backend repaints the pixels. The fake canvas only records calls, so it cannot tell a
    /// repaint from a texture that still holds the previous background.
    /// </summary>
    [TestCase]
    public void EditingTheThemeRepaintsTheSurfaceWithTheNewBackground()
    {
        const string name = nameof(EditingTheThemeRepaintsTheSurfaceWithTheNewBackground);
        if (NoRenderingDevice(name)) return;

        var c = ChartRenderCase.All[0];
        var deepBlue = new Color(0.03f, 0.05f, 0.45f);
        var deepRed = new Color(0.45f, 0.04f, 0.05f);
        var problems = new List<string>();

        float beforeRatio = 0f;
        float afterRatio = 0f;
        int differing = -1;

        var view = AddView(c, ViewSize);
        try
        {
            var theme = ChartTheme.Dark().Clone();
            theme.BackgroundColor = deepBlue;
            view.CustomTheme = theme;
            view.SetData(c.Rows);
            Pump(view);

            var before = Pixels(view);
            if (before is null)
            {
                problems.Add("the surface published no texture");
            }
            else
            {
                // The chart is padded by 20 px, so above the plot area only the background paints.
                var corner = before.GetPixel(40, 12);
                beforeRatio = BackgroundRatio(before, deepBlue);
                if (!IsBackground(corner, deepBlue))
                    problems.Add($"before the edit (40, 12) is {corner}, want the theme background {deepBlue}");
                if (beforeRatio < MinBackgroundRatio)
                    problems.Add($"before the edit only {beforeRatio:P2} of the surface is the theme background");
                if (BackgroundRatio(before, deepRed) > MaxStaleBackgroundRatio)
                    problems.Add("the surface already shows the colour the edit is about to set");
            }

            // Edit the resource in place, the way the Inspector does, and notify: the instance stays the
            // same, so only the resource's own signal can tell the view that the palette moved.
            theme.BackgroundColor = deepRed;
            theme.EmitChanged();
            Pump(view);

            var after = Pixels(view);
            if (after is null)
            {
                problems.Add("the edited theme left the surface without a texture");
            }
            else
            {
                var corner = after.GetPixel(40, 12);
                afterRatio = BackgroundRatio(after, deepRed);
                if (!IsBackground(corner, deepRed))
                    problems.Add($"after the edit (40, 12) is {corner}, want the new theme background {deepRed}");
                if (afterRatio < MinBackgroundRatio)
                    problems.Add($"after the edit only {afterRatio:P2} of the surface is the new theme background");
                if (BackgroundRatio(after, deepBlue) > MaxStaleBackgroundRatio)
                    problems.Add("the previous background survived the theme edit");

                if (before is not null)
                {
                    differing = DifferingPixels(before, after);
                    int total = after.GetWidth() * after.GetHeight();
                    if (differing < total / 2)
                        problems.Add($"only {differing} of {total} pixels changed with the theme");
                }
            }
        }
        finally
        {
            Release(view);
        }

        GD.Print($"{name}: background {beforeRatio:P2} → {afterRatio:P2}, {differing} pixels changed");
        AssertThat(string.Join("; ", problems)).IsEqual("");
    }

    // ── Hover and selection ─────────────────────────────────────────────────

    /// <summary>
    /// Hovering a bar and selecting it have to show up in the pixels of the real surface: the crosshair and
    /// the tooltip bubble add content, the hovered bar is widened, and the click strokes the selection ring
    /// on top. The fake canvas only records the calls, so a tooltip that is laid out but never rasterised -
    /// or a selection that never reaches the backend - passes there and fails here.
    /// </summary>
    [TestCase]
    public void HoverAndSelectionPaintTheirOverlaysOnTheRealSurface()
    {
        const string name = nameof(HoverAndSelectionPaintTheirOverlaysOnTheRealSurface);
        if (NoRenderingDevice(name)) return;

        var c = ChartRenderCase.All[0];
        AssertThat(c.Kind).IsEqual(ChartKind.Bar);   // the pointer position below assumes bars

        var problems = new List<string>();
        int idleContent;
        int hoverContent = 0;
        int selectedContent = 0;
        int hoverChanged = -1;
        int selectionChanged = -1;

        var view = AddView(c, ViewSize);
        try
        {
            view.ShowCrosshair = true;
            view.ShowTooltip = true;
            view.SetData(c.Rows);
            Pump(view);

            ContentRatio(view, out idleContent);
            var idleImage = Pixels(view);
            if (idleImage is null)
                problems.Add("the surface published no texture");

            if (view.Chart is not { } chart || chart.CurrentPlotArea is not { } plot)
            {
                problems.Add("the chart published no plot area to hover over");
            }
            else
            {
                // The middle bar: the category band centre, measured from the live plot the chart laid out
                // rather than from a constant (the case has an odd number of rows, so it is a bar and not a gap).
                int rowCount = c.Rows.Count;
                int middleRow = rowCount / 2;
                var onMiddleBar = new Vector2(
                    plot.X + plot.Width * (float)((middleRow + 0.5) / rowCount),
                    plot.Y + plot.Height - 12f);

                view._GuiInput(new InputEventMouseMotion { Position = onMiddleBar });
                // The tooltip fades in over a few frames. Waiting for the surface to settle states that
                // precondition instead of betting on a frame count the fade could outgrow.
                if (ChartRenderHarness.PumpUntilStable(view) == 0)
                    problems.Add("the surface never settled after the hover");
                ContentRatio(view, out hoverContent);
                var hoverImage = Pixels(view);
                if (hoverImage is null)
                    problems.Add("the surface published no texture while hovering");

                if (chart.CurrentHoveredRowIndex != middleRow)
                    problems.Add($"hovering the middle bar reports row {chart.CurrentHoveredRowIndex}, " +
                                 $"want {middleRow}");
                if (hoverContent <= idleContent)
                    problems.Add($"the hover painted nothing: {hoverContent} content px, {idleContent} without it");
                if (idleImage is not null && hoverImage is not null)
                    hoverChanged = DifferingPixels(idleImage, hoverImage);

                view._GuiInput(new InputEventMouseButton
                {
                    ButtonIndex = MouseButton.Left,
                    Pressed = true,
                    Position = onMiddleBar,
                });
                if (ChartRenderHarness.PumpUntilStable(view) == 0)
                    problems.Add("the surface never settled after the click");
                ContentRatio(view, out selectedContent);
                var selectedImage = Pixels(view);
                if (selectedImage is null)
                    problems.Add("the surface published no texture after the click");

                if (chart.CurrentSelectedRowIndex < 0)
                    problems.Add($"the click selected nothing (index {chart.CurrentSelectedRowIndex})");
                if (hoverImage is not null && selectedImage is not null)
                {
                    selectionChanged = DifferingPixels(hoverImage, selectedImage);
                    if (selectionChanged == 0)
                        problems.Add("the selection changed no pixel of the surface");
                }
            }
        }
        finally
        {
            Release(view);
        }

        GD.Print($"{name}: content {idleContent} → hover {hoverContent} ({hoverChanged} px changed) " +
                 $"→ selected {selectedContent} ({selectionChanged} px changed)");
        AssertThat(string.Join("; ", problems)).IsEqual("");
    }

    // ── Frame stability ─────────────────────────────────────────────────────

    /// <summary>
    /// The second frame of the same chart has to draw exactly the same pixels as the first one. Marks
    /// reuse one cached path and one cached paint, and on the real backend a native object disposed on
    /// the first frame is only noticed on the next draw - the fake canvas cannot see that. The redraw
    /// goes through <see cref="ChartView.Repaint"/>, so it is the same mark instances drawing again.
    /// </summary>
    [TestCase]
    public void TwoConsecutiveFramesOfTheSameChartDrawTheSamePixels()
    {
        const string name = nameof(TwoConsecutiveFramesOfTheSameChartDrawTheSamePixels);
        if (NoRenderingDevice(name)) return;

        var report = new List<string>();
        var problems = new List<string>();

        foreach (var c in ChartRenderCase.All)
        {
            var view = AddView(c, ViewSize);
            try
            {
                view.SetData(c.Rows);
                Pump(view);
                float firstRatio = ContentRatio(view, out int first);
                var firstImage = Pixels(view);

                view.Repaint();          // same chart, same marks, same cached drawing objects
                Pump(view);
                float secondRatio = ContentRatio(view, out int second);
                var secondImage = Pixels(view);

                if (first == 0)
                    problems.Add($"{c.Label}: the first frame drew nothing");
                if (second != first)
                    problems.Add($"{c.Label}: second frame has {second} content pixels, first had {first}");

                // Counting alone lets "3 pixels lost, 3 pixels gained" through: compare them one by one.
                if (firstImage is not null && secondImage is not null)
                {
                    int differing = DifferingPixels(firstImage, secondImage);
                    if (differing != 0)
                        problems.Add($"{c.Label}: {differing} pixel(s) differ between two frames of the same chart");
                }

                report.Add($"{c.Label,-12} frame 1 {first,6} px ({firstRatio:P2}) → frame 2 {second,6} px ({secondRatio:P2})");
            }
            catch (Exception ex)
            {
                problems.Add($"{c.Label}: the second frame threw {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                Release(view);
            }
        }

        GD.Print($"{name}: {ChartRenderCase.All.Count} kinds\n" + string.Join("\n", report));
        AssertThat(string.Join("; ", problems)).IsEqual("");
    }

    // ── Resize ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Growing the node re-creates the Skia surface at the new pixel size and the chart renders into it
    /// again (the chart is rebuilt for the new canvas size, not stretched from the old texture).
    /// </summary>
    [TestCase]
    public void TheSurfaceFollowsTheNodeSizeAndRendersAgain()
    {
        const string name = nameof(TheSurfaceFollowsTheNodeSizeAndRendersAgain);
        if (NoRenderingDevice(name)) return;

        var report = new List<string>();
        var problems = new List<string>();

        foreach (var c in ChartRenderCase.All)
        {
            var view = AddView(c, ViewSize);
            try
            {
                view.SetData(c.Rows);
                Pump(view);
                var before = view.Texture?.GetSize();

                view.Size = ResizedViewSize;
                Pump(view);
                var after = view.Texture?.GetSize();
                float ratio = ContentRatio(view, out int content);

                if (before != ViewSize)
                    problems.Add($"{c.Label}: before the resize the texture was {before}, want {ViewSize}");
                if (after != ResizedViewSize)
                    problems.Add($"{c.Label}: after the resize the texture is {after}, want {ResizedViewSize}");
                if (ratio < MinContentRatio)
                    problems.Add($"{c.Label}: only {ratio:P2} of the resized surface is drawn");

                report.Add($"{c.Label,-12} {before} → {after}, content {content,6} px = {ratio,6:P2}");
            }
            catch (Exception ex)
            {
                problems.Add($"{c.Label}: the resize threw {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                Release(view);
            }
        }

        GD.Print($"{name}: {ChartRenderCase.All.Count} kinds\n" + string.Join("\n", report));
        AssertThat(string.Join("; ", problems)).IsEqual("");
    }

    /// <summary>
    /// The same resize with a custom theme assigned: the new surface really is the new pixel size <i>and</i>
    /// the repaint still uses the theme's background. A resize that silently drops back to a built-in
    /// palette, or that presents the old texture stretched, only shows up in the pixels - the texture size
    /// alone would look right either way.
    /// </summary>
    [TestCase]
    public void ResizingKeepsTheBackgroundOfTheCustomTheme()
    {
        const string name = nameof(ResizingKeepsTheBackgroundOfTheCustomTheme);
        if (NoRenderingDevice(name)) return;

        var deepBlue = new Color(0.03f, 0.05f, 0.45f);
        var report = new List<string>();
        var problems = new List<string>();

        foreach (var c in ChartRenderCase.All)
        {
            var view = AddView(c, ViewSize);
            try
            {
                var theme = ChartTheme.Dark().Clone();
                theme.BackgroundColor = deepBlue;
                view.CustomTheme = theme;
                view.SetData(c.Rows);
                Pump(view);

                var before = view.Texture?.GetSize();
                var beforeImage = Pixels(view);
                float beforeRatio = beforeImage is null ? 0f : BackgroundRatio(beforeImage, deepBlue);

                if (before != ViewSize)
                    problems.Add($"{c.Label}: before the resize the texture was {before}, want {ViewSize}");
                if (beforeRatio < MinThemedBackgroundRatio)
                    problems.Add($"{c.Label}: before the resize only {beforeRatio:P2} of the surface is the theme background");

                view.Size = ResizedViewSize;
                Pump(view);

                var after = view.Texture?.GetSize();
                var afterImage = Pixels(view);
                float afterRatio = afterImage is null ? 0f : BackgroundRatio(afterImage, deepBlue);

                if (after != ResizedViewSize)
                    problems.Add($"{c.Label}: after the resize the texture is {after}, want {ResizedViewSize}");
                if (afterImage is null)
                    problems.Add($"{c.Label}: the resized surface published no texture");
                if (afterRatio < MinThemedBackgroundRatio)
                    problems.Add($"{c.Label}: after the resize only {afterRatio:P2} of the surface is the theme background");
                // A repaint that only kept a corner of the theme would clear the floor above; this keeps the
                // assertion tied to how much of the surface the theme covered before the resize.
                if (afterRatio < beforeRatio * 0.5f)
                    problems.Add($"{c.Label}: the theme background dropped from {beforeRatio:P2} to {afterRatio:P2} with the resize");

                report.Add($"{c.Label,-12} {before} → {after}, background {beforeRatio,6:P2} → {afterRatio,6:P2}");
            }
            catch (Exception ex)
            {
                problems.Add($"{c.Label}: the themed resize threw {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                Release(view);
            }
        }

        GD.Print($"{name}: {ChartRenderCase.All.Count} kinds\n" + string.Join("\n", report));
        AssertThat(string.Join("; ", problems)).IsEqual("");
    }

    // ── The pieces below ChartView ──────────────────────────────────────────

    /// <summary>
    /// The documented shortcut without the <see cref="ChartView"/> node - <see cref="Canvas2DFactory"/>
    /// plus <see cref="Chart"/> - renders through the real backend too, and the texture it publishes
    /// really holds the drawn pixels.
    /// </summary>
    [TestCase]
    public void TheCanvasAndChartPiecesRenderDirectlyThroughTheRealBackend()
    {
        const string name = nameof(TheCanvasAndChartPiecesRenderDirectlyThroughTheRealBackend);
        if (NoRenderingDevice(name)) return;

        var barCase = ChartRenderCase.All[0];
        AssertThat(barCase.Kind).IsEqual(ChartKind.Bar);   // the rows below assume the fields of case 0

        var canvas = Canvas2DFactory.Create(240, 160);
        try
        {
            AssertThat(canvas is SkiaCanvas2DBackend).IsTrue();

            var chart = new Chart(canvas) { Width = 240, Height = 160, Title = "Bars" };
            chart.Data(new List<DataRow>(barCase.Rows));
            chart.Mark(new IntervalMark());
            chart.Encode(Channel.X, barCase.XField);
            chart.Encode(Channel.Y, barCase.YField);

            for (int frame = 0; frame < 2; frame++)
            {
                // The low level frame loop: begin → clear → draw → commit (uploads the surface).
                canvas.BeginFrame();
                canvas.Clear(Colors.Black);
                chart.Render();
                canvas.EndFrame();
            }

            var image = ((SkiaCanvas2DBackend)canvas).SkiaTexture.GetImage();
            AssertThat(image.GetWidth()).IsEqual(240);
            AssertThat(image.GetHeight()).IsEqual(160);

            var measured = Measure(image);
            float ratio = (float)measured.Content / measured.Total;
            GD.Print($"{name}: content {measured.Content} px = {ratio:P2}");
            AssertThat(ratio > MinContentRatio).IsTrue();
        }
        finally
        {
            canvas.Dispose();
        }
    }

    /// <summary>
    /// <see cref="ICanvas2D.DrawCircle"/> really strokes the circle's outline on the real backend and never
    /// fills it (<c>Canvas2DBase.DrawCircle</c> routes through <c>Stroke</c>). The fake canvas counted the same
    /// call as a fill, which is exactly the kind of disagreement that lets a "fill count" case pass headless
    /// while the device paints nothing - so the contract is pinned where the pixels are real.
    /// </summary>
    [TestCase]
    public void TheRealBackendStrokesACircleInsteadOfFillingIt()
    {
        const string name = nameof(TheRealBackendStrokesACircleInsteadOfFillingIt);
        if (NoRenderingDevice(name)) return;

        var canvas = Canvas2DFactory.Create(80, 80);
        try
        {
            canvas.BeginFrame();
            canvas.Clear(Colors.Black);
            using (var paint = canvas.CreatePaint())
            {
                paint.SetColor(Colors.White).SetStrokeWidth(4f).SetAntiAlias(true);
                canvas.DrawCircle(40f, 40f, 24f, paint);
            }
            canvas.EndFrame();

            var image = ((SkiaCanvas2DBackend)canvas).SkiaTexture.GetImage();
            // Nothing was filled: the centre still holds the cleared colour ...
            AssertThat(image.GetPixel(40, 40).R < 0.05f).IsTrue();
            // ... while the outline itself was painted, 24 px from the centre.
            AssertThat(image.GetPixel(64, 40).R > 0.5f).IsTrue();
            GD.Print($"{name}: centre {image.GetPixel(40, 40).R:F3}, ring {image.GetPixel(64, 40).R:F3}");
        }
        finally
        {
            canvas.Dispose();
        }
    }
}
