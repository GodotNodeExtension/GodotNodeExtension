using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;
using GodotNodeExtension.Component.GodotChart;
using GodotNodeExtension.Component.GodotChart.Canvas;
using GodotNodeExtension.Component.GodotChart.Marks;

namespace GodotNodeExtension.Example.GodotChart;

/// <summary>
/// Customization showcase: the library without <c>ChartView</c> - hand-built <see cref="Chart"/>s drawn
/// into <see cref="Canvas2DControl"/>s by the demo itself. Custom <see cref="Mark"/> subclasses (with the
/// mark-level encodes, <see cref="Mark.Data"/>, <see cref="Mark.States"/>, <see cref="Mark.StyleOverride"/>
/// and the label bindings), a custom <see cref="IDataTransform"/>, the built-in <see cref="BinTransform"/>
/// (a data transform, so it needs a hand-built chart) and the full <see cref="Channel.Y2"/> chain. The state
/// styles configured here are driven by the pointer on the ChartCanvasDemo page; the seven renderer slots are
/// ChartRendererDemo's subject.
/// </summary>
public partial class ChartCustomizationDemo : Control
{
    private Chart? _mainChart;
    private Chart? _histogramChart;
    private double _entryClock;      // drives Chart.Animate (the entry animation)

    // ── Custom marks ─────────────────────────────────────────────────────────

    /// <summary>
    /// Error-bar mark: a custom mark is a <see cref="Mark"/> subclass drawing with the canvas API. Rows come
    /// from the encodes, the colour from <see cref="Mark.ResolveFill"/>, so the mark-level constant colour
    /// wins while the hover / selection states keep working.
    /// </summary>
    private sealed class ErrorBarMark : Mark
    {
        /// <inheritdoc />
        public override MarkCoordinate Coordinate => MarkCoordinate.Cartesian;

        /// <inheritdoc />
        public override void Render(MarkContext ctx)
        {
            var xScale = ctx.Scales.TryGet(Channel.X);
            var yScale = GetYScale(ctx);
            if (xScale is null || yScale is null) return;

            const string errorField = "error";
            const float cap = 6f;
            var path = ShapePath(ctx);
            var paint = ShapePaint(ctx);
            paint.SetAntiAlias(true).SetStrokeWidth(2f).SetLineCap(LineCap.Round);

            for (int i = 0; i < ctx.Data.Count; i++)
            {
                var row = ctx.Data[i];
                if (IsSeriesHidden(ctx, row)) continue;
                var xRaw = ResolveEncode(ctx, Channel.X, row);
                var yRaw = ResolveY(ctx, row);
                if (xRaw is null || yRaw is null || !HasField(row, errorField)) continue;

                double y = ToDouble(yRaw, "Y"), error = GetDouble(row, errorField);
                if (!double.IsFinite(y) || !double.IsFinite(error)) continue;

                // A non-finite mapping has no screen position: skip the element instead of drawing at NaN.
                float xNorm = (float)xScale.Map(xRaw), upper = (float)yScale.Map(y + error);
                float lower = (float)yScale.Map(y - error);
                if (!float.IsFinite(xNorm) || !float.IsFinite(upper) || !float.IsFinite(lower)) continue;

                float cx = ctx.Plot.MapX(xNorm), top = ctx.Plot.MapY(upper), bottom = ctx.Plot.MapY(lower);
                paint.SetColor(ResolveFill(ctx, row, i, GetDefaultColor(ctx)))
                     .SetOpacity(ComputeElementOpacity(ctx, row, i));

                path.Reset()
                    .MoveTo(cx, top).LineTo(cx, bottom)
                    .MoveTo(cx - cap, top).LineTo(cx + cap, top)
                    .MoveTo(cx - cap, bottom).LineTo(cx + cap, bottom);
                ctx.Canvas.Stroke(path, paint);
            }
        }
    }

