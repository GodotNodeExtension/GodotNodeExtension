namespace GodotNodeExtension.Component.Typography.Core.Model;

/// <summary>
/// Character classification for CJK typography rules.
/// Based on huozi.js categories and W3C CLREQ §6.
/// </summary>
public enum CharacterClass
{
    /// <summary>CJK ideograph (Han character).</summary>
    Ideograph,

    /// <summary>Opening punctuation: （ 「 『 【 〔 ﹝ 《 〈 etc.</summary>
    PunctuationOpen,

    /// <summary>Closing punctuation: ） 」 』 】 〕 ﹞ 》 〉 etc.</summary>
    PunctuationClose,

    /// <summary>Pause/stop marks: ， 、 。 ： ； ！ ？ etc.</summary>
    PunctuationPauseStop,

    /// <summary>Two-em dash ⸺ / —— (unbreakable pair).</summary>
    PunctuationDash,

    /// <summary>Ellipsis …… (unbreakable pair).</summary>
    PunctuationEllipsis,

    /// <summary>Interpunct · (middle dot).</summary>
    PunctuationInterpunct,

    /// <summary>
    /// A Common punctuation character used in a Western context: a quotation mark, a dash or an ellipsis whose
    /// code point alone does not say which convention it obeys.
    /// <para>
    /// It is breakable after, is not prohibited at either line edge, and counts as Western when scripts meet —
    /// which is what an apostrophe inside <c>don’t</c> and the quotes around an English phrase in Chinese text
    /// need. The segmenter assigns it from context (see <see cref="Core.ContextualRoleResolver"/>).
    /// </para>
    /// </summary>
    PunctuationWestern,


    /// <summary>Latin letter, digit, or other Western character.</summary>
    Latin,

    /// <summary>Whitespace (space, tab).</summary>
    Space,

    /// <summary>A tabulation character. It carries no width of its own: where it lands is decided by the tab stops.</summary>
    Tab,

    /// <summary>Line break / paragraph break.</summary>
    Break,

    /// <summary>Non-text element (image, rect, extension, action).</summary>
    NonText,

    /// <summary>Atomic block-level element that cannot be split by the line breaker (fixed-size).</summary>
    Block,

    /// <summary>Start of an auto-size block. Content continues until a matching <see cref="BlockEnd"/>.</summary>
    BlockStart,

    /// <summary>End marker for an auto-size block started by <see cref="BlockStart"/>.</summary>
    BlockEnd,
}
