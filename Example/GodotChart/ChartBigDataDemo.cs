using System.Collections.Generic;
using Godot;
using GodotNodeExtension.Component.GodotChart;

namespace GodotNodeExtension.Example.GodotChart;

/// <summary>
/// Real multi-series data page: the World Bank's annual population growth (<c>SP.POP.GROW</c>) for 24 major
/// countries, 1970-2023 — 1296 rows loaded from the CSV the page ships, one series per country.
/// <para>
/// It exists because the other pages answer "which mark does what" and "how expensive is a redraw", but not
/// "does the library stay usable on a chart a reader actually asks for": a wide data table, one colour per
/// series, a legend long enough to need filtering, and a time axis the reader wants to zoom into. The chart
/// itself is a plain <see cref="ChartView"/> node wired in the scene; this script only loads the CSV and
/// reports what came out of it.
/// </para>
/// <para>
/// The file carries the attribution in its comment lines (the parser skips them), and the country names are
/// comma-free on purpose: the inline CSV parser splits on <c>,</c>.
/// </para>
/// <para>
/// Keys, all of them <see cref="ChartView"/> exports that matter on a table this wide: <c>D</c>
/// <see cref="ChartView.Decimate"/> (the mark decides / off / on - with 1296 points over a few hundred pixels
/// the reduction is what keeps the frame cheap), <c>Z</c> <see cref="ChartView.ZoomFactor"/> (how far one wheel
/// step goes), <c>B</c> <see cref="ChartView.PanButton"/> (which button drags) and <c>X</c>
/// <see cref="ChartView.ResetZoomOnDoubleClick"/>. The readout names the current value of each.
/// </para>
/// </summary>
public partial class ChartBigDataDemo : Control
{
    /// <summary>The chart the CSV is loaded into (a <see cref="ChartView"/> node from the scene).</summary>
    [Export] public ChartView Chart { get; set; } = null!;

    /// <summary>On/off as the readout prints it.</summary>
    /// <param name="value">Flag to print.</param>
    private static string OnOff(bool value) => value ? "on" : "off";

    /// <summary>What was loaded and what the reader can do with it.</summary>
    [Export] public RichTextLabel Readout { get; set; } = null!;

    /// <summary>Data file shipped next to the page; comment lines carry the source and the licence.</summary>
    private const string CsvPath = "res://Example/GodotChart/Assets/worldbank_population_growth.csv";

    /// <summary>Decimation modes <c>D</c> cycles: the mark decides, off, on.</summary>
    private static readonly DecimateMode[] DecimateCycle = [DecimateMode.Auto, DecimateMode.Off, DecimateMode.On];

    /// <summary>Zoom factors <c>Z</c> cycles: how far one wheel step goes.</summary>
    private static readonly float[] ZoomFactorCycle = [1.1f, 1.5f, 2f];

    /// <summary>Pan buttons <c>B</c> cycles: which mouse button drags the window.</summary>
    private static readonly MouseButton[] PanButtonCycle = [MouseButton.Left, MouseButton.Middle, MouseButton.Right];

    private int _decimateIndex;
    private int _zoomFactorIndex;
    private int _panButtonIndex;

    private double _sinceReadout;

    /// <inheritdoc />
    public override void _Ready()
    {
        if (!FileAccess.FileExists(CsvPath))
        {
            Readout.Text = $"missing {CsvPath}";
            return;
        }

        string csv = FileAccess.GetFileAsString(CsvPath);
        Chart.SetCsv(csv);

        // Start from what the scene declares, so the first key press continues from there.
        _decimateIndex = System.Math.Max(0, System.Array.IndexOf(DecimateCycle, Chart.Decimate));
        _zoomFactorIndex = System.Math.Max(0, System.Array.IndexOf(ZoomFactorCycle, Chart.ZoomFactor));
        _panButtonIndex = System.Math.Max(0, System.Array.IndexOf(PanButtonCycle, Chart.PanButton));

        RefreshReadout();
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        // The frame rate is the one number that changes while the reader zooms and pans; refresh it a few
        // times a second instead of every frame.
        _sinceReadout += delta;
        if (_sinceReadout < 0.5) return;

        _sinceReadout = 0;
        RefreshReadout();
    }

