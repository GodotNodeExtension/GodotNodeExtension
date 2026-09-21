namespace GodotNodeExtension.Tests.GodotChart.Scales;

using System;
using GdUnit4;
using GodotNodeExtension.Component.GodotChart;
using static GdUnit4.Assertions;

/// <summary>
/// Behaviour specification for <see cref="NiceTickGenerator"/> (the "nice number" tick algorithm):
/// covering the requested range, choosing a nice step, clamping the interval count, normalising a
/// reversed domain to ascending order, and the degenerate-range conventions.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class NiceTickGeneratorTest
{
    /// <summary>Assert two doubles are within <paramref name="eps"/> (IsEqual is exact for doubles).</summary>
    private static void Approx(double actual, double expected, double eps = 1e-9)
        => AssertThat(Math.Abs(actual - expected) <= eps).IsTrue();

    // ── Range and step selection ───────────────────────────────────────────

    [TestCase]
    public void NiceTickGeneratorComputesNiceSteps()
    {
        var ticks = NiceTickGenerator.Compute(3, 97);

        Approx(ticks.Min, 0.0);
        Approx(ticks.Max, 100.0);
        Approx(ticks.Step, 20.0);
        AssertThat(ticks.Count).IsEqual(5);
        // The result covers the data.
        AssertThat(ticks.Min <= 3).IsTrue();
        AssertThat(ticks.Max >= 97).IsTrue();
    }

    [TestCase]
    public void NiceTickGeneratorHandlesNegativeDomains()
    {
        var ticks = NiceTickGenerator.Compute(-100, -50);

        Approx(ticks.Min, -100.0);
        Approx(ticks.Max, -50.0);
        Approx(ticks.Step, 10.0);
        AssertThat(ticks.Count).IsEqual(5);
    }

    [TestCase]
    public void NiceTickGeneratorClampsMaxTicksToATwoIntervalMinimum()
    {
        var wide = NiceTickGenerator.Compute(0, 100, 1);
        var twoIntervals = NiceTickGenerator.Compute(0, 100, 2);
        var defaultTicks = NiceTickGenerator.Compute(0, 100);

        // maxTicks < 2 is silently raised to 2.
        Approx(wide.Step, 50.0);
        AssertThat(wide.Count).IsEqual(2);
        AssertThat(wide.Step).IsEqual(twoIntervals.Step);
        AssertThat(defaultTicks.Count).IsEqual(5);
    }

    [TestCase]
    public void NiceTickGeneratorReversedDomainIsNormalisedToAscendingOrder()
    {
        // A reversed caller domain is swapped to ascending order, so it yields exactly the same
        // ticks as the equivalent forward domain (never Min > Max with a negative step).
        var reversed = NiceTickGenerator.Compute(100, 0);
        var forward = NiceTickGenerator.Compute(0, 100);

        Approx(reversed.Min, 0.0);
        Approx(reversed.Max, 100.0);
        Approx(reversed.Step, 20.0);
        AssertThat(reversed.Count).IsEqual(5);
        AssertThat(reversed.Min <= reversed.Max).IsTrue();
        AssertThat(reversed.Step > 0).IsTrue();
        AssertThat(reversed.Step).IsEqual(forward.Step);
        AssertThat(reversed.Count == forward.Count).IsTrue();

        var small = NiceTickGenerator.Compute(10, 0);
        Approx(small.Min, 0.0);
        Approx(small.Max, 10.0);
        Approx(small.Step, 2.0);
        AssertThat(small.Count).IsEqual(5);
    }

    [TestCase]
    public void NiceTickGeneratorCountMatchesTheRangeOverTheStep()
    {
        var ticks = NiceTickGenerator.Compute(0, 100, 20);

        Approx(ticks.Step, 5.0);
        AssertThat(ticks.Count).IsEqual(20);
        AssertThat(ticks.Count).IsEqual((int)Math.Round((ticks.Max - ticks.Min) / ticks.Step));
    }

    [TestCase]
    public void NiceTickGeneratorResultsAreAlignedToTheStep()
    {
        (double Min, double Max)[] ranges =
        {
            (0.0, 100.0),
            (3.0, 97.0),
            (-100.0, -50.0),
            (-3.7, 42.3),
            (1.0, 2.0),
        };

        foreach (var (lo, hi) in ranges)
        {
            var ticks = NiceTickGenerator.Compute(lo, hi);

            AssertThat(ticks.Step > 0).IsTrue();
            AssertThat(ticks.Count >= 1).IsTrue();
            AssertThat(ticks.Min <= lo).IsTrue();
            AssertThat(ticks.Max >= hi).IsTrue();

            // Both bounds are exact multiples of the step.
            double minSteps = ticks.Min / ticks.Step;
            double maxSteps = ticks.Max / ticks.Step;
            Approx(minSteps, Math.Round(minSteps), 1e-6);
            Approx(maxSteps, Math.Round(maxSteps), 1e-6);
        }
    }

    // ── Degenerate domains ─────────────────────────────────────────────────

    [TestCase]
    public void NiceTickGeneratorHandlesDegenerateRanges()
    {
        var single = NiceTickGenerator.Compute(5, 5);
        Approx(single.Min, 4.0);
        Approx(single.Max, 6.0);
        Approx(single.Step, 1.0);
        AssertThat(single.Count).IsEqual(2);

        var zero = NiceTickGenerator.Compute(0, 0);
        Approx(zero.Min, -1.0);
        Approx(zero.Max, 1.0);

        // The degeneracy test is relative: at magnitude 1e12 the tolerance is exactly 1, so a
        // one-unit range counts as equal and gets a synthetic +-1 window (twice the data range).
        var relative = NiceTickGenerator.Compute(1e12, 1e12 + 1);
        Approx(relative.Min, 1e12 - 1);
        Approx(relative.Max, 1e12 + 1);
        AssertThat(relative.Count).IsEqual(2);
    }
}
