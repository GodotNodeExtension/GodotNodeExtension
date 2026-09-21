using System;
using System.Collections.Generic;
using Godot;
using GodotNodeExtension.Component.GodotChart;

namespace GodotNodeExtension.Example.GodotChart;

/// <summary>
/// The animation layer, driven by a host - which is the only way it ever runs, because a
/// <see cref="ChartView"/> never animates itself. The captions in the scene stay short; the
/// explanation lives here.
/// <para>
/// The closed loop is <see cref="_Process"/>: build an <see cref="AnimationContext"/> from the
/// controller's values, hand it to <c>view.Chart.Animate(context)</c> and call
/// <see cref="ChartView.Repaint"/>. <see cref="ChartView.Repaint"/> and not
/// <see cref="ChartView.Refresh"/> - a rebuild replaces the <see cref="Chart"/> instance the context is
/// stored on and drops the animation with it, so the loop never reconfigures an export mid-phase.
/// </para>
/// <para>
/// <see cref="AnimationController"/> owns the four tweens (entry, hover, data transition, exit). They
/// come from <c>owner.CreateTween()</c>, so the scene tree advances them and the host only reads the
/// properties they animate: <see cref="AnimationController.EntryProgress"/>,
/// <see cref="AnimationController.GlobalOpacity"/>, <see cref="AnimationController.HoverScale"/>,
/// <see cref="AnimationController.DataTransitionProgress"/>,
/// <see cref="AnimationController.ExitProgress"/>,
/// <see cref="AnimationController.SeriesProgress"/> and <see cref="AnimationController.IsAnimating"/>.
/// Outside the scene tree nothing tweens: StartEntry, AnimateHover and StartDataTransition settle on
/// their final value and the StartExit callback runs at once - which is why <see cref="PlayExit"/>
/// restarts the entry from inside that callback instead of after the call, and why
/// <see cref="_ExitTree"/> disposes the controller (leaving the tree already kills its node-bound
/// tweens). The durations are read when a tween is created, so a slider change applies to the next
/// <see cref="ReplayEntry"/>.
/// </para>
/// <para>
/// What the built-in marks consume: the entry progress (one scalar, read as both
/// <c>ctx.AnimationProgress</c> and <c>ctx.Animation.EntryProgress</c> - the per-series
/// <see cref="AnimationContext.SeriesProgress"/> is *not* read), the exit progress, the global opacity
/// and - for the hovered element only - the hover scale.
/// <see cref="AnimationContext.DataTransitionProgress"/> and <see cref="AnimationContext.SeriesProgress"/>
/// have no consumer in the library at all, so a data transition is the host's business: it has to keep
/// the values that were on screen (<see cref="_previousAmplitudes"/> here, handed to the mark as
/// <see cref="EaseCurveMark.From"/>) and lerp towards the new ones itself. The middle panel's custom
/// <see cref="Mark"/> is what does that, and its bar height is
/// <c>Ease.Apply(per-series progress, type)</c> - the seven <see cref="EaseType"/> curves through one
/// raw progress. Its narrow category cells make the axis renderer fit the curve names (with an
/// ellipsis) and thin them out.
/// </para>
/// <para>
/// The first panel is a scatter because the built-in point mark is the one that consumes the most of
/// the context: the entry grows every radius from zero, the exit shrinks it back, the global opacity
/// fades the chart, and the hover scale multiplies the radius of the hovered element (press Hover, then
/// rest the pointer on a dot - it stays enlarged while the scale sits at 1.6; the ease panel widens all
/// of its bars instead). The third panel is the same rows with a theme whose
/// <see cref="ChartTheme.EnableAnimation"/> is false: the library honours that flag itself -
/// <see cref="Chart.Animate(AnimationContext?)"/> settles on the end state instead of animating - so this
/// panel simply does not move while the switch leaves the flag off.
/// </para>
/// </summary>
public partial class ChartAnimationDemo : Control
{
    /// <summary>Samples per series on the entry panel (the X axis is the sample index).</summary>
    private const int ScatterSamples = 4;

    /// <summary>Series name plus the value offset that keeps the three traces apart.</summary>
    private static readonly (string Name, double Offset)[] ScatterSeries =
        [("alpha", 0.0), ("beta", 12.0), ("gamma", 24.0)];

    /// <summary>The seven easing curves in <see cref="EaseType"/> order - one bar on the middle panel each.</summary>
    private static readonly EaseType[] Eases = Enum.GetValues<EaseType>();

    // ── Nodes ───────────────────────────────────────────────────────────────────

    /// <summary>Scatter panel whose built-in mark the controller animates.</summary>
    [Export] public ChartView EntryChart { get; set; } = null!;

