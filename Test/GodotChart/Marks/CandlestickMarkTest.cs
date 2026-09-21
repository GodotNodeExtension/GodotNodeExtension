namespace GodotNodeExtension.Tests.GodotChart.Marks;

using System;
using System.Collections.Generic;
using GdUnit4;
using Godot;
using GodotNodeExtension.Component.GodotChart;
using GodotNodeExtension.Component.GodotChart.Marks;
using Support;
using static GdUnit4.Assertions;

/// <summary>
/// Behaviour specification for <see cref="CandlestickMark"/>.
/// <para>
/// Covers the bullish/bearish colours, the body geometry (width ratio, minimum doji height, hollow
/// rising bodies), the configurable OHLC fields and the scale contribution, the hidden-series
/// filter, the guard for inverted OHLC ranges and the OHLC hit test. The skip of a row with a
/// missing, null or non-finite OHLC value is pinned once for every mark by
/// <see cref="MarkDataSafetyTest"/>.
/// </para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class CandlestickMarkTest
{
    private static readonly string[] SingleCategory = { "A" };
    private static readonly string[] TwoCategories = { "A", "B" };

    // ── shared helpers ──

    /// <summary>Local floating point comparison: mark geometry is never bit-exact.</summary>
    private static bool Approx(double a, double b, double tol = 0.01) => Math.Abs(a - b) <= tol;

    private static DataRow D(params (string Field, object? Value)[] fields) => TestContexts.Row(fields);

    private static (CandlestickMark Mark, FakeCanvas2D Canvas, MarkContext Ctx) CandleCtx(
        List<DataRow> rows, IReadOnlySet<string>? hiddenSeries = null)
    {
        var canvas = new FakeCanvas2D();
        var encodes = TestContexts.XyEncodes("cat", "close");
        encodes.Set(Channel.Color, new FieldEncode("series"));
        var ctx = TestContexts.Mark(canvas, rows, encodes,
            TestContexts.CategoryScales(TwoCategories), hiddenSeries: hiddenSeries);
        return (new CandlestickMark(), canvas, ctx);
    }

    // ── Default rendering ──

    [TestCase]
    public void CandlestickUsesBullishAndBearishColors()
    {
        var bull = new Color(0.1f, 0.9f, 0.2f);
        var bear = new Color(0.9f, 0.2f, 0.1f);
        var (mark, canvas, ctx) = CandleCtx(MarkCases.Candles());
        mark.BullishColor = bull;
        mark.BearishColor = bear;
        mark.Render(ctx);

        // Row A closes above its open (bullish), row B below (bearish).
        AssertThat(canvas.FillColors.Count).IsEqual(2);
        AssertThat(canvas.FillColors[0].ToHtml()).IsEqual(bull.ToHtml());
        AssertThat(canvas.FillColors[1].ToHtml()).IsEqual(bear.ToHtml());
    }

    [TestCase]
    public void CandlestickDojiKeepsAMinimumBodyHeight()
    {
        var rows = new List<DataRow>
        {
            D(("cat", "A"), ("open", 10.0), ("high", 12.0), ("low", 8.0), ("close", 10.0)),
        };
        var (mark, canvas, ctx) = CandleCtx(rows);
        mark.Render(ctx);

        AssertThat(canvas.Rects.Count).IsEqual(1);
        AssertThat(Approx(canvas.Rects[0].H, 1, 0.001)).IsTrue();
    }

    [TestCase]
    public void CandlestickDrawsCompleteRow()
    {
        var rows = new List<DataRow>
        {
            D(("cat", "A"), ("open", 10.0), ("high", 15.0), ("low", 8.0), ("close", 12.0)),
        };
        var (mark, canvas, ctx) = CandleCtx(rows);

        mark.Render(ctx);

        AssertThat(canvas.DrewAnything).IsTrue();
    }

    // ── Options ──

    [TestCase]
    public void CandlestickBodyWidthRatioSetsTheBodyWidth()
    {
        var (mark, canvas, ctx) = CandleCtx(MarkCases.Candles());
        mark.BodyWidthRatio = 0.4f;
        mark.Render(ctx);

        // slot width = 400 / 2 categories = 200.
        AssertThat(canvas.Rects.Count).IsEqual(2);
        AssertThat(Approx(canvas.Rects[0].W, 200 * 0.4f, 0.05)).IsTrue();
    }

    [TestCase]
    public void CandlestickFillBullishSwitchHollowsRisingCandles()
    {
        var rows = new List<DataRow>
        {
            D(("cat", "A"), ("open", 10.0), ("high", 15.0), ("low", 8.0), ("close", 12.0)),
        };
        var (mark, canvas, ctx) = CandleCtx(rows);
        mark.FillBullish = false;
        mark.Render(ctx);

        AssertThat(canvas.FillCount).IsEqual(0);      // the body is stroked, not filled
        AssertThat(canvas.StrokeCount).IsEqual(3);    // two wicks + the body outline
    }

    [TestCase]
    public void CandlestickUsesTheConfiguredOhlcFieldsAndContributesItsScale()
    {
        var rows = new List<DataRow>
        {
            D(("cat", "A"), ("o", 10.0), ("h", 15.0), ("l", 8.0), ("c", 12.0)),
        };
        var canvas = new FakeCanvas2D();
        var mark = new CandlestickMark
        {
            OpenField = "o", HighField = "h", LowField = "l", CloseField = "c",
        };
        var ctx = TestContexts.Mark(canvas, rows, TestContexts.XyEncodes("cat", "c"),
            TestContexts.CategoryScales(SingleCategory));
        mark.Render(ctx);
        AssertThat(canvas.Rects.Count).IsEqual(1);

        var scales = new ScaleSet();
        mark.ContributeScales(scales, TestContexts.XyEncodes("cat", "c"), rows);
        var y = scales.TryGet(Channel.Y) as LinearScale;
        AssertThat(y is not null).IsTrue();
        // The rows span 8 .. 15; the contribution pads that by 5% on both ends (0.35).
        AssertThat(Approx(y!.Min, 7.65f)).IsTrue();
        AssertThat(Approx(y.Max, 15.35f)).IsTrue();
    }

    [TestCase]
    public void CandlestickHiddenSeriesIsNotDrawn()
    {
        var rows = new List<DataRow>
        {
            D(("cat", "A"), ("open", 10.0), ("high", 15.0), ("low", 8.0), ("close", 12.0), ("series", "S1")),
            D(("cat", "B"), ("open", 12.0), ("high", 14.0), ("low", 9.0), ("close", 10.0), ("series", "S2")),
        };
        var (mark, canvas, ctx) = CandleCtx(rows, new HashSet<string> { "S1" });
        mark.Render(ctx);
        AssertThat(canvas.Rects.Count).IsEqual(1);

        var (all, allCanvas, allCtx) = CandleCtx(rows);
        all.Render(allCtx);
        AssertThat(allCanvas.Rects.Count).IsEqual(2);
    }

    // ── Element opacity ──

    /// <summary>
    /// Render one candle per given Opacity-channel value and report the alpha the candles were filled
    /// with. The channel needs a scale, exactly like the chart installs one for a bound opacity field.
    /// </summary>
    private static List<float> RenderWithOpacityChannel(params double[] alphas)
    {
        var rows = new List<DataRow>(alphas.Length);
        for (int i = 0; i < alphas.Length; i++)
        {
            rows.Add(D(("cat", TwoCategories[i]), ("open", 10.0), ("high", 15.0),
                       ("low", 8.0), ("close", 12.0), ("alpha", alphas[i])));
        }

        var canvas = new FakeCanvas2D();
        var encodes = TestContexts.XyEncodes("cat", "close");
        encodes.Set(Channel.Opacity, new FieldEncode("alpha"));

        var scales = TestContexts.CategoryScales(TwoCategories);
        scales.Set(Channel.Opacity, new LinearScale(0, 1));

        new CandlestickMark().Render(TestContexts.Mark(canvas, rows, encodes, scales));
        return canvas.FillOpacities;
    }

    [TestCase]
    public void CandlestickBodyAlphaFollowsTheOpacityChannel()
    {
        // The Opacity channel used to be bypassed: every candle was painted with the global animation
        // opacity alone, so a per-row alpha had no effect on the bodies or the wicks.
        var alphas = RenderWithOpacityChannel(0.4, 1.0);

        AssertThat(alphas.Count).IsEqual(2);
        AssertThat(Approx(alphas[0], 0.4, 0.001)).IsTrue();
        AssertThat(Approx(alphas[1], 1.0, 0.001)).IsTrue();
    }

    [TestCase]
    public void CandlestickStyleCallbackOpacityReachesTheBody()
    {
        var rows = new List<DataRow>
        {
            D(("cat", "A"), ("open", 10.0), ("high", 15.0), ("low", 8.0), ("close", 12.0)),
        };
        var canvas = new FakeCanvas2D();
        var mark = new CandlestickMark
        {
            StyleOverride = (_, _, style) => style.WithOpacity(0.25f),
        };

        mark.Render(TestContexts.Mark(canvas, rows, TestContexts.XyEncodes("cat", "close"),
            TestContexts.CategoryScales(SingleCategory)));

        AssertThat(canvas.FillOpacities.Count).IsEqual(1);
        AssertThat(Approx(canvas.FillOpacities[0], 0.25f, 0.001)).IsTrue();
    }

    [TestCase]
    public void CandlestickDimsTheUnfocusedSeries()
    {
        var rows = new List<DataRow>
        {
            D(("cat", "A"), ("open", 10.0), ("high", 15.0), ("low", 8.0), ("close", 12.0), ("series", "S1")),
            D(("cat", "B"), ("open", 12.0), ("high", 14.0), ("low", 9.0), ("close", 10.0), ("series", "S2")),
        };
        var canvas = new FakeCanvas2D();
        var encodes = TestContexts.XyEncodes("cat", "close");
        encodes.Set(Channel.Color, new FieldEncode("series"));

        var mark = new CandlestickMark();
        mark.States.InactiveOpacity = 0.3f;

        mark.Render(TestContexts.Mark(canvas, rows, encodes,
            TestContexts.CategoryScales(TwoCategories), focusedSeries: "S2"));

        AssertThat(canvas.FillOpacities.Count).IsEqual(2);
        AssertThat(Approx(canvas.FillOpacities[0], 0.3f, 0.001)).IsTrue();   // S1 is not focused
        AssertThat(Approx(canvas.FillOpacities[1], 1.0, 0.001)).IsTrue();
    }

    // ── Hit testing ──

    [TestCase]
    public void CandlestickHitTestReportsOhlcInsideTheCandleOnly()
    {
        var (mark, canvas, ctx) = CandleCtx(MarkCases.Candles());
        mark.Render(ctx);

        // The hit area is the candle itself: the body's width around the candle axis and the wick
        // span, both from the same geometry Render draws (row A: open 10, high 15, low 8, close 12 over
        // a [0,100] axis -> the wick spans y 255..276 on the 300 px plot).
        AssertThat(canvas.Lines.Count).IsEqual(4);   // two wicks (upper and lower) per candle, two candles
        float axisX = canvas.Lines[0].X0;

        var hit = mark.HitTest(ctx, new Vector2(axisX, 265f));
        AssertThat(hit is not null).IsTrue();
        AssertThat(hit!.RowIndex).IsEqual(0);
        AssertThat(hit.Label!.Contains("O:")).IsTrue();
        AssertThat(hit.Label.Contains("H:")).IsTrue();
        AssertThat(hit.Label.Contains("L:")).IsTrue();
        AssertThat(hit.Label.Contains("C:")).IsTrue();

        // Above the wick span of that candle nothing is drawn, so nothing may be hit ...
        AssertThat(mark.HitTest(ctx, new Vector2(axisX, 150f)) is null).IsTrue();
        // ... and the same holds well outside the body's width, even at the candle's own height.
        AssertThat(mark.HitTest(ctx, new Vector2(axisX + 80f, 265f)) is null).IsTrue();
    }

    [TestCase]
    public void CandlestickDoesNotHitTestIncompleteRow()
    {
        var rows = new List<DataRow>
        {
            D(("cat", "A"), ("open", 10.0), ("close", 12.0)),
        };
        var (mark, _, ctx) = CandleCtx(rows);

        var hit = mark.HitTest(ctx, new Vector2(200f, 150f));

        AssertThat(hit is null).IsTrue();
    }

    // ── Degenerate data ──

    [TestCase]
    public void CandlestickWithInvertedHighLowKeepsFiniteGeometry()
    {
        var rows = new List<DataRow>
        {
            D(("cat", "A"), ("open", 10.0), ("high", 5.0), ("low", 15.0), ("close", 12.0)),
        };
        var (mark, canvas, ctx) = CandleCtx(rows);
        mark.Render(ctx);

        AssertThat(canvas.NonFiniteCoordinateCount).IsEqual(0);
        AssertThat(canvas.NegativeSizeRectCount).IsEqual(0);
    }

    // ── Allocation ──

    /// <summary>Objects a whole frame may create regardless of the element count (axes, grid, background).</summary>
    private const int FrameOverheadBudget = 12;

    private static (int Paths, int Paints) RenderOnce(Mark mark, List<DataRow> data,
        string x, string y, int frames = 1)
    {
        var canvas = new FakeCanvas2D();
        using var chart = new Chart(canvas) { Width = 400f, Height = 300f };
        chart.Data(data);
        chart.Mark(mark);
        chart.Encode(Channel.X, x);
        chart.Encode(Channel.Y, y);

        canvas.PathCreateCount = 0;
        canvas.PaintCreateCount = 0;
        for (int i = 0; i < frames; i++)
            chart.Render();
        return (canvas.PathCreateCount, canvas.PaintCreateCount);
    }

    private static List<DataRow> CandleRows(int count)
    {
        var rows = new List<DataRow>(count);
        for (int i = 0; i < count; i++)
        {
            double open = 10 + (i % 5);
            rows.Add(new DataRow(5)
                .Set("cat", $"C{i}")
                .Set("open", open)
                .Set("high", open + 4)
                .Set("low", open - 2)
                .Set("close", open + (i % 2 == 0 ? 2 : -1)));
        }
        return rows;
    }

    [TestCase]
    public void CandlestickDoesNotAllocatePerCandle()
    {
        var small = RenderOnce(new CandlestickMark(), CandleRows(4), "cat", "close");
        var large = RenderOnce(new CandlestickMark(), CandleRows(120), "cat", "close");

        int delta = (large.Paths + large.Paints) - (small.Paths + small.Paints);
        AssertThat(delta <= FrameOverheadBudget).IsTrue();
    }

    /// <summary><see cref="CandlestickMark.WickWidth"/> is the stroke width of both wick lines.</summary>
    [TestCase]
    public void CandleWickWidthReachesTheWickLines()
    {
        var (mark, canvas, ctx) = CandleCtx(MarkCases.Candles());
        mark.WickWidth = 6f;
        mark.Render(ctx);

        // The wicks go through DrawLine, so their width is on the line record rather than in StrokeWidths.
        AssertThat(canvas.LineWidths).Contains(6f);
    }
}
