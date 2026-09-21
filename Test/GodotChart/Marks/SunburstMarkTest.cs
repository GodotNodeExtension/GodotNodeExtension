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
/// Behaviour specification for <see cref="SunburstMark"/>: the ring geometry built from the parent
/// hierarchy, <see cref="SunburstMark.ParentField"/>, radii/ring-gap options, labels, clipping and
/// canvas state, hit testing, deep hierarchies, cycle breaking and the layout cache.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class SunburstMarkTest
{
    // ── helpers ─────────────────────────────────────────────────────────────

    /// <summary>Local floating point comparison: mark geometry is never bit-exact.</summary>
    private static bool Approx(double a, double b, double tol = 0.01) => Math.Abs(a - b) <= tol;

    private static DataRow D(params (string Field, object? Value)[] fields) => TestContexts.Row(fields);

    /// <summary>Minimal scale set: the sunburst reads the hierarchy, not the axes.</summary>
    private static ScaleSet DummyScales()
    {
        var scales = new ScaleSet();
        scales.Set(Channel.X, new OrdinalScale());
        scales.Set(Channel.Y, new LinearScale(0, 10));
        return scales;
    }

    private static (SunburstMark Mark, FakeCanvas2D Canvas, MarkContext Ctx) SunburstCtx(List<DataRow> rows)
    {
        var canvas = new FakeCanvas2D();
        var ctx = TestContexts.Mark(canvas, rows, TestContexts.XyEncodes("label", "value"), DummyScales());
        return (new SunburstMark(), canvas, ctx);
    }

    private static Chart SunburstChart(FakeCanvas2D canvas, List<DataRow> rows, Mark mark)
    {
        var chart = new Chart(canvas) { Width = 400f, Height = 300f };
        chart.Data(rows);
        chart.Mark(mark);
        chart.Encode(Channel.X, "label");
        chart.Encode(Channel.Y, "value");
        return chart;
    }

    // ── Default rendering ───────────────────────────────────────────────────

    [TestCase]
    public void SunburstRootRingCoversTheFullCircle()
    {
        var rows = new List<DataRow>
        {
            D(("label", "r1"), ("parent", ""), ("value", 6.0)),
            D(("label", "r2"), ("parent", ""), ("value", 4.0)),
        };
        var (mark, canvas, ctx) = SunburstCtx(rows);
        mark.Render(ctx);

        float outer = canvas.Arcs.Max(a => a.Radius);
        double sweep = canvas.Arcs
            .Where(a => Approx(a.Radius, outer) && a.End > a.Start)
            .Sum(a => (double)(a.End - a.Start));

        // The two roots share the whole circle (only the root gaps are cut out).
        AssertThat(Approx(sweep, MathF.Tau, 0.05)).IsTrue();
        AssertThat(canvas.NegativeSizeRectCount).IsEqual(0);
    }

    /// <summary>
    /// A negative value is clamped to 0 (the same rule <see cref="TreemapMark"/> documents) instead of
    /// shrinking the layer's total: with a smaller total the sweeps of the positive siblings added up to more
    /// than the span their parent handed them, so the arcs overran their parent's sector and the hit test
    /// reported a node the reader never saw.
    /// </summary>
    [TestCase]
    public void NegativeValueContributesNothingInsteadOfOverrunningTheParentSweep()
    {
        var withNegative = new List<DataRow>
        {
            D(("label", "A"), ("parent", ""), ("value", 10.0)),
            D(("label", "B"), ("parent", ""), ("value", -5.0)),
            D(("label", "C"), ("parent", ""), ("value", 10.0)),
        };
        // What the clamped row must look like: a row without a value takes part in neither the layout nor
        // the drawing (a null value is 0 on purpose, so a pure grouping node still derives its own).
        var withoutTheNegativeRow = new List<DataRow>
        {
            D(("label", "A"), ("parent", ""), ("value", 10.0)),
            D(("label", "C"), ("parent", ""), ("value", 10.0)),
        };

        var (mark, canvas, ctx) = SunburstCtx(withNegative);
        mark.Render(ctx);
        var (baselineMark, baseline, baselineCtx) = SunburstCtx(withoutTheNegativeRow);
        baselineMark.Render(baselineCtx);

        var problems = new List<string>();
        if (canvas.Arcs.Count != baseline.Arcs.Count)
        {
            problems.Add($"{canvas.Arcs.Count} arc(s) instead of {baseline.Arcs.Count}");
        }
        for (int i = 0; i < Math.Min(canvas.Arcs.Count, baseline.Arcs.Count); i++)
        {
            if (!Approx(canvas.Arcs[i].Start, baseline.Arcs[i].Start, 1e-4) ||
                !Approx(canvas.Arcs[i].End, baseline.Arcs[i].End, 1e-4))
            {
                problems.Add($"arc {i}: {canvas.Arcs[i]} instead of {baseline.Arcs[i]}");
            }
        }
        AssertThat(string.Join("; ", problems)).IsEqual("");
    }

    [TestCase]
    public void RootArcsTakeOnePaletteColorEachAndChildrenShadeWithDepth()
    {
        var rows = new List<DataRow>
        {
            D(("label", "A"), ("value", 10.0)),
            D(("label", "B"), ("value", 10.0)),
            D(("label", "A1"), ("parent", "A"), ("value", 5.0)),
            D(("label", "A2"), ("parent", "A"), ("value", 5.0)),
        };
        var palette = ChartTheme.DefaultPalette;

        // Arcs are filled branch-first: A, then A's children, then B.
        var (plain, plainCanvas, plainCtx) = SunburstCtx(rows);
        plain.DepthShadeStep = 0f;              // shading off: one flat colour per branch
        plain.Render(plainCtx);

        AssertThat(plainCanvas.FillColors[0].ToHtml()).IsEqual(palette[0].ToHtml());
        AssertThat(plainCanvas.FillColors[1].ToHtml()).IsEqual(palette[0].ToHtml());
        AssertThat(plainCanvas.FillColors[2].ToHtml()).IsEqual(palette[0].ToHtml());
        AssertThat(plainCanvas.FillColors[3].ToHtml()).IsEqual(palette[1].ToHtml());

        var (shaded, shadedCanvas, shadedCtx) = SunburstCtx(rows);
        shaded.DepthShadeStep = 0.2f;
        shaded.Render(shadedCtx);

        // The inner ring keeps the branch colour; a deeper ring is the same hue, one step darker.
        AssertThat(shadedCanvas.FillColors[0].ToHtml()).IsEqual(palette[0].ToHtml());
        AssertThat(shadedCanvas.FillColors[1].ToHtml()).IsNotEqual(palette[0].ToHtml());
        AssertThat(shadedCanvas.FillColors[1].R < palette[0].R).IsTrue();
        AssertThat(shadedCanvas.FillColors[3].ToHtml()).IsEqual(palette[1].ToHtml());
    }

    [TestCase]
    public void ASingleRootIsTheFrameAndItsChildrenAreTheBranches()
    {
        var rows = new List<DataRow>
        {
            D(("label", "total"), ("value", 0.0)),
            D(("label", "a"), ("parent", "total"), ("value", 10.0)),
            D(("label", "b"), ("parent", "total"), ("value", 10.0)),
            D(("label", "c"), ("parent", "total"), ("value", 10.0)),
        };
        var (mark, canvas, ctx) = SunburstCtx(rows);
        mark.DepthShadeStep = 0f;
        mark.Render(ctx);

        // A lone root is the frame, not a branch: the chart would otherwise collapse into one colour.
        // The frame keeps the neutral mark colour, the three children take the first palette entries.
        var palette = ChartTheme.DefaultPalette;
        var fills = canvas.FillColors.Select(c => c.ToHtml()).ToList();

        AssertThat(fills.Count).IsEqual(4);
        AssertThat(fills[0]).IsEqual(new Color(0.29f, 0.59f, 0.98f).ToHtml());
        AssertThat(fills[1]).IsEqual(palette[0].ToHtml());
        AssertThat(fills[2]).IsEqual(palette[1].ToHtml());
        AssertThat(fills[3]).IsEqual(palette[2].ToHtml());
    }

    [TestCase]
    public void SunburstLabelsFollowShowLabelAndTheSweepThreshold()
    {
        var rows = new List<DataRow>
        {
            D(("label", "big"), ("parent", ""), ("value", 99.0)),
            D(("label", "tiny"), ("parent", ""), ("value", 1.0)),
        };
        var (mark, canvas, ctx) = SunburstCtx(rows);
        mark.Render(ctx);

        AssertThat(canvas.Texts).Contains("big");
        // A 1% slice sweeps about 0.06 rad, below the default SunburstLabelMinSweep of 0.15.
        AssertThat(canvas.Texts.Contains("tiny")).IsFalse();

        var (quiet, quietCanvas, quietCtx) = SunburstCtx(MarkCases.Hierarchy());
        quiet.ShowLabel = false;
        quiet.Render(quietCtx);
        AssertThat(quietCanvas.Texts.Count).IsEqual(0);
    }

    [TestCase]
    public void SunburstLabelsFollowTheLabelFormat()
    {
        var (mark, canvas, ctx) = SunburstCtx(MarkCases.Hierarchy());
        mark.LabelFormat = "{0} ({1})";
        mark.Render(ctx);

        // {0} is the node label, {1} its value (the root of a group takes the sum of its children).
        AssertThat(canvas.Texts).Contains("root (10)");
        AssertThat(canvas.Texts).Contains("child1 (6)");
        AssertThat(canvas.Texts).Contains("child2 (4)");
    }

    [TestCase]
    public void SunburstKeepsSaveRestoreBalanced()
    {
        var (mark, canvas, ctx) = SunburstCtx(MarkCases.Hierarchy());
        mark.Render(ctx);

        // Clipping the marks to the plot rectangle is the chart's job (see Chart.Render.cs), and the test
        // double deliberately does not implement clipping - what a mark itself has to keep straight is the
        // save/restore pair it uses for its own transforms.
        AssertThat(canvas.SaveCount).IsEqual(1);
        AssertThat(canvas.SaveRestoreBalanced).IsTrue();

        var (empty, emptyCanvas, emptyCtx) = SunburstCtx([]);
        empty.Render(emptyCtx);
        AssertThat(emptyCanvas.DrewAnything).IsFalse();
        AssertThat(emptyCanvas.SaveCount).IsEqual(0);
    }

    // ── Options ─────────────────────────────────────────────────────────────

    [TestCase]
    public void SunburstHonoursACustomParentField()
    {
        var rows = new List<DataRow>
        {
            D(("label", "root"), ("parentKey", ""), ("value", 0.0)),
            D(("label", "kid"), ("parentKey", "root"), ("value", 5.0)),
        };

        int DistinctRadii(string parentField)
        {
            var (mark, canvas, ctx) = SunburstCtx(rows);
            mark.ParentField = parentField;
            mark.Render(ctx);
            return canvas.Arcs.Select(a => MathF.Round(a.Radius, 2)).Distinct().Count();
        }

        // Two levels => two rings (four distinct radii: inner/outer of each ring).
        AssertThat(DistinctRadii("parentKey")).IsEqual(4);
        // Without a usable parent field both rows are roots => a single ring (two radii).
        AssertThat(DistinctRadii("notAField")).IsEqual(2);
    }

    [TestCase]
    public void SunburstRadiusOptionsSetTheInnerAndOuterRadius()
    {
        var rows = new List<DataRow> { D(("label", "root"), ("parent", ""), ("value", 6.0)) };

        (float Inner, float Outer) Radii(float radiusFactor, float innerRatio)
        {
            var (mark, canvas, ctx) = SunburstCtx(rows);
            mark.RadiusFactor = radiusFactor;
            mark.InnerRadiusRatio = innerRatio;
            mark.Render(ctx);
            return (canvas.Arcs.Min(a => a.Radius), canvas.Arcs.Max(a => a.Radius));
        }

        var (inner, outer) = Radii(0.9f, 0.15f);
        AssertThat(Approx(outer, 135)).IsTrue();          // 150 * 0.9
        AssertThat(Approx(inner, 135 * 0.15f)).IsTrue();

        var (inner2, outer2) = Radii(0.5f, 0.4f);
        AssertThat(Approx(outer2, 75)).IsTrue();          // 150 * 0.5
        AssertThat(Approx(inner2, 75 * 0.4f)).IsTrue();
    }

    [TestCase]
    public void SunburstRingGapSeparatesTheRings()
    {
        var rows = new List<DataRow>
        {
            D(("label", "root"), ("parent", ""), ("value", 0.0)),
            D(("label", "kid"), ("parent", "root"), ("value", 5.0)),
        };

        float SmallestRadiusGap(float ringGap)
        {
            var (mark, canvas, ctx) = SunburstCtx(rows);
            mark.RingGap = ringGap;
            mark.Render(ctx);

            var radii = canvas.Arcs.Select(a => a.Radius).Distinct().OrderBy(r => r).ToList();
            float min = float.MaxValue;
            for (int i = 1; i < radii.Count; i++)
                min = MathF.Min(min, radii[i] - radii[i - 1]);
            return min;
        }

        // The gap between the outermost radius of one ring and the innermost of the next is RingGap.
        AssertThat(Approx(SmallestRadiusGap(2f), 2)).IsTrue();
        AssertThat(Approx(SmallestRadiusGap(20f), 20)).IsTrue();
    }

    // ── Hit testing ─────────────────────────────────────────────────────────

    [TestCase]
    public void SunburstHitTestReturnsThePlotCentre()
    {
        var rows = new List<DataRow>
        {
            D(("label", "A"), ("parent", ""), ("value", 5.0)),
            D(("label", "B"), ("parent", ""), ("value", 5.0)),
        };
        var (mark, canvas, ctx) = SunburstCtx(rows);
        mark.Render(ctx);

        float outer = canvas.Arcs.Max(a => a.Radius);
        float inner = canvas.Arcs.Min(a => a.Radius);
        var firstBand = canvas.Arcs.First(a => Approx(a.Radius, outer) && a.End > a.Start);
        float mid = (firstBand.Start + firstBand.End) / 2f;
        float r = (outer + inner) / 2f;

        var hit = mark.HitTest(ctx, new Vector2(200f + r * MathF.Cos(mid), 150f + r * MathF.Sin(mid)));

        AssertThat(hit is not null).IsTrue();
        AssertThat(Approx(hit!.ScreenX, 200)).IsTrue();
        AssertThat(Approx(hit.ScreenY, 150)).IsTrue();
        AssertThat(hit.Label).IsEqual("A: 5");
    }

    // ── Deep hierarchies ────────────────────────────────────────────────────

    [TestCase]
    public void DeepSunburstKeepsPositiveRingWidths()
    {
        var rows = new List<DataRow> { D(("label", "L0"), ("parent", ""), ("value", 0.0)) };
        for (int depth = 1; depth <= 8; depth++)
            rows.Add(D(("label", $"L{depth}"), ("parent", $"L{depth - 1}"), ("value", 10.0)));

        var canvas = new FakeCanvas2D();
        SunburstChart(canvas, rows, new SunburstMark { RingGap = 20f }).Render();

        AssertThat(canvas.NegativeSizeRectCount).IsEqual(0);
        AssertThat(canvas.NonFiniteCoordinateCount).IsEqual(0);
    }

    // ── Cycle breaking (pure hierarchy logic) ───────────────────────────────

    private static SunburstMark.SunburstNode Node(string label, string parentKey = "")
        => new() { Label = label, ParentKey = parentKey, Value = 1, Row = new DataRow(), RowIndex = 0 };

    private static Dictionary<string, SunburstMark.SunburstNode> Map(
        params SunburstMark.SunburstNode[] nodes)
    {
        var map = new Dictionary<string, SunburstMark.SunburstNode>();
        foreach (var node in nodes)
            map[node.ParentKey.Length == 0 ? node.Label : $"{node.ParentKey}/{node.Label}"] = node;
        return map;
    }

    [TestCase]
    public void ValidForestIsLeftUntouched()
    {
        var root = Node("root");
        var child = Node("child", "root");
        root.Children.Add(child);
        var roots = new List<SunburstMark.SunburstNode> { root };

        int broken = SunburstMark.BreakCycles(Map(root, child), roots);

        AssertThat(broken).IsEqual(0);
        AssertThat(roots.Count).IsEqual(1);
        AssertThat(root.Children.Count).IsEqual(1);
    }

    [TestCase]
    public void SelfReferencingParentIsDetachedAndPromotedToRoot()
    {
        var self = Node("self", "self");
        self.Children.Add(self); // parent.Children.Add(node) with parent == node
        var roots = new List<SunburstMark.SunburstNode>();

        int broken = SunburstMark.BreakCycles(Map(self), roots);

        AssertThat(broken).IsEqual(1);
        AssertThat(roots.Count).IsEqual(1);
        AssertThat(ReferenceEquals(roots[0], self)).IsTrue();
        AssertThat(self.Children.Count).IsEqual(0);
    }

    [TestCase]
    public void TwoNodeCycleStaysReachableAfterBreaking()
    {
        var a = Node("a", "b");
        var b = Node("b", "a");
        a.Children.Add(b); // b's parent is a
        b.Children.Add(a); // a's parent is b -> cycle a <-> b
        var roots = new List<SunburstMark.SunburstNode>();

        int broken = SunburstMark.BreakCycles(Map(a, b), roots);

        AssertThat(broken).IsEqual(1);
        AssertThat(roots.Count).IsEqual(1);
        // The promoted node keeps the other loop member as its child, so nothing is dropped
        // and the remaining structure is a proper tree (a -> b).
        var promoted = roots[0];
        AssertThat(ReferenceEquals(promoted, a)).IsTrue();
        AssertThat(promoted.Children.Count).IsEqual(1);
        AssertThat(ReferenceEquals(promoted.Children[0], b)).IsTrue();
        AssertThat(b.Children.Count).IsEqual(0);
    }

    [TestCase]
    public void PureCycleWithoutValidRootIsBroken()
    {
        // mid <-> leaf form a cycle; root is an unrelated valid tree.
        var root = Node("root");
        var mid = Node("mid", "leaf");
        var leaf = Node("leaf", "mid");
        mid.Children.Add(leaf);
        leaf.Children.Add(mid);
        var roots = new List<SunburstMark.SunburstNode> { root };

        int broken = SunburstMark.BreakCycles(Map(root, mid, leaf), roots);

        AssertThat(broken).IsEqual(1);
        // One cycle member is promoted; the loop is gone and both members stay reachable.
        AssertThat(roots.Count).IsEqual(2);
        AssertThat(root.Children.Count).IsEqual(0);
        var promoted = roots[1];
        AssertThat(promoted.Children.Count).IsEqual(1);
    }

    // ── Layout cache ────────────────────────────────────────────────────────

    [TestCase]
    public void SunburstTreeIsNotRebuiltOnEveryFrame()
    {
        var canvas = new FakeCanvas2D();
        var mark = new SunburstMark();
        var chart = SunburstChart(canvas, Hierarchy(), mark);

        chart.Render();
        chart.Render();

        AssertThat(mark.LayoutBuildCount).IsEqual(1);
    }

    [TestCase]
    public void TwoChartsSharingAMarkDoNotShareLayouts()
    {
        var mark = new SunburstMark();

        var canvasA = new FakeCanvas2D();
        var chartA = SunburstChart(canvasA, Hierarchy(), mark);
        chartA.Render();

        var canvasB = new FakeCanvas2D();
        var chartB = SunburstChart(canvasB, Hierarchy(), mark);
        chartB.Render();

        // Both charts must have rendered, each with its own layout.
        AssertThat(mark.LayoutBuildCount).IsEqual(2);
        AssertThat(canvasA.FillCount > 0).IsTrue();
        AssertThat(canvasB.FillCount > 0).IsTrue();
    }

    // ── shared data helper ──────────────────────────────────────────────────

    private static List<DataRow> Hierarchy() =>
    [
        D(("label", "root"), ("parent", ""), ("value", 0.0)),
        D(("label", "child1"), ("parent", "root"), ("value", 6.0)),
        D(("label", "child2"), ("parent", "root"), ("value", 4.0)),
    ];
}
