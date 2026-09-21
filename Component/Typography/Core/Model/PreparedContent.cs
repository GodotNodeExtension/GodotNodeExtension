using System.Collections.Generic;

namespace GodotNodeExtension.Component.Typography.Core.Model;

/// <summary>
/// Paragraph boundary info within <see cref="PreparedContent"/>.
/// Stores the range of segments and paragraph-specific settings.
/// </summary>
public struct PreparedParagraph
{
    /// <summary>Index of the first segment in this paragraph (inclusive).</summary>
    public int StartSegmentIndex { get; set; }

    /// <summary>Index past the last segment in this paragraph (exclusive).</summary>
    public int EndSegmentIndex { get; set; }

    /// <summary>Paragraph-level settings (indent, spacing, alignment override).</summary>
    public ParagraphSettings Settings { get; init; }
}

/// <summary>
/// Result of the Prepare phase. Contains all measured segments ready for layout.
/// This is cached and reused across multiple Layout calls (e.g., on resize).
/// Analogous to pretext's PreparedText.
/// </summary>
public class PreparedContent
{
    /// <summary>All text segments in document order.</summary>
    public List<TextSegment> Segments { get; } = [];

    /// <summary>Paragraph boundary information. Each entry spans a range of segments.</summary>
    public List<PreparedParagraph> Paragraphs { get; } = [];

    /// <summary>Total number of source DrawElements that were prepared.</summary>
    public int SourceCount { get; internal set; }

    /// <summary>
    /// Orientation of the geometry this content is expressed in: which way a line runs (the inline axis) and which
    /// way lines advance (the block axis).
    /// <para>
    /// It is fixed when the content is measured, because the mode belongs to the request and the measurement depends
    /// on it, and it travels with the content so no stage has to be handed it separately.
    /// </para>
    /// </summary>
    public LayoutAxes Axes { get; init; }

    /// <summary>
    /// Annotations measured for this content, keyed by the source element that carries them. Measured here
    /// because a ruby is width-independent: it is part of the compile phase, and a resize must not re-shape it.
    /// </summary>
    public Dictionary<int, MeasuredRuby> Rubies { get; } = [];

    /// <summary>
    /// Cluster indices that sit inside a ruby group. A boundary between two of them must not break, so an
    /// annotation never separates from the base text it annotates; the check lives here rather than in a language
    /// rule because it is a framework-level invariant (like number-unit atomicity).
    /// </summary>
    public HashSet<int> InsideRubyGroup { get; } = [];

    /// <summary>
    /// Create an empty PreparedContent.
    /// </summary>
    public PreparedContent()
    {
    }

    /// <summary>
    /// Create a PreparedContent from pre-built segment and paragraph lists.
    /// </summary>
    /// <param name="segments">Measured segments.</param>
    /// <param name="paragraphs">Paragraph boundaries.</param>
    /// <param name="sourceCount">Number of source DrawElements.</param>
    public PreparedContent(List<TextSegment> segments, List<PreparedParagraph> paragraphs, int sourceCount)
    {
        Segments = segments;
        Paragraphs = paragraphs;
        SourceCount = sourceCount;
    }

    /// <summary>
    /// Build paragraph boundaries from the segment list.
    /// Scans <see cref="Segments"/> and groups consecutive segments with the same
    /// <see cref="TextSegment.ParagraphIndex"/> into <see cref="PreparedParagraph"/> entries.
    /// </summary>
    internal void BuildParagraphs()
    {
        Paragraphs.Clear();
        if (Segments.Count == 0) return;

        int currentParaIndex = Segments[0].ParagraphIndex;
        int startIndex = 0;

        for (int i = 1; i < Segments.Count; i++)
        {
            if (Segments[i].ParagraphIndex != currentParaIndex)
            {
                Paragraphs.Add(new PreparedParagraph
                {
                    StartSegmentIndex = startIndex,
                    EndSegmentIndex = i,
                    Settings = ParagraphSettingsOf(Segments[startIndex]),
                });
                currentParaIndex = Segments[i].ParagraphIndex;
                startIndex = i;
            }
        }

        // Final paragraph
        Paragraphs.Add(new PreparedParagraph
        {
            StartSegmentIndex = startIndex,
            EndSegmentIndex = Segments.Count,
            Settings = ParagraphSettingsOf(Segments[startIndex]),
        });
    }

    /// <summary>
    /// Paragraph settings are carried by the element that starts the paragraph; before this existed
    /// every paragraph reported an empty <see cref="ParagraphSettings"/>, which made the whole
    /// per-paragraph override path unreachable.
    /// </summary>
    private static ParagraphSettings ParagraphSettingsOf(in TextSegment first) =>
        first.Source.ParagraphSettings ?? new ParagraphSettings();
}
