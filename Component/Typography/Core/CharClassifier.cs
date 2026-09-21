using GodotNodeExtension.Component.Typography.Core.Model;
using GodotNodeExtension.Component.Typography.Languages;

namespace GodotNodeExtension.Component.Typography.Core;

/// <summary>
/// Classifies Unicode characters for CJK typography rules.
/// Based on huozi.js classification and W3C CLREQ §6.
/// </summary>
public static class CharClassifier
{
    // ── Opening punctuation characters ──
    internal const string OpenPunctuation =
        "\u2018\u201C\u3008\u300A\u300C\u300E\u3010\u3014\u3016\u3018\u301A" +
        "\uFF08\uFF3B\uFF5B\uFE59\uFE5B\uFE5D\uFE35\uFE37\uFE39\uFE3B\uFE3D\uFE3F\uFE41\uFE43\uFE47";

    // ── Closing punctuation characters ──
    internal const string ClosePunctuation =
        "\u2019\u201D\u3009\u300B\u300D\u300F\u3011\u3015\u3017\u3019\u301B" +
        "\uFF09\uFF3D\uFF5D\uFE5A\uFE5C\uFE5E\uFE36\uFE38\uFE3A\uFE3C\uFE3E\uFE40\uFE42\uFE44\uFE48";

    // ── Pause/stop punctuation ──
    internal const string PauseStopPunctuation =
        "\uFF0C\u3001\u3002\uFF1A\uFF1B\uFF01\uFF1F";

    // The prohibition sets themselves live in KinsokuClassSets: they are language data, and the engine used to
    // carry one hard-coded pair here, which made every language Chinese by construction.

    /// <summary>
    /// Classify a single character into a <see cref="CharacterClass"/>.
    /// </summary>
    public static CharacterClass Classify(char c)
    {
        if (c == '\n' || c == '\r')
            return CharacterClass.Break;

        if (c == ' ')
            return CharacterClass.Space;

        if (c == '\t')
            return CharacterClass.Tab;

        if (OpenPunctuation.Contains(c))
            return CharacterClass.PunctuationOpen;

        if (ClosePunctuation.Contains(c))
            return CharacterClass.PunctuationClose;

        if (PauseStopPunctuation.Contains(c))
            return CharacterClass.PunctuationPauseStop;

        // Em dash U+2014 and Two-em dash U+2E3A
        if (c == '\u2014' || c == '\u2E3A')
            return CharacterClass.PunctuationDash;

        // Horizontal ellipsis U+2026 and Two-dot leader U+2025
        if (c == '\u2026' || c == '\u2025')
            return CharacterClass.PunctuationEllipsis;

        // Interpunct: middle dot U+00B7 and CJK middle dot U+30FB
        if (c == '\u00B7' || c == '\u30FB')
            return CharacterClass.PunctuationInterpunct;

        if (IsCjkIdeograph(c))
            return CharacterClass.Ideograph;

        // Default: Latin or other script
        return CharacterClass.Latin;
    }

    /// <summary>
    /// Check if a character is prohibited at line start according to the specified level.
    /// </summary>
    /// <param name="c">Character to test.</param>
    /// <param name="level">Strictness level.</param>
    /// <returns>True when the character may not start a line.</returns>
    public static bool IsProhibitedAtLineStart(char c, ProhibitionLevel level) =>
        IsProhibitedAtLineStart(c, KinsokuClassSets.Legacy, level);

    /// <summary>
    /// Check if a character is prohibited at line start under one language's classes.
    /// <para>
    /// The classes are the language's (see <see cref="KinsokuClassSet"/>), and the level is shared by all of them:
    /// Chinese distinguishes four cumulative levels, Japanese and Korean one set each. Reading the set from the
    /// language is what lets a Japanese document stop at a small kana while a Chinese one does not.
    /// </para>
    /// </summary>
    /// <param name="c">Character to test.</param>
    /// <param name="set">Classes the language prohibits.</param>
    /// <param name="level">Strictness level.</param>
    /// <returns>True when the character may not start a line.</returns>
    public static bool IsProhibitedAtLineStart(char c, KinsokuClassSet set, ProhibitionLevel level) =>
        level != ProhibitionLevel.None && set.LineStartAt(level).Contains(c);

    /// <summary>
    /// Check if a character is prohibited at line end.
    /// </summary>
    /// <param name="c">Character to test.</param>
    /// <param name="level">Strictness level.</param>
    /// <returns>True when the character may not end a line.</returns>
    public static bool IsProhibitedAtLineEnd(char c, ProhibitionLevel level) =>
        IsProhibitedAtLineEnd(c, KinsokuClassSets.Legacy, level);

