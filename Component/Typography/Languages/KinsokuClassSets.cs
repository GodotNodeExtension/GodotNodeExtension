using System;
using System.Collections.Generic;
using GodotNodeExtension.Component.Typography.Core;
using GodotNodeExtension.Component.Typography.Core.Model;

namespace GodotNodeExtension.Component.Typography.Languages;

/// <summary>
/// The character classes a language prohibits at the start and at the end of a line, at each strictness level.
/// <para>
/// This is language data, not algorithm: the rules are the same everywhere ("do not start a line with a closing
/// bracket") and what differs is which characters count and how strict the level is. Chinese has four cumulative
/// levels (clreq §6.1.1), Japanese lists its classes as <c>cl-01</c>…<c>cl-30</c> (jlreq §3.1.7, §3.9) and
/// Korean names its own set (klreq §7.1.2‑3). The engine used to carry one hard-coded pair of strings, which
/// made every language Chinese by construction.
/// </para>
/// </summary>
public sealed class KinsokuClassSet
{
    /// <summary>Identifier a language profile names, for example <c>clreq</c> or <c>jlreq</c>.</summary>
    public string Id { get; init; } = "legacy";

    /// <summary>What the set covers and what it deliberately leaves out, for a reader of a dump.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>Characters prohibited at line start at the basic level.</summary>
    public string LineStartBasic { get; init; } = string.Empty;

    /// <summary>Characters prohibited at line start at the GB-style level (clreq's second level).</summary>
    public string LineStartGbStyle { get; init; } = string.Empty;

    /// <summary>Characters prohibited at line start at the strict level.</summary>
    public string LineStartStrict { get; init; } = string.Empty;

    /// <summary>Characters prohibited at line end at the basic level.</summary>
    public string LineEndBasic { get; init; } = string.Empty;

    /// <summary>Characters prohibited at line end at the GB-style level.</summary>
    public string LineEndGbStyle { get; init; } = string.Empty;

    /// <summary>Characters prohibited at line end at the strict level.</summary>
    public string LineEndStrict { get; init; } = string.Empty;

    /// <summary>
    /// The line-start set for a strictness level. <see cref="ProhibitionLevel.None"/> yields an empty set, which
    /// is how "this document applies no prohibition rules" is expressed here.
    /// </summary>
    /// <param name="level">Strictness asked for by the language or the request.</param>
    /// <returns>The characters that may not start a line.</returns>
    public string LineStartAt(ProhibitionLevel level) => level switch
    {
        ProhibitionLevel.None => string.Empty,
        ProhibitionLevel.Gb => LineStartGbStyle,
        ProhibitionLevel.Strict => LineStartStrict,
        _ => LineStartBasic,
    };

    /// <summary>
    /// The line-end set for a strictness level.
    /// </summary>
    /// <param name="level">Strictness asked for by the language or the request.</param>
    /// <returns>The characters that may not end a line.</returns>
    public string LineEndAt(ProhibitionLevel level) => level switch
    {
        ProhibitionLevel.None => string.Empty,
        ProhibitionLevel.Gb => LineEndGbStyle,
        ProhibitionLevel.Strict => LineEndStrict,
        _ => LineEndBasic,
    };
}

/// <summary>
/// The class sets the shipped language profiles name, and the lookup a host uses to register its own.
/// </summary>
public static class KinsokuClassSets
{
    /// <summary>Identifier of the historical set, the one the engine had before languages owned their sets.</summary>
    public const string LegacyId = "legacy";

    /// <summary>Identifier of the Chinese set defined by clreq §6.1.1.</summary>
    public const string ChineseId = "clreq";

    /// <summary>Identifier of the Japanese set defined by jlreq §3.1.7/§3.9.</summary>
    public const string JapaneseId = "jlreq";

    /// <summary>Identifier of the Korean set defined by klreq §7.1.2/§7.1.3.</summary>
    public const string KoreanId = "klreq";

    // ── Chinese (clreq §6.1.1) ──

