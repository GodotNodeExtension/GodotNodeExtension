namespace GodotNodeExtension.Component.Typography.Core.Model;

/// <summary>
/// A half-open range <c>[Start, End)</c> of source text coordinates.
/// <para>
/// Coordinates are UTF-16 code unit indices (the indexing C# strings and Godot's APIs use), not
/// Unicode scalar values and not bytes. When a range is carried by a <see cref="LayoutElement"/>,
/// it is relative to the text of the element identified by <see cref="LayoutElement.SourceIndex"/>,
/// not to the whole document.
/// </para>
/// <para>
/// Grapheme and cluster boundaries are deliberately modelled with the same type but never mixed
/// with character offsets: callers must not assume that a range of length N covers N visible
/// characters, because combining marks, CRLF pairs and ZWJ emoji sequences are indivisible units.
/// </para>
/// </summary>
/// <param name="Start">Inclusive start index.</param>
/// <param name="End">Exclusive end index.</param>
public readonly record struct TextRange(int Start, int End)
{
    /// <summary>Number of code units covered by this range.</summary>
    public int Length => End - Start;

    /// <summary>Whether this range covers no code units.</summary>
    public bool IsEmpty => End <= Start;

    /// <summary>An empty range at the start of the text.</summary>
    public static readonly TextRange Empty = new(0, 0);

    /// <inheritdoc />
    public override string ToString() => $"[{Start},{End})";
}
