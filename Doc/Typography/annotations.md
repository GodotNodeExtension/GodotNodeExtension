# Typography annotations

An annotation — a reading set over a run of base text, 注音 or ルビ — is laid out rather than decorated: the layout
shapes it, decides where it goes and which way it reads, and hands the renderer finished geometry with its own
glyphs. It is inline content, not a separate line: it stays with the base text it belongs to, and nothing separates
the two.

This document covers the three distributions, the side and the direction an annotation is set in, its size, the
room it takes from the line, and the emphasis mark, which is the other inline mark the engine places. The fields
involved are in [`input-format.md`](input-format.md) (`RubySpec`, `EmphasisMark`) and
[`output-format.md`](output-format.md) (`RubyAnnotation`, `EmphasisMarkGeometry`); which side and direction a
language uses is in [`languages.md`](languages.md).

## Three distributions

`RubySpec.Distribution` says how the annotation is spread over the base text.

| Distribution | Over the base run | Breakability |
|---|---|---|
| `Mono` | One piece per base character, centred on that character (jlreq's モノルビ). | The annotated run is atomic. |
| `Group` | One piece for the whole run, centred over the run (jlreq's グループルビ). | Same. |
| `Jukugo` | One piece per character, laid out as a group (jlreq's 熟語ルビ, §3.3.7). | Same. |

- **The annotated run never breaks across lines.** Every position inside the base run of an annotation is refused
  as a break, and the output says why: the boundary that would have broken there reports the annotation group as
  its reason. A line that cannot hold the run moves the whole run down instead of splitting annotation from base.
- **`Mono` with fewer annotation characters than base characters is measured as one group piece**, because cutting
  it per character would mean inventing text. In every distribution the annotation is centred on what it covers: on
  its character for `Mono` and `Jukugo`, and on the whole run for `Group` — or for a `Mono` annotation that was
  widened into a single piece.
- **`Jukugo` widens the base run.** When a character's annotation is wider than the character, the shortfall is
  added to the base text's inter-character spacing so neighbouring annotations keep apart (jlreq §3.3.7), up to a
  cap per position that is an engine value — the specification states the principle, not a number. The added room
  is part of the base text's own advance, so line breaking, the element box and the pen all see it without knowing
  that annotations exist. The cap is why a very wide annotation can still meet its neighbour; an unbounded stretch
  would not.
- **The distribution that placed each annotation is reported**: an annotation element records it as its reason.

```text
            東 京                  base text, one annotated run
Mono        と う | き ょ う        one piece per character, centred on it
Group       と う き ょ う          one piece, centred over the two characters
Jukugo      と う | き ょ う        per character, base run widened between the pieces
```

## Where an annotation goes

Both halves are the language's business: the side an annotation sits on, and the direction it reads in. They are
independent values, and a language declares both.

### Side: RubyPlacement

| Placement | Where the annotation sits | Where its room comes from |
|---|---|---|
| `ReserveAbove` | Above the text box, inside the line. | The line grows by the band, and its baselines move down together. |
| `OverflowBetweenLines` | Above the text box as well, because that is where the space for it is: jlreq's 行間処理 sets an annotation between the lines. | The same band: the engine reserves it rather than trusting the author's line spacing, because a layout whose annotation overlaps the line above it is not a usable layout. |
| `ReserveBeside` | Beside the base character. | The base text's own advance: the annotated characters grow by half an em of the base size each (clreq §5.5.3.2), and the annotation is placed inside that room, centred on it. The line's height does not change unless the column is taller than its base character. |

The side is expressed along the block axis, so in vertical writing "above the text box" means the column's
block-start side — its right (clreq §5.5.3.1, jlreq §3.3.9). Beside-set Bopomofo is what that placement is for
(clreq §5.5.3.1), in either writing mode.

### Direction: RubyOrientation

- **`Horizontal`**: the annotation is set along the line, the way the base text runs.
- **`Vertical`**: the annotation is set down a column of its own, one symbol under the next, beside the base
  character.

The two are independent, so **a horizontal paragraph can carry a column**: Traditional Chinese sets Bopomofo as a
column beside the base character in horizontal writing as much as in vertical (clreq §5.5.3.1), while Japanese and
Simplified Chinese set their annotations along the line. That is why the orientation is not derived from the
writing mode.

Two consequences are worth knowing before relying on it:

- **The annotation is shaped in the direction it reads.** A column is shaped top to bottom, so its glyphs carry
  vertical origins; shaping it along the line instead would leave the renderer with glyphs that have none, and the
  column would be drawn up and to the right of where the layout placed it.
- **A band above the line cannot carry a column**, so the combination is refused where a language is registered:
  the band is as deep as the annotation is tall, while a column runs along the base character and needs room beside
  it. A profile that asks for both fails loudly instead of being laid out as something else.

The output says which one it got, and how wide it is in that direction: the annotation's `Width` is the band's
width when it reads along the line and the column's height when it reads down one, and the room reserved across
that direction is reported separately, so a renderer can centre the annotation in its own band instead of anchoring
it at an edge.

## Annotation size

- **The default is half the base size.** `RubySpec.SizeRatio` defaults to `0.5`: the size jlreq §3.3.3 sets
  Japanese annotations at, and the size CSS Ruby's user-agent stylesheet gives every annotation. A value of `0` or
  less is treated as the default.
- **clreq §5.5.3.2's 3:10 ratio is not enforced.** That figure belongs to Bopomofo, so the engine does not apply it
  to a caller who did not ask: a Traditional Chinese annotation gets the half size like any other, and a caller who
  wants the Bopomofo proportion asks for it with `SizeRatio = 0.3f`.
- The annotation is shaped at the resulting size, and that size is reported on the annotation geometry, so the
  renderer rasterizes the face at it and never re-measures.

## The room an annotation takes

- **The band is measured from the ink the glyphs leave, not from the annotation font's line metrics.** A tone mark
  rises above the font's ascent, and a tall glyph exceeds it too, so a band measured with line metrics alone cuts
  off part of the annotation it exists for. The line metrics stay the floor — an annotation is still set as a text
  run — so the band is the larger of the ink extent and the font's own ascent plus descent.
- **A line grows for what it carries.** The line reports the room it had to find outside its text box on each side,
  and its height is that room plus the text box plus the spacing the request asked for. So the baselines on an
  annotated line move down together and two annotated lines cannot overlap, even when the author gave no line
  spacing; the line spacing a request declares stays a floor, and the engine adds the room the content asks for
  rather than taking it out of the author's spacing.
- **A beside-set column taller than its base character adds its overhang**, split evenly between the two sides: the
  column is centred on its character, so the part that reaches past the character's box is half of the difference
  on each side. (That is the horizontal case; in vertical writing the same room is found on the column's block-start
  side instead.) The annotation's room *along* the line is already part of the base advance, which is why this is
  the only room it asks for from the line's height.
- **At a line's start or end, the line gives way only for what would leave the area** (jlreq §3.3.9). An annotation
  wider than the run it annotates is centred, and only the part that would stick out past the edge moves the line's
  content in, by that much and no more. Room before the line's content is not wasted — padding, a first-line indent
  and the intervals an exclusion leaves all sit there — so a paragraph already indented by a character, or a layout
  with padding, keeps its position while the annotation still stays inside the area. What a line gave way for is
  reported as part of the line's geometry, so a consumer that repositions a line starts from there.
- **Two marks on the same character share the room, they do not add to each other.** An annotation and an emphasis
  mark on one character are given the larger of the two rooms on the side they both need, not the sum; a character
  under both is laid out to fit, not to stack.

## Bopomofo tone marks

A Bopomofo tone mark is not another symbol of the reading: it is narrower than a symbol's cell, it sits against the
corner of the last symbol, and the reading's own length does not depend on it (clreq §5.5.3.3, §5.5.3.2). The engine
places the marks that way as soon as an annotation is Bopomofo — one that carries any symbol of the block:

- **平上去 and the dialectal non-checked tones** (`ˊˇˋˉ`, `˪˫`) take no cell of their own. Their ink hangs outside the
  column of symbols and straddles the top edge of the last symbol's cell — "outside the upper right corner of the
  last phonetic symbol", with half of the mark above that symbol and half of it beyond the column.
- **The checked tones** (`ㆴㆵㆶㆷ`) hang on the same side but at the column's foot: the mark's ink ends where the
  reading ends, which is the convention's "outside the lower right corner".
- **The neutral tone** (`˙`) comes before the reading and does take a cell — a thin one, 1:15 of the base character
  along the reading direction, with the dot centred in it.

Two consequences are worth knowing before relying on it:

- **The room reserved beside the base character is the same either way.** clreq §5.5.3.2 states half of the base
  character's size for a right-hand annotation, tone marks included, and it says a reading without a tone mark takes
  the same room as one with. So `ㄏㄠ` and `ㄏㄠˋ` occupy the same space and the characters of a paragraph line up.
- **A reading cannot be taller than the character it annotates any more.** Read as a symbol, a tone mark added a
  fourth cell to a three-symbol reading at the 3:10 ratio, which is taller than the character itself; now the reading
  is its symbols' cells long.

A language whose annotation is set along the line (the other standard form, Bopomofo above the character, clreq
§5.5.3.1) gets the same treatment: the mark keeps half of its space past the last symbol and its ink against the top
or the bottom of the annotation's own box. Only a Bopomofo annotation's tone marks are placed this way — the
diacritics of a Romanization belong to its letters and are shaped with them.

## Emphasis marks and underlines

- **The mark's side comes from the language, not from the input.** `DrawElement.EmphasisMark` chooses the style
  (`None`, `Dot` or `SesameDot`); where the mark goes belongs to the language: below the characters in horizontal
  Chinese (clreq §5.3.1), above them in horizontal Japanese (jlreq §3.3.9), and to the right of the column in
  vertical writing, which is the block-start side in both conventions.
- **The mark's character follows the style and the side.** A filled circle for the Chinese form or wherever the
  mark is below, a bullet above, and the sesame dot where the element asks for `SesameDot` — jlreq's vertical form,
  which the element names explicitly so that the choice survives whatever the language would do by default.
- **Size.** The mark is set at a quarter of the base size by default. That is a convention value rather than a
  figure any specification states, kept in one place so a language can state its own.
- **Placement.** The mark is centred on its character along the line, and sits in the line gap on its side —
  outside the character's own box, which is what "in the line gap" means. The geometry reports the centre point and
  the side, so a renderer draws the one character at the given size and does not re-derive the side from the
  language.
- **The line grows for the mark.** The mark needs one mark of offset plus half a mark of its own width, so the line
  reserves one and a half times the mark size on the side it sits on. A marked paragraph is therefore laid out
  inside its own box instead of dropping the mark onto the line above it.
- **An underline** grows the line the same way, on the block-end side: the part of the font's own underline that
  reaches past the descender is added to the room the line reports below. Both decorations are drawn from their own
  geometry, which [`output-format.md`](output-format.md) describes field by field.

## Making an annotation move

An annotation and an emphasis mark are input values; everything else about them is geometry the layout produces.

```csharp
using Godot;
using GodotNodeExtension.Component.Typography.Core.Model;
using GodotNodeExtension.Component.Typography.Server;

// One source element: a base run, its annotation, and an emphasis mark over the same characters.
var source = new DrawElement
{
    Type = DrawElement.ElementType.Text,
    Text = "東京",
    Font = font,                              // the base font; the annotation is measured with it too
    FontSize = 18,
    Ruby = new RubySpec
    {
        Text = "とうきょう",
        Distribution = RubyDistribution.Group, // one piece, centred over the two characters
        SizeRatio = 0.5f,                      // half the base size (the default)
    },
    EmphasisMark = EmphasisMarkStyle.Dot,
};

// The language decides where the annotation goes and which way it reads; "ja" puts it above, along the line.
var settings = new TypographySettings { LanguageTag = "ja", MaxWidth = 320f };

TypographyServer server = TypographyServer.Instance;
LayoutHandle handle = server.CreateHandle();
long requestId = server.RequestFullLayout(handle, [source], settings);

if (server.TryGetResult(handle, out LayoutResult result) && result.RequestId == requestId)
{
    foreach (LayoutElement element in result.Elements!)
    {
        // The annotation comes back shaped, measured and placed: draw its glyphs, never measure them again.
        if (element.Ruby is { } annotation)
        {
            DrawAnnotation(annotation.Glyphs!, annotation.X, annotation.BaselineY,
                annotation.FontSize, annotation.Width, annotation.Orientation);
        }

        // The mark is a character, centred on its character, on the side the language put it.
        if (element.Emphasis is { } mark)
            DrawMark(mark.Mark, mark.X, mark.CenterY, mark.Size);
    }
}
```
