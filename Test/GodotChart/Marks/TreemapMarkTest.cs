namespace GodotNodeExtension.Tests.GodotChart.Marks;

using System;
using System.Collections.Generic;
using System.Linq;
using GdUnit4;
using Godot;
using GodotNodeExtension.Component.GodotChart;
using GodotNodeExtension.Component.GodotChart.Canvas;
using GodotNodeExtension.Component.GodotChart.Marks;
using Support;
using static GdUnit4.Assertions;

/// <summary>
/// Behaviour specification for <see cref="TreemapMark"/>: the cell layout and the area it covers,
/// labels, <see cref="TreemapMark.CellGap"/>, the binary-split and squarified layout modes, colours
/// and hover highlight, hit testing, degenerate values and the layout cache.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class TreemapMarkTest
{
    private static readonly string[] ThreeCategories = { "A", "B", "C" };
    private static readonly string[] TreeCategories = { "root", "media", "video", "music", "code" };
    private static readonly string[] FlatCategories = { "group", "leaf" };

    // ── helpers ─────────────────────────────────────────────────────────────

    /// <summary>Local floating point comparison: mark geometry is never bit-exact.</summary>
    private static bool Approx(double a, double b, double tol = 0.01) => Math.Abs(a - b) <= tol;

    /// <summary>Assert that two numbers are within <paramref name="tol"/> of each other.</summary>
    private static void AssertApprox(double a, double b, double tol = 0.01)
        => AssertThat(Approx(a, b, tol)).IsTrue();

    private static DataRow D(params (string Field, object? Value)[] fields) => TestContexts.Row(fields);

    private static (TreemapMark Mark, FakeCanvas2D Canvas, MarkContext Ctx) TreemapCtx(
        List<DataRow> rows, PlotArea? plot = null, int hovered = -1)
    {
        var canvas = new FakeCanvas2D();
        var ctx = TestContexts.Mark(canvas, rows, TestContexts.XyEncodes("cat", "value"),
            TestContexts.CategoryScales(ThreeCategories), plot: plot, hoveredRowIndex: hovered);
        return (new TreemapMark(), canvas, ctx);
    }

    private static Chart TreemapChart(FakeCanvas2D canvas, List<DataRow> rows, Mark mark,
                                     float width = 400f, float height = 300f, string xField = "label")
    {
        var chart = new Chart(canvas) { Width = width, Height = height };
        chart.Data(rows);
        chart.Mark(mark);
        chart.Encode(Channel.X, xField);
        chart.Encode(Channel.Y, "value");
        return chart;
    }

    // ── Default rendering and labels ────────────────────────────────────────

    [TestCase]
    public void TreemapLabelsFollowTheShowLabelSwitch()
    {
        var (mark, canvas, ctx) = TreemapCtx(MarkCases.Simple());
        mark.Render(ctx);

        AssertThat(canvas.Texts).Contains("A");
        AssertThat(canvas.Texts).Contains("B");
        AssertThat(canvas.Texts).Contains("C");

        var (quiet, quietCanvas, quietCtx) = TreemapCtx(MarkCases.Simple());
        quiet.ShowLabel = false;
        quiet.Render(quietCtx);

        AssertThat(quietCanvas.Texts.Count).IsEqual(0);
        AssertThat(quietCanvas.Rects.Count).IsEqual(3);   // cells are still drawn
    }

    [TestCase]
    public void TreemapLabelsFollowTheLabelFormat()
    {
        var (mark, canvas, ctx) = TreemapCtx(MarkCases.Simple());
        mark.LabelFormat = "{0} ({1})";
        mark.Render(ctx);

        // {0} is the cell label, {1} its value.
        AssertThat(canvas.Texts).Contains("A (10)");
        AssertThat(canvas.Texts).Contains("C (15)");
    }

    /// <summary>
    /// A group node that carries a value of its own is sized by that value: its children are details inside
    /// it, not an addition to it. Adding both counted the subtree twice, so a group declaring 30 with
    /// children of 20+10 outbid its 40-valued sibling and the cell areas stopped matching the labels
    /// (<c>SunburstMark.PropagateValues</c> states the same rule for the rings).
    /// </summary>
    [TestCase]
    public void AGroupWithItsOwnValueIsNotCountedTwice()
    {
        var rows = new List<DataRow>
        {
            D(("label", "root"), ("parent", ""), ("value", 0.0)),
            D(("label", "A"), ("parent", "root"), ("value", 30.0)),   // a group that also carries a value
            D(("label", "A1"), ("parent", "A"), ("value", 20.0)),
            D(("label", "A2"), ("parent", "A"), ("value", 10.0)),
            D(("label", "B"), ("parent", "root"), ("value", 40.0)),
        };
        var canvas = new FakeCanvas2D();
        var chart = TreemapChart(canvas, rows, new TreemapMark { CellGap = 0f }, width: 600f, height: 400f);
        chart.Render();

        // Every node gets a cell, the root's one being the whole plot: the largest cell after it belongs to
        // the sibling with the larger declared value (40 > 30). Its label carries the path ("root / B: 40").
        var sibling = canvas.Rects.OrderByDescending(r => (double)r.W * r.H).Skip(1).First();
        var hit = chart.HitTest(new Vector2(sibling.X + sibling.W * 0.5f, sibling.Y + sibling.H * 0.5f));

        AssertThat(hit is not null).IsTrue();
        var cell = hit!;
        AssertThat(cell.Hit).IsTrue();
        string cellLabel = cell.Label ?? "";
        AssertThat(cellLabel.Contains("B: 40")).IsTrue();
        AssertThat(cellLabel.Contains('A')).IsFalse();
    }

    [TestCase]
    public void TreemapBinarySplitCellsFillThePlotArea()
    {
        var rows = new List<DataRow>
        {
            D(("cat", "A"), ("value", 50.0)),
            D(("cat", "B"), ("value", 30.0)),
            D(("cat", "C"), ("value", 20.0)),
        };
        var (mark, canvas, ctx) = TreemapCtx(rows);
        mark.CellGap = 0f;   // no gap => cell areas are exactly proportional to the values
        mark.Render(ctx);

        AssertThat(canvas.Rects.Count).IsEqual(3);
        double area = canvas.Rects.Sum(r => (double)r.W * r.H);
        AssertThat(Approx(area, 400 * 300, 1.0)).IsTrue();
        AssertThat(canvas.NegativeSizeRectCount).IsEqual(0);
    }

    [TestCase]
    public void TreemapEqualValuesSplitTheAreaEvenly()
    {
        var rows = new List<DataRow>
        {
            D(("cat", "A"), ("value", 10.0)),
            D(("cat", "B"), ("value", 10.0)),
        };
        var (mark, canvas, ctx) = TreemapCtx(rows);
        mark.CellGap = 0f;
        mark.Render(ctx);

        AssertThat(canvas.Rects.Count).IsEqual(2);
        AssertThat(Approx(canvas.Rects[0].W, canvas.Rects[1].W)).IsTrue();
        AssertThat(Approx(canvas.Rects[0].H, canvas.Rects[1].H)).IsTrue();
        AssertThat(Approx(canvas.Rects[0].W, 200)).IsTrue();
    }

    [TestCase]
    public void TreemapStyleOverrideAndHoverHighlightReachTheCanvas()
    {
        var rows = new List<DataRow> { D(("cat", "A"), ("value", 10.0)) };

        var (plain, plainCanvas, plainCtx) = TreemapCtx(rows);
        plain.Render(plainCtx);
        var baseColor = plainCanvas.FillColors[0];

        var (overridden, overrideCanvas, overrideCtx) = TreemapCtx(rows);
        overridden.StyleOverride = (_, _, style) => style.WithFill(Colors.Red);
        overridden.Render(overrideCtx);
        AssertThat(overrideCanvas.FillColors[0].ToHtml()).IsEqual(Colors.Red.ToHtml());

        var (hovered, hoverCanvas, hoverCtx) = TreemapCtx(rows, hovered: 0);
        hovered.Render(hoverCtx);
        AssertThat(hoverCanvas.FillColors[0].R > baseColor.R).IsTrue();
    }

    [TestCase]
    public void FlatCellsTakeOnePaletteColorEach()
    {
        var rows = new List<DataRow>
        {
            D(("cat", "A"), ("value", 10.0)),
            D(("cat", "B"), ("value", 20.0)),
            D(("cat", "C"), ("value", 30.0)),
        };
        var (mark, canvas, ctx) = TreemapCtx(rows);
        mark.CellGap = 0f;
        mark.Render(ctx);

        // Without a hierarchy every cell is a top-level category of its own, so it takes a palette
        // colour (picked by row index) instead of the single mark default colour. The cells are painted
        // largest first, so the order in the recorded fills is not the row order - compare as a set.
        var palette = ChartTheme.DefaultPalette;
        static string Sorted(IEnumerable<string> values)
            => string.Join(",", values.OrderBy(v => v, StringComparer.Ordinal));

        var cellFills = canvas.FillColors
            .Where(c => palette.Any(p => p.ToHtml() == c.ToHtml()))
            .Select(c => c.ToHtml());
        var expected = palette.Take(3).Select(c => c.ToHtml());

        AssertThat(Sorted(cellFills)).IsEqual(Sorted(expected));
    }

    // ── Options ─────────────────────────────────────────────────────────────

    [TestCase]
    public void TreemapCellGapShrinksASingleCell()
    {
        var rows = new List<DataRow> { D(("cat", "A"), ("value", 10.0)) };
        var plot = new PlotArea(0, 0, 100, 100);

        var (noGap, noGapCanvas, noGapCtx) = TreemapCtx(rows, plot);
        noGap.CellGap = 0f;
        noGap.Render(noGapCtx);

        var (withGap, gapCanvas, gapCtx) = TreemapCtx(rows, plot);
        withGap.CellGap = 20f;
        withGap.Render(gapCtx);

        AssertThat(noGapCanvas.Rects.Count).IsEqual(1);
        AssertThat(Approx(noGapCanvas.Rects[0].W, 100)).IsTrue();
        AssertThat(Approx(gapCanvas.Rects[0].W, 80)).IsTrue();
        AssertThat(Approx(noGapCanvas.Rects[0].W - gapCanvas.Rects[0].W, 20)).IsTrue();
    }

    // ── Layout modes ────────────────────────────────────────────────────────

    [TestCase]
    public void SquarifiedLayoutImprovesWorstAspectRatio()
    {
        var values = new List<DataRow>();
        foreach (var v in new[] { 6.0, 6.0, 4.0, 3.0, 2.0, 2.0, 2.0 })
            values.Add(D(("label", $"L{values.Count}"), ("value", v)));

        float WorstAspect(TreemapLayoutMode mode)
        {
            var canvas = new FakeCanvas2D();
            var chart = TreemapChart(canvas, values,
                new TreemapMark { LayoutMode = mode, CellGap = 0f }, width: 600f, height: 400f);
            chart.Render();

            var rects = canvas.Rects.Where(r => r is { W: > 1f, H: > 1f, W: < 500f }).ToList();
            AssertThat(rects.Count > 0).IsTrue();
            return rects.Max(r => MathF.Max(r.W / r.H, r.H / r.W));
        }

        float binary = WorstAspect(TreemapLayoutMode.BinarySplit);
        float squarified = WorstAspect(TreemapLayoutMode.Squarify);

        AssertThat(squarified < binary).IsTrue();
    }

    [TestCase]
    public void SquarifiedLayoutCoversTheSameArea()
    {
        var canvas = new FakeCanvas2D();
        var chart = TreemapChart(canvas,
        [
            D(("label", "A"), ("value", 50.0)),
            D(("label", "B"), ("value", 30.0)),
            D(("label", "C"), ("value", 20.0)),
        ], new TreemapMark { LayoutMode = TreemapLayoutMode.Squarify, CellGap = 0f });
        chart.Render();

        var cells = canvas.Rects.Where(r => r is { W: > 1f, H: > 1f, W: < 400f, H: < 300f }).ToList();
        AssertThat(cells.Count).IsEqual(3);
        // The cells are proportional to their values (50/30/20 of the plot area).
        double total = cells.Sum(c => (double)c.W * c.H);
        var largest = cells.OrderByDescending(c => (double)c.W * c.H).First();
        double share = (double)largest.W * largest.H / total;
        AssertThat(Math.Abs(share - 0.5) < 0.05).IsTrue();
    }

    [TestCase]
    public void BinarySplitLayoutRemainsTheDefault()
    {
        AssertThat(new TreemapMark().LayoutMode).IsEqual(TreemapLayoutMode.BinarySplit);
    }

    /// <summary>
    /// A dominant value next to a tiny one in the layout <see cref="ChartView"/> builds for a treemap
    /// (squarify, see ChartView.CreateMark): once the remaining sliver cannot hold another row the
    /// layout must still emit a rectangle for the leftover row - a degenerate (x, y, 0, 0) cell - so the
    /// cell count stays equal to the data rows and no negative/non-finite size is produced.
    /// </summary>
    [TestCase]
    public void SquarifiedLayoutKeepsOneCellPerRowForADominantValue()
    {
        var rows = new List<DataRow>
        {
            D(("cat", "dominant"), ("value", 1000.0)),
            D(("cat", "tiny"), ("value", 1.0)),
        };
        var (mark, canvas, ctx) = TreemapCtx(rows, new PlotArea(0, 0, 400, 300));
        mark.LayoutMode = TreemapLayoutMode.Squarify;
        mark.Render(ctx);

        AssertThat(canvas.Rects.Count).IsEqual(2);                 // one cell per row, filler included
        AssertThat(canvas.FillCount).IsEqual(2);
        AssertThat(canvas.Rects.Count(r => r.W <= 0f || r.H <= 0f)).IsEqual(1);   // the degenerate filler
        AssertThat(canvas.NegativeSizeRectCount).IsEqual(0);
        AssertThat(canvas.NonFiniteCoordinateCount).IsEqual(0);
    }

    // ── Degenerate data ─────────────────────────────────────────────────────

    [TestCase]
    public void TreemapSkipsNonPositiveValues()
    {
        var rows = new List<DataRow>
        {
            D(("cat", "A"), ("value", 0.0)),
            D(("cat", "B"), ("value", -5.0)),
        };
        var (mark, canvas, ctx) = TreemapCtx(rows);
        mark.Render(ctx);

        AssertThat(canvas.Rects.Count).IsEqual(0);
        AssertThat(canvas.DrewAnything).IsFalse();
    }

    [TestCase]
    public void TreemapWithTinyCellsAndALargeGapKeepsPositiveSizes()
    {
        var rows = new List<DataRow>();
        for (int i = 0; i < 30; i++)
            rows.Add(D(("label", $"L{i}"), ("value", 1.0)));
        rows.Add(D(("label", "big"), ("value", 500.0)));

        var canvas = new FakeCanvas2D();
        TreemapChart(canvas, rows, new TreemapMark { CellGap = 10f }).Render();

        AssertThat(canvas.NegativeSizeRectCount).IsEqual(0);
    }

    // ── Hit testing ─────────────────────────────────────────────────────────

    /// <summary>
    /// The hit area follows the entry animation, as it does in every other mark: a cell grows out of its centre,
    /// so the part of the target rectangle that has not been painted yet is not a hit. The final rectangle used
    /// to answer regardless, which let the pointer describe a cell that was still invisible (see
    /// <see cref="WaffleMark"/>, which has always scaled its hit area).
    /// </summary>
    [TestCase]
    public void TreemapHitTestFollowsTheEntryAnimation()
    {
        var rows = new List<DataRow> { D(("cat", "A"), ("value", 10.0)) };
        var (mark, canvas, fullCtx) = TreemapCtx(rows);
        mark.Render(fullCtx);

        // A corner of the finished cell: inside the cell it ends up as, outside the half-size one it is drawn
        // as halfway through the entry.
        var cell = canvas.Rects[0];
        var nearCorner = new Vector2(cell.X + 2f, cell.Y + 2f);
        AssertThat(mark.HitTest(fullCtx, nearCorner)).IsNotNull();

        var growingCtx = TestContexts.Mark(canvas, rows, TestContexts.XyEncodes("cat", "value"),
            TestContexts.CategoryScales(ThreeCategories),
            animation: new AnimationContext { EntryProgress = 0.5f });
        AssertThat(mark.HitTest(growingCtx, nearCorner)).IsNull();
    }

    [TestCase]
    public void TreemapHitTestReturnsTheCellCenterAndLabel()
    {
        var rows = new List<DataRow> { D(("cat", "A"), ("value", 10.0)) };
        var (mark, canvas, ctx) = TreemapCtx(rows);
        mark.Render(ctx);

        var cell = canvas.Rects[0];
        var hit = mark.HitTest(ctx, new Vector2(cell.X + cell.W / 2f, cell.Y + cell.H / 2f));

        AssertThat(hit is not null).IsTrue();
        AssertThat(Approx(hit!.ScreenX, cell.X + cell.W / 2f)).IsTrue();
        AssertThat(Approx(hit.ScreenY, cell.Y + cell.H / 2f)).IsTrue();
        AssertThat(hit.Label).IsEqual("A: 10");
        AssertThat(hit.MarkType).IsEqual(nameof(TreemapMark));
    }

    // ── Layout cache ────────────────────────────────────────────────────────

    [TestCase]
    public void TreemapInvalidateCacheRebuildsTheLayout()
    {
        var rows = new List<DataRow> { D(("cat", "A"), ("value", 10.0)) };
        var (mark, _, ctx) = TreemapCtx(rows);

        mark.Render(ctx);
        mark.Render(ctx);
        AssertThat(mark.LayoutBuildCount).IsEqual(1);   // the layout is cached between frames

        mark.InvalidateCache();
        mark.Render(ctx);
        AssertThat(mark.LayoutBuildCount).IsEqual(2);
    }

    [TestCase]
    public void TreemapLayoutIsNotRebuiltOnEveryFrame()
    {
        var canvas = new FakeCanvas2D();
        var mark = new TreemapMark();
        var chart = TreemapChart(canvas, Leaves(), mark);

        chart.Render();
        chart.Render();

        AssertThat(mark.LayoutBuildCount).IsEqual(1);
    }

    [TestCase]
    public void LayoutIsRebuiltWhenThePlotIsResized()
    {
        var canvas = new FakeCanvas2D();
        var mark = new TreemapMark();
        var chart = TreemapChart(canvas, Leaves(), mark);
        chart.Render();

        chart.Width = 700f;
        chart.Render();

        AssertThat(mark.LayoutBuildCount).IsEqual(2);
    }

    // ── shared data helper ──────────────────────────────────────────────────

    private static List<DataRow> Leaves() =>
    [
        D(("label", "A"), ("value", 30.0)),
        D(("label", "B"), ("value", 20.0)),
        D(("label", "C"), ("value", 10.0)),
    ];
    // ── Hierarchy (ParentField) ─────────────────────────────────────────────

    /// <summary>
    /// Rows with a parent reference: root holds two groups, one of them (media) holds two leaves.
    /// The values stay distinct so the draw order — biggest child first — is deterministic.
    /// </summary>
    private static List<DataRow> Tree() =>
    [
        D(("label", "root"), ("parent", ""), ("value", 0.0)),
        D(("label", "media"), ("parent", "root"), ("value", 0.0)),
        D(("label", "video"), ("parent", "media"), ("value", 30.0)),
        D(("label", "music"), ("parent", "media"), ("value", 20.0)),
        D(("label", "code"), ("parent", "root"), ("value", 70.0)),
    ];

    private static (TreemapMark Mark, FakeCanvas2D Canvas, MarkContext Ctx) TreeCtx(List<DataRow> rows)
    {
        var canvas = new FakeCanvas2D();
        var ctx = TestContexts.Mark(canvas, rows,
            TestContexts.XyEncodes("label", "value"),
            TestContexts.CategoryScales(TreeCategories));
        return (new TreemapMark(), canvas, ctx);
    }

    [TestCase]
    public void ParentRowsNestInsteadOfTilingFlat()
    {
        var (mark, canvas, ctx) = TreeCtx(Tree());
        mark.Render(ctx);

        // One cell per row, drawn parent first: root, code, media, video, music.
        AssertThat(canvas.Rects.Count).IsEqual(5);
        var root = canvas.Rects[0];
        var code = canvas.Rects[1];
        var media = canvas.Rects[2];

        // The only top level row takes the plot area, inset by half a cell gap.
        AssertApprox(root.X, ctx.Plot.X + mark.CellGap / 2f, 0.5);
        AssertApprox(root.Y, ctx.Plot.Y + mark.CellGap / 2f, 0.5);
        AssertApprox(root.W, ctx.Plot.Width - mark.CellGap, 0.5);
        AssertApprox(root.H, ctx.Plot.Height - mark.CellGap, 0.5);

        // Nesting instead of tiling: the children stay inside the root, below the header strip, and
        // split its area in proportion to their subtrees (code 70 vs media 50).
        AssertApprox(code.Y, root.Y + mark.GroupHeaderHeight + mark.CellGap / 2f, 0.5);
        AssertApprox(code.H, media.H, 0.5);
        AssertApprox(code.W / media.W, 70f / 50f, 0.02);
        AssertApprox(media.X - (code.X + code.W), mark.CellGap, 0.5);
        AssertApprox(media.X + media.W, root.X + root.W - mark.CellGap / 2f, 0.5);
    }

    [TestCase]
    public void AGroupIsSizedByItsChildren()
    {
        // media holds video 30 + music 20 = 50, code is a leaf of 70: the root splits 70 : 50.
        var (mark, canvas, ctx) = TreeCtx(Tree());
        mark.Render(ctx);

        var root = canvas.Rects[0];
        var code = canvas.Rects[1];
        var media = canvas.Rects[2];

        // Only the top level row owns the whole plot: the two groups inside it cover less than root.
        AssertThat(code.W * code.H + media.W * media.H < root.W * root.H).IsTrue();

        // ...and each of them gets its share of what is left after the header strip.
        double innerArea = (root.W - mark.CellGap) * (root.H - mark.GroupHeaderHeight - mark.CellGap);
        AssertApprox((double)code.W * code.H / innerArea, 70.0 / 120.0, 0.02);
        AssertApprox((double)media.W * media.H / innerArea, 50.0 / 120.0, 0.02);
    }

    [TestCase]
    public void ChildrenSplitTheAreaTheirGroupLeaves()
    {
        var (mark, canvas, ctx) = TreeCtx(Tree());
        mark.Render(ctx);

        var media = canvas.Rects[2];
        var video = canvas.Rects[3];
        var music = canvas.Rects[4];

        // The leaves start below the strip reserved for the group label...
        AssertApprox(video.Y, media.Y + mark.GroupHeaderHeight + mark.CellGap / 2f, 0.5);

        // ...and share what is left in proportion to their values: video 30 vs music 20.
        AssertApprox(video.H / music.H, 30f / 20f, 0.02);
        AssertApprox(video.W, music.W, 0.5);
        AssertThat(video.Y + video.H <= media.Y + media.H + 0.5f).IsTrue();
    }

    [TestCase]
    public void SquarifyModeNestsTheSameHierarchy()
    {
        var (mark, canvas, ctx) = TreeCtx(Tree());
        mark.LayoutMode = TreemapLayoutMode.Squarify;
        mark.Render(ctx);

        // Both layout modes go through the same recursion, so the nesting rules hold for either.
        AssertThat(canvas.Rects.Count).IsEqual(5);
        var root = canvas.Rects[0];
        foreach (var rect in canvas.Rects)
        {
            AssertThat(rect.X >= root.X - 0.5f).IsTrue();
            AssertThat(rect.Y >= root.Y - 0.5f).IsTrue();
            AssertThat(rect.X + rect.W <= root.X + root.W + 0.5f).IsTrue();
            AssertThat(rect.Y + rect.H <= root.Y + root.H + 0.5f).IsTrue();
        }
        AssertThat(canvas.NegativeSizeRectCount).IsEqual(0);
    }

    [TestCase]
    public void ParentFieldNameCanBeRemapped()
    {
        var rows = new List<DataRow>
        {
            D(("label", "group"), ("up", ""), ("value", 0.0)),
            D(("label", "leaf"), ("up", "group"), ("value", 40.0)),
        };
        var canvas = new FakeCanvas2D();
        var ctx = TestContexts.Mark(canvas, rows, TestContexts.XyEncodes("label", "value"),
            TestContexts.CategoryScales(FlatCategories));
        var mark = new TreemapMark { ParentField = "up" };
        mark.Render(ctx);

        // The default "parent" field is missing from the rows, so only ParentField turns this into a tree.
        AssertThat(canvas.Rects.Count).IsEqual(2);
        var group = canvas.Rects[0];
        var leaf = canvas.Rects[1];
        AssertApprox(leaf.Y, group.Y + mark.GroupHeaderHeight + mark.CellGap / 2f, 0.5);
        AssertThat(leaf.W <= group.W - mark.CellGap + 0.5f).IsTrue();
    }

    [TestCase]
    public void AGroupHeaderStripIsReservedWhenShowLabelIsOn()
    {
        var (mark, canvas, ctx) = TreeCtx(Tree());
        mark.Render(ctx);

        // The group name is drawn as a left aligned label inside its header strip.
        var header = canvas.TextDraws.FirstOrDefault(d => d.Align == TextAlign.Left);
        AssertThat(header.Text.Length > 0).IsTrue();
        AssertThat(header.Y <= ctx.Plot.Y + mark.GroupHeaderHeight).IsTrue();
    }

    [TestCase]
    public void SiblingsShareAFamilyColourAndShadeApart()
    {
        // Two top-level branches with two leaves each: one palette colour per branch, and inside a
        // branch every further sibling is that same colour one SiblingShadeStep darker - the hue has to
        // survive the shading, which is what makes the leaves read as one family.
        var rows = new List<DataRow>
        {
            D(("label", "g1"), ("parent", ""), ("value", 0.0)),
            D(("label", "g2"), ("parent", ""), ("value", 0.0)),
            D(("label", "a1"), ("parent", "g1"), ("value", 30.0)),
            D(("label", "a2"), ("parent", "g1"), ("value", 10.0)),
            D(("label", "b1"), ("parent", "g2"), ("value", 20.0)),
            D(("label", "b2"), ("parent", "g2"), ("value", 5.0)),
        };
        var (mark, canvas, ctx) = TreeCtx(rows);
        mark.SiblingShadeStep = 0.2f;
        mark.Render(ctx);

        var fills = canvas.FillColors;
        AssertThat(fills.Count).IsEqual(6);   // two groups plus their two leaves

        // A family shares its hue (the shade is a per-channel scaling, so the hue is preserved), which
        // groups the six fills by branch - two step-1 branches, never one base colour per cell.
        var families = fills.GroupBy(c => MathF.Round(c.H, 2)).ToList();
        AssertThat(families.Count).IsEqual(2);

        foreach (var family in families)
        {
            var shades = family.Select(c => c.V).Distinct().OrderByDescending(v => v).ToList();
            AssertThat(shades.Count).IsEqual(2);                          // the family shade step really ran
            AssertApprox(shades[1], shades[0] * 0.8, 1e-4);               // 1 - SiblingShadeStep * 1
        }

        // ...and one palette colour per branch: the family colours are the first two palette entries.
        var palette = ChartTheme.DefaultPalette;
        var familyBases = families.Select(f => f.MaxBy(c => c.V)!.ToHtml())
                                  .OrderBy(h => h, StringComparer.Ordinal);
        var expectedBases = palette.Take(2).Select(c => c.ToHtml())
                                  .OrderBy(h => h, StringComparer.Ordinal);

        AssertThat(string.Join(",", familyBases)).IsEqual(string.Join(",", expectedBases));
    }

    [TestCase]
    public void UnknownOrSelfParentsStayVisible()
    {
        var rows = new List<DataRow>
        {
            D(("label", "orphan"), ("parent", "nope"), ("value", 30.0)),
            D(("label", "self"), ("parent", "self"), ("value", 20.0)),
            D(("label", "cycleA"), ("parent", "cycleB"), ("value", 10.0)),
            D(("label", "cycleB"), ("parent", "cycleA"), ("value", 15.0)),
        };
        var (mark, canvas, ctx) = TreeCtx(rows);
        mark.Render(ctx);

        // Every row is still drawn, and nothing escaped the plot.
        AssertThat(canvas.Rects.Count).IsEqual(4);
        AssertThat(canvas.NonFiniteCoordinateCount).IsEqual(0);
        AssertThat(canvas.NegativeSizeRectCount).IsEqual(0);
    }

    [TestCase]
    public void HitTestReportsThePathThroughTheTree()
    {
        var (mark, canvas, ctx) = TreeCtx(Tree());
        mark.Render(ctx);

        // The centre of the video cell: the deepest cell wins over the groups it sits in.
        var video = canvas.Rects[3];
        var hit = mark.HitTest(ctx, new Vector2(video.X + video.W / 2f, video.Y + video.H / 2f));

        AssertThat(hit is not null).IsTrue();
        AssertThat(hit!.Label!.StartsWith("root / media / video", StringComparison.Ordinal)).IsTrue();
        AssertThat(hit.RowIndex).IsEqual(2);   // row 2 of Tree() is video
    }

    [TestCase]
    public void FlatRowsAreStillOneLevel()
    {
        var (mark, canvas, ctx) = TreemapCtx(MarkCases.Simple());
        mark.Render(ctx);

        // No parent field values: the classic one-level treemap keeps its three cells.
        AssertThat(canvas.Rects.Count).IsEqual(3);
    }

    [TestCase]
    public void CellGapAndCornerRadiusStillApplyToNestedCells()
    {
        var (mark, canvas, ctx) = TreeCtx(Tree());
        mark.CellGap = 4f;
        mark.CornerRadius = 2f;
        mark.Render(ctx);

        AssertThat(canvas.NegativeSizeRectCount).IsEqual(0);
        AssertThat(canvas.RoundRectCount).IsEqual(5);
    }
}
