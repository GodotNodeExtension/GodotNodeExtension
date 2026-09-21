using System;
using System.Collections.Generic;
using Godot;
using GodotNodeExtension.Component.GodotChart;
using GodotNodeExtension.Component.GodotChart.Canvas;
using GodotNodeExtension.Component.GodotChart.Marks;

namespace GodotNodeExtension.Example.GodotChart;

/// <summary>
/// The seven renderer slots of <see cref="Chart"/> and the raw canvas drawing API, hand-built through
/// <see cref="Canvas2DControl"/> cells; the captions in the scene stay short - the reasoning lives here.
/// <para>
/// Cell 1 swaps all seven slots, one stage each, while every other stage keeps its <see cref="DefaultRenderers"/>
/// implementation: <see cref="Chart.BackgroundRenderer"/> (a gradient in the rounded rect),
/// <see cref="Chart.TitleRenderer"/> (DrawTitle, then the badge), <see cref="Chart.GridRenderer"/> (default ticks
/// with a dash pattern), <see cref="Chart.AxisRenderer"/> (outward tick marks), <see cref="Chart.AxisLabelRenderer"/>
/// (default labels plus the AxisConfig.Unit suffix), <see cref="Chart.LegendRenderer"/> (plus the entry count) and
/// <see cref="Chart.CrosshairRenderer"/> (plus the nearest reading).
/// </para>
/// <para>
/// A slot gets the whole <see cref="RenderContext"/>, which is why three renderers here are the default plus extra
/// rather than a reimplementation. Ticks and legend geometry come from the library's internal helpers, data becomes a
/// pixel through <see cref="PlotArea.MapX"/> / <see cref="PlotArea.MapY"/> after the scale normalized it, and an arc
/// needs less than a full turn (2*pi collapses). The crosshair stage runs only while the chart holds a pointer
/// position, fed from the cell's GuiInput.
/// </para>
/// <para>
/// Cell 2 keeps its hand-built chart in the top band and paints the band below with the canvas API: IPath2D geometry
/// and shapes, IPaint2D stroke state, gradients, the transform stack (Save/Restore and the exception-safe SaveScope,
/// ClipRect), MeasureText and an image built with Image.CreateEmpty + LoadImage. Cell 3 leaves its chart without a
/// pointer and paints ring plus readout from the <see cref="HitResult"/> in <see cref="Chart.CurrentPlotArea"/>.
/// </para>
/// </summary>
public partial class ChartRendererDemo : Control
{
    private const float ApiChartHeight = 132f;   // cell 2: the chart keeps this band, the API tour follows
    private const string TitleBadge = "title renderer + badge";
    /// <summary>Cell 1: all seven renderer slots replaced by custom implementations.</summary>
    [Export] public Canvas2DControl SlotsChart { get; set; } = null!;
    /// <summary>Cell 2: the raw canvas drawing API (<see cref="IPath2D"/> / <see cref="IPaint2D"/>).</summary>
    [Export] public Canvas2DControl CanvasApiChart { get; set; } = null!;
    /// <summary>Cell 3: a hit-test driven, host-painted hover overlay.</summary>
    [Export] public Canvas2DControl HitTestChart { get; set; } = null!;
    /// <summary>Puts the seven custom renderers back on cell 1.</summary>
    [Export] public Button CustomSlotButton { get; set; } = null!;
    /// <summary>Restores <see cref="DefaultRenderers"/> on the seven slots of cell 1.</summary>
    [Export] public Button DefaultSlotButton { get; set; } = null!;
    /// <summary>Readout of the slots, the measured text and the canvas capabilities.</summary>
    [Export] public Label Status { get; set; } = null!;
    private Chart? _slots;
    private Chart? _canvasApi;
    private Chart? _hitTest;
    private Vector2? _slotsPointer;
    private Vector2? _hitTestPointer;
    private IImageHandle? _checkerImage;
    private bool _checkerTried;
    /// <inheritdoc />
    public override void _Ready()
    {
        if (SlotsChart.Canvas is not { } slotsCanvas || CanvasApiChart.Canvas is not { } apiCanvas
            || HitTestChart.Canvas is not { } hitCanvas)
        {
            Report("no canvas: this demo needs a rendering device (a headless run has none)");
            return;
        }
        _slots = BuildSlotsChart(slotsCanvas);
        _canvasApi = BuildCanvasApiChart(apiCanvas);
        _hitTest = BuildHitTestChart(hitCanvas);
        SlotsChart.CanvasDraw += (control, _) => DrawSlots(control);
        CanvasApiChart.CanvasDraw += DrawCanvasApi;
        HitTestChart.CanvasDraw += DrawHitTest;
        SlotsChart.GuiInput += @event => OnCellInput(@event, hitTest: false);
        SlotsChart.MouseExited += () => SetPointer(hitTest: false, position: null);
        HitTestChart.GuiInput += @event => OnCellInput(@event, hitTest: true);
        HitTestChart.MouseExited += () => SetPointer(hitTest: true, position: null);
        CustomSlotButton.Pressed += () => SetCustomSlots(custom: true);
        DefaultSlotButton.Pressed += () => SetCustomSlots(custom: false);
        var c = apiCanvas.Capabilities;
        Report($"capabilities: gradients={c.SupportsGradients}, clipping={c.SupportsClipping}, transforms="
            + $"{c.SupportsTransforms}, dash={c.SupportsLineDash}, images={c.SupportsImages}, gpu={c.IsGpuBacked}");
    }
    /// <inheritdoc />
    public override void _ExitTree()
    {
        _checkerImage?.Dispose();   // an image handle is a backend resource owned by this demo
        _checkerImage = null;
        // The flag falls with the handle: _Ready only runs once per instance, so a node that re-enters the
        // tree keeps this state - and a flag left at true made CheckerImage() skip the reload, leaving the
        // image band silently empty for the rest of the session.
        _checkerTried = false;
    }

