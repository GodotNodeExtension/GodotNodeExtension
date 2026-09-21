using System.Collections.Generic;
using System.Globalization;
using Godot;
using GodotNodeExtension.Component.GodotChart;

namespace GodotNodeExtension.Example.GodotChart;

/// <summary>
/// Layout budget page: one <see cref="ChartView"/> node on the left, the numbers it laid itself out with on
/// the right. Everything in the readout is read back from the public API - <see cref="Chart.MinimumSize"/>,
/// <see cref="Chart.MinimumPlotSize"/>, <see cref="Chart.CurrentPlotArea"/>, the chart's padding, the theme's
/// own reservations and the node's <see cref="Control.Size"/> - so the page cannot disagree with the chart.
/// <para>
/// The point is to watch the reservations <b>add up</b>. <see cref="Chart.MinimumSize"/> is the chart's own
/// estimate of what its content needs (title row, legend, X axis label row, axis title row, Y label column,
/// Y2 column, plus <see cref="Chart.MinimumPlotSize"/> for the plot itself), <see cref="ChartView"/> reports
/// it to the container through <c>_GetMinimumSize</c>, and <see cref="Chart.CurrentPlotArea"/> is what the
/// last frame actually got. The four insets between the node rect and that plot rectangle are the budget:
/// <c>left = plot.X</c>, <c>right = size.X - (plot.X + plot.W)</c>, and the same vertically.
/// </para>
/// <para>
/// Keys: <c>T</c> title on/off, <c>L</c> legend Top → Bottom → Left → Right → None, <c>A</c> X/Y axis titles
/// on/off, <c>Y</c> second Y axis on/off (built through <see cref="ChartView.ConfigureChart"/>, since the node
/// has no Y2 export - what moves is the reservation, not the data), <c>F</c> the theme's font sizes (small /
/// default / large) and <c>M</c> pins the node to <see cref="Chart.MinimumSize"/>, then to 0.85 / 0.70 / 0.55
/// of it, then lets it fill its cell again. Below the minimum the axis thins its own labels and the chart
/// prints one warning to the editor Output; the readout says where the node sits.
/// </para>
/// <para>
/// The second half of the keys drives the axis and content <b>exports</b> - the knobs a page sets in the scene
/// or from code, each one visible in the readout next to the reservation it moves: <c>R</c>
/// <see cref="ChartView.XAxisLabelRotation"/> (0° → 30° → 60°), <c>S</c> the tick density
/// (<see cref="ChartView.XAxisTickStep"/> / <see cref="ChartView.XAxisTickSpacing"/> /
/// <see cref="ChartView.XAxisTickCount"/>, one mode each), <c>N</c> the label formats
/// (<see cref="ChartView.XAxisLabelFormat"/> and <see cref="ChartView.YAxisLabelFormat"/> take the same one -
/// one code labels both axes), <c>P</c> <see cref="ChartView.PlotAspectRatio"/> (fill / square / wide), <c>V</c>
/// <see cref="ChartView.PlotAlignVertical"/>, and <c>W</c> the pinned Y ends
/// (<see cref="ChartView.YAxisMinLimit"/> / <see cref="ChartView.YAxisMaxLimit"/>). A rotated label occupies
/// less of the axis and a pinned end stops the axis refitting to the data: both move the numbers above.
/// </para>
/// <para>
/// The readout keeps two kinds of comparison, both measured and never estimated: the <b>Δ</b> line holds the
/// difference between the two states around the last key (both read after a render), and the hint compares
/// <b>legend positions</b> that were already visited in the same content state - the page records the minimum
/// of every state it sees, so "Top is cheaper than Left here" is a measurement, not an assumption.
/// </para>
/// </summary>
public partial class ChartLayoutDemo : Control
{
    /// <summary>The chart this page measures (a <see cref="ChartView"/> node wired in the scene).</summary>
    [Export] public ChartView View { get; set; } = null!;

    /// <summary>The whole readout, assembled from the public API on every refresh.</summary>
    [Export] public RichTextLabel Readout { get; set; } = null!;

    /// <summary>Font scales <c>F</c> cycles: small / default / large, applied to the page's own theme.</summary>
    private static readonly float[] FontScales = [0.8f, 1.0f, 1.3f];

    /// <summary>Names of <see cref="FontScales"/>, in the same order.</summary>
    private static readonly string[] FontNames = ["small", "default", "large"];

    /// <summary>
    /// Sizes <c>M</c> cycles, as a multiple of <see cref="Chart.MinimumSize"/>. Index 0 means "the layout
    /// decides" (the node fills its cell); the others pin the node to the minimum and then below it, which is
    /// what makes the too-small warning reachable.
    /// </summary>
    private static readonly float[] ShrinkFactors = [float.NaN, 1f, 0.85f, 0.7f, 0.55f];

    /// <summary>Legend positions <c>L</c> walks, in the order the widths are worth comparing.</summary>
    private static readonly LegendPosition[] LegendCycle =
    [
        LegendPosition.Top, LegendPosition.Bottom, LegendPosition.Left, LegendPosition.Right, LegendPosition.None,
    ];

    /// <summary>
    /// Label rotations <c>R</c> cycles. A rotated label occupies less of the axis horizontally, so the axis
    /// keeps more of them before its stride starts skipping - the readout's label column moves with it.
    /// </summary>
    private static readonly float[] RotationCycle = [0f, 30f, 60f];

    /// <summary>
    /// Tick densities <c>S</c> cycles. The three exports are alternatives - a step in data units, the pixels one
    /// label may take, an exact count - and the first entry leaves all three at <c>0</c> ("the axis decides").
    /// </summary>
    private static readonly (string Name, float Step, float Spacing, int Count)[] TickModes =
    [
        ("automatic", 0f, 0f, 0),
        ("step 500", 500f, 0f, 0),
        ("spacing 40px", 0f, 40f, 0),
        ("count 12", 0f, 0f, 12),
    ];