    /// <summary>
    /// Bar mark that consumes the animation, state and style API: the height follows
    /// <see cref="MarkContext.AnimationProgress"/> (entry times inverse exit),
    /// <see cref="AnimationContext.GlobalOpacity"/> fades the fill in,
    /// <see cref="AnimationContext.HoverScale"/> widens the hovered bar, and <see cref="Mark.States"/> /
    /// <see cref="Mark.StyleOverride"/> style it. It draws its own labels, so
    /// <see cref="Mark.ShowLabel"/> / <see cref="Mark.LabelFormat"/> / <see cref="Mark.LabelPosition"/> are
    /// exercised on a custom mark. The hovered / selected row index comes from the chart.
    /// </summary>
    private sealed class PulseBarMark : Mark
    {
        /// <summary>Bar width in pixels, before the hover scale is applied.</summary>
        public float Width { get; init; } = 10f;

        /// <inheritdoc />
        public override MarkCoordinate Coordinate => MarkCoordinate.Cartesian;

        /// <inheritdoc />
        public override void Render(MarkContext ctx)
        {
            var xScale = ctx.Scales.TryGet(Channel.X);
            var yScale = GetYScale(ctx);
            if (xScale is null || yScale is null) return;

            float reveal = ComputeAnimProgress(ctx);      // entry * (1 - exit)
            List<LabelElement>? labels = BeginLabelCollection();
            var path = ShapePath(ctx);
            var paint = ShapePaint(ctx);

            for (int i = 0; i < ctx.Data.Count; i++)
            {
                var row = ctx.Data[i];
                if (IsSeriesHidden(ctx, row)) continue;
                var xRaw = ResolveEncode(ctx, Channel.X, row);
                var yRaw = ResolveY(ctx, row);
                if (xRaw is null || yRaw is null) continue;

                double value = ToDouble(yRaw, "Y");
                if (!double.IsFinite(value)) continue;
                float xNorm = (float)xScale.Map(xRaw), yNorm = (float)yScale.Map(value);
                if (!float.IsFinite(xNorm) || !float.IsFinite(yNorm)) continue;

                // The bar grows out of the data zero line (not out of the plot bottom), the entry animation
                // scales its height and the hovered one is widened by AnimationContext.HoverScale.
                bool active = StateOf(ctx, row, i) == ElementState.Active;
                float width = Width * (active ? ctx.Animation.HoverScale : 1f);
                float cx = ctx.Plot.MapX(xNorm);
                float zero = ctx.Plot.MapY(Math.Clamp(yScale.Map(0), 0.0, 1.0));
                float top = ctx.Plot.MapY(yNorm * reveal);
                float opacity = ComputeElementOpacity(ctx, row, i);

                path.Reset()
                    .RoundRect(cx - width * 0.5f, MathF.Min(top, zero), width, MathF.Abs(zero - top), 2f);
                paint.SetColor(ResolveFill(ctx, row, i, GetDefaultColor(ctx)))
                     .SetAntiAlias(true).SetOpacity(opacity);
                ctx.Canvas.Fill(path, paint);

                // Selected: the theme's ring, overridable through States.SelectedStroke / width.
                if (StateOf(ctx, row, i) == ElementState.Selected)
                {
                    ApplySelectionPaint(ctx, paint, opacity);
                    ctx.Canvas.Stroke(path, paint);
                }

                labels?.Add(new LabelElement(cx, MathF.Min(top, zero),
                    FormatLabel(LabelFormat, yRaw, xRaw), opacity));
            }

            if (labels is not null) DrawLabels(ctx, labels);
        }
    }

    /// <summary>
    /// Mark with its own rows (<see cref="Mark.Data"/>): it draws the benchmark levels as dashed rules
    /// through the shared Y scale and never touches the chart's data. The rows are handed over by
    /// <see cref="Chart.ApplyToAllMarks"/> in <c>_Ready</c>.
    /// </summary>
    private sealed class BenchmarkRuleMark : Mark
    {
        private static readonly float[] Dash = [7f, 5f];

