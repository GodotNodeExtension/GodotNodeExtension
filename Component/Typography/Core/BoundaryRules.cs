using System.Collections.Generic;
using GodotNodeExtension.Component.Typography.Core.Model;
using GodotNodeExtension.Component.Typography.Core.Unicode;
using GodotNodeExtension.Component.Typography.Languages;

namespace GodotNodeExtension.Component.Typography.Core;

/// <summary>
/// Boundary rule: decide break positions with the Unicode line breaking algorithm (UAX #14).
/// <para>
/// This is the baseline every script can use, and the rule a language without its own convention should name.
/// It answers only what the default algorithm answers; prohibition of specific character classes at line edges
/// is a tailoring and belongs to <see cref="KinsokuBoundaryRule"/>.
/// </para>
/// </summary>
public sealed class UnicodeBoundaryRule : IBoundaryRuleFeature
{
    /// <inheritdoc />
    public string Id => TypographyFeatureRegistry.UnicodeBoundaryRuleId;

    /// <inheritdoc />
    public FeatureScope Scope => FeatureScope.Boundary;

    /// <inheritdoc />
    public IReadOnlyList<string> Requires => [];

    /// <inheritdoc />
    public IReadOnlyList<string> ConflictsWith => [TypographyFeatureRegistry.KinsokuBoundaryRuleId];

    /// <inheritdoc />
    public BoundaryDecision Decide(in BoundaryRuleContext context)
    {
        // A number stays with its unit whatever the language: 10米, 5公斤 and 第3号 are as unbreakable as 10%.
        // The character before the pair tells a unit's second character (公斤) from an unrelated word (办公).
        if (NumberUnitBinding.Binds(
                context.CharAt(context.Position - 2),
                ClusterChars.Last(context.Left),
                ClusterChars.First(context.Right)))
        {
            return new BoundaryDecision(true, false, false);
        }

        // The breaker refuses a candidate when the left cluster cannot end a line at all; that is a property of
        // the cluster, not of the position, and it holds under every rule.
        if (!context.Left.CanBreakAfter)
            return new BoundaryDecision(true, false, false);

        if (context.CodePoints is not { } codePoints
            || context.Position <= 0
            || context.Position >= codePoints.Count)
        {
            return BoundaryDecision.Allowed;
        }

        var points = new int[codePoints.Count];

        for (int i = 0; i < codePoints.Count; i++)
            points[i] = codePoints[i];

        return LineBreakAlgorithm.OpportunityAt(points, context.Position) is LineBreakOpportunity.Prohibited
            ? new BoundaryDecision(true, false, false)
            : BoundaryDecision.Allowed;
    }
}

/// <summary>
/// Boundary rule: the CJK prohibition rules (CLREQ §6.1, jlreq §3.1.7-8, klreq §7.1.2-3).
/// <para>
/// These are a tailoring of the Unicode baseline: a closing bracket must not start a line, an opening bracket
/// must not end one, and a dash or ellipsis pair is one unit. The rule set is a feature rather than a branch in
/// the breaker so that another language can replace it without the breaker knowing.
/// </para>
/// </summary>
public sealed class KinsokuBoundaryRule : IBoundaryRuleFeature
{
    /// <inheritdoc />
    public string Id => TypographyFeatureRegistry.KinsokuBoundaryRuleId;

    /// <inheritdoc />
    public FeatureScope Scope => FeatureScope.Boundary;

    /// <inheritdoc />
    public IReadOnlyList<string> Requires => [];

    /// <inheritdoc />
    public IReadOnlyList<string> ConflictsWith => [TypographyFeatureRegistry.UnicodeBoundaryRuleId];

    /// <inheritdoc />
    public BoundaryDecision Decide(in BoundaryRuleContext context)
    {
        // The three answers are independent: a closing bracket is forbidden at line start, an opening bracket is
        // forbidden at line end, and "this pair must not be split" is a third question again. Returning early on
        // the first one would silently drop the other two.
        var level = context.Typography.EnableLineProhibition
            ? context.Typography.ProhibitionLevel
            : ProhibitionLevel.None;

        // The classes are the language's: a Japanese line stops at a small kana or a prolonged sound mark and a
        // Korean one at an iteration mark, while the sets the engine used to hard-code said neither.
        KinsokuClassSet classes = context.Typography.ProhibitionClassSet;

        char leftChar = ClusterChars.Last(context.Left);
        char rightChar = ClusterChars.First(context.Right);

        // A Common mark whose role the segmenter resolved as Western follows the Western convention, so the CJK
        // prohibition tables must not claim it: that resolution is the whole point of reading a quotation mark by
        // the script it sits in. Without this, U+201C next to Latin letters would still be forbidden at line end.
        bool leftWestern = context.Left.CharClass == CharacterClass.PunctuationWestern;
        bool rightWestern = context.Right.CharClass == CharacterClass.PunctuationWestern;

        bool forbiddenAtLineStart = !rightWestern
            && rightChar != '\0' && CharClassifier.IsProhibitedAtLineStart(rightChar, classes, level);
        bool forbiddenAtLineEnd = !leftWestern
            && leftChar != '\0' && CharClassifier.IsProhibitedAtLineEnd(leftChar, classes, level);
        bool unbreakablePair = leftChar != '\0' && rightChar != '\0'
                               && CharClassifier.IsUnbreakablePair(leftChar, rightChar);

        // A number with its unit is unbreakable under every rule set, not only under this one.
        bool numberWithUnit = NumberUnitBinding.Binds(
            context.CharAt(context.Position - 2), leftChar, rightChar);

        return new BoundaryDecision(
            !context.Left.CanBreakAfter || unbreakablePair || numberWithUnit,
            forbiddenAtLineStart,
            forbiddenAtLineEnd);
    }

}

/// <summary>
/// Character accessors shared by the boundary rules: what a cluster starts and ends with is what a rule looks at,
/// and both rules need the same answer for a cluster without text.
/// </summary>
internal static class ClusterChars
{
    /// <summary>First character of a cluster, or '\0' when it has no text.</summary>
    /// <param name="segment">The cluster to read.</param>
    /// <returns>The first character, or '\0'.</returns>
    internal static char First(in TextSegment segment) =>
        string.IsNullOrEmpty(segment.Text) ? '\0' : segment.Text[0];

    /// <summary>Last character of a cluster, or '\0' when it has no text.</summary>
    /// <param name="segment">The cluster to read.</param>
    /// <returns>The last character, or '\0'.</returns>
    internal static char Last(in TextSegment segment) =>
        string.IsNullOrEmpty(segment.Text) ? '\0' : segment.Text[^1];
}
