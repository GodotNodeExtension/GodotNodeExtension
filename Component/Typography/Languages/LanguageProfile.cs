using System.Collections.Generic;

namespace GodotNodeExtension.Component.Typography.Languages;

/// <summary>
/// A language's typography contract: the parameters its script convention prescribes, plus the
/// fallback chain used when a more specific tag is not registered.
/// <para>
/// The profile deliberately carries no algorithm. Everything it can say is a value, so adding a
/// language is a data change (see the acceptance criterion in the architecture document: adding a
/// language must not require touching the framework).
/// </para>
/// </summary>
public sealed class LanguageProfile
{
    /// <summary>
    /// Create a profile.
    /// </summary>
    /// <param name="id">Canonical BCP-47 tag of the profile, e.g. <c>zh-Hans</c>.</param>
    /// <param name="fallback">Tags tried, in order, when this profile is not registered itself.</param>
    /// <param name="parameters">The typography parameters this language prescribes.</param>
    /// <param name="description">Why the parameters are what they are; quoted in diagnostics.</param>
    public LanguageProfile(
        string id,
        IReadOnlyList<string> fallback,
        TypographyParameters parameters,
        string description)
    {
        Id = id;
        Fallback = fallback;
        Parameters = parameters;
        Description = description;
    }

    /// <summary>Canonical BCP-47 tag of this profile.</summary>
    public string Id { get; }

    /// <summary>Tags tried in order before falling back to <c>und</c>.</summary>
    public IReadOnlyList<string> Fallback { get; }

    /// <summary>Parameters this language prescribes; see <see cref="TypographyParameters"/>.</summary>
    public TypographyParameters Parameters { get; }

    /// <summary>Short explanation of the parameter set, for diagnostics and golden dumps.</summary>
    public string Description { get; }

    /// <inheritdoc />
    public override string ToString() => $"{Id} ({Description})";
}
