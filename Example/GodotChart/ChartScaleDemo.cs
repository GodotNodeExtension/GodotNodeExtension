using System;
using System.Collections.Generic;
using Godot;
using GodotNodeExtension.Component.GodotChart;
using GodotNodeExtension.Component.GodotChart.Canvas;
using GodotNodeExtension.Component.GodotChart.Marks;

namespace GodotNodeExtension.Example.GodotChart;

/// <summary>
/// Scale gallery: every <see cref="IScale"/> implementation in the library, one hand-built
/// <see cref="Chart"/> per cell (no <c>ChartView</c> anywhere).
/// <para>
/// Covered here: <see cref="TimeScale"/>, <see cref="OrdinalScale"/>, <see cref="ColorScale"/>,
/// <see cref="IdentityColorScale"/>, <see cref="SequentialColorScale"/>, <see cref="DivergingColorScale"/>,
/// <see cref="ShapeScale"/>, <see cref="BandScale"/> and <see cref="RadialScale"/>.
/// <b>BandScale and RadialScale are not consumed by any built-in mark</b> - the built-in bar mark wants an
/// <see cref="OrdinalScale"/> on its category axis and the polar marks compute their own radii - so they are
/// consumed by the two custom marks below, which is what they exist for.
/// </para>
/// <para>
/// A scale installed with <see cref="Chart.Scale"/> is never fitted by the chart (only the scales the chart
/// infers itself are), so the demo fits every one of them: in code with <see cref="IScale.Fit"/>, or inside
/// the consuming mark through <see cref="Mark.ContributeScales"/>. The raw-canvas, streaming and disposal
/// part of the API is on the ChartCanvasDemo page, the mark/encode part on ChartCustomizationDemo.
/// </para>
/// </summary>
public partial class ChartScaleDemo : Control
{
    private readonly List<Chart> _charts = [];
    private bool _canvasMissing;

    // ── Custom marks for the two scales no built-in mark consumes ────────────

    /// <summary>
    /// Grouped-bar mark driven by <see cref="BandScale"/>: the scale gives every category a band and every
    /// series a sub-band inside it. The mark fits the scale itself (through
    /// <see cref="Mark.ContributeScales"/>) and counts the sub-bands from the colour channel, because a
    /// scale the demo installs is never fitted by the chart.
    /// </summary>
    private sealed class BandColumnMark : Mark
    {
        private readonly Dictionary<string, int> _seriesIndex = [];

        /// <inheritdoc />
        public override MarkCoordinate Coordinate => MarkCoordinate.Cartesian;

        /// <inheritdoc />
        public override void ContributeScales(ScaleSet scales, EncodeSet encodes, List<DataRow> data)
        {
            if (scales.TryGet(Channel.X) is not BandScale band) return;

            band.Fit(ScaleValues(encodes, data, Channel.X));

            _seriesIndex.Clear();
            foreach (var row in data)
            {
                string key = encodes.Resolve(Channel.Color, row)?.ToString() ?? string.Empty;
                if (!_seriesIndex.TryGetValue(key, out _)) _seriesIndex[key] = _seriesIndex.Count;
            }
            band.SubBandCount = Math.Max(1, _seriesIndex.Count);
        }

