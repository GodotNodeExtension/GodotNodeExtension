using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace GodotNodeExtension.Component.Typography.Core;

/// <summary>
/// The features the engine can apply, keyed by id.
/// <para>
/// A language profile names the features it wants; this registry turns those names into behaviour and reports
/// the combinations that cannot work (a missing dependency, a declared conflict, an id nobody registered).
/// That check is the difference between a language pack and a pile of switches: a profile that asks for
/// something impossible fails loudly at registration instead of silently doing something else.
/// </para>
/// </summary>
public sealed class TypographyFeatureRegistry
{
    /// <summary>Boundary rule: the Unicode line breaking algorithm (UAX #14).</summary>
    public const string UnicodeBoundaryRuleId = "line-breaking.unicode";

    /// <summary>Boundary rule: CJK prohibition rules tailored on top of the Unicode baseline.</summary>
    public const string KinsokuBoundaryRuleId = "line-breaking.kinsoku";

    private readonly ConcurrentDictionary<string, ITypographyFeature> _features =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Process-wide registry with the built-in features.</summary>
    public static TypographyFeatureRegistry Shared { get; } = CreateDefault();

    /// <summary>
    /// Create a registry holding the features the engine ships with.
    /// </summary>
    /// <returns>A registry containing the built-in features.</returns>
    public static TypographyFeatureRegistry CreateDefault()
    {
        var registry = new TypographyFeatureRegistry();

        registry.Register(new UnicodeBoundaryRule());
        registry.Register(new KinsokuBoundaryRule());

        return registry;
    }

    /// <summary>
    /// Register a feature, replacing any feature with the same id.
    /// </summary>
    /// <param name="feature">The feature to register.</param>
    /// <exception cref="ArgumentNullException">When <paramref name="feature"/> is null.</exception>
    public void Register(ITypographyFeature feature)
    {
        ArgumentNullException.ThrowIfNull(feature);
        _features[feature.Id] = feature;
    }

    /// <summary>
    /// Look up a feature by id.
    /// </summary>
    /// <param name="featureId">The id to look up.</param>
    /// <returns>The feature, or null when nothing is registered under that id.</returns>
    public ITypographyFeature? Find(string featureId) =>
        string.IsNullOrEmpty(featureId) ? null : _features.GetValueOrDefault(featureId);

    /// <summary>
    /// Resolve the boundary rule with an id, falling back to the Unicode baseline when the id is unknown.
    /// <para>
    /// Falling back rather than failing keeps a profile with a typo from producing no line breaking at all; the
    /// fallback is visible instead through <see cref="Validate"/>, which a profile is expected to have passed.
    /// </para>
    /// </summary>
    /// <param name="featureId">Id named by the profile.</param>
    /// <returns>Always a usable rule.</returns>
    public IBoundaryRuleFeature ResolveBoundaryRule(string featureId) =>
        Find(featureId) as IBoundaryRuleFeature ?? new UnicodeBoundaryRule();

    /// <summary>
    /// Check a set of feature ids for problems: ids that are not registered, dependencies that are missing and
    /// pairs that declare a conflict.
    /// </summary>
    /// <param name="featureIds">Ids a profile wants to use.</param>
    /// <returns>One message per problem; empty when the set is usable.</returns>
    /// <exception cref="ArgumentNullException">When <paramref name="featureIds"/> is null.</exception>
    public IReadOnlyList<string> Validate(IEnumerable<string> featureIds)
    {
        ArgumentNullException.ThrowIfNull(featureIds);

        var wanted = new HashSet<string>(featureIds, StringComparer.OrdinalIgnoreCase);
        var problems = new List<string>();

        foreach (string id in wanted)
        {
            if (Find(id) is not { } feature)
            {
                problems.Add($"feature '{id}' is not registered");
                continue;
            }

            foreach (string dependency in feature.Requires)
            {
                if (!wanted.Contains(dependency))
                    problems.Add($"feature '{id}' requires '{dependency}', which the profile does not include");
            }

            foreach (string conflict in feature.ConflictsWith)
            {
                if (wanted.Contains(conflict))
                    problems.Add($"feature '{id}' conflicts with '{conflict}'");
            }
        }

        return problems;
    }
}
