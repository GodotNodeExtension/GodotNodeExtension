using System;
using System.Collections.Generic;
using GodotNodeExtension.Component.Typography.Core.Model;
using GodotNodeExtension.Component.Typography.Core.Unicode;

namespace GodotNodeExtension.Component.Typography.Core;

/// <summary>
/// Segments DrawElement sequences into TextSegment sequences by character class boundaries.
/// Each segment contains characters of the same class. Also tracks paragraph boundaries.
/// This is step P1 of the Prepare phase.
/// </summary>
public class Segmenter
{
    private int _currentParagraphIndex;
    private int _blockDepth;
    private int _autoBlockDepth;

    /// <summary>
    /// Segment a complete DrawElement array into TextSegments.
    /// </summary>
    /// <param name="elements">Source elements to segment.</param>
    /// <returns>List of segments in document order.</returns>
    public List<TextSegment> Segment(ReadOnlySpan<DrawElement> elements)
    {
        var segments = new List<TextSegment>();
        _currentParagraphIndex = 0;

        int fixedBlockDepth = 0;

        for (int i = 0; i < elements.Length; i++)
        {
            // Skip elements inside a fixed-size block
            if (fixedBlockDepth > 0)
            {
                if (elements[i].IsBlockEnd)
                    fixedBlockDepth--;
                continue;
            }

            var element = elements[i];

            if (element.BlockInfo != null)
            {
                if (element.BlockInfo.IsAutoSize)
                {
                    // Auto-size block: emit BlockStart marker, continue segmenting inner elements
                    SegmentElement(ref element, i, segments);
                }
                else
                {
                    // Fixed-size block: emit Block marker and skip inner elements
                    SegmentElement(ref element, i, segments);
                    fixedBlockDepth++;
                }
                continue;
            }

            // Block end markers of auto-size blocks take the same path as any other element:
            // SegmentElement emits the BlockEnd marker itself (element.IsBlockEnd).
            SegmentElement(ref element, i, segments);
        }

        StampParagraphLanguages(segments);
        return segments;
    }

    /// <summary>
    /// Stamp every segment from <paramref name="from"/> onwards with the language of the paragraph it belongs
    /// to, without shaping or measuring anything.
    /// <para>
    /// The language a paragraph is laid out with is declared by the element that opens the paragraph (the
    /// same element whose <see cref="DrawElement.ParagraphSettings"/> the prepared content later reads), so
    /// the rule is "when the paragraph index changes, take the language from this segment's source element".
    /// Segments carry the answer because shaping comes before paragraph-level resolution and a shaper has to
    /// know the language to pick a font's localized forms.
    /// </para>
    /// </summary>
    /// <param name="segments">The segment list to stamp.</param>
    /// <param name="from">
    /// Index to start at. A range that continues a paragraph reads that paragraph's language from the segment
    /// before it, which is what streaming appends do.
    /// </param>
    internal static void StampParagraphLanguages(List<TextSegment> segments, int from = 0)
    {
        if (from < 0 || from > segments.Count)
            return;

        string? language = null;
        int paragraph = -1;

        if (from > 0)
        {
            language = segments[from - 1].LanguageTag;
            paragraph = segments[from - 1].ParagraphIndex;
        }

        for (int i = from; i < segments.Count; i++)
        {
            var segment = segments[i];

            if (segment.ParagraphIndex != paragraph)
            {
                paragraph = segment.ParagraphIndex;
                language = segment.Source.ParagraphSettings?.LanguageTag;
            }

            // A paragraph that named no language still says which it is when its text carries kana or Hangul: a
            // Japanese sentence inside a Chinese document is ordinary, and everything that follows the language -
            // where an annotation goes, how it is set, line breaking, prohibition - would otherwise be the
            // document's answer rather than the text's. The first segment that names a script decides for the
            // paragraph, and a declared language is never overridden.
            language ??= LanguageInference.FromText(segment.Source.Text);

            segment.LanguageTag = language;
            segments[i] = segment;
        }
    }

