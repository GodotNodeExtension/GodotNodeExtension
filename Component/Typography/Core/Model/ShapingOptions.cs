using System;
using System.Collections.Generic;

namespace GodotNodeExtension.Component.Typography.Core.Model;

/// <summary>
/// What shaping needs to know besides the text, the face and the font size: the script the text is written
/// in, the language whose OpenType conventions apply, and any features the caller wants on or off.
/// <para>
/// This exists because shaping is where a language shows up first. HarfBuzz selects a font's localized forms
/// (<c>locl</c>) from the language and its shaping model from the script, so a pipeline that shapes before it
/// knows the language can only ever produce whichever forms happen to be the font's default. The engine used
/// to be in exactly that position: the language was resolved during layout, long after measuring.
/// </para>
/// <para>
/// Value equality is required here because the shaper cache keys on these options — two segments that ask for
/// the same script, language and features may share one shaper, and a segment that asks for a different
/// language must not.
/// </para>
/// </summary>
public readonly struct ShapingOptions : IEquatable<ShapingOptions>
{
    /// <summary>
    /// OpenType script tag (for example <c>Hani</c>, <c>Latn</c>). Null lets HarfBuzz guess from the text,
    /// which is the right answer for a run that is already split by script.
    /// </summary>
    public string? Script { get; init; }

    /// <summary>
    /// OpenType language tag (for example <c>ZHS</c>, <c>ZHT</c>, <c>JAN</c>, <c>KOR</c>). Null means "no
    /// language preference" — the font's default forms, which is what a document without a declared language
    /// has always been shaped with.
    /// </summary>
    public string? Language { get; init; }

    /// <summary>
    /// Features to turn on or off, in HarfBuzz syntax (for example <c>palt=1</c>, <c>liga=0</c>). Empty uses
    /// the font's defaults, plus whatever the language implies (which is how <c>locl</c> gets applied).
    /// </summary>
    public IReadOnlyList<string>? Features { get; init; }

    /// <summary>
    /// Direction the run is written in: <see cref="TextDirection.LeftToRight"/> (the default, and the value a
    /// default-constructed struct carries) or <see cref="TextDirection.RightToLeft"/>. Arabic and Hebrew shape right
    /// to left — the glyph order and the cluster mapping both follow it — so a shaper that is never told the
    /// direction can only produce left-to-right runs.
    /// </summary>
    public TextDirection Direction { get; init; }

    /// <summary>
    /// Whether the run is set vertically, i.e. its inline axis runs down the page.
    /// <para>
    /// Shaping has to know: in the top-to-bottom direction HarfBuzz reports the inline distance as a negative
    /// <c>YAdvance</c> instead of a positive <c>XAdvance</c>, and it measures a glyph's origin from a vertical
    /// reference point. Measured on this machine at 16px (1 em = 1024 units): a CJK face whose <c>vmtx</c> says so
    /// comes back as -1024 (one em), and a face without <c>vmtx</c> still gets a synthesised value from its
    /// horizontal metrics.
    /// </para>
    /// </summary>
    public bool IsVertical { get; init; }

    /// <summary>
    /// Whether these options ask for anything a default shaping would not do. A shaper created for a default
    /// request is reusable for every language that does not name its own forms.
    /// </summary>
    public bool IsDefault =>
        string.IsNullOrEmpty(Script) && string.IsNullOrEmpty(Language) && (Features is null || Features.Count == 0)
        && Direction == TextDirection.LeftToRight && !IsVertical;

    /// <summary>
    /// Compare two option sets by value: script and language by ordinal comparison, features in order.
    /// </summary>
    /// <param name="other">The options to compare against.</param>
    /// <returns>True when both ask shaping for the same thing.</returns>
    public bool Equals(ShapingOptions other)
    {
        if (!string.Equals(Script, other.Script, StringComparison.Ordinal))
            return false;

        if (!string.Equals(Language, other.Language, StringComparison.Ordinal))
            return false;

        if (Direction != other.Direction)
            return false;

        if (IsVertical != other.IsVertical)
            return false;

        int count = Features?.Count ?? 0;
        if (count != (other.Features?.Count ?? 0))
            return false;

        for (int i = 0; i < count; i++)
        {
            if (!string.Equals(Features![i], other.Features![i], StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is ShapingOptions other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Script, StringComparer.Ordinal);
        hash.Add(Language, StringComparer.Ordinal);
        hash.Add(Direction);
        hash.Add(IsVertical);

        if (Features is { } features)
        {
            for (int i = 0; i < features.Count; i++)
                hash.Add(features[i], StringComparer.Ordinal);
        }

        return hash.ToHashCode();
    }

    /// <summary>Value equality of two option sets.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns>True when both ask shaping for the same thing.</returns>
    public static bool operator ==(ShapingOptions left, ShapingOptions right) => left.Equals(right);

    /// <summary>Value inequality of two option sets.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns>True when the two ask shaping for different things.</returns>
    public static bool operator !=(ShapingOptions left, ShapingOptions right) => !left.Equals(right);
}
