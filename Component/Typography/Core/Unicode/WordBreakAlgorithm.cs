using System;

namespace GodotNodeExtension.Component.Typography.Core.Unicode;

/// <summary>
/// Unicode word boundaries (UAX #29): decides where one word ends and the next begins.
/// <para>
/// This is what lets a language that separates words with spaces be laid out correctly, and it is also
/// the foundation for languages that do not (Thai and friends need a dictionary on top, which is a
/// later, language-owned step). Like the line breaking algorithm it is the untailored, specification
/// conformant form, checked against Unicode's own conformance suite.
/// </para>
/// <para>
/// The class table comes from <see cref="WordBreakData"/>, generated from <c>WordBreakProperty.txt</c>.
/// </para>
/// </summary>
public static class WordBreakAlgorithm
{
    /// <summary>
    /// Whether a word boundary falls before <paramref name="index"/>, where <paramref name="index"/> is
    /// the position between the code point at <c>index - 1</c> and the one at <c>index</c>.
    /// </summary>
    /// <param name="codePoints">The whole text as code points.</param>
    /// <param name="index">Boundary position, 1..<c>codePoints.Length - 1</c>.</param>
    /// <returns>True when a word boundary occurs there.</returns>
    /// <exception cref="ArgumentOutOfRangeException">When the index is not inside the text.</exception>
    public static bool IsBoundary(ReadOnlySpan<int> codePoints, int index)
    {
        if (index <= 0 || index >= codePoints.Length)
            throw new ArgumentOutOfRangeException(nameof(index));

        WordBreakValue left = Data(codePoints[index - 1]);
        WordBreakValue right = Data(codePoints[index]);

        // WB3: CR × LF.
        if (left == WordBreakValue.CR && right == WordBreakValue.LF)
            return false;

        // WB3a / WB3b: a newline never attaches to its neighbours.
        if (IsNewline(left) || IsNewline(right))
            return true;

        // WB3d: horizontal whitespace runs together.
        if (left == WordBreakValue.WSegSpace && right == WordBreakValue.WSegSpace)
            return false;

        // WB3c: a zero width joiner glues an emoji sequence together, so there is no boundary between it
        // and the pictograph that follows.
        if (left == WordBreakValue.ZWJ
            && ExtendedPictographicData.Lookup(codePoints[index]) is ExtendedPictographicValue.Y)
        {
            return false;
        }

        // WB4: ignore format characters and extenders — they belong to the character they follow.
        if (right is WordBreakValue.Extend or WordBreakValue.Format or WordBreakValue.ZWJ)
            return false;

        // Resolve the left neighbour past any extenders, and the right neighbour's following character
        // for the rules that look one further (WB6, WB7b, WB12).
        int leftIndex = SkipExtenders(codePoints, index - 1);
        left = Data(codePoints[leftIndex]);

        int rightIndex = SkipExtendersForward(codePoints, index);
        right = Data(codePoints[rightIndex]);

        int nextIndex = SkipExtendersForward(codePoints, rightIndex + 1);
        WordBreakValue rightNext = nextIndex < codePoints.Length
            ? Data(codePoints[nextIndex])
            : WordBreakValue.Other;

        int beforeLeftIndex = leftIndex - 1;
        WordBreakValue leftPrev = beforeLeftIndex >= 0
            ? Data(codePoints[SkipExtenders(codePoints, beforeLeftIndex)])
            : WordBreakValue.Other;

        // WB5: letters attach to letters.
        if (IsALetter(left) && IsALetter(right))
            return false;

        // WB6 / WB7: a single internal separator between letters does not split them.
        if (IsALetter(left) && IsMidLetterOrQuote(right) && IsALetter(rightNext))
            return false;

        if (IsALetter(leftPrev) && IsMidLetterOrQuote(left) && IsALetter(right))
            return false;

        // WB7a: a Hebrew letter keeps a trailing single quote.
        if (left == WordBreakValue.Hebrew_Letter && right == WordBreakValue.Single_Quote)
            return false;

        // WB7b / WB7c: a double quote inside a Hebrew word.
        if (left == WordBreakValue.Hebrew_Letter && right == WordBreakValue.Double_Quote
            && rightNext == WordBreakValue.Hebrew_Letter)
        {
            return false;
        }

        if (leftPrev == WordBreakValue.Hebrew_Letter && left == WordBreakValue.Double_Quote
            && right == WordBreakValue.Hebrew_Letter)
        {
            return false;
        }

        // WB8 / WB9 / WB10: numbers and letters attach.
        if (left == WordBreakValue.Numeric && right == WordBreakValue.Numeric)
            return false;

        if (IsALetter(left) && right == WordBreakValue.Numeric)
            return false;

        if (left == WordBreakValue.Numeric && IsALetter(right))
            return false;

        // WB11 / WB12: a thousands or decimal separator inside a number.
        if (leftPrev == WordBreakValue.Numeric && IsMidNumber(left) && right == WordBreakValue.Numeric)
            return false;

        if (left == WordBreakValue.Numeric && IsMidNumber(right) && rightNext == WordBreakValue.Numeric)
            return false;

        // WB13: Katakana runs together.
        if (left == WordBreakValue.Katakana && right == WordBreakValue.Katakana)
            return false;

        // WB13a / WB13b: the extender class sticks to words on either side.
        if (IsWordish(left) && right == WordBreakValue.ExtendNumLet)
            return false;

        if (left == WordBreakValue.ExtendNumLet && IsWordish(right))
            return false;

        // WB15 / WB16: regional indicators pair up (flags), so only every second one breaks.
        if (left == WordBreakValue.Regional_Indicator && right == WordBreakValue.Regional_Indicator)
        {
            // WB15/WB16: an odd run before the position means the position is inside a pair, and a pair is
            // a single word (a flag), so there is no boundary.
            return !OddRegionalIndicatorRun(codePoints, index);
        }

        // WB999: otherwise every character is its own word — which is what makes this usable for CJK,
        // where each ideograph is a word, and for scripts that need a dictionary on top.
        return true;
    }

