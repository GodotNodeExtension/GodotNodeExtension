namespace GodotNodeExtension.Tests.Typography;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GdUnit4;
using Godot;
using GodotNodeExtension.Component.Typography.Core;
using GodotNodeExtension.Component.Typography.Core.Model;
using GodotNodeExtension.Component.Typography.Languages;
using static GdUnit4.Assertions;

/// <summary>
/// Specification for the inline/block axis abstraction and for the writing mode's place in the settings.
/// <para>
/// The point of the abstraction is that a stage states geometry once, in the two axes, and the mode decides which
/// Godot axis carries which quantity. What has to hold is therefore a round trip - a block coordinate survives
/// being turned into a content-space point and back - plus the two anchors a vertical mode is defined by: block zero
/// sits on the block-start edge (the content box' right edge for right-to-left columns), and inline zero on the
/// content origin.
/// </para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class TypographyAxesTest
{
    /// <summary>Horizontal writing is a plain mapping: inline is X, block is Y.</summary>
    [TestCase]
    public void HorizontalAxesMapInlineToXAndBlockToY()
    {
        var axes = new LayoutAxes(WritingMode.HorizontalTb, 400f);

        AssertThat(axes.BlockUnit).IsEqual(new Vector2(0f, 1f));
        AssertThat(axes.Point(10f, 20f)).IsEqual(new Vector2(10f, 20f));
        AssertThat(axes.Inline(new Vector2(3f, 4f))).IsEqual(3f);
        AssertThat(axes.Block(new Vector2(3f, 4f))).IsEqual(4f);
        AssertThat(axes.Size(5f, 6f)).IsEqual(new Vector2(5f, 6f));
    }

    /// <summary>
    /// Right-to-left columns: inline runs down the page and block is measured leftward from the content box' right
    /// edge, so the first column occupies the rightmost strip.
    /// </summary>
    [TestCase]
    public void VerticalRlColumnsAdvanceLeftwardFromTheRightEdge()
    {
        var axes = new LayoutAxes(WritingMode.VerticalRl, 400f);

        AssertThat(axes.IsVertical).IsTrue();
        AssertThat(axes.BlockUnit).IsEqual(new Vector2(-1f, 0f));

        // Block zero is the content box' right edge; the inline coordinate is Y.
        AssertThat(axes.Point(0f, 0f)).IsEqual(new Vector2(400f, 0f));
        AssertThat(axes.Point(30f, 0f)).IsEqual(new Vector2(400f, 30f));
        AssertThat(axes.Point(0f, 25f)).IsEqual(new Vector2(375f, 0f));

        AssertThat(axes.Block(new Vector2(375f, 0f))).IsEqual(25f);
        AssertThat(axes.Inline(new Vector2(100f, 7f))).IsEqual(7f);
        AssertThat(axes.Size(5f, 6f)).IsEqual(new Vector2(6f, 5f));
    }

    /// <summary>
    /// Left-to-right columns (reserved, but the arithmetic has to be consistent): block is measured rightward from
    /// the left edge.
    /// </summary>
    [TestCase]
    public void VerticalLrColumnsAdvanceRightwardFromTheLeftEdge()
    {
        var axes = new LayoutAxes(WritingMode.VerticalLr, 400f);

        AssertThat(axes.BlockUnit).IsEqual(new Vector2(1f, 0f));
        AssertThat(axes.Point(30f, 0f)).IsEqual(new Vector2(0f, 30f));
        AssertThat(axes.Point(0f, 25f)).IsEqual(new Vector2(25f, 0f));
        AssertThat(axes.Block(new Vector2(25f, 0f))).IsEqual(25f);
    }

    /// <summary>A block coordinate survives the trip through content space, in every mode.</summary>
    /// <param name="mode">The mode under test.</param>
    [TestCase(WritingMode.HorizontalTb)]
    [TestCase(WritingMode.VerticalRl)]
    [TestCase(WritingMode.VerticalLr)]
    public void AxesPositionsRoundTrip(WritingMode mode)
    {
        var axes = new LayoutAxes(mode, 400f);

        foreach (float inlinePos in new[] { 0f, 1f, 16f, 250f })
        {
            foreach (float blockPos in new[] { 0f, 1f, 16f, 250f })
            {
                Vector2 point = axes.Point(inlinePos, blockPos);

                AssertThat(axes.Inline(point)).OverrideFailureMessage(
                    $"{mode}: inline {inlinePos} came back as {axes.Inline(point)}").IsEqual(inlinePos);
                AssertThat(axes.Block(point)).OverrideFailureMessage(
                    $"{mode}: block {blockPos} came back as {axes.Block(point)}").IsEqual(blockPos);
            }
        }
    }

    /// <summary>
    /// A request may name its writing mode, and a vertical one is refused rather than laid out horizontally: the
    /// vocabulary exists, the geometry does not follow it yet, and a horizontal document that claims to be vertical
    /// is worse than a failure.
    /// </summary>
    [TestCase]
    public void ARequestedVerticalModeIsRefusedRatherThanLaidOutHorizontally()
    {
        var registry = LanguageProfileRegistry.CreateDefault();

        AssertThat(registry.Resolve(new TypographySettings { LanguageTag = "ja", MaxWidth = 200f }).WritingMode)
            .IsEqual(WritingMode.HorizontalTb);

        // VerticalRl is the mode the axis plumbing was built for and is laid out as columns; VerticalLr is the one
        // that is still refused, because nothing implements right-to-left columns.
        AssertThat(registry.Resolve(new TypographySettings
        {
            LanguageTag = "ja",
            MaxWidth = 200f,
            MaxHeight = 400f,
            WritingMode = WritingMode.VerticalRl,
        }).WritingMode).IsEqual(WritingMode.VerticalRl);

        bool refused = false;

        try
        {
            _ = registry.Resolve(new TypographySettings
            {
                LanguageTag = "ja",
                MaxWidth = 200f,
                WritingMode = WritingMode.VerticalLr,
            });
        }
        catch (NotSupportedException)
        {
            refused = true;
        }

        AssertThat(refused).OverrideFailureMessage(
            "asking for VerticalLr was accepted, so the request would come back laid out horizontally").IsTrue();
    }

    /// <summary>
    /// The axes travel with the measured content, taken from the same resolution the stages use.
    /// </summary>
    [TestCase]
    public void PreparedContentCarriesTheRequestsAxes()
    {
        using var engine = new TypographyEngine(new TypographySettings
        {
            LanguageTag = "ja",
            MaxWidth = 300f,
        });

        _ = engine.Prepare([]);

        PreparedContent prepared = engine.CurrentPreparedContent!;

        AssertThat(prepared.Axes.Mode).IsEqual(WritingMode.HorizontalTb);
        AssertThat(prepared.Axes.BlockExtentLimit).IsEqual(300f);
        AssertThat(prepared.Axes.IsVertical).IsFalse();
    }

    /// <summary>
    /// <see cref="TypographySettings.Clone"/> keeps every setting.
    /// <para>
    /// Reflected over the properties on purpose: the failure this guards against is a setting added later and not
    /// copied, which a hand-written list of assertions cannot catch. The fixture is checked to differ from the
    /// defaults for every property first, so a property the fixture forgets fails here instead of being skipped.
    /// </para>
    /// </summary>
    [TestCase]
    public void CloneKeepsEverySetting()
    {
        TypographySettings populated = FullyPopulated();
        var defaults = new TypographySettings();

        foreach (PropertyInfo property in typeof(TypographySettings).GetProperties())
        {
            object? value = property.GetValue(populated);
            object? fallback = property.GetValue(defaults);

            AssertThat(SameValue(value, fallback)).OverrideFailureMessage(
                $"the fixture does not set {property.Name}, so the clone check would skip it").IsFalse();

            AssertThat(SameValue(value, property.GetValue(populated.Clone()))).OverrideFailureMessage(
                $"Clone() drops {property.Name}").IsTrue();
        }
    }

    /// <summary>A settings instance with every property set away from its default.</summary>
    /// <returns>The instance.</returns>
    private static TypographySettings FullyPopulated() => new()
    {
        LanguageTag = "zh-Hant",
        WritingMode = WritingMode.HorizontalTb,
        MaxWidth = 321f,
        MaxHeight = 654f,
        LineSpacing = 2f,
        ParagraphSpacing = 12f,
        EnableLineProhibition = false,
        ProhibitionLevel = ProhibitionLevel.Strict,
        EnableCjkLatinSpacing = false,
        CjkLatinSpacingEm = 0.125f,
        EnableHyphenation = false,
        EnablePunctuationCompression = false,
        EnableLetterformSubstitution = false,
        FirstLineIndent = 3,
        Alignment = TextAlignment.Right,
        Direction = TextDirection.RightToLeft,
        GridStep = 8f,
        GridOrigin = 4f,
        TabStops = [new TabStop(40f)],
        DefaultTabStopEm = null,
        WrapRegions = [new WrapRegion { Position = new Vector2(1f, 2f) }],
        Padding = 7f,
    };

    /// <summary>
    /// Whether two property values are the same value, for lists as well as scalars: a clone is expected to hold a
    /// copy of a list, not the same instance.
    /// </summary>
    /// <param name="left">One value.</param>
    /// <param name="right">The other.</param>
    /// <returns>True when they are equal.</returns>
    private static bool SameValue(object? left, object? right)
    {
        if (left is IEnumerable a and not string && right is IEnumerable b and not string)
        {
            List<object?> leftItems = a.Cast<object?>().ToList();
            List<object?> rightItems = b.Cast<object?>().ToList();

            return leftItems.Count == rightItems.Count && leftItems.SequenceEqual(rightItems);
        }

        return Equals(left, right);
    }
}
