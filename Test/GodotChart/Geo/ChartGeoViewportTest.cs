namespace GodotNodeExtension.Tests.GodotChart.Geo;

using System;
using System.Collections.Generic;
using GdUnit4;
using Godot;
using GodotNodeExtension.Component.GodotChart;
using Support;
using static GdUnit4.Assertions;

/// <summary>
/// Behaviour specification for the geographic API on <see cref="Chart"/>: the frame a chart starts with,
/// the viewport that only exists once something asks for one, and the layout invalidation every change
/// has to trigger (a cached projection that survives a viewport change is a frozen picture).
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class ChartGeoViewportTest
{
    private static readonly PlotArea Plot = new(0f, 0f, 400f, 300f);

    /// <summary>Assert two doubles are within <paramref name="eps"/> (IsEqual is exact for doubles).</summary>
    private static void Approx(double actual, double expected, double eps = 1e-9)
        => AssertThat(Math.Abs(actual - expected) <= eps).IsTrue();

    /// <summary>A chart with one bar to render, so the pipeline has something to do.</summary>
    private static Chart BarChart(FakeCanvas2D canvas)
    {
        var chart = new Chart(canvas) { Width = 400f, Height = 300f };
        chart.Data(new List<DataRow>
        {
            TestContexts.Row(("category", "A"), ("value", 10.0)),
            TestContexts.Row(("category", "B"), ("value", 20.0)),
        });
        chart.Encode(Channel.X, "category");
        chart.Encode(Channel.Y, "value");
        return chart;
    }

    // ── Frame and viewport lifetime ────────────────────────────────────────

    [TestCase]
    public void AChartIsGeographicByDefaultWithoutCarryingAViewport()
    {
        using var chart = BarChart(new FakeCanvas2D());

        Approx(chart.GeoFrame.Aspect, 1.0);
        AssertThat(chart.GeoFrame.WrapsX).IsTrue();
        AssertThat(chart.GeoViewport is null).IsTrue();
    }

    [TestCase]
    public void SettingAViewCreatesTheViewportAndInvalidatesTheLayout()
    {
        using var chart = BarChart(new FakeCanvas2D());
        int before = chart.EffectiveLayoutVersion;

        chart.SetGeoViewport(116.4, 39.9, 6.0);

        AssertThat(chart.GeoViewport is not null).IsTrue();
        Approx(chart.GeoViewport!.CenterX, 116.4);
        Approx(chart.GeoViewport.CenterY, 39.9);
        Approx(chart.GeoViewport.ZoomLevel, 6.0);
        AssertThat(chart.EffectiveLayoutVersion != before).IsTrue();
    }

    [TestCase]
    public void ChangingTheViewportItselfInvalidatesTheLayout()
    {
        using var chart = BarChart(new FakeCanvas2D());
        chart.SetGeoViewport(0.0, 0.0, 3.0);
        int before = chart.EffectiveLayoutVersion;

        // A host is free to move the viewport it was handed: the chart has to hear about it.
        chart.GeoViewport!.SetZoom(7.0);

        AssertThat(chart.EffectiveLayoutVersion != before).IsTrue();

        before = chart.EffectiveLayoutVersion;
        chart.GeoViewport.PanBy(new Vector2(5f, 5f));
        AssertThat(chart.EffectiveLayoutVersion != before).IsTrue();
    }

    [TestCase]
    public void ChangingTheFrameDropsTheViewportItWasMeasuredIn()
    {
        using var chart = BarChart(new FakeCanvas2D());
        chart.SetGeoViewport(116.4, 39.9, 6.0);
        int before = chart.EffectiveLayoutVersion;
        var frame = GeoFrames.CustomPlane(0.0, 0.0, 1000.0, 500.0);

        chart.SetGeoFrame(frame);

        AssertThat(ReferenceEquals(chart.GeoFrame, frame)).IsTrue();
        AssertThat(chart.GeoViewport is null).IsTrue();
        AssertThat(chart.EffectiveLayoutVersion != before).IsTrue();
    }

    [TestCase]
    public void ANullFrameIsRejected()
    {
        using var chart = BarChart(new FakeCanvas2D());

        _ = Asserts.Throws<ArgumentNullException>(() => chart.SetGeoFrame(null!));
    }

    // ── Navigation through the chart API ───────────────────────────────────

    [TestCase]
    public void FittingBoundsShowsThemInTheViewport()
    {
        using var chart = BarChart(new FakeCanvas2D());

        chart.FitGeoBounds(100.0, 20.0, 110.0, 30.0, Plot, paddingRatio: 0f);

        var viewport = chart.GeoViewport!;
        var visible = viewport.VisibleBounds(Plot);
        AssertThat(visible.MinX <= 100.0 + 1e-6 && visible.MaxX >= 110.0 - 1e-6).IsTrue();
        AssertThat(visible.MinY <= 20.0 + 1e-6 && visible.MaxY >= 30.0 - 1e-6).IsTrue();
    }

    [TestCase]
    public void TheGeoGesturesAreChainable()
    {
        using var chart = BarChart(new FakeCanvas2D());
        var anchor = new Vector2(120f, 80f);

        var returned = chart
            .SetGeoViewport(10.0, 20.0, 3.0)
            .ZoomGeo(1.5, anchor, Plot)
            .PanGeo(new Vector2(6f, -4f))
            .FitGeoWorld(Plot)
            .SetGeoWrapX(false)
            .SetGeoPanBounds(new GeoBounds(-30.0, -30.0, 30.0, 30.0));

        AssertThat(ReferenceEquals(returned, chart)).IsTrue();
        AssertThat(chart.GeoViewport!.WrapsX).IsFalse();
        AssertThat(chart.GeoViewport.PanBounds is not null).IsTrue();
    }

    // ── Isolation from the Cartesian path ──────────────────────────────────

    [TestCase]
    public void ConfiguringAGeoViewportChangesNoPixelOfACartesianChart()
    {
        var plainCanvas = new FakeCanvas2D();
        using var plain = BarChart(plainCanvas);
        plain.Render();

        var geoCanvas = new FakeCanvas2D();
        using var withGeo = BarChart(geoCanvas);
        withGeo.SetGeoViewport(116.4, 39.9, 6.0);
        withGeo.SetGeoPanBounds(new GeoBounds(-180.0, -85.0, 180.0, 85.0));
        withGeo.Render();

        // A chart that carries a frame but no geographic mark has to draw exactly what it drew before
        // the geographic layer existed.
        AssertThat(geoCanvas.FillCount).IsEqual(plainCanvas.FillCount);
        AssertThat(geoCanvas.StrokeCount).IsEqual(plainCanvas.StrokeCount);
        AssertThat(geoCanvas.LineWidths.Count).IsEqual(plainCanvas.LineWidths.Count);
        AssertThat(geoCanvas.RoundRects.Count).IsEqual(plainCanvas.RoundRects.Count);
        AssertThat(geoCanvas.Texts.Count).IsEqual(plainCanvas.Texts.Count);
        AssertThat(string.Join("|", geoCanvas.Texts)).IsEqual(string.Join("|", plainCanvas.Texts));
        AssertThat(geoCanvas.NonFiniteCoordinateCount).IsEqual(0);
    }
}
