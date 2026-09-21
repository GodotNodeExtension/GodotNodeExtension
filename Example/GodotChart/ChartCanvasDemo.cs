using System;
using System.Collections.Generic;
using Godot;
using GodotNodeExtension.Component.GodotChart;
using GodotNodeExtension.Component.GodotChart.Canvas;
using GodotNodeExtension.Component.GodotChart.Marks;

namespace GodotNodeExtension.Example.GodotChart;

/// <summary>
/// Host integration showcase: hand-built charts (no <c>ChartView</c>) wired to the host loop - pointer
/// input, series visibility, the read-only queries and canvas presentation.
/// <para>
/// Covered here: pointer feedback through <see cref="Chart.Interaction"/> (crosshair) +
/// <see cref="Chart.HitTest"/> + <see cref="Chart.NotifyHoverChanged"/> + <see cref="Chart.HandleClick"/>
/// (selection), the element state styles (<see cref="ElementStateStyles"/> ActiveFill / SelectedStroke /
/// InactiveOpacity, legend click = <see cref="Chart.FocusSeries"/>), the read-only queries
/// (<see cref="Chart.CurrentPlotArea"/>, <see cref="Chart.CurrentSelectedRowIndex"/>,
/// <see cref="Chart.CurrentHoveredRowIndex"/>, <see cref="Chart.GetSeriesInfo"/>,
/// <see cref="Chart.GetRenderDataSnapshot"/>), series visibility (<see cref="Chart.HideSeries"/> /
/// <see cref="Chart.ShowSeries"/>), both <see cref="Chart.AppendData(DataRow)"/> overloads (one row and a
/// batch, with the axis refitting itself to the new category), one canvas from <see cref="Canvas2DFactory"/>
/// shared by <b>two</b> chart consumers whose texture a plain <see cref="TextureRect"/> presents, and the two
/// <see cref="Chart.Dispose"/> rules (<c>ownsCanvas</c> plus <see cref="Canvas2DControl.OwnsCanvas"/>).
/// </para>
/// <para>
/// Hover a bar for the Active state, click for Selected, click a legend entry to focus a series, <c>H</c> /
/// <c>L</c> hide or show a series, <c>A</c> / <c>B</c> append a row / a batch of rows to the interactive chart
/// (the status line counts them). Dynamic data is ChartStreamingDemo, the events are ChartCallbacksDemo,
/// the scales are ChartScaleDemo and the marks and encodings are ChartCustomizationDemo.
/// </para>
/// </summary>
public partial class ChartCanvasDemo : Control
{
    /// <summary>Pixel size of the canvas the two shared-canvas charts divide between them (two halves).</summary>
    private const int SharedWidth = 720;
    private const int SharedHeight = 200;

    private Chart? _interactive;
    private ICanvas2D? _sharedCanvas;
    private Chart? _sharedLine;
    private Chart? _sharedPoints;

    private double _statusClock;
    private int _frames;
    private bool _firstSeriesHidden;
    private int _appendedMonths;

    /// <summary>Months the <c>A</c> / <c>B</c> keys append, one press at a time.</summary>
    private static readonly string[] ExtraMonths = ["May", "Jun", "Jul", "Aug"];

    // ── Scene setup ──────────────────────────────────────────────────────────

    // Wired in ChartCanvasDemo.tscn (node_paths + NodePath): the container skeleton, the two captions, the
    // status line and the preview rectangle live in the scene - this script only builds the charts.

    /// <summary>Canvas the interactive chart is drawn into; its pointer events drive the chart's state.</summary>
    [Export] public Canvas2DControl InteractiveView { get; set; } = null!;

    /// <summary>Dynamic readout of the chart's queries (a plain <see cref="Label"/>, no bbcode).</summary>
    [Export] public Label Status { get; set; } = null!;

    /// <summary>Presents the texture of the canvas the two shared charts draw into.</summary>
    [Export] public TextureRect SharedPreview { get; set; } = null!;

    /// <inheritdoc />
    public override void _Ready()
    {
        BuildInteractiveChart();
        BuildSharedCanvas();
        UpdateStatus(0.0);
    }

