namespace GodotNodeExtension.Tests.GodotChart;

using System;
using GdUnit4;
using GodotNodeExtension.Component.GodotChart;
using Support;
using static GdUnit4.Assertions;

/// <summary>
/// <see cref="ShapeGeometry.Build"/>: the symbol vocabulary the marks and the legend draw from. The recorder
/// counts the path operations (MoveTo/LineTo for a polygon, Rect, Circle) and keeps the rectangle/circle
/// geometry, so each glyph is asserted by what it asks the canvas to do.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class ShapeGeometryTest
{
    private const float Cx = 50f;
    private const float Cy = 60f;
    private const float Radius = 10f;

    /// <summary>Build one symbol at (Cx, Cy) and hand back the canvas that recorded the operations.</summary>
    private static FakeCanvas2D Build(ShapeKind shape, float radius = Radius)
    {
        var canvas = new FakeCanvas2D();
        using var path = canvas.CreatePath();
        ShapeGeometry.Build(path, shape, Cx, Cy, radius);
        return canvas;
    }

    [TestCase]
    public void TheSquareIsOneRectangleAroundTheCentre()
    {
        var canvas = Build(ShapeKind.Square);

        AssertThat(canvas.PathOpCount).IsEqual(1);
        AssertThat(canvas.Rects.Count).IsEqual(1);
        var (x, y, w, h) = canvas.Rects[0];
        AssertThat(x).IsEqual(Cx - Radius);
        AssertThat(y).IsEqual(Cy - Radius);
        AssertThat(w).IsEqual(Radius * 2f);
        AssertThat(h).IsEqual(Radius * 2f);
    }

    [TestCase]
    public void TheCircleIsADiscOfTheGivenRadius()
    {
        var canvas = Build(ShapeKind.Circle);

        AssertThat(canvas.PathOpCount).IsEqual(1);
        AssertThat(canvas.Circles.Count).IsEqual(1);
        var (cx, cy, radius) = canvas.Circles[0];
        AssertThat(cx).IsEqual(Cx);
        AssertThat(cy).IsEqual(Cy);
        AssertThat(radius).IsEqual(Radius);
    }

    /// <summary>
    /// The polygons are point counts: a triangle is three operations, a diamond four, a star ten (five outer
    /// points and five inner ones) and a cross twelve. The recorder does not count <c>Close()</c>, which is why
    /// these are the point counts and not one more.
    /// </summary>
    [TestCase]
    public void EachKindDrawsItsOwnNumberOfPoints()
    {
        AssertThat(Build(ShapeKind.Triangle).PathOpCount).IsEqual(3);
        AssertThat(Build(ShapeKind.Diamond).PathOpCount).IsEqual(4);
        AssertThat(Build(ShapeKind.Star).PathOpCount).IsEqual(10);
        AssertThat(Build(ShapeKind.Cross).PathOpCount).IsEqual(12);

        // ...and none of them is a rectangle or a disc.
        AssertThat(Build(ShapeKind.Triangle).Rects.Count).IsEqual(0);
        AssertThat(Build(ShapeKind.Star).Circles.Count).IsEqual(0);
    }

    /// <summary>
    /// Every kind the enum declares has a glyph. A member added without a branch would fall into the disc
    /// default and silently draw the wrong symbol for every mark that uses it.
    /// </summary>
    [TestCase]
    public void EveryShapeKindProducesGeometry()
    {
        var problems = "";
        foreach (ShapeKind shape in Enum.GetValues<ShapeKind>())
        {
            if (Build(shape).PathOpCount == 0) problems += $"{shape} produced nothing; ";
        }

        AssertThat(problems).IsEqual("");
    }

    /// <summary>A radius of zero (or less) draws nothing: there is no symbol to draw.</summary>
    [TestCase]
    public void ANonPositiveRadiusDrawsNothing()
    {
        AssertThat(Build(ShapeKind.Square, radius: 0f).PathOpCount).IsEqual(0);
        AssertThat(Build(ShapeKind.Circle, radius: -3f).PathOpCount).IsEqual(0);
    }
}
