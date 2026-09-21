namespace GodotNodeExtension.Component.Typography.Core.Hyphenation;

using System;
using System.Collections.Generic;
using System.Text;

/// <summary>
/// Liang's algorithm (the one TeX uses): where a word may be broken.
/// <para>
/// The word is matched against the pattern trie from every position, with a dot on each side so that patterns which
/// anchor to a word boundary can match. A gap whose highest score is odd is a place a break is allowed; the minimum
/// word parts on either side (the pattern file's left and right hyphen minima) keep a break from leaving a fragment
/// like "a-" behind. Exceptions stated by the pattern file win over the patterns, including a word listed without any
/// hyphen - which means the language refuses to hyphenate it at all.
/// </para>
/// </summary>
public static class Hyphenator
{
    /// <summary>
    /// Collect the positions inside a word where a break is allowed, in ascending order.
    /// </summary>
    /// <param name="word">The word, without punctuation.</param>
    /// <param name="set">The patterns to use.</param>
    /// <param name="positions">
    /// List the positions are appended to: a position <c>i</c> means the word may be broken after its first
    /// <c>i</c> characters.
    /// </param>
    /// <exception cref="ArgumentNullException">When <paramref name="word"/> or <paramref name="set"/> is null.</exception>
    public static void Opportunities(string word, HyphenationPatternSet set, List<int> positions)
    {
        ArgumentNullException.ThrowIfNull(word);
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(positions);

        if (word.Length < set.LeftMin + set.RightMin)
            return;

        string lower = word.ToLowerInvariant();
        List<int>? exception = set.ExceptionPositions(lower);

        if (exception is not null)
        {
            foreach (int position in exception)
            {
                if (position >= set.LeftMin && position <= word.Length - set.RightMin)
                    positions.Add(position);
            }

            return;
        }

        string padded = "." + lower + ".";
        var scores = new int[padded.Length + 2];
        HyphenationTrie root = set.Trie;

        for (int start = 0; start < padded.Length; start++)
        {
            HyphenationTrie? node = root;

            for (int index = start; index < padded.Length; index++)
            {
                node = node.Step(padded[index]);

                if (node is null)
                    break;

                if (node.Scores is not { Length: > 0 } values)
                    continue;

                for (int gap = 0; gap < values.Length; gap++)
                {
                    int target = start + gap;

                    if (target < scores.Length && values[gap] > scores[target])
                        scores[target] = values[gap];
                }
            }
        }

        // Scores are indexed in the padded word, whose dot shifts every position by one: a break after the word's i
        // characters is the gap before the (i + 1)-th padded character.
        for (int position = set.LeftMin; position <= word.Length - set.RightMin; position++)
        {
            int gap = position + 1;

            if (gap < scores.Length && (scores[gap] & 1) == 1)
                positions.Add(position);
        }
    }

    /// <summary>
    /// The word with a hyphen at every place a break is allowed, which is what a test or a dump wants to read.
    /// </summary>
    /// <param name="word">The word to hyphenate.</param>
    /// <param name="set">The patterns to use.</param>
    /// <returns>The word with its optional hyphens written as <c>-</c>.</returns>
    /// <exception cref="ArgumentNullException">When <paramref name="word"/> or <paramref name="set"/> is null.</exception>
    public static string Hyphenate(string word, HyphenationPatternSet set)
    {
        ArgumentNullException.ThrowIfNull(word);
        ArgumentNullException.ThrowIfNull(set);

        var positions = new List<int>();
        Opportunities(word, set, positions);

        if (positions.Count == 0)
            return word;

        var text = new StringBuilder(word.Length + positions.Count);
        int previous = 0;

        foreach (int position in positions)
        {
            text.Append(word, previous, position - previous);
            text.Append('-');
            previous = position;
        }

        text.Append(word, previous, word.Length - previous);
        return text.ToString();
    }
}