        /// <inheritdoc />
        public override MarkCoordinate Coordinate => MarkCoordinate.Cartesian;

        /// <inheritdoc />
        public override void Render(MarkContext ctx)
        {
            var yScale = GetYScale(ctx);
            if (yScale is null) return;

            var rows = Data ?? ctx.Data;            // Mark.Data; the chart's rows are only the fallback
            var path = ShapePath(ctx);
            var paint = ShapePaint(ctx);
            paint.SetColor(new Color(1f, 0.86f, 0.4f, 0.75f)).SetStrokeWidth(1.5f);
            if (ctx.Canvas.Capabilities.SupportsLineDash) paint.SetLineDash(Dash);

            foreach (var row in rows)
            {
                if (!HasField(row, "level")) continue;
                double level = GetDouble(row, "level");
                float norm = (float)yScale.Map(level);
                if (!double.IsFinite(level) || !float.IsFinite(norm)) continue;

                float y = ctx.Plot.MapY(norm);
                path.Reset().MoveTo(ctx.Plot.X, y).LineTo(ctx.Plot.X + ctx.Plot.Width, y);
                ctx.Canvas.Stroke(path, paint);
            }
        }
    }

    /// <summary>
    /// Custom <see cref="IDataTransform"/>: formats the bins <see cref="BinTransform"/> produced into the
    /// labels the category axis shows. An ordinal axis keys categories by the <i>text</i> of the value the
    /// mark reads, so the readable label has to live in a field of its own - formatting the value in place
    /// would rename the category and no bar would land on it.
    /// </summary>
    private sealed class BinLabelTransform : IDataTransform
    {
        /// <summary>Field holding the bin midpoint the label is derived from.</summary>
        public string SourceField { get; init; } = "BinMid";

        /// <summary>Field the formatted label is written to.</summary>
        public string LabelField { get; init; } = "bin";

        /// <inheritdoc />
        public List<DataRow> Apply(List<DataRow> data)
        {
            var result = new List<DataRow>(data.Count);

            foreach (var row in data)
            {
                // Rows are copied field by field: a transform must not mutate the caller's rows.
                var copy = new DataRow(row.Fields.Count + 1);
                foreach (var (field, value) in row.Fields) copy.Set(field, value);
                double mid = row.TryGet<double>(SourceField, out var midpoint) ? midpoint : 0.0;
                copy.Set(LabelField, $"{mid:F0}");
                result.Add(copy);
            }
            return result;
        }
    }

    /// <summary>
    /// Custom <see cref="IDataTransform"/>: scales the amplitude and error columns from volts to microvolts
    /// and stamps a <c>unit</c> column on every row, so the axis can name what the numbers mean. Transforms
    /// run before the scales are fitted and are cached until the data or the transform list changes.
    /// </summary>
    private sealed class MilliUnitTransform : IDataTransform
    {
        /// <summary>Unit the transformed amplitude is expressed in (the demo reads it for the axis title).</summary>
        public const string ScaledUnit = "uV";

        /// <inheritdoc />
        public List<DataRow> Apply(List<DataRow> data)
        {
            const double factor = 1000.0;
            var result = new List<DataRow>(data.Count);

            foreach (var row in data)
            {
                // Rows are copied field by field: a transform must not mutate the caller's rows.
                var copy = new DataRow(row.Fields.Count + 1);
                foreach (var (field, value) in row.Fields) copy.Set(field, value);
                Scale(copy, row, "value", factor);
                Scale(copy, row, "error", factor);
                copy.Set("unit", ScaledUnit);
                result.Add(copy);
            }
            return result;
        }

        /// <summary>Copy one numeric column scaled by <paramref name="factor"/>; a missing column stays missing.</summary>
        private static void Scale(DataRow target, DataRow source, string field, double factor)
        {
            if (!source.Has(field) || source.Get(field) is not { } raw) return;
            target.Set(field, Convert.ToDouble(raw, CultureInfo.InvariantCulture) * factor);
        }
    }

