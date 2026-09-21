using System;
using System.Collections.Generic;
using GodotNodeExtension.Component.Typography.Core.Fonts;
using GodotNodeExtension.Component.Typography.Core.Hyphenation;
using GodotNodeExtension.Component.Typography.Core.Model;
using GodotNodeExtension.Component.Typography.Core.Unicode;
using GodotNodeExtension.Component.Typography.Languages;
using SkiaSharp;

namespace GodotNodeExtension.Component.Typography.Core;

/// <summary>
/// Orchestrates the Prepare phase (P1→P2→P3) of the typography pipeline.
/// Converts DrawElement sequences into measured <see cref="PreparedContent"/> ready for layout.
///
/// Pipeline steps:
///   P1: Segmentation (via <see cref="Segmenter"/>)
///   P2: Character classification (done inline during P1)
///   P3: HarfBuzz shaping and measurement (via <see cref="HarfBuzzTextShaper"/>)
///
/// Thread-safe for single-writer usage on the typography server thread.
/// </summary>
public class ContentPreparer : IDisposable
{
    private readonly Segmenter _segmenter = new();
    private readonly Dictionary<ShaperKey, HarfBuzzTextShaper> _shaperCache = [];
    private readonly SKFontManager _fontManager;

    /// <summary>
    /// Effective typography per language tag, resolved once per tag. The compile phase asks per segment, and
    /// the answer depends only on the tag and the request, so resolving it per segment would be repeated work
    /// with an identical result.
    /// </summary>
    private readonly Dictionary<string, ResolvedTypography> _typographyCache = new(StringComparer.OrdinalIgnoreCase);

    private TypographySettings _requestSettings = new();
    private bool _disposed;

    /// <summary>
    /// Create a ContentPreparer with the default system font manager.
    /// </summary>
    public ContentPreparer() : this(SKFontManager.Default)
    {
    }

    /// <summary>
    /// Create a ContentPreparer with a specific font manager for fallback resolution.
    /// </summary>
    /// <param name="fontManager">Font manager for fallback typeface lookup.</param>
    public ContentPreparer(SKFontManager fontManager)
    {
        _fontManager = fontManager ?? throw new ArgumentNullException(nameof(fontManager));
    }

    /// <summary>
    /// Prepare a complete set of DrawElements into measured content.
    /// Runs P1 (segmentation), P2 (classification), and P3 (HarfBuzz measurement).
    /// </summary>
    /// <param name="elements">Source DrawElements to prepare.</param>
    /// <param name="request">
    /// The layout request whose language and overrides govern this content. It matters here, and not only
    /// during layout, because shaping is the first stage a language changes anything in: a paragraph that
    /// declares English must be shaped as English, not as whatever the document's other paragraphs are.
    /// </param>
    /// <returns>A <see cref="PreparedContent"/> with all segments measured and ready for layout.</returns>
    public PreparedContent Prepare(ReadOnlySpan<DrawElement> elements, TypographySettings? request = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        UseRequest(request);
        _bidiLevels.Clear();
        _paragraphBaseLevel.Clear();

        // P1 + P2: Segment and classify
        _segmenter.Reset();
        var segments = _segmenter.Segment(elements);
        _segmentList = segments;

        // Display forms come before measurement: they decide the text the shaper gets. Mirroring runs first: it
        // answers what the character looks like in a right-to-left run, and a language's own form for a quotation
        // mark then has the last word over it.
        ApplyMirroring(segments, 0, segments.Count);
        ApplyLetterforms(segments, 0, segments.Count);

        // Word hyphenation last among the text-level passes: it needs the paragraph language (which the stamping
        // before this established) and it cuts segments, so anything that reads them should run before it.
        ApplyHyphenation(segments, 0, segments.Count);

        // P3: Measure each segment via HarfBuzz
        MeasureSegments(segments);

        // Build result. The axes come from the same resolution the stages use, so the mode cannot be read two
        // different ways.
        var result = new PreparedContent(segments, [], elements.Length) { Axes = AxesFor(TypographyFor(null)) };
        result.BuildParagraphs();
        MeasureRubies(result, segments, 0, segments.Count);
        return result;
    }

    /// <summary>
    /// Incrementally prepare a single DrawElement and append to existing content.
    /// Used for streaming/progressive builds.
    /// </summary>
    /// <param name="content">Existing prepared content to append to.</param>
    /// <param name="element">The new element to prepare.</param>
    /// <param name="sourceIndex">Index of this element in the source list.</param>
    /// <param name="request">The layout request whose language and overrides govern this content.</param>
    public void AppendPrepare(
        PreparedContent content,
        ref DrawElement element,
        int sourceIndex,
        TypographySettings? request = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        UseRequest(request);

        int segmentsBefore = content.Segments.Count;

        // P1 + P2: Segment the new element
        _segmenter.SegmentSingle(ref element, sourceIndex, content.Segments);

        _segmentList = content.Segments;

        // The new segments need the language of their paragraph before they can be measured.
        Segmenter.StampParagraphLanguages(content.Segments, segmentsBefore);

        // Display forms come before measurement: they decide the text the shaper gets.
        ApplyMirroring(content.Segments, segmentsBefore, content.Segments.Count);
        ApplyLetterforms(content.Segments, segmentsBefore, content.Segments.Count);
        ApplyHyphenation(content.Segments, segmentsBefore, content.Segments.Count);

        // P3: Measure only the newly added segments
        MeasureSegmentsRange(content.Segments, segmentsBefore, content.Segments.Count);

        content.SourceCount = sourceIndex + 1;

        // Rebuild paragraphs (could be optimized to only update the tail)
        content.BuildParagraphs();
        MeasureRubies(content, content.Segments, segmentsBefore, content.Segments.Count);
    }

