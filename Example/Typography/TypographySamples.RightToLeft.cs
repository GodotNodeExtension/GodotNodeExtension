
namespace GodotNodeExtension.Example.Typography;

/// <summary>
/// The right-to-left sample: the direction of a paragraph comes from its language, so an Arabic block fills its
/// lines from the right edge and a Hebrew block does the same. Latin words and digits inside them keep their own
/// direction, and paired brackets and quotation marks are drawn mirrored.
/// </summary>
public static partial class TypographySamples
{
    /// <summary>Blocks of the right-to-left sample.</summary>
    /// <returns>The blocks, in reading order.</returns>
    private static Block[] RightToLeft() =>
    [
        new(BlockKind.Heading, "الاتجاه من اليمين إلى اليسار: عربي وعِبري", Language: "ar"),

        new(BlockKind.Clause,
            "اتجاه الفقرة يأتي من اللغة نفسها: الفقرة العربية تُملأ من الحافة اليمنى نحو اليسار، "
            + "ولا يُختار الاتجاه يدويًا.",
            Language: "ar"),
        new(BlockKind.Example,
            "مثال: يبدأ السطر عند الهامش الأيمن ويمتد نحو اليسار. الأقواس «» و[ ] و( ) تنعكس عند الرسم، "
            + "فيقع الفتح يمينًا والغلق يسارًا، وتبقى الأرقام 2024 وكلمة Latin باتجاهها الأصلي داخل النص العربي.",
            Language: "ar"),

        new(BlockKind.Clause,
            "כיוון הפסקה נגזר מן השפה: פסקה בעברית נמלאת מן השוליים הימניים, ואין בוחרים את הכיוון ביד.",
            Language: "he"),
        new(BlockKind.Example,
            "דוגמה: השורה מתחילה בימין ונמשכת שמאלה. הסוגריים [ ] ו־( ) והמרכאות 「 」 נבנים בהשתקפות, "
            + "והמספר 42 והמלה Latin נשארים בכיוונם המקורי בתוך הטקסט העברי.",
            Language: "he"),
    ];
}
