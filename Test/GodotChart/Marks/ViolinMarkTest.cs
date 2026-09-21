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
/// Behaviour specification for <see cref="ViolinMark"/>.
/// <para>
/// Covers the density body and its width ratio, the median dot and the IQR box indicators and
/// their toggles, the degenerate-group guards (single value, constant group, zero bins) and the
/// silhouette hit test — which reports the row that actually sits at the probed height. The skip
/// of a non-finite value is pinned once for every mark by <see cref="MarkDataSafetyTest"/>.
/// </para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class ViolinMarkTest
{
    private static readonly string[] SingleCategory = { "A" };
    private static readonly string[] TwoCategories = { "A", "B" };

    // ── shared helpers ──

    /// <summary>Local floating point comparison: mark geometry is never bit-exact.</summary>
    private static bool Approx(double a, double b, double tol = 0.01) => Math.Abs(a - b) <= tol;

    private static DataRow D(params (string Field, object? Value)[] fields) => TestContexts.Row(fields);

    /// <summary>Default plot is 400 x 300 with Y over [0, 10] and two categories (slots of 200px).</summary>
    private static (ViolinMark Mark, FakeCanvas2D Canvas, MarkContext Ctx) ViolinCtx(List<DataRow> rows)
    {
        var canvas = new FakeCanvas2D();
        var ctx = TestContexts.Mark(canvas, rows, TestContexts.XyEncodes("cat", "value"),
            TestContexts.CategoryScales(TwoCategories, 0, 10));
        return (new ViolinMark(), canvas, ctx);
    }

    // ── Density outline ──

    [TestCase]
    public void DensityOutlineStaysContinuousForSparseGroups()
    {
        // A group of four samples used to collapse into one sliver per non-empty histogram bin; the
        // kernel estimate is positive everywhere between the samples, so the body stays solid.
        var values = new List<double> { 2.0, 4.0, 7.0, 9.0 };
        var curve = ViolinMark.BuildDensityCurve(values, 20);

        AssertThat(curve.Lo < values[0]).IsTrue();
        AssertThat(curve.Hi > values[^1]).IsTrue();
        AssertThat(curve.Density.Length).IsEqual(20);

        double peak = 0;
        foreach (double d in curve.Density) peak = Math.Max(peak, d);
        AssertThat(Approx(peak, 1.0, 1e-9)).IsTrue();

        // Every sample sits inside a positive band of the silhouette ...
        foreach (double v in values)
            AssertThat(curve.At(v) > 0.1).IsTrue();

        // ... and so does the space between them (no notch).
        AssertThat(curve.At(5.5) > 0.1).IsTrue();
    }

    [TestCase]
    public void DensityResolutionFollowsBinCountWithAFloor()
    {
        var values = new List<double> { 1.0, 2.0, 3.0, 4.0 };

        AssertThat(ViolinMark.BuildDensityCurve(values, 20).Density.Length).IsEqual(20);
        AssertThat(ViolinMark.BuildDensityCurve(values, 8).Density.Length).IsEqual(8);
        // A degenerate/zero resolution still produces a usable outline.
        AssertThat(ViolinMark.BuildDensityCurve(values, 0).Density.Length).IsEqual(8);
        AssertThat(ViolinMark.BuildDensityCurve([], 20).Density.Length).IsEqual(0);
    }

    [TestCase]
    public void DensityCurveIsSymmetricAroundItsMedianForSymmetricData()
    {
        var values = new List<double> { 1.0, 3.0, 5.0, 7.0, 9.0 };
        var curve = ViolinMark.BuildDensityCurve(values, 41);

        // 5.0 is the middle sample: the silhouette mirrors around it.
        AssertThat(Approx(curve.At(5.0 - 1.5), curve.At(5.0 + 1.5), 1e-6)).IsTrue();
        AssertThat(Approx(curve.At(5.0 - 3.0), curve.At(5.0 + 3.0), 1e-6)).IsTrue();
        AssertThat(curve.At(5.0) > curve.At(5.0 + 3.0)).IsTrue();
    }

    [TestCase]
    public void DensityTailWidensTheValueAxis()
    {
        var rows = new List<DataRow>
        {
            D(("cat", "A"), ("value", 3.0)),
            D(("cat", "A"), ("value", 5.0)),
            D(("cat", "A"), ("value", 7.0)),
        };
        var scales = TestContexts.CategoryScales(SingleCategory, 0, 10);
        var mark = new ViolinMark();

        mark.ContributeScales(scales, TestContexts.XyEncodes("cat", "value"), rows);

        // Nothing clips a mark, so the tails have to be part of the axis: the scale reaches below the
        // smallest and above the largest sample.
        var yScale = (LinearScale)scales.Get(Channel.Y);
        AssertThat(yScale.Min < 3.0).IsTrue();
        AssertThat(yScale.Max > 7.0).IsTrue();
    }

    // ── Default rendering ──

    [TestCase]
    public void ViolinMedianDotSitsAtTheMappedMedian()
    {
        var (mark, canvas, ctx) = ViolinCtx(MarkCases.Distribution());
        mark.Render(ctx);

        // Values 3,5,7,9,11 -> interpolated median 7 -> yScale(0..10) -> MapY(0.7) = 90.
        AssertThat(canvas.Circles.Count).IsEqual(2);
        AssertThat(Approx(canvas.Circles[0].Cx, 100)).IsTrue();
        AssertThat(Approx(canvas.Circles[0].Cy, 90)).IsTrue();
        AssertThat(Approx(canvas.Circles[0].Radius, 3)).IsTrue();
    }

    // ── Options ──

    [TestCase]
    public void ViolinWidthRatioSetsTheBoxWidth()
    {
        float BoxWidth(float ratio)
        {
            var (mark, canvas, ctx) = ViolinCtx(MarkCases.Distribution());
            mark.WidthRatio = ratio;
            mark.Render(ctx);
            AssertThat(canvas.Rects.Count).IsEqual(2);   // one IQR box per category
            return canvas.Rects[0].W;
        }

        // slot width = 400 / 2 categories = 200; box width = slot * WidthRatio * ViolinBoxWidthRatio(0.15)
        AssertThat(Approx(BoxWidth(0.7f), 200 * 0.7 * 0.15, 0.05)).IsTrue();
        AssertThat(Approx(BoxWidth(0.5f), 200 * 0.5 * 0.15, 0.05)).IsTrue();
    }

    [TestCase]
    public void ViolinMedianAndBoxTogglesRemoveTheIndicators()
    {
        var (noMedian, noMedianCanvas, noMedianCtx) = ViolinCtx(MarkCases.Distribution());
        noMedian.ShowMedian = false;
        noMedian.Render(noMedianCtx);
        AssertThat(noMedianCanvas.Circles.Count).IsEqual(0);
        AssertThat(noMedianCanvas.Rects.Count).IsEqual(2);   // the box is still drawn

        var (noBox, noBoxCanvas, noBoxCtx) = ViolinCtx(MarkCases.Distribution());
        noBox.ShowBox = false;
        noBox.Render(noBoxCtx);
        AssertThat(noBoxCanvas.Rects.Count).IsEqual(0);
        AssertThat(noBoxCanvas.Circles.Count).IsEqual(2);    // the median dot is still drawn
    }

    // ── Selection ──

    [TestCase]
    public void TheSelectedRowRingsTheViolinItBelongsTo()
    {
        var rows = MarkCases.Distribution();   // rows 0-4 are category A, rows 5-9 category B

        FakeCanvas2D RenderSelected(int selected)
        {
            var canvas = new FakeCanvas2D();
            var mark = new ViolinMark();
            mark.States.SelectedStroke = new Color(1f, 0f, 1f);
            mark.States.SelectedStrokeWidth = 4f;
            mark.Render(TestContexts.Mark(canvas, rows, TestContexts.XyEncodes("cat", "value"),
                TestContexts.CategoryScales(TwoCategories, 0, 10), selectedRowIndex: selected));
            return canvas;
        }

        // One outline per category plus the ring of the violin the selected row belongs to: the ring
        // follows the group, so it moves to the second stroke for a row of category B.
        var selectedA = RenderSelected(0);
        AssertThat(selectedA.StrokeCount).IsEqual(3);
        AssertThat(selectedA.StrokeColors[1]).IsEqual(new Color(1f, 0f, 1f));
        AssertThat(selectedA.StrokeWidths[1]).IsEqual(4f);

        var selectedB = RenderSelected(5);
        AssertThat(selectedB.StrokeCount).IsEqual(3);
        AssertThat(selectedB.StrokeColors[2]).IsEqual(new Color(1f, 0f, 1f));

        var (plain, plainCanvas, plainCtx) = ViolinCtx(rows);
        plain.Render(plainCtx);
        AssertThat(plainCanvas.StrokeCount).IsEqual(2);
    }

    // ── Hover ──

    /// <summary>
    /// A violin is one element per category, so its hover state belongs to the group - the same membership the
    /// selected ring below uses. Resolving the fill from the group's smallest row (which is what the mark did)
    /// lit the violin up only while the pointer was on that one sample.
    /// </summary>
    [TestCase]
    public void HoverLightsTheViolinFromEverySampleOfTheGroup()
    {
        var rows = MarkCases.Distribution();   // rows 0-4 are category A, rows 5-9 category B
        var active = new Color(1f, 0f, 1f);

        // A body fill per category, in category order: index 0 is violin A, index 1 is violin B.
        FakeCanvas2D RenderHovering(int hovered)
        {
            var canvas = new FakeCanvas2D();
            // The IQR box and the median dot fill shapes of their own: off, so the two bodies are the only fills.
            var mark = new ViolinMark { ShowBox = false, ShowMedian = false };
            mark.States.ActiveFill = active;
            mark.Render(TestContexts.Mark(canvas, rows, TestContexts.XyEncodes("cat", "value"),
                TestContexts.CategoryScales(TwoCategories, 0, 10), hoveredRowIndex: hovered));
            return canvas;
        }

        for (int row = 0; row < 5; row++)
        {
            var canvas = RenderHovering(row);
            AssertThat(canvas.FillColors.Count).IsEqual(2);
            AssertThat(canvas.FillColors[0]).IsEqual(active);              // violin A
            AssertThat(canvas.FillColors[1] == active).IsFalse();          // violin B stays plain
        }

        // A sample of B lights B, and no pointer lights nothing.
        var viaB = RenderHovering(7);
        AssertThat(viaB.FillColors[0] == active).IsFalse();
        AssertThat(viaB.FillColors[1]).IsEqual(active);

        var plain = RenderHovering(-1);
        AssertThat(plain.FillColors.Contains(active)).IsFalse();
    }

    // ── Hit testing ──

    [TestCase]
    public void ViolinHitTestStaysInsideTheContour()
    {
        var (mark, _, ctx) = ViolinCtx(MarkCases.Distribution());
        mark.Render(ctx);

        // Category A is centred on x = 100; its median (value 7) maps to y = 90.
        var inside = mark.HitTest(ctx, new Vector2(100.5f, 90f));
        AssertThat(inside is not null).IsTrue();
        AssertThat(inside!.RowIndex).IsEqual(2);
        AssertThat(inside.Label).IsEqual("A");
        AssertThat(Approx(inside.ScreenX, 100)).IsTrue();

        // Same height, but in the empty gap between the two violins: no hit.
        AssertThat(mark.HitTest(ctx, new Vector2(200f, 90f)) is null).IsTrue();
    }

    [TestCase]
    public void ViolinHitTestMapsThePointBackToItsRow()
    {
        // Rows are deliberately out of value order: the hit test must report the row the probed
        // value came from, not the position of that value after the internal sort.
        var rows = new List<DataRow>
        {
            D(("cat", "A"), ("value", 11.0)),   // row 0
            D(("cat", "A"), ("value", 3.0)),    // row 1
            D(("cat", "A"), ("value", 9.0)),    // row 2
            D(("cat", "A"), ("value", 5.0)),    // row 3
            D(("cat", "A"), ("value", 7.0)),    // row 4
        };
        var (mark, _, ctx) = ViolinCtx(rows);
        mark.Render(ctx);

        // Value 7 is the median and lives on row 4.
        var hit = mark.HitTest(ctx, new Vector2(100.5f, 90f));
        AssertThat(hit is not null).IsTrue();
        AssertThat(hit!.RowIndex).IsEqual(4);
    }

    // ── Degenerate data ──

    [TestCase]
    public void ViolinDrawsAMinimalMarkerForGroupsWithoutSpread()
    {
        // A single sample has no density to estimate; the category used to disappear without a word.
        // It is now drawn as a minimal horizontal line at its value, and the geometry stays finite.
        var single = new List<DataRow> { D(("cat", "A"), ("value", 5.0)) };
        var (mark, canvas, ctx) = ViolinCtx(single);
        mark.Render(ctx);
        AssertThat(canvas.StrokeCount).IsEqual(1);            // the flat marker, and nothing else
        AssertThat(canvas.FillCount).IsEqual(0);
        AssertThat(canvas.PathOpCount).IsEqual(2);            // MoveTo + LineTo
        AssertThat(canvas.NonFiniteCoordinateCount).IsEqual(0);
        // Value 5 of the [0, 10] axis maps to the vertical centre of the plot, and the marker is
        // hittable there - while the rest of the category slot stays empty.
        AssertThat(mark.HitTest(ctx, new Vector2(100f, 150f)) is not null).IsTrue();
        AssertThat(mark.HitTest(ctx, new Vector2(100f, 90f)) is null).IsTrue();

        // A constant group (zero variance) is the same situation.
        var constant = new List<DataRow>();
        for (int i = 0; i < 5; i++) constant.Add(D(("cat", "A"), ("value", 5.0)));
        var (flat, flatCanvas, flatCtx) = ViolinCtx(constant);
        flat.Render(flatCtx);
        AssertThat(flatCanvas.StrokeCount).IsEqual(1);
        AssertThat(flatCanvas.FillCount).IsEqual(0);          // no violin body
        AssertThat(flatCanvas.NonFiniteCoordinateCount).IsEqual(0);
    }

    [TestCase]
    public void ViolinWithZeroBinsStillDrawsFiniteGeometry()
    {
        var (mark, canvas, ctx) = ViolinCtx(MarkCases.Distribution());
        mark.BinCount = 0;
        mark.Render(ctx);

        AssertThat(canvas.DrewAnything).IsTrue();
        AssertThat(canvas.NonFiniteCoordinateCount).IsEqual(0);
        AssertThat(canvas.NegativeSizeRectCount).IsEqual(0);
    }

    // ── Layout cache ────────────────────────────────────────────────────────

    /// <summary>
    /// The group cache is keyed with <c>Mark.CacheKey</c> (owner + layout version + data list + hidden
    /// series) instead of the bare data version: a second chart reusing the same mark instance keeps
    /// the same data version, so the old key reused the first chart's distributions - and drew the
    /// second chart's violins with the first chart's medians.
    /// </summary>
    [TestCase]
    public void ViolinRebuildsItsGroupsWhenTheDataListChanges()
    {
        var mark = new ViolinMark();

        var first = new FakeCanvas2D();
        var firstCtx = TestContexts.Mark(first, Group("A", 1.0, 2.0, 3.0, 4.0, 5.0),
            TestContexts.XyEncodes("cat", "value"),
            TestContexts.CategoryScales(SingleCategory, 0, 10));
        mark.Render(firstCtx);
        // Median 3 maps to y = 300 - 0.3 * 300 = 210.
        AssertThat(Approx(first.Circles[0].Cy, 210)).IsTrue();

        // Same versions, different data list instance: the group must be rebuilt.
        var second = new FakeCanvas2D();
        var secondCtx = TestContexts.Mark(second, Group("A", 6.0, 7.0, 8.0, 9.0, 10.0),
            TestContexts.XyEncodes("cat", "value"),
            TestContexts.CategoryScales(SingleCategory, 0, 10),
            dataVersion: firstCtx.DataVersion, layoutVersion: firstCtx.LayoutVersion);
        mark.Render(secondCtx);

        // Median 8 maps to y = 300 - 0.8 * 300 = 60; a stale cache would still draw 210.
        AssertThat(Approx(second.Circles[0].Cy, 60)).IsTrue();
    }

    /// <summary>One category holding the given values.</summary>
    private static List<DataRow> Group(string category, params double[] values)
    {
        var rows = new List<DataRow>(values.Length);
        foreach (double value in values) rows.Add(D(("cat", category), ("value", value)));
        return rows;
    }

    // ── Quantiles and silhouette hit test ───────────────────────────────────

    [TestCase]
    public void ViolinQuantilesAreInterpolated()
    {
        var values = new List<double> { 1, 2, 3, 4 };

        // Index-based quantiles would return 2 / 3 (and q3 == max for n = 4); the helper
        // interpolates between the neighbouring samples instead.
        AssertThat(ViolinMark.Quantile(values, 0.25)).IsEqual(1.75);
        AssertThat(ViolinMark.Quantile(values, 0.5)).IsEqual(2.5);
        AssertThat(ViolinMark.Quantile(values, 0.75)).IsEqual(3.25);
    }

    [TestCase]
    public void ViolinHitTestIgnoresTheEmptySpaceBesideTheSilhouette()
    {
        var canvas = new FakeCanvas2D();
        var chart = new Chart(canvas) { Width = 400f, Height = 300f };
        var rows = new List<DataRow>();
        for (int i = 0; i < 10; i++)
            rows.Add(D(("cat", "A"), ("value", 5.0 + i)));   // one category, wide spread
        chart.Data(rows);
        chart.Mark(new ViolinMark());
        chart.Encode(Channel.X, "cat");
        chart.Encode(Channel.Y, "value");
        chart.Render();

        var plot = chart.CurrentPlotArea!.Value;
        float cx = plot.X + plot.Width / 2f;

        // At the very top of the range the silhouette is thin: a point far to the side must miss.
        var sideHit = chart.HitTest(new Vector2(cx + plot.Width * 0.4f, plot.Y + 5f));
        AssertThat(sideHit is null).IsTrue();
    }
}
