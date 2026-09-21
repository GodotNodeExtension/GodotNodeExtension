namespace GodotNodeExtension.Tests.GodotChart.Marks;

using System;
using System.Collections.Generic;
using System.Linq;
using GdUnit4;
using Godot;
using GodotNodeExtension.Component.GodotChart;
using GodotNodeExtension.Component.GodotChart.Marks;
using Support;
using static GdUnit4.Assertions;

/// <summary>
/// Behaviour specification for <see cref="GaugeMark"/>.
/// <para>
/// Covers the arc geometry (start/end angles, outer radius, arc width and the inner-radius
/// override), the optional centre and min/max labels and the track/value colours, the
/// single-row rendering contract, and the value-arc hit test — including how out-of-range values
/// are clamped so the arc never sweeps past the scale. The skip of a non-finite value is pinned
/// once for every mark by <see cref="MarkDataSafetyTest"/>.
/// </para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class GaugeMarkTest
{
    private static readonly string[] SingleCategory = { "A" };

    // ── shared helpers ──

    /// <summary>Local floating point comparison: mark geometry is never bit-exact.</summary>
    private static bool Approx(double a, double b, double tol = 0.01) => Math.Abs(a - b) <= tol;

    private static DataRow D(params (string Field, object? Value)[] fields) => TestContexts.Row(fields);

    /// <summary>The gauge sweep in radians: the absolute difference of the two angle properties.</summary>
    private static float TotalSweep(GaugeMark mark)
        => MathF.Abs(mark.EndAngleDeg - mark.StartAngleDeg) * MathF.PI / 180f;

    private static (GaugeMark Mark, FakeCanvas2D Canvas, MarkContext Ctx) GaugeCtx(List<DataRow> rows)
    {
        var canvas = new FakeCanvas2D();
        var ctx = TestContexts.Mark(canvas, rows, TestContexts.XyEncodes("cat", "value"),
            TestContexts.CategoryScales(SingleCategory));
        return (new GaugeMark(), canvas, ctx);
    }

    /// <summary>
    /// Render a gauge through a real <see cref="Chart"/> so the Y scale can be supplied explicitly
    /// (used by the out-of-range and full-circle cases).
    /// </summary>
    private static Chart GaugeChart(FakeCanvas2D canvas, double value, GaugeMark mark)
    {
        var chart = new Chart(canvas);
        chart.Data(new List<DataRow> { D(("metric", "load"), ("value", value)) });
        chart.Mark(mark);
        chart.Encode(Channel.X, "metric");
        chart.Encode(Channel.Y, "value");
        return chart;
    }

    // ── Default rendering ──

    [TestCase]
    public void GaugeAnglesReachTheArc()
    {
        var rows = new List<DataRow> { D(("cat", "A"), ("value", 50.0)) };
        var (mark, canvas, ctx) = GaugeCtx(rows);
        mark.Render(ctx);

        float startRad = -210f * MathF.PI / 180f;
        float totalSweep = 240f * MathF.PI / 180f;
        AssertThat(Approx(canvas.Arcs[0].Start, startRad)).IsTrue();
        AssertThat(Approx(canvas.Arcs[0].End, startRad + totalSweep)).IsTrue();

        var (mark2, canvas2, ctx2) = GaugeCtx(rows);
        mark2.StartAngleDeg = 0f;
        mark2.EndAngleDeg = 180f;
        mark2.Render(ctx2);
        AssertThat(Approx(canvas2.Arcs[0].Start, 0)).IsTrue();
        AssertThat(Approx(canvas2.Arcs[0].End, MathF.PI)).IsTrue();
    }

    [TestCase]
    public void GaugeHalfValueSweepsHalfOfTheArc()
    {
        var rows = new List<DataRow> { D(("cat", "A"), ("value", 50.0)) };
        var (mark, canvas, ctx) = GaugeCtx(rows);
        mark.Render(ctx);

        // Arcs[0..1] are the track, Arcs[2..3] the value band.
        float total = 240f * MathF.PI / 180f;
        AssertThat(Approx(canvas.Arcs[2].End - canvas.Arcs[2].Start, total * 0.5f)).IsTrue();
    }

    [TestCase]
    public void GaugeRendersOnlyTheFirstRowAndNothingWithoutData()
    {
        var rows = new List<DataRow>
        {
            D(("cat", "A"), ("value", 50.0)),
            D(("cat", "B"), ("value", 100.0)),
        };
        var (mark, canvas, ctx) = GaugeCtx(rows);
        mark.Render(ctx);

        AssertThat(canvas.FillCount).IsEqual(2);   // track + a single value band
        // ...and the band is row 0's: half the arc, where row 1's value (100) would have swept all of it.
        float total = 240f * MathF.PI / 180f;
        AssertThat(Approx(canvas.Arcs[2].End - canvas.Arcs[2].Start, total * 0.5f)).IsTrue();

        var (empty, emptyCanvas, emptyCtx) = GaugeCtx([]);
        empty.Render(emptyCtx);
        AssertThat(emptyCanvas.DrewAnything).IsFalse();
    }

    // ── Options ──

    [TestCase]
    public void GaugeArcWidthAndInnerRadiusRatioSetTheInnerRadius()
    {
        var rows = new List<DataRow> { D(("cat", "A"), ("value", 50.0)) };
        const float outer = 150f * 0.85f;   // RadiusFactor default is 0.85

        var (mark, canvas, ctx) = GaugeCtx(rows);
        mark.ArcWidth = 0.25f;
        mark.Render(ctx);
        AssertThat(Approx(canvas.Arcs.Max(a => a.Radius), outer)).IsTrue();
        AssertThat(Approx(canvas.Arcs.Min(a => a.Radius), outer * 0.75f)).IsTrue();

        // A positive InnerRadiusRatio overrides the width derived from ArcWidth.
        var (mark2, canvas2, ctx2) = GaugeCtx(rows);
        mark2.ArcWidth = 0.25f;
        mark2.InnerRadiusRatio = 0.6f;
        mark2.Render(ctx2);
        AssertThat(Approx(canvas2.Arcs.Min(a => a.Radius), outer * 0.6f)).IsTrue();
    }

    [TestCase]
    public void GaugeRadiusFactorScalesTheArc()
    {
        var rows = new List<DataRow> { D(("cat", "A"), ("value", 50.0)) };

        foreach (var factor in new[] { 0.5f, 1.0f })
        {
            var (mark, canvas, ctx) = GaugeCtx(rows);
            mark.RadiusFactor = factor;
            mark.Render(ctx);
            AssertThat(Approx(canvas.Arcs.Max(a => a.Radius), 150f * factor)).IsTrue();
        }
    }

    [TestCase]
    public void GaugeCenterLabelAndColorsAreOptional()
    {
        var rows = new List<DataRow> { D(("cat", "A"), ("value", 50.0)) };
        var track = new Color(0.1f, 0.2f, 0.3f);
        var value = new Color(0.9f, 0.1f, 0.1f);

        var (mark, canvas, ctx) = GaugeCtx(rows);
        mark.TrackColor = track;
        mark.ValueColor = value;
        mark.Render(ctx);

        AssertThat(canvas.FillColors[0].ToHtml()).IsEqual(track.ToHtml());
        AssertThat(canvas.FillColors[1].ToHtml()).IsEqual(value.ToHtml());
        AssertThat(canvas.Texts).Contains("50");

        var (plain, plainCanvas, plainCtx) = GaugeCtx(rows);
        plain.TrackColor = track;
        plain.ValueColor = value;
        plain.ShowCenterLabel = false;
        plain.Render(plainCtx);

        AssertThat(plainCanvas.Texts.Contains("50")).IsFalse();
        AssertThat(plainCanvas.Texts.Count).IsEqual(2);   // min and max labels remain
    }

    // ── Selection ──

    /// <summary>
    /// With no colour set, the value arc follows the theme like every other mark does. The mark used to carry a
    /// blue of its own as the property default, which is the base (dark) theme's mark colour: a light chart drew
    /// that one instead of the light theme's, unlike every sibling mark.
    /// </summary>
    [TestCase]
    public void TheValueArcUsesTheThemeDefaultColor()
    {
        var rows = new List<DataRow> { D(("cat", "A"), ("value", 50.0)) };
        var theme = ChartTheme.Light();
        var canvas = new FakeCanvas2D();
        var ctx = TestContexts.Mark(canvas, rows, TestContexts.XyEncodes("cat", "value"),
            TestContexts.CategoryScales(SingleCategory), theme: theme);

        new GaugeMark().Render(ctx);

        AssertThat(canvas.FillColors.Count).IsEqual(2);              // the track, then the value arc
        AssertThat(canvas.FillColors[0].ToHtml()).IsEqual(theme.GaugeTrackColor.ToHtml());
        AssertThat(canvas.FillColors[1].ToHtml()).IsEqual(theme.DefaultMarkColor.ToHtml());
    }

    [TestCase]
    public void TheSelectedGaugeGetsTheSelectionRing()
    {
        var rows = new List<DataRow> { D(("cat", "A"), ("value", 50.0)) };
        var canvas = new FakeCanvas2D();
        var ctx = TestContexts.Mark(canvas, rows, TestContexts.XyEncodes("cat", "value"),
            TestContexts.CategoryScales(SingleCategory), selectedRowIndex: 0);

        var mark = new GaugeMark();
        mark.States.SelectedStroke = new Color(1f, 0f, 1f);
        mark.States.SelectedStrokeWidth = 4f;
        mark.Render(ctx);

        // The track and the value arc are filled, never stroked, so the ring is the only stroke.
        AssertThat(canvas.StrokeCount).IsEqual(1);
        AssertThat(canvas.StrokeColors[0]).IsEqual(new Color(1f, 0f, 1f));
        AssertThat(canvas.StrokeWidths[0]).IsEqual(4f);

        var plainCanvas = new FakeCanvas2D();
        new GaugeMark().Render(TestContexts.Mark(plainCanvas, rows,
            TestContexts.XyEncodes("cat", "value"),
            TestContexts.CategoryScales(SingleCategory)));
        AssertThat(plainCanvas.StrokeCount).IsEqual(0);
    }

    // ── Hit testing ──

    [TestCase]
    public void GaugeHitTestMissesTheCentreAndHitsTheValueArc()
    {
        var rows = new List<DataRow> { D(("cat", "A"), ("value", 50.0)) };
        var (mark, _, ctx) = GaugeCtx(rows);
        mark.Render(ctx);

        AssertThat(mark.HitTest(ctx, new Vector2(200f, 150f)) is null).IsTrue();

        const float outer = 150f * 0.85f;
        float inner = outer * 0.88f;                 // default ArcWidth 0.12
        float r = (outer + inner) / 2f;
        float mid = -210f * MathF.PI / 180f + 240f * MathF.PI / 180f * 0.25f;   // inside the value band

        var hit = mark.HitTest(ctx, new Vector2(200f + r * MathF.Cos(mid), 150f + r * MathF.Sin(mid)));
        AssertThat(hit is not null).IsTrue();
        AssertThat(hit!.RowIndex).IsEqual(0);
        AssertThat(Approx(hit.ScreenX, 200)).IsTrue();
        AssertThat(Approx(hit.ScreenY, 150)).IsTrue();
    }

    // ── Degenerate data ──

    [TestCase]
    public void OutOfRangeGaugeValueDoesNotSweepPastTheScale()
    {
        var mark = new GaugeMark();
        var canvas = new FakeCanvas2D();
        using var chart = GaugeChart(canvas, 250.0, mark);
        chart.Scale(Channel.Y, new LinearScale(0, 100)); // value is 2.5x the maximum

        chart.Render();

        float limit = TotalSweep(mark) + 1e-3f;
        var offenders = canvas.Arcs
            .Where(a => MathF.Abs(a.End - a.Start) > limit)
            .ToList();

        AssertThat(offenders.Count).IsEqual(0);
    }

    [TestCase]
    public void NegativeGaugeValueDoesNotSweepBackwards()
    {
        var mark = new GaugeMark();
        var canvas = new FakeCanvas2D();
        using var chart = GaugeChart(canvas, -50.0, mark);
        chart.Scale(Channel.Y, new LinearScale(0, 100));

        chart.Render();

        // A negative normalized value previously wrapped to a ~340 degree sweep. The value arc is
        // the last one drawn; it must collapse to nothing instead of sweeping backwards.
        AssertThat(canvas.Arcs.Count >= 2).IsTrue();
        var valueArc = canvas.Arcs[^1];
        AssertThat(MathF.Abs(valueArc.End - valueArc.Start) < 1e-3f).IsTrue();
    }

    [TestCase]
    public void FullCircleGaugeNeverRequestsACompleteTurn()
    {
        var mark = new GaugeMark { StartAngleDeg = 0f, EndAngleDeg = 360f };
        var canvas = new FakeCanvas2D();
        using var chart = GaugeChart(canvas, 75.0, mark);
        chart.Scale(Channel.Y, new LinearScale(0, 100));

        chart.Render();

        // A sweep of exactly 2*pi becomes 0 in the backend (modulo), which makes the arc vanish.
        const float tau = MathF.Tau;
        var offenders = canvas.Arcs
            .Where(a => MathF.Abs(a.End - a.Start) >= tau - 1e-6f)
            .ToList();

        AssertThat(offenders.Count).IsEqual(0);
    }

    /// <summary>
    /// <see cref="GaugeMark.ShowMinMaxLabels"/> = false drops the two end labels; the centre value is a
    /// different switch, so it stays.
    /// </summary>
    [TestCase]
    public void TurningOffTheMinMaxLabelsDropsThem()
    {
        var (onMark, onCanvas, onCtx) = GaugeCtx([D(("metric", "load"), ("value", 50.0))]);
        onMark.Render(onCtx);

        var (offMark, offCanvas, offCtx) = GaugeCtx([D(("metric", "load"), ("value", 50.0))]);
        offMark.ShowMinMaxLabels = false;
        offMark.Render(offCtx);

        AssertThat(onCanvas.Texts.Count).IsGreater(offCanvas.Texts.Count);
        AssertThat(offCanvas.Texts).Contains("50");       // the centre value is still read out
    }
}