    // ── Cell 1: the seven renderer slots ───────────────────────────────────
    private static Chart BuildSlotsChart(ICanvas2D canvas)
    {
        var chart = new Chart(canvas) { Title = "7/7 slots replaced" };
        chart.Data(
        [
            Row(("category", "Mon"), ("value", 42.0), ("segment", "load")),
            Row(("category", "Mon"), ("value", 26.0), ("segment", "solar")),
            Row(("category", "Tue"), ("value", 51.0), ("segment", "load")),
            Row(("category", "Tue"), ("value", 33.0), ("segment", "solar")),
            Row(("category", "Wed"), ("value", 47.0), ("segment", "load")),
        ]);
        chart.Mark(new IntervalMark { CornerRadius = 3f });
        chart.Encode(Channel.X, "category");
        chart.Encode(Channel.Y, "value");
        chart.Encode(Channel.Color, "segment");            // gives the legend something to list
        chart.YAxis(new AxisConfig { Unit = "kWh" });      // the values are kWh - the Y axis names the unit
        // The custom axis label slot reads XAxisConfig.Unit (the axis those ticks belong to), so the X
        // axis has to carry a unit as well: without it the slot has nothing to append and returns early.
        chart.XAxis(new AxisConfig { Unit = "day" });      // the X ticks are weekday categories
        chart.Legend(new LegendConfig { Position = LegendPosition.Bottom });
        ApplyCustomSlots(chart);
        return chart;
    }
    // The seven slots, one stage each - the class comment lists them and their values.
    private static void ApplyCustomSlots(Chart chart)
    {
        chart.BackgroundRenderer = GradientBackground;      // 1/7 background: gradient + rounded rect
        chart.TitleRenderer = TitleWithBadge;               // 2/7 title: default text + badge
        chart.GridRenderer = DashedGrid;                    // 3/7 grid: dashed lines
        chart.AxisRenderer = TickMarkAxes;                  // 4/7 axis lines: outward tick marks
        chart.AxisLabelRenderer = LabelsWithUnitSuffix;     // 5/7 axis labels: default + unit
        chart.LegendRenderer = LegendWithCount;             // 6/7 legend: default + entry count
        chart.CrosshairRenderer = CrosshairWithReadout;     // 7/7 crosshair: dashed + readout
    }
    private static void RestoreDefaultSlots(Chart chart)
    {
        chart.BackgroundRenderer = DefaultRenderers.DrawBackground;
        chart.TitleRenderer = DefaultRenderers.DrawTitle;
        chart.GridRenderer = DefaultRenderers.DrawGrid;
        chart.AxisRenderer = DefaultRenderers.DrawAxes;
        chart.AxisLabelRenderer = DefaultRenderers.DrawAxisLabels;
        chart.LegendRenderer = DefaultRenderers.DrawLegend;
        chart.CrosshairRenderer = DefaultRenderers.DrawCrosshair;
    }
    private static void GradientBackground(RenderContext ctx)
    {
        using var path = ctx.Canvas.CreatePath();
        using var paint = ctx.Canvas.CreatePaint();
        path.RoundRect(ctx.OffsetX, ctx.OffsetY, ctx.Width, ctx.Height, ctx.Theme.BackgroundCornerRadius);
        if (ctx.Canvas.Capabilities.SupportsGradients)
            paint.SetLinearGradient(ctx.OffsetX, ctx.OffsetY, ctx.OffsetX, ctx.OffsetY + ctx.Height,
                [new GradientStop(0f, new Color(0.05f, 0.08f, 0.17f)),
                 new GradientStop(0.55f, new Color(0.09f, 0.07f, 0.19f)),
                 new GradientStop(1f, new Color(0.17f, 0.06f, 0.12f))]);
        else
            paint.SetColor(ctx.BackgroundColor);
        ctx.Canvas.Fill(path, paint);
    }
    private static void TitleWithBadge(RenderContext ctx)
    {
        DefaultRenderers.DrawTitle(ctx);
        if (ctx.Title is null) return;
        using var paint = ctx.Canvas.CreatePaint();
        paint.SetColor(new Color(0.55f, 0.95f, 0.80f));
        ctx.Canvas.DrawText(TitleBadge, ctx.OffsetX + ctx.Width - 10f, ctx.OffsetY + ctx.Theme.TitleYOffset,
            new FontSettings { Size = 11f, Align = TextAlign.Right }, paint);
    }
    private static void DashedGrid(RenderContext ctx)
    {
        using var paint = ctx.Canvas.CreatePaint();
        paint.SetColor(ctx.GridColor).SetStrokeWidth(ctx.Theme.GridLineWidth);
        if (ctx.Canvas.Capabilities.SupportsLineDash)
            paint.SetLineDash([2f, 4f]);        // without the capability the lines stay solid
        foreach (var (norm, _) in Ticks(ctx, Channel.Y))
            ctx.Canvas.DrawLine(ctx.Plot.X, ctx.Plot.MapY(norm), ctx.Plot.X + ctx.Plot.Width,
                ctx.Plot.MapY(norm), paint);
        foreach (var (norm, _) in Ticks(ctx, Channel.X))
            ctx.Canvas.DrawLine(ctx.Plot.MapX(norm), ctx.Plot.Y, ctx.Plot.MapX(norm),
                ctx.Plot.Y + ctx.Plot.Height, paint);
    }
    private static void TickMarkAxes(RenderContext ctx)
    {
        var plot = ctx.Plot;
        using var paint = ctx.Canvas.CreatePaint();
        paint.SetColor(ctx.AxisColor).SetStrokeWidth(ctx.Theme.AxisLineWidth).SetLineCap(LineCap.Round);
        ctx.Canvas.DrawLine(plot.X, plot.Y, plot.X, plot.Y + plot.Height, paint);
        ctx.Canvas.DrawLine(plot.X, plot.Y + plot.Height, plot.X + plot.Width, plot.Y + plot.Height, paint);
        foreach (var (norm, _) in Ticks(ctx, Channel.Y))
            ctx.Canvas.DrawLine(plot.X - 4f, plot.MapY(norm), plot.X, plot.MapY(norm), paint);
        foreach (var (norm, _) in Ticks(ctx, Channel.X))
            ctx.Canvas.DrawLine(plot.MapX(norm), plot.Y + plot.Height, plot.MapX(norm),
                plot.Y + plot.Height + 4f, paint);
    }
    private static void LabelsWithUnitSuffix(RenderContext ctx)
    {
        DefaultRenderers.DrawAxisLabels(ctx);
        // The suffix is the unit of the axis these ticks belong to - the X axis, whose AxisConfig.Unit the cell
        // sets ("day"); reading the Y unit instead would stamp the values' "kWh" next to an X label. It is
        // printed the way the library prints a unit in an axis label (see the axis hit test's BuildAxisLabel).
        if (ctx.XAxisConfig?.Unit is not { Length: > 0 } unit) return;
        var ticks = Ticks(ctx, Channel.X);
        if (ticks.Count == 0) return;
        float size = ctx.Theme.LabelFontSize > 0f ? ctx.Theme.LabelFontSize : FontSettings.Default.Size;
        var font = new FontSettings { Size = size, Align = TextAlign.Left };
        var (norm, text) = ticks[^1];
        float x = ctx.Plot.MapX(norm) + ctx.Canvas.MeasureText(text, font).Width * 0.5f + 2f;
        using var paint = ctx.Canvas.CreatePaint();
        paint.SetColor(ctx.AxisColor).SetStrokeWidth(1f);
        ctx.Canvas.DrawText($" ({unit})", x, ctx.Plot.Y + ctx.Plot.Height + ctx.Theme.XAxisLabelOffset,
            font, paint);
    }
    private static void LegendWithCount(RenderContext ctx)
    {
        DefaultRenderers.DrawLegend(ctx);
        if (ctx.ColorScale is not { } scale || ctx.LegendLayout is not { Items.Count: > 0 } layout)
            return;
        var last = layout.Items[^1];
        using var paint = ctx.Canvas.CreatePaint();
        paint.SetColor(ctx.Theme.LabelColor).SetStrokeWidth(1f);
        ctx.Canvas.DrawText($"{scale.Domain.Count} legend entries", ctx.Plot.X + ctx.Plot.Width,
            last.Y + last.Height * 0.8f, new FontSettings { Size = 11f, Align = TextAlign.Right }, paint);
    }
    private static void CrosshairWithReadout(RenderContext ctx)
    {
        if (ctx.MousePos is not { } mouse || !ctx.Plot.Contains(mouse.X, mouse.Y)) return;
        if (ctx.Scales.TryGet(Channel.X) is not { } xScale) return;
        if (ctx.Scales.TryGet(Channel.Y) is not { } yScale) return;
        if (ctx.Data.Count == 0) return;
        var plot = ctx.Plot;
        using (var crosshair = ctx.Canvas.CreatePaint())
        {
            crosshair.SetColor(ctx.Theme.CrosshairColor).SetStrokeWidth(ctx.Theme.CrosshairStrokeWidth);
            if (ctx.Canvas.Capabilities.SupportsLineDash)
                crosshair.SetLineDash([ctx.Theme.CrosshairDashLength, ctx.Theme.CrosshairDashLength]);
            ctx.Canvas.DrawLine(plot.X, mouse.Y, plot.X + plot.Width, mouse.Y, crosshair);
            ctx.Canvas.DrawLine(mouse.X, plot.Y, mouse.X, plot.Y + plot.Height, crosshair);
        }
        int nearest = 0;
        float best = float.MaxValue;
        for (int i = 0; i < ctx.Data.Count; i++)
        {
            if (ctx.Encodes.Resolve(Channel.X, ctx.Data[i]) is not { } xRaw) continue;
            if (ctx.Encodes.Resolve(Channel.Y, ctx.Data[i]) is not { } yRaw) continue;
            float d = mouse.DistanceSquaredTo(
                new Vector2(plot.MapX(xScale.Map(xRaw)), plot.MapY(yScale.Map(yRaw))));
            (best, nearest) = d < best ? (d, i) : (best, nearest);
        }
        var row = ctx.Data[nearest];
        string label = $"({ctx.Encodes.Resolve(Channel.X, row)}, {ctx.Encodes.Resolve(Channel.Y, row)})";
        float width = ctx.Canvas.MeasureText(label, new FontSettings { Size = 11f }).Width + 10f;
        DrawReadout(ctx.Canvas, Math.Clamp(mouse.X + 8f, plot.X, MathF.Max(plot.X, plot.X + plot.Width - width)),
            Math.Clamp(mouse.Y - 26f, plot.Y, MathF.Max(plot.Y, plot.Y + plot.Height - 22f)),
            width, ctx.Theme.CrosshairColor, label);
    }

