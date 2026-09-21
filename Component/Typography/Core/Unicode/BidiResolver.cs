namespace GodotNodeExtension.Component.Typography.Core.Unicode;

using System;
using System.Collections.Generic;
using global::Unicode.Bidi;
using BidiTextRange = global::Unicode.Bidi.TextRange;

/// <summary>
/// A range the caller forces a direction on, whatever the text inside it says.
/// <para>
/// This is the "bidi override" a text system needs: a code sample, a file path or a product name inside a
/// right-to-left paragraph is left to right because someone said so, not because the algorithm could tell. The
/// range is in UTF-16 code units, like every other range here.
/// </para>
/// </summary>
/// <param name="Start">First code unit of the range.</param>
/// <param name="Length">Number of code units in the range.</param>
/// <param name="Direction">Direction forced on the range.</param>
public readonly record struct BidiOverride(int Start, int Length, Model.TextDirection Direction)
{
    /// <summary>One past the last code unit of the range.</summary>
    public int End => Start + Length;
}

/// <summary>
/// Bidi resolution, delegated to the ported <c>unicode-bidi</c> implementation.
/// <para>
/// The engine does not implement UAX #9 itself: it asks a specialised implementation for the levels and the visual
/// runs. This component does the same — <c>Unicode.Bidi</c> (a port of the Rust crate, with a parity harness
/// against it) answers the algorithm, and what lives here is only the shape the rest of the pipeline wants:
/// levels per code unit, and the visual text of a line.
/// </para>
/// <para>
/// The text and the ranges handed in and back are UTF-16 code units, which is also the unit this component's source
/// ranges use, so nothing has to be converted. Decisions about the *paragraph* level stay ours
/// (<see cref="BidiAlgorithm.ParagraphLevel"/>): the algorithm needs a base direction before it can answer, and
/// this component already decides that from the language and the text.
/// </para>
/// </summary>
public static class BidiResolver
{
    /// <summary>
    /// Whether any part of the text is written right to left.
    /// <para>
    /// This is the cheap gate for the whole feature: a document that is entirely left to right needs no bidi pass at
    /// all, and asking for one would only add work to the common case.
    /// </para>
    /// </summary>
    /// <param name="text">Text to inspect.</param>
    /// <returns>True when the text contains right-to-left content.</returns>
    /// <exception cref="ArgumentNullException">When <paramref name="text"/> is null.</exception>
    public static bool HasRightToLeft(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.Length > 0 && BidiInfo.Create(text).HasRtl();
    }

    /// <summary>
    /// The embedding level of every code unit of the text, in logical order.
    /// </summary>
    /// <param name="text">Text to resolve.</param>
    /// <param name="baseDirection">
    /// Direction the paragraph is declared to run in. <see cref="Model.TextDirection.LeftToRight"/> lets the
    /// algorithm decide from the text, which is what "undeclared" means.
    /// </param>
    /// <returns>One level per code unit (even = left to right, odd = right to left).</returns>
    /// <exception cref="ArgumentNullException">When <paramref name="text"/> is null.</exception>
    public static byte[] LevelsOf(string text, Model.TextDirection baseDirection)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (text.Length == 0)
            return [];

        BidiInfo info = BidiInfo.Create(text, BaseLevel(baseDirection));
        ParagraphInfo paragraph = info.Paragraphs[0];
        var levels = new byte[text.Length];

        for (int i = 0; i < text.Length; i++)
            levels[i] = info.LevelAt(paragraph, i).Number();

