using System.Collections.Generic;
using GodotNodeExtension.Component.Typography.Core.Model;
namespace GodotNodeExtension.Component.Typography.Languages;

/// <summary>
/// Typography behaviour that belongs to a <em>language</em> rather than to a request: line-start and
/// line-end prohibition, the spacing rules between scripts, punctuation handling, and the defaults a
/// script convention prescribes for indent, alignment and spacing.
/// <para>
/// This exists because those rules were previously spelled as defaults on
/// <see cref="TypographySettings"/>, which is a per-request object. That made the engine behave as if
/// every document were Chinese (CJK prohibition on, CJK/Latin spacing on) and gave a caller no way to
/// say "this text is English" — the switch that should have been language-owned was request-owned.
/// </para>
/// <para>
/// A profile supplies defaults; a request may still override any of them explicitly, which is what the
/// nullable properties on <see cref="TypographySettings"/> express.
/// </para>
/// </summary>
public sealed class TypographyParameters
{
    /// <summary>
    /// Boundary rule feature that decides break opportunities between characters: the Unicode line breaking
    /// algorithm by default, or a language's own prohibition rules (see
    /// <see cref="Core.TypographyFeatureRegistry"/>).
    /// </summary>
    public string BoundaryRuleFeature { get; init; } = Core.TypographyFeatureRegistry.UnicodeBoundaryRuleId;

    /// <summary>Whether line-start/line-end prohibition rules are applied at all.</summary>
    public bool EnableLineProhibition { get; init; } = true;

    /// <summary>How strict the prohibition rules are (which character classes are affected).</summary>
    public ProhibitionLevel ProhibitionLevel { get; init; } = ProhibitionLevel.Basic;

    /// <summary>Whether a gap is inserted between CJK and Latin runs that were written without one.</summary>
    public bool EnableCjkLatinSpacing { get; init; } = true;

    /// <summary>Width of that gap in ems.</summary>
    public float CjkLatinSpacingEm { get; init; } = 0.25f;

    /// <summary>Whether punctuation may be squeezed to fit a line.</summary>
    public bool EnablePunctuationCompression { get; init; } = true;

    /// <summary>Default first-line indent in ems (0 = none; 2 is the CJK convention).</summary>
    public int FirstLineIndent { get; init; }

    /// <summary>Default paragraph spacing in pixels.</summary>
    public float ParagraphSpacing { get; init; }

    /// <summary>Default extra line spacing in pixels.</summary>
    public float LineSpacing { get; init; }

    /// <summary>Default alignment for paragraphs of this language.</summary>
    public TextAlignment Alignment { get; init; } = TextAlignment.Left;

    /// <summary>Base direction of this language's script.</summary>
    public TextDirection Direction { get; init; } = TextDirection.LeftToRight;

    /// <summary>
    /// OpenType language tag this language shapes with (for example <c>ZHS</c>, <c>ZHT</c>, <c>JAN</c>,
    /// <c>KOR</c>, <c>ENG</c>), or null for no preference.
    /// <para>
    /// It is what makes a font's localized forms reachable: the same code point is drawn differently in
    /// Chinese, Japanese and Korean fonts, and the OpenType <c>locl</c> feature that selects between them is
    /// driven by the language a shaper is told, not by the script. Shaping is also the earliest stage that
    /// can act on a language, so this value travels with the profile into the compile phase.
    /// </para>
    /// </summary>
    public string? OpenTypeLanguageTag { get; init; }

    /// <summary>
    /// Identifier of the hyphenation patterns this language breaks words with (see
    /// <see cref="Core.Hyphenation.HyphenationPatternSets"/>), or null when it does not hyphenate at all.
    /// Chinese, Japanese and Korean leave it null: their text breaks between characters, so a word-level break
    /// would be a rule the language does not have.
    /// </summary>
    public string? HyphenationPatternsId { get; init; }

    /// <summary>Whether this language's display forms (see <see cref="LetterformSubstitution"/>) are applied.</summary>
    public bool EnableLetterformSubstitution { get; init; } = true;

    /// <summary>
    /// Identifier of the character classes this language prohibits at line start and line end; see
    /// <see cref="KinsokuClassSet.Id"/>. Defaults to the engine's historical set.
    /// </summary>
    public string ProhibitionClassSetId { get; init; } = KinsokuClassSets.LegacyId;

    /// <summary>
    /// Lower bound of the CJK/Latin gap in ems: how far a line may compress it when it has to fit. The gap the
    /// layout inserts by default is <see cref="CjkLatinSpacingEm"/>, and the clreq/jlreq range is 1/8 to 1/2 em.
    /// </summary>
    public float CjkLatinSpacingMinEm { get; init; }

    /// <summary>Upper bound of the CJK/Latin gap in ems: how far a justified line may stretch it.</summary>
    public float CjkLatinSpacingMaxEm { get; init; }

    /// <summary>What this language does with the width of punctuation that ends a line.</summary>
    public LineEndPunctuationPolicy LineEndPunctuation { get; init; } = LineEndPunctuationPolicy.HalfWidthOnOverflow;

    /// <summary>Writing mode this language is laid out in; only horizontal is implemented.</summary>
    public WritingMode WritingMode { get; init; } = WritingMode.HorizontalTb;

    /// <summary>
    /// Whether this language lets a punctuation mark hang past the line's end edge (clreq §6.1.3 allows it for
    /// simplified Chinese; jlreq §3.8.2 advises against it in mixed text).
    /// </summary>
    public HangingPunctuationPolicy HangingPunctuation { get; init; }

    /// <summary>
    /// Whether an opening bracket at the head of a line may have its empty leading half trimmed. Both clreq
    /// §6.3.2.3 and jlreq §3.1.5 describe the adjustment.
    /// </summary>
    public bool HalfWidthOpeningBracketAtLineHead { get; init; }

    /// <summary>
    /// What this language does with the room an annotation needs: above the line, beside the base text, or
    /// nothing at all (the annotation then falls into the line gap the author's spacing provides).
    /// </summary>
    public RubyPlacement RubyPlacement { get; init; } = RubyPlacement.ReserveAbove;

    /// <summary>
    /// Which way this language sets the annotation itself: along the line, or down a column of its own. It is
    /// independent of <see cref="WritingMode"/> - Traditional Chinese sets Bopomofo as a column beside the base
    /// character in horizontal writing too (clreq §5.5.3.1).
    /// </summary>
    public RubyOrientation RubyOrientation { get; init; } = RubyOrientation.Horizontal;

    /// <summary>Which side of the base characters this language puts its emphasis marks on.</summary>
    public EmphasisSide EmphasisSide { get; init; } = EmphasisSide.Below;

    /// <summary>
    /// Emphasis mark size in ems. A convention value rather than a figure any specification states, kept in one
    /// place so it can be tuned once and so a dump records what it was.
    /// </summary>
    public float EmphasisMarkSizeEm { get; init; } = 0.25f;

    /// <summary>
    /// Display forms this language prefers: quotation marks, ellipsis, sentence marks. Empty for a language
    /// that does not rewrite anything, which is why an undeclared language changes no output.
    /// </summary>
    public IReadOnlyList<LetterformSubstitution> Letterforms { get; init; } = [];
}
