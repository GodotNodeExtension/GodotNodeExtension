using System;
using System.Collections.Generic;

namespace GodotNodeExtension.Component.Typography.Core.Unicode;

/// <summary>
/// Splits text at the positions Unicode allows a line break (UAX #14).
/// <para>
/// The pipeline breaks lines between segments, so a segment defines the finest position the line breaker
/// can choose. Grouping a whole Western word into one segment therefore makes "well-known" unbreakable
/// anywhere, which is not what the script's convention says: a break after the hyphen is expected. Cutting
/// the run where UAX #14 allows it restores that without teaching the line breaker about hyphens.
/// </para>
/// <para>
/// The reverse is also true: the rules that refuse a break (inside a word, before a closing bracket, before
/// a number's suffix) keep those characters in one segment, so the shape of the segmentation already
/// encodes most of what the breaker needs.
/// </para>
/// </summary>
public static class BreakOpportunitySplitter
{
    /// <summary>
    /// Find the offsets inside a range where a line break is allowed.
    /// </summary>
    /// <param name="text">The text the range belongs to; the surrounding text supplies rule context.</param>
    /// <param name="start">Start of the range, as a UTF-16 offset.</param>
    /// <param name="end">End of the range (exclusive), as a UTF-16 offset.</param>
    /// <returns>
    /// UTF-16 offsets strictly inside the range, in ascending order. Empty when the range cannot break.
    /// </returns>
    /// <exception cref="ArgumentNullException">When <paramref name="text"/> is null.</exception>
    public static List<int> SplitOffsets(string text, int start, int end)
    {
        ArgumentNullException.ThrowIfNull(text);

        var offsets = new List<int>();

        if (end - start <= 1)
            return offsets;

        // Code points, with the UTF-16 offset each one starts at: a surrogate pair is one position, and a
        // break between its halves would be inside a character.
        var codePoints = new List<int>(text.Length);
        var charOffsets = new List<int>(text.Length);

        for (int i = 0; i < text.Length; i++)
        {
            charOffsets.Add(i);
            codePoints.Add(char.ConvertToUtf32(text, i));

            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                i++;
        }

        int firstCodePoint = charOffsets.IndexOf(start);
        if (firstCodePoint < 0)
            return offsets;

        var scratch = new int[codePoints.Count];
        codePoints.CopyTo(scratch);

        for (int index = firstCodePoint + 1; index < codePoints.Count; index++)
        {
            if (charOffsets[index] >= end)
                break;

            if (charOffsets[index] <= start)
                continue;

            if (LineBreakAlgorithm.OpportunityAt(scratch, index) != LineBreakOpportunity.Prohibited)
                offsets.Add(charOffsets[index]);
        }

        return offsets;
    }
}