    /// <summary>
    /// Segment a single DrawElement and append results to the segment list.
    /// Used for incremental/streaming mode.
    /// </summary>
    /// <param name="element">The element to segment.</param>
    /// <param name="sourceIndex">Index of this element in the source list.</param>
    /// <param name="segments">Target list to append segments to.</param>
    public void SegmentSingle(ref DrawElement element, int sourceIndex, List<TextSegment> segments)
    {
        // Track fixed-size block nesting — skip internal elements
        if (element.BlockInfo != null)
        {
            if (element.BlockInfo.IsAutoSize)
            {
                // Auto-size block: emit BlockStart marker, process inner elements normally
                _autoBlockDepth++;
                SegmentElement(ref element, sourceIndex, segments);
            }
            else
            {
                // Fixed-size block: emit Block marker, skip inner elements
                _blockDepth++;
                SegmentElement(ref element, sourceIndex, segments);
            }
            return;
        }
        if (element.IsBlockEnd)
        {
            if (_autoBlockDepth > 0)
            {
                // End of auto-size block: emit BlockEnd marker
                _autoBlockDepth--;
                SegmentElement(ref element, sourceIndex, segments);
            }
            else if (_blockDepth > 0)
            {
                // End of fixed-size block: just decrement, don't emit
                _blockDepth--;
            }
            return;
        }
        if (_blockDepth > 0)
            return; // Skip elements inside a fixed-size block

        SegmentElement(ref element, sourceIndex, segments);
    }

    /// <summary>
    /// Get the current paragraph index (for streaming mode).
    /// </summary>
    public int CurrentParagraphIndex => _currentParagraphIndex;

    /// <summary>
    /// Reset the segmenter state for reuse.
    /// </summary>
    public void Reset()
    {
        _currentParagraphIndex = 0;
        _blockDepth = 0;
        _autoBlockDepth = 0;
    }

    private void SegmentElement(ref DrawElement element, int sourceIndex, List<TextSegment> segments)
    {
        // Handle paragraph break marker
        if (element.IsParagraphBreak)
        {
            segments.Add(new TextSegment
            {
                SourceIndex = sourceIndex,
                CharOffset = 0,
                CharLength = 0,
                Text = string.Empty,
                CharClass = CharacterClass.Break,
                Width = 0,
                CanBreakAfter = true,
                ParagraphIndex = _currentParagraphIndex,
                Source = element,
            });
            _currentParagraphIndex++;
            return;
        }

        // Handle block start marker
        if (element.BlockInfo != null)
        {
            var isAutoSize = element.BlockInfo.IsAutoSize;
            segments.Add(new TextSegment
            {
                SourceIndex = sourceIndex,
                CharOffset = 0,
                CharLength = 0,
                Text = string.Empty,
                CharClass = isAutoSize ? CharacterClass.BlockStart : CharacterClass.Block,
                Width = isAutoSize ? 0 : element.BlockInfo.Size.X,
                CanBreakAfter = true,
                ParagraphIndex = _currentParagraphIndex,
                Source = element,
            });
            return;
        }

        // Handle block end marker
        if (element.IsBlockEnd)
        {
            segments.Add(new TextSegment
            {
                SourceIndex = sourceIndex,
                CharOffset = 0,
                CharLength = 0,
                Text = string.Empty,
                CharClass = CharacterClass.BlockEnd,
                Width = 0,
                CanBreakAfter = true,
                ParagraphIndex = _currentParagraphIndex,
                Source = element,
            });
            return;
        }

        switch (element.Type)
        {
            case DrawElement.ElementType.Text:
                SegmentText(ref element, sourceIndex, segments);
                break;

            case DrawElement.ElementType.Image:
            case DrawElement.ElementType.Rect:
            case DrawElement.ElementType.Line:
            case DrawElement.ElementType.ExtensionRegion:
                segments.Add(CreateNonTextSegment(ref element, sourceIndex));
                break;

            case DrawElement.ElementType.Action:
                // Action elements are zero-width markers
                segments.Add(new TextSegment
                {
                    SourceIndex = sourceIndex,
                    CharOffset = 0,
                    CharLength = 0,
                    Text = string.Empty,
                    CharClass = CharacterClass.NonText,
                    Width = 0,
                    CanBreakAfter = false,
                    ParagraphIndex = _currentParagraphIndex,
                    Source = element,
                });
                break;
        }
    }

    private void SegmentText(ref DrawElement element, int sourceIndex, List<TextSegment> segments)
    {
        var text = element.Text;
        if (string.IsNullOrEmpty(text)) return;

        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];

            // Role first, class second: a Common mark (a quotation mark, a dash, an ellipsis) obeys the script
            // it sits in, and the code point alone cannot say which. Everything else keeps its plain class.
            var charClass = ClassifyInContext(text, i);

            if (charClass == CharacterClass.Break)
            {
                // Emit a break segment and advance paragraph
                segments.Add(new TextSegment
                {
                    SourceIndex = sourceIndex,
                    CharOffset = i,
                    CharLength = 1,
                    Text = text[i].ToString(),
                    CharClass = CharacterClass.Break,
                    Width = 0,
                    CanBreakAfter = true,
                    ParagraphIndex = _currentParagraphIndex,
                    Source = element,
                });

                // Handle \r\n as a single break
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                    // Update the segment to include both characters
                    var last = segments[^1];
                    last.CharLength = 2;
                    last.Text = "\r\n";
                    segments[^1] = last;
                }

