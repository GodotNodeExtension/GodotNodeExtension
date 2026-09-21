using System;
using System.Collections.Generic;
using System.Text;
using Godot;
using GodotNodeExtension.Component.GodotChart;
using GodotNodeExtension.Component.GodotChart.Canvas;
using GodotNodeExtension.Component.GodotChart.Marks;

namespace GodotNodeExtension.Example.GodotChart;

/// <summary>
/// The chart API that <b>only code can reach</b> - nothing in this scene is an export. Every capability below
/// hangs off <see cref="ChartView.ConfigureChart"/>, the hook that hands the script the live
/// <see cref="Chart"/> instance:
/// <list type="bullet">
/// <item>the five events (<see cref="Chart.OnHover"/>, <see cref="Chart.OnClick"/>,
/// <see cref="Chart.OnSelectionChanged"/>, <see cref="Chart.OnFocusChanged"/>,
/// <see cref="Chart.OnLegendClick"/>) and the <c>Handled</c> flag that skips the default legend toggle;</item>
/// <item>the state verbs (<see cref="Chart.Select"/>, <see cref="Chart.FocusSeries"/>,
/// <see cref="Chart.HideSeries"/>, <see cref="Chart.ShowSeries"/>,
/// <see cref="Chart.ToggleSeriesVisibility"/>, <see cref="Chart.ShowAllSeries"/>,
/// <see cref="Chart.IsSeriesHidden"/>) beside the read-only queries (<see cref="Chart.CurrentFocusedSeries"/>,
/// <see cref="Chart.CurrentSelectedRowIndex"/>);</item>
/// <item>an external legend built from <see cref="Chart.GetSeriesInfo"/> while the chart runs with
/// <see cref="LegendPosition.None"/>, and the focus fade (<see cref="ChartTheme.UnfocusedOpacity"/>) beside
/// a hidden series;</item>
/// <item>a hand-built host (a bare <see cref="Canvas2DControl"/> plus a <see cref="Chart"/>) running the
/// pointer loop (<see cref="Chart.HitTest"/>, <see cref="Chart.Interaction"/>,
/// <see cref="Chart.NotifyHoverChanged"/>) that <see cref="ChartView"/> usually runs for you. It calls
/// <see cref="Chart.NotifyHoverChanged"/> and not <see cref="Chart.Hover"/>, because <c>Hover(row)</c> is the
/// silent variant: it stores the row without raising <see cref="Chart.OnHover"/>, which is the notification
/// this page is about.</item>
/// </list>
/// <para>
/// Event payloads: <c>RowIndex</c> is -1 as soon as the pointer leaves the data, <c>MarkType</c> names the mark
/// that produced the hit (<c>PointMark</c> here) or one of the non-data zones - <c>Legend</c> for a legend
/// entry, <c>XAxis</c> / <c>YAxis</c> for an axis zone, which only exists while that axis carries a title or a
/// unit - and <c>ScreenPosition</c> is the hit element's centre for a data or legend hit; an axis hit keeps the pointer coordinate. A hover event fires only when
/// the row really changed, and <c>Handled = true</c> in <see cref="Chart.OnLegendClick"/> is the one way to
/// keep the built-in focus toggle from running: that click then raises no
/// <see cref="Chart.OnFocusChanged"/>, while every other legend entry still does.
/// </para>
/// <para>
/// State lives on the chart <i>instance</i>, so the demo repaints and never rebuilds:
/// <see cref="ChartView.Repaint"/> draws a new frame of the same chart (hover, selection, focus and the hidden
/// set survive) where <see cref="ChartView.Refresh"/> would replace it and drop all of that - which is also why
/// the handlers re-subscribe in <see cref="ChartView.ConfigureChart"/> and why the hidden set is applied there.
/// A hidden series keeps its legend entry, drawn with <see cref="ChartTheme.LegendDimmedOpacity"/>, and every
/// series outside the focused one renders at <see cref="ChartTheme.UnfocusedOpacity"/>.
/// </para>
/// <para>
/// <see cref="Chart.GetSeriesInfo"/> reads the colour scale, which a chart only fits while rendering, so an
/// external legend is legitimately empty for the first frames. <see cref="LegendPosition.None"/> drops the
/// legend renderer (and its hit region), not the scale the external UI reads.
/// </para>
/// <para>
/// The hand-built cell replaces <see cref="ChartView"/> with its two bare pieces: the host runs
/// <see cref="Chart.HitTest"/>, <see cref="Chart.Interaction"/> and <see cref="Chart.NotifyHoverChanged"/>
/// itself - exactly what <c>ChartView._GuiInput</c> and its <c>UpdateHover</c> do (<c>Hover(row)</c> is
/// deliberately not called: it sets the row without raising the hover event) - and draws the chart into the
/// canvas minus a strip at the bottom, where its readout lines sit (the hit test shares that layout, because
/// it reads the chart's own width and height). A hand-built chart is only needed for a custom host, or when
/// the input handling of the node itself gets in the way.
/// </para>
/// </summary>
public partial class ChartCallbacksDemo : Control
{
    private const string NorthSeries = "north";
    private const string SouthSeries = "south";
    private const string EastSeries = "east";