    /// <summary>Label formats <c>N</c> cycles; an empty one keeps the scale's own text.</summary>
    private static readonly string[] LabelFormats = ["", "0.0 °C", "# N0"];

    /// <summary>Content shapes <c>P</c> cycles: the cell's own shape, a square, a wide box.</summary>
    private static readonly float[] AspectCycle = [0f, 1f, 1.6f];

    /// <summary>Vertical alignment <c>V</c> cycles - where the content box sits in the plot rectangle.</summary>
    private static readonly VerticalAlignment[] AlignCycle =
    [
        VerticalAlignment.Top, VerticalAlignment.Center, VerticalAlignment.Bottom,
    ];

    /// <summary>
    /// The Y-limit modes <c>W</c> cycles: both ends fitted, then one end pinned, then both. The pinned values are
    /// the data's own range, so a pinned end is visible (the bars reach the axis edge) instead of a number
    /// nobody can relate to.
    /// </summary>
    private static readonly (string Name, bool PinMin, bool PinMax)[] LimitModes =
    [
        ("fit both ends", false, false),
        ("pin the lower end", true, false),
        ("pin the upper end", false, true),
        ("pin both ends", true, true),
    ];

    /// <summary>Seconds between two readout refreshes (the numbers only move on a key or on a resize).</summary>
    private const double ReadoutInterval = 0.25;

    /// <summary>Frames waited after a key before the change is read back (the new state has to be drawn first).</summary>
    private const int SettleFrames = 3;

    /// <summary>Shown when there is no chart to measure - a headless run builds no canvas, so no Chart.</summary>
    private const string NoChartText =
        "[b]no canvas[/b] · this page measures a real chart, and without a rendering device (a headless run) " +
        "ChartView never builds its Chart, so there is nothing to read. Open the scene in Godot to see the " +
        "numbers - the keys still work, they only change state here.";

    /// <summary>The page's theme: <c>F</c> moves three of its font sizes and the chart follows the resource.</summary>
    private ChartTheme _theme = null!;

    private float _baseLabelFontSize;
    private float _baseTitleFontSize;
    private float _baseTooltipFontSize;

    // ── State the keys move ──────────────────────────────────────────────────

    private int _fontIndex = 1;         // default
    private int _shrinkIndex;           // 0 = the layout decides
    private int _legendIndex;           // the scene's legend position
    private int _rotationIndex;         // 0 = the scene's rotation
    private int _tickModeIndex;         // 0 = the axis decides
    private int _formatIndex;           // 0 = the scale's own text
    private int _aspectIndex;           // 0 = the cell's own shape
    private int _alignIndex;            // the scene's vertical alignment
    private int _limitIndex;            // 0 = both Y ends fitted
    private bool _titleOn;
    private bool _axisTitlesOn = true;
    private bool _y2On;

    /// <summary>The scene's title and axis titles, kept so the toggles can put them back.</summary>
    private string _title = string.Empty;
    private string _xAxisTitle = string.Empty;
    private string _yAxisTitle = string.Empty;

    /// <summary>Field name the node falls back to when the scene leaves <see cref="ChartView.YField"/> empty.</summary>
    private const string FallbackValueField = "value";

    // ── The layout the scene declares, kept so M can restore it ──────────────

    private Vector2 _sceneMinimum;
    private SizeFlags _sceneFlagsHorizontal;
    private SizeFlags _sceneFlagsVertical;
    private bool _sceneIgnoreMinimum;

    // ── Measurements ─────────────────────────────────────────────────────────

    /// <summary>One frame's worth of layout numbers, as the public API reports them.</summary>
    private readonly record struct Sample(Vector2 Size, Vector2 Minimum, Rect2 Plot);

    /// <summary>The last sample a render produced (null until the first frame has drawn).</summary>
    private Sample? _latest;

    /// <summary>
    /// The minimum measured for every (content state, legend position) pair the page has seen. Two positions
    /// are only compared while the rest of the state matches, which is what keeps the hint's numbers honest.
    /// </summary>
    private readonly Dictionary<(string State, LegendPosition Legend), Vector2> _legendSamples = [];

    private Sample? _beforeSample;
    private string _beforeState = string.Empty;
    private string _changeKey = string.Empty;
    private bool _captureBaseline;
    private int _framesSinceChange = -1;
    private string _changeText = string.Empty;

    private double _sinceReadout;

