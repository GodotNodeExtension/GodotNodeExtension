namespace GodotNodeExtension.Tests.GodotChart;

using System;
using System.Collections.Generic;
using GdUnit4;
using Godot;
using GodotNodeExtension.Component.GodotChart;
using Support;
using static GdUnit4.Assertions;

/// <summary>
/// Behaviour specification for the projection layer of a frame (<see cref="ICoordinateMapper"/> and the
/// 2D implementation <see cref="PlanarMapper"/>): the normalized-to-pixel mapping a Cartesian chart
/// hands its marks, its inverse, and the degenerate cases.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class CoordinateMapperTest
{
    private static readonly PlotArea Plot = new(30f, 20f, 400f, 300f);

    /// <summary>Normalized values a projection is sampled at, corners included.</summary>
    private static readonly double[] Samples = [0.0, 0.25, 0.5, 0.75, 1.0];

    /// <summary>Assert two doubles are within <paramref name="eps"/> (IsEqual is exact for doubles).</summary>
    private static void Approx(double actual, double expected, double eps = 1e-6)
        => AssertThat(Math.Abs(actual - expected) <= eps).IsTrue();

    // ── Mapping ────────────────────────────────────────────────────────────

    [TestCase]
    public void ThePlanarMapperProjectsLikeThePlotRectangle()
    {
        var mapper = new PlanarMapper { Plot = Plot };

        foreach (double x in Samples)
        foreach (double y in Samples)
        {
            var projected = mapper.Project(x, y, 0.0, out float depth);

            AssertThat(projected.X).IsEqual(Plot.MapX(x));
            AssertThat(projected.Y).IsEqual(Plot.MapY(y));
            AssertThat(depth).IsEqual(0f);
        }
    }

    [TestCase]
    public void ThePlanarMapperHasNoDepthAndIgnoresTheZInput()
    {
        var mapper = new PlanarMapper { Plot = Plot };

        var without = mapper.Project(0.3, 0.7, 0.0, out float depthWithout);
        var with = mapper.Project(0.3, 0.7, 0.9, out float depthWith);

        AssertThat(with).IsEqual(without);
        AssertThat(depthWith).IsEqual(depthWithout);
    }

    [TestCase]
    public void UnprojectInvertsThePlanarProjection()
    {
        var mapper = new PlanarMapper { Plot = Plot };

        foreach (double x in Samples)
        foreach (double y in Samples)
        {
            var projected = mapper.Project(x, y, 0.0, out _);

            AssertThat(mapper.TryUnproject(projected, new Vector2(640f, 480f), out double ux, out double uy, out double uz))
                .IsTrue();
            Approx(ux, x, 1e-6);
            Approx(uy, y, 1e-6);
            Approx(uz, 0.0, 1e-6);
        }
    }

    [TestCase]
    public void ADegeneratePlotRectangleIsRefusedInsteadOfDividingByZero()
    {
        foreach (var plot in new[]
                 {
                     new PlotArea(0f, 0f, 0f, 300f),
                     new PlotArea(0f, 0f, 400f, 0f),
                     new PlotArea(0f, 0f, 0f, 0f),
                 })
        {
            var mapper = new PlanarMapper { Plot = plot };

            AssertThat(mapper.TryUnproject(new Vector2(10f, 10f), Vector2.Zero, out double x, out double y, out double z))
                .IsFalse();
            AssertThat(double.IsFinite(x) && double.IsFinite(y) && double.IsFinite(z)).IsTrue();
        }
    }

    // ── Wiring into the render pipeline ────────────────────────────────────

    [TestCase]
    public void AChartHandsItsMarksAMapperOverThePlotRectangle()
    {
        var canvas = new FakeCanvas2D();
        var probe = new MapperProbe();
        var chart = new Chart(canvas) { Width = 320f, Height = 200f };
        chart.Data(new List<DataRow>
        {
            TestContexts.Row(("x", 1.0), ("y", 2.0)),
            TestContexts.Row(("x", 2.0), ("y", 4.0)),
        });
        chart.Mark(probe);
        chart.Encode(Channel.X, "x");
        chart.Encode(Channel.Y, "y");

        chart.Render();

        AssertThat(probe.Mapper is PlanarMapper).IsTrue();
        var projected = probe.Mapper!.Project(0.25, 0.75, 0.0, out float depth);
        AssertThat(projected.X).IsEqual(probe.Plot.MapX(0.25));
        AssertThat(projected.Y).IsEqual(probe.Plot.MapY(0.75));
        AssertThat(depth).IsEqual(0f);
    }

    [TestCase]
    public void TheMapperFollowsALayoutChange()
    {
        var canvas = new FakeCanvas2D();
        var probe = new MapperProbe();
        var chart = new Chart(canvas) { Width = 320f, Height = 200f };
        chart.Data(new List<DataRow> { TestContexts.Row(("x", 1.0), ("y", 2.0)) });
        chart.Mark(probe);
        chart.Encode(Channel.X, "x");
        chart.Encode(Channel.Y, "y");

        chart.Render();
        var projectedBefore = probe.Mapper!.Project(1.0, 1.0, 0.0, out _);

        chart.Width = 520f; // a wider chart moves the plot rectangle and its centre
        chart.Render();
        var projectedAfter = probe.Mapper!.Project(1.0, 1.0, 0.0, out _);

        AssertThat(projectedAfter.X > projectedBefore.X).IsTrue();
        AssertThat(projectedAfter.X).IsEqual(probe.Plot.MapX(1.0));
    }

    /// <summary>Mark that reports the context a chart rendered it with.</summary>
    private sealed class MapperProbe : Mark
    {
        /// <inheritdoc />
        public override MarkCoordinate Coordinate => MarkCoordinate.Cartesian;

        /// <summary>The mapper of the last render.</summary>
        public ICoordinateMapper? Mapper { get; private set; }

        /// <summary>The plot rectangle of the last render.</summary>
        public PlotArea Plot { get; private set; }

        /// <inheritdoc />
        public override void Render(MarkContext ctx)
        {
            Mapper = ctx.Mapper;
            Plot = ctx.Plot;
        }
    }
}
