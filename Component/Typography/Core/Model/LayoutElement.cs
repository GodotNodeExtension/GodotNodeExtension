using System.Collections.Generic;
using Godot;

namespace GodotNodeExtension.Component.Typography.Core.Model;

/// <summary>
/// A laid-out element with finalized position, size, and all visual attributes.
/// Produced by the typography engine; consumed by the renderer.
/// </summary>
public struct LayoutElement
{
    // ── Identity ──

    /// <summary>Index of the source DrawElement this was derived from.</summary>
    public int SourceIndex { get; init; }

    // ── Geometry (output of layout) ──

    /// <summary>
    /// Final position of the corner the element's box starts at, relative to the content origin. This is a box
    /// origin, not a text baseline — use <see cref="BaselineY"/> for the baseline.
    /// <para>
    /// The box extends from here along both axes: along the inline axis it is
    /// <see cref="LayoutAxes.InlineUnit"/> and along the block axis <see cref="LayoutAxes.BlockUnit"/>, so in
    /// vertical writing it runs <em>leftward</em> from the position for right-to-left columns. A consumer that draws
    /// or hit-tests the box has to take the mode into account rather than assuming a rightward/downward rectangle.
    /// </para>
    /// </summary>
    public Vector2 Position { get; set; }

    /// <summary>
    /// Coordinate of the text baseline along the <em>block</em> axis, relative to the content origin: the line
    /// the baseline is measured from advances along the block axis, so what identifies it is a position on that
    /// axis rather than a Y.
    /// <para>
    /// Equal to <c>LayoutAxes.Block(Position) + ascent</c> of the cluster this element came from, and it lies
    /// inside the box' block extent. In horizontal writing the block axis is Y, so this is the familiar
    /// <c>Position.Y + ascent</c>; in vertical writing the block axis is X and the value is the column's own
    /// coordinate. For non-text elements it is the edge implied by their <see cref="InlineVerticalAlignment"/>.
    /// </para>
    /// <para>
    /// A renderer drawing a <em>line</em> places its glyphs on this baseline instead of re-deriving it from font
    /// metrics: a font swap would otherwise shift the whole line, and a second layout backend that reports
    /// baselines would end up one ascent off. A renderer drawing a <em>column</em> has no horizontal baseline to
    /// place: the pen runs down the middle of the column from the element's inline start, and the shaped glyphs
    /// carry their own offsets (see <see cref="GlyphRun"/>), so the vertical path must not read this value as a Y.
    /// </para>
    /// <para>
    /// <see cref="float.NaN"/> means "not computed": the element did not pass through the line
    /// breaker (fixed-size block content, block markers), so consumers must fall back to font
    /// metrics exactly as they did before this field existed. Glyph-level output fills it in later.
    /// </para>
    /// </summary>
    public float BaselineY { get; set; }

    /// <summary>Final size after layout.</summary>
    public Vector2 Size { get; set; }

    /// <summary>
    /// Character class the cluster was classified with, for elements that came from the line breaker.
    /// <para>
    /// Exposed on the output because a consumer that needs to know "is this element a space / punctuation"
    /// should not have to re-classify the text: the classification is a language rule, and re-deriving it
    /// outside the compile phase is how a rule ends up implemented twice. Elements the assembly stage
    /// synthesised (backgrounds, borders, markers, fixed-size block content) carry
    /// <see cref="CharacterClass.NonText"/>, because they were never classified.
    /// </para>
    /// </summary>
    public CharacterClass CharClass { get; init; }

    // ── Provenance (selection, hit testing, golden diffs) ──

    /// <summary>
    /// Source text range this element covers, relative to the text of the element identified by
    /// <see cref="SourceIndex"/>. Half-open, UTF-16 code unit indices.
    /// </summary>
    public TextRange SourceRange { get; init; }

    /// <summary>
    /// Index of the first layout cluster this element covers.
    /// <para>
    /// A cluster is the smallest indivisible layout unit (one CJK character, one Latin word, one
    /// grapheme cluster); a source element normally maps to many clusters, and a cluster never spans
    /// a line break. Consumers must map layout back to source text through these indices rather than
    /// through element order.
    /// </para>
    /// <para>
    /// P0 limitation: the value is the index of the <see cref="TextSegment"/> in the layout context
    /// that produced the element. A nested block layout numbers its own clusters from zero, and
    /// segments that never reach a line leave gaps — so the index is 1:1 and ordered, but not
    /// document-global. P1 replaces it with a document-global cluster index.
    /// </para>
    /// </summary>
    public int ClusterStart { get; init; }

