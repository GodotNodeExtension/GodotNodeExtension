using System.Collections.Generic;
using Godot;
using GodotNodeExtension.Component.Typography.Core.Model;
using GodotNodeExtension.Component.Typography.Languages;
using GodotNodeExtension.Component.Typography.Server;

namespace GodotNodeExtension.Component.Typography.Core;

/// <summary>
/// Greedy line-breaking algorithm implementing step L1 of the typography pipeline.
/// Breaks prepared segments into lines while respecting:
///   - CJK line-start/end prohibition rules (CLREQ §6.1)
///   - Unbreakable symbol pair rules
///   - Paragraph boundaries with first-line indent and spacing
///   - WrapRegion exclusion zones via <see cref="WrapRegionManager"/>
/// </summary>
public class LineBreaker
{
    private readonly ResolvedTypography _typography;
    private readonly ResolvedTypography[]? _paragraphTypography;

    /// <summary>
    /// Create a LineBreaker for one layout, reading the effective typography parameters it was given.
    /// </summary>
    /// <param name="typography">
    /// Effective typography parameters for this layout, already merged with the language profile.
    /// </param>
    /// <param name="paragraphTypography">
    /// Parameters per paragraph when a paragraph named its own language; null keeps every paragraph on
    /// <paramref name="typography"/>. Geometry is always taken from <paramref name="typography"/>: a paragraph
    /// does not own the box it sits in, it only owns the language-governed defaults.
    /// </param>
    public LineBreaker(
        ResolvedTypography typography,
        ResolvedTypography[]? paragraphTypography = null)
    {
        _typography = typography;
        _paragraphTypography = paragraphTypography;
    }

    /// <summary>
    /// Break candidates rejected by a prohibition or unbreakable-pair rule during the last
    /// <see cref="BreakLines"/> call. It is reported through <see cref="LayoutResult.ProhibitedBreakSkips"/>
    /// because it is the cheapest explanation for a line that ends earlier than its width allows.
    /// </summary>
    public int ProhibitedBreakSkips { get; private set; }

    /// <summary>
    /// Lines that had to be broken at an invalid position because the line contained no legal break
    /// point. Non-zero is a layout-quality signal: either one unavoidable break, or a box too narrow.
    /// </summary>
    public int ForcedBreaks { get; private set; }

    /// <summary>
    /// Break prepared content into lines.
    /// When <see cref="TypographySettings.WrapRegions"/> is non-empty, per-line available
    /// width is determined by <see cref="WrapRegionManager"/> instead of fixed maxWidth.
    /// </summary>
    /// <param name="prepared">Measured content to break into lines.</param>
    /// <param name="boundaries">
    /// Boundaries between the prepared clusters, from the compile phase. The break search reads its
    /// decisions from them instead of re-deriving prohibition and unbreakable-pair rules, which is what
    /// keeps the rules in one place (the language profile) rather than in two stages.
    /// </param>
    /// <param name="contentSize">Output: total content bounding box.</param>
    /// <returns>List of laid-out lines.</returns>
    public List<LayoutLine> BreakLines(
        PreparedContent prepared,
        IReadOnlyList<Boundary> boundaries,
        out Vector2 contentSize)
    {
        ProhibitedBreakSkips = 0;
        ForcedBreaks = 0;

        var lines = new List<LayoutLine>();
        var segments = prepared.Segments;

        if (segments.Count == 0)
        {
            contentSize = Vector2.Zero;
            return lines;
        }

        var state = new BreakState(_typography, _paragraphTypography, prepared);

        for (int i = 0; i < segments.Count; i++)
        {
            var seg = segments[i];

            // Handle paragraph break
            if (seg.CharClass == CharacterClass.Break)
            {
                // Only finalize when there is content on the current line.
                // After block elements, the line is already finalized and empty;
                // calling FinalizeLine on an empty line would add an unwanted
                // LineSpacing gap before the ParagraphSpacing.
                if (!state.CurrentLineEmpty)
                    state.FinalizeLine(lines);
                state.EndParagraph();
                continue;
            }

            // Handle block-level element
            if (seg.CharClass == CharacterClass.Block)
            {
                // Finalize any current line before placing the block
                if (!state.CurrentLineEmpty)
                {
                    state.FinalizeLine(lines);
                }

                // Place block as a single-element line
                state.AddSegmentToLine(i, seg);
                state.FinalizeLine(lines);
                continue;
            }

            // Handle auto-size block start
            if (seg.CharClass == CharacterClass.BlockStart)
            {
                // Finalize any current line before placing the block
                if (!state.CurrentLineEmpty)
                {
                    state.FinalizeLine(lines);
                }

                var blockInfo = seg.Source.BlockInfo!;

                // Collect inner segments until matching BlockEnd
                var innerSegments = new List<TextSegment>();
                int depth = 1;
                for (i++; i < segments.Count && depth > 0; i++)
                {
                    if (segments[i].CharClass == CharacterClass.BlockStart)
                        depth++;
                    else if (segments[i].CharClass == CharacterClass.BlockEnd)
                    {
                        depth--;
                        if (depth == 0)
                            break;
                    }
                    innerSegments.Add(segments[i]);
                }

                // Compute sub-layout dimensions
                float availableWidth = state.LineRight - state.LineLeft;
                float subWidth = availableWidth - blockInfo.LeftIndent - blockInfo.RightIndent
                                 - blockInfo.Padding.X * 2;
                if (subWidth < 0) subWidth = 0;

                // Run nested layout on inner segments
                var subPrepared = new PreparedContent(innerSegments, [], 0)
                {
                    // A nested block is laid out in the same orientation as the document around it; only its
                    // measure changes, which is what the nested width is for.
                    Axes = new LayoutAxes(_typography.WritingMode, subWidth),
                };
                subPrepared.BuildParagraphs();

                // Derive the nested parameters from the parent's: the width changes, the language
                // behaviour does not. Deriving (instead of copying fields by hand) is what keeps a new
                // parameter from being silently dropped, which already happened once with Padding.
                var subTypography = _typography.ForNestedLayout(
                    subWidth, blockInfo.WrapRegions ?? []);

                var subBreaker = new LineBreaker(subTypography);
                // The nested content is a separate segment list, so it needs its own boundaries.
                var subLines = subBreaker.BreakLines(
                    subPrepared,
                    BoundaryBuilder.Build(subPrepared, subTypography),
                    out var subContentSize);

                // Compute block size
                float blockWidth = blockInfo.FullWidth
                    ? availableWidth
                    : subContentSize.X + blockInfo.LeftIndent + blockInfo.RightIndent + blockInfo.Padding.X * 2;
                float blockHeight = subContentSize.Y + blockInfo.Padding.Y * 2;

                // Create block LayoutLine
                var blockLine = new LayoutLine
                {
                    Y = state.CurrentY,
                    Height = blockHeight,
                    Ascent = blockHeight,
                    Descent = 0,
                    ParagraphIndex = state.CurrentParagraphIndex,
                    IsFirstLineOfParagraph = false,
                    // Carry the span too: line adjustment works on this line as well, and giving it a
                    // zero span would make alignment disagree with the span the block was placed in.
                    LineLeft = state.LineLeft,
                    LineRight = state.LineRight,
                };

                // Assembled through the axes like the text path above: the span edge is the inline coordinate, the
                // block's position in the flow is the block coordinate. Horizontal writing maps these onto X and Y
                // unchanged, which is why no golden moves.
                LayoutAxes axes = new(_typography.WritingMode, _typography.MaxWidth);

                var blockElement = LayoutElement.FromSegment(
                    seg,
                    axes.Point(state.LineLeft, state.CurrentY),
                    state.CurrentY + blockHeight,
                    axes.Size(blockWidth, blockHeight),
                    i,
                    lines.Count
                );
                blockElement.SubLines = subLines;
                blockElement.BlockInfo = blockInfo;
                blockLine.Elements.Add(blockElement);
                lines.Add(blockLine);

                // Advance state past the block
                state.AdvancePastBlock(blockHeight);
                continue;
            }

            // BlockEnd segments outside of auto-size collection are ignored
            if (seg.CharClass == CharacterClass.BlockEnd)
                continue;

            // Skip trailing spaces at line start (except first-line indent placeholders)
            if (seg.CharClass is CharacterClass.Space or CharacterClass.Tab && state.CurrentLineEmpty)
                continue;

            // If available width is zero or negative (fully obstructed), try to advance past
            if (state.LineRight <= state.LineLeft && state.CurrentLineEmpty)
            {
                if (state.TryAdvancePastObstruction())
                {
                    i--; // Retry this segment at new Y
                    continue;
                }
                // No obstruction to clear — force the segment onto the line anyway
            }

            // A piece that could end a line would end it with a hyphen, and the line has to fit that hyphen too -
            // but only the piece that ends the line pays for it. Adding it to the fit test rather than to the
            // measured width is what keeps a word the breaker did not break free of a hyphen-wide gap.
            float effectiveWidth = seg.Width
                + (seg.HyphenRun is { } pendingHyphen ? pendingHyphen.Width : 0f);

            // A mark its convention may hang does not have to fit: it stays at the end of this line and sticks out
            // past the edge, and what follows it starts the next line. Measuring it as zero wide here is what makes
            // the line still end after it — the segment is added exactly once, by the ordinary path below.
            bool mayHang = state.MayHang(seg);
            float fitWidth = mayHang ? 0f : effectiveWidth;

            // An annotation wider than the run it annotates may not be drawn outside the area when that run sits at
            // the line's start or end (jlreq §3.3.9), so the line pays for the overhang: it begins after it, and it
            // ends early enough to leave room for the annotation of the run ending there.
            fitWidth += state.LeadingRubyOverhang(i, seg) + state.TrailingRubyOverhang(i, seg);

            // Check if segment fits on current line
            if (state.CurrentX + fitWidth > state.LineRight)
            {


                // Whether this mark hangs is decided by the convention; recording it where the fit check
                // sees the overhang keeps the reason with the mark that caused it. A mark that exactly fills
                // the line hangs without the marker (same behaviour, quieter dump); see the plan notes.
                if (mayHang)
                    state.MarkHanging(i);

                // The line may offer another interval (text flowing around an exclusion that covers part of
                // it). Moving the pen there keeps the line full; breaking would leave it half empty.
                if (state.TryAdvanceToNextSpan())
                {
                    i--; // Re-process this segment in the new interval
                    continue;
                }

                // Overflow: try to find a valid break point
                int breakIndex = FindBreakPoint(segments, boundaries, state.LineStartSegIndex, i);

                if (breakIndex >= state.LineStartSegIndex)
                {
                    // Anything the line already accepted past the break point belongs to the next line.
                    state.DropSegmentsAfter(breakIndex);
                    state.FinalizeLine(lines);

                    // Re-process remaining segments from break point + 1
                    i = breakIndex; // loop will i++ to breakIndex + 1
                    continue;
                }
                else
                {
                    // No valid break point — force break at current position
                    if (!state.CurrentLineEmpty)
                    {
                        ForcedBreaks++;
                        state.FinalizeLine(lines);
                        i--; // Re-process current segment on new line
                        continue;
                    }
                    // Line is empty and single segment overflows: put it on this line anyway
                }
            }

            state.AddSegmentToLine(i, seg);
            state.NextSegToAdd = i + 1;
        }

        // Finalize the last line if it has content
        if (!state.CurrentLineEmpty)
        {
            state.FinalizeLine(lines);
        }

        contentSize = state.ComputeContentSize(lines);
        return lines;
    }

