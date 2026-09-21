namespace GodotNodeExtension.Tests.GodotChart.Marks;

using System;
using System.Collections.Generic;
using GdUnit4;
using GodotNodeExtension.Component.GodotChart;
using Support;
using static GdUnit4.Assertions;

/// <summary>
/// Contract of the two shared scale-contribution helpers used by the marks:
/// <see cref="StackScaleHelper.ContributeStackedYScale"/> (the value axis must cover the largest
/// positive stack and the deepest negative one - or normalise to [0, 1]) and
/// <see cref="ScaleContributionHelper.ContributeMinMaxScale"/> (min/max fields padded into a
/// <see cref="LinearScale"/>).
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class MarkStackingHelperTest
{
    /// <summary>Approximate comparison for computed floating point values.</summary>
    private static bool Approx(double actual, double expected, double tolerance = 1e-6)
        => Math.Abs(actual - expected) <= tolerance;

    /// <summary>Assert that <paramref name="actual"/> is within <paramref name="tolerance"/> of the expected value.</summary>
    private static void AssertApprox(double actual, double expected, double tolerance = 1e-6)
        => AssertThat(Approx(actual, expected, tolerance)).IsTrue();

    // ── StackScaleHelper ────────────────────────────────────────────────────

    [TestCase]
    public void StackScaleHelperIgnoresNoneAndEmptyData()
    {
        var scales = new ScaleSet();
        var original = new LinearScale(0, 100);
        scales.Set(Channel.Y, original);
        var encodes = TestContexts.XyEncodes("cat", "value");
        var data = new List<DataRow> { TestContexts.Row(("cat", "A"), ("value", 50.0)) };

        StackScaleHelper.ContributeStackedYScale(StackMode.None, scales, encodes, data);
        AssertThat(ReferenceEquals(scales.Get(Channel.Y), original)).IsTrue();

        StackScaleHelper.ContributeStackedYScale(StackMode.Stack, scales, encodes, []);
        AssertThat(ReferenceEquals(scales.Get(Channel.Y), original)).IsTrue();
        AssertApprox(((LinearScale)scales.Get(Channel.Y)).Max, 100);
    }

    [TestCase]
    public void StackScaleHelperCoversTheLargestStackWithHeadroom()
    {
        var scales = new ScaleSet();
        var encodes = TestContexts.XyEncodes("cat", "value");
        var data = new List<DataRow>
        {
            TestContexts.Row(("cat", "A"), ("value", 3.0)),
            TestContexts.Row(("cat", "A"), ("value", 4.0)),   // A total = 7
            TestContexts.Row(("cat", "B"), ("value", 1.0)),
            TestContexts.Row(("cat", "B"), ("value", 2.0)),   // B total = 3
        };

        StackScaleHelper.ContributeStackedYScale(StackMode.Stack, scales, encodes, data);

        var scale = scales.Get(Channel.Y) as LinearScale;
        AssertThat(scale is not null).IsTrue();
        AssertApprox(scale!.Min, 0);
        AssertApprox(scale.Max, 7 * 1.05);
    }

    [TestCase]
    public void StackScaleHelperNormalisesToTheUnitRange()
    {
        var encodes = TestContexts.XyEncodes("cat", "value");

        // Normalize always maps to [0, 1], whatever the values are.
        var normalized = new ScaleSet();
        StackScaleHelper.ContributeStackedYScale(StackMode.Normalize, normalized, encodes,
            [TestContexts.Row(("cat", "A"), ("value", 4.0))]);
        var normalizedScale = normalized.Get(Channel.Y) as LinearScale;
        AssertThat(normalizedScale is not null).IsTrue();
        AssertApprox(normalizedScale!.Min, 0);
        AssertApprox(normalizedScale.Max, 1);
    }

    [TestCase]
    public void StackScaleHelperKeepsNegativeStacksBelowTheZeroLine()
    {
        var encodes = TestContexts.XyEncodes("cat", "value");

        // Positive and negative contributions are accumulated separately: a negative value grows the
        // stack downwards instead of shrinking it. All-negative data used to fall back to [0, 1],
        // which drew every bar below the axis and clipped it all away.
        var scales = new ScaleSet();
        StackScaleHelper.ContributeStackedYScale(StackMode.Stack, scales, encodes,
        [
            TestContexts.Row(("cat", "A"), ("value", -5.0)),
            TestContexts.Row(("cat", "A"), ("value", -3.0)),   // A total = -8
            TestContexts.Row(("cat", "B"), ("value", -1.0)),   // B total = -1
        ]);

        var scale = scales.Get(Channel.Y) as LinearScale;
        AssertThat(scale is not null).IsTrue();
        AssertApprox(scale!.Min, -8 * 1.05);               // deepest negative stack, with headroom
        AssertApprox(scale.Max, 1);                        // no positive stack: the unit fallback
    }

    [TestCase]
    public void StackScaleHelperCoversBothSidesOfAMixedSignStack()
    {
        var encodes = TestContexts.XyEncodes("cat", "value");

        var scales = new ScaleSet();
        StackScaleHelper.ContributeStackedYScale(StackMode.Stack, scales, encodes,
        [
            TestContexts.Row(("cat", "A"), ("value", 3.0)),
            TestContexts.Row(("cat", "A"), ("value", -4.0)),   // A: +3 up, -4 down
            TestContexts.Row(("cat", "B"), ("value", 2.0)),
        ]);

        var scale = scales.Get(Channel.Y) as LinearScale;
        AssertThat(scale is not null).IsTrue();
        // The axis has to hold the tallest upward stack and the deepest downward one at once.
        AssertApprox(scale!.Min, -4 * 1.05);
        AssertApprox(scale.Max, 3 * 1.05);
    }

    // ── ScaleContributionHelper ─────────────────────────────────────────────

    [TestCase]
    public void ScaleContributionHelperPadsTheFittedRange()
    {
        var data = new List<DataRow>
        {
            TestContexts.Row(("low", 10.0), ("high", 20.0)),
            TestContexts.Row(("low", 12.0), ("high", 18.0)),
        };

        var scales = new ScaleSet();
        ScaleContributionHelper.ContributeMinMaxScale(scales, data, "low", "high");

        var scale = scales.Get(Channel.Y) as LinearScale;
        AssertThat(scale is not null).IsTrue();
        AssertApprox(scale!.Min, 10 - 0.5);   // 5% of the 10 wide range
        AssertApprox(scale.Max, 20 + 0.5);

        // empty data leaves the scale set alone
        var empty = new ScaleSet();
        ScaleContributionHelper.ContributeMinMaxScale(empty, [], "low", "high");
        AssertThat(empty.Has(Channel.Y)).IsFalse();
    }

    [TestCase]
    public void ScaleContributionHelperOnlyWidensAnExistingLinearScale()
    {
        var data = new List<DataRow> { TestContexts.Row(("low", 10.0), ("high", 20.0)) };

        // already covering the data → the instance is kept, not replaced
        var covering = new ScaleSet();
        var existing = new LinearScale(0, 100);
        covering.Set(Channel.Y, existing);
        ScaleContributionHelper.ContributeMinMaxScale(covering, data, "low", "high");
        AssertThat(ReferenceEquals(covering.Get(Channel.Y), existing)).IsTrue();

        // only partially covering → replaced by the widened range
        var partial = new ScaleSet();
        partial.Set(Channel.Y, new LinearScale(15, 18));
        ScaleContributionHelper.ContributeMinMaxScale(partial, data, "low", "high");
        AssertApprox(((LinearScale)partial.Get(Channel.Y)).Min, 9.5);
        AssertApprox(((LinearScale)partial.Get(Channel.Y)).Max, 20.5);
    }

    [TestCase]
    public void ScaleContributionHelperReplacesNonLinearScalesAndSkipsMissingFields()
    {
        var data = new List<DataRow> { TestContexts.Row(("low", 10.0), ("high", 20.0)) };

        // a non-LinearScale on the channel is silently replaced
        var ordinal = new ScaleSet();
        ordinal.Set(Channel.Y, new OrdinalScale());
        ScaleContributionHelper.ContributeMinMaxScale(ordinal, data, "low", "high");
        AssertThat(ordinal.Get(Channel.Y) is LinearScale).IsTrue();

        // no row carries either field → nothing is fitted
        var missing = new ScaleSet();
        ScaleContributionHelper.ContributeMinMaxScale(missing,
            [TestContexts.Row(("other", 1.0))], "low", "high");
        AssertThat(missing.Has(Channel.Y)).IsFalse();
    }

    [TestCase]
    public void ScaleContributionHelperKeepsEqualBoundsForEqualFields()
    {
        var scales = new ScaleSet();

        ScaleContributionHelper.ContributeMinMaxScale(scales,
        [
            TestContexts.Row(("low", 5.0), ("high", 5.0)),
            TestContexts.Row(("low", 5.0), ("high", 5.0)),
        ], "low", "high");

        var scale = scales.Get(Channel.Y) as LinearScale;
        AssertThat(scale is not null).IsTrue();
        // A zero range yields a degenerate [5, 5] domain, so every value maps onto the same position.
        AssertApprox(scale!.Min, 5);
        AssertApprox(scale.Max, 5);
    }

    /// <summary>
    /// <see cref="ScaleContributionHelper.ContributeRange"/>: the helper for marks whose geometry reaches past
    /// their samples (a violin's density). The scale widens to cover the range and never shrinks, the padding is
    /// applied on both ends, an inverted range changes nothing, and an unbound channel gets a scale of its own.
    /// </summary>
    [TestCase]
    public void ContributeRangeWidensTheScaleAndNeverShrinksIt()
    {
        var scales = new ScaleSet();
        scales.Set(Channel.Y, new LinearScale(0, 10));

        ScaleContributionHelper.ContributeRange(scales, Channel.Y, 12, 20, padding: 0.5);
        AssertApprox(((LinearScale)scales.Get(Channel.Y)).Min, 0);    // never shrinks what the axis shows
        AssertApprox(((LinearScale)scales.Get(Channel.Y)).Max, 24);   // 20 + 50% of (20 - 12)

        ScaleContributionHelper.ContributeRange(scales, Channel.Y, 2, 8);    // inside: no change
        AssertApprox(((LinearScale)scales.Get(Channel.Y)).Max, 24);
        ScaleContributionHelper.ContributeRange(scales, Channel.Y, 30, 1);   // inverted: ignored
        AssertApprox(((LinearScale)scales.Get(Channel.Y)).Max, 24);

        var fresh = new ScaleSet();
        ScaleContributionHelper.ContributeRange(fresh, Channel.Y, 5, 15);
        AssertApprox(((LinearScale)fresh.Get(Channel.Y)).Min, 5);
        AssertApprox(((LinearScale)fresh.Get(Channel.Y)).Max, 15);
    }
}
