using System.Collections.Generic;
using GodotNodeExtension.Component.Typography.Core.Model;

namespace GodotNodeExtension.Component.Typography.Languages;

/// <summary>
/// The effective typography parameters of one layout request: the language profile's values with the
/// request's explicit overrides applied.
/// <para>
/// This is what the pipeline stages read. Splitting it out of <see cref="TypographySettings"/> has two
/// consequences that matter for the multi-language goal: a stage can no longer consult a request-level
/// default (so language behaviour cannot leak in through a default value), and the resolved set is a
/// single immutable value that can be cached, dumped and diffed per paragraph.
/// </para>
/// </summary>
public sealed record ResolvedTypography
{
    /// <summary>Profile this resolution came from; null only on a hand-built instance.</summary>
    public LanguageProfile? Profile { get; init; }

    // ── Geometry (request-owned) ──

    /// <summary>Maximum line width in pixels.</summary>
    public float MaxWidth { get; init; }

    /// <summary>
    /// Requested extent of a column along the inline axis, or 0 when the request did not name one. Only vertical
    /// writing uses it: there, <see cref="MaxWidth"/> bounds the block axis (how far the columns may run) and this
    /// bounds a line.
    /// </summary>
    public float MaxHeight { get; init; }

    /// <summary>
    /// How long a line may be, along the inline axis: <see cref="MaxWidth"/> in horizontal writing (which is the
    /// extent of both a line and the content box) and <see cref="MaxHeight"/> in vertical writing, falling back to
    /// <see cref="MaxWidth"/> when the request did not name a column height.
    /// </summary>
    public float InlineLimit =>
        WritingMode == WritingMode.HorizontalTb || MaxHeight <= 0f ? MaxWidth : MaxHeight;

    /// <summary>Padding around the content area, in pixels.</summary>
    public float Padding { get; init; }

    /// <summary>Exclusion regions text flows around.</summary>
    public IReadOnlyList<WrapRegion> WrapRegions { get; init; } = new List<WrapRegion>();

    /// <summary>Grid step in pixels, or null when no grid constrains the line.</summary>
    public float? GridStep { get; init; }

    /// <summary>Origin of the grid, relative to the content origin.</summary>
    public float GridOrigin { get; init; }

    /// <summary>Tab stops in effect, ascending.</summary>
    public IReadOnlyList<TabStop> TabStops { get; init; } = [];

    /// <summary>Spacing of the automatic tab stops in ems, or null when there are none.</summary>
    public float? DefaultTabStopEm { get; init; }

    // ── Paragraph style ──

    /// <summary>Extra line spacing in pixels.</summary>
    public float LineSpacing { get; init; }

    /// <summary>Paragraph spacing in pixels.</summary>
    public float ParagraphSpacing { get; init; }

    /// <summary>Alignment used when a paragraph does not override it.</summary>
    public TextAlignment Alignment { get; init; }

    /// <summary>First-line indent in ems used when a paragraph does not override it.</summary>
    public int FirstLineIndent { get; init; }

    // ── Language-governed behaviour ──

    /// <summary>Feature id that decides break opportunities; see <see cref="Core.TypographyFeatureRegistry"/>.</summary>
    public string BoundaryRuleFeature { get; init; } = Core.TypographyFeatureRegistry.UnicodeBoundaryRuleId;

    /// <summary>Whether line-start/line-end prohibition applies.</summary>
    public bool EnableLineProhibition { get; init; }

    /// <summary>Prohibition strictness.</summary>
    public ProhibitionLevel ProhibitionLevel { get; init; }

    /// <summary>Whether a gap is inserted between CJK and Latin runs.</summary>
    public bool EnableCjkLatinSpacing { get; init; }

    /// <summary>Width of the CJK/Latin gap in ems.</summary>
    public float CjkLatinSpacingEm { get; init; }

    /// <summary>Whether punctuation may be squeezed.</summary>
    public bool EnablePunctuationCompression { get; init; }

    /// <summary>Base direction of the paragraph.</summary>
    public TextDirection Direction { get; init; }

    /// <summary>
    /// OpenType language tag the compile phase shapes this paragraph with, or null for no preference. Read
    /// from the profile rather than from the request: it identifies the language, it is not a knob.
    /// </summary>
    public string? OpenTypeLanguageTag { get; init; }