    /// <summary>Ease-curve panel, drawn by <see cref="EaseCurveMark"/>.</summary>
    [Export] public ChartView EaseChart { get; set; } = null!;

    /// <summary>Contrast panel: a theme with <see cref="ChartTheme.EnableAnimation"/> off.</summary>
    [Export] public ChartView ThemeChart { get; set; } = null!;

    /// <summary>Starts the entry animation again (through <see cref="AnimationController.Reset"/>).</summary>
    [Export] public Button ReplayButton { get; set; } = null!;

    /// <summary>Animates the hover scale up - press it again to animate it back down.</summary>
    [Export] public Button HoverButton { get; set; } = null!;

    /// <summary>Feeds one new data generation and starts the data transition.</summary>
    [Export] public Button TransitionButton { get; set; } = null!;

    /// <summary>Runs the exit animation; its callback replays the entry.</summary>
    [Export] public Button ExitButton { get; set; } = null!;

    /// <summary><see cref="AnimationController.EntryDuration"/> in seconds (0.1 .. 2.0).</summary>
    [Export] public HSlider EntrySlider { get; set; } = null!;

    /// <summary><see cref="AnimationController.SeriesStagger"/> in seconds (0 .. 0.4).</summary>
    [Export] public HSlider StaggerSlider { get; set; } = null!;

    /// <summary>Turns the contrast panel's theme <see cref="ChartTheme.EnableAnimation"/> on and off.</summary>
    [Export] public CheckButton ThemeSwitch { get; set; } = null!;

    /// <summary>Live readout of the controller's progress values.</summary>
    [Export] public Label Status { get; set; } = null!;

    // One controller for the three panels.
    private readonly AnimationController _anim = new();

    // The middle panel is rebuilt like any ChartView, so its mark has to be added to every new Chart
    // instance - and the data transition is the mark's business, so the host keeps the reference.
    private readonly EaseCurveMark _easeMark = new();

    // Amplitudes (0..1) of the seven bars: the data the custom mark interpolates between during a
    // transition. The host keeps the snapshot because that is exactly what no built-in mark does.
    private readonly double[] _amplitudes = new double[Eases.Length];
    private readonly double[] _previousAmplitudes = new double[Eases.Length];

    // The contrast panel's theme resource. EnableAnimation is read by the library (Chart.Animate settles on
    // the end state), which is exactly what this panel demonstrates.
    private ChartTheme _contrastTheme = null!;

    private int _generation;
    private bool _hoverRaised;

    /// <inheritdoc />
    public override void _Ready()
    {
        ConfigureEntryChart();
        ConfigureEaseChart();
        ConfigureThemeChart();

        // Durations are read when a tween is created, so a slider change applies to the next Replay.
        EntrySlider.Value = _anim.EntryDuration;
        StaggerSlider.Value = _anim.SeriesStagger;
        EntrySlider.ValueChanged += value => _anim.EntryDuration = (float)value;
        StaggerSlider.ValueChanged += value => _anim.SeriesStagger = (float)value;

        ReplayButton.Pressed += ReplayEntry;
        HoverButton.Pressed += ToggleHover;
        TransitionButton.Pressed += PlayDataTransition;
        ExitButton.Pressed += PlayExit;
        // The switch writes the flag and nothing else - a rebuild is enough for the editor, where an edit
        // made in the Inspector also arrives through the resource's Changed signal. The library reads the
        // flag itself: Chart.Animate settles on the end state while it is off, so the panel stays still.
        ThemeSwitch.Toggled += pressed => _contrastTheme.EnableAnimation = pressed;

        FillAmplitudes(_generation);
        Array.Copy(_amplitudes, _previousAmplitudes, _amplitudes.Length);
        EaseChart.SetData(BuildEaseRows());

        var rows = BuildScatterRows(_generation);
        EntryChart.SetData(rows);
        ThemeChart.SetData(rows);

        // The threshold guard: ElementCount is set once (ShouldAnimate() then reads it instead of being
        // passed the count on every call) and AnimationThreshold is the limit past which a phase would
        // settle immediately instead of tweening - StartEntry asks the same question internally, so a
        // host that wants to know whether a phase will really animate can ask it here first.
        _anim.AnimationThreshold = 2000;
        _anim.ElementCount = EntryChart.DataRows.Count + Eases.Length;
        if (_anim.ShouldAnimate()) ReplayEntry();
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        // One context per frame, shared by the panels that are driven this frame.
        var context = BuildContext();
        Drive(EntryChart, context);

        // The contrast panel: its theme says "do not animate me" and Chart.Animate honours that by settling
        // on the end state. Tick the switch and it animates exactly like the first panel.
        Drive(ThemeChart, context);

        // The ease bars read the same context; their mark is what consumes the parts no built-in
        // mark touches (per-series progress and the data transition).
        Drive(EaseChart, context);

        Report();
    }