    /// <summary>
    /// Index past the last layout cluster this element covers, in the same context-local numbering
    /// as <see cref="ClusterStart"/>. It equals <c>ClusterStart + 1</c> today because one segment is
    /// one cluster; it stays a range so a later cluster model can cover several segments.
    /// </summary>
    public int ClusterEnd { get; init; }

    /// <summary>
    /// Index into the line's <see cref="LayoutLine.Spans"/> of the interval this element was placed in.
    /// Zero for an ordinary line; line adjustment needs it to keep an element in the interval the breaker
    /// chose, and to align each interval on its own.
    /// </summary>
    public int SpanIndex { get; init; }

    /// <summary>
    /// Index of the <see cref="LayoutLine"/> this element was placed on.
    /// <para>
    /// Valid for elements produced by the document's line breaker. Elements expanded from a block's
    /// own sub-layout (see <see cref="BlockInfo"/> / <see cref="SubLines"/>) carry the index of the
    /// block's internal line instead, and are marked with a <see cref="Reason"/> so consumers can
    /// tell the two apart.
    /// </para>
    /// </summary>
    public int LineIndex { get; init; }

    /// <summary>Base text direction in effect for this element.</summary>
    public TextDirection Direction { get; init; }

    /// <summary>
    /// Text actually drawn when it differs from the source text (display-form substitution,
    /// e.g. <c>……</c> rendered as <c>⋯⋯</c>). Null means "identical to the source range";
    /// the substitution never changes <see cref="SourceRange"/>.
    /// </summary>
    public string? DisplayText { get; set; }

    /// <summary>
    /// Shaped glyphs of a text element, positioned at the element's own origin, or null for elements that
    /// have no text of their own (backgrounds, rules, markers).
    /// <para>
    /// This is the shape-once contract: the renderer draws these glyphs and never shapes the text again, so
    /// a glyph cannot end up anywhere other than where the layout measured it. It also frees the renderer
    /// from knowing the shaping engine, and lets it draw a correctly shaped run without the original text.
    /// </para>
    /// </summary>
    public GlyphRun? GlyphRun { get; init; }

    /// <summary>
    /// Human-readable reason for the decisions that produced this element's geometry, for example
    /// <c>"MergedInlineBackground"</c> or <c>"BlockDecoration:Background"</c>. Region spacing,
    /// prohibition and justification reasons are carried by the boundary model at P1; until then
    /// this field only records assembly-time decisions. It exists so layout behaviour can be
    /// regression-tested by diffing dumps, which is the only reliable way to keep a typography
    /// engine honest.
    /// </summary>
    public string? Reason { get; set; }

    // ── Visual attributes (passed through from DrawElement) ──

    /// <summary>The element type.</summary>
    public DrawElement.ElementType Type { get; init; }

    /// <summary>Color for rendering.</summary>
    public Color Color { get; init; }

    // ── Text ──

    /// <summary>Text content to render.</summary>
    public string? Text { get; init; }

    /// <summary>Font for text rendering.</summary>
    public Font? Font { get; init; }

    /// <summary>Font size for text rendering.</summary>
    public int FontSize { get; init; }

    /// <summary>Whether this text should be rendered bold.</summary>
    public bool IsBold { get; init; }

    /// <summary>Whether this text should be rendered italic.</summary>
    public bool IsItalic { get; init; }

    /// <summary>Whether this text has a strikethrough decoration.</summary>
    public bool IsStrikethrough { get; init; }

    /// <summary>Whether this text has an underline decoration.</summary>
    public bool IsUnderline { get; init; }

    /// <summary>Whether this text is rendered as subscript.</summary>
    public bool IsSubscript { get; init; }

    /// <summary>Whether this text is rendered as superscript.</summary>
    public bool IsSuperscript { get; init; }

    /// <summary>Ruby annotation text displayed above the base text.</summary>
    public string? RubyText { get; init; }

    /// <summary>
    /// Where this element's annotation was placed, with its own shaped glyphs: the renderer draws it without
    /// measuring anything again. Null when the element carries no annotation.
    /// </summary>
    public RubyAnnotation? Ruby { get; init; }