    /// <summary>
    /// Interactive section: the <see cref="Canvas2DControl"/> the scene declares hosts the canvas, and the
    /// pointer events of that node drive the chart's hover / selection state.
    /// </summary>
    private void BuildInteractiveChart()
    {
        if (InteractiveView.Canvas is not { } canvas)
        {
            Status.Text = "no canvas: the demo needs a rendering device";
            return;
        }

        // OwnsCanvas is already true by default; it is spelled out here so the rule the _ExitTree comment
        // states - the view releases the canvas it hosts, the charts never do - is visible where it is
        // relied on, instead of only where the default happens to be declared.
        InteractiveView.OwnsCanvas = true;
        InteractiveView.MouseFilter = MouseFilterEnum.Stop; // else this node never sees the pointer
        InteractiveView.GuiInput += OnViewInput;
        InteractiveView.MouseExited += OnPointerLeft;

        var chart = BuildBars(canvas);
        _interactive = chart;
        InteractiveView.CanvasDraw += (control, _) =>
        {
            chart.Width = Mathf.Max(1f, control.CanvasSize.X);
            chart.Height = Mathf.Max(1f, control.CanvasSize.Y);
            chart.Render();
        };
        InteractiveView.Invalidate();
    }

    /// <summary>The interactive chart: stacked bars per channel, a legend, and mark-level state styles.</summary>
    private static Chart BuildBars(ICanvas2D canvas)
    {
        var chart = new Chart(canvas)       // ownsCanvas: false - the view owns this canvas
        {
            Width = 640f,
            Height = 360f,
            Title = "Sessions per month - hover, click, legend focus",
        };
        chart.Theme(ChartTheme.Dark());
        chart.Data(MonthRows());
        chart.Encode(Channel.X, "month");           // text -> the chart infers an OrdinalScale
        chart.Encode(Channel.Y, "sessions");
        chart.Encode(Channel.Color, "channel");
        chart.XAxis(new AxisConfig { Title = "Month" });
        chart.YAxis(new AxisConfig { Title = "Sessions", Unit = "k" });
        chart.Legend(new LegendConfig { Position = LegendPosition.Bottom });

        // G2's state styles: only what differs from the theme has to be declared, and they are what the
        // hover (Active), the selection (Selected) and a focused series (Inactive) are painted with.
        var bars = new IntervalMark { Stack = StackMode.Stack, BarPadding = 0.28f };
        bars.States.ActiveFill = new Color(0.55f, 0.92f, 1f);
        bars.States.ActiveBrighten = 1.35f;
        bars.States.SelectedStroke = new Color(1f, 0.92f, 0.45f);
        bars.States.SelectedStrokeWidth = 3f;
        bars.States.InactiveOpacity = 0.2f;
        chart.Mark(bars);
        return chart;
    }

    /// <summary>
    /// Presentation section: one canvas built by <see cref="Canvas2DFactory"/> is consumed by two charts -
    /// each one owns half of it through <see cref="Chart.OffsetX"/> and <see cref="Chart.Width"/> - and the
    /// resulting texture is handed to the <see cref="TextureRect"/> the scene declares, with no
    /// <see cref="Canvas2DControl"/> of its own.
    /// </summary>
    private void BuildSharedCanvas()
    {
        ICanvas2D canvas;
        try
        {
            canvas = Canvas2DFactory.Create(SharedWidth, SharedHeight);
        }
        catch (Exception ex)
        {
            // A surface needs a rendering device: a headless run (or --rendering-driver dummy) has none,
            // and this page builds its canvas by hand instead of letting a Canvas2DControl host it, so it
            // is the page's job to report that and keep the rest of the demo working.
            _sharedCanvas = null;
            SharedPreview.Texture = null;
            SharedPreview.Visible = false;
            Status.Text = $"shared canvas: unavailable ({ex.GetType().Name}) - run with a rendering device";
            return;
        }

        _sharedCanvas = canvas;

        // Static rows, drawn by both consumers: a DataRow is read-only inside a chart, so the two charts can
        // share the very same objects instead of copying them.
        var rows = SharedRows();

        var line = BuildSharedChart(canvas, 0f,
            "shared canvas - LineMark (consumer 1)", new LineMark { Smooth = false });
        line.Data(rows);
        _sharedLine = line;

        var points = BuildSharedChart(canvas, SharedWidth * 0.5f,
            "the same rows - PointMark (consumer 2)", new PointMark { DefaultRadius = 3f });
        points.Data(rows);
        _sharedPoints = points;

        SharedPreview.Texture = canvas.Texture;
        RenderSharedCanvas();
    }

