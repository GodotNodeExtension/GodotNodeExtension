namespace GodotNodeExtension.Tests.GodotChart;

using System;
using System.Collections.Generic;
using GdUnit4;
using GodotNodeExtension.Component.GodotChart;
using Support;
using static GdUnit4.Assertions;

/// <summary>
/// Tests for the data layer of <see cref="Chart"/> (Chart.Data.cs): replacing and appending rows,
/// the stream window, the transform pipeline and its cache, series visibility (hide/show/toggle)
/// and the post-transform render-data snapshot the marks and renderers consume.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class ChartDataTest
{
    private static DataRow D(params (string Field, object Value)[] fields)
    {
        var row = new DataRow(fields.Length);
        foreach (var (field, value) in fields)
            row.Set(field, value);
        return row;
    }

    /// <summary>Rows carrying the numeric <c>x</c> / <c>y</c> columns expected by the chart tests.</summary>
    private static List<DataRow> NumericRows(int count)
    {
        var rows = new List<DataRow>(count);
        for (int i = 0; i < count; i++)
            rows.Add(TestContexts.Row(("x", (double)i), ("y", (double)i + 1)));
        return rows;
    }

    /// <summary>Render data row 0 of <paramref name="chart"/> as the numeric <c>value</c> field.</summary>
    private static double FirstValue(Chart chart)
        => chart.GetRenderDataSnapshot()[0].Get<double>("value");

    /// <summary>Transform that adds a constant to the <c>value</c> field and counts its invocations.</summary>
    private sealed class CountingAddTransform : IDataTransform
    {
        private readonly double _amount;

        public CountingAddTransform(double amount) => _amount = amount;

        public int Calls { get; private set; }

        /// <inheritdoc />
        public List<DataRow> Apply(List<DataRow> data)
        {
            Calls++;
            var result = new List<DataRow>(data.Count);
            foreach (var row in data)
                result.Add(new DataRow().Set("value", row.Get<double>("value") + _amount));
            return result;
        }
    }

    private static void Approx(double actual, double expected, double tolerance = 1e-6)
        => AssertThat(Math.Abs(actual - expected) <= tolerance).IsTrue();

    [TestCase]
    public void DataAppliesTheWindowSizeLikeAppendData()
    {
        var canvas = new FakeCanvas2D();
        var probe = new CapturingMark();
        var chart = new Chart(canvas);
        chart.WindowSize = 2;
        chart.Mark(probe);
        chart.Encode(Channel.X, "x");
        chart.Encode(Channel.Y, "y");

        chart.Data(NumericRows(5));
        chart.Render();
        AssertThat(probe.LastDataCount).IsEqual(2);   // trimmed to the window

        chart.Data(NumericRows(4));
        chart.Render();
        AssertThat(probe.LastDataCount).IsEqual(2);
    }

    [TestCase]
    public void AppendDataTrimsToTheWindowSize()
    {
        var canvas = new FakeCanvas2D();
        var probe = new CapturingMark();
        var chart = new Chart(canvas);
        chart.WindowSize = 3;
        chart.Mark(probe);
        chart.Encode(Channel.X, "x");
        chart.Encode(Channel.Y, "y");

        for (int i = 0; i < 5; i++)
            chart.AppendData(TestContexts.Row(("x", (double)i), ("y", (double)i)));

        AssertThat(chart.GetRenderDataSnapshot().Count).IsEqual(3);
        chart.Render();
        AssertThat(probe.LastDataCount).IsEqual(3);
        Approx(chart.GetRenderDataSnapshot()[2].Get<double>("x"), 4);   // the newest row survives

        chart.AppendData(new List<DataRow>
        {
            TestContexts.Row(("x", 9.0), ("y", 9.0)),
            TestContexts.Row(("x", 10.0), ("y", 10.0)),
        });
        AssertThat(chart.GetRenderDataSnapshot().Count).IsEqual(3);
        Approx(chart.GetRenderDataSnapshot()[2].Get<double>("x"), 10);
    }

    [TestCase]
    public void TransformRejectsNull()
    {
        var chart = new Chart(new FakeCanvas2D());

        AssertThat(CaptureException(() => chart.Transform(null!)) is ArgumentNullException).IsTrue();
    }

    private static Exception? CaptureException(Action action)
    {
        try { action(); return null; }
        catch (Exception ex) { return ex; }
    }

    [TestCase]
    public void TransformsRunInOrderAndOnlyOnceWhileTheDataIsUnchanged()
    {
        var add10 = new CountingAddTransform(10);
        var add100 = new CountingAddTransform(100);
        var chart = new Chart(new FakeCanvas2D());
        chart.Data(new List<DataRow> { TestContexts.Row(("value", 1.0)) });
        chart.Mark(new CapturingMark());
        chart.Encode(Channel.X, "value");
        chart.Encode(Channel.Y, "value");
        chart.Transform(add10).Transform(add100);

        chart.Render();
        AssertThat(add10.Calls).IsEqual(1);
        AssertThat(add100.Calls).IsEqual(1);
        Approx(FirstValue(chart), 111);   // 1 + 10, then + 100: the transforms run in order

        chart.Render();
        AssertThat(add10.Calls).IsEqual(1);     // cached: the pipeline does not run again
        AssertThat(add100.Calls).IsEqual(1);

        chart.Data(new List<DataRow> { TestContexts.Row(("value", 2.0)) });
        chart.Render();
        AssertThat(add10.Calls).IsEqual(2);
        AssertThat(add100.Calls).IsEqual(2);
        Approx(FirstValue(chart), 112);
    }

    [TestCase]
    public void GetRenderDataSnapshotIsTransformedData()
    {
        var chart = new Chart(new FakeCanvas2D());
        chart.Data(new List<DataRow>
        {
            D(("value", 1.0)), D(("value", 2.0)), D(("value", 3.0)), D(("value", 4.0)),
        });
        chart.Transform(new BinTransform { Field = "value", BinCount = 2 });

        var snapshot = chart.GetRenderDataSnapshot();

        AssertThat(snapshot.Count).IsEqual(2);
        AssertThat(snapshot[0].Has("Count")).IsTrue();
        AssertThat(snapshot[0].Has("value")).IsFalse();
    }

    /// <summary>
    /// Handing the chart's own render data back to it keeps the rows. <see cref="Chart.GetRenderDataSnapshot"/>
    /// used to be a read-only <i>view</i> over the chart's list and <c>Data()</c> cleared that list before
    /// reading its argument, so the chart ended up with nothing - the copy has to be built first.
    /// </summary>
    [TestCase]
    public void ReplacingDataWithTheChartsOwnRenderDataKeepsTheRows()
    {
        var chart = new Chart(new FakeCanvas2D());
        chart.Data(NumericRows(3));

        chart.Data(chart.GetRenderDataSnapshot());

        AssertThat(chart.GetRenderDataSnapshot().Count).IsEqual(3);
    }

    /// <summary>
    /// The same for a caller-supplied sequence that is evaluated lazily: <c>Data()</c> materialises it
    /// before clearing, so a query over the rows being replaced still yields them.
    /// </summary>
    [TestCase]
    public void ReplacingDataWithALazyViewOfTheOldRowsKeepsTheRows()
    {
        var chart = new Chart(new FakeCanvas2D());
        chart.Data(NumericRows(4));

        var lazy = System.Linq.Enumerable.Select(chart.GetRenderDataSnapshot(), row => row);
        chart.Data(lazy);

        AssertThat(chart.GetRenderDataSnapshot().Count).IsEqual(4);
    }

    [TestCase]
    public void ReplacingDataRejectsNull()
    {
        var chart = new Chart(new FakeCanvas2D());

        AssertThat(CaptureException(() => chart.Data(null!)) is ArgumentNullException).IsTrue();
    }

    [TestCase]
    public void WindowSizeTrimsOldRowsOnAppend()
    {
        var chart = new Chart(new FakeCanvas2D()) { WindowSize = 2 };
        chart.AppendData(D(("cat", "A"), ("value", 1.0)));
        chart.AppendData(D(("cat", "B"), ("value", 2.0)));
        chart.AppendData(D(("cat", "C"), ("value", 3.0)));

        var snapshot = chart.GetRenderDataSnapshot();

        AssertThat(snapshot.Count).IsEqual(2);
        AssertThat(snapshot[0].Get<string>("cat")).IsEqual("B");
        AssertThat(snapshot[1].Get<string>("cat")).IsEqual("C");
    }

    [TestCase]
    public void SeriesVisibilityFluentApiAndQueries()
    {
        var chart = new Chart(new FakeCanvas2D());

        AssertThat(chart.IsSeriesHidden("A")).IsFalse();
        AssertThat(ReferenceEquals(chart.HideSeries("A"), chart)).IsTrue();
        AssertThat(chart.IsSeriesHidden("A")).IsTrue();
        AssertThat(chart.IsSeriesHidden("B")).IsFalse();

        chart.ShowSeries("A");
        AssertThat(chart.IsSeriesHidden("A")).IsFalse();

        chart.ToggleSeriesVisibility("A");
        AssertThat(chart.IsSeriesHidden("A")).IsTrue();
        chart.ToggleSeriesVisibility("A");
        AssertThat(chart.IsSeriesHidden("A")).IsFalse();

        chart.HideSeries("A").HideSeries("B");
        AssertThat(chart.IsSeriesHidden("A")).IsTrue();
        AssertThat(chart.IsSeriesHidden("B")).IsTrue();

        AssertThat(ReferenceEquals(chart.ShowAllSeries(), chart)).IsTrue();
        AssertThat(chart.IsSeriesHidden("A")).IsFalse();
        AssertThat(chart.IsSeriesHidden("B")).IsFalse();
    }

    [TestCase]
    public void HiddenSeriesAreForwardedToTheMarkContext()
    {
        var canvas = new FakeCanvas2D();
        var probe = new CapturingMark();
        var chart = new Chart(canvas);
        chart.Data(NumericRows(2));
        chart.Mark(probe);
        chart.Encode(Channel.X, "x");
        chart.Encode(Channel.Y, "y");

        chart.Render();
        AssertThat(probe.LastHiddenSeries is null).IsTrue();

        chart.HideSeries("A");
        chart.Render();
        AssertThat(probe.LastHiddenSeries is not null).IsTrue();
        AssertThat(probe.LastHiddenSeries!.Contains("A")).IsTrue();

        chart.ShowAllSeries();
        chart.Render();
        AssertThat(probe.LastHiddenSeries is not null).IsTrue();   // the set stays allocated but empty
        AssertThat(probe.LastHiddenSeries!.Count).IsEqual(0);
    }
}
