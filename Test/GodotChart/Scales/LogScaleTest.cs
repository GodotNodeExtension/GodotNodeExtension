namespace GodotNodeExtension.Tests.GodotChart.Scales;

using System;
using GdUnit4;
using GodotNodeExtension.Component.GodotChart;
using static GdUnit4.Assertions;

/// <summary>
/// Behaviour specification for <see cref="LogScale"/>: log10 mapping and clamping, domain
/// construction/fitting (clamping non-positive bounds, skipping non-positive and non-finite
/// values, snapping to decades) and K/M label formatting.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class LogScaleTest
{
    /// <summary>Assert two doubles are within <paramref name="eps"/> (IsEqual is exact for doubles).</summary>
    private static void Approx(double actual, double expected, double eps = 1e-9)
        => AssertThat(Math.Abs(actual - expected) <= eps).IsTrue();

    // ── Domain construction ────────────────────────────────────────────────

    [TestCase]
    public void LogScaleDefaultDomainIsOneToHundred()
    {
        var scale = new LogScale();

        Approx(scale.Min, 1.0);
        Approx(scale.Max, 100.0);
        Approx(scale.Map(1.0), 0.0);
        Approx(scale.Map(10.0), 0.5);
        Approx(scale.Map(100.0), 1.0);
    }

    [TestCase]
    public void LogScaleConstructorClampsNonPositiveBounds()
    {
        var nonPositiveMin = new LogScale(-5, 100);
        Approx(nonPositiveMin.Min, 1.0);
        Approx(nonPositiveMin.Max, 100.0);

        var bothNonPositive = new LogScale(-5, -2);
        Approx(bothNonPositive.Min, 1.0);
        Approx(bothNonPositive.Max, 10.0); // Max = Min * 10

        var zeros = new LogScale(0, 0);
        Approx(zeros.Min, 1.0);
        Approx(zeros.Max, 10.0);
    }

    [TestCase]
    public void LogScaleConstructorRepairsReversedAndDegenerateDomains()
    {
        var reversed = new LogScale(100, 10); // max <= min -> max becomes min * 10
        Approx(reversed.Min, 100.0);
        Approx(reversed.Max, 1000.0);

        var degenerate = new LogScale(5, 5);
        Approx(degenerate.Min, 5.0);
        Approx(degenerate.Max, 50.0);
    }

    // ── Fitting ────────────────────────────────────────────────────────────

    [TestCase]
    public void LogScaleFitSingleValueSpansADecadeAroundIt()
    {
        var scale = new LogScale();

        scale.Fit(new object[] { 50.0 });

        Approx(scale.Min, 5.0);
        Approx(scale.Max, 500.0);
        Approx(scale.Map(50.0), 0.5); // the value sits in the middle instead of at the bottom
    }

    [TestCase]
    public void LogScaleFitSnapsTheDomainToDecades()
    {
        var scale = new LogScale();

        scale.Fit(new object[] { 2.0, 800.0 });

        Approx(scale.Min, 1.0);
        Approx(scale.Max, 1000.0);
        Approx(scale.Map(2.0), Math.Log10(2.0) / 3.0);
    }

    [TestCase]
    public void LogScaleFitIgnoresNonPositiveValues()
    {
        var scale = new LogScale();

        scale.Fit(new object[] { -5.0, 10.0 });

        // Only 10.0 survives; a single distinct value gives one decade below and above it.
        Approx(scale.Min, 1.0);
        Approx(scale.Max, 100.0);
    }

    [TestCase]
    public void LogScaleFitKeepsTheDomainWhenEveryValueIsNonPositive()
    {
        var scale = new LogScale(1, 100);

        scale.Fit(new object[] { -1.0, 0.0 });

        // Current convention: non-positive values are skipped, and a fit with nothing usable only
        // pushes a warning while leaving the previous range in place.
        Approx(scale.Min, 1.0);
        Approx(scale.Max, 100.0);
    }

    [TestCase]
    public void LogScaleFitOfOnlyNonFiniteValuesKeepsTheDomain()
    {
        var scale = new LogScale(1, 100);

        scale.Fit(new object[] { double.NaN });

        // Non-finite values (NaN, +-inf) are skipped like non-positive ones, so a fit that finds no
        // usable value keeps the current domain instead of overflowing the upper bound.
        Approx(scale.Min, 1.0);
        Approx(scale.Max, 100.0);
        Approx(scale.Map(10.0), 0.5);

        scale.Fit(new object[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity });
        Approx(scale.Min, 1.0);
        Approx(scale.Max, 100.0);
    }

    // ── Mapping ────────────────────────────────────────────────────────────

    [TestCase]
    public void LogScaleMapReportsNaNForNonPositiveValues()
    {
        var scale = new LogScale(10, 1000);

        // A logarithmic axis has no place for zero or negative values. They used to be folded onto the lower
        // bound, which drew them at the bottom of the axis as if they were real data; NaN says "no position"
        // and lets the caller skip them.
        AssertThat(double.IsNaN(scale.Map(0.0))).IsTrue();
        AssertThat(double.IsNaN(scale.Map(-123.0))).IsTrue();
        Approx(scale.Map(10.0), 0.0);
        Approx(scale.Map(100.0), 0.5);
        Approx(scale.Map(1000.0), 1.0);
    }

    [TestCase]
    public void LogScaleMapClampsOutOfRangeValues()
    {
        var log = new LogScale(1, 100);
        var linear = new LinearScale(1, 100);

        // The log scale clamps; LinearScale reports the raw position outside [0, 1].
        Approx(log.Map(0.01), 0.0);
        Approx(log.Map(10000.0), 1.0);

        Approx(linear.Map(0.01), (0.01 - 1) / 99.0);
        Approx(linear.Map(10000.0), (10000.0 - 1) / 99.0);
        AssertThat(linear.Map(10000.0) > 1.0).IsTrue();
    }

    [TestCase]
    public void LogScaleMapHandlesNonFiniteValues()
    {
        var scale = new LogScale(1, 100);

        // No infinity gets a position either: both are "no finite number", which is the same answer the
        // linear scale gives (and unlike the old code, which clamped +Infinity onto the top of the axis).
        AssertThat(double.IsNaN(scale.Map(double.NaN))).IsTrue();
        AssertThat(double.IsNaN(scale.Map(double.PositiveInfinity))).IsTrue();
        AssertThat(double.IsNaN(scale.Map(double.NegativeInfinity))).IsTrue();
    }

    // ── Formatting ─────────────────────────────────────────────────────────

    /// <summary>
    /// K/M suffixes for a large domain.
    /// <para>
    /// <b>Known convention</b> - an intentional, <c>[KnownConvention]</c>-style note used by this suite: the last
    /// assertion pins today's behaviour, <i>not</i> a desirable property. <c>Format(999_999)</c> is
    /// <c>"1000K"</c>, because the K mantissa is fixed point (<c>"0.##"</c>) and a mantissa that rounds up to four
    /// digits is not carried into the next suffix. Such a case is still worth keeping: it turns a changed
    /// formatting rule into a deliberate decision instead of a silent side effect of a refactor.
    /// </para>
    /// </summary>
    [TestCase]
    public void LogScaleFormatUsesKAndMSuffixes()
    {
        var scale = new LogScale(1, 1e9);

        AssertThat(scale.Format(2_500_000.0)).IsEqual("2.5M");
        AssertThat(scale.Format(2_000_000.0)).IsEqual("2M");
        AssertThat(scale.Format(1_234_000.0)).IsEqual("1.23M");
        AssertThat(scale.Format(1_234_567.0)).IsEqual("1.23M");
        AssertThat(scale.Format(1_000_000.0)).IsEqual("1M");
        AssertThat(scale.Format(1_500.0)).IsEqual("1.5K");
        AssertThat(scale.Format(999.0)).IsEqual("999");
        AssertThat(scale.Format(0.5)).IsEqual("0.5");
        AssertThat(scale.Format(-2500.0)).IsEqual("-2500");
        // The mantissa is fixed point ("0.##"), so the thresholds sit one rounding step below the next unit:
        // without that, 999_999 printed its mantissa as "1000" while keeping the "K" suffix ("1000K" for a
        // mega value).
        AssertThat(scale.Format(999_999.0)).IsEqual("1M");
        AssertThat(scale.Format(999_499.0)).IsEqual("999.5K");
        AssertThat(scale.Format(999.4)).IsEqual("999.4");
    }
}
