using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using GodotNodeExtension.Component.Typography.Core.Model;

namespace GodotNodeExtension.Component.Typography.Languages;

/// <summary>
/// Resolves a BCP-47 language tag to a <see cref="LanguageProfile"/>, and merges that profile with the
/// explicit overrides of a request.
/// <para>
/// Matching walks from the most specific tag to the least: <c>zh-Hant-HK</c> → <c>zh-Hant</c> →
/// <c>zh</c> → the profile's own fallback chain → <see cref="Und"/>. An unregistered language never
/// silently becomes Chinese; it lands on <see cref="Und"/>, which is the explicit "no language
/// assumption" path.
/// </para>
/// </summary>
public sealed class LanguageProfileRegistry
{
    private readonly ConcurrentDictionary<string, LanguageProfile> _profiles =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Process-wide registry with the built-in profiles.</summary>
    public static LanguageProfileRegistry Shared { get; } = CreateDefault();

    /// <summary>The profile used when a document declares no language or an unknown one.</summary>
    public LanguageProfile Und { get; }

    private LanguageProfileRegistry(LanguageProfile und)
    {
        Und = und;
        Register(und);
    }

    /// <summary>
    /// Create the registry with the built-in profiles the engine ships with.
    /// </summary>
    /// <returns>A registry containing the built-in profiles.</returns>
    public static LanguageProfileRegistry CreateDefault()
    {
        var registry = new LanguageProfileRegistry(BuiltInProfiles.NoLanguageAssumption);

        registry.Register(BuiltInProfiles.English);
        registry.Register(BuiltInProfiles.EnglishBritish);
        registry.Register(BuiltInProfiles.German);
        registry.Register(BuiltInProfiles.French);
        registry.Register(BuiltInProfiles.ChineseSimplified);
        registry.Register(BuiltInProfiles.ChineseTraditional);
        registry.Register(BuiltInProfiles.GenericChinese);
        registry.Register(BuiltInProfiles.Japanese);
        registry.Register(BuiltInProfiles.Korean);
        registry.Register(BuiltInProfiles.Arabic);
        registry.Register(BuiltInProfiles.Hebrew);

        return registry;
    }

    /// <summary>
    /// Register a profile, replacing any profile with the same id. Used by the built-ins, and by hosts
    /// that want to override a language without touching the framework.
    /// </summary>
    /// <param name="profile">The profile to register.</param>
    /// <exception cref="ArgumentNullException">When <paramref name="profile"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// When the profile names a feature the registry does not have, or a combination that conflicts. A profile
    /// is checked when it is registered rather than when it is used, so a broken language pack fails loudly
    /// instead of quietly laying text out with a fallback rule.
    /// </exception>
    public void Register(LanguageProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        IReadOnlyList<string> problems =
            Core.TypographyFeatureRegistry.Shared.Validate([profile.Parameters.BoundaryRuleFeature]);

        // A class set the engine cannot find would silently mean "no prohibition rules", which is the opposite
        // of what a language pack that names one is asking for.
        if (KinsokuClassSets.Find(profile.Parameters.ProhibitionClassSetId) is null)
        {
            problems = [.. problems,
                $"prohibition class set '{profile.Parameters.ProhibitionClassSetId}' is not registered "
                + $"(known: {string.Join(", ", KinsokuClassSets.RegisteredIds)})"];
        }

        // Vertical writing has contracts but no implementation, so a profile that asks for it is a bug in the
        // language pack rather than something to lay out horizontally while pretending otherwise.
        if (profile.Parameters.WritingMode is not (WritingMode.HorizontalTb or WritingMode.VerticalRl))
        {
            problems = [.. problems,
                $"writing mode '{profile.Parameters.WritingMode}' is not implemented yet "
                + $"(only '{WritingMode.VerticalRl}' joins '{WritingMode.HorizontalTb}')"];
        }

        // A band reserved above the line and an annotation set down a column are two different pieces of
        // geometry: the band is as deep as the annotation is tall, while a column runs along the base character
        // and needs room beside it. A profile that asks for both describes a layout that cannot exist, so it is
        // refused where the other conflicts are.
        if (profile.Parameters.RubyPlacement == RubyPlacement.ReserveAbove
            && profile.Parameters.RubyOrientation == RubyOrientation.Vertical)
        {
            problems = [.. problems,
                $"ruby placement '{profile.Parameters.RubyPlacement}' cannot carry a "
                + $"'{profile.Parameters.RubyOrientation}' annotation: the band above the line is not where a "
                + "column of annotation symbols is set (use ReserveBeside)"];
        }

        if (problems.Count > 0)
        {
            throw new ArgumentException(
                $"profile '{profile.Id}' cannot be registered: {string.Join("; ", problems)}",
                nameof(profile));
        }

        _profiles[profile.Id] = profile;
    }