    // ── Cell 2: the canvas drawing API ─────────────────────────────────────
    private static Chart BuildCanvasApiChart(ICanvas2D canvas)
    {
        var chart = new Chart(canvas);
        chart.Data(
        [
            Row(("x", 1.0), ("value", 18.0)),
            Row(("x", 2.0), ("value", 40.0)),
            Row(("x", 3.0), ("value", 26.0)),
            Row(("x", 4.0), ("value", 52.0)),
        ]);
        chart.Mark(new LineMark { ShowArea = true, AreaOpacity = 0.18f });
        chart.Encode(Channel.X, "x");
        chart.Encode(Channel.Y, "value");
        return chart;
    }
    // The canvas-API tour - the class comment names the IPath2D / IPaint2D members each helper draws.
    private void DrawCanvasApi(Canvas2DControl control, ICanvas2D canvas)
    {
        if (_canvasApi is null) return;
        _canvasApi.Width = MathF.Max(1f, control.CanvasSize.X);
        _canvasApi.Height = MathF.Min(ApiChartHeight, MathF.Max(1f, control.CanvasSize.Y));
        _canvasApi.Render();
        float bandY = _canvasApi.Height;
        float bandHeight = MathF.Max(1f, control.CanvasSize.Y) - bandY - 20f;    // text + image strip
        if (bandHeight <= 20f) return;
        float column = _canvasApi.Width / 4f;
        ApiPaths(canvas, 0f, bandY, column);
        ApiShapes(canvas, column, bandY, column);
        ApiPaintState(canvas, column * 2f, bandY, column);
        ApiGradientsAndTransforms(canvas, column * 3f, bandY, column, bandHeight);
        ApiTextAndImage(canvas, bandY + bandHeight + 2f, _canvasApi.Width);
    }
    private static void ApiPaths(ICanvas2D canvas, float x, float y, float w)
    {
        using var paint = canvas.CreatePaint();
        paint.SetColor(new Color(0.55f, 0.85f, 1f)).SetStrokeWidth(1.5f).SetLineCap(LineCap.Round).SetLineJoin(LineJoin.Round);
        using var path = canvas.CreatePath();
        path.MoveTo(x + 8f, y + 30f).LineTo(x + w * 0.4f, y + 18f).LineTo(x + w * 0.7f, y + 30f).Close();
        path.MoveTo(x + 8f, y + 48f).CubicTo(x + 20f, y + 26f, x + 40f, y + 70f, x + w - 12f, y + 48f);
        path.MoveTo(x + 8f, y + 68f).QuadTo(x + w * 0.5f, y + 46f, x + w - 12f, y + 68f);
        path.ArcTo(x + w * 0.5f, y + 86f, 11f, 0f, MathF.PI * 1.5f);
        canvas.Stroke(path, paint);
        path.Reset().Circle(x + w * 0.5f, y + 86f, 2.5f);
        paint.SetColor(new Color(1f, 0.85f, 0.4f));
        canvas.Fill(path, paint);
    }
    private static void ApiShapes(ICanvas2D canvas, float x, float y, float w)
    {
        using var fill = canvas.CreatePaint();
        fill.SetColor(new Color(0.35f, 0.78f, 0.58f, 0.9f));
        using var outline = canvas.CreatePaint();
        outline.SetColor(new Color(0.95f, 0.95f, 1f, 0.55f)).SetStrokeWidth(1.5f);
        using var path = canvas.CreatePath();
        path.RoundRect(x + 8f, y + 18f, w - 24f, 22f, 6f);
        canvas.StrokeAndFill(path, outline, fill);
        path.Reset().Circle(x + w * 0.42f, y + 82f, 12f);
        canvas.Fill(path, fill);
        path.Reset().Circle(x + w * 0.42f, y + 82f, 7f);
        fill.SetColor(new Color(1f, 0.62f, 0.42f, 0.9f));
        canvas.Fill(path, fill);
    }
    private static void ApiPaintState(ICanvas2D canvas, float x, float y, float w)
    {
        using var paint = canvas.CreatePaint();
        paint.SetColor(new Color(1f, 0.78f, 0.38f)).SetLineCap(LineCap.Butt).SetLineJoin(LineJoin.Miter);
        canvas.DrawLine(x + 8f, y + 30f, x + w - 8f, y + 30f, paint.SetStrokeWidth(4f));
        using var dashed = canvas.CreatePaint();
        dashed.SetColor(new Color(0.95f, 0.55f, 0.85f)).SetStrokeWidth(2f).SetLineCap(LineCap.Round);
        if (canvas.Capabilities.SupportsLineDash)
            dashed.SetLineDash([5f, 3f], 1f);
        canvas.DrawLine(x + 8f, y + 44f, x + w - 8f, y + 44f, dashed);
        canvas.DrawLine(x + 12f, y + 60f, x + 46f, y + 60f, paint.SetStrokeWidth(8f).SetLineCap(LineCap.Butt));
        canvas.DrawLine(x + 12f, y + 74f, x + 46f, y + 74f, paint.SetLineCap(LineCap.Round));
        canvas.DrawLine(x + 12f, y + 88f, x + 46f, y + 88f, paint.SetLineCap(LineCap.Square));
        paint.SetStrokeWidth(3f).SetLineCap(LineCap.Butt);
        using var path = canvas.CreatePath();
        for (int i = 0; i < 3; i++)
        {
            float cx = x + 56f + i * 16f;
            path.Reset().MoveTo(cx, y + 90f).LineTo(cx + 7f, y + 80f).LineTo(cx + 14f, y + 90f);
            canvas.Stroke(path, paint.SetLineJoin((LineJoin)i));
        }
    }
    private static void ApiGradientsAndTransforms(ICanvas2D canvas, float x, float y, float w, float h)
    {
        using var paint = canvas.CreatePaint();
        using var path = canvas.CreatePath();
        using (canvas.SaveScope())          // SaveScope: Save + Restore as one disposable scope
        {
            if (canvas.Capabilities.SupportsGradients)
            {
                path.RoundRect(x + 8f, y + 18f, w - 20f, 20f, 5f);
                paint.SetLinearGradient(x + 8f, y + 18f, x + w - 12f, y + 38f,
                    [new GradientStop(0f, new Color(0.25f, 0.65f, 1f)),
                     new GradientStop(1f, new Color(0.90f, 0.30f, 0.75f))]);
                canvas.Fill(path, paint);
                path.Reset().Circle(x + w * 0.35f, y + 68f, 15f);
                paint.SetRadialGradient(x + w * 0.35f, y + 68f, 15f,
                    [new GradientStop(0f, new Color(1f, 0.95f, 0.60f)),
                     new GradientStop(1f, new Color(1f, 0.35f, 0.20f, 0f))]);
                canvas.Fill(path, paint);
            }
        }
        if (!canvas.Capabilities.SupportsTransforms) return;
        canvas.Save();                      // the same pair spelled out: clip + transforms + Restore
        float clipY = y + h - 26f;
        canvas.ClipRect(x, clipY, w, 24f);
        canvas.Translate(x + w * 0.72f, clipY + 12f);
        canvas.Rotate(MathF.PI / 4f);
        canvas.Scale(1.5f, 1.5f);
        path.Reset().RoundRect(-7f, -7f, 14f, 14f, 3f);
        paint.SetColor(new Color(1f, 0.90f, 0.55f)).SetStrokeWidth(2f).SetOpacity(0.9f);
        canvas.Stroke(path, paint);
        canvas.Restore();
    }
    private void ApiTextAndImage(ICanvas2D canvas, float y, float width)
    {
        const string measured = "MeasureText(\"canvas API\")";
        var font = new FontSettings { Size = 11f, Align = TextAlign.Left };
        var metrics = canvas.MeasureText(measured, font);
        using var paint = canvas.CreatePaint();
        paint.SetColor(new Color(0.78f, 0.86f, 1f, 0.9f));
        canvas.DrawText($"{measured} -> {metrics.Width:F0} x {metrics.Height:F0} px", 6f, y + 12f, font, paint);
        var image = CheckerImage(canvas);
        if (image is null) return;
        canvas.DrawText($"DrawImage({image.Width}x{image.Height})", width - 110f, y + 12f, font, paint);
        canvas.DrawImage(image, width - 26f, y + 1f, 16f, 16f, 0.9f);
    }
    private IImageHandle? CheckerImage(ICanvas2D canvas)
    {
        if (_checkerImage is not null || _checkerTried) return _checkerImage;
        _checkerTried = true;
        try
        {
            var image = Image.CreateEmpty(2, 2, false, Image.Format.Rgba8);
            image.Fill(new Color(0.95f, 0.75f, 0.30f));
            image.SetPixel(1, 0, new Color(0.20f, 0.45f, 0.85f));
            image.SetPixel(0, 1, new Color(0.20f, 0.45f, 0.85f));
            _checkerImage = canvas.LoadImage(ImageTexture.CreateFromImage(image));
        }
        // Two failures are expected and mean the same thing - this cell draws without the image:
        // NotSupportedException when the backend has no raster images at all, and InvalidOperationException
        // from LoadImage(Texture2D) itself (a texture that hands out no pixel data). Catching only the
        // first one let the second take the whole page down.
        catch (NotSupportedException) { _checkerImage = null; }
        catch (InvalidOperationException) { _checkerImage = null; }
        return _checkerImage;
    }

