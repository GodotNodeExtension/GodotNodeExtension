namespace GodotNodeExtension.Tests.GodotChart.Marks;

using System;
using GdUnit4;
using GodotNodeExtension.Component.GodotChart;
using static GdUnit4.Assertions;

/// <summary>
/// Contract of <see cref="ScaleSet"/>: the per-<see cref="Channel"/> container for resolved scales.
/// Covers set/replace, the throwing <c>Get</c> versus the null-returning <c>TryGet</c>, <c>Has</c>
/// and the <c>Remove</c> result.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class ScaleSetTest
{
    /// <summary>Approximate comparison for computed floating point values.</summary>
    private static bool Approx(double actual, double expected, double tolerance = 1e-6)
        => Math.Abs(actual - expected) <= tolerance;

    /// <summary>Assert that <paramref name="actual"/> is within <paramref name="tolerance"/> of the expected value.</summary>
    private static void AssertApprox(double actual, double expected, double tolerance = 1e-6)
        => AssertThat(Approx(actual, expected, tolerance)).IsTrue();

    /// <summary>Run <paramref name="action"/> and return the exception it threw (null when it did not throw).</summary>
    private static Exception? CaptureException(Action action)
    {
        try { action(); return null; }
        catch (Exception ex) { return ex; }
    }

    [TestCase]
    public void ScaleSetSetReplacesTheSameChannel()
    {
        var scales = new ScaleSet();
        var first = new LinearScale(0, 10);
        var second = new LinearScale(5, 20);

        scales.Set(Channel.Y, first);
        scales.Set(Channel.Y, second);

        AssertThat(ReferenceEquals(scales.Get(Channel.Y), second)).IsTrue();
        AssertApprox(((LinearScale)scales.Get(Channel.Y)).Min, 5);
    }

    [TestCase]
    public void ScaleSetGetThrowsForAnUnsetChannel()
    {
        var scales = new ScaleSet();

        var ex = CaptureException(() => scales.Get(Channel.Y2));

        AssertThat(ex is InvalidOperationException).IsTrue();
        AssertThat(ex!.Message.Contains("Y2")).IsTrue();
    }

    [TestCase]
    public void ScaleSetTryGetAndHasReportConfiguredChannels()
    {
        var scales = new ScaleSet();

        AssertThat(scales.Has(Channel.X)).IsFalse();
        AssertThat(scales.TryGet(Channel.X) is null).IsTrue();

        var scale = new OrdinalScale();
        scales.Set(Channel.X, scale);

        AssertThat(scales.Has(Channel.X)).IsTrue();
        AssertThat(ReferenceEquals(scales.TryGet(Channel.X), scale)).IsTrue();
        AssertThat(scales.Has(Channel.Y)).IsFalse();
    }

    [TestCase]
    public void ScaleSetRemoveReportsWhetherTheChannelWasPresent()
    {
        var scales = new ScaleSet();
        scales.Set(Channel.Y, new LinearScale(0, 1));

        AssertThat(scales.Remove(Channel.Y)).IsTrue();
        AssertThat(scales.Remove(Channel.Y)).IsFalse();
        AssertThat(scales.Has(Channel.Y)).IsFalse();
        AssertThat(scales.Remove(Channel.Y2)).IsFalse();
    }
}