    /// <summary>
    /// What clreq's <em>basic</em> level prohibits. Pause and stop marks, closing quotation marks, closing
    /// brackets, connector marks, the interpunct and the solidus may not start a line; opening marks may not end
    /// one.
    /// </summary>
    private const string ClreqBasicLineStart =
        CharClassifier.ClosePunctuation + CharClassifier.PauseStopPunctuation + "\u00B7\uFF0F\u2013";

    /// <summary>
    /// What clreq's <em>strict</em> level adds to the basic set: two-em dashes and ellipses may not start a line.
    /// </summary>
    private const string ClreqStrictLineStart = ClreqBasicLineStart + "\u2014\u2026\u2025";

    // ── Japanese (jlreq §3.1.7, §3.9, §C) ──

    /// <summary>
    /// jlreq's line-start prohibition, expressed as character classes: closing brackets (cl-02), hyphens (cl-03),
    /// dividing marks (cl-04), middle dots (cl-05), full stops (cl-06), commas (cl-07), iteration marks (cl-09),
    /// prolonged sound marks (cl-10), small kana (cl-11) and warichu closing brackets (cl-29).
    /// <para>
    /// The classes are encoded here as characters, because that is what this engine classifies; jlreq's tables
    /// also decide which classes may not be split from each other, which this engine does not model yet, so only
    /// the two rows that are about line edges are encoded. The data is a documented subset, not a claim of full
    /// coverage.
    /// </para>
    /// </summary>
    private const string JlreqLineStart =
        CharClassifier.ClosePunctuation   // cl-02 closing brackets
        + "\u2010\u2013\u2014"            // cl-03 hyphens and dashes
        + "\uFF01\uFF1F"                  // cl-04 dividing marks (question and exclamation)
        + "\u30FB\uFF1A\uFF1B"            // cl-05 middle dots
        + "\u3002\uFF0E."                 // cl-06 full stops
        + "\u3001\uFF0C,"                 // cl-07 commas
        + "\u3005\u303B"                  // cl-09 iteration marks
        + "\u30FC"                        // cl-10 prolonged sound mark
        + "\u3041\u3043\u3045\u3047\u3049\u3063\u3083\u3085\u3087\u308E" // cl-11 small hiragana
        + "\u30A1\u30A3\u30A5\u30A7\u30A9\u30C3\u30E3\u30E5\u30E7\u30EE\u30F5\u30F6" // small katakana
        + "\uFE48";                       // cl-29 warichu closing bracket

    /// <summary>jlreq's line-end prohibition: opening brackets (cl-01) and warichu opening brackets (cl-28).</summary>
    private const string JlreqLineEnd = CharClassifier.OpenPunctuation + "\uFE47";

    // ── Korean (klreq §7.1.2, §7.1.3) ──

    /// <summary>
    /// klreq's line-start prohibition: closing parentheses (cl2), hyphens (cl5), dividing marks (cl6), middle
    /// dots (cl7), commas and periods (cl8‑9), iteration marks (cl11) and the prolonged sound mark (cl12).
    /// </summary>
    private const string KlreqLineStart =
        CharClassifier.ClosePunctuation   // cl2 closing parentheses
        + "\u2010\u2013\u2014-"           // cl5 hyphens
        + "\uFF01\uFF1F"                  // cl6 dividing marks
        + "\u00B7\u30FB\uFF1A\uFF1B"      // cl7 middle dots
        + ",.\uFF0C\u3001\u3002\uFF0E"    // cl8-9 commas and periods
        + "\u3005\u303B"                  // cl11 iteration marks
        + "\u30FC";                       // cl12 prolonged sound mark

    private static readonly KinsokuClassSet LegacySet = new()
    {
        Id = LegacyId,
        Description = "the engine's historical set: closing marks, pause/stop marks, interpunct, solidus, dashes "
                      + "and ellipses at line start; opening marks at line end",
        LineStartBasic = CharClassifier.ClosePunctuation + CharClassifier.PauseStopPunctuation + "\u00B7\uFF0F\u2013\u2014\u2026\u2025",
        LineStartGbStyle = CharClassifier.ClosePunctuation + CharClassifier.PauseStopPunctuation
                           + "\u00B7\uFF0F\u2013\u2014\u2026\u2025\u2500\u2501\u2502\u2503",
        LineStartStrict = CharClassifier.ClosePunctuation + CharClassifier.PauseStopPunctuation
                          + "\u00B7\uFF0F\u2013\u2014\u2026\u2025\u2500\u2501\u2502\u2503",
        LineEndBasic = CharClassifier.OpenPunctuation,
        LineEndGbStyle = CharClassifier.OpenPunctuation,
        LineEndStrict = CharClassifier.OpenPunctuation,
    };

