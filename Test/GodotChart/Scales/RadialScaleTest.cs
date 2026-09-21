namespace GodotNodeExtension.Tests.GodotChart.Scales;

using System;
using GdUnit4;
using GodotNodeExtension.Component.GodotChart;
using static GdUnit4.Assertions;

/// <summary>
/// Behaviour specification for <see cref="RadialScale"/>: mapping categories to evenly spaced
/// angular positions [0, 1) (multiply by 2π for radians), including the single-category, unknown
/// and empty-domain conventions.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class RadialScaleTest
{
    /// <summary>Assert two doubles are within <paramref name="eps"/> (IsEqual is exact for doubles).</summary>
    private static void Approx(double actual, double expected, double eps = 1e-9)
        => AssertThat(Math.Abs(actual - expected) <= eps).IsTrue();

    // ── Mapping ────────────────────────────────────────────────────────────

    [TestCase]
    public void RadialScaleStepsByInverseCountStartingAtZero()
    {
        var scale = new RadialScale();
        scale.Fit(new object[] { "A", "B", "C" });

        // Unlike OrdinalScale the first category sits at 0, not at the centre of a cell.
        Approx(scale.Map("A"), 0.0);
        Approx(scale.Map("B"), 1.0 / 3.0);
        Approx(scale.Map("C"), 2.0 / 3.0);
    }

    [TestCase]
    public void RadialScaleSingleCategoryIsZero()
    {
        var scale = new RadialScale();
        scale.Fit(new object[] { "only" });

        Approx(scale.Map("only"), 0.0);
    }

    [TestCase]
    public void RadialScaleUnknownAndEmptyDomainsMapToZero()
    {
        var unfitted = new RadialScale();
        Approx(unfitted.Map("A"), 0.0);

        var fitted = new RadialScale();
        fitted.Fit(new object[] { "A", "B", "C" });
        Approx(fitted.Map("Z"), 0.0);
    }

    [TestCase]
    public void RadialScaleMapTreatsNullAsUnknown()
    {
        var scale = new RadialScale();
        scale.Fit(new object[] { "A" });

        AssertThat(scale.Map(null!)).IsEqual(0.0);
        AssertThat(scale.Format(null!)).IsEqual("");
    }
}
