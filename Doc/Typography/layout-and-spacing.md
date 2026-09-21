# Typography layout and spacing

Where a line breaks is one half of a layout; the other half is how what it broke into fills the room it was given — how
lines are aligned, how a paragraph is indented, how tall a line is, how far apart paragraphs stand, how a box that is not
text takes part in a line, how a grid constrains the elements, and what the layout reports as its content size. Every
number this page explains comes back in the output, described field by field in [`output-format.md`](output-format.md);
the request fields named here are in [`input-format.md`](input-format.md), and how a line is filled when it is justified
is in [`line-breaking.md`](line-breaking.md).

## Alignment

`TextAlignment` has four values. `Alignment` on the request decides which one a document uses, `null` leaves the choice to
the language, and a paragraph may state its own:

| Value | What the line does | Where it is used |
|---|---|---|
| `Left` | the content starts at the start of its interval | Western and undeclared text; in a right-to-left paragraph this is where a line's content begins, because the direction decides which edge "start" is |
| `Center` | the slack is split evenly between the two sides | titles and short lines |
| `Right` | the content ends at the end of its interval | captions and number columns |
| `Justify` | the line fills its interval by moving its gaps | the CJK default, and any Western paragraph that asks for it |

- **Alignment is applied per interval.** When a line offers more than one interval because text flows around an exclusion,
  each interval is aligned inside its own bounds — an interval is a line as far as alignment is concerned, so centring a
  split line does not move one side's text onto the other's. `Left` and `Justify` leave every interval at its own start.
- **The last line of a paragraph is not justified.** It ends where it ends, and only the lines before it are stretched or
  squeezed; a line that offers more than one interval is left-aligned per interval even when it is not the last one.
- **The paragraph's direction decides the edge.** A right-to-left paragraph fills from the right edge: its lines start
  there, and the runs of a mixed line are put into visual order before the line is placed against that edge. Alignment is
  therefore about the inline axis, not about left and right as screen directions.
- **A rounding-level difference is not worth moving glyphs.** A deficit or slack below half a pixel leaves the line at its
  natural position.

## Indents

| Field | Unit | Where it lives | What it applies to |
|---|---|---|---|
| `FirstLineIndent` | character widths (ems) | request, paragraph override | the first line of a paragraph, unless that paragraph states its own |
| `LeftIndent` | px | paragraph | every line of that paragraph |
| `RightIndent` | px | paragraph | every line of that paragraph |

A first-line indent is stated in **character widths** because that is how the conventions state it: two characters for
Chinese, one for Japanese and Korean, none for Western text. The engine turns it into pixels with the em of the paragraph
— the font size of its first run that declares one, and `16` px when nothing declares a size — so "two characters" is
exact for a setting whose characters are a font size wide, which is what the CJK conventions assume. Zero turns the
indent off, and a paragraph may state its own number on the element that opens it: an explicit zero indents nothing even
when its language asks for two characters.

Left and right indents are **absolute pixel values, not overrides**: they always apply. They narrow the outer edges of the
line's room, which is how a block quote is simply narrower, and when an exclusion splits a line the left indent comes off
the first interval's start while the right indent comes off the last interval's end — so an exclusion in the middle of a
line never indents what sits to its right.

The indent reaches the output as `LayoutLine.LineIndent`. Breaking narrowed the line by it, so anything that repositions a
line has to start from the same place, `LineLeft + LineIndent`; the field is the first-line indent **plus** any room the
line had to give way for at its start — an annotation wider than the run it annotates pushes the line's content in by the
part of it that would leave the area (see [`annotations.md`](annotations.md)). A consumer that re-arranges a line
therefore starts from `LineLeft + LineIndent` and never from `LineLeft`.

## The line height model

A line is as tall as what it carries, and what it carries is not only text. The line's box is assembled from four parts,
one of which the author owns:

```
line box along the block axis
  ExtraAbove      room the content needs on the block-start side of the text box
  Ascent          the tallest element's ascent
  ---------------- every baseline on the line sits on this line
  Descent         the tallest element's descent
  ExtraBelow      room the content needs on the block-end side of the text box
  LineSpacing     the leading the request asked for
```

`LayoutLine.Height` is `ExtraAbove + Ascent + Descent + ExtraBelow + LineSpacing`. `Ascent` and `Descent` are the maxima
over the elements on the line — a font's own line box, which is what line spacing is measured against — and everything
that does not fit inside that box is reported separately, per side, so a consumer can draw the line as the layout sized
it without knowing why:

