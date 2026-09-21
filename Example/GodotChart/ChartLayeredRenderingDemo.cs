using System;
using System.Collections.Generic;
using System.Diagnostics;
using Godot;
using GodotNodeExtension.Component.GodotChart;
using GodotNodeExtension.Component.GodotChart.Canvas;
using GodotNodeExtension.Component.GodotChart.Marks;

namespace GodotNodeExtension.Example.GodotChart;

/// <summary>
/// One data table, one mark, two charts side by side: the left one draws every frame single-pass
/// (<c>LayeredRendering = false</c>), the right one keeps its data layer in an image and repaints only the
/// overlay (<c>LayeredRendering = true</c>, which is <see cref="Chart.UseLayerCache"/>). Both are hand-built
/// <see cref="Chart"/> instances, because only the host that calls <see cref="Chart.Render"/> can time it.
/// <para>
/// Three frames are measured per side: the <b>rebuild</b> frame (the layer is dropped, so the data layer is
/// drawn again - the cache buys nothing there, it only skips drawing that is already done), the <b>static</b>
/// frame (nothing changed: the left redraws the layer, the right presents it) and the <b>hover</b> frame (the
/// highlighted row moves: the right presents the layer and draws the marker, the left draws the whole table
/// again). The static and the hover frames are what the option is for.
/// </para>
/// <para>
/// Memory is reported twice, and the two figures are not the same kind of number: the layer image is
/// <b>computed</b> from the chart rectangle (<c>width × height × 4</c> bytes - what the right chart costs
/// extra), while the per-row figure is <b>measured</b> with <see cref="GC.GetTotalMemory(bool)"/> around the
/// table build (what the shared data costs, both charts alike).
/// </para>
/// <para>
/// Keys: <c>1</c>/<c>2</c>/<c>3</c>/<c>4</c> = 1k/10k/100k/200k rows (default 100k), <c>S</c> toggles
/// <see cref="LineMark.Smooth"/>, <c>B</c> runs 60 static redraws, <c>H</c> runs 60 hover redraws and
/// <c>A</c> replays the entry animation on both charts.
/// </para>
/// <para>
/// <c>A</c> is the case the cache cannot win: the entry progress is a <b>data-layer</b> input (the marks
/// draw shorter or dimmer while it runs), so the right chart has to draw and capture its layer on every
/// frame of the animation - exactly like the left one. The two columns' numbers converge while it runs,
/// which is the honest statement about what layering buys: it skips drawing that is already done, and an
/// animation is never already done. The library never animates on its own - a host drives it (see
/// <c>ChartAnimationDemo</c>) - so this page carries the loop the key needs.
/// </para>
/// </summary>
public partial class ChartLayeredRenderingDemo : Control
{
    /// <summary>Row counts the number keys select.</summary>
    private static readonly int[] Sizes = [1_000, 10_000, 100_000, 200_000];

    /// <summary>Redraws one benchmark run drives (<c>B</c> static, <c>H</c> hover).</summary>
    private const int BenchmarkFrames = 60;

    /// <summary>Redraws one rebuild run measures: the layer is dropped again on each of them.</summary>
    private const int RebuildFrames = 4;

    /// <summary>
    /// Redraws drawn before a measurement starts. The right chart captures its layer on the first frame that
    /// draws, and that readback must not land in a static or hover sample.
    /// </summary>
    private const int SettleFrames = 3;

    /// <summary>Redraws the rolling window behind each <c>Render()</c> line holds.</summary>
    private const int SampleWindow = 60;

    // ── Scene wiring ─────────────────────────────────────────────────────────

    /// <summary>Canvas of the single-pass chart (<c>LayeredRendering = false</c> in the scene).</summary>
    [Export] public Canvas2DControl PlainView { get; set; } = null!;

    /// <summary>Canvas of the chart that keeps its layer (<c>LayeredRendering = true</c> in the scene).</summary>
    [Export] public Canvas2DControl CachedView { get; set; } = null!;

    /// <summary>Numbers of the single-pass chart.</summary>
    [Export] public RichTextLabel PlainReadout { get; set; } = null!;

    /// <summary>Numbers of the layered chart.</summary>
    [Export] public RichTextLabel CachedReadout { get; set; } = null!;