    /// <summary>One of the two charts sharing a canvas; both keep <c>ownsCanvas: false</c>.</summary>
    private static Chart BuildSharedChart(ICanvas2D canvas, float offsetX, string title, Mark mark)
    {
        var chart = new Chart(canvas)
        {
            Width = SharedWidth * 0.5f,
            Height = SharedHeight,
            OffsetX = offsetX,
            Title = title,
            PaddingLeft = 34f,
            PaddingTop = 24f,
            PaddingRight = 10f,
            PaddingBottom = 18f,
        };
        chart.Theme(ChartTheme.Dark());
        chart.Encode(Channel.X, "t");
        chart.Encode(Channel.Y, "value");
        chart.Encode(Channel.Color, "series");
        chart.Mark(mark);
        chart.XAxis(new AxisConfig { Title = "sample" });
        chart.YAxis(new AxisConfig { Title = "level" });
        return chart;
    }

    // ── Shared-canvas data ───────────────────────────────────────────────────

    /// <summary>The static rows both shared-canvas charts draw: one sample per row, in two series.</summary>
    private static List<DataRow> SharedRows()
    {
        double[] values =
        [
            48.0, 52.0, 61.0, 58.0, 66.0, 72.0, 69.0, 75.0, 71.0, 64.0, 58.0, 55.0,
            61.0, 68.0, 74.0, 79.0, 76.0, 70.0, 63.0, 59.0, 54.0, 57.0, 62.0, 67.0,
        ];
        var rows = new List<DataRow>(values.Length);
        for (int i = 0; i < values.Length; i++)
        {
            int sample = i + 1;
            rows.Add(Row(("t", sample), ("value", values[i]), ("series", sample % 3 == 0 ? "noise" : "load")));
        }
        return rows;
    }

    /// <summary>
    /// Draw the shared canvas. Nothing hosts it, so its owner drives the frame by hand: <c>Tick</c> once,
    /// then begin, draw both charts, end - exactly what <see cref="Canvas2DControl"/> does for its own canvas.
    /// The rows are static, so one draw at build time is enough.
    /// </summary>
    private void RenderSharedCanvas()
    {
        if (_sharedCanvas is not { } canvas) return;

        canvas.Tick();
        canvas.BeginFrame();
        canvas.Clear(new Color(0.05f, 0.05f, 0.08f));
        _sharedLine?.Render();
        _sharedPoints?.Render();
        canvas.EndFrame();
    }

    // ── Pointer and keyboard ─────────────────────────────────────────────────

