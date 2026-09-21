namespace GodotNodeExtension.Tests.GodotChart;

using System;
using System.Collections.Generic;
using System.Linq;
using GdUnit4;
using GodotNodeExtension.Component.GodotChart;
using GodotNodeExtension.Component.GodotChart.Marks;
using GodotNodeExtension.Tests.Support;
using Support;
using static GdUnit4.Assertions;

/// <summary>
/// The two-pass contract of the marks that have moved their interaction-state visuals to the overlay
/// (<see cref="Mark.InteractionStateInOverlay"/> / <see cref="Mark.RenderOverlay"/>): with the layer cache
/// off the data layer paints the hover/selection look exactly as it always did and the overlay pass adds
/// nothing, and with the cache on the same look is painted by the overlay instead - from the same numbers.
/// <para>
/// The equality asserted here is the geometry of the state visual (the hover circle, the enlarged dot, the
/// widened bar) rather than a recorded snapshot hash: "the highlight still follows, and it is the same
/// highlight" is what a reader of the chart sees, and it stays checkable without recording a table.
/// </para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class MarkOverlayTest
{
    /// <summary>Three categories with a value each: enough for a hover, a selection and a neighbour.</summary>
    private static List<DataRow> Rows() =>
    [
        TestContexts.Row(("cat", "A"), ("value", 10.0)),
        TestContexts.Row(("cat", "B"), ("value", 20.0)),
        TestContexts.Row(("cat", "C"), ("value", 15.0)),
    ];

    /// <summary>
    /// Every mark that declares its interaction state as overlay-owned
    /// (<see cref="Mark.InteractionStateInOverlay"/>), by the name <see cref="MarkCases"/> uses.
    /// <para>
    /// A name missing from this list is not a gap to fill blindly: it is a mark whose hover/selection look
    /// stays in the data layer, with the reason written on its own declaration (<c>=> false</c>) - the
    /// translucent band of <c>RangeAreaMark</c>, the exploded slice label of <c>PieMark</c>, the flow
    /// opacity of <c>SankeyMark</c>/<c>ChordMark</c>, the recursive arc walk of <c>SunburstMark</c>, the
    /// density body of <c>ViolinMark</c>, the per-series vertices of <c>RadarMark</c>. A chart carrying one
    /// of those renders single-pass (with a warning), which is the safe half of the trade.
    /// </para>
    /// </summary>
    private static readonly string[] MigratedMarkNames =
    [
        "IntervalMark", "LineMark", "PointMark",
        "BoxMark", "CandlestickMark",
        "HeatmapMark", "LollipopMark", "MilestoneMark", "TimelineMark", "WaffleMark",
        "FunnelMark", "GaugeMark",
        "TreemapMark",
    ];

    /// <summary>
    /// Marks with no interaction state of their own (an annotation mark: reference lines and a band). They
    /// answer <see cref="Mark.InteractionStateInOverlay"/> with true - there is nothing that would have to stay
    /// in the data layer - but they paint nothing on hover, so the case that checks "the overlay paints the
    /// hovered row" has nothing to look for here. They still have to keep the layer: a chart carrying one must
    /// not fall back to the single pass.
    /// </summary>
    private static readonly string[] StatelessMarkNames = ["SectionMark"];

    /// <summary>Fresh instances of the migrated marks, with the case name that carries their data.</summary>
    private static IEnumerable<(string Name, Mark Mark)> MigratedMarks()
        => MarkCases.All.Where(c => MigratedMarkNames.Contains(c.Name)).Select(c => (c.Name, c.Create()));

    /// <summary>A chart with one migrated mark over its own case data, with the layer switch set as asked.</summary>
    private static Chart ChartWith(FakeCanvas2D canvas, MarkCases.MarkCase c, Mark mark, bool layeredRendering)
    {
        var chart = new Chart(canvas) { Width = 400f, Height = 300f, UseLayerCache = layeredRendering };
        chart.Data(c.Data);
        chart.Mark(mark);
        chart.Encode(Channel.X, c.XField);
        chart.Encode(Channel.Y, c.YField);
        if (c.ColorField != null)
            chart.Encode(Channel.Color, c.ColorField);
        return chart;
    }

    /// <summary>A chart with one mark over the given rows, hover and selection set - or not.</summary>
    private static FakeCanvas2D Render(Mark mark, bool layeredRendering, int hovered = 1, int selected = -1)
    {
        var canvas = new FakeCanvas2D();
        var chart = new Chart(canvas)
        {
            Width = 400f,
            Height = 300f,
            UseLayerCache = layeredRendering,
        };
        chart.Data(Rows());
        chart.Mark(mark);
        chart.Encode(Channel.X, "cat");
        chart.Encode(Channel.Y, "value");
        chart.Hover(hovered);
        chart.Select(selected);
        chart.Render();
        return canvas;
    }

    /// <summary>Formatted geometry of the recorded circles, so two frames can be compared as one string.</summary>
    private static string Circles(FakeCanvas2D canvas) => string.Join(";",
        canvas.Circles.Select(c => $"{c.Cx:F2},{c.Cy:F2},{c.Radius:F2}"));

    /// <summary>The bars of a frame: the elements, told apart from the chart's own rounded background.</summary>
    private static List<(float X, float Y, float W, float H, float Radius)> Bars(FakeCanvas2D canvas)
        => canvas.RoundRects.Where(r => r.W < 200f).ToList();

    /// <summary>
    /// A mark whose state lives on the overlay must draw nothing there while the chart does not cache the
    /// data layer: that is the whole guarantee that "LayeredRendering off" is pixel-identical to the single
    /// pass the library had before the option existed, and it is checked for each migrated mark.
    /// </summary>
    [TestCase]
    public void TheOverlayPassAddsNothingWhileTheCacheIsOff()
    {
        var problems = new List<string>();
        foreach (var (name, mark) in MigratedMarks())
        {
            var canvas = new FakeCanvas2D();
            var context = TestContexts.Mark(
                canvas, Rows(), TestContexts.XyEncodes("cat", "value"),
                TestContexts.CategoryScales(["A", "B", "C"]),
                hoveredRowIndex: 1, selectedRowIndex: 2);

            mark.Render(context);
            string singlePass = canvas.Snapshot();
            mark.RenderOverlay(context);
            if (canvas.Snapshot() != singlePass) problems.Add($"{name}: the overlay pass drew something");

            // ...and the overlay owner is declared by the mark, so the chart can act on it.
            if (!mark.InteractionStateInOverlay) problems.Add($"{name}: does not declare its state as overlay-owned");
        }

        AssertThat(string.Join("\n", problems)).IsEqual("");
    }

    /// <summary>
    /// The overlay must not paint a highlight nobody asked for: without a hovered or selected row it draws
    /// nothing at all (a cached frame with the pointer outside the plot has to stay as cheap as it looks).
    /// </summary>
    [TestCase]
    public void TheOverlayDrawsNothingWithoutHoverOrSelection()
    {
        var problems = new List<string>();
        foreach (var (name, mark) in MigratedMarks())
        {
            var canvas = new FakeCanvas2D();
            var context = TestContexts.Mark(
                canvas, Rows(), TestContexts.XyEncodes("cat", "value"),
                TestContexts.CategoryScales(["A", "B", "C"]),
                hoveredRowIndex: -1, selectedRowIndex: -1);
            // The chart owns the flag; the mark only has to honour it (false here, so the overlay returns).
            mark.RenderOverlay(context);
            if (canvas.DrewAnything) problems.Add($"{name}: drew {canvas.FillCount + canvas.StrokeCount} op(s)");
        }

        AssertThat(string.Join("\n", problems)).IsEqual("");
    }

    /// <summary>
    /// A cached frame paints the same hover marker a single-pass frame does: same size, same place, same
    /// number of them. The marker is the drawing a reader follows with the pointer, so "the overlay drew
    /// something" is not enough - it has to be the same circle.
    /// </summary>
    [TestCase]
    public void TheCachedFrameDrawsTheSameHoverMarker()
    {
        var singlePass = Render(new LineMark(), layeredRendering: false);
        var cached = Render(new LineMark(), layeredRendering: true);

        // The cached frame really went through the layer (it captured its data layer) ...
        AssertThat(cached.CaptureCount).IsEqual(1);
        // ... and the hover circle of the data layer came from the overlay pass: the layer itself holds none.
        AssertThat(Circles(singlePass)).IsNotEqual("");
        AssertThat(Circles(cached)).IsEqual(Circles(singlePass));
    }

    /// <summary>
    /// The hovered point of a scatter chart is enlarged by the overlay pass, at the radius the single-pass
    /// frame uses. The data layer keeps drawing the point at its own size (that is what makes it cacheable),
    /// so the cached frame has one circle more.
    /// </summary>
    [TestCase]
    public void TheCachedFrameEnlargesTheHoveredPoint()
    {
        static PointMark Mark() => new() { DefaultRadius = 5f };

        var singlePass = Render(Mark(), layeredRendering: false);
        var cached = Render(Mark(), layeredRendering: true);

        AssertThat(cached.CaptureCount).IsEqual(1);
        AssertThat(cached.Circles.Count).IsEqual(singlePass.Circles.Count + 1);
        AssertThat(cached.Circles.Max(c => c.Radius)).IsEqual(singlePass.Circles.Max(c => c.Radius));
    }

    /// <summary>
    /// The hovered bar is widened (and its fill brightened) by the overlay pass, at the width the single-pass
    /// frame draws. The cached frame therefore has one bar more - the data layer's default one plus the
    /// overlay's active one - and the wider of the two is the highlight.
    /// </summary>
    [TestCase]
    public void TheCachedFrameWidensTheHoveredBar()
    {
        var singlePass = Render(new IntervalMark { GroupedBars = false }, layeredRendering: false);
        var cached = Render(new IntervalMark { GroupedBars = false }, layeredRendering: true);

        AssertThat(cached.CaptureCount).IsEqual(1);

        var singleBars = Bars(singlePass);
        var cachedBars = Bars(cached);
        AssertThat(singleBars.Count).IsEqual(3);
        AssertThat(cachedBars.Count).IsEqual(4);

        // The hovered bar of the single pass is the second one (row 1) and the widest of its frame; the
        // overlay's bar is the last one of the cached frame.
        float singleHoverWidth = singleBars.Max(b => b.W);
        float cachedOverlayWidth = cachedBars[^1].W;
        AssertThat(MathF.Abs(singleHoverWidth - cachedOverlayWidth) < 0.01f).IsTrue();
        AssertThat(cachedOverlayWidth > cachedBars[0].W).IsTrue();
    }

    /// <summary>
    /// A decimated series (thousands of rows in a few hundred pixel columns) still highlights the hovered row.
    /// The hover loop used to index the <i>decimated</i> point list with an index that belongs to the full one,
    /// so hovering a row past the decimated length read past the end of the list - and a mark that throws is a
    /// mark the frame loses. The state now reads the full point list, which is what makes the marker sit on the
    /// row's real point as well.
    /// </summary>
    [TestCase]
    public void TheHoverMarkerOfADecimatedSeriesIsDrawn()
    {
        var log = EngineMessageLog.Attach();
        try
        {
            var canvas = new FakeCanvas2D();
            var chart = new Chart(canvas) { Width = 400f, Height = 300f, UseLayerCache = true };
            var rows = new List<DataRow>();
            for (int i = 0; i < 5000; i++)
                rows.Add(TestContexts.Row(("x", (double)i), ("value", 5.0 + (i % 7))));
            chart.Data(rows);
            chart.Mark(new LineMark { Decimate = DecimateMode.On });
            chart.Encode(Channel.X, "x");
            chart.Encode(Channel.Y, "value");
            chart.Hover(4000);
            chart.Render();

            AssertThat(log.ErrorsContaining("LineMark.RenderOverlay failed").Length).IsEqual(0);
            AssertThat(log.ErrorsContaining("LineMark.Render failed").Length).IsEqual(0);
            AssertThat(canvas.Circles.Count).IsEqual(1);
            AssertThat(canvas.CaptureCount).IsEqual(1);          // the layer was kept, the marker came from the overlay
            AssertThat(canvas.NonFiniteCoordinateCount).IsEqual(0);

            // The marker is inside the plot and on the right half of it: row 4000 of 5000 has to be there.
            var plot = chart.CurrentPlotArea!.Value;
            var (cx, cy, radius) = canvas.Circles[0];
            AssertThat(cx > plot.X + plot.Width * 0.5f).IsTrue();
            AssertThat(cx >= plot.X && cx <= plot.X + plot.Width).IsTrue();
            AssertThat(cy >= plot.Y && cy <= plot.Y + plot.Height).IsTrue();
            AssertThat(radius > 0f).IsTrue();
        }
        finally
        {
            log.Detach();
        }
    }

    /// <summary>
    /// The marks that keep their interaction state in the data layer, each with the reason it does. They are
    /// <b>not</b> a to-do list: every one of them either paints its state as the element's own translucent fill
    /// (re-painting it on the overlay would blend it twice and darken the element the pointer is on), or its
    /// state moves something the cached image already holds (the exploded slice label), or the geometry costs a
    /// full walk of the table for a visual of a few pixels (a radar vertex, the sunburst rings).
    /// <para>
    /// A future change that migrates one of them has to come here and say what changed - the alternative would
    /// be a mark that silently loses half of its highlight when a page turns the layer cache on.
    /// </para>
    /// </summary>
    private static readonly (string Name, string Reason)[] DataLayerStateMarkNames =
    [
        ("RangeAreaMark", "its hover look is the band's own 0.3-alpha fill, not a separate element"),
        ("ViolinMark", "the body is filled at 0.5 alpha: the overlay would darken it, and the density runs over the group"),
        ("PieMark", "the hover offset moves the slice's outside label, which a cached image cannot take back"),
        ("RadarMark", "the hover dot needs the per-series dimension walk again, for a few pixels"),
        ("SankeyMark", "a ribbon is drawn at 0.35 alpha and hovers in its own colour: a second pass darkens it"),
        ("ChordMark", "a chord is drawn at 0.4 alpha and hovers in its own colour, same double blend"),
        ("SunburstMark", "an arc is drawn at 0.85 alpha and its angles come from a recursive ring walk"),
        ("GeoAreaMark", "a region hovers by brightening its own fill, and the overlay would need the polygon walk again for every region"),
        ("GeoBubbleMark", "a bubble hovers by brightening its own fill and the overlay would project the table again for a fade over a few pixels"),
        ("GeoFlowMark", "a flow hovers by brightening its own curve, and the overlay would have to project both ends of every route again for that"),
    ];

    /// <summary>
    /// Every mark of the library takes one of the three sides of the overlay contract, and says which: the
    /// three lists above have to cover <see cref="MarkCases.All"/> exactly once each (a new mark cannot slip in
    /// unlisted), each mark has to answer what its side promises, and a chart that carries a data-layer mark has
    /// to fall back to the single pass with one warning naming it - the highlight then still follows the pointer
    /// because it was never moved to the overlay.
    /// </summary>
    [TestCase]
    public void EveryMarkPicksItsSideOfTheOverlayContract()
    {
        var problems = new List<string>();

        var migrated = MarkCases.All.Where(c => MigratedMarkNames.Contains(c.Name)).Select(c => c.Name).ToList();
        var dataLayer = MarkCases.All.Where(c => DataLayerStateMarkNames.Any(d => d.Name == c.Name))
            .Select(c => c.Name).ToList();
        var stateless = MarkCases.All.Where(c => StatelessMarkNames.Contains(c.Name)).Select(c => c.Name).ToList();
        foreach (var c in MarkCases.All)
        {
            int sides = (migrated.Contains(c.Name) ? 1 : 0) + (dataLayer.Contains(c.Name) ? 1 : 0) +
                        (stateless.Contains(c.Name) ? 1 : 0);
            if (sides == 0)
                problems.Add($"{c.Name}: listed in none of the overlay contract lists");
            else if (sides > 1)
                problems.Add($"{c.Name}: listed in {sides} of the overlay contract lists");
        }

        var log = EngineMessageLog.Attach();
        try
        {
            foreach (var name in dataLayer)
            {
                var markCase = MarkCases.All.First(c => c.Name == name);
                var mark = markCase.Create();
                if (mark.InteractionStateInOverlay)
                    problems.Add($"{name}: answers true although it is listed as a data-layer mark");

                int reportedBefore = log.WarningsContaining("UseLayerCache").Length;
                var canvas = new FakeCanvas2D();
                var chart = MarkCases.Build(canvas, markCase);
                chart.UseLayerCache = true;
                chart.Hover(1);
                chart.Select(0);
                chart.Render();

                // Single pass: no layer was captured, and the state really was drawn in the data layer.
                if (canvas.CaptureCount != 0)
                    problems.Add($"{name}: captured a layer although its state lives in the data layer");

                // Exactly one new warning, naming this mark (the log holds the ones the earlier marks pushed).
                var reported = log.WarningsContaining("UseLayerCache");
                if (reported.Length != reportedBefore + 1 || !reported[^1].Contains(name))
                    problems.Add($"{name}: the fallback was not reported once with its name " +
                                 $"({reported.Length - reportedBefore} new warning(s))");
            }
        }
        finally
        {
            log.Detach();
        }

        AssertThat(string.Join("\n", problems)).IsEqual("");
    }

    /// <summary>
    /// A mark that has not moved its state to the overlay keeps painting it in the data layer, and the chart
    /// does not cache the layer at all (a frozen highlight is the failure the option must not introduce).
    /// The hover still follows the pointer - the chart simply renders single-pass - and the reason is reported
    /// once per chart.
    /// </summary>
    [TestCase]
    public void AMarkThatKeepsItsStateInTheDataLayerTurnsTheCacheOff()
    {
        var markCase = MarkCases.All.First(c => c.Name == "PieMark");
        var log = EngineMessageLog.Attach();
        try
        {
            var hoveredCanvas = new FakeCanvas2D();
            var hovered = MarkCases.Build(hoveredCanvas, markCase);
            hovered.UseLayerCache = true;
            hovered.Hover(1);
            hovered.Render();

            var plainCanvas = new FakeCanvas2D();
            var plain = MarkCases.Build(plainCanvas, markCase);
            plain.UseLayerCache = true;
            plain.Render();

            // No layer was captured on either frame: the mark's highlight would have been frozen in it.
            AssertThat(hoveredCanvas.CaptureCount).IsEqual(0);
            AssertThat(plainCanvas.CaptureCount).IsEqual(0);

            // The highlight is still in the data layer, so it follows the pointer.
            AssertThat(hoveredCanvas.Snapshot() == plainCanvas.Snapshot()).IsFalse();

            var warnings = log.WarningsContaining("UseLayerCache");
            AssertThat(warnings.Length).IsEqual(2);       // one per chart instance
            AssertThat(warnings[0].Contains("PieMark")).IsTrue();
        }
        finally
        {
            log.Detach();
        }
    }

    /// <summary>
    /// Every migrated mark keeps its data layer and still follows the pointer: the frame that hovers a row
    /// presents the cached layer (it is not rebuilt) and the overlay paints something for that row.
    /// <para>
    /// This is the failure <see cref="Mark.InteractionStateInOverlay"/> exists to prevent - a highlight that
    /// stops following, or stops being drawn at all, once the layer is kept - so it is checked for every
    /// migrated mark in one place, on the data each mark actually needs (<see cref="MarkCases"/>).
    /// </para>
    /// </summary>
    [TestCase]
    public void EveryMigratedMarkFollowsThePointerWhileTheLayerIsKept()
    {
        var problems = new List<string>();
        var log = EngineMessageLog.Attach();
        try
        {
            foreach (var c in MarkCases.All.Where(c => MigratedMarkNames.Contains(c.Name) ||
                                                       StatelessMarkNames.Contains(c.Name)))
            {
                bool paintsItsState = MigratedMarkNames.Contains(c.Name);
                var mark = c.Create();
                if (!mark.InteractionStateInOverlay)
                {
                    problems.Add($"{c.Name}: does not declare its state as overlay-owned");
                    continue;
                }

                var canvas = new FakeCanvas2D();
                var chart = ChartWith(canvas, c, mark, layeredRendering: true);

                // Row 0: a gauge draws its first row only, and every other case has a row 0 as well.
                chart.Render();
                if (canvas.CaptureCount != 1)
                    problems.Add($"{c.Name}: captured {canvas.CaptureCount} time(s) on the first frame");
                int layerOps = canvas.FillCount + canvas.StrokeCount;

                chart.Hover(0);
                chart.Render();

                // The layer was presented, not redrawn: a highlight painted into it would be frozen, and
                // one painted by nobody would simply be missing.
                if (canvas.CaptureCount != 1)
                    problems.Add($"{c.Name}: hovering rebuilt the data layer");
                if (canvas.ImageDrawCount != 1)
                    problems.Add($"{c.Name}: the cached layer was not presented");
                // A stateless mark has nothing to paint and an empty overlay is exactly its contract: only the
                // marks whose state moved to the overlay have to show something for the hovered row.
                if (paintsItsState && canvas.FillCount + canvas.StrokeCount <= layerOps)
                    problems.Add($"{c.Name}: the overlay painted nothing for the hovered row");
            }

            // A mark that throws while painting its overlay is reported by the render stage and the frame
            // silently loses the highlight, and a chart that falls back to the single pass says why: both
            // are failures here rather than noise to ignore.
            var errors = log.ErrorsContaining("RenderOverlay");
            if (errors.Length > 0)
                problems.Add($"overlay errors: {string.Join(" | ", errors)}");

            var fallbacks = log.WarningsContaining("UseLayerCache");
            if (fallbacks.Length > 0)
                problems.Add($"the cache was turned off: {fallbacks[0]}");
        }
        finally
        {
            log.Detach();
        }

        AssertThat(string.Join("\n", problems)).IsEqual("");
    }

    /// <summary>
    /// A stacked bar chart follows the pointer too. A stacked segment's rectangle starts where the segments
    /// below it end, so its highlight is the one case where the overlay has to re-walk the accumulation: the
    /// hovered segment is widened on the overlay and the layer keeps every segment at its default width, so
    /// hovering another stack moves the widening instead of leaving it baked in the image.
    /// </summary>    [TestCase]
    public void TheStackedBarHighlightMovesWithThePointer()
    {
        var rows = new List<DataRow>
        {
            TestContexts.Row(("cat", "A"), ("value", 10.0), ("series", "S1")),
            TestContexts.Row(("cat", "B"), ("value", 20.0), ("series", "S1")),
            TestContexts.Row(("cat", "A"), ("value", 15.0), ("series", "S2")),
            TestContexts.Row(("cat", "B"), ("value", 5.0), ("series", "S2")),
        };
        var canvas = new FakeCanvas2D();
        var chart = new Chart(canvas) { Width = 400f, Height = 300f, UseLayerCache = true };
        chart.Data(rows);
        chart.Mark(new IntervalMark { Stack = StackMode.Stack });
        chart.Encode(Channel.X, "cat");
        chart.Encode(Channel.Y, "value");
        chart.Encode(Channel.Color, "series");

        chart.Render();
        AssertThat(canvas.CaptureCount).IsEqual(1);
        AssertThat(Bars(canvas).Count).IsEqual(4);       // four segments, none of them hovered yet

        int layerBars = Bars(canvas).Count;
        chart.Hover(0);
        chart.Render();
        var firstStack = Bars(canvas).Skip(layerBars).ToList();
        AssertThat(canvas.CaptureCount).IsEqual(1);      // the layer was kept
        AssertThat(firstStack.Count).IsEqual(1);         // ...and the hovered segment came from the overlay

        int afterFirst = Bars(canvas).Count;
        chart.Hover(1);
        chart.Render();
        var secondStack = Bars(canvas).Skip(afterFirst).ToList();
        AssertThat(canvas.CaptureCount).IsEqual(1);
        AssertThat(secondStack.Count).IsEqual(1);

        // The two categories sit at different X, so a widening that followed the pointer cannot be the same
        // rectangle - one baked into the layer would have been drawn here as the very same one.
        AssertThat(secondStack[0].X).IsNotEqual(firstStack[0].X);
        AssertThat(secondStack[0].W).IsEqual(firstStack[0].W);
    }

    /// <summary>
    /// The layer that gets captured holds the marks' <b>plain</b> drawing: with a hover and a selection set, the
    /// frame a capture is taken from drew exactly what the same chart draws with no interaction state at all.
    /// <para>
    /// This is the other half of <see cref="Mark.InteractionStateInOverlay"/> - "the layer does not contain the
    /// highlight" - and it is checked by comparing what the canvas held at the moment of the capture
    /// (<see cref="FakeCanvas2D.DrawnAtCapture"/>), because a highlight baked into the image would never be
    /// visible in the draw calls again. Checked for every migrated mark, with its own case data.
    /// </para>
    /// </summary>
    [TestCase]
    public void TheCapturedLayerHoldsThePlainDrawing()
    {
        var problems = new List<string>();
        foreach (var c in MarkCases.All.Where(c => MigratedMarkNames.Contains(c.Name) ||
                                                   StatelessMarkNames.Contains(c.Name)))
        {
            var plainCanvas = new FakeCanvas2D();
            ChartWith(plainCanvas, c, c.Create(), layeredRendering: true).Render();

            // Hover and selection are the whole interaction state: both must stay out of the layer.
            var busyCanvas = new FakeCanvas2D();
            var busy = ChartWith(busyCanvas, c, c.Create(), layeredRendering: true);
            busy.Hover(0);
            busy.Select(1);
            busy.Render();

            if (plainCanvas.DrawnAtCapture.Count != 1 || busyCanvas.DrawnAtCapture.Count != 1)
            {
                problems.Add($"{c.Name}: expected one capture per frame, got " +
                             $"{plainCanvas.DrawnAtCapture.Count}/{busyCanvas.DrawnAtCapture.Count}");
                continue;
            }

            var plain = plainCanvas.DrawnAtCapture[0];
            var busyLayer = busyCanvas.DrawnAtCapture[0];
            if (plain != busyLayer)
                problems.Add($"{c.Name}: the layer differs from the plain drawing " +
                             $"(fills/strokes {plain.Fills}/{plain.Strokes} vs {busyLayer.Fills}/{busyLayer.Strokes})");
        }

        AssertThat(string.Join("\n", problems)).IsEqual("");
    }
}