        return levels;
    }

    /// <summary>
    /// The embedding level of every code unit, with the paragraph level forced rather than inferred.
    /// <para>
    /// A line of Unicode's conformance suite states its base direction, so the check has to be able to ask for
    /// exactly that: passing the level instead of leaving it to the text is the difference between "this text is
    /// left to right" and "decide from this text".
    /// </para>
    /// </summary>
    /// <param name="text">Text to resolve.</param>
    /// <param name="paragraphLevel">0 for left to right, 1 for right to left.</param>
    /// <returns>One level per code unit (even = left to right, odd = right to left).</returns>
    /// <exception cref="ArgumentNullException">When <paramref name="text"/> is null.</exception>
    public static byte[] LevelsOf(string text, int paragraphLevel)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (text.Length == 0)
            return [];

        BidiInfo info = BidiInfo.Create(text, Level.CreateExplicit((byte)(paragraphLevel & 1)));
        ParagraphInfo paragraph = info.Paragraphs[0];
        var levels = new byte[text.Length];

        for (int i = 0; i < text.Length; i++)
            levels[i] = info.LevelAt(paragraph, i).Number();

        return levels;
    }

    /// <summary>
    /// The levels of the text with some ranges forced to a direction.
    /// <para>
    /// Each forced range is resolved on its own with that direction as its base, and the result replaces the levels
    /// of its code units: the paragraph keeps its own resolution around them. Later ranges win over earlier ones
    /// where they overlap, which is the order a caller reads its own list in.
    /// </para>
    /// </summary>
    /// <param name="text">Text to resolve.</param>
    /// <param name="paragraphLevel">Base level of the paragraph: 0 or 1.</param>
    /// <param name="overrides">Ranges to force, or null for none.</param>
    /// <returns>One level per code unit.</returns>
    /// <exception cref="ArgumentNullException">When <paramref name="text"/> is null.</exception>
    public static byte[] LevelsOf(string text, int paragraphLevel, IReadOnlyList<BidiOverride>? overrides)
    {
        byte[] levels = LevelsOf(text, paragraphLevel);

        if (overrides is null)
            return levels;

        foreach (BidiOverride range in overrides)
        {
            int start = Math.Clamp(range.Start, 0, text.Length);
            int end = Math.Clamp(range.End, start, text.Length);

            if (end <= start)
                continue;

            byte[] forced = LevelsOf(
                text[start..end], range.Direction == Model.TextDirection.RightToLeft ? 1 : 0);

            for (int i = start; i < end; i++)
                levels[i] = forced[i - start];
        }

        return levels;
    }

    /// <summary>
    /// The text of one line in display order.
    /// <para>
    /// The line's range is in logical order, which is the order everything else in this component works in; the
    /// result is what a reader of that line sees. Mixed content is where this differs from the input at all — a
    /// single-direction line comes back unchanged.
    /// </para>
    /// </summary>
    /// <param name="text">The paragraph's text, in logical order.</param>
    /// <param name="start">First UTF-16 unit of the line.</param>
    /// <param name="end">One past the last UTF-16 unit of the line.</param>
    /// <param name="baseDirection">Direction the paragraph is declared to run in.</param>
    /// <returns>The line's text in display order.</returns>
    /// <exception cref="ArgumentNullException">When <paramref name="text"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">When the range is not inside the text.</exception>
    public static string VisualTextOfLine(string text, int start, int end, Model.TextDirection baseDirection)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(end, text.Length);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(start, end);

        if (text.Length == 0 || start == end)
            return string.Empty;

        BidiInfo info = BidiInfo.Create(text, BaseLevel(baseDirection));
        ParagraphInfo paragraph = info.Paragraphs[0];
        return info.ReorderLine(paragraph, new BidiTextRange(start, end));
    }

    /// <summary>
    /// The visual order of the code units of one line: the index each logical position is drawn at.
    /// <para>
    /// A renderer or an element assembler needs the permutation rather than the reordered string, because the
    /// elements it moves carry their own glyphs and source ranges.
    /// </para>
    /// </summary>
    /// <param name="text">The paragraph's text, in logical order.</param>
    /// <param name="start">First UTF-16 unit of the line.</param>
    /// <param name="end">One past the last UTF-16 unit of the line.</param>
    /// <param name="baseDirection">Direction the paragraph is declared to run in.</param>
    /// <returns>Logical positions in display order.</returns>
    /// <exception cref="ArgumentNullException">When <paramref name="text"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">When the range is not inside the text.</exception>
    public static int[] VisualOrderOfLine(string text, int start, int end, Model.TextDirection baseDirection)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(end, text.Length);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(start, end);

        int length = end - start;

        if (length <= 0)
            return [];

        byte[] levels = LevelsOf(text, baseDirection);
        return VisualOrder(levels, start, length);
    }

    /// <summary>
    /// Rule L2: from the levels of a line, the order the code units are drawn in.
    /// <para>
    /// This is the part of the algorithm that needs no further data, so it is done here over the levels the
    /// implementation reported: from the highest level down to the lowest odd level, every run of characters at or
    /// above that level is reversed.
    /// </para>
    /// </summary>
    /// <param name="levels">One level per code unit, for the whole paragraph.</param>
    /// <param name="start">First code unit of the line.</param>
    /// <param name="length">Number of code units in the line.</param>
    /// <returns>Logical positions in display order.</returns>
    /// <exception cref="ArgumentNullException">When <paramref name="levels"/> is null.</exception>
    public static int[] VisualOrder(byte[] levels, int start, int length)
    {
        ArgumentNullException.ThrowIfNull(levels);

        var order = new int[length];

        for (int i = 0; i < length; i++)
            order[i] = start + i;

        byte highest = 0;
        byte lowestOdd = byte.MaxValue;

        for (int i = start; i < start + length && i < levels.Length; i++)
        {
            byte level = levels[i];

            if (level > highest)
                highest = level;

            if ((level & 1) == 1 && level < lowestOdd)
                lowestOdd = level;
        }

        if (lowestOdd == byte.MaxValue)
            return order;

        for (byte level = highest; level >= lowestOdd; level--)
        {
            for (int i = 0; i < length; i++)
            {
                if (levels[order[i]] < level)
                    continue;

                int runStart = i;

                while (i < length && levels[order[i]] >= level)
                    i++;

                Reverse(order, runStart, i - 1);
            }
        }

        return order;
    }

    /// <summary>
    /// The levels of one line, with rule L1 applied: segment and paragraph separators, and the whitespace leading up
    /// to them, take the paragraph level again.
    /// <para>
    /// The rule is a line-level rule, and the implementation reports the paragraph levels without it — a trailing
    /// space inside a right-to-left paragraph comes back as a right-to-left character, which would put it on the
    /// wrong side of the line. Applying it here is what makes the levels a line can actually be laid out from.
    /// </para>
    /// </summary>
    /// <param name="text">The line's text (which is also the paragraph, in this component's usage).</param>
    /// <param name="baseDirection">Direction the paragraph is declared to run in.</param>
    /// <returns>One level per code unit of the line.</returns>
    /// <exception cref="ArgumentNullException">When <paramref name="text"/> is null.</exception>
    public static byte[] LineLevels(string text, Model.TextDirection baseDirection) =>
        LineLevels(text, baseDirection == Model.TextDirection.RightToLeft ? 1 : 0, auto: baseDirection != Model.TextDirection.RightToLeft);

    /// <summary>
    /// The levels of one line with rule L1 applied and the base level stated rather than inferred.
    /// </summary>
    /// <param name="text">The line's text.</param>
    /// <param name="paragraphLevel">0 for left to right, 1 for right to left.</param>
    /// <returns>One level per code unit of the line.</returns>
    /// <exception cref="ArgumentNullException">When <paramref name="text"/> is null.</exception>
    public static byte[] LineLevels(string text, int paragraphLevel) => LineLevels(text, paragraphLevel, auto: false);

    /// <summary>
    /// Apply rule L1 to a level array: separators and the whitespace before them go back to the paragraph level.
    /// </summary>
    /// <param name="levels">Levels to adjust in place.</param>
    /// <param name="paragraphLevel">Level to reset those characters to.</param>
    /// <param name="start">First code unit of the line.</param>
    /// <param name="length">Number of code units in the line.</param>
    /// <param name="text">The line's text, for the whitespace and separator classes.</param>
    /// <exception cref="ArgumentNullException">When <paramref name="levels"/> or <paramref name="text"/> is null.</exception>
    public static void ApplyLineResets(byte[] levels, string text, int paragraphLevel, int start, int length)
    {
        ArgumentNullException.ThrowIfNull(levels);
        ArgumentNullException.ThrowIfNull(text);

        int end = Math.Min(start + length, Math.Min(levels.Length, text.Length));

        for (int i = start; i < end; i++)
        {
            BidiClassValue value = ClassOf(i, text);

            // Only the separators trigger the reset; the whitespace *before* them is reset with them, which the
            // backward scan below does. A whitespace character anywhere else keeps the level the algorithm gave it.
            if (value is not (BidiClassValue.S or BidiClassValue.B))
                continue;

            // The separator itself, and every whitespace character before it.
            levels[i] = (byte)paragraphLevel;

            for (int j = i - 1; j >= start; j--)
            {
                if (ClassOf(j, text) != BidiClassValue.WS)
                    break;

                levels[j] = (byte)paragraphLevel;
            }
        }
    }

    private static byte[] LineLevels(string text, int paragraphLevel, bool auto)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (text.Length == 0)
            return [];

        BidiInfo info = auto
            ? BidiInfo.Create(text)
            : BidiInfo.Create(text, Level.CreateExplicit((byte)(paragraphLevel & 1)));

        ParagraphInfo paragraph = info.Paragraphs[0];
        int level = auto ? paragraph.Level.Number() : paragraphLevel & 1;
        var levels = new byte[text.Length];

        for (int i = 0; i < text.Length; i++)
            levels[i] = info.LevelAt(paragraph, i).Number();

        ApplyLineResets(levels, text, level, 0, text.Length);
        return levels;
    }

    private static BidiClassValue ClassOf(int index, string text) =>
        index < text.Length ? ClassOfCodePoint(char.ConvertToUtf32(text, index)) : BidiClassValue.L;

    private static BidiClassValue ClassOfCodePoint(int codePoint) => BidiClassData.Lookup(codePoint);

    /// <summary>
    /// The same levels, grouped into runs of one direction each, in logical order.
    /// </summary>
    /// <param name="levels">One level per code unit, for the whole paragraph.</param>
    /// <returns>Runs of equal direction, in logical order, as (start, length, isRightToLeft).</returns>
    /// <exception cref="ArgumentNullException">When <paramref name="levels"/> is null.</exception>
    public static List<(int Start, int Length, bool RightToLeft)> DirectionRuns(byte[] levels)
    {
        ArgumentNullException.ThrowIfNull(levels);

        var runs = new List<(int Start, int Length, bool RightToLeft)>();

        if (levels.Length == 0)
            return runs;

        int start = 0;
        bool rightToLeft = (levels[0] & 1) == 1;

        for (int i = 1; i <= levels.Length; i++)
        {
            bool current = i < levels.Length && (levels[i] & 1) == 1;

            if (i < levels.Length && current == rightToLeft)
                continue;

            runs.Add((start, i - start, rightToLeft));
            start = i;
            rightToLeft = current;
        }

        return runs;
    }

    private static void Reverse(int[] values, int from, int to)
    {
        while (from < to)
        {
            (values[from], values[to]) = (values[to], values[from]);
            from++;
            to--;
        }
    }

    private static Level? BaseLevel(Model.TextDirection baseDirection) => baseDirection switch
    {
        Model.TextDirection.RightToLeft => Level.CreateExplicit(1),
        _ => null,
    };
}