    /// <summary>The facts both charts share: frame time, the two memory figures and the key list.</summary>
    [Export] public RichTextLabel Readout { get; set; } = null!;

    // ── Charts and data ──────────────────────────────────────────────────────

    private Chart? _plain;
    private Chart? _cached;

    /// <summary>The one table both charts read (a <see cref="DataRow"/> is read-only inside a chart).</summary>
    private List<DataRow> _rows = [];

    private int _rowCount = Sizes[2];
    private bool _smooth = true;      // LineMark.Smooth defaults to true; S turns it off

    /// <summary>Managed bytes one row costs, measured while the table was built (NaN when unmeasurable).</summary>
    private double _bytesPerRow = double.NaN;

    /// <summary>Managed bytes the whole table retained (the same measurement as <see cref="_bytesPerRow"/>).</summary>
    private double _retainedBytes;

    /// <summary>Milliseconds the last table build took.</summary>
    private double _buildMilliseconds;

    /// <summary>
    /// Entry animation of both charts, replayed by <c>A</c>. The library does not animate by itself: a host
    /// builds the <see cref="AnimationContext"/> from this controller each frame and hands it to the charts,
    /// exactly like <c>ChartAnimationDemo</c> does (see its <c>BuildContext</c>).
    /// </summary>
    private readonly AnimationController _anim = new();

    // ── Measurement state ────────────────────────────────────────────────────

    /// <summary>What the next redraw is: it decides which window the sample goes into.</summary>
    private enum FrameKind { None, Settle, Rebuild, Static, Hover }

    private FrameKind _frameKind = FrameKind.None;

    /// <summary>Benchmark waiting for the settle frames to pass before it starts recording.</summary>
    private FrameKind _pending = FrameKind.None;

    private int _settleFrames;
    private int _rebuildFrames;
    private int _staticFrames;
    private int _hoverFrames;

    /// <summary>Set while the first rebuild run still has to happen on a warm page (see <see cref="_Ready"/>).</summary>
    private bool _warmRebuild;

    /// <summary>Position of the highlighted row inside the sweep of a hover run.</summary>
    private int _hoverStep;

    /// <summary>Rolling windows of every redraw, whatever triggered it.</summary>
    private readonly Queue<double> _plainDraws = new();
    private readonly Queue<double> _cachedDraws = new();

    /// <summary>Windows holding the samples that belong to one kind of frame.</summary>
    private readonly Queue<double> _plainRebuilds = new();
    private readonly Queue<double> _cachedRebuilds = new();
    private readonly Queue<double> _plainStatic = new();
    private readonly Queue<double> _cachedStatic = new();
    private readonly Queue<double> _plainHover = new();
    private readonly Queue<double> _cachedHover = new();

    private double _frameMilliseconds;
    private double _sinceReadout;

    // ── Frame loop ───────────────────────────────────────────────────────────

