namespace GodotNodeExtension.Tests.Typography;

using System.Collections.Generic;
using GdUnit4;
using Godot;
using GodotNodeExtension.Component.Typography.Core;
using GodotNodeExtension.Component.Typography.Core.Model;
using GodotNodeExtension.Component.Typography.Languages;
using static GdUnit4.Assertions;

/// <summary>
/// Specification for the languages that are written right to left: their direction is part of the language, not
/// something every request has to declare.
/// <para>
/// It is the same reasoning as the OpenType language tag: a paragraph that says it is Hebrew is telling the layout
/// which way its text runs, and a caller that has to repeat that in the request will eventually forget. The cases
/// below pin the profiles and the fact that the direction reaches the geometry through the language alone.
/// </para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class TypographyRtlLanguagesTest
{
    /// <summary>Two Hebrew words, so the line has more than one element to order.</summary>
    private const string HebrewWords = "\u05D0\u05D1 \u05D2\u05D3";

    /// <summary>Arabic and Hebrew resolve to profiles that carry their direction.</summary>
    [TestCase]
    public void RightToLeftLanguagesCarryTheirDirection()
    {
        var arabic = LanguageProfileRegistry.Shared.Resolve(new TypographySettings { LanguageTag = "ar" });
        AssertThat(arabic.ProfileId).IsEqual("ar");
        AssertThat(arabic.Direction).IsEqual(TextDirection.RightToLeft);
        AssertThat(arabic.OpenTypeLanguageTag).IsEqual("ARA");
        AssertThat(arabic.EnableLineProhibition).OverrideFailureMessage(
            "the CJK prohibition rules are not this language's business").IsFalse();

        var hebrew = LanguageProfileRegistry.Shared.Resolve(new TypographySettings { LanguageTag = "he" });
        AssertThat(hebrew.ProfileId).IsEqual("he");
        AssertThat(hebrew.Direction).IsEqual(TextDirection.RightToLeft);
        AssertThat(hebrew.OpenTypeLanguageTag).IsEqual("IWR");

        // A regional tag resolves to the language, the same way zh-Hant-HK does.
        AssertThat(LanguageProfileRegistry.Shared.Resolve("ar-EG").Id).IsEqual("ar");
    }

    /// <summary>
    /// The direction reaches the layout: declaring the language is enough for the line to fill from the right edge.
    /// </summary>
    [TestCase]
    public void TheLanguageAloneMakesTheLineFillFromTheRightEdge()
    {
        DrawElement[] elements =
        [
            new()
            {
                Type = DrawElement.ElementType.Text,
                Text = HebrewWords,
                Font = ThemeDB.FallbackFont,
                FontSize = 16,
                Color = new Color(0.1f, 0.1f, 0.1f),
            },
        ];

        // No Direction in the request: the language profile supplies it.
        using var engine = new TypographyEngine(new TypographySettings
        {
            MaxWidth = 200f,
            LanguageTag = "he",
            FirstLineIndent = 0,
            Alignment = TextAlignment.Left,
        });

        List<LayoutLine> lines = engine.PrepareAndLayout(elements, out _);
        AssertThat(lines[0].Elements.Count).IsGreater(0);

        LayoutElement rightmost = lines[0].Elements[^1];

        AssertThat(rightmost.Position.X + rightmost.Size.X).OverrideFailureMessage(
            "a Hebrew paragraph fills from the right edge without being asked to").IsEqual(200f);
    }
}
