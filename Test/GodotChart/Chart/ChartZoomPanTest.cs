namespace GodotNodeExtension.Tests.GodotChart;

using System;
using GdUnit4;
using GodotNodeExtension.Component.GodotChart;
using GodotNodeExtension.Component.GodotChart.Marks;
using Support;
using static GdUnit4.Assertions;

/// <summary>
/// The interactive window of a chart (Chart.Scales.cs): zooming around a point of the current window,
/// panning by a fraction of it, reading the effective domain, and the layering between an interactive zoom
/// and a domain the host pinned with <see cref="Chart.ScaleDomain(Channel, double, double)"/>.
/// <para>
/// The numbers are checked against a chart whose domain is known: two points, <c>x</c> 0..100 and
/// <c>y</c> 0..50, so every expected boundary can be written by hand.
/// </para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class ChartZoomPanTest
{
    private const double Tolerance = 1e-9;

    /// <summary>A line chart over two points: x 0..100 (no zero baseline on X) and y 0..50.</summary>
    private static Chart NumericChart()
    {
        var chart = new Chart(new FakeCanvas2D());
        chart.Data(
        [
            TestContexts.Row(("x", 0.0), ("y", 0.0)),
            TestContexts.Row(("x", 100.0), ("y", 50.0)),
        ]);
        chart.Encode(Channel.X, "x");
        chart.Encode(Channel.Y, "y");
        chart.Mark(new LineMark());
        chart.Render();     // fits the scales and records the base domains
        return chart;
    }

    /// <summary>The chart's window on a channel, asserting it has one.</summary>
    private static (double Min, double Max) Window(Chart chart, Channel channel)
    {
        AssertThat(chart.TryGetDomain(channel, out double min, out double max)).IsTrue();
        return (min, max);
    }

    private static void Approx(double actual, double expected)
        => AssertThat(Math.Abs(actual - expected) <= Tolerance || Math.Abs(actual - expected) <= Math.Abs(expected) * 1e-9)
            .IsTrue();

    [TestCase]
    public void ZoomNarrowsTheWindowAroundTheMiddle()
    {
        var chart = NumericChart();
        var (min, max) = Window(chart, Channel.X);

        AssertThat(chart.ZoomDomain(Channel.X, 0.5, 0.5)).IsTrue();

        var (zoomMin, zoomMax) = Window(chart, Channel.X);
        Approx(zoomMax - zoomMin, (max - min) * 0.5);
        Approx(zoomMin + zoomMax, min + max);       // the middle stayed put
    }

    [TestCase]
    public void ZoomKeepsTheFocusedValueInPlace()
    {
        var chart = NumericChart();
        var (min, max) = Window(chart, Channel.X);
        double width = max - min;

        // Focus on the low end: the lower bound may not move, the upper one has to come in by the same width.
        AssertThat(chart.ZoomDomain(Channel.X, 0.5, 0.0)).IsTrue();

        var (zoomMin, zoomMax) = Window(chart, Channel.X);
        Approx(zoomMin, min);
        Approx(zoomMax, min + width * 0.5);
    }

    [TestCase]
    public void ZoomOutStopsAtTheDataAndPanCannotLeaveIt()
    {
        var chart = NumericChart();
        var (min, max) = Window(chart, Channel.X);

        // Zooming out beyond the data is clamped: the window becomes the whole domain again, which is not a
        // zoom any more, so the zoom layer stays empty and ResetZoom has nothing to undo.
        AssertThat(chart.ZoomDomain(Channel.X, 4.0, 0.5)).IsTrue();
        var (wideMin, wideMax) = Window(chart, Channel.X);
        Approx(wideMin, min);
        Approx(wideMax, max);

        // Panning far to the right parks the window against the upper bound instead of losing the data.
        chart.ZoomDomain(Channel.X, 0.25, 0.5);
        AssertThat(chart.PanDomain(Channel.X, 100.0)).IsTrue();
        var (panMin, panMax) = Window(chart, Channel.X);
        Approx(panMax, max);
        Approx(panMax - panMin, (max - min) * 0.25);

        // ... and far to the left against the lower bound.
        AssertThat(chart.PanDomain(Channel.X, -100.0)).IsTrue();
        var (backMin, backMax) = Window(chart, Channel.X);
        Approx(backMin, min);
        Approx(backMax - backMin, (max - min) * 0.25);
    }

    [TestCase]
    public void PanMovesByAFractionOfTheWindow()
    {
        var chart = NumericChart();
        var (min, max) = Window(chart, Channel.Y);

        AssertThat(chart.ZoomDomain(Channel.Y, 0.5, 0.5)).IsTrue();
        var (zoomMin, zoomMax) = Window(chart, Channel.Y);

        AssertThat(chart.PanDomain(Channel.Y, 0.5)).IsTrue();
        var (panMin, panMax) = Window(chart, Channel.Y);
        Approx(panMin, zoomMin + (zoomMax - zoomMin) * 0.5);
        Approx(panMax - panMin, zoomMax - zoomMin);
        AssertThat(panMax <= max).IsTrue();
    }

    [TestCase]
    public void ResetZoomKeepsAPinnedDomain()
    {
        var chart = NumericChart();
        chart.ScaleDomain(Channel.Y, 0, 4);     // the host's decision, not a gesture
        chart.Render();

        var pinned = Window(chart, Channel.Y);
        Approx(pinned.Min, 0);
        Approx(pinned.Max, 4);

        AssertThat(chart.ZoomDomain(Channel.Y, 0.5, 0.5)).IsTrue();
        var zoomed = Window(chart, Channel.Y);
        Approx(zoomed.Max - zoomed.Min, 2);

        chart.ResetZoom();

        var after = Window(chart, Channel.Y);
        Approx(after.Min, 0);
        Approx(after.Max, 4);
    }

    [TestCase]
    public void ACategoryAxisHasNoWindowToZoom()
    {
        var chart = new Chart(new FakeCanvas2D());
        chart.Data([TestContexts.Row(("category", "Mon"), ("value", 3.0)),
                    TestContexts.Row(("category", "Tue"), ("value", 5.0))]);
        chart.Encode(Channel.X, "category");
        chart.Encode(Channel.Y, "value");
        chart.Mark(new IntervalMark());
        chart.Render();

        AssertThat(chart.TryGetDomain(Channel.X, out _, out _)).IsFalse();
        AssertThat(chart.ZoomDomain(Channel.X, 0.5)).IsFalse();
        AssertThat(chart.PanDomain(Channel.X, 0.5)).IsFalse();

        // The value axis of the same chart zooms normally.
        AssertThat(chart.ZoomDomain(Channel.Y, 0.5)).IsTrue();
    }

    [TestCase]
    public void ADataChangeReclampsAnExistingZoom()
    {
        var chart = NumericChart();
        chart.ZoomDomain(Channel.X, 0.5, 0.5);

        // New data on a different range: the window has to end up inside the new domain, not half outside it.
        chart.Data(
        [
            TestContexts.Row(("x", 200.0), ("y", 0.0)),
            TestContexts.Row(("x", 400.0), ("y", 50.0)),
        ]);
        chart.Render();

        var (min, max) = Window(chart, Channel.X);
        AssertThat(min >= 200.0).IsTrue();
        AssertThat(max <= 400.0).IsTrue();
        AssertThat(max > min).IsTrue();
    }

    /// <summary>
    /// A rebuild must not re-base the zoom on its own window, and zooming out has to widen the window again.
    /// Recording the base from the scales after the fit made every rebuild adopt the narrowed window as "the
    /// data": zooming in then looked unbounded, zooming back out became a no-op, and dropping a window left
    /// the scale sitting on it.
    /// </summary>
    [TestCase]
    public void ARebuildKeepsTheBaseDomainAndZoomingBackOutWorks()
    {
        var chart = NumericChart();
        var (dataMin, dataMax) = Window(chart, Channel.X);
        double dataWidth = dataMax - dataMin;

        AssertThat(chart.ZoomDomain(Channel.X, 0.5, 0.5)).IsTrue();
        chart.Render();     // this fit is what applies the window to the scale
        var (zoomMin, zoomMax) = Window(chart, Channel.X);
        Approx(zoomMax - zoomMin, dataWidth * 0.5);

        chart.Render();     // a rebuild used to adopt the window as the base domain
        var (afterMin, afterMax) = Window(chart, Channel.X);
        Approx(afterMin, zoomMin);
        Approx(afterMax, zoomMax);

        // Zooming out widens the window back to the data instead of stopping at the window it was on.
        AssertThat(chart.ZoomDomain(Channel.X, 4.0, 0.5)).IsTrue();
        chart.Render();
        var (wideMin, wideMax) = Window(chart, Channel.X);
        Approx(wideMin, dataMin);
        Approx(wideMax, dataMax);
    }

    /// <summary>Zooming in stops at the smallest window instead of collapsing towards a point.</summary>
    [TestCase]
    public void ZoomingInStopsAtTheSmallestWindow()
    {
        var chart = NumericChart();
        var (dataMin, dataMax) = Window(chart, Channel.X);
        double dataWidth = dataMax - dataMin;

        for (int step = 0; step < 200; step++)
        {
            AssertThat(chart.ZoomDomain(Channel.X, 0.5, 0.5)).IsTrue();
            chart.Render();     // a rebuild used to shrink the floor along with the window
        }

        var (min, max) = Window(chart, Channel.X);
        double width = max - min;
        AssertThat(width).IsGreater(0.0);
        // The floor has to be reachable and visible: a twentieth of the data (see MinZoomWidthFraction), not
        // the sub-pixel window that made a deep zoom look like it had no end at all.
        Approx(width, dataWidth / 20.0);
    }

    /// <summary>The plot rectangle is the chart's, not the gesture's: zooming must not move it.</summary>
    [TestCase]
    public void ZoomingDoesNotMoveThePlotRectangle()
    {
        var chart = NumericChart();
        var before = chart.CurrentPlotArea;

        AssertThat(chart.ZoomDomain(Channel.X, 0.5, 0.5)).IsTrue();
        chart.Render();
        AssertThat(chart.CurrentPlotArea).IsEqual(before);

        AssertThat(chart.ZoomDomain(Channel.X, 2.0, 0.5)).IsTrue();
        chart.Render();
        AssertThat(chart.CurrentPlotArea).IsEqual(before);
    }
}
