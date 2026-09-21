using System.Collections.Generic;
using Godot;

namespace GodotNodeExtension.Component.Typography.Core.Model;

/// <summary>
/// Represents a single drawing element in the rendering pipeline.
/// Used as the fundamental unit for layout and rendering.
/// </summary>
public struct DrawElement
{
    /// <summary>
    /// The type of this drawing element.
    /// </summary>
    public enum ElementType
    {
        /// <summary>Renderable text span.</summary>
        Text,
        /// <summary>Filled rectangle.</summary>
        Rect,
        /// <summary>Line segment.</summary>
        Line,
        /// <summary>Image/texture.</summary>
        Image,
        /// <summary>Block extension custom draw region.</summary>
        ExtensionRegion,
        /// <summary>Non-visual trigger point (game actions).</summary>
        Action,
    }

    /// <summary>The element type.</summary>
    public ElementType Type { get; set; }

    /// <summary>Position relative to content origin.</summary>
    public Vector2 Position { get; set; }

    /// <summary>Size of the element.</summary>
    public Vector2 Size { get; set; }

    // ── Layout output only (ignored when the element is used as layout input) ──
    // These fields are filled in by the typography engine so that a renderer can consume laid-out
    // elements without re-deriving geometry. Producers of layout input leave them at their defaults.

    /// <summary>
    /// Coordinate of the text baseline along the block axis, relative to the content origin; see
    /// <see cref="LayoutElement.BaselineY"/>. A renderer drawing a line uses it instead of adding a font
    /// ascent to <see cref="Position"/>, which would double-count once a font is swapped; a renderer drawing a
    /// column does not, because a column has no horizontal baseline.
    /// <see cref="float.NaN"/> means the layout never computed one; fall back to font metrics.
    /// </summary>
    public float BaselineY { get; set; }

    /// <summary>Source text range this element covers; see <see cref="LayoutElement.SourceRange"/>.</summary>
    public TextRange SourceRange { get; set; }

    /// <summary>Shaped glyphs of this element; see <see cref="LayoutElement.GlyphRun"/>.</summary>
    public GlyphRun? GlyphRun { get; set; }

    /// <summary>Character class of the cluster; see <see cref="LayoutElement.CharClass"/>.</summary>
    public CharacterClass CharClass { get; set; }

    /// <summary>Index of the line interval this element sits in; see <see cref="LayoutElement.SpanIndex"/>.</summary>
    public int SpanIndex { get; set; }

    /// <summary>Index of the first layout cluster; see <see cref="LayoutElement.ClusterStart"/>.</summary>
    public int ClusterStart { get; set; }

    /// <summary>Index past the last layout cluster; see <see cref="LayoutElement.ClusterEnd"/>.</summary>
    public int ClusterEnd { get; set; }

    /// <summary>Index of the line this element was placed on; see <see cref="LayoutElement.LineIndex"/>.</summary>
    public int LineIndex { get; set; }

    /// <summary>Base text direction in effect; see <see cref="LayoutElement.Direction"/>.</summary>
    public TextDirection Direction { get; set; }

    /// <summary>Drawn text when it differs from the source text; see <see cref="LayoutElement.DisplayText"/>.</summary>
    public string? DisplayText { get; set; }

    /// <summary>Reason for the geometry decisions; see <see cref="LayoutElement.Reason"/>.</summary>
    public string? Reason { get; set; }

    /// <summary>Color for rendering.</summary>
    public Color Color { get; set; }

    // ── Text specific ──

    /// <summary>Text content to render.</summary>
    public string? Text { get; set; }

    /// <summary>Font for text rendering.</summary>
    public Font? Font { get; set; }

    /// <summary>
    /// Id of the typeface this element's <see cref="Font"/> was resolved to, filled in by the producer
    /// through <see cref="FontCatalog.EnsureResolved(DrawElement[])"/> on the main thread. The layout
    /// thread reads the typeface from the catalog by this id, so it never has to touch the Godot
    /// resource (see <see cref="FontCatalog"/> for why that matters). Zero means "not resolved yet".
    /// </summary>
    public ulong ResolvedFontId { get; set; }

    /// <summary>Font size for text rendering.</summary>
    public int FontSize { get; set; }

