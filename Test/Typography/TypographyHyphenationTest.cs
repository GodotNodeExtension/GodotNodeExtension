namespace GodotNodeExtension.Tests.Typography;

using System.Collections.Generic;
using GdUnit4;
using GodotNodeExtension.Component.Typography.Core.Hyphenation;
using static GdUnit4.Assertions;

/// <summary>
/// Conformance for automatic hyphenation: where Liang's algorithm, driven by the TeX patterns hyph-utf8 ships, says a
/// word may break.
/// <para>
/// The pattern files carry their own examples — the exception lists are the classic ones (`as-so-ciate`,
/// `ta-ble`, …) — so the cases below use those where they exist and the patterns themselves everywhere else. The
/// point is not that our implementation "looks right" but that it agrees with the data: an odd score means allowed,
/// an even one forbidden, and the left/right minima keep fragments like "a-" from being produced.
/// </para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class TypographyHyphenationTest
{
    /// <summary>The exception list of the American English patterns is reproduced exactly.</summary>
    [TestCase]
    public void TheExceptionsOfThePatternFileAreHonoured()
    {
        HyphenationPatternSet set = Set(HyphenationPatternSets.EnUsId);

        AssertThat(Hyphenator.Hyphenate("associate", set)).IsEqual("as-so-ciate");
        AssertThat(Hyphenator.Hyphenate("associates", set)).IsEqual("as-so-ciates");
        AssertThat(Hyphenator.Hyphenate("declination", set)).IsEqual("dec-li-na-tion");
        AssertThat(Hyphenator.Hyphenate("obligatory", set)).IsEqual("oblig-a-tory");
        AssertThat(Hyphenator.Hyphenate("philanthropic", set)).IsEqual("phil-an-thropic");
        AssertThat(Hyphenator.Hyphenate("reciprocity", set)).IsEqual("reci-procity");
        AssertThat(Hyphenator.Hyphenate("recognizance", set)).IsEqual("re-cog-ni-zance");
        AssertThat(Hyphenator.Hyphenate("retribution", set)).IsEqual("ret-ri-bu-tion");
        AssertThat(Hyphenator.Hyphenate("table", set)).IsEqual("ta-ble");
    }

    /// <summary>
    /// A word the exception list states without a hyphen is never hyphenated: the language refuses it, and that is a
    /// different answer from "not listed" (which falls back to the patterns).
    /// </summary>
    [TestCase]
    public void AWordTheExceptionsRefuseIsNeverHyphenated()
    {
        HyphenationPatternSet set = Set(HyphenationPatternSets.EnUsId);

        AssertThat(Hyphenator.Hyphenate("present", set)).IsEqual("present");
        AssertThat(Hyphenator.Hyphenate("project", set)).IsEqual("project");
    }

    /// <summary>
    /// Words that are not exceptions come from the patterns, and they land where TeX puts them: the examples are the
    /// ones Liang's paper and the TeXbook use.
    /// </summary>
    [TestCase]
    public void ThePatternsHyphenateTheClassicExamples()
    {
        HyphenationPatternSet set = Set(HyphenationPatternSets.EnUsId);

        AssertThat(Hyphenator.Hyphenate("typography", set)).IsEqual("ty-pog-ra-phy");
        AssertThat(Hyphenator.Hyphenate("hyphenation", set)).IsEqual("hy-phen-ation");
        AssertThat(Hyphenator.Hyphenate("computer", set)).IsEqual("com-puter");
        AssertThat(Hyphenator.Hyphenate("democracy", set)).IsEqual("democ-racy");
        AssertThat(Hyphenator.Hyphenate("automatic", set)).IsEqual("au-to-matic");
    }

    /// <summary>
    /// Short words and short word parts are left alone: the minima are part of the data, not a constant.
    /// </summary>
    [TestCase]
    public void ShortWordsAndFragmentsAreLeftAlone()
    {
        HyphenationPatternSet set = Set(HyphenationPatternSets.EnUsId);

        foreach (string word in new[] { "a", "an", "the", "and", "it", "of", "cat" })
            AssertThat(Hyphenator.Hyphenate(word, set)).OverrideFailureMessage(word).IsEqual(word);

        // Every break leaves at least LeftMin characters before it and RightMin after it.
        foreach (string word in new[] { "typography", "hyphenation", "associate", "declination" })
        {
            var positions = new List<int>();
            Hyphenator.Opportunities(word, set, positions);

            foreach (int position in positions)
            {
                AssertThat(position >= set.LeftMin).OverrideFailureMessage($"{word} at {position}").IsTrue();
                AssertThat(word.Length - position >= set.RightMin).IsTrue();
            }
        }
    }

    /// <summary>
    /// Another language hyphenates by its own patterns: the German table breaks a German word and leaves an English
    /// one mostly alone, which is what "the language owns the patterns" means.
    /// </summary>
    [TestCase]
    public void EachLanguageUsesItsOwnPatterns()
    {
        HyphenationPatternSet german = Set(HyphenationPatternSets.GermanId);
        HyphenationPatternSet french = Set(HyphenationPatternSets.FrenchId);

        AssertThat(Hyphenator.Hyphenate("Silbentrennung", german)).Contains("-");
        AssertThat(Hyphenator.Hyphenate("bibliothèque", french)).Contains("-");

        // A word shorter than the minima stays whole in either language.
        AssertThat(Hyphenator.Hyphenate("xy", german)).IsEqual("xy");
        AssertThat(Hyphenator.Hyphenate("xy", french)).IsEqual("xy");
    }

    /// <summary>
    /// Every generated table says where it came from: the source file, its version and SHA-256, and the licence the
    /// pattern file states (hyph-utf8 collects them under different terms, so a table that cannot name its licence
    /// cannot be shipped).
    /// </summary>
    [TestCase]
    public void TheGeneratedTablesRecordTheirSource()
    {
        foreach (string id in HyphenationPatternSets.RegisteredIds)
        {
            HyphenationPatternSet set = Set(id);

            AssertThat(set.Source).OverrideFailureMessage(id).Contains("hyph-");
            AssertThat(set.Sha256.Length).OverrideFailureMessage(id).IsEqual(64);
            AssertThat(set.Licence.Length > 20).OverrideFailureMessage($"{id}: no licence recorded").IsTrue();
            AssertThat(set.PatternLines.Length > 0).OverrideFailureMessage($"{id}: no patterns").IsTrue();
            AssertThat(set.LeftMin >= 1 && set.RightMin >= 1).IsTrue();
        }
    }

    /// <summary>The trie is built once per set and the answers stay the same across calls.</summary>
    [TestCase]
    public void TheSameWordAlwaysBreaksAtTheSamePlaces()
    {
        HyphenationPatternSet set = Set(HyphenationPatternSets.EnUsId);

        string first = Hyphenator.Hyphenate("typography", set);

        for (int i = 0; i < 3; i++)
            AssertThat(Hyphenator.Hyphenate("typography", set)).IsEqual(first);
    }

    private static HyphenationPatternSet Set(string id)
    {
        HyphenationPatternSet? set = HyphenationPatternSets.Find(id);
        AssertThat(set).OverrideFailureMessage($"no hyphenation patterns registered for {id}").IsNotNull();
        return set!;
    }
}
