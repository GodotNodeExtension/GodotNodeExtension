namespace GodotNodeExtension.Component.Typography.Languages;

using System.Collections.Generic;
using Core.Model;

/// <summary>
/// One display form a language prefers over the code point that was written: the character to look for and the
/// character to draw instead.
/// <para>
/// The written character and the displayed form are two different things, and a language owns the second one:
/// the same quotation mark is <c>“”</c> in simplified Chinese, <c>「」</c> in traditional Chinese and Japanese,
/// and a Korean paragraph written with <c>。</c> is displayed with <c>.</c> in horizontal writing. Substitution
/// leaves the source text, its ranges and its character class alone — only what gets shaped changes.
/// </para>
/// </summary>
/// <param name="Source">Character as written.</param>
/// <param name="Target">Character to shape and draw instead.</param>
/// <param name="Reason">Why the language does this, for a dump or a test.</param>
public readonly record struct LetterformSubstitution(char Source, char Target, string Reason);

/// <summary>
/// Applies a language's display-form table to a segment's text.
/// </summary>
public static class LetterformResolver
{
    /// <summary>
    /// Rewrite the parts of <paramref name="text"/> that the table covers, or return null when nothing
    /// changes.
    /// <para>
    /// Only CJK punctuation is eligible. A quotation mark is a "common" code point whose role comes from the
    /// text around it, and the segmenter has already decided which side it belongs to: a mark it classified as
    /// Western punctuation belongs to Western text and must keep its Latin form even inside a Japanese
    /// paragraph.
    /// </para>
    /// </summary>
    /// <param name="text">Source text of one segment.</param>
    /// <param name="charClass">Character class the segment was classified with.</param>
    /// <param name="table">The language's substitution table; empty means nothing to do.</param>
    /// <returns>The display text, or null when the table changes nothing.</returns>
    public static string? Substitute(string text, CharacterClass charClass, IReadOnlyList<LetterformSubstitution> table)
    {
        if (string.IsNullOrEmpty(text) || table.Count == 0 || !IsCjkPunctuation(charClass))
            return null;

        char[]? replaced = null;

        for (int i = 0; i < text.Length; i++)
        {
            char source = text[i];

            foreach (LetterformSubstitution substitution in table)
            {
                if (substitution.Source != source)
                    continue;

                replaced ??= text.ToCharArray();
                replaced[i] = substitution.Target;
                break;
            }
        }

        return replaced is null ? null : new string(replaced);
    }

    /// <summary>
    /// Whether a character class is punctuation whose form a language may choose. Latin punctuation is not:
    /// its form is the Latin text's business.
    /// </summary>
    /// <param name="charClass">The class to test.</param>
    /// <returns>True when the class is CJK punctuation.</returns>
    public static bool IsCjkPunctuation(CharacterClass charClass) =>
        charClass is CharacterClass.PunctuationOpen or CharacterClass.PunctuationClose
            or CharacterClass.PunctuationPauseStop or CharacterClass.PunctuationEllipsis
            or CharacterClass.PunctuationDash or CharacterClass.PunctuationInterpunct;
}

/// <summary>
/// The display-form tables the shipped language profiles use.
/// <para>
/// Traditional Chinese and Japanese prefer the same corner brackets, but they are separate tables on purpose: a
/// language pack is data, and two languages agreeing today is not a reason to make them share a value. The
/// reasons quote the source of each rule so a reader of a dump can check it.
/// </para>
/// </summary>
internal static class LetterformSets
{
    /// <summary>Traditional Chinese: corner brackets, and the centered ellipsis (clreq §5.2, §5.1.4).</summary>
    internal static readonly LetterformSubstitution[] TraditionalChinese =
    [
        new('\u201C', '\u300C', "clreq: traditional Chinese opens with a corner bracket"),
        new('\u201D', '\u300D', "clreq: traditional Chinese closes with a corner bracket"),
        new('\u2018', '\u300E', "clreq: traditional Chinese nests with white corner brackets"),
        new('\u2019', '\u300F', "clreq: traditional Chinese nests with white corner brackets"),
        new('\u2026', '\u22EF', "clreq: the ellipsis is centered in the em box"),
    ];

    /// <summary>
    /// Japanese: the same corner brackets for quotation (jlreq §3.1.1). The ellipsis keeps its written form:
    /// jlreq places <c>……</c> on the baseline rather than re-centering it.
    /// </summary>
    internal static readonly LetterformSubstitution[] Japanese =
    [
        new('\u201C', '\u300C', "jlreq: Japanese opens with a corner bracket"),
        new('\u201D', '\u300D', "jlreq: Japanese closes with a corner bracket"),
        new('\u2018', '\u300E', "jlreq: Japanese nests with white corner brackets"),
        new('\u2019', '\u300F', "jlreq: Japanese nests with white corner brackets"),
    ];

    /// <summary>
    /// Korean: corner brackets for quotation, and the horizontal-writing sentence marks (klreq §6.1.2, §6.1.3
    /// — horizontal writing uses the narrow <c>.</c> and <c>,</c> rather than <c>。</c> and <c>、</c>).
    /// </summary>
    internal static readonly LetterformSubstitution[] Korean =
    [
        new('\u201C', '\u300C', "klreq: Korean opens with a corner bracket"),
        new('\u201D', '\u300D', "klreq: Korean closes with a corner bracket"),
        new('\u2018', '\u300E', "klreq: Korean nests with white corner brackets"),
        new('\u2019', '\u300F', "klreq: Korean nests with white corner brackets"),
        new('\u3002', '.', "klreq 6.1.2: horizontal writing uses a narrow full stop"),
        new('\u3001', ',', "klreq 6.1.2: horizontal writing uses a narrow comma"),
    ];
}
