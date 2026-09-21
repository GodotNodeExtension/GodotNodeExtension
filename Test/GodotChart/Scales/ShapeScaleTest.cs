namespace GodotNodeExtension.Tests.GodotChart.Scales;

using System;
using System.Linq;
using GdUnit4;
using GodotNodeExtension.Component.GodotChart;
using static GdUnit4.Assertions;

/// <summary>
/// Behaviour specification for <see cref="ShapeScale"/> - the <see cref="Channel.Shape"/> channel's
/// counterpart of a colour scale: the category order (and therefore the symbol order), cycling when
/// there are more categories than shapes, an unknown category, the degenerate single-category domain
/// and the custom shape range.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class ShapeScaleTest
{
    /// <summary>Assert two doubles are within <paramref name="eps"/> (IsEqual is exact for doubles).</summary>
    private static void Approx(double actual, double expected, double eps = 1e-9)
        => AssertThat(Math.Abs(actual - expected) <= eps).IsTrue();

    private static ShapeScale Fitted(params object[] values)
    {
        var scale = new ShapeScale();
        scale.Fit(values);
        return scale;
    }

    // ── Domain and symbol order ─────────────────────────────────────────────

    [TestCase]
    public void CategoriesGetShapesInFirstSeenOrder()
    {
        var scale = Fitted("a", "b", "c");

        AssertThat(scale.Domain.Count).IsEqual(3);
        AssertThat(scale.Domain[0]).IsEqual("a");
        AssertThat(scale.Domain[1]).IsEqual("b");
        AssertThat(scale.Domain[2]).IsEqual("c");
        AssertThat(scale.MapShape("a")).IsEqual(ShapeKind.Circle);
        AssertThat(scale.MapShape("b")).IsEqual(ShapeKind.Square);
        AssertThat(scale.MapShape("c")).IsEqual(ShapeKind.Triangle);
    }

    [TestCase]
    public void RepeatingACategoryKeepsItsShape()
    {
        var scale = Fitted("a", "b", "a", "b", "a");

        AssertThat(scale.Domain.Count).IsEqual(2);
        AssertThat(scale.MapShape("a")).IsEqual(scale.MapShape("a"));
        AssertThat(scale.MapShape("b")).IsEqual(ShapeKind.Square);
    }

    [TestCase]
    public void MoreCategoriesThanShapesCyclesTheVocabulary()
    {
        var scale = Fitted("a", "b", "c", "d", "e", "f", "g");

        // Seven categories over six shapes: the seventh wraps back to the first one.
        AssertThat(scale.MapShape("a")).IsEqual(ShapeKind.Circle);
        AssertThat(scale.MapShape("f")).IsEqual(ShapeKind.Star);
        AssertThat(scale.MapShape("g")).IsEqual(ShapeKind.Circle);
    }

    [TestCase]
    public void UnknownCategoryFallsBackToTheFirstShape()
    {
        AssertThat(Fitted("a", "b").MapShape("nope")).IsEqual(ShapeKind.Circle);
        AssertThat(Fitted("a", "b").MapShape(null)).IsEqual(ShapeKind.Circle);
    }

    [TestCase]
    public void EmptyShapesRangeFallsBackToTheDefaultVocabulary()
    {
        var scale = Fitted("a", "b");
        scale.Shapes = [];

        AssertThat(scale.MapShape("a")).IsEqual(ShapeKind.Circle);
        AssertThat(scale.MapShape("b")).IsEqual(ShapeKind.Square);
    }

    [TestCase]
    public void CustomShapeRangeDecidesTheSymbols()
    {
        var scale = Fitted("a", "b");
        scale.Shapes = new[] { ShapeKind.Cross, ShapeKind.Star };

        AssertThat(scale.MapShape("a")).IsEqual(ShapeKind.Cross);
        AssertThat(scale.MapShape("b")).IsEqual(ShapeKind.Star);
    }

    // ── Scale interface ─────────────────────────────────────────────────────

    [TestCase]
    public void MapSpreadsTheCategoriesOverZeroToOne()
    {
        var scale = Fitted("a", "b", "c");

        Approx(scale.Map("a"), 0.0);
        Approx(scale.Map("b"), 0.5);
        Approx(scale.Map("c"), 1.0);
    }

    [TestCase]
    public void SingleCategoryAndUnknownValuesMapToTheMiddleAndZero()
    {
        Approx(Fitted("only").Map("only"), 0.5);
        Approx(Fitted("a", "b").Map("unknown"), 0.0);
    }

    [TestCase]
    public void FormatReturnsTheCategoryName()
    {
        AssertThat(Fitted("a").Format("a")).IsEqual("a");
    }

    [TestCase]
    public void DefaultVocabularyHasOneOfEachShape()
    {
        AssertThat(ShapeScale.DefaultShapes.Distinct().Count()).IsEqual(ShapeScale.DefaultShapes.Length);
    }

    // ── Ownership of the default vocabulary ─────────────────────────────────

    /// <summary>
    /// Locks the "static default arrays are never shared mutably" convention - the same rule
    /// <see cref="ChartTheme.DefaultPalette"/> follows: every read of
    /// <see cref="ShapeScale.DefaultShapes"/> hands out a fresh array, so writing into the result cannot
    /// repaint the symbols of the next scale.
    /// </summary>
    [TestCase]
    public void WritingIntoTheDefaultShapesCannotReachANewScale()
    {
        // Two reads are two arrays, never the same instance.
        var firstRead = ShapeScale.DefaultShapes;
        var secondRead = ShapeScale.DefaultShapes;
        AssertThat(ReferenceEquals(firstRead, secondRead)).IsFalse();

        var writable = ShapeScale.DefaultShapes;
        writable[0] = ShapeKind.Star;

        // The write stays in the caller's array: neither the default nor a scale built afterwards sees it.
        AssertThat(ShapeScale.DefaultShapes[0]).IsEqual(ShapeKind.Circle);
        AssertThat(new ShapeScale().Shapes[0]).IsEqual(ShapeKind.Circle);
    }

    /// <summary>
    /// Every owner keeps its own array: two scales, and the static default, are all independent, so an
    /// edit on one of them repaints nothing else.
    /// </summary>
    [TestCase]
    public void InstanceShapeArraysAreIsolatedFromEachOther()
    {
        var first = new ShapeScale();
        var second = new ShapeScale();

        AssertThat(ReferenceEquals(first.Shapes, second.Shapes)).IsFalse();
        AssertThat(ReferenceEquals(first.Shapes, ShapeScale.DefaultShapes)).IsFalse();

        first.Shapes[0] = ShapeKind.Cross;
        AssertThat(second.Shapes[0]).IsEqual(ShapeKind.Circle);
        AssertThat(ShapeScale.DefaultShapes[0]).IsEqual(ShapeKind.Circle);
    }
}
