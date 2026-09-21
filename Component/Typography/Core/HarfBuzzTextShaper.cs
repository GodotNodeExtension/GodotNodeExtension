using System;
using System.Collections.Generic;
using GodotNodeExtension.Component.GodotSkia;
using GodotNodeExtension.Component.Typography.Core.Fonts;
using GodotNodeExtension.Component.Typography.Core.Model;
using SkiaSharp;

namespace GodotNodeExtension.Component.Typography.Core;

/// <summary>
/// Shapes text on one typeface and one font size into the glyphs the layout measures with and the renderer
/// draws, splitting the text into runs where the typeface cannot cover a character and shaping the rest with
/// a fallback face.
/// <para>
/// The shaping itself is done by <see cref="GlyphIdResolver"/> (HarfBuzz), which reports advances, offsets
/// and glyph indices from a single pass. There used to be a second shaper for the advances (Skia's), which
/// could not be told about a script, a language or a feature and whose results had to be reconciled with
/// HarfBuzz's by glyph count; both are gone, and a font's language-specific forms are now reachable.
/// </para>
/// <para>
/// Each instance is bound to a (typeface, font size, shaping options) triple. Not documented as thread-safe:
/// in practice each instance is used only from the typography server thread (see
/// <see cref="ContentPreparer"/>), and HarfBuzz's font object holds reusable state, so an instance must not
/// be shared across threads.
/// </para>
/// </summary>
public class HarfBuzzTextShaper : IDisposable
{
    private readonly SKFont _font;
    private readonly SKTypeface _typeface;
    private readonly float _fontSize;
    private readonly ShapingOptions _options;
    private bool _disposed;

    /// <summary>
    /// Create a shaper from a SkiaSharp typeface, a font size and the shaping options a language asks for.
    /// </summary>
    /// <param name="typeface">The SKTypeface to shape text with.</param>
    /// <param name="fontSize">Font size in pixels.</param>
    /// <param name="options">Script, language and features the shaping honours.</param>
    public HarfBuzzTextShaper(SKTypeface typeface, float fontSize, in ShapingOptions options)
    {
        _typeface = typeface ?? throw new ArgumentNullException(nameof(typeface));
        _fontSize = fontSize;
        _options = options;
        // Single font-configuration entry point (hinting/edging/subpixel), shared with the chart and
        // rich-text paths so the metrics the layout uses are the ones the renderer draws with.
        _font = SkiaGodotConverter.ConfigureFont(new SKFont(typeface, fontSize));
    }

    /// <summary>
    /// The typeface this shaper is configured for.
    /// </summary>
    public SKTypeface Typeface => _typeface;

    /// <summary>
    /// The font size this shaper is configured for.
    /// </summary>
    public float FontSize => _fontSize;

    /// <summary>
    /// The shaping options this shaper is configured for. Part of its identity: a shaper for one language
    /// must not be reused for another, or the second language would silently get the first one's forms.
    /// </summary>
    public ShapingOptions Options => _options;