    /// <summary>
    /// Find the best break point within the range [lineStart, currentIndex).
    /// Walks backwards from currentIndex to find the last valid break-after position
    /// while respecting prohibition rules.
    /// </summary>
    /// <summary>
    /// Find the best break point within the range <c>[lineStart, currentIndex)</c>: the last boundary the
    /// line may end at.
    /// <para>
    /// Every rule that can refuse a break — line-end prohibition, line-start prohibition and the
    /// unbreakable pairs (——, ……, a digit with its affix) — arrives here already decided, on the
    /// boundary. The search no longer asks the classifier anything, so adding or changing a rule is a
    /// change to the compile phase only.
    /// </para>
    /// </summary>
    /// <param name="segments">All segments of the paragraph.</param>
    /// <param name="boundaries">Boundaries between them, indexed by the left cluster.</param>
    /// <param name="lineStart">Segment index the line starts at.</param>
    /// <param name="currentIndex">Index of the segment that did not fit.</param>
    /// <returns>The segment index the line may end at, or <c>lineStart - 1</c> when there is none.</returns>
    private int FindBreakPoint(
        List<TextSegment> segments,
        IReadOnlyList<Boundary> boundaries,
        int lineStart,
        int currentIndex)
    {
        // Walk backwards from the current position.
        for (int i = currentIndex - 1; i >= lineStart; i--)
        {
            // A segment that cannot end a line at all is not a rejected candidate, it is simply not a
            // candidate; only the rules below count as skips.
            if (!segments[i].CanBreakAfter)
                continue;

            if (i >= boundaries.Count)
                continue;

            var boundary = boundaries[i];
            if (boundary.ForbiddenToBreak || boundary.ForbiddenAtLineEnd || boundary.ForbiddenAtLineStart)
            {
                ProhibitedBreakSkips++;
                continue;
            }

            return i;
        }

        return lineStart - 1; // No valid break point found
    }

    /// <summary>
    /// Internal state tracker for the line-breaking algorithm.
    /// Supports both fixed-width and wrap-region-aware layout.
    /// </summary>
    private struct BreakState
    {
        private readonly ResolvedTypography _typography;
        private readonly ResolvedTypography[]? _paragraphTypography;
        private readonly PreparedContent _prepared;
        private readonly WrapRegionManager? _wrapMgr;

        /// <summary>Font size assumed when the content does not specify one (matches the prepare phase).</summary>
        private const float DefaultFontSize = 16f;

        public float CurrentX;
        public float CurrentY;
        public float LineRight;
        public float LineLeft;
        private float _maxAscent;
        private float _maxDescent;
        private float _maxExtraAbove;
        private float _maxExtraBelow;

        /// <summary>How many lines have been finished, which is the line index a float's lifecycle is measured in.</summary>
        private int _lineCount;

        /// <summary>Cluster index of the mark that hangs past this line's end edge, or -1 for none.</summary>
        private int _hangingClusterIndex = -1;

        /// <summary>
        /// Notice that a mark will hang past this line's end, so the element it becomes can say why it does.
        /// </summary>
        /// <param name="clusterIndex">Cluster index of the hanging mark.</param>
        public void MarkHanging(int clusterIndex) => _hangingClusterIndex = clusterIndex;
        public int CurrentParagraphIndex;
        private bool _isFirstLineOfParagraph;
        public int LineStartSegIndex;
        public int NextSegToAdd;

