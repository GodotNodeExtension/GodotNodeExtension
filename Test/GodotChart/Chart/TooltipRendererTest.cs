namespace GodotNodeExtension.Tests.GodotChart;

using System;
using System.Linq;
using GdUnit4;
using Godot;
using GodotNodeExtension.Component.GodotChart;
using GodotNodeExtension.Component.GodotChart.Canvas;
using Support;
using static GdUnit4.Assertions;

/// <summary>
/// Tests for <see cref="TooltipRenderer"/> and its content model (<see cref="TooltipLine"/> /
/// <see cref="TooltipSpan"/>): the fallback chain from explicit options through the theme to the
/// built-in defaults, edge flipping and clamping, the fade-out state, image-icon measuring and
/// drawing against the backend capabilities, and the span-to-font mapping.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class TooltipRendererTest
{
    private static DataRow D(params (string Field, object Value)[] fields)
    {
        var row = new DataRow(fields.Length);
        foreach (var (field, value) in fields)
            row.Set(field, value);
        return row;
    }

    private static void Approx(float actual, float expected, float tolerance = 1e-3f)
        => AssertThat(MathF.Abs(actual - expected) <= tolerance).IsTrue();

    private static (float X, float Y, float W, float H) DrawTooltip(
        TooltipRenderer renderer, FakeCanvas2D canvas, float screenX, float screenY,
        int canvasW = 100, int canvasH = 100)
    {
        renderer.Options.RichContentBuilder = _ => new[] { TooltipLine.Plain("value") };
        var hit = new HitResult
        {
            Hit = true, RowIndex = 0, Row = D(("value", 1.0)),
            ScreenX = screenX, ScreenY = screenY, Label = "value",
        };
        // Update() clamps delta to 0.1s, so a single call only moves the smooth-follow position
        // part of the way. Converge first, otherwise the box is positioned for an older pointer
        // position and the clamp assertions below would not mean anything.
        for (int i = 0; i < 80; i++)
            renderer.Update(0.1f, hit);
        canvas.Rects.Clear();
        renderer.Draw(canvas, canvasW, canvasH);
        AssertThat(canvas.Rects.Count > 0).IsTrue();
        return canvas.Rects[0]; // the background round-rect is drawn first
    }

    // ── Position, clamping and visibility ───────────────────────────────────

    [TestCase]
    public void TooltipFlipsAndClampsInsideTheCanvas()
    {
        // Near the right edge: the box is flipped to the left of the pointer.
        var flipped = DrawTooltip(new TooltipRenderer(), new FakeCanvas2D(), 95f, 50f);
        AssertThat(flipped.X + flipped.W <= 95f + 1e-3f).IsTrue();

        // Middle-right: the flipped box would leave the canvas, so it is clamped to the margin.
        var clamped = DrawTooltip(new TooltipRenderer(), new FakeCanvas2D(), 45f, 50f);
        Approx(clamped.X, 4f);
        AssertThat(clamped.X + clamped.W <= 100f + 1e-3f).IsTrue();

        // Near the top: the box is moved below the pointer.
        var below = DrawTooltip(new TooltipRenderer(), new FakeCanvas2D(), 10f, 5f);
        AssertThat(below.Y > 5f).IsTrue();
    }

    [TestCase]
    public void TooltipDoesNotDrawAfterFadingOut()
    {
        var canvas = new FakeCanvas2D();
        var renderer = new TooltipRenderer();
        DrawTooltip(renderer, canvas, 20f, 20f);
        AssertThat(renderer.IsVisible).IsTrue();

        renderer.Update(1f, null); // fade out
        renderer.Update(1f, null);

        canvas.Rects.Clear();
        renderer.Draw(canvas, 100, 100);
        AssertThat(canvas.Rects.Count).IsEqual(0);
    }

    // ── Options / theme / fallback chain ────────────────────────────────────

    [TestCase]
    public void TooltipFallsBackToDefaultsWhenTheThemeIsNull()
    {
        // Level 3: no theme, no options -> the hard-coded fallbacks.
        var canvas = new FakeCanvas2D();
        var renderer = new TooltipRenderer(); // Theme = null
        DrawTooltip(renderer, canvas, 20f, 20f);
        AssertThat(canvas.FillColors.Contains(new Color(0.12f, 0.12f, 0.18f, 0.92f))).IsTrue();
        AssertThat(canvas.FontOf("value").Size).IsEqual(12f);

        // Level 2: the theme supplies the fallbacks.
        var themed = new FakeCanvas2D();
        var theme = ChartTheme.Dark();
        theme.TooltipBackground = new Color(1f, 0f, 0f);
        theme.TooltipFontSize = 30f;
        var themedRenderer = new TooltipRenderer { Theme = theme };
        DrawTooltip(themedRenderer, themed, 20f, 20f);
        AssertThat(themed.FillColors.Contains(new Color(1f, 0f, 0f))).IsTrue();
        AssertThat(themed.FontOf("value").Size).IsEqual(30f);

        // Level 1: explicit options win over the theme.
        var explicitCanvas = new FakeCanvas2D();
        var explicitRenderer = new TooltipRenderer { Theme = theme };
        explicitRenderer.Options.BackgroundColor = new Color(0f, 1f, 0f);
        explicitRenderer.Options.FontSize = 18f;
        DrawTooltip(explicitRenderer, explicitCanvas, 20f, 20f);
        AssertThat(explicitCanvas.FillColors.Contains(new Color(0f, 1f, 0f))).IsTrue();
        AssertThat(explicitCanvas.FontOf("value").Size).IsEqual(18f);
    }

    // ── Content model ───────────────────────────────────────────────────────

    [TestCase]
    public void TooltipPlainAndWithIconMapTheirFields()
    {
        var plain = TooltipLine.Plain("hello");
        AssertThat(plain.Spans.Length).IsEqual(1);
        AssertThat(plain.Spans[0].Text).IsEqual("hello");
        AssertThat(plain.Spans[0].Icon).IsEqual(TooltipIcon.None);

        var nullText = TooltipLine.Plain(null);
        AssertThat(nullText.Spans[0].Text).IsEqual("");

        var icon = TooltipLine.WithIcon(TooltipIcon.Triangle, new Color(1f, 0f, 0f), "L");
        AssertThat(icon.Spans.Length).IsEqual(1);
        AssertThat(icon.Spans[0].Text).IsEqual("L");
        AssertThat(icon.Spans[0].Color).IsEqual(new Color(1f, 0f, 0f));
        AssertThat(icon.Spans[0].Icon).IsEqual(TooltipIcon.Triangle);
    }

    [TestCase]
    public void TooltipSpanToFontSettingsMapsEveryField()
    {
        var span = new TooltipSpan
        {
            Text = "t",
            Bold = true,
            Italic = true,
            FontSize = 18f,
            Decoration = TextDecoration.Underline | TextDecoration.Strikethrough,
            LetterSpacing = 2f,
            Family = "Consolas",
        };

        var font = span.ToFontSettings(12f);

        AssertThat(font.Size).IsEqual(18f);
        AssertThat(font.Bold).IsTrue();
        AssertThat(font.Italic).IsTrue();
        AssertThat(font.Decoration).IsEqual(TextDecoration.Underline | TextDecoration.Strikethrough);
        AssertThat(font.LetterSpacing).IsEqual(2f);
        AssertThat(font.Family).IsEqual("Consolas");
        AssertThat(font.GodotFont is null).IsTrue();

        // No override -> the caller's default font size is used.
        AssertThat(new TooltipSpan { Text = "t" }.ToFontSettings(15f).Size).IsEqual(15f);
    }

    // ── Image icons and backend capabilities ────────────────────────────────

    private static float TooltipWidth(TooltipRenderer renderer, ICanvas2D canvas, IImageHandle? image)
    {
        renderer.Options.RichContentBuilder = _ => new[]
        {
            image != null
                ? new TooltipLine { Spans = new[] { new TooltipSpan { Text = "value", Image = image } } }
                : TooltipLine.Plain("value"),
        };

        // The row is required: without it the tooltip cannot build a TooltipContext and silently
        // falls back to the plain label path.
        var hit = new HitResult
        {
            Hit = true, RowIndex = 0, Row = D(("value", 1.0)),
            ScreenX = 50f, ScreenY = 50f, Label = "value",
        };
        renderer.Update(1f, hit);

        var probe = (FakeCanvas2D)canvas;
        probe.Rects.Clear();
        renderer.Draw(canvas, 400, 300);

        // The tooltip background is the widest rect drawn (the text/icons are drawn after it). A tooltip that
        // drew nothing is a failure, not a width of 0: both sides of the comparisons below returning 0 would
        // make them trivially true.
        AssertThat(probe.Rects.Count).IsGreater(0);
        return probe.Rects.Max(r => r.W);
    }

    [TestCase]
    public void TooltipDoesNotReserveSpaceForAnIconItCannotDraw()
    {
        var canvas = new FakeCanvas2D
        {
            Capabilities = new CanvasCapabilities(
                SupportsGradients: true, SupportsClipping: true, SupportsTransforms: true,
                IsGpuBacked: false, SupportsLineDash: true, SupportsImages: false),
        };

        var withImage = new TooltipRenderer();
        float widthWithImage = TooltipWidth(withImage, canvas, new FakeImageHandle(8, 8));

        var withoutImage = new TooltipRenderer();
        float widthWithout = TooltipWidth(withoutImage, canvas, null);

        // The backend cannot draw images, so the image span must not inflate the tooltip box.
        AssertThat(MathF.Abs(widthWithImage - widthWithout) < 0.5f).IsTrue();
        AssertThat(canvas.ImageDrawCount).IsEqual(0);
    }

    [TestCase]
    public void TooltipDrawsTheImageWhenTheBackendSupportsIt()
    {
        var canvas = new FakeCanvas2D(); // SupportsImages = true by default

        float width = TooltipWidth(new TooltipRenderer(), canvas, new FakeImageHandle(8, 8));

        AssertThat(canvas.ImageDrawCount > 0).IsTrue();
        AssertThat(width > 0f).IsTrue();
    }
}
