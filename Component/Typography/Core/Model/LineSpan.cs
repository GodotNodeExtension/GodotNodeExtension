namespace GodotNodeExtension.Component.Typography.Core.Model;

/// <summary>
/// One interval of a line that text may occupy, relative to the content origin.
/// <para>
/// A line usually offers a single interval, but text flowing around an exclusion that only covers part of the
/// line leaves two — a floated image on the left and another on the right leaves one in the middle, and a
/// shape narrower in the middle than at its edges leaves one on each side. Modelling the line as a list of
/// intervals is what lets the line breaker place text in the second one instead of leaving it empty.
/// </para>
/// <para>
/// Intervals are an exhaustive, ordered partition of what is usable at that Y: they never overlap and they are
/// sorted left to right.
/// </para>
/// </summary>
/// <param name="Left">Left edge, inclusive.</param>
/// <param name="Right">Right edge, exclusive.</param>
public readonly record struct LineSpan(float Left, float Right)
{
    /// <summary>Width of the interval in pixels.</summary>
    public float Width => Right - Left;

    /// <inheritdoc />
    public override string ToString() => $"[{Left:F3},{Right:F3})";
}
