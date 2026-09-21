namespace GodotNodeExtension.Tests.GodotChart;

using System;
using System.Collections.Generic;
using System.Linq;
using GdUnit4;
using GodotNodeExtension.Component.GodotChart;
using GodotNodeExtension.Component.GodotChart.Canvas;
using GodotNodeExtension.Component.GodotChart.Marks;
using Support;
using static GdUnit4.Assertions;

/// <summary>
/// Tests for the text placement inside a <see cref="Chart"/>: that category labels are centred
/// under their tick, that long category names are truncated to their slot, that wide tick labels
/// widen the plot and are then drawn whole (and that auto-padding can be disabled), and that the
/// shared centred-text helper lifts the baseline so the text is visually centred.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class TextLayoutTest
{
    private static DataRow D(params (string Field, object Value)[] fields)
    {
        var row = new DataRow(fields.Length);
        foreach (var (field, value) in fields)
            row.Set(field, value);
        return row;
    }

    private static Chart BarChart(FakeCanvas2D canvas, List<DataRow> data, float width = 400f)
    {
        var chart = new Chart(canvas) { Width = width, Height = 300f };
        chart.Data(data);
        chart.Mark(new IntervalMark());
        chart.Encode(Channel.X, "cat");
        chart.Encode(Channel.Y, "value");
        return chart;
    }

    // ── X labels are centred under their tick ───────────────────────────────

    [TestCase]
    public void CategoryLabelsAreCentredUnderTheirTick()
    {
        var canvas = new FakeCanvas2D();
        var chart = BarChart(canvas,
        [
            D(("cat", "A"), ("value", 10.0)),
            D(("cat", "B"), ("value", 20.0)),
        ]);

        chart.Render();

        var labelA = canvas.TextDraws.Where(d => d.Text == "A").ToList();
        AssertThat(labelA.Count).IsEqual(1);
        AssertThat(labelA[0].Align).IsEqual(TextAlign.Center);
        // ... and the anchor is the CENTRE of category A's slot, not the plot edge: two categories put
        // the first band centre at a quarter of the plot width.
        var plot = chart.CurrentPlotArea!.Value;
        AssertThat(Math.Abs(labelA[0].X - (plot.X + plot.Width * 0.25f)) <= 1f).IsTrue();
    }

    // ── Long category names are truncated to their slot ─────────────────────

    [TestCase]
    public void LongCategoryLabelsAreTruncated()
    {
        const string longName = "a-very-long-category-name-that-cannot-fit";
        var canvas = new FakeCanvas2D();
        var chart = BarChart(canvas,
        [
            D(("cat", longName), ("value", 10.0)),
            D(("cat", "B"), ("value", 20.0)),
            D(("cat", "C"), ("value", 30.0)),
        ], width: 300f);

        chart.Render();

        AssertThat(canvas.Texts.Contains(longName)).IsFalse();
        var truncated = canvas.Texts.FirstOrDefault(t => t.StartsWith("a-very", StringComparison.Ordinal));
        AssertThat(truncated is not null).IsTrue();
        AssertThat(truncated!.EndsWith('\u2026')).IsTrue();
    }

    // ── The plot widens when the Y labels need more room ────────────────────

    [TestCase]
    public void WideYTickLabelsWidenThePlot()
    {
        var canvas = new FakeCanvas2D();
        var chart = BarChart(canvas, [D(("cat", "A"), ("value", 1.0))]);
        // Small decimal values produce wide tick labels ("0.0002"), so the default 50px padding
        // is not enough and the plot has to start further right.
        chart.Scale(Channel.Y, new LinearScale(0, 0.0008));

        chart.Render();

        AssertThat(chart.CurrentPlotArea!.Value.X > 50f).IsTrue();
    }

    [TestCase]
    public void AutoPaddingCanBeDisabled()
    {
        var canvas = new FakeCanvas2D();
        var chart = BarChart(canvas, [D(("cat", "A"), ("value", 1.0))]);
        chart.Scale(Channel.Y, new LinearScale(0, 0.0008));
        chart.AutoPadding = false;

        chart.Render();

        AssertThat(chart.CurrentPlotArea!.Value.X).IsEqual(50f);
    }

    /// <summary>
    /// The width the plot reserves for the Y labels and the width the labels are drawn into are two
    /// sides of one formula: the reservation measures the widest label, the renderer fits each label
    /// into what the reservation leaves. They used to differ by a pixel, so the widest label of every
    /// chart was trimmed to an ellipsis even though nothing had to be cut.
    /// </summary>
    [TestCase]
    public void TheWidestYAxisLabelIsDrawnWhole()
    {
        const string widest = "a-very-long-category-label";
        var canvas = new FakeCanvas2D();
        // A horizontal bar chart labels the Y axis with its category names.
        var chart = new Chart(canvas) { Width = 400f, Height = 300f };
        chart.Data(new List<DataRow>
        {
            D(("cat", widest), ("value", 10.0)),
            D(("cat", "B"), ("value", 20.0)),
        });
        chart.Mark(new IntervalMark { Orientation = BarOrientation.Horizontal });
        chart.Encode(Channel.X, "value");
        chart.Encode(Channel.Y, "cat");

        chart.Render();

        var labels = canvas.YAxisLabels.ToList();
        AssertThat(labels.Contains(widest)).IsTrue();
        AssertThat(labels.All(text => !text.Contains('\u2026'))).IsTrue();
        // The plot really did widen for the wide label (it starts past the plain padding).
        AssertThat(chart.CurrentPlotArea!.Value.X > ChartDefaults.PaddingLeft).IsTrue();
    }

    // ── The shared centred-text helper lifts the baseline ───────────────────

    [TestCase]
    public void CentredTextIsCentredHorizontallyAndVertically()
    {
        var canvas = new FakeCanvas2D();
        var ctx = TestContexts.Mark(canvas, [], new EncodeSet(), new ScaleSet());
        using var paint = canvas.CreatePaint();
        var font = FontSettings.Default;

        new ProbeMark().DrawCentered(ctx, paint, "value", 100f, 200f, font);

        var draw = canvas.TextDraws.Single();
        AssertThat(draw.Align).IsEqual(TextAlign.Center);
        float lineHeight = canvas.MeasureText("value", font).Height;
        // The baseline moves down by a third of the line height, which visually centres the text.
        AssertThat(MathF.Abs(draw.Y - (200f + lineHeight * 0.35f)) < 0.01f).IsTrue();
    }
}