    /// <inheritdoc />
    public override void _ExitTree()
    {
        // Tweens are bound to the node that created them, so leaving the tree already stops them;
        // Dispose is the explicit version of that (it also drops a pending exit callback). Driving the
        // chart after this point would change nothing anyway: outside the tree StartEntry /
        // AnimateHover / StartDataTransition settle on their final value instead of creating a tween,
        // and StartExit runs its callback immediately (AnimationController checks IsInsideTree).
        _anim.Dispose();
    }

    // ── Panels ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Scatter plus size and colour: the built-in mark consumes the entry progress (the radius grows
    /// from zero), the exit progress (it shrinks back), the global opacity (the fade) and the hover
    /// scale of the hovered element.
    /// </summary>
    private void ConfigureEntryChart()
    {
        EntryChart.Kind = ChartKind.Scatter;
        EntryChart.XField = "x";
        EntryChart.YField = "value";
        EntryChart.ColorField = "series";
        EntryChart.SizeField = "weight";
        EntryChart.Legend = LegendPosition.Bottom;
    }

    /// <summary>
    /// Seven bars, one per <see cref="EaseType"/>. The built-in bar mark the node builds is kept out of
    /// the way by the rows - they carry no <c>value</c> field, so it has nothing to draw - and the
    /// custom mark is added to the chart itself, where it can read the whole
    /// <see cref="AnimationContext"/> instead of the entry scalar alone.
    /// </summary>
    private void ConfigureEaseChart()
    {
        EaseChart.Kind = ChartKind.Bar;
        EaseChart.XField = "ease";
        EaseChart.YField = "value";          // no row carries "value": IntervalMark draws nothing here
        EaseChart.Legend = LegendPosition.None;

        // ConfigureChart runs on every rebuild, so the mark is added to each new Chart instance.
        EaseChart.ConfigureChart = chart =>
        {
            chart.Scale(Channel.Y, new LinearScale(0, 1));   // the bars are amplitudes: pin the domain
            chart.Mark(_easeMark);
        };
    }

    /// <summary>
    /// The contrast panel: same rows, same kind, same palette and the same host loop as the first one -
    /// the only difference is this theme resource with <see cref="ChartTheme.EnableAnimation"/> off.
    /// Nothing in the host reads that flag: the library does, in
    /// <see cref="Chart.Animate(AnimationContext?)"/>, so this panel settles on the end state while the
    /// first one animates even though <see cref="_Process"/> feeds both the same context.
    /// </summary>
    private void ConfigureThemeChart()
    {
        _contrastTheme = ChartTheme.Dark();
        _contrastTheme.EnableAnimation = false;
        ThemeChart.CustomTheme = _contrastTheme;
        ThemeChart.Kind = ChartKind.Scatter;
        ThemeChart.XField = "x";
        ThemeChart.YField = "value";
        ThemeChart.ColorField = "series";
        ThemeChart.SizeField = "weight";
        ThemeChart.Legend = LegendPosition.Bottom;
    }

    // ── Driving ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Hand one context to a chart and redraw the frame. <see cref="ChartView.Repaint"/> is the point:
    /// it draws again without rebuilding, so the chart instance - and with it the animation context set
    /// just before - survives. <see cref="ChartView.Refresh"/> would throw the state away.
    /// </summary>
    private static void Drive(ChartView view, AnimationContext context)
    {
        // A ChartView builds its Chart in its own _Process, so on the very first frame it may be null.
        if (view.Chart is not { } chart) return;

        chart.Animate(context);
        view.Repaint();
    }

    /// <summary>
    /// This frame's context: exactly the values the <see cref="AnimationController"/> is animating.
    /// The two members no built-in mark reads yet (<see cref="AnimationContext.DataTransitionProgress"/>
    /// and <see cref="AnimationContext.SeriesProgress"/>) are passed on as well - the custom mark of
    /// the middle panel is what consumes them.
    /// </summary>
    private AnimationContext BuildContext() => new()
    {
        EntryProgress = _anim.EntryProgress,
        GlobalOpacity = _anim.GlobalOpacity,
        HoverScale = _anim.HoverScale,
        ExitProgress = _anim.ExitProgress,
        DataTransitionProgress = _anim.DataTransitionProgress,
        SeriesProgress = _anim.SeriesProgress,
    };

