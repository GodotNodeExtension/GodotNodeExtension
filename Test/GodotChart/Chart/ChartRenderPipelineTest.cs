namespace GodotNodeExtension.Tests.GodotChart;

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
/// Tests for the rendering pipeline of <see cref="Chart"/> (Chart.Render.cs): which stages run for
/// which kind of chart, that one failing stage (renderer or mark) does not abort the frame or the
/// remaining stages, the guard that forbids hit testing before the first layout, mark list
/// management, and the version counters that invalidate the cached layout, the cached mark
/// geometries and the mark-compatibility check.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class ChartRenderPipelineTest
{
    private static DataRow D(params (string Field, object Value)[] fields)
    {
        var row = new DataRow(fields.Length);
        foreach (var (field, value) in fields)
            row.Set(field, value);
        return row;
    }

    private static List<DataRow> Bars() =>

    [
        D(("cat", "A"), ("value", 10.0)),
        D(("cat", "B"), ("value", 20.0)),
    ];

    private static List<DataRow> Rows(int count)
    {
        var rows = new List<DataRow>(count);
        for (int i = 0; i < count; i++)
            rows.Add(TestContexts.Row(("cat", $"C{i}"), ("value", (double)(i + 1))));
        return rows;
    }

    private static Chart Cartesian(FakeCanvas2D canvas, Mark mark, List<DataRow>? data = null)
    {
        var chart = new Chart(canvas) { Width = 400f, Height = 300f };
        chart.Data(data ?? Bars());
        chart.Mark(mark);
        chart.Encode(Channel.X, "cat");
        chart.Encode(Channel.Y, "value");
        return chart;
    }

    [TestCase]
    public void AChartWhoseMarksNeedNoAxesSkipsTheAxisAndGridDecorations()
    {
        var rows = new List<DataRow>
        {
            D(("service", "API"), ("value", 60.0)),
            D(("service", "Web"), ("value", 40.0)),
        };

        var waffleCanvas = new FakeCanvas2D();
        var waffle = new Chart(waffleCanvas) { Width = 400f, Height = 300f };
        waffle.Data(rows);
        waffle.Mark(new WaffleMark());
        waffle.Encode(Channel.X, "service");
        waffle.Encode(Channel.Y, "value");
        waffle.Render();

        AssertThat(waffleCanvas.FillCount > 0).IsTrue();          // the grid itself is drawn
        // A waffle fills a grid of its own; an axis with the category labels would only suggest that the
        // whole grid belongs to one of them (that is how it looked before).
        AssertThat(waffleCanvas.XAxisLabels.Count()).IsEqual(0);
        AssertThat(waffleCanvas.YAxisLabels.Count()).IsEqual(0);
        AssertThat(waffleCanvas.Texts.Count).IsEqual(0);   // no axis tick labels at all

        // The same rows as bars keep their axes.
        var barCanvas = new FakeCanvas2D();
        var bar = new Chart(barCanvas) { Width = 400f, Height = 300f };
        bar.Data(rows);
        bar.Mark(new IntervalMark());
        bar.Encode(Channel.X, "service");
        bar.Encode(Channel.Y, "value");
        bar.Render();

        AssertThat(barCanvas.Texts).Contains("API");       // the category axis is still labelled
    }

    // ── Stage isolation ─────────────────────────────────────────────────────

    [TestCase]
    public void AMarkLevelEncodeDecidesWhatThatMarkDrawsFrom()
    {
        // G2 precedence: the chart binding is the base, a mark's own binding wins for that mark alone.
        var data = new List<DataRow> { D(("cat", "A"), ("value", 10.0), ("other", 90.0)) };
        var canvas = new FakeCanvas2D();
        var chart = new Chart(canvas) { Width = 400f, Height = 300f };
        chart.Data(data);
        chart.Encode(Channel.X, "cat");
        chart.Encode(Channel.Y, "value");

        var fromChart = new ProbeMark();
        var fromMark = new ProbeMark();
        fromMark.Encode(Channel.Y, "other");
        chart.Mark(fromChart).Mark(fromMark);

        chart.Render();

        AssertThat((double)fromChart.RenderedY!).IsEqual(10.0);
        AssertThat((double)fromMark.RenderedY!).IsEqual(90.0);
    }

    [TestCase]
    public void AMarkLevelEncodeAlsoDecidesWhatThatMarkHitTests()
    {
        var data = new List<DataRow> { D(("cat", "A"), ("value", 10.0), ("other", 90.0)) };
        var canvas = new FakeCanvas2D();
        var chart = new Chart(canvas) { Width = 400f, Height = 300f };
        chart.Data(data);
        chart.Encode(Channel.X, "cat");
        chart.Encode(Channel.Y, "value");

        HitResult? seen = null;
        var probing = new ProbeMark();
        probing.HitTestOverride = ctx => seen = new HitResult
        {
            Hit = true, Row = ctx.Data[0], RowIndex = 0,
            Label = $"{ctx.Encodes.Resolve(Channel.Y, ctx.Data[0])}", MarkType = nameof(ProbeMark),
        };
        probing.Encode(Channel.Y, "other");
        chart.Mark(probing);

        chart.Render();
        chart.HitTest(new Vector2(200f, 150f));

        AssertThat(seen?.Label).IsEqual("90");
    }

    [TestCase]
    public void AFailingRendererDoesNotAbortTheFrame()
    {
        var canvas = new FakeCanvas2D();
        var chart = Cartesian(canvas, new IntervalMark());
        chart.Title = "Broken";

        int axisCalls = 0, labelCalls = 0;
        chart.BackgroundRenderer = _ => throw new InvalidOperationException("bg");
        chart.TitleRenderer = _ => throw new InvalidOperationException("title");
        chart.GridRenderer = _ => throw new InvalidOperationException("grid");
        chart.AxisRenderer = _ => axisCalls++;
        chart.AxisLabelRenderer = _ => labelCalls++;

        chart.Render(); // must not throw

        AssertThat(axisCalls).IsEqual(1);
        AssertThat(labelCalls).IsEqual(1);
        AssertThat(canvas.FillCount > 0).IsTrue(); // the mark still drew
    }

    [TestCase]
    public void FailingMarkDoesNotAbortTheFrame()
    {
        var canvas = new FakeCanvas2D();
        var chart = new Chart(canvas) { Width = 400f, Height = 300f };
        chart.Data(new List<DataRow> { D(("cat", "A"), ("value", 10.0)) });
        chart.Mark(new ThrowingMark());   // fails first
        chart.Mark(new IntervalMark());   // must still be drawn
        chart.Encode(Channel.X, "cat");
        chart.Encode(Channel.Y, "value");

        chart.Render(); // must not throw

        // One row, so one bar - plus the frame's own rounded rect, which is what makes this count instead of
        // "something was filled" (the background alone satisfies that).
        AssertThat(canvas.RoundRects.Count).IsEqual(2);
    }

    /// <summary>
    /// A mark whose scale contribution throws is isolated to that stage: the frame renders, the other marks
    /// draw, and the failure is reported by the stage wrapper instead of taking the frame down.
    /// </summary>
    [TestCase]
    public void AMarkWhoseContributeScalesThrowsDoesNotAbortTheFrame()
    {
        var canvas = new FakeCanvas2D();
        var chart = new Chart(canvas) { Width = 400f, Height = 300f };
        chart.Data(new List<DataRow> { D(("cat", "A"), ("value", 10.0)) });
        chart.Mark(new ThrowingMark { ThrowFrom = ThrowingMark.Callback.ContributeScales });
        chart.Mark(new IntervalMark());
        chart.Encode(Channel.X, "cat");
        chart.Encode(Channel.Y, "value");

        chart.Render(); // must not throw

        // The same count the Render-isolation case asserts: the frame survived *and* still drew its bar.
        AssertThat(canvas.RoundRects.Count).IsEqual(2);
    }

    /// <summary>
    /// <see cref="Chart.HitTest"/> is a host-facing call, not a render stage: an exception from a mark travels
    /// to the caller. Swallowing it into a null ("nothing hit") would hide the broken mark and leave the host
    /// wondering why its hover stopped working.
    /// </summary>
    [TestCase]
    public void AHitTestFailureReachesTheCaller()
    {
        var canvas = new FakeCanvas2D();
        var chart = new Chart(canvas) { Width = 400f, Height = 300f };
        chart.Data(new List<DataRow> { D(("cat", "A"), ("value", 10.0)) });
        chart.Mark(new ThrowingMark { ThrowFrom = ThrowingMark.Callback.HitTest });
        chart.Encode(Channel.X, "cat");
        chart.Encode(Channel.Y, "value");
        chart.Render();

        AssertThat(Asserts.Throws<InvalidOperationException>(() => chart.HitTest(new Vector2(200f, 150f)))
            is not null).IsTrue();
    }

    /// <summary>
    /// A renderer slot that renders again: the reentrant call is <b>ignored</b> (the frame already running
    /// keeps building its state) instead of nesting, and a callback that always rendered would otherwise
    /// recurse until the stack ran out.
    /// </summary>
    [TestCase]
    public void ARendererSlotThatRendersAgainIsIgnored()
    {
        var canvas = new FakeCanvas2D();
        var chart = Cartesian(canvas, new IntervalMark());
        int slotCalls = 0;
        chart.BackgroundRenderer = _ =>
        {
            slotCalls++;
            chart.Render();          // reentrant: ignored, not nested
        };

        chart.Render();

        AssertThat(slotCalls).IsEqual(1);                  // one frame, so one slot call
        AssertThat(canvas.RoundRects.Count).IsEqual(2);    // ...and that frame still drew its bar
    }

    // ── Stage gating ────────────────────────────────────────────────────────

    [TestCase]
    public void NullTitleDoesNotInvokeTheTitleRenderer()
    {
        var canvas = new FakeCanvas2D();
        var chart = Cartesian(canvas, new CapturingMark());
        int titleCalls = 0;
        chart.TitleRenderer = _ => titleCalls++;
        chart.Title = null;

        chart.Render();

        AssertThat(titleCalls).IsEqual(0);
    }

    [TestCase]
    public void CrosshairRendererIsInvokedWhenTheMouseIsSet()
    {
        var canvas = new FakeCanvas2D();
        var chart = Cartesian(canvas, new IntervalMark());
        int calls = 0;
        chart.CrosshairRenderer = _ => calls++;
        chart.Interaction(new Vector2(200f, 150f));

        chart.Render();

        AssertThat(calls).IsEqual(1);
    }

    [TestCase]
    public void PolarChartsSkipTheCrosshairOverlay()
    {
        var canvas = new FakeCanvas2D();
        var chart = new Chart(canvas) { Width = 400f, Height = 300f };
        chart.Data(Bars());
        chart.Mark(new PieMark());
        chart.Encode(Channel.X, "cat");
        chart.Encode(Channel.Y, "value");
        int calls = 0;
        chart.CrosshairRenderer = _ => calls++;
        chart.Interaction(new Vector2(200f, 150f));

        chart.Render();

        AssertThat(calls).IsEqual(0);
    }

    [TestCase]
    public void EnableCrosshairFalseSkipsTheOverlay()
    {
        var canvas = new FakeCanvas2D();
        var chart = Cartesian(canvas, new IntervalMark());
        var theme = ChartTheme.Dark();
        theme.EnableCrosshair = false;
        chart.Theme(theme);
        int calls = 0;
        chart.CrosshairRenderer = _ => calls++;
        chart.Interaction(new Vector2(200f, 150f));

        chart.Render();

        AssertThat(calls).IsEqual(0);
    }

    [TestCase]
    public void IncompatibleMarkCoordinateSystemsAreSkipped()
    {
        var canvas = new FakeCanvas2D();
        var probe = new CapturingMark();
        var chart = new Chart(canvas) { Width = 400f, Height = 300f };
        chart.Data(Bars());
        chart.Mark(new PieMark());   // polar: defines the coordinate system
        chart.Mark(probe);           // cartesian: incompatible, must be skipped
        chart.Encode(Channel.X, "cat");
        chart.Encode(Channel.Y, "value");

        chart.Render();

        AssertThat(probe.RenderCount).IsEqual(0);
        AssertThat(probe.ContributeScalesCount).IsEqual(0);
    }

    // ── Hit testing before layout ───────────────────────────────────────────

    [TestCase]
    public void HitTestBeforeRenderReturnsNull()
    {
        var canvas = new FakeCanvas2D();
        var chart = Cartesian(canvas, new IntervalMark());

        // No layout has been computed yet.
        AssertThat(chart.HitTest(new Vector2(60f, 100f)) is null).IsTrue();
    }

    // ── Mark list management ────────────────────────────────────────────────

    [TestCase]
    public void MarkGenericAddsADefaultInstance()
    {
        var chart = new Chart(new FakeCanvas2D()).Mark<IntervalMark>();

        AssertThat(chart.Marks.Count).IsEqual(1);
        AssertThat(chart.Marks[0] is IntervalMark).IsTrue();
    }

    [TestCase]
    public void ApplyToAllMarksVisitsEveryMark()
    {
        var chart = new Chart(new FakeCanvas2D());
        chart.Mark(new IntervalMark());
        chart.Mark(new PointMark());

        int visited = 0;
        chart.ApplyToAllMarks(_ => visited++);

        AssertThat(visited).IsEqual(2);
    }

    [TestCase]
    public void AddingANullMarkIsRejected()
    {
        var chart = new Chart(new FakeCanvas2D());

        AssertThat(ThrowsInvalidArgument(() => chart.Mark(null!))).IsTrue();
        AssertThat(ThrowsInvalidArgument(() => chart.Theme(null!))).IsTrue();
    }

    private static bool ThrowsInvalidArgument(Action action)
    {
        try
        {
            action();
            return false;
        }
        catch (ArgumentNullException)
        {
            return true;
        }
    }

    // ── Version invalidation ────────────────────────────────────────────────

    [TestCase]
    public void TransformAddedAfterFirstRenderIsApplied()
    {
        var probe = new CapturingMark();
        var chart = new Chart(new FakeCanvas2D());
        chart.Data(Rows(4));
        chart.Mark(probe);
        chart.Encode(Channel.X, "cat");
        chart.Encode(Channel.Y, "value");

        chart.Render();
        int afterFirstRender = probe.LastDataCount;

        chart.Transform(new BinTransform { Field = "value", BinCount = 2 });
        chart.Render();

        AssertThat(afterFirstRender).IsEqual(4);
        AssertThat(probe.LastDataCount).IsEqual(2); // 4 rows binned into 2 bins
    }

    [TestCase]
    public void EffectiveLayoutVersionChangesWithPlotGeometry()
    {
        var chart = new Chart(new FakeCanvas2D());
        int baseline = chart.EffectiveLayoutVersion;

        chart.Width = 800f;
        int afterWidth = chart.EffectiveLayoutVersion;
        AssertThat(afterWidth != baseline).IsTrue();

        chart.PaddingLeft = 80f;
        AssertThat(chart.EffectiveLayoutVersion != afterWidth).IsTrue();

        chart.OffsetY = 12f;
        AssertThat(chart.EffectiveLayoutVersion != afterWidth).IsTrue();
    }

    [TestCase]
    public void MarkSeesANewLayoutVersionAfterResize()
    {
        var probe = new CapturingMark();
        var chart = new Chart(new FakeCanvas2D());
        chart.Data(Rows(4));
        chart.Mark(probe);
        chart.Encode(Channel.X, "cat");
        chart.Encode(Channel.Y, "value");

        chart.Render();
        int first = probe.LastLayoutVersion;

        chart.Width = 640f;
        chart.Render();

        AssertThat(probe.LastLayoutVersion != first).IsTrue();
    }

    [TestCase]
    public void EffectiveLayoutVersionChangesWithThemeSwap()
    {
        var chart = new Chart(new FakeCanvas2D());
        int before = chart.EffectiveLayoutVersion;

        chart.Theme(ChartTheme.Light());

        AssertThat(chart.EffectiveLayoutVersion != before).IsTrue();
    }

    /// <summary>
    /// Editing a theme in place keeps the resource instance, which nothing on the chart can notice by
    /// itself: the chart follows the resource's <see cref="Resource.Changed"/> signal, so the cached
    /// mark geometries (and the colours baked into them) are rebuilt.
    /// </summary>
    [TestCase]
    public void InPlaceThemeEditInvalidatesTheLayoutCache()
    {
        var canvas = new FakeCanvas2D();
        var theme = ChartTheme.Dark();
        var mark = new WaffleMark();
        var chart = new Chart(canvas) { Width = 400f, Height = 300f };
        chart.Theme(theme);
        chart.Data(Bars());
        chart.Mark(mark);
        chart.Encode(Channel.X, "cat");
        chart.Encode(Channel.Y, "value");

        chart.Render();
        AssertThat(mark.LayoutBuildCount).IsEqual(1);
        int version = chart.EffectiveLayoutVersion;

        // In-place array edit plus the explicit notification: exactly what an inspector edit of one
        // palette entry does (the array setter would notify on its own).
        theme.Palette[0] = new Color(1f, 0f, 1f);
        theme.EmitChanged();

        AssertThat(chart.EffectiveLayoutVersion != version).IsTrue();

        chart.Render();
        AssertThat(mark.LayoutBuildCount).IsEqual(2);
    }

    /// <summary>
    /// The theme hook is symmetric: the chart follows exactly one theme, so switching away from a
    /// theme stops reacting to it (a stale hook would invalidate the layout on someone else's edit).
    /// </summary>
    [TestCase]
    public void SwitchingTheThemeStopsFollowingThePreviousOne()
    {
        var chart = new Chart(new FakeCanvas2D());
        var abandoned = ChartTheme.Dark();
        chart.Theme(abandoned);
        chart.Theme(ChartTheme.Light());
        int version = chart.EffectiveLayoutVersion;

        abandoned.EmitChanged();

        AssertThat(chart.EffectiveLayoutVersion).IsEqual(version);
    }

    /// <summary>A disposed chart no longer reacts to its theme (the hook is released in Dispose).</summary>
    [TestCase]
    public void DisposedChartStopsFollowingItsTheme()
    {
        var chart = new Chart(new FakeCanvas2D());
        var theme = ChartTheme.Dark();
        chart.Theme(theme);
        chart.Dispose();
        int version = chart.EffectiveLayoutVersion;

        theme.EmitChanged();   // must not reach the chart, and must not throw

        AssertThat(chart.EffectiveLayoutVersion).IsEqual(version);
    }

    [TestCase]
    public void SeriesVisibilityChangesInvalidateLayout()
    {
        var chart = new Chart(new FakeCanvas2D());
        int baseline = chart.EffectiveLayoutVersion;

        chart.HideSeries("A");
        int afterHide = chart.EffectiveLayoutVersion;
        AssertThat(afterHide != baseline).IsTrue();

        chart.ShowSeries("A");
        AssertThat(chart.EffectiveLayoutVersion != afterHide).IsTrue();

        chart.HideSeries("A");
        int beforeToggle = chart.EffectiveLayoutVersion;
        chart.ToggleSeriesVisibility("A");
        AssertThat(chart.EffectiveLayoutVersion != beforeToggle).IsTrue();

        chart.HideSeries("B");
        int beforeShowAll = chart.EffectiveLayoutVersion;
        chart.ShowAllSeries();
        AssertThat(chart.EffectiveLayoutVersion != beforeShowAll).IsTrue();
    }

    [TestCase]
    public void AddingAMarkInvalidatesTheSkippedMarkCache()
    {
        var chart = new Chart(new FakeCanvas2D());
        chart.Mark(new IntervalMark());
        chart.Render();
        int before = chart.EffectiveLayoutVersion;

        chart.Mark(new PieMark());

        AssertThat(chart.EffectiveLayoutVersion != before).IsTrue();
    }
}
