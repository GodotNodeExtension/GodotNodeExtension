**English** | [中文](rendering.cn.md)

# Rendering a layout

The layout hands you finished geometry: a flat stream of elements in drawing order, each carrying the shaped
glyphs to draw and the position they were measured at. Rendering is therefore a walk over that stream —
**you never measure again, and you never shape again**. What every field means is in
[output-format.md](output-format.md); this document is how to *use* them to draw, hit-test and debug.

SkiaSharp is used for the examples because it can draw a glyph from a face at a position, but nothing in the
output is Skia-specific: any drawing API that can place glyphs works, and the rules below are about geometry,
not about a library.

## What you receive

- `Elements` — the stream to draw, already ordered and already positioned. One source element normally
  becomes many elements, one per cluster.
- `Lines` — the same elements grouped into lines, with the line-level geometry. Use it for line boxes, hit
  testing and paragraph grouping; the elements it holds are the same objects as in `Elements`.
- `ContentSize` — the bounding box of the content, to size a scroll region.
- `LayoutLine.Spans` and the element's `SpanIndex` — which interval of the line the element sits in.

## Coordinates and axes

- The **content origin** is the top-left corner of the content box, i.e. after the request's `Padding`. X
  grows to the right and Y grows down in every writing mode, and everything in the output is relative to
  that origin; your own padding, scroll offset and zoom go on top of it.
- **Inline and block are two axes, and which Godot axis carries which depends on the writing mode.** Inline
  is `+X` in horizontal writing and `+Y` in vertical writing; block is `+Y` horizontally, and `-X` for
  `VerticalRl` (columns advance leftward). `LayoutAxes` turns a content-space point or size into inline and
  block scalars, so renderer code rarely has to ask which mode it is in:

```csharp
LayoutAxes axes = new(WritingMode.VerticalRl, BlockExtentLimit: 800f);

float inlineStart = axes.Inline(element.Position);          // where the box starts along the column
float blockStart = axes.Block(element.Position);            // which column the box sits on
float blockExtent = axes.BlockExtent(element.Size);         // how thick the box is across it
```

- `Position` is the corner the box **starts** at, and the box extends along the inline unit first, then along
  the block unit. In a right-to-left column that means it runs **leftward**: never draw or hit-test it as a
  rectangle that grows right and down.
- `BaselineY` is a **block-axis** coordinate, `Block(Position) + ascent`: the Y of the baseline in horizontal
  writing, the column's own coordinate in vertical writing. Draw a horizontal line's glyphs **on** it, and
  never derive a baseline from font metrics yourself — that is what the field replaces, and a font swap
  would otherwise shift the whole line.
- `GlyphRun.Origin` is the pen position of the first glyph. In horizontal writing it is a point in content
  space — start there. In vertical writing it is not a point: the pen starts at the element's **inline
  start** on the column's centre line instead (see below).
- `Glyph.Advance` is positive in both writing modes, so advancing the pen by it always moves the pen along
  the run's own reading direction.
- `GlyphRun.Rotation` says whether the run is turned: `None` draws the glyphs upright with the line or
  column, `ClockwiseQuarter` turns each glyph a quarter turn clockwise about the pen — that is how a Latin
  word or a number is set in a column.

## Drawing a glyph run

The whole contract in one sentence: start the pen at the run's origin, walk the glyphs, add `Advance` each
time, and apply the glyph's own `OffsetX`/`OffsetY` before drawing it. Grouping the glyphs by `FontId` keeps
the number of draw calls down, because a run is not necessarily one face.

### Horizontal writing

```csharp
using GodotNodeExtension.Component.Typography.Core;
using GodotNodeExtension.Component.Typography.Core.Model;
using SkiaSharp;

// One laid-out text element: draw the glyphs it carries, in the order it carries them.
static void DrawRun(SKCanvas canvas, in GlyphRun run, SKPaint paint)
{
    float baseline = run.Origin.Y;   // the pen on the baseline of the first glyph
    float pen = run.Origin.X;

    var builder = new SKTextBlobBuilder();
    int index = 0;

    while (index < run.Glyphs.Length)
    {
        // One positioned run per face: shaping fallback can mix faces inside one glyph run.
        ulong fontId = run.Glyphs[index].FontId;
        int start = index;
        while (index < run.Glyphs.Length && run.Glyphs[index].FontId == fontId)
            index++;

        int count = index - start;
        var glyphs = new ushort[count];
        var positions = new SKPoint[count];
        float x = pen;

        for (int i = 0; i < count; i++)
        {
            Glyph glyph = run.Glyphs[start + i];
            positions[i] = new SKPoint(x + glyph.OffsetX, baseline + glyph.OffsetY);

            // Drawing APIs are usually 16-bit: a glyph index beyond that still advances the pen.
            glyphs[i] = glyph.Id <= ushort.MaxValue ? (ushort)glyph.Id : (ushort)0;
            x += glyph.Advance;
        }

        if (FontCatalog.Shared.TryGetTypeface(fontId, out SKTypeface typeface) && typeface is not null)
        {
            using var font = new SKFont(typeface, run.FontSize);
            builder.AddPositionedRun(glyphs, font, positions);
        }

        pen = x;
    }

    SKTextBlob blob = builder.Build();
    if (blob is not null)
        canvas.DrawText(blob, 0f, 0f, paint);
}
```