    /// <summary>The live readout the four phases can be checked against.</summary>
    private void Report() => Status.Text =
        $"{(_anim.IsAnimating ? "animating" : "idle")} · enter {_anim.EntryProgress:F2}" +
        $" · hover {_anim.HoverScale:F2} · data {_anim.DataTransitionProgress:F2}" +
        $" · exit {_anim.ExitProgress:F2}";

    // ── The four phases ─────────────────────────────────────────────────────

    /// <summary>
    /// Replay the entry. <see cref="AnimationController.Reset"/> drops whatever is still running, so a
    /// replay always starts from a clean state (exit progress back to 0, hover scale back to 1).
    /// </summary>
    private void ReplayEntry()
    {
        _anim.Reset();
        // One progress per series: the seven bars of the ease panel are the series, so SeriesStagger
        // gives each curve its own delayed progress (SeriesProgress[0..6]).
        _anim.StartEntry(this, Eases.Length);
    }

    /// <summary>
    /// Animate the hover scale and leave it where the tween ends, so the built-in marks can show it:
    /// they read it for the <i>hovered</i> element, so press Hover and then rest the pointer on a dot -
    /// the dot stays enlarged while <see cref="AnimationController.HoverScale"/> is 1.6. The ease panel
    /// applies the same value to every bar, which makes the tween visible without a hovered element.
    /// </summary>
    private void ToggleHover()
    {
        _hoverRaised = !_hoverRaised;
        _anim.AnimateHover(this, _hoverRaised ? 1.6f : 1f);
    }

    /// <summary>
    /// One new data generation. The built-in marks jump to the new values (they ignore
    /// <see cref="AnimationContext.DataTransitionProgress"/> - see the dots on the first panel), while
    /// the ease bars are lerped from the snapshot the host kept to the values just handed over.
    /// </summary>
    private void PlayDataTransition()
    {
        _generation++;

        // A data-only update: SetData pushes the rows into the existing Chart, so no rebuild happens
        // and the animation state (and the custom mark's snapshot) survives.
        var rows = BuildScatterRows(_generation);
        EntryChart.SetData(rows);
        ThemeChart.SetData(rows);

        // What is on screen now becomes the "from" side of the lerp; the "to" side is the row value.
        Array.Copy(_amplitudes, _previousAmplitudes, _amplitudes.Length);
        _easeMark.From = _previousAmplitudes;
        FillAmplitudes(_generation);
        EaseChart.SetData(BuildEaseRows());

        _anim.StartDataTransition(this, duration: 0.4f);
    }

    /// <summary>
    /// Run the exit; the callback then replays the entry, so the chart is never left invisible. The
    /// callback fires exactly once when the tween finishes - or synchronously when the node is outside
    /// the tree - which is why the restart lives inside it rather than after the call: outside the tree
    /// the exit would already be over by the time the call returns, and a second StartExit would not
    /// swallow the pending callback either.
    /// </summary>
    private void PlayExit() => _anim.StartExit(this, () => _anim.StartEntry(this, Eases.Length));

    // ── Data ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Rows of one data generation: three series with four samples each. The values are derived from
    /// the generation instead of a random source, so a transition is reproducible.
    /// </summary>
    private static List<DataRow> BuildScatterRows(int generation)
    {
        var rows = new List<DataRow>(ScatterSeries.Length * ScatterSamples);
        foreach (var (name, offset) in ScatterSeries)
        {
            for (int i = 0; i < ScatterSamples; i++)
            {
                rows.Add(new DataRow(4)
                    .Set("x", i + 1)
                    .Set("value", 30 + offset + (i * 17 + generation * 23) % 45)
                    .Set("series", name)
                    .Set("weight", 0.2 + ((i + generation) % 4) * 0.25));
            }
        }
        return rows;
    }

    /// <summary>
    /// One row per easing curve. The category names it on the X axis, and the custom mark reads the
    /// curve name (<c>ease</c>) and the amplitude it animates to (<c>target</c>) out of the row.
    /// </summary>
    private List<DataRow> BuildEaseRows()
    {
        var rows = new List<DataRow>(Eases.Length);
        for (int i = 0; i < Eases.Length; i++)
            rows.Add(new DataRow(2).Set("ease", Eases[i].ToString()).Set("target", _amplitudes[i]));
        return rows;
    }

    /// <summary>Deterministic amplitudes (0.35 .. 0.94) so the demo always looks the same.</summary>
    private void FillAmplitudes(int generation)
    {
        for (int i = 0; i < _amplitudes.Length; i++)
            _amplitudes[i] = 0.35 + (i * 13 + generation * 29) % 60 / 100.0;
    }

    // ── Custom mark: the animation fields no built-in mark consumes ─────────

