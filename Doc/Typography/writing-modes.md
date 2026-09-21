# Typography writing modes

A writing mode says which way a line runs and which way lines advance, and it is the request's decision rather than
the language's: the same language is set in columns in a book and along lines on a page, so the caller chooses the
mode and the language profile only supplies what a language prescribes *inside* that mode. This document describes
what a horizontal layout and a vertical one have in common, what a column changes, and how a canvas host draws one.
The fields themselves are in [`input-format.md`](input-format.md) and [`output-format.md`](output-format.md); why the
pipeline is shaped this way is in [`design.md`](design.md). The examples below use the component's namespaces:

```csharp
using Godot;
using GodotNodeExtension.Component.Typography.Core.Model;   // WritingMode, TypographySettings, LayoutElement, Glyph
```

## Horizontal and vertical writing

Every layout is expressed on two axes: the **inline** axis, along which a line runs, and the **block** axis, along
which lines advance. A writing mode is nothing more than the choice of which direction each axis grows in, plus where
block coordinate zero sits.

| Mode | A line is | Inline axis | Block axis | Block coordinate zero |
|---|---|---|---|---|
| `HorizontalTb` | a row that runs rightward, read left to right | `+X` | `+Y` | the content box' left edge |
| `VerticalRl` | a column that runs downward, columns advancing leftward, read right to left | `+Y` | `-X` | the content box' right edge |
| `VerticalLr` | reserved: the vocabulary exists, the geometry does not follow it | `+Y` | `+X` | — |

Content space itself never rotates: X is to the right and Y down in every mode, so exclusion regions, padding, scroll
offsets and hit testing stay page geometry. What the mode changes is which of those two coordinates carries which
quantity, and this is the one place where the two modes differ.

## Asking for a writing mode

`TypographySettings.WritingMode` is nullable, and `null` means "whatever the language's profile declares". No built-in
language profile declares a vertical mode — a mode is a layout decision, not a property of a language — so a request
without a mode is a horizontal layout, and columns are always asked for explicitly.

`VerticalRl` is accepted. `VerticalLr` is not: a request that asks for it fails at request resolution with an explicit
`NotSupportedException`, because laying the text out horizontally instead would hand back a document that claims to be
vertical. A clear refusal is more useful than a quiet wrong answer.

A vertical request should name both bounds, because they bound different things there:

- `MaxHeight` bounds a line — how long a column may be. This is the inline limit in vertical writing.
- `MaxWidth` bounds the block axis — how far the columns may run. It also anchors block coordinate zero. A request
  that asks for no column height (zero or less) falls back to `MaxWidth` as the line limit.

```csharp compile
// Columns: the caller asks for them, and a request that wants them sets both bounds.
var settings = new TypographySettings
{
    WritingMode = WritingMode.VerticalRl,  // null = whatever the language profile declares (horizontal today)
    LanguageTag = "ja",
    MaxHeight = 480f,                      // px: how long a column may be (the inline limit)
    MaxWidth = 320f,                       // px: how far the columns may run (the block extent)
};
```

## What changes in vertical writing

Everything the engine decides, it decides in inline and block scalars, so a mode is not a second pipeline — it is a
different mapping of the same numbers. Seven things about that mapping are visible to a caller.

### Columns and where they start

A line is a column that runs downward, and lines advance **leftward**: the first column stands at the content box'
right edge, the next one to its left, and so on. That right edge is where block coordinate zero sits, so a block
coordinate grows leftward in `VerticalRl` — the opposite of the block axis in horizontal writing, and the thing to
remember when a position or a line's own coordinate is read.

```text
content box of a VerticalRl request

   block axis grows leftward   <--------------------------------
   +-------------+-------------+-------------+
   |  column 2   |  column 1   |  block      |   the inline axis grows downward:
   |  (line 2)   |  (line 1)   |  coordinate |   one line runs down its own column
   |             |             |  0 is here  |
   +-------------+-------------+-------------+
                               ^
                               the content box' right edge
```

### How long a line is

A line's length is bounded by `MaxHeight`, and `MaxWidth` bounds how far the columns may run: the same two numbers as
in horizontal writing, with their roles exchanged. Breaking, prohibition, squeezing and justification all measure
along the inline axis, so nothing about the decisions changes — only the direction the resulting lengths run in.

### Boxes, baselines and sizes

A laid-out box is described along the two axes rather than in X/Y, and in vertical writing that shows up in two
fields:

- `Size` is the box measured **block first**: `Size.X` is the extent across the column and `Size.Y` how far down it
  runs. The box starts at `Position` and extends along the inline unit and then the block unit, which in `VerticalRl`
  means it runs leftward — a consumer must not assume a rightward, downward rectangle when drawing or hit testing it.
- `BaselineY` stays a coordinate on the **block** axis (`Block(Position) + ascent`), which in horizontal writing is
  `Position.Y + ascent` and in vertical writing is the column's own coordinate, not a Y. A column has no horizontal
  baseline to place, so a renderer drawing one must not read this field as a position down the page; the pen for a
  column comes from the element's inline start and the glyphs' own offsets instead.

### Line height and line pitch

Line geometry stays a scalar along the block axis: the line's `Y` is its coordinate on that axis, `Height` is
`ExtraAbove + Ascent + Descent + ExtraBelow + LineSpacing`, and the next line starts one `Height` further along the
block axis, i.e. to the left. `Ascent` and `Descent` are the maxima over the line's elements along that axis, so
everything that keeps two horizontal lines apart keeps two columns apart for the same reason: a line grows for what it
carries, and the room an annotation band, a mark or an underline needs shows up as the column's thickness.

