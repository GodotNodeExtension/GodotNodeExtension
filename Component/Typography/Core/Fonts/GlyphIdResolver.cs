using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using GodotNodeExtension.Component.Typography.Core.Model;
using HarfBuzzSharp;
using SkiaSharp;
using SkiaSharp.HarfBuzz;

namespace GodotNodeExtension.Component.Typography.Core.Fonts;

/// <summary>
/// Shapes text on a typeface and reports everything the layout needs from that shaping: glyph indices,
/// advances, offsets and the cluster mapping.
/// <para>
/// The engine used to shape twice — Skia's shaper for the advances, this class for the glyph indices — and
/// reconciled the two by asserting that they produced the same glyph count. That reconciliation is gone:
/// one pass now answers both questions, so an advance can never describe a different glyph than the one the
/// renderer draws, and the language and features a shaping needs can actually be passed (Skia's shaper takes
/// neither, which is why a font's localized forms were unreachable).
/// </para>
/// <para>
/// Instances are cached per typeface because the face and font objects hold native state. The class is not
/// thread-safe by itself: like the rest of the layout machinery, it is used from one thread at a time.
/// </para>
/// </summary>
public sealed class GlyphIdResolver : IDisposable
{
    private static readonly ConcurrentDictionary<IntPtr, GlyphIdResolver> SCache = new();

    /// <summary>
    /// Release the cached resolvers when the load context unloads.
    /// <para>
    /// Each resolver owns HarfBuzz objects (blob, face, font) whose native side holds references back into
    /// this assembly. A resolver that stays cached therefore keeps the assembly - and the load context that
    /// owns it - reachable past an editor reload, which the engine reports as
    /// `Failed to unload assemblies` (godot#78513). The cache is filled on first use, so the hook is what
    /// makes the objects releasable; nothing else disposes them.
    /// </para>
    /// </summary>
    static GlyphIdResolver()
    {
        var context = System.Runtime.Loader.AssemblyLoadContext
            .GetLoadContext(typeof(GlyphIdResolver).Assembly);
        if (context is null) return;

        context.Unloading += _ => ResetStaticState();
        UnloadHookInstalled = true;
    }

    /// <summary>Whether the unload hook is registered. Test seam (the event cannot be raised in-process).</summary>
    internal static bool UnloadHookInstalled { get; private set; }

    /// <summary>How many resolvers the cache holds. Test seam for <see cref="ResetStaticState"/>.</summary>
    internal static int CachedResolverCount => SCache.Count;

    /// <summary>
    /// Dispose every cached resolver and empty the cache. Called automatically when the load context
    /// unloads; safe to call again afterwards (the cache simply refills on the next use).
    /// </summary>
    internal static void ResetStaticState()
    {
        foreach (GlyphIdResolver resolver in SCache.Values)
        {
            resolver.Dispose();
        }

        SCache.Clear();
    }

    /// <summary>
    /// The typeface the font data behind <see cref="_blob"/> came from. Held so the face cannot outlive the
    /// object it belongs to.
    /// </summary>
    private readonly SKTypeface _typeface;

    /// <summary>
    /// The stream the blob's font data lives in. It has to outlive the blob, and it is what actually owns
    /// the data: <c>SKStreamAsset.ToHarfBuzzBlob()</c> does not copy the font data, it points the blob at the
    /// stream's memory base. Keeping only the typeface alive was not enough — a 20 MB CJK face whose stream
    /// had already been released faulted inside HarfBuzz while shaping, and whether it faulted depended on
    /// whether something else still held that memory.
    /// </summary>
    private readonly SKStreamAsset _stream;

    private readonly Blob _blob;
    private readonly Face _face;
    private readonly Font _font;
    private bool _disposed;

    private GlyphIdResolver(SKTypeface typeface)
    {
        _typeface = typeface;

        // Not disposed here: the blob points into the stream's memory, so the stream lives as long as the
        // resolver (and is released after the blob, in Dispose).
        _stream = typeface.OpenStream(out int faceIndex);

        _blob = _stream.ToHarfBuzzBlob();
        _face = new Face(_blob, faceIndex);
        _font = new Font(_face);
    }

    /// <summary>
    /// Obtain a resolver for a typeface, creating it on first use.
    /// </summary>
    /// <param name="typeface">The Skia typeface to shape with.</param>
    /// <returns>A cached resolver; do not dispose it, the cache owns it.</returns>
    /// <exception cref="ArgumentNullException">When <paramref name="typeface"/> is null.</exception>
    public static GlyphIdResolver For(SKTypeface typeface)
    {
        ArgumentNullException.ThrowIfNull(typeface);

        return SCache.GetOrAdd(typeface.Handle, _ => new GlyphIdResolver(typeface));
    }