    // ── Cell 3: a hit-test driven, host-painted overlay ────────────────────
    private static Chart BuildHitTestChart(ICanvas2D canvas)
    {
        var chart = new Chart(canvas) { Title = "HitTest + host overlay" };
        chart.Data(
        [
            Row(("x", 1.0), ("value", 22.0), ("series", "a")),
            Row(("x", 2.0), ("value", 41.0), ("series", "b")),
            Row(("x", 3.0), ("value", 33.0), ("series", "a")),
            Row(("x", 4.0), ("value", 58.0), ("series", "b")),
            Row(("x", 5.0), ("value", 47.0), ("series", "a")),
        ]);
        chart.Mark(new PointMark { DefaultRadius = 7f });
        chart.Encode(Channel.X, "x");
        chart.Encode(Channel.Y, "value");
        chart.Encode(Channel.Color, "series");
        return chart;
    }
    private void DrawHitTest(Canvas2DControl control, ICanvas2D canvas)
    {
        if (_hitTest is null) return;
        RenderChart(_hitTest, control);                 // layout first: HitTest needs the plot
        if (_hitTest.CurrentPlotArea is not { } plot) return;
        var hit = _hitTestPointer is { } pointer ? _hitTest.HitTest(pointer) : null;
        using var paint = canvas.CreatePaint();
        if (hit is not { Hit: true })
        {
            paint.SetColor(new Color(0.80f, 0.88f, 1f, 0.85f));
            canvas.DrawText(_hitTestPointer is null
                ? "hover a dot: this cell hit-tests and paints the ring itself"
                : "nothing under the pointer (HitTest() returned null)",
                plot.X + 4f, plot.Y + 14f, new FontSettings { Size = 11f }, paint);
            return;
        }
        var center = new Vector2(hit.ScreenX, hit.ScreenY);
        using var path = canvas.CreatePath();
        path.Circle(center.X, center.Y, 16f);
        paint.SetColor(new Color(1f, 0.88f, 0.40f)).SetStrokeWidth(2f);
        canvas.Stroke(path, paint);
        path.Reset().Circle(center.X, center.Y, 5f);
        canvas.Fill(path, paint);
        // One line, built from the HitResult: mark type, row index, label and the pixel it returned.
        string readout = $"{hit.MarkType} #{hit.RowIndex}: {hit.Label} @ ({center.X:F0}, {center.Y:F0}) px";
        float plate = canvas.MeasureText(readout, new FontSettings { Size = 11f }).Width + 12f;
        DrawReadout(canvas,
            Math.Clamp(center.X + 20f, plot.X, MathF.Max(plot.X, plot.X + plot.Width - plate)),
            Math.Clamp(center.Y - 30f, plot.Y, MathF.Max(plot.Y, plot.Y + plot.Height - 22f)),
            plate, new Color(1f, 0.90f, 0.45f, 0.85f), readout);
    }

