namespace GodotNodeExtension.Tests.GodotChart.Support;

using System;

/// <summary>
/// Assertion helpers the GodotChart suites share. Each of these used to be re-declared in every file that
/// needed one (twelve copies of an approximation check, six of the exception capture), which is what this
/// type replaces for new code.
/// </summary>
public static class Asserts
{
    /// <summary>Whether two floats are within <paramref name="eps"/> of each other.</summary>
    public static bool Approx(float a, float b, float eps = 1e-3f) => Math.Abs(a - b) <= eps;

    /// <summary>
    /// Run <paramref name="action"/> and hand back the exception it threw, or null when it did not throw.
    /// A <i>different</i> exception type propagates on purpose: that is a failure, not an expected throw.
    /// </summary>
    public static TException? Throws<TException>(Action action) where TException : Exception
    {
        try
        {
            action();
            return null;
        }
        catch (TException expected)
        {
            return expected;
        }
    }
}
