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
using static GodotNodeExtension.Tests.GodotChart.Support.Asserts;

/// <summary>
/// Minimal mark that pushes one label element per row through the base label pipeline
/// (<see cref="Mark"/>'s protected <c>DrawLabels</c>), so the shared label behaviour -
/// <c>LabelContentBuilder</c> and its fallback to the formatted text - can be exercised without going
/// through a concrete mark's geometry. It mirrors what the built-in interval/line/point marks do.
/// </summary>
internal sealed class LabelPipelineMark : Mark
{
    /// <inheritdoc />
    public override MarkCoordinate Coordinate => MarkCoordinate.Cartesian;

    /// <summary>
    /// When true the elements carry their row, row index, fill and value; when false they carry the
    /// text alone (the shape a mark that only formats text uses).
    /// </summary>
    public bool FeedElementDetails { get; set; } = true;

    /// <inheritdoc />
    public override void Render(MarkContext ctx)
    {
        var labels = BeginLabelCollection();
        if (labels is null) return;

        for (int i = 0; i < ctx.Data.Count; i++)
        {
            var row = ctx.Data[i];
            var yRaw = ctx.Encodes.Resolve(YChannel, row);
            var xRaw = ctx.Encodes.Resolve(Channel.X, row);
            string text = FormatLabel(LabelFormat, yRaw, xRaw);

            labels.Add(FeedElementDetails
                ? new LabelElement(i * 10f, 100f, text, 1f, row, i, Colors.Red, LabelValue(yRaw))
                : new LabelElement(i * 10f, 100f, text));
        }

        DrawLabels(ctx, labels);
    }
}

