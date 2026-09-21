namespace GodotNodeExtension.Tests.Typography;

using GdUnit4;
using GodotNodeExtension.Component.Typography.Core;
using GodotNodeExtension.Component.Typography.Core.Model;
using GodotNodeExtension.Component.Typography.Languages;
using static GdUnit4.Assertions;

/// <summary>
/// Specification for language resolution and for the merge between a language profile and a request.
/// <para>
/// Two properties are worth locking down here. First, an unregistered language must fall back to the
/// profile that makes no language assumption instead of silently becoming Chinese — a trap the
/// reference implementation falls into by defaulting its locale to <c>zh-Hans</c>. Second, adding the
/// profile layer must not have changed any output, which is what the "no language declared" and
/// "zh-Hans" parameter sets being identical currently asserts.
/// </para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class TypographyLanguageProfileTest
{
    private static readonly LanguageProfileRegistry Registry = LanguageProfileRegistry.Shared;

    /// <summary>Matching walks from the most specific tag to the least.</summary>
    [TestCase]
    public void TagsResolveFromMostSpecificToLeast()
    {
        AssertThat(Registry.Resolve("zh-Hant-HK").Id).IsEqual("zh-Hant");
        AssertThat(Registry.Resolve("zh-Hant").Id).IsEqual("zh-Hant");
        AssertThat(Registry.Resolve("zh-Hans").Id).IsEqual("zh-Hans");
        AssertThat(Registry.Resolve("zh-TW").Id).IsEqual("zh");
        AssertThat(Registry.Resolve("ZH_hant").Id).OverrideFailureMessage(
            "tags are matched case-insensitively and tolerate underscore separators").IsEqual("zh-Hant");
    }

    /// <summary>
    /// An unknown or missing language lands on the no-assumption profile; it never quietly becomes
    /// Chinese, because that would look like a working layout while applying the wrong conventions.
    /// </summary>
    [TestCase]
    public void UnknownLanguagesDoNotBecomeChinese()
    {
        // English is registered now, so it resolves to its own profile rather than to the fallback.
        AssertThat(Registry.Resolve("en").Id).IsEqual("en");

        // Everything else still falls back explicitly instead of silently inheriting CJK behaviour.
        AssertThat(Registry.Resolve("th-TH").Id).IsEqual("und");
        AssertThat(Registry.Resolve("xx").Id).IsEqual("und");
        AssertThat(Registry.Resolve("").Id).IsEqual("und");

        // Typed local: a bare null is ambiguous between the tag and the settings overload.
        string? noTag = null;
        AssertThat(Registry.Resolve(noTag).Id).IsEqual("und");
    }

    /// <summary>An explicit request value wins; a null means "whatever the language says".</summary>
    [TestCase]
    public void RequestOverridesWinOverTheProfile()
    {
        var fromProfile = Registry.Resolve(new TypographySettings { LanguageTag = "zh-Hans" });
        AssertThat(fromProfile.EnableLineProhibition).IsTrue();
        AssertThat(fromProfile.CjkLatinSpacingEm).IsEqual(0.25f);
        AssertThat(fromProfile.ProfileId).IsEqual("zh-Hans");

        var overridden = Registry.Resolve(new TypographySettings
        {
            LanguageTag = "zh-Hans",
            EnableLineProhibition = false,
            CjkLatinSpacingEm = 0.5f,
            FirstLineIndent = 3,
        });

        AssertThat(overridden.EnableLineProhibition).IsFalse();
        AssertThat(overridden.CjkLatinSpacingEm).IsEqual(0.5f);
        AssertThat(overridden.FirstLineIndent).IsEqual(3);
        AssertThat(overridden.EnablePunctuationCompression).OverrideFailureMessage(
            "values the request leaves alone still come from the profile").IsTrue();
    }

    /// <summary>
    /// Geometry is request-owned: a profile must never contribute a width, a padding or a wrap region.
    /// </summary>
    [TestCase]
    public void GeometryComesFromTheRequestOnly()
    {
        var settings = new TypographySettings { MaxWidth = 321f, Padding = 7f, LanguageTag = "zh-Hans" };
        settings.WrapRegions.Add(new WrapRegion { Shape = new RectWrapShape { Width = 40f, Height = 40f } });

        var resolved = Registry.Resolve(settings);

        AssertThat(resolved.MaxWidth).IsEqual(321f);
        AssertThat(resolved.Padding).IsEqual(7f);
        AssertThat(resolved.WrapRegions.Count).IsEqual(1);
    }

    /// <summary>
    /// A nested layout (an auto-size block) changes geometry and inherits language behaviour, so a new
    /// parameter can never be dropped the way <c>Padding</c> was by the hand-written copy it replaced.
    /// </summary>
    [TestCase]
    public void NestedLayoutsInheritLanguageBehaviour()
    {
        var parent = Registry.Resolve(new TypographySettings
        {
            MaxWidth = 400f,
            LanguageTag = "zh-Hant",
            EnableCjkLatinSpacing = false,
        });

        var nested = parent.ForNestedLayout(120f, parent.WrapRegions);

        AssertThat(nested.MaxWidth).IsEqual(120f);
        AssertThat(nested.ProfileId).IsEqual("zh-Hant");
        AssertThat(nested.EnableCjkLatinSpacing).OverrideFailureMessage(
            "the nested layout must inherit the language behaviour, including request overrides").IsFalse();
        AssertThat(nested.EnableLineProhibition).IsTrue();
    }

    /// <summary>
    /// The CJK profiles now carry the values their conventions prescribe, and each one differs from the others
    /// where the conventions differ: the indent is two characters in Chinese and one in Japanese and Korean, all
    /// four justify, and each names its own prohibition class set and its own shaping language.
    /// <para>
    /// What stays equal is the engine's <em>behaviour</em> for a document that declares no language — that is
    /// the only profile that still resolves to the historical values, and this is what keeps the refactor from
    /// silently changing undeclared text.
    /// </para>
    /// </summary>
    [TestCase]
    public void EachLanguageCarriesItsOwnConventions()
    {
        var legacy = Registry.Resolve(new TypographySettings());
        AssertThat(legacy.ProfileId).IsEqual("und");
        AssertThat(legacy.FirstLineIndent).IsEqual(0);
        AssertThat(legacy.Alignment).IsEqual(TextAlignment.Left);
        AssertThat(legacy.ProhibitionClassSet.Id).IsEqual(KinsokuClassSets.LegacyId);
        AssertThat((object?)legacy.OpenTypeLanguageTag).IsNull();
        AssertThat(legacy.Letterforms.Count).IsEqual(0);

        var simplified = Registry.Resolve(new TypographySettings { LanguageTag = "zh-Hans" });
        AssertThat(simplified.FirstLineIndent).IsEqual(2);
        AssertThat(simplified.Alignment).IsEqual(TextAlignment.Justify);
        AssertThat(simplified.ProhibitionClassSet.Id).IsEqual(KinsokuClassSets.ChineseId);
        AssertThat(simplified.OpenTypeLanguageTag).IsEqual("ZHS");
        AssertThat(simplified.LineEndPunctuation).IsEqual(LineEndPunctuationPolicy.HalfWidthOnOverflow);
        AssertThat(simplified.CjkLatinSpacingMinEm).IsEqual(0.125f);
        AssertThat(simplified.CjkLatinSpacingMaxEm).IsEqual(0.5f);

        // Traditional Chinese shares the Chinese classes but not the display forms, and it does not hang
        // punctuation in horizontal writing.
        var traditional = Registry.Resolve(new TypographySettings { LanguageTag = "zh-Hant" });
        AssertThat(traditional.ProhibitionClassSet.Id).IsEqual(KinsokuClassSets.ChineseId);
        AssertThat(traditional.OpenTypeLanguageTag).IsEqual("ZHT");
        AssertThat(traditional.Letterforms.Count > 0).IsTrue();

        var japanese = Registry.Resolve(new TypographySettings { LanguageTag = "ja" });
        AssertThat(japanese.ProfileId).IsEqual("ja");
        AssertThat(japanese.FirstLineIndent).IsEqual(1);
        AssertThat(japanese.Alignment).IsEqual(TextAlignment.Justify);
        AssertThat(japanese.ProhibitionClassSet.Id).IsEqual(KinsokuClassSets.JapaneseId);
        AssertThat(japanese.OpenTypeLanguageTag).IsEqual("JAN");
        AssertThat(japanese.LineEndPunctuation).IsEqual(LineEndPunctuationPolicy.PreserveHalfEm);

        var korean = Registry.Resolve(new TypographySettings { LanguageTag = "ko" });
        AssertThat(korean.ProfileId).IsEqual("ko");
        AssertThat(korean.FirstLineIndent).IsEqual(1);
        AssertThat(korean.ProhibitionClassSet.Id).IsEqual(KinsokuClassSets.KoreanId);
        AssertThat(korean.OpenTypeLanguageTag).IsEqual("KOR");
        AssertThat(korean.Letterforms.Count > 0).IsTrue();
    }

    /// <summary>
    /// English differs from the CJK profiles in exactly the places the language owns: it breaks lines by the
    /// Unicode algorithm, and it switches off the CJK-only behaviours instead of inheriting them.
    /// </summary>
    [TestCase]
    public void EnglishProfileUsesUnicodeLineBreakingWithoutCjkTailoring()
    {
        var english = Registry.Resolve(new TypographySettings { LanguageTag = "en" });
        AssertThat(english.ProfileId).IsEqual("en");
        AssertThat(english.BoundaryRuleFeature).IsEqual(TypographyFeatureRegistry.UnicodeBoundaryRuleId);
        AssertThat(english.EnableLineProhibition).IsFalse();
        AssertThat(english.EnableCjkLatinSpacing).IsFalse();
        AssertThat(english.EnablePunctuationCompression).IsFalse();
        AssertThat(english.FirstLineIndent).IsEqual(0);
        AssertThat(english.Alignment).IsEqual(TextAlignment.Left);

        // Chinese tailors line breaking with its own prohibition rules instead.
        var chinese = Registry.Resolve(new TypographySettings { LanguageTag = "zh-Hans" });
        AssertThat(chinese.BoundaryRuleFeature).IsEqual(TypographyFeatureRegistry.KinsokuBoundaryRuleId);
        AssertThat(chinese.EnableLineProhibition).IsTrue();
        AssertThat(chinese.EnableCjkLatinSpacing).IsTrue();

        // A request may still override the policy-owned behaviours it cares about.
        var overridden = Registry.Resolve(new TypographySettings
        {
            LanguageTag = "en",
            FirstLineIndent = 2,
            EnableCjkLatinSpacing = true,
        });

        AssertThat(overridden.FirstLineIndent).IsEqual(2);
        AssertThat(overridden.EnableCjkLatinSpacing).IsTrue();
    }

    /// <summary>
    /// The OpenType language a profile shapes with. It is what makes a font's localized forms reachable — the
    /// same code point is drawn differently in Chinese, Japanese and Korean text — so it has to be an identity
    /// per language rather than a shared value, and <c>und</c> has to ask for nothing: undeclared text must
    /// keep shaping exactly as it did before this existed.
    /// </summary>
    [TestCase]
    public void ProfilesNameTheOpenTypeLanguageTheyShapeWith()
    {
        AssertThat((object?)Registry.Resolve(new TypographySettings()).OpenTypeLanguageTag).IsNull();
        AssertThat(Registry.Resolve(new TypographySettings { LanguageTag = "en" }).OpenTypeLanguageTag)
            .IsEqual("ENG");
        AssertThat(Registry.Resolve(new TypographySettings { LanguageTag = "zh-Hans" }).OpenTypeLanguageTag)
            .IsEqual("ZHS");
        AssertThat(Registry.Resolve(new TypographySettings { LanguageTag = "zh-Hant" }).OpenTypeLanguageTag)
            .IsEqual("ZHT");
    }

    /// <summary>A script pack can be replaced by a host without touching the framework.</summary>
    [TestCase]
    public void HostsCanReplaceAProfile()
    {        var registry = LanguageProfileRegistry.CreateDefault();
        registry.Register(new LanguageProfile(
            "test-Hant",
            ["zh-Hant"],
            new TypographyParameters { EnableLineProhibition = false, FirstLineIndent = 8 },
            "test profile"));

        var resolved = registry.Resolve(new TypographySettings { LanguageTag = "test-Hant" });

        AssertThat(resolved.ProfileId).IsEqual("test-Hant");
        AssertThat(resolved.EnableLineProhibition).IsFalse();
        AssertThat(resolved.FirstLineIndent).IsEqual(8);
    }
}
