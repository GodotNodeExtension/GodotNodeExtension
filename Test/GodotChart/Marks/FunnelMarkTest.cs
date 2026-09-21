namespace GodotNodeExtension.Tests.GodotChart.Marks;

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
/// Behaviour specification for <see cref="FunnelMark"/>: stage sizing (min width ratio, stage gap,
/// single stage), the value labels, hit testing, and the degenerate cases (all-zero values, many
/// stages in a short plot).
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class FunnelMarkTest
{

    private static DataRow D(params (string Field, object? Value)[] fields) => TestContexts.Row(fields);

    private static List<DataRow> Stages() =>
    [
        D(("cat", "A"), ("value", 10.0)),
        D(("cat", "B"), ("value", 20.0)),
        D(("cat", "C"), ("value", 15.0)),
    ];

    private static FakeCanvas2D RenderFunnel(FunnelMark mark, List<DataRow> data)
    {
        var canvas = new FakeCanvas2D();
        mark.Render(TestContexts.Mark(canvas, data, TestContexts.XyEncodes("cat", "value"), new ScaleSet()));
        return canvas;
    }

    private static Chart Chart2D(FakeCanvas2D canvas, List<DataRow> data, Mark mark, string x, string y,
                                 float width = 400f, float height = 300f)
    {
        var chart = new Chart(canvas) { Width = width, Height = height };
        chart.Data(data);
        chart.Mark(mark);
        chart.Encode(Channel.X, x);
        chart.Encode(Channel.Y, y);
        return chart;
    }

    // ── Stage sizing ─────────────────────────────────────────────────────────

    [TestCase]
    public void FunnelMinWidthRatioControlsTheNarrowestStageWidth()
    {
        var loose = RenderFunnel(new FunnelMark { MinWidthRatio = 0.5f }, Stages());
        var tight = RenderFunnel(new FunnelMark { MinWidthRatio = 0.15f }, Stages());

        float looseMin = loose.RoundRects.Min(r => r.W);
        float tightMin = tight.RoundRects.Min(r => r.W);

        // Narrowest stage is the smallest value (10/20 = 0.5 of the plot width).
        AssertThat(Approx(looseMin, 400f * (0.5f + 0.5f * 0.5f))).IsTrue();
        AssertThat(looseMin > tightMin).IsTrue();
    }

    [TestCase]
    public void FunnelStageGapControlsTheVerticalSpacing()
    {
        var tight = RenderFunnel(new FunnelMark { StageGap = 0f }, Stages());
        var spaced = RenderFunnel(new FunnelMark { StageGap = 12f }, Stages());

        float tightStep = tight.RoundRects[1].Y - tight.RoundRects[0].Y;
        float spacedStep = spaced.RoundRects[1].Y - spaced.RoundRects[0].Y;

        AssertThat(spacedStep > tightStep).IsTrue();
    }

    [TestCase]
    public void FunnelSingleStageFillsThePlotHeight()
    {
        var data = new List<DataRow> { D(("cat", "A"), ("value", 10.0)) };
        var canvas = RenderFunnel(new FunnelMark(), data);

        AssertThat(canvas.RoundRects.Count).IsEqual(1);
        AssertThat(Approx(canvas.RoundRects[0].H, 300f)).IsTrue();
    }

    // ── Labels ───────────────────────────────────────────────────────────────

    [TestCase]
    public void FunnelShowLabelTogglesTheStageLabels()
    {
        var on = RenderFunnel(new FunnelMark { ShowLabel = true }, Stages());
        var off = RenderFunnel(new FunnelMark { ShowLabel = false }, Stages());

        AssertThat(on.Texts.Count).IsEqual(3);        // one label per stage
        AssertThat(off.Texts.Count).IsEqual(0);
    }

    [TestCase]
    public void FunnelLabelsFollowTheLabelFormat()
    {
        var canvas = RenderFunnel(new FunnelMark { LabelFormat = "{0}%" }, Stages());

        // {0} is the stage value, keeping the category prefix of the default label.
        AssertThat(canvas.Texts).Contains("A: 10%");
        AssertThat(canvas.Texts).Contains("B: 20%");

        var plain = RenderFunnel(new FunnelMark(), Stages());
        AssertThat(plain.Texts).Contains("A: 10");
    }

    // ── Hit testing ──────────────────────────────────────────────────────────

    [TestCase]
    public void FunnelHitTestAtAStageCentreReturnsItsRowIndex()
    {
        var mark = new FunnelMark();
        var ctx = TestContexts.Mark(
            new FakeCanvas2D(), Stages(), TestContexts.XyEncodes("cat", "value"), new ScaleSet());

        // stage height = (300 - 2*4)/3 = 97.33, stage 0 runs 0..97.33 and is centred on x = 200
        var hit = mark.HitTest(ctx, new Vector2(200f, 48f));

        AssertThat(hit is not null).IsTrue();
        AssertThat(hit!.RowIndex).IsEqual(0);
    }

    // ── Layout cache ─────────────────────────────────────────────────────────

    /// <summary>
    /// The cached maximum is keyed with <c>Mark.CacheKey</c> (owner + layout version + data list +
    /// hidden series), not with the layout version alone: a second chart that reuses the same mark
    /// instance has its own layout version, and a stale maximum stretched its stages.
    /// </summary>
    [TestCase]
    public void FunnelRebuildsItsMaxValueWhenTheDataListChanges()
    {
        var mark = new FunnelMark();

        // Contexts built by hand share the same DataVersion/LayoutVersion, so only the data list
        // instance distinguishes them - exactly the second chart reusing this mark.
        var first = RenderFunnel(mark, Stages());            // max 20
        var second = RenderFunnel(mark,
        [
            D(("cat", "A"), ("value", 10.0)),
            D(("cat", "B"), ("value", 40.0)),                // max 40
            D(("cat", "C"), ("value", 15.0)),
        ]);

        float firstWidest = first.RoundRects.Max(r => r.W);
        float secondWidest = second.RoundRects.Max(r => r.W);

        // The widest stage is the one holding the maximum value: it fills the plot width either way...
        AssertThat(Approx(firstWidest, 400f)).IsTrue();
        // ...but with the first context's maximum still cached, the 40 was read as 2x the peak and
        // drew far past the plot.
        AssertThat(Approx(secondWidest, 400f)).IsTrue();
        AssertThat(Approx(second.RoundRects[0].W, 400f * (0.15f + 0.85f * 0.25f))).IsTrue();
    }

    // ── Degenerate data ──────────────────────────────────────────────────────

    [TestCase]
    public void FunnelAllZeroValuesDrawNothing()
    {
        var data = new List<DataRow>
        {
            D(("cat", "A"), ("value", 0.0)),
            D(("cat", "B"), ("value", 0.0)),
        };
        var canvas = RenderFunnel(new FunnelMark(), data);

        AssertThat(canvas.FillCount).IsEqual(0);
    }

    [TestCase]
    public void FunnelWithManyStagesKeepsPositiveStageHeights()
    {
        var rows = new List<DataRow>();
        for (int i = 0; i < 40; i++)
            rows.Add(D(("stage", $"S{i}"), ("value", 40.0 - i)));

        var canvas = new FakeCanvas2D();
        using var chart = Chart2D(canvas, rows, new FunnelMark(), "stage", "value");

        chart.Render();

        AssertThat(canvas.NegativeSizeRectCount).IsEqual(0);
    }
}
