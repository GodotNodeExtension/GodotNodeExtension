namespace GodotNodeExtension.Tests.GodotChart.Marks;

using System;
using System.Linq;
using GdUnit4;
using GodotNodeExtension.Component.GodotChart;
using Support;
using static GdUnit4.Assertions;

/// <summary>
/// Contract of <see cref="EncodeSet"/>: the per-<see cref="Channel"/> container for encode mappings.
/// Covers set/lookup/channel enumeration and the silent-null <c>Resolve</c> paths (no encode,
/// absent field, constant null, unknown encode implementation).
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class EncodeSetTest
{
    /// <summary>An <see cref="IEncodeValue"/> implementation the resolver does not know about.</summary>
    private sealed class UnknownEncode : IEncodeValue { }

    /// <summary>Run <paramref name="action"/> and return the exception it threw (null when it did not throw).</summary>
    private static Exception? CaptureException(Action action)
    {
        try { action(); return null; }
        catch (Exception ex) { return ex; }
    }

    [TestCase]
    public void EncodeSetLookupsAndChannelEnumeration()
    {
        var encodes = new EncodeSet();

        AssertThat(encodes.Has(Channel.X)).IsFalse();
        AssertThat(encodes.TryGet(Channel.X) is null).IsTrue();
        AssertThat(encodes.Channels.Any()).IsFalse();
        AssertThat(CaptureException(() => encodes.Get(Channel.X)) is InvalidOperationException).IsTrue();

        encodes.Set(Channel.X, new FieldEncode("x"));
        encodes.Set(Channel.Y, new ConstantEncode(3));

        AssertThat(encodes.Channels.Count()).IsEqual(2);
        AssertThat(encodes.Channels.Contains(Channel.X)).IsTrue();
        AssertThat(encodes.Channels.Contains(Channel.Y)).IsTrue();
        AssertThat(encodes.Channels.Contains(Channel.Color)).IsFalse();
        AssertThat(encodes.TryGet(Channel.Y) is ConstantEncode).IsTrue();
    }

    [TestCase]
    public void EncodeSetResolveIsSilentForEveryMissingPath()
    {
        var row = TestContexts.Row(("x", 1.0));
        var encodes = new EncodeSet();

        // no encode configured for the channel
        AssertThat(encodes.Resolve(Channel.X, row) is null).IsTrue();

        // a field encode whose field is absent from the row
        encodes.Set(Channel.X, new FieldEncode("ghost"));
        AssertThat(encodes.Resolve(Channel.X, row) is null).IsTrue();

        // a constant encode carrying null
        encodes.Set(Channel.Y, new ConstantEncode(null!));
        AssertThat(encodes.Resolve(Channel.Y, row) is null).IsTrue();

        // an IEncodeValue implementation the resolver does not know
        encodes.Set(Channel.Color, new UnknownEncode());
        AssertThat(encodes.Resolve(Channel.Color, row) is null).IsTrue();

        // sanity: a present field and a real constant do resolve
        encodes.Set(Channel.Size, new FieldEncode("x"));
        AssertThat((double)encodes.Resolve(Channel.Size, row)!).IsEqual(1.0);
        encodes.Set(Channel.Shape, new ConstantEncode("circle"));
        AssertThat((string)encodes.Resolve(Channel.Shape, row)!).IsEqual("circle");
    }

    /// <summary>
    /// <see cref="EncodeSet.MergedWith"/>: the overlay wins where it binds a channel, the base set stays the
    /// fallback and is left untouched, and nothing to overlay hands the base set itself back.
    /// <see cref="EncodeSet.FieldOf"/> reads the field behind a binding - a constant is not a field name.
    /// </summary>
    [TestCase]
    public void MergedWithLayersAnOverlayAndFieldOfReadsBindings()
    {
        var baseSet = new EncodeSet();
        baseSet.Set(Channel.X, new FieldEncode("cat"));
        baseSet.Set(Channel.Y, new FieldEncode("value"));
        var overlay = new EncodeSet();
        overlay.Set(Channel.Y, new FieldEncode("other"));
        overlay.Set(Channel.Color, new ConstantEncode(3));

        var merged = baseSet.MergedWith(overlay);

        AssertThat(merged.FieldOf(Channel.X)).IsEqual("cat");        // the base set is the fallback
        AssertThat(merged.FieldOf(Channel.Y)).IsEqual("other");      // the overlay wins
        AssertThat(baseSet.FieldOf(Channel.Y)).IsEqual("value");     // ...and the base set is untouched
        AssertThat(merged.FieldOf(Channel.Color) is null).IsTrue();  // a constant is not a field
        AssertThat(merged.FieldOf(Channel.Shape) is null).IsTrue();  // nor is an unbound channel

        AssertThat(ReferenceEquals(baseSet.MergedWith(null), baseSet)).IsTrue();
        AssertThat(ReferenceEquals(baseSet.MergedWith(new EncodeSet()), baseSet)).IsTrue();
    }
}
