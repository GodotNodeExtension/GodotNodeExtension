namespace GodotNodeExtension.Tests.GodotChart;

using System.Collections.Generic;
using GdUnit4;
using GodotNodeExtension.Component.GodotChart;
using GodotNodeExtension.Component.GodotChart.Marks;
using Support;
using static GdUnit4.Assertions;

/// <summary>
/// Behaviour specification for the <see cref="Channel.Shape"/> channel - G2 treats <c>shape</c> as a
/// first-class channel and so does this library: the chart auto-fits a <see cref="ShapeScale"/> for it,
/// marks draw the resulting symbol, and the legend shows that symbol instead of a square swatch.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class ShapeChannelTest
{
    private static DataRow D(params (string Field, object Value)[] fields)
    {
        var row = new DataRow(fields.Length);
        foreach (var (field, value) in fields)
            row.Set(field, value);
        return row;
    }

    private static List<DataRow> Points() =>

    [
        D(("x", 1.0), ("y", 5.0), ("kind", "a"), ("series", "s1")),
        D(("x", 2.0), ("y", 9.0), ("kind", "b"), ("series", "s2")),
        D(("x", 3.0), ("y", 7.0), ("kind", "c"), ("series", "s3")),
    ];

    private static Chart ChartWith(FakeCanvas2D canvas, bool shapeChannel, bool legend = false)
    {
        var chart = new Chart(canvas) { Width = 400f, Height = 300f };
        chart.Data(Points());
        chart.Mark(new PointMark());
        chart.Encode(Channel.X, "x");
        chart.Encode(Channel.Y, "y");
        if (shapeChannel) chart.Encode(Channel.Shape, legend ? "series" : "kind");
        if (legend)
        {
            // The legend is colour-driven, so it only shows symbols when the Shape channel covers the
            // same categories; binding both to "series" is the shape-encoded series case.
            chart.Encode(Channel.Color, "series");
            chart.Legend(new LegendConfig { Position = LegendPosition.Bottom });
        }
        return chart;
    }

    [TestCase]
    public void ShapeChannelIsAutoFittedAndDrivesTheGlyphs()
    {
        var canvas = new FakeCanvas2D();
        ChartWith(canvas, shapeChannel: true).Render();

        // Three categories, three symbols: the first is a disc, the second a square, the third a
        // polygon (triangle) built from path operations. Rect operations also include the chart
        // background, hence the "1 + 1".
        AssertThat(canvas.Circles.Count).IsEqual(1);
        AssertThat(canvas.Rects.Count).IsEqual(2);
        AssertThat(canvas.PathOpCount >= 3).IsTrue();
    }

    [TestCase]
    public void WithoutTheShapeChannelEveryPointStaysACircle()
    {
        var canvas = new FakeCanvas2D();
        ChartWith(canvas, shapeChannel: false).Render();

        AssertThat(canvas.Circles.Count).IsEqual(3);
        AssertThat(canvas.Rects.Count).IsEqual(1);   // the chart background only
    }

    [TestCase]
    public void RowsWithoutAShapeValueFallBackToACircle()
    {
        var canvas = new FakeCanvas2D();
        var rows = new List<DataRow>
        {
            D(("x", 1.0), ("y", 5.0), ("kind", "unknown")),
            D(("x", 2.0), ("y", 9.0)),
        };

        var chart = new Chart(canvas) { Width = 400f, Height = 300f };
        chart.Data(rows);
        chart.Mark(new PointMark());
        chart.Encode(Channel.X, "x");
        chart.Encode(Channel.Y, "y");
        chart.Encode(Channel.Shape, "kind");
        chart.Render();

        // An unknown category and a missing field both mean "the first symbol of the vocabulary".
        AssertThat(canvas.Circles.Count).IsEqual(2);
    }

    [TestCase]
    public void AManuallySetShapeScaleWins()
    {
        var canvas = new FakeCanvas2D();
        var chart = ChartWith(canvas, shapeChannel: true);
        chart.Scale(Channel.Shape, new ShapeScale { Shapes = new[] { ShapeKind.Cross, ShapeKind.Star } });
        chart.Render();

        // Both symbols are polygons, so no disc and no rectangle is drawn for the points.
        AssertThat(canvas.Circles.Count).IsEqual(0);
        AssertThat(canvas.Rects.Count).IsEqual(1);   // the chart background only
        AssertThat(canvas.PathOpCount >= 6).IsTrue();
    }

    [TestCase]
    public void LegendSwatchesFollowTheShapeChannel()
    {
        var canvas = new FakeCanvas2D();
        ChartWith(canvas, shapeChannel: true, legend: true).Render();

        // Three series: the first gets a disc and the second a square, and the legend repeats the same
        // symbols - so each appears once for the point and once for its legend swatch. The remaining
        // rectangle is the chart background.
        AssertThat(canvas.Circles.Count).IsEqual(2);
        AssertThat(canvas.Rects.Count).IsEqual(3);
    }

    [TestCase]
    public void LegendKeepsSquareSwatchesWithoutAShapeChannel()
    {
        var canvas = new FakeCanvas2D();
        var chart = ChartWith(canvas, shapeChannel: false, legend: true);
        chart.Render();

        // Three point discs plus three square swatches (and the chart background rectangle).
        AssertThat(canvas.Circles.Count).IsEqual(3);
        AssertThat(canvas.Rects.Count).IsEqual(4);
    }
}