    /// <summary>Whether the compile phase applies this language's display forms.</summary>
    public bool EnableLetterformSubstitution { get; init; }

    /// <summary>Hyphenation patterns in effect, or null when this language does not hyphenate.</summary>
    public Core.Hyphenation.HyphenationPatternSet? Hyphenation { get; init; }

    /// <summary>Whether word hyphenation is applied at all (request override of the language's own switch).</summary>
    public bool EnableHyphenation { get; init; } = true;

    /// <summary>Display forms this language prefers; empty when it rewrites nothing.</summary>
    public IReadOnlyList<LetterformSubstitution> Letterforms { get; init; } = [];

    /// <summary>Classes this language prohibits at line start and line end.</summary>
    public KinsokuClassSet ProhibitionClassSet { get; init; } = KinsokuClassSets.Legacy;

    /// <summary>Lower bound of the CJK/Latin gap in ems.</summary>
    public float CjkLatinSpacingMinEm { get; init; }

    /// <summary>Upper bound of the CJK/Latin gap in ems.</summary>
    public float CjkLatinSpacingMaxEm { get; init; }

    /// <summary>What this language does with punctuation that ends a line.</summary>
    public LineEndPunctuationPolicy LineEndPunctuation { get; init; } = LineEndPunctuationPolicy.HalfWidthOnOverflow;

    /// <summary>Writing mode in effect.</summary>
    public WritingMode WritingMode { get; init; }

    /// <summary>Whether this language lets punctuation hang past the line's end edge.</summary>
    public HangingPunctuationPolicy HangingPunctuation { get; init; }

    /// <summary>Whether an opening bracket at the head of a line may be trimmed to half width.</summary>
    public bool HalfWidthOpeningBracketAtLineHead { get; init; }

    /// <summary>What this language does with the room an annotation needs.</summary>
    public RubyPlacement RubyPlacement { get; init; }

    /// <summary>Which way the annotation itself is set, independently of the writing mode.</summary>
    public RubyOrientation RubyOrientation { get; init; }

    /// <summary>Side the language puts emphasis marks on.</summary>
    public EmphasisSide EmphasisSide { get; init; }

    /// <summary>Emphasis mark size in ems.</summary>
    public float EmphasisMarkSizeEm { get; init; }

    /// <summary>
    /// Identifier of the profile in effect, for diagnostics and golden dumps. Kept as a string so a
    /// dumped resolution stays readable without resolving it again.
    /// </summary>
    public string ProfileId => Profile != null ? Profile.Id : "und";

    /// <summary>
    /// Whether a change from <paramref name="other"/> can change a boundary decision, as opposed to only
    /// the line geometry. Width, padding and wrap regions do not; the language-governed values do.
    /// <para>
    /// This is what lets a resize reuse the boundaries while a language switch rebuilds them: caching the
    /// compile half is only sound if the cache key names every input that can change its result.
    /// </para>
    /// <para>
    /// The CJK/Latin spacing is compared exactly on purpose: it is configuration, so two profiles that
    /// disagree by any amount are two different configurations. <c>float.Equals(float)</c> states that
    /// without a floating point comparison operator.
    /// </para>
    /// </summary>
    /// <param name="other">The parameters to compare against.</param>
    /// <returns>True when a boundary rebuild is required.</returns>
    public bool AffectsBoundaries(ResolvedTypography other) =>
        (Profile?.Id) != (other.Profile?.Id)
        || !string.Equals(BoundaryRuleFeature, other.BoundaryRuleFeature, System.StringComparison.Ordinal)
        || EnableLineProhibition != other.EnableLineProhibition
        || ProhibitionLevel != other.ProhibitionLevel
        || EnableCjkLatinSpacing != other.EnableCjkLatinSpacing
        || !CjkLatinSpacingEm.Equals(other.CjkLatinSpacingEm);

    /// <summary>
    /// Derive a copy for a nested layout (an auto-size block lays its content out at a narrower width).
    /// </summary>
    /// <param name="maxWidth">Width available to the nested content.</param>
    /// <param name="wrapRegions">Exclusion regions for the nested content.</param>
    /// <returns>A copy with the nested geometry; language behaviour is inherited unchanged.</returns>
    public ResolvedTypography ForNestedLayout(float maxWidth, IReadOnlyList<WrapRegion> wrapRegions) =>
        this with { MaxWidth = maxWidth, WrapRegions = wrapRegions };
}
