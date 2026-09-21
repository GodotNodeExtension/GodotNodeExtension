namespace GodotNodeExtension.Component.Typography.Core;

/// <summary>
/// Number-and-unit atomicity: a number stays with the unit, measure word or ordinal prefix next to it.
/// <para>
/// This is not a language convention a script may opt out of — it is what makes <c>10米</c>, <c>5公斤</c> or
/// <c>第3号</c> readable — so it lives outside the language rules and both boundary rules consult it. The Unicode
/// algorithm already covers the Western cases (<c>10%</c>, <c>$10</c>) through LB25; these tables are the CJK ones.
/// </para>
/// <para>
/// Units are strings, not characters, because a unit is a word: <c>公斤</c> and <c>公里</c> must not be split
/// either, and the rule that protects them needs one character of look-behind — the character before the pair —
/// to tell a unit's second character from an unrelated word that happens to contain the same character.
/// </para>
/// <para>
/// The tables are data on purpose: adding a unit is a data change, which is what the architecture requires of
/// anything language-shaped.
/// </para>
/// </summary>
public static class NumberUnitBinding
{
    /// <summary>
    /// Measure words and units that bind to the number before them: length, weight, money, time and counting, in
    /// the simplified and traditional forms a CJK document commonly mixes.
    /// </summary>
    private static readonly string[] UnitStrings =
    [
        // length
        "公里", "厘米", "毫米", "米", "厘", "毫", "分", "寸", "丈", "尺", "里", "浬",
        // weight and volume
        "公斤", "克", "斤", "吨", "磅", "升", "斗", "加侖", "加仑", "毫升",
        // money
        "元", "角", "分", "圆", "圓", "塊", "块", "日元", "美元", "欧元", "欧", "円", "원",
        // time
        "年", "月", "日", "时", "分", "秒", "周", "週", "天", "号", "號", "期", "世纪",
        // counting and measure
        "个", "個", "只", "隻", "件", "条", "條", "张", "張", "页", "頁", "层", "層", "次", "间", "間",
        "部", "台", "套", "双", "雙", "对", "對", "群", "批", "组", "組", "倍", "回", "度", "遍",
        "点", "点儿", "點", "名", "位", "岁", "歲",
    ];

    /// <summary>Ordinal and approximation prefixes that bind to the number after them, such as 第.</summary>
    private const string PrefixChars = "第约約近逾超";

    /// <summary>Fractions that read as part of the number before them (三分之一).</summary>
    private const string FractionChars = "分之";

    /// <summary>
    /// Whether a character can start a unit, so a digit before it binds.
    /// </summary>
    /// <param name="character">The character to test.</param>
    /// <returns>True when a unit string starts with it.</returns>
    public static bool StartsUnit(char character)
    {
        foreach (string unit in UnitStrings)
        {
            if (unit.Length > 0 && unit[0] == character)
                return true;
        }

        return PrefixChars.Contains(character) || FractionChars.Contains(character);
    }

    /// <summary>
    /// Whether a pair of adjacent characters is a number with its unit or prefix, and must not be split.
    /// </summary>
    /// <param name="left">Character on the left.</param>
    /// <param name="right">Character on the right.</param>
    /// <returns>True when the two belong together.</returns>
    public static bool Binds(char left, char right) => Binds('\0', left, right);

    /// <summary>
    /// Whether a pair of adjacent characters must not be split, given the character before the pair.
    /// <para>
    /// The third character is what makes a multi-character unit safe: the pair <c>公斤</c> is only part of a number
    /// when a digit precedes it, and <c>办公</c> must stay breakable.
    /// </para>
    /// </summary>
    /// <param name="beforeLeft">Character before <paramref name="left"/>, or '\0' when unknown.</param>
    /// <param name="left">Character on the left of the position.</param>
    /// <param name="right">Character on the right of the position.</param>
    /// <returns>True when the two belong together.</returns>
    public static bool Binds(char beforeLeft, char left, char right)
    {
        // A digit followed by a unit's first character: 10米, 5公斤, 100元, 3月.
        if (IsAsciiDigit(left) && StartsUnit(right))
            return true;

        // A prefix followed by a digit: 第3号.
        if ((PrefixChars.Contains(left) || FractionChars.Contains(left)) && IsAsciiDigit(right))
            return true;

        // Inside a multi-character unit whose number precedes it: 5公斤, 10公里.
        if (IsAsciiDigit(beforeLeft) && ContinuesUnit(left, right))
            return true;

        return false;
    }

    /// <summary>
    /// Whether a pair of characters is inside one of the unit words, which keeps a unit like 公斤 together.
    /// </summary>
    /// <param name="left">Character on the left of the position.</param>
    /// <param name="right">Character on the right of the position.</param>
    /// <returns>True when some unit word contains the pair.</returns>
    private static bool ContinuesUnit(char left, char right)
    {
        foreach (string unit in UnitStrings)
        {
            if (unit.Length < 2)
                continue;

            for (int i = 0; i + 1 < unit.Length; i++)
            {
                if (unit[i] == left && unit[i + 1] == right)
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether a character is an ASCII digit. CJK numeric characters (三, 十) are words rather than numbers, and a
    /// unit after them is not bound by this rule.
    /// </summary>
    /// <param name="character">The character to test.</param>
    /// <returns>True for '0'-'9'.</returns>
    private static bool IsAsciiDigit(char character) => character is >= '0' and <= '9';
}