`DisplayText`, when it is set, is the text the layout measured and substituted (quotation marks, ellipsis,
mirrored brackets) — the glyph run already reflects it, so there is nothing left to do about it. Where
there is no glyph run at all (a text effect, a syntax-highlighted span drawn from a string), draw
`DisplayText ?? Text` with your own text API; that path is the exception, not the rule.

### Columns

In vertical writing the pen runs **down** the column: `Advance` moves it along +Y, `OffsetX` is the only
sideways movement a glyph has, and `OffsetY` is a distance from the glyph's vertical origin that the shaper
reports negated — so the drawable baseline is `pen - OffsetY`, not `pen + OffsetY`. The column sits at the
element's block-start edge and is as thick as the box: its centre line is half the block extent inside it,
and the pen starts at the element's inline start, i.e. the top of the box, with **no** font metric added.

```csharp
using GodotNodeExtension.Component.Typography.Core;
using GodotNodeExtension.Component.Typography.Core.Model;
using SkiaSharp;

// One laid-out text element of a vertical layout.
static void DrawColumn(SKCanvas canvas, LayoutElement element, in GlyphRun run, SKPaint paint)
{
    // The box grows leftward from its position, so the centre line is half its block extent inside it.
    float centre = element.Position.X - (element.Size.X * 0.5f);
    float pen = element.Position.Y;   // the element's own inline start: no ascent is added here

    using var builder = new SKTextBlobBuilder();
    int index = 0;

    while (index < run.Glyphs.Length)
    {
        ulong fontId = run.Glyphs[index].FontId;
        int start = index;
        while (index < run.Glyphs.Length && run.Glyphs[index].FontId == fontId)
            index++;

        int count = index - start;
        var glyphs = new ushort[count];
        var positions = new SKPoint[count];
        float y = pen;

        for (int i = 0; i < count; i++)
        {
            Glyph glyph = run.Glyphs[start + i];
            positions[i] = new SKPoint(centre + glyph.OffsetX, y - glyph.OffsetY);
            glyphs[i] = glyph.Id <= ushort.MaxValue ? (ushort)glyph.Id : (ushort)0;
            y += glyph.Advance;
        }

        if (FontCatalog.Shared.TryGetTypeface(fontId, out SKTypeface typeface) && typeface is not null)
        {
            using var font = new SKFont(typeface, run.FontSize);
            builder.AddPositionedRun(glyphs, font, positions);
        }

        pen = y;
    }

    SKTextBlob blob = builder.Build();
    if (blob is not null)
        canvas.DrawText(blob, 0f, 0f, paint);
}
```

### A turned run

A run with `Rotation = ClockwiseQuarter` is a Latin word or a number in a column: each glyph keeps the
baseline it would have along a line, turned a quarter turn clockwise, so the run reads on when the head is
tilted. A text blob cannot turn individual glyphs, so this path draws them one by one — and because a turned
glyph grows towards +X from the pen, the pen moves back by roughly `0.375 × FontSize` so the ink stays
inside the column.

```csharp
// Inside the loop above, for a run whose Rotation is ClockwiseQuarter.
using SKPath path = font.GetGlyphPath((ushort)glyph.Id);

if (path is not null)
{
    canvas.Save();
    canvas.Translate(centre + glyph.OffsetX, pen - glyph.OffsetY);
    canvas.RotateDegrees(90f);
    canvas.DrawPath(path, paint);
    canvas.Restore();
}
```

## Element order

Draw the stream in the order it arrives. It is already sorted so that what lies behind comes first: a
background or a block decoration precedes the text it backs, and the overlays of a text element follow it.

```text
one line of the stream, in drawing order

  Rect / Line                 a merged inline background, a block's background or border
  Text, with a GlyphRun       the line's clusters: draw each one at its own origin
  Text, with a HyphenRun      the hyphen the line broke with, drawn after that element's glyphs
  Text, with Emphasis         the emphasis mark, on its own side of the character
  Text, with Ruby             the annotation, in its band or beside its base character
  Image / ExtensionRegion     the inline objects the caller owns
  Action                      nothing: a trigger point, not ink
```

