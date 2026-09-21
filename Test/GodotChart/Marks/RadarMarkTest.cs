namespace GodotNodeExtension.Tests.GodotChart.Marks;

using System;
using System.Collections.Generic;
using System.Linq;
using GdUnit4;
using Godot;
using GodotNodeExtension.Component.GodotChart;
using GodotNodeExtension.Component.GodotChart.Marks;
using Support;
using static GdUnit4.Assertions;

/// <summary>
/// Behaviour specification for <see cref="RadarMark"/>.
/// <para>
/// Covers the polar grid and the dimension labels (which are drawn independently of each other),
/// the per-dimension vertex geometry (point radius, outer radius, minimum dimension count), the
/// hidden-series filter and the nearest-vertex hit test.
/// </para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class RadarMarkTest
{
    private static readonly string[] ThreeCategories = { "A", "B", "C" };
    private static readonly string[] FourCategories = { "A", "B", "C", "D" };

    // ── shared helpers ──

    /// <summary>Local floating point comparison: mark geometry is never bit-exact.</summary>
    private static bool Approx(double a, double b, double tol = 0.01) => Math.Abs(a - b) <= tol;

    private static (RadarMark Mark, FakeCanvas2D Canvas, MarkContext Ctx) RadarCtx(
        List<DataRow> rows, IReadOnlySet<string>? hiddenSeries = null)
    {
        var canvas = new FakeCanvas2D();
        var encodes = new EncodeSet();
        encodes.Set(Channel.X, new FieldEncode("cat"));
        encodes.Set(Channel.Y, new FieldEncode("value"));
        encodes.Set(Channel.Color, new FieldEncode("series"));
        var ctx = TestContexts.Mark(canvas, rows, encodes,
            TestContexts.CategoryScales(ThreeCategories, 0, 20), hiddenSeries: hiddenSeries);
        return (new RadarMark(), canvas, ctx);
    }

    // ── Default rendering ──

    [TestCase]
    public void RadarGridRingCountDrivesTheStrokeCount()
    {
        // Strokes: GridRings rings + one axis line per dimension (3) + one outline per series (2).
        foreach (var rings in new[] { 1, 5 })
        {
            var (mark, canvas, ctx) = RadarCtx(MarkCases.Series());
            mark.GridRings = rings;
            mark.Render(ctx);
            AssertThat(canvas.StrokeCount).IsEqual(rings + 3 + 2);
        }
    }

    // ── Options ──

    [TestCase]
    public void RadarPointRadiusSetsTheDotSize()
    {
        var (mark, canvas, ctx) = RadarCtx(MarkCases.Series());
        mark.PointRadius = 7f;
        mark.Render(ctx);

        AssertThat(canvas.Circles.Count).IsEqual(6);   // 3 dimensions x 2 series
        AssertThat(canvas.Circles.All(c => Approx(c.Radius, 7))).IsTrue();
    }

    [TestCase]
    public void RadarRadiusFactorScalesTheVertices()
    {
        float MaxVertexRadius(float factor)
        {
            var (mark, canvas, ctx) = RadarCtx(MarkCases.Series());
            mark.RadiusFactor = factor;
            mark.Render(ctx);

            const float cx = 200f, cy = 150f;
            return canvas.Circles.Max(c =>
                MathF.Sqrt((c.Cx - cx) * (c.Cx - cx) + (c.Cy - cy) * (c.Cy - cy)));
        }

        // The domain is [0, 20] and the largest value is 20, so one vertex sits on the outer radius.
        AssertThat(Approx(MaxVertexRadius(0.5f), 75, 0.5)).IsTrue();
        AssertThat(Approx(MaxVertexRadius(0.9f), 135, 0.5)).IsTrue();
    }

    [TestCase]
    public void RadarAxisLabelsAreIndependentOfTheGrid()
    {
        // ShowGrid off removes the rings, axis lines and ticks, but the dimension labels stay:
        // ShowAxisLabels defaults to true and is no longer consulted inside DrawPolarGrid.
        var (mark, canvas, ctx) = RadarCtx(MarkCases.Series());
        mark.ShowGrid = false;
        mark.Render(ctx);

        AssertThat(canvas.StrokeCount).IsEqual(2);    // only the two series outlines
        AssertThat(canvas.Texts.Count).IsEqual(3);    // the three dimension labels, no tick texts
        AssertThat(canvas.Texts).Contains("A");
        AssertThat(canvas.Texts).Contains("B");
        AssertThat(canvas.Texts).Contains("C");

        // With the grid on, the labels are still drawn.
        var (grid, gridCanvas, gridCtx) = RadarCtx(MarkCases.Series());
        grid.Render(gridCtx);
        AssertThat(gridCanvas.Texts).Contains("A");
        AssertThat(gridCanvas.Texts).Contains("B");
    }

    [TestCase]
    public void RadarGridStaysVisibleWhenTheAxisLabelsAreDisabled()
    {
        // The opposite direction of the decoupling: turning the labels off leaves the grid alone.
        var (mark, canvas, ctx) = RadarCtx(MarkCases.Series());
        mark.ShowAxisLabels = false;
        mark.Render(ctx);

        // Default GridRings (5) + one axis line per dimension (3) + one outline per series (2).
        AssertThat(canvas.StrokeCount).IsEqual(5 + 3 + 2);
        AssertThat(canvas.Texts.Contains("A")).IsFalse();
        AssertThat(canvas.Texts.Contains("B")).IsFalse();
    }

    // ── Hit testing ──

    [TestCase]
    public void RadarHitTestSnapsToTheNearestVertex()
    {
        var (mark, _, ctx) = RadarCtx(MarkCases.Series());
        mark.Render(ctx);

        // Dimension "A" carries 10 of the [0, 20] domain (half the outer radius) and sits straight up.
        var hit = mark.HitTest(ctx, new Vector2(200f, 150f - 135f * 0.5f));

        AssertThat(hit is not null).IsTrue();
        AssertThat(hit!.RowIndex).IsEqual(0);
        AssertThat(hit.Label).IsEqual("A: 10");
        AssertThat(Approx(hit.ScreenX, 200f, 0.5)).IsTrue();
    }

    // ── Degenerate data ──

    [TestCase]
    public void RadarNeedsAtLeastThreeDimensions()
    {
        var rows = new List<DataRow>
        {
            D(("cat", "A"), ("value", 10.0), ("series", "S1")),
            D(("cat", "B"), ("value", 5.0), ("series", "S1")),
        };
        var (mark, canvas, ctx) = RadarCtx(rows);
        mark.Render(ctx);

        AssertThat(canvas.DrewAnything).IsFalse();
        AssertThat(mark.HitTest(ctx, new Vector2(200f, 150f)) is null).IsTrue();
    }

    [TestCase]
    public void RadarHiddenSeriesIsSkipped()
    {
        var (all, allCanvas, allCtx) = RadarCtx(MarkCases.Series());
        all.Render(allCtx);
        AssertThat(allCanvas.FillCount).IsEqual(8);   // 2 polygon fills + 6 dots

        var (mark, canvas, ctx) = RadarCtx(MarkCases.Series(), new HashSet<string> { "S1" });
        mark.Render(ctx);
        AssertThat(canvas.FillCount).IsEqual(4);      // polygon + 3 dots of S2 only
        AssertThat(canvas.Circles.Count).IsEqual(3);
    }

    // ── Layout cache ──

    /// <summary>
    /// The cached dimension list is keyed with <c>Mark.CacheKey</c> (owner + layout version + data
    /// list + hidden series) instead of the bare data version: a mark instance shared by two charts
    /// used to keep the first chart's dimensions and label every axis of the second one with them.
    /// </summary>
    [TestCase]
    public void RadarRebuildsItsDimensionsWhenTheDataListChanges()
    {
        var mark = new RadarMark();

        var first = new FakeCanvas2D();
        var firstCtx = TestContexts.Mark(first, MarkCases.Series(),
            RadarEncodes(), TestContexts.CategoryScales(ThreeCategories, 0, 20));
        mark.Render(firstCtx);
        AssertThat(first.TextDraws.Count(d => d.Text is "A" or "B" or "C")).IsEqual(3);

        // A different data list instance at the same data version (what a second chart reusing the
        // mark produces) with one dimension more.
        var second = new FakeCanvas2D();
        var secondCtx = TestContexts.Mark(second, SeriesWithFourDimensions(),
            RadarEncodes(), TestContexts.CategoryScales(FourCategories, 0, 20),
            dataVersion: firstCtx.DataVersion, layoutVersion: firstCtx.LayoutVersion);
        mark.Render(secondCtx);

        AssertThat(second.TextDraws.Count(d => d.Text is "A" or "B" or "C" or "D")).IsEqual(4);
    }

    /// <summary>One series spanning four dimensions.</summary>
    private static List<DataRow> SeriesWithFourDimensions()
    {
        var rows = new List<DataRow>();
        foreach (var (dim, value) in new[] { ("A", 10.0), ("B", 20.0), ("C", 15.0), ("D", 12.0) })
            rows.Add(D(("cat", dim), ("value", value), ("series", "S1")));
        return rows;
    }

    /// <summary>X = dimension, Y = value, Color = series: the channels every radar test uses.</summary>
    private static EncodeSet RadarEncodes()
    {
        var encodes = new EncodeSet();
        encodes.Set(Channel.X, new FieldEncode("cat"));
        encodes.Set(Channel.Y, new FieldEncode("value"));
        encodes.Set(Channel.Color, new FieldEncode("series"));
        return encodes;
    }

    // ── helper for data rows ──

    private static DataRow D(params (string Field, object? Value)[] fields) => TestContexts.Row(fields);
}
