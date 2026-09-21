namespace GodotNodeExtension.Component.Typography.Core.Model;

using System.Collections.Generic;

/// <summary>
/// How an annotation is distributed over its base text.
/// </summary>
public enum RubyDistribution
{
    /// <summary>One annotation per base character, centred on that character (jlreq's モノルビ).</summary>
    Mono,

    /// <summary>One annotation for the whole base run, centred over the run (jlreq's グループルビ).</summary>
    Group,

    /// <summary>
    /// Per-character annotations whose run is kept together and laid out as a group, so an annotation wider than its
    /// character does not collide with its neighbour (jlreq's 熟語ルビ, §3.3.7).
    /// <para>
    /// The base run widens to fit the annotations it carries: the shortfall between what the annotations ask for and
    /// the width of the characters they belong to is added to the base text, up to a cap per position (the cap is an
    /// engine value - the specification states the principle, not a number), which is why extremely wide annotations
    /// can still meet. The run is atomic: nothing separates it from its annotation.
    /// </para>
    /// </summary>
    Jukugo,
}

/// <summary>
/// An annotation to place above (or beside) a run of base text, as the caller describes it.
/// <para>
/// The input says what the annotation is and how it should be distributed; the geometry it ends up with is the
/// layout's business, and comes back on <see cref="LayoutElement.Ruby"/>. Ruby is an inline annotation, not a
/// separate line: it stays with its base when a line breaks and may reserve room above it.
/// </para>
/// </summary>
public sealed record RubySpec
{
    /// <summary>The annotation text.</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>How the annotation is distributed over the base text.</summary>
    public RubyDistribution Distribution { get; init; } = RubyDistribution.Mono;

    /// <summary>
    /// Annotation font size as a fraction of the base font size. The default is jlreq §3.3.3's half size.
    /// </summary>
    public float SizeRatio { get; init; } = 0.5f;
}

/// <summary>
/// The annotation pieces one source element's text carries, measured and positioned by cluster.
/// <para>
/// It is stored per source element (and per cluster for its pieces) rather than on every segment, because one
/// annotation spans several clusters: a group ruby is one piece covering many clusters, and a mono ruby is one
/// piece per cluster. <see cref="GroupClusterStart"/>…<see cref="GroupClusterStart"/>+<see cref="GroupClusterCount"/>
/// is the range that must not be split across lines.
/// </para>
/// </summary>
public sealed class MeasuredRuby
{
    /// <summary>Distribution this measurement was produced under.</summary>
    public RubyDistribution Distribution { get; init; }

    /// <summary>Annotation font size in pixels (base font size times the spec's ratio).</summary>
    public float FontSize { get; init; }

    /// <summary>Font the annotation was shaped with, in <see cref="FontCatalog"/> terms.</summary>
    public ulong FontId { get; init; }

    /// <summary>First cluster of the base run the annotation belongs to.</summary>
    public int GroupClusterStart { get; init; }

    /// <summary>Number of clusters the base run covers.</summary>
    public int GroupClusterCount { get; init; }

    /// <summary>The annotation pieces, in cluster order.</summary>
    public IReadOnlyList<RubyPiece> Pieces { get; init; } = [];

    /// <summary>Largest ascent among the pieces.</summary>
    public float Ascent { get; init; }

    /// <summary>Largest descent among the pieces.</summary>
    public float Descent { get; init; }

    /// <summary>
    /// Largest room any of the annotation's pieces needs across its reading direction; see
    /// <see cref="RubyPiece.InkExtent"/>. The band a line reserves for the annotation covers this.
    /// </summary>
    public float InkExtent { get; init; }

    /// <summary>
    /// Room added to the base run's inter-character spacing to keep neighbouring annotations apart (jukugo ruby);
    /// 0 for every other distribution.
    /// </summary>
    public float Expansion { get; init; }

    /// <summary>
    /// Advance added to each annotated base cluster to make room for an annotation that sits beside it, in pixels;
    /// 0 when the language reserves nothing beside the base text.
    /// <para>
    /// The value is uniform across the pieces of one run, and exactly one reserve was added to the last cluster
    /// each piece covers - which is what lets the assembler recover the advance an annotation was placed against
    /// by taking one reserve off the width the piece covers. Like <see cref="Expansion"/> it is part of the base
    /// text's own advance, so line breaking, the element box and the pen all see it without knowing that
    /// annotations exist.
    /// </para>
    /// </summary>
    public float SideReserve { get; init; }
}

/// <summary>
/// One annotation piece and the clusters it covers: a single cluster for a mono ruby, the whole run for a group
/// ruby.
/// </summary>
public sealed class RubyPiece
{
    /// <summary>The annotation text of this piece.</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>First cluster of the base text this piece annotates.</summary>
    public int ClusterStart { get; init; }

    /// <summary>Number of base clusters this piece annotates.</summary>
    public int ClusterCount { get; init; } = 1;

