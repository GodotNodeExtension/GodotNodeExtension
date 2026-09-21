namespace GodotNodeExtension.Tests.GodotChart.Marks;

using System;
using System.Collections.Generic;
using System.Linq;
using GdUnit4;
using Godot;
using GodotNodeExtension.Component.GodotChart;
using GodotNodeExtension.Component.GodotChart.Marks;
using GodotNodeExtension.Tests.Support;
using Support;
using static GdUnit4.Assertions;

/// <summary>
/// Contract of the protected helpers on the <see cref="Mark"/> base class, exercised through
/// <see cref="ProbeMark"/>: local encode overrides, colour / series-colour resolution, opacity
/// resolution and the G2-style <see cref="Mark.StyleOverride"/> callback with its declarative
/// <see cref="Mark.States"/>, theme fallbacks, hover factors, the opacity / animation math,
/// series hiding, channel grouping (plain and cached), and the numeric / label / format helpers.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class MarkBaseContractTest
{
    /// <summary>Approximate comparison for computed floating point values.</summary>
    private static bool Approx(double actual, double expected, double tolerance = 1e-6)
        => Math.Abs(actual - expected) <= tolerance;

    /// <summary>Assert that <paramref name="actual"/> is within <paramref name="tolerance"/> of the expected value.</summary>
    private static void AssertApprox(double actual, double expected, double tolerance = 1e-6)
        => AssertThat(Approx(actual, expected, tolerance)).IsTrue();

    /// <summary>Run <paramref name="action"/> and return the exception it threw (null when it did not throw).</summary>
    private static Exception? CaptureException(Action action)
    {
        try { action(); return null; }
        catch (Exception ex) { return ex; }
    }

    /// <summary>Mark context with a throw-away fake canvas and only the parts a test cares about.</summary>
    private static MarkContext Ctx(
        List<DataRow> data,
        EncodeSet? encodes = null,
        ScaleSet? scales = null,
        ChartTheme? theme = null,
        AnimationContext? animation = null,
        string? focusedSeries = null,
        IReadOnlySet<string>? hiddenSeries = null,
        int dataVersion = 1,
        int hoveredRowIndex = -1,
        int selectedRowIndex = -1)
        => TestContexts.Mark(new FakeCanvas2D(), data, encodes ?? new EncodeSet(), scales ?? new ScaleSet(),
            theme: theme, animation: animation, focusedSeries: focusedSeries,
            hiddenSeries: hiddenSeries, dataVersion: dataVersion,
            hoveredRowIndex: hoveredRowIndex, selectedRowIndex: selectedRowIndex);

    // ── encode resolution ───────────────────────────────────────────────────

    [TestCase]
    public void LocalEncodeWinsOverTheChartEncode()
    {
        var mark = new ProbeMark();
        mark.Encode(Channel.Y, "localY");
        var row = TestContexts.Row(("x", 1.0), ("y", 5.0), ("localY", 9.0));
        var ctx = Ctx([row], TestContexts.XyEncodes("x", "y"));

        AssertThat((double)mark.ReadEncode(ctx, Channel.Y, row)!).IsEqual(9.0);
        AssertThat((double)mark.ReadY(ctx, row)!).IsEqual(9.0);
        // channels the mark does not encode still come from the chart
        AssertThat((double)mark.ReadEncode(ctx, Channel.X, row)!).IsEqual(1.0);
    }

    [TestCase]
    public void LocalEncodeShadowsTheChartEncodeWhenItsFieldIsMissing()
    {
        var mark = new ProbeMark();
        mark.Encode(Channel.Y, "ghost");
        var row = TestContexts.Row(("x", 1.0), ("y", 5.0));
        var ctx = Ctx([row], TestContexts.XyEncodes("x", "y"));

        // the local encode takes priority, so the chart's "y" field is never consulted
        AssertThat(mark.ReadEncode(ctx, Channel.Y, row) is null).IsTrue();
        AssertThat(mark.EncodesChannel(ctx, Channel.Y)).IsTrue();
    }

    [TestCase]
    public void LocalEncodeMetadataIsExposedToTheChart()
    {
        var mark = new ProbeMark();

        AssertThat(mark.GetLocalEncodeField(Channel.Y) is null).IsTrue();
        AssertThat(mark.LocalEncodeChannels.Any()).IsFalse();

        mark.Encode(Channel.Y, "y").Encode(Channel.Color, "series");

        AssertThat(mark.GetLocalEncodeField(Channel.Y)).IsEqual("y");
        AssertThat(mark.GetLocalEncodeField(Channel.Color)).IsEqual("series");
        AssertThat(mark.LocalEncodeChannels.Count()).IsEqual(2);
        AssertThat(mark.LocalEncodeChannels.Contains(Channel.Color)).IsTrue();
        AssertThat(mark.GetLocalEncode(Channel.Y) is FieldEncode { FieldName: "y" }).IsTrue();
        AssertThat(mark.GetLocalEncode(Channel.X) is null).IsTrue();
    }

    [TestCase]
    public void HasEncodeReportsLocalChartOrNeither()
    {
        var mark = new ProbeMark();
        var row = TestContexts.Row(("x", 1.0), ("y", 2.0));
        var ctx = Ctx([row], TestContexts.XyEncodes("x", "y"));

        AssertThat(mark.EncodesChannel(ctx, Channel.X)).IsTrue();       // chart-level only
        AssertThat(mark.EncodesChannel(ctx, Channel.Color)).IsFalse();  // neither

        mark.Encode(Channel.Size, "s");
        AssertThat(mark.EncodesChannel(ctx, Channel.Size)).IsTrue();    // local only
    }

    /// <summary>
    /// A mark-level Y encode must stay local to that mark. Two marks on one chart, the first binding its
    /// own field and the second relying on the chart-level Y, must each read their own column - in the
    /// render pass and in the hit-test pass, both of which bind the encodes per mark. A shared/leaked
    /// encode would feed the second mark the first mark's field (or vice versa).
    /// </summary>
    [TestCase]
    public void AMarkLevelEncodeDoesNotLeakToTheOtherMark()
    {
        var canvas = new FakeCanvas2D();
        var chart = new Chart(canvas) { Width = 400f, Height = 300f };
        chart.Data(new List<DataRow>
        {
            TestContexts.Row(("cat", "A"), ("y1", 10.0), ("y2", 20.0)),
            TestContexts.Row(("cat", "B"), ("y1", 30.0), ("y2", 40.0)),
        });
        chart.Encode(Channel.X, "cat");
        chart.Encode(Channel.Y, "y2");          // the chart-level value column

        var local = new ProbeMark();
        local.Encode(Channel.Y, "y1");          // ... and a mark that draws its own column
        var chartWide = new ProbeMark();        // ... and one that does not
        chart.Mark(local);
        chart.Mark(chartWide);

        chart.Render();

        // Both marks rendered (each recorded the row it drew from), each with its own value column.
        AssertThat((double)local.RenderedY!).IsEqual(10.0);
        AssertThat((double)chartWide.RenderedY!).IsEqual(20.0);

        // Same binding on the hit-test path (ChartInteraction.TestAll binds per mark as well).
        object? hitEncodeLocal = null;
        object? hitEncodeChart = null;
        chartWide.HitTestOverride = ctx =>
        {
            hitEncodeChart = chartWide.ReadEncode(ctx, Channel.Y, ctx.Data[0]);
            return null;   // let the loop fall through to the mark below
        };
        local.HitTestOverride = ctx =>
        {
            hitEncodeLocal = local.ReadEncode(ctx, Channel.Y, ctx.Data[0]);
            return new HitResult { Hit = true, RowIndex = 0, Row = ctx.Data[0], MarkType = nameof(ProbeMark) };
        };

        var hit = chart.HitTest(new Vector2(200f, 150f));

        AssertThat(hit is not null).IsTrue();
        AssertThat((double)hitEncodeLocal!).IsEqual(10.0);
        AssertThat((double)hitEncodeChart!).IsEqual(20.0);
    }

    /// <summary>
    /// <see cref="Mark.GetYScale"/> follows the mark's value channel: a mark that draws against Y2 must
    /// read the Y2 scale, and a context without scales reports null instead of throwing.
    /// </summary>
    [TestCase]
    public void YScaleOfFollowsTheMarksValueChannel()
    {
        var mark = new ProbeMark();
        var row = TestContexts.Row(("x", 1.0), ("y", 2.0), ("y2", 3.0));

        AssertThat(mark.YScaleOf(Ctx([row], TestContexts.XyEncodes("x", "y"))) is null).IsTrue();

        var scales = TestContexts.CategoryScales(["A"], 0, 10);
        AssertThat(ReferenceEquals(mark.YScaleOf(Ctx([row], TestContexts.XyEncodes("x", "y"), scales)),
                                   scales.Get(Channel.Y))).IsTrue();

        mark.YChannel = Channel.Y2;
        var y2Scales = new ScaleSet();
        y2Scales.Set(Channel.Y2, new LinearScale(0, 5));
        AssertThat(mark.YScaleOf(Ctx([row], TestContexts.XyEncodes("x", "y"), y2Scales)) is LinearScale
                   { Max: 5 }).IsTrue();
    }

    // ── colour resolution ───────────────────────────────────────────────────
    [TestCase]
    public void ResolveColorFallsBackForMissingEncodeNullValueOrNonColorScale()
    {
        var mark = new ProbeMark();
        var fallback = new Color(0.1f, 0.2f, 0.3f, 0.4f);
        var row = TestContexts.Row(("series", "A"), ("color", null));

        // no Color encode at all
        AssertThat(mark.ReadColor(Ctx([row]), row, fallback)).IsEqual(fallback);

        // encode present, but the row carries no value (named null)
        var withNull = new EncodeSet();
        withNull.Set(Channel.Color, new FieldEncode("color"));
        AssertThat(mark.ReadColor(Ctx([row], withNull), row, fallback)).IsEqual(fallback);

        // encode and value present, but the channel scale is not an IColorScale
        var encodes = new EncodeSet();
        encodes.Set(Channel.Color, new FieldEncode("series"));
        var scales = new ScaleSet();
        scales.Set(Channel.Color, new LinearScale(0, 1));
        AssertThat(mark.ReadColor(Ctx([row], encodes, scales), row, fallback)).IsEqual(fallback);
    }

    [TestCase]
    public void ResolveColorAndSeriesColorUseAnIColorScale()
    {
        var mark = new ProbeMark();
        var scale = new ColorScale();
        scale.Fit(new object[] { "A", "B" });
        var scales = new ScaleSet();
        scales.Set(Channel.Color, scale);
        var encodes = new EncodeSet();
        encodes.Set(Channel.Color, new FieldEncode("series"));
        var row = TestContexts.Row(("series", "B"));
        var ctx = Ctx([row], encodes, scales);

        AssertThat(mark.ReadColor(ctx, row, Colors.Black)).IsEqual(scale.MapColor("B"));
        AssertThat(mark.ReadSeriesColor(ctx, "A")).IsEqual(scale.MapColor("A"));

        // without a colour scale the series colour is the theme-less default
        var plain = Ctx([row]);
        AssertThat(mark.ReadSeriesColor(plain, "A")).IsEqual(new Color(0.29f, 0.59f, 0.98f));
    }

    // ── opacity resolution ──────────────────────────────────────────────────

    [TestCase]
    public void ResolveOpacityIsSilentForNullUnconvertibleAndScalelessEncodes()
    {
        var mark = new ProbeMark();
        var row = TestContexts.Row(("op", 50.0), ("nothing", null));
        var data = new List<DataRow> { row };
        const float fallback = 0.7f;

        MarkContext WithOpacityEncode(IEncodeValue encode, IScale? scale = null)
        {
            var encodes = new EncodeSet();
            encodes.Set(Channel.Opacity, encode);
            var scales = new ScaleSet();
            if (scale is not null) scales.Set(Channel.Opacity, scale);
            return Ctx(data, encodes, scales);
        }

        // no Opacity encode
        AssertApprox(mark.ReadOpacity(Ctx(data), row, fallback), fallback);

        // constants: null, unconvertible, and a real number
        AssertApprox(mark.ReadOpacity(WithOpacityEncode(new ConstantEncode(null!)), row, fallback), fallback);
        AssertApprox(mark.ReadOpacity(WithOpacityEncode(new ConstantEncode("not-a-number")), row, fallback), fallback);
        AssertApprox(mark.ReadOpacity(WithOpacityEncode(new ConstantEncode(0.25f)), row, fallback), 0.25);

        // field encodes: named null, value without a scale, value mapped through a scale
        AssertApprox(mark.ReadOpacity(WithOpacityEncode(new FieldEncode("nothing")), row, fallback), fallback);
        AssertApprox(mark.ReadOpacity(WithOpacityEncode(new FieldEncode("op")), row, fallback), fallback);
        AssertApprox(mark.ReadOpacity(WithOpacityEncode(new FieldEncode("op"), new LinearScale(0, 100)), row, fallback), 0.5);
    }

    [TestCase]
    public void StyleOverrideReceivesTheRowIndexAndResolvedStyle()
    {
        var mark = new ProbeMark();
        var row = TestContexts.Row(("v", 1.0));
        var baseColor = new Color(0.5f, 0.5f, 0.5f);
        var custom = new Color(1f, 0f, 0f);

        // no callback at all: the resolved style passes straight through
        AssertThat(mark.OverrideColor(row, 0, baseColor)).IsEqual(baseColor);
        AssertApprox(mark.OverrideOpacity(row, 0, 0.6f), 0.6);

        // the callback receives the row, the element index and the style resolved from the data,
        // and it gets the last word: returning the style unchanged is a no-op.
        mark.StyleOverride = (r, i, style)
            => ReferenceEquals(r, row) && i == 3 && style.Fill == baseColor
                ? style.WithFill(custom) : style;
        AssertThat(mark.OverrideColor(row, 3, baseColor)).IsEqual(custom);
        AssertThat(mark.OverrideColor(row, 4, baseColor)).IsEqual(baseColor);

        // the same callback also overrides the element opacity
        mark.StyleOverride = (_, i, style) => i == 3 ? style.WithOpacity(0.1f) : style;
        AssertApprox(mark.OverrideOpacity(row, 3, 0.6f), 0.1);
        AssertApprox(mark.OverrideOpacity(row, 4, 0.6f), 0.6);
    }

    [TestCase]
    public void StyleOverrideCombinesWithChannelResolution()
    {
        var mark = new ProbeMark();
        var row = TestContexts.Row(("v", 1.0));
        var defaultColor = new Color(0.2f, 0.4f, 0.6f, 0.8f);
        var ctx = Ctx([row]);

        // no style callback → the channel resolution result survives
        AssertThat(mark.ReadColorWithOverride(ctx, row, 0, defaultColor)).IsEqual(defaultColor);
        AssertApprox(mark.ReadOpacityWithOverride(ctx, row, 0, 0.4f), 0.4);

        // a style callback transforms the resolved fill and opacity
        mark.StyleOverride = (_, _, style)
            => style.WithFill(style.Fill with { A = 0.5f }).WithOpacity(style.Opacity * 2f);
        AssertApprox(mark.ReadColorWithOverride(ctx, row, 0, defaultColor).A, 0.5);
        AssertApprox(mark.ReadOpacityWithOverride(ctx, row, 0, 0.4f), 0.8);
    }

    // ── style callback / declarative state ──────────────────────────────────

    [TestCase]
    public void StyleOverrideFillReachesTheCanvas()
    {
        var canvas = new FakeCanvas2D();
        var chart = new Chart(canvas) { Width = 400f, Height = 300f };
        chart.Data(new List<DataRow> { TestContexts.Row(("cat", "A"), ("value", 10.0)) });
        chart.Mark(new PointMark
        {
            StyleOverride = (_, _, style) => style.WithFill(Colors.Red),
        });
        chart.Encode(Channel.X, "cat");
        chart.Encode(Channel.Y, "value");
        chart.Render();

        // The style callback decides the fill, and that fill is what the mark paints.
        AssertThat(canvas.FillColors.Any(c => c.ToHtml() == Colors.Red.ToHtml())).IsTrue();
    }

    [TestCase]
    public void HoveredElementIsBrightenedUnlessActiveFillOverridesIt()
    {
        var mark = new ProbeMark();
        var row = TestContexts.Row(("v", 1.0));
        var data = new List<DataRow> { row };
        var baseColor = new Color(0.4f, 0.4f, 0.4f);

        // not hovered: default state, fill unchanged
        AssertThat(mark.ReadState(Ctx(data), row, 0)).IsEqual(ElementState.Default);
        AssertThat(mark.ReadColorWithOverride(Ctx(data), row, 0, baseColor)).IsEqual(baseColor);

        // the hovered row is the active state: its fill is brightened by the hover factor
        var hovered = Ctx(data, hoveredRowIndex: 0);
        AssertThat(mark.ReadState(hovered, row, 0)).IsEqual(ElementState.Active);
        var brightened = mark.ReadColorWithOverride(hovered, row, 0, baseColor);
        AssertThat(brightened.R > baseColor.R).IsTrue();
        AssertApprox(brightened.R, 0.48);   // 0.4 * the default 1.2 hover factor
        AssertApprox(brightened.A, 1);

        // ...and States.ActiveFill replaces the brightening entirely, for the hovered row only
        mark.States.ActiveFill = Colors.Red;
        AssertThat(mark.ReadColorWithOverride(hovered, row, 0, baseColor)).IsEqual(Colors.Red);
        AssertThat(mark.ReadColorWithOverride(Ctx(data), row, 0, baseColor)).IsEqual(baseColor);
    }

    [TestCase]
    public void InactiveOpacityStateReplacesTheThemeValueWhileASeriesIsFocused()
    {
        var mark = new ProbeMark();
        var theme = ChartTheme.Dark();   // UnfocusedOpacity = 0.15
        var encodes = new EncodeSet();
        encodes.Set(Channel.Color, new FieldEncode("series"));
        var rowA = TestContexts.Row(("series", "A"));
        var rowB = TestContexts.Row(("series", "B"));
        var data = new List<DataRow> { rowA, rowB };
        var ctx = Ctx(data, encodes, theme: theme, focusedSeries: "A");

        // by default the theme's unfocused opacity dims the series that is not focused
        AssertThat(mark.ReadState(ctx, rowB, 1)).IsEqual(ElementState.Inactive);
        AssertApprox(mark.ReadOpacityWithOverride(ctx, rowB, 1), 0.15);

        // States.InactiveOpacity replaces the theme value for the unfocused series only
        mark.States.InactiveOpacity = 0.4f;
        AssertApprox(mark.ReadOpacityWithOverride(ctx, rowB, 1), 0.4);
        AssertApprox(mark.SeriesOpacity(ctx, "B", 1f), 0.4);
        AssertApprox(mark.SeriesOpacity(ctx, "A", 1f), 1.0);
        AssertApprox(mark.ReadOpacityWithOverride(ctx, rowA, 0), 1.0);
    }

    [TestCase]
    public void SelectedStrokeStateOverridesTheThemeRingStyle()
    {
        var mark = new ProbeMark();
        var theme = ChartTheme.Dark();
        theme.SelectionColor = new Color(0f, 1f, 0f);
        theme.SelectionStrokeWidth = 5f;

        var canvas = new FakeCanvas2D();
        var ctx = TestContexts.Mark(canvas, [], new EncodeSet(), new ScaleSet(), theme: theme);

        void PaintRing()
        {
            var paint = canvas.CreatePaint();
            mark.ApplySelectionRing(ctx, paint);
            var path = canvas.CreatePath();
            path.MoveTo(0f, 0f).LineTo(1f, 1f);
            canvas.Stroke(path, paint);
        }

        // without state styles the theme ring colour / width is used
        PaintRing();
        AssertThat(canvas.StrokeColors[0]).IsEqual(new Color(0f, 1f, 0f));
        AssertApprox(canvas.StrokeWidths[0], 5);

        // States.SelectedStroke / SelectedStrokeWidth win over the theme
        mark.States.SelectedStroke = new Color(1f, 0f, 1f);
        mark.States.SelectedStrokeWidth = 9f;
        PaintRing();
        AssertThat(canvas.StrokeColors[1]).IsEqual(new Color(1f, 0f, 1f));
        AssertApprox(canvas.StrokeWidths[1], 9);
    }

    // ── theme fallbacks ─────────────────────────────────────────────────────

    [TestCase]
    public void NullThemeUsesTheBuiltInFallbackValues()
    {
        var mark = new ProbeMark();
        var ctx = Ctx([]);

        AssertThat(mark.DefaultColorOf(ctx)).IsEqual(new Color(0.29f, 0.59f, 0.98f));
        AssertThat(mark.SelectionColorOf(ctx)).IsEqual(new Color(1f, 1f, 1f, 0.8f));
        AssertApprox(mark.SelectionStrokeWidthOf(ctx), 2);
        AssertThat(mark.DataLabelColorOf(ctx)).IsEqual(new Color(1f, 1f, 1f, 0.9f));
        AssertThat(mark.SegmentBorderColorOf(ctx)).IsEqual(new Color(0.08f, 0.08f, 0.12f));
        AssertApprox(mark.SegmentBorderWidthOf(ctx), 1);
        AssertThat(mark.HoverExplodeEnabled(ctx)).IsTrue();
        AssertApprox(mark.HoverBrightenOf(ctx), 1.2);
        AssertApprox(mark.HoverScaleOf(ctx), 1.05);
    }

    [TestCase]
    public void AThemeOverridesTheFallbackValues()
    {
        var mark = new ProbeMark();
        var theme = ChartTheme.Dark();
        theme.DefaultMarkColor = new Color(1f, 0f, 0f);
        theme.SelectionColor = new Color(0f, 1f, 0f);
        theme.SelectionStrokeWidth = 5f;
        theme.DataLabelColor = new Color(0f, 0f, 1f);
        theme.SegmentBorderColor = new Color(0.5f, 0.5f, 0.5f);
        theme.SegmentBorderWidth = 4f;
        theme.HoverBrighten = 1.9f;
        theme.HoverScale = 2.5f;
        theme.EnableHoverExplode = false;

        var ctx = Ctx([], theme: theme);

        AssertThat(mark.DefaultColorOf(ctx)).IsEqual(new Color(1f, 0f, 0f));
        AssertThat(mark.SelectionColorOf(ctx)).IsEqual(new Color(0f, 1f, 0f));
        AssertApprox(mark.SelectionStrokeWidthOf(ctx), 5);
        AssertThat(mark.DataLabelColorOf(ctx)).IsEqual(new Color(0f, 0f, 1f));
        AssertThat(mark.SegmentBorderColorOf(ctx)).IsEqual(new Color(0.5f, 0.5f, 0.5f));
        AssertApprox(mark.SegmentBorderWidthOf(ctx), 4);
        AssertApprox(mark.HoverBrightenOf(ctx), 1.9);
        AssertApprox(mark.HoverScaleOf(ctx), 2.5);
        AssertThat(mark.HoverExplodeEnabled(ctx)).IsFalse();
    }

    [TestCase]
    public void DisabledHoverHighlightForcesUnitFactors()
    {
        var mark = new ProbeMark();
        var theme = ChartTheme.Dark();
        theme.EnableHoverHighlight = false;
        theme.HoverBrighten = 3f;
        theme.HoverScale = 3f;

        var ctx = Ctx([], theme: theme);

        AssertApprox(mark.HoverBrightenOf(ctx), 1);
        AssertApprox(mark.HoverScaleOf(ctx), 1);
    }

    [TestCase]
    public void BrightenColorClampsUpAndKeepsAlpha()
    {
        var mark = new ProbeMark();

        var brightened = mark.Brighten(new Color(0.5f, 0.5f, 0.5f, 0.4f), 3f);
        AssertApprox(brightened.R, 1);
        AssertApprox(brightened.G, 1);
        AssertApprox(brightened.B, 1);
        AssertApprox(brightened.A, 0.4);

        // a factor below 1 darkens: only the upper bound is clamped
        var darkened = mark.Brighten(new Color(0.5f, 0.5f, 0.5f), 0.5f);
        AssertApprox(darkened.R, 0.25);
        AssertApprox(darkened.A, 1);
    }

    // ── opacity / animation math ────────────────────────────────────────────

    [TestCase]
    public void OpacityMathAppliesGlobalOpacityAndFocusDimming()
    {
        var mark = new ProbeMark();
        var theme = ChartTheme.Dark();   // UnfocusedOpacity = 0.15
        var animation = new AnimationContext { EntryProgress = 0.5f, GlobalOpacity = 0.5f };
        var encodes = new EncodeSet();
        encodes.Set(Channel.Color, new FieldEncode("series"));
        var rowA = TestContexts.Row(("series", "A"));
        var rowB = TestContexts.Row(("series", "B"));
        var focused = Ctx([rowA, rowB], encodes, theme: theme, animation: animation, focusedSeries: "A");

        // ComputeElementOpacity (through the ProbeMark shim) folds in the global animation opacity ...
        AssertApprox(mark.EffectiveOpacity(focused, rowA), 0.5);
        AssertApprox(mark.EffectiveOpacity(focused, rowA, 0.4f), 0.2);
        // ... and dims the rows of an unfocused series
        AssertApprox(mark.EffectiveOpacity(focused, rowB), 0.5 * 0.15);

        // ComputeSeriesOpacity leaves the global opacity to the caller
        AssertApprox(mark.SeriesOpacity(focused, "A", 0.8f), 0.8);
        AssertApprox(mark.SeriesOpacity(focused, "B", 0.8f), 0.8 * 0.15);
        AssertApprox(mark.SeriesOpacity(focused, null, 1f), 0.15);

        // without a focused series nothing is dimmed (and the fallback ignoring group is unused)
        var unfocused = Ctx([rowB], encodes);
        AssertApprox(mark.EffectiveOpacity(unfocused, rowB), 1.0);
        AssertApprox(mark.SeriesOpacity(unfocused, "B", 1f), 1.0);
    }

    [TestCase]
    public void AnimProgressMultipliesEntryByTheRemainingExit()
    {
        var mark = new ProbeMark();
        var data = new List<DataRow>();

        AssertApprox(mark.AnimProgress(Ctx(data, animation: new AnimationContext { EntryProgress = 0.8f })), 0.8);
        AssertApprox(mark.AnimProgress(Ctx(data, animation: new AnimationContext { EntryProgress = 0.8f, ExitProgress = 0.25f })), 0.8 * 0.75);
        AssertApprox(mark.AnimProgress(Ctx(data, animation: new AnimationContext { EntryProgress = 1f, ExitProgress = 1f })), 0.0);
    }

    // ── series hiding / grouping ────────────────────────────────────────────

    [TestCase]
    public void IsSeriesHiddenHandlesNullEmptyAndUnknownSeries()
    {
        var mark = new ProbeMark();
        var encodes = new EncodeSet();
        encodes.Set(Channel.Color, new FieldEncode("series"));
        var row = TestContexts.Row(("series", "B"));
        var data = new List<DataRow> { row };

        AssertThat(mark.SeriesKeyOf(Ctx(data, encodes), row)).IsEqual("B");

        AssertThat(mark.RowIsHidden(Ctx(data, encodes), row)).IsFalse();                                              // null set
        AssertThat(mark.RowIsHidden(Ctx(data, encodes, hiddenSeries: new HashSet<string>()), row)).IsFalse();         // empty set
        AssertThat(mark.RowIsHidden(Ctx(data, encodes, hiddenSeries: new HashSet<string> { "A" }), row)).IsFalse();
        AssertThat(mark.RowIsHidden(Ctx(data, encodes, hiddenSeries: new HashSet<string> { "B" }), row)).IsTrue();

        // without a Color encode there is no series key, so nothing can be hidden
        var noColor = Ctx(data, hiddenSeries: new HashSet<string> { "B" });
        AssertThat(mark.SeriesKeyOf(noColor, row) is null).IsTrue();
        AssertThat(mark.RowIsHidden(noColor, row)).IsFalse();
    }

    [TestCase]
    public void GroupByChannelKeepsInsertionOrderAndTheDefaultBucket()
    {
        var mark = new ProbeMark();
        var encodes = new EncodeSet();
        encodes.Set(Channel.Color, new FieldEncode("series"));
        var data = new List<DataRow>
        {
            TestContexts.Row(("series", "B")),
            TestContexts.Row(("series", "A")),
            TestContexts.Row(("series", "B")),
            TestContexts.Row(("series", null)),   // present but null → no key
            TestContexts.Row(("other", 1.0)),     // field missing → no key
        };

        var groups = mark.Groups(Ctx(data, encodes), Channel.Color);
        var keys = groups.Keys.ToList();

        AssertThat(keys.Count).IsEqual(3);
        AssertThat(keys[0].ToString()).IsEqual("B");
        AssertThat(keys[1].ToString()).IsEqual("A");
        AssertThat(keys[2].ToString()).IsEqual("__default__");
        AssertThat(groups["B"].Count).IsEqual(2);
        AssertThat(groups["A"].Count).IsEqual(1);
        AssertThat(groups["__default__"].Count).IsEqual(2);

        // with no encode every row lands in the default bucket
        var noEncode = mark.Groups(Ctx(data), Channel.Color);
        AssertThat(noEncode.Count).IsEqual(1);
        AssertThat(noEncode["__default__"].Count).IsEqual(5);
    }

    [TestCase]
    public void CachedGroupByChannelReusesAndRebuildsOnVersionOrChannelChange()
    {
        var mark = new ProbeMark();
        var encodes = new EncodeSet();
        encodes.Set(Channel.Color, new FieldEncode("series"));
        var data = new List<DataRow> { TestContexts.Row(("series", "A")) };

        var v1Color = mark.CachedGroups(Ctx(data, encodes, dataVersion: 1), Channel.Color);
        var v1ColorAgain = mark.CachedGroups(Ctx(data, encodes, dataVersion: 1), Channel.Color);
        AssertThat(ReferenceEquals(v1Color, v1ColorAgain)).IsTrue();   // same version + channel → cached

        var v2Color = mark.CachedGroups(Ctx(data, encodes, dataVersion: 2), Channel.Color);
        AssertThat(ReferenceEquals(v1Color, v2Color)).IsFalse();       // data version changed

        var v2X = mark.CachedGroups(Ctx(data, encodes, dataVersion: 2), Channel.X);
        AssertThat(ReferenceEquals(v2Color, v2X)).IsFalse();           // channel changed

        var backToColor = mark.CachedGroups(Ctx(data, encodes, dataVersion: 2), Channel.Color);
        AssertThat(ReferenceEquals(v2Color, backToColor)).IsFalse();   // the channel round-trip rebuilds
    }

    // ── numeric / label helpers ─────────────────────────────────────────────

    [TestCase]
    public void GetDoubleThrowsKeyNotFoundForAMissingField()
    {
        var mark = new ProbeMark();
        var row = TestContexts.Row(("v", 1.0));

        AssertThat(CaptureException(() => mark.ReadDouble(row, "ghost")) is KeyNotFoundException).IsTrue();
        AssertApprox(mark.ReadDouble(row, "v"), 1.0);

        // a present-but-null field is a missing value, reported as NaN
        var nullRow = new DataRow().Set("v", null!);
        AssertThat(double.IsNaN(mark.ReadDouble(nullRow, "v"))).IsTrue();
        AssertThat(double.IsNaN(mark.ConvertToDouble(null, "v"))).IsTrue();
        AssertThat(float.IsNaN(mark.ConvertToSingle(null, "v"))).IsTrue();
    }

    [TestCase]
    public void HasFieldsOverloadsRequirePresentNonNullValues()
    {
        var mark = new ProbeMark();
        var row = TestContexts.Row(("a", 1.0), ("b", 2.0), ("c", 3.0), ("d", 4.0), ("e", null));

        AssertThat(mark.HasOne(row, "a")).IsTrue();
        AssertThat(mark.HasOne(row, "e")).IsFalse();     // present but null
        AssertThat(mark.HasOne(row, "zz")).IsFalse();    // missing

        AssertThat(mark.HasBoth(row, "a", "b")).IsTrue();
        AssertThat(mark.HasBoth(row, "a", "zz")).IsFalse();
        AssertThat(mark.HasFour(row, "a", "b", "c", "d")).IsTrue();
        AssertThat(mark.HasFour(row, "a", "b", "c", "e")).IsFalse();
        AssertThat(mark.HasAll(row, "a", "b", "c", "d", "b")).IsTrue();
        AssertThat(mark.HasAll(row, "a", "b", "c", "d", "e")).IsFalse();

        // GetStringOrNull mirrors the same "missing means null" contract
        AssertThat(mark.ReadString(row, "c")).IsEqual("3");
        AssertThat(mark.ReadString(row, "e") is null).IsTrue();
        AssertThat(mark.ReadString(row, "zz") is null).IsTrue();
    }

    [TestCase]
    public void FormatLabelUsesTheFastPathsAndTheGenericFallback()
    {
        var mark = new ProbeMark();

        AssertThat(mark.Format("{0}", 5.0, 3.0)).IsEqual("5");
        AssertThat(mark.Format("{0}", null, 3.0)).IsEqual(string.Empty);
        AssertThat(mark.Format("{0}: {1}", 5.0, 3.0)).IsEqual("5: 3");
        AssertThat(mark.Format("{0}: {1}", null, null)).IsEqual(": ");
        AssertThat(mark.Format("v={0}", 7.0, 2.0)).IsEqual("v=7");
        AssertThat(mark.Format("{1}/{0}", 7.0, 2.0)).IsEqual("2/7");

        // only two arguments are supplied: a third placeholder is a format error
        AssertThat(CaptureException(() => mark.Format("{0} {1} {2}", 1, 2)) is FormatException).IsTrue();
    }

    [TestCase]
    public void NonNumericValuesAreReportedAsNaNWithAOneTimeWarning()
    {
        var mark = new ProbeMark();
        var log = EngineMessageLog.Attach();
        try
        {
            // A value that is not a number means "no value": the caller skips the element it belongs to,
            // which is the same answer a null field gets. It used to throw InvalidCastException - and one
            // such row took the whole mark out of the frame (the render stage catches and logs once).
            AssertThat(double.IsNaN(mark.ConvertToDouble("abc", "growth"))).IsTrue();
            AssertThat(float.IsNaN(mark.ConvertToSingle("abc", "ratio"))).IsTrue();

            // What the exception message used to carry is not lost: the mark, the field and the value.
            var growth = log.WarningsContaining("growth");
            AssertThat(growth.Length).IsEqual(1);
            AssertThat(growth[0].Contains("abc")).IsTrue();

            // ...and it is reported once per field, not once per row per frame.
            mark.ConvertToDouble("abc", "growth");
            mark.ConvertToDouble(17.0, "growth");
            AssertThat(log.WarningsContaining("growth").Length).IsEqual(1);
        }
        finally
        {
            log.Detach();
        }

        // the fast paths for numeric types stay loss-free
        AssertApprox(mark.ConvertToDouble(3, "growth"), 3.0);
        AssertApprox(mark.ConvertToDouble(1.5f, "growth"), 1.5);
        AssertApprox(mark.ConvertToSingle(2.5f, "ratio"), 2.5);

        // A numeric string is a number, not a dirty value (CSV-imported rows arrive as text).
        AssertApprox(mark.ConvertToDouble("2.5", "growth"), 2.5);
        AssertApprox(mark.ConvertToSingle("2.5", "ratio"), 2.5);
    }

    // ── Per-mark tooltip content ────────────────────────────────────────────

    // ── Per-mark tooltip content ────────────────────────────────────────────

    [TestCase]
    public void MarkTooltipContentBuilderIsUsedByTheTooltip()
    {
        var canvas = new FakeCanvas2D();
        var chart = new Chart(canvas) { Width = 400f, Height = 300f };
        chart.Data(new List<DataRow> { TestContexts.Row(("cat", "A"), ("value", 10.0)) });

        var mark = new PointMark
        {
            TooltipContentBuilder = _ => new[] { TooltipLine.Plain("custom-tooltip") },
        };
        chart.Mark(mark);
        chart.Encode(Channel.X, "cat");
        chart.Encode(Channel.Y, "value");
        chart.Render();

        // The single category sits at the horizontal centre; its value (10 of an auto-fitted 0..10)
        // puts the point on the top edge of the plot.
        var plot = chart.CurrentPlotArea!.Value;
        var hit = chart.HitTest(new Vector2(plot.X + plot.Width / 2f, plot.Y));

        AssertThat(hit is not null).IsTrue();
        AssertThat(hit!.TooltipLines is { Count: 1 }).IsTrue();
        AssertThat(hit.TooltipLines![0].Spans[0].Text).IsEqual("custom-tooltip");
    }
}
