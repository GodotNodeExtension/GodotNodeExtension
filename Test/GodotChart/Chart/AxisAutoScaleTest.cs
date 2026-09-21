namespace GodotNodeExtension.Tests.GodotChart;

using System;
using System.Collections.Generic;
using GdUnit4;
using Godot;
using GodotNodeExtension.Component.GodotChart;
using GodotNodeExtension.Component.GodotChart.Canvas;
using GodotNodeExtension.Component.GodotChart.Marks;
using Support;
using static GdUnit4.Assertions;

/// <summary>
/// The automatic value axis when it is asked to be <i>sticky</i>: <see cref="AxisConfig.AutoScaleMargin"/>
/// holds the domain while the data wobbles inside the margin, <see cref="AxisConfig.NiceDomain"/> rounds a
/// refitted domain out to the {1, 2, 5} x 10^n ladder, and <see cref="AxisConfig.MinLimit"/> /
/// <see cref="AxisConfig.MaxLimit"/> pin one end while the other keeps following the data (all in
/// <c>Chart.Scales.cs</c>).
/// <para>
/// The numbers are hand-checkable: a chart over two points, <c>y</c> 10 and 20, fits to 0..20 (the zero
/// baseline a value axis adds), so a 0.2 margin seeds the band -4..24 and the fitted step is 5. A domain
/// that is only re-fitted when the data really leaves that band is the whole point of the feature - a
/// quote feed must not re-label its axis on every row.
/// </para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class AxisAutoScaleTest
{
    /// <summary>Tolerance of the ladder test: a margin is a float, so the band it seeds is not exact.</summary>
    private const double LadderTolerance = 1e-6;

    /// <summary>The {1, 2, 5} mantissas of the nice ladder, coarsest first for <see cref="SharedLadderStep"/>.</summary>
    private static readonly double[] LadderFactors = [5, 2, 1];

    /// <summary>
    /// Compare against an expected bound. The margin is a <see cref="float"/> and the domain is a
    /// <see cref="double"/>, so a band seeded from 0.2f reads -4.0000000596 rather than -4: the property
    /// under test is where the axis is, not the last bit of it.
    /// </summary>
    private static void Approx(double actual, double expected)
        => AssertThat(Math.Abs(actual - expected) <= 1e-4).IsTrue();

    /// <summary>A line chart with a numeric x (0..n-1) and an optional Y axis configuration.</summary>
    private static Chart ValueChart(FakeCanvas2D canvas, AxisConfig? yAxis = null)
    {
        var chart = new Chart(canvas) { Width = 400f, Height = 300f };
        if (yAxis is not null) chart.YAxis(yAxis);
        chart.Encode(Channel.X, "x");
        chart.Encode(Channel.Y, "y");
        chart.Mark(new LineMark());
        return chart;
    }

    /// <summary>Replace the data with one series and render, the way a feed updates a chart.</summary>
    private static void Feed(Chart chart, params double[] y)
    {
        var rows = new List<DataRow>(y.Length);
        for (int i = 0; i < y.Length; i++) rows.Add(TestContexts.Row(("x", (double)i), ("y", y[i])));
        chart.Data(rows);
        chart.Render();
    }

    /// <summary>The chart's window on the Y axis, asserting it has one.</summary>
    private static (double Min, double Max) Domain(Chart chart)
    {
        AssertThat(chart.TryGetDomain(Channel.Y, out double min, out double max)).IsTrue();
        return (min, max);
    }

    /// <summary>How far the two ends of a domain are from the ladder step they are multiples of.</summary>
    private static double SharedLadderStep(double min, double max)
    {
        for (int exponent = 12; exponent >= -6; exponent--)
        {
            double magnitude = Math.Pow(10, exponent);
            foreach (double factor in LadderFactors)
            {
                double step = factor * magnitude;
                if (IsMultiple(min, step) && IsMultiple(max, step)) return step;
            }
        }
        return 0;
    }

    /// <summary>
    /// Whether a value sits on a grid of <paramref name="step"/>. The tolerance is in value units on purpose: a
    /// ratio-based test would accept every too-coarse step, since "0 steps of a huge grid" is also a multiple.
    /// </summary>
    private static bool IsMultiple(double value, double step)
        => Math.Abs(value - Math.Round(value / step) * step) <= LadderTolerance;

    // ── Sticky margin ───────────────────────────────────────────────────────

    /// <summary>
    /// The acceptance criterion of the whole feature: while the data stays inside the margin the axis does
    /// not move by a bit. A chart that re-labelled itself on every row is what the knob exists to stop.
    /// </summary>
    [TestCase]
    public void AStickyAxisKeepsItsDomainWhileTheDataStaysInside()
    {
        var chart = ValueChart(new FakeCanvas2D(), new AxisConfig { AutoScaleMargin = 0.2f });

        Feed(chart, 10, 20);
        var (min, max) = Domain(chart);

        // The fitted domain (0..20, the zero baseline included) padded by 20 * 0.2 = 4 on each side.
        Approx(min, -4.0);
        Approx(max, 24.0);

        // A wobble well inside that band, and then one pressed against its inner edge.
        Feed(chart, 5, 18);
        var (keptMin, keptMax) = Domain(chart);
        AssertThat(keptMin).IsEqual(min);
        AssertThat(keptMax).IsEqual(max);

        Feed(chart, 0, 21);
        var (stillMin, stillMax) = Domain(chart);
        AssertThat(stillMin).IsEqual(min);
        AssertThat(stillMax).IsEqual(max);
    }

    /// <summary>A value leaving the margin refits the axis, and the new domain still contains the data.</summary>
    [TestCase]
    public void AStickyAxisRefitsWhenTheDataLeavesTheMargin()
    {
        var chart = ValueChart(new FakeCanvas2D(), new AxisConfig { AutoScaleMargin = 0.2f });
        Feed(chart, 10, 20);
        var (min, max) = Domain(chart);

        Feed(chart, 0, 100);
        var (wideMin, wideMax) = Domain(chart);

        AssertThat(wideMin).IsLess(min);
        AssertThat(wideMax).IsGreater(max);
        // Padded again around the new fitted range (0..100), so the data is strictly inside.
        AssertThat(wideMin <= 0.0).IsTrue();
        AssertThat(wideMax >= 100.0).IsTrue();
    }

    /// <summary>
    /// A rebuilt axis is not re-fitted just because the chart rendered again: the held band is the band, so
    /// nothing about the axis moves until the data leaves it.
    /// </summary>
    [TestCase]
    public void RerenderingDoesNotMoveAStickyAxis()
    {
        var chart = ValueChart(new FakeCanvas2D(), new AxisConfig { AutoScaleMargin = 0.2f });
        Feed(chart, 10, 20);
        var (min, max) = Domain(chart);

        for (int frame = 0; frame < 3; frame++)
        {
            chart.Render();
            var (againMin, againMax) = Domain(chart);
            AssertThat(againMin).IsEqual(min);
            AssertThat(againMax).IsEqual(max);
        }
    }

    // ── Nice domain ─────────────────────────────────────────────────────────

    /// <summary>
    /// <see cref="AxisConfig.NiceDomain"/> rounds the padded band out to a {1, 2, 5} x 10^n step, so the
    /// domain jumps in steps instead of drifting by a pixel: both ends land on one shared grid, and the
    /// grid is coarser than the one the raw band would allow.
    /// </summary>
    [TestCase]
    public void NiceDomainLandsOnTheLadder()
    {
        var nice = ValueChart(new FakeCanvas2D(), new AxisConfig { AutoScaleMargin = 0.2f, NiceDomain = true });
        var plain = ValueChart(new FakeCanvas2D(), new AxisConfig { AutoScaleMargin = 0.2f });

        Feed(nice, 10, 20);
        Feed(plain, 10, 20);

        var (plainMin, plainMax) = Domain(plain);
        var (niceMin, niceMax) = Domain(nice);

        // The band -4..24 is rounded out to the next step of the ladder (5): 30 = 6 x 5.
        Approx(plainMin, -4.0);
        Approx(plainMax, 24.0);
        AssertThat(niceMin).IsEqual(-5.0);
        AssertThat(niceMax).IsEqual(25.0);

        // Both ends sit on one shared ladder step, and the rounding went outwards (which is what keeps the
        // data - and the margin the reader is looking at - inside the axis).
        AssertThat(SharedLadderStep(niceMin, niceMax)).IsEqual(5.0);
        AssertThat(SharedLadderStep(niceMin, niceMax)).IsGreater(SharedLadderStep(plainMin, plainMax));
        AssertThat(niceMin <= 0.0).IsTrue();
        AssertThat(niceMax >= 20.0).IsTrue();

        // A wider range lands on a coarser rung of the same ladder.
        Feed(nice, 3, 47);
        var (coarseMin, coarseMax) = Domain(nice);
        AssertThat(SharedLadderStep(coarseMin, coarseMax)).IsEqual(10.0);
        AssertThat(coarseMin <= 0.0).IsTrue();
        AssertThat(coarseMax >= 50.0).IsTrue();
    }

    // ── One-sided pins ──────────────────────────────────────────────────────

    /// <summary>
    /// A one-sided pin fixes one end and leaves the other to the data - "cap the axis, let it grow", which is
    /// what a quote feed wants when a long trend must not flatten the current range.
    /// </summary>
    [TestCase]
    public void OneSidedLimitKeepsTheOtherEndAuto()
    {
        var capped = ValueChart(new FakeCanvas2D(), new AxisConfig { MaxLimit = 60 });
        Feed(capped, 10, 20);
        var (cappedMin, cappedMax) = Domain(capped);
        AssertThat(cappedMax).IsEqual(60.0);
        AssertThat(cappedMin).IsEqual(0.0);        // the fitted lower end (the zero baseline), untouched

        // The pinned end stays, the free one follows: a negative series moves the lower end down, and the
        // cap does not move with it.
        Feed(capped, -30, -10);
        var (lowerMin, lowerMax) = Domain(capped);
        AssertThat(lowerMax).IsEqual(60.0);
        AssertThat(lowerMin).IsLess(cappedMin);
        AssertThat(lowerMin <= -30.0).IsTrue();

        var floored = ValueChart(new FakeCanvas2D(), new AxisConfig { MinLimit = -100 });
        Feed(floored, 10, 20);
        var (floorMin, floorMax) = Domain(floored);
        AssertThat(floorMin).IsEqual(-100.0);
        AssertThat(floorMax).IsEqual(20.0);

        Feed(floored, 10, 80);
        var (grownMin, grownMax) = Domain(floored);
        AssertThat(grownMin).IsEqual(-100.0);
        AssertThat(grownMax).IsGreater(floorMax);
    }

    /// <summary>
    /// A pin outranks the sticky band: the band decides how the automatic end is fitted, and the pinned end
    /// is not overridden by it afterwards.
    /// </summary>
    [TestCase]
    public void APinOutranksTheStickyMargin()
    {
        var chart = ValueChart(new FakeCanvas2D(), new AxisConfig { AutoScaleMargin = 0.2f, MaxLimit = 60 });
        Feed(chart, 10, 20);
        var (min, max) = Domain(chart);
        AssertThat(max).IsEqual(60.0);
        Approx(min, -4.0);      // the sticky band's lower end is still the automatic one

        // The data wobbles inside the band: the automatic end holds, and the pin still holds with it.
        Feed(chart, 5, 18);
        var (keptMin, keptMax) = Domain(chart);
        AssertThat(keptMin).IsEqual(min);
        AssertThat(keptMax).IsEqual(max);
    }

    // ── A gesture outranks the automatic mode ───────────────────────────────

    /// <summary>
    /// Zoom and pan win: once the reader framed the axis, a rebuild must not refit it back to the data (the
    /// two would fight, and the pointer would feel like it lost the axis).
    /// </summary>
    [TestCase]
    public void ZoomAndPanBeatTheAutomaticMode()
    {
        var chart = ValueChart(new FakeCanvas2D(), new AxisConfig { AutoScaleMargin = 0.2f });
        Feed(chart, 10, 20);
        var (min, max) = Domain(chart);

        AssertThat(chart.ZoomDomain(Channel.Y, 0.5)).IsTrue();
        var (zoomMin, zoomMax) = Domain(chart);
        AssertThat(zoomMax - zoomMin).IsLess(max - min);

        chart.Render();
        var (afterZoomMin, afterZoomMax) = Domain(chart);
        AssertThat(afterZoomMin).IsEqual(zoomMin);
        AssertThat(afterZoomMax).IsEqual(zoomMax);

        AssertThat(chart.PanDomain(Channel.Y, 0.25)).IsTrue();
        var (panMin, panMax) = Domain(chart);
        AssertThat(panMin).IsGreater(zoomMin);      // the window really moved

        chart.Render();
        var (afterPanMin, afterPanMax) = Domain(chart);
        AssertThat(afterPanMin).IsEqual(panMin);
        AssertThat(afterPanMax).IsEqual(panMax);
    }

    // ── No knobs: nothing changes ───────────────────────────────────────────

    /// <summary>
    /// The regression guard for every chart that does not ask for the feature: with the knobs off the axis
    /// refits on every data change, exactly as it did before they existed. The contrast with the sticky axis
    /// is what makes this meaningful - both charts are fed the same rows.
    /// </summary>
    [TestCase]
    public void WithoutTheKnobsNothingChanges()
    {
        var bare = ValueChart(new FakeCanvas2D());
        var explicitlyOff = ValueChart(new FakeCanvas2D(), new AxisConfig
        {
            AutoScaleMargin = 0f,
            NiceDomain = false,
            MinLimit = null,
            MaxLimit = null,
        });
        var sticky = ValueChart(new FakeCanvas2D(), new AxisConfig { AutoScaleMargin = 0.2f });

        foreach (var chart in new[] { bare, explicitlyOff, sticky })
        {
            Feed(chart, 10, 20);
            var (min, max) = Domain(chart);
            AssertThat(max - min > 0.0).IsTrue();
        }

        // A new series inside the sticky band but on a different fitted range: the plain axes follow it, the
        // sticky one stays where it is.
        Feed(bare, 12, 25);
        Feed(explicitlyOff, 12, 25);
        Feed(sticky, 12, 25);

        var (bareMin, bareMax) = Domain(bare);
        var (offMin, offMax) = Domain(explicitlyOff);
        var (stickyMin, stickyMax) = Domain(sticky);

        AssertThat(bareMin).IsEqual(0.0);
        AssertThat(bareMax).IsEqual(25.0);
        AssertThat(offMin).IsEqual(bareMin);
        AssertThat(offMax).IsEqual(bareMax);
        AssertThat(stickyMax).IsLess(bareMax);      // the sticky axis never followed the new range
        Approx(stickyMin, -4.0);
        Approx(stickyMax, 24.0);
    }

    // ── The view's exports reach the axis ───────────────────────────────────

    /// <summary>
    /// <see cref="ChartView"/> is how a scene uses the feature (nothing here needs code), so the exports have
    /// to reach <see cref="AxisConfig"/> - including on a view that configures <i>only</i> a knob, with no
    /// axis title or tick setting to build the axis config from.
    /// </summary>
    [TestCase]
    public void TheViewCarriesTheStickyMargin()
    {
        var view = AddView();
        try
        {
            view.YAxisAutoScaleMargin = 0.2f;

            Feed(view, 10, 20);
            var (min, max) = DomainOf(view);
            Approx(min, -4.0);
            Approx(max, 24.0);
        }
        finally
        {
            Release(view);
        }
    }

    /// <summary>The nice domain and the one-sided pins reach the axis the same way.</summary>
    [TestCase]
    public void TheViewCarriesTheNiceDomainAndThePins()
    {
        var nice = AddView();
        var pinned = AddView();
        try
        {
            nice.YAxisAutoScaleMargin = 0.2f;
            nice.YAxisNiceDomain = true;
            Feed(nice, 10, 20);
            var (niceMin, niceMax) = DomainOf(nice);
            AssertThat(niceMin).IsEqual(-5.0);
            AssertThat(niceMax).IsEqual(25.0);

            pinned.YAxisMaxLimit = 60f;
            Feed(pinned, 10, 20);
            var (pinMin, pinMax) = DomainOf(pinned);
            AssertThat(pinMax).IsEqual(60.0);
            AssertThat(pinMin).IsEqual(0.0);
        }
        finally
        {
            Release(nice);
            Release(pinned);
        }
    }

    /// <summary>A view over a numeric x and a y of 10..20, with a fake canvas (no GPU needed).</summary>
    private static ChartView AddView()
    {
        var view = new ChartView
        {
            Size = new Vector2(320f, 220f),
            Kind = ChartKind.Line,
            XField = "x",
            YField = "value",
            CanvasFactory = (_, _) => new FakeCanvas2D(),
        };
        ((SceneTree)Engine.GetMainLoop()).Root.AddChild(view);
        return view;
    }

    /// <summary>Feed the view and run the frames the engine would.</summary>
    private static void Feed(ChartView view, params double[] y)
    {
        var rows = new List<DataRow>(y.Length);
        for (int i = 0; i < y.Length; i++) rows.Add(TestContexts.Row(("x", (double)i), ("value", y[i])));
        view.SetData(rows);
        for (int frame = 0; frame < 2; frame++)
        {
            view._Process(0.016f);
            foreach (var child in view.GetChildren(includeInternal: true))
                if (child is Canvas2DControl surface) surface._Process(0.016f);
        }
    }

    /// <summary>The view's chart window on the Y axis, asserting the view built one.</summary>
    private static (double Min, double Max) DomainOf(ChartView view)
    {
        AssertThat(view.Chart is not null).IsTrue();
        AssertThat(view.Chart!.TryGetDomain(Channel.Y, out double min, out double max)).IsTrue();
        return (min, max);
    }

    /// <summary>Remove the view from the tree and free it.</summary>
    private static void Release(ChartView view)
    {
        try
        {
            view.GetParent()?.RemoveChild(view);
        }
        finally
        {
            view.Free();
        }
    }
}
