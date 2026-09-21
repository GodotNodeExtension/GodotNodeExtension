namespace GodotNodeExtension.Tests.GodotSkia;

using GdUnit4;
using Godot;
using GodotNodeExtension.Component.GodotSkia;
using SkiaSharp;
using static GdUnit4.Assertions;

/// <summary>
/// Behaviour specification for the font handling of <see cref="SkiaGodotConverter"/>: Godot
/// expresses a synthetic bold or italic face as a <see cref="FontVariation"/>, which a Skia
/// typeface cannot carry, so the style has to be re-applied to the <c>SKFont</c> — in one shared
/// place, so that measurement and drawing see the same width.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class SkiaFontStyleTest
{
    private static FontVariation EmboldenedVariation()
        => new() { BaseFont = ThemeDB.FallbackFont, VariationEmbolden = 0.5f };

    private static FontVariation SlantedVariation()
        => new()
        {
            BaseFont = ThemeDB.FallbackFont,
            // y axis carries an x component → the same skew Godot uses for fake italics
            VariationTransform = new Transform2D(new Vector2(1f, 0f), new Vector2(0.3f, 1f), Vector2.Zero),
        };

    [TestCase]
    public void PlainFontRequestsNoSyntheticStyle()
    {
        var style = SkiaGodotConverter.SyntheticStyleOf(ThemeDB.FallbackFont);

        AssertThat(style.Bold).IsFalse();
        AssertThat(style.Italic).IsFalse();
    }

    [TestCase]
    public void EmboldenVariationRequestsSyntheticBold()
    {
        var style = SkiaGodotConverter.SyntheticStyleOf(EmboldenedVariation());

        AssertThat(style.Bold).IsTrue();
        AssertThat(style.Italic).IsFalse();
    }

    [TestCase]
    public void SkewVariationRequestsSyntheticItalic()
    {
        var style = SkiaGodotConverter.SyntheticStyleOf(SlantedVariation());

        AssertThat(style.Italic).IsTrue();
        AssertThat(style.Bold).IsFalse();
    }

    [TestCase]
    public void ConfigureFontAppliesSyntheticStyleWhenTheTypefaceLacksIt()
    {
        using var plain = SkiaGodotConverter.ConfigureFont(new SKFont(SKTypeface.Default, 24f));
        using var bold = SkiaGodotConverter.ConfigureFont(
            new SKFont(SKTypeface.Default, 24f),
            new SkiaGodotConverter.SkiaFontStyle(Bold: true, Italic: false));
        using var italic = SkiaGodotConverter.ConfigureFont(
            new SKFont(SKTypeface.Default, 24f),
            new SkiaGodotConverter.SkiaFontStyle(Bold: false, Italic: true));

        AssertThat(plain.Embolden).IsFalse();
        AssertThat(plain.SkewX).IsEqual(0f);

        AssertThat(bold.Embolden).IsTrue();
        AssertThat(bold.SkewX).IsEqual(0f);

        AssertThat(italic.SkewX < 0f).IsTrue();
        AssertThat(italic.Embolden).IsFalse();
    }

    [TestCase]
    public void ConfigureFontDoesNotDoubleApplyARealBoldFace()
    {
        var boldFace = SkiaGodotConverter.GetOrCreateFamilyTypeface(
            null, SKFontStyleWeight.Bold);

        using var font = SkiaGodotConverter.ConfigureFont(
            new SKFont(boldFace, 24f),
            new SkiaGodotConverter.SkiaFontStyle(Bold: true, Italic: false));

        // The typeface already provides the weight: no synthetic embolden on top of it.
        AssertThat(font.Embolden).IsFalse();
    }

    /// <summary>Whether the font carries the requested bold weight, synthetically or in its face.</summary>
    private static bool IsBoldTypeface(SKFont font)
        => (font.Typeface?.FontStyle.Weight ?? 0) >= (int)SKFontStyleWeight.SemiBold;

    /// <summary>Whether the font renders bold at all (face weight or synthetic embolden).</summary>
    private static bool IsBold(SKFont font) => font.Embolden || IsBoldTypeface(font);

    /// <summary>
    /// The weight the font renders at: the synthetic embolden is stronger than any face below the
    /// semi-bold threshold, which is what makes "strictly heavier" a statement about the rendered font.
    /// </summary>
    private static int EffectiveWeight(SKFont font)
        => font.Embolden ? (int)SKFontStyleWeight.Bold : font.Typeface?.FontStyle.Weight ?? 0;

    [TestCase]
    public void ToSkFontMakesTheVariationBold()
    {
        using var plain = ThemeDB.FallbackFont.ToSkFont(20f);
        using var bold = EmboldenedVariation().ToSkFont(20f);

        // The bold request has to land exactly once: on a face that already carries the weight, or as a
        // synthetic embolden on the SKFont - never as both (a doubled face) and never as neither (a
        // silently dropped variation). "Strictly heavier than the plain font" is the observable contract.
        AssertThat(IsBold(plain)).IsFalse();
        AssertThat(IsBold(bold)).IsTrue();
        AssertThat(bold.Embolden && IsBoldTypeface(bold)).IsFalse();
        AssertThat(EffectiveWeight(bold) > EffectiveWeight(plain)).IsTrue();
        GD.Print($"{nameof(ToSkFontMakesTheVariationBold)}: plain {EffectiveWeight(plain)}, " +
                 $"bold {EffectiveWeight(bold)} (embolden {bold.Embolden})");
    }
}
