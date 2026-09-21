using GodotNodeExtension.Component.Typography.Core.Model;

namespace GodotNodeExtension.Example.Typography;

/// <summary>
/// The Japanese sample: the conventions jlreq states, each clause followed by the layout meeting it.
/// </summary>
public static partial class TypographySamples
{
    /// <summary>Blocks of the Japanese sample.</summary>
    /// <returns>The blocks, in reading order.</returns>
    private static Block[] Japanese() =>
    [
        new(BlockKind.Heading, "日本語組版の規範（jlreq）", Language: "ja"),

        new(BlockKind.Clause,
            "行頭禁則：終わり括弧類（」』）〕】）と、句読点（。、）、中黒（・）、長音符（ー）、"
            + "小書きのかな（ぁぃぅぇぉっゃゅょゎ）とそのカタカナ（ァィゥェォッャュョヮヵヶ）は、"
            + "行の先頭に置かない。字が行頭に来そうなときは、その字を前の字ごと次の行へ送るか、"
            + "前の行に字を一つ詰めて場所を作る。下の例は幅を詰めてあり、これらの字が行末に来ている。",
            Language: "ja", FirstLineIndent: 1),
        new(BlockKind.Example,
            "「しゃ」「ちょっと」「コーヒー」と並べても、小書きのかなと長音符は行末に残り、"
            + "行頭へは回らない。句読点も同じで、行の先頭に置かれることはない。",
            Language: "ja", FirstLineIndent: 1),

        new(BlockKind.Clause,
            "行末禁則：始め括弧類（「『〔【）は行の末尾に置かない。始め括弧が行末に来そうなときは、"
            + "括弧を次の行へ送るか、前の行から一字を次の行へ移して場所を作る。始め括弧と終わり括弧の対は、"
            + "行をまたいでも対のまま保たれる。",
            Language: "ja", FirstLineIndent: 1),
        new(BlockKind.Example,
            "（このように）始め括弧は行末に残らず、「この対」は行をまたいでも対のままである。"
            + "『入れ子』になった括弧も、外側と内側の対がそれぞれ崩れない。",
            Language: "ja", FirstLineIndent: 1),

        new(BlockKind.Clause,
            "行末の句点の二分アキ：句点（。）が行の末尾に来たときは、そのあとに二分の全角アキ（半角分の空白）"
            + "を置く（jlreq §3.1.9）。このアキは行末の調整でも詰めない。読点（、）が行末に来たときも同様である。",
            Language: "ja", FirstLineIndent: 1),
        new(BlockKind.Example,
            "この例の句点は行の末尾に来る。そのあとには半角分のアキが入り、行末の字面がそろう。"
            + "読点が行末に来たときにも、同じアキが入る。",
            Language: "ja", FirstLineIndent: 1),

        new(BlockKind.Clause,
            "振り仮名（モノルビ）：親字の一字ごとに読みを付け、読みは親字の真上に一字ずつ中心を合わせて載せる。"
            + "読みの字の大きさは親字の半分を標準とする（jlreq §3.3.3）。読みは親字の数だけ、空白で区切って並べる。"
            + "読みの帯は行が用意するので、振り仮名のある行はそれだけ高くなる。",
            Language: "ja", FirstLineIndent: 1),
        new(BlockKind.Example,
            "東京", Reading: "とう きょう", Distribution: RubyDistribution.Mono, Language: "ja", FirstLineIndent: 1),
        new(BlockKind.Example,
            "日本語", Reading: "に ほん ご", Distribution: RubyDistribution.Mono, Language: "ja", FirstLineIndent: 1),

        new(BlockKind.Clause,
            "振り仮名（グループルビ）：熟語や複合語のように語全体で一つの読みを持つときは、"
            + "読みを語全体の真上に均等に配る。親字の字間を少し広げて読みの幅に合わせ、語の途中では行を分けない。",
            Language: "ja", FirstLineIndent: 1),
        new(BlockKind.Example,
            "大人", Reading: "おとな", Distribution: RubyDistribution.Group, Language: "ja", FirstLineIndent: 1),

        new(BlockKind.Clause,
            "圏点：語句を目立たせるときは、親字の真上（横組）に圏点を一字ずつ打つ。点は中黒ではなく丸点「•」で、"
            + "親字の幅の中心に置く。圏点の分だけ行は高くなり、隣の行の字と重なることはない。",
            Language: "ja", FirstLineIndent: 1),
        new(BlockKind.Example,
            "強調", Emphasis: EmphasisMarkStyle.Dot, Language: "ja", FirstLineIndent: 1),
        new(BlockKind.Example,
            "この句に圏点を打つ", Emphasis: EmphasisMarkStyle.Dot, Language: "ja", FirstLineIndent: 1),

        new(BlockKind.Clause,
            "段落字下げ：横組の日本語では、段落の第一行を全角一字分だけ下げて書き出す。"
            + "字下げは段落の切れ目を示す目印であり、行の長さや字詰めは変わらない。"
            + "この文書の段落は、すべてこの一字下げで組んである。",
            Language: "ja", FirstLineIndent: 1),
        new(BlockKind.Example,
            "この例の第一行は一字下げで始まり、二行目からは行頭に字が並ぶ。"
            + "段落の初めが一段深く見えることが、読み手への手がかりになる。",
            Language: "ja", FirstLineIndent: 1),
    ];
}