    /// <summary>Whether a code point is a hard line break class.</summary>
    /// <param name="value">The word break class.</param>
    /// <returns>True for CR, LF and Newline.</returns>
    private static bool IsNewline(WordBreakValue value) =>
        value is WordBreakValue.CR or WordBreakValue.LF or WordBreakValue.Newline;

    /// <summary>Whether a class participates in words as a letter (AHLetter in UAX #29).</summary>
    /// <param name="value">The word break class.</param>
    /// <returns>True for ALetter and Hebrew_Letter.</returns>
    private static bool IsALetter(WordBreakValue value) =>
        value is WordBreakValue.ALetter or WordBreakValue.Hebrew_Letter;

    /// <summary>Whether a class is a letter-internal separator (MidLetter or MidNumLet or a quote).</summary>
    /// <param name="value">The word break class.</param>
    /// <returns>True for MidLetter, MidNumLet and Single_Quote.</returns>
    private static bool IsMidLetterOrQuote(WordBreakValue value) =>
        value is WordBreakValue.MidLetter or WordBreakValue.MidNumLet or WordBreakValue.Single_Quote;

    /// <summary>Whether a class is a number-internal separator.</summary>
    /// <param name="value">The word break class.</param>
    /// <returns>True for MidNum, MidNumLet and Single_Quote.</returns>
    private static bool IsMidNumber(WordBreakValue value) =>
        value is WordBreakValue.MidNum or WordBreakValue.MidNumLet or WordBreakValue.Single_Quote;

    /// <summary>
    /// Whether a class is one the extender class attaches to (WB13a's left side, which includes the
    /// extender class itself so that runs like "A__A" stay one word).
    /// </summary>
    /// <param name="value">The word break class.</param>
    /// <returns>True for letters, numbers, Katakana and ExtendNumLet.</returns>
    private static bool IsWordish(WordBreakValue value) =>
        IsALetter(value)
        || value is WordBreakValue.Numeric or WordBreakValue.Katakana or WordBreakValue.ExtendNumLet;

    /// <summary>
    /// Walk left over extenders and format characters, so a rule sees the character a cluster really
    /// attaches to.
    /// </summary>
    /// <param name="codePoints">The text as code points.</param>
    /// <param name="index">Index to start from.</param>
    /// <returns>The index of the cluster's first code point.</returns>
    private static int SkipExtenders(ReadOnlySpan<int> codePoints, int index)
    {
        while (index > 0 && IsIgnorable(Data(codePoints[index])))
        {
            WordBreakValue previous = Data(codePoints[index - 1]);

            // WB4's exception: at the start of the text, or after a newline, an extender is not ignored.
            if (IsNewline(previous))
                break;

            index--;
        }

        return index;
    }

    /// <summary>
    /// Walk right over extenders, for the rules that look at the character following a separator.
    /// </summary>
    /// <param name="codePoints">The text as code points.</param>
    /// <param name="index">Index to start from.</param>
    /// <returns>The index of the next non-ignorable code point, or the text length.</returns>
    private static int SkipExtendersForward(ReadOnlySpan<int> codePoints, int index)
    {
        while (index < codePoints.Length && IsIgnorable(Data(codePoints[index])))
            index++;

        return index;
    }

    /// <summary>Whether a class is ignored by WB4.</summary>
    /// <param name="value">The word break class.</param>
    /// <returns>True for Extend, Format and ZWJ.</returns>
    private static bool IsIgnorable(WordBreakValue value) =>
        value is WordBreakValue.Extend or WordBreakValue.Format or WordBreakValue.ZWJ;

    /// <summary>
    /// WB15/WB16: whether the run of regional indicators ending before this position has odd length, so
    /// the position falls between a pair.
    /// </summary>
    /// <param name="codePoints">The text as code points.</param>
    /// <param name="index">Boundary position.</param>
    /// <returns>True when the preceding run length is odd.</returns>
    private static bool OddRegionalIndicatorRun(ReadOnlySpan<int> codePoints, int index)
    {
        int count = 0;

        for (int i = index - 1; i >= 0; i--)
        {
            if (IsIgnorable(Data(codePoints[i])))
                continue;

            if (Data(codePoints[i]) != WordBreakValue.Regional_Indicator)
                break;

            count++;
        }

        return (count & 1) == 1;
    }

    /// <summary>Look up the word break class of a code point.</summary>
    /// <param name="codePoint">The code point to classify.</param>
    /// <returns>The word break class.</returns>
    private static WordBreakValue Data(int codePoint) => WordBreakData.Lookup(codePoint);
}