    /// <inheritdoc />
    public override void _Input(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false } key) return;

        // Read here rather than in _UnhandledKeyInput: the example browser's tree consumes plain letters for
        // its type-ahead, so a shortcut handled later never arrives (the same reason ChartLayoutDemo reads its
        // keys in _Input).
        switch (key.Keycode)
        {
            case Key.D: CycleDecimate(); break;
            case Key.Z: CycleZoomFactor(); break;
            case Key.B: CyclePanButton(); break;
            case Key.X: ToggleResetOnDoubleClick(); break;
            default: return;
        }

        GetViewport().SetInputAsHandled();
        RefreshReadout();
    }

    /// <summary>Walk the decimation mode (<see cref="ChartView.Decimate"/>).</summary>
    private void CycleDecimate()
    {
        _decimateIndex = NextIndex(_decimateIndex, DecimateCycle.Length);
        Chart.Decimate = NextDecimate(_decimateIndex);
    }

    /// <summary>Walk the wheel's zoom factor (<see cref="ChartView.ZoomFactor"/>).</summary>
    private void CycleZoomFactor()
    {
        _zoomFactorIndex = NextIndex(_zoomFactorIndex, ZoomFactorCycle.Length);
        Chart.ZoomFactor = NextZoomFactor(_zoomFactorIndex);
    }

    /// <summary>Walk the button that drags (<see cref="ChartView.PanButton"/>).</summary>
    private void CyclePanButton()
    {
        _panButtonIndex = NextIndex(_panButtonIndex, PanButtonCycle.Length);
        Chart.PanButton = NextPanButton(_panButtonIndex);
    }

    /// <summary>Toggle whether a double click resets the zoom (<see cref="ChartView.ResetZoomOnDoubleClick"/>).</summary>
    private void ToggleResetOnDoubleClick()
        => Chart.ResetZoomOnDoubleClick = !Chart.ResetZoomOnDoubleClick;

    // ── The cycles themselves ────────────────────────────────────────────────
    // Pure functions, so the key handlers and the tests walk the same values (Test/GodotChart/ExampleDemoKeysTest).

    /// <summary>Next index of a cycle, wrapping at its end.</summary>
    /// <param name="index">Current index.</param>
    /// <param name="length">Entries in the cycle.</param>
    internal static int NextIndex(int index, int length) => length <= 0 ? 0 : (index + 1) % length;

    /// <summary>Decimation mode the next <c>D</c> press applies.</summary>
    /// <param name="index">Index the press moves to.</param>
    internal static DecimateMode NextDecimate(int index) => DecimateCycle[index % DecimateCycle.Length];

    /// <summary>Zoom factor the next <c>Z</c> press applies.</summary>
    /// <param name="index">Index the press moves to.</param>
    internal static float NextZoomFactor(int index) => ZoomFactorCycle[index % ZoomFactorCycle.Length];

    /// <summary>Pan button the next <c>B</c> press applies.</summary>
    /// <param name="index">Index the press moves to.</param>
    internal static MouseButton NextPanButton(int index) => PanButtonCycle[index % PanButtonCycle.Length];

    /// <summary>How many entries the decimation cycle has.</summary>
    internal static int DecimateCount => DecimateCycle.Length;

    /// <summary>How many entries the zoom-factor cycle has.</summary>
    internal static int ZoomFactorCount => ZoomFactorCycle.Length;

    /// <summary>How many entries the pan-button cycle has.</summary>
    internal static int PanButtonCount => PanButtonCycle.Length;

    private void RefreshReadout()
    {
        var rows = Chart.DataRows;
        var countries = new HashSet<string>();
        var years = new List<double>();

        foreach (var row in rows)
        {
            if (row.TryGet<string>("country", out var country)) countries.Add(country);
            if (row.TryGet("year", out double year)) years.Add(year);
        }

        years.Sort();

        Readout.Text =
            $"[b]{rows.Count:N0} rows[/b] · {countries.Count} countries · " +
            (years.Count > 0 ? $"years {years[0]:F0}-{years[^1]:F0}" : "no years") +
            $" · {Engine.GetFramesPerSecond():F0} fps · one series and one colour per country\n" +
            "[color=#93a1b5]wheel = zoom X at the pointer · drag = pan · double click = reset · " +
            "click a legend entry = hide/show that country[/color]\n" +
            $"[b]exports[/b]  Decimate {Chart.Decimate} (D) · ZoomFactor {Chart.ZoomFactor:F2} (Z) · " +
            $"PanButton {Chart.PanButton} (B) · ResetZoomOnDoubleClick {OnOff(Chart.ResetZoomOnDoubleClick)} (X)" +
            "\n[color=#93a1b5]Decimate is what keeps a 1296-point series cheap: with Off every point is mapped " +
            "and drawn, with Auto the mark reduces the points per pixel column, with On it reduces regardless " +
            "of the plot width. The other three decide how the reader moves the window.[/color]";
    }
}
