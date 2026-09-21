using GodotNodeExtension.Component.Typography.Core.Model;
namespace GodotNodeExtension.Component.Typography.Core;

/// <summary>
/// Decides which script a character belongs to when the code point alone does not say.
/// <para>
/// Quotation marks, dashes and ellipses are Common: the same code point is CJK punctuation in Chinese text and
/// Western punctuation in English text, and it is the mixing place most likely to be wrong. U+201C is an
/// opening bracket under the Chinese convention (it must not end a line) and an ordinary quotation mark under
/// the Western one, and a word like <c>don’t</c> is one word only because its apostrophe is read as Western.
/// </para>
/// <para>
/// The decision is made from script evidence around the character, never from the code point: a character that
/// happens to sit next to Latin letters is Western there, regardless of the paragraph's language. The evidence
/// follows the content the mark belongs to — an opening mark looks at what it opens, a closing mark at what it
/// closes — which is the convention both CJK requirements documents state for quotations.
/// </para>
/// <para>
/// This resolver deliberately stops at evidence. When there is none (the text is only the mark), the caller
/// keeps its own classification; a language-dependent fallback belongs to the rule set that governs the
/// paragraph, which is where the language is known.
/// </para>
/// </summary>
public static class ContextualRoleResolver
{
    /// <summary>Opening quotation marks whose role depends on the text around them.</summary>
    private const string OpeningQuotes = "\u2018\u201C";

    /// <summary>Closing quotation marks whose role depends on the text around them.</summary>
    private const string ClosingQuotes = "\u2019\u201D";

    /// <summary>Marks that follow the text they sit in: the em dash and the horizontal ellipsis.</summary>
    private const string NeutralMarks = "\u2014\u2026";

    /// <summary>
    /// Whether the character's role depends on the surrounding script at all.
    /// </summary>
    /// <param name="character">The character to test.</param>
    /// <returns>True when <see cref="Resolve"/> can say something about it.</returns>
    public static bool IsContextDependent(char character) =>
        OpeningQuotes.Contains(character)
        || ClosingQuotes.Contains(character)
        || NeutralMarks.Contains(character);

    /// <summary>
    /// Whether a context-dependent character is used in a Western context.
    /// </summary>
    /// <param name="text">The text the character belongs to.</param>
    /// <param name="index">Index of the character in that text.</param>
    /// <returns>
    /// True for a Western context, false for a CJK one, and null when the text offers no evidence either way.
    /// </returns>
    public static bool? Resolve(string text, int index)
    {
        if (string.IsNullOrEmpty(text) || index < 0 || index >= text.Length)
            return null;

        char character = text[index];

        // A mark that opens something follows what it opens; a mark that closes something follows what it
        // closes. Only when that side says nothing does the other side get a vote.
        bool looksForwardFirst = OpeningQuotes.Contains(character);

        ScriptEvidence forward = NextEvidence(text, index);
        ScriptEvidence backward = PreviousEvidence(text, index);

        ScriptEvidence first = looksForwardFirst ? forward : backward;
        ScriptEvidence second = looksForwardFirst ? backward : forward;

        foreach (ScriptEvidence evidence in new[] { first, second })
        {
            if (evidence != ScriptEvidence.None)
                return evidence == ScriptEvidence.Western;
        }

        return null;
    }

    /// <summary>What the text around a mark says about its script.</summary>
    private enum ScriptEvidence
    {
        /// <summary>Nothing to go on.</summary>
        None,

        /// <summary>CJK letters (Han, kana or hangul) are the closest letters.</summary>
        Cjk,

        /// <summary>Latin letters are the closest letters.</summary>
        Western,
    }

    /// <summary>
    /// The script of the closest letter after an index, looking through everything that is not a letter
    /// itself: marks, spaces, digits and punctuation carry no script of their own.
    /// </summary>
    /// <param name="text">The text to scan.</param>
    /// <param name="index">Index of the mark.</param>
    /// <returns>The evidence, or <see cref="ScriptEvidence.None"/>.</returns>
    private static ScriptEvidence NextEvidence(string text, int index)
    {
        for (int i = index + 1; i < text.Length; i++)
        {
            ScriptEvidence evidence = EvidenceOf(text[i]);

            if (evidence != ScriptEvidence.None)
            {
                // A mark that is itself context-dependent is not evidence; keep looking.
                if (IsContextDependent(text[i]))
                    continue;

                return evidence;
            }
        }

        return ScriptEvidence.None;
    }

    /// <summary>
    /// The script of the closest letter before an index, with the same rules as <see cref="NextEvidence"/>.
    /// </summary>
    /// <param name="text">The text to scan.</param>
    /// <param name="index">Index of the mark.</param>
    /// <returns>The evidence, or <see cref="ScriptEvidence.None"/>.</returns>
    private static ScriptEvidence PreviousEvidence(string text, int index)
    {
        for (int i = index - 1; i >= 0; i--)
        {
            ScriptEvidence evidence = EvidenceOf(text[i]);

            if (evidence != ScriptEvidence.None)
            {
                if (IsContextDependent(text[i]))
                    continue;

                return evidence;
            }
        }

        return ScriptEvidence.None;
    }

    /// <summary>
    /// What a character says about its script: letters answer, everything else abstains.
    /// </summary>
    /// <param name="character">The character to judge.</param>
    /// <returns>The evidence.</returns>
    private static ScriptEvidence EvidenceOf(char character)
    {
        if (char.IsWhiteSpace(character) || char.IsDigit(character))
            return ScriptEvidence.None;

        CharacterClass cls = CharClassifier.Classify(character);

        return cls switch
        {
            CharacterClass.Ideograph => ScriptEvidence.Cjk,
            CharacterClass.Latin => ScriptEvidence.Western,
            _ => ScriptEvidence.None,
        };
    }
}
