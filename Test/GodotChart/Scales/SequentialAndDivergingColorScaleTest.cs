namespace GodotNodeExtension.Tests.GodotChart.Scales;

using System;
using GdUnit4;
using Godot;
using GodotNodeExtension.Component.GodotChart;
using static GdUnit4.Assertions;

/// <summary>
/// Behaviour specification for the continuous colour scales <see cref="SequentialColorScale"/> and
/// <see cref="DivergingColorScale"/>: value mapping and clamping, gradient interpolation, fitting
/// (symmetric/asymmetric divergence, empty and constant domains) and degenerate colour output.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class SequentialAndDivergingColorScaleTest
{
    /// <summary>Assert two doubles are within <paramref name="eps"/> (IsEqual is exact for doubles).</summary>
    private static void Approx(double actual, double expected, double eps = 1e-9)
        => AssertThat(Math.Abs(actual - expected) <= eps).IsTrue();

    /// <summary>A three-stop red/green/blue gradient (exact components, no palette dependency).</summary>
    private static Color[] RedGreenBlue() =>
        new[] { new Color(1f, 0f, 0f), new Color(0f, 1f, 0f), new Color(0f, 0f, 1f) };

    // ══ SequentialColorScale ═══════════════════════════════════════════════

    // ── Mapping ────────────────────────────────────────────────────────────

    [TestCase]
    public void SequentialColorScaleMapClampsToUnitRange()
    {
        var scale = new SequentialColorScale(0, 10);

        Approx(scale.Map(-5.0), 0.0);
        Approx(scale.Map(5.0), 0.5);
        Approx(scale.Map(20.0), 1.0);
    }

    [TestCase]
    public void SequentialColorScaleMapReturnsNaNForNonFiniteValues()
    {
        var scale = new SequentialColorScale(0, 10);

        AssertThat(double.IsNaN(scale.Map(double.NaN))).IsTrue();
        AssertThat(double.IsNaN(scale.Map(double.PositiveInfinity))).IsTrue();
    }

    [TestCase]
    public void SequentialColorScaleMapTreatsNullAsZero()
    {
        Approx(new SequentialColorScale(0, 10).Map(null!), 0.0);
        // A domain that does not contain 0 clamps the substituted 0 up to the low end.
        Approx(new SequentialColorScale(5, 10).Map(null!), 0.0);

        var scale = new SequentialColorScale(0, 1) { Gradient = RedGreenBlue() };
        AssertThat(scale.MapColor(null!).ToHtml()).IsEqual(scale.Gradient[0].ToHtml());
    }

    // ── Colour interpolation ───────────────────────────────────────────────

    [TestCase]
    public void SequentialColorScaleMapColorInterpolatesBetweenStops()
    {
        var stops = RedGreenBlue();
        var scale = new SequentialColorScale(0, 1) { Gradient = stops };

        AssertThat(scale.MapColor(0.0).ToHtml()).IsEqual(stops[0].ToHtml());
        AssertThat(scale.MapColor(1.0).ToHtml()).IsEqual(stops[2].ToHtml());

        var quarter = scale.MapColor(0.25); // pos = 0.5 -> halfway red..green
        Approx(quarter.R, 0.5, 1e-6);
        Approx(quarter.G, 0.5, 1e-6);
        Approx(quarter.B, 0.0, 1e-6);

        var threeQuarters = scale.MapColor(0.75); // pos = 1.5 -> halfway green..blue
        Approx(threeQuarters.R, 0.0, 1e-6);
        Approx(threeQuarters.G, 0.5, 1e-6);
        Approx(threeQuarters.B, 0.5, 1e-6);
    }

    [TestCase]
    public void SequentialColorScaleMapColorHitsTheMiddleStopAtHalf()
    {
        var stops = RedGreenBlue();
        var scale = new SequentialColorScale(0, 1) { Gradient = stops };

        // pos = 0.5 * (3 - 1) = 1 -> exactly the middle stop, no blending.
        AssertThat(scale.MapColor(0.5).ToHtml()).IsEqual(stops[1].ToHtml());
    }

    [TestCase]
    public void SequentialColorScaleWithASingleStopRepeatsIt()
    {
        var scale = new SequentialColorScale(0, 1) { Gradient = new[] { new Color(1f, 0f, 0f) } };

        AssertThat(scale.MapColor(0.0).ToHtml()).IsEqual(new Color(1f, 0f, 0f).ToHtml());
        AssertThat(scale.MapColor(1.0).ToHtml()).IsEqual(new Color(1f, 0f, 0f).ToHtml());
    }

    [TestCase]
    public void SequentialColorScaleEmptyGradientFallsBackToTheDefault()
    {
        var scale = new SequentialColorScale(0, 1) { Gradient = [] };

        // An empty gradient means "use the default ramp": the value still maps, and the colour is
        // the one the default ramp produces for it.
        Approx(scale.Map(0.5), 0.5);
        AssertThat(scale.MapColor(0.5).ToHtml())
            .IsEqual(SequentialColorScale.DefaultGradient[2].ToHtml());
    }

    [TestCase]
    public void SequentialColorScaleMapColorOfNonFiniteValuesUsesTheRampMidpoint()
    {
        var scale = new SequentialColorScale(0, 1);

        // A value without a position has no gradient position either; the ramp midpoint is the
        // "no position" colour, matching the diverging scale's MidColor.
        AssertThat(scale.MapColor(double.NaN).ToHtml())
            .IsEqual(SequentialColorScale.DefaultGradient[2].ToHtml());
        AssertThat(scale.MapColor(double.PositiveInfinity).ToHtml())
            .IsEqual(SequentialColorScale.DefaultGradient[2].ToHtml());
    }

    [TestCase]
    public void SequentialColorScaleInstanceGradientsAreIsolated()
    {
        var first = new SequentialColorScale();
        var second = new SequentialColorScale();

        // The constructor clones the default ramp per instance, so an edit of one instance's stops
        // cannot repaint another chart.
        AssertThat(ReferenceEquals(first.Gradient, second.Gradient)).IsFalse();
        first.Gradient[0] = new Color(1f, 1f, 1f);
        AssertThat(second.Gradient[0].ToHtml())
            .IsEqual(SequentialColorScale.DefaultGradient[0].ToHtml());
    }

    /// <summary>
    /// <see cref="SequentialColorScale.DefaultGradient"/> follows the same "read-only by copy" rule as
    /// <see cref="ChartTheme.DefaultPalette"/>: every read is a fresh array, so writing into the returned
    /// stops cannot change the initial gradient of any scale built afterwards.
    /// </summary>
    [TestCase]
    public void SequentialColorScaleDefaultGradientIsReadOnlyByCopy()
    {
        var firstGradient = SequentialColorScale.DefaultGradient;
        var secondGradient = SequentialColorScale.DefaultGradient;
        AssertThat(ReferenceEquals(firstGradient, secondGradient)).IsFalse();

        var stops = SequentialColorScale.DefaultGradient;
        stops[0] = new Color(1f, 1f, 1f);

        // The write stays in the caller's array: the next scale starts on the documented ramp.
        AssertThat(SequentialColorScale.DefaultGradient[0].ToHtml())
            .IsNotEqual(Colors.White.ToHtml());
        AssertThat(new SequentialColorScale().Gradient[0].ToHtml())
            .IsEqual(SequentialColorScale.DefaultGradient[0].ToHtml());
    }

    // ── Fitting / degenerate domains ───────────────────────────────────────

    [TestCase]
    public void SequentialColorScaleDefaultConstructorIsEmptyWithFiveStops()
    {
        var scale = new SequentialColorScale();

        Approx(scale.Min, 0.0);
        Approx(scale.Max, 0.0);
        AssertThat(scale.Gradient.Length).IsEqual(5);
        Approx(scale.Map(5.0), 0.5); // degenerate domain -> middle of the ramp
    }

    [TestCase]
    public void SequentialColorScaleDegenerateDomainReturnsHalf()
    {
        var scale = new SequentialColorScale(7, 7);

        Approx(scale.Map(7.0), 0.5);
        Approx(scale.Map(1000.0), 0.5); // the value is ignored while the domain is degenerate
    }

    [TestCase]
    public void SequentialColorScaleFitWithEmptyDataKeepsTheDomain()
    {
        var scale = new SequentialColorScale(0, 10);

        scale.Fit(Array.Empty<object>());

        Approx(scale.Min, 0.0);
        Approx(scale.Max, 10.0);
    }

    [TestCase]
    public void SequentialColorScaleFitKeepsAConstantDomainDegenerate()
    {
        var scale = new SequentialColorScale();
        scale.Fit(new object[] { 4.0, 4.0 });

        Approx(scale.Min, 4.0);
        Approx(scale.Max, 4.0); // unlike LogScale there is no decade expansion
        Approx(scale.Map(4.0), 0.5);
    }

    // ── Formatting ─────────────────────────────────────────────────────────

    [TestCase]
    public void SequentialColorScaleFormatUsesFourSignificantDigits()
    {
        var scale = new SequentialColorScale(0, 10);

        AssertThat(scale.Format(1234.5678)).IsEqual("1235");
        AssertThat(scale.Format(double.NaN)).IsEqual("NaN");
    }

    // ══ DivergingColorScale ════════════════════════════════════════════════

    // ── Domain construction / mapping ──────────────────────────────────────

    [TestCase]
    public void DivergingColorScaleDefaultDomainIsMinusOneToOne()
    {
        var scale = new DivergingColorScale();

        Approx(scale.Min, -1.0);
        Approx(scale.Max, 1.0);
        Approx(scale.MidPoint, 0.0);
        AssertThat(scale.Symmetric).IsTrue();
        Approx(scale.Map(0.0), 0.5);
    }

    [TestCase]
    public void DivergingColorScaleMapClampsToUnitRange()
    {
        var scale = new DivergingColorScale(-1, 1);

        Approx(scale.Map(-2.0), 0.0);
        Approx(scale.Map(0.5), 0.75);
        Approx(scale.Map(2.0), 1.0);
    }

    [TestCase]
    public void DivergingColorScaleMapTreatsNullAsZero()
    {
        var scale = new DivergingColorScale(-1, 1);

        Approx(scale.Map(null!), 0.5); // 0.0 sits exactly on the midpoint
        AssertThat(scale.MapColor(null!).ToHtml()).IsEqual(scale.MidColor.ToHtml());
    }

    [TestCase]
    public void DivergingColorScaleNonFiniteMapIsNaNButTheColourIsNeutral()
    {
        var scale = new DivergingColorScale(-1, 1);

        // Map reports NaN for a non-finite value, while MapColor returns the neutral midpoint colour.
        AssertThat(double.IsNaN(scale.Map(double.NaN))).IsTrue();
        AssertThat(double.IsNaN(scale.Map(double.PositiveInfinity))).IsTrue();
        AssertThat(scale.MapColor(double.NaN).ToHtml()).IsEqual(scale.MidColor.ToHtml());
        AssertThat(scale.MapColor(double.PositiveInfinity).ToHtml()).IsEqual(scale.MidColor.ToHtml());
    }

    // ── Colour interpolation ───────────────────────────────────────────────

    [TestCase]
    public void DivergingColorScaleMapColorHitsTheThreeAnchors()
    {
        var scale = new DivergingColorScale(-1, 1);

        AssertThat(scale.MapColor(-1.0).ToHtml()).IsEqual(scale.NegativeColor.ToHtml());
        AssertThat(scale.MapColor(0.0).ToHtml()).IsEqual(scale.MidColor.ToHtml());
        AssertThat(scale.MapColor(1.0).ToHtml()).IsEqual(scale.PositiveColor.ToHtml());
    }

    [TestCase]
    public void DivergingColorScaleMapColorInterpolatesWithinEachHalf()
    {
        var scale = new DivergingColorScale(-1, 1);

        var below = scale.MapColor(-0.5); // halfway negative -> mid
        AssertThat(below.ToHtml()).IsEqual(scale.NegativeColor.Lerp(scale.MidColor, 0.5f).ToHtml());

        var above = scale.MapColor(0.5); // halfway mid -> positive
        AssertThat(above.ToHtml()).IsEqual(scale.MidColor.Lerp(scale.PositiveColor, 0.5f).ToHtml());
    }

    /// <summary>
    /// [KnownConvention] The midpoint is CLAMPED into the domain rather than rejected: a one-sided
    /// domain ("positive data only") fades from MidColor at Min towards PositiveColor at Max, so the
    /// value at Max is PositiveColor and not "everything is positive". This documents a deliberate
    /// convention (the alternative - treating the out-of-range midpoint as an error - has no useful
    /// rendering answer), it is not a claim about a mathematically clean diverging scale.
    /// </summary>
    [TestCase]
    public void DivergingColorScaleMidpointOutsideTheDomainIsClamped()
    {
        var scale = new DivergingColorScale(5, 9) { MidPoint = 0 };

        AssertThat(scale.MapColor(5.0).ToHtml()).IsEqual(scale.MidColor.ToHtml());
        AssertThat(scale.MapColor(9.0).ToHtml()).IsEqual(scale.PositiveColor.ToHtml());

        var middle = scale.MapColor(7.0);
        AssertThat(middle.ToHtml()).IsEqual(scale.MidColor.Lerp(scale.PositiveColor, 0.5f).ToHtml());
    }

    [TestCase]
    public void DivergingColorScaleDegenerateDomainMapsToTheNeutralColour()
    {
        var scale = new DivergingColorScale(5, 5);

        // A collapsed domain (every value equal to the midpoint) has no side to pick: Map reports the
        // neutral 0.5 and MapColor returns MidColor. It used to fall into the "above the midpoint"
        // branch and paint the whole chart with PositiveColor (full red for all-zero data).
        Approx(scale.Map(5.0), 0.5);
        AssertThat(scale.MapColor(5.0).ToHtml()).IsEqual(scale.MidColor.ToHtml());
    }

    // ── Fitting ────────────────────────────────────────────────────────────

    [TestCase]
    public void DivergingColorScaleSymmetricFitMirrorsAroundTheMidpoint()
    {
        var scale = new DivergingColorScale();
        scale.Fit(new object[] { 2.0, 8.0 });

        Approx(scale.Min, -8.0);
        Approx(scale.Max, 8.0);
        Approx(scale.Map(0.0), 0.5);
        AssertThat(scale.MapColor(-8.0).ToHtml()).IsEqual(scale.NegativeColor.ToHtml());
        AssertThat(scale.MapColor(8.0).ToHtml()).IsEqual(scale.PositiveColor.ToHtml());
    }

    [TestCase]
    public void DivergingColorScaleAsymmetricFitKeepsTheDataRange()
    {
        var scale = new DivergingColorScale { Symmetric = false };
        scale.Fit(new object[] { 2.0, 8.0 });

        Approx(scale.Min, 2.0);
        Approx(scale.Max, 8.0);
        Approx(scale.Map(2.0), 0.0);
        Approx(scale.Map(8.0), 1.0);
    }

    [TestCase]
    public void DivergingColorScaleSingleValueFitBecomesSymmetric()
    {
        var scale = new DivergingColorScale();
        scale.Fit(new object[] { 5.0 });

        Approx(scale.Min, -5.0);
        Approx(scale.Max, 5.0);
        Approx(scale.Map(5.0), 1.0);
    }

    [TestCase]
    public void DivergingColorScaleFitWithEmptyDataKeepsTheDomain()
    {
        var scale = new DivergingColorScale(-2, 3);

        scale.Fit(Array.Empty<object>());

        Approx(scale.Min, -2.0);
        Approx(scale.Max, 3.0);
    }

    // ── Formatting ─────────────────────────────────────────────────────────

    [TestCase]
    public void DivergingColorScaleFormatUsesFourSignificantDigits()
    {
        var scale = new DivergingColorScale(-1, 1);

        AssertThat(scale.Format(1234.5678)).IsEqual("1235");
        AssertThat(scale.Format(-0.5)).IsEqual("-0.5");
    }
}
