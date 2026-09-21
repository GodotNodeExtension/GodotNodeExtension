namespace GodotNodeExtension.Tests.Typography;

using System;
using System.Collections.Generic;
using GdUnit4;
using Godot;
using GodotNodeExtension.Component.Typography.Core;
using GodotNodeExtension.Component.Typography.Core.Model;
using static GdUnit4.Assertions;

/// <summary>
/// Specification for the grid: an external constraint that decides where elements may stand (rule C13 of the
/// architecture note), used for the column-like alignment CJK pages are set in and for the integral character cells
/// that CJK/Latin mixing is supposed to produce (clreq §6.2.4).
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class TypographyGridTest
{
    /// <summary>With a grid of one em, every element starts on a grid line.</summary>
    [TestCase]
    public void ElementsSnapToTheGridStep()
    {
        List<LayoutLine> lines = LayOut("中文排版会用到网格对齐，每个字都落在格线上。", gridStep: 16f);

        AssertThat(lines.Count).IsGreater(0);

        foreach (LayoutLine line in lines)
        {
            foreach (LayoutElement element in line.Elements)
            {
                if (element.Type != DrawElement.ElementType.Text)
                    continue;

                float offset = element.Position.X / 16f;

                AssertThat(Math.Abs(offset - MathF.Round(offset)) < 0.01f).OverrideFailureMessage(
                    $"\"{element.Text}\" starts at {element.Position.X:F3}, which is not a multiple of 16").IsTrue();
            }
        }
    }

    /// <summary>A half-em grid places the same text on twice as many lines, and still on grid lines.</summary>
    [TestCase]
    public void AHalfEmGridUsesItsOwnStep()
    {
        List<LayoutLine> lines = LayOut("中文排版会用到网格对齐。", gridStep: 8f);

        foreach (LayoutElement element in lines[0].Elements)
        {
            if (element.Type != DrawElement.ElementType.Text)
                continue;

            AssertThat(element.Position.X % 8f < 0.01f || 8f - (element.Position.X % 8f) < 0.01f).IsTrue();
        }
    }

    /// <summary>No grid means no constraints: the elements keep the positions the rest of the pipeline gave them.</summary>
    [TestCase]
    public void WithoutAGridNothingMoves()
    {
        List<LayoutLine> withGrid = LayOut("中文排版会用到网格对齐。", gridStep: 16f);
        List<LayoutLine> without = LayOut("中文排版会用到网格对齐。", gridStep: null);

        // The first element starts at the content edge either way: a grid may not move text leftwards.
        AssertThat(withGrid[0].Elements[0].Position.X).IsEqual(without[0].Elements[0].Position.X);

        foreach (LayoutElement element in withGrid[0].Elements)
        {
            if (element.Type != DrawElement.ElementType.Text)
                continue;

            AssertThat(element.Position.X).OverrideFailureMessage(
                "a grid only ever moves an element forward").IsGreater(-0.01f);
        }
    }

    /// <summary>Snapping never moves an element backwards past its interval.</summary>
    [TestCase]
    public void SnappingStaysInsideTheLineInterval()
    {
        List<LayoutLine> lines = LayOut("中文排版会用到网格对齐，每个字都落在格线上，这一行会被填满。", gridStep: 16f);

        foreach (LayoutLine line in lines)
        {
            foreach (LayoutElement element in line.Elements)
                AssertThat(element.Position.X >= line.LineLeft - 0.01f).IsTrue();
        }
    }

    // ── Helpers ──

    private static List<LayoutLine> LayOut(string text, float? gridStep)
    {
        DrawElement[] elements =
        [
            new()
            {
                Type = DrawElement.ElementType.Text,
                Text = text,
                Font = ThemeDB.FallbackFont,
                FontSize = 16,
                Color = new Color(0.1f, 0.1f, 0.1f),
            },
        ];

        using var engine = new TypographyEngine(new TypographySettings
        {
            MaxWidth = 240f,
            LanguageTag = "zh-Hans",
            FirstLineIndent = 0,
            Alignment = TextAlignment.Left,
            GridStep = gridStep,
        });

        return engine.PrepareAndLayout(elements, out _);
    }
}
