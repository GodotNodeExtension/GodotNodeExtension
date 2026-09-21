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
/// Behaviour specification for <see cref="HeatmapMark"/>.
/// <para>
/// Covers the cell grid (gap, rows following the Y axis, only the cells present in the data), the value
/// labels and the colours taken from the colour scale, the scale contribution (a caller-provided
/// continuous scale wins, a categorical fallback is replaced), the row categories exposed through
/// the Y axis and the cell hit test.
/// </para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class HeatmapMarkTest
{
    // ── shared helpers ──

    /// <summary>Local floating point comparison: mark geometry is never bit-exact.</summary>
    private static bool Approx(double a, double b, double tol = 0.01) => Math.Abs(a - b) <= tol;

    private static DataRow D(params (string Field, object? Value)[] fields) => TestContexts.Row(fields);

    /// <summary>
    /// A 2 x 2 heatmap context: ordinal X ("A","B"), ordinal Y ("1","2") and a sequential colour
    /// scale fitted to the values 1..4. Two cells of a 400 x 300 plot are 199.5 x 149.5 px.
    /// </summary>
    private static (HeatmapMark Mark, FakeCanvas2D Canvas, MarkContext Ctx, ScaleSet Scales) HeatmapCtx(
        List<DataRow> rows, PlotArea? plot = null)
    {
        var canvas = new FakeCanvas2D();
        var encodes = new EncodeSet();
        encodes.Set(Channel.X, new FieldEncode("x"));
        encodes.Set(Channel.Y, new FieldEncode("y"));
        encodes.Set(Channel.Color, new FieldEncode("value"));

        var xScale = new OrdinalScale();
        xScale.Fit(new List<object> { "A", "B" });
        var yScale = new OrdinalScale();
        yScale.Fit(new List<object> { "1", "2" });
        var colorScale = new SequentialColorScale();
        colorScale.Fit(new List<object> { 1.0, 2.0, 3.0, 4.0 });

        var scales = new ScaleSet();
        scales.Set(Channel.X, xScale);
        scales.Set(Channel.Y, yScale);
        scales.Set(Channel.Color, colorScale);

        var ctx = TestContexts.Mark(canvas, rows, encodes, scales, plot: plot);
        return (new HeatmapMark(), canvas, ctx, scales);
    }

    // ── Default rendering ──

    [TestCase]
    public void HeatmapRowsFollowTheYAxis()
    {
        var (mark, canvas, ctx, scales) = HeatmapCtx(MarkCases.Matrix());
        mark.Render(ctx);

        // Rect 0 is (x=A, y="1"), rect 2 is (x=A, y="2"). Rows follow the Y scale, which is drawn
        // bottom-up: the first Y category sits at the bottom of the plot (reversed from the label
        // order) and its cell is centred exactly on the screen position the Y scale maps it to, so
        // the cell lines up with its own axis label instead of mirroring it.
        AssertThat(canvas.Rects.Count).IsEqual(4);

        var yScale = (OrdinalScale)scales.Get(Channel.Y);
        float firstRowCentre = canvas.Rects[0].Y + canvas.Rects[0].H / 2f;
        float secondRowCentre = canvas.Rects[2].Y + canvas.Rects[2].H / 2f;

        AssertThat(Approx(firstRowCentre, ctx.Plot.MapY(yScale.Map("1")))).IsTrue();
        AssertThat(Approx(secondRowCentre, ctx.Plot.MapY(yScale.Map("2")))).IsTrue();
        // "1" is the first category of the domain, so it sits below "2" on screen.
        AssertThat(firstRowCentre > secondRowCentre).IsTrue();
    }

    [TestCase]
    public void HeatmapColorsComeFromTheColorScale()
    {
        var (mark, canvas, ctx, scales) = HeatmapCtx(MarkCases.Matrix());
        mark.Render(ctx);

        var colorScale = (IColorScale)scales.Get(Channel.Color);
        AssertThat(canvas.FillColors[0].ToHtml()).IsEqual(colorScale.MapColor(1.0).ToHtml());
        AssertThat(canvas.FillColors[3].ToHtml()).IsEqual(colorScale.MapColor(4.0).ToHtml());
        // The ramp is monotonic in red for the default gradient.
        AssertThat(colorScale.MapColor(4.0).R > colorScale.MapColor(1.0).R).IsTrue();
    }

    [TestCase]
    public void HeatmapRowCategoriesAreLabelled()
    {
        var canvas = new FakeCanvas2D();
        using var chart = new Chart(canvas) { Width = 400f, Height = 300f };
        chart.Data(new List<DataRow>
        {
            D(("x", "A"), ("y", "Mon"), ("value", 1.0)),
            D(("x", "B"), ("y", "Mon"), ("value", 2.0)),
            D(("x", "A"), ("y", "Tue"), ("value", 3.0)),
            D(("x", "B"), ("y", "Tue"), ("value", 4.0)),
        });
        chart.Mark(new HeatmapMark());
        chart.Encode(Channel.X, "x");
        chart.Encode(Channel.Y, "y");
        chart.Encode(Channel.Color, "value");

        chart.Render();

        var yLabels = canvas.YAxisLabels.ToList();
        AssertThat(yLabels.Contains("Mon")).IsTrue();
        AssertThat(yLabels.Contains("Tue")).IsTrue();
    }

    // ── Options ──

    [TestCase]
    public void HeatmapLabelsFollowTheShowLabelSwitch()
    {
        var (labeled, labeledCanvas, labeledCtx, _) = HeatmapCtx(MarkCases.Matrix());
        labeled.ShowLabel = true;
        labeled.Render(labeledCtx);

        AssertThat(labeledCanvas.Texts.Count).IsEqual(4);
        AssertThat(labeledCanvas.Texts).Contains("1");

        var (plain, plainCanvas, plainCtx, _) = HeatmapCtx(MarkCases.Matrix());
        plain.Render(plainCtx);
        AssertThat(plainCanvas.Texts.Count).IsEqual(0);   // ShowLabel defaults to false
    }

    [TestCase]
    public void HeatmapLabelsFollowTheLabelFormat()
    {
        var (mark, canvas, ctx, _) = HeatmapCtx(MarkCases.Matrix());
        mark.ShowLabel = true;
        mark.LabelFormat = "{0}%";
        mark.Render(ctx);

        // {0} is the value already formatted by the colour scale.
        AssertThat(canvas.Texts).Contains("1%");
        AssertThat(canvas.Texts).Contains("4%");
    }

    [TestCase]
    public void HeatmapCellGapSetsTheCellSizeAndOffset()
    {
        var plot = new PlotArea(0, 0, 100, 100);
        var (mark, canvas, ctx, _) = HeatmapCtx(MarkCases.Matrix(), plot);
        mark.CellGap = 10f;
        mark.Render(ctx);

        // cell = (100 - 10 * (2 - 1)) / 2 = 45 in both directions, columns at x = 0 and 55.
        AssertThat(canvas.Rects.Count).IsEqual(4);
        AssertThat(Approx(canvas.Rects[0].W, 45)).IsTrue();
        AssertThat(Approx(canvas.Rects[0].X, 0)).IsTrue();
        AssertThat(Approx(canvas.Rects[1].X, 55)).IsTrue();
    }

    [TestCase]
    public void HeatmapContributeScalesInstallsASequentialScaleWithoutOverwritingACustomOne()
    {
        var encodes = new EncodeSet();
        encodes.Set(Channel.Color, new FieldEncode("value"));
        var data = MarkCases.Matrix();

        var fresh = new ScaleSet();
        new HeatmapMark().ContributeScales(fresh, encodes, data);
        AssertThat(fresh.TryGet(Channel.Color) is SequentialColorScale).IsTrue();

        var custom = new DivergingColorScale(-1, 1);
        var kept = new ScaleSet();
        kept.Set(Channel.Color, custom);
        new HeatmapMark().ContributeScales(kept, encodes, data);
        AssertThat(ReferenceEquals(kept.TryGet(Channel.Color), custom)).IsTrue();
    }

    // ── Hit testing ──

    [TestCase]
    public void HeatmapConstantMatrixAndHitTestStayConsistent()
    {
        var rows = new List<DataRow>
        {
            D(("x", "A"), ("y", "1"), ("value", 5.0)),
            D(("x", "B"), ("y", "1"), ("value", 5.0)),
            D(("x", "A"), ("y", "2"), ("value", 5.0)),
            D(("x", "B"), ("y", "2"), ("value", 5.0)),
        };
        var (mark, canvas, ctx, _) = HeatmapCtx(rows);
        mark.Render(ctx);

        AssertThat(canvas.NegativeSizeRectCount).IsEqual(0);
        AssertThat(canvas.Rects.Count).IsEqual(4);

        var first = canvas.Rects[0];
        var hit = mark.HitTest(ctx, new Vector2(first.X + first.W / 2f, first.Y + first.H / 2f));

        AssertThat(hit is not null).IsTrue();
        AssertThat(hit!.RowIndex).IsEqual(0);
        AssertThat(hit.Label!.Contains("A/1")).IsTrue();
    }

    [TestCase]
    public void HeatmapHitTestReportsTheCellCentre()
    {
        var (mark, canvas, ctx, _) = HeatmapCtx(MarkCases.Matrix());
        mark.Render(ctx);

        var cell = canvas.Rects[0];
        // A point just inside the top-left corner of the first cell still reports the cell centre
        // on both axes (the label anchor used by Render).
        var hit = mark.HitTest(ctx, new Vector2(cell.X + 1f, cell.Y + 1f));

        AssertThat(hit is not null).IsTrue();
        AssertThat(Approx(hit!.ScreenX, cell.X + cell.W / 2f)).IsTrue();
        AssertThat(Approx(hit.ScreenY, cell.Y + cell.H / 2f)).IsTrue();
    }

    // ── Degenerate data ──

    [TestCase]
    public void HeatmapDrawsOnlyTheCellsPresentInTheData()
    {
        var rows = new List<DataRow>
        {
            D(("x", "A"), ("y", "1"), ("value", 1.0)),
            D(("x", "B"), ("y", "2"), ("value", 4.0)),
            D(("x", "A"), ("y", "2"), ("value", 3.0)),
        };
        var (mark, canvas, ctx, _) = HeatmapCtx(rows);
        mark.Render(ctx);

        AssertThat(canvas.FillCount).IsEqual(3);   // (B, 1) is missing from the data
        AssertThat(canvas.Rects.Count).IsEqual(3);
    }

    /// <summary>
    /// A pinned colour is not a value on the ramp. <c>Encode(Channel.Color, "constant:#ff8800")</c> used
    /// to feed the colour string straight into the sequential scale - the contribution must filter it,
    /// the frame must not throw, and it must not emit non-finite geometry. A mixed column (numbers plus a
    /// colour string) still draws its numeric cells, so the guard cannot be "skip the whole chart".
    /// </summary>
    [TestCase]
    public void HeatmapWithAConstantColourStaysSafeAndStillFiltersNonNumericCells()
    {
        var constant = new FakeCanvas2D();
        var constantChart = new Chart(constant) { Width = 400f, Height = 300f };
        constantChart.Data(MarkCases.Matrix());
        constantChart.Mark(new HeatmapMark());
        constantChart.Encode(Channel.X, "x");
        constantChart.Encode(Channel.Y, "y");
        constantChart.Encode(Channel.Color, "constant:#ff8800");
        constantChart.Render();     // a throw here would fail the case

        AssertThat(constant.NonFiniteCoordinateCount).IsEqual(0);
        AssertThat(constant.NegativeSizeRectCount).IsEqual(0);

        var mixed = new List<DataRow>
        {
            D(("x", "A"), ("y", "1"), ("value", 1.0)),
            D(("x", "B"), ("y", "1"), ("value", 2.0)),
            D(("x", "A"), ("y", "2"), ("value", 3.0)),
            D(("x", "B"), ("y", "2"), ("value", "#ff8800")),   // a colour where the ramp expects a number
        };
        var (mark, canvas, ctx, _) = HeatmapCtx(mixed);
        mark.Render(ctx);

        AssertThat(canvas.FillCount).IsEqual(3);   // the three numeric cells, the colour string is skipped
        AssertThat(canvas.NonFiniteCoordinateCount).IsEqual(0);
    }

    // ── Allocation ──

    /// <summary>Objects a whole frame may create regardless of the element count (axes, grid, background).</summary>
    private const int FrameOverheadBudget = 12;

    private static (int Paths, int Paints) RenderOnce(Mark mark, List<DataRow> data,
        string x, string y, string? color = null, int frames = 1)
    {
        var canvas = new FakeCanvas2D();
        using var chart = new Chart(canvas) { Width = 400f, Height = 300f };
        chart.Data(data);
        chart.Mark(mark);
        chart.Encode(Channel.X, x);
        chart.Encode(Channel.Y, y);
        if (color != null) chart.Encode(Channel.Color, color);

        canvas.PathCreateCount = 0;
        canvas.PaintCreateCount = 0;
        for (int i = 0; i < frames; i++)
            chart.Render();
        return (canvas.PathCreateCount, canvas.PaintCreateCount);
    }

    [TestCase]
    public void HeatmapDoesNotAllocatePerCell()
    {
        var small = RenderOnce(new HeatmapMark(), MarkCases.ManyCells(2), "x", "y", "value");
        var large = RenderOnce(new HeatmapMark(), MarkCases.ManyCells(12), "x", "y", "value");

        int delta = (large.Paths + large.Paints) - (small.Paths + small.Paints);
        AssertThat(delta <= FrameOverheadBudget).IsTrue();
    }
}
