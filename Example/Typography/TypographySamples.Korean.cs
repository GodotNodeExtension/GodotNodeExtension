
namespace GodotNodeExtension.Example.Typography;

/// <summary>
/// The Korean sample: what klreq requires of Hangul text, each clause followed by the layout meeting it.
/// </summary>
public static partial class TypographySamples
{
    /// <summary>Blocks of the Korean sample.</summary>
    /// <returns>The blocks, in reading order.</returns>
    private static Block[] Korean() =>
    [
        new(BlockKind.Heading, "한국어 조판 규범(klreq)", Language: "ko"),

        new(BlockKind.Clause,
            "줄머리 금지(klreq §7.1.2): 닫는 괄호, 붙임표, 느낌표와 물음표, 가운데점, 쉼표와 마침표, "
            + "되풀이표, 장음표는 줄의 첫머리에 놓을 수 없다. 그런 표시가 줄머리에 홀로 남을 자리라면 "
            + "앞 글자를 함께 끌어내려 두 글자를 한 줄에 묶거나, 표시를 앞 줄 끝으로 밀어 넣는다. "
            + "아래 문단은 너비를 좁게 잡아 이 표시들이 줄머리에 닿도록 만든 것이다.",
            Language: "ko", FirstLineIndent: 1),
        new(BlockKind.Example,
            "너비를 좁게 잡으면 마침표와 쉼표가 줄머리에 닿는다。 그런데도 이 문단의 줄은 그런 표시로 시작하지 않는다、 "
            + "왜냐하면 그 표시는 앞 줄 끝으로 밀려 들어가고、 홀로 남을 수 없는 표시는 앞 글자와 함께 다음 줄로 "
            + "내려가기 때문이다。 줄끝 금지도 같은 이치여서、 여는 괄호는 뒤에 오는 글자와 함께 다음 줄로 "
            + "간다(klreq §7.1.3)。",
            Language: "ko", FirstLineIndent: 1),

        new(BlockKind.Clause,
            "가로쓰기의 좁은 문장 부호(klreq §6.1.2, §6.1.3): 가로쓰기에서는 문장 부호를 좁은 꼴로 그린다. "
            + "전각 마침표를 적어 두어도 가로쓰기에서는 반각 마침표로 나오고, 전각 쉼표를 적어 두어도 "
            + "반각 쉼표로 나온다. 좁은 꼴은 한 글자 폭을 다 쓰지 않으므로 뒤에 오는 글자와의 사이에 빈 칸을 "
            + "따로 넣지 않아도 되고, 줄 끝에서는 그만큼 자리가 남는다.",
            Language: "ko", FirstLineIndent: 1),
        new(BlockKind.Example,
            "적어 둔 꼴과 그려진 꼴이 다르다。 이 문단의 마침표와 쉼표는 전각 부호로 적었지만 화면에는 좁은 "
            + "꼴로 나온다、 그래서 좁은 자리에는 좁은 부호를 따로 적어 둘 필요가 없다。",
            Language: "ko", FirstLineIndent: 1),

        new(BlockKind.Clause,
            "한글과 漢字, 한글과 로마자가 한 줄에 섞이면 글자와 글자 사이에 틈이 하나 들어간다(klreq §7.3.2). "
            + "여기서는 그 틈을 4분의 1 em로 그린다. 한글과 한자처럼 폭이 같은 글자끼리는 서로의 자리를 "
            + "그대로 지키고, 이 틈은 줄을 맞출 때 조금 늘거나 줄어들 수 있다.",
            Language: "ko", FirstLineIndent: 1),
        new(BlockKind.Example,
            "한글과 漢字, 그리고 Latin 글자를 한 줄에 섞어 놓으면 글자 사이마다 4분의 1 em의 틈이 생긴다. "
            + "Godot 4.7과 SkiaSharp, HarfBuzz를 나란히 놓아도 같은 틈이 생기고, 2024년이나 3시 30분처럼 "
            + "숫자와 한글이 붙는 자리에도 같은 틈이 들어간다. 이 틈은 줄을 맞출 때 늘었다 줄었다 하는 "
            + "여유 있는 값이다.",
            Language: "ko", FirstLineIndent: 1),

        new(BlockKind.Clause,
            "주음(덧말): klreq는 한글에 주음을 다는 방법을 규정하지 않는다. 그래서 이 문서에는 주음이 없고, "
            + "덧말을 어디에 어떻게 다는지는 klreq가 아니라 별도의 루비 규범이 정할 일이다. "
            + "정하지 않은 것을 이 자리에서 지어내지는 않는다.",
            Language: "ko", FirstLineIndent: 1),
    ];
}