        /// <inheritdoc />
        public override void Render(MarkContext ctx)
        {
            if (ctx.Scales.TryGet(Channel.X) is not BandScale band) return;
            if (GetYScale(ctx) is not LinearScale yScale || band.Domain.Count == 0) return;

            float slot = ctx.Plot.Width * (float)band.SubBandWidth * 0.9f;
            float zero = ctx.Plot.MapY(Math.Clamp(yScale.Map(0), 0.0, 1.0));
            var path = ShapePath(ctx);
            var paint = ShapePaint(ctx);

            for (int i = 0; i < ctx.Data.Count; i++)
            {
                var row = ctx.Data[i];
                var category = ResolveEncode(ctx, Channel.X, row);
                var valueRaw = ResolveEncode(ctx, Channel.Y, row);
                if (category is null || valueRaw is null) continue;
                double value = ToDouble(valueRaw, "Y");
                if (!double.IsFinite(value)) continue;

                string key = ResolveEncode(ctx, Channel.Color, row)?.ToString() ?? string.Empty;
                int sub = _seriesIndex.GetValueOrDefault(key);
                double center = band.MapSubBand(category, sub);
                float cx = ctx.Plot.MapX(center);
                float top = ctx.Plot.MapY(yScale.Map(value));
                if (!float.IsFinite(cx) || !float.IsFinite(top)) continue;

                path.Reset().RoundRect(cx - slot * 0.5f, MathF.Min(top, zero), slot,
                                       MathF.Abs(zero - top), 2f);
                paint.SetColor(ResolveFill(ctx, row, i, GetDefaultColor(ctx)))
                     .SetAntiAlias(true).SetOpacity(ComputeElementOpacity(ctx, row, i));
                ctx.Canvas.Fill(path, paint);
            }

            // BandScale is no tick-producing scale, so this mark labels the categories it laid out itself.
            using var textPaint = ctx.Canvas.CreatePaint();
            textPaint.SetColor(ctx.Theme?.LabelColor ?? new Color(1f, 1f, 1f, 0.6f));
            var font = ThemedFont(ctx, new FontSettings { Size = 11f, Align = TextAlign.Center });
            float labelY = ctx.Plot.Y + ctx.Plot.Height + 14f;
            foreach (var category in band.Domain)
                DrawTextCentered(ctx, textPaint, category, ctx.Plot.MapX(band.Map(category)), labelY, font);
        }
    }

    /// <summary>
    /// Polar "spoke" mark driven by <see cref="RadialScale"/>: the scale turns the category into an angle
    /// and the Y scale into the distance from the centre. It is a <see cref="MarkCoordinate.Polar"/> chart,
    /// so the chart skips the Cartesian grid and axes, and the mark draws its own spokes.
    /// </summary>
    private sealed class PetalMark : Mark
    {
        /// <summary>Radius of the dot at the end of each spoke, in pixels.</summary>
        private float DotRadius { get; } = 6f;

        /// <inheritdoc />
        public override MarkCoordinate Coordinate => MarkCoordinate.Polar;

        /// <inheritdoc />
        public override void ContributeScales(ScaleSet scales, EncodeSet encodes, List<DataRow> data)
        {
            if (scales.TryGet(Channel.X) is RadialScale radial)
                radial.Fit(ScaleValues(encodes, data, Channel.X));
        }

        /// <inheritdoc />
        public override void Render(MarkContext ctx)
        {
            if (ctx.Scales.TryGet(Channel.X) is not RadialScale radial) return;
            var yScale = ctx.Scales.TryGet(Channel.Y);
            if (yScale is null) return;

            float cx = ctx.Plot.X + ctx.Plot.Width * 0.5f;
            float cy = ctx.Plot.Y + ctx.Plot.Height * 0.5f;
            float radius = MathF.Min(ctx.Plot.Width, ctx.Plot.Height) * 0.42f;
            var path = ShapePath(ctx);
            var paint = ShapePaint(ctx);
            paint.SetAntiAlias(true).SetStrokeWidth(1.5f);

            for (int i = 0; i < ctx.Data.Count; i++)
            {
                var row = ctx.Data[i];
                var axis = ResolveEncode(ctx, Channel.X, row);
                var valueRaw = ResolveEncode(ctx, Channel.Y, row);
                if (axis is null || valueRaw is null) continue;
                double norm = yScale.Map(valueRaw);
                if (!double.IsFinite(norm)) continue;

                // RadialScale.Map returns [0, 1) per category: multiply by 2π for radians.
                float angle = (float)(radial.Map(axis) * Math.Tau) - MathF.PI / 2f;
                float reach = radius * (float)Math.Clamp(norm, 0.0, 1.0);

                paint.SetColor(new Color(1f, 1f, 1f, 0.15f));
                ctx.Canvas.DrawLine(cx, cy, cx + MathF.Cos(angle) * radius, cy + MathF.Sin(angle) * radius, paint);

                path.Reset().Circle(cx + MathF.Cos(angle) * reach, cy + MathF.Sin(angle) * reach, DotRadius);
                paint.SetColor(ResolveFill(ctx, row, i, GetDefaultColor(ctx)))
                     .SetOpacity(ComputeElementOpacity(ctx, row, i));
                ctx.Canvas.Fill(path, paint);
            }
        }
    }

