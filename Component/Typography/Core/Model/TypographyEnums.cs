namespace GodotNodeExtension.Component.Typography.Core.Model;

/// <summary>
/// Prohibition rule strictness level for line-start/end rules (CLREQ §6.1.1).
/// </summary>
public enum ProhibitionLevel
{
    /// <summary>No line-start/end rules.</summary>
    None,

    /// <summary>Standard prohibition (recommended).</summary>
    Basic,

    /// <summary>GB/T 15834 compliance.</summary>
    Gb,

    /// <summary>Strictest: dash/ellipsis also prohibited at line start.</summary>
    Strict,
}

/// <summary>
/// Text alignment mode for layout.
/// </summary>
public enum TextAlignment
{
    /// <summary>Left-aligned text.</summary>
    Left,

    /// <summary>Center-aligned text.</summary>
    Center,

    /// <summary>Right-aligned text.</summary>
    Right,

    /// <summary>Justified text (both edges aligned).</summary>
    Justify,
}

/// <summary>
/// Base text direction of a paragraph, run or laid-out element.
/// <para>
/// Layout always works in logical order; the direction decides, per segment, which edge a line
/// starts at and how the assembled elements are ordered for display. Only
/// <see cref="LeftToRight"/> is produced today (no bidi pass exists yet); the enum is part of the
/// output contract so that adding a right-to-left pass later does not change the element shape.
/// </para>
/// </summary>
public enum TextDirection
{
    /// <summary>Left-to-right (Latin, CJK, and every script supported today).</summary>
    LeftToRight,

    /// <summary>Right-to-left (Arabic, Hebrew, and other RTL scripts; reserved, not yet produced).</summary>
    RightToLeft,
}

/// <summary>
/// Vertical alignment mode for inline non-text elements (images) within a line.
/// Controls how the element's height is distributed between ascent and descent,
/// which determines its vertical position relative to the text baseline.
/// </summary>
public enum InlineVerticalAlignment
{
    /// <summary>Bottom of element sits on the text baseline (ascent = height, descent = 0).</summary>
    Baseline,

    /// <summary>Top of element aligns with top of line (ascent = height, descent = 0).</summary>
    Top,

    /// <summary>Center of element aligns with text baseline (ascent = height/2, descent = height/2).</summary>
    Middle,

    /// <summary>Bottom of element aligns with bottom of line (ascent = 0, descent = height).</summary>
    Bottom,
}

/// <summary>
/// What a language does with the width of a punctuation mark that ends a line.
/// <para>
/// The conventions disagree, and the disagreement is visible in the geometry rather than in a glyph: a Chinese
/// line may trim the trailing half of a full-width stop (clreq §6.2.2.3), Japanese keeps the half em that
/// follows a full stop uncompressible (jlreq §3.1.9), and Korean chooses a narrower character in the first
/// place (that substitution is a display form, not a width rule).
/// </para>
/// </summary>
public enum LineEndPunctuationPolicy
{
    /// <summary>Leave the width alone.</summary>
    None,

    /// <summary>
    /// Trim the trailing half em of a full-width mark that ends a line, when the line needs the room. This is
    /// the Chinese rule, and it is what the engine has always done for CJK text.
    /// </summary>
    HalfWidthOnOverflow,

    /// <summary>
    /// Keep the half em after a line-ending mark: the space is part of the mark and no adjustment may compress
    /// it. This is the Japanese rule, so a Japanese line that does not fit breaks earlier instead.
    /// </summary>
    PreserveHalfEm,
}

/// <summary>
/// Whether a punctuation mark may hang past the line's end edge instead of moving to the next line.
/// </summary>
public enum HangingPunctuationPolicy
{
    /// <summary>The mark moves to the next line.</summary>
    None,

    /// <summary>The mark may hang outside the line (clreq §6.1.3 allows it for simplified Chinese).</summary>
    Allowed,