    /// <summary>Pointer events of the interactive view: hover feeds the chart, a click selects an element.</summary>
    private void OnViewInput(InputEvent @event)
    {
        if (_interactive is not { } chart) return;

        switch (@event)
        {
            case InputEventMouseMotion motion:
                FeedPointer(motion.Position);
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } button:
                chart.HandleClick(button.Position);   // selection + OnClick / OnSelectionChanged / legend focus
                FeedPointer(button.Position);
                break;
        }
    }

    /// <summary>
    /// Feed the pointer to the chart: <see cref="Chart.Interaction"/> places the crosshair,
    /// <see cref="Chart.HitTest"/> finds the element under it and <see cref="Chart.NotifyHoverChanged"/> raises
    /// the hover notification that the state styles render.
    /// </summary>
    private void FeedPointer(Vector2 position)
    {
        if (_interactive is not { } chart) return;

        chart.Interaction(position);
        var hit = chart.HitTest(position);                 // needs the layout of the last Render()
        // NotifyHoverChanged stores the row and raises OnHover when the row actually changed; Hover() would
        // store it first, so the change the notification compares against would already be gone and the
        // event would stay silent.
        chart.NotifyHoverChanged(hit?.RowIndex ?? -1, hit);
        InteractiveView.Invalidate();
    }

    /// <summary>The pointer left the view: drop hover and crosshair so no element stays highlighted.</summary>
    private void OnPointerLeft()
    {
        if (_interactive is not { } chart) return;

        chart.Interaction(null);
        chart.NotifyHoverChanged(-1, null);
        InteractiveView.Invalidate();
    }

    /// <inheritdoc />
    public override void _Input(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false } key) return;
        if (_interactive is not { } chart) return;

        switch (key.Keycode)
        {
            case Key.H:
                _firstSeriesHidden = !_firstSeriesHidden;
                // The key comes from the chart itself, so the demo never hard-codes a field value.
                var series = chart.GetSeriesInfo();
                if (series.Count == 0) break;
                if (_firstSeriesHidden) chart.HideSeries(series[0].Key);
                else chart.ShowSeries(series[0].Key);
                break;
            case Key.L:
                _firstSeriesHidden = false;
                chart.ShowAllSeries();
                break;
            case Key.A:
                // AppendData(DataRow): one row at a time. A new category is enough - the ordinal axis is
                // refitted from the data, so the bar appears without touching the scale by hand.
                string month = ExtraMonths[_appendedMonths % ExtraMonths.Length];
                chart.AppendData(Row(("month", month), ("channel", "web"), ("sessions", 40 + _appendedMonths * 7)));
                _appendedMonths++;
                break;
            case Key.B:
                // AppendData(IEnumerable<DataRow>): a batch, which is what a feed that has both channels of
                // one month at hand hands over in one call.
                string batchMonth = ExtraMonths[_appendedMonths % ExtraMonths.Length];
                DataRow[] batch =
                [
                    Row(("month", batchMonth), ("channel", "web"), ("sessions", 55 + _appendedMonths * 3)),
                    Row(("month", batchMonth), ("channel", "app"), ("sessions", 33 + _appendedMonths * 5)),
                ];
                chart.AppendData(batch);
                _appendedMonths++;
                break;
            default:
                return;                                    // nothing changed: no redraw needed
        }

        // The key was handled here, so the GUI must not also act on it: _Input runs before the GUI, and the
        // example browser's tree reads a plain letter as type-ahead.
        GetViewport().SetInputAsHandled();
        InteractiveView.Invalidate();
    }

    // ── Frame loop ───────────────────────────────────────────────────────────

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        // The charts here are static, so only the readout refreshes (a few times a second).
        _frames++;
        _statusClock += delta;
        if (_statusClock >= 0.3)
        {
            UpdateStatus(_frames / _statusClock);
            _statusClock = 0;
            _frames = 0;
        }
    }

    /// <summary>
    /// Print the read-only queries of the interactive chart: the plot rectangle and the selection / hover row
    /// index of the last frame, the series keys with the colours the colour scale mapped them to, and the row
    /// count the shared canvas charts draw (through <see cref="Chart.GetRenderDataSnapshot"/>).
    /// </summary>
    private void UpdateStatus(double fps)
    {
        var plot = _interactive?.CurrentPlotArea;
        var keys = new List<string>();
        if (_interactive is not null)
        {
            foreach (var (key, _) in _interactive.GetSeriesInfo()) keys.Add(key);
        }

        Status.Text =
            $"{fps:F0} fps · CurrentPlotArea {plot?.Width:F0}x{plot?.Height:F0} at ({plot?.X:F0},{plot?.Y:F0}) · " +
            $"CurrentHoveredRowIndex {_interactive?.CurrentHoveredRowIndex ?? -1} · " +
            $"CurrentSelectedRowIndex {_interactive?.CurrentSelectedRowIndex ?? -1} · " +
            $"GetSeriesInfo [{string.Join(", ", keys)}] hidden: {_firstSeriesHidden} · " +
            $"rows {_interactive?.GetRenderDataSnapshot().Count ?? 0} (A / B append) · " +
            $"shared rows {_sharedLine?.GetRenderDataSnapshot().Count ?? 0}";

    }

    // ── Data ─────────────────────────────────────────────────────────────────

    /// <summary>Two channels over four months: the rows behind the stacked, interactive bar chart.</summary>
    private static List<DataRow> MonthRows() =>

    [
        Row(("month", "Jan"), ("channel", "web"), ("sessions", 62)),
        Row(("month", "Jan"), ("channel", "app"), ("sessions", 38)),
        Row(("month", "Feb"), ("channel", "web"), ("sessions", 74)),
        Row(("month", "Feb"), ("channel", "app"), ("sessions", 45)),
        Row(("month", "Mar"), ("channel", "web"), ("sessions", 58)),
        Row(("month", "Mar"), ("channel", "app"), ("sessions", 61)),
        Row(("month", "Apr"), ("channel", "web"), ("sessions", 91)),
        Row(("month", "Apr"), ("channel", "app"), ("sessions", 52)),
    ];

    /// <summary>Build a row from field/value pairs.</summary>
    private static DataRow Row(params (string Field, object Value)[] fields)
    {
        var row = new DataRow(fields.Length);
        foreach (var (field, value) in fields) row.Set(field, value);
        return row;
    }

    /// <inheritdoc />
    public override void _ExitTree()
    {
        // Two owners, two rules: the interactive chart keeps the default ownsCanvas: false, because the
        // Canvas2DControl hosting that canvas owns it (OwnsCanvas) and releases it when the node leaves the
        // tree - an owned canvas would be disposed twice. The shared canvas has no node owner at all, so the
        // demo disposes it here, after the last of its two consumers.
        _interactive?.Dispose();
        _sharedLine?.Dispose();
        _sharedPoints?.Dispose();
        _sharedCanvas?.Dispose();
        _sharedCanvas = null;
    }
}
