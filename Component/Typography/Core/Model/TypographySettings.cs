using System.Collections.Generic;
using GodotNodeExtension.Component.Typography.Languages;

namespace GodotNodeExtension.Component.Typography.Core.Model;

/// <summary>
/// Paragraph-level settings that can override global typography defaults.
/// Each paragraph can carry its own ParagraphSettings; null fields fall back to global.
/// </summary>
public class ParagraphSettings
{
    /// <summary>
    /// BCP-47 language of this paragraph, overriding the request's. Null means "same language as the request".
    /// <para>
    /// A paragraph is the smallest unit a language can be attached to: mixed-language documents usually switch at
    /// paragraph boundaries (a Chinese document quoting an English block, an English manual with Japanese
    /// examples), and a paragraph is what a convention is written for — indent, prohibition rules and script
    /// spacing are paragraph properties, not run properties.
    /// </para>
    /// </summary>
    public string? LanguageTag { get; init; }

    /// <summary>First-line indent in character widths (null = use global). 0 = off, 2 = standard CJK.</summary>
    public int? FirstLineIndent { get; init; }

    /// <summary>Spacing before this paragraph in pixels (null = use global ParagraphSpacing).</summary>
    public float? SpacingBefore { get; set; }

    /// <summary>Spacing after this paragraph in pixels (null = use global ParagraphSpacing).</summary>
    public float? SpacingAfter { get; init; }

    /// <summary>Text alignment override for this paragraph (null = use global).</summary>
    public TextAlignment? Alignment { get; init; }

    /// <summary>Left margin indent for the entire paragraph (pixels). Used for blockquotes, list indents.</summary>
    public float LeftIndent { get; init; }

    /// <summary>Right margin indent for the entire paragraph (pixels).</summary>
    public float RightIndent { get; init; }
}

/// <summary>
/// Per-request typography configuration: the geometry of this layout plus optional overrides of the
/// behaviour its language prescribes.
/// <para>
/// The language-governed properties are nullable on purpose. A null means "whatever the language
/// profile says" (see <see cref="LanguageProfileRegistry.Resolve(TypographySettings)"/>); the old
/// non-nullable defaults made every document behave like Chinese, because a request-level default
/// cannot be distinguished from a deliberate choice. Geometry (<see cref="MaxWidth"/>,
/// <see cref="Padding"/>, <see cref="WrapRegions"/>) is request-owned and never comes from a profile.
/// </para>
/// <para>
/// Individual paragraphs can still override the paragraph-level values via
/// <see cref="ParagraphSettings"/>.
/// </para>
/// </summary>
public class TypographySettings
{
    /// <summary>
    /// BCP-47 language tag of the text being laid out, e.g. <c>zh-Hans</c> or <c>en</c>. Null means
    /// "not specified" and resolves to the profile with no language assumption.
    /// </summary>
    public string? LanguageTag { get; init; }

    /// <summary>
    /// Writing mode to lay this request out in, or null for "whatever the language's profile says" (today: the
    /// horizontal mode for every built-in profile).
    /// <para>
    /// A writing mode is a layout decision, not a property of a language: the same language is set vertically in
    /// books and horizontally on a web page, so the caller chooses and the profile only supplies what a language
    /// prescribes *within* that mode (where ruby and emphasis marks go, whether punctuation may hang).
    /// </para>
    /// <para>
    /// Asking for a vertical mode is currently refused with an explicit failure rather than laid out
    /// horizontally - see <see cref="Languages.LanguageProfileRegistry.Resolve(TypographySettings)"/>.
    /// </para>
    /// </summary>
    public WritingMode? WritingMode { get; init; }

    /// <summary>Maximum line width in pixels: the extent of a line along the inline axis in horizontal writing.</summary>
    public float MaxWidth { get; set; }

    /// <summary>
    /// Maximum column height in pixels: the inline extent limit for vertical writing, ignored in horizontal
    /// writing (where <see cref="MaxWidth"/> is the inline limit and the block axis is unbounded). Zero or less
    /// means "no limit".
    /// </summary>
    public float MaxHeight { get; set; }

    /// <summary>Extra line spacing in pixels.</summary>
    public float LineSpacing { get; init; }

    /// <summary>Default paragraph spacing in pixels (used when ParagraphSettings.SpacingBefore/After is null).</summary>
    public float ParagraphSpacing { get; set; }

    /// <summary>Override: whether CJK line-start/end prohibition rules apply. Null = profile decides.</summary>
    public bool? EnableLineProhibition { get; init; }

    /// <summary>Override: prohibition strictness. Null = profile decides.</summary>
    public ProhibitionLevel? ProhibitionLevel { get; init; }

    /// <summary>Override: whether to insert spacing between CJK and Latin. Null = profile decides.</summary>
    public bool? EnableCjkLatinSpacing { get; set; }

    /// <summary>Override: CJK-Latin spacing in ems. Null = profile decides.</summary>
    public float? CjkLatinSpacingEm { get; init; }

    /// <summary>
    /// Override: whether automatic hyphenation is applied. Null = profile decides (a language with patterns
    /// hyphenates, one without does not).
    /// </summary>
    public bool? EnableHyphenation { get; init; }

    /// <summary>Override: whether punctuation may be squeezed. Null = profile decides.</summary>
    public bool? EnablePunctuationCompression { get; init; }

