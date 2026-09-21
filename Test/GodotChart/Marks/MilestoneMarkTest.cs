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
/// Behaviour specification for <see cref="MilestoneMark"/> - the event timeline: where a marker lands on
/// the position axis, the per-lane lines, the label source and the alternating label sides, the marker
/// symbol from the Shape channel, selection and hover, the hit test, and the degenerate inputs.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class MilestoneMarkTest
{
    private static readonly string[] ShapeKeys = { "a", "b" };
    private static readonly string[] PhaseKeys = { "Alpha", "Beta" };

    /// <summary>Local floating point comparison: mark geometry is never bit-exact.</summary>
    private static bool Approx(double a, double b, double tol = 0.01) => Math.Abs(a - b) <= tol;

    private static DataRow D(params (string Field, object? Value)[] fields) => TestContexts.Row(fields);

    /// <summary>Scales for an event timeline: a numeric position axis and optional ordinal lanes.</summary>
    private static ScaleSet Scales(double xMax = 12, params string[] lanes)
    {
        var scales = new ScaleSet();
        scales.Set(Channel.X, new LinearScale(0, xMax));
        if (lanes.Length > 0)
        {
            var laneScale = new OrdinalScale();
            laneScale.Fit(lanes);
            scales.Set(Channel.Y, laneScale);
        }
        return scales;
    }

    private static EncodeSet PositionOnly() 
    {
        var encodes = new EncodeSet();
        encodes.Set(Channel.X, new FieldEncode("day"));
        return encodes;
    }

    private static (MilestoneMark Mark, FakeCanvas2D Canvas, MarkContext Ctx) Milestone(
        List<DataRow> rows, ScaleSet? scales = null, EncodeSet? encodes = null,
        int hovered = -1, int selected = -1)
    {
        var canvas = new FakeCanvas2D();
        var ctx = TestContexts.Mark(canvas, rows, encodes ?? PositionOnly(), scales ?? Scales(),
            hoveredRowIndex: hovered, selectedRowIndex: selected);
        return (new MilestoneMark(), canvas, ctx);
    }

    // ── Placement ───────────────────────────────────────────────────────────

    [TestCase]
    public void AMarkerSitsAtItsPositionOnTheMiddleLine()
    {
        var (mark, canvas, ctx) = Milestone([D(("day", 6.0))]);
        mark.Render(ctx);

        // day 6 of the 0..12 axis is the middle of the 400px plot, and without lanes the middle line is
        // the vertical centre.
        AssertThat(canvas.Circles.Count).IsEqual(1);
        AssertThat(Approx(canvas.Circles[0].Cx, 200)).IsTrue();
        AssertThat(Approx(canvas.Circles[0].Cy, 150)).IsTrue();
        AssertThat(Approx(canvas.Circles[0].Radius, 6)).IsTrue();
    }

    [TestCase]
    public void LanesPutEachMarkerOnItsOwnLine()
    {
        var rows = new List<DataRow>
        {
            D(("day", 3.0), ("lane", "Release")),
            D(("day", 9.0), ("lane", "Infra")),
        };
        var (mark, canvas, ctx) = Milestone(rows,
            Scales(12, "Release", "Infra"), TestContexts.XyEncodes("day", "lane"));
        mark.Render(ctx);

        AssertThat(canvas.Circles.Count).IsEqual(2);
        // Lanes follow the ordinal scale like TimelineMark does: the first category of the domain sits in
        // the lower half of the plot ((0 + 0.5) / 2), the second in the upper half.
        AssertThat(Approx(canvas.Circles[0].Cy, 225)).IsTrue();
        AssertThat(Approx(canvas.Circles[1].Cy, 75)).IsTrue();
        AssertThat(Approx(canvas.Circles[0].Cx, 100)).IsTrue();
        AssertThat(Approx(canvas.Circles[1].Cx, 300)).IsTrue();
    }

    [TestCase]
    public void TheAxisLineIsDrawnOncePerLaneAndCanBeSwitchedOff()
    {
        var rows = new List<DataRow>
        {
            D(("day", 3.0), ("lane", "Release")),
            D(("day", 9.0), ("lane", "Release")),
            D(("day", 6.0), ("lane", "Infra")),
        };
        var encodes = TestContexts.XyEncodes("day", "lane");

        var (laned, lanedCanvas, lanedCtx) = Milestone(rows, Scales(12, "Release", "Infra"), encodes);
        laned.Render(lanedCtx);
        AssertThat(lanedCanvas.StrokeCount).IsEqual(2);   // one line per lane, not per event

        var (plain, plainCanvas, plainCtx) = Milestone(rows, Scales(), PositionOnly());
        plain.Render(plainCtx);
        AssertThat(plainCanvas.StrokeCount).IsEqual(1);   // a single centre line

        var (quiet, quietCanvas, quietCtx) = Milestone(rows, Scales(12, "Release", "Infra"), encodes);
        quiet.ShowAxisLine = false;
        quiet.Render(quietCtx);
        AssertThat(quietCanvas.StrokeCount).IsEqual(0);
    }

    // ── Labels ──────────────────────────────────────────────────────────────

    [TestCase]
    public void TheLabelComesFromTheChannelThenTheFieldThenThePosition()
    {
        var rows = new List<DataRow>
        {
            D(("day", 2.0), ("label", "from field"), ("caption", "from channel")),
            D(("day", 6.0), ("label", "from field")),
            D(("day", 10.0)),
        };
        var encodes = PositionOnly();
        encodes.Set(Channel.Label, new FieldEncode("caption"));
        var (mark, canvas, ctx) = Milestone(rows, Scales(), encodes);
        mark.Render(ctx);

        AssertThat(canvas.Texts).Contains("from channel");
        AssertThat(canvas.Texts).Contains("from field");
        AssertThat(canvas.Texts).Contains("10");           // falls back to the position value
    }

    [TestCase]
    public void TheEventLabelFollowsTheLabelFormat()
    {
        var rows = new List<DataRow> { D(("day", 3.0), ("label", "v1.0")) };
        var (mark, canvas, ctx) = Milestone(rows);
        mark.LabelFormat = "{0} @ {1}";
        mark.Render(ctx);

        // {0} is the event text, {1} its position on the axis.
        AssertThat(canvas.Texts).Contains("v1.0 @ 3");

        var (plain, plainCanvas, plainCtx) = Milestone(rows);
        plain.Render(plainCtx);
        AssertThat(plainCanvas.Texts).Contains("v1.0");
    }

    [TestCase]
    public void LabelsAlternateAboveAndBelowTheirMarker()
    {
        var rows = new List<DataRow>
        {
            D(("day", 3.0), ("label", "first")),
            D(("day", 9.0), ("label", "second")),
        };
        var (mark, canvas, ctx) = Milestone(rows);
        mark.Render(ctx);

        var first = canvas.TextDraws.First(d => d.Text == "first");
        var second = canvas.TextDraws.First(d => d.Text == "second");

        // The first label sits above the middle line (150), the second below it.
        AssertThat(first.Y < 150f).IsTrue();
        AssertThat(second.Y >= 150f).IsTrue();

        // ... and with the alternation off both stay on the same side.
        var (stacked, stackedCanvas, stackedCtx) = Milestone(rows);
        stacked.AlternateLabels = false;
        stacked.Render(stackedCtx);
        AssertThat(stackedCanvas.TextDraws.Select(d => d.Y).Distinct().Count()).IsEqual(1);
    }

    [TestCase]
    public void LabelsCanBeSwitchedOff()
    {
        var rows = new List<DataRow> { D(("day", 3.0), ("label", "hidden")) };
        var (mark, canvas, ctx) = Milestone(rows);
        mark.ShowLabel = false;
        mark.Render(ctx);

        AssertThat(canvas.Texts.Count).IsEqual(0);
        AssertThat(canvas.Circles.Count).IsEqual(1);   // the marker itself is still drawn
    }

    // ── Marker style ────────────────────────────────────────────────────────

    [TestCase]
    public void TheMarkerSymbolFollowsTheShapeChannelAndItsOwnDefault()
    {
        var rows = new List<DataRow>
        {
            D(("day", 3.0), ("kind", "a")),
            D(("day", 9.0), ("kind", "b")),
        };
        var shapeScale = new ShapeScale();
        shapeScale.Fit(ShapeKeys);

        var scales = Scales();
        scales.Set(Channel.Shape, shapeScale);
        var encodes = PositionOnly();
        encodes.Set(Channel.Shape, new FieldEncode("kind"));

        var (mark, canvas, ctx) = Milestone(rows, scales, encodes);
        mark.Render(ctx);

        AssertThat(canvas.Circles.Count).IsEqual(1);   // "a" is a disc
        AssertThat(canvas.Rects.Count).IsEqual(1);     // "b" is a square

        // Without the channel the mark's own default symbol is used.
        var (diamond, diamondCanvas, diamondCtx) = Milestone([D(("day", 6.0))]);
        diamond.MarkerShape = ShapeKind.Diamond;
        diamond.Render(diamondCtx);
        AssertThat(diamondCanvas.Circles.Count).IsEqual(0);
        AssertThat(diamondCanvas.PathOpCount > 0).IsTrue();
    }

    [TestCase]
    public void TheSelectedEventGetsTheSelectionRing()
    {
        var rows = new List<DataRow> { D(("day", 6.0)), D(("day", 9.0)) };
        var (mark, canvas, ctx) = Milestone(rows, selected: 1);
        mark.Render(ctx);

        // The centre line plus the ring of the selected marker.
        AssertThat(canvas.StrokeCount).IsEqual(2);
    }

    // ── Interaction ─────────────────────────────────────────────────────────

    [TestCase]
    public void HitTestReportsTheEventUnderTheCursor()
    {
        var rows = new List<DataRow>
        {
            D(("day", 3.0), ("label", "early")),
            D(("day", 9.0), ("label", "late")),
        };
        var (mark, _, ctx) = Milestone(rows);
        mark.Render(ctx);

        var hit = mark.HitTest(ctx, new Vector2(300f, 150f));
        AssertThat(hit is not null).IsTrue();
        AssertThat(hit!.RowIndex).IsEqual(1);
        AssertThat(hit.Label).IsEqual("late");
        AssertThat(hit.MarkType).IsEqual(nameof(MilestoneMark));

        // Far from every marker (the tolerance is the radius plus the theme padding).
        AssertThat(mark.HitTest(ctx, new Vector2(300f, 260f)) is null).IsTrue();
    }

    [TestCase]
    public void ACategoryAxisPlacesEventsOnTheirSlots()
    {
        // A phase timeline: the position axis does not have to be numeric - an ordinal axis puts each
        // event on its category slot.
        var rows = new List<DataRow>
        {
            D(("phase", "Alpha"), ("label", "kickoff")),
            D(("phase", "Beta"), ("label", "feature freeze")),
        };
        var phase = new OrdinalScale();
        phase.Fit(PhaseKeys);
        var scales = new ScaleSet();
        scales.Set(Channel.X, phase);

        var encodes = new EncodeSet();
        encodes.Set(Channel.X, new FieldEncode("phase"));

        var (mark, canvas, ctx) = Milestone(rows, scales, encodes);
        mark.Render(ctx);

        AssertThat(canvas.Circles.Count).IsEqual(2);
        AssertThat(Approx(canvas.Circles[0].Cx, 100)).IsTrue();   // (0 + 0.5) / 2 of the plot
        AssertThat(Approx(canvas.Circles[1].Cx, 300)).IsTrue();   // (1 + 0.5) / 2
        AssertThat(canvas.Texts).Contains("feature freeze");
    }

    [TestCase]
    public void ATimeScalePlacesEventsByDate()
    {
        // The advertised use case: a real time axis. The event dates are what the scale maps, so the
        // events land at their position on the axis.
        var start = new DateTime(2024, 1, 1);
        var end = start.AddDays(10);
        var rows = new List<DataRow>
        {
            D(("when", start), ("label", "kickoff")),
            D(("when", end), ("label", "release")),
        };

        var timeScale = new TimeScale(start, end);
        var scales = new ScaleSet();
        scales.Set(Channel.X, timeScale);

        var encodes = new EncodeSet();
        encodes.Set(Channel.X, new FieldEncode("when"));

        var (mark, canvas, ctx) = Milestone(rows, scales, encodes);
        mark.Render(ctx);

        AssertThat(canvas.Circles.Count).IsEqual(2);
        AssertThat(Approx(canvas.Circles[0].Cx, 0)).IsTrue();      // the first date is the axis minimum
        AssertThat(Approx(canvas.Circles[1].Cx, 400)).IsTrue();    // the last one the axis maximum
    }

    [TestCase]
    public void MilestoneSkipsHiddenSeriesAndBadPositions()
    {
        var rows = new List<DataRow>
        {
            D(("day", 3.0), ("series", "keep")),
            D(("day", 6.0), ("series", "gone")),
            D(("day", double.NaN), ("series", "keep")),
        };
        var encodes = PositionOnly();
        encodes.Set(Channel.Color, new FieldEncode("series"));

        var canvas = new FakeCanvas2D();
        var ctx = TestContexts.Mark(canvas, rows, encodes, Scales(),
            hiddenSeries: new HashSet<string> { "gone" });

        new MilestoneMark().Render(ctx);

        AssertThat(canvas.Circles.Count).IsEqual(1);   // the hidden row and the NaN row are both skipped
        AssertThat(canvas.NonFiniteCoordinateCount).IsEqual(0);
    }

    [TestCase]
    public void WithoutAPositionAxisNothingIsDrawn()
    {
        var canvas = new FakeCanvas2D();
        var ctx = TestContexts.Mark(canvas, [D(("day", 6.0))],
            PositionOnly(), new ScaleSet());   // no X scale at all

        new MilestoneMark().Render(ctx);

        AssertThat(canvas.DrewAnything).IsFalse();
    }

    [TestCase]
    public void AMilestoneChartKeepsItsAxes()
    {
        // Unlike a waffle, a milestone is read against the axis it shares, so the chart draws them.
        AssertThat(new MilestoneMark().UsesAxes).IsTrue();
        AssertThat(new MilestoneMark().Coordinate).IsEqual(MarkCoordinate.Cartesian);
    }
}
