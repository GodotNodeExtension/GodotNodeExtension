using Godot;

namespace GodotNodeExtension.Component.Typography.Core.Model;

/// <summary>
/// One shaped glyph of a laid-out element: what to draw, where the pen sits relative to it, and which
/// source characters it came from.
/// <para>
/// A glyph is not a character. Shaping merges several characters into one glyph (a ligature) and splits one
/// character into several (a base with marks), so the only sound way to map layout back to text is through
/// <see cref="ClusterStart"/>/<see cref="ClusterEnd"/> rather than through position.
/// </para>
/// </summary>
public readonly record struct Glyph
{
    /// <summary>Font-specific glyph index to draw.</summary>
    public uint Id { get; init; }

    /// <summary>
    /// Id of the font this glyph belongs to, in <see cref="FontCatalog"/> terms. A run is not necessarily
    /// drawn with one face: the fallback path shapes the parts of the text the primary face cannot cover
    /// with another face, and a glyph index only means something together with the face it came from.
    /// </summary>
    public ulong FontId { get; init; }

    /// <summary>
    /// Horizontal advance in pixels, including kerning and ligature effects. The sum over a run is the
    /// width the layout measured and broke lines with.
    /// </summary>
    public float Advance { get; init; }

    /// <summary>Horizontal offset from the pen position to the glyph origin (mark positioning).</summary>
    public float OffsetX { get; init; }

    /// <summary>Vertical offset from the baseline to the glyph origin.</summary>
    public float OffsetY { get; init; }

    /// <summary>First source character this glyph covers, relative to the element's text.</summary>
    public int ClusterStart { get; init; }

    /// <summary>One past the last source character this glyph covers.</summary>
    public int ClusterEnd { get; init; }
}

/// <summary>
/// How the glyphs of a run are turned relative to the line.
/// </summary>
public enum GlyphRotation
{
    /// <summary>The glyphs keep the orientation the font draws them in (every horizontal run, and ideographs).</summary>
    None,

    /// <summary>
    /// The glyphs are turned a quarter turn clockwise, which is how a Latin run is set in vertical writing: the run
    /// then reads top to bottom down the column.
    /// </summary>
    ClockwiseQuarter,
}

/// <summary>
/// The shaped glyphs of one laid-out text element, positioned from <see cref="Origin"/>.
/// <para>
/// This is the shape-once contract: the renderer draws these glyphs and never shapes the text again, so a
/// glyph can never appear at a position other than the one the layout measured. It also means the renderer
/// no longer needs the shaping engine, the font size or the text itself to place glyphs.
/// </para>
/// </summary>
public readonly record struct GlyphRun
{
    /// <summary>
    /// Id of the resolved font the glyphs belong to, as handed out by <see cref="FontCatalog"/>. The
    /// renderer resolves it to a platform font; the layout thread never carries a Godot resource across.
    /// <para>
    /// The architecture note called this an opaque string key for backend neutrality. It is the catalog's
    /// numeric id instead, because the only consumer today resolves it back through that same catalog; a
    /// backend-neutral key can replace it when a second font backend exists.
    /// </para>
    /// </summary>
    public ulong FontId { get; init; }

    /// <summary>Font size in pixels used to shape the run.</summary>
    public float FontSize { get; init; }

    /// <summary>Glyphs in drawing order.</summary>
    public Glyph[] Glyphs { get; init; }

    /// <summary>
    /// Origin of the run: the pen position on the baseline of the first glyph, relative to the content
    /// origin.
    /// </summary>
    public Vector2 Origin { get; init; }

    /// <summary>
    /// How the run is turned in its line, which only vertical writing asks for: a run of Latin letters and digits is
    /// set rotated, so that it reads on when the head is tilted, while an ideograph stays upright.
    /// </summary>
    public GlyphRotation Rotation { get; init; }

    /// <summary>
    /// Natural width of the run in pixels: the sum of the advances, i.e. the width the layout measured
    /// before any line adjustment. Adjustment changes the element's box, not the glyphs, which is what keeps
    /// "natural" and "adjusted" geometry separable.
    /// </summary>
    public float Width { get; init; }
}