    /// <summary>Row <see cref="Chart.Select"/> highlights: the third row of the shared two-series data.</summary>
    private const int SelectedRow = 2;

    private const int MaxLogLines = 5;
    private const float HandReadoutBand = 52f;   // strip at the bottom of the hand-built canvas, for the readout

    // The event names double as the counter labels, in print order.
    private static readonly string[] EventNames =
        ["OnHover", "OnClick", "OnSelectionChanged", "OnFocusChanged", "OnLegendClick"];

    private readonly Dictionary<string, int> _eventCounts = [];
    private readonly List<string> _eventLines = [];
    private readonly List<string> _legendKeys = [];

    private readonly ChartTheme _handTheme = ChartTheme.Dark();
    private Chart? _handChart;
    private string _handHitLine = "HitTest(pos) -> move the pointer over the chart";
    private string _handStateLine = "Interaction | NotifyHoverChanged(row, hit)";
    private string _handEventLine = "chart.OnHover / chart.OnClick: nothing yet";

    // ── Nodes ─────────────────────────────────────────────────────────────────

    /// <summary>Chart whose five events this demo logs.</summary>
    [Export] public ChartView EventsChart { get; set; } = null!;

    /// <summary>Multi-line log of the event payloads and the per-event counters.</summary>
    [Export] public Label EventLog { get; set; } = null!;

    /// <summary>Chart the button panel drives through <see cref="ChartView.Chart"/>.</summary>
    [Export] public ChartView ButtonsChart { get; set; } = null!;

    /// <summary>Calls <see cref="Chart.Select"/>.</summary>
    [Export] public Button SelectButton { get; set; } = null!;

    /// <summary>Calls <see cref="Chart.FocusSeries"/> with the "north" key.</summary>
    [Export] public Button FocusNorthButton { get; set; } = null!;

    /// <summary>Calls <see cref="Chart.FocusSeries"/> with null (clears the focus).</summary>
    [Export] public Button ClearFocusButton { get; set; } = null!;

    /// <summary>Calls <see cref="Chart.HideSeries"/>.</summary>
    [Export] public Button HideButton { get; set; } = null!;

    /// <summary>Calls <see cref="Chart.ShowSeries"/>.</summary>
    [Export] public Button ShowButton { get; set; } = null!;

    /// <summary>Calls <see cref="Chart.ToggleSeriesVisibility"/>.</summary>
    [Export] public Button ToggleButton { get; set; } = null!;

    /// <summary>Calls <see cref="Chart.ShowAllSeries"/>.</summary>
    [Export] public Button ShowAllButton { get; set; } = null!;

    /// <summary>Calls <see cref="Chart.IsSeriesHidden"/> for both series and reports the result.</summary>
    [Export] public Button IsHiddenButton { get; set; } = null!;

    /// <summary>Result line of the button panel, in the header row.</summary>
    [Export] public Label Status { get; set; } = null!;

    /// <summary>Chart that draws no legend of its own (<see cref="LegendPosition.None"/>).</summary>
    [Export] public ChartView LegendChart { get; set; } = null!;

    /// <summary>Host of the generated external legend rows (<see cref="Chart.GetSeriesInfo"/>).</summary>
    [Export] public VBoxContainer LegendList { get; set; } = null!;

    /// <summary>Bare canvas this demo drives by hand instead of using a <see cref="ChartView"/>.</summary>
    [Export] public Canvas2DControl HandSurface { get; set; } = null!;