    /// <summary>
    /// Only in vertical writing (traditional Chinese allows it there and not in horizontal writing, per
    /// clreq §6.1.3). Since vertical writing is not implemented, this behaves like <see cref="None"/> today and
    /// says why.
    /// </summary>
    VerticalOnly,

    /// <summary>
    /// Not when CJK and Latin mix on the line (jlreq §3.8.2 notes hanging punctuation reads badly in mixed
    /// text).
    /// </summary>
    NotInMixedText,
}

/// <summary>
/// What a language does with the room an annotation needs.
/// </summary>
public enum RubyPlacement
{
    /// <summary>
    /// The annotation sits above the text box, inside the line: the line grows by the band and its baselines move
    /// down together.
    /// </summary>
    ReserveAbove,

    /// <summary>
    /// The annotation sits above the text box as well, because that is where the space for it is: jlreq's 行間処理
    /// puts ruby between the lines, and the engine reserves the band rather than trusting the author's line spacing
    /// to be large enough - a line whose annotation overlaps its neighbour is not a usable layout, and this engine
    /// grew annotated lines instead of reporting one.
    /// </summary>
    OverflowBetweenLines,

    /// <summary>
    /// Reserve the room beside the base text: the annotation band sits to the right of the character it annotates
    /// and the room comes from the base text's own advance, which grows by half an em of the base size per
    /// annotated character (clreq §5.5.3.2, note 2), so the line's height does not change.
    /// <para>
    /// This is where Bopomofo goes in either writing mode: clreq §5.5.3.1 makes the right-hand position the
    /// preferred one for Traditional Chinese in horizontal writing as much as in vertical, and §5.5.3.2 states
    /// the room the spacing has to provide for it. Whether the band's content then reads across the band or down
    /// it is <see cref="RubyOrientation"/>'s business, not this value's.
    /// </para>
    /// </summary>
    ReserveBeside,
}

/// <summary>
/// Which way the annotation itself is set, independently of the writing mode the base text is in.
/// <para>
/// A language owns this the way it owns its line-breaking rules. Traditional Chinese sets Bopomofo down a column
/// of its own beside the base character, in horizontal writing as well as in vertical (clreq §5.5.3.1: the
/// right-hand position is the recommended one "whether in horizontal or vertical writing mode", and §5.5.3.2's
/// note for horizontal writing describes the column that has to fit in the half-em space); Japanese and
/// Simplified Chinese set their annotations along the line.
/// </para>
/// <para>
/// Because the two directions are independent, a horizontal paragraph can carry a column: the base text runs
/// left to right and each annotation runs top to bottom beside its character. That is why this is not derived
/// from <see cref="WritingMode"/> - the reference examples for Bopomofo show the same vertical annotation in
/// both modes.
/// </para>
/// </summary>
public enum RubyOrientation
{
    /// <summary>The annotation is set along the line, the way the base text is.</summary>
    Horizontal,

    /// <summary>The annotation is set down a column of its own: one symbol under the next, beside the base character.</summary>
    Vertical,
}

/// <summary>
/// The writing mode a paragraph is laid out in.
/// <para>
/// Only <see cref="HorizontalTb"/> is implemented. The value exists because several conventions are stated per
/// writing mode — hanging punctuation, emphasis mark side, the Korean choice between full-width and narrow
/// sentence marks, ruby placement — and a rule that cannot ask which mode it is in has to either ignore those
/// distinctions or invent them. A profile that asks for a vertical mode today gets an explicit failure rather
/// than a horizontal layout that pretends to be vertical.
/// </para>
/// </summary>
public enum WritingMode
{
    /// <summary>Lines run left to right, text flows top to bottom (the only implemented mode).</summary>
    HorizontalTb,

    /// <summary>Lines run top to bottom, text flows right to left (reserved).</summary>
    VerticalRl,

    /// <summary>Lines run top to bottom, text flows left to right (reserved).</summary>
    VerticalLr,
}
