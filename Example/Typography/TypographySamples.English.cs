using GodotNodeExtension.Component.Typography.Core.Model;

namespace GodotNodeExtension.Example.Typography;

/// <summary>
/// The English sample: line breaking per UAX #14, hyphenation in a narrow measure and justification, each clause
/// followed by the layout meeting it.
/// </summary>
public static partial class TypographySamples
{
    /// <summary>Blocks of the English sample.</summary>
    /// <returns>The blocks, in reading order.</returns>
    private static Block[] English() =>
    [
        new(BlockKind.Heading, "English typography (UAX #14)", Language: "en"),

        new(BlockKind.Clause,
            "Line breaking (UAX #14): a line may end after a space, and never inside a word. Every space between "
            + "words is a break opportunity and no pair of letters inside a word is one, so a word that does not "
            + "fit moves to the next line whole instead of being cut. The example below is set in the same narrow "
            + "measure as the rest of this sample, and every line of it ends where a space allowed it to end.",
            Language: "en"),
        new(BlockKind.Example,
            "Every line of this paragraph ends where a space let it end, and no word is ever cut in two. The line "
            + "breaker walks the text, keeps the words that still fit, and starts a new line at the space before "
            + "the word that does not. Nothing inside a word is a break opportunity, so an unfit word moves down "
            + "the column whole, however much room is left behind it.",
            Language: "en"),

        new(BlockKind.Clause,
            "Automatic hyphenation in a narrow measure: when a word is wider than the measure, the layout may "
            + "break it at the positions the language's hyphenation patterns allow, and a broken line ends with a "
            + "hyphen. The patterns belong to the language and not to the measure, so a word breaks at the same "
            + "places in every column that is too narrow to hold it.",
            Language: "en"),
        new(BlockKind.Example,
            "A measure narrower than the words themselves puts the patterns to work: internationalization and "
            + "institutionalization break, as do counterrevolutionaries, antidisestablishmentarianism, "
            + "electroencephalography, spectrophotometrically, uncharacteristically and "
            + "pseudohypoparathyroidism. Each of those is wider than this column, so each one is broken where "
            + "its patterns allow, and each breaks in the same places wherever this sample is set.",
            Language: "en"),

        new(BlockKind.Clause,
            "The soft hyphen (U+00AD) is a break opportunity the author writes into a word by hand. A line may "
            + "end where one of those marks stands, whether or not the language's patterns would allow a break "
            + "there, and a line that ends at one is marked with a hyphen in the usual way. It is the writer's "
            + "own choice of break point, which is what the paragraph below exercises.",
            Language: "en"),
        new(BlockKind.Example,
            "A word may also carry a break the writer chose: trans\u00adsub\u00adstan\u00adti\u00adation may end "
            + "after any of those marks, coun\u00adter\u00adrev\u00adolu\u00adtion\u00adar\u00adies and "
            + "in\u00adcom\u00adpre\u00adhen\u00adsi\u00adbil\u00adities too, and the line that ends at one of "
            + "them is marked with a hyphen just as a pattern break is.",
            Language: "en"),

        new(BlockKind.Clause,
            "Justification (TextAlignment.Justify): the slack of a line goes into the spaces between its words, "
            + "so the right edge is straight down the column while the letters keep their own shapes and their "
            + "own distances. A word already set at the measure is left where it is, and the last line of a "
            + "paragraph has no slack to give, so it is set flush left like the rest of this sample.",
            Language: "en"),
        new(BlockKind.Example,
            "This paragraph is set justified: every line except the last is stretched to the measure by the "
            + "spaces between its words, and the right edge stands as straight as a rule. The letters themselves "
            + "keep their own shapes and their own distances, and no line is stretched so far that a reader would "
            + "stop to notice the gaps; the last line of the paragraph keeps its natural width and ends where the "
            + "text ends.",
            Language: "en", Alignment: TextAlignment.Justify),
    ];
}