    /// <summary>Chart with a hidden series next to a focused one, to show both effects at once.</summary>
    [Export] public ChartView HiddenChart { get; set; } = null!;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public override void _Ready()
    {
        WireEventChart();
        WireButtons();
        LegendChart.SetData(TwoSeriesRows());
        CreateHandChart();

        // The fade is theme-driven: the default UnfocusedOpacity (0.15) is nearly invisible on dark.
        var fadeTheme = ChartTheme.Dark();
        fadeTheme.UnfocusedOpacity = 0.35f;
        HiddenChart.CustomTheme = fadeTheme;
        HiddenChart.ConfigureChart = WireHiddenSeries;
        HiddenChart.SetData(ThreeSeriesRows());

        UpdateEventLog();
        Report("ready - hover a chart, click an element, click a legend entry, then press the buttons");
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        // GetSeriesInfo() reads a scale that is only fitted during the chart's first Render().
        SyncExternalLegend();
    }

    // ── 1. The five events ────────────────────────────────────────────────────
    /// <summary>Subscribe the five events on every rebuild (see the class remarks for the payload rules).</summary>
    private void WireEventChart()
    {
        EventsChart.SetData(TwoSeriesRows());
        EventsChart.ConfigureChart = chart =>
        {
            chart.OnHover += (_, e) => Log("OnHover",
                $"row {e.PreviousRowIndex} -> {e.RowIndex} | mark {OrNone(e.MarkType)} | series {OrNone(e.SeriesKey)}"
                + $" | screen ({e.ScreenPosition.X:F0}, {e.ScreenPosition.Y:F0})");
            chart.OnClick += (_, e) => Log("OnClick",
                $"mark {OrNone(e.MarkType)} | row {e.RowIndex} ({RowText(e.Row)}) | series {OrNone(e.SeriesKey)}"
                + $" | screen ({e.ScreenPosition.X:F0}, {e.ScreenPosition.Y:F0})");
            chart.OnSelectionChanged += (_, e) => Log("OnSelectionChanged",
                $"row {e.PreviousRowIndex} -> {e.RowIndex} ({RowText(e.Row)}) | series {OrNone(e.SeriesKey)}");
            chart.OnFocusChanged += (_, e) => Log("OnFocusChanged",
                $"focused {OrNone(e.PreviousSeriesKey)} -> {OrNone(e.SeriesKey)}");
            // Intercept one entry: a click on "north" is logged and leaves the focus alone.
            chart.OnLegendClick += (_, e) =>
            {
                bool handled = e.SeriesKey == NorthSeries;
                e.Handled = handled;
                Log("OnLegendClick", $"entry {e.SeriesKey} | focused before: {OrNone(e.CurrentFocusedSeries)}"
                    + $" | Handled = {Bool(handled)}");
            };
        };
    }

    /// <summary>Count one event and refresh the log label (newest line first, counters on top).</summary>
    private void Log(string name, string payload)
    {
        _eventCounts[name] = _eventCounts.GetValueOrDefault(name) + 1;
        _eventLines.Insert(0, $"{name}: {payload}");
        if (_eventLines.Count > MaxLogLines) _eventLines.RemoveAt(_eventLines.Count - 1);
        UpdateEventLog();
    }

    /// <summary>Render the counters plus the buffered event lines into the log label.</summary>
    private void UpdateEventLog()
    {
        var builder = new StringBuilder("counters");
        for (int i = 0; i < EventNames.Length; i++)
        {
            if (i > 0) builder.Append(" | ");
            builder.Append(EventNames[i]).Append('=').Append(_eventCounts.GetValueOrDefault(EventNames[i]));
        }
        builder.Append("\n\n").Append(_eventLines.Count == 0
            ? "hover a point, click it, then click a legend entry (the \"north\" one is intercepted)."
            : string.Join("\n", _eventLines));
        EventLog.Text = builder.ToString();
    }