    // ── Scene setup ──────────────────────────────────────────────────────────

    // Wired in ChartCustomizationDemo.tscn (node_paths + NodePath): the container skeleton, the heading and
    // the cell live in the scene - this script only builds the chart into the canvas the view hosts.

    /// <summary>Canvas the hand-built chart is drawn into.</summary>
    [Export] public Canvas2DControl MainView { get; set; } = null!;

    /// <summary>Canvas the histogram cell is drawn into.</summary>
    [Export] public Canvas2DControl HistogramView { get; set; } = null!;

    /// <summary>Shown when there is no rendering device; the scene keeps it hidden.</summary>
    [Export] public Label NoCanvasNote { get; set; } = null!;

    /// <inheritdoc />
    public override void _Ready()
    {
        BuildMainChart();
        BuildHistogramChart();
    }

    /// <summary>
    /// The one hand-built chart of the page: two value axes, five marks, a custom transform and the default
    /// renderer slots. Only what needs the live canvas is created here.
    /// </summary>
    private void BuildMainChart()
    {
        if (MainView.Canvas is not { } canvas)
        {
            // No rendering device (a headless run): say so instead of drawing nothing.
            NoCanvasNote.Visible = true;
            return;
        }

        // The view owns the canvas it hosts (OwnsCanvas defaults to true) while the chart BuildChart returns
        // keeps ownsCanvas: false - two owners would dispose the same canvas twice (see _ExitTree).
        var chart = BuildChart(canvas);     // build before wiring the redraw: the delegate captures the chart
        _mainChart = chart;
        MainView.CanvasDraw += (control, _) =>
        {
            chart.Width = Mathf.Max(1f, control.CanvasSize.X);
            chart.Height = Mathf.Max(1f, control.CanvasSize.Y);
            chart.Render();
        };
        MainView.Invalidate();
    }

    /// <summary>
    /// The page's second chart: a histogram, which is a data transform plus a plain bar mark. The transform
    /// runs before the scales are fitted, so the axes see the binned range, and the mark then reads the
    /// columns the transform added (<c>BinMid</c> / <c>Count</c>) instead of the raw sample column.
    /// </summary>
    private void BuildHistogramChart()
    {
        if (HistogramView.Canvas is not { } canvas) return;

        var chart = new Chart(canvas)     // ownsCanvas: false - the view owns this canvas
        {
            Width = 640f,
            Height = 300f,
            Title = "Sword damage - BinTransform(Field = value, BinCount = 12) feeding an IntervalMark",
        };
        chart.Theme(ChartTheme.Dark());

        chart.Transform(new BinTransform { Field = "value", BinCount = 12 });
        chart.Transform(new BinLabelTransform());   // readable category keys (see the class comment)
        chart.Data(DamageSamples());

        chart.Encode(Channel.X, "bin");           // the label the second transform wrote
        chart.Encode(Channel.Y, "Count");         // a column BinTransform added

        // The mark draws bars on category bands, so the numeric bins need an ordinal axis - and a scale
        // installed by hand is never fitted by the chart (only the inferred ones are), so the categories are
        // fitted here, from the rows the two transforms produced. Left to itself the same column would be
        // auto-fitted to a linear axis and no bar would be drawn at all.
        var bins = new OrdinalScale();
        var binKeys = new List<object>();
        foreach (var row in chart.GetRenderDataSnapshot())
            if (row.TryGet<string>("bin", out var label))
                binKeys.Add(label);
        bins.Fit(binKeys);
        chart.Scale(Channel.X, bins);

        chart.Mark(new IntervalMark { BarPadding = 0.05f, CornerRadius = 2f });

        chart.XAxis(new AxisConfig { Title = "Damage", Unit = "hp" });
        chart.YAxis(new AxisConfig { Title = "Frequency", Unit = "hits" });

        _histogramChart = chart;
        HistogramView.CanvasDraw += (control, _) =>
        {
            chart.Width = Mathf.Max(1f, control.CanvasSize.X);
            chart.Height = Mathf.Max(1f, control.CanvasSize.Y);
            chart.Render();
        };
        HistogramView.Invalidate();
    }