    /// <summary>
    /// Shape a text run and return everything the layout and the renderer need from it: glyph indices,
    /// advances, offsets and cluster assignment, all taken from this one shaping pass.
    /// </summary>
    /// <param name="text">The text to shape.</param>
    /// <param name="fontSize">Font size in pixels.</param>
    /// <param name="options">Script, language and features the shaping should honour.</param>
    /// <param name="glyphIds">Output: one glyph index per shaped glyph, in drawing order.</param>
    /// <param name="advances">Output: horizontal advance per glyph, in pixels.</param>
    /// <param name="offsetX">Output: horizontal offset from the pen to the glyph origin, in pixels.</param>
    /// <param name="offsetY">Output: vertical offset from the baseline to the glyph origin, in pixels.</param>
    /// <param name="clusters">Output: the source offset each glyph belongs to, parallel to the ids.</param>
    /// <exception cref="ArgumentException">When <paramref name="text"/> is empty.</exception>
    public void Shape(
        string text,
        float fontSize,
        in ShapingOptions options,
        out uint[] glyphIds,
        out float[] advances,
        out float[] offsetX,
        out float[] offsetY,
        out int[] clusters)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrEmpty(text);

        // HarfBuzz works in integer font units; scaling by 64 keeps a 1/64 px resolution instead of
        // rounding advances to whole pixels.
        const float subPixelScale = 64f;
        int scaled = (int)MathF.Round(fontSize * subPixelScale);
        _font.SetScale(scaled, scaled);

        using var buffer = new HarfBuzzSharp.Buffer();
        buffer.AddUtf16(text);

        // The direction decides the glyph order and the cluster values, so it has to be set before guessing; a run
        // that is written right to left and shaped as left to right would be reversed twice.
        // A vertical run is set top to bottom. HarfBuzz then reports the inline distance in YAdvance and measures the
        // glyph offsets from a vertical origin, so the direction decided here also decides how the numbers below are
        // read.
        buffer.Direction = options.IsVertical
            ? Direction.TopToBottom
            : options.Direction == TextDirection.RightToLeft
                ? Direction.RightToLeft
                : Direction.LeftToRight;
        buffer.ClusterLevel = ClusterLevel.MonotoneGraphemes;
        buffer.GuessSegmentProperties();

        // Guessing fills script and language from the text and the process locale. The script guess is fine
        // for a run that is already split by script; the language is not: the caller's language is the
        // authority, and setting it is also what makes HarfBuzz apply the font's localized forms.
        if (!string.IsNullOrEmpty(options.Language))
            buffer.Language = new Language(options.Language);

        if (!string.IsNullOrEmpty(options.Script) && Script.TryParse(options.Script, out Script script))
            buffer.Script = script;

        // The parameterless overload is used when there are no features: passing an empty array to the
        // feature overload marshals a zero-length native buffer, which HarfBuzz dereferences.
        Feature[] features = ParseFeatures(options.Features);
        if (features.Length == 0)
            _font.Shape(buffer);
        else
            _font.Shape(buffer, features);

        GlyphInfo[] infos = buffer.GlyphInfos;
        GlyphPosition[] positions = buffer.GlyphPositions;

        int count = Math.Min(infos.Length, positions.Length);
        glyphIds = new uint[count];
        advances = new float[count];
        offsetX = new float[count];
        offsetY = new float[count];
        clusters = new int[count];

        for (int i = 0; i < count; i++)
        {
            // After shaping, HarfBuzz stores the glyph index in the info's codepoint field.
            glyphIds[i] = infos[i].Codepoint;
            clusters[i] = (int)infos[i].Cluster;
            // In the top-to-bottom direction the inline distance arrives as a *negative* YAdvance; the caller wants
            // an inline distance, so the sign is flipped here, once, where the convention is documented. Measured
            // (16px, 1 em = 1024): a CJK face with a vmtx advanceHeight of one em gives YAdvance = -1024, a face
            // without vmtx gives a synthesised value instead (arial: -1144), and the glyph's origin sits off the pen
            // by (XOffset, YOffset) - for a CJK face about half an em sideways.
            advances[i] = (options.IsVertical ? -positions[i].YAdvance : positions[i].XAdvance) / subPixelScale;
            offsetX[i] = positions[i].XOffset / subPixelScale;
            offsetY[i] = positions[i].YOffset / subPixelScale;
        }
    }

    /// <summary>
    /// Parse feature strings in HarfBuzz syntax into the features HarfBuzz takes. A name HarfBuzz cannot
    /// parse is dropped rather than thrown: features come from language data, and a typo in one must not take
    /// the whole layout down.
    /// </summary>
    /// <param name="features">Feature strings such as <c>palt=1</c>, or null for none.</param>
    /// <returns>The parsed features, in the order given.</returns>
    private static Feature[] ParseFeatures(IReadOnlyList<string>? features)
    {
        if (features is null || features.Count == 0)
            return [];

        var parsed = new List<Feature>(features.Count);
        foreach (string feature in features)
        {
            if (Feature.TryParse(feature, out Feature value))
                parsed.Add(value);
        }

        return [.. parsed];
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _font.Dispose();
        _face.Dispose();
        _blob.Dispose();
        // After the blob: the blob's font data lives in the stream's memory.
        _stream.Dispose();

        // The font data came from the typeface's font, so the typeface must not be collected while any of it
        // is still referenced.
        GC.KeepAlive(_typeface);
    }
}
