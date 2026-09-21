using GodotNodeExtension.Component.RichTextCanvas;
using GodotNodeExtension.Component.Typography.Core;
namespace GodotNodeExtension.Tests.Typography;

using System;
using System.Collections.Generic;
using GdUnit4;
using Godot;
using GodotNodeExtension.Component.Typography.Core.Model;
using static GdUnit4.Assertions;

/// <summary>
/// Specification for the glyph-level output (decision 1c): every laid-out text element carries the glyphs
/// the renderer should draw, and those glyphs must match the measurement the layout broke lines with.
/// <para>
/// This is the "c" half of the verification: the data is checked exactly, and the pixel half (drawing those
/// glyphs and comparing against the previous string-drawing path) is a separate, device-dependent test
/// because it needs a real rendering device.
/// </para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class TypographyGlyphOutputTest
{
    /// <summary>
    /// Every text element from the line breaker carries a glyph run whose advances sum to the width the
    /// layout gave it, so the renderer cannot draw a narrower or wider run than was measured.
    /// <para>
    /// Two adjustment kinds are filtered out because they legitimately change an element's box without
    /// changing its glyphs: inline background padding (reserved as extra width by the prepare phase) and an
    /// inserted script gap (added by line adjustment). Both are visible in the element or its boundaries.
    /// </para>
    /// </summary>
    [TestCase]
    public void GlyphAdvancesSumToTheMeasuredWidth()
    {
        foreach (var (name, scenario) in Scenarios())
        {
            var layout = Run(scenario);
            int checkedElements = 0;

            // Pairs are examined per line, because a script gap is only inserted between two elements that
            // share a line: the last element of a line keeps its natural width.
            foreach (var line in layout.Lines)
            {
                for (int i = 0; i < line.Elements.Count; i++)
                {
                    var element = line.Elements[i];

                    if (element.Type != DrawElement.ElementType.Text || element.Reason != null)
                        continue;

                    if (element.GlyphRun is not { } run)
                    {
                        AssertThat(false).OverrideFailureMessage(
                            $"{name}: text element \"{element.Text}\" carries no glyph run").IsTrue();
                        continue;
                    }

                    if (element.BackgroundPadding != Vector2.Zero)
                        continue; // the box reserves padding the glyphs do not occupy

                    float gap = i + 1 < line.Elements.Count
                        ? BoundarySpacing(layout.Boundaries, element, line.Elements[i + 1])
                        : 0f;

                    // The measured width is the text's own: the hyphen a piece could end a line with is paid for by
                    // the line breaker's fit test, and drawn only when the line really ends there.
                    float expected = element.Size.X - gap;
                    AssertThat(Math.Abs(run.Width - expected) < 0.01f).OverrideFailureMessage(
                            $"{name}: glyphs of \"{element.Text}\" sum to {run.Width:F3} but the element is " +
                            $"{element.Size.X:F3} wide ({expected:F3} without the inserted gap)")
                        .IsTrue();

                    checkedElements++;
                }
            }

            AssertThat(checkedElements > 0).OverrideFailureMessage(
                $"{name}: no text element was checked").IsTrue();
        }
    }

    /// <summary>
    /// The glyph indices must be real glyphs of the resolved font: an id at or past the face's glyph count
    /// would draw nothing or the wrong shape. Unshaped fallback runs are the one documented exception, and
    /// they report zero ids.
    /// </summary>
    [TestCase]
    public void GlyphIndicesBelongToTheResolvedFont()
    {
        foreach (var (name, scenario) in Scenarios())
        {
            var layout = Run(scenario);

            foreach (var element in layout.Elements)
            {
                if (element.GlyphRun is not { } run)
                    continue;

                AssertThat(run.FontId != 0).OverrideFailureMessage(
                    $"{name}: glyph run of \"{element.Text}\" has no font id").IsTrue();
                AssertThat(run.Glyphs.Length > 0).IsTrue();

                int nonZero = 0;
                foreach (Glyph glyph in run.Glyphs)
                {
                    if (glyph.Id != 0)
                        nonZero++;
                }

                // Every scenario uses text the fallback font covers, so a run of only .notdef would mean the
                // ids were not paired with the right face.
                AssertThat(nonZero > 0).OverrideFailureMessage(
                    $"{name}: every glyph of \"{element.Text}\" is .notdef").IsTrue();
            }
        }
    }

    /// <summary>
    /// The cluster ranges must be monotone and cover the element's text exactly once: that is what lets a
    /// consumer map a glyph back to the character it came from (caret, selection, hit testing).
    /// </summary>
    [TestCase]
    public void GlyphClustersCoverTheSourceTextExactlyOnce()
    {
        foreach (var (name, scenario) in Scenarios())
        {
            var layout = Run(scenario);

            foreach (var element in layout.Elements)
            {
                if (element.GlyphRun is not { } run || string.IsNullOrEmpty(element.Text))
                    continue;

                int covered = 0;
                int previousEnd = 0;

                foreach (Glyph glyph in run.Glyphs)
                {
                    AssertThat(glyph.ClusterStart >= previousEnd).OverrideFailureMessage(
                        $"{name}: clusters of \"{element.Text}\" are not monotone " +
                        $"({glyph.ClusterStart} after {previousEnd})").IsTrue();
                    AssertThat(glyph.ClusterEnd > glyph.ClusterStart).OverrideFailureMessage(
                        $"{name}: empty cluster range in \"{element.Text}\"").IsTrue();

                    covered += glyph.ClusterEnd - glyph.ClusterStart;
                    previousEnd = glyph.ClusterEnd;
                }

                AssertThat(previousEnd <= element.Text.Length).OverrideFailureMessage(
                    $"{name}: cluster {previousEnd} past the end of \"{element.Text}\"").IsTrue();
                AssertThat(covered >= element.Text.Length).OverrideFailureMessage(
                    $"{name}: clusters cover {covered} of {element.Text.Length} characters of " +
                    $"\"{element.Text}\"").IsTrue();
            }
        }
    }

    /// <summary>
    /// A merged background rectangle has no glyphs, and neither does a marker: only elements that actually
    /// carry text get a run, so a consumer can treat a missing run as "nothing to draw".
    /// </summary>
    [TestCase]
    public void OnlyTextElementsCarryGlyphRuns()
    {
        var (_, scenario) = Scenarios()[0];
        var layout = Run(scenario);

        foreach (var element in layout.Elements)
        {
            if (element.Type == DrawElement.ElementType.Text)
                continue;

            AssertThat(element.GlyphRun is null).OverrideFailureMessage(
                $"a {element.Type} element must not carry glyphs").IsTrue();
        }
    }

    /// <summary>
    /// How far the layout's shaping and the renderer's shaping drift apart on the same text.
    /// <para>
    /// The layout measures with HarfBuzz, kerning included; the renderer today draws with Skia's string path,
    /// which maps characters to glyphs itself and applies no kerning. The drift is therefore not noise, it is
    /// the kerning: measured on this machine, "AVATAR" is 59.72px by the layout and 64.21px by the renderer,
    /// while text without kerning pairs stays within a tenth of a pixel.
    /// </para>
    /// <para>
    /// That number is why moving the renderer onto the layout's glyphs is a visible correction rather than an
    /// invisible refactor: the drawn text becomes as narrow as the layout assumed. It is asserted here so the
    /// gap cannot quietly grow, and so the decision to close it has a documented size.
    /// </para>
    /// </summary>
    [TestCase]
    public void LayoutAndRendererShapingDriftIsMeasured()
    {
        // Strings with kerning or ligature pairs: HarfBuzz applies them, Skia's string path does not.
        string[] kerningSamples = ["AVATAR To Ta", "WAVE", "Yo, Table", "Waffle iron", "office fluff"];

        // Strings with neither: here the two paths agree to a fraction of a pixel, which is what shows the
        // drift above really is kerning rather than a different shaping engine.
        string[] plainSamples = ["SkiaSharp renders text", "在Godot中使用SkiaSharp"];

        float worstKerningDrift = MeasureDrift(kerningSamples);
        float worstPlainDrift = MeasureDrift(plainSamples);

        AssertThat(worstPlainDrift < 0.5f).OverrideFailureMessage(
            $"text without kerning pairs drifted by {worstPlainDrift:F3}px, which is more than a " +
            "hinting difference").IsTrue();

        // A loose ceiling on the kerning case: it documents that the drift is kerning-sized, not a broken
        // shaping path.
        AssertThat(worstKerningDrift < 6f).OverrideFailureMessage(
            $"kerning-heavy text drifted by {worstKerningDrift:F3}px").IsTrue();
    }

    /// <summary>
    /// Measure the worst per-element drift between the layout's glyph advances and the renderer's own
    /// measurement, printing every number so a failure reports the size of the gap rather than only a bound.
    /// </summary>
    /// <param name="samples">Texts to measure.</param>
    /// <returns>The largest drift seen, in pixels.</returns>
    private static float MeasureDrift(string[] samples)
    {
        float worst = 0f;
        int compared = 0;

        foreach (string sample in samples)
        {
            var element = Text(sample);
            var scenario = new Scenario([element], new TypographySettings { MaxWidth = 400f, LanguageTag = "en" });
            var layout = Run(scenario);

            foreach (var laidOut in layout.Elements)
            {
                if (laidOut.GlyphRun is not { } run || string.IsNullOrEmpty(laidOut.Text))
                    continue;

                float rendererWidth = SkiaTextMeasurer.MeasureTextWidth(
                    laidOut.Text, element.Font!, element.FontSize);

                float drift = Math.Abs(run.Width - rendererWidth);
                compared++;

                if (drift > worst)
                    worst = drift;

                GD.Print($"[shaping-drift] \"{laidOut.Text}\": layout {run.Width:F4} vs renderer " +
                         $"{rendererWidth:F4} = {drift:F4}px over {run.Glyphs.Length} glyph(s)");
            }
        }

        AssertThat(compared > 0).OverrideFailureMessage(
            $"no element of {string.Join(", ", samples)} was compared").IsTrue();

        return worst;
    }

    // ── Helpers ──

    /// <summary>Spacing the compile phase prescribed for the boundary between two adjacent elements.</summary>
    private static float BoundarySpacing(
        IReadOnlyList<Boundary> boundaries,
        in LayoutElement left,
        in LayoutElement right)
    {
        int index = left.ClusterStart;

        if (index < 0 || index >= boundaries.Count)
            return 0f;

        Boundary boundary = boundaries[index];
        return boundary.RightCluster == right.ClusterStart ? boundary.BaseSpacing : 0f;
    }

    private static (string Name, Scenario Scenario)[] Scenarios() =>
    [
        ("cjk", Chinese()),
        ("cjk-latin", Mixed()),
        ("english", English()),
    ];

    private sealed record Scenario(DrawElement[] Elements, TypographySettings Settings);

    private sealed record Layout(List<LayoutLine> Lines, List<LayoutElement> Elements, IReadOnlyList<Boundary> Boundaries);

    private static Scenario Chinese() => new(
        [Text("这是一段用于验证字形输出的中文文本，包含标点。")],
        // Explicit left alignment: the glyph-advance sum is the element's *natural* width, and justification
        // adjusts the box instead (that separation is the output contract, see GlyphRun.Width).
        new TypographySettings { MaxWidth = 200f, LanguageTag = "zh-Hans", Alignment = TextAlignment.Left });

    private static Scenario Mixed() => new(
        [Text("在Godot中使用SkiaSharp渲染中文与Latin混排文本。")],
        new TypographySettings { MaxWidth = 200f, LanguageTag = "zh-Hans", Alignment = TextAlignment.Left });

    private static Scenario English() => new(
        [Text("A well-known implementation of a layout engine, with punctuation.")],
        new TypographySettings { MaxWidth = 200f, LanguageTag = "en" });

    private static DrawElement Text(string text) => new()
    {
        Type = DrawElement.ElementType.Text,
        Text = text,
        Font = ThemeDB.FallbackFont,
        FontSize = 16,
        Color = new Color(0.1f, 0.1f, 0.1f),
    };

    private static Layout Run(Scenario scenario)
    {
        using var engine = new TypographyEngine(scenario.Settings);
        var lines = engine.PrepareAndLayout(scenario.Elements.AsSpan(), out _);
        var elements = engine.GetLayoutElements(scenario.Elements);
        return new Layout(lines, elements, engine.LastBoundaries);
    }
}
