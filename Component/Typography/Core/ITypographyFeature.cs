using System.Collections.Generic;

namespace GodotNodeExtension.Component.Typography.Core;

using Model;
using Languages;

/// <summary>
/// What part of the text a feature has a say over. It decides who wins when two features disagree: a
/// paragraph-level decision outranks a run-level one, which outranks a decision about a single boundary.
/// </summary>
public enum FeatureScope
{
    /// <summary>The feature answers for a whole paragraph (writing mode, alignment, indent).</summary>
    Paragraph,

    /// <summary>The feature answers for a run of one script (segmentation, shaping, line breaking rules).</summary>
    Run,

    /// <summary>The feature answers for the position between two clusters.</summary>
    Boundary,

    /// <summary>The feature answers for one cluster (classification, display form).</summary>
    Cluster,
}

/// <summary>
/// A pluggable piece of typography behaviour.
/// <para>
/// The point of naming features — rather than switching on a language — is that a language profile selects
/// behaviour by id, so a host can register a rule set the engine has never heard of and a profile can name it.
/// Dependencies and conflicts are declared rather than discovered, which is what keeps "a language is a set of
/// features" from turning into "a language is a set of features that silently contradict each other".
/// </para>
/// </summary>
public interface ITypographyFeature
{
    /// <summary>Stable identifier a profile refers to, e.g. <c>line-breaking.kinsoku</c>.</summary>
    string Id { get; }

    /// <summary>Scope the feature answers for.</summary>
    FeatureScope Scope { get; }

    /// <summary>Ids of features this one needs to be present.</summary>
    IReadOnlyList<string> Requires { get; }

    /// <summary>Ids of features this one cannot be combined with.</summary>
    IReadOnlyList<string> ConflictsWith { get; }
}

/// <summary>
/// What a boundary rule decided about one position between two clusters.
/// </summary>
/// <param name="ForbiddenToBreak">Whether the position may not end a line.</param>
/// <param name="ForbiddenAtLineStart">Whether the right cluster may not start a line.</param>
/// <param name="ForbiddenAtLineEnd">Whether the left cluster may not end a line.</param>
public readonly record struct BoundaryDecision(
    bool ForbiddenToBreak,
    bool ForbiddenAtLineStart,
    bool ForbiddenAtLineEnd)
{
    /// <summary>Nothing forbids a break here.</summary>
    public static readonly BoundaryDecision Allowed = new(false, false, false);
}

/// <summary>
/// Everything a boundary rule needs to decide one position. Read-only: a rule answers a question, it does not
/// change the text.
/// </summary>
public readonly struct BoundaryRuleContext
{
    /// <summary>Cluster on the left of the position.</summary>
    public TextSegment Left { get; init; }

    /// <summary>Cluster on the right of the position.</summary>
    public TextSegment Right { get; init; }

    /// <summary>
    /// Index of the right cluster in the prepared text, which is also the position in
    /// <see cref="CodePoints"/> where the break would occur.
    /// </summary>
    public int Position { get; init; }

    /// <summary>The prepared text as code points, or null when the rule does not need the context.</summary>
    public IReadOnlyList<int>? CodePoints { get; init; }

    /// <summary>Effective typography parameters of this layout.</summary>
    public ResolvedTypography Typography { get; init; }

    /// <summary>
    /// The character at a code point position, or '\0' when the context carries no text or the position is out of
    /// range. Rules that need one character of look-behind (is this pair inside a number's unit?) use it.
    /// </summary>
    /// <param name="position">Code point index into <see cref="CodePoints"/>.</param>
    /// <returns>The character at that position, or '\0'.</returns>
    public char CharAt(int position) =>
        CodePoints is { } points && position >= 0 && position < points.Count
            ? char.ConvertFromUtf32(points[position])[0]
            : '\0';
}

/// <summary>
/// A feature that decides whether a line may break between two clusters.
/// <para>
/// Two implementations ship: the Unicode line breaking algorithm (UAX #14), which is the baseline every
/// script can use, and the CJK prohibition rules, which tailor it with character classes and line-edge
/// restrictions the default rules deliberately leave open.
/// </para>
/// </summary>
public interface IBoundaryRuleFeature : ITypographyFeature
{
    /// <summary>
    /// Decide the position between two clusters.
    /// </summary>
    /// <param name="context">Read-only view of the text around the position.</param>
    /// <returns>What the rule forbids there, if anything.</returns>
    BoundaryDecision Decide(in BoundaryRuleContext context);
}
