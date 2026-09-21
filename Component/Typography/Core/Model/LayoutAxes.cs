using Godot;

namespace GodotNodeExtension.Component.Typography.Core.Model;

/// <summary>
/// How a laid-out box is oriented: which way a line runs (the inline axis) and which way lines advance (the block
/// axis).
/// <para>
/// Every stage expresses geometry as inline/block scalars and reaches content space through this type, so no stage
/// has to ask "am I vertical?". The two writing modes differ only in which Godot axis carries which quantity and in
/// which direction each grows.
/// </para>
/// <para>
/// Content space stays page geometry - origin at the content box' top-left corner, X to the right, Y down - so
/// everything already expressed in it (wrap regions, a canvas' padding and scroll offset, hit testing) keeps
/// working unchanged. What the mode decides is where block coordinate zero sits: on the block-start edge, which for
/// <see cref="WritingMode.VerticalRl"/> is the content box' <em>right</em> edge, because columns advance leftward.
/// </para>
/// </summary>
/// <param name="Mode">The writing mode this box is laid out in.</param>
/// <param name="BlockExtentLimit">
/// Extent of the content box along the block axis in content space (<c>TypographySettings.MaxWidth</c> today): the
/// anchor a vertical block coordinate is measured from. The horizontal mode ignores it, its block coordinate being
/// a plain Y.
/// </param>
public readonly record struct LayoutAxes(WritingMode Mode, float BlockExtentLimit)
{
    /// <summary>Whether the inline axis runs down the page.</summary>
    public bool IsVertical => Mode != WritingMode.HorizontalTb;

    /// <summary>Unit vector of the inline axis in content space: +X in horizontal writing, +Y in vertical writing.</summary>
    public Vector2 InlineUnit => IsVertical ? new Vector2(0, 1) : new Vector2(1, 0);

    /// <summary>
    /// Unit vector of the block axis in content space: +Y in horizontal writing, -X for
    /// <see cref="WritingMode.VerticalRl"/> (columns run right to left) and +X for
    /// <see cref="WritingMode.VerticalLr"/>.
    /// </summary>
    public Vector2 BlockUnit => Mode switch
    {
        WritingMode.VerticalRl => new Vector2(-1, 0),
        WritingMode.VerticalLr => new Vector2(1, 0),
        _ => new Vector2(0, 1),
    };

    /// <summary>
    /// Content-space X of block coordinate zero: the block-start edge of the content box, i.e. its right edge for
    /// <see cref="WritingMode.VerticalRl"/> and its left edge otherwise.
    /// </summary>
    private float BlockOrigin => Mode == WritingMode.VerticalRl ? BlockExtentLimit : 0f;

    /// <summary>Content-space point of an (inline, block) pair.</summary>
    /// <param name="inlinePos">Position along the inline axis, from the content origin.</param>
    /// <param name="blockPos">Position along the block axis, from the block-start edge.</param>
    /// <returns>The point in content space.</returns>
    public Vector2 Point(float inlinePos, float blockPos) => IsVertical
        ? BlockUnit * blockPos + new Vector2(BlockOrigin, inlinePos)
        : InlineUnit * inlinePos + BlockUnit * blockPos;

    /// <summary>Inline coordinate of a content-space vector (a distance, not a position).</summary>
    /// <param name="v">A vector in content space.</param>
    /// <returns>Its inline component.</returns>
    public float Inline(Vector2 v) => IsVertical ? v.Y : v.X;

    /// <summary>Block coordinate of a content-space vector, measured from the block-start edge.</summary>
    /// <param name="v">A vector in content space.</param>
    /// <returns>Its block component.</returns>
    public float Block(Vector2 v) => Mode switch
    {
        WritingMode.VerticalRl => BlockExtentLimit - v.X,
        WritingMode.VerticalLr => v.X,
        _ => v.Y,
    };

    /// <summary>
    /// Extent of a displacement along the block axis: the block-axis dual of <see cref="Inline(Vector2)"/>.
    /// <para>
    /// <see cref="Block(Vector2)"/> answers "where on the axis", which is not the same question as "how far
    /// along it": a displacement has no position, so the anchor disappears and only the direction of
    /// <see cref="BlockUnit"/> remains. Use this for offsets (a box moved by a padding) and
    /// <see cref="Block(Vector2)"/> for positions.
    /// </para>
    /// </summary>
    /// <param name="v">A displacement in content space.</param>
    /// <returns>Its signed extent along the block axis.</returns>
    public float BlockDelta(Vector2 v) => Mode switch
    {
        WritingMode.VerticalRl => -v.X,
        WritingMode.VerticalLr => v.X,
        _ => v.Y,
    };

    /// <summary>Extent of a box along the block axis (the block-axis dual of <see cref="Inline(Vector2)"/>).</summary>
    /// <param name="size">A size in content space.</param>
    /// <returns>Its block extent.</returns>
    public float BlockExtent(Vector2 size) => IsVertical ? size.X : size.Y;

    /// <summary>Size of a box from its extent along each axis.</summary>
    /// <param name="inlineExtent">Extent along the inline axis (a line's natural length).</param>
    /// <param name="blockExtent">Extent along the block axis (a line's thickness).</param>
    /// <returns>The size in content space.</returns>
    public Vector2 Size(float inlineExtent, float blockExtent) =>
        IsVertical ? new Vector2(blockExtent, inlineExtent) : new Vector2(inlineExtent, blockExtent);
}