    // ── Image specific ──

    /// <summary>Texture for image rendering.</summary>
    public Texture2D? Texture { get; set; }

    // ── Interaction specific ──

    /// <summary>URL for link hit testing. Null if not a link.</summary>
    public string? LinkUrl { get; set; }

    /// <summary>Unique element ID for selection tracking.</summary>
    public int ElementId { get; set; }

    // ── Extension specific ──

    /// <summary>Index into active extension regions. -1 if not an extension.</summary>
    public int ExtensionId { get; set; }

    /// <summary>Raw content passed to the block extension (e.g. code inside fenced block).</summary>
    public string? ExtensionContent { get; set; }

    // ── Action specific ──

    /// <summary>Action identifier (e.g. "shake", "sfx").</summary>
    public string? ActionTag { get; set; }

    /// <summary>Optional data passed with the action signal.</summary>
    public Variant ActionPayload { get; set; }

    // ── Playback metadata ──

    /// <summary>Global character index for typewriter progress.</summary>
    public int CharacterIndex { get; set; }

    /// <summary>Number of characters in this element (0 for non-text).</summary>
    public int CharacterCount { get; set; }

    // ── Text effect ──

    /// <summary>-1 = no effect; index into TextEffectRegistry.</summary>
    public int TextEffectId { get; set; }

    // ── Text style ──

    /// <summary>Whether this text should be rendered bold.</summary>
    public bool IsBold { get; set; }

    /// <summary>Whether this text should be rendered italic.</summary>
    public bool IsItalic { get; set; }

    // ── Text decorations ──

    /// <summary>Whether this text has a strikethrough decoration.</summary>
    public bool IsStrikethrough { get; set; }

    /// <summary>Whether this text has an underline decoration.</summary>
    public bool IsUnderline { get; set; }

    /// <summary>Whether this text is rendered as subscript (smaller, lowered baseline).</summary>
    public bool IsSubscript { get; set; }

    /// <summary>Whether this text is rendered as superscript (smaller, raised baseline).</summary>
    public bool IsSuperscript { get; set; }

    /// <summary>
    /// Ruby annotation text displayed above the base text.
    /// Null means no ruby annotation.
    /// <para>
    /// Shorthand for <see cref="Ruby"/> with <see cref="RubyDistribution.Group"/>: one annotation over the whole
    /// element's text, which is what a caller who has a single annotation string means. Setting
    /// <see cref="Ruby"/> directly is how a caller asks for per-character distribution or a different size.
    /// </para>
    /// </summary>
    public string? RubyText
    {
        get => Ruby?.Text;
        set => Ruby = string.IsNullOrEmpty(value)
            ? null
            : new RubySpec { Text = value, Distribution = RubyDistribution.Group };
    }

    /// <summary>Ruby annotation to place over this element's text, or null for none.</summary>
    public RubySpec? Ruby { get; set; }

    /// <summary>
    /// Emphasis marks over the characters of this element (着重点 / 圏点), or
    /// <see cref="EmphasisMarkStyle.None"/> for none. The mark's shape and side come from the language.
    /// </summary>
    public EmphasisMarkStyle EmphasisMark { get; set; }

    /// <summary>Where the layout placed this element's annotation, or null when it has none.</summary>
    public RubyAnnotation? LaidOutRuby { get; set; }

    /// <summary>Where the layout placed this element's emphasis mark, or null when it has none.</summary>
    public EmphasisMarkGeometry? LaidOutEmphasis { get; set; }

    // ── Syntax highlighting ──

    /// <summary>
    /// Per-span syntax coloring within a text element. Null = use element Color.
    /// When set, the renderer draws each span with its own color instead of the element color.
    /// </summary>
    public List<ColoredSpan>? SyntaxSpans { get; set; }

    // ── Shape ──

    /// <summary>Corner radius for rounded rectangle rendering. 0 means sharp corners.</summary>
    public float CornerRadius { get; set; }

    // ── Typography semantic markers ──

    /// <summary>
    /// When true, this element represents a paragraph break.
    /// The typography engine uses this to split content into paragraphs.
    /// </summary>
    public bool IsParagraphBreak { get; set; }