    /// <summary>
    /// Resolve a language tag to a profile.
    /// </summary>
    /// <param name="languageTag">
    /// BCP-47 tag, or null/empty for "not specified" (resolves to <see cref="Und"/>).
    /// </param>
    /// <returns>The most specific registered profile, never null.</returns>
    public LanguageProfile Resolve(string? languageTag)
    {
        if (string.IsNullOrWhiteSpace(languageTag))
            return Und;

        string tag = Normalize(languageTag);

        // Most specific first, then progressively drop subtags.
        for (string candidate = tag; candidate.Length > 0; candidate = Parent(candidate))
        {
            if (_profiles.TryGetValue(candidate, out var profile))
                return profile;
        }

        // Then the fallback chain of the closest registered ancestor (if any).
        for (string candidate = tag; candidate.Length > 0; candidate = Parent(candidate))
        {
            if (!_profiles.TryGetValue(candidate, out var profile))
                continue;

            foreach (string fallbackTag in profile.Fallback)
            {
                if (_profiles.TryGetValue(Normalize(fallbackTag), out var fallback))
                    return fallback;
            }
        }

        return Und;
    }

    /// <summary>
    /// Merge a request's settings with its language profile into the effective parameters the pipeline
    /// stages read. An explicit value on the request always wins; everything else comes from the profile.
    /// </summary>
    /// <param name="settings">Request-level settings (geometry plus optional overrides).</param>
    /// <returns>The effective typography parameters for this request.</returns>
    /// <exception cref="ArgumentNullException">When <paramref name="settings"/> is null.</exception>
    public ResolvedTypography Resolve(TypographySettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return Resolve(settings, Resolve(settings.LanguageTag));
    }

    /// <summary>
    /// Merge a request's settings with the profile of one paragraph.
    /// <para>
    /// A paragraph that names its own language gets that language's defaults (its boundary rule set, its script
    /// spacing, the indent and spacing its convention prescribes) while the request's explicit overrides still
    /// win: the caller asked for those, and a paragraph's language does not silently undo them.
    /// </para>
    /// </summary>
    /// <param name="settings">Request-level settings (geometry plus optional overrides).</param>
    /// <param name="paragraph">The paragraph being resolved, or null for a paragraph without overrides.</param>
    /// <returns>The effective typography parameters for that paragraph.</returns>
    /// <exception cref="ArgumentNullException">When <paramref name="settings"/> is null.</exception>
    public ResolvedTypography Resolve(TypographySettings settings, ParagraphSettings? paragraph)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (paragraph?.LanguageTag is null or "")
            return Resolve(settings);

