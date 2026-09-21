namespace GodotNodeExtension.Tests.GodotChart;

using GdUnit4;
using Godot;
using GodotNodeExtension.Component.GodotChart;
using GodotNodeExtension.Component.GodotChart.Marks;
using static GdUnit4.Assertions;

/// <summary>
/// Golden image baselines for the rendering paths a whole-picture regression would show up in: the chart is
/// rendered on the engine's real backend and compared with an image recorded in the repository
/// (<c>Integration/baseline/*.png</c>, see <see cref="ChartRenderHarness.AssertMatchesGolden"/>).
/// <para>
/// Why this suite exists: the other integration suites assert relative thresholds ("at least 2% of the surface
/// carries this kind's ink"), which stay green when an element moves, loses its colour or is drawn over by
/// something else. A baseline is the only check that says "the picture is still the picture".
/// </para>
/// <para>
/// One image per <b>rendering path</b>, not one per kind: a baseline is reviewed by eye and re-recorded whenever
/// the picture legitimately changes, so the count is kept deliberate. The kinds themselves are covered by
/// <c>ChartViewRenderIntegrationTest</c> (per-kind probes) and by the mark geometry snapshots.
/// </para>
/// <para>
/// The cases need a rendering device and skip visibly without one (see
/// <see cref="ChartRenderHarness.NoRenderingDevice"/>): run them with a device - the sandbox does by default,
/// and the nightly workflow runs them with <c>--render</c>.
/// </para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public partial class ChartGoldenImageIntegrationTest
{
    // ── Fixtures ────────────────────────────────────────────────────────────

    /// <summary>
    /// A decorated Cartesian chart: title, both axis titles, a wrapped legend, two series and rotated tick
    /// labels. This is the picture the layout reservations are for (see <c>GC-R26</c>/<c>GC-R30</c>) -
    /// a label column that grew, a title that moved onto its axis labels or a legend row that stopped
    /// wrapping all show up here.
    /// </summary>
    [TestCase]
    public void ADecoratedCartesianChartMatchesItsBaseline()
    {
        if (ChartRenderHarness.NoRenderingDevice(nameof(ADecoratedCartesianChartMatchesItsBaseline))) return;

        var view = ChartRenderHarness.AddView(ChartKind.Bar, ChartRenderHarness.ResizedViewSize);
        view.Title = "Revenue";
        view.XAxisTitle = "Quarter";
        view.YAxisTitle = "USD thousands";
        view.Legend = LegendPosition.Bottom;
        view.GroupedBars = true;
        view.XAxisLabelRotation = 30f;
        SetFields(view, x: "quarter", y: "amount", color: "channel");
        view.SetCsv(
            "quarter,channel,amount\n" +
            "Q1,Retail,12.5\nQ1,Online,7.25\n" +
            "Q2,Retail,15\nQ2,Online,9.5\n" +
            "Q3,Retail,11\nQ3,Online,13.75\n" +
            "Q4,Retail,18.5\nQ4,Online,6.25\n");

        CaptureAndCompare(view, "cartesian-decorated");
    }

    /// <summary>
    /// The polar drawing path: a donut (an arc with a hole), its slice labels and the centre content. Arc
    /// geometry, label placement and the centre text are all in one picture, and none of them is a threshold.
    /// </summary>
    [TestCase]
    public void ThePolarMarksMatchTheirBaseline()
    {
        if (ChartRenderHarness.NoRenderingDevice(nameof(ThePolarMarksMatchTheirBaseline))) return;

        var view = ChartRenderHarness.AddView(ChartKind.Donut, ChartRenderHarness.ResizedViewSize);
        view.ConfigureMark(mark =>
        {
            if (mark is PieMark pie)
                pie.CenterText = "42%";
        });
        view.SetValues([("Search", 42.0), ("Direct", 28.0), ("Referral", 18.0), ("Social", 12.0)]);

        CaptureAndCompare(view, "polar-marks");
    }

    /// <summary>
    /// The flow path: node boxes, the cubic ribbons between them and their labels. Sampling a ribbon at the
    /// pointer's x, the node order in a column and the label text are the parts a threshold cannot see.
    /// </summary>
    [TestCase]
    public void TheFlowDiagramMatchesItsBaseline()
    {
        if (ChartRenderHarness.NoRenderingDevice(nameof(TheFlowDiagramMatchesItsBaseline))) return;

        var view = ChartRenderHarness.AddView(ChartKind.Sankey, ChartRenderHarness.ViewSize);
        SetFields(view, x: "", y: "value", color: "");
        view.SetCsv(
            "source,target,value\n" +
            "Applicants,Screen,30\nApplicants,Interview,20\n" +
            "Screen,Offer,12\nScreen,Rejected,18\n" +
            "Interview,Offer,10\nInterview,Rejected,10\n" +
            "Offer,Hired,18\n");

        CaptureAndCompare(view, "flow-diagram");
    }

    /// <summary>
    /// The light theme with a continuous colour scale. Every themed value the marks read (background, grid,
    /// axis, labels, the mark default and the ramp itself) is in this picture, which is how a colour that
    /// stopped following the theme - see <c>GC-B11</c> - gets caught for good.
    /// </summary>
    [TestCase]
    public void ALightThemedHeatmapMatchesItsBaseline()
    {
        if (ChartRenderHarness.NoRenderingDevice(nameof(ALightThemedHeatmapMatchesItsBaseline))) return;

        var view = ChartRenderHarness.AddView(ChartKind.Heatmap, ChartRenderHarness.ResizedViewSize);
        view.ThemeKind = ChartThemeKind.Light;
        view.ColorMapping = ColorMappingKind.Sequential;
        SetFields(view, x: "day", y: "slot", color: "load");
        view.SetCsv(
            "day,slot,load\n" +
            "Mon,Morning,12\nMon,Afternoon,34\nMon,Evening,58\n" +
            "Tue,Morning,18\nTue,Afternoon,41\nTue,Evening,63\n" +
            "Wed,Morning,9\nWed,Afternoon,29\nWed,Evening,47\n" +
            "Thu,Morning,22\nThu,Afternoon,52\nThu,Evening,71\n");

        CaptureAndCompare(view, "light-heatmap");
    }

    /// <summary>
    /// The layered path: the data layer is kept in an image and presented, and only the interaction state is
    /// drawn again. The pointer is moved before the capture, so the picture holds both halves - the blitted
    /// layer (its position and size, which a wrong blit would move) and the crosshair/tooltip drawn over it.
    /// <para>
    /// If the pointer injection ever turns out to be unstable on a real device, this fixture falls back to
    /// <c>LayeredRendering</c> alone: an interaction state that flaps would cost more than it is worth, and
    /// <c>MarkOverlayTest</c> plus the interaction integration cases cover that state without a picture.
    /// </para>
    /// </summary>
    [TestCase]
    public void TheLayeredFrameMatchesItsBaseline()
    {
        if (ChartRenderHarness.NoRenderingDevice(nameof(TheLayeredFrameMatchesItsBaseline))) return;

        var view = ChartRenderHarness.AddView(ChartKind.Line, ChartRenderHarness.ResizedViewSize);
        view.LayeredRendering = true;
        SetFields(view, x: "month", y: "value", color: "series");
        view.SetCsv(
            "month,series,value\n" +
            "Jan,Actual,12\nFeb,Actual,19\nMar,Actual,15\nApr,Actual,24\nMay,Actual,21\n" +
            "Jan,Plan,15\nFeb,Plan,16\nMar,Plan,18\nApr,Plan,20\nMay,Plan,22\n");

        try
        {
            // The data layer first: it is what the cache holds, and the overlay is drawn on top of it.
            AssertThat(ChartRenderHarness.PumpUntilStable(view, 60)).OverrideFailureMessage(
                "the surface never settled; a frame that is still changing cannot be compared with a baseline")
                .IsGreater(0);

            // A pointer move is what the layer cache exists for: from here on, only the overlay is redrawn.
            view._GuiInput(new InputEventMouseMotion { Position = LayeredPointer });
            AssertThat(ChartRenderHarness.PumpUntilStable(view, 60)).OverrideFailureMessage(
                "the surface never settled after the pointer moved").IsGreater(0);

            var rendered = ChartRenderHarness.Pixels(view);
            AssertThat(rendered).OverrideFailureMessage(
                "the view presents no pixels, so there is nothing to compare").IsNotNull();
            ChartRenderHarness.AssertMatchesGolden("layered-frame", rendered!);
        }
        finally
        {
            ChartRenderHarness.Release(view);
        }
    }

    /// <summary>
    /// Where the pointer is put in <see cref="TheLayeredFrameMatchesItsBaseline"/>: the February point of the
    /// "Actual" series (5 categories across the plot, 19 of a 0..25 axis), so the tooltip has a row to describe
    /// and not just the crosshair to draw.
    /// </summary>
    private static readonly Vector2 LayeredPointer = new(167f, 66f);

    // ── Plumbing ────────────────────────────────────────────────────────────

    /// <summary>
    /// Point the view at the columns of a fixture's CSV. The named fields are what makes a fixture readable:
    /// the conventions ("x", "y", "value") do not cover a heatmap or a flow diagram.
    /// </summary>
    /// <param name="view">View to configure.</param>
    /// <param name="x">Field the X channel reads, or an empty string to keep the convention.</param>
    /// <param name="y">Field the Y channel reads.</param>
    /// <param name="color">Field the colour channel reads.</param>
    private static void SetFields(ChartView view, string x, string y, string color)
    {
        view.XField = x;
        view.YField = y;
        view.ColorField = color;
    }

    /// <summary>
    /// Pump the view until its picture settles, capture it and compare it with the recorded baseline.
    /// </summary>
    /// <param name="view">View to render; it is released before this returns.</param>
    /// <param name="baseline">Baseline name without extension.</param>
    private static void CaptureAndCompare(ChartView view, string baseline)
    {
        try
        {
            AssertThat(ChartRenderHarness.PumpUntilStable(view, 60)).OverrideFailureMessage(
                "the surface never settled; a frame that is still changing cannot be compared with a baseline")
                .IsGreater(0);

            var rendered = ChartRenderHarness.Pixels(view);
            AssertThat(rendered).OverrideFailureMessage(
                "the view presents no pixels, so there is nothing to compare").IsNotNull();

            ChartRenderHarness.AssertMatchesGolden(baseline, rendered!);
        }
        finally
        {
            ChartRenderHarness.Release(view);
        }
    }
}