        /// <summary>
        /// Intervals the current line offers, left to right. One for an ordinary line; more when text flows
        /// around an exclusion that covers only part of the line.
        /// </summary>
        private readonly List<LineSpan> _lineSpans = [];

        /// <summary>Interval the pen currently writes into.</summary>
        private int _spanIndex;

        /// <summary>
        /// A segment placed on the current line, with its index in the document's segment list.
        /// The index is carried explicitly because a line holds a <em>subsequence</em> of the segments
        /// (leading spaces are dropped, blocks consume runs of them), so a position within the line is
        /// not an index within the document. Assuming otherwise is what made cluster indices rewind.
        /// </summary>
        private readonly record struct LineEntry(int Index, TextSegment Segment, int SpanIndex);

        private readonly List<LineEntry> _currentLineSegments;

        public bool CurrentLineEmpty => _currentLineSegments.Count == 0;

        public BreakState(
            ResolvedTypography typography,
            ResolvedTypography[]? paragraphTypography,
            PreparedContent prepared)
        {
            _paragraphTypography = paragraphTypography;

            _typography = typography;
            _prepared = prepared;
            _currentLineSegments = [];

            float padding = typography.Padding;
            CurrentX = padding;
            CurrentY = padding;
            LineLeft = padding;
            LineRight = typography.InlineLimit + padding;
            _maxAscent = 0;
            _maxDescent = 0;
            CurrentParagraphIndex = 0;
            _isFirstLineOfParagraph = true;
            LineStartSegIndex = 0;
            NextSegToAdd = 0;
            _lineSpans.Clear();
            _spanIndex = 0;

            // Initialize WrapRegionManager if there are wrap regions
            if (typography.WrapRegions.Count > 0)
            {
                _wrapMgr = new WrapRegionManager(typography.InlineLimit);
                foreach (var region in typography.WrapRegions)
                {
                    _wrapMgr.AddRegion(region);
                }
            }
            else
            {
                _wrapMgr = null;
            }

            ApplyParagraphIndents();
            ApplyFirstLineIndent();
        }

        /// <summary>
        /// Where the annotation of a cluster goes, or null when the cluster carries none.
        /// <para>
        /// Two placements, and the annotation's own orientation decides what its measured width means in each.
        /// Set along the line, the annotation is centred over the base text it annotates — over its own cluster
        /// for a mono ruby, over the whole run for a group ruby — and its box sits directly above the base box,
        /// which is the room the line reserves for it (see <see cref="BreakState.LineExtrasOf"/>). Set down
        /// a column, the annotation sits beside its base character along the character's own extent
        /// (clreq §5.5.3.2: vertically centre-aligned to the base character in vertical Bopomofo, as opposed to
        /// centre-aligned over it in horizontal Bopomofo) and its width is the column's height.
        /// </para>
        /// <para>
        /// The two numbers the annotation is built from are inline and block coordinates; <paramref name="axes"/>
        /// turns them into a point in content space, so a vertical writing mode puts the same "above the box"
        /// reserve on the block-start side of the column - the right-hand side in
        /// <see cref="WritingMode.VerticalRl"/> - without this method asking which mode it is in. In horizontal
        /// writing the mapping is the identity, which is why the geometry of every horizontal dump is unchanged.
        /// </para>
        /// </summary>
        /// <param name="clusterIndex">Index of the cluster in the document.</param>
        /// <param name="segment">The segment that cluster came from.</param>
        /// <param name="x">Left edge of the element on the line (inline coordinate).</param>
        /// <param name="width">Width of the element (inline extent).</param>
        /// <param name="boxTop">Top of the element's text box (block coordinate).</param>
        /// <param name="height">Height of the element's text box (block extent).</param>
        /// <param name="axes">Axes of the layout, which turn the pair into content-space geometry.</param>
        /// <returns>The annotation geometry, or null.</returns>
        /// <param name="band">Room the line reserved on the block-start side of its text box, for annotations.</param>
        private RubyAnnotation? BuildRuby(int clusterIndex, in TextSegment segment, float x, float width,
            float boxTop, float height, float band, LayoutAxes axes)
        {
            if (!_prepared.Rubies.TryGetValue(segment.SourceIndex, out MeasuredRuby? ruby))
                return null;

            foreach (RubyPiece piece in ruby.Pieces)
            {
                if (piece.ClusterStart != clusterIndex)
                    continue;

                float coveredWidth = width;

                if (piece.ClusterCount > 1)
                {
                    coveredWidth = 0f;

                    foreach (LineEntry entry in _currentLineSegments)
                    {
                        if (entry.Index >= piece.ClusterStart
                            && entry.Index < piece.ClusterStart + piece.ClusterCount)
                        {
                            coveredWidth += entry.Segment.Width;
                        }
                    }
                }

                RubyOrientation orientation = TypographyFor(CurrentParagraphIndex).RubyOrientation;
                float inlinePos;
                float blockPos;
                float bandWidth;

                if (axes.IsVertical)
                {
                    // Vertical writing: the annotation runs along the column, so it is centred on the base character
                    // along the inline axis (clreq §5.5.3.2) and sits in the band the line reserved on the block axis
                    // - the column's right-hand side for right-to-left columns. The band is what the line set aside
                    // for annotations, and the column's centre line goes in the middle of it: the column's ink is
                    // narrower than the band, so anchoring it at either edge both reached onto the text and left the
                    // rest of the band empty.
                    inlinePos = x + ((coveredWidth - piece.Width) * 0.5f);
                    blockPos = boxTop - (band * 0.5f);
                    bandWidth = band;
                }
                else if (orientation == RubyOrientation.Vertical)
                {
                    // Beside the character, not over it: the annotation occupies the room the compile phase added to
                    // the base advance (`SideReserve`, clreq §5.5.3.2's half an em), and it sits in the middle of
                    // that room rather than on its edge - the column's own ink is narrower than the room, so
                    // anchoring it at the edge both covered the base character and left the rest of the band empty.
                    // This is CSS Ruby's initial `ruby-align: space-around`.
                    inlinePos = x + coveredWidth - (ruby.SideReserve * 0.5f);
                    // The column's own ink runs downwards from the pen, so centring it on the character is the box'
                    // middle minus half the column: adding the annotation's ascent here (which the older
                    // pen-as-baseline convention needed) drops the column onto the character's bottom edge.
                    blockPos = boxTop + ((height - piece.Width) * 0.5f);
                    bandWidth = ruby.SideReserve;
                }
                else
                {
                    inlinePos = x + ((coveredWidth - piece.Width) * 0.5f);
                    blockPos = boxTop - piece.Descent;
                    bandWidth = piece.Ascent + piece.Descent;
                }

                Vector2 point = axes.Point(inlinePos, blockPos);

                return new RubyAnnotation
                {
                    Text = piece.Text,
                    Glyphs = piece.Glyphs,
                    FontId = ruby.FontId,
                    FontSize = ruby.FontSize,
                    Orientation = orientation,
                    X = point.X,
                    BaselineY = point.Y,
                    Width = piece.Width,
                    BandWidth = bandWidth,
                    Reason = ruby.Distribution switch
                    {
                        RubyDistribution.Mono => "Ruby:Mono",
                        RubyDistribution.Jukugo => "Ruby:Jukugo",
                        _ => "Ruby:Group",
                    },
                };
            }

            return null;
        }

