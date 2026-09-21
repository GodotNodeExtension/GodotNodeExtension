namespace GodotNodeExtension.Tests.GodotChart;

using System;
using System.Collections.Generic;
using GdUnit4;
using Godot;
using GodotNodeExtension.Component.GodotChart;
using GodotNodeExtension.Component.GodotChart.Marks;
using GodotNodeExtension.Component.GodotSkia;
using Support;
using static GdUnit4.Assertions;

/// <summary>
/// Tests for the interaction layer of <see cref="Chart"/> (Chart.Interaction.cs) and the shared
/// hit-testing helpers: the focus / selection / hover state machine and its change-only events,
/// click handling (element, empty space and legend), hit tests against marks, the legend and the
/// axis zones, hidden-series and skipped-mark exclusion, the crosshair helper, plus the canvas
/// interactions a clipped drawing pass relies on (balanced Save/Restore after a failure, and the
/// release of the process-wide Skia backend state).
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class ChartInteractionTest
{
    private static readonly string[] SingleCategory = { "A" };
    private static readonly string[] TwoCategories = { "A", "B" };

    private static DataRow D(params (string Field, object Value)[] fields)
    {
        var row = new DataRow(fields.Length);
        foreach (var (field, value) in fields)
            row.Set(field, value);
        return row;
    }

    private static List<DataRow> Bars() =>
    [
        D(("cat", "A"), ("value", 40.0)),
        D(("cat", "B"), ("value", 100.0)),
        D(("cat", "C"), ("value", 60.0)),
    ];

    private static List<DataRow> SeriesRows() =>
    [
        D(("cat", "A"), ("value", 10.0), ("series", "S1")),
        D(("cat", "B"), ("value", 20.0), ("series", "S1")),
        D(("cat", "A"), ("value", 8.0), ("series", "S2")),
        D(("cat", "B"), ("value", 12.0), ("series", "S2")),
    ];

    /// <summary>
    /// A 400x300 cartesian chart over <paramref name="canvas"/>, carrying the fields these cases encode
    /// (<c>cat</c> / <c>value</c>).
    /// <para>
    /// The caller owns the returned chart (<c>using var chart = Cartesian(...)</c>) and disposes it at the
    /// end of its case, after its assertions: the helper must not return a chart it has already disposed,
    /// which is what a <c>using</c> inside the helper would do.
    /// </para>
    /// </summary>
    private static Chart Cartesian(FakeCanvas2D canvas, Mark mark, List<DataRow>? data = null)
    {
        var chart = new Chart(canvas) { Width = 400f, Height = 300f };
        chart.Data(data ?? Bars());
        chart.Mark(mark);
        chart.Encode(Channel.X, "cat");
        chart.Encode(Channel.Y, "value");
        return chart;
    }

    private static void Approx(float actual, float expected, float tolerance = 1e-3f)
        => AssertThat(MathF.Abs(actual - expected) <= tolerance).IsTrue();

    /// <summary>
    /// Legend geometry of the chart's last frame, read the way a custom legend renderer reads it
    /// (<see cref="RenderContext.LegendLayout"/>). <see cref="Chart"/> exposes no legend geometry
    /// of its own: the render pipeline and the legend hit test are its only readers.
    /// </summary>
    private static LegendLayout LegendGeometryOf(Chart chart)
    {
        LegendLayout? captured = null;
        chart.LegendRenderer = ctx => captured = ctx.LegendLayout;
        chart.Render();
        return captured!.Value;
    }

    // ── Focus state ─────────────────────────────────────────────────────────

    [TestCase]
    public void FocusSeriesFiresOnlyOnChange()
    {
        using var chart = new Chart(new FakeCanvas2D());
        var events = new List<(string? Prev, string? Cur)>();
        chart.OnFocusChanged += (_, e) => events.Add((e.PreviousSeriesKey, e.SeriesKey));

        chart.FocusSeries("A");
        chart.FocusSeries("A"); // unchanged -> no event
        chart.FocusSeries("B");
        chart.FocusSeries(null);

        AssertThat(events.Count).IsEqual(3);
        AssertThat(events[0].Prev is null).IsTrue();
        AssertThat(events[0].Cur).IsEqual("A");
        AssertThat(events[1].Prev).IsEqual("A");
        AssertThat(events[1].Cur).IsEqual("B");
        AssertThat(events[2].Prev).IsEqual("B");
        AssertThat(events[2].Cur is null).IsTrue();
    }

    // ── Selection state ─────────────────────────────────────────────────────

    [TestCase]
    public void SelectClampsToTheRenderedRows()
    {
        var canvas = new FakeCanvas2D();
        using var chart = Cartesian(canvas, new IntervalMark());   // three rows

        chart.Select(999);

        // Reporting 999 while nothing is drawn as selected made the getter describe a selection the
        // renderer could never show; the index is clamped to what is on screen instead.
        AssertThat(chart.CurrentSelectedRowIndex).IsEqual(2);
        AssertThat(chart.GetRenderDataSnapshot().Count).IsEqual(3);

        chart.Select(-5);
        AssertThat(chart.CurrentSelectedRowIndex).IsEqual(-1);
    }

    [TestCase]
    public void SelectWithoutRowsSelectsNothing()
    {
        var canvas = new FakeCanvas2D();
        using var chart = Cartesian(canvas, new IntervalMark(), data: []);

        chart.Select(0);

        AssertThat(chart.CurrentSelectedRowIndex).IsEqual(-1);
    }

    [TestCase]
    public void SelectFiresOnlyOnChangeAndCarriesTheRow()
    {
        var canvas = new FakeCanvas2D();
        using var chart = Cartesian(canvas, new IntervalMark());
        var events = new List<ChartSelectionEventArgs>();
        chart.OnSelectionChanged += (_, e) => events.Add(e);

        chart.Select(1);
        chart.Select(1); // unchanged
        chart.Select(-1);

        AssertThat(events.Count).IsEqual(2);
        AssertThat(events[0].PreviousRowIndex).IsEqual(-1);
        AssertThat(events[0].RowIndex).IsEqual(1);
        AssertThat(events[0].Row is not null).IsTrue();
        AssertThat(events[1].PreviousRowIndex).IsEqual(1);
        AssertThat(events[1].RowIndex).IsEqual(-1);
        AssertThat(events[1].Row is null).IsTrue();
    }

    // ── Hover state ─────────────────────────────────────────────────────────

    [TestCase]
    public void NotifyHoverChangedFiresOnlyOnChangeAndCarriesTheHit()
    {
        using var chart = new Chart(new FakeCanvas2D());
        var events = new List<ChartHoverEventArgs>();
        chart.OnHover += (_, e) => events.Add(e);

        var row = D(("value", 1.0));
        var hit = new HitResult
        {
            Hit = true, RowIndex = 0, Row = row, SeriesKey = "S1",
            MarkType = "PointMark", ScreenX = 5f, ScreenY = 7f,
        };

        chart.NotifyHoverChanged(0, hit);
        chart.NotifyHoverChanged(0, hit); // unchanged
        chart.NotifyHoverChanged(-1, null);

        AssertThat(events.Count).IsEqual(2);
        AssertThat(events[0].PreviousRowIndex).IsEqual(-1);
        AssertThat(events[0].RowIndex).IsEqual(0);
        AssertThat(events[0].Row).IsEqual(row);
        AssertThat(events[0].SeriesKey).IsEqual("S1");
        AssertThat(events[0].MarkType).IsEqual("PointMark");
        Approx(events[0].ScreenPosition.X, 5f);
        Approx(events[0].ScreenPosition.Y, 7f);

        AssertThat(events[1].PreviousRowIndex).IsEqual(0);
        AssertThat(events[1].RowIndex).IsEqual(-1);
        AssertThat(events[1].Row is null).IsTrue();
        Approx(events[1].ScreenPosition.X, 0f);
    }

    [TestCase]
    public void HoverChangesStateWithoutFiringEvents()
    {
        using var chart = new Chart(new FakeCanvas2D());
        int hoverEvents = 0;
        chart.OnHover += (_, _) => hoverEvents++;

        chart.Hover(3);

        AssertThat(chart.CurrentHoveredRowIndex).IsEqual(3);
        AssertThat(hoverEvents).IsEqual(0);
    }

    // ── Click handling ──────────────────────────────────────────────────────

    [TestCase]
    public void ClickingEmptySpaceClearsSelectionAndFocus()
    {
        var canvas = new FakeCanvas2D();
        using var chart = Cartesian(canvas, new IntervalMark());
        chart.Render();

        chart.FocusSeries("S1");
        chart.Select(0);
        int focusCleared = 0, selectionCleared = 0;
        chart.OnFocusChanged += (_, e) => { if (e.SeriesKey is null) focusCleared++; };
        chart.OnSelectionChanged += (_, e) => { if (e.RowIndex == -1) selectionCleared++; };

        var hit = chart.HandleClick(new Vector2(chart.Width - 1f, 1f)); // top-right corner

        AssertThat(hit is null).IsTrue();
        AssertThat(chart.CurrentFocusedSeries is null).IsTrue();
        AssertThat(chart.CurrentSelectedRowIndex).IsEqual(-1);
        AssertThat(focusCleared).IsEqual(1);
        AssertThat(selectionCleared).IsEqual(1);
    }

    [TestCase]
    public void LegendClickTogglesFocus()
    {
        var canvas = new FakeCanvas2D();
        using var chart = new Chart(canvas) { Width = 400f, Height = 300f };
        chart.Data(SeriesRows());
        chart.Mark(new IntervalMark());
        chart.Encode(Channel.X, "cat");
        chart.Encode(Channel.Y, "value");
        chart.Encode(Channel.Color, "series");
        chart.Legend(new LegendConfig { Position = LegendPosition.Bottom });

        var item = LegendGeometryOf(chart).Items[0];
        var center = new Vector2(item.X + item.Width * 0.5f, item.Y + item.Height * 0.5f);

        string? legendKey = null;
        chart.OnLegendClick += (_, e) => legendKey = e.SeriesKey;

        var hit = chart.HandleClick(center);
        AssertThat(hit is not null).IsTrue();
        AssertThat(hit!.MarkType).IsEqual("Legend");
        AssertThat(hit.SeriesKey).IsEqual(item.Key);
        AssertThat(hit.RowIndex).IsEqual(-1);
        AssertThat(legendKey).IsEqual(item.Key);
        AssertThat(chart.CurrentFocusedSeries).IsEqual(item.Key);

        // Clicking the focused item again clears the focus.
        var second = chart.HandleClick(center);
        AssertThat(second!.FocusedSeries is null).IsTrue();
        AssertThat(chart.CurrentFocusedSeries is null).IsTrue();
    }

    [TestCase]
    public void HandledLegendClickDoesNotToggleFocus()
    {
        var canvas = new FakeCanvas2D();
        using var chart = new Chart(canvas) { Width = 400f, Height = 300f };
        chart.Data(SeriesRows());
        chart.Mark(new IntervalMark());
        chart.Encode(Channel.X, "cat");
        chart.Encode(Channel.Y, "value");
        chart.Encode(Channel.Color, "series");
        chart.Legend(new LegendConfig { Position = LegendPosition.Bottom });

        chart.OnLegendClick += (_, e) => e.Handled = true;

        var item = LegendGeometryOf(chart).Items[0];
        chart.HandleClick(new Vector2(item.X + item.Width * 0.5f, item.Y + item.Height * 0.5f));

        AssertThat(chart.CurrentFocusedSeries is null).IsTrue();
    }

    /// <summary>
    /// The documented split between the two click events: a data element raises <see cref="Chart.OnClick"/>
    /// with its row, a legend item raises <see cref="Chart.OnLegendClick"/> <b>instead</b> (a legend item
    /// stands for a series, not for a row, so a host counting clicks per row must not see it), and empty
    /// space raises neither.
    /// </summary>
    [TestCase]
    public void ElementLegendAndEmptyClicksRaiseTheirOwnEvents()
    {
        var canvas = new FakeCanvas2D();
        using var chart = new Chart(canvas) { Width = 400f, Height = 300f };
        chart.Data(SeriesRows());
        chart.Mark(new IntervalMark());
        chart.Encode(Channel.X, "cat");
        chart.Encode(Channel.Y, "value");
        chart.Encode(Channel.Color, "series");
        chart.Legend(new LegendConfig { Position = LegendPosition.Bottom });

        var clicks = new List<(int Row, string? MarkType)>();
        var legendClicks = new List<string>();
        chart.OnClick += (_, e) => clicks.Add((e.RowIndex, e.MarkType));
        chart.OnLegendClick += (_, e) => legendClicks.Add(e.SeriesKey);

        // A bar: the element click is reported with a row (two series share each category, and the topmost
        // one wins the hit test).
        chart.Render();
        var plot = chart.CurrentPlotArea!.Value;
        var slot = plot.Width / 2f;                    // two categories
        var bar = new Vector2(plot.X + slot * 0.5f, plot.Y + plot.Height * 0.75f);
        AssertThat(chart.HandleClick(bar) is not null).IsTrue();
        AssertThat(clicks.Count).IsEqual(1);
        AssertThat(clicks[0].Row >= 0).IsTrue();
        AssertThat(clicks[0].MarkType).IsEqual("IntervalMark");

        // A legend item: only the legend event.
        var item = LegendGeometryOf(chart).Items[0];
        chart.HandleClick(new Vector2(item.X + item.Width * 0.5f, item.Y + item.Height * 0.5f));
        AssertThat(legendClicks.Count).IsEqual(1);
        AssertThat(legendClicks[0]).IsEqual(item.Key);
        AssertThat(clicks.Count).IsEqual(1);           // unchanged: the legend click was not reported as a row click

        // Empty space: neither event.
        AssertThat(chart.HandleClick(new Vector2(chart.Width - 1f, 1f)) is null).IsTrue();
        AssertThat(clicks.Count).IsEqual(1);
        AssertThat(legendClicks.Count).IsEqual(1);
    }

    // ── Axis zone hit tests ─────────────────────────────────────────────────

    [TestCase]
    public void HitTestInTheXAxisZoneReturnsTheJoinedTitle()
    {
        var canvas = new FakeCanvas2D();
        using var chart = Cartesian(canvas, new IntervalMark());
        chart.XAxis(new AxisConfig { Title = "Time", Unit = "s", Description = "elapsed" });
        chart.Render();

        var plot = chart.CurrentPlotArea!.Value;
        var hit = chart.HitTest(new Vector2(plot.X + plot.Width * 0.5f, plot.Y + plot.Height + 5f));

        AssertThat(hit is not null).IsTrue();
        AssertThat(hit!.MarkType).IsEqual("XAxis");
        AssertThat(hit.Label).IsEqual("Time\n(s)\nelapsed");
        Approx(hit.ScreenY, plot.Y + plot.Height + chart.PaddingBottom * 0.5f);
    }

    [TestCase]
    public void HitTestInTheYAxisZoneReturnsTheJoinedTitle()
    {
        var canvas = new FakeCanvas2D();
        using var chart = Cartesian(canvas, new IntervalMark());
        chart.YAxis(new AxisConfig { Title = "Revenue" });
        chart.Render();

        var plot = chart.CurrentPlotArea!.Value;
        var hit = chart.HitTest(new Vector2(plot.X - 5f, plot.Y + plot.Height * 0.5f));

        AssertThat(hit is not null).IsTrue();
        AssertThat(hit!.MarkType).IsEqual("YAxis");
        AssertThat(hit.Label).IsEqual("Revenue");
        Approx(hit.ScreenX, plot.X - chart.PaddingLeft * 0.5f);
    }

    // ── Mark hit tests ──────────────────────────────────────────────────────

    [TestCase]
    public void TopMostMarkWinsTheHitTest()
    {
        var canvas = new FakeCanvas2D();
        using var chart = new Chart(canvas) { Width = 400f, Height = 300f };
        chart.Data(Bars());
        chart.Mark(new IntervalMark());          // drawn first (bottom)
        chart.Mark(new PointMark());             // drawn last (top)
        chart.Encode(Channel.X, "cat");
        chart.Encode(Channel.Y, "value");
        chart.Render();

        // A point of the point mark sits exactly on the bar's value position.
        var plot = chart.CurrentPlotArea!.Value;
        var hit = chart.HitTest(new Vector2(plot.X + plot.Width / 6f, plot.Y + plot.Height * 0.6f));

        AssertThat(hit is not null).IsTrue();
        AssertThat(hit!.MarkType).IsEqual(nameof(PointMark));
    }

    [TestCase]
    public void SkippedMarksAreNotHitTestable()
    {
        var canvas = new FakeCanvas2D();
        using var chart = new Chart(canvas) { Width = 400f, Height = 300f };
        chart.Data(Bars());
        chart.Mark(new PieMark());               // primary coordinate system: polar
        chart.Mark(new IntervalMark());          // incompatible -> skipped by Render
        chart.Encode(Channel.X, "cat");
        chart.Encode(Channel.Y, "value");
        chart.Render();

        var plot = chart.CurrentPlotArea!.Value;
        var hits = new List<string?>();
        for (float x = plot.X; x <= plot.X + plot.Width; x += 10f)
        {
            for (float y = plot.Y; y <= plot.Y + plot.Height; y += 10f)
            {
                var hit = chart.HitTest(new Vector2(x, y));
                if (hit != null) hits.Add(hit.MarkType);
            }
        }

        AssertThat(hits.Contains(nameof(IntervalMark))).IsFalse();
    }

    [TestCase]
    public void HitAreaFollowsTheEntryAnimation()
    {
        var canvas = new FakeCanvas2D();
        using var chart = Cartesian(canvas, new IntervalMark());
        chart.Animate(0.5f);                     // bars are drawn at half height
        chart.Render();

        var plot = chart.CurrentPlotArea!.Value;
        float barX = plot.X + plot.Width / 6f;   // first bar, value 40 of 0..100

        var nearBaseline = chart.HitTest(new Vector2(barX, plot.Y + plot.Height - 5f));
        var atFinalTop  = chart.HitTest(new Vector2(barX, plot.Y + plot.Height * 0.45f));

        AssertThat(nearBaseline is not null).IsTrue();
        AssertThat(atFinalTop is null).IsTrue(); // not drawn yet at 50% progress
    }

    [TestCase]
    public void SelectionReportsTheRenderedRow()
    {
        var canvas = new FakeCanvas2D();
        using var chart = new Chart(canvas) { Width = 400f, Height = 300f };
        chart.Data(new List<DataRow>
        {
            D(("value", 1.0)), D(("value", 2.0)), D(("value", 3.0)), D(("value", 4.0)),
        });
        chart.Transform(new BinTransform { Field = "value", BinCount = 2 });
        chart.Mark(new IntervalMark());
        chart.Encode(Channel.X, "BinMid");
        chart.Encode(Channel.Y, "Count");
        chart.Render();

        DataRow? selected = null;
        chart.OnSelectionChanged += (_, e) => selected = e.Row;
        chart.Select(0);

        AssertThat(selected is not null).IsTrue();
        // Raw rows carry "value"; the rendered rows are bins and carry "Count".
        AssertThat(selected!.Has("Count")).IsTrue();
        AssertThat(selected.Has("value")).IsFalse();
    }

    [TestCase]
    public void MarksWithOwnDataAreHitTestedAgainstThatData()
    {
        var canvas = new FakeCanvas2D();
        var mark = new ChordMark();
        using var chart = new Chart(canvas) { Width = 400f, Height = 300f };
        chart.Data(new List<DataRow> { D(("cat", "A"), ("value", 1.0)) });
        chart.Mark(mark);
        chart.Encode(Channel.X, "cat");
        chart.Encode(Channel.Y, "value");
        mark.Data =
        [
            D(("source", "A"), ("target", "B"), ("value", 3.0)),
            D(("source", "B"), ("target", "C"), ("value", 2.0)),
        ];
        chart.Render();

        HitResult? outOfRange = null;
        for (float y = 0; y <= 300; y += 5)
        {
            for (float x = 0; x <= 400; x += 5)
            {
                var hit = chart.HitTest(new Vector2(x, y));
                if (hit is not null && hit.RowIndex >= mark.Data.Count) outOfRange = hit;
            }
        }

        AssertThat(outOfRange is null).IsTrue();
    }

    [TestCase]
    public void HiddenSeriesAreNotHitTestable()
    {
        var canvas = new FakeCanvas2D();
        using var chart = new Chart(canvas) { Width = 400f, Height = 300f };
        chart.Data(SeriesRows());
        chart.Mark(new IntervalMark());
        chart.Encode(Channel.X, "cat");
        chart.Encode(Channel.Y, "value");
        chart.Encode(Channel.Color, "series");
        chart.Render();

        chart.HideSeries("S2");
        chart.Render();

        var plot = chart.CurrentPlotArea!.Value;
        var seen = new HashSet<string?>();
        for (float y = plot.Y; y <= plot.Y + plot.Height; y += 5f)
            for (float x = plot.X; x <= plot.X + plot.Width; x += 5f)
            {
                var hit = chart.HitTest(new Vector2(x, y));
                if (hit is { Hit: true }) seen.Add(hit.SeriesKey);
            }

        AssertThat(seen.Contains("S1")).IsTrue();  // the visible series is still hittable
        AssertThat(seen.Contains("S2")).IsFalse(); // the hidden series is not
    }

    [TestCase]
    public void TestAllReturnsTheTopMostMarkAndHonoursTheSkippedSet()
    {
        var canvas = new FakeCanvas2D();
        var data = new List<DataRow> { D(("cat", "A"), ("value", 10.0)) };
        var ctx = TestContexts.Mark(
            canvas, data, TestContexts.XyEncodes("cat", "value"),
            TestContexts.CategoryScales(SingleCategory, 0, 10), plot: new PlotArea(0, 0, 400, 300));

        var bar = new IntervalMark();   // bottom
        var point = new PointMark();    // top
        var marks = new List<Mark> { bar, point };

        // The single category sits at x = 200; value 10 (of 0..10) is the top of the plot, where
        // both the bar and the point are present.
        var probe = new Vector2(200f, 0f);
        var hit = ChartInteraction.TestAll(marks, ctx, probe);
        AssertThat(hit is not null).IsTrue();
        AssertThat(hit!.MarkType).IsEqual(nameof(PointMark));

        // Skipping the top mark falls through to the one below.
        var fallback = ChartInteraction.TestAll(marks, ctx, probe, new HashSet<Mark> { point });
        AssertThat(fallback is not null).IsTrue();
        AssertThat(fallback!.MarkType).IsEqual(nameof(IntervalMark));
    }

    // ── Crosshair helper ────────────────────────────────────────────────────

    [TestCase]
    public void DrawCrosshairOnlyDrawsInsideThePlot()
    {
        var canvas = new FakeCanvas2D();
        var plot = new PlotArea(50f, 20f, 300f, 200f);

        ChartInteraction.DrawCrosshair(canvas, new Vector2(10f, 100f), plot); // left of the plot
        AssertThat(canvas.StrokeCount).IsEqual(0);

        ChartInteraction.DrawCrosshair(canvas, new Vector2(plot.X + 10f, plot.Y + 10f), plot);
        AssertThat(canvas.StrokeCount).IsEqual(2); // vertical + horizontal line
    }

    // ── State across layout changes ─────────────────────────────────────────

    [TestCase]
    public void InteractionStateSurvivesAResize()
    {
        var canvas = new FakeCanvas2D();
        var probe = new CapturingMark();
        using var chart = Cartesian(canvas, probe);
        chart.FocusSeries("S1");
        chart.Select(1);
        chart.Hover(0);

        chart.Width = 800f;
        chart.Render();

        AssertThat(probe.LastFocusedSeries).IsEqual("S1");
        AssertThat(probe.LastSelectedRowIndex).IsEqual(1);
        AssertThat(chart.CurrentHoveredRowIndex).IsEqual(0);
    }

    // ── Canvas cleanup and backend lifecycle ────────────────────────────────

    /// <summary>
    /// <see cref="Chart.Dispose"/> releases the theme hook and - only for a chart that owns it - the canvas;
    /// it does not tear the chart down. A chart whose canvas is shared (the default <c>ownsCanvas: false</c>)
    /// keeps rendering and hit testing after the dispose, and disposing it again is a no-op; an owning chart
    /// releases its canvas exactly once. This is the contract every case of this file relies on when it
    /// returns a live chart from a helper and disposes it at the end of the case.
    /// </summary>
    [TestCase]
    public void DisposedChartKeepsWorkingAndDisposeIsIdempotent()
    {
        var canvas = new FakeCanvas2D();
        // Deliberately *not* a using statement: the case disposes the chart explicitly (twice here and
        // once at the end) because that is exactly the contract under test.
        var chart = Cartesian(canvas, new IntervalMark());
        chart.Render();
        AssertThat(canvas.FillCount > 0).IsTrue();

        // The bar geometry the other cases use: the first bar ("cat" A, value 40 of 0..100) spans the first
        // sixth of the plot, so a probe just above the baseline sits inside it.
        var plot = chart.CurrentPlotArea!.Value;
        var probe = new Vector2(plot.X + plot.Width / 6f, plot.Y + plot.Height - 5f);
        var hitBefore = chart.HitTest(probe);
        AssertThat(hitBefore is not null).IsTrue();
        AssertThat(hitBefore!.Hit).IsTrue();

        int fillsBeforeDispose = canvas.FillCount;
        chart.Dispose();

        // The canvas is shared (ownsCanvas: false), so the chart must not release it - other charts draw on it.
        AssertThat(canvas.DisposeCount).IsEqual(0);

        // A disposed chart is still a working one: it renders into the same canvas and still reports hits.
        chart.Render();
        AssertThat(canvas.FillCount > fillsBeforeDispose).IsTrue();
        AssertThat(canvas.DisposeCount).IsEqual(0);

        var hitAfter = chart.HitTest(probe);
        AssertThat(hitAfter is not null).IsTrue();
        AssertThat(hitAfter!.Hit).IsTrue();

        // Disposing twice is a no-op (no second release, no throw).
        chart.Dispose();
        AssertThat(canvas.DisposeCount).IsEqual(0);

        // An owning chart is responsible for its canvas: exactly one release, and the second dispose does not
        // reach the released canvas again. (Nothing renders into it after that: its owner released it.)
        var ownedCanvas = new FakeCanvas2D();
        var owner = new Chart(ownedCanvas, ownsCanvas: true);
        owner.Dispose();
        AssertThat(ownedCanvas.DisposeCount).IsEqual(1);
        owner.Dispose();
        AssertThat(ownedCanvas.DisposeCount).IsEqual(1);

        // The shared-canvas chart is disposed explicitly here (and not by a using statement), so this
        // third dispose is the one the using statement used to perform - it must stay a no-op too.
        chart.Dispose();
        AssertThat(canvas.DisposeCount).IsEqual(0);
    }

    [TestCase]
    public void ClippedLineRestoresTheCanvasEvenWhenDrawingThrows()
    {
        var canvas = new FakeCanvas2D();
        // ShowArea forces a Fill(), which is what the throwing canvas intercepts.
        var mark = new LineMark { ShowArea = true };
        using var chart = new Chart(canvas) { Width = 400f, Height = 300f };
        chart.Data(new List<DataRow>
        {
            D(("cat", "A"), ("value", 1.0)),
            D(("cat", "B"), ("value", 2.0)),
        });
        chart.Mark(mark);
        chart.Encode(Channel.X, "cat");
        chart.Encode(Channel.Y, "value");
        chart.Animate(0.5f);      // enables the clip-based reveal
        chart.Render();

        var ctx = TestContexts.Mark(
            canvas,
            [
                D(("cat", "A"), ("value", 1.0)),
                D(("cat", "B"), ("value", 2.0)),
            ],
            TestContexts.XyEncodes("cat", "value"),
            TestContexts.CategoryScales(TwoCategories),
            plot: chart.CurrentPlotArea,
            // anim < 1 enables the clip-based reveal (Save + ClipRect).
            animation: new AnimationContext { EntryProgress = 0.5f });
        canvas.ThrowOnFill = true;

        AssertThat(ThrowsFillFailure(() => mark.Render(ctx))).IsTrue();

        canvas.ThrowOnFill = false;
        // The save/clip pair must not stay on the stack: it used to clip every later frame.
        AssertThat(canvas.SaveCount > 0).IsTrue();
        AssertThat(canvas.SaveRestoreBalanced).IsTrue();
    }

    private static bool ThrowsFillFailure(Action action)
    {
        try
        {
            action();
            return false;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    [TestCase]
    public void StaticSkiaStateCanBeReset()
    {
        // The release is what the assembly-unload hook calls, and that hook can run after an explicit
        // reset, so the call has to be idempotent - and it has to really drop the cache: a kept
        // rendering device / GRContext describes the device of the context that created it and pins
        // the assembly. Clearing twice must leave the same empty state without throwing.
        SkiaCanvasTexture2D.ResetStaticState();
        AssertThat(SkiaCanvasTexture2D.HasCachedDeviceState).IsFalse();
        AssertThat(SkiaCanvasTexture2D.SharedGrContextRefCount).IsEqual(0);

        SkiaCanvasTexture2D.ResetStaticState();
        AssertThat(SkiaCanvasTexture2D.HasCachedDeviceState).IsFalse();
        AssertThat(SkiaCanvasTexture2D.SharedGrContextRefCount).IsEqual(0);

        // It drops the cache, not the backend: the next texture has to build the state again.
        // (That needs a rendering device, so this half of the case skips in a headless run.)
        if (RenderingServer.GetRenderingDevice() is null)
        {
            GD.Print("[skip] StaticSkiaStateCanBeReset (rebuild half): no rendering device in this run");
            return;
        }

        // Release the texture before the reset: the shared GRContext the cache holds is about to be
        // disposed, and the cached handles describe the device the surface was built from.
        var texture = new SkiaCanvasTexture2D(4, 4);
        AssertThat(texture.Canvas is not null).IsTrue();   // builds the surface, i.e. caches the state
        AssertThat(SkiaCanvasTexture2D.HasCachedDeviceState).IsTrue();

        // Explicit release (not just Dispose) so the texture RIDs are handed back deterministically.
        texture.ReleaseResources();
        texture.Dispose();

        SkiaCanvasTexture2D.ResetStaticState();
        AssertThat(SkiaCanvasTexture2D.HasCachedDeviceState).IsFalse();
        AssertThat(SkiaCanvasTexture2D.SharedGrContextRefCount).IsEqual(0);
    }
}
