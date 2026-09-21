namespace GodotNodeExtension.Component.Typography.Core.Unicode;

using System;

/// <summary>
/// The Unicode bidirectional algorithm (UAX #9), implemented rule by rule.
/// <para>
/// Only the rules that are done are here, and each one states its number: the algorithm is large enough that a
/// half-applied version is worse than a clearly partial one, because the rest of the pipeline (line breaking,
/// glyph order, element order) has to know which parts it may rely on.
/// </para>
/// <para>
/// Done: <b>P2/P3</b> (the paragraph level: the first strong type, skipping isolates). The remaining rules — X, W,
/// N, I, L — and the level-to-visual-order step are named in the component's design notes as the next work.
/// </para>
/// </summary>
public static class BidiAlgorithm
{
    /// <summary>
    /// Paragraph level requested explicitly by the caller, as the values UAX #9 uses for <c>base direction</c>.
    /// </summary>
    public const int LeftToRight = 0;

    /// <summary>Paragraph level of a right-to-left paragraph.</summary>
    public const int RightToLeft = 1;

    /// <summary>
    /// Auto direction: P2 and P3 decide, which is what <see cref="ParagraphLevel"/> expects for "no explicit
    /// direction".
    /// </summary>
    public const int Auto = -1;

    /// <summary>
    /// Rule P2 and P3: the paragraph's embedding level.
    /// <para>
    /// P2 says to look for the first character of type L, AL or R, <em>skipping any text between an isolate
    /// initiator and its matching PDI</em> — so a quotation inside an isolate does not decide the direction of the
    /// paragraph around it. P3 gives level 0 when there is no strong character at all, which is what a paragraph of
    /// digits and punctuation gets.
    /// </para>
    /// </summary>
    /// <param name="codePoints">The paragraph's code points, in logical order.</param>
    /// <param name="paragraphLevel">
    /// <see cref="LeftToRight"/>, <see cref="RightToLeft"/>, or <see cref="Auto"/> to apply P2/P3.
    /// </param>
    /// <returns>The paragraph level: even for left-to-right, odd for right-to-left.</returns>
    public static int ParagraphLevel(ReadOnlySpan<int> codePoints, int paragraphLevel)
    {
        if (paragraphLevel >= 0)
            return paragraphLevel & 1;

        int isolateDepth = 0;

        foreach (int codePoint in codePoints)
        {
            BidiClassValue value = BidiClassData.Lookup(codePoint);

            switch (value)
            {
                case BidiClassValue.LRI:
                case BidiClassValue.RLI:
                case BidiClassValue.FSI:
                    isolateDepth++;
                    continue;

                case BidiClassValue.PDI:
                    if (isolateDepth > 0)
                        isolateDepth--;

                    continue;
            }

            if (isolateDepth > 0)
                continue;

            switch (value)
            {
                case BidiClassValue.L:
                    return LeftToRight;
                case BidiClassValue.R:
                case BidiClassValue.AL:
                    return RightToLeft;
            }
        }

        // P3: no strong type found.
        return LeftToRight;
    }

    /// <summary>
    /// Whether a character may take part in the resolution at all: the formatting characters (embedding,
    /// override, isolate) carry instructions rather than text.
    /// </summary>
    /// <param name="value">Bidirectional class to test.</param>
    /// <returns>True for the explicit formatting classes.</returns>
    public static bool IsExplicitFormatting(BidiClassValue value) =>
        value is BidiClassValue.LRE or BidiClassValue.RLE or BidiClassValue.LRO or BidiClassValue.RLO
            or BidiClassValue.PDF or BidiClassValue.LRI or BidiClassValue.RLI or BidiClassValue.FSI
            or BidiClassValue.PDI;
}
