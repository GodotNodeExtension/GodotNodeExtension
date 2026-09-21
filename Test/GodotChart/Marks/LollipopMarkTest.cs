namespace GodotNodeExtension.Tests.GodotChart.Marks;

using System.Collections.Generic;
using GdUnit4;
using Godot;
using GodotNodeExtension.Component.GodotChart;
using GodotNodeExtension.Component.GodotChart.Marks;
using Support;
using static GdUnit4.Assertions;
using static GodotNodeExtension.Tests.GodotChart.Support.Asserts;

/// <summary>
/// Behaviour specification for <see cref="LollipopMark"/>: the dot radius, the horizontal
/// orientation, hover scaling and an empty domain. The guard that skips rows whose value cannot be
/// placed (NaN/±∞) is pinned once for every mark by <see cref="MarkDataSafetyTest"/>.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class LollipopMarkTest
{
    private static readonly string[] TwoCategories = { "A", "B" };
    private static readonly string[] ThreeCategories = { "A", "B", "C" };


    private static DataRow D(params (string Field, object? Value)[] fields) => TestContexts.Row(fields);

    private static OrdinalScale Ordinal(IEnumerable<string> keys)
    {
        var scale = new OrdinalScale();
        scale.Fit(keys);
        return scale;
    }

    private static List<DataRow> Simple() =>

    [
        D(("cat", "A"), ("value", 10.0)),
        D(("cat", "B"), ("value", 20.0)),
        D(("cat", "C"), ("value", 15.0)),
    ];

    // ── Dot & stem geometry ──────────────────────────────────────────────────

    [TestCase]
    public void LollipopDotRadiusReachesTheCircle()
    {
        var canvas = new FakeCanvas2D();
        var scales = TestContexts.CategoryScales(ThreeCategories, 0, 25);
        new LollipopMark { DotRadius = 9f }.Render(
            TestContexts.Mark(canvas, Simple(), TestContexts.XyEncodes("cat", "value"), scales));

        AssertThat(canvas.Circles.Count).IsEqual(3);
        AssertThat(Approx(canvas.Circles[0].Radius, 9f)).IsTrue();
    }

    [TestCase]
    public void LollipopHorizontalDotsFollowTheValueOnX()
    {
        var data = new List<DataRow>
        {
            D(("cat", "A"), ("value", 10.0)),
            D(("cat", "B"), ("value", 20.0)),
        };
        var encodes = TestContexts.XyEncodes("value", "cat");

        var scales = new ScaleSet();
        scales.Set(Channel.Y, Ordinal(TwoCategories));
        scales.Set(Channel.X, new LinearScale(0, 25));

        var canvas = new FakeCanvas2D();
        new LollipopMark { Orientation = BarOrientation.Horizontal }.Render(
            TestContexts.Mark(canvas, data, encodes, scales));

        // Larger value -> further right; different categories -> different Y.
        AssertThat(canvas.Circles[0].Cx < canvas.Circles[1].Cx).IsTrue();
        AssertThat(Approx(canvas.Circles[0].Cy, canvas.Circles[1].Cy)).IsFalse();
        AssertThat(canvas.StrokeCount).IsEqual(2);   // one stem per point
    }

    // ── Hover ────────────────────────────────────────────────────────────────

    [TestCase]
    public void LollipopHoverScalesTheDot()
    {
        var theme = ChartTheme.Dark();
        theme.LollipopHoverScale = 2f;

        var scales = TestContexts.CategoryScales(ThreeCategories, 0, 25);
        var canvas = new FakeCanvas2D();
        new LollipopMark { DotRadius = 5f }.Render(TestContexts.Mark(
            canvas, Simple(), TestContexts.XyEncodes("cat", "value"), scales,
            theme: theme, hoveredRowIndex: 0));

        AssertThat(Approx(canvas.Circles[0].Radius, 10f)).IsTrue();
    }

    // ── Degenerate data ──────────────────────────────────────────────────────

    // ── Hit testing ──────────────────────────────────────────────────────────

    /// <summary>
    /// The dot is what a pointer can hit, at the position the renderer draws it - and nothing else is: the
    /// probe takes its coordinates from the geometry <c>Render</c> just produced, so hit and draw cannot drift
    /// apart, and a row <c>Render</c> skips (a value that is not a number) is not hittable either.
    /// </summary>
    [TestCase]
    public void LollipopHitTestFindsTheDrawnDotOnly()
    {
        var canvas = new FakeCanvas2D();
        var scales = TestContexts.CategoryScales(ThreeCategories, 0, 25);
        var mark = new LollipopMark { DotRadius = 5f };
        var ctx = TestContexts.Mark(canvas, Simple(), TestContexts.XyEncodes("cat", "value"), scales);

        mark.Render(ctx);

        AssertThat(canvas.Circles.Count).IsEqual(3);       // one dot per row
        var (dotX, dotY, dotR) = canvas.Circles[0];
        AssertThat(dotR).IsEqual(5f);

        var hit = mark.HitTest(ctx, new Vector2(dotX, dotY));
        AssertThat(hit).IsNotNull();
        AssertThat(hit!.RowIndex).IsEqual(0);
        AssertThat(Approx(hit.ScreenX, dotX)).IsTrue();
        AssertThat(Approx(hit.ScreenY, dotY)).IsTrue();

        // Well away from every dot (and outside the plot).
        AssertThat(mark.HitTest(ctx, new Vector2(dotX, dotY - 80f))).IsNull();

        // A value that is not a number skips the row in Render, so it is not hittable either.
        var broken = TestContexts.Mark(canvas, [D(("cat", "A"), ("value", "nope"))],
            TestContexts.XyEncodes("cat", "value"), scales);
        AssertThat(mark.HitTest(broken, new Vector2(dotX, dotY))).IsNull();
    }

    [TestCase]
    public void LollipopWithAnEmptyDomainDrawsNothing()
    {
        var scales = new ScaleSet();
        scales.Set(Channel.X, new OrdinalScale());          // never fitted -> empty domain
        scales.Set(Channel.Y, new LinearScale(0, 25));

        var canvas = new FakeCanvas2D();
        new LollipopMark().Render(
            TestContexts.Mark(canvas, Simple(), TestContexts.XyEncodes("cat", "value"), scales));

        AssertThat(canvas.Circles.Count).IsEqual(0);
        AssertThat(canvas.StrokeCount).IsEqual(0);
    }
}
