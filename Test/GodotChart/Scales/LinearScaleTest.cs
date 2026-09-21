namespace GodotNodeExtension.Tests.GodotChart.Scales;

using System;
using System.Collections.Generic;
using System.Linq;
using GdUnit4;
using GodotNodeExtension.Component.GodotChart;
using static GdUnit4.Assertions;

/// <summary>
/// Behaviour specification for <see cref="LinearScale"/>: linear mapping of a numeric domain to
/// [0, 1], data fitting (zero baseline, skipping non-finite values, non-numeric input), G4
/// formatting, and the degenerate-domain conventions (relative tolerance, centred collapse).
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class LinearScaleTest
{
    /// <summary>Assert two doubles are within <paramref name="eps"/> (IsEqual is exact for doubles).</summary>
    private static void Approx(double actual, double expected, double eps = 1e-9)
        => AssertThat(Math.Abs(actual - expected) <= eps).IsTrue();

    /// <summary>True when <paramref name="action"/> throws exactly <typeparamref name="T"/>.</summary>
    private static bool Throws<T>(Action action) where T : Exception
    {
        try
        {
            action();
            return false;
        }
        catch (T)
        {
            return true;
        }
    }

    // ── Mapping ────────────────────────────────────────────────────────────

    [TestCase]
    public void LinearScaleZeroCrossingDomainCentresAtZero()
    {
        var scale = new LinearScale(-50, 50);

        Approx(scale.Map(-50.0), 0.0);
        Approx(scale.Map(-25.0), 0.25);
        Approx(scale.Map(0.0), 0.5);
        Approx(scale.Map(25.0), 0.75);
        Approx(scale.Map(50.0), 1.0);
    }

    [TestCase]
    public void LinearScaleMapReturnsNaNForNonFiniteValues()
    {
        // A non-finite value has no position: Map reports NaN so callers can skip the element.
        var scale = new LinearScale(0, 100);

        AssertThat(double.IsNaN(scale.Map(double.NaN))).IsTrue();
        AssertThat(double.IsNaN(scale.Map(double.PositiveInfinity))).IsTrue();
        AssertThat(double.IsNaN(scale.Map(double.NegativeInfinity))).IsTrue();
    }

    [TestCase]
    public void LinearScaleMapReportsNaNForNull()
    {
        // A null is "no value", not zero: it reports NaN so the caller skips the element. Treating it as 0
        // used to place it at an arbitrary position - for a domain that does not contain 0 that was even
        // outside the axis.
        AssertThat(double.IsNaN(new LinearScale(0, 100).Map(null!))).IsTrue();
        AssertThat(double.IsNaN(new LinearScale(10, 20).Map(null!))).IsTrue();
    }

    // ── Fitting ────────────────────────────────────────────────────────────

    [TestCase]
    public void LinearScaleFitLeavesTheLowerBoundAloneWhenIncludeZeroIsFalse()
    {
        var scale = new LinearScale { IncludeZero = false };

        scale.Fit(new object[] { 3.0, 7.5 });

        Approx(scale.Min, 3.0);
        Approx(scale.Max, 8.0);
        Approx(scale.Map(5.5), 0.5);
    }

    [TestCase]
    public void LinearScaleFitKeepsThePreviousDomainWhenEveryValueIsNaN()
    {
        var scale = new LinearScale(0, 10);

        scale.Fit(new object[] { double.NaN, double.NaN });

        // A domain without a single usable value leaves the previous range alone.
        Approx(scale.Min, 0.0);
        Approx(scale.Max, 10.0);
    }

    [TestCase]
    public void LinearScaleFitSkipsNullAndNonFiniteValues()
    {
        var scale = new LinearScale();

        // Null means "no value" and NaN / infinity have no position, so neither can widen the
        // domain - the same rule every other scale applies in Fit.
        scale.Fit(new object[] { null!, double.NaN, double.PositiveInfinity, 4.0, 8.0 });

        Approx(scale.Min, 0.0);   // IncludeZero keeps the lower bound at zero
        Approx(scale.Max, 8.0);
    }

    [TestCase]
    public void LinearScaleFitSkipsNaNMixedIntoValidValues()
    {
        var scale = new LinearScale { IncludeZero = false };

        scale.Fit(new object[] { 5.0, double.NaN, 10.0 });

        Approx(scale.Min, 5.0);
        Approx(scale.Max, 10.0);
    }

    /// <summary>
    /// A value that cannot be read as a number is skipped by <c>Fit</c> and reported as NaN by <c>Map</c> -
    /// the same contract the other scales have (Sequential, Diverging and Time always did it this way, and
    /// the linear scale used to be the odd one out that threw).
    /// </summary>
    [TestCase]
    public void LinearScaleFitSkipsAndMapReportsNaNForNonNumericValues()
    {
        var scale = new LinearScale { IncludeZero = false };

        scale.Fit(new[] { new object(), 5.0, "7", 10.0 });

        Approx(scale.Min, 5.0);
        Approx(scale.Max, 10.0);
        AssertThat(double.IsNaN(scale.Map(new object()))).IsTrue();
        AssertThat(double.IsNaN(scale.Map("n/a"))).IsTrue();
        Approx(scale.Map("7"), (7.0 - 5.0) / (10.0 - 5.0));   // a numeric string is a number
    }

    // ── Formatting ─────────────────────────────────────────────────────────

    [TestCase]
    public void LinearScaleFormatUsesSixSignificantDigits()
    {
        var scale = new LinearScale(0, 1000);

        AssertThat(scale.Format(100.0)).IsEqual("100");
        AssertThat(scale.Format(0.5)).IsEqual("0.5");
        AssertThat(scale.Format(-1234.5678)).IsEqual("-1234.57");
        AssertThat(scale.Format(double.NaN)).IsEqual("NaN");
    }

    // ── Nice ticks ─────────────────────────────────────────────────────────

    [TestCase]
    public void LinearScaleConstructorComputesTheNiceTicks()
    {
        var ticks = new LinearScale(0, 100).NiceTicks;

        Approx(ticks.Min, 0.0);
        Approx(ticks.Max, 100.0);
        Approx(ticks.Step, 20.0);
        AssertThat(ticks.Count).IsEqual(5);
    }

    // ── Degenerate domains ─────────────────────────────────────────────────

    [TestCase]
    public void LinearScaleDefaultConstructorHasAnEmptyDomain()
    {
        var scale = new LinearScale();

        // Current convention: a default-constructed scale has a degenerate domain and no ticks.
        Approx(scale.Min, 0.0);
        Approx(scale.Max, 0.0);
        AssertThat(scale.IncludeZero).IsTrue();
        AssertThat(scale.NiceTicks.Count).IsEqual(0);
        Approx(scale.NiceTicks.Step, 0.0);
    }

    [TestCase]
    public void LinearScaleDefaultConstructorMapsEveryValueToTheMiddle()
    {
        // Min == Max == 0 is degenerate, so every finite value lands in the middle of the axis (never on NaN or
        // +-inf) - the same convention the ordinal, colour and time scales use.
        var scale = new LinearScale();

        Approx(scale.Map(5.0), 0.5);
        Approx(scale.Map(-5.0), 0.5);
        Approx(scale.Map(0.0), 0.5);
        // ... while a value without a number has no position at all, degenerate domain or not.
        AssertThat(double.IsNaN(scale.Map(null!))).IsTrue();
    }

    [TestCase]
    public void LinearScaleDegenerateDomainCentresEveryValue()
    {
        var scale = new LinearScale(5, 5);

        Approx(scale.Map(5.0), 0.5);
        Approx(scale.Map(100.0), 0.5); // out-of-domain values collapse as well
    }

    [TestCase]
    public void LinearScaleRelativeToleranceDecidesWhatCountsAsDegenerate()
    {
        // ScaleMath.IsDegenerate compares |max-min| against magnitude*1e-12. At magnitude 1e12 the tolerance is
        // exactly 1, so a one-unit range counts as degenerate and every position collapses onto the middle of
        // the axis (see LinearScale.Map).
        var oneApart = new LinearScale(1e12, 1e12 + 1);
        Approx(oneApart.Map(1e12), 0.5);
        Approx(oneApart.Map(1e12 + 1), 0.5);

        // Two units apart crosses the threshold: the domain is live again.
        var twoApart = new LinearScale(1e12, 1e12 + 2);
        Approx(twoApart.Map(1e12 + 1), 0.5);
    }

    // ── The tick values come from the data ───────────────────────────────────

    /// <summary>One value per year, 1970-2023: the shape a time axis without a date type has.</summary>
    private static LinearScale YearScale()
    {
        var scale = new LinearScale();
        var years = new List<object>();
        for (int year = 1970; year <= 2023; year++) years.Add((double)year);
        scale.Fit(years);
        return scale;
    }

    /// <summary>Axis labels for a scale, as <see cref="DefaultRenderers.ComputeTicks"/> formats them.</summary>
    private static string TickTexts(LinearScale scale, int fallback = 6)
    {
        var ticks = DefaultRenderers.ComputeTicks(scale, maxTicks: 32, fallbackTickCount: fallback);
        var texts = new List<string>(ticks.Count);
        foreach (var (_, text) in ticks) texts.Add(text);
        return string.Join(",", texts);
    }

    /// <summary>
    /// A zoomed window labels the years the table has. Deriving the ticks from the domain instead puts them
    /// on 1982.25 / 1982.5 / 1982.75, which are not years in the data (and, at four significant digits, all
    /// printed as "1982").
    /// </summary>
    [TestCase]
    public void TicksComeFromTheFittedValues()
    {
        var scale = YearScale();
        scale.SetDomain(1980, 1986);

        string joined = TickTexts(scale);
        var seen = new HashSet<string>();
        foreach (string text in joined.Split(','))
        {
            double year = double.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
            Approx(year, System.Math.Round(year));                          // never 1982.25
            AssertThat(scale.FittedValues.Contains(year)).IsTrue();         // and always a real year
            AssertThat(year >= 1980 && year <= 1986).IsTrue();              // inside the window
            AssertThat(seen.Add(text)).IsTrue();                           // no repeated label
        }
    }

    /// <summary>Zoomed all the way in, every tick is a different value of the data.</summary>
    [TestCase]
    public void DeepZoomLabelsOneTickPerValue()
    {
        var scale = YearScale();
        scale.SetDomain(1982, 1983);

        AssertThat(TickTexts(scale)).IsEqual("1982,1983");
    }

    /// <summary>Values that are not evenly spaced are labelled as they are, not as a nice step.</summary>
    [TestCase]
    public void IrregularValuesAreUsedAsIs()
    {
        var scale = new LinearScale();
        scale.Fit([0.0, 1.0, 1.5, 4.2]);

        foreach (string text in TickTexts(scale).Split(','))
        {
            double value = double.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
            AssertThat(scale.FittedValues.Contains(value)).IsTrue();
        }
    }

    /// <summary>
    /// A window holding fewer than two real values cannot sample: the arithmetic step labels it instead
    /// (which is the one path the six-digit format is for).
    /// </summary>
    [TestCase]
    public void AWindowWithoutValuesFallsBackToTheStep()
    {
        var scale = new LinearScale();
        scale.Fit([1.0, 2.0, 3.0]);
        scale.SetDomain(1.2, 1.8);

        string joined = TickTexts(scale);
        AssertThat(joined.Length).IsGreater(0);
        AssertThat(joined.Split(',').Distinct().Count()).IsEqual(joined.Split(',').Length);
    }

    /// <summary>A million values must not be kept: the set is sampled, endpoints included.</summary>
    [TestCase]
    public void ManyValuesAreSampledDown()
    {
        var scale = new LinearScale();
        var values = new List<object>();
        for (int i = 0; i < 1_000_000; i++) values.Add((double)i);
        scale.Fit(values);

        AssertThat(scale.FittedValues.Count).IsLessEqual(2049);
        AssertThat(scale.FittedValues.Count).IsGreater(1000);
        Approx(scale.FittedValues[0], 0.0);
        Approx(scale.FittedValues[^1], 999_999.0);
    }
}