    private static readonly KinsokuClassSet ChineseSet = new()
    {
        Id = ChineseId,
        Description = "clreq §6.1.1: pause/stop marks, closing marks, connector marks, interpunct and solidus "
                      + "at line start; opening marks at line end. GB-style adds the solidus at line end, strict "
                      + "adds dashes and ellipses at line start.",
        LineStartBasic = ClreqBasicLineStart,
        LineStartGbStyle = ClreqBasicLineStart,
        LineStartStrict = ClreqStrictLineStart,
        LineEndBasic = CharClassifier.OpenPunctuation,
        LineEndGbStyle = CharClassifier.OpenPunctuation + "\uFF0F",
        LineEndStrict = CharClassifier.OpenPunctuation + "\uFF0F",
    };

    private static readonly KinsokuClassSet JapaneseSet = new()
    {
        Id = JapaneseId,
        Description = "jlreq §3.1.7/§C: closing brackets, hyphens, dividing marks, middle dots, stops, commas, "
                      + "iteration marks, prolonged sound marks and small kana at line start; opening brackets at "
                      + "line end (line-edge rows only)",
        LineStartBasic = JlreqLineStart,
        LineStartGbStyle = JlreqLineStart,
        LineStartStrict = JlreqLineStart,
        LineEndBasic = JlreqLineEnd,
        LineEndGbStyle = JlreqLineEnd,
        LineEndStrict = JlreqLineEnd,
    };

    private static readonly KinsokuClassSet KoreanSet = new()
    {
        Id = KoreanId,
        Description = "klreq §7.1.2/§7.1.3: closing parentheses, hyphens, dividing marks, middle dots, commas, "
                      + "periods, iteration marks and the prolonged sound mark at line start; opening parentheses "
                      + "at line end",
        LineStartBasic = KlreqLineStart,
        LineStartGbStyle = KlreqLineStart,
        LineStartStrict = KlreqLineStart,
        LineEndBasic = CharClassifier.OpenPunctuation,
        LineEndGbStyle = CharClassifier.OpenPunctuation,
        LineEndStrict = CharClassifier.OpenPunctuation,
    };

    private static readonly Dictionary<string, KinsokuClassSet> Sets = new(StringComparer.OrdinalIgnoreCase)
    {
        [LegacySet.Id] = LegacySet,
        [ChineseSet.Id] = ChineseSet,
        [JapaneseSet.Id] = JapaneseSet,
        [KoreanSet.Id] = KoreanSet,
    };

    /// <summary>The set a profile that names none gets: the engine's historical behaviour.</summary>
    public static KinsokuClassSet Legacy => LegacySet;

    /// <summary>
    /// Look a set up by the identifier a profile names.
    /// </summary>
    /// <param name="id">Identifier such as <c>clreq</c>.</param>
    /// <returns>The set, or null when nothing is registered under that id.</returns>
    public static KinsokuClassSet? Find(string? id) =>
        string.IsNullOrEmpty(id) ? LegacySet : Sets.GetValueOrDefault(id);

    /// <summary>
    /// Register a set, replacing any set with the same id. This is how a host adds a convention the engine has
    /// never heard of, without touching the framework.
    /// </summary>
    /// <param name="set">The set to register.</param>
    /// <exception cref="ArgumentNullException">When <paramref name="set"/> is null.</exception>
    public static void Register(KinsokuClassSet set)
    {
        ArgumentNullException.ThrowIfNull(set);
        Sets[set.Id] = set;
    }

    /// <summary>Identifiers of the registered sets, for diagnostics.</summary>
    public static IReadOnlyCollection<string> RegisteredIds => Sets.Keys;
}
