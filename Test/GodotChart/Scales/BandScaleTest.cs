namespace GodotNodeExtension.Tests.GodotChart.Scales;

using System;
using GdUnit4;
using GodotNodeExtension.Component.GodotChart;
using static GdUnit4.Assertions;

/// <summary>
/// Behaviour specification for <see cref="BandScale"/>: band and sub-band layout for grouped
/// charts - band widths from the outer padding, centred band positions, sub-band insets, and the
/// empty/unknown-domain conventions.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class BandScaleTest
{
    /// <summary>Assert two doubles are within <paramref name="eps"/> (IsEqual is exact for doubles).</summary>
    private static void Approx(double actual, double expected, double eps = 1e-9)
        => AssertThat(Math.Abs(actual - expected) <= eps).IsTrue();

    // ── Band widths ────────────────────────────────────────────────────────

    [TestCase]
    public void BandScaleBandWidthIsOneMinusPaddingOverCount()
    {
        var scale = new BandScale();
        scale.Fit(new object[] { "A", "B", "C", "D", "E" });

        AssertThat(scale.Padding).IsEqual(0.2f);
        Approx(scale.BandWidth, (1.0 - 0.2f) / 5, 1e-6);
    }

    [TestCase]
    public void BandScaleBandWidthIsZeroForAnEmptyDomain()
    {
        var scale = new BandScale();

        Approx(scale.BandWidth, 0.0);
        Approx(scale.Map("A"), 0.0);
    }

    // ── Band positions ─────────────────────────────────────────────────────

    [TestCase]
    public void BandScaleMapCentresBandsWhenPaddingIsZero()
    {
        var scale = new BandScale { Padding = 0f };
        scale.Fit(new object[] { "A", "B", "C", "D", "E" });

        Approx(scale.Map("A"), 0.1);
        Approx(scale.Map("C"), 0.5);
        Approx(scale.Map("E"), 0.9);
    }

    [TestCase]
    public void BandScalePaddingInsetsAndNarrowsEveryBand()
    {
        var padded = new BandScale(); // Padding = 0.2 (default)
        var flush = new BandScale { Padding = 0f };
        var categories = new object[] { "A", "B", "C", "D", "E" };
        padded.Fit(categories);
        flush.Fit(categories);

        // The first centre sits inside by padding/2 plus the half band width.
        Approx(padded.Map("A"), 0.1 + 0.5 * ((1.0 - 0.2f) / 5), 1e-6);
        // Narrowing the bands moves a centre by padding * (0.5 - (i + 0.5) / count): the outer
        // bands move inwards, the middle one does not move at all.
        Approx(padded.Map("A") - flush.Map("A"), 0.2f * (0.5 - 0.5 / 5.0), 1e-6);
        Approx(padded.Map("E") - flush.Map("E"), 0.2f * (0.5 - 4.5 / 5.0), 1e-6);
        Approx(padded.Map("C") - flush.Map("C"), 0.0, 1e-6);
        Approx(padded.Map("C"), 0.5, 1e-6); // the middle band stays centred
    }

    [TestCase]
    public void BandScaleUnknownValuesMapToZero()
    {
        var scale = new BandScale();
        scale.Fit(new object[] { "A", "B", "C", "D", "E" });

        Approx(scale.Map("Z"), 0.0);
        Approx(scale.MapSubBand("Z", 0), 0.0);
    }

    // ── Sub-bands ──────────────────────────────────────────────────────────

    [TestCase]
    public void BandScaleSubBandWidthIsNotDividedForASingleSubBand()
    {
        var scale = new BandScale { Padding = 0f, InnerPadding = 0.25f, SubBandCount = 1 };
        scale.Fit(new object[] { "only" });

        Approx(scale.BandWidth, 1.0);
        Approx(scale.SubBandWidth, 0.75); // BandWidth * (1 - InnerPadding), no division

        scale.SubBandCount = 3;
        Approx(scale.SubBandWidth, 0.25); // 0.75 / 3
    }

    [TestCase]
    public void BandScaleSubBandsSitInsideTheirBand()
    {
        // BandWidth = 1, inner start = InnerPadding * BandWidth / 2 = 0.125,
        // SubBandWidth = (1 - InnerPadding) / SubBandCount = 0.375.
        var scale = new BandScale { Padding = 0f, InnerPadding = 0.25f, SubBandCount = 2 };
        scale.Fit(new object[] { "only" });

        Approx(scale.MapSubBand("only", 0), 0.3125);
        Approx(scale.MapSubBand("only", 1), 0.6875);
    }

    [TestCase]
    public void BandScaleMapTreatsNullAsUnknown()
    {
        var scale = new BandScale();
        scale.Fit(new object[] { "A" });

        AssertThat(scale.Map(null!)).IsEqual(0.0);
        AssertThat(scale.MapSubBand(null!, 0)).IsEqual(0.0);
        AssertThat(scale.Format(null!)).IsEqual("");
    }

    [TestCase]
    public void BandScaleFitSkipsNullEntries()
    {
        var scale = new BandScale { Padding = 0f };
        scale.Fit(new object[] { "A", null!, "B" });

        AssertThat(scale.Domain.Count).IsEqual(2);
    }
}