        /// <summary>
        /// Where the emphasis mark of a cluster goes, or null when it carries none.
        /// <para>
        /// The mark is centred on its character and sits in the line gap on the side the language puts it — under
        /// the characters in horizontal Chinese, over them in horizontal Japanese (clreq §5.3.1, jlreq §3.3.9).
        /// It does not change the line's height: it is an interlinear object, so a tight line spacing lets it fall
        /// into the gap, which is what the conventions describe.
        /// </para>
        /// <para>
        /// The side is a block-axis position, so a vertical writing mode turns it into a side of the column: both
        /// clreq §5.3.1 ("in vertical writing emphasis marks are mostly placed on the right") and jlreq §3.3.9
        /// ("in vertical setting, on the right of the characters") place them there, which is the block-start side
        /// in <see cref="WritingMode.VerticalRl"/>. That is what <see cref="EmphasisSide.Right"/> names, and why
        /// the side reported for a vertical layout is not the profile's horizontal one.
        /// </para>
        /// </summary>
        /// <param name="segment">Segment the mark belongs to.</param>
        /// <param name="x">Left edge of the element on the line (inline coordinate).</param>
        /// <param name="width">Width of the element (inline extent).</param>
        /// <param name="boxTop">Top of the element's text box (block coordinate).</param>
        /// <param name="height">Height of the element's text box (block extent).</param>
        /// <param name="typography">Effective typography of the paragraph.</param>
        /// <param name="axes">Axes of the layout, which turn the pair into content-space geometry.</param>
        /// <returns>The mark geometry, or null.</returns>
        private static EmphasisMarkGeometry? BuildEmphasis(
            in TextSegment segment,
            float x,
            float width,
            float boxTop,
            float height,
            ResolvedTypography typography,
            LayoutAxes axes)
        {
            EmphasisMarkStyle style = segment.Source.EmphasisMark;

            if (style == EmphasisMarkStyle.None || segment.CharLength == 0)
                return null;

            EmphasisSide side = axes.IsVertical ? EmphasisSide.Right : typography.EmphasisSide;

            // The mark itself is a convention: a filled circle in Chinese, a bullet in horizontal Japanese, and a
            // sesame dot for Japanese, whose vertical form jlreq §3.3.9 gives as ﹅ (the element asks for it by
            // style, so the choice survives whatever the language would do by default).
            string mark = style == EmphasisMarkStyle.SesameDot
                ? "﹅"
                : side == EmphasisSide.Above ? "•" : "●";

            float fontSize = segment.Source.FontSize > 0 ? segment.Source.FontSize : DefaultFontSize;
            float size = fontSize * typography.EmphasisMarkSizeEm;

            // Above (and, in vertical writing, to the right of the column) is the block-start side; below is the
            // block-end side. Both are outside the element's own box, which is what "in the line gap" means.
            float blockCenter = side is EmphasisSide.Above or EmphasisSide.Right
                ? boxTop - size
                : boxTop + height + size;

            Vector2 point = axes.Point(x + (width * 0.5f), blockCenter);

            return new EmphasisMarkGeometry
            {
                Mark = mark,
                Size = size,
                X = point.X,
                CenterY = point.Y,
                Side = side,
                Reason = $"Emphasis:{side}",
            };
        }

