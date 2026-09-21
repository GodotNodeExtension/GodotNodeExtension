using System;
using System.Collections.Generic;
using Godot;
using GodotNodeExtension.Component.Typography.Core.Model;
using GodotNodeExtension.Component.Typography.Core.Unicode;
using GodotNodeExtension.Component.Typography.Languages;

namespace GodotNodeExtension.Component.Typography.Core;

/// <summary>
/// Line-level adjustment logic implementing step L2 of the typography pipeline.
/// After lines are broken (L1), this adjusts element positions within each line:
///   - Punctuation width compression (CLREQ §6.3.2)
///   - CJK-Latin inter-character spacing (CLREQ §6.3.3)
///   - Justify alignment with priority-based stretching/squeezing (CLREQ §6.2.2.3)
///   - Left/Center/Right alignment offsets
/// </summary>
public class LineAdjuster
{
    private readonly ResolvedTypography _typography;

    /// <summary>
    /// Create a LineAdjuster for one layout, reading the effective typography parameters it was given.
    /// </summary>
    /// <param name="typography">
    /// Effective typography parameters for this layout, already merged with the language profile.
    /// </param>
    public LineAdjuster(ResolvedTypography typography)
    {
        _typography = typography;
    }

    /// <summary>
    /// Adjust all lines in place according to alignment and spacing rules.
    /// </summary>
    /// <param name="lines">Lines produced by <see cref="LineBreaker"/>.</param>
    /// <param name="prepared">The prepared content (for paragraph settings lookup).</param>
    /// <param name="boundaries">
    /// Boundaries between the prepared clusters. Spacing and the stretch/squeeze priorities are read from
    /// them, so the rules live in the language profile rather than being re-derived here.
    /// </param>
    /// <param name="paragraphTypography">
    /// Parameters per paragraph when a paragraph named its own language; null keeps every paragraph on the
    /// request's parameters.
    /// </param>
    public void AdjustLines(
        List<LayoutLine> lines,
        PreparedContent prepared,
        IReadOnlyList<Boundary> boundaries,
        ResolvedTypography[]? paragraphTypography = null)
    {
        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (line.Elements.Count == 0) continue;

            // The paragraph's language decides the language-governed half of adjustment: the alignment it
            // prescribes by default and whether punctuation may be squeezed.
            ResolvedTypography typography = TypographyFor(paragraphTypography, line.ParagraphIndex);
            var paraSettings = ResolveParagraphSettings(prepared, line.ParagraphIndex);
            var alignment = paraSettings.Alignment ?? typography.Alignment;

            // Use the span L1 actually resolved. Re-deriving it from MaxWidth here (the previous
            // behaviour) ignored padding and paragraph indent on the left, ignored wrap regions
            // entirely, and made L1 and L2 disagree about where the line starts and how wide it is.
            // The first-line indent is part of that span: breaking already narrowed the line by it, so
            // positioning has to start from the same place or the indent is silently dropped.
            float lineLeft = line.LineLeft + line.LineIndent;
            float lineRight = line.LineRight;
            float availableWidth = lineRight - lineLeft;

            // Everything below moves or measures the line along its inline axis, so the writing mode has to be part
            // of the arithmetic: in vertical writing "how long is this line" is a sum of Y extents and a gap grows
            // downwards, and an adjustment that used X would resize the column instead of the line.
            LayoutAxes axes = new(typography.WritingMode, typography.MaxWidth);

            // Insert the spacing the compile phase decided for each boundary. The profile's switch is
            // already reflected in Boundary.BaseSpacing, so there is no second place to consult.
            InsertBoundarySpacing(line, boundaries, axes);

            // Compute actual content width
            float contentWidth = ComputeLineContentWidth(line, axes);

            bool isLastLineOfParagraph = IsLastLineOfParagraph(lines, i);

            // Start position of every interval of this line. Each interval is aligned on its own: an interval
            // is a line as far as alignment is concerned, because an exclusion that splits a line does not
            // shift what sits to the other side of it.
            float[] spanStarts = ComputeSpanStarts(line, alignment, lineLeft, axes);

            switch (alignment)
            {
                case TextAlignment.Justify:
                    // Justification distributes space between the gaps of a span, so it is applied to the
                    // ordinary single-interval line. A line split by an exclusion is left-aligned per interval
                    // instead (the same treatment the last line of a paragraph gets), which is a documented
                    // limitation rather than a silent one.
                    if (!isLastLineOfParagraph && line.Elements.Count > 0 && line.Spans.Length <= 1)
                        JustifyLine(line, contentWidth, availableWidth, lineLeft, boundaries, typography);
                    else
                        RepositionLineElements(line, spanStarts,
                            new LayoutAxes(typography.WritingMode, typography.MaxWidth));
                    break;

                case TextAlignment.Center:
                case TextAlignment.Right:
                case TextAlignment.Left:
                default:
                    RepositionLineElements(line, spanStarts,
                        new LayoutAxes(typography.WritingMode, typography.MaxWidth));
                    break;
            }

            TrimOpeningBracketAtLineHead(line, typography, alignment);

            // Text that contains anything right to left is put into visual order here: the algorithm reports a level
            // per character, and the levels decide both the order of the runs and the order inside each of them.
            // A paragraph that is entirely left to right never reaches this, so the common case pays nothing.
            if (LineNeedsBidi(line, typography))
                PlaceLineInVisualOrder(line, typography);

            ApplyGrid(line, typography);

            // Tabs have the last word: a stop is a position the author named, while the grid is a global rule, and
            // the two only conflict when both are switched on.
            ApplyTabStops(line, typography);

            // Last, because it appends an element: a line that broke inside a word ends with the hyphen the piece
            // reserved room for.
            PlaceHyphenAtLineEnd(line);
        }
    }

    /// <summary>
    /// Give a line that broke inside a word the hyphen it ends with.
    /// <para>
    /// The piece ending such a line reserved the hyphen's advance, so the geometry already accounts for it; what is
    /// missing is the mark itself, and that belongs here rather than in a renderer: a renderer that had to append it
    /// would have to know hyphenation exists, while the glyphs were shaped in the compile phase precisely so nobody
    /// else has to. The element appended is synthetic - an empty source range and a reason - which is what keeps it
    /// out of the text coverage invariants.
    /// </para>
    /// </summary>
    /// <param name="line">The line that was just positioned.</param>
    private static void PlaceHyphenAtLineEnd(LayoutLine line)
    {
        // The piece that was broken may be followed by the space the line breaker kept on this line, so the search
        // walks back past trailing spaces: what matters is the last *text* the line carries.
        int index = line.Elements.Count - 1;

        while (index >= 0 && line.Elements[index].CharClass == CharacterClass.Space)
            index--;

        if (index < 0)
            return;

        LayoutElement last = line.Elements[index];

        if (last.HyphenRun is not { } hyphen || hyphen.Glyphs.Length == 0)
            return;

        // The hyphen sits at the end of the space the piece reserved for it.
        float x = last.Position.X + last.Size.X - hyphen.Width;

        // Inserted right after the piece it belongs to, so a trailing space stays where the line breaker put it.
        line.Elements.Insert(index + 1, new LayoutElement
        {
            Type = DrawElement.ElementType.Text,
            Text = "-",
            Font = last.Font,
            FontSize = last.FontSize,
            Color = last.Color,
            Position = new Vector2(x, last.Position.Y),
            BaselineY = last.BaselineY,
            Size = new Vector2(hyphen.Width, last.Size.Y),
            Direction = last.Direction,
            LineIndex = last.LineIndex,
            SpanIndex = last.SpanIndex,
            SourceIndex = last.SourceIndex,
            SourceRange = new TextRange(last.SourceRange.End, last.SourceRange.End),
            ClusterStart = -1,
            ClusterEnd = -1,
            CharacterIndex = last.CharacterIndex + last.CharacterCount,
            CharacterCount = 0,
            GlyphRun = hyphen with { Origin = new Vector2(x, last.BaselineY) },
            Reason = "HyphenationBreak",
        });
    }

    /// <summary>
    /// Snap the elements of a line to the grid, when the request asked for one.
    /// <para>
    /// This runs last, because it is the more specific instruction: alignment and justification decide how the line
    /// fills its interval, and a grid decides where things are allowed to stand. Snapping happens forward — an
    /// element never moves left of where the layout put it — and it is done per element, which is the granularity
    /// the layout has (one element per cluster; a Latin word is one element).
    /// </para>
    /// </summary>
    /// <param name="line">Line whose elements are snapped.</param>
    /// <param name="typography">Effective typography of the line's paragraph.</param>
    private static void ApplyGrid(LayoutLine line, ResolvedTypography typography)
    {
        if (typography.GridStep is not { } step || step <= 0f || line.Elements.Count == 0)
            return;

        LineSpan[] spans = line.Spans.Length > 0 ? line.Spans : [new LineSpan(line.LineLeft, line.LineRight)];
        float origin = spans[0].Left + typography.GridOrigin;
        float x = spans[0].Left + line.LineIndent;

        for (int i = 0; i < line.Elements.Count; i++)
        {
            LayoutElement element = line.Elements[i];

            // The next grid line at or after where the element currently starts.
            float target = origin + (MathF.Ceiling((x - origin) / step) * step);

            element = element with
            {
                Position = new Vector2(target, element.Position.Y),
                GlyphRun = element.GlyphRun is { } run
                    ? run with { Origin = new Vector2(target, run.Origin.Y) }
                    : null,
            };

            line.Elements[i] = element;
            x = target + element.Size.X;
        }

        line.Elements.Sort(static (a, b) => a.Position.X.CompareTo(b.Position.X));
    }

    /// <summary>
    /// Send the content after each tab to its stop, and line it up there.
    /// <para>
    /// A tab occupies no room of its own: it moves the pen to the next stop, and the element that follows is placed
    /// according to how that stop is aligned — its left edge, its centre or its right edge at the stop, or, for a
    /// number column, its decimal separator. The stops are the author's; where none reaches far enough, the automatic
    /// stops (a fixed number of ems from the line's start) take over, and if those are switched off the tab does
    /// nothing, which is what a tab past the last stop should do.
    /// </para>
    /// </summary>
    /// <param name="line">Line whose tabs are applied.</param>
    /// <param name="typography">Effective typography of the line's paragraph.</param>
    private static void ApplyTabStops(LayoutLine line, ResolvedTypography typography)
    {
        if (line.Elements.Count == 0 || !HasTab(line))
            return;

        LineSpan[] spans = line.Spans.Length > 0 ? line.Spans : [new LineSpan(line.LineLeft, line.LineRight)];
        float em = FontSizeOf(line);
        float fallback = typography.DefaultTabStopEm is { } value && value > 0f ? value * em : 0f;
        float pen = spans[0].Left + line.LineIndent;

        for (int i = 0; i < line.Elements.Count; i++)
        {
            LayoutElement element = line.Elements[i];

            if (element.CharClass != CharacterClass.Tab)
            {
                line.Elements[i] = MoveTo(element, pen);
                pen += element.Size.X;
                continue;
            }

            (float stop, TabAlignment alignment) = NextStop(typography.TabStops, spans[0].Left, pen, fallback);

            if (stop <= pen || i + 1 >= line.Elements.Count)
                continue;

            LayoutElement following = line.Elements[i + 1];

            // The alignment decides how much of the following element sits before the stop.
            float before = alignment switch
            {
                TabAlignment.Right => following.Size.X,
                TabAlignment.Center => following.Size.X * 0.5f,
                TabAlignment.DecimalPoint => WidthBeforeSeparator(following),
                _ => 0f,
            };

            // The tab's own element records the gap up to the stop and the character that fills it: it is the only
            // place the consumer can learn both without shaping anything itself.
            TabStop hit = StopAt(typography.TabStops, stop, alignment);
            float gap = stop - pen;

            line.Elements[i] = MoveTo(element, pen) with
            {
                Size = new Vector2(gap, element.Size.Y),
                Text = hit.Leader is { } leader ? leader.ToString() : string.Empty,
            };

            pen = stop - before;
        }
    }

    /// <summary>
    /// The stop a position and alignment belong to, so the leader the author asked for can be attached to it.
    /// </summary>
    /// <param name="stops">The author's stops.</param>
    /// <param name="position">The stop's position that was used.</param>
    /// <param name="alignment">The alignment that was used.</param>
    /// <returns>The matching stop, or a bare one when the stop was automatic.</returns>
    private static TabStop StopAt(IReadOnlyList<TabStop> stops, float position, TabAlignment alignment)
    {
        foreach (TabStop stop in stops)
        {
            if (MathF.Abs(stop.Position - position) < 0.01f && stop.Alignment == alignment)
                return stop;
        }

        return new TabStop(position, alignment);
    }

    /// <summary>Whether the line carries a tab at all, which is the cheap gate for the whole pass.</summary>
    /// <param name="line">Line to test.</param>
    /// <returns>True when one of its elements is a tab.</returns>
    private static bool HasTab(LayoutLine line)
    {
        foreach (LayoutElement element in line.Elements)
        {
            if (element.CharClass == CharacterClass.Tab)
                return true;
        }

        return false;
    }

    /// <summary>The font size the line is set in, used to turn the automatic stop spacing from ems into pixels.</summary>
    /// <param name="line">Line to inspect.</param>
    /// <returns>The first text element's font size, or 16 when the line has none.</returns>
    private static float FontSizeOf(LayoutLine line)
    {
        foreach (LayoutElement element in line.Elements)
        {
            if (element.Type == DrawElement.ElementType.Text && element.FontSize > 0)
                return element.FontSize;
        }

        return 16f;
    }

    /// <summary>
    /// The stop a tab points at: the first one past the pen, or the next automatic stop when none is and the
    /// automatic stops are on.
    /// </summary>
    /// <param name="stops">The author's stops, ascending.</param>
    /// <param name="lineStart">Start of the line, which the automatic stops are measured from.</param>
    /// <param name="pen">Where the pen is.</param>
    /// <param name="fallback">Spacing of the automatic stops in pixels; 0 turns them off.</param>
    /// <returns>The stop's position and how content lines up at it.</returns>
    private static (float Stop, TabAlignment Alignment) NextStop(
        IReadOnlyList<TabStop> stops,
        float lineStart,
        float pen,
        float fallback)
    {
        foreach (TabStop stop in stops)
        {
            if (stop.Position > pen + 0.01f)
                return (stop.Position, stop.Alignment);
        }

        if (fallback <= 0f)
            return (pen, TabAlignment.Left);

        float steps = MathF.Floor((pen - lineStart) / fallback) + 1f;
        return (lineStart + (steps * fallback), TabAlignment.Left);
    }

    /// <summary>
    /// How much of an element sits before its decimal separator, which is where a <see cref="TabAlignment.DecimalPoint"/>
    /// stop puts the separator. Falls back to nothing when the element has no separator.
    /// </summary>
    /// <param name="element">Element to measure.</param>
    /// <returns>Approximate width before the separator, in pixels.</returns>
    private static float WidthBeforeSeparator(LayoutElement element)
    {
        string? text = element.Text;

        if (string.IsNullOrEmpty(text))
            return 0f;

        int separator = text.IndexOfAny(['.', ',', '．', '，']);

        if (separator <= 0)
            return 0f;

        // Proportional share of the element's width: the layout measures the whole element, not its parts.
        return element.Size.X * separator / text.Length;
    }

    /// <summary>Move an element (and the glyphs it draws) to a new left edge.</summary>
    /// <param name="element">Element to move.</param>
    /// <param name="x">New left edge.</param>
    /// <returns>The element at its new position.</returns>
    private static LayoutElement MoveTo(LayoutElement element, float x) => element with
    {
        Position = new Vector2(x, element.Position.Y),
        GlyphRun = element.GlyphRun is { } run ? run with { Origin = new Vector2(x, run.Origin.Y) } : null,
    };

    /// <summary>
    /// Whether a line needs the bidi pass: it does as soon as one of its elements carries right-to-left text, and a
    /// paragraph declared right to left needs it even for text that would be resolved the same way anyway.
    /// </summary>
    /// <param name="line">Line to test.</param>
    /// <param name="typography">Effective typography of the line's paragraph.</param>
    /// <returns>True when the line has to be put into visual order.</returns>
    private static bool LineNeedsBidi(LayoutLine line, ResolvedTypography typography)
    {
        if (typography.Direction == TextDirection.RightToLeft)
            return true;

        foreach (LayoutElement element in line.Elements)
        {
            if (element.Type == DrawElement.ElementType.Text && BidiResolver.HasRightToLeft(element.Text ?? string.Empty))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Put a line into visual order: the runs the bidi algorithm reported are laid out in display order, and the
    /// line hugs the edge its paragraph starts at.
    /// <para>
    /// The algorithm is asked once per line, over the line's own text: what a line needs is the order of its own
    /// characters, and the paragraph's base direction is what decides the edge. Each element is treated as one unit
    /// whose direction is its first character's level — which is what the levels mean for a run — and the same
    /// reversal the algorithm uses (from the highest level down to the lowest odd one) is applied to those units.
    /// </para>
    /// <para>
    /// The glyph runs move with their boxes: they are drawn from their own origin, so a box that moved without its
    /// run would draw its text where the line was before. What this does <em>not</em> do yet is shape each run in its
    /// own direction, so a left-to-right run inside a right-to-left paragraph is still shaped the paragraph's way.
    /// </para>
    /// </summary>
    /// <param name="line">Line to reorder.</param>
    /// <param name="typography">Effective typography of the line's paragraph.</param>
    private static void PlaceLineInVisualOrder(LayoutLine line, ResolvedTypography typography)
    {
        if (line.Elements.Count == 0)
            return;

        var text = new System.Text.StringBuilder();
        var elementStarts = new int[line.Elements.Count];

        for (int i = 0; i < line.Elements.Count; i++)
        {
            elementStarts[i] = text.Length;
            text.Append(line.Elements[i].Text ?? string.Empty);
        }

        if (text.Length == 0)
            return;

        // Line levels: rule L1 has to be applied before the levels decide anything, or a trailing space inside a
        // right-to-left line would be drawn as part of the run instead of at the line's edge.
        byte[] levels = BidiResolver.LineLevels(text.ToString(), typography.Direction);
        var elementLevels = new byte[line.Elements.Count];

        for (int i = 0; i < elementLevels.Length; i++)
            elementLevels[i] = levels[Math.Clamp(elementStarts[i], 0, levels.Length - 1)];

        int[] visual = BidiResolver.VisualOrder(elementLevels, 0, elementLevels.Length);

        LineSpan[] spans = line.Spans.Length > 0 ? line.Spans : [new LineSpan(line.LineLeft, line.LineRight)];
        LineSpan span = spans[0];

        float total = 0f;

        foreach (LayoutElement element in line.Elements)
            total += element.Size.X;

        // The line starts at the edge its paragraph starts at, and the first-line indent comes off that edge.
        float x = typography.Direction == TextDirection.RightToLeft
            ? span.Right - line.LineIndent - total
            : span.Left + line.LineIndent;

        foreach (int index in visual)
        {
            LayoutElement element = line.Elements[index];

            element = element with
            {
                Position = new Vector2(x, element.Position.Y),
                GlyphRun = element.GlyphRun is { } run
                    ? run with { Origin = new Vector2(x, run.Origin.Y) }
                    : null,
            };

            line.Elements[index] = element;
            x += element.Size.X;
        }

        // Display order: the element order is what a reader of the line sees, left to right.
        line.Elements.Sort(static (a, b) => a.Position.X.CompareTo(b.Position.X));
    }

    /// <summary>
    /// Trim the empty leading half of an opening bracket that starts a line, so the bracket's ink sits flush with
    /// the line's edge and the line gains half an em.
    /// <para>
    /// Both conventions describe it (jlreq §3.1.5 calls it 折り返し天付き, clreq §6.3.2.3 allows the leading half
    /// of a line-head bracket to be trimmed) and both say the same thing about where it applies: the bracket's
    /// ink is in the trailing half of its em box, so the leading half is empty space the line does not need. The
    /// glyph does not move with its box: the box shrinks and the renderer is told to draw the glyph half an em
    /// further back, which is what makes the ink land on the edge instead of half an em after it.
    /// </para>
    /// </summary>
    /// <param name="line">Line that has already been positioned.</param>
    /// <param name="typography">Effective typography of the line's paragraph.</param>
    /// <param name="alignment">Alignment in effect: only a line that starts at the content edge can be trimmed.</param>
    private static void TrimOpeningBracketAtLineHead(
        LayoutLine line,
        ResolvedTypography typography,
        TextAlignment alignment)
    {
        if (!typography.HalfWidthOpeningBracketAtLineHead || line.Elements.Count == 0)
            return;

        // Centred and right-aligned lines moved their interval away from the content edge; pulling the bracket
        // left from there would put it outside the line it belongs to.
        if (alignment is TextAlignment.Center or TextAlignment.Right)
            return;

        LayoutAxes axes = new(typography.WritingMode, typography.MaxWidth);
        LayoutElement first = line.Elements[0];

        if (first.SpanIndex != 0 || first.CharClass != CharacterClass.PunctuationOpen)
            return;

        float fontSize = first.FontSize > 0 ? first.FontSize : 16f;
        float half = fontSize * 0.5f;
        float inlineExtent = axes.Inline(first.Size);

        // Only a full-width bracket has an empty leading half to trim; a narrower one is already its own form.
        if (inlineExtent < half * 2f - 0.01f)
            return;

        // The empty half is the head of the bracket *along the line*, so the box loses it on the inline axis - which
        // in a column is the box' Y, not its X - and the glyph is drawn half an em further back along that axis.
        first = first with
        {
            Size = axes.Size(inlineExtent - half, axes.BlockExtent(first.Size)),
            GlyphRun = first.GlyphRun is { } glyphs
                ? glyphs with { Origin = glyphs.Origin - (axes.InlineUnit * half) }
                : null,
            Reason = "OpeningBracketHalfWidth",
        };

        line.Elements[0] = first;

        // The line is now half an em shorter: everything after the bracket moves back with it.
        RepositionLineElements(line, axes.Inline(first.Position), axes);

        // And the bracket itself starts where the line does.
        first = line.Elements[0];
        line.Elements[0] = first with { Position = axes.Point(line.LineLeft, axes.Block(first.Position)) };
    }

    /// <summary>
    /// Justify a line by distributing extra space (or compressing) using CLREQ priority rules.
    /// </summary>
    private static void JustifyLine(
        LayoutLine line,
        float contentWidth,
        float availableWidth,
        float lineLeft,
        IReadOnlyList<Boundary> boundaries,
        ResolvedTypography typography)
    {
        float slack = availableWidth - contentWidth;

        if (MathF.Abs(slack) < 0.5f)
        {
            // Close enough to fill — just align left
            AlignLeft(line, lineLeft, new LayoutAxes(typography.WritingMode, typography.MaxWidth));
            return;
        }

        if (slack > 0)
        {
            // Need to stretch: distribute slack across stretchable gaps
            StretchLine(line, slack, lineLeft, boundaries, new LayoutAxes(typography.WritingMode, typography.MaxWidth));
        }
        else
        {
            // Need to squeeze: compress punctuation and spacing
            SqueezeLine(line, -slack, lineLeft, typography, boundaries);
        }
    }

    /// <summary>
    /// Stretch a line by distributing extra space using priority order:
    /// 1. Latin word spaces
    /// 2. CJK-Latin gaps (already inserted)
    /// 3. Ideograph inter-character gaps
    /// </summary>
    // The axes say which axis a stretch grows along and which one it leaves alone; the default is horizontal
    // writing, where the inline axis is X.
    private static void StretchLine(LayoutLine line, float slack, float lineLeft,
        IReadOnlyList<Boundary> boundaries, LayoutAxes axes = default)
    {
        // Count stretchable gaps by type
        int latinSpaceCount = 0;
        int cjkLatinGapCount = 0;
        int ideographGapCount = 0;

        for (int i = 0; i < line.Elements.Count - 1; i++)
        {
            var current = line.Elements[i];
            var next = line.Elements[i + 1];

            if (IsLatinSpace(current))
                latinSpaceCount++;
            else if (IsCjkLatinBoundary(boundaries, current, next))
                cjkLatinGapCount++;
            else if (IsCjkIdeographBoundary(boundaries, current, next))
                ideographGapCount++;
        }

        // Priority 1: distribute across Latin word spaces
        float remaining = slack;

        if (latinSpaceCount > 0 && remaining > 0)
        {
            float perGap = remaining / latinSpaceCount;
            remaining = DistributeStretch(line, perGap, IsLatinSpaceElement, axes);
        }

        // Priority 2: CJK-Latin gaps
        if (cjkLatinGapCount > 0 && remaining > 0.5f)
        {
            float perGap = remaining / cjkLatinGapCount;

            // The gap has an upper bound: clreq §6.3.3 and jlreq §3.2.6 state the CJK/Latin spacing as a range
            // (1/4 em in a range of 1/8 to 1/2), and the boundary carries it. A justified line that would need
            // more than the range allows leaves the remainder to the next priority instead of widening the gap
            // past what the convention permits.
            float room = StretchRoomAt(boundaries, line, (a, b) => IsCjkLatinBoundary(boundaries, a, b));

            if (room > 0f && perGap * cjkLatinGapCount > room)
                perGap = room / cjkLatinGapCount;

            remaining = DistributeStretchAtBoundary(line, perGap,
                (a, b) => IsCjkLatinBoundary(boundaries, a, b), axes);
        }

        // Priority 3: Ideograph inter-character gaps
        if (ideographGapCount > 0 && remaining > 0.5f)
        {
            float perGap = remaining / ideographGapCount;
            DistributeStretchAtBoundary(line, perGap,
                (a, b) => IsCjkIdeographBoundary(boundaries, a, b), axes);
        }

        // Reposition all elements from lineLeft, along the same axes the stretch above grew them by.
        RepositionLineElements(line, lineLeft, axes);
    }

    /// <summary>
    /// Squeeze a line by compressing punctuation and spacing to fit.
    /// Priority order per CLREQ §6.2.2.3:
    /// 1. End-of-line punctuation → half width
    /// 2. Latin word spaces
    /// 3. CJK-Latin spacing
    /// 4. Other punctuation
    /// </summary>
    /// <summary>
    /// Compress the CJK/Latin gaps of a line, each down to the lower bound its boundary carries.
    /// </summary>
    /// <param name="line">The line being squeezed.</param>
    /// <param name="boundaries">Compile-phase boundaries.</param>
    /// <param name="remaining">How much still has to be found.</param>
    /// <param name="axes">Orientation of the geometry: the gap is taken off the inline axis.</param>
    /// <returns>How much the gaps gave up.</returns>
    private static float SqueezeScriptGaps(
        LayoutLine line,
        IReadOnlyList<Boundary> boundaries,
        float remaining,
        LayoutAxes axes = default)
    {
        float squeezed = 0f;

        for (int i = 0; i < line.Elements.Count - 1 && remaining - squeezed > 0.5f; i++)
        {
            if (BoundaryBetween(boundaries, line.Elements[i], line.Elements[i + 1]) is not { } boundary)
                continue;

            float slack = boundary.BaseSpacing - boundary.MinSpacing;

            if (slack <= 0f)
                continue;

            float take = MathF.Min(slack, remaining - squeezed);
            var element = line.Elements[i];
            element.Size = axes.Size(axes.Inline(element.Size) - take, axes.BlockExtent(element.Size));
            line.Elements[i] = element;
            squeezed += take;
        }

        return squeezed;
    }

    /// <summary>
    /// Squeeze a line until it fits, taking room off the elements the convention allows to give it up.
    /// <para>
    /// Everything here shortens the line, so every write goes through the content's axes: a mark that is trimmed to
    /// half width gives up half of its advance along the line (its <em>inline</em> extent), and in vertical writing
    /// that is the element's room down the column - trimming X there would thin the column instead, and the mark
    /// would be drawn half a column off to the side of the characters it belongs to.
    /// </para>
    /// </summary>
    /// <param name="line">The line to squeeze.</param>
    /// <param name="overshoot">How much room the line has to give up.</param>
    /// <param name="lineLeft">Inline coordinate of the interval's start.</param>
    /// <param name="typography">Effective typography of the line's paragraph.</param>
    /// <param name="boundaries">Compile-phase boundaries.</param>
    private static void SqueezeLine(
        LayoutLine line,
        float overshoot,
        float lineLeft,
        ResolvedTypography typography,
        IReadOnlyList<Boundary> boundaries)
    {
        // Squeezing compresses punctuation and spaces that are already on the line; which elements those
        // are is answered by the element's own character class, which the prepare phase assigned.
        float remaining = overshoot;
        var axes = new LayoutAxes(typography.WritingMode, typography.MaxWidth);

        // Punctuation compression is a CJK behaviour, so the paragraph's language decides whether it applies
        // and the request may still switch it off explicitly (that value reaches here through the merge).
        bool compressPunctuation = typography.EnablePunctuationCompression;

        // Chinese trims the trailing half em of a full-width mark that ends a line, Japanese keeps it (the half
        // em after a stop is part of the mark and must not be compressed, jlreq §3.1.9), and Korean has already
        // chosen a narrower character. So the policy, not the engine, decides whether this stage may trim.
        bool mayTrimLineEndPunctuation =
            compressPunctuation && typography.LineEndPunctuation == LineEndPunctuationPolicy.HalfWidthOnOverflow;

        // Priority 1: Compress end-of-line punctuation to half width
        if (mayTrimLineEndPunctuation && line.Elements.Count > 0 && remaining > 0.5f)
        {
            var lastIdx = line.Elements.Count - 1;
            var last = line.Elements[lastIdx];
            if (IsPunctuation(last))
            {
                float inline = axes.Inline(last.Size);
                float compress = MathF.Min(inline * 0.5f, remaining);
                last.Size = axes.Size(inline - compress, axes.BlockExtent(last.Size));
                line.Elements[lastIdx] = last;
                remaining -= compress;
            }
        }

        // Priority 2: Compress Latin word spaces
        if (remaining > 0.5f)
        {
            int spaceCount = CountElements(line, IsLatinSpaceElement);
            if (spaceCount > 0)
            {
                float perSpace = MathF.Min(remaining / spaceCount, 4f); // max 4px compression per space
                remaining -= CompressElements(line, IsLatinSpaceElement, perSpace, axes);
            }
        }

        // Priority 3: the CJK/Latin gap, down to the lower bound its convention states (clreq §6.3.3, jlreq
        // §3.2.6). The boundary carries that bound, so the squeeze cannot go below what the language allows.
        if (remaining > 0.5f)
            remaining -= SqueezeScriptGaps(line, boundaries, remaining, axes);

        // Priority 4: Compress all punctuation proportionally
        if (compressPunctuation && remaining > 0.5f)
        {
            int punctCount = CountElements(line, IsPunctuationElement);
            if (punctCount > 0)
            {
                float perPunct = remaining / punctCount;
                CompressElements(line, IsPunctuationElement, perPunct, axes);
            }
        }

        // Reposition all elements from lineLeft
        RepositionLineElements(line, lineLeft, axes);
    }

    /// <summary>
    /// Total room the matching gaps of a line may still take, summed from the boundaries' upper bounds. A
    /// boundary whose base and maximum spacing are equal is not stretchable at all and contributes nothing.
    /// </summary>
    /// <param name="boundaries">Compile-phase boundaries.</param>
    /// <param name="line">The line being stretched.</param>
    /// <param name="matches">Predicate that says which element pairs are eligible.</param>
    /// <returns>The room in pixels; 0 when no pair is eligible or nothing may grow.</returns>
    private static float StretchRoomAt(
        IReadOnlyList<Boundary> boundaries,
        LayoutLine line,
        Func<LayoutElement, LayoutElement, bool> matches)
    {
        float room = 0f;

        for (int i = 0; i < line.Elements.Count - 1; i++)
        {
            LayoutElement left = line.Elements[i];
            LayoutElement right = line.Elements[i + 1];

            if (!matches(left, right))
                continue;

            if (BoundaryBetween(boundaries, left, right) is not { } boundary)
                continue;

            if (boundary.MaxSpacing > boundary.BaseSpacing)
                room += boundary.MaxSpacing - boundary.BaseSpacing;
        }

        return room;
    }

    /// <summary>
    /// Reposition all elements sequentially from lineLeft, using their current sizes.
    /// </summary>
    /// <param name="line">The line to reposition.</param>
    /// <param name="lineLeft">Inline coordinate of the interval's start.</param>
    /// <param name="axes">
    /// Orientation of the geometry: the inline coordinate the pen walks along, and the block coordinate the line
    /// sits at. The default is horizontal writing, where both coincide with X and Y; a caller that knows the writing
    /// mode passes the content's axes so the adjustment moves along the inline axis instead.
    /// </param>
    private static void RepositionLineElements(LayoutLine line, float lineLeft, LayoutAxes axes = default)
    {
        float x = lineLeft;
        for (int i = 0; i < line.Elements.Count; i++)
        {
            var elem = line.Elements[i];

            // The laid-out glyphs hang off the element's box, and the renderer draws them from their own
            // origin: an adjustment that moves the box without moving them would draw the text where the
            // line was *before* alignment, which is invisible in a dump but wrong on screen.
            elem = elem.GlyphRun is { } glyphs
                ? elem with
                {
                    Position = axes.Point(x, axes.Block(elem.Position)),
                    GlyphRun = glyphs with { Origin = axes.Point(x, axes.Block(glyphs.Origin)) },
                }
                : elem with { Position = axes.Point(x, axes.Block(elem.Position)) };

            line.Elements[i] = elem;
            x += axes.Inline(elem.Size);
        }
    }

    /// <summary>
    /// Distribute stretch amount across elements matching the predicate by widening them along the line.
    /// Returns remaining undistributed slack.
    /// </summary>
    private static float DistributeStretch(LayoutLine line, float perGap,
        Func<LayoutElement, bool> predicate, LayoutAxes axes = default)
    {
        for (int i = 0; i < line.Elements.Count; i++)
        {
            var elem = line.Elements[i];
            if (predicate(elem))
            {
                // A stretch grows the box along the inline axis only: the block extent (the line's thickness) is
                // not the gaps' business, and in vertical writing the inline axis is not X. The block extent comes
                // from <see cref="LayoutAxes.BlockExtent"/>, not from <see cref="LayoutAxes.Block"/> - the latter is
                // a coordinate measured from the block-start edge, which in vertical writing is a distance from the
                // other side of the content box and would turn a stretched element into a box as wide as the page.
                elem.Size = axes.Size(axes.Inline(elem.Size) + perGap, axes.BlockExtent(elem.Size));
                line.Elements[i] = elem;
            }
        }
        return 0f; // All distributed (simplified)
    }

    /// <summary>
    /// Distribute stretch at boundaries between pairs of elements matching the predicate.
    /// Extra space is added to the first element of each matching pair.
    /// Returns remaining undistributed slack.
    /// </summary>
    /// <param name="line">The line being stretched.</param>
    /// <param name="perGap">How much each matching boundary grows by.</param>
    /// <param name="predicate">Which element pairs are eligible.</param>
    /// <param name="axes">Orientation of the geometry: the stretch grows along the inline axis; see
    /// <see cref="DistributeStretch"/>.</param>
    private static float DistributeStretchAtBoundary(LayoutLine line, float perGap,
        Func<LayoutElement, LayoutElement, bool> predicate, LayoutAxes axes = default)
    {
        for (int i = 0; i < line.Elements.Count - 1; i++)
        {
            if (predicate(line.Elements[i], line.Elements[i + 1]))
            {
                var elem = line.Elements[i];
                elem.Size = axes.Size(axes.Inline(elem.Size) + perGap, axes.BlockExtent(elem.Size));
                line.Elements[i] = elem;
            }
        }
        return 0f;
    }

    /// <summary>
    /// Compress elements matching the predicate by the specified amount per element, along the line.
    /// Returns total amount successfully compressed.
    /// </summary>
    /// <param name="line">The line being squeezed.</param>
    /// <param name="predicate">Which elements may give up room.</param>
    /// <param name="perElement">How much each of them gives up.</param>
    /// <param name="axes">Orientation of the geometry: the line gets shorter along its inline axis, so a
    /// compression in vertical writing takes room off the element's advance down the column rather than off the
    /// column's thickness.</param>
    /// <returns>How much room the elements actually gave up.</returns>
    private static float CompressElements(LayoutLine line,
        Func<LayoutElement, bool> predicate, float perElement, LayoutAxes axes = default)
    {
        float total = 0f;
        for (int i = 0; i < line.Elements.Count; i++)
        {
            var elem = line.Elements[i];
            if (predicate(elem))
            {
                float inline = axes.Inline(elem.Size);
                float compress = MathF.Min(perElement, inline * 0.5f);
                elem.Size = axes.Size(inline - compress, axes.BlockExtent(elem.Size));
                line.Elements[i] = elem;
                total += compress;
            }
        }
        return total;
    }

    private static int CountElements(LayoutLine line, Func<LayoutElement, bool> predicate)
    {
        int count = 0;
        for (int i = 0; i < line.Elements.Count; i++)
            if (predicate(line.Elements[i])) count++;
        return count;
    }

    /// <summary>
    /// Insert the spacing each boundary prescribes, on the trailing edge of its owner.
    /// <para>
    /// The size used to be a single number per line (the profile's em factor times the line's first font
    /// size); it now comes from the boundary, which was measured from the left cluster it separates. For
    /// uniform text the two agree exactly, and for mixed font sizes the boundary is the more defensible
    /// answer: a gap belongs to the pair, not to the line's first element.
    /// </para>
    /// </summary>
    /// <param name="line">The line to adjust.</param>
    /// <param name="boundaries">Compile-phase boundaries.</param>
    /// <param name="axes">
    /// Orientation of the geometry: the gap sits on the trailing edge of the element the boundary belongs to, which
    /// is the inline axis. In vertical writing that is the element's advance down the column, so a gap added to X
    /// would thicken the column instead of separating the two runs.
    /// </param>
    private static void InsertBoundarySpacing(LayoutLine line, IReadOnlyList<Boundary> boundaries, LayoutAxes axes)
    {
        if (line.Elements.Count < 2) return;

        for (int i = 0; i < line.Elements.Count - 1; i++)
        {
            if (BoundaryBetween(boundaries, line.Elements[i], line.Elements[i + 1]) is not { } boundary)
                continue;

            if (boundary.BaseSpacing <= 0f)
                continue;

            var current = line.Elements[i];
            current.Size = axes.Size(axes.Inline(current.Size) + boundary.BaseSpacing,
                axes.BlockExtent(current.Size));
            line.Elements[i] = current;
        }
    }

    // The axes say which axis the line is aligned along; the default keeps horizontal writing (see the
    // repositioning overloads below).
    private static void AlignLeft(LayoutLine line, float lineLeft, LayoutAxes axes = default)
    {
        RepositionLineElements(line, lineLeft, axes);
    }

    /// <summary>
    /// Start position of every interval of a line.
    /// <para>
    /// The first interval starts at the line's content edge (which already includes the first-line indent);
    /// the others start at their own left edge. Centring and right alignment shift each interval within its
    /// own bounds, and left alignment (and justification, which moves the gaps afterwards) leaves every
    /// interval at its left edge.
    /// </para>
    /// </summary>
    /// <param name="line">The line being adjusted.</param>
    /// <param name="alignment">Alignment in effect for this line.</param>
    /// <param name="lineLeft">Content edge of the first interval, indents included.</param>
    /// <param name="axes">Orientation of the geometry: how much room the content takes is a sum of inline
    /// extents, which in vertical writing are the elements' advances down their column rather than their
    /// thickness.</param>
    /// <returns>One start position per interval.</returns>
    private static float[] ComputeSpanStarts(LayoutLine line, TextAlignment alignment, float lineLeft,
        LayoutAxes axes)
    {
        LineSpan[] spans = line.Spans.Length > 0 ? line.Spans : [new LineSpan(line.LineLeft, line.LineRight)];
        var starts = new float[spans.Length];

        starts[0] = lineLeft;

        for (int i = 1; i < spans.Length; i++)
            starts[i] = spans[i].Left;

        if (alignment is TextAlignment.Left or TextAlignment.Justify)
            return starts;

        for (int i = 0; i < spans.Length; i++)
        {
            float right = i == 0 ? spans[0].Right : spans[i].Right;
            float spanWidth = right - starts[i];
            float content = 0f;

            foreach (var element in line.Elements)
            {
                if (element.SpanIndex == i)
                    content += axes.Inline(element.Size);
            }

            float slack = spanWidth - content;

            if (slack <= 0f)
                continue;

            starts[i] += alignment is TextAlignment.Center ? slack * 0.5f : slack;
        }

        return starts;
    }

    /// <summary>
    /// Reposition the elements of a line, each in the interval it was placed in. Elements keep their order,
    /// and an interval is laid out from its own start position, which keeps text that the breaker put in a
    /// second interval from being pulled back into the first one.
    /// </summary>
    /// <param name="line">The line to reposition.</param>
    /// <param name="spanStarts">Start position per interval.</param>
    /// <param name="axes">
    /// Orientation of the geometry; see the single-interval overload. The default keeps horizontal writing.
    /// </param>
    private static void RepositionLineElements(LayoutLine line, float[] spanStarts, LayoutAxes axes = default)
    {
        int currentSpan = -1;
        float x = 0f;

        for (int i = 0; i < line.Elements.Count; i++)
        {
            var elem = line.Elements[i];
            int span = spanStarts.Length > 1 ? Math.Clamp(elem.SpanIndex, 0, spanStarts.Length - 1) : 0;

            if (span != currentSpan)
            {
                currentSpan = span;
                x = spanStarts[span];
            }

            // The laid-out glyphs hang off the element's box and the renderer draws them from their own origin, so
            // an adjustment that moves the box has to move them as well - the single-interval overload above does
            // exactly this. A golden dump cannot see the difference (it prints the box, not the origin), which is how
            // this half of the fix went missing: multi-interval lines drew their text where the line was before the
            // adjustment.
            elem = elem.GlyphRun is { } glyphs
                ? elem with
                {
                    Position = axes.Point(x, axes.Block(elem.Position)),
                    GlyphRun = glyphs with { Origin = axes.Point(x, axes.Block(glyphs.Origin)) },
                }
                : elem with { Position = axes.Point(x, axes.Block(elem.Position)) };

            line.Elements[i] = elem;
            x += axes.Inline(elem.Size);
        }
    }

    /// <summary>
    /// Room the line's content takes along its inline axis: the sum of the elements' inline extents.
    /// </summary>
    /// <param name="line">The line to measure.</param>
    /// <param name="axes">Orientation of the geometry; see <see cref="ComputeSpanStarts"/>.</param>
    /// <returns>The content's extent along the line.</returns>
    private static float ComputeLineContentWidth(LayoutLine line, LayoutAxes axes)
    {
        float width = 0f;
        for (int i = 0; i < line.Elements.Count; i++)
            width += axes.Inline(line.Elements[i].Size);
        return width;
    }

    private static bool IsLastLineOfParagraph(List<LayoutLine> lines, int index)
    {
        if (index >= lines.Count - 1) return true;
        return lines[index + 1].ParagraphIndex != lines[index].ParagraphIndex;
    }


    /// <summary>
    /// Parameters that govern one paragraph: the request's, unless the paragraph named its own language.
    /// </summary>
    /// <param name="paragraphTypography">Per-paragraph parameters, or null.</param>
    /// <param name="paragraphIndex">Paragraph being adjusted.</param>
    /// <returns>The parameters for that paragraph.</returns>
    private ResolvedTypography TypographyFor(
        ResolvedTypography[]? paragraphTypography,
        int paragraphIndex) =>
        paragraphTypography is not null
        && paragraphIndex >= 0
        && paragraphIndex < paragraphTypography.Length
            ? paragraphTypography[paragraphIndex]
            : _typography;

    private static ParagraphSettings ResolveParagraphSettings(PreparedContent prepared, int paragraphIndex)
    {
        if (prepared.Paragraphs.Count > paragraphIndex)
            return prepared.Paragraphs[paragraphIndex].Settings;
        return new ParagraphSettings();
    }

    // ── Classification helpers ──
    // Element-level questions are answered by the element's own class (assigned in the prepare phase);
    // pair-level questions are answered by the compile-phase boundary between the two clusters. Neither
    // asks the classifier again, so a rule change happens in one place.

    /// <summary>Whether an element is a whitespace cluster.</summary>
    /// <param name="elem">The element to test.</param>
    /// <returns>True for whitespace.</returns>
    private static bool IsLatinSpace(in LayoutElement elem) =>
        elem is { Type: DrawElement.ElementType.Text, CharClass: CharacterClass.Space or CharacterClass.Tab };

    private static bool IsLatinSpaceElement(LayoutElement elem) => IsLatinSpace(elem);

    /// <summary>Whether an element is a punctuation cluster.</summary>
    /// <param name="elem">The element to test.</param>
    /// <returns>True for punctuation.</returns>
    private static bool IsPunctuation(in LayoutElement elem) =>
        elem is { Type: DrawElement.ElementType.Text, CharClass: CharacterClass.PunctuationOpen

            or CharacterClass.PunctuationClose

            or CharacterClass.PunctuationPauseStop

            or CharacterClass.PunctuationDash

            or CharacterClass.PunctuationEllipsis

            or CharacterClass.PunctuationInterpunct

            or CharacterClass.PunctuationWestern
        };

    private static bool IsPunctuationElement(LayoutElement elem) => IsPunctuation(elem);

    /// <summary>
    /// The boundary between two elements that are adjacent clusters, or null when they are not (a block
    /// element, an inserted background rectangle, or a cluster that was dropped from the line).
    /// </summary>
    /// <param name="boundaries">Compile-phase boundaries, indexed by the left cluster.</param>
    /// <param name="left">Left element of the pair.</param>
    /// <param name="right">Right element of the pair.</param>
    /// <returns>The boundary, or null when the two are not document-adjacent.</returns>
    private static Boundary? BoundaryBetween(
        IReadOnlyList<Boundary> boundaries,
        in LayoutElement left,
        in LayoutElement right)
    {
        int index = left.ClusterStart;
        if (index < 0 || index >= boundaries.Count)
            return null;

        var boundary = boundaries[index];
        return boundary.RightCluster == right.ClusterStart ? boundary : null;
    }

    /// <summary>Whether the pair sits across the CJK/Western split.</summary>
    /// <param name="boundaries">Compile-phase boundaries.</param>
    /// <param name="a">Left element.</param>
    /// <param name="b">Right element.</param>
    /// <returns>True when one side is CJK and the other Western.</returns>
    private static bool IsCjkLatinBoundary(IReadOnlyList<Boundary> boundaries, in LayoutElement a, in LayoutElement b)
    {
        if (BoundaryBetween(boundaries, a, b) is not { } boundary)
            return false;

        return IsCjk(boundary.LeftScript) && IsWestern(boundary.RightScript)
               || IsWestern(boundary.LeftScript) && IsCjk(boundary.RightScript);
    }

    /// <summary>Whether both sides are CJK, the inter-character case that stretches last.</summary>
    /// <param name="boundaries">Compile-phase boundaries.</param>
    /// <param name="a">Left element.</param>
    /// <param name="b">Right element.</param>
    /// <returns>True when both sides are CJK.</returns>
    private static bool IsCjkIdeographBoundary(IReadOnlyList<Boundary> boundaries, in LayoutElement a, in LayoutElement b)
    {
        return BoundaryBetween(boundaries, a, b) is { } boundary
               && IsCjk(boundary.LeftScript)
               && IsCjk(boundary.RightScript);
    }

    /// <summary>Whether a script role is CJK.</summary>
    /// <param name="role">The role to test.</param>
    /// <returns>True for the CJK group.</returns>
    private static bool IsCjk(ScriptRole role) => role == ScriptRole.Han;

    /// <summary>
    /// Whether a script role is Western for the CJK/Latin gap. Digits count as Western because the
    /// classifier groups them with Latin letters, and the gap rule has always treated them that way.
    /// </summary>
    /// <param name="role">The role to test.</param>
    /// <returns>True for Latin letters and digits.</returns>
    private static bool IsWestern(ScriptRole role) => role is ScriptRole.LatinLetter or ScriptRole.Digit;
}
