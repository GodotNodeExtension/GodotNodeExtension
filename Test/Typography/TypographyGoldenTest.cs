namespace GodotNodeExtension.Tests.Typography;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using GdUnit4;
using Godot;
using GodotNodeExtension.Component.RichTextCanvas;
using GodotNodeExtension.Component.Typography.Core;
using GodotNodeExtension.Component.Typography.Core.Model;
using GodotNodeExtension.Component.Typography.Languages;
using static GdUnit4.Assertions;

/// <summary>
/// Golden-dump regression suite for the typography pipeline, plus the output invariants that the
/// P0 contract freezes.
/// <para>
/// Why dumps instead of assertions: a typography engine is a decision engine, and the only practical
/// way to keep a refactor from silently changing line breaking, spacing or punctuation handling is to
/// diff the whole layout output. The dump records every line and every laid-out element with the
/// geometry, provenance and reason fields the P0 contract added.
/// </para>
/// <para>
/// Baseline policy: when a golden file is missing the case records it and passes, printing the path;
/// the value of the run is in the <em>second</em> run, which compares. Baselines depend on the font
/// the engine measures with (<see cref="ThemeDB.FallbackFont"/> plus system fallback for CJK), so a
/// Godot upgrade or a different font set legitimately changes them — re-record and review the diff
/// instead of assuming a regression.
/// </para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class TypographyGoldenTest
{
    private const string GoldenDir = "res://Test/Typography/golden";

    /// <summary>
    /// A layout input: the element stream plus the settings it is laid out with. <paramref name="Note"/> is
    /// written into the file's header when a scenario needs a word about what its dump is supposed to show; a
    /// scenario without one produces the same file it always did.
    /// </summary>
    private sealed record Scenario(DrawElement[] Elements, TypographySettings Options, string? Note = null);

    /// <summary>Laid-out result: lines, flattened elements and the reported content size.</summary>
    private sealed record Layout(
        List<LayoutLine> Lines,
        List<LayoutElement> Elements,
        Vector2 ContentSize,
        IReadOnlyList<Boundary> Boundaries);

    // ── Golden cases ──

    [TestCase]
    public void ChineseParagraphMatchesGolden() => AssertGolden("chinese-paragraph", ChineseParagraph());

    [TestCase]
    public void CjkLatinMixedMatchesGolden() => AssertGolden("cjk-latin-mixed", CjkLatinMixed());

    /// <summary>Vertical writing lays the same conventions out as columns.</summary>
    [TestCase]
    public void VerticalJapaneseMatchesGolden() => AssertGolden("vertical-rl-japanese", VerticalJapanese());

    /// <summary>The annotated samples the demo shows, so their geometry can be read rather than guessed at.</summary>
    [TestCase]
    public void AnnotatedSamplesMatchGolden() => AssertGolden("annotated-samples", AnnotatedSamples());

    /// <summary>Vertical writing with a Bopomofo annotation beside its base character.</summary>
    [TestCase]
    public void VerticalChineseBopomofoMatchesGolden() =>
        AssertGolden("vertical-zh-hant-bopomofo", VerticalChineseBopomofo());

    [TestCase]
    public void PunctuationStressMatchesGolden() => AssertGolden("punctuation-stress", PunctuationStress());

    [TestCase]
    public void MultiParagraphMatchesGolden() => AssertGolden("multi-paragraph", MultiParagraph());

    [TestCase]
    public void InlineImageMatchesGolden() => AssertGolden("inline-image", InlineImage());

    [TestCase]
    public void BackgroundPaddingMatchesGolden() => AssertGolden("background-padding", BackgroundPadding());

    [TestCase]
    public void AutoSizeBlockMatchesGolden() => AssertGolden("auto-size-block", AutoSizeBlock());

    [TestCase]
    public void PaddingAndParagraphIndentMatchesGolden() =>
        AssertGolden("padding-paragraph-indent", PaddingAndParagraphIndent());

    [TestCase]
    public void WrapRegionMatchesGolden() => AssertGolden("wrap-region", WrapRegionLeft());

    [TestCase]
    public void NumberUnitsMatchesGolden() => AssertGolden("number-units", NumberUnits());

    [TestCase]
    public void ParagraphLanguageSwitchMatchesGolden() =>
        AssertGolden("paragraph-language-switch", ParagraphLanguageSwitch());

    [TestCase]
    public void MixedQuotesMatchesGolden() => AssertGolden("mixed-quotes", MixedQuotes());

    [TestCase]
    public void LetterformsMatchesGolden() => AssertGolden("letterforms-zh-hant", Letterforms());

    [TestCase]
    public void EnglishHyphenationMatchesGolden() =>
        AssertGolden("english-hyphenation", EnglishHyphenation());

    [TestCase]
    public void KoreanDisplayFormsMatchesGolden() =>
        AssertGolden("letterforms-ko", KoreanDisplayForms());

    [TestCase]
    public void MonoRubyMatchesGolden() => AssertGolden("ruby-mono", MonoRuby());

    [TestCase]
    public void GroupRubyMatchesGolden() => AssertGolden("ruby-group", GroupRuby());

    [TestCase]
    public void EmphasisMarksMatchesGolden() => AssertGolden("emphasis-marks", EmphasisMarks());

    [TestCase]
    public void ZhuyinRubyMatchesGolden() => AssertGolden("ruby-zhuyin-zh-hant", ZhuyinRuby());

    [TestCase]
    public void ZhuyinToneMarksMatchGolden() =>
        AssertGolden("ruby-zhuyin-tones-zh-hant", ZhuyinToneMarks());

    [TestCase]
    public void WrapMiddleBandMatchesGolden() =>
        AssertGolden("wrap-middle-band", WrapMiddleBand());

    [TestCase]
    public void EnglishParagraphMatchesGolden() =>
        AssertGolden("english-paragraph", EnglishParagraph());

    [TestCase]
    public void EnglishHyphenatedMatchesGolden() =>
        AssertGolden("english-hyphenated", EnglishHyphenated());

    /// <summary>
    /// The baseline the layout publishes must be the one the renderer used to derive for itself from the
    /// font metrics. If the two ever diverge, every text element silently shifts by an ascent — which is
    /// the exact failure the baseline field was introduced to prevent, and the reason "the render result
    /// is unchanged" is asserted here instead of being assumed.
    /// </summary>
    [TestCase]
    public void PublishedBaselineMatchesTheOneTheRendererDerives()
    {
        foreach (var (name, scenario) in AllScenarios())
        {
            var layout = Run(scenario);
            foreach (var element in layout.Elements)
            {
                if (element.Type != DrawElement.ElementType.Text || element.Font == null)
                    continue;

                // NaN means the layout never computed one (block content, block markers); the renderer
                // falls back to the metrics in exactly the way it did before the field existed.
                if (float.IsNaN(element.BaselineY))
                    continue;

                // The baseline sits one ascent along the block axis from the box' block-start edge, in either
                // writing mode: in horizontal writing that edge is Y, in vertical writing it is the content box'
                // right edge, which is why the same relation is expressed through the axes.
                LayoutAxes axes = new(scenario.Options.WritingMode ?? WritingMode.HorizontalTb,
                    scenario.Options.MaxWidth);
                float rendererBaseline = axes.Block(element.Position) +
                                         SkiaTextMeasurer.GetAscent(element.Font, element.FontSize);

                AssertThat(Math.Abs(element.BaselineY - rendererBaseline) < 0.01f)
                    .OverrideFailureMessage(
                        $"{name}: layout baseline {element.BaselineY:F3} != renderer baseline " +
                        $"{rendererBaseline:F3} for \"{element.Text}\"")
                    .IsTrue();
            }
        }
    }

    // ── Invariants ──

    /// <summary>
    /// Reasons a laid-out text element carries while still being the document's own text: the characters are the
    /// source characters at its index, so the invariants have to look at them. Every other reason marks something
    /// the assembly stage synthesised - a hyphen, a block's own sub-layout, a merged background - whose text
    /// belongs to another context.
    /// <para>
    /// This distinction is load bearing: while the invariants skipped *any* element with a reason, marking a
    /// hanging mark with <c>Reason=HangingPunctuation</c> made the coverage check report its character as lost,
    /// which read exactly like a layout that had dropped text.
    /// </para>
    /// </summary>
    private static readonly HashSet<string> SourceTextReasons = ["HangingPunctuation", "OpeningBracketHalfWidth"];

    /// <summary>Whether the assembly stage created this element rather than the line breaker.</summary>
    /// <param name="element">Element to classify.</param>
    /// <returns>True when its text belongs to another context.</returns>
    private static bool IsSynthesised(LayoutElement element) =>
        element.Reason is { } reason && !SourceTextReasons.Contains(reason);

    /// <summary>
    /// Freezes the invariants the P0 output contract promises: a baseline exists for everything the
    /// line breaker produced, provenance indices are monotonic and inside the document, and the
    /// reported line index is real. These hold regardless of the font, so unlike the goldens they do
    /// not need re-recording when the font changes.
    /// </summary>
    [TestCase]
    public void OutputInvariantsHold()
    {
        foreach (var (name, scenario) in AllScenarios())
        {
            var layout = Run(scenario);

            // Regression guard for the duplicated-character defect: a line holds a *subsequence* of the
            // segments, so the same cluster index must never reach the output twice. Before the rollback
            // fix, six ranges in the punctuation sample appeared on two adjacent lines.
            var seenClusters = new HashSet<int>();

            foreach (var element in layout.Elements)
            {
                // Only elements the document's line breaker produced are checked here. Everything the
                // assembly stage synthesised carries a reason and comes with different guarantees:
                // decorations have no source text, block markers are never measured by the breaker,
                // and content expanded from a block's own sub-layout keeps that context's line and
                // cluster indices (the P0 limitation documented on LayoutElement.LineIndex).
                if (element.Type != DrawElement.ElementType.Text) continue;
                if (IsSynthesised(element)) continue;

                AssertThat(seenClusters.Add(element.ClusterStart)).OverrideFailureMessage(
                    $"{name}: cluster {element.ClusterStart} (\"{element.Text}\") was emitted twice").IsTrue();
                AssertThat(float.IsNaN(element.BaselineY)).OverrideFailureMessage(
                    $"{name}: text element on line {element.LineIndex} has no baseline").IsFalse();
                LayoutAxes axes = new(scenario.Options.WritingMode ?? WritingMode.HorizontalTb,
                    scenario.Options.MaxWidth);
                float boxStart = axes.Block(element.Position);
                float boxExtent = axes.BlockExtent(element.Size);

                AssertThat(element.BaselineY >= boxStart - 0.01f).OverrideFailureMessage(
                    $"{name}: baseline {element.BaselineY} above the box' block-start edge {boxStart}").IsTrue();
                AssertThat(element.BaselineY <= boxStart + boxExtent + 0.01f)
                    .OverrideFailureMessage(
                        $"{name}: baseline {element.BaselineY} below box bottom " +
                        $"{element.Position.Y + element.Size.Y}").IsTrue();
                AssertThat(element.LineIndex >= 0 && element.LineIndex < layout.Lines.Count)
                    .OverrideFailureMessage($"{name}: element line index {element.LineIndex} out of range").IsTrue();
                AssertThat(element.SourceRange.Length).OverrideFailureMessage(
                        $"{name}: source range {element.SourceRange} does not cover \"{element.Text}\"")
                    .IsEqual(element.Text!.Length);
                AssertThat(element.ClusterStart >= 0).OverrideFailureMessage(
                    $"{name}: negative cluster index").IsTrue();
                AssertThat(element.ClusterEnd).IsEqual(element.ClusterStart + 1);
            }
        }
    }

    /// <summary>
    /// Every source character must reach the output exactly once. The cluster guard in
    /// <see cref="OutputInvariantsHold"/> proves nothing is duplicated; this proves nothing is lost.
    /// <para>
    /// One deliberate exception: a line drops the spaces it would otherwise start with, which is normal
    /// typographic behaviour but does mean a space can disappear from the element stream (and therefore
    /// from a copy/selection). Both sides are compared with whitespace removed, so that exception is
    /// stated here instead of being hidden. Elements expanded from a block's own sub-layout are skipped:
    /// they belong to a nested context whose text is covered by that context's own run.
    /// </para>
    /// </summary>
    [TestCase]
    public void SourceTextReachesTheOutputExactlyOnce()
    {
        foreach (var (name, scenario) in AllScenarios())
        {
            var layout = Run(scenario);
            var perSource = new Dictionary<int, StringBuilder>();

            foreach (var element in layout.Elements)
            {
                if (element.Type != DrawElement.ElementType.Text || IsSynthesised(element))
                    continue;
                if (string.IsNullOrEmpty(element.Text))
                    continue;

                if (!perSource.TryGetValue(element.SourceIndex, out var text))
                    perSource[element.SourceIndex] = text = new StringBuilder();

                text.Append(element.Text);
            }

            foreach (var (sourceIndex, text) in perSource)
            {
                string expected = StripWhitespace(scenario.Elements[sourceIndex].Text ?? string.Empty);
                AssertThat(StripWhitespace(text.ToString())).OverrideFailureMessage(
                        $"{name}: the output for source element {sourceIndex} does not match its text"
                        + " | expected: " + StripWhitespace(expected + $" | expected=[{StripWhitespace(expected)}] actual=[{StripWhitespace(text.ToString())}]")
                        + " | actual: " + StripWhitespace(text.ToString()))
                    .IsEqual(expected);
            }
        }
    }

    /// <summary>
    /// Remove whitespace so a dropped line-leading space does not count as lost text.
    /// </summary>
    /// <param name="text">The text to normalize.</param>
    /// <returns>The text without whitespace characters.</returns>
    private static string StripWhitespace(string text)
    {
        var sb = new StringBuilder(text.Length);

        foreach (char c in text)
        {
            if (!char.IsWhiteSpace(c))
                sb.Append(c);
        }

        return sb.ToString();
    }

    // ── Scenarios ──

    private static IEnumerable<(string Name, Scenario Scenario)> AllScenarios()
    {
        yield return ("chinese-paragraph", ChineseParagraph());
        yield return ("cjk-latin-mixed", CjkLatinMixed());
        yield return ("vertical-rl-japanese", VerticalJapanese());
        yield return ("vertical-zh-hant-bopomofo", VerticalChineseBopomofo());
        yield return ("punctuation-stress", PunctuationStress());
        yield return ("multi-paragraph", MultiParagraph());
        yield return ("inline-image", InlineImage());
        yield return ("background-padding", BackgroundPadding());
        yield return ("auto-size-block", AutoSizeBlock());
        yield return ("padding-paragraph-indent", PaddingAndParagraphIndent());
        yield return ("wrap-region", WrapRegionLeft());
        yield return ("number-units", NumberUnits());
        yield return ("paragraph-language-switch", ParagraphLanguageSwitch());
        yield return ("mixed-quotes", MixedQuotes());
        yield return ("letterforms-zh-hant", Letterforms());
        yield return ("english-hyphenation", EnglishHyphenation());
        yield return ("letterforms-ko", KoreanDisplayForms());
        yield return ("ruby-mono", MonoRuby());
        yield return ("ruby-group", GroupRuby());
        yield return ("emphasis-marks", EmphasisMarks());
        yield return ("ruby-zhuyin-zh-hant", ZhuyinRuby());
        yield return ("ruby-zhuyin-tones-zh-hant", ZhuyinToneMarks());
        yield return ("wrap-middle-band", WrapMiddleBand());
        yield return ("english-paragraph", EnglishParagraph());
        yield return ("english-hyphenated", EnglishHyphenated());
    }

    /// <summary>
    /// Numbers with their units: a multi-character unit (公斤) must not be split either, which needs the character
    /// before the pair to be seen as a digit, and a unit word without a number (办公) must stay breakable.
    /// </summary>
    private static Scenario NumberUnits() => new(
        [
            Text("长度10米，重量5公斤，价格100元，编号第3号，时间是2024年。"),
        ],
        Settings(maxWidth: 160f, languageTag: "zh-Hans"));

    /// <summary>
    /// Three paragraphs under one Chinese request: the middle one names English, so its breaks come from UAX #14
    /// and it inserts no CJK/Latin gap, while the Chinese paragraphs keep their prohibition tailoring and their
    /// gap. This is the paragraph-scope layer, visible in one dump.
    /// </summary>
    private static Scenario ParagraphLanguageSwitch() => new(
        [
            Text("中文第一段，包含Latin词。"),
            Break(),
            Text("English paragraph with CJK中文mixed in.", paragraph: new ParagraphSettings { LanguageTag = "en" }),
            Break(),
            Text("中文第三段，也包含Latin词。"),
        ],
        Settings(maxWidth: 200f, languageTag: "zh-Hans"));

    /// <summary>
    /// A Chinese paragraph quoting an English phrase: the quotation marks are Common characters, so the segmenter
    /// resolves them by the content they belong to — here Western — and the boundary dump shows which role each
    /// one took. This is the mixing case a code-point table gets wrong.
    /// </summary>
    private static Scenario MixedQuotes() => new(
        [
            Text("中文段落里引用一段英文：“the quick brown fox”，然后再回到中文。"),
        ],
        Settings(maxWidth: 240f, languageTag: "zh-Hans"));

    /// <summary>
    /// The same source text under a traditional Chinese request: the quotation marks are written the
    /// simplified way and displayed as corner brackets, and the ellipsis is re-centered. This is the display
    /// form a language owns — the dump shows both the source text and the form that was shaped, and the
    /// source ranges do not move because of it.
    /// </summary>
    private static Scenario Letterforms() => new(
        [
            Text("繁體中文的引號：“引號”與‘嵌套’，省略號……三點居中。"),
        ],
        Settings(maxWidth: 240f, languageTag: "zh-Hant"));

    /// <summary>
    /// Korean horizontal writing: the sentence marks are written as the full-width ones and displayed as the
    /// narrow ones (klreq §6.1.2), and the profile names its own prohibition classes. The dump shows the
    /// substitution and the narrower widths it produces.
    /// </summary>
    private static Scenario KoreanDisplayForms() => new(
        [
            Text("한국어 문장 부호는 가로짜기에서 좁은 형태를 씁니다。、 마침표와 쉼표를 확인합니다."),
        ],
        Settings(240f, languageTag: "ko"));

    /// <summary>
    /// One annotation per base character: each ruby is centred on its own character, and the line reserves the
    /// room above the text box because the Chinese convention keeps the line gap independent of the annotations
    /// (clreq §5.5.3). The dump shows both the base elements and where each annotation went.
    /// </summary>
    private static Scenario MonoRuby() => new(
        [
            Text("振り仮名", ruby: new RubySpec
            {
                Text = "ふりがな",
                Distribution = RubyDistribution.Mono,
            }),
            Text("を付けた中文文本。"),
        ],
        Settings(240f, languageTag: "zh-Hans"));

    /// <summary>
    /// One annotation over a whole run: it is centred over the base text, and the run is kept on one line so the
    /// annotation never separates from what it annotates.
    /// </summary>
    private static Scenario GroupRuby() => new(
        [
            Text("東京", ruby: new RubySpec
            {
                Text = "とうきょう",
                Distribution = RubyDistribution.Group,
            }),
            Text("は日本の首都であり、世界有数の大都市である。"),
        ],
        Settings(200f, languageTag: "ja"));

    /// <summary>
    /// Bopomofo for Traditional Chinese: the annotation sits beside its base character and is set as a column of
    /// its own, one symbol under the next, in horizontal writing as well as in vertical (clreq §5.5.3.1 finds the
    /// right-hand position the better practice "whether in horizontal or vertical writing mode").
    /// <para>
    /// One annotated element per character, because clreq sets one annotation per Han character: a caller with
    /// three symbols for three characters gives three annotations, which is also the only way the tone mark stays
    /// in the cell of the character it belongs to.
    /// </para>
    /// <para>
    /// The ratio is passed explicitly. clreq §5.5.3.2 states 3:10, while the engine's default stays the half size
    /// jlreq measures Japanese annotations with; the specification's figure belongs to Bopomofo, so the caller who
    /// writes Bopomofo is the one who asks for it, and no language silently changes the meaning of the public
    /// parameter.
    /// </para>
    /// </summary>
    private static Scenario ZhuyinRuby() => new(
        [
            Bopomofo("今", "ㄐㄧㄣ"),
            Bopomofo("天", "ㄊㄧㄢ"),
            Text("的天氣。"),
        ],
        Settings(240f, languageTag: "zh-Hant"),
        "Bopomofo beside the base text: ruby=@(x,y) starts at the base advance's right edge, w= is the column's"
        + " height (the sum of the annotation glyphs' own advances, read downwards), size= is 3:10 of the base"
        + " size, and the base character gained half an em (size=(24.000,...)) which is the room the column"
        + " occupies - so line[0] h= is the same as it would be without any annotation.");

    /// <summary>
    /// The three places a Bopomofo tone mark can go (clreq §5.5.3.3): outside the last symbol's upper right corner
    /// for 平上去, outside its lower right corner for the checked tones, and before the reading for the neutral tone -
    /// which is a cell of its own, but a thin one: 1:15 of the base character (clreq §5.5.3.2's note).
    /// <para>
    /// The readings are one cell shorter than they would be with the mark treated as a symbol, which is what the
    /// dump's <c>w=</c> shows: three symbols and a mark take three cells, not four, so a reading can no longer be
    /// taller than the character it annotates (§5.5.3.2).
    /// </para>
    /// </summary>
    private static Scenario ZhuyinToneMarks() => new(
        [
            Bopomofo("調", "ㄉㄧㄠˋ"),
            Bopomofo("桌", "˙ㄗ"),
            Bopomofo("國", "ㄍㄨㄛㆷ"),
            Text("的聲調。"),
        ],
        Settings(240f, languageTag: "zh-Hant"),
        "Bopomofo tone marks: 平上去 hangs at the last symbol's upper right without a cell, the checked tone at the"
        + " column's foot, and the neutral tone is a thin cell (1:15 of the base size) in front of the reading");

    /// <summary>
    /// Emphasis marks: the same styled run under a Chinese request puts them under the characters, under a
    /// Japanese one over them (clreq §5.3.1, jlreq §3.3.9).
    /// </summary>
    private static Scenario EmphasisMarks() => new(
        [
            Text("这段文字带着重号。", emphasis: EmphasisMarkStyle.Dot),
            Break(),
            Text("この文には圏点がある。", emphasis: EmphasisMarkStyle.Dot),
        ],
        Settings(240f, languageTag: "zh-Hans"));

    /// <summary>
    /// A column too narrow for the words it carries: the language's hyphenation patterns cut the long ones, and each
    /// line that ends inside a word ends with the hyphen the assembler placed there.
    /// </summary>
    private static Scenario EnglishHyphenation() => new(
        [
            Text("Typography is the arrangement of implementation details that a reader never notices."),
        ],
        Settings(90f, languageTag: "en"));

    /// <summary>
    /// An exclusion that covers the middle of the line, leaving text an interval on each side. This is the
    /// multi-interval case: the text continues in the right interval instead of the line ending at the shape.
    /// </summary>
    private static Scenario WrapMiddleBand() => new(
        [
            Text("文字应当填满排除带左侧的区间，然后继续排入右侧的区间，而不是在排除带前就结束这一行。"),
            Text("排除带结束之后，后续文字恢复整行宽度继续排布。"),
        ],
        Settings(maxWidth: 240f, wrapRegion: new WrapRegion
        {
            Shape = new RectWrapShape { Width = 72f, Height = 200f },
            Position = new Vector2(96f, 0f),
            WrapMode = WrapFloat.Left,
            Margin = 6f,
        }));

    /// <summary>
    /// English prose: the profile breaks lines by the Unicode algorithm, so breaks land at spaces and after
    /// punctuation, and none of the CJK behaviours (prohibition, script gap, squeezing) apply.
    /// </summary>
    private static Scenario EnglishParagraph() => new(
        [
            Text("The quick brown fox jumps over the lazy dog, and the dog does not seem to mind at all."),
            Break(),
            Text("Typography is the craft of arranging text so that reading it costs no effort."),
        ],
        Settings(maxWidth: 240f, languageTag: "en"));

    /// <summary>
    /// A run of hyphenated compounds: Unicode line breaking allows a break after each hyphen, which the
    /// segmenter expresses as a segment boundary, so these words can fill a line down to the hyphen.
    /// </summary>
    private static Scenario EnglishHyphenated() => new(
        [
            Text("A well-known state-of-the-art implementation of a self-contained layout engine."),
        ],
        Settings(maxWidth: 200f, languageTag: "en"));

    /// <summary>
    /// Content-area padding plus per-paragraph overrides. Both paths used to be invisible to line
    /// adjustment: it re-derived the span from <c>MaxWidth</c> (ignoring padding) and every paragraph
    /// reported empty settings, so the overrides could not reach the layout.
    /// </summary>
    private static Scenario PaddingAndParagraphIndent() => new(
        [
            Text(
                "这一段的段落设置来自元素自身，而不是全局默认值，因此缩进与段后间距都不一样。",
                paragraph: new ParagraphSettings
                {
                    FirstLineIndent = 1,
                    LeftIndent = 8f,
                    RightIndent = 8f,
                    Alignment = TextAlignment.Left,
                    SpacingAfter = 4f,
                }),
            Break(),
            Text("第二段没有自己的设置，应当回落到全局设置。"),
        ],
        Settings(maxWidth: 220f, padding: 12f));

    /// <summary>
    /// Text flowing around an exclusion region. The wrap region narrows individual lines, so line
    /// adjustment must honour the span L1 resolved instead of assuming the full width.
    /// </summary>
    private static Scenario WrapRegionLeft() => new(
        [
            Text("这段文字应当绕开左侧的排除区域排布，每一行的可用区间由排除区与当前行高共同决定。"),
            Text("排除区结束之后，后续文字恢复到整行宽度继续排布。"),
        ],
        Settings(maxWidth: 240f, wrapRegion: new WrapRegion
        {
            Shape = new RectWrapShape { Width = 72f, Height = 56f },
            Position = new Vector2(0f, 0f),
            WrapMode = WrapFloat.Left,
            Margin = 6f,
        }));

    private static Scenario ChineseParagraph() => new(
        [
            Text("中文排版需要处理标点与行首禁则，这一段的长度足以检验断行位置。"),
            Break(),
            Text("第二段用来检验段落间距，以及段落末行的对齐行为。"),
        ],
        Settings(maxWidth: 240f));

    /// <summary>
    /// Vertical writing: the same conventions laid out as columns. Right-to-left columns advance leftward from the
    /// content box' right edge, a line's length is the request's <c>MaxHeight</c> (not <c>MaxWidth</c>, which bounds
    /// how far the columns may run), an annotation sits beside its base character and an emphasis mark on the
    /// column's right - all of which the dump's coordinates make checkable.
    /// </summary>
    private static Scenario VerticalJapanese() => new(
        [
            Text("日本語の縦組では、行は右から左へ進みます。"),
            Break(),
            Text("注釈", ruby: new RubySpec { Text = "ちゅうしゃく", Distribution = RubyDistribution.Group }),
            Text("と圏点", emphasis: EmphasisMarkStyle.Dot),
        ],
        Settings(maxWidth: 120f, maxHeight: 240f, writingMode: WritingMode.VerticalRl, languageTag: "ja"),
        "vertical: columns advance leftward, a line is bounded by MaxHeight; in this mode `base` is the baseline's "
        + "block coordinate (the column's own), not a Y");

    /// <summary>
    /// Vertical writing with the annotation that belongs beside its base character: Traditional Chinese Bopomofo.
    /// The room it needs comes from the base character's own advance, which in vertical writing is the distance along
    /// the column - so this scenario records where the annotation and the widened base end up when the two axes are
    /// not the ones the horizontal case exercises.
    /// </summary>
    private static Scenario VerticalChineseBopomofo() => new(
        [
            Text("注音符號", paragraph: new ParagraphSettings { LanguageTag = "zh-Hant" }),
            Text("號", paragraph: new ParagraphSettings { LanguageTag = "zh-Hant" },
                ruby: new RubySpec
                {
                    Text = "ㄏㄠˋ",
                    Distribution = RubyDistribution.Mono,
                    SizeRatio = 0.3f,
                }),
        ],
        Settings(maxWidth: 120f, maxHeight: 240f, writingMode: WritingMode.VerticalRl, languageTag: "zh-Hant"),
        "vertical Bopomofo: the annotation's room comes from the inline axis, and it is drawn beside the column; "
        + "`base` is a column coordinate here (see the vertical note above)");

    /// <summary>
    /// The demo's annotated samples, as layout: a vertical column with a Japanese annotation and an emphasis mark on
    /// the block-start side, then the same Japanese annotation in horizontal writing, and a Simplified Chinese
    /// paragraph whose pinyin carries tone marks.
    /// </summary>
    private static Scenario AnnotatedSamples() => new(
        [
            Text("縦書きの"),
            Text("行", ruby: new RubySpec { Text = "ぎょう" }),
            Text("は右から左へ進み、注釈は列の右に付きます。"),
            Text("圏点", emphasis: EmphasisMarkStyle.SesameDot),
            Text("も同じ側に付き、行の高さはどちらも行が負担します。"),
            Break(),
            Text("振り仮名", ruby: new RubySpec { Text = "ふりがな" }),
            Text(" と "),
            Text("漢字", ruby: new RubySpec { Text = "かんじ" }),
            Break(),
            Text("注", paragraph: new ParagraphSettings { LanguageTag = "zh-Hans" },
                ruby: new RubySpec { Text = "zhù", Distribution = RubyDistribution.Mono }),
            Text("音", paragraph: new ParagraphSettings { LanguageTag = "zh-Hans" },
                ruby: new RubySpec { Text = "yīn", Distribution = RubyDistribution.Mono }),
            Text("符", paragraph: new ParagraphSettings { LanguageTag = "zh-Hans" },
                ruby: new RubySpec { Text = "fú", Distribution = RubyDistribution.Mono }),
            Text("号", paragraph: new ParagraphSettings { LanguageTag = "zh-Hans" },
                ruby: new RubySpec { Text = "hào", Distribution = RubyDistribution.Mono }),
            Text(" 是汉语拼音的标法。", paragraph: new ParagraphSettings { LanguageTag = "zh-Hans" }),
        ],
        Settings(maxWidth: 160f, maxHeight: 200f, writingMode: WritingMode.VerticalRl, languageTag: "ja"),
        "the demo's annotated samples: a column with an annotation and a mark, then the same in horizontal writing");

    private static Scenario CjkLatinMixed() => new(
        [
            Text("在Godot引擎中使用C#进行游戏开发时，可以利用SkiaSharp进行2D渲染。"),
            Text("HarfBuzz是一个OpenType文本整形引擎，支持Unicode标准中定义的复杂文字排版规则。"),
        ],
        Settings(maxWidth: 240f));

    private static Scenario PunctuationStress() => new(
        [
            Text("测试标点：「这是『带有』嵌套引号的（测试）文本」。"),
            Text("连续标点测试：，，。。！！？？、、；；"),
            Text("行首禁则测试：这段文本的某些标点符号不应出现在行首，例如句号。逗号，问号？"),
            Text("省略号不拆分测试：这是一段包含省略号……的文本。"),
        ],
        Settings(maxWidth: 200f));

    private static Scenario MultiParagraph() => new(
        [
            Text("第一段落：这是带有首行缩进的段落，用于检验首行缩进与两端对齐的组合行为。"),
            Break(),
            Text("第二段落：两端对齐模式下，文字会均匀分布在行的两端。"),
            Break(),
            Text("第三段落：段落间距与行间距共同决定了大段文本的可读性。"),
        ],
        Settings(maxWidth: 260f, firstLineIndent: 2, alignment: TextAlignment.Justify, paragraphSpacing: 6f));

    private static Scenario InlineImage() => new(
        [
            Text("图片之前的文字内容用于检验行内对象的垂直对齐，"),
            Image(new Vector2(32f, 32f), InlineVerticalAlignment.Middle),
            Text("图片之后的文字继续排布在同一段落里。"),
        ],
        Settings(maxWidth: 240f));

    private static Scenario BackgroundPadding() => new(
        [
            Text("行内代码样式的背景"),
            Text("inline-code", background: new Color(0.2f, 0.2f, 0.2f), padding: new Vector2(4f, 2f)),
            Text("应当合并成一个背景矩形而不是每字一块。"),
        ],
        Settings(maxWidth: 240f));

    private static Scenario AutoSizeBlock() => new(
        [
            Text("列表前的引导段落。"),
            Break(),
            Block(
                new BlockLayout
                {
                    FullWidth = true,
                    LeftIndent = 24f,
                    Padding = new Vector2(8f, 6f),
                    BackgroundColor = new Color(0.92f, 0.92f, 0.96f),
                    BackgroundCornerRadius = 4f,
                    LeftBorderColor = new Color(0.4f, 0.5f, 0.9f),
                    LeftBorderWidth = 3f,
                    MarkerText = "1.",
                    MarkerFont = ThemeDB.FallbackFont,
                    MarkerFontSize = 14,
                    MarkerColor = new Color(0.1f, 0.1f, 0.1f),
                }),
            Text("块内的第一行内容会自动换行并参与嵌套排版。"),
            Text("块内的第二段内容。"),
            BlockEnd(),
        ],
        Settings(maxWidth: 260f));

    // ── Element builders ──

    private static DrawElement Text(
        string text,
        Color? background = null,
        Vector2? padding = null,
        ParagraphSettings? paragraph = null,
        RubySpec? ruby = null,
        EmphasisMarkStyle emphasis = EmphasisMarkStyle.None) => new()
        {
            Type = DrawElement.ElementType.Text,
            Text = text,
            Font = ThemeDB.FallbackFont,
            FontSize = 16,
            Color = new Color(0.1f, 0.1f, 0.1f),
            BackgroundColor = background,
            BackgroundPadding = padding ?? Vector2.Zero,
            BackgroundCornerRadius = background.HasValue ? 3f : 0f,
            ParagraphSettings = paragraph,
            Ruby = ruby,
            EmphasisMark = emphasis,
        };

    /// <summary>
    /// One Bopomofo annotation over one character: mono (one piece for the one cluster) at clreq §5.5.3.2's
    /// 3:10 ratio, which is the shape a Taiwanese textbook sets a character's reading in.
    /// </summary>
    /// <param name="baseText">The character being annotated.</param>
    /// <param name="bopomofo">Its Bopomofo reading, tone mark included.</param>
    /// <returns>The source element.</returns>
    private static DrawElement Bopomofo(string baseText, string bopomofo) => Text(baseText, ruby: new RubySpec
    {
        Text = bopomofo,
        Distribution = RubyDistribution.Mono,
        SizeRatio = 0.3f,
    });

    private static DrawElement Break() => new()
    {
        Type = DrawElement.ElementType.Text,
        Font = ThemeDB.FallbackFont,
        FontSize = 16,
        IsParagraphBreak = true,
    };

    private static DrawElement Image(Vector2 size, InlineVerticalAlignment alignment) => new()
    {
        Type = DrawElement.ElementType.Image,
        Size = size,
        VerticalAlignment = alignment,
        Color = new Color(0.3f, 0.3f, 0.3f),
    };

    private static DrawElement Block(BlockLayout block) => new()
    {
        Type = DrawElement.ElementType.ExtensionRegion,
        BlockInfo = block,
        Font = ThemeDB.FallbackFont,
        FontSize = 16,
    };

    private static DrawElement BlockEnd() => new()
    {
        Type = DrawElement.ElementType.ExtensionRegion,
        IsBlockEnd = true,
        Font = ThemeDB.FallbackFont,
        FontSize = 16,
    };

    private static TypographySettings Settings(
        float maxWidth,
        int firstLineIndent = 0,
        TextAlignment alignment = TextAlignment.Left,
        float paragraphSpacing = 0f,
        float padding = 0f,
        WrapRegion? wrapRegion = null,
        string? languageTag = null,
        WritingMode writingMode = WritingMode.HorizontalTb,
        float maxHeight = 0f)
    {
        var settings = new TypographySettings
        {
            MaxWidth = maxWidth,
            MaxHeight = maxHeight,
            WritingMode = writingMode,
            LanguageTag = languageTag,
            LineSpacing = 0f,
            ParagraphSpacing = paragraphSpacing,
            FirstLineIndent = firstLineIndent,
            Alignment = alignment,
            Padding = padding,
        };

        if (wrapRegion != null)
            settings.WrapRegions.Add(wrapRegion);

        return settings;
    }

    // ── Layout + dump ──

    private static Layout Run(Scenario scenario)
    {
        using var engine = new TypographyEngine(scenario.Options);
        var lines = engine.PrepareAndLayout(scenario.Elements.AsSpan(), out var contentSize);
        var elements = engine.GetLayoutElements(scenario.Elements);
        return new Layout(lines, elements, contentSize, engine.LastBoundaries);
    }

    private static void AssertGolden(string name, Scenario scenario)
    {
        var layout = Run(scenario);
        var actual = Dump(scenario, layout);
        var path = ProjectSettings.GlobalizePath($"{GoldenDir}/{name}.txt");

        if (!File.Exists(path))
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            File.WriteAllText(path, actual);
            GD.Print($"[golden] recorded {name} ({NameOf(path)}) — re-run to compare against it");
            return;
        }

        var expected = File.ReadAllText(path).ReplaceLineEndings("\n");
        var normalized = actual.ReplaceLineEndings("\n");
        if (expected == normalized)
            return;

        AssertThat(false).OverrideFailureMessage(DescribeDifference(name, expected, normalized)).IsTrue();
    }

    private static string NameOf(string path) => Path.GetFileName(path);

    private static string Dump(Scenario scenario, Layout layout)
    {
        var settings = scenario.Options;

        // Dump the parameters the layout actually used, not the request: language-governed values come
        // from the profile and the request only overrides them, so printing the request would hide
        // exactly the values a diff exists to show.
        var typography = LanguageProfileRegistry.Shared.Resolve(settings);

        var sb = new StringBuilder();
        sb.Append("# typography golden dump (P0 contract)\n");
        sb.Append("# fonts: ThemeDB.FallbackFont (baselines depend on the engine font + system fallback)\n");

        if (scenario.Note is { Length: > 0 } note)
            sb.Append(Invariant($"# note: {note}\n"));
        sb.Append(Invariant($"contentSize=({settings.MaxWidth:F3} used) ({layout.ContentSize.X:F3},{layout.ContentSize.Y:F3})\n"));
        sb.Append(Invariant($"settings maxWidth={settings.MaxWidth:F3} lineSpacing={settings.LineSpacing:F3} "));
        sb.Append(Invariant($"paragraphSpacing={settings.ParagraphSpacing:F3} firstLineIndent={typography.FirstLineIndent} "));
        sb.Append(Invariant($"alignment={typography.Alignment} prohibition={typography.ProhibitionLevel} "));
        sb.Append(Invariant($"profile={typography.ProfileId} cjkLatinGapEm={typography.CjkLatinSpacingEm:F3} mode={typography.WritingMode}\n"));
        sb.Append(Invariant($"source={scenario.Elements.Length} lines={layout.Lines.Count} elements={layout.Elements.Count}\n"));

        for (int i = 0; i < layout.Lines.Count; i++)
        {
            var line = layout.Lines[i];
            sb.Append(Invariant($"line[{i}] y={line.Y:F3} h={line.Height:F3} asc={line.Ascent:F3} desc={line.Descent:F3} "));
            sb.Append(Invariant($"span=[{line.LineLeft:F3},{line.LineRight:F3}) para={line.ParagraphIndex} "));

            // A line split by an exclusion has more than one interval; the element lines below name the
            // interval each element went into.
            if (line.Spans.Length > 1)
                sb.Append(Invariant($"intervals={string.Join("|", Array.ConvertAll(line.Spans, span => span.ToString()))} "));
            sb.Append(Invariant($"first={line.IsFirstLineOfParagraph} frozen={line.IsFrozen} elems={line.Elements.Count}\n"));
        }

        sb.Append(BoundarySummary(layout.Boundaries));

        for (int i = 0; i < layout.Elements.Count; i++)
        {
            var e = layout.Elements[i];
            sb.Append(Invariant($"elem[{i}] {e.Type} pos=({e.Position.X:F3},{e.Position.Y:F3}) "));
            sb.Append(Invariant($"base={FormatFloat(e.BaselineY)} size=({e.Size.X:F3},{e.Size.Y:F3}) "));
            sb.Append(Invariant($"src={e.SourceIndex} range={e.SourceRange} cluster=[{e.ClusterStart},{e.ClusterEnd}) "));
            sb.Append(Invariant($"line={e.LineIndex} dir={e.Direction} text={Quote(e.Text)}"));
            if (e.DisplayText != null)
                sb.Append(Invariant($" display={Quote(e.DisplayText)}"));
            if (e.Ruby is { } ruby)
            {
                sb.Append(Invariant(
                    $" ruby={Quote(ruby.Text)}@({ruby.X:F3},{ruby.BaselineY:F3}) size={ruby.FontSize:F3} w={ruby.Width:F3} band={ruby.BandWidth:F3} glyphs={ruby.Glyphs?.Length ?? 0} why={ruby.Reason}"));

                // The orientation is the one thing about an annotation that a position cannot show: a column and
                // a band are told apart by which way the glyphs run. Only a column is recorded, so every other
                // dump stays the file it was.
                if (ruby.Orientation == RubyOrientation.Vertical)
                    sb.Append(" orient=Vertical");
            }

            if (e.Emphasis is { } emphasis)
            {
                sb.Append(Invariant(
                    $" emph={Quote(emphasis.Mark)}@({emphasis.X:F3},{emphasis.CenterY:F3}) size={emphasis.Size:F3} side={emphasis.Side} why={emphasis.Reason}"));
            }

            if (e.Hanging)
                sb.Append(" hanging=True");

            if (e.Reason != null)
                sb.Append(Invariant($" reason={e.Reason}"));
            sb.Append('\n');
        }

        return sb.ToString();
    }

    /// <summary>
    /// Render the boundary decisions of the layout. Only boundaries that carry a decision are listed —
    /// plain neighbours are the majority and would bury the interesting rows — but the per-kind counts
    /// pin how many of them there were. Boundaries are dumped before the elements because they are the
    /// input to the decisions that produced the element geometry.
    /// </summary>
    /// <param name="boundaries">Boundaries produced in the compile phase.</param>
    /// <returns>The dump section.</returns>
    private static string BoundarySummary(IReadOnlyList<Boundary> boundaries)
    {
        var counts = new Dictionary<BoundaryKind, int>();
        var sb = new StringBuilder();

        foreach (var boundary in boundaries)
        {
            counts[boundary.Kind] = counts.TryGetValue(boundary.Kind, out int seen) ? seen + 1 : 1;
        }

        sb.Append(Invariant($"boundaries total={boundaries.Count}"));

        foreach (BoundaryKind kind in Enum.GetValues<BoundaryKind>())
        {
            if (counts.TryGetValue(kind, out int count))
                sb.Append(Invariant($" {kind}={count}"));
        }

        sb.Append('\n');

        foreach (var boundary in boundaries)
        {
            if (!boundary.IsNotable)
                continue;

            sb.Append(Invariant($"  b[{boundary.LeftCluster}|{boundary.RightCluster}] kind={boundary.Kind} "));
            sb.Append(Invariant($"{boundary.LeftScript}->{boundary.RightScript} owner={boundary.Owner} "));
            sb.Append(Invariant($"spacing={boundary.BaseSpacing:F3} range=[{boundary.MinSpacing:F3},{boundary.MaxSpacing:F3}] "));
            sb.Append(Invariant($"break={!boundary.ForbiddenToBreak} start={boundary.ForbiddenAtLineStart} "));
            sb.Append(Invariant($"end={boundary.ForbiddenAtLineEnd} stretch={!boundary.ForbiddenToStretch} "));
            sb.Append(Invariant($"reason={boundary.Reason}\n"));
        }

        return sb.ToString();
    }

    private static string FormatFloat(float value) =>
        float.IsNaN(value) ? "NaN" : value.ToString("F3", CultureInfo.InvariantCulture);

    private static string Quote(string? text)
    {
        if (text == null) return "-";
        var sb = new StringBuilder(text.Length + 2);
        sb.Append('"');
        foreach (var ch in text)
        {
            switch (ch)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default: sb.Append(ch); break;
            }
        }
        sb.Append('"');
        return sb.ToString();
    }

    private static string Invariant(FormattableString text) =>
        text.ToString(CultureInfo.InvariantCulture);

    private static string DescribeDifference(string name, string expected, string actual)
    {
        var expectedLines = expected.Split('\n');
        var actualLines = actual.Split('\n');
        int count = Math.Min(expectedLines.Length, actualLines.Length);

        for (int i = 0; i < count; i++)
        {
            if (expectedLines[i] == actualLines[i]) continue;
            return $"golden '{name}' differs at line {i + 1}:\n" +
                   $"  expected: {expectedLines[i]}\n" +
                   $"  actual:   {actualLines[i]}\n" +
                   "Delete the golden file to re-record it after reviewing the change.";
        }

        return $"golden '{name}' differs in length: expected {expectedLines.Length} lines, " +
               $"actual {actualLines.Length} lines.\nDelete the golden file to re-record it.";
    }
}
