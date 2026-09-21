using System;
using System.Globalization;

namespace GodotNodeExtension.Component.Typography.Core.Unicode;

/// <summary>
/// Unicode line breaking (UAX #14): decides where a line may end between two adjacent code points.
/// <para>
/// This is the untailored, specification-conformant algorithm, checked against Unicode's own conformance
/// suite. It answers "may I break here" and "must I break here" for the default rules; a language then
/// tailors the answer — CJK prohibition rules are a tailoring, and so is refusing to break inside a
/// number with a unit. Keeping the untailored form separate is what makes it testable: a tailoring is a
/// decision about an exception, not a rewrite of the rules.
/// </para>
/// <para>
/// The class table comes from <see cref="LineBreakData"/> (generated from <c>LineBreak.txt</c>) and the
/// East Asian width from <see cref="EastAsianWidthData"/>; both carry the Unicode version they came from.
/// </para>
/// </summary>
public static class LineBreakAlgorithm
{
    /// <summary>
    /// Whether a line break may occur between two code points, with no surrounding text.
    /// </summary>
    /// <param name="before">Code point on the left of the position.</param>
    /// <param name="after">Code point on the right of the position.</param>
    /// <returns>A break opportunity, and whether it is mandatory.</returns>
    public static LineBreakOpportunity OpportunityBetween(int before, int after)
    {
        Span<int> pair = [before, after];
        return Decide(pair, 1);
    }

    /// <summary>
    /// Whether a line break may occur at a position in a text.
    /// <para>
    /// Many rules look beyond the pair — spaces attached to a preceding character, quotation marks,
    /// numbers and Brahmic syllables — so the decision needs the surrounding text, not just two code
    /// points.
    /// </para>
    /// </summary>
    /// <param name="codePoints">The whole text as code points.</param>
    /// <param name="index">Position between the code points at <c>index - 1</c> and <c>index</c>.</param>
    /// <returns>A break opportunity, and whether it is mandatory.</returns>
    /// <exception cref="ArgumentOutOfRangeException">When the index is not inside the text.</exception>
    public static LineBreakOpportunity OpportunityAt(ReadOnlySpan<int> codePoints, int index)
    {
        if (index <= 0 || index >= codePoints.Length)
            throw new ArgumentOutOfRangeException(nameof(index));

        return Decide(codePoints, index);
    }

    /// <summary>
    /// Whether this position is a mandatory break (a hard line break on the left).
    /// </summary>
    /// <param name="codePoint">Code point on the left of the position.</param>
    /// <returns>True when the text must break here.</returns>
    public static bool IsMandatoryAfter(int codePoint) =>
        Resolve(codePoint) is LineBreakValue.BK or LineBreakValue.CR or LineBreakValue.LF
            or LineBreakValue.NL;

    /// <summary>
    /// Resolve a code point to its line break class, applying the LB1 class aliases.
    /// </summary>
    /// <param name="codePoint">The code point to classify.</param>
    /// <returns>The resolved class.</returns>
    public static LineBreakValue ClassOf(int codePoint) => Resolve(codePoint);

    /// <summary>
    /// The dotted circle, written <c>[◌]</c> in the LB28a patterns: a placeholder base character for an
    /// orthographic syllable. The conformance suite labels it on its own, which is how it is identified
    /// here rather than as a class.
    /// </summary>
    private const int DottedCircle = 0x25CC;