    // ── Redraw, input and helpers ──────────────────────────────────────────
    private void DrawSlots(Canvas2DControl control)
    {
        if (_slots is null) return;
        _slots.Interaction(_slotsPointer);   // the crosshair slot only runs with a pointer position
        RenderChart(_slots, control);
    }
    private static void RenderChart(Chart chart, Canvas2DControl control)
    {
        chart.Width = MathF.Max(1f, control.CanvasSize.X);
        chart.Height = MathF.Max(1f, control.CanvasSize.Y);
        chart.Render();
    }
    private void OnCellInput(InputEvent @event, bool hitTest)
    {
        if (@event is not InputEventMouseMotion motion) return;
        SetPointer(hitTest, motion.Position);        // gui_input positions are local to the control
    }
    private void SetPointer(bool hitTest, Vector2? position)
    {
        if (hitTest)
            _hitTestPointer = position;
        else
            _slotsPointer = position;
        (hitTest ? HitTestChart : SlotsChart).Invalidate();
    }
    private void SetCustomSlots(bool custom)
    {
        if (_slots is null) return;
        if (custom)
            ApplyCustomSlots(_slots);
        else
            RestoreDefaultSlots(_slots);
        SlotsChart.Invalidate();
        // The status label names the stage each renderer replaced, so the caption and the label agree.
        Report(custom
            ? "cell 1, 7/7 slots replaced -> background=gradient, title=default+badge, grid=dashes, axis=ticks, "
              + "axis label=default+unit, legend=default+count, crosshair=dashed+readout"
            : "cell 1, 7/7 slots back to DefaultRenderers: DrawBackground, DrawTitle, DrawGrid, DrawAxes, "
              + "DrawAxisLabels, DrawLegend, DrawCrosshair");
    }
    private void Report(string message) => Status.Text = message;
    private static List<(double Norm, string Text)> Ticks(RenderContext ctx, Channel channel)
        => DefaultRenderers.ComputeTicks(ctx.Scales.TryGet(channel),
            fallbackTickCount: ctx.Theme.FallbackTickCount);
    private static void DrawReadout(ICanvas2D canvas, float x, float y, float width, Color border, string text)
    {
        using var path = canvas.CreatePath();
        using var paint = canvas.CreatePaint();
        path.RoundRect(x, y, width, 20f, 5f);
        paint.SetColor(new Color(0.08f, 0.10f, 0.16f, 0.94f));
        canvas.Fill(path, paint);
        path.Reset().RoundRect(x, y, width, 20f, 5f);
        paint.SetColor(border).SetStrokeWidth(1f);
        canvas.Stroke(path, paint);
        paint.SetColor(new Color(1f, 1f, 1f, 0.92f));
        canvas.DrawText(text, x + 6f, y + 14f, new FontSettings { Size = 11f }, paint);
    }
    private static DataRow Row(params (string Field, object Value)[] fields)
    {
        var row = new DataRow(fields.Length);
        foreach (var (field, value) in fields) row.Set(field, value);
        return row;
    }
}
