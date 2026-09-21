using System;
using System.Collections.Generic;
using GodotNodeExtension.Component.Typography.Core.Model;
using GodotNodeExtension.Component.Typography.Languages;

namespace GodotNodeExtension.Component.Typography.Core;

/// <summary>
/// Builds the <see cref="Boundary"/> list of a prepared paragraph: the compile-phase step that turns
/// "what characters are next to each other" into "what decisions apply between them".
/// <para>
/// It reads the same rules the break and adjustment stages apply today
/// (<see cref="CharClassifier"/> for prohibition and unbreakable pairs, the segment's own
/// <see cref="TextSegment.CanBreakAfter"/> for break permission), so the boundaries it produces describe
/// the current behaviour exactly. That is deliberate: the model can then be compared against the
/// behaviour it is meant to replace, and consuming it becomes a reviewable change instead of a leap.
/// </para>
/// </summary>
public static class BoundaryBuilder
{
    /// <summary>
    /// Build the boundaries between the clusters of a prepared paragraph.
    /// </summary>
    /// <param name="prepared">Measured segments in document order.</param>
    /// <param name="typography">Effective parameters (prohibition strictness, script-spacing rule).</param>
    /// <param name="paragraphTypography">
    /// Parameters per paragraph, when a paragraph named its own language; null for a single-language document,
    /// which then uses <paramref name="typography"/> throughout.
    /// </param>
    /// <returns>One boundary per adjacent segment pair; empty for fewer than two segments.</returns>
    /// <exception cref="ArgumentNullException">When an argument is null.</exception>
    public static List<Boundary> Build(
        PreparedContent prepared,
        ResolvedTypography typography,
        ResolvedTypography[]? paragraphTypography = null)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        ArgumentNullException.ThrowIfNull(typography);

        var segments = prepared.Segments;
        var boundaries = new List<Boundary>(Math.Max(0, segments.Count - 1));

        // The Unicode policy needs the text as code points and the position of every cluster, because its
        // rules look at the surrounding text rather than only at the pair.
        int[]? codePoints = null;
        int[]? clusterStarts = null;

        // The Unicode rule needs the text as code points; an id is either one of the two built-ins or a
        // feature registered by the host, so the index is built whenever any rule may ask for it.
        if (segments.Count > 1)
            BuildCodePointIndex(segments, out codePoints, out clusterStarts);

        for (int i = 0; i < segments.Count - 1; i++)
        {
            // The boundary belongs to the paragraph the left cluster is in: a break is a decision about that
            // paragraph's convention.
            ResolvedTypography effective = TypographyFor(paragraphTypography, segments[i], typography);

            boundaries.Add(BuildOne(
                segments[i], segments[i + 1], i, i + 1, effective, codePoints, clusterStarts,
                prepared.InsideRubyGroup.Contains(i + 1)));
        }

