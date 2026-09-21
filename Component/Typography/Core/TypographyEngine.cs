using System;
using System.Collections.Generic;
using System.Diagnostics;
using Godot;
using GodotNodeExtension.Component.Typography.Core.Model;
using GodotNodeExtension.Component.Typography.Languages;
using GodotNodeExtension.Component.Typography.Server;

namespace GodotNodeExtension.Component.Typography.Core;

/// <summary>
/// Typography engine implementing a two-phase layout model (Prepare → Layout).
/// This is the pure computation class with no threading logic.
/// Invoked by <see cref="TypographyServer"/> on its dedicated background thread,
/// or used directly for synchronous layout.
/// Supports streaming: elements can be appended incrementally via <see cref="Append"/>.
/// </summary>
public class TypographyEngine : IDisposable
{
    private TypographySettings _settings;
    private readonly ContentPreparer _preparer;
    private readonly List<LayoutLine> _lines = [];
    private PreparedContent? _preparedContent;
    private int _streamSourceIndex;
    private bool _disposed;
    private double _prepareMs;
    private double _boundaryMs;
    private double _flattenMs;

    /// <summary>
    /// Cached compile-phase boundaries, with the parameters they were built from. They are
    /// width-independent, so a relayout reuses them; a change that can affect a boundary decision
    /// (see <see cref="ResolvedTypography.AffectsBoundaries"/>) rebuilds them.
    /// </summary>
    private List<Boundary> _boundaries = [];
    private ResolvedTypography? _boundaryTypography;

    /// <summary>
    /// Signature of the languages the paragraphs name, so the boundary cache notices a paragraph that switched
    /// language even when the request's own parameters did not change.
    /// </summary>
    private string _boundaryLanguageSignature = string.Empty;

    /// <summary>
    /// Phase timings of the most recent prepare/layout/flatten, surfaced through
    /// <see cref="LayoutResult.Timings"/>. The split exists because the point of separating prepare from
    /// layout is that a resize must only re-run the cheap half, and that claim needs numbers.
    /// </summary>
    public LayoutTimings LastTimings { get; private set; }

    /// <summary>
    /// Prohibited break candidates rejected in the most recent line breaking pass; see
    /// <see cref="LayoutResult.ProhibitedBreakSkips"/>.
    /// </summary>
    public int LastProhibitedBreakSkips { get; private set; }

    /// <summary>
    /// Lines broken at an invalid position in the most recent line breaking pass; see
    /// <see cref="LineBreaker.ForcedBreaks"/>.
    /// </summary>
    public int LastForcedBreaks { get; private set; }

    /// <summary>
    /// Boundaries of the most recent layout, one per adjacent cluster pair, produced by
    /// <see cref="BoundaryBuilder"/> in the compile phase. Exposed so a dump or a test can inspect the
    /// decisions the pipeline is about to take, before the stages are moved onto them.
    /// </summary>
    public IReadOnlyList<Boundary> LastBoundaries { get; private set; } = [];

    /// <summary>
    /// Create a new typography engine with the given settings.
    /// </summary>
    /// <param name="settings">Global typography configuration.</param>
    public TypographyEngine(TypographySettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _preparer = new ContentPreparer();
    }

    /// <summary>
    /// Current typography settings. Can be updated before a Layout call.
    /// </summary>
    public TypographySettings Settings
    {
        get => _settings;
        set => _settings = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>
    /// The prepared content accumulated so far (for streaming mode or re-layout).
    /// </summary>
    public PreparedContent? CurrentPreparedContent => _preparedContent;

    /// <summary>
    /// Total content size after current layout state.
    /// </summary>
    public Vector2 ContentSize { get; private set; }

    /// <summary>
    /// Get all laid-out lines.
    /// </summary>
    public IReadOnlyList<LayoutLine> Lines => _lines;

    // ── Phase 1: Prepare (one-time, expensive) ──

    /// <summary>
    /// Prepare a complete element list: segment, classify, and measure via HarfBuzz.
    /// The result is cached and can be used for multiple Layout calls.
    /// </summary>
    /// <param name="elements">Source DrawElements to prepare.</param>
    /// <returns>The prepared content.</returns>
    public PreparedContent Prepare(ReadOnlySpan<DrawElement> elements)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        long start = Stopwatch.GetTimestamp();
        _preparedContent = _preparer.Prepare(elements, _settings);
        _prepareMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;

        // New measurement, new clusters: the boundaries describe segments that no longer exist.
        InvalidateBoundaries();

        return _preparedContent;
    }

    // ── Phase 2: Layout (pure arithmetic, cheap, repeatable) ──