        /// <summary>
        /// Whether a mark may hang past the end edge of the line under construction.
        /// <para>
        /// Three things have to agree: the mark must be one that may not start a line in the first place (other
        /// characters are ordinary text with nothing to hang from), the language must allow hanging, and the
        /// Japanese convention — no hanging in mixed text — must hold, which is why a line that already carries
        /// Latin letters refuses the mark.
        /// </para>
        /// </summary>
        /// <param name="segment">The segment that did not fit.</param>
        /// <returns>True when it may stay on this line past the edge.</returns>
        public bool MayHang(in TextSegment segment)
        {
            HangingPunctuationPolicy policy = TypographyFor(CurrentParagraphIndex).HangingPunctuation;

            if (policy is HangingPunctuationPolicy.None or HangingPunctuationPolicy.VerticalOnly)
                return false;

            // A line with more than one interval flows around an exclusion, and the end of its first interval is
            // the exclusion's edge rather than the margin: a mark that hung there would be printed on the obstacle.
            if (_lineSpans.Count > 1)
                return false;

            bool hangable = segment.CharClass
                is CharacterClass.PunctuationPauseStop or CharacterClass.PunctuationClose
                or CharacterClass.PunctuationEllipsis;

            if (!hangable)
                return false;

            if (policy == HangingPunctuationPolicy.NotInMixedText)
            {
                foreach (LineEntry entry in _currentLineSegments)
                {
                    if (entry.Segment.CharClass == CharacterClass.Latin)
                        return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Place a segment on the current line.
        /// </summary>
        /// <param name="index">Index of the segment in the document's segment list.</param>
        /// <param name="segment">The segment to place.</param>
        public void AddSegmentToLine(int index, in TextSegment segment)
        {
            // Opening the line: give way for this run's annotation first, so the annotation starts where the line
            // does instead of being drawn past its edge. The positioning pass derives the same value from the line's
            // first segment, which is what keeps packing and geometry in step.
            CurrentX += LeadingRubyOverhang(index, segment);

            _currentLineSegments.Add(new LineEntry(index, segment, _spanIndex));
            CurrentX += segment.Width;

            if (segment.Ascent > _maxAscent)
                _maxAscent = segment.Ascent;
            if (segment.Descent > _maxDescent)
                _maxDescent = segment.Descent;

            (float above, float below) = LineExtrasOf(index, segment);

            if (above > _maxExtraAbove)
                _maxExtraAbove = above;
            if (below > _maxExtraBelow)
                _maxExtraBelow = below;
        }

        /// <summary>
        /// Room this segment's content needs outside the line's text box, per side.
        /// <para>
        /// Two things ask for room on this side of the text, and both are the engine's job to provide rather than
        /// the author's: an annotation band (jlreq puts ruby between the lines, clreq asks for a line gap of one and
        /// a half times the base size for a right-hand annotation in vertical writing, and neither convention makes
        /// the layout work out on its own), and the part of a Beside annotation's column that is taller than the
        /// character it annotates - clreq asks annotations not to exceed their base, which is what keeps that at
        /// zero for a reading that fits, but a caller may ask for a larger annotation and the layout follows.
        /// </para>
        /// </summary>
        /// <param name="clusterIndex">Index of the segment in the document's segment list.</param>
        /// <param name="segment">Segment that may carry an annotation.</param>
        /// <returns>Room to add on the block-start side, and on the block-end side.</returns>
        private (float Above, float Below) LineExtrasOf(int clusterIndex, in TextSegment segment)
        {
            float above = 0f;
            float below = 0f;

            // The band belongs to the run's *first* cluster, which is the segment this cluster index names. Asking
            // "is this the element just added" instead worked only for a run that happened to open the line: a run
            // further along, or one further down a column, reserved nothing, so its annotation had no room and
            // landed on the line above it.
            if (_prepared.Rubies.TryGetValue(segment.SourceIndex, out MeasuredRuby? ruby)
                && ruby.GroupClusterStart == clusterIndex)
            {
                // The band holds the annotation's ink, its line metrics being the floor (an annotation is still a
                // text run): a tone mark rises above the font's ascent, so a band measured with line metrics alone
                // hides part of the mark it exists for.
                float band = System.MathF.Max(ruby.InkExtent, ruby.Ascent + ruby.Descent);

                bool beside = TypographyFor(CurrentParagraphIndex).RubyPlacement == RubyPlacement.ReserveBeside;
                bool vertical = _typography.WritingMode != WritingMode.HorizontalTb;

                if (beside && !vertical)
                {
                    // The room the annotation itself occupies is already part of the base character's advance; what
                    // the line has to find is the part of the column that reaches past the text box. The column is
                    // centred on its base character, so the overhang is split evenly between the two sides.
                    float overhang = System.MathF.Max(0f, ColumnHeightOf(ruby) - segment.Ascent - segment.Descent) * 0.5f;
                    above += overhang;
                    below += overhang;
                }
                else
                {
                    // Everywhere else the band sits outside the text box: above it in horizontal writing, on the
                    // block-start side of the column in vertical writing.
                    above += band;
                }
            }

            // An underline is drawn at the font's own position, which for many faces reaches below the descender
            // the line was measured with.
            if (segment.Source.IsUnderline)
                below = System.MathF.Max(below, UnderlineOverhang(segment.Source, segment.Ascent));

            // An emphasis mark sits outside the element's own box - that is what "in the line gap" means - so its
            // 1.5 marks of room (its centre is one mark outside the box and it is half a mark wide itself) belongs to
            // the line, or the mark lands in the line above/below it.
            if (segment.Source.EmphasisMark != EmphasisMarkStyle.None)
            {
                ResolvedTypography typography = TypographyFor(CurrentParagraphIndex);
                float mark = (segment.Source.FontSize > 0 ? segment.Source.FontSize : DefaultFontSize)
                             * typography.EmphasisMarkSizeEm;

                EmphasisSide side = _typography.WritingMode != WritingMode.HorizontalTb
                    ? EmphasisSide.Right
                    : typography.EmphasisSide;

                if (side is EmphasisSide.Above or EmphasisSide.Right)
                    above = System.MathF.Max(above, mark * 1.5f);
                else
                    below = System.MathF.Max(below, mark * 1.5f);
            }

            return (above, below);
        }

        /// <summary>
        /// How far the annotation of the run starting at this cluster reaches beyond that run on one side, or 0 when
        /// it is no wider than the run and when it reaches no further on that side.
        /// <para>
        /// The annotation is centred on the run it annotates, so an annotation wider than the run overhangs it by
        /// half the difference on each side. That overhang may not be drawn outside the page area when it sits at the
        /// line's start or end (jlreq §3.3.9), which is why the fit test pays for it: the line begins after the
        /// overhang, and it ends early enough to leave room for the annotation of whatever run ends there.
        /// </para>
        /// </summary>
        /// <param name="clusterIndex">Index of the first cluster of the run.</param>
        /// <param name="segment">The segment that starts (or ends) that run.</param>
        /// <returns>The overhang in pixels.</returns>
        private float RubyOverhangBeyondRun(int clusterIndex, in TextSegment segment)
        {
            if (!_prepared.Rubies.TryGetValue(segment.SourceIndex, out MeasuredRuby? ruby))
                return 0f;

            foreach (RubyPiece piece in ruby.Pieces)
            {
                if (clusterIndex < piece.ClusterStart
                    || clusterIndex >= piece.ClusterStart + piece.ClusterCount)
                    continue;

                float covered = 0f;

                for (int c = piece.ClusterStart; c < piece.ClusterStart + piece.ClusterCount; c++)
                {
                    if (c >= 0 && c < _prepared.Segments.Count)
                        covered += _prepared.Segments[c].Width;
                }

                return System.MathF.Max(0f, (piece.Width - covered) * 0.5f);
            }

            return 0f;
        }

        /// <summary>
        /// Room the line has to give way for at its start, for the segment that would open it: the annotation's
        /// overhang beyond what the line's own start already leaves free.
        /// <para>
        /// The room before a line's content is not wasted - padding, a first-line indent and the intervals an
        /// exclusion leaves all sit there - so an annotation only asks the line to move when its overhang reaches
        /// past that room. A Japanese first line indented by a character, or a canvas with padding, therefore keeps
        /// its position while the annotation still stays inside the area.
        /// </para>
        /// </summary>
        /// <param name="clusterIndex">Index of the segment in the document's segment list.</param>
        /// <param name="segment">The segment that would open the line.</param>
        /// <returns>The room to give way for, in pixels, or 0 when this segment does not open the line.</returns>
        public float LeadingRubyOverhang(int clusterIndex, in TextSegment segment) =>
            _currentLineSegments.Count == 0 ? HeadRubyOverhangOf(clusterIndex, segment) : 0f;

        /// <summary>
        /// Shortfall of an annotation that starts a line, once the room before that line's content is counted.
        /// </summary>
        /// <param name="clusterIndex">Index of the run's first segment in the document's segment list.</param>
        /// <param name="segment">That segment.</param>
        /// <returns>The room the line has to give way for, in pixels.</returns>
        private float HeadRubyOverhangOf(int clusterIndex, in TextSegment segment) =>
            System.MathF.Max(0f, RubyOverhangBeyondRun(clusterIndex, segment) - LineLeft);

        /// <summary>
        /// Room the line has to leave at its end for the annotation of the run that ends with this segment - which is
        /// the run's <em>last</em> cluster, because an annotated run never breaks across lines.
        /// </summary>
        /// <param name="clusterIndex">Index of the segment in the document's segment list.</param>
        /// <param name="segment">The segment that would end the run.</param>
        /// <returns>The overhang in pixels, or 0 when no annotated run ends here.</returns>
        public float TrailingRubyOverhang(int clusterIndex, in TextSegment segment)
        {
            if (!_prepared.Rubies.TryGetValue(segment.SourceIndex, out MeasuredRuby? ruby))
                return 0f;

            foreach (RubyPiece piece in ruby.Pieces)
            {
                if (piece.ClusterStart + piece.ClusterCount - 1 == clusterIndex)
                    return RubyOverhangBeyondRun(clusterIndex, segment);
            }

            return 0f;
        }

        /// <summary>
        /// Extent of one annotation along its own reading direction: the piece that starts at the cluster this segment
        /// ends, whose width is the column's height when the annotation reads downwards and the band's width when it
        /// reads along the line.
        /// </summary>
        /// <param name="ruby">Measured annotation of the source element.</param>
        /// <returns>The extent in pixels, or 0 when no piece starts here.</returns>
        private float ColumnHeightOf(MeasuredRuby ruby)
        {
            int cluster = _currentLineSegments[^1].Index;

            foreach (RubyPiece piece in ruby.Pieces)
            {
                if (piece.ClusterStart == cluster)
                    return piece.Width;
            }

            return 0f;
        }

        /// <summary>
        /// How far an underline reaches past the line's descender, or 0 when the font's own underline fits inside it.
        /// </summary>
        /// <param name="source">Element being drawn, which carries the font.</param>
        /// <param name="ascent">Ascent of the segment, which turns the font's metrics into offsets from the baseline.</param>
        /// <returns>The overhang in pixels.</returns>
        private static float UnderlineOverhang(in DrawElement source, float ascent)
        {
            _ = ascent;

            if (source.Font is not { } font || source.FontSize <= 0)
                return 0f;

            float position = font.GetUnderlinePosition(source.FontSize);
            float thickness = font.GetUnderlineThickness(source.FontSize);
            float descent = font.GetDescent(source.FontSize);

            return System.MathF.Max(0f, position + thickness - descent);
        }

        /// <summary>
        /// Move the pen to the next interval of this line, when it has one.
        /// <para>
        /// An exclusion that covers only part of a line leaves the text a second place to go; breaking the line
        /// there instead would leave that interval empty and the line short.
        /// </para>
        /// </summary>
        /// <returns>True when there was another interval and the pen moved into it.</returns>
        public bool TryAdvanceToNextSpan()
        {
            if (_spanIndex + 1 >= _lineSpans.Count)
                return false;

            _spanIndex++;
            LineLeft = _lineSpans[_spanIndex].Left;
            LineRight = _lineSpans[_spanIndex].Right;
            CurrentX = LineLeft;
            return true;
        }

        /// <summary>
        /// Hand the segments past a break point back to the next line.
        /// <para>
        /// The greedy loop accepts a segment as soon as it fits, so when line-start/line-end prohibition
        /// forces the break earlier than the last accepted segment, that tail is already on the line.
        /// Without this rollback the tail would be emitted twice — once at the end of this line and once
        /// at the start of the next — which is exactly the duplication the golden dumps recorded.
        /// </para>
        /// </summary>
        /// <param name="breakIndex">Index of the segment the line may end at.</param>
        public void DropSegmentsAfter(int breakIndex)
        {
            bool dropped = false;

            while (_currentLineSegments.Count > 0 && _currentLineSegments[^1].Index > breakIndex)
            {
                CurrentX -= _currentLineSegments[^1].Segment.Width;
                _currentLineSegments.RemoveAt(_currentLineSegments.Count - 1);
                dropped = true;
            }

            // The next line starts right after the break point; FinalizeLine reads this to set the next
            // line's first segment index, which the break search uses as its lower bound.
            NextSegToAdd = breakIndex + 1;

            if (!dropped)
                return;

            // Ascent, descent and the annotation reserve are maxima, so they cannot be rolled back by
            // subtraction.
            _maxAscent = 0;
            _maxDescent = 0;
            _maxExtraAbove = 0;
            _maxExtraBelow = 0;
            foreach (var entry in _currentLineSegments)
            {
                if (entry.Segment.Ascent > _maxAscent)
                    _maxAscent = entry.Segment.Ascent;
                if (entry.Segment.Descent > _maxDescent)
                    _maxDescent = entry.Segment.Descent;

                (float above, float below) = LineExtrasOf(entry.Index, entry.Segment);

                if (above > _maxExtraAbove)
                    _maxExtraAbove = above;
                if (below > _maxExtraBelow)
                    _maxExtraBelow = below;
            }
        }

        public void FinalizeLine(List<LayoutLine> lines)
        {
            // The line has been decided: if its last mark sticks out past the edge, that mark is the one the
            // convention lets hang. A mark that exactly fills the line does not stick out, so there is nothing to
            // mark - which is what made the earlier attempt at this look like a missing case.
            _hangingClusterIndex = -1;

            if (_currentLineSegments.Count > 0 && CurrentX > LineRight
                && MayHang(_currentLineSegments[^1].Segment))
            {
                _hangingClusterIndex = _currentLineSegments[^1].Index;
            }



            ResolvedTypography paragraphTypography = TypographyFor(CurrentParagraphIndex);
            float lineSpacing = paragraphTypography.LineSpacing;

            if (_currentLineSegments.Count == 0)
            {
                // Empty line: a rollback handed this line's tail back (see DropSegmentsAfter), so the line ends
                // with nothing on it. Advancing Y is the visible part; the load-bearing part is the line start,
                // which the break search uses as its lower bound. Leaving it behind the pen makes the search find
                // the same break again, the rollback drop the same tail again, and the greedy loop never advance -
                // it spins forever inside this method, which stalls the layout thread for every other consumer.
                CurrentY += lineSpacing > 0 ? lineSpacing : 0;
                LineStartSegIndex = NextSegToAdd;
                ApplyParagraphIndents();
                return;
            }

            // The room the line's content asked for outside its text box is part of the line itself: the band above
            // it (or a Beside column's overhang below it) is reserved for the whole line, so every baseline on the
            // line moves down by the same amount and the elements keep one common baseline. A line thus grows for
            // what it carries, and two neighbouring lines cannot overlap even when the author gave no line spacing.
            float extraAbove = _maxExtraAbove;
            float extraBelow = _maxExtraBelow;

            float lineHeight = extraAbove + _maxAscent + _maxDescent + extraBelow;
            if (lineHeight <= 0) lineHeight = lineSpacing;
            lineHeight += lineSpacing;

            bool isFirstLineOfParagraph = lines.Count == 0 ||
                (lines.Count > 0 && lines[^1].ParagraphIndex != CurrentParagraphIndex);

            // A character-based indent is measured in ems, one em being the run's font size.
            float indent = 0f;
            if (isFirstLineOfParagraph)
            {
                var paraSettings = ResolveParagraphSettings();
                indent = (paraSettings.FirstLineIndent ?? paragraphTypography.FirstLineIndent)
                         * EstimateEmWidth();
            }

            // Room this line gave way for at its start: an annotation on the run that opens the line, when it is
            // wider than that run *and* wider than the room the line's own start already leaves free, decides where
            // its content begins (jlreq §3.3.9's first method - the annotation's own start lines up with the line
            // head, and nothing is drawn outside the area). The shortfall is what the packing pass reserved.
            float headOverhang = _currentLineSegments.Count > 0
                ? HeadRubyOverhangOf(_currentLineSegments[0].Index, _currentLineSegments[0].Segment)
                : 0f;

            var line = new LayoutLine
            {
                Y = CurrentY,
                Height = lineHeight,
                Ascent = _maxAscent,
                Descent = _maxDescent,
                ExtraAbove = extraAbove,
                ExtraBelow = extraBelow,
                ParagraphIndex = CurrentParagraphIndex,
                IsFirstLineOfParagraph = isFirstLineOfParagraph,
                // Record the geometry L1 used so L2 (alignment, justification, squeezing) works on the same
                // values instead of re-deriving them from MaxWidth — and so the indent survives being
                // repositioned.
                //
                // LineLeft/LineRight describe the FIRST interval. Reading them from the mutable cursor would
                // record whichever interval the line happened to end in, which then became the line's own
                // reference: every element was pulled into the last interval instead of its own.
                LineLeft = _lineSpans.Count > 0 ? _lineSpans[0].Left : LineLeft,
                LineRight = _lineSpans.Count > 0 ? _lineSpans[0].Right : LineRight,
                // The content offset the line is laid out with: the paragraph's first-line indent plus the room this
                // line gave way for at its start (an overhanging annotation). Line adjustment repositions elements
                // from `LineLeft + LineIndent`, so carrying it here is what keeps the annotation's room through L2.
                LineIndent = indent + headOverhang,
                Spans = _lineSpans.Count > 0 ? [.. _lineSpans] : [new LineSpan(LineLeft, LineRight)],
            };

            // Position elements within the line. Each interval is laid out from its own left edge, so the pen
            // starts over when the breaker moved to the next one; the first interval additionally carries the
            // first-line indent.
            int penSpan = -1;
            float x = LineLeft + indent;


            // Compute text-only ascent/descent (excluding Middle-aligned non-text elements)
            // so that images are centered relative to the text region, not the inflated line.
            float textAsc = 0, textDesc = 0;
            foreach (var entry in _currentLineSegments)
            {
                var s = entry.Segment;
                if (s is { CharClass: CharacterClass.NonText, Source.VerticalAlignment: InlineVerticalAlignment.Middle })
                    continue;
                if (s.Ascent > textAsc) textAsc = s.Ascent;
                if (s.Descent > textDesc) textDesc = s.Descent;
            }

            for (int clusterOffset = 0; clusterOffset < _currentLineSegments.Count; clusterOffset++)
            {
                int elementSpan = _currentLineSegments[clusterOffset].SpanIndex;

                if (elementSpan != penSpan)
                {
                    penSpan = elementSpan;
                    x = SpanLeft(elementSpan)
                        + (elementSpan == 0 ? indent + headOverhang : 0f);
                }

                var seg = _currentLineSegments[clusterOffset].Segment;
                float elementHeight = seg.Ascent + seg.Descent;
                if (elementHeight <= 0) elementHeight = lineHeight;

                // Compute vertical offset based on alignment mode
                float yOffset;
                switch (seg.Source.VerticalAlignment)
                {
                    case InlineVerticalAlignment.Middle:
                        // Center within text region (not inflated line height)
                        if (textAsc > 0 || textDesc > 0)
                        {
                            float textTop = _maxAscent - textAsc;
                            float textBottom = _maxAscent + textDesc;
                            float textCenter = (textTop + textBottom) / 2f;
                            yOffset = textCenter - elementHeight / 2f;
                        }
                        else
                        {
                            // No text on line, fall back to overall centering
                            yOffset = (lineHeight - lineSpacing - elementHeight) / 2f;
                        }
                        break;
                    case InlineVerticalAlignment.Bottom:
                        // Align to bottom of line
                        yOffset = lineHeight - lineSpacing - elementHeight;
                        break;
                    case InlineVerticalAlignment.Top:
                        // Align to top of line
                        yOffset = 0;
                        break;
                    default:
                        // Baseline alignment: position Y so baseline aligns, under the annotation band.
                        yOffset = extraAbove + _maxAscent - seg.Ascent;
                        break;
                }

                int clusterIndex = _currentLineSegments[clusterOffset].Index;

                // A tab is a marker, not content: it stands for the stop the following elements line up at, so it
                // carries no text and no width of its own.
                if (seg.CharClass == CharacterClass.Tab)
                {
                    seg.Text = string.Empty;
                    seg.Width = 0f;
                    seg.Glyphs = null;
                    seg.GlyphAdvances = null;
                }

                // Content space is assembled from the two axes instead of from X and Y: the pen position is the
                // inline coordinate, the line position is the block coordinate, and the writing mode decides which
                // Godot axis carries each and in which direction. In horizontal writing the two coincide with X and
                // Y, which is why this reads as a plain mapping and why every golden stays byte-identical.
                //
                // The scalars the stages work with are deliberately unchanged: `x` stays the distance along the line
                // and `CurrentY` stays the distance between lines, so line breaking and line adjustment keep their
                // arithmetic and only the assembly knows the mode. `baselineY` is `CurrentY` plus an ascent, i.e. a
                // position on the block axis - so the output field keeps its name and carries the block
                // coordinate, which is the familiar Y in horizontal writing and the column's own coordinate in
                // vertical writing (the vertical renderer places its glyphs from the element's inline start instead).
                LayoutAxes axes = new(_typography.WritingMode, _typography.MaxWidth);

                var layoutElement = LayoutElement.FromSegment(
                    seg,
                    axes.Point(x, CurrentY + yOffset),
                    // The baseline sits `seg.Ascent` along the block axis from the box' block-start edge, which
                    // makes this correct for every alignment mode (text, Middle, Bottom, Top) and for non-text
                    // elements whose ascent was assigned from their vertical alignment.
                    CurrentY + yOffset + seg.Ascent,
                    axes.Size(seg.Width, elementHeight),
                    // The cluster index is the segment's index in the document, not its offset on the
                    // line: a line skips segments (leading spaces, block content), so offsets rewind.
                    clusterIndex,
                    lines.Count,
                    default,
                    _currentLineSegments[clusterOffset].SpanIndex,
                    BuildRuby(clusterIndex, seg, x, seg.Width, CurrentY + yOffset, elementHeight,
                        extraAbove, axes),
                    BuildEmphasis(seg, x, seg.Width, CurrentY + yOffset, elementHeight, paragraphTypography, axes),
                    // A Latin run is turned a quarter turn in vertical writing, so that it reads on down the column
                    // when the head is tilted; an ideograph stays upright, which is what clreq and jlreq prescribe.
                    axes.IsVertical && seg.CharClass == CharacterClass.Latin
                        ? GlyphRotation.ClockwiseQuarter
                        : GlyphRotation.None
                );
                if (clusterIndex == _hangingClusterIndex)
                {
                    // A hanging mark is reported twice over: Hanging is the flag a renderer acts on, and Reason
                    // is what a dump - and the reader of one - can see. Doc/Typography/limitations.md tells users
                    // the element reports the latter, so it does.
                    layoutElement = layoutElement with
                    {
                        Hanging = true,
                        Reason = layoutElement.Reason ?? "HangingPunctuation",
                    };
                    _hangingClusterIndex = -1;
                }

                line.Elements.Add(layoutElement);
                x += seg.Width;
            }

            lines.Add(line);

            // Reset for next line
            CurrentY += lineHeight;
            _maxAscent = 0;
            _maxDescent = 0;
            _maxExtraAbove = 0;
            _maxExtraBelow = 0;
            _lineCount++;
            _currentLineSegments.Clear();
            LineStartSegIndex = NextSegToAdd;
            _isFirstLineOfParagraph = false;

            ApplyParagraphIndents();
            CurrentX = LineLeft;
        }

        public void EndParagraph()
        {
            // Apply paragraph-after spacing
            var paraSettings = ResolveParagraphSettings();
            CurrentY += paraSettings.SpacingAfter ?? TypographyFor(CurrentParagraphIndex).ParagraphSpacing;

            // Advance to next paragraph
            CurrentParagraphIndex++;
            _isFirstLineOfParagraph = true;

            // Apply paragraph-before spacing for new paragraph
            var newParaSettings = ResolveParagraphSettings();
            CurrentY += newParaSettings.SpacingBefore ?? 0;

            ApplyParagraphIndents();
            ApplyFirstLineIndent();
        }

        /// <summary>
        /// Size of the laid-out content: how far it reaches along the inline axis (the longest line), and how far
        /// along the block axis (the last line's end), each including the content padding.
        /// <para>
        /// Both extents are reported in the two axes and then mapped into content space, so which Godot axis carries
        /// which follows the writing mode: in horizontal writing the pair reads as (width, height), and in vertical
        /// writing the first component is how far the columns run and the second the longest column - a caller that
        /// wants to size a surface around columns needs both, and reading them off X and Y would get neither.
        /// </para>
        /// </summary>
        /// <param name="lines">Lines the layout produced.</param>
        /// <returns>The content size in content space.</returns>
        public Vector2 ComputeContentSize(List<LayoutLine> lines)
        {
            if (lines.Count == 0)
                return Vector2.Zero;

            float padding = _typography.Padding;
            var axes = new LayoutAxes(_typography.WritingMode, _typography.MaxWidth);

            // The extents are read back from the elements the layout produced rather than accumulated while the text
            // was positioned: an element placed by another path - a block's own line, a marker - is in the same list,
            // and accumulating at the point of use is how those came to be missing from the content size. The block
            // extent starts from the lines themselves, because a line's box already holds the annotation band it
            // reserved for them.
            float inlineExtent = 0f;
            float blockExtent = 0f;

            foreach (LayoutLine line in lines)
            {
                blockExtent = System.MathF.Max(blockExtent, line.Y + line.Height);

                foreach (LayoutElement element in line.Elements)
                {
                    inlineExtent = System.MathF.Max(inlineExtent,
                        axes.Inline(element.Position) + axes.Inline(element.Size));

                    // What a renderer draws is not only the elements' boxes. An annotation can be wider than the
                    // character it annotates - and in a column it runs past that character along the line - while an
                    // emphasis mark is centred outside the element's box on its own side: below it in Chinese, above
                    // it in Japanese, to the right of the column in vertical writing. A content box that stopped at
                    // the boxes clipped exactly those decorations, and that box is what a consumer sizes a scroll
                    // region with.
                    if (element.Ruby is { } ruby)
                    {
                        // Which edges the annotation's numbers name depends on the writing mode and on its own
                        // orientation: along a column its baseline is the pen and its width the run down the column,
                        // while a column of its own (Bopomofo beside the base) spans the band it was given. Reading
                        // X as "the inline start" would be right in horizontal writing and wrong by a whole page in
                        // vertical writing - the values only mean something through the axes.
                        float annotationEnd = axes.IsVertical
                            ? ruby.BaselineY + ruby.Width
                            : ruby.Orientation == RubyOrientation.Vertical
                                ? ruby.X + ruby.BandWidth
                                : ruby.X + ruby.Width;

                        inlineExtent = System.MathF.Max(inlineExtent, annotationEnd);
                    }

                    if (element.Emphasis is { } mark)
                    {
                        var centre = new Vector2(mark.X, mark.CenterY);
                        float half = mark.Size * 0.5f;

                        inlineExtent = System.MathF.Max(inlineExtent, axes.Inline(centre) + half);
                        blockExtent = System.MathF.Max(blockExtent, axes.Block(centre) + half);
                    }
                }
            }

            return axes.Size(inlineExtent, blockExtent + padding);
        }

        /// <summary>
        /// Advance state past an auto-size block that was placed externally.
        /// </summary>
        public void AdvancePastBlock(float blockHeight)
        {
            CurrentY += blockHeight;
            _maxAscent = 0;
            _maxDescent = 0;
            _currentLineSegments.Clear();
            LineStartSegIndex = NextSegToAdd;

            ApplyParagraphIndents();
            CurrentX = LineLeft;
        }

        /// <summary>
        /// Attempt to advance Y past obstructing wrap regions when no space is available.
        /// Returns true if Y was advanced (caller should retry), false if no regions to clear.
        /// </summary>
        public bool TryAdvancePastObstruction()
        {
            if (_wrapMgr == null) return false;

            float clearY = _wrapMgr.GetClearY();
            if (clearY > CurrentY)
            {
                CurrentY = clearY;
                ApplyParagraphIndents();
                CurrentX = LineLeft;
                return true;
            }
            return false;
        }

        private ParagraphSettings ResolveParagraphSettings()
        {
            if (_prepared.Paragraphs.Count > CurrentParagraphIndex)
                return _prepared.Paragraphs[CurrentParagraphIndex].Settings;
            return new ParagraphSettings();
        }

        private void ApplyParagraphIndents()
        {
            var paraSettings = ResolveParagraphSettings();
            float leftIndent = paraSettings.LeftIndent;
            float rightIndent = paraSettings.RightIndent;
            float padding = _typography.Padding;

            _lineSpans.Clear();
            _spanIndex = 0;

            if (_wrapMgr != null)
            {
                // Every interval available at this Y, not only the leftmost one: text continues in the next
                // interval instead of the line ending early.
                float estimatedLineHeight = EstimateEmWidth(); // rough estimate
                List<(float Left, float Right)> available =
                    _wrapMgr.GetAvailableSpans(CurrentY, estimatedLineHeight, _lineCount);

                foreach ((float left, float right) in available)
                {
                    // The paragraph indents narrow the outer edges of the line, not each interval: an
                    // exclusion in the middle of a line does not indent what sits to its right.
                    float spanLeft = left + (left <= available[0].Left ? leftIndent : 0f);
                    float spanRight = right - (right >= available[^1].Right ? rightIndent : 0f);

                    if (spanRight > spanLeft)
                        _lineSpans.Add(new LineSpan(spanLeft, spanRight));
                }
            }
            else
            {
                _lineSpans.Add(new LineSpan(padding + leftIndent, padding + _typography.InlineLimit - rightIndent));
            }

            if (_lineSpans.Count > 0)
            {
                LineLeft = _lineSpans[0].Left;
                LineRight = _lineSpans[0].Right;
            }
            else
            {
                // No available space at this Y — a zero-width line the caller advances past.
                LineLeft = 0;
                LineRight = 0;
            }

            CurrentX = LineLeft;
        }

        /// <summary>
        /// Parameters that govern one paragraph: the request's, unless the paragraph named its own language — in
        /// which case that language's defaults (its rule set, its indent, its spacing) apply to it.
        /// </summary>
        /// <param name="paragraphIndex">Paragraph to resolve.</param>
        /// <returns>The parameters for that paragraph.</returns>
        private ResolvedTypography TypographyFor(int paragraphIndex) =>
            _paragraphTypography is not null
            && paragraphIndex >= 0
            && paragraphIndex < _paragraphTypography.Length
                ? _paragraphTypography[paragraphIndex]
                : _typography;

        /// <summary>
        /// Left edge of an interval, falling back to the line's own left edge when the line has no interval
        /// list yet (a line placed by a block rather than by the breaker).
        /// </summary>
        /// <param name="spanIndex">Index of the interval.</param>
        /// <returns>The interval's left edge.</returns>
        private readonly float SpanLeft(int spanIndex) =>
            spanIndex >= 0 && spanIndex < _lineSpans.Count ? _lineSpans[spanIndex].Left : LineLeft;

        private void ApplyFirstLineIndent()
        {
            if (!_isFirstLineOfParagraph) return;
            var paraSettings = ResolveParagraphSettings();
            float indent = (paraSettings.FirstLineIndent ?? TypographyFor(CurrentParagraphIndex).FirstLineIndent)
                * EstimateEmWidth();
            CurrentX += indent;
        }

        /// <summary>
        /// One em in pixels, used wherever a distance is specified in characters (first-line indent,
        /// wrap-region clear height). The em is the font size of the paragraph's first run; the
        /// previous implementation returned a hardcoded 16 px *and* another call site used the line's
        /// ascent+descent, so the two disagreed and neither matched the requested "N characters".
        /// </summary>
        private readonly float EstimateEmWidth()
        {
            var segments = _prepared.Segments;
            for (int i = 0; i < segments.Count; i++)
            {
                int fontSize = segments[i].Source.FontSize;
                if (fontSize > 0)
                    return fontSize;
            }

            return DefaultFontSize;
        }
    }
}
