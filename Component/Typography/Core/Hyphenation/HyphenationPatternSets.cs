namespace GodotNodeExtension.Component.Typography.Core.Hyphenation;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

/// <summary>
/// The hyphenation pattern sets the engine ships with, and the lookup a host uses to register its own.
/// <para>
/// The tables themselves are generated (<c>Tools/Typography/gen_hyphenation_tables.py</c> from the hyph-utf8 TeX patterns); this
/// is only the naming they are reached by, so a language profile can say <c>en-us</c> the way it says <c>clreq</c>
/// for a prohibition class set.
/// </para>
/// </summary>
public static class HyphenationPatternSets
{
    /// <summary>Identifier of the American English patterns.</summary>
    public const string EnUsId = "en-us";

    /// <summary>Identifier of the British English patterns.</summary>
    public const string EnGbId = "en-gb";

    /// <summary>Identifier of the German patterns (1996 orthography).</summary>
    public const string GermanId = "de-1996";

    /// <summary>Identifier of the French patterns.</summary>
    public const string FrenchId = "fr";

    private static readonly ConcurrentDictionary<string, HyphenationPatternSet> Sets =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [EnUsId] = new HyphenationPatternSet
            {
                Id = EnUsId,
                Source = HyphenationEnUs.Source,
                Version = HyphenationEnUs.Version,
                Sha256 = HyphenationEnUs.Sha256,
                Licence = HyphenationEnUs.Licence,
                LeftMin = HyphenationEnUs.LeftMin,
                RightMin = HyphenationEnUs.RightMin,
                PatternLines = HyphenationEnUs.PatternLines,
                ExceptionLines = HyphenationEnUs.ExceptionLines,
            },
            [EnGbId] = new HyphenationPatternSet
            {
                Id = EnGbId,
                Source = HyphenationEnGb.Source,
                Version = HyphenationEnGb.Version,
                Sha256 = HyphenationEnGb.Sha256,
                Licence = HyphenationEnGb.Licence,
                LeftMin = HyphenationEnGb.LeftMin,
                RightMin = HyphenationEnGb.RightMin,
                PatternLines = HyphenationEnGb.PatternLines,
                ExceptionLines = HyphenationEnGb.ExceptionLines,
            },
            [GermanId] = new HyphenationPatternSet
            {
                Id = GermanId,
                Source = HyphenationDe1996.Source,
                Version = HyphenationDe1996.Version,
                Sha256 = HyphenationDe1996.Sha256,
                Licence = HyphenationDe1996.Licence,
                LeftMin = HyphenationDe1996.LeftMin,
                RightMin = HyphenationDe1996.RightMin,
                PatternLines = HyphenationDe1996.PatternLines,
                ExceptionLines = HyphenationDe1996.ExceptionLines,
            },
            [FrenchId] = new HyphenationPatternSet
            {
                Id = FrenchId,
                Source = HyphenationFr.Source,
                Version = HyphenationFr.Version,
                Sha256 = HyphenationFr.Sha256,
                Licence = HyphenationFr.Licence,
                LeftMin = HyphenationFr.LeftMin,
                RightMin = HyphenationFr.RightMin,
                PatternLines = HyphenationFr.PatternLines,
                ExceptionLines = HyphenationFr.ExceptionLines,
            },
        };

    /// <summary>
    /// Look a set up by the identifier a language profile names.
    /// </summary>
    /// <param name="id">Identifier such as <c>en-us</c>.</param>
    /// <returns>The set, or null when nothing is registered under that id.</returns>
    public static HyphenationPatternSet? Find(string? id) =>
        string.IsNullOrEmpty(id) ? null : Sets.GetValueOrDefault(id);

    /// <summary>
    /// Register a set, replacing any set with the same id. This is how a host adds patterns the engine does not ship,
    /// or points a language at its own table.
    /// </summary>
    /// <param name="set">The set to register.</param>
    /// <exception cref="ArgumentNullException">When <paramref name="set"/> is null.</exception>
    public static void Register(HyphenationPatternSet set)
    {
        ArgumentNullException.ThrowIfNull(set);
        Sets[set.Id] = set;
    }

    /// <summary>Identifiers of the registered sets, for diagnostics.</summary>
    public static ICollection<string> RegisteredIds => Sets.Keys;
}