    // ── Scale fitting helpers ────────────────────────────────────────────────

    /// <summary>Values of one field across the rows; rows without that field are skipped.</summary>
    private static List<object> ValuesOf(List<DataRow> rows, string field)
    {
        var values = new List<object>();
        foreach (var row in rows)
        {
            if (row.Has(field) && row.Get(field) is { } value) values.Add(value);
        }
        return values;
    }

    /// <summary>Values of one channel across the rows, for a mark that fits the scale it consumes.</summary>
    private static List<object> ScaleValues(EncodeSet encodes, List<DataRow> data, Channel channel)
    {
        var values = new List<object>(data.Count);
        foreach (var row in data)
        {
            if (encodes.Resolve(channel, row) is { } value) values.Add(value);
        }
        return values;
    }

    // ── Scene setup ──────────────────────────────────────────────────────────

    // Cell views, wired in ChartScaleDemo.tscn (node_paths + NodePath): the margin container, the header, the
    // grid and the caption of every cell live in the scene - this script only builds each cell's chart.

    /// <summary>First cell's canvas: the <c>TimeScale</c> + <c>SequentialColorScale</c> chart.</summary>
    [Export] public Canvas2DControl TimeView { get; set; } = null!;

    /// <summary>Second cell's canvas: the ordinal, colour and shape scale chart.</summary>
    [Export] public Canvas2DControl OrdinalView { get; set; } = null!;

    /// <summary>Third cell's canvas: the identity colour scale chart.</summary>
    [Export] public Canvas2DControl IdentityView { get; set; } = null!;

    /// <summary>Fourth cell's canvas: the diverging colour scale chart.</summary>
    [Export] public Canvas2DControl DivergingView { get; set; } = null!;

    /// <summary>Fifth cell's canvas: the band scale chart, drawn by the custom bar mark.</summary>
    [Export] public Canvas2DControl BandView { get; set; } = null!;

    /// <summary>Sixth cell's canvas: the radial scale chart, drawn by the custom petal mark.</summary>
    [Export] public Canvas2DControl RadialView { get; set; } = null!;

    /// <summary>Shown when there is no rendering device; the scene keeps it hidden.</summary>
    [Export] public Label NoCanvasNote { get; set; } = null!;