    /// <inheritdoc />
    public override void _Ready()
    {
        if (PlainView.Canvas is not { } plainCanvas || CachedView.Canvas is not { } cachedCanvas)
        {
            // No rendering device (a headless run): say so instead of showing two empty columns.
            PlainReadout.Text = string.Empty;
            CachedReadout.Text = string.Empty;
            Readout.Text = "no canvas: this page needs a rendering device - every number here is measured " +
                           "on the two canvases the scene declares";
            return;
        }

        Rebuild();      // first, so the measured table cost is not mixed up with the chart setup

        _plain = BuildChart(plainCanvas, "LayeredRendering = false", useLayerCache: false);
        _cached = BuildChart(cachedCanvas, "LayeredRendering = true", useLayerCache: true);
        _plain.Data(_rows);
        _cached.Data(_rows);

        PlainView.CanvasDraw += (control, _) => DrawChart(control, _plain);
        CachedView.CanvasDraw += (control, _) => DrawChart(control, _cached);

        // The first frames of the page are not a measurement: they compile and allocate. Draw them, then take
        // the rebuild sample on a warm page (see _warmRebuild in _Process).
        _settleFrames = SettleFrames;
        _warmRebuild = true;
        RefreshReadout();
        PlainView.Invalidate();
        CachedView.Invalidate();
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        _frameMilliseconds = delta * 1000.0;
        _sinceReadout += delta;

        if (_plain is null) return;     // no canvas: the note written in _Ready stays

        // One redraw per frame while a run is on: this _Process runs before the two canvases', so the kind set
        // here is the kind they draw with.
        if (_settleFrames > 0)
        {
            _settleFrames--;
            _frameKind = FrameKind.Settle;
        }
        else if (_rebuildFrames > 0)
        {
            _rebuildFrames--;
            _frameKind = FrameKind.Rebuild;
            DropLayer();                // the frame after this one has to draw the layer again
        }
        else if (_staticFrames > 0)
        {
            _staticFrames--;
            _frameKind = FrameKind.Static;
        }
        else if (_hoverFrames > 0)
        {
            _hoverFrames--;
            _frameKind = FrameKind.Hover;
            MoveHover();
        }
        else
        {
            _frameKind = FrameKind.None;
        }

        if (_settleFrames == 0 && _pending != FrameKind.None) StartRun();
        if (_settleFrames == 0 && _rebuildFrames == 0 && _staticFrames == 0 && _hoverFrames == 0 && _warmRebuild)
        {
            // Nothing else is running: measure the first rebuild on a page that has already drawn (see _Ready).
            _warmRebuild = false;
            StartRebuildRun();
        }

        // The entry animation is driven here, one context per frame (the same loop ChartAnimationDemo uses).
        // It is a data-layer input, so it invalidates both charts - and on the right one it drops the layer,
        // which is the point of A: no cache can skip an animation.
        if (_anim.IsAnimating || _anim.EntryProgress < 1f)
        {
            var context = new AnimationContext
            {
                EntryProgress = _anim.EntryProgress,
                GlobalOpacity = _anim.GlobalOpacity,
                HoverScale = _anim.HoverScale,
                ExitProgress = _anim.ExitProgress,
                DataTransitionProgress = _anim.DataTransitionProgress,
                SeriesProgress = _anim.SeriesProgress,
            };
            _plain?.Animate(context);
            _cached?.Animate(context);
            PlainView.Invalidate();
            CachedView.Invalidate();
        }

        if (_frameKind != FrameKind.None)
        {
            PlainView.Invalidate();
            CachedView.Invalidate();
        }

        if (_sinceReadout < 0.25) return;

        _sinceReadout = 0;
        RefreshReadout();
    }

