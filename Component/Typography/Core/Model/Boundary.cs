namespace GodotNodeExtension.Component.Typography.Core.Model;

/// <summary>
/// What a boundary between two clusters is about: the reason the pair needs a decision at all.
/// </summary>
public enum BoundaryKind
{
    /// <summary>Two ordinary neighbours inside one run; no rule applies.</summary>
    Plain,

    /// <summary>At least one side is whitespace.</summary>
    SpaceRun,

    /// <summary>The two sides belong to different scripts (CJK next to Latin is the common case).</summary>
    ScriptChange,

    /// <summary>At least one side is punctuation.</summary>
    Punctuation,

    /// <summary>The pair is a number with an affix, a unit or a currency symbol; it must not split.</summary>
    Numeric,

    /// <summary>At least one side is a non-text object (image, rule, block marker).</summary>
    InlineObject,

    /// <summary>A paragraph or hard line break sits here.</summary>
    HardBreak,
}

/// <summary>
/// The script role of a cluster, as far as boundary decisions are concerned. Deliberately coarser than
/// Unicode script: it names what a boundary rule needs to ask ("is this CJK, Latin, a digit, a space …"),
/// and the CJK group is not split into Han/Kana/Hangul yet because nothing distinguishes them today.
/// </summary>
public enum ScriptRole
{
    /// <summary>Character class carries no script-bearing information (break, marker, unknown).</summary>
    Neutral,

    /// <summary>CJK ideograph, kana or hangul, as far as the current classifier can tell.</summary>
    Han,

    /// <summary>A Latin letter or word character.</summary>
    LatinLetter,

    /// <summary>A digit.</summary>
    Digit,

    /// <summary>Whitespace.</summary>
    Space,

    /// <summary>Punctuation of any class.</summary>
    Punctuation,

    /// <summary>A non-text object (image, rule, block marker).</summary>
    InlineObject,

    /// <summary>A Common mark used in a Western context: a quotation mark, a dash or an ellipsis.</summary>
    WesternPunctuation,
}

/// <summary>
/// Which side of a boundary owns an adjustment. Adjustment used to be modelled as "the width of an
/// element", which cannot express that a piece of space belongs to the gap rather than to either
/// glyph; naming the owner is what makes gaps movable and measurable.
/// </summary>
public enum BoundaryOwner
{
    /// <summary>The left cluster carries the adjustment on its trailing edge (the current behaviour).</summary>
    Left,

    /// <summary>The right cluster carries it on its leading edge.</summary>
    Right,

    /// <summary>Both sides share it.</summary>
    Both,
}

/// <summary>
/// A decision point between two adjacent clusters, carrying everything the break and adjustment stages
/// need in one place.
/// <para>
/// This replaces the old arrangement where the same question ("may I break here?", "is there a gap
/// here?", "may this gap stretch?") was answered by re-deriving it from character classes in three
/// different stages. A boundary is computed once per layout in the compile phase, is width-independent,
/// and carries a <see cref="Reason"/> so a diff can explain itself.
/// </para>
/// <para>
/// P1 status: boundaries are produced and dumped, but the break and adjustment stages do not consume
/// them yet — they still apply their own rules, so behaviour is unchanged. Consuming them is the next
/// step, and the golden dumps are what make that step reviewable.
/// </para>
/// </summary>
public readonly struct Boundary
{
    /// <summary>Index of the cluster on the left of this boundary.</summary>
    public int LeftCluster { get; init; }

    /// <summary>Index of the cluster on the right of this boundary.</summary>
    public int RightCluster { get; init; }

    /// <summary>What kind of decision this boundary carries.</summary>
    public BoundaryKind Kind { get; init; }

    /// <summary>Script role of the left cluster.</summary>
    public ScriptRole LeftScript { get; init; }

    /// <summary>Script role of the right cluster.</summary>
    public ScriptRole RightScript { get; init; }

    /// <summary>Side that owns any adjustment applied here.</summary>
    public BoundaryOwner Owner { get; init; }

    /// <summary>
    /// Natural spacing between the two clusters in pixels, on top of their advances. Zero when the
    /// clusters already carry their own separation (a space cluster, for instance).
    /// </summary>
    public float BaseSpacing { get; init; }

    /// <summary>
    /// Smallest spacing this boundary may be squeezed to. Equal to <see cref="BaseSpacing"/> until the
    /// adjustment stage is moved onto boundaries, at which point the profile supplies the range.
    /// </summary>
    public float MinSpacing { get; init; }

    /// <summary>
    /// Largest spacing this boundary may be stretched to. Equal to <see cref="BaseSpacing"/> until the
    /// adjustment stage is moved onto boundaries.
    /// </summary>
    public float MaxSpacing { get; init; }

    /// <summary>Whether a line may not start at the right cluster of this boundary.</summary>
    public bool ForbiddenAtLineStart { get; init; }

    /// <summary>Whether a line may not end at the left cluster of this boundary.</summary>
    public bool ForbiddenAtLineEnd { get; init; }

    /// <summary>Whether the two clusters must stay on the same line.</summary>
    public bool ForbiddenToBreak { get; init; }

    /// <summary>Whether the adjustment stages may not add space here (separation prohibition).</summary>
    public bool ForbiddenToStretch { get; init; }

    /// <summary>Machine-readable explanation of how this boundary was decided.</summary>
    public string Reason { get; init; }

    /// <summary>
    /// Whether this boundary carries any decision a reader of a dump would care about: a prohibition, a
    /// spacing, or a non-plain kind. Plain neighbours are the majority and only add noise.
    /// </summary>
    public bool IsNotable =>
        Kind != BoundaryKind.Plain
        || ForbiddenAtLineStart
        || ForbiddenAtLineEnd
        || ForbiddenToBreak
        || ForbiddenToStretch
        || BaseSpacing != 0f;

    /// <inheritdoc />
    public override string ToString() =>
        $"[{LeftCluster}|{RightCluster}] {Kind} {LeftScript}->{RightScript} " +
        $"spacing={BaseSpacing:F3} break={!ForbiddenToBreak} " +
        $"start={ForbiddenAtLineStart} end={ForbiddenAtLineEnd} {Reason}";
}
