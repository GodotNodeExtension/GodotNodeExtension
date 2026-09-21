namespace GodotNodeExtension.Tests.GodotChart;

using System.Collections.Generic;
using GdUnit4;
using GodotNodeExtension.Component.GodotChart;
using static GdUnit4.Assertions;

/// <summary>
/// <see cref="AxisConfig.LabelFormat"/>: the format string an axis may put on its numeric tick labels, so a
/// reader sees "3.3 °C" or "1.250" instead of whatever the scale prints by itself.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class AxisLabelFormatTest
{
    /// <summary>A linear scale fitted to 0..10, the shape a numeric axis has.</summary>
    private static LinearScale Scale()
    {
        var scale = new LinearScale();
        scale.Fit([0.0, 10.0]);
        return scale;
    }

    [TestCase]
    public void TheFormatStringIsAppliedToTheTickValues()
    {
        var ticks = DefaultRenderers.ComputeTicks(Scale(), labelFormat: "0.00");

        AssertThat(ticks.Count).IsGreater(1);
        foreach (var (_, text) in ticks) AssertThat(text.Contains('.')).IsTrue();
    }

    [TestCase]
    public void UnitsInTheFormatReachTheLabel()
    {
        var ticks = DefaultRenderers.ComputeTicks(Scale(), labelFormat: "0.0 °C");

        AssertThat(ticks.Count).IsGreater(1);
        foreach (var (_, text) in ticks) AssertThat(text.EndsWith(" °C", System.StringComparison.Ordinal)).IsTrue();
    }

    [TestCase]
    public void NoFormatKeepsTheScalesOwnText()
    {
        var plain = DefaultRenderers.ComputeTicks(Scale());
        var formatted = DefaultRenderers.ComputeTicks(Scale(), labelFormat: "0.00");

        AssertThat(string.Join(",", plain.ConvertAll(t => t.Text)))
            .IsNotEqual(string.Join(",", formatted.ConvertAll(t => t.Text)));
    }

    /// <summary>A category axis labels categories, so a numeric format must leave it alone.</summary>
    [TestCase]
    public void ACategoryAxisKeepsItsLabels()
    {
        var categories = new OrdinalScale();
        categories.Fit(new List<object> { "Jan", "Feb", "Mar" });

        var ticks = DefaultRenderers.ComputeTicks(categories, labelFormat: "0.00");

        AssertThat(ticks.Count).IsGreater(0);
        foreach (var (_, text) in ticks)
        {
            // A category label is a word: "0.00" would have turned it into a number.
            AssertThat(double.TryParse(text, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out _)).IsFalse();
        }
    }
}
