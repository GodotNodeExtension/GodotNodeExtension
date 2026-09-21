using GodotNodeExtension.Component.Typography.Core.Model;

namespace GodotNodeExtension.Example.Typography;

/// <summary>
/// The vertical sample: Japanese set in columns, each clause followed by the layout that meets it. The writing
/// mode itself is the scene's, so these blocks only carry the text, the language and the annotations.
/// </summary>
public static partial class TypographySamples
{
    /// <summary>Blocks of the vertical sample.</summary>
    /// <returns>The blocks, in reading order.</returns>
    private static Block[] Vertical() =>
    [
        new(BlockKind.Heading, "縦書きの日本語組版（jlreq）", Language: "ja"),

        new(BlockKind.Clause,
            "縦書きでは、一行が一列になり、列は右から左へ進む。最初の列は内容領域の右端に立ち、"
            + "次の列はその左隣に置かれる。読み終えた列の左に、次の列が来る。",
            Language: "ja", FirstLineIndent: 1),
        new(BlockKind.Example,
            "この段落は列の進み方を示す。右端の列から読み始め、次の列はその左側にある。"
            + "三つ目の列は、さらにその左である。",
            Language: "ja", FirstLineIndent: 1),

        new(BlockKind.Clause,
            "一行の長さは列の高さで決まる。横組みで行の長さを決めていた版面の幅に代わり、"
            + "縦書きでは上から下までの長さが行内の上限になる。",
            Language: "ja", FirstLineIndent: 1),
        new(BlockKind.Example,
            "この列は高さで折り返す。一列に収まらない文字は次の列の先頭へ送られ、右から左へ続いていく。",
            Language: "ja", FirstLineIndent: 1),

        new(BlockKind.Clause,
            "ルビも圏点も列の右側に付く。右から左へ進む縦書きでは、右側が行の始まる側だからである"
            + "（jlreq §3.3.9）。縦書きの圏点はゴマ点で、基の字の右隣に置かれる。",
            Language: "ja", FirstLineIndent: 1),
        new(BlockKind.Example,
            "振り仮名", Reading: "ふ り が な", Distribution: RubyDistribution.Mono,
            Language: "ja", FirstLineIndent: 1),
        new(BlockKind.Example,
            "東京", Reading: "とう きょう", Distribution: RubyDistribution.Mono,
            Language: "ja", FirstLineIndent: 1),
        new(BlockKind.Example,
            "大切", Emphasis: EmphasisMarkStyle.SesameDot, Language: "ja", FirstLineIndent: 1),

        new(BlockKind.Clause,
            "列の先頭では、ルビが列の外へはみ出さないように本文が譲る。先頭のルビが基の文字より広いときは、"
            + "その分だけ本文が内側へ寄り、ルビの先頭が列の起点にそろう（jlreq §3.3.9）。",
            Language: "ja", FirstLineIndent: 1),
        new(BlockKind.Example,
            "承諾", Reading: "しょう だく", Distribution: RubyDistribution.Mono,
            Language: "ja", FirstLineIndent: 1),

        new(BlockKind.Clause,
            "欧文と数字は四分の一回転させて列に沿わせる。そのまま縦に組むと語が語でなくなるので、"
            + "語は語として組んでから回し、字を一列に並べて読ませる。",
            Language: "ja", FirstLineIndent: 1),
        new(BlockKind.Example,
            "この列の Godot 4.7 と SkiaSharp は回転していて、数字の 100 も同じ向きである。",
            Language: "ja", FirstLineIndent: 1),

        new(BlockKind.Clause,
            "禁則は同じく列にも働く。句点や読点は列の先頭に来られず、開き括弧は列の末尾に残らない。"
            + "一列に収まらない分は、前の一字とともに次の列へ送られる。",
            Language: "ja", FirstLineIndent: 1),
        new(BlockKind.Example,
            "この列も「、」と「。」が先頭に来ないように組んである。"
            + "「（一）はじめに」の開き括弧は、末尾に一つで残ったりしない。",
            Language: "ja", FirstLineIndent: 1),
    ];
}
