using System.Collections.Generic;

namespace GodotNodeExtension.Component.Typography.Core.Model;

/// <summary>
/// Represents a laid-out line of content.
/// Contains positioned elements and line-level metrics.
/// </summary>
public class LayoutLine
{
    /// <summary>
    /// Coordinate of this line's start edge along the <em>block</em> axis, relative to the content origin: Y in
    /// horizontal writing, the column's own coordinate in vertical writing (see <see cref="LayoutAxes"/>). It is
    /// compared against <see cref="LayoutElement.BaselineY"/>-style block coordinates, never against a Y.
    /// </summary>
    public float Y { get; set; }

    /// <summary>Extent of this line along the block axis: the line height in horizontal writing, the column's thickness in vertical writing.</summary>
    public float Height { get; init; }

    /// <summary>
    /// Room this line needs on the block-<em>start</em> side of its text box, beyond <see cref="Ascent"/>: an
    /// annotation band above the text (or, in vertical writing, on the block-start side of the column), or the part
    /// of a Beside annotation's column that reaches past the text box.
    /// <para>
    /// It is part of the line's own box, which is why a line that carries an annotation simply gets taller: both
    /// this and <see cref="ExtraBelow"/> are added to <see cref="Height"/>, and every baseline on the line sits this
    /// far into the box, so the annotation is inside the line rather than overlapping its neighbour. The engine
    /// reserves the room the content asks for instead of trusting the author's line spacing to be large enough -
    /// a convention that leaves the collision to the author was what made annotated lines overlap.
    /// </para>
    /// </summary>
    public float ExtraAbove { get; init; }

    /// <summary>
    /// Room this line needs on the block-<em>end</em> side of its text box, beyond <see cref="Descent"/>: an
    /// emphasis mark below the characters, an underline, or the part of a Beside annotation's column that reaches
    /// past the text box.
    /// </summary>
    public float ExtraBelow { get; init; }

    /// <summary>Maximum ascent among all elements in this line, along the block axis (what the line's box contributes before its baseline).</summary>
    public float Ascent { get; init; }

    /// <summary>Maximum descent among all elements in this line, along the block axis (what the line's box contributes after its baseline).</summary>
    public float Descent { get; init; }

    /// <summary>Elements in this line.</summary>
    public List<LayoutElement> Elements { get; } = [];

    /// <summary>Whether this line is finalized and won't change (for streaming mode).</summary>
    public bool IsFrozen { get; set; }

    /// <summary>Index of the paragraph this line belongs to.</summary>
    public int ParagraphIndex { get; init; }

    /// <summary>Whether this is the first line of its paragraph (for first-line indent).</summary>
    public bool IsFirstLineOfParagraph { get; init; }

    /// <summary>
    /// Start of the usable span for this line along the <em>inline</em> axis, relative to the content origin (X in
    /// horizontal writing, Y in vertical writing). It already includes padding and paragraph indent, and is
    /// narrowed by wrap regions when text flows around an exclusion. Line adjustment must use this value instead
    /// of re-deriving the span from <c>MaxWidth</c>, otherwise wrap regions and indents disagree between L1 and L2.
    /// </summary>
    public float LineLeft { get; init; }

    /// <summary>End (exclusive) of the usable span for this line along the inline axis, relative to the content origin.</summary>
    public float LineRight { get; init; }

    /// <summary>
    /// The intervals this line offers, left to right and non-overlapping: one for an ordinary line, more when
    /// text flows around an exclusion that covers only part of it.
    /// <para>
    /// <see cref="LineLeft"/> and <see cref="LineRight"/> describe the first interval and stay the convenient
    /// way to ask about an ordinary line; anything that positions elements must use the interval the element
    /// was placed in (<see cref="LayoutElement.SpanIndex"/>), or text in the second interval would be moved to
    /// the first one.
    /// </para>
    /// </summary>
    public LineSpan[] Spans { get; init; } = [];

    /// <summary>
    /// Extra space the line's content is pushed right by, relative to <see cref="LineLeft"/>: the
    /// first-line indent of its paragraph, in pixels.
    /// <para>
    /// It is part of the line geometry because two stages need the same answer. Line breaking uses it to
    /// narrow the line, and line adjustment must start its positioning at
    /// <c>LineLeft + LineIndent</c> — otherwise the indent is applied while breaking, then erased when
    /// the elements are repositioned, which is exactly what happened before this field existed.
    /// </para>
    /// </summary>
    public float LineIndent { get; init; }
}