    /// <summary>
    /// Perform layout on prepared content with the current settings.
    /// Can be called multiple times with different settings (e.g., on resize)
    /// without re-measuring text.
    /// </summary>
    /// <param name="prepared">Prepared content from Prepare().</param>
    /// <param name="contentSize">Output: total content size after layout.</param>
    /// <returns>Laid-out lines.</returns>
    public List<LayoutLine> Layout(PreparedContent prepared, out Vector2 contentSize)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // The request's language supplies the geometry and the defaults every paragraph inherits. A paragraph
        // that names its own language gets its own resolution, which is what makes a mixed-language document
        // work: the Chinese paragraphs keep prohibition rules while the English ones break lines by UAX #14.
        var typography = LanguageProfileRegistry.Shared.Resolve(_settings);
        ResolvedTypography[]? paragraphTypography = ResolveParagraphTypography(prepared);
        string languageSignature = LanguageSignature(prepared);

        // Compile phase: the boundary decisions between adjacent clusters. They are width-independent and
        // therefore belong with the prepared content rather than with a line, so a resize reuses them and
        // only a change that can affect a decision rebuilds them.
        if (_boundaryTypography is null
            || typography.AffectsBoundaries(_boundaryTypography)
            || !string.Equals(_boundaryLanguageSignature, languageSignature, StringComparison.Ordinal))
        {
            long boundaryStart = Stopwatch.GetTimestamp();
            _boundaries = BoundaryBuilder.Build(prepared, typography, paragraphTypography);
            _boundaryMs = Stopwatch.GetElapsedTime(boundaryStart).TotalMilliseconds;
            _boundaryTypography = typography;
            _boundaryLanguageSignature = languageSignature;
        }
        else
        {
            _boundaryMs = 0d;
        }

        LastBoundaries = _boundaries;

        // L1: Line breaking
        var breaker = new LineBreaker(typography, paragraphTypography);
        long start = Stopwatch.GetTimestamp();
        var lines = breaker.BreakLines(prepared, LastBoundaries, out contentSize);
        double breakMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        LastProhibitedBreakSkips = breaker.ProhibitedBreakSkips;
        LastForcedBreaks = breaker.ForcedBreaks;

        // L2: Line adjustment (punctuation compression, spacing, alignment)
        var adjuster = new LineAdjuster(typography);
        start = Stopwatch.GetTimestamp();
        adjuster.AdjustLines(lines, prepared, LastBoundaries, paragraphTypography);
        double adjustMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;

        LastTimings = new LayoutTimings(_prepareMs, _boundaryMs, breakMs, adjustMs, _flattenMs);

        // The prepare time belongs to the request that actually prepared. Consuming it here makes
        // "PrepareMs == 0" the signature of a relayout that reused the cached PreparedContent — which is
        // exactly the property RequestRelayout promises, and it would be invisible if the number lingered.
        _prepareMs = 0;

        _lines.Clear();
        _lines.AddRange(lines);
        ContentSize = contentSize;

