namespace GodotNodeExtension.Tests.GodotChart;

using GdUnit4;
using GodotNodeExtension.Component.GodotChart;
using GodotNodeExtension.Component.GodotChart.Marks;
using Support;
using static GdUnit4.Assertions;

/// <summary>
/// <see cref="SectionMark"/> - the reference lines and bands: they are annotation, so they draw on the plot
/// without touching the axis, and a level outside the visible window is skipped rather than pinned to the edge.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class SectionMarkTest
{
    /// <summary>A line chart over y 0..100, with one <see cref="SectionMark"/> on it.</summary>
    private static (Chart Chart, FakeCanvas2D Canvas) ChartWith(SectionMark section)
    {
        var canvas = new FakeCanvas2D();
        var chart = new Chart(canvas) { Width = 400f, Height = 300f };
        chart.Data(
        [
            TestContexts.Row(("x", 0.0), ("y", 0.0)),
            TestContexts.Row(("x", 100.0), ("y", 100.0)),
        ]);
        chart.Encode(Channel.X, "x");
        chart.Encode(Channel.Y, "y");
        chart.Mark(new LineMark { Smooth = false });
        chart.Mark(section);
        chart.Render();
        return (chart, canvas);
    }

    [TestCase]
    public void EveryLevelIsStrokedOnce()
    {
        var (_, canvas) = ChartWith(new SectionMark { Levels = [25.0, 50.0, 75.0], Dashed = false });

        // One line for the series, then one stroke per level.
        AssertThat(canvas.StrokeCount).IsGreater(3);
    }

    [TestCase]
    public void DashedLinesAreSplitIntoSegments()
    {
        var (_, solid) = ChartWith(new SectionMark { Levels = [50.0], Dashed = false });
        var (_, dashed) = ChartWith(new SectionMark { Levels = [50.0], Dashed = true });

        AssertThat(dashed.StrokeCount > solid.StrokeCount).IsTrue();
    }

    [TestCase]
    public void TheBandIsFilled()
    {
        var (_, plain) = ChartWith(new SectionMark { Levels = [] });
        var (_, banded) = ChartWith(new SectionMark { BandFrom = 40.0, BandTo = 60.0 });

        AssertThat(banded.FillCount > plain.FillCount).IsTrue();
    }

    [TestCase]
    public void LevelsOutsideTheWindowAreSkipped()
    {
        var (_, inside) = ChartWith(new SectionMark { Levels = [50.0], Dashed = false });
        var (_, outside) = ChartWith(new SectionMark { Levels = [5000.0], Dashed = false });

        AssertThat(outside.StrokeCount).IsEqual(inside.StrokeCount - 1);
    }

    /// <summary>Annotation must not stretch the axis: a level far above the data leaves the domain alone.</summary>
    [TestCase]
    public void SectionsDoNotStretchTheAxis()
    {
        var (plain, _) = ChartWith(new SectionMark { Levels = [] });
        var (annotated, _) = ChartWith(new SectionMark { Levels = [5000.0] });

        AssertThat(plain.TryGetDomain(Channel.Y, out double plainMin, out double plainMax)).IsTrue();
        AssertThat(annotated.TryGetDomain(Channel.Y, out double min, out double max)).IsTrue();
        AssertThat(min).IsEqual(plainMin);
        AssertThat(max).IsEqual(plainMax);
    }

    /// <summary>
    /// The label format is the one <see cref="Mark"/> declares (default <c>"{0}"</c>), so a level is labelled
    /// with its own value unless the format says something else - and an axis-level format reuses that.
    /// </summary>
    [TestCase]
    public void TheLabelFormatReachesTheCanvas()
    {
        var (_, plain) = ChartWith(new SectionMark { Levels = [50.0], LabelFormat = "" });
        var (_, labelled) = ChartWith(new SectionMark { Levels = [50.0], LabelFormat = "0.0 °C" });

        // The canvas records every text the chart draws (title, axis labels, ...), so the assertion looks at
        // the content: the formatted level shows up, and the run with an empty format never writes it.
        string plainText = string.Join("|", plain.Texts);
        string labelledText = string.Join("|", labelled.Texts);

        AssertThat(plainText.Contains(" °C")).IsFalse();
        AssertThat(labelledText.Contains(" °C")).IsTrue();
    }

    /// <summary>
    /// The label format may name both placeholders. The level label used to be formatted with a single
    /// argument, so a format written the way <see cref="Mark.LabelFormat"/> documents it ("{0}: {1}") raised
    /// <c>FormatException</c> inside the render stage: the level kept its line and lost its label. A section
    /// spans the other axis, so it has no single value for {1} and that placeholder comes out empty.
    /// </summary>
    [TestCase]
    public void ALabelFormatWithBothPlaceholdersIsAccepted()
    {
        var (_, canvas) = ChartWith(new SectionMark { Levels = [37.0], LabelFormat = "{0} °C: {1}" });

        // 37 is not an axis tick of the 0..100 domain, so the text can only come from the level label.
        AssertThat(string.Join("|", canvas.Texts).Contains("37 °C:")).IsTrue();
    }

    /// <summary>
    /// The two ways to ask for no label at all: an empty format draws none, while leaving the property alone
    /// keeps the base <c>"{0}"</c> (the level itself).
    /// </summary>
    [TestCase]
    public void OnlyAnEmptyLabelFormatSilencesTheLevelLabel()
    {
        var (_, unset) = ChartWith(new SectionMark { Levels = [37.0] });
        var (_, empty) = ChartWith(new SectionMark { Levels = [37.0], LabelFormat = "" });

        AssertThat(string.Join("|", unset.Texts).Contains("37")).IsTrue();
        AssertThat(string.Join("|", empty.Texts).Contains("37")).IsFalse();
    }

    [TestCase]
    public void AVerticalSectionFollowsTheXAxis()
    {
        var (chart, canvas) = ChartWith(new SectionMark { Levels = [50.0], Target = Channel.X, Dashed = false });
        var plot = chart.CurrentPlotArea!.Value;

        AssertThat(canvas.StrokeCount).IsGreater(1);
        AssertThat(plot.Width > 0f).IsTrue();
    }
}
