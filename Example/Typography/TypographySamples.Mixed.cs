using GodotNodeExtension.Component.Typography.Core.Model;

namespace GodotNodeExtension.Example.Typography;

/// <summary>
/// The mixed-language sample: one document, several languages, each paragraph under its own convention - and one
/// paragraph that declares no language at all, so the characters themselves have to decide which convention it is.
/// </summary>
public static partial class TypographySamples
{
    /// <summary>Blocks of the mixed-language sample.</summary>
    /// <returns>The blocks, in reading order.</returns>
    private static Block[] MixedLanguages() =>
    [
        new(BlockKind.Heading, "混排：一份文档里的四种语言", Language: "zh-Hans"),

        new(BlockKind.Clause,
            "这一段声明 zh-Hans，于是简体中文的规范接着它：标点用中文的全角形式，收尾标点不落在行首，"
            + "开始括号不落在行尾；汉字与拉丁字母、数字之间留四分之一全角宽的间隙；段首缩进两个字宽。"
            + "这些都由所声明的语言决定，段落自己不必再写一遍。",
            Language: "zh-Hans"),
        new(BlockKind.Example,
            "这一段声明 zh-Hans：句号与顿号留在行尾，行首一定是汉字；Godot 4.7 与 SkiaSharp 之间"
            + "留着四分之一 em 的间隙，段首缩进两个字宽。",
            Language: "zh-Hans"),

        new(BlockKind.Clause,
            "第二の段落は言語を ja と宣言する。ここからは日本語の規範で組まれ、小書きのかなと長音符は行頭に置かれず、"
            + "行末の句点のあとには二分のアキが残り、振り仮名と圏点は親字の上へ載る。"
            + "段首の字下げも、この段落からは全角一字分になる。",
            Language: "ja"),
        new(BlockKind.Example,
            "この段落は ja を宣言する。句点は行末で半角分のアキを取り、"
            + "「しゃ」や「コーヒー」の小書きのかなと長音符は行頭へ回らない。",
            Language: "ja"),
        new(BlockKind.Example,
            "日本語", Reading: "に ほん ご", Distribution: RubyDistribution.Mono, Language: "ja"),

        new(BlockKind.Clause,
            "第三段宣告 zh-Hant：引號換成「」與『』，刪節號改為居中；注音符號標在基字的右側，"
            + "而不是字的上方（clreq §5.5.3.1）。音符佔用的空間由基字自己的字寬讓出，"
            + "所以這一段的行高和沒有注音的行一樣（clreq §5.5.3.2），音符則沿著基字排成一小列。",
            Language: "zh-Hant"),
        new(BlockKind.Example,
            "這一段宣告 zh-Hant：標點與引號依繁體條目排，段首縮進兩字寬，注音落在字的右邊，"
            + "而不是壓在字的上頭。",
            Language: "zh-Hant"),
        new(BlockKind.Example,
            "注音", Reading: "ㄓㄨˋ ㄧㄣ", Distribution: RubyDistribution.Mono, Language: "zh-Hant"),

        new(BlockKind.Clause,
            "This paragraph is declared en. None of the CJK tailoring reaches it: its lines break at spaces, "
            + "a word crosses a line only where a hyphenation pattern allows it, and its quotation marks and "
            + "apostrophes keep their Latin shapes instead of turning into corner brackets.",
            Language: "en"),
        new(BlockKind.Example,
            "This paragraph is en as well: no first-line indent, lines broken between words, "
            + "and hyphenation only where the patterns say so.",
            Language: "en"),

        new(BlockKind.Clause,
            "この段落は言語を宣言しない。だが仮名が入っているので、文字そのものが「これは日本語だ」と告げ、"
            + "段落は日本語として組まれる。禁則も、行末の句点のアキも、振り仮名の位置も ja のものになり、"
            + "この注意書きの段落も同じ扱いを受ける。"),
        new(BlockKind.Example,
            "この段落は言語を宣言していない。仮名があるので日本語と推定され、"
            + "句点は行末で半角分のアキを取り、「きょう」の拗音も行頭へは回らない。"),
        new(BlockKind.Example,
            "お茶", Reading: "お ちゃ", Distribution: RubyDistribution.Mono),
    ];
}