    /// <summary>
    /// Check if a character is prohibited at line end under one language's classes.
    /// </summary>
    /// <param name="c">Character to test.</param>
    /// <param name="set">Classes the language prohibits.</param>
    /// <param name="level">Strictness level.</param>
    /// <returns>True when the character may not end a line.</returns>
    public static bool IsProhibitedAtLineEnd(char c, KinsokuClassSet set, ProhibitionLevel level) =>
        level != ProhibitionLevel.None && set.LineEndAt(level).Contains(c);

    /// <summary>
    /// Check if two adjacent characters form an unbreakable pair
    /// (must not be split across lines).
    /// </summary>
    public static bool IsUnbreakablePair(char left, char right)
    {
        // Em dash pair: —— (U+2014 U+2014) or ⸺⸺ (U+2E3A U+2E3A)
        if (left == '\u2014' && right == '\u2014') return true;
        if (left == '\u2E3A' && right == '\u2E3A') return true;

        // Ellipsis pair: …… (U+2026 U+2026)
        if (left == '\u2026' && right == '\u2026') return true;

        // Digit followed by percentage/degree/currency suffix
        if (char.IsDigit(left) && IsNumericSuffix(right)) return true;

        // Currency/sign prefix followed by digit
        if (IsNumericPrefix(left) && char.IsDigit(right)) return true;

        // Consecutive digits (don't break number sequences)
        if (char.IsDigit(left) && char.IsDigit(right)) return true;

        // Digit and decimal point
        if (char.IsDigit(left) && right == '.') return true;
        if (left == '.' && char.IsDigit(right)) return true;

        // Digit and comma (thousands separator)
        if (char.IsDigit(left) && right == ',') return true;
        if (left == ',' && char.IsDigit(right)) return true;

        return false;
    }

    /// <summary>
    /// Get the adjustable space for a character (in ems) that can be compressed.
    /// Returns 0 for non-adjustable characters.
    /// </summary>
    /// <param name="c">The character to look up.</param>
    /// <returns>The adjustable space in ems.</returns>
    public static float GetAdjustableSpace(char c)
    {
        var cls = Classify(c);

        return cls switch
        {
            // Opening/closing brackets: full-width → half-width (0.5em adjustable)
            CharacterClass.PunctuationOpen => 0.5f,
            CharacterClass.PunctuationClose => 0.5f,

            // Pause/stop: 0.5em adjustable, and the same 0.5em when it is compressed at the end of a line
            CharacterClass.PunctuationPauseStop => 0.5f,

            _ => 0f,
        };
    }

    /// <summary>
    /// Check if a character is a CJK Unified Ideograph or common CJK range.
    /// </summary>
    private static bool IsCjkIdeograph(char c)
    {
        // CJK Unified Ideographs: U+4E00 – U+9FFF
        if (c is >= '\u4E00' and <= '\u9FFF') return true;

        // CJK Unified Ideographs Extension A: U+3400 – U+4DBF
        if (c is >= '\u3400' and <= '\u4DBF') return true;

        // CJK Compatibility Ideographs: U+F900 – U+FAFF
        if (c is >= '\uF900' and <= '\uFAFF') return true;

        // Hiragana U+3040 – U+309F
        if (c is >= '\u3040' and <= '\u309F') return true;

        // Katakana U+30A0 – U+30FF
        if (c is >= '\u30A0' and <= '\u30FF') return true;

        // Hangul Syllables U+AC00 – U+D7AF
        if (c is >= '\uAC00' and <= '\uD7AF') return true;

        // CJK Symbols and Punctuation U+3000 – U+303F (excluding already classified punctuation)
        // Only ideographic space U+3000 and ideographic comma/period handled elsewhere
        if (c == '\u3000') return false; // Ideographic space → Space

        // Bopomofo U+3100 – U+312F
        if (c is >= '\u3100' and <= '\u312F') return true;

        // Fullwidth Latin letters and digits: U+FF01 – U+FF60
        // These are treated as CJK-width characters for layout purposes
        if (c is >= '\uFF01' and <= '\uFF60')
        {
            // Already classified as punctuation above; remaining fullwidth chars are ideograph-width
            if (!OpenPunctuation.Contains(c) && !ClosePunctuation.Contains(c) && !PauseStopPunctuation.Contains(c))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Check if a character is a numeric suffix that should not be separated from digits.
    /// </summary>
    private static bool IsNumericSuffix(char c) =>
        c is '%' or '\u2030' or '\u2031' or '\u00B0' or '\u2103' or '\u2109';

    /// <summary>
    /// Check if a character is a numeric prefix that should not be separated from digits.
    /// </summary>
    private static bool IsNumericPrefix(char c) =>
        c is '\u00A5' or '$' or '\u20AC' or '\u00A3' or '#';
}
