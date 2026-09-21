namespace GodotNodeExtension.Tests.GodotChart.Scales;

using System;
using GdUnit4;
using GodotNodeExtension.Component.GodotChart;
using static GdUnit4.Assertions;

/// <summary>
/// Behaviour specification for <see cref="OrdinalScale"/>: category-to-position mapping (cell
/// centres), domain fitting (order-preserving de-duplication), case-sensitive lookup and value
/// formatting.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class OrdinalScaleTest
{
    /// <summary>Assert two doubles are within <paramref name="eps"/> (IsEqual is exact for doubles).</summary>
    private static void Approx(double actual, double expected, double eps = 1e-9)
        => AssertThat(Math.Abs(actual - expected) <= eps).IsTrue();

    // ── Mapping ────────────────────────────────────────────────────────────

    [TestCase]
    public void OrdinalScaleUnknownValueMapsToZero()
    {
        var scale = new OrdinalScale();
        scale.Fit(new object[] { "A", "B", "C" });

        // 0 is a sentinel: it is not the centre of the first cell.
        Approx(scale.Map("Z"), 0.0);
        Approx(scale.Map("A"), 1.0 / 6.0);
    }

    [TestCase]
    public void OrdinalScaleSingleCategorySitsAtTheCentre()
    {
        var scale = new OrdinalScale();
        scale.Fit(new object[] { "only" });

        Approx(scale.Map("only"), 0.5);
    }

    [TestCase]
    public void OrdinalScaleIsCaseSensitive()
    {
        var scale = new OrdinalScale();
        scale.Fit(new object[] { "A", "B" });

        AssertThat(scale.IndexOf("a")).IsEqual(-1);
        Approx(scale.Map("a"), 0.0);
        Approx(scale.Map("A"), 0.25);
    }

    [TestCase]
    public void OrdinalScaleEmptyDomainMapsToZero()
    {
        var scale = new OrdinalScale();

        AssertThat(scale.Domain.Count).IsEqual(0);
        Approx(scale.Map("A"), 0.0);
    }

    [TestCase]
    public void OrdinalScaleMapTreatsNullAsUnknown()
    {
        var scale = new OrdinalScale();
        scale.Fit(new object[] { "A" });

        AssertThat(scale.Map(null!)).IsEqual(0.0);
        AssertThat(scale.Format(null!)).IsEqual("");
    }

    [TestCase]
    public void OrdinalScaleFitSkipsNullEntries()
    {
        var scale = new OrdinalScale();
        scale.Fit(new object[] { "A", null!, "B" });

        AssertThat(scale.Domain.Count).IsEqual(2);
        AssertThat(scale.IndexOf("B")).IsEqual(1);
    }

    // ── Fitting / lookup ───────────────────────────────────────────────────

    [TestCase]
    public void OrdinalScaleFitDeduplicatesAndKeepsOrder()
    {
        var scale = new OrdinalScale();
        scale.Fit(new object[] { "B", "A", "B", "C", "A" });

        AssertThat(scale.Domain.Count).IsEqual(3);
        AssertThat(scale.Domain[0]).IsEqual("B");
        AssertThat(scale.Domain[1]).IsEqual("A");
        AssertThat(scale.Domain[2]).IsEqual("C");
        AssertThat(scale.IndexOf("A")).IsEqual(1);
    }

    [TestCase]
    public void OrdinalScaleIndexOfReturnsMinusOneForMissingKeys()
    {
        var scale = new OrdinalScale();
        scale.Fit(new object[] { "A", "B" });

        AssertThat(scale.IndexOf("B")).IsEqual(1);
        AssertThat(scale.IndexOf("missing")).IsEqual(-1);
    }

    // ── Formatting ─────────────────────────────────────────────────────────

    [TestCase]
    public void OrdinalScaleFormatReturnsTheValueText()
    {
        var scale = new OrdinalScale();
        scale.Fit(new object[] { "A" });

        AssertThat(scale.Format("A")).IsEqual("A");
        AssertThat(scale.Format(42)).IsEqual("42");
    }
}
