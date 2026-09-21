using System.Collections.Generic;
using Godot;
using GodotNodeExtension.Component.GodotChart;
using GodotNodeExtension.Component.GodotChart.Canvas;
using GodotNodeExtension.Component.GodotChart.Marks;

namespace GodotNodeExtension.Example.GodotChart;

/// <summary>
/// The escape hatches of <see cref="ChartView"/> - everything the exports cannot express. On screen every
/// cell is one chart plus its short caption; the reasoning lives here:
/// <list type="bullet">
/// <item><see cref="ChartView.ConfigureMark"/>, invoked while the node builds the mark
/// (<see cref="IntervalMark.BarPadding"/> / <see cref="IntervalMark.CornerRadius"/>);</item>
/// <item><see cref="ChartView.Tooltip"/>: <see cref="TooltipOptions.ContentBuilder"/> for plain tooltip
/// lines and <see cref="TooltipOptions.RichContentBuilder"/> for styled ones
/// (<see cref="TooltipLine.Plain"/>, <see cref="TooltipLine.WithIcon"/>, <see cref="TooltipSpan"/> with
/// <see cref="TooltipSpan.Bold"/> / <see cref="TooltipSpan.Color"/>) next to the bubble styling
/// (<see cref="TooltipOptions.BackgroundColor"/>, <see cref="TooltipOptions.FontSize"/>,
/// <see cref="TooltipOptions.CornerRadius"/>);</item>
/// <item><see cref="ChartView.CanvasFactory"/> sharing one canvas between two views, with
/// <see cref="ChartView.Surface"/> (<see cref="Canvas2DControl.OwnsCanvas"/> decides who releases that
/// canvas), <see cref="ChartView.Canvas"/> - the interface both views draw through, read for the label next
/// to the preview - and <see cref="ChartView.Texture"/>, the one handed to a plain
/// <see cref="TextureRect"/>;</item>
/// <item><see cref="ChartView.EditorPreview"/>, the switch that only changes what the editor does.</item>
/// </list>
/// <para>
/// Why the hooks are the whole story: the node builds a fresh <see cref="Chart"/> on every rebuild (a
/// <see cref="ChartView.Refresh"/>, a changed export, a new row set), so anything hung on the previous
/// instance goes with it - a <see cref="Chart"/> event handler attached once would simply never fire
/// again. <see cref="ChartView.ConfigureChart"/> and <see cref="ChartView.ConfigureMark"/> run again for
/// every new instance, which is what makes them the place for a subscription, a hand-fitted scale or a
/// restyled mark (this page shows ConfigureMark; ConfigureChart itself is used on the ChartCallbacksDemo
/// and ChartViewFieldsDemo pages).
/// </para>
/// <para>
/// Sharing one canvas has two consequences this demo respects. A canvas has a single surface size and
/// every surface node resizes it to its own rect (<see cref="Canvas2DControl.AutoResize"/>), so views
/// sharing one must be the same size - here both are 360x280 and the last resize wins. And exactly one
/// owner must release it, so both surfaces report <see cref="Canvas2DControl.OwnsCanvas"/> = false while
/// this demo disposes the canvas it created in <see cref="_ExitTree"/>. The two views are fed the same
/// rows on purpose: they draw in sequence every frame, so different content would mean the last one to
/// draw is the one you see.
/// </para>
/// <para>
/// <see cref="ChartView.Refresh"/> versus <see cref="ChartView.Repaint"/> - rebuild against redraw - is
/// demonstrated on the <c>ChartViewFieldsDemo</c> page. <see cref="ChartView.EditorPreview"/> is the
/// editor-only switch: at runtime it changes nothing, in the editor it decides between a live surface and
/// the node's placeholder.
/// </para>
/// <para>
/// Two neighbouring lessons have demos of their own: the five <see cref="Chart"/> events with their
/// payloads and the hit testing around them are the subject of <c>ChartCallbacksDemo</c>, and the full
/// tour of the mark knobs is <c>ChartMarksCartesianDemo</c> (with the other ChartMarks demos).
/// </para>
/// </summary>
public partial class ChartViewHooksDemo : Control
{
    /// <summary>Chart whose mark is restyled through <see cref="ChartView.ConfigureMark"/>.</summary>
    [Export] public ChartView IntervalChart { get; set; } = null!;

    /// <summary>Chart using a plain-text <see cref="TooltipOptions.ContentBuilder"/>.</summary>
    [Export] public ChartView PlainTooltipChart { get; set; } = null!;

    /// <summary>Chart using a styled <see cref="TooltipOptions.RichContentBuilder"/>.</summary>
    [Export] public ChartView RichTooltipChart { get; set; } = null!;

    /// <summary>First view of the shared canvas.</summary>
    [Export] public ChartView SharedChartA { get; set; } = null!;

