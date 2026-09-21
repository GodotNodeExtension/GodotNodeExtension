namespace GodotNodeExtension.Tests.GodotChart;

using System;
using System.Collections.Generic;
using GdUnit4;
using Godot;
using GodotNodeExtension.Component.GodotChart;
using static GdUnit4.Assertions;

/// <summary>
/// The streaming path on the engine's real backend: rows arriving one at a time (<see cref="ChartView.AddRow"/>,
/// the oscilloscope pattern) have to end up on exactly the pixels a single <c>SetData</c> of the same rows
/// produces, and a surface that has nothing new to show must not keep repainting.
/// <para>
/// The unit cases already check that <c>AddRow</c> keeps the chart instance and trims the window; what they
/// cannot check is whether the accumulated frames still draw the same picture.
/// </para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class ChartViewStreamingIntegrationTest
{
    private static readonly Vector2 ViewSize = new(360f, 220f);

    private static DataRow Row(double t, double value, string series = "S1")
        => new DataRow(3).Set("t", t).Set("value", value).Set("series", series);

    /// <summary>A view encoding the <c>t</c> / <c>value</c> columns of <see cref="Row"/>.</summary>
    private static ChartView AddLineView(string title)
    {
        var view = new ChartView
        {
            Kind = ChartKind.Line,
            Title = title,
            XField = "t",
            YField = "value",
            ColorField = "series",
            Size = ViewSize,
        };
        ((SceneTree)Engine.GetMainLoop()).Root.AddChild(view);
        return view;
    }

    /// <summary>
    /// Sixty rows appended one frame at a time and one single <c>SetData</c> of the same sixty rows have to
    /// produce the same picture: streaming may not accumulate a drift (a stale scale, a cached layout that
    /// never rebuilds, a window that trims one row late).
    /// </summary>
    [TestCase]
    public void StreamedRowsRenderLikeAOneShotDataSet()
    {
        const string name = nameof(StreamedRowsRenderLikeAOneShotDataSet);
        if (ChartRenderHarness.NoRenderingDevice(name)) return;

        const int count = 60;
        var rows = new List<DataRow>(count);
        for (int i = 0; i < count; i++)
            rows.Add(Row(i, 40 + 30 * Math.Sin(i / 6.0)));

        ulong oneShot;
        var single = AddLineView("streamed rows");
        try
        {
            single.SetData(rows);
            ChartRenderHarness.Pump(single, 2);
            var image = ChartRenderHarness.Pixels(single);
            AssertThat(image is not null).IsTrue();
            oneShot = ChartRenderHarness.PixelFingerprint(image!);
        }
        finally
        {
            ChartRenderHarness.Release(single);
        }

        // The same title on purpose: the title is part of the picture, so it must not be the difference
        // between the two paths this case compares.
        var streamed = AddLineView("streamed rows");
        try
        {
            foreach (var row in rows)
            {
                streamed.AddRow(row);
                ChartRenderHarness.Pump(streamed, 1);
            }
            ChartRenderHarness.Pump(streamed, 2);

            AssertThat(streamed.DataRows.Count).IsEqual(count);

            var image = ChartRenderHarness.Pixels(streamed);
            AssertThat(image is not null).IsTrue();
            AssertThat(ChartRenderHarness.PixelFingerprint(image!)).IsEqual(oneShot);
        }
        finally
        {
            ChartRenderHarness.Release(streamed);
        }
    }
    /// <summary>
    /// A streaming chart redraws while the data changes and stops once it does not: three consecutive frames
    /// of an active feed differ, and three frames after the feed stopped are the same picture.
    /// </summary>
    [TestCase]
    public void TheSurfaceRedrawsWhileStreamingAndSettlesAfterwards()
    {
        const string name = nameof(TheSurfaceRedrawsWhileStreamingAndSettlesAfterwards);
        if (ChartRenderHarness.NoRenderingDevice(name)) return;

        var view = AddLineView("feed");
        try
        {
            view.WindowSize = 30;
            for (int i = 0; i < 30; i++) view.AddRow(Row(i, 50 + i));
            ChartRenderHarness.Pump(view, 2);

            var streaming = new List<ulong>();
            for (int i = 0; i < 3; i++)
            {
                view.AddRow(Row(30 + i, 60 + i));
                ChartRenderHarness.Pump(view, 1);
                var frame = ChartRenderHarness.Pixels(view);
                AssertThat(frame is not null).IsTrue();
                streaming.Add(ChartRenderHarness.PixelFingerprint(frame!));
            }

            AssertThat(streaming[0] != streaming[1]).IsTrue();
            AssertThat(streaming[1] != streaming[2]).IsTrue();

            // The feed stopped: nothing changed, so the surface must present the same pixels frame after frame.
            ChartRenderHarness.Pump(view, 1);
            var settled = ChartRenderHarness.Pixels(view);
            AssertThat(settled is not null).IsTrue();
            ulong settledPrint = ChartRenderHarness.PixelFingerprint(settled!);

            ChartRenderHarness.Pump(view, 3);
            var later = ChartRenderHarness.Pixels(view);
            AssertThat(later is not null).IsTrue();
            AssertThat(ChartRenderHarness.PixelFingerprint(later!)).IsEqual(settledPrint);
        }
        finally
        {
            ChartRenderHarness.Release(view);
        }
    }

    /// <summary>
    /// A windowed feed stays bounded and keeps the newest rows: after more rows than the window holds, the
    /// surface shows the window (not everything) and the oldest rows are gone from the model.
    /// </summary>
    [TestCase]
    public void AWindowedFeedKeepsTheNewestRowsOnScreen()
    {
        const string name = nameof(AWindowedFeedKeepsTheNewestRowsOnScreen);
        if (ChartRenderHarness.NoRenderingDevice(name)) return;

        var view = AddLineView("window");
        try
        {
            view.WindowSize = 20;
            for (int i = 0; i < 60; i++)
            {
                view.AddRow(Row(i, 50 + (i % 10)));
                if (i % 10 == 0) ChartRenderHarness.Pump(view, 1);
            }
            ChartRenderHarness.Pump(view, 2);

            AssertThat(view.DataRows.Count).IsEqual(20);
            AssertThat(view.DataRows[0].Get<double>("t")).IsEqual(40.0);
            AssertThat(view.DataRows[^1].Get<double>("t")).IsEqual(59.0);

            // The surface follows the window: it is drawn (not blank) and stable once the feed stops.
            AssertThat(ChartRenderHarness.ContentRatio(view, out int content) > 0f).IsTrue();
            AssertThat(content).IsGreater(0);
            // 1 means "it was already settled when asked" - the point is that it settles at all, so 0
            // (a surface that kept changing) is the failure.
            AssertThat(ChartRenderHarness.PumpUntilStable(view)).IsGreater(0);
        }
        finally
        {
            ChartRenderHarness.Release(view);
        }
    }
}
