namespace GodotNodeExtension.Tests.GodotChart.Scales;

using System;
using GdUnit4;
using GodotNodeExtension.Component.GodotChart;
using static GdUnit4.Assertions;

/// <summary>
/// Behaviour specification for the internal scale helpers: <see cref="ScaleMath"/> (relative
/// degeneracy test shared by every scale) and <see cref="ScaleConvert"/> (value-to-double
/// conversion with fast paths and a safe fallback).
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class ScaleConversionTest
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

    // ── ScaleMath: relative degeneracy ─────────────────────────────────────

    [TestCase]
    public void ScaleMathIsDegenerateUsesARelativeTolerance()
    {
        AssertThat(ScaleMath.IsDegenerate(5, 5)).IsTrue();
        AssertThat(ScaleMath.IsDegenerate(0, 0)).IsTrue();
        AssertThat(ScaleMath.IsDegenerate(0, 1)).IsFalse();

        // The tolerance scales with the magnitude of the bounds.
        AssertThat(ScaleMath.IsDegenerate(1e12, 1e12 + 1)).IsTrue();
        AssertThat(ScaleMath.IsDegenerate(1e12, 1e12 + 2)).IsFalse();
        AssertThat(ScaleMath.IsDegenerate(-1e12 - 1, -1e12)).IsTrue();
    }

    [TestCase]
    public void ScaleMathIsDegenerateFloorsTheToleranceAtOne()
    {
        // Below magnitude 1 the tolerance stays at 1e-12, so tiny domains count as degenerate.
        AssertThat(ScaleMath.IsDegenerate(0, 1e-12)).IsTrue();
        AssertThat(ScaleMath.IsDegenerate(0, 2e-12)).IsFalse();
        AssertThat(ScaleMath.IsDegenerate(1, 1 + 1e-13)).IsTrue();
        AssertThat(ScaleMath.IsDegenerate(1, 1 + 1e-11)).IsFalse();
    }

    // ── ScaleConvert: value-to-double ──────────────────────────────────────

    [TestCase]
    public void ScaleConvertToDoubleUsesFastPathsAndFallbacks()
    {
        Approx(ScaleConvert.ToDouble(3.5, "T"), 3.5, 0);
        Approx(ScaleConvert.ToDouble(0.5f, "T"), 0.5, 0);
        Approx(ScaleConvert.ToDouble(7, "T"), 7.0, 0);
        Approx(ScaleConvert.ToDouble(7L, "T"), 7.0, 0);
        Approx(ScaleConvert.ToDouble((short)7, "T"), 7.0, 0); // Convert.ToDouble fallback
        Approx(ScaleConvert.ToDouble("42", "T"), 42.0, 0);
        Approx(ScaleConvert.ToDouble(true, "T"), 1.0, 0);
    }

    [TestCase]
    public void ScaleConvertToDoubleReturnsZeroForNull()
    {
        // Current convention: Convert.ToDouble(null) yields 0.0, so a missing value silently looks
        // like a real zero.
        Approx(ScaleConvert.ToDouble(null!, "T"), 0.0, 0);
    }

    [TestCase]
    public void ScaleConvertToDoubleThrowsForNonConvertibleValues()
    {
        AssertThat(Throws<InvalidCastException>(() => ScaleConvert.ToDouble(new object(), "T"))).IsTrue();
    }
}
