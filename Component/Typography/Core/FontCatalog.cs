using System;
using System.Threading;
using Godot;
using GodotNodeExtension.Component.GodotSkia;
using GodotNodeExtension.Component.Typography.Core.Model;
using SkiaSharp;

namespace GodotNodeExtension.Component.Typography.Core;

/// <summary>
/// Owns the platform font objects the layout pipeline measures with, and is the single place that
/// converts a Godot <see cref="Font"/> into a Skia typeface.
/// <para>
/// Why it exists: the typography server lays out on a dedicated thread, and
/// <c>Font.ToSKTypeface()</c> reads Godot resource state (font data, family names, variation chains).
/// Doing that off the main thread races with font release and hot reload — the top risk of the old
/// design. Producers therefore resolve their fonts <em>before</em> submitting work, and the layout
/// thread only performs a plain dictionary lookup on the id it was handed
/// (<see cref="DrawElement.ResolvedFontId"/>).
/// </para>
/// <para>
/// Ownership: the typefaces are process-wide and are never disposed, because
/// <c>SkiaGodotConverter</c> keeps an equally long-lived cache of the same typefaces. Moving the
/// ownership here (and dropping that cache) is P1 work; until then disposing anything from this
/// catalog would free a typeface the renderer still draws with.
/// </para>
/// <para>
/// Known limitation: entries are keyed by Godot's instance id, which the engine may reuse after a
/// font is freed — the same latent flaw the converter's cache has. Keying by font identity needs an
/// explicit lifetime hook, which arrives with the language-driven font stacks in P1.
/// </para>
/// </summary>
public sealed class FontCatalog
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<ulong, SKTypeface> _typefaces = new();

    /// <summary>Process-wide catalog shared by every producer and every layout handle.</summary>
    public static FontCatalog Shared { get; } = new();

    /// <summary>
    /// Number of types that were resolved on a thread other than the main one. Any non-zero value is
    /// a bug: it means a Godot resource was read from the layout thread. Exposed so a test can assert
    /// the server path stays at zero instead of relying on review.
    /// </summary>
    public long ResolutionsOffMainThread => Interlocked.Read(ref _resolutionsOffMainThread);

    private long _resolutionsOffMainThread;

    /// <summary>
    /// Resolve a Godot font to a stable id, converting and caching its typeface on first use.
    /// Main-thread only: the conversion reads Godot resource state. It still works when called from
    /// the layout thread (synchronous/direct engine use), but then it counts as a violation and
    /// reports through <see cref="ResolutionsOffMainThread"/>.
    /// </summary>
    /// <param name="font">The Godot font to resolve.</param>
    /// <returns>The font's id, to be carried by <see cref="DrawElement.ResolvedFontId"/>.</returns>
    public ulong Resolve(Font font)
    {
        ArgumentNullException.ThrowIfNull(font);

        if (!IsMainThread())
            Interlocked.Increment(ref _resolutionsOffMainThread);

        ulong id = font.GetInstanceId();
        if (!_typefaces.ContainsKey(id))
            _typefaces[id] = font.ToSkTypeface();

        return id;
    }

    /// <summary>
    /// Fill <see cref="DrawElement.ResolvedFontId"/> for every element of a layout input that carries
    /// a font. Producers call this on the main thread before handing the array to the server.
    /// </summary>
    /// <param name="elements">The elements about to be laid out (modified in place).</param>
    public void EnsureResolved(DrawElement[] elements)
    {
        ArgumentNullException.ThrowIfNull(elements);
        for (int i = 0; i < elements.Length; i++)
            EnsureResolved(ref elements[i]);
    }

    /// <summary>
    /// Fill <see cref="DrawElement.ResolvedFontId"/> for a single element. Producers call this on the
    /// main thread before streaming an element to the server.
    /// </summary>
    /// <param name="element">The element to resolve (modified in place).</param>
    public void EnsureResolved(ref DrawElement element)
    {
        if (element.Font is null || element.ResolvedFontId != 0)
            return;

        element.ResolvedFontId = Resolve(element.Font);
    }

    /// <summary>
    /// Register a typeface that the layout resolved itself rather than through <see cref="Resolve"/>: the
    /// shaping fallback picks a face per run from the font manager, and the glyphs it produces have to name
    /// that face so the renderer can draw them.
    /// </summary>
    /// <param name="typeface">The typeface to register.</param>
    /// <returns>An id that resolves back to the same typeface for the life of the process.</returns>
    /// <exception cref="ArgumentNullException">When <paramref name="typeface"/> is null.</exception>
    public ulong RegisterTypeface(SKTypeface typeface)
    {
        ArgumentNullException.ThrowIfNull(typeface);

        // The native handle is unique while the typeface is alive, and the catalog keeps every typeface it
        // has seen alive for the life of the process, so the handle is a sound id.
        ulong id = (ulong)typeface.Handle;
        _typefaces.TryAdd(id, typeface);
        return id;
    }

    /// <summary>
    /// Look up an already resolved typeface by id. Safe to call from any thread: it never touches a
    /// Godot object.
    /// </summary>
    /// <param name="fontId">The id produced by <see cref="Resolve"/>.</param>
    /// <param name="typeface">The resolved typeface, when present.</param>
    /// <returns>True when the font was resolved earlier.</returns>
    public bool TryGetTypeface(ulong fontId, out SKTypeface? typeface) =>
        _typefaces.TryGetValue(fontId, out typeface);

    /// <summary>
    /// Whether a font id has been resolved, i.e. whether the layout thread can measure with it.
    /// </summary>
    /// <param name="fontId">The id produced by <see cref="Resolve"/>.</param>
    /// <returns>True when the id is known to the catalog.</returns>
    public bool IsResolved(ulong fontId) => _typefaces.ContainsKey(fontId);

    /// <summary>Whether the calling thread is the engine's main thread.</summary>
    /// <returns>True on the main thread.</returns>
    private static bool IsMainThread() => OS.GetThreadCallerId() == OS.GetMainThreadId();
}