    // ── 2. The state verbs, driven by buttons ─────────────────────────────────
    /// <summary>One button per verb; each call runs on the live chart and is followed by a repaint.</summary>
    private void WireButtons()
    {
        ButtonsChart.SetData(TwoSeriesRows());
        SelectButton.Pressed += () => OnChart($"Select({SelectedRow})", chart =>
        { chart.Select(SelectedRow); return SelectedReport(chart); });
        FocusNorthButton.Pressed += () => OnChart($"FocusSeries({Quoted(NorthSeries)})", chart =>
        { chart.FocusSeries(NorthSeries); return FocusReport(chart); });
        ClearFocusButton.Pressed += () => OnChart("FocusSeries(null)", chart =>
        { chart.FocusSeries(null); return FocusReport(chart); });
        HideButton.Pressed += () => OnChart($"HideSeries({Quoted(SouthSeries)})", chart =>
        { chart.HideSeries(SouthSeries); return HiddenReport(chart); });
        ShowButton.Pressed += () => OnChart($"ShowSeries({Quoted(SouthSeries)})", chart =>
        { chart.ShowSeries(SouthSeries); return HiddenReport(chart); });
        ToggleButton.Pressed += () => OnChart($"ToggleSeriesVisibility({Quoted(SouthSeries)})", chart =>
        { chart.ToggleSeriesVisibility(SouthSeries); return HiddenReport(chart); });
        ShowAllButton.Pressed += () => OnChart("ShowAllSeries()", chart =>
        { chart.ShowAllSeries(); return HiddenReport(chart); });
        // A pure read: this button changes nothing, it only asks the chart what it currently shows.
        IsHiddenButton.Pressed += () => OnChart("IsSeriesHidden(...)", chart =>
            HiddenReport(chart) + $" | CurrentSelectedRowIndex = {chart.CurrentSelectedRowIndex}"
            + $" | CurrentFocusedSeries = {OrNone(chart.CurrentFocusedSeries)}");
    }

    /// <summary>Run one verb on the live chart instance, repaint it and put the result on the status label.</summary>
    private void OnChart(string call, Func<Chart, string> verb)
    {
        if (ButtonsChart.Chart is not { } chart) return;

        string result = verb(chart);
        ButtonsChart.Repaint();     // Repaint, not Refresh: a rebuild would drop what the verb just set
        Report($"{call} -> {result}");
    }

    /// <summary>What <see cref="Chart.CurrentSelectedRowIndex"/> answers after <see cref="Chart.Select"/>.</summary>
    private static string SelectedReport(Chart chart)
        => $"CurrentSelectedRowIndex = {chart.CurrentSelectedRowIndex} (that point gets a selection ring)";

    /// <summary>What <see cref="Chart.CurrentFocusedSeries"/> answers after a focus change.</summary>
    private static string FocusReport(Chart chart)
        => $"CurrentFocusedSeries = {OrNone(chart.CurrentFocusedSeries)} | others fade to theme.UnfocusedOpacity";

    /// <summary>What <see cref="Chart.IsSeriesHidden"/> answers for both series of the button chart.</summary>
    private static string HiddenReport(Chart chart)
        => $"IsSeriesHidden({Quoted(NorthSeries)}) = {Bool(chart.IsSeriesHidden(NorthSeries))}"
            + $" | IsSeriesHidden({Quoted(SouthSeries)}) = {Bool(chart.IsSeriesHidden(SouthSeries))}";

    // ── 3. An external legend, built from GetSeriesInfo ───────────────────────
    /// <summary>Build the external legend once the chart can name its series, and only when that list changes.</summary>
    private void SyncExternalLegend()
    {
        if (LegendChart.Chart?.GetSeriesInfo() is not { Count: > 0 } info) return;
        if (LegendMatches(info)) return;

        _legendKeys.Clear();
        // Free() and not QueueFree(): a queued row stays in the tree until the end of the frame, so the VBox
        // would hold the old rows and the new ones at the same time and briefly double in height.
        foreach (var child in LegendList.GetChildren()) child.Free();
        foreach (var (key, color) in info)
        {
            _legendKeys.Add(key);
            LegendList.AddChild(BuildLegendRow(key, color));
        }
    }

    /// <summary>True when the generated rows already show exactly these series, in this order.</summary>
    private bool LegendMatches(IReadOnlyList<(string Key, Color Color)> info)
    {
        if (info.Count != _legendKeys.Count) return false;
        for (int i = 0; i < info.Count; i++)
            if (info[i].Key != _legendKeys[i]) return false;
        return true;
    }