    /// <summary>
    /// The hyphen this element ends its line with, when the line broke inside a word here. Null otherwise.
    /// </summary>
    public GlyphRun? HyphenRun { get; init; }

    /// <summary>
    /// Whether this element's mark hangs past the end edge of its line.
    /// <para>
    /// A field of its own rather than a <see cref="Reason"/>: a reason says why a geometry decision was taken, and
    /// the invariants treat an element that carries one as synthetic (a decoration the layout made up rather than
    /// text from the source). A hanging mark is ordinary text that the convention lets stick out, so it needs its
    /// own flag or the text-coverage check stops seeing it.
    /// </para>
    /// </summary>
    public bool Hanging { get; init; }

    /// <summary>Emphasis mark style carried over from the source element.</summary>
    public EmphasisMarkStyle EmphasisMark { get; init; }

    /// <summary>Where this element's emphasis mark was placed, or null when it has none.</summary>
    public EmphasisMarkGeometry? Emphasis { get; init; }

    /// <summary>Per-span syntax coloring within a text element.</summary>
    public List<ColoredSpan>? SyntaxSpans { get; init; }

    // ── Image ──

    /// <summary>Texture for image rendering.</summary>
    public Texture2D? Texture { get; init; }

    // ── Interaction ──

    /// <summary>URL for link hit testing.</summary>
    public string? LinkUrl { get; init; }

    /// <summary>Unique element ID for selection tracking.</summary>
    public int ElementId { get; init; }

    // ── Extension ──

    /// <summary>Index into active extension regions.</summary>
    public int ExtensionId { get; init; }

    /// <summary>Raw content passed to the block extension.</summary>
    public string? ExtensionContent { get; init; }

    // ── Action ──

    /// <summary>Action identifier.</summary>
    public string? ActionTag { get; init; }

    /// <summary>Optional data passed with the action signal.</summary>
    public Variant ActionPayload { get; init; }

    // ── Playback ──

    /// <summary>Global character index for typewriter progress.</summary>
    public int CharacterIndex { get; init; }

    /// <summary>Number of characters in this element.</summary>
    public int CharacterCount { get; init; }

    // ── Effect ──

    /// <summary>-1 = no effect; index into TextEffectRegistry.</summary>
    public int TextEffectId { get; init; }

    // ── Shape ──

    /// <summary>Corner radius for rounded rectangle rendering.</summary>
    public float CornerRadius { get; init; }

    // ── Background decoration ──

    /// <summary>
    /// Background color for text elements with inline decoration (e.g. inline code, highlight).
    /// When non-null, the renderer draws a background rectangle behind the text.
    /// </summary>
    public Color? BackgroundColor { get; set; }

    /// <summary>
    /// Extra padding around text for background rendering (X = horizontal, Y = vertical).
    /// Applied symmetrically.
    /// </summary>
    public Vector2 BackgroundPadding { get; set; }

    /// <summary>
    /// Corner radius of the background rectangle. 0 for sharp corners.
    /// </summary>
    public float BackgroundCornerRadius { get; init; }

    /// <summary>
    /// When true, the merged background rectangle fills the full line height.
    /// </summary>
    public bool BackgroundFillLine { get; init; }

    // ── Auto-size block ──

    /// <summary>
    /// Sub-layout lines for auto-size blocks. Positions are relative to the block's content area
    /// (offset by LeftIndent + Padding from the block origin).
    /// Null for non-block or fixed-size block elements.
    /// </summary>
    public List<LayoutLine>? SubLines { get; set; }

    /// <summary>
    /// Block layout info for auto-size blocks, carried through for expansion in GetLayoutElements.
    /// Null for non-block elements.
    /// </summary>
    public BlockLayout? BlockInfo { get; set; }

