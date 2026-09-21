namespace GodotNodeExtension.Tests.Typography;

using GdUnit4;
using GodotNodeExtension.Component.Typography.Core.Model;
using GodotNodeExtension.Component.Typography.Core.Unicode;
using static GdUnit4.Assertions;

/// <summary>
/// Specification for the bidi integration: the algorithm is delegated to the ported <c>unicode-bidi</c>
/// implementation, and what this component owns is the way it is asked and the way the answer is used.
/// <para>
/// The cases pin the three things the rest of the pipeline relies on: the cheap gate that keeps a purely
/// left-to-right document out of the bidi path, the levels per code unit (which the line assembly needs to know
/// which way a run is drawn), and the visual order of a mixed line — the rule that reverses the right-to-left run
/// and leaves the left-to-right part alone.
/// </para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class TypographyBidiResolverTest
{
    /// <summary>Latin then Hebrew: the mixed case the whole feature exists for.</summary>
    private const string Mixed = "abc \u05D0\u05D1\u05D2";

    /// <summary>A document with nothing right-to-left in it never enters the bidi path.</summary>
    [TestCase]
    public void TheGateOnlyOpensForRightToLeftContent()
    {
        AssertThat(BidiResolver.HasRightToLeft(Mixed)).IsTrue();
        AssertThat(BidiResolver.HasRightToLeft("abc def")).IsFalse();
        AssertThat(BidiResolver.HasRightToLeft(string.Empty)).IsFalse();
    }

    /// <summary>
    /// The library reports one level per code unit: the Latin letters keep the paragraph's even level, the Hebrew
    /// letters get an odd one.
    /// </summary>
    [TestCase]
    public void LevelsComeBackPerCodeUnit()
    {
        byte[] levels = BidiResolver.LevelsOf(Mixed, TextDirection.LeftToRight);

        AssertThat(levels.Length).IsEqual(Mixed.Length);

        for (int i = 0; i < 3; i++)
            AssertThat((levels[i] & 1) == 0).OverrideFailureMessage($"Latin at {i} must be even").IsTrue();

        for (int i = 4; i < Mixed.Length; i++)
            AssertThat((levels[i] & 1) == 1).OverrideFailureMessage($"Hebrew at {i} must be odd").IsTrue();

        // A paragraph declared right-to-left keeps the Hebrew on its base level and pushes the Latin run deeper
        // (level 2, still even): the parity is what says which way a run is drawn, the depth says how nested it is.
        byte[] rtlBase = BidiResolver.LevelsOf(Mixed, TextDirection.RightToLeft);
        AssertThat(rtlBase[4] & 1).OverrideFailureMessage("Hebrew sits on the right-to-left base level").IsEqual(1);
        AssertThat(rtlBase[0] & 1).IsEqual(0);
        AssertThat(rtlBase[0] > rtlBase[4]).OverrideFailureMessage(
            "the Latin run is nested inside the right-to-left paragraph").IsTrue();
    }

    /// <summary>
    /// Rule L2 over the reported levels: only the right-to-left run is reversed, and the left-to-right part plus
    /// the separator between them keep their order.
    /// </summary>
    [TestCase]
    public void TheVisualOrderReversesOnlyTheRightToLeftRun()
    {
        int[] order = BidiResolver.VisualOrderOfLine(Mixed, 0, Mixed.Length, TextDirection.LeftToRight);

        AssertThat(order.Length).IsEqual(Mixed.Length);
        AssertThat(string.Join(",", order)).OverrideFailureMessage(
            "the Latin part stays, the Hebrew part reverses").IsEqual("0,1,2,3,6,5,4");

        AssertThat(BidiResolver.VisualTextOfLine(Mixed, 0, Mixed.Length, TextDirection.LeftToRight))
            .IsEqual("abc \u05D2\u05D1\u05D0");
    }

    /// <summary>
    /// A line that is entirely one direction comes back as it was written, so nothing is reordered needlessly.
    /// </summary>
    [TestCase]
    public void SingleDirectionLinesKeepTheirOrder()
    {
        AssertThat(BidiResolver.VisualTextOfLine("abc", 0, 3, TextDirection.LeftToRight)).IsEqual("abc");

        int[] order = BidiResolver.VisualOrderOfLine("\u05D0\u05D1\u05D2", 0, 3, TextDirection.RightToLeft);
        AssertThat(string.Join(",", order)).IsEqual("2,1,0");
    }

    /// <summary>
    /// The direction runs of the levels: two runs for the mixed text (left to right, then right to left), which is
    /// what the element assembly groups by.
    /// </summary>
    [TestCase]
    public void DirectionRunsSplitTheTextWhereTheDirectionChanges()
    {
        byte[] levels = BidiResolver.LevelsOf(Mixed, TextDirection.LeftToRight);
        var runs = BidiResolver.DirectionRuns(levels);

        AssertThat(runs.Count).OverrideFailureMessage("expected a left-to-right half and a right-to-left half")
            .IsEqual(2);
        AssertThat(runs[0].RightToLeft).IsFalse();
        AssertThat(runs[0].Start).IsEqual(0);
        AssertThat(runs[1].RightToLeft).IsTrue();
        AssertThat(runs[1].Start).IsEqual(4);
    }

    /// <summary>
    /// A forced range is resolved with the direction it was given as its base, and the paragraph keeps its own
    /// resolution around it. What that changes is the *nesting* of the range's content: a Latin run forced into a
    /// right-to-left span is drawn as a nested (even, level 2) run, not as if it had become right to left — which is
    /// the same thing Godot's bidi override asks for when it resolves a range with its own base direction.
    /// </summary>
    [TestCase]
    public void AForcedRangeIsResolvedWithItsOwnBaseDirection()
    {
        byte[] plain = BidiResolver.LevelsOf(Mixed, 0);
        byte[] forced = BidiResolver.LevelsOf(Mixed, 0, [new BidiOverride(0, 3, TextDirection.RightToLeft)]);

        AssertThat(plain[0]).OverrideFailureMessage("the Latin run sits on the paragraph's level").IsEqual(0);
        AssertThat(forced[0]).OverrideFailureMessage(
            "a Latin run forced into a right-to-left span is nested at level 2").IsEqual(2);

        // Outside the forced range nothing moved, and the Hebrew run after it is unchanged.
        AssertThat(forced[3]).IsEqual(plain[3]);
        AssertThat(forced[4] & 1).IsEqual(plain[4] & 1);
        AssertThat(forced[4]).IsEqual(plain[4]);

        // A range past the end of the text is ignored rather than throwing.
        byte[] ignored = BidiResolver.LevelsOf(Mixed, 0, [new BidiOverride(99, 5, TextDirection.RightToLeft)]);
        AssertThat(ignored[0]).IsEqual(plain[0]);
    }

    /// <summary>
    /// Our own P2/P3 and the library agree on which way a paragraph runs: the paragraph level this component
    /// decides is the parity of the level the algorithm reports.
    /// </summary>
    [TestCase]
    public void OurParagraphLevelAgreesWithTheAlgorithmsAnswer()
    {
        int ours = BidiAlgorithm.ParagraphLevel([0x05D0, 0x05D1], BidiAlgorithm.Auto);
        byte[] levels = BidiResolver.LevelsOf("\u05D0\u05D1", TextDirection.LeftToRight);

        AssertThat(ours).IsEqual(1);
        AssertThat(levels[0] & 1).OverrideFailureMessage(
            "the paragraph level this component decides must match the level the algorithm gives its text")
            .IsEqual(ours);
    }
}