    /// <summary>One generated legend row: a colour swatch, the series key and a focus button.</summary>
    private HBoxContainer BuildLegendRow(string key, Color color)
    {
        var row = new HBoxContainer { Name = $"Legend_{key}" };
        row.AddChild(new ColorRect
        {
            Color = color,
            CustomMinimumSize = new Vector2(14f, 14f),
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
        });
        row.AddChild(new Label { Text = key + " " });

        var focus = new Button { Text = $"FocusSeries({Quoted(key)})" };
        focus.Pressed += () =>
        {
            if (LegendChart.Chart is not { } chart) return;
            bool focused = chart.CurrentFocusedSeries == key;
            chart.FocusSeries(focused ? null : key);            // the row toggles its own series
            LegendChart.Repaint();
            string call = focused ? "null" : Quoted(key);
            Report($"GetSeriesInfo() row -> FocusSeries({call}) -> {FocusReport(chart)}");
        };
        row.AddChild(focus);
        return row;
    }

    // ── 4. The hand-driven pointer loop ───────────────────────────────────────
    /// <summary>Host the hand-built chart: a bare canvas plus a Chart the demo drives itself.</summary>
    private void CreateHandChart()
    {
        if (HandSurface.Canvas is not { } canvas) return;   // no rendering device: leave the cell empty

        // Control's default mouse filter already is Stop; set it explicitly so the canvas is the node that
        // receives the pointer events (ChartView does the same on itself).
        HandSurface.MouseFilter = MouseFilterEnum.Stop;
        HandSurface.BackgroundColor = _handTheme.BackgroundColor;
        HandSurface.CanvasDraw += OnHandSurfaceDraw;
        HandSurface.GuiInput += OnHandSurfaceInput;
        HandSurface.MouseExited += OnHandSurfaceExit;

        var chart = new Chart(canvas)
        {
            Width = Math.Max(1, HandSurface.CanvasSize.X),
            Height = Math.Max(1, HandSurface.CanvasSize.Y - HandReadoutBand),
            Title = "Hit test and hover by hand",
        };
        chart.Theme(_handTheme);
        chart.Data(SingleSeriesRows());
        chart.Mark(new IntervalMark());
        chart.Encode(Channel.X, "category");
        chart.Encode(Channel.Y, "value");
        // No Legend(...) call: a hand-built chart only gets what the host asks for.

        chart.OnHover += (_, e) =>
        {
            _handEventLine = $"chart.OnHover: row {e.PreviousRowIndex} -> {e.RowIndex} | mark {OrNone(e.MarkType)}";
            HandSurface.Invalidate();
        };
        chart.OnClick += (_, e) =>
        {
            _handEventLine = $"chart.OnClick: row {e.RowIndex} (HandleClick also set the selection)";
            HandSurface.Invalidate();
        };
        _handChart = chart;
        HandSurface.Invalidate();
    }

    /// <summary>Draw the hand-built chart above the readout strip, then the pointer readout inside it.</summary>
    private void OnHandSurfaceDraw(Canvas2DControl control, ICanvas2D canvas)
    {
        if (_handChart is null) return;

        // The chart gets the canvas minus the readout strip, so the text can never cover a bar or an axis label.
        _handChart.Width = Math.Max(1, control.CanvasSize.X);
        _handChart.Height = Math.Max(1, control.CanvasSize.Y - HandReadoutBand);
        _handChart.Render();

        using var paint = canvas.CreatePaint();
        paint.SetColor(new Color(1f, 1f, 1f, 0.85f));
        var font = new FontSettings { Size = 11f };
        float top = _handChart.Height + 12f;
        canvas.DrawText(_handHitLine, 8f, top, font, paint);
        canvas.DrawText(_handStateLine, 8f, top + 13f, font, paint);
        canvas.DrawText(_handEventLine, 8f, top + 26f, font, paint);
    }

    /// <summary>Host pointer loop: motion updates the hover state, a press selects through <c>HandleClick</c>.</summary>
    private void OnHandSurfaceInput(InputEvent @event)
    {
        if (_handChart is not { } chart) return;

        switch (@event)
        {
            case InputEventMouseMotion motion:
                UpdateHandHover(motion.Position);
                HandSurface.AcceptEvent();      // the host consumed the pointer event
                break;

            case InputEventMouseButton { Pressed: true } click:
                chart.HandleClick(click.Position);
                UpdateHandHover(click.Position);
                HandSurface.AcceptEvent();
                break;
        }
    }

