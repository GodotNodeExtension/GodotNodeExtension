using GodotNodeExtension.Component.Typography.Core.Model;

namespace GodotNodeExtension.Example.Typography;

/// <summary>
/// The line-breaking stress sample: the clauses that decide where a Chinese line may end, each shown on a line
/// deliberately too narrow for the text, so the prohibition rules, the hanging marks and the unbreakable units are
/// all forced to happen on screen.
/// </summary>
public static partial class TypographySamples
{
    /// <summary>Blocks of the line-breaking stress sample.</summary>
    /// <returns>The blocks, in reading order.</returns>
    private static Block[] LineBreakingStress() =>
    [
        new(BlockKind.Heading, "换行压力测试（clreq 行首禁则与行尾处理）", Language: "zh-Hans"),

        new(BlockKind.Clause,
            "行首禁则：句号、逗号、顿号、分号、冒号、问号、叹号，以及收尾的引号和括号，都不得出现在一行的开头。"
            + "下面的示例栏宽被压到很窄，正是为了让这些标记正好撞到行首。",
            Language: "zh-Hans", FirstLineIndent: 2),
        new(BlockKind.Example,
            "这一行很窄，检验「，」、「。」、「、」、「；」、「：」撞到行首时的处理：排版器要么把前一个字一起"
            + "推到下一行，要么把标点挤进上一行的末尾。",
            Language: "zh-Hans", FirstLineIndent: 2),

        new(BlockKind.Clause,
            "连续标点：收尾标点在同一行尾相邻时，它们之间的距离被收紧，通常收到半个字宽，"
            + "多个连在一起也不会把行尾撑破。",
            Language: "zh-Hans", FirstLineIndent: 2),
        new(BlockKind.Example,
            "行尾挤压：」、。！？以及……与——在同一行尾相邻时，它们之间的空白被压到半个字宽，"
            + "换来一条整齐的右缘。",
            Language: "zh-Hans", FirstLineIndent: 2),

        new(BlockKind.Clause,
            "悬挂标点：逗号、句号、顿号、分号、冒号这类收尾小标点，可以在行尾略微越过版心悬挂出去，"
            + "而收尾的引号和括号则整对留在版心之内，不拆到下一行。",
            Language: "zh-Hans", FirstLineIndent: 2),
        new(BlockKind.Example,
            "悬挂示例：行末的逗号、句号与顿号允许挂出版心一点点，收尾的」和）则整对保留，"
            + "既不留出行首标点，也不把字距拉得忽宽忽窄。",
            Language: "zh-Hans", FirstLineIndent: 2),

        new(BlockKind.Clause,
            "省略号与破折号：……（U+2026 连用）与——（两倍宽度）各是一个整体，不得在中间拆行，"
            + "也不能只把半个留在上一行。",
            Language: "zh-Hans", FirstLineIndent: 2),
        new(BlockKind.Example,
            "这里放一个省略号……再看一个破折号——它们都占满两格，作为一个不可拆的单位整体换行，"
            + "绝不会一半在行末、一半在行首。",
            Language: "zh-Hans", FirstLineIndent: 2),

        new(BlockKind.Clause,
            "不断开的单位：数字与它后面的单位、数与数之间的范围号、百分号与年份后缀，都要黏在一起，"
            + "不因为在窄栏里放不下就把数字与单位拆到两行。",
            Language: "zh-Hans", FirstLineIndent: 2),
        new(BlockKind.Example,
            "不拆开：10 米、3—5 厘米、12.5%、2024 年，这些整体一起换行，数字和单位永远不会分家。",
            Language: "zh-Hans", FirstLineIndent: 2),

        new(BlockKind.Clause,
            "挤压与拉伸：栏很窄时，中西文之间的弹性间隙先被压缩，还不够就压行尾的标点；"
            + "栏稍宽时，余量先加在词间距与中西文间隙上，最后才动字距。",
            Language: "zh-Hans", FirstLineIndent: 2),
        new(BlockKind.Example,
            "窄栏示例：Godot 4.7、SkiaSharp、HarfBuzz 与 Unicode.Bidi 之间的间隙先被压到八分之一 em，"
            + "再让行尾的标点收进半个字宽，这样每一行仍然保持方块般的整齐。",
            Language: "zh-Hans", FirstLineIndent: 2, Alignment: TextAlignment.Justify),
    ];
}