    /// <inheritdoc />
    public override void _Ready()
    {
        BuildCell(TimeView, canvas =>
        {
            var rows = TimeRows();
            var time = new TimeScale();
            time.Fit(ValuesOf(rows, "time"));
            var ramp = new SequentialColorScale();
            ramp.Fit(ValuesOf(rows, "load"));

            var chart = new Chart(canvas) { Width = 340f, Height = 220f };
            chart.Theme(ChartTheme.Dark());
            chart.Data(rows);
            chart.Encode(Channel.X, "time");
            chart.Encode(Channel.Y, "load");
            chart.Scale(Channel.X, time);
            chart.Scale(Channel.Color, ramp);
            chart.Mark(new LineMark { YChannel = Channel.Y });
            // The mark-level colour binding keeps the line in one series colour while the points take their
            // colour from the continuous ramp - one Color scale, two consumers.
            var points = new PointMark { DefaultRadius = 5f };
            points.Encode(Channel.Color, "load");
            chart.Mark(points);
            return chart;
        });

        BuildCell(OrdinalView, canvas =>
        {
            var rows = SeriesRows();
            var categories = new OrdinalScale();
            categories.Fit(ValuesOf(rows, "quarter"));
            var colors = new ColorScale { Palette = [new Color(0.35f, 0.7f, 1f), new Color(0.98f, 0.6f, 0.35f)] };
            colors.Fit(ValuesOf(rows, "series"));
            var shapes = new ShapeScale { Shapes = [ShapeKind.Circle, ShapeKind.Diamond] };
            shapes.Fit(ValuesOf(rows, "series"));

            var chart = new Chart(canvas) { Width = 340f, Height = 220f };
            chart.Theme(ChartTheme.Dark());
            chart.Data(rows);
            chart.Encode(Channel.X, "quarter");
            chart.Encode(Channel.Y, "value");
            chart.Encode(Channel.Color, "series");
            chart.Encode(Channel.Shape, "series");
            chart.Scale(Channel.X, categories);
            chart.Scale(Channel.Color, colors);
            chart.Scale(Channel.Shape, shapes);
            chart.Mark(new IntervalMark
            {
                Stack = StackMode.Stack,
                ShowLabel = true,                  // LabelFormat defaults to "{0}", the value
                LabelPosition = LabelPosition.Inside,
            });
            chart.Legend(new LegendConfig { Position = LegendPosition.Bottom, SwatchSize = 9f });
            return chart;
        });

        BuildCell(IdentityView, canvas =>
        {
            var rows = IdentityRows();
            var identity = new IdentityColorScale();
            identity.Fit(ValuesOf(rows, "color"));

            var chart = new Chart(canvas) { Width = 340f, Height = 220f };
            chart.Theme(ChartTheme.Dark());
            chart.Data(rows);
            chart.Encode(Channel.X, "x");
            chart.Encode(Channel.Y, "y");
            chart.Encode(Channel.Color, "color");
            chart.Scale(Channel.Color, identity);
            chart.Mark(new PointMark { DefaultRadius = 10f });
            return chart;
        });

        BuildCell(DivergingView, canvas =>
        {
            var rows = DeltaRows();
            var chart = new Chart(canvas) { Width = 340f, Height = 220f };
            chart.Theme(ChartTheme.Dark());
            chart.Data(rows);
            chart.Encode(Channel.X, "x");
            chart.Encode(Channel.Y, "y");
            chart.Encode(Channel.Color, "delta");
            // A fixed domain needs no Fit: the two ends of the ramp are known up front.
            chart.Scale(Channel.Color, new DivergingColorScale(-1, 1));
            chart.Mark(new PointMark { DefaultRadius = 10f });
            return chart;
        });

        BuildCell(BandView, canvas =>
        {
            var rows = BandRows();
            var bands = new BandScale { Padding = 0.3f, InnerPadding = 0.15f };

            var chart = new Chart(canvas) { Width = 340f, Height = 220f };
            chart.Theme(ChartTheme.Dark());
            chart.Data(rows);
            chart.Encode(Channel.X, "product");
            chart.Encode(Channel.Y, "sales");
            chart.Encode(Channel.Color, "region");
            chart.Scale(Channel.X, bands);          // fitted by BandColumnMark.ContributeScales
            chart.Mark(new BandColumnMark());
            return chart;
        });

        BuildCell(RadialView, canvas =>
        {
            var rows = SpokeRows();
            var chart = new Chart(canvas) { Width = 340f, Height = 220f };
            chart.Theme(ChartTheme.Dark());
            chart.Data(rows);
            chart.Encode(Channel.X, "axis");
            chart.Encode(Channel.Y, "score");
            chart.Scale(Channel.X, new RadialScale());            // fitted by PetalMark.ContributeScales
            chart.Scale(Channel.Y, new LinearScale(0, 100));      // fixed domain: 0..100 points
            chart.Mark(new PetalMark());
            return chart;
        });

        // No rendering device (a headless run): report it once instead of drawing nothing.
        if (_canvasMissing) NoCanvasNote.Visible = true;
    }

    /// <summary>
    /// Build the chart of one gallery cell into the canvas its <see cref="Canvas2DControl"/> hosts. The cell,
    /// its caption and the view come from the scene; only what needs the live canvas is created here.
    /// </summary>
    private void BuildCell(Canvas2DControl view, Func<ICanvas2D, Chart> build)
    {
        if (view.Canvas is not { } canvas)
        {
            // No rendering device (a headless run): _Ready reports it once, at the end.
            _canvasMissing = true;
            return;
        }

        // Each chart is owned by this demo (ownsCanvas: false) and released in _ExitTree; the canvas belongs
        // to the view, which disposes it when it leaves the tree.
        var chart = build(canvas);
        _charts.Add(chart);
        view.CanvasDraw += (control, _) =>
        {
            chart.Width = Mathf.Max(1f, control.CanvasSize.X);
            chart.Height = Mathf.Max(1f, control.CanvasSize.Y);
            chart.Render();
        };
        view.Invalidate();
    }

