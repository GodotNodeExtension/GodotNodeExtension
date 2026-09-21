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
using static GodotNodeExtension.Tests.GodotChart.Support.Asserts;

/// <summary>
/// Behaviour specification for <see cref="PieMark"/>: the start angle, the donut inner radius, the
/// outer radius factor, the centre text and slice labels, hover explode, hit testing around the
/// donut hole, hit testing that follows the drawn radius and the entry animation, the all-zero case,
/// and hot-path allocation.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class PieMarkTest
{

    private static DataRow D(params (string Field, object? Value)[] fields) => TestContexts.Row(fields);

    private static List<DataRow> TwoSlices() =>

    [
        D(("cat", "A"), ("value", 1.0)),
        D(("cat", "B"), ("value", 1.0)),
    ];

    // ── Geometry ─────────────────────────────────────────────────────────────

    [TestCase]
    public void PieStartAngleIsTheFirstArcStart()
    {
        var canvas = new FakeCanvas2D();
        new PieMark { StartAngle = 0f, ShowLabel = false }.Render(
            TestContexts.Mark(canvas, TwoSlices(), TestContexts.XyEncodes("cat", "value"), new ScaleSet()));

        AssertThat(canvas.Arcs.Count).IsEqual(2);
        AssertThat(Approx(canvas.Arcs[0].Start, 0f)).IsTrue();
    }

    [TestCase]
    public void PieInnerRadiusRendersTwoArcsPerSliceWithTheInnerRatio()
    {
        var canvas = new FakeCanvas2D();
        new PieMark { InnerRadius = 0.5f, ShowLabel = false }.Render(
            TestContexts.Mark(canvas, TwoSlices(), TestContexts.XyEncodes("cat", "value"), new ScaleSet()));

        // Each slice becomes an outer arc + an inner arc.
        AssertThat(canvas.Arcs.Count).IsEqual(4);
        AssertThat(Approx(canvas.Arcs[1].Radius, canvas.Arcs[0].Radius * 0.5f)).IsTrue();
    }

    [TestCase]
    public void PieRadiusFactorScalesTheOuterRadius()
    {
        var canvas = new FakeCanvas2D();
        new PieMark { RadiusFactor = 0.5f, ShowLabel = false }.Render(
            TestContexts.Mark(canvas, TwoSlices(), TestContexts.XyEncodes("cat", "value"), new ScaleSet()));

        // min(400,300)/2 * 0.5 = 75
        AssertThat(Approx(canvas.Arcs[0].Radius, 75f)).IsTrue();
    }

    // ── Centre text & slice labels ───────────────────────────────────────────

    [TestCase]
    public void PieCenterTextIsDrawnInTheDonutHole()
    {
        var canvas = new FakeCanvas2D();
        new PieMark { InnerRadius = 0.5f, CenterText = "Total\n42", ShowLabel = false }.Render(
            TestContexts.Mark(canvas, TwoSlices(), TestContexts.XyEncodes("cat", "value"), new ScaleSet()));

        AssertThat(canvas.Texts.Contains("Total")).IsTrue();
        AssertThat(canvas.Texts.Contains("42")).IsTrue();
    }

    [TestCase]
    public void PieSliceLabelBuilderOverridesTheDefaultLabel()
    {
        var canvas = new FakeCanvas2D();
        new PieMark { SliceLabelBuilder = c => "S" + c.RowIndex }.Render(
            TestContexts.Mark(canvas, TwoSlices(), TestContexts.XyEncodes("cat", "value"), new ScaleSet()));

        AssertThat(canvas.Texts.Contains("S0")).IsTrue();
        AssertThat(canvas.Texts.Contains("S1")).IsTrue();
    }

    // ── Hover & hit testing ──────────────────────────────────────────────────

    [TestCase]
    public void PieHoverExplodesTheSliceCentre()
    {
        var canvas = new FakeCanvas2D();
        new PieMark { ShowLabel = false }.Render(TestContexts.Mark(
            canvas, TwoSlices(), TestContexts.XyEncodes("cat", "value"), new ScaleSet(),
            theme: ChartTheme.Dark(), hoveredRowIndex: 0));

        // The first slice's mid-angle is 0 rad, so the explode offset pushes the arc centre right of
        // the plot centre (200).
        AssertThat(canvas.Arcs[0].Cx > 200f).IsTrue();
    }

    [TestCase]
    public void PieHitTestExcludesTheDonutHole()
    {
        var mark = new PieMark { InnerRadius = 0.5f, ShowLabel = false };
        var ctx = TestContexts.Mark(
            new FakeCanvas2D(), TwoSlices(), TestContexts.XyEncodes("cat", "value"), new ScaleSet());

        // Plot centre is inside the hole, a point 100px out lies on the ring.
        AssertThat(mark.HitTest(ctx, new Vector2(200f, 150f)) is null).IsTrue();
        AssertThat(mark.HitTest(ctx, new Vector2(300f, 150f)) is not null).IsTrue();
    }

    [TestCase]
    public void PieHitTestStopsAtTheDrawnRadiusAndNotAtTheLabelRing()
    {
        // With labels on, Render shrinks the pie to RadiusFactor / max(LabelDistance, 1) = 0.85/1.15
        // of the 150px half-height, i.e. ~110.9px, while the label ring sits at 150 * 0.85 = 127.5px.
        // Hit testing used to use the label-ring radius, so clicking the ring selected a slice that is
        // not drawn there.
        var mark = new PieMark();   // ShowLabel = true by default, so the label ring exists
        var ctx = TestContexts.Mark(
            new FakeCanvas2D(), TwoSlices(), TestContexts.XyEncodes("cat", "value"), new ScaleSet());

        AssertThat(mark.HitTest(ctx, new Vector2(200f + 120f, 150f)) is null).IsTrue();      // in the label ring
        AssertThat(mark.HitTest(ctx, new Vector2(200f + 100f, 150f)) is not null).IsTrue();  // on the drawn pie
    }

    [TestCase]
    public void PieHitTestFollowsTheEntryAnimation()
    {
        var mark = new PieMark { ShowLabel = false };
        var encodes = TestContexts.XyEncodes("cat", "value");

        // The sweep starts at -π/2 (top) and covers τ/2 per slice, so the top of the circle is the
        // first thing drawn and the bottom is only reached after half the sweep.
        var nothing = TestContexts.Mark(new FakeCanvas2D(), TwoSlices(), encodes, new ScaleSet(),
                                        animation: new AnimationContext { EntryProgress = 0f });
        AssertThat(mark.HitTest(nothing, new Vector2(200f, 90f)) is null).IsTrue();
        AssertThat(mark.HitTest(nothing, new Vector2(200f, 210f)) is null).IsTrue();

        var quarter = TestContexts.Mark(new FakeCanvas2D(), TwoSlices(), encodes, new ScaleSet(),
                                        animation: new AnimationContext { EntryProgress = 0.25f });
        AssertThat(mark.HitTest(quarter, new Vector2(200f, 90f)) is not null).IsTrue();   // already revealed
        AssertThat(mark.HitTest(quarter, new Vector2(200f, 210f)) is null).IsTrue();      // not drawn yet
    }

    // ── Degenerate data ──────────────────────────────────────────────────────

    [TestCase]
    public void PieDrawsASinglePositiveSliceAsAFullRing()
    {
        // One positive row is 100% of the pie. The sweep has to stay a hair below a full turn (the
        // backend turns a 2*pi sweep into 0 and the whole pie disappeared), the slice must really have
        // area, and a negative row has no slice at all: it is skipped instead of subtracting from the
        // total and drawing a backwards wedge over the pie.
        var rows = new List<DataRow>
        {
            D(("cat", "only"), ("value", 100.0)),
            D(("cat", "negative"), ("value", -40.0)),
        };
        var canvas = new FakeCanvas2D();
        new PieMark { ShowLabel = false }.Render(
            TestContexts.Mark(canvas, rows, TestContexts.XyEncodes("cat", "value"), new ScaleSet()));

        AssertThat(canvas.FillCount).IsEqual(1);       // the negative row drew nothing
        AssertThat(canvas.Arcs.Count).IsEqual(1);
        float swept = canvas.Arcs[0].End - canvas.Arcs[0].Start;
        AssertThat(swept > MathF.Tau * 0.999f).IsTrue();   // effectively the whole circle
        AssertThat(swept < MathF.Tau).IsTrue();            // ... but never a full turn
        AssertThat(canvas.Arcs[0].Radius > 0f).IsTrue();   // a real radius, not a zero-width path
        AssertThat(canvas.NonFiniteCoordinateCount).IsEqual(0);
        AssertThat(canvas.NegativeSizeRectCount).IsEqual(0);
    }

    [TestCase]
    public void PieAllZeroValuesDrawNothing()
    {
        var data = new List<DataRow>
        {
            D(("cat", "A"), ("value", 0.0)),
            D(("cat", "B"), ("value", 0.0)),
        };
        var canvas = new FakeCanvas2D();
        new PieMark().Render(
            TestContexts.Mark(canvas, data, TestContexts.XyEncodes("cat", "value"), new ScaleSet()));

        AssertThat(canvas.DrewAnything).IsFalse();
    }

    // ── Allocation ───────────────────────────────────────────────────────────

    /// <summary>Objects a whole frame may create regardless of the element count (axes, grid, background).</summary>
    private const int FrameOverheadBudget = 12;

    [TestCase]
    public void PieDoesNotAllocatePerSlice()
    {
        var small = RenderOnce(new PieMark(), MarkCases.ManyBars(4), "cat", "value");
        var large = RenderOnce(new PieMark(), MarkCases.ManyBars(60), "cat", "value");

        int delta = (large.Paths + large.Paints) - (small.Paths + small.Paints);
        AssertThat(delta <= FrameOverheadBudget).IsTrue();
    }

    private static (int Paths, int Paints) RenderOnce(Mark mark, List<DataRow> data,
        string x, string y, string? color = null, int frames = 1)
    {
        var canvas = new FakeCanvas2D();
        var chart = new Chart(canvas) { Width = 400f, Height = 300f };
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

    // ── Slice label builder ─────────────────────────────────────────────────

    // ── Per-mark label builder ──────────────────────────────────────────────

    [TestCase]
    public void PieMarkUsesItsSliceLabelBuilder()
    {
        var canvas = new FakeCanvas2D();
        var chart = new Chart(canvas) { Width = 400f, Height = 300f };
        chart.Data(new List<DataRow>
        {
            D(("cat", "A"), ("value", 10.0)),
            D(("cat", "B"), ("value", 20.0)),
        });

        var pie = new PieMark
        {
            ShowLabel = true,
            SliceLabelBuilder = ctx => $"slice:{ctx.RowIndex}",
        };
        chart.Mark(pie);
        chart.Encode(Channel.X, "cat");
        chart.Encode(Channel.Y, "value");
        chart.Render();

        AssertThat(canvas.Texts.Any(t => t.StartsWith("slice:", StringComparison.Ordinal))).IsTrue();
    }
}
