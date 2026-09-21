namespace GodotNodeExtension.Tests.GodotChart.Marks;

using System.Collections.Generic;
using System.Linq;
using GdUnit4;
using Godot;
using GodotNodeExtension.Component.GodotChart;
using GodotNodeExtension.Component.GodotChart.Marks;
using Support;
using static GdUnit4.Assertions;

/// <summary>
/// One source of truth for the "no theme attached" defaults.
/// <para>
/// Every renderer that needs a value without a theme reads <see cref="ChartTheme.Default"/>, so the
/// values that used to be restated as literals at ~75 call sites now live only in
/// <see cref="ChartTheme"/>'s own field initializers. These tests pin both halves of that contract:
/// the values are still the documented ones (the unthemed output did not move), and an unthemed
/// render is exactly what an explicit <see cref="ChartTheme.Default"/> produces - which is what makes
/// the fallback and the theme impossible to drift apart.
/// </para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class ThemeDefaultFallbackTest
{
    // Shared fixtures: a constant array argument would be flagged by CA1861 (which the build keeps
    // at warning level), so every collection handed to a call is a static readonly field.
    private static readonly string[] SingleCategory = { "A" };
    private static readonly string[] TwoCategories = { "A", "B" };
    private static readonly object[] TwoSeries = { "series-0", "series-1" };
    private static readonly object[] ThreeSeries = { "series-0", "series-1", "series-2" };

    // ── helpers ─────────────────────────────────────────────────────────────

    /// <summary>Exposes the protected theme-fallback helpers of <see cref="Mark"/> to the assertions.</summary>
    private sealed class ThemedProbe : Mark
    {
        /// <inheritdoc />
        public override MarkCoordinate Coordinate => MarkCoordinate.Cartesian;

        /// <inheritdoc />
        public override void Render(MarkContext ctx) { }

        public static Color DefaultColor(MarkContext ctx) => GetDefaultColor(ctx);
        public static Color SelectionColor(MarkContext ctx) => GetSelectionColor(ctx);
        public static float SelectionStrokeWidth(MarkContext ctx) => GetSelectionStrokeWidth(ctx);
        public static Color DataLabelColor(MarkContext ctx) => GetDataLabelColor(ctx);
        public static float HoverBrighten(MarkContext ctx) => GetHoverBrighten(ctx);
        public static float HoverScale(MarkContext ctx) => GetHoverScale(ctx);
        public static Color SegmentBorderColor(MarkContext ctx) => GetSegmentBorderColor(ctx);
        public static float SegmentBorderWidth(MarkContext ctx) => GetSegmentBorderWidth(ctx);
        public static bool HoverExplode(MarkContext ctx) => IsHoverExplodeEnabled(ctx);
    }

    /// <summary>A context without any theme, i.e. the "theme not provided" case under test.</summary>
    private static MarkContext Unthemed() => Context(null);

    private static MarkContext Context(ChartTheme? theme) => TestContexts.Mark(
        new FakeCanvas2D(), [], new EncodeSet(), new ScaleSet(), theme);

    private static DataRow D(params (string Field, object? Value)[] fields) => TestContexts.Row(fields);

    /// <summary>Every recorded draw of both canvases must be identical, down to the stroke width.</summary>
    private static void AssertSameRender(FakeCanvas2D unthemed, FakeCanvas2D themed)
    {
        AssertThat(unthemed.Snapshot()).IsEqual(themed.Snapshot());
        AssertThat(unthemed.StrokeWidths.Count).IsEqual(themed.StrokeWidths.Count);
        for (int i = 0; i < unthemed.StrokeWidths.Count; i++)
            AssertThat(unthemed.StrokeWidths[i]).IsEqual(themed.StrokeWidths[i]);
        AssertThat(unthemed.FillOpacities.Count).IsEqual(themed.FillOpacities.Count);
        for (int i = 0; i < unthemed.FillOpacities.Count; i++)
            AssertThat(unthemed.FillOpacities[i]).IsEqual(themed.FillOpacities[i]);
    }

    // ── ChartTheme.Default itself ───────────────────────────────────────────

    [TestCase]
    public void DefaultIsOneSharedInstanceAndDarkStaysAFactory()
    {
        // Same instance on every read: the fallback cannot be mutated per call site by accident.
        var firstRead = ChartTheme.Default;
        var secondRead = ChartTheme.Default;
        AssertThat(ReferenceEquals(firstRead, secondRead)).IsTrue();

        // Dark() is the palette factory: fresh (mutable) instances that start at the same values.
        var dark = ChartTheme.Dark();
        AssertThat(ReferenceEquals(ChartTheme.Default, dark)).IsFalse();
        AssertThat(dark.DefaultMarkColor).IsEqual(ChartTheme.Default.DefaultMarkColor);
        AssertThat(dark.TooltipPadding).IsEqual(ChartTheme.Default.TooltipPadding);
        AssertThat(dark.EnableHoverHighlight).IsEqual(ChartTheme.Default.EnableHoverHighlight);
    }

    // ── Mark helpers (the funnel most marks read their defaults through) ────

    [TestCase]
    public void UnthemedMarkHelpersKeepTheirDocumentedValues()
    {
        var ctx = Unthemed();

        // These are the literals the helpers used to fall back to; the theme's initializers now
        // carry them, and this is the assertion that fails if one of them is changed by accident.
        AssertThat(ThemedProbe.DefaultColor(ctx)).IsEqual(new Color(0.29f, 0.59f, 0.98f));
        AssertThat(ThemedProbe.SelectionColor(ctx)).IsEqual(new Color(1f, 1f, 1f, 0.8f));
        AssertThat(ThemedProbe.SelectionStrokeWidth(ctx)).IsEqual(2f);
        AssertThat(ThemedProbe.DataLabelColor(ctx)).IsEqual(new Color(1f, 1f, 1f, 0.9f));
        AssertThat(ThemedProbe.HoverBrighten(ctx)).IsEqual(1.2f);
        AssertThat(ThemedProbe.HoverScale(ctx)).IsEqual(1.05f);
        AssertThat(ThemedProbe.SegmentBorderColor(ctx)).IsEqual(new Color(0.08f, 0.08f, 0.12f));
        AssertThat(ThemedProbe.SegmentBorderWidth(ctx)).IsEqual(1f);
        AssertThat(ThemedProbe.HoverExplode(ctx)).IsTrue();
    }

    [TestCase]
    public void UnthemedMarkHelpersReadTheSharedDefaultTheme()
    {
        var ctx = Unthemed();
        var fallback = ChartTheme.Default;

        AssertThat(ThemedProbe.DefaultColor(ctx)).IsEqual(fallback.DefaultMarkColor);
        AssertThat(ThemedProbe.SelectionColor(ctx)).IsEqual(fallback.SelectionColor);
        AssertThat(ThemedProbe.SelectionStrokeWidth(ctx)).IsEqual(fallback.SelectionStrokeWidth);
        AssertThat(ThemedProbe.DataLabelColor(ctx)).IsEqual(fallback.DataLabelColor);
        AssertThat(ThemedProbe.HoverBrighten(ctx)).IsEqual(fallback.HoverBrighten);
        AssertThat(ThemedProbe.HoverScale(ctx)).IsEqual(fallback.HoverScale);
        AssertThat(ThemedProbe.SegmentBorderColor(ctx)).IsEqual(fallback.SegmentBorderColor);
        AssertThat(ThemedProbe.SegmentBorderWidth(ctx)).IsEqual(fallback.SegmentBorderWidth);
        AssertThat(ThemedProbe.HoverExplode(ctx)).IsEqual(fallback.EnableHoverExplode);
    }

    [TestCase]
    public void AnAttachedThemeStillOverridesEveryHelper()
    {
        var theme = ChartTheme.Dark();
        theme.DefaultMarkColor = new Color(1f, 0f, 0f);
        theme.SelectionStrokeWidth = 7f;
        theme.HoverBrighten = 1.9f;
        theme.SegmentBorderWidth = 5f;
        theme.EnableHoverExplode = false;
        var ctx = Context(theme);

        AssertThat(ThemedProbe.DefaultColor(ctx)).IsEqual(new Color(1f, 0f, 0f));
        AssertThat(ThemedProbe.SelectionStrokeWidth(ctx)).IsEqual(7f);
        AssertThat(ThemedProbe.HoverBrighten(ctx)).IsEqual(1.9f);
        AssertThat(ThemedProbe.SegmentBorderWidth(ctx)).IsEqual(5f);
        AssertThat(ThemedProbe.HoverExplode(ctx)).IsFalse();
    }

    [TestCase]
    public void DisablingHoverHighlightOnTheDefaultThemeWouldDropTheHighlight()
    {
        // The "no theme" path used to spell the toggle out as `Theme?.EnableHoverHighlight == false`
        // (null theme = enabled). It now reads the same flag the theme does, so the two cannot
        // disagree - here the flag is observed through a theme that genuinely has it switched off.
        var theme = ChartTheme.Dark();
        theme.EnableHoverHighlight = false;

        AssertThat(ThemedProbe.HoverBrighten(Context(theme))).IsEqual(1f);
        AssertThat(ThemedProbe.HoverScale(Context(theme))).IsEqual(1f);
        AssertThat(ThemedProbe.HoverBrighten(Unthemed())).IsEqual(ChartTheme.Default.HoverBrighten);
    }

    // ── Real marks: unthemed render == ChartTheme.Default render ────────────

    /// <summary>
    /// Every mark case draws exactly the same with no theme attached (a null context theme) as with an
    /// explicit <see cref="ChartTheme.Default"/>. The hand-written Box/Candlestick/Gauge cases below used
    /// to be the whole coverage; driving <see cref="MarkCases.All"/> makes the equivalence a property of
    /// the fallback itself, so a new mark cannot quietly read a local literal instead of the theme.
    /// </summary>
    [TestCase]
    public void EveryMarkRendersTheSameWithoutAThemeAsWithTheDefaultTheme()
    {
        var problems = new List<string>();
        int drew = 0;
        foreach (var c in MarkCases.All)
        {
            var unthemed = DirectRender(c, theme: null);
            var themed = DirectRender(c, ChartTheme.Default);
            if (themed.DrewAnything) drew++;

            if (unthemed.Snapshot() != themed.Snapshot())
                problems.Add($"{c.Name}: the unthemed render differs from the explicit default theme");
            else if (unthemed.FillColors.Count != themed.FillColors.Count ||
                     unthemed.StrokeColors.Count != themed.StrokeColors.Count)
                problems.Add($"{c.Name}: the unthemed render painted a different number of elements");
        }

        // A scale set that drew nothing for most marks would make the comparison above vacuous.
        if (drew < MarkCases.All.Length - 4)
            problems.Add($"only {drew} of {MarkCases.All.Length} marks drew anything with the default theme");

        AssertThat(problems.Count == 0 ? "" : string.Join("\n", problems)).IsEqual("");
    }

    /// <summary>
    /// Render one <see cref="MarkCases"/> case directly against a fitted scale set, with or without a
    /// theme on the context. The chart pipeline cannot express "no theme" (<see cref="Chart.Theme"/>
    /// rejects null and a fresh chart is already themed), so the marks are driven the way the existing
    /// Box/Candlestick/Gauge cases drive them.
    /// </summary>
    private static FakeCanvas2D DirectRender(MarkCases.MarkCase c, ChartTheme? theme)
    {
        var canvas = new FakeCanvas2D();
        var encodes = new EncodeSet();
        encodes.Set(Channel.X, new FieldEncode(c.XField));
        encodes.Set(Channel.Y, new FieldEncode(c.YField));
        if (c.ColorField != null)
            encodes.Set(Channel.Color, new FieldEncode(c.ColorField));

        var scales = new ScaleSet();
        scales.Set(Channel.X, FitScale(c, c.XField, includeZero: false));
        scales.Set(Channel.Y, FitScale(c, c.YField, includeZero: true));
        if (c.ColorField != null)
            scales.Set(Channel.Color, FitColorScale(c, c.ColorField));

        c.Create().Render(TestContexts.Mark(canvas, c.Data, encodes, scales, theme));
        return canvas;
    }

    /// <summary>The scale the chart would infer for one field: linear for numbers, ordinal otherwise.</summary>
    private static IScale FitScale(MarkCases.MarkCase c, string field, bool includeZero)
    {
        var values = Collect(c, field);
        if (values.Count > 0 && IsNumeric(values[0]))
        {
            var linear = new LinearScale { IncludeZero = includeZero };
            linear.Fit(values);
            return linear;
        }

        var ordinal = new OrdinalScale();
        ordinal.Fit(values);
        return ordinal;
    }

    /// <summary>
    /// The colour scale the chart would infer: values that are colours map to themselves, numbers to the
    /// sequential ramp (a heatmap needs a continuous ramp, a linear scale would draw nothing) and
    /// anything else to the categorical palette.
    /// </summary>
    private static IScale FitColorScale(MarkCases.MarkCase c, string field)
    {
        var values = Collect(c, field);
        if (values.Count > 0 && values.All(v => ColorValues.TryParse(v, out _)))
            return new IdentityColorScale();

        if (values.Count > 0 && IsNumeric(values[0]))
        {
            var ramp = new SequentialColorScale();
            ramp.Fit(values);
            return ramp;
        }

        var palette = new ColorScale();
        palette.Fit(values);
        return palette;
    }

    private static List<object> Collect(MarkCases.MarkCase c, string field)
    {
        var values = new List<object>();
        foreach (var row in c.Data)
        {
            if (!row.Has(field)) continue;
            object? value = row.Get(field);
            if (value is not null) values.Add(value);
        }
        return values;
    }

    private static bool IsNumeric(object value)
        => value is byte or sbyte or short or ushort or int or uint or long or ulong
                or float or double or decimal;

    [TestCase]
    public void BoxMarkDrawsTheSameWithoutAThemeAsWithTheDefaultOne()
    {
        var rows = new List<DataRow>
        {
            D(("cat", "A"), ("lo", 1.0), ("q1", 3.0), ("med", 5.0), ("q3", 7.0), ("hi", 9.0)),
            D(("cat", "B"), ("lo", 2.0), ("q1", 4.0), ("med", 6.0), ("q3", 8.0), ("hi", 10.0)),
        };
        var unthemed = new FakeCanvas2D();
        var themed = new FakeCanvas2D();

        RenderBox(unthemed, null, rows);
        RenderBox(themed, ChartTheme.Default, rows);

        AssertSameRender(unthemed, themed);
        // The box fill is still the documentated default, now sourced from the theme.
        AssertThat(unthemed.FillColors[0]).IsEqual(ChartTheme.Default.BoxFillColor);
        AssertThat(unthemed.FillColors[0]).IsEqual(new Color(0.29f, 0.59f, 0.98f, 0.6f));
    }

    private static void RenderBox(FakeCanvas2D canvas, ChartTheme? theme, List<DataRow> rows)
    {
        var mark = new BoxMark { MinField = "lo", Q1Field = "q1", MedianField = "med", Q3Field = "q3", MaxField = "hi" };
        var ctx = TestContexts.Mark(canvas, rows, TestContexts.XyEncodes("cat", "med"),
            TestContexts.CategoryScales(TwoCategories, 0, 10), theme);
        mark.Render(ctx);
    }

    [TestCase]
    public void CandlestickMarkDrawsTheSameWithoutAThemeAsWithTheDefaultOne()
    {
        var rows = new List<DataRow>
        {
            D(("cat", "A"), ("open", 10.0), ("high", 30.0), ("low", 5.0), ("close", 25.0)), // bullish
            D(("cat", "B"), ("open", 25.0), ("high", 28.0), ("low", 8.0), ("close", 12.0)), // bearish
        };
        // Hollow bullish bodies: that is the path reading the hollow stroke width. No hover here, so the
        // colours can be compared against the theme values directly (a hovered element is brightened).
        var unthemed = new FakeCanvas2D();
        var themed = new FakeCanvas2D();
        RenderCandlesticks(unthemed, null, rows);
        RenderCandlesticks(themed, ChartTheme.Default, rows);

        AssertSameRender(unthemed, themed);
        // Row A is bullish and drawn hollow (stroke only), row B is bearish and filled.
        AssertThat(unthemed.StrokeColors[0]).IsEqual(ChartTheme.Default.CandlestickBullishColor);
        AssertThat(unthemed.StrokeColors[0]).IsEqual(new Color(0.29f, 0.85f, 0.60f));
        AssertThat(unthemed.StrokeWidths[0]).IsEqual(ChartTheme.Default.CandlestickHollowStrokeWidth);
        AssertThat(unthemed.FillColors[0]).IsEqual(ChartTheme.Default.CandlestickBearishColor);
        AssertThat(unthemed.FillColors[0]).IsEqual(new Color(0.98f, 0.45f, 0.29f));

        // Hovering row 0 additionally reads the hover wick scale; the two renders must still agree.
        var hoveredUnthemed = new FakeCanvas2D();
        var hoveredThemed = new FakeCanvas2D();
        RenderCandlesticks(hoveredUnthemed, null, rows, hovered: 0);
        RenderCandlesticks(hoveredThemed, ChartTheme.Default, rows, hovered: 0);

        AssertSameRender(hoveredUnthemed, hoveredThemed);
        // A hovered bullish candle is brightened by the theme's HoverBrighten, not by a local constant.
        AssertThat(hoveredUnthemed.StrokeColors[0]).IsEqual(
            ChartTheme.Default.CandlestickBullishColor with
            {
                R = Mathf.Min(1f, ChartTheme.Default.CandlestickBullishColor.R * ChartTheme.Default.HoverBrighten),
                G = Mathf.Min(1f, ChartTheme.Default.CandlestickBullishColor.G * ChartTheme.Default.HoverBrighten),
                B = Mathf.Min(1f, ChartTheme.Default.CandlestickBullishColor.B * ChartTheme.Default.HoverBrighten),
            });
    }

    private static void RenderCandlesticks(FakeCanvas2D canvas, ChartTheme? theme, List<DataRow> rows, int hovered = -1)
    {
        var mark = new CandlestickMark { FillBullish = false };
        var ctx = TestContexts.Mark(canvas, rows, TestContexts.XyEncodes("cat", "close"),
            TestContexts.CategoryScales(TwoCategories, 0, 30), theme, hoveredRowIndex: hovered);
        mark.Render(ctx);
    }

    [TestCase]
    public void GaugeMarkDrawsTheSameWithoutAThemeAsWithTheDefaultOne()
    {
        var rows = new List<DataRow> { D(("value", 42.0)) };
        var unthemed = new FakeCanvas2D();
        var themed = new FakeCanvas2D();

        RenderGauge(unthemed, null, rows);
        RenderGauge(themed, ChartTheme.Default, rows);

        AssertSameRender(unthemed, themed);
        AssertThat(unthemed.FillColors[0]).IsEqual(ChartTheme.Default.GaugeTrackColor);
        AssertThat(unthemed.TextDraws.Count > 0).IsTrue();
        AssertThat(unthemed.TextDraws[0].Color.R).IsEqual(ChartTheme.Default.GaugeLabelColor.R);
    }

    private static void RenderGauge(FakeCanvas2D canvas, ChartTheme? theme, List<DataRow> rows)
    {
        var ctx = TestContexts.Mark(canvas, rows, TestContexts.XyEncodes("cat", "value"),
            TestContexts.CategoryScales(SingleCategory), theme);
        new GaugeMark().Render(ctx);
    }

    // ── Tooltip ─────────────────────────────────────────────────────────────

    [TestCase]
    public void TooltipDrawsTheSameWithoutAThemeAsWithTheDefaultOne()
    {
        var unthemed = TooltipCanvas(null);
        var themed = TooltipCanvas(ChartTheme.Default);
        AssertSameRender(unthemed, themed);

        // The bubble background is still the documented default, now sourced from the theme.
        AssertThat(unthemed.FillColors[0]).IsEqual(new Color(0.12f, 0.12f, 0.18f, 0.92f));
        AssertThat(unthemed.FillColors[0]).IsEqual(ChartTheme.Default.TooltipBackground);
    }

    private static FakeCanvas2D TooltipCanvas(ChartTheme? theme)
    {
        var canvas = new FakeCanvas2D();
        var renderer = new TooltipRenderer { Theme = theme };
        renderer.Options.RichContentBuilder = _ => new[] { TooltipLine.Plain("value") };
        var hit = new HitResult
        {
            Hit = true, RowIndex = 0, Row = D(("value", 1.0)),
            ScreenX = 50f, ScreenY = 50f, Label = "value",
        };
        renderer.Update(1f, hit);
        renderer.Draw(canvas, 800, 600);
        return canvas;
    }

    // ── Legend layout ───────────────────────────────────────────────────────

    [TestCase]
    public void LegendLayoutWithoutAThemeMatchesTheDefaultTheme()
    {
        var cfg = new LegendConfig { Position = LegendPosition.Bottom };
        var scale = new ColorScale();
        scale.Fit(ThreeSeries);
        var plot = new PlotArea(0, 0, 400, 300);

        var unthemed = LegendLayoutHelper.Compute(plot, cfg, scale, 0f);
        var themed = LegendLayoutHelper.Compute(plot, cfg, scale, 0f, null, ChartTheme.Default);

        AssertThat(unthemed is not null).IsTrue();
        AssertThat(themed is not null).IsTrue();
        AssertThat(unthemed!.Value.Width).IsEqual(themed!.Value.Width);
        AssertThat(unthemed.Value.Height).IsEqual(themed.Value.Height);
        AssertThat(unthemed.Value.Items.Count).IsEqual(themed.Value.Items.Count);
        for (int i = 0; i < unthemed.Value.Items.Count; i++)
        {
            AssertThat(unthemed.Value.Items[i].X).IsEqual(themed.Value.Items[i].X);
            AssertThat(unthemed.Value.Items[i].Y).IsEqual(themed.Value.Items[i].Y);
            AssertThat(unthemed.Value.Items[i].Width).IsEqual(themed.Value.Items[i].Width);
            AssertThat(unthemed.Value.Items[i].Key).IsEqual(themed.Value.Items[i].Key);
        }
    }

    [TestCase]
    public void TheThemeStillDrivesTheLegendSpacing()
    {
        var cfg = new LegendConfig { Position = LegendPosition.Bottom };
        var scale = new ColorScale();
        scale.Fit(TwoSeries);
        var plot = new PlotArea(0, 0, 400, 300);

        var theme = ChartTheme.Dark();
        theme.LegendSwatchTextGap = 4f + 12f;
        theme.LegendBottomGap = 10f + 20f;

        var unthemed = LegendLayoutHelper.Compute(plot, cfg, scale, 0f)!.Value;
        var widened = LegendLayoutHelper.Compute(plot, cfg, scale, 0f, null, theme)!.Value;

        AssertThat(widened.Width > unthemed.Width).IsTrue();
        AssertThat(widened.Items[0].Y > unthemed.Items[0].Y).IsTrue();
    }
}