    /// <summary>
    /// Bar mark that draws one bar per row and takes its height from <see cref="Ease.Apply"/>: a
    /// custom mark is what can read the whole <see cref="AnimationContext"/>, including the two members
    /// no built-in mark consumes yet - <see cref="AnimationContext.SeriesProgress"/> (per-series entry
    /// progress, so each curve can start at its own time) and
    /// <see cref="AnimationContext.DataTransitionProgress"/> (the lerp between the values before and
    /// after a data change).
    /// <para>
    /// Height = <c>Ease.Apply(entry, type) * lerp(From, row target, DataTransitionProgress)</c>, times
    /// the exit factor; opacity is <see cref="MarkContext"/>'s element opacity, which already folds in
    /// <see cref="AnimationContext.GlobalOpacity"/>.
    /// </para>
    /// </summary>
    private sealed class EaseCurveMark : Mark
    {
        /// <summary>The seven <see cref="EaseType"/> members by name; the row field spells them.</summary>
        private static readonly Dictionary<string, EaseType> EaseByName = BuildEaseLookup();

        /// <summary>Field naming the easing curve of a row.</summary>
        private string EaseField { get; } = "ease";

        /// <summary>Field holding the amplitude (0..1) the bar animates to.</summary>
        private string TargetField { get; } = "target";

        /// <summary>
        /// Amplitudes the bars start a data transition from - the values that were on screen before the
        /// change. A real host keeps that snapshot itself, because no built-in mark does.
        /// </summary>
        public double[] From { get; set; } = [];

        /// <inheritdoc />
        public override MarkCoordinate Coordinate => MarkCoordinate.Cartesian;

        private static Dictionary<string, EaseType> BuildEaseLookup()
        {
            var lookup = new Dictionary<string, EaseType>(StringComparer.Ordinal);
            foreach (var ease in Eases)
                lookup[ease.ToString()] = ease;
            return lookup;
        }

        /// <inheritdoc />
        public override void Render(MarkContext ctx)
        {
            if (ctx.Scales.TryGet(Channel.X) is not OrdinalScale categories || categories.Domain.Count == 0)
                return;

            var yScale = GetYScale(ctx);
            float baseline = ctx.Plot.MapY(0f);   // the domain is pinned to 0..1, so zero is the plot bottom
            float slot = ctx.Plot.Width / categories.Domain.Count;
            // Every element may read the hover scale; the built-in marks use it for the hovered element,
            // this mark for all bars, so the tween is visible without a pointer on an element.
            float barWidth = slot * 0.6f * ctx.Animation.HoverScale;
            float exit = 1f - ctx.Animation.ExitProgress;
            float[] seriesProgress = ctx.Animation.SeriesProgress;
            Color[]? palette = ctx.Theme?.Palette;

            for (int i = 0; i < ctx.Data.Count; i++)
            {
                var row = ctx.Data[i];
                if (!HasFields(row, EaseField, TargetField)) continue;
                string? name = GetStringOrNull(row, EaseField);
                if (name is null || !EaseByName.TryGetValue(name, out var ease)) continue;

                // Per-series progress wins when the controller staggered the series; the scalar entry
                // progress is the fallback (both come out of the same AnimationContext).
                float entry = i < seriesProgress.Length ? seriesProgress[i] : ctx.AnimationProgress;

                double target = GetDouble(row, TargetField);
                double from = i < From.Length ? From[i] : target;
                double amplitude = from + (target - from) * ctx.Animation.DataTransitionProgress;

                // Clamped: EaseOutBack and EaseOutElastic overshoot 1 on purpose.
                float value = (float)(amplitude * Ease.Apply(entry, ease)) * exit;
                float top = yScale is null
                    ? baseline - ctx.Plot.Height * value
                    : ctx.Plot.MapY(Mathf.Clamp((float)yScale.Map(value), 0f, 1f));

                // One palette colour per curve (the theme is the single source of the palette): the
                // built-in helpers then add the hover brightening and any declared state style.
                Color fallback = palette is { Length: > 0 }
                    ? palette[i % palette.Length]
                    : GetDefaultColor(ctx);
                var color = ResolveFill(ctx, row, i, fallback);
                var path = ShapePath(ctx);
                var paint = ShapePaint(ctx);
                path.RoundRect(ctx.Plot.MapX((float)categories.Map(name)) - barWidth * 0.5f,
                               top, barWidth, baseline - top, 3f);
                paint.SetColor(color).SetAntiAlias(true).SetOpacity(ComputeElementOpacity(ctx, row, i));
                ctx.Canvas.Fill(path, paint);
            }
        }
    }
}
