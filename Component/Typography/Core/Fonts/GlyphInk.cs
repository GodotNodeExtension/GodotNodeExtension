using GodotNodeExtension.Component.Typography.Core.Model;
using System.Collections.Generic;
using SkiaSharp;

namespace GodotNodeExtension.Component.Typography.Core.Fonts;

/// <summary>
/// Ink extents of a shaped run: the box the glyphs actually paint, measured from the run's own origin.
/// <para>
/// The layout's metrics are line metrics - the font's ascent and descent, which describe the box a line reserves rather
/// than where the ink actually is. That is the right answer for the text itself, but not for everything: an annotation
/// band has to hold the annotation's ink, and a tone mark (pinyin's <c>ā</c>, Bopomofo's mark in its upper right) rises
/// above the annotation font's ascent, so a band measured with line metrics hides part of the mark it exists for. The
/// ink is measured from the glyph outlines, which is what the renderer draws.
/// </para>
/// <para>
/// Two things make the measurement more than a loop over outlines. A run is not necessarily drawn with one face - the
/// fallback path shapes the parts of the text the primary face cannot cover with another face - so each glyph is looked
/// up in the face its own <see cref="Glyph.FontId"/> names; measuring them in one face would look up glyph indices that
/// mean something else there, and the ink of an annotation whose script the element's own font lacks (Bopomofo beside a
/// Han character, for instance) came out as nothing at all. And a run is read in its own direction: along a line the pen
/// walks x and the box is stated from the baseline, down a column it walks y and the box is stated from the column's
/// own line, which is what the renderer does with the same numbers.
/// </para>
/// </summary>
internal static class GlyphInk
{
    /// <summary>
    /// Measure the box a run's glyphs paint, in the frame the run is drawn in.
    /// <para>
    /// Along a line the box is stated from the pen and the baseline: <c>Left</c>/<c>Right</c> from the run's start,
    /// <c>Top</c>/<c>Bottom</c> from the baseline, growing downward. Down a column it is stated from the column's own
    /// line and the run's start: <c>Left</c>/<c>Right</c> from the column's centre line, <c>Top</c>/<c>Bottom</c> from
    /// the top of the run, growing down it. Both are positive-down, i.e. the renderer's coordinates, so a caller that
    /// has to move a glyph can subtract the two boxes instead of restating the convention.
    /// </para>
    /// </summary>
    /// <param name="fontSize">Size the glyphs are drawn at.</param>
    /// <param name="glyphs">Shaped glyphs, in drawing order.</param>
    /// <param name="vertical">Whether the run is set down a column rather than along a line.</param>
    /// <returns>The ink box; all four are 0 when no glyph of the run has an outline to measure.</returns>
    public static (float Left, float Top, float Right, float Bottom) Measure(
        float fontSize, IReadOnlyList<Glyph> glyphs, bool vertical)
    {
        float left = 0f;
        float top = 0f;
        float right = 0f;
        float bottom = 0f;
        float pen = 0f;
        bool any = false;

        foreach (Glyph glyph in glyphs)
        {
            // A text blob addresses glyphs with 16 bits, and the ink of a larger glyph index is not what a renderer
            // would draw either.
            if (glyph.Id <= ushort.MaxValue
                && FontCatalog.Shared.TryGetTypeface(glyph.FontId, out SKTypeface? typeface)
                && typeface is not null)
            {
                using var font = new SKFont(typeface, fontSize);
                using SKPath? path = font.GetGlyphPath((ushort)glyph.Id);

                if (path is not null)
                {
                    // The path is in the glyph's own frame, its baseline at y = 0 and its origin at x = 0, and the
                    // shaper's offsets move that frame off the pen: the ink of a glyph is its outline bounds shifted
                    // by where the glyph was placed. Across the column the pen does not move at all, and along it the
                    // drawable baseline sits one negated offset below the pen (see DrawGlyphColumn).
                    SKRect bounds = path.Bounds;
                    float x = (vertical ? 0f : pen) + glyph.OffsetX;
                    float y = vertical ? pen - glyph.OffsetY : glyph.OffsetY;

                    if (!any)
                    {
                        left = x + bounds.Left;
                        right = x + bounds.Right;
                        top = y + bounds.Top;
                        bottom = y + bounds.Bottom;
                        any = true;
                    }
                    else
                    {
                        left = System.MathF.Min(left, x + bounds.Left);
                        right = System.MathF.Max(right, x + bounds.Right);
                        top = System.MathF.Min(top, y + bounds.Top);
                        bottom = System.MathF.Max(bottom, y + bounds.Bottom);
                    }
                }
            }

            pen += glyph.Advance;
        }

        return (left, top, right, bottom);
    }
}