    /// <summary>The chart: two value axes, five marks, a custom transform and the default renderer slots.</summary>
    private static Chart BuildChart(ICanvas2D canvas)
    {
        var chart = new Chart(canvas)     // ownsCanvas: false - the view owns this canvas
        {
            Width = 640f,
            Height = 400f,
            Title = "Signal vs frequency - ErrorBarMark, PulseBarMark and a pinned right axis",
        };
        chart.Theme(ChartTheme.Dark());

        // The transform runs before the scales are fitted: the amplitude arrives in volts, the axis has to
        // show microvolts, and every row carries the unit the axis title names.
        chart.Transform(new MilliUnitTransform());
        chart.Data(SignalRows());

        chart.Encode(Channel.X, "x");
        chart.Encode(Channel.Y, "value");
        chart.Encode(Channel.Color, "phase");                 // categorical: drives the legend
        chart.Encode(Channel.Opacity, "constant:0.9");        // the "constant:" form of the string overload
        chart.Encode(Channel.Y2, "baseline");                 // the right axis reads its own field

        chart.Scale(Channel.X, new LogScale(0.25, 10));
        chart.Scale(Channel.Y2, new LinearScale(0, 4));
        // ScaleDomain re-applies the lock after every fit, so the right axis stays comparable while the data
        // changes underneath (on a categorical channel the lock is a no-op, never an error).
        chart.ScaleDomain(Channel.Y2, 0, 4);

        // Mark 1: the animated bar mark, configured entirely on the instance.
        var pulse = new PulseBarMark
        {
            ShowLabel = true,                    // the mark collects and draws its own labels
            LabelFormat = "{0} uV",              // {0} = the value, {1} = the X value
            LabelPosition = LabelPosition.Top,
            Width = 9f,
        };
        pulse.Encode(Channel.X, "x");            // mark-level FIELD encode (string overload)
        pulse.Encode(Channel.Opacity, 0.55f);    // mark-level CONSTANT encode (object overload)
        pulse.States.ActiveFill = new Color(0.60f, 0.95f, 1f);
        pulse.States.SelectedStroke = new Color(1f, 0.92f, 0.45f);
        pulse.States.SelectedStrokeWidth = 3f;
        // StyleOverride is G2's style callback: it gets the last word over the style resolved from data.
        // Its signature is (row, index, style); this cell only reads the index (the row is the discard).
        pulse.StyleOverride = (_, index, style) =>
            index % 2 == 0 ? style.WithOpacity(style.Opacity * 0.7f) : style;
        chart.Mark(pulse);

        // Mark 2: the plain line on the left axis, for comparison with the custom mark.
        chart.Mark(new LineMark { YChannel = Channel.Y, StrokeWidth = 2f });

        // Mark 3: error bars. The mark-level constant colour keeps this mark orange whatever the colour
        // channel says, while the legend keeps listing the series.
        var errorBars = new ErrorBarMark();
        errorBars.Encode(Channel.X, "x");
        errorBars.Encode(Channel.Color, new Color(0.95f, 0.65f, 0.25f));
        chart.Mark(errorBars);

        // Mark 4: the only mark reading Channel.Y2, so the right axis has a series of its own.
        chart.Mark(new LineMark { YChannel = Channel.Y2, Smooth = false, StrokeWidth = 1.5f });

        // Mark<T>() builds a mark with its default settings; the fluent pass finishes the configuration of
        // every mark at once and gives the rule mark its own rows.
        chart.Mark<BenchmarkRuleMark>();
        chart.ApplyToAllMarks(mark =>
        {
            mark.States.InactiveOpacity = 0.12f;
            if (mark is BenchmarkRuleMark rules) rules.Data = BenchmarkRows();
        });

        chart.XAxis(new AxisConfig { Title = "Frequency", Unit = "Hz" });
        chart.YAxis(new AxisConfig { Title = "Amplitude", Unit = MilliUnitTransform.ScaledUnit });
        chart.Y2Axis(new AxisConfig
        {
            Title = "Baseline",
            Unit = "ratio",
            Description = "second value axis, fed by Channel.Y2",
        });
        chart.Legend(new LegendConfig { Position = LegendPosition.Bottom });

        // Renderer slots: every one keeps its default here - ChartRendererDemo swaps all seven of them.
        return chart;
    }

