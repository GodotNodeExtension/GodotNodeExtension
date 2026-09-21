namespace GodotNodeExtension.Tests.Typography;

using System;
using System.Collections.Generic;
using System.Linq;
using GdUnit4;
using Godot;
using GodotNodeExtension.Component.Typography.Core;
using GodotNodeExtension.Component.Typography.Core.Model;
using static GdUnit4.Assertions;

/// <summary>
/// The output contract for <see cref="LayoutElement.BaselineY"/> and for what the assembly stage synthesises.
/// <para>
/// Two things are checked here that the golden dumps cannot show. First, the baseline is a <em>block-axis</em>
/// coordinate: <c>Block(Position) + ascent</c>, inside the box' block extent - which is a Y in horizontal writing
/// but the column's own coordinate in vertical writing, so a suite that only runs horizontal scenarios cannot tell
/// the two apart.
/// </para>
/// <para>
/// Second, everything the assembly stage makes up (a merged inline background, a block's background, border and
/// marker) has to obey the same reading: its baseline is its own block-end edge. Those elements are skipped by the
/// golden invariants, because they carry a <see cref="LayoutElement.Reason"/> and never went through the line
/// breaker - which is exactly where an axis left unmapped hides: the merged background used to group by Y, which is
/// the inline coordinate in vertical writing, so a run of inline code came out as one rectangle per character.
/// </para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class TypographyBaselineAxisTest
{
    /// <summary>Inline-code styling: the shape whose background has to merge.</summary>
    private static readonly Color InlineCodeBackground = new(0.2f, 0.2f, 0.2f);

    /// <summary>Padding that makes the merge run at all (no padding and no fill-line means no rectangle).</summary>
    private static readonly Vector2 InlineCodePadding = new(4f, 2f);

    /// <summary>Index of the inline-code element in the fixture's source array.</summary>
    private const int InlineCodeSourceIndex = 1;

    /// <summary>
    /// Every element the assembly stage synthesised carries a baseline that is its own block-end edge, in both
    /// writing modes. Horizontal writing is the control: there the block axis is Y, so the values are the ones the
    /// horizontal path has always produced.
    /// <para>
    /// Text elements are not part of this: their baseline is <c>Block(Position) + ascent</c> rather than an edge,
    /// which is what keeps the two readings the same field (<see cref="LayoutElement.BaselineY"/>).
    /// </para>
    /// </summary>
    /// <param name="mode">Writing mode under test.</param>
    [TestCase(WritingMode.HorizontalTb)]
    [TestCase(WritingMode.VerticalRl)]
    public void SynthesisedGeometryReportsItsBlockEndEdge(WritingMode mode)
    {
        List<LayoutElement> synthesised = Run(mode)
            .Where(e => e.Reason is not null && e.Type != DrawElement.ElementType.Text && !float.IsNaN(e.BaselineY))
            .ToList();

        AssertThat(synthesised.Count).OverrideFailureMessage(
            $"no synthesised geometry was produced in {mode}, so this case proves nothing").IsGreater(0);

        LayoutAxes axes = Axes(mode);

        foreach (LayoutElement element in synthesised)
        {
            float blockEnd = axes.Block(element.Position) + axes.BlockExtent(element.Size);

            AssertThat(MathF.Abs(element.BaselineY - blockEnd) < 0.01f).OverrideFailureMessage(
                    $"{mode}: {element.Reason} baseline {element.BaselineY:F3} is not its block-end edge "
                    + $"{blockEnd:F3}").IsTrue();
        }
    }

    /// <summary>
    /// A run of inline code becomes one background rectangle, in both writing modes.
    /// <para>
    /// "One line" is a block coordinate. Comparing Y instead put every character of a vertical column in a group of
    /// its own, because their Y values differ by one cell - so the run merged into as many rectangles as it had
    /// characters.
    /// </para>
    /// </summary>
    /// <param name="mode">Writing mode under test.</param>
    [TestCase(WritingMode.HorizontalTb)]
    [TestCase(WritingMode.VerticalRl)]
    public void AnInlineBackgroundMergesOncePerLine(WritingMode mode)
    {
        List<LayoutElement> elements = Run(mode);

        var backgrounds = elements.Where(e => e.Reason == "MergedInlineBackground").ToList();

        AssertThat(backgrounds.Count).OverrideFailureMessage(
                $"{mode}: the run of inline code should merge into one background, got {backgrounds.Count}")
            .IsEqual(1);

        // The rectangle covers the run it was merged for: every element that came from the same source element sits
        // inside its block extent.
        LayoutAxes axes = Axes(mode);
        LayoutElement rect = backgrounds[0];
        float rectStart = axes.Block(rect.Position);
        float rectEnd = rectStart + axes.BlockExtent(rect.Size);

        var run = elements
            .Where(e => e.SourceIndex == InlineCodeSourceIndex && e.Type == DrawElement.ElementType.Text)
            .ToList();

        AssertThat(run.Count).OverrideFailureMessage(
                $"{mode}: the inline-code run produced {run.Count} element(s), so the merge is not exercised")
            .IsGreater(1);

        foreach (LayoutElement piece in run)
        {
            float block = axes.Block(piece.Position);

            AssertThat(block >= rectStart - 0.01f && block <= rectEnd + 0.01f).OverrideFailureMessage(
                    $"{mode}: \"{piece.Text}\" sits at block {block:F3}, outside the merged background "
                    + $"[{rectStart:F3}, {rectEnd:F3})").IsTrue();
        }
    }

    /// <summary>
    /// The published content size is measured along the two axes, not along X and Y: its inline component covers the
    /// farthest inline end any element reaches, and its block component the farthest block end. In vertical writing
    /// the two are exchanged, which is what a consumer sizing a surface around columns depends on - reading the pair
    /// as a width and a height would get neither, and a swap of the two would fail here.
    /// </summary>
    /// <param name="mode">Writing mode under test.</param>
    [TestCase(WritingMode.HorizontalTb)]
    [TestCase(WritingMode.VerticalRl)]
    public void TheContentSizeIsMeasuredInTheTwoAxes(WritingMode mode)
    {
        (List<LayoutElement> elements, Vector2 contentSize) = RunWithSize(mode);
        LayoutAxes axes = Axes(mode);

        float inlineEnd = 0f;
        float blockEnd = 0f;

        foreach (LayoutElement element in elements)
        {
            inlineEnd = MathF.Max(inlineEnd, axes.Inline(element.Position) + axes.Inline(element.Size));
            blockEnd = MathF.Max(blockEnd, axes.Block(element.Position) + axes.BlockExtent(element.Size));
        }

        AssertThat(inlineEnd > 0f && blockEnd > 0f).OverrideFailureMessage(
            $"{mode}: the fixture produced no extent to check ({inlineEnd:F3}, {blockEnd:F3})").IsTrue();

        AssertThat(axes.Inline(contentSize) >= inlineEnd - 0.01f).OverrideFailureMessage(
            $"{mode}: the content size reaches {axes.Inline(contentSize):F3} along the inline axis while an element "
            + $"reaches {inlineEnd:F3}").IsTrue();

        AssertThat(axes.BlockExtent(contentSize) >= blockEnd - 0.01f).OverrideFailureMessage(
            $"{mode}: the content size reaches {axes.BlockExtent(contentSize):F3} along the block axis while an "
            + $"element reaches {blockEnd:F3}").IsTrue();
    }

    /// <summary>
    /// Content expanded from an auto-size block keeps the baseline relation: a block's content offset moves a
    /// baseline only along the block axis, so the baseline stays inside the box it was measured for.
    /// <para>
    /// Horizontal writing only, and deliberately so: a block's own sub-layout is assembled with horizontal
    /// geometry, so in vertical writing its inner baselines are not block coordinates of the page at all. That is a
    /// gap in the block layout rather than in this contract, and it is recorded as one instead of being asserted
    /// away here.
    /// </para>
    /// </summary>
    [TestCase(WritingMode.HorizontalTb)]
    public void BlockContentKeepsItsBaselineInsideItsBox(WritingMode mode)
    {
        LayoutAxes axes = Axes(mode);

        List<LayoutElement> fromBlocks = Run(mode)
            .Where(e => e.Reason == "AutoSizeBlockContent" && !float.IsNaN(e.BaselineY))
            .ToList();

        AssertThat(fromBlocks.Count).OverrideFailureMessage(
            $"no block content was expanded in {mode}, so this case proves nothing").IsGreater(0);

        foreach (LayoutElement element in fromBlocks)
        {
            float blockStart = axes.Block(element.Position);
            float blockEnd = blockStart + axes.BlockExtent(element.Size);

            AssertThat(element.BaselineY >= blockStart - 0.01f).OverrideFailureMessage(
                $"{mode}: block content \"{element.Text}\" has its baseline {element.BaselineY:F3} before its box "
                + $"(block start {blockStart:F3})").IsTrue();
            AssertThat(element.BaselineY <= blockEnd + 0.01f).OverrideFailureMessage(
                $"{mode}: block content \"{element.Text}\" has its baseline {element.BaselineY:F3} past its box "
                + $"(block end {blockEnd:F3})").IsTrue();
        }
    }

    // ── Fixture ──

    /// <summary>Width of the content box: the measure in horizontal writing, and how far columns may run in vertical writing.</summary>
    private const float BoxWidth = 240f;

    /// <summary>How long a line may be in vertical writing.</summary>
    private const float ColumnHeight = 200f;

    /// <summary>Axes of the fixture, for the relations the cases assert.</summary>
    /// <param name="mode">Writing mode under test.</param>
    /// <returns>The axes the fixture is laid out with.</returns>
    private static LayoutAxes Axes(WritingMode mode) => new(mode, BoxWidth);

    /// <summary>
    /// One layout holding both shapes that make the assembly stage synthesise geometry: a run of inline code
    /// (merged background) and an auto-size block (background, border, marker and expanded content).
    /// </summary>
    /// <param name="mode">Writing mode under test.</param>
    /// <returns>The flattened layout elements.</returns>
    private static (List<LayoutElement> Elements, Vector2 ContentSize) RunWithSize(WritingMode mode)
    {
        var settings = new TypographySettings
        {
            MaxWidth = BoxWidth,
            MaxHeight = mode == WritingMode.HorizontalTb ? 0f : ColumnHeight,
            WritingMode = mode,
            LineSpacing = 0f,
            ParagraphSpacing = 0f,
        };

        DrawElement[] source =
        [
            Text("前文"),
            Text("inline-code 樣本", background: InlineCodeBackground, padding: InlineCodePadding),
            Text("後文"),
            Break(),
            Block(
                new BlockLayout
                {
                    FullWidth = true,
                    LeftIndent = 12f,
                    Padding = new Vector2(6f, 4f),
                    BackgroundColor = new Color(0.92f, 0.92f, 0.96f),
                    BackgroundCornerRadius = 4f,
                    LeftBorderColor = new Color(0.4f, 0.5f, 0.9f),
                    LeftBorderWidth = 3f,
                    MarkerText = "1.",
                    MarkerFont = ThemeDB.FallbackFont,
                    MarkerFontSize = 14,
                    MarkerColor = new Color(0.1f, 0.1f, 0.1f),
                }),
            Text("块内的第一行内容会自动换行。"),
            Text("块内的第二行。"),
            BlockEnd(),
        ];

        var engine = new TypographyEngine(settings);
        engine.PrepareAndLayout(source.AsSpan(), out _);
        return (engine.GetLayoutElements(source), engine.ContentSize);
    }

    /// <summary>Just the elements, for the cases that do not look at the content size.</summary>
    /// <param name="mode">Writing mode under test.</param>
    /// <returns>The flattened layout elements.</returns>
    private static List<LayoutElement> Run(WritingMode mode) => RunWithSize(mode).Elements;

    private static DrawElement Text(string text, Color? background = null, Vector2? padding = null) => new()
    {
        Type = DrawElement.ElementType.Text,
        Text = text,
        Font = ThemeDB.FallbackFont,
        FontSize = 16,
        Color = new Color(0.1f, 0.1f, 0.1f),
        BackgroundColor = background,
        BackgroundPadding = padding ?? Vector2.Zero,
        BackgroundCornerRadius = background.HasValue ? 3f : 0f,
        ExtensionId = -1,
        TextEffectId = -1,
    };

    private static DrawElement Break() => new()
    {
        Type = DrawElement.ElementType.Text,
        Font = ThemeDB.FallbackFont,
        FontSize = 16,
        IsParagraphBreak = true,
        ExtensionId = -1,
        TextEffectId = -1,
    };

    private static DrawElement Block(BlockLayout block) => new()
    {
        Type = DrawElement.ElementType.ExtensionRegion,
        BlockInfo = block,
        Font = ThemeDB.FallbackFont,
        FontSize = 16,
        ExtensionId = -1,
        TextEffectId = -1,
    };

    private static DrawElement BlockEnd() => new()
    {
        Type = DrawElement.ElementType.ExtensionRegion,
        IsBlockEnd = true,
        Font = ThemeDB.FallbackFont,
        FontSize = 16,
        ExtensionId = -1,
        TextEffectId = -1,
    };
}