    /// <summary>
    /// Bidi levels per source element, computed on demand for the elements whose text is not purely left to right.
    /// <para>
    /// Keyed by source element because that is the unit the algorithm resolves: one pass per element covers every
    /// segment it was cut into, and a document with nothing right-to-left in it never fills this at all.
    /// </para>
    /// </summary>
    private readonly Dictionary<int, byte[]> _bidiLevels = [];

    /// <summary>Base level per paragraph, decided once from the paragraph's own text (rules P2 and P3).</summary>
    private readonly Dictionary<int, int> _paragraphBaseLevel = [];

    /// <summary>
    /// The segments of the content being prepared, so a paragraph's text can be rebuilt from them when its base
    /// direction has to be decided.
    /// </summary>
    private List<TextSegment>? _segmentList;

    /// <summary>
    /// The direction a segment is shaped in: its own run's, not the paragraph's.
    /// <para>
    /// This is the piece that makes mixed text come out right. A right-to-left paragraph that quotes a Latin word
    /// shapes that word left to right — otherwise its glyphs come out reversed — and a left-to-right document pays
    /// nothing for the question because it is answered by a single call that finds nothing right to left in it.
    /// </para>
    /// </summary>
    /// <param name="segment">Segment about to be shaped.</param>
    /// <returns>The direction the run is written in.</returns>
    private TextDirection DirectionOfSegment(in TextSegment segment)
    {
        ResolvedTypography typography = TypographyFor(segment.LanguageTag);

        if (typography.Direction != TextDirection.RightToLeft
            && !BidiResolver.HasRightToLeft(segment.Source.Text ?? string.Empty))
        {
            return typography.Direction;
        }

        byte[] levels = LevelsOfSource(segment);

        if (levels.Length == 0)
            return typography.Direction;

        int index = Math.Clamp(segment.CharOffset, 0, levels.Length - 1);
        return (levels[index] & 1) == 1 ? TextDirection.RightToLeft : TextDirection.LeftToRight;
    }

    /// <summary>
    /// Adopt the request the following segments are prepared with. A different request invalidates the
    /// per-language resolutions, which were computed with the previous one.
    /// </summary>
    /// <param name="request">The new request, or null to keep the current one.</param>
    private void UseRequest(TypographySettings? request)
    {
        if (request is null || ReferenceEquals(request, _requestSettings))
            return;

        _requestSettings = request;
        _typographyCache.Clear();
    }

    /// <summary>
    /// Orientation the layout of a request happens in: the writing mode its typography resolved to (the request's
    /// override, or the language's), and the content box' extent along the block axis.
    /// </summary>
    /// <param name="typography">The request's effective typography.</param>
    /// <returns>The axes the layout stages read.</returns>
    private static LayoutAxes AxesFor(ResolvedTypography typography) =>
        new(typography.WritingMode, typography.MaxWidth);

    /// <summary>
    /// Effective typography for one paragraph language, as the compile phase needs it (a segment knows its
    /// paragraph's language tag but not the settings object behind it).
    /// </summary>
    /// <param name="languageTag">Language of the segment's paragraph, or null when it declares none.</param>
    /// <returns>The merged parameters for that language.</returns>
    private ResolvedTypography TypographyFor(string? languageTag)
    {
        string key = languageTag ?? string.Empty;

        if (_typographyCache.TryGetValue(key, out ResolvedTypography? cached))
            return cached;

        ResolvedTypography resolved = LanguageProfileRegistry.Shared.Resolve(_requestSettings, languageTag);
        _typographyCache[key] = resolved;
        return resolved;
    }

    /// <summary>
    /// Cut words at the positions their language allows a break, so the line breaker can break inside a word instead
    /// of dropping the whole of it to the next line.
    /// <para>
    /// The cut happens here rather than in the segmenter because it needs the paragraph's language, which the
    /// segmenter does not know yet; a segment becomes as many segments as the word has break opportunities, which is
    /// the same shape the Unicode algorithm produces for punctuation and hyphens. A language without patterns (or a
    /// request that switched hyphenation off) leaves every segment as it was.
    /// </para>
    /// </summary>
    /// <param name="segments">Segment list to cut in place.</param>
    /// <param name="from">First segment to consider, inclusive.</param>
    /// <param name="to">One past the last segment to consider.</param>
    private void ApplyHyphenation(List<TextSegment> segments, int from, int to)
    {
        var positions = new List<int>();

        for (int i = from; i < to && i < segments.Count; i++)
        {
            TextSegment segment = segments[i];

            // Only a run of letters is a word: a run that already carries digits, a hyphen or a slash has its break
            // opportunities from the Unicode algorithm, and adding more there would move its pieces around.
            if (segment.CharClass != CharacterClass.Latin || segment.CharLength < 4
                || segment.DisplayText is not null || !IsWordOfLetters(segment.Text))
            {
                continue;
            }

            ResolvedTypography typography = TypographyFor(segment.LanguageTag);

            if (!typography.EnableHyphenation || typography.Hyphenation is not { } patterns)
                continue;

            positions.Clear();
            Hyphenator.Opportunities(segment.Text, patterns, positions);

            if (positions.Count == 0)
                continue;

            // The hyphen is shaped here, once per word: only the pieces that could end a line reserve it, and a piece
            // whose word continues on the next line has to fit the hyphen as well.
            SplitAt(segments, i, segment, positions, ShapeHyphen(segment));
            to += positions.Count;
        }
    }

