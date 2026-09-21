namespace GodotNodeExtension.Tests.Typography;

using System.Collections.Generic;
using GdUnit4;
using Godot;
using GodotNodeExtension.Component.Typography.Core;
using GodotNodeExtension.Component.Typography.Core.Model;
using SkiaSharp;
using static GdUnit4.Assertions;

/// <summary>
/// Specification for two things the wrap contract promises and that had no test: a float's lifecycle (it belongs to
/// a range of lines, not to the whole layout) and what happens to a hole in a shape. Plus the emoji side of grapheme
/// integrity, which the Unicode line breaking rules are supposed to give for free.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class TypographyFloatAndEmojiTest
{
    /// <summary>A float that belongs to one line only affects that line.</summary>
    [TestCase]
    public void AFloatOnlyAffectsTheLinesItBelongsTo()
    {
        List<LayoutLine> lines = LayOut("这一段文字用来检验浮动区域的生效行范围，因此需要写得足够长，至少要排满三行以上。",
            new WrapRegion
            {
                Shape = new RectWrapShape { Width = 60f, Height = 200f },
                Position = Vector2.Zero,
                WrapMode = WrapFloat.Left,
                Margin = 0f,
                FirstLine = 1,
                LastLine = 1,
            });

        AssertThat(lines.Count).IsGreater(2);

        AssertThat(lines[0].Spans[0].Left).OverrideFailureMessage(
            "the float does not belong to the first line").IsEqual(0f);
        AssertThat(lines[1].Spans[0].Left).OverrideFailureMessage(
            "the float belongs to the second line").IsGreater(50f);
        AssertThat(lines[2].Spans[0].Left).OverrideFailureMessage(
            "the float stopped after the second line").IsEqual(0f);
    }

    /// <summary>
    /// A hole in an alpha-derived shape is filled: each row contributes the interval between its first and last
    /// opaque pixel, so text never flows into the hole.
    /// </summary>
    [TestCase]
    public void AHoleInAShapeIsFilled()
    {
        using var bitmap = new SKBitmap(64, 64);

        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Transparent);
            using var paint = new SKPaint { Color = SKColors.Black, IsAntialias = false };
            canvas.DrawCircle(32f, 32f, 28f, paint);         // the ring's body
            paint.BlendMode = SKBlendMode.Clear;
            paint.Color = SKColors.Transparent;
            canvas.DrawCircle(32f, 32f, 12f, paint);         // the hole
        }

        PolygonWrapShape shape = ShapeContourExtractor.ExtractFromBitmap(
            bitmap, new Vector2(64f, 64f), alphaThreshold: 10, margin: 0f);

        var spans = new List<LineSpan>();
        shape.GetSpansAtY(32f, 1f, spans);

        AssertThat(spans.Count).OverrideFailureMessage("a row of a ring is one interval").IsEqual(1);
        AssertThat(spans[0].Left < 32f && spans[0].Right > 32f).OverrideFailureMessage(
            $"the hole is filled, so the interval covers its centre (got [{spans[0].Left},{spans[0].Right}])").IsTrue();
    }

    /// <summary>
    /// An emoji sequence stays one element: a zero-width joiner, a variation selector, a regional-indicator pair and
    /// a skin-tone modifier all belong to the character they modify, and no line break may fall inside one.
    /// </summary>
    [TestCase]
    public void EmojiSequencesStayInOneElement()
    {
        string family = "\U0001F468\u200D\U0001F469\u200D\U0001F467";   // man + ZWJ + woman + ZWJ + girl
        string flag = "\U0001F1EF\U0001F1F5";                            // regional indicators for JP
        string skinTone = "\U0001F44D\U0001F3FD";                        // thumbs up + skin tone

        foreach (string sequence in new[] { family, flag, skinTone })
        {
            List<LayoutLine> lines = LayOut(sequence, wrapRegion: null);

            AssertThat(lines[0].Elements.Count).OverrideFailureMessage(
                $"the sequence {sequence} was cut into {lines[0].Elements.Count} elements").IsEqual(1);
            AssertThat(lines[0].Elements[0].Text).IsEqual(sequence);
        }
    }

    // ── Helpers ──

    private static List<LayoutLine> LayOut(string text, WrapRegion? wrapRegion)
    {
        DrawElement[] elements =
        [
            new()
            {
                Type = DrawElement.ElementType.Text,
                Text = text,
                Font = ThemeDB.FallbackFont,
                FontSize = 16,
                Color = new Color(0.1f, 0.1f, 0.1f),
            },
        ];

        var settings = new TypographySettings
        {
            MaxWidth = 240f,
            LanguageTag = "zh-Hans",
            FirstLineIndent = 0,
            Alignment = TextAlignment.Left,
        };

        if (wrapRegion is not null)
            settings.WrapRegions.Add(wrapRegion);

        using var engine = new TypographyEngine(settings);
        return engine.PrepareAndLayout(elements, out _);
    }
}