Dispatch on `Type`, and treat an element that is invisible as absent: a `Rect`, `Line`, `Image` or
`ExtensionRegion` with a zero size draws nothing, an `Action` is never drawn, and a text element without a
glyph run draws nothing through the glyph path (it is the string-path exception described above).

```csharp compile
static bool NeedsDrawing(LayoutElement element) => element.Type switch
{
    DrawElement.ElementType.Action => false,                        // a trigger point, not ink
    DrawElement.ElementType.Text => element.GlyphRun is not null,   // text with no run draws nothing
    _ => element.Size.X > 0f || element.Size.Y > 0f,                // a box needs a size to be visible
};
```

Elements whose `Reason` is non-null were synthesised or expanded while the line was assembled — a merged
background, a block's decoration and marker, the hyphen of a hyphenated break, a block's flattened content.
Draw them like any other element; the reason to know about them is that they are what a debug overlay
explains itself with, and that an element with a non-null `Reason` *and* an empty `SourceRange` covers no
source text, so it must stay out of selection, copying and hit testing.

`BackgroundFillLine` is the one order-sensitive detail: that background fills the full line box rather than
the text, so draw it over `LayoutLine.Y .. Y + Height` along the block axis instead of over the element's
own box. `SyntaxSpans` overrides the element's `Color` per span when it is set, and a `Hanging` mark is
text that sticks out past the line's end edge by convention: draw it where it is and do not clamp it back
into the line.

## Annotations and emphasis marks

Both come out placed, shaped or measured, with the geometry to draw them: an annotation carries its own
glyphs, and a mark is a character with its own size and centre point. Re-deriving either from the language
would be a second, disagreeing answer.

```csharp
using GodotNodeExtension.Component.Typography.Core;
using GodotNodeExtension.Component.Typography.Core.Model;
using SkiaSharp;

// An annotation: its glyphs, walked along its own reading direction.
static void DrawAnnotation(SKCanvas canvas, in RubyAnnotation ruby, SKPaint paint)
{
    if (ruby.Glyphs is null || ruby.Glyphs.Length == 0) return;

    if (!FontCatalog.Shared.TryGetTypeface(ruby.FontId, out SKTypeface typeface) || typeface is null) return;

    using var font = new SKFont(typeface, ruby.FontSize);

    // Orientation is the annotation's own direction, independent of the writing mode: a horizontal
    // paragraph can carry a column of symbols (Bopomofo, clreq §5.5.3.1).
    bool down = ruby.Orientation == RubyOrientation.Vertical;
    float pen = down ? ruby.BaselineY : ruby.X;

    foreach (Glyph glyph in ruby.Glyphs)
    {
        SKPoint position = down
            ? new SKPoint(ruby.X + glyph.OffsetX, pen - glyph.OffsetY)          // down its own column
            : new SKPoint(pen + glyph.OffsetX, ruby.BaselineY + glyph.OffsetY); // along the line
        pen += glyph.Advance;

        using var one = new SKTextBlobBuilder();
        one.AddPositionedRun([(ushort)glyph.Id], font, [position]);
        SKTextBlob blob = one.Build();
        if (blob is not null)
            canvas.DrawText(blob, 0f, 0f, paint);
    }

    // ruby.Width is the annotation's extent along that direction and ruby.BandWidth the room the layout
    // reserved for it across that direction: centre it in that room instead of inventing a band when it is 0.
}

// An emphasis mark: one character, centred on the point the layout gave.
static void DrawEmphasis(SKCanvas canvas, in EmphasisMarkGeometry mark, SKFont markFont, SKPaint paint)
{
    float halfWidth = markFont.MeasureText(mark.Mark) * 0.5f;
    SKFontMetrics metrics = markFont.Metrics;

    // CentreY is the centre of the mark on the block axis; a drawing API takes a baseline, so the mark
    // moves up by half its own height. Mark.Side is the side of the character the mark belongs on.
    float baseline = mark.CenterY - ((metrics.Ascent + metrics.Descent) * 0.5f);

    canvas.DrawText(mark.Mark, mark.X - halfWidth, baseline, markFont, paint);
}
```

`HyphenRun` is a glyph run like any other: draw it with the same walk, from its own origin, after the
element's own glyphs. A hyphen may instead arrive as a separate element with `Reason = HyphenationBreak` —
then that element is the one drawn, so do not draw the run as well. The lines that carry these overlays are
taller for them (`LayoutLine.ExtraAbove` / `ExtraBelow` are already inside `Height`), so a line box drawn
from `LayoutLine` needs nothing added.

## Hit testing and selection

Two directions, and the output supports both.