    /// <inheritdoc />
    public override void _Input(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false } key) return;

        // _Input runs before the GUI: the example browser puts keyboard focus on its tree, and the
        // tree consumes plain letters and digits for its type-ahead, so a shortcut handled in
        // _UnhandledKeyInput would never arrive (pressing "B" jumped to the Basics page instead).
        if (_plain is null || _cached is null) return;

        switch (key.Keycode)
        {
            case Key.Key1 or Key.Key2 or Key.Key3 or Key.Key4:
                _rowCount = Sizes[(int)key.Keycode - (int)Key.Key1];
                Rebuild();
                break;
            case Key.S:
                _smooth = !_smooth;
                ApplyMarkSettings();
                break;
            case Key.B:
                // A static run: nothing changes, so the cached chart presents its layer while the other one
                // draws it again. Hover is cleared first so the overlay of the cached chart is empty - the
                // static number then means the presented layer alone.
                _pending = FrameKind.Static;
                _rebuildFrames = 0;
                _staticFrames = 0;
                _hoverFrames = 0;
                _settleFrames = SettleFrames;
                _plain.Hover(-1);
                _cached.Hover(-1);
                break;
            case Key.H:
                // A hover run: the highlight moves every frame, so each sample is a real state change.
                _pending = FrameKind.Hover;
                _rebuildFrames = 0;
                _staticFrames = 0;
                _hoverFrames = 0;
                _settleFrames = SettleFrames;
                break;
            case Key.A:
                // Replay the entry animation. Whatever run is in progress is dropped first: its samples would
                // mix an animation frame into a static or hover window, and the two are not comparable.
                _pending = FrameKind.None;
                _rebuildFrames = 0;
                _staticFrames = 0;
                _hoverFrames = 0;
                _settleFrames = 0;
                _frameKind = FrameKind.None;
                _anim.Reset();
                _anim.StartEntry(this, 1);      // one series: both charts draw one line
                break;
            default:
                return;
        }

        // The key was handled here, so the GUI must not also act on it (the example browser's tree would
        // otherwise treat the letter as type-ahead - see the note above).
        GetViewport().SetInputAsHandled();
        RefreshReadout();
        PlainView.Invalidate();
        CachedView.Invalidate();
    }

    /// <inheritdoc />
    public override void _ExitTree()
    {
        // The views own the canvases they host, so both charts keep ownsCanvas: false.
        _anim.Dispose();
        _plain?.Dispose();
        _cached?.Dispose();
        _plain = null;
        _cached = null;
    }

    // ── Charts ───────────────────────────────────────────────────────────────

    /// <summary>
    /// One of the two charts. They are configured identically - same table, same encodings, same mark, same
    /// axes - so the only difference the measured numbers can come from is the layer cache.
    /// </summary>
    private Chart BuildChart(ICanvas2D canvas, string title, bool useLayerCache)
    {
        var chart = new Chart(canvas)      // ownsCanvas: false - the view owns this canvas
        {
            Width = 640f,
            Height = 360f,
            Title = title,
            UseLayerCache = useLayerCache,
        };
        chart.Theme(ChartTheme.Dark());
        chart.Encode(Channel.X, "x");
        chart.Encode(Channel.Y, "value");
        chart.Mark(new LineMark { Smooth = _smooth, StrokeWidth = 1f, ShowArea = false });
        chart.XAxis(new AxisConfig { Title = "Sample", Unit = "index" });
        chart.YAxis(new AxisConfig { Title = "Value", Unit = "units" });
        return chart;
    }

    /// <summary>
    /// Draw one side and file the sample: the chart follows the canvas rect, so both charts measure the same
    /// geometry, and the elapsed time is this chart's <see cref="Chart.Render"/> call alone.
    /// </summary>
    private void DrawChart(Canvas2DControl view, Chart? chart)
    {
        if (chart is null) return;

        chart.Width = Mathf.Max(1f, view.CanvasSize.X);
        chart.Height = Mathf.Max(1f, view.CanvasSize.Y);

        long started = Stopwatch.GetTimestamp();
        chart.Render();
        double elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;

        bool plain = ReferenceEquals(chart, _plain);
        Record(plain ? _plainDraws : _cachedDraws, elapsed);

        switch (_frameKind)
        {
            case FrameKind.Rebuild:
                Record(plain ? _plainRebuilds : _cachedRebuilds, elapsed);
                break;
            case FrameKind.Static:
                Record(plain ? _plainStatic : _cachedStatic, elapsed);
                break;
            case FrameKind.Hover:
                Record(plain ? _plainHover : _cachedHover, elapsed);
                break;
        }
    }

    // ── Measurement runs ─────────────────────────────────────────────────────

    /// <summary>Start the benchmark that was waiting for the settle frames, with empty windows.</summary>
    private void StartRun()
    {
        if (_pending == FrameKind.Static)
        {
            _plainStatic.Clear();
            _cachedStatic.Clear();
            _staticFrames = BenchmarkFrames;
        }
        else
        {
            _plainHover.Clear();
            _cachedHover.Clear();
            _hoverStep = 0;
            _hoverFrames = BenchmarkFrames;
        }

        _pending = FrameKind.None;
    }

    /// <summary>
    /// Measure the frame that has to draw the data layer from scratch on <b>both</b> charts: the only
    /// comparison the cache cannot win, because it skips drawing that is already done instead of drawing
    /// faster. The layer is dropped before every sample, so the number is reproducible rather than the one
    /// warm frame a single drop would give.
    /// </summary>
    private void StartRebuildRun()
    {
        _plainRebuilds.Clear();
        _cachedRebuilds.Clear();
        _rebuildFrames = RebuildFrames;
        _settleFrames = SettleFrames;
        _pending = FrameKind.None;
        _staticFrames = 0;
        _hoverFrames = 0;
        DropLayer();
    }

    /// <summary>
    /// Throw away the layer of the right chart, so the next frame draws (and captures) it again. On the left
    /// chart the call is a no-op - it keeps no layer, so its next frame is a full redraw anyway.
    /// </summary>
    private void DropLayer()
        => _cached?.InvalidateLayerCache();

    /// <summary>
    /// Move the highlighted row across the middle third of the table and hand it to both charts: a hover frame
    /// must be a state change, not the same highlight measured twice.
    /// </summary>
    private void MoveHover()
    {
        int first = _rowCount / 3;
        int span = Math.Max(1, _rowCount / 3);
        int offset = Math.Min(span - 1, _hoverStep * span / BenchmarkFrames);
        _hoverStep++;
        int row = first + offset;

        // Hover never rebuilds the layer (that is the whole point of the option), so this is a state change
        // the right chart answers with its overlay alone.
        _plain?.Hover(row);
        _cached?.Hover(row);
    }

    /// <summary>Append one sample to a window of the last <see cref="SampleWindow"/> redraws.</summary>
    private static void Record(Queue<double> samples, double milliseconds)
    {
        samples.Enqueue(milliseconds);
        while (samples.Count > SampleWindow) samples.Dequeue();
    }

    /// <summary>Average of a window, or NaN while it holds no sample.</summary>
    private static double Average(Queue<double> samples)
    {
        if (samples.Count == 0) return double.NaN;

        double total = 0;
        foreach (double sample in samples) total += sample;
        return total / samples.Count;
    }

    /// <summary>Peak of a window, or NaN while it holds no sample.</summary>
    private static double Peak(Queue<double> samples)
    {
        double peak = double.NaN;
        foreach (double sample in samples) peak = double.IsNaN(peak) ? sample : Math.Max(peak, sample);
        return peak;
    }

    // ── Data ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Build the shared table and measure what it costs. The waveform is deterministic (sine layers plus a
    /// hash noise), so the same row count always yields the same table and two runs are comparable.
    /// </summary>
    private void Rebuild()
    {
        long before = GC.GetTotalMemory(forceFullCollection: true);
        long started = Stopwatch.GetTimestamp();

        var rows = new List<DataRow>(_rowCount);
        for (int i = 0; i < _rowCount; i++)
        {
            double wave = 50.0 + 35.0 * Math.Sin(i / 400.0) + 12.0 * Math.Sin(i / 23.0);
            double noise = HashNoise(i) * 8.0;
            rows.Add(new DataRow(2).Set("x", i).Set("value", wave + noise));
        }

        _buildMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        _rows = rows;

        long after = GC.GetTotalMemory(forceFullCollection: true);
        // A few hundred rows are smaller than the noise of a full collection, so the figure is only reported
        // where it means something; the number is retained managed bytes, not the allocation traffic.
        _retainedBytes = after - before;
        _bytesPerRow = _rowCount >= 10_000 && _retainedBytes > 0
            ? _retainedBytes / _rowCount
            : double.NaN;

        _plain?.Data(_rows);        // the chart's own data version changes, so the layer of the right chart is
        _cached?.Data(_rows);       // stale from here on and the rebuild run below draws it again

        ClearSamples();

        // Called from _Ready the charts do not exist yet: there the table is only built, and the rebuild
        // sample is taken on a warm page instead (see _warmRebuild).
        if (_plain is null || _cached is null) return;
        StartRebuildRun();
    }

    /// <summary>Deterministic pseudo-noise in [0, 1) - the same sample index always yields the same value.</summary>
    private static double HashNoise(int index)
    {
        double value = Math.Sin(index * 12.9898) * 43758.5453;
        return value - Math.Floor(value);
    }

    /// <summary>Push the knobs the keys change onto the marks the charts already hold.</summary>
    private void ApplyMarkSettings()
    {
        // ApplyToAllMarks invalidates the layout, so the layer of the right chart is stale afterwards - no
        // separate InvalidateLayerCache() call is needed for a setting changed through the chart.
        _plain?.ApplyToAllMarks(mark =>
        {
            if (mark is LineMark line) line.Smooth = _smooth;
        });
        _cached?.ApplyToAllMarks(mark =>
        {
            if (mark is LineMark line) line.Smooth = _smooth;
        });

        // A mark setting is a data-layer input: the next frame is a rebuild frame on both sides.
        ClearSamples();
        StartRebuildRun();
    }

    /// <summary>Drop every window: a new table or a new mark setting makes the old samples incomparable.</summary>
    private void ClearSamples()
    {
        _plainDraws.Clear();
        _cachedDraws.Clear();
        _plainRebuilds.Clear();
        _cachedRebuilds.Clear();
        _plainStatic.Clear();
        _cachedStatic.Clear();
        _plainHover.Clear();
        _cachedHover.Clear();
    }

    // ── Readout ──────────────────────────────────────────────────────────────

    /// <summary>Refresh the three readouts: one column per chart plus the facts they share.</summary>
    private void RefreshReadout()
    {
        if (_plain is null || _cached is null) return;      // no canvas: the note written in _Ready stays

        PlainReadout.Text = Column(_plainDraws, _plainRebuilds, _plainStatic, _plainHover,
            "[b]LayeredRendering = false[/b] · single pass · the data layer is drawn again every frame",
            "[color=#93a1b5]no layer image is kept[/color]");
        CachedReadout.Text = Column(_cachedDraws, _cachedRebuilds, _cachedStatic, _cachedHover,
            "[b]LayeredRendering = true[/b] · the data layer is kept in an image",
            "[color=#93a1b5]layer image: " + LayerImageText() + "[/color]");
        Readout.Text = SharedText();
    }

    /// <summary>The numbers of one chart, in the order the frame kinds happen.</summary>
    private static string Column(Queue<double> draws, Queue<double> rebuilds, Queue<double> staticFrames,
                                Queue<double> hoverFrames, string title, string footnote)
    {
        double hover = Average(hoverFrames);
        double staticFrame = Average(staticFrames);

        var lines = new List<string>
        {
            title,
            $"Render(): {Milliseconds(Average(draws))} average / {Milliseconds(Peak(draws))} peak " +
            $"({draws.Count} redraws)",
            $"rebuild frame: {Milliseconds(Average(rebuilds))} ({rebuilds.Count} redraws that draw the data " +
            "layer again)",
            $"static frame: {Milliseconds(staticFrame)} (B: nothing changes)",
            $"hover frame: {Milliseconds(hover)} (H: the highlighted row moves)",
            footnote,
        };

        if (!double.IsNaN(hover) && !double.IsNaN(staticFrame))
        {
            lines.Add($"[color=#7fd28a]hover marker alone: {Milliseconds(hover - staticFrame)} " +
                      "(hover frame - static frame)[/color]");
        }

        return string.Join("\n", lines);
    }

    /// <summary>
    /// The facts both columns depend on: the frame time, the side-by-side comparison of the two frames that
    /// matter, and the two memory figures - each labelled with where the number comes from.
    /// </summary>
    private string SharedText()
    {
        var lines = new List<string>
        {
            $"frame: {_frameMilliseconds:F1} ms · {Engine.GetFramesPerSecond():F0} fps (one engine frame - both " +
            "charts are drawn inside it; every number above times a single Render() call)",
        };

        if (!LayerCacheEngaged)
        {
            lines.Add("[color=#e8a33d]this canvas backend cannot read its surface back " +
                      "(CanvasCapabilities.SupportsSurfaceCapture is false), so the right chart falls back to " +
                      "single-pass: it says why once, and the two columns then match.[/color]");
        }

        double plainRebuild = Average(_plainRebuilds);
        double cachedRebuild = Average(_cachedRebuilds);
        lines.Add($"layer drawn again: left {Milliseconds(plainRebuild)} · right {Milliseconds(cachedRebuild)} " +
                  "- the same drawing on both charts, and the right one adds one surface readback (what a " +
                  "rebuild costs the cache), so the cache does not make drawing faster - it skips drawing " +
                  "that is already done");

        double plainHover = Average(_plainHover);
        double cachedHover = Average(_cachedHover);
        double cachedStatic = Average(_cachedStatic);
        lines.Add($"static frame: left {Milliseconds(Average(_plainStatic))} (the data layer is redrawn) " +
                  $"against right {Milliseconds(cachedStatic)} (the cached image is presented) - nothing " +
                  "changed, so the right chart has nothing to draw but the blit");
        lines.Add($"hover frame: left {Milliseconds(plainHover)} (the whole table again) against " +
                  $"right {Milliseconds(cachedHover)} (the presented layer plus the hover marker) - press H " +
                  "on both charts and this is the layering's gain");

        if (!double.IsNaN(cachedHover) && !double.IsNaN(cachedStatic))
        {
            lines.Add($"hover marker alone: {Milliseconds(cachedHover - cachedStatic)} on the right - derived " +
                      "from the two cached frames. The presented layer is the static figure; the rest is the " +
                      "mark's own hover work, and a LineMark finds the hovered vertex by walking its series, " +
                      "so its overlay grows with the row count (0.1 ms at 1k rows against about 9 ms at 100k) " +
                      "while the presented layer stays flat");
        }

        lines.Add("memory, computed: " + LayerImageText() +
                  " - the right chart's extra footprint (one RGBA image of the chart rectangle, " +
                  "width × height × 4 bytes); the left chart keeps none");
        lines.Add("memory, measured: " + BytesPerRowText() +
                  " - GC.GetTotalMemory(true) before and after building the table, which both charts read " +
                  "(not a per-chart cost)");
        lines.Add("[color=#93a1b5]keys: 1/2/3/4 = 1k/10k/100k/200k rows (default 100k) · " +
                  "S = toggle LineMark.Smooth · B = 60 static redraws · H = 60 hover redraws · " +
                  "A = replay the entry animation on both charts[/color]");

        if (_anim.IsAnimating || _anim.EntryProgress < 1f)
        {
            lines.Add($"[color=#e8a33d]entry animation at {_anim.EntryProgress:F2} on both charts: the marks " +
                      "draw shorter and dimmer, which is a data-layer input - so the right chart draws and " +
                      "captures its layer on every frame of it, exactly like the left one. The cache skips " +
                      "drawing that is already done, and an animation is never already done.[/color]");
        }

        return string.Join("\n", lines);
    }

    /// <summary>
    /// The layer image in bytes, <b>computed</b> from the chart rectangle - the same <c>width × height × 4</c>
    /// the implementation pays, rounded out to whole pixels, and the only honest way to report it: the chart
    /// never lets the host read the allocation.
    /// </summary>
    private string LayerImageText()
    {
        if (_cached is null) return "—";

        int width = Math.Max(1, (int)Math.Ceiling(_cached.Width));
        int height = Math.Max(1, (int)Math.Ceiling(_cached.Height));
        double bytes = (double)width * height * 4;
        return $"{width} × {height} × 4 bytes = {Megabytes(bytes)} (computed)";
    }

    /// <summary>
    /// The measured table cost. <see cref="GC.GetTotalMemory(bool)"/> reports retained managed bytes, so the
    /// figure says what the rows keep alive, not what building them allocated.
    /// </summary>
    private string BytesPerRowText()
    {
        if (double.IsNaN(_bytesPerRow))
            return $"{_rowCount:N0} rows are too small to measure with GC.GetTotalMemory(true) " +
                   $"(built in {_buildMilliseconds:F0} ms)";

        return $"{_bytesPerRow:F0} bytes per row for {_rowCount:N0} rows = {Megabytes(_retainedBytes)} " +
               $"(measured, built in {_buildMilliseconds:F0} ms)";
    }

    /// <summary>
    /// Whether the right chart really keeps a layer here. The chart falls back silently (plus one warning when
    /// a mark is the reason), so the page checks the precondition the option rests on and says it out loud.
    /// </summary>
    private bool LayerCacheEngaged => CachedView.Canvas?.Capabilities.SupportsSurfaceCapture == true;

    /// <summary>Format a measured time, or a dash while the window holds no sample.</summary>
    private static string Milliseconds(double milliseconds)
        => double.IsNaN(milliseconds) ? "—" : $"{milliseconds:F1} ms";

    /// <summary>Format a byte count as megabytes.</summary>
    private static string Megabytes(double bytes) => $"{bytes / (1024.0 * 1024.0):F2} MB";
}
