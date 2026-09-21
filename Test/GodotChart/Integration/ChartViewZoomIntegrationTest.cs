namespace GodotNodeExtension.Tests.GodotChart;

using System;
using GdUnit4;
using Godot;
using GodotNodeExtension.Component.GodotChart;
using Support;
using static GdUnit4.Assertions;

/// <summary>
/// The pointer gestures of <see cref="ChartView"/> (the page side of the zoom feature): the wheel zooms
/// around the pointer, the pan button drags the window, a double click resets, and - the regression this
/// file exists for - a view with <see cref="ChartZoomMode.None"/> leaves the wheel alone so the surrounding
/// <c>ScrollContainer</c> keeps scrolling.
/// <para>
/// The cases run on the engine's real device (a view builds a Skia surface), so they skip themselves in a
/// headless run: <c>python Tools/run_tests.py --component GodotChart --integration --render</c>.
/// </para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class ChartViewZoomIntegrationTest
{
    private const string Suite = nameof(ChartViewZoomIntegrationTest);

    /// <summary>
    /// A line view with a known numeric domain (x = 10..50) in the tree, on the real backend. Numeric X on
    /// purpose: zoom is defined on a numeric window, while a category axis has none.
    /// </summary>
    private static ChartView AddView(ChartZoomMode mode)
    {
        var view = ChartRenderHarness.AddView(ChartKind.Line, ChartRenderHarness.ViewSize);
        view.ZoomMode = mode;
        view.XField = "x";
        view.YField = "value";
        view.SetData(
        [
            TestContexts.Row(("x", 10.0), ("value", 1.0)),
            TestContexts.Row(("x", 20.0), ("value", 2.0)),
            TestContexts.Row(("x", 30.0), ("value", 3.0)),
            TestContexts.Row(("x", 40.0), ("value", 4.0)),
            TestContexts.Row(("x", 50.0), ("value", 5.0)),
        ]);
        ChartRenderHarness.Pump(view, 2);
        return view;
    }

    /// <summary>A synthetic mouse button event at a position inside the view.</summary>
    private static InputEventMouseButton Wheel(MouseButton button, Vector2 position, bool doubleClick = false) => new()
    {
        ButtonIndex = button,
        Pressed = true,
        DoubleClick = doubleClick,
        Position = position,
    };

    private static InputEventMouseMotion Drag(Vector2 position, Vector2 relative) => new()
    {
        Position = position,
        Relative = relative,
        ButtonMask = MouseButtonMask.Middle,
    };

    /// <summary>The X window of the view's chart, asserting the view built one.</summary>
    private static (double Min, double Max) WindowX(ChartView view)
    {
        AssertThat(view.Chart is not null).IsTrue();
        AssertThat(view.Chart!.TryGetDomain(Channel.X, out double min, out double max)).IsTrue();
        return (min, max);
    }

    [TestCase]
    public void TheWheelZoomsTheWindowAroundThePointer()
    {
        if (ChartRenderHarness.NoRenderingDevice(Suite)) return;

        var view = AddView(ChartZoomMode.X);
        try
        {
            var (min, max) = WindowX(view);
            double width = max - min;

            // A notch to the left of the plot keeps the low end where it is and pulls the high end in.
            view._GuiInput(Wheel(MouseButton.WheelUp, new Vector2(2f, ChartRenderHarness.ViewSize.Y * 0.5f)));

            var (zoomMin, zoomMax) = WindowX(view);
            AssertThat(zoomMax - zoomMin < width).IsTrue();
            AssertThat(zoomMin < (min + max) * 0.5).IsTrue();      // the left half is the one that stayed

            // ... and a notch down widens it again.
            view._GuiInput(Wheel(MouseButton.WheelDown, new Vector2(2f, ChartRenderHarness.ViewSize.Y * 0.5f)));
            var (backMin, backMax) = WindowX(view);
            AssertThat(backMax - backMin > zoomMax - zoomMin).IsTrue();
        }
        finally
        {
            ChartRenderHarness.Release(view);
        }
    }

    [TestCase]
    public void ThePanButtonDragsTheWindow()
    {
        if (ChartRenderHarness.NoRenderingDevice(Suite)) return;

        var view = AddView(ChartZoomMode.X);
        try
        {
            var start = new Vector2(ChartRenderHarness.ViewSize.X * 0.5f, ChartRenderHarness.ViewSize.Y * 0.5f);

            // Two notches in: one leaves a window so wide that a 20 pixel drag is clamped back to the same
            // window, which would make the pan assertions below say nothing.
            view._GuiInput(Wheel(MouseButton.WheelUp, start));
            view._GuiInput(Wheel(MouseButton.WheelUp, start));
            var (min, max) = WindowX(view);

            // The default pan button is the left one (see ChartView.PanButton): the gesture a reader tries
            // first, and what the page relies on without configuring anything.
            view._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = start });
            view._GuiInput(Drag(start + new Vector2(20f, 0f), new Vector2(20f, 0f)));
            view._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = start });

            var (panMin, panMax) = WindowX(view);
            AssertThat(Math.Abs((panMax - panMin) - (max - min)) <= 0.001).IsTrue();   // the width is untouched
            AssertThat(panMin < min).IsTrue();                                    // dragging right moves the window left
        }
        finally
        {
            ChartRenderHarness.Release(view);
        }
    }

    [TestCase]
    public void ADoubleClickResetsTheZoom()
    {
        if (ChartRenderHarness.NoRenderingDevice(Suite)) return;

        var view = AddView(ChartZoomMode.Both);
        try
        {
            var (min, max) = WindowX(view);
            var start = new Vector2(ChartRenderHarness.ViewSize.X * 0.5f, ChartRenderHarness.ViewSize.Y * 0.5f);

            view._GuiInput(Wheel(MouseButton.WheelUp, start));
            var (zoomMin, zoomMax) = WindowX(view);
            AssertThat(zoomMax - zoomMin < max - min).IsTrue();

            view._GuiInput(Wheel(MouseButton.Left, start, doubleClick: true));

            var (backMin, backMax) = WindowX(view);
            AssertThat(Math.Abs((backMax - backMin) - (max - min)) <= 0.001).IsTrue();
            AssertThat(Math.Abs(backMin - min) <= 0.001).IsTrue();
        }
        finally
        {
            ChartRenderHarness.Release(view);
        }
    }

    /// <summary>
    /// The compatibility case: with zoom off (the default) a wheel notch must not change the view at all -
    /// which is what lets the event reach the host container and scroll a page of charts.
    /// </summary>
    [TestCase]
    public void AViewWithoutZoomIgnoresTheWheel()
    {
        if (ChartRenderHarness.NoRenderingDevice(Suite)) return;

        var view = AddView(ChartZoomMode.None);
        try
        {
            var (min, max) = WindowX(view);
            var start = new Vector2(ChartRenderHarness.ViewSize.X * 0.5f, ChartRenderHarness.ViewSize.Y * 0.5f);

            view._GuiInput(Wheel(MouseButton.WheelUp, start));
            view._GuiInput(Wheel(MouseButton.WheelDown, start));

            var (afterMin, afterMax) = WindowX(view);
            AssertThat(afterMin).IsEqualApprox(min, 1e-9);
            AssertThat(afterMax).IsEqualApprox(max, 1e-9);
        }
        finally
        {
            ChartRenderHarness.Release(view);
        }
    }

    /// <summary>
    /// A zoom narrows the window, so part of the data maps outside the plot rectangle. Those points must not be
    /// painted: the column just left of the plot belongs to the axis decoration and has to stay background.
    /// Without the clip the series run over the Y-axis labels, which is what "the line goes past the axis"
    /// looks like on screen.
    /// </summary>
    [TestCase]
    public void ZoomedSeriesStayInsideThePlot()
    {
        if (ChartRenderHarness.NoRenderingDevice(Suite)) return;

        var view = AddView(ChartZoomMode.X);
        try
        {
            var centre = new Vector2(ChartRenderHarness.ViewSize.X * 0.5f, ChartRenderHarness.ViewSize.Y * 0.5f);
            for (int notch = 0; notch < 6; notch++) view._GuiInput(Wheel(MouseButton.WheelUp, centre));
            ChartRenderHarness.Pump(view, 3);

            var image = ChartRenderHarness.Pixels(view);
            AssertThat(image is not null).IsTrue();
            var plot = view.Chart!.CurrentPlotArea!.Value;

            // The chart's own background, read from a corner no decoration reaches.
            var background = image!.GetPixel(2, 2);

            // The column just left of the plot, down the plot's own height: nothing may be painted there.
            int column = Math.Max(0, (int)plot.X - 2);
            int painted = 0;
            for (int y = (int)plot.Y; y < (int)(plot.Y + plot.Height); y++)
            {
                if (!ChartRenderHarness.IsBackground(image.GetPixel(column, y), background)) painted++;
            }

            AssertThat(painted).IsEqual(0);
        }
        finally
        {
            ChartRenderHarness.Release(view);
        }
    }
}
