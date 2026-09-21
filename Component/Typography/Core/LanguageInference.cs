using System;

namespace GodotNodeExtension.Component.Typography.Core;

/// <summary>
/// What script a piece of text is written in, as far as that can be told from the characters alone.
/// <para>
/// A document's language is declared once and governs the whole request, but the text inside it is not always one
/// language: a Traditional Chinese document that quotes a Japanese sentence, or a Japanese one with a paragraph of
/// Chinese, is ordinary. Everything that follows the language - which side an annotation goes on, how an annotation
/// is set, line breaking, prohibition, hyphenation - is then wrong for that paragraph unless the text is read for
/// what it is. This type answers only the question the characters themselves answer ("does this text carry kana, or
/// Hangul, or neither"), which is enough to tell Japanese and Korean apart from the Han-only languages, and it
/// deliberately guesses nothing about Han text: Simplified and Traditional Chinese share one script, so a Han-only
/// paragraph keeps whatever the request declared.
/// </para>
/// </summary>
internal static class LanguageInference
{
    /// <summary>
    /// Infer the language of a paragraph from its text, or null when the characters do not say.
    /// </summary>
    /// <param name="text">The paragraph's text.</param>
    /// <returns>A BCP-47 tag ("ja" or "ko"), or null when the text is not written in a script that identifies one.</returns>
    public static string? FromText(ReadOnlySpan<char> text)
    {
        foreach (char c in text)
        {
            if (IsJapanese(c))
                return "ja";

            if (IsKorean(c))
                return "ko";
        }

        return null;
    }

    /// <summary>
    /// Whether a character is one of the Japanese syllabaries: kana (including the prolonged sound mark, which
    /// belongs to the katakana block) is what separates Japanese from Chinese text - both write Han characters, only
    /// one of them writes kana.
    /// </summary>
    /// <param name="c">Character to test.</param>
    /// <returns>True for hiragana, katakana and half-width katakana.</returns>
    private static bool IsJapanese(char c) =>
        (c >= '\u3040' && c <= '\u30FF')     // hiragana + katakana (includes U+30FC, the prolonged sound mark)
        || (c >= '\uFF66' && c <= '\uFF9D'); // half-width katakana

    /// <summary>
    /// Whether a character is Hangul: its presence is what separates Korean from Chinese and Japanese text.
    /// </summary>
    /// <param name="c">Character to test.</param>
    /// <returns>True for Hangul syllables and the Hangul Jamo blocks.</returns>
    private static bool IsKorean(char c) =>
        (c >= '\uAC00' && c <= '\uD7A3')     // Hangul syllables
        || (c >= '\u1100' && c <= '\u11FF')  // Hangul Jamo
        || (c >= '\u3130' && c <= '\u318F')  // Hangul compatibility Jamo
        || (c >= '\uA960' && c <= '\uA97F'); // Hangul Jamo extended-A
}