    // ── Setup ────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public override void _Ready()
    {
        // The layout the scene declares is what M restores, so it is read before anything is pinned.
        _sceneMinimum = View.CustomMinimumSize;
        _sceneFlagsHorizontal = View.SizeFlagsHorizontal;
        _sceneFlagsVertical = View.SizeFlagsVertical;
        _sceneIgnoreMinimum = View.IgnoreContentMinimumSize;

        _title = View.Title;
        _titleOn = !string.IsNullOrEmpty(_title);
        _xAxisTitle = View.XAxisTitle;
        _yAxisTitle = View.YAxisTitle;
        _axisTitlesOn = !string.IsNullOrEmpty(_xAxisTitle) || !string.IsNullOrEmpty(_yAxisTitle);
        _legendIndex = System.Array.IndexOf(LegendCycle, View.Legend);

        // The page owns the theme so F has three sizes to move, and it reads the sizes the built-in theme
        // ships with instead of hard-coding them here.
        _theme = ChartTheme.Dark();
        _baseLabelFontSize = _theme.LabelFontSize;
        _baseTitleFontSize = _theme.TitleFontSize;
        _baseTooltipFontSize = _theme.TooltipFontSize;
        ApplyFontScale();
        View.CustomTheme = _theme;

        _rotationIndex = System.Math.Max(0, System.Array.IndexOf(RotationCycle, View.XAxisLabelRotation));
        _aspectIndex = System.Math.Max(0, System.Array.IndexOf(AspectCycle, View.PlotAspectRatio));
        _alignIndex = System.Math.Max(0, System.Array.IndexOf(AlignCycle, View.PlotAlignVertical));

        View.ConfigureChart = ApplySecondAxis;
        RefreshReadout();
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        if (_captureBaseline)
        {
            // The key was handled this frame and the render of the new state has not happened yet, so the
            // sample held here is still the state the key is about to change.
            _beforeSample = _latest;
            _captureBaseline = false;
            _framesSinceChange = 0;
        }

        if (_shrinkIndex > 0) ApplyShrink();

        if (Measure() is { } now) _latest = now;

        if (_framesSinceChange >= 0)
        {
            _framesSinceChange++;
            if (_framesSinceChange >= SettleFrames)
            {
                _framesSinceChange = -1;
                if (_beforeSample is { } before && _latest is { } after)
                {
                    _changeText = DeltaText(_changeKey, _beforeState, before, after);
                    RefreshReadout();   // the numbers and the difference that explains them arrive together
                }
            }
        }

        _sinceReadout += delta;
        if (_sinceReadout < ReadoutInterval) return;

        _sinceReadout = 0;
        RefreshReadout();
    }

