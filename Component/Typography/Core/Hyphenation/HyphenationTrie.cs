namespace GodotNodeExtension.Component.Typography.Core.Hyphenation;

using System.Collections.Generic;
using System.Text;

/// <summary>
/// The pattern trie Liang's algorithm walks: every node is a letter, and a node that ends a pattern carries the
/// scores that pattern states for the gaps around it.
/// <para>
/// A pattern is a letter sequence with a digit in the gaps it scores (Teil 1 of `1na5tio`), so a trie is the natural
/// shape: walking the word once per starting position visits every pattern that could match there, and the score of
/// each gap is the highest score any matching pattern gives it — which is why the digits are kept per node rather
/// than as one big table.
/// </para>
/// </summary>
internal sealed class HyphenationTrie
{
    private readonly Dictionary<char, HyphenationTrie> _next = [];
    private byte[]? _scores;

    /// <summary>The scores this node's pattern states, or null when no pattern ends here.</summary>
    internal byte[]? Scores => _scores;

    /// <summary>
    /// Build the trie from the patterns of a table.
    /// </summary>
    /// <param name="patternLines">One pattern per line.</param>
    /// <returns>The root of the trie.</returns>
    internal static HyphenationTrie Build(string patternLines)
    {
        var root = new HyphenationTrie();

        foreach (string line in patternLines.Split('\n'))
        {
            string pattern = line.Trim();

            if (pattern.Length > 0)
                root.Insert(pattern);
        }

        return root;
    }

    /// <summary>The node reached by this letter, or null when no pattern continues that way.</summary>
    /// <param name="letter">Letter to step into.</param>
    /// <returns>The next node, or null.</returns>
    internal HyphenationTrie? Step(char letter) => _next.GetValueOrDefault(letter);

    private void Insert(string pattern)
    {
        var letters = new StringBuilder(pattern.Length);
        var scores = new List<int> { 0 };

        // A digit scores the gap it stands in; a letter opens the next gap. That is the whole syntax.
        foreach (char character in pattern)
        {
            if (character is >= '0' and <= '9')
            {
                scores[^1] = character - '0';
                continue;
            }

            letters.Append(character);
            scores.Add(0);
        }

        HyphenationTrie node = this;

        foreach (char letter in letters.ToString())
            node = node.Next(letter);

        var values = new byte[scores.Count];

        for (int i = 0; i < scores.Count; i++)
            values[i] = (byte)scores[i];

        node._scores = values;
    }

    private HyphenationTrie Next(char letter)
    {
        if (_next.TryGetValue(letter, out HyphenationTrie? node))
            return node;

        node = new HyphenationTrie();
        _next[letter] = node;
        return node;
    }
}