        return boundaries;
    }

    /// <summary>
    /// Decide one boundary. Each field is derived from the rule the corresponding stage applies today, and
    /// the reason string records which rule it was.
    /// </summary>
    /// <param name="left">Left segment.</param>
    /// <param name="right">Right segment.</param>
    /// <param name="leftIndex">Index of the left segment (its cluster index).</param>
    /// <param name="rightIndex">Index of the right segment.</param>
    /// <param name="typography">Effective typography parameters.</param>
    /// <param name="codePoints">
    /// The prepared text as code points, or null when the policy does not need it.
    /// </param>
    /// <param name="clusterStarts">
    /// Code point index each cluster starts at, parallel to <paramref name="codePoints"/>.
    /// </param>
    /// <param name="insideRubyGroup">
    /// Whether the position sits inside a run that carries one annotation, which no break may split.
    /// </param>
    /// <returns>The boundary between the two segments.</returns>
    private static Boundary BuildOne(
        in TextSegment left,
        in TextSegment right,
        int leftIndex,
        int rightIndex,
        ResolvedTypography typography,
        int[]? codePoints,
        int[]? clusterStarts,
        bool insideRubyGroup)
    {
        ScriptRole leftScript = RoleOf(left);
        ScriptRole rightScript = RoleOf(right);

        char leftChar = LastChar(left);
        char rightChar = FirstChar(right);

        // The language names the rule that decides break positions; the registry turns that name into
        // behaviour, and falls back to the Unicode baseline when the name is unknown.
        IBoundaryRuleFeature boundaryRule =
            TypographyFeatureRegistry.Shared.ResolveBoundaryRule(typography.BoundaryRuleFeature);

        var context = new BoundaryRuleContext
        {
            Left = left,
            Right = right,
            Position = clusterStarts != null && rightIndex < clusterStarts.Length ? clusterStarts[rightIndex] : 0,
            CodePoints = codePoints,
            Typography = typography,
        };

        BoundaryDecision decision = boundaryRule.Decide(context);

        bool forbiddenToBreak = decision.ForbiddenToBreak;
        bool forbiddenAtLineStart = decision.ForbiddenAtLineStart;
        bool forbiddenAtLineEnd = decision.ForbiddenAtLineEnd;

        // Classification is diagnostic: it labels the pair for a dump or a test. It must not be fed the rule's
        // decision, because "a break is forbidden here" is true for every closing bracket, while "this is a
        // number with its affix" is a property of the characters themselves.
        bool numericPair = leftChar != '\0' && rightChar != '\0'
                           && (CharClassifier.IsUnbreakablePair(leftChar, rightChar)
                               || NumberUnitBinding.Binds(
                                   PreviousChar(codePoints, clusterStarts, rightIndex),
                                   leftChar,
                                   rightChar));

        var (kind, spacing, reason) = Classify(
            left, right, leftScript, rightScript, numericPair, typography);

        if (boundaryRule.Id == TypographyFeatureRegistry.UnicodeBoundaryRuleId)
            reason += ":UAX14";

        // A ruby group is atomic the way a number with its unit is: the annotation belongs to the run, so no
        // position inside it may end a line. The check is framework-level (it reads the prepared content, not a
        // language rule) because "the annotation stays with its base" is not a convention a language may drop.
        if (insideRubyGroup)
        {
            forbiddenToBreak = true;
            reason = "RubyGroup";
        }

        // A tab stands for a stop, so neither side of it may end a line: the break would move the content away
        // from the stop it belongs to. Framework-level, like a ruby group or a number with its unit.
        if (left.CharClass == CharacterClass.Tab || right.CharClass == CharacterClass.Tab)
        {
            forbiddenToBreak = true;
            reason = "TabStop";
        }

        bool cjkLatinGap = kind == BoundaryKind.ScriptChange && spacing > 0f;

        // A language that states no range (both bounds zero) keeps the engine's "not adjustable" expression:
        // an equal min/max. Only a language that names the clreq/jlreq range gets a real interval.
        float minSpacing = cjkLatinGap && typography.CjkLatinSpacingMinEm > 0f
            ? EmOf(left) * typography.CjkLatinSpacingMinEm
            : spacing;
        float maxSpacing = cjkLatinGap && typography.CjkLatinSpacingMaxEm > 0f
            ? EmOf(left) * typography.CjkLatinSpacingMaxEm
            : spacing;

        return new Boundary
        {
            LeftCluster = leftIndex,
            RightCluster = rightIndex,
            Kind = kind,
            LeftScript = leftScript,
            RightScript = rightScript,
            Owner = BoundaryOwner.Left,
            BaseSpacing = spacing,
            // The CJK/Latin gap is the one boundary whose limits a convention states (clreq §6.3.3, jlreq
            // §3.2.6: 1/8 to 1/2 em), so it carries the range it may be compressed and stretched within. Every
            // other boundary keeps an equal min/max, which is how "this is not adjustable" is expressed while
            // the adjustment stage still takes squeezing and stretching decisions through its own ladder.
            MinSpacing = minSpacing,
            MaxSpacing = maxSpacing,
            ForbiddenAtLineStart = forbiddenAtLineStart,
            ForbiddenAtLineEnd = forbiddenAtLineEnd,
            ForbiddenToBreak = forbiddenToBreak,
            ForbiddenToStretch = false,
            Reason = reason,
        };
    }

    /// <summary>
    /// Classify the pair. The kind is what a reader of a dump needs first; the spacing is the gap the
    /// current implementation would insert, so it is expressed in the boundary's own font size.
    /// </summary>
    /// <param name="left">Left segment.</param>
    /// <param name="right">Right segment.</param>
    /// <param name="leftScript">Script role of the left segment.</param>
    /// <param name="rightScript">Script role of the right segment.</param>
    /// <param name="numericPair">Whether the pair is a number with its affix or unit.</param>
    /// <param name="typography">Effective typography parameters.</param>
    /// <returns>The kind, the natural spacing in pixels, and the reason string.</returns>
    private static (BoundaryKind Kind, float Spacing, string Reason) Classify(
        in TextSegment left,
        in TextSegment right,
        ScriptRole leftScript,
        ScriptRole rightScript,
        bool numericPair,
        ResolvedTypography typography)
    {
        if (left.CharClass == CharacterClass.Break || right.CharClass == CharacterClass.Break)
            return (BoundaryKind.HardBreak, 0f, "HardBreak");

        if (IsObject(left) || IsObject(right))
            return (BoundaryKind.InlineObject, 0f, "InlineObject");

        if (left.CharClass is CharacterClass.Space or CharacterClass.Tab
            || right.CharClass is CharacterClass.Space or CharacterClass.Tab)
            return (BoundaryKind.SpaceRun, 0f, "SpaceRun");

        // A number with its unit is a numeric pair rather than merely a change of script: testing it first is what
        // makes a dump say why 10米 does not break, instead of calling it a script change.
        if (numericPair)
            return (BoundaryKind.Numeric, 0f, "UnbreakablePair");

        bool cjkLatin = IsCjk(leftScript) && IsWestern(rightScript)
                        || IsWestern(leftScript) && IsCjk(rightScript);

        if (cjkLatin)
        {
            float spacing = typography.EnableCjkLatinSpacing
                ? EmOf(left) * typography.CjkLatinSpacingEm
                : 0f;

            return (BoundaryKind.ScriptChange, spacing,
                typography.EnableCjkLatinSpacing ? "ScriptChange:CjkLatinGap" : "ScriptChange:GapDisabled");
        }

        if (IsPunctuation(left) || IsPunctuation(right))
            return (BoundaryKind.Punctuation, 0f, "Punctuation");

        if (leftScript != rightScript)
            return (BoundaryKind.ScriptChange, 0f, "ScriptChange:Other");

        return (BoundaryKind.Plain, 0f, "Plain");
    }

    /// <summary>
    /// The character before the left character of the pair that straddles two clusters, or '\0' when it is not
    /// available. Classification needs it to tell a multi-character unit (公斤) from an unrelated word (办公), and
    /// it uses the same position the boundary rules do: the code point just before the left cluster's last one.
    /// </summary>
    /// <param name="codePoints">Prepared text as code points, or null.</param>
    /// <param name="clusterStarts">Code point index each cluster starts at, or null.</param>
    /// <param name="rightClusterIndex">Index of the right cluster of the pair.</param>
    /// <returns>The character before the pair's left character, or '\0'.</returns>
    private static char PreviousChar(int[]? codePoints, int[]? clusterStarts, int rightClusterIndex)
    {
        if (codePoints is null || clusterStarts is null
            || rightClusterIndex < 0 || rightClusterIndex >= clusterStarts.Length)
        {
            return '\0';
        }

        int position = clusterStarts[rightClusterIndex] - 2;

        return position >= 0 && position < codePoints.Length
            ? char.ConvertFromUtf32(codePoints[position])[0]
            : '\0';
    }

    /// <summary>
    /// Typography for the paragraph a cluster belongs to, falling back to the request's when the paragraph named
    /// no language.
    /// </summary>
    /// <param name="paragraphTypography">Per-paragraph parameters, or null.</param>
    /// <param name="segment">The cluster the decision is about.</param>
    /// <param name="fallback">Parameters to use when the paragraph has none of its own.</param>
    /// <returns>The parameters that govern this cluster.</returns>
    private static ResolvedTypography TypographyFor(
        ResolvedTypography[]? paragraphTypography,
        in TextSegment segment,
        ResolvedTypography fallback) =>
        paragraphTypography is not null
        && segment.ParagraphIndex >= 0
        && segment.ParagraphIndex < paragraphTypography.Length
            ? paragraphTypography[segment.ParagraphIndex]
            : fallback;

    /// <summary>
    /// Flatten the prepared segments into one code point sequence and record where each cluster starts, so a
    /// boundary can ask the Unicode algorithm about the exact position between two clusters.
    /// </summary>
    /// <param name="segments">Prepared segments in document order.</param>
    /// <param name="codePoints">Output: every segment's text as code points, concatenated.</param>
    /// <param name="clusterStarts">Output: the code point index each segment starts at.</param>
    private static void BuildCodePointIndex(
        List<TextSegment> segments,
        out int[] codePoints,
        out int[] clusterStarts)
    {
        var points = new List<int>();
        var starts = new int[segments.Count];

        for (int i = 0; i < segments.Count; i++)
        {
            starts[i] = points.Count;
            string text = segments[i].Text;

            for (int c = 0; c < text.Length; c++)
            {
                points.Add(char.ConvertToUtf32(text, c));

                if (char.IsHighSurrogate(text[c]) && c + 1 < text.Length && char.IsLowSurrogate(text[c + 1]))
                    c++;
            }
        }

        codePoints = [.. points];
        clusterStarts = starts;
    }

    /// <summary>Whether a segment is a non-text object.</summary>
    /// <param name="segment">The segment to test.</param>
    /// <returns>True for images, rules, extensions and block markers.</returns>
    private static bool IsObject(in TextSegment segment) => segment.CharClass is
        CharacterClass.NonText or CharacterClass.Block or CharacterClass.BlockStart or CharacterClass.BlockEnd;

    /// <summary>Whether a script role is part of the CJK group.</summary>
    /// <param name="role">The role to test.</param>
    /// <returns>True for the CJK group.</returns>
    private static bool IsCjk(ScriptRole role) => role == ScriptRole.Han;

    /// <summary>
    /// Whether a script role belongs to the Western group for the purpose of the CJK/Latin gap.
    /// <para>
    /// Digits count as Western because <see cref="CharClassifier"/> classifies them as
    /// <see cref="CharacterClass.Latin"/>, so the existing adjustment rule already inserts a gap between
    /// a CJK character and a digit. Keeping the roles distinct while sharing this predicate lets the
    /// boundary describe today's behaviour exactly and still make the distinction available to the rules
    /// that will need it (clreq treats a digit and a Latin letter differently in places).
    /// </para>
    /// </summary>
    /// <param name="role">The role to test.</param>
    /// <returns>True for Latin letters and digits.</returns>
    private static bool IsWestern(ScriptRole role) =>
        role is ScriptRole.LatinLetter or ScriptRole.Digit;

    /// <summary>Whether a segment is punctuation of any class.</summary>
    /// <param name="segment">The segment to test.</param>
    /// <returns>True when the segment is punctuation.</returns>
    private static bool IsPunctuation(in TextSegment segment) => segment.CharClass is
        CharacterClass.PunctuationOpen
        or CharacterClass.PunctuationClose
        or CharacterClass.PunctuationPauseStop
        or CharacterClass.PunctuationDash
        or CharacterClass.PunctuationEllipsis
        or CharacterClass.PunctuationInterpunct;

    /// <summary>
    /// Map a segment's character class to the script role a boundary rule asks about. The CJK group is
    /// not split into Han, kana and hangul because the classifier does not distinguish them yet; that
    /// split belongs to the same step that aligns the profiles with the script conventions.
    /// </summary>
    /// <param name="segment">The segment to classify.</param>
    /// <returns>The script role of the segment.</returns>
    private static ScriptRole RoleOf(in TextSegment segment)
    {
        switch (segment.CharClass)
        {
            case CharacterClass.Ideograph:
                return ScriptRole.Han;

            case CharacterClass.Space or CharacterClass.Tab:
                return ScriptRole.Space;

            case CharacterClass.Break:
                return ScriptRole.Neutral;

            case CharacterClass.NonText:
            case CharacterClass.Block:
            case CharacterClass.BlockStart:
            case CharacterClass.BlockEnd:
                return ScriptRole.InlineObject;

            case CharacterClass.PunctuationWestern:
                return ScriptRole.WesternPunctuation;

            case CharacterClass.Latin:
            {
                char c = FirstChar(segment);
                return c != '\0' && char.IsDigit(c) ? ScriptRole.Digit : ScriptRole.LatinLetter;
            }

            default:
                return ScriptRole.Punctuation;
        }
    }

    /// <summary>
    /// The em size a boundary is measured in: the left cluster's font size, falling back to the prepare
    /// phase's default so a boundary never silently measures as zero.
    /// </summary>
    /// <param name="segment">The left segment of the boundary.</param>
    /// <returns>Em size in pixels.</returns>
    private static float EmOf(in TextSegment segment) =>
        segment.Source.FontSize > 0 ? segment.Source.FontSize : 16f;

    /// <summary>First character of a segment, or '\0' when it has no text.</summary>
    /// <param name="segment">The segment to read.</param>
    /// <returns>The first character, or '\0'.</returns>
    private static char FirstChar(in TextSegment segment) =>
        string.IsNullOrEmpty(segment.Text) ? '\0' : segment.Text[0];

    /// <summary>Last character of a segment, or '\0' when it has no text.</summary>
    /// <param name="segment">The segment to read.</param>
    /// <returns>The last character, or '\0'.</returns>
    private static char LastChar(in TextSegment segment) =>
        string.IsNullOrEmpty(segment.Text) ? '\0' : segment.Text[^1];
}