        return Resolve(settings, Resolve(paragraph.LanguageTag));
    }

    /// <summary>
    /// Merge a request's settings with a paragraph's language, named directly.
    /// <para>
    /// This is the form the compile phase uses: a segment knows the language tag of the paragraph it belongs
    /// to (<see cref="Core.Model.TextSegment.LanguageTag"/>) but not the settings object it came from, and
    /// shaping and display-form selection need the merge before any paragraph structure is resolved.
    /// </para>
    /// </summary>
    /// <param name="settings">Request-level settings (geometry plus optional overrides).</param>
    /// <param name="paragraphLanguageTag">Language of the paragraph, or null when it declares none.</param>
    /// <returns>The effective typography parameters for that paragraph.</returns>
    /// <exception cref="ArgumentNullException">When <paramref name="settings"/> is null.</exception>
    public ResolvedTypography Resolve(TypographySettings settings, string? paragraphLanguageTag)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (paragraphLanguageTag is null or "")
            return Resolve(settings);

        return Resolve(settings, Resolve(paragraphLanguageTag));
    }

    /// <summary>
    /// Merge a request's settings with one profile.
    /// </summary>
    /// <param name="settings">Request-level settings.</param>
    /// <param name="profile">Profile whose defaults fill in what the request leaves open.</param>
    /// <returns>The effective typography parameters.</returns>
    private static ResolvedTypography Resolve(TypographySettings settings, LanguageProfile profile)
    {
        var parameters = profile.Parameters;

        return new ResolvedTypography
        {
            Profile = profile,
            MaxWidth = settings.MaxWidth,
            MaxHeight = settings.MaxHeight,
            Padding = settings.Padding,
            WrapRegions = settings.WrapRegions,
            GridStep = settings.GridStep,
            GridOrigin = settings.GridOrigin,
            TabStops = settings.TabStops,
            DefaultTabStopEm = settings.DefaultTabStopEm,
            LineSpacing = settings.LineSpacing,
            ParagraphSpacing = settings.ParagraphSpacing,
            Alignment = settings.Alignment ?? parameters.Alignment,
            FirstLineIndent = settings.FirstLineIndent ?? parameters.FirstLineIndent,
            BoundaryRuleFeature = parameters.BoundaryRuleFeature,
            EnableLineProhibition = settings.EnableLineProhibition ?? parameters.EnableLineProhibition,
            ProhibitionLevel = settings.ProhibitionLevel ?? parameters.ProhibitionLevel,
            EnableCjkLatinSpacing = settings.EnableCjkLatinSpacing ?? parameters.EnableCjkLatinSpacing,
            CjkLatinSpacingEm = settings.CjkLatinSpacingEm ?? parameters.CjkLatinSpacingEm,
            EnablePunctuationCompression =
                settings.EnablePunctuationCompression ?? parameters.EnablePunctuationCompression,
            Direction = settings.Direction ?? parameters.Direction,
            OpenTypeLanguageTag = parameters.OpenTypeLanguageTag,
            EnableLetterformSubstitution =
                settings.EnableLetterformSubstitution ?? parameters.EnableLetterformSubstitution,
            Letterforms = parameters.Letterforms,
            ProhibitionClassSet = KinsokuClassSets.Find(parameters.ProhibitionClassSetId)
                                  ?? KinsokuClassSets.Legacy,
            CjkLatinSpacingMinEm = parameters.CjkLatinSpacingMinEm,
            CjkLatinSpacingMaxEm = parameters.CjkLatinSpacingMaxEm,
            LineEndPunctuation = parameters.LineEndPunctuation,
            WritingMode = ResolveWritingMode(settings.WritingMode, parameters.WritingMode),
            HangingPunctuation = parameters.HangingPunctuation,
            Hyphenation = Core.Hyphenation.HyphenationPatternSets.Find(parameters.HyphenationPatternsId),
            EnableHyphenation = settings.EnableHyphenation ?? true,
            HalfWidthOpeningBracketAtLineHead = parameters.HalfWidthOpeningBracketAtLineHead,
            RubyPlacement = parameters.RubyPlacement,
            RubyOrientation = parameters.RubyOrientation,
            EmphasisSide = parameters.EmphasisSide,
            EmphasisMarkSizeEm = parameters.EmphasisMarkSizeEm,
        };
    }

    /// <summary>
    /// The writing mode in effect for a request: its override, or what the language's profile says.
    /// <para>
    /// A vertical mode fails here instead of being laid out horizontally. The vocabulary for vertical layout (the
    /// inline/block axis abstraction) is in place but the stages still express geometry in Godot axes, so a
    /// vertical request would come back as a horizontal document that claims to be vertical - the same quiet
    /// wrongness this registry already refuses when a profile is registered.
    /// </para>
    /// </summary>
    /// <param name="requested">The request's override, or null to use the profile's mode.</param>
    /// <param name="fromProfile">The mode the language's profile declares.</param>
    /// <returns>The mode to lay out in.</returns>
    /// <exception cref="NotSupportedException">When a vertical mode is requested and cannot be honoured yet.</exception>
    private static WritingMode ResolveWritingMode(WritingMode? requested, WritingMode fromProfile)
    {
        WritingMode mode = requested ?? fromProfile;

        if (mode != WritingMode.HorizontalTb && mode != WritingMode.VerticalRl)
        {
            throw new NotSupportedException(
                $"writing mode '{mode}' is not implemented yet: only '{WritingMode.VerticalRl}' can be laid out "
                + "vertically today");
        }

        return mode;

    }

    /// <summary>
    /// Normalize a tag for lookup: lowercase, underscores to hyphens, no surrounding whitespace.
    /// </summary>
    /// <param name="languageTag">The tag to normalize.</param>
    /// <returns>A lookup-friendly tag.</returns>
    private static string Normalize(string languageTag) =>
        languageTag.Trim().Replace('_', '-').ToLowerInvariant();

    /// <summary>
    /// The next less specific form of a tag, or an empty string when there is none.
    /// </summary>
    /// <param name="tag">A normalized tag.</param>
    /// <returns>The parent tag, e.g. <c>zh-hant-hk</c> → <c>zh-hant</c>.</returns>
    private static string Parent(string tag)
    {
        int separator = tag.LastIndexOf('-');
        return separator <= 0 ? string.Empty : tag[..separator];
    }
}

