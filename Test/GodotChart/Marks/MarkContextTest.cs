namespace GodotNodeExtension.Tests.GodotChart.Marks;

using System;
using System.Collections.Generic;
using GdUnit4;
using GodotNodeExtension.Component.GodotChart;
using Support;
using static GdUnit4.Assertions;

/// <summary>
/// Contract of <see cref="MarkContext.WithData"/>: it returns a shallow copy that swaps only the
/// data list, sharing every other reference (plot, canvas, scales, encodes, theme, animation,
/// hidden series, owner) and copying the scalar versions and indices.
/// </summary>
// [RequireGodotRuntime] stays: the suite builds a ChartTheme to check that WithData shares it, and
// a ChartTheme is a Godot Resource, so the suite cannot run without the engine.
[TestSuite]
[RequireGodotRuntime]
public class MarkContextTest
{
    private static readonly string[] SingleCategory = { "A" };

    /// <summary>Approximate comparison for computed floating point values.</summary>
    private static bool Approx(double actual, double expected, double tolerance = 1e-6)
        => Math.Abs(actual - expected) <= tolerance;

    /// <summary>Assert that <paramref name="actual"/> is within <paramref name="tolerance"/> of the expected value.</summary>
    private static void AssertApprox(double actual, double expected, double tolerance = 1e-6)
        => AssertThat(Approx(actual, expected, tolerance)).IsTrue();

    [TestCase]
    public void MarkContextWithDataSharesEveryOtherField()
    {
        var canvas = new FakeCanvas2D();
        var data = new List<DataRow> { TestContexts.Row(("x", 1.0)) };
        var replacement = new List<DataRow> { TestContexts.Row(("x", 2.0)), TestContexts.Row(("x", 3.0)) };
        var scales = TestContexts.CategoryScales(SingleCategory);
        var encodes = TestContexts.XyEncodes("x", "y");
        var theme = ChartTheme.Dark();
        var hidden = new HashSet<string> { "A" };
        var animation = new AnimationContext { EntryProgress = 0.5f, GlobalOpacity = 0.25f };

        var ctx = TestContexts.Mark(canvas, data, encodes, scales, theme: theme, plot: new PlotArea(1, 2, 3, 4),
            hoveredRowIndex: 2, dataVersion: 7, layoutVersion: 8, animation: animation,
            selectedRowIndex: 3, focusedSeries: "A", hiddenSeries: hidden);
        var owner = new object();
        ctx.OwnerId = owner;

        var copy = ctx.WithData(replacement);

        AssertThat(ReferenceEquals(copy.Data, replacement)).IsTrue();
        AssertThat(copy.Plot == ctx.Plot).IsTrue();
        AssertThat(ReferenceEquals(copy.Canvas, ctx.Canvas)).IsTrue();
        AssertThat(ReferenceEquals(copy.Scales, ctx.Scales)).IsTrue();
        AssertThat(ReferenceEquals(copy.Encodes, ctx.Encodes)).IsTrue();
        AssertThat(ReferenceEquals(copy.Theme, ctx.Theme)).IsTrue();
        AssertThat(ReferenceEquals(copy.HiddenSeries, ctx.HiddenSeries)).IsTrue();
        AssertThat(ReferenceEquals(copy.Animation, ctx.Animation)).IsTrue();
        AssertThat(ReferenceEquals(copy.OwnerId, owner)).IsTrue();
        AssertThat(copy.DataVersion).IsEqual(7);
        AssertThat(copy.LayoutVersion).IsEqual(8);
        AssertThat(copy.HoveredRowIndex).IsEqual(2);
        AssertThat(copy.SelectedRowIndex).IsEqual(3);
        AssertThat(copy.FocusedSeries).IsEqual("A");
        AssertApprox(copy.AnimationProgress, 0.5);
    }
}