        return lines;
    }

    // ── Convenience: Prepare + Layout in one call ──

    /// <summary>
    /// Shorthand for Prepare() followed by Layout().
    /// </summary>
    /// <param name="elements">Source DrawElements.</param>
    /// <param name="contentSize">Output: total content size.</param>
    /// <returns>Laid-out lines.</returns>
    public List<LayoutLine> PrepareAndLayout(ReadOnlySpan<DrawElement> elements, out Vector2 contentSize)
    {
        var prepared = Prepare(elements);
        return Layout(prepared, out contentSize);
    }

    // ── Streaming API (incremental prepare + layout) ──

    /// <summary>
    /// Append a single element for incremental/streaming layout.
    /// Internally prepares (measures) the element, then re-lays-out
    /// only the unfrozen portion. Frozen lines are not modified.
    /// </summary>
    /// <param name="element">The element to append.</param>
    /// <param name="sourceIndex">Index of this element in the source list.</param>
    public void Append(in DrawElement element, int sourceIndex)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Ensure PreparedContent exists. Without a Prepare phase to take the axes from, they come from the
        // request; a language profile that asked for a vertical mode would have failed at resolution already.
        _preparedContent ??= new PreparedContent
        {
            Axes = new LayoutAxes(Settings.WritingMode ?? WritingMode.HorizontalTb, Settings.MaxWidth),
        };

        // The prepared content grows, so the cached boundaries no longer cover it.
        InvalidateBoundaries();

        // Incrementally prepare the new element
        var elem = element;
        _preparer.AppendPrepare(_preparedContent, ref elem, sourceIndex, _settings);
        _streamSourceIndex = sourceIndex + 1;

        // Re-layout only unfrozen lines:
        // Keep all frozen lines, then re-break from the first unfrozen segment
        RelayoutFromUnfrozen();
    }

    /// <summary>
    /// Finalize the current line and freeze all lines.
    /// Call when the stream ends.
    /// </summary>
    public void Flush()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Freeze all lines
        for (int i = 0; i < _lines.Count; i++)
        {
            _lines[i].IsFrozen = true;
        }

        // Update content size
        if (_lines.Count > 0)
        {
            var lastLine = _lines[^1];
            float maxWidth = 0f;
            foreach (var line in _lines)
            {
                float lineWidth = 0f;
                foreach (var elem in line.Elements)
                    lineWidth = elem.Position.X + elem.Size.X;
                if (lineWidth > maxWidth) maxWidth = lineWidth;
            }
            ContentSize = new Vector2(maxWidth, lastLine.Y + lastLine.Height);
        }
    }

    /// <summary>
    /// Re-layout starting from the first unfrozen segment.
    /// Frozen lines are preserved; only the tail is recalculated.
    /// </summary>
    private void RelayoutFromUnfrozen()
    {
        if (_preparedContent == null) return;

        // Find where frozen lines end
        int frozenLineCount = 0;
        int firstUnfrozenSegmentIndex = 0;
        float startY = 0f;

        for (int i = 0; i < _lines.Count; i++)
        {
            if (_lines[i].IsFrozen)
            {
                frozenLineCount++;
                startY = _lines[i].Y + _lines[i].Height;
            }
            else
            {
                break;
            }
        }

        // Count segments in frozen lines to find where to start breaking
        if (frozenLineCount > 0)
        {
            foreach (var frozenLine in _lines.GetRange(0, frozenLineCount))
            {
                firstUnfrozenSegmentIndex += frozenLine.Elements.Count;
            }
        }

        // Remove unfrozen lines
        if (_lines.Count > frozenLineCount)
        {
            _lines.RemoveRange(frozenLineCount, _lines.Count - frozenLineCount);
        }

        // Re-break from unfrozen segments
        if (firstUnfrozenSegmentIndex < _preparedContent.Segments.Count)
        {
            var tailTypography = LanguageProfileRegistry.Shared.Resolve(_settings);
            var breaker = new LineBreaker(tailTypography);
            // Create a sub-prepared content for just the unfrozen segments
            var tailSegments = _preparedContent.Segments.GetRange(
                firstUnfrozenSegmentIndex,
                _preparedContent.Segments.Count - firstUnfrozenSegmentIndex);

            if (tailSegments.Count > 0)
            {
                var tailPrepared = new PreparedContent(
                    tailSegments,
                    [],
                    _streamSourceIndex)
                {
                    Axes = _preparedContent.Axes,
                };
                tailPrepared.BuildParagraphs();

                // The tail is a separate segment list, so it needs its own compile-phase boundaries.
                var tailBoundaries = BoundaryBuilder.Build(tailPrepared, tailTypography);

                var newLines = breaker.BreakLines(
                    tailPrepared,
                    tailBoundaries,
                    out _);
                LastProhibitedBreakSkips = breaker.ProhibitedBreakSkips;
                LastForcedBreaks = breaker.ForcedBreaks;

                // Adjust Y positions to continue from frozen lines
                foreach (var line in newLines)
                {
                    line.Y += startY;
                }

                // Apply adjustments
                var adjuster = new LineAdjuster(tailTypography);
                adjuster.AdjustLines(newLines, tailPrepared, tailBoundaries);

                // Freeze all but the last line (current line)
                for (int i = 0; i < newLines.Count - 1; i++)
                {
                    newLines[i].IsFrozen = true;
                }

                _lines.AddRange(newLines);
            }
        }

        // Update content size
        if (_lines.Count > 0)
        {
            var lastLine = _lines[^1];
            ContentSize = new Vector2(_settings.MaxWidth, lastLine.Y + lastLine.Height);
        }
    }

    /// <summary>
    /// Flatten all lines into a single list of LayoutElements.
    /// Block elements are expanded: the block's internal DrawElements (between
    /// the BlockInfo marker and the IsBlockEnd marker in the source array)
    /// are emitted with their positions offset by the block's absolute position.
    /// </summary>
    /// <returns>All layout elements from all lines.</returns>
    public List<LayoutElement> GetLayoutElements()
    {
        return GetLayoutElements(null);
    }

    /// <summary>
    /// Flatten all lines into a single list of LayoutElements.
    /// When <paramref name="sourceElements"/> is provided, block elements are expanded
    /// with their internal elements offset by the block's absolute position.
    /// </summary>
    /// <param name="sourceElements">Original source DrawElement array (needed for block expansion). May be null.</param>
    /// <returns>All layout elements from all lines.</returns>
    public List<LayoutElement> GetLayoutElements(DrawElement[]? sourceElements)
    {
        long start = Stopwatch.GetTimestamp();
        var result = new List<LayoutElement>();

        // The stages that follow position what the line breaker measured, so they need the same axes it used:
        // "the same line" is a block coordinate and a rectangle's baseline is its block-end edge, whichever
        // axis that turns out to be.
        LayoutAxes axes = new(Settings.WritingMode ?? WritingMode.HorizontalTb, Settings.MaxWidth);

        foreach (var line in _lines)
        {
            foreach (var elem in line.Elements)
            {
                // Check for auto-size block (has sub-layout lines)
                if (elem is { SubLines: not null, BlockInfo: not null })
                {
                    ExpandAutoSizeBlock(result, elem, axes);
                    continue;
                }

                // Check if this element is a fixed-size block-start marker
                if (sourceElements != null &&
                    elem.SourceIndex >= 0 && elem.SourceIndex < sourceElements.Length &&
                    sourceElements[elem.SourceIndex].BlockInfo != null)
                {
                    ExpandFixedSizeBlock(result, elem, sourceElements);
                }
                else
                {
                    result.Add(elem);
                }
            }
        }

        MergeInlineBackgrounds(result, _lines, axes);

        // Flattening (block expansion, background merging) is part of the request's cost, so it is counted
        // here rather than by the caller: the number then travels with the result.
        _flattenMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        LastTimings = new LayoutTimings(
            LastTimings.PrepareMs,
            LastTimings.BoundaryMs,
            LastTimings.BreakMs,
            LastTimings.AdjustMs,
            _flattenMs);

        return result;
    }

    /// <summary>
    /// Shift a baseline by a block's content offset, leaving the "not computed" sentinel alone. Only the block
    /// component of the offset applies: the baseline is a block coordinate, so moving the box along the inline
    /// axis does not move it.
    /// </summary>
    /// <param name="baselineY">The baseline to shift, possibly <see cref="float.NaN"/>.</param>
    /// <param name="offset">Content offset applied to the owning element's position.</param>
    /// <param name="axes">Axes of the layout, which say which component of the offset is the block one.</param>
    /// <returns>The shifted baseline, or NaN when there was none.</returns>
    private static float OffsetBaseline(float baselineY, Vector2 offset, LayoutAxes axes) =>
        float.IsNaN(baselineY) ? baselineY : baselineY + axes.BlockDelta(offset);

    /// <summary>
    /// Post-process layout elements to merge per-segment backgrounds into single Rect elements.
    /// When a DrawElement with BackgroundPadding is split into multiple segments (e.g., CJK chars
    /// inside inline code), each segment inherits BackgroundColor. This method replaces those
    /// individual backgrounds with one Rect element per same-line group, providing correct
    /// padding on only the outer edges.
    /// <para>
    /// Layout-aware padding: ContentPreparer adds padX to the first and last segment widths
    /// so line breaking reserves space. Here we adjust text positions within that reserved space:
    /// first element shifts right by padX (left padding gap), last element shrinks by padX
    /// (right padding gap). The background Rect covers the full reserved region.
    /// </para>
    /// When <see cref="LayoutElement.BackgroundFillLine"/> is set, the background rectangle
    /// expands vertically to fill the full line height instead of fitting text only.
    /// </summary>
    private static void MergeInlineBackgrounds(List<LayoutElement> result, List<LayoutLine> lines, LayoutAxes axes)
    {
        int i = 0;
        while (i < result.Count)
        {
            var elem = result[i];
            if (elem.Type != DrawElement.ElementType.Text || !elem.BackgroundColor.HasValue)
            {
                i++;
                continue;
            }

            // Skip elements with no padding and no fill-line (rendered directly in _Draw)
            if (elem.BackgroundPadding == Vector2.Zero && !elem.BackgroundFillLine)
            {
                i++;
                continue;
            }

            // Found start of a background group. Scan forward for same-SourceIndex, same-line. "Same line" is a
            // position on the block axis: in vertical writing the elements of one line share the column's block
            // coordinate and differ along the inline axis, so comparing a Y would end the group after one glyph.
            int srcIdx = elem.SourceIndex;
            float lineBlock = axes.Block(elem.Position);
            int groupStart = i;
            int groupEnd = i; // inclusive

            while (groupEnd + 1 < result.Count)
            {
                var next = result[groupEnd + 1];
                if (next.SourceIndex != srcIdx ||
                    next.Type != DrawElement.ElementType.Text ||
                    MathF.Abs(axes.Block(next.Position) - lineBlock) > 1f)
                    break;
                groupEnd++;
            }

            var pad = result[groupStart].BackgroundPadding;

            // Adjust text positions: shift the first element along the inline axis by padX to create the padding
            // gap the breaker reserved, and shrink the last element's inline extent by the same amount. The gap
            // belongs to the inline axis - moving X unconditionally (which is what this did) moved a vertical run
            // along the *block* axis instead, straight out of its column.
            if (pad.X > 0)
            {
                var f = result[groupStart];
                f.Position += axes.InlineUnit * pad.X;
                f.Size = axes.Size(axes.Inline(f.Size) - pad.X, axes.BlockExtent(f.Size));
                result[groupStart] = f;

                var l = result[groupEnd];
                l.Size = axes.Size(axes.Inline(l.Size) - pad.X, axes.BlockExtent(l.Size));
                result[groupEnd] = l;
            }

            // Bounding box of the run, in axis terms: the padding is axis-relative (the breaker reserved padX along
            // the inline axis by widening the first and last cluster, and the band grows by padY across it), so
            // building the rectangle from X/Y put a vertical run's padding on the wrong axis.
            var first = result[groupStart];
            var last = result[groupEnd];
            float inlineStart = axes.Inline(first.Position) - pad.X;
            float inlineEnd = axes.Inline(last.Position) + axes.Inline(last.Size) + pad.X;
            float blockStart = axes.Block(first.Position);
            float blockEnd = blockStart + axes.BlockExtent(first.Size);

            for (int k = groupStart + 1; k <= groupEnd; k++)
            {
                var e = result[k];
                blockStart = MathF.Min(blockStart, axes.Block(e.Position));
                blockEnd = MathF.Max(blockEnd, axes.Block(e.Position) + axes.BlockExtent(e.Size));
            }

            // When BackgroundFillLine is set, expand to the line's full block extent
            if (first.BackgroundFillLine)
            {
                var line = FindLineByBlock(lines, lineBlock);
                if (line != null)
                {
                    blockStart = line.Y;
                    blockEnd = line.Y + line.Height;
                }
            }

            blockStart -= pad.Y;
            blockEnd += pad.Y;

            // Insert a Rect element for the merged background (before text, correct z-order)
            var bgPosition = axes.Point(inlineStart, blockStart);
            var bgSize = axes.Size(inlineEnd - inlineStart, blockEnd - blockStart);

            var bgRect = new LayoutElement
            {
                SourceIndex = srcIdx,
                Position = bgPosition,
                // A rectangle's baseline is its block-end edge, which is the bottom edge in horizontal writing:
                // written through the axes it holds in either mode.
                BaselineY = axes.Block(bgPosition) + axes.BlockExtent(bgSize),
                Size = bgSize,
                Type = DrawElement.ElementType.Rect,
                Color = first.BackgroundColor!.Value,
                CornerRadius = first.BackgroundCornerRadius,
                // Synthesised: it never went through classification.
                CharClass = CharacterClass.NonText,
                SourceRange = first.SourceRange,
                ClusterStart = first.ClusterStart,
                ClusterEnd = last.ClusterEnd,
                LineIndex = first.LineIndex,
                Reason = "MergedInlineBackground",
            };
            result.Insert(groupStart, bgRect);
            groupEnd++; // Adjust for insertion

            // Clear background from individual text elements
            for (int k = groupStart + 1; k <= groupEnd; k++)
            {
                var e = result[k];
                e.BackgroundColor = null;
                e.BackgroundPadding = Vector2.Zero;
                result[k] = e;
            }

            i = groupEnd + 1;
        }
    }

    /// <summary>
    /// Find the layout line that contains the given position on the block axis: <see cref="LayoutLine.Y"/> and
    /// <see cref="LayoutLine.Height"/> are block-axis scalars, so the argument is one too (a Y in horizontal
    /// writing, a column coordinate in vertical writing).
    /// </summary>
    private static LayoutLine? FindLineByBlock(List<LayoutLine> lines, float blockPos)
    {
        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (blockPos >= line.Y && blockPos < line.Y + line.Height)
                return line;
        }
        return null;
    }

    /// <summary>
    /// Expand an auto-size block by flattening its sub-layout lines.
    /// Inner element positions are offset by the block position plus indent and padding.
    /// Emits block decorations (background, border) before content.
    /// Recursively handles nested auto-size blocks.
    /// </summary>
    private static void ExpandAutoSizeBlock(List<LayoutElement> result, in LayoutElement blockElem,
        LayoutAxes axes)
    {
        var blockInfo = blockElem.BlockInfo!;
        var blockPos = blockElem.Position;
        var blockSize = blockElem.Size;

        // Emit background rectangle if specified
        if (blockInfo.BackgroundColor.HasValue)
        {
            result.Add(new LayoutElement
            {
                SourceIndex = blockElem.SourceIndex,
                Position = blockPos,
                // A synthesised rectangle's baseline is its block-end edge, in whichever mode that turns out to be.
                BaselineY = axes.Block(blockPos) + axes.BlockExtent(blockSize),
                Size = blockSize,
                Type = DrawElement.ElementType.Rect,
                Color = blockInfo.BackgroundColor.Value,
                CornerRadius = blockInfo.BackgroundCornerRadius,
                CharClass = CharacterClass.NonText,
                SourceRange = blockElem.SourceRange,
                ClusterStart = blockElem.ClusterStart,
                ClusterEnd = blockElem.ClusterEnd,
                LineIndex = blockElem.LineIndex,
                Reason = "BlockDecoration:Background",
                ExtensionId = -1,
                TextEffectId = -1,
            });
        }

        // Emit left border line if specified
        if (blockInfo is { LeftBorderColor: not null, LeftBorderWidth: > 0 })
        {
            var borderPosition = new Vector2(blockPos.X + blockInfo.LeftBorderOffset, blockPos.Y);
            var borderSize = new Vector2(blockInfo.LeftBorderWidth, blockSize.Y);

            result.Add(new LayoutElement
            {
                SourceIndex = blockElem.SourceIndex,
                Position = borderPosition,
                // The border's own block-end edge, so the relation below holds for it as well.
                BaselineY = axes.Block(borderPosition) + axes.BlockExtent(borderSize),
                Size = borderSize,
                Type = DrawElement.ElementType.Line,
                Color = blockInfo.LeftBorderColor.Value,
                CharClass = CharacterClass.NonText,
                SourceRange = blockElem.SourceRange,
                ClusterStart = blockElem.ClusterStart,
                ClusterEnd = blockElem.ClusterEnd,
                LineIndex = blockElem.LineIndex,
                Reason = "BlockDecoration:LeftBorder",
                ExtensionId = -1,
                TextEffectId = -1,
            });
        }

        // Expand inner content with offset
        var contentOffset = new Vector2(
            blockPos.X + blockInfo.LeftIndent + blockInfo.Padding.X,
            blockPos.Y + blockInfo.Padding.Y
        );

        // Emit marker text (list bullet/number) aligned with first content line
        if (blockInfo is { MarkerText: not null, MarkerFont: not null })
        {
            float markerY = contentOffset.Y;
            // Align with the first line's Y position if available
            if (blockElem.SubLines!.Count > 0)
            {
                var firstLine = blockElem.SubLines[0];
                markerY += firstLine.Y;
            }

            result.Add(new LayoutElement
            {
                SourceIndex = blockElem.SourceIndex,
                Position = new Vector2(blockPos.X + blockInfo.Padding.X, markerY),
                // No baseline: the marker is emitted outside the line breaker and its font metrics are
                // not available on the layout thread. The renderer falls back to the font ascent, and
                // the glyph-level output at P1 fills this in from the font catalog.
                BaselineY = float.NaN,
                Size = Vector2.Zero, // Sized by renderer from font metrics
                Type = DrawElement.ElementType.Text,
                Text = blockInfo.MarkerText,
                Font = blockInfo.MarkerFont,
                FontSize = blockInfo.MarkerFontSize,
                Color = blockInfo.MarkerColor,
                CharClass = CharacterClass.NonText,
                SourceRange = blockElem.SourceRange,
                ClusterStart = blockElem.ClusterStart,
                ClusterEnd = blockElem.ClusterEnd,
                LineIndex = blockElem.LineIndex,
                Reason = "BlockDecoration:Marker",
                ExtensionId = -1,
                TextEffectId = -1,
            });
        }
        // Emit custom marker elements (e.g., checkboxes) aligned with first content line
        else if (blockInfo.MarkerElements is { Count: > 0 })
        {
            float markerY = contentOffset.Y;
            if (blockElem.SubLines!.Count > 0)
            {
                var firstLine = blockElem.SubLines[0];
                markerY += firstLine.Y;
            }

            var markerOrigin = new Vector2(blockPos.X + blockInfo.Padding.X, markerY);
            foreach (var markerElem in blockInfo.MarkerElements)
            {
                result.Add(new LayoutElement
                {
                    SourceIndex = blockElem.SourceIndex,
                    Position = markerOrigin + markerElem.Position,
                    // Text markers have no computed baseline (see the marker-text branch above);
                    // non-text markers use their block-end edge, which is what the geometry stages
                    // assign for a top-aligned inline object.
                    BaselineY = markerElem.Type == DrawElement.ElementType.Text
                        ? float.NaN
                        : axes.Block(markerOrigin + markerElem.Position) + axes.BlockExtent(markerElem.Size),
                    Size = markerElem.Size,
                    Type = markerElem.Type,
                    Color = markerElem.Color,
                    Text = markerElem.Text,
                    Font = markerElem.Font,
                    FontSize = markerElem.FontSize,
                    CornerRadius = markerElem.CornerRadius,
                    Texture = markerElem.Texture,
                    CharClass = CharacterClass.NonText,
                    SourceRange = blockElem.SourceRange,
                    ClusterStart = blockElem.ClusterStart,
                    ClusterEnd = blockElem.ClusterEnd,
                    LineIndex = blockElem.LineIndex,
                    Reason = "BlockDecoration:MarkerElement",
                    ExtensionId = -1,
                    TextEffectId = -1,
                });
            }
        }

        foreach (var subLine in blockElem.SubLines!)
        {
            foreach (var innerElem in subLine.Elements)
            {
                if (innerElem is { SubLines: not null, BlockInfo: not null })
                {
                    // Nested auto-size block: recursively expand with accumulated offset
                    var nested = innerElem;
                    nested.Position += contentOffset;
                    nested.BaselineY = OffsetBaseline(nested.BaselineY, contentOffset, axes);
                    ExpandAutoSizeBlock(result, nested, axes);
                }
                else
                {
                    var adjusted = innerElem;
                    adjusted.Position += contentOffset;
                    // The baseline is part of the geometry, so it has to move with the box: expanding a
                    // block shifts Position only, which would leave every inner baseline one padding away
                    // from its glyphs.
                    adjusted.BaselineY = OffsetBaseline(adjusted.BaselineY, contentOffset, axes);
                    // Nested content keeps the indices of the block's own layout context: its LineIndex
                    // refers to the block's SubLines and its cluster indices restart inside the block.
                    // Marking it lets consumers tell those context-local indices from document-global
                    // ones (the outer elements are the ones with no reason).
                    adjusted.Reason ??= "AutoSizeBlockContent";
                    result.Add(adjusted);
                }
            }
        }
    }

    /// <summary>
    /// Expand a fixed-size block by emitting its internal DrawElements with offset positions.
    /// </summary>
    private static void ExpandFixedSizeBlock(List<LayoutElement> result, in LayoutElement elem, DrawElement[] sourceElements)
    {
        var blockPosition = elem.Position;
        int blockStartIndex = elem.SourceIndex;
        var blockInfo = sourceElements[blockStartIndex].BlockInfo;

        // Compute horizontal alignment offset if ContentAlignment is set
        float alignOffset = 0f;
        if (blockInfo != null && blockInfo.ContentAlignment != TextAlignment.Left)
        {
            float contentRight = 0f;
            for (int j = blockStartIndex + 1; j < sourceElements.Length; j++)
            {
                ref var s = ref sourceElements[j];
                if (s.IsBlockEnd) break;
                var right = s.Position.X + s.Size.X;
                if (right > contentRight) contentRight = right;
            }

            float availableWidth = blockInfo.Size.X;
            float slack = availableWidth - contentRight;
            if (slack > 0)
            {
                alignOffset = blockInfo.ContentAlignment switch
                {
                    TextAlignment.Center => slack / 2f,
                    TextAlignment.Right => slack,
                    _ => 0f,
                };
            }
        }

        for (int i = blockStartIndex + 1; i < sourceElements.Length; i++)
        {
            ref var src = ref sourceElements[i];
            if (src.IsBlockEnd)
                break;

            var pos = blockPosition + src.Position;
            if (alignOffset > 0f)
                pos.X += alignOffset;

            result.Add(new LayoutElement
            {
                SourceIndex = i,
                Position = pos,
                // Fixed-size block content is authored with explicit positions and never measured by
                // the line breaker, so no baseline exists for it; the renderer falls back to the font
                // ascent exactly as before. P1 fills this from glyph metrics.
                BaselineY = float.NaN,
                Size = src.Size,
                LineIndex = elem.LineIndex,
                // Fixed-size block content is positioned by the producer, not measured here, so it was
                // never classified either.
                CharClass = CharacterClass.NonText,
                Reason = "FixedSizeBlockContent",
                Type = src.Type,
                Color = src.Color,
                Text = src.Text,
                Font = src.Font,
                FontSize = src.FontSize,
                IsBold = src.IsBold,
                IsItalic = src.IsItalic,
                IsStrikethrough = src.IsStrikethrough,
                IsUnderline = src.IsUnderline,
                IsSubscript = src.IsSubscript,
                IsSuperscript = src.IsSuperscript,
                RubyText = src.RubyText,
                SyntaxSpans = src.SyntaxSpans,
                Texture = src.Texture,
                LinkUrl = src.LinkUrl,
                ElementId = src.ElementId,
                ExtensionId = src.ExtensionId,
                ExtensionContent = src.ExtensionContent,
                ActionTag = src.ActionTag,
                ActionPayload = src.ActionPayload,
                CharacterIndex = src.CharacterIndex,
                CharacterCount = src.CharacterCount,
                TextEffectId = src.TextEffectId,
                CornerRadius = src.CornerRadius,
                BackgroundColor = src.BackgroundColor,
                BackgroundPadding = src.BackgroundPadding,
                BackgroundCornerRadius = src.BackgroundCornerRadius,
                BackgroundFillLine = src.BackgroundFillLine,
            });
        }
    }

    /// <summary>
    /// Reset the engine state for reuse.
    /// </summary>
    public void Reset()
    {
        _lines.Clear();
        _preparedContent = null;
        _streamSourceIndex = 0;
        ContentSize = Vector2.Zero;
        InvalidateBoundaries();
    }

    /// <summary>
    /// Resolve parameters per paragraph, or null when no paragraph names its own language.
    /// <para>
    /// Returning null in the common case is deliberate: a single-language document then runs exactly the code
    /// path it ran before paragraph languages existed, and the stages never allocate a per-paragraph list.
    /// </para>
    /// </summary>
    /// <param name="prepared">Prepared content, whose paragraphs carry their settings.</param>
    /// <returns>One resolution per paragraph, or null.</returns>
    private ResolvedTypography[]? ResolveParagraphTypography(PreparedContent prepared)
    {
        bool anyOverride = false;

        foreach (PreparedParagraph paragraph in prepared.Paragraphs)
        {
            if (LanguageOf(prepared, paragraph) is not null)
            {
                anyOverride = true;
                break;
            }
        }

        if (!anyOverride)
            return null;

        var resolved = new ResolvedTypography[prepared.Paragraphs.Count];

        for (int i = 0; i < prepared.Paragraphs.Count; i++)
        {
            // The registry merges the request's explicit overrides with the paragraph's own language - declared on
            // the element that opens it, or inferred from the paragraph's script when it named none.
            resolved[i] = LanguageProfileRegistry.Shared.Resolve(_settings, LanguageOf(prepared, prepared.Paragraphs[i]));
        }

        return resolved;
    }

    /// <summary>
    /// Language of one paragraph: what the element that opens it declares, or what its text says it is (the compile
    /// phase stamps that onto every segment, see <see cref="Segmenter.StampParagraphLanguages"/>).
    /// </summary>
    /// <param name="prepared">Prepared content the paragraph belongs to.</param>
    /// <param name="paragraph">Paragraph to look up.</param>
    /// <returns>The tag, or null when neither the paragraph nor its script names one.</returns>
    private static string? LanguageOf(PreparedContent prepared, in PreparedParagraph paragraph)
    {
        if (paragraph.Settings.LanguageTag is { Length: > 0 } declared)
            return declared;

        int first = paragraph.StartSegmentIndex;

        return first >= 0 && first < prepared.Segments.Count
            ? prepared.Segments[first].LanguageTag
            : null;
    }

    /// <summary>
    /// Signature of the languages the paragraphs name, so the boundary cache notices a paragraph that switched
    /// language even when the request's own parameters did not change.
    /// </summary>
    /// <param name="prepared">Prepared content.</param>
    /// <returns>A stable string, empty for a document without paragraph languages.</returns>
    private static string LanguageSignature(PreparedContent prepared)
    {
        var builder = new System.Text.StringBuilder();

        foreach (PreparedParagraph paragraph in prepared.Paragraphs)
            builder.Append(paragraph.Settings.LanguageTag ?? "-").Append('|');

        return builder.ToString();
    }

    /// <summary>
    /// Drop the cached compile-phase boundaries, so the next layout rebuilds them.
    /// </summary>
    private void InvalidateBoundaries()
    {
        _boundaries = [];
        _boundaryTypography = null;
        LastBoundaries = _boundaries;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _preparer.Dispose();
        GC.SuppressFinalize(this);
    }
}