/// <summary>
/// The profiles the engine ships with.
/// <para>
/// The CJK profiles now carry the values their conventions prescribe (clreq's two-character indent, jlreq's and
/// klreq's one-character indent, justifying alignment, the quarter-em CJK/Latin gap with its compressible range)
/// and their own prohibition class sets and display forms. Nothing here is a default for another language: a
/// document that declares no language keeps the engine's historical behaviour through
/// <see cref="NoLanguageAssumption"/>, and that is deliberately the only profile that does.
/// </para>
/// </summary>
internal static class BuiltInProfiles
{
    /// <summary>
    /// The historical CJK-friendly parameter set, used by the profile that declares no language.
    /// <para>
    /// These are the values the engine had before language profiles existed, kept for the one case where the
    /// caller has not said which language the text is: applying a convention to it would be inventing one.
    /// </para>
    /// </summary>
    private static TypographyParameters LegacyCjkFriendly() => new()
    {
        BoundaryRuleFeature = Core.TypographyFeatureRegistry.KinsokuBoundaryRuleId,
        EnableLineProhibition = true,
        ProhibitionLevel = ProhibitionLevel.Basic,
        EnableCjkLatinSpacing = true,
        CjkLatinSpacingEm = 0.25f,
        EnablePunctuationCompression = true,
        FirstLineIndent = 0,
        Alignment = TextAlignment.Left,
        Direction = TextDirection.LeftToRight,
    };