    /// <summary>
    /// Apply the rules at one position. The order follows the specification's rule order, because the
    /// first matching rule decides — which is why this reads as a cascade.
    /// </summary>
    /// <param name="text">The whole text as code points.</param>
    /// <param name="index">Position to decide.</param>
    /// <returns>The decision.</returns>
    private static LineBreakOpportunity Decide(ReadOnlySpan<int> text, int index)
    {
        int beforeCode = text[index - 1];
        int afterCode = text[index];

        // Rules that ask about the left character's own properties (its East Asian width, its Hangul
        // syllable type, whether it is an unassigned pictograph) must ask about the base character, not
        // about a combining mark that folded into it.
        int beforeBaseCode = text[BaseIndex(text, index - 1)];

        LineBreakValue left = EffectiveClass(text, index - 1);
        LineBreakValue right = Resolve(afterCode);

        // LB9 / LB10: a combining mark or zero width joiner belongs to the character before it, and only
        // becomes a letter-like class of its own when there is nothing to attach to.
        if (right is LineBreakValue.CM or LineBreakValue.ZWJ)
        {
            LineBreakValue leftRaw = Resolve(beforeCode);

            if (leftRaw is LineBreakValue.BK or LineBreakValue.CR or LineBreakValue.LF
                or LineBreakValue.NL or LineBreakValue.SP or LineBreakValue.ZW)
            {
                right = LineBreakValue.AL;
            }
            else
            {
                return LineBreakOpportunity.Prohibited;
            }
        }

        // LB4: a hard break is mandatory.
        if (left is LineBreakValue.BK)
            return LineBreakOpportunity.Mandatory;

        // LB5: CR LF is one break; a lone CR, LF or NL is mandatory.
        if (left is LineBreakValue.CR && right is LineBreakValue.LF)
            return LineBreakOpportunity.Prohibited;

        if (left is LineBreakValue.CR or LineBreakValue.LF or LineBreakValue.NL)
            return LineBreakOpportunity.Mandatory;

        // LB6: nothing attaches to a hard break.
        if (right is LineBreakValue.BK or LineBreakValue.CR or LineBreakValue.LF or LineBreakValue.NL)
            return LineBreakOpportunity.Prohibited;

        // LB7: no break before a space or a zero width space.
        if (right is LineBreakValue.SP or LineBreakValue.ZW)
            return LineBreakOpportunity.Prohibited;

        // LB8: a zero width space allows a break after itself, spaces in between notwithstanding.
        if (RunEndsWith(text, index - 1, LineBreakValue.ZW, skipSpaces: true, out int zwIndex)
            && zwIndex >= 0)
        {
            return LineBreakOpportunity.Allowed;
        }

        // LB8a: no break after a zero width joiner. The class is read raw here rather than through the
        // LB9 folding below: LB8a comes before LB9 in the rule order and is about the literal joiner.
        if (Resolve(beforeCode) is LineBreakValue.ZWJ)
            return LineBreakOpportunity.Prohibited;

        // LB11: a word joiner binds on both sides.
        if (left is LineBreakValue.WJ || right is LineBreakValue.WJ)
            return LineBreakOpportunity.Prohibited;

        // LB12: a non-breaking glue does not break after itself.
        if (left is LineBreakValue.GL)
            return LineBreakOpportunity.Prohibited;

        // LB12a: only a space, a hyphen (HH or HY) or a break-after may precede a non-breaking glue.
        if (right is LineBreakValue.GL
            && left is not (LineBreakValue.SP or LineBreakValue.BA or LineBreakValue.HY
                or LineBreakValue.HH))
        {
            return LineBreakOpportunity.Prohibited;
        }

        // LB13: nothing attaches to a closing bracket, an exclamation mark or a symbol.
        if (right is LineBreakValue.CL or LineBreakValue.CP or LineBreakValue.EX or LineBreakValue.SY)
            return LineBreakOpportunity.Prohibited;

        // LB14: an opening bracket does not break after itself, even across spaces.
        if (RunEndsWith(text, index - 1, LineBreakValue.OP, skipSpaces: true, out int opIndex)
            && opIndex >= 0)
        {
            return LineBreakOpportunity.Prohibited;
        }

        // LB15a: an unresolved initial quotation mark that opens a line keeps what follows attached.
        if (RunEndsWithInitialQuote(text, index - 1, out int piIndex) && piIndex >= 0)
            return LineBreakOpportunity.Prohibited;

        // LB15b: an unresolved final quotation mark does not break before itself when what follows can end
        // a line.
        if (right is LineBreakValue.QU && IsFinalQuote(afterCode) && NextIsLineEdge(text, index + 1))
            return LineBreakOpportunity.Prohibited;

        // LB15c: a decimal mark after a space starts a new token, so the space may end the line.
        if (left is LineBreakValue.SP && right is LineBreakValue.IS
            && index + 1 < text.Length && EffectiveClass(text, index + 1) is LineBreakValue.NU)
        {
            return LineBreakOpportunity.Allowed;
        }

        // LB15d: otherwise nothing attaches to a mid-number separator.
        if (right is LineBreakValue.IS)
            return LineBreakOpportunity.Prohibited;

        // LB16: a closing bracket binds to a non-starter, even across spaces.
        if (right is LineBreakValue.NS)
        {
            if (RunEndsWith(text, index - 1, LineBreakValue.CL, skipSpaces: true, out int clIndex)
                && clIndex >= 0)
            {
                return LineBreakOpportunity.Prohibited;
            }

            if (RunEndsWith(text, index - 1, LineBreakValue.CP, skipSpaces: true, out int cpIndex)
                && cpIndex >= 0)
            {
                return LineBreakOpportunity.Prohibited;
            }
        }

        // LB17: two ideographic spaces do not separate.
        if (right is LineBreakValue.B
            && RunEndsWith(text, index - 1, LineBreakValue.B, skipSpaces: true, out int bIndex)
            && bIndex >= 0)
        {
            return LineBreakOpportunity.Prohibited;
        }

        // LB18: a space allows a break after itself.
        if (left is LineBreakValue.SP)
            return LineBreakOpportunity.Allowed;

        // LB19: an unresolved quotation mark that is neither initial nor final does not break on either
        // side.
        if (right is LineBreakValue.QU && !IsInitialQuote(afterCode))
            return LineBreakOpportunity.Prohibited;

        if (left is LineBreakValue.QU && !IsFinalQuote(beforeCode))
            return LineBreakOpportunity.Prohibited;

        // LB19a: unless surrounded by East Asian characters, no unresolved quotation mark breaks on either
        // side.
        if (right is LineBreakValue.QU)
        {
            if (!IsEastAsian(afterCode) && !IsEastAsian(beforeBaseCode))
                return LineBreakOpportunity.Prohibited;

            if (index + 1 >= text.Length || !IsEastAsian(text[index + 1]))
                return LineBreakOpportunity.Prohibited;
        }

        if (left is LineBreakValue.QU)
        {
            if (!IsEastAsian(afterCode))
                return LineBreakOpportunity.Prohibited;

            int previousBase = BaseIndex(text, index - 2);

            if (previousBase < 0 || !IsEastAsian(text[previousBase]))
                return LineBreakOpportunity.Prohibited;
        }

        // LB20: a contingent break may occur on either side.
        if (left is LineBreakValue.CB || right is LineBreakValue.CB)
            return LineBreakOpportunity.Allowed;

        // LB20a: a word-initial hyphen keeps the word attached to it.
        if (left is LineBreakValue.HY or LineBreakValue.HH && IsLetter(right)
            && IsWordInitialHyphen(text, BaseIndex(text, index - 1)))
            return LineBreakOpportunity.Prohibited;

        // LB21: these classes attach to what precedes them, and a break-before does not break after.
        if (right is LineBreakValue.BA or LineBreakValue.HH or LineBreakValue.HY or LineBreakValue.NS)
            return LineBreakOpportunity.Prohibited;

        if (left is LineBreakValue.BB)
            return LineBreakOpportunity.Prohibited;

        // LB21a: after a hyphen following a Hebrew letter, a non-Hebrew letter stays attached.
        if (left is LineBreakValue.HY or LineBreakValue.HH && right is not LineBreakValue.HL
            && BaseIndex(text, index - 2) >= 0
            && EffectiveClass(text, BaseIndex(text, index - 2)) is LineBreakValue.HL)
        {
            return LineBreakOpportunity.Prohibited;
        }

        // LB21b: a symbol does not split from a following Hebrew letter.
        if (left is LineBreakValue.SY && right is LineBreakValue.HL)
            return LineBreakOpportunity.Prohibited;

        // LB22: nothing attaches to an ellipsis.
        if (right is LineBreakValue.IN)
            return LineBreakOpportunity.Prohibited;

        // LB23: digits and letters do not break.
        if (IsLetter(left) && right is LineBreakValue.NU)
            return LineBreakOpportunity.Prohibited;

        if (left is LineBreakValue.NU && IsLetter(right))
            return LineBreakOpportunity.Prohibited;

        // LB23a: currency signs and emoji.
        if (left is LineBreakValue.PR && right is LineBreakValue.ID or LineBreakValue.EB or LineBreakValue.EM)
            return LineBreakOpportunity.Prohibited;

        if (left is LineBreakValue.ID or LineBreakValue.EB or LineBreakValue.EM && right is LineBreakValue.PO)
            return LineBreakOpportunity.Prohibited;

        // LB24: currency signs bind to letters.
        if (left is LineBreakValue.PR or LineBreakValue.PO && IsLetter(right))
            return LineBreakOpportunity.Prohibited;

        if (IsLetter(left) && right is LineBreakValue.PR or LineBreakValue.PO)
            return LineBreakOpportunity.Prohibited;

        // LB25: the number patterns.
        if (IsNumberRule(text, index, left, right))
            return LineBreakOpportunity.Prohibited;

        // LB26: Hangul syllables built from jamo do not break. The property table uses one class (H) for
        // both LV and LVT syllables, but the rule distinguishes them, so the syllable type is derived from
        // the code point: an LV syllable (no trailing consonant) keeps a following vowel jamo.
        if (left is LineBreakValue.JL
            && right is LineBreakValue.JL or LineBreakValue.JV or LineBreakValue.H)
        {
            return LineBreakOpportunity.Prohibited;
        }

        bool leftIsLvSyllable = left is LineBreakValue.H && IsLvSyllable(beforeBaseCode);
        bool leftIsLvtSyllable = left is LineBreakValue.H && !IsLvSyllable(beforeBaseCode);

        if ((left is LineBreakValue.JV || leftIsLvSyllable)
            && right is LineBreakValue.JV or LineBreakValue.JT)
        {
            return LineBreakOpportunity.Prohibited;
        }

        if ((left is LineBreakValue.JT || leftIsLvtSyllable) && right is LineBreakValue.JT)
            return LineBreakOpportunity.Prohibited;

        // LB27: a Hangul syllable block behaves like an ideograph next to a postfix.
        if (left is LineBreakValue.JL or LineBreakValue.JV or LineBreakValue.JT or LineBreakValue.H
            && right is LineBreakValue.PO)
        {
            return LineBreakOpportunity.Prohibited;
        }

        if (left is LineBreakValue.PR
            && right is LineBreakValue.JL or LineBreakValue.JV or LineBreakValue.JT or LineBreakValue.H)
        {
            return LineBreakOpportunity.Prohibited;
        }

        // LB28: letters do not break between themselves.
        if (IsLetter(left) && IsLetter(right))
            return LineBreakOpportunity.Prohibited;

        // LB28a: the orthographic syllables of Brahmic scripts stay together.
        if (IsBrahmicSyllableRule(text, index, left, right))
            return LineBreakOpportunity.Prohibited;

        // LB29: a mid-number separator binds to a following letter.
        if (left is LineBreakValue.IS && IsLetter(right))
            return LineBreakOpportunity.Prohibited;

        // LB30: letters and numbers do not break around brackets, unless the bracket is East Asian.
        if (left is LineBreakValue.AL or LineBreakValue.HL or LineBreakValue.NU
            && right is LineBreakValue.OP && !IsEastAsian(afterCode))
        {
            return LineBreakOpportunity.Prohibited;
        }

        if (left is LineBreakValue.CP && !IsEastAsian(beforeBaseCode)
            && right is LineBreakValue.AL or LineBreakValue.HL or LineBreakValue.NU)
        {
            return LineBreakOpportunity.Prohibited;
        }

        // LB30a: regional indicators pair up; only every second one may be followed by a break.
        if (left is LineBreakValue.RI && right is LineBreakValue.RI)
        {
            // An odd run length means this position is the second half of a pair, which must not break.
            return OddRegionalIndicatorRun(text, index)
                ? LineBreakOpportunity.Prohibited
                : LineBreakOpportunity.Allowed;
        }

        // LB30b: an emoji modifier belongs to its base, including one the emoji property covers while it is
        // still unassigned.
        if (right is LineBreakValue.EM
            && (left is LineBreakValue.EB || IsUnassignedExtendedPictographic(beforeBaseCode)))
        {
            return LineBreakOpportunity.Prohibited;
        }

        // LB31: everything else may break.
        return LineBreakOpportunity.Allowed;
    }

