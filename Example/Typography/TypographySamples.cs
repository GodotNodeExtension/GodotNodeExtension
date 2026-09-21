using System.Collections.Generic;
using Godot;
using GodotNodeExtension.Component.Typography.Core.Model;

namespace GodotNodeExtension.Example.Typography;

/// <summary>
/// The sample documents the example scenes show: one per language, plus one that puts everything together.
/// <para>
/// A sample is a small specification walkthrough rather than a block of lorem ipsum. It is written as a list of
/// blocks - a heading, a clause that states what the convention requires, and then an example of that requirement
/// being met by the layout - so a reader sees each rule and its rendering next to each other. The blocks carry the
/// language they are written in, the indent and alignment they ask for, and the annotations or marks they
/// demonstrate, which is also the only reason the samples touch the input format at all.
/// </para>
/// <para>
/// Each language's blocks live in their own file (<c>TypographySamples.*.cs</c>), so a sample can be read, edited or
/// translated without touching another language's.
/// </para>
/// </summary>
public static partial class TypographySamples
{
    /// <summary>Which sample document to build.</summary>
    public enum SampleId
    {
        /// <summary>Simplified Chinese: prohibition, punctuation squeeze, the CJK/Latin gap, pinyin.</summary>
        ChineseSimplified,

        /// <summary>Traditional Chinese: its quotation marks, the centred ellipsis, Bopomofo beside the character.</summary>
        ChineseTraditional,

        /// <summary>Japanese: kana prohibition, furigana between the lines, emphasis dots.</summary>
        Japanese,

        /// <summary>Korean: its prohibition set, the narrow sentence marks, Hangul with Hanja and Latin.</summary>
        Korean,

        /// <summary>English: line breaking at spaces, automatic hyphenation, justification.</summary>
        English,

        /// <summary>Right to left: Arabic and Hebrew paragraphs, with Latin and numbers inside them.</summary>
        RightToLeft,

        /// <summary>Vertical writing: columns, annotations and marks on the column's side, turned Latin runs.</summary>
        Vertical,

        /// <summary>One document, several languages: each paragraph by its own convention.</summary>
        MixedLanguages,

        /// <summary>Line breaking under pressure: runs of punctuation, hanging marks, unbreakable units.</summary>
        LineBreakingStress,

        /// <summary>Everything at once, as one long document.</summary>
        Everything,
    }

    /// <summary>What a block is, which decides how big the sample sets it.</summary>
    public enum BlockKind
    {
        /// <summary>The title of a section.</summary>
        Heading,

        /// <summary>A clause: what a convention requires, in prose.</summary>
        Clause,

        /// <summary>A demonstrated example of the clause above it.</summary>
        Example,
    }

    /// <summary>
    /// One paragraph of a sample: the prose or the example, and what it demonstrates.
    /// </summary>
    /// <param name="Kind">Heading, clause or example.</param>
    /// <param name="Text">The text itself.</param>
    /// <param name="Reading">Annotation over this paragraph, or null. Empty when there is none.</param>
    /// <param name="Distribution">How the annotation is distributed: one reading for the paragraph, or one per character.</param>
    /// <param name="Emphasis">Emphasis mark on every character of the paragraph, or none.</param>
    /// <param name="Underline">Whether the paragraph is underlined.</param>
    /// <param name="Language">Language this paragraph declares, or null to let the document's stand.</param>
    /// <param name="FirstLineIndent">First-line indent in characters, or null for the language's own.</param>
    /// <param name="Alignment">Alignment this paragraph asks for, or null for the language's own.</param>
    public readonly record struct Block(
        BlockKind Kind,
        string Text,
        string Reading = "",
        RubyDistribution Distribution = RubyDistribution.Mono,
        EmphasisMarkStyle Emphasis = EmphasisMarkStyle.None,
        bool Underline = false,
        string? Language = null,
        int? FirstLineIndent = null,
        TextAlignment? Alignment = null);