    // ── Data ─────────────────────────────────────────────────────────────────

    /// <summary>A sampled load curve, stamped with real dates so TimeScale can format the axis.</summary>
    private static List<DataRow> TimeRows()
    {
        DateTime start = new(2024, 3, 1, 8, 0, 0, DateTimeKind.Unspecified);
        double[] load = [42.0, 58.0, 51.0, 76.0, 64.0, 88.0];
        var rows = new List<DataRow>(load.Length);
        for (int i = 0; i < load.Length; i++)
            rows.Add(Row(("time", start.AddHours(i * 3)), ("load", load[i])));
        return rows;
    }

    /// <summary>Two series over four quarters, for the categorical scales and the stacked bars.</summary>
    private static List<DataRow> SeriesRows()
    {
        string[] quarters = ["Q1", "Q2", "Q3", "Q4"];
        double[][] values =
        [
            [62.0, 74.0, 58.0, 91.0],
            [48.0, 55.0, 67.0, 72.0],
        ];
        var rows = new List<DataRow>();
        for (int s = 0; s < values.Length; s++)
            for (int q = 0; q < quarters.Length; q++)
                rows.Add(Row(("quarter", quarters[q]), ("series", s == 0 ? "plan" : "actual"),
                             ("value", values[s][q])));
        return rows;
    }

    /// <summary>Points whose own colour is part of the data (the identity colour scale).</summary>
    private static List<DataRow> IdentityRows() =>
    [
        Row(("x", 1.0), ("y", 2.4), ("color", "#4fc3f7")),
        Row(("x", 2.0), ("y", 3.1), ("color", "#81c784")),
        Row(("x", 3.0), ("y", 1.7), ("color", "#ffb74d")),
        Row(("x", 4.0), ("y", 4.2), ("color", "#e57373")),
    ];

    /// <summary>Signed deltas around a meaningful midpoint: what the diverging scale is for.</summary>
    private static List<DataRow> DeltaRows() =>
    [
        Row(("x", -2.0), ("y", 1.0), ("delta", -0.9)),
        Row(("x", -1.0), ("y", 2.2), ("delta", -0.35)),
        Row(("x", 0.0), ("y", 1.6), ("delta", 0.0)),
        Row(("x", 1.0), ("y", 3.0), ("delta", 0.45)),
        Row(("x", 2.0), ("y", 2.6), ("delta", 0.85)),
    ];

    /// <summary>Three products with three regional series each: the shape the BandScale lays out.</summary>
    private static List<DataRow> BandRows()
    {
        string[] products = ["A", "B", "C"];
        string[] regions = ["north", "south", "east"];
        double[][] sales =
        [
            [12.0, 18.0, 9.0],
            [8.0, 14.0, 21.0],
            [16.0, 7.0, 13.0],
        ];
        var rows = new List<DataRow>();
        for (int p = 0; p < products.Length; p++)
            for (int r = 0; r < regions.Length; r++)
                rows.Add(Row(("product", products[p]), ("region", regions[r]), ("sales", sales[p][r])));
        return rows;
    }

    /// <summary>Scores per axis; RadialScale gives every one of them the same slice of the circle.</summary>
    private static List<DataRow> SpokeRows() =>
    [
        Row(("axis", "aim"), ("score", 82.0)),
        Row(("axis", "mobility"), ("score", 54.0)),
        Row(("axis", "armor"), ("score", 71.0)),
        Row(("axis", "range"), ("score", 38.0)),
        Row(("axis", "reload"), ("score", 63.0)),
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
        // Every chart of this page is released here (ownsCanvas: false), while the canvases belong to their
        // Canvas2DControl hosts and are disposed when those leave the tree.
        foreach (var chart in _charts) chart.Dispose();
        _charts.Clear();
    }
}
