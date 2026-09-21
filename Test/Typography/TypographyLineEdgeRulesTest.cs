namespace GodotNodeExtension.Tests.Typography;

using System.Collections.Generic;
using GdUnit4;
using Godot;
using GodotNodeExtension.Component.Typography.Core;
using GodotNodeExtension.Component.Typography.Core.Model;
using static GdUnit4.Assertions;

/// <summary>
/// Specification for the two rules about a line's edges: hanging a mark past the end edge, and trimming the empty
/// leading half of an opening bracket that starts a line.
/// <para>
/// Both are conventions and the conventions disagree — simplified Chinese hangs punctuation and traditional
/// Chinese does not in horizontal writing (clreq §6.1.3), Chinese and Japanese trim a line-head bracket while
/// Korean states no such adjustment (jlreq §3.1.5, clreq §6.3.2.3) — so the cases below pin the language where
/// the rule differs, and that neither rule applies where the line does not really reach the content edge.
/// </para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class TypographyLineEdgeRulesTest
{
    /// <summary>
    /// A mark that would start the next line stays on this one instead, sticking out past the line's end edge
    /// (clreq §6.1.3). The line count is the evidence: without hanging the mark needs a line of its own.
    /// </summary>
    [TestCase]
    public void SimplifiedChineseHangsAMarkPastTheLineEdge()
    {
        List<LayoutLine> lines = LayOut("一二三四五六七八。", "zh-Hans", 128f);

        AssertThat(lines.Count).OverrideFailureMessage("the hanging mark must not need a line of its own").IsEqual(1);

        LayoutElement mark = LastElementOf(lines[0]);
        AssertThat(mark.Text).IsEqual("。");
        AssertThat(mark.Position.X + mark.Size.X > lines[0].LineRight).OverrideFailureMessage(
            $"the mark ends at {mark.Position.X + mark.Size.X:F3} and the line ends at {lines[0].LineRight:F3}")
            .IsTrue();

        // Both halves of the report: the flag a renderer acts on, and the reason a dump shows. The reason is
        // what Doc/Typography/limitations.md tells a reader to look for, so it is asserted rather than assumed.
        AssertThat(mark.Hanging).OverrideFailureMessage("a mark past the edge hangs").IsTrue();
        AssertThat(mark.Reason).IsEqual("HangingPunctuation");
    }

    /// <summary>
    /// Traditional Chinese does not hang punctuation in horizontal writing (clreq §6.1.3 allows it in vertical
    /// writing), so the mark moves to the next line there.
    /// </summary>
    [TestCase]
    public void TraditionalChineseMovesTheMarkToTheNextLine()
    {
        List<LayoutLine> lines = LayOut("一二三四五六七八。", "zh-Hant", 128f);

        AssertThat(lines.Count).OverrideFailureMessage("the mark was hung instead of moving down").IsEqual(2);

        foreach (LayoutElement element in lines[0].Elements)
            AssertThat(element.Text).IsNotEqual("。");

        AssertThat(LastElementOf(lines[1]).Text).IsEqual("。");
    }

    /// <summary>
    /// An opening bracket at the head of a line gives up its empty leading half: the line gains half an em, the
    /// glyph is drawn half an em further back, and the element says why.
    /// </summary>
    [TestCase]
    public void ChineseTrimsTheEmptyHalfOfALineHeadBracket()
    {
        List<LayoutLine> lines = LayOut("「引号」后面的文字。", "zh-Hans", 200f);
        LayoutElement bracket = lines[0].Elements[0];

        AssertThat(bracket.CharClass).IsEqual(CharacterClass.PunctuationOpen);
        AssertThat(bracket.Size.X).OverrideFailureMessage(
            $"the bracket kept its full width ({bracket.Size.X:F3}px)").IsEqual(8f);
        AssertThat(bracket.Reason).IsEqual("OpeningBracketHalfWidth");
    }

    /// <summary>
    /// The same trim down a column. The rule used to be written in X and Y outright, which is right in horizontal
    /// writing and meaningless in a column: the shift that moves the rest of the line back was applied along the
    /// *block* axis, so everything after the bracket landed a page away and a column came out 2305px long inside a
    /// 380px column height.
    /// </summary>
    [TestCase]
    public void ChineseTrimsTheHeadBracketInVerticalWritingToo()
    {
        const float columnHeight = 200f;
        List<LayoutLine> lines = LayOut("「引号」后面的文字。", "zh-Hans", 380f, WritingMode.VerticalRl, columnHeight);
        LayoutElement bracket = lines[0].Elements[0];

        AssertThat(bracket.CharClass).IsEqual(CharacterClass.PunctuationOpen);
        AssertThat(bracket.Reason).IsEqual("OpeningBracketHalfWidth");
        AssertThat(bracket.Size.Y).OverrideFailureMessage(
            $"the bracket kept its full inline extent ({bracket.Size.Y:F3}px)").IsEqual(8f);

        foreach (LayoutLine line in lines)
        {
            foreach (LayoutElement element in line.Elements)
            {
                AssertThat(element.Position.Y).OverrideFailureMessage(
                    $"an element of the column starts at {element.Position.Y:F1} along the inline axis, past the "
                    + $"column height of {columnHeight}px").IsLess(columnHeight + 32f);
            }
        }
    }

    /// <summary>Korean has no such convention (klreq states nothing to trim), so the bracket keeps its width.</summary>
    [TestCase]
    public void KoreanKeepsTheBracketAtFullWidth()
    {
        List<LayoutLine> lines = LayOut("「인용」 뒤의 문장。", "ko", 200f);
        LayoutElement bracket = lines[0].Elements[0];

        AssertThat(bracket.Size.X).IsEqual(16f);
        AssertThat(bracket.Reason).IsNotEqual("OpeningBracketHalfWidth");
    }

    /// <summary>
    /// A mark that exactly fills the line does not hang: nothing sticks out, so the element must not claim it does.
    /// This is the case that made the earlier attempt at the hanging marker look like a missing one - the marker has
    /// to say "this sticks out", not "this is the last character".
    /// </summary>
    [TestCase]
    public void AMarkThatExactlyFillsTheLineDoesNotHang()
    {
        // Seven characters plus the mark at 16px fill 128px exactly: the mark is the last character and still fits.
        List<LayoutLine> lines = LayOut("一二三四五六七。", "zh-Hans", 128f);

        AssertThat(lines.Count).OverrideFailureMessage("the mark fits, so it needs no line of its own").IsEqual(1);

        LayoutElement mark = lines[0].Elements[^1];
        AssertThat(mark.Text).IsEqual("。");
        AssertThat(mark.Hanging).OverrideFailureMessage(
            "the mark ends exactly at the line's edge, so nothing hangs").IsFalse();
        AssertThat(mark.Reason).IsNotEqual("HangingPunctuation");
    }

    // ── Helpers ──

    private static List<LayoutLine> LayOut(string text, string languageTag, float maxWidth,
        WritingMode mode = WritingMode.HorizontalTb, float maxHeight = 0f)
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

        using var engine = new TypographyEngine(new TypographySettings
        {
            MaxWidth = maxWidth,
            MaxHeight = maxHeight,
            WritingMode = mode,
            LanguageTag = languageTag,
            FirstLineIndent = 0,
            Alignment = TextAlignment.Left,
        });

        return engine.PrepareAndLayout(elements, out _);
    }

    private static LayoutElement LastElementOf(LayoutLine line) => line.Elements[^1];
}