    /// <summary>Paragraph spacing a block asks for, which is what separates the sections of a sample.</summary>
    /// <param name="kind">Kind of block.</param>
    /// <returns>The spacing in pixels.</returns>
    public static float SpacingAfter(BlockKind kind) => kind switch
    {
        BlockKind.Heading => 6f,
        BlockKind.Clause => 2f,
        _ => 10f,
    };

    /// <summary>
    /// Build one sample: its blocks become elements, with paragraph breaks between them so that each block is a
    /// paragraph of the document and can carry its own language, indent and spacing.
    /// </summary>
    /// <param name="sample">Which sample to build.</param>
    /// <param name="font">Font the text is shaped with.</param>
    /// <param name="fontSize">Body size in pixels; headings and examples are scaled from it.</param>
    /// <param name="color">Colour the text is drawn in.</param>
    /// <returns>The elements, ready to send to the server.</returns>
    public static List<DrawElement> Build(SampleId sample, Font font, int fontSize, Color color)
    {
        var elements = new List<DrawElement>();

        foreach (Block block in BlocksOf(sample))
        {
            int size = block.Kind switch
            {
                BlockKind.Heading => Mathf.RoundToInt(fontSize * 1.35f),
                BlockKind.Example => Mathf.RoundToInt(fontSize * 1.15f),
                _ => fontSize,
            };

            Color tint = block.Kind switch
            {
                BlockKind.Heading => color,
                BlockKind.Clause => new Color(color.R, color.G, color.B, 0.72f),
                _ => color,
            };

            elements.Add(new DrawElement
            {
                Type = DrawElement.ElementType.Text,
                Text = block.Text,
                Font = font,
                FontSize = size,
                Color = tint,
                IsUnderline = block.Underline,
                EmphasisMark = block.Emphasis,
                Ruby = block.Reading.Length == 0
                    ? null
                    : new RubySpec
                    {
                        Text = block.Reading,
                        Distribution = block.Distribution,
                    },
                ParagraphSettings = new ParagraphSettings
                {
                    LanguageTag = block.Language,
                    FirstLineIndent = block.FirstLineIndent,
                    Alignment = block.Alignment,
                    SpacingAfter = SpacingAfter(block.Kind),
                },
                ExtensionId = -1,
                TextEffectId = -1,
            });

            elements.Add(new DrawElement
            {
                Type = DrawElement.ElementType.Text,
                Font = font,
                FontSize = size,
                IsParagraphBreak = true,
                ExtensionId = -1,
                TextEffectId = -1,
            });
        }

        return elements;
    }

    /// <summary>
    /// The kind of every element <see cref="Build"/> produces, in the same order: a block's own element carries its
    /// kind and the paragraph break after it carries none. The layout reports source indices, not block kinds, so
    /// this is what lets a reader of a laid-out page tell the blocks that demonstrate a rule from the prose that
    /// states it - the example canvas outlines exactly those.
    /// </summary>
    /// <param name="sample">Which sample.</param>
    /// <returns>One entry per element of <see cref="Build"/>, <c>null</c> for a paragraph break.</returns>
    public static IReadOnlyList<BlockKind?> BuildKinds(SampleId sample)
    {
        var kinds = new List<BlockKind?>();

        foreach (Block block in BlocksOf(sample))
        {
            kinds.Add(block.Kind);
            kinds.Add(null);
        }

        return kinds;
    }

    /// <summary>The blocks of one sample.</summary>
    /// <param name="sample">Which sample.</param>
    /// <returns>Its blocks, in reading order.</returns>
    private static Block[] BlocksOf(SampleId sample) => sample switch
    {
        SampleId.ChineseSimplified => ChineseSimplified(),
        SampleId.ChineseTraditional => ChineseTraditional(),
        SampleId.Japanese => Japanese(),
        SampleId.Korean => Korean(),
        SampleId.English => English(),
        SampleId.RightToLeft => RightToLeft(),
        SampleId.Vertical => Vertical(),
        SampleId.MixedLanguages => MixedLanguages(),
        SampleId.LineBreakingStress => LineBreakingStress(),
        _ => Everything(),
    };
}
