namespace GodotNodeExtension.Component.Typography.Core.Hyphenation;

using System;
using System.Collections.Generic;

/// <summary>
/// One language variant's hyphenation patterns and the metadata that makes them traceable.
/// <para>
/// The table is data: a list of Liang patterns (a letter sequence with a digit in the gaps it scores) plus the
/// exceptions the pattern file states, the shortest word parts allowed on either side of a break, and where the data
/// came from — version, source file, SHA-256 and licence, because hyph-utf8 collects pattern files under different
/// terms and a table that cannot name its licence cannot be shipped.
/// </para>
/// <para>
/// Patterns are matched through a trie built on first use: a language's patterns are tens of thousands of strings and
/// building the trie costs a few milliseconds once, while every word after that is a handful of dictionary lookups.
/// </para>
/// </summary>
public sealed class HyphenationPatternSet
{
    /// <summary>Identifier a language profile names, for example <c>en-us</c>.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>File the data came from.</summary>
    public string Source { get; init; } = string.Empty;

    /// <summary>Version the source states.</summary>
    public string Version { get; init; } = string.Empty;

    /// <summary>SHA-256 of the source data.</summary>
    public string Sha256 { get; init; } = string.Empty;

    /// <summary>Licence the source states.</summary>
    public string Licence { get; init; } = string.Empty;

    /// <summary>Shortest word part allowed before a break (TeX's left hyphen minimum).</summary>
    public int LeftMin { get; init; } = 2;

    /// <summary>Shortest word part allowed after a break (TeX's right hyphen minimum).</summary>
    public int RightMin { get; init; } = 2;

    /// <summary>The patterns, one per line.</summary>
    public string PatternLines { get; init; } = string.Empty;

    /// <summary>The exceptions, as hyphenated words (a word without hyphens is never hyphenated).</summary>
    public string ExceptionLines { get; init; } = string.Empty;

    private Dictionary<string, List<int>>? _exceptions;
    private HyphenationTrie? _trie;

    /// <summary>The patterns as a trie, built once.</summary>
    internal HyphenationTrie Trie => _trie ??= HyphenationTrie.Build(PatternLines);

    /// <summary>
    /// The exception positions of a word, or null when the word is not an exception.
    /// <para>
    /// A word in the exception list <em>without</em> any hyphen is a word the language refuses to hyphenate, which is
    /// why this returns an empty list rather than null for it: "no positions" and "not an exception" are different
    /// answers, and only the second one falls back to the patterns.
    /// </para>
    /// </summary>
    /// <param name="word">Word to look up, already lowercased.</param>
    /// <returns>The positions, empty when the word is never hyphenated, or null when it is not listed.</returns>
    internal List<int>? ExceptionPositions(string word)
    {
        if (_exceptions is null)
        {
            _exceptions = new Dictionary<string, List<int>>(StringComparer.Ordinal);

            foreach (string line in ExceptionLines.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var positions = new List<int>();
                var plain = new System.Text.StringBuilder(line.Length);

                foreach (char character in line)
                {
                    if (character == '-')
                    {
                        // A hyphen marks the gap it stands in, which is the number of letters before it.
                        positions.Add(plain.Length);
                        continue;
                    }

                    plain.Append(character);
                }

                _exceptions[plain.ToString()] = positions;
            }
        }

        return _exceptions.GetValueOrDefault(word);
    }
}