    /// <summary>
    /// Paragraph-level typography overrides (indent, spacing, alignment) for the paragraph this
    /// element starts. Null — the default — means "use the global settings", which is what every
    /// caller got before this field existed. Only the element that begins a paragraph is consulted;
    /// the value is ignored on elements inside it.
    /// </summary>
    public ParagraphSettings? ParagraphSettings { get; set; }

    /// <summary>
    /// Block layout info. When set, this element marks the start of a block region.
    /// Elements between this and the matching <see cref="IsBlockEnd"/> element form an
    /// atomic block with internally-managed layout. The typography engine places
    /// the block as a single unit without rearranging its internal elements.
    /// </summary>
    public BlockLayout? BlockInfo { get; set; }

    /// <summary>
    /// When true, marks the end of a block region started by the previous
    /// element with <see cref="BlockInfo"/> set.
    /// </summary>
    public bool IsBlockEnd { get; set; }

    // ── Inline alignment ──

    /// <summary>
    /// Vertical alignment for inline non-text elements (images).
    /// Controls how the element's height is distributed between ascent and descent
    /// during typography layout. Default is <see cref="InlineVerticalAlignment.Top"/>.
    /// </summary>
    public InlineVerticalAlignment VerticalAlignment { get; set; }

    // ── Background decoration ──

    /// <summary>
    /// Background color for text elements with inline decoration (e.g. inline code, highlight).
    /// When non-null, the renderer draws a background rectangle behind the text.
    /// </summary>
    public Color? BackgroundColor { get; set; }

    /// <summary>
    /// Extra padding around text for background rendering (X = horizontal, Y = vertical).
    /// Applied symmetrically. Also affects width during typography layout.
    /// </summary>
    public Vector2 BackgroundPadding { get; set; }

    /// <summary>
    /// Corner radius of the background rectangle. 0 for sharp corners.
    /// </summary>
    public float BackgroundCornerRadius { get; set; }

    /// <summary>
    /// When true, the background rectangle expands to fill the full line height
    /// instead of tightly fitting the text element's own height.
    /// Useful for console-style or highlighted text spans.
    /// </summary>
    public bool BackgroundFillLine { get; set; }

    /// <summary>
    /// Create a DrawElement from a LayoutElement, copying all visual attributes.
    /// Position is preserved as-is (caller must offset for block-relative coordinates).
    /// </summary>
    public static DrawElement FromLayoutElement(in LayoutElement le)
    {
        return new DrawElement
        {
            Type = le.Type,
            Position = le.Position,
            Size = le.Size,
            BaselineY = le.BaselineY,
            SourceRange = le.SourceRange,
            CharClass = le.CharClass,
            GlyphRun = le.GlyphRun,
            SpanIndex = le.SpanIndex,
            ClusterStart = le.ClusterStart,
            ClusterEnd = le.ClusterEnd,
            LineIndex = le.LineIndex,
            Direction = le.Direction,
            DisplayText = le.DisplayText,
            Reason = le.Reason,
            Color = le.Color,
            Text = le.Text,
            Font = le.Font,
            FontSize = le.FontSize,
            IsBold = le.IsBold,
            IsItalic = le.IsItalic,
            IsStrikethrough = le.IsStrikethrough,
            IsUnderline = le.IsUnderline,
            IsSubscript = le.IsSubscript,
            IsSuperscript = le.IsSuperscript,
            RubyText = le.Ruby?.Text,
            LaidOutRuby = le.Ruby,
            LaidOutEmphasis = le.Emphasis,
            SyntaxSpans = le.SyntaxSpans,
            Texture = le.Texture,
            LinkUrl = le.LinkUrl,
            ElementId = le.ElementId,
            ExtensionId = le.ExtensionId,
            ExtensionContent = le.ExtensionContent,
            ActionTag = le.ActionTag,
            ActionPayload = le.ActionPayload,
            CharacterIndex = le.CharacterIndex,
            CharacterCount = le.CharacterCount,
            TextEffectId = le.TextEffectId,
            CornerRadius = le.CornerRadius,
            BackgroundColor = le.BackgroundColor,
            BackgroundPadding = le.BackgroundPadding,
            BackgroundCornerRadius = le.BackgroundCornerRadius,
            BackgroundFillLine = le.BackgroundFillLine,
        };
    }
}