    /// <summary>
    /// Get font metrics (ascent as positive, descent as positive, line height).
    /// </summary>
    /// <returns>The vertical metrics of the configured font.</returns>
    public (float Ascent, float Descent, float LineHeight) GetMetrics()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var metrics = _font.Metrics;
        var ascent = -metrics.Ascent;   // SKFontMetrics.Ascent is negative
        var descent = metrics.Descent;
        var lineHeight = ascent + descent + metrics.Leading;
        return (ascent, descent, lineHeight);
    }

    /// <summary>
    /// Shape a run into glyphs: advances and glyph indices come from the same shaping pass, so they cannot
    /// drift apart.
    /// <para>
    /// Ids are resolved per run rather than once for the whole string because the fallback path changes
    /// typeface mid-string: a glyph index only means something together with the face it belongs to.
    /// </para>
    /// </summary>
    /// <param name="text">The text to shape.</param>
    /// <param name="primaryFontId">
    /// Catalog id of the font this shaper was created for; stamped on every glyph shaped with it.
    /// </param>
    /// <param name="fontManager">Font manager for fallback resolution. Null uses the default.</param>
    /// <returns>The shaped glyphs, in drawing order; empty for empty text.</returns>
    public Glyph[] ShapeGlyphs(string text, ulong primaryFontId, SKFontManager? fontManager = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (string.IsNullOrEmpty(text))
            return [];

        fontManager ??= SKFontManager.Default;

        var result = new List<Glyph>(text.Length);
        ushort[] coverage = _typeface.GetGlyphs(text);
        SKFontStyle style = _typeface.FontStyle;

        // GetGlyphs answers once per *code point*, while the run loop below walks UTF-16 units: indexing it by the
        // latter walks off the end as soon as the text contains a character outside the basic plane (an emoji, any
        // astral script). Expanding it to one flag per unit also keeps a surrogate pair from being cut in half,
        // because both of its units carry the same flag.
        var hasGlyph = new bool[text.Length];
        int codePoint = 0;

        for (int i = 0; i < text.Length; i++)
        {
            bool covered = codePoint < coverage.Length && coverage[codePoint] != 0;
            hasGlyph[i] = covered;

            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                hasGlyph[i + 1] = covered;
                i++;
            }

            codePoint++;
        }

        int runStart = 0;

        while (runStart < text.Length)
        {
            bool missing = !hasGlyph[runStart];
            int runEnd = runStart;

            while (runEnd < text.Length && hasGlyph[runEnd] == !missing)
                runEnd++;

            string runText = text[runStart..runEnd];

            if (!missing)
            {
                AppendShapedRun(result, runText, runStart, _typeface, primaryFontId);
            }
            else
            {
                // The fallback is matched on a code point, so a surrogate pair is looked up as the character it
                // spells rather than as half of one.
                int probe = char.IsHighSurrogate(text[runStart]) && runStart + 1 < text.Length
                    && char.IsLowSurrogate(text[runStart + 1])
                        ? char.ConvertToUtf32(text[runStart], text[runStart + 1])
                        : text[runStart];

                SKTypeface? fallback = fontManager.MatchCharacter(null, style, null, probe);

                if (fallback != null)
                {
                    // A fallback face is not in the catalog yet: register it so the glyphs can name it and
                    // the renderer can resolve it back.
                    AppendShapedRun(result, runText, runStart, fallback,
                        FontCatalog.Shared.RegisterTypeface(fallback));
                }
                else
                {
                    // No fallback found: the primary font draws its .notdef, as before.
                    AppendShapedRun(result, runText, runStart, _typeface, primaryFontId);
                }
            }

            runStart = runEnd;
        }

        return [.. result];
    }

    /// <summary>
    /// Shape one run and append its glyphs, converting the source offsets of the run into offsets within the
    /// element's text.
    /// </summary>
    /// <param name="target">Glyph list being built.</param>
    /// <param name="runText">The run's text.</param>
    /// <param name="runOffset">Offset of the run within the element's text.</param>
    /// <param name="typeface">Typeface that shapes this run.</param>
    /// <param name="fontId">Catalog id of that typeface, stamped on the glyphs.</param>
    private void AppendShapedRun(
        List<Glyph> target,
        string runText,
        int runOffset,
        SKTypeface typeface,
        ulong fontId)
    {
        GlyphIdResolver resolver = GlyphIdResolver.For(typeface);
        resolver.Shape(
            runText,
            _fontSize,
            _options,
            out uint[] ids,
            out float[] advances,
            out float[] offsetX,
            out float[] offsetY,
            out int[] clusters);

        for (int i = 0; i < ids.Length; i++)
        {
            // Clusters are reported in source order (the buffer is shaped monotone), so the next glyph's
            // cluster is where this glyph's coverage ends; the last glyph covers the rest of the run.
            int clusterStart = Math.Clamp(clusters[i], 0, runText.Length);
            int clusterEnd = i + 1 < clusters.Length
                ? Math.Clamp(clusters[i + 1], clusterStart, runText.Length)
                : runText.Length;

            if (clusterEnd <= clusterStart)
                clusterEnd = Math.Min(runText.Length, clusterStart + 1);

            target.Add(new Glyph
            {
                Id = ids[i],
                FontId = fontId,
                Advance = advances[i],
                OffsetX = offsetX[i],
                OffsetY = offsetY[i],
                ClusterStart = runOffset + clusterStart,
                ClusterEnd = runOffset + clusterEnd,
            });
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _font.Dispose();
        GC.SuppressFinalize(this);
    }
}
