namespace GodotNodeExtension.Tests.Typography;

using System;
using System.Collections.Generic;
using GdUnit4;
using Godot;
using GodotNodeExtension.Component.Typography.Core;
using GodotNodeExtension.Component.Typography.Core.Model;
using GodotNodeExtension.Component.Typography.Languages;
using SkiaSharp;
using static GdUnit4.Assertions;

/// <summary>
/// Specification for ruby as a laid-out annotation rather than a decorated string.
/// <para>
/// The properties that make it part of layout are the ones asserted here: an annotation reserves room without
/// changing the base text's own width or source range, it is kept with its base when a line breaks, and a
/// convention that does not reserve room (Japanese puts ruby between the lines) changes nothing about the line
/// height. The distribution the engine cannot do yet fails loudly instead of quietly producing something else.
/// </para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class TypographyRubyTest
{
    /// <summary>A Chinese annotation reserves room above the text box and moves the baselines down with it.</summary>
    [TestCase]
    public void AnAnnotationReservesRoomAndMovesTheWholeLineDown()
    {
        LayoutResult withRuby = LayOut(Ruby("振り仮名", RubyDistribution.Mono), "zh-Hans");
        LayoutResult without = LayOut(null, "zh-Hans");

        AssertThat(withRuby.LineHeight > without.LineHeight).OverrideFailureMessage(
            $"the annotated line is {withRuby.LineHeight:F3}px and the plain one {without.LineHeight:F3}px").IsTrue();

        // Every element on the line shares the one baseline, so all of them moved down by the reserve.
        float shift = withRuby.FirstTextBaseline - without.FirstTextBaseline;
        AssertThat(Math.Abs(shift - (withRuby.LineHeight - without.LineHeight)) < 0.01f).OverrideFailureMessage(
            $"baseline moved {shift:F3}px but the line grew {withRuby.LineHeight - without.LineHeight:F3}px").IsTrue();
    }

    /// <summary>
    /// A long annotation at the line's start or end is not drawn outside the area: the line gives way for the
    /// overhang (jlreq §3.3.9's first method - the annotation's start lines up with the line head, and the line ends
    /// early enough to leave room at its end). The annotation is otherwise centred on the run it annotates.
    /// </summary>
    [TestCase]
    public void AGroupAnnotationWiderThanItsRunStaysInsideTheLine()
    {
        LayoutResult layout = LayOut(Ruby("東京", RubyDistribution.Group), "ja");

        LayoutElement first = layout.Elements[0];
        RubyAnnotation ruby = first.Ruby!.Value;

        AssertThat(ruby.Width > first.Size.X).OverrideFailureMessage(
            $"the annotation is {ruby.Width:F3}px and the character {first.Size.X:F3}px, so this case does not "
            + "exercise an overhanging annotation").IsTrue();

        // The annotation's own start is what lines up with the line head, and the run it annotates is pushed in by
        // the overhang, so nothing is drawn outside the line (jlreq §3.3.9's first method, Figure 141).
        AssertThat(ruby.X >= -0.01f).OverrideFailureMessage(
            $"the annotation starts at {ruby.X:F3}, before the line's own start, so it is drawn outside the line")
            .IsTrue();

        AssertThat(ruby.X < first.Position.X - 0.01f).OverrideFailureMessage(
            $"the annotation starts at {ruby.X:F3} and the run it annotates at {first.Position.X:F3}: the line did "
            + "not give way for the annotation's overhang").IsTrue();

        AssertThat(Math.Abs(first.Position.X - ((ruby.Width - 32f) * 0.5f)) < 0.01f).OverrideFailureMessage(
            $"the run starts at {first.Position.X:F3}: the line's content should begin one overhang "
            + $"({(ruby.Width - 32f) * 0.5f:F3}px) past its edge").IsTrue();
    }

    /// <summary>
    /// An annotation too wide for the line never leaves the area: the line gives way at its head, so the
    /// annotation's own start lines up with the line head and the run it annotates is pushed in by the overhang
    /// (jlreq §3.3.9's first method, Figure 141). This covers the case the line cannot reserve room for - an
    /// annotation wider than the line itself - which a clamp inside the annotation builder would not have caught,
    /// because centring is not what places it there in the first place.
    /// </summary>
    [TestCase]
    public void AnAnnotationTooWideForTheLineStartsAtTheHead()
    {
        var elements = new List<DrawElement> { Ruby("東", RubyDistribution.Group) };

        DrawElement[] source = [.. elements];

        // A 32px measure: the annotated character (16px) fits, the annotation (four times that) cannot.
        using var engine = new TypographyEngine(new TypographySettings
        {
            MaxWidth = 32f,
            LanguageTag = "ja",
            FirstLineIndent = 0,
            Alignment = TextAlignment.Left,
        });

        List<LayoutLine> lines = engine.PrepareAndLayout(source, out _);
        RubyAnnotation ruby = default;
        float characterStart = 0f;

        foreach (LayoutElement element in engine.GetLayoutElements(source))
        {
            if (element.Ruby is { } found)
            {
                ruby = found;
                characterStart = element.Position.X;
            }
        }

        AssertThat(lines.Count).IsEqual(1);
        AssertThat(ruby.Width > lines[0].LineRight - lines[0].LineLeft).OverrideFailureMessage(
            $"the annotation is {ruby.Width:F3}px and the line {lines[0].LineRight - lines[0].LineLeft:F3}px, so this "
            + "case does not exercise an annotation the line cannot hold").IsTrue();
        AssertThat(Math.Abs(ruby.X - lines[0].LineLeft) < 0.01f).OverrideFailureMessage(
            $"the annotation starts at {ruby.X:F3}, before the line head at {lines[0].LineLeft:F3}")
            .IsTrue();
        AssertThat(characterStart > lines[0].LineLeft + 0.01f).OverrideFailureMessage(
            $"the annotated character starts at {characterStart:F3}: the line did not give way for the annotation")
            .IsTrue();
    }

    /// <summary>
    /// The content box of a column is measured along the column. An annotation's numbers name the pen and the run
    /// down the column, so reading its X and its width as a horizontal extent gives the block coordinate - which
    /// came out as a content box a whole page long for a column a fifth of it high.
    /// </summary>
    [TestCase]
    public void TheContentBoxOfAColumnIsMeasuredAlongTheColumn()
    {
        var elements = new List<DrawElement>
        {
            Ruby("振り仮名", RubyDistribution.Mono),
            Text("の中文"),
        };

        DrawElement[] source = [.. elements];

        using var engine = new TypographyEngine(new TypographySettings
        {
            MaxWidth = 200f,
            MaxHeight = 300f,
            WritingMode = WritingMode.VerticalRl,
            LanguageTag = "ja",
            FirstLineIndent = 0,
            Alignment = TextAlignment.Left,
        });

        engine.PrepareAndLayout(source, out Vector2 contentSize);
        RubyAnnotation ruby = default;
        float lastElementEnd = 0f;

        foreach (LayoutElement element in engine.GetLayoutElements(source))
        {
            if (element.Ruby is { } found)
                ruby = found;

            lastElementEnd = Math.Max(lastElementEnd, element.Position.Y + element.Size.Y);
        }

        AssertThat(ruby.Width > 0f).OverrideFailureMessage("the fixture produced no annotation").IsTrue();

        // Down a column the annotation's baseline is its pen and its width is the run below it.
        AssertThat(ruby.BaselineY + ruby.Width <= contentSize.Y + 0.01f).OverrideFailureMessage(
            $"the annotation ends at {ruby.BaselineY + ruby.Width:F3} on the inline axis and the content box is "
            + $"{contentSize.Y:F3} deep").IsTrue();

        // A column may overhang its boxes by about a glyph - a mark or an annotation - and not by the block
        // coordinate, which is what a page-sized content box meant.
        AssertThat(contentSize.Y <= lastElementEnd + ruby.FontSize + 0.01f).OverrideFailureMessage(
            $"the content box is {contentSize.Y:F3} deep while the elements reach {lastElementEnd:F3}, with an "
            + $"annotation font of {ruby.FontSize:F3}").IsTrue();
    }

    /// <summary>
    /// The content box covers what is drawn rather than only the elements' own boxes: an annotation can end past
    /// the character it annotates, and a scroll region sized by the elements alone would clip it. This fixture makes
    /// the annotation the widest thing on the page, which is the case the element geometry cannot describe.
    /// </summary>
    [TestCase]
    public void TheContentBoxCoversAnOverhangingAnnotation()
    {
        var elements = new List<DrawElement> { Ruby("東", RubyDistribution.Group) };

        DrawElement[] source = [.. elements];

        using var engine = new TypographyEngine(new TypographySettings
        {
            MaxWidth = 200f,
            LanguageTag = "ja",
            FirstLineIndent = 0,
            Alignment = TextAlignment.Left,
        });

        List<LayoutLine> lines = engine.PrepareAndLayout(source, out Vector2 contentSize);
        RubyAnnotation ruby = default;
        float characterEnd = 0f;

        foreach (LayoutElement element in engine.GetLayoutElements(source))
        {
            if (element.Ruby is { } found)
                ruby = found;

            characterEnd = Math.Max(characterEnd, element.Position.X + element.Size.X);
        }

        AssertThat(lines.Count).IsEqual(1);
        AssertThat(ruby.Width > 0f).OverrideFailureMessage("the fixture produced no annotation").IsTrue();
        AssertThat(characterEnd > 0f).OverrideFailureMessage("the fixture laid nothing out").IsTrue();
        AssertThat(ruby.X + ruby.Width > characterEnd).OverrideFailureMessage(
            $"the annotation ends at {ruby.X + ruby.Width:F3} and the characters at {characterEnd:F3}, so this case "
            + "does not exercise an annotation that reaches past them").IsTrue();
        AssertThat(contentSize.X >= ruby.X + ruby.Width - 0.01f).OverrideFailureMessage(
            $"the content box is {contentSize.X:F3}px wide and the annotation ends at {ruby.X + ruby.Width:F3}: "
            + "a scroll region sized by it would clip the annotation").IsTrue();
    }

    /// <summary>
    /// The same rule at the other end: a character whose annotation would reach past the line's end is moved to
    /// the next line instead, because the line reserves the overhang of the run that ends it. Without that reserve
    /// the annotation is drawn outside the line and the content box reports a size the reader cannot see.
    /// </summary>
    [TestCase]
    public void ACharacterWhoseAnnotationWouldOverhangTheLineEndMovesDown()
    {
        // Eleven plain characters (176px) plus the annotated one (16px) fill a 192px measure exactly; the
        // annotation is wider than its character, so the line's end has to give way - or keep the character out.
        var elements = new List<DrawElement>
        {
            Text("一二三四五六七八九十一二"),
            Ruby("東", RubyDistribution.Group),
        };

        DrawElement[] source = [.. elements];

        using var engine = new TypographyEngine(new TypographySettings
        {
            MaxWidth = 192f,
            LanguageTag = "ja",
            FirstLineIndent = 0,
            Alignment = TextAlignment.Left,
        });

        List<LayoutLine> lines = engine.PrepareAndLayout(source, out _);
        int annotatedLine = -1;
        float annotationEnd = 0f;

        foreach (LayoutElement element in engine.GetLayoutElements(source))
        {
            if (element.Ruby is { } ruby)
            {
                annotatedLine = element.LineIndex;
                annotationEnd = ruby.X + ruby.Width;
            }
        }

        AssertThat(annotatedLine).OverrideFailureMessage("the fixture produced no annotation").IsGreater(0);
        AssertThat(annotationEnd <= lines[annotatedLine].LineRight + 0.01f).OverrideFailureMessage(
            $"the annotation ends at {annotationEnd:F3} and line {annotatedLine} ends at "
            + $"{lines[annotatedLine].LineRight:F3}")
            .IsTrue();
    }

    /// <summary>
    /// Japanese puts annotations between the lines (jlreq's 行間処理): the line does not grow, so the author's
    /// line spacing is what has to make room.
    /// </summary>
    [TestCase]
    public void AnAnnotatedLineGrowsOnBothSidesOfTheLineGap()
    {
        // jlreq lets ruby fall into the line gap the author's spacing provides, and clreq expects a line gap wide
        // enough to hold it; neither makes the layout work out when the author gave no spacing at all, which is the
        // state this engine ships its own examples in. The engine therefore reserves the band itself, for every
        // placement: an annotated line is taller than the same line without an annotation, in Japanese as much as in
        // Chinese, and two annotated lines cannot overlap.
        LayoutResult japanese = LayOut(Ruby("東京", RubyDistribution.Group), "ja");
        LayoutResult chinese = LayOut(Ruby("東京", RubyDistribution.Group), "zh-Hans");
        LayoutResult plain = LayOut(null, "ja");

        AssertThat(japanese.LineHeight > plain.LineHeight + 1f).OverrideFailureMessage(
            $"the Japanese line is {japanese.LineHeight:F3}px tall with an annotation and {plain.LineHeight:F3}px "
            + "without one, so nothing was reserved for the band").IsTrue();

        AssertThat(Math.Abs(japanese.LineHeight - chinese.LineHeight) < 1f).OverrideFailureMessage(
            $"the two conventions grew the line differently: {japanese.LineHeight:F3} / {chinese.LineHeight:F3}")
            .IsTrue();
    }

    /// <summary>
    /// The annotation does not change the base text: its width, its source range and the characters it reports
    /// stay the ones the caller wrote.
    /// </summary>
    [TestCase]
    public void TheBaseTextIsUnchangedByItsAnnotation()
    {
        // Both layouts have the same shape: the annotated run replaces the plain one, so an element-by-element
        // comparison is meaningful.
        LayoutResult plain = LayOutPlainBase();
        LayoutResult annotated = LayOut(Ruby("東京", RubyDistribution.Group), "zh-Hans");

        AssertThat(annotated.Elements.Count).IsEqual(plain.Elements.Count);

        for (int i = 0; i < plain.Elements.Count; i++)
        {
            AssertThat(annotated.Elements[i].Size.X).IsEqual(plain.Elements[i].Size.X);
            AssertThat(annotated.Elements[i].Text ?? string.Empty)
                .IsEqual(plain.Elements[i].Text ?? string.Empty);
            AssertThat(annotated.Elements[i].SourceRange.ToString())
                .IsEqual(plain.Elements[i].SourceRange.ToString());
        }
    }

    /// <summary>
    /// An annotated run is one unit: no break is allowed between the clusters that share an annotation, so the
    /// annotation can never end up on a different line than its base text.
    /// </summary>
    [TestCase]
    public void AnAnnotatedRunIsKeptTogether()
    {
        DrawElement[] elements =
        [
            Text("前面这些字把行占满，"),
            Ruby("東京", RubyDistribution.Group),
            Text("后面继续。"),
        ];

        using var engine = new TypographyEngine(new TypographySettings
        {
            MaxWidth = 120f,
            LanguageTag = "zh-Hans",
            FirstLineIndent = 0,
            Alignment = TextAlignment.Left,
        });

        engine.PrepareAndLayout(elements, out _);
        IReadOnlyList<Boundary> boundaries = engine.LastBoundaries;
        int rubyStart = FindFirstClusterOf(engine, elements, 1);

        AssertThat(rubyStart).OverrideFailureMessage("the annotated element produced no cluster").IsGreater(0);

        // The position inside the run is the one that must be forbidden, and the dump has to say why.
        AssertThat(boundaries[rubyStart].ForbiddenToBreak).OverrideFailureMessage(
            $"a break was allowed inside an annotated run: {boundaries[rubyStart].Reason}").IsTrue();

        // And no line may split the run: both of its clusters report the same line.
        int lineOfFirst = -1;

        foreach (LayoutElement element in engine.GetLayoutElements(elements))
        {
            if (element.SourceIndex != 1 || element.GlyphRun is null)
                continue;

            if (lineOfFirst < 0)
                lineOfFirst = element.LineIndex;
            else
                AssertThat(element.LineIndex).OverrideFailureMessage(
                    "the annotated run was split across two lines").IsEqual(lineOfFirst);
        }
    }

    /// <summary>
    /// Jukugo ruby is a per-character run whose characters are one unit (jlreq §3.3.7): every character carries
    /// its own annotation — the dump shows them — and no break is allowed anywhere inside the run, which is what
    /// separates it from mono.
    /// </summary>
    [TestCase]
    public void JukugoRubyAnnotatesEveryCharacterAndKeepsTheRunTogether()
    {
        DrawElement[] elements =
        [
            Text("前面这些字把行占满，"),
            Ruby("東京", RubyDistribution.Jukugo),
            Text("后面继续。"),
        ];

        var settings = new TypographySettings
        {
            MaxWidth = 120f,
            LanguageTag = "ja",
            FirstLineIndent = 0,
            Alignment = TextAlignment.Left,
        };

        using var engine = new TypographyEngine(settings);
        engine.PrepareAndLayout(elements, out _);

        int annotated = 0;
        int lineOfFirst = -1;

        foreach (LayoutElement element in engine.GetLayoutElements(elements))
        {
            if (element.Ruby is not { } ruby)
                continue;

            annotated++;
            AssertThat(ruby.Reason).IsEqual("Ruby:Jukugo");
            AssertThat(ruby.Text.Length > 0).OverrideFailureMessage("a piece has no annotation text").IsTrue();

            if (lineOfFirst < 0)
                lineOfFirst = element.LineIndex;
            else
                AssertThat(element.LineIndex).IsEqual(lineOfFirst);
        }

        AssertThat(annotated).OverrideFailureMessage("every base character of the run carries an annotation")
            .IsEqual(2);

        // The room the annotations needed shows up in the base run's own width: whichever character could not hold
        // its annotation borrowed from its neighbour (jlreq §3.3.7's 熟語ルビ).
        float runWidth = 0f;

        foreach (LayoutElement element in engine.GetLayoutElements(elements))
        {
            if (element.SourceIndex == 1 && element.GlyphRun is not null)
                runWidth += element.Size.X;
        }

        AssertThat(runWidth > 32f).OverrideFailureMessage(
            $"the annotated run stayed {runWidth:F3}px wide, so no base space was added").IsTrue();
    }

    /// <summary>
    /// An emphasis mark is centred on its character and sits on the side its language puts it on: below for
    /// Chinese, above for Japanese.
    /// </summary>
    [TestCase]
    public void EmphasisMarksSitWhereTheirLanguagePutsThem()
    {
        LayoutResult chinese = LayOutEmphasis("这段文字", "zh-Hans");
        LayoutResult japanese = LayOutEmphasis("この文", "ja");

        AssertThat(chinese.Marquee!.Value.Side).IsEqual(EmphasisSide.Below);
        AssertThat(japanese.Marquee!.Value.Side).IsEqual(EmphasisSide.Above);
        AssertThat(chinese.Marquee!.Value.Mark).IsEqual("\u25CF");
        AssertThat(japanese.Marquee!.Value.Mark).IsEqual("\u2022");

        LayoutElement element = chinese.Elements[0];
        EmphasisMarkGeometry mark = element.Emphasis!.Value;

        AssertThat(Math.Abs(mark.X - (element.Position.X + (element.Size.X * 0.5f))) < 0.01f).OverrideFailureMessage(
            $"the mark is at {mark.X:F3} but the character centre is {element.Position.X + (element.Size.X * 0.5f):F3}")
            .IsTrue();
    }

    /// <summary>
    /// Traditional Chinese asks for the Bopomofo position: the annotation band beside the base character and the
    /// annotation itself set down a column of its own (clreq §5.5.3.1 recommends the right-hand position for
    /// Traditional Chinese in either writing mode, and §5.5.3.2 describes the column it sets there).
    /// <para>
    /// The same case pins what the other languages keep. A default that moved for them would be a silent change of
    /// everything they already set, which is why this is a language's own parameter rather than a new engine
    /// default.
    /// </para>
    /// </summary>
    [TestCase]
    public void TraditionalChineseAsksForBopomofoAndNoOtherLanguageDoes()
    {
        AssertThat(PlacementOf("zh-Hant")).IsEqual(RubyPlacement.ReserveBeside);
        AssertThat(OrientationOf("zh-Hant")).IsEqual(RubyOrientation.Vertical);

        // Japanese is checked on its own: jlreq lets an annotation overflow into the line gap instead of reserving a
        // band above the line, which is neither of the two placements the loop below asserts.
        AssertThat(PlacementOf("ja")).OverrideFailureMessage("ja no longer overflows into the line gap")
            .IsEqual(RubyPlacement.OverflowBetweenLines);
        AssertThat(OrientationOf("ja")).OverrideFailureMessage("ja no longer sets its annotations along the line")
            .IsEqual(RubyOrientation.Horizontal);

        foreach (string? tag in new[] { "zh-Hans", "zh", "ko", "en", null })
        {
            string name = tag ?? "no language at all";

            AssertThat(PlacementOf(tag)).OverrideFailureMessage($"{name} no longer reserves above the line")
                .IsEqual(RubyPlacement.ReserveAbove);
            AssertThat(OrientationOf(tag)).OverrideFailureMessage($"{name} no longer sets its annotations along the line")
                .IsEqual(RubyOrientation.Horizontal);
        }
    }

    /// <summary>
    /// The room a Bopomofo annotation needs comes out of the base character's advance and not out of the line:
    /// half an em of base size is added to the annotated character (clreq §5.5.3.2), the annotation starts at the
    /// edge that character ended at before the room was added, and the line is exactly as tall as the same text
    /// without any annotation. The last of the three is the point of the placement: clreq expects the line gap to
    /// be the same whether or not annotations are present.
    /// </summary>
    [TestCase]
    public void BopomofoRoomComesFromTheBaseAdvanceAndNotFromTheLine()
    {
        (List<LayoutElement> annotated, float annotatedHeight) = LayOutOne("zh-Hant", "今", "ㄐㄧㄣ");
        (List<LayoutElement> plain, float plainHeight) = LayOutOne("zh-Hant", "今", null);

        AssertThat(annotatedHeight).OverrideFailureMessage(
            $"the annotated line is {annotatedHeight:F3}px tall and the same line without an annotation "
            + $"{plainHeight:F3}px").IsEqual(plainHeight);

        LayoutElement baseCharacter = annotated[0];
        LayoutElement withoutAnnotation = plain[0];
        float grown = baseCharacter.Size.X - withoutAnnotation.Size.X;

        AssertThat(grown).OverrideFailureMessage(
            $"the annotated character grew by {grown:F3}px instead of half an em").IsEqual(BaseSize * 0.5f);

        RubyAnnotation ruby = baseCharacter.Ruby!.Value;

        AssertThat(ruby.Orientation).OverrideFailureMessage(
            "the annotation of a Traditional Chinese paragraph is no longer set as a column")
            .IsEqual(RubyOrientation.Vertical);

        // The annotation is placed inside the room the character gained, centred on it: the advance the character
        // had before the room was added ends where the room begins, so the column's centre line is half the room
        // past that edge (CSS Ruby's initial `ruby-align: space-around`). Anchoring the column at the edge instead
        // put its ink over the base character and left the rest of the room empty - which is what a reader saw as
        // "the Bopomofo is too small and hugs the left".
        float edge = withoutAnnotation.Position.X + withoutAnnotation.Size.X;
        float centre = edge + (ruby.BandWidth * 0.5f);

        AssertThat(Math.Abs(ruby.BandWidth - (BaseSize * 0.5f)) < 0.01f).OverrideFailureMessage(
            $"the room the annotation was given is {ruby.BandWidth:F3}px instead of half an em").IsTrue();

        AssertThat(Math.Abs(ruby.X - centre) < 0.01f).OverrideFailureMessage(
            $"the annotation is centred on {ruby.X:F3} while the room it was given spans "
            + $"[{edge:F3},{edge + ruby.BandWidth:F3}]").IsTrue();

        // Its width is the extent along its own reading direction, which for a column is its height: the sum of
        // the annotation glyphs' advances, read downwards.
        float advances = 0f;

        foreach (Glyph glyph in ruby.Glyphs!)
            advances += glyph.Advance;

        AssertThat(Math.Abs(advances - ruby.Width) < 0.01f).OverrideFailureMessage(
            $"the annotation glyphs advance {advances:F3}px while its width is {ruby.Width:F3}px").IsTrue();

        AssertThat(ruby.Width > 0f).IsTrue();
    }

    /// <summary>
    /// A Bopomofo run keeps its own size ratio: clreq §5.5.3.2 states 3:10, and the caller who writes Bopomofo is
    /// who asks for it, because the engine's default is the half size jlreq measures Japanese annotations with.
    /// </summary>
    [TestCase]
    public void TheAnnotationSizeIsTheRatioTheCallerAskedFor()
    {
        (List<LayoutElement> elements, _) = LayOutOne("zh-Hant", "今", "ㄐㄧㄣ");

        AssertThat(elements[0].Ruby!.Value.FontSize).IsEqual(BaseSize * 0.3f);
    }

    /// <summary>
    /// A tone mark is not another symbol of the reading: clreq §5.5.3.3 puts it against the corner of the last
    /// phonetic symbol, and §5.5.3.2 keeps the room reserved beside the base character the same whether or not one is
    /// written. So a reading with a tone mark is exactly as long as the same reading without one - which is what the
    /// engine got wrong by shaping the mark as a symbol with a cell of its own, leaving <c>ㄓㄨˋ</c> one cell longer
    /// than <c>ㄓㄨ</c> and, in the worst case, a reading taller than the character it annotates.
    /// </summary>
    [TestCase]
    public void AToneMarkIsNotASymbolOfTheReading()
    {
        foreach (WritingMode mode in RubyWritingModes)
        {
            (List<LayoutElement> marked, _) = LayOutOne("zh-Hant", "號", "ㄏㄠˋ", mode);
            (List<LayoutElement> plain, _) = LayOutOne("zh-Hant", "號", "ㄏㄠ", mode);

            RubyAnnotation withMark = marked[0].Ruby!.Value;
            RubyAnnotation without = plain[0].Ruby!.Value;

            AssertThat(withMark.Width).OverrideFailureMessage(
                $"{mode}: the reading with a tone mark is {withMark.Width:F3}px long and the same reading without "
                + $"one {without.Width:F3}px").IsEqual(without.Width);

            // Two symbols at the 3:10 ratio the fixture asks for: the mark added nothing to that. The tolerance is
            // HarfBuzz's own: advances come back quantised to 1/64 of a pixel, so two cells read as 2 x 4.797 and
            // not as 9.6 exactly.
            AssertThat(Math.Abs(without.Width - (without.FontSize * 2f)) < 0.05f).OverrideFailureMessage(
                $"{mode}: two symbols of {without.FontSize:F3}px are {without.Width:F3}px together").IsTrue();

            Glyph mark = withMark.Glyphs![^1];

            AssertThat(mark.Advance).OverrideFailureMessage(
                $"{mode}: the tone mark still advances the pen by {mark.Advance:F3}px").IsEqual(0f);

            // The room the base character gave the annotation does not depend on the tone, which is the other half of
            // clreq §5.5.3.2's rule and the reason "reading with" and "reading without" cannot be told apart here.
            AssertThat(withMark.BandWidth).IsEqual(without.BandWidth);
        }
    }

    /// <summary>
    /// A non-neutral tone mark hangs outside the last symbol's cell and straddles the top edge of that cell: "outside
    /// the upper right corner of the last phonetic symbol", with "half the space taken by a tone mark above the top of
    /// the adjacent phonetic character" (clreq §5.5.3.3). Outside means clear of the column, not on top of it - the
    /// mark is what tells the tone apart, so it may not sit on the symbol it follows.
    /// </summary>
    [TestCase]
    public void ANonNeutralToneMarkHangsAtTheLastSymbolsUpperRightCorner()
    {
        foreach (WritingMode mode in RubyWritingModes)
        {
            (List<LayoutElement> elements, _) = LayOutOne("zh-Hant", "號", "ㄏㄠˋ", mode);
            RubyAnnotation ruby = elements[0].Ruby!.Value;

            (float left, float top, float right, float bottom) = ColumnInkOf(ruby, ruby.Glyphs!.Length - 1);

            float columnEdge = ruby.X + (ruby.FontSize * 0.5f);
            float lastCellTop = ruby.BaselineY + ruby.Width - ruby.Glyphs[^2].Advance;

            AssertThat(left >= columnEdge - 0.01f).OverrideFailureMessage(
                $"{mode}: the mark's ink starts at {left:F3} while the column's own cell ends at {columnEdge:F3}, so "
                + "the mark is drawn over the symbols it belongs to").IsTrue();

            AssertThat(right > columnEdge).OverrideFailureMessage(
                $"{mode}: the mark's ink spans [{left:F3},{right:F3}], which lies inside the column").IsTrue();

            AssertThat(top < lastCellTop && bottom > lastCellTop).OverrideFailureMessage(
                $"{mode}: the mark's ink spans y=[{top:F3},{bottom:F3}] around the corner at {lastCellTop:F3}, so it "
                + "does not straddle the top edge of the last symbol's cell").IsTrue();

            AssertThat(Math.Abs(((top + bottom) * 0.5f) - lastCellTop) < 0.5f).OverrideFailureMessage(
                $"{mode}: the mark's ink is centred on {((top + bottom) * 0.5f):F3} and the corner is at "
                + $"{lastCellTop:F3}").IsTrue();
        }
    }

    /// <summary>
    /// A checked tone mark sits at the lower right of the column: "the dialectal checked tones are set outside the
    /// lower right corner of the phonetic symbols" (clreq §5.5.3.3), so its ink ends where the reading ends instead
    /// of hanging below it.
    /// </summary>
    [TestCase]
    public void ACheckedToneMarkSitsAtTheFootOfTheReading()
    {
        foreach (WritingMode mode in RubyWritingModes)
        {
            (List<LayoutElement> elements, _) = LayOutOne("zh-Hant", "號", "ㄏㄠㆷ", mode);
            RubyAnnotation ruby = elements[0].Ruby!.Value;

            (float left, _, float right, float bottom) = ColumnInkOf(ruby, ruby.Glyphs!.Length - 1);

            float columnEdge = ruby.X + (ruby.FontSize * 0.5f);
            float columnEnd = ruby.BaselineY + ruby.Width;

            AssertThat(left >= columnEdge - 0.01f).OverrideFailureMessage(
                $"{mode}: the mark's ink starts at {left:F3} while the column's own cell ends at {columnEdge:F3}")
                .IsTrue();

            AssertThat(right > columnEdge).OverrideFailureMessage(
                $"{mode}: the mark's ink spans [{left:F3},{right:F3}], which lies inside the column").IsTrue();

            AssertThat(Math.Abs(bottom - columnEnd) < 0.5f).OverrideFailureMessage(
                $"{mode}: the mark's ink ends at y={bottom:F3} while the reading ends at {columnEnd:F3}, so the mark "
                + "is not at the column's foot").IsTrue();
        }
    }

    /// <summary>
    /// The neutral tone comes before the reading and takes a cell of its own - a thin one: "in vertical Bopomofo the
    /// width of the space taken by the neutral tone mark does not change but the height ratio to the base character
    /// should be 1:15" (clreq §5.5.3.2), which is what keeps <c>˙ㄗ</c> from spending a whole symbol's room on a dot.
    /// </summary>
    [TestCase]
    public void TheNeutralToneComesBeforeTheReadingInAThinCell()
    {
        foreach (WritingMode mode in RubyWritingModes)
        {
            (List<LayoutElement> elements, _) = LayOutOne("zh-Hant", "桌", "˙ㄗ", mode);
            RubyAnnotation ruby = elements[0].Ruby!.Value;

            Glyph dot = ruby.Glyphs![0];
            Glyph symbol = ruby.Glyphs[1];
            float thin = BaseSize / 15f;

            AssertThat(dot.Advance).OverrideFailureMessage(
                $"{mode}: the neutral tone mark takes {dot.Advance:F3}px of the reading instead of {thin:F3}px")
                .IsEqual(thin);
            AssertThat(dot.Advance < symbol.Advance).OverrideFailureMessage(
                $"{mode}: the neutral tone mark takes as much room as a symbol ({dot.Advance:F3}px)").IsTrue();

            AssertThat(Math.Abs(ruby.Width - (thin + symbol.Advance)) < 0.01f).OverrideFailureMessage(
                $"{mode}: the reading is {ruby.Width:F3}px long, so the thin cell is not at its head").IsTrue();

            // Centred in the thin cell, on the column's own line: the dot is what a reader looks for before the
            // symbols, and hanging it on a corner would put it inside the first symbol's cell instead.
            (float left, float top, float right, float bottom) = ColumnInkOf(ruby, 0);
            float cellMiddle = ruby.BaselineY + (thin * 0.5f);

            AssertThat(Math.Abs(((top + bottom) * 0.5f) - cellMiddle) < 0.5f).OverrideFailureMessage(
                $"{mode}: the dot is centred on {((top + bottom) * 0.5f):F3} while its cell centres on {cellMiddle:F3}")
                .IsTrue();

            AssertThat(Math.Abs(((left + right) * 0.5f) - ruby.X) < 0.5f).OverrideFailureMessage(
                $"{mode}: the dot is centred on {((left + right) * 0.5f):F3} while the column's line is at {ruby.X:F3}")
                .IsTrue();
        }
    }

    // ── Helpers ──

    /// <summary>Base font size the fixtures are laid out at.</summary>
    private const float BaseSize = 16f;

    /// <summary>The writing modes a Bopomofo annotation is asked about in this suite.</summary>
    private static readonly WritingMode[] RubyWritingModes = [WritingMode.HorizontalTb, WritingMode.VerticalRl];

    /// <summary>
    /// Ink box of one glyph of a column annotation, in content coordinates.
    /// <para>
    /// The assertions about a tone mark are about where its stroke lands, and what the layout publishes for a column
    /// is exactly what a renderer draws from: the annotation's origin, its glyphs and their advances. The box is
    /// therefore derived here the way the renderer derives it - the glyph's own outline, moved by the shaper's
    /// offsets, with the pen walking the advances down the column - rather than read back out of the engine's own ink
    /// measurement, which would make the assertion an echo of the code under test.
    /// </para>
    /// </summary>
    /// <param name="ruby">Annotation to measure.</param>
    /// <param name="glyphIndex">Index of the glyph in its run.</param>
    /// <returns>The ink box in content coordinates, or a point at the pen when nothing can be measured.</returns>
    private static (float Left, float Top, float Right, float Bottom) ColumnInkOf(RubyAnnotation ruby, int glyphIndex)
    {
        float pen = ruby.BaselineY;

        for (int i = 0; i < glyphIndex; i++)
            pen += ruby.Glyphs![i].Advance;

        Glyph glyph = ruby.Glyphs![glyphIndex];

        if (!FontCatalog.Shared.TryGetTypeface(glyph.FontId, out SKTypeface? typeface) || typeface is null)
            return (pen, pen, pen, pen);

        using var font = new SKFont(typeface, ruby.FontSize);
        using SKPath? path = font.GetGlyphPath((ushort)glyph.Id);

        if (path is null)
            return (pen, pen, pen, pen);

        // Down a column the pen does not move across it, and the shaper's vertical origin reports the offset negated:
        // the drawable baseline sits that far below the pen.
        SKRect bounds = path.Bounds;
        float x = ruby.X + glyph.OffsetX;
        float y = pen - glyph.OffsetY;

        return (x + bounds.Left, y + bounds.Top, x + bounds.Right, y + bounds.Bottom);
    }

    private sealed record LayoutResult(
        List<LayoutElement> Elements,
        float LineHeight,
        float FirstTextBaseline,
        EmphasisMarkGeometry? Marquee);

    private static DrawElement Ruby(string baseText, RubyDistribution distribution) => new()
    {
        Type = DrawElement.ElementType.Text,
        Text = baseText,
        Font = ThemeDB.FallbackFont,
        FontSize = 16,
        Color = new Color(0.1f, 0.1f, 0.1f),
        Ruby = new RubySpec { Text = "とうきょう", Distribution = distribution },
    };

    private static DrawElement Text(string text) => new()
    {
        Type = DrawElement.ElementType.Text,
        Text = text,
        Font = ThemeDB.FallbackFont,
        FontSize = (int)BaseSize,
        Color = new Color(0.1f, 0.1f, 0.1f),
    };

    /// <summary>The ruby placement a language's profile asks for.</summary>
    /// <param name="languageTag">Tag to resolve, or null for a document that declares none.</param>
    /// <returns>The placement in effect.</returns>
    private static RubyPlacement PlacementOf(string? languageTag) => ResolvedOf(languageTag).RubyPlacement;

    /// <summary>The annotation orientation a language's profile asks for.</summary>
    /// <param name="languageTag">Tag to resolve, or null for a document that declares none.</param>
    /// <returns>The orientation in effect.</returns>
    private static RubyOrientation OrientationOf(string? languageTag) => ResolvedOf(languageTag).RubyOrientation;

    /// <summary>
    /// Effective parameters of a request that declares one language and nothing else: the merged answer is what
    /// the pipeline reads, so it is what a language's decision has to show up in.
    /// </summary>
    /// <param name="languageTag">Tag to resolve, or null for a document that declares none.</param>
    /// <returns>The resolved parameters.</returns>
    private static ResolvedTypography ResolvedOf(string? languageTag) =>
        LanguageProfileRegistry.Shared.Resolve(new TypographySettings
        {
            MaxWidth = 240f,
            LanguageTag = languageTag,
        });

    /// <summary>
    /// Lay out one character with (or without) a mono annotation, at clreq §5.5.3.2's 3:10 ratio, followed by a
    /// plain character so the line has a character that never moves.
    /// </summary>
    /// <param name="languageTag">Language the paragraph declares.</param>
    /// <param name="baseText">The character being annotated.</param>
    /// <param name="bopomofo">Its reading, or null for the same text without an annotation.</param>
    /// <param name="writingMode">Writing mode to lay the line out in.</param>
    /// <returns>The laid-out elements and the height of the line they are on.</returns>
    private static (List<LayoutElement> Elements, float LineHeight) LayOutOne(
        string languageTag, string baseText, string? bopomofo, WritingMode writingMode = WritingMode.HorizontalTb)
    {
        DrawElement element = new()
        {
            Type = DrawElement.ElementType.Text,
            Text = baseText,
            Font = ThemeDB.FallbackFont,
            FontSize = (int)BaseSize,
            Color = new Color(0.1f, 0.1f, 0.1f),
            Ruby = bopomofo is null
                ? null
                : new RubySpec
                {
                    Text = bopomofo,
                    Distribution = RubyDistribution.Mono,
                    SizeRatio = 0.3f,
                },
        };

        DrawElement[] elements = [element, Text("後")];

        using var engine = new TypographyEngine(new TypographySettings
        {
            MaxWidth = 240f,
            MaxHeight = 240f,
            WritingMode = writingMode,
            LanguageTag = languageTag,
            FirstLineIndent = 0,
            Alignment = TextAlignment.Left,
        });

        List<LayoutLine> lines = engine.PrepareAndLayout(elements, out _);
        return (engine.GetLayoutElements(elements), lines[0].Height);
    }

    private static LayoutResult LayOut(DrawElement? ruby, string languageTag)
    {
        var elements = new List<DrawElement>();

        if (ruby is null)
            elements.Add(Text("振り仮名の中文"));
        else
        {
            elements.Add(ruby.Value);
            elements.Add(Text("の中文"));
        }

        using var engine = new TypographyEngine(new TypographySettings
        {
            MaxWidth = 240f,
            LanguageTag = languageTag,
            FirstLineIndent = 0,
            Alignment = TextAlignment.Left,
        });

        DrawElement[] source = [.. elements];
        List<LayoutLine> lines = engine.PrepareAndLayout(source, out _);
        List<LayoutElement> laidOut = engine.GetLayoutElements(source);
        float baseline = 0f;

        foreach (LayoutElement element in laidOut)
        {
            if (element.Type == DrawElement.ElementType.Text)
            {
                baseline = element.BaselineY;
                break;
            }
        }

        return new LayoutResult(laidOut, lines[0].Height, baseline, null);
    }

    /// <summary>Lay out the base text without any annotation, in the same shape as the annotated case.</summary>
    private static LayoutResult LayOutPlainBase()
    {
        DrawElement[] elements = [Text("東京"), Text("の中文")];

        using var engine = new TypographyEngine(new TypographySettings
        {
            MaxWidth = 240f,
            LanguageTag = "zh-Hans",
            FirstLineIndent = 0,
            Alignment = TextAlignment.Left,
        });

        List<LayoutLine> lines = engine.PrepareAndLayout(elements, out _);
        return new LayoutResult(engine.GetLayoutElements(elements), lines[0].Height, 0f, null);
    }

    private static LayoutResult LayOutEmphasis(string text, string languageTag)
    {
        DrawElement[] elements =
        [
            new DrawElement
            {
                Type = DrawElement.ElementType.Text,
                Text = text,
                Font = ThemeDB.FallbackFont,
                FontSize = 16,
                Color = new Color(0.1f, 0.1f, 0.1f),
                EmphasisMark = EmphasisMarkStyle.Dot,
            },
        ];

        using var engine = new TypographyEngine(new TypographySettings
        {
            MaxWidth = 240f,
            LanguageTag = languageTag,
            FirstLineIndent = 0,
            Alignment = TextAlignment.Left,
        });

        List<LayoutLine> lines = engine.PrepareAndLayout(elements, out _);
        List<LayoutElement> laidOut = engine.GetLayoutElements(elements);
        return new LayoutResult(laidOut, lines[0].Height, laidOut[0].BaselineY, laidOut[0].Emphasis);
    }

    /// <summary>First cluster index reported for a source element, or -1 when it has none.</summary>
    /// <param name="engine">Engine whose last layout is inspected.</param>
    /// <param name="elements">The full source array the layout was produced from.</param>
    /// <param name="sourceIndex">Source element to look for.</param>
    /// <returns>The cluster index of its first laid-out element.</returns>
    private static int FindFirstClusterOf(TypographyEngine engine, DrawElement[] elements, int sourceIndex)
    {
        foreach (LayoutElement laidOut in engine.GetLayoutElements(elements))
        {
            if (laidOut.SourceIndex == sourceIndex && laidOut.GlyphRun is not null)
                return laidOut.ClusterStart;
        }

        return -1;
    }

    private static void AssertThrows<TException>(Action action) where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException($"expected {typeof(TException).Name}, but nothing was thrown");
    }
}
