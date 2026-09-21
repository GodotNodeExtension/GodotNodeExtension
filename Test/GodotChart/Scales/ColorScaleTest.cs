namespace GodotNodeExtension.Tests.GodotChart.Scales;

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
/// Behaviour specification for <see cref="ColorScale"/> (categorical category-to-color mapping,
/// palette cycling, unknown/empty/one-category conventions) together with the <see cref="IColorScale"/>
/// contract the chart pipeline relies on when a caller supplies a sequential or diverging scale.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class ColorScaleTest
{
    /// <summary>Assert two doubles are within <paramref name="eps"/> (IsEqual is exact for doubles).</summary>
    private static void Approx(double actual, double expected, double eps = 1e-9)
        => AssertThat(Math.Abs(actual - expected) <= eps).IsTrue();

    // ── Categorical mapping ────────────────────────────────────────────────

    [TestCase]
    public void ColorScaleSpreadsEndpointsAcrossTheDomain()
    {
        var scale = new ColorScale();
        scale.Fit(new object[] { "A", "B", "C", "D", "E" });

        Approx(scale.Map("A"), 0.0);
        Approx(scale.Map("C"), 0.5);
        Approx(scale.Map("E"), 1.0);
    }

    [TestCase]
    public void ColorScaleSingleCategoryCentresAtHalf()
    {
        var scale = new ColorScale();
        scale.Fit(new object[] { "only" });

        Approx(scale.Map("only"), 0.5);
        AssertThat(scale.MapColor("only").ToHtml()).IsEqual(scale.Palette[0].ToHtml());
    }

    [TestCase]
    public void ColorScaleUnknownValueFallsBackToTheFirstColour()
    {
        var scale = new ColorScale();
        scale.Fit(new object[] { "A", "B", "C" });

        Approx(scale.Map("Z"), 0.0);
        // An unknown value is indistinguishable from the first category (both report Palette[0]).
        AssertThat(scale.MapColor("Z").ToHtml()).IsEqual(scale.Palette[0].ToHtml());
    }

    [TestCase]
    public void ColorScalePaletteCyclesWhenThereAreMoreCategoriesThanColours()
    {
        var scale = new ColorScale
        {
            Palette = new[] { new Color(1f, 0f, 0f), new Color(0f, 1f, 0f) },
        };
        scale.Fit(new object[] { "A", "B", "C" });

        AssertThat(scale.MapColor("A").ToHtml()).IsEqual(scale.Palette[0].ToHtml());
        AssertThat(scale.MapColor("B").ToHtml()).IsEqual(scale.Palette[1].ToHtml());
        AssertThat(scale.MapColor("C").ToHtml()).IsEqual(scale.Palette[0].ToHtml()); // 2 % 2
    }

    [TestCase]
    public void ColorScaleEmptyPaletteFallsBackToTheDefault()
    {
        var scale = new ColorScale { Palette = [] };
        scale.Fit(new object[] { "A" });

        // An empty palette means "use the default", not "crash": a known key and an unknown key
        // both resolve through ChartTheme.DefaultPalette.
        AssertThat(scale.MapColor("A").ToHtml()).IsEqual(ChartTheme.DefaultPalette[0].ToHtml());
        AssertThat(scale.MapColor("Z").ToHtml()).IsEqual(ChartTheme.DefaultPalette[0].ToHtml());
    }

    [TestCase]
    public void ColorScaleMapTreatsNullAsUnknown()
    {
        var scale = new ColorScale();
        scale.Fit(new object[] { "A" });

        AssertThat(scale.Map(null!)).IsEqual(0.0);
        AssertThat(scale.MapColor(null!).ToHtml()).IsEqual(ChartTheme.DefaultPalette[0].ToHtml());
        AssertThat(scale.Format(null!)).IsEqual("");
    }

    [TestCase]
    public void ColorScaleTreatsAColourLikeStringAsACategory()
    {
        // The reason IdentityColorScale exists: naming a field is the binding, not the colour. The
        // categorical scale always assigns palette colours, even for a value that looks like a colour.
        var scale = new ColorScale();
        scale.Fit(new object[] { "#ff8800" });

        AssertThat(scale.MapColor("#ff8800").ToHtml()).IsEqual(scale.Palette[0].ToHtml());
        AssertThat(scale.MapColor("#ff8800").ToHtml()).IsNotEqual(new Color("#ff8800").ToHtml());
    }

    // ── Identity: the data carries the colours ─────────────────────────────

    [TestCase]
    public void IdentityScaleUsesColourValuesAsTheyAre()
    {
        var scale = new IdentityColorScale();
        scale.Fit(new object[] { Colors.Red, "#00ff00" });

        AssertThat(scale.MapColor(Colors.Red).ToHtml()).IsEqual(Colors.Red.ToHtml());
        AssertThat(scale.MapColor("#00ff00").ToHtml()).IsEqual(Colors.Green.ToHtml());
        // Every HTML hexadecimal form Godot accepts works, including the short and alpha variants.
        AssertThat(scale.MapColor("#f00").ToHtml()).IsEqual(Colors.Red.ToHtml());
        AssertThat(scale.MapColor("#0000ff80").A < 1f).IsTrue();
    }

    [TestCase]
    public void IdentityScaleFallsBackToThePaletteForValuesThatAreNotColours()
    {
        // A category that merely *reads* like a colour name stays a category: only Color values and
        // "#..." strings are treated as colours.
        var scale = new IdentityColorScale();
        scale.Fit(new object[] { "red", "blue", "#123456" });

        AssertThat(scale.MapColor("red").ToHtml()).IsEqual(ChartTheme.DefaultPalette[0].ToHtml());
        AssertThat(scale.MapColor("blue").ToHtml()).IsEqual(ChartTheme.DefaultPalette[1].ToHtml());
        AssertThat(scale.MapColor("#123456").ToHtml()).IsEqual(new Color("#123456").ToHtml());
    }

    [TestCase]
    public void IdentityScaleIsNotCategoricalSoItCannotDriveALegend()
    {
        var scale = new IdentityColorScale();

        // Runtime check: IdentityColorScale must not implement ICategoricalColorScale.
        AssertThat(typeof(ICategoricalColorScale).IsAssignableFrom(scale.GetType())).IsFalse();
    }

    // ── IColorScale consumption by the chart pipeline ──────────────────────

    private static List<DataRow> MatrixRows() =>
    [
        TestContexts.Row(("x", "A"), ("y", "1"), ("value", -1.0)),
        TestContexts.Row(("x", "B"), ("y", "1"), ("value", 0.0)),
        TestContexts.Row(("x", "A"), ("y", "2"), ("value", 1.0)),
        TestContexts.Row(("x", "B"), ("y", "2"), ("value", 0.5)),
    ];

    private static Chart HeatmapChart(FakeCanvas2D canvas, out CapturingMark probe)
    {
        probe = new CapturingMark();
        var chart = new Chart(canvas);
        chart.Data(MatrixRows());
        chart.Mark(new HeatmapMark());
        chart.Mark(probe);
        chart.Encode(Channel.X, "x");
        chart.Encode(Channel.Y, "y");
        chart.Encode(Channel.Color, "value");
        return chart;
    }

    private static Chart BarChart(FakeCanvas2D canvas, List<DataRow> rows)
    {
        var chart = new Chart(canvas);
        chart.Data(rows);
        chart.Mark(new IntervalMark());
        chart.Encode(Channel.X, "cat");
        chart.Encode(Channel.Y, "value");
        return chart;
    }

    [TestCase]
    public void ColoursInTheDataAreUsedAsTheyAre()
    {
        var canvas = new FakeCanvas2D();
        var chart = BarChart(canvas,
        [
            TestContexts.Row(("cat", "A"), ("value", 10.0), ("color", Colors.Red)),
            TestContexts.Row(("cat", "B"), ("value", 20.0), ("color", "#0000ff")),
        ]);
        chart.Encode(Channel.Color, "color");

        chart.Render();

        var fills = canvas.FillColors.Select(c => c.ToHtml()).ToList();
        AssertThat(fills.Contains(Colors.Red.ToHtml())).IsTrue();
        AssertThat(fills.Contains(Colors.Blue.ToHtml())).IsTrue();
        // The values are colours, not categories, so they cannot drive a legend.
        AssertThat(chart.GetSeriesInfo().Count).IsEqual(0);
    }

    [TestCase]
    public void AConstantColourChannelPaintsTheWholeMark()
    {
        var canvas = new FakeCanvas2D();
        var chart = BarChart(canvas,
        [
            TestContexts.Row(("cat", "A"), ("value", 10.0)),
            TestContexts.Row(("cat", "B"), ("value", 20.0)),
        ]);
        chart.Encode(Channel.Color, Colors.Red);   // the type-safe constant overload

        chart.Render();

        // Without the constant-colour path every bar would fall back to the mark's default colour. The
        // chart fills its own background first, so leave that one out.
        string background = ChartTheme.Dark().BackgroundColor.ToHtml();
        var barFills = canvas.FillColors
            .Where(c => c.ToHtml() != background)
            .Select(c => c.ToHtml())
            .Distinct()
            .ToList();

        AssertThat(barFills.Count).IsEqual(1);
        AssertThat(barFills[0]).IsEqual(Colors.Red.ToHtml());
    }

    [TestCase]
    public void AConstantColourStringChannelWorksToo()
    {
        var canvas = new FakeCanvas2D();
        var chart = BarChart(canvas,
        [
            TestContexts.Row(("cat", "A"), ("value", 10.0)),
        ]);
        chart.Encode(Channel.Color, "constant:#00ff00");

        chart.Render();

        AssertThat(canvas.FillColors.Any(c => c.ToHtml() == Colors.Green.ToHtml())).IsTrue();
    }

    [TestCase]
    public void UserProvidedDivergingScaleSurvivesHeatmapContribution()
    {
        var canvas = new FakeCanvas2D();
        var chart = HeatmapChart(canvas, out var probe);
        chart.Scale(Channel.Color, new DivergingColorScale(-1, 1));

        chart.Render();

        AssertThat(probe.LastColorScaleType).IsEqual(nameof(DivergingColorScale));
        AssertThat(canvas.FillCount > 0).IsTrue();
    }

    [TestCase]
    public void UserProvidedSequentialScaleSurvivesHeatmapContribution()
    {
        var canvas = new FakeCanvas2D();
        var chart = HeatmapChart(canvas, out var probe);
        chart.Scale(Channel.Color, new SequentialColorScale { Gradient = ChartTheme.DefaultSequentialGradient });

        chart.Render();

        AssertThat(probe.LastColorScaleType).IsEqual(nameof(SequentialColorScale));
    }

    [TestCase]
    public void HeatmapInstallsASequentialScaleWhenTheColorScaleIsCategorical()
    {
        var canvas = new FakeCanvas2D();
        var chart = HeatmapChart(canvas, out var probe);

        chart.Render(); // auto-fit installs a categorical ColorScale which the heatmap replaces

        AssertThat(probe.LastColorScaleType).IsEqual(nameof(SequentialColorScale));
    }

    [TestCase]
    public void DivergingScaleProducesDistinctColorsPerValue()
    {
        var canvas = new FakeCanvas2D();
        var chart = HeatmapChart(canvas, out _);
        chart.Scale(Channel.Color, new DivergingColorScale(-1, 1));

        chart.Render();

        var distinct = canvas.FillColors.Select(c => c.ToHtml()).Distinct().Count();
        AssertThat(distinct > 1).IsTrue();
    }

    [TestCase]
    public void ColorResolutionWorksThroughTheInterfaceForPointMarks()
    {
        var canvas = new FakeCanvas2D();
        var chart = new Chart(canvas);
        chart.Data(new List<DataRow>
        {
            TestContexts.Row(("x", 1.0), ("y", 1.0), ("score", -1.0)),
            TestContexts.Row(("x", 2.0), ("y", 2.0), ("score", 1.0)),
        });
        chart.Mark(new PointMark());
        chart.Encode(Channel.X, "x");
        chart.Encode(Channel.Y, "y");
        chart.Encode(Channel.Color, "score");
        var diverging = new DivergingColorScale(-1, 1);
        chart.Scale(Channel.Color, diverging);

        chart.Render();

        // Both ends of the diverging scale must show up as element colors.
        var expectedLow = diverging.MapColor(-1.0);
        var expectedHigh = diverging.MapColor(1.0);
        AssertThat(canvas.FillColors.Any(c => c.ToHtml() == expectedLow.ToHtml())).IsTrue();
        AssertThat(canvas.FillColors.Any(c => c.ToHtml() == expectedHigh.ToHtml())).IsTrue();
    }
}