    // ── Data ─────────────────────────────────────────────────────────────────

    /// <summary>The signal table: frequency (log axis), amplitude with its error, baseline ratio, phase.</summary>
    private static List<DataRow> SignalRows() =>

    [
        Row(("x", 0.5), ("value", 3.2), ("error", 0.6), ("baseline", 1.0), ("phase", "cal")),
        Row(("x", 1.0), ("value", 9.5), ("error", 1.4), ("baseline", 1.4), ("phase", "cal")),
        Row(("x", 2.0), ("value", 24.0), ("error", 3.1), ("baseline", 1.8), ("phase", "meas")),
        Row(("x", 4.0), ("value", 68.0), ("error", 6.5), ("baseline", 2.4), ("phase", "meas")),
        Row(("x", 8.0), ("value", 190.0), ("error", 18.0), ("baseline", 3.0), ("phase", "meas")),
    ];

    /// <summary>Rows the benchmark mark draws: its own table, in its own field, inside the Y domain.</summary>
    private static List<DataRow> BenchmarkRows() =>

    [
        Row(("level", 50000.0), ("name", "target")),
        Row(("level", 150000.0), ("name", "limit")),
    ];

    /// <summary>
    /// Raw samples the histogram bins - one value per hit, deliberately clustered so the binned result has a
    /// shape to look at. Nothing here is pre-binned: <see cref="BinTransform"/> does that.
    /// </summary>
    private static List<DataRow> DamageSamples()
    {
        double[] samples =
        [
            8, 9, 10, 11, 11, 12, 12, 13, 14, 15, 16, 17,
            17, 18, 19, 20, 21, 22, 23, 24, 26, 27, 29, 31,
        ];

        var rows = new List<DataRow>(samples.Length);
        foreach (double sample in samples) rows.Add(Row(("value", sample)));
        return rows;
    }

    /// <summary>Build a row from field/value pairs, like the hand-built tables above.</summary>
    private static DataRow Row(params (string Field, object Value)[] fields)
    {
        var row = new DataRow(fields.Length);
        foreach (var (field, value) in fields) row.Set(field, value);
        return row;
    }

    // ── Frame loop ───────────────────────────────────────────────────────────

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        // Entry animation: Chart.Animate hands the marks the AnimationContext PulseBarMark reads from.
        _entryClock += delta;
        float linear = (float)Math.Clamp(_entryClock / 1.1, 0.0, 1.0);
        float progress = Ease.Apply(linear, EaseType.EaseOutCubic);
        _mainChart?.Animate(new AnimationContext
        {
            EntryProgress = progress,
            GlobalOpacity = 0.45f + 0.55f * progress,
            HoverScale = 1.35f,
        });
        if (linear < 1f) MainView.Invalidate();
    }

    /// <inheritdoc />
    public override void _Input(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false, Keycode: Key.R }) return;

        _entryClock = 0;

        // Handled here, so the GUI must not also act on it (see the class doc: _Input runs first, and the
        // example browser's tree reads a plain letter as type-ahead).
        GetViewport().SetInputAsHandled();
    }

    /// <inheritdoc />
    public override void _ExitTree()
    {
        // Dispose follows the ownership: the Canvas2DControl hosting the canvas owns it (OwnsCanvas),
        // so the chart keeps the default ownsCanvas: false - two owners would dispose it twice.
        _mainChart?.Dispose();
        _histogramChart?.Dispose();
    }
}