/// <summary>
/// Behaviour specification for the label content hook of <see cref="Mark"/>: the built-in label
/// pipeline draws the lines <c>LabelContentBuilder</c> returns for an element, keeps the element's own
/// text when the callback declines, and stacks several returned lines below each other. The built-in
/// marks that draw through the pipeline hand the row, its index and its value over.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class MarkLabelContentBuilderTest
{
    private static readonly string[] Categories = { "A", "B" };
    private static readonly string[] ThreeCategories = { "A", "B", "C" };

    // ── helpers ─────────────────────────────────────────────────────────────

    /// <summary>Local floating point comparison: label geometry is never bit-exact.</summary>

    private static DataRow D(params (string Field, object? Value)[] fields) => TestContexts.Row(fields);

    private static List<DataRow> Rows() =>

    [
        D(("cat", "A"), ("value", 10.0)),
        D(("cat", "B"), ("value", 20.0)),
    ];

    private static (LabelPipelineMark Mark, FakeCanvas2D Canvas, MarkContext Ctx) Pipeline(
        string format = "{0}", bool details = true, int selected = -1)
    {
        var canvas = new FakeCanvas2D();
        var ctx = TestContexts.Mark(canvas, Rows(), TestContexts.XyEncodes("cat", "value"),
            TestContexts.CategoryScales(Categories, 0, 30), selectedRowIndex: selected);
        var mark = new LabelPipelineMark
        {
            ShowLabel = true, LabelFormat = format, FeedElementDetails = details,
        };
        return (mark, canvas, ctx);
    }

    // ── The builder replaces the label text ─────────────────────────────────

    [TestCase]
    public void TheBuilderTextReplacesTheFormattedLabel()
    {
        var (mark, canvas, ctx) = Pipeline();
        mark.LabelContentBuilder = c => new[] { TooltipLine.Plain($"#{c.RowIndex}={c.DefaultText}") };
        mark.Render(ctx);

        AssertThat(canvas.Texts).Contains("#0=10");
        AssertThat(canvas.Texts).Contains("#1=20");
        AssertThat(canvas.Texts.Contains("10")).IsFalse();   // the formatted text is gone
    }

    [TestCase]
    public void TheBuilderReceivesTheRowTheIndexAndTheValue()
    {
        var (mark, canvas, ctx) = Pipeline();
        mark.LabelContentBuilder = c =>
            new[] { TooltipLine.Plain($"{c.Row!.Get("cat")}/{c.RowIndex}/{c.Value}") };
        mark.Render(ctx);

        AssertThat(canvas.Texts).Contains("A/0/10");
        AssertThat(canvas.Texts).Contains("B/1/20");
    }

    // ── Declining the callback keeps the formatted text ─────────────────────

    [TestCase]
    public void ANullOrEmptyResultFallsBackToTheFormattedText()
    {
        var (nullBuilder, nullCanvas, nullCtx) = Pipeline("{0}%");
        nullBuilder.LabelContentBuilder = _ => null;
        nullBuilder.Render(nullCtx);
        AssertThat(nullCanvas.Texts).Contains("10%");

        var (emptyBuilder, emptyCanvas, emptyCtx) = Pipeline("{0}%");
        emptyBuilder.LabelContentBuilder = _ => Array.Empty<TooltipLine>();
        emptyBuilder.Render(emptyCtx);
        AssertThat(emptyCanvas.Texts).Contains("10%");
    }

    [TestCase]
    public void WithoutABuilderTheFormattedTextIsDrawn()
    {
        var (mark, canvas, ctx) = Pipeline("{0}%");
        mark.Render(ctx);

        AssertThat(canvas.Texts).Contains("10%");
        AssertThat(canvas.Texts).Contains("20%");
    }

    [TestCase]
    public void TheBuilderWorksOnElementsThatCarryNoRow()
    {
        var (mark, canvas, ctx) = Pipeline(details: false);
        mark.LabelContentBuilder = c => new[] { TooltipLine.Plain($"x{c.DefaultText}") };
        mark.Render(ctx);

        AssertThat(canvas.Texts).Contains("x10");
        AssertThat(canvas.Texts).Contains("x20");
    }

    // ── Several lines ───────────────────────────────────────────────────────

    [TestCase]
    public void SeveralLinesStackBelowTheFirstOne()
    {
        var (mark, canvas, ctx) = Pipeline();
        mark.LabelContentBuilder = _ => new[]
        {
            TooltipLine.Plain("line 1"),
            TooltipLine.Plain("line 2"),
        };
        mark.Render(ctx);

        var first = canvas.TextDraws.First(d => d.Text == "line 1");
        var second = canvas.TextDraws.First(d => d.Text == "line 2");

        // Same-length lines, so an equal x also proves both are centred on the same anchor.
        AssertThat(second.Y > first.Y).IsTrue();
        AssertThat(second.X).IsEqual(first.X);

        // The lines are one line box apart, measured with the label font.
        float lineHeight = canvas.MeasureText("0", canvas.FontOf("line 1")).Height;
        AssertThat(Approx(second.Y - first.Y, lineHeight)).IsTrue();
    }

    [TestCase]
    public void SpanColorAndStyleReachTheCanvas()
    {
        var (mark, canvas, ctx) = Pipeline();
        mark.LabelContentBuilder = _ => new[]
        {
            new TooltipLine
            {
                Spans =
                [
                    new TooltipSpan { Text = "bold", Color = Colors.Red, Bold = true },
                    new TooltipSpan { Text = "+plain" },
                ],
            },
        };
        mark.Render(ctx);

        AssertThat(canvas.Texts).Contains("bold");
        AssertThat(canvas.Texts).Contains("+plain");
        AssertThat(canvas.TextColorsOf("bold").First().ToHtml(false)).IsEqual(Colors.Red.ToHtml(false));
        AssertThat(canvas.FontOf("bold").Bold).IsTrue();
        // The spans of one line are laid out side by side, not on top of each other.
        AssertThat(canvas.TextDraws.First(d => d.Text == "+plain").X
                   > canvas.TextDraws.First(d => d.Text == "bold").X).IsTrue();
    }

    // ── The built-in marks feed the hook ─────────────────────────────────────

    [TestCase]
    public void TheBuiltInLabelPipelineMarksFeedTheHook()
    {
        var canvas = new FakeCanvas2D();
        var mark = new IntervalMark { ShowLabel = true };
        mark.LabelContentBuilder = c => new[] { TooltipLine.Plain($"<{c.RowIndex}:{c.Value}>") };
        mark.Render(TestContexts.Mark(canvas, MarkCases.Simple(),
            TestContexts.XyEncodes("cat", "value"),
            TestContexts.CategoryScales(ThreeCategories, 0, 30)));

        AssertThat(canvas.Texts).Contains("<0:10>");
        AssertThat(canvas.Texts).Contains("<2:15>");
    }
}
