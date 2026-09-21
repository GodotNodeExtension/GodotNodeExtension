namespace GodotNodeExtension.Tests.GodotChart.Marks;

using System;
using System.Collections.Generic;
using System.Linq;
using GdUnit4;
using Godot;
using GodotNodeExtension.Component.GodotChart;
using GodotNodeExtension.Component.GodotChart.Marks;
using Support;
using static GdUnit4.Assertions;

/// <summary>
/// Behaviour specification for <see cref="WaffleMark"/>: the cell grid produced by
/// <see cref="WaffleMark.TotalCells"/> / <see cref="WaffleMark.Columns"/> / <see cref="WaffleMark.CellGap"/>,
/// the proportional cell allocation per category, hit testing, the clamping of degenerate grid
/// parameters and the cell-layout cache (including its allocation behaviour).
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class WaffleMarkTest
{
    private static readonly string[] TwoCategories = { "A", "B" };
    private static readonly string[] ThreeCategories = { "A", "B", "C" };

    // ── helpers ─────────────────────────────────────────────────────────────

    /// <summary>Local floating point comparison: mark geometry is never bit-exact.</summary>
    private static bool Approx(double a, double b, double tol = 0.01) => Math.Abs(a - b) <= tol;

    private static DataRow D(params (string Field, object? Value)[] fields) => TestContexts.Row(fields);

    private static Chart WaffleChart(FakeCanvas2D canvas, List<DataRow> rows, Mark mark)
    {
        var chart = new Chart(canvas) { Width = 400f, Height = 300f };
        chart.Data(rows);
        chart.Mark(mark);
        chart.Encode(Channel.X, "cat");
        chart.Encode(Channel.Y, "value");
        return chart;
    }

    private static ColorScale ColorBy(IEnumerable<string> keys)
    {
        var scale = new ColorScale();
        scale.Fit(keys);
        return scale;
    }

    /// <summary>Two equal categories, so every cell of the grid is allocated.</summary>
    private static List<DataRow> Even() =>
    [
        D(("cat", "A"), ("value", 1.0)),
        D(("cat", "B"), ("value", 1.0)),
    ];

    /// <summary>Three categories used by the cache tests: 50 / 30 / 20.</summary>
    private static List<DataRow> Proportions() =>
    [
        D(("cat", "A"), ("value", 50.0)),
        D(("cat", "B"), ("value", 30.0)),
        D(("cat", "C"), ("value", 20.0)),
    ];

    // ── Default rendering ───────────────────────────────────────────────────

    [TestCase]
    public void WaffleDrawsExactlyTotalCellsCells()
    {
        var canvas = new FakeCanvas2D();
        new WaffleMark { TotalCells = 25, Columns = 5 }.Render(
            TestContexts.Mark(canvas, Even(), TestContexts.XyEncodes("cat", "value"), new ScaleSet()));

        AssertThat(canvas.RoundRects.Count).IsEqual(25);
    }

    [TestCase]
    public void WaffleCellGapShrinksTheCells()
    {
        var noGap = new FakeCanvas2D();
        new WaffleMark { TotalCells = 100, Columns = 10, CellGap = 0f }.Render(
            TestContexts.Mark(noGap, Even(), TestContexts.XyEncodes("cat", "value"), new ScaleSet()));

        var withGap = new FakeCanvas2D();
        new WaffleMark { TotalCells = 100, Columns = 10, CellGap = 4f }.Render(
            TestContexts.Mark(withGap, Even(), TestContexts.XyEncodes("cat", "value"), new ScaleSet()));

        // Height-limited cell: 300/10 = 30 with no gap, smaller once the gap is subtracted.
        AssertThat(Approx(noGap.RoundRects[0].W, 30f)).IsTrue();
        AssertThat(withGap.RoundRects[0].W < 30f).IsTrue();
    }

    // ── Cell allocation ─────────────────────────────────────────────────────

    [TestCase]
    public void WaffleAllocatesCellsProportionally()
    {
        var data = new List<DataRow>
        {
            D(("cat", "A"), ("value", 75.0)),
            D(("cat", "B"), ("value", 25.0)),
        };
        var encodes = TestContexts.XyEncodes("cat", "value");
        encodes.Set(Channel.Color, new FieldEncode("cat"));

        var scales = new ScaleSet();
        scales.Set(Channel.Color, ColorBy(TwoCategories));

        var canvas = new FakeCanvas2D();
        new WaffleMark { TotalCells = 10, Columns = 5 }.Render(
            TestContexts.Mark(canvas, data, encodes, scales));

        // 75/25 of 10 cells -> 8 / 2 (largest-remainder tie broken by first category).
        var first = canvas.FillColors[0];
        AssertThat(canvas.FillColors.Count).IsEqual(10);
        AssertThat(canvas.FillColors.Count(c => c == first)).IsEqual(8);
    }

    [TestCase]
    public void WaffleSkipsNonPositiveCategories()
    {
        var data = new List<DataRow>
        {
            D(("cat", "A"), ("value", 50.0)),
            D(("cat", "B"), ("value", -5.0)),
            D(("cat", "C"), ("value", 50.0)),
        };
        var encodes = TestContexts.XyEncodes("cat", "value");
        encodes.Set(Channel.Color, new FieldEncode("cat"));

        var scales = new ScaleSet();
        scales.Set(Channel.Color, ColorBy(ThreeCategories));

        var canvas = new FakeCanvas2D();
        new WaffleMark { TotalCells = 10, Columns = 5 }.Render(
            TestContexts.Mark(canvas, data, encodes, scales));

        var colorB = scales.Get(Channel.Color) is IColorScale cs ? cs.MapColor("B") : Colors.White;
        AssertThat(canvas.FillColors.Count).IsEqual(10);            // the grid is still full
        AssertThat(canvas.FillColors.Contains(colorB)).IsFalse();   // but B is never drawn
    }

    // ── Degenerate grid parameters ──────────────────────────────────────────

    [TestCase]
    public void WaffleRejectsDegenerateGridParameters()
    {
        var canvas = new FakeCanvas2D();
        var mark = new WaffleMark { Columns = 0, TotalCells = 0, CellGap = -5f };
        var chart = WaffleChart(canvas,
        [
            D(("cat", "A"), ("value", 50.0)),
            D(("cat", "B"), ("value", 50.0)),
        ], mark);

        chart.Render();

        AssertThat(mark.Columns).IsEqual(1);
        AssertThat(mark.TotalCells).IsEqual(1);
        AssertThat(mark.CellGap).IsEqual(0f);
        AssertThat(canvas.NonFiniteCoordinateCount).IsEqual(0);
        AssertThat(canvas.NegativeSizeRectCount).IsEqual(0);
    }

    // ── Hit testing ─────────────────────────────────────────────────────────

    [TestCase]
    public void WaffleHitTestReturnsTheCellRow()
    {
        var data = new List<DataRow>
        {
            D(("cat", "A"), ("value", 50.0)),
            D(("cat", "B"), ("value", 50.0)),
        };
        var mark = new WaffleMark { TotalCells = 10, Columns = 5 };
        var canvas = new FakeCanvas2D();
        var ctx = TestContexts.Mark(canvas, data, TestContexts.XyEncodes("cat", "value"), new ScaleSet());
        mark.Render(ctx);

        var cell = canvas.RoundRects[0];
        var hit = mark.HitTest(ctx, new Vector2(cell.X + cell.W * 0.5f, cell.Y + cell.H * 0.5f));

        AssertThat(hit is not null).IsTrue();
        AssertThat(hit!.RowIndex).IsEqual(0);
        AssertThat(hit.Label).IsEqual("A");
    }

    // ── Layout cache and per-cell allocation ────────────────────────────────

    [TestCase]
    public void WaffleIsNotRebuiltWhenNothingChanged()
    {
        var canvas = new FakeCanvas2D();
        var mark = new WaffleMark();
        var chart = WaffleChart(canvas, Proportions(), mark);

        chart.Render();
        chart.Render();

        AssertThat(mark.LayoutBuildCount).IsEqual(1);
    }

    [TestCase]
    public void ChangingWaffleColumnsInvalidatesTheCellLayout()
    {
        var canvas = new FakeCanvas2D();
        var mark = new WaffleMark { TotalCells = 12, Columns = 6 };
        var chart = WaffleChart(canvas, Proportions(), mark);
        chart.Render();
        // Cell rects only: the chart background spans the full width, so filter it out.
        float widthWithSixColumns = canvas.Rects.Where(r => r.W is > 0f and < 200f).Max(r => r.W);

        mark.Columns = 3; // same data, different grid
        canvas.Rects.Clear();
        chart.Render();
        float widthWithThreeColumns = canvas.Rects.Where(r => r.W is > 0f and < 200f).Max(r => r.W);

        AssertThat(mark.LayoutBuildCount >= 2).IsTrue();
        // Fewer columns means wider cells; the geometry must follow the new property value.
        AssertThat(widthWithThreeColumns > widthWithSixColumns).IsTrue();
    }

    [TestCase]
    public void WaffleHiddenSeriesChangesTheCacheKey()
    {
        // The same mark, the same data list instance and the same versions - only the hidden set
        // differs. It is part of Mark.CacheKey, so the cell layout (which contains the hidden
        // category's cells) must be rebuilt instead of reusing the previous allocation.
        var mark = new WaffleMark();
        var data = Proportions();
        var encodes = TestContexts.XyEncodes("cat", "value");
        var canvas = new FakeCanvas2D();

        mark.Render(TestContexts.Mark(canvas, data, encodes, new ScaleSet()));
        AssertThat(mark.LayoutBuildCount).IsEqual(1);

        mark.Render(TestContexts.Mark(canvas, data, encodes, new ScaleSet(),
            hiddenSeries: new HashSet<string> { "B" }));
        AssertThat(mark.LayoutBuildCount).IsEqual(2);
    }

    /// <summary>
    /// The layout cache is dropped on request: a host that rewrites the rows of the list it handed the mark
    /// (same list, same version) cannot be seen by the cache key, so it needs the entry point the sankey and
    /// treemap marks expose too.
    /// </summary>
    [TestCase]
    public void WaffleInvalidateCacheRebuildsTheLayout()
    {
        var mark = new WaffleMark();
        var data = Proportions();
        var encodes = TestContexts.XyEncodes("cat", "value");
        var canvas = new FakeCanvas2D();
        var ctx = TestContexts.Mark(canvas, data, encodes, new ScaleSet());

        mark.Render(ctx);
        mark.Render(ctx);
        AssertThat(mark.LayoutBuildCount).IsEqual(1);     // the layout is cached between frames

        mark.InvalidateCache();
        mark.Render(ctx);
        AssertThat(mark.LayoutBuildCount).IsEqual(2);
    }

    [TestCase]
    public void WaffleDoesNotAllocatePerCell()
    {
        var small = RenderOnce(new WaffleMark { TotalCells = 16 }, 3);
        var large = RenderOnce(new WaffleMark { TotalCells = 400 }, 3);

        int delta = (large.Paths + large.Paints) - (small.Paths + small.Paints);
        AssertThat(delta <= FrameOverheadBudget).IsTrue();
    }

    /// <summary>Objects a whole frame may create regardless of the element count (axes, grid, background).</summary>
    private const int FrameOverheadBudget = 12;

    /// <summary>Render one frame through a chart and report how many drawing objects it requested.</summary>
    private static (int Paths, int Paints) RenderOnce(Mark mark, int categoryCount)
    {
        var canvas = new FakeCanvas2D();
        var chart = WaffleChart(canvas, MarkCases.ManyBars(categoryCount), mark);

        canvas.PathCreateCount = 0;
        canvas.PaintCreateCount = 0;
        chart.Render();
        return (canvas.PathCreateCount, canvas.PaintCreateCount);
    }
}