    /// <inheritdoc />
    public override void _Input(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false } key) return;

        // _Input runs before the GUI: the example browser puts keyboard focus on its tree, and the tree
        // consumes plain letters for its type-ahead, so a shortcut handled in _UnhandledKeyInput would never
        // arrive (the same reason ChartLayeredRenderingDemo reads its keys here).
        string action = key.Keycode switch
        {
            Key.T => "T title",
            Key.L => "L legend",
            Key.A => "A axis titles",
            Key.Y => "Y second Y axis",
            Key.F => "F font size",
            Key.M => "M size",
            Key.R => "R label rotation",
            Key.S => "S tick density",
            Key.N => "N label format",
            Key.P => "P content shape",
            Key.V => "V vertical alignment",
            Key.W => "W pinned Y ends",
            _ => string.Empty,
        };

        if (action.Length == 0) return;

        // The state the key is about to leave, labelled while it is still in place.
        _beforeState = StateLabel();
        _changeKey = action;

        switch (key.Keycode)
        {
            case Key.T: ToggleTitle(); break;
            case Key.L: CycleLegend(); break;
            case Key.A: ToggleAxisTitles(); break;
            case Key.Y: ToggleSecondAxis(); break;
            case Key.F: CycleFontSize(); break;
            case Key.R: CycleRotation(); break;
            case Key.S: CycleTickMode(); break;
            case Key.N: CycleLabelFormat(); break;
            case Key.P: CycleAspect(); break;
            case Key.V: CycleVerticalAlignment(); break;
            case Key.W: CycleLimits(); break;
            default: CycleShrink(); break;
        }

        _captureBaseline = true;
        // Handled here, so the GUI must not also act on it: _Input runs before the GUI, and the example
        // browser's tree reads a plain letter as type-ahead (see the note above the action table).
        GetViewport().SetInputAsHandled();
        // No readout refresh here: the numbers of the *old* state would be recorded under the new state's key
        // (the legend samples in particular). The refresh a few frames later carries both, and the Δ line
        // explains the change.
    }

    // ── The keys ─────────────────────────────────────────────────────────────

    /// <summary>Show or hide the chart title (<see cref="ChartView.Title"/>).</summary>
    private void ToggleTitle()
    {
        _titleOn = !_titleOn;
        View.Title = _titleOn ? _title : string.Empty;
    }

    /// <summary>Walk the legend around the plot (<see cref="ChartView.Legend"/>).</summary>
    private void CycleLegend()
    {
        _legendIndex = (_legendIndex + 1) % LegendCycle.Length;
        View.Legend = LegendCycle[_legendIndex];
    }

    /// <summary>Show or hide both axis titles (the chart's own exports).</summary>
    private void ToggleAxisTitles()
    {
        _axisTitlesOn = !_axisTitlesOn;
        View.XAxisTitle = _axisTitlesOn ? _xAxisTitle : string.Empty;
        View.YAxisTitle = _axisTitlesOn ? _yAxisTitle : string.Empty;
    }

    /// <summary>
    /// Add or remove the second Y axis. The node has no Y2 export, so the page builds that chain itself in
    /// <see cref="ChartView.ConfigureChart"/> - the escape hatch ChartViewFieldsDemo documents - and asks for a
    /// rebuild (<see cref="ChartView.Refresh"/>) because the hook only runs while a chart is built. No mark
    /// reads the channel, so the drawing does not change: the right column's reservation does.
    /// </summary>
    private void ToggleSecondAxis()
    {
        _y2On = !_y2On;
        View.Refresh();
    }

    /// <summary>Cycle the theme's font sizes (small / default / large).</summary>
    private void CycleFontSize()
    {
        _fontIndex = (_fontIndex + 1) % FontScales.Length;
        ApplyFontScale();
    }

    /// <summary>Pin the node to the minimum, then below it, then let the layout decide again.</summary>
    private void CycleShrink()
    {
        _shrinkIndex = (_shrinkIndex + 1) % ShrinkFactors.Length;
        ApplyShrink();
    }

    /// <summary>Walk the label rotation (<see cref="ChartView.XAxisLabelRotation"/>).</summary>
    private void CycleRotation()
    {
        _rotationIndex = NextIndex(_rotationIndex, RotationCycle.Length);
        View.XAxisLabelRotation = NextRotation(_rotationIndex);
    }

    /// <summary>Walk the tick density: the three exports that ask for one, one mode at a time.</summary>
    private void CycleTickMode()
    {
        _tickModeIndex = NextIndex(_tickModeIndex, TickModes.Length);
        (string _, float step, float spacing, int count) = NextTickMode(_tickModeIndex);
        View.XAxisTickStep = step;
        View.XAxisTickSpacing = spacing;
        View.XAxisTickCount = count;
    }

    /// <summary>Walk the label format, applied to both axes (they are labelled by the same code).</summary>
    private void CycleLabelFormat()
    {
        _formatIndex = NextIndex(_formatIndex, LabelFormats.Length);
        string format = NextLabelFormat(_formatIndex);
        View.XAxisLabelFormat = format;
        View.YAxisLabelFormat = format;
    }

    /// <summary>Walk the content shape (<see cref="ChartView.PlotAspectRatio"/>).</summary>
    private void CycleAspect()
    {
        _aspectIndex = NextIndex(_aspectIndex, AspectCycle.Length);
        View.PlotAspectRatio = NextAspect(_aspectIndex);
    }

    /// <summary>Walk the vertical alignment (<see cref="ChartView.PlotAlignVertical"/>).</summary>
    private void CycleVerticalAlignment()
    {
        _alignIndex = NextIndex(_alignIndex, AlignCycle.Length);
        View.PlotAlignVertical = NextAlignment(_alignIndex);
    }

    /// <summary>Walk the pinned Y ends (<see cref="ChartView.YAxisMinLimit"/> / <see cref="ChartView.YAxisMaxLimit"/>).</summary>
    private void CycleLimits()
    {
        _limitIndex = NextIndex(_limitIndex, LimitModes.Length);
        ApplyLimits();
    }

    // ── The cycles themselves ────────────────────────────────────────────────
    // Pure functions so the key handlers and the tests walk the same values (Test/GodotChart/ExampleDemoKeysTest).

    /// <summary>Next index of a cycle, wrapping at its end.</summary>
    /// <param name="index">Current index.</param>
    /// <param name="length">Entries in the cycle.</param>
    internal static int NextIndex(int index, int length) => length <= 0 ? 0 : (index + 1) % length;

    /// <summary>Rotation the next <c>R</c> press applies.</summary>
    /// <param name="index">Index the press moves to.</param>
    internal static float NextRotation(int index) => RotationCycle[index % RotationCycle.Length];

    /// <summary>The tick exports the next <c>S</c> press applies.</summary>
    /// <param name="index">Index the press moves to.</param>
    internal static (string Name, float Step, float Spacing, int Count) NextTickMode(int index)
        => TickModes[index % TickModes.Length];

    /// <summary>The format the next <c>N</c> press applies to both axes.</summary>
    /// <param name="index">Index the press moves to.</param>
    internal static string NextLabelFormat(int index) => LabelFormats[index % LabelFormats.Length];

    /// <summary>The content shape the next <c>P</c> press applies.</summary>
    /// <param name="index">Index the press moves to.</param>
    internal static float NextAspect(int index) => AspectCycle[index % AspectCycle.Length];

    /// <summary>The alignment the next <c>V</c> press applies.</summary>
    /// <param name="index">Index the press moves to.</param>
    internal static VerticalAlignment NextAlignment(int index) => AlignCycle[index % AlignCycle.Length];

    /// <summary>The Y-limit mode the next <c>W</c> press applies.</summary>
    /// <param name="index">Index the press moves to.</param>
    internal static (string Name, bool PinMin, bool PinMax) NextLimitMode(int index)
        => LimitModes[index % LimitModes.Length];

    /// <summary>How many entries the rotation cycle has (the tests walk it).</summary>
    internal static int RotationCount => RotationCycle.Length;

    /// <summary>How many entries the tick-density cycle has.</summary>
    internal static int TickModeCount => TickModes.Length;

    /// <summary>How many entries the label-format cycle has.</summary>
    internal static int LabelFormatCount => LabelFormats.Length;

    /// <summary>How many entries the content-shape cycle has.</summary>
    internal static int AspectCount => AspectCycle.Length;

    /// <summary>How many entries the alignment cycle has.</summary>
    internal static int AlignmentCount => AlignCycle.Length;

    /// <summary>How many entries the Y-limit cycle has.</summary>
    internal static int LimitModeCount => LimitModes.Length;

    /// <summary>
    /// Pin the Y ends the current <c>W</c> mode asks for. The values come from the data, so a pinned end is
    /// visible: with both ends pinned the axis stops refitting and the bars reach its edges.
    /// </summary>
    private void ApplyLimits()
    {
        (string _, bool pinMin, bool pinMax) = NextLimitMode(_limitIndex);
        (float min, float max) = DataRange();

        View.YAxisMinLimit = pinMin ? min : float.NaN;
        View.YAxisMaxLimit = pinMax ? max : float.NaN;
    }

    /// <summary>The value range of the rows the chart holds (read back through the public API).</summary>
    private (float Min, float Max) DataRange()
    {
        string field = string.IsNullOrEmpty(View.YField) ? FallbackValueField : View.YField;
        float min = float.MaxValue;
        float max = float.MinValue;

        foreach (var row in View.Chart?.GetRenderDataSnapshot() ?? [])
        {
            if (!row.TryGet(field, out double value)) continue;
            min = Mathf.Min(min, (float)value);
            max = Mathf.Max(max, (float)value);
        }

        return min > max ? (0f, 0f) : (min, max);
    }

    // ── What the keys change on the chart ────────────────────────────────────

    /// <summary>Scale the three font sizes of the page's theme by the current step.</summary>
    private void ApplyFontScale()
    {
        float scale = FontScales[_fontIndex];
        _theme.LabelFontSize = _baseLabelFontSize * scale;
        _theme.TitleFontSize = _baseTitleFontSize * scale;
        _theme.TooltipFontSize = _baseTooltipFontSize * scale;
    }

    /// <summary>
    /// The Y2 chain, applied after the node configured the chart. The hook runs on every rebuild, so the axis
    /// survives a refresh and a theme change. It carries no title on this page: the reservation the page is
    /// about is then exactly <see cref="ChartTheme.Y2LabelReservedWidth"/> plus whatever its labels measure.
    /// </summary>
    private void ApplySecondAxis(Chart chart)
    {
        if (!_y2On) return;

        // The field Y already reads is enough: the axis needs values to label, not a series of its own (no
        // mark of this page draws against Channel.Y2).
        string field = string.IsNullOrEmpty(View.YField) ? FallbackValueField : View.YField;
        chart.Encode(Channel.Y2, field);
        chart.Y2Axis(new AxisConfig { Unit = View.YAxisUnit });
    }

    /// <summary>
    /// Put the node at a multiple of <see cref="Chart.MinimumSize"/> (or back into the layout's hands). The
    /// chart's own minimum would keep the node from ever getting there, so the switch that reports it is turned
    /// off and the size is pinned instead - this is what <see cref="ChartView.IgnoreContentMinimumSize"/> is
    /// for.
    /// </summary>
    private void ApplyShrink()
    {
        if (View.Chart is not { } chart) return;    // no device: nothing to size against

        if (_shrinkIndex == 0)
        {
            View.IgnoreContentMinimumSize = _sceneIgnoreMinimum;
            View.CustomMinimumSize = _sceneMinimum;
            View.SizeFlagsHorizontal = _sceneFlagsHorizontal;
            View.SizeFlagsVertical = _sceneFlagsVertical;
            return;
        }

        Vector2 minimum = chart.MinimumSize;
        if (minimum.X <= 0f || minimum.Y <= 0f) return;     // the first layout has not been computed yet

        // The wanted size moves with the content (a title or a font size changes the minimum), so it is
        // recomputed here and not only when the key is pressed.
        Vector2 wanted = (minimum * ShrinkFactors[_shrinkIndex]).Round();
        if (View.IgnoreContentMinimumSize && View.CustomMinimumSize.IsEqualApprox(wanted)) return;

        View.IgnoreContentMinimumSize = true;
        View.CustomMinimumSize = wanted;
        // ShrinkCenter (not Fill): the container then hands the node exactly its minimum size instead of
        // stretching it, which is what makes the pinned size visible on screen.
        View.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        View.SizeFlagsVertical = SizeFlags.ShrinkCenter;
    }

    // ── Reading the layout back ──────────────────────────────────────────────

    /// <summary>
    /// The public numbers of the frame that just drew, or null while the chart has not been laid out yet (a
    /// rebuild replaces the <see cref="Chart"/> instance, and a fresh one has no plot area until it draws).
    /// </summary>
    private Sample? Measure()
    {
        if (View.Chart is not { } chart || chart.CurrentPlotArea is not { } plot) return null;

        return new Sample(
            View.Size,
            chart.MinimumSize,
            new Rect2(plot.X, plot.Y, plot.Width, plot.Height));
    }

    /// <summary>The content state the legend samples are keyed by (the pin and the legend itself are left out).</summary>
    private string ContentState()
    {
        return $"{(_titleOn ? "title" : "no title")} + {(_axisTitlesOn ? "axis titles" : "no axis titles")} + " +
               $"{(_y2On ? "Y2" : "no Y2")} + fonts {FontNames[_fontIndex]} + {KnobsLabel()}";
    }

    /// <summary>The state as the readout and the Δ line describe it.</summary>
    private string StateLabel()
    {
        return $"title {OnOff(_titleOn)} · legend {LegendCycle[_legendIndex]} · axis titles {OnOff(_axisTitlesOn)} · " +
               $"Y2 {OnOff(_y2On)} · fonts {FontNames[_fontIndex]} · {ShrinkLabel()} · {KnobsLabel()}";
    }

    /// <summary>
    /// The export knobs of this page in one line, for the readout's state line and for the key the legend
    /// samples are stored under: the minimum size moves with the label rotation, the tick density and the label
    /// format, so two samples are only comparable while those match as well.
    /// </summary>
    private string KnobsLabel()
    {
        (string ticks, float _, float _, int _) = TickModes[_tickModeIndex];
        (string limits, bool _, bool _) = LimitModes[_limitIndex];
        string format = LabelFormats[_formatIndex].Length > 0 ? LabelFormats[_formatIndex] : "scale's own";
        return $"rotation {RotationCycle[_rotationIndex]:F0}° · ticks {ticks} · format {format} · " +
               $"shape {AspectLabel()} · align {AlignCycle[_alignIndex]} · Y ends {limits}";
    }

    /// <summary>The content shape as the readout names it (0 = the cell's own shape).</summary>
    private string AspectLabel()
        => AspectCycle[_aspectIndex] <= 0f ? "fill the cell" : $"{AspectCycle[_aspectIndex]:F1} : 1";

    /// <summary>The tick mode as the readout names it.</summary>
    private string TickName() => TickModes[_tickModeIndex].Name;

    /// <summary>The Y-limit mode as the readout names it.</summary>
    private string LimitName() => LimitModes[_limitIndex].Name;

    /// <summary>A numeric export as the readout prints it: <c>0</c> is the "let the axis decide" value.</summary>
    private static string Number(float value)
        => value > 0f ? value.ToString("G4", CultureInfo.InvariantCulture) : "auto (0)";

    /// <summary>Where the node's size comes from, in the words of the M key.</summary>
    private string ShrinkLabel()
    {
        return _shrinkIndex switch
        {
            0 => "size from the layout",
            1 => "size = Chart.MinimumSize",
            _ => $"size = Chart.MinimumSize × {ShrinkFactors[_shrinkIndex]:F2}",
        };
    }

    /// <summary>Record the frame's minimum for the current state, so the hint can compare legend positions.</summary>
    private void RecordLegendSample(Sample sample, LegendPosition legend)
        => _legendSamples[(ContentState(), legend)] = sample.Minimum;

    /// <summary>
    /// What the legend positions visited in this content state measured, cheapest width first. The current
    /// position always takes <paramref name="currentMinimum"/> (the frame's own reading) rather than whatever
    /// the dictionary holds for it, so the list is complete even on the first frame after a move.
    /// </summary>
    private List<(LegendPosition Legend, Vector2 Minimum)> LegendComparisons(Vector2 currentMinimum)
    {
        LegendPosition current = LegendCycle[_legendIndex];
        var found = new List<(LegendPosition Legend, Vector2 Minimum)>();

        foreach (LegendPosition position in LegendCycle)
        {
            if (position == current)
                found.Add((current, currentMinimum));
            else if (_legendSamples.TryGetValue((ContentState(), position), out Vector2 minimum))
                found.Add((position, minimum));
        }

        found.Sort((a, b) => a.Minimum.X.CompareTo(b.Minimum.X));
        return found;
    }

    /// <summary>The four insets of one sample: the padding the chart, the title, the legend and the axis left.</summary>
    private static (float Left, float Right, float Top, float Bottom) Insets(Sample sample)
    {
        return (
            sample.Plot.Position.X,
            sample.Size.X - sample.Plot.End.X,
            sample.Plot.Position.Y,
            sample.Size.Y - sample.Plot.End.Y);
    }

    // ── Readout ──────────────────────────────────────────────────────────────

    /// <summary>Rebuild the whole readout from the last sample.</summary>
    private void RefreshReadout()
    {
        if (View.Chart is not { } chart)
        {
            Readout.Text = NoChartText;
            return;
        }

        if (_latest is not { } sample)
        {
            Readout.Text = "measuring: the chart is built, its first layout has not been drawn yet";
            return;
        }

        // Only a settled state is worth remembering: right after a key the sample still belongs to the state
        // the key just left, and recording it under the new legend position would poison the comparison.
        if (!_captureBaseline && _framesSinceChange < 0) RecordLegendSample(sample, LegendCycle[_legendIndex]);

        Rect2 plot = sample.Plot;
        Vector2 size = sample.Size;
        Vector2 minimum = sample.Minimum;

        (float left, float right, float top, float bottom) = Insets(sample);

        // A node below the minimum can leave the plot rectangle inside out (the reserves alone are taller than
        // the node), and "W 38 × H -8" is not an area: the budget is clamped there and said out loud below.
        float plotArea = Mathf.Max(0f, plot.Size.X) * Mathf.Max(0f, plot.Size.Y);
        float nodeArea = size.X * size.Y;
        float ratio = nodeArea > 0f ? 100f * plotArea / nodeArea : 0f;

        var lines = new List<string>
        {
            $"[b]{View.Kind} chart, {RowSummary()}[/b] · {StateLabel()}",
            "",
            "[b]1 · the minimum the content asks for[/b]",
            $"Chart.MinimumSize  {Px(minimum.X)} × {Px(minimum.Y)}",
            $"Chart.MinimumPlotSize  {Px(Chart.MinimumPlotSize.X)} × {Px(Chart.MinimumPlotSize.Y)} " +
            "(static: the plot rectangle a minimum keeps)",
            "minimum − plot = " + Px(minimum.X - Chart.MinimumPlotSize.X) + " × " +
            Px(minimum.Y - Chart.MinimumPlotSize.Y) + " of decorations in total, of which the chart's own " +
            "padding is " + Px(chart.PaddingLeft + chart.PaddingRight) + " × " +
            Px(chart.PaddingTop + chart.PaddingBottom) + " (Chart.PaddingLeft/Right and PaddingTop/Bottom)",
            $"the theme reserves {Px(_theme.TitleReservedHeight)} for a title row " +
            $"(ChartTheme.TitleReservedHeight, {OnOff(_titleOn)}) and {Px(_theme.Y2LabelReservedWidth)} for a Y2 " +
            $"column (ChartTheme.Y2LabelReservedWidth, {OnOff(_y2On)}); the legend, the axis label column and " +
            "the axis title rows are measured by the chart itself and are inside the rest",
            "",
            "[b]2 · the node the layout gave us[/b]",
            $"Size  {Px(size.X)} × {Px(size.Y)}   " + SlackText(size, minimum) + $"   · {ShrinkLabel()}",
            "",
            "[b]3 · the plot rectangle that came out[/b] (Chart.CurrentPlotArea)",
            $"X {plot.Position.X:F0}   Y {plot.Position.Y:F0}   W {plot.Size.X:F0}   H {plot.Size.Y:F0}",
            $"insets  left {Px(left)} · right {Px(right)} · top {Px(top)} · bottom {Px(bottom)}  " +
            "(left = plot.X, right = size.X − (plot.X + plot.W), top and bottom the same way)",
            $"of which the chart's padding  left {Px(chart.PaddingLeft)} · right {Px(chart.PaddingRight)} · " +
            $"top {Px(chart.PaddingTop)} · bottom {Px(chart.PaddingBottom)}  → the rest is the title, the " +
            "legend, the axis labels and the axis titles",
            "",
            "[b]4 · how much of the node the plot got[/b]",
            $"plot {Px(plot.Size.X)} × {Px(plot.Size.Y)} = {plotArea:N0} px² of the node {Px(size.X)} × " +
            $"{Px(size.Y)} = {nodeArea:N0} px²  →  {ratio:F1} %",
            $"the plot uses {Part(plot.Size.X, size.X)} of the node's width and {Part(plot.Size.Y, size.Y)} of its height",
            DegenerateText(plot),
            BudgetHint(chart, sample, left, right, top, bottom, ratio),
            "",
            "[b]5 · the export knobs this page drives[/b] (ChartView exports, set from code here)",
            $"XAxisLabelRotation  {View.XAxisLabelRotation:F0}°   ·   tick density {TickName()}   " +
            $"(XAxisTickStep {Number(View.XAxisTickStep)} · XAxisTickSpacing {Number(View.XAxisTickSpacing)} · " +
            $"XAxisTickCount {View.XAxisTickCount})",
            $"XAxisLabelFormat  {Quote(View.XAxisLabelFormat)}   ·   YAxisLabelFormat  {Quote(View.YAxisLabelFormat)}" +
            "   (one format reaches both axes: one code labels them)",
            $"PlotAspectRatio  {AspectLabel()}   ·   PlotAlignVertical {AlignCycle[_alignIndex]}   " +
            "→ the box the content is shaped into, and where it sits in the plot rectangle",
            $"YAxisMinLimit / YAxisMaxLimit  {LimitName()}   " +
            "(NaN fits that end; a pinned end stops the axis refitting to the data)",
            "",
            "[b]last change[/b]",
            _changeText.Length > 0
                ? _changeText
                : "[color=#93a1b5]press a key: the state above changes at once, and the difference it made is " +
                  "measured here once the new layout has been drawn (a few frames later)[/color]",
        };

        Readout.Text = string.Join("\n", lines);
    }

    /// <summary>
    /// The slack between the node and the minimum, or how far below it the node is. The minimum is an estimate
    /// and rarely a whole number of pixels, so a shortfall is reported with one decimal - being pinned exactly
    /// to <see cref="Chart.MinimumSize"/> can still short one axis by a fraction.
    /// </summary>
    private static string SlackText(Vector2 size, Vector2 minimum)
    {
        if (size.X >= minimum.X && size.Y >= minimum.Y)
        {
            return $"[color=#7fd28a]slack  {Px(size.X - minimum.X)} × {Px(size.Y - minimum.Y)} over the " +
                   "minimum[/color]";
        }

        float shortX = Mathf.Max(0f, minimum.X - size.X);
        float shortY = Mathf.Max(0f, minimum.Y - size.Y);
        string shortfall = shortX <= 0f
            ? $"in height by {Px1(shortY)}"
            : shortY <= 0f
                ? $"in width by {Px1(shortX)}"
                : $"by {Px1(shortX)} × {Px1(shortY)}";
        string fraction = shortX < 1f || shortY < 1f
            ? " (the estimate is fractional and the surface is whole pixels, so the chart can still warn " +
              "about one label)"
            : string.Empty;

        return $"[color=#e8a33d]below the minimum {shortfall}{fraction}[/color]";
    }

    /// <summary>
    /// Say it out loud when the plot rectangle came out inside out: below the minimum the reserves alone can be
    /// taller (or wider) than the node, and a negative rectangle is not an area the budget can be a share of.
    /// </summary>
    private static string DegenerateText(Rect2 plot)
    {
        if (plot.Size.X > 0f && plot.Size.Y > 0f) return string.Empty;

        return "[color=#e8a33d]the reserves no longer fit: the plot rectangle came out " +
               $"{plot.Size.X:F0} × {plot.Size.Y:F0} px" +
               (plot.Size.Y <= 0f ? " (thinner than nothing in height)" : " (thinner than nothing in width)") +
               ", so this is past the point where the minimum still describes the layout.[/color]";
    }

    /// <summary>
    /// The number-driven advice: what the widest reservation costs, and - once another legend position has been
    /// measured in the same content state - what that position would save or spend.
    /// </summary>
    private string BudgetHint(Chart chart, Sample sample, float left, float right, float top, float bottom,
        float ratio)
    {
        Vector2 size = sample.Size;
        Vector2 minimum = sample.Minimum;
        LegendPosition legend = LegendCycle[_legendIndex];

        if (size.X < minimum.X - 0.5f || size.Y < minimum.Y - 0.5f)
        {
            return $"[color=#e8a33d]the node is pinned below the minimum ({Part(size.X, minimum.X)} × " +
                   $"{Part(size.Y, minimum.Y)} of it): the axis thins its own labels to keep the plot usable, " +
                   "and the chart printed one warning to the editor Output. Make the thinning a decision: give " +
                   "the node the minimum, or accept what the axis drops.[/color]";
        }

        List<(LegendPosition Legend, Vector2 Minimum)> measured = LegendComparisons(minimum);
        if (measured.Count > 1)
        {
            (LegendPosition cheapest, Vector2 cheapestMinimum) = measured[0];
            var parts = new List<string>();
            foreach ((LegendPosition position, Vector2 positionMinimum) in measured)
                parts.Add($"{position} {Px(positionMinimum.X)}×{Px(positionMinimum.Y)}");
            string table = string.Join(" · ", parts);

            if (cheapest != legend)
            {
                return $"[color=#7fd28a]legend {legend} costs {Px(minimum.X - cheapestMinimum.X)} of minimum " +
                       $"width more than {cheapest} in this same state ({table}): the {HintSide(legend)} inset " +
                       $"is the one to look at ({Px(HintInset(legend, left, right, top, bottom))} here). A " +
                       "legend on a side spends width, a legend above or below the plot spends height - the " +
                       "cheaper one is the axis you have more of.[/color]";
            }

            return $"[color=#7fd28a]{table} measured in this state: {legend} is already the cheapest minimum " +
                   $"width here, so the legend is not what costs space. The plot gets {ratio:F1} % of the " +
                   "node; the title, the axes and their labels are the rest.[/color]";
        }

        // Nothing to compare yet: name the widest reserve the layout actually produced, from its numbers.
        string side = WidestSide(left, right, top, bottom, out float inset);
        float padding = side switch
        {
            "right" => chart.PaddingRight,
            "top" => chart.PaddingTop,
            "bottom" => chart.PaddingBottom,
            _ => chart.PaddingLeft,
        };

        return $"[color=#7fd28a]the {side} inset is the widest reserve at {Px(inset)}, {Px(inset - padding)} of " +
               $"it beyond the chart's {Px(padding)} padding (the legend, an axis label column or a title " +
               $"band); the plot keeps {ratio:F1} % of the node. Press L to measure the other legend positions: " +
               "the hint then compares the measured numbers of this same state instead of guessing.[/color]";
    }

    /// <summary>A format string as the readout prints it, with empty named instead of shown as nothing.</summary>
    private static string Quote(string format) => format.Length > 0 ? $"\"{format}\"" : "\"\" (the scale's own)";

    /// <summary>Which inset is the widest, and how wide it is.</summary>
    private static string WidestSide(float left, float right, float top, float bottom, out float inset)
    {
        string side = "left";
        float widest = left;
        if (right > widest)
        {
            side = "right";
            widest = right;
        }

        if (top > widest)
        {
            side = "top";
            widest = top;
        }

        if (bottom > widest)
        {
            side = "bottom";
            widest = bottom;
        }

        inset = widest;
        return side;
    }

    /// <summary>Which inset a legend position is paid from.</summary>
    private static string HintSide(LegendPosition legend)
    {
        return legend switch
        {
            LegendPosition.Right => "right",
            LegendPosition.Top => "top",
            LegendPosition.Bottom => "bottom",
            _ => "left",
        };
    }

    /// <summary>The inset a legend position currently occupies.</summary>
    private static float HintInset(LegendPosition legend, float left, float right, float top, float bottom)
    {
        return legend switch
        {
            LegendPosition.Right => right,
            LegendPosition.Top => top,
            LegendPosition.Bottom => bottom,
            _ => left,
        };
    }

    /// <summary>
    /// The difference between the two states around the last key. Both sides were read back after a render, so
    /// this line is a report rather than a prediction - which is the whole point of pressing the key.
    /// </summary>
    private string DeltaText(string key, string beforeState, Sample before, Sample after)
    {
        (float beforeLeft, float beforeRight, float beforeTop, float beforeBottom) = Insets(before);
        (float left, float right, float top, float bottom) = Insets(after);

        var parts = new List<string>
        {
            $"min {Signed(after.Minimum.X - before.Minimum.X)} × {Signed(after.Minimum.Y - before.Minimum.Y)} px",
            $"node {Signed(after.Size.X - before.Size.X)} × {Signed(after.Size.Y - before.Size.Y)} px",
            // The insets move even when the plot rectangle does not: a legend that walks from the top to the
            // bottom of the same chart trades the two reserves without changing the plot's width or height.
            "insets left " + Signed(left - beforeLeft) + " · right " + Signed(right - beforeRight) +
            " · top " + Signed(top - beforeTop) + " · bottom " + Signed(bottom - beforeBottom) + " px",
            $"plot {Signed(after.Plot.Size.X - before.Plot.Size.X)} × {Signed(after.Plot.Size.Y - before.Plot.Size.Y)} px",
            $"plot area {Signed(Area(after) - Area(before))} px²",
        };

        return $"[b]{key}[/b] · {beforeState} → {StateLabel()} · {string.Join(" · ", parts)}";
    }

    /// <summary>The rows and series the chart is drawing, as the chart itself reports them.</summary>
    private string RowSummary()
    {
        var series = new HashSet<string>();
        foreach (DataRow row in View.DataRows)
        {
            if (row.TryGet("series", out string name) && !string.IsNullOrEmpty(name)) series.Add(name);
        }

        return series.Count > 0
            ? $"{View.DataRows.Count} rows · {series.Count} series"
            : $"{View.DataRows.Count} rows";
    }

    private static float Area(Sample sample) => sample.Plot.Size.X * sample.Plot.Size.Y;

    // ── Text helpers ─────────────────────────────────────────────────────────

    private static string Px(float value) => $"{value:F0} px";

    /// <summary>One decimal: for a shortfall of a fraction of a pixel, where <see cref="Px"/> would hide it.</summary>
    private static string Px1(float value) => $"{value:F1} px";

    /// <summary>A signed pixel delta (a movement smaller than half a pixel reads as zero).</summary>
    private static string Signed(float value)
        => value > 0.5f ? $"+{value:F0}" : value < -0.5f ? $"{value:F0}" : "0";

    /// <summary>One part against another, as a share of the second (both in pixels).</summary>
    private static string Part(float part, float whole)
        => whole > 0f ? $"{100f * Mathf.Max(0f, part) / whole:F1} %" : "—";

    private static string OnOff(bool value) => value ? "on" : "off";
}