                _currentParagraphIndex++;
                i++;
                continue;
            }

            // Find the extent of the current character class run
            int runStart = i;

            switch (charClass)
            {
                case CharacterClass.Ideograph:
                    // Each CJK character is its own segment (supports per-character line breaking)
                    EmitSegment(segments, ref element, sourceIndex, i, 1, charClass);
                    i++;
                    break;

                case CharacterClass.Latin:
                    // Latin: one segment per stretch that cannot be broken internally. Unicode line breaking
                    // decides where a run may be cut, so a hyphenated word can break after its hyphen
                    // instead of being unbreakable everywhere.
                    i = ScanLatinRun(text, i);
                    EmitLatinRun(segments, ref element, sourceIndex, text, runStart, i);
                    break;

                case CharacterClass.Space:
                    // Group consecutive spaces
                    while (i < text.Length && CharClassifier.Classify(text[i]) == CharacterClass.Space)
                        i++;
                    EmitSegment(segments, ref element, sourceIndex, runStart, i - runStart, charClass);
                    break;

                case CharacterClass.Tab:
                    // A tab is its own segment: it stands for a tab stop, so merging it with the spaces around it
                    // would move that stop. Its width is decided at line adjustment time, where the stops are known.
                    EmitSegment(segments, ref element, sourceIndex, i, 1, charClass);
                    i++;
                    break;

                case CharacterClass.PunctuationDash:
                case CharacterClass.PunctuationEllipsis:
                    // Dash/ellipsis pairs should stay together
                    i = ScanUnbreakableSymbolRun(text, i, charClass);
                    EmitSegment(segments, ref element, sourceIndex, runStart, i - runStart, charClass);
                    break;

                default:
                    // Single punctuation character per segment
                    EmitSegment(segments, ref element, sourceIndex, i, 1, charClass);
                    i++;
                    break;
            }
        }

        // Apply break-after rules to the emitted text segments
        ApplyBreakAfterRules(segments, sourceIndex);
    }

    /// <summary>
    /// Classify a character, resolving the ones whose role depends on the text around them.
    /// <para>
    /// A Common mark with no evidence either way keeps its plain classification: the language-dependent fallback
    /// belongs to the rule set that governs the paragraph, and the prepare phase does not know the language.
    /// </para>
    /// </summary>
    /// <param name="text">The element's text.</param>
    /// <param name="index">Index of the character.</param>
    /// <returns>The class to segment and lay out with.</returns>
    private static CharacterClass ClassifyInContext(string text, int index)
    {
        char character = text[index];
        CharacterClass plain = CharClassifier.Classify(character);

        if (!ContextualRoleResolver.IsContextDependent(character))
            return plain;

        return ContextualRoleResolver.Resolve(text, index) switch
        {
            true => CharacterClass.PunctuationWestern,
            false => plain,
            null => plain,
        };
    }


    /// <summary>
    /// Emit a Western run as one segment per stretch that cannot be broken internally, using Unicode line
    /// breaking (UAX #14) to find the cuts.
    /// <para>
    /// This keeps the line breaker language-agnostic: a break opportunity is expressed as a segment
    /// boundary, so the breaker never needs to know that a hyphen (or a slash, or a middle dot) is a place
    /// where Western text may break.
    /// </para>
    /// </summary>
    /// <param name="segments">Target list.</param>
    /// <param name="element">Source element.</param>
    /// <param name="sourceIndex">Index of the source element.</param>
    /// <param name="text">The element's text.</param>
    /// <param name="start">Start offset of the run.</param>
    /// <param name="end">End offset of the run (exclusive).</param>
    private void EmitLatinRun(
        List<TextSegment> segments,
        ref DrawElement element,
        int sourceIndex,
        string text,
        int start,
        int end)
    {
        var cuts = BreakOpportunitySplitter.SplitOffsets(text, start, end);
        int offset = start;

        foreach (int cut in cuts)
        {
            EmitSegment(segments, ref element, sourceIndex, offset, cut - offset, CharacterClass.Latin);
            offset = cut;
        }

        EmitSegment(segments, ref element, sourceIndex, offset, end - offset, CharacterClass.Latin);
    }

    /// <summary>
    /// Scan a run of Latin characters (letters, digits, and inline punctuation like hyphens).
    /// Does NOT include spaces — spaces are separate segments.
    /// </summary>
    private static int ScanLatinRun(string text, int start)
    {
        int i = start;
        while (i < text.Length)
        {
            var cls = CharClassifier.Classify(text[i]);
            if (cls != CharacterClass.Latin)
                break;
            i++;
        }
        return i;
    }

    /// <summary>
    /// Scan consecutive identical unbreakable symbols (e.g., ——, ……).
    /// </summary>
    private static int ScanUnbreakableSymbolRun(string text, int start, CharacterClass targetClass)
    {
        int i = start;
        while (i < text.Length && CharClassifier.Classify(text[i]) == targetClass)
            i++;
        return i;
    }

    /// <summary>First character of a segment, or '\0' when it has none.</summary>
    /// <param name="segment">The segment to read.</param>
    /// <returns>The first character, or '\0'.</returns>
    private static char FirstChar(in TextSegment segment) =>
        string.IsNullOrEmpty(segment.Text) ? '\0' : segment.Text[0];

    private void EmitSegment(List<TextSegment> segments, ref DrawElement element,
        int sourceIndex, int charOffset, int charLength, CharacterClass charClass)
    {
        var text = element.Text!.AsSpan(charOffset, charLength);
        float adjustableSpace = 0f;

        // Calculate adjustable space for punctuation
        if (charLength == 1)
        {
            adjustableSpace = CharClassifier.GetAdjustableSpace(element.Text![charOffset]);
        }

        segments.Add(new TextSegment
        {
            SourceIndex = sourceIndex,
            CharOffset = charOffset,
            CharLength = charLength,
            Text = text.ToString(),
            CharClass = charClass,
            Width = 0, // Will be filled by HarfBuzz in Phase 2+
            AdjustableSpace = adjustableSpace,
            MinWidth = 0, // Will be calculated after Width is measured
            CanBreakAfter = false, // Will be set by ApplyBreakAfterRules
            GlyphAdvances = null,
            Ascent = 0,
            Descent = 0,
            ParagraphIndex = _currentParagraphIndex,
            Source = element,
        });
    }

    private TextSegment CreateNonTextSegment(ref DrawElement element, int sourceIndex)
    {
        return new TextSegment
        {
            SourceIndex = sourceIndex,
            CharOffset = 0,
            CharLength = 0,
            Text = string.Empty,
            CharClass = CharacterClass.NonText,
            Width = element.Size.X,
            CanBreakAfter = true,
            ParagraphIndex = _currentParagraphIndex,
            Source = element,
        };
    }

    /// <summary>
    /// Apply CanBreakAfter rules to segments that were just emitted for a given source element.
    /// Rules based on CLREQ §6.1 and character class logic.
    /// </summary>
    private static void ApplyBreakAfterRules(List<TextSegment> segments, int sourceIndex)
    {
        // Walk backwards through segments from this source element
        for (int i = segments.Count - 1; i >= 0; i--)
        {
            if (segments[i].SourceIndex != sourceIndex) break;
            if (segments[i].CharClass == CharacterClass.Break) continue;

            var seg = segments[i];
            seg.CanBreakAfter = DetermineCanBreakAfter(seg.CharClass, segments, i);
            segments[i] = seg;
        }
    }

    private static bool DetermineCanBreakAfter(CharacterClass cls, List<TextSegment> segments, int index)
    {
        return cls switch
        {
            // After CJK ideograph: generally breakable (unless next is prohibited at line start)
            CharacterClass.Ideograph => true,

            // After closing punctuation: breakable
            CharacterClass.PunctuationClose => true,

            // After pause/stop: breakable
            CharacterClass.PunctuationPauseStop => true,

            // After opening punctuation: NOT breakable (content must follow)
            CharacterClass.PunctuationOpen => false,

            // A Common mark resolved as Western keeps the shape of the mark: an opening mark holds its content,
            // a closing mark or a dash may end a line. That is the distinction the Unicode rules also draw.
            CharacterClass.PunctuationWestern =>
                CharClassifier.Classify(FirstChar(segments[index])) is not CharacterClass.PunctuationOpen,

            // After interpunct: breakable
            CharacterClass.PunctuationInterpunct => true,

            // After dash/ellipsis run: breakable (the run itself is unbreakable internally)
            CharacterClass.PunctuationDash => true,
            CharacterClass.PunctuationEllipsis => true,

            // After Latin word: breakable at word boundary (space follows or CJK follows)
            CharacterClass.Latin => true,

            // After space: breakable
            CharacterClass.Space => true,

            // After a tab: not breakable. The tab is an alignment point and what follows it belongs to that stop,
            // so the line may not be cut between them.
            CharacterClass.Tab => false,

            // After non-text: breakable
            CharacterClass.NonText => true,

            // After block: breakable
            CharacterClass.Block => true,

            // After block start/end: breakable
            CharacterClass.BlockStart => true,
            CharacterClass.BlockEnd => true,

            // Break: always breakable
            CharacterClass.Break => true,

            _ => false,
        };
    }
}