    /// <summary>The four hover calls plus a new frame, and the text of the readout strip.</summary>
    private void UpdateHandHover(Vector2 position)
    {
        if (_handChart is not { } chart) return;

        chart.Interaction(position);
        var hit = chart.HitTest(position);

        // Legend / axis hits are not data elements and carry RowIndex = 0, so they map to -1 (as ChartView does).
        int rowIndex = hit is { Hit: true, MarkType: not "Legend" and not "XAxis" and not "YAxis" }
            ? hit.RowIndex
            : -1;
        // NotifyHoverChanged stores the row *and* raises OnHover when it changed; calling Hover() first
        // would set the row and silence the event.
        chart.NotifyHoverChanged(rowIndex, hit);

        _handHitLine = hit is { Hit: true }
            ? $"HitTest({position.X:F0},{position.Y:F0}) -> {OrNone(hit.MarkType)} row {hit.RowIndex} {OrNone(hit.Label)}"
            : $"HitTest({position.X:F0},{position.Y:F0}) -> null";
        _handStateLine = $"Interaction | NotifyHoverChanged({rowIndex}, hit)";
        HandSurface.Invalidate();
    }

    /// <summary>Leaving the canvas clears everything the pointer had set - ChartView's mouse-exit path.</summary>
    private void OnHandSurfaceExit()
    {
        if (_handChart is not { } chart) return;

        chart.Interaction(null);                // the crosshair disappears with the anchor
        chart.NotifyHoverChanged(-1, null);
        _handHitLine = "pointer left the canvas";
        _handStateLine = "Interaction(null) | NotifyHoverChanged(-1, null)";
        HandSurface.Invalidate();
    }

    // ── 5. Series visibility and the focus fade ───────────────────────────────
    /// <summary>Hide one series and focus another on every rebuild (see the class remarks).</summary>
    private static void WireHiddenSeries(Chart chart)
    {
        chart.HideSeries(EastSeries);       // gone from the marks and from HitTest, still in the legend
        chart.FocusSeries(NorthSeries);     // every other series renders at theme.UnfocusedOpacity
    }

    // ── Data ──────────────────────────────────────────────────────────────────

    /// <summary>The two-series rows the event, button and legend charts share (3 categories each).</summary>
    private static List<DataRow> TwoSeriesRows() =>
    [
        Row("Mon", NorthSeries, 32.0), Row("Tue", NorthSeries, 41.0), Row("Wed", NorthSeries, 38.0),
        Row("Mon", SouthSeries, 24.0), Row("Tue", SouthSeries, 35.0), Row("Wed", SouthSeries, 29.0),
    ];

    /// <summary>One series only: the hand-built chart encodes no colour channel and draws no legend.</summary>
    private static List<DataRow> SingleSeriesRows() =>
    [
        Row("Mon", NorthSeries, 32.0), Row("Tue", NorthSeries, 41.0), Row("Wed", NorthSeries, 38.0),
        Row("Thu", NorthSeries, 51.0),
    ];

    /// <summary>Three series, so one can be hidden while the other two show the focus fade.</summary>
    private static List<DataRow> ThreeSeriesRows()
    {
        var rows = new List<DataRow>(12);
        string[] days = ["Mon", "Tue", "Wed", "Thu"];
        double[] north = [42.0, 51.0, 47.0, 63.0];
        double[] south = [28.0, 33.0, 39.0, 35.0];
        double[] east = [15.0, 19.0, 22.0, 18.0];
        for (int i = 0; i < days.Length; i++)
        {
            rows.Add(Row(days[i], NorthSeries, north[i]));
            rows.Add(Row(days[i], SouthSeries, south[i]));
            rows.Add(Row(days[i], EastSeries, east[i]));
        }
        return rows;
    }

    /// <summary>A row carrying the three fields every chart in this scene encodes.</summary>
    private static DataRow Row(string category, string series, double value)
        => new DataRow(3).Set("category", category).Set("series", series).Set("value", value);

    // ── Small helpers ─────────────────────────────────────────────────────────

    /// <summary>Put a message on the header's status label.</summary>
    private void Report(string message) => Status.Text = message;

    /// <summary>A nullable text field of an event payload, printed as "none" when it is null.</summary>
    private static string OrNone(string? value) => value ?? "none";

    /// <summary>A series key as it is written in the API calls this demo reports.</summary>
    private static string Quoted(string key) => "\"" + key + "\"";

    /// <summary>The category of a payload row (the events carry the whole <see cref="DataRow"/>).</summary>
    private static string RowText(DataRow? row) => row is null ? "no row" : $"category {row.Get("category")}";

    /// <summary>A boolean as the lowercase text the status line uses.</summary>
    private static string Bool(bool value) => value ? "true" : "false";
}