**A point to an element.** Walk the lines, find the one whose block range contains the point, then find the
element whose box contains it — in the interval the element reports, because a line can offer more than one
and the first one is not always where the element is:

```csharp compile
static bool Contains(LayoutAxes axes, LayoutElement element, Vector2 point)
{
    // The box starts at Position and grows along the inline axis first, then along the block axis.
    float inline = axes.Inline(point - element.Position);
    float block = axes.BlockDelta(point - element.Position);

    return inline >= 0f && inline <= axes.Inline(element.Size)
        && block >= 0f && block <= axes.BlockExtent(element.Size);
}
```

```csharp compile
static float InlineStart(LayoutLine line, LayoutElement element)
{
    // The element sits in the interval it was placed in; LineLeft describes the first interval only.
    LineSpan span = line.Spans[element.SpanIndex];

    return span.Left + (element.SpanIndex == 0 ? line.LineIndent : 0f);
}
```

**An element back to text.** `SourceIndex` names the source element the layout piece came from, and
`SourceRange` is a **half-open** `[Start, End)` range of UTF-16 code units relative to *that* element's
text — not document-global, and not a character count: a length of *N* may cover fewer visible characters
than *N*. `ClusterStart`/`ClusterEnd` are the cluster-level mapping and are what a per-glyph question
(caret position, which glyph covers this character) should use, because shaping merges several characters
into one glyph and splits one character into several. `CharacterIndex`/`CharacterCount` are the global,
monotonic coordinates for selection and typewriter playback: reveal a prefix of the text by comparing
`CharacterCount` with `Glyph.ClusterStart`.

```csharp compile
static TextRange UnionOfOneSourceElement(LayoutLine line)
{
    int start = int.MaxValue;
    int end = 0;

    foreach (LayoutElement element in line.Elements)
    {
        // Synthesised pieces (an inserted hyphen, a decoration) cover no source text of their own.
        if (element.Reason is not null || element.SourceRange.IsEmpty) continue;

        start = Math.Min(start, element.SourceRange.Start);
        end = Math.Max(end, element.SourceRange.End);
    }

    return start == int.MaxValue ? TextRange.Empty : new TextRange(start, end);
}
```

Two rules keep the mapping honest: an element order is a *drawing* order (right-to-left text and mixed
content are put in visual order), so never map text through the position of an element in `Elements`; and
an element carrying a non-null `Reason` with an empty `SourceRange` has no text of its own, so it is not
part of any selection.

## Debug overlays

A layout that is wrong is almost always a geometry question, and the output answers it directly — every
element says where it is, which line it is on and why. The overlays worth having, in the order they pay off:

```text
line boxes        LayoutLine.Y .. Y + Height, along the block axis
baselines         BaselineY of every element that has one, and a column's centre line across it
element boxes     Position, then Size along the inline axis and the block axis
line intervals    Spans, plus LineLeft / LineRight / LineIndent for the first one
labels            CharClass, SpanIndex, LineIndex and Reason on each box
counters          LineCount, ProhibitedBreakSkips and Timings for the frame
```

The counters are the cheapest part and explain the most: `ProhibitedBreakSkips` says a line ended earlier
than its width would have allowed because a prohibition refused the break, and `Timings.CompileMs` says
whether a relayout actually reused the prepared content (`0` means it did).

## Must and must not

**Must**

- Draw the glyphs a `GlyphRun` carries, in order, advancing the pen by `Advance` and applying
  `OffsetX`/`OffsetY`.
- Place a horizontal line's glyphs on `BaselineY`, and take a column's pen from the element's inline start
  on the column's centre line.
- Resolve a glyph's face through its `FontId` and batch by it: one run can mix faces.
- Draw in `Elements` order, and draw every element — including the ones with a `Reason`.
- Draw the annotation, the emphasis mark and the hyphen from their own geometry.
- Map layout back to text through `SourceIndex`, `SourceRange` and the cluster indices.
- Use `Spans` and the element's `SpanIndex` when a line offers more than one interval.
- Draw a `Hanging` mark outside the line box, and reserve nothing for an overlay: the line box already
  grew for it.

**Must not**

- Shape the text again, or measure it again. Once a glyph run is present, `Text` is informational.
- Read `BaselineY` as a Y in vertical writing: it is a block-axis coordinate.
- Add a font ascent to `Position.Y` to build a baseline, or a font metric to a column's pen.
- Re-classify characters — `CharClass` is the classification the language rule produced.
- Put an element with a non-null `Reason` and an empty `SourceRange` into selection or hit testing.
- Assume one element per character, or one glyph per element.
- Assume every element has a baseline: `NaN` means the layout never computed one, and the font metrics are
  the fallback.
- Draw `Text` where `DisplayText` is set, or invent a band for an annotation whose `BandWidth` is `0`.