    /// <summary>Override: first-line indent in ems (0 = off, 2 = CJK convention). Null = profile decides.</summary>
    public int? FirstLineIndent { get; set; }

    /// <summary>Override: paragraph alignment. Null = profile decides.</summary>
    public TextAlignment? Alignment { get; set; }

    /// <summary>Override: base direction of the text. Null = profile decides.</summary>
    public TextDirection? Direction { get; set; }

    /// <summary>
    /// Override: whether this language's display forms (quotation marks, ellipsis, sentence marks) are applied.
    /// Null = profile decides.
    /// </summary>
    public bool? EnableLetterformSubstitution { get; init; }

    /// <summary>
    /// Grid step in pixels, or null for no grid. When set, every element's left edge is snapped to a multiple of
    /// this step from <see cref="GridOrigin"/>, which is what makes a page of text line up column by column and
    /// what gives CJK/Latin mixing its integral character cells (clreq §6.2.4).
    /// </summary>
    public float? GridStep { get; set; }

    /// <summary>Where the grid starts, relative to the content origin. Only used when <see cref="GridStep"/> is set.</summary>
    public float GridOrigin { get; set; }

    /// <summary>
    /// Tab stops, in ascending order. A tabulation character sends what follows it to the next one. The list is
    /// mutable like <see cref="WrapRegions"/>, so a caller can build it up and a canvas subclass can add to what its
    /// base class produced.
    /// </summary>
    public List<TabStop> TabStops { get; init; } = [];

    /// <summary>
    /// Spacing of the automatic tab stops, in ems, used where <see cref="TabStops"/> has no stop past the pen.
    /// Null turns the automatic stops off, so a tab past the last explicit stop does nothing.
    /// </summary>
    public float? DefaultTabStopEm { get; init; } = 4f;

    /// <summary>
    /// Wrap regions for image-text mixed layout.
    /// When non-empty, layout uses per-line available span queries instead of fixed maxWidth.
    /// </summary>
    public List<WrapRegion> WrapRegions { get; init; } = [];

    /// <summary>
    /// Padding applied around the entire content area (all four sides).
    /// </summary>
    public float Padding { get; init; }

    /// <summary>
    /// Create an independent copy of these settings.
    /// <para>
    /// Used wherever a nested layout needs its own settings (an auto-size block re-lays-out its inner
    /// content at a narrower width). A hand-written field-by-field copy used to live at the call site
    /// and had already silently dropped <see cref="Padding"/>, which is exactly the failure mode this
    /// method removes: a new setting is now inherited unless the caller overrides it explicitly.
    /// </para>
    /// </summary>
    /// <returns>
    /// A copy with the same values; <see cref="WrapRegions"/> and <see cref="TabStops"/> are copied as new lists.
    /// <c>TypographyCloneTest</c> reflects over the properties so a setting added later cannot be dropped here
    /// silently.
    /// </returns>
    public TypographySettings Clone() => new()
    {
        LanguageTag = LanguageTag,
        WritingMode = WritingMode,
        MaxWidth = MaxWidth,
        MaxHeight = MaxHeight,
        LineSpacing = LineSpacing,
        ParagraphSpacing = ParagraphSpacing,
        EnableLineProhibition = EnableLineProhibition,
        ProhibitionLevel = ProhibitionLevel,
        EnableCjkLatinSpacing = EnableCjkLatinSpacing,
        CjkLatinSpacingEm = CjkLatinSpacingEm,
        EnableHyphenation = EnableHyphenation,
        EnablePunctuationCompression = EnablePunctuationCompression,
        EnableLetterformSubstitution = EnableLetterformSubstitution,
        FirstLineIndent = FirstLineIndent,
        Alignment = Alignment,
        Direction = Direction,
        GridStep = GridStep,
        GridOrigin = GridOrigin,
        TabStops = [.. TabStops],
        DefaultTabStopEm = DefaultTabStopEm,
        WrapRegions = [.. WrapRegions],
        Padding = Padding,
    };
}

/// <summary>
/// A positioned exclusion region in layout space.
/// Combines a shape with positioning and float mode.
/// Text flows around this region during line layout.
/// </summary>
public class WrapRegion
{
    /// <summary>The shape of the exclusion zone.</summary>
    public WrapShape Shape { get; init; } = new RectWrapShape();

    /// <summary>Position of the shape's origin in content space.</summary>
    public Godot.Vector2 Position { get; init; }

    /// <summary>Wrap mode for this region (left/right float, or inline).</summary>
    public WrapFloat WrapMode { get; set; }

    /// <summary>Margin/padding around the shape (text keeps this distance).</summary>
    public float Margin { get; init; }

    /// <summary>
    /// First line this float belongs to (0 = the first line of the layout). Lines before it are not affected, which
    /// is what gives a float a lifecycle: an exclusion does not have to start at the top of the content.
    /// </summary>
    public int FirstLine { get; init; }

    /// <summary>Last line this float belongs to, or -1 for "to the end of the layout".</summary>
    public int LastLine { get; init; } = -1;
}

/// <summary>
/// How a wrap region is anchored in the text flow.
/// </summary>
public enum WrapFloat
{
    /// <summary>Float to the left, text wraps on the right.</summary>
    Left,

    /// <summary>Float to the right, text wraps on the left.</summary>
    Right,

    /// <summary>No float, inline block that breaks the line.</summary>
    Inline,
}