    /// <summary>
    /// Parameters shared by the CJK conventions: prohibition rules, punctuation compression, the quarter-em
    /// CJK/Latin gap and its compressible and stretchable bounds (clreq §6.3.3, jlreq §3.2.6 state the range as
    /// 1/8 to 1/2 em), justifying alignment and left-to-right direction.
    /// </summary>
    /// <param name="indent">First-line indent in ems (two characters for Chinese, one for Japanese and Korean).</param>
    /// <param name="classSetId">Identifier of the language's prohibition class set.</param>
    /// <param name="openTypeLanguageTag">OpenType language tag the language shapes with.</param>
    /// <param name="letterforms">Display forms the language prefers.</param>
    /// <param name="lineEndPunctuation">What the language does with punctuation that ends a line.</param>
    /// <param name="rubyPlacement">What the language does with the room an annotation needs.</param>
    /// <param name="emphasisSide">Side the language puts emphasis marks on.</param>
    /// <param name="halfWidthOpeningBracketAtLineHead">
    /// Whether an opening bracket at the head of a line may be trimmed to half width.
    /// </param>
    /// <param name="hangingPunctuation">Whether the language lets punctuation hang past the line edge.</param>
    /// <param name="rubyOrientation">
    /// Which way the language sets its annotations; horizontal unless the language writes them as a column.
    /// </param>
    /// <returns>The parameter set.</returns>
    private static TypographyParameters Cjk(
        int indent,
        string classSetId,
        string openTypeLanguageTag,
        IReadOnlyList<LetterformSubstitution> letterforms,
        LineEndPunctuationPolicy lineEndPunctuation,
        RubyPlacement rubyPlacement = RubyPlacement.ReserveAbove,
        EmphasisSide emphasisSide = EmphasisSide.Below,
        bool halfWidthOpeningBracketAtLineHead = false,
        HangingPunctuationPolicy hangingPunctuation = HangingPunctuationPolicy.None,
        RubyOrientation rubyOrientation = RubyOrientation.Horizontal) => new()
    {
        BoundaryRuleFeature = Core.TypographyFeatureRegistry.KinsokuBoundaryRuleId,
        EnableLineProhibition = true,
        ProhibitionLevel = ProhibitionLevel.Basic,
        EnableCjkLatinSpacing = true,
        CjkLatinSpacingEm = 0.25f,
        CjkLatinSpacingMinEm = 0.125f,
        CjkLatinSpacingMaxEm = 0.5f,
        EnablePunctuationCompression = true,
        FirstLineIndent = indent,
        Alignment = TextAlignment.Justify,
        Direction = TextDirection.LeftToRight,
        OpenTypeLanguageTag = openTypeLanguageTag,
        Letterforms = letterforms,
        ProhibitionClassSetId = classSetId,
        LineEndPunctuation = lineEndPunctuation,
        HalfWidthOpeningBracketAtLineHead = halfWidthOpeningBracketAtLineHead,
        HangingPunctuation = hangingPunctuation,
        RubyPlacement = rubyPlacement,
        RubyOrientation = rubyOrientation,
        EmphasisSide = emphasisSide,
        WritingMode = WritingMode.HorizontalTb,
    };

    /// <summary>
    /// The "no language assumption" profile. It carries the legacy CJK-friendly values because that is
    /// what the engine did for every document; it is a statement about history, not a claim that
    /// undeclared text is Chinese. It also names no OpenType language, so undeclared text shapes exactly as
    /// it always did.
    /// </summary>
    internal static readonly LanguageProfile NoLanguageAssumption = new(
        "und",
        [],
        LegacyCjkFriendly(),
        "no language declared; keeps the pre-profile behaviour");

    /// <summary>
    /// English: Western line breaking per UAX #14, and none of the CJK-only behaviours — no prohibition
    /// rules, no CJK/Latin gap, no punctuation squeezing, and a left-aligned paragraph with no indent.
    /// </summary>
    internal static readonly LanguageProfile English = new(
        "en",
        [],
        new TypographyParameters
        {
            BoundaryRuleFeature = Core.TypographyFeatureRegistry.UnicodeBoundaryRuleId,
            EnableLineProhibition = false,
            EnableCjkLatinSpacing = false,
            EnablePunctuationCompression = false,
            FirstLineIndent = 0,
            Alignment = TextAlignment.Left,
            Direction = TextDirection.LeftToRight,
            OpenTypeLanguageTag = "ENG",
            HyphenationPatternsId = Core.Hyphenation.HyphenationPatternSets.EnUsId,
        },
        "Western line breaking (UAX #14) and American English hyphenation");

