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
/// Behaviour specification for <see cref="SankeyMark"/>: the node boxes and flow paths it draws,
/// the column layout controlled by <see cref="SankeyMark.NodeWidth"/>, <see cref="SankeyMark.NodeGap"/>
/// and <see cref="SankeyMark.ColumnGap"/>, its labels, hit testing, degenerate data and the layout cache.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class SankeyMarkTest
{
    private static readonly string[] ThreeCategories = { "A", "B", "C" };

    // ── helpers ─────────────────────────────────────────────────────────────

    /// <summary>Local floating point comparison: mark geometry is never bit-exact.</summary>
    private static bool Approx(double a, double b, double tol = 0.01) => Math.Abs(a - b) <= tol;

    private static DataRow D(params (string Field, object? Value)[] fields) => TestContexts.Row(fields);

    private static (SankeyMark Mark, FakeCanvas2D Canvas, MarkContext Ctx) SankeyCtx(List<DataRow> rows)
    {
        var canvas = new FakeCanvas2D();
        var ctx = TestContexts.Mark(canvas, rows, TestContexts.XyEncodes("cat", "value"),
            TestContexts.CategoryScales(ThreeCategories));
        return (new SankeyMark(), canvas, ctx);
    }

    private static Chart SankeyChart(FakeCanvas2D canvas, List<DataRow> rows, Mark mark,
                                     float width = 400f, float height = 300f)
    {
        var chart = new Chart(canvas) { Width = width, Height = height };
        chart.Data(rows);
        chart.Mark(mark);
        chart.Encode(Channel.X, "source");
        chart.Encode(Channel.Y, "value");
        return chart;
    }

    // ── Default rendering ───────────────────────────────────────────────────

    [TestCase]
    public void FlowsTakeTheSourceNodeColor()
    {
        var rows = new List<DataRow>
        {
            D(("source", "A"), ("target", "C"), ("value", 10.0)),
            D(("source", "A"), ("target", "D"), ("value", 10.0)),
            D(("source", "B"), ("target", "C"), ("value", 10.0)),
        };
        var (mark, canvas, ctx) = SankeyCtx(rows);
        mark.Render(ctx);

        // Flows are filled before the node bars, in data order, and each flow carries the colour of its
        // source node: the two flows leaving A share a colour, the flow leaving B has its own.
        var palette = ChartTheme.DefaultPalette;

        AssertThat(canvas.FillColors.Count >= 3).IsTrue();
        AssertThat(palette.Any(p => p.ToHtml() == canvas.FillColors[0].ToHtml())).IsTrue();
        AssertThat(canvas.FillColors[0].ToHtml()).IsEqual(canvas.FillColors[1].ToHtml());
        AssertThat(canvas.FillColors[2].ToHtml()).IsNotEqual(canvas.FillColors[0].ToHtml());
    }

    [TestCase]
    public void SankeyNodeWidthReachesTheNodeRects()
    {
        var (mark, canvas, ctx) = SankeyCtx(MarkCases.Relations());
        mark.NodeWidth = 30f;
        mark.Render(ctx);

        AssertThat(canvas.Rects.Count).IsEqual(3);   // one rect per node, no other rect
        AssertThat(canvas.Rects.All(r => Approx(r.W, 30))).IsTrue();
    }

    [TestCase]
    public void SankeyLabelsAreCentredBesideTheirNode()
    {
        var (mark, canvas, ctx) = SankeyCtx(MarkCases.Relations());
        mark.Render(ctx);

        // Every label goes through DrawTextCentered (the shared helper), so none of them is left- or
        // right-aligned any more. The column rule still holds through the anchor: the last column's
        // label hangs to the LEFT of its node, the earlier columns sit to the RIGHT of theirs.
        AssertThat(canvas.Texts).Contains("A");
        AssertThat(canvas.Texts).Contains("C");
        AssertThat(canvas.TextDraws.All(d => d.Align == TextAlign.Center)).IsTrue();

        // Relations: A -> B -> C, so C is the only node in the last column (the largest X) and A the
        // only one in the first (the smallest X).
        float lastColumnX = canvas.Rects.Max(r => r.X);
        float firstColumnX = canvas.Rects.Min(r => r.X);
        var cLabel = canvas.TextDraws.Single(d => d.Text == "C");
        var aLabel = canvas.TextDraws.Single(d => d.Text == "A");

        AssertThat(cLabel.X < lastColumnX).IsTrue();
        AssertThat(aLabel.X > firstColumnX + mark.NodeWidth).IsTrue();

        var (quiet, quietCanvas, quietCtx) = SankeyCtx(MarkCases.Relations());
        quiet.ShowLabel = false;
        quiet.Render(quietCtx);
        AssertThat(quietCanvas.Texts.Count).IsEqual(0);
    }

    [TestCase]
    public void SankeyNodeLabelsFollowTheLabelFormat()
    {
        // Relations: A→B 5, A→C 2, B→C 3, so the node totals are A 7, B 5 and C 5.
        var (mark, canvas, ctx) = SankeyCtx(MarkCases.Relations());
        mark.LabelFormat = "{0} ({1})";
        mark.Render(ctx);

        AssertThat(canvas.Texts).Contains("A (7)");
        AssertThat(canvas.Texts).Contains("C (5)");

        // The default format keeps the bare node name.
        var (plain, plainCanvas, plainCtx) = SankeyCtx(MarkCases.Relations());
        plain.Render(plainCtx);
        AssertThat(plainCanvas.Texts).Contains("A");
        AssertThat(plainCanvas.Texts.Contains("A (5)")).IsFalse();
    }

    // ── Selection ───────────────────────────────────────────────────────────

    [TestCase]
    public void TheSelectedFlowGetsTheSelectionRing()
    {
        var rows = MarkCases.Relations();
        var canvas = new FakeCanvas2D();
        var ctx = TestContexts.Mark(canvas, rows, TestContexts.XyEncodes("cat", "value"),
            TestContexts.CategoryScales(ThreeCategories), selectedRowIndex: 0);

        var mark = new SankeyMark();
        mark.States.SelectedStroke = new Color(1f, 0f, 1f);
        mark.States.SelectedStrokeWidth = 4f;
        mark.Render(ctx);

        // Flows and nodes are filled, never stroked, so the single stroke is the selection ring.
        AssertThat(canvas.StrokeCount).IsEqual(1);
        AssertThat(canvas.StrokeColors[0]).IsEqual(new Color(1f, 0f, 1f));
        AssertThat(canvas.StrokeWidths[0]).IsEqual(4f);

        var plainCanvas = new FakeCanvas2D();
        var plainCtx = TestContexts.Mark(plainCanvas, rows, TestContexts.XyEncodes("cat", "value"),
            TestContexts.CategoryScales(ThreeCategories));
        new SankeyMark().Render(plainCtx);
        AssertThat(plainCanvas.StrokeCount).IsEqual(0);
    }

    // ── Column layout ───────────────────────────────────────────────────────

    [TestCase]
    public void SankeyChainPlacesNodesInIncreasingColumns()
    {
        var (mark, canvas, ctx) = SankeyCtx(MarkCases.Relations());
        mark.Render(ctx);

        var xs = canvas.Rects.Select(r => r.X).Distinct().OrderBy(x => x).ToList();
        AssertThat(xs.Count).IsEqual(3);            // A -> B -> C occupy three columns
        AssertThat(xs[1] > xs[0] && xs[2] > xs[1]).IsTrue();
        AssertThat(xs[0] > 0).IsTrue();             // the default ColumnGap pulls the first column right
        // The last column is always flush with the right edge of the plot.
        AssertThat(Approx(canvas.Rects.Max(r => r.X + r.W), 400)).IsTrue();
    }

    [TestCase]
    public void SankeyColumnGapShrinksTheColumnPitch()
    {
        (float FirstX, float LastRight) Columns(float columnGap)
        {
            var (mark, canvas, ctx) = SankeyCtx(MarkCases.Relations());
            mark.ColumnGap = columnGap;
            mark.Render(ctx);
            return (canvas.Rects.Min(r => r.X), canvas.Rects.Max(r => r.X + r.W));
        }

        // Three columns: the plot is 400 wide and the node bars are 16 wide, so the even spread of
        // ColumnGap = 0 puts the first column at the left edge.
        var spread = Columns(0f);
        var packed = Columns(0.9f);

        AssertThat(Approx(spread.FirstX, 0)).IsTrue();
        AssertThat(packed.FirstX > spread.FirstX).IsTrue();
        AssertThat(Approx(packed.FirstX, 384f - 2f * (192f + 0.9f * (16f - 192f)))).IsTrue();

        // Both values keep the same anchor: the last column ends on the plot's right edge.
        AssertThat(Approx(spread.LastRight, 400)).IsTrue();
        AssertThat(Approx(packed.LastRight, 400)).IsTrue();
    }

    [TestCase]
    public void SankeyNodeGapSeparatesNodesOfTheSameColumn()
    {
        // A and B are both sources: they share the left-most column and are stacked with NodeGap
        // in between.
        var rows = new List<DataRow>
        {
            D(("source", "A"), ("target", "C"), ("value", 10.0)),
            D(("source", "B"), ("target", "C"), ("value", 20.0)),
        };

        float GapBetweenLeftColumnNodes(float nodeGap)
        {
            var (mark, canvas, ctx) = SankeyCtx(rows);
            mark.NodeGap = nodeGap;
            mark.Render(ctx);

            float columnX = canvas.Rects.Min(r => r.X);
            var column = canvas.Rects.Where(r => Approx(r.X, columnX)).OrderBy(r => r.Y).ToList();
            AssertThat(column.Count).IsEqual(2);
            return column[1].Y - (column[0].Y + column[0].H);
        }

        AssertThat(Approx(GapBetweenLeftColumnNodes(8f), 8)).IsTrue();
        AssertThat(Approx(GapBetweenLeftColumnNodes(40f), 40)).IsTrue();
    }

    // ── Degenerate data ─────────────────────────────────────────────────────

    [TestCase]
    public void SankeySkipsSelfLoopsAndEmptyData()
    {
        var loop = new List<DataRow> { D(("source", "A"), ("target", "A"), ("value", 5.0)) };
        var (mark, canvas, ctx) = SankeyCtx(loop);
        mark.Render(ctx);
        AssertThat(canvas.DrewAnything).IsFalse();

        var (empty, emptyCanvas, emptyCtx) = SankeyCtx([]);
        empty.Render(emptyCtx);
        AssertThat(emptyCanvas.DrewAnything).IsFalse();
    }

    [TestCase]
    public void SankeyAssignsDistinctColumnsWhenTheRelationsFormACycle()
    {
        var rows = new List<DataRow>
        {
            D(("source", "A"), ("target", "B"), ("value", 5.0)),
            D(("source", "B"), ("target", "A"), ("value", 3.0)),
        };
        var (mark, canvas, ctx) = SankeyCtx(rows);
        mark.Render(ctx);

        // The relations cannot be ordered topologically, but the cycle is walked instead of collapsing
        // every node onto column 0 (which used to stack all of them on top of each other at the left
        // edge): the layout stays finite and each node gets a column of its own.
        AssertThat(canvas.DrewAnything).IsTrue();
        AssertThat(canvas.NonFiniteCoordinateCount).IsEqual(0);
        AssertThat(canvas.NegativeSizeRectCount).IsEqual(0);
        AssertThat(canvas.Rects.Count).IsEqual(2);
        AssertThat(canvas.Rects.Select(r => r.X).Distinct().Count()).IsEqual(2);
    }

    [TestCase]
    public void SankeyNodesKeepAPositiveHeightInAShortPlot()
    {
        var rows = new List<DataRow>();
        for (int i = 0; i < 12; i++)
        {
            rows.Add(D(("source", $"S{i}"), ("target", "T"), ("value", 1.0 + i)));
        }

        var canvas = new FakeCanvas2D();
        SankeyChart(canvas, rows, new SankeyMark(), height: 90f).Render();

        AssertThat(canvas.NegativeSizeRectCount).IsEqual(0);
        AssertThat(canvas.NonFiniteCoordinateCount).IsEqual(0);
    }

    // ── Hit testing ─────────────────────────────────────────────────────────

    [TestCase]
    public void SankeyNodeHitReturnsRowIndexMinusOneAndTheNodeName()
    {
        var (mark, canvas, ctx) = SankeyCtx(MarkCases.Relations());
        mark.Render(ctx);

        // Node A is alone in the left-most column; its centre is left of every flow, so no flow
        // claims it first.
        float columnX = canvas.Rects.Min(r => r.X);
        var node = canvas.Rects.First(r => Approx(r.X, columnX));
        var hit = mark.HitTest(ctx, new Vector2(node.X + node.W / 2f, node.Y + node.H / 2f));

        AssertThat(hit is not null).IsTrue();
        AssertThat(hit!.RowIndex).IsEqual(-1);   // a node is not backed by a single row
        AssertThat(hit.Row).IsNull();            // ... so it must not name a row either
        AssertThat(hit.Label).IsEqual("A");
        AssertThat(Approx(hit.ScreenX, node.X + node.W / 2f)).IsTrue();
    }

    [TestCase]
    public void SankeyHitTestStaysInBoundsWhenLayoutAndHitDataDiffer()
    {
        var canvas = new FakeCanvas2D();
        var mark = new SankeyMark();

        var layoutCtx = TestContexts.Mark(canvas, LinkedNodes(3), new EncodeSet(), DummyScales());
        mark.Render(layoutCtx);

        var hitCtx = TestContexts.Mark(
            canvas,
            LinkedNodes(1),
            new EncodeSet(),
            DummyScales(),
            layoutVersion: layoutCtx.LayoutVersion);

        HitResult? outOfBounds = null;
        for (float y = 0; y <= 300; y += 5)
        {
            for (float x = 0; x <= 400; x += 5)
            {
                var hit = mark.HitTest(hitCtx, new Vector2(x, y));
                if (hit is not null && hit.RowIndex >= hitCtx.Data.Count)
                    outOfBounds = hit;
            }
        }

        AssertThat(outOfBounds is null).IsTrue();
    }

    // ── Layout cache ────────────────────────────────────────────────────────

    [TestCase]
    public void SankeyLayoutIsNotRebuiltOnEveryFrame()
    {
        var canvas = new FakeCanvas2D();
        var mark = new SankeyMark();
        var chart = SankeyChart(canvas, Relations(0), mark);

        chart.Render();
        chart.Render();
        chart.Render();

        AssertThat(mark.LayoutBuildCount).IsEqual(1);
    }

    [TestCase]
    public void LayoutIsRebuiltWhenTheDataChanges()
    {
        var canvas = new FakeCanvas2D();
        var mark = new SankeyMark();
        var chart = SankeyChart(canvas, Relations(0), mark);
        chart.Render();

        chart.Data(Relations(100));
        chart.Render();

        AssertThat(mark.LayoutBuildCount).IsEqual(2);
    }

    // ── shared data helpers ─────────────────────────────────────────────────

    private static List<DataRow> Relations(int valueOffset) =>
    [
        D(("source", "A"), ("target", "B"), ("value", 5.0 + valueOffset)),
        D(("source", "B"), ("target", "C"), ("value", 3.0)),
    ];

    private static List<DataRow> LinkedNodes(int count)
    {
        var rows = new List<DataRow>();
        for (int i = 0; i < count; i++)
        {
            rows.Add(D(("source", $"N{i}"), ("target", $"N{(i + 1) % count}"), ("value", (double)(i + 1))));
        }
        return rows;
    }

    private static ScaleSet DummyScales()
    {
        var scales = new ScaleSet();
        scales.Set(Channel.X, new OrdinalScale());
        scales.Set(Channel.Y, new LinearScale(0, 10));
        return scales;
    }
}
