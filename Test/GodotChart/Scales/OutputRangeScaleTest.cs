namespace GodotNodeExtension.Tests.GodotChart;

using System;
using System.Collections.Generic;
using GdUnit4;
using GodotNodeExtension.Component.GodotChart;
using static GdUnit4.Assertions;

/// <summary>
/// <see cref="OutputRangeScale"/>: the decorator that remaps another scale's normalized output onto a
/// sub-range. It is what keeps a faintest element visible (<c>ChartView.OpacityRange</c>) or a bar from
/// collapsing to zero width, and it used to be a private class inside <c>ChartView</c> with no test of its own.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class OutputRangeScaleTest
{
    private static void Approx(double actual, double expected, double tolerance = 1e-9)
        => AssertThat(Math.Abs(actual - expected) <= tolerance).IsTrue();

    private static LinearScale Fitted(params double[] values)
    {
        var scale = new LinearScale();
        scale.Fit(Array.ConvertAll(values, v => (object)v));
        return scale;
    }

    /// <summary>The inner scale decides the position; this one only decides where that position lands.</summary>
    [TestCase]
    public void TheOutputRangeRemapsTheInnerScalesNormalizedValue()
    {
        var inner = Fitted(0, 100);
        var range = new OutputRangeScale(inner, 0.2, 0.9);

        Approx(range.Map(0), 0.2);      // the bottom of the domain
        Approx(range.Map(100), 0.9);    // ... and the top
        Approx(range.Map(50), 0.55);    // halfway between them
    }

    /// <summary>A reversed output range is normalised at construction instead of producing an inverted scale.</summary>
    [TestCase]
    public void AReversedOutputRangeIsNormalised()
    {
        var range = new OutputRangeScale(Fitted(0, 10), 0.9, 0.2);

        Approx(range.OutputMin, 0.2);
        Approx(range.OutputMax, 0.9);
        Approx(range.Map(0), 0.2);
        Approx(range.Map(10), 0.9);
    }

    /// <summary>
    /// Out-of-domain values are clamped into the output range: a value below the domain must not push an
    /// opacity below the minimum the host asked for, and one above it must not exceed the maximum.
    /// </summary>
    [TestCase]
    public void ValuesOutsideTheDomainAreClamped()
    {
        var range = new OutputRangeScale(Fitted(0, 10), 0.3, 0.8);

        Approx(range.Map(-100), 0.3);
        Approx(range.Map(1000), 0.8);
    }

    /// <summary>
    /// A non-finite value - the inner scale's answer for "this has no position" - stays NaN instead of being
    /// reported as the bottom of the range (which a chart would draw as real data). Whatever the inner scale
    /// does with a <i>missing</i> value is its own convention and is passed through unchanged (a linear scale
    /// maps null to 0); this decorator only adds the remap.
    /// </summary>
    [TestCase]
    public void ANonFiniteValueStaysNaN()
    {
        var range = new OutputRangeScale(Fitted(0, 10), 0.3, 0.8);

        AssertThat(double.IsNaN(range.Map(double.NaN))).IsTrue();
        AssertThat(double.IsNaN(range.Map(double.PositiveInfinity))).IsTrue();
    }

    /// <summary>Fitting and formatting belong to the inner scale, so the same domain keeps its labels.</summary>
    [TestCase]
    public void FittingAndFormattingAreTheInnerScales()
    {
        var inner = new LinearScale();
        var range = new OutputRangeScale(inner, 0, 1);

        range.Fit(new List<object> { 5.0, 25.0 });

        Approx(inner.Min, 0);
        Approx(inner.Max, 25);
        AssertThat(range.Format(5.0)).IsEqual(inner.Format(5.0));
        AssertThat(ReferenceEquals(range.Inner, inner)).IsTrue();
    }

    /// <summary>The decorator refuses a missing inner scale instead of failing on its first use.</summary>
    [TestCase]
    public void AMissingInnerScaleIsRejected()
    {
        AssertThat(CaptureException(() => _ = new OutputRangeScale(null!, 0, 1)) is ArgumentNullException).IsTrue();
    }

    private static Exception? CaptureException(Action action)
    {
        try { action(); return null; }
        catch (Exception ex) { return ex; }
    }
}