    /// <summary>Shaped annotation glyphs, ready to draw.</summary>
    public Glyph[] Glyphs { get; init; } = [];

    /// <summary>Width of the shaped annotation in pixels.</summary>
    public float Width { get; init; }

    /// <summary>Ascent of the annotation's font in pixels.</summary>
    public float Ascent { get; init; }

    /// <summary>Descent of the annotation's font in pixels.</summary>
    public float Descent { get; init; }

    /// <summary>
    /// Room this piece's ink needs across its reading direction, in pixels: above and below the baseline when the
    /// annotation is set along a line, to either side of the pen when it runs down a column.
    /// <para>
    /// This is what a band has to hold, and it is not the font's line height: a tone mark rises above the font's
    /// ascent and a mark with a tall glyph exceeds it too, so a band measured with line metrics cuts off part of the
    /// annotation it exists for. The line metrics stay the floor, since an annotation is still set as a text run.
    /// </para>
    /// </summary>
    public float InkExtent { get; init; }
}

/// <summary>
/// Where one annotation was placed, in content coordinates.
/// <para>
/// The layout hands the renderer the finished geometry and the shaped glyphs, exactly as it does for base text:
/// an annotation is not measured again by whoever draws it, and its position does not depend on the renderer's
/// font metrics.
/// </para>
/// </summary>
public readonly record struct RubyAnnotation
{
    /// <summary>Annotation text.</summary>
    public string Text { get; init; }

    /// <summary>Shaped glyphs of the annotation.</summary>
    public Glyph[]? Glyphs { get; init; }

    /// <summary>Font the glyphs belong to.</summary>
    public ulong FontId { get; init; }

    /// <summary>Annotation font size in pixels.</summary>
    public float FontSize { get; init; }

    /// <summary>Left edge of the annotation.</summary>
    public float X { get; init; }

    /// <summary>Baseline of the annotation.</summary>
    public float BaselineY { get; init; }

    /// <summary>
    /// Extent of the annotation along its own reading direction: the band's width when the annotation is set
    /// along the line, the column's height when it is set vertically. It is the sum of the glyph advances in
    /// either case, so a renderer walks those advances along whichever direction the annotation reads in.
    /// </summary>
    public float Width { get; init; }

    /// <summary>
    /// Room the layout reserved for this annotation across its reading direction, or 0 when it reserved none.
    /// <para>
    /// A Beside annotation is given half a base em of the base character's own advance
    /// (<see cref="MeasuredRuby.SideReserve"/>, clreq §5.5.3.2), and an interlinear one is given the line's
    /// annotation band; either way the annotation is placed <em>inside</em> that room, so a renderer that has it
    /// can centre the annotation in its own band - which is what CSS Ruby calls <c>ruby-align: space-around</c>,
    /// its initial value. Without the room a renderer can only anchor the annotation at one edge of it.
    /// </para>
    /// </summary>
    public float BandWidth { get; init; }

    /// <summary>Which way the annotation is set: along the line, or down a column of its own.</summary>
    public RubyOrientation Orientation { get; init; }

    /// <summary>Why it sits where it sits, for a dump.</summary>
    public string? Reason { get; init; }
}

/// <summary>
/// Emphasis mark above or beside a base character (着重点 / 圏点).
/// </summary>
public enum EmphasisMarkStyle
{
    /// <summary>No emphasis mark.</summary>
    None,

    /// <summary>A dot: <c>●</c> (U+25CF) in Chinese, <c>•</c> (U+2022) in horizontal Japanese.</summary>
    Dot,

    /// <summary>A sesame dot (<c>﹅</c>), which is the vertical-writing form in jlreq.</summary>
    SesameDot,
}

/// <summary>Which side of the base characters a language puts its emphasis marks on.</summary>
public enum EmphasisSide
{
    /// <summary>Under the characters (horizontal Chinese, clreq §5.3.1).</summary>
    Below,

    /// <summary>Over the characters (horizontal Japanese, jlreq §3.3.9).</summary>
    Above,

    /// <summary>To the right of the characters (vertical writing; reserved).</summary>
    Right,
}

/// <summary>
/// Where one emphasis mark was placed, in content coordinates. The mark is centred on the base character it
/// applies to (clreq §5.3.1: emphasis marks shall be centre-aligned with their character).
/// </summary>
public readonly record struct EmphasisMarkGeometry
{
    /// <summary>The mark's character.</summary>
    public string Mark { get; init; }

    /// <summary>Mark font size in pixels.</summary>
    public float Size { get; init; }

    /// <summary>Centre of the mark on the inline axis.</summary>
    public float X { get; init; }

    /// <summary>Centre of the mark on the block axis.</summary>
    public float CenterY { get; init; }

    /// <summary>Which side the mark was placed on.</summary>
    public EmphasisSide Side { get; init; }

    /// <summary>Why it sits where it sits, for a dump.</summary>
    public string? Reason { get; init; }
}
