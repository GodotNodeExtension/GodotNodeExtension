using GodotNodeExtension.Component.Typography.Core.Model;

namespace GodotNodeExtension.Example.Typography;

/// <summary>
/// The everything sample: one long document that walks through every other sample in turn. Each paragraph declares
/// its own language with <c>Block.Language</c>, and a clause before it says which convention to watch for, so a
/// reader can follow the document from Simplified Chinese to Traditional Chinese, Japanese, Korean, English, right
/// to left, marks and underlines, and finally an annotation wider than the text it sits on.
/// </summary>
public static partial class TypographySamples
{
    /// <summary>Blocks of the everything sample.</summary>
    /// <returns>The blocks, in reading order.</returns>
    private static Block[] Everything() =>
    [
        new(BlockKind.Heading, "综合示例：一份长文档里的各种排版约定", Language: "zh-Hans"),

        new(BlockKind.Clause,
            "这一段看简体中文：行首禁则、连续标点的挤压、中西文之间的弹性间隙、拼音注音带，"
            + "以及两端对齐会在同一段里同时出现。",
            Language: "zh-Hans", FirstLineIndent: 2),
        new(BlockKind.Example,
            "简体中文这一段同时演示禁则与挤压：「，」「。」不会移到下一行开头，行尾的连续标点被收进半个字宽；"
            + "汉字与 Godot 4.7、SkiaSharp 之间留出四分之一 em 的弹性间隙；两端对齐把余量分给词间距。",
            Language: "zh-Hans", FirstLineIndent: 2, Alignment: TextAlignment.Justify),
        new(BlockKind.Example,
            "拼音注音", Reading: "pīn yīn zhù yīn", Distribution: RubyDistribution.Mono,
            Language: "zh-Hans", FirstLineIndent: 2),

        new(BlockKind.Clause,
            "这一段看繁体中文：直角引号「」『』成对出现，注音符号标在字的右侧——直排时在字右，"
            + "横排时在字的上方偏右。",
            Language: "zh-Hant", FirstLineIndent: 2),
        new(BlockKind.Example,
            "繁體中文用「引號」與『雙引號』，注音符號標在字的右側，一聲不加調號，其餘聲調附在符號右上。",
            Language: "zh-Hant", FirstLineIndent: 2),
        new(BlockKind.Example,
            "注音符號", Reading: "ㄓㄨˋ ㄧㄣ ㄈㄨˊ ㄏㄠˋ", Distribution: RubyDistribution.Mono,
            Language: "zh-Hant", FirstLineIndent: 2),

        new(BlockKind.Clause,
            "这一段看日文：振り仮名排在字的上方（直排时在字的右侧，即 jlreq 的「モノルビ」），"
            + "圏点（着重点）标在字的旁边。",
            Language: "ja", FirstLineIndent: 1),
        new(BlockKind.Example,
            "日本語の段落では、振り仮名を字の上に付け、圏点で強調する。行頭禁則と行末禁則も同時に働く。",
            Language: "ja", FirstLineIndent: 1),
        new(BlockKind.Example,
            "振り仮名", Reading: "ふりがな", Distribution: RubyDistribution.Group,
            Emphasis: EmphasisMarkStyle.Dot, Language: "ja", FirstLineIndent: 1),

        new(BlockKind.Clause,
            "这一段看韩文：谚文与汉字、拉丁字母混排，韩文自己的禁则集管住行首与行末的标点"
            + "（klreq 的行头·行末禁则）。",
            Language: "ko"),
        new(BlockKind.Example,
            "한국어 문단은 한글과 漢字, 그리고 Latin alphabet을 함께 섞어 씁니다. 마침표와 쉼표는 "
            + "줄 머리에 오지 않도록 줄바꿈이 조정됩니다.",
            Language: "ko"),

        new(BlockKind.Clause,
            "这一段看英文：断行发生在空格处，必要时才按连字符断开很长的词，两端对齐只在词间距上伸缩，"
            + "不去拉扯字距。",
            Language: "en"),
        new(BlockKind.Example,
            "This English paragraph is justified on both edges. Line breaking happens at spaces, and a word such as "
            + "antidisestablishmentarianism is hyphenated only when the spaces alone cannot fill the measure.",
            Language: "en", Alignment: TextAlignment.Justify),

        new(BlockKind.Clause,
            "这一段看从右到左的书写：阿拉伯语的段落从右缘开始填满，里面的拉丁字母与数字保持自己的方向，"
            + "括号与引号在绘制时镜像。",
            Language: "ar"),
        new(BlockKind.Example,
            "مثال: يبدأ السطر من اليمين، وتُعكس الأقواس ( ) و[ ] عند الرسم، وتبقى الأرقام 2024 وكلمة Latin "
            + "باتجاهها الأصلي داخل النص العربي.",
            Language: "ar"),

        new(BlockKind.Clause,
            "这一段看着重号与下划线的行高：两者都画在字下（横排中文），行会为它们长高，"
            + "而不会让墨迹压到下一行去。",
            Language: "zh-Hans", FirstLineIndent: 2),
        new(BlockKind.Example,
            "着重号", Emphasis: EmphasisMarkStyle.Dot, Language: "zh-Hans", FirstLineIndent: 2),
        new(BlockKind.Example,
            "下划线", Underline: true, Language: "zh-Hans", FirstLineIndent: 2),

        new(BlockKind.Clause,
            "这一段看超长注音：基文只有四个字，注音却有九个字，基文行会被撑宽以容纳注音（熟語ルビ），"
            + "整段作为一个单位，注音与基文不会被拆散。",
            Language: "ja", FirstLineIndent: 1),
        new(BlockKind.Example,
            "東京都庁", Reading: "とうきょうとちょう", Distribution: RubyDistribution.Jukugo,
            Language: "ja", FirstLineIndent: 1),
    ];
}
