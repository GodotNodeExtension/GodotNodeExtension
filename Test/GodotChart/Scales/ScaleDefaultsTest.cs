namespace GodotNodeExtension.Tests.GodotChart.Scales;

using System;
using GdUnit4;
using GodotNodeExtension.Component.GodotChart;
using static GdUnit4.Assertions;

/// <summary>
/// Cross-cutting default conventions shared by the scale implementations: mapping a domain to the
/// unit range, the IncludeZero baseline, empty/degenerate domains, non-finite values, and the
/// default colour/degenerate-domain agreement between the ordinal, categorical, sequential and
/// diverging scales.
/// <para>
/// Only the conventions live here: the per-implementation details (a scale's own formatting, its own
/// non-finite answer, its own single-category placement) are asserted in that implementation's suite,
/// which is where a change to it belongs. Five cases used to be repeated here and were removed.
/// </para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class ScaleDefaultsTest
{
    /// <summary>Assert two doubles are within <paramref name="eps"/> (IsEqual is exact for doubles).</summary>
    private static void Approx(double actual, double expected, double eps = 1e-9)
        => AssertThat(Math.Abs(actual - expected) <= eps).IsTrue();

    // ── LinearScale defaults ───────────────────────────────────────────────

    [TestCase]
    public void SetDomainPinsTheRangeOverWhateverFitComputed()
    {
        var scale = new LinearScale();
        scale.Fit(new object[] { 20.0, 30.0 });

        scale.SetDomain(0, 200);

        AssertThat(scale.Min).IsEqual(0.0);
        AssertThat(scale.Max).IsEqual(200.0);
        Approx(scale.Map(100.0), 0.5);
        Approx(scale.Map(50.0), 0.25);

        // A reversed call is swapped instead of collapsing the range.
        scale.SetDomain(200, 0);
        Approx(scale.Map(100.0), 0.5);
    }

    [TestCase]
    public void LinearScaleMapsDomainToUnitRange()
    {
        var scale = new LinearScale(0, 100);

        Approx(scale.Map(0), 0.0);
        Approx(scale.Map(50), 0.5);
        Approx(scale.Map(100), 1.0);
    }

    [TestCase]
    public void LinearScaleKeepsRangeWhenFittedWithEmptyDomain()
    {
        var scale = new LinearScale(0, 10);

        scale.Fit(Array.Empty<object>());

        Approx(scale.Min, 0.0);
        Approx(scale.Max, 10.0);
    }

    [TestCase]
    public void LinearScaleFitIncludesZeroByDefault()
    {
        var scale = new LinearScale();

        scale.Fit(new object[] { 5.0, 10.0 });

        Approx(scale.Min, 0.0);
        AssertThat(scale.Max >= 10.0).IsTrue();
    }

    [TestCase]
    public void LinearScaleFitKeepsNegativeRange()
    {
        var scale = new LinearScale();

        scale.Fit(new object[] { -8.0, -2.0 });

        AssertThat(scale.Min <= -8.0).IsTrue();
        AssertThat(scale.Max >= -2.0).IsTrue();
        // Default convention: IncludeZero only extends the lower bound (Math.Min(0, dataMin)); an
        // all-negative range keeps Max < 0, so callers must place their baseline at the data zero
        // line instead of assuming the plot bottom.
        AssertThat(scale.Max < 0.0).IsTrue();
    }

    [TestCase]
    public void LinearScaleMapIsNotClamped()
    {
        // Marks (e.g. GaugeMark) clamp themselves; the scale reports the unclamped position.
        var scale = new LinearScale(0, 10);

        Approx(scale.Map(20), 2.0);
        Approx(scale.Map(-10), -1.0);
    }

    // ── Degenerate domains (relative tolerance) ────────────────────────────

    [TestCase]
    public void LargeDomainsAreNotTreatedAsDegenerate()
    {
        // A 1e12-magnitude range: an absolute epsilon would divide by a range that is not "zero".
        var scale = new LinearScale(1e12, 1e12 + 1e6);

        AssertThat(double.IsFinite(scale.Map(1e12 + 5e5))).IsTrue();
        Approx(scale.Map(1e12), 0.0);
    }

    [TestCase]
    public void TinyDomainsAreTreatedAsDegenerate()
    {
        // A 1e-15 range: an absolute epsilon missed this and produced infinities.
        var scale = new LinearScale(0, 1e-15);

        Approx(scale.Map(5e-16), 0.0);
    }

    // ── Ordinal / categorical defaults ─────────────────────────────────────

    [TestCase]
    public void OrdinalScalePlacesCategoriesEvenly()
    {
        var scale = new OrdinalScale();
        scale.Fit(new object[] { "A", "B", "C" });

        AssertThat(scale.Domain.Count).IsEqual(3);
        Approx(scale.Map("A"), 1.0 / 6.0);
        Approx(scale.Map("B"), 0.5);
        Approx(scale.Map("C"), 5.0 / 6.0);
    }

    [TestCase]
    public void ColourScalesAgreeWithTheOrdinalScaleForASingleCategory()
    {
        var ordinal = new OrdinalScale();
        ordinal.Fit(new object[] { "only" });
        var colour = new ColorScale();
        colour.Fit(new object[] { "only" });

        Approx(colour.Map("only"), ordinal.Map("only"));
    }

    // ── Diverging defaults ─────────────────────────────────────────────────

    [TestCase]
    public void DivergingScaleStillFadesThroughTheMidpointInsideTheDomain()
    {
        var scale = new DivergingColorScale(-1, 1) { MidPoint = 0 };

        AssertThat(scale.MapColor(0.0).ToHtml()).IsEqual(scale.MidColor.ToHtml());
        AssertThat(scale.MapColor(-1.0).ToHtml()).IsEqual(scale.NegativeColor.ToHtml());
        AssertThat(scale.MapColor(1.0).ToHtml()).IsEqual(scale.PositiveColor.ToHtml());
    }
}