    /// <summary>Whether every character of the text is a letter, which is what makes it a word to hyphenate.</summary>
    /// <param name="text">Text of the segment.</param>
    /// <returns>True when the text is letters only.</returns>
    private static bool IsWordOfLetters(string text)
    {
        foreach (char character in text)
        {
            if (!char.IsLetter(character))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Shape the hyphen a cut word may end a line with, in the font and size that word is set in. Null when the font
    /// cannot be resolved, in which case the pieces simply do not reserve one.
    /// </summary>
    /// <param name="segment">A piece of the word being cut.</param>
    /// <returns>The shaped hyphen, or null.</returns>
    private GlyphRun? ShapeHyphen(in TextSegment segment)
    {
        if (segment.Source.Font is null
            || !TryResolveTypeface(segment, out SKTypeface? typeface, out ulong fontId) || typeface is null)
        {
            return null;
        }

        float fontSize = segment.Source.FontSize > 0 ? segment.Source.FontSize : 16f;
        ResolvedTypography hyphenTypography = TypographyFor(segment.LanguageTag);
        var options = new ShapingOptions
        {
            Language = hyphenTypography.OpenTypeLanguageTag,
            Direction = TextDirection.LeftToRight,
            IsVertical = hyphenTypography.WritingMode != WritingMode.HorizontalTb,
        };

        HarfBuzzTextShaper shaper = GetOrCreateShaper(typeface, fontSize, options);
        Glyph[] glyphs = shaper.ShapeGlyphs("-", fontId, _fontManager);
        float width = 0f;

        foreach (Glyph glyph in glyphs)
            width += glyph.Advance;

        return new GlyphRun
        {
            FontId = fontId,
            FontSize = fontSize,
            Glyphs = glyphs,
            Origin = Godot.Vector2.Zero,
            Width = width,
        };
    }

    /// <summary>
    /// Resolve the typeface and catalog id a segment is drawn with, the way <see cref="MeasureTextSegment"/> does.
    /// </summary>
    /// <param name="segment">Segment to resolve for.</param>
    /// <param name="typeface">Receives the typeface, or null.</param>
    /// <param name="fontId">Receives the catalog id.</param>
    /// <returns>True when both were resolved.</returns>
    private static bool TryResolveTypeface(in TextSegment segment, out SKTypeface? typeface, out ulong fontId)
    {
        typeface = null;
        fontId = segment.Source.ResolvedFontId;

        if (fontId == 0 && segment.Source.Font is not null)
            fontId = FontCatalog.Shared.Resolve(segment.Source.Font);

        return fontId != 0 && FontCatalog.Shared.TryGetTypeface(fontId, out typeface) && typeface is not null;
    }

    /// <summary>
    /// Replace one segment with the pieces its break positions define, carrying the source offsets over so selection
    /// and hit testing still map back to what the caller wrote, and giving every piece that could end a line the
    /// hyphen it would end it with.
    /// </summary>
    /// <param name="segments">Segment list to modify.</param>
    /// <param name="index">Index of the segment being cut.</param>
    /// <param name="segment">The segment itself.</param>
    /// <param name="positions">Break positions inside the word, ascending.</param>
    /// <param name="hyphen">The shaped hyphen, or null when it could not be shaped.</param>
    private static void SplitAt(
        List<TextSegment> segments,
        int index,
        in TextSegment segment,
        List<int> positions,
        GlyphRun? hyphen)
    {
        var pieces = new List<TextSegment>(positions.Count + 1);
        int start = 0;

        foreach (int position in positions)
        {
            var piece = Piece(segment, start, position);
            piece.HyphenRun = hyphen;
            pieces.Add(piece);
            start = position;
        }

        pieces.Add(Piece(segment, start, segment.Text.Length));
        segments.RemoveAt(index);
        segments.InsertRange(index, pieces);
    }

    /// <summary>One piece of a cut word, with its source range shifted to the piece.</summary>
    /// <param name="segment">The word being cut.</param>
    /// <param name="start">First character of the piece.</param>
    /// <param name="end">One past its last character.</param>
    /// <returns>The piece, ready to be measured.</returns>
    private static TextSegment Piece(in TextSegment segment, int start, int end) => segment with
    {
        CharOffset = segment.CharOffset + start,
        CharLength = end - start,
        Text = segment.Text[start..end],
        CanBreakAfter = true,
        Width = 0f,
        MinWidth = 0f,
        GlyphAdvances = null,
        Glyphs = null,
        HyphenRun = null,
    };

    /// <summary>
    /// Replace the characters a right-to-left run draws as their mirror image (UAX #9, rule L4): a bracket, a
    /// quotation mark, an angle bracket.
    /// <para>
    /// The rule is per character and not per run: a character is mirrored when its own resolved level is odd, which
    /// is the level the algorithm reported for it. The substitution writes <see cref="TextSegment.DisplayText"/>,
    /// the same channel the language-specific forms use, so the source text, its ranges and the character class all
    /// stay what the caller wrote.
    /// </para>
    /// </summary>
    /// <param name="segments">Segment list to rewrite in place.</param>
    /// <param name="from">First segment to consider, inclusive.</param>
    /// <param name="to">One past the last segment to consider.</param>
    private void ApplyMirroring(List<TextSegment> segments, int from, int to)
    {
        for (int i = from; i < to; i++)
        {
            var segment = segments[i];

            if (segment.CharLength == 0 || string.IsNullOrEmpty(segment.Text))
                continue;

            byte[] levels = LevelsOfSource(segment);

            if (levels.Length == 0)
                continue;

            char[]? mirrored = null;

            for (int c = 0; c < segment.Text.Length; c++)
            {
                int index = Math.Clamp(segment.CharOffset + c, 0, levels.Length - 1);

                if ((levels[index] & 1) == 0)
                    continue;

                int codePoint = segment.Text[c];
                int replacement = BidiMirroringData.Mirror(codePoint);

                if (replacement == codePoint || replacement > char.MaxValue)
                    continue;

                mirrored ??= segment.Text.ToCharArray();
                mirrored[c] = (char)replacement;
            }

            if (mirrored is null)
                continue;

            segment.DisplayText = new string(mirrored);
            segments[i] = segment;
        }
    }

    /// <summary>
    /// The resolved levels of a segment's source text, computed once per source element and only when the text has
    /// something right to left in it; an empty array means the question does not arise.
    /// </summary>
    /// <param name="segment">Segment whose levels are wanted.</param>
    /// <returns>One level per code unit of the source element's text, or an empty array.</returns>
    private byte[] LevelsOfSource(in TextSegment segment)
    {
        string text = segment.Source.Text ?? string.Empty;

        if (text.Length == 0)
            return [];

        ResolvedTypography typography = TypographyFor(segment.LanguageTag);

        if (typography.Direction != TextDirection.RightToLeft && !BidiResolver.HasRightToLeft(text))
            return [];

        if (_bidiLevels.TryGetValue(segment.SourceIndex, out byte[]? levels))
            return levels;

        // The base direction belongs to the paragraph, not to the element: an element that carries no strong
        // character at all (a number, a punctuation mark) has no opinion, and asking it on its own would give the
        // digits of a right-to-left paragraph a left-to-right base.
        levels = BidiResolver.LevelsOf(text, BaseLevelOf(segment));
        _bidiLevels[segment.SourceIndex] = levels;
        return levels;
    }

    /// <summary>
    /// The base level in force for a segment: the paragraph's own, decided from the paragraph's text when the
    /// language does not simply say "right to left".
    /// </summary>
    /// <param name="segment">Segment whose paragraph is being resolved.</param>
    /// <returns>0 for a left-to-right paragraph, 1 for a right-to-left one.</returns>
    private int BaseLevelOf(in TextSegment segment)
    {
        if (TypographyFor(segment.LanguageTag).Direction == TextDirection.RightToLeft)
            return 1;

        if (_paragraphBaseLevel.TryGetValue(segment.ParagraphIndex, out int cached))
            return cached;

        string paragraph = ParagraphTextOf(segment.ParagraphIndex);
        int level = paragraph.Length == 0
            ? 0
            : BidiAlgorithm.ParagraphLevel(CodePointsOf(paragraph), BidiAlgorithm.Auto);

        _paragraphBaseLevel[segment.ParagraphIndex] = level;
        return level;
    }

    /// <summary>The text of one paragraph, rebuilt from the segments that belong to it.</summary>
    /// <param name="paragraphIndex">Paragraph to rebuild.</param>
    /// <returns>The paragraph's text, in logical order.</returns>
    private string ParagraphTextOf(int paragraphIndex)
    {
        if (_segmentList is not { } segments)
            return string.Empty;

        var text = new System.Text.StringBuilder();

        foreach (TextSegment segment in segments)
        {
            if (segment.ParagraphIndex == paragraphIndex && segment.CharLength > 0)
                text.Append(segment.Text);
        }

        return text.ToString();
    }

    /// <summary>The code points of a string, for the rules that work on code points rather than characters.</summary>
    /// <param name="text">Text to convert.</param>
    /// <returns>One code point per element.</returns>
    private static int[] CodePointsOf(string text)
    {
        var points = new List<int>(text.Length);

        for (int i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                points.Add(char.ConvertToUtf32(text[i], text[i + 1]));
                i++;
            }
            else
            {
                points.Add(text[i]);
            }
        }

        return [.. points];
    }

    /// <summary>
    /// Apply the display forms of each segment's language, before anything is measured: the form decides the
    /// width, so it has to be chosen while the layout is still width-independent.
    /// </summary>
    /// <param name="segments">Segment list to rewrite in place.</param>
    /// <param name="from">First segment to consider, inclusive.</param>
    /// <param name="to">One past the last segment to consider.</param>
    private void ApplyLetterforms(List<TextSegment> segments, int from, int to)
    {
        for (int i = from; i < to; i++)
        {
            var segment = segments[i];

            if (string.IsNullOrEmpty(segment.Text))
                continue;

            ResolvedTypography typography = TypographyFor(segment.LanguageTag);

            if (!typography.EnableLetterformSubstitution)
                continue;

            string? display = LetterformResolver.Substitute(
                segment.DisplayText ?? segment.Text, segment.CharClass, typography.Letterforms);

            if (display is null)
                continue;

            segment.DisplayText = display;
            segments[i] = segment;
        }
    }

    /// <summary>
    /// Measure the annotations the source elements of the given range carry, and mark the cluster positions a
    /// break may not split.
    /// <para>
    /// This belongs to the compile phase for the same reason the base text does: an annotation has a width and a
    /// height and both are independent of the line width, so a resize must reuse them. Where an annotation ends
    /// up is decided during line assembly, because that is where the base text is positioned.
    /// </para>
    /// </summary>
    /// <param name="content">Content the annotations are recorded on.</param>
    /// <param name="segments">The prepared segments.</param>
    /// <param name="from">First segment of the range that gained annotations.</param>
    /// <param name="to">One past the last segment of that range.</param>
    /// <exception cref="NotSupportedException">When an annotation asks for a distribution the engine lacks.</exception>
    private void MeasureRubies(PreparedContent content, List<TextSegment> segments, int from, int to)
    {
        var sources = new List<int>();

        for (int i = from; i < to; i++)
        {
            TextSegment segment = segments[i];

            if (segment.Source.Ruby is null || segment.Source.Font is null || segment.CharLength == 0)
                continue;

            if (sources.Count == 0 || sources[^1] != segment.SourceIndex)
                sources.Add(segment.SourceIndex);
        }

        foreach (int sourceIndex in sources)
        {
            var clusters = new List<int>();

            for (int i = from; i < to; i++)
            {
                if (segments[i].SourceIndex == sourceIndex && segments[i].CharLength > 0)
                    clusters.Add(i);
            }

            if (clusters.Count == 0)
                continue;

            TextSegment first = segments[clusters[0]];
            RubySpec spec = first.Source.Ruby!;

            ulong fontId = first.Source.ResolvedFontId;
            if (fontId == 0)
                fontId = FontCatalog.Shared.Resolve(first.Source.Font!);

            if (!FontCatalog.Shared.TryGetTypeface(fontId, out SKTypeface? typeface) || typeface is null)
                continue;

            float baseSize = first.Source.FontSize > 0 ? first.Source.FontSize : 16f;
            float rubySize = baseSize * (spec.SizeRatio > 0f ? spec.SizeRatio : 0.5f);
            ResolvedTypography annotationTypography = TypographyFor(first.LanguageTag);
            RubyPlacement placement = annotationTypography.RubyPlacement;
            var options = new ShapingOptions
            {
                Language = annotationTypography.OpenTypeLanguageTag,
                Direction = annotationTypography.Direction,

                // How the annotation is shaped follows how it is *set*, not how the document is written: Bopomofo
                // is a column of its own even in horizontal writing (clreq §5.5.3.1), and a vertical layout sets
                // every annotation down its column. Shaping a column along the line leaves each glyph with a
                // horizontal origin and no vertical one, and the renderer then draws every symbol above its own pen
                // - which is what put the whole column up and to the right of where the layout placed it.
                IsVertical = annotationTypography.RubyOrientation == RubyOrientation.Vertical
                             || annotationTypography.WritingMode != WritingMode.HorizontalTb,
            };

            MeasuredRuby annotation = ShapeAnnotation(spec, rubySize, baseSize, clusters, fontId, typeface, options);

            float expansion = annotation.Distribution == RubyDistribution.Jukugo
                ? WidenBaseForAnnotations(annotation, segments, baseSize)
                : 0f;

            // An annotation that sits beside its base character takes its room from that character's own advance
            // (clreq §5.5.3.2, note 2), so the base text carries the annotation through the rest of the pipeline:
            // the breaker fits the wider character, the element box includes it and the pen advances over it,
            // none of them knowing that an annotation is why.
            // In vertical writing the side an annotation sits on is the block axis (it goes to the right of the
            // column, clreq §5.5.3.1), so the room cannot come from the base's advance there: the breaker reserves it
            // on the block axis instead, the way it reserves the band above the line in horizontal writing.
            bool verticalLayout = annotationTypography.WritingMode != WritingMode.HorizontalTb;

            float sideReserve = placement == RubyPlacement.ReserveBeside && !verticalLayout
                ? WidenBaseBesideAnnotations(annotation, segments, baseSize)
                : 0f;

            annotation = new MeasuredRuby
            {
                Distribution = annotation.Distribution,
                FontSize = annotation.FontSize,
                FontId = annotation.FontId,
                GroupClusterStart = annotation.GroupClusterStart,
                GroupClusterCount = annotation.GroupClusterCount,
                Pieces = annotation.Pieces,
                Ascent = annotation.Ascent,
                Descent = annotation.Descent,
                InkExtent = annotation.InkExtent,
                Expansion = expansion,
                SideReserve = sideReserve,
            };

            content.Rubies[sourceIndex] = annotation;

            // Every position between the first and the last cluster of the run is inside the annotation's group:
            // a break there would separate the annotation from the base text it annotates.
            for (int c = clusters[0] + 1; c <= clusters[^1]; c++)
                content.InsideRubyGroup.Add(c);
        }
    }

    /// <summary>
    /// Widen the inter-character spacing of a jukugo run so neighbouring annotations do not collide, which is what
    /// jlreq §3.3.7 asks for (熟語ルビ): each annotation is centred on its own character, and a character whose
    /// annotation is wider than itself borrows room from its neighbours.
    /// <para>
    /// The room is added to the base segments' widths, so it is part of the layout the breaker sees. The cap per
    /// gap is an engine choice: the convention states the principle, not a number, so the limit is recorded here
    /// instead of being implied.
    /// </para>
    /// </summary>
    /// <param name="annotation">The measured jukugo annotation.</param>
    /// <param name="segments">Prepared segments, widened in place.</param>
    /// <param name="baseSize">Base font size in pixels.</param>
    /// <returns>The total room added, in pixels.</returns>
    private static float WidenBaseForAnnotations(
        MeasuredRuby annotation,
        List<TextSegment> segments,
        float baseSize)
    {
        const float maxPerGapEm = 0.5f;
        float cap = baseSize * maxPerGapEm;
        float total = 0f;
        IReadOnlyList<RubyPiece> pieces = annotation.Pieces;

        for (int p = 0; p + 1 < pieces.Count; p++)
        {
            RubyPiece left = pieces[p];
            RubyPiece right = pieces[p + 1];

            if (left.ClusterStart + left.ClusterCount != right.ClusterStart)
                continue;

            float required = (left.Width + right.Width) * 0.5f;
            float available = (segments[left.ClusterStart].Width + segments[right.ClusterStart].Width) * 0.5f;
            float extra = MathF.Min(MathF.Max(0f, required - available), cap);

            if (extra <= 0f)
                continue;

            TextSegment widened = segments[left.ClusterStart];
            widened.Width += extra;
            segments[left.ClusterStart] = widened;
            total += extra;
        }

        return total;
    }

    /// <summary>
    /// Give the characters an annotation sits beside the room that annotation needs, and report how much room
    /// that was per character.
    /// <para>
    /// clreq §5.5.3.2 states the room as half of the base character's size; it also says, for a horizontal
    /// paragraph, that every character carries it whether it is annotated or not, which is what keeps such a
    /// paragraph on a grid. This engine adds it only to the characters that are annotated: that is the smallest
    /// implementation of the stated purpose, and the difference is recorded here rather than left implied, so a
    /// later grid pass knows what it would have to add to the rest.
    /// </para>
    /// <para>
    /// The room goes to the last cluster a piece covers, so it lands on the outer side of the annotation rather
    /// than between it and the character before it. It is the same amount for every piece, which is what lets the
    /// assembler recover the advance an annotation was placed against by taking one reserve off the width the
    /// piece covers.
    /// </para>
    /// </summary>
    /// <param name="annotation">The measured annotation.</param>
    /// <param name="segments">Prepared segments, widened in place.</param>
    /// <param name="baseSize">Base font size in pixels.</param>
    /// <returns>The advance added per annotated cluster, in pixels.</returns>
    private static float WidenBaseBesideAnnotations(
        MeasuredRuby annotation,
        List<TextSegment> segments,
        float baseSize)
    {
        float reserve = baseSize * BesideReserveEm;

        foreach (RubyPiece piece in annotation.Pieces)
        {
            int last = piece.ClusterStart + piece.ClusterCount - 1;

            if (last < 0 || last >= segments.Count)
                continue;

            TextSegment widened = segments[last];
            widened.Width += reserve;
            segments[last] = widened;
        }

        return reserve;
    }

    /// <summary>
    /// Fraction of the base font size an annotation that sits beside its base character takes from that
    /// character's advance (clreq §5.5.3.2, note 2: half the size of the base character).
    /// </summary>
    private const float BesideReserveEm = 0.5f;

    /// <summary>
    /// Shape the annotation and cut it into the pieces its distribution asks for.
    /// <para>
    /// Mono gives every base cluster its own piece. A run whose annotation is shorter than the number of base
    /// characters cannot be cut that way without inventing characters, so it is measured as one group piece
    /// instead, and the recorded distribution says so.
    /// </para>
    /// </summary>
    /// <param name="spec">What the caller asked for.</param>
    /// <param name="rubySize">Annotation font size in pixels.</param>
    /// <param name="baseSize">Size of the text being annotated, in pixels.</param>
    /// <param name="clusters">Cluster indices of the base run.</param>
    /// <param name="fontId">Catalog id of the base element's font.</param>
    /// <param name="typeface">Typeface of that font.</param>
    /// <param name="options">Shaping options (language the annotation is shaped with).</param>
    /// <returns>The measured annotation.</returns>
    private MeasuredRuby ShapeAnnotation(
        RubySpec spec,
        float rubySize,
        float baseSize,
        List<int> clusters,
        ulong fontId,
        SKTypeface typeface,
        in ShapingOptions options)
    {
        var pieces = new List<RubyPiece>();
        string text = spec.Text;
        RubyDistribution distribution = spec.Distribution;

        // Jukugo is a per-character run like mono; what distinguishes it is that the *whole run* is one unit, so
        // it never breaks between its characters — which is what the distribution it was asked for records.
        if (distribution is RubyDistribution.Mono or RubyDistribution.Jukugo && text.Length < clusters.Count)
            distribution = RubyDistribution.Group;

        if (distribution == RubyDistribution.Group)
        {
            pieces.Add(ShapePiece(text, rubySize, baseSize, clusters[0], clusters.Count, fontId, typeface, options));
        }
        else
        {
            int count = clusters.Count;

            for (int p = 0; p < count; p++)
            {
                int start = p * text.Length / count;
                int end = (p + 1) * text.Length / count;

                if (end <= start)
                    end = start + 1;

                pieces.Add(ShapePiece(
                    text[start..end], rubySize, baseSize, clusters[p], 1, fontId, typeface, options));
            }
        }

        float ascent = 0f;
        float descent = 0f;
        float ink = 0f;

        foreach (RubyPiece piece in pieces)
        {
            if (piece.Ascent > ascent) ascent = piece.Ascent;
            if (piece.Descent > descent) descent = piece.Descent;
            if (piece.InkExtent > ink) ink = piece.InkExtent;
        }

        return new MeasuredRuby
        {
            Distribution = distribution,
            FontSize = rubySize,
            FontId = fontId,
            GroupClusterStart = clusters[0],
            GroupClusterCount = clusters.Count,
            Pieces = pieces,
            Ascent = ascent,
            Descent = descent,
            InkExtent = ink,
        };
    }

    /// <summary>Shape one annotation piece at the annotation font size.</summary>
    /// <param name="text">Annotation text of the piece.</param>
    /// <param name="rubySize">Annotation font size in pixels.</param>
    /// <param name="baseSize">Size of the text being annotated, in pixels.</param>
    /// <param name="clusterStart">First base cluster the piece annotates.</param>
    /// <param name="clusterCount">Number of base clusters the piece annotates.</param>
    /// <param name="fontId">Catalog id of the font it is shaped with.</param>
    /// <param name="typeface">Typeface of that font.</param>
    /// <param name="options">Shaping options.</param>
    /// <returns>The shaped piece.</returns>
    private RubyPiece ShapePiece(
        string text,
        float rubySize,
        float baseSize,
        int clusterStart,
        int clusterCount,
        ulong fontId,
        SKTypeface typeface,
        in ShapingOptions options)
    {
        HarfBuzzTextShaper shaper = GetOrCreateShaper(typeface, rubySize, options);
        Glyph[] glyphs = shaper.ShapeGlyphs(text, fontId, _fontManager);
        (float ascent, float descent, _) = shaper.GetMetrics();

        // A tone mark is not a symbol of the reading: the convention puts it against the corner of the last symbol
        // and gives it no cell of its own, which is a change to the advances and offsets the shaper just produced
        // (clreq §5.5.3.3). Doing it here is what lets the width and the ink below be measured from the glyphs that
        // will actually be drawn, instead of from a reading the renderer would then lay out differently.
        glyphs = BopomofoTone.Place(glyphs, text, rubySize, baseSize, options.IsVertical, ascent, descent);

        float width = 0f;

        foreach (Glyph glyph in glyphs)
            width += glyph.Advance;

        // What the band has to hold: the ink, not the line box. A tone mark rises above the annotation font's ascent
        // (clreq's Bopomofo tone marks, pinyin's diacritics), so a band measured with line metrics would cut it off.
        // Along a line the ink's height is what matters; down a column it is the ink's width that the band holds.
        (float inkLeft, float inkTop, float inkRight, float inkBottom) =
            GlyphInk.Measure(rubySize, glyphs, options.IsVertical);

        float inkExtent = MathF.Max(
            ascent + descent,
            options.IsVertical ? inkRight - inkLeft : inkBottom - inkTop);

        return new RubyPiece
        {
            Text = text,
            ClusterStart = clusterStart,
            ClusterCount = clusterCount,
            Glyphs = glyphs,
            Width = width,
            Ascent = ascent,
            Descent = descent,
            InkExtent = inkExtent,
        };
    }

    /// <summary>
    /// Measure all segments in the list via HarfBuzz (P3).
    /// </summary>
    private void MeasureSegments(List<TextSegment> segments)
    {
        MeasureSegmentsRange(segments, 0, segments.Count);
    }

    /// <summary>
    /// Measure segments in range [start, end) via HarfBuzz.
    /// </summary>
    private void MeasureSegmentsRange(List<TextSegment> segments, int start, int end)
    {
        for (int i = start; i < end; i++)
        {
            var seg = segments[i];
            MeasureSegment(ref seg);
            segments[i] = seg;
        }

        // Reserve layout space for background padding on the first and last segments
        // of each source DrawElement. This ensures line breaking accounts for the padding
        // so adjacent elements are correctly pushed away.
        ApplyBackgroundPadding(segments, start, end);
    }

    /// <summary>
    /// Add background padding width to the first (left) and last (right) segments
    /// of each source DrawElement that has BackgroundPadding set.
    /// Only the outer edges get padding — middle segments are unaffected.
    /// </summary>
    private static void ApplyBackgroundPadding(List<TextSegment> segments, int start, int end)
    {
        int i = start;
        while (i < end)
        {
            var seg = segments[i];
            if (seg.Source.BackgroundPadding.X <= 0)
            {
                i++;
                continue;
            }

            float padX = seg.Source.BackgroundPadding.X;
            int srcIdx = seg.SourceIndex;

            // Find the range [i, j) of segments sharing the same SourceIndex
            int j = i + 1;
            while (j < end && segments[j].SourceIndex == srcIdx)
                j++;

            // Add left padding to first segment
            var first = segments[i];
            first.Width += padX;
            first.MinWidth += padX;
            segments[i] = first;

            // Add right padding to last segment
            var last = segments[j - 1];
            last.Width += padX;
            last.MinWidth += padX;
            segments[j - 1] = last;

            i = j;
        }
    }

    /// <summary>
    /// Measure a single segment. Text segments are shaped with HarfBuzz;
    /// non-text segments get their width from the source DrawElement.
    /// </summary>
    private void MeasureSegment(ref TextSegment segment)
    {
        switch (segment.CharClass)
        {
            case CharacterClass.NonText:
                // Non-text: width comes from the source element's Size.
                // Height distribution depends on VerticalAlignment.
                segment.Width = segment.Source.Size.X;
                var height = segment.Source.Size.Y;
                switch (segment.Source.VerticalAlignment)
                {
                    case InlineVerticalAlignment.Middle:
                        // Center on text baseline: split height equally
                        segment.Ascent = height / 2f;
                        segment.Descent = height / 2f;
                        break;
                    case InlineVerticalAlignment.Bottom:
                        // Bottom of line: all descent
                        segment.Ascent = 0;
                        segment.Descent = height;
                        break;
                    case InlineVerticalAlignment.Baseline:
                    case InlineVerticalAlignment.Top:
                    default:
                        // Top-aligned or baseline: all ascent (existing behavior)
                        segment.Ascent = height;
                        segment.Descent = 0;
                        break;
                }
                break;

            case CharacterClass.Block:
                // Block: width/height comes from BlockInfo, already set by Segmenter.
                // No HarfBuzz measurement needed. Set metrics from block size.
                if (segment.Source.BlockInfo != null)
                {
                    segment.Width = segment.Source.BlockInfo.Size.X;
                    segment.Ascent = segment.Source.BlockInfo.Size.Y;
                    segment.Descent = 0;
                }
                break;

            case CharacterClass.BlockStart:
            case CharacterClass.BlockEnd:
                // Auto-size block markers: zero-width, no measurement needed.
                // Actual size is computed during the layout pass.
                break;

            case CharacterClass.Break:
                // Line breaks: zero width, no measurement needed
                break;

            case CharacterClass.Space or CharacterClass.Tab:
                MeasureTextSegment(ref segment);
                break;

            default:
                MeasureTextSegment(ref segment);
                break;
        }
    }

    /// <summary>
    /// Shape and measure a text segment using HarfBuzz.
    /// Handles font resolution (Godot Font → SKTypeface) and fallback.
    /// </summary>
    private void MeasureTextSegment(ref TextSegment segment)
    {
        var font = segment.Source.Font;
        var fontSize = segment.Source.FontSize > 0 ? segment.Source.FontSize : 16;

        if (font == null)
        {
            // No font specified — use zero metrics
            return;
        }

        // Prefer the id the producer resolved on the main thread. Resolving here touches a Godot
        // resource, which is only legitimate for direct/synchronous engine use on the main thread;
        // the server path always arrives with the id filled in.
        ulong fontId = segment.Source.ResolvedFontId;
        if (fontId == 0)
            fontId = FontCatalog.Shared.Resolve(font);

        if (!FontCatalog.Shared.TryGetTypeface(fontId, out var typeface) || typeface is null)
        {
            // Unresolved font: measure nothing instead of reading a Godot resource on this thread.
            return;
        }

        ResolvedTypography segmentTypography = TypographyFor(segment.LanguageTag);

        // A run that the renderer turns (Latin letters and digits in a vertical layout) is shaped horizontally: it is
        // drawn rotated, so the advances that carry it down the column are the widths the letters need, while a
        // top-to-bottom advance is the em box - which would space the letters out and push the word out of its column.
        bool shapesVertically = segmentTypography.WritingMode != WritingMode.HorizontalTb
            && segment.CharClass != CharacterClass.Latin;

        var shaper = GetOrCreateShaper(typeface, fontSize, new ShapingOptions
        {
            Language = segmentTypography.OpenTypeLanguageTag,
            Direction = DirectionOfSegment(segment),
            IsVertical = shapesVertically,
        });

        // One shaping pass produces both the advances the layout measures with and the glyph indices the
        // renderer will draw, so the two cannot drift apart. The fallback decision stays inside the shaper
        // (it splits the text into runs by glyph coverage).
        string shapedText = segment.DisplayText ?? segment.Text;
        Glyph[] shaped = shaper.ShapeGlyphs(shapedText, fontId, _fontManager);
        segment.Glyphs = shaped;
        segment.FontId = fontId;
        segment.GlyphAdvances = new float[shaped.Length];

        float width = 0f;
        for (int g = 0; g < shaped.Length; g++)
        {
            segment.GlyphAdvances[g] = shaped[g].Advance;
            width += shaped[g].Advance;
        }

        // The width is the text's own. A piece that could end a line carries the hyphen it would end it with, and
        // LineBreaker adds that hyphen's width to the fit test for exactly that piece; reserving it here as well
        // would leave a hyphen-wide gap inside every word the breaker did not break.
        segment.Width = width;

        // Compute MinWidth = Width - AdjustableSpace
        segment.MinWidth = segment.Width - segment.AdjustableSpace;
        if (segment.MinWidth < 0f) segment.MinWidth = 0f;

        // Get font metrics
        var metrics = shaper.GetMetrics();
        segment.Ascent = metrics.Ascent;
        segment.Descent = metrics.Descent;
    }

    /// <summary>
    /// Get or create a cached HarfBuzzTextShaper for the given typeface, size and shaping options.
    /// <para>
    /// The options are part of the key because a shaper carries them: a shaper created for one language would
    /// otherwise hand those forms to a segment that asked for another language.
    /// </para>
    /// </summary>
    /// <param name="typeface">Typeface to shape with.</param>
    /// <param name="fontSize">Font size in pixels.</param>
    /// <param name="options">Script, language and features the shaper honours.</param>
    /// <returns>A cached shaper.</returns>
    private HarfBuzzTextShaper GetOrCreateShaper(SKTypeface typeface, float fontSize, in ShapingOptions options)
    {
        var key = new ShaperKey(typeface, fontSize, options);
        if (_shaperCache.TryGetValue(key, out var cached))
            return cached;

        var shaper = new HarfBuzzTextShaper(typeface, fontSize, options);
        _shaperCache[key] = shaper;
        return shaper;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var shaper in _shaperCache.Values)
            shaper.Dispose();
        _shaperCache.Clear();

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Cache key for HarfBuzzTextShaper instances (typeface + font size + shaping options).
    /// </summary>
    private readonly struct ShaperKey : IEquatable<ShaperKey>
    {
        private readonly SKTypeface _typeface;
        private readonly float _fontSize;
        private readonly ShapingOptions _options;

        public ShaperKey(SKTypeface typeface, float fontSize, in ShapingOptions options)
        {
            _typeface = typeface;
            _fontSize = fontSize;
            _options = options;
        }

        /// <summary>
        /// Value equality of the cache key. The font size is compared exactly on purpose - a key has to
        /// match the size it was created with, and an epsilon would let two different sizes share one
        /// shaper. <c>float.Equals(float)</c> states that intent without a floating point comparison
        /// operator.
        /// </summary>
        /// <param name="other">The key to compare against.</param>
        /// <returns>True when both keys name the same typeface, font size and shaping options.</returns>
        public bool Equals(ShaperKey other) =>
            ReferenceEquals(_typeface, other._typeface)
            && _fontSize.Equals(other._fontSize)
            && _options.Equals(other._options);

        public override bool Equals(object? obj) => obj is ShaperKey other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(
            _typeface != null ? _typeface.GetHashCode() : 0, _fontSize, _options);
    }
}
