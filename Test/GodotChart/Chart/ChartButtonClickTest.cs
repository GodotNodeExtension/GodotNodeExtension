namespace GodotNodeExtension.Tests.GodotChart;

using System.Collections.Generic;
using GdUnit4;
using Godot;
using GodotNodeExtension.Component.GodotChart;
using GodotNodeExtension.Component.GodotChart.Canvas;
using GodotNodeExtension.Component.GodotChart.Marks;
using Support;
using static GdUnit4.Assertions;

/// <summary>
/// Which mouse button a click came from. The left button drives the built-in selection and focus, the other
/// buttons are reported through <see cref="Chart.OnClick"/> (via
/// <see cref="ChartClickEventArgs.Button"/>) without changing chart state, so a host can use them for its own
/// gestures - a context menu, or drilling back out of a hierarchy.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class ChartButtonClickTest
{
    private static DataRow Row(string category, double value)
        => new DataRow(2).Set("category", category).Set("value", value);

    private static Chart Bars(FakeCanvas2D canvas)
    {
        var chart = new Chart(canvas) { Width = 300f, Height = 200f };
        chart.Data(new List<DataRow> { Row("A", 10.0), Row("B", 20.0), Row("C", 15.0) });
        chart.Mark(new IntervalMark());
        chart.Encode(Channel.X, "category");
        chart.Encode(Channel.Y, "value");
        chart.Render();
        return chart;
    }

    /// <summary>A point inside the middle bar's body.</summary>
    private static Vector2 OnTheMiddleBar(Chart chart)
    {
        var plot = chart.CurrentPlotArea!.Value;
        return new Vector2(plot.X + plot.Width * 0.5f, plot.Y + plot.Height - 12f);
    }

    [TestCase]
    public void TheLeftButtonSelectsAndReportsItself()
    {
        using var chart = Bars(new FakeCanvas2D());
        ChartClickEventArgs? seen = null;
        chart.OnClick += (_, e) => seen = e;

        chart.HandleClick(OnTheMiddleBar(chart));

        AssertThat(seen is not null).IsTrue();
        AssertThat(seen!.Button).IsEqual(MouseButton.Left);
        AssertThat(seen.RowIndex).IsEqual(1);
        AssertThat(chart.CurrentSelectedRowIndex).IsEqual(1);
    }

    [TestCase]
    public void TheRightButtonIsReportedWithoutSelecting()
    {
        using var chart = Bars(new FakeCanvas2D());
        ChartClickEventArgs? seen = null;
        chart.OnClick += (_, e) => seen = e;

        chart.HandleClick(OnTheMiddleBar(chart), MouseButton.Right);

        // The host still learns which element was under the pointer ...
        AssertThat(seen is not null).IsTrue();
        AssertThat(seen!.Button).IsEqual(MouseButton.Right);
        AssertThat(seen.RowIndex).IsEqual(1);

        // ... but the chart did not react to it.
        AssertThat(chart.CurrentSelectedRowIndex).IsEqual(-1);
    }

    [TestCase]
    public void ARightClickOnEmptySpaceKeepsTheSelection()
    {
        using var chart = Bars(new FakeCanvas2D());
        chart.HandleClick(OnTheMiddleBar(chart));
        AssertThat(chart.CurrentSelectedRowIndex).IsEqual(1);

        var outside = new Vector2(2f, 2f);   // far from every bar
        chart.HandleClick(outside, MouseButton.Right);
        AssertThat(chart.CurrentSelectedRowIndex).IsEqual(1);

        // The left button on empty space still clears it, as before.
        chart.HandleClick(outside);
        AssertThat(chart.CurrentSelectedRowIndex).IsEqual(-1);
    }

    [TestCase]
    public void TheViewForwardsTheButtonItReceived()
    {
        if (Engine.GetMainLoop() is not SceneTree tree) return;

        var fake = new FakeCanvas2D();
        var view = new ChartView { Size = new Vector2(300f, 200f), CanvasFactory = (_, _) => fake };
        tree.Root.AddChild(view);
        try
        {
            view.SetValues(new[] { ("A", 10.0), ("B", 20.0), ("C", 15.0) });
            for (int i = 0; i < 3; i++)
            {
                view._Process(0.016);
                foreach (var child in view.GetChildren(includeInternal: true))
                    if (child is Canvas2DControl surface)
                        surface._Process(0.016);
            }

            var plot = view.Chart!.CurrentPlotArea!.Value;
            var onTheBar = new Vector2(plot.X + plot.Width * 0.5f, plot.Y + plot.Height - 12f);

            view._GuiInput(new InputEventMouseButton
            {
                ButtonIndex = MouseButton.Right,
                Pressed = true,
                Position = onTheBar,
            });
            AssertThat(view.Chart!.CurrentSelectedRowIndex).IsEqual(-1);

            view._GuiInput(new InputEventMouseButton
            {
                ButtonIndex = MouseButton.Left,
                Pressed = true,
                Position = onTheBar,
            });
            AssertThat(view.Chart!.CurrentSelectedRowIndex).IsEqual(1);
        }
        finally
        {
            tree.Root.RemoveChild(view);
            view.Free();
        }
    }
}
