namespace GodotNodeExtension.Tests.GodotChart;

using GdUnit4;
using GodotNodeExtension.Component.GodotChart;
using GodotNodeExtension.Component.GodotChart.Marks;
using Support;
using static GdUnit4.Assertions;

/// <summary>
/// X-axis tick label rotation: zero degrees takes the plain path (so a chart that does not ask for rotation
/// draws the same pixels as before), a rotated axis turns every label around its own tick, and the per-axis
/// value beats the theme's.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class AxisLabelRotationTest
{
    private static (Chart Chart, FakeCanvas2D Canvas) ChartWith(float themeRotation, float? axisRotation)
    {
        var canvas = new FakeCanvas2D();
        var chart = new Chart(canvas) { Width = 400f, Height = 300f };
        chart.Data(
        [
            TestContexts.Row(("x", 0.0), ("y", 1.0)),
            TestContexts.Row(("x", 1.0), ("y", 2.0)),
            TestContexts.Row(("x", 2.0), ("y", 1.5)),
        ]);
        chart.Encode(Channel.X, "x");
        chart.Encode(Channel.Y, "y");

        var theme = ChartTheme.Dark().Clone();      // never mutate the shared instance
        theme.XAxisLabelRotation = themeRotation;
        chart.Theme(theme);
        if (axisRotation is { } rotation) chart.XAxis(new AxisConfig { LabelRotation = rotation });

        chart.Mark(new LineMark { Smooth = false });
        chart.Render();
        return (chart, canvas);
    }

    [TestCase]
    public void TheThemeRotationTurnsEveryLabel()
    {
        var (_, plain) = ChartWith(0f, null);
        var (_, rotated) = ChartWith(45f, null);

        // One save/restore pair per rotated label, on top of whatever the chart saves for its own passes.
        AssertThat(rotated.SaveCount > plain.SaveCount).IsTrue();
        AssertThat(rotated.SaveRestoreBalanced).IsTrue();
    }

    [TestCase]
    public void TheAxisSettingBeatsTheTheme()
    {
        var (_, plain) = ChartWith(0f, null);
        var (_, axisOnly) = ChartWith(0f, 45f);

        AssertThat(axisOnly.SaveCount > plain.SaveCount).IsTrue();
        AssertThat(axisOnly.SaveRestoreBalanced).IsTrue();
    }

    /// <summary>The axis can switch rotation off again even when the theme asks for it.</summary>
    [TestCase]
    public void TheAxisCanTurnTheThemesRotationOff()
    {
        var (_, themed) = ChartWith(45f, null);
        var (_, off) = ChartWith(45f, 0f);

        AssertThat(themed.SaveCount > off.SaveCount).IsTrue();
    }
}
