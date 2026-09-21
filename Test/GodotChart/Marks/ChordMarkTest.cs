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
/// Behaviour specification for <see cref="ChordMark"/>: the radii and arc geometry produced by
/// <see cref="ChordMark.RadiusFactor"/>, <see cref="ChordMark.ArcWidthRatio"/> and
/// <see cref="ChordMark.ArcGap"/>, node labels, hit testing of arcs and chords, relations with
/// missing endpoints, full-circle sweep safety and the layout cache.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class ChordMarkTest
{
    private static readonly string[] ThreeCategories = { "A", "B", "C" };

    // ── helpers ─────────────────────────────────────────────────────────────

    /// <summary>Local floating point comparison: mark geometry is never bit-exact.</summary>
    private static bool Approx(double a, double b, double tol = 0.01) => Math.Abs(a - b) <= tol;

    private static DataRow D(params (string Field, object? Value)[] fields) => TestContexts.Row(fields);

    private static (ChordMark Mark, FakeCanvas2D Canvas, MarkContext Ctx) ChordCtx(
        List<DataRow> rows, ChartTheme? theme = null)
    {
        var canvas = new FakeCanvas2D();
        var ctx = TestContexts.Mark(canvas, rows, TestContexts.XyEncodes("cat", "value"),
            TestContexts.CategoryScales(ThreeCategories), theme: theme);
        return (new ChordMark(), canvas, ctx);
    }

    private static Chart ChordChart(FakeCanvas2D canvas, List<DataRow> rows, Mark mark)
    {
        var chart = new Chart(canvas) { Width = 400f, Height = 300f };
        chart.Data(rows);
        chart.Mark(mark);
        chart.Encode(Channel.X, "source");
        chart.Encode(Channel.Y, "value");
        return chart;
    }

    private static ScaleSet DummyScales()
    {
        var scales = new ScaleSet();
        scales.Set(Channel.X, new OrdinalScale());
        scales.Set(Channel.Y, new LinearScale(0, 10));
        return scales;
    }

    private static List<DataRow> Ring(int count)
    {
        var rows = new List<DataRow>();
        for (int i = 0; i < count; i++)
        {
            rows.Add(D(("source", $"N{i}"), ("target", $"N{(i + 1) % count}"), ("value", (double)(i + 1))));
        }
        return rows;
    }

    // ── Colour ──────────────────────────────────────────────────────────────

    [TestCase]
    public void ChordsTakeTheSourceNodeColor()
    {
        var rows = new List<DataRow>
        {
            D(("source", "A"), ("target", "B"), ("value", 10.0)),
            D(("source", "B"), ("target", "A"), ("value", 10.0)),
        };
        var (mark, canvas, ctx) = ChordCtx(rows);
        mark.Render(ctx);

        // Chords are filled before the node arcs, and each chord carries the colour of the node it
        // leaves: with the arcs following in node order (A, B), chord 1 matches arc A and chord 2 arc B.
        AssertThat(canvas.FillColors.Count).IsEqual(4);   // two chords and the two node arcs
        AssertThat(canvas.FillColors[0].ToHtml()).IsEqual(canvas.FillColors[2].ToHtml());
        AssertThat(canvas.FillColors[1].ToHtml()).IsEqual(canvas.FillColors[3].ToHtml());
        AssertThat(canvas.FillColors[0].ToHtml()).IsNotEqual(canvas.FillColors[1].ToHtml());
    }

    // ── Ring geometry options ───────────────────────────────────────────────

    [TestCase]
    public void ChordRadiusFactorScalesTheOuterRing()
    {
        float OuterRadius(float factor)
        {
            var (mark, canvas, ctx) = ChordCtx(MarkCases.Relations());
            mark.RadiusFactor = factor;
            mark.Render(ctx);
            return canvas.Arcs.Max(a => a.Radius);
        }

        // min(plot.Width, plot.Height) / 2 = 150 for the default 400 x 300 plot.
        AssertThat(Approx(OuterRadius(0.5f), 150 * 0.5)).IsTrue();
        AssertThat(Approx(OuterRadius(0.9f), 150 * 0.9)).IsTrue();
    }

    [TestCase]
    public void ChordArcWidthRatioSetsTheInnerRadius()
    {
        float InnerRadius(float ratio)
        {
            var (mark, canvas, ctx) = ChordCtx(MarkCases.Relations());
            mark.RadiusFactor = 0.5f;   // outer radius 75
            mark.ArcWidthRatio = ratio;
            mark.Render(ctx);
            return canvas.Arcs.Min(a => a.Radius);
        }

        AssertThat(Approx(InnerRadius(0.05f), 75 * 0.95)).IsTrue();
        AssertThat(Approx(InnerRadius(0.25f), 75 * 0.75)).IsTrue();
    }

    [TestCase]
    public void ChordArcGapNarrowsTheNodeArcs()
    {
        double TotalNodeSweep(float gap)
        {
            var (mark, canvas, ctx) = ChordCtx(MarkCases.Relations());
            mark.RadiusFactor = 0.5f;
            mark.ArcGap = gap;
            mark.Render(ctx);

            // Node bands are the only arcs drawn on the outer ring.
            return canvas.Arcs
                .Where(a => Approx(a.Radius, 75) && a.End > a.Start)
                .Sum(a => (double)(a.End - a.Start));
        }

        double tight = TotalNodeSweep(0.02f);
        double wide = TotalNodeSweep(0.5f);

        AssertThat(wide < tight).IsTrue();
        // The gaps are taken out of the circle: 3 nodes => 3 gaps.
        AssertThat(Approx(tight, MathF.Tau - 0.02f * 3, 0.02)).IsTrue();
        AssertThat(Approx(wide, MathF.Tau - 0.5f * 3, 0.02)).IsTrue();
    }

    // ── Labels ──────────────────────────────────────────────────────────────

    [TestCase]
    public void ChordLabelsFollowTheShowLabelSwitch()
    {
        var (mark, canvas, ctx) = ChordCtx(MarkCases.Relations());
        mark.Render(ctx);
        AssertThat(canvas.Texts).Contains("A");
        AssertThat(canvas.Texts).Contains("C");

        var (quiet, quietCanvas, quietCtx) = ChordCtx(MarkCases.Relations());
        quiet.ShowLabel = false;
        quiet.Render(quietCtx);
        AssertThat(quietCanvas.Texts.Count).IsEqual(0);

        var (empty, emptyCanvas, emptyCtx) = ChordCtx([]);
        empty.Render(emptyCtx);
        AssertThat(emptyCanvas.DrewAnything).IsFalse();
    }

    [TestCase]
    public void ChordNodeLabelsFollowTheLabelFormat()
    {
        // Relations(0): A→B 5, B→C 3, so A totals 5, B 8 and C 3.
        var (mark, canvas, ctx) = ChordCtx(Relations(0));
        mark.LabelFormat = "{0} ({1})";
        mark.Render(ctx);

        AssertThat(canvas.Texts).Contains("A (5)");
        AssertThat(canvas.Texts).Contains("B (8)");
        AssertThat(canvas.Texts).Contains("C (3)");

        // The default format keeps the bare node name.
        var (plain, plainCanvas, plainCtx) = ChordCtx(Relations(0));
        plain.Render(plainCtx);
        AssertThat(plainCanvas.Texts).Contains("A");
        AssertThat(plainCanvas.Texts.Contains("A (5)")).IsFalse();
    }

    // ── Selection ───────────────────────────────────────────────────────────

    [TestCase]
    public void TheSelectedRelationGetsTheSelectionRing()
    {
        var rows = Relations(0);
        var canvas = new FakeCanvas2D();
        var ctx = TestContexts.Mark(canvas, rows, TestContexts.XyEncodes("cat", "value"),
            TestContexts.CategoryScales(ThreeCategories), selectedRowIndex: 0);

        var mark = new ChordMark();
        mark.States.SelectedStroke = new Color(1f, 0f, 1f);
        mark.States.SelectedStrokeWidth = 4f;
        mark.Render(ctx);

        // Only the selected relation is outlined; chords and arcs are otherwise filled, never stroked.
        AssertThat(canvas.StrokeCount).IsEqual(1);
        AssertThat(canvas.StrokeColors[0]).IsEqual(new Color(1f, 0f, 1f));
        AssertThat(canvas.StrokeWidths[0]).IsEqual(4f);

        var plainCanvas = new FakeCanvas2D();
        var plainCtx = TestContexts.Mark(plainCanvas, rows, TestContexts.XyEncodes("cat", "value"),
            TestContexts.CategoryScales(ThreeCategories));
        new ChordMark().Render(plainCtx);
        AssertThat(plainCanvas.StrokeCount).IsEqual(0);
    }

    // ── Hit testing ─────────────────────────────────────────────────────────

    [TestCase]
    public void ChordArcHitReturnsTheNodeLabel()
    {
        var (mark, canvas, ctx) = ChordCtx(MarkCases.Relations());
        mark.RadiusFactor = 0.5f;
        mark.Render(ctx);

        float outer = canvas.Arcs.Max(a => a.Radius);
        float inner = canvas.Arcs.Min(a => a.Radius);
        var firstBand = canvas.Arcs.First(a => Approx(a.Radius, outer) && a.End > a.Start);
        float mid = (firstBand.Start + firstBand.End) / 2f;
        float r = (outer + inner) / 2f;

        var hit = mark.HitTest(ctx, new Vector2(200f + r * MathF.Cos(mid), 150f + r * MathF.Sin(mid)));

        AssertThat(hit is not null).IsTrue();
        AssertThat(hit!.RowIndex).IsEqual(-1);       // node arcs are not backed by a single row
        AssertThat(hit.Label).IsEqual("A");
        AssertThat(Approx(hit.ScreenX, 200)).IsTrue();
        AssertThat(Approx(hit.ScreenY, 150)).IsTrue();
    }

    [TestCase]
    public void ChordHitToleranceControlsChordDetection()
    {
        // The first chord (A -> B) starts at its source midpoint (about 0.77 rad). A ray along +X
        // is outside the default 0.2 rad tolerance but inside a 1.0 rad one.
        HitResult? Hit(ChartTheme? theme)
        {
            var (mark, canvas, ctx) = ChordCtx(MarkCases.Relations(), theme);
            mark.RadiusFactor = 0.5f;
            mark.Render(ctx);
            float inner = canvas.Arcs.Min(a => a.Radius);
            float r = inner * 0.5f;   // well inside the chord area
            return mark.HitTest(ctx, new Vector2(200f + r, 150f));
        }

        AssertThat(Hit(null) is null).IsTrue();

        var tolerant = ChartTheme.Dark();
        tolerant.HitTestAngleTolerance = 1.0f;
        var hit = Hit(tolerant);

        AssertThat(hit is not null).IsTrue();
        AssertThat(hit!.Label!.Contains('A')).IsTrue();
        AssertThat(hit.Label.Contains('B')).IsTrue();
        AssertThat(hit.Label.Contains('5')).IsTrue();
    }

    [TestCase]
    public void ChordHitTestStaysInBoundsWhenLayoutAndHitDataDiffer()
    {
        var canvas = new FakeCanvas2D();
        var mark = new ChordMark();

        // The layout is built from three relations...
        var layoutCtx = TestContexts.Mark(canvas, Ring(3), new EncodeSet(), DummyScales());
        mark.Render(layoutCtx);
        AssertThat(canvas.DrewAnything).IsTrue();

        // ...while the hit test runs against a single row, reusing the cached layout.
        var hitCtx = TestContexts.Mark(
            canvas,
            Ring(1),
            new EncodeSet(),
            DummyScales(),
            layoutVersion: layoutCtx.LayoutVersion);

        int hits = 0;
        HitResult? outOfBounds = null;
        for (float y = 0; y <= 300; y += 5)
        {
            for (float x = 0; x <= 400; x += 5)
            {
                var hit = mark.HitTest(hitCtx, new Vector2(x, y));
                if (hit is null) continue;
                hits++;
                // RowIndex -1 is the documented "node arc" hit; only real row indices must be in range.
                if (hit.RowIndex >= hitCtx.Data.Count)
                    outOfBounds = hit;
            }
        }

        AssertThat(hits).IsGreater(0);              // the geometry was actually exercised
        AssertThat(outOfBounds is null).IsTrue();   // and no hit escaped the data range
    }

    // ── Relations with missing endpoints ────────────────────────────────────

    [TestCase]
    public void ChordSkipsRelationsWithNullEndpoints()
    {
        var rows = new List<DataRow>
        {
            D(("source", "A"), ("target", "B"), ("value", 1.0)),
            D(("source", null), ("target", "B"), ("value", 1.0)),
            D(("source", "A"), ("target", null), ("value", 1.0)),
        };
        var canvas = new FakeCanvas2D();
        var ctx = TestContexts.Mark(canvas, rows, new EncodeSet(), DummyScales());

        new ChordMark().Render(ctx);

        AssertThat(canvas.DrewAnything).IsTrue();   // the valid relation still renders
    }

    [TestCase]
    public void ChordWithOnlyNullEndpointsDrawsNothing()
    {
        var rows = new List<DataRow>
        {
            D(("source", null), ("target", null), ("value", 1.0)),
        };
        var canvas = new FakeCanvas2D();
        var ctx = TestContexts.Mark(canvas, rows, new EncodeSet(), DummyScales());

        new ChordMark().Render(ctx);

        AssertThat(canvas.DrewAnything).IsFalse();
    }

    // ── Full-circle sweep safety ────────────────────────────────────────────

    [TestCase]
    public void FullCircleChordWithASingleNodeNeverRequestsACompleteTurn()
    {
        var canvas = new FakeCanvas2D();
        ChordChart(canvas,
        [
            D(("source", "A"), ("target", "A"), ("value", 1.0)),
        ], new ChordMark { ArcGap = 0f }).Render();

        const float tau = MathF.Tau;
        var offenders = canvas.Arcs
            .Where(a => MathF.Abs(a.End - a.Start) >= tau - 1e-6f)
            .ToList();

        AssertThat(offenders.Count).IsEqual(0);
    }

    // ── Layout cache ────────────────────────────────────────────────────────

    [TestCase]
    public void ChordLayoutIsNotRebuiltOnEveryFrame()
    {
        var canvas = new FakeCanvas2D();
        var mark = new ChordMark();
        using var chart = ChordChart(canvas, Relations(0), mark);

        chart.Render();
        chart.Render();

        AssertThat(mark.LayoutBuildCount).IsEqual(1);
    }

    [TestCase]
    public void MarkLocalDataDoesNotReuseTheChartLayout()
    {
        var canvas = new FakeCanvas2D();
        var mark = new ChordMark();
        using var chart = ChordChart(canvas, Relations(0), mark);
        chart.Render();

        mark.Data = Relations(50);
        chart.Render();

        AssertThat(mark.LayoutBuildCount).IsEqual(2);
    }

    // ── shared data helper ──────────────────────────────────────────────────

    private static List<DataRow> Relations(int valueOffset) =>
    [
        D(("source", "A"), ("target", "B"), ("value", 5.0 + valueOffset)),
        D(("source", "B"), ("target", "C"), ("value", 3.0)),
    ];

    /// <summary>
    /// <see cref="ChordMark.ChordOpacity"/> is the chord fill's own opacity (0.4 by default; the node arcs stay
    /// opaque) - which is also why a chord's hover state has to stay in the data layer: repainting a translucent
    /// chord on the overlay would blend it twice.
    /// </summary>
    [TestCase]
    public void ChordOpacityReachesTheChordFill()
    {
        var (mark, canvas, ctx) = ChordCtx(MarkCases.Relations());
        mark.ChordOpacity = 0.25f;
        mark.Render(ctx);

        AssertThat(canvas.FillOpacities).Contains(0.25f);
    }
}
