namespace GodotNodeExtension.Tests.GodotChart.Marks;

using System;
using GdUnit4;
using GodotNodeExtension.Component.GodotChart;
using static GdUnit4.Assertions;

/// <summary>
/// Contract of <see cref="PlotArea"/>: the normalized-to-screen mapping of X and the flipped Y
/// axis, plus the closed-interval <c>Contains</c> test.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class PlotAreaTest
{
    /// <summary>Approximate comparison for computed floating point values.</summary>
    private static bool Approx(double actual, double expected, double tolerance = 1e-6)
        => Math.Abs(actual - expected) <= tolerance;

    /// <summary>Assert that <paramref name="actual"/> is within <paramref name="tolerance"/> of the expected value.</summary>
    private static void AssertApprox(double actual, double expected, double tolerance = 1e-6)
        => AssertThat(Approx(actual, expected, tolerance)).IsTrue();

    [TestCase]
    public void PlotAreaMapsXAndFlippedY()
    {
        var plot = new PlotArea(10f, 20f, 100f, 200f);

        AssertApprox(plot.MapX(0.0), 10);
        AssertApprox(plot.MapX(0.5), 60);
        AssertApprox(plot.MapX(1.0), 110);

        // the Y axis points up: normalized 0 sits on the bottom edge
        AssertApprox(plot.MapY(0.0), 220);
        AssertApprox(plot.MapY(0.5), 120);
        AssertApprox(plot.MapY(1.0), 20);
    }

    [TestCase]
    public void PlotAreaContainsIsAClosedInterval()
    {
        var plot = new PlotArea(0f, 0f, 400f, 300f);

        AssertThat(plot.Contains(0f, 0f)).IsTrue();
        AssertThat(plot.Contains(200f, 150f)).IsTrue();
        AssertThat(plot.Contains(400f, 300f)).IsTrue();   // the boundary is inclusive
        AssertThat(plot.Contains(0f, 300f)).IsTrue();
        AssertThat(plot.Contains(-0.5f, 150f)).IsFalse();
        AssertThat(plot.Contains(200f, -0.5f)).IsFalse();
        AssertThat(plot.Contains(400.5f, 150f)).IsFalse();
        AssertThat(plot.Contains(200f, 300.5f)).IsFalse();
    }
}
