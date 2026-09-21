namespace GodotNodeExtension.Tests.GodotChart;

using System.Collections.Generic;
using System.Linq;
using GdUnit4;
using Godot;
using GodotNodeExtension.Component.GodotChart;
using GodotNodeExtension.Component.GodotChart.Canvas;
using GodotNodeExtension.Component.GodotChart.Marks;
using Support;
using static GdUnit4.Assertions;

/// <summary>
/// Tests for <see cref="ChartTheme"/>, the default palette/gradient constants and
/// <see cref="ChartDefaults"/>: the dark and light presets, the deep clone of the colour arrays,
/// the default state of a fresh <see cref="Chart"/>, and how the theme values reach the axis
/// labels and the tooltip.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class ChartThemeTest
{
    private static DataRow D(params (string Field, object Value)[] fields)
    {
        var row = new DataRow(fields.Length);
        foreach (var (field, value) in fields)
            row.Set(field, value);
        return row;
    }

    // ── Dark and light presets ──────────────────────────────────────────────

    [TestCase]
    public void DarkThemeKeyDefaults()
    {
        var t = ChartTheme.Dark();

        AssertThat(t.BackgroundColor).IsEqual(new Color(0.08f, 0.08f, 0.12f));
        AssertThat(t.BackgroundCornerRadius).IsEqual(8f);
        AssertThat(t.GridLineWidth).IsEqual(1f);
        AssertThat(t.AxisLineWidth).IsEqual(2f);
        AssertThat(t.AxisTitleMargin).IsEqual(6f);
        AssertThat(t.TitleReservedHeight).IsEqual(24f);
        AssertThat(t.Y2LabelReservedWidth).IsEqual(35f);
        AssertThat(t.TitleFontSize).IsEqual(13f);
        AssertThat(t.LabelFontSize).IsEqual(13f);
        AssertThat(t.TitleColor).IsEqual(new Color(1f, 1f, 1f, 0.85f));
        AssertThat(t.LabelColor).IsEqual(new Color(1f, 1f, 1f, 0.6f));
        AssertThat(t.HoverBrighten).IsEqual(1.2f);
        AssertThat(t.UnfocusedOpacity).IsEqual(0.15f);
        AssertThat(t.FallbackTickCount).IsEqual(5);
        AssertThat(t.TooltipFontSize).IsEqual(12f);
        AssertThat(t.TooltipPadding).IsEqual(8f);
        AssertThat(t.EnableAnimation).IsTrue();
        AssertThat(t.EnableCrosshair).IsTrue();
        AssertThat(t.EnableTooltip).IsTrue();
    }

    [TestCase]
    public void LightThemeOverridesTheKeyValues()
    {
        var dark = ChartTheme.Dark();
        var light = ChartTheme.Light();

        AssertThat(light.BackgroundColor).IsEqual(new Color(0.98f, 0.98f, 0.96f));
        AssertThat(light.GridColor).IsEqual(new Color(0f, 0f, 0f, 0.08f));
        AssertThat(light.AxisColor).IsEqual(new Color(0f, 0f, 0f, 0.5f));
        AssertThat(light.TitleColor).IsEqual(new Color(0.1f, 0.1f, 0.1f));
        AssertThat(light.HoverBrighten).IsEqual(1.15f);
        AssertThat(light.UnfocusedOpacity).IsEqual(0.2f);
        AssertThat(light.PointDefaultOpacity).IsEqual(0.85f);
        AssertThat(light.TooltipBackground).IsEqual(new Color(1f, 1f, 1f, 0.95f));
        AssertThat(light.CrosshairColor).IsEqual(new Color(0f, 0f, 0f, 0.3f));

        // Every overridden value really differs from the dark base.
        AssertThat(light.BackgroundColor).IsNotEqual(dark.BackgroundColor);
        AssertThat(light.TitleColor).IsNotEqual(dark.TitleColor);
    }

    [TestCase]
    public void LightThemeInheritsTheUnchangedValues()
    {
        var dark = ChartTheme.Dark();
        var light = ChartTheme.Light();

        AssertThat(light.LabelFontSize).IsEqual(dark.LabelFontSize);
        AssertThat(light.TitleFontSize).IsEqual(dark.TitleFontSize);
        AssertThat(light.AxisTitleMargin).IsEqual(dark.AxisTitleMargin);
        AssertThat(light.TitleReservedHeight).IsEqual(dark.TitleReservedHeight);
        AssertThat(light.Y2LabelReservedWidth).IsEqual(dark.Y2LabelReservedWidth);
        AssertThat(light.FallbackTickCount).IsEqual(dark.FallbackTickCount);
        AssertThat(light.EnableTooltip).IsEqual(dark.EnableTooltip);
        AssertThat(light.EnableAnimation).IsEqual(dark.EnableAnimation);
        // The light theme does not touch the sequential gradient.
        AssertThat(light.SequentialGradient.Length).IsEqual(dark.SequentialGradient.Length);
    }

    [TestCase]
    public void DefaultPaletteHasSixStops()
    {
        var palette = ChartTheme.DefaultPalette;

        AssertThat(palette.Length).IsEqual(6);
        AssertThat(palette[0]).IsEqual(new Color(0.29f, 0.59f, 0.98f));
        AssertThat(palette[5]).IsEqual(new Color(0.29f, 0.92f, 0.98f));
    }

    [TestCase]
    public void DefaultSequentialGradientHasFiveStops()
    {
        var gradient = ChartTheme.DefaultSequentialGradient;

        AssertThat(gradient.Length).IsEqual(5);
        AssertThat(gradient[0]).IsEqual(new Color(0.12f, 0.07f, 0.53f));
        AssertThat(gradient[4]).IsEqual(new Color(0.99f, 0.95f, 0.70f));
    }

    /// <summary>
    /// The static palettes are read-only <i>by copy</i>: the returned array is a fresh clone, so a caller
    /// that writes into it cannot poison the defaults every new theme - or every suite that uses
    /// <c>ChartTheme.DefaultPalette</c> as its expected value - starts from.
    /// </summary>
    [TestCase]
    public void WritingIntoTheDefaultPaletteCannotReachAnyTheme()
    {
        var firstPaletteColor = new Color(0.29f, 0.59f, 0.98f);
        var lastGradientColor = new Color(0.99f, 0.95f, 0.70f);

        AssertThat(ChartTheme.DefaultPalette[0]).IsEqual(firstPaletteColor);
        AssertThat(ChartTheme.DefaultSequentialGradient[4]).IsEqual(lastGradientColor);

        // A caller that treats the result as scratch space...
        ChartTheme.DefaultPalette[0] = new Color(1f, 0f, 1f);
        ChartTheme.DefaultSequentialGradient[4] = new Color(1f, 0f, 1f);

        // ...changes neither the next read, nor the shared instance, nor a theme (or a scale) built
        // afterwards - they all start from the untouched copy.
        AssertThat(ChartTheme.DefaultPalette[0]).IsEqual(firstPaletteColor);
        AssertThat(ChartTheme.DefaultSequentialGradient[4]).IsEqual(lastGradientColor);
        AssertThat(ChartTheme.Default.Palette[0]).IsEqual(firstPaletteColor);
        AssertThat(ChartTheme.Default.SequentialGradient[4]).IsEqual(lastGradientColor);
        AssertThat(ChartTheme.Dark().Palette[0]).IsEqual(firstPaletteColor);
        AssertThat(ChartTheme.Light().SequentialGradient[4]).IsEqual(lastGradientColor);
        AssertThat(new ColorScale().Palette[0]).IsEqual(firstPaletteColor);
    }

    /// <summary>Every read hands out its own array - the copy is per call, not one cached instance.</summary>
    [TestCase]
    public void EveryReadOfTheDefaultPalettesReturnsAFreshArray()
    {
        // Two reads are two arrays, never the same instance.
        var firstPaletteRead = ChartTheme.DefaultPalette;
        var secondPaletteRead = ChartTheme.DefaultPalette;
        AssertThat(ReferenceEquals(firstPaletteRead, secondPaletteRead)).IsFalse();

        var firstGradientRead = ChartTheme.DefaultSequentialGradient;
        var secondGradientRead = ChartTheme.DefaultSequentialGradient;
        AssertThat(ReferenceEquals(firstGradientRead, secondGradientRead)).IsFalse();

        // ...and neither aliases the array a theme holds, so an in-place edit of a theme stays local.
        AssertThat(ReferenceEquals(ChartTheme.DefaultPalette, ChartTheme.Dark().Palette)).IsFalse();
        AssertThat(ReferenceEquals(ChartTheme.DefaultSequentialGradient, ChartTheme.Dark().SequentialGradient)).IsFalse();
    }

    [TestCase]
    public void CloneDeepCopiesTheArrays()
    {
        var original = ChartTheme.Dark();
        var clone = original.Clone();

        AssertThat(ReferenceEquals(original, clone)).IsFalse();
        AssertThat(clone.Palette.Length).IsEqual(original.Palette.Length);

        // Mutating the clone's arrays must never leak into the original (or the static palette).
        var sentinel = new Color(1f, 0f, 1f);
        clone.Palette[0] = sentinel;
        clone.SequentialGradient[0] = sentinel;

        AssertThat(original.Palette[0]).IsEqual(new Color(0.29f, 0.59f, 0.98f));
        AssertThat(original.SequentialGradient[0]).IsEqual(new Color(0.12f, 0.07f, 0.53f));
        AssertThat(ChartTheme.DefaultPalette[0]).IsEqual(new Color(0.29f, 0.59f, 0.98f));
    }

    /// <summary>
    /// The theme has no arc gap and no ring gap: those are per-mark settings
    /// (<see cref="ChordMark.ArcGap"/>, <see cref="SunburstMark.ArcGap"/> and
    /// <see cref="SunburstMark.RingGap"/>), and a theme member that no renderer ever read - with a value
    /// that disagreed with the mark default - was worse than none.
    /// </summary>
    [TestCase]
    public void TheThemeHasNoArcOrRingGap()
    {
        AssertThat(typeof(ChartTheme).GetProperty("ArcGap") is null).IsTrue();
        AssertThat(typeof(ChartTheme).GetProperty("RingGap") is null).IsTrue();

        // The real switches are the marks' own values.
        AssertThat(new ChordMark().ArcGap).IsEqual(0.04f);
        AssertThat(new SunburstMark().ArcGap).IsEqual(0.02f);
        AssertThat(new SunburstMark().RingGap).IsEqual(2f);
    }

    // ── ChartDefaults and constructor state ─────────────────────────────────

    [TestCase]
    public void ChartDefaultsExposeTheLayoutConstants()
    {
        AssertThat(ChartDefaults.PaddingLeft).IsEqual(50f);
        AssertThat(ChartDefaults.PaddingRight).IsEqual(20f);
        AssertThat(ChartDefaults.PaddingTop).IsEqual(20f);
        AssertThat(ChartDefaults.PaddingBottom).IsEqual(40f);
        AssertThat(ChartDefaults.Width).IsEqual(600f);
        AssertThat(ChartDefaults.Height).IsEqual(400f);
    }

    [TestCase]
    public void NewChartStartsWithTheDefaultState()
    {
        var chart = new Chart(new FakeCanvas2D());

        AssertThat(chart.PaddingLeft).IsEqual(ChartDefaults.PaddingLeft);
        AssertThat(chart.PaddingRight).IsEqual(ChartDefaults.PaddingRight);
        AssertThat(chart.PaddingTop).IsEqual(ChartDefaults.PaddingTop);
        AssertThat(chart.PaddingBottom).IsEqual(ChartDefaults.PaddingBottom);
        AssertThat(chart.Width).IsEqual(ChartDefaults.Width);
        AssertThat(chart.Height).IsEqual(ChartDefaults.Height);

        AssertThat(chart.AutoPadding).IsTrue();
        AssertThat(chart.Title is null).IsTrue();
        AssertThat(chart.WindowSize).IsEqual(0);
        AssertThat(chart.CurrentPlotArea is null).IsTrue();
        AssertThat(chart.CurrentFocusedSeries is null).IsTrue();
        AssertThat(chart.CurrentSelectedRowIndex).IsEqual(-1);
        AssertThat(chart.CurrentHoveredRowIndex).IsEqual(-1);

        // All renderer slots are wired to their defaults.
        AssertThat(chart.BackgroundRenderer is not null).IsTrue();
        AssertThat(chart.TitleRenderer is not null).IsTrue();
        AssertThat(chart.GridRenderer is not null).IsTrue();
        AssertThat(chart.AxisRenderer is not null).IsTrue();
        AssertThat(chart.AxisLabelRenderer is not null).IsTrue();
        AssertThat(chart.LegendRenderer is not null).IsTrue();
        AssertThat(chart.CrosshairRenderer is not null).IsTrue();
    }

    // ── Theme wiring into the renderers ─────────────────────────────────────

    [TestCase]
    public void ThemeFontSizeAndFamilyReachTheAxisLabels()
    {
        var canvas = new FakeCanvas2D();
        var theme = ChartTheme.Dark();
        theme.LabelFontSize = 22f;
        theme.FontFamily = "Consolas";

        var chart = new Chart(canvas) { Width = 400f, Height = 300f };
        chart.Data(new List<DataRow> { D(("cat", "A"), ("value", 10.0)) });
        chart.Mark(new IntervalMark());
        chart.Encode(Channel.X, "cat");
        chart.Encode(Channel.Y, "value");
        chart.Theme(theme);
        chart.Render();

        // The label draws carry the themed font: find the category label and check its font.
        AssertThat(canvas.TextDraws.Any(d => d.Text == "A")).IsTrue();
        AssertThat(canvas.FontOf("A").Size).IsEqual(22f);
        AssertThat(canvas.FontOf("A").Family).IsEqual("Consolas");
    }

    [TestCase]
    public void ThemeLabelColorIsUsedForTickLabels()
    {
        var canvas = new FakeCanvas2D();
        var theme = ChartTheme.Dark();
        theme.LabelColor = new Color(1f, 0f, 0f);
        theme.AxisColor = new Color(0f, 1f, 0f);

        var chart = new Chart(canvas) { Width = 400f, Height = 300f };
        chart.Data(new List<DataRow> { D(("cat", "A"), ("value", 10.0)) });
        chart.Mark(new IntervalMark());
        chart.Encode(Channel.X, "cat");
        chart.Encode(Channel.Y, "value");
        chart.Theme(theme);
        chart.Render();

        // The tick label color comes from LabelColor, not from AxisColor.
        var labelColor = canvas.TextColorsOf("A").FirstOrDefault();
        AssertThat(labelColor is { R: > 0.9f, G: < 0.1f }).IsTrue();
    }

    [TestCase]
    public void TooltipFallsBackToThemeMetrics()
    {
        var canvas = new FakeCanvas2D();
        var theme = ChartTheme.Dark();
        theme.TooltipPadding = 30f;   // much wider than the built-in default

        var renderer = new TooltipRenderer { Theme = theme };
        renderer.Options.RichContentBuilder = _ => new[] { TooltipLine.Plain("value") };
        var hit = new HitResult
        {
            Hit = true, RowIndex = 0, Row = D(("value", 1.0)),
            ScreenX = 50f, ScreenY = 50f, Label = "value",
        };
        renderer.Update(1f, hit);

        canvas.Rects.Clear();
        renderer.Draw(canvas, 800, 600);

        // The background box includes the themed 30px padding on both sides.
        AssertThat(canvas.Rects.Count > 0).IsTrue();
        AssertThat(BoxPadding(canvas) >= 60f).IsTrue();
    }

    /// <summary>Approximate padding: box height minus the text line height.</summary>
    private static float BoxPadding(FakeCanvas2D canvas)
    {
        float boxH = canvas.Rects.Max(r => r.H);
        float lineH = canvas.MeasureText("value", FontSettings.Default).Height;
        return boxH - lineH;
    }
}
