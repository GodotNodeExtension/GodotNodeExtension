using System;
using GodotNodeExtension.Component.Typography.Core.Model;

namespace GodotNodeExtension.Component.Typography.Core.Fonts;

/// <summary>What a Bopomofo tone mark is, which decides where clreq §5.5.3.3 puts it.</summary>
internal enum BopomofoToneKind
{
    /// <summary>Not a tone mark.</summary>
    None,

    /// <summary>Mandarin non-neutral tone (平上去) or a dialectal non-checked one: <c>ˊˇˋˉ</c>, <c>˪˫</c>.</summary>
    NonNeutral,

    /// <summary>Dialectal checked tone (入声): <c>ㆴㆵㆶㆷ</c>.</summary>
    Checked,

    /// <summary>Mandarin neutral tone: <c>˙</c>.</summary>
    Neutral,
}

/// <summary>
/// Where the tone marks of a Bopomofo annotation go (clreq §5.5.3.3), applied to the glyphs the shaper produced.
/// <para>
/// A tone mark is not another symbol of the reading. The convention puts it against the corner of the last phonetic
/// symbol - outside its upper right for 平上去, outside its lower right for the checked tones - and the neutral tone
/// before the reading, in a cell an order of magnitude thinner than a symbol. A shaper knows none of that: it hands
/// back a tone mark as a glyph with a symbol's advance, so a reading set that way is one cell too long and its mark
/// lands where the next symbol would have gone, which is what made <c>ㄓㄨˋ</c> read as three cells and put <c>˙</c>
/// half a base character wide. This class turns the mark's own ink into the placement the convention describes and
/// takes its advance away, which is what makes the reading occupy the two cells <c>ㄓㄨˋ</c> should.
/// </para>
/// <para>
/// The mark is placed by its ink rather than by an em box because the stroke is what a reader sees: the reference
/// figures of clreq §5.5.3.3 place the mark's own box against the symbol's corner, and a face whose tone mark is a
/// narrow dash and one whose mark is wide both end up at the corner this way.
/// </para>
/// </summary>
internal static class BopomofoTone
{
    /// <summary>
    /// Room the neutral tone mark takes along the reading direction, as a fraction of the base character's size: the
    /// height in vertical Bopomofo and the width in horizontal Bopomofo (clreq §5.5.3.2's note, 1:15).
    /// </summary>
    internal const float NeutralCellRatio = 1f / 15f;

    /// <summary>Whether the annotation text is Bopomofo at all, i.e. whether its tone marks are Bopomofo's.</summary>
    /// <param name="text">Annotation text of one piece.</param>
    /// <returns>True when the text holds a Bopomofo symbol.</returns>
    internal static bool ContainsBopomofo(string text)
    {
        foreach (char c in text)
        {
            // The symbols of Mandarin (U+3100–U+312F) plus the extended block the dialectal letters and the checked
            // tone marks live in (U+31A0–U+31BF).
            if (c is >= '\u3100' and <= '\u312F' or >= '\u31A0' and <= '\u31BF')
                return true;
        }

        return false;
    }

    /// <summary>Classify one character.</summary>
    /// <param name="c">Character to classify.</param>
    /// <returns>What kind of tone mark it is, or <see cref="BopomofoToneKind.None"/>.</returns>
    internal static BopomofoToneKind KindOf(char c) => c switch
    {
        // 平上去 and the dialectal non-checked tones: clreq §5.5.3.2's note lists them as ˊˇˋ˪˫; the macron of the
        // level tone is included because a caller who writes it means the same thing by it.
        '\u02CA' or '\u02C7' or '\u02CB' or '\u02C9' or '\u02EA' or '\u02EB' => BopomofoToneKind.NonNeutral,

        // The checked tones of the dialectal systems.
        '\u31B4' or '\u31B5' or '\u31B6' or '\u31B7' => BopomofoToneKind.Checked,

        // The neutral tone, which writes the same dot as pinyin's neutral tone.
        '\u02D9' => BopomofoToneKind.Neutral,

        _ => BopomofoToneKind.None,
    };