    /// <summary>Second view of the same shared canvas.</summary>
    [Export] public ChartView SharedChartB { get; set; } = null!;

    /// <summary>TextureRect presenting <see cref="ChartView.Texture"/> directly.</summary>
    [Export] public TextureRect SharedPreview { get; set; } = null!;

    /// <summary>Label next to the preview, showing the shared <see cref="ChartView.Canvas"/> it came from.</summary>
    [Export] public Label PreviewLabel { get; set; } = null!;

    /// <summary>Chart with <see cref="ChartView.EditorPreview"/> turned off.</summary>
    [Export] public ChartView EditorChart { get; set; } = null!;

    private ICanvas2D? _sharedCanvas;
    private bool _canvasReported;

    /// <inheritdoc />
    public override void _EnterTree()
    {
        // A view reads its factory once, while its surface enters the tree (see ChartView._EnterTree), and
        // Godot enters the tree top-down: this node gets here before its children, so the factory is
        // already in place when the two views ask for a canvas - _Ready would be too late. The exports are
        // resolved while the scene is instantiated, which is what makes them usable here at all.
        // The "not wired" test goes through IsInstanceValid instead of a null test: the two exports are
        // annotated non-nullable, so Godot is the one that can still report a view that was never wired
        // (or one that was already freed).
        if (!GodotObject.IsInstanceValid(SharedChartA) || !GodotObject.IsInstanceValid(SharedChartB))
        {
            GD.PushError($"{nameof(ChartViewHooksDemo)}: the two shared views are not wired in the scene.");
            return;
        }

        SharedChartA.CanvasFactory = ShareCanvas;
        SharedChartB.CanvasFactory = ShareCanvas;
    }

    /// <inheritdoc />
    public override void _Ready()
    {
        ConfigureMarkCell();
        ConfigureTooltips();
        ConfigureSharedCanvas();
        ConfigureEditorPreview();
    }

    /// <inheritdoc />
    public override void _ExitTree()
    {
        // This demo created the shared canvas, so this demo releases it; both surface nodes were told
        // they do not own it (see ConfigureSharedCanvas).
        _sharedCanvas?.Dispose();
        _sharedCanvas = null;
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        // Surface -> Canvas -> Texture: the surface both views draw into, presented here by a plain
        // TextureRect. The texture is only up to date once the canvas committed a frame.
        if (SharedChartA.Texture is { } texture && !ReferenceEquals(SharedPreview.Texture, texture))
            SharedPreview.Texture = texture;

        // The middle piece of the triple, read instead of only named in a comment: Canvas is the interface
        // both views draw through (one instance here, hence one size), and Texture is what the rectangle
        // shows. A surface only exists once the view entered the tree, so the readout waits for it.
        if (!_canvasReported && SharedChartA is { Canvas: { } canvas, Surface: { } surface })
        {
            PreviewLabel.Text = $"{canvas.GetType().Name} {surface.CanvasSize.X}x{surface.CanvasSize.Y}"
                + $" (shared with view B: {ReferenceEquals(canvas, SharedChartB.Canvas)}) -> TextureRect";
            _canvasReported = true;
        }
    }

    /// <summary>The one canvas both shared views draw into; created by the first surface that asks.</summary>
    private ICanvas2D ShareCanvas(int width, int height)
    {
        _sharedCanvas ??= Canvas2DFactory.Create(width, height);

        return _sharedCanvas;
    }

    // ── ConfigureMark: restyle the mark the node builds ─────────────────────

    /// <summary>
    /// <see cref="ChartView.ConfigureMark"/> is invoked while the node builds its mark - on every rebuild,
    /// on whatever instance the kind produced - so the callback tests the type first. The mark knobs
    /// themselves are toured in ChartMarksCartesianDemo; this cell shows the hook and that it re-runs.
    /// </summary>
    private void ConfigureMarkCell()
    {
        IntervalChart.SetValues([("Mon", 32.0), ("Tue", 47.0), ("Wed", 39.0), ("Thu", 61.0), ("Fri", 54.0)]);
        IntervalChart.ConfigureMark(mark =>
        {
            if (mark is not IntervalMark bars) return;
            bars.BarPadding = 0.45f;     // 0.2 by default: a wider gap inside each band
            bars.CornerRadius = 12f;     // 3 by default
        });
    }

    // ── Tooltip.Options: the two content builders ───────────────────────────