| What needs the room | Which side | Where it comes from |
|---|---|---|
| the text box | both, as ascent and descent | the tallest element the line measured: a font's line box, unless a taller inline object is on the line |
| an annotation band | block-start side (`ExtraAbove`) | an annotation placed outside the text box — above it in horizontal writing, on the column's block-start side in vertical writing. The band holds the annotation's ink, with the annotation font's own line metrics as a floor, because a tone mark rises above the font's ascent and a band measured from line metrics alone would hide the mark it exists for |
| an overhanging annotation column | both sides (`ExtraAbove`, `ExtraBelow`) | the part of a Beside annotation's column that reaches past the character it annotates, split evenly between the two sides; the room for a column that stays within the character already comes out of the base text's own advance |
| an underline | block-end side (`ExtraBelow`) | the part of the font's own underline — its position plus its thickness — that reaches below the descender the line was measured with |
| an emphasis mark | the side the language puts it on | the mark sits outside the character's own box by convention, so its room — one mark of offset plus the mark itself — belongs to the line rather than to the character |

- **The author's line spacing is a floor, not a ceiling.** The engine adds the room the content asks for and never takes
  it out of `LineSpacing`: a line that carries an annotation or a mark is taller than the same line without one, whether
  or not the author left room for it. The conventions disagree about who provides that room — one leaves it to the
  author's line spacing, another asks for a line gap of one and a half times the base size for a right-hand annotation in
  vertical writing — and neither says what to do with a document whose author gave none, so the engine reserves it: a
  document with no line spacing at all still cannot have two annotated lines overlap.
- **The baselines on a line move together.** The room on the block-start side is added above the whole line, so every
  baseline on it sits `ExtraAbove` into the box, and the next line starts below the whole box. Two neighbouring lines
  therefore cannot overlap, whatever either of them carries.
- **The first line is computed exactly like the others.** There is no previous line to take the room from: a first line's
  band is added on top of it, inside the content box, and the content simply starts that much lower — which is why an
  annotated first line does not sit flush with the top of the content.
- **`LineSpacing` is leading, not a CSS line height.** It is added to the text's own line box (ascent plus descent), which
  is the convention clreq and jlreq measure line spacing against; zero means the lines stand on their own boxes with
  nothing between them.
- **A paragraph cannot override the line spacing.** A paragraph owns its indent, its alignment and the space before and
  after it; a paragraph that needs more leading than the document's takes it in the request, where it applies to the whole
  document.
- **An empty line costs the line spacing alone.** A paragraph break with nothing on the line advances by `LineSpacing`
  only; there is no box to build.
- **A block is a line of its own.** A block is placed as one line whose height is the block's own height and which carries
  no line spacing of its own; how an auto-size block measures itself is described with the block's own fields in
  [`input-format.md`](input-format.md).

## Paragraph spacing

| Field | Unit | Where it lives | Used for |
|---|---|---|---|
| `SpacingAfter` | px | paragraph | the space after that paragraph |
| `SpacingBefore` | px | paragraph | the space before that paragraph, added to the preceding one's after-space |
| `ParagraphSpacing` | px | request | the fallback for `SpacingAfter` where a paragraph does not state one |

The distance between two paragraphs is the first one's `SpacingAfter` — or the request's `ParagraphSpacing` when the
paragraph stays silent — plus the second one's `SpacingBefore`. Paragraph spacing is therefore applied as the space
**after** the preceding paragraph, and a "before" is an addition to it rather than a replacement; a paragraph that states
nothing leaves no space before itself, which is why a document that wants uniform gaps says so once, on the request.

Paragraph spacing moves paragraphs, not lines: nothing about a line's own box changes, and both fields are read from the
element that opens a paragraph, never from the elements inside it.

## Inline objects

An element that is not text — an image, a rectangle, a line segment, a region the caller draws itself — is an atomic box
on the line: it takes its extent from its `Size`, nothing may break inside it, and it consumes no text characters. Its
height takes part in the line's box through `VerticalAlignment`, which decides how that height is split between ascent and
descent and where the box then sits:

| `VerticalAlignment` | Ascent / descent | Where the box sits |
|---|---|---|
| `Baseline` (the default) | the whole height as ascent | its bottom on the text baseline |
| `Top` | the whole height as ascent | its top at the top of the line's box |
| `Middle` | half as ascent, half as descent | centred on the line's text region, not on the line's own box, so an object taller than the text is centred on the text it accompanies rather than on itself |
| `Bottom` | the whole height as descent | its bottom at the bottom of the line's box, above its line spacing |

- **The split is what makes the object grow the line.** `Ascent` and `Descent` are the maxima over the elements on the
  line, so an image taller than the text makes the line taller and never overlaps the line above or below it.
- **The alignment also decides the reported baseline.** An object reports `BaselineY` as its box' block-start edge plus
  the ascent the alignment gave it, so a consumer places the object with the same coordinate it uses for text.
- **An action is not a box at all.** It is a zero-width marker that takes no room, and nothing may break after it, which
  is how a trigger point stays with the text it belongs to.
- **The object is not a break candidate on its own account, but the text around it is**: a line may break before it or
  after it unless a rule forbids that (see [`line-breaking.md`](line-breaking.md)).

## Grid alignment

`GridStep` is `null` by default, which means no grid. `GridOrigin` — a distance in pixels from the content origin — says
where the grid starts, and is only read when a step is set.

A grid is a promise that things line up: with a step set, every element's inline start is moved forward to the next
multiple of the step counted from the line's start plus `GridOrigin`. It moves forward only — a grid may push an element
further into the line, never to the left of where the layout put it — and it is applied per element, which is the
granularity the layout has: one element per cluster, and a whole Latin word is one cluster.

The grid runs after the alignment has decided how the line fills its interval and before the tab stops are applied,
because a grid is a global rule while a tab stop is a position the author named. Its purpose is the mixing of scripts: a
quarter-em gap between CJK and Latin text plus a grid step of one character makes a Latin word occupy an integral number
of character cells, which is what makes a page line up column by column (clreq §6.2.4).

## Content size

`LayoutResult.ContentSize` is the box the laid-out content occupies in content space: the far edge of the widest element,
and the block-axis end of the last line, with the request's trailing `Padding` counted in. The padding is applied on all
four sides, so the value is directly usable as a consumer's scroll region and canvas size.

- It grows for what the layout placed outside a text box: a hanging mark, an annotation column, a background widened by
  its own inline padding.
- The empty case is zero: a layout with no line has no content size to report.
- **The pair is an (inline, block) pair, mapped into content space.** In horizontal writing that is the familiar width
  and height; in vertical writing the first component is how far the columns run and the second is how long the longest
  column is, so a consumer sizing a surface around columns reads both from one value.

## Defaults per language

A request that leaves a language-governed field `null` gets the language's value; a value the request states always wins
over it; and a paragraph may name a language of its own, so one document can mix these tables paragraph by paragraph.

| Language | First-line indent | Alignment |
|---|---|---|
| Simplified Chinese (`zh-Hans`) | 2 characters | `Justify` |
| Traditional Chinese (`zh-Hant`) | 2 characters | `Justify` |
| Japanese (`ja`) | 1 character | `Justify` |
| Korean (`ko`) | 1 character | `Justify` |
| English and the Western languages | none | `Left` |
| Right-to-left languages (Arabic, Hebrew) | none | `Left`, which for them means the paragraph's own start edge |
| Undeclared (`und`) | none | `Left` |

- **No shipped language asks for line spacing or paragraph space.** Both are the request's to give, and zero means what it
  says: lines stand on their own boxes, and paragraphs are separated by nothing but their own lines. A language a host
  registers may state both, and a request that leaves them `null` then gets that value the same way it gets an indent.
- **Writing mode is deliberately not in the table.** A writing mode is a decision about the document, not a property of a
  language, so a language profile that declares one is only offering a default to a caller who did not choose — a request
  that chooses a mode always lays the document out that way.
- **The indent is a whole number of character widths**, not a pixel value, and alignment is an enumeration: neither
  depends on the font, which is why a table like the one above can state them exactly.

```csharp
using GodotNodeExtension.Component.Typography.Core.Model;

// The line box reports its two sides separately, so a consumer can draw the whole of it.
static float LineBox(LayoutLine line) =>
    line.ExtraAbove + line.Ascent + line.Descent + line.ExtraBelow;

// The content of the first interval begins at the line's start plus the indent it was laid out with.
static float ContentStart(LayoutLine line) => line.LineLeft + line.LineIndent;
```