### Latin runs and numbers are turned

Text that has a direction of its own keeps it: a Latin run — a word or a number, since Latin letters, digits and other
Western characters are one class — is shaped the way it reads, along its own direction with its own metrics, and then
turned a quarter turn clockwise so it reads on down the column when the head is tilted. The run reports it as
`GlyphRun.Rotation = ClockwiseQuarter`; its glyphs still advance down the column, one after the next, and each one is
drawn turned about its own pen.

Turning is what keeps a word a word and a number a number. Shaped the way a column runs, a run advances by its em box,
which would space the letters out to full-width cells and push the word out of its column; shaped along its own
direction, it advances by the widths the letters actually need. Characters that are set upright in vertical writing —
ideographs, kana — carry `Rotation = None` and are shaped down the column.

### Annotations and emphasis marks

An annotation and an emphasis mark go on the column's block-start side, which for right-to-left columns is the
**right** of the column (clreq §5.3.1, jlreq §3.3.9). The room is still the layout's: an annotation band is reserved
on the block-start side, so it thickens the column rather than pushing the neighbouring column aside at the last
moment.

Two things about them do not follow the writing mode at all. An annotation's own direction is its own: `RubyOrientation`
says whether the annotation runs along the base text or down a column of its own, and a Bopomofo annotation is a
column beside its base character even in a horizontal paragraph (clreq §5.5.3.1). And the side an emphasis mark goes on
is chosen by the language within the mode, so a mark that sits below the characters in a horizontal Chinese paragraph
sits to the right of the column here.

### The head of a column

A line's start gives way for an annotation that would otherwise leave the area. When the annotation of the run that
opens a line is wider than the run itself, the line's content moves in by the shortfall, so the annotation's own start
lines up with the head of the line and nothing is drawn outside it (jlreq §3.3.9). The room is recorded as the line's
indent, which is an inline-axis quantity — in vertical writing it is therefore the head of the *column* that gives way,
and the amount is a distance down the column. A line that is already indented keeps its position: its first character
already stands far enough in for the annotation to fit.

## Writing a canvas host for columns

A consumer of the result needs three things, and none of them is a font metric.

- **What is where.** `Lines` carries the lines with their block-axis geometry and the intervals each one offers;
  `Elements` carries the same elements flattened in drawing order, each with `Position`, `Size` and its own glyph run.
- **The pen down a column.** Start at the element's inline start, which is the top of its box, and walk the glyphs:
  place each one at `pen - OffsetY` and advance the pen by its `Advance`. No ascent is added — in this direction the
  shaper reports the offset as a distance from the glyph's vertical origin, negated — and adding a line-box ascent is
  exactly how drawn text ends up half a character above the box the layout returned.
- **The position across the column.** The glyphs are centred on the column's thickness, and a glyph's `OffsetX` is the
  only sideways movement it has. A run that was turned grows toward +X from its pen, so a rotated run's pen stands back
  from the centre line by roughly `0.375 * FontSize` to keep the turned glyphs inside their column.

Everything else the layout decided is drawn from its own geometry: an annotation, an emphasis mark and a hyphen each
come with their own glyphs and position, a decoration runs down the column rather than across the page, and a hanging
mark hangs past the line's end edge, which is the bottom of the column. `ContentSize` is built from content-space X/Y,
so a scroll region sized from it is not yet an (inline, block) extent in vertical writing.

```csharp compile
// A canvas host: one element of a vertical layout, drawn from the geometry the layout returned.
static void DrawColumn(in LayoutElement element, Action<Glyph, float, float> drawGlyph)
{
    if (element.GlyphRun is not { } run)
        return;

    // Size.X is the column's thickness and Size.Y how far down it runs, so this is the column's centre line.
    float column = element.Position.X - (element.Size.X * 0.5f);
    float pen = element.Position.Y;

    // A turned glyph grows toward +X from the pen, so a rotated run's pen stands back from the centre line.
    if (run.Rotation == GlyphRotation.ClockwiseQuarter)
        column -= run.FontSize * 0.375f;

    foreach (Glyph glyph in run.Glyphs)
    {
        drawGlyph(glyph, column + glyph.OffsetX, pen - glyph.OffsetY);
        pen += glyph.Advance;
    }
}
```

## Rules the two modes share

Vertical writing is the same engine along a different axis, and that is deliberate: a rule that was written for a
horizontal line keeps working in a column, because a rule is never about X or Y.

- **Line breaking and prohibition** — the same break search over the same boundaries: the Unicode line breaking
  algorithm (UAX #14) as the baseline, and the same prohibition class sets on top of it (clreq, jlreq, klreq). A
  cluster that may not start a line may not start a column either.
- **Spacing, squeezing and stretching** — the same ranges on the same boundaries. The lower and upper bounds of a gap
  are lengths along the inline axis, so they become distances down the column without being re-derived; justification,
  the indents and the tab stops work the same way.
- **Hyphenation, annotations and marks** — the same language parameters decide whether a word may be hyphenated, where
  an annotation sits, which way it reads and which side an emphasis mark goes on. The mode decides where that geometry
  lands in content space, not what the rules are.
- **The output contract** — the same fields with the two axis-flavoured readings this document described: a box size
  whose components are block and inline, and a baseline that is a block coordinate.

What the engine does not do is invent a vertical-only rule where a convention states one: a column is laid out with the
same rule set a line is, applied along the axis, and a rule a convention states separately for vertical writing is not
claimed here.
