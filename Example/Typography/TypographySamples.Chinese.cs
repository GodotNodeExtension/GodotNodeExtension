using GodotNodeExtension.Component.Typography.Core.Model;

namespace GodotNodeExtension.Example.Typography;

/// <summary>
/// The Simplified Chinese sample: the conventions clreq states, each clause followed by the layout meeting it.
/// </summary>
public static partial class TypographySamples
{
    /// <summary>Blocks of the Simplified Chinese sample.</summary>
    /// <returns>The blocks, in reading order.</returns>
    private static Block[] ChineseSimplified() =>
    [
        new(BlockKind.Heading, "简体中文排版规范（clreq）", Language: "zh-Hans"),

        new(BlockKind.Clause,
            "行首禁则：句号、逗号、顿号、分号、冒号与收尾括号不得出现在一行的开头；"
            + "省略号与破折号也不得被拆开而只留下半个。下面的示例排得很窄，正是为了让这些标记撞到行首。",
            Language: "zh-Hans", FirstLineIndent: 2),
        new(BlockKind.Example,
            "这一段的宽度被压到刚好让「，」与「。」落在行尾，于是下一行不会以标点开头；"
            + "行首若真的只剩下标点，就会把前一个字一起拉到下一行，或是把标点挤进上一行。",
            Language: "zh-Hans", FirstLineIndent: 2),

        new(BlockKind.Clause,
            "行尾禁则：开始括号与开始引号（（、「、《、〈）不得落在行尾，收尾标点不单独成行；"
            + "连续的收尾标点会在行尾被挤压到半个字宽，换来更整齐的行尾。",
            Language: "zh-Hans", FirstLineIndent: 2),
        new(BlockKind.Example,
            "连续标点被挤压：」、。！？以及……与——在同一行尾相邻时，它们之间的距离会被收紧到半个字宽。",
            Language: "zh-Hans", FirstLineIndent: 2),

        new(BlockKind.Clause,
            "中西文间距：汉字与拉丁字母、数字之间插入四分之一个全角宽的间隙；"
            + "这个间隙是弹性的，挤压时可以缩到八分之一 em，拉伸时可以涨到二分之一 em。",
            Language: "zh-Hans", FirstLineIndent: 2),
        new(BlockKind.Example,
            "间隙示例：Godot 4.7 与 SkiaSharp、HarfBuzz、Unicode.Bidi 之间各有一个四分之一 em 的间隙，"
            + "而 100 元、5 公斤、12.5% 与 2024 年这些整体不被拆开。",
            Language: "zh-Hans", FirstLineIndent: 2),

        new(BlockKind.Clause,
            "注音（拼音）：注文标在基文上方，一字一注，声调符号属于注文的一部分；"
            + "注音带由行自己预留，所以有注音的行比没有注音的行高，上下两行不会重叠。",
            Language: "zh-Hans", FirstLineIndent: 2),
        new(BlockKind.Example,
            "注音", Reading: "zhù yīn", Distribution: RubyDistribution.Mono, Language: "zh-Hans", FirstLineIndent: 2),
        new(BlockKind.Example,
            "声调符号也在注音带里：", Language: "zh-Hans", FirstLineIndent: 2),
        new(BlockKind.Example,
            "汉语拼音", Reading: "hàn yǔ pīn yīn", Distribution: RubyDistribution.Mono,
            Language: "zh-Hans", FirstLineIndent: 2),

        new(BlockKind.Clause,
            "对齐与缩进：首行缩进两个字宽是段落的层次标志；两端对齐把多余的空白按优先级分配到西文词间距、"
            + "中西文间距与字间距上，最后才动字间距。",
            Language: "zh-Hans", FirstLineIndent: 2),
        new(BlockKind.Example,
            "这一段采用两端对齐：行末的空隙被分配到词与词之间，而不是把字距拉得忽宽忽窄，"
            + "读起来仍然保持方块的节奏。",
            Language: "zh-Hans", FirstLineIndent: 2, Alignment: TextAlignment.Justify),

        new(BlockKind.Clause,
            "着重号与下划线：着重号标在基文下方（横排中文），下划线的墨迹同样在字下；"
            + "两者都不属于文字本身，所以行会为它们长高，而不是让它们压到下一行去。",
            Language: "zh-Hans", FirstLineIndent: 2),
        new(BlockKind.Example,
            "着重号", Emphasis: EmphasisMarkStyle.Dot, Language: "zh-Hans", FirstLineIndent: 2),
        new(BlockKind.Example,
            "下划线", Underline: true, Language: "zh-Hans", FirstLineIndent: 2),
    ];
}