    /// <summary>
    /// British English: the same shape as <c>en</c> with its own hyphenation patterns (the spelling differs from
    /// American English, and so do the places a word may break).
    /// </summary>
    internal static readonly LanguageProfile EnglishBritish = new(
        "en-GB",
        ["en"],
        new TypographyParameters
        {
            BoundaryRuleFeature = Core.TypographyFeatureRegistry.UnicodeBoundaryRuleId,
            EnableLineProhibition = false,
            EnableCjkLatinSpacing = false,
            EnablePunctuationCompression = false,
            FirstLineIndent = 0,
            Alignment = TextAlignment.Left,
            Direction = TextDirection.LeftToRight,
            OpenTypeLanguageTag = "ENG",
            HyphenationPatternsId = Core.Hyphenation.HyphenationPatternSets.EnGbId,
        },
        "British English: Western line breaking with British hyphenation patterns");

    /// <summary>
    /// German: Western line breaking with German hyphenation. The 1996 orthography's patterns are the ones the
    /// hyph-utf8 package carries as <c>de-1996</c>.
    /// </summary>
    internal static readonly LanguageProfile German = new(
        "de",
        ["de-DE"],
        new TypographyParameters
        {
            BoundaryRuleFeature = Core.TypographyFeatureRegistry.UnicodeBoundaryRuleId,
            EnableLineProhibition = false,
            EnableCjkLatinSpacing = false,
            EnablePunctuationCompression = false,
            FirstLineIndent = 0,
            Alignment = TextAlignment.Left,
            Direction = TextDirection.LeftToRight,
            OpenTypeLanguageTag = "DEU",
            HyphenationPatternsId = Core.Hyphenation.HyphenationPatternSets.GermanId,
        },
        "German: Western line breaking with German hyphenation patterns");

    /// <summary>French: Western line breaking with French hyphenation.</summary>
    internal static readonly LanguageProfile French = new(
        "fr",
        ["fr-FR"],
        new TypographyParameters
        {
            BoundaryRuleFeature = Core.TypographyFeatureRegistry.UnicodeBoundaryRuleId,
            EnableLineProhibition = false,
            EnableCjkLatinSpacing = false,
            EnablePunctuationCompression = false,
            FirstLineIndent = 0,
            Alignment = TextAlignment.Left,
            Direction = TextDirection.LeftToRight,
            OpenTypeLanguageTag = "FRA",
            HyphenationPatternsId = Core.Hyphenation.HyphenationPatternSets.FrenchId,
        },
        "French: Western line breaking with French hyphenation patterns");

    /// <summary>
    /// Simplified Chinese (clreq): two-character first-line indent, justifying alignment, the Chinese
    /// prohibition classes (whose basic level does not prohibit the ellipsis at line start — that is the strict
    /// level) and the Chinese line-end punctuation width.
    /// </summary>
    internal static readonly LanguageProfile ChineseSimplified = new(
        "zh-Hans",
        ["zh"],
        Cjk(2, KinsokuClassSets.ChineseId, "ZHS", [], LineEndPunctuationPolicy.HalfWidthOnOverflow,
            RubyPlacement.ReserveAbove, EmphasisSide.Below, true, HangingPunctuationPolicy.Allowed),
        "clreq: two-character indent, Chinese classes, Chinese punctuation width, hanging punctuation");

    /// <summary>
    /// Traditional Chinese: the simplified values with the traditional display forms (corner brackets and the
    /// centered ellipsis), and Bopomofo where the convention puts it - beside the base character, in a column of
    /// its own (clreq §5.5.3.1/§5.5.3.2). The annotation takes its room from the base advance rather than from
    /// the line's height, so the line is the same height with or without annotations, which is what §5.5.3.2's
    /// notes ask for.
    /// </summary>
    internal static readonly LanguageProfile ChineseTraditional = new(
        "zh-Hant",
        ["zh"],
        Cjk(2, KinsokuClassSets.ChineseId, "ZHT", LetterformSets.TraditionalChinese,
            LineEndPunctuationPolicy.HalfWidthOnOverflow,
            RubyPlacement.ReserveBeside, EmphasisSide.Below, true, HangingPunctuationPolicy.VerticalOnly,
            rubyOrientation: RubyOrientation.Vertical),
        "clreq: two-character indent, traditional display forms, Bopomofo beside the base text");

