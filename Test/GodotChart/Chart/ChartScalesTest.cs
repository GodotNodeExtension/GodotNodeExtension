namespace GodotNodeExtension.Tests.GodotChart;

using System;
using System.Collections.Generic;
using System.Linq;
using GdUnit4;
using GodotNodeExtension.Component.GodotChart;
using GodotNodeExtension.Component.GodotChart.Marks;
using Support;
using static GdUnit4.Assertions;

/// <summary>
/// Tests for the scale layer of <see cref="Chart"/> (Chart.Scales.cs): which encoded column
/// becomes numeric, categorical or a colour scale, how mark-local encodes and per-mark data join
/// the fit, when the secondary Y axis gets a scale, and how manually configured scales take
/// precedence over inference across data and encode changes.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class ChartScalesTest
{
    private static List<DataRow> TwoPoints() =>

    [
        TestContexts.Row(("x", 1.0), ("y", 2.0)),
        TestContexts.Row(("x", 2.0), ("y", 3.0)),
    ];

    /// <summary>Approximate comparison for computed floating point values.</summary>
    private static void Approx(double actual, double expected, double tolerance = 1e-6)
        => AssertThat(Math.Abs(actual - expected) <= tolerance).IsTrue();

    // ── Scale inference through Render ──────────────────────────────────────

    [TestCase]
    public void RenderWithEmptyDataDoesNotThrow()
    {
        var canvas = new FakeCanvas2D();
        var probe = new CapturingMark();
        var chart = new Chart(canvas);
        chart.Data(new List<DataRow>());
        chart.Mark(probe);
        chart.Encode(Channel.X, "x");
        chart.Encode(Channel.Y, "y");

        chart.Render(); // an empty data set must not abort the frame

        AssertThat(probe.LastDataCount).IsEqual(0);
        AssertThat(probe.LastYScaleType is null).IsTrue();
    }

    [TestCase]
    public void RenderWithMisspelledFieldNameDoesNotThrow()
    {
        var canvas = new FakeCanvas2D();
        var probe = new CapturingMark();
        var chart = new Chart(canvas);
        chart.Data(TwoPoints());
        chart.Mark(probe);
        chart.Encode(Channel.X, "typo"); // no row has this field
        chart.Encode(Channel.Y, "y");

        chart.Render();

        AssertThat(probe.LastXScaleType is null).IsTrue(); // nothing to fit
        AssertThat(probe.LastYScaleType).IsEqual(nameof(LinearScale));
    }

    [TestCase]
    public void MarkLocalEncodeIsFittedWithoutChartLevelEncode()
    {
        var canvas = new FakeCanvas2D();
        var mark = new PointMark();
        mark.Encode(Channel.X, "x");
        mark.Encode(Channel.Y, "y");

        var chart = new Chart(canvas);
        chart.Data(TwoPoints());
        chart.Mark(mark);

        chart.Render();

        AssertThat(canvas.DrewAnything).IsTrue();
    }

    [TestCase]
    public void ConstantOpacityEncodeDoesNotThrow()
    {
        var canvas = new FakeCanvas2D();
        var chart = new Chart(canvas);
        chart.Data(TwoPoints());
        chart.Mark(new PointMark());
        chart.Encode(Channel.X, "x");
        chart.Encode(Channel.Y, "y");
        chart.Encode(Channel.Opacity, 0.5f); // constant encode, no scale is fitted for it

        chart.Render();

        AssertThat(canvas.DrewAnything).IsTrue();
    }

    [TestCase]
    public void NumericColorChannelStaysCategorical()
    {
        var canvas = new FakeCanvas2D();
        var chart = new Chart(canvas);
        chart.Data(new List<DataRow>
        {
            TestContexts.Row(("x", 1.0), ("y", 2.0), ("year", 2020)),
            TestContexts.Row(("x", 2.0), ("y", 3.0), ("year", 2021)),
        });
        chart.Mark(new PointMark());
        chart.Encode(Channel.X, "x");
        chart.Encode(Channel.Y, "y");
        chart.Encode(Channel.Color, "year");

        chart.Render();

        // A numeric colour column stays categorical: a LinearScale there would drop the legend and
        // paint every element with a single colour.
        AssertThat(chart.GetSeriesInfo().Count).IsEqual(2);
    }

    [TestCase]
    public void NarrowIntegerTypesAreRecognisedAsNumeric()
    {
        var canvas = new FakeCanvas2D();
        var probe = new CapturingMark();
        var chart = new Chart(canvas);
        chart.Data(new List<DataRow>
        {
            TestContexts.Row(("x", 1.0), ("y", (ushort)5)),
            TestContexts.Row(("x", 2.0), ("y", (byte)9)),
        });
        chart.Mark(probe);
        chart.Encode(Channel.X, "x");
        chart.Encode(Channel.Y, "y");

        chart.Render();

        AssertThat(probe.LastYScaleType).IsEqual(nameof(LinearScale));
    }

    [TestCase]
    public void NullFirstValueDoesNotForceAnOrdinalScale()
    {
        var canvas = new FakeCanvas2D();
        var probe = new CapturingMark();
        var chart = new Chart(canvas);
        chart.Data(new List<DataRow>
        {
            TestContexts.Row(("x", 1.0), ("y", null)), // missing value in the first row
            TestContexts.Row(("x", 2.0), ("y", 7.0)),
        });
        chart.Mark(probe);
        chart.Encode(Channel.X, "x");
        chart.Encode(Channel.Y, "y");

        chart.Render();

        AssertThat(probe.LastYScaleType).IsEqual(nameof(LinearScale));
    }

    [TestCase]
    public void AStringFirstValueProducesAnOrdinalScale()
    {
        var canvas = new FakeCanvas2D();
        var probe = new CapturingMark();
        var chart = new Chart(canvas);
        chart.Data(new List<DataRow>
        {
            TestContexts.Row(("x", 1.0), ("y", "A")),
            TestContexts.Row(("x", 2.0), ("y", "B")),
        });
        chart.Mark(probe);
        chart.Encode(Channel.X, "x");
        chart.Encode(Channel.Y, "y");

        chart.Render();

        AssertThat(probe.LastYScaleType).IsEqual(nameof(OrdinalScale));
        AssertThat(canvas.YAxisLabels).Contains("A");
    }

    [TestCase]
    public void AnEncodedFieldNobodyCarriesProducesNoScale()
    {
        var canvas = new FakeCanvas2D();
        var probe = new CapturingMark();
        var chart = new Chart(canvas);
        chart.Data(TwoPoints());
        chart.Mark(probe);
        chart.Encode(Channel.X, "typo");
        chart.Encode(Channel.Y, "y");

        chart.Render();

        AssertThat(probe.LastXScaleType is null).IsTrue();
        AssertThat(probe.LastYScaleType).IsEqual(nameof(LinearScale));
    }

    [TestCase]
    public void NonNumericValuesInANumericColumnAreDroppedFromTheScale()
    {
        // A numeric column may still contain a stray non-numeric value (a failed parse, a
        // placeholder string). Such entries are dropped before fitting, so the frame renders
        // normally and the inferred scale only covers the numeric subset.
        var canvas = new FakeCanvas2D();
        var probe = new CapturingMark();
        var chart = new Chart(canvas);
        chart.Data(new List<DataRow>
        {
            TestContexts.Row(("x", 1.0), ("y", 2.0)),
            TestContexts.Row(("x", 2.0), ("y", "abc")),
        });
        chart.Mark(probe);
        chart.Encode(Channel.X, "x");
        chart.Encode(Channel.Y, "y");

        chart.Render(); // must not throw

        AssertThat(probe.LastYScaleType).IsEqual(nameof(LinearScale));
        AssertThat(probe.RenderCount).IsEqual(1);
        AssertThat(canvas.DrewAnything).IsTrue();
    }

    // ── Manual scale precedence ─────────────────────────────────────────────

    [TestCase]
    public void ManualScaleIsNotOverriddenByInference()
    {
        var canvas = new FakeCanvas2D();
        var probe = new CapturingMark();
        var chart = new Chart(canvas);
        chart.Data(new List<DataRow>
        {
            TestContexts.Row(("x", 0.0), ("y", 1.0)),
            TestContexts.Row(("x", 1.0), ("y", 2.0)),
        });   // y values 1 and 2
        chart.Mark(probe);
        chart.Encode(Channel.X, "x");
        chart.Encode(Channel.Y, "y");
        chart.Scale(Channel.Y, new LinearScale(0, 1000));

        chart.Render();

        AssertThat(probe.LastYScaleType).IsEqual(nameof(LinearScale));
        // a fitted domain would only reach 2: the manual domain survives
        AssertThat(canvas.YAxisLabels).Contains("1000");
    }

    [TestCase]
    public void ManualScaleSurvivesADataChange()
    {
        var canvas = new FakeCanvas2D();
        var chart = new Chart(canvas) { Width = 400f, Height = 300f };
        chart.Data(new List<DataRow>
        {
            TestContexts.Row(("cat", "A"), ("value", 10.0)),
            TestContexts.Row(("cat", "B"), ("value", 20.0)),
        });
        chart.Mark(new IntervalMark());
        chart.Encode(Channel.X, "cat");
        chart.Encode(Channel.Y, "value");
        chart.Scale(Channel.Y, new LinearScale(0, 100));

        chart.Render();
        int firstCount = canvas.YAxisLabels.Count();
        AssertThat(canvas.YAxisLabels.Contains("100")).IsTrue();

        // New data far outside the manual domain: the manual scale must still win.
        chart.Data(new List<DataRow>
        {
            TestContexts.Row(("cat", "A"), ("value", 1000.0)),
            TestContexts.Row(("cat", "B"), ("value", 2000.0)),
        });
        chart.Render();

        var second = canvas.YAxisLabels.Skip(firstCount).ToList();
        AssertThat(second.Contains("100")).IsTrue();
        AssertThat(second.Contains("2000")).IsFalse();
    }

    [TestCase]
    public void ReEncodeDropsTheAutoFittedScaleAndRefits()
    {
        var canvas = new FakeCanvas2D();
        var chart = new Chart(canvas) { Width = 400f, Height = 300f };
        chart.Data(new List<DataRow>
        {
            TestContexts.Row(("cat", "A"), ("value", 10.0), ("other", 1000.0)),
            TestContexts.Row(("cat", "B"), ("value", 20.0), ("other", 2000.0)),
        });
        chart.Mark(new IntervalMark());
        chart.Encode(Channel.X, "cat");
        chart.Encode(Channel.Y, "value");

        chart.Render();
        int firstCount = canvas.YAxisLabels.Count();
        AssertThat(canvas.YAxisLabels.Contains("20")).IsTrue();
        AssertThat(canvas.YAxisLabels.Contains("2000")).IsFalse();

        // Re-encoding the Y channel must drop the old auto-fitted scale and fit the new field.
        chart.Encode(Channel.Y, "other");
        chart.Render();

        var second = canvas.YAxisLabels.Skip(firstCount).ToList();
        AssertThat(second.Contains("2000")).IsTrue();
    }

    /// <summary>
    /// The constant overload of <see cref="Chart.Encode(Channel, object)"/> has to invalidate exactly like
    /// the field overload: it used to leave the auto-fitted scale in place, and <c>AutoFitScales</c> skips a
    /// channel that already has one - so a chart switched from a field to a constant kept the old field's
    /// domain (and, for the colour channel, its legend).
    /// </summary>
    [TestCase]
    public void AConstantEncodeAlsoDropsTheAutoFittedScale()
    {
        var canvas = new FakeCanvas2D();
        var chart = new Chart(canvas) { Width = 400f, Height = 300f };
        chart.Data(new List<DataRow>
        {
            TestContexts.Row(("cat", "A"), ("value", 10.0), ("series", "S1")),
            TestContexts.Row(("cat", "B"), ("value", 20.0), ("series", "S2")),
        });
        chart.Mark(new IntervalMark());
        chart.Encode(Channel.X, "cat");
        chart.Encode(Channel.Y, "value");
        chart.Encode(Channel.Color, "series");

        chart.Render();
        AssertThat(chart.GetSeriesInfo().Count).IsEqual(2);       // S1 / S2 from the field

        // One colour for the whole chart: the categorical colour scale is gone with it.
        chart.Encode(Channel.Color, new Godot.Color(1f, 0f, 0f));
        chart.Render();

        AssertThat(chart.GetSeriesInfo().Count).IsEqual(0);
    }

    /// <summary>
    /// The two <c>Encode</c> overloads agree: the "constant:" form goes through the field overload and the
    /// typed form through the object one, and both have to drop the auto-fitted scale of the channel they
    /// replace. (Only the field overload used to, which is why <c>Encode(Color, (object)…)</c> left the old
    /// legend behind.)
    /// </summary>
    [TestCase]
    public void BothConstantEncodeOverloadsInvalidateTheSameChannel()
    {
        var canvas = new FakeCanvas2D();
        var chart = new Chart(canvas) { Width = 400f, Height = 300f };
        chart.Data(new List<DataRow>
        {
            TestContexts.Row(("cat", "A"), ("value", 10.0), ("series", "S1")),
            TestContexts.Row(("cat", "B"), ("value", 20.0), ("series", "S2")),
        });
        chart.Mark(new IntervalMark());
        chart.Encode(Channel.X, "cat");
        chart.Encode(Channel.Y, "value");
        chart.Encode(Channel.Color, "series");

        chart.Render();
        AssertThat(chart.GetSeriesInfo().Count).IsEqual(2);

        chart.Encode(Channel.Color, "constant:#00ff00");   // the "constant:" path of the field overload
        chart.Render();
        AssertThat(chart.GetSeriesInfo().Count).IsEqual(0);

        // ...and re-binding to a field fits it again, so the two paths stay interchangeable.
        chart.Encode(Channel.Color, "series");
        chart.Render();
        AssertThat(chart.GetSeriesInfo().Count).IsEqual(2);
    }

    // ── Y2 auto-fit ─────────────────────────────────────────────────────────

    [TestCase]
    public void AMarkBoundToY2TriggersTheY2AutoFit()
    {
        var canvas = new FakeCanvas2D();
        var probe = new CapturingMark { YChannel = Channel.Y2 };
        var leftProbe = new CapturingMark();   // reads the plain (left) Y scale, the negative control
        var chart = new Chart(canvas) { Width = 400f, Height = 300f };
        chart.Data(new List<DataRow>
        {
            TestContexts.Row(("cat", "A"), ("value", 0.0), ("cost", 100.0)),
            TestContexts.Row(("cat", "B"), ("value", 5.0), ("cost", 200.0)),
            TestContexts.Row(("cat", "C"), ("value", 10.0), ("cost", 300.0)),
        });
        chart.Mark(probe);
        chart.Mark(leftProbe);
        chart.Encode(Channel.X, "cat");
        chart.Encode(Channel.Y, "value");   // 0…10, the domain the left axis has to keep
        chart.Encode(Channel.Y2, "cost");   // 100…300: a different field in a different domain

        chart.Render();

        // YChannel = Y2 makes LastYScale* report the Y2 scale of the rendered context. The two domains are far
        // apart, so the scale really was fitted to the Y2 field: with the old data (Y2 fed from the Y field)
        // "the Y2 scale" and "the Y scale" were indistinguishable and a chart that never fitted a second
        // domain passed the case.
        AssertThat(probe.LastYScaleType).IsEqual(nameof(LinearScale));
        // LinearScale.IncludeZero folds the non-negative 100…300 domain down to its zero baseline, so only the
        // top end is exact - the same baseline the other cases of this file account for (a fitted value axis
        // reports Min == 0, see ANumericCategoryAxisDoesNotForceZero).
        AssertThat(probe.LastYScaleMin <= 100.0).IsTrue();
        Approx(probe.LastYScaleMax, 300.0);

        // Negative control: the left Y scale was fitted to "value" (0…10) and never saw "cost".
        Approx(leftProbe.LastYScaleMax, 10.0);
        AssertThat(leftProbe.LastYScaleMax < probe.LastYScaleMax).IsTrue();

        // The auto-fit block proper (no Y2 encode at all) reads its field name from the mark's own Y encode
        // or, failing that, from the chart's Y encode - from the mark's own data when it carries it. That path
        // is what the old version of this case really exercised, and it stays covered with its own domain.
        var fallbackCanvas = new FakeCanvas2D();
        var fallbackProbe = new CapturingMark { YChannel = Channel.Y2 };
        var extentProbe = new CapturingMark();
        var fallbackChart = new Chart(fallbackCanvas) { Width = 400f, Height = 300f };
        fallbackChart.Data(new List<DataRow>
        {
            TestContexts.Row(("cat", "A"), ("value", 0.0)),
            TestContexts.Row(("cat", "B"), ("value", 10.0)),
        });
        fallbackProbe.Data = new List<DataRow>
        {
            TestContexts.Row(("cat", "A"), ("value", 100.0)),
            TestContexts.Row(("cat", "B"), ("value", 300.0)),
        };
        fallbackChart.Mark(fallbackProbe);
        fallbackChart.Mark(extentProbe);
        fallbackChart.Encode(Channel.X, "cat");
        fallbackChart.Encode(Channel.Y, "value");

        fallbackChart.Render();

        AssertThat(fallbackProbe.LastYScaleType).IsEqual(nameof(LinearScale));
        Approx(fallbackProbe.LastYScaleMax, 300.0);   // the mark's own 100…300 data
        Approx(extentProbe.LastYScaleMax, 10.0);      // the chart's 0…10 data, untouched
    }

    [TestCase]
    public void WithoutAy2MarkNoY2ScaleIsCreated()
    {
        var canvas = new FakeCanvas2D();
        var probe = new CapturingMark();
        var chart = new Chart(canvas);
        chart.Data(new List<DataRow>
        {
            TestContexts.Row(("x", 0.0), ("y", 1.0)),
            TestContexts.Row(("x", 1.0), ("y", 2.0)),
            TestContexts.Row(("x", 2.0), ("y", 3.0)),
        });
        chart.Mark(probe);
        chart.Encode(Channel.X, "x");
        chart.Encode(Channel.Y, "y");

        chart.Render();

        var plot = probe.LastPlot.GetValueOrDefault();
        // A fitted Y2 scale makes Render reserve Theme.Y2LabelReservedWidth on the right; with no Y2
        // mark the plot reaches all the way to Width - PaddingRight.
        Approx(plot.X + plot.Width, ChartDefaults.Width - ChartDefaults.PaddingRight);
    }

    // ── Numeric detection ───────────────────────────────────────────────────

    [TestCase]
    public void IsNumericAcceptsDecimalUintAndUlong()
    {
        AssertThat(FittedYScaleFor(1m, 2m)).IsEqual(nameof(LinearScale));
        AssertThat(FittedYScaleFor((uint)1, (uint)2)).IsEqual(nameof(LinearScale));
        AssertThat(FittedYScaleFor((ulong)1, (ulong)2)).IsEqual(nameof(LinearScale));
        AssertThat(FittedYScaleFor((byte)1, (byte)2)).IsEqual(nameof(LinearScale));
        // a bool column is not numeric → categorical
        AssertThat(FittedYScaleFor(true, false)).IsEqual(nameof(OrdinalScale));
    }

    /// <summary>Render a two row chart with the given Y values and report the fitted Y scale type.</summary>
    private static string? FittedYScaleFor(object first, object second)
    {
        var probe = new CapturingMark();
        var chart = new Chart(new FakeCanvas2D());
        chart.Data(new List<DataRow>
        {
            TestContexts.Row(("x", 1.0), ("y", first)),
            TestContexts.Row(("x", 2.0), ("y", second)),
        });
        chart.Mark(probe);
        chart.Encode(Channel.X, "x");
        chart.Encode(Channel.Y, "y");
        chart.Render();
        return probe.LastYScaleType;
    }

    /// <summary>Mark that reports the scales it was rendered with (the chart does not expose them).</summary>
    private sealed class ScaleProbe : Mark
    {
        /// <inheritdoc />
        public override MarkCoordinate Coordinate => MarkCoordinate.Cartesian;

        /// <summary>The X scale of the last render.</summary>
        public LinearScale? X { get; private set; }

        /// <summary>The Y scale of the last render.</summary>
        public LinearScale? Y { get; private set; }

        /// <inheritdoc />
        public override void Render(MarkContext ctx)
        {
            X = ctx.Scales.TryGet(Channel.X) as LinearScale;
            Y = ctx.Scales.TryGet(Channel.Y) as LinearScale;
        }
    }

    [TestCase]
    public void ANumericCategoryAxisDoesNotForceZero()
    {
        var canvas = new FakeCanvas2D();
        var probe = new ScaleProbe();
        var chart = new Chart(canvas) { Width = 400f, Height = 300f };
        chart.Data(new List<DataRow>
        {
            TestContexts.Row(("t", 60.0), ("value", 10.0)),
            TestContexts.Row(("t", 92.0), ("value", 40.0)),
            TestContexts.Row(("t", 125.0), ("value", 90.0)),
        });
        chart.Mark(probe);
        chart.Encode(Channel.X, "t");
        chart.Encode(Channel.Y, "value");

        chart.Render();

        // A numeric category axis describes where the data sits (a sample index, a timestamp, a
        // frequency): forcing zero would squeeze a rolling window into a corner of the plot.
        AssertThat(probe.X is not null).IsTrue();
        AssertThat(probe.X!.Min >= 60.0).IsTrue();
        AssertThat(probe.X.Max >= 125.0).IsTrue();

        // The value axis keeps the zero baseline.
        AssertThat(probe.Y is not null).IsTrue();
        AssertThat(probe.Y!.Min).IsEqual(0.0);
    }
}