    /// <summary>
    /// Build the glyph run of a segment, or null when the segment carries no shaped glyphs. The run's width
    /// is the sum of its advances: the natural width, before any line adjustment, which is what makes
    /// "measured" and "adjusted" geometry separable.
    /// </summary>
    /// <param name="segment">The measured segment.</param>
    /// <param name="position">Final top-left position of the element.</param>
    /// <param name="baselineY">Final baseline Y.</param>
    /// <param name="rotation">How the run is turned in its line (vertical writing turns a Latin run).</param>
    /// <returns>The glyph run, or null.</returns>
    private static GlyphRun? CreateGlyphRun(in TextSegment segment, Vector2 position, float baselineY,
        GlyphRotation rotation = GlyphRotation.None)
    {
        if (segment.Glyphs is not { Length: > 0 } glyphs)
            return null;

        float width = 0f;
        foreach (Glyph glyph in glyphs)
            width += glyph.Advance;

        return new GlyphRun
        {
            FontId = segment.FontId,
            FontSize = segment.Source.FontSize,
            Glyphs = glyphs,
            Origin = new Vector2(position.X, baselineY),
            Width = width,
            Rotation = rotation,
        };
    }

    /// <summary>
    /// Create a LayoutElement from a TextSegment, copying visual attributes from the source DrawElement.
    /// </summary>
    /// <param name="segment">The measured segment (one cluster of the layout).</param>
    /// <param name="position">Final top-left position, relative to the content origin.</param>
    /// <param name="baselineY">Final baseline Y, relative to the content origin.</param>
    /// <param name="size">Final size.</param>
    /// <param name="clusterIndex">Logical index of this segment's cluster; -1 when unknown.</param>
    /// <param name="lineIndex">Index of the line the element was placed on; -1 when unknown.</param>
    /// <param name="direction">Base text direction in effect.</param>
    /// <param name="spanIndex">Index of the line interval this element was placed in.</param>
    /// <param name="ruby">Laid-out annotation for this cluster, or null when it carries none.</param>
    /// <param name="emphasis">Laid-out emphasis mark for this cluster, or null when it carries none.</param>
    /// <param name="rotation">How the glyphs are turned in their line; vertical writing turns a Latin run.</param>
    /// <returns>A fully populated LayoutElement.</returns>
    public static LayoutElement FromSegment(
        in TextSegment segment,
        Vector2 position,
        float baselineY,
        Vector2 size,
        int clusterIndex = -1,
        int lineIndex = -1,
        TextDirection direction = TextDirection.LeftToRight,
        int spanIndex = 0,
        RubyAnnotation? ruby = null,
        EmphasisMarkGeometry? emphasis = null,
        GlyphRotation rotation = GlyphRotation.None)
    {
        DrawElement src = segment.Source;
        return new LayoutElement
        {
            SourceIndex = segment.SourceIndex,
            Position = position,
            BaselineY = baselineY,
            Size = size,
            CharClass = segment.CharClass,
            GlyphRun = CreateGlyphRun(segment, position, baselineY, rotation),
            SourceRange = new TextRange(segment.CharOffset, segment.CharOffset + segment.CharLength),
            ClusterStart = clusterIndex,
            ClusterEnd = clusterIndex >= 0 ? clusterIndex + 1 : -1,
            SpanIndex = spanIndex,
            LineIndex = lineIndex,
            Direction = direction,
            Type = src.Type,
            Color = src.Color,
            Text = segment.Text,
            DisplayText = segment.DisplayText,
            Font = src.Font,
            FontSize = src.FontSize,
            IsBold = src.IsBold,
            IsItalic = src.IsItalic,
            IsStrikethrough = src.IsStrikethrough,
            IsUnderline = src.IsUnderline,
            IsSubscript = src.IsSubscript,
            IsSuperscript = src.IsSuperscript,
            RubyText = src.Ruby?.Text,
            HyphenRun = segment.HyphenRun,
            Ruby = ruby,
            EmphasisMark = src.EmphasisMark,
            Emphasis = emphasis,
            SyntaxSpans = src.SyntaxSpans,
            Texture = src.Texture,
            LinkUrl = src.LinkUrl,
            ElementId = src.ElementId,
            ExtensionId = src.ExtensionId,
            ExtensionContent = src.ExtensionContent,
            ActionTag = src.ActionTag,
            ActionPayload = src.ActionPayload,
            CharacterIndex = src.CharacterIndex + segment.CharOffset,
            CharacterCount = segment.CharLength,
            TextEffectId = src.TextEffectId,
            CornerRadius = src.CornerRadius,
            BackgroundColor = src.BackgroundColor,
            BackgroundPadding = src.BackgroundPadding,
            BackgroundCornerRadius = src.BackgroundCornerRadius,
            BackgroundFillLine = src.BackgroundFillLine,
        };
    }
}