    /// <summary>
    /// The languages written right to left: their direction belongs to the language (a paragraph that says it is
    /// Arabic is telling the layout which way its text runs), and none of the CJK tailoring applies. What they do
    /// not yet have is bidi resolution for the Latin and digit runs they usually contain; that is the algorithm the
    /// levels and the visual-order assembly are for.
    /// </summary>
    /// <param name="openTypeLanguageTag">OpenType language tag the language shapes with.</param>
    /// <returns>The parameter set.</returns>
    private static TypographyParameters RightToLeft(
        string openTypeLanguageTag) => new()
    {
        BoundaryRuleFeature = Core.TypographyFeatureRegistry.UnicodeBoundaryRuleId,
        EnableLineProhibition = false,
        EnableCjkLatinSpacing = false,
        EnablePunctuationCompression = false,
        FirstLineIndent = 0,
        Alignment = TextAlignment.Left,
        Direction = TextDirection.RightToLeft,
        OpenTypeLanguageTag = openTypeLanguageTag,
        WritingMode = WritingMode.HorizontalTb,
    };

    /// <summary>Arabic: right-to-left, with the Unicode line breaking algorithm and no CJK tailoring.</summary>
    internal static readonly LanguageProfile Arabic = new(
        "ar",
        ["ar-EG"],
        RightToLeft("ARA"),
        "right-to-left, Unicode line breaking, no CJK tailoring (bidi resolution not implemented yet)");

    /// <summary>Hebrew: the same shape as Arabic, with its own OpenType language tag.</summary>
    internal static readonly LanguageProfile Hebrew = new(
        "he",
        ["he-IL"],
        RightToLeft("IWR"),
        "right-to-left, Unicode line breaking, no CJK tailoring (bidi resolution not implemented yet)");

    /// <summary>Generic Chinese, the fallback for any <c>zh-*</c> tag; currently the simplified parameter set.</summary>
    internal static readonly LanguageProfile GenericChinese = new(
        "zh",
        ["zh-Hans"],
        Cjk(2, KinsokuClassSets.ChineseId, "ZHS", [], LineEndPunctuationPolicy.HalfWidthOnOverflow),
        "generic Chinese; currently defers to the simplified parameter set");

    /// <summary>
    /// Japanese (jlreq): one-character indent, the Japanese prohibition classes (which stop lines at small kana
    /// and prolonged sound marks, and which Chinese does not) and the half em after a line-ending stop kept
    /// uncompressible (jlreq §3.1.9) instead of being trimmed the way Chinese trims it.
    /// </summary>
    internal static readonly LanguageProfile Japanese = new(
        "ja",
        ["ja-JP"],
        Cjk(1, KinsokuClassSets.JapaneseId, "JAN", LetterformSets.Japanese,
            LineEndPunctuationPolicy.PreserveHalfEm,
            RubyPlacement.OverflowBetweenLines, EmphasisSide.Above, true,
            HangingPunctuationPolicy.NotInMixedText),
        "jlreq: one-character indent, Japanese prohibition classes, half em after a stop preserved");

    /// <summary>
    /// Korean (klreq): one-character indent, the Korean prohibition classes, and the narrow sentence marks of
    /// horizontal writing as display forms (klreq §6.1.2).
    /// <para>
    /// The CJK/Latin gap is left at the quarter em the other CJK profiles use: klreq §7.3.2 states that mixing
    /// Hangul with Latin has its own spacing, but this engine has no value from the specification to encode
    /// yet, and a placeholder that says so is better than a citation that does not exist.
    /// </para>
    /// </summary>
    internal static readonly LanguageProfile Korean = new(
        "ko",
        ["ko-KR"],
        Cjk(1, KinsokuClassSets.KoreanId, "KOR", LetterformSets.Korean,
            LineEndPunctuationPolicy.None),
        "klreq: one-character indent, Korean prohibition classes, narrow sentence marks");
}