    /// <summary>
    /// LB25: the number patterns, each written as a look-behind over the run that precedes the position.
    /// </summary>
    /// <param name="text">The whole text as code points.</param>
    /// <param name="index">Position to decide.</param>
    /// <param name="left">Effective left class.</param>
    /// <param name="right">Effective right class.</param>
    /// <returns>True when the position must not break.</returns>
    private static bool IsNumberRule(ReadOnlySpan<int> text, int index, LineBreakValue left, LineBreakValue right)
    {
        // (PO | PR) × (OP NU | OP IS NU | NU)
        if (left is LineBreakValue.PO or LineBreakValue.PR)
        {
            if (right is LineBreakValue.NU)
                return true;

            if (right is LineBreakValue.OP && index + 1 < text.Length)
            {
                LineBreakValue next = EffectiveClass(text, index + 1);

                if (next is LineBreakValue.NU)
                    return true;

                if (next is LineBreakValue.IS && index + 2 < text.Length
                    && EffectiveClass(text, index + 2) is LineBreakValue.NU)
                {
                    return true;
                }
            }
        }

        // (HY | IS) × NU
        if (left is LineBreakValue.HY or LineBreakValue.IS && right is LineBreakValue.NU)
            return true;

        // NU (SY | IS)* × (NU | CL | CP | PO | PR). The two patterns that end in a closing bracket keep
        // the number alive, so the look-behind may step over one bracket.
        if (right is LineBreakValue.NU or LineBreakValue.CL or LineBreakValue.CP)
        {
            if (FollowsNumberRun(text, index, skipClosingBracket: false))
                return true;
        }

        if (right is LineBreakValue.PO or LineBreakValue.PR)
        {
            if (FollowsNumberRun(text, index, skipClosingBracket: true))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Whether the text before a position ends with <c>NU (SY | IS)*</c>, which several LB25 patterns
    /// require.
    /// </summary>
    /// <param name="text">The whole text as code points.</param>
    /// <param name="index">Position to look before.</param>
    /// <param name="skipClosingBracket">
    /// Whether a closing bracket directly before the position may be stepped over (the LB25 patterns that
    /// keep a number alive across it).
    /// </param>
    /// <returns>True when a number run ends there.</returns>
    private static bool FollowsNumberRun(ReadOnlySpan<int> text, int index, bool skipClosingBracket)
    {
        int i = index - 1;

        if (skipClosingBracket && i >= 0 && EffectiveClass(text, i) is LineBreakValue.CL or LineBreakValue.CP)
            i--;

        while (i >= 0 && EffectiveClass(text, i) is LineBreakValue.SY or LineBreakValue.IS)
            i--;

        return i >= 0 && EffectiveClass(text, i) is LineBreakValue.NU;
    }

    /// <summary>
    /// Whether a Hangul syllable is of the LV kind (no trailing consonant), which LB26 distinguishes from
    /// LVT even though the property table gives both the class H.
    /// </summary>
    /// <param name="codePoint">The code point to test.</param>
    /// <returns>True for an LV syllable.</returns>
    private static bool IsLvSyllable(int codePoint)
    {
        const int hangulSyllableBase = 0xAC00;
        const int hangulSyllableCount = 11172;
        const int trailingConsonantCount = 28;

        if (codePoint < hangulSyllableBase || codePoint >= hangulSyllableBase + hangulSyllableCount)
            return false;

        return (codePoint - hangulSyllableBase) % trailingConsonantCount == 0;
    }

    /// <summary>
    /// LB20a: whether a hyphen is word-initial, meaning it follows the start of the text or one of the
    /// classes that cannot be part of a word.
    /// </summary>
    /// <param name="text">The whole text as code points.</param>
    /// <param name="hyphenIndex">Index of the hyphen.</param>
    /// <returns>True when the hyphen starts a word.</returns>
    private static bool IsWordInitialHyphen(ReadOnlySpan<int> text, int hyphenIndex)
    {
        if (hyphenIndex == 0)
            return true;

        return EffectiveClass(text, hyphenIndex - 1) is
            LineBreakValue.BK or LineBreakValue.CR or LineBreakValue.LF or LineBreakValue.NL
            or LineBreakValue.SP or LineBreakValue.ZW or LineBreakValue.CB or LineBreakValue.GL;
    }

    /// <summary>
    /// LB28a: do not break inside the orthographic syllables of Brahmic scripts. The dotted circle
    /// (<c>[◌]</c> in the patterns) is the placeholder base of an incomplete syllable.
    /// <para>
    /// Positions are resolved to base characters first: a combining mark folds into the character it
    /// attaches to (LB9), so "the character before the syllable" means the base before the folded
    /// position, not the code point one slot earlier.
    /// </para>
    /// </summary>
    /// <param name="text">The whole text as code points.</param>
    /// <param name="index">Position to decide.</param>
    /// <param name="left">Effective left class.</param>
    /// <param name="right">Effective right class.</param>
    /// <returns>True when the position must not break.</returns>
    private static bool IsBrahmicSyllableRule(
        ReadOnlySpan<int> text,
        int index,
        LineBreakValue left,
        LineBreakValue right)
    {
        int leftBase = BaseIndex(text, index - 1);
        int rightBase = BaseIndex(text, index);

        // Computed once: a local function may not capture the span.
        bool leftIsSyllableBase = left is LineBreakValue.AK or LineBreakValue.AS
                                  || text[leftBase] == DottedCircle;
        bool rightIsSyllableBase = right is LineBreakValue.AK or LineBreakValue.AS
                                   || text[rightBase] == DottedCircle;

        // AP × (AK | [◌] | AS)
        if (left is LineBreakValue.AP && rightIsSyllableBase)
            return true;

        // (AK | [◌] | AS) × (VF | VI)
        if (right is LineBreakValue.VF or LineBreakValue.VI && leftIsSyllableBase)
            return true;

        // (AK | [◌] | AS) VI × (AK | [◌])
        if (left is LineBreakValue.VI && (right is LineBreakValue.AK || text[rightBase] == DottedCircle))
        {
            int before = leftBase - 1;

            if (before >= 0)
            {
                int beforeBase = BaseIndex(text, before);

                if (EffectiveClass(text, beforeBase) is LineBreakValue.AK or LineBreakValue.AS
                    || text[beforeBase] == DottedCircle)
                {
                    return true;
                }
            }
        }

        // (AK | [◌] | AS) × (AK | [◌] | AS) VF
        if (leftIsSyllableBase && rightIsSyllableBase && rightBase + 1 < text.Length)
        {
            int after = BaseIndex(text, rightBase + 1);

            if (EffectiveClass(text, after) is LineBreakValue.VF)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Whether the run ending at <paramref name="index"/> ends with a class, looking through intervening
    /// spaces (the shape of LB8, LB14, LB16 and LB17).
    /// </summary>
    /// <param name="text">The whole text as code points.</param>
    /// <param name="index">Index the run ends at.</param>
    /// <param name="target">Class the run must end with.</param>
    /// <param name="skipSpaces">Whether intervening spaces are skipped.</param>
    /// <param name="foundIndex">Index of the code point that matched, or -1.</param>
    /// <returns>True when the run ends with the class.</returns>
    private static bool RunEndsWith(
        ReadOnlySpan<int> text,
        int index,
        LineBreakValue target,
        bool skipSpaces,
        out int foundIndex)
    {
        LineBreakValue value = EffectiveClass(text, index);

        if (value == target)
        {
            foundIndex = index;
            return true;
        }

        if (!skipSpaces || value is not LineBreakValue.SP)
        {
            foundIndex = -1;
            return false;
        }

        for (int i = index - 1; i >= 0; i--)
        {
            LineBreakValue previous = EffectiveClass(text, i);

            if (previous is LineBreakValue.SP)
                continue;

            if (previous == target)
            {
                foundIndex = i;
                return true;
            }

            break;
        }

        foundIndex = -1;
        return false;
    }

    /// <summary>
    /// LB15a: whether the run ends with an initial quotation mark that itself follows the start of a line
    /// or one of the classes that open one.
    /// </summary>
    /// <param name="text">The whole text as code points.</param>
    /// <param name="index">Index the run ends at.</param>
    /// <param name="quoteIndex">Index of the quotation mark, or -1.</param>
    /// <returns>True when the rule applies.</returns>
    private static bool RunEndsWithInitialQuote(ReadOnlySpan<int> text, int index, out int quoteIndex)
    {
        int candidate = index;

        while (candidate >= 0 && EffectiveClass(text, candidate) is LineBreakValue.SP)
            candidate--;

        // The quotation mark may carry combining marks of its own, which fold into it (LB9).
        candidate = BaseIndex(text, candidate);

        if (candidate < 0
            || EffectiveClass(text, candidate) is not LineBreakValue.QU
            || !IsInitialQuote(text[candidate]))
        {
            quoteIndex = -1;
            return false;
        }

        quoteIndex = candidate;

        if (candidate == 0)
            return true;

        return EffectiveClass(text, candidate - 1) is
            LineBreakValue.BK or LineBreakValue.CR or LineBreakValue.LF or LineBreakValue.NL
            or LineBreakValue.OP or LineBreakValue.QU or LineBreakValue.GL or LineBreakValue.SP
            or LineBreakValue.ZW;
    }

    /// <summary>
    /// LB15b: whether what follows a final quotation mark may end a line.
    /// </summary>
    /// <param name="text">The whole text as code points.</param>
    /// <param name="index">Index after the quotation mark.</param>
    /// <returns>True when the position may end a line.</returns>
    private static bool NextIsLineEdge(ReadOnlySpan<int> text, int index)
    {
        if (index >= text.Length)
            return true;

        return EffectiveClass(text, index) is
            LineBreakValue.SP or LineBreakValue.GL or LineBreakValue.WJ or LineBreakValue.CL
            or LineBreakValue.QU or LineBreakValue.CP or LineBreakValue.EX or LineBreakValue.IS
            or LineBreakValue.SY or LineBreakValue.BK or LineBreakValue.CR or LineBreakValue.LF
            or LineBreakValue.NL or LineBreakValue.ZW;
    }

    /// <summary>
    /// The class of a code point with combining marks and zero width joiners folded into the character
    /// they attach to (LB9, LB10).
    /// </summary>
    /// <param name="text">The whole text as code points.</param>
    /// <param name="index">Index to resolve.</param>
    /// <returns>The class the rules should see.</returns>
    private static LineBreakValue EffectiveClass(ReadOnlySpan<int> text, int index)
    {
        LineBreakValue value = Resolve(text[index]);

        if (value is not (LineBreakValue.CM or LineBreakValue.ZWJ))
            return value;

        for (int i = index - 1; i >= 0; i--)
        {
            LineBreakValue previous = Resolve(text[i]);

            if (previous is LineBreakValue.CM or LineBreakValue.ZWJ)
                continue;

            // LB9's exception list, and LB10 for everything left over.
            if (previous is LineBreakValue.BK or LineBreakValue.CR or LineBreakValue.LF
                or LineBreakValue.NL or LineBreakValue.SP or LineBreakValue.ZW)
            {
                return LineBreakValue.AL;
            }

            return previous;
        }

        return LineBreakValue.AL;
    }

    /// <summary>
    /// The index of the base character a code point belongs to: combining marks and zero width joiners
    /// fold into the character they attach to (LB9), so a rule that looks one character further back
    /// must skip them or it inspects the mark instead of its base.
    /// </summary>
    /// <param name="text">The whole text as code points.</param>
    /// <param name="index">Index to resolve.</param>
    /// <returns>The index of the base character, or the index itself when it is not a folding class.</returns>
    private static int BaseIndex(ReadOnlySpan<int> text, int index)
    {
        if (index < 0)
            return -1;

        while (index > 0 && Resolve(text[index]) is LineBreakValue.CM or LineBreakValue.ZWJ)
            index--;

        return index;
    }

    /// <summary>Whether a class behaves like a letter for LB20a, LB23, LB24, LB28 and LB29.</summary>
    /// <param name="value">The line break class.</param>
    /// <returns>True for AL and HL.</returns>
    private static bool IsLetter(LineBreakValue value) => value is LineBreakValue.AL or LineBreakValue.HL;

    /// <summary>
    /// LB30a: whether the run of regional indicators before this position has odd length, so the position
    /// falls inside a pair.
    /// </summary>
    /// <param name="text">The whole text as code points.</param>
    /// <param name="index">Position to decide.</param>
    /// <returns>True when the preceding run length is odd.</returns>
    private static bool OddRegionalIndicatorRun(ReadOnlySpan<int> text, int index)
    {
        int count = 0;
        int i = index - 1;

        while (i >= 0)
        {
            int baseIndex = BaseIndex(text, i);

            if (EffectiveClass(text, baseIndex) is not LineBreakValue.RI)
                break;

            count++;
            i = baseIndex - 1;
        }

        return (count & 1) == 1;
    }

    /// <summary>
    /// The line break class of a code point with the LB1 aliases resolved: AI, SG and XX behave like AL;
    /// SA behaves like CM for diacritics and AL otherwise; CJ behaves like NS.
    /// </summary>
    /// <param name="codePoint">The code point to classify.</param>
    /// <returns>The resolved class.</returns>
    private static LineBreakValue Resolve(int codePoint)
    {
        LineBreakValue value = LineBreakData.Lookup(codePoint);

        return value switch
        {
            LineBreakValue.AI or LineBreakValue.SG or LineBreakValue.XX => LineBreakValue.AL,
            LineBreakValue.SA => IsMnOrMc(codePoint) ? LineBreakValue.CM : LineBreakValue.AL,
            LineBreakValue.CJ => LineBreakValue.NS,
            _ => value,
        };
    }

    /// <summary>Whether a code point is a diacritic, which is how LB1 resolves the SA class.</summary>
    /// <param name="codePoint">The code point to test.</param>
    /// <returns>True for Mn and Mc.</returns>
    private static bool IsMnOrMc(int codePoint)
    {
        UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(codePoint);
        return category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark;
    }

    /// <summary>Whether a code point is initial punctuation (Pi), which LB15a and LB19 distinguish.</summary>
    /// <param name="codePoint">The code point to test.</param>
    /// <returns>True for Pi.</returns>
    private static bool IsInitialQuote(int codePoint) =>
        CharUnicodeInfo.GetUnicodeCategory(codePoint) is UnicodeCategory.InitialQuotePunctuation;

    /// <summary>Whether a code point is final punctuation (Pf), which LB15b and LB19 distinguish.</summary>
    /// <param name="codePoint">The code point to test.</param>
    /// <returns>True for Pf.</returns>
    private static bool IsFinalQuote(int codePoint) =>
        CharUnicodeInfo.GetUnicodeCategory(codePoint) is UnicodeCategory.FinalQuotePunctuation;

    /// <summary>Whether a code point is East Asian, which LB19a and LB30 exempt.</summary>
    /// <param name="codePoint">The code point to test.</param>
    /// <returns>True for the wide, fullwidth and halfwidth classes.</returns>
    private static bool IsEastAsian(int codePoint)
    {
        EastAsianWidthValue width = EastAsianWidthData.Lookup(codePoint);
        return width is EastAsianWidthValue.F or EastAsianWidthValue.W or EastAsianWidthValue.H;
    }

    /// <summary>
    /// Whether a code point is unassigned but covered by Extended_Pictographic, which LB30b treats like an
    /// emoji base.
    /// </summary>
    /// <param name="codePoint">The code point to test.</param>
    /// <returns>True for unassigned pictographs.</returns>
    private static bool IsUnassignedExtendedPictographic(int codePoint)
    {
        if (ExtendedPictographicData.Lookup(codePoint) is not ExtendedPictographicValue.Y)
            return false;

        return CharUnicodeInfo.GetUnicodeCategory(codePoint) is UnicodeCategory.OtherNotAssigned;
    }
}

/// <summary>Answer for a candidate line break position.</summary>
public enum LineBreakOpportunity
{
    /// <summary>A break is allowed here.</summary>
    Allowed,

    /// <summary>A break must occur here.</summary>
    Mandatory,

    /// <summary>A break must not occur here.</summary>
    Prohibited,
}
