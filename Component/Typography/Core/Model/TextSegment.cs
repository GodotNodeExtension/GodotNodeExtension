namespace GodotNodeExtension.Component.Typography.Core.Model;

/// <summary>
/// A segment of text with uniform style, measured by HarfBuzz (or placeholder in Phase 1).
/// Analogous to pretext's "segment" concept. Produced by the Segmenter during the Prepare phase.
/// </summary>
public struct TextSegment
{
    /// <summary>Index of the source DrawElement.</summary>
    public int SourceIndex { get; init; }

    /// <summary>Character offset within the source DrawElement's Text.</summary>
    public int CharOffset { get; init; }

    /// <summary>Character count of this segment.</summary>
    public int CharLength { get; set; }

    /// <summary>The text content of this segment.</summary>
    public string Text { get; set; }

    /// <summary>Character class of this segment (all chars share the same class).</summary>
    public CharacterClass CharClass { get; init; }

    /// <summary>Measured natural width via HarfBuzz shaping (pixels). 0 until measured.</summary>
    public float Width { get; set; }

    /// <summary>Adjustable space that can be compressed (pixels).</summary>
    public float AdjustableSpace { get; init; }

    /// <summary>
    /// Minimum acceptable width after full compression (pixels), computed as
    /// <c>Width - AdjustableSpace</c>. The prepare phase writes it; the line breaker does not read it
    /// yet, so punctuation compression currently happens only in line adjustment. It is kept because
    /// it is the natural carrier for a compression floor and the P2 break solver consumes it.
    /// </summary>
    public float MinWidth { get; set; }

    /// <summary>Whether a line break is allowed after this segment.</summary>
    public bool CanBreakAfter { get; set; }

    /// <summary>
    /// Per-glyph advance widths from HarfBuzz, in glyph order. Note that an advance count is not a
    /// character count: shaping can merge several characters into one glyph and split one character into
    /// several, which is why <see cref="Glyphs"/> carries a cluster mapping rather than only widths.
    /// </summary>
    public float[]? GlyphAdvances { get; set; }

    /// <summary>
    /// Shaped glyphs of this segment: what the renderer draws, and which source characters each glyph came
    /// from. Null when the segment has no text or could not be shaped.
    /// </summary>
    public Glyph[]? Glyphs { get; set; }

    /// <summary>
    /// The hyphen this piece would end a line with, already shaped, or null when the piece cannot be hyphenated.
    /// <para>
    /// A word cut for hyphenation reserves the hyphen's advance (it is part of the width a line has to fit), and the
    /// hyphen itself is drawn only when the line really ends there - which is a decision the line assembler takes, so
    /// the glyphs have to be shaped here, in the compile phase, where the font and its size are known.
    /// </para>
    /// </summary>
    public GlyphRun? HyphenRun { get; set; }

    /// <summary>
    /// Id of the font this segment was shaped with, in <see cref="FontCatalog"/> terms. Recorded here rather
    /// than read from the source element because a caller that invokes the engine directly never has its
    /// elements stamped with a resolved id, and the glyph run still has to name its font.
    /// </summary>
    public ulong FontId { get; set; }

    /// <summary>
    /// The text to shape and draw, when the language prefers a different display form than the one written
    /// (see <see cref="GodotNodeExtension.Component.Typography.Languages.LetterformResolver"/>); null when the
    /// source text is what gets drawn.
    /// <para>
    /// Substitution does not change <see cref="Text"/>: that stays the source, so selection, copy and the
    /// typewriter keep mapping to the characters the caller wrote, and only the shaped glyphs differ. The
    /// segment's <see cref="CharClass"/> is the source character's class too, because "this is a full stop" is
    /// a fact about the text, not about the form it is displayed in.
    /// </para>
    /// </summary>
    public string? DisplayText { get; set; }

    /// <summary>Font metrics: ascent for this segment's font.</summary>
    public float Ascent { get; set; }

    /// <summary>Font metrics: descent for this segment's font.</summary>
    public float Descent { get; set; }

    /// <summary>Index of the paragraph this segment belongs to.</summary>
    public int ParagraphIndex { get; init; }

    /// <summary>
    /// Language that governs this segment's paragraph, as declared by the element that started the paragraph,
    /// or null when the paragraph declares none.
    /// <para>
    /// The segment carries it because the compile phase is where a language first changes anything: shaping
    /// takes the language's OpenType conventions and its display forms, and that happens before the layout
    /// stage resolves a paragraph's typography. It is filled in by
    /// <see cref="Segmenter.StampParagraphLanguages"/>, which is the same rule paragraph settings are read
    /// with — the element that opens the paragraph owns them.
    /// </para>
    /// </summary>
    public string? LanguageTag { get; set; }

    /// <summary>Reference to the source DrawElement for pass-through attributes.</summary>
    public DrawElement Source { get; init; }
}