    /// <summary>
    /// Place the tone marks of one annotation piece: take their advance away and move their ink to the corner the
    /// convention gives them.
    /// </summary>
    /// <param name="glyphs">Shaped glyphs of the piece.</param>
    /// <param name="text">The piece's text, which the glyphs' cluster indices point into.</param>
    /// <param name="cell">The annotation's size in pixels: one symbol's cell in the reading direction.</param>
    /// <param name="baseSize">The annotated text's font size in pixels.</param>
    /// <param name="vertical">Whether the piece is set down a column rather than along a line.</param>
    /// <param name="ascent">Ascent of the annotation's font, for the along-the-line placement.</param>
    /// <param name="descent">Descent of the annotation's font, for the along-the-line placement.</param>
    /// <returns>The glyphs to draw: the same ones, with the marks' advances and offsets adjusted.</returns>
    internal static Glyph[] Place(
        Glyph[] glyphs,
        string text,
        float cell,
        float baseSize,
        bool vertical,
        float ascent,
        float descent)
    {
        if (glyphs.Length == 0 || !ContainsBopomofo(text))
            return glyphs;

        for (int g = 0; g < glyphs.Length; g++)
        {
            Glyph glyph = glyphs[g];
            BopomofoToneKind kind = KindOfGlyph(text, glyph);

            if (kind == BopomofoToneKind.None)
                continue;

            // The mark's own ink, in the frame the piece is drawn in: from the column's centre line and the mark's
            // own pen down a column, from the pen and the baseline along a line.
            (float left, float top, float right, float bottom) = GlyphInk.Measure(cell, [glyph], vertical);
            float inkWidth = right - left;
            float inkHeight = bottom - top;

            float advance;
            float targetLeft;
            float targetTop;

            if (kind == BopomofoToneKind.Neutral)
            {
                // clreq §5.5.3.3: the neutral tone comes before the phonetic symbols, so its cell is the piece's
                // first. It is a thin one - §5.5.3.2's note keeps the mark's width and takes 1:15 of the base
                // character along the reading direction - and the dot is centred in it rather than hung off a corner.
                advance = MathF.Max(baseSize * NeutralCellRatio, 0f);

                if (vertical)
                {
                    // The thin cell is along the column, so the dot keeps the column's own line across it.
                    targetLeft = -inkWidth * 0.5f;
                    targetTop = (advance - inkHeight) * 0.5f;
                }
                else
                {
                    targetLeft = (advance - inkWidth) * 0.5f;
                    targetTop = ((descent - ascent) * 0.5f) - (inkHeight * 0.5f);
                }
            }
            else
            {
                // 平上去 and the checked tones take no cell of their own: the mark hangs beside the last symbol, and
                // the reading keeps the length of its symbols alone.
                advance = 0f;

                // Where that corner is: the last symbol's cell ends where the mark's own pen is, so the top edge of
                // that cell is one of the symbol's advances above it. A mark with no symbol in front of it falls
                // back to the annotation's own cell, which is all there is to hang off.
                float cornerAbove = g > 0 ? glyphs[g - 1].Advance : cell;

                if (vertical)
                {
                    // Outside the column, at the symbol's upper right (a checked tone at its lower right, clreq
                    // §5.5.3.3): the ink starts where the symbol's cell ends and straddles the corner of its top edge.
                    targetLeft = cell * 0.5f;
                    targetTop = kind == BopomofoToneKind.Checked
                        ? -inkHeight
                        : -cornerAbove - (inkHeight * 0.5f);
                }
                else
                {
                    // Along the line the same corner is the symbol's: half the mark's space to the right of it
                    // (clreq §5.5.3.3's horizontal Bopomofo) and its ink against the top or bottom of the
                    // annotation's own box.
                    targetLeft = -inkWidth * 0.5f;
                    targetTop = kind == BopomofoToneKind.Checked
                        ? descent - inkHeight
                        : -ascent;
                }
            }

            float dx = targetLeft - left;
            float dy = targetTop - top;

            glyphs[g] = glyph with
            {
                Advance = advance,
                OffsetX = glyph.OffsetX + dx,

                // Down a column the drawable baseline is the pen minus the offset, so the ink follows a change in the
                // offset the other way round; along a line the offset is the shift itself.
                OffsetY = vertical ? glyph.OffsetY - dy : glyph.OffsetY + dy,
            };
        }

        return glyphs;
    }

    /// <summary>Classify the character the glyph was shaped from.</summary>
    /// <param name="text">Text of the piece the glyph belongs to.</param>
    /// <param name="glyph">Glyph whose cluster the character is read from.</param>
    /// <returns>The kind of tone mark, or <see cref="BopomofoToneKind.None"/>.</returns>
    private static BopomofoToneKind KindOfGlyph(string text, in Glyph glyph) =>
        glyph.ClusterStart >= 0 && glyph.ClusterStart < text.Length
            ? KindOf(text[glyph.ClusterStart])
            : BopomofoToneKind.None;
}
