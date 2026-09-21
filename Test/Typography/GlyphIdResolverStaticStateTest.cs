namespace GodotNodeExtension.Tests.Typography;

using System;
using GdUnit4;
using GodotNodeExtension.Component.Typography.Core.Fonts;
using SkiaSharp;
using static GdUnit4.Assertions;

/// <summary>
/// Behaviour specification for the static state of <see cref="GlyphIdResolver"/>: resolvers are cached per
/// typeface for the process lifetime, and each one owns HarfBuzz objects whose native side references this
/// assembly. An editor assembly reload must therefore be able to release them (godot#78513) - a resolver
/// that stays cached keeps the old load context alive and the editor reports `Failed to unload assemblies`.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class GlyphIdResolverStaticStateTest
{
    [TestCase]
    public void TheUnloadHookIsRegistered()
    {
        AssertThat(GlyphIdResolver.UnloadHookInstalled).IsTrue();
    }

    [TestCase]
    public void ResolversAreCachedPerTypeface()
    {
        GlyphIdResolver.ResetStaticState();
        SKTypeface typeface = TestTypeface();
        SKTypeface other = SKTypeface.FromFamilyName("Times New Roman") ?? SKTypeface.Default;

        GlyphIdResolver first = GlyphIdResolver.For(typeface);

        AssertThat(GlyphIdResolver.For(typeface)).IsSame(first);      // cached, not re-created
        AssertThat(GlyphIdResolver.For(other)).IsNotSame(first);      // one per typeface
        AssertThat(GlyphIdResolver.CachedResolverCount).IsEqual(2);
    }

    [TestCase]
    public void TheStaticStateReleaseDisposesEveryCachedResolver()
    {
        GlyphIdResolver.ResetStaticState();
        GlyphIdResolver resolver = GlyphIdResolver.For(TestTypeface());

        GlyphIdResolver.ResetStaticState();

        AssertThat(GlyphIdResolver.CachedResolverCount).IsEqual(0);
        AssertThat(Throws<ObjectDisposedException>(() => resolver.Shape(
            "a", 12f, default, out _, out _, out _, out _, out _))).IsTrue();

        // The hook can run after an explicit reset: doing it twice must stay safe, and the cache refills.
        GlyphIdResolver.ResetStaticState();
        AssertThat(GlyphIdResolver.For(TestTypeface())).IsNotNull();
        GlyphIdResolver.ResetStaticState();
    }

    [TestCase]
    public void AResolverShapesAfterTheCacheWasReleased()
    {
        // The font data behind a resolver's blob lives in the stream the resolver opened, not in the
        // typeface. Releasing that stream in the constructor faulted inside HarfBuzz while shaping, and
        // whether it faulted depended on whether something else still held the same memory, so a green suite
        // could hide it. This is the cheap version of that sequence: release the cache, then shape.
        GlyphIdResolver.ResetStaticState();

        GlyphIdResolver.For(TestTypeface()).Shape(
            "ab", 12f, default, out uint[] ids, out _, out _, out _, out _);

        AssertThat(ids.Length).IsEqual(2);
    }

    /// <summary>A typeface that exists on every platform this suite runs on.</summary>
    private static SKTypeface TestTypeface() =>
        SKTypeface.FromFamilyName("Arial") ?? SKTypeface.FromFamilyName(null) ?? SKTypeface.Default;

    private static bool Throws<T>(Action action) where T : Exception
    {
        try
        {
            action();
        }
        catch (T)
        {
            return true;
        }

        return false;
    }
}
