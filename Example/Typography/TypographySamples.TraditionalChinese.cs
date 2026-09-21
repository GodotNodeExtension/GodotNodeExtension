using GodotNodeExtension.Component.Typography.Core.Model;

namespace GodotNodeExtension.Example.Typography;

/// <summary>
/// The Traditional Chinese sample: the conventions clreq states for <c>zh-Hant</c>, each clause followed by the
/// layout that meets it.
/// </summary>
public static partial class TypographySamples
{
    /// <summary>Blocks of the Traditional Chinese sample.</summary>
    /// <returns>The blocks, in reading order.</returns>
    private static Block[] ChineseTraditional() =>
    [
        new(BlockKind.Heading, "繁體中文排版規範（clreq）", Language: "zh-Hant"),

        new(BlockKind.Clause,
            "引號用直角引號：「」是單層引號，引號之內再引一層時用『』；原文若寫成彎引號 “ ”、‘ ’，"
            + "排版時會被替換成「」與『』再塑形，寫下的字元本身不改（clreq §5.2）。",
            Language: "zh-Hant", FirstLineIndent: 2),
        new(BlockKind.Example,
            "顯示形示例：他說 “老師交代‘明天交稿’”，於是稿子當天就送到了；"
            + "寫成 “ ” 與 ‘ ’ 的引號在這裡按繁體顯示成「」與『』。",
            Language: "zh-Hant", FirstLineIndent: 2),

        new(BlockKind.Clause,
            "省略號是兩個接連的「…」，佔兩個全角寬並落在字面的中央——繁體把它顯示成 ⋯⋯，"
            + "而不是靠下的三點（clreq §5.1.4）；兩個省略號是一個整體，不得被拆到兩行去。",
            Language: "zh-Hant", FirstLineIndent: 2),
        new(BlockKind.Example,
            "他翻到最後一頁，上面只有「其餘從略……」幾個字；再往後，是……一片空白。",
            Language: "zh-Hant", FirstLineIndent: 2),

        new(BlockKind.Clause,
            "行首禁則：句號、逗號、頓號、分號、冒號、問號、驚嘆號與收尾的括號、引號都不得起一行；"
            + "為了讓下一行不以標點開頭，前一個字會被一起帶到下一行去。下面的示例排得很窄，"
            + "正是為了讓標點撞到行首。",
            Language: "zh-Hant", FirstLineIndent: 2),
        new(BlockKind.Example,
            "這一段刻意排窄，讓「，」與「。」撞到行尾：若不這樣做，下一行便會以標點起頭，"
            + "所以整句被往前推，收尾的標點寧可擠進上一行的行尾。",
            Language: "zh-Hant", FirstLineIndent: 2),

        new(BlockKind.Clause,
            "行尾禁則：開始的引號與括號（「、『、（、《）不得落在行末；收尾標點也不單獨成行——"
            + "一行若只剩一個標點，仍由前一個字陪著它一起過去。",
            Language: "zh-Hant", FirstLineIndent: 2),
        new(BlockKind.Example,
            "「（一）前言」與「『摘要』」的起首符號不會被留在行末，"
            + "《書名》兩側的書名號也一樣，永遠跟著被包住的那幾個字。",
            Language: "zh-Hant", FirstLineIndent: 2),

        new(BlockKind.Clause,
            "行末擠壓：行尾相鄰的收尾標點各自剪去一半的寬度，讓行末收齊；繁體橫排只做擠壓，"
            + "不把標點懸到行外——懸掛留給豎排。",
            Language: "zh-Hant", FirstLineIndent: 2),
        new(BlockKind.Example,
            "連續標點在行尾被擠壓：句號、逗號、收尾引號與省略號彼此相鄰時，"
            + "每個標點只留下半個字寬，行末因此收得整整齊齊，不會多出一個空位。",
            Language: "zh-Hant", FirstLineIndent: 2),

        new(BlockKind.Clause,
            "注音符號排在基字的右側，一字一注：注文本身豎排、符號由上而下，注音帶佔的是基字右邊半個字的"
            + "寬度，所以行高不因為有注音而改變（clreq §5.5.3.1、§5.5.3.2）。",
            Language: "zh-Hant", FirstLineIndent: 2),
        new(BlockKind.Example,
            "注音", Reading: "ㄓㄨˋ ㄧㄣ", Distribution: RubyDistribution.Mono,
            Language: "zh-Hant", FirstLineIndent: 2),
        new(BlockKind.Example,
            "臺灣", Reading: "ㄊㄞˊ ㄨㄢ", Distribution: RubyDistribution.Mono,
            Language: "zh-Hant", FirstLineIndent: 2),

        new(BlockKind.Clause,
            "調號的位置：陰平不標號；陽平的 ˊ、上聲的 ˇ、去聲的 ˋ 都標在最後一個注音符號的右上角；"
            + "輕聲的 ˙ 標在整個音節的最前面，也就是注文第一個符號之前（豎排時在最上方）。",
            Language: "zh-Hant", FirstLineIndent: 2),
        new(BlockKind.Example,
            "調號", Reading: "ㄉㄧㄠˋ ㄏㄠˋ", Distribution: RubyDistribution.Mono,
            Language: "zh-Hant", FirstLineIndent: 2),
        new(BlockKind.Example,
            "桌子", Reading: "ㄓㄨㄛ ˙ㄗ", Distribution: RubyDistribution.Mono,
            Language: "zh-Hant", FirstLineIndent: 2),

        new(BlockKind.Clause,
            "著重號標在字的下方（橫排繁體），是一個實心的圓點；它不屬於文字本身，"
            + "所以行會為它長高，而不是讓它壓到下一行去（clreq §5.3.1）。",
            Language: "zh-Hant", FirstLineIndent: 2),
        new(BlockKind.Example,
            "著重號", Emphasis: EmphasisMarkStyle.Dot, Language: "zh-Hant", FirstLineIndent: 2),
        new(BlockKind.Example,
            "底線畫在字的下方，與著重號同一側。", Underline: true, Language: "zh-Hant", FirstLineIndent: 2),
    ];
}