    /// <summary>
    /// <see cref="TooltipOptions.ContentBuilder"/> returns one plain line per entry, and
    /// <see cref="TooltipOptions.RichContentBuilder"/> returns styled lines - it takes priority when both
    /// are set. Both receive a <see cref="TooltipContext"/> with the hit's row, index, mark type, series
    /// key and element colour, which is everything the default <see cref="HitResult.Label"/> cannot say.
    /// </summary>
    private void ConfigureTooltips()
    {
        PlainTooltipChart.SetData(
        [
            Row(("category", "North"), ("value", 42.0), ("note", "grew 6% on the previous quarter")),
            Row(("category", "South"), ("value", 28.0), ("note", "flat, one warehouse offline")),
            Row(("category", "East"), ("value", 36.0), ("note", "new channel, ramping up")),
            Row(("category", "West"), ("value", 51.0), ("note", "best quarter so far")),
        ]);
        PlainTooltipChart.Tooltip.Options.ContentBuilder = context =>
        [
            $"{context.Row.Get("category")}   ({context.MarkType}, row {context.RowIndex})",
            $"value: {context.Row.Get<double>("value"):N0}",
            $"{context.Row.Get<string>("note")}",
        ];

        RichTooltipChart.SetData(
        [
            Row(("category", "Basic"), ("value", 1240.0), ("share", 0.18)),
            Row(("category", "Pro"), ("value", 2680.0), ("share", 0.39)),
            Row(("category", "Team"), ("value", 1930.0), ("share", 0.28)),
            Row(("category", "Trial"), ("value", 1030.0), ("share", 0.15)),
        ]);

        var options = RichTooltipChart.Tooltip.Options;
        options.BackgroundColor = new Color(0.07f, 0.10f, 0.18f, 0.96f);
        options.BorderColor = new Color(0.45f, 0.72f, 1f, 0.75f);
        options.FontSize = 15f;
        options.CornerRadius = 12f;
        options.RichContentBuilder = context =>
        {
            var row = context.Row;
            return
            [
                TooltipLine.Plain($"{row.Get("category")}   {context.MarkType} #{context.RowIndex}"),
                TooltipLine.WithIcon(TooltipIcon.Circle, context.ElementColor,
                                     $"{context.SeriesKey ?? "value"}: {row.Get<double>("value"):N0}"),
                new TooltipLine
                {
                    Spans =
                    [
                        new TooltipSpan { Text = "share: ", Color = new Color(0.62f, 0.72f, 0.88f) },
                        new TooltipSpan
                        {
                            Text = $"{row.Get<double>("share"):P0}", Bold = true, Color = context.ElementColor,
                        },
                        new TooltipSpan { Text = "   (TooltipSpan with Bold + Color)" },
                    ],
                },
            ];
        };
    }

    // ── CanvasFactory: two views, one canvas ────────────────────────────────

    /// <summary>
    /// Point both views at the one canvas this demo holds - the factory handed to them in
    /// <see cref="_EnterTree"/> returns that same instance for every request. The size and ownership rules
    /// it has to respect are in the class summary, and both views carry the same rows on purpose.
    /// </summary>
    private void ConfigureSharedCanvas()
    {
        if (SharedChartA.Surface is { } surfaceA) surfaceA.OwnsCanvas = false;
        if (SharedChartB.Surface is { } surfaceB) surfaceB.OwnsCanvas = false;

        var rows = new List<DataRow>();
        foreach (var (step, low, high) in new[]
                 {
                     ("A", 12.0, 22.0), ("B", 18.0, 26.0), ("C", 15.0, 34.0),
                     ("D", 24.0, 30.0), ("E", 20.0, 41.0), ("F", 28.0, 36.0),
                 })
        {
            rows.Add(Row(("category", step), ("value", low), ("series", "low")));
            rows.Add(Row(("category", step), ("value", high), ("series", "high")));
        }

        foreach (var view in new[] { SharedChartA, SharedChartB })
        {
            view.ColorField = "series";
            view.ColorMapping = ColorMappingKind.Category;
            view.Legend = LegendPosition.Bottom;
            view.SetData(rows);
        }
    }

    // ── EditorPreview: the editor-only switch ───────────────────────────────

    /// <summary>
    /// <see cref="ChartView.EditorPreview"/> switched off: nothing changes at runtime, but the editor stops
    /// building a live surface for this view and draws its placeholder instead. (The two refresh verbs,
    /// <see cref="ChartView.Refresh"/> and <see cref="ChartView.Repaint"/>, are demonstrated on the
    /// ChartViewFieldsDemo page.)
    /// </summary>
    private void ConfigureEditorPreview()
    {
        // Only the editor cares: with the preview off the tool script draws no surface there and the chart
        // shows its "ChartView · kind · rows" placeholder instead.
        EditorChart.EditorPreview = false;
        EditorChart.SetValues(
        [
            ("1", 12.0), ("2", 19.0), ("3", 16.0), ("4", 28.0), ("5", 24.0), ("6", 33.0),
        ]);
    }

    /// <summary>One row from field/value pairs - every chart in this demo is fed through it.</summary>
    private static DataRow Row(params (string Field, object Value)[] fields)
    {
        var row = new DataRow(fields.Length);
        foreach (var (field, value) in fields) row.Set(field, value);
        return row;
    }
}
